using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using ArcGIS.Core.Geometry;
using ArcGIS.Core.CIM;
using ArcGIS.Desktop.Core;
using ArcGIS.Desktop.Framework;
using ArcGIS.Desktop.Core.Geoprocessing;
using ArcGIS.Desktop.Framework.Threading.Tasks;
using ArcGIS.Desktop.Mapping;
namespace ProCodex;
internal sealed class ProGeoprocessing {
    readonly Dictionary<string,MapMember> members=new();
    readonly Dictionary<string,string> identities=new();
    public List<GpInput> Inputs {get;}=new();
    string target="";
    public string? LastRun {get;private set;}
    public MapMember Member(string id)=>members.TryGetValue(id,out var m)?m:throw new InvalidOperationException("图层ID不存在，请重新读取分析上下文。");
    static bool Meters(int wkid){var sr=SpatialReferenceBuilder.CreateSpatialReference(wkid);return !sr.IsGeographic&&Math.Abs(sr.Unit.ConversionFactor-1)<1e-8;}
    static string Identity(MapMember member) {
        string definition=member is FeatureLayer f?f.GetDefinition().ToJson():member is RasterLayer r?r.GetDefinition().ToJson():member is StandaloneTable t?t.GetDefinition().ToJson():"";
        using var selection=member is FeatureLayer fs?fs.GetSelection():member is StandaloneTable ts?ts.GetSelection():null;
        string ids=selection==null?"":string.Join(",",selection.GetObjectIDs().OrderBy(id=>id));
        return Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes((member.GetDataConnection()?.ToJson()??member.URI)+definition+ids)));
    }
    public Task BindAsync()=>QueuedTask.Run(()=>{
        var map=MapView.Active?.Map??throw new InvalidOperationException("请打开二维地图。");target=map.URI;members.Clear();identities.Clear();Inputs.Clear();
        foreach(var layer in map.GetLayersAsFlattenedList().Where(l=>l is FeatureLayer||l is RasterLayer).Take(200)) {
            var item=new GpInput{Id="L"+(Inputs.Count+1),Name=layer.Name,LayerUri=layer.URI,Visible=layer.IsVisible};
            if(layer is FeatureLayer f){using var fc=f.GetFeatureClass();using var def=fc.GetDefinition();var sr=def.GetSpatialReference();item.Kind="vector";item.Shape=def.GetShapeType().ToString();item.Fields=def.GetFields().Select(x=>x.Name).ToArray();item.Wkid=sr.Wkid;item.ProjectedMeters=!sr.IsGeographic&&Math.Abs(sr.Unit.ConversionFactor-1)<1e-8;}
            else if(layer is RasterLayer r){using var raster=r.GetRaster();var sr=raster.GetSpatialReference();var cell=raster.GetMeanCellSize();item.Kind="raster";item.Wkid=sr.Wkid;item.ProjectedMeters=!sr.IsGeographic&&Math.Abs(sr.Unit.ConversionFactor-1)<1e-8;item.CellSizeX=cell.Item1;item.CellSizeY=cell.Item2;}
            Inputs.Add(item);members.Add(item.Id,layer);identities.Add(item.Id,Identity(layer));
        }
        foreach(var table in map.GetStandaloneTablesAsFlattenedList().Take(10)){using var t=table.GetTable();using var def=t.GetDefinition();var item=new GpInput{Id="L"+(Inputs.Count+1),Name=table.Name,LayerUri=table.URI,Kind="table",Fields=def.GetFields().Select(f=>f.Name).ToArray()};Inputs.Add(item);members.Add(item.Id,table);identities.Add(item.Id,Identity(table));}
        if(Inputs.Count==0)throw new InvalidOperationException("地图中没有可分析的要素、栅格或表，请先添加数据。");
    });
    public string Context=>"已绑定当前地图输入，仅图层名称、字段、坐标系和像元大小，不含属性值。图层选择和定义查询会影响实际输入。输入："+JsonSerializer.Serialize(Inputs,Json.Options)+"。受控工具目录："+GpCatalog.Describe();
    public Task ValidateAsync(GeoprocessingPlan plan)=>QueuedTask.Run(()=>{plan.ValidateInputs(Inputs,Meters);CheckRasterBudget(plan);if(MapView.Active?.Map.URI!=target)throw new InvalidOperationException("地图已改变，请重新读取地图。");foreach(var p in members)if(Identity(p.Value)!=identities[p.Key]||!MapView.Active.Map.GetLayersAsFlattenedList().Cast<MapMember>().Concat(MapView.Active.Map.GetStandaloneTablesAsFlattenedList()).Any(m=>m.URI==p.Value.URI))throw new InvalidOperationException("输入数据来源已改变，请重新读取地图。");});
    void CheckRasterBudget(GeoprocessingPlan plan) {
        var bounds=members.Where(p=>p.Value is Layer).ToDictionary(p=>p.Key,p=>((Layer)p.Value).QueryExtent());
        var cells=Inputs.ToDictionary(p=>p.Id,p=>p.CellSizeX);
        foreach(var step in plan.Steps) {
            if(!bounds.TryGetValue(step.Inputs[0],out var extent))continue;
            if(step.Operation.StartsWith("project_"))extent=(Envelope)GeometryEngine.Instance.Project(extent,SpatialReferenceBuilder.CreateSpatialReference((int)step.Number("wkid")));
            if(step.Operation=="buffer"&&!extent.SpatialReference.IsGeographic){double distance=step.Number("distance")/extent.SpatialReference.Unit.ConversionFactor;extent=EnvelopeBuilderEx.CreateEnvelope(extent.XMin-distance,extent.YMin-distance,extent.XMax+distance,extent.YMax+distance,extent.SpatialReference);}
            if(step.Operation is "raster_clip" or "clip" && bounds.TryGetValue(step.Inputs[1],out var clip)) {
                clip=(Envelope)GeometryEngine.Instance.Project(clip,extent.SpatialReference);
                double xmin=Math.Max(extent.XMin,clip.XMin),ymin=Math.Max(extent.YMin,clip.YMin),xmax=Math.Min(extent.XMax,clip.XMax),ymax=Math.Min(extent.YMax,clip.YMax);
                if(xmax<=xmin||ymax<=ymin)throw new InvalidOperationException("裁剪数据范围没有交集。");extent=EnvelopeBuilderEx.CreateEnvelope(xmin,ymin,xmax,ymax,extent.SpatialReference);
            }
            double cell=step.Number("cell_size",cells.GetValueOrDefault(step.Inputs[0]));bounds[step.Id]=extent;cells[step.Id]=cell;
            if(GpCatalog.Get(step.Operation).OutputKind=="raster"&&cell>0&&Math.Ceiling(extent.Width/cell)*Math.Ceiling(extent.Height/cell)>100000000)throw new InvalidOperationException(step.Id+"预计超过1亿像元，请先裁剪或增大像元；未执行分析。");
        }
    }
    public async Task<string> ExecuteAsync(GeoprocessingPlan plan,Action<string> progress,CancellationToken token) {
        await ValidateAsync(plan);
        string folder=Path.Combine(Project.Current.HomeFolderPath,"AgentAnalysis",DateTime.Now.ToString("yyyyMMdd-HHmmss")+"-"+Guid.NewGuid().ToString("N").Substring(0,6));Directory.CreateDirectory(folder);LastRun=folder;
        string log=Path.Combine(folder,"execution.log"),gdb=Path.Combine(folder,"results.gdb");
        File.WriteAllText(Path.Combine(folder,"plan.json"),plan.ToJson());File.WriteAllText(Path.Combine(folder,"inputs.json"),JsonSerializer.Serialize(Inputs,Json.Options));
        var results=new Dictionary<string,object>();var source=members.ToDictionary(p=>p.Key,p=>(object)p.Value);
        var overrides=new Dictionary<string,string>();
        var env=Geoprocessing.MakeEnvironmentArray(overwriteoutput:false,extent:"MAXOF",mask:"",snapRaster:"",outputCoordinateSystem:"",cellSize:"MAXOF",workspace:gdb,scratchWorkspace:gdb);
        async Task<IGPResult> Run(string tool,params object[] args){token.ThrowIfCancellationRequested();if(await ProDrawing.TargetAsync()!=target)throw new InvalidOperationException("活动地图已改变，停止执行。");progress("正在执行："+tool);var toolEnv=new[]{"sa.ExtractByMask","sa.Plus","sa.Minus","sa.Times","sa.Divide"}.Contains(tool)?Geoprocessing.MakeEnvironmentArray(overwriteoutput:false,extent:"MINOF",mask:"",snapRaster:"",outputCoordinateSystem:"",cellSize:"MAXOF",workspace:gdb,scratchWorkspace:gdb):env;var effective=toolEnv.Where(p=>!overrides.ContainsKey(p.Key)).Concat(overrides);var result=await Geoprocessing.ExecuteToolAsync(tool,Geoprocessing.MakeValueArray(args),effective,token,null,GPExecuteToolFlags.GPThread);File.AppendAllText(log,tool+Environment.NewLine+string.Join(Environment.NewLine,result.Messages.Select(m=>m.Text))+Environment.NewLine);if(result.IsFailed)throw new InvalidOperationException(tool+"："+string.Join("；",result.Messages.Select(m=>m.Text)));token.ThrowIfCancellationRequested();return result;}
        try {
            await Run("management.CreateFileGDB",folder,"results.gdb");
            foreach(var step in plan.Steps){await ValidateAsync(plan);var spec=GpCatalog.Get(step.Operation);string output=spec.OutputKind=="raster"?Path.Combine(folder,step.Id+"_"+step.Operation+".tif"):Path.Combine(gdb,step.Id+"_"+step.Operation);var args=step.Inputs.Select(id=>source[id]).ToArray();
                overrides.Clear();object? method=null;
                if(step.Operation=="raster_normalize"){
                    double Property(IGPResult result){if(!double.TryParse(result.ReturnValue,NumberStyles.Float,CultureInfo.InvariantCulture,out double number)||!double.IsFinite(number))throw new InvalidOperationException("无法读取有限栅格统计，未创建标准化结果。");return number;}
                    double minimum=Property(await Run("management.GetRasterProperties",args[0],"MINIMUM")),maximum=Property(await Run("management.GetRasterProperties",args[0],"MAXIMUM"));
                    if(maximum<minimum)throw new InvalidOperationException("栅格统计范围无效。");
                    string floating=Path.Combine(folder,step.Id+"_float.tif");await Run("sa.Float",args[0],floating);
                    if(maximum==minimum)await Run("sa.Times",floating,0,output);
                    else{string shifted=Path.Combine(folder,step.Id+"_shift.tif");if(step.Text("direction")=="negative")await Run("sa.Minus",maximum,floating,shifted);else await Run("sa.Minus",floating,minimum,shifted);await Run("sa.Divide",shifted,maximum-minimum,output);}
                    method=new{minimum,maximum,direction=step.Text("direction"),constantPolicy="zero",noData="preserved",method="independent_min_max"};
                }else if(step.Operation=="create_grid"){
                    var extent=await QueuedTask.Run(()=>args[0] is Layer layer?layer.QueryExtent():throw new InvalidOperationException("网格范围需当前面图层。"));
                    double cell=step.Number("cell_size");if(Math.Ceiling(extent.Width/cell)*Math.Ceiling(extent.Height/cell)>50000)throw new InvalidOperationException("预计超过5万网格，请增大网格尺寸。");
                    string Coordinate(double x,double y)=>x.ToString("R",CultureInfo.InvariantCulture)+" "+y.ToString("R",CultureInfo.InvariantCulture);
                    overrides["outputCoordinateSystem"]=extent.SpatialReference.Wkid.ToString(CultureInfo.InvariantCulture);
                    await Run(spec.Tool,output,Coordinate(extent.XMin,extent.YMin),Coordinate(extent.XMin,extent.YMin+1),cell,cell,0,0,Coordinate(extent.XMax,extent.YMax),"NO_LABELS","#","POLYGON");
                    method=new{cellSize=cell,units="meters",boundaryPolicy="extent_only_clip_to_boundary_separately"};
                }else if(step.Operation=="feature_density"){
                    await Run("management.CopyFeatures",args[0],output);
                    await Run("management.CalculateGeometryAttributes",output,"AG_AREA_KM2 AREA","#","SQUARE_KILOMETERS");
                    await Run("management.AddField",output,"AG_DENS","DOUBLE");
                    await Run("management.CalculateField",output,"AG_DENS","!"+step.Text("field")+"! / !AG_AREA_KM2! if !AG_AREA_KM2! > 0 and !"+step.Text("field")+"! is not None else None","PYTHON3");
                    method=new{countField=step.Text("field"),areaField="AG_AREA_KM2",densityField="AG_DENS",areaPolicy="actual_input_polygon_area"};
                }else if(step.Operation=="global_moran"){
                    var observations=await QueuedTask.Run(()=>{
                        if(args[0] is not FeatureLayer feature)throw new InvalidOperationException("全局置换检验需已加入地图的要素图层，请先刷新上下文。");
                        using var selection=feature.GetSelection();var ids=selection.GetObjectIDs();var query=ids.Count>0?new ArcGIS.Core.Data.QueryFilter{ObjectIDs=ids}:new ArcGIS.Core.Data.QueryFilter();
                        var rows=new List<SpatialPermutation.Observation>();using var cursor=feature.Search(query);
                        while(cursor.MoveNext()){using var row=(ArcGIS.Core.Data.Feature)cursor.Current;if(rows.Count>=5000)throw new InvalidOperationException("全局置换检验最多5000个单元，请先聚合或选择合适网格。");object value=row[step.Text("field")];if(value is null or DBNull)throw new InvalidOperationException("统计字段含空值，需明确缺失处理后重试。");var geometry=row.GetShape();var center=geometry as MapPoint??GeometryEngine.Instance.Centroid(geometry);rows.Add(new(row.GetObjectID(),center.X,center.Y,Convert.ToDouble(value,CultureInfo.InvariantCulture)));}return rows.ToArray();
                    });
                    var permutation=await Task.Run(()=>SpatialPermutation.Global(observations,(int)step.Number("neighbors"),(int)step.Number("seed")),token);
                    var result=await Run(spec.Tool,args[0],step.Text("field"),"GENERATE_REPORT","K_NEAREST_NEIGHBORS","EUCLIDEAN_DISTANCE","ROW","#","#",(int)step.Number("neighbors"));
                    output=Path.Combine(folder,step.Id+"_global_moran.json");
                    var receipt=new{operation=step.Operation,path=output,status="completed",values=result.Values,messages=result.Messages.Select(m=>m.Text).ToArray(),analyticInference="native_GP_randomization_not_permutation",permutation,weightCaution="置换检验使用明确的K近邻和OID同距规则；原生GP可能使用不同同距处理，其解析p值不可与置换I混配。",neighbors=(int)step.Number("neighbors"),standardization="ROW"};
                    File.WriteAllText(output,JsonSerializer.Serialize(receipt,Json.Options));source[step.Id]=output;results[step.Id]=receipt;File.WriteAllText(Path.Combine(folder,"results.json"),JsonSerializer.Serialize(results,Json.Options));continue;
                }else if(step.Operation=="local_moran"){
                    overrides["randomGenerator"]=((int)step.Number("seed")).ToString(CultureInfo.InvariantCulture)+" MERSENNE_TWISTER";
                    await Run(spec.Tool,GpCatalog.Arguments(step,args,output));
                    method=new{permutations=999,fdr=true,requestedSeed=(int)step.Number("seed"),neighbors=(int)step.Number("neighbors"),standardization="ROW",seedReproducibility="requires_repeat_verification"};
                }else if(step.Operation=="select_values"){
                    await Run(spec.Tool,args[0],output,GpCatalog.TextSelection(step));
                }else if(step.Operation=="select_attribute"){
                    string value=step.Text("value");if(step.Text("value_type")=="number"){if(!double.TryParse(value,NumberStyles.Float,CultureInfo.InvariantCulture,out double n)||!double.IsFinite(n))throw new InvalidOperationException("属性数值无效。");value=n.ToString("R",CultureInfo.InvariantCulture);}else value="'"+value.Replace("'","''")+"'";
                    string op=step.Text("operator") switch{"EQ"=>"=","NE"=>"<>","GT"=>">","GE"=>">=","LT"=>"<",_=>"<="};await Run(spec.Tool,args[0],output,"\""+step.Text("field")+"\" "+op+" "+value);
                }else if(step.Operation=="select_location"){
                    string work=Path.Combine(gdb,step.Id+"_selection_work");
                    await Run("management.CopyFeatures",args[0],work);
                    FeatureLayer? temporary=null;Map? temporaryMap=null;
                    try {
                        await QueuedTask.Run(()=>{temporaryMap=MapView.Active?.Map??throw new InvalidOperationException("地图已关闭。");temporary=LayerFactory.Instance.CreateLayer<FeatureLayer>(new FeatureLayerCreationParams(new Uri(work)),temporaryMap);temporary.SetName("Agent选择中间层_"+step.Id);temporary.SetVisibility(false);});
                        await Run(spec.Tool,temporary!,step.Text("relationship","INTERSECT"),args[1],"#","NEW_SELECTION");
                        await Run("management.CopyFeatures",temporary!,output);
                    }finally{if(temporary!=null&&temporaryMap!=null)try{await QueuedTask.Run(()=>temporaryMap.RemoveLayer(temporary));}catch(Exception cleanup){File.AppendAllText(log,"临时图层清理未完成："+cleanup.Message+Environment.NewLine);}}
                }else {
                    var values=GpCatalog.Arguments(step,args,output);
                    if(new[]{"intersect","union","merge"}.Contains(step.Operation))values[0]=string.Join(";",args.Select(a=>"'"+Geoprocessing.MakeValueArray(a)[0].Replace("'","''")+"'"));
                    await Run(spec.Tool,values);
                }
                source[step.Id]=output;results[step.Id]=new{operation=step.Operation,path=output,status="completed",method};File.WriteAllText(Path.Combine(folder,"results.json"),JsonSerializer.Serialize(results,Json.Options));
                await QueuedTask.Run(async()=>{var map=MapView.Active?.Map;if(map?.URI!=target)return;MapMember? added=null;if(spec.OutputKind=="raster")added=LayerFactory.Instance.CreateLayer<RasterLayer>(new RasterLayerCreationParams(new Uri(output)),map);else if(spec.OutputKind=="vector")added=LayerFactory.Instance.CreateLayer<FeatureLayer>(new FeatureLayerCreationParams(new Uri(output)),map);else added=StandaloneTableFactory.Instance.CreateStandaloneTable(new Uri(output),map);added.SetName(step.Id+" · "+spec.Label);results[step.Id]=new{operation=step.Operation,path=output,status="completed",layerUri=added.URI,method};if(added is RasterLayer raster&&step.Operation=="reclassify") {
                    var ramp=ColorFactory.Instance.ConstructColorRamp(ColorRampAlgorithm.LinearContinuous,ColorFactory.Instance.CreateRGBColor(255,255,180),ColorFactory.Instance.CreateRGBColor(180,20,20));
                    raster.SetColorizer(await raster.CreateColorizerAsync(new UniqueValueColorizerDefinition("Value",ramp)));
                }
                if(step.Operation=="local_moran"&&added is FeatureLayer local){
                    using var fc=local.GetFeatureClass();using var definition=fc.GetDefinition();string shape=definition.GetShapeType().ToString();
                    CIMSymbolReference Symbol(int red,int green,int blue){var color=ColorFactory.Instance.CreateRGBColor(red,green,blue);return shape=="Point"?SymbolFactory.Instance.ConstructPointSymbol(color,5,SimpleMarkerStyle.Circle).MakeSymbolReference():shape=="Polyline"?SymbolFactory.Instance.ConstructLineSymbol(color,1,SimpleLineStyle.Solid).MakeSymbolReference():SymbolFactory.Instance.ConstructPolygonSymbol(color).MakeSymbolReference();}
                    var classes=new[]{("HH",180,30,40),("LL",35,90,180),("HL",240,150,150),("LH",140,195,235)}.Select(c=>new CIMUniqueValueClass{Label=c.Item1,Values=new[]{new CIMUniqueValue{FieldValues=new[]{c.Item1}}},Symbol=Symbol(c.Item2,c.Item3,c.Item4),Visible=true}).ToArray();
                    local.SetRenderer(new CIMUniqueValueRenderer{Fields=new[]{"COType"},Groups=new[]{new CIMUniqueValueGroup{Heading="LISA (999 permutations, FDR)",Classes=classes}},UseDefaultSymbol=true,DefaultLabel="Not significant / unclassified",DefaultSymbol=Symbol(220,220,220)});
                }
                if(added is Layer layer){layer.SetVisibility(step==plan.Steps.Last());if(step==plan.Steps.Last())MapView.Active?.ZoomTo(layer,false,TimeSpan.Zero,true);}});
                File.WriteAllText(Path.Combine(folder,"results.json"),JsonSerializer.Serialize(results,Json.Options));
            }
            if(!await FrameworkApplication.Current.Dispatcher.InvokeAsync(()=>Project.Current.SaveAsync()).Task.Unwrap())throw new InvalidOperationException("处理结果已生成，但工程未保存。");File.WriteAllText(Path.Combine(folder,"status.json"),JsonSerializer.Serialize(new{status="completed",steps=results.Count},Json.Options));return "分析完成："+results.Count+" 步。结果及日志："+folder;
        }catch(Exception ex){File.WriteAllText(Path.Combine(folder,"failure-details.txt"),ex.ToString());File.WriteAllText(Path.Combine(folder,"status.json"),JsonSerializer.Serialize(new{status=token.IsCancellationRequested?"cancelled":"failed",completedSteps=results.Count,error=ex.Message},Json.Options));throw new InvalidOperationException(ex.Message+"。已有输出保留："+folder,ex);}
    }
}

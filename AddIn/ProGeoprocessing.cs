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
        var env=Geoprocessing.MakeEnvironmentArray(overwriteoutput:false,extent:"MAXOF",mask:"",snapRaster:"",outputCoordinateSystem:"",cellSize:"MAXOF",workspace:gdb,scratchWorkspace:gdb);
        async Task<IGPResult> Run(string tool,params object[] args){token.ThrowIfCancellationRequested();if(await ProDrawing.TargetAsync()!=target)throw new InvalidOperationException("活动地图已改变，停止执行。");progress("正在执行："+tool);var toolEnv=new[]{"sa.ExtractByMask","sa.Plus","sa.Minus","sa.Times","sa.Divide"}.Contains(tool)?Geoprocessing.MakeEnvironmentArray(overwriteoutput:false,extent:"MINOF",mask:"",snapRaster:"",outputCoordinateSystem:"",cellSize:"MAXOF",workspace:gdb,scratchWorkspace:gdb):env;var result=await Geoprocessing.ExecuteToolAsync(tool,Geoprocessing.MakeValueArray(args),toolEnv,token,null,GPExecuteToolFlags.GPThread);File.AppendAllText(log,tool+Environment.NewLine+string.Join(Environment.NewLine,result.Messages.Select(m=>m.Text))+Environment.NewLine);if(result.IsFailed)throw new InvalidOperationException(tool+"："+string.Join("；",result.Messages.Select(m=>m.Text)));token.ThrowIfCancellationRequested();return result;}
        try {
            await Run("management.CreateFileGDB",folder,"results.gdb");
            foreach(var step in plan.Steps){await ValidateAsync(plan);var spec=GpCatalog.Get(step.Operation);string output=spec.OutputKind=="raster"?Path.Combine(folder,step.Id+"_"+step.Operation+".tif"):Path.Combine(gdb,step.Id+"_"+step.Operation);var args=step.Inputs.Select(id=>source[id]).ToArray();
                if(step.Operation=="select_attribute"){
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
                source[step.Id]=output;results[step.Id]=new{operation=step.Operation,path=output,status="completed"};File.WriteAllText(Path.Combine(folder,"results.json"),JsonSerializer.Serialize(results,Json.Options));
                await QueuedTask.Run(async()=>{var map=MapView.Active?.Map;if(map?.URI!=target)return;MapMember? added=null;if(spec.OutputKind=="raster")added=LayerFactory.Instance.CreateLayer<RasterLayer>(new RasterLayerCreationParams(new Uri(output)),map);else if(spec.OutputKind=="vector")added=LayerFactory.Instance.CreateLayer<FeatureLayer>(new FeatureLayerCreationParams(new Uri(output)),map);else added=StandaloneTableFactory.Instance.CreateStandaloneTable(new Uri(output),map);added.SetName(step.Id+" · "+spec.Label);results[step.Id]=new{operation=step.Operation,path=output,status="completed",layerUri=added.URI};if(added is RasterLayer raster&&step.Operation=="reclassify") {
                    var ramp=ColorFactory.Instance.ConstructColorRamp(ColorRampAlgorithm.LinearContinuous,ColorFactory.Instance.CreateRGBColor(255,255,180),ColorFactory.Instance.CreateRGBColor(180,20,20));
                    raster.SetColorizer(await raster.CreateColorizerAsync(new UniqueValueColorizerDefinition("Value",ramp)));
                }
                if(added is Layer layer){layer.SetVisibility(step==plan.Steps.Last());if(step==plan.Steps.Last())MapView.Active?.ZoomTo(layer,false,TimeSpan.Zero,true);}});
                File.WriteAllText(Path.Combine(folder,"results.json"),JsonSerializer.Serialize(results,Json.Options));
            }
            if(!await FrameworkApplication.Current.Dispatcher.InvokeAsync(()=>Project.Current.SaveAsync()).Task.Unwrap())throw new InvalidOperationException("处理结果已生成，但工程未保存。");File.WriteAllText(Path.Combine(folder,"status.json"),JsonSerializer.Serialize(new{status="completed",steps=results.Count},Json.Options));return "分析完成："+results.Count+" 步。结果及日志："+folder;
        }catch(Exception ex){File.WriteAllText(Path.Combine(folder,"failure-details.txt"),ex.ToString());File.WriteAllText(Path.Combine(folder,"status.json"),JsonSerializer.Serialize(new{status=token.IsCancellationRequested?"cancelled":"failed",completedSteps=results.Count,error=ex.Message},Json.Options));throw new InvalidOperationException(ex.Message+"。已有输出保留："+folder,ex);}
    }
}

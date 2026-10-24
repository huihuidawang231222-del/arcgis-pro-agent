using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using ArcGIS.Desktop.Framework.Threading.Tasks;
using ArcGIS.Desktop.Mapping;
using ArcGIS.Core.CIM;
using ArcGIS.Desktop.Core;
using ArcGIS.Desktop.Framework;
using ArcGIS.Desktop.Core.Geoprocessing;
namespace ProCodex;
internal sealed class AnalysisBridge : IDisposable {
    readonly ProGeoprocessing engine=new();readonly Dictionary<string,Job> jobs=new();readonly object sync=new();
    string snapshot="";Job? active;
    sealed class Job {public string Id="",State="queued",Progress="",Error="",Request="",Canonical="";public object? Result;public CancellationTokenSource Cancel=new(TimeSpan.FromMinutes(15));}
    void Idle(){lock(sync)if(active!=null)throw new InvalidOperationException("分析正在运行，请查询任务或取消后再操作地图。");}
    public void RequireIdle()=>Idle();
    public async Task<object> Presentation(string value,string id,string name,string? color,double width,string? labelField,int? order){Idle();Snapshot(value);
        if(string.IsNullOrWhiteSpace(name)||name.Length>60||name.Contains('<')||name.Contains('>'))throw new InvalidOperationException("图层名称无效。");
        if(!double.IsFinite(width)||width<0.1||width>8)throw new InvalidOperationException("线宽须0.1–8。");
        if(color!=null&&!System.Text.RegularExpressions.Regex.IsMatch(color,"^#[0-9a-fA-F]{6}$"))throw new InvalidOperationException("颜色须#RRGGBB。");
        await QueuedTask.Run(()=>{
            if(engine.Member(id) is not Layer layer)throw new InvalidOperationException("只能设置地图图层。");
            if(labelField!=null&&(layer is not FeatureLayer||!engine.Inputs.Single(x=>x.Id==id).Fields.Contains(labelField)))throw new InvalidOperationException("标注字段不存在。");
            if(color!=null){if(layer is not FeatureLayer feature)throw new InvalidOperationException("颜色设置仅支持矢量图层。");
                var c=ColorFactory.Instance.CreateRGBColor(Convert.ToInt32(color.Substring(1,2),16),Convert.ToInt32(color.Substring(3,2),16),Convert.ToInt32(color.Substring(5,2),16));
                if(engine.Inputs.Single(x=>x.Id==id).Shape!="Polyline")throw new InvalidOperationException("当前矢量符号支持线图层。");
                feature.SetRenderer(new CIMSimpleRenderer{Symbol=SymbolFactory.Instance.ConstructLineSymbol(c,width,SimpleLineStyle.Solid).MakeSymbolReference()});}
            layer.SetName(name);if(order.HasValue){if(order.Value<0||order.Value>=MapView.Active.Map.Layers.Count)throw new InvalidOperationException("图层顺序超出范围。");MapView.Active.Map.MoveLayer(layer,order.Value);}
            if(labelField!=null&&layer is FeatureLayer f){var classes=f.LabelClasses.ToArray();if(classes.Length==0)throw new InvalidOperationException("此图层没有标注类。");
                f.SetLabelVisibility(false);
                foreach(var cls in classes){cls.SetExpressionEngine(LabelExpressionEngine.Arcade);cls.SetExpression("$feature["+JsonSerializer.Serialize(labelField)+"]");var text=cls.GetTextSymbol();text.Height=8;cls.SetTextSymbol(text);cls.SetLabelVisibility(true);}f.SetLabelVisibility(true);}
        });
        if(!await FrameworkApplication.Current.Dispatcher.InvokeAsync(()=>Project.Current.SaveAsync()).Task.Unwrap())throw new InvalidOperationException("工程保存失败。");snapshot="";return new{status="presentation_updated",name,labelField,order,saved=true};
    }
    public async Task<object> Context(){Idle();await engine.BindAsync();snapshot=Guid.NewGuid().ToString("N");return new{snapshotId=snapshot,inputs=engine.Inputs,operations=GpCatalog.Tools.Select(t=>new{operation=t.Operation,description=t.Label,inputKinds=t.InputKinds,minInputs=t.MinInputs,maxInputs=t.MaxInputs,outputKind=t.OutputKind,parameters=t.Rules.ToDictionary(p=>p.Key,p=>new{required=p.Value.Required,schema=p.Value.Schema()})}),limits=new{maxSteps=12,maxRasterCells=100000000,maxJobMinutes=15},instructions="输入编号仅在此snapshotId有效。每次执行后重新读取上下文获取新图层。按任务自主选择工具；单位或范围不明时询问；不要把运行成功当作数值或制图验收。"};}
    void Snapshot(string value){if(snapshot.Length==0||snapshot!=value)throw new InvalidOperationException("分析上下文已过期，请调用get_analysis_context。");}
    public async Task<object> Inspect(string value,string id){Idle();Snapshot(value);var details=await QueuedTask.Run<object>(()=>{
        var member=engine.Member(id);var meta=engine.Inputs.Single(x=>x.Id==id);
        if(member is FeatureLayer f){using var fc=f.GetFeatureClass();using var def=fc.GetDefinition();return new{input=meta,sourceFeatureCount=fc.GetCount(),selectedCount=f.SelectionCount,countMeaning="源要素类总数；定义查询与选择集影响实际处理输入",fields=def.GetFields().Select(x=>new{name=x.Name,type=x.FieldType.ToString()}).ToArray()};}
        if(member is RasterLayer r){using var raster=r.GetRaster();return new{input=meta,width=raster.GetWidth(),height=raster.GetHeight(),pixelType=raster.GetPixelType().ToString(),extent=new{xmin=raster.GetExtent().XMin,ymin=raster.GetExtent().YMin,xmax=raster.GetExtent().XMax,ymax=raster.GetExtent().YMax},note="未读取像元值或推断高程单位。"};}
        if(member is StandaloneTable table){using var data=table.GetTable();return new{input=meta,sourceRowCount=data.GetCount()};}
        throw new InvalidOperationException("不支持检查此图层。");
    });
        var rasterLayer=await QueuedTask.Run(()=>engine.Member(id) as RasterLayer);if(rasterLayer==null)return details;
        var statistics=new Dictionary<string,string>();var unavailable=new Dictionary<string,string>();
        foreach(string property in new[]{"MINIMUM","MAXIMUM","MEAN","ALLNODATA"}) {
            var result=await Geoprocessing.ExecuteToolAsync("management.GetRasterProperties",Geoprocessing.MakeValueArray(rasterLayer,property),null,CancellationToken.None,null,GPExecuteToolFlags.GPThread);
            if(result.IsFailed)unavailable[property]=string.Join(";",result.Messages.Select(m=>m.Text));else statistics[property]=result.ReturnValue;
        }
        return new{metadata=details,band=1,statistics,unavailable,note="统计来自Pro实际返回；缺失值不作推断。"};
    }
    public async Task<object> Start(string value,string requestId,GeoprocessingPlan plan){
        if(!Guid.TryParse(requestId,out _))throw new InvalidOperationException("requestId必须为UUID；重试同一次调用请使用相同UUID。");string canonical=plan.ToJson();
        lock(sync){if(jobs.TryGetValue(requestId,out var existing)){if(existing.Canonical!=canonical)throw new InvalidOperationException("同一requestId不可提交不同分析。");return View(existing);}if(jobs.Count>=100)throw new InvalidOperationException("本会话任务记录已满，请重启桥接。");}
        Idle();Snapshot(value);await engine.ValidateAsync(plan);
        var job=new Job{Id=requestId,Request=requestId,Canonical=canonical};lock(sync){jobs.Add(requestId,job);active=job;}
        _=Run(job,plan);return View(job);
    }
    async Task Run(Job job,GeoprocessingPlan plan){try{
        lock(sync)job.State="running";
        await engine.ExecuteAsync(plan,text=>{lock(sync)job.Progress=text;},job.Cancel.Token);
        object result=new{runDirectory=engine.LastRun,outputs=JsonSerializer.Deserialize<JsonElement>(File.ReadAllText(Path.Combine(engine.LastRun!,"results.json")))};
        lock(sync){job.Result=result;job.State="completed";}
    }catch(Exception ex){lock(sync){job.Error=ex.Message;job.State=job.Cancel.IsCancellationRequested?"cancelled":"failed";}}
    finally{lock(sync){active=null;snapshot="";job.Cancel.Dispose();}}}
    object View(Job job){lock(sync)return new{jobId=job.Id,status=job.State,progress=job.Progress,error=job.Error,result=job.Result,terminal=job.State is "completed" or "failed" or "cancelled"};}
    public object Status(string id){lock(sync)return jobs.TryGetValue(id,out var job)?View(job):throw new InvalidOperationException("任务不存在。");}
    public object Cancel(string id){lock(sync){if(!jobs.TryGetValue(id,out var job))throw new InvalidOperationException("任务不存在。");if(job.State is "queued" or "running")job.Cancel.Cancel();return View(job);}}
    public async Task<object> Style(string value,string id,string style,double opacity){Idle();Snapshot(value);if(!double.IsFinite(opacity)||opacity<0||opacity>100)throw new InvalidOperationException("不透明度须0–100。");if(style is not ("elevation" or "grayscale" or "classes"))throw new InvalidOperationException("未知符号方案。");
        await QueuedTask.Run(async()=>{if(engine.Member(id) is not RasterLayer layer)throw new InvalidOperationException("当前符号工具只支持栅格。");
            var ramp=style=="grayscale"?ColorFactory.Instance.ConstructColorRamp(ColorRampAlgorithm.LinearContinuous,ColorFactory.Instance.CreateRGBColor(0,0,0),ColorFactory.Instance.CreateRGBColor(255,255,255)):ColorFactory.Instance.ConstructColorRamp(ColorRampAlgorithm.LinearContinuous,ColorFactory.Instance.CreateRGBColor(80,140,65),ColorFactory.Instance.CreateRGBColor(245,235,200));
            if(style=="classes"){
                using var raster=layer.GetRaster();string pixel=raster.GetPixelType().ToString().ToUpperInvariant();
                if(pixel.Contains("FLOAT")||pixel.Contains("DOUBLE"))throw new InvalidOperationException("连续浮点栅格不能使用唯一值类别样式，请使用连续色带或先重分类为整数类别。");
            }
            var colorizer=style=="classes"?await layer.CreateColorizerAsync(new UniqueValueColorizerDefinition("Value",ramp)):await layer.CreateColorizerAsync(new StretchColorizerDefinition(0,RasterStretchType.MinimumMaximum,1,ramp));
            if(colorizer==null)throw new InvalidOperationException("栅格渲染器创建失败，原样式保留。");layer.SetColorizer(colorizer);
            layer.SetTransparency(100-opacity);layer.SetVisibility(true);MapView.Active?.ZoomTo(layer,false,TimeSpan.Zero,true);
        });if(!await FrameworkApplication.Current.Dispatcher.InvokeAsync(()=>Project.Current.SaveAsync()).Task.Unwrap())throw new InvalidOperationException("处理结果已生成，但工程未保存。");snapshot="";return new{status="styled",style,opacity,layerId=id,saved=true,note="已修改地图符号；像元值未修改。需重新读取上下文。"};
    }
    public async Task<object> Visibility(string value,string id,bool visible){Idle();Snapshot(value);
        await QueuedTask.Run(()=>{if(engine.Member(id) is not Layer layer)throw new InvalidOperationException("独立表没有地图可见性。");layer.SetVisibility(visible);});
        if(!await FrameworkApplication.Current.Dispatcher.InvokeAsync(()=>Project.Current.SaveAsync()).Task.Unwrap())throw new InvalidOperationException("可见性已修改，但工程未保存。");snapshot="";return new{status="visibility_updated",layerId=id,visible,saved=true};
    }
    public void Dispose(){lock(sync)foreach(var job in jobs.Values){if(job.State is "running" or "queued")job.Cancel.Cancel();}}
}

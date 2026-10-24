using System;
using System.Diagnostics;
using System.IO;
using System.IO.Pipes;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using ArcGIS.Desktop.Framework.Threading.Tasks;
using ArcGIS.Desktop.Mapping;
namespace ProCodex;
internal sealed class BridgeServer : IDisposable {
    readonly CancellationTokenSource stop=new();readonly ProDrawing drawing=new();readonly AnalysisBridge analysis=new();
    readonly string pipeName="ProCodex-"+Process.GetCurrentProcess().Id;
    readonly string endpoint=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"ArcGISProCodex","endpoints",Process.GetCurrentProcess().Id+".json");
    public void Start(){Directory.CreateDirectory(Path.GetDirectoryName(endpoint)!);File.WriteAllText(endpoint,JsonSerializer.Serialize(new{processId=Process.GetCurrentProcess().Id,pipe=pipeName,version="0.4.0",buildId=typeof(BridgeServer).Assembly.ManifestModule.ModuleVersionId.ToString("D")}));_=Task.Run(Loop);}
    async Task Loop(){while(!stop.IsCancellationRequested){
        try{using var pipe=new NamedPipeServerStream(pipeName,PipeDirection.InOut,1,PipeTransmissionMode.Byte,PipeOptions.Asynchronous|PipeOptions.CurrentUserOnly);
            await pipe.WaitForConnectionAsync(stop.Token);
            using var timeout=CancellationTokenSource.CreateLinkedTokenSource(stop.Token);timeout.CancelAfter(TimeSpan.FromMinutes(3));
            using var reader=new StreamReader(pipe,new UTF8Encoding(false),false,4096,true);using var writer=new StreamWriter(pipe,new UTF8Encoding(false),4096,true){AutoFlush=true};
            object response;
            try{string line=await reader.ReadLineAsync().WaitAsync(timeout.Token)??throw new IOException("空请求。");if(line.Length>200000)throw new InvalidOperationException("请求过大。");using var document=JsonDocument.Parse(line);response=new{success=true,result=await Dispatch(document.RootElement,timeout.Token)};}
            catch(Exception ex){response=new{success=false,error=ex.Message};}
            await writer.WriteLineAsync(JsonSerializer.Serialize(response,PipeJson.Options));
        }catch(OperationCanceledException){if(stop.IsCancellationRequested)return;}catch(Exception){if(stop.IsCancellationRequested)return;await Task.Delay(250,stop.Token);}
    }}
    async Task<object> Dispatch(JsonElement request,CancellationToken token){
        string action=request.GetProperty("action").GetString()??"";var args=request.GetProperty("arguments");
        if(action=="get_active_map")return await QueuedTask.Run<object>(()=>{
            var view=MapView.Active??throw new InvalidOperationException("请先在ArcGIS Pro中打开二维地图。");
            return new{processId=Process.GetCurrentProcess().Id,mapUri=view.Map.URI,name=view.Map.Name,wkid=view.Map.SpatialReference.Wkid,mapType=view.Map.MapType.ToString(),layers=view.Map.GetLayersAsFlattenedList().Take(40).Select(l=>new{name=l.Name,uri=l.URI,type=l.GetType().Name,visible=l.IsVisible}).ToArray()};
        });
        if(action=="get_analysis_job")return analysis.Status(args.GetProperty("jobId").GetString()!);
        if(action=="cancel_analysis_job")return analysis.Cancel(args.GetProperty("jobId").GetString()!);
        string target=args.GetProperty("mapUri").GetString()??"";if(string.IsNullOrWhiteSpace(target)||await ProDrawing.TargetAsync()!=target)throw new InvalidOperationException("活动地图已改变，请重新调用get_active_map。");
        token.ThrowIfCancellationRequested();
        switch(action){
            case "set_layer_presentation":return await analysis.Presentation(args.GetProperty("snapshotId").GetString()!,args.GetProperty("layerId").GetString()!,args.GetProperty("name").GetString()!,args.TryGetProperty("color",out var color)?color.GetString():null,args.TryGetProperty("width",out var width)?width.GetDouble():0.5,args.TryGetProperty("labelField",out var label)?label.GetString():null,args.TryGetProperty("order",out var order)?order.GetInt32():null);
            case "export_map":analysis.RequireIdle();return await MapDelivery.Export(args.GetProperty("title").GetString()!,args.GetProperty("sourceNote").GetString()!,args.TryGetProperty("dpi",out var dpi)?dpi.GetInt32():150,args.TryGetProperty("extentLayerUri",out var extentLayer)?extentLayer.GetString():null);
            case "set_layer_visibility":return await analysis.Visibility(args.GetProperty("snapshotId").GetString()!,args.GetProperty("layerId").GetString()!,args.GetProperty("visible").GetBoolean());
            case "get_analysis_context":return await analysis.Context();
            case "inspect_layer":return await analysis.Inspect(args.GetProperty("snapshotId").GetString()!,args.GetProperty("layerId").GetString()!);
            case "start_analysis":return await analysis.Start(args.GetProperty("snapshotId").GetString()!,args.GetProperty("requestId").GetString()!,GeoprocessingPlan.Parse(args.GetProperty("plan").GetRawText()));
            case "style_raster":return await analysis.Style(args.GetProperty("snapshotId").GetString()!,args.GetProperty("layerId").GetString()!,args.GetProperty("style").GetString()!,args.TryGetProperty("opacity",out var opacity)?opacity.GetDouble():100);
            case "draw_graphics":
                analysis.RequireIdle();var plan=DrawingPlan.Parse(args.GetProperty("plan").GetRawText());int count=await drawing.ExecuteAsync(plan,target);await drawing.FitAsync();
                return new{status="drawn",count,mapUri=target,persistence="temporary_overlay",message="已绘制并定位。临时叠加图形不会保存为业务要素。"};
            case "clear_graphics":analysis.RequireIdle();await drawing.ClearAsync();return new{status="cleared",scope="this_bridge_only"};
            case "zoom_to_graphics":await drawing.FitAsync();return new{status="zoomed",mapUri=target};
            default:throw new InvalidOperationException("未注册的地图工具。");
        }
    }
    public void Dispose(){analysis.Dispose();stop.Cancel();try{File.Delete(endpoint);}catch(IOException){}stop.Dispose();}
}

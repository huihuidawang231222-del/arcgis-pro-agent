using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using ArcGIS.Desktop.Core;
using ArcGIS.Desktop.Core.Geoprocessing;
using ArcGIS.Desktop.Framework.Threading.Tasks;
using ArcGIS.Desktop.Mapping;
namespace ProCodex;
internal sealed class MapTools {
    readonly Dictionary<string,FeatureLayer> layers=new();readonly Dictionary<string,string> fingerprints=new();
    string mapId="";public string Context {get;private set;}="尚未读取地图";
    static string Fingerprint(FeatureLayer layer){using var sel=layer.GetSelection();return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(layer.GetDefinition().ToJson()+"|"+string.Join(",",sel.GetObjectIDs().OrderBy(x=>x)))));}
    public Task ReadAsync()=>QueuedTask.Run(()=>{
        layers.Clear();fingerprints.Clear();Context="尚未读取地图";
        var map=MapView.Active?.Map??throw new InvalidOperationException("请先打开地图并添加要素图层。");mapId=map.URI;
        var summary=new List<object>();foreach(var layer in map.GetLayersAsFlattenedList().OfType<FeatureLayer>().Take(40)){
            using var fc=layer.GetFeatureClass();if(fc==null)continue;using var def=fc.GetDefinition();
            string id="L"+(layers.Count+1);layers.Add(id,layer);fingerprints.Add(id,Fingerprint(layer));
            summary.Add(new{id,name=layer.Name,shape=def.GetShapeType().ToString(),wkid=def.GetSpatialReference().Wkid,selectedCount=layer.SelectionCount});
        }
        if(layers.Count==0)throw new InvalidOperationException("当前地图没有可用要素图层。");Context=JsonSerializer.Serialize(summary);
    });
    public Task Validate(BufferPlan plan)=>QueuedTask.Run(()=>{
        var map=MapView.Active?.Map;if(map==null||map.URI!=mapId)throw new InvalidOperationException("活动地图已改变，请重新读取。");
        if(!layers.TryGetValue(plan.LayerId,out var layer)||!map.GetLayersAsFlattenedList().Any(l=>l.URI==layer.URI)||Fingerprint(layer)!=fingerprints[plan.LayerId])throw new InvalidOperationException("图层、选择集或定义查询已改变，请重新读取地图并生成计划。");
        if(!double.IsFinite(plan.DistanceMeters)||plan.DistanceMeters<=0||plan.DistanceMeters>100000)throw new InvalidOperationException("缓冲距离超出允许范围。");
    });
    public async Task<string> Buffer(BufferPlan plan,CancellationToken token){
        await Validate(plan);token.ThrowIfCancellationRequested();
        string home=Project.Current.HomeFolderPath;if(string.IsNullOrWhiteSpace(home)||!Directory.Exists(home))throw new InvalidOperationException("请先保存Pro工程。");
        string folder=Path.Combine(home,"CodexAnalysis",DateTime.Now.ToString("yyyyMMdd-HHmmss")+"-"+Guid.NewGuid().ToString("N").Substring(0,8));Directory.CreateDirectory(folder);
        File.WriteAllText(Path.Combine(folder,"plan.json"),JsonSerializer.Serialize(new{source="codex_structured_plan_user_confirmed",plan,mapMetadata=Context}));
        string gdb=Path.Combine(folder,"results.gdb"),output=Path.Combine(gdb,"buffer_result");
        async Task Run(string tool,params object[] args){token.ThrowIfCancellationRequested();await Validate(plan);
            var result=await Geoprocessing.ExecuteToolAsync(tool,Geoprocessing.MakeValueArray(args),Geoprocessing.MakeEnvironmentArray(overwriteoutput:false),token,null,GPExecuteToolFlags.GPThread);
            File.AppendAllText(Path.Combine(folder,"execution.log"),tool+Environment.NewLine+string.Join(Environment.NewLine,result.Messages.Select(m=>m.Text))+Environment.NewLine);
            if(result.IsFailed)throw new InvalidOperationException(tool+"失败，详见执行日志。");token.ThrowIfCancellationRequested();}
        try{await Run("management.CreateFileGDB",folder,"results.gdb");await Run("analysis.Buffer",layers[plan.LayerId],output,plan.DistanceMeters.ToString("R",CultureInfo.InvariantCulture)+" Meters","FULL","ROUND","NONE","#","GEODESIC");
            await Validate(plan);await QueuedTask.Run(()=>LayerFactory.Instance.CreateLayer<FeatureLayer>(new FeatureLayerCreationParams(new Uri(output)),MapView.Active.Map));
            File.WriteAllText(Path.Combine(folder,"result.json"),JsonSerializer.Serialize(new{status="completed",output,loadedIntoMap=true}));return output;
        }catch(Exception ex){File.WriteAllText(Path.Combine(folder,"failure.txt"),ex.Message);throw;}
    }
}

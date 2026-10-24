using System.Text.Json;
namespace ProCodex;
public sealed record BufferPlan(string LayerId, double DistanceMeters);
public sealed record Reply(string Message, BufferPlan? Plan) {
    public static object Schema => new { type="object", additionalProperties=false, required=new[]{"message","buffer"}, properties=new {
        message=new {type="string"}, buffer=new {anyOf=new object[]{new {type="null"},new {type="object",additionalProperties=false,required=new[]{"layerId","distanceMeters"},properties=new {layerId=new{type="string"},distanceMeters=new{type="number"}}}}}
    }};
    public static Reply Parse(string json) {
        if(json.Length>64000)throw new InvalidOperationException("回复过长。");
        using var doc=JsonDocument.Parse(json);var root=doc.RootElement;
        Exact(root,"message","buffer");string message=root.GetProperty("message").GetString()??"";
        var b=root.GetProperty("buffer");if(b.ValueKind==JsonValueKind.Null)return new(message,null);
        Exact(b,"layerId","distanceMeters");string id=b.GetProperty("layerId").GetString()??"";
        double distance=b.GetProperty("distanceMeters").GetDouble();
        if(!System.Text.RegularExpressions.Regex.IsMatch(id,"^L[1-9][0-9]{0,2}$")||!double.IsFinite(distance)||distance<=0||distance>100000)
            throw new InvalidOperationException("缓冲参数无效：需有效图层编号及0至100000米距离。");
        return new(message,new(id,distance));
    }
    static void Exact(JsonElement value,params string[] keys) {
        if(value.ValueKind!=JsonValueKind.Object)throw new InvalidOperationException("计划必须是对象。");
        var names=value.EnumerateObject().Select(p=>p.Name).ToArray();
        if(names.Length!=keys.Length||names.Distinct().Count()!=names.Length||names.Except(keys).Any())throw new InvalidOperationException("计划包含未知、缺失或重复字段。");
    }
}

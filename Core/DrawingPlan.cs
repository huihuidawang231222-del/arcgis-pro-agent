using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace ProCodex;

public sealed class Drawing {
    public string Type { get; set; } = "";
    public List<double[]> Coordinates { get; set; } = new();
    public string Color { get; set; } = "#336699";
    public double Size { get; set; } = 6;
}
public sealed class DrawingPlan {
    public string Summary { get; set; } = "";
    public List<Drawing> Drawings { get; set; } = new();
    public static DrawingPlan Parse(string json) {
        if(json.Length>200000)throw new InvalidOperationException("绘图计划过大。");
        using var doc=JsonDocument.Parse(json,new JsonDocumentOptions{MaxDepth=16});
        static void Keys(JsonElement obj,params string[] allowed) {
            if(obj.ValueKind!=JsonValueKind.Object)throw new InvalidOperationException("绘图计划需要 JSON 对象。");
            var seen=new HashSet<string>(StringComparer.Ordinal);
            foreach(var p in obj.EnumerateObject())if(!allowed.Contains(p.Name)||!seen.Add(p.Name))throw new InvalidOperationException("计划含未知或重复参数。");
            if(allowed.Any(key=>!seen.Contains(key)))throw new InvalidOperationException("计划缺少必填参数。");
        }
        Keys(doc.RootElement,"summary","drawings");
        var list=doc.RootElement.GetProperty("drawings");
        if(list.ValueKind!=JsonValueKind.Array)throw new InvalidOperationException("drawings 必须是数组。");
        foreach(var drawing in list.EnumerateArray())Keys(drawing,"type","coordinates","color","size");
        var result=JsonSerializer.Deserialize<DrawingPlan>(json,Json.Options)??throw new InvalidOperationException("空计划。");
        result.Validate();return result;
    }
    public void Validate() {
        if(Summary==null||Summary.Length>2000||Drawings==null||Drawings.Count<1||Drawings.Count>20)throw new InvalidOperationException("计划需包含 1–20 个图形及有效说明。");
        int vertices=0;
        foreach(var d in Drawings) {
            if(d==null||d.Coordinates==null||!new[]{"point","polyline","polygon"}.Contains(d.Type))throw new InvalidOperationException("仅允许点、线和面绘图。");
            if(d.Color==null||!Regex.IsMatch(d.Color,"^#[0-9a-fA-F]{6}$")||!double.IsFinite(d.Size)||d.Size<1||d.Size>30)throw new InvalidOperationException("颜色或符号大小无效（1–30 点）。");
            int count=d.Coordinates.Count;
            if((d.Type=="point"&&count!=1)||(d.Type=="polyline"&&count<2)||(d.Type=="polygon"&&count<4))throw new InvalidOperationException("图形顶点数不足或点坐标数不为 1。");
            foreach(var p in d.Coordinates) {
                if(p==null||p.Length!=2||!double.IsFinite(p[0])||!double.IsFinite(p[1])||Math.Abs(p[0])>180||Math.Abs(p[1])>90)throw new InvalidOperationException("坐标必须是明确的 WGS84 [经度,纬度]，不接受未知/投影坐标。");
                if(++vertices>5000)throw new InvalidOperationException("每个计划最多 5000 个顶点。");
            }
            if(d.Type=="polygon") {
                if(!d.Coordinates[0].SequenceEqual(d.Coordinates[count-1]))throw new InvalidOperationException("面环必须显式闭合。");
                if(d.Coordinates.Take(count-1).Select(p=>$"{p[0]:R},{p[1]:R}").Distinct().Count()<3)throw new InvalidOperationException("面至少需要三个不同顶点。");
            }
        }
    }
    public string ToJson()=>JsonSerializer.Serialize(this,Json.Options);
}
public static class Json {
    public static readonly JsonSerializerOptions Options=new() {
        PropertyNamingPolicy=JsonNamingPolicy.CamelCase,PropertyNameCaseInsensitive=false,WriteIndented=true,
        NumberHandling=JsonNumberHandling.Strict,MaxDepth=24
    };
}

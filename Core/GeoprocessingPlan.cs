using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace ProCodex;
public sealed class GpStep {
    public string Id {get;set;}="";
    public string Operation {get;set;}="";
    public List<string> Inputs {get;set;}=new();
    public Dictionary<string,JsonElement> Parameters {get;set;}=new();
    public string Text(string key,string fallback="")=>Parameters.TryGetValue(key,out var p)?p.GetString()??fallback:fallback;
    public double Number(string key,double fallback=0)=>Parameters.TryGetValue(key,out var p)?p.GetDouble():fallback;
    public string[] Fields(string key)=>Parameters.TryGetValue(key,out var p)?p.EnumerateArray().Select(v=>v.GetString()!).ToArray():Array.Empty<string>();
}
public sealed class GpInput {
    public string Id {get;set;}="";
    public string Name {get;set;}="";
    public string LayerUri {get;set;}="";
    public bool? Visible {get;set;}
    public string Kind {get;set;}="";
    public string Shape {get;set;}="";
    public int Wkid {get;set;}
    public bool ProjectedMeters {get;set;}
    public double CellSizeX {get;set;}
    public double CellSizeY {get;set;}
    public string[] Fields {get;set;}=Array.Empty<string>();
}
public sealed class GeoprocessingPlan {
    public string Summary {get;set;}="";
    public List<GpStep> Steps {get;set;}=new();
    public string ToJson()=>JsonSerializer.Serialize(this,new JsonSerializerOptions(Json.Options){Encoder=System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping});
    static readonly Regex IdPattern=new("^s[1-9][0-9]?$",RegexOptions.CultureInvariant);
    public static readonly Regex FieldPattern=new(@"^[\p{L}_][\p{L}\p{N}_]{0,63}$",RegexOptions.CultureInvariant);
    static void Keys(JsonElement node,params string[] allowed) {
        if(node.ValueKind!=JsonValueKind.Object)throw new InvalidOperationException("计划必须是对象。");
        var found=new HashSet<string>();foreach(var p in node.EnumerateObject())if(!allowed.Contains(p.Name)||!found.Add(p.Name))throw new InvalidOperationException("存在未知或重复计划参数："+p.Name);
        if(found.Count!=allowed.Length)throw new InvalidOperationException("计划缺少必填字段。");
    }
    public static GeoprocessingPlan Parse(string json) {
        using var doc=JsonDocument.Parse(json,new JsonDocumentOptions{MaxDepth=24});var root=doc.RootElement;Keys(root,"summary","steps");
        foreach(var step in root.GetProperty("steps").EnumerateArray()) {
            Keys(step,"id","operation","inputs","parameters");var names=new HashSet<string>();foreach(var p in step.GetProperty("parameters").EnumerateObject())if(!names.Add(p.Name))throw new InvalidOperationException("重复工具参数："+p.Name);
        }
        var result=JsonSerializer.Deserialize<GeoprocessingPlan>(json,Json.Options)??throw new InvalidOperationException("分析计划为空。");result.Validate();return result;
    }
    public void Validate() {
        if(Summary.Length==0||Summary.Length>2000||Steps.Count<1||Steps.Count>12)throw new InvalidOperationException("分析计划需说明目的，并包含1–12步。");
        var ids=new HashSet<string>();
        foreach(var step in Steps) {
            if(!IdPattern.IsMatch(step.Id)||!ids.Add(step.Id))throw new InvalidOperationException("步骤ID须唯一，如s1、s2。");
            var tool=GpCatalog.Get(step.Operation);
            if(step.Inputs.Count<tool.MinInputs||step.Inputs.Count>tool.MaxInputs)throw new InvalidOperationException(step.Operation+"输入数量不正确。");
            foreach(var input in step.Inputs)if(!Regex.IsMatch(input,"^L[1-9][0-9]{0,2}$")&&(!ids.Contains(input)||input==step.Id))throw new InvalidOperationException("输入只允许当前图层ID或前序步骤ID，禁止文件路径与前向引用。");
            foreach(var p in step.Parameters) {
                if(!tool.Rules.TryGetValue(p.Key,out var rule))throw new InvalidOperationException(step.Operation+"不接受参数："+p.Key);
                rule.Validate(p.Key,p.Value);
            }
            foreach(var required in tool.Rules.Where(p=>p.Value.Required))if(!step.Parameters.ContainsKey(required.Key))throw new InvalidOperationException(step.Operation+"缺少参数："+required.Key);
            if(step.Operation=="reclassify") {
                double previous=double.NegativeInfinity;
                foreach(var row in step.Parameters["ranges"].EnumerateArray()) {var v=row.EnumerateArray().Select(x=>x.GetDouble()).ToArray();if(v[0]>=v[1]||v[0]<previous||v[2]!=Math.Truncate(v[2])||Math.Abs(v[2])>1000000)throw new InvalidOperationException("重分类区间须升序且不重叠，输出类别须为整数。");previous=v[1];}
            }
        }
    }
    public void ValidateInputs(IEnumerable<GpInput> source,Func<int,bool>? isProjectedMeters=null) {
        Validate();var inputs=source.ToDictionary(s=>s.Id,StringComparer.Ordinal);
        foreach(var step in Steps) {
            var tool=GpCatalog.Get(step.Operation);var resolved=step.Inputs.Select(id=>inputs.TryGetValue(id,out var input)?input:throw new InvalidOperationException("输入图层不存在："+id)).ToArray();
            for(int i=0;i<resolved.Length;i++) {
                string expected=tool.InputKinds[Math.Min(i,tool.InputKinds.Length-1)];
                if(!expected.Split('|').Contains(resolved[i].Kind))throw new InvalidOperationException(step.Operation+"的输入类型应为"+expected+"，实际为"+resolved[i].Kind);
            }
            if(tool.RequiresMeters&&!resolved[0].ProjectedMeters)throw new InvalidOperationException(step.Operation+"需要米制投影输入，请先添加project_vector/project_raster步骤。");
            if(step.Operation=="buffer"&&resolved[0].Wkid==0)throw new InvalidOperationException("缓冲输入坐标系未知。");
            if(new[]{"slope","aspect"}.Contains(step.Operation)&&resolved[0].Wkid==0)throw new InvalidOperationException("地形计算需已知坐标系。");
            if(new[]{"polygon_to_line","raster_clip","clip"}.Contains(step.Operation)&&resolved[step.Operation=="polygon_to_line"?0:1].Shape is not ("Polygon" or ""))throw new InvalidOperationException(step.Operation+"需要面要素。");
            if(step.Operation=="union"&&resolved.Any(r=>r.Shape is not ("Polygon" or "")))throw new InvalidOperationException("联合叠加需要面要素。");
            if(step.Operation=="line_density"&&resolved[0].Shape!="Polyline")throw new InvalidOperationException("线密度需要线要素。");
            if(new[]{"idw","extract_values","points_to_line"}.Contains(step.Operation)&&resolved[0].Shape is not ("Point" or ""))throw new InvalidOperationException(step.Operation+"需要点要素。");
            if(step.Operation=="kernel_density"&&resolved[0].Shape is not ("Point" or "Polyline" or ""))throw new InvalidOperationException("核密度输入须为点或线。");
            if(new[]{"raster_add","raster_subtract","raster_multiply","raster_divide"}.Contains(step.Operation)&&(resolved[0].Wkid==0||resolved[0].Wkid!=resolved[1].Wkid||Math.Abs(resolved[0].CellSizeX-resolved[1].CellSizeX)>1e-8||Math.Abs(resolved[0].CellSizeY-resolved[1].CellSizeY)>1e-8))throw new InvalidOperationException("栅格算术需相同已知坐标系和像元大小，请先投影/重采样。");
            foreach(var pair in step.Parameters)if(tool.Rules[pair.Key].Type=="field"||tool.Rules[pair.Key].Type=="fields") {
                var fields=pair.Value.ValueKind==JsonValueKind.Array?pair.Value.EnumerateArray().Select(v=>v.GetString()!):new[]{pair.Value.GetString()!};
                if(resolved[0].Fields.Length>0)foreach(var f in fields)if(!resolved[0].Fields.Contains(f,StringComparer.OrdinalIgnoreCase))throw new InvalidOperationException("字段不存在："+f);
            }
            bool projected=resolved[0].ProjectedMeters;int wkid=resolved[0].Wkid;
            if(step.Operation.StartsWith("project_")){wkid=(int)step.Number("wkid");projected=isProjectedMeters?.Invoke(wkid)??false;}
            double cell=step.Number("cell_size",resolved[0].CellSizeX);
            inputs[step.Id]=new GpInput{Id=step.Id,Kind=tool.OutputKind,Shape=tool.OutputShape=="same"?resolved[0].Shape:tool.OutputShape,Wkid=wkid,ProjectedMeters=projected,CellSizeX=cell,CellSizeY=step.Parameters.ContainsKey("cell_size")?cell:resolved[0].CellSizeY,Fields=Array.Empty<string>()};
        }
    }
}
public sealed record GpRule(string Type,bool Required=false,double Min=0,double Max=1e9,string[]? Choices=null) {
    public void Validate(string name,JsonElement value) {
        bool valid=Type switch {
            "number" or "integer"=>value.ValueKind==JsonValueKind.Number&&value.TryGetDouble(out double n)&&double.IsFinite(n)&&n>=Min&&n<=Max&&(Type!="integer"||n==Math.Truncate(n)),
            "enum"=>value.ValueKind==JsonValueKind.String&&Choices!.Contains(value.GetString()),
            "field"=>value.ValueKind==JsonValueKind.String&&GeoprocessingPlan.FieldPattern.IsMatch(value.GetString()??""),
            "fields"=>value.ValueKind==JsonValueKind.Array&&value.GetArrayLength()<=20&&value.EnumerateArray().All(v=>v.ValueKind==JsonValueKind.String&&GeoprocessingPlan.FieldPattern.IsMatch(v.GetString()??"")),
            "text"=>value.ValueKind==JsonValueKind.String&&value.GetString()!.Length<=500&&!value.GetString()!.Any(char.IsControl),
            "ranges"=>value.ValueKind==JsonValueKind.Array&&value.GetArrayLength()>0&&value.GetArrayLength()<=100&&value.EnumerateArray().All(r=>r.ValueKind==JsonValueKind.Array&&r.GetArrayLength()==3&&r.EnumerateArray().All(v=>v.ValueKind==JsonValueKind.Number&&v.TryGetDouble(out double x)&&double.IsFinite(x))),
            _=>false
        };if(!valid)throw new InvalidOperationException("参数格式或范围无效："+name);
    }
    public object Schema()=>Type switch {
        "number" or "integer"=>new{type=Type,minimum=Min,maximum=Max},
        "enum"=>new{type="string",@enum=Choices},
        "fields"=>new{type="array",items=new{type="string"},maxItems=20},
        "ranges"=>new{type="array",items=new{type="array",items=new{type="number"},minItems=3,maxItems=3},minItems=1,maxItems=100},
        _=>new{type="string",maxLength=500}
    };
}
public sealed record GpTool(string Operation,string Label,string Tool,string OutputKind,string[] InputKinds,int MinInputs,int MaxInputs,Dictionary<string,GpRule> Rules,bool RequiresMeters=false,string OutputShape="same");
public static class GpCatalog {
    static GpRule Num(bool required=true,double min=.01,double max=1e7)=>new("number",required,min,max);
    static GpRule Choice(bool required,params string[] choices)=>new("enum",required,Choices:choices);
    static GpRule Field(bool required=true)=>new("field",required);
    static Dictionary<string,GpRule> Rules(params (string,GpRule)[] rules)=>rules.ToDictionary(p=>p.Item1,p=>p.Item2);
    static GpTool T(string id,string label,string tool,string output,string input,int min=1,int max=1,Dictionary<string,GpRule>? rules=null,bool meters=false,string shape="same")=>new(id,label,tool,output,input.Split(','),min,max,rules??new(),meters,shape);
    public static readonly IReadOnlyList<GpTool> Tools=new[]{
        T("copy_features","导出要素","management.CopyFeatures","vector","vector"),
        T("project_vector","要素投影","management.Project","vector","vector",rules:Rules(("wkid",new("integer",true,2000,100000)))),
        T("buffer","测地线缓冲","analysis.Buffer","vector","vector",rules:Rules(("distance",Num())),shape:"Polygon"),
        T("clip","矢量裁剪","analysis.Clip","vector","vector,vector",2,2),
        T("intersect","相交叠加","analysis.Intersect","vector","vector",2,8,shape:""),
        T("union","面联合叠加","analysis.Union","vector","vector",2,8,shape:"Polygon"),
        T("erase","擦除","analysis.Erase","vector","vector,vector",2,2),
        T("dissolve","融合","management.Dissolve","vector","vector",rules:Rules(("fields",new("fields")))),
        T("merge","合并","management.Merge","vector","vector",2,8),
        T("singlepart","多部件转单部件","management.MultipartToSinglepart","vector","vector"),
        T("polygon_to_line","面转线","management.PolygonToLine","vector","vector",shape:"Polyline"),
        T("feature_to_point","要素转内部点","management.FeatureToPoint","vector","vector",shape:"Point"),
        T("points_to_line","点转线","management.PointsToLine","vector","vector",rules:Rules(("group_field",Field(false)),("sort_field",Field(false))),shape:"Polyline"),
        T("spatial_join","空间连接统计","analysis.SpatialJoin","vector","vector,vector",2,2),
        T("select_location","按位置提取","management.SelectLayerByLocation","vector","vector,vector",2,2,rules:Rules(("relationship",Choice(false,"INTERSECT","WITHIN","CONTAINS","BOUNDARY_TOUCHES")))),
        T("select_attribute","按属性提取","analysis.Select","vector","vector",rules:Rules(("field",Field()),("operator",Choice(true,"EQ","NE","GT","GE","LT","LE")),("value",new("text",true)),("value_type",Choice(true,"text","number")))),
        T("summary_statistics","分组汇总统计","analysis.Statistics","table","vector|table",rules:Rules(("field",Field()),("statistic",Choice(true,"SUM","MEAN","MIN","MAX","COUNT","STD")),("fields",new("fields")))),
        T("project_raster","栅格投影","management.ProjectRaster","raster","raster",rules:Rules(("wkid",new("integer",true,2000,100000)),("cell_size",Num()),("resampling",Choice(false,"NEAREST","BILINEAR","CUBIC")))),
        T("resample","栅格重采样","management.Resample","raster","raster",rules:Rules(("cell_size",Num()),("resampling",Choice(false,"NEAREST","BILINEAR","CUBIC")))),
        T("raster_clip","栅格按面掩膜","sa.ExtractByMask","raster","raster,vector",2,2),
        T("slope","测地线坡度","sa.Slope","raster","raster",rules:Rules(("vertical_unit",Choice(true,"METER","FOOT")),("slope_unit",Choice(true,"DEGREE","PERCENT_RISE")))),
        T("aspect","测地线坡向","sa.Aspect","raster","raster",rules:Rules(("vertical_unit",Choice(true,"METER","FOOT")))),
        T("hillshade","山体阴影","sa.HillShade","raster","raster",rules:Rules(("azimuth",Num(false,0,360)),("altitude",Num(false,0,90)),("z_factor",Num(true,.00001,10000))),meters:true),
        T("reclassify","栅格重分类","sa.Reclassify","raster","raster",rules:Rules(("ranges",new("ranges",true)),("missing",Choice(true,"NODATA","DATA")))),
        T("contour","等值线","sa.Contour","vector","raster",rules:Rules(("interval",Num()),("base",Num(false,-1e9,1e9))),shape:"Polyline"),
        T("fill","填洼","sa.Fill","raster","raster",rules:Rules(("z_limit",Num(false)))),
        T("flow_direction","D8流向","sa.FlowDirection","raster","raster"),
        T("flow_accumulation","D8汇流累积","sa.FlowAccumulation","raster","raster"),
        T("watershed","流域划分","sa.Watershed","raster","raster,raster",2,2),
        T("line_density","线密度","sa.LineDensity","raster","vector",rules:Rules(("cell_size",Num()),("radius",Num())),meters:true),
        T("kernel_density","核密度","sa.KernelDensity","raster","vector",rules:Rules(("cell_size",Num()),("radius",Num())),meters:true),
        T("zonal_statistics","分区统计表","sa.ZonalStatisticsAsTable","table","vector|raster,raster",2,2,rules:Rules(("field",Field()),("statistic",Choice(false,"ALL","MEAN","SUM","MINIMUM","MAXIMUM","STD","RANGE")))),
        T("extract_values","栅格值采样至点","sa.ExtractValuesToPoints","vector","vector,raster",2,2,shape:"Point"),
        T("raster_float","栅格转浮点","sa.Float","raster","raster"),
        T("idw","反距离权重插值","sa.Idw","raster","vector",rules:Rules(("field",Field()),("cell_size",Num()),("power",Num(false,.01,10))),meters:true),
        T("raster_add","栅格相加","sa.Plus","raster","raster",2,2),T("raster_subtract","栅格相减","sa.Minus","raster","raster",2,2),T("raster_multiply","栅格相乘","sa.Times","raster","raster",2,2),T("raster_divide","栅格相除","sa.Divide","raster","raster",2,2)
    };
    public static GpTool Get(string id)=>Tools.FirstOrDefault(t=>t.Operation==id)??throw new InvalidOperationException("未注册的分析操作："+id);
    public static string Describe()=>JsonSerializer.Serialize(Tools.Select(t=>new{operation=t.Operation,description=t.Label,inputKinds=t.InputKinds,minInputs=t.MinInputs,maxInputs=t.MaxInputs,output=t.OutputKind,requiresProjectedMeters=t.RequiresMeters,parameters=t.Rules.ToDictionary(p=>p.Key,p=>new{type=p.Value.Type,required=p.Value.Required,choices=p.Value.Choices})}),Json.Options);
    public static object Schema() {
        var parameters=Tools.SelectMany(t=>t.Rules).GroupBy(p=>p.Key).ToDictionary(g=>g.Key,g=>g.First().Value.Type=="enum"?new GpRule("enum",Choices:g.SelectMany(p=>p.Value.Choices!).Distinct().ToArray()).Schema():g.First().Value.Schema());
        return new{type="object",additionalProperties=false,required=new[]{"summary","steps"},properties=new{summary=new{type="string",maxLength=2000},steps=new{type="array",minItems=1,maxItems=12,items=new{type="object",additionalProperties=false,required=new[]{"id","operation","inputs","parameters"},properties=new{id=new{type="string",pattern="^s[1-9][0-9]?$"},operation=new{type="string",@enum=Tools.Select(t=>t.Operation).ToArray()},inputs=new{type="array",items=new{type="string"},minItems=1,maxItems=8},parameters=new{type="object",additionalProperties=false,properties=parameters}}}}}};
    }
    static string N(double value)=>value.ToString("R",CultureInfo.InvariantCulture);
    public static object[] Arguments(GpStep s,object[] input,string output) {
        string Joined(string key)=>string.Join(";",s.Fields(key));object a=input[0];object b=input.Length>1?input[1]:"#";
        return s.Operation switch {
            "copy_features" or "singlepart" or "polygon_to_line" or "raster_float"=>new[]{a,output},
            "project_vector"=>new object[]{a,output,(int)s.Number("wkid")},
            "buffer"=>new object[]{a,output,N(s.Number("distance"))+" Meters","FULL","ROUND","ALL","","GEODESIC"},
            "clip" or "erase"=>new[]{a,b,output},
            "intersect" or "union" or "merge"=>new object[]{input,output},
            "dissolve"=>new object[]{a,output,Joined("fields")},
            "feature_to_point"=>new object[]{a,output,"INSIDE"},
            "points_to_line"=>new object[]{a,output,s.Text("group_field"),s.Text("sort_field")},
            "spatial_join"=>new object[]{a,b,output,"JOIN_ONE_TO_ONE","KEEP_ALL","#","INTERSECT"},
            "summary_statistics"=>new object[]{a,output,s.Text("field")+" "+s.Text("statistic"),Joined("fields")},
            "project_raster"=>new object[]{a,output,(int)s.Number("wkid"),s.Text("resampling","NEAREST"),s.Number("cell_size")},
            "resample"=>new object[]{a,output,s.Number("cell_size"),s.Text("resampling","NEAREST")},
            "raster_clip"=>new[]{a,b,output},
            "slope"=>new object[]{a,output,s.Text("slope_unit"),1,"GEODESIC",s.Text("vertical_unit")},
            "aspect"=>new object[]{a,output,"GEODESIC",s.Text("vertical_unit")},
            "hillshade"=>new object[]{a,output,s.Number("azimuth",315),s.Number("altitude",45),"NO_SHADOWS",s.Number("z_factor")},
            "reclassify"=>new object[]{a,"VALUE",string.Join(";",s.Parameters["ranges"].EnumerateArray().Select(r=>string.Join(" ",r.EnumerateArray().Select(v=>N(v.GetDouble()))))),output,s.Text("missing")},
            "contour"=>new object[]{a,output,s.Number("interval"),s.Number("base")},
            "fill"=>new object[]{a,output,s.Parameters.ContainsKey("z_limit")?s.Number("z_limit"):"#"},
            "flow_direction"=>new object[]{a,output,"NORMAL","#","D8"},
            "flow_accumulation"=>new object[]{a,output,"#","FLOAT","D8"},
            "watershed"=>new object[]{a,b,output,"VALUE"},
            "line_density"=>new object[]{a,"NONE",output,s.Number("cell_size"),s.Number("radius"),"SQUARE_KILOMETERS"},
            "kernel_density"=>new object[]{a,"NONE",output,s.Number("cell_size"),s.Number("radius"),"SQUARE_KILOMETERS","DENSITIES","PLANAR"},
            "zonal_statistics"=>new object[]{a,s.Text("field"),b,output,"DATA",s.Text("statistic","ALL")},
            "extract_values"=>new object[]{a,b,output,"NONE","VALUE_ONLY"},
            "idw"=>new object[]{a,s.Text("field"),output,s.Number("cell_size"),s.Number("power",2)},
            "raster_add" or "raster_subtract" or "raster_multiply" or "raster_divide"=>new[]{a,b,output},
            _=>throw new InvalidOperationException("此操作需要本地执行器处理："+s.Operation)
        };
    }
}

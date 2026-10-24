using ProCodex;
using System.Text.Json;
if(args.Length>0&&args[0]=="--analysis-schema"){Console.WriteLine(JsonSerializer.Serialize(GpCatalog.Schema()));return 0;}
// Synthetic subprocess exercises the real stdio transport without model/network use.
if(args.Length>0&&args[0]=="app-server"){
    string? line;while((line=Console.ReadLine())!=null){using var d=JsonDocument.Parse(line);var r=d.RootElement;if(!r.TryGetProperty("method",out var m))continue;
        string method=m.GetString()!;if(!r.TryGetProperty("id",out var id))continue;
        object result=method switch{"account/read"=>new{account=new{type="chatgpt",planType="plus"}},"thread/start"=>new{thread=new{id="fixture-thread"}},"turn/start"=>new{turn=new{id="fixture-turn"}},_=>new{}};
        Console.WriteLine(JsonSerializer.Serialize(new{id=id.Clone(),result}));
        if(method=="turn/start"){
            Console.WriteLine(JsonSerializer.Serialize(new{id=999,method="item/commandExecution/requestApproval",@params=new{}}));
            string? denied=Console.ReadLine();using var denial=JsonDocument.Parse(denied!);if(!denial.RootElement.TryGetProperty("error",out _))return 2;
            string mode=Environment.GetEnvironmentVariable("PROCODEX_FAKE_MODE")??"success";
            string text=mode=="invalid"?"{}":"{\"message\":\"fixture\",\"buffer\":{\"layerId\":\"L1\",\"distanceMeters\":500}}";
            Console.WriteLine(JsonSerializer.Serialize(new{method="item/completed",@params=new{item=new{type="agentMessage",text}}}));
            Console.WriteLine(JsonSerializer.Serialize(new{method="turn/completed",@params=new{turn=new{status=mode=="failed"?"failed":"completed"}}}));
        }
    }return 0;
}
var checks=new List<object>();int failures=0;
void Check(string name,Action action){try{action();checks.Add(new{name,passed=true});}catch(Exception ex){failures++;checks.Add(new{name,passed=false,error=ex.Message});}}
Check("turn-limit-structured-not-crash",()=>{var r=GisAgentDesktop.RunOutcome.Parse("{\"status\":\"partial\",\"stopReason\":\"turn_limit\",\"maxTurns\":60}");if(!r.IsTurnLimit||!r.LimitNotice.Contains("60")||!r.LimitNotice.Contains("不是API"))throw new Exception("Missing limit guidance");});
Check("turn-limit-legacy-report",()=>{var r=GisAgentDesktop.RunOutcome.Parse("{\"status\":\"partial\",\"message\":\"达到决策轮次上限；保留日志和成果，可继续对话。\"}");if(!r.IsTurnLimit)throw new Exception("Old limit report misclassified");});
Check("request-error-is-not-turn-limit",()=>{var r=GisAgentDesktop.RunOutcome.Parse("{\"status\":\"interrupted\",\"message\":\"HTTP 429\"}");if(r.IsTurnLimit)throw new Exception("API failure mislabeled as task limit");});
Check("partial-output-is-not-turn-limit",()=>{var r=GisAgentDesktop.RunOutcome.Parse("{\"status\":\"partial\",\"message\":\"导出失败\"}");if(r.IsTurnLimit)throw new Exception("Partial task mislabeled as task limit");});
Check("checkpoint-found-after-api-test-clears-memory",()=>{
 string folder=Path.Combine(Path.GetTempPath(),"gis-checkpoint-"+Guid.NewGuid().ToString("N"));string run=Path.Combine(folder,"outputs","agent","prior");Directory.CreateDirectory(run);
 try{string file=Path.Combine(run,"conversation.json");File.WriteAllText(file,JsonSerializer.Serialize(new{processId=123,mapUri="map",model="previous-model",messages=new[]{new{role="user",content="original goal"}}}));if(GisAgentDesktop.TaskCheckpoint.Find(folder,"",123,"map")!=file)throw new Exception("Lost task was not recovered from disk");if(GisAgentDesktop.TaskCheckpoint.Find(folder,"",123,"other-map")!="")throw new Exception("Wrong map accepted");if(GisAgentDesktop.TaskCheckpoint.Find(folder,"",999,"map")!="")throw new Exception("Wrong Pro process accepted");}finally{Directory.Delete(folder,true);}
});
Check("checkpoint-corrupt-preferred-falls-back-to-valid-history",()=>{
 string folder=Path.Combine(Path.GetTempPath(),"gis-checkpoint-"+Guid.NewGuid().ToString("N"));string valid=Path.Combine(folder,"outputs","agent","valid");string broken=Path.Combine(folder,"outputs","agent","broken");Directory.CreateDirectory(valid);Directory.CreateDirectory(broken);
 try{string file=Path.Combine(valid,"conversation.json");File.WriteAllText(file,JsonSerializer.Serialize(new{processId=123,mapUri="map",messages=new[]{new{role="user",content="goal"}}}));File.WriteAllText(Path.Combine(broken,"conversation.json"),"{");if(GisAgentDesktop.TaskCheckpoint.Find(folder,broken,123,"map")!=file)throw new Exception("Valid checkpoint was not recovered");}finally{Directory.Delete(folder,true);}
});
void Reject(string json){try{Reply.Parse(json);}catch{ return;}throw new Exception("Invalid plan was accepted");}
Check("valid-buffer",()=>{var r=Reply.Parse("{\"message\":\"pending\",\"buffer\":{\"layerId\":\"L1\",\"distanceMeters\":500}}");if(r.Plan?.DistanceMeters!=500)throw new Exception();});
Check("clarification",()=>{if(Reply.Parse("{\"message\":\"which layer?\",\"buffer\":null}").Plan!=null)throw new Exception();});
foreach(var distance in new[]{"0","-1","100001","1e309"})Check("reject-distance-"+distance,()=>Reject("{\"message\":\"\",\"buffer\":{\"layerId\":\"L1\",\"distanceMeters\":"+distance+"}}"));
Check("reject-path-input",()=>Reject("{\"message\":\"\",\"buffer\":{\"layerId\":\"C:/secret\",\"distanceMeters\":500}}"));
Check("reject-unknown-tool",()=>Reject("{\"message\":\"\",\"buffer\":null,\"shell\":\"run\"}"));
Check("reject-duplicate-field",()=>Reject("{\"message\":\"\",\"buffer\":null,\"buffer\":null}"));
Check("reject-extra-plan-field",()=>Reject("{\"message\":\"\",\"buffer\":{\"layerId\":\"L1\",\"distanceMeters\":500,\"output\":\"x\"}}"));
Check("schema-serializable",()=>{using var schema=JsonDocument.Parse(JsonSerializer.Serialize(Reply.Schema));if(schema.RootElement.GetProperty("type").GetString()!="object")throw new Exception();});
Check("pipe-single-line-camel-case-metadata",()=>{string encoded=JsonSerializer.Serialize(new{inputs=new[]{new GpInput{Id="L1",Name="fixture",Kind="raster"}}},PipeJson.Options);using var d=JsonDocument.Parse(encoded);if(encoded.Contains('\n')||d.RootElement.GetProperty("inputs")[0].GetProperty("name").GetString()!="fixture")throw new Exception("Invalid pipe metadata");});
Check("analysis-catalog-46",()=>{if(GpCatalog.Tools.Count!=46)throw new Exception("Incomplete catalog");});
Check("moran-known-complete-graph",()=>{var rows=Enumerable.Range(0,8).Select(i=>new SpatialPermutation.Observation(i,i,0,i+1)).ToArray();var r=SpatialPermutation.Global(rows,7,42);if(Math.Abs(r.MoranI+1.0/7)>1e-12||r.PermutationP!=1||r.Permutations!=999)throw new Exception("Complete graph must equal expected I for every permutation");});
Check("moran-clustered-and-reproducible",()=>{var rows=Enumerable.Range(0,8).Select(i=>new SpatialPermutation.Observation(i,i<4?i:i+100,0,i<4?1:10)).ToArray();var a=SpatialPermutation.Global(rows,3,42);var b=SpatialPermutation.Global(rows.Reverse().ToArray(),3,42);if(Math.Abs(a.MoranI-1)>1e-12||a.PermutationP>=0.1||a!=b)throw new Exception("Clustering or ID-stable seeded permutation failed");});
Check("moran-rejects-constant-and-missing",()=>{foreach(double v in new[]{1.0,double.NaN}){bool rejected=false;try{SpatialPermutation.Global(Enumerable.Range(0,4).Select(i=>new SpatialPermutation.Observation(i,i,0,v)).ToArray(),1,42);}catch(ArgumentException){rejected=true;}if(!rejected)throw new Exception("Degenerate data accepted");}});
Check("category-selection-escapes-literal",()=>{var p=GeoprocessingPlan.Parse("{\"summary\":\"categories\",\"steps\":[{\"id\":\"s1\",\"operation\":\"select_values\",\"inputs\":[\"L1\"],\"parameters\":{\"field\":\"fclass\",\"values\":[\"restaurant\",\"a'b\"]}}]}");if(GpCatalog.TextSelection(p.Steps[0])!="\"fclass\" IN ('restaurant','a''b')")throw new Exception("Category text escaped incorrectly");});
Check("category-selection-rejects-empty-list",()=>{bool rejected=false;try{GeoprocessingPlan.Parse("{\"summary\":\"categories\",\"steps\":[{\"id\":\"s1\",\"operation\":\"select_values\",\"inputs\":[\"L1\"],\"parameters\":{\"field\":\"fclass\",\"values\":[]}}]}");}catch{rejected=true;}if(!rejected)throw new Exception("Empty category selection accepted");});
Check("normalization-requires-direction",()=>{bool rejected=false;try{GeoprocessingPlan.Parse("{\"summary\":\"normalize\",\"steps\":[{\"id\":\"s1\",\"operation\":\"raster_normalize\",\"inputs\":[\"L1\"],\"parameters\":{}}]}");}catch{rejected=true;}if(!rejected)throw new Exception("Direction was guessed");});
Check("grid-rejects-geographic-units",()=>{var p=GeoprocessingPlan.Parse("{\"summary\":\"grid\",\"steps\":[{\"id\":\"s1\",\"operation\":\"create_grid\",\"inputs\":[\"L1\"],\"parameters\":{\"cell_size\":1000}}]}");bool rejected=false;try{p.ValidateInputs(new[]{new GpInput{Id="L1",Kind="vector",Shape="Polygon",Wkid=4326}});}catch{rejected=true;}if(!rejected)throw new Exception("Degrees accepted as meters");});
Check("density-rejects-nonpolygon",()=>{var p=GeoprocessingPlan.Parse("{\"summary\":\"density\",\"steps\":[{\"id\":\"s1\",\"operation\":\"feature_density\",\"inputs\":[\"L1\"],\"parameters\":{\"field\":\"Join_Count\"}}]}");bool rejected=false;try{p.ValidateInputs(new[]{new GpInput{Id="L1",Kind="vector",Shape="Point",Wkid=32649,ProjectedMeters=true,Fields=new[]{"Join_Count"}}});}catch{rejected=true;}if(!rejected)throw new Exception("Point area density accepted");});
Check("lisa-999-fdr-parameter-order",()=>{var p=GeoprocessingPlan.Parse("{\"summary\":\"lisa\",\"steps\":[{\"id\":\"s1\",\"operation\":\"local_moran\",\"inputs\":[\"L1\"],\"parameters\":{\"field\":\"AG_DENS\",\"neighbors\":8,\"seed\":42}}]}");var values=GpCatalog.Arguments(p.Steps[0],new object[]{"fixture"},"output");if(values.Length!=11||!Equals(values[8],"APPLY_FDR")||!Equals(values[9],999)||!Equals(values[10],8)||!Equals(values[5],"ROW"))throw new Exception("Wrong LISA inference configuration");});
Check("statistics-reject-expression-as-field",()=>{bool rejected=false;try{GeoprocessingPlan.Parse("{\"summary\":\"moran\",\"steps\":[{\"id\":\"s1\",\"operation\":\"global_moran\",\"inputs\":[\"L1\"],\"parameters\":{\"field\":\"x;import os\",\"neighbors\":8}}]}");}catch{rejected=true;}if(!rejected)throw new Exception("Untrusted expression accepted");});
Check("analysis-reject-path-and-unknown-op",()=>{foreach(string operation in new[]{"copy_features","shell"}){bool rejected=false;try{GeoprocessingPlan.Parse(JsonSerializer.Serialize(new{summary="fixture",steps=new[]{new{id="s1",operation,inputs=new[]{"C:/secret.shp"},parameters=new{}}}}));}catch{rejected=true;}if(!rejected)throw new Exception("Unsafe plan accepted");}});
Check("analysis-chain-typed-validation",()=>{var plan=GeoprocessingPlan.Parse("{\"summary\":\"fixture\",\"steps\":[{\"id\":\"s1\",\"operation\":\"buffer\",\"inputs\":[\"L1\"],\"parameters\":{\"distance\":100}},{\"id\":\"s2\",\"operation\":\"clip\",\"inputs\":[\"L1\",\"s1\"],\"parameters\":{}}]}");plan.ValidateInputs(new[]{new GpInput{Id="L1",Kind="vector",Shape="Polygon",Wkid=32649,ProjectedMeters=true}});});
foreach(string mode in new[]{"success","invalid","failed"}){
    Environment.SetEnvironmentVariable("PROCODEX_FAKE_MODE",mode);bool passed=false;string error="";
    try{using var timeout=new CancellationTokenSource(TimeSpan.FromSeconds(10));using var fake=new CodexClient(Path.Combine(AppContext.BaseDirectory,"Checks.exe"));await fake.Initialize(timeout.Token);var reply=await fake.Ask("fixture","fixture","",timeout.Token);passed=mode=="success"&&reply.Plan?.DistanceMeters==500;}
    catch(Exception ex){passed=mode!="success";error=ex.Message;}
    if(!passed)failures++;checks.Add(new{name="synthetic-stdio-"+mode,passed,error,live=false});
}
Environment.SetEnvironmentVariable("PROCODEX_FAKE_MODE",null);
if(args.Length>1&&(args[0]=="--account"||args[0]=="--live")){
    using var timeout=new CancellationTokenSource(TimeSpan.FromSeconds(30));using var client=new CodexClient(args[1]);
    try{await client.Initialize(timeout.Token);string account=await client.Account(timeout.Token);checks.Add(new{name="local-codex-initialize-account",passed=true,account,inference=false});
        if(args[0]=="--live"){
            timeout.CancelAfter(TimeSpan.FromMinutes(3));var reply=await client.Ask("为L1创建500米缓冲区，只提出计划，尚未执行。","[{\"id\":\"L1\",\"name\":\"synthetic_roads\",\"shape\":\"Polyline\",\"wkid\":4326,\"selectedCount\":0}]","",timeout.Token);
            if(reply.Plan?.LayerId!="L1"||reply.Plan.DistanceMeters!=500)throw new Exception("Live model did not produce expected buffer plan");
            File.WriteAllText("outputs/live-plan.json",JsonSerializer.Serialize(new{live=true,syntheticMetadata=true,gisExecuted=false,reply},new JsonSerializerOptions{WriteIndented=true}));checks.Add(new{name="live-subscription-buffer-plan",passed=true});
        }
    }catch(Exception ex){failures++;checks.Add(new{name="local-codex-check",passed=false,error=ex.Message});}
}
Directory.CreateDirectory("outputs");File.WriteAllText("outputs/checks.json",JsonSerializer.Serialize(new{timestamp=DateTimeOffset.Now,failures,checks},new JsonSerializerOptions{WriteIndented=true}));Console.WriteLine($"{checks.Count} checks; {failures} failed");return failures==0?0:1;

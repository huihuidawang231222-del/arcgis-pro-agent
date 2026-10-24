using System.Text.Json.Nodes;

internal static class ModelCompatibility {
 internal static string Apply(Uri endpoint,string payload){
  // Only the verified official model receives this adaptation. Other providers retain their payload.
  if(endpoint.Host!="open.bigmodel.cn")return payload;
  var body=JsonNode.Parse(payload)!;
  if(!string.Equals(body["model"]?.GetValue<string>(),"glm-5.3-flash",StringComparison.OrdinalIgnoreCase))return payload;
  body["thinking"]=new JsonObject{["type"]="enabled"};
  if(body["reasoning_effort"]==null)body["reasoning_effort"]="low";
  return body.ToJsonString();
 }
}

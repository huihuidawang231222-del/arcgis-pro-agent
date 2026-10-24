using System.Text.Json.Nodes;

internal static class ModelCompatibility {
 internal static string Apply(Uri endpoint,string payload){
  // Both official 5.3 Flash variants require thinking; unrelated providers/models stay unchanged.
  if(endpoint.Host!="open.bigmodel.cn")return payload;
  var body=JsonNode.Parse(payload)!;
  string model=body["model"]?.GetValue<string>()??"";
  if(!string.Equals(model,"glm-5.3-flash",StringComparison.OrdinalIgnoreCase)&&!string.Equals(model,"glm-5.3-flashx",StringComparison.OrdinalIgnoreCase))return payload;
  body["thinking"]=new JsonObject{["type"]="enabled"};
  if(body["reasoning_effort"]==null)body["reasoning_effort"]="low";
  return body.ToJsonString();
 }
}

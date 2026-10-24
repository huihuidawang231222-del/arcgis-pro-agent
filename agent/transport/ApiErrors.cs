using System.Text.Json;
using System.Text.RegularExpressions;

internal static class ApiErrors {
 internal static string Describe(int status,string body,string secret){
  string code="",message="";
  try{using var doc=JsonDocument.Parse(body);if(doc.RootElement.TryGetProperty("error",out var e)&&e.ValueKind==JsonValueKind.Object){if(e.TryGetProperty("code",out var c))code=c.ToString();if(e.TryGetProperty("message",out var m)&&m.ValueKind==JsonValueKind.String)message=m.GetString()??"";}}catch(JsonException){}
  string Clean(string text){if(secret.Length>0){text=text.Replace(secret,"[redacted]");foreach(string part in secret.Split('.'))if(part.Length>=8)text=text.Replace(part,"[redacted]");}text=Regex.Replace(text,@"[A-Za-z0-9_\-]{24,}","[redacted]");return text.Replace('\r',' ').Replace('\n',' ');}
  code=Clean(code);message=Clean(message);if(code.Length>40)code=code[..40];if(message.Length>240)message=message[..240];
  string advice=status switch {
   401 when code=="1005"=>"账户要求二次认证，请先在智谱官网完成验证。",
   401 when code=="1001"=>"服务端未收到鉴权头，请核对接口及代理是否移除了 Authorization。",
   401=>"鉴权未通过：请在本机设置中更新有效密钥，并核对标准API与Coding套餐的密钥/接口类型；修改模型名或反复重试不能修复无效凭据。",
   403=>"请检查该密钥的模型和接口权限。",
   429 when code=="1113"=>"当前模型余额不足或没有适用资源包；请核对所选模型与资源包适用范围，其他模型的剩余额度不代表此模型可用。已保留任务，不会自动重试或自动切换模型。",
   429=>"请求受限，请按业务码检查频率、额度或服务繁忙。",
   _=>"请检查接口、模型和请求参数。"
  };
  return JsonSerializer.Serialize(new{error=$"模型HTTP {status}"+(code.Length>0?$" / 业务码 {code}":"")+(message.Length>0?$"：{message}":"")+"；"+advice+"未自动重试。",httpStatus=status,providerCode=code,errorType=status==429&&code=="1113"?"resource_exhausted":"provider_error"});
 }
}

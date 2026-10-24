using System.Text.Json;

namespace GisAgentDesktop;

internal sealed record RunOutcome(string Status, string StopReason, string Message, int? MaxTurns) {
 public bool IsTurnLimit=>StopReason=="turn_limit"||(Status=="partial"&&Message.StartsWith("达到决策轮次上限",StringComparison.Ordinal));
 public string LimitNotice=>"已达到本轮"+(MaxTurns.HasValue?$" {MaxTurns.Value} 轮":"决策轮次")+"上限，任务已暂停。已有成果和记录已保留，输入“继续”可接续任务。这是本地Agent的任务保护限制，不是API账户调用次数限制。";
 public static RunOutcome Parse(string json){
  using var doc=JsonDocument.Parse(json);var root=doc.RootElement;
  string Text(string name)=>root.TryGetProperty(name,out var value)&&value.ValueKind==JsonValueKind.String?value.GetString()??"":"";
  int? limit=root.TryGetProperty("maxTurns",out var turns)&&turns.TryGetInt32(out int count)?count:null;
  return new(Text("status"),Text("stopReason"),Text("message"),limit);
 }
}

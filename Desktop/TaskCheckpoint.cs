using System.Text.Json;

namespace GisAgentDesktop;

internal static class TaskCheckpoint {
 public static string Find(string root,string preferredRunFolder,int processId,string mapUri){
  if(string.IsNullOrEmpty(mapUri))return "";
  var candidates=new List<string>();
  if(!string.IsNullOrEmpty(preferredRunFolder))candidates.Add(Path.Combine(preferredRunFolder,"conversation.json"));
  string runs=Path.Combine(root,"outputs","agent");
  if(Directory.Exists(runs))candidates.AddRange(Directory.EnumerateDirectories(runs).Select(folder=>Path.Combine(folder,"conversation.json")).Where(File.Exists).OrderByDescending(File.GetLastWriteTimeUtc));
  foreach(string file in candidates.Distinct()){
   try{using var state=JsonDocument.Parse(File.ReadAllText(file));var value=state.RootElement;
    if(value.GetProperty("processId").GetInt32()==processId&&value.GetProperty("mapUri").GetString()==mapUri&&value.TryGetProperty("messages",out var messages)&&messages.ValueKind==JsonValueKind.Array&&messages.GetArrayLength()>0)return file;
   }catch(IOException){}catch(JsonException){}catch(KeyNotFoundException){}catch(InvalidOperationException){}
  }
  return "";
 }
}

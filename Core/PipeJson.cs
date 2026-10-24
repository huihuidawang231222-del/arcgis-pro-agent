using System.Text.Json;
namespace ProCodex;
public static class PipeJson {
    public static readonly JsonSerializerOptions Options=new(Json.Options){WriteIndented=false};
}

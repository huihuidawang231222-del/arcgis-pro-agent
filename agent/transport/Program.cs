using System.Net;
using System.Net.Http.Headers;
using System.Runtime.InteropServices;
using System.Security.Authentication;
using System.Text;
using System.Text.Json;

internal static class Program {
    [StructLayout(LayoutKind.Sequential)] struct Blob { public int Length; public IntPtr Data; }
    [DllImport("crypt32.dll",SetLastError=true)] static extern bool CryptUnprotectData(ref Blob input,IntPtr description,IntPtr entropy,IntPtr reserved,IntPtr prompt,int flags,out Blob output);
    [DllImport("kernel32.dll")] static extern IntPtr LocalFree(IntPtr pointer);
    static string Secret(string encoded){var bytes=Convert.FromBase64String(encoded);var input=new Blob{Length=bytes.Length,Data=Marshal.AllocHGlobal(bytes.Length)};Blob output=default;
        try{Marshal.Copy(bytes,0,input.Data,bytes.Length);if(!CryptUnprotectData(ref input,IntPtr.Zero,IntPtr.Zero,IntPtr.Zero,IntPtr.Zero,1,out output))throw new InvalidOperationException("本机凭据解密失败。");var clear=new byte[output.Length];try{Marshal.Copy(output.Data,clear,0,clear.Length);return Encoding.UTF8.GetString(clear);}finally{Array.Clear(clear);}}
        finally{Array.Clear(bytes);for(int i=0;i<input.Length;i++)Marshal.WriteByte(input.Data,i,0);Marshal.FreeHGlobal(input.Data);if(output.Data!=IntPtr.Zero){for(int i=0;i<output.Length;i++)Marshal.WriteByte(output.Data,i,0);LocalFree(output.Data);}}}
    static async Task Main(string[] args){Console.InputEncoding=Encoding.UTF8;Console.OutputEncoding=new UTF8Encoding(false);
        int configIndex=Array.IndexOf(args,"--config");
        if(configIndex>=0&&configIndex+1>=args.Length)throw new ArgumentException("--config requires a local configuration file.");
        string configPath=configIndex>=0?Path.GetFullPath(args[configIndex+1]):Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"ArcGISProAgent","providers.json");
        using var cfg=JsonDocument.Parse(File.ReadAllText(configPath));
        string id=args.Length>0?args[0]:cfg.RootElement.GetProperty("activeId").GetString()!;
        var p=cfg.RootElement.GetProperty("profiles").EnumerateArray().Single(x=>x.GetProperty("id").GetString()==id);
        if(p.GetProperty("protocol").GetString()!="chat")throw new InvalidOperationException("仅支持Chat兼容协议。");
        var endpoint=new Uri(p.GetProperty("endpoint").GetString()!);if(endpoint.Scheme!="https"||endpoint.UserInfo.Length>0||endpoint.Query.Length>0||endpoint.Fragment.Length>0)throw new InvalidOperationException("HTTPS地址无效。");
        var handler=new HttpClientHandler{AllowAutoRedirect=false,UseProxy=p.GetProperty("useSystemProxy").GetBoolean()||args.Contains("--system-proxy"),SslProtocols=p.GetProperty("forceTls12").GetBoolean()?SslProtocols.Tls12:SslProtocols.None};
        if(handler.UseProxy&&p.TryGetProperty("proxyAddress",out var proxy)&&proxy.GetString() is string address&&address.Length>0)handler.Proxy=new WebProxy(address);
        int timeoutSeconds=p.TryGetProperty("timeoutSeconds",out var configuredTimeout)?configuredTimeout.GetInt32():300;
        if(timeoutSeconds<30||timeoutSeconds>1800)throw new InvalidOperationException("模型请求超时须在30–1800秒之间。");
        using var client=new HttpClient(handler){Timeout=TimeSpan.FromSeconds(timeoutSeconds)};
        string secret=Secret(p.GetProperty("encryptedKey").GetString()!).Trim();
        if(secret.StartsWith("Bearer ",StringComparison.OrdinalIgnoreCase))secret=secret[7..].Trim();
        string? line;
        while((line=Console.ReadLine())!=null){try{
            using var body=JsonDocument.Parse(line);if(body.RootElement.GetProperty("model").GetString()!=p.GetProperty("model").GetString())throw new InvalidOperationException("模型与当前配置不一致。");
            using var request=new HttpRequestMessage(HttpMethod.Post,endpoint);request.Headers.Authorization=new AuthenticationHeaderValue("Bearer",secret);request.Content=new StringContent(ModelCompatibility.Apply(endpoint,line),Encoding.UTF8,"application/json");
            using var response=await client.SendAsync(request);if(!response.IsSuccessStatusCode){Console.WriteLine(ApiErrors.Describe((int)response.StatusCode,await response.Content.ReadAsStringAsync(),secret));continue;}
            using var result=JsonDocument.Parse(await response.Content.ReadAsStringAsync());Console.WriteLine(JsonSerializer.Serialize(result.RootElement));
        }catch(Exception ex){Console.WriteLine(JsonSerializer.Serialize(new{error=ex is TaskCanceledException?$"模型响应等待超过{timeoutSeconds}秒；会话已保留，可输入“继续”恢复。未自动重试，此轮未执行GIS工具。":"模型网络或响应失败（"+ex.GetType().Name+"）："+ex.GetBaseException().Message.Replace(secret,"[redacted]")+"；未自动重试。",errorType=ex is TaskCanceledException?"timeout":"network_or_response",timeoutSeconds}));}}
    }
}

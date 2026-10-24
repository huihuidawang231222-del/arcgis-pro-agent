using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Text.Json;
namespace ProCodex;
public sealed class CodexView : UserControl {
    readonly TextBox executable=new(),model=new(),input=new(){AcceptsReturn=true,Height=75,TextWrapping=TextWrapping.Wrap},log=new(){IsReadOnly=true,AcceptsReturn=true,TextWrapping=TextWrapping.Wrap,VerticalScrollBarVisibility=ScrollBarVisibility.Auto};
    readonly TextBox preview=new(){IsReadOnly=true,AcceptsReturn=true,TextWrapping=TextWrapping.Wrap,Height=95};
    readonly Button execute=new(){Content="确认执行缓冲区",IsEnabled=false},cancel=new(){Content="取消",IsEnabled=false};
    readonly System.Collections.Generic.List<Button> actions=new();readonly MapTools maps=new();BufferPlan? pending;CancellationTokenSource? cancellation;bool busy;
    static string SettingsPath=>Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"ArcGISProCodex","connection.json");
    public CodexView(){var root=new DockPanel{Margin=new Thickness(8)};Content=root;var top=new StackPanel();DockPanel.SetDock(top,Dock.Top);root.Children.Add(top);
        var connection=new StackPanel();top.Children.Add(new Expander{Header="Codex 连接与登录设置",Content=connection,IsExpanded=false});
        connection.Children.Add(new TextBlock{Text="Codex CLI 路径"});connection.Children.Add(executable);var choose=Add(connection,"选择 codex.exe…");choose.Click+=(_,_)=>{var d=new Microsoft.Win32.OpenFileDialog{Filter="Codex|codex.exe"};if(d.ShowDialog()==true)executable.Text=d.FileName;};
        connection.Children.Add(new TextBlock{Text="模型ID（留空使用Codex默认模型）"});connection.Children.Add(model);
        var account=Add(connection,"检查订阅登录");account.Click+=async(_,_)=>await WithClient(async(c,t)=>Say(await c.Account(t)));
        var login=Add(connection,"使用 ChatGPT 登录…");login.Click+=async(_,_)=>await WithClient(async(c,t)=>{Say("请在浏览器完成ChatGPT登录。取消会关闭本插件发起的登录流程。");await c.Login(url=>Process.Start(new ProcessStartInfo(url){UseShellExecute=true}),t);Say(await c.Account(t));});
        var read=Add(top,"读取当前地图图层");read.Click+=async(_,_)=>await Work(async t=>{Invalidate();await maps.ReadAsync();Say(maps.Context);});
        top.Children.Add(new TextBlock{Text="只发送主动输入及读取的图层名称、类型、坐标系、选择数量。生成方案后需确认；结果保存到工程的CodexAnalysis目录。",TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,6,0,6)});
        var bottom=new StackPanel();DockPanel.SetDock(bottom,Dock.Bottom);root.Children.Add(bottom);bottom.Children.Add(input);var send=Add(bottom,"发送给 Codex");send.Click+=async(_,_)=>{
            string text=input.Text.Trim();if(text.Length==0||text.Length>12000){Say("请输入1至12000字。");return;}Invalidate();Say("你："+text);
            await WithClient(async(c,t)=>{var reply=await c.Ask(text,maps.Context,model.Text,t);Say("Codex："+reply.Message);if(reply.Plan!=null){await maps.Validate(reply.Plan);pending=reply.Plan;preview.Text=JsonSerializer.Serialize(pending)+"\n测地线缓冲；输入选择集与定义查询生效。确认后生成独立结果并添加地图。";}});
        };
        bottom.Children.Add(preview);bottom.Children.Add(execute);bottom.Children.Add(cancel);cancel.Click+=(_,_)=>cancellation?.Cancel();
        execute.Click+=async(_,_)=>{var plan=pending;if(plan==null)return;Invalidate();await Work(async t=>Say("缓冲区已完成并加入地图："+await maps.Buffer(plan,t)));};root.Children.Add(log);
        Unloaded+=(_,_)=>{cancellation?.Cancel();Invalidate();};LoadConnection();Say("独立Codex插件。先检查订阅登录，再读取地图；例如：为L1生成500米缓冲区。每次请求独立，不自动携带历史对话。");
    }
    Button Add(Panel panel,string text){var b=new Button{Content=text,Margin=new Thickness(0,3,0,0),Padding=new Thickness(6,3,6,3)};panel.Children.Add(b);actions.Add(b);return b;}
    void Say(string text){log.AppendText(text+"\n\n");log.ScrollToEnd();}
    void Invalidate(){pending=null;preview.Clear();execute.IsEnabled=false;}
    async Task Work(Func<CancellationToken,Task> action){if(busy)return;busy=true;actions.ForEach(b=>b.IsEnabled=false);execute.IsEnabled=false;executable.IsEnabled=model.IsEnabled=false;cancel.IsEnabled=true;cancellation=new CancellationTokenSource(TimeSpan.FromMinutes(5));
        try{await action(cancellation.Token);}catch(OperationCanceledException){Invalidate();Say("已取消或超时；若GIS已生成中间成果，会保留在结果目录。");}catch(Exception ex){Invalidate();Say("未完成："+ex.Message);}finally{cancellation.Dispose();cancellation=null;busy=false;actions.ForEach(b=>b.IsEnabled=true);executable.IsEnabled=model.IsEnabled=true;execute.IsEnabled=pending!=null;cancel.IsEnabled=false;}}
    Task WithClient(Func<CodexClient,CancellationToken,Task> action)=>Work(async t=>{SaveConnection();using var c=new CodexClient(executable.Text.Trim());using var registration=t.Register(c.Dispose);await c.Initialize(t);await action(c,t);});
    void SaveConnection(){Directory.CreateDirectory(Path.GetDirectoryName(SettingsPath)!);File.WriteAllText(SettingsPath,JsonSerializer.Serialize(new{executable=executable.Text.Trim(),model=model.Text.Trim()}));}
    void LoadConnection(){try{if(File.Exists(SettingsPath)){using var d=JsonDocument.Parse(File.ReadAllText(SettingsPath));executable.Text=d.RootElement.GetProperty("executable").GetString();model.Text=d.RootElement.GetProperty("model").GetString();return;}
        foreach(string dir in (Environment.GetEnvironmentVariable("PATH")??"").Split(Path.PathSeparator)){string p=Path.Combine(dir.Trim('"'),"codex.exe");if(File.Exists(p)){executable.Text=p;break;}}}catch(Exception ex){Say("连接设置未加载："+ex.Message);}}
}

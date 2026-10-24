using ArcGIS.Desktop.Framework;
using ArcGIS.Desktop.Framework.Contracts;
namespace ProCodex;
internal class CodexModule : Module {
    BridgeServer? bridge;
    protected override bool Initialize(){bridge=new BridgeServer();bridge.Start();return true;}
    protected override void Uninitialize(){bridge?.Dispose();}
    protected override bool CanUnload()=>true;
}
internal class ShowButton : Button {protected override void OnClick()=>FrameworkApplication.DockPaneManager.Find("ProCodex_Pane")?.Activate();}
internal class CodexPane : DockPane {protected CodexPane(){}}

using System.Windows;
namespace ListFXFlow.Setup;
public partial class App : Application {
    protected override async void OnStartup(StartupEventArgs e) {
        if(e.Args.Contains("--remove-old-login")) {
            StartupUri=null;ShutdownMode=ShutdownMode.OnExplicitShutdown;base.OnStartup(e);
            try { await Packaging.LegacyLoginRemoval.Remove();Shutdown(0); }
            catch(Exception error) { MessageBox.Show(error.Message,"移除旧版登录组件");Shutdown(1); }
            return;
        }
        base.OnStartup(e);
    }
}

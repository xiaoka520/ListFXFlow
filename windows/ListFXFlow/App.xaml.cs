using System.Windows;

namespace ListFXFlow;
public partial class App : Application
{
    private Mutex? instance;
    private EventWaitHandle? showEvent,exitEvent;
    private RegisteredWaitHandle? showWait,exitWait;
    protected override async void OnStartup(StartupEventArgs e) {
        base.OnStartup(e);
        if(e.Args.Contains("--touchpad-hid-diagnostics")) {
            ShutdownMode=ShutdownMode.OnExplicitShutdown;
            try {
                int seconds=0,index=Array.IndexOf(e.Args,"--capture-seconds");
                if(index>=0 && (index+1>=e.Args.Length || !int.TryParse(e.Args[index+1],out seconds)))throw new ArgumentException("采样秒数无效");
                string path=await PtpHidDiagnostics.Run(seconds);MessageBox.Show("诊断已保存：\n"+path,"原始 HID 触控板诊断");Shutdown(0);
            } catch(Exception error) {MessageBox.Show(error.Message,"原始 HID 触控板诊断");Shutdown(1);}
            return;
        }
        if(e.Args.Length==3 && e.Args[0]=="--touchpad-worker" && int.TryParse(e.Args[2],out int parent)) {
            ShutdownMode=ShutdownMode.OnExplicitShutdown;
            try {
                if(!OperatingSystem.IsWindowsVersionAtLeast(10,0,26100))throw new NotSupportedException("触控板 SDK 后端需要 Windows 11 24H2 或更新系统。");
                await TouchpadWorkerHost.Run(e.Args[1],parent,Dispatcher);Shutdown(0);
            }
            catch(Exception error) { TouchpadDiagnostics.Write("worker","fatal managed error",error);Shutdown(1); }
            return;
        }
        if(e.Args.Contains("--remove-old-login")) {
            try { await Packaging.LegacyLoginRemoval.Remove();Shutdown(0); }
            catch(Exception error) { MessageBox.Show(error.Message,"移除旧版登录组件");Shutdown(1); }
            return;
        }
        if(e.Args.Contains("--uninstall") || e.Args.Contains("--uninstall-worker")) { await Uninstall.Run(e.Args); Shutdown(); return; }
        if(e.Args.Contains("--quit")) { Signal("Exit"); Shutdown(); return; }
        try { await Packaging.LegacyLoginRemoval.RequestIfNeeded(); }
        catch(Exception error) { MessageBox.Show(error.Message,"移除旧版登录组件"); }
        instance=new Mutex(true,"Local\\ListFXFlow.Instance",out bool first);
        if(!first) { if(!e.Args.Contains("--background")) Signal("Show"); Shutdown(); return; }
        showEvent=new EventWaitHandle(false,EventResetMode.AutoReset,"Local\\ListFXFlow.Show");
        exitEvent=new EventWaitHandle(false,EventResetMode.AutoReset,"Local\\ListFXFlow.Exit");
        var window=new MainWindow(); MainWindow=window;
        showWait=ThreadPool.RegisterWaitForSingleObject(showEvent,(_,_)=>Dispatcher.BeginInvoke(window.ShowMainWindow),null,Timeout.Infinite,false);
        exitWait=ThreadPool.RegisterWaitForSingleObject(exitEvent,(_,_)=>Dispatcher.BeginInvoke(window.Quit),null,Timeout.Infinite,false);
        window.InitializeBackground(e.Args.Contains("--show"),e.Args.Contains("--background"));
    }
    internal static void Signal(string action) { try { using var signal=EventWaitHandle.OpenExisting("Local\\ListFXFlow."+action); signal.Set(); } catch(WaitHandleCannotBeOpenedException) {} }
    protected override void OnSessionEnding(SessionEndingCancelEventArgs e) { if(MainWindow is MainWindow window) window.AllowExit(); base.OnSessionEnding(e); }
    protected override void OnExit(ExitEventArgs e) {
        showWait?.Unregister(null); exitWait?.Unregister(null); showEvent?.Dispose(); exitEvent?.Dispose();
        if(instance!=null) { try { instance.ReleaseMutex(); } catch(ApplicationException) {} instance.Dispose(); }
        base.OnExit(e);
    }
}

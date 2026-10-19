using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using Microsoft.Win32;

namespace ListFXFlow;

public partial class MainWindow : Window
{
    private readonly InputEngine input=new();
    private readonly ClipboardBridge clipboard=new(System.Windows.Threading.Dispatcher.CurrentDispatcher);
    private volatile FlowClient? client;
    private Topology layout=new();
    private string activeDevice="windows";
    private bool connected,quitting,autoReconnect,suspended,sessionLocked;
    private bool gameLocked,canToggleGame;
    private DispatcherTimer? retryTimer;
    private DispatcherTimer? watchdog;
    private DateTime wakeGuardUntil;
    private TrayIcon? tray;
    private Point? dragStart;
    private double dragLeft,dragTop,diagramScale=0.1;
    private (string Side,double Offset)? pendingArrangement;
    private ConnectionPreferences preferences=ConnectionPreferences.Load();
    public MainWindow()
    {
        InitializeComponent();
        watchdog=new DispatcherTimer { Interval=TimeSpan.FromSeconds(5) };
        watchdog.Tick+=(_,_)=> {
            if(!autoReconnect || quitting || sessionLocked || client!=null || retryTimer!=null) return;
            suspended=false; ScheduleReconnect(1);
        };
        watchdog.Start();
        clipboard.OnStatus=status=>ClipboardText.Text=status;
        MacAddress.Text=preferences.MacAddress;
        input.OnTouchpadStatus=message=>PostUi(()=>TouchpadStatus.Text=message);
        input.OnGameProtection=(locked,status,canToggle)=>PostUi(()=> { gameLocked=locked;canToggleGame=canToggle;GameStatus.Text=status;GameLockButton.Content=locked ? "解除锁定":"锁定 Windows";GameLockButton.IsEnabled=canToggle;tray?.UpdateGameProtection(locked,canToggle); });
        AutomaticGameProtection.IsChecked=preferences.AutomaticGameProtection;
        input.SetAutomaticGameProtection(preferences.AutomaticGameProtection);
        SourceInitialized+=(_,_)=> { int dark=1; Native.DwmSetWindowAttribute(new System.Windows.Interop.WindowInteropHelper(this).Handle,20,ref dark,sizeof(int)); };
        layout.WindowsAspect=input.Width/input.Height; input.ApplyTopology(layout);
        input.Send=m => client?.Send(m);
        input.Emergency=() => PostUi(()=>Stop("紧急停止 · 已恢复本机控制"));
        input.OnFocus=active => PostUi(()=> { activeDevice=active; FocusText.Text="当前控制 · "+(active=="mac" ? "Mac" : "Windows"); UpdateDiagram(); });
        input.OnLayout=value => PostUi(()=> { layout=value; UpdateDiagram(); });
        input.OnControl=m => { if(m.Type=="reset") PostUi(()=>Stop("Mac 已停止共享 · 已恢复本机控制")); };
        input.InjectionFailed=() => PostUi(()=> { if(client!=null) Stop("Windows 拒绝了输入，请退出受保护界面；若要控制管理员窗口，需以同等权限运行。"); });
        Loaded+=(_,_) => UpdateDiagram();
        StateChanged+=(_,_) => { if(WindowState==WindowState.Minimized) Hide(); };
        Closing+=(_,e)=> { if(!quitting) { e.Cancel=true; Hide(); } };
        Closed+=(_,_) => { suspended=true; retryTimer?.Stop(); retryTimer=null; watchdog?.Stop(); watchdog=null; var closing=client; client=null; closing?.Stop(); clipboard.Stop(); input.Dispose(); tray?.Dispose(); SystemEvents.SessionSwitch-=SessionSwitch; SystemEvents.PowerModeChanged-=PowerChanged; SystemEvents.DisplaySettingsChanged-=DisplayChanged; };
        SystemEvents.SessionSwitch+=SessionSwitch; SystemEvents.PowerModeChanged+=PowerChanged; SystemEvents.DisplaySettingsChanged+=DisplayChanged;
    }
    public void InitializeBackground(bool show,bool background) {
        tray=new TrayIcon(Dispatcher,ShowMainWindow,()=>ConnectClick(this,new RoutedEventArgs()),Quit,input.ToggleGameProtection);
        tray.UpdateGameProtection(gameLocked,canToggleGame);
        RefreshAdapters(); tray.Update(StatusText.Text,false);
        bool configured=!string.IsNullOrWhiteSpace(preferences.LocalAddress);
        if(show || !background || !configured) ShowMainWindow();
        if(configured) { autoReconnect=true;ScheduleReconnect(1); }
    }
    public void ShowMainWindow() { Show(); if(WindowState==WindowState.Minimized) WindowState=WindowState.Normal; Activate(); }
    public void AllowExit() { quitting=true; }
    public void Quit() { quitting=true; Close(); Application.Current.Shutdown(); }
    private void RefreshAdapters() {
        if(client!=null) return;
        string selected=(Adapters.SelectedItem as BridgeAdapter)?.Address.ToString()??preferences.LocalAddress;
        var adapters=BridgeAdapter.All(); Adapters.ItemsSource=adapters;
        Adapters.SelectedItem=adapters.FirstOrDefault(a=>a.Address.ToString()==selected)
            ??adapters.FirstOrDefault(a=>!string.IsNullOrEmpty(preferences.AdapterName) && a.Name==preferences.AdapterName)
            ??adapters.FirstOrDefault(a=>a.Thunderbolt);
        if(adapters.Length==0) StatusText.Text="尚未发现有线网卡。请插入雷雳线并检查 USB4 / Thunderbolt 网络适配器。";
        else if(Adapters.SelectedItem==null) StatusText.Text="请选择雷雳网卡。程序只会通过你选择的有线接口连接。";
    }
    private async void ConnectClick(object sender,RoutedEventArgs e)
    {
        if(client!=null) { autoReconnect=false; Stop(); return; }
        bool retrying=autoReconnect;
        if(retrying) { suspended=false; wakeGuardUntil=DateTime.UtcNow.AddSeconds(8); RefreshAdapters(); }
        if(Adapters.SelectedItem is not BridgeAdapter adapter) { StatusText.Text="请先选择 Thunderbolt / USB4 网络适配器。"; if(retrying) ScheduleReconnect(); return; }
        string macAddress=MacAddress.Text.Trim();
        try { ConnectionTarget.Preferred(adapter,macAddress,preferences.LastMacAddress); } catch(ArgumentException error) { StatusText.Text=error.Message; if(retrying) ScheduleReconnect(); return; }
        try { input.Start(); } catch(Exception error) { StatusText.Text=error.Message; if(retrying) ScheduleReconnect(); return; }
        autoReconnect=true; CancelRetry();
        preferences=preferences with { MacAddress=macAddress,LocalAddress=adapter.Address.ToString(),AdapterName=adapter.Name };preferences.Save();
        var current=new FlowClient(); client=current;
        ConnectButton.Content="取消连接"; tray?.Update("正在连接远端…",true); Adapters.IsEnabled=false; MacAddress.IsEnabled=false;
        current.OnStatus=(ready,status) => {
            if(client!=current) return;
            input.Enabled=ready;
            if(ready) current.Send(new("display") { Width=input.Width,Height=input.Height,Features=input.Features });
            PostUi(()=> {
                if(client!=current) return;
                connected=ready; StatusText.Text=status; tray?.Update(status,client!=null);
                if(ready && current.PeerAddress is { } saved) { preferences=preferences with { LastMacAddress=saved.ToString() };preferences.Save(); }
                if(!ready) clipboard.Stop();
                ConnectButton.Content=ready ? "停止共享" : "取消连接";
                ConnectionBadge.Text=ready ? "已连接" : "连接中";
                ConnectionBadge.Foreground=new SolidColorBrush(ready ? Color.FromRgb(122,227,193) : Color.FromRgb(133,140,159));
                ConnectionDot.Fill=ready ? ConnectionBadge.Foreground : Brushes.Transparent; ConnectionDot.Stroke=ConnectionBadge.Foreground;
                if(ready && pendingArrangement is { } pending) { current.Send(new("layout") { Side=pending.Side,Offset=pending.Offset }); pendingArrangement=null; }
            });
        };
        current.OnMessage=m => { if(client!=current) return; if(m.Type=="clipboardReady") PostUi(()=> { if(client==current && current.PeerAddress is { } remote) clipboard.Connect(adapter.Address,remote,Convert.FromHexString(m.Nonce!)); }); else input.Receive(m); };
        await Task.Run(()=>current.Connect(adapter,macAddress,preferences.LastMacAddress));
        if(client==current) { client=null; connected=false; input.Stop(); tray?.Update(StatusText.Text,false); ConnectButton.Content="连接 Mac"; ConnectionBadge.Text="未连接"; ConnectionDot.Fill=Brushes.Transparent; Adapters.IsEnabled=true; MacAddress.IsEnabled=true; }
        if(autoReconnect && !quitting) ScheduleReconnect();
    }
    private void RetryTouchpadClick(object sender,RoutedEventArgs e)=>input.RetryTouchpadSuppression();
    private void GameLockClick(object sender,RoutedEventArgs e)=>input.ToggleGameProtection();
    private void AutomaticGameProtectionChanged(object sender,RoutedEventArgs e) {
        if(AutomaticGameProtection==null)return;
        bool value=AutomaticGameProtection.IsChecked==true;
        preferences=preferences with {AutomaticGameProtection=value};preferences.Save();input.SetAutomaticGameProtection(value);
    }
    private void PostUi(Action action) { if(!Dispatcher.HasShutdownStarted) Dispatcher.BeginInvoke(action); }
    private void Stop(string reason="共享已停止 · 本机控制", bool keepReconnect=false) {
        if(!keepReconnect) autoReconnect=false;
        CancelRetry();
        clipboard.Stop();
        var old=client; client=null; old?.Stop(reason);
        input.Stop(); input.Enabled=false; connected=false;
        StatusText.Text=reason; tray?.Update(reason,false);
        ConnectButton.Content="连接 Mac"; ConnectionBadge.Text="未连接";
        ConnectionBadge.Foreground=new SolidColorBrush(Color.FromRgb(133,140,159));
        ConnectionDot.Fill=Brushes.Transparent; ConnectionDot.Stroke=ConnectionBadge.Foreground;
        Adapters.IsEnabled=true; MacAddress.IsEnabled=true;
        if(autoReconnect && !quitting && !sessionLocked) ScheduleReconnect();
    }
    private void CancelRetry() { retryTimer?.Stop(); retryTimer=null; }
    private void ScheduleReconnect(int seconds=2) {
        if(!autoReconnect || quitting || sessionLocked || client!=null) return;
        CancelRetry();
        StatusText.Text=suspended ? "系统正在休眠 · 唤醒后自动重连" : "等待 Mac 恢复 · 将自动重连";
        tray?.Update(StatusText.Text,false);
        retryTimer=new DispatcherTimer { Interval=TimeSpan.FromSeconds(seconds) };
        retryTimer.Tick+=(_,_)=> {
            CancelRetry();
            if(!autoReconnect || quitting || client!=null) return;
            suspended=false; wakeGuardUntil=DateTime.UtcNow.AddSeconds(8);
            ConnectClick(this,new RoutedEventArgs());
        };
        retryTimer.Start();
    }
    private void Arrange(string side,double offset=0) {
        if(!connected) { pendingArrangement=(side,Math.Clamp(offset,-900,900)); layout.Side=side; layout.Offset=Math.Clamp(offset,-900,900); input.ApplyTopology(layout); UpdateDiagram(); return; }
        client?.Send(new("layout") { Side=side,Offset=Math.Clamp(offset,-900,900) });
    }
    private void UpdateDiagram()
    {
        if(Diagram==null || Diagram.ActualWidth<1) return;
        var mac=layout.Mac; var win=layout.Windows;
        double minX=Math.Min(mac.X,win.X),maxX=Math.Max(mac.MaxX,win.MaxX),minY=Math.Min(mac.Y,win.Y),maxY=Math.Max(mac.MaxY,win.MaxY);
        diagramScale=Math.Min((Diagram.ActualWidth-76)/(maxX-minX),(Diagram.ActualHeight-50)/(maxY-minY));
        double ox=(Diagram.ActualWidth-(maxX-minX)*diagramScale)/2-minX*diagramScale,oy=(Diagram.ActualHeight-(maxY-minY)*diagramScale)/2-minY*diagramScale;
        void Place(Border card,ScreenRect r,bool active) {
            card.Width=Math.Max(50,r.Width*diagramScale-6); card.Height=Math.Max(50,r.Height*diagramScale-6);
            Canvas.SetLeft(card,ox+r.X*diagramScale+3); Canvas.SetTop(card,oy+r.Y*diagramScale+3);
            card.BorderBrush=new SolidColorBrush(active ? Color.FromRgb(146,144,255) : Color.FromRgb(70,75,102));
            card.Background=new SolidColorBrush(active ? Color.FromRgb(48,44,85) : Color.FromRgb(29,33,47));
        }
        Place(MacDevice,mac,activeDevice=="mac"); Place(WindowsDevice,win,activeDevice=="windows");
    }
    private void DragStart(object sender,MouseButtonEventArgs e) { dragStart=e.GetPosition(Diagram); dragLeft=Canvas.GetLeft(WindowsDevice); dragTop=Canvas.GetTop(WindowsDevice); WindowsDevice.CaptureMouse(); }
    private void DragMove(object sender,MouseEventArgs e) { if(dragStart is not Point start) return; var p=e.GetPosition(Diagram); Canvas.SetLeft(WindowsDevice,dragLeft+p.X-start.X); Canvas.SetTop(WindowsDevice,dragTop+p.Y-start.Y); }
    private void DragEnd(object sender,MouseButtonEventArgs e) {
        if(dragStart is not Point start) return;
        var p=e.GetPosition(Diagram); var mac=layout.Mac; var win=layout.Windows;
        double cx=win.X+win.Width/2+(p.X-start.X)/diagramScale,cy=win.Y+500+(p.Y-start.Y)/diagramScale;
        double rx=(cx-mac.Width/2)/(mac.Width/2+win.Width/2),ry=(cy-500)/1000;
        string side=Math.Abs(rx)>=Math.Abs(ry) ? (rx<0 ? "left" : "right") : (ry<0 ? "top" : "bottom");
        dragStart=null; WindowsDevice.ReleaseMouseCapture(); Arrange(side,side is "left" or "right" ? cy-500 : cx-win.Width/2); UpdateDiagram();
    }
    private void DiagramSizeChanged(object sender,SizeChangedEventArgs e) { if(dragStart==null) UpdateDiagram(); }
    private void RefreshClick(object sender,RoutedEventArgs e) => RefreshAdapters();
    private void SessionSwitch(object sender,SessionSwitchEventArgs e) => Dispatcher.Invoke(()=> {
        if(e.Reason==SessionSwitchReason.SessionLock) {
            sessionLocked=true;
            Stop("系统已锁定 · 解锁后自动恢复共享", keepReconnect:autoReconnect);
        } else if(e.Reason==SessionSwitchReason.SessionUnlock) {
            sessionLocked=false;
            if(autoReconnect && client==null) { suspended=false; ScheduleReconnect(1); }
        } else if(e.Reason is SessionSwitchReason.SessionLogoff or SessionSwitchReason.ConsoleDisconnect) {
            sessionLocked=true;
            Stop("系统已锁定 · 共享已停止");
        }
    });
    private void PowerChanged(object sender,PowerModeChangedEventArgs e) {
        Dispatcher.Invoke(()=> {
            if(e.Mode==PowerModes.Suspend) {
                suspended=true; CancelRetry();
                if(client!=null) Stop("系统正在休眠 · 唤醒后自动重连", keepReconnect:true);
                else if(autoReconnect) { StatusText.Text="系统正在休眠 · 唤醒后自动重连"; tray?.Update(StatusText.Text,false); }
            } else if(e.Mode==PowerModes.Resume) {
                suspended=false; wakeGuardUntil=DateTime.UtcNow.AddSeconds(8); RefreshAdapters();
                if(autoReconnect && !quitting) ScheduleReconnect(1);
            }
        });
    }
    private void DisplayChanged(object? sender,EventArgs e) => Dispatcher.Invoke(()=> {
        layout.WindowsAspect=input.Width/input.Height; input.ApplyTopology(layout); UpdateDiagram();
        if(suspended || DateTime.UtcNow < wakeGuardUntil) {
            if(connected) client?.Send(new("display") { Width=input.Width,Height=input.Height,Features=input.Features });
            return;
        }
        Stop("显示器配置已变化 · 正在重新连接", keepReconnect:autoReconnect);
    });
}

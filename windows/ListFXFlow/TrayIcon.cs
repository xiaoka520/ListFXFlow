using System.Windows.Threading;
using Forms=System.Windows.Forms;
using Drawing=System.Drawing;

namespace ListFXFlow;

internal sealed class TrayIcon : IDisposable
{
    private readonly Forms.NotifyIcon icon;
    private readonly Forms.ContextMenuStrip menu=new();
    private readonly Forms.ToolStripMenuItem state=new("ListFXFlow"),toggle=new("连接远端"),gameLock=new("锁定 Windows · Ctrl + Alt + L");
    private string currentStatus="ListFXFlow";
    private bool gameLocked;
    private readonly Drawing.Icon? ownedIcon;
    public TrayIcon(Dispatcher dispatcher,Action show,Action connect,Action exit,Action toggleGame) {
        menu.BackColor=Drawing.Color.FromArgb(24,28,41); menu.ForeColor=Drawing.Color.FromArgb(231,234,245);
        menu.Renderer=new Forms.ToolStripProfessionalRenderer(new Colors()); menu.ShowImageMargin=false;
        void Invoke(Action action)=>dispatcher.BeginInvoke(action);
        state.Enabled=false; menu.Items.Add(state); menu.Items.Add(new Forms.ToolStripSeparator());
        menu.Items.Add("打开主界面…",null,(_,_)=>Invoke(show));
        toggle.Click+=(_,_)=>Invoke(connect); menu.Items.Add(toggle); menu.Items.Add(new Forms.ToolStripSeparator());
        gameLock.Enabled=false;gameLock.Click+=(_,_)=>Invoke(toggleGame);menu.Items.Add(gameLock);menu.Items.Add(new Forms.ToolStripSeparator());
        menu.Items.Add("退出 ListFXFlow",null,(_,_)=>Invoke(exit));
        ownedIcon=Drawing.Icon.ExtractAssociatedIcon(Environment.ProcessPath!);
        icon=new Forms.NotifyIcon { Icon=ownedIcon??Drawing.SystemIcons.Application,Text="ListFXFlow",ContextMenuStrip=menu,Visible=true };
        icon.DoubleClick+=(_,_)=>Invoke(show);
    }
    public void Update(string status,bool running) {
        state.Text=status;currentStatus=status;UpdateTooltip();
        toggle.Text=running ? "停止共享" : "连接远端";
    }
    public void UpdateGameProtection(bool locked,bool canToggle) {
        gameLocked=locked;gameLock.Text=locked ? "解除 Windows 游戏锁定 · Ctrl + Alt + L":"锁定 Windows · Ctrl + Alt + L";
        gameLock.Enabled=canToggle;UpdateTooltip();
    }
    private void UpdateTooltip() { string value="ListFXFlow · "+(gameLocked ? "游戏锁定 · ":"")+currentStatus;icon.Text=value[..Math.Min(63,value.Length)]; }
    public void Dispose() { icon.Visible=false; icon.Dispose(); menu.Dispose(); ownedIcon?.Dispose(); }
    private sealed class Colors : Forms.ProfessionalColorTable {
        private static Drawing.Color Surface=>Drawing.Color.FromArgb(24,28,41);
        public override Drawing.Color ToolStripDropDownBackground=>Surface;
        public override Drawing.Color MenuItemSelected=>Drawing.Color.FromArgb(55,48,82);
        public override Drawing.Color MenuItemBorder=>Drawing.Color.FromArgb(91,81,133);
        public override Drawing.Color MenuBorder=>Drawing.Color.FromArgb(62,68,91);
        public override Drawing.Color ImageMarginGradientBegin=>Surface;
        public override Drawing.Color ImageMarginGradientMiddle=>Surface;
        public override Drawing.Color ImageMarginGradientEnd=>Surface;
    }
}

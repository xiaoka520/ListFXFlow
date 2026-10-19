using System.Diagnostics;
using System.IO;
using System.Text;
using System.Windows;
using Microsoft.Win32;
using ListFXFlow.Packaging;

namespace ListFXFlow;
internal static class Uninstall
{
    internal static async Task Run(string[] args) {
        try {
            if(!File.Exists(InstallPaths.Marker)) { MessageBox.Show("当前不是已安装版本。便携版退出后直接移除程序文件即可。","ListFXFlow"); return; }
            if(!args.Contains("--uninstall-worker")) {
                string worker=Path.Combine(Path.GetTempPath(),"ListFXFlow-Uninstall-"+Guid.NewGuid().ToString("N")+".exe");
                File.Copy(Environment.ProcessPath!,worker);
                var start=new ProcessStartInfo(worker) { UseShellExecute=true }; start.ArgumentList.Add("--uninstall-worker"); Process.Start(start); return;
            }
            // Only a temporary copy may delete the fixed per-user installation.
            string own=Path.GetFullPath(Environment.ProcessPath!);
            if(!own.StartsWith(Path.GetFullPath(Path.GetTempPath()),StringComparison.OrdinalIgnoreCase) || !Path.GetFileName(own).StartsWith("ListFXFlow-Uninstall-",StringComparison.Ordinal)) throw new IOException("无效的卸载入口");
            if(MessageBox.Show("卸载 ListFXFlow？连接设置和已接收的剪贴板文件会保留。","卸载 ListFXFlow",MessageBoxButton.YesNo,MessageBoxImage.Question)!=MessageBoxResult.Yes) { DeleteWorker(own); return; }
            await LegacyLoginRemoval.RequestIfNeeded();
            await InstallPaths.CloseRunningApps();
            foreach(string file in new[]{InstallPaths.Executable,InstallPaths.Marker,InstallPaths.StartShortcut,InstallPaths.DesktopShortcut}) if(File.Exists(file)) File.Delete(file);
            if(Directory.Exists(InstallPaths.Directory) && !Directory.EnumerateFileSystemEntries(InstallPaths.Directory).Any()) Directory.Delete(InstallPaths.Directory);
            Registry.CurrentUser.DeleteSubKeyTree(InstallPaths.RegistryPath,false);
            MessageBox.Show("ListFXFlow 已卸载。","ListFXFlow"); DeleteWorker(own);
        } catch(Exception error) { MessageBox.Show("卸载未完成："+error.Message,"ListFXFlow",MessageBoxButton.OK,MessageBoxImage.Error); }
    }
    private static void DeleteWorker(string path) {
        string literal=path.Replace("'","''");
        string script=$"Wait-Process -Id {Environment.ProcessId} -ErrorAction SilentlyContinue; Remove-Item -LiteralPath '{literal}' -Force -ErrorAction SilentlyContinue";
        var start=new ProcessStartInfo(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System),"WindowsPowerShell","v1.0","powershell.exe")) { UseShellExecute=false,CreateNoWindow=true };
        start.ArgumentList.Add("-NoProfile"); start.ArgumentList.Add("-NonInteractive"); start.ArgumentList.Add("-EncodedCommand"); start.ArgumentList.Add(Convert.ToBase64String(Encoding.Unicode.GetBytes(script)));
        Process.Start(start);
    }
}

using Microsoft.Win32;
using System.Diagnostics;
using System.IO;
using System.Text.Json;

namespace ListFXFlow.Packaging;

internal static class InstallPaths
{
    internal const string RegistryPath=@"Software\Microsoft\Windows\CurrentVersion\Uninstall\ListFXFlow";
    internal static string Directory=>Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"Programs","ListFXFlow");
    internal static string Executable=>Path.Combine(Directory,"ListFXFlow.exe");
    internal static string Marker=>Path.Combine(Directory,"install.json");
    internal static string StartShortcut=>Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),"Microsoft","Windows","Start Menu","Programs","ListFXFlow.lnk");
    internal static string DesktopShortcut=>Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory),"ListFXFlow.lnk");
    internal static void Shortcut(string path) {
        System.IO.Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        dynamic shell=Activator.CreateInstance(Type.GetTypeFromProgID("WScript.Shell")!)!;
        dynamic shortcut=shell.CreateShortcut(path); shortcut.TargetPath=Executable; shortcut.Arguments="--show"; shortcut.WorkingDirectory=Directory; shortcut.IconLocation=Executable+",0"; shortcut.Description="ListFXFlow 雷雳键鼠与剪贴板共享"; shortcut.Save();
        System.Runtime.InteropServices.Marshal.FinalReleaseComObject(shortcut); System.Runtime.InteropServices.Marshal.FinalReleaseComObject(shell);
    }
    internal static void Register(string version,long bytes) {
        using var key=Registry.CurrentUser.CreateSubKey(RegistryPath);
        key.SetValue("DisplayName","ListFXFlow"); key.SetValue("DisplayVersion",version); key.SetValue("Publisher","ListFXFlow");
        key.SetValue("InstallLocation",Directory); key.SetValue("DisplayIcon",Executable); key.SetValue("UninstallString","\""+Executable+"\" --uninstall");
        key.SetValue("NoModify",1,RegistryValueKind.DWord); key.SetValue("NoRepair",1,RegistryValueKind.DWord); key.SetValue("EstimatedSize",(int)(bytes/1024),RegistryValueKind.DWord);
        File.WriteAllText(Marker,JsonSerializer.Serialize(new { application="ListFXFlow",version }));
    }
    internal static void RequestExit() { try { using var signal=EventWaitHandle.OpenExisting(@"Local\ListFXFlow.Exit"); signal.Set(); } catch(WaitHandleCannotBeOpenedException) {} }
    internal static async Task CloseRunningApps() {
        RequestExit();
        foreach(var process in Process.GetProcessesByName("ListFXFlow")) {
            using(process) {
                if(process.Id==Environment.ProcessId || process.SessionId!=Process.GetCurrentProcess().SessionId) continue;
                try {
                    string? path=process.MainModule?.FileName;
                    if(path==null || !Path.GetFullPath(path).Equals(Path.GetFullPath(Executable),StringComparison.OrdinalIgnoreCase)) continue;
                    process.CloseMainWindow();
                    using var timeout=new CancellationTokenSource(TimeSpan.FromSeconds(5));
                    await process.WaitForExitAsync(timeout.Token);
                } catch(OperationCanceledException) { throw new IOException("请先从系统托盘退出正在运行的 ListFXFlow，再重试。"); }
                catch(InvalidOperationException) { }
                catch(System.ComponentModel.Win32Exception) { /* Another protected process may have the same executable name. */ }
            }
        }
    }
}

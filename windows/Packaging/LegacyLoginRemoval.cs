using Microsoft.Win32;
using System.Diagnostics;
using System.IO;
using System.Security.Principal;
using System.Text.Json;

namespace ListFXFlow.Packaging;

/// Migration only. No service installation, desktop access or input worker remains.
internal static class LegacyLoginRemoval {
    private const string ServiceName="ListFXFlowLogin";
    private static string Root=>Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),"ListFXFlow Login Support");
    private static bool Installed {
        get { using var key=Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Services\"+ServiceName);return key!=null || Directory.Exists(Root); }
    }
    internal static async Task RequestIfNeeded() {
        if(!Installed)return;
        var info=new ProcessStartInfo(Environment.ProcessPath!) { UseShellExecute=true,Verb="runas" };
        info.ArgumentList.Add("--remove-old-login");
        using var process=Process.Start(info)??throw new IOException("无法启动旧版登录组件清理。");
        await process.WaitForExitAsync();
        if(process.ExitCode!=0)throw new IOException("旧版登录组件尚未移除，请完成管理员授权后重试。");
    }
    private static async Task<int> Sc(params string[] args) {
        var info=new ProcessStartInfo(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System),"sc.exe")) { UseShellExecute=false,CreateNoWindow=true,RedirectStandardOutput=true,RedirectStandardError=true };
        foreach(var arg in args)info.ArgumentList.Add(arg);
        using var process=Process.Start(info)!;
        var output=process.StandardOutput.ReadToEndAsync();var error=process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();await output;await error;return process.ExitCode;
    }
    internal static async Task Remove() {
        using var user=WindowsIdentity.GetCurrent();
        if(!new WindowsPrincipal(user).IsInRole(WindowsBuiltInRole.Administrator))throw new UnauthorizedAccessException("移除旧版系统组件需要管理员授权。");
        int stopped=await Sc("stop",ServiceName);
        if(stopped is not (0 or 1060 or 1062))throw new IOException("旧版服务停止失败："+stopped);
        string executable=Path.Combine(Root,"ListFXFlow.exe");
        bool running=true;
        for(int attempt=0;attempt<75 && running;attempt++) {
            running=false;
            foreach(var process in Process.GetProcessesByName("ListFXFlow"))using(process)try {
                if(string.Equals(process.MainModule?.FileName,executable,StringComparison.OrdinalIgnoreCase))running=true;
            }catch(InvalidOperationException){}catch(System.ComponentModel.Win32Exception){}
            if(running)await Task.Delay(200);
        }
        if(running)throw new IOException("旧版登录组件尚未退出，请稍后重试。");
        int deleted=await Sc("delete",ServiceName);
        if(deleted is not (0 or 1060 or 1072))throw new IOException("旧版服务移除失败："+deleted);
        string config=Path.Combine(Root,"connection.json");
        if(File.Exists(config))try {
            using var json=JsonDocument.Parse(File.ReadAllBytes(config));
            string? sid=json.RootElement.GetProperty("OwnerSid").GetString();
            string? app=json.RootElement.GetProperty("UserExecutable").GetString();
            if(sid!=null && app!=null) {
                _=new SecurityIdentifier(sid);
                using var run=Registry.Users.OpenSubKey(sid+@"\Software\Microsoft\Windows\CurrentVersion\Run",true);
                if(run?.GetValue("ListFXFlow") is string value && value=="\""+app+"\" --background")run.DeleteValue("ListFXFlow",false);
            }
        }catch(JsonException){}catch(KeyNotFoundException){}catch(ArgumentException){}
        if(Directory.Exists(Root))Directory.Delete(Root,true);
    }
}

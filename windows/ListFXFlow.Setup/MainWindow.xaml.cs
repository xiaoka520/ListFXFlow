using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using System.Windows;
using ListFXFlow.Packaging;

namespace ListFXFlow.Setup;
public partial class MainWindow : Window
{
    private bool installing,finished;
    public MainWindow() { InitializeComponent(); SourceInitialized+=(_,_)=> { int dark=1; DwmSetWindowAttribute(new System.Windows.Interop.WindowInteropHelper(this).Handle,20,ref dark,sizeof(int)); }; Location.Text=InstallPaths.Directory; Closing+=(_,e)=> { if(installing) e.Cancel=true; }; }
    private void CloseClick(object sender,RoutedEventArgs e) { if(!installing) Close(); }
    private async void InstallClick(object sender,RoutedEventArgs e) {
        if(finished) { Close(); return; }
        installing=true; InstallButton.IsEnabled=false; DesktopShortcut.IsEnabled=RunAfter.IsEnabled=false;
        bool desktop=DesktopShortcut.IsChecked==true;
        Status.Text="正在关闭旧版并校验安装文件…"; Progress.IsIndeterminate=true;
        try {
            await LegacyLoginRemoval.RequestIfNeeded();
            await InstallPaths.CloseRunningApps();
            var progress=new Progress<double>(value=> { Progress.IsIndeterminate=false; Progress.Value=value; Status.Text="正在安装… "+(int)value+"%"; });
            // Shortcut creation uses COM on the UI STA after the file operation completes.
            var manifest=await Task.Run(()=>Install(progress));
            InstallPaths.Shortcut(InstallPaths.StartShortcut); if(desktop) InstallPaths.Shortcut(InstallPaths.DesktopShortcut);
            InstallPaths.Register(manifest.Version,manifest.Size);
            string backup=InstallPaths.Executable+".previous"; if(File.Exists(backup)) File.Delete(backup);
            Progress.IsIndeterminate=false; Progress.Value=100; Status.Text="安装完成。可从系统托盘打开主界面或退出。"; finished=true; InstallButton.Content="完成";
            if(RunAfter.IsChecked==true) Process.Start(new ProcessStartInfo(InstallPaths.Executable) { UseShellExecute=true,Arguments="--background" });
        } catch(Exception error) {
            string backup=InstallPaths.Executable+".previous";
            if(File.Exists(backup)) { try { File.Move(backup,InstallPaths.Executable,true); } catch(IOException) {} }
            Progress.IsIndeterminate=false; Progress.Value=0; Status.Text="安装未完成："+error.Message; DesktopShortcut.IsEnabled=RunAfter.IsEnabled=true;
        } finally { installing=false; InstallButton.IsEnabled=true; }
    }
    private static Payload Install(IProgress<double> progress) {
        var assembly=Assembly.GetExecutingAssembly();
        using var metadata=assembly.GetManifestResourceStream("ListFXFlow.Payload.json")!;
        var manifest=JsonSerializer.Deserialize<Payload>(metadata,new JsonSerializerOptions { PropertyNameCaseInsensitive=true })??throw new InvalidDataException("缺少安装清单");
        using var payload=assembly.GetManifestResourceStream("ListFXFlow.Payload.zip")!; using var zip=new ZipArchive(payload,ZipArchiveMode.Read);
        var entry=zip.Entries.FirstOrDefault(other=>Path.GetFileName(other.FullName).Equals("ListFXFlow.exe",StringComparison.OrdinalIgnoreCase)) ?? throw new InvalidDataException("安装文件不完整");
        if(zip.Entries.Any(other=>other!=entry && !other.FullName.EndsWith("/",StringComparison.Ordinal)) || entry.Length!=manifest.Size) throw new InvalidDataException("安装文件不完整");
        Directory.CreateDirectory(InstallPaths.Directory);
        string staged=Path.Combine(InstallPaths.Directory,"install-"+Guid.NewGuid().ToString("N")+".tmp");
        try {
            using(var source=entry.Open()) using(var target=new FileStream(staged,FileMode.CreateNew,FileAccess.Write,FileShare.None)) using(var hash=IncrementalHash.CreateHash(HashAlgorithmName.SHA256)) {
                byte[] buffer=new byte[262144]; int count; long total=0;
                while((count=source.Read(buffer))>0) { target.Write(buffer,0,count); hash.AppendData(buffer,0,count); total+=count; progress.Report(total*100.0/manifest.Size); }
                if(!Convert.ToHexString(hash.GetHashAndReset()).Equals(manifest.Sha256,StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("安装文件校验失败");
            }
            string backup=InstallPaths.Executable+".previous";
            if(File.Exists(backup)) File.Delete(backup);
            if(File.Exists(InstallPaths.Executable)) File.Move(InstallPaths.Executable,backup);
            File.Move(staged,InstallPaths.Executable); return manifest;
        } finally { if(File.Exists(staged)) File.Delete(staged); }
    }
    [System.Runtime.InteropServices.DllImport("dwmapi.dll")] private static extern int DwmSetWindowAttribute(IntPtr window,int attribute,ref int value,int size);
    private sealed record Payload(string Version,long Size,string Sha256);
}

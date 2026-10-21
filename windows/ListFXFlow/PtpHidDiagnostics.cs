using System.ComponentModel;
using System.IO;
using System.Text.Json;

namespace ListFXFlow;

internal static class PtpHidDiagnostics {
    internal static async Task<string> Run(int seconds=0) {
        if(seconds is <0 or >30)throw new ArgumentOutOfRangeException(nameof(seconds));
        var directory=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"ListFXFlow","Diagnostics");Directory.CreateDirectory(directory);
        string path=Path.Combine(directory,"touchpad-hid.json");
        object[] physical=[];string? inventoryError=null,captureError=null;var observers=new List<object>();
        try {physical=PtpHidNative.PhysicalInventory();}catch(Exception e) when(e is Win32Exception or IOException){inventoryError=e.Message;}
        try {
            foreach(var device in PtpHidNative.Devices(PtpHidProtocol.Interface)) {
                using var handle=PtpHidNative.CreateFile(device.Path,0x80000000,0,0,3,0,0);
                if(handle.IsInvalid)observers.Add(new {Error=new Win32Exception(System.Runtime.InteropServices.Marshal.GetLastWin32Error()).Message});
                else observers.Add(PtpHidProtocol.Metadata(PtpHidNative.Ioctl(handle,PtpHidProtocol.MetadataIoctl,64)));
            }
        } catch(Exception e) when(e is Win32Exception or IOException) {observers.Add(new {Error=e.Message});}
        long frames=0;int maxContacts=0;string status="未请求采样";
        if(seconds>0) {
            using var capture=PtpHidCapture.TryOpen(()=>{},error=>captureError=error,out status);
            if(capture!=null) {
                long until=Environment.TickCount64+seconds*1000;
                while(Environment.TickCount64<until) {
                    while(capture.TryRead(out var frame)) {frames++;maxContacts=Math.Max(maxContacts,frame!.Contacts.Length);}
                    await Task.Delay(10);
                }
            }
        }
        var result=new {Schema=1,Build=TouchpadDiagnostics.Runtime,CreatedAt=DateTimeOffset.UtcNow,PhysicalTouchpads=physical,InventoryError=inventoryError,
            Observers=observers,Sampling=new {Seconds=seconds,Frames=frames,MaximumContacts=maxContacts,Status=status,Error=captureError},
            ObserveOnly=true,Note="未修改驱动、设备配置或系统输入；未记录触点坐标、按键或剪贴板内容。"};
        await File.WriteAllTextAsync(path,JsonSerializer.Serialize(result,new JsonSerializerOptions{WriteIndented=true}));return path;
    }
}

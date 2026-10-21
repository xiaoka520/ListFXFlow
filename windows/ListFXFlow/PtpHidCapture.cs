using System.ComponentModel;
using System.IO;
using System.Threading.Channels;
using Microsoft.Win32.SafeHandles;

namespace ListFXFlow;

internal sealed record PtpHidFrame(TouchpadContact[] Contacts,double Time);
/// Optional driver reader. No native read/parse call occurs on the input dispatcher.
/// The driver is observe-only; SDK ownership still suppresses local 3+ gestures.
internal sealed class PtpHidCapture : IDisposable {
    private readonly CancellationTokenSource stop=new();
    private readonly Channel<PtpHidFrame> frames=Channel.CreateBounded<PtpHidFrame>(new BoundedChannelOptions(64) {FullMode=BoundedChannelFullMode.Wait,SingleWriter=true,SingleReader=true});
    private readonly Action notify;
    private readonly Action<string> failed;
    private readonly SafeFileHandle handle;
    private readonly PtpHidDecoder decoder;
    private readonly PtpHidMetadata metadata;
    private readonly Task pump;
    private int disposed;
    internal Task Completion=>pump;
    internal string Description=>$"原始 HID 触点已接入 · VID {metadata.Vendor:X4} / PID {metadata.Product:X4}";
    private PtpHidCapture(SafeFileHandle handle,PtpHidMetadata metadata,PtpHidDecoder decoder,Action notify,Action<string> failed) {
        this.handle=handle;this.metadata=metadata;this.decoder=decoder;this.notify=notify;this.failed=failed;
        pump=Task.Run(Read);
    }
    internal static PtpHidCapture? TryOpen(Action notify,Action<string> failed,out string status) {
        status="未安装或未授权原始 HID 观察驱动，使用 SDK 触点";
        if(!OperatingSystem.IsWindows())return null;
        try {
            var devices=PtpHidNative.Devices(PtpHidProtocol.Interface);
            // Never pick an arbitrary touchpad if several observers are present.
            if(devices.Length>1) {status="发现多个原始 HID 设备，使用 SDK 触点";return null;}
            if(devices.Length==0)return null;
            var handle=PtpHidNative.CreateFile(devices[0].Path,0x80000000,0,0,3,0,0);
            if(handle.IsInvalid) {int error=System.Runtime.InteropServices.Marshal.GetLastWin32Error();handle.Dispose();throw new Win32Exception(error);}
            try {
                var meta=PtpHidProtocol.Metadata(PtpHidNative.Ioctl(handle,PtpHidProtocol.MetadataIoctl,64));
                if(!meta.Validated)throw new InvalidDataException("旁路驱动没有确认 PTP 设备");
                var bytes=PtpHidNative.Ioctl(handle,PtpHidProtocol.DescriptorIoctl,meta.DescriptorLength);
                if(bytes.Length!=meta.DescriptorLength)throw new InvalidDataException("旁路描述信息不完整");
                var capture=new PtpHidCapture(handle,meta,new PtpHidDecoder(bytes),notify,failed);status=capture.Description;return capture;
            } catch {handle.Dispose();throw;}
        } catch(Exception error) when(error is IOException or Win32Exception or NotSupportedException or ArgumentException) {
            status="原始 HID 不可用，使用 SDK 触点："+error.Message;return null;
        }
    }
    internal bool TryRead(out PtpHidFrame? frame)=>frames.Reader.TryRead(out frame);
    internal bool HasPending=>frames.Reader.TryPeek(out _);
    private async Task Read() {
        var assembler=new PtpHidFrames();ulong? sequence=null;ulong fragments=0,lastCounter=0;
        try {
            while(!stop.IsCancellationRequested) {
                int count=0;
                for(;count<64 && !stop.IsCancellationRequested;count++) {
                    var bytes=PtpHidNative.Ioctl(handle,PtpHidProtocol.ReportIoctl,PtpHidProtocol.HeaderLength+PtpHidProtocol.ReportLimit,allowEmpty:true);
                    if(bytes.Length==0)break;
                    var report=PtpHidProtocol.Report(bytes);
                    if(sequence is {} previous && report.Sequence!=previous+1 || report.Counter<lastCounter)throw new InvalidDataException("原始 HID 报告丢失或时间倒退");
                    sequence=report.Sequence;lastCounter=report.Counter;
                    var fragment=decoder.Decode(report.Bytes);if(fragment==null)continue;
                    if(assembler.Push(++fragments,fragment) is {} contacts) {
                        await frames.Writer.WriteAsync(new(contacts,(double)report.Counter/metadata.Frequency),stop.Token);notify();
                    }
                }
                await Task.Delay(count==64 ? 1:4,stop.Token);
            }
        } catch(OperationCanceledException) when(stop.IsCancellationRequested) { }
        catch(Exception error) when(error is IOException or Win32Exception or NotSupportedException or ArgumentException or OverflowException) {
            if(!stop.IsCancellationRequested)failed("原始 HID 读取暂停，已回退 SDK："+error.Message);
        } finally {frames.Writer.TryComplete();decoder.Dispose();handle.Dispose();stop.Dispose();}
    }
    public void Dispose() {
        if(Interlocked.Exchange(ref disposed,1)==0)try {stop.Cancel();}catch(ObjectDisposedException) { }
        // No waits on the caller's dispatcher; the bounded IOCTL pump owns cleanup.
    }
}

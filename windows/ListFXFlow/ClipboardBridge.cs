using System.Net;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Threading;
using System.Collections.Specialized;
using System.Windows.Media.Imaging;

namespace ListFXFlow;

public sealed class ClipboardBridge
{
    private readonly Dispatcher dispatcher;
    private readonly DispatcherTimer timer;
    private ClipboardLink? link;
    private uint lastSequence;
    private long generation;
    public Action<string>? OnStatus;
    public ClipboardBridge(Dispatcher dispatcher) {
        this.dispatcher=dispatcher;
        timer=new DispatcherTimer(TimeSpan.FromMilliseconds(300),DispatcherPriority.Background,(_,_)=>Poll(),dispatcher); timer.Stop();
    }
    public void Connect(IPAddress local,IPAddress remote,byte[] secret) {
        Stop(); var current=new ClipboardLink(System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"ListFXFlow","ClipboardReceived")); link=current;
        current.OnReady=()=>dispatcher.BeginInvoke(()=> { if(link!=current) return; lastSequence=Native.GetClipboardSequenceNumber(); timer.Start(); });
        current.OnStatus=status=>dispatcher.BeginInvoke(()=> { if(link==current) OnStatus?.Invoke(status); });
        current.OnResult=(result,version)=>Apply(current,result,version);
        OnStatus?.Invoke("剪贴板连接中…");
        _=Task.Run(async()=>await current.Run(local,remote,secret));
    }
    private void Poll() {
        if(link==null) return;
        uint sequence=Native.GetClipboardSequenceNumber(); if(sequence==lastSequence) return;
        try {
            string[]? files=Clipboard.ContainsFileDropList() ? Clipboard.GetFileDropList().Cast<string>().ToArray() : null;
            BitmapSource? image=files is {Length:>0} ? null : Clipboard.ContainsImage() ? Clipboard.GetImage() : null;
            string? text=files is {Length:>0} || image!=null ? null : Clipboard.ContainsText() ? Clipboard.GetText() : null;
            if(Native.GetClipboardSequenceNumber()!=sequence) return;
            lastSequence=sequence; generation++;
            var current=link;
            current.LocalChanged(text,files is {Length:>0} ? files : null,generation);
            if(image!=null) {
                image.Freeze(); long revision=generation;
                // Compression must not stall input dispatch or publish an older copy after a newer one.
                _=EncodeImage(current,image,revision,sequence);
            }
        } catch(ExternalException) { /* Another app has the clipboard open; retry the next tick. */ }
        catch(Exception error) when(error is System.IO.IOException or FormatException or NotSupportedException or ArgumentException or InvalidOperationException) {
            // A malformed/unsupported bitmap supplied by another application must not
            // escape the DispatcherTimer and terminate ordinary keyboard/mouse sharing.
            lastSequence=sequence; generation++; link?.LocalChanged(null,null,generation);
            OnStatus?.Invoke("剪贴板："+error.Message);
        }
    }
    private async Task EncodeImage(ClipboardLink current,BitmapSource bitmap,long revision,uint sequence) {
        try {
            byte[] png=await Task.Run(()=>ClipboardBitmap.Encode(bitmap));
            await dispatcher.InvokeAsync(()=> {
                if(link==current && generation==revision && Native.GetClipboardSequenceNumber()==sequence)
                    current.LocalChanged(null,null,revision,png);
            });
        } catch(Exception error) when(error is System.IO.IOException or ArgumentException or NotSupportedException or InvalidOperationException) {
            await dispatcher.InvokeAsync(()=> { if(link==current && generation==revision) OnStatus?.Invoke("剪贴板图片："+error.Message); });
        }
    }
    private async Task<bool> Apply(ClipboardLink current,ClipboardResult result,long version) {
        // Load and validate completely before clearing the clipboard. BitmapCacheOption.OnLoad
        // detaches the image from its cache file, allowing a persistent native bitmap payload.
        ClipboardBitmap.Prepared? image=result.Image==null ? null : await Task.Run(()=>ClipboardBitmap.Decode(result.Image));
        for(int attempt=0;attempt<8;attempt++) {
            bool retry=false,applied=false;
            await dispatcher.InvokeAsync(()=> {
                if(link!=current || generation!=version || Native.GetClipboardSequenceNumber()!=lastSequence) return;
                try {
                    var data=new DataObject();
                    if(image!=null) data=ClipboardBitmap.DataObject(image);
                    else if(result.Files!=null) { var paths=new StringCollection(); paths.AddRange(result.Files); data.SetFileDropList(paths); data.SetData("Preferred DropEffect",new System.IO.MemoryStream(BitConverter.GetBytes(1))); }
                    else data.SetText(result.Text!);
                    Clipboard.SetDataObject(data,true); lastSequence=Native.GetClipboardSequenceNumber(); applied=true;
                } catch(ExternalException) { retry=true; }
            });
            if(!retry) return applied; await Task.Delay(75);
        }
        await dispatcher.InvokeAsync(()=> { if(link==current) OnStatus?.Invoke("剪贴板正被其他应用占用，请重新复制"); });
        return false;
    }
    public void Stop() { timer.Stop(); link?.Dispose(); link=null; generation=0; OnStatus?.Invoke("剪贴板未连接"); }
}

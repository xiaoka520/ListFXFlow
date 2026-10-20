using ListFXFlow;

internal static class TouchpadScrollTests {
    private static nint Wheel(short delta)=>unchecked((nint)((uint)(ushort)delta<<16));
    internal static async Task Run(Action<bool,string> check) {
        foreach(uint message in new uint[]{0x245,0x246,0x247,0x24c,0x24e,0x24f})
            check(TouchpadScroll.PromotePointer(true,message) && !TouchpadScroll.PromotePointer(false,message),"default Windows recognition receives the two-finger lifecycle "+message);
        check(!TouchpadScroll.PromotePointer(true,0x20a),"promoted wheel is consumed separately instead of recurring through pointer promotion");
        var fine=TouchpadScroll.FromWheel(0x20a,Wheel(1))!;
        check(fine.Dx==0 && fine.Dy==1.0/3,"ultra-high precision vertical wheel keeps fractional scroll");
        var horizontal=TouchpadScroll.FromWheel(0x20e,Wheel(-120))!;
        check(horizontal.Dx==40 && horizontal.Dy==0,"horizontal two-finger scrolling uses the same signed units as raw mouse scrolling");
        check(TouchpadScroll.FromWheel(0x20a,Wheel(-120))!.Dy==-40,"negative vertical delta remains signed");
        check(TouchpadScroll.FromWheel(0x20a,Wheel(0))==null && TouchpadScroll.FromWheel(0x319,Wheel(120))==null,"empty wheel and unrelated app gestures never become scroll");
        var pinch=TouchpadScroll.FromWheel(0x20a,Wheel(120)|8)!;
        check(pinch.Down==true && !TouchpadScroll.CanForward(true,"mac",true,pinch) && TouchpadScroll.CanForward(true,"mac",true,pinch,true),"system pinch-to-Ctrl-wheel is ignored while genuine held Ctrl plus scrolling remains available");
        check(TouchpadScroll.CanForward(true,"mac",true) && !TouchpadScroll.CanForward(true,"windows",true) &&
              !TouchpadScroll.CanForward(false,"mac",true) && !TouchpadScroll.CanForward(true,"mac",false),"worker scroll requires active sharing, Mac ownership and confirmed native capture");
        check(!TouchpadSuppressionPolicy.CaptureTwoFingerPointers(true),"suppress-only windows do not intercept two-finger contacts needed by Windows wheel recognition");
        var raw=new Native.RawMouseInput {Header=new() {Device=0},Mouse=new() {ButtonFlags=0xc00,ButtonData=1}};
        var touchpad=TouchpadScroll.FromRaw(raw)!;
        check(touchpad.Dx==-1.0/3 && touchpad.Dy==1.0/3,"zero-device touchpad wheel is forwarded on both axes instead of being discarded after native acquire");
        raw.Header.Device=123;var mouse=TouchpadScroll.FromRaw(raw)!;
        check(mouse.Dx==touchpad.Dx && mouse.Dy==touchpad.Dy,"existing physical mouse raw wheel path and units remain unchanged");
        raw.Mouse.ButtonFlags=0;check(TouchpadScroll.FromRaw(raw)==null,"ordinary raw motion is never converted into spurious scrolling");
        var lines=new List<string>();
        using(var diagnostics=new ScrollDiagnostics("test",lines.Add,automatic:false)) {
            Parallel.For(0,10000,_=>diagnostics.Count(ScrollStage.RawTouchpad));
            check(lines.Count==0,"input counter updates never perform synchronous logging");
            diagnostics.Flush();diagnostics.Flush();
            check(lines.Count==1 && lines[0].Contains("RawTouchpad=10000"),"background summaries retain exact counters and skip unchanged snapshots");
        }
        var queue=new TouchpadWorkerQueue(4);
        for(int i=0;i<10000;i++)if(!queue.Offer(new("scroll",4,Input:TouchpadScroll.FromWheel(0x20a,Wheel(1)))))throw new Exception("fine-scroll queue overflow");
        check(queue.Count==1 && queue.TryTake(out var sum) && Math.Abs(sum!.Input!.Dy!.Value-10000.0/3)<0.000001,"10k fine scroll samples sum without dropped distance or unbounded IPC backlog");
        queue.Offer(new("scroll",4,Input:TouchpadScroll.FromWheel(0x20a,Wheel(1))));
        queue.Offer(new("scroll",4,Input:TouchpadScroll.FromWheel(0x20a,Wheel(-1))));
        queue.Offer(new("state",4));
        queue.Offer(new("scroll",5,Input:fine));
        check(queue.Count==4,"scroll reversal, ownership state and epoch changes remain ordering barriers");
        queue.Replace(new("configure",6));check(queue.Count==1,"new ownership clears queued scroll from prior capture");
        using var wire=new MemoryStream();await TouchpadWorkerWire.Write(wire,new("scroll",7,Input:horizontal),CancellationToken.None);wire.Position=0;
        var packet=await TouchpadWorkerWire.Read(wire,CancellationToken.None);
        check(packet.Kind=="scroll" && packet.Epoch==7 && packet.Input is {Type:"scroll",Dx:40,Dy:0},"dedicated scroll packet is validated over actual IPC framing");
        foreach(var invalid in new[]{new TouchpadWorkerPacket("scroll",7),new TouchpadWorkerPacket("scroll",7,Input:new("touchpad") {Phase="cancel",Contacts=[]}),new TouchpadWorkerPacket("scroll",7,Input:new("scroll") {Dx=double.PositiveInfinity,Dy=0})}) {
            using var stream=new MemoryStream();bool rejected=false;
            try { await TouchpadWorkerWire.Write(stream,invalid,CancellationToken.None);stream.Position=0;await TouchpadWorkerWire.Read(stream,CancellationToken.None); }
            catch(Exception e) when(e is InvalidDataException or System.Text.Json.JsonException or ArgumentException) { rejected=true; }
            check(rejected,"invalid or non-scroll worker payload cannot enter the scroll path");
        }
    }
}

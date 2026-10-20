namespace ListFXFlow;

/// Windows recognizes the gesture; only ordinary scroll deltas leave the worker.
internal static class TouchpadScroll {
    internal static Message? FromRaw(Native.RawMouseInput input) {
        // RAWINPUTHEADER.hDevice==0 is a valid precision-touchpad report.
        // Do not make delivery depend on gesture-worker ownership.
        var mouse=input.Mouse;
        if((mouse.ButtonFlags&0xc00)==0)return null;
        double amount=Math.Clamp(unchecked((short)mouse.ButtonData)/3.0,-10000,10000);
        if(amount==0)return null;
        return new("scroll") { Dx=(mouse.ButtonFlags&0x800)!=0 ? -amount:0,Dy=(mouse.ButtonFlags&0x400)!=0 ? amount:0 };
    }
    internal static bool PromotePointer(bool suppressOnly,uint message)=>suppressOnly && message is 0x245 or 0x246 or 0x247 or 0x24c or 0x24e or 0x24f;
    internal static Message? FromWheel(uint message,nint wParam) {
        if(message is not (0x20a or 0x20e))return null;
        double delta=Math.Clamp(unchecked((short)((long)wParam>>16))/3.0,-10000,10000);
        if(delta==0)return null;
        // This flag stays inside IPC: Windows can promote a pinch to Ctrl+wheel.
        return new("scroll") { Dx=message==0x20e ? -delta:0,Dy=message==0x20a ? delta:0,Down=((long)wParam&8)!=0 };
    }
    internal static bool CanForward(bool sharing,string active,bool owned,Message? scroll=null,bool controlHeld=false)=>
        sharing && active=="mac" && owned && (scroll?.Down!=true || controlHeld);
}

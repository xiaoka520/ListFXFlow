namespace ListFXFlow;

/// Restores exactly the window we displaced, and only while we still own focus.
/// Native calls are injected so lifecycle/error paths are testable without a UI.
internal sealed class TouchpadFocusLease(nint target,Func<nint> foreground,Func<nint,bool> valid,Func<nint,bool> activate) {
    private nint previous;
    internal bool Pending { get;private set; }
    internal bool Active { get;private set; }
    internal bool Acquire() {
        if(Active && foreground()==target)return true;
        // Capture the displaced window once, across deferred activation and
        // retries. Otherwise a retry could replace it with our own window.
        if(!Pending && !Active)previous=foreground();
        Active=false;Pending=true;
        if(!activate(target)) {
            // A failed native call can still have changed focus; undo that part.
            if(foreground()==target && previous!=0 && previous!=target && valid(previous))activate(previous);
            return false;
        }
        return Confirm();
    }
    internal bool Confirm() {
        if((Active || Pending) && foreground()==target) { Active=true;Pending=false;return true; }
        return false;
    }
    internal void Release() {
        if(!Active && !Pending)return;
        Active=false;Pending=false;var old=previous;previous=0;
        if(foreground()==target && old!=0 && old!=target && valid(old))activate(old);
    }
}

internal static class TouchpadCaptureErrors {
    // Marshal.ThrowExceptionForHR maps some WinRT errors to managed types rather
    // than COMException. Experimental/API permission failures must not crash input.
    internal static bool Recoverable(Exception error)=>error is System.Runtime.InteropServices.COMException
        or UnauthorizedAccessException or NotSupportedException or NotImplementedException
        or InvalidOperationException or InvalidCastException or ArgumentException or EntryPointNotFoundException
        or DllNotFoundException or System.ComponentModel.Win32Exception;
}

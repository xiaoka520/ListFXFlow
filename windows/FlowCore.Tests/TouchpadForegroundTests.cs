using ListFXFlow;

internal static class TouchpadForegroundTests {
    internal static void Run(Action<bool,string> check) {
        nint current=100;bool eligible=false;int requests=0;
        var lease=new TouchpadFocusLease(200,()=>current,_=>true,w=> {requests++;if(!eligible)return false;current=w;return true;});
        check(!lease.Acquire() && lease.Pending && !lease.Active,"denied activation remains pending instead of claiming ownership");
        eligible=true;
        check(lease.Acquire() && lease.Active,"a renewed foreground grant can complete the same pending capture");
        lease.Release();
        check(current==100,"retry preserves the original application for release");
        eligible=false;
        check(!lease.Acquire() && !lease.Active,"second crossing cannot reuse the first crossing's foreground eligibility");
        eligible=true;check(lease.Acquire(),"second crossing accepts a fresh grant");lease.Release();
        check(current==100 && requests==6,"repeated handoffs retain the displaced window and restore it once each time");
        bool requested=false;
        var deferred=new TouchpadFocusLease(200,()=>current,_=>true,w=> {if(w==200)requested=true;else current=w;return true;});
        check(!deferred.Acquire() && requested && deferred.Pending,"an accepted asynchronous foreground request is not immediately reported as owned");
        current=200;
        check(deferred.Confirm() && deferred.Active,"message-pump completion confirms the actual foreground window");
        deferred.Release();check(current==100,"asynchronous activation still restores the original foreground");
        var bounded=new TouchpadActivationRetry();bounded.Begin(7,0);
        check(bounded.Request(0) && !bounded.Request(249) && !bounded.Expired(249),"foreground retries wait for the message pump and do not spin");
        check(bounded.Request(250) && bounded.Request(500) && bounded.Request(750) && !bounded.Request(1000) && bounded.Expired(1000),"foreground denial has four bounded requests per crossing");
        bounded.Stop();check(!bounded.Accepts(7) && !bounded.Request(2000),"returning to Windows cancels deferred activation");
        bounded.Begin(8,2000);
        check(!bounded.Accepts(7) && bounded.Accepts(8) && bounded.Request(2000),"a new crossing can retry while stale foreground replies are rejected");
    }
}

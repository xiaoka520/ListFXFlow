using ListFXFlow;

internal static class DesktopInputTests
{
    internal static void Run(Action<bool,string> check) {
        var gate=new DesktopInputGate();long original=gate.Epoch;
        var down=new Native.Input {Type=1,Union=new Native.InputUnion {Key=new Native.KeyInput {Scan=29,Flags=9,Extra=Native.Marker}}};
        gate.Delivered(down);
        gate.Delivered(new Native.Input {Type=0,Union=new Native.InputUnion {Mouse=new Native.MouseInput {Flags=128,Data=2,Extra=Native.Marker}}});
        check(gate.Pause() && !gate.Pause() && !gate.Allows(original),"UAC suspends once and immediately rejects queued input");
        check(gate.Resume() && !gate.Resume() && !gate.Allows(original),"returning from UAC never replays pre-prompt keys or clicks");
        var releases=gate.Releases;
        check(releases.Length==2 && releases.Any(x=>x.Type==1 && x.Union.Key.Scan==29 && x.Union.Key.Flags==11) &&
            releases.Any(x=>x.Type==0 && x.Union.Mouse.Flags==256 && x.Union.Mouse.Data==2),
            "desktop recovery releases an injected extended modifier and the correct side button");
        foreach(var release in releases)gate.Delivered(release);
        check(gate.Releases.Length==0 && gate.Allows(gate.Epoch),"successful recovery releases are removed before fresh input");
        gate.Delivered(down);var up=down;up.Union.Key.Flags|=2;gate.Delivered(up);
        check(gate.Releases.Length==0,"a normally released key is not released again after a later UAC prompt");
        // In-flight native calls can finish after capture has paused. Keep their release.
        gate.Pause();gate.Delivered(down);gate.Resume();
        check(gate.Releases.Length==1,"a native key-down completing during desktop transition still gets cleaned up");
    }
    internal static void Worker(Action<bool,string> check) {
        using var entered=new ManualResetEventSlim();using var unblock=new ManualResetEventSlim();using var recovered=new ManualResetEventSlim();
        var gate=new DesktopInputGate();var delivered=new List<InjectionWork>();
        using var worker=new InjectionWorker(work=> {
            if(work.Input.Type==1 && work.Input.Union.Key.Scan==999) {entered.Set();unblock.Wait(5000);return;}
            if(!gate.Allows(work.DesktopEpoch))return;
            lock(delivered)delivered.Add(work);
            if(work.DesktopCleanup)recovered.Set();
        });
        try {
            worker.Post(new(new Native.Input {Type=1,Union=new Native.InputUnion {Key=new Native.KeyInput {Scan=999}}}));
            if(!entered.Wait(5000))throw new Exception("Injection stall did not start");
            worker.Post(new(new Native.Input {Type=1,Union=new Native.InputUnion {Key=new Native.KeyInput {Scan=58}}},DesktopEpoch:gate.Epoch));
            worker.Post(new(new Native.Input {Type=0,Union=new Native.InputUnion {Mouse=new Native.MouseInput {Flags=2}}},DesktopEpoch:gate.Epoch));
            gate.Pause();gate.Resume();
            worker.Post(new(default,DesktopCleanup:true,DesktopEpoch:gate.Epoch));
            // Ownership changes cancel pending movement but must retain desktop cleanup.
            worker.CancelPending();unblock.Set();
            check(recovered.Wait(5000),"focus changes cannot cancel secure-desktop release cleanup");
            lock(delivered)check(delivered.Count==1 && delivered[0].DesktopCleanup,"buffered key-down and click never leak after a native stall and UAC transition");
        } finally {unblock.Set();}
    }
}

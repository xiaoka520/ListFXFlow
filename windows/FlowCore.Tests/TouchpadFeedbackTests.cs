using ListFXFlow;

internal static class TouchpadFeedbackTests {
    internal static void Run(Action<bool,string> check) {
        var counter=new TouchpadFeedbackCounter("synthetic-touchpad");
        Message Stroke(string phase,long epoch=3)=>new("touchpad") {Phase=phase,GestureEpoch=epoch,Contacts=phase is "end" or "cancel" ? []:[new(0,.3,.5),new(1,.7,.5)]};
        check(counter.Applied(Stroke("change"),1,0)==null,"orphan injected updates cannot create receipt acknowledgements");
        var first=counter.Applied(Stroke("begin"),1,10)!;
        first.Validate();
        check(first is {Phase:"submitted",Frames:1,Events:1,GestureEpoch:3},"feedback records successful native submissions with the source stroke epoch");
        check(counter.Applied(Stroke("change"),1,20)==null,"native update acknowledgements are throttled off the hot path");
        check(counter.Applied(Stroke("end",2),1,30)==null,"stale stroke acknowledgement cannot update current stroke counters");
        var end=counter.Applied(Stroke("end"),1,40)!;
        check(end is {Frames:3,Events:3},"end feedback bypasses throttling without counting a rejected stale stroke");
        var fresh=counter.Applied(Stroke("begin",4),0,50)!;
        check(fresh is {Phase:"received",Frames:1,Events:0,GestureEpoch:4},"new stroke starts receipt counters without claiming an unsubmitted native event");
        check(Wire.Decode(Wire.Encode(fresh)).Backend=="synthetic-touchpad","receipt feedback round trips through the protocol");
        bool Reject(Message m) {try {m.Validate();return false;}catch {return true;} }
        fresh.GestureEpoch=-1;check(Reject(fresh),"negative receipt epoch rejected");fresh.GestureEpoch=4;
        fresh.Frames=1_000_000_001;check(Reject(fresh),"unbounded receipt counters rejected");fresh.Frames=1;
        fresh.Phase="accepted";check(Reject(fresh),"submission feedback cannot claim application acceptance");
    }
}

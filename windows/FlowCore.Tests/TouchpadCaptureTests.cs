using ListFXFlow;

internal static class TouchpadCaptureTests {
    internal static void Run(Action<bool,string> check) {
        var batch=new TouchpadContactBatch();
        batch.Push(new(70000,1,true,false,.3,.5));batch.Push(new(80000,1,true,false,.7,.5));
        var first=batch.Flush()!;
        check(first is [{Id:0,X:.3},{Id:1,X:.7}],"SDK per-pointer updates form a complete bounded contact snapshot");
        check(batch.Flush()==null,"idle SDK callbacks do not resend an unchanged frame");
        batch.Push(new(80000,2,true,false,.8,.5));batch.Push(new(70000,2,true,false,.2,.5));
        var next=batch.Push(new(80000,3,true,false,.9,.5));
        check(next is [{Id:0,X:.2},{Id:1,X:.8}],"frame boundary flushes both fingers before applying a newer pinch sample");
        batch.Push(new(70000,3,true,false,.1,.5));
        check(batch.Flush() is [{Id:0,X:.1},{Id:1,X:.9}],"callback order cannot swap finger identities");
        batch.Push(new(80000,4,false,false,0,0));batch.Push(new(70000,4,false,false,0,0));
        check(batch.Flush() is {Length:0},"all SDK releases produce a real lift/end snapshot");
        batch.Push(new(1,5,true,false,.2,.3));batch.Push(new(2,5,true,true,.7,.3));batch.Push(new(3,5,true,false,.5,.5));
        check(batch.Flush() is {Length:0},"cancelled frame cannot be resurrected by another finger callback in the same frame");
        batch.Reset();batch.Push(new(1,1,true,false,-.1,1.2));
        check(batch.Flush() is [{Id:0,X:0,Y:1}],"new capture lifetime clears identities and clamps physical coordinates");
        bool rejected=false;try {batch.Push(new(2,2,true,false,double.NaN,.2));}catch(ArgumentException) {rejected=true;}
        check(rejected,"invalid physical SDK coordinates are rejected before transmission");
    }
}

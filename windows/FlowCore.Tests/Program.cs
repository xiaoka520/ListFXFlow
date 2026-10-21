using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using ListFXFlow;

if(args.Length==3 && args[0]=="--fake-touchpad") { await TouchpadWorkerTests.Child(args[1],args[2]);return; }
int passed=0;
void Check(bool condition,string label) { if(!condition) throw new Exception(label); passed++; Console.WriteLine("PASS "+label); }
void Reject(Action action,string label) { try { action(); } catch { Check(true,label); return; } throw new Exception(label); }
PtpHidTests.Run(Check,Reject);
TouchpadCaptureTests.Run(Check);
TouchpadFeedbackTests.Run(Check);
TouchpadForegroundTests.Run(Check);
TouchpadSuppressionTests.Run(Check);
GameProtectionTests.Run(Check);
byte[] secret=Enumerable.Repeat((byte)7,32).ToArray(),c=Enumerable.Repeat((byte)1,32).ToArray(),s=Enumerable.Repeat((byte)2,32).ToArray();
using(var a=new SecureChannel(secret,c,s,false)) using(var b=new SecureChannel(secret,c,s,true)) {
    byte[] encrypted=a.Seal(new("ping")); Check(b.Open(encrypted).Type=="ping","AES-GCM duplex");
    Reject(()=>b.Open(encrypted),"reject replay"); Check(a.Open(b.Seal(new("reset"))).Type=="reset","reverse encryption");
    var corrupt=a.Seal(new("ping")); corrupt[^1]^=1; Reject(()=>b.Open(corrupt),"reject tampered packet");
}
foreach(string side in new[]{"left","right","top","bottom"}) {
    var router=new PointerRouter(); router.Reset("mac"); router.Topology.Side=side;
    var edge=side switch { "left"=>(0,500,-10,0),"right"=>(1599,500,10,0),"top"=>(800,0,0,-10),_=>(800,999,0,10) };
    Check(router.Move("mac",edge.Item1,edge.Item2,edge.Item3,edge.Item4,1600,1000).Active=="windows","cross "+side);
    Check(router.Move("mac",0,0,-edge.Item3*4,-edge.Item4*4,1600,1000).Active=="mac","return "+side);
}
{
    var router=new PointerRouter(); router.Reset("mac"); router.Topology.Offset=800;
    Check(router.Move("mac",1599,200,10,0,1600,1000).Active=="mac","offset gap does not cross");
    Check(router.Move("mac",1599,900,10,0,1600,1000,false).Active=="mac","held input blocks crossing");
}
Reject(()=>new Message("pointer") { Active="mac",X=double.NaN,Y=0 }.Validate(),"reject NaN");
Check(KeyMap.ByHid.Count==KeyMap.ByScan.Count && KeyMap.ByHid[4].Mac==0,"canonical physical key map");
await using(var stream=new MemoryStream()) {
    await Wire.WriteFrame(stream,Wire.Encode(new("ping")),CancellationToken.None); stream.Position=0;
    Check(Wire.Decode(await Wire.ReadFrame(stream,CancellationToken.None)).Type=="ping","length-prefixed framing");
}
{
    var adapter=new BridgeAdapter("Thunderbolt",IPAddress.Parse("10.0.0.3"),IPAddress.Parse("255.255.255.0"),true);
    Check(ConnectionTarget.Resolve(adapter,"  ")==null,"blank address preserves automatic discovery");
    Check(ConnectionTarget.Resolve(adapter," 10.0.0.2 ")?.Address.Equals(IPAddress.Parse("10.0.0.2"))==true,"manual Mac address and whitespace");
    Check(ConnectionTarget.Preferred(adapter,"","10.0.0.2")?.Address.ToString()=="10.0.0.2","last authenticated address is preferred before discovery");
    Check(ConnectionTarget.Preferred(adapter,"10.0.0.4","10.0.0.2")?.Address.ToString()=="10.0.0.4","explicit saved address retains priority");
    Check(ConnectionTarget.Preferred(adapter,"192.168.3.1","10.0.0.2")==null,"stale saved subnet falls back to discovery");
    Check(ConnectionTarget.Preferred(adapter,"","garbled")==null,"invalid cached address cannot block discovery");
    foreach(string invalid in new[]{"10.2","mac.local","256.0.0.2","::1","224.0.0.1","10.0.0.255","10.0.0.3","192.168.1.2","127.0.0.1"})
        Reject(()=>ConnectionTarget.Resolve(adapter,invalid),"reject invalid manual address "+invalid);
}
// An old saved disabled toggle must not disable the now-unconditional precise path.
{
    var migrated=System.Text.Json.JsonSerializer.Deserialize<ConnectionPreferences>("{\"MacAddress\":\"10.0.0.2\",\"LocalAddress\":\"10.0.0.3\",\"SmoothScroll\":false}");
    Check(migrated?.MacAddress=="10.0.0.2" && migrated.LocalAddress=="10.0.0.3","old settings preserve manual IP and adapter when the wheel switch is removed");
    var wheel=new WheelTranslator(); wheel.Add(1,0);
    Check(wheel.Take()==(3,0),"precise scrolling is unconditional even for a previous disabled preference");
}
// Simulate a stalled Windows injection API. Capture-side enqueues must still
// finish; queued motion must collapse without losing click/release ordering.
{
    using var entered=new ManualResetEventSlim(); using var resume=new ManualResetEventSlim();
    var executed=new List<InjectionWork>(); int ownerThread=Environment.CurrentManagedThreadId,workerThread=0;
    var worker=new InjectionWorker(work=> {
        workerThread=Environment.CurrentManagedThreadId;
        lock(executed) executed.Add(work);
        if(work.Input.Type==1 && work.Input.Union.Key.Scan==999) {entered.Set();resume.Wait();}
    });
    worker.Post(new InjectionWork(new Native.Input {Type=1,Union=new Native.InputUnion {Key=new Native.KeyInput {Scan=999}}}));
    if(!entered.Wait(3000))throw new Exception("injection worker did not start");
    for(int i=0;i<10000;i++) {
        if(!worker.Post(new InjectionWork(new Native.Input(),true,true,i,10)) ||
           !worker.Post(new InjectionWork(new Native.Input {Type=0,Union=new Native.InputUnion {Mouse=new Native.MouseInput {Flags=0x800,Data=3}}}))) throw new Exception("blocked injector grew an unbounded motion queue");
    }
    worker.Post(new InjectionWork(new Native.Input {Type=0,Union=new Native.InputUnion {Mouse=new Native.MouseInput {Flags=2}}}));
    worker.Post(new InjectionWork(new Native.Input(),true,false,42,10));
    worker.Post(new InjectionWork(new Native.Input {Type=0,Union=new Native.InputUnion {Mouse=new Native.MouseInput {Flags=4}}}));
    Check(executed.Count==1,"capture can enqueue 10k mixed samples while native injection is blocked");
    resume.Set();worker.Dispose();
    Check(workerThread!=ownerThread,"native injection executes outside the capture/caller thread");
    Check(executed.Count==6 && executed[1].Pointer && executed[1].X==9999 && executed[2].Input.Union.Mouse.Data==30000,
        "native queue coalesces mixed motion without losing scroll distance");
    Check(executed[3].Input.Union.Mouse.Flags==2 && executed[4].X==42 && executed[5].Input.Union.Mouse.Flags==4,
        "button down-move-up barriers survive a stalled native API");
    Check(worker.Statistics.Coalesced>10000 && worker.Statistics.MaxCallMs>0,"queue and native-call timing measurements are recorded");
}
{
    using var entered=new ManualResetEventSlim();using var resume=new ManualResetEventSlim();var executed=new List<InjectionWork>();
    var worker=new InjectionWorker(work=> {executed.Add(work);if(work.Input.Union.Key.Scan==999) {entered.Set();resume.Wait();}});
    InjectionWork Key(ushort scan,bool up=false)=>new(new Native.Input {Type=1,Union=new Native.InputUnion {Key=new Native.KeyInput {Scan=scan,Flags=up ? 2u : 0u}}});
    worker.Post(Key(999));if(!entered.Wait(3000))throw new Exception("worker did not start");
    worker.Post(Key(30));worker.Post(new InjectionWork(new Native.Input(),true,true,999,999));
    worker.Post(Key(58));worker.Post(Key(58,true));worker.CancelPending();worker.Post(Key(30,true));
    resume.Set();worker.Dispose();
    Check(executed.Select(w=>w.Input.Union.Key.Scan).SequenceEqual(new ushort[]{999,58,58,30}),"reset discards old motion/presses but preserves global Caps transitions and final releases");
    Check(executed[^1].Input.Union.Key.Flags==2,"queued release drains before injection worker shutdown");
}
// Mixed pointer/wheel bursts keep one latest pointer, preserve distance and click barriers.
{
    var queue=new InputMessageQueue(8);
    for(int i=0;i<10000;i++) {
        if(!queue.TryEnqueue(new("pointer") { Active="windows",X=i/10000.0,Y=.5 }) ||
           !queue.TryEnqueue(new("scroll") { Dx=0,Dy=.125 })) throw new Exception("mixed input burst overflow");
    }
    queue.TryEnqueue(new("button") { Button=0,Down=true });
    queue.TryEnqueue(new("pointer") { Active="windows",X=.2,Y=.3 });
    queue.TryEnqueue(new("caps") { Down=true });
    queue.TryEnqueue(new("pointer") { Active="windows",X=.3,Y=.4 });
    queue.TryEnqueue(new("button") { Button=0,Down=false });
    var all=new List<Message>(); while(queue.TryDequeue(out var packet)) all.Add(packet!);
    Check(all.Select(p=>p.Type).SequenceEqual(new[]{"pointer","scroll","button","pointer","caps","pointer","button"}),"10k interleaved samples preserve click and Caps barriers without queue growth");
    Check(all[0].X==.9999 && all[1].Dy==1250 && all[3].X==.2 && all[5].X==.3,"mixed burst keeps latest positions and full wheel distance");
}
// A high-polling-rate mouse must never put thousands of positions in front of its first click.
{
    var queue=new InputMessageQueue(8);
    for(int i=0;i<100000;i++) if(!queue.TryEnqueue(new("pointer") { Active="windows",X=i/100000.0,Y=.5 })) throw new Exception("pointer burst overflow");
    Check(queue.TryEnqueue(new("button") { Button=0,Down=true }),"click admitted immediately after 100k pointer burst");
    for(int i=0;i<100000;i++) if(!queue.TryEnqueue(new("pointer") { Active="windows",X=.5,Y=i/100000.0 })) throw new Exception("drag burst overflow");
    Check(queue.TryEnqueue(new("button") { Button=0,Down=false }),"release preserved after high-rate drag");
    var events=new List<Message>(); while(queue.TryDequeue(out var m)) events.Add(m!);
    Check(events.Select(m=>m.Type).SequenceEqual(new[]{"pointer","button","pointer","button"}),"pointer coalescing preserves down-move-up order");
    Check(events[0].X==.99999 && events[1].Down==true && events[2].Y==.99999 && events[3].Down==false,"latest positions stay on the correct side of each click barrier");
}
{
    var queue=new InputMessageQueue(8);
    queue.TryEnqueue(new("pointer") { Active="windows",X=.1,Y=.1 });
    queue.TryEnqueue(new("scroll") { Dx=0,Dy=40 });
    queue.TryEnqueue(new("pointer") { Active="windows",X=.9,Y=.8 });
    var events=new List<Message>(); while(queue.TryDequeue(out var m)) events.Add(m!);
    Check(events.Count==2 && events[0].Type=="pointer" && events[0].X==.9 && events[1].Type=="scroll","pointer supersedes an older sample across queued scroll without moving a click");
}
{
    var queue=new InputMessageQueue(4);
    queue.TryEnqueue(new("pointer") { Active="mac",X=.99,Y=.5 });
    queue.TryEnqueue(new("pointer") { Active="windows",X=0,Y=.5 });
    queue.TryEnqueue(new("key") { Key=4,Down=true });
    queue.TryEnqueue(new("key") { Key=4,Down=false });
    Check(!queue.TryEnqueue(new("reset")),"control overflow is explicit, never silently drops releases");
    var events=new List<Message>(); while(queue.TryDequeue(out var m)) events.Add(m!);
    Check(events.Count==4 && events[0].Active=="mac" && events[1].Active=="windows" && events[2].Down==true && events[3].Down==false,"focus transition and keyboard ordering barriers remain intact");
    queue.TryEnqueue(new("key") { Key=4,Down=true }); queue.Clear(); Check(!queue.HasPending,"disconnect clears pending input");
}
{
    Check(System.Runtime.InteropServices.Marshal.SizeOf<Native.RawInputHeader>()==24 && System.Runtime.InteropServices.Marshal.SizeOf<Native.RawMouseInput>()==48 && System.Runtime.InteropServices.Marshal.SizeOf<Native.RawInputDevice>()==16,"Windows x64 raw mouse ABI sizes");
    var motion=new RawMouseMotion();
    Check(motion.Read(1,0,-40,15,2560,1600)==new PointD(-40,15),"relative motion survives a cursor parked on any screen edge");
    Check(motion.Read(1,1,65535,0,2560,1600)==new PointD(0,0),"first absolute sample establishes a baseline");
    Check(motion.Read(1,1,0,65535,2560,1600)==new PointD(-2559,1599),"absolute mouse movement uses device coordinates instead of parked cursor");
    Check(motion.Read(2,1,32768,32768,2560,1600)==new PointD(0,0),"separate absolute devices do not share baselines");
    motion.Clear(); Check(motion.Read(1,1,32768,32768,2560,1600)==new PointD(0,0),"disconnect clears absolute device history");
    Check(RawMouseMotion.Since(5,uint.MaxValue-4)==10 && RawMouseMotion.Since(uint.MaxValue-4,5)==-10,"focus timestamp comparison handles tick wraparound");
    var screen=new ScreenRect(100,200,1600,1000);
    Check(CursorDeparture.Project(screen,new(900,700),new(1900,950),1600,1000)==new PointD(1599,699.3),"fast remote crossing parks on the right edge at the interpolated height");
    Check(CursorDeparture.Project(screen,new(900,700),new(-100,450),1600,1000)==new PointD(0,299.7),"fast remote crossing parks on the left edge at the interpolated height");
    Check(CursorDeparture.Project(screen,new(900,700),new(1300,1400),1600,1000) is { Y:999 },"fast remote crossing parks on the bottom edge");
    Check(CursorDeparture.Project(screen,new(900,700),new(500,0),1600,1000) is { Y:0 },"fast remote crossing parks on the top edge");
    Check(CursorDeparture.Project(screen,new(900,700),new(1000,800),1600,1000)==null,"local movement never relocates the parked cursor");
    foreach(string side in new[]{"left","right","top","bottom"}) {
        var router=new PointerRouter(); router.Topology.Side=side; router.Reset("windows");
        var edge=side switch { "left"=>(1599,500,10,0),"right"=>(0,500,-10,0),"top"=>(800,999,0,10),_=>(800,0,0,-10) };
        router.Move("windows",edge.Item1,edge.Item2,edge.Item3,edge.Item4,1600,1000);
        var before=router.Position;
        var raw=motion.Read(3,0,edge.Item3*4,edge.Item4*4,1600,1000);
        var packet=router.Move("windows",edge.Item1,edge.Item2,raw.X,raw.Y,1600,1000);
        Check(packet.Active=="mac" && router.Position!=before,"continue moving on Mac with Windows cursor parked at "+side+" exit");
        raw=motion.Read(3,0,-edge.Item3*10,-edge.Item4*10,1600,1000);
        Check(router.Move("windows",edge.Item1,edge.Item2,raw.X,raw.Y,1600,1000).Active=="windows","raw mouse returns from Mac across "+side+" seam");
    }
}
{
    foreach(string device in new[]{"mac","windows"}) foreach(string side in new[]{"left","right","top","bottom"}) {
        var router=new PointerRouter(); router.Accept(new("pointer") { Active=device,X=.23,Y=.71 });
        router.UpdateTopology(new Topology { Side=side,Offset=420,MacAspect=2,WindowsAspect=1.3 }); var r=router.Topology.Rect(device);
        Check(router.Active==device && Math.Abs((router.Position.X-r.X)/r.Width-.23)<.000001 && Math.Abs((router.Position.Y-r.Y)/r.Height-.71)<.000001,"layout preserves focus and screen coordinates "+device+" "+side);
    }
    var wheel=new WheelAccumulator(); Check(wheel.Add(40)==120 && wheel.Add(-40)==-120,"one notch maps to one Windows wheel detent");
    int sum=0; for(int i=0;i<100;i++) sum+=wheel.Add(.125); Check(sum==37,"fine wheel events preserve accumulated fractions");
    wheel.Reset(); Check(wheel.Add(.125)==0,"focus changes clear pending scroll fractions");
    new Message("caps") { Down=true }.Validate(); Check(true,"caps state message validates");
    Reject(()=>new Message("caps").Validate(),"caps state message requires a state");
    var precise=new WheelTranslator();
    precise.Add(40,0); Check(precise.Take()==(120,0) && !precise.Pending,"default precise mode emits one notch immediately without animation");
    precise.Add(1,-1); Check(precise.Take()==(3,-3),"small wheel increments are never rounded to whole detents");
    int fineTotal=0; for(int i=0;i<1000;i++) { precise.Add(.125,-.125); var d=precise.Take(); fineTotal+=d.X; if(d.X!=-d.Y) throw new Exception("wheel axes lost symmetry"); }
    Check(fineTotal==375 && !precise.Pending,"precise mode retains sub-unit carry across idle intervals");
    precise.Add(120,-80); Check(precise.Take()==(360,-240) && precise.Take()==(0,0),"combined input is sent once without queued animation frames");
    precise.Add(.125,0); precise.Reset(); precise.Add(.25,0); Check(!precise.Pending,"focus reset clears residual motion from the old desktop");
    precise.Reset(); precise.Add(double.NaN,0); Check(!precise.Pending,"nonfinite wheel input is ignored");
    foreach(string path in new[]{"../escape","/absolute","C:/bad","a\\b","a//b","x/../b","NUL.txt","COM1","a.","a "}) Check(!ClipboardPath.Valid(path),"reject unsafe clipboard path "+path);
    string cache=Path.Combine(Path.GetTempPath(),"ListFXFlow-inbox-"+Guid.NewGuid());
    try {
        using var inbox=new ClipboardInbox(cache); string id=Guid.NewGuid().ToString("D"); byte[] bytes=System.Text.Encoding.UTF8.GetBytes("资料 Unicode 🌏");
        inbox.Receive(new("begin",id) { Roots=["资料"],Count=3,Total=bytes.Length });
        inbox.Receive(new("entry",id) { Path="资料",Directory=true,Size=0 });
        inbox.Receive(new("entry",id) { Path="资料/空目录",Directory=true,Size=0 });
        inbox.Receive(new("entry",id) { Path="资料/文件.txt",Directory=false,Size=bytes.Length });
        Reject(()=>inbox.Receive(new("commit",id)),"incomplete clipboard files cannot commit");
        inbox.Receive(new("data",id) { Bytes=bytes });
        Reject(()=>inbox.Receive(new("fileEnd",id) { Hash=new string('0',64) }),"corrupt clipboard files cannot commit");
        // Hash failure has not closed the stream; production aborts it immediately.
        inbox.Abort(); Check(Directory.GetDirectories(cache).Length==0,"cancel removes incomplete clipboard data");
        inbox.Receive(new("begin",id) { Roots=["资料"],Count=3,Total=bytes.Length });
        inbox.Receive(new("entry",id) { Path="资料",Directory=true,Size=0 });
        inbox.Receive(new("entry",id) { Path="资料/空目录",Directory=true,Size=0 });
        inbox.Receive(new("entry",id) { Path="资料/文件.txt",Directory=false,Size=bytes.Length });
        inbox.Receive(new("data",id) { Bytes=bytes }); inbox.Receive(new("fileEnd",id) { Hash=Convert.ToHexString(SHA256.HashData(bytes)) });
        var complete=inbox.Receive(new("commit",id));
        Check(complete?.Files is {Length:1} && File.ReadAllBytes(Path.Combine(complete.Files[0],"文件.txt")).SequenceEqual(bytes) && Directory.Exists(Path.Combine(complete.Files[0],"空目录")),"files and empty folders publish after integrity verification");
        inbox.Abort(); Check(Directory.Exists(complete!.Files![0]),"completed clipboard files remain usable after disconnect");
    } finally { if(Directory.Exists(cache)) Directory.Delete(cache,true); }
}
ClipboardImageTests.Run(Check,Reject);
await TouchpadScrollTests.Run(Check);
// Native touchpad messages, opt-in gating and ABI construction never call user32.
{
    var contacts=new[] { new TouchpadContact(1,.3,.5),new TouchpadContact(2,.7,.5) };
    var idleSender=new TouchpadSender();idleSender.Cancel();
    Check(idleSender.Sample(contacts,true)?.Phase=="begin","idle focus transition preserves the very first gesture");
    var sender=new TouchpadSender();
    Check(sender.Sample(contacts,false)==null && sender.Sample(contacts,true)==null,"touchpad opt-in never steals an in-progress local stroke");
    sender.Sample([],true);
    Check(sender.Sample(contacts,true)?.Phase=="begin" && sender.Sample(contacts,true)?.Phase=="change","touchpad preserves continuous stroke phases");
    Check(sender.Cancel()?.Phase=="cancel" && sender.Cancel()==null,"touchpad cancellation releases exactly once");
    sender.Sample([],true);sender.Sample(contacts,true);
    Check(sender.Sample([contacts[0]],true)?.Phase=="end","gesture-only stream ends when fewer than two fingers remain");
    new Message("touchpad") { Phase="begin",Contacts=contacts }.Validate();
    Reject(()=>new Message("touchpad") { Phase="begin",Contacts=[contacts[0],contacts[0]] }.Validate(),"reject duplicate touchpad identities");
    Reject(()=>new Message("touchpad") { Phase="change",Contacts=[new(1,double.NaN,0),contacts[1]] }.Validate(),"reject non-finite touchpad coordinates");
    Reject(()=>new Message("touchpad") { Phase="end",Contacts=contacts }.Validate(),"end phase cannot introduce contacts");
    Reject(()=>new Message("touchpadControl").Validate(),"gesture opt-in requires an explicit boolean");
    var migrated=System.Text.Json.JsonSerializer.Deserialize<ConnectionPreferences>("{}");
    Check(migrated?.ForwardMacGestures==false,"new Windows-to-Mac gesture option is disabled for fresh settings");
    var oldSettings=System.Text.Json.JsonSerializer.Deserialize<ConnectionPreferences>("{\"TouchpadEnabled\":true}");
    Check(oldSettings?.ForwardMacGestures==false,"an old experimental preference cannot silently turn on Windows-to-Mac gestures");
    var globalSender=new TouchpadSender();var three=contacts.Append(new TouchpadContact(3,.5,.7)).ToArray();
    Check(globalSender.Sample(contacts,true,3)==null,"global forwarding does not intercept the ordinary two-finger wheel path");
    Check(globalSender.Sample(three,true,3)?.Phase=="begin","three-finger SDK contacts begin one gesture stroke");
    Check(globalSender.Sample(contacts,true,3)?.Phase=="end","lifting to two fingers ends the system gesture without generating a scroll stroke");
    Check(globalSender.Sample(three,true,3)==null,"adding a third finger again cannot restart a partially lifted stroke");
    globalSender.Sample([],true,3);
    Check(globalSender.Sample(three,true,3)?.Phase=="begin" && globalSender.Cancel()?.Phase=="cancel","full lift rearms three-finger capture and settings cancellation releases it");
    Check(System.Runtime.InteropServices.Marshal.SizeOf<NativeTouchpadInjector.Creation>()==40 &&
          System.Runtime.InteropServices.Marshal.SizeOf<NativeTouchpadInjector.PointerInfo>()==96 &&
          System.Runtime.InteropServices.Marshal.SizeOf<NativeTouchpadInjector.TouchInfo>()==144 &&
          System.Runtime.InteropServices.Marshal.SizeOf<NativeTouchpadInjector.PointerTypeInfo>()==152,"Windows x64 precision-touchpad ABI layout");
    var native=NativeTouchpadInjector.MakeFrame([],contacts);
    Check(native.Length==2 && native.All(p=>p.Type==5 && p.Touch.Pointer.Flags==0x4006) && native[1].Touch.Pointer.Himetric.X==7000,"native PT_TOUCHPAD frame uses device-relative confident contacts");
    Check(NativeTouchpadInjector.MakeFrame(contacts,[],true).All(p=>p.Touch.Pointer.Flags==0xc000),"cancelled native gestures carry POINTER_FLAG_CANCELED");
    var ended=NativeTouchpadInjector.MakeFrame(contacts,[]);
    Check(ended.All(p=>p.Touch.Pointer.Flags==0x4000),"native touchpad release frame has no contact or range flags");
    var q=new InputMessageQueue();q.TryEnqueue(new("pointer") { Active="windows",X=.1,Y=.1 });
    q.TryEnqueue(new("touchpad") { Phase="begin",Contacts=contacts });
    q.TryEnqueue(new("pointer") { Active="windows",X=.2,Y=.2 });
    var ordered=new List<string>();while(q.TryDequeue(out var m))ordered.Add(m!.Type);
    Check(ordered.SequenceEqual(new[]{"pointer","touchpad","pointer"}),"gesture frames are pointer coalescing barriers");
}
Check(TouchpadCaptureErrors.Recoverable(System.Runtime.InteropServices.Marshal.GetExceptionForHR(unchecked((int)0x80070005))!) && TouchpadCaptureErrors.Recoverable(new NotImplementedException()),"WinRT access denial and unavailable API errors cannot crash the sharing engine");
Check(TouchpadCaptureErrors.Recoverable(System.Runtime.InteropServices.Marshal.GetExceptionForHR(unchecked((int)0x80004002))!),"WinRT E_NOINTERFACE/InvalidCast is a capability failure, not an unhandled input-thread error");
Check(!TouchpadCaptureErrors.Recoverable(new AccessViolationException()),"native memory corruption is not hidden as an ordinary capability failure");
await TouchpadWorkerTests.Run(Check);
{
    var frames=new TouchpadPointerFrames();
    var bounds=new NativeTouchpadInjector.Rect {Left=100,Top=200,Right=10100,Bottom=6200};
    NativeTouchpadInjector.TouchInfo Point(uint id,uint frame,int x,int y,uint flags=0x4006)=>new() {Pointer=new() {Type=5,Id=id,Frame=frame,Device=42,Flags=flags,Himetric=new(){X=x,Y=y},Pixel=new(){X=999,Y=888}}};
    var first=frames.Read([Point(100000,1,3100,3200),Point(200000,1,7100,3200)],bounds)!;
    Check(first.Length==2 && first[0].X==.3 && first[0].Y==.5 && first[1].X==.7,"routed touchpad uses physical contacts, not the frozen cursor position");
    Check(first.Select(p=>p.Id).SequenceEqual(new[]{0,1}),"native 32-bit pointer identities map to bounded wire contact IDs");
    Check(frames.Read([Point(100000,1,3100,3200)],bounds)==null,"each native frame forwards once despite per-finger callbacks");
    var changed=frames.Read([Point(200000,2,8100,3200),Point(100000,2,2100,3200)],bounds)!;
    Check(changed[0].Id==first[0].Id && changed[0].X==.2 && changed[1].Id==first[1].Id && changed[1].X==.8,"reordered native callbacks preserve contact identity and pinch motion");
    Check(frames.Read([Point(100000,3,2100,3200,0x4000),Point(200000,3,8100,3200,0x4000)],bounds)!.Length==0,"native release produces a complete gesture end");
    Check(frames.Read([Point(1,4,2000,2000,0xc004)],bounds)!.Length==0,"cancelled native frame cannot leave a held contact");
    frames.Reset();
    Check(frames.Read([Point(1,1,100,200),Point(2,1,10100,6200)],bounds) is [{X:0,Y:0},{X:1,Y:1}],"focus change resets frame deduplication and device-relative bounds");
    Check(frames.Read([Point(1,5,100,200)],default)==null,"invalid device dimensions cannot generate bogus coordinates");
}
// Native focus ownership must restore the displaced window, never a new user choice.
{
    nint current=100;var calls=new List<nint>();var valid=new HashSet<nint>{100,200,300};
    var lease=new TouchpadFocusLease(200,()=>current,w=>valid.Contains(w),w=> { calls.Add(w);current=w;return true; });
    Check(lease.Acquire() && current==200 && lease.Active,"remote touchpad capture owns foreground");
    Check(lease.Acquire() && calls.Count==1,"repeated capture never overwrites the original foreground window");
    lease.Release();Check(current==100 && !lease.Active,"return to Windows restores the exact displaced window");
    lease.Release();Check(calls.Count==2,"repeated release never changes focus");
    lease.Acquire();current=300;lease.Release();
    Check(current==300,"external foreground change is not overwritten during release");
    current=100;lease.Acquire();valid.Remove(100);lease.Release();
    Check(current==200,"closed original window is never reactivated");
    current=100;
    var denied=new TouchpadFocusLease(200,()=>current,_=>true,_=>false);
    Check(!denied.Acquire() && !denied.Active && current==100,"denied foreground capture never reports successful ownership");
    var partial=new TouchpadFocusLease(200,()=>current,_=>true,w=> { current=w;return w!=200; });
    Check(!partial.Acquire() && current==100,"partial native activation failure restores previous focus");
}
if(args.Contains("--interop")) {
    using var deadline=new CancellationTokenSource(TimeSpan.FromSeconds(30));
    using var pairedIdentity=ECDiffieHellman.Create(ECCurve.NamedCurves.nistP256);
    byte[] pairedPrivate=pairedIdentity.ExportPkcs8PrivateKey();
    ECDiffieHellman PairedIdentity() { var key=ECDiffieHellman.Create();key.ImportPkcs8PrivateKey(pairedPrivate,out _);return key; }
    var completed=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
    string[] types=["display","layout","pointer","key","button","scroll","caps","touchpadControl","touchpad","touchpadFeedback","gameMode","gameModeRequest","relativePointer","reset"];
    var received=new HashSet<string>();
    var receivedTouchpad=new List<Message>();
    Message? receivedFeedback=null,receivedGameMode=null,receivedRelative=null,receivedGameRequest=null,receivedScroll=null;
    var clipboardToken=new TaskCompletionSource<byte[]>(TaskCreationOptions.RunContinuationsAsynchronously);
    bool pinned=false;
    var client=new FlowClient(PairedIdentity,()=>null,peer=>pinned=peer.Length==65,port:49334,discoveryPort:49335);
    client.OnStatus=(ready,status)=> {
        Console.WriteLine("STATUS "+status);
        if(ready) {
            client.Send(new("display") { Width=2560,Height=1600,Features=["clipboard-v1"] });
            client.Send(new("layout") { Side="left",Offset=250 });
            client.Send(new("pointer") { Active="windows",X=.75,Y=.25 });
            client.Send(new("key") { Key=4,Down=true });
            client.Send(new("button") { Button=0,Down=false });
            client.Send(new("scroll") { Dx=-1.0/3,Dy=2.0/3 });
            client.Send(new("caps") { Down=true });
            client.Send(new("gameMode") {Down=true,Phase="manual",Active="windows",X=.75,Y=.25});
            client.Send(new("relativePointer") {Dx=37.5,Dy=-9});
            client.Send(new("gameModeRequest") {Down=false});
            client.Send(new("touchpadControl") { Down=true });
            client.Send(new("touchpad") { Phase="begin",GestureEpoch=42,GestureTime=123.5,Contacts=[new(1,.3,.5),new(2,.7,.5)] });
            client.Send(new("touchpad") { Phase="change",GestureEpoch=42,GestureTime=123.55,Contacts=[new(1,.2,.5),new(2,.8,.5)] });
            client.Send(new("touchpad") { Phase="end",GestureEpoch=42,GestureTime=123.6,Contacts=[] });
            client.Send(new("touchpadFeedback") {GestureEpoch=42,Frames=3,Events=2,Phase="submitted",Backend="quartz"});
            client.Send(new("reset"));
        }
    };
    client.OnMessage=m=> { if(m.Type=="clipboardReady") clipboardToken.TrySetResult(Convert.FromHexString(m.Nonce!)); lock(received) { if(m.Type=="touchpad")receivedTouchpad.Add(m);if(m.Type=="scroll")receivedScroll=m;if(m.Type=="touchpadFeedback")receivedFeedback=m;if(m.Type=="gameMode")receivedGameMode=m;if(m.Type=="relativePointer")receivedRelative=m;if(m.Type=="gameModeRequest")receivedGameRequest=m;received.Add(m.Type); if(types.All(received.Contains)) completed.TrySetResult(); } };
    var run=client.Connect(new BridgeAdapter("loopback",IPAddress.Loopback,IPAddress.Parse("255.0.0.0"),true));
    try {
        await completed.Task.WaitAsync(deadline.Token); Check(pinned,"Swift/.NET ECDH authenticated discovery and identity exchange");
        Check(types.All(received.Contains),"Swift/.NET fourteen encrypted message types including game lock and relative motion over real TCP");
        Check(receivedGameMode is {Down:true,Phase:"manual",Active:"windows",X:.75,Y:.25} && receivedRelative is {Dx:37.5,Dy:-9} && receivedGameRequest is {Down:false},"game lock snapshot, manual escape request and signed fractional relative motion survive Swift/.NET TCP");
        Check(receivedScroll?.Dx==-1.0/3 && receivedScroll.Dy==2.0/3,"fractional two-finger scroll survives Swift/.NET encrypted TCP without rounding");
        Check(receivedTouchpad.Select(m=>m.Phase).SequenceEqual(new[]{"begin","change","end"}) &&
              receivedTouchpad[0].Contacts![0]==new TouchpadContact(1,.3,.5) && receivedTouchpad[1].Contacts![1]==new TouchpadContact(2,.8,.5) && receivedTouchpad[2].Contacts!.Length==0 && receivedTouchpad.All(m=>m.GestureEpoch==42),
              "native touchpad phases, stable contact IDs and precise coordinates survive Swift/.NET encryption");
        Check(receivedTouchpad.Select(m=>m.GestureTime).SequenceEqual(new double?[]{123.5,123.55,123.6}),"raw source timestamps survive Swift/.NET encrypted TCP without conversion to arrival time");
        Check(receivedFeedback is {GestureEpoch:42,Frames:3,Events:2,Phase:"submitted",Backend:"quartz"},"receiver backend and submitted-versus-received feedback survive Swift/.NET encrypted TCP");
        byte[] cap=await clipboardToken.Task.WaitAsync(deadline.Token);
        using(var rejected=new TcpClient()) {
            await rejected.ConnectAsync(IPAddress.Loopback,49336,deadline.Token);
            await Wire.WriteFrame(rejected.GetStream(),Wire.Encode(new("hello") { Nonce=Convert.ToHexString(RandomNumberGenerator.GetBytes(32)),Auth=new string('0',64) }),deadline.Token);
            Check(await rejected.GetStream().ReadAsync(new byte[1],deadline.Token)==0,"clipboard channel rejects invalid session authentication");
        }
        await Task.Delay(100,deadline.Token);
        string fixture=Path.Combine(Path.GetTempPath(),"ListFXFlow-interop-"+Guid.NewGuid());
        string receiveCache=Path.Combine(Path.GetTempPath(),"ListFXFlow-interop-received-"+Guid.NewGuid());
        Directory.CreateDirectory(Path.Combine(fixture,"资料","空目录"));
        byte[] contents=RandomNumberGenerator.GetBytes(1200000);
        File.WriteAllText(Path.Combine(fixture,"另一个.txt"),"second root");
        File.WriteAllBytes(Path.Combine(fixture,"资料","内容.bin"),contents); File.WriteAllText(Path.Combine(fixture,"资料","空文件"),"");
        try {
            using var clipboard=new ClipboardLink(receiveCache);
            var ready=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var textDone=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var filesDone=new TaskCompletionSource<string[]>(TaskCreationOptions.RunContinuationsAsynchronously);
            var imageDone=new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
            var replaced=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var sendStarted=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            clipboard.OnReady=()=>ready.TrySetResult();
            clipboard.OnStatus=status=> { Console.WriteLine("CLIPBOARD "+status); if(status=="正在发送文件…") sendStarted.TrySetResult(); };
            clipboard.OnResult=(result,_)=> { if(result.Text=="最新复制内容") replaced.TrySetResult(); if(result.Text=="跨平台剪贴板 Unicode 🌏") textDone.TrySetResult(); if(result.Files!=null) filesDone.TrySetResult(result.Files); if(result.Image!=null) imageDone.TrySetResult(result.Image); return Task.FromResult(true); };
            var clipRun=clipboard.Run(IPAddress.Loopback,IPAddress.Loopback,cap,49336);
            await ready.Task.WaitAsync(deadline.Token);
            clipboard.LocalChanged("跨平台剪贴板 Unicode 🌏",null,1); await textDone.Task.WaitAsync(deadline.Token);
            Check(true,"encrypted Swift/.NET text clipboard round trip");
            clipboard.LocalChanged(null,[Path.Combine(fixture,"资料"),Path.Combine(fixture,"另一个.txt")],2);
            var firstClip=await Task.WhenAny(filesDone.Task,clipRun).WaitAsync(deadline.Token);
            if(firstClip==clipRun) { await clipRun; throw new Exception("Clipboard connection ended before files completed"); }
            var files=await filesDone.Task.WaitAsync(deadline.Token);
            Check(File.ReadAllBytes(Path.Combine(files[0],"内容.bin")).SequenceEqual(contents),"encrypted Swift/.NET multi-chunk file clipboard round trip");
            Check(Directory.Exists(Path.Combine(files[0],"空目录")) && new FileInfo(Path.Combine(files[0],"空文件")).Length==0,"cross-platform clipboard preserves empty directory and empty file");
            Check(files.Length==2 && File.ReadAllText(files[1])=="second root","multi-selection clipboard preserves separate top-level files");
            byte[] image=ClipboardImageTests.Fixture();
            clipboard.LocalChanged(null,null,3,image);
            string imagePath=await imageDone.Task.WaitAsync(deadline.Token);
            Check(File.ReadAllBytes(imagePath).SequenceEqual(image),"encrypted Swift/.NET PNG above 1 MiB round trip retains exact image representation and hash");
            sendStarted=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            clipboard.LocalChanged(null,[Path.Combine(fixture,"资料")],4);
            await sendStarted.Task.WaitAsync(deadline.Token);
            clipboard.LocalChanged("最新复制内容",null,5);
            await replaced.Task.WaitAsync(deadline.Token);
            Check(true,"new clipboard copy supersedes an active file transfer without breaking the channel");
            clipboard.Dispose(); await clipRun;
        } finally { Directory.Delete(fixture,true); if(Directory.Exists(receiveCache)) Directory.Delete(receiveCache,true); }

    } finally { client.Stop("Interop test complete"); await run; }
    var directDone=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
    var direct=new FlowClient(PairedIdentity,()=>null,_=>{},port:49334,discoveryPort:49335);
    direct.OnStatus=(ready,_)=> { if(ready) direct.Send(new("pointer") { Active="mac",X=.3,Y=.7 }); };
    direct.OnMessage=m=> { if(m.Type=="pointer" && m.X==.3 && m.Y==.7) directDone.TrySetResult(); };
    var directRun=direct.Connect(new BridgeAdapter("loopback",IPAddress.Loopback,IPAddress.Parse("255.0.0.0"),true),"127.0.0.1");
    try { await directDone.Task.WaitAsync(deadline.Token); Check(true,"manual IP bypasses discovery and completes encrypted exchange"); }
    finally { direct.Stop(); await directRun; }
    foreach(string saved in new[]{"127.0.0.1","127.0.0.2"}) {
        var recovered=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        bool fallback=false;
        var cached=new FlowClient(PairedIdentity,()=>null,_=>{},port:49334,discoveryPort:49335);
        cached.OnStatus=(ready,status)=> { if(status.StartsWith("上次地址不可用"))fallback=true;if(ready)recovered.TrySetResult(); };
        var cachedRun=cached.Connect(new BridgeAdapter("loopback",IPAddress.Loopback,IPAddress.Parse("255.0.0.0"),true),lastMacAddress:saved);
        try {
            await recovered.Task.WaitAsync(deadline.Token);
            Check(cached.PeerAddress?.Equals(IPAddress.Loopback)==true && fallback==(saved=="127.0.0.2"),saved=="127.0.0.1" ? "cached address authenticates directly":"unreachable cached address falls back to authenticated discovery");
        } finally { cached.Stop();await cachedRun; }
    }
    using(var outsider=new TcpClient()) {
        await outsider.ConnectAsync(IPAddress.Loopback,49334,deadline.Token);
        using var foreign=ECDiffieHellman.Create(ECCurve.NamedCurves.nistP256);
        await Wire.WriteFrame(outsider.GetStream(),Wire.Encode(new("hello") { Version=1,Nonce=Convert.ToHexString(RandomNumberGenerator.GetBytes(32)),PublicKey=Convert.ToHexString(Wire.Public(foreign)) }),deadline.Token);
        Check(await outsider.GetStream().ReadAsync(new byte[1],deadline.Token)==0,"Swift preserves the pinned peer across reconnects and rejects an unpaired identity");
    }
    // The real Swift listener must reject an unauthenticated finish.
    using var tcp=new TcpClient(); await tcp.ConnectAsync(IPAddress.Loopback,49334,deadline.Token);
    using var identity=PairedIdentity(); var network=tcp.GetStream();
    var hello=Wire.Encode(new("hello") { Version=1,Nonce=Convert.ToHexString(RandomNumberGenerator.GetBytes(32)),PublicKey=Convert.ToHexString(Wire.Public(identity)) });
    // Deliberately fragment the length header and payload.
    await using(var packed=new MemoryStream()) {
        await Wire.WriteFrame(packed,hello,deadline.Token); var bytes=packed.ToArray();
        foreach(byte value in bytes) await network.WriteAsync(new byte[]{value},deadline.Token);
    }
    var challenge=Wire.Decode(await Wire.ReadFrame(network,deadline.Token)); Check(challenge.Type=="challenge","Swift handles fragmented handshake");
    await Wire.WriteFrame(network,Wire.Encode(new("finish") { Auth=new string('0',64) }),deadline.Token);
    byte[] eof=new byte[1]; Check(await network.ReadAsync(eof,deadline.Token)==0,"Swift rejects invalid peer authentication");
    // Authenticated but silent peers must be disconnected by the monotonic heartbeat watchdog.
    using var silent=new TcpClient(); await silent.ConnectAsync(IPAddress.Loopback,49334,deadline.Token);
    using var silentIdentity=PairedIdentity(); var silentStream=silent.GetStream();
    byte[] cn=RandomNumberGenerator.GetBytes(32);
    await Wire.WriteFrame(silentStream,Wire.Encode(new("hello") { Version=1,Nonce=Convert.ToHexString(cn),PublicKey=Convert.ToHexString(Wire.Public(silentIdentity)) }),deadline.Token);
    var reply=Wire.Decode(await Wire.ReadFrame(silentStream,deadline.Token));
    byte[] sn=Convert.FromHexString(reply.Nonce!),shared=Wire.SharedSecret(silentIdentity,Convert.FromHexString(reply.PublicKey!));
    await Wire.WriteFrame(silentStream,Wire.Encode(new("finish") { Auth=Convert.ToHexString(Wire.Proof(shared,"client",cn,sn)) }),deadline.Token);
    using var silentSecure=new SecureChannel(shared,cn,sn,false);
    Check(silentSecure.Open(await Wire.ReadFrame(silentStream,deadline.Token)).Type=="ping","authenticated idle session starts");
    var stopwatch=System.Diagnostics.Stopwatch.StartNew();
    try { while(true) silentSecure.Open(await Wire.ReadFrame(silentStream,deadline.Token)); }
    catch(EndOfStreamException) { Check(stopwatch.Elapsed.TotalSeconds is >=2.5 and <6,"Swift closes silent authenticated peer after heartbeat timeout"); }
}
Console.WriteLine($"{passed} checks passed");

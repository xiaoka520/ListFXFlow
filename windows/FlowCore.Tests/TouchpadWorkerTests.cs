using ListFXFlow;
using System.Diagnostics;
using System.IO.Pipes;
using System.Runtime.InteropServices;

internal static class TouchpadWorkerTests {
    private static Process Launch(string pipe,string mode) {
        var dotnet=Path.GetFullPath(Path.Combine(RuntimeEnvironment.GetRuntimeDirectory(),"../../..",OperatingSystem.IsWindows()?"dotnet.exe":"dotnet"));
        var info=new ProcessStartInfo(dotnet) {UseShellExecute=false,CreateNoWindow=true};
        foreach(string arg in new[]{typeof(TouchpadWorkerTests).Assembly.Location,"--fake-touchpad",pipe,mode})info.ArgumentList.Add(arg);
        return Process.Start(info)!;
    }
    internal static async Task Child(string pipeName,string mode) {
        if(mode=="startup-hang") { await Task.Delay(Timeout.Infinite);return; }
        using var pipe=new NamedPipeClientStream(".",pipeName,PipeDirection.InOut,PipeOptions.Asynchronous);
        await pipe.ConnectAsync(5000);
        await TouchpadWorkerWire.Write(pipe,new("ready"),CancellationToken.None);
        var contacts=new[]{new TouchpadContact(1,.3,.5),new TouchpadContact(2,.7,.5)};
        long epoch=0;bool remote=false;
        while(true) {
            var packet=await TouchpadWorkerWire.Read(pipe,CancellationToken.None);
            if(packet.Kind=="stop")return;
            if(packet.Kind=="configure") {
                epoch=packet.Epoch;remote=packet.Remote;
                if(mode=="global-forward") {
                    if(!packet.SuppressOnly)throw new Exception("Global forwarding must preserve system two-finger recognition");
                    await TouchpadWorkerWire.Write(pipe,new("state",epoch,Owned:remote,Status:packet.ForwardGestures ? "forwarding":"suppressed"),CancellationToken.None);
                    if(packet.ForwardGestures && remote) {
                        await TouchpadWorkerWire.Write(pipe,new("input",epoch,Input:new("touchpad") {Phase="begin",GestureEpoch=epoch,Contacts=contacts.Append(new TouchpadContact(3,.5,.7)).ToArray()}),CancellationToken.None);
                    } else await TouchpadWorkerWire.Write(pipe,new("input",epoch-1,Input:new("touchpad") {Phase="change",Contacts=contacts}),CancellationToken.None);
                    continue;
                }
                if(mode=="suppression") {
                    if(!packet.SuppressOnly)throw new Exception("Suppression mode was lost over IPC");
                    await TouchpadWorkerWire.Write(pipe,new("state",epoch,Owned:remote,Status:remote ? "suppressed":"local"),CancellationToken.None);
                    if(remote)await TouchpadWorkerWire.Write(pipe,new("scroll",epoch,Input:new("scroll") {Dx=-1.0/3,Dy=2.0/3}),CancellationToken.None);
                    else await TouchpadWorkerWire.Write(pipe,new("scroll",epoch-1,Input:new("scroll") {Dx=100,Dy=100}),CancellationToken.None);
                    continue;
                }
                if(mode=="foreground" && remote) {
                    await TouchpadWorkerWire.Write(pipe,new("state",epoch,Status:"waiting for grant"),CancellationToken.None);
                    await TouchpadWorkerWire.Write(pipe,new("foregroundRequest",epoch),CancellationToken.None);
                    continue;
                }
                if(mode=="interface-denied") {
                    await TouchpadWorkerWire.Write(pipe,new("error",packet.Epoch,Status:"QueryInterface(ITouchpadGesturesController) 失败 (HRESULT 0x80004002)"),CancellationToken.None);
                    continue;
                }
                if(mode=="crash")Environment.Exit(29);
                if(mode=="hang") { await Task.Delay(Timeout.Infinite);return; }
                await TouchpadWorkerWire.Write(pipe,new("state",packet.Epoch,Owned:packet.Remote,Status:"fake-ready"),CancellationToken.None);
                if(mode=="read-denied") {
                    await TouchpadWorkerWire.Write(pipe,new("captureWarning",packet.Epoch,Owned:packet.Remote,Status:"GetPointerFrameTouchpadInfo: Win32 5"),CancellationToken.None);
                    continue;
                }
                if(packet.Remote) {
                    await TouchpadWorkerWire.Write(pipe,new("input",packet.Epoch,Input:new("touchpad") {Phase="begin",Contacts=contacts}),CancellationToken.None);
                    int updates=mode=="flood"?10000:1;
                    for(int i=0;i<updates;i++)await TouchpadWorkerWire.Write(pipe,new("input",packet.Epoch,Input:new("touchpad") {Phase="change",Contacts=[new(1,.2+i%100*.001,.5),contacts[1]]}),CancellationToken.None);
                    await TouchpadWorkerWire.Write(pipe,new("input",packet.Epoch,Input:new("touchpad") {Phase="end",Contacts=[]}),CancellationToken.None);
                }
            } else if(packet.Kind=="activate" && mode=="foreground" && remote && packet.Epoch==epoch) {
                await TouchpadWorkerWire.Write(pipe,new("state",epoch,Owned:true,Status:"foreground confirmed"),CancellationToken.None);
            } else if(packet.Kind=="reset" && mode=="read-denied") {
                await TouchpadWorkerWire.Write(pipe,new("state",packet.Epoch,Owned:true,Status:"fake-recovered"),CancellationToken.None);
                await TouchpadWorkerWire.Write(pipe,new("input",packet.Epoch,Input:new("touchpad") {Phase="begin",Contacts=contacts}),CancellationToken.None);
            } else if(packet.Kind=="input") {
                await TouchpadWorkerWire.Write(pipe,new("state",packet.Epoch,Status:"fake-injected"),CancellationToken.None);
            }
        }
    }
    private static async Task Wait(Func<bool> condition,int ms=4000) {
        long deadline=Environment.TickCount64+ms;
        while(!condition()) { if(Environment.TickCount64>deadline)throw new Exception("Worker test timed out");await Task.Delay(10); }
    }
    internal static async Task Run(Action<bool,string> check) {
        TouchpadDiagnostics.DirectoryOverride=Path.GetFullPath(".build/worker-test-diagnostics");
        var contacts=new[]{new TouchpadContact(1,.3,.5),new TouchpadContact(2,.7,.5)};
        var queue=new TouchpadWorkerQueue(4);
        queue.Offer(new("input",1,Input:new("touchpad") {Phase="begin",Contacts=contacts}));
        for(int i=0;i<10000;i++)if(!queue.Offer(new("input",1,Input:new("touchpad") {Phase="change",Contacts=contacts})))throw new Exception("Worker update queue overflowed");
        queue.Offer(new("input",1,Input:new("touchpad") {Phase="end",Contacts=[]}));
        check(queue.Count==3,"10k IPC contact updates stay bounded without losing begin/end barriers");
        queue.Replace(new("configure",2));
        check(queue.Count==1 && queue.TryTake(out var config) && config!.Epoch==2,"ownership changes discard queued input from the old epoch");
        await using(var bad=new MemoryStream(new byte[]{0,1,0,0})) {
            bool rejected=false;try { await TouchpadWorkerWire.Read(bad,CancellationToken.None); } catch(InvalidDataException) { rejected=true; }
            check(rejected,"worker framing rejects oversized payload before allocating or reading it");
        }
        int launched=0;
        using(var client=new TouchpadWorkerClient(()=>{},pipe=>Launch(pipe,"suppression"),1500)) {
            client.Configure(true,true,suppressOnly:true);
            await Wait(()=>client.Owned);
            var received=new List<TouchpadWorkerPacket>();
            await Wait(()=> {while(client.TryTake(out var packet))received.Add(packet!);return received.Any(p=>p.Kind=="scroll");});
            check(received.Any(p=>p.Status=="suppressed") && received.All(p=>p.Input==null || p.Input.Type=="scroll"),"suppression-only configuration crosses real IPC with scrolling but without contact frames");
            var scroll=received.Single(p=>p.Kind=="scroll").Input!;
            check(scroll.Dx==-1.0/3 && scroll.Dy==2.0/3,"two-finger fractional horizontal and vertical scroll survive isolated worker IPC");
            client.Configure(true,false,suppressOnly:true);
            await Wait(()=> {while(client.TryTake(out var p))if(p!.Status=="local")return true;return false;});
            client.Post(new("touchpad") {Phase="begin",Contacts=contacts});
            await Task.Delay(100);
            bool injected=false,staleScroll=false;while(client.TryTake(out var p)) { injected|=p!.Status=="fake-injected";staleScroll|=p.Kind=="scroll"; }
            check(!client.Owned && !injected && !staleScroll,"suppression mode releases local ownership and rejects obsolete gesture injection and stale scroll");
            client.Configure(false,false,suppressOnly:true);await client.Completion.WaitAsync(TimeSpan.FromSeconds(4));
        }
        using(var client=new TouchpadWorkerClient(()=>{},pipe=>Launch(pipe,"global-forward"),1500)) {
            client.Configure(true,true,suppressOnly:true,forwardGestures:true);
            var received=new List<TouchpadWorkerPacket>();
            await Wait(()=> {while(client.TryTake(out var p))received.Add(p!);return received.Any(p=>p.Input?.Phase=="begin");});
            check(client.Owned && received.Any(p=>p.Input?.Contacts?.Length==3),"global-contact forwarding opt-in crosses real IPC while retaining suppression-only two-finger mode");
            long firstEpoch=client.Epoch;
            client.Configure(true,true,suppressOnly:true,forwardGestures:false);
            received.Clear();
            await Wait(()=> {while(client.TryTake(out var p))received.Add(p!);return received.Any(p=>p.Status=="suppressed");});
            check(client.Epoch>firstEpoch && client.Owned && received.All(p=>p.Input==null),"disabling forwarding changes epoch and rejects stale contacts without releasing local gesture suppression");
            client.Configure(false,false,suppressOnly:true);await client.Completion.WaitAsync(TimeSpan.FromSeconds(4));
        }
        int grants=0,grantedPid=0,workerPid=0;
        using(var client=new TouchpadWorkerClient(()=>{},pipe=> {var p=Launch(pipe,"foreground");workerPid=p.Id;return p;},1500,pid=> {grants++;grantedPid=pid;return true;})) {
            for(int crossing=1;crossing<=2;crossing++) {
                client.Configure(true,true);long epoch=client.Epoch;
                await Wait(()=>client.HasPending);
                client.GrantForeground(epoch-1);
                check(grants==crossing-1,"stale foreground request is ignored for crossing "+crossing);
                await Wait(()=> {
                    while(client.TryTake(out var packet))if(packet!.Kind=="foregroundRequest")client.GrantForeground(packet.Epoch);
                    return client.Owned;
                });
                check(grants==crossing && grantedPid==workerPid,"specific worker receives a renewed parent grant before activation for crossing "+crossing);
                client.Configure(true,false);
                await Wait(()=> {while(client.TryTake(out var packet))if(packet!.Kind=="state" && !packet.Owned)return true;return false;});
                client.GrantForeground(epoch);
                check(grants==crossing && !client.Owned,"return to Windows rejects late foreground activation for crossing "+crossing);
            }
            client.Configure(false,false);await client.Completion.WaitAsync(TimeSpan.FromSeconds(4));
        }
        using(var client=new TouchpadWorkerClient(()=>{},pipe=> {Interlocked.Increment(ref launched);return Launch(pipe,"normal");},1500)) {
            client.Configure(false,false);await Task.Delay(30);
            check(launched==0,"disabled or pre-handshake gestures never start a native worker");
            var elapsed=Stopwatch.StartNew();client.Configure(true,true);elapsed.Stop();
            check(elapsed.ElapsedMilliseconds<100,"enabling gestures does not wait for worker startup on the caller");
            await Wait(()=>client.Owned);
            var received=new List<TouchpadWorkerPacket>();
            await Wait(()=> {while(client.TryTake(out var p))received.Add(p!);return received.Any(p=>p.Input?.Phase=="end");});
            check(received.Where(p=>p.Input!=null).Select(p=>p.Input!.Phase).SequenceEqual(new[]{"begin","change","end"}),"isolated worker contacts cross a real pipe in phase order");
            client.Configure(true,false);
            await Wait(()=> {while(client.TryTake(out var p))if(p!.Kind=="state" && !p.Owned)return true;return false;});
            client.Post(new("touchpad") {Phase="begin",Contacts=contacts});
            await Wait(()=> {while(client.TryTake(out var p))if(p!.Status=="fake-injected")return true;return false;});
            check(!client.Owned && launched==1,"return to Windows releases capture and routes injection through the same child");
            client.Configure(false,false);await client.Completion.WaitAsync(TimeSpan.FromSeconds(4));
            check(!client.Owned,"switching off waits only in the test, not in the input or UI caller");
        }
        foreach(string failure in new[]{"hang","crash","startup-hang"}) {
            launched=0;int childId=0;
            using var client=new TouchpadWorkerClient(()=>{},pipe=> {Interlocked.Increment(ref launched);var p=Launch(pipe,failure);childId=p.Id;return p;},500);
            client.Configure(true,true);
            await client.Completion.WaitAsync(TimeSpan.FromSeconds(5));
            var errors=new List<TouchpadWorkerPacket>();while(client.TryTake(out var p))errors.Add(p!);
            check(errors.Count(p=>p.Kind=="error")==1 && !client.Owned,$"{failure}: report one gesture failure without killing the parent");
            bool exited;
            try { using var child=Process.GetProcessById(childId);exited=child.HasExited; } catch(ArgumentException) { exited=true; }
            check(exited,$"{failure}: stalled or exited child is fully reclaimed");
            client.Configure(true,true);await Task.Delay(30);
            check(launched==1,$"{failure}: no automatic worker restart loop");
        }
        using(var client=new TouchpadWorkerClient(()=>{},pipe=>Launch(pipe,"read-denied"),1500)) {
            client.Configure(true,true);
            await Wait(()=> {while(client.TryTake(out var p))if(p!.Kind=="captureWarning")return true;return false;});
            check(client.Owned && !client.Completion.IsCompleted,"Win32 5 sample warning crosses IPC without releasing interception or stopping the worker");
            client.ResetInjection();
            await Wait(()=> {while(client.TryTake(out var p))if(p!.Input?.Phase=="begin")return true;return false;});
            check(client.Owned,"a recovered read forwards contacts through the existing worker without a restart");
            client.Configure(true,false);
            await Wait(()=>!client.Owned);
            check(!client.Completion.IsCompleted,"return to Windows still releases interception after a sample read failure");
            client.Configure(false,false);await client.Completion.WaitAsync(TimeSpan.FromSeconds(4));
        }
        using(var client=new TouchpadWorkerClient(()=>{},pipe=>Launch(pipe,"interface-denied"),1500)) {
            client.Configure(true,true);await client.Completion.WaitAsync(TimeSpan.FromSeconds(4));
            var messages=new List<TouchpadWorkerPacket>();while(client.TryTake(out var packet))messages.Add(packet!);
            check(!client.Owned && messages is [{Kind:"error"}],"a native interface initialization failure never reports successful ownership and stops only its worker");
            check(messages[0].Status!.Contains("QueryInterface") && messages[0].Status!.Contains("0x80004002"),"the UI receives the exact failed native initialization stage and HRESULT");
        }
        launched=0;
        using(var client=new TouchpadWorkerClient(()=>{},pipe=> {Interlocked.Increment(ref launched);return Launch(pipe,"hang");},500)) {
            client.Configure(true,true);await client.Completion.WaitAsync(TimeSpan.FromSeconds(5));
            client.Configure(false,false);client.Retry();client.Configure(true,true);
            await client.Completion.WaitAsync(TimeSpan.FromSeconds(5));
            check(launched==2,"explicit off/on retries a failed helper exactly once");
        }
        using(var client=new TouchpadWorkerClient(()=>{},pipe=>Launch(pipe,"flood"),3000)) {
            client.Configure(true,true);await Wait(()=>client.Owned);
            await Task.Delay(400);
            var received=new List<TouchpadWorkerPacket>();
            await Wait(()=> {while(client.TryTake(out var p))received.Add(p!);return received.Any(p=>p.Input?.Phase=="end");});
            check(received.All(p=>p.Kind!="error") && received.Count<1000,"real IPC burst coalesces contact changes while the consumer is busy");
            client.Configure(false,false);await client.Completion.WaitAsync(TimeSpan.FromSeconds(4));
        }
        using(var client=new TouchpadWorkerClient(()=>{},pipe=>Launch(pipe,"startup-hang"),1500)) {
            client.Configure(true,true);await Task.Delay(30);
            var elapsed=Stopwatch.StartNew();client.Configure(false,false);elapsed.Stop();
            await client.Completion.WaitAsync(TimeSpan.FromSeconds(4));
            bool error=false;while(client.TryTake(out var packet))error|=packet!.Kind=="error";
            check(elapsed.ElapsedMilliseconds<100 && !error,"switching off during startup cancels promptly without reporting a stale failure");
        }
        using(var client=new TouchpadWorkerClient(()=>{},pipe=>Launch(pipe,"normal"),1500)) {
            client.Configure(true,true);client.Configure(false,false);client.Retry();client.Configure(true,true);
            await Wait(()=>client.Owned);
            bool error=false;while(client.TryTake(out var packet))error|=packet!.Kind=="error";
            check(!error,"rapid off/on ignores cancellation from the replaced startup session");
            client.Configure(false,false);await client.Completion.WaitAsync(TimeSpan.FromSeconds(4));
        }
        // An unrelated live input/network loop keeps making progress while the
        // native surrogate is deliberately hung. No app UI or native input runs.
        var listener=new System.Net.Sockets.TcpListener(System.Net.IPAddress.Loopback,0);listener.Start();
        using var socket=new System.Net.Sockets.TcpClient();
        var accept=listener.AcceptTcpClientAsync();await socket.ConnectAsync((System.Net.IPEndPoint)listener.LocalEndpoint);
        using var accepted=await accept;listener.Stop();using var stopEcho=new CancellationTokenSource();
        var echo=Task.Run(async()=> {
            var buffer=new byte[1];
            try { while(true) { await accepted.GetStream().ReadExactlyAsync(buffer,stopEcho.Token);await accepted.GetStream().WriteAsync(buffer,stopEcho.Token); } }
            catch(OperationCanceledException) { }
        });
        using(var client=new TouchpadWorkerClient(()=>{},pipe=>Launch(pipe,"hang"),500)) {
            client.Configure(true,true);int echoed=0;
            using var deadline=new CancellationTokenSource(4000);
            while(!client.Completion.IsCompleted) {
                byte[] one=[42];await socket.GetStream().WriteAsync(one,deadline.Token);await socket.GetStream().ReadExactlyAsync(one,deadline.Token);
                if(one[0]!=42)throw new Exception("Echo corruption");echoed++;await Task.Delay(10);
            }
            check(echoed>10 && socket.Connected,"live TCP traffic continues during worker hang and forced recovery");
        }
        stopEcho.Cancel();await echo;
    }
}

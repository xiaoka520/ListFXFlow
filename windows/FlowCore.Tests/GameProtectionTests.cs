using ListFXFlow;

internal static class GameProtectionTests {
    internal static void Run(Action<bool,string> check) {
        var game=new GameProtection();game.Observe(123);
        check(game.Locked && game.Phase=="automatic","capture automatically protects Windows");
        game.Toggle();game.Observe(123);
        check(!game.Locked,"manual escape is not immediately rearmed by the same game capture");
        game.Observe(456);check(game.Locked,"a new foreground capture clears the temporary escape");
        game.Observe(0);check(!game.Locked,"game releases capture or Alt-Tab releases automatic protection");
        game.SetManual(true);game.Observe(0);check(game.Locked && game.Phase=="manual","explicit lock survives capture release until explicitly unlocked");
        game.Reset();check(!game.Locked,"disconnect or stop clears manual and automatic protection");
        game.AutomaticEnabled=false;game.Observe(123);check(!game.Locked,"automatic detection can be disabled without disabling manual lock");
        game.SetManual(true);check(game.Locked,"manual lock works with automatic detection disabled");
        var desktop=new CaptureRect(0,0,2560,1600);var small=new CaptureRect(10,10,1200,900);
        check(GameCapturePolicy.Captured(true,desktop,desktop,desktop,desktop),"hidden cursor in fullscreen is a capture heuristic");
        check(GameCapturePolicy.Captured(true,desktop,desktop,small,small),"hidden confined cursor also protects a windowed game");
        check(!GameCapturePolicy.Captured(false,desktop,desktop,desktop,small),"a visible fullscreen cursor does not trigger automatic protection");
        check(!GameCapturePolicy.Captured(true,desktop,desktop,small,desktop),"hidden ordinary window without confinement does not trigger protection");
        check(!GameCapturePolicy.Captured(true,desktop,desktop,default,small),"invalid foreground geometry cannot claim game capture");
        foreach(string side in new[]{"left","right","top","bottom"}) {
            var router=new PointerRouter();router.Reset("mac");router.Topology.Side=side;
            var edge=side switch {"left"=>(0,500,-10,0),"right"=>(1599,500,10,0),"top"=>(800,0,0,-10),_=>(800,999,0,10)};
            router.Move("mac",edge.Item1,edge.Item2,edge.Item3,edge.Item4,1600,1000);
            var initial=router.Position;router.CrossingLocked=true;
            check(router.Move("mac",0,0,-edge.Item3*100000,-edge.Item4*100000,1600,1000).Active=="windows","game lock blocks Mac-source return across "+side);
            var before=router.Position;
            check(!router.Accept(new("pointer") {Active="mac",X=.5,Y=.5}) && router.Position==before,"in-flight foreign ownership cannot break game lock "+side);
            router.Reset("windows");check(!router.CrossingLocked,"reset clears router lock "+side);
        }
        var shortcut=new GameShortcut();
        check(shortcut.Process(true,true,true)==(true,true),"Ctrl Alt L toggles exactly once");
        check(shortcut.Process(true,true,true)==(true,false),"L autorepeat is consumed without toggling");
        check(shortcut.Process(false,false,false)==(true,false),"captured key release is consumed after modifiers release");
        check(shortcut.Process(true,false,true)==(false,false) && shortcut.Process(true,true,true)==(false,false),"adding modifiers to an already held L cannot trigger a lock");
        shortcut.Reset();check(shortcut.Process(true,true,false)==(false,false),"shortcut is ordinary input outside Windows ownership");
        foreach(var invalid in new[]{new Message("gameMode"),new("gameMode") {Down=true,Phase="off",Active="windows",X=.5,Y=.5},new("gameMode") {Down=true,Phase="manual",Active="mac",X=.5,Y=.5},new("relativePointer") {Dx=double.NaN,Dy=0}}) {
            bool rejected=false;try {invalid.Validate();}catch {rejected=true;}check(rejected,"invalid game control or relative movement rejected");
        }
        var queue=new InputMessageQueue(8);
        queue.TryEnqueue(new("relativePointer") {Dx=5,Dy=-2});queue.TryEnqueue(new("relativePointer") {Dx=7,Dy=1});
        queue.TryEnqueue(new("button") {Button=0,Down=true});queue.TryEnqueue(new("relativePointer") {Dx=-3,Dy=0});
        queue.TryEnqueue(new("gameMode") {Down=false,Phase="off",Active="windows",X=.5,Y=.5});
        queue.TryEnqueue(new("pointer") {Active="windows",X=.2,Y=.3});
        var packets=new List<Message>();while(queue.TryDequeue(out var packet))packets.Add(packet!);
        check(packets.Select(p=>p.Type).SequenceEqual(new[]{"relativePointer","button","relativePointer","gameMode","pointer"}) && packets[0].Dx==12 && packets[0].Dy==-1,"relative deltas sum while clicks and mode changes remain ordering barriers");
        using var entered=new ManualResetEventSlim();using var release=new ManualResetEventSlim();using var done=new ManualResetEventSlim();
        var injected=new List<InjectionWork>();
        using(var worker=new InjectionWorker(work=> {
            lock(injected)injected.Add(work);
            if(work.Input.Type==1) {entered.Set();release.Wait(3000);}
            if(work.Input.Type==0 && work.Input.Union.Mouse.Flags==2)done.Set();
        })) {
            worker.Post(new(new Native.Input {Type=1}));
            check(entered.Wait(1000),"relative-motion worker test entered blocked native call");
            bool accepted=true;
            for(int i=0;i<10000;i++)accepted&=worker.Post(new(new Native.Input {Type=0,Union=new Native.InputUnion {Mouse=new Native.MouseInput {X=2,Y=-1,Flags=1}}},Relative:true));
            worker.Post(new(new Native.Input {Type=0,Union=new Native.InputUnion {Mouse=new Native.MouseInput {Flags=2}}}));
            release.Set();check(done.Wait(3000),"relative burst drains before its click barrier");
            lock(injected)check(accepted && injected.Where(w=>w.Relative).Sum(w=>w.Input.Union.Mouse.X)==20000 && injected.Where(w=>w.Relative).Sum(w=>w.Input.Union.Mouse.Y)==-10000,"10k relative samples conserve displacement instead of replacing the last absolute position");
        }
    }
}

using ChainedBeasts.Rerolls;
using ChainedBeasts.Rerolls.Core;
var checks=0;
void Check(bool ok,string message){if(!ok)throw new Exception("FAIL: "+message);checks++;}
try
{
    using(var f=new Party(1)) {
        Check(f.Host.Service.State.Charges==1,"Fresh solo run has one shared charge.");
        f.Host.Vote(0);f.Pump();
        Check(f.Host.Service.State.Charges==0 && f.Host.Applied==1 && f.Host.Finished==1,"One solo click applies the full offer set and spends one charge.");
        f.Host.Vote(0);f.Pump();Check(f.Host.Applied==1,"Empty pool cannot reroll again.");
    }
    using(var f=new Party(2)) {
        Check(f.Nodes.All(n=>n.Service.State.Ok),"Matching peers handshake on the current screen.");
        f.Host.Vote(0);f.Pump();
        Check(f.Host.Service.Busy && f.Host.Applied==0 && f.Host.Service.State.Charges==1,"One multiplayer vote waits without spending.");
        f.Nodes[1].Vote(1);f.Pump();
        Check(f.Nodes.All(n=>n.Items[0].Id==11 && n.Service.State.Charges==0),"Host and client apply identical concrete offers and share the debit.");
        Check(f.Nodes.All(n=>n.Prepared==1 && n.Applied==1 && n.Finished==1),"Each machine prepares/applies/finalizes exactly once.");
    }
    using(var f=new Party(2,couch:true)) {
        f.Host.Vote(0);f.Nodes[1].Vote(1);f.Pump();
        Check(f.Host.Applied==0,"A second local couch character still needs to consent.");
        f.Nodes[1].Vote(2);f.Pump();Check(f.Host.Service.State.Charges==0,"All characters consent while each machine prepares only once.");
    }
    using(var f=new Party(2)) {
        Bus.DropFinish=true;f.Host.ThrowFinish=true;f.Nodes[1].ThrowFinish=true;
        f.Host.Vote(0);f.Nodes[1].Vote(1);f.Pump(60);
        Check(f.Nodes.All(n=>n.Items[0].Id==11 && n.Service.State.Charges==0 && !n.Service.Busy),
            "Commit receipts in host snapshots recover a missing final message; cleanup errors cannot refund or roll back committed offers.");
    }
    using(var f=new Party(1)) {
        f.Host.Vote(0);f.Host.Items=new[]{new Item(7),new Item(8),new Item(9)};f.Pump();
        Check(f.Host.Prepared==0 && f.Host.Service.State.Charges==1,"An input queued on an old offer screen cannot spend a charge on replacement offers.");
    }
    foreach(var failure in new[]{"prepare","apply","screen","disconnect","cancel"})
    using(var f=new Party(2)) {
        var client=f.Nodes[1];
        if(failure=="prepare")client.ThrowPrepare=true;
        if(failure=="apply")client.ThrowApply=true;
        f.Host.Vote(0);f.Pump();
        if(failure=="screen")f.Host.RoomAvailable=false;
        else if(failure=="disconnect")client.Online=false;
        else if(failure=="cancel"){client.Activate();client.Service.QueueVote(1,true);}
        else client.Vote(1);
        f.Pump(failure=="disconnect"?120:20);
        Check(f.Host.Service.State.Charges==1 && f.Host.Items[0].Id==1,"Interrupted "+failure+" keeps the charge and original host offers.");
        if(client.Online)Check(client.Items[0].Id==1,"Interrupted "+failure+" restores the client's original offers too.");
    }
    using(var f=new Party(2)) {
        f.Host.Vote(0);f.Pump();f.Pump(350);
        Check(!f.Host.Service.Busy && f.Host.Service.State.Charges==1,"Unanimity timeout clears votes without spending.");
    }
    using(var f=new Party(2)) {
        var host=f.Host;host.Activate();host.Service.Damage(host.Players.players[0],4);
        host.Session.Level.ActiveGameState=GameLevel.eGameState.LEVEL_COMPLETE;
        f.Pump();
        Check(host.Service.State.Charges==2,"One undamaged survivor earns one party charge even if another player took damage.");
        host.Activate();f.Pump();
        Check(host.Service.State.Charges==2,"Duplicate end callbacks cannot reward again.");
        host.Vote(0);f.Nodes[1].Vote(1);f.Pump();
        Check(host.Service.State.Charges==1,"A potion/skill/merchant reroll draws from the earned shared balance.");
        f.Nodes.ForEach(n=>n.Kind=OfferKind.Potions);f.Pump(30);
        host.Vote(0);f.Nodes[1].Vote(1);f.Pump();Check(host.Service.State.Charges==0,"The next screen uses the remaining charge, not a separate category pool.");
    }
    using(var f=new Party(1)) {
        f.Host.Activate();f.Host.Service.Damage(f.Host.Players.players[0],1);f.Host.Session.Level.ActiveGameState=GameLevel.eGameState.LEVEL_COMPLETE;
        f.Pump();Check(f.Host.Service.State.Charges==1,"Healing to full after damage does not earn a flawless reward.");
        f.Host.Vote(0);f.Pump();f.Host.Activate();f.Host.Service.Dispose();f.Host.Service=new();f.Host.Tick();f.Pump();
        Check(f.Host.Service.State.Charges==0,"Resume restores the host ledger instead of gifting a new starting charge.");
    }
    using(var f=new Party(2)) {
        f.Nodes[1].Build="different-build";f.Host.Activate();f.Host.Service.Dispose();f.Host.Service=new();f.Host.Service.Fresh(f.Host.Session.runDataManager,71,12345);f.Pump(100);
        f.Host.Vote(0);f.Nodes[1].Vote(1);f.Pump();Check(f.Host.Applied==0 && f.Host.Service.State.Charges==1,"Mismatched build cannot enter a reroll transaction.");
    }
    using(var f=new Party(1)) {
        f.Host.Session.Level.hasCompletedLevelForPeer=true;
        f.Host.Session.runDataManager.CompletedLevels=1;
        f.Host.Session.Level.ActiveGameState=GameLevel.eGameState.LEVEL_COMPLETE;f.Pump();
        Check(f.Host.Service.State.Charges==2,"Replicated victory awards without the private completion callback, even after native bookkeeping advanced.");
        f.Pump(60);Check(f.Host.Service.State.Charges==2,"Repeated terminal state polls cannot award again.");
    }
    using(var f=new Party(1,emptyStart:true)) {
        var p=new Player{playerData=f.Host.Session.runDataManager.playerDatas[0],clientID=new(){Value=new(){NetworkID=0,PlayerIndex=0}}};
        f.Host.Players.players.Add(p);f.Pump();
        f.Host.Session.Level.ActiveGameState=GameLevel.eGameState.LEVEL_COMPLETE;f.Pump();
        Check(f.Host.Service.State.Charges==2,"An initially empty PlayerManager does not permanently latch an empty battle roster.");
    }
    using(var f=new Party(1,spawnFlagFalse:true)) {
        f.Host.Session.Level.ActiveGameState=GameLevel.eGameState.LEVEL_COMPLETE;f.Pump();
        Check(f.Host.Service.State.Charges==2,"Native RUNNING scene state starts tracking even when the separate spawn flag is false.");
    }
    using(var f=new Party(1)) {
        f.Host.Players.players[0].playerData.lastFightDamageTaken=7;
        f.Host.Session.Level.ActiveGameState=GameLevel.eGameState.LEVEL_COMPLETE;f.Pump();
        Check(f.Host.Service.State.Charges==1,"Native damage counter prevents a false flawless reward when callbacks were absent and HP was healed.");
    }
    using(var f=new Party(2)) {
        f.Host.Players.players[0].IsDead=true;f.Host.Players.players[0].health.Value=0;
        f.Host.Players.players[1].playerData.lastFightDamageTaken=1;
        f.Host.Session.Level.ActiveGameState=GameLevel.eGameState.LEVEL_COMPLETE;f.Pump();
        Check(f.Host.Service.State.Charges==1,"An undamaged dead character cannot qualify in place of the damaged surviving character.");
    }
    using(var f=new Party(1)) {
        f.Host.Session.Level.ActiveGameState=GameLevel.eGameState.DEMO_COMPLETE;f.Pump();
        Check(f.Host.Service.State.Charges==2,"Native final-day victory state also earns the flawless bonus.");
    }
    using(var f=new Party(1)) {
        f.Host.Session.Level.ActiveGameState=GameLevel.eGameState.LEVEL_FAILED;f.Pump();
        Check(f.Host.Service.State.Charges==1,"Polling a failed battle never awards a charge.");
    }
    using(var f=new Party(1,emptyStart:true)) {
        var p=new Player{playerData=f.Host.Session.runDataManager.playerDatas[0],clientID=new(){Value=new(){NetworkID=0,PlayerIndex=0}}};
        f.Host.Activate();f.Host.Service.Damage(p,1);f.Host.Players.players.Add(p);f.Pump();
        f.Host.Session.Level.ActiveGameState=GameLevel.eGameState.LEVEL_COMPLETE;f.Pump();
        Check(f.Host.Service.State.Charges==1,"Damage seen before full roster registration survives delayed battle initialization.");
    }
    Check(!AppDomain.CurrentDomain.GetAssemblies().Any(a=>a.GetName().Name is "Assembly-CSharp" or "Unity.Netcode.Runtime"),"Checks never loaded a game/runtime DLL.");
    Console.WriteLine($"PASS: {checks} production service/transport/store integration checks with inert NGO/Steam/native offers.");
}
finally {Bus.RemoveFixture();}

sealed class Party:IDisposable
{
    internal List<Node> Nodes=new();internal Node Host=>Nodes[0];
    internal Party(int count,bool couch=false,bool emptyStart=false,bool spawnFlagFalse=false)
    {
        Bus.Nodes.Clear();Bus.Pending.Clear();Bus.Tags.Clear();Bus.Now=0;Bus.DropFinish=false;
        Bus.Root=Path.Combine(Path.GetTempPath(),"cb-reroll-checks-"+Guid.NewGuid().ToString("N"));
        var seats=Enumerable.Range(0,count).Select(i=>new Seat((ulong)i,i)).ToList();if(couch)seats.Add(new(1,2));
        for(var i=0;i<count;i++)Nodes.Add(new((ulong)i,seats.ToArray()));if(emptyStart)Host.Players.players.Clear();if(spawnFlagFalse)Host.Session.Level.playersSpawned=false;Pump(60);
    }
    internal void Pump(int frames=20){for(var i=0;i<frames;i++){Bus.Now+=.1;foreach(var node in Nodes)node.Tick();Bus.Deliver();}}
    public void Dispose(){foreach(var node in Nodes){node.Activate();node.Service.Dispose();}Bus.RemoveFixture();}
}

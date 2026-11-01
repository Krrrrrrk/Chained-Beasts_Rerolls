using System.Text.Json;
using ChainedBeasts.Rerolls;using ChainedBeasts.Rerolls.Core;using UnityEngine;
var checks=0;
void Check(bool ok,string message){if(!ok)throw new Exception(message);checks++;}
if(args.Length>0)Directory.CreateDirectory(args[0]);
foreach(var size in new[]{(640,360),(1280,720),(1920,1080),(2560,1440)})
foreach(var mode in new[]{"idle","empty","waiting","voting","applying"})
foreach(var local in new[]{1,4}) {
    Screen.width=size.Item1;Screen.height=size.Item2;GUI.Commands.Clear();GUI.color=new(.2f,.3f,.4f,.5f);GUI.depth=9;GUI.enabled=false;
    var service=new Service {Seats=Enumerable.Range(0,local).Select(i=>new Seat(0,i)).Concat(new[]{new Seat(1,local)}).ToArray(),
        Busy=mode is "voting" or "applying",State=new(){Charges=mode=="empty"?0:3,Ok=mode!="waiting",Step=mode=="voting"?Step.Voting:mode=="applying"?Step.Applying:Step.Idle,Votes=new[]{0}}};
    var created=Texture2D.Created;var destroyed=Texture2D.Destroyed;
    using(var view=new RerollView()) {
        view.Draw(service);
        Check(GUI.color.r==.2f&&GUI.color.a==.5f&&GUI.depth==9&&!GUI.enabled,"Reroll view restores native GUI state.");
        Check(Texture2D.Created==created+1,"One cached embedded icon is used for the whole view.");
    }
    Check(Texture2D.Destroyed==destroyed+1,"Owned icon texture is disposed exactly once.");
    if(args.Length>0)File.WriteAllText(Path.Combine(args[0],$"{mode}-{local}local-{size.Item1}x{size.Item2}.json"),
        JsonSerializer.Serialize(new{width=Screen.width,height=Screen.height,commands=GUI.Commands}));
}
foreach(var size in new[]{(640,360),(1280,720),(1920,1080),(2560,1440)}) {
    Screen.width=size.Item1;Screen.height=size.Item2;
    var scale=Math.Max(.35f,Math.Min(1.6f,Math.Min(Screen.width/1280f,Screen.height/720f)));
    var box=new Rect(Screen.width-286*scale,Screen.height-100*scale,262*scale,72*scale);
    foreach(var spot in new[]{(1f,1f),(34f,36f),(130f,36f),(261f,71f),(2f,70f)}) {
        var service=new Service {Seats=new[]{new Seat(0,0)},State=new(){Ok=true,Charges=3}};
        GUI.Click=new(box.x+spot.Item1*scale,box.y+spot.Item2*scale);
        using var view=new RerollView();view.Draw(service);view.Draw(service);
        Check(service.VotesQueued==1,"A click anywhere inside the reroll panel, including icon, border and padding, queues exactly one vote.");
    }
    foreach(var offset in new[]{(-1f,36f),(263f,36f),(130f,-1f),(130f,73f)}) {
        var service=new Service {Seats=new[]{new Seat(0,0)},State=new(){Ok=true,Charges=3}};
        GUI.Click=new(box.x+offset.Item1*scale,box.y+offset.Item2*scale);
        using var view=new RerollView();view.Draw(service);
        Check(service.VotesQueued==0,"Clicks outside the reroll panel cannot queue a vote.");
    }
    foreach(var state in new[]{new Packet{Ok=false,Charges=3},new Packet{Ok=true,Charges=0}}) {
        var service=new Service {Seats=new[]{new Seat(0,0)},State=state};
        GUI.Click=new(box.x+34*scale,box.y+36*scale);
        using var view=new RerollView();view.Draw(service);
        Check(service.VotesQueued==0,"The full panel stays disabled while waiting for peers or out of charges.");
    }
    GUI.Click=null;
}
Console.WriteLine($"PASS: {checks} production reroll presentation checks with inert GUI; 40 layout scenarios.");
namespace Unity.Netcode {internal sealed class NetworkManager {internal static NetworkManager Singleton=new();internal ulong LocalClientId=0;}}
namespace ChainedBeasts.Rerolls {
    internal sealed class NativeOffers {internal Room Room=new("sample",OfferKind.Equipment,1,0,Array.Empty<Item>());}
    internal sealed class Service {internal NativeOffers Visible=new();internal Seat[] Seats;internal Packet State;internal bool Busy;internal int VotesQueued;internal void QueueVote(int player,bool cancel=false){VotesQueued++;} }
}

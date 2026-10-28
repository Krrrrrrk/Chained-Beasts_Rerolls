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
Console.WriteLine($"PASS: {checks} production reroll presentation checks with inert GUI; 40 layout scenarios.");
namespace Unity.Netcode {internal sealed class NetworkManager {internal static NetworkManager Singleton=new();internal ulong LocalClientId=0;}}
namespace ChainedBeasts.Rerolls {
    internal sealed class NativeOffers {internal Room Room=new("sample",OfferKind.Equipment,1,0,Array.Empty<Item>());}
    internal sealed class Service {internal NativeOffers Visible=new();internal Seat[] Seats;internal Packet State;internal bool Busy;internal void QueueVote(int player,bool cancel=false){} }
}

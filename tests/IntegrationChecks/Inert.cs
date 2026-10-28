// Inert substitutes: no game, Steam, Unity, or IL2CPP assemblies are loaded by this executable.
using ChainedBeasts.Rerolls;
using ChainedBeasts.Rerolls.Core;
using Il2CppInterop.Runtime.InteropTypes.Arrays;
using Unity.Netcode;

internal static class Bus
{
    internal static readonly Dictionary<ulong, Node> Nodes=new();
    internal static readonly Queue<(ulong From,ulong To,byte[] Data)> Pending=new();
    internal static readonly Dictionary<string,string> Tags=new();
    internal static Node Active;
    internal static double Now;
    internal static bool DropFinish;
    internal static string Root=Path.Combine(Path.GetTempPath(),"cb-reroll-checks-"+Guid.NewGuid().ToString("N"));
    internal static void RemoveFixture()
    {
        var full=Path.GetFullPath(Root);var parent=Path.GetFullPath(Path.GetTempPath());
        if(!full.StartsWith(parent,StringComparison.OrdinalIgnoreCase)||!Path.GetFileName(full).StartsWith("cb-reroll-checks-",StringComparison.Ordinal))throw new IOException("Unexpected fixture path");
        if(Directory.Exists(full))Directory.Delete(full,true);
    }
    internal static void Deliver()
    {
        while(Pending.Count>0) {
            var (from,to,data)=Pending.Dequeue();
            if(DropFinish && Wire.TryDecode(data,out var packet) && packet.Op==Op.Finish) continue;
            if(Nodes.TryGetValue(to,out var node)&&node.Online) {node.Activate();node.Manager.CustomMessagingManager.Handler?.Invoke(from,new(data));}
        }
    }
}
internal sealed class Node
{
    internal readonly ulong Id;
    internal Service Service=new();
    internal NetworkManager Manager;
    internal NetworkSession Session=new();
    internal PlayerManager Players=new();
    internal bool Online=true, RoomAvailable=true, ThrowPrepare, ThrowApply, ThrowFinish;
    internal int Prepared, Applied, RolledBack, Finished;
    internal Item[] Items=new[]{new Item(1),new Item(2),new Item(3)};
    internal string Build="checked-build";
    internal OfferKind Kind=OfferKind.Skills;
    internal Node(ulong id,Seat[] seats)
    {
        Id=id;Manager=new(){LocalClientId=id,Pointer=new((int)id+1),IsServer=id==0,CustomMessagingManager=new(){Owner=id}};
        Session.Clients.AddRange(seats.Select(s=>new ClientIdentifier{NetworkID=s.Client,PlayerIndex=(ulong)s.Player}));
        foreach(var s in seats) {var data=new PlayerData{playerID=s.Player};Session.runDataManager.playerDatas.Add(data);Players.players.Add(new(){playerData=data,clientID=new(){Value=new(){NetworkID=s.Client,PlayerIndex=(ulong)s.Player}}});}
        Session.Pointer=new(100+(int)id);
        Bus.Nodes[id]=this;Activate();Service.Fresh(Session.runDataManager,71,12345);
    }
    internal void Activate() {Bus.Active=this;NetworkManager.Singleton=Manager;NetworkSession.Instance=Session;PlayerManager.Instance=Players;Plugin.Stamp=Build;UnityEngine.Time.realtimeSinceStartupAsDouble=Bus.Now;}
    internal void Tick(){Activate();if(Online)Service.Tick();}
    internal void Vote(int player){Activate();Service.QueueVote(player);}
}
public sealed class NativeList<T>:List<T>
{
    public U Cast<U>() where U:class => this as U;
}
namespace Il2CppSystem.Collections.Generic {public interface ICollection<T>{int Count{get;}}}
public sealed class Clients:List<ClientIdentifier>,Il2CppSystem.Collections.Generic.ICollection<ClientIdentifier>
{public T Cast<T>() where T:class=>this as T;}
public class Castable {public T TryCast<T>() where T:class=>this as T;}
public sealed class NetworkManagerEx {public static bool InstanceExists=>true;public static NetworkManagerEx Instance=new();public Castable Transport=new SteamTransportController();}
public sealed class SteamTransportController:Castable {public Steamworks.CSteamID lobbyID=new(77);}
public sealed class GameManager {public enum eState {MENU,GAME};public static bool InstanceExists=>true;public static GameManager Instance=new();public eState State=eState.GAME;}
public sealed class NetworkSession
{
    public static NetworkSession Instance;public static bool InstanceExists=>Instance!=null;
    public bool IsSpawned=true,TransitionPending,isRestoringFromCheckpoint,IsPaused;
    public IntPtr Pointer;public object Lobby=new();public GameLevel Level=new();public GameLevel sceneRoot=>Level;
    public Clients Clients=new();public RunDataManager runDataManager=new();
}
public class GameLevel:Castable {public enum eGameState{RUNNING,LEVEL_COMPLETE,LEVEL_FAILED,DEMO_COMPLETE};public enum eSceneState{NETWORK_SPAWN,SPAWN_LAYOUT,SPAWN_PLAYERS,SPAWN_CHAINS,RUNNING};public eSceneState activeSceneState=eSceneState.RUNNING;public bool playersSpawned=true,hasCompletedLevelForPeer;public eGameState ActiveGameState=eGameState.RUNNING;public string sceneName="arena";public ulong NetworkObjectId=100;}
public sealed class GameLevelTutorial:GameLevel{}
public sealed class RunDataManager {public int RunSeed=71,RunStartTime=12345,CompletedLevels;public bool checkpointRestored;public NativeList<PlayerData> playerDatas=new();}
public sealed class ClientIdentifier{public ulong NetworkID,PlayerIndex;}
public sealed class PlayerData{public int playerID,lastFightDamageTaken;}
public sealed class Variable<T>{public T Value;}
public sealed class Player{public bool IsSpawned=true,IsDead;public PlayerData playerData;public Variable<ClientIdentifier> clientID;public Variable<int> health=new(){Value=100};}
public sealed class PlayerManager{public static PlayerManager Instance;public static bool InstanceExists=>Instance!=null;public NativeList<Player> players=new();}
namespace UnityEngine {public static class Time {public static double realtimeSinceStartupAsDouble;}}
namespace Unity.Collections {public enum Allocator {Temp}}
namespace Il2CppInterop.Runtime.InteropTypes.Arrays
{
    public sealed class Il2CppStructArray<T>{public T[] Data;public Il2CppStructArray(int n){Data=new T[n];}public static explicit operator Il2CppStructArray<T>(T[] a)=>new(a.Length){Data=(T[])a.Clone()};public T[] ToArray()=>(T[])Data.Clone();}
}
namespace Unity.Netcode
{
    public enum NetworkDelivery{ReliableSequenced}
    public sealed class NetworkManager {public static NetworkManager Singleton;public const ulong ServerClientId=0;public bool IsListening=true,IsServer;public ulong LocalClientId;public IntPtr Pointer;public CustomMessagingManager CustomMessagingManager;}
    public sealed class CustomMessagingManager
    {
        public sealed class HandleNamedMessageDelegate
        {
            private Action<ulong,FastBufferReader> _action;
            public static implicit operator HandleNamedMessageDelegate(Action<ulong,FastBufferReader> action)=>new(){_action=action};
            public void Invoke(ulong sender,FastBufferReader reader)=>_action(sender,reader);
        }
        public HandleNamedMessageDelegate Handler;public ulong Owner;
        public void RegisterNamedMessageHandler(string channel,HandleNamedMessageDelegate handler){Handler=handler;}
        public void UnregisterNamedMessageHandler(string channel){Handler=null;}
        public void SendNamedMessage(string channel,ulong target,FastBufferWriter writer,NetworkDelivery delivery){Bus.Pending.Enqueue((Owner,target,(byte[])writer.Data.Clone()));}
    }
    public struct FastBufferReader
    {
        private byte[] _data;public int Position;public int Length=>_data.Length;public FastBufferReader(byte[] data){_data=data;Position=0;}
        public bool TryBeginRead(int count)=>count<=Length-Position;
        public void ReadBytesSafe(ref Il2CppStructArray<byte> bytes,int count){Array.Copy(_data,Position,bytes.Data,0,count);Position+=count;}
    }
    public struct FastBufferWriter:IDisposable{public byte[] Data;public FastBufferWriter(int n,Unity.Collections.Allocator a,int max){Data=new byte[n];}public void WriteBytesSafe(Il2CppStructArray<byte> a){Data=a.ToArray();}public void Dispose(){}}
}
namespace Steamworks
{
    public struct CSteamID{public ulong m_SteamID;public CSteamID(ulong id){m_SteamID=id;}public bool IsValid()=>m_SteamID!=0;public bool IsLobby()=>m_SteamID==77;}
    public static class SteamUser{public static CSteamID GetSteamID()=>new(100+Bus.Active.Id);}
    public static class SteamMatchmaking
    {
        public static CSteamID GetLobbyOwner(CSteamID id)=>new(100);
        public static bool SetLobbyData(CSteamID id,string key,string value){Bus.Tags[key]=value;return true;}
        public static string GetLobbyData(CSteamID id,string key)=>Bus.Tags.GetValueOrDefault(key,"");
        public static void DeleteLobbyData(CSteamID id,string key){Bus.Tags.Remove(key);}
    }
}
namespace BepInEx {public static class Paths{public static string ConfigPath=>Path.Combine(Bus.Root,Bus.Active.Id.ToString());}}
namespace ChainedBeasts.Rerolls
{
    public sealed class InertLog{public List<string> Messages=new();public void LogInfo(string text){Messages.Add(text);}public void LogWarning(string text){Messages.Add(text);}public void LogError(string text){Messages.Add(text);}}
    internal static class Plugin{internal const string Version="0.1.1";internal static string Stamp="checked-build";internal static InertLog Logger=new();}
    internal sealed class NativeOffers
    {
        internal Room Room;internal IntPtr Pointer=new(500);internal Node Node;
        internal static NativeOffers Read(){var n=Bus.Active;if(!n.RoomAvailable)return null;return new(){Node=n,Room=new(Wire.ItemHash(n.Items),n.Kind,100,0,n.Items.ToArray())};}
        internal Item[] Generate(int revision)=>Room.Items.Select(i=>new Item(i.Id+10)).ToArray();
        internal NativeStage Stage(Item[] items){if(Node.ThrowPrepare)throw new IOException("Fixture preparation failure");Node.Prepared++;return new(Node,items);}
    }
    internal sealed class NativeStage:IDisposable
    {
        private readonly Node _node;private readonly Item[] _before,_after;private bool _applied,_finished;
        internal NativeStage(Node n,Item[] items){_node=n;_before=n.Items;_after=items;}
        internal void Apply(){if(_applied)return;_applied=true;_node.Items=_after;_node.Applied++;if(_node.ThrowApply)throw new IOException("Fixture application failure");}
        internal void Finish(){_finished=true;_node.Finished++;if(_node.ThrowFinish)throw new IOException("Fixture cleanup failure");}
        public void Dispose(){if(_finished)return;if(_applied){_node.Items=_before;_node.RolledBack++;}_finished=true;}
    }
}

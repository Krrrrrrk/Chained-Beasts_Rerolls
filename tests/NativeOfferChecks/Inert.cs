// Native card/generator stand-ins. Production adapter runs unchanged; no game code is loaded.
using ChainedBeasts.Rerolls.Core;
using UnityEngine;
namespace Il2CppSystem.Collections.Generic
{
    public class List<T>:System.Collections.Generic.List<T>{private static int _next;public IntPtr Pointer=new(++_next);}
}
namespace Il2CppInterop.Runtime.InteropTypes.Arrays
{
    public sealed class Il2CppStructArray<T>{private T[] _v;public Il2CppStructArray(params T[] v){_v=v;}public T[] ToArray()=>(T[])_v.Clone();}
}
namespace UnityEngine
{
    public class Transform{public Transform parent;public int Index;public int GetSiblingIndex()=>Index;public void SetSiblingIndex(int value){Index=value;}}
    public sealed class GameObject{public bool activeInHierarchy=true;public bool Destroyed;public void SetActive(bool value){activeInHierarchy=value;}}
    public struct Vector3{public float x,y,z;}
    public static class Object{public static T Instantiate<T>(T original,Transform parent) where T:Card,new()=>new(){transform=new(){parent=parent}};public static void Destroy(GameObject o){o.Destroyed=true;o.activeInHierarchy=false;}}
    public static class Resources{public static Dictionary<Type,object> Objects=new();public static T[] FindObjectsOfTypeAll<T>()=>Objects.TryGetValue(typeof(T),out var list)?(T[])list:Array.Empty<T>();}
}
public enum SkillType{None=0,A=1,B=2,C=3,D=4,E=5,F=6}
public enum InjuryType{None=0,A=1,B=2}
public enum PurchaseType{None=0,Weapon=1,Passive=2,Equipment=3,Instant=4,ReplacementWeapon=5,ReplacementPassive=6,ReplacementEquipment=7,ReplacementInstant=8}
public enum PurchaseCategory{Instant,Passive,Equipment,Spawn}
public sealed class PurchaseDef{public PurchaseCategory Category;}
public sealed class GameLibrary
{
    public static GameLibrary Instance=new();public Dictionary<SkillType,object> SkillDict=new();public Dictionary<InjuryType,object> InjuryDict=new();public Dictionary<PurchaseType,PurchaseDef> PurchaseDict=new();
}
public sealed class Potion{public InjuryType injury;public int _healAmount,potionSize,modelIdx;public bool shared;public string nameTerm;}
public sealed class PlayerData{}
public sealed class GameLobby{}
public sealed class Scene{public ulong NetworkObjectId=100;}
public sealed class NetworkSession{public static NetworkSession Instance;public static bool InstanceExists=>Instance!=null;public bool TransitionPending,isRestoringFromCheckpoint,IsPaused;public Scene sceneRoot=new();public GameLobby Lobby=new();public RunDataManager runDataManager=new();}
public sealed class RunDataManager
{
    public int RunSeed=7,RunStartTime=123,CompletedLevels;
    public Il2CppSystem.Collections.Generic.List<PlayerData> playerDatas=new();
    public Il2CppSystem.Collections.Generic.List<Il2CppSystem.Collections.Generic.List<SkillType>> trainingSkills=new();
    public Il2CppSystem.Collections.Generic.List<Potion> potions=new();
    public Il2CppSystem.Collections.Generic.List<PurchaseType> currentShopOptions=new();
}
public class View{public bool InFocus=true;public GameObject gameObject=new();public bool ThrowRefresh;public int RefreshCount;protected void Refreshed(){RefreshCount++;if(ThrowRefresh)throw new IOException("Fixture refresh failed");}}
public sealed class UIGameLobbyTrainerRpc
{
    public enum eState{LEVEL_UP,DONE}
    public IntPtr Pointer=new(1);public UILobbyTrainer uiLobbyTrainer=new();public bool tutorialDialogueActive;
    public UIState uiState=new();public int Changed;
    public sealed class UIState
    {
        public eState State=eState.LEVEL_UP;public int trainingLayer;public int RefreshCounter=4;
        public Il2CppInterop.Runtime.InteropTypes.Arrays.Il2CppStructArray<bool> done=new(false),waitingForCoroutine=new(false),upgradeAutoSelect=new(false);
        public void ResetUpgradeSelections(){throw new Exception("Must not reset native refresh bookkeeping");}
    }
    public void OnTrainingSkillsChanged(int layer){Changed++;}
}
public sealed class UILobbyTrainer:View{public void Refresh(UIGameLobbyTrainerRpc.UIState state){Refreshed();}}
public sealed class UIGameLobbyDoctorRpc
{
    public IntPtr Pointer=new(2);public UILobbyDoctor uiLobbyDoctor=new();public UIState uiState=new();
    public bool tutorialDialogueActive,potionsConsumed,executeSelectionsScheduled,executed;
    public sealed class UIState{public Il2CppInterop.Runtime.InteropTypes.Arrays.Il2CppStructArray<bool> selected=new(false),autoSelect=new(false),waitingForAutoSelect=new(false),twitchIsSelected=new(false);}
}
public sealed class UIGameLobbyShopRpc
{
    public IntPtr Pointer=new(3);public UILobbyShop ui=new();public UIState uiState=new();public bool tutorialDialogueActive,executed,waitingForTransitionOut;
    public sealed class UIState{public Il2CppInterop.Runtime.InteropTypes.Arrays.Il2CppStructArray<bool> ready=new(false),purchased=new(false),locked=new(false);}
}
public class Card{public GameObject gameObject=new();public Transform transform=new(){parent=new()};public static int FailAt=-1;}
public sealed class UILobbyDoctorSelectionPage:Card{public Vector3 potionPosition,cameraPosition;public Potion Potion;public void Initialise(UILobbyDoctor ui,int index,Potion potion,Vector3 p,Vector3 c){if(index==FailAt)throw new IOException("Fixture card initialization failed");Potion=potion;}}
public sealed class UILobbyShopSelectionPage:Card{public Vector3 cameraPosition;public Transform worldAnchor=new();public PurchaseType Purchase;public void Initialise(UILobbyShop ui,int index,Vector3 p,Transform anchor){if(index==FailAt)throw new IOException("Fixture card initialization failed");Purchase=NetworkSession.Instance.runDataManager.currentShopOptions[index];}}
public sealed class UILobbyDoctor:View{public Il2CppSystem.Collections.Generic.List<UILobbyDoctorSelectionPage> selectionPages=new();public UILobbyDoctorSelectionPage selectionPagePrefab=new();public void Refresh(UIGameLobbyDoctorRpc.UIState state){Refreshed();}}
public sealed class UILobbyShop:View{public Il2CppSystem.Collections.Generic.List<UILobbyShopSelectionPage> selectionPages=new();public UILobbyShopSelectionPage selectionPagePrefab=new();public int previousCost;public bool previousCostAboveZero;public void Refresh(UIGameLobbyShopRpc.UIState state){Refreshed();}}
public static class RandomUtils{public static object rand=new();}
public static class SkillGenerator{public static Il2CppSystem.Collections.Generic.List<Il2CppSystem.Collections.Generic.List<SkillType>> GenerateTrainingSkills(int seed,object reserved){RandomUtils.rand=new();return new(){new(){SkillType.D,SkillType.E,SkillType.F}};}}
public static class InjuryGenerator{public static Il2CppSystem.Collections.Generic.List<Potion> GeneratePotions(RunDataManager data,int count,int seed,out bool extra){RandomUtils.rand=new();extra=false;return new(){new(){injury=InjuryType.B,_healAmount=30,nameTerm="new",potionSize=2,modelIdx=1},new(){injury=InjuryType.None,nameTerm="water"}};}}
public static class PurchaseGenerator{public static Il2CppSystem.Collections.Generic.List<PurchaseType> GenerateShopOptions(int seed,int shop){RandomUtils.rand=new();return new(){PurchaseType.ReplacementWeapon,PurchaseType.ReplacementPassive,PurchaseType.ReplacementEquipment,PurchaseType.ReplacementInstant};}}
public static class GladiatorConsts{public static int GetAct(int completed)=>0;}

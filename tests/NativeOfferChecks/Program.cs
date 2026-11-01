using ChainedBeasts.Rerolls;
using ChainedBeasts.Rerolls.Core;
using UnityEngine;
var checks=0;
void Check(bool ok,string message){if(!ok)throw new Exception(message);checks++;}
void Reset(){NetworkSession.Instance=new();NetworkSession.Instance.runDataManager.playerDatas.Add(new());Resources.Objects.Clear();Card.FailAt=-1;GameLibrary.Instance=new();foreach(var id in Enum.GetValues<SkillType>())GameLibrary.Instance.SkillDict[id]=new();foreach(var id in Enum.GetValues<InjuryType>())GameLibrary.Instance.InjuryDict[id]=new();foreach(var id in Enum.GetValues<PurchaseType>())GameLibrary.Instance.PurchaseDict[id]=new();}
Reset();var data=NetworkSession.Instance.runDataManager;data.trainingSkills.Add(new(){SkillType.A,SkillType.B,SkillType.C});
var trainer=new UIGameLobbyTrainerRpc();Resources.Objects[typeof(UIGameLobbyTrainerRpc)]=new[]{trainer};
var room=NativeOffers.Read();Check(room!=null&&room.Room.Kind==OfferKind.Skills,"Trainer exposes only actual level-up offers.");
var rng=RandomUtils.rand;var generated=room.Generate(0);Check(ReferenceEquals(RandomUtils.rand,rng),"Native RNG object restored after skill generation.");
var before=data.trainingSkills[0];using(var stage=room.Stage(generated)){Check(ReferenceEquals(data.trainingSkills[0],before),"Preparation does not replace live skills.");stage.Apply();Check(data.trainingSkills[0][0]==SkillType.D&&trainer.uiState.RefreshCounter==4,"Skill cards update without resetting native refresh counters.");}
Check(ReferenceEquals(data.trainingSkills[0],before),"Uncommitted skills roll back.");
using(var stage=room.Stage(generated)){stage.Apply();stage.Finish();}Check(data.trainingSkills[0][0]==SkillType.D,"Committed skill replacement survives stage disposal.");
trainer.uiState.State=UIGameLobbyTrainerRpc.eState.DONE;Check(NativeOffers.Read()==null,"Completed trainer does not expose reroll.");

foreach(var kind in new[]{OfferKind.Potions,OfferKind.Equipment}) {
    Reset();data=NetworkSession.Instance.runDataManager;
    var doctor=new UIGameLobbyDoctorRpc();var shop=new UIGameLobbyShopRpc();
    if(kind==OfferKind.Potions){data.potions.Add(new(){injury=InjuryType.A,_healAmount=20,nameTerm="old"});data.potions.Add(new(){injury=InjuryType.None,nameTerm="water"});doctor.uiLobbyDoctor.selectionPages.Add(new());doctor.uiLobbyDoctor.selectionPages.Add(new());Resources.Objects[typeof(UIGameLobbyDoctorRpc)]=new[]{doctor};}
    else{data.currentShopOptions.AddRange(new[]{PurchaseType.Weapon,PurchaseType.Passive,PurchaseType.Equipment,PurchaseType.Instant});for(var i=0;i<4;i++)shop.ui.selectionPages.Add(new());Resources.Objects[typeof(UIGameLobbyShopRpc)]=new[]{shop};}
    room=NativeOffers.Read();Check(room!=null&&room.Room.Kind==kind,"Only the current eligible "+kind+" screen is exposed.");rng=RandomUtils.rand;generated=room.Generate(0);
    Check(ReferenceEquals(RandomUtils.rand,rng),"RNG restored after "+kind+" generation.");
    var oldPotions=data.potions;var oldShop=data.currentShopOptions;
    using(var stage=room.Stage(generated)) {
        Check(ReferenceEquals(data.potions,oldPotions)&&ReferenceEquals(data.currentShopOptions,oldShop),"Preparation leaves live offer lists untouched.");
        stage.Apply();
        Check(kind==OfferKind.Potions?data.potions[0]._healAmount==30:data.currentShopOptions.SequenceEqual(new[]{PurchaseType.ReplacementWeapon,PurchaseType.ReplacementPassive,PurchaseType.ReplacementEquipment,PurchaseType.ReplacementInstant}),"Every "+kind+" offer is replaced, including all merchant categories.");
    }
    Check(ReferenceEquals(data.potions,oldPotions)&&ReferenceEquals(data.currentShopOptions,oldShop),"Abort restores original "+kind+" lists.");
    Check(kind==OfferKind.Potions?doctor.uiLobbyDoctor.selectionPages.All(p=>p.gameObject.activeInHierarchy):shop.ui.selectionPages.All(p=>p.gameObject.activeInHierarchy),"Abort restores the original visible cards.");
    Card.FailAt=1;var threw=false;try{using var stage=room.Stage(generated);}catch(IOException){threw=true;}
    Check(threw&&ReferenceEquals(data.potions,oldPotions)&&ReferenceEquals(data.currentShopOptions,oldShop),"Partial card construction preserves native offers.");Card.FailAt=-1;
    using(var stage=room.Stage(generated)){stage.Apply();NetworkSession.Instance.sceneRoot.NetworkObjectId=999;data.potions=new();data.currentShopOptions=new();}
    Check(data.potions.Count==0&&data.currentShopOptions.Count==0,"A stale stage cannot overwrite a new scene's lists.");
}
foreach(var kind in new[]{OfferKind.Skills,OfferKind.Potions,OfferKind.Equipment}) {
    Reset();data=NetworkSession.Instance.runDataManager;
    View view;
    if(kind==OfferKind.Skills) {
        data.trainingSkills.Add(new(){SkillType.A,SkillType.B,SkillType.C});
        trainer=new();Resources.Objects[typeof(UIGameLobbyTrainerRpc)]=new[]{trainer};view=trainer.uiLobbyTrainer;
    } else if(kind==OfferKind.Potions) {
        data.potions.Add(new(){injury=InjuryType.A,_healAmount=20});
        var doctor=new UIGameLobbyDoctorRpc();doctor.uiLobbyDoctor.selectionPages.Add(new());
        Resources.Objects[typeof(UIGameLobbyDoctorRpc)]=new[]{doctor};view=doctor.uiLobbyDoctor;
    } else {
        data.currentShopOptions.Add(PurchaseType.Weapon);
        var shop=new UIGameLobbyShopRpc();shop.ui.selectionPages.Add(new());
        Resources.Objects[typeof(UIGameLobbyShopRpc)]=new[]{shop};view=shop.ui;
    }
    var session=NetworkSession.Instance;session.TransitionPending=true;
    Check(NativeOffers.Read()==null,"A host still blocks "+kind+" rerolls while its transition is pending.");
    session.IsServer=false;room=NativeOffers.Read();
    Check(room?.Room.Kind==kind,"A client with native lingering transition bookkeeping exposes its actual "+kind+" cards.");
    session.TransitionPending=false;
    Check(NativeOffers.Read()?.Room.Key==room.Room.Key,"Lingering host-only bookkeeping cannot change the client's "+kind+" offer identity.");
    view.gameObject.SetActive(false);Check(NativeOffers.Read()==null,"Inactive "+kind+" cards remain excluded on clients.");view.gameObject.SetActive(true);
    session.IsPaused=true;Check(NativeOffers.Read()==null,"Paused "+kind+" cards remain excluded on clients.");session.IsPaused=false;
    session.isRestoringFromCheckpoint=true;Check(NativeOffers.Read()==null,"Restoring "+kind+" cards remain excluded on clients.");session.isRestoringFromCheckpoint=false;
    session.sceneRoot=null;Check(NativeOffers.Read()==null,"A missing client scene cannot expose "+kind+" rerolls.");session.sceneRoot=new();
    if(kind==OfferKind.Skills) trainer.uiState.done=new(true);
    else if(kind==OfferKind.Potions) ((UIGameLobbyDoctorRpc[])Resources.Objects[typeof(UIGameLobbyDoctorRpc)])[0].uiState.selected=new(true);
    else ((UIGameLobbyShopRpc[])Resources.Objects[typeof(UIGameLobbyShopRpc)])[0].uiState.purchased=new(true);
    Check(NativeOffers.Read()==null,"Resolved "+kind+" selections stay blocked on clients.");
}
Check(!AppDomain.CurrentDomain.GetAssemblies().Any(a=>a.GetName().Name=="Assembly-CSharp"),"Native adapter checks did not load the game.");
Console.WriteLine($"PASS: {checks} production native-offer adapter checks with inert cards/generators.");

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
Check(!AppDomain.CurrentDomain.GetAssemblies().Any(a=>a.GetName().Name=="Assembly-CSharp"),"Native adapter checks did not load the game.");
Console.WriteLine($"PASS: {checks} production native-offer adapter checks with inert cards/generators.");

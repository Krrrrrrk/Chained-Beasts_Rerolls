using ChainedBeasts.Rerolls.Core;
using System.Text;
var checks=0;
void Check(bool ok,string text){if(!ok)throw new Exception(text);checks++;}
var ledger=new PartyLedger();ledger.Start("first");
Check(ledger.Charges==1,"Every fresh run begins with one party charge.");
ledger.BeginBattle("a",new[]{0,1});ledger.Damage(0,1);
Check(ledger.CompleteBattle("a",true,new[]{0,1})&&ledger.Charges==2,"One flawless survivor adds exactly one charge.");
Check(!ledger.CompleteBattle("a",true,new[]{0,1})&&ledger.Charges==2,"End callback replay is idempotent.");
foreach(var kind in new[]{OfferKind.Potions,OfferKind.Skills})Check(ledger.Commit(ledger.Revision+1),"Shared balance can be spent on "+kind);
Check(ledger.Charges==0&&!ledger.Commit(10),"One category cannot reuse a charge already spent on another.");
ledger.BeginBattle("b",new[]{0,1});Check(ledger.CompleteBattle("b",true,new[]{0,1})&&ledger.Charges==1,"Multiple flawless survivors still award one.");
Check(ledger.Commit(10)&&!ledger.Commit(10),"Duplicate transaction cannot spend twice.");
ledger.BeginBattle("c",new[]{0,1});ledger.Damage(0,2);ledger.Damage(1,2);Check(!ledger.CompleteBattle("c",true,new[]{0,1}),"All damaged means no reward, irrespective of healing.");
ledger.BeginBattle("d",new[]{0});Check(!ledger.CompleteBattle("d",true,Array.Empty<int>()),"An undamaged dead player is not a survivor.");
ledger.BeginBattle("e",new[]{0});Check(!ledger.CompleteBattle("e",false,new[]{0}),"Losing does not reward.");
ledger.BeginBattle("f",new[]{0});Check(!ledger.CompleteBattle("f",true,new[]{1}),"A mid-battle newcomer is not credited for surviving the whole battle.");
ledger.BeginBattle("g",new[]{0});ledger.Damage(0,0);Check(ledger.CompleteBattle("g",true,new[]{0}),"Zero damage such as a successful block does not break flawless status.");
ledger.Start("second");Check(ledger.Charges==1&&ledger.Revision==0,"A new run does not inherit charges, hurts, or transactions.");
ledger.Start("resume",7,20);Check(ledger.Charges==7&&ledger.Revision==20,"Resume accepts the saved shared balance without a starting grant.");

var seats=new[]{new Seat(0,0),new Seat(1,1),new Seat(1,2)};
var c=new Consensus();Check(c.Begin("room",seats,10,1),"Proposal begins on a frozen roster.");
Check(!c.Vote(1,0,"room")&&!c.Vote(1,1,"stale"),"Spoofed player and stale screen votes are rejected.");
Check(c.Vote(0,0,"room")&&c.Vote(0,0,"room")&&!c.AllVoted,"Duplicate vote cannot stand in for another player.");
Check(c.Vote(1,1,"room")&&!c.AllVoted,"Couch characters each need consent.");
Check(c.Vote(1,2,"room")&&c.AllVoted&&c.Prepare(11),"Unanimous votes begin preparation.");
Check(!c.Ack(3,1,Step.Preparing)&&!c.Ack(1,99,Step.Preparing),"Outsider and stale transaction acknowledgements are rejected.");
Check(c.Ack(0,1,Step.Preparing)&&!c.AllAcknowledged,"Host alone cannot commit a multiplayer reroll.");
Check(c.Ack(1,1,Step.Preparing)&&c.AllAcknowledged&&c.Apply(12),"One prepare acknowledgement per machine covers its couch characters.");
Check(!c.Ack(0,1,Step.Preparing)&&!c.AllAcknowledged,"Preparation acknowledgements cannot count as application receipts.");
Check(c.Ack(1,1,Step.Applying)&&!c.AllAcknowledged&&c.Ack(0,1,Step.Applying)&&c.AllAcknowledged,"Every machine must apply before spending.");
Check(c.SameParty(seats)&&!c.SameParty(seats[..^1]),"Roster changes invalidate the proposal.");
Check(c.Expired(25)&&c.Expired(double.NaN),"Timeouts and invalid clocks cannot leave the transaction open.");
c.Clear();Check(!c.AllVoted&&!c.AllAcknowledged&&!c.Begin("room",seats,26,1),"Clearing retains replay protection and clears acknowledgements.");
Check(c.Begin("new",new[]{new Seat(0,0)},26,2)&&c.Vote(0,0,"new")&&c.Prepare(26),"Solo needs exactly one consent.");
Check(c.Ack(0,2,Step.Preparing)&&c.Apply(26)&&c.Ack(0,2,Step.Applying)&&c.AllAcknowledged,"Solo follows the same atomic charge gate.");

var packet=new Packet{Op=Op.Prepare,Epoch=7,Room="room",Transaction=3,Kind=OfferKind.Potions,Items=new[]{new Item(2,25,1,false,0,"Potion/Name")}};
Check(Wire.TryDecode(Wire.Encode(packet),out var decoded)&&decoded!.Items.SequenceEqual(packet.Items),"Concrete potion values survive serialization.");
foreach(var invalid in new[]{packet with{Version=2},packet with{Op=(Op)999},packet with{Charges=-1},packet with{Player=8},
 packet with{Items=new[]{new Item(1,Name:new string('a',161))}},packet with{Items=new[]{new Item(1,Heal:-1)}},packet with{Items=new Item[33]},packet with{Votes=new[]{1,1}}})
    Check(!Wire.Valid(invalid),"Malformed packet rejected before native access.");
foreach(var text in new[]{"null","{} garbage","[","{\"Items\":null}","{\"Room\":null}","{\"Items\":[null]}"})Check(!Wire.TryDecode(Encoding.UTF8.GetBytes(text),out _),"Invalid JSON/null collections rejected.");
var random=new Random(17);for(var i=0;i<100;i++){var bytes=new byte[random.Next(2,200)];random.NextBytes(bytes);Check(!Wire.TryDecode(bytes,out _),"Random malformed input contained.");}
if(args.Length==2)ArtifactChecks.Run(args[0],args[1],Check);
Console.WriteLine($"PASS: {checks} production-core / native-binding / artifact checks.");

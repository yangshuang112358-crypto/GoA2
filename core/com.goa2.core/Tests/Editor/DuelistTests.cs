using System.IO;
using System.Linq;
using Goa2.Application;
using Goa2.Domain;
using Goa2.Infrastructure;
using Goa2.Rules;
using NUnit.Framework;
using static Goa2.Tests.SessionTests;
using static Goa2.Tests.TurnFlowTests;
namespace Goa2.Tests
{
    public sealed class DuelistTests
    {
        internal const string Card="arien-15-决斗家";
        internal static GameSession Ready(ContentCatalog cat,string card=Card,bool bard=false)
        {
            var game=LocalGameFactory.Create(cat,"duelist",new[]{"A","B","C","D"},42,true);
            Apply(game,0,CommandKind.DebugPrepare,bard?"sabina,arien,wasp,brogan":"brogan,arien,wasp,sabina");
            Apply(game,0,CommandKind.DebugEquipCard,card,target:1);
            if(bard)Apply(game,0,CommandKind.DebugEquipCard,"brogan-10-吟游诗人",target:3);
            var positions=new[]{new Hex(7,-10),new Hex(8,-10),new Hex(7,-9),new Hex(6,-9)};
            for(int s=0;s<4;s++)Apply(game,0,CommandKind.DebugTeleport,"hero:"+s,cell:positions[s]);return game;
        }
        internal static void Defend(GameSession game,string card=Card,int power=5)
        {Apply(game,0,CommandKind.DebugAttack,"hero:1|"+power);Apply(game,1,CommandKind.Defend,card);}
        [Test] public void ExactDefenseTextAndVersionGate()
        {
            var card=BattlefieldTests.Catalog().Card(Card);Assert.That(CombatRules.HasDefenseProgram(card),Is.True);
            Assert.That(CombatRules.HasDefenseProgram(card,64),Is.False);Assert.That(CombatRules.HasPrimaryProgram(card),Is.False);
            card.Text+="成功后";Assert.That(CombatRules.HasDefenseProgram(card),Is.False);
        }
        [TestCase(5,false)] [TestCase(8,true)] public void BothSuccessfulAndFailedDefenseCreateSameTurnImmunity(int power,bool defeated)
        {
            var cat=BattlefieldTests.Catalog();var game=Ready(cat);Defend(game,power:power);game=ChargeTests.Restore(cat,game);
            Assert.That(game.View(1).Effects.Single(e=>e.SourceCardId==Card).Kind.ToString(),Is.EqualTo("OtherEnemyAttackImmunity"));
            Assert.That(game.View(1).Players[1].AwaitingRespawn,Is.EqualTo(defeated));
            Assert.That(game.View(null).Effects.Any(e=>e.SourceCardId==Card),Is.False);Assert.That(game.View(null).Events.Any(e=>e.CardId==Card),Is.False);
        }
        [Test] public void OnlyOtherEnemyAttacksAreForbidden()
        {
            var cat=BattlefieldTests.Catalog();var game=Ready(cat);Defend(game);var state=new JsonStateCodec().Read(game.ExportSave());var target=state.Units.Single(u=>u.Seat==1);
            foreach(int seat in new[]{0,1,3})Assert.That(EffectRules.CanBeAttacked(state,state.Units.Single(u=>u.Seat==seat),target,false),Is.True);
            Assert.That(EffectRules.CanBeAttacked(state,state.Units.Single(u=>u.Seat==2),target,false),Is.False);
            Assert.That(EffectRules.CanAffect(state,2,target),Is.True);Assert.That(EffectRules.CanDisplace(cat,state,2,target),Is.True);
            Assert.That(EffectRules.CanDisplace(cat,state,2,target,attackAction:true),Is.False);
        }
        [Test] public void FailedDefenseSurvivesActualRespawnAndThenExpires()
        {
            var cat=BattlefieldTests.Catalog();var game=Ready(cat);Defend(game,power:8);
            string[] cards={"brogan-00-猛攻","arien-00-华丽刀锋","wasp-13-控物","sabina-07-指挥"};
            for(int s=0;s<4;s++)Apply(game,s,CommandKind.SelectCard,cards[s]);Apply(game,0,CommandKind.Pass);
            Assert.That(game.View(1).Pending?.Kind,Is.EqualTo("hero_respawn"));game=ChargeTests.Restore(cat,game);
            Apply(game,1,CommandKind.RespawnHero,cell:game.View(1).RespawnCells.First());game=ChargeTests.Restore(cat,game);
            Assert.That(game.View(1).Turn,Is.EqualTo(1));var state=new JsonStateCodec().Read(game.ExportSave());var target=state.Units.Single(u=>u.Seat==1);
            Assert.That(EffectRules.CanBeAttacked(state,state.Units.Single(u=>u.Seat==2),target,false),Is.False);
            Apply(game,0,CommandKind.DebugAdvance,"turn");Assert.That(game.View(1).Effects.Any(e=>e.SourceCardId==Card),Is.False);ChargeTests.Restore(cat,game);
        }
        [Test] public void BardRetrievalCancelsEvenDeathPersistentImmunity()
        {
            var cat=BattlefieldTests.Catalog();var game=Ready(cat,bard:true);Defend(game);
            string[] cards={"sabina-00-近身射击","arien-07-潮水","wasp-13-控物","brogan-10-吟游诗人"};
            for(int s=0;s<4;s++)Apply(game,s,CommandKind.SelectCard,cards[s]);OpportuneMomentTests.AdvanceTo(game,3);
            Apply(game,3,CommandKind.BeginPrimary);Apply(game,3,CommandKind.ChooseEffectTarget,"hero:1");Apply(game,1,CommandKind.ChooseRecoveredCard,Card);
            Assert.That(game.View(1).Effects.Any(e=>e.SourceCardId==Card),Is.False);ChargeTests.Restore(cat,game);
        }
        [Test] public void NumericDefenseIgnoresEveryMinionModifier()
        {
            var cat=BattlefieldTests.Catalog();var game=Ready(cat);Apply(game,0,CommandKind.DebugTeleport,"minion:-1,-3",cell:new Hex(8,-9));
            Apply(game,0,CommandKind.DebugAttack,"hero:1|6");var option=game.View(1).DefenseOptions.Single(o=>o.CardId==Card);
            Assert.That(game.View(1).Attack.FriendlyGuard,Is.GreaterThan(0));Assert.That(option.Block,Is.False);Assert.That(option.Assessment.IgnoresMinions,Is.True);
            Assert.That(option.Assessment.AttackCompared,Is.EqualTo(6));Assert.That(option.Assessment.Successful,Is.True);Apply(game,1,CommandKind.Defend,Card);ChargeTests.Restore(cat,game);
        }
        [Test] public void WrongActorAndDuplicateDefenseAreAtomic()
        {
            var cat=BattlefieldTests.Catalog();var game=Ready(cat);Apply(game,0,CommandKind.DebugAttack,"hero:1|5");string before=game.ExportSave();
            Assert.That(game.Execute(2,Cmd(game,2,CommandKind.Defend,Card)).Accepted,Is.False);Assert.That(game.ExportSave(),Is.EqualTo(before));
            var cmd=Cmd(game,1,CommandKind.Defend,Card);Assert.That(game.Execute(1,cmd).Accepted,Is.True);before=game.ExportSave();
            Assert.That(game.Execute(1,cmd).Duplicate,Is.True);Assert.That(game.ExportSave(),Is.EqualTo(before));ChargeTests.Restore(cat,game);
        }
        [Test] public void DecliningDefenseDoesNotCreateImmunity()
        {
            var cat=BattlefieldTests.Catalog();var game=Ready(cat);Apply(game,0,CommandKind.DebugAttack,"hero:1|8");Apply(game,1,CommandKind.DeclineDefense);
            Assert.That(game.View(1).Effects.Any(e=>e.SourceCardId==Card),Is.False);ChargeTests.Restore(cat,game);
        }
        [Test] public void Old64RecoveryWindowIsByteStable()
        {
            var cat=BattlefieldTests.Catalog();string save=File.ReadAllText(Path.Combine(ContentTests.Root(),"tests/fixtures/engine64-undying-recovery.json"));
            var game=LocalGameFactory.Restore(cat,save);Assert.That(game.ExportSave(),Is.EqualTo(save));Assert.That(CombatRules.HasDefenseProgram(cat.Card(Card),64),Is.False);
        }
        [Test] public void DefeatStillCancelsOrdinaryEffectsWhilePreservingThisExplicitException()
        {
            var cat=BattlefieldTests.Catalog();var game=Ready(cat);Defend(game);var state=new JsonStateCodec().Read(game.ExportSave());
            // Isolated lifecycle fixture, separate from the real respawn/replay test above.
            state.Effects.Add(new ActiveEffect{Id="ordinary-silence",SourceCardId="arien-06-打断施法",SourceUnitId="hero:1",ControllerSeat=1,Kind=EffectKind.SkillSuppression,Window=EffectTimeline.Create(state.Round,state.Turn,4,EffectDuration.ThisTurn)});
            new GameRules().Apply(cat,state,new Command{ActorSeat=0,Kind=CommandKind.DebugDefeatHero,Value="hero:1",TargetSeat=0});
            Assert.That(state.Effects.Any(e=>e.Id=="ordinary-silence"),Is.False);Assert.That(state.Effects.Any(e=>e.SourceCardId==Card),Is.True);
        }
    }
}

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
    public sealed class PincerAttackTests
    {
        internal const string Pincer="tigerclaw-05-两面夹攻",Lead="sabina-08-带头冲锋",Numeric="sabina-05-一枪爆头";
        internal static GameSession Setup(ContentCatalog catalog,bool support=true,string card=Pincer)
        {
            var game=LocalGameFactory.Create(catalog,"pincer",new[]{"A","B","C","D"},42,true);
            Apply(game,0,CommandKind.DebugPrepare,"tigerclaw,sabina,brogan,arien");Apply(game,0,CommandKind.DebugEquipCard,card,target:0);
            Apply(game,0,CommandKind.DebugEquipCard,Lead,target:1);Apply(game,0,CommandKind.DebugEquipCard,Numeric,target:1);
            Apply(game,0,CommandKind.DebugEquipCard,"sabina-13-近身支援",target:1);
            Apply(game,0,CommandKind.DebugSetCoin,"blue");
            Apply(game,0,CommandKind.DebugTeleport,"hero:0",cell:new Hex(6,-8));Apply(game,0,CommandKind.DebugTeleport,"hero:1",cell:new Hex(7,-8));
            if(support)Apply(game,0,CommandKind.DebugTeleport,"hero:2",cell:new Hex(8,-8));
            string minion=game.View(0).Units.First(u=>u.Kind=="melee" && u.Team==Team.Red).Id;
            Apply(game,0,CommandKind.DebugTeleport,minion,cell:new Hex(7,-9));
            string[] cards={card,"sabina-13-近身支援","brogan-06-铜墙铁壁","arien-07-潮水"};
            for(int seat=0;seat<4;seat++)Apply(game,seat,CommandKind.SelectCard,cards[seat]);
            while(game.View(0).ActiveSeat!=0)Apply(game,game.View(0).ActiveSeat!.Value,CommandKind.Pass);
            Assert.That(game.View(0).ActiveSeat,Is.EqualTo(0));return game;
        }
        private static GameSession Restore(ContentCatalog catalog,GameSession game)
        {string saved=game.ExportSave();var next=LocalGameFactory.Restore(catalog,saved);Assert.That(next.ExportSave(),Is.EqualTo(saved));return next;}
        private static void Attack(GameSession game){Apply(game,0,CommandKind.BeginPrimary);Apply(game,0,CommandKind.ChooseAttackTarget,"hero:1");}
        [Test]
        public void ExactCardAndVersionGate()
        {
            var card=BattlefieldTests.Catalog().Card(Pincer);
            Assert.That(card.PrimaryFamily,Is.EqualTo("attack"));Assert.That(card.PrimaryValue,Is.EqualTo(4));Assert.That(card.Initiative,Is.EqualTo(10));
            Assert.That(card.Subtype,Is.EqualTo("远程"));Assert.That(card.SubtypeValue,Is.EqualTo(1));Assert.That(card.SecondaryDefense,Is.EqualTo(6));Assert.That(card.SecondaryMovement,Is.EqualTo(5));
            Assert.That(CombatRules.HasPrimaryProgram(card),Is.True);Assert.That(CombatRules.HasPrimaryProgram(card,16),Is.False);
        }
        [TestCase(false)] [TestCase(true)]
        public void SupportControlsBlockLegalityButNeverForbidsSufficientNumericDefense(bool support)
        {
            var catalog=BattlefieldTests.Catalog();var game=Setup(catalog,support);Apply(game,0,CommandKind.BeginPrimary);game=Restore(catalog,game);
            Apply(game,0,CommandKind.ChooseAttackTarget,"hero:1");game=Restore(catalog,game);
            var view=game.View(1);Assert.That(view.Attack!.Ranged,Is.True);Assert.That(view.Attack.Unblockable,Is.EqualTo(support));
            Assert.That(view.Attack.CardTextBonus,Is.EqualTo(support?3:0));Assert.That(view.Attack.FinalAttack,Is.EqualTo(support?6:3));
            Assert.That(view.DefenseOptions.Any(o=>o.CardId==Lead && o.Block),Is.EqualTo(!support));
            if(support)
            {
                Assert.That(view.DefenseRestrictions[Lead],Is.EqualTo("unblockable"));string before=game.ExportSave();
                Assert.That(game.Execute(1,Cmd(game,1,CommandKind.Defend,Lead)).Code,Is.EqualTo("invalid_defense"));Assert.That(game.ExportSave(),Is.EqualTo(before));
            }
            Assert.That(view.DefenseOptions.Single(o=>o.CardId==Numeric).Assessment.Successful,Is.True);
            var defend=Cmd(game,1,CommandKind.Defend,Numeric);Assert.That(game.Execute(1,defend).Accepted,Is.True);game=Restore(catalog,game);string after=game.ExportSave();
            Assert.That(game.Execute(1,defend).Duplicate,Is.True);Assert.That(game.ExportSave(),Is.EqualTo(after));
            Assert.That(game.View(1).Players[1].AwaitingRespawn,Is.False);Assert.That(game.View(1).RedCrystal,Is.EqualTo(7));
            Assert.That(game.View(1).Events.Count(e=>e.Kind=="AttackResolved"),Is.EqualTo(1));
        }
        [Test]
        public void AttackerAloneDoesNotEnableSupportAndLeadChargeCanBlock()
        {
            var catalog=BattlefieldTests.Catalog();var game=Setup(catalog,false);Attack(game);
            Assert.That(game.View(1).Attack!.CardTextSourceUnits,Is.Empty);Apply(game,1,CommandKind.Defend,Lead);
            Assert.That(game.View(1).Events.Last(e=>e.Kind=="DefenseCalculated").Detail,Is.EqualTo("block"));Restore(catalog,game);
        }
        [TestCase("hero",Team.Blue,true)] [TestCase("melee",Team.Blue,true)] [TestCase("ranged",Team.Blue,true)] [TestCase("heavy",Team.Blue,true)] [TestCase("hero",Team.Red,false)]
        public void SupportUsesOtherFriendlyHeroesOrAnyMinionButNotEnemies(string kind,Team team,bool expected)
        {
            var catalog=BattlefieldTests.Catalog();var game=Setup(catalog,false);
            string id=kind=="hero" ? team==Team.Blue?"hero:2":"hero:3" : game.View(0).Units.First(u=>u.Kind==kind && u.Team==team).Id;
            Apply(game,0,CommandKind.DebugTeleport,id,cell:new Hex(8,-8));Attack(game);
            Assert.That(game.View(1).Attack!.CardTextBonus,Is.EqualTo(expected?3:0));Assert.That(game.View(1).Attack!.Unblockable,Is.EqualTo(expected));
            Assert.That(game.View(1).Attack!.CardTextSourceUnits,Is.EqualTo(expected?new[]{id}:new string[0]));Restore(catalog,game);
        }
        [Test]
        public void SeveralSupportersOnlyAddThreeOnceAndBackstabDoesNotBecomeUnblockable()
        {
            var catalog=BattlefieldTests.Catalog();var game=Setup(catalog);
            string minion=game.View(0).Units.First(u=>u.Kind=="melee" && u.Team==Team.Blue).Id;Apply(game,0,CommandKind.DebugTeleport,minion,cell:new Hex(6,-7));Attack(game);
            Assert.That(game.View(1).Attack!.CardTextSourceUnits.Count,Is.EqualTo(2));Assert.That(game.View(1).Attack!.CardTextBonus,Is.EqualTo(3));
            Assert.That(game.View(1).Attack!.EnemySupport,Is.EqualTo(1));Assert.That(game.View(1).Attack!.FinalAttack,Is.EqualTo(7));
            var backstab=Setup(catalog,true,"tigerclaw-03-背刺");
            while(backstab.View(0).ActiveSeat!=0)Apply(backstab,backstab.View(0).ActiveSeat!.Value,CommandKind.Pass);
            Attack(backstab);Assert.That(backstab.View(1).Attack!.CardTextBonus,Is.EqualTo(2));Assert.That(backstab.View(1).Attack!.Unblockable,Is.False);
        }
        [TestCase(1,0,0,true)] [TestCase(2,0,0,false)] [TestCase(2,1,0,true)] [TestCase(2,0,4,false)]
        public void RangedIconUsesRangedBonusAndExactDistance(int distance,int ranged,int radius,bool valid)
        {
            var catalog=BattlefieldTests.Catalog();var game=Setup(catalog,false);
            if(distance==2)Apply(game,0,CommandKind.DebugTeleport,"hero:1",cell:new Hex(8,-8));
            var codec=new JsonStateCodec();var state=codec.Read(game.ExportSave());state.Players[0].RangedBonus=ranged;state.Players[0].RangeBonus=radius;
            // Host-seeded numeric bonuses isolate item policy; command-based replay is covered separately.
            game=new GameSession(catalog,codec,state);Assert.That(game.View(0).AttackTargets.Contains("hero:1"),Is.EqualTo(valid));
        }
        [Test]
        public void WrongTargetsAndSeatsCannotSpendTheAttackAndMinionsStillUseNormalProtection()
        {
            var catalog=BattlefieldTests.Catalog();var game=Setup(catalog);string heavy=game.View(0).Units.First(u=>u.Team==Team.Red && u.Kind=="heavy").Id;
            Apply(game,0,CommandKind.DebugTeleport,heavy,cell:new Hex(5,-8));Apply(game,0,CommandKind.BeginPrimary);string before=game.ExportSave();
            foreach(var command in new[]{Cmd(game,1,CommandKind.ChooseAttackTarget,"hero:1"),Cmd(game,0,CommandKind.ChooseAttackTarget,"hero:2"),Cmd(game,0,CommandKind.ChooseAttackTarget,heavy)})
            {Assert.That(game.Execute(command.ActorSeat,command).Accepted,Is.False);Assert.That(game.ExportSave(),Is.EqualTo(before));}
            string minion=game.View(0).Units.First(u=>u.Team==Team.Red && u.Kind=="melee" && u.Position==new Hex(7,-9)).Id;
            Apply(game,0,CommandKind.ChooseAttackTarget,minion);Assert.That(game.View(0).Units.Any(u=>u.Id==minion),Is.False);
            Assert.That(game.View(0).Players[0].Gold,Is.EqualTo(2));Assert.That(new JsonStateCodec().Read(game.ExportSave()).Execution,Is.Null);Restore(catalog,game);
        }
        [Test]
        public void DefeatMayEndTheMatchAndSecondaryMovementDoesNotExecuteThePrimary()
        {
            var catalog=BattlefieldTests.Catalog();var game=Setup(catalog);Apply(game,0,CommandKind.DebugSetCrystal,"1",target:1);Attack(game);Apply(game,1,CommandKind.DeclineDefense);
            Assert.That(game.View(0).Winner,Is.EqualTo(Team.Blue));Assert.That(new JsonStateCodec().Read(game.ExportSave()).Execution,Is.Null);Restore(catalog,game);
            game=Setup(catalog);Apply(game,0,CommandKind.Move,cell:game.View(0).SecondaryMoves.First().Destination);
            Assert.That(game.View(0).Events.Any(e=>e.Kind=="AttackDeclared"),Is.False);Restore(catalog,game);
        }
        [Test]
        public void FrozenMinstrelAuraRecoveryStillCancelsAndFinishesItsOriginalAction()
        {
            var catalog=BattlefieldTests.Catalog();string save=File.ReadAllText(Path.Combine(ContentTests.Root(),"tests/fixtures/engine16-minstrel-aura.json"));
            var game=LocalGameFactory.Restore(catalog,save);Assert.That(game.ExportSave(),Is.EqualTo(save));Assert.That(game.View(0).SupportedPrimaryCards,Does.Not.Contain(Pincer));
            Apply(game,2,CommandKind.ChooseRecoveredCard,MinstrelTests.Static);Assert.That(game.View(0).Effects,Is.Empty);Assert.That(game.View(0).ActiveSeat,Is.EqualTo(3));Restore(catalog,game);
        }
        [Test]
        public void SupportedUnblockableDoesNotBypassRangedImmunityAndNoTargetsStopsCleanly()
        {
            var catalog=BattlefieldTests.Catalog();var game=Setup(catalog,false);
            string minion=game.View(0).Units.First(u=>u.Position==new Hex(7,-9)).Id;
            Apply(game,0,CommandKind.DebugTeleport,minion,cell:game.View(0).DebugTeleports[minion].First(h=>h.Distance(new Hex(6,-8))>5));
            Apply(game,0,CommandKind.DebugTeleport,"hero:1",cell:new Hex(8,-8));Apply(game,0,CommandKind.DebugTeleport,"hero:2",cell:new Hex(8,-9));
            Apply(game,0,CommandKind.DebugTeleport,"hero:3",cell:game.View(0).DebugTeleports["hero:3"].First(h=>h.Distance(new Hex(6,-8))>5));
            var codec=new JsonStateCodec();var state=codec.Read(game.ExportSave());state.Players[0].RangedBonus=1;
            // Query fixture isolates an immunity from a future source; existing reflection scenarios cover real creation.
            state.Effects.Add(new ActiveEffect {Id="test-immunity",Kind=EffectKind.NonAdjacentRangedImmunity,ProtectedUnitId="hero:1",Window=EffectTimeline.Create(1,1,4,EffectDuration.ThisTurn)!});
            Assert.That(CombatRules.CardTextModifier(catalog,state,catalog.Card(Pincer),state.Units.Single(u=>u.Seat==0),state.Units.Single(u=>u.Seat==1)).Amount,Is.EqualTo(3));
            Assert.That(CombatRules.AttackTargets(catalog,state,0),Is.Empty);
            game=new GameSession(catalog,codec,state);Apply(game,0,CommandKind.BeginPrimary);
            Assert.That(game.View(0).Events.Last(e=>e.Kind=="CardEffectStopped").Detail,Is.EqualTo("no_targets"));
            Assert.That(game.View(0).Events.Any(e=>e.Kind=="AttackDeclared"),Is.False);Assert.That(codec.Read(game.ExportSave()).Execution,Is.Null);
        }
    }
}

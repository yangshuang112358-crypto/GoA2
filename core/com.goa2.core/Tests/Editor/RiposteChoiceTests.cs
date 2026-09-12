using System.IO;
using System.Linq;
using Goa2.Application;
using Goa2.Domain;
using Goa2.Infrastructure;
using NUnit.Framework;
using static Goa2.Tests.SessionTests;
using static Goa2.Tests.TurnFlowTests;
using static Goa2.Tests.ConditionalDefenseTests;

namespace Goa2.Tests
{
    public sealed class RiposteChoiceTests
    {
        private static GameSession Counter(ContentCatalog catalog,int handCount=4,int engine=GameState.CurrentEngineVersion)
        {
            var game=Ready(catalog,Riposte,engine);
            foreach(var hand in game.View(0).OwnCards.Where(c=>c.Zone==CardZone.InHand).Skip(handCount).ToArray())
                Apply(game,0,CommandKind.DebugDiscard,hand.CardId,target:0);
            BarrierResponseTests.Attack(game);
            game=LocalGameFactory.Restore(catalog,game.ExportSave());
            Apply(game,1,CommandKind.Defend,Riposte);
            return game;
        }
        [TestCase(1,false)] [TestCase(4,false)] [TestCase(1,true)] [TestCase(4,true)]
        public void AttackerMayChooseDiscardOrDefeatWithOneOrSeveralCardsAndRestoreEachWindow(int cards,bool defeat)
        {
            var catalog=BattlefieldTests.Catalog();var game=Counter(catalog,cards);
            string pending=game.ExportSave();
            Assert.That(game.View(0).CanDeclineRetaliationDiscard,Is.True);
            Assert.That(game.View(0).ForcedDiscardCards.Count,Is.EqualTo(cards));
            Assert.That(game.View(0).Pending!.Source,Is.Empty);
            Assert.That(game.View(1).Pending!.Source,Is.EqualTo(Riposte));
            foreach(int? viewer in new int?[]{null,1,2,3}) Assert.That(game.View(viewer).CanDeclineRetaliationDiscard,Is.False);
            Assert.That(game.ExportSave(),Is.EqualTo(pending),"Legal queries are read-only");
            game=LocalGameFactory.Restore(catalog,pending);
            Assert.That(game.View(0).CanDeclineRetaliationDiscard,Is.True);
            string chosen=game.View(0).ForcedDiscardCards.Last();
            var command=Cmd(game,0,defeat ? CommandKind.DeclineRetaliationDiscard : CommandKind.ForcedDiscard,defeat ? "" : chosen);
            int beforeEvents=game.View(null).Events.Count;
            Assert.That(game.Execute(0,command).Accepted,Is.True);
            var view=game.View(null);
            Assert.That(view.Players[0].HandCount,Is.EqualTo(defeat ? cards : cards-1));
            Assert.That(view.Players[0].AwaitingRespawn,Is.EqualTo(defeat));
            Assert.That(view.BlueCrystal,Is.EqualTo(defeat ? 6 : 7));
            Assert.That(view.Players[1].Gold,Is.EqualTo(defeat ? 1 : 0));
            Assert.That(view.Players[3].Gold,Is.EqualTo(defeat ? 1 : 0));
            Assert.That(view.Events.Count(e=>e.Kind=="AttackResolved"),Is.EqualTo(1));
            Assert.That(view.Events.Count(e=>e.Kind=="HeroDefeated"),Is.EqualTo(defeat ? 1 : 0));
            Assert.That(view.Events.Count(e=>e.Kind=="CardResolved" && e.Seat==0),Is.EqualTo(1));
            Assert.That(view.Players[0].Revealed.Single().Zone,Is.EqualTo(CardZone.PlayedResolved));
            if(defeat) Assert.That(view.Effects,Is.Empty);
            else Assert.That(view.Effects.Single().SourceCardId,Is.EqualTo("wasp-00-闪耀之刃"));
            if(defeat)
            {
                Assert.That(view.Events.Skip(beforeEvents).Any(e=>e.Kind=="DiscardColorShown"),Is.False);
                foreach(int? viewer in new int?[]{null,0,2,3})
                    Assert.That(game.View(viewer).Events.Any(e=>e.CardId==Riposte || e.Kind=="HeroDefeatSource"),Is.False);
                Assert.That(game.View(1).Events.Single(e=>e.Kind=="HeroDefeatSource").CardId,Is.EqualTo(Riposte));
                Assert.That(view.Events.Single(e=>e.Kind=="HeroDefeated").From,Is.EqualTo(new Hex(7,-10)));
            }
            else Assert.That(game.View(0).OwnCards.Single(c=>c.CardId==chosen).Zone,Is.EqualTo(CardZone.Discarded));
            string after=game.ExportSave();game=LocalGameFactory.Restore(catalog,after);
            Assert.That(game.Execute(0,command).Duplicate,Is.True);Assert.That(game.ExportSave(),Is.EqualTo(after));
            Assert.That(game.Execute(0,Cmd(game,0,CommandKind.DeclineRetaliationDiscard)).Accepted,Is.False);
            Assert.That(game.ExportSave(),Is.EqualTo(after));
        }
        [Test]
        public void WrongResponderStaleIdentityAndWrongPhaseCannotChooseDefeatOrPartiallyModifyState()
        {
            var catalog=BattlefieldTests.Catalog();var game=Ready(catalog,Riposte);
            string before=game.ExportSave();
            Assert.That(game.Execute(0,Cmd(game,0,CommandKind.DeclineRetaliationDiscard)).Accepted,Is.False);
            Assert.That(game.ExportSave(),Is.EqualTo(before));
            BarrierResponseTests.Attack(game);
            before=game.ExportSave();
            Assert.That(game.Execute(1,Cmd(game,1,CommandKind.DeclineRetaliationDiscard)).Accepted,Is.False);
            Assert.That(game.ExportSave(),Is.EqualTo(before));
            Apply(game,1,CommandKind.Defend,Riposte);before=game.ExportSave();
            foreach(int seat in new[]{1,2,3})
            {
                Assert.That(game.Execute(seat,Cmd(game,seat,CommandKind.DeclineRetaliationDiscard)).Code,Is.EqualTo("invalid_retaliation_choice"));
                Assert.That(game.ExportSave(),Is.EqualTo(before));
            }
            var command=Cmd(game,0,CommandKind.DeclineRetaliationDiscard);
            Assert.That(game.Execute(1,command).Code,Is.EqualTo("unauthorized"));
            command.ExpectedRevision--;
            Assert.That(game.Execute(0,command).Code,Is.EqualTo("stale_revision"));
            Assert.That(game.Execute(0,Cmd(game,0,CommandKind.Pass)).Accepted,Is.False);
            Assert.That(game.ExportSave(),Is.EqualTo(before));
        }
        [TestCase(7)] [TestCase(8)] [TestCase(9)]
        public void LegacyEnginesKeepMandatoryDiscardAndCannotUpgradeWhileResponseIsPending(int engine)
        {
            var catalog=BattlefieldTests.Catalog();var game=Counter(catalog,4,engine);
            string before=game.ExportSave();
            Assert.That(game.View(0).CanDeclineRetaliationDiscard,Is.False);
            Assert.That(game.Execute(0,Cmd(game,0,CommandKind.DeclineRetaliationDiscard)).Code,Is.EqualTo("invalid_retaliation_choice"));
            Assert.That(game.Execute(0,Cmd(game,0,CommandKind.UpgradeEngine,GameState.CurrentEngineVersion.ToString())).Accepted,Is.False);
            Assert.That(game.ExportSave(),Is.EqualTo(before));
            game=LocalGameFactory.Restore(catalog,before);
            Apply(game,0,CommandKind.ForcedDiscard,game.View(0).ForcedDiscardCards.First());
            Assert.That(game.View(null).BlueCrystal,Is.EqualTo(7));
            Assert.That(game.View(null).EngineVersion,Is.EqualTo(engine));
            Apply(game,0,CommandKind.DebugAdvance,"turn");
            Apply(game,0,CommandKind.UpgradeEngine,GameState.CurrentEngineVersion.ToString());
            Assert.That(new JsonStateCodec().Read(game.ExportSave()).InitialEngineVersion,Is.EqualTo(engine));
            Assert.That(LocalGameFactory.Restore(catalog,game.ExportSave()).ExportSave(),Is.EqualTo(game.ExportSave()));
        }
        [TestCase(MeleeBlock)] [TestCase(BarrierResponseTests.Deflection)] [TestCase(BarrierResponseTests.Reflection)]
        public void OtherForcedDiscardsCannotChooseToBeDefeated(string card)
        {
            var catalog=BattlefieldTests.Catalog();var game=card==MeleeBlock ? Ready(catalog,card) : CombatFlowTests.Duel(catalog,equipment:card);
            BarrierResponseTests.Attack(game);Apply(game,1,CommandKind.Defend,card);
            string before=game.ExportSave();
            Assert.That(game.View(0).CanDeclineRetaliationDiscard,Is.False);
            Assert.That(game.Execute(0,Cmd(game,0,CommandKind.DeclineRetaliationDiscard)).Code,Is.EqualTo("invalid_retaliation_choice"));
            Assert.That(game.ExportSave(),Is.EqualTo(before));
            Apply(game,0,CommandKind.ForcedDiscard,game.View(0).ForcedDiscardCards.First());
            Assert.That(game.View(null).BlueCrystal,Is.EqualTo(7));
            Assert.That(LocalGameFactory.Restore(catalog,game.ExportSave()).ExportSave(),Is.EqualTo(game.ExportSave()));
        }
        [TestCase("hand")] [TestCase("empty-defense")]
        public void ActualEngineNineCapturesReplayAndRetainBothOldBranches(string name)
        {
            var catalog=BattlefieldTests.Catalog();var codec=new JsonStateCodec();
            string json=File.ReadAllText(Path.Combine(ContentTests.Root(),"tests","fixtures","engine9-riposte-"+name+".json"));
            var game=LocalGameFactory.Restore(catalog,json);
            Assert.That(game.ExportSave(),Is.EqualTo(codec.Write(codec.Read(json))));
            Assert.That(game.View(null).EngineVersion,Is.EqualTo(9));
            string before=game.ExportSave();
            Assert.That(game.View(0).CanDeclineRetaliationDiscard,Is.False);
            Assert.That(game.Execute(0,Cmd(game,0,CommandKind.DeclineRetaliationDiscard)).Code,Is.EqualTo("invalid_retaliation_choice"));
            Assert.That(game.ExportSave(),Is.EqualTo(before));
            if(name=="hand") Apply(game,0,CommandKind.ForcedDiscard,"wasp-01-电击");
            else Apply(game,1,CommandKind.Defend,Riposte);
            Assert.That(game.View(null).Players[0].AwaitingRespawn,Is.EqualTo(name!="hand"));
            Assert.That(game.View(null).Events.Count(e=>e.Kind=="AttackResolved"),Is.EqualTo(1));
            Assert.That(LocalGameFactory.Restore(catalog,game.ExportSave()).ExportSave(),Is.EqualTo(game.ExportSave()));
        }
        [Test]
        public void VoluntaryDefeatImmediatelyWinsWithoutResumingOrDuplicatingTheAttack()
        {
            var catalog=BattlefieldTests.Catalog();var game=Ready(catalog,Riposte);
            Apply(game,0,CommandKind.DebugSetCrystal,"1",target:0);
            BarrierResponseTests.Attack(game);Apply(game,1,CommandKind.Defend,Riposte);
            game=LocalGameFactory.Restore(catalog,game.ExportSave());
            Apply(game,0,CommandKind.DeclineRetaliationDiscard);
            var state=new JsonStateCodec().Read(game.ExportSave());
            Assert.That(state.Phase,Is.EqualTo(Phase.Finished));Assert.That(state.Winner,Is.EqualTo(Team.Red));
            Assert.That(state.Execution,Is.Null);Assert.That(state.Pending,Is.Null);Assert.That(state.ActiveSeat,Is.Null);
            Assert.That(state.Events.Last().Kind,Is.EqualTo("MatchWon"));
            Assert.That(state.Events.Count(e=>e.Kind=="AttackResolved"),Is.EqualTo(1));
            Assert.That(state.Events.Count(e=>e.Kind=="HeroDefeated"),Is.EqualTo(1));
            Assert.That(state.Events.Any(e=>e.Kind=="CardResolved" && e.Seat==0),Is.False);
            Assert.That(LocalGameFactory.Restore(catalog,game.ExportSave()).ExportSave(),Is.EqualTo(game.ExportSave()));
        }
        [Test]
        public void DebugAttackCanAlsoBeCounterDefeatedWithoutConsumingACardOrTurn()
        {
            var catalog=BattlefieldTests.Catalog();var game=LocalGameFactory.Create(catalog,"debug-counter",new[]{"A","B","C","D"},42,true);
            Apply(game,0,CommandKind.DebugPrepare,"wasp,tigerclaw,brogan,arien");
            Apply(game,0,CommandKind.DebugEquipCard,Riposte,target:1);
            Apply(game,0,CommandKind.DebugAttack,"hero:1|0");Apply(game,1,CommandKind.Defend,Riposte);
            Assert.That(game.View(0).CanDeclineRetaliationDiscard,Is.True);
            game=LocalGameFactory.Restore(catalog,game.ExportSave());Apply(game,0,CommandKind.DeclineRetaliationDiscard);
            var view=game.View(null);
            Assert.That(view.Phase,Is.EqualTo(Phase.Planning));Assert.That(view.Turn,Is.EqualTo(1));
            Assert.That(view.Players[0].HandCount,Is.EqualTo(5));Assert.That(view.Players[0].AwaitingRespawn,Is.True);
            Assert.That(view.Events.Count(e=>e.Kind=="DebugAttackCompleted"),Is.EqualTo(1));
            Assert.That(view.Events.Any(e=>e.Kind=="CardResolved"),Is.False);
            Assert.That(LocalGameFactory.Restore(catalog,game.ExportSave()).ExportSave(),Is.EqualTo(game.ExportSave()));
        }
    }
}

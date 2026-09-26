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
    public sealed class CloakAndDaggerTests
    {
        internal const string Card="tigerclaw-13-斗篷与匕首", Gold="tigerclaw-00-瞬闪打击";
        internal static GameSession Setup(ContentCatalog catalog, bool owned=true, int engine=GameState.CurrentEngineVersion, string played=Gold)
        {
            var game=LocalGameFactory.Create(catalog,"cloak",new[]{"A","B","C","D"},42,true,engine);
            Apply(game,0,CommandKind.DebugPrepare,"tigerclaw,sabina,brogan,wasp");
            if(owned){Apply(game,0,CommandKind.DebugSetGold,"28",target:0);Apply(game,0,CommandKind.DebugAdvance,"round");Apply(game,0,CommandKind.ResolveRoundEnd);
                for(int i=0;i<7;i++)Apply(game,0,CommandKind.ChooseUpgrade,game.View(0).UpgradeOptions.First().CardId);}
            Apply(game,0,CommandKind.DebugEquipCard,OpportuneMomentTests.Card,target:0);
            Apply(game,0,CommandKind.DebugTeleport,"hero:0",cell:new Hex(-1,0));
            Apply(game,0,CommandKind.DebugTeleport,"hero:1",cell:new Hex(7,-8));
            Apply(game,0,CommandKind.DebugTeleport,"hero:2",cell:new Hex(4,-8));
            Apply(game,0,CommandKind.DebugTeleport,"hero:3",cell:new Hex(6,-5));
            string[] first={OpportuneMomentTests.Card,"sabina-06-并肩作战","brogan-06-铜墙铁壁","wasp-07-抵挡屏障"};
            for(int i=0;i<4;i++)Apply(game,i,CommandKind.SelectCard,first[i]);OpportuneMomentTests.AdvanceTo(game,0);
            Apply(game,0,CommandKind.BeginPrimary);Apply(game,0,CommandKind.ChoosePlacement,cell:new Hex(-1,-1));
            IronWallTests.FinishTurn(game);
            Apply(game,0,CommandKind.DebugTeleport,"hero:0",cell:new Hex(6,-8));
            if(played!=Gold)Apply(game,0,CommandKind.DebugEquipCard,played,target:0);
            string[] second={played,"sabina-00-近身射击","brogan-13-冲拳","wasp-13-控物"};
            for(int i=0;i<4;i++)Apply(game,i,CommandKind.SelectCard,second[i]);OpportuneMomentTests.AdvanceTo(game,0);
            return game;
        }
        [Test]
        public void PreludeIsSeparateFromRequiredGoldMovementAndDoesNotRepeatInternally()
        {
            var catalog=BattlefieldTests.Catalog();var game=Setup(catalog);Apply(game,0,CommandKind.BeginPrimary);
            Assert.That(game.View(0).Pending?.Source,Is.EqualTo(Card));Assert.That(game.View(0).Pending?.Optional,Is.True);
            Assert.That(game.View(0).EffectMoves.All(m=>m.Path.Count<=3),Is.True);
            game=ChargeTests.Restore(catalog,game);Apply(game,0,CommandKind.ChooseEffectMove,"skip");
            Assert.That(game.View(0).Pending?.ResumeAt,Is.EqualTo("strike_through_enemy"));
            Apply(game,0,CommandKind.ChooseEffectMove,cell:new Hex(8,-8));
            Apply(game,1,CommandKind.DeclineDefense);
            Assert.That(game.View(0).Pending?.ResumeAt,Is.EqualTo("ultimate_attack_repeat"));
            Assert.That(game.View(0).Events.Count(e=>e.Kind=="UltimateTriggered" && e.CardId==Card),Is.EqualTo(1));
        }
        [Test]
        public void PreludeMayMoveSomewhereWithoutGoldRouteAndRetainsMovement()
        {
            var catalog=BattlefieldTests.Catalog();var game=Setup(catalog);Apply(game,0,CommandKind.BeginPrimary);
            Assert.That(game.View(0).Pending?.Source,Is.EqualTo(Card));
            Apply(game,0,CommandKind.ChooseEffectMove,cell:new Hex(5,-7));
            Assert.That(game.View(0).Units.Single(u=>u.Seat==0).Position,Is.EqualTo(new Hex(5,-7)));
            Assert.That(game.View(0).Events.Any(e=>e.Kind=="AttackDeclared"),Is.False);
            Assert.That(game.View(0).OwnCards.Single(c=>c.CardId==Gold).Zone,Is.EqualTo(CardZone.PlayedResolved));
            ChargeTests.Restore(catalog,game);
        }
        [Test]
        public void SecondaryMovementChoosesNewDestinationAfterPrelude()
        {
            var catalog=BattlefieldTests.Catalog();var game=Setup(catalog);
            Apply(game,0,CommandKind.Move,"begin");Assert.That(game.View(0).Pending?.Source,Is.EqualTo(Card));
            Apply(game,0,CommandKind.ChooseEffectMove,cell:new Hex(5,-7));
            Assert.That(game.View(0).Pending?.ResumeAt,Is.EqualTo("before_action_movement_destination"));
            var destination=game.View(0).EffectMoves.First().Destination;
            Apply(game,0,CommandKind.ChooseEffectMove,cell:destination);
            Assert.That(game.View(0).Units.Single(u=>u.Seat==0).Position,Is.EqualTo(destination));ChargeTests.Restore(catalog,game);
        }
        [TestCase(false,57)] [TestCase(true,57)] [TestCase(false,GameState.CurrentEngineVersion)]
        public void OlderEngineDoesNotGivePrelude(bool owned,int engine)
        {
            var game=Setup(BattlefieldTests.Catalog(),owned,engine);Apply(game,0,CommandKind.BeginPrimary);
            Assert.That(game.View(0).Pending?.ResumeAt,Is.EqualTo("strike_through_enemy"));
        }

        private static void FirstAttack(GameSession game,bool defend=true)
        {
            Apply(game,0,CommandKind.BeginPrimary);Apply(game,0,CommandKind.ChooseEffectMove,"skip");
            Apply(game,0,CommandKind.ChooseEffectMove,cell:new Hex(8,-8));
            if(defend)Apply(game,1,CommandKind.Defend,game.View(1).DefenseOptions.First(o=>o.Assessment.Successful).CardId);
            else Apply(game,1,CommandKind.DeclineDefense);
        }

        [Test]
        public void RepeatExecutesWholeGoldExcludesSurvivingFirstTargetAndCannotRepeatAgain()
        {
            var catalog=BattlefieldTests.Catalog();var game=Setup(catalog);
            int resolved=game.View(0).Events.Count(e=>e.Kind=="CardResolved" && e.CardId==Gold);
            Apply(game,0,CommandKind.DebugTeleport,"hero:3",cell:new Hex(6,-7));FirstAttack(game);
            Assert.That(game.View(0).Units.Any(u=>u.Seat==1),Is.True);game=ChargeTests.Restore(catalog,game);
            Apply(game,0,CommandKind.ChoosePrimaryOption,"repeat");game=ChargeTests.Restore(catalog,game);
            Apply(game,0,CommandKind.ChooseEffectMove,cell:new Hex(7,-7));
            Assert.That(game.View(0).EffectMoves.Select(m=>m.Destination),Has.No.Member(new Hex(7,-9)));
            Apply(game,0,CommandKind.ChooseEffectMove,cell:new Hex(5,-7));Apply(game,3,CommandKind.DeclineDefense);
            Assert.That(game.View(0).OwnCards.Single(c=>c.CardId==Gold).Zone,Is.EqualTo(CardZone.PlayedResolved));
            Assert.That(game.View(0).Events.Count(e=>e.Kind=="AttackDeclared" && e.CardId==Gold),Is.EqualTo(2));
            Assert.That(game.View(0).Events.Count(e=>e.Kind=="CardResolved" && e.CardId==Gold),Is.EqualTo(resolved+1));
            Assert.That(game.View(0).Events.Count(e=>e.Kind=="UltimateTriggered" && e.CardId==Card),Is.EqualTo(2));
            Assert.That(game.View(0).Pending?.ResumeAt,Is.Not.EqualTo("ultimate_attack_repeat"));ChargeTests.Restore(catalog,game);
        }

        [Test]
        public void RepeatedPreludeMayFailTheGoldRouteWithoutRollingBackOrOfferingThirdAttack()
        {
            var catalog=BattlefieldTests.Catalog();var game=Setup(catalog);FirstAttack(game);
            Apply(game,0,CommandKind.ChoosePrimaryOption,"repeat");Apply(game,0,CommandKind.ChooseEffectMove,cell:new Hex(8,-7));
            Assert.That(game.View(0).Units.Single(u=>u.Seat==0).Position,Is.EqualTo(new Hex(8,-7)));
            Assert.That(game.View(0).Events.Count(e=>e.Kind=="AttackDeclared" && e.CardId==Gold),Is.EqualTo(1));
            Assert.That(game.View(0).OwnCards.Single(c=>c.CardId==Gold).Zone,Is.EqualTo(CardZone.PlayedResolved));ChargeTests.Restore(catalog,game);
        }

        [Test]
        public void InitialNoRouteMayBeEnabledByPreludeAndSkippingRepeatDoesNotMoveAgain()
        {
            var catalog=BattlefieldTests.Catalog();var game=Setup(catalog);
            Apply(game,0,CommandKind.DebugTeleport,"hero:1",cell:new Hex(5,-6));
            Apply(game,0,CommandKind.BeginPrimary);Apply(game,0,CommandKind.ChooseEffectMove,cell:new Hex(5,-7));
            Apply(game,0,CommandKind.ChooseEffectMove,cell:new Hex(5,-5));Apply(game,1,CommandKind.DeclineDefense);
            Apply(game,0,CommandKind.ChoosePrimaryOption,"finish");
            Assert.That(game.View(0).Events.Count(e=>e.Kind=="UltimateTriggered" && e.CardId==Card),Is.EqualTo(1));
            ChargeTests.Restore(catalog,game);
        }

        [Test]
        public void RedAttackGetsPreludeButNoUltimateRepeat()
        {
            var catalog=BattlefieldTests.Catalog();var game=Setup(catalog,played:"tigerclaw-02-偷袭");
            Apply(game,0,CommandKind.BeginPrimary);Apply(game,0,CommandKind.ChooseEffectMove,"skip");
            Apply(game,0,CommandKind.ChooseAttackTarget,"hero:1");Apply(game,1,CommandKind.DeclineDefense);
            if(game.View(0).Pending?.Kind=="effect_move")Apply(game,0,CommandKind.ChooseEffectMove,"skip");
            Assert.That(game.View(0).Events.Any(e=>e.Kind=="UltimateRepeatOffered"),Is.False);
            Assert.That(game.View(0).Events.Count(e=>e.Kind=="UltimateTriggered" && e.CardId==Card),Is.EqualTo(1));ChargeTests.Restore(catalog,game);
        }

        [TestCase(false)] [TestCase(true)]
        public void InvalidPreludeOrRepeatCommandsAreAtomic(bool repeat)
        {
            var game=Setup(BattlefieldTests.Catalog());if(repeat)FirstAttack(game);else Apply(game,0,CommandKind.BeginPrimary);
            string before=game.ExportSave();
            foreach(var cmd in new[]{Cmd(game,1,CommandKind.ChooseEffectMove,"skip"),Cmd(game,0,CommandKind.ChooseEffectMove,destination:new Hex(7,-8)),
                Cmd(game,0,CommandKind.ChoosePrimaryOption,"unknown"),Cmd(game,0,CommandKind.Pass),Cmd(game,0,CommandKind.BeginPrimary),
                Cmd(game,0,CommandKind.DebugTeleport,"hero:0",destination:new Hex(5,-7))})
            {Assert.That(game.Execute(cmd.ActorSeat,cmd).Accepted,Is.False,cmd.Kind.ToString());Assert.That(game.ExportSave(),Is.EqualTo(before));}
        }

        [Test]
        public void DuplicatePreludeDoesNotSpendMovementTwice()
        {
            var catalog=BattlefieldTests.Catalog();var game=Setup(catalog);Apply(game,0,CommandKind.BeginPrimary);
            var command=Cmd(game,0,CommandKind.ChooseEffectMove,destination:new Hex(6,-7));
            Assert.That(game.Execute(0,command).Accepted,Is.True);string after=game.ExportSave();
            Assert.That(game.Execute(0,command).Duplicate,Is.True);Assert.That(game.ExportSave(),Is.EqualTo(after));ChargeTests.Restore(catalog,game);
        }

        [Test]
        public void FastMoveUsesItsOwnLegalRegionSetAfterPrelude()
        {
            var catalog=BattlefieldTests.Catalog();var game=Setup(catalog);var state=new JsonStateCodec().Read(game.ExportSave());
            var unit=state.Units.Single(u=>u.Seat==0);var occupied=state.Units.Select(u=>u.Position).ToList();
            var origin=catalog.Cells.First(c=>!c.Obstacle && !occupied.Contains(c.Position) &&
                !state.Units.Any(u=>u.Team!=unit.Team && catalog.Cell(u.Position).Region==c.Region)).Position;
            Apply(game,0,CommandKind.DebugTeleport,"hero:0",cell:origin);
            var command=Cmd(game,0,CommandKind.Move,"begin",mode:MoveMode.Fast);Assert.That(game.Execute(0,command).Accepted,Is.True);
            Apply(game,0,CommandKind.ChooseEffectMove,"skip");game=ChargeTests.Restore(catalog,game);
            var destination=game.View(0).EffectMoves.First().Destination;Apply(game,0,CommandKind.ChooseEffectMove,cell:destination);
            Assert.That(game.View(0).Events.Last(e=>e.Kind=="UnitMoved").Detail,Is.EqualTo("Fast"));ChargeTests.Restore(catalog,game);
        }

        [Test]
        public void ImmunityExpiryRemovesPreludeWithoutRemovingPurpleOwnership()
        {
            var catalog=BattlefieldTests.Catalog();var game=Setup(catalog);IronWallTests.FinishTurn(game);
            Apply(game,0,CommandKind.DebugEquipCard,Gold,target:0);
            for(int i=0;i<4;i++)Apply(game,i,CommandKind.SelectCard,i==0?Gold:game.View(i).OwnCards.First(c=>c.Zone==CardZone.InHand).CardId);
            OpportuneMomentTests.AdvanceTo(game,0);Apply(game,0,CommandKind.BeginPrimary);
            Assert.That(game.View(0).Pending?.ResumeAt,Is.EqualTo("strike_through_enemy"));
            Assert.That(game.View(0).Players[0].PurpleCardId,Is.EqualTo(Card));ChargeTests.Restore(catalog,game);
        }

        [Test]
        public void VictoryDoesNotOfferRepeatAndPriorDefenseFrameIsByteStable()
        {
            var catalog=BattlefieldTests.Catalog();var game=Setup(catalog);Apply(game,0,CommandKind.DebugSetCrystal,"1",target:1);FirstAttack(game,false);
            Assert.That(game.View(0).Phase,Is.EqualTo(Phase.Finished));Assert.That(game.View(0).Events.Any(e=>e.Kind=="UltimateRepeatOffered"),Is.False);
            string save=System.IO.File.ReadAllText(System.IO.Path.Combine(ContentTests.Root(),"tests/fixtures/engine57-phantasm-defense-discard.json"));
            game=LocalGameFactory.Restore(catalog,save);Assert.That(game.ExportSave(),Is.EqualTo(save));
            Apply(game,1,CommandKind.ForcedDiscard,game.View(1).ForcedDiscardCards.First());ChargeTests.Restore(catalog,game);
        }

        [Test]
        public void PreludeFixedBudgetAndImmunityTraversalAreNotAttackTargetsOrOpponentChoices()
        {
            var catalog=BattlefieldTests.Catalog();var game=Setup(catalog);Apply(game,0,CommandKind.BeginPrimary);
            Assert.That(game.View(0).EffectMoves.Single(m=>m.Destination==new Hex(8,-8)).Path,Has.Member(new Hex(7,-8)));
            Assert.That(game.View(0).EffectMoves.Any(m=>m.Destination==new Hex(7,-8)),Is.False);
            Assert.That(game.View(0).EffectMoves.All(m=>!catalog.Cell(m.Destination).Obstacle && m.Path.Count<=3),Is.True);
            Assert.That(game.View(1).EffectMoves,Is.Empty);Assert.That(game.View(null).EffectMoves,Is.Empty);
            Assert.That(game.View(0).AttackTargets,Is.Empty);
            string before=game.ExportSave();Assert.That(game.Execute(0,Cmd(game,0,CommandKind.ChooseEffectMove,destination:new Hex(5,-5))).Accepted,Is.False);
            Assert.That(game.ExportSave(),Is.EqualTo(before));
        }

        [Test]
        public void RegistryRequiresExactCardAndUnrelatedProtectionIsNotFullImmunity()
        {
            var catalog=BattlefieldTests.Catalog();var game=Setup(catalog);var state=new JsonStateCodec().Read(game.ExportSave());
            Assert.That(UltimateRules.HasImmuneActionPrelude(catalog,state,0),Is.True);
            state.Effects.Single(e=>e.SourceCardId==OpportuneMomentTests.Card).Kind=EffectKind.OtherEnemyActionImmunity;
            Assert.That(UltimateRules.HasImmuneActionPrelude(catalog,state,0),Is.False);
            Assert.That(UltimateRules.HasProgram(catalog.Card(Card),57),Is.False);
            catalog.Card(Card).Text+="可选";Assert.That(UltimateRules.HasProgram(catalog.Card(Card)),Is.False);
        }
    }
}

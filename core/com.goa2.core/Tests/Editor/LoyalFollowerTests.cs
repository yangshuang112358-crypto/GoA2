using System;
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
    public sealed class LoyalFollowerTests
    {
        internal const string Loyal="shargatha-15-忠实信徒", Red="shargatha-01-劈砍";
        private const CommandKind Recover=CommandKind.ChooseRecoveredCard;
        internal static GameSession Setup(ContentCatalog catalog,string kind="melee",Team team=Team.Red,string discard=Red,int engine=GameState.CurrentEngineVersion,string defender="wasp")
        {
            var game=LocalGameFactory.Create(catalog,"loyal",new[]{"A","B","C","D"},42,true,engine);
            Apply(game,0,CommandKind.DebugPrepare,"shargatha,"+defender+",brogan,"+(defender=="arien" ? "wasp" : "arien"));Apply(game,0,CommandKind.DebugEquipCard,Loyal,target:0);
            Apply(game,0,CommandKind.DebugTeleport,"hero:0",cell:new Hex(7,-10));
            if(kind!="none")
            {
                var unit=game.View(null).Units.First(u=>u.Kind==kind && u.Team==team);
                Apply(game,0,CommandKind.DebugTeleport,unit.Id,cell:new Hex(8,-10));
            }
            if(discard!="") Apply(game,0,CommandKind.DebugDiscard,discard,target:0);
            return game;
        }
        internal static void Select(GameSession game)
        {
            string[] cards={Loyal,"wasp-07-抵挡屏障","brogan-06-铜墙铁壁","arien-07-潮水"};
            for(int i=0;i<4;i++) Apply(game,i,CommandKind.SelectCard,cards[i]);
            Assert.That(game.View(0).ActiveSeat,Is.EqualTo(0));
        }
        [Test]
        public void ContractValuesAndVersionAreExact()
        {
            var c=BattlefieldTests.Catalog().Card(Loyal);
            Assert.That(c.PrimaryCategory,Is.EqualTo("技能"));Assert.That(c.PrimaryValue,Is.Zero);Assert.That(c.Subtype,Is.Null);
            Assert.That(c.Initiative,Is.EqualTo(10));Assert.That(c.SecondaryDefense,Is.EqualTo(6));Assert.That(c.SecondaryMovement,Is.EqualTo(3));
            Assert.That(CombatRules.HasPrimaryProgram(c),Is.True);Assert.That(CombatRules.HasPrimaryProgram(c,11),Is.False);
        }
        [TestCase("melee",Team.Blue)] [TestCase("melee",Team.Red)]
        [TestCase("ranged",Team.Blue)] [TestCase("ranged",Team.Red)]
        [TestCase("heavy",Team.Blue)] [TestCase("heavy",Team.Red)]
        public void EitherTeamAndAllMinionKindsEnableExactlyOneRecoveryWithoutRemovingAnything(string kind,Team team)
        {
            var catalog=BattlefieldTests.Catalog();var game=Setup(catalog,kind,team);Select(game);
            var units=game.View(null).Units.Select(u=>u.Id+":"+u.Position).ToArray();int bonus=new JsonStateCodec().Read(game.ExportSave()).Players[0].AttackBonus;
            Apply(game,0,CommandKind.BeginPrimary);Assert.That(game.View(0).Pending?.Kind,Is.EqualTo("recover_discard"));
            game=LocalGameFactory.Restore(catalog,game.ExportSave());var command=Cmd(game,0,Recover,Red);
            Assert.That(game.Execute(0,command).Accepted,Is.True);
            Assert.That(game.View(0).OwnCards.Single(c=>c.CardId==Red).Zone,Is.EqualTo(CardZone.InHand));
            Assert.That(game.View(0).OwnCards.Single(c=>c.CardId==Loyal).Zone,Is.EqualTo(CardZone.PlayedResolved));
            Assert.That(game.View(null).Units.Select(u=>u.Id+":"+u.Position),Is.EqualTo(units));
            Assert.That(game.View(0).Players[0].Gold,Is.Zero);Assert.That(new JsonStateCodec().Read(game.ExportSave()).Players[0].AttackBonus,Is.EqualTo(bonus));
            Assert.That(game.View(0).Events.Count(e=>e.Kind=="CardRecovered" && e.CardId==Red),Is.EqualTo(1));
            string after=game.ExportSave();game=LocalGameFactory.Restore(catalog,after);
            Assert.That(game.Execute(0,command).Duplicate,Is.True);Assert.That(game.ExportSave(),Is.EqualTo(after));
        }
        [TestCase("none",Red)] [TestCase("melee","")] [TestCase("hero",Red)]
        public void MissingMinionOrEmptyDiscardFinishesWithoutInventingAChoice(string kind,string discard)
        {
            var catalog=BattlefieldTests.Catalog();var game=Setup(catalog,kind,Team.Red,discard);Select(game);Apply(game,0,CommandKind.BeginPrimary);
            Assert.That(game.View(null).Pending?.Kind,Is.Not.EqualTo("recover_discard"));
            Assert.That(game.View(0).OwnCards.Single(c=>c.CardId==Loyal).Zone,Is.EqualTo(CardZone.PlayedResolved));
            Assert.That(game.View(0).Events.Any(e=>e.Kind=="CardRecovered"),Is.False);
        }
        [Test]
        public void GoldCanBeRecoveredAndOptionalRecoveryCanBeSkipped()
        {
            var catalog=BattlefieldTests.Catalog();var game=Setup(catalog,discard:"shargatha-00-反击");Select(game);Apply(game,0,CommandKind.BeginPrimary);
            string before=game.ExportSave();Apply(game,0,Recover,"skip");Assert.That(game.View(0).OwnCards.Single(c=>c.CardId=="shargatha-00-反击").Zone,Is.EqualTo(CardZone.Discarded));
            game=LocalGameFactory.Restore(catalog,before);Apply(game,0,Recover,"shargatha-00-反击");Assert.That(game.View(0).OwnCards.Single(c=>c.CardId=="shargatha-00-反击").Zone,Is.EqualTo(CardZone.InHand));
        }
        [Test]
        public void WrongSeatIdentityStageCardAndRevisionLeaveThePendingSaveUnchanged()
        {
            var catalog=BattlefieldTests.Catalog();var game=Setup(catalog);Select(game);Apply(game,0,CommandKind.BeginPrimary);string before=game.ExportSave();
            foreach(int seat in new[]{1,2,3}) Assert.That(game.Execute(seat,Cmd(game,seat,Recover,Red)).Accepted,Is.False);
            foreach(string id in new[]{Loyal,"shargatha-00-反击","wasp-01-电击","shargatha-12-幻化","", "missing"}) Assert.That(game.Execute(0,Cmd(game,0,Recover,id)).Accepted,Is.False);
            var command=Cmd(game,0,Recover,Red);Assert.That(game.Execute(1,command).Code,Is.EqualTo("unauthorized"));command.ExpectedRevision--;
            Assert.That(game.Execute(0,command).Code,Is.EqualTo("stale_revision"));
            foreach(var kind in new[]{CommandKind.BeginPrimary,CommandKind.Pass,CommandKind.DebugRecover,CommandKind.DebugTeleport})
                Assert.That(game.Execute(0,Cmd(game,0,kind,Red)).Accepted,Is.False);
            Assert.That(game.ExportSave(),Is.EqualTo(before));
        }
        [Test]
        public void RecoveryDoesNotLeakThePrivateCardThroughPendingOrEvents()
        {
            var game=Setup(BattlefieldTests.Catalog());Select(game);Apply(game,0,CommandKind.BeginPrimary);
            Assert.That(game.View(null).Pending!.CandidateUnits,Is.Empty);Assert.That(game.View(null).Pending!.Source,Is.EqualTo(Loyal));
            foreach(int? seat in new int?[]{null,1,2,3}) Assert.That(game.View(seat).RecoverableCards,Is.Empty);
            var own=game.View(0);Assert.That(own.RecoverableCards,Is.EqualTo(new[]{Red}));own.RecoverableCards.Clear();
            Assert.That(game.View(0).RecoverableCards,Is.EqualTo(new[]{Red}));
            Apply(game,0,Recover,Red);
            foreach(int? seat in new int?[]{null,1,2,3}) Assert.That(game.View(seat).Events.Any(e=>e.Kind=="CardRecovered" && e.CardId==Red),Is.False);
            Assert.That(game.View(null).Events.Any(e=>e.Kind=="RecoveredColorShown" && e.Detail=="red"),Is.True);
        }
        [Test]
        public void ExactTwoHexDistanceDoesNotSatisfyAdjacency()
        {
            var catalog=BattlefieldTests.Catalog();var game=Setup(catalog,"none");var minion=game.View(null).Units.First(u=>u.Kind=="melee");
            Apply(game,0,CommandKind.DebugTeleport,minion.Id,cell:new Hex(8,-9));Select(game);Apply(game,0,CommandKind.BeginPrimary);
            Assert.That(game.View(null).Pending?.Kind,Is.Not.EqualTo("recover_discard"));
        }
        [TestCase(false)] [TestCase(true)]
        public void MovementOrPassingCannotExecuteRecovery(bool move)
        {
            var game=Setup(BattlefieldTests.Catalog());Select(game);
            if(move) Apply(game,0,CommandKind.Move,cell:game.View(0).SecondaryMoves.First().Destination);else Apply(game,0,CommandKind.Pass);
            Assert.That(game.View(0).OwnCards.Single(c=>c.CardId==Red).Zone,Is.EqualTo(CardZone.Discarded));
            Assert.That(game.View(0).Events.Any(e=>e.Kind=="RecoverDiscardRequired"),Is.False);
        }
        [Test]
        public void RecoveredCardCanDefendLaterInTheSameTurnAndRoundEndDoesNotDuplicateIt()
        {
            var catalog=BattlefieldTests.Catalog();var game=Setup(catalog,team:Team.Blue,defender:"sabina");
            Apply(game,0,CommandKind.DebugTeleport,"hero:1",cell:new Hex(8,-9));
            string[] cards={Loyal,"sabina-01-拔枪","brogan-06-铜墙铁壁","arien-07-潮水"};
            for(int i=0;i<4;i++) Apply(game,i,CommandKind.SelectCard,cards[i]);
            Apply(game,0,CommandKind.BeginPrimary);Apply(game,0,Recover,Red);
            Apply(game,1,CommandKind.BeginPrimary);Apply(game,1,CommandKind.ChooseAttackTarget,"hero:0");
            Assert.That(game.View(0).DefenseOptions.Any(d=>d.CardId==Red && d.Assessment.Successful),Is.True);
            game=LocalGameFactory.Restore(catalog,game.ExportSave());Apply(game,0,CommandKind.Defend,Red);
            Assert.That(game.View(0).Players[0].AwaitingRespawn,Is.False);Assert.That(game.View(0).OwnCards.Single(c=>c.CardId==Red).Zone,Is.EqualTo(CardZone.Discarded));
            Apply(game,0,CommandKind.DebugAdvance,"round");Apply(game,0,CommandKind.ResolveRoundEnd);
            for(int step=0;step<20 && game.View(null).Phase!=Phase.Planning;step++)
            {
                int seat=game.View(null).Pending!.ChooserSeat;
                if(game.View(null).Pending!.Kind=="round_minion_removal") Apply(game,seat,CommandKind.ChooseRoundMinionRemoval,game.View(seat).RoundMinionRemovals.First());
                else { var choice=game.View(seat).SpawnChoices.First(p=>p.Value.Count>0);Apply(game,seat,CommandKind.ChooseMinionSpawn,choice.Key,cell:choice.Value.First()); }
            }
            Assert.That(game.View(null).Round,Is.EqualTo(2));Assert.That(game.View(0).OwnCards.Count,Is.EqualTo(5));
            Assert.That(game.View(0).OwnCards.All(c=>c.Zone==CardZone.InHand),Is.True);
            Assert.That(game.View(0).Events.Count(e=>e.Kind=="CardRecovered" && e.CardId==Red),Is.EqualTo(1));
            Assert.That(LocalGameFactory.Restore(catalog,game.ExportSave()).ExportSave(),Is.EqualTo(game.ExportSave()));
        }
        [Test]
        public void BarrierDiscardFromQuickThrustCanBeRecoveredOnTheFollowingTurn()
        {
            var catalog=BattlefieldTests.Catalog();var game=Setup(catalog,discard:"");
            Apply(game,0,CommandKind.DebugEquipCard,"shargatha-02-快速突刺",target:0);
            Apply(game,0,CommandKind.DebugEquipCard,"wasp-10-反射屏障",target:1);
            Apply(game,0,CommandKind.DebugTeleport,"hero:1",cell:new Hex(8,-9));
            string[] cards={"shargatha-02-快速突刺","wasp-06-静电封锁","brogan-06-铜墙铁壁","arien-07-潮水"};
            for(int i=0;i<4;i++) Apply(game,i,CommandKind.SelectCard,cards[i]);
            Apply(game,1,CommandKind.Pass);Apply(game,0,CommandKind.BeginPrimary);Apply(game,0,CommandKind.ChooseAttackTarget,"hero:1");
            Apply(game,1,CommandKind.Defend,"wasp-10-反射屏障");Apply(game,0,CommandKind.ForcedDiscard,"shargatha-00-反击");
            Apply(game,0,CommandKind.DebugAdvance,"turn");Assert.That(game.View(null).Effects,Is.Empty);
            for(int i=0;i<4;i++) Apply(game,i,CommandKind.SelectCard,i==0 ? Loyal : game.View(i).OwnCards.Where(c=>c.Zone==CardZone.InHand).OrderBy(c=>catalog.Card(c.CardId).Initiative).First().CardId);
            Apply(game,0,CommandKind.BeginPrimary);game=LocalGameFactory.Restore(catalog,game.ExportSave());
            Assert.That(game.View(0).RecoverableCards,Is.EqualTo(new[]{"shargatha-00-反击"}));
            Apply(game,0,Recover,"shargatha-00-反击");Assert.That(game.View(0).OwnCards.Single(c=>c.CardId=="shargatha-00-反击").Zone,Is.EqualTo(CardZone.InHand));
            Assert.That(game.View(1).OwnCards.Single(c=>c.CardId=="wasp-10-反射屏障").Zone,Is.EqualTo(CardZone.Discarded));
        }
        [Test]
        public void SilenceBlocksRecoveryWithoutConsumingTheSkill()
        {
            var catalog=BattlefieldTests.Catalog();var game=Setup(catalog,defender:"arien");Apply(game,0,CommandKind.DebugTeleport,"hero:1",cell:new Hex(8,-9));
            string[] cards={Loyal,"arien-06-打断施法","brogan-06-铜墙铁壁","wasp-07-抵挡屏障"};
            for(int i=0;i<4;i++) Apply(game,i,CommandKind.SelectCard,cards[i]);Apply(game,1,CommandKind.BeginPrimary);
            Assert.That(game.View(0).CanBeginPrimary,Is.False);string before=game.ExportSave();
            Assert.That(game.Execute(0,Cmd(game,0,CommandKind.BeginPrimary)).Code,Is.EqualTo("primary_restricted"));Assert.That(game.ExportSave(),Is.EqualTo(before));
            Apply(game,0,CommandKind.Pass);
        }
        [Test]
        public void SafePlanningUpgradeEnablesRecoveryButSelectedCardsCannotUpgrade()
        {
            var catalog=BattlefieldTests.Catalog();var game=Setup(catalog,engine:11);
            Assert.That(game.View(0).SupportedPrimaryCards,Does.Not.Contain(Loyal));Apply(game,0,CommandKind.UpgradeEngine,"12");
            Select(game);Apply(game,0,CommandKind.BeginPrimary);Apply(game,0,Recover,Red);
            Assert.That(LocalGameFactory.Restore(catalog,game.ExportSave()).ExportSave(),Is.EqualTo(game.ExportSave()));
            game=Setup(catalog,engine:11);Apply(game,0,CommandKind.SelectCard,Loyal);string before=game.ExportSave();
            Assert.That(game.Execute(0,Cmd(game,0,CommandKind.UpgradeEngine,"12")).Code,Is.EqualTo("invalid_engine_upgrade"));Assert.That(game.ExportSave(),Is.EqualTo(before));
        }
        [Test]
        public void FrozenEngineElevenResumesTextMoveWithoutAcquiringRecovery()
        {
            var catalog=BattlefieldTests.Catalog();var game=LocalGameFactory.Restore(catalog,File.ReadAllText(Path.Combine(ContentTests.Root(),"tests/fixtures/engine11-sneak-choice.json")));
            Assert.That(game.View(0).EngineVersion,Is.EqualTo(11));Assert.That(game.View(0).SupportedPrimaryCards,Does.Not.Contain(Loyal));
            Apply(game,0,CommandKind.ChooseEffectMove,"skip");Assert.That(LocalGameFactory.Restore(catalog,game.ExportSave()).ExportSave(),Is.EqualTo(game.ExportSave()));
        }
    }
}

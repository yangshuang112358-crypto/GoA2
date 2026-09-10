using System.IO;
using System.Linq;
using Goa2.Domain;
using Goa2.Infrastructure;
using NUnit.Framework;

namespace Goa2.Tests
{
    public sealed class LegacySaveTests
    {
        [Test]
        public void EngineFiveKeepsItsPendingConditionalBonusWithoutNewCountSources()
        {
            var catalog=ContentLoader.LoadDirectory(ContentTests.Root());
            string json=File.ReadAllText(Path.Combine(ContentTests.Root(),"tests","fixtures","engine5-headshot-pending.json"));
            Assert.That(json, Does.Not.Contain("CardTextReason").And.Not.Contain("CardTextSourceUnits"));
            var game=LocalGameFactory.Restore(catalog,json);
            Assert.That(game.View(null).EngineVersion, Is.EqualTo(5)); Assert.That(game.View(null).Pending!.Kind, Is.EqualTo("defense"));
            Assert.That(game.View(null).Attack!.CardTextBonus, Is.EqualTo(2)); Assert.That(game.View(null).Attack!.FinalAttack, Is.EqualTo(6));
            Assert.That(game.View(null).Attack!.CardTextSourceUnits, Is.Empty); Assert.That(game.View(null).Attack!.CardTextReason, Is.Empty);
            Assert.That(game.ExportSave(), Is.EqualTo(new JsonStateCodec().Write(new JsonStateCodec().Read(json))));
            Assert.That(game.View(null).SupportedPrimaryCards, Does.Not.Contain("shargatha-01-劈砍"));
            TurnFlowTests.Apply(game,1,CommandKind.Defend,"wasp-01-电击");
            Assert.That(game.View(null).RedCrystal, Is.EqualTo(7)); Assert.That(game.View(null).Events.Count(e => e.Kind=="AttackResolved"), Is.EqualTo(1));
            TurnFlowTests.Apply(game,0,CommandKind.DebugAdvance,"turn");
            TurnFlowTests.Apply(game,0,CommandKind.UpgradeEngine,GameState.CurrentEngineVersion.ToString());
            Assert.That(game.View(null).SupportedPrimaryCards, Does.Contain("shargatha-01-劈砍"));
            Assert.That(new JsonStateCodec().Read(game.ExportSave()).InitialEngineVersion, Is.EqualTo(5));
            Assert.That(LocalGameFactory.Restore(catalog,game.ExportSave()).ExportSave(), Is.EqualTo(game.ExportSave()));
        }
        [Test]
        public void EngineFourCounterKeepsItsPrivateSourceAndFinishesTheOriginalAttackOnce()
        {
            var catalog=ContentLoader.LoadDirectory(ContentTests.Root());
            string json=File.ReadAllText(Path.Combine(ContentTests.Root(),"tests","fixtures","engine4-barrier-pending.json"));
            Assert.That(json, Does.Not.Contain("CardTextBonus"));
            var game=LocalGameFactory.Restore(catalog,json);
            Assert.That(game.View(null).EngineVersion, Is.EqualTo(4)); Assert.That(game.View(null).Pending!.Kind, Is.EqualTo("forced_discard"));
            Assert.That(game.View(0).Pending!.Source, Is.Empty); Assert.That(game.View(1).Pending!.Source, Is.EqualTo("wasp-10-反射屏障"));
            Assert.That(game.View(0).Attack!.CardTextBonus, Is.Zero);
            Assert.That(game.View(null).SupportedPrimaryCards, Does.Not.Contain("sabina-03-神枪手"));
            TurnFlowTests.Apply(game,0,CommandKind.ForcedDiscard,"sabina-00-近身射击");
            Assert.That(game.View(null).Effects.Count, Is.EqualTo(1)); Assert.That(game.View(null).Events.Count(e => e.Kind=="AttackResolved"), Is.EqualTo(1));
            Assert.That(game.View(null).Effects.Single().SourceCardId, Is.Empty); Assert.That(game.View(1).Effects.Single().SourceCardId, Is.EqualTo("wasp-10-反射屏障"));
            Assert.That(LocalGameFactory.Restore(catalog,game.ExportSave()).ExportSave(), Is.EqualTo(game.ExportSave()));
        }
        [Test]
        public void EngineTwoPurpleOwnerKeepsItsHistoricalSkillWithoutNewAfterTriggers()
        {
            var catalog=ContentLoader.LoadDirectory(ContentTests.Root());
            string json=File.ReadAllText(Path.Combine(ContentTests.Root(),"tests","fixtures","engine2-purple-aura.json"));
            Assert.That(json, Does.Not.Contain("AreaKind"));
            var game=LocalGameFactory.Restore(catalog,json); var view=game.View(null);
            Assert.That(view.EngineVersion, Is.EqualTo(2)); Assert.That(view.Revision, Is.EqualTo(15));
            Assert.That(view.Players[0].PurpleCardId, Is.EqualTo("wasp-12-电闪雷鸣"));
            Assert.That(view.Effects.Single().AreaKind, Is.EqualTo(EffectAreaKind.SkillRange)); Assert.That(view.Pending, Is.Null);
            Assert.That(view.SupportedPrimaryCards, Does.Not.Contain("wasp-00-闪耀之刃"));
            Assert.That(game.ExportSave(), Is.EqualTo(new JsonStateCodec().Write(new JsonStateCodec().Read(json))));
            TurnFlowTests.Apply(game,0,CommandKind.DebugAdvance,"turn");
            TurnFlowTests.Apply(game,0,CommandKind.UpgradeEngine,GameState.CurrentEngineVersion.ToString());
            Assert.That(game.View(null).SupportedPrimaryCards, Does.Contain("wasp-00-闪耀之刃"));
            Assert.That(new JsonStateCodec().Read(game.ExportSave()).InitialEngineVersion, Is.EqualTo(2));
            Assert.That(LocalGameFactory.Restore(catalog,game.ExportSave()).ExportSave(), Is.EqualTo(game.ExportSave()));
        }
        [Test]
        public void FormalLegacyRoundEndCanContinueThroughANewJournaledSettlementCommand()
        {
            var catalog=ContentLoader.LoadDirectory(ContentTests.Root());
            var game=LocalGameFactory.Restore(catalog,File.ReadAllText(Path.Combine(ContentTests.Root(),"tests","fixtures","legacy-v1-roundend.json")));
            Assert.That(game.View(null).Sandbox, Is.False);
            TurnFlowTests.Apply(game,0,CommandKind.ResolveRoundEnd);
            Assert.That(game.View(null).Round, Is.EqualTo(2)); Assert.That(game.View(null).Phase, Is.EqualTo(Phase.Planning));
            Assert.That(game.View(null).Players.All(p => p.HandCount==5 && p.Gold==1), Is.True);
            Assert.That(new JsonStateCodec().Read(game.ExportSave()).AcceptedCommands.Count, Is.EqualTo(60));
            Assert.That(LocalGameFactory.Restore(catalog,game.ExportSave()).ExportSave(), Is.EqualTo(game.ExportSave()));
        }
        [Test]
        public void BatchOneRoundEndJournalStillRestoresWithNewFieldsDefaulted()
        {
            string root = ContentTests.Root();
            string json = File.ReadAllText(Path.Combine(root, "tests", "fixtures", "legacy-v1-roundend.json"));
            Assert.That(json, Does.Not.Contain("Sandbox"));
            Assert.That(json, Does.Not.Contain("BlueMarks"));
            var game = LocalGameFactory.Restore(ContentLoader.LoadDirectory(root), json);
            var state = new JsonStateCodec().Read(game.ExportSave());
            Assert.That(state.Phase, Is.EqualTo(Phase.RoundEnd)); Assert.That(state.Revision, Is.EqualTo(59));
            Assert.That(state.AcceptedCommands.Count, Is.EqualTo(59)); Assert.That(state.Sandbox, Is.False);
            Assert.That(state.BlueMarks, Is.Zero); Assert.That(state.Frontline, Is.Null); Assert.That(state.Winner, Is.Null);
            Assert.That(state.Events.Last().Detail, Is.EqualTo("本切片到达轮末；轮末结算与升级尚未实装。"));
            Assert.That(game.ExportSave(), Is.EqualTo(new JsonStateCodec().Write(new JsonStateCodec().Read(json))));
        }
    }
}

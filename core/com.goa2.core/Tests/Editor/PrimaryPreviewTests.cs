using System.Linq;
using System.IO;
using Goa2.Domain;
using Goa2.Infrastructure;
using Goa2.Infrastructure.Scenarios;
using NUnit.Framework;
using static Goa2.Tests.SessionTests;
using static Goa2.Tests.TurnFlowTests;

namespace Goa2.Tests
{
    public sealed class PrimaryPreviewTests
    {
        [TestCase("tidal-place","placement",CommandKind.CommitPrimaryPlacement)]
        [TestCase("charge-hero","effect_move",CommandKind.CommitPrimaryMove)]
        public void MapPreviewConfirmsFirstStepAtomicallyAndReplays(string fixture,string kind,CommandKind command)
        {
            var catalog=BattlefieldTests.Catalog();var definition=ScenarioRunner.Load(File.ReadAllText(Path.Combine(ContentTests.Root(),"tests/scenarios/"+fixture+".json")));
            var runner=new ScenarioRunner(catalog,definition);
            foreach(var step in definition.Steps){if(step.Command=="BeginPrimary")break;Assert.That(runner.Next().Passed,Is.True);}
            var game=runner.Session;int actor=game.View(null).ActiveSeat!.Value;string before=game.ExportSave();var preview=game.View(actor).PrimaryPreview;
            Assert.That(preview,Is.Not.Null);Assert.That(preview!.Kind,Is.EqualTo(kind));Assert.That(preview.Cells,Is.Not.Empty);Assert.That(game.ExportSave(),Is.EqualTo(before));
            Assert.That(game.Execute(actor,Cmd(game,actor,command,destination:new Hex(999,999))).Accepted,Is.False);Assert.That(game.ExportSave(),Is.EqualTo(before));
            Apply(game,actor,command,cell:preview.Cells.First());string save=game.ExportSave();Assert.That(LocalGameFactory.Restore(catalog,save).ExportSave(),Is.EqualTo(save));
        }
        [Test] public void PreviewIsReadOnlyPrivateAndCannotMutateAuthority()
        {
            var game=CombatFlowTests.Duel(BattlefieldTests.Catalog());string before=game.ExportSave();
            var view=game.View(0);Assert.That(view.PrimaryPreview,Is.Not.Null);Assert.That(view.PrimaryPreview!.Targets,Does.Contain("hero:1"));
            Assert.That(game.View(1).PrimaryPreview,Is.Null);Assert.That(game.View(null).PrimaryPreview,Is.Null);
            view.PrimaryPreview.Targets.Clear();Assert.That(game.ExportSave(),Is.EqualTo(before));Assert.That(game.View(0).PrimaryPreview!.Targets,Does.Contain("hero:1"));
        }
        [Test] public void ConfirmCommitsOnceAndRestoresFromReplay()
        {
            var catalog=BattlefieldTests.Catalog();var game=CombatFlowTests.Duel(catalog);long revision=game.View(0).Revision;
            var command=Cmd(game,0,CommandKind.CommitPrimaryAttack,"hero:1");Assert.That(game.Execute(0,command).Accepted,Is.True);
            Assert.That(game.View(1).Pending!.Kind,Is.EqualTo("defense"));Assert.That(game.View(1).Revision,Is.EqualTo(revision+1));
            string save=game.ExportSave();Assert.That(game.Execute(0,command).Duplicate,Is.True);Assert.That(game.ExportSave(),Is.EqualTo(save));
            Assert.That(LocalGameFactory.Restore(catalog,save).ExportSave(),Is.EqualTo(save));
        }
        [Test] public void BadTargetStaleRevisionAndWrongActorDoNotStartAction()
        {
            var game=CombatFlowTests.Duel(BattlefieldTests.Catalog());string before=game.ExportSave();
            Assert.That(game.Execute(0,Cmd(game,0,CommandKind.CommitPrimaryAttack,"missing")).Accepted,Is.False);
            Assert.That(game.Execute(1,Cmd(game,1,CommandKind.CommitPrimaryAttack,"hero:0")).Accepted,Is.False);
            var stale=Cmd(game,0,CommandKind.CommitPrimaryAttack,"hero:1");stale.ExpectedRevision--;Assert.That(game.Execute(0,stale).Code,Is.EqualTo("stale_revision"));
            Assert.That(game.ExportSave(),Is.EqualTo(before));Assert.That(game.View(0).Phase,Is.EqualTo(Phase.Action));
        }
        [Test] public void OptionalDiscardPreludeCannotBeBypassedWithAtomicAttack()
        {
            var catalog=BattlefieldTests.Catalog();var game=LocalGameFactory.Create(catalog,"prelude",new[]{"A","B","C","D"},42,true);
            Apply(game,0,CommandKind.DebugPrepare,"brogan,wasp,shargatha,arien");
            Apply(game,0,CommandKind.DebugEquipCard,"brogan-02-投掷飞斧",target:0);
            Apply(game,0,CommandKind.SelectCard,"brogan-02-投掷飞斧");Apply(game,1,CommandKind.SelectCard,"wasp-01-电击");
            Apply(game,2,CommandKind.SelectCard,"shargatha-06-海妖之歌");Apply(game,3,CommandKind.SelectCard,"arien-07-潮水");Apply(game,1,CommandKind.Pass);
            Assert.That(game.View(0).PrimaryPreview,Is.Null);string before=game.ExportSave();
            Assert.That(game.Execute(0,Cmd(game,0,CommandKind.CommitPrimaryAttack,"hero:1")).Code,Is.EqualTo("primary_preview_changed"));
            Assert.That(game.ExportSave(),Is.EqualTo(before));Apply(game,0,CommandKind.BeginPrimary);
            Assert.That(game.View(0).Pending!.Kind,Is.EqualTo("optional_discard"));
        }
    }
}

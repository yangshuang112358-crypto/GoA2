using System.IO;
using System.Linq;
using Goa2.Domain;
using Goa2.Infrastructure;
using NUnit.Framework;
using static Goa2.Tests.SessionTests;
using static Goa2.Tests.TurnFlowTests;

namespace Goa2.Tests
{
    public sealed class EngineUpgradeTests
    {
        [Test]
        public void QuietPlanningUpgradeKeepsTheInitialVersionAndReplaysAtTheRecordedBoundary()
        {
            var catalog=BattlefieldTests.Catalog(); var game=BattlefieldTests.Ready(catalog,1);
            var command=Cmd(game,0,CommandKind.UpgradeEngine,"2");
            Assert.That(game.Execute(0,command).Accepted, Is.True); Assert.That(game.Execute(0,command).Duplicate, Is.True);
            var state=new JsonStateCodec().Read(game.ExportSave());
            Assert.That(state.InitialEngineVersion, Is.EqualTo(1)); Assert.That(state.EngineVersion, Is.EqualTo(2));
            Assert.That(state.Events.Count(e => e.Kind=="EngineUpgraded"), Is.EqualTo(1));
            Assert.That(LocalGameFactory.Restore(catalog,game.ExportSave()).ExportSave(), Is.EqualTo(game.ExportSave()));
            string before=game.ExportSave();
            Assert.That(game.Execute(0,Cmd(game,0,CommandKind.UpgradeEngine,"1")).Code, Is.EqualTo("invalid_engine_upgrade"));
            Assert.That(game.Execute(0,Cmd(game,0,CommandKind.UpgradeEngine,"2")).Code, Is.EqualTo("invalid_engine_upgrade"));
            Assert.That(game.Execute(0,Cmd(game,0,CommandKind.UpgradeEngine,"999")).Code, Is.EqualTo("invalid_engine_upgrade"));
            Assert.That(game.ExportSave(), Is.EqualTo(before));
        }
        [Test]
        public void SelectedCardsOrOngoingActionsPreventVersionSwitchingWithoutMutation()
        {
            var catalog=BattlefieldTests.Catalog(); var game=BattlefieldTests.Ready(catalog,1);
            Apply(game,0,CommandKind.SelectCard,"wasp-01-电击");
            string before=game.ExportSave();
            Assert.That(game.Execute(0,Cmd(game,0,CommandKind.UpgradeEngine,"2")).Code, Is.EqualTo("invalid_engine_upgrade"));
            Assert.That(game.ExportSave(), Is.EqualTo(before));
            Apply(game,0,CommandKind.DebugSelectAll);
            before=game.ExportSave();
            Assert.That(game.Execute(0,Cmd(game,0,CommandKind.UpgradeEngine,"2")).Code, Is.EqualTo("invalid_engine_upgrade"));
            Assert.That(game.ExportSave(), Is.EqualTo(before));
        }
        [Test]
        public void AFormalLegacyMatchCanExplicitlyAdoptTheCurrentRulesAfterItsRound()
        {
            var catalog=BattlefieldTests.Catalog();
            var game=LocalGameFactory.Restore(catalog,File.ReadAllText(Path.Combine(ContentTests.Root(),"tests","fixtures","legacy-v1-roundend.json")));
            Apply(game,0,CommandKind.ResolveRoundEnd); Apply(game,0,CommandKind.UpgradeEngine,"2");
            var state=new JsonStateCodec().Read(game.ExportSave());
            Assert.That(state.Sandbox, Is.False); Assert.That(state.InitialEngineVersion, Is.EqualTo(0)); Assert.That(state.EngineVersion, Is.EqualTo(2));
            Assert.That(state.AcceptedCommands.Count, Is.EqualTo(61));
            Assert.That(LocalGameFactory.Restore(catalog,game.ExportSave()).ExportSave(), Is.EqualTo(game.ExportSave()));
        }
    }
}

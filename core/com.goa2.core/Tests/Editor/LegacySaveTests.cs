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

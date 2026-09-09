using System;
using System.IO;
using System.Linq;
using System.Text;
using Goa2.Infrastructure;
using NUnit.Framework;

namespace Goa2.Tests
{
    public sealed class ContentTests
    {
        internal static string Root()
        {
            var directory = new DirectoryInfo(TestContext.CurrentContext.TestDirectory);
            while (directory != null && !File.Exists(Path.Combine(directory.FullName, "content", "manifest.json"))) directory = directory.Parent;
            return directory?.FullName ?? throw new InvalidOperationException("Repository content not found");
        }
        [Test]
        public void ImportPreservesAllFormalDataAndAsymmetricSpawnCounts()
        {
            var content = ContentLoader.LoadDirectory(Root());
            Assert.That(content.Heroes.Count, Is.EqualTo(6));
            Assert.That(content.Cards.Count, Is.EqualTo(108));
            Assert.That(content.Cells.Count, Is.EqualTo(254));
            Assert.That(content.Cells.Count(c => c.Obstacle), Is.EqualTo(44));
            Assert.That(content.Cells.Count(c => c.Region == "redNear" && c.Spawn.StartsWith("blue") && c.Spawn.EndsWith("Spawn")), Is.EqualTo(5));
            Assert.That(content.Cells.Count(c => c.Region == "redNear" && c.Spawn.StartsWith("red") && c.Spawn.EndsWith("Spawn")), Is.EqualTo(6));
            Assert.That(content.Cards.First(c => c.Id == "wasp-01-电击").SecondaryMovement, Is.EqualTo(4));
            Assert.That(content.Cards.First(c => c.Id == "wasp-06-静电封锁").SecondaryMovement, Is.Null);
        }
        [Test]
        public void ModifiedContentBytesAreRejectedBeforeImport()
        {
            Assert.Throws<InvalidDataException>(() => ContentLoader.Load(path =>
            {
                var bytes = File.ReadAllBytes(Path.Combine(Root(), path));
                return path.EndsWith("cards.json") ? bytes.Concat(new byte[] { 32 }).ToArray() : bytes;
            }));
        }
        [Test]
        public void ManifestCannotReadOutsideFixedCanonicalFiles()
        {
            bool illegalRead = false;
            Assert.Throws<InvalidDataException>(() => ContentLoader.Load(path =>
            {
                if (path.Contains("..")) { illegalRead = true; throw new Exception("Traversal"); }
                var bytes = File.ReadAllBytes(Path.Combine(Root(), path));
                return path == "content/manifest.json" ? Encoding.UTF8.GetBytes(Encoding.UTF8.GetString(bytes).Replace("content/canonical/cards.json", "../outside.json")) : bytes;
            }));
            Assert.That(illegalRead, Is.False);
        }
    }
}

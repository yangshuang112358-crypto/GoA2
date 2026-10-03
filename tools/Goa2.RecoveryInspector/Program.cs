using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using Goa2.Domain;
using Goa2.Infrastructure;
using Newtonsoft.Json;

namespace Goa2.RecoveryInspector
{
    // Offline, read-only inspection. Does not start a room or output private cards/credentials.
    internal static class Program
    {
        private static int Main(string[] args)
        {
            if (args.Length != 2)
            {
                Console.Error.WriteLine("Usage: Goa2.RecoveryInspector <content-root> <authority-save.json>");
                return 2;
            }
            try
            {
                string path = Path.GetFullPath(args[1]);
                var info = new FileInfo(path);
                if (!info.Exists || info.Length < 2 || info.Length > 32 * 1024 * 1024)
                    throw new InvalidDataException("invalid size");
                byte[] bytes = File.ReadAllBytes(path);
                string text = new UTF8Encoding(false, true).GetString(bytes).TrimStart('\uFEFF');
                var restored = LocalGameFactory.Restore(ContentLoader.LoadDirectory(Path.GetFullPath(args[0])), text);
                var view = restored.View(null);
                using var sha = SHA256.Create();
                Console.WriteLine(JsonConvert.SerializeObject(new {
                    passed = true, method = "LocalGameFactory.Restore full replay and state equality",
                    sha256 = BitConverter.ToString(sha.ComputeHash(bytes)).Replace("-", "").ToLowerInvariant(),
                    view.Revision, view.EngineVersion, view.Round, view.Turn,
                    phase = view.Phase.ToString(), pendingDecision = view.Pending != null,
                    roomStarted = false, sourceModified = false
                }));
                return 0;
            }
            catch (Exception error) when (error is RuleViolation || error is IOException || error is InvalidDataException ||
                error is UnauthorizedAccessException || error is DecoderFallbackException || error is ArgumentException)
            {
                // Exception text may quote file paths or game data; never print it by default.
                Console.WriteLine(JsonConvert.SerializeObject(new { passed = false,
                    code = error is RuleViolation rule ? rule.Code : "unreadable_or_invalid_save",
                    roomStarted = false, sourceModified = false }));
                return 1;
            }
        }
    }
}

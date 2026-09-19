using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using Goa2.Domain;
using Goa2.Infrastructure;
using Goa2.Infrastructure.Scenarios;
using Newtonsoft.Json;

namespace Goa2.ScenarioCli
{
    internal static class Program
    {
        private static int Main(string[] args)
        {
            if (args.Length != 3)
            {
                Console.Error.WriteLine("Usage: Goa2.ScenarioCli <project-root> <scenario.json> <new-output-directory>");
                return 2;
            }
            string output = Path.GetFullPath(args[2]);
            try
            {
                // Never replace a previous report or an input with a new run.
                if (Directory.Exists(output) || File.Exists(output)) throw new IOException("Output must be a new directory.");
                string input = Path.GetFullPath(args[1]);
                if (new FileInfo(input).Length > 2 * 1024 * 1024) throw new InvalidDataException("Scenario exceeds 2 MiB.");
                byte[] bytes = File.ReadAllBytes(input);
                var definition = ScenarioRunner.Load(new UTF8Encoding(false, true).GetString(bytes));
                var runner = new ScenarioRunner(ContentLoader.LoadDirectory(Path.GetFullPath(args[0])), definition);
                while (!runner.Complete) runner.Next();
                Directory.CreateDirectory(output);
                File.WriteAllText(Path.Combine(output, "report.json.save.json"), runner.Session.ExportSave(), new UTF8Encoding(false));
                Write(output, "report.json", runner.Report);
                int exit = runner.Report.Passed ? 0 : 1;
                Write(output, "run.json", new
                {
                    runner = "Dotnet CLI", engine = GameState.CurrentEngineVersion,
                    input, inputSha256 = Hash(bytes), exitCode = exit,
                    finishedUtc = DateTime.UtcNow.ToString("o"),
                    assemblies = Directory.GetFiles(AppContext.BaseDirectory, "Goa2.*.dll")
                        .OrderBy(p => p, StringComparer.Ordinal)
                        .ToDictionary(p => Path.GetFileName(p), p => Hash(File.ReadAllBytes(p))),
                    unityPlayerTested = false, uiTested = false
                });
                Console.WriteLine((exit == 0 ? "PASS " : "FAIL ") + runner.Report.Id + " " + output);
                return exit;
            }
            catch (Exception error)
            {
                Console.Error.WriteLine(error.Message);
                return 2;
            }
        }
        private static string Hash(byte[] bytes)
        {
            using var sha = SHA256.Create();
            return BitConverter.ToString(sha.ComputeHash(bytes)).Replace("-", "").ToLowerInvariant();
        }
        private static void Write(string output, string name, object value) =>
            File.WriteAllText(Path.Combine(output, name), JsonConvert.SerializeObject(value, Formatting.Indented), new UTF8Encoding(false));
    }
}

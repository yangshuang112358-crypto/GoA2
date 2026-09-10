#nullable enable
using System;
using System.Globalization;
using System.IO;
using System.Text;
using Goa2.Domain;
using Goa2.Infrastructure;
using Goa2.Infrastructure.Scenarios;
using Newtonsoft.Json;
using UnityEngine;
using UnityApplication = UnityEngine.Application;

namespace Goa2.Presentation
{
    public static class ScenarioPlayer
    {
        public static string? Option(string[] args, string key)
        {
            int index = Array.IndexOf(args, key);
            return index >= 0 && index+1 < args.Length ? args[index+1] : null;
        }
        public static ScenarioRunner Create(string[] args, ContentCatalog catalog)
        {
            string path = Option(args, "-goaScenario") ?? throw new InvalidDataException("缺少场景文件路径。");
            if (new FileInfo(path).Length > 2*1024*1024) throw new InvalidDataException("场景文件超过2 MiB。");
            return new ScenarioRunner(catalog, ScenarioRunner.Load(File.ReadAllText(path, new UTF8Encoding(false,true))));
        }
        public static float Delay(string[] args)
        {
            string? value = Option(args, "-goaScenarioDelay");
            if (value == null) return .75f;
            if (!float.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out float delay) || float.IsNaN(delay) || delay < 0 || delay > 10)
                throw new InvalidDataException("场景延迟须在0至10秒之间。");
            return delay;
        }
        public static string ReportPath(string[] args) => Path.GetFullPath(Option(args, "-goaScenarioReport") ?? Path.Combine(UnityApplication.persistentDataPath, "scenarios", "latest.json"));
        private static void WriteAtomic(string path, string value)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path+".tmp", value, new UTF8Encoding(false));
            if (File.Exists(path)) File.Replace(path+".tmp", path, null); else File.Move(path+".tmp", path);
        }
        public static void WriteReport(string[] args, ScenarioRunner runner)
        {
            string path = ReportPath(args);
            WriteAtomic(path+".save.json", runner.Session.ExportSave());
            WriteAtomic(path, JsonConvert.SerializeObject(runner.Report, Formatting.Indented));
        }
        public static void WriteFailure(string[] args, Exception error)
        {
            try { WriteAtomic(ReportPath(args), JsonConvert.SerializeObject(new { Complete = true, Passed = false, Failure = error.Message, TotalSteps = 0 }, Formatting.Indented)); }
            catch (Exception writeError) { Debug.LogError("场景报告无法写入："+writeError.Message); }
        }
        public static void RunHeadless(string[] args)
        {
            try
            {
                var catalog = ContentLoader.LoadDirectory(Path.Combine(UnityApplication.streamingAssetsPath, "Goa2"));
                var runner = Create(args, catalog);
                while (!runner.Complete) runner.Next();
                WriteReport(args, runner);
                Debug.Log((runner.Report.Passed ? "GOA2_SCENARIO_PASS " : "GOA2_SCENARIO_FAIL ")+runner.Report.Id+" "+runner.Report.FinalStateHash);
                UnityApplication.Quit(runner.Report.Passed ? 0 : 1);
            }
            catch (Exception error)
            {
                WriteFailure(args,error); Debug.LogError("GOA2_SCENARIO_FAIL "+error.Message); UnityApplication.Quit(2);
            }
        }
    }
}

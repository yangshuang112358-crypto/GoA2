#nullable enable
using System;
using System.Collections;
using System.IO;
using System.Linq;
using Goa2.Infrastructure.Scenarios;
using UnityEngine;
using UnityEngine.UIElements;
using UnityApplication = UnityEngine.Application;

namespace Goa2.Presentation
{
    public sealed partial class GameScreen
    {
        private ScenarioRunner? scenario;
        private string[] scenarioArguments = Array.Empty<string>();
        private bool scenarioPaused, scenarioStepRequested, scenarioReported;
        private float scenarioDelay = .75f;
        private bool ScenarioRunning => scenario != null;
        private void SetupScenario(string[] arguments)
        {
            scenarioArguments = arguments;
            scenario = ScenarioPlayer.Create(arguments, catalog);
            scenarioDelay = ScenarioPlayer.Delay(arguments);
            scenarioPaused = arguments.Contains("-goaScenarioPaused");
            scenarioReported = false; scenarioStepRequested = false;
            customSavePath ??= Path.Combine(Path.GetDirectoryName(ScenarioPlayer.ReportPath(arguments))!, "manual-save.json");
            session = scenario.Session; seat = 0; notice = "已载入自动测试场景。";
            Render(); StartCoroutine(PlayScenario(scenario));
        }
        private IEnumerator PlayScenario(ScenarioRunner runner)
        {
            float elapsed = 0;
            while (!runner.Complete && ReferenceEquals(scenario,runner))
            {
                if (scenarioPaused && !scenarioStepRequested) { yield return null; continue; }
                elapsed += Time.unscaledDeltaTime;
                if (!scenarioStepRequested && elapsed < scenarioDelay) { yield return null; continue; }
                elapsed = 0; scenarioStepRequested = false;
                var result = runner.Next(); session = runner.Session;
                int actor = result.Command?.ActorSeat ?? 0;
                seat = actor >= 0 && actor < 4 ? actor : 0;
                notice = result.Passed ? "步骤 "+result.Number+"通过 · "+result.Name : "场景失败 · "+string.Join("；", result.Errors);
                ClearPending(); Render(); yield return null;
            }
            if (!ReferenceEquals(scenario,runner)) yield break;
            try
            {
                ScenarioPlayer.WriteReport(scenarioArguments,runner); scenarioReported = true;
                Debug.Log((runner.Report.Passed ? "GOA2_SCENARIO_PASS " : "GOA2_SCENARIO_FAIL ")+runner.Report.Id+" "+runner.Report.FinalStateHash);
            }
            catch (Exception error) { notice = "报告写入失败："+error.Message; Debug.LogException(error); }
            Render();
            if (scenarioArguments.Contains("-goaScenarioQuit"))
            {
                yield return new WaitForSecondsRealtime(1.5f);
                UnityApplication.Quit(scenarioReported && runner.Report.Passed ? 0 : 1);
            }
        }
        private void BuildScenarioBar(VisualElement parent)
        {
            if (scenario == null) return;
            var bar = Box("scenario-bar"); parent.Add(bar);
            string status = scenario.Complete ? scenario.Report.Passed ? "通过" : "失败" : scenarioPaused ? "已暂停" : "播放中";
            var label = Text("场景 · "+scenario.Report.Name+"  "+scenario.Report.Steps.Count+"/"+scenario.Report.TotalSteps+" · "+status, "scenario-info"); bar.Add(label);
            var pause = Button(scenarioPaused ? "继续场景" : "暂停场景", () => { scenarioPaused = !scenarioPaused; Render(); }, "compact-button", "scenario-pause");
            pause.SetEnabled(!scenario.Complete); bar.Add(pause);
            var step = Button("执行下一步", () => scenarioStepRequested = true, "compact-button", "scenario-step");
            step.SetEnabled(!scenario.Complete && scenarioPaused); bar.Add(step);
            if (scenario.Complete && !scenarioArguments.Contains("-goaScenarioQuit"))
            {
                var manual = Button("转为手工操作", () => { scenario = null; Render(); }, "compact-button", "scenario-finish");
                manual.SetEnabled(scenarioReported); bar.Add(manual);
            }
        }
        private void OnApplicationQuit()
        {
            if (scenario == null || scenarioReported) return;
            try { ScenarioPlayer.WriteReport(scenarioArguments,scenario); } catch (Exception error) { Debug.LogError(error.Message); }
        }
    }
}

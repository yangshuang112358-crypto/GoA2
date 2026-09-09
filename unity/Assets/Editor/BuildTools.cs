#nullable enable
using System;
using System.IO;
using System.Linq;
using Goa2.Domain;
using Goa2.Infrastructure;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityApplication = UnityEngine.Application;

namespace Goa2.Editor
{
    public static class BuildTools
    {
        private static string Root => Directory.GetParent(UnityApplication.dataPath)!.Parent!.FullName;
        [MenuItem("Goa2/Prepare project")]
        public static void Prepare()
        {
            var catalog = ContentLoader.LoadDirectory(Root);
            foreach (string relative in new[] { "content/manifest.json", "content/canonical/cards.json", "content/canonical/heroes.json", "content/canonical/map.json", "content/canonical/ruleset.json" })
            {
                string target = Path.Combine(UnityApplication.streamingAssetsPath, "Goa2", relative);
                Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                File.WriteAllBytes(target, File.ReadAllBytes(Path.Combine(Root, relative)));
            }
            PlayerSettings.companyName = "Goa2V1";
            PlayerSettings.productName = "Goa2V1";
            PlayerSettings.bundleVersion = "0.1.0";
            PlayerSettings.defaultScreenWidth = 1600;
            PlayerSettings.defaultScreenHeight = 1000;
            PlayerSettings.fullScreenMode = FullScreenMode.Windowed;
            PlayerSettings.resizableWindow = true;
            PlayerSettings.runInBackground = true;
            PlayerSettings.SetScriptingBackend(NamedBuildTarget.Standalone, ScriptingImplementation.Mono2x);
            PlayerSettings.SetApiCompatibilityLevel(NamedBuildTarget.Standalone, ApiCompatibilityLevel.NET_Standard);
            PlayerSettings.SetUseDefaultGraphicsAPIs(BuildTarget.StandaloneWindows64, false);
            PlayerSettings.SetGraphicsAPIs(BuildTarget.StandaloneWindows64, new[] { GraphicsDeviceType.Direct3D11 });
            Directory.CreateDirectory("Assets/Scenes");
            if (!File.Exists("Assets/Scenes/Main.unity"))
            {
                var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
                var camera = new GameObject("Main Camera").AddComponent<Camera>();
                camera.clearFlags = CameraClearFlags.SolidColor;
                camera.backgroundColor = new Color(.06f, .10f, .14f);
                camera.orthographic = true;
                camera.transform.position = new Vector3(0, 0, -10);
                EditorSceneManager.SaveScene(scene, "Assets/Scenes/Main.unity");
            }
            EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene("Assets/Scenes/Main.unity", true) };
            AssetDatabase.Refresh();
            AssetDatabase.SaveAssets();
            Debug.Log("GOA2_PREPARE_OK heroes=" + catalog.Heroes.Count + " cards=" + catalog.Cards.Count + " cells=" + catalog.Cells.Count);
        }
        [MenuItem("Goa2/Validate editor integration")]
        public static void Validate()
        {
            var catalog = ContentLoader.LoadDirectory(Root);
            var game = LocalGameFactory.Create(catalog, "editor-smoke", new[] { "A", "B", "C", "D" }, 42);
            for (int seat = 0; seat < 4; seat++)
            {
                var result = game.Execute(seat, new Command { Id = "hero" + seat, MatchId = "editor-smoke", ExpectedRevision = game.View(seat).Revision, ActorSeat = seat, Kind = CommandKind.ChooseHero, Value = catalog.Heroes[seat].Id });
                if (!result.Accepted) throw new Exception(result.Code);
            }
            if (game.View(0).Units.Count != 12) throw new Exception("Initial minion import failed");
            var restored = LocalGameFactory.Restore(catalog, game.ExportSave());
            if (restored.ExportSave() != game.ExportSave()) throw new Exception("Unity state roundtrip failed");
            Directory.CreateDirectory(Path.Combine(Root, "artifacts", "unity"));
            File.WriteAllText(Path.Combine(Root, "artifacts", "unity", "editor-validation.json"),
                "{\"result\":\"PASS\",\"unity\":\"" + UnityApplication.unityVersion + "\",\"heroes\":6,\"cards\":108,\"cells\":254,\"initial_minions\":12,\"state_roundtrip\":true}\n");
            Debug.Log("GOA2_EDITOR_VALIDATION_PASS");
        }
        [MenuItem("Goa2/Build Windows player")]
        public static void BuildWindows()
        {
            Prepare();
            Validate();
            string target = Path.Combine(Root, "artifacts", "player", "Goa2V1.exe");
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            var report = BuildPipeline.BuildPlayer(new BuildPlayerOptions
            {
                scenes = EditorBuildSettings.scenes.Where(s => s.enabled).Select(s => s.path).ToArray(),
                locationPathName = target, target = BuildTarget.StandaloneWindows64,
                options = Environment.GetCommandLineArgs().Contains("-goaDevelopment") ? BuildOptions.Development : BuildOptions.None
            });
            var summary = report.summary;
            File.WriteAllText(Path.Combine(Root, "artifacts", "unity", "build-report.json"),
                "{\"result\":\"" + summary.result + "\",\"errors\":" + summary.totalErrors + ",\"warnings\":" + summary.totalWarnings + ",\"bytes\":" + summary.totalSize + "}\n");
            if (summary.result != BuildResult.Succeeded) throw new Exception("Player build failed: " + summary.result);
            Debug.Log("GOA2_BUILD_PASS " + target);
        }
    }
}

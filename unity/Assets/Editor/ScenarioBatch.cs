#nullable enable
using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using Goa2.Domain;
using Goa2.Infrastructure;
using Goa2.Infrastructure.Scenarios;
using Newtonsoft.Json;
using UnityEditor;
using UnityEngine;
namespace Goa2.Editor
{
    public static class ScenarioBatch
    {
        private sealed class Input
        {
            public string Scenario="", Output="";
        }
        private static string Hash(byte[] bytes)
        {using var sha=SHA256.Create();return BitConverter.ToString(sha.ComputeHash(bytes)).Replace("-","").ToLowerInvariant();}
        public static void Run()
        {
            try
            {
                var args=Environment.GetCommandLineArgs();int index=Array.IndexOf(args,"-goaScenarioBatch");
                if(index<0 || index+1>=args.Length)throw new InvalidDataException("Missing -goaScenarioBatch manifest.");
                string manifest=args[index+1];if(new FileInfo(manifest).Length>2*1024*1024)throw new InvalidDataException("Manifest exceeds 2 MiB.");
                var inputs=JsonConvert.DeserializeObject<Input[]>(File.ReadAllText(manifest),new JsonSerializerSettings{MissingMemberHandling=MissingMemberHandling.Error,TypeNameHandling=TypeNameHandling.None}) ?? throw new InvalidDataException("Invalid manifest.");
                if(inputs.Length==0 || inputs.Length>512)throw new InvalidDataException("Batch must contain 1 to 512 scenarios.");
                string root=Directory.GetParent(UnityEngine.Application.dataPath)!.Parent!.FullName;
                string allowed=Path.GetFullPath(Path.Combine(root,"artifacts/editor-scenarios"))+Path.DirectorySeparatorChar;
                var outputs=inputs.Select(i=>Path.GetFullPath(i.Output)).ToArray();
                if(outputs.Distinct(StringComparer.OrdinalIgnoreCase).Count()!=outputs.Length || outputs.Any(p=>!p.StartsWith(allowed,StringComparison.OrdinalIgnoreCase) || Directory.Exists(p) || File.Exists(p)))
                    throw new IOException("Outputs must be new distinct directories under artifacts/editor-scenarios.");
                var catalog=ContentLoader.LoadDirectory(root);int exit=0;
                for(int n=0;n<inputs.Length;n++)
                {
                    string input=Path.GetFullPath(inputs[n].Scenario),output=outputs[n];
                    if(new FileInfo(input).Length>2*1024*1024)throw new InvalidDataException("Scenario exceeds 2 MiB.");
                    byte[] bytes=File.ReadAllBytes(input);
                    var runner=new ScenarioRunner(catalog,ScenarioRunner.Load(File.ReadAllText(input,new UTF8Encoding(false,true))));
                    while(!runner.Complete)runner.Next();
                    Directory.CreateDirectory(output);
                    File.WriteAllText(Path.Combine(output,"report.json.save.json"),runner.Session.ExportSave(),new UTF8Encoding(false));
                    File.WriteAllText(Path.Combine(output,"report.json"),JsonConvert.SerializeObject(runner.Report,Formatting.Indented),new UTF8Encoding(false));
                    int result=runner.Report.Passed?0:1;if(result!=0)exit=1;
                    var assemblies=new[]{"Goa2.Domain","Goa2.Rules","Goa2.Application","Goa2.Infrastructure","Goa2.Editor"}.ToDictionary(name=>name+".dll",name=>Hash(File.ReadAllBytes(Path.Combine(root,"unity/Library/ScriptAssemblies",name+".dll"))));
                    File.WriteAllText(Path.Combine(output,"run.json"),JsonConvert.SerializeObject(new{runner="Unity Editor batch",engine=GameState.CurrentEngineVersion,input,inputSha256=Hash(bytes),exitCode=result,finishedUtc=DateTime.UtcNow.ToString("o"),assemblies,unityPlayerTested=false,uiTested=false},Formatting.Indented),new UTF8Encoding(false));
                    Debug.Log((result==0?"GOA2_EDITOR_SCENARIO_PASS ":"GOA2_EDITOR_SCENARIO_FAIL ")+runner.Report.Id+" "+output);
                }
                EditorApplication.Exit(exit);
            }
            catch(Exception error){Debug.LogError("GOA2_EDITOR_SCENARIO_ERROR "+error);EditorApplication.Exit(2);}
        }
    }
}

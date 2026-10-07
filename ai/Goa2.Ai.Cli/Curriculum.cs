using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using Goa2.Ai;
using Goa2.Application;
using Goa2.Domain;
using Goa2.Infrastructure;
using Goa2.Infrastructure.Scenarios;
using Newtonsoft.Json;

namespace Goa2.Ai.Cli
{
    internal sealed class TeachingSource
    {
        public string Path="", Group="", Split="";
    }
    internal static class Curriculum
    {
        // Scenario playback and legality live in C#. Python receives only the allowlisted decision.
        public static int Export(string root,string manifest,string output)
        {
            if(Directory.Exists(output)) throw new IOException("Teaching output must be new");
            Directory.CreateDirectory(output);
            var sources=JsonConvert.DeserializeObject<List<TeachingSource>>(File.ReadAllText(manifest))!;
            if(sources.Count==0 || sources.Any(s=>string.IsNullOrEmpty(s.Group) || (s.Split!="train" && s.Split!="holdout")) ||
               sources.GroupBy(s=>s.Group).Any(g=>g.Select(s=>s.Split).Distinct().Count()!=1) ||
               sources.Select(s=>System.IO.Path.GetFullPath(System.IO.Path.Combine(root,s.Path))).Distinct(StringComparer.OrdinalIgnoreCase).Count()!=sources.Count)
                throw new InvalidDataException("Invalid or overlapping teaching source groups");
            var c=ContentLoader.LoadDirectory(root); var codec=new JsonStateCodec(); var teacher=new SimplePolicy(c,1);
            File.WriteAllText(System.IO.Path.Combine(output,"contract.json"),JsonConvert.SerializeObject(ArtifactContract.Current(c),Formatting.Indented));
            File.Copy(manifest,System.IO.Path.Combine(output,"sources.json"));
            Directory.CreateDirectory(System.IO.Path.Combine(output,"authority"));
            var counts=new Dictionary<string,int>(); int checkedLabels=0,uninformative=0;
            string current="",save=""; int prefix=0;
            using var rows=new StreamWriter(System.IO.Path.Combine(output,"policy.jsonl"),false,new UTF8Encoding(false));
            try
            {
                for(int index=0;index<sources.Count;index++)
                {
                    var source=sources[index]; current=source.Path;
                    string raw=File.ReadAllText(System.IO.Path.Combine(root,source.Path));
                    string hash=Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(raw))).ToLowerInvariant();
                    File.WriteAllText(System.IO.Path.Combine(output,"authority",index+"-source.json"),raw);
                    var definition=ScenarioRunner.Load(raw); definition.VerifyReplayAfterEachStep=false;
                    var runner=new ScenarioRunner(c,definition); var seen=new HashSet<string>();
                    while(!runner.Complete)
                    {
                        var result=runner.Next(); prefix=result.Number;
                        save=runner.Session.ExportSave();
                        if(!result.Passed) throw new InvalidDataException(current+":"+prefix+":"+string.Join(";",result.Errors));
                        var state=codec.Read(save);
                        if(state.Phase==Phase.Finished || state.Phase==Phase.HeroSelection) continue;
                        var env=new HeadlessEnvironment(c,new GameSession(c,codec,state),1,"teaching");
                        var d=env.Next()!;
                        var labels=teacher.Preferred(d.Observation,d.Actions);
                        if(labels.Count==d.Actions.Count){uninformative++;continue;}
                        // Revision is audit identity, not policy input or a feature.
                        string fingerprint=JsonConvert.SerializeObject(new {d.Observation,d.Actions});
                        if(!seen.Add(fingerprint)) continue;
                        foreach(string label in labels)
                        {
                            var probe=new HeadlessEnvironment(c,new GameSession(c,codec,codec.Read(save)),1,"teaching");
                            var decision=probe.Next()!; probe.Submit(decision.Revision,label); checkedLabels++;
                        }
                        rows.WriteLine(JsonConvert.SerializeObject(new {Source=index,SourceHash=hash,source.Group,source.Split,Prefix=prefix,
                            Teacher="simple-v1",Decision=d,Preferred=labels})); rows.Flush();
                        string key=source.Split+"/"+d.Observation.Decision;
                        counts[key]=counts.TryGetValue(key,out int n)?n+1:1;
                    }
                }
                File.WriteAllText(System.IO.Path.Combine(output,"summary.json"),JsonConvert.SerializeObject(new {Counts=counts,CheckedLabels=checkedLabels,
                    UninformativeWindows=uninformative,FormalEvaluation=false,Teacher="simple-v1",Conclusion="Imitation targets, not optimal actions or victory labels"},Formatting.Indented));
                Console.WriteLine(JsonConvert.SerializeObject(counts)); return 0;
            }
            catch(Exception e)
            {
                File.WriteAllText(System.IO.Path.Combine(output,"failure.json"),JsonConvert.SerializeObject(new {Source=current,Prefix=prefix,Error=e.ToString()},Formatting.Indented));
                File.WriteAllText(System.IO.Path.Combine(output,"authority","failure-save.json"),save); throw;
            }
        }
    }
}

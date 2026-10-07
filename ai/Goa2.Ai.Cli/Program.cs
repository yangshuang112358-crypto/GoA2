using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using Goa2.Ai;
using Goa2.Domain;
using Goa2.Infrastructure;
using Goa2.Infrastructure.Scenarios;
using Newtonsoft.Json;

namespace Goa2.Ai.Cli
{
    public sealed class RunResult
    {
        public int Seed, Swap, Decisions, Commands, IllegalCommands, Exceptions;
        public string Stop="running", Winner="", Failure="", StateHash="", ReplayHash="";
        public double Seconds, CpuSeconds, DecisionsPerSecond, PeakWorkingSetMiB, VerificationSeconds;
        public bool RestoreVerified, ScenarioVerified;
        public Dictionary<string,int> Choices = new Dictionary<string,int>();
    }
    internal static class Program
    {
        private static string Json(object x) => JsonConvert.SerializeObject(x,Formatting.Indented);
        private static string Hash(string x) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(x))).ToLowerInvariant();
        private static void Write(string dir,string file,object value) => File.WriteAllText(Path.Combine(dir,file),Json(value),new UTF8Encoding(false));
        private static string Git(string root,params string[] args)
        {
            var start=new ProcessStartInfo("git") { WorkingDirectory=root, RedirectStandardOutput=true, UseShellExecute=false, CreateNoWindow=true };
            foreach(var arg in args) start.ArgumentList.Add(arg);
            using var p=Process.Start(start)!; string result=p.StandardOutput.ReadToEnd().Trim(); p.WaitForExit();
            if(p.ExitCode!=0) throw new InvalidOperationException("git metadata failed"); return result;
        }
        public static int Main(string[] args)
        {
            Console.InputEncoding=new UTF8Encoding(false);
            Console.OutputEncoding=new UTF8Encoding(false);
            if(args.Length==4 && args[0]=="curriculum") return Curriculum.Export(Path.GetFullPath(args[1]),Path.GetFullPath(args[2]),Path.GetFullPath(args[3]));
            if(args.Length==4 && args[0]=="teaching-replay") return TeachingReplay.Export(Path.GetFullPath(args[1]),Path.GetFullPath(args[2]),Path.GetFullPath(args[3]));
            if(args.Length==3 && args[0]=="serve") return TrainingServer.Run(Path.GetFullPath(args[1]),Path.GetFullPath(args[2]));
            if(args.Length==3 && args[0]=="export-replay")
            {
                var episode=Path.GetFullPath(args[1]); var destination=Path.GetFullPath(args[2]);
                if(Directory.Exists(destination)) throw new IOException("Replay output must be new");
                var c=ContentLoader.LoadDirectory(episode);
                string saved=File.ReadAllText(Path.Combine(episode,"final-save.json"));
                if(LocalGameFactory.Restore(c,saved).ExportSave()!=saved) throw new InvalidDataException("Restore mismatch");
                var definition=ScenarioRunner.Load(File.ReadAllText(Path.Combine(episode,"commands.json")));
                if(definition.Steps.Count>1000) throw new InvalidDataException("Existing Unity scenario capacity exceeded; full authority log preserved");
                var runner=new ScenarioRunner(c,definition); while(!runner.Complete) runner.Next();
                if(!runner.Report.Passed || runner.Session.ExportSave()!=saved) throw new InvalidDataException("Scenario mismatch");
                Directory.CreateDirectory(destination);
                Write(destination,"scenario.json",definition); File.WriteAllText(Path.Combine(destination,"final-save.json"),saved);
                Write(destination,"result.json",new {StateHash=Hash(saved),ScenarioVerified=true,Stop=runner.Session.View(null).Phase==Phase.Finished?"terminated":"truncated"});
                Write(destination,"scenario-report.json",runner.Report);
                File.Copy(Path.Combine(episode,"contract.json"),Path.Combine(destination,"contract.json"));
                Console.WriteLine("Verified replay export: "+Hash(saved)); return 0;
            }
            if(args.Length==4 && args[0]=="verify")
            {
                var c=ContentLoader.LoadDirectory(Path.GetFullPath(args[1]));
                var contract=JsonConvert.DeserializeObject<ArtifactContract>(File.ReadAllText(Path.Combine(args[2],"contract.json")))??throw new InvalidDataException("Missing contract");
                contract.RequireCompatible(ArtifactContract.Current(c));
                string save=File.ReadAllText(Path.Combine(args[3],"final-save.json"));
                if(LocalGameFactory.Restore(c,save).ExportSave()!=save) throw new InvalidDataException("Replay mismatch");
                Console.WriteLine("Compatible artifact; authoritative replay verified: "+Hash(save)); return 0;
            }
            if(args.Length!=6 && args.Length!=8) { Console.Error.WriteLine("Usage: <root> <new-output> <seed> <pairs 1..16> <limit 1..9000> <simple-random|random-random|simple-simple> [crystal-life victory-marks]"); return 2; }
            var root=Path.GetFullPath(args[0]); var output=Path.GetFullPath(args[1]);
            int seed=int.Parse(args[2]),pairs=int.Parse(args[3]),limit=int.Parse(args[4]); string matchup=args[5];
            if(pairs<1 || pairs>16 || limit<1 || limit>9000 || !new[]{"simple-random","random-random","simple-simple"}.Contains(matchup)) throw new ArgumentException("Invalid run configuration");
            if(Directory.Exists(output) || File.Exists(output)) throw new IOException("Output must be new; previous failures are immutable.");
            Directory.CreateDirectory(output);
            var catalog=ProfileCatalog.Load(root,args.Length==8?(int?)int.Parse(args[6]):null,args.Length==8?(int?)int.Parse(args[7]):null,output);
            Write(output,"contract.json",ArtifactContract.Current(catalog));
            Write(output,"manifest.json",new { SourceCommit=Git(root,"rev-parse","HEAD"), SourceStatus=Git(root,"status","--porcelain"),
                EngineVersion=GameState.CurrentEngineVersion, RulesVersion=catalog.Rules.Version, ContentVersion=catalog.Version, ContentHash=catalog.Hash,
                ObservationVersion=Observation.Format, ActionVersion=Candidate.Format, RewardVersion=1, Seed=seed, Pairs=pairs, StepLimit=limit,
                Configuration=new { Matchup=matchup, Workers=1, FixedRoster=new[]{"wasp","brogan","arien","sabina"}, Coin="seeded-environment-uniform-v1", Training=false },
                OpponentPool=new[]{"random-v1","simple-v1"}, Evaluation="summary.json", StartedUtc=DateTime.UtcNow, Runtime=Environment.Version.ToString(), LogicalProcessors=Environment.ProcessorCount,
                Assemblies=Directory.GetFiles(AppContext.BaseDirectory,"Goa2.*.dll").ToDictionary(p=>Path.GetFileName(p)!,p=>Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(p))).ToLowerInvariant()) });
            var pipelineWatch=Stopwatch.StartNew();
            var results=new List<RunResult>();
            for(int pair=0;pair<pairs;pair++) for(int swap=0;swap<2;swap++)
            {
                int runSeed=seed+pair; string id=$"ai-{runSeed}-{swap}"; string dir=Path.Combine(output,id); Directory.CreateDirectory(dir);
                var r=new RunResult { Seed=runSeed, Swap=swap }; results.Add(r);
                var env=new HeadlessEnvironment(catalog,runSeed,id);
                var policies=Enumerable.Range(0,4).Select(s =>
                {
                    bool simple=matchup=="simple-simple" || matchup=="simple-random" && s%2==swap;
                    int policySeed=unchecked(runSeed*1009+(s^swap)*7919);
                    return simple?(IPolicy)new SimplePolicy(catalog,policySeed):new RandomPolicy(policySeed);
                }).ToArray();
                Write(dir,"seed-streams.json",new { GameSeed=runSeed, EnvironmentCoinSeed=runSeed ^ 0x43e19a,
                    Policies=Enumerable.Range(0,4).Select(s=>new { Seat=s, Policy=policies[s] is SimplePolicy?"simple-v1":"random-v1", Seed=unchecked(runSeed*1009+(s^swap)*7919) }) });
                var definition=new ScenarioDefinition { SchemaVersion=1, Id=id, Name=id, Seed=runSeed, Sandbox=false, Players=new[]{"AI 0","AI 1","AI 2","AI 3"}, VerifyReplayAfterEachStep=false };
                using var trace=new StreamWriter(new GZipStream(File.Create(Path.Combine(dir,"policy-trace.jsonl.gz")),CompressionLevel.Fastest),new UTF8Encoding(false));
                using var spectator=new StreamWriter(new GZipStream(File.Create(Path.Combine(dir,"spectator-events.jsonl.gz")),CompressionLevel.Fastest),new UTF8Encoding(false));
                long lastEvent=0;
                env.Accepted=(c,v)=>
                {
                    r.Commands++;
                    definition.Steps.Add(new ScenarioStep { Name=c.Kind.ToString(), Actor="p"+(c.ActorSeat+1), Command=c.Kind.ToString(), Value=c.Value, Target=c.TargetSeat<0?"none":"p"+(c.TargetSeat+1), Destination=c.Destination, MoveMode=c.MoveMode.ToString() });
                    // Spectator log uses public projection, never private returned command-result events.
                    var publicView=env.Spectator;
                    spectator.WriteLine(JsonConvert.SerializeObject(new { v.Revision, Events=publicView.Events.Where(e=>e.Sequence>lastEvent) }));
                    lastEvent=publicView.Events.LastOrDefault()?.Sequence??lastEvent;
                };
                var process=Process.GetCurrentProcess(); var cpu=process.TotalProcessorTime; var watch=Stopwatch.StartNew();
                Decision? current=null;
                try
                {
                    while(r.Decisions<limit)
                    {
                        current=env.Next(); if(current==null) break;
                        string key=current.Observation.Decision;
                        r.Choices[key]=r.Choices.TryGetValue(key,out int count)?count+1:1;
                        string action;
                        if(current.Observation.Phase=="HeroSelection")
                        {
                            string hero=new[]{"wasp","brogan","arien","sabina"}[current.Observation.Seat^swap];
                            action=current.Actions.Single(a=>a.Value==hero).Id;
                        }
                        else action=policies[current.Observation.Seat].Choose(current.Observation,current.Actions);
                        trace.WriteLine(JsonConvert.SerializeObject(new { current.Revision, current.Observation, current.Actions, Selected=action }));
                        env.Submit(current.Revision,action); r.Decisions++;
                        if(r.Decisions%100==0) { process.Refresh(); r.PeakWorkingSetMiB=Math.Max(r.PeakWorkingSetMiB,process.PeakWorkingSet64/1048576.0); Console.WriteLine($"{id} decisions={r.Decisions} elapsed={watch.Elapsed.TotalSeconds:F1}s"); }
                    }
                    var final=env.Spectator; r.Stop=final.Phase==Phase.Finished?"terminated":"truncated"; r.Winner=final.Winner?.ToString()??"";
                }
                catch(Exception error)
                {
                    r.Stop="failure"; r.Failure=error.ToString(); r.Exceptions++;
                    if(error.Message.StartsWith("candidate_rejected:",StringComparison.Ordinal)) r.IllegalCommands++;
                    Write(dir,"failure-decision.json",new { Current=current, env.LastAttempt });
                    File.WriteAllText(Path.Combine(dir,"before-failure.json"),env.ExportSave());
                }
                finally
                {
                    watch.Stop(); process.Refresh(); r.Seconds=watch.Elapsed.TotalSeconds; r.CpuSeconds=(process.TotalProcessorTime-cpu).TotalSeconds;
                    r.PeakWorkingSetMiB=Math.Max(r.PeakWorkingSetMiB,process.PeakWorkingSet64/1048576.0); r.DecisionsPerSecond=r.Decisions/r.Seconds;
                    string save=env.ExportSave(); File.WriteAllText(Path.Combine(dir,"final-save.json"),save); r.StateHash=Hash(save);
                    Write(dir,"commands.json",definition);
                    var verifyWatch=Stopwatch.StartNew();
                    try
                    {
                        r.RestoreVerified=LocalGameFactory.Restore(catalog,save).ExportSave()==save;
                        if(definition.Steps.Count<=1000 && definition.Steps.Count>0)
                        {
                            var last=env.Spectator;
                            definition.Steps.Last().Expect=new ScenarioExpectation { Phase=last.Phase.ToString(), Winner=last.Winner?.ToString()??"none", BlueCrystal=last.BlueCrystal, RedCrystal=last.RedCrystal, Round=last.Round, Turn=last.Turn };
                            Write(dir,"scenario.json",definition);
                            var replay=new ScenarioRunner(catalog,definition); while(!replay.Complete) replay.Next();
                            r.ReplayHash=replay.Report.FinalStateHash; r.ScenarioVerified=replay.Report.Passed && replay.Session.ExportSave()==save;
                            Write(dir,"scenario-report.json",replay.Report);
                        }
                        if(!r.RestoreVerified || definition.Steps.Count<=1000 && !r.ScenarioVerified) throw new InvalidOperationException("Replay mismatch");
                    }
                    catch(Exception error) { r.Stop="failure"; r.Exceptions++; r.Failure+="\n"+error; }
                    r.VerificationSeconds=verifyWatch.Elapsed.TotalSeconds;
                    Write(dir,"result.json",r); Write(output,"results.json",results);
                    Console.WriteLine($"{id}: {r.Stop}, {r.Decisions} decisions, {r.Seconds:F2}s, {r.DecisionsPerSecond:F2}/s, {r.Winner} {r.Failure}");
                }
            }
            var completed=results.Where(r=>r.Stop=="terminated").Select(r=>r.Seconds).OrderBy(x=>x).ToArray();
            double? Percentile(double p)
            {
                if(completed.Length==0) return null;
                double position=p*(completed.Length-1); int low=(int)Math.Floor(position), high=(int)Math.Ceiling(position);
                return completed[low]+(completed[high]-completed[low])*(position-low);
            }
            Write(output,"summary.json",new { Games=results.Count, Completed=completed.Length, CompleteRate=(double)completed.Length/results.Count,
                TruncationRate=(double)results.Count(r=>r.Stop=="truncated")/results.Count, ExceptionRate=(double)results.Count(r=>r.Exceptions>0)/results.Count,
                IllegalCommandRate=(double)results.Sum(r=>r.IllegalCommands)/Math.Max(1,results.Sum(r=>r.Commands+r.IllegalCommands)),
                DecisionsPerSecond=results.Sum(r=>r.Decisions)/results.Sum(r=>r.Seconds), CompleteSeconds=new { P50=Percentile(.5), P90=Percentile(.9), Min=Percentile(0), Max=Percentile(1) },
                VerifiedPipelineSeconds=pipelineWatch.Elapsed.TotalSeconds, VerifiedPipelineDecisionsPerSecond=results.Sum(r=>r.Decisions)/pipelineWatch.Elapsed.TotalSeconds,
                AverageCpuPercent=100*results.Sum(r=>r.CpuSeconds)/results.Sum(r=>r.Seconds)/Environment.ProcessorCount, PeakWorkingSetMiB=results.Max(r=>r.PeakWorkingSetMiB),
                TeamRewards=results.Select(r=>new {r.Seed,r.Swap,r.Stop,Blue=r.Stop=="terminated"?(int?)(r.Winner=="Blue"?1:-1):null,Red=r.Stop=="terminated"?(int?)(r.Winner=="Red"?1:-1):null}), Results=results });
            return results.Any(r=>r.Stop=="failure")?1:0;
        }
    }
}

using System;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using Goa2.Ai;
using Goa2.Domain;
using Goa2.Infrastructure;
using Goa2.Infrastructure.Scenarios;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Goa2.Ai.Cli
{
    // JSON-lines host. Only allowlisted policy observations and public catalog features leave stdout.
    // Authority, spectator history, command log and saves remain in the host's separate audit directory.
    internal sealed class TrainingServer : IDisposable
    {
        private readonly string root, output;
        private ContentCatalog catalog;
        private HeadlessEnvironment? env;
        private Decision? decision;
        private IPolicy[] opponents=Array.Empty<IPolicy>();
        private int learner, swap, limit, steps, sequence;
        private string? dir;
        private ScenarioDefinition scenario=new ScenarioDefinition();
        private StreamWriter? trace, spectator;
        private long eventSequence;
        private Stopwatch watch=new Stopwatch();
        private bool ended=true;
        private TrainingServer(string root,string output)
        {
            if(Directory.Exists(output)) throw new IOException("Host output must be new");
            Directory.CreateDirectory(output); this.root=root; this.output=output; catalog=ContentLoader.LoadDirectory(root);
        }
        private void Save(string name,object value) => File.WriteAllText(Path.Combine(dir??output,name),JsonConvert.SerializeObject(value,Formatting.Indented));
        private object Description()
        {
            var ids=new StableIds(catalog);
            return new { Protocol=1, Contract=ArtifactContract.Current(catalog),
                SemanticVersion=Goa2.Rules.Cards.PublicCardMechanics.Format, EventReasons=PublicEventSemantics.Reasons, StepDefinitions=Goa2.Rules.Cards.PublicStepSemantics.Describe(), EffectDefinitions=Goa2.Rules.Cards.PublicEffectSemantics.Describe(),
                Cards=catalog.Cards.Select(c=>new {Id=ids.Card(c.Id),c.HeroId,c.Initiative,c.PrimaryFamily,PrimaryCategory=ObservationProjector.CategoryId(c.PrimaryCategory),c.PrimaryValue,c.SecondaryMovement,c.SecondaryDefense,c.Level,c.Color,c.Exclamation,Mechanics=Goa2.Rules.Cards.PublicCardSemantics.Describe(c,GameState.CurrentEngineVersion),Subtype=ObservationProjector.BonusId(c.Subtype??""),c.SubtypeValue,Passive=ObservationProjector.BonusId(c.Passive??"")}),
                Cells=catalog.Cells.Select(c=>new {c.Position,c.Region,c.Obstacle,c.Lane,c.Base,c.Spawn}),
                Heroes=catalog.Heroes.Select(h=>h.Id),
                ActionKinds=Enum.GetNames(typeof(CommandKind)).Where(k=>!k.StartsWith("Debug",StringComparison.Ordinal)).OrderBy(k=>k,StringComparer.Ordinal) };
        }
        private object Reset(JObject r)
        {
            if(!ended) throw new InvalidOperationException("reset_requires_finished_or_truncated_episode");
            int seed=(int)r["seed"]!; learner=(int)r["learner"]!; swap=(int)r["swap"]!; limit=(int)r["limit"]!;
            if(learner<0 || learner>3 || swap<0 || swap>1 || limit<1 || limit>8000) throw new ArgumentException("invalid_training_configuration");
            string opponent=(string?)r["opponent"]??"simple";
            if(opponent!="simple" && opponent!="random") throw new ArgumentException("unknown_opponent");
            string id=$"train-{sequence++:D4}-{seed}-{swap}";
            dir=Path.Combine(output,id); Directory.CreateDirectory(dir);
            catalog=ProfileCatalog.Load(root,(int?)r["life"],(int?)r["marks"],dir);
            env=new HeadlessEnvironment(catalog,seed,id); steps=0; decision=null; eventSequence=0; ended=false; watch.Restart();
            opponents=Enumerable.Range(0,4).Select(s=>opponent=="simple"?(IPolicy)new SimplePolicy(catalog,unchecked(seed*1009+(s^swap)*7919)):new RandomPolicy(unchecked(seed*1009+(s^swap)*7919))).ToArray();
            scenario=new ScenarioDefinition {SchemaVersion=1,Id=id,Name=id,Seed=seed,Sandbox=false,Players=new[]{"AI 0","AI 1","AI 2","AI 3"},VerifyReplayAfterEachStep=false};
            Save("config.json",r); Save("contract.json",ArtifactContract.Current(catalog));
            trace=new StreamWriter(new GZipStream(File.Create(Path.Combine(dir,"policy.jsonl.gz")),CompressionLevel.Fastest)) {AutoFlush=true};
            spectator=new StreamWriter(new GZipStream(File.Create(Path.Combine(dir,"spectator.jsonl.gz")),CompressionLevel.Fastest)) {AutoFlush=true};
            env.Accepted=(c,v)=> {
                scenario.Steps.Add(new ScenarioStep {Name=c.Kind.ToString(),Actor="p"+(c.ActorSeat+1),Command=c.Kind.ToString(),Value=c.Value,Target=c.TargetSeat<0?"none":"p"+(c.TargetSeat+1),Destination=c.Destination,MoveMode=c.MoveMode.ToString()});
                var events=env.Spectator.Events.Where(e=>e.Sequence>eventSequence).ToArray();
                spectator.WriteLine(JsonConvert.SerializeObject(new {v.Revision,Events=events}));
                if(events.Length>0) eventSequence=events.Last().Sequence;
            };
            return Advance();
        }
        private object Advance()
        {
            while(true)
            {
                decision=env!.Next();
                if(decision==null) return Finish("terminated");
                if(scenario.Steps.Count>=9500) throw new InvalidOperationException("authority_replay_capacity_exceeded");
                // Draft is fixed and excluded from learning. Truncate only at the same learner's next decision,
                // so the bootstrap observation is never a teammate's or enemy's private input.
                if(decision.Observation.Seat==learner && decision.Observation.Phase!="HeroSelection")
                    return steps>=limit?Finish("truncated"):Reply(false,false,0);
                string action=decision.Observation.Phase=="HeroSelection"
                    ?decision.Actions.Single(a=>a.Value==new[]{"wasp","brogan","arien","sabina"}[decision.Observation.Seat^swap]).Id
                    :opponents[decision.Observation.Seat].Choose(decision.Observation,decision.Actions);
                env.Submit(decision.Revision,action); steps++;
            }
        }
        private object Reply(bool terminated,bool truncated,int reward) => new {Protocol=1,Decision=decision,Terminated=terminated,Truncated=truncated,Reward=reward,EnvironmentSteps=steps};
        private object Finish(string stop)
        {
            ended=true; watch.Stop(); trace?.Dispose(); spectator?.Dispose(); trace=null; spectator=null;
            string save=env!.ExportSave(); File.WriteAllText(Path.Combine(dir!,"final-save.json"),save); Save("commands.json",scenario);
            int reward=stop=="terminated"?(env.Spectator.Winner==(learner%2==0?Team.Blue:Team.Red)?1:-1):0;
            bool verified=LocalGameFactory.Restore(catalog,save).ExportSave()==save;
            Save("result.json",new {Stop=stop,Reward=reward,EnvironmentSteps=steps,Commands=scenario.Steps.Count,Seconds=watch.Elapsed.TotalSeconds,RestoreVerified=verified,Winner=env.Spectator.Winner?.ToString()});
            if(!verified) throw new InvalidDataException("authoritative_restore_mismatch");
            return Reply(stop=="terminated",stop=="truncated",reward);
        }
        private object Handle(JObject r)
        {
            switch((string?)r["op"])
            {
                case "describe": return Description();
                case "reset": return Reset(r);
                case "step":
                    if(ended || decision==null) throw new InvalidOperationException("no_live_decision");
                    string action=(string)r["action"]!;
                    trace!.WriteLine(JsonConvert.SerializeObject(new {Input=decision,Selected=action}));
                    env!.Submit((long)r["revision"]!,action); steps++; return Advance();
                default: throw new ArgumentException("unsupported_protocol_operation");
            }
        }
        public void Dispose()
        {
            if(!ended && env!=null) Finish("interrupted");
            trace?.Dispose(); spectator?.Dispose();
        }
        public static int Run(string root,string output)
        {
            using var host=new TrainingServer(root,output);
            try
            {
                string? line;
                while((line=Console.ReadLine())!=null)
                {
                    var r=JObject.Parse(line);
                    if((string?)r["op"]=="close") break;
                    Console.WriteLine(JsonConvert.SerializeObject(host.Handle(r))); Console.Out.Flush();
                }
                return 0;
            }
            catch(Exception error)
            {
                host.Save("failure.json",new {Error=error.ToString(),host.decision,LastAttempt=host.env?.LastAttempt});
                if(host.env!=null) File.WriteAllText(Path.Combine(host.dir!,"failure-save.json"),host.env.ExportSave());
                host.ended=true; Console.WriteLine(JsonConvert.SerializeObject(new {Protocol=1,Error=error.Message})); Console.Out.Flush(); return 1;
            }
        }
    }
}

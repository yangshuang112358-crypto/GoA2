using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using Goa2.Ai;
using Goa2.Infrastructure;
using Goa2.Infrastructure.Scenarios;
using Newtonsoft.Json;

namespace Goa2.Ai.Cli
{
    internal sealed class TeachingReplayRequest
    {
        public string Source="", Id="", Name="", Action="";
        public int Prefix=0;
    }
    internal static class TeachingReplay
    {
        public static int Export(string root,string requestPath,string output)
        {
            if(Directory.Exists(output)) throw new IOException("Teaching replay output must be new");
            Directory.CreateDirectory(output);
            string save="";
            try
            {
                var r=JsonConvert.DeserializeObject<TeachingReplayRequest>(File.ReadAllText(requestPath))!;
                var c=ContentLoader.LoadDirectory(root);
                var definition=ScenarioRunner.Load(File.ReadAllText(Path.Combine(root,r.Source)));
                if(r.Prefix<1 || r.Prefix>=definition.Steps.Count) throw new ArgumentOutOfRangeException(nameof(r.Prefix));
                definition.Steps=definition.Steps.Take(r.Prefix).ToList(); definition.Id=r.Id; definition.Name=r.Name;
                definition.VerifyReplayAfterEachStep=false;
                var runner=new ScenarioRunner(c,ScenarioRunner.Load(JsonConvert.SerializeObject(definition)));
                while(!runner.Complete) runner.Next();
                save=runner.Session.ExportSave();
                if(!runner.Report.Passed) throw new InvalidDataException("Teaching prefix failed");
                var env=new HeadlessEnvironment(c,runner.Session,1,"teaching-choice"); var d=env.Next()!;
                env.Submit(d.Revision,r.Action); var command=env.LastAttempt!;
                definition.Steps.Add(new ScenarioStep {Name=r.Name,Actor="p"+(command.ActorSeat+1),Command=command.Kind.ToString(),
                    Value=command.Value,Target=command.TargetSeat<0?"none":"p"+(command.TargetSeat+1),Destination=command.Destination,MoveMode=command.MoveMode.ToString()});
                // Rebuild accepted command IDs using the existing scenario mechanism itself.
                runner=new ScenarioRunner(c,definition); while(!runner.Complete) runner.Next();
                save=runner.Session.ExportSave();
                if(!runner.Report.Passed || LocalGameFactory.Restore(c,save).ExportSave()!=save) throw new InvalidDataException("Teaching branch replay failed");
                void Write(string name,object value)=>File.WriteAllText(Path.Combine(output,name),JsonConvert.SerializeObject(value,Formatting.Indented));
                Write("scenario.json",definition); Write("scenario-report.json",runner.Report);
                Write("policy-decision.json",new {Input=d,Selected=r.Action});
                Write("request.json",r); Write("contract.json",ArtifactContract.Current(c));
                File.WriteAllText(Path.Combine(output,"final-save.json"),save);
                Write("result.json",new {ScenarioVerified=true,FormalEvaluation=false,Stop="teaching-prefix",Commands=definition.Steps.Count,
                    StateHash=Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(save))).ToLowerInvariant()});
                return 0;
            }
            catch(Exception e)
            {
                File.WriteAllText(Path.Combine(output,"failure.txt"),e.ToString());
                File.WriteAllText(Path.Combine(output,"failure-save.json"),save); throw;
            }
        }
    }
}

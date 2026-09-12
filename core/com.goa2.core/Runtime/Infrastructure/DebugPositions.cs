#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using Goa2.Application;
using Goa2.Domain;
using Goa2.Infrastructure.Scenarios;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Goa2.Infrastructure
{
    public sealed class DebugPosition
    {
        public string Id { get; }
        public string Title { get; }
        public Phase Phase { get; }
        public string Pending { get; }
        public string Instructions { get; }
        internal string ScenarioJson { get; }
        internal DebugPosition(string id,string title,Phase phase,string pending,string scenarioJson,string instructions="")
        { Id=id;Title=title;Phase=phase;Pending=pending;ScenarioJson=scenarioJson;Instructions=instructions; }
    }
    public static class DebugPositions
    {
        private static void Require(bool valid,string message) { if(!valid) throw new InvalidDataException(message); }
        private static JObject Object(JToken token,params string[] fields)
        {
            Require(token is JObject,"调试局面需要对象。"); var value=(JObject)token;
            Require(new HashSet<string>(value.Properties().Select(p=>p.Name),StringComparer.Ordinal).SetEquals(fields),"调试局面字段缺失或包含未知字段。");
            return value;
        }
        private static string Text(JObject value,string name,int maximum,bool empty=false)
        {
            var token=value[name]!;
            Require(token.Type==JTokenType.String,"调试局面字段须为字符串："+name);
            string text=token.Value<string>()!;
            Require(text.Length<=maximum && (empty || !string.IsNullOrWhiteSpace(text)),"调试局面字段长度无效："+name);
            return text;
        }
        private static JObject WithInstructions(JToken token,string key,params string[] fields)
        {
            Require(token is JObject,"调试局面需要对象。");
            return Object(token,((JObject)token).Property(key)==null ? fields : fields.Concat(new[]{key}).ToArray());
        }
        private static string Id(JObject value,string name)
        {
            string id=Text(value,name,40);
            Require(Regex.IsMatch(id,"\\A[a-z0-9][a-z0-9-]*\\z"),"调试局面ID无效："+name); return id;
        }
        private static Phase ReadPhase(JObject value,string name)
        {
            string text=Text(value,name,30);
            Require(Enum.TryParse<Phase>(text,out var phase) && Enum.IsDefined(typeof(Phase),phase) && phase.ToString()==text,"调试局面阶段无效。");
            return phase;
        }
        private static JArray Array(JToken token)
        {
            Require(token is JArray && ((JArray)token).Count>0 && ((JArray)token).Count<=256,"需要1至256个调试局面。");
            return (JArray)token;
        }
        public static string Prepare(string root)
        {
            var source=Array(JsonStateCodec.ParseStrict(File.ReadAllText(Path.Combine(root,"tools","debug-positions.json"),Encoding.UTF8)));
            var prepared=new JArray(); var ids=new HashSet<string>(StringComparer.Ordinal);
            foreach(var token in source)
            {
                var entry=WithInstructions(token,"instructions","id","title","scenario","through_step","phase","pending");
                string id=Id(entry,"id"); Require(ids.Add(id),"调试局面ID重复。");
                string title=Text(entry,"title",160),scenario=Id(entry,"scenario"),pending=Text(entry,"pending",80,true);
                var phase=ReadPhase(entry,"phase");
                var steps=entry["through_step"]!;
                Require(steps.Type==JTokenType.Integer && int.TryParse(steps.ToString(),out _),"through_step须为整数。");
                int through=steps.Value<int>();
                var definition=ScenarioRunner.Load(File.ReadAllText(Path.Combine(root,"tests","scenarios",scenario+".json"),Encoding.UTF8));
                Require(definition.Sandbox,"调试局面必须来自测试对局。");
                Require(through>0 && through<=definition.Steps.Count,"调试局面截取步数越界。");
                definition.Id="debug-"+id; definition.Name=title; definition.Steps=definition.Steps.Take(through).ToList();
                definition.VerifyReplayAfterEachStep=true;
                prepared.Add(new JObject { ["Id"]=id,["Title"]=title,["Phase"]=phase.ToString(),["Pending"]=pending,["Scenario"]=JObject.FromObject(definition),
                    ["Instructions"]=entry.Property("instructions")==null ? "" : Text(entry,"instructions",2400,true) });
            }
            string json=new JObject { ["SchemaVersion"]=1,["Presets"]=prepared }.ToString(Formatting.Indented);
            _=Read(json); return json;
        }
        public static IReadOnlyList<DebugPosition> Read(string json)
        {
            Require(json!=null && Encoding.UTF8.GetByteCount(json)<=2*1024*1024,"调试局面资料为空或过大。");
            var document=Object(JsonStateCodec.ParseStrict(json!),"SchemaVersion","Presets");
            Require(document["SchemaVersion"]!.Type==JTokenType.Integer && document["SchemaVersion"]!.ToString()=="1","调试局面版本无效。");
            var positions=new List<DebugPosition>(); var ids=new HashSet<string>(StringComparer.Ordinal);
            foreach(var token in Array(document["Presets"]!))
            {
                var entry=WithInstructions(token,"Instructions","Id","Title","Phase","Pending","Scenario");
                string id=Id(entry,"Id"); Require(ids.Add(id),"调试局面ID重复。");
                string title=Text(entry,"Title",160),pending=Text(entry,"Pending",80,true); var phase=ReadPhase(entry,"Phase");
                string scenarioJson=entry["Scenario"]!.ToString(Formatting.None);
                var definition=ScenarioRunner.Load(scenarioJson);
                Require(definition.Sandbox && definition.VerifyReplayAfterEachStep,"调试局面需要测试权限和逐步恢复验证。");
                positions.Add(new DebugPosition(id,title,phase,pending,scenarioJson,entry.Property("Instructions")==null ? "" : Text(entry,"Instructions",2400,true)));
            }
            return positions.AsReadOnly();
        }
        public static GameSession Open(ContentCatalog catalog,DebugPosition position)
        {
            var definition=ScenarioRunner.Load(position.ScenarioJson);
            definition.Id="debug-"+position.Id+"-"+Guid.NewGuid().ToString("N").Substring(0,16);
            var runner=new ScenarioRunner(catalog,definition);
            while(!runner.Complete) runner.Next();
            Require(runner.Report.Passed,"调试局面验证未通过："+string.Join("; ",runner.Report.Steps.SelectMany(s=>s.Errors)));
            var view=runner.Session.View(null);
            Require(view.Phase==position.Phase && (view.Pending?.Kind ?? "")==position.Pending,"调试局面的实际阶段与定义不符。");
            return runner.Session;
        }
    }
}

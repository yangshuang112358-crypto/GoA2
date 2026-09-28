#nullable enable
using System;
using System.Collections;
using System.IO;
using Goa2.Domain;
using Goa2.Network.Client;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;
using Newtonsoft.Json.Linq;
using UnityEngine;
using UnityEngine.UIElements;

namespace Goa2.Presentation
{
    public sealed partial class GameScreen
    {
        // Explicit local QA launch only. No RPC endpoint; no credentials written.
        private void StartNetworkAudit()
        {
            var args=Environment.GetCommandLineArgs();int i=Array.IndexOf(args,"-goaNetworkAudit");
            if(i>=0 && i+1<args.Length && NetworkMode)StartCoroutine(NetworkAuditFiles(args[i+1]));
        }
        private IEnumerator NetworkAuditFiles(string directory)
        {
            Directory.CreateDirectory(directory);long last=-1;
            var settings=new JsonSerializerSettings{Converters={new StringEnumConverter()}};
            UnityEngine.Application.targetFrameRate=30;
            while(true)
            {
                yield return new WaitForSecondsRealtime(.05f);
                string input=Path.Combine(directory,"input.json");if(!File.Exists(input))continue;
                JObject? request=null;try{request=JObject.Parse(File.ReadAllText(input));}catch(IOException){}catch(JsonException){}
                if(request==null || (long)request["seq"]!<=last)continue;last=(long)request["seq"]!;
                string op=(string)request["op"]!;string error="";
                try
                {
                    var args=request["args"] as JObject ?? new JObject();
                    if(op=="submit")Submit(Enum.Parse<CommandKind>((string)request["kind"]!), (string?)args["Value"]??"",(int?)args["TargetSeat"]??-1,args["Destination"]?.ToObject<Hex>()??default,args["MoveMode"]==null ? MoveMode.Secondary : Enum.Parse<MoveMode>((string)args["MoveMode"]!));
                    else if(op=="pick"){lastNetworkResult=null;WheelPick((string)request["card"]!);}
                    else if(op=="confirm"){lastNetworkResult=null;ConfirmCurrent();}
                    else if(op=="switch")SwitchSeat((int)request["seat"]!);
                    else if(op=="disconnect")networkSession!.Disconnect();
                    else if(op=="connect")ReconnectNetwork();
                    else if(op=="retry")RetryNetwork();
                    else if(op=="settings"){rightExpanded=true;Render();}
                    else if(op=="screen"){rightExpanded=false;Render();}
                    else if(op=="quit"){UnityEngine.Application.Quit();yield break;}
                    else if(op!="view")throw new ArgumentException("Unknown QA operation");
                }
                catch(Exception e){error=e.Message;}
                float until=Time.realtimeSinceStartup+22;
                while((networkBusy || networkReconnecting || op=="view" && (networkView==null || networkView.Revision<((long?)request["revision"]??0))) && Time.realtimeSinceStartup<until)yield return null;
                yield return null;yield return null;
                if(op=="screen"){yield return new WaitForSecondsRealtime(.4f);ScreenCapture.CaptureScreenshot(Path.Combine(directory,"screen.png"));}
                if(Time.realtimeSinceStartup>=until)error="Timed out";
                var result=new{seq=last,error,seat,connection=networkSession!.Connection.ToString(),canAct=NetworkCanAct,
                    hasLocalSession=session!=null,hasBoard=board!=null,boardConnected=board?.Connected,
                    confirmEnabled=confirmButton?.enabledInHierarchy??false,wheelPreview,uncertain=uncertainCommand!="",
                    reconnectEnabled=root.Q<Button>("network-reconnect")?.enabledInHierarchy??false,
                    discardBeats=wheelState.Discards.Count,result=lastNetworkResult,view=networkView};
                string output=Path.Combine(directory,"result-"+last+".json"),temp=output+".tmp";
                File.WriteAllText(temp,JsonConvert.SerializeObject(result,settings));
                File.Move(temp,output);
            }
        }
    }
}

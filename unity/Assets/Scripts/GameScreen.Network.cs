#nullable enable
using System;
using System.IO;
using System.Threading;
using Goa2.Domain;
using Goa2.Network.Client;
using Newtonsoft.Json.Linq;
using UnityEngine.UIElements;

namespace Goa2.Presentation
{
    public sealed partial class GameScreen
    {
        private IPlayerSession? networkSession;
        private GameView? networkView;
        private bool networkBusy, networkReconnecting, networkDestroyed;
        private string uncertainCommand="";
        private IntentResult? lastNetworkResult;
        private ConnectionState previousConnection;
        private bool NetworkMode => networkSession!=null;
        private bool NetworkCanAct => !NetworkMode || networkSession!.Connection==ConnectionState.Connected && !networkBusy && uncertainCommand=="";
        private bool HasGameView => NetworkMode ? networkView!=null : session!=null;

        private void StartNetwork(string path)
        {
            var json=File.ReadAllText(path);var ticket=JObject.Parse(json);var caps=ticket["Capabilities"];
            if((int?)caps?["WireVersion"]!=1 || (string?)caps?["ProtocolVersion"]!=GameState.CurrentProtocol ||
               (int?)caps?["EngineVersion"]!=GameState.CurrentEngineVersion || (string?)caps?["ContentHash"]!=catalog.Hash ||
               (string?)caps?["ContentVersion"]!=catalog.Version || (string?)caps?["RulesVersion"]!=catalog.Rules.Version)
                throw new InvalidOperationException("联机版本不一致，请使用同一发行包和新房间的席位文件。");
            networkSession=new NetworkPlayerSession(json,SynchronizationContext.Current);
            networkSession.StateUpdated+=OnNetworkUpdated;
            notice="正在连接房间…";rightExpanded=true;Render();ReconnectNetwork();
        }
        private void ClearNetworkPreview()
        {
            ClearPending();wheelPreview="";wheelDecline=false;
            primaryOptionChoice="";returnUnitId="";spawnUnitId="";
        }
        private void OnNetworkUpdated()
        {
            if(networkDestroyed || networkSession==null)return;
            var next=networkSession.View;var state=networkSession.Connection;
            bool changed=next!=null && (networkView==null || next.MatchId!=networkView.MatchId || next.Revision!=networkView.Revision);
            if(changed || previousConnection!=state)ClearNetworkPreview();
            previousConnection=state;
            if(next!=null){networkView=next;seat=networkSession.AuthenticatedSeat!.Value;}
            if(state!=ConnectionState.Connected)notice=state==ConnectionState.Connecting ? "连接中…" : "连接已断开，操作已停用；可重连原席位。";
            Render();
        }
        private async void ReconnectNetwork()
        {
            if(networkReconnecting || networkSession==null)return;
            networkReconnecting=true;ClearNetworkPreview();
            try{await networkSession.ReconnectAsync();notice="已连接 · 本人席位 "+(networkSession.AuthenticatedSeat+1)+" · 四人分别确认选牌";}
            catch{notice="连接失败。请检查服务端、地址及席位文件后重试。";}
            finally{networkReconnecting=false;if(!networkDestroyed)Render();}
        }
        private async void SubmitNetwork(CommandKind kind,string value,int target,Hex destination,MoveMode mode)
        {
            if(!NetworkCanAct){notice="等待连接或上一条操作确认后再操作。";Render();return;}
            lastNetworkResult=null;networkBusy=true;Render();
            try{var result=await networkSession!.SubmitAsync(UiIntent.Create(kind,value,target,destination,mode));ApplyNetworkResult(result);}
            catch{notice="操作未提交，请重新选择。";}
            finally{networkBusy=false;if(!networkDestroyed){ClearNetworkPreview();Render();}}
        }
        private void ApplyNetworkResult(IntentResult result)
        {
            lastNetworkResult=result;
            uncertainCommand=result.Uncertain ? result.CommandId : "";
            notice=result.Uncertain ? "上次操作结果未知。重连后请查询/重试原操作，不会重复执行。" :
                result.Accepted ? "操作已由服务端确认。" : "未接受："+result.Code+" · "+result.Message;
        }
        private async void RetryNetwork()
        {
            if(networkBusy || networkSession?.Connection!=ConnectionState.Connected || uncertainCommand=="")return;
            networkBusy=true;Render();
            try{ApplyNetworkResult(await networkSession.RetryAsync(uncertainCommand));}
            catch{notice="原操作查询失败，请重连后再试。";}
            finally{networkBusy=false;if(!networkDestroyed){ClearNetworkPreview();Render();}}
        }
        private void RenderNetworkControls(VisualElement parent)
        {
            parent.Add(Text(networkSession!.Connection==ConnectionState.Connected ? "联机 · 席位 "+(seat+1) : "联机 · 未连接","muted"));
            if(networkSession.Connection!=ConnectionState.Connected){var retry=Button("重连",ReconnectNetwork,"quiet-button","network-reconnect");retry.SetEnabled(!networkReconnecting);parent.Add(retry);}
            else parent.Add(Button("断开",()=>networkSession.Disconnect(),"quiet-button","network-disconnect"));
            if(uncertainCommand!=""){var retry=Button("查询/重试原操作",RetryNetwork,"quiet-button","network-retry");retry.SetEnabled(!networkBusy && networkSession.Connection==ConnectionState.Connected);parent.Add(retry);}
        }
        private void RenderNetworkWaiting()
        {
            root.Clear();var panel=Box("shell");root.Add(panel);panel.Add(Text("GOA II 联机","brand-title"));
            panel.Add(Text(notice,"body"));RenderNetworkControls(panel);
        }
        private void ApplyNetworkInputGate()
        {
            if(!NetworkMode || NetworkCanAct)return;
            board?.SetConnected(false);
            root.Q("decision-content")?.SetEnabled(false);root.Q("hand-zone")?.SetEnabled(false);root.Q("upgrade-zone")?.SetEnabled(false);
            root.Q("world-decisions")?.SetEnabled(false);
            skillWheel?.SetEnabled(false);root.Q("floating-confirm")?.SetEnabled(false);
            confirmAction=null;confirmButton=null;
        }
        private void OnDestroy()
        {
            networkDestroyed=true;if(networkSession!=null){networkSession.StateUpdated-=OnNetworkUpdated;networkSession.Dispose();}
        }
    }
}

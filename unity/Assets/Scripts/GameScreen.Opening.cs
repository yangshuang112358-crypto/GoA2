#nullable enable
using System;
using System.Linq;
using System.Globalization;
using System.Threading.Tasks;
using Goa2.Domain;
using Goa2.Network.Client;
using Goa2.Presentation.UI3D;
using UnityEngine;
using UnityEngine.UIElements;
namespace Goa2.Presentation
{
    public sealed partial class GameScreen
    {
        private PhysicalCoinStage? physicalCoin;
        private Image? coinImage;
        private string coinMatch="",completedToss="";
        private Task lastCoinFrame=Task.CompletedTask;
        private bool openingSubmitting,coinWasConnected;
        private Team displayedDraftCoin;
        private void PrepareCoinVoteDemo()
        {
            if(NetworkMode)return;
            NewMatch();UpdateOpening();physicalCoin?.PreviewStuck();
            Submit(CommandKind.MarkCoinStuck,renderedView.Opening!.TossId);
            rightExpanded=false;notice="卡边投票演示：按1—4依次切换本地席位，四人都同意才重新抛币。";Render();
        }
        private void UpdateOpening()
        {
            if(!HasGameView || startupFailed)return;
            var opening=renderedView.Opening;
            if(opening==null){physicalCoin?.Dispose();physicalCoin=null;coinImage=null;board3DViewport.Presentation.HideCoin=false;coinMatch="";return;}
            string identity=renderedView.MatchId+":"+opening.TossId;
            bool connected=!NetworkMode || networkSession!.Connection==ConnectionState.Connected;
            bool host=!NetworkMode || seat==opening.HostSeat;
            if(identity!=coinMatch && (opening.Status!="settled" || renderedView.Phase==Phase.HeroSelection))
            {
                physicalCoin?.Dispose();physicalCoin=new PhysicalCoinStage(opening.TossId);coinMatch=identity;completedToss="";
                physicalCoin.FrameReady=frame=>{if(networkSession is NetworkPlayerSession client)lastCoinFrame=client.SendCoinMotionAsync(frame);};
                physicalCoin.Settled=(side,q)=>SubmitOpening(CommandKind.ReportCoinToss,opening.TossId+"|"+side+"|"+string.Join(",",new[]{q.x,q.y,q.z,q.w}.Select(x=>x.ToString("R",CultureInfo.InvariantCulture))));
                physicalCoin.Stuck=()=>SubmitOpening(CommandKind.MarkCoinStuck,opening.TossId);
                if(networkSession is NetworkPlayerSession connection && connection.LatestCoinMotion is CoinMotion saved && saved.TossId==opening.TossId)
                {if(host)physicalCoin.Resume(saved);else physicalCoin.AcceptFrame(saved);}
                if(opening.Status=="settled"){physicalCoin.DisplaySide(renderedView.DecisionCoin,false);displayedDraftCoin=renderedView.DecisionCoin;}
                Render();
            }
            if(physicalCoin==null)return;
            if(networkSession is NetworkPlayerSession net && net.LatestCoinMotion is CoinMotion motion && motion.TossId==opening.TossId)
            {
                if(host && connected && !coinWasConnected)physicalCoin.Resume(motion);
                else if(!host)physicalCoin.AcceptFrame(motion);
            }
            coinWasConnected=connected;
            if(opening.Status=="settled" && opening.Result.HasValue && !physicalCoin.Finishing){physicalCoin.Park(opening.Result.Value,opening.FinalPose);displayedDraftCoin=renderedView.DecisionCoin;}
            if(renderedView.Phase==Phase.HeroSelection && physicalCoin.Complete && displayedDraftCoin!=renderedView.DecisionCoin){physicalCoin.DisplaySide(renderedView.DecisionCoin,true);displayedDraftCoin=renderedView.DecisionCoin;completedToss="";}
            physicalCoin.Tick(host,connected && !StagePresenting && !openingSubmitting && opening.Status=="throwing" && uncertainCommand=="");
            board3DViewport.Presentation.HideCoin=!opening.OpeningComplete || !physicalCoin.Complete;
            if(opening.Purpose=="draft" && physicalCoin.Finishing)physicalCoin.SetParkingFraming(physicalCoin.ParkProgress*.78f);
            if(opening.Purpose=="opening" && physicalCoin.Finishing && coinImage!=null && board?.Scene!=null)
            {
                float t=physicalCoin.ParkProgress,ease=1-Mathf.Pow(1-t,3);physicalCoin.SetParkingFraming(ease);
                var world=(Board3DGeometry.World(new Hex(0,0))+Board3DGeometry.World(new Hex(0,1)))*.5f+Vector3.up*(Board3DScene.WallHeight+.245f);
                var p=board.Scene.Camera.WorldToViewportPoint(world);var rect=board.worldBound;
                float width=.965f*rect.height/(2*board.Scene.Camera.orthographicSize)*1.75f;
                float x=rect.x+p.x*rect.width,y=rect.y+(1-p.y)*rect.height;
                coinImage.style.right=StyleKeyword.Auto;coinImage.style.bottom=StyleKeyword.Auto;
                Place(coinImage,Mathf.Lerp(0,x-width*.5f,ease),Mathf.Lerp(0,y-width*.375f,ease),Mathf.Lerp(Screen.width,width,ease),Mathf.Lerp(Screen.height,width*.75f,ease));
            }
            if(coinImage!=null)coinImage.image=physicalCoin.Texture;
            if(physicalCoin.Complete && completedToss!=identity){completedToss=identity;Render();}
        }
        private async void SubmitOpening(CommandKind kind,string value)
        {
            if(openingSubmitting)return;openingSubmitting=true;
            try
            {
                await lastCoinFrame;
                bool accepted;
                if(NetworkMode)
                {
                    var result=await networkSession!.SubmitAsync(new PlayerIntent(kind,value));ApplyNetworkResult(result);accepted=result.Accepted;
                    if(result.Uncertain)return;
                }
                else
                {
                    var v=session.View(0);var result=session.Execute(0,new Command{Id=Guid.NewGuid().ToString("N"),MatchId=v.MatchId,ExpectedRevision=v.Revision,ActorSeat=0,Kind=kind,Value=value});accepted=result.Accepted;
                    if(!accepted)notice=result.Message;
                }
                if(!accepted){await Task.Delay(400);physicalCoin?.RetryResult();}
            }
            catch(Exception error){notice="投币同步暂未完成："+error.Message;physicalCoin?.RetryResult();}
            finally
            {
                openingSubmitting=false;
                if(!networkDestroyed)
                {
                    if(NetworkMode && networkSession!.View!=null)networkView=networkSession.View;
                    var op=(NetworkMode?networkView:session.View(0))?.Opening;
                    if(op?.Status=="settled" && op.Result.HasValue && physicalCoin!=null && !physicalCoin.Finishing){physicalCoin.Park(op.Result.Value,op.FinalPose);displayedDraftCoin=op.Result.Value;}
                    Render();
                }
            }
        }
        private void BuildOpening()
        {
            coinImage=null;var opening=renderedView.Opening;if(opening==null || rightExpanded)return;
            if(renderedView.Phase==Phase.HeroSelection)
            {
                var overlay=new VisualElement{name="draft-overlay",pickingMode=PickingMode.Ignore};overlay.style.position=Position.Absolute;overlay.style.left=0;overlay.style.right=0;overlay.style.top=0;overlay.style.bottom=0;overlay.style.backgroundColor=new Color(.02f,.04f,.06f,.65f);root.Add(overlay);
                var panel=new VisualElement{name="draft-panel"};panel.style.position=Position.Absolute;panel.style.width=920;panel.style.height=610;panel.style.left=Length.Percent(50);panel.style.top=Length.Percent(50);panel.style.translate=new Translate(Length.Percent(-50),Length.Percent(-50));
                float scale=Mathf.Min(1,(Screen.width-48)/920f,(Screen.height-40)/610f);panel.style.scale=new Scale(new Vector3(scale,scale,1));
                panel.style.backgroundColor=new Color(.11f,.135f,.16f,.98f);panel.style.borderTopWidth=4;panel.style.borderBottomWidth=4;panel.style.borderLeftWidth=4;panel.style.borderRightWidth=4;
                var gold=new Color(.57f,.43f,.24f);panel.style.borderTopColor=gold;panel.style.borderBottomColor=gold;panel.style.borderLeftColor=gold;panel.style.borderRightColor=gold;overlay.Add(panel);
                var heading=OpeningLabel("选择你的英雄",28,new Color(1,.83f,.47f));Place(heading,24,14,500,45);panel.Add(heading);
                var ring=new VisualElement{name="draft-hero-ring"};Place(ring,14,62,515,465);panel.Add(ring);
                ring.generateVisualContent+=ctx=>{var p=ctx.painter2D;p.strokeColor=new Color(.58f,.45f,.28f,.7f);p.lineWidth=4;p.BeginPath();p.Arc(new Vector2(257,233),178,0,360);p.Stroke();};
                coinImage=new Image{name="draft-coin",pickingMode=PickingMode.Ignore,scaleMode=ScaleMode.ScaleToFit};Place(coinImage,78,96,360,270);ring.Add(coinImage);
                if(physicalCoin!=null)coinImage.image=physicalCoin.Texture;
                int i=0;
                foreach(var hero in catalog.Heroes)
                {
                    string id=hero.Id;var owner=renderedView.Players.FirstOrDefault(p=>p.HeroId==id);float a=(-90+i++*60)*Mathf.Deg2Rad;
                    var choice=Button(hero.Name.Replace("·","\n"),()=>{chosenHero=id;Sound("select");Render();},"choice-button","draft-hero-"+id);
                    Place(choice,257+Mathf.Cos(a)*178-77,233+Mathf.Sin(a)*178-39,154,78);choice.style.whiteSpace=WhiteSpace.Normal;choice.style.fontSize=20;choice.style.unityTextAlign=TextAnchor.MiddleCenter;
                    choice.style.backgroundColor=owner!=null?new Color(.075f,.085f,.10f):chosenHero==id?new Color(.29f,.27f,.20f):new Color(.17f,.2f,.23f);
                    var border=owner==null?gold:owner.Team==Team.Red?new Color(.86f,.23f,.28f):new Color(.25f,.55f,.95f);
                    choice.style.borderTopColor=border;choice.style.borderBottomColor=border;choice.style.borderLeftColor=border;choice.style.borderRightColor=border;
                    choice.style.borderTopWidth=3;choice.style.borderBottomWidth=3;choice.style.borderLeftWidth=3;choice.style.borderRightWidth=3;
                    if(owner!=null)choice.style.color=new Color(.6f,.62f,.65f);ring.Add(choice);
                }
                string line=OpeningPrompt();var prompt=OpeningLabel(line,20,new Color(.9f,.83f,.68f));Place(prompt,24,530,500,58);panel.Add(prompt);
                var details=new VisualElement{name="draft-details"};Place(details,557,65,326,510);panel.Add(details);
                var selected=catalog.Heroes.FirstOrDefault(h=>h.Id==chosenHero);details.Add(OpeningLabel(selected?.Name??"英雄介绍",27,new Color(1,.84f,.52f)));
                var bio=OpeningLabel(selected==null?"选择任一英雄，查看其战斗风格。":HeroOverview(selected.Id),22,new Color(.83f,.85f,.87f));bio.style.marginTop=24;bio.style.whiteSpace=WhiteSpace.Normal;details.Add(bio);
                var current=renderedView.Players.First(p=>p.Seat==seat);
                if(selected!=null && current.HeroId==null && current.Team==renderedView.DraftTeam && renderedView.AvailableHeroes.Contains(selected.Id))
                {
                    var confirm=Button("选择 "+HeroName(selected.Id),()=>Submit(CommandKind.ChooseHero,selected.Id),"button","draft-confirm");Place(confirm,0,412,326,64);confirm.style.fontSize=23;confirm.SetEnabled(NetworkCanAct && !StagePresenting && (physicalCoin==null || physicalCoin.Complete));details.Add(confirm);
                    if(confirm.enabledSelf){confirmAction=()=>Submit(CommandKind.ChooseHero,selected.Id);confirmButton=confirm;}
                }
                if(!NetworkMode)
                {
                    var seats=new VisualElement();seats.style.flexDirection=FlexDirection.Row;Place(seats,0,335,330,55);
                    foreach(var player in renderedView.Players){int s=player.Seat;var b=Button((s+1)+" "+(player.Team==Team.Red?"红":"蓝"),()=>SwitchSeat(s),"quiet-button","draft-seat-"+s);b.style.width=78;b.style.fontSize=18;seats.Add(b);}details.Add(seats);
                }
                AddReroll(details,opening,278);
            }
            else if(physicalCoin!=null && (!physicalCoin.Complete || opening.Status=="stuck"))
            {
                coinImage=new Image{name="opening-coin",image=physicalCoin.Texture,pickingMode=PickingMode.Ignore,scaleMode=ScaleMode.ScaleToFit};coinImage.style.position=Position.Absolute;coinImage.style.left=0;coinImage.style.right=0;coinImage.style.top=0;coinImage.style.bottom=0;root.Add(coinImage);
                if(opening.Status=="stuck"){var holder=new VisualElement();Place(holder,Screen.width-370,Screen.height*.4f,345,200);root.Add(holder);AddReroll(holder,opening,0);}
            }
        }
        private void AddReroll(VisualElement parent,MatchOpening opening,float top)
        {
            if(opening.Status!="stuck")return;
            var missing=Enumerable.Range(0,4).Except(renderedView.ConnectedSeats).ToList();
            string label=missing.Count>0?"等待席位 "+string.Join("、",missing.Select(s=>(s+1).ToString()))+" 重连":"硬币未落定 · 同意重投 "+opening.RerollVotes.Count+"/4";
            var vote=Button(label,()=>Submit(CommandKind.VoteCoinReroll,opening.TossId),"button","coin-reroll");Place(vote,0,top,326,66);vote.style.fontSize=18;vote.style.whiteSpace=WhiteSpace.Normal;
            vote.SetEnabled(missing.Count==0 && !opening.RerollVotes.Contains(seat) && NetworkCanAct);parent.Add(vote);
        }
        private string OpeningPrompt()
        {
            var op=renderedView.Opening!;
            if(op.Status=="throwing")return NetworkMode && !renderedView.ConnectedSeats.Contains(op.HostSeat)?"等待房主重连，继续本次投币。":"投掷决策币，确定先选队伍。";
            if(op.Status=="stuck")return "硬币尚未落定，四位玩家同意后重投。";
            int count=renderedView.Players.Count(p=>p.HeroId!=null);int left=count==1?2:1;
            return (renderedView.DraftTeam==Team.Red?"红队":"蓝队")+"选择 "+left+" 名英雄 · 同队先确认者先选";
        }
        private static Label OpeningLabel(string text,int size,Color color){var label=new Label(text);label.style.fontSize=size;label.style.color=color;label.style.whiteSpace=WhiteSpace.Normal;return label;}
        private static void Place(VisualElement element,float x,float y,float width,float height){element.style.position=Position.Absolute;element.style.left=x;element.style.top=y;element.style.width=width;element.style.height=height;}
        private static string HeroOverview(string id)=>id switch{
            "wasp"=>"电能与念力\n\n操纵单位位置，连锁施加弃牌压力。擅长隔空干扰与重新组织战场。",
            "shargatha"=>"石化与幻化\n\n限制敌方移动，用反击与弃牌连动制造威胁。幻化可穿过障碍。",
            "brogan"=>"重甲与战线\n\n坚守前线、保护队友与小兵。以重击推动敌人，并直接影响小兵战斗。",
            "arien"=>"潮汐与决斗\n\n以灵活位移改变进攻角度，兼具决斗防御、水流推动与战区控制。",
            "tigerclaw"=>"暗影与匕首\n\n灵活切入、隐蔽防御与毒素干扰。免疫状态下能连续调整位置并攻击不同目标。",
            "sabina"=>"远射与枪械\n\n从合适距离施压，用推动与弃牌惩罚敌人。选择射线和站位十分重要。",
            _=>"查看该英雄的卡牌与战斗能力。"};
    }
}

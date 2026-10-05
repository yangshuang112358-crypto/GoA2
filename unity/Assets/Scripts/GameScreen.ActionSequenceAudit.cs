#nullable enable
using System;
using System.Collections;
using System.IO;
using System.Linq;
using Goa2.Domain;
using Goa2.Infrastructure.Scenarios;
using Goa2.Presentation.UI3D;
using UnityEngine;
using UnityEngine.UIElements;
namespace Goa2.Presentation
{
    public sealed partial class GameScreen
    {
        private IEnumerator AuditActionSequence(string output,BoardAuditReport report)
        {
            void Check(bool condition,string text){if(!condition)throw new InvalidOperationException(text);report.Checks.Add(text);}
            ScenarioRunner Fixture(string name)=>new ScenarioRunner(catalog,ScenarioRunner.Load(File.ReadAllText(Path.GetFullPath(Path.Combine(UnityEngine.Application.dataPath,"../../tests/scenarios/"+name+".json")))));
            void Take(ScenarioRunner r,int count){for(int i=0;i<count;i++)Check(r.Next().Passed,"Rule fixture step "+i);session=r.Session;seat=0;Render();}
            yield return null;var runner=Fixture("throwing-axe-reflection");Take(runner,9);
            rightExpanded=false;cameraFollow=false;CloseHeroWheel();Render();yield return new WaitForSecondsRealtime(2);
            Check(root.Q<ScrollView>("revealed-zone")==null&&actionRail!=null,"Floating rail is not a window ScrollView");
            Check(root.Query<ActionSlab>().ToList().Count==4,"Four original compact cards");
            Check(actionRail!.worldBound.width<=296,"Rail occupies at most 296px");
            foreach(var n in renderedView.ActionSequence.Cards)
            {
                var slab=root.Q<ActionSlab>("revealed-seat-"+(n.Seat+1));
                Check(slab.Q<Label>(className:"action-card-rules")==null && slab.Q<Label>(className:"action-card-result")==null,"No effects or results on compact stone "+n.CardId);
                Check(slab.worldBound.height<=114 && slab.Q("action-portrait-"+n.Id)!=null && slab.Q("action-skill-"+n.Id)!=null,"Compact portrait and skill icon "+n.CardId);
                foreach(var number in slab.Query<VisualElement>(className:"action-number").ToList())Check(number.worldBound.xMax<=slab.worldBound.xMax,"Numbers fit compact stone "+n.CardId);
            }
            ScreenCapture.CaptureScreenshot(Path.Combine(output,"compact-cards.png"));yield return new WaitForSecondsRealtime(.25f);
            var first=root.Query<ActionSlab>().ToList().First();var firstNode=renderedView.ActionSequence.Cards.First();
            string beforeHover=session.ExportSave();
            using(var enter=PointerEnterEvent.GetPooled(new Event{type=EventType.MouseMove,mousePosition=first.worldBound.center})){enter.target=first;first.SendEvent(enter);}
            yield return new WaitForSecondsRealtime(.4f);
            Check(cardPreview!=null && root.Q<Label>("card-preview-rules").text==CardTextMarkup.Format(CardTextMarkup.Description(catalog.Card(firstNode.CardId))),"Hover opens full canonical card");
            Check(cardPreview!.worldBound.x>=first.worldBound.xMax && cardPreview.worldBound.yMax<=Screen.height,"Hover detail beside rail fits screen");
            Check(session.ExportSave()==beforeHover,"Hover never changes game state");
            ScreenCapture.CaptureScreenshot(Path.Combine(output,"hover-full-card.png"));yield return new WaitForSecondsRealtime(.25f);
            using(var leave=PointerLeaveEvent.GetPooled(new Event{type=EventType.MouseMove,mousePosition=new Vector2(1000,90)})){leave.target=first;first.SendEvent(leave);}
            yield return null;Check(cardPreview==null,"Leaving stone closes full card");
            float zoom=board3DViewport.Zoom;
            using(var e=WheelEvent.GetPooled(new Event{type=EventType.ScrollWheel,delta=new Vector2(0,5),mousePosition=new Vector2(120,230)})){e.target=actionRail;actionRail!.SendEvent(e);}
            yield return new WaitForSecondsRealtime(.5f);Check(actionRail!.FreeBrowsing&&actionRail.ScrollOffset>30,"Wheel scrolls stone rail");Check(board3DViewport.Zoom==zoom,"Stone wheel does not zoom battlefield");
            float offset=actionRail.ScrollOffset;Render();yield return new WaitForSecondsRealtime(.4f);Check(actionRail.FreeBrowsing&&Mathf.Abs(actionRail.ScrollOffset-offset)<4,"Ordinary Render preserves free browsing");
            Take(runner,1);yield return new WaitForSecondsRealtime(.8f);Check(!actionRail.FreeBrowsing,"Next actual main action ends free browsing");
            Take(runner,7);yield return new WaitForSecondsRealtime(1.5f);
            var trace=renderedView.ActionSequence;var defense=trace.Cards.Single(n=>n.Role=="defense");
            Check(root.Q<ActionSlab>("action-node-"+defense.Id)?.Notched==false,"Defense slab has straight top and bottom");
            Take(runner,2);yield return new WaitForSecondsRealtime(1.5f);trace=renderedView.ActionSequence;
            Check(trace.Cards.Single(n=>n.CardId=="brogan-06-铜墙铁壁").ParentId==defense.Id,"Actual forced discard nested under defense");
            var defenderSlab=root.Q<ActionSlab>("action-node-"+defense.Id);
            actionRail.ScrollBy(defenderSlab.worldBound.y-125);yield return new WaitForSecondsRealtime(.7f);
            ScreenCapture.CaptureScreenshot(Path.Combine(output,"nested-response.png"));yield return new WaitForSecondsRealtime(.25f);
            runner=Fixture("counterattack-nested");Take(runner,23);yield return new WaitForSecondsRealtime(1);
            Check(renderedView.ActionSequence.Cards.Count(n=>n.Role=="reaction")==2,"Two real counterattacks create distinct root cards");
            foreach(var n in renderedView.ActionSequence.Cards.Where(n=>n.Role=="reaction"))Check(root.Q<ActionSlab>("action-node-"+n.Id)?.Notched==false,"Extra root has no main-card notch");
            ScreenCapture.CaptureScreenshot(Path.Combine(output,"counterattack.png"));yield return new WaitForSecondsRealtime(.25f);
            runner=Fixture("action-sequence-four-tie");Take(runner,6);yield return new WaitForSecondsRealtime(1.2f);
            Check(actionRail!.CoinCount==3,"Four pending tied main cards have three group coins");
            string save=session.ExportSave();var coin=root.Q<ActionGroupCoin>();coin.Spin();yield return new WaitForSecondsRealtime(.15f);
            Check(Mathf.Abs(coin.Velocity)>0&&session.ExportSave()==save,"Random coin interaction changes no authoritative state");
            var target=root.Q<ActionSlab>("revealed-seat-3");actionRail.ScrollBy(target.worldBound.y-135);yield return new WaitForSecondsRealtime(.6f);
            using(var e=PointerUpEvent.GetPooled(new Event{type=EventType.MouseUp,button=0,mousePosition=target.worldBound.center})){e.target=target;target.SendEvent(e);}
            yield return new WaitForSecondsRealtime(1.2f);
            Check(renderedView.ActiveSeat==2&&renderedView.ActionSequence.Cards[0].Seat==2,"Captain chooses real next actor by clicking stone");
            Check(actionRail.CoinCount==2&&!actionRail.FreeBrowsing,"Actor detaches, two coins remain and focus resets");
            Check(root.Query<ActionSlab>().ToList().All(s=>s.worldBound.yMax<=Screen.height),"Four compact main cards fit after selection");
            ScreenCapture.CaptureScreenshot(Path.Combine(output,"same-initiative.png"));yield return new WaitForSecondsRealtime(.25f);
            // Rendering stress fixture only: all canonical descriptions are measured at this width.
            var actual=renderedView;int count=0;
            foreach(var card in catalog.Cards)
            {
                var copy=new GameView{MatchId="text-audit",Revision=count,Players=actual.Players,ActionSequence=new ActionSequenceView{Id="text",FocusId="text"}};
                copy.ActionSequence.Cards.Add(new ActionCardView{Id="text",CardId=card.Id,Seat=0,Started=true});
                BuildActionSequence(root,copy);yield return null;yield return null;
                var compact=root.Q<ActionSlab>("action-node-text");
                if(compact==null)compact=root.Q<ActionSlab>("revealed-seat-1");
                ShowActionCardPreview(compact,card,copy.Players[0],copy.ActionSequence.Cards[0]);yield return null;yield return null;
                var text=root.Q<Label>("card-preview-rules");
                Check(text!=null&&text.text==CardTextMarkup.Format(CardTextMarkup.Description(card))&&cardPreview!.worldBound.yMax<=Screen.height && text.worldBound.yMax<cardPreview.worldBound.yMax,"Unabridged hover layout "+card.Id);
                HideCardPreview();count++;
            }
            Check(count==108,"All 108 canonical descriptions rendered without excerpting");
            renderedView=actual;Render();yield return new WaitForSecondsRealtime(.4f);
            File.WriteAllText(Path.Combine(output,"action-sequence.json"),JsonUtility.ToJson(renderedView.ActionSequence,true));
        }
    }
}

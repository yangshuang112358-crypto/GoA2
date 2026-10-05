#nullable enable
using System;
using System.Collections;
using System.IO;
using System.Linq;
using Goa2.Domain;
using Goa2.Presentation.UI3D;
using UnityEngine;
namespace Goa2.Presentation
{
    public sealed partial class GameScreen
    {
        private void PrepareCombatDemo()
        {
            NewMatch();Submit(CommandKind.DebugPrepare,"sabina,wasp,brogan,arien");
            bool Free(Hex cell)=>catalog.Cell(cell)?.Obstacle==false && !renderedView.Units.Any(u=>u.Position==cell);
            var anchor=catalog.Cells.Where(c=>Free(c.Position) && c.Position.Neighbors().Count(Free)>=4).OrderBy(c=>c.Position.Distance(new Hex(0,3))).First().Position;
            var adjacent=anchor.Neighbors().Where(Free).ToList();Submit(CommandKind.DebugTeleport,"hero:1",destination:anchor);Submit(CommandKind.DebugTeleport,"hero:0",destination:adjacent[0]);
            var units=renderedView.Units.ToList();
            Submit(CommandKind.DebugTeleport,units.First(u=>u.Kind=="melee" && u.Team==Team.Blue).Id,destination:adjacent[1]);
            Submit(CommandKind.DebugTeleport,units.First(u=>u.Kind=="heavy" && u.Team==Team.Blue).Id,destination:adjacent[2]);
            Submit(CommandKind.DebugTeleport,units.First(u=>u.Kind=="melee" && u.Team==Team.Red).Id,destination:adjacent[3]);
            var bow=catalog.Cells.Where(c=>Free(c.Position)&&c.Position.Distance(anchor)==2).First().Position;
            Submit(CommandKind.DebugTeleport,units.First(u=>u.Kind=="ranged" && u.Team==Team.Blue).Id,destination:bow);
            rightExpanded=false;cameraFollow=false;board3DViewport.StopFollowing();board3DViewport.Focus=Board3DGeometry.World(anchor);board3DViewport.Zoom=3.3f;
            notice="战斗演示：在设置→调试中使用攻击，点黄蜂；切到黄蜂选择防御或不防御，可看小兵动作、击败金币及助攻飞行。";Render();
        }
        private IEnumerator AuditCombatPresentation(string output,BoardAuditReport report)
        {
            void Check(bool ok,string label){if(!ok)throw new InvalidOperationException(label);report.Checks.Add(label);}
            PrepareCombatDemo();yield return new WaitForSecondsRealtime(5.5f);Submit(CommandKind.DebugAttack,"hero:1|9");
            yield return new WaitForSecondsRealtime(7);Check(renderedView.Pending?.Kind=="defense","Actual core attack awaits defender");
            ScreenCapture.CaptureScreenshot(Path.Combine(output,"minions-ready.png"));yield return new WaitForSecondsRealtime(.3f);
            seat=1;Render();Submit(CommandKind.Defend,renderedView.DefenseOptions.OrderBy(d=>d.Assessment.FinalDefense).First().CardId);
            var timeline=board3DViewport.Presentation.Combat;Check(timeline.Shots.Count==1,"One strike from public defense result");var shot=timeline.Shots.Single();
            Check(shot.Support.Any(u=>u.Kind=="ranged") && shot.Support.Any(u=>u.Kind=="melee") && shot.Support.Any(u=>u.Kind=="heavy"),"Three supporting minion kinds captured");
            Check(timeline.Rewards.Count==2 && timeline.Rewards.Sum(r=>r.Amount)==2,"Killer and assist amounts read from actual core events");
            Check(renderedView.Players[0].Gold==1 && timeline.VisibleGold(0,1,Time.realtimeSinceStartup)==0,"Rules award immediately; visible meter waits");
            while(Time.realtimeSinceStartup<shot.Start+.25f)yield return null;
            ScreenCapture.CaptureScreenshot(Path.Combine(output,"minions-strike.png"));yield return new WaitForSecondsRealtime(.1f);
            Check(board!.Scene!.Camera.transform.parent.GetComponentsInChildren<Transform>().Any(t=>t.name.StartsWith("defeated visual ghost")),"Removed target has noninteractive impact ghost");
            while(Time.realtimeSinceStartup<shot.Impact+1)yield return null;
            Check(rewardWorld!=null && rewardWorld.Count==2,"Both actual reward coins spawned in isolated physics");ScreenCapture.CaptureScreenshot(Path.Combine(output,"reward-coins.png"));yield return new WaitForSecondsRealtime(.2f);
            while(Time.realtimeSinceStartup<shot.Impact+3.45f)yield return null;
            ScreenCapture.CaptureScreenshot(Path.Combine(output,"reward-homing.png"));yield return new WaitForSecondsRealtime(.6f);
            Check(rewardWorld!.Count==0 && timeline.VisibleGold(0,1,Time.realtimeSinceStartup)==1,"Coins finish and visible experience catches up");
            Check(renderedView.Players[0].Level==1,"Reward animation never advances true level");
            yield return new WaitForSecondsRealtime(4);ScreenCapture.CaptureScreenshot(Path.Combine(output,"reward-finished.png"));yield return new WaitForSecondsRealtime(.3f);
        }
    }
}

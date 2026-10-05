#nullable enable
using System;
using System.Collections;
using System.IO;
using System.Linq;
using Goa2.Domain;
using Goa2.Presentation.UI3D;
using UnityEngine;
using UnityEngine.UIElements;
namespace Goa2.Presentation
{
 public sealed partial class GameScreen
 {
  private IEnumerator AuditUIRevision(string output,BoardAuditReport report)
  {
   void Check(bool ok,string label){if(!ok)throw new InvalidOperationException(label);report.Checks.Add(label);}
   yield return null;PrepareFourTieDemo();yield return new WaitForSecondsRealtime(5.5f);
   Check(renderedView.Phase==Phase.Planning && seat==3,"Manual four-tie fixture begins before reveal");
   Check(guideInstructions.Contains("静电封锁"),"Manual instructions describe final selection and both captains");
   Submit(CommandKind.SelectCard,"wasp-06-静电封锁",3);yield return null;
   Check(renderedView.Phase==Phase.InitiativeChoice && InitiativePresenting,"Fourth selection triggers live coin explanation");
   var beat=board3DViewport.Presentation.Conflict(Time.realtimeSinceStartup)!;
   Check(beat.Winner==Team.Blue && renderedView.DecisionCoin==Team.Red,"UI explains winner while authority already holds next colour");
   while(Time.realtimeSinceStartup<beat.Flip-.8f)yield return null;
   Check(root.Q<Label>("initiative-explanation")?.text.Contains("蓝队先行")==true,"Winner explanation stays visible before half flip");
   ScreenCapture.CaptureScreenshot(Path.Combine(output,"four-tie-before-flip.png"));yield return new WaitForSecondsRealtime(.2f);
   while(Time.realtimeSinceStartup<beat.Flip+.55f)yield return null;
   ScreenCapture.CaptureScreenshot(Path.Combine(output,"four-tie-after-flip.png"));
   while(InitiativePresenting)yield return null;yield return new WaitForSecondsRealtime(5.5f);
   seat=0;Render();Submit(CommandKind.ChooseInitiative,target:2);yield return new WaitForSecondsRealtime(5.5f);
   Check(renderedView.ActiveSeat==2,"Blue captain selects teammate to act first");
   seat=2;Render();Check(renderedView.FastMoves.Count>0,"Four-tie fixture supplies real fast-move destinations");
   PickAction("fast");var moves=LegalCells(renderedView).ToArray();string before=session.ExportSave();
   chosenCell=moves.Last();Render();yield return null;yield return null;string selected=DecisionSelectionKey();board!.ManualPan?.Invoke();board.ZoomAtCenter(.8f);board.EmptyClick?.Invoke();yield return null;
   Check(DecisionSelectionKey()==selected && LegalCells(renderedView).SequenceEqual(moves),"Fast-move selection survives camera changes and empty click");
   ScreenCapture.CaptureScreenshot(Path.Combine(output,"fast-move-free-camera.png"));yield return new WaitForSecondsRealtime(.3f);
   ReturnMainFlow();Check(session.ExportSave()==before && actionChoice=="" && chosenCell==null,"Main-flow return resets UI without executing a move");
   Submit(CommandKind.Pass);
   File.WriteAllText(Path.Combine(output,"tie-presentation.txt"),"now="+Time.realtimeSinceStartup+"\n"+string.Join("\n",board3DViewport.Presentation.CoinConflicts.Select(c=>c.Sequence+" "+c.Winner+" "+c.Start+" "+c.Flip+" "+c.End))+"\n"+string.Join("\n",renderedView.Events.Where(e=>e.Kind=="DecisionCoinFlipped").Select(e=>e.Sequence+" "+e.Detail)));
   yield return null;
   File.WriteAllText(Path.Combine(output,"tie-after-pass.json"),session.ExportSave());
   Check(board3DViewport.Presentation.Conflict(Time.realtimeSinceStartup)?.Winner==Team.Red,"Next unresolved tie now gives red team priority; "+notice+" phase="+renderedView.Phase+" seat="+seat+" pending="+renderedView.Pending?.Kind);
   while(InitiativePresenting)yield return null;
   yield return new WaitForSecondsRealtime(5.5f);seat=1;Render();Submit(CommandKind.ChooseInitiative,target:3);
   Check(renderedView.ActiveSeat==3,"Red captain can choose yellow Wasp next");
   PrepareCombatDemo();yield return new WaitForSecondsRealtime(5.5f);
   Submit(CommandKind.DebugSetGold,"5",0);yield return null;
   var plate=root.Q<HeroPlate>("hero-plate-1");Check(plate.Q<Label>("level-preview").text=="1→3","Entire level forecast is inside larger circular boss");
   Check(renderedView.Players[0].Level==1 && renderedView.Players[0].Gold==5,"Level meter never changes authoritative level or spends gold");
   cameraFollow=false;mainFlow=false;browsingWheels.Clear();wheelSeat=null;stageStarted=-100;stageWaiting=false;Render();
   var all=board!.Scene!.Camera.transform.parent.GetComponentsInChildren<Transform>();
   var coin=all.Single(t=>t.name=="decision coin");
   var bounds=coin.GetComponentsInChildren<Renderer>().Select(r=>r.bounds).Aggregate((a,b)=>{a.Encapsulate(b);return a;});
   Check(Mathf.Abs(Mathf.Max(bounds.size.x,bounds.size.z)-Board3DScene.DecisionCoinDiameter)<.02f,"Board coin spans 2.08 m, fitting tray inner diameter");
   Check(bounds.min.y>=1.135f,"Coin underside rests above stone surface");
   var stone=all.Where(t=>t.name.StartsWith("central spiral half")).Select(t=>(t,t.position,t.rotation,t.localScale)).ToList();
   yield return new WaitForSecondsRealtime(1.5f);
   Check(stone.Count==2 && stone.All(s=>s.t.position==s.position && s.t.rotation==s.rotation && s.t.localScale==s.localScale),"Central stone halves remain completely static");
   board3DViewport.Focus=(Board3DGeometry.World(new Hex(0,0))+Board3DGeometry.World(new Hex(0,1)))*.5f;board3DViewport.Zoom=4;yield return new WaitForSecondsRealtime(.5f);
   ScreenCapture.CaptureScreenshot(Path.Combine(output,"coin-static-platform.png"));yield return new WaitForSecondsRealtime(.3f);
   foreach(string pose in new[]{"idle","support","guard"}){
    board3DViewport.MinionPreviewPose=pose;Render();yield return new WaitForSecondsRealtime(2.3f);
    foreach(string kind in new[]{"melee","ranged","heavy"}){
     var unit=renderedView.Units.First(u=>u.Team==Team.Blue && u.Kind==kind);board3DViewport.Focus=Board3DGeometry.World(unit.Position)+Vector3.up*.8f;board3DViewport.Zoom=6;yield return new WaitForSecondsRealtime(.4f);
     var model=board!.Scene!.Camera.transform.parent.GetComponentsInChildren<Transform>().Single(t=>t.name=="minion "+kind+" "+unit.Id);
     Transform Bone(string n)=>model.GetComponentsInChildren<Transform>().Single(t=>t.name==n);
     File.AppendAllText(Path.Combine(output,"pose-landmarks.txt"),kind+" "+pose+" R="+model.InverseTransformPoint(Bone("Hand.R").position)+" L="+model.InverseTransformPoint(Bone("Hand.L").position)+"\n");
     if(kind=="ranged"){
      var line=board.Scene.Camera.transform.parent.GetComponentsInChildren<LineRenderer>().Single(l=>l.name=="bow string "+unit.Id);
      Check(Vector3.Distance(line.GetPosition(0),Bone("BowTip.Bottom").position)<.001f && Vector3.Distance(line.GetPosition(2),Bone("BowTip.Top").position)<.001f,"Real bow-tip string endpoints stay attached in "+pose);
     }
     ScreenCapture.CaptureScreenshot(Path.Combine(output,"pose-"+kind+"-"+pose+".png"));yield return new WaitForSecondsRealtime(.2f);
     if(pose=="support"){board.Rotate(3);yield return new WaitForSecondsRealtime(.5f);ScreenCapture.CaptureScreenshot(Path.Combine(output,"pose-"+kind+"-side.png"));yield return new WaitForSecondsRealtime(.2f);board.Rotate(-3);yield return new WaitForSecondsRealtime(.3f);}
    }
   }
   board3DViewport.MinionPreviewPose="";Render();
  }
 }
}

#nullable enable
using System;
using System.Collections;
using System.IO;
using System.Linq;
using Goa2.Domain;
using Goa2.Presentation.UI3D;
using Goa2.Infrastructure.Scenarios;
using UnityEngine;
using UnityEngine.UIElements;

namespace Goa2.Presentation
{
    public sealed partial class GameScreen
    {
        private IEnumerator AuditWorldDecisions(string output,BoardAuditReport report)
        {
            void Check(bool value,string label){if(!value)throw new InvalidOperationException(label);report.Checks.Add(label);}
            void Load(string name,int count)
            {
                var args=Environment.GetCommandLineArgs();int index=Array.IndexOf(args,"-goaAuditScenarios");
                string folder=index>=0 && index+1<args.Length ? args[index+1] : Path.GetFullPath(Path.Combine(UnityEngine.Application.dataPath,"../../tests/scenarios"));
                var runner=new ScenarioRunner(catalog,ScenarioRunner.Load(File.ReadAllText(Path.Combine(folder,name+".json"))));
                for(int i=0;i<count;i++)Check(runner.Next().Passed,"Fixture "+name+" step "+i);
                session=runner.Session;seat=0;ClearPending();wheelState.Discards.Clear();worldContext="";wheelContext="";cameraFollow=true;mainFlow=true;browsingWheels.Clear();rightExpanded=false;Render();
            }
            yield return null;Load("combat-defense",7);yield return new WaitForSecondsRealtime(9);
            Check(root.Q("action-wheel")!=null && root.Q("operation-panel").resolvedStyle.display==DisplayStyle.None,"Action choices on hero with settings closed");
            Check(root.Q("operation-panel").Q("begin-primary")==null,"No old action duplicate in settings");
            var guide=root.Q("main-flow-status");
            Check(guide.worldBound.xMax<=Screen.width && guide.worldBound.yMax>=Screen.height-16 && guide.worldBound.width<=282,"Compact guide sits at bottom-right");
            Check(root.Q("follow-toggle")==null && root.Q("follow-toast")==null && root.Q("world-instruction")==null,"One guide replaces old follow button and bottom hint");
            Check(!root.Q<Label>(className:"flow-summary").text.Contains("（"),"Summary omits seat suffix");
            var firstStone=root.Query<ActionSlab>().ToList().OrderBy(v=>v.worldBound.yMin).First();
            Check(Mathf.Abs(firstStone.worldBound.yMin-firstStone.worldBound.xMin)<4,"First stone top margin matches left margin");
            Check(firstStone.worldBound.width<=280 && firstStone.worldBound.height<=114 && root.Q(className:"action-team-stripe")==null,"Compact action slabs omit bottom team stripe");
            ScreenCapture.CaptureScreenshot(Path.Combine(output,"action-wheel.png"));yield return new WaitForSecondsRealtime(.2f);
            string before=session.ExportSave();
            ToggleHeroWheel(1);ToggleHeroWheel(2);yield return new WaitForSecondsRealtime(1);
            Check(!mainFlow && !cameraFollow && browsingWheels.Contains(1) && browsingWheels.Contains(2),"Multiple inspection rings coexist without follow");
            Check(root.Q("skill-wheel-anchor-1")!=null && root.Q("skill-wheel-anchor-2")!=null,"Both inspection rings rendered");
            Check(root.Q(className:"flow-return")!=null && root.Q("action-wheel")==null,"Free mode offers return and hides operation ring");
            Render();Check(browsingWheels.Count>=2 && !mainFlow,"UI refresh preserves free browsing");
            ScreenCapture.CaptureScreenshot(Path.Combine(output,"free-multiple-rings.png"));yield return new WaitForSecondsRealtime(.3f);
            ReturnMainFlow();ReturnMainFlow();yield return new WaitForSecondsRealtime(.5f);
            Check(mainFlow && cameraFollow && browsingWheels.Count==0 && root.Q("action-wheel")!=null,"Return clears inspected rings and restores actor choices");
            Check(session.ExportSave()==before,"Browsing and returning do not mutate rules");
            PickAction("primary");yield return null;
            Check(renderedView.PrimaryPreview?.Kind=="attack_target" && LegalCells(renderedView).Count>0,"Authoritative attack preview before BeginPrimary");
            Check(session.ExportSave()==before && root.Q("action-wheel")==null,"Entering targeting leaves entire state unchanged and closes ring");
            ReturnWorldChoice();Check(session.ExportSave()==before && root.Q("action-wheel")!=null,"Return restores action ring without command");
            PickAction("secondary");Check(LegalCells(renderedView).Count>0,"Secondary movement destinations visible");
            var moves=LegalCells(renderedView).ToArray();chosenCell=moves.First();Render();string selection=DecisionSelectionKey();
            board!.ManualPan?.Invoke();board.ZoomAtCenter(1.1f);board.EmptyClick?.Invoke();yield return null;
            Check(DecisionSelectionKey()==selection && moves.SequenceEqual(LegalCells(renderedView)),"Pan zoom and empty click preserve pending movement and legal cells");
            Check(root.Q(className:"flow-return")!=null && root.Q<Button>("flow-confirm")?.enabledInHierarchy==true,"Free camera keeps move confirmation available");
            ReturnMainFlow();Check(actionChoice=="" && chosenCell==null && session.ExportSave()==before,"Return resets only local choice and camera");
            PickAction("secondary");ReturnWorldChoice();Check(session.ExportSave()==before,"Movement cancellation is read-only");
            PickAction("primary");chosenCell=renderedView.Units.Single(u=>u.Id=="hero:1").Position;Render();yield return new WaitForSecondsRealtime(1);
            var confirm=root.Q<Button>("flow-confirm");Check(confirm!=null && confirm.enabledInHierarchy,"Map target has confirmation check");
            Check(confirm!.worldBound.xMin>Screen.width-250 && root.Q("flow-withdraw").worldBound.yMin>=confirm.worldBound.yMax,"Confirm and withdraw stacked at right edge");
            Check(confirm.worldBound.width<=181 && root.Q("flow-withdraw").worldBound.yMin-confirm.worldBound.yMax<=3,"Decision buttons are narrower with tightly stacked hit boxes");
            Check(confirm.Query<Label>().ToList().Count==0 && root.Q("flow-withdraw").Query<Label>().ToList().Count==0,"Lettering and symbols are baked into model, no overlaid labels");
            ScreenCapture.CaptureScreenshot(Path.Combine(output,"target-confirm.png"));yield return new WaitForSecondsRealtime(.2f);
            ConfirmCurrent();ConfirmCurrent();yield return new WaitForSecondsRealtime(.5f);Check(renderedView.Pending?.Kind=="defense" && renderedView.Revision>0,"Single confirmation begins actual defender response");
            Check(actionChoice=="" && root.Q("world-return")==null,"Cannot undo committed attack from response");
            seat=1;Render();yield return new WaitForSecondsRealtime(9);WheelPick("wasp-00-闪耀之刃");yield return new WaitForSecondsRealtime(.5f);
            string formula=AttackFormula(renderedView.Attack!);
            for(int viewer=0;viewer<4;viewer++){seat=viewer;Render();yield return null;Check(root.Q<Label>("public-attack-formula")?.text==formula,"Same public attack calculation for viewer "+viewer);}
            seat=1;Render();WheelPick("wasp-00-闪耀之刃");yield return new WaitForSecondsRealtime(.3f);
            Check(root.Q<Button>("flow-confirm")?.enabledInHierarchy==true,"Defender preselection exposes right confirmation");
            Check(root.Q(className:"flow-mine")!=null,"Defender sees personal red prompt");
            ScreenCapture.CaptureScreenshot(Path.Combine(output,"defense-dock.png"));yield return new WaitForSecondsRealtime(.3f);
            ConfirmCurrent();yield return new WaitForSecondsRealtime(9);
            Check(renderedView.Pending?.Kind=="hero_respawn","Defense completes and opens respawn");
            chosenCell=renderedView.RespawnCells.First();Render();yield return null;Check(root.Q("flow-confirm")!=null,"Respawn confirms on battlefield");ConfirmCurrent();yield return new WaitForSecondsRealtime(.5f);
            Check(renderedView.Phase==Phase.Action,"Respawn continues original card");
            Load("throwing-axe-reflection",10);yield return new WaitForSecondsRealtime(9);before=session.ExportSave();PickAction("primary");
            Check(renderedView.PrimaryPreview==null && session.ExportSave()==before,"Pre-discard card requires explicit start confirmation");
            ConfirmCurrent();yield return new WaitForSecondsRealtime(9);Check(renderedView.Pending?.Kind=="optional_discard","Confirmed start enters pre-discard ring");
            WheelPick("brogan-00-猛攻");WheelConfirm();yield return new WaitForSecondsRealtime(9);
            before=session.ExportSave();ReturnWorldChoice();Check(session.ExportSave()==before && renderedView.Pending?.Kind=="attack_target","Returning after discard cannot roll back paid card");
            Check(root.Q("begin-primary")==null,"Committed prelude cannot select a different action type");
            chosenCell=renderedView.Units.Single(u=>u.Id=="hero:1").Position;worldOptionsOpen=false;Render();ConfirmCurrent();yield return new WaitForSecondsRealtime(9);
            seat=1;Render();WheelPick("wasp-10-反射屏障");WheelConfirm();yield return new WaitForSecondsRealtime(9);
            seat=0;Render();WheelPick("brogan-06-铜墙铁壁");WheelConfirm();yield return new WaitForSecondsRealtime(9);
            Check(renderedView.ActionSequence.Cards.Any(c=>c.CardId=="brogan-06-铜墙铁壁"),"Nested response remains present after battlefield-only choices");
            ScreenCapture.CaptureScreenshot(Path.Combine(output,"nested-after.png"));yield return new WaitForSecondsRealtime(.2f);
            Check(!rightExpanded,"Full attack, defense, discard and respawn did not open settings");
            Load("round-frontline",8);seat=1;Render();yield return new WaitForSecondsRealtime(2);
            for(int i=0;i<3;i++)
            {
                chosenCell=renderedView.Units.First(u=>renderedView.RoundMinionRemovals.Contains(u.Id)).Position;Render();
                Check(root.Q("flow-confirm")!=null,"Captain minion removal check "+i);ConfirmCurrent();yield return new WaitForSecondsRealtime(.5f);
            }
            Check(renderedView.Pending?.Kind=="minion_spawn","Captain advances into blocked spawn choice");
            chosenCell=LegalCells(renderedView).First();Render();ConfirmCurrent();yield return new WaitForSecondsRealtime(.5f);
            seat=0;Render();yield return null;Check(renderedView.UpgradeOptions.Count>0,"Spawn completion resumes upgrades");
            int upgrades=0;
            while(renderedView.UpgradeOptions.Count>0 && upgrades++<5)
            {
                upgradeCardId=renderedView.UpgradeOptions.First().CardId;Render();yield return null;yield return null;
                Check(confirmButton!=null && root.Q("decision-dock").Contains(confirmButton),"Upgrade confirmation uses right dock");
                Check(root.Q("upgrade-wheel")!=null && root.Query<VisualElement>().ToList().Count(e=>e.name.StartsWith("upgrade-card-"))==renderedView.UpgradeOptions.Count && root.Query<VisualElement>().ToList().Count(e=>e.name.StartsWith("upgrade-current-"))==renderedView.UpgradeOptions.Select(o=>o.Color).Distinct().Count(),"Upgrade ring contains exactly projected routes and their current cards");
                yield return new WaitForSecondsRealtime(9);
                Check(root.Query<VisualElement>().ToList().Count(e=>e.name.StartsWith("upgrade-arc-"))==renderedView.UpgradeOptions.Count,"Every non-purple upgrade route has a curved direction arrow"); Check(root.Q<Label>("upgrade-benefit").text.Contains(renderedView.UpgradeOptions.First().Bonus),"Selected route shows authoritative permanent bonus"); ScreenCapture.CaptureScreenshot(Path.Combine(output,"upgrade-layout-"+upgrades+".png"));yield return new WaitForSecondsRealtime(.2f);
                if(upgrades==1){
                    var option=renderedView.UpgradeOptions.First();var owner=root.Q("upgrade-card-"+option.CardId);
                    ShowUpgradeInfo(catalog.Card(option.PreviousCardId),catalog.Card(option.CardId),false,owner,owner.worldBound.center);
                    yield return new WaitForSecondsRealtime(.5f);
                    ScreenCapture.CaptureScreenshot(Path.Combine(output,"upgrade-comparison.png"));yield return new WaitForSecondsRealtime(.2f);HideSkillInfo();
                }
                ConfirmCurrent();yield return new WaitForSecondsRealtime(.5f);
            }
            Check(renderedView.Phase==Phase.Planning && !rightExpanded,"Minion battle, spawn and upgrades reach next round without settings");
            ReturnMainFlow();yield return new WaitForSecondsRealtime(.4f);
            ToggleHeroWheel(1);ReturnMainFlow();yield return null;
            Check(mainFlow && wheelSeat==seat,"Returning during hidden planning restores own ring");
            Check(root.Q("skill-wheel-anchor-1")==null,"Inspected opponent ring closed on return to planning");
            ScreenCapture.CaptureScreenshot(Path.Combine(output,"planning-return.png"));yield return new WaitForSecondsRealtime(.3f);
            Load("combat-defense",7);yield return new WaitForSecondsRealtime(.5f);PickAction("pass");
            before=session.ExportSave();AnimateDecision(()=>Submit(CommandKind.Pass));WithdrawPreview();Render();
            yield return new WaitForSecondsRealtime(.5f);Check(session.ExportSave()==before,"Withdrawing during exit animation cancels stale confirmation");
            ReturnMainFlow();PickAction("primary");WithdrawPreview();Check(actionChoice=="" && session.ExportSave()==before,"Withdraw restores choices without rule mutation");
            bool sent=false;AnimateDecision(()=>sent=true);chosenCell=new Hex(999,999);
            yield return new WaitForSecondsRealtime(.4f);Check(!sent,"Changing preselection during exit animation invalidates captured confirmation");
            ClearPending();Render();AnimateDecision(()=>sent=true);renderedView.Revision++;
            yield return new WaitForSecondsRealtime(.4f);Check(!sent,"Newer projected revision invalidates captured confirmation");
            Render();
            PreviewStageBanner("暗选阶段");var focusBefore=board3DViewport.Focus;
            yield return new WaitForSecondsRealtime(.35f);
            Check(root.Q("stage-banner")!=null && Vector3.Distance(focusBefore,board3DViewport.Focus)<.001f,"Stage banner precedes camera movement");
            ScreenCapture.CaptureScreenshot(Path.Combine(output,"stage-banner.png"));yield return new WaitForSecondsRealtime(StageBannerPolicy.Duration);
            Check(root.Q("stage-banner")==null,"Stage banner disappears after five-second presentation");
            OpenArtSamples(2);yield return new WaitForSecondsRealtime(.3f);
            Check(root.Q("art-samples")!=null,"Debug art gallery displays prepared Blender samples");
            ScreenCapture.CaptureScreenshot(Path.Combine(output,"atlantis-emblems-gallery.png"));yield return new WaitForSecondsRealtime(.2f);
            artSamplesOpen=false;Render();
            Load("round-upgrades",22);yield return new WaitForSecondsRealtime(9);
            Check(renderedView.UpgradeOptions.Count==1 && root.Q("upgrade-wheel")!=null && root.Query<VisualElement>().ToList().All(e=>!e.name.StartsWith("upgrade-current-")),"Level eight retains a single purple upgrade ring choice");
            upgradeCardId=renderedView.UpgradeOptions.Single().CardId;var purpleChoice=upgradeCardId;Render();yield return new WaitForSecondsRealtime(.5f);
            var purpleAnchor=root.Q("upgrade-zone");var purpleHero=renderedView.Units.Single(u=>u.Seat==seat);
            Check(Vector2.Distance(purpleAnchor.worldBound.center,board.LocalToWorld(board.ProjectHero(purpleHero.Position)))<4,"Purple upgrade ring remains centered on its hero after refresh");
            ScreenCapture.CaptureScreenshot(Path.Combine(output,"upgrade-purple.png"));yield return new WaitForSecondsRealtime(.2f);
            ConfirmCurrent();yield return new WaitForSecondsRealtime(.6f);
            Check(renderedView.Players.Single(p=>p.Seat==seat).PurpleCardId==purpleChoice,"Purple ring confirmation equips the chosen ultimate");
        }
    }
}

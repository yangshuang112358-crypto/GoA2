#nullable enable
using System;
using System.Collections;
using System.IO;
using System.Linq;
using Goa2.Domain;
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
                var runner=new ScenarioRunner(catalog,ScenarioRunner.Load(File.ReadAllText(Path.GetFullPath(Path.Combine(UnityEngine.Application.dataPath,"../../tests/scenarios/"+name+".json")))));
                for(int i=0;i<count;i++)Check(runner.Next().Passed,"Fixture "+name+" step "+i);
                session=runner.Session;seat=0;ClearPending();wheelState.Discards.Clear();worldContext="";wheelContext="";cameraFollow=true;rightExpanded=false;Render();
            }
            yield return null;Load("combat-defense",7);yield return new WaitForSecondsRealtime(3);
            Check(root.Q("action-wheel")!=null && root.Q("operation-panel").resolvedStyle.display==DisplayStyle.None,"Action choices on hero with settings closed");
            Check(root.Q("operation-panel").Q("begin-primary")==null,"No old action duplicate in settings");
            ScreenCapture.CaptureScreenshot(Path.Combine(output,"action-wheel.png"));yield return new WaitForSecondsRealtime(.2f);
            string before=session.ExportSave();PickAction("primary");yield return null;
            Check(renderedView.PrimaryPreview?.Kind=="attack_target" && LegalCells(renderedView).Count>0,"Authoritative attack preview before BeginPrimary");
            Check(session.ExportSave()==before && root.Q("action-wheel")==null,"Entering targeting leaves entire state unchanged and closes ring");
            ReturnWorldChoice();Check(session.ExportSave()==before && root.Q("action-wheel")!=null,"Return restores action ring without command");
            PickAction("secondary");Check(LegalCells(renderedView).Count>0,"Secondary movement destinations visible");ReturnWorldChoice();Check(session.ExportSave()==before,"Movement cancellation is read-only");
            PickAction("primary");chosenCell=renderedView.Units.Single(u=>u.Id=="hero:1").Position;Render();yield return new WaitForSecondsRealtime(1);
            var confirm=root.Q<Button>("world-confirm");Check(confirm!=null && confirm.enabledInHierarchy,"Map target has confirmation check");
            Check(Vector2.Distance(confirm!.worldBound.center,board!.PanelCenter(chosenCell.Value))<95,"Check stays beside selected target");
            ScreenCapture.CaptureScreenshot(Path.Combine(output,"target-confirm.png"));yield return new WaitForSecondsRealtime(.2f);
            ConfirmCurrent();yield return null;Check(renderedView.Pending?.Kind=="defense" && renderedView.Revision>0,"Single confirmation begins actual defender response");
            Check(actionChoice=="" && root.Q("world-return")==null,"Cannot undo committed attack from response");
            seat=1;Render();yield return new WaitForSecondsRealtime(3);WheelPick("wasp-00-闪耀之刃");WheelConfirm();yield return new WaitForSecondsRealtime(3);
            Check(renderedView.Pending?.Kind=="hero_respawn","Defense completes and opens respawn");
            chosenCell=renderedView.RespawnCells.First();Render();yield return null;Check(root.Q("world-confirm")!=null,"Respawn confirms on battlefield");ConfirmCurrent();yield return null;
            Check(renderedView.Phase==Phase.Action,"Respawn continues original card");
            Load("throwing-axe-reflection",10);yield return new WaitForSecondsRealtime(3);before=session.ExportSave();PickAction("primary");
            Check(renderedView.PrimaryPreview==null && session.ExportSave()==before,"Pre-discard card requires explicit start confirmation");
            ConfirmCurrent();yield return new WaitForSecondsRealtime(3);Check(renderedView.Pending?.Kind=="optional_discard","Confirmed start enters pre-discard ring");
            WheelPick("brogan-00-猛攻");WheelConfirm();yield return new WaitForSecondsRealtime(3);
            before=session.ExportSave();ReturnWorldChoice();Check(session.ExportSave()==before && renderedView.Pending?.Kind=="attack_target","Returning after discard cannot roll back paid card");
            Check(root.Q("begin-primary")==null,"Committed prelude cannot select a different action type");
            chosenCell=renderedView.Units.Single(u=>u.Id=="hero:1").Position;worldOptionsOpen=false;Render();ConfirmCurrent();yield return new WaitForSecondsRealtime(3);
            seat=1;Render();WheelPick("wasp-10-反射屏障");WheelConfirm();yield return new WaitForSecondsRealtime(3);
            seat=0;Render();WheelPick("brogan-06-铜墙铁壁");WheelConfirm();yield return new WaitForSecondsRealtime(3);
            Check(renderedView.ActionSequence.Cards.Any(c=>c.CardId=="brogan-06-铜墙铁壁"),"Nested response remains present after battlefield-only choices");
            ScreenCapture.CaptureScreenshot(Path.Combine(output,"nested-after.png"));yield return new WaitForSecondsRealtime(.2f);
            Check(!rightExpanded,"Full attack, defense, discard and respawn did not open settings");
            Load("round-frontline",8);seat=1;Render();yield return new WaitForSecondsRealtime(2);
            for(int i=0;i<3;i++)
            {
                chosenCell=renderedView.Units.First(u=>renderedView.RoundMinionRemovals.Contains(u.Id)).Position;Render();
                Check(root.Q("world-confirm")!=null,"Captain minion removal check "+i);ConfirmCurrent();yield return null;
            }
            Check(renderedView.Pending?.Kind=="minion_spawn","Captain advances into blocked spawn choice");
            chosenCell=LegalCells(renderedView).First();Render();ConfirmCurrent();yield return null;
            seat=0;Render();yield return null;Check(renderedView.UpgradeOptions.Count>0,"Spawn completion resumes upgrades");
            int upgrades=0;
            while(renderedView.UpgradeOptions.Count>0 && upgrades++<5)
            {
                upgradeCardId=renderedView.UpgradeOptions.First().CardId;Render();yield return null;yield return null;
                Check(confirmButton!=null && root.Q("upgrade-zone").Contains(confirmButton),"Upgrade confirmation belongs to card layer");
                Check(root.Query<VisualElement>(className:"upgrade-row").ToList().Count==3,"Six upgrade candidates retain three color rows");
                ConfirmCurrent();yield return null;
            }
            Check(renderedView.Phase==Phase.Planning && !rightExpanded,"Minion battle, spawn and upgrades reach next round without settings");
        }
    }
}

#nullable enable
using System;
using System.Collections;
using System.IO;
using System.Linq;
using Goa2.Domain;
using Goa2.Infrastructure;
using UnityEngine;
using UnityEngine.UIElements;

namespace Goa2.Presentation
{
    public sealed partial class GameScreen
    {
        private IEnumerator AuditTutorial(string output,BoardAuditReport report)
        {
            void Check(bool value,string name){if(!value){ScreenCapture.CaptureScreenshot(Path.Combine(output,"failure.png"));throw new InvalidOperationException(name);}report.Checks.Add(name);}
            void Click(string name){var element=root.Q<Button>(name);Check(element!=null && VisibleCenter(element),"Clickable "+name+" "+element?.worldBound+" picked="+(element==null?"missing":root.panel?.Pick(element.worldBound.center)?.name));using(var e=NavigationSubmitEvent.GetPooled()){e.target=element;element!.SendEvent(e);}}
            yield return null;yield return null;
            var previous=session;string previousSave=session.ExportSave();
            string hotseatBefore=File.Exists(SavePath)?File.ReadAllText(SavePath):"";
            OpenTutorialMenu();yield return null;yield return null;
            int from=0;var args=Environment.GetCommandLineArgs();int fi=Array.IndexOf(args,"-goaTutorialFrom");if(fi>=0)from=int.Parse(args[fi+1]);
            Check(root.Q("tutorial-menu")!=null,"Dedicated chapter entry");Click("tutorial-continue");if(from>0)StartTutorialChapter(from);
            for(int chapter=from;chapter<TutorialCourse.Chapters.Length;chapter++)
            {
                if(chapter>from)StartTutorialChapter(chapter);
                for(int guard=0;tutorial!=null && !tutorial.Complete && guard<70;guard++)
                {
                    yield return null;
                    float deadline=Time.realtimeSinceStartup+22;
                    while((TutorialPresenting || decisionAnimating || wheelState.Discards.Count>0) && Time.realtimeSinceStartup<deadline)yield return null;
                    Check(!TutorialPresenting && !decisionAnimating,"Presentation completes c"+chapter+" "+tutorial!.Line.Id);
                    yield return new WaitForSecondsRealtime(.35f);
                    // A paced bot can confirm the last card during layout settling.
                    // Re-evaluate after that transition instead of inspecting a hidden banner frame.
                    if(TutorialPresenting || decisionAnimating)continue;
                    var line=tutorial.Line.Id;
                    Check(seat==0,"Learner remains seat0 "+line);
                    Check(tutorial.Error=="", "Script healthy "+line);
                    var coach=root.Q("tutorial-coach");Check(coach!=null && coach.worldBound.xMin>=0 && coach.worldBound.xMax<=Screen.width+1,"Coach stays onscreen "+line);
                    if(line=="welcome" || line=="move-preview" || line=="attack-hero" || line=="defend" || line=="hand-bar" || line=="upgrade-pick" || line=="ultimate-pick" || line=="finish")
                    {
                        ScreenCapture.CaptureScreenshot(Path.Combine(output,line+".png"));yield return new WaitForSecondsRealtime(.3f);
                        if(TutorialPresenting || tutorial.Line.Id!=line)continue;
                    }
                    if(tutorial.Line.Reading){Click("tutorial-next");continue;}
                    if(line=="read-card")
                    {
                        ToggleHeroWheel(0);yield return new WaitForSecondsRealtime(.5f);
                        var disc=root.Q("skill-wheel-anchor-0")?.Q<Goa2.Presentation.UI3D.SkillDisc>("skill-red");
                        Check(disc!=null,"Read real learner red disc");disc!.Inspect?.Invoke(disc.worldBound.center);yield return null;
                        Check(root.Q("skill-description")!=null && tutorial.Line.Id=="move-preview","Actual inspection advances read step and keeps tooltip");
                        HideSkillInfo();ReturnMainFlow();continue;
                    }
                    if(line=="move-preview"){PickAction("secondary");continue;}
                    if(line=="move-cancel")
                    {
                        string before=session.ExportSave();BrowseCamera();Check(moveMode.HasValue,"Panning preserves move preview");
                        WithdrawPreview();Check(session.ExportSave()==before && tutorial.Line.Id=="move-commit","Cancel is readonly and advances gesture");continue;
                    }
                    if(line=="select-first"){WheelPick("sabina-00-近身射击");continue;}
                    if(line=="select-change" || line=="select-final"){WheelPick("sabina-01-拔枪");continue;}
                    if(line=="select-cancel"){WheelPick("sabina-01-拔枪");continue;}
                    if(line=="select-confirm")
                    {
                        if(renderedView.Players[0].Confirmed){yield return new WaitForSecondsRealtime(1.6f);continue;}
                        ConfirmCurrent();yield return new WaitForSecondsRealtime(.4f);continue;
                    }
                    if(line=="defend")
                    {
                        if(renderedView.Pending?.Kind!="defense"){yield return new WaitForSecondsRealtime(1.6f);continue;}
                        Check(renderedView.Pending.ChooserSeat==0,"Actual learner defense response");
                        WheelPick("sabina-01-拔枪");yield return null;
                    }
                    else if(line=="move-commit" || line=="practice")
                    {
                        PickAction("secondary");chosenCell=renderedView.SecondaryMoves.First(m=>m.Destination!=renderedView.Units.Single(u=>u.Seat==0).Position).Destination;Render();yield return null;
                    }
                    else if(line=="attack-hero" || line=="attack-minion" || line=="attack-heavy")
                    {
                        if(renderedView.Pending?.Kind=="defense"){yield return new WaitForSecondsRealtime(1.6f);continue;}
                        PickAction("primary");string target=line=="attack-hero"?"hero:1":tutorial.TargetId;
                        chosenCell=renderedView.Units.Single(u=>u.Id==target).Position;Render();yield return null;
                    }
                    else if(line=="round-remove")
                    {
                        chosenCell=renderedView.Units.Single(u=>u.Id==renderedView.RoundMinionRemovals.First()).Position;Render();yield return null;
                    }
                    else if(line=="upgrade-pick" || line=="ultimate-pick")
                    {
                        var card=renderedView.UpgradeOptions.First().CardId;
                        var disc=root.Q<Goa2.Presentation.UI3D.SkillDisc>("upgrade-card-"+card);Check(disc!=null,"Actual upgrade disc "+card);
                        // Same UI preselection as the disc callback; confirmation still goes through animated dock.
                        upgradeCardId=card;Render();yield return null;
                    }
                    else if(line=="round-start" || line=="upgrade-start" || line=="ultimate-start"){Click("resolve-round-end");continue;}
                    else throw new Exception("No UI action for "+line);
                    long revision=renderedView.Revision;yield return new WaitForSecondsRealtime(.4f);
                    Check(confirmButton!=null && confirmButton.enabledInHierarchy && VisibleCenter(confirmButton),"Confirmation available and not covered "+line);
                    ConfirmCurrent();deadline=Time.realtimeSinceStartup+6;
                    while((decisionAnimating || renderedView.Revision==revision) && Time.realtimeSinceStartup<deadline)yield return null;
                    Check(renderedView.Revision>revision,"Rule accepted after real UI confirmation "+line);
                }
                Check(tutorial!.Complete,"Chapter completed "+chapter);yield return null;
            }
            Check(tutorialProgress!.Completed.Skip(from).All(x=>x),"All exercised chapter completion records written");
            StartTutorialChapter(0);tutorial!.Acknowledge();Render();yield return null;
            string beforeRetry=session.ExportSave();StartTutorialChapter(0);Check(tutorial!.Step==0 && session.ExportSave()!=beforeRetry,"Retry starts new isolated chapter");
            SwitchSeat(2);Check(seat==0,"Hotseat shortcuts cannot impersonate bot");
            SaveCurrent();Check((File.Exists(SavePath)?File.ReadAllText(SavePath):"")==hotseatBefore,"Tutorial save leaves hotseat file unchanged");
            ExitTutorial();Check(ReferenceEquals(previous,session) && session.ExportSave()==previousSave,"Exit restores exact previous session");
            tutorialHelp=true;Render();yield return new WaitForSecondsRealtime(.5f);Click("tutorial-help-close");Check(session.ExportSave()==previousSave,"Help is readonly");
        }
    }
}

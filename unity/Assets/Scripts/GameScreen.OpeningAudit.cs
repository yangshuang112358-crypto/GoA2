#nullable enable
using System;
using System.Collections;
using System.IO;
using System.Linq;
using Goa2.Domain;
using UnityEngine;
using UnityEngine.UIElements;
namespace Goa2.Presentation
{
    public sealed partial class GameScreen
    {
        private IEnumerator AuditOpening(string output,BoardAuditReport report)
        {
            void Check(bool ok,string label){if(!ok)throw new InvalidOperationException(label);report.Checks.Add(label);}
            rightExpanded=false;NewMatch();yield return null;yield return null;
            Check(renderedView.Opening?.Status=="throwing","New match uses physical BP");
            Check(root.Query<Button>().ToList().Count(b=>b.name.StartsWith("draft-hero-"))==6,"Six browseable heroes around coin");
            Check(root.Q("stage-banner")!=null,"Five-second stage introduction before throw");
            yield return new WaitForSecondsRealtime(5.6f);ScreenCapture.CaptureScreenshot(Path.Combine(output,"draft-toss.png"));
            float deadline=Time.realtimeSinceStartup+40;
            while(renderedView.Opening!.Status!="settled")
            {
                Check(Time.realtimeSinceStartup<deadline,"Physical BP progresses within fallback bound");
                if(renderedView.Opening.Status=="stuck")for(int s=0;s<4;s++){SwitchSeat(s);Submit(CommandKind.VoteCoinReroll,renderedView.Opening.TossId);}
                yield return new WaitForSecondsRealtime(.5f);
            }
            yield return new WaitForSecondsRealtime(7.5f);
            var first=renderedView.DraftTeam!.Value;int captain=first==Team.Blue?0:1;SwitchSeat(captain);chosenHero="wasp";Render();yield return null;yield return null;
            Check(root.Q<Button>("draft-confirm")?.enabledInHierarchy==true,"Eligible team can confirm after toss and banner");
            Check(root.Q("draft-panel").worldBound.xMin>=0 && root.Q("draft-panel").worldBound.xMax<=Screen.width,"Draft modal fits resolution");
            ScreenCapture.CaptureScreenshot(Path.Combine(output,"draft-select.png"));yield return new WaitForSecondsRealtime(.4f);
            int[] seats={captain,1-captain,3-captain,captain+2};string[] heroes={"wasp","shargatha","brogan","arien"};
            for(int i=0;i<4;i++)
            {
                SwitchSeat(seats[i]);chosenHero=heroes[i];Render();yield return new WaitForSecondsRealtime(.5f);Submit(CommandKind.ChooseHero,heroes[i]);yield return new WaitForSecondsRealtime(.7f);
                Check(renderedView.Players[seats[i]].HeroId==heroes[i],"BP accepted pick "+(i+1));
                if(i==1){ScreenCapture.CaptureScreenshot(Path.Combine(output,"draft-team-colors.png"));yield return new WaitForSecondsRealtime(.3f);}
            }
            Check(renderedView.Phase==Phase.Deployment && root.Q("draft-overlay")==null,"Fourth pick leaves BP");
            for(int s=0;s<2;s++)
            {
                SwitchSeat(s);
                while(renderedView.Deployments.Count>0){var d=renderedView.Deployments.First();Submit(CommandKind.DeployHero,target:d.Key,destination:d.Value.First());yield return null;}
            }
            Check(renderedView.Opening.Purpose=="opening" && renderedView.Phase==Phase.Deployment,"Second toss gates planning");
            yield return new WaitForSecondsRealtime(5.8f);ScreenCapture.CaptureScreenshot(Path.Combine(output,"opening-toss.png"));
            deadline=Time.realtimeSinceStartup+45;
            while(renderedView.Phase!=Phase.Planning)
            {
                Check(Time.realtimeSinceStartup<deadline,"Opening toss reaches planning");
                if(renderedView.Opening.Status=="stuck")for(int s=0;s<4;s++){SwitchSeat(s);Submit(CommandKind.VoteCoinReroll,renderedView.Opening.TossId);}
                yield return new WaitForSecondsRealtime(.5f);
            }
            yield return new WaitForSecondsRealtime(9);Check(root.Q("opening-coin")==null,"Finished coin returns to board display");
            Check(!board3DViewport.Presentation.HideCoin,"Board decision coin restored");
            ScreenCapture.CaptureScreenshot(Path.Combine(output,"opening-planning.png"));yield return new WaitForSecondsRealtime(.5f);
        }
    }
}

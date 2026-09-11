#nullable enable
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Goa2.Domain;
using UnityEngine;
using UnityEngine.UIElements;

namespace Goa2.Presentation
{
    public sealed partial class GameScreen
    {
        // Explicit QA launch with an isolated save. Reads the real Player panel, without physical input.
        private void StartRevealedStripeAudit(string[] args)
        {
            int index=Array.IndexOf(args,"-goaRevealedStripeAudit");
            if(index>=0 && index+1<args.Length && screenshotPath!=null && customSavePath!=null)
            { UnityEngine.Application.runInBackground=true;StartCoroutine(AuditRevealedStripes(args[index+1])); }
        }
        [Serializable] private sealed class RevealedStripeAuditResult
        {
            public string Mode="Real Player geometry with debug setup and commands; no physical input",StartedUtc="",FinishedUtc="";
            public int Width,Height;public bool Passed;
            public List<RevealedStripeAuditState> States=new List<RevealedStripeAuditState>();
        }
        [Serializable] private sealed class RevealedStripeAuditState
        {
            public string Name="",Phase="";public int ExpectedCards,ActualCards;public bool Passed;
            public List<RevealedStripeAuditTile> Tiles=new List<RevealedStripeAuditTile>();
        }
        [Serializable] private sealed class RevealedStripeAuditTile
        {
            public int Seat;public bool HasCard,EmptyHasNoStripes,TeamAtBottom,SeparateFromContent,CardColorAtTop,CorrectTeam,Passed;
            public Rect CardBounds,TeamBounds,ColorBounds;public float ContentBottom;
        }
        private RevealedStripeAuditState InspectRevealedStripes(string name,int expectedCards)
        {
            var state=new RevealedStripeAuditState {Name=name,Phase=renderedView.Phase.ToString(),ExpectedCards=expectedCards};
            var latest=renderedView.Players.SelectMany(p=>p.Plays).OrderByDescending(p=>p.Round).ThenByDescending(p=>p.Turn).FirstOrDefault();
            foreach(var player in renderedView.Players)
            {
                var tile=root.Q<VisualElement>("revealed-seat-"+(player.Seat+1));
                var item=new RevealedStripeAuditTile {Seat=player.Seat+1,HasCard=latest!=null && player.Plays.Any(p=>p.Round==latest.Round && p.Turn==latest.Turn)};
                if(item.HasCard) state.ActualCards++;
                if(tile!=null)
                {
                    var team=tile.Q<VisualElement>("revealed-team-"+item.Seat);
                    var color=tile.Q<VisualElement>("revealed-color-"+item.Seat);
                    item.CardBounds=tile.worldBound;
                    item.EmptyHasNoStripes=team==null && color==null;
                    if(team!=null && color!=null)
                    {
                        item.TeamBounds=team.worldBound;item.ColorBounds=color.worldBound;
                        item.ContentBottom=tile.Children().Where(e=>e!=team).Max(e=>e.worldBound.yMax);
                        item.TeamAtBottom=Mathf.Abs(item.TeamBounds.yMax-item.CardBounds.yMax)<=2 && Mathf.Abs(item.TeamBounds.height-6)<=.5f && tile[tile.childCount-1]==team;
                        item.SeparateFromContent=item.TeamBounds.y>=item.ContentBottom && item.TeamBounds.y>item.ColorBounds.yMax;
                        item.CardColorAtTop=Mathf.Abs(item.ColorBounds.y-item.CardBounds.y)<=2 && Mathf.Abs(item.ColorBounds.height-9)<=.5f;
                        var expected=player.Team==Team.Blue ? new Color(.15f,.45f,.95f) : new Color(.9f,.2f,.25f);
                        var actual=team.resolvedStyle.backgroundColor;
                        item.CorrectTeam=Mathf.Abs(expected.r-actual.r)<.01f && Mathf.Abs(expected.g-actual.g)<.01f && Mathf.Abs(expected.b-actual.b)<.01f;
                    }
                    item.Passed=item.HasCard ? item.TeamAtBottom && item.SeparateFromContent && item.CardColorAtTop && item.CorrectTeam : item.EmptyHasNoStripes;
                }
                state.Tiles.Add(item);
            }
            state.Passed=state.ActualCards==expectedCards && state.Tiles.Count==4 && state.Tiles.All(t=>t.Passed);
            return state;
        }
        private IEnumerator AuditRevealedStripes(string output)
        {
            var result=new RevealedStripeAuditResult {StartedUtc=DateTime.UtcNow.ToString("o"),Width=Screen.width,Height=Screen.height};
            if(UnityEngine.Application.isEditor) result.Mode="Unity Editor Play Mode geometry with debug setup and commands; no standalone Player or physical input";
            yield return null;yield return null;
            Submit(CommandKind.DebugPrepare,"wasp,shargatha,brogan,arien");
            yield return null;yield return null;
            result.States.Add(InspectRevealedStripes("No selections",0));
            for(int number=0;number<3;number++)
            {
                seat=number;Render();
                Submit(CommandKind.SelectCard,renderedView.OwnCards.First().CardId);
            }
            yield return null;yield return null;
            result.States.Add(InspectRevealedStripes("Three private selections, nothing revealed",0));
            seat=3;Render();Submit(CommandKind.SelectCard,renderedView.OwnCards.First().CardId);
            yield return null;yield return null;
            result.States.Add(InspectRevealedStripes("Four revealed cards",4));
            if(UnityEngine.Application.isEditor) yield return null;
            else yield return new WaitForEndOfFrame();
            ScreenCapture.CaptureScreenshot(Path.ChangeExtension(output,".png"));
            yield return new WaitForSecondsRealtime(.2f);
            Submit(CommandKind.DebugAdvance,"turn");
            yield return null;yield return null;
            var retained=InspectRevealedStripes("Next planning retains revealed cards",4);
            retained.Passed &= renderedView.Phase==Phase.Planning && renderedView.Turn==2;
            result.States.Add(retained);
            result.Passed=result.States.Count==4 && result.States.All(s=>s.Passed);
            result.FinishedUtc=DateTime.UtcNow.ToString("o");
            File.WriteAllText(output,JsonUtility.ToJson(result,true));
            yield return new WaitForSecondsRealtime(.3f);
#if UNITY_EDITOR
            if(Environment.GetCommandLineArgs().Contains("-goaStripeEditor")) UnityEditor.EditorApplication.Exit(result.Passed ? 0 : 1);
            else UnityEditor.EditorApplication.isPlaying=false;
#else
            UnityEngine.Application.Quit(result.Passed ? 0 : 1);
#endif
        }
    }
}

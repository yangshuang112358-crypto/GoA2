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
        // Explicit QA-only launch: use the real UI Toolkit panel and its font measurements.
        // This does not replace foreground mouse/keyboard testing.
        private void StartCardReadingAudit(string[] args)
        {
            int index=Array.IndexOf(args,"-goaCardReadingAudit");
            if(index>=0 && index+1<args.Length && screenshotPath!=null)
            { UnityEngine.Application.runInBackground=true;StartCoroutine(AuditCardReading(args[index+1])); }
        }
        [Serializable] private sealed class CardReadingAuditResult
        {
            public string Mode="Real Player layout and synthetic pointer events; no physical input",StartedUtc="",FinishedUtc="";
            public int Width,Height;public bool Passed;
            public List<CardReadingAuditItem> Cards=new List<CardReadingAuditItem>();
        }
        [Serializable] private sealed class CardReadingAuditItem
        {
            public string CardId="";public bool FullText,FitsWindow,TextNotClipped,NoScroll,NoCommand,IgnoresInput,Passed;
            public float FontSize,RequiredTextHeight;public Rect PreviewBounds,TextBounds;
        }
        private IEnumerator AuditCardReading(string output)
        {
            var result=new CardReadingAuditResult {StartedUtc=DateTime.UtcNow.ToString("o"),Width=Screen.width,Height=Screen.height};
            yield return null;yield return null;
            if(renderedView.Phase==Phase.HeroSelection) Submit(CommandKind.DebugPrepare,"wasp,shargatha,brogan,arien");
            yield return null;yield return null;
            long revision=renderedView.Revision;
            var source=Box("audit-card-source");source.style.position=Position.Absolute;source.style.left=24;source.style.top=24;source.style.width=300;source.style.height=50;root.Add(source);
            foreach(var card in catalog.Cards)
            {
                source.Clear(); // A fresh source exercises the actual registered pointer entry handler.
                var trigger=new VisualElement();trigger.style.width=300;trigger.style.height=50;source.Add(trigger);
                AttachCardReading(trigger,card);
                using(var enter=PointerEnterEvent.GetPooled()) {enter.target=trigger;trigger.SendEvent(enter);}
                yield return new WaitForSecondsRealtime(.25f);yield return null;
                var item=new CardReadingAuditItem {CardId=card.Id};
                var preview=root.Q<VisualElement>("card-preview");var rules=preview?.Q<Label>("card-preview-rules");
                if(preview!=null && rules!=null)
                {
                    item.PreviewBounds=preview.worldBound;item.TextBounds=rules.worldBound;item.FontSize=rules.resolvedStyle.fontSize;
                    item.FullText=rules.text==card.Text;
                    item.FitsWindow=item.PreviewBounds.x>=0 && item.PreviewBounds.y>=0 && item.PreviewBounds.xMax<=Screen.width && item.PreviewBounds.yMax<=Screen.height;
                    item.RequiredTextHeight=rules.MeasureTextSize(card.Text,rules.contentRect.width,VisualElement.MeasureMode.Exactly,0,VisualElement.MeasureMode.Undefined).y;
                    item.TextNotClipped=rules.contentRect.height+1>=item.RequiredTextHeight && item.TextBounds.y>=item.PreviewBounds.y && item.TextBounds.yMax<=item.PreviewBounds.yMax;
                    item.NoScroll=preview.Query<ScrollView>().ToList().Count==0;
                    item.NoCommand=renderedView.Revision==revision;
                    item.IgnoresInput=preview.Query<VisualElement>().ToList().All(e=>e.pickingMode==PickingMode.Ignore);
                    item.Passed=item.FullText && item.FitsWindow && item.TextNotClipped && item.NoScroll && item.NoCommand && item.IgnoresInput && item.FontSize>=26;
                }
                result.Cards.Add(item);
                using(var leave=PointerLeaveEvent.GetPooled()) {leave.target=trigger;trigger.SendEvent(leave);}
            }
            ShowCardPreview(source,catalog.Cards.OrderByDescending(c=>c.Text.Length).First(),null);
            yield return new WaitForSecondsRealtime(.2f);yield return new WaitForEndOfFrame();
            ScreenCapture.CaptureScreenshot(Path.ChangeExtension(output,".png"));
            result.Passed=result.Cards.Count==108 && result.Cards.All(c=>c.Passed);
            result.FinishedUtc=DateTime.UtcNow.ToString("o");
            File.WriteAllText(output,JsonUtility.ToJson(result,true));
            yield return new WaitForSecondsRealtime(.3f);
            UnityEngine.Application.Quit(result.Passed ? 0 : 1);
        }
    }
}

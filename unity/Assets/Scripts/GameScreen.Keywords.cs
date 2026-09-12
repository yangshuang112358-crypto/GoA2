#nullable enable
using System.Linq;
using Goa2.Domain;
using UnityEngine;
using UnityEngine.UIElements;

namespace Goa2.Presentation
{
    public sealed partial class GameScreen
    {
        private bool keywordGlossaryOpen;
        private CardDefinition? glossaryCard;
        private static Label RulesText(string text,string css)
        {
            var label=Text(CardTextMarkup.Format(text),css);
            label.enableRichText=true;label.parseEscapeSequences=false;
            label.AddToClassList("keyword-rules");
            return label;
        }
        private void OpenKeywordGlossary(CardDefinition? card=null)
        {
            glossaryCard=card;keywordGlossaryOpen=true;
            scrollPositions.Remove("goa-scroll-keywords");Render();
        }
        private void CloseKeywordGlossary() { keywordGlossaryOpen=false;glossaryCard=null;Render(); }
        private void RenderKeywordGlossary()
        {
            var overlay=Box("dialog-overlay");overlay.name="keyword-glossary";root.Add(overlay);
            var dialog=Box("dialog");dialog.AddToClassList("keyword-dialog");
            dialog.style.width=Mathf.Min(780,Screen.width-40);dialog.style.maxHeight=Screen.height-40;overlay.Add(dialog);
            dialog.Add(Text(glossaryCard==null ? "规则术语" : "本牌术语 · "+glossaryCard.Name,"panel-title"));
            dialog.Add(Text("金色：动作 · 青色：时点 · 白色加粗：条件 · 红色：击败","tiny"));
            var scroll=new ScrollView {name="goa-scroll-keywords"};scroll.AddToClassList("keyword-list");dialog.Add(scroll);
            var terms=glossaryCard==null ? CardTextMarkup.Keywords : CardTextMarkup.Spans(glossaryCard.Text).Select(s=>s.Keyword).Distinct().ToArray();
            foreach(var term in terms)
            {
                var row=Box("keyword-entry");scroll.Add(row);
                var title=Text(term.Name,"section-title");ColorUtility.TryParseHtmlString(term.Color,out var color);title.style.color=color;row.Add(title);
                row.Add(Text(term.Explanation,"body"));row.Add(Text(term.Rule,"tiny"));
            }
            var sourceCard=glossaryCard;
            if(sourceCard!=null)
            {
                dialog.Add(Button("复制卡牌原文",()=>GUIUtility.systemCopyBuffer=sourceCard.Text,"quiet-button","keyword-copy"));
                dialog.Add(Button("查看全部术语",()=>OpenKeywordGlossary(),"quiet-button","keyword-all"));
            }
            dialog.Add(Button("关闭 · Esc",CloseKeywordGlossary,"primary-button","keyword-close"));
        }
    }
}

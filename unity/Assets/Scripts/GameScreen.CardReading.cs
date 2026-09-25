#nullable enable
using System;
using System.Linq;
using Goa2.Domain;
using UnityEngine;
using UnityEngine.UIElements;

namespace Goa2.Presentation
{
    public sealed partial class GameScreen
    {
        private VisualElement? cardPreview;
        private CardDefinition? previewCard;
        private IVisualElementScheduledItem? cardPreviewDelay;

        private void HideCardPreview()
        {
            cardPreviewDelay?.Pause(); cardPreviewDelay=null;
            cardPreview?.RemoveFromHierarchy(); cardPreview=null;
            previewCard=null;
            RequestCapture();
        }
        private void AttachCardReading(VisualElement source,CardDefinition card,PlayerView? player=null)
        {
            source.tooltip="";
            source.RegisterCallback<PointerEnterEvent>(_=>
            {
                HideCardPreview();
                cardPreviewDelay=source.schedule.Execute(()=>
                {
                    if(source.panel==null || newMatchPending || debugPresetsOpen || keywordGlossaryOpen) return;
                    ShowCardPreview(source,card,player);
                }).StartingIn(180);
            });
            source.RegisterCallback<PointerLeaveEvent>(_=>HideCardPreview());
        }
        private void ShowCardPreview(VisualElement source,CardDefinition card,PlayerView? player)
        {
            cardPreview?.RemoveFromHierarchy();
            previewCard=card;
            var preview=Box("card-preview"); preview.name="card-preview";cardPreview=preview;
            preview.style.width=Mathf.Min(700,root.worldBound.width-32);
            preview.style.borderTopColor=CardColor(card.Color);
            preview.Add(Text(card.Name,"panel-title"));
            preview.Add(Text(HeroName(card.HeroId)+" · "+ColorName(card.Color)+"色 · "+(card.Color=="purple" ? "英雄8级" : card.Level.HasValue ? "卡牌"+card.Level+"级" : "基础牌"),"tiny"));
            var rules=RulesText(CardTextMarkup.Description(card),"card-preview-rules");rules.name="card-preview-rules";preview.Add(rules);
            if(player!=null) CompactCardNumbers(preview,card,player,true,"card-preview");
            else
            {
                preview.Add(Text(card.PrimaryCategory+" "+(card.Exclamation ? "!" : card.PrimaryValue.ToString())+SubtypeText(card)+" · 先攻 "+card.Initiative,"body"));
                preview.Add(Text("次要移动 "+Number(card.SecondaryMovement)+" · 次要防御 "+Number(card.SecondaryDefense),"tiny"));
            }
            preview.Add(Text("卡底升级图标 · "+(card.Passive ?? "无"),"tiny"));
            preview.Add(Text(renderedView.SupportedUltimateCards.Contains(card.Id) ? "紫卡持续能力已开放" : renderedView.SupportedPrimaryCards.Contains(card.Id) ? "主要行动已开放" : renderedView.SupportedDefenseCards.Contains(card.Id) ? "防御响应已开放" : "牌文效果待实施","card-zone"));
            if(player?.Seat==seat && renderedView.DefenseRestrictions.TryGetValue(card.Id,out string restriction))
                preview.Add(Text(DefenseRestrictionText(restriction),"restriction-text"));
            preview.Add(Text("F1 查看本牌术语 · 移开鼠标或 Esc 收起","tiny"));
            // The reading layer must never intercept the click that selects a card or a map cell.
            preview.Query<VisualElement>().ForEach(e=>e.pickingMode=PickingMode.Ignore);
            preview.pickingMode=PickingMode.Ignore;
            preview.RegisterCallback<GeometryChangedEvent>(_=>
            {
                float w=preview.worldBound.width,h=preview.worldBound.height;
                float x=Mathf.Clamp(source.worldBound.center.x-w/2,16,Mathf.Max(16,root.worldBound.width-w-16));
                float above=source.worldBound.y-h-10,below=source.worldBound.yMax+10;
                float y=above>=16 ? above : below+h<=root.worldBound.height-16 ? below : (root.worldBound.height-h)/2;
                preview.style.left=x;preview.style.top=Mathf.Clamp(y,16,Mathf.Max(16,root.worldBound.height-h-16));
                RequestCapture();
            });
            root.Add(preview);RequestCapture();
        }
        private void CardRulesPreview(VisualElement parent,CardDefinition card,string name)
        {
            // Keep the formal text as the source. Two lines are an overview; hover reads every word.
            var label=RulesText(CardTextMarkup.Description(card),"card-rules-preview");label.name=name;
            // UI Toolkit's single-line ellipsis would hide the second line. Build a measured
            // two-line excerpt instead, so no half-height glyphs are clipped at the bottom.
            label.RegisterCallback<GeometryChangedEvent>(_=>
            {
                float width=label.contentRect.width;if(width<=0) return;
                label.text=CardTextMarkup.Preview(CardTextMarkup.Description(card),width,candidate=>label.MeasureTextSize(candidate,0,VisualElement.MeasureMode.Undefined,0,VisualElement.MeasureMode.Undefined).x);
            });
            parent.Add(label);
        }
        private void CompactCardNumbers(VisualElement parent,CardDefinition card,PlayerView player,bool bonuses,string prefix,bool overviewOnly=false)
        {
            var stats=Box("card-compact-stats");parent.Add(stats);
            void Add(string label,int bonus,string suffix)
            {
                var chip=Box("card-stat-chip");stats.Add(chip);chip.Add(Text(label,"tiny"));
                if(bonus>0) {var plus=Text("+"+bonus,"passive-plus");plus.name=prefix+"-"+suffix+"-bonus";chip.Add(plus);}
            }
            string key=card.PrimaryFamily=="attack" ? "攻击" : card.PrimaryFamily=="defense" ? "防御" : card.PrimaryFamily=="movement" ? "移动" : "";
            Add(card.PrimaryCategory+" "+(card.Exclamation ? "!" : card.PrimaryValue.ToString()),bonuses && !card.Exclamation ? (Bonus(player,key)+(card.PrimaryCategory=="基础攻击" ? player.BasicAttackBonus : 0)) : 0,"primary");
            Add("先 "+card.Initiative,bonuses ? Bonus(player,"先攻") : 0,"initiative");
            if(overviewOnly) return;
            Add("移 "+Number(card.SecondaryMovement),bonuses && card.SecondaryMovement.HasValue ? Bonus(player,"移动") : 0,"movement");
            Add("防 "+Number(card.SecondaryDefense),bonuses && card.SecondaryDefense.HasValue ? Bonus(player,"防御") : 0,"defense");
            if(card.SubtypeValue.HasValue) Add((card.Subtype??"")+" "+card.SubtypeValue,bonuses ? (Bonus(player,card.Subtype=="远程" ? "远程" : "范围")+(card.PrimaryCategory=="基础攻击" ? player.BasicAttackRangeBonus : 0)) : 0,"range");
        }
    }
}

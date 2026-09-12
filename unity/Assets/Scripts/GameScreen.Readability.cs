#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using Goa2.Domain;
using UnityEngine;
using UnityEngine.UIElements;

namespace Goa2.Presentation
{
    public sealed partial class GameScreen
    {
        private Action? confirmAction;
        private Button? confirmButton;
        private bool spaceGuardInstalled;
        private bool historyOpen;
        private int historyRound=1;
        private int Bonus(PlayerView player,string key) => player.PermanentBonuses.TryGetValue(key,out var value) ? value : 0;
        private void StatLine(VisualElement parent,string text,int bonus,string name="")
        {
            var line=Box("stat-line"); parent.Add(line);
            line.Add(Text(text,"tiny"));
            if(bonus>0) { var label=Text("+"+bonus,"passive-plus"); label.name=name; line.Add(label); }
        }
        private void CardNumbers(VisualElement parent,CardDefinition card,PlayerView player,bool showBonuses,string prefix)
        {
            string key=card.PrimaryFamily=="attack" ? "攻击" : card.PrimaryFamily=="defense" ? "防御" : card.PrimaryFamily=="movement" ? "移动" : "";
            StatLine(parent,card.PrimaryCategory+" "+(card.Exclamation ? "!" : card.PrimaryValue.ToString()),showBonuses && !card.Exclamation ? Bonus(player,key) : 0,prefix+"-primary-bonus");
            StatLine(parent,"先攻 "+card.Initiative,showBonuses ? Bonus(player,"先攻") : 0,prefix+"-initiative-bonus");
            var secondary=Box("stat-line"); parent.Add(secondary);
            StatLine(secondary,"移 "+Number(card.SecondaryMovement),showBonuses && card.SecondaryMovement.HasValue ? Bonus(player,"移动") : 0,prefix+"-movement-bonus");
            StatLine(secondary,"防 "+Number(card.SecondaryDefense),showBonuses && card.SecondaryDefense.HasValue ? Bonus(player,"防御") : 0,prefix+"-defense-bonus");
            if(card.SubtypeValue.HasValue) StatLine(parent,(card.Subtype??"")+" "+card.SubtypeValue,showBonuses ? Bonus(player,card.Subtype=="远程" ? "远程" : "范围") : 0,prefix+"-range-bonus");
        }
        private void AddPurpleDot(VisualElement rounds,PlayerView player)
        {
            if(player.PurpleCardId==null) return;
            var card=catalog.Card(player.PurpleCardId);
            var dot=Box("purple-dot"); dot.name="purple-dot-"+(player.Seat+1); rounds.Add(dot);
            dot.tooltip=card.Name+"\n"+card.Text+"\n紫卡持续文字待实施";
            VisualElement? preview=null;
            dot.RegisterCallback<PointerEnterEvent>(_=>
            {
                preview=Box("purple-preview"); preview.name="purple-preview";
                preview.pickingMode=PickingMode.Ignore;
                preview.Add(Text(card.Name,"panel-title"));preview.Add(Text("紫卡 · 英雄8级 · 持续被动","tiny"));
                var rules=RulesText(card.Text,"body");rules.name="purple-preview-text";preview.Add(rules);
                preview.Add(Text("持续文字待实施","tiny"));
                preview.style.left=Mathf.Clamp(dot.worldBound.xMax+12,12,Mathf.Max(12,root.worldBound.width-520));
                preview.RegisterCallback<GeometryChangedEvent>(_=> { if(preview!=null) preview.style.top=Mathf.Clamp(dot.worldBound.y,12,Mathf.Max(12,root.worldBound.height-preview.worldBound.height-12)); });
                root.Add(preview);
                RequestCapture();
            });
            dot.RegisterCallback<PointerLeaveEvent>(_=> { preview?.RemoveFromHierarchy();preview=null;RequestCapture(); });
        }
        private void RenderHistory(GameView view)
        {
            var overlay=Box("gallery-overlay"); root.Add(overlay);
            var pages=new SortedDictionary<int,List<(GameEvent entry,int turn)>>(); int round=1,turn=1;
            foreach(var entry in view.Events)
            {
                if(entry.Kind=="PlanningStarted")
                {
                    var parts=entry.Detail.Split(':');
                    if(parts.Length==2 && int.TryParse(parts[0],out int r) && int.TryParse(parts[1],out int t)) {round=r;turn=t;}
                }
                if(!pages.ContainsKey(round)) pages[round]=new List<(GameEvent,int)>();
                pages[round].Add((entry,turn));
            }
            if(pages.Count==0) pages[1]=new List<(GameEvent,int)>();
            var rounds=pages.Keys.ToList(); if(!pages.ContainsKey(historyRound)) historyRound=rounds.Last();
            int index=rounds.IndexOf(historyRound);
            var heading=Box("gallery-header"); overlay.Add(heading);
            var title=Text("全部记录 · 第 "+historyRound+" 轮 · 第 "+(index+1)+" / "+rounds.Count+" 页","panel-title");title.name="history-page";heading.Add(title);
            heading.Add(Button("返回战场",()=>{historyOpen=false;Render();},"primary-button","history-close"));
            var nav=Box("debug-button-row");overlay.Add(nav);
            var previous=Button("上一轮",()=>{historyRound=rounds[index-1];Render();},"quiet-button","history-previous");previous.SetEnabled(index>0);nav.Add(previous);
            var next=Button("下一轮",()=>{historyRound=rounds[index+1];Render();},"quiet-button","history-next");next.SetEnabled(index<rounds.Count-1);nav.Add(next);
            var scroll=new ScrollView {name="goa-scroll-history-"+historyRound};scroll.AddToClassList("gallery-scroll");overlay.Add(scroll);
            foreach(var item in pages[historyRound])
            { var label=Text("#"+item.entry.Sequence+" · 回合 "+item.turn+" · "+EventText(item.entry),"body");label.name="history-event-"+item.entry.Sequence;scroll.Add(label); }
        }
    }
}

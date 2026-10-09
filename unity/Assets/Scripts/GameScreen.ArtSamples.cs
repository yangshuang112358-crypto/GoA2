#nullable enable
using UnityEngine;
using UnityEngine.UIElements;
using System.Linq;
using System.Collections.Generic;
using Goa2.Domain;
using Goa2.Presentation.UI3D;
namespace Goa2.Presentation
{
    public sealed partial class GameScreen
    {
        private bool artSamplesOpen;
        private int artSample;
        private string cardArtHero="wasp";
        private void OpenArtSamples(int index){artSample=index;artSamplesOpen=true;rightExpanded=false;Render();}
        private void BuildArtSamples()
        {
            if(!artSamplesOpen)return;
            var overlay=new VisualElement{name="art-samples"};overlay.StretchToParentSize();overlay.style.backgroundColor=new Color(.025f,.035f,.05f,.98f);overlay.style.paddingLeft=24;overlay.style.paddingRight=24;overlay.style.paddingTop=18;overlay.style.paddingBottom=18;
            var heading=Box("debug-button-row");overlay.Add(heading);
            string[] titles={"六英雄精修","三类小兵精修","三版亚特兰蒂斯金币","双面决策币","技能石标","英雄技能图案"};string[] assets={"heroes","minions","coins","decision"};
            for(int i=0;i<titles.Length;i++){int index=i;heading.Add(Button(titles[i],()=>OpenArtSamples(index),"choice-button"));}
            heading.Add(Button("返回战场",()=>{artSamplesOpen=false;Render();},"quiet-button","close-art-samples"));
            if(artSample==4){BuildSkillDiscSamples(overlay);overlay.RegisterCallback<PointerDownEvent>(e=>e.StopPropagation());overlay.RegisterCallback<WheelEvent>(e=>e.StopPropagation());root.Add(overlay);return;}
            if(artSample==5){BuildHeroCardArtSamples(overlay);overlay.RegisterCallback<PointerDownEvent>(e=>e.StopPropagation());overlay.RegisterCallback<WheelEvent>(e=>e.StopPropagation());root.Add(overlay);return;}
            var image=new Image{image=Resources.Load<Texture2D>("UI3D/ArtSamples/"+assets[artSample]),scaleMode=ScaleMode.ScaleToFit,pickingMode=PickingMode.Ignore};image.style.flexGrow=1;image.style.minHeight=0;overlay.Add(image);
            overlay.Add(Text(artSample==3?"红蓝宝石双面决策币。新对局含BP与开局两次房主物理投币，其余切面为快速半圈。":artSample==2?"从左至右：A 三叉戟与潮线 · B 沉城之门 · C 潮汐之眼。已选 C，上下三段折线中心对称；击杀奖励读取规则事件。":artSample==0?"从左至右：黄蜂、夏尔加萨、布罗根、艾瑞恩、虎爪、萨彼娜。当前精修模型棚拍；战场采用游戏灯光与动态特效。":"近战剑盾 · 远程弓箭与箭筒 · 重型圆盾、宝石剑、八足底盘。护甲、布褶与握持细节精修；动作可在调试页切换样例。","body"));
            overlay.RegisterCallback<PointerDownEvent>(e=>e.StopPropagation());overlay.RegisterCallback<WheelEvent>(e=>e.StopPropagation());root.Add(overlay);
        }
        private void BuildSkillDiscSamples(VisualElement parent)
        {
            parent.Add(Text("雕刻石标 · 数字嵌入圆框边缘","panel-title"));
            var scroll=new ScrollView{name="skill-disc-samples"};scroll.style.flexGrow=1;parent.Add(scroll);
            var grid=new VisualElement();grid.style.width=Length.Percent(100);grid.style.maxWidth=1040;grid.style.alignSelf=Align.Center;grid.style.flexDirection=FlexDirection.Row;grid.style.flexWrap=Wrap.Wrap;grid.style.justifyContent=Justify.Center;scroll.Add(grid);
            var basic=catalog.Cards.First(c=>c.Id=="wasp-01-电击");
            void Sample(string key,string title,CardDefinition card,CardZone zone,int bonus=0)
            {
                var box=new VisualElement();box.style.width=230;box.style.height=242;box.style.marginLeft=10;box.style.marginRight=10;grid.Add(box);
                var heading=Text(title,"body");heading.style.unityTextAlign=TextAnchor.MiddleCenter;heading.style.fontSize=18;box.Add(heading);
                var samplePlayer=new PlayerView{EffectiveBonuses=new Dictionary<string,int>{{"攻击",bonus},{"先攻",bonus},{"防御",bonus},{"移动",bonus>0?1:0}}};
                var disc=new SkillDisc(card.Color,card,samplePlayer,zone,false,false,new SkillWheelState.Motion(),0,()=>{},()=>{}){name="sample-"+key};
                disc.style.left=37;disc.style.top=40;box.Add(disc);
                disc.Inspect=at=>ShowSkillInfo(card,disc,at);
                disc.RegisterCallback<PointerMoveEvent>(e=>{if(skillPopupOwner==disc)PositionSkillInfo(e.position);});
                disc.RegisterCallback<PointerLeaveEvent>(_=>{if(skillPopupOwner==disc)HideSkillInfo();});
            }
            Sample("normal","原值 · 真实凹刻",basic,CardZone.InHand);
            Sample("boosted","加成绿字 · 双位数",basic,CardZone.InHand,7);
            Sample("reduced","减值红字 · 负数压力样例",basic,CardZone.InHand,-8);
            Sample("infinity","条件防御 · ∞",catalog.Cards.First(c=>c.Exclamation && c.PrimaryFamily=="defense"),CardZone.InHand);
            Sample("selected","已选 · 压下",basic,CardZone.Selected);
            Sample("played","已出 · 保留暗色",basic,CardZone.PlayedResolved);
            Sample("discarded","弃置 · 金属背面",basic,CardZone.Discarded);
            Sample("ranged","远程石标",catalog.Cards.First(c=>c.Subtype=="远程" && c.SubtypeValue.HasValue),CardZone.InHand);
            parent.Add(Text("左上移动 / 右上防御 / 左下主要行动 / 右下范围或远程 / 底部先攻。悬停观察倾斜，右键读牌；样例数值不改变对局。","body"));
        }
        private void BuildHeroCardArtSamples(VisualElement parent)
        {
            var choices=Box("debug-button-row");parent.Add(choices);
            foreach(var pair in new[]{("wasp","黄蜂"),("shargatha","夏尔加萨"),("brogan","布罗根"),("arien","艾瑞恩"),("tigerclaw","虎爪"),("sabina","萨彼娜")}){
                var hero=pair.Item1;choices.Add(Button(pair.Item2,()=>{cardArtHero=hero;Render();},"choice-button"));
            }
            parent.Add(Text("基础牌独立图案 · 两条升级路线各复用一图 · 金色菱形对应卡牌等级","body"));
            var scroll=new ScrollView{name="hero-card-art-samples"};scroll.style.flexGrow=1;parent.Add(scroll);
            var grid=new VisualElement();grid.style.maxWidth=1152;grid.style.alignSelf=Align.Center;grid.style.flexDirection=FlexDirection.Row;grid.style.flexWrap=Wrap.Wrap;grid.style.justifyContent=Justify.Center;scroll.Add(grid);
            foreach(var card in catalog.Cards.Where(c=>c.HeroId==cardArtHero).OrderBy(c=>c.Id,System.StringComparer.Ordinal)){
                var box=new VisualElement();box.style.width=184;box.style.height=207;box.style.marginLeft=3;box.style.marginRight=3;grid.Add(box);
                var label=Text(card.Name+(card.Level.HasValue?" · "+card.Level.Value:""),"body");label.style.fontSize=17;label.style.unityTextAlign=TextAnchor.MiddleCenter;box.Add(label);
                var disc=new SkillDisc(card.Color,card,new PlayerView(),CardZone.InHand,false,false,new SkillWheelState.Motion(),0,()=>{},()=>{}){name="card-art-"+card.Id};disc.style.left=14;disc.style.top=30;box.Add(disc);
                disc.Inspect=at=>ShowSkillInfo(card,disc,at);disc.RegisterCallback<PointerLeaveEvent>(_=>{if(skillPopupOwner==disc)HideSkillInfo();});
                disc.RegisterCallback<PointerMoveEvent>(e=>{if(skillPopupOwner==disc)PositionSkillInfo(e.position);});
            }
        }
    }
}

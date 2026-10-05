#nullable enable
using UnityEngine;
using UnityEngine.UIElements;
namespace Goa2.Presentation
{
    public sealed partial class GameScreen
    {
        private bool artSamplesOpen;
        private int artSample;
        private void OpenArtSamples(int index){artSample=index;artSamplesOpen=true;rightExpanded=false;Render();}
        private void BuildArtSamples()
        {
            if(!artSamplesOpen)return;
            var overlay=new VisualElement{name="art-samples"};overlay.StretchToParentSize();overlay.style.backgroundColor=new Color(.025f,.035f,.05f,.98f);overlay.style.paddingLeft=24;overlay.style.paddingRight=24;overlay.style.paddingTop=18;overlay.style.paddingBottom=18;
            var heading=Box("debug-button-row");overlay.Add(heading);
            string[] titles={"六英雄造型初稿","三类小兵装备初稿","三版亚特兰蒂斯金币","双面决策币"};string[] assets={"heroes","minions","coins","decision"};
            for(int i=0;i<titles.Length;i++){int index=i;heading.Add(Button(titles[i],()=>OpenArtSamples(index),"choice-button"));}
            heading.Add(Button("返回战场",()=>{artSamplesOpen=false;Render();},"quiet-button","close-art-samples"));
            var image=new Image{image=Resources.Load<Texture2D>("UI3D/ArtSamples/"+assets[artSample]),scaleMode=ScaleMode.ScaleToFit,pickingMode=PickingMode.Ignore};image.style.flexGrow=1;image.style.minHeight=0;overlay.Add(image);
            overlay.Add(Text(artSample==3?"红蓝宝石双面决策币。新对局含BP与开局两次房主物理投币，其余切面为快速半圈。":artSample==2?"从左至右：A 三叉戟与潮线 · B 沉城之门 · C 潮汐之眼。已选 C，上下三段折线中心对称；击杀奖励读取规则事件。":artSample==0?"从左至右：黄蜂、夏尔加萨、布罗根、艾瑞恩、虎爪、萨彼娜。原创轮廓初稿，非最终人物精度。":"近战剑盾 · 远程弓箭与箭筒 · 重型圆盾、宝石剑、八足底盘。动作可在调试页切换样例。","body"));
            overlay.RegisterCallback<PointerDownEvent>(e=>e.StopPropagation());overlay.RegisterCallback<WheelEvent>(e=>e.StopPropagation());root.Add(overlay);
        }
    }
}

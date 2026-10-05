using System;
using UnityEngine;
using UnityEngine.UIElements;
namespace Goa2.Presentation.UI3D
{
    // Blender supplies the lit bevels/materials; Unity keeps a stable hit box and local animation.
    public sealed class DecisionStoneButton : Button
    {
        private readonly VisualElement stone;
        private readonly Texture2D idle,hover,pressed;
        private bool over,held;
        private float lift,pressure;
        public DecisionStoneButton(bool confirm,Action clicked,string caption):base(clicked)
        {
            AddToClassList("decision-stone");tooltip=caption;
            string key=confirm?"confirm":"withdraw";
            idle=Resources.Load<Texture2D>("UI3D/DecisionButtons/"+key+"-idle");
            hover=Resources.Load<Texture2D>("UI3D/DecisionButtons/"+key+"-hover");
            pressed=Resources.Load<Texture2D>("UI3D/DecisionButtons/"+key+"-pressed");
            stone=new VisualElement{pickingMode=PickingMode.Ignore};stone.StretchToParentSize();Add(stone);
            stone.style.backgroundImage=new StyleBackground(idle);
            var icon=new Label(confirm?"✓":"↶"){pickingMode=PickingMode.Ignore};icon.AddToClassList("decision-symbol");stone.Add(icon);
            var label=new Label(confirm?"确认":"撤回"){pickingMode=PickingMode.Ignore};label.AddToClassList("decision-caption");stone.Add(label);
            RegisterCallback<PointerEnterEvent>(_=>over=true);
            RegisterCallback<PointerLeaveEvent>(_=>{over=false;held=false;});
            RegisterCallback<PointerDownEvent>(e=>{if(e.button==0)held=true;});
            RegisterCallback<PointerUpEvent>(_=>held=false);
            RegisterCallback<PointerCaptureOutEvent>(_=>held=false);
            schedule.Execute(()=>{
                lift=Mathf.Lerp(lift,over && enabledInHierarchy ? 1 : 0,.18f);pressure=Mathf.Lerp(pressure,held?1:0,.3f);
                stone.style.translate=new Translate(-4*lift,4*pressure-2*lift);
                stone.style.rotate=new Rotate(-1.2f*lift);
                stone.style.scale=new Scale(new Vector3(1+.025f*lift-.045f*pressure,1+.04f*lift-.07f*pressure,1));
                stone.style.opacity=enabledInHierarchy?1:.38f;
                stone.style.backgroundImage=new StyleBackground(held?pressed:over?hover:idle);
            }).Every(16);
        }
    }
}

#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Goa2.Infrastructure;
using Goa2.Domain;
using UnityEngine;
using UnityEngine.UIElements;

namespace Goa2.Presentation
{
    public sealed partial class GameScreen
    {
        private bool debugPresetsOpen;
        private IReadOnlyList<DebugPosition>? debugPositions;
        private string selectedDebugPosition="",debugPositionError="",debugPresetFilter="";
        private string guideMatchId="",guideTitle="",guideInstructions="";
        private void UpdateDebugGuide(GameView view)
        {
            if(guideMatchId!=view.MatchId)
            {
                guideMatchId=view.MatchId;guideTitle="";guideInstructions="";
                if(view.Sandbox)
                {
                    try
                    {
                        debugPositions ??= DebugPositions.Read(File.ReadAllText(Path.Combine(UnityEngine.Application.streamingAssetsPath,"Goa2Debug","presets.json")));
                        var position=debugPositions.OrderByDescending(p=>p.Id.Length).FirstOrDefault(p=>
                            view.MatchId.StartsWith("scenario:debug-"+p.Id+"-",StringComparison.Ordinal) ||
                            view.MatchId.StartsWith("scenario:position-"+p.Id+"-",StringComparison.Ordinal));
                        if(position!=null) { guideTitle=position.Title;guideInstructions=position.Instructions; }
                    }
                    catch(Exception error) { Debug.LogWarning("测试说明不可用："+error.Message); }
                }
            }
        }
        private void RenderDebugGuideShortcut(VisualElement parent,GameView view)
        {
            UpdateDebugGuide(view);
            if(guideInstructions!="") parent.Add(Button("查看本用例操作与分支",()=>{historyOpen=true;historyRound=view.Round;Render();},"quiet-button","debug-guide-open"));
        }
        private void RenderDebugGuide(VisualElement parent,GameView view)
        {
            UpdateDebugGuide(view);
            if(guideInstructions=="") return;
            var title=Text("测试用例："+guideTitle,"body");title.name="debug-guide-title";parent.Add(title);
            var instructions=Text(guideInstructions,"tiny");instructions.name="debug-guide-instructions";parent.Add(instructions);
        }
        private void OpenDebugPositions()
        {
            if(!renderedView.Sandbox || ScenarioRunning) return;
            try
            {
                debugPositions=DebugPositions.Read(File.ReadAllText(Path.Combine(UnityEngine.Application.streamingAssetsPath,"Goa2Debug","presets.json")));
                selectedDebugPosition="";debugPositionError="";debugPresetFilter="";debugPresetsOpen=true;
            }
            catch(Exception error) { notice="调试局面不可用："+error.Message; Debug.LogWarning(error.Message); }
            Render();
        }
        private void RenderDebugPositions()
        {
            var overlay=Box("dialog-overlay"); root.Add(overlay);
            var dialog=Box("dialog"); dialog.AddToClassList("debug-preset-dialog"); overlay.Add(dialog);
            dialog.style.maxHeight=Screen.height-32;
            dialog.Add(Text("选择手工测试局面","panel-title"));
            dialog.Add(Text("卡牌用例从整个行动开始前进入；你亲自执行攻击及后续选择。加载后，右侧最近记录显示操作方法与分支。","body"));
            var filter=new TextField("查找卡牌 / 局面") {name="debug-presets-filter"};
            filter.SetValueWithoutNotify(debugPresetFilter);dialog.Add(filter);
            var list=new ScrollView {name="goa-scroll-debug-presets"};list.AddToClassList("debug-preset-list");dialog.Add(list);
            void RefreshOptions()
            {
                list.Clear();
                foreach(var position in debugPositions!.Where(p=>(p.Title+" "+p.Id).IndexOf(debugPresetFilter.Trim(),StringComparison.OrdinalIgnoreCase)>=0))
                {
                    string id=position.Id;
                    var option=Button(position.Title,()=> { selectedDebugPosition=id;debugPositionError="";Render(); },"choice-button","debug-preset-"+id);
                    if(selectedDebugPosition==id) option.AddToClassList("chosen");list.Add(option);
                }
                if(list.childCount==0) list.Add(Text("没有匹配的局面。","body"));
                RequestCapture();
            }
            RefreshOptions();filter.RegisterValueChangedCallback(e=> { debugPresetFilter=e.newValue;RefreshOptions(); });
            if(debugPositionError!="") dialog.Add(Text(debugPositionError,"restriction-text"));
            var confirm=Button("保存并加载",LoadDebugPosition,"primary-button","debug-presets-confirm");
            confirm.SetEnabled(debugPositions!.Any(p=>p.Id==selectedDebugPosition)); dialog.Add(confirm);
            dialog.Add(Button("取消，继续当前对局",()=> { debugPresetsOpen=false;debugPositionError="";Render(); },"quiet-button","debug-presets-cancel"));
        }
        private void LoadDebugPosition()
        {
            if(!renderedView.Sandbox || ScenarioRunning) return;
            try
            {
                var position=debugPositions!.Single(p=>p.Id==selectedDebugPosition);
                var next=DebugPositions.Open(catalog,position);
                if(!SaveCurrent()) { debugPositionError=notice;Render();return; }
                session=next; scenario=null; ClearPending(); debugTeleport=false; showDebug=false; debugPresetsOpen=false;
                var view=session.View(null);
                seat=view.Pending?.ChooserSeat ?? view.ActiveSeat ?? (view.UpgradingSeats.Count>0 ? view.UpgradingSeats[0] : 0);
                debugUnitId=view.Units.FirstOrDefault(u=>u.Seat==seat)?.Id ?? "";
                notice="已加载测试局面："+position.Title+"。";
            }
            catch(Exception error) { debugPositionError="无法加载局面："+error.Message;Debug.LogWarning(error.Message); }
            Render();
        }
    }
}

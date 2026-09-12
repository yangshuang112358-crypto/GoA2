#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Goa2.Infrastructure;
using UnityEngine;
using UnityEngine.UIElements;

namespace Goa2.Presentation
{
    public sealed partial class GameScreen
    {
        private bool debugPresetsOpen;
        private IReadOnlyList<DebugPosition>? debugPositions;
        private string selectedDebugPosition="",debugPositionError="",debugPresetFilter="";
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
            dialog.Add(Text("选择一个局面后，点击“保存并加载”。当前对局会先保存，新局面从指定步骤继续操作。","body"));
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

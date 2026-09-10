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
        private string selectedDebugPosition="",debugPositionError="";
        private void OpenDebugPositions()
        {
            if(!renderedView.Sandbox || ScenarioRunning) return;
            try
            {
                debugPositions=DebugPositions.Read(File.ReadAllText(Path.Combine(UnityEngine.Application.streamingAssetsPath,"Goa2Debug","presets.json")));
                selectedDebugPosition=""; debugPositionError=""; debugPresetsOpen=true;
            }
            catch(Exception error) { notice="调试局面不可用："+error.Message; Debug.LogWarning(error.Message); }
            Render();
        }
        private void RenderDebugPositions()
        {
            var overlay=Box("dialog-overlay"); root.Add(overlay);
            var dialog=Box("dialog"); dialog.AddToClassList("debug-preset-dialog"); overlay.Add(dialog);
            dialog.Add(Text("选择手工测试局面","panel-title"));
            dialog.Add(Text("选择一个局面后，点击“保存并加载”。当前对局会先保存，新局面从指定步骤继续操作。","body"));
            foreach(var position in debugPositions!)
            {
                string id=position.Id;
                var option=Button(position.Title,()=> { selectedDebugPosition=id;debugPositionError="";Render(); },"choice-button","debug-preset-"+id);
                if(selectedDebugPosition==id) option.AddToClassList("chosen");
                dialog.Add(option);
            }
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

#nullable enable
using System;
using System.IO;
using Goa2.Infrastructure;
using UnityEngine;
using UnityEngine.UIElements;

namespace Goa2.Presentation
{
    public sealed partial class GameScreen
    {
        private void RenderStartupFailure(string? savePath,string reason)
        {
            startupFailed=true;
            root.Clear();
            var overlay=Box("dialog-overlay");root.Add(overlay);
            var dialog=Box("dialog");dialog.AddToClassList("startup-dialog");overlay.Add(dialog);
            dialog.Add(Text(savePath==null ? "程序资料无法加载" : "这份存档无法读取","panel-title"));
            dialog.Add(Text(savePath==null ? "请重新解压完整程序包后启动。" : "原存档保留在原位置。修复文件后可重试，也可开始新的测试对局。","body"));
            var details=new ScrollView();details.AddToClassList("startup-details");dialog.Add(details);
            details.Add(Text(reason,"muted"));
            if(savePath!=null)
            {
                dialog.Add(Button("重新尝试读取",()=>RetryStartupSave(savePath),"primary-button","startup-retry"));
                dialog.Add(Button("开始新的测试对局",NewMatch,"choice-button","startup-new"));
            }
            dialog.Add(Button("退出程序",()=>UnityEngine.Application.Quit(),"quiet-button","startup-quit"));
            RequestCapture();
        }
        private void RetryStartupSave(string path)
        {
            try
            {
                var restored=LocalGameFactory.Restore(catalog,File.ReadAllText(path,System.Text.Encoding.UTF8));
                session=restored;startupFailed=false;seat=0;ClearPending();notice="已恢复指定存档。";Render();
            }
            catch(Exception error)
            {
                Debug.LogWarning("存档重试失败："+error.Message);
                RenderStartupFailure(path,error.Message);
            }
        }
    }
}

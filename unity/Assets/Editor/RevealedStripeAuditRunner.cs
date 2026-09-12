#nullable enable
using System;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Goa2.Editor
{
    // Run the same runtime audit inside the supported Editor when a standalone executable cannot launch.
    public static class RevealedStripeAuditRunner
    {
        public static void Run()
        {
            var args=Environment.GetCommandLineArgs();
            if(Array.IndexOf(args,"-goaStripeEditor")<0 && Array.IndexOf(args,"-goaCardReadingEditor")<0) throw new InvalidOperationException("An explicit editor audit launch is required.");
            int width=int.Parse(args[Array.IndexOf(args,"-screen-width")+1]);
            int height=int.Parse(args[Array.IndexOf(args,"-screen-height")+1]);
            EditorSceneManager.OpenScene("Assets/Scenes/Main.unity");
            // Game View sizes are an Editor API; fail visibly if this pinned Unity version changes them.
            var assembly=typeof(EditorWindow).Assembly;
            var sizesType=assembly.GetType("UnityEditor.GameViewSizes",true)!;
            var sizes=sizesType.BaseType!.GetProperty("instance",BindingFlags.Public|BindingFlags.Static)!.GetValue(null);
            var groupType=assembly.GetType("UnityEditor.GameViewSizeGroupType",true)!;
            var group=sizesType.GetMethod("GetGroup")!.Invoke(sizes,new[] {Enum.Parse(groupType,"Standalone")})!;
            var sizeType=assembly.GetType("UnityEditor.GameViewSize",true)!;
            var typeEnum=assembly.GetType("UnityEditor.GameViewSizeType",true)!;
            var size=Activator.CreateInstance(sizeType,BindingFlags.Public|BindingFlags.NonPublic|BindingFlags.Instance,null,new object[] {Enum.Parse(typeEnum,"FixedResolution"),width,height,"Goa2 layout QA"},null);
            group.GetType().GetMethod("AddCustomSize")!.Invoke(group,new[] {size});
            int index=(int)group.GetType().GetMethod("GetTotalCount")!.Invoke(group,null)!-1;
            var gameType=assembly.GetType("UnityEditor.GameView",true)!;
            var window=EditorWindow.GetWindow(gameType);
            gameType.GetProperty("selectedSizeIndex",BindingFlags.Public|BindingFlags.NonPublic|BindingFlags.Instance)!.SetValue(window,index);
            window.Show();window.Focus();
            EditorApplication.EnterPlaymode();
        }
    }
}

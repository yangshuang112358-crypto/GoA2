#nullable enable
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Goa2.Domain;
using Goa2.Infrastructure;
using Goa2.Presentation.UI3D;
using UnityEngine;
using UnityEngine.UIElements;

namespace Goa2.Presentation
{
    public sealed partial class GameScreen
    {
        [Serializable] private sealed class BoardAuditReport
        {
            public string Method="Unity Editor/Player rendered UI, synthetic calls/events; NOT real OS mouse/keyboard";
            public string UnityVersion="",Error="";
            public int Width,Height;
            public bool Passed;
            public List<string> Checks=new List<string>();
        }
        private void Start()
        {
            var args=Environment.GetCommandLineArgs();int index=Array.IndexOf(args,"-goa3dAudit");
            if(index>=0 && index+1<args.Length) StartCoroutine(BoardAuditGuard(args[index+1]));
        }
        private IEnumerator BoardAuditGuard(string output)
        {
            Directory.CreateDirectory(output);
            var report=new BoardAuditReport {UnityVersion=UnityEngine.Application.unityVersion,Width=Screen.width,Height=Screen.height};
            var routine=AuditBoard3D(output,report);
            while(true)
            {
                object? next=null;bool more=false;
                try {more=routine.MoveNext();if(more) next=routine.Current;}
                catch(Exception error) {report.Error=error.ToString();Debug.LogException(error);break;}
                if(!more) {report.Passed=true;break;}
                yield return next;
            }
            File.WriteAllText(Path.Combine(output,"report.json"),JsonUtility.ToJson(report,true));
            Debug.Log("GOA2_UI3D_AUDIT_"+(report.Passed ? "PASS" : "FAIL"));
            yield return new WaitForSecondsRealtime(.4f);
#if UNITY_EDITOR
            UnityEditor.EditorApplication.Exit(report.Passed ? 0 : 1);
#else
            UnityEngine.Application.Quit(report.Passed ? 0 : 1);
#endif
        }
        private IEnumerator AuditBoard3D(string output,BoardAuditReport report)
        {
            void Check(bool condition,string text) {if(!condition) throw new InvalidOperationException(text);report.Checks.Add(text);}
            yield return null;yield return null;
            Check(!startupFailed,"Startup content loaded");
            Submit(CommandKind.DebugPrepare,"wasp,shargatha,brogan,arien");
            yield return null;yield return null;
            Check(board?.Scene!=null,"Actual GameScreen contains 3D RenderTexture");
            ScreenCapture.CaptureScreenshot(Path.Combine(output,"initial-board.png"));yield return new WaitForSecondsRealtime(.25f);
            board!.ResetView();yield return null;
            long revision=renderedView.Revision;
            for(int step=0;step<12;step++)
            {
                var surface=board!;var scene=surface.Scene!;var size=surface.contentRect.size;
                Check(surface.RotationStep==step,"Rotation step "+step);
                foreach(var cell in catalog.Cells)
                {
                    var token=scene.Labels.FirstOrDefault(t=>t.cell==cell.Position);
                    var point=scene.Project(cell.Position,size,token.text==null ? 0 : token.top);
                    Check(scene.Hit(point,size)?.Position==cell.Position,"Pick "+step+" "+cell.Position);
                    Check(point.x>=0 && point.y>=0 && point.x<=size.x && point.y<=size.y,"Fit "+step+" "+cell.Position);
                }
                if(step==0 || step==3 || step==7)
                {ScreenCapture.CaptureScreenshot(Path.Combine(output,"board-"+step+".png"));yield return new WaitForSecondsRealtime(.25f);}
                surface.Rotate(1);yield return null;
            }
            Check(board!.RotationStep==0 && renderedView.Revision==revision,"Twelve rotations preserve revision and wrap to zero");
            board.Rotate(-1);Check(board.RotationStep==11,"Q wraps backwards");board.Rotate(1);
            var oldScene=board.Scene;Render();yield return null;yield return null;
            Check(oldScene!.Disposed && board!.Scene!=oldScene,"Snapshot rebuild disposes old scene");
            Check(board!.Scene!.TokenCount==renderedView.Units.Count,"Snapshot reconstruction matches all units");
            board.ZoomAtCenter(1.25f);board.ResetView();
            Check(Mathf.Abs(board3DViewport.Zoom-1)<.0001f,"Zoom and Home reset");
            leftExpanded=false;rightExpanded=false;topExpanded=false;bottomExpanded=false;Render();
            yield return null;yield return null;
            ScreenCapture.CaptureScreenshot(Path.Combine(output,"expanded-board.png"));yield return new WaitForSecondsRealtime(.25f);
            Check(root.Q<Button>("toggle-left")!=null && root.Q<Button>("toggle-right")!=null,"Panel collapse controls retained");
            leftExpanded=rightExpanded=topExpanded=bottomExpanded=true;Render();yield return null;yield return null;
            var source=root.Q<VisualElement>("hand-zone");var longest=catalog.Cards.OrderByDescending(c=>c.Text.Length).First();
            ShowCardPreview(source,longest,null);yield return null;yield return null;
            var preview=root.Q<VisualElement>("card-preview");var rules=preview?.Q<Label>("card-preview-rules");
            Check(rules!=null && CardTextMarkup.PlainText(rules.text)==CardTextMarkup.Description(longest),"Longest card full text retained");
            Check(preview!=null && preview.worldBound.x>=0 && preview.worldBound.y>=0 && preview.worldBound.xMax<=Screen.width+1 && preview.worldBound.yMax<=Screen.height+1,"Longest card stays inside window");
            ScreenCapture.CaptureScreenshot(Path.Combine(output,"long-card.png"));yield return new WaitForSecondsRealtime(.25f);HideCardPreview();
            // Standalone visual boundary test: legal selection, offline gating and rebuilding.
            var target=renderedView.Units.First().Position;int selections=0;
            var fixture=new BattlefieldSurface(catalog,renderedView,new[]{target},null,_=>selections++,_=>{},new BoardViewport(),Array.Empty<Hex>(),new Board3DViewport());
            fixture.style.position=Position.Absolute;fixture.style.left=0;fixture.style.top=0;fixture.style.width=Screen.width;fixture.style.height=Screen.height;root.Add(fixture);
            yield return null;yield return null;
            fixture.ResetView();yield return null;
            var pointOnTop=fixture.Scene!.Project(target,fixture.contentRect.size,fixture.Scene.Labels.First(t=>t.cell==target).top);
            Check(fixture.SelectAt(pointOnTop) && selections==1,"Legal top click routes exact selected hex");
            fixture.SetConnected(false);
            Check(!fixture.SelectAt(pointOnTop) && selections==1,"Disconnected board blocks selection");
            ScreenCapture.CaptureScreenshot(Path.Combine(output,"disconnected-board.png"));yield return new WaitForSecondsRealtime(.25f);
            Check(fixture.Scene.TokenCount==renderedView.Units.Count,"Disconnected board retains latest units");
            fixture.RemoveFromHierarchy();yield return null;
            var positions=DebugPositions.Read(File.ReadAllText(Path.Combine(UnityEngine.Application.streamingAssetsPath,"Goa2Debug","presets.json")));
            foreach(var id in new[]{"axe-ready","occupied-spawn","respawn","upgrades"})
            {
                var position=positions.FirstOrDefault(p=>p.Id==id);
                Check(position!=null,"Preset exists: "+id);
                session=DebugPositions.Open(catalog,position!);seat=session.View(0).Pending?.ChooserSeat ?? 0;ClearPending();Render();
                yield return null;yield return null;
                Check(renderedView.Phase==position.Phase,"Preset "+id+" renders original phase");
                ScreenCapture.CaptureScreenshot(Path.Combine(output,"pending-"+id+".png"));yield return new WaitForSecondsRealtime(.25f);
            }
            NewMatch();Submit(CommandKind.DebugPrepare,"wasp,shargatha,brogan,arien");yield return null;yield return null;
            // Both renderers remain usable with the exact same local session.
            board3DViewport.Enabled=false;Render();yield return null;yield return null;
            Check(board?.Scene==null,"2D fallback retained");
            board3DViewport.Enabled=true;Render();yield return null;yield return null;
            Check(board?.Scene!=null,"Return to 2.5D");
        }
    }
}

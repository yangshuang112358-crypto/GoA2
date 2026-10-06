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
            StartNetworkAudit();
            var args=Environment.GetCommandLineArgs();int index=Array.IndexOf(args,"-goa3dAudit");
            if(index>=0 && index+1<args.Length) StartCoroutine(BoardAuditGuard(args[index+1]));
        }
        private IEnumerator BoardAuditGuard(string output)
        {
            Directory.CreateDirectory(output);
            var report=new BoardAuditReport {UnityVersion=UnityEngine.Application.unityVersion,Width=Screen.width,Height=Screen.height};
            var routine=Environment.GetCommandLineArgs().Contains("-goaOpeningAuditOnly") ? AuditOpening(output,report) : Environment.GetCommandLineArgs().Contains("-goaWorldDecisionsAuditOnly") ? AuditWorldDecisions(output,report) : Environment.GetCommandLineArgs().Contains("-goaActionSequenceAuditOnly") ? AuditActionSequence(output,report) : Environment.GetCommandLineArgs().Contains("-goaBattlefieldAuditOnly") ? AuditBattlefieldLayout(output,report) : Environment.GetCommandLineArgs().Contains("-goaTerrainAuditOnly") ? AuditTerrain(output,report) : Environment.GetCommandLineArgs().Contains("-goaSkillBadgesAuditOnly") ? AuditSkillBadges(output,report) : Environment.GetCommandLineArgs().Contains("-goaSettingsAuditOnly") ? AuditSettingsButton(output,report) : AuditBoard3D(output,report);
            if(Environment.GetCommandLineArgs().Contains("-goaCombatPresentationAuditOnly"))routine=AuditCombatPresentation(output,report);
            if(Environment.GetCommandLineArgs().Contains("-goaRevisionAuditOnly"))routine=AuditUIRevision(output,report);
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
        private IEnumerator AuditBattlefieldLayout(string output,BoardAuditReport report)
        {
            void Check(bool condition,string text){if(!condition)throw new InvalidOperationException(text);report.Checks.Add(text);}
            yield return null;yield return null;Submit(CommandKind.DebugPrepare,"wasp,shargatha,brogan,arien");yield return new WaitForSecondsRealtime(4);
            Check(board!=null && board.worldBound.width>=Screen.width-2 && board.worldBound.height>=Screen.height-2,"Battlefield fills viewport");
            Check(root.Q("match-header")==null && root.Q("status-bar")==null && root.Q("world-team-status")==null && root.Q(className:"field-header")==null,"Removed brand, map header, counter and footer bars");
            Check(root.Q("match-phase")!=null && root.Q("revealed-zone")==null,"Compact phase and no empty revealed rail");
            string before=session.ExportSave();cameraFollow=false;board3DViewport.StopFollowing();board3DViewport.Focus+=new Vector3(100,0,0);yield return new WaitForSecondsRealtime(.3f);
            Check(wheelFrame!=null && !wheelFrame.worldBound.Overlaps(board!.worldBound),"Skill wheel can leave viewport with world anchor");
            board!.EmptyClick?.Invoke();yield return new WaitForSecondsRealtime(.3f);Check(wheelSeat==null,"Empty click closes wheel");
            Check(session.ExportSave()==before,"Closing and panning do not change card selection or rules");
            board3DViewport.Focus=Board3DGeometry.World(renderedView.Units.First(u=>u.Seat==seat).Position);
            ToggleHeroWheel(seat);yield return new WaitForSecondsRealtime(3);
            Check(wheelFrame!.worldBound.Overlaps(board.worldBound),"Opened wheel is visible at hero");
            Check(Vector2.Distance(wheelFrame.worldBound.center,board.LocalToWorld(board.ProjectHero(renderedView.Units.First(u=>u.Seat==seat).Position)))<3,"Wheel centered on projected hero");
            var disc=root.Q<SkillDisc>("skill-gold");var pointer=disc.worldBound.center;
            using(var e=PointerDownEvent.GetPooled(new Event{type=EventType.MouseDown,button=1,mousePosition=pointer})){e.target=disc;disc.SendEvent(e);}
            yield return null;yield return null;
            var popup=root.Q("skill-description");Check(popup!=null && popup.Q<Button>()==null,"Right click shows details with no close button");
            Check(popup!.worldBound.xMin>=0 && popup.worldBound.xMax<=Screen.width && popup.worldBound.yMax<=Screen.height,"Pointer-side tooltip fits screen");
            ScreenCapture.CaptureScreenshot(Path.Combine(output,"skill-details.png"));yield return new WaitForSecondsRealtime(.2f);
            using(var e=PointerLeaveEvent.GetPooled(new Event{type=EventType.MouseMove,mousePosition=pointer+Vector2.one*200})){e.target=disc;disc.SendEvent(e);}
            Check(root.Q("skill-description")==null,"Leaving skill immediately closes detail");
            Submit(CommandKind.DebugSelectAll);yield return new WaitForSecondsRealtime(3);
            cameraFollow=false;board3DViewport.StopFollowing();board3DViewport.Focus=BattlePresentationState.Center(catalog);board3DViewport.Zoom=1f;CloseHeroWheel();Render();yield return new WaitForSecondsRealtime(.3f);
            var cards=root.Query<VisualElement>(className:"action-card").ToList();Check(cards.Count==4,"Four revealed cards in rail");
            for(int n=0;n<4;n++){Check(cards[n].worldBound.xMin<35 && cards[n].worldBound.height>100,"Full card located at left "+n);if(n>0)Check(cards[n].worldBound.yMin>=cards[n-1].worldBound.yMax,"Revealed cards stack vertically "+n);}
            Check(root.Q("revealed-heading")==null,"No repeated revealed title or round label");
            ScreenCapture.CaptureScreenshot(Path.Combine(output,"battlefield.png"));yield return new WaitForSecondsRealtime(.3f);
            rightExpanded=true;Render();yield return null;yield return null;
            Check(root.Q("settings-commands")?.Q<Button>("keyword-open")!=null,"Former header commands live in settings");
            var path=Path.GetFullPath(Path.Combine(UnityEngine.Application.dataPath,"../../tests/scenarios/throwing-axe-reflection.json"));
            var runner=new Goa2.Infrastructure.Scenarios.ScenarioRunner(catalog,Goa2.Infrastructure.Scenarios.ScenarioRunner.Load(File.ReadAllText(path)));
            for(int n=0;n<11;n++){var step=runner.Next();Check(step.Passed,"Response fixture step "+n);}
            session=runner.Session;seat=3;rightExpanded=false;cameraFollow=true;Render();yield return new WaitForSecondsRealtime(3);
            Check(wheelSeat==0 && seat==3,"Other viewer sees discarder's wheel without identity change");
            long rev=renderedView.Revision;WheelPick("brogan-00-猛攻");Check(renderedView.Revision==rev && wheelPreview=="","Other hero's wheel remains read-only");
            seat=0;Render();WheelPick("brogan-00-猛攻");Check(renderedView.Revision==rev,"Discard preview stays local");WheelConfirm();yield return new WaitForSecondsRealtime(3);
            Submit(CommandKind.ChooseAttackTarget,"hero:1");yield return new WaitForSecondsRealtime(3);
            Check(renderedView.Pending?.Kind=="defense" && wheelSeat==1,"Attacked hero wheel opens for response");
            Check(CameraFollowPolicy.Target(renderedView,seat)==renderedView.Units.First(u=>u.Seat==1).Position,"Camera follows defender");
            string defenseSave=session.ExportSave();seat=1;Render();yield return new WaitForSecondsRealtime(.3f);
            Check(root.Q("hand-zone")==null,"Old defense hand panel migrated to wheel");
            WheelPick("wasp-10-反射屏障");Check(wheelPreview=="wasp-10-反射屏障" && confirmButton?.enabledInHierarchy==true,"Defense can be selected and confirmed in ring");
            yield return new WaitForSecondsRealtime(.3f);
            Check(Vector2.Distance(wheelFrame!.worldBound.center,board!.LocalToWorld(board.ProjectHero(renderedView.Units.First(u=>u.Seat==1).Position)))<3,"Defense wheel centered on defender");
            ScreenCapture.CaptureScreenshot(Path.Combine(output,"defense-wheel.png"));yield return new WaitForSecondsRealtime(.2f);
            WheelConfirm();yield return new WaitForSecondsRealtime(3);
            Check(renderedView.Pending?.Kind=="forced_discard" && wheelSeat==0,"Defense resumes forced discard at correct hero");
            session=LocalGameFactory.Restore(catalog,defenseSave);seat=1;Render();yield return new WaitForSecondsRealtime(3);
            WheelAlternative();WheelConfirm();yield return null;
            Check(renderedView.Events.Any(e=>e.Kind=="HeroDefeated"),"Decline defense requires confirmation and resolves through rules");
        }

        private IEnumerator AuditTerrain(string output,BoardAuditReport report)
        {
            void Check(bool condition,string text){if(!condition)throw new InvalidOperationException(text);report.Checks.Add(text);}
            yield return null;yield return null;
            Check(!startupFailed,"Content loaded");Submit(CommandKind.DebugPrepare,"wasp,shargatha,brogan,arien");
            yield return new WaitForSecondsRealtime(4);
            ToggleHeroWheel(0);cameraFollow=false;board3DViewport.StopFollowing();
            board3DViewport.Focus=(Board3DGeometry.World(new Hex(0,0))+Board3DGeometry.World(new Hex(0,1)))*.5f;
            board3DViewport.Zoom=1;topExpanded=false;Render();yield return new WaitForSecondsRealtime(.5f);
            string before=session.ExportSave();
            var filters=board!.Scene!.Camera.transform.parent.GetComponentsInChildren<MeshFilter>();
            var rocks=filters.Single(f=>f.name=="connected rocks");var vertices=rocks.sharedMesh.triangles.Select(i=>rocks.transform.TransformPoint(rocks.sharedMesh.vertices[i])).ToArray();
            Check(vertices.Length>0,"Connected rock mesh present");
            var center=board3DViewport.Focus;
            Check(vertices.All(v=>vertices.Any(w=>(w-new Vector3(2*center.x-v.x,v.y,2*center.z-v.z)).sqrMagnitude<.000001f)),"Rock vertices are centrally symmetric within 1 mm");
            var obstacleCenters=catalog.Cells.Where(c=>c.Obstacle).Select(c=>Board3DGeometry.World(c.Position)).ToArray();
            var footprint=obstacleCenters.SelectMany(c=>Enumerable.Range(0,6).Select(i=>c+new Vector3(Mathf.Cos((-30+i*60)*Mathf.Deg2Rad),0,Mathf.Sin((-30+i*60)*Mathf.Deg2Rad))))
                .Where(p=>obstacleCenters.Count(c=>Vector3.Distance(p,c)<1.001f)<3).ToArray();
            float floor=vertices.Min(v=>v.y);var bottom=vertices.Where(v=>Mathf.Abs(v.y-floor)<.001f).Select(v=>new Vector3(v.x,0,v.z)).ToArray();
            File.WriteAllLines(Path.Combine(output,"rock-footprint.tsv"),footprint.Select(p=>"expected\t"+p.x+"\t"+p.z+"\t"+bottom.Min(v=>Vector3.Distance(v,p))).Concat(bottom.Distinct().Select(p=>"actual\t"+p.x+"\t"+p.z+"\t"+footprint.Min(v=>Vector3.Distance(v,p)))));
            Check(footprint.All(p=>bottom.Any(v=>(v-p).sqrMagnitude<.000001f)) && bottom.All(v=>footprint.Any(p=>(v-p).sqrMagnitude<.000001f)),"Every exterior rock foot corner matches its canonical hex corner within 1 mm");
            var roof=Enumerable.Range(0,vertices.Length/3).Select(i=>new[]{vertices[i*3],vertices[i*3+1],vertices[i*3+2]}).Where(t=>t.All(v=>v.y>1.05f)).SelectMany(t=>t).ToArray();
            Check(roof.Length>0 && roof.Max(v=>v.y)-roof.Min(v=>v.y)<=.12f,"Ordinary rock roof relief stays below 12 cm across the board");
            string Key(Vector3 p)=>Mathf.RoundToInt(p.x*10000)+","+Mathf.RoundToInt(p.y*10000)+","+Mathf.RoundToInt(p.z*10000);
            var edges=new System.Collections.Generic.Dictionary<string,(int count,bool floor)>();
            for(int i=0;i<vertices.Length;i+=3)for(int j=0;j<3;j++){
                var a=vertices[i+j];var b=vertices[i+(j+1)%3];string ka=Key(a),kb=Key(b),key=string.CompareOrdinal(ka,kb)<0?ka+"/"+kb:kb+"/"+ka;
                edges.TryGetValue(key,out var previous);edges[key]=(previous.count+1,Mathf.Abs(a.y-floor)<.001f && Mathf.Abs(b.y-floor)<.001f);
            }
            Check(edges.Values.All(e=>e.count==2 || e.count==1 && e.floor),"Joined rocks have no open seams above the ground contact");
            var platform=filters.Where(f=>f.name.StartsWith("central spiral half")).ToList();
            if(platform.Count>0)vertices=platform.SelectMany(f=>f.sharedMesh.triangles.Select(i=>f.transform.TransformPoint(f.sharedMesh.vertices[i]))).ToArray();
            float Surface(Vector3 point){
                float height=float.NegativeInfinity;
                for(int i=0;i<vertices.Length;i+=3){
                    var a=vertices[i];var b=vertices[i+1];var c=vertices[i+2];
                    float denominator=(b.z-c.z)*(a.x-c.x)+(c.x-b.x)*(a.z-c.z);if(Mathf.Abs(denominator)<.00001f)continue;
                    float u=((b.z-c.z)*(point.x-c.x)+(c.x-b.x)*(point.z-c.z))/denominator;
                    float v=((c.z-a.z)*(point.x-c.x)+(a.x-c.x)*(point.z-c.z))/denominator;float w=1-u-v;
                    if(u>=-.00001f && v>=-.00001f && w>=-.00001f)height=Mathf.Max(height,u*a.y+v*b.y+w*c.y);
                }return height;
            }
            var samples=Enumerable.Range(0,16).Select(i=>center+new Vector3(Mathf.Cos(i*Mathf.PI/8),0,Mathf.Sin(i*Mathf.PI/8))*(Board3DScene.DecisionCoinDiameter*.5f)).Append(center);
            Check(samples.All(p=>Mathf.Abs(Surface(p)-(platform.Count>0?1.14f:Board3DScene.WallHeight))<.002f),"Coin center and 16 perimeter samples lie on a flat covered top");
            Check(filters.Any(f=>f.name=="coin platform engraving"),"Coin platform engraved ring present");
            Check(filters.Count(f=>f.name=="spawn rune backing")==filters.Count(f=>f.name.StartsWith("spawn rune ") && f.name!="spawn rune backing"),"Every spawn rune has contrast backing");
            ScreenCapture.CaptureScreenshot(Path.Combine(output,"map-overview.png"));yield return new WaitForSecondsRealtime(.3f);
            board3DViewport.Zoom=3;Render();yield return new WaitForSecondsRealtime(.5f);
            ScreenCapture.CaptureScreenshot(Path.Combine(output,"central-platform.png"));yield return new WaitForSecondsRealtime(.3f);
            board3DViewport.Focus=Board3DGeometry.World(new Hex(-7,1));board3DViewport.Zoom=2.2f;Render();yield return new WaitForSecondsRealtime(.5f);
            ScreenCapture.CaptureScreenshot(Path.Combine(output,"connected-wall.png"));yield return new WaitForSecondsRealtime(.3f);
            Check(session.ExportSave()==before,"Camera and visuals do not mutate game rules");
        }

        private IEnumerator AuditSkillBadges(string output,BoardAuditReport report)
        {
            void Check(bool condition,string text){if(!condition)throw new InvalidOperationException(text);report.Checks.Add(text);}
            yield return null;yield return null;
            Check(!startupFailed,"Startup content loaded");Submit(CommandKind.DebugPrepare,"wasp,shargatha,brogan,arien");
            yield return new WaitForSecondsRealtime(5.6f);
            string before=session.ExportSave();
            var discs=root.Query<SkillDisc>().ToList();Check(discs.Count==5,"Five skill discs visible");
            foreach(var disc in discs){
                var init=disc.Q<SkillBadge>("badge-initiative");var primary=disc.Q<SkillBadge>("badge-primary");
                Check(init!=null && primary!=null,"Initiative and primary badges present: "+disc.name);
                Check(init.worldBound.center.y>primary.worldBound.center.y && Mathf.Abs(init.worldBound.center.x-disc.worldBound.center.x)<3,"Initiative centered below primary: "+disc.name);
                foreach(var badge in disc.Query<SkillBadge>().ToList()){
                    var number=badge.Q<Label>("badge-number");
                    Check(number.worldBound.yMin>=badge.worldBound.yMin-.5f && number.worldBound.yMax<=badge.worldBound.yMax+.5f,"Number is inset in its stone: "+disc.name+"/"+badge.name);
                    Check(badge.worldBound.xMin>=disc.worldBound.xMin-.5f && badge.worldBound.xMax<=disc.worldBound.xMax+.5f && badge.worldBound.yMin>=disc.worldBound.yMin-.5f && badge.worldBound.yMax<=disc.worldBound.yMax+.5f,"Stone stays within disc footprint: "+disc.name+"/"+badge.name);
                    Check(number.worldBound.xMin>=0 && number.worldBound.xMax<=Screen.width && number.worldBound.yMin>=0 && badge.worldBound.yMax<=Screen.height,"Badge fits viewport: "+disc.name+"/"+badge.name);
                }
                var movement=disc.Q<SkillBadge>("badge-movement");var defense=disc.Q<SkillBadge>("badge-defense");var range=disc.Q<SkillBadge>("badge-range");
                if(movement!=null)Check(movement.worldBound.center.x<disc.worldBound.center.x && movement.worldBound.center.y<disc.worldBound.center.y,"Movement at upper left: "+disc.name);
                if(defense!=null)Check(defense.worldBound.center.x>disc.worldBound.center.x && defense.worldBound.center.y<disc.worldBound.center.y,"Defense at upper right: "+disc.name);
                Check(primary.worldBound.center.x<disc.worldBound.center.x,"Primary at lower left: "+disc.name);
                if(range!=null)Check(range.worldBound.center.x>disc.worldBound.center.x && range.worldBound.center.y>disc.worldBound.center.y,"Range at lower right: "+disc.name);
            }
            ScreenCapture.CaptureScreenshot(Path.Combine(output,"skill-badges.png"));yield return new WaitForSecondsRealtime(.25f);
            var gold=root.Q<SkillDisc>("skill-gold");using(var e=PointerEnterEvent.GetPooled(new Event{type=EventType.MouseMove,mousePosition=gold.worldBound.center})){e.target=gold;gold.SendEvent(e);}
            yield return new WaitForSecondsRealtime(.4f);ScreenCapture.CaptureScreenshot(Path.Combine(output,"skill-badges-hover.png"));yield return new WaitForSecondsRealtime(.25f);
            Check(session.ExportSave()==before,"Visual inspection does not alter rules");
            foreach(string asset in new[]{"front","back","boot","shield","sword","spark","range","arrow","speed"})Check(SkillDiscArtwork.Texture(asset)!=null,"Blender art imported: "+asset);
            OpenArtSamples(4);yield return new WaitForSecondsRealtime(1);
            Check(root.Q("skill-disc-samples")!=null,"Debug gallery exposes live skill samples");
            ScreenCapture.CaptureScreenshot(Path.Combine(output,"skill-stone-samples.png"));yield return new WaitForSecondsRealtime(.3f);
            var sampleImage=new Texture2D(2,2,TextureFormat.RGBA32,false);
            try{
                sampleImage.LoadImage(File.ReadAllBytes(Path.Combine(output,"skill-stone-samples.png")));
                var reverse=root.Q<SkillDisc>("sample-discarded");var point=reverse.worldBound.center-new Vector2(0,60);
                Color pixel=sampleImage.GetPixel(Mathf.RoundToInt(point.x),sampleImage.height-1-Mathf.RoundToInt(point.y));
                Check(pixel.r+pixel.g+pixel.b>.65f,"Discarded reverse gold casing remains visible after full half turn");
            }finally{UnityEngine.Object.Destroy(sampleImage);}
            var positive=root.Q<SkillDisc>("sample-boosted").Q<SkillBadge>("badge-primary").Q<Label>("badge-number");
            var negative=root.Q<SkillDisc>("sample-reduced").Q<SkillBadge>("badge-primary").Q<Label>("badge-number");
            Check(positive.resolvedStyle.color.g>positive.resolvedStyle.color.r && negative.resolvedStyle.color.r>negative.resolvedStyle.color.g,"Live bonus values retain green increases and red reductions");
            Check(root.Q<SkillDisc>("sample-infinity").Q<SkillBadge>("badge-primary").Q<Label>("badge-number").text=="∞","Conditional defense retains infinity");
            Check(session.ExportSave()==before,"Gallery samples do not change real rules or player bonuses");
        }

        private IEnumerator AuditSettingsButton(string output, BoardAuditReport report)
        {
            void Check(bool condition,string text) {if(!condition)throw new InvalidOperationException(text);report.Checks.Add(text);}
            void Down(StoneSettingsButton button) {
                using(var e=PointerDownEvent.GetPooled(new Event{type=EventType.MouseDown,button=0,mousePosition=button.worldBound.center})){e.target=button;button.SendEvent(e);}
            }
            void Up(StoneSettingsButton button) {
                using(var e=PointerUpEvent.GetPooled(new Event{type=EventType.MouseUp,button=0,mousePosition=button.worldBound.center})){e.target=button;button.SendEvent(e);}
            }
            yield return null;yield return null;
            Check(!startupFailed,"Startup content loaded");
            Submit(CommandKind.DebugPrepare,"wasp,shargatha,brogan,arien");
            yield return new WaitForSecondsRealtime(1.2f);
            var settings=root.Q<StoneSettingsButton>("settings-toggle");
            Check(settings!=null,"Sculpted button is connected to live GameScreen");
            var bounds=settings.worldBound;string before=session.ExportSave();int originalSeat=seat;
            Check(bounds.xMin>=0 && bounds.yMin>=0 && bounds.xMax<=Screen.width && bounds.yMax<=Screen.height,"Settings hit area stays inside screen");
            ScreenCapture.CaptureScreenshot(Path.Combine(output,"settings-normal.png"));yield return new WaitForSecondsRealtime(.15f);
            Vector2 hoverPoint=new Vector2(bounds.xMax-12,bounds.yMin+16);
            using(var e=PointerMoveEvent.GetPooled(new Event{type=EventType.MouseMove,mousePosition=hoverPoint})){e.target=settings;settings.SendEvent(e);}
            yield return new WaitForSecondsRealtime(.3f);
            Check(settings.HoverAmount>.8f && settings.worldBound==bounds,"Hover animates artwork while preserving the hit area");
            ScreenCapture.CaptureScreenshot(Path.Combine(output,"settings-hover.png"));yield return new WaitForSecondsRealtime(.15f);
            Down(settings);yield return new WaitForSecondsRealtime(.14f);
            Check(settings.PressAmount>.5f && !rightExpanded,"Pointer down depresses stone without opening early");
            ScreenCapture.CaptureScreenshot(Path.Combine(output,"settings-pressed.png"));yield return new WaitForSecondsRealtime(.15f);
            Up(settings);yield return new WaitForSecondsRealtime(.45f);
            Check(rightExpanded && root.Q<VisualElement>("operation-panel")!=null,"Pointer release opens the existing settings drawer");
            var opened=root.Q<StoneSettingsButton>("settings-toggle");
            Check(opened.PressAmount<.45f,"Rebuilt button rebounds instead of remaining stuck pressed");
            Check(opened.worldBound.yMax<root.Q<VisualElement>("operation-panel").worldBound.yMin,"Button remains above drawer");
            ScreenCapture.CaptureScreenshot(Path.Combine(output,"settings-open.png"));yield return new WaitForSecondsRealtime(.15f);
            Down(opened);yield return new WaitForSecondsRealtime(.1f);Up(opened);yield return new WaitForSecondsRealtime(.45f);
            Check(!rightExpanded,"Second click closes drawer");
            Check(session.ExportSave()==before && seat==originalSeat,"All button animations and drawer toggles leave rules and identity unchanged");
        }

        private IEnumerator AuditBoard3D(string output,BoardAuditReport report)
        {
            void Check(bool condition,string text) {if(!condition) throw new InvalidOperationException(text);report.Checks.Add(text);}
            yield return null;yield return null;
            Check(!startupFailed,"Startup content loaded");
            Submit(CommandKind.DebugPrepare,"wasp,shargatha,brogan,arien");
            yield return null;yield return null;
            Check(board?.Scene!=null,"Actual GameScreen contains 3D RenderTexture");
            yield return new WaitForSecondsRealtime(9f);
            Check(cameraFollow && !rightExpanded,"Default follow enabled and settings drawer closed");
            Check(root.Q<Button>("settings-toggle")!=null && root.Q<Button>("follow-toggle")!=null,"Floating settings and follow buttons exist");
            Check(Vector3.Distance(board3DViewport.Focus,Board3DGeometry.World(renderedView.Units.First(u=>u.Seat==seat).Position))<.03f,"Planning follows own hero");
            ScreenCapture.CaptureScreenshot(Path.Combine(output,"follow-planning.png"));yield return new WaitForSecondsRealtime(.25f);
            Check(root.Q<SkillWheel>("skill-wheel")!=null && root.Query<SkillDisc>().ToList().Count==5,"Planning opens five skill discs");
            Check(root.Q<VisualElement>("hand-zone")==null,"Migrated planning hand panel removed");
            string wheelGold=renderedView.OwnCards.First(c=>catalog.Card(c.CardId).Color=="gold").CardId;
            string wheelSilver=renderedView.OwnCards.First(c=>catalog.Card(c.CardId).Color=="silver").CardId;
            WheelPick(wheelGold);Check(renderedView.OwnCards.Any(c=>c.CardId==wheelGold && c.Zone==CardZone.Selected),"Wheel selects a real card");
            WheelPick(wheelSilver);Check(renderedView.OwnCards.Any(c=>c.CardId==wheelSilver && c.Zone==CardZone.Selected),"Wheel changes selection before reveal");
            WheelPick(wheelSilver);Check(!renderedView.OwnCards.Any(c=>c.Zone==CardZone.Selected),"Click selected skill again cancels authority selection");
            yield return null;yield return null;
            var goldDisc=root.Q<SkillDisc>("skill-gold");long previewRevision=renderedView.Revision;
            using(var skillRight=PointerDownEvent.GetPooled(new Event{type=EventType.MouseDown,button=1,mousePosition=goldDisc.worldBound.center})){skillRight.target=goldDisc;goldDisc.SendEvent(skillRight);}
            Check(root.Q<VisualElement>("skill-description")!=null && renderedView.Revision==previewRevision,"Right-click skill reads without command");skillPopup?.RemoveFromHierarchy();skillPopup=null;
            ToggleHeroWheel(1);yield return null;yield return null;long readOnlyRevision=renderedView.Revision;
            var otherDisc=root.Q<SkillDisc>("skill-gold");using(var otherClick=PointerDownEvent.GetPooled(new Event{type=EventType.MouseDown,button=0,mousePosition=otherDisc.worldBound.center})){otherClick.target=otherDisc;otherDisc.SendEvent(otherClick);}
            Check(renderedView.Revision==readOnlyRevision && seat==0,"Other hero wheel cannot act or change identity");ToggleHeroWheel(0);yield return null;yield return null;
            var ownPlate=board!.Q<HeroPlate>("hero-plate-"+(seat+1));
            using(var hoverEvent=PointerMoveEvent.GetPooled(new Event{type=EventType.MouseMove,mousePosition=ownPlate.worldBound.center}))ownPlate.SendEvent(hoverEvent);
            Check(root.Q<VisualElement>("hero-hover")==null,"Hovering hero does not open inspection");
            using(var clickEvent=PointerDownEvent.GetPooled(new Event{type=EventType.MouseDown,button=1,mousePosition=ownPlate.worldBound.center})){clickEvent.target=ownPlate;ownPlate.SendEvent(clickEvent);}
            yield return null;yield return null;
            Check(root.Q<VisualElement>("hero-hover")!=null,"Right-click hero opens full card information");
            ScreenCapture.CaptureScreenshot(Path.Combine(output,"hero-hover-own.png"));yield return new WaitForSecondsRealtime(.25f);HideHeroHover();
            ShowHeroHover(1,new Vector2(Screen.width*.6f,Screen.height*.5f));yield return null;yield return null;
            Check(!root.Q<VisualElement>("hero-hover").Query<Label>().ToList().Any(l=>l.text=="未公开手牌隐藏"),"Opponent inspection omits hidden-hand notice");Check(root.Q<VisualElement>("hero-card-grid")!=null,"Two-column hero inspection grid exists");Check(root.Q<VisualElement>("hero-hover").Q<Button>("card-keywords")==null,"Inspection has no glossary button");HideHeroHover();
            long followRevision=renderedView.Revision;int followSeat=seat;
            SetCameraFollow(false);yield return new WaitForSecondsRealtime(.25f);
            Check(root.Q(className:"flow-return")!=null,"Free mode offers unified flow return");
            Render();yield return null;
            Check(!cameraFollow && seat==followSeat && renderedView.Revision==followRevision,"Follow toggle survives rebuild without command or identity change");
            yield return new WaitForSecondsRealtime(1.5f);
            Check(root.Q("follow-toast")==null && root.Q("follow-toggle")==null,"No redundant follow controls or toast");
            rightExpanded=true;showHotkeys=true;Render();yield return null;yield return null;
            var drawer=root.Q<VisualElement>("operation-panel");
            Check(drawer.worldBound.xMax<=Screen.width && drawer.worldBound.yMax<=Screen.height,"Settings drawer stays inside viewport");
            Check(drawer.Query<Label>().ToList().Any(l=>l.text.Contains("Enter /")),"Hotkey list documents Enter confirm");
            ScreenCapture.CaptureScreenshot(Path.Combine(output,"settings-hotkeys.png"));yield return new WaitForSecondsRealtime(.25f);
            showHotkeys=false;rightExpanded=false;Render();yield return null;yield return null;

            foreach(var unit in renderedView.Units)
            {
                var token=board!.Scene!.Labels.Single(t=>t.cell==unit.Position);
                if(unit.Seat.HasValue)
                {
                    string hero=catalog.Heroes.Single(h=>h.Id==renderedView.Players.Single(p=>p.Seat==unit.Seat).HeroId).Name;
                    Check(token.text==hero,"Full hero name: "+hero);
                }
                else Check(new[]{"近","远","重"}.Contains(token.text),"Minion label: "+token.text);
            }
            board!.Rotate(1);yield return new WaitForSecondsRealtime(.08f);
            Check(board3DViewport.Yaw>0 && board3DViewport.Yaw<30,"Rendered intermediate rotation angle");
            yield return new WaitForSecondsRealtime(.25f);board.Rotate(-1);yield return new WaitForSecondsRealtime(.3f);
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
                    Check(token.text!=null ? scene.Hit(point,size)?.Position==cell.Position :
                        Board3DGeometry.HexAt(scene.Ground(point,size))==cell.Position,"Pick "+step+" "+cell.Position);
                    Check(point.x>=0 && point.y>=0 && point.x<=size.x && point.y<=size.y,"Fit "+step+" "+cell.Position);
                }
                if(step==0 || step==3 || step==7)
                {ScreenCapture.CaptureScreenshot(Path.Combine(output,"board-"+step+".png"));yield return new WaitForSecondsRealtime(.25f);}
                surface.Rotate(1);yield return new WaitForSecondsRealtime(.3f);
            }
            Check(board!.RotationStep==0 && renderedView.Revision==revision,"Twelve rotations preserve revision and wrap to zero");
            board.Rotate(-1);Check(board.RotationStep==11,"Q wraps backwards");yield return new WaitForSecondsRealtime(.3f);board.Rotate(1);yield return new WaitForSecondsRealtime(.3f);
            var oldScene=board.Scene;Render();yield return null;yield return null;
            Check(oldScene!.Disposed && board!.Scene!=oldScene,"Snapshot rebuild disposes old scene");
            Check(board!.Scene!.TokenCount==renderedView.Units.Count,"Snapshot reconstruction matches all units");
            board.ZoomAtCenter(1.25f);board.ResetView();
            Check(Mathf.Abs(board3DViewport.Zoom-1)<.0001f,"Zoom and Home reset");
            leftExpanded=false;rightExpanded=false;topExpanded=false;bottomExpanded=false;Render();
            yield return null;yield return null;
            ScreenCapture.CaptureScreenshot(Path.Combine(output,"expanded-board.png"));yield return new WaitForSecondsRealtime(.25f);
            board!.ZoomAtCenter(1.8f);board.Rotate(1);yield return new WaitForSecondsRealtime(.3f);
            ScreenCapture.CaptureScreenshot(Path.Combine(output,"tall-pieces.png"));yield return new WaitForSecondsRealtime(.25f);
            board.FocusAt(renderedView.Units.First(u=>u.Seat==0).Position);yield return null;
            ScreenCapture.CaptureScreenshot(Path.Combine(output,"hero-full-name.png"));yield return new WaitForSecondsRealtime(.25f);
            board.ResetView();board.Rotate(-1);yield return new WaitForSecondsRealtime(.3f);
            Check(root.Q<VisualElement>("hero-roster")==null && root.Q<Button>("toggle-right")!=null,"Panel collapse controls retained");
            leftExpanded=rightExpanded=topExpanded=bottomExpanded=true;Render();yield return null;yield return null;
            var source=root.Q<VisualElement>("hand-zone") ?? root.Q<VisualElement>("skill-wheel");var longest=catalog.Cards.OrderByDescending(c=>c.Text.Length).First();
            ShowCardPreview(source,longest,null);yield return null;yield return null;
            var preview=root.Q<VisualElement>("card-preview");var rules=preview?.Q<Label>("card-preview-rules");
            Check(rules!=null && CardTextMarkup.PlainText(rules.text)==CardTextMarkup.Description(longest),"Longest card full text retained");
            Check(preview!=null && preview.worldBound.x>=0 && preview.worldBound.y>=0 && preview.worldBound.xMax<=Screen.width+1 && preview.worldBound.yMax<=Screen.height+1,"Longest card stays inside window");
            ScreenCapture.CaptureScreenshot(Path.Combine(output,"long-card.png"));yield return new WaitForSecondsRealtime(.25f);HideCardPreview();
            foreach(var card in catalog.Cards)
            {
                var numbers=new VisualElement();CompactCardNumbers(numbers,card,renderedView.Players[0],false,"audit");
                Check(numbers.Query<Label>().ToList().Any(l=>l.text==CardDisplay.Primary(card)),"Unified card value: "+card.Id);
            }
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
                Check(renderedView.Phase==position!.Phase,"Preset "+id+" renders original phase");
                SetCameraFollow(true);yield return new WaitForSecondsRealtime(9f);
                var expected=CameraFollowPolicy.Target(renderedView,seat);
                Check(board!=null && board.worldBound.height>=59,"Board remains visible in "+id);
                if(expected.HasValue) Check(Vector3.Distance(board3DViewport.Focus,Board3DGeometry.World(expected.Value))<.03f,"Live phase camera target: "+id);
                else {
                    Check(followOverview,"Live phase overview: "+id);
                    if(id=="occupied-spawn") Check(Vector3.Distance(board3DViewport.Focus,BattlePresentationState.Center(catalog,renderedView.CombatRegion))<.03f,"Captain spawn camera centers actual combat region");
                }
                SetCameraFollow(false);
                ScreenCapture.CaptureScreenshot(Path.Combine(output,"pending-"+id+".png"));yield return new WaitForSecondsRealtime(.25f);
            }
            session=DebugPositions.Open(catalog,positions.Single(p=>p.Id=="axe-ready"));seat=0;ClearPending();Render();
            Submit(CommandKind.BeginPrimary);yield return null;yield return null;
            Check(WheelDiscard(renderedView) && root.Q<VisualElement>("hand-zone")==null,"Optional discard moved into battlefield wheel");
            string wheelBefore=session.ExportSave();WheelPick("brogan-00-猛攻");Check(session.ExportSave()==wheelBefore,"Discard preselection emits no command");
            Check(root.Q<Button>("wheel-confirm").enabledInHierarchy,"Local magic confirmation appears after discard preselection");
            yield return new WaitForSecondsRealtime(.5f);
            var wheelBounds=wheelFrame!.worldBound;var boardBounds=board!.worldBound;
            Check(wheelBounds.xMin>=boardBounds.xMin && wheelBounds.xMax<=boardBounds.xMax && wheelBounds.yMin>=boardBounds.yMin && wheelBounds.yMax<=boardBounds.yMax,"Discard wheel fits board viewport: "+wheelBounds+" in "+boardBounds);
            var alternativeBounds=skillWheel!.Alternative.worldBound;
            Check(!skillWheel.Confirm.worldBound.Overlaps(alternativeBounds) && !skillWheel.Query<SkillDisc>().ToList().Any(d=>d.worldBound.Overlaps(alternativeBounds)),"Discard alternative does not overlap confirm or skill controls");
            Check(wheelBounds.yMax<=Screen.height && (!rightExpanded || wheelBounds.xMax<root.Q<VisualElement>("operation-panel").worldBound.xMin),"Discard wheel remains on screen and outside settings drawer");
            ScreenCapture.CaptureScreenshot(Path.Combine(output,"wheel-discard-confirm.png"));yield return new WaitForSecondsRealtime(.25f);
            ToggleHeroWheel(0);Check(session.ExportSave()==wheelBefore,"Closing discard wheel does not commit");ToggleHeroWheel(0);WheelPick("brogan-00-猛攻");WheelConfirm();
            Check(wheelState.Discards.Count>0 && renderedView.Players[0].PublicDiscards.Any(c=>c.CardId=="brogan-00-猛攻"),"Confirmed discard queues public result animation");
            yield return new WaitForSecondsRealtime(1.7f);Check(root.Q<VisualElement>("hand-zone")==null,"Public discard animation does not restore legacy hand panel");ScreenCapture.CaptureScreenshot(Path.Combine(output,"wheel-discard-back.png"));yield return new WaitForSecondsRealtime(1f);
            Check(wheelState.Discards.Count==0,"Public discard animation closes without another command");
            Submit(CommandKind.ChooseAttackTarget,"hero:1");
            seat=1;Render();yield return null;yield return null;
            Check(renderedView.Pending?.Kind=="defense","Live defense choice reached through rules");
            SetCameraFollow(true);yield return new WaitForSecondsRealtime(9f);
            Check(seat==1 && renderedView.ActiveSeat==0 && Vector3.Distance(board3DViewport.Focus,Board3DGeometry.World(renderedView.Units.First(u=>u.Seat==1).Position))<.03f,"Defense response focuses defender without changing identity");
            SetCameraFollow(false);
            int warnings=0;
            foreach(var option in renderedView.DefenseOptions)
            {
                var button=root.Q<Button>("defense-option-"+option.CardId);
                bool warn=CardDisplay.WarnDefense(catalog.Card(option.CardId),option.Assessment);
                Check(button!=null && (button.text.Contains("数值偏低")==warn),"Defense numeric warning: "+option.CardId);
                if(warn) {warnings++;Check(button!.resolvedStyle.backgroundColor.r>.4f,"Pink background applied");}
            }
            Check(warnings>0,"Defense fixture includes insufficient finite defense");
            ScreenCapture.CaptureScreenshot(Path.Combine(output,"defense-warning.png"));yield return new WaitForSecondsRealtime(.25f);
            NewMatch();Submit(CommandKind.DebugPrepare,"wasp,shargatha,brogan,arien");yield return null;yield return null;
            // Both renderers remain usable with the exact same local session.
            board3DViewport.Enabled=false;Render();yield return null;yield return null;
            Check(board?.Scene==null,"2D fallback retained");
            board3DViewport.Enabled=true;Render();yield return null;yield return null;
            Check(board?.Scene!=null,"Return to 2.5D");
            topExpanded=false;bottomExpanded=false;rightExpanded=false;Render();yield return null;yield return null;
            SetCameraFollow(false);board!.ResetView();yield return null;
            var opposite=renderedView.DecisionCoin==Team.Blue ? "red" : "blue";
            Submit(CommandKind.DebugSetCoin,opposite);yield return new WaitForSecondsRealtime(.13f);
            Check(board3DViewport.Presentation.CoinTo==renderedView.DecisionCoin,"Coin animation consumes authoritative side");
            ScreenCapture.CaptureScreenshot(Path.Combine(output,"coin-flipping.png"));yield return new WaitForSecondsRealtime(2f);
            board3DViewport.Focus=(Board3DGeometry.World(new Hex(0,0))+Board3DGeometry.World(new Hex(0,1)))*.5f;board3DViewport.Zoom=8;yield return new WaitForSecondsRealtime(.25f);
            var renderedObjects=board.Scene!.Camera.transform.parent.GetComponentsInChildren<Transform>();
            var mintedCoin=renderedObjects.Single(t=>t.name=="decision coin");var restingPosition=mintedCoin.localPosition;
            Check(Mathf.Abs(mintedCoin.localScale.x*2-Board3DScene.DecisionCoinDiameter)<.001f,"Coin diameter fills the central tray");
            Check(renderedObjects.Count(t=>t.name.StartsWith("crystal life "))==renderedView.BlueCrystal+renderedView.RedCrystal,"No extra oversized decorative crystal");
            var life=renderedObjects.First(t=>t.name.StartsWith("crystal life "));var lifePosition=life.localPosition;
            yield return new WaitForSecondsRealtime(.3f);
            Check((mintedCoin.localPosition-restingPosition).sqrMagnitude<.000001f,"Settled coin does not hover");
            Check((life.localPosition-lifePosition).sqrMagnitude>.00000001f,"Life crystals float");
            ScreenCapture.CaptureScreenshot(Path.Combine(output,"coin-grounded-closeup.png"));yield return new WaitForSecondsRealtime(.25f);board.ResetView();yield return null;
            int crystalBefore=renderedView.RedCrystal;
            Submit(CommandKind.DebugDefeatHero,"hero:1",target:0);yield return new WaitForSecondsRealtime(.35f);
            Check(renderedView.RedCrystal==crystalBefore-1 && board3DViewport.Presentation.Shards.Count>0,"Live hero defeat drives crystal shards from actual damage");
            ScreenCapture.CaptureScreenshot(Path.Combine(output,"crystal-shatter.png"));yield return new WaitForSecondsRealtime(2f);
            var heavy=renderedView.Units.First(u=>u.Team==Team.Red && u.Kind=="heavy");var from=heavy.Position;int marks=renderedView.BlueMarks;
            Submit(CommandKind.DebugDefeatMinion,heavy.Id,target:0);yield return new WaitForSecondsRealtime(.8f);
            Check(renderedView.BlueMarks==marks+1 && board3DViewport.Presentation.Crowns.Any(c=>c.From==Board3DGeometry.World(from,1)),"Live heavy defeat drives crown from removed unit to winning team");
            ScreenCapture.CaptureScreenshot(Path.Combine(output,"crown-flight.png"));yield return new WaitForSecondsRealtime(2f);
            ScreenCapture.CaptureScreenshot(Path.Combine(output,"crown-arrived.png"));yield return new WaitForSecondsRealtime(.25f);
            // Explicit presentation fixture: level-eight art and card-zone palette, not a fabricated rules playthrough.
            var showcase=session.View(0);showcase.Players[0].Level=8;showcase.Players[0].Gold=99;
            showcase.OwnCards[0].Zone=CardZone.PlayedResolved;showcase.OwnCards[1].Zone=CardZone.Discarded;
            var showcaseHero=showcase.Units.First(u=>u.Seat==0);
            var art=new BattlefieldSurface(catalog,showcase,Array.Empty<Hex>(),null,_=>{},_=>{},new BoardViewport(),Array.Empty<Hex>(),new Board3DViewport{Enabled=true,Initialized=true,Focus=Board3DGeometry.World(showcaseHero.Position),Zoom=4},0);
            art.style.position=Position.Absolute;art.style.left=0;art.style.top=0;art.style.width=Screen.width;art.style.height=Screen.height;root.Add(art);
            yield return null;yield return null;
            Check(art.Query<HeroPlate>().ToList().Count==showcase.Units.Count(u=>u.Seat.HasValue),"World nameplates replace roster for every on-board hero");
            ScreenCapture.CaptureScreenshot(Path.Combine(output,"level8-art-fixture-a.png"));yield return new WaitForSecondsRealtime(.4f);
            ScreenCapture.CaptureScreenshot(Path.Combine(output,"level8-art-fixture-b.png"));yield return new WaitForSecondsRealtime(.25f);art.RemoveFromHierarchy();
            var realView=renderedView;renderedView=showcase;showcase.Players[0].PurpleCardId=catalog.Cards.First(c=>c.HeroId==showcase.Players[0].HeroId && c.Color=="purple").Id;
            ShowHeroHover(0,new Vector2(Screen.width*.5f,0));yield return null;yield return null;
            Check(root.Q<VisualElement>("hero-card-grid").childCount==6,"Level-eight inspection has six slots in two columns");
            ScreenCapture.CaptureScreenshot(Path.Combine(output,"hero-inspection-level8-fixture.png"));yield return new WaitForSecondsRealtime(.25f);HideHeroHover();renderedView=realView;
        }
    }
}

#nullable enable
using System;
using System.IO;
using UnityEngine;

namespace Goa2.Presentation
{
    public sealed partial class GameScreen
    {
        private string lastBoardTelemetry="";
        [Serializable] private sealed class BoardTelemetry
        {
            public bool Is3D,Connected,Follow,Overview,SettingsOpen;
            public string Toast="";
            public int Step,Seat,Units;
            public long Revision;
            public float Zoom;
            public Vector3 Focus;
            public Rect Bounds;
        }
        // Only explicit -goaScreenshot QA launches write this sidecar. The old 2D QA
        // layout's Zoom/Focus fields are not evidence for the 3D camera.
        private void LateUpdate()
        {
            if(screenshotPath==null || board==null || board.panel==null) return;
            var data=new BoardTelemetry {Is3D=board3DViewport.Enabled,Connected=board.Connected,
                Step=board.RotationStep,Seat=seat,Units=board.Scene?.TokenCount ?? renderedView.Units.Count,
                Follow=cameraFollow,Overview=followOverview,SettingsOpen=rightExpanded,Toast="",
                Revision=renderedView.Revision,Zoom=board3DViewport.Zoom,Focus=board3DViewport.Focus,Bounds=board.worldBound};
            string json=JsonUtility.ToJson(data,true);
            if(json==lastBoardTelemetry) return;
            string path=Path.ChangeExtension(screenshotPath,"board3d.json");
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);File.WriteAllText(path,json);lastBoardTelemetry=json;
        }
    }
}

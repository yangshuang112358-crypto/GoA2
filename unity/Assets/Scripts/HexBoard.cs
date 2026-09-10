#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using Goa2.Domain;
using UnityEngine;
using UnityEngine.UIElements;

namespace Goa2.Presentation
{
    public sealed class BoardViewport
    {
        public float Zoom = 1;
        public Vector2 Focus;
        public bool Initialized;
        public bool InitialScaleApplied;
    }
    public sealed class HexBoard : VisualElement
    {
        private readonly ContentCatalog catalog;
        private readonly GameView view;
        private readonly HashSet<Hex> legal;
        private readonly HashSet<Hex> effectArea;
        private readonly Hex? selected;
        private readonly Action<Hex> choose;
        private readonly Action<CellDefinition> hover;
        private readonly List<(Label label, UnitState unit)> labels = new List<(Label, UnitState)>();
        private float radius;
        private Vector2 origin;
        private readonly BoardViewport viewport;
        private bool dragging;
        private int dragPointer;
        private Vector2 lastPointer;
        public Action? ViewportChanged;
        public float HexRadius => radius;

        public HexBoard(ContentCatalog catalog, GameView view, IEnumerable<Hex> legal, Hex? selected, Action<Hex> choose, Action<CellDefinition> hover, BoardViewport viewport, IEnumerable<Hex>? effectArea=null)
        {
            this.catalog = catalog; this.view = view; this.legal = new HashSet<Hex>(legal);
            this.selected = selected; this.choose = choose; this.hover = hover;
            this.viewport = viewport;
            this.effectArea = new HashSet<Hex>(effectArea ?? Enumerable.Empty<Hex>());
            name = "hex-board"; AddToClassList("hex-board");
            generateVisualContent += Draw;
            RegisterCallback<GeometryChangedEvent>(_ => LayoutBoard());
            RegisterCallback<PointerMoveEvent>(e =>
            {
                if (dragging && e.pointerId == dragPointer)
                {
                    Vector2 pointer = e.localPosition;
                    viewport.Focus -= (pointer - lastPointer) / radius;
                    lastPointer = pointer; LayoutBoard(); e.StopPropagation(); return;
                }
                var cell = Hit(e.localPosition);
                if (cell != null) hover(cell);
            });
            RegisterCallback<PointerDownEvent>(e =>
            {
                if (e.button == 1 || e.button == 2)
                {
                    dragging = true; dragPointer = e.pointerId; lastPointer = e.localPosition;
                    this.CapturePointer(e.pointerId); e.StopPropagation(); return;
                }
                if (e.button != 0) return;
                var cell = Hit(e.localPosition);
                if (cell != null) choose(cell.Position);
            });
            RegisterCallback<PointerUpEvent>(e =>
            {
                if (!dragging || e.pointerId != dragPointer) return;
                dragging = false; this.ReleasePointer(e.pointerId); e.StopPropagation();
            });
            RegisterCallback<PointerCaptureOutEvent>(_ => dragging = false);
            RegisterCallback<WheelEvent>(e =>
            {
                ZoomAround(e.localMousePosition, Mathf.Pow(1.12f, -e.delta.y / 3f)); e.StopPropagation();
            });
            foreach (var unit in view.Units)
            {
                var label = new Label(unit.Seat.HasValue ? (unit.Seat.Value + 1).ToString() : unit.Kind == "heavy" ? "重" : unit.Kind == "ranged" ? "弓" : "兵");
                label.AddToClassList("unit-label"); label.pickingMode = PickingMode.Ignore;
                Add(label); labels.Add((label, unit));
            }
        }
        private static Vector2 World(Hex at) => new Vector2(Mathf.Sqrt(3) * (at.X + at.Y * .5f), at.Y * 1.5f);
        private Vector2 Center(Hex at) => origin + World(at) * radius;
        public Vector2 PanelCenter(Hex at) => this.LocalToWorld(Center(at));
        public void ResetView() { viewport.Initialized = false; viewport.Zoom = 1; LayoutBoard(); }
        public void FocusAt(Hex cell)
        {
            viewport.Initialized=true; viewport.Focus=World(cell); LayoutBoard();
            if (radius<=0) return;
            viewport.Zoom=Mathf.Clamp(viewport.Zoom*20/radius,.25f,40f); LayoutBoard();
        }
        public void ZoomAtCenter(float factor) => ZoomAround(contentRect.center, factor);
        private void ZoomAround(Vector2 pointer, float factor)
        {
            if (radius <= 1) return;
            var anchor = (pointer - origin) / radius;
            viewport.Zoom = Mathf.Clamp(viewport.Zoom * factor, .25f, 40f);
            LayoutBoard();
            viewport.Focus = anchor - (pointer - contentRect.center) / radius;
            LayoutBoard();
        }
        private void LayoutBoard()
        {
            if (contentRect.width <= 0 || contentRect.height <= 0) return;
            var points = catalog.Cells.Select(c => World(c.Position)).ToList();
            float minX = points.Min(p => p.x) - 1, maxX = points.Max(p => p.x) + 1;
            float minY = points.Min(p => p.y) - 1, maxY = points.Max(p => p.y) + 1;
            if (!viewport.Initialized) { viewport.Focus = new Vector2((minX + maxX) * .5f, (minY + maxY) * .5f); viewport.Initialized = true; }
            float padding=Mathf.Min(18,Mathf.Min(contentRect.width,contentRect.height)*.07f);
            float fittedRadius = Mathf.Max(1, Mathf.Min((contentRect.width - padding*2) / (maxX - minX), (contentRect.height - padding*2) / (maxY - minY)));
            if (!viewport.InitialScaleApplied)
            {
                viewport.Zoom = Mathf.Max(1, 14 / fittedRadius);
                viewport.InitialScaleApplied = true;
            }
            radius = fittedRadius * viewport.Zoom;
            origin = contentRect.center - viewport.Focus * radius;
            foreach (var pair in labels)
            {
                var center = Center(pair.unit.Position);
                pair.label.style.display=radius >= (pair.unit.Seat.HasValue ? 5 : 9) ? DisplayStyle.Flex : DisplayStyle.None;
                pair.label.style.left = center.x - radius; pair.label.style.top = center.y - radius * .62f;
                pair.label.style.width = radius * 2; pair.label.style.height = radius * 1.24f;
                pair.label.style.fontSize = Mathf.Clamp(radius * .78f, 8, 26);
            }
            MarkDirtyRepaint();
            ViewportChanged?.Invoke();
        }
        private CellDefinition? Hit(Vector2 pointer)
        {
            if (radius <= 1) return null;
            // Hit testing is presentation geometry only; the host rechecks legal targets.
            return catalog.Cells.FirstOrDefault(cell =>
            {
                var delta = pointer - Center(cell.Position);
                float x = Mathf.Abs(delta.x), y = Mathf.Abs(delta.y);
                return x <= Mathf.Sqrt(3) * radius * .5f && y <= radius && Mathf.Sqrt(3) * y + x <= Mathf.Sqrt(3) * radius;
            });
        }
        private static Color ColorOf(string hex) { ColorUtility.TryParseHtmlString(hex, out var color); return color; }
        private static Color RegionColor(CellDefinition cell)
        {
            if (cell.Obstacle) return ColorOf("#101c25");
            switch (cell.Region)
            {
                case "redFountain": return ColorOf("#513640");
                case "redNear": return ColorOf("#423940");
                case "blueFountain": return ColorOf("#25445e");
                case "blueNear": return ColorOf("#293e4b");
                case "topGrass": case "bottomGrass": return ColorOf("#284940");
                default: return ColorOf(cell.Lane ? "#495247" : "#364741");
            }
        }
        private static void Polygon(Painter2D painter, Vector2 center, float size)
        {
            painter.BeginPath();
            for (int i = 0; i < 6; i++)
            {
                float a = (60 * i - 30) * Mathf.Deg2Rad;
                var p = center + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * size;
                if (i == 0) painter.MoveTo(p); else painter.LineTo(p);
            }
            painter.ClosePath();
        }
        private void Draw(MeshGenerationContext context)
        {
            if (radius <= 1) return;
            var painter = context.painter2D;
            foreach (var cell in catalog.Cells)
            {
                var center = Center(cell.Position);
                Polygon(painter, center, radius - .8f);
                painter.fillColor = RegionColor(cell); painter.Fill();
                painter.strokeColor = ColorOf("#60706a"); painter.lineWidth = .7f; painter.Stroke();
                if (cell.Obstacle)
                {
                    painter.BeginPath(); painter.MoveTo(center + new Vector2(-radius * .27f, radius * .15f));
                    painter.LineTo(center + new Vector2(0, -radius * .35f));
                    painter.LineTo(center + new Vector2(radius * .32f, radius * .15f));
                    painter.strokeColor = ColorOf("#455961"); painter.lineWidth = 1.3f; painter.Stroke();
                }
                if (cell.Spawn.EndsWith("Spawn", StringComparison.Ordinal) && !view.Units.Any(u => u.Position == cell.Position))
                {
                    painter.BeginPath();
                    painter.Arc(center, Mathf.Max(2, radius * .19f), 0, 360);
                    painter.strokeColor = ColorOf(cell.Spawn.StartsWith("blue", StringComparison.Ordinal) ? "#83b8e6" : "#d49a9a");
                    painter.lineWidth = cell.Spawn.Contains("Hero") ? 2 : 1; painter.Stroke();
                }
                if (effectArea.Contains(cell.Position))
                {
                    Polygon(painter,center,radius-2.5f);
                    painter.strokeColor=ColorOf("#ba9ceb"); painter.lineWidth=2; painter.Stroke();
                }
                if (legal.Contains(cell.Position))
                {
                    Polygon(painter, center, radius - 1.2f);
                    painter.fillColor = new Color(.30f, .80f, .69f, .22f); painter.Fill();
                    painter.strokeColor = ColorOf("#65d6bb"); painter.lineWidth = 1.8f; painter.Stroke();
                }
                if (selected == cell.Position)
                {
                    Polygon(painter, center, radius - .5f);
                    painter.strokeColor = ColorOf("#ffd391"); painter.lineWidth = 3; painter.Stroke();
                }
            }
            foreach (var unit in view.Units)
            {
                var center = Center(unit.Position);
                painter.BeginPath(); painter.Arc(center, radius * (unit.Seat.HasValue ? .65f : .48f), 0, 360);
                painter.fillColor = ColorOf(unit.Team == Team.Blue ? "#548ecd" : "#b46970"); painter.Fill();
                painter.strokeColor = unit.Seat == view.ActiveSeat && unit.Seat.HasValue ? ColorOf("#ffdb9a") : ColorOf("#d5e0df");
                painter.lineWidth = unit.Seat.HasValue ? 2.5f : 1; painter.Stroke();
            }
        }
    }
}

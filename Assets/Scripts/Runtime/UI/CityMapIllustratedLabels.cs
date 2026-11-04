using System;
using System.Collections.Generic;
using UnityEngine;

namespace BarPromenade
{
    public sealed partial class CityMapView
    {
        private static readonly Color CityIllustratedLabelPaper = new Color(.075f, .10f, .10f, 1f);
        private static readonly Color VillageIllustratedLabelPaper = new Color(.94f, .90f, .78f, 1f);
        private static readonly Color CityIllustratedLabelInk = new Color(.97f, .96f, .88f, 1f);

        private struct IllustratedMapLabel
        {
            public int PointIndex, Priority;
            public GUIContent Content;
            public Vector2 Anchor;
            public Rect Bounds, RowBounds;
            public bool Placed;
        }

        // Retained semantic catalog shared by the right-hand list and its one
        // selected callout. It is independent of the illustrated/schematic backing.
        private IllustratedMapLabel[] illustratedMapLabels = new IllustratedMapLabel[64];
        private int[] illustratedMapLabelOrder = new int[64];
        private IReadOnlyList<CityMapPointDescriptor> illustratedLabelCatalog;
        private int illustratedLabelCatalogPointCount = -1;
        private int illustratedLabelCatalogCount, illustratedLabelPlacedCount;
        private int selectedPlaceSlot = -1, firstVisiblePlaceRow;
        private bool focusSelectedPlace;
        private Rect illustratedLabelClipRect;
        private GUIStyle cityIllustratedLabelStyle, villageIllustratedLabelStyle;

        internal Rect FullscreenPanelRect { get; private set; }
        internal Rect PlayerMarkerRect { get; private set; }
        internal Rect PointOfInterestListRect { get; private set; }
        internal int PointOfInterestRowCount => illustratedLabelCatalogCount;
        internal int IllustratedMapLabelCount => illustratedLabelPlacedCount;
        internal int IllustratedMapLabelCandidateCount => illustratedLabelPlacedCount;
        internal int UnplacedIllustratedMapLabelCount => 0;
        internal Rect IllustratedMapLabelClipRect => illustratedLabelClipRect;

        internal bool TryGetPointOfInterestRow(int index, out int pointIndex, out Rect bounds)
        {
            if (index >= 0 && index < illustratedLabelCatalogCount)
            {
                IllustratedMapLabel label = illustratedMapLabels[illustratedMapLabelOrder[index]];
                pointIndex = label.PointIndex;
                bounds = label.RowBounds;
                return bounds.width > 0f;
            }
            pointIndex = -1; bounds = default;
            return false;
        }

        internal bool TryGetIllustratedMapLabelLayout(int index, out int pointIndex, out Rect bounds, out Vector2 anchor)
        {
            if (index == 0 && illustratedLabelPlacedCount == 1)
            {
                IllustratedMapLabel label = illustratedMapLabels[selectedPlaceSlot];
                pointIndex = label.PointIndex; bounds = label.Bounds; anchor = label.Anchor;
                return true;
            }
            pointIndex = -1; bounds = default; anchor = default;
            return false;
        }

        internal bool TryGetIllustratedMapLabelText(int index, out string text)
        {
            text = index == 0 && illustratedLabelPlacedCount == 1
                ? illustratedMapLabels[selectedPlaceSlot].Content.text : string.Empty;
            return text.Length > 0;
        }

        private void ResetIllustratedMapLabels()
        {
            illustratedLabelCatalog = null;
            illustratedLabelCatalogPointCount = -1;
            illustratedLabelCatalogCount = illustratedLabelPlacedCount = 0;
            ClearSelectedPlace();
        }

        private void ClearSelectedPlace()
        {
            selectedPlaceSlot = -1;
            firstVisiblePlaceRow = illustratedLabelPlacedCount = 0;
            focusSelectedPlace = false;
        }

        private void EnsurePlaceCatalog()
        {
            EnsureIllustratedMapLabelStyles();
            IReadOnlyList<CityMapPointDescriptor> points = controller.ActiveMapPoints;
            if (ReferenceEquals(illustratedLabelCatalog, points) && illustratedLabelCatalogPointCount == points.Count) return;
            int required = 0;
            for (int index = 0; index < points.Count; index++)
                if (ShouldLabelIllustratedMapPoint(points[index])) required++;
            if (required > illustratedMapLabels.Length)
            {
                int capacity = Mathf.NextPowerOfTwo(required);
                Array.Resize(ref illustratedMapLabels, capacity);
                Array.Resize(ref illustratedMapLabelOrder, capacity);
            }
            ClearSelectedPlace();
            illustratedLabelCatalogCount = 0;
            for (int index = 0; index < points.Count; index++)
            {
                CityMapPointDescriptor point = points[index];
                if (!ShouldLabelIllustratedMapPoint(point)) continue;
                int slot = illustratedLabelCatalogCount++;
                ref IllustratedMapLabel label = ref illustratedMapLabels[slot];
                label.PointIndex = index;
                label.Priority = IllustratedMapLabelPriority(point.Kind);
                label.Content ??= new GUIContent();
                label.Content.text = TryGetIllustratedOpenAreaRegion(point, out CityMapAreaRegion region)
                    ? LocalizationService.Get(region.LocalizationKey) : point.Label;
                label.Placed = false;
                label.RowBounds = Rect.zero;
                int order = slot;
                while (order > 0 && illustratedMapLabels[illustratedMapLabelOrder[order - 1]].Priority < label.Priority)
                { illustratedMapLabelOrder[order] = illustratedMapLabelOrder[order - 1]; order--; }
                illustratedMapLabelOrder[order] = slot;
            }
            illustratedLabelCatalog = points;
            illustratedLabelCatalogPointCount = points.Count;
        }

        private void EnsureIllustratedMapLabelStyles()
        {
            if (cityIllustratedLabelStyle != null) return;
            cityIllustratedLabelStyle = RetroUiTheme.CreateLabelStyle(10,
                TextAnchor.MiddleLeft, CityIllustratedLabelInk, true, true);
            villageIllustratedLabelStyle = RetroUiTheme.CreateLabelStyle(10,
                TextAnchor.MiddleLeft, AlpineVillageBrochureInk, true, true);
        }

        private bool ShouldLabelIllustratedMapPoint(CityMapPointDescriptor point) =>
            !string.IsNullOrWhiteSpace(point.Label) && point.Kind != CityMapPointKind.Player &&
            point.Kind != CityMapPointKind.GroundSquare &&
            !(point.Kind == CityMapPointKind.MapObject && point.StableId.StartsWith("city:lot:", StringComparison.Ordinal)) &&
            !(TryGetIllustratedOpenAreaRegion(point, out CityMapAreaRegion region) &&
                region.Feature == CityAreaFeatureKind.Yard && region.LocalizationKey == "map.district.yard");

        private bool TryGetIllustratedOpenAreaRegion(CityMapPointDescriptor point, out CityMapAreaRegion region)
        {
            const string prefix = "city:open-area:";
            if (point.Kind == CityMapPointKind.OpenArea && point.Area == GameAreaId.City &&
                point.StableId.StartsWith(prefix, StringComparison.Ordinal))
            {
                IReadOnlyList<CityMapAreaTarget> areas = controller.MapAreaTargets;
                for (int index = 0; index < areas.Count; index++)
                {
                    CityMapAreaRegion candidate = areas[index].Region;
                    if (point.StableId.Length != prefix.Length + candidate.AreaId.Length ||
                        string.CompareOrdinal(point.StableId, prefix.Length, candidate.AreaId, 0, candidate.AreaId.Length) != 0)
                        continue;
                    region = candidate;
                    return true;
                }
            }
            region = null;
            return false;
        }

        private static int IllustratedMapLabelPriority(CityMapPointKind kind)
        {
            switch (kind)
            {
                case CityMapPointKind.MothersHouse:
                case CityMapPointKind.Home: return 500;
                case CityMapPointKind.Supermarket: return 450;
                case CityMapPointKind.PointOfInterest:
                case CityMapPointKind.Village:
                case CityMapPointKind.Cableway: return 400;
                case CityMapPointKind.Tunnel:
                case CityMapPointKind.BoatStation: return 375;
                case CityMapPointKind.Bar: return 350;
                case CityMapPointKind.OpenArea: return 300;
                case CityMapPointKind.BusStop: return 100;
                default: return 200;
            }
        }

        private void PrepareIllustratedMapLabels(MapProjection projection)
        {
            illustratedLabelPlacedCount = 0;
            PlayerMarkerRect = controller.ShouldDrawPlayerOnSelectedArea
                ? CreateCenteredRect(projection.WorldToScreen(controller.PlayerWorldPosition), 34f, 34f) : Rect.zero;
            Rect clip = Intersect(projection.ScreenRect, new Rect(Vector2.zero, new Vector2(
                Mathf.Max(0f, mapLineClipRect.width - (mapViewport.CanScrollVertical ? 10f : 0f)),
                Mathf.Max(0f, mapLineClipRect.height - (mapViewport.CanScrollHorizontal ? 10f : 0f)))));
            illustratedLabelClipRect = new Rect(clip.x + 4f, clip.y + 4f,
                Mathf.Max(0f, clip.width - 8f), Mathf.Max(0f, clip.height - 8f));
            if (selectedPlaceSlot < 0) return;
            ref IllustratedMapLabel label = ref illustratedMapLabels[selectedPlaceSlot];
            label.Placed = false;
            label.Anchor = projection.WorldToScreen(controller.ResolveMapPointWorldPosition(
                illustratedLabelCatalog[label.PointIndex]));
            if (!illustratedLabelClipRect.Contains(label.Anchor)) return;
            GUIStyle style = IsUsingIllustratedVillageMap ? villageIllustratedLabelStyle : cityIllustratedLabelStyle;
            float width = Mathf.Min(illustratedLabelClipRect.width,
                Mathf.Clamp(style.CalcSize(label.Content).x + 16f, 64f, 174f));
            Vector2 size = new Vector2(width, style.CalcHeight(label.Content, width - 16f) + 12f);
            if (size.y > illustratedLabelClipRect.height) return;
            Rect legend = controller.SelectedArea == GameAreaId.City && !controller.BusOverlay.IsEmpty
                ? CreateBusLegendRect(mapLineClipRect, controller.BusOverlay.Stops.Count > 0) : Rect.zero;
            // Only one label: bounded candidate search keeps it close to its
            // square without covering that square, the hero or the bus legend.
            for (int ring = 0; ring < 8; ring++)
                for (int direction = 0; direction < 4; direction++)
                {
                    float gap = 20f + ring * 18f;
                    Vector2 offset = direction == 0 ? new Vector2(gap, -size.y * .5f) :
                        direction == 1 ? new Vector2(-gap - size.x, -size.y * .5f) :
                        direction == 2 ? new Vector2(-size.x * .5f, -gap - size.y) :
                        new Vector2(-size.x * .5f, gap);
                    Rect candidate = new Rect(label.Anchor + offset, size);
                    candidate.x = Mathf.Clamp(candidate.x, illustratedLabelClipRect.xMin, illustratedLabelClipRect.xMax - size.x);
                    candidate.y = Mathf.Clamp(candidate.y, illustratedLabelClipRect.yMin, illustratedLabelClipRect.yMax - size.y);
                    if (candidate.Overlaps(legend) || candidate.Overlaps(PlayerMarkerRect) ||
                        candidate.Overlaps(CreateCenteredRect(label.Anchor, 14f, 14f))) continue;
                    label.Bounds = candidate;
                    label.Placed = true;
                    illustratedLabelPlacedCount = 1;
                    return;
                }
        }

        private bool IsPointerOverIllustratedMapLabel(Vector2 localPointer) =>
            illustratedLabelPlacedCount == 1 && illustratedMapLabels[selectedPlaceSlot].Bounds.Contains(localPointer);

        private void DrawIllustratedMapLabels()
        {
            if (illustratedLabelPlacedCount != 1) return;
            IllustratedMapLabel label = illustratedMapLabels[selectedPlaceSlot];
            Color ink = IsUsingIllustratedVillageMap ? AlpineVillageBrochureInk : CityIllustratedLabelInk;
            Color paper = IsUsingIllustratedVillageMap ? VillageIllustratedLabelPaper : CityIllustratedLabelPaper;
            GUIStyle style = IsUsingIllustratedVillageMap ? villageIllustratedLabelStyle : cityIllustratedLabelStyle;
            Vector2 endpoint = new Vector2(Mathf.Clamp(label.Anchor.x, label.Bounds.xMin, label.Bounds.xMax),
                Mathf.Clamp(label.Anchor.y, label.Bounds.yMin, label.Bounds.yMax));
            DrawLine(label.Anchor, endpoint, 3f, paper);
            DrawLine(label.Anchor, endpoint, 1.25f, ink);
            Rect marker = CreateCenteredRect(label.Anchor, 8f, 8f);
            DrawSolidRect(Expand(marker, 2f), paper);
            DrawSolidRect(marker, ink);
            RetroUiTheme.StrokeRect(marker, 1f, paper);
            DrawSolidRect(label.Bounds, paper);
            RetroUiTheme.StrokeRect(label.Bounds, 1f, ink);
            GUI.Label(new Rect(label.Bounds.x + 8f, label.Bounds.y + 6f,
                label.Bounds.width - 16f, label.Bounds.height - 12f), label.Content, style);
        }
    }
}

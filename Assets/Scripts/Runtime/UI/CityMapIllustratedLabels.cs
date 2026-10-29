using System;
using System.Collections.Generic;
using UnityEngine;

namespace BarPromenade
{
    public sealed partial class CityMapView
    {
        private const float IllustratedLabelFontSize = 8f;
        private const float IllustratedLabelMaximumWidth = 108f;
        private const float IllustratedLabelGap = 2f;
        private const float IllustratedLabelGridStep = 4f;
        private static readonly Color CityIllustratedLabelPaper = new Color(.075f, .10f, .10f, .97f);
        private static readonly Color VillageIllustratedLabelPaper = new Color(.94f, .90f, .78f, .98f);
        private static readonly Color CityIllustratedLabelInk = new Color(.97f, .96f, .88f, 1f);

        private struct IllustratedMapLabel
        {
            public int PointIndex, Priority;
            public GUIContent Content;
            public Vector2 Size, Anchor;
            public Rect Bounds;
            public bool Candidate, Placed;
        }

        private IllustratedMapLabel[] illustratedMapLabels = new IllustratedMapLabel[64];
        private int[] illustratedMapLabelOrder = new int[64];
        private IReadOnlyList<CityMapPointDescriptor> illustratedLabelCatalog;
        private int illustratedLabelCatalogPointCount = -1;
        private int illustratedLabelCatalogCount, illustratedLabelCandidateCount, illustratedLabelPlacedCount;
        private Rect illustratedLabelProjectionRect, illustratedLabelClipRect, illustratedLabelLegendRect;
        private bool illustratedLabelLayoutValid, illustratedLabelOverflowReported;
        private GUIStyle cityIllustratedLabelStyle, villageIllustratedLabelStyle;

        internal int IllustratedMapLabelCount => illustratedLabelPlacedCount;
        internal int IllustratedMapLabelCandidateCount => illustratedLabelCandidateCount;
        internal int UnplacedIllustratedMapLabelCount => illustratedLabelCandidateCount - illustratedLabelPlacedCount;
        internal Rect IllustratedMapLabelClipRect => illustratedLabelClipRect;

        internal bool TryGetIllustratedMapLabelLayout(int index, out int pointIndex, out Rect bounds, out Vector2 anchor)
        {
            for (int slot = 0; slot < illustratedLabelCatalogCount; slot++)
            {
                IllustratedMapLabel label = illustratedMapLabels[slot];
                if (!label.Placed || index-- != 0) continue;
                pointIndex = label.PointIndex; bounds = label.Bounds; anchor = label.Anchor;
                return true;
            }
            pointIndex = -1; bounds = default; anchor = default;
            return false;
        }

        internal bool TryGetIllustratedMapLabelText(int index, out string text)
        {
            for (int slot = 0; slot < illustratedLabelCatalogCount; slot++)
            {
                IllustratedMapLabel label = illustratedMapLabels[slot];
                if (!label.Placed || index-- != 0) continue;
                text = label.Content.text;
                return true;
            }
            text = string.Empty;
            return false;
        }

        private void ResetIllustratedMapLabels()
        {
            illustratedLabelCatalog = null;
            illustratedLabelCatalogPointCount = -1;
            illustratedLabelCatalogCount = illustratedLabelCandidateCount = illustratedLabelPlacedCount = 0;
            illustratedLabelLayoutValid = illustratedLabelOverflowReported = false;
        }

        private bool HasIllustratedMapLabels => IsUsingIllustratedMap || IsUsingIllustratedVillageMap;

        private void PrepareIllustratedMapLabels(MapProjection projection)
        {
            if (!HasIllustratedMapLabels)
            {
                illustratedLabelCandidateCount = illustratedLabelPlacedCount = 0;
                return;
            }
            EnsureIllustratedMapLabelStyles();
            IReadOnlyList<CityMapPointDescriptor> points = controller.ActiveMapPoints;
            if (!ReferenceEquals(illustratedLabelCatalog, points) || illustratedLabelCatalogPointCount != points.Count)
                RebuildIllustratedMapLabelCatalog(points);

            // Scroll indicators paint after the group; keep name plaques
            // inside the ground that remains visible beside their tracks.
            Rect unobscuredViewport = new Rect(mapLineClipRect.position, new Vector2(
                Mathf.Max(0f, mapLineClipRect.width - (mapViewport.CanScrollVertical ? 10f : 0f)),
                Mathf.Max(0f, mapLineClipRect.height - (mapViewport.CanScrollHorizontal ? 10f : 0f))));
            Rect clip = Intersect(projection.ScreenRect, unobscuredViewport);
            clip = new Rect(clip.x + 2f, clip.y + 2f, Mathf.Max(0f, clip.width - 4f), Mathf.Max(0f, clip.height - 4f));
            Rect legend = IsUsingIllustratedMap && !controller.BusOverlay.IsEmpty
                ? CreateBusLegendRect(mapLineClipRect, controller.BusOverlay.Stops.Count > 0) : Rect.zero;
            if (!illustratedLabelLayoutValid || illustratedLabelProjectionRect != projection.ScreenRect ||
                illustratedLabelClipRect != clip || illustratedLabelLegendRect != legend)
            {
                illustratedLabelProjectionRect = projection.ScreenRect;
                illustratedLabelClipRect = clip;
                illustratedLabelLegendRect = legend;
                RebuildIllustratedMapLabelLayout(projection, points);
                illustratedLabelLayoutValid = true;
            }

            // Claim only the plate's left click before broad map/lot controls.
            // Existing right/middle drags remain owned by the viewport.
            for (int slot = 0; slot < illustratedLabelCatalogCount; slot++)
            {
                IllustratedMapLabel label = illustratedMapLabels[slot];
                if (!label.Placed || !RetroUiInput.Button(label.Bounds, GUIContent.none, GUIStyle.none)) continue;
                if (!controller.MapPointInspectionEnabled) controller.QueueToggleMapPointInspection();
                controller.QueueSelectMapPoint(label.PointIndex, false);
            }
        }

        private void EnsureIllustratedMapLabelStyles()
        {
            if (cityIllustratedLabelStyle != null) return;
            cityIllustratedLabelStyle = RetroUiTheme.CreateLabelStyle((int)IllustratedLabelFontSize,
                TextAnchor.MiddleLeft, CityIllustratedLabelInk, true, true);
            villageIllustratedLabelStyle = RetroUiTheme.CreateLabelStyle((int)IllustratedLabelFontSize,
                TextAnchor.MiddleLeft, AlpineVillageBrochureInk, true, true);
        }

        private void RebuildIllustratedMapLabelCatalog(IReadOnlyList<CityMapPointDescriptor> points)
        {
            int required = 0;
            for (int index = 0; index < points.Count; index++)
                if (ShouldLabelIllustratedMapPoint(points[index])) required++;
            if (required > illustratedMapLabels.Length)
            {
                int capacity = Mathf.NextPowerOfTwo(required);
                Array.Resize(ref illustratedMapLabels, capacity);
                Array.Resize(ref illustratedMapLabelOrder, capacity);
            }
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
                // An area's coordinate-bearing debug name remains available
                // in its inspector. The paper prints only its authored name.
                label.Content.text = TryGetIllustratedOpenAreaRegion(point, out CityMapAreaRegion region)
                    ? LocalizationService.Get(region.LocalizationKey) : point.Label;
                Vector2 natural = cityIllustratedLabelStyle.CalcSize(label.Content);
                float width = Mathf.Clamp(natural.x + 6f, 24f, IllustratedLabelMaximumWidth);
                label.Size = new Vector2(width, Mathf.Ceil(cityIllustratedLabelStyle.CalcHeight(label.Content, width - 6f)) + 4f);
                // Stable priority insertion, without a comparison delegate or a
                // temporary list. Every authored named point retains a slot.
                int order = slot;
                while (order > 0 && illustratedMapLabels[illustratedMapLabelOrder[order - 1]].Priority < label.Priority)
                { illustratedMapLabelOrder[order] = illustratedMapLabelOrder[order - 1]; order--; }
                illustratedMapLabelOrder[order] = slot;
            }
            illustratedLabelCatalog = points;
            illustratedLabelCatalogPointCount = points.Count;
            illustratedLabelLayoutValid = false;
        }

        private bool ShouldLabelIllustratedMapPoint(CityMapPointDescriptor point) =>
            !string.IsNullOrWhiteSpace(point.Label) && point.Kind != CityMapPointKind.Player &&
            point.Kind != CityMapPointKind.GroundSquare && point.Kind != CityMapPointKind.BusStop &&
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

        private void RebuildIllustratedMapLabelLayout(MapProjection projection, IReadOnlyList<CityMapPointDescriptor> points)
        {
            illustratedLabelCandidateCount = illustratedLabelPlacedCount = 0;
            for (int slot = 0; slot < illustratedLabelCatalogCount; slot++)
            {
                ref IllustratedMapLabel label = ref illustratedMapLabels[slot];
                label.Anchor = projection.WorldToScreen(controller.ResolveMapPointWorldPosition(points[label.PointIndex]));
                label.Candidate = illustratedLabelClipRect.Contains(label.Anchor);
                label.Placed = false;
                if (label.Candidate) illustratedLabelCandidateCount++;
            }
            for (int order = 0; order < illustratedLabelCatalogCount; order++)
            {
                int slot = illustratedMapLabelOrder[order];
                ref IllustratedMapLabel label = ref illustratedMapLabels[slot];
                if (!label.Candidate) continue;
                if (TryPlaceIllustratedMapLabel(slot, true) || TryPlaceIllustratedMapLabel(slot, false))
                    illustratedLabelPlacedCount++;
            }
            if (illustratedLabelPlacedCount < illustratedLabelCandidateCount)
                RepackIllustratedMapLabelRows();
            if (UnplacedIllustratedMapLabelCount > 0 && !illustratedLabelOverflowReported)
            {
                illustratedLabelOverflowReported = true;
                GameLog.Warning("map", "illustrated_label_layout_has_no_free_space",
                    GameLog.Field("unplaced", UnplacedIllustratedMapLabelCount));
            }
        }

        private bool TryPlaceIllustratedMapLabel(int slot, bool nearby)
        {
            ref IllustratedMapLabel label = ref illustratedMapLabels[slot];
            if (label.Size.x > illustratedLabelClipRect.width || label.Size.y > illustratedLabelClipRect.height) return false;
            if (nearby)
            {
                for (int ring = 0; ring < 8; ring++)
                    for (int direction = 0; direction < 8; direction++)
                    {
                        float radius = 10f + ring * 18f;
                        Vector2 offset;
                        switch (direction)
                        {
                            case 0: offset = new Vector2(radius, -label.Size.y * .5f); break;
                            case 1: offset = new Vector2(-radius - label.Size.x, -label.Size.y * .5f); break;
                            case 2: offset = new Vector2(-label.Size.x * .5f, -radius - label.Size.y); break;
                            case 3: offset = new Vector2(-label.Size.x * .5f, radius); break;
                            case 4: offset = new Vector2(radius, -radius - label.Size.y); break;
                            case 5: offset = new Vector2(-radius - label.Size.x, radius); break;
                            case 6: offset = new Vector2(radius, radius); break;
                            default: offset = new Vector2(-radius - label.Size.x, -radius - label.Size.y); break;
                        }
                        Rect candidate = ClampIllustratedMapLabel(new Rect(label.Anchor + offset, label.Size));
                        if (AcceptIllustratedMapLabel(slot, candidate, true)) return true;
                    }
                return false;
            }
            // A finite scan offers free rows/columns when local clusters fill.
            for (float y = illustratedLabelClipRect.yMin; y <= illustratedLabelClipRect.yMax - label.Size.y; y += IllustratedLabelGridStep)
                for (float x = illustratedLabelClipRect.xMin; x <= illustratedLabelClipRect.xMax - label.Size.x; x += IllustratedLabelGridStep)
                    if (AcceptIllustratedMapLabel(slot, new Rect(new Vector2(x, y), label.Size), true)) return true;
            return false;
        }

        private Rect ClampIllustratedMapLabel(Rect rectangle) => new Rect(
            Mathf.Clamp(rectangle.x, illustratedLabelClipRect.xMin, illustratedLabelClipRect.xMax - rectangle.width),
            Mathf.Clamp(rectangle.y, illustratedLabelClipRect.yMin, illustratedLabelClipRect.yMax - rectangle.height),
            rectangle.width, rectangle.height);

        private bool AcceptIllustratedMapLabel(int slot, Rect candidate, bool preserveMarkers)
        {
            Rect clearance = Expand(candidate, IllustratedLabelGap * .5f);
            if (clearance.Overlaps(illustratedLabelLegendRect)) return false;
            for (int other = 0; other < illustratedLabelCatalogCount; other++)
            {
                if (other != slot && illustratedMapLabels[other].Placed && clearance.Overlaps(illustratedMapLabels[other].Bounds)) return false;
                if (preserveMarkers && illustratedMapLabels[other].Candidate &&
                    clearance.Overlaps(CreateCenteredRect(illustratedMapLabels[other].Anchor, 10f, 10f))) return false;
            }
            illustratedMapLabels[slot].Bounds = candidate;
            illustratedMapLabels[slot].Placed = true;
            return true;
        }

        private void RepackIllustratedMapLabelRows()
        {
            for (int slot = 0; slot < illustratedLabelCatalogCount; slot++) illustratedMapLabels[slot].Placed = false;
            illustratedLabelPlacedCount = 0;
            float x = illustratedLabelClipRect.xMin, y = illustratedLabelClipRect.yMin, rowHeight = 0f;
            for (int order = 0; order < illustratedLabelCatalogCount; order++)
            {
                int slot = illustratedMapLabelOrder[order];
                ref IllustratedMapLabel label = ref illustratedMapLabels[slot];
                if (!label.Candidate) continue;
                if (x + label.Size.x > illustratedLabelClipRect.xMax)
                { x = illustratedLabelClipRect.xMin; y += rowHeight + IllustratedLabelGap; rowHeight = 0f; }
                Rect candidate = new Rect(new Vector2(x, y), label.Size);
                if (candidate.yMax > illustratedLabelClipRect.yMax || candidate.xMax > illustratedLabelClipRect.xMax) continue;
                if (candidate.Overlaps(illustratedLabelLegendRect))
                { y = illustratedLabelLegendRect.yMax + IllustratedLabelGap; candidate.y = y; }
                if (candidate.yMax > illustratedLabelClipRect.yMax) continue;
                if (!AcceptIllustratedMapLabel(slot, candidate, false)) continue;
                x = candidate.xMax + IllustratedLabelGap;
                rowHeight = Mathf.Max(rowHeight, label.Size.y);
                illustratedLabelPlacedCount++;
            }
        }

        private bool IsPointerOverIllustratedMapLabel(Vector2 localPointer)
        {
            if (!HasIllustratedMapLabels) return false;
            for (int slot = 0; slot < illustratedLabelCatalogCount; slot++)
                if (illustratedMapLabels[slot].Placed && illustratedMapLabels[slot].Bounds.Contains(localPointer)) return true;
            return false;
        }

        private void DrawIllustratedMapLabels()
        {
            if (!HasIllustratedMapLabels) return;
            Color ink = IsUsingIllustratedVillageMap ? AlpineVillageBrochureInk : CityIllustratedLabelInk;
            Color paper = IsUsingIllustratedVillageMap ? VillageIllustratedLabelPaper : CityIllustratedLabelPaper;
            GUIStyle style = IsUsingIllustratedVillageMap ? villageIllustratedLabelStyle : cityIllustratedLabelStyle;
            for (int slot = 0; slot < illustratedLabelCatalogCount; slot++)
            {
                IllustratedMapLabel label = illustratedMapLabels[slot];
                if (!label.Placed) continue;
                Vector2 endpoint = new Vector2(Mathf.Clamp(label.Anchor.x, label.Bounds.xMin, label.Bounds.xMax),
                    Mathf.Clamp(label.Anchor.y, label.Bounds.yMin, label.Bounds.yMax));
                DrawLine(label.Anchor, endpoint, 1f, ink);
                DrawOpenOctagonOutline(label.Anchor, 2f, 1f, ink);
            }
            for (int slot = 0; slot < illustratedLabelCatalogCount; slot++)
            {
                IllustratedMapLabel label = illustratedMapLabels[slot];
                if (!label.Placed) continue;
                DrawSolidRect(label.Bounds, paper);
                RetroUiTheme.StrokeRect(label.Bounds, 1f, ink);
                GUI.Label(new Rect(label.Bounds.x + 3f, label.Bounds.y + 2f,
                    label.Bounds.width - 6f, label.Bounds.height - 4f), label.Content, style);
                RegisterHoverTarget(label.Bounds, label.Bounds.center, label.Content.text,
                    PlayerHoverPriority + 10, label.PointIndex);
            }
        }
    }
}

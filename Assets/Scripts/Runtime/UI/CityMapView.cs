using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

namespace BarPromenade
{
    [DisallowMultipleComponent]
    public sealed class CityMapView : MonoBehaviour
    {
        private const int PointOfInterestHoverPriority = 10;
        internal const int BusStopHoverPriority = 15;
        private const int BarHoverPriority = 20;
        private const int LandmarkHoverPriority = 30;
        private const float MinimumMapCellPixels = 22f;
        private const float KeyboardPanSpeed = 116f;
        private const float MouseWheelStep = 18f;

        private readonly struct MapProjection
        {
            public MapProjection(
                Rect screenRect,
                float minimumX,
                float maximumX,
                float minimumZ,
                float maximumZ)
            {
                ScreenRect = screenRect;
                MinimumX = minimumX;
                MaximumX = maximumX;
                MinimumZ = minimumZ;
                MaximumZ = maximumZ;
            }

            public Rect ScreenRect { get; }
            public float MinimumX { get; }
            public float MaximumX { get; }
            public float MinimumZ { get; }
            public float MaximumZ { get; }

            public Vector2 WorldToScreen(Vector3 worldPosition)
            {
                float normalizedX = Mathf.InverseLerp(
                    MinimumX,
                    MaximumX,
                    worldPosition.x);
                float normalizedZ = Mathf.InverseLerp(
                    MinimumZ,
                    MaximumZ,
                    worldPosition.z);
                return new Vector2(
                    Mathf.Lerp(
                        ScreenRect.xMin,
                        ScreenRect.xMax,
                        normalizedX),
                    Mathf.Lerp(
                        ScreenRect.yMax,
                        ScreenRect.yMin,
                        normalizedZ));
            }
        }

        internal readonly struct MapHoverTarget
        {
            internal MapHoverTarget(
                Rect hitbox,
                Vector2 anchor,
                string label,
                int priority)
            {
                Hitbox = hitbox;
                Anchor = anchor;
                Label = label ?? string.Empty;
                Priority = priority;
            }

            public Rect Hitbox { get; }
            public Vector2 Anchor { get; }
            public string Label { get; }
            public int Priority { get; }
        }

        private static readonly Color Backdrop =
            RetroUiTheme.WithAlpha(RetroUiTheme.Backdrop, 0.96f);
        private static readonly Color MapGround =
            RetroUiTheme.MapGround;
        private static readonly Color MapVoid =
            new Color32(18, 22, 27, 255);
        private static readonly Color Building =
            RetroUiTheme.MapBuilding;
        private static readonly Color OldTownBuilding =
            new Color32(91, 76, 68, 255);
        private static readonly Color ResidentialBuilding =
            new Color32(64, 83, 78, 255);
        private static readonly Color IndustrialBuilding =
            new Color32(70, 72, 82, 255);
        private static readonly Color NightlifeBuilding =
            new Color32(80, 63, 87, 255);
        private static readonly Color ParkLand =
            new Color32(54, 83, 60, 255);
        private static readonly Color WaterfrontLand =
            new Color32(147, 124, 77, 255);
        private static readonly Color LakeLand =
            new Color32(50, 91, 101, 255);
        private static readonly Color CemeteryLand =
            new Color32(66, 77, 65, 255);
        private static readonly Color YardLand =
            new Color32(94, 84, 63, 255);
        private static readonly Color WaterLand =
            new Color32(35, 91, 119, 255);
        private static readonly Color PublicPlaceLand =
            new Color32(123, 112, 91, 255);
        private static readonly Color BarBuilding =
            RetroUiTheme.MapBar;
        private static readonly Color HomeBuilding =
            new Color32(70, 116, 124, 255);
        private static readonly Color Road =
            RetroUiTheme.MapRoad;
        private static readonly Color ParkPath =
            new Color32(159, 150, 105, 255);
        private static readonly Color DistrictLabelBackdrop =
            RetroUiTheme.WithAlpha(RetroUiTheme.MapGround, 0.78f);
        private static readonly Color Route =
            RetroUiTheme.Accent;
        private static readonly Color BusRoute =
            new Color32(91, 143, 209, 255);
        private static readonly Color BusStop =
            RetroUiTheme.AccentPale;
        private static readonly Color UnselectedBar =
            RetroUiTheme.MapBar;
        private static readonly Color Player =
            RetroUiTheme.Cyan;
        private static readonly Color PlayerHome =
            RetroUiTheme.AccentPale;
        private static readonly Color Supermarket =
            new Color32(224, 194, 91, 255);
        private static readonly Color TooltipBackdrop =
            new Color32(19, 15, 25, 250);

        internal static Color BusRouteColor => BusRoute;

        private readonly List<MapHoverTarget> hoverTargets =
            new List<MapHoverTarget>(160);
        private readonly CityMapViewport mapViewport =
            new CityMapViewport();

        private CityMapController controller;
        private bool wasOpen;
        private bool isPointerPanning;
        private int pointerPanButton = -1;
        private Vector2 previousPanPointer;
        private Vector2 hoverCoordinateOffset;
        private Rect hoverClipRect;
        private bool isMapLineContextActive;
        private Rect mapLineClipRect;
        private Vector2 mapLineGroupOffset;
        private GUIStyle titleStyle;
        private GUIStyle subtitleStyle;
        private GUIStyle centeredStyle;
        private GUIStyle routeItemStyle;
        private GUIStyle markerButtonStyle;
        private GUIStyle routeBadgeStyle;
        private GUIStyle hintStyle;
        private GUIStyle smallButtonStyle;
        private GUIStyle districtLabelStyle;
        private GUIStyle pointOfInterestTitleStyle;
        private GUIStyle pointOfInterestItemStyle;
        private GUIStyle tooltipStyle;

        public void Initialize(CityMapController mapController)
        {
            controller = mapController;
        }

        private void Update()
        {
            if (controller == null || !controller.IsOpen)
            {
                return;
            }

            Vector2 panInput = ReadPanInput();
            if (panInput.sqrMagnitude <= 0.0001f)
            {
                return;
            }

            mapViewport.ScrollBy(
                panInput.normalized *
                KeyboardPanSpeed *
                Time.unscaledDeltaTime);
        }

        private void OnGUI()
        {
            if (controller == null || !controller.IsInitialized)
            {
                return;
            }

            EnsureStyles();
            GUI.depth = -90;
            if (!controller.IsOpen)
            {
                wasOpen = false;
                isPointerPanning = false;
                pointerPanButton = -1;
                return;
            }

            RetroUiCanvas canvas = RetroUiTheme.CalculateCanvas(
                Screen.width,
                Screen.height);
            Vector2 logicalPointer =
                RetroUiTheme.LogicalMousePosition(canvas);

            RetroUiTheme.FillRect(
                new Rect(0f, 0f, Screen.width, Screen.height),
                Backdrop);
            Matrix4x4 previousMatrix =
                RetroUiTheme.BeginCanvas(canvas);
            try
            {
                Rect panel = new Rect(
                    8f,
                    8f,
                    RetroUiTheme.LogicalWidth - 16f,
                    RetroUiTheme.LogicalHeight - 16f);
                RetroUiTheme.DrawPanel(
                    panel,
                    RetroUiTheme.Panel,
                    RetroUiTheme.Accent,
                    true,
                    3f,
                    1f);

                GUI.Label(
                    new Rect(
                        panel.x + 12f,
                        panel.y + 7f,
                        panel.width - 24f,
                        22f),
                    LocalizationService.Get("map.title"),
                    titleStyle);

                const float outerMargin = 11f;
                const float headerHeight = 33f;
                float routePanelWidth = Mathf.Clamp(
                    panel.width * 0.28f,
                    130f,
                    170f);
                Rect content = new Rect(
                    panel.x + outerMargin,
                    panel.y + headerHeight,
                    panel.width - outerMargin * 2f,
                    panel.height - headerHeight);
                Rect mapArea = new Rect(
                    content.x,
                    content.y,
                    content.width - routePanelWidth - 9f,
                    content.height);
                Rect routePanel = new Rect(
                    mapArea.xMax + 9f,
                    content.y,
                    routePanelWidth,
                    content.height);

                mapViewport.Configure(
                    controller.Layout.MapWorldXZBounds,
                    controller.Layout.NodeSpacing,
                    mapArea.size,
                    MinimumMapCellPixels);
                if (!wasOpen)
                {
                    mapViewport.CenterOnWorld(
                        controller.PlayerWorldPosition,
                        controller.Layout.MapWorldXZBounds);
                    wasOpen = true;
                }

                HandlePointerScrolling(mapArea, logicalPointer);
                hoverTargets.Clear();
                hoverCoordinateOffset = mapArea.position;
                hoverClipRect = mapArea;
                DrawSolidRect(mapArea, MapVoid);
                GUI.BeginGroup(mapArea);
                try
                {
                    isMapLineContextActive = true;
                    mapLineClipRect = new Rect(
                        Vector2.zero,
                        mapArea.size);
                    mapLineGroupOffset = mapArea.position;
                    MapProjection projection = CreateProjection(
                        mapViewport.ContentRect);
                    DrawMap(projection);
                }
                finally
                {
                    isMapLineContextActive = false;
                    GUI.EndGroup();
                    hoverCoordinateOffset = Vector2.zero;
                }

                RetroUiTheme.StrokeRect(
                    mapArea,
                    1f,
                    RetroUiTheme.BorderMuted);
                DrawScrollIndicators(mapArea);
                DrawRoutePanel(routePanel);
                DrawHoverTooltip(
                    mapArea,
                    logicalPointer);
            }
            finally
            {
                RetroUiTheme.EndCanvas(previousMatrix);
            }
        }

        private void DrawMap(MapProjection projection)
        {
            RetroUiTheme.DrawPanel(
                projection.ScreenRect,
                MapVoid,
                RetroUiTheme.BorderMuted,
                true,
                2f,
                1f);
            DrawSurfaces(projection);
            DrawBuildings(projection);
            DrawRoads(projection);
            DrawBusRoute(projection);
            DrawDistrictLabels(projection);
            DrawRoute(projection);
            DrawBusStops(projection);
            DrawPointsOfInterest(projection);
            DrawSupermarket(projection);
            DrawBars(projection);
            DrawPlayerHome(projection);
            DrawPlayer(projection);
            DrawBusLegend();
        }

        private void HandlePointerScrolling(
            Rect mapBounds,
            Vector2 logicalPointer)
        {
            Event current = Event.current;
            if (isPointerPanning)
            {
                if (current.type == EventType.MouseDrag &&
                    current.button == pointerPanButton)
                {
                    mapViewport.ScrollBy(
                        previousPanPointer - logicalPointer);
                    previousPanPointer = logicalPointer;
                    current.Use();
                    return;
                }

                if (current.type == EventType.MouseUp &&
                    current.button == pointerPanButton)
                {
                    isPointerPanning = false;
                    pointerPanButton = -1;
                    current.Use();
                    return;
                }
            }

            if (!mapBounds.Contains(logicalPointer))
            {
                return;
            }

            if (current.type == EventType.MouseDown &&
                (current.button == 1 || current.button == 2) &&
                (mapViewport.CanScrollHorizontal ||
                 mapViewport.CanScrollVertical))
            {
                isPointerPanning = true;
                pointerPanButton = current.button;
                previousPanPointer = logicalPointer;
                current.Use();
                return;
            }

            if (current.type != EventType.ScrollWheel)
            {
                return;
            }

            Vector2 wheel = current.delta * MouseWheelStep;
            Vector2 scrollDelta = Vector2.zero;
            if (current.shift && mapViewport.CanScrollHorizontal)
            {
                scrollDelta.x = Mathf.Abs(wheel.x) > 0.01f
                    ? wheel.x
                    : wheel.y;
            }
            else if (mapViewport.CanScrollVertical)
            {
                scrollDelta.y = wheel.y;
                if (mapViewport.CanScrollHorizontal &&
                    Mathf.Abs(wheel.x) > 0.01f)
                {
                    scrollDelta.x = wheel.x;
                }
            }
            else if (mapViewport.CanScrollHorizontal)
            {
                scrollDelta.x = Mathf.Abs(wheel.x) > 0.01f
                    ? wheel.x
                    : wheel.y;
            }

            if (mapViewport.ScrollBy(scrollDelta))
            {
                current.Use();
            }
        }

        private void DrawScrollIndicators(Rect mapBounds)
        {
            const float edgeInset = 4f;
            const float trackThickness = 4f;
            const float otherAxisClearance = 7f;
            Color trackColor = RetroUiTheme.WithAlpha(
                RetroUiTheme.Ink,
                0.72f);
            Color thumbColor = RetroUiTheme.AccentPale;

            if (mapViewport.CanScrollHorizontal)
            {
                float rightInset = mapViewport.CanScrollVertical
                    ? edgeInset + otherAxisClearance
                    : edgeInset;
                Rect track = new Rect(
                    mapBounds.x + edgeInset,
                    mapBounds.yMax - edgeInset - trackThickness,
                    mapBounds.width - edgeInset - rightInset,
                    trackThickness);
                DrawSolidRect(track, trackColor);
                DrawSolidRect(
                    mapViewport.CreateHorizontalThumb(track),
                    thumbColor);
            }

            if (mapViewport.CanScrollVertical)
            {
                float bottomInset = mapViewport.CanScrollHorizontal
                    ? edgeInset + otherAxisClearance
                    : edgeInset;
                Rect track = new Rect(
                    mapBounds.xMax - edgeInset - trackThickness,
                    mapBounds.y + edgeInset,
                    trackThickness,
                    mapBounds.height - edgeInset - bottomInset);
                DrawSolidRect(track, trackColor);
                DrawSolidRect(
                    mapViewport.CreateVerticalThumb(track),
                    thumbColor);
            }
        }

        private static Vector2 ReadPanInput()
        {
            Vector2 input = Vector2.zero;
            Keyboard keyboard = Keyboard.current;
            if (keyboard != null)
            {
                input.x += keyboard.dKey.isPressed ? 1f : 0f;
                input.x -= keyboard.aKey.isPressed ? 1f : 0f;
                input.y += keyboard.sKey.isPressed ? 1f : 0f;
                input.y -= keyboard.wKey.isPressed ? 1f : 0f;
            }

            Gamepad gamepad = Gamepad.current;
            if (gamepad != null)
            {
                Vector2 stick = gamepad.rightStick.ReadValue();
                if (stick.sqrMagnitude >= 0.04f)
                {
                    input += new Vector2(stick.x, -stick.y);
                }
            }

            return Vector2.ClampMagnitude(input, 1f);
        }

        private void DrawSurfaces(MapProjection projection)
        {
            IReadOnlyList<CitySurfaceDescriptor> surfaces =
                controller.Layout.Surfaces;
            for (int index = 0; index < surfaces.Count; index++)
            {
                CitySurfaceDescriptor surface = surfaces[index];
                Vector2 topLeft = projection.WorldToScreen(
                    new Vector3(
                        surface.WorldBounds.xMin,
                        0f,
                        surface.WorldBounds.yMax));
                Vector2 bottomRight = projection.WorldToScreen(
                    new Vector3(
                        surface.WorldBounds.xMax,
                        0f,
                        surface.WorldBounds.yMin));
                DrawSolidRect(
                    Rect.MinMaxRect(
                        topLeft.x,
                        topLeft.y,
                        bottomRight.x,
                        bottomRight.y),
                    ResolveSurfaceMapColor(surface));
            }
        }

        private void DrawBuildings(MapProjection projection)
        {
            IReadOnlyList<BuildingLot> lots =
                controller.Layout.BuildingLots;
            for (int index = 0; index < lots.Count; index++)
            {
                BuildingLot lot = lots[index];
                Vector3 minimum = lot.Center - new Vector3(
                    lot.Size.x * 0.5f,
                    0f,
                    lot.Size.y * 0.5f);
                Vector3 maximum = lot.Center + new Vector3(
                    lot.Size.x * 0.5f,
                    0f,
                    lot.Size.y * 0.5f);
                Vector2 topLeft = projection.WorldToScreen(
                    new Vector3(minimum.x, 0f, maximum.z));
                Vector2 bottomRight = projection.WorldToScreen(
                    new Vector3(maximum.x, 0f, minimum.z));
                Rect buildingRect = Rect.MinMaxRect(
                    topLeft.x,
                    topLeft.y,
                    bottomRight.x,
                    bottomRight.y);
                bool isPointOfInterest =
                    controller.IsPointOfInterestLot(lot.Cell);
                DrawSolidRect(
                    buildingRect,
                    GetLotColor(lot, isPointOfInterest));
                if (isPointOfInterest)
                {
                    DrawOpenPublicPlaceLot(
                        buildingRect,
                        GetDistrictColor(lot.District));
                }

                if (controller.DebugTeleportEnabled)
                {
                    int mapObjectIndex = index;
                    RegisterHoverTarget(
                        buildingRect,
                        buildingRect.center,
                        controller.GetMapObjectLabel(mapObjectIndex),
                        PointOfInterestHoverPriority);
                    if (mapObjectIndex ==
                        controller.SelectedMapObjectIndex)
                    {
                        RetroUiTheme.StrokeRect(
                            buildingRect,
                            2f,
                            RetroUiTheme.AccentPale);
                    }

                    if (GUI.Button(
                        buildingRect,
                        GUIContent.none,
                        GUIStyle.none))
                    {
                        controller.QueueSelectMapObject(mapObjectIndex);
                    }
                }
            }
        }

        private void DrawOpenPublicPlaceLot(
            Rect lotRect,
            Color districtColor)
        {
            float tick = Mathf.Max(
                1f,
                Mathf.Min(lotRect.width, lotRect.height) * 0.22f);
            Color accent =
                RetroUiTheme.WithAlpha(districtColor, 0.9f);
            Vector2[] corners =
            {
                new Vector2(lotRect.xMin, lotRect.yMin),
                new Vector2(lotRect.xMax, lotRect.yMin),
                new Vector2(lotRect.xMax, lotRect.yMax),
                new Vector2(lotRect.xMin, lotRect.yMax)
            };
            Vector2[] horizontalDirections =
            {
                Vector2.right,
                Vector2.left,
                Vector2.left,
                Vector2.right
            };
            Vector2[] verticalDirections =
            {
                Vector2.down,
                Vector2.down,
                Vector2.up,
                Vector2.up
            };
            for (int index = 0; index < corners.Length; index++)
            {
                DrawLine(
                    corners[index],
                    corners[index] + horizontalDirections[index] * tick,
                    1f,
                    accent);
                DrawLine(
                    corners[index],
                    corners[index] + verticalDirections[index] * tick,
                    1f,
                    accent);
            }
        }

        private void DrawRoads(MapProjection projection)
        {
            float worldWidth =
                projection.MaximumX - projection.MinimumX;
            float roadWidth = Mathf.Clamp(
                Mathf.Round(
                    controller.Layout.RoadWidth /
                    Mathf.Max(0.01f, worldWidth) *
                    projection.ScreenRect.width),
                2f,
                8f);

            IReadOnlyList<RoadEdge> roads =
                controller.Layout.RoadEdges;
            for (int index = 0; index < roads.Count; index++)
            {
                RoadEdge edge = roads[index];
                CityPathKind pathKind =
                    controller.Layout.GetPathKind(edge);
                DrawLine(
                    projection.WorldToScreen(
                        controller.Layout.GetNodeWorldPosition(edge.A)),
                    projection.WorldToScreen(
                        controller.Layout.GetNodeWorldPosition(edge.B)),
                    GetPathWidth(pathKind, roadWidth),
                    GetPathColor(pathKind));
            }
        }

        private void DrawDistrictLabels(MapProjection projection)
        {
            IReadOnlyList<CityAreaPlacement> areas =
                controller.Layout.Blueprint.Areas;
            for (int index = 0; index < areas.Count; index++)
            {
                CityAreaPlacement area = areas[index];
                Vector2 position = projection.WorldToScreen(
                    ResolveAreaLabelPosition(
                        controller.Layout,
                        area));
                const float labelWidth = 104f;
                const float labelHeight = 13f;
                var labelRect = new Rect(
                    Mathf.Round(position.x - labelWidth * 0.5f),
                    Mathf.Round(position.y - labelHeight * 0.5f),
                    labelWidth,
                    labelHeight);
                DrawSolidRect(labelRect, DistrictLabelBackdrop);
                RetroUiTheme.StrokeRect(
                    labelRect,
                    1f,
                    RetroUiTheme.WithAlpha(
                        area.Definition.MapColor,
                        0.92f));
                GUI.Label(
                    labelRect,
                    LocalizationService.Get(
                        area.Definition.LocalizationKey),
                    districtLabelStyle);
            }
        }

        private void DrawBusRoute(MapProjection projection)
        {
            IReadOnlyList<Vector3> points =
                controller.BusOverlay.RoutePoints;
            if (points.Count < 2)
            {
                return;
            }

            DrawBusRoutePass(
                projection,
                points,
                4f,
                RetroUiTheme.Ink);
            DrawBusRoutePass(
                projection,
                points,
                2f,
                BusRoute);
        }

        private void DrawBusRoutePass(
            MapProjection projection,
            IReadOnlyList<Vector3> points,
            float width,
            Color color)
        {
            for (int index = 1; index < points.Count; index++)
            {
                DrawLine(
                    projection.WorldToScreen(points[index - 1]),
                    projection.WorldToScreen(points[index]),
                    width,
                    color);
            }
        }

        private static Vector3 ResolveAreaLabelPosition(
            CityLayout layout,
            CityAreaPlacement area)
        {
            Vector2 average = Vector2.zero;
            for (int index = 0; index < area.Cells.Count; index++)
            {
                Vector2Int cell = area.Cells[index];
                average += new Vector2(cell.x, cell.y);
            }

            average /= area.Cells.Count;
            Vector2Int anchor = area.Cells[0];
            float bestDistance = float.PositiveInfinity;
            for (int index = 0; index < area.Cells.Count; index++)
            {
                Vector2Int cell = area.Cells[index];
                float distance = (new Vector2(cell.x, cell.y) - average)
                    .sqrMagnitude;
                if (distance < bestDistance)
                {
                    bestDistance = distance;
                    anchor = cell;
                }
            }

            return layout.GetGridWorldPosition(anchor) +
                   new Vector3(
                       layout.NodeSpacing.x * 0.5f,
                       0f,
                       layout.NodeSpacing.y * 0.5f);
        }

        private void DrawRoute(MapProjection projection)
        {
            CityRoutePath path = controller.CurrentPath;
            if (path == null || path.IsEmpty)
            {
                return;
            }

            IReadOnlyList<Vector3> points = path.Points;
            for (int index = 1; index < points.Count; index++)
            {
                DrawLine(
                    projection.WorldToScreen(points[index - 1]),
                    projection.WorldToScreen(points[index]),
                    3f,
                    Route);
            }
        }

        private void DrawBusStops(MapProjection projection)
        {
            IReadOnlyList<CityMapBusStopMarker> stops =
                controller.BusOverlay.Stops;
            for (int index = 0; index < stops.Count; index++)
            {
                CityMapBusStopMarker stop = stops[index];
                Vector2 position = projection.WorldToScreen(
                    stop.WorldPosition);
                RegisterHoverTarget(
                    CreateCenteredRect(position, 17f, 17f),
                    position,
                    controller.GetBusStopLabel(index),
                    BusStopHoverPriority);
                DrawBusStopMarker(
                    position,
                    stop.Ordinal.ToString());
            }
        }

        private void DrawBusStopMarker(
            Vector2 center,
            string ordinal)
        {
            Rect marker = CreateCenteredRect(center, 11f, 11f);
            DrawSolidRect(marker, RetroUiTheme.Ink);
            DrawSolidRect(
                new Rect(
                    marker.x + 2f,
                    marker.y + 2f,
                    marker.width - 4f,
                    marker.height - 4f),
                BusStop);
            if (!string.IsNullOrEmpty(ordinal))
            {
                GUI.Label(marker, ordinal, routeBadgeStyle);
            }
        }

        private void DrawBusLegend()
        {
            CityMapBusOverlay overlay = controller.BusOverlay;
            if (overlay.IsEmpty)
            {
                return;
            }

            bool includeStop = overlay.Stops.Count > 0;
            Rect legend = CreateBusLegendRect(
                mapLineClipRect,
                includeStop);
            RemoveHoverTargetsUnderLegend(legend);
            DrawSolidRect(
                legend,
                RetroUiTheme.WithAlpha(RetroUiTheme.MapGround, 0.9f));
            RetroUiTheme.StrokeRect(
                legend,
                1f,
                RetroUiTheme.BorderMuted);

            float routeY = legend.y + 9f;
            DrawLine(
                new Vector2(legend.x + 7f, routeY),
                new Vector2(legend.x + 25f, routeY),
                4f,
                RetroUiTheme.Ink);
            DrawLine(
                new Vector2(legend.x + 7f, routeY),
                new Vector2(legend.x + 25f, routeY),
                2f,
                BusRoute);
            GUI.Label(
                new Rect(
                    legend.x + 31f,
                    legend.y + 2f,
                    legend.width - 35f,
                    14f),
                LocalizationService.Get("map.bus.route"),
                pointOfInterestItemStyle);

            if (!includeStop)
            {
                return;
            }

            float stopY = legend.y + 24f;
            DrawBusStopMarker(
                new Vector2(legend.x + 16f, stopY),
                string.Empty);
            GUI.Label(
                new Rect(
                    legend.x + 31f,
                    legend.y + 17f,
                    legend.width - 35f,
                    14f),
                LocalizationService.Get("map.bus.stop_legend"),
                pointOfInterestItemStyle);
        }

        private void RemoveHoverTargetsUnderLegend(Rect localLegend)
        {
            Rect globalLegend = new Rect(
                localLegend.position + hoverCoordinateOffset,
                localLegend.size);
            for (int index = hoverTargets.Count - 1;
                 index >= 0;
                 index--)
            {
                Rect overlap = Intersect(
                    hoverTargets[index].Hitbox,
                    globalLegend);
                if (overlap.width > 0f && overlap.height > 0f)
                {
                    hoverTargets.RemoveAt(index);
                }
            }
        }

        internal static Rect CreateBusLegendRect(
            Rect visibleMapRect,
            bool includeStop)
        {
            const float margin = 5f;
            float width = Mathf.Min(
                132f,
                Mathf.Max(1f, visibleMapRect.width - margin * 2f));
            float requestedHeight = includeStop ? 33f : 18f;
            float height = Mathf.Min(
                requestedHeight,
                Mathf.Max(1f, visibleMapRect.height - margin * 2f));
            return RetroUiTheme.SnapRect(new Rect(
                visibleMapRect.x + margin,
                visibleMapRect.y + margin,
                width,
                height));
        }

        private void DrawBars(MapProjection projection)
        {
            for (int index = 0; index < controller.Bars.Count; index++)
            {
                BuildingLot bar = controller.Bars[index];
                Vector2 position =
                    projection.WorldToScreen(bar.ReturnPosition);
                int routeOrder = controller.GetRouteOrder(bar.BarId);
                bool selected = routeOrder >= 0;
                int mapObjectIndex =
                    controller.FindMapObjectIndex(bar);
                bool focused = controller.DebugTeleportEnabled
                    ? mapObjectIndex ==
                      controller.SelectedMapObjectIndex
                    : index == controller.SelectedBarIndex;
                const float markerSize = 17f;
                Rect marker = new Rect(
                    position.x - markerSize * 0.5f,
                    position.y - markerSize * 0.5f,
                    markerSize,
                    markerSize);
                RegisterHoverTarget(
                    marker,
                    position,
                    controller.GetBarLabel(index),
                    BarHoverPriority);

                if (focused)
                {
                    DrawSolidRect(
                        new Rect(
                            marker.x - 2f,
                            marker.y - 2f,
                            marker.width + 4f,
                            marker.height + 4f),
                        RetroUiTheme.Text);
                }

                DrawSolidRect(marker, UnselectedBar);

                Color previousContentColor = GUI.contentColor;
                GUI.contentColor = RetroUiTheme.Text;
                string markerLabel = (index + 1).ToString();
                if (GUI.Button(marker, markerLabel, markerButtonStyle))
                {
                    if (controller.DebugTeleportEnabled)
                    {
                        controller.QueueSelectMapObject(mapObjectIndex);
                    }
                    else
                    {
                        controller.QueueToggleBar(index);
                    }
                }

                if (selected)
                {
                    Rect routeBadge = new Rect(
                        marker.xMax - 5f,
                        marker.y - 5f,
                        10f,
                        10f);
                    RetroUiTheme.DrawPanel(
                        routeBadge,
                        RetroUiTheme.Accent,
                        RetroUiTheme.Ink,
                        false,
                        1f,
                        1f);
                    GUI.contentColor = RetroUiTheme.Ink;
                    GUI.Label(
                        routeBadge,
                        (routeOrder + 1).ToString(),
                        routeBadgeStyle);
                }

                GUI.contentColor = previousContentColor;
            }
        }

        private void DrawPointsOfInterest(MapProjection projection)
        {
            IReadOnlyList<CityMapPointOfInterest> pointsOfInterest =
                controller.PointsOfInterest;
            for (int index = 0; index < pointsOfInterest.Count; index++)
            {
                CityMapPointOfInterest pointOfInterest =
                    pointsOfInterest[index];
                Vector2 position = projection.WorldToScreen(
                    pointOfInterest.WorldPosition);
                DrawPointOfInterestMarker(
                    pointOfInterest.Kind,
                    position,
                    GetDistrictColor(pointOfInterest.District),
                    6f,
                    4f,
                    2f);
                RegisterHoverTarget(
                    CreateCenteredRect(position, 17f, 17f),
                    position,
                    controller.GetPointOfInterestLabel(index),
                    PointOfInterestHoverPriority);
            }
        }

        private void DrawSupermarket(MapProjection projection)
        {
            BuildingLot supermarket = controller.Supermarket;
            if (supermarket == null)
            {
                return;
            }

            Vector2 position =
                projection.WorldToScreen(supermarket.Center);
            Rect hitbox = CreateCenteredRect(position, 19f, 19f);
            RegisterHoverTarget(
                hitbox,
                position,
                controller.GetSupermarketLabel(),
                LandmarkHoverPriority);

            Rect bagBody = new Rect(
                position.x - 6f,
                position.y - 3f,
                12f,
                11f);
            DrawSolidRect(
                new Rect(
                    bagBody.x - 2f,
                    bagBody.y - 2f,
                    bagBody.width + 4f,
                    bagBody.height + 4f),
                RetroUiTheme.Ink);
            DrawSolidRect(bagBody, Supermarket);
            DrawLine(
                new Vector2(position.x - 4f, bagBody.y),
                new Vector2(position.x - 4f, position.y - 7f),
                2f,
                Supermarket);
            DrawLine(
                new Vector2(position.x - 4f, position.y - 7f),
                new Vector2(position.x + 4f, position.y - 7f),
                2f,
                Supermarket);
            DrawLine(
                new Vector2(position.x + 4f, position.y - 7f),
                new Vector2(position.x + 4f, bagBody.y),
                2f,
                Supermarket);
            DrawSolidRect(
                new Rect(position.x - 1f, position.y, 2f, 5f),
                RetroUiTheme.Ink);
        }

        private void DrawPlayer(MapProjection projection)
        {
            Vector2 position =
                projection.WorldToScreen(controller.PlayerWorldPosition);
            Vector3 forward = controller.PlayerForward;
            Vector2 screenForward = new Vector2(forward.x, -forward.z);
            if (screenForward.sqrMagnitude < 0.001f)
            {
                screenForward = Vector2.up;
            }

            screenForward.Normalize();
            Vector2 arrowTip = position + screenForward * 11f;
            Vector2 arrowSide = new Vector2(
                -screenForward.y,
                screenForward.x);
            DrawLine(
                position,
                arrowTip,
                3f,
                Player);
            DrawLine(
                arrowTip,
                arrowTip - screenForward * 4f + arrowSide * 3f,
                2f,
                Player);
            DrawLine(
                arrowTip,
                arrowTip - screenForward * 4f - arrowSide * 3f,
                2f,
                Player);
            DrawSolidRect(
                new Rect(position.x - 3f, position.y - 3f, 6f, 6f),
                Player);
            GUI.Label(
                new Rect(position.x + 5f, position.y - 7f, 55f, 13f),
                LocalizationService.Get("map.player"),
                routeItemStyle);
        }

        private void DrawPlayerHome(MapProjection projection)
        {
            BuildingLot home = controller.PlayerHome;
            if (home == null)
            {
                return;
            }

            Vector2 position =
                projection.WorldToScreen(home.Center);
            const float bodyWidth = 13f;
            const float bodyHeight = 10f;
            Rect body = new Rect(
                position.x - bodyWidth * 0.5f,
                position.y - 2f,
                bodyWidth,
                bodyHeight);
            RegisterHoverTarget(
                Rect.MinMaxRect(
                    body.xMin - 2f,
                    body.yMin - 6f,
                    body.xMax + 2f,
                    body.yMax),
                position,
                LocalizationService.Get("map.home"),
                LandmarkHoverPriority);
            DrawSolidRect(body, PlayerHome);
            Vector2 roofLeft =
                new Vector2(body.x - 2f, body.y + 1f);
            Vector2 roofPeak =
                new Vector2(position.x, body.y - 6f);
            Vector2 roofRight =
                new Vector2(body.xMax + 2f, body.y + 1f);
            DrawLine(
                roofLeft,
                roofPeak,
                3f,
                PlayerHome);
            DrawLine(
                roofPeak,
                roofRight,
                3f,
                PlayerHome);
            DrawSolidRect(
                new Rect(
                    position.x - 1.5f,
                    body.yMax - 5f,
                    3f,
                    5f),
                RetroUiTheme.Ink);
            GUI.Label(
                new Rect(
                    body.xMax + 4f,
                    body.y - 2f,
                    58f,
                    14f),
                LocalizationService.Get("map.home"),
                routeItemStyle);
        }

        private void RegisterHoverTarget(
            Rect hitbox,
            Vector2 anchor,
            string label,
            int priority)
        {
            if (string.IsNullOrWhiteSpace(label))
            {
                return;
            }

            Rect globalHitbox = new Rect(
                hitbox.position + hoverCoordinateOffset,
                hitbox.size);
            Rect clippedHitbox = Intersect(
                globalHitbox,
                hoverClipRect);
            if (clippedHitbox.width <= 0f ||
                clippedHitbox.height <= 0f)
            {
                return;
            }

            hoverTargets.Add(
                new MapHoverTarget(
                    clippedHitbox,
                    anchor + hoverCoordinateOffset,
                    label,
                    priority));
        }

        private static Rect Intersect(Rect left, Rect right)
        {
            float xMin = Mathf.Max(left.xMin, right.xMin);
            float yMin = Mathf.Max(left.yMin, right.yMin);
            float xMax = Mathf.Min(left.xMax, right.xMax);
            float yMax = Mathf.Min(left.yMax, right.yMax);
            return Rect.MinMaxRect(
                xMin,
                yMin,
                Mathf.Max(xMin, xMax),
                Mathf.Max(yMin, yMax));
        }

        private void DrawHoverTooltip(
            Rect mapBounds,
            Vector2 logicalPointer)
        {
            string label = ResolveHoveredLabel(
                hoverTargets,
                logicalPointer);
            if (string.IsNullOrEmpty(label))
            {
                return;
            }

            var content = new GUIContent(label);
            float maximumTextWidth = Mathf.Max(
                48f,
                Mathf.Min(176f, mapBounds.width - 20f));
            float naturalTextWidth =
                tooltipStyle.CalcSize(content).x;
            float textWidth = Mathf.Clamp(
                naturalTextWidth,
                48f,
                maximumTextWidth);
            float textHeight = Mathf.Max(
                12f,
                tooltipStyle.CalcHeight(content, textWidth));
            Rect tooltip = CreateTooltipRect(
                logicalPointer,
                new Vector2(textWidth + 12f, textHeight + 8f),
                mapBounds);

            RetroUiTheme.DrawPanel(
                tooltip,
                TooltipBackdrop,
                RetroUiTheme.AccentPale,
                true,
                2f,
                1f);
            GUI.Label(
                new Rect(
                    tooltip.x + 6f,
                    tooltip.y + 4f,
                    tooltip.width - 12f,
                    tooltip.height - 8f),
                content,
                tooltipStyle);
        }

        internal static string ResolveHoveredLabel(
            IReadOnlyList<MapHoverTarget> targets,
            Vector2 pointer)
        {
            if (targets == null)
            {
                return string.Empty;
            }

            string bestLabel = string.Empty;
            float bestDistance = float.PositiveInfinity;
            int bestPriority = int.MinValue;
            for (int index = 0; index < targets.Count; index++)
            {
                MapHoverTarget target = targets[index];
                if (string.IsNullOrEmpty(target.Label) ||
                    !target.Hitbox.Contains(pointer))
                {
                    continue;
                }

                float distance =
                    (target.Anchor - pointer).sqrMagnitude;
                if (distance < bestDistance ||
                    (Mathf.Approximately(distance, bestDistance) &&
                     target.Priority > bestPriority))
                {
                    bestLabel = target.Label;
                    bestDistance = distance;
                    bestPriority = target.Priority;
                }
            }

            return bestLabel;
        }

        internal static Rect CreateTooltipRect(
            Vector2 pointer,
            Vector2 requestedSize,
            Rect bounds)
        {
            const float edgePadding = 3f;
            const float pointerGap = 10f;
            Rect safeBounds = new Rect(
                bounds.x + edgePadding,
                bounds.y + edgePadding,
                Mathf.Max(1f, bounds.width - edgePadding * 2f),
                Mathf.Max(1f, bounds.height - edgePadding * 2f));
            float width = Mathf.Min(
                Mathf.Max(1f, requestedSize.x),
                safeBounds.width);
            float height = Mathf.Min(
                Mathf.Max(1f, requestedSize.y),
                safeBounds.height);
            float x = pointer.x + pointerGap;
            float y = pointer.y + pointerGap;

            if (x + width > safeBounds.xMax)
            {
                x = pointer.x - pointerGap - width;
            }

            if (y + height > safeBounds.yMax)
            {
                y = pointer.y - pointerGap - height;
            }

            x = Mathf.Clamp(
                x,
                safeBounds.xMin,
                safeBounds.xMax - width);
            y = Mathf.Clamp(
                y,
                safeBounds.yMin,
                safeBounds.yMax - height);
            return RetroUiTheme.SnapRect(
                new Rect(x, y, width, height));
        }

        internal static Rect CreateCenteredRect(
            Vector2 center,
            float width,
            float height)
        {
            return new Rect(
                center.x - width * 0.5f,
                center.y - height * 0.5f,
                width,
                height);
        }

        private void DrawRoutePanel(Rect panel)
        {
            RetroUiTheme.DrawPanel(
                panel,
                RetroUiTheme.PanelInset,
                RetroUiTheme.BorderMuted,
                true,
                2f,
                1f);
            if (controller.DebugTeleportEnabled)
            {
                DrawDebugTeleportPanel(panel);
                return;
            }

            GUI.Label(
                new Rect(
                    panel.x + 6f,
                    panel.y + 5f,
                    panel.width - 12f,
                    18f),
                LocalizationService.Get("map.route_title"),
                subtitleStyle);

            IReadOnlyList<string> route = controller.Route;
            if (route.Count == 0)
            {
                GUI.Label(
                    new Rect(
                        panel.x + 9f,
                        panel.y + 32f,
                        panel.width - 18f,
                        42f),
                    LocalizationService.Get("map.route_empty"),
                    centeredStyle);
            }
            else
            {
                DrawRouteRows(panel, route);
            }

            DrawPointOfInterestLegend(panel, route.Count);

            CityRoutePath path = controller.CurrentPath;
            float distance = path == null ? 0f : path.TotalLength;
            GUI.Label(
                new Rect(
                    panel.x + 7f,
                    panel.yMax - 41f,
                    panel.width - 14f,
                    14f),
                string.Format(
                    LocalizationService.Get("map.distance"),
                    distance),
                centeredStyle);

            Rect clearButton = new Rect(
                panel.x + 8f,
                panel.yMax - 24f,
                panel.width - 16f,
                16f);
            RetroUiTheme.DrawPanel(
                clearButton,
                RetroUiTheme.PanelRaised,
                RetroUiTheme.Accent,
                false,
                1f,
                1f);
            bool previousEnabled = GUI.enabled;
            GUI.enabled = route.Count > 0;
            if (GUI.Button(
                clearButton,
                LocalizationService.Get("map.clear"),
                hintStyle))
            {
                controller.QueueClearRoute();
            }

            GUI.enabled = previousEnabled;
        }

        private void DrawDebugTeleportPanel(Rect panel)
        {
            GUI.Label(
                new Rect(
                    panel.x + 6f,
                    panel.y + 5f,
                    panel.width - 12f,
                    18f),
                LocalizationService.Get("map.teleport.title"),
                subtitleStyle);

            BuildingLot selected = controller.SelectedMapObject;
            if (selected == null)
            {
                GUI.Label(
                    new Rect(
                        panel.x + 9f,
                        panel.y + 34f,
                        panel.width - 18f,
                        58f),
                    LocalizationService.Get("map.teleport.select"),
                    centeredStyle);
                return;
            }

            GUI.Label(
                new Rect(
                    panel.x + 9f,
                    panel.y + 35f,
                    panel.width - 18f,
                    42f),
                controller.GetMapObjectLabel(
                    controller.SelectedMapObjectIndex),
                centeredStyle);
            GUI.Label(
                new Rect(
                    panel.x + 9f,
                    panel.y + 84f,
                    panel.width - 18f,
                    28f),
                LocalizationService.Get("map.teleport.question"),
                centeredStyle);

            Rect confirmButton = new Rect(
                panel.x + 18f,
                panel.y + 119f,
                panel.width - 36f,
                24f);
            RetroUiTheme.DrawPanel(
                confirmButton,
                RetroUiTheme.PanelRaised,
                RetroUiTheme.Good,
                true,
                2f,
                1f);
            if (GUI.Button(
                confirmButton,
                LocalizationService.Get("common.yes"),
                hintStyle))
            {
                controller.QueueConfirmDebugTeleport();
            }
        }

        private void DrawPointOfInterestLegend(
            Rect panel,
            int routeCount)
        {
            int pointOfInterestCount = controller.PointsOfInterest.Count;
            if (pointOfInterestCount == 0)
            {
                return;
            }

            Rect legend = CreatePointOfInterestLegendRect(
                panel,
                routeCount,
                pointOfInterestCount);
            DrawSolidRect(
                legend,
                RetroUiTheme.WithAlpha(RetroUiTheme.MapGround, 0.55f));
            RetroUiTheme.StrokeRect(
                legend,
                1f,
                RetroUiTheme.BorderMuted);
            GUI.Label(
                new Rect(
                    legend.x + 4f,
                    legend.y + 2f,
                    legend.width - 8f,
                    16f),
                LocalizationService.Get("map.poi.title"),
                pointOfInterestTitleStyle);

            const float rowHeight = 17f;
            float rowY = legend.y + 19f;
            for (int index = 0;
                 index < pointOfInterestCount;
                 index++)
            {
                CityMapPointOfInterest pointOfInterest =
                    controller.PointsOfInterest[index];
                Vector2 markerCenter = new Vector2(
                    legend.x + 10f,
                    rowY + rowHeight * 0.5f);
                DrawPointOfInterestMarker(
                    pointOfInterest.Kind,
                    markerCenter,
                    GetDistrictColor(pointOfInterest.District),
                    4f,
                    3f,
                    1f);
                GUI.Label(
                    new Rect(
                        legend.x + 19f,
                        rowY,
                        legend.width - 23f,
                        rowHeight),
                    controller.GetPointOfInterestLabel(index),
                    pointOfInterestItemStyle);
                rowY += rowHeight;
            }
        }

        internal static Rect CreatePointOfInterestLegendRect(
            Rect panel,
            int routeCount,
            int pointOfInterestCount)
        {
            const float titleHeight = 19f;
            const float rowHeight = 17f;
            const float bottomPadding = 3f;
            const float footerReserve = 68f;
            float height = titleHeight +
                           Mathf.Max(0, pointOfInterestCount) * rowHeight +
                           bottomPadding;
            float routeBottom = panel.y + 29f +
                                Mathf.Max(0, routeCount) * 26f;
            float preferredTop = Mathf.Max(
                panel.y + 94f,
                routeBottom + 9f);
            float maximumTop = panel.yMax - footerReserve - height;
            float top = Mathf.Max(
                panel.y + 4f,
                Mathf.Min(preferredTop, maximumTop));
            return new Rect(
                panel.x + 6f,
                top,
                panel.width - 12f,
                height);
        }

        private void DrawRouteRows(
            Rect panel,
            IReadOnlyList<string> route)
        {
            const float rowHeight = 22f;
            const float rowGap = 4f;
            float rowY = panel.y + 29f;

            for (int routeIndex = 0;
                 routeIndex < route.Count;
                 routeIndex++)
            {
                string barId = route[routeIndex];
                int barIndex = controller.FindBarIndex(barId);
                if (barIndex < 0)
                {
                    continue;
                }

                Rect row = new Rect(
                    panel.x + 6f,
                    rowY,
                    panel.width - 12f,
                    rowHeight);
                bool focused =
                    barIndex == controller.SelectedBarIndex;
                RetroUiTheme.DrawPanel(
                    row,
                    focused
                        ? RetroUiTheme.PanelRaised
                        : RetroUiTheme.Panel,
                    focused
                        ? RetroUiTheme.Accent
                        : RetroUiTheme.BorderMuted,
                    false,
                    1f,
                    1f);

                const float buttonWidth = 15f;
                const float buttonGap = 2f;
                float buttonsWidth = buttonWidth * 3f + buttonGap * 2f;
                GUI.Label(
                    new Rect(
                        row.x + 5f,
                        row.y,
                        row.width - buttonsWidth - 9f,
                        row.height),
                    $"{routeIndex + 1}. {controller.GetBarLabel(barIndex)}",
                    routeItemStyle);

                float buttonX = row.xMax - buttonsWidth - 3f;
                bool previousEnabled = GUI.enabled;
                Rect upButton = new Rect(
                    buttonX,
                    row.y + 3f,
                    buttonWidth,
                    15f);
                RetroUiTheme.DrawPanel(
                    upButton,
                    RetroUiTheme.PanelRaised,
                    RetroUiTheme.BorderMuted,
                    false,
                    1f,
                    1f);
                GUI.enabled = routeIndex > 0;
                if (GUI.Button(
                    upButton,
                    "\u25B2",
                    smallButtonStyle))
                {
                    controller.QueueMoveBar(barId, -1);
                }

                buttonX += buttonWidth + buttonGap;
                Rect downButton = new Rect(
                    buttonX,
                    row.y + 3f,
                    buttonWidth,
                    15f);
                RetroUiTheme.DrawPanel(
                    downButton,
                    RetroUiTheme.PanelRaised,
                    RetroUiTheme.BorderMuted,
                    false,
                    1f,
                    1f);
                GUI.enabled = routeIndex < route.Count - 1;
                if (GUI.Button(
                    downButton,
                    "\u25BC",
                    smallButtonStyle))
                {
                    controller.QueueMoveBar(barId, 1);
                }

                buttonX += buttonWidth + buttonGap;
                Rect removeButton = new Rect(
                    buttonX,
                    row.y + 3f,
                    buttonWidth,
                    15f);
                RetroUiTheme.DrawPanel(
                    removeButton,
                    RetroUiTheme.PanelRaised,
                    RetroUiTheme.Bad,
                    false,
                    1f,
                    1f);
                GUI.enabled = true;
                if (GUI.Button(
                    removeButton,
                    "\u00D7",
                    smallButtonStyle))
                {
                    controller.QueueToggleBar(barIndex);
                }

                GUI.enabled = previousEnabled;
                rowY += rowHeight + rowGap;
            }
        }

        private MapProjection CreateProjection(Rect mapRect)
        {
            CityLayout layout = controller.Layout;
            Rect bounds = layout.MapWorldXZBounds;
            float minimumX = bounds.xMin;
            float maximumX = bounds.xMax;
            float minimumZ = bounds.yMin;
            float maximumZ = bounds.yMax;

            return new MapProjection(
                mapRect,
                minimumX,
                maximumX,
                minimumZ,
                maximumZ);
        }

        private void DrawLine(
            Vector2 start,
            Vector2 end,
            float width,
            Color color)
        {
            if (isMapLineContextActive &&
                !TryClipLineToRect(
                    mapLineClipRect,
                    width,
                    ref start,
                    ref end))
            {
                return;
            }

            Vector2 delta = end - start;
            float length = delta.magnitude;
            if (length <= 0.01f)
            {
                return;
            }

            Matrix4x4 previousMatrix = GUI.matrix;
            Color previousColor = GUI.color;
            GUI.color = color;
            GUI.matrix = CreateLineMatrix(
                previousMatrix,
                start,
                end,
                isMapLineContextActive
                    ? mapLineGroupOffset
                    : Vector2.zero);
            GUI.DrawTexture(
                new Rect(0f, -width * 0.5f, length, width),
                Texture2D.whiteTexture);
            GUI.matrix = previousMatrix;
            GUI.color = previousColor;
        }

        private void DrawPointOfInterestMarker(
            CityDistrictPointOfInterestKind kind,
            Vector2 center,
            Color districtColor,
            float halfSize,
            float haloWidth,
            float outlineWidth)
        {
            DrawPointOfInterestOutline(
                kind,
                center,
                halfSize,
                haloWidth,
                RetroUiTheme.Ink);
            DrawPointOfInterestOutline(
                kind,
                center,
                halfSize,
                outlineWidth,
                RetroUiTheme.Text);
            DrawSolidRect(
                new Rect(center.x - 1.5f, center.y - 1.5f, 3f, 3f),
                districtColor);
        }

        private void DrawPointOfInterestOutline(
            CityDistrictPointOfInterestKind kind,
            Vector2 center,
            float halfSize,
            float width,
            Color color)
        {
            switch (kind)
            {
                case CityDistrictPointOfInterestKind.OldTownWaterworksCourt:
                    DrawDiamondOutline(
                        new Vector2(center.x, center.y - halfSize),
                        new Vector2(center.x + halfSize, center.y),
                        new Vector2(center.x, center.y + halfSize),
                        new Vector2(center.x - halfSize, center.y),
                        width,
                        color);
                    break;
                case CityDistrictPointOfInterestKind.ResidentialDryingYard:
                    RetroUiTheme.StrokeRect(
                        new Rect(
                            center.x - halfSize,
                            center.y - halfSize,
                            halfSize * 2f,
                            halfSize * 2f),
                        width,
                        color);
                    break;
                case CityDistrictPointOfInterestKind.IndustrialWeighbridge:
                    RetroUiTheme.StrokeRect(
                        new Rect(
                            center.x - halfSize,
                            center.y - halfSize * 0.55f,
                            halfSize * 2f,
                            halfSize * 1.1f),
                        width,
                        color);
                    break;
                case CityDistrictPointOfInterestKind.NightlifeLastRouteIsland:
                    DrawOpenOctagonOutline(
                        center,
                        halfSize,
                        width,
                        color);
                    break;
                default:
                    throw new ArgumentOutOfRangeException(
                        nameof(kind),
                        kind,
                        "Unsupported map point of interest kind.");
            }
        }

        private void DrawOpenOctagonOutline(
            Vector2 center,
            float radius,
            float width,
            Color color)
        {
            const float diagonal = 0.7071068f;
            Vector2[] points =
            {
                center + new Vector2(0f, -radius),
                center + new Vector2(radius * diagonal, -radius * diagonal),
                center + new Vector2(radius, 0f),
                center + new Vector2(radius * diagonal, radius * diagonal),
                center + new Vector2(0f, radius),
                center + new Vector2(-radius * diagonal, radius * diagonal),
                center + new Vector2(-radius, 0f),
                center + new Vector2(-radius * diagonal, -radius * diagonal)
            };
            for (int index = 0; index < points.Length; index++)
            {
                if (index == 4)
                {
                    continue;
                }

                DrawLine(
                    points[index],
                    points[(index + 1) % points.Length],
                    width,
                    color);
            }
        }

        private void DrawDiamondOutline(
            Vector2 top,
            Vector2 right,
            Vector2 bottom,
            Vector2 left,
            float width,
            Color color)
        {
            DrawLine(top, right, width, color);
            DrawLine(right, bottom, width, color);
            DrawLine(bottom, left, width, color);
            DrawLine(left, top, width, color);
        }

        internal static bool TryClipLineToRect(
            Rect clipRect,
            float width,
            ref Vector2 start,
            ref Vector2 end)
        {
            Vector2 origin = start;
            Vector2 delta = end - origin;
            if (delta.sqrMagnitude <= 0.0001f)
            {
                return false;
            }

            Vector2 direction = delta.normalized;
            float halfWidth = Mathf.Max(0f, width * 0.5f);
            float horizontalInset = Mathf.Abs(direction.y) * halfWidth;
            float verticalInset = Mathf.Abs(direction.x) * halfWidth;
            float xMin = clipRect.xMin + horizontalInset;
            float xMax = clipRect.xMax - horizontalInset;
            float yMin = clipRect.yMin + verticalInset;
            float yMax = clipRect.yMax - verticalInset;
            if (xMin > xMax || yMin > yMax)
            {
                return false;
            }

            float minimumTime = 0f;
            float maximumTime = 1f;
            if (!TryClipLineParameter(
                    -delta.x,
                    origin.x - xMin,
                    ref minimumTime,
                    ref maximumTime) ||
                !TryClipLineParameter(
                    delta.x,
                    xMax - origin.x,
                    ref minimumTime,
                    ref maximumTime) ||
                !TryClipLineParameter(
                    -delta.y,
                    origin.y - yMin,
                    ref minimumTime,
                    ref maximumTime) ||
                !TryClipLineParameter(
                    delta.y,
                    yMax - origin.y,
                    ref minimumTime,
                    ref maximumTime))
            {
                return false;
            }

            start = origin + delta * minimumTime;
            end = origin + delta * maximumTime;
            return (end - start).sqrMagnitude > 0.0001f;
        }

        private static bool TryClipLineParameter(
            float denominator,
            float numerator,
            ref float minimumTime,
            ref float maximumTime)
        {
            if (Mathf.Abs(denominator) <= Mathf.Epsilon)
            {
                return numerator >= 0f;
            }

            float time = numerator / denominator;
            if (denominator < 0f)
            {
                if (time > maximumTime)
                {
                    return false;
                }

                minimumTime = Mathf.Max(minimumTime, time);
            }
            else
            {
                if (time < minimumTime)
                {
                    return false;
                }

                maximumTime = Mathf.Min(maximumTime, time);
            }

            return true;
        }

        private static Matrix4x4 CreateLineMatrix(
            Matrix4x4 parentMatrix,
            Vector2 start,
            Vector2 end,
            Vector2 groupOffset)
        {
            Vector2 direction = (end - start).normalized;
            var logicalLineTransform = Matrix4x4.identity;
            logicalLineTransform.m00 = direction.x;
            logicalLineTransform.m01 = -direction.y;
            logicalLineTransform.m03 = start.x;
            logicalLineTransform.m10 = direction.y;
            logicalLineTransform.m11 = direction.x;
            logicalLineTransform.m13 = start.y;
            // BeginGroup contributes its offset before GUI.matrix. Conjugate
            // the line transform so rotation cannot rotate that group origin.
            Matrix4x4 groupTransform = Matrix4x4.Translate(
                new Vector3(groupOffset.x, groupOffset.y, 0f));
            Matrix4x4 inverseGroupTransform = Matrix4x4.Translate(
                new Vector3(-groupOffset.x, -groupOffset.y, 0f));
            return parentMatrix *
                   groupTransform *
                   logicalLineTransform *
                   inverseGroupTransform;
        }

        private static void DrawSolidRect(Rect rectangle, Color color)
        {
            RetroUiTheme.FillRect(rectangle, color);
        }

        private static Color GetLotColor(
            BuildingLot lot,
            bool isPointOfInterest)
        {
            if (isPointOfInterest)
            {
                return PublicPlaceLand;
            }

            if (lot.LandUse == CityLandUseKind.Park)
            {
                return ParkLand;
            }

            if (lot.IsBar)
            {
                return BarBuilding;
            }

            if (lot.IsPlayerHome)
            {
                return HomeBuilding;
            }

            return GetDistrictColor(lot.District);
        }

        private static Color GetDistrictColor(CityDistrictKind district)
        {
            switch (district)
            {
                case CityDistrictKind.OldTown:
                    return OldTownBuilding;
                case CityDistrictKind.Residential:
                    return ResidentialBuilding;
                case CityDistrictKind.Industrial:
                    return IndustrialBuilding;
                case CityDistrictKind.Nightlife:
                    return NightlifeBuilding;
                case CityDistrictKind.CentralPark:
                    return ParkLand;
                case CityDistrictKind.NorthWaterfront:
                    return WaterfrontLand;
                case CityDistrictKind.Lake:
                    return LakeLand;
                case CityDistrictKind.Cemetery:
                    return CemeteryLand;
                case CityDistrictKind.Yard:
                    return YardLand;
                default:
                    return Building;
            }
        }

        private static Color ResolveSurfaceMapColor(
            CitySurfaceDescriptor surface)
        {
            switch (surface.Kind)
            {
                case CitySurfaceKind.Water:
                    return WaterLand;
                case CitySurfaceKind.Beach:
                    return WaterfrontLand;
                case CitySurfaceKind.LakeShore:
                    return LakeLand;
                case CitySurfaceKind.CemeteryGround:
                    return CemeteryLand;
                case CitySurfaceKind.OpenGround:
                    return YardLand;
                default:
                    return surface.MapColor;
            }
        }

        private static string GetDistrictLocalizationKey(
            CityDistrictKind district)
        {
            switch (district)
            {
                case CityDistrictKind.OldTown:
                    return "map.district.old_town";
                case CityDistrictKind.Residential:
                    return "map.district.residential";
                case CityDistrictKind.Industrial:
                    return "map.district.industrial";
                case CityDistrictKind.Nightlife:
                    return "map.district.nightlife";
                case CityDistrictKind.CentralPark:
                    return "map.district.central_park";
                case CityDistrictKind.NorthWaterfront:
                    return "map.district.north_waterfront";
                case CityDistrictKind.Lake:
                    return "map.district.lake";
                case CityDistrictKind.Cemetery:
                    return "map.district.cemetery";
                case CityDistrictKind.Yard:
                    return "map.district.yard";
                default:
                    throw new ArgumentOutOfRangeException(
                        nameof(district),
                        district,
                        "Unsupported city district.");
            }
        }

        private static Color GetPathColor(CityPathKind pathKind)
        {
            return pathKind == CityPathKind.ParkPath
                ? ParkPath
                : Road;
        }

        private static float GetPathWidth(
            CityPathKind pathKind,
            float streetWidth)
        {
            return pathKind == CityPathKind.ParkPath
                ? Mathf.Max(2f, streetWidth * 0.55f)
                : streetWidth;
        }

        private void EnsureStyles()
        {
            if (titleStyle != null)
            {
                return;
            }

            titleStyle = RetroUiTheme.CreateLabelStyle(
                15,
                TextAnchor.MiddleCenter,
                RetroUiTheme.Accent,
                true);
            subtitleStyle = RetroUiTheme.CreateLabelStyle(
                11,
                TextAnchor.MiddleCenter,
                RetroUiTheme.AccentPale,
                true);
            centeredStyle = RetroUiTheme.CreateLabelStyle(
                8,
                TextAnchor.MiddleCenter,
                RetroUiTheme.Text,
                false,
                true);
            routeItemStyle = RetroUiTheme.CreateLabelStyle(
                8,
                TextAnchor.MiddleLeft,
                RetroUiTheme.Text,
                true);
            markerButtonStyle = RetroUiTheme.CreateButtonStyle(
                9,
                TextAnchor.MiddleCenter,
                RetroUiTheme.Text,
                true);
            routeBadgeStyle = RetroUiTheme.CreateLabelStyle(
                7,
                TextAnchor.MiddleCenter,
                RetroUiTheme.Ink,
                true);
            hintStyle = RetroUiTheme.CreateButtonStyle(
                9,
                TextAnchor.MiddleCenter,
                RetroUiTheme.Text,
                true);
            smallButtonStyle = RetroUiTheme.CreateButtonStyle(
                8,
                TextAnchor.MiddleCenter,
                RetroUiTheme.Text,
                true);
            districtLabelStyle = RetroUiTheme.CreateLabelStyle(
                7,
                TextAnchor.MiddleCenter,
                RetroUiTheme.AccentPale,
                true);
            pointOfInterestTitleStyle = RetroUiTheme.CreateLabelStyle(
                8,
                TextAnchor.MiddleCenter,
                RetroUiTheme.AccentPale,
                true);
            pointOfInterestItemStyle = RetroUiTheme.CreateLabelStyle(
                7,
                TextAnchor.MiddleLeft,
                RetroUiTheme.Text,
                true);
            tooltipStyle = RetroUiTheme.CreateLabelStyle(
                9,
                TextAnchor.MiddleLeft,
                RetroUiTheme.Text,
                true,
                true);
        }
    }
}

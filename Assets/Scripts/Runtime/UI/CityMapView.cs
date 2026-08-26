using System;
using System.Collections.Generic;
using System.Globalization;
using UnityEngine;
using UnityEngine.InputSystem;

namespace BarPromenade
{
    [DisallowMultipleComponent]
    public sealed partial class CityMapView : MonoBehaviour
    {
        // A precinct name is the background layer of the map: it answers
        // "what is this ground" only where no named marker answers first,
        // so it is resolved after every foreground target has missed.
        internal const int AreaHoverPriority = -10;
        private const int ForegroundHoverPriorityFloor = 0;
        private const int PointOfInterestHoverPriority = 10;
        internal const int BusStopHoverPriority = 15;
        private const int BarHoverPriority = 20;
        private const int LandmarkHoverPriority = 30;
        // Highest of the foreground ranks, so when the hero shares a
        // spot with a landmark - their own front door, say - the tie
        // goes to the hero.
        private const int PlayerHoverPriority = 40;
        private const float MinimumMapCellPixels = 22f;
        // A backstop, not a budget: the lattice step comes from the same
        // world size the viewport keeps readable, so a tab can only ask for
        // more lines than this if that contract has been broken.
        private const int MaximumLatticeLinesPerAxis = 512;
        private const float KeyboardPanSpeed = 116f;
        private const float MouseWheelStep = 18f;
        private const float MountainTunnelMarkerWidth = 19f;
        private const float MountainTunnelMarkerHeight = 17f;
        private const float MountainTunnelMarkerEdgePadding = 3f;

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

            /// <summary>
            /// The chart coordinate a screen point sits over. Clamped to the
            /// chart on purpose: the drawn map is the whole world the tab
            /// has, so a pointer beyond its edge names the edge rather than
            /// inventing ground outside it.
            /// </summary>
            public Vector2 ScreenToWorld(Vector2 screenPosition)
            {
                float normalizedX = Mathf.InverseLerp(
                    ScreenRect.xMin,
                    ScreenRect.xMax,
                    screenPosition.x);
                float normalizedZ = Mathf.InverseLerp(
                    ScreenRect.yMax,
                    ScreenRect.yMin,
                    screenPosition.y);
                return new Vector2(
                    Mathf.Lerp(MinimumX, MaximumX, normalizedX),
                    Mathf.Lerp(MinimumZ, MaximumZ, normalizedZ));
            }
        }

        internal readonly struct MapHoverTarget
        {
            internal MapHoverTarget(
                Rect hitbox,
                Vector2 anchor,
                string label,
                int priority,
                int mapPointIndex = -1)
            {
                Hitbox = hitbox;
                Anchor = anchor;
                Label = label ?? string.Empty;
                Priority = priority;
                MapPointIndex = mapPointIndex;
            }

            public Rect Hitbox { get; }
            public Vector2 Anchor { get; }
            public string Label { get; }
            public int Priority { get; }
            public int MapPointIndex { get; }
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
        private static readonly Color CemeteryLand =
            new Color32(66, 77, 65, 255);
        private static readonly Color YardLand =
            new Color32(94, 84, 63, 255);
        private static readonly Color ChurchLand =
            new Color32(102, 96, 84, 255);
        private static readonly Color WaterLand =
            new Color32(35, 91, 119, 255);
        private static readonly Color PierTimber =
            new Color32(141, 116, 84, 255);
        private static readonly Color BoatHut =
            new Color32(163, 134, 92, 255);
        // The seacoast landmarks: pale concrete for the mol, the
        // lighthouse's navigation white, rust for the stranded barge.
        private static readonly Color MolConcrete =
            new Color32(132, 133, 126, 255);
        private static readonly Color LighthouseLight =
            new Color32(242, 230, 199, 255);
        private static readonly Color BargeRust =
            new Color32(112, 66, 47, 255);
        private static readonly Color CemeteryMarker =
            new Color32(176, 178, 166, 255);
        private static readonly Color BeachSand =
            new Color32(178, 158, 111, 255);
        private static readonly Color YardTexture =
            new Color32(126, 113, 86, 255);
        private static readonly Color ChurchMarker =
            new Color32(219, 204, 164, 255);
        private static readonly Color AreaGate =
            new Color32(226, 178, 96, 255);
        private static readonly Color RiverWater =
            new Color32(26, 77, 103, 255);
        private static readonly Color RiverPromenade =
            new Color32(112, 106, 91, 255);
        private static readonly Color MountainToe =
            new Color32(112, 108, 96, 255);
        private static readonly Color MountainOuter =
            new Color32(72, 75, 70, 255);
        private static readonly Color MountainHatch =
            new Color32(90, 91, 82, 255);
        private static readonly Color MountainTunnelThroat =
            new Color32(25, 28, 27, 255);
        private static readonly Color MountainRiverCaveMouth =
            new Color32(20, 24, 24, 255);
        private static readonly Color MountainTunnelFrame =
            new Color32(178, 139, 72, 255);
        private static readonly Color WorksBridge =
            new Color32(105, 116, 121, 255);
        private static readonly Color TimberBridge =
            new Color32(161, 111, 67, 255);
        private static readonly Color MouthBridge =
            new Color32(150, 133, 109, 255);
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

        // The even lattice the point inspector rules over the whole chart,
        // and the scrim that puts out the squares nothing can stand in.
        //
        // Marking the DEAD squares rather than the live ones is deliberate:
        // every square of the city is a destination, so tinting the live
        // ones there would be a wash over the whole chart that says nothing.
        // The mountain road keeps about two squares in five, and there the
        // scrim is the whole answer.
        private static readonly Color TeleportLatticeLine =
            new Color32(151, 161, 148, 48);
        private static readonly Color TeleportDeadSquareScrim =
            new Color32(12, 14, 17, 92);

        internal static Color BusRouteColor => BusRoute;

        // Every marker plus one background target per visible area cell.
        // Reused across IMGUI events: identical number strings and a
        // tooltip GUIContent were otherwise re-allocated every pass for
        // as long as the map stays open.
        private readonly GUIContent tooltipContent = new GUIContent();
        private string[] numberLabelCache;

        private readonly List<MapHoverTarget> hoverTargets =
            new List<MapHoverTarget>(320);
        private readonly CityMapViewport mapViewport =
            new CityMapViewport();

        private CityMapController controller;
        private bool wasOpen;
        private int lastMapPointFocusRevision = -1;
        private bool isPointerPanning;
        private int pointerPanButton = -1;
        private Vector2 previousPanPointer;
        private Vector2 hoverCoordinateOffset;
        private Rect hoverClipRect;
        // What the bus legend covers: the map under it is hidden, so
        // a pointer resting on the legend names nothing.
        private Rect hoverBlockRect;
        private bool isMapLineContextActive;
        private Rect mapLineClipRect;
        private Vector2 mapLineGroupOffset;
        // Kept from the map pass so the tooltip, drawn after the group has
        // closed, can still say which square the pointer rests on.
        private MapProjection lastInspectionProjection;
        private bool hasInspectionProjection;
        private GUIStyle titleStyle;
        private GUIStyle subtitleStyle;
        private GUIStyle centeredStyle;
        private GUIStyle routeItemStyle;
        private GUIStyle markerButtonStyle;
        private GUIStyle routeBadgeStyle;
        private GUIStyle hintStyle;
        private GUIStyle smallButtonStyle;
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

                if (controller.AreaTabsConfigured)
                {
                    DrawAreaTabs(panel);
                }

                const float outerMargin = 11f;
                float headerHeight = controller.AreaTabsConfigured
                    ? 55f
                    : 33f;
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
                    controller.ActiveDisplayWorldXZBounds,
                    controller.ActiveMapReferenceWorldSize,
                    mapArea.size,
                    MinimumMapCellPixels);
                if (!wasOpen ||
                    lastPresentedArea != controller.SelectedArea)
                {
                    mapViewport.CenterOnWorld(
                        controller.ShouldDrawPlayerOnSelectedArea
                            ? controller.PlayerWorldPosition
                            : controller.GetSelectedAreaTravelTargetPosition(),
                        controller.ActiveDisplayWorldXZBounds);
                    wasOpen = true;
                    lastPresentedArea = controller.SelectedArea;
                }

                if (controller.MapPointInspectionEnabled &&
                    lastMapPointFocusRevision !=
                    controller.MapPointFocusRevision &&
                    controller.TryGetSelectedMapPoint(
                        out _,
                        out Vector3 selectedPointPosition))
                {
                    mapViewport.CenterOnWorld(
                        selectedPointPosition,
                        controller.ActiveDisplayWorldXZBounds);
                }

                lastMapPointFocusRevision =
                    controller.MapPointFocusRevision;

                HandlePointerScrolling(mapArea, logicalPointer);
                hoverTargets.Clear();
                hasInspectionProjection = false;
                hoverBlockRect = Rect.zero;
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
            if (controller.SelectedArea == GameAreaId.MountainRoad)
            {
                DrawMountainRoadMap(projection);
                DrawMapPointInspectionPass(projection);
                return;
            }

            RetroUiTheme.DrawPanel(
                projection.ScreenRect,
                MapVoid,
                RetroUiTheme.BorderMuted,
                true,
                2f,
                1f);
            DrawSurfaces(projection);
            DrawAreaGrounds(projection);
            DrawRiver(projection);
            DrawMountainBoundary(projection);
            DrawBuildings(projection);
            DrawRoads(projection);
            DrawRiverBridges(projection);
            DrawAreaOutlines(projection);
            DrawMountainTunnel(projection);
            DrawSeacoastLandmarks(projection);
            DrawBusRoute(projection);
            DrawRoute(projection);
            DrawBusStops(projection);
            DrawPointsOfInterest(projection);
            DrawSupermarket(projection);
            DrawBars(projection);
            DrawPlayerHome(projection);
            DrawPlayer(projection);
            DrawMountainTunnelMarker(projection);
            DrawBusLegend();
            DrawAreaSelectionPass(projection);
            DrawMapPointInspectionPass(projection);
        }

        /// <summary>
        /// Makes the open precincts clickable in test-teleport mode. The
        /// lake, the cemetery, the beach and the yards carry no building
        /// lot, so nothing else on the map offers them a hit box.
        ///
        /// Issued dead last on purpose: IMGUI gives a press to the first
        /// control that claims it, so every lot, bar, stop and landmark
        /// button above has already had its chance and a full-cell button
        /// can never swallow their clicks.
        /// </summary>
        private void DrawAreaSelectionPass(MapProjection projection)
        {
            if (!controller.IsCityMapInteractionActive ||
                !controller.DebugTeleportEnabled)
            {
                return;
            }

            IReadOnlyList<CityMapAreaTarget> targets =
                controller.MapAreaTargets;
            for (int index = 0; index < targets.Count; index++)
            {
                CityMapAreaTarget target = targets[index];
                bool selected = target.SelectionIndex ==
                                controller.SelectedMapObjectIndex;
                DrawAreaSelectionCells(
                    projection,
                    target.Region.LandBounds,
                    target.SelectionIndex);
                DrawAreaSelectionCells(
                    projection,
                    target.Region.WaterBounds,
                    target.SelectionIndex);
                if (!selected)
                {
                    continue;
                }

                // The same acknowledgement a selected lot gets, drawn on
                // the outline the precinct already has rather than as a
                // box around every one of its cells.
                IReadOnlyList<CityMapAreaEdge> outline =
                    target.Region.Outline;
                for (int edge = 0; edge < outline.Count; edge++)
                {
                    DrawLine(
                        projection.WorldToScreen(new Vector3(
                            outline[edge].Start.x,
                            0f,
                            outline[edge].Start.y)),
                        projection.WorldToScreen(new Vector3(
                            outline[edge].End.x,
                            0f,
                            outline[edge].End.y)),
                        2f,
                        RetroUiTheme.AccentPale);
                }
            }
        }

        private void DrawAreaSelectionCells(
            MapProjection projection,
            IReadOnlyList<Rect> bounds,
            int selectionIndex)
        {
            for (int index = 0; index < bounds.Count; index++)
            {
                // The same rect the hover layer registers, so what the map
                // names under the pointer is what the pointer can click.
                Rect cell = ProjectWorldRect(projection, bounds[index]);
                if (GUI.Button(cell, GUIContent.none, GUIStyle.none))
                {
                    controller.QueueSelectMapObject(selectionIndex);
                }
            }
        }

        private void DrawMapPointInspectionPass(MapProjection projection)
        {
            if (!controller.MapPointInspectionEnabled)
            {
                return;
            }

            lastInspectionProjection = projection;
            hasInspectionProjection = true;
            DrawTeleportLattice(projection);

            IReadOnlyList<CityMapPointDescriptor> points =
                controller.ActiveMapPoints;
            int selectedIndex = controller.SelectedMapPointIndex;
            for (int index = 0; index < points.Count; index++)
            {
                CityMapPointDescriptor point = points[index];

                // A ground square is the layer UNDER the markers, so it
                // registers no hover target of its own. Registering one
                // would put a whole square into the same distance-first
                // contest a bar marker wins by being small and near, and a
                // square is neither. The pointer finds it arithmetically
                // instead, once everything named has missed.
                if (point.Kind == CityMapPointKind.GroundSquare)
                {
                    if (index == selectedIndex)
                    {
                        DrawSelectedTeleportSquare(projection, point);
                    }

                    continue;
                }

                Vector3 worldPosition =
                    controller.ResolveMapPointWorldPosition(point);
                Vector2 anchor = projection.WorldToScreen(worldPosition);
                Rect hitbox;
                if (point.UsesWorldHitBounds)
                {
                    hitbox = ProjectWorldRect(
                        projection,
                        point.WorldXZHitBounds);
                    hitbox.width = Mathf.Max(4f, hitbox.width);
                    hitbox.height = Mathf.Max(4f, hitbox.height);
                }
                else
                {
                    Vector2 size = point.ScreenHitSize;
                    hitbox = CreateCenteredRect(
                        anchor,
                        Mathf.Max(11f, size.x),
                        Mathf.Max(11f, size.y));
                }

                // The city tunnel can sit beyond the authored city chart.
                // Match its clamped visible marker for picking while keeping
                // the descriptor's real portal coordinate in the card.
                if (point.Area == GameAreaId.City &&
                    point.Kind == CityMapPointKind.Tunnel)
                {
                    CityMountainTunnelDescriptor tunnel =
                        controller.MountainBoundaryPlan.Tunnel;
                    Vector3 displayPosition =
                        tunnel.PortalGroundCenter +
                        FlattenMountainAxis(tunnel.Axis) *
                        (tunnel.MapDisplayDepth * 0.45f);
                    anchor = projection.WorldToScreen(displayPosition);
                    hitbox = CreateMountainTunnelMarkerRect(
                        anchor,
                        mapLineClipRect);
                    anchor = hitbox.center;
                }

                RegisterHoverTarget(
                    hitbox,
                    anchor,
                    point.Label,
                    point.Priority,
                    index);
                if (index == selectedIndex)
                {
                    if (point.UsesWorldHitBounds)
                    {
                        RetroUiTheme.StrokeRect(
                            hitbox,
                            2f,
                            RetroUiTheme.AccentPale);
                    }

                    DrawOpenOctagonOutline(
                        anchor,
                        11f,
                        2f,
                        RetroUiTheme.AccentPale);
                }
            }

            Event current = Event.current;
            Vector2 globalPointer =
                current.mousePosition + hoverCoordinateOffset;
            bool clicked = GUI.Button(
                mapLineClipRect,
                GUIContent.none,
                GUIStyle.none);
            if (!clicked || hoverBlockRect.Contains(globalPointer))
            {
                return;
            }

            int pointIndex = ResolveMapPointIndex(
                hoverTargets,
                globalPointer);
            if (pointIndex < 0 &&
                TryResolveTeleportSquarePoint(
                    projection,
                    current.mousePosition,
                    out int squarePointIndex))
            {
                pointIndex = squarePointIndex;
            }

            if (pointIndex >= 0)
            {
                // No recentre on a pointer pick: what was clicked is already
                // under the hand, and pulling the chart out from under it on
                // every click makes choosing a square into chasing one.
                controller.QueueSelectMapPoint(pointIndex, false);
            }
        }

        /// <summary>
        /// The even lattice of squares the point inspector rules over the
        /// whole chart, so that every part of the map is a place and not
        /// only the parts something happens to stand on.
        ///
        /// Lines are ruled across the tab whatever the tab is. The scrim
        /// belongs to the squares the area's own ground refused, and the tab
        /// the player is not standing in gets none of it, because reaching
        /// that scene is a transition rather than a teleport.
        /// </summary>
        private void DrawTeleportLattice(MapProjection projection)
        {
            float cellSize = controller.ActiveTeleportCellSize;
            if (cellSize < CityMapTeleportLatticeBuilder.MinimumCellSize)
            {
                return;
            }

            Rect chart = controller.ActiveDisplayWorldXZBounds;
            Vector2 firstCorner = projection.ScreenToWorld(
                new Vector2(mapLineClipRect.xMin, mapLineClipRect.yMax));
            Vector2 secondCorner = projection.ScreenToWorld(
                new Vector2(mapLineClipRect.xMax, mapLineClipRect.yMin));
            Rect visible = Intersect(
                Rect.MinMaxRect(
                    Mathf.Min(firstCorner.x, secondCorner.x),
                    Mathf.Min(firstCorner.y, secondCorner.y),
                    Mathf.Max(firstCorner.x, secondCorner.x),
                    Mathf.Max(firstCorner.y, secondCorner.y)),
                chart);
            if (visible.width <= 0f || visible.height <= 0f)
            {
                return;
            }

            DrawDeadTeleportSquares(projection, visible);
            Vector2 anchor = controller.ActiveTeleportOriginAnchor;
            DrawTeleportLatticeLines(
                projection,
                visible,
                anchor.x,
                cellSize,
                true);
            DrawTeleportLatticeLines(
                projection,
                visible,
                anchor.y,
                cellSize,
                false);
        }

        /// <summary>
        /// Puts out the squares of the current area that no arrival fits in.
        /// The tab the player is not standing in has no lattice at all, and
        /// gets no scrim - an unbuilt chart is not the same claim as a chart
        /// of dead ground.
        /// </summary>
        private void DrawDeadTeleportSquares(
            MapProjection projection,
            Rect visible)
        {
            CityMapTeleportLattice lattice =
                controller.ActiveTeleportLattice;
            if (lattice.IsEmpty)
            {
                return;
            }

            Vector2Int first = lattice.GetCell(visible.min);
            Vector2Int last = lattice.GetCell(visible.max);
            first = Vector2Int.Max(first, lattice.MinimumCell);
            last = Vector2Int.Min(last, lattice.MaximumCell);
            if (last.x - first.x > MaximumLatticeLinesPerAxis ||
                last.y - first.y > MaximumLatticeLinesPerAxis)
            {
                return;
            }

            for (int cellZ = first.y; cellZ <= last.y; cellZ++)
            {
                for (int cellX = first.x; cellX <= last.x; cellX++)
                {
                    var cell = new Vector2Int(cellX, cellZ);
                    if (lattice.TryGetSquareIndex(cell, out _))
                    {
                        continue;
                    }

                    DrawSolidRect(
                        ProjectWorldRect(
                            projection,
                            lattice.GetCellWorldBounds(cell)),
                        TeleportDeadSquareScrim);
                }
            }
        }

        private void DrawTeleportLatticeLines(
            MapProjection projection,
            Rect visible,
            float anchor,
            float cellSize,
            bool vertical)
        {
            float minimum = vertical ? visible.xMin : visible.yMin;
            float maximum = vertical ? visible.xMax : visible.yMax;
            int first = Mathf.CeilToInt((minimum - anchor) / cellSize);
            int last = Mathf.FloorToInt((maximum - anchor) / cellSize);
            if (last - first > MaximumLatticeLinesPerAxis)
            {
                return;
            }

            for (int index = first; index <= last; index++)
            {
                float coordinate = anchor + index * cellSize;
                Vector3 start = vertical
                    ? new Vector3(coordinate, 0f, visible.yMin)
                    : new Vector3(visible.xMin, 0f, coordinate);
                Vector3 end = vertical
                    ? new Vector3(coordinate, 0f, visible.yMax)
                    : new Vector3(visible.xMax, 0f, coordinate);
                DrawLine(
                    projection.WorldToScreen(start),
                    projection.WorldToScreen(end),
                    1f,
                    TeleportLatticeLine);
            }
        }

        private void DrawSelectedTeleportSquare(
            MapProjection projection,
            CityMapPointDescriptor point)
        {
            Rect box = ProjectWorldRect(
                projection,
                point.WorldXZHitBounds);
            RetroUiTheme.StrokeRect(box, 2f, RetroUiTheme.AccentPale);
            DrawOpenOctagonOutline(
                projection.WorldToScreen(point.WorldPosition),
                7f,
                2f,
                RetroUiTheme.AccentPale);
        }

        private bool TryResolveTeleportSquarePoint(
            MapProjection projection,
            Vector2 localPointer,
            out int pointIndex)
        {
            pointIndex = -1;
            return projection.ScreenRect.Contains(localPointer) &&
                   controller.TryGetTeleportSquarePointIndex(
                       projection.ScreenToWorld(localPointer),
                       out pointIndex);
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

        private void DrawRiver(MapProjection projection)
        {
            CityRiverPlan river = controller.Layout.River;
            if (!river.IsEnabled)
            {
                return;
            }

            for (int index = 0; index < river.Segments.Count; index++)
            {
                DrawSolidRect(
                    ProjectWorldRect(
                        projection,
                        river.Segments[index].WaterBounds),
                    RiverWater);
            }

            for (int index = 0; index < river.Promenades.Count; index++)
            {
                Rect promenade = ProjectWorldRect(
                    projection,
                    river.Promenades[index].Bounds);
                DrawSolidRect(promenade, RiverPromenade);
                RetroUiTheme.StrokeRect(
                    promenade,
                    1f,
                    RetroUiTheme.WithAlpha(RetroUiTheme.Ink, 0.72f));
            }
        }

        private void DrawMountainBoundary(MapProjection projection)
        {
            CityMountainBoundaryPlan plan =
                controller.MountainBoundaryPlan;
            if (plan == null || !plan.IsEnabled)
            {
                return;
            }

            if (plan.HasRiverCave)
            {
                DrawRiverCaveApproach(projection, plan.RiverCave);
            }

            for (int index = 0; index < plan.Ridges.Count; index++)
            {
                DrawMountainRidge(projection, plan.Ridges[index]);
            }

            if (plan.HasRiverCave)
            {
                DrawRiverCaveMouth(projection, plan.RiverCave);
            }
        }

        private void DrawRiverCaveApproach(
            MapProjection projection,
            CityMountainRiverNotchDescriptor cave)
        {
            DrawRiverCaveBank(projection, cave.WestBankBounds);
            DrawRiverCaveBank(projection, cave.EastBankBounds);
            DrawSolidRect(
                ProjectWorldRect(projection, cave.WaterApproachBounds),
                RiverWater);
        }

        private void DrawRiverCaveBank(
            MapProjection projection,
            Rect worldBounds)
        {
            Rect bank = ProjectWorldRect(projection, worldBounds);
            DrawSolidRect(bank, RiverPromenade);
            RetroUiTheme.StrokeRect(
                bank,
                1f,
                RetroUiTheme.WithAlpha(RetroUiTheme.Ink, 0.72f));
        }

        private void DrawRiverCaveMouth(
            MapProjection projection,
            CityMountainRiverNotchDescriptor cave)
        {
            Rect mouth = ProjectWorldRect(projection, cave.MouthBounds);
            DrawSolidRect(mouth, MountainRiverCaveMouth);
            RetroUiTheme.StrokeRect(
                mouth,
                2f,
                RetroUiTheme.Ink);
            float hatchDepth = Mathf.Min(5f, mouth.height);
            float hatchRun = Mathf.Min(4f, mouth.width * 0.18f);
            for (int index = 1; index <= 3; index++)
            {
                float x = Mathf.Lerp(
                    mouth.xMin,
                    mouth.xMax,
                    index * 0.25f);
                DrawLine(
                    new Vector2(x, mouth.yMin),
                    new Vector2(x - hatchRun, mouth.yMin + hatchDepth),
                    1f,
                    MountainHatch);
            }
        }

        private void DrawMountainRidge(
            MapProjection projection,
            CityMountainRidgeDescriptor ridge)
        {
            IReadOnlyList<CityMountainRidgeStation> stations =
                ridge.Stations;
            for (int index = 1; index < stations.Count; index++)
            {
                CityMountainRidgeStation first = stations[index - 1];
                CityMountainRidgeStation second = stations[index];
                Vector2 firstToe = projection.WorldToScreen(first.Toe);
                Vector2 secondToe = projection.WorldToScreen(second.Toe);
                Vector2 firstOuter = projection.WorldToScreen(
                    first.OuterFoot);
                Vector2 secondOuter = projection.WorldToScreen(
                    second.OuterFoot);

                DrawLine(
                    firstOuter,
                    secondOuter,
                    3f,
                    RetroUiTheme.Ink);
                DrawLine(
                    firstOuter,
                    secondOuter,
                    1f,
                    MountainOuter);
                DrawLine(firstToe, secondOuter, 1f, MountainHatch);
                DrawLine(secondToe, firstOuter, 1f, MountainHatch);
                DrawLine(
                    firstToe,
                    secondToe,
                    4f,
                    RetroUiTheme.Ink);
                DrawLine(firstToe, secondToe, 2f, MountainToe);
            }
        }

        private void DrawMountainTunnel(MapProjection projection)
        {
            CityMountainBoundaryPlan plan =
                controller.MountainBoundaryPlan;
            if (plan == null || !plan.IsEnabled || !plan.HasTunnel)
            {
                return;
            }

            CityMountainTunnelDescriptor tunnel = plan.Tunnel;
            Rect throat = ProjectWorldRect(
                projection,
                CreateMountainTunnelThroatBounds(tunnel));
            DrawSolidRect(throat, MountainTunnelThroat);
            RetroUiTheme.StrokeRect(
                throat,
                1f,
                RetroUiTheme.Ink);
        }

        private void DrawMountainTunnelMarker(MapProjection projection)
        {
            CityMountainBoundaryPlan plan =
                controller.MountainBoundaryPlan;
            if (plan == null || !plan.IsEnabled || !plan.HasTunnel)
            {
                return;
            }

            CityMountainTunnelDescriptor tunnel = plan.Tunnel;
            Vector3 axis = FlattenMountainAxis(tunnel.Axis);
            Vector3 markerCenter = tunnel.PortalGroundCenter +
                                   axis * (tunnel.MapDisplayDepth * 0.45f);
            Vector2 projectedCenter =
                projection.WorldToScreen(markerCenter);
            Rect marker = CreateMountainTunnelMarkerRect(
                projectedCenter,
                mapLineClipRect);

            if (!mapLineClipRect.Contains(projectedCenter))
            {
                Vector2 direction = projectedCenter - marker.center;
                if (direction.sqrMagnitude > 0.01f)
                {
                    DrawLine(
                        marker.center,
                        marker.center + direction.normalized * 16f,
                        3f,
                        MountainTunnelFrame);
                }
            }

            DrawSolidRect(marker, RetroUiTheme.Ink);
            Rect mouth = new Rect(
                marker.x + 2f,
                marker.y + 2f,
                marker.width - 4f,
                marker.height - 4f);
            DrawSolidRect(mouth, MountainTunnelThroat);

            Vector2 leftBottom = new Vector2(
                mouth.x + 1f,
                mouth.yMax - 1f);
            Vector2 leftShoulder = new Vector2(
                mouth.x + 1f,
                mouth.y + 4f);
            Vector2 crown = new Vector2(
                mouth.center.x,
                mouth.y + 1f);
            Vector2 rightShoulder = new Vector2(
                mouth.xMax - 1f,
                mouth.y + 4f);
            Vector2 rightBottom = new Vector2(
                mouth.xMax - 1f,
                mouth.yMax - 1f);
            DrawLine(
                leftBottom,
                leftShoulder,
                2f,
                MountainTunnelFrame);
            DrawLine(
                leftShoulder,
                crown,
                2f,
                MountainTunnelFrame);
            DrawLine(
                crown,
                rightShoulder,
                2f,
                MountainTunnelFrame);
            DrawLine(
                rightShoulder,
                rightBottom,
                2f,
                MountainTunnelFrame);

            RegisterHoverTarget(
                Rect.MinMaxRect(
                    marker.xMin - 2f,
                    marker.yMin - 2f,
                    marker.xMax + 2f,
                    marker.yMax + 2f),
                marker.center,
                LocalizationService.Get(
                    "map.mountain.tunnel"),
                LandmarkHoverPriority);
        }

        internal static Rect CreateMountainTunnelMarkerRect(
            Vector2 projectedCenter,
            Rect viewport)
        {
            float halfWidth = MountainTunnelMarkerWidth * 0.5f;
            float halfHeight = MountainTunnelMarkerHeight * 0.5f;
            float centerX = viewport.width >=
                            MountainTunnelMarkerWidth +
                            MountainTunnelMarkerEdgePadding * 2f
                ? Mathf.Clamp(
                    projectedCenter.x,
                    viewport.xMin + halfWidth +
                    MountainTunnelMarkerEdgePadding,
                    viewport.xMax - halfWidth -
                    MountainTunnelMarkerEdgePadding)
                : viewport.center.x;
            float centerY = viewport.height >=
                            MountainTunnelMarkerHeight +
                            MountainTunnelMarkerEdgePadding * 2f
                ? Mathf.Clamp(
                    projectedCenter.y,
                    viewport.yMin + halfHeight +
                    MountainTunnelMarkerEdgePadding,
                    viewport.yMax - halfHeight -
                    MountainTunnelMarkerEdgePadding)
                : viewport.center.y;
            return CreateCenteredRect(
                new Vector2(centerX, centerY),
                MountainTunnelMarkerWidth,
                MountainTunnelMarkerHeight);
        }

        internal static Rect CreateMountainTunnelThroatBounds(
            CityMountainTunnelDescriptor tunnel)
        {
            Vector3 axis = FlattenMountainAxis(tunnel.Axis);
            Vector3 right = Vector3.Cross(Vector3.up, axis).normalized;
            float halfWidth = tunnel.OpeningWidth * 0.5f;
            Vector3 start = tunnel.PortalGroundCenter;
            Vector3 end = start + axis * tunnel.MapDisplayDepth;
            Vector3 startLeft = start - right * halfWidth;
            Vector3 startRight = start + right * halfWidth;
            Vector3 endLeft = end - right * halfWidth;
            Vector3 endRight = end + right * halfWidth;
            return Rect.MinMaxRect(
                Mathf.Min(
                    startLeft.x,
                    startRight.x,
                    endLeft.x,
                    endRight.x),
                Mathf.Min(
                    startLeft.z,
                    startRight.z,
                    endLeft.z,
                    endRight.z),
                Mathf.Max(
                    startLeft.x,
                    startRight.x,
                    endLeft.x,
                    endRight.x),
                Mathf.Max(
                    startLeft.z,
                    startRight.z,
                    endLeft.z,
                    endRight.z));
        }

        private static Vector3 FlattenMountainAxis(Vector3 direction)
        {
            direction.y = 0f;
            return direction.sqrMagnitude <= 0.0001f
                ? Vector3.back
                : direction.normalized;
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

        private void DrawRiverBridges(MapProjection projection)
        {
            CityRiverPlan river = controller.Layout.River;
            if (!river.IsEnabled)
            {
                return;
            }

            float worldWidth =
                projection.MaximumX - projection.MinimumX;
            float streetWidth = Mathf.Clamp(
                Mathf.Round(
                    controller.Layout.RoadWidth /
                    Mathf.Max(0.01f, worldWidth) *
                    projection.ScreenRect.width),
                2f,
                8f);
            for (int index = 0; index < river.Bridges.Count; index++)
            {
                CityRiverBridgeDescriptor bridge = river.Bridges[index];
                Rect bounds = bridge.DeckBounds;
                Vector2 west = projection.WorldToScreen(
                    new Vector3(bounds.xMin, 0f, bounds.center.y));
                Vector2 east = projection.WorldToScreen(
                    new Vector3(bounds.xMax, 0f, bounds.center.y));
                float width = GetRiverBridgeMapWidth(
                    bridge.Definition,
                    streetWidth);
                DrawLine(
                    west,
                    east,
                    width + 2f,
                    RetroUiTheme.Ink);
                DrawLine(
                    west,
                    east,
                    width,
                    GetRiverBridgeMapColor(bridge.Definition.Style));

                if (bridge.Definition.Style ==
                    CityBridgeStyle.TimberPark)
                {
                    DrawTimberBridgePlanks(west, east, width);
                }
            }
        }

        private void DrawTimberBridgePlanks(
            Vector2 west,
            Vector2 east,
            float width)
        {
            Vector2 across = east - west;
            Vector2 normal = new Vector2(-across.y, across.x).normalized;
            for (int index = 1; index <= 4; index++)
            {
                Vector2 center = Vector2.Lerp(
                    west,
                    east,
                    index / 5f);
                DrawLine(
                    center - normal * width * 0.36f,
                    center + normal * width * 0.36f,
                    1f,
                    RetroUiTheme.WithAlpha(RetroUiTheme.Ink, 0.72f));
            }
        }

        /// <summary>
        /// Draws what each precinct is made of, and registers its name as
        /// the background hover layer. Nothing here prints text: an area
        /// says what it is by how it is drawn, and gives its name only to
        /// a pointer resting on it.
        /// </summary>
        private void DrawAreaGrounds(MapProjection projection)
        {
            IReadOnlyList<CityMapAreaRegion> regions =
                controller.AreaRegions;
            for (int index = 0; index < regions.Count; index++)
            {
                CityMapAreaRegion region = regions[index];
                RegisterAreaHoverTargets(projection, region);
                if (region.IsUrban)
                {
                    continue;
                }

                DrawAreaTexture(projection, region);
            }
        }

        private void RegisterAreaHoverTargets(
            MapProjection projection,
            CityMapAreaRegion region)
        {
            string label = LocalizationService.Get(
                region.LocalizationKey);
            RegisterAreaHoverTargets(projection, region.LandBounds, label);
            RegisterAreaHoverTargets(projection, region.WaterBounds, label);
        }

        private void RegisterAreaHoverTargets(
            MapProjection projection,
            IReadOnlyList<Rect> bounds,
            string label)
        {
            for (int index = 0; index < bounds.Count; index++)
            {
                Rect cell = ProjectWorldRect(projection, bounds[index]);
                RegisterHoverTarget(
                    cell,
                    cell.center,
                    label,
                    AreaHoverPriority);
            }
        }

        /// <summary>
        /// The motif that tells one open precinct from another once the
        /// permanent labels are gone: grave crosses for the cemetery, a
        /// Latin cross for the Catholic church, sand for the beach and
        /// drying lines for a yard.
        /// </summary>
        private void DrawAreaTexture(
            MapProjection projection,
            CityMapAreaRegion region)
        {
            if (region.Feature == CityAreaFeatureKind.Church)
            {
                DrawLatinChurchCross(projection, region.LandBounds);
                return;
            }

            for (int index = 0;
                 index < region.LandBounds.Count;
                 index++)
            {
                Rect cell = ProjectWorldRect(
                    projection,
                    region.LandBounds[index]);
                if (cell.width < 6f || cell.height < 6f)
                {
                    continue;
                }

                switch (region.Feature)
                {
                    case CityAreaFeatureKind.Cemetery:
                        DrawCemeteryGraves(cell);
                        break;
                    case CityAreaFeatureKind.NorthWaterfront:
                        DrawBeachSand(cell);
                        break;
                    case CityAreaFeatureKind.Yard:
                        DrawYardLines(cell);
                        break;
                }
            }
        }

        /// <summary>
        /// One conventional Latin cross identifies the Catholic church as
        /// a precinct landmark without repeating a glyph in every cell.
        /// </summary>
        private void DrawLatinChurchCross(
            MapProjection projection,
            IReadOnlyList<Rect> landBounds)
        {
            if (landBounds.Count == 0)
            {
                return;
            }

            Rect markerBounds = ProjectWorldRect(
                projection,
                landBounds[0]);
            for (int index = 1; index < landBounds.Count; index++)
            {
                Rect cell = ProjectWorldRect(projection, landBounds[index]);
                markerBounds = Rect.MinMaxRect(
                    Mathf.Min(markerBounds.xMin, cell.xMin),
                    Mathf.Min(markerBounds.yMin, cell.yMin),
                    Mathf.Max(markerBounds.xMax, cell.xMax),
                    Mathf.Max(markerBounds.yMax, cell.yMax));
            }

            Vector2 center = markerBounds.center;
            float height = Mathf.Clamp(
                markerBounds.height * 0.52f,
                13f,
                25f);
            float stemWidth = Mathf.Max(2f, height * 0.10f);
            DrawSolidRect(
                new Rect(
                    center.x - stemWidth * 0.5f,
                    center.y - height * 0.5f,
                    stemWidth,
                    height),
                ChurchMarker);
            DrawSolidRect(
                new Rect(
                    center.x - height * 0.32f,
                    center.y - height * 0.20f,
                    height * 0.64f,
                    stemWidth),
                ChurchMarker);
        }

        private void DrawCemeteryGraves(Rect cell)
        {
            const int columns = 3;
            const int rows = 3;
            float stemHeight = Mathf.Min(
                4f,
                Mathf.Max(2f, cell.height / (rows * 2.4f)));
            for (int column = 0; column < columns; column++)
            {
                float x = Mathf.Round(
                    cell.x + cell.width * (column + 0.5f) / columns);
                for (int row = 0; row < rows; row++)
                {
                    float y = Mathf.Round(
                        cell.y + cell.height * (row + 0.5f) / rows);
                    DrawSolidRect(
                        new Rect(x, y - stemHeight * 0.5f, 1f, stemHeight),
                        CemeteryMarker);
                    DrawSolidRect(
                        new Rect(
                            x - 1f,
                            y - stemHeight * 0.5f + 1f,
                            3f,
                            1f),
                        CemeteryMarker);
                }
            }
        }

        private void DrawBeachSand(Rect cell)
        {
            for (int index = 0; index < 4; index++)
            {
                float x = Mathf.Round(
                    cell.x + cell.width * (index + 0.5f) / 4f);
                float y = Mathf.Round(
                    cell.y + cell.height * (index % 2 == 0 ? 0.34f : 0.66f));
                DrawSolidRect(new Rect(x, y, 2f, 1f), BeachSand);
            }
        }

        private void DrawYardLines(Rect cell)
        {
            float inset = Mathf.Min(3f, cell.width * 0.2f);
            for (int index = 0; index < 2; index++)
            {
                float y = Mathf.Round(
                    cell.y + cell.height * (index + 1f) / 3f);
                DrawSolidRect(
                    new Rect(
                        cell.x + inset,
                        y,
                        Mathf.Max(1f, cell.width - inset * 2f),
                        1f),
                    YardTexture);
            }
        }

        /// <summary>
        /// The edge of every non-urban precinct, plus the street openings
        /// that are the only way into it.
        /// </summary>
        private void DrawAreaOutlines(MapProjection projection)
        {
            IReadOnlyList<CityMapAreaRegion> regions =
                controller.AreaRegions;
            for (int index = 0; index < regions.Count; index++)
            {
                CityMapAreaRegion region = regions[index];
                if (region.IsUrban)
                {
                    continue;
                }

                Color outline = RetroUiTheme.WithAlpha(
                    Brighten(region.MapColor, 0.42f),
                    0.92f);
                for (int edge = 0;
                     edge < region.Outline.Count;
                     edge++)
                {
                    CityMapAreaEdge segment = region.Outline[edge];
                    DrawLine(
                        projection.WorldToScreen(
                            new Vector3(segment.Start.x, 0f, segment.Start.y)),
                        projection.WorldToScreen(
                            new Vector3(segment.End.x, 0f, segment.End.y)),
                        1f,
                        outline);
                }

                for (int gate = 0; gate < region.Gates.Count; gate++)
                {
                    DrawAreaGate(projection, region.Gates[gate]);
                }
            }
        }

        private void DrawAreaGate(
            MapProjection projection,
            Rect approach)
        {
            Rect gate = ProjectWorldRect(projection, approach);
            bool horizontal = gate.width >= gate.height;
            Vector2 start = horizontal
                ? new Vector2(gate.xMin, gate.center.y)
                : new Vector2(gate.center.x, gate.yMin);
            Vector2 end = horizontal
                ? new Vector2(gate.xMax, gate.center.y)
                : new Vector2(gate.center.x, gate.yMax);
            DrawLine(start, end, 3f, RetroUiTheme.Ink);
            DrawLine(start, end, 1f, AreaGate);
        }

        /// <summary>
        /// The seacoast's anchors, straight from the coast plan: the
        /// mol, the boat station's sea pier and hut, the mouth
        /// footbridge, the sea wall line, the rotten pile row and the
        /// stranded barge. Projected part unions over an ink backing,
        /// so the map and the world cannot disagree. The lighthouse
        /// island stands past the chart's north edge, so its dot pins
        /// to the border at the island's true easting — off the map,
        /// the way it is off the shore.
        /// </summary>
        private void DrawSeacoastLandmarks(MapProjection projection)
        {
            CitySeacoastPlan coast = controller.SeacoastPlan;
            if (coast == null)
            {
                return;
            }

            if (TryProjectSeacoastParts(
                    projection,
                    coast,
                    CitySeacoastPartKind.EsplanadeParapet,
                    out Rect seaWall))
            {
                DrawSolidRect(seaWall, MolConcrete);
            }

            if (TryProjectSeacoastParts(
                    projection,
                    coast,
                    CitySeacoastPartKind.FootbridgeDeck,
                    out Rect footbridge))
            {
                DrawSolidRect(Expand(footbridge, 1f), RetroUiTheme.Ink);
                DrawSolidRect(footbridge, PierTimber);
            }

            if (TryProjectSeacoastParts(
                    projection,
                    coast,
                    CitySeacoastPartKind.MolDeck,
                    out Rect mol))
            {
                DrawSolidRect(Expand(mol, 1f), RetroUiTheme.Ink);
                DrawSolidRect(mol, MolConcrete);
            }

            if (CityLighthouseIslandPlanner.TryResolveLanternPosition(
                    coast,
                    out Vector3 lantern))
            {
                Vector2 island = projection.WorldToScreen(new Vector3(
                    lantern.x,
                    0f,
                    lantern.z));
                DrawSolidRect(
                    new Rect(island.x - 2f, island.y - 2f, 4f, 4f),
                    RetroUiTheme.Ink);
                DrawSolidRect(
                    new Rect(island.x - 1f, island.y - 1f, 2f, 2f),
                    LighthouseLight);
            }

            for (int index = 0; index < coast.Parts.Count; index++)
            {
                CitySeacoastPartDescriptor part = coast.Parts[index];
                if (part.Kind != CitySeacoastPartKind.RottenPile)
                {
                    continue;
                }

                Vector2 pile = projection.WorldToScreen(new Vector3(
                    part.Center.x,
                    0f,
                    part.Center.z));
                DrawSolidRect(
                    new Rect(pile.x - 1f, pile.y - 1f, 2f, 2f),
                    RetroUiTheme.Ink);
            }

            if (TryProjectSeacoastParts(
                    projection,
                    coast,
                    CitySeacoastPartKind.Barge,
                    out Rect barge))
            {
                DrawSolidRect(Expand(barge, 1f), RetroUiTheme.Ink);
                DrawSolidRect(barge, BargeRust);
            }

            if (TryProjectSeacoastParts(
                    projection,
                    coast,
                    CitySeacoastPartKind.PierDeck,
                    out Rect pier))
            {
                DrawSolidRect(Expand(pier, 1f), RetroUiTheme.Ink);
                DrawSolidRect(pier, PierTimber);
            }

            if (!TryProjectSeacoastParts(
                    projection,
                    coast,
                    CitySeacoastPartKind.Hut,
                    out Rect hut))
            {
                return;
            }

            Rect marker = Expand(hut, 1f);
            DrawSolidRect(marker, RetroUiTheme.Ink);
            DrawSolidRect(hut, BoatHut);
            RegisterHoverTarget(
                Expand(marker, 3f),
                marker.center,
                LocalizationService.Get("map.seacoast.boat_station"),
                LandmarkHoverPriority);
        }

        private static bool TryProjectSeacoastParts(
            MapProjection projection,
            CitySeacoastPlan coast,
            CitySeacoastPartKind kind,
            out Rect screenRect)
        {
            float minimumX = float.PositiveInfinity;
            float maximumX = float.NegativeInfinity;
            float minimumZ = float.PositiveInfinity;
            float maximumZ = float.NegativeInfinity;
            for (int index = 0; index < coast.Parts.Count; index++)
            {
                CitySeacoastPartDescriptor part = coast.Parts[index];
                if (part.Kind != kind)
                {
                    continue;
                }

                Vector3 extents = part.Rotation * part.Size;
                float halfX = Mathf.Abs(extents.x) * 0.5f;
                float halfZ = Mathf.Abs(extents.z) * 0.5f;
                minimumX = Mathf.Min(minimumX, part.Center.x - halfX);
                maximumX = Mathf.Max(maximumX, part.Center.x + halfX);
                minimumZ = Mathf.Min(minimumZ, part.Center.z - halfZ);
                maximumZ = Mathf.Max(maximumZ, part.Center.z + halfZ);
            }

            if (minimumX > maximumX || minimumZ > maximumZ)
            {
                screenRect = default;
                return false;
            }

            Rect projected = ProjectWorldRect(
                projection,
                Rect.MinMaxRect(minimumX, minimumZ, maximumX, maximumZ));
            screenRect = new Rect(
                projected.x,
                projected.y,
                Mathf.Max(2f, projected.width),
                Mathf.Max(2f, projected.height));
            return true;
        }

        private static Rect Expand(Rect rectangle, float amount)
        {
            return new Rect(
                rectangle.x - amount,
                rectangle.y - amount,
                rectangle.width + amount * 2f,
                rectangle.height + amount * 2f);
        }

        private static Color Brighten(Color color, float amount)
        {
            return new Color(
                Mathf.Lerp(color.r, 1f, amount),
                Mathf.Lerp(color.g, 1f, amount),
                Mathf.Lerp(color.b, 1f, amount),
                color.a);
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
                    GetNumberLabel(stop.Ordinal));
            }
        }

        private string GetNumberLabel(int value)
        {
            if (value < 0)
            {
                return value.ToString();
            }

            if (numberLabelCache == null ||
                numberLabelCache.Length <= value)
            {
                System.Array.Resize(
                    ref numberLabelCache,
                    Mathf.Max(16, value + 1));
            }

            return numberLabelCache[value] ??= value.ToString();
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
            hoverBlockRect = new Rect(
                legend.position + hoverCoordinateOffset,
                legend.size);
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
                    controller.GetBarMapObjectIndex(index);
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
                string markerLabel = GetNumberLabel(index + 1);
                bool pressed = false;
                if (controller.MapPointInspectionEnabled)
                {
                    GUI.Label(marker, markerLabel, markerButtonStyle);
                }
                else
                {
                    pressed = GUI.Button(
                        marker,
                        markerLabel,
                        markerButtonStyle);
                }

                if (pressed)
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
            if (!controller.ShouldDrawPlayerOnSelectedArea)
            {
                return;
            }

            Vector2 position =
                projection.WorldToScreen(controller.PlayerWorldPosition);
            Vector3 forward = controller.PlayerForward;
            Vector2 screenForward = new Vector2(forward.x, -forward.z);
            if (screenForward.sqrMagnitude < 0.001f)
            {
                screenForward = Vector2.up;
            }

            screenForward.Normalize();
            // One solid arrowhead standing on the player's own position
            // and pointing where they face, outlined so it survives over
            // pale ground. The name it carries belongs to the tooltip.
            const float ahead = 8f;
            const float behind = 5f;
            const float halfBase = 5.5f;
            Vector2 tip = position + screenForward * ahead;
            Vector2 tail = position - screenForward * behind;
            FillArrowhead(
                tip + screenForward,
                tail - screenForward,
                halfBase + 1.4f,
                RetroUiTheme.Ink);
            FillArrowhead(tip, tail, halfBase, Player);
            RegisterHoverTarget(
                CreateCenteredRect(position, 17f, 17f),
                position,
                LocalizationService.Get("map.player"),
                PlayerHoverPriority);
        }

        /// <summary>
        /// Fills the triangle from <paramref name="tip"/> back to the base
        /// centred on <paramref name="tail"/>. IMGUI draws rectangles, so
        /// the triangle is laid down as rows that widen towards the base.
        /// </summary>
        private void FillArrowhead(
            Vector2 tip,
            Vector2 tail,
            float halfBase,
            Color color)
        {
            Vector2 axis = tail - tip;
            float length = axis.magnitude;
            if (length <= 0.01f)
            {
                return;
            }

            Vector2 direction = axis / length;
            Vector2 side = new Vector2(-direction.y, direction.x);
            int rows = Mathf.Clamp(Mathf.CeilToInt(length), 3, 24);
            float step = length / rows;
            for (int index = 0; index < rows; index++)
            {
                float across = (index + 0.5f) / rows;
                Vector2 center = tip + direction * (length * across);
                float half = Mathf.Max(0.5f, halfBase * across);
                DrawLine(
                    center - side * half,
                    center + side * half,
                    step + 1f,
                    color);
            }
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
        }

        private void RegisterHoverTarget(
            Rect hitbox,
            Vector2 anchor,
            string label,
            int priority,
            int mapPointIndex = -1)
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
                    priority,
                    mapPointIndex));
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
            if (hoverBlockRect.Contains(logicalPointer))
            {
                return;
            }

            string label = ResolveHoveredLabel(
                hoverTargets,
                logicalPointer);
            if (string.IsNullOrEmpty(label))
            {
                // Nothing named is here, so the ground answers - the same
                // last-resort square a click would pick.
                label = ResolveHoveredTeleportSquareLabel(logicalPointer);
            }

            if (string.IsNullOrEmpty(label))
            {
                return;
            }

            tooltipContent.text = label;
            GUIContent content = tooltipContent;
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

        private string ResolveHoveredTeleportSquareLabel(
            Vector2 logicalPointer)
        {
            if (!hasInspectionProjection ||
                !hoverClipRect.Contains(logicalPointer))
            {
                return string.Empty;
            }

            // The map draws inside a GUI group, so the projection speaks in
            // group-local coordinates while the tooltip runs after the group
            // has closed.
            if (!TryResolveTeleportSquarePoint(
                    lastInspectionProjection,
                    logicalPointer - hoverClipRect.position,
                    out int pointIndex))
            {
                return string.Empty;
            }

            IReadOnlyList<CityMapPointDescriptor> points =
                controller.ActiveMapPoints;
            return pointIndex < points.Count
                ? points[pointIndex].Label
                : string.Empty;
        }

        internal static string ResolveHoveredLabel(
            IReadOnlyList<MapHoverTarget> targets,
            Vector2 pointer)
        {
            if (targets == null)
            {
                return string.Empty;
            }

            // Markers first, and only then the ground they stand on: a
            // precinct covers whole cells, so on distance alone it would
            // outbid the very markers it lies under.
            string label = ResolveHoveredLabel(
                targets,
                pointer,
                ForegroundHoverPriorityFloor,
                int.MaxValue);
            return string.IsNullOrEmpty(label)
                ? ResolveHoveredLabel(
                    targets,
                    pointer,
                    int.MinValue,
                    ForegroundHoverPriorityFloor - 1)
                : label;
        }

        internal static int ResolveMapPointIndex(
            IReadOnlyList<MapHoverTarget> targets,
            Vector2 pointer)
        {
            if (targets == null)
            {
                return -1;
            }

            int index = ResolveMapPointIndex(
                targets,
                pointer,
                ForegroundHoverPriorityFloor,
                int.MaxValue);
            return index >= 0
                ? index
                : ResolveMapPointIndex(
                    targets,
                    pointer,
                    int.MinValue,
                    ForegroundHoverPriorityFloor - 1);
        }

        private static int ResolveMapPointIndex(
            IReadOnlyList<MapHoverTarget> targets,
            Vector2 pointer,
            int minimumPriority,
            int maximumPriority)
        {
            int bestIndex = -1;
            float bestDistance = float.PositiveInfinity;
            int bestPriority = int.MinValue;
            for (int index = 0; index < targets.Count; index++)
            {
                MapHoverTarget target = targets[index];
                if (target.MapPointIndex < 0 ||
                    target.Priority < minimumPriority ||
                    target.Priority > maximumPriority ||
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
                    bestIndex = target.MapPointIndex;
                    bestDistance = distance;
                    bestPriority = target.Priority;
                }
            }

            return bestIndex;
        }

        private static string ResolveHoveredLabel(
            IReadOnlyList<MapHoverTarget> targets,
            Vector2 pointer,
            int minimumPriority,
            int maximumPriority)
        {
            string bestLabel = string.Empty;
            float bestDistance = float.PositiveInfinity;
            int bestPriority = int.MinValue;
            for (int index = 0; index < targets.Count; index++)
            {
                MapHoverTarget target = targets[index];
                if (target.Priority < minimumPriority ||
                    target.Priority > maximumPriority ||
                    string.IsNullOrEmpty(target.Label) ||
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
            if (controller.MapPointInspectionEnabled)
            {
                DrawMapPointPanel(panel);
                DrawMapPointModeButton(panel);
                return;
            }

            if (!controller.IsCityMapInteractionActive)
            {
                DrawAreaTravelPanel(panel);
                DrawMapPointModeButton(panel);
                return;
            }

            if (controller.DebugTeleportEnabled)
            {
                DrawDebugTeleportPanel(panel);
                DrawMapPointModeButton(panel);
                return;
            }

            GUI.Label(
                new Rect(
                    panel.x + 6f,
                    panel.y + 5f,
                    panel.width - 58f,
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
            DrawMapPointModeButton(panel);
        }

        private void DrawMapPointPanel(Rect panel)
        {
            GUI.Label(
                new Rect(
                    panel.x + 6f,
                    panel.y + 5f,
                    panel.width - 58f,
                    18f),
                LocalizationService.Get("map.point.title"),
                subtitleStyle);

            GUI.Label(
                new Rect(
                    panel.x + 8f,
                    panel.y + 27f,
                    panel.width - 16f,
                    16f),
                controller.GetAreaLabel(controller.SelectedArea),
                pointOfInterestItemStyle);

            if (!controller.TryGetSelectedMapPoint(
                    out CityMapPointDescriptor point,
                    out Vector3 worldPosition))
            {
                GUI.Label(
                    new Rect(
                        panel.x + 10f,
                        panel.y + 67f,
                        panel.width - 20f,
                        56f),
                    LocalizationService.Get("map.point.select"),
                    centeredStyle);
            }
            else
            {
                GUI.Label(
                    new Rect(
                        panel.x + 9f,
                        panel.y + 50f,
                        panel.width - 18f,
                        42f),
                    point.Label,
                    centeredStyle);
                GUI.Label(
                    new Rect(
                        panel.x + 9f,
                        panel.y + 101f,
                        panel.width - 18f,
                        48f),
                    FormatMapPointCoordinates(worldPosition),
                    centeredStyle);
                GUI.Label(
                    new Rect(
                        panel.x + 9f,
                        panel.y + 154f,
                        panel.width - 18f,
                        16f),
                    string.Format(
                        CultureInfo.InvariantCulture,
                        "{0} / {1}",
                        controller.SelectedMapPointIndex + 1,
                        controller.ActiveMapPoints.Count),
                    pointOfInterestItemStyle);

                DrawMapPointTeleportButton(panel, point);
            }

            GUI.Label(
                new Rect(
                    panel.x + 8f,
                    panel.yMax - 43f,
                    panel.width - 16f,
                    35f),
                LocalizationService.Get("map.point.select_hint"),
                centeredStyle);
        }

        /// <summary>
        /// Go to the point that is selected, not to the middle of the region
        /// that contains it.
        ///
        /// The button belongs to the inspector itself now rather than to
        /// debug mode. It was hidden outright unless the F9 window had been
        /// used to arm the teleport - a switch in another window, and in the
        /// mountain-road scene one that did not exist at all - so the mode
        /// read as broken: a point, its coordinates, and nothing to press.
        ///
        /// It still only reaches the tab the player is standing in. The
        /// other tab charts somewhere else, and getting there is a scene
        /// transition that the area travel button already owns.
        /// </summary>
        private void DrawMapPointTeleportButton(
            Rect panel,
            CityMapPointDescriptor point)
        {
            var button = new Rect(
                panel.x + 18f,
                panel.y + 176f,
                panel.width - 36f,
                24f);
            bool reachable = controller.CanTeleportToSelectedMapPoint;
            bool travelable = !reachable &&
                              controller.CanTravelToSelectedMapPoint;
            RetroUiTheme.DrawPanel(
                button,
                RetroUiTheme.PanelRaised,
                reachable || travelable
                    ? RetroUiTheme.Good
                    : RetroUiTheme.BorderMuted,
                reachable || travelable,
                2f,
                1f);
            if (travelable)
            {
                // The other tab is a scene that is not loaded, so this is a
                // transition and not a teleport - which the panel used to
                // report and then stop at. It is still a trip the map can
                // start, and the coordinate rides along.
                if (GUI.Button(
                        button,
                        LocalizationService.Get("map.point.travel"),
                        hintStyle))
                {
                    controller.QueueConfirmMapPointTravel();
                }

                return;
            }

            if (!reachable)
            {
                GUI.Label(
                    button,
                    LocalizationService.Get("map.point.teleport_elsewhere"),
                    hintStyle);
                return;
            }

            if (GUI.Button(
                    button,
                    LocalizationService.Get("map.point.teleport"),
                    hintStyle))
            {
                controller.QueueConfirmMapPointTeleport();
            }
        }

        private void DrawMapPointModeButton(Rect panel)
        {
            Rect button = new Rect(
                panel.xMax - 45f,
                panel.y + 4f,
                39f,
                19f);
            RetroUiTheme.DrawPanel(
                button,
                controller.MapPointInspectionEnabled
                    ? RetroUiTheme.Accent
                    : RetroUiTheme.PanelRaised,
                controller.MapPointInspectionEnabled
                    ? RetroUiTheme.AccentPale
                    : RetroUiTheme.BorderMuted,
                controller.MapPointInspectionEnabled,
                1f,
                1f);
            if (GUI.Button(button, "XYZ", smallButtonStyle))
            {
                controller.QueueToggleMapPointInspection();
            }
        }

        /// <summary>
        /// The two coordinates a map actually has.
        ///
        /// The readout used to print all three, which is a debug dump rather
        /// than a chart: height is the one number a plan view cannot show,
        /// nobody navigates by it, and on a city whose ground is graded
        /// everywhere it is noise beside the two that locate the point.
        /// </summary>
        internal static string FormatMapPointCoordinates(Vector3 position)
        {
            return string.Format(
                CultureInfo.InvariantCulture,
                LocalizationService.Get("map.point.coordinates"),
                NormalizeMapCoordinate(position.x),
                NormalizeMapCoordinate(position.z));
        }

        private static float NormalizeMapCoordinate(float value)
        {
            return Mathf.Abs(value) < 0.05f ? 0f : value;
        }

        private void DrawDebugTeleportPanel(Rect panel)
        {
            GUI.Label(
                new Rect(
                    panel.x + 6f,
                    panel.y + 5f,
                    panel.width - 58f,
                    18f),
                LocalizationService.Get("map.teleport.title"),
                subtitleStyle);

            if (controller.SelectedMapObjectIndex < 0)
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
            Rect bounds = controller.ActiveDisplayWorldXZBounds;
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

        private static Rect ProjectWorldRect(
            MapProjection projection,
            Rect worldBounds)
        {
            Vector2 topLeft = projection.WorldToScreen(
                new Vector3(
                    worldBounds.xMin,
                    0f,
                    worldBounds.yMax));
            Vector2 bottomRight = projection.WorldToScreen(
                new Vector3(
                    worldBounds.xMax,
                    0f,
                    worldBounds.yMin));
            return Rect.MinMaxRect(
                topLeft.x,
                topLeft.y,
                bottomRight.x,
                bottomRight.y);
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
                case CityDistrictKind.Cemetery:
                    return CemeteryLand;
                case CityDistrictKind.Yard:
                    return YardLand;
                case CityDistrictKind.Church:
                    return ChurchLand;
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
                case CitySurfaceKind.RiverWater:
                    return RiverWater;
                case CitySurfaceKind.Beach:
                    return WaterfrontLand;
                case CitySurfaceKind.CemeteryGround:
                    return CemeteryLand;
                case CitySurfaceKind.OpenGround:
                    return YardLand;
                case CitySurfaceKind.ChurchGround:
                    return ChurchLand;
                default:
                    return surface.MapColor;
            }
        }

        private static Color GetRiverBridgeMapColor(
            CityBridgeStyle style)
        {
            switch (style)
            {
                case CityBridgeStyle.Works:
                    return WorksBridge;
                case CityBridgeStyle.TimberPark:
                    return TimberBridge;
                case CityBridgeStyle.Mouth:
                    return MouthBridge;
                default:
                    throw new ArgumentOutOfRangeException(
                        nameof(style),
                        style,
                        "Unsupported river bridge style.");
            }
        }

        private static float GetRiverBridgeMapWidth(
            CityBridgeDefinition bridge,
            float streetWidth)
        {
            return bridge.Role == CityBridgeRole.ParkFootbridge
                ? Mathf.Max(2f, streetWidth * 0.42f)
                : streetWidth;
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
                case CityDistrictKind.Cemetery:
                    return "map.district.cemetery";
                case CityDistrictKind.Yard:
                    return "map.district.yard";
                case CityDistrictKind.Church:
                    return "map.district.church";
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

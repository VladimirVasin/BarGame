using System;
using System.Collections.Generic;
using UnityEngine;

namespace BarPromenade
{
    [DisallowMultipleComponent]
    public sealed class CityMapView : MonoBehaviour
    {
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

        private static readonly Color Backdrop =
            RetroUiTheme.WithAlpha(RetroUiTheme.Backdrop, 0.96f);
        private static readonly Color MapGround =
            RetroUiTheme.MapGround;
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
        private static readonly Color UnselectedBar =
            RetroUiTheme.MapBar;
        private static readonly Color VisitedBar =
            RetroUiTheme.Good;
        private static readonly Color Player =
            RetroUiTheme.Cyan;
        private static readonly Color PlayerHome =
            RetroUiTheme.AccentPale;

        private CityMapController controller;
        private GUIStyle titleStyle;
        private GUIStyle subtitleStyle;
        private GUIStyle centeredStyle;
        private GUIStyle routeItemStyle;
        private GUIStyle markerButtonStyle;
        private GUIStyle routeBadgeStyle;
        private GUIStyle hintStyle;
        private GUIStyle smallButtonStyle;
        private GUIStyle districtLabelStyle;

        public void Initialize(CityMapController mapController)
        {
            controller = mapController;
        }

        private void OnGUI()
        {
            if (controller == null || !controller.IsInitialized)
            {
                return;
            }

            EnsureStyles();
            GUI.depth = -90;
            RetroUiCanvas canvas = RetroUiTheme.CalculateCanvas(
                Screen.width,
                Screen.height);

            if (!controller.IsOpen)
            {
                Matrix4x4 hintMatrix =
                    RetroUiTheme.BeginCanvas(canvas);
                try
                {
                    DrawOpenHint();
                }
                finally
                {
                    RetroUiTheme.EndCanvas(hintMatrix);
                }

                return;
            }

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
                const float footerHeight = 27f;
                float routePanelWidth = Mathf.Clamp(
                    panel.width * 0.28f,
                    130f,
                    170f);
                Rect content = new Rect(
                    panel.x + outerMargin,
                    panel.y + headerHeight,
                    panel.width - outerMargin * 2f,
                    panel.height - headerHeight - footerHeight);
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

                MapProjection projection = CreateProjection(mapArea);
                DrawMap(projection);
                DrawRoutePanel(routePanel);

                GUI.Label(
                    new Rect(
                        panel.x + 10f,
                        panel.yMax - footerHeight + 4f,
                        panel.width - 20f,
                        16f),
                    LocalizationService.Get("map.instructions"),
                    centeredStyle);
            }
            finally
            {
                RetroUiTheme.EndCanvas(previousMatrix);
            }
        }

        private void DrawOpenHint()
        {
            const float width = 87f;
            const float height = 21f;
            Rect hint = new Rect(
                RetroUiTheme.LogicalWidth - width - 10f,
                9f,
                width,
                height);
            RetroUiTheme.DrawPanel(
                hint,
                RetroUiTheme.PanelRaised,
                RetroUiTheme.Accent,
                true,
                2f,
                1f);
            if (GUI.Button(
                hint,
                LocalizationService.Get("map.open_hint"),
                hintStyle))
            {
                controller.QueueToggleMap();
            }
        }

        private void DrawMap(MapProjection projection)
        {
            RetroUiTheme.DrawPanel(
                projection.ScreenRect,
                MapGround,
                RetroUiTheme.BorderMuted,
                true,
                2f,
                1f);
            DrawBuildings(projection);
            DrawRoads(projection);
            DrawDistrictLabels(projection);
            DrawRoute(projection);
            DrawBars(projection);
            DrawPlayerHome(projection);
            DrawPlayer(projection);
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
                DrawSolidRect(
                    buildingRect,
                    GetLotColor(lot));
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
            IReadOnlyList<CityDistrictDescriptor> districts =
                controller.Layout.Districts;
            for (int index = 0; index < districts.Count; index++)
            {
                CityDistrictDescriptor district = districts[index];
                Vector2 position = projection.WorldToScreen(
                    district.CenterWorldPosition);
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
                        GetDistrictColor(district.Kind),
                        0.92f));
                GUI.Label(
                    labelRect,
                    LocalizationService.Get(
                        GetDistrictLocalizationKey(district.Kind)),
                    districtLabelStyle);
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

        private void DrawBars(MapProjection projection)
        {
            for (int index = 0; index < controller.Bars.Count; index++)
            {
                BuildingLot bar = controller.Bars[index];
                Vector2 position =
                    projection.WorldToScreen(bar.ReturnPosition);
                int routeOrder = controller.GetRouteOrder(bar.BarId);
                bool selected = routeOrder >= 0;
                bool visited = controller.IsBarVisited(bar.BarId);
                bool focused = index == controller.SelectedBarIndex;
                const float markerSize = 17f;
                Rect marker = new Rect(
                    position.x - markerSize * 0.5f,
                    position.y - markerSize * 0.5f,
                    markerSize,
                    markerSize);

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

                DrawSolidRect(
                    marker,
                    visited ? VisitedBar : UnselectedBar);

                Color previousContentColor = GUI.contentColor;
                GUI.contentColor = visited
                    ? RetroUiTheme.Ink
                    : RetroUiTheme.Text;
                string markerLabel = (index + 1).ToString();
                if (GUI.Button(marker, markerLabel, markerButtonStyle))
                {
                    controller.QueueToggleBar(index);
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

        private void DrawRoutePanel(Rect panel)
        {
            RetroUiTheme.DrawPanel(
                panel,
                RetroUiTheme.PanelInset,
                RetroUiTheme.BorderMuted,
                true,
                2f,
                1f);
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

            CityRoutePath path = controller.CurrentPath;
            float distance = path == null ? 0f : path.TotalLength;
            Rect visitedSwatch = new Rect(
                panel.x + 8f,
                panel.yMax - 59f,
                8f,
                8f);
            DrawSolidRect(visitedSwatch, VisitedBar);
            GUI.Label(
                new Rect(
                    visitedSwatch.xMax + 5f,
                    panel.yMax - 62f,
                    panel.width - 27f,
                    14f),
                string.Format(
                    LocalizationService.Get("map.visited_count"),
                    controller.VisitedBarCount,
                    controller.Bars.Count),
                routeItemStyle);
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

        private MapProjection CreateProjection(Rect available)
        {
            CityLayout layout = controller.Layout;
            Vector3 minimum =
                layout.GetNodeWorldPosition(Vector2Int.zero);
            Vector3 maximum =
                layout.GetNodeWorldPosition(layout.BlockCount);
            float padding = layout.RoadWidth * 0.75f;
            float minimumX = minimum.x - padding;
            float maximumX = maximum.x + padding;
            float minimumZ = minimum.z - padding;
            float maximumZ = maximum.z + padding;
            float worldWidth = maximumX - minimumX;
            float worldHeight = maximumZ - minimumZ;
            float worldAspect =
                worldWidth / Mathf.Max(0.01f, worldHeight);
            float screenAspect =
                available.width / Mathf.Max(0.01f, available.height);

            Rect mapRect;
            if (screenAspect > worldAspect)
            {
                float width = available.height * worldAspect;
                mapRect = new Rect(
                    available.center.x - width * 0.5f,
                    available.y,
                    width,
                    available.height);
            }
            else
            {
                float height = available.width / worldAspect;
                mapRect = new Rect(
                    available.x,
                    available.center.y - height * 0.5f,
                    available.width,
                    height);
            }

            return new MapProjection(
                mapRect,
                minimumX,
                maximumX,
                minimumZ,
                maximumZ);
        }

        private static void DrawLine(
            Vector2 start,
            Vector2 end,
            float width,
            Color color)
        {
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
                end);
            GUI.DrawTexture(
                new Rect(0f, -width * 0.5f, length, width),
                Texture2D.whiteTexture);
            GUI.matrix = previousMatrix;
            GUI.color = previousColor;
        }

        private static Matrix4x4 CreateLineMatrix(
            Matrix4x4 parentMatrix,
            Vector2 start,
            Vector2 end)
        {
            Vector2 direction = (end - start).normalized;
            var logicalLineTransform = Matrix4x4.identity;
            logicalLineTransform.m00 = direction.x;
            logicalLineTransform.m01 = -direction.y;
            logicalLineTransform.m03 = start.x;
            logicalLineTransform.m10 = direction.y;
            logicalLineTransform.m11 = direction.x;
            logicalLineTransform.m13 = start.y;
            return parentMatrix * logicalLineTransform;
        }

        private static void DrawSolidRect(Rect rectangle, Color color)
        {
            RetroUiTheme.FillRect(rectangle, color);
        }

        private static Color GetLotColor(BuildingLot lot)
        {
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
                default:
                    return Building;
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
        }
    }
}

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
            new Color(0.015f, 0.012f, 0.017f, 0.94f);
        private static readonly Color MapGround =
            new Color(0.11f, 0.12f, 0.13f);
        private static readonly Color Building =
            new Color(0.25f, 0.25f, 0.27f);
        private static readonly Color BarBuilding =
            new Color(0.43f, 0.16f, 0.08f);
        private static readonly Color Road =
            new Color(0.34f, 0.36f, 0.39f);
        private static readonly Color Route =
            new Color(1f, 0.67f, 0.16f);
        private static readonly Color UnselectedBar =
            new Color(0.55f, 0.17f, 0.08f);
        private static readonly Color SelectedBar =
            new Color(1f, 0.67f, 0.16f);
        private static readonly Color Player =
            new Color(0.20f, 0.86f, 0.94f);

        private CityMapController controller;
        private GUIStyle titleStyle;
        private GUIStyle subtitleStyle;
        private GUIStyle centeredStyle;
        private GUIStyle routeItemStyle;
        private GUIStyle markerButtonStyle;
        private GUIStyle hintStyle;
        private GUIStyle smallButtonStyle;

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

            if (!controller.IsOpen)
            {
                DrawOpenHint();
                return;
            }

            DrawSolidRect(
                new Rect(0f, 0f, Screen.width, Screen.height),
                Backdrop);

            float panelWidth = Mathf.Min(1240f, Screen.width - 32f);
            float panelHeight = Mathf.Min(760f, Screen.height - 32f);
            Rect panel = new Rect(
                (Screen.width - panelWidth) * 0.5f,
                (Screen.height - panelHeight) * 0.5f,
                panelWidth,
                panelHeight);
            GUI.Box(panel, GUIContent.none);

            GUI.Label(
                new Rect(
                    panel.x + 24f,
                    panel.y + 14f,
                    panel.width - 48f,
                    44f),
                LocalizationService.Get("map.title"),
                titleStyle);

            const float outerMargin = 22f;
            const float headerHeight = 66f;
            const float footerHeight = 54f;
            float routePanelWidth = Mathf.Clamp(
                panel.width * 0.28f,
                260f,
                340f);
            Rect content = new Rect(
                panel.x + outerMargin,
                panel.y + headerHeight,
                panel.width - outerMargin * 2f,
                panel.height - headerHeight - footerHeight);
            Rect mapArea = new Rect(
                content.x,
                content.y,
                content.width - routePanelWidth - 18f,
                content.height);
            Rect routePanel = new Rect(
                mapArea.xMax + 18f,
                content.y,
                routePanelWidth,
                content.height);

            MapProjection projection = CreateProjection(mapArea);
            DrawMap(projection);
            DrawRoutePanel(routePanel);

            GUI.Label(
                new Rect(
                    panel.x + 20f,
                    panel.yMax - footerHeight + 8f,
                    panel.width - 40f,
                    32f),
                LocalizationService.Get("map.instructions"),
                centeredStyle);
        }

        private void DrawOpenHint()
        {
            const float width = 174f;
            const float height = 42f;
            Rect hint = new Rect(
                Screen.width - width - 20f,
                18f,
                width,
                height);
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
            DrawSolidRect(projection.ScreenRect, MapGround);
            DrawBuildings(projection);
            DrawRoads(projection);
            DrawRoute(projection);
            DrawBars(projection);
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
                    lot.IsBar ? BarBuilding : Building);
            }
        }

        private void DrawRoads(MapProjection projection)
        {
            float worldWidth =
                projection.MaximumX - projection.MinimumX;
            float roadWidth = Mathf.Clamp(
                controller.Layout.RoadWidth /
                Mathf.Max(0.01f, worldWidth) *
                projection.ScreenRect.width,
                3f,
                16f);

            IReadOnlyList<RoadEdge> roads =
                controller.Layout.RoadEdges;
            for (int index = 0; index < roads.Count; index++)
            {
                RoadEdge edge = roads[index];
                DrawLine(
                    projection.WorldToScreen(
                        controller.Layout.GetNodeWorldPosition(edge.A)),
                    projection.WorldToScreen(
                        controller.Layout.GetNodeWorldPosition(edge.B)),
                    roadWidth,
                    Road);
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
                    6f,
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
                bool focused = index == controller.SelectedBarIndex;
                const float markerSize = 34f;
                Rect marker = new Rect(
                    position.x - markerSize * 0.5f,
                    position.y - markerSize * 0.5f,
                    markerSize,
                    markerSize);

                if (focused)
                {
                    DrawSolidRect(
                        new Rect(
                            marker.x - 4f,
                            marker.y - 4f,
                            marker.width + 8f,
                            marker.height + 8f),
                        Color.white);
                }

                DrawSolidRect(
                    marker,
                    selected ? SelectedBar : UnselectedBar);

                Color previousContentColor = GUI.contentColor;
                GUI.contentColor = selected
                    ? new Color(0.12f, 0.07f, 0.03f)
                    : Color.white;
                string markerLabel = selected
                    ? (routeOrder + 1).ToString()
                    : (index + 1).ToString();
                if (GUI.Button(marker, markerLabel, markerButtonStyle))
                {
                    controller.QueueToggleBar(index);
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
            DrawLine(
                position,
                position + screenForward * 18f,
                5f,
                Player);
            DrawSolidRect(
                new Rect(position.x - 6f, position.y - 6f, 12f, 12f),
                Player);
            GUI.Label(
                new Rect(position.x + 10f, position.y - 13f, 110f, 26f),
                LocalizationService.Get("map.player"),
                routeItemStyle);
        }

        private void DrawRoutePanel(Rect panel)
        {
            GUI.Box(panel, GUIContent.none);
            GUI.Label(
                new Rect(panel.x + 12f, panel.y + 10f, panel.width - 24f, 36f),
                LocalizationService.Get("map.route_title"),
                subtitleStyle);

            IReadOnlyList<string> route = controller.Route;
            if (route.Count == 0)
            {
                GUI.Label(
                    new Rect(
                        panel.x + 18f,
                        panel.y + 64f,
                        panel.width - 36f,
                        84f),
                    LocalizationService.Get("map.route_empty"),
                    centeredStyle);
            }
            else
            {
                DrawRouteRows(panel, route);
            }

            CityRoutePath path = controller.CurrentPath;
            float distance = path == null ? 0f : path.TotalLength;
            GUI.Label(
                new Rect(
                    panel.x + 14f,
                    panel.yMax - 82f,
                    panel.width - 28f,
                    28f),
                string.Format(
                    LocalizationService.Get("map.distance"),
                    distance),
                centeredStyle);

            Rect clearButton = new Rect(
                panel.x + 16f,
                panel.yMax - 48f,
                panel.width - 32f,
                32f);
            bool previousEnabled = GUI.enabled;
            GUI.enabled = route.Count > 0;
            if (GUI.Button(
                clearButton,
                LocalizationService.Get("map.clear")))
            {
                controller.QueueClearRoute();
            }

            GUI.enabled = previousEnabled;
        }

        private void DrawRouteRows(
            Rect panel,
            IReadOnlyList<string> route)
        {
            const float rowHeight = 44f;
            const float rowGap = 8f;
            float rowY = panel.y + 58f;

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
                    panel.x + 12f,
                    rowY,
                    panel.width - 24f,
                    rowHeight);
                bool focused =
                    barIndex == controller.SelectedBarIndex;
                DrawSolidRect(
                    row,
                    focused
                        ? new Color(0.35f, 0.25f, 0.12f)
                        : new Color(0.17f, 0.17f, 0.19f));

                const float buttonWidth = 30f;
                const float buttonGap = 4f;
                float buttonsWidth = buttonWidth * 3f + buttonGap * 2f;
                GUI.Label(
                    new Rect(
                        row.x + 10f,
                        row.y,
                        row.width - buttonsWidth - 18f,
                        row.height),
                    $"{routeIndex + 1}. {controller.GetBarLabel(barIndex)}",
                    routeItemStyle);

                float buttonX = row.xMax - buttonsWidth - 6f;
                bool previousEnabled = GUI.enabled;
                GUI.enabled = routeIndex > 0;
                if (GUI.Button(
                    new Rect(buttonX, row.y + 7f, buttonWidth, 30f),
                    "\u25B2",
                    smallButtonStyle))
                {
                    controller.QueueMoveBar(barId, -1);
                }

                buttonX += buttonWidth + buttonGap;
                GUI.enabled = routeIndex < route.Count - 1;
                if (GUI.Button(
                    new Rect(buttonX, row.y + 7f, buttonWidth, 30f),
                    "\u25BC",
                    smallButtonStyle))
                {
                    controller.QueueMoveBar(barId, 1);
                }

                buttonX += buttonWidth + buttonGap;
                GUI.enabled = true;
                if (GUI.Button(
                    new Rect(buttonX, row.y + 7f, buttonWidth, 30f),
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
            GUIUtility.RotateAroundPivot(
                Mathf.Atan2(delta.y, delta.x) * Mathf.Rad2Deg,
                start);
            GUI.DrawTexture(
                new Rect(start.x, start.y - width * 0.5f, length, width),
                Texture2D.whiteTexture);
            GUI.matrix = previousMatrix;
            GUI.color = previousColor;
        }

        private static void DrawSolidRect(Rect rectangle, Color color)
        {
            Color previousColor = GUI.color;
            GUI.color = color;
            GUI.DrawTexture(rectangle, Texture2D.whiteTexture);
            GUI.color = previousColor;
        }

        private void EnsureStyles()
        {
            if (titleStyle != null)
            {
                return;
            }

            titleStyle = new GUIStyle(GUI.skin.label)
            {
                alignment = TextAnchor.MiddleCenter,
                fontSize = 30,
                fontStyle = FontStyle.Bold,
                normal = { textColor = Route }
            };
            subtitleStyle = new GUIStyle(GUI.skin.label)
            {
                alignment = TextAnchor.MiddleCenter,
                fontSize = 22,
                fontStyle = FontStyle.Bold,
                normal = { textColor = Route }
            };
            centeredStyle = new GUIStyle(GUI.skin.label)
            {
                alignment = TextAnchor.MiddleCenter,
                fontSize = 17,
                wordWrap = true,
                normal = { textColor = Color.white }
            };
            routeItemStyle = new GUIStyle(GUI.skin.label)
            {
                alignment = TextAnchor.MiddleLeft,
                fontSize = 16,
                fontStyle = FontStyle.Bold,
                normal = { textColor = Color.white }
            };
            markerButtonStyle = new GUIStyle(GUI.skin.button)
            {
                alignment = TextAnchor.MiddleCenter,
                fontSize = 18,
                fontStyle = FontStyle.Bold
            };
            markerButtonStyle.normal.background = null;
            markerButtonStyle.hover.background = null;
            markerButtonStyle.active.background = null;
            markerButtonStyle.focused.background = null;
            hintStyle = new GUIStyle(GUI.skin.button)
            {
                alignment = TextAnchor.MiddleCenter,
                fontSize = 18,
                fontStyle = FontStyle.Bold,
                normal = { textColor = Color.white }
            };
            smallButtonStyle = new GUIStyle(GUI.skin.button)
            {
                alignment = TextAnchor.MiddleCenter,
                fontSize = 17,
                fontStyle = FontStyle.Bold
            };
        }
    }
}

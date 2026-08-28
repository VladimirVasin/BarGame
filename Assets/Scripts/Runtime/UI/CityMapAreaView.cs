using System.Collections.Generic;
using UnityEngine;

namespace BarPromenade
{
    public sealed partial class CityMapView
    {
        private static readonly Color MountainRoadForest =
            new Color32(36, 51, 45, 255);
        private static readonly Color MountainRoadInk =
            new Color32(164, 157, 128, 255);
        private static readonly Color MountainRoadPlateau =
            new Color32(103, 99, 82, 255);
        private static readonly Color MountainRoadSnow =
            new Color32(174, 183, 179, 255);
        private static readonly Color MountainRoadHairpin =
            new Color32(211, 163, 89, 255);
        private static readonly Color MountainRoadBridge =
            new Color32(190, 207, 201, 255);
        private static readonly Color MountainRoadCafeGlow =
            new Color32(226, 199, 116, 255);
        private static readonly Color MountainRoadCableway =
            new Color32(125, 179, 171, 255);

        // The village's three tones are warm where every other tab is cold.
        // That is the whole difference the chart is allowed to state.
        private static readonly Color AlpineVillageGround =
            new Color32(74, 68, 58, 255);
        private static readonly Color AlpineVillageSettled =
            new Color32(104, 94, 78, 255);
        private static readonly Color AlpineVillageLane =
            new Color32(214, 186, 138, 255);

        private GameAreaId lastPresentedArea = GameAreaId.City;

        private void DrawAreaTabs(Rect panel)
        {
            IReadOnlyList<GameAreaId> areas = controller.AreaTabs;
            for (int index = 0; index < areas.Count; index++)
            {
                GameAreaId area = areas[index];
                Rect tab = CreateAreaTabRect(panel, index, areas.Count);
                bool selected = area == controller.SelectedArea;
                RetroUiTheme.DrawPanel(
                    tab,
                    selected
                        ? RetroUiTheme.PanelRaised
                        : RetroUiTheme.PanelInset,
                    selected
                        ? RetroUiTheme.Accent
                        : RetroUiTheme.BorderMuted,
                    selected,
                    1f,
                    1f);
                string currentPrefix = area == controller.CurrentArea
                    ? "\u25C6 "
                    : string.Empty;
                if (GUI.Button(
                    tab,
                    currentPrefix + controller.GetAreaLabel(area),
                    smallButtonStyle))
                {
                    controller.QueueSelectArea(area);
                }
            }

            GUI.Label(
                new Rect(
                    panel.xMax - 83f,
                    panel.y + 31f,
                    69f,
                    17f),
                "TAB   LT / RT",
                pointOfInterestItemStyle);
        }

        internal static Rect CreateAreaTabRect(
            Rect panel,
            int index,
            int count)
        {
            const float tabWidth = 112f;
            const float tabHeight = 18f;
            const float gap = 4f;
            int safeCount = Mathf.Max(1, count);
            int safeIndex = Mathf.Clamp(index, 0, safeCount - 1);
            float totalWidth = safeCount * tabWidth +
                               (safeCount - 1) * gap;
            float left = panel.x + 12f;
            if (totalWidth > panel.width - 112f)
            {
                float available = Mathf.Max(
                    40f,
                    panel.width - 112f - gap * (safeCount - 1));
                float fittedWidth = available / safeCount;
                return new Rect(
                    left + safeIndex * (fittedWidth + gap),
                    panel.y + 31f,
                    fittedWidth,
                    tabHeight);
            }

            return new Rect(
                left + safeIndex * (tabWidth + gap),
                panel.y + 31f,
                tabWidth,
                tabHeight);
        }

        /// <summary>
        /// The village tab. Same primitives as the mountain road because it
        /// is the same kind of chart - a line and a patch of ground - but on
        /// a warmer field, which is the one thing the map says about the
        /// place. The buildings are not drawn: the points carry them, and a
        /// village drawn plan-view stops being a village and becomes a plan.
        /// </summary>
        private void DrawAlpineVillageMap(MapProjection projection)
        {
            RetroUiTheme.DrawPanel(
                projection.ScreenRect,
                AlpineVillageGround,
                RetroUiTheme.BorderMuted,
                true,
                2f,
                1f);
            CityMapMountainRoadOverlay overlay =
                controller.AlpineVillageOverlay;
            if (overlay.IsEmpty)
            {
                return;
            }

            DrawMountainRoadHatches(projection, overlay);
            Rect extent = ProjectWorldRect(
                projection,
                overlay.PlateauBounds);
            DrawSolidRect(extent, AlpineVillageSettled);
            RetroUiTheme.StrokeRect(extent, 1f, RetroUiTheme.BorderMuted);

            IReadOnlyList<Vector3> lane = overlay.RoutePoints;
            for (int index = 1; index < lane.Count; index++)
            {
                DrawLine(
                    projection.WorldToScreen(lane[index - 1]),
                    projection.WorldToScreen(lane[index]),
                    4f,
                    AlpineVillageLane);
            }

            DrawPlayer(projection);
        }

        private void DrawMountainRoadMap(MapProjection projection)
        {
            RetroUiTheme.DrawPanel(
                projection.ScreenRect,
                MountainRoadForest,
                RetroUiTheme.BorderMuted,
                true,
                2f,
                1f);
            CityMapMountainRoadOverlay overlay =
                controller.MountainRoadOverlay;
            if (overlay.IsEmpty)
            {
                return;
            }

            DrawMountainRoadHatches(projection, overlay);
            DrawMountainRoadPlateau(projection, overlay);
            DrawMountainRoadRoute(projection, overlay);
            DrawMountainRoadBridge(projection, overlay);
            DrawMountainRoadHairpins(projection, overlay);
            DrawMountainRoadTerminalLandmarks(projection, overlay);
            DrawMountainRoadTunnel(projection, overlay);
            DrawPlayer(projection);
        }

        private void DrawMountainRoadHatches(
            MapProjection projection,
            CityMapMountainRoadOverlay overlay)
        {
            IReadOnlyList<CityMapMountainHatchSegment> hatches =
                overlay.MountainHatches;
            for (int index = 0; index < hatches.Count; index++)
            {
                DrawLine(
                    projection.WorldToScreen(hatches[index].Start),
                    projection.WorldToScreen(hatches[index].End),
                    1f,
                    MountainRoadSnow);
            }
        }

        private void DrawMountainRoadRoute(
            MapProjection projection,
            CityMapMountainRoadOverlay overlay)
        {
            IReadOnlyList<Vector3> route = overlay.RoutePoints;
            for (int index = 1; index < route.Count; index++)
            {
                Vector2 start = projection.WorldToScreen(route[index - 1]);
                Vector2 end = projection.WorldToScreen(route[index]);
                DrawLine(start, end, 8f, RetroUiTheme.Ink);
                DrawLine(start, end, 4f, MountainRoadInk);
            }
        }

        private void DrawMountainRoadPlateau(
            MapProjection projection,
            CityMapMountainRoadOverlay overlay)
        {
            Rect plateau = ProjectWorldRect(
                projection,
                overlay.PlateauBounds);
            DrawSolidRect(plateau, MountainRoadPlateau);
            RetroUiTheme.StrokeRect(
                plateau,
                2f,
                MountainRoadSnow);

            Vector2 center = projection.WorldToScreen(
                overlay.EndpointPosition);
            const float peakHalfWidth = 9f;
            const float peakHeight = 10f;
            for (int index = -1; index <= 1; index++)
            {
                float x = center.x + index * 12f;
                Vector2 left = new Vector2(x - peakHalfWidth, center.y - 9f);
                Vector2 crown = new Vector2(x, center.y - 9f - peakHeight);
                Vector2 right = new Vector2(x + peakHalfWidth, center.y - 9f);
                DrawLine(left, crown, 2f, MountainRoadSnow);
                DrawLine(crown, right, 2f, MountainRoadSnow);
            }

            RegisterHoverTarget(
                Expand(plateau, 3f),
                plateau.center,
                LocalizationService.Get("map.mountain_road.plateau"),
                LandmarkHoverPriority);
        }

        private void DrawMountainRoadHairpins(
            MapProjection projection,
            CityMapMountainRoadOverlay overlay)
        {
            IReadOnlyList<Vector3> hairpins = overlay.HairpinPositions;
            for (int index = 0; index < hairpins.Count; index++)
            {
                Vector2 center = projection.WorldToScreen(hairpins[index]);
                DrawOpenOctagonOutline(
                    center,
                    6f,
                    1f,
                    MountainRoadHairpin);
                RegisterHoverTarget(
                    CreateCenteredRect(center, 15f, 15f),
                    center,
                    string.Format(
                        LocalizationService.Get(
                            "map.mountain_road.hairpin"),
                        index + 1),
                    LandmarkHoverPriority);
            }
        }

        private void DrawMountainRoadBridge(
            MapProjection projection,
            CityMapMountainRoadOverlay overlay)
        {
            if (!overlay.HasBridge)
            {
                return;
            }

            Vector2 center = projection.WorldToScreen(
                overlay.BridgePosition);
            Rect marker = CreateCenteredRect(center, 24f, 16f);
            DrawSolidRect(
                new Rect(marker.x + 1f, marker.y + 5f, marker.width - 2f, 6f),
                RetroUiTheme.Ink);
            DrawSolidRect(
                new Rect(marker.x + 3f, marker.y + 7f, marker.width - 6f, 2f),
                MountainRoadBridge);
            DrawLine(
                new Vector2(marker.x + 4f, marker.y + 2f),
                new Vector2(marker.x + 4f, marker.yMax - 2f),
                2f,
                MountainRoadBridge);
            DrawLine(
                new Vector2(marker.xMax - 4f, marker.y + 2f),
                new Vector2(marker.xMax - 4f, marker.yMax - 2f),
                2f,
                MountainRoadBridge);
            DrawLine(
                new Vector2(marker.x + 4f, marker.y + 3f),
                new Vector2(marker.xMax - 4f, marker.y + 3f),
                1f,
                MountainRoadBridge);
            DrawLine(
                new Vector2(marker.x + 4f, marker.yMax - 3f),
                new Vector2(marker.xMax - 4f, marker.yMax - 3f),
                1f,
                MountainRoadBridge);

            RegisterHoverTarget(
                Expand(marker, 3f),
                center,
                LocalizationService.Get("map.mountain_road.bridge"),
                LandmarkHoverPriority);
        }

        private void DrawMountainRoadTerminalLandmarks(
            MapProjection projection,
            CityMapMountainRoadOverlay overlay)
        {
            IReadOnlyList<MountainRoadTerminalLandmark> landmarks =
                overlay.TerminalLandmarks;
            for (int index = 0; index < landmarks.Count; index++)
            {
                MountainRoadTerminalLandmark landmark = landmarks[index];
                Vector2 center = projection.WorldToScreen(
                    landmark.Position);
                Rect marker = CreateCenteredRect(center, 18f, 18f);
                switch (landmark.Kind)
                {
                    case MountainRoadTerminalLandmarkKind.Cafe:
                        DrawMountainRoadCafeMarker(marker);
                        break;
                    case MountainRoadTerminalLandmarkKind.Cableway:
                        DrawMountainRoadCablewayMarker(marker);
                        break;
                }

                RegisterHoverTarget(
                    Expand(marker, 3f),
                    center,
                    LocalizationService.Get(landmark.LocalizationKey),
                    LandmarkHoverPriority);
            }
        }

        private void DrawMountainRoadCafeMarker(Rect marker)
        {
            Rect body = new Rect(
                marker.x + 2f,
                marker.y + 6f,
                marker.width - 4f,
                marker.height - 8f);
            DrawSolidRect(body, RetroUiTheme.Ink);
            DrawSolidRect(
                new Rect(
                    body.x + 2f,
                    body.y + 2f,
                    body.width - 4f,
                    body.height - 4f),
                MountainRoadCafeGlow);
            DrawLine(
                new Vector2(marker.x + 1f, marker.y + 6f),
                new Vector2(marker.xMax - 1f, marker.y + 6f),
                2f,
                MountainRoadCafeGlow);
            DrawLine(
                new Vector2(marker.center.x, body.y + 2f),
                new Vector2(marker.center.x, body.yMax - 2f),
                1f,
                RetroUiTheme.Ink);
        }

        private void DrawMountainRoadCablewayMarker(Rect marker)
        {
            Vector2 leftCable = new Vector2(
                marker.x + 1f,
                marker.y + 4f);
            Vector2 rightCable = new Vector2(
                marker.xMax - 1f,
                marker.y + 1f);
            DrawLine(
                leftCable,
                rightCable,
                2f,
                MountainRoadCableway);
            DrawLine(
                new Vector2(marker.center.x, marker.y + 3f),
                new Vector2(marker.center.x, marker.yMax - 2f),
                2f,
                RetroUiTheme.Ink);
            DrawLine(
                new Vector2(marker.x + 4f, marker.y + 6f),
                new Vector2(marker.xMax - 4f, marker.y + 6f),
                2f,
                MountainRoadCableway);

            Rect cabin = new Rect(
                marker.xMax - 7f,
                marker.y + 6f,
                6f,
                6f);
            DrawSolidRect(cabin, RetroUiTheme.Ink);
            DrawSolidRect(
                new Rect(
                    cabin.x + 1f,
                    cabin.y + 1f,
                    cabin.width - 2f,
                    cabin.height - 2f),
                MountainRoadCableway);
        }

        private void DrawMountainRoadTunnel(
            MapProjection projection,
            CityMapMountainRoadOverlay overlay)
        {
            Vector2 center = projection.WorldToScreen(
                overlay.TunnelPosition);
            Rect marker = CreateCenteredRect(center, 20f, 18f);
            Vector2 leftBottom = new Vector2(marker.x + 3f, marker.yMax - 2f);
            Vector2 leftShoulder = new Vector2(marker.x + 3f, marker.y + 7f);
            Vector2 crown = new Vector2(marker.center.x, marker.y + 2f);
            Vector2 rightShoulder = new Vector2(marker.xMax - 3f, marker.y + 7f);
            Vector2 rightBottom = new Vector2(
                marker.xMax - 3f,
                marker.yMax - 2f);
            DrawLine(leftBottom, leftShoulder, 2f, MountainRoadHairpin);
            DrawLine(leftShoulder, crown, 2f, MountainRoadHairpin);
            DrawLine(crown, rightShoulder, 2f, MountainRoadHairpin);
            DrawLine(rightShoulder, rightBottom, 2f, MountainRoadHairpin);

            RegisterHoverTarget(
                marker,
                center,
                LocalizationService.Get(
                    "map.mountain_road.tunnel_exit"),
                LandmarkHoverPriority);
            if (!controller.MapPointInspectionEnabled &&
                !controller.IsSelectedAreaCurrent &&
                GUI.Button(marker, GUIContent.none, GUIStyle.none))
            {
                controller.QueueRequestAreaTravel(GameAreaId.MountainRoad);
            }
        }

        private void DrawAreaTravelPanel(Rect panel)
        {
            string areaName = controller.GetAreaLabel(
                controller.SelectedArea);
            GUI.Label(
                new Rect(
                    panel.x + 6f,
                    panel.y + 5f,
                    panel.width - 58f,
                    18f),
                areaName,
                subtitleStyle);

            string message = controller.IsSelectedAreaCurrent
                ? LocalizationService.Get("map.area.current")
                : string.Format(
                    LocalizationService.Get("map.area.travel_question"),
                    areaName);
            GUI.Label(
                new Rect(
                    panel.x + 9f,
                    panel.y + 39f,
                    panel.width - 18f,
                    62f),
                message,
                centeredStyle);

            if (controller.IsSelectedAreaCurrent)
            {
                return;
            }

            Rect travelButton = new Rect(
                panel.x + 14f,
                panel.y + 119f,
                panel.width - 28f,
                26f);
            RetroUiTheme.DrawPanel(
                travelButton,
                RetroUiTheme.PanelRaised,
                RetroUiTheme.Good,
                true,
                2f,
                1f);
            bool previousEnabled = GUI.enabled;
            GUI.enabled = controller.CanRequestSelectedAreaTravel;
            if (GUI.Button(
                travelButton,
                LocalizationService.Get("map.area.travel"),
                hintStyle))
            {
                controller.QueueRequestAreaTravel(
                    controller.SelectedArea);
            }

            GUI.enabled = previousEnabled;
        }
    }
}

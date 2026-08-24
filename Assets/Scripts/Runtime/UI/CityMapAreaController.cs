using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using UnityEngine;
using UnityEngine.InputSystem;

namespace BarPromenade
{
    public sealed partial class CityMapController
    {
        private enum AreaMapCommandType
        {
            SelectArea,
            RequestTravel
        }

        private readonly struct AreaMapCommand
        {
            public AreaMapCommand(
                AreaMapCommandType type,
                GameAreaId area)
            {
                Type = type;
                Area = area;
            }

            public AreaMapCommandType Type { get; }
            public GameAreaId Area { get; }
        }

        private static readonly IReadOnlyList<GameAreaId> MapAreas =
            new ReadOnlyCollection<GameAreaId>(
                new List<GameAreaId>
                {
                    GameAreaId.City,
                    GameAreaId.MountainRoad
                });

        private readonly Queue<AreaMapCommand> pendingAreaMapCommands =
            new Queue<AreaMapCommand>();

        private Func<GameAreaId, AreaArrivalToken, bool>
            areaTravelRequested;
        private bool areaTabsConfigured;
        private GameAreaId currentArea = GameAreaId.City;
        private GameAreaId selectedArea = GameAreaId.City;
        private CityMapMountainRoadOverlay mountainRoadOverlay =
            CityMapMountainRoadOverlay.Empty;

        public bool AreaTabsConfigured => areaTabsConfigured;
        public IReadOnlyList<GameAreaId> AreaTabs => MapAreas;
        public GameAreaId CurrentArea => currentArea;
        public GameAreaId SelectedArea => selectedArea;
        public CityMapMountainRoadOverlay MountainRoadOverlay =>
            mountainRoadOverlay;
        public bool IsSelectedAreaCurrent => selectedArea == currentArea;
        public bool CanRequestSelectedAreaTravel =>
            areaTabsConfigured &&
            selectedArea != currentArea &&
            areaTravelRequested != null;
        public bool IsCityMapInteractionActive =>
            selectedArea == GameAreaId.City &&
            currentArea == GameAreaId.City;
        public bool ShouldDrawPlayerOnSelectedArea =>
            selectedArea == currentArea;

        public Rect ActiveDisplayWorldXZBounds =>
            selectedArea == GameAreaId.MountainRoad &&
            !mountainRoadOverlay.IsEmpty
                ? mountainRoadOverlay.DisplayWorldXZBounds
                : DisplayWorldXZBounds;

        public Vector2 ActiveMapReferenceWorldSize =>
            selectedArea == GameAreaId.City && Layout != null
                ? Layout.NodeSpacing
                : new Vector2(8f, 8f);

        public void ConfigureAreas(
            GameAreaId activeArea,
            IReadOnlyList<Vector3> mountainRouteSamples,
            Rect mountainPlateauBounds,
            Func<GameAreaId, AreaArrivalToken, bool> travelRequested)
        {
            ConfigureAreas(
                activeArea,
                CityMapMountainRoadOverlayBuilder.Create(
                    mountainRouteSamples,
                    mountainPlateauBounds),
                travelRequested);
        }

        public void ConfigureAreas(
            GameAreaId activeArea,
            CityMapMountainRoadOverlay mountainPresentation,
            Func<GameAreaId, AreaArrivalToken, bool> travelRequested)
        {
            if (activeArea != GameAreaId.City &&
                activeArea != GameAreaId.MountainRoad)
            {
                throw new ArgumentOutOfRangeException(nameof(activeArea));
            }

            mountainRoadOverlay = mountainPresentation ??
                throw new ArgumentNullException(nameof(mountainPresentation));
            if (mountainRoadOverlay.IsEmpty)
            {
                throw new ArgumentException(
                    "The mountain-road tab needs a visible route.",
                    nameof(mountainPresentation));
            }

            areaTravelRequested = travelRequested;
            currentArea = activeArea;
            selectedArea = activeArea;
            areaTabsConfigured = true;
            pendingAreaMapCommands.Clear();
            SelectedMapObjectIndex = -1;
        }

        public bool SelectArea(GameAreaId area)
        {
            if (!areaTabsConfigured || !IsKnownArea(area))
            {
                return false;
            }

            bool changed = selectedArea != area;
            selectedArea = area;
            SelectedMapObjectIndex = -1;
            if (changed)
            {
                RetroAudio.Play(RetroSfxId.UiMove);
                GameLog.Info(
                    "map",
                    "area_tab_selected",
                    GameLog.Field("area", area.ToString()),
                    GameLog.Field(
                        "current_area",
                        currentArea.ToString()));
            }

            return changed;
        }

        public bool RequestSelectedAreaTravel()
        {
            return RequestAreaTravel(selectedArea);
        }

        public bool RequestAreaTravel(GameAreaId destinationArea)
        {
            if (!areaTabsConfigured ||
                !IsKnownArea(destinationArea) ||
                destinationArea == currentArea ||
                areaTravelRequested == null)
            {
                return false;
            }

            Func<GameAreaId, AreaArrivalToken, bool> callback =
                areaTravelRequested;
            if (!callback(
                    destinationArea,
                    AreaArrivalToken.MapTeleport))
            {
                GameLog.Warning(
                    "map",
                    "area_travel_rejected",
                    GameLog.Field("from_area", currentArea.ToString()),
                    GameLog.Field(
                        "to_area",
                        destinationArea.ToString()),
                    GameLog.Field(
                        "arrival",
                        AreaArrivalToken.MapTeleport.ToString()));
                return false;
            }

            if (IsOpen)
            {
                Close(false, "area_travel");
            }

            RetroAudio.Play(RetroSfxId.UiConfirm);
            GameLog.Info(
                "map",
                "area_travel_requested",
                GameLog.Field("from_area", currentArea.ToString()),
                GameLog.Field("to_area", destinationArea.ToString()),
                GameLog.Field(
                    "arrival",
                    AreaArrivalToken.MapTeleport.ToString()));
            return true;
        }

        public string GetAreaLabel(GameAreaId area)
        {
            switch (area)
            {
                case GameAreaId.City:
                    return LocalizationService.Get("map.area.city");
                case GameAreaId.MountainRoad:
                    return LocalizationService.Get(
                        "map.area.mountain_road");
                default:
                    return area.ToString();
            }
        }

        public Vector3 GetSelectedAreaTravelTargetPosition()
        {
            if (selectedArea == GameAreaId.MountainRoad)
            {
                return mountainRoadOverlay.TunnelPosition;
            }

            if (MountainBoundaryPlan != null &&
                MountainBoundaryPlan.HasTunnel)
            {
                return MountainBoundaryPlan.Tunnel.PortalGroundCenter;
            }

            Rect bounds = DisplayWorldXZBounds;
            return new Vector3(bounds.center.x, 0f, bounds.center.y);
        }

        public void QueueSelectArea(GameAreaId area)
        {
            pendingAreaMapCommands.Enqueue(
                new AreaMapCommand(
                    AreaMapCommandType.SelectArea,
                    area));
        }

        public void QueueRequestAreaTravel(GameAreaId area)
        {
            pendingAreaMapCommands.Enqueue(
                new AreaMapCommand(
                    AreaMapCommandType.RequestTravel,
                    area));
        }

        private void ProcessAreaMapCommands()
        {
            while (pendingAreaMapCommands.Count > 0)
            {
                AreaMapCommand command = pendingAreaMapCommands.Dequeue();
                if (!IsOpen)
                {
                    continue;
                }

                if (command.Type == AreaMapCommandType.SelectArea)
                {
                    SelectArea(command.Area);
                }
                else
                {
                    RequestAreaTravel(command.Area);
                }
            }
        }

        private void ClearAreaMapCommands()
        {
            pendingAreaMapCommands.Clear();
        }

        private void MoveAreaSelection(int delta)
        {
            if (!areaTabsConfigured || delta == 0)
            {
                return;
            }

            int index = 0;
            for (int areaIndex = 0;
                 areaIndex < MapAreas.Count;
                 areaIndex++)
            {
                if (MapAreas[areaIndex] == selectedArea)
                {
                    index = areaIndex;
                    break;
                }
            }

            int next = (index + Math.Sign(delta)) % MapAreas.Count;
            if (next < 0)
            {
                next += MapAreas.Count;
            }

            SelectArea(MapAreas[next]);
        }

        private static int ReadAreaSelectionDelta()
        {
            Keyboard keyboard = Keyboard.current;
            if (keyboard != null &&
                keyboard.tabKey.wasPressedThisFrame)
            {
                return keyboard.leftShiftKey.isPressed ||
                       keyboard.rightShiftKey.isPressed
                    ? -1
                    : 1;
            }

            Gamepad gamepad = Gamepad.current;
            if (gamepad != null)
            {
                if (gamepad.leftTrigger.wasPressedThisFrame)
                {
                    return -1;
                }

                if (gamepad.rightTrigger.wasPressedThisFrame)
                {
                    return 1;
                }
            }

            return 0;
        }

        private static bool IsKnownArea(GameAreaId area)
        {
            return area == GameAreaId.City ||
                   area == GameAreaId.MountainRoad;
        }
    }
}

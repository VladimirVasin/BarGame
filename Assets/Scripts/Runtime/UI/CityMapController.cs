using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using Stopwatch = System.Diagnostics.Stopwatch;

namespace BarPromenade
{
    public readonly struct CityMapPointOfInterest
    {
        internal CityMapPointOfInterest(
            string stableId,
            CityDistrictPointOfInterestKind kind,
            CityDistrictKind district,
            Vector2Int lotCell,
            Vector3 worldPosition)
        {
            StableId = stableId ?? string.Empty;
            Kind = kind;
            District = district;
            LotCell = lotCell;
            WorldPosition = worldPosition;
        }

        public string StableId { get; }
        public CityDistrictPointOfInterestKind Kind { get; }
        public CityDistrictKind District { get; }
        public Vector2Int LotCell { get; }
        public Vector3 WorldPosition { get; }
    }

    [DisallowMultipleComponent]
    public sealed class CityMapController : MonoBehaviour
    {
        private enum CommandType
        {
            ToggleMap,
            ToggleBar,
            MoveBar,
            ClearRoute,
            SelectMapObject,
            ConfirmDebugTeleport
        }

        private readonly struct PendingCommand
        {
            public PendingCommand(
                CommandType type,
                int barIndex = -1,
                string barId = "",
                int direction = 0)
            {
                Type = type;
                BarIndex = barIndex;
                BarId = barId ?? string.Empty;
                Direction = direction;
            }

            public CommandType Type { get; }
            public int BarIndex { get; }
            public string BarId { get; }
            public int Direction { get; }
        }

        private readonly List<BuildingLot> bars = new List<BuildingLot>();
        private readonly List<CityMapPointOfInterest> pointsOfInterest =
            new List<CityMapPointOfInterest>();
        private readonly List<BuildingLot> orderedStops =
            new List<BuildingLot>();
        private readonly Queue<PendingCommand> pendingCommands =
            new Queue<PendingCommand>();

        private PlayerRuntime player;
        private PlayerCameraFollow cameraFollow;
        private IntoxicationHudView intoxicationHud;
        private readonly BarMinigameModalLock modalLock =
            new BarMinigameModalLock();
        private int inputUnlockFrame;
        private long openedTimestamp;

        public bool IsInitialized { get; private set; }
        public bool IsOpen { get; private set; }
        public CityLayout Layout { get; private set; }
        public IReadOnlyList<BuildingLot> Bars => bars;
        public IReadOnlyList<CityMapPointOfInterest> PointsOfInterest =>
            pointsOfInterest;
        public CityMapBusOverlay BusOverlay { get; private set; } =
            CityMapBusOverlay.Empty;
        public IReadOnlyList<BuildingLot> MapObjects =>
            Layout?.BuildingLots ?? Array.Empty<BuildingLot>();
        public BuildingLot PlayerHome => Layout?.PlayerHome;
        public BuildingLot Supermarket => Layout?.Supermarket;
        public IReadOnlyList<string> Route => GameSessionState.PlannedBarRoute;
        public int VisitedBarCount
        {
            get
            {
                int count = 0;
                for (int index = 0; index < bars.Count; index++)
                {
                    if (IsBarVisited(bars[index].BarId))
                    {
                        count++;
                    }
                }

                return count;
            }
        }
        public CityRoutePath CurrentPath { get; private set; }
        public int SelectedBarIndex { get; private set; }
        public int SelectedMapObjectIndex { get; private set; } = -1;
        public bool DebugTeleportEnabled { get; private set; }
        public BuildingLot SelectedMapObject =>
            IsValidMapObjectIndex(SelectedMapObjectIndex)
                ? MapObjects[SelectedMapObjectIndex]
                : null;
        public CityMapView View { get; private set; }
        public Vector3 PlayerWorldPosition =>
            player.GameObject == null
                ? Vector3.zero
                : player.GameObject.transform.position;
        public Vector3 PlayerForward =>
            player.GameObject == null
                ? Vector3.forward
                : player.GameObject.transform.forward;

        public void Initialize(
            CityLayout layout,
            PlayerRuntime playerRuntime,
            PlayerCameraFollow follow,
            IntoxicationHudView hud)
        {
            Initialize(
                layout,
                playerRuntime,
                follow,
                hud,
                null);
        }

        public void Initialize(
            CityLayout layout,
            PlayerRuntime playerRuntime,
            PlayerCameraFollow follow,
            IntoxicationHudView hud,
            CityBusPlan busPlan)
        {
            Layout = layout ?? throw new ArgumentNullException(nameof(layout));
            player = playerRuntime;
            cameraFollow = follow;
            intoxicationHud = hud;
            BusOverlay = CityMapBusOverlayBuilder.Create(busPlan);

            bars.Clear();
            for (int index = 0; index < Layout.BuildingLots.Count; index++)
            {
                BuildingLot lot = Layout.BuildingLots[index];
                if (lot.IsBar)
                {
                    bars.Add(lot);
                }
            }

            bars.Sort(CompareBarLots);
            CollectPointsOfInterest();
            SelectedBarIndex = bars.Count == 0
                ? -1
                : Mathf.Clamp(SelectedBarIndex, 0, bars.Count - 1);
            SelectedMapObjectIndex = -1;

            View = GetComponent<CityMapView>();
            if (View == null)
            {
                View = gameObject.AddComponent<CityMapView>();
            }

            View.Initialize(this);
            IsInitialized = true;
            RefreshPath("initialize");
            GameLog.Info(
                "map",
                "initialized",
                GameLog.Field("bar_count", bars.Count),
                GameLog.Field(
                    "point_of_interest_count",
                    pointsOfInterest.Count),
                GameLog.Field(
                    "bus_route_id",
                    BusOverlay.RouteId),
                GameLog.Field(
                    "bus_route_point_count",
                    BusOverlay.RoutePoints.Count),
                GameLog.Field(
                    "bus_stop_count",
                    BusOverlay.Stops.Count),
                GameLog.Field(
                    "selected_bar_index",
                    SelectedBarIndex),
                GameLog.Field(
                    "route",
                    FormatRoute()),
                GameLog.Field(
                    "visited_count",
                    VisitedBarCount),
                GameLog.Field(
                    "path_length",
                    CurrentPath.TotalLength));
        }

        public void Initialize(
            CityLayout layout,
            CityDecorationPlan decorationPlan,
            PlayerRuntime playerRuntime,
            PlayerCameraFollow follow,
            IntoxicationHudView hud)
        {
            _ = decorationPlan;
            Initialize(
                layout,
                playerRuntime,
                follow,
                hud);
        }

        public void Initialize(
            CityLayout layout,
            CityDecorationPlan decorationPlan,
            CityBusPlan busPlan,
            PlayerRuntime playerRuntime,
            PlayerCameraFollow follow,
            IntoxicationHudView hud)
        {
            _ = decorationPlan;
            Initialize(
                layout,
                playerRuntime,
                follow,
                hud,
                busPlan);
        }

        public bool Open()
        {
            if (!IsInitialized ||
                IsOpen ||
                SceneTransitionService.IsTransitioning)
            {
                return false;
            }

            if (!modalLock.TryCaptureAndDisable(
                    player.Interactor,
                    cameraFollow,
                    intoxicationHud))
            {
                return false;
            }

            inputUnlockFrame = Time.frameCount + 1;
            IsOpen = true;
            openedTimestamp = Stopwatch.GetTimestamp();
            RefreshPath("open");
            RetroAudio.Play(RetroSfxId.MapOpen);
            GameLog.Info(
                "map",
                "opened",
                GameLog.Field(
                    "selected_bar_id",
                    GetSelectedBarId()),
                GameLog.Field(
                    "selected_bar_index",
                    SelectedBarIndex),
                GameLog.Field(
                    "route_count",
                    Route.Count),
                GameLog.Field(
                    "path_length",
                    CurrentPath.TotalLength),
                GameLog.Field(
                    "player_x",
                    PlayerWorldPosition.x),
                GameLog.Field(
                    "player_z",
                    PlayerWorldPosition.z));
            return true;
        }

        public bool Close()
        {
            return Close(true, "user");
        }

        private bool Close(
            bool playSound,
            string reason)
        {
            if (!IsOpen)
            {
                return false;
            }

            IsOpen = false;
            modalLock.Restore();

            if (playSound)
            {
                RetroAudio.Play(RetroSfxId.UiCancel);
            }

            GameLog.Info(
                "map",
                "closed",
                GameLog.Field("reason", reason),
                GameLog.Field(
                    "open_duration_ms",
                    GetOpenDurationMilliseconds()),
                GameLog.Field(
                    "route",
                    FormatRoute()),
                GameLog.Field(
                    "path_length",
                    CurrentPath == null
                        ? 0f
                        : CurrentPath.TotalLength));
            openedTimestamp = 0L;
            return true;
        }

        public bool ToggleBar(int barIndex)
        {
            if (!IsValidBarIndex(barIndex))
            {
                return false;
            }

            SelectedBarIndex = barIndex;
            string barId = bars[barIndex].BarId;
            bool wasSelected = GetRouteOrder(barId) >= 0;
            if (wasSelected)
            {
                GameSessionState.RemoveRouteStop(barId);
            }
            else
            {
                GameSessionState.TryAddRouteStop(barId);
            }

            RefreshPath(
                wasSelected
                    ? "remove_stop"
                    : "add_stop");
            bool changed =
                wasSelected != (GetRouteOrder(barId) >= 0);
            if (changed)
            {
                RetroAudio.Play(RetroSfxId.UiConfirm);
            }

            return changed;
        }

        public bool MoveBar(string barId, int direction)
        {
            int previousIndex = GetRouteOrder(barId);
            if (previousIndex < 0 || direction == 0)
            {
                return false;
            }

            int barIndex = FindBarIndex(barId);
            if (barIndex >= 0)
            {
                SelectedBarIndex = barIndex;
            }

            GameSessionState.MoveRouteStop(barId, direction);
            RefreshPath("move_stop");
            bool changed = GetRouteOrder(barId) != previousIndex;
            if (changed)
            {
                RetroAudio.Play(RetroSfxId.UiMove);
            }

            return changed;
        }

        public bool ClearRoute()
        {
            if (GameSessionState.PlannedBarRoute.Count == 0)
            {
                return false;
            }

            GameSessionState.ClearRoute();
            RefreshPath("clear_route");
            RetroAudio.Play(RetroSfxId.UiCancel);
            return true;
        }

        public int GetRouteOrder(string barId)
        {
            IReadOnlyList<string> route = GameSessionState.PlannedBarRoute;
            for (int index = 0; index < route.Count; index++)
            {
                if (string.Equals(
                    route[index],
                    barId,
                    StringComparison.Ordinal))
                {
                    return index;
                }
            }

            return -1;
        }

        public bool IsBarVisited(string barId)
        {
            return GameSessionState.IsBarVisited(barId);
        }

        public int FindBarIndex(string barId)
        {
            for (int index = 0; index < bars.Count; index++)
            {
                if (string.Equals(
                    bars[index].BarId,
                    barId,
                    StringComparison.Ordinal))
                {
                    return index;
                }
            }

            return -1;
        }

        public string GetBarLabel(int barIndex)
        {
            return IsValidBarIndex(barIndex)
                ? string.Format(
                    LocalizationService.Get("map.bar_name"),
                    barIndex + 1)
                : string.Empty;
        }

        public string GetPointOfInterestLabel(int pointOfInterestIndex)
        {
            if (pointOfInterestIndex < 0 ||
                pointOfInterestIndex >= pointsOfInterest.Count)
            {
                return string.Empty;
            }

            return TryGetPointOfInterestLocalizationKey(
                    pointsOfInterest[pointOfInterestIndex].Kind,
                    out string key)
                ? LocalizationService.Get(key)
                : string.Empty;
        }

        public string GetSupermarketLabel()
        {
            return LocalizationService.Get("map.supermarket");
        }

        public string GetBusStopLabel(int busStopIndex)
        {
            if (busStopIndex < 0 ||
                busStopIndex >= BusOverlay.Stops.Count)
            {
                return string.Empty;
            }

            CityMapBusStopMarker marker =
                BusOverlay.Stops[busStopIndex];
            if (!string.IsNullOrWhiteSpace(
                    marker.LabelLocalizationKey))
            {
                string localized = LocalizationService.Get(
                    marker.LabelLocalizationKey);
                if (!string.Equals(
                        localized,
                        marker.LabelLocalizationKey,
                        StringComparison.Ordinal))
                {
                    return localized;
                }
            }

            return string.Format(
                LocalizationService.Get("map.bus.stop"),
                marker.Ordinal);
        }

        public string GetMapObjectLabel(int mapObjectIndex)
        {
            if (!IsValidMapObjectIndex(mapObjectIndex))
            {
                return string.Empty;
            }

            BuildingLot lot = MapObjects[mapObjectIndex];
            if (lot.IsBar)
            {
                return GetBarLabel(FindBarIndex(lot.BarId));
            }

            if (lot.IsPlayerHome)
            {
                return LocalizationService.Get("map.home");
            }

            if (lot.IsSupermarket)
            {
                return GetSupermarketLabel();
            }

            int pointOfInterestIndex = FindPointOfInterestIndex(lot.Cell);
            return pointOfInterestIndex >= 0
                ? GetPointOfInterestLabel(pointOfInterestIndex)
                : string.Format(
                    LocalizationService.Get("map.object"),
                    lot.Cell.x,
                    lot.Cell.y);
        }

        public int FindMapObjectIndex(BuildingLot lot)
        {
            if (lot == null)
            {
                return -1;
            }

            for (int index = 0; index < MapObjects.Count; index++)
            {
                if (ReferenceEquals(MapObjects[index], lot))
                {
                    return index;
                }
            }

            return -1;
        }

        public bool SetDebugTeleportEnabled(bool enabled)
        {
            if (DebugTeleportEnabled == enabled)
            {
                return false;
            }

            DebugTeleportEnabled = enabled;
            SelectedMapObjectIndex = -1;
            GameLog.Info(
                "map",
                "debug_teleport_mode_changed",
                GameLog.Field("enabled", enabled));
            return true;
        }

        public bool SelectMapObject(int mapObjectIndex)
        {
            if (!DebugTeleportEnabled ||
                !IsOpen ||
                !IsValidMapObjectIndex(mapObjectIndex))
            {
                return false;
            }

            bool changed = SelectedMapObjectIndex != mapObjectIndex;
            SelectedMapObjectIndex = mapObjectIndex;
            if (changed)
            {
                RetroAudio.Play(RetroSfxId.UiMove);
            }

            return true;
        }

        public bool ConfirmDebugTeleport()
        {
            if (!DebugTeleportEnabled ||
                !IsOpen ||
                player.GameObject == null ||
                player.Motor == null ||
                SelectedMapObject == null)
            {
                return false;
            }

            BuildingLot target = SelectedMapObject;
            Vector3 destination =
                ResolveDebugTeleportDestination(target);
            Vector3 facing = target.DoorPosition - destination;
            facing.y = 0f;
            SelectedMapObjectIndex = -1;

            Close(false, "debug_teleport");
            player.Motor.Teleport(destination);
            if (facing.sqrMagnitude > 0.0001f)
            {
                player.GameObject.transform.rotation =
                    Quaternion.LookRotation(facing.normalized, Vector3.up);
            }

            RefreshPath("debug_teleport");
            RetroAudio.Play(RetroSfxId.UiConfirm);
            GameLog.Info(
                "map",
                "debug_teleported",
                GameLog.Field("cell_x", target.Cell.x),
                GameLog.Field("cell_y", target.Cell.y),
                GameLog.Field("x", destination.x),
                GameLog.Field("y", destination.y),
                GameLog.Field("z", destination.z));
            return true;
        }

        private Vector3 ResolveDebugTeleportDestination(
            BuildingLot target)
        {
            Vector3 destination = target.ReturnPosition;
            if (!Layout.TryGetFrontageEdge(target, out _))
            {
                float bestSquaredDistance = float.PositiveInfinity;
                for (int index = 0;
                     index < Layout.RoadEdges.Count;
                     index++)
                {
                    RoadEdge edge = Layout.RoadEdges[index];
                    Vector3 candidate = ClosestPointOnPlanarSegment(
                        target.Center,
                        Layout.GetNodeWorldPosition(edge.A),
                        Layout.GetNodeWorldPosition(edge.B));
                    float squaredDistance =
                        (candidate - target.Center).sqrMagnitude;
                    if (squaredDistance < bestSquaredDistance)
                    {
                        bestSquaredDistance = squaredDistance;
                        destination = candidate;
                    }
                }
            }

            if (Layout.ElevationPlan.TrySampleSurface(
                    new Vector2(destination.x, destination.z),
                    CitySurfaceRole.RoadTop,
                    out float roadTop,
                    out _))
            {
                destination.y = roadTop +
                                PlayerFactory.GroundedRootOffset;
            }
            else
            {
                destination.y += CityStreetSurfacePlanner.RoadTop +
                                 PlayerFactory.GroundedRootOffset;
            }

            return destination;
        }

        private static Vector3 ClosestPointOnPlanarSegment(
            Vector3 point,
            Vector3 start,
            Vector3 end)
        {
            Vector2 pointXZ = new Vector2(point.x, point.z);
            Vector2 startXZ = new Vector2(start.x, start.z);
            Vector2 endXZ = new Vector2(end.x, end.z);
            Vector2 segment = endXZ - startXZ;
            float squaredLength = segment.sqrMagnitude;
            if (squaredLength <= 0.0001f)
            {
                return start;
            }

            float progress = Mathf.Clamp01(
                Vector2.Dot(pointXZ - startXZ, segment) /
                squaredLength);
            return Vector3.Lerp(start, end, progress);
        }

        internal static bool TryGetPointOfInterestLocalizationKey(
            CityDistrictPointOfInterestKind kind,
            out string key)
        {
            switch (kind)
            {
                case CityDistrictPointOfInterestKind.OldTownWaterworksCourt:
                    key = "map.poi.old_town_waterworks_court";
                    return true;
                case CityDistrictPointOfInterestKind.ResidentialDryingYard:
                    key = "map.poi.residential_drying_yard";
                    return true;
                case CityDistrictPointOfInterestKind.IndustrialWeighbridge:
                    key = "map.poi.industrial_weighbridge";
                    return true;
                case CityDistrictPointOfInterestKind.NightlifeLastRouteIsland:
                    key = "map.poi.nightlife_last_route_island";
                    return true;
                default:
                    key = string.Empty;
                    return false;
            }
        }

        public void QueueToggleMap()
        {
            pendingCommands.Enqueue(
                new PendingCommand(CommandType.ToggleMap));
        }

        public void QueueToggleBar(int barIndex)
        {
            pendingCommands.Enqueue(
                new PendingCommand(
                    CommandType.ToggleBar,
                    barIndex: barIndex));
        }

        public void QueueMoveBar(string barId, int direction)
        {
            pendingCommands.Enqueue(
                new PendingCommand(
                    CommandType.MoveBar,
                    barId: barId,
                    direction: direction));
        }

        public void QueueClearRoute()
        {
            pendingCommands.Enqueue(
                new PendingCommand(CommandType.ClearRoute));
        }

        public void QueueSelectMapObject(int mapObjectIndex)
        {
            pendingCommands.Enqueue(
                new PendingCommand(
                    CommandType.SelectMapObject,
                    barIndex: mapObjectIndex));
        }

        public void QueueConfirmDebugTeleport()
        {
            pendingCommands.Enqueue(
                new PendingCommand(CommandType.ConfirmDebugTeleport));
        }

        private void Update()
        {
            ProcessQueuedCommands();

            if (!IsInitialized)
            {
                return;
            }

            if (!IsOpen)
            {
                if (WasMapTogglePressed())
                {
                    Open();
                }

                return;
            }

            if (SceneTransitionService.IsTransitioning)
            {
                Close(false, "transition");
                return;
            }

            if (Time.frameCount <= inputUnlockFrame)
            {
                return;
            }

            if (WasMapTogglePressed() || WasCancelPressed())
            {
                Close();
                return;
            }

            if (!DebugTeleportEnabled && WasClearPressed())
            {
                ClearRoute();
                return;
            }

            int selectionDelta = ReadSelectionDelta();
            if (selectionDelta != 0)
            {
                if (DebugTeleportEnabled)
                {
                    MoveMapObjectSelection(selectionDelta);
                }
                else
                {
                    MoveSelection(selectionDelta);
                }
            }

            int routeMove = ReadRouteMove();
            if (!DebugTeleportEnabled &&
                routeMove != 0 &&
                IsValidBarIndex(SelectedBarIndex))
            {
                MoveBar(bars[SelectedBarIndex].BarId, routeMove);
            }

            if (WasConfirmPressed())
            {
                if (DebugTeleportEnabled)
                {
                    ConfirmDebugTeleport();
                }
                else if (IsValidBarIndex(SelectedBarIndex))
                {
                    ToggleBar(SelectedBarIndex);
                }
            }
        }

        private void OnDisable()
        {
            pendingCommands.Clear();
            Close(false, "disabled");
        }

        private void OnDestroy()
        {
            Close(false, "destroyed");
        }

        private void ProcessQueuedCommands()
        {
            while (pendingCommands.Count > 0)
            {
                PendingCommand command = pendingCommands.Dequeue();
                switch (command.Type)
                {
                    case CommandType.ToggleMap:
                        if (IsOpen)
                        {
                            Close();
                        }
                        else
                        {
                            Open();
                        }

                        break;
                    case CommandType.ToggleBar:
                        if (IsOpen)
                        {
                            ToggleBar(command.BarIndex);
                        }

                        break;
                    case CommandType.MoveBar:
                        if (IsOpen)
                        {
                            MoveBar(command.BarId, command.Direction);
                        }

                        break;
                    case CommandType.ClearRoute:
                        if (IsOpen)
                        {
                            ClearRoute();
                        }

                        break;
                    case CommandType.SelectMapObject:
                        SelectMapObject(command.BarIndex);
                        break;
                    case CommandType.ConfirmDebugTeleport:
                        ConfirmDebugTeleport();
                        break;
                }
            }
        }

        private void RefreshPath(string reason)
        {
            if (!IsInitialized && Layout == null)
            {
                return;
            }

            RemoveUnknownRouteStops();
            orderedStops.Clear();
            IReadOnlyList<string> route = GameSessionState.PlannedBarRoute;
            for (int index = 0; index < route.Count; index++)
            {
                int barIndex = FindBarIndex(route[index]);
                if (barIndex >= 0)
                {
                    orderedStops.Add(bars[barIndex]);
                }
            }

            CurrentPath = CityRoutePathfinder.Build(
                Layout,
                PlayerWorldPosition,
                orderedStops);
            GameLog.Debug(
                "map",
                "path_rebuilt",
                GameLog.Field("reason", reason),
                GameLog.Field("route", FormatRoute()),
                GameLog.Field(
                    "ordered_stop_count",
                    orderedStops.Count),
                GameLog.Field(
                    "point_count",
                    CurrentPath.Points.Count),
                GameLog.Field(
                    "path_length",
                    CurrentPath.TotalLength),
                GameLog.Field(
                    "is_empty",
                    CurrentPath.IsEmpty),
                GameLog.Field(
                    "player_x",
                    PlayerWorldPosition.x),
                GameLog.Field(
                    "player_z",
                    PlayerWorldPosition.z));
        }

        private void RemoveUnknownRouteStops()
        {
            IReadOnlyList<string> route =
                GameSessionState.PlannedBarRoute;
            for (int index = route.Count - 1; index >= 0; index--)
            {
                if (FindBarIndex(route[index]) < 0)
                {
                    string barId = route[index];
                    GameSessionState.RemoveRouteStop(barId);
                    GameLog.Warning(
                        "map",
                        "unknown_route_stop_removed",
                        GameLog.Field("bar_id", barId));
                }
            }
        }

        private string GetSelectedBarId()
        {
            return IsValidBarIndex(SelectedBarIndex)
                ? bars[SelectedBarIndex].BarId
                : string.Empty;
        }

        private static string FormatRoute()
        {
            return string.Join(
                ",",
                GameSessionState.PlannedBarRoute);
        }

        private long GetOpenDurationMilliseconds()
        {
            if (openedTimestamp <= 0L)
            {
                return 0L;
            }

            long elapsedTicks =
                Stopwatch.GetTimestamp() - openedTimestamp;
            return Math.Max(
                0L,
                (long)(
                    (elapsedTicks * 1000d) /
                    Stopwatch.Frequency));
        }

        private void MoveSelection(int delta)
        {
            if (bars.Count == 0 || delta == 0)
            {
                SelectedBarIndex = -1;
                return;
            }

            int previousIndex = SelectedBarIndex;
            SelectedBarIndex = (SelectedBarIndex + Math.Sign(delta)) % bars.Count;
            if (SelectedBarIndex < 0)
            {
                SelectedBarIndex += bars.Count;
            }

            if (SelectedBarIndex != previousIndex)
            {
                RetroAudio.Play(RetroSfxId.UiMove);
            }
        }

        private void MoveMapObjectSelection(int delta)
        {
            if (MapObjects.Count == 0 || delta == 0)
            {
                SelectedMapObjectIndex = -1;
                return;
            }

            int nextIndex = SelectedMapObjectIndex < 0
                ? delta > 0 ? 0 : MapObjects.Count - 1
                : (SelectedMapObjectIndex + Math.Sign(delta)) %
                  MapObjects.Count;
            if (nextIndex < 0)
            {
                nextIndex += MapObjects.Count;
            }

            SelectMapObject(nextIndex);
        }

        private bool IsValidBarIndex(int index)
        {
            return index >= 0 && index < bars.Count;
        }

        private bool IsValidMapObjectIndex(int index)
        {
            return index >= 0 && index < MapObjects.Count;
        }

        internal bool IsPointOfInterestLot(Vector2Int cell)
        {
            return FindPointOfInterestIndex(cell) >= 0;
        }

        private int FindPointOfInterestIndex(Vector2Int cell)
        {
            for (int index = 0; index < pointsOfInterest.Count; index++)
            {
                if (pointsOfInterest[index].LotCell == cell)
                {
                    return index;
                }
            }

            return -1;
        }

        private void CollectPointsOfInterest()
        {
            pointsOfInterest.Clear();
            var descriptors = Layout.DistrictPointsOfInterest;
            for (int index = 0; index < descriptors.Count; index++)
            {
                var descriptor = descriptors[index];

                pointsOfInterest.Add(
                    new CityMapPointOfInterest(
                        descriptor.Id,
                        descriptor.Kind,
                        descriptor.District,
                        descriptor.Cell,
                        descriptor.Center));
            }

            pointsOfInterest.Sort(ComparePointsOfInterest);
        }

        private static int CompareBarLots(BuildingLot left, BuildingLot right)
        {
            int rowComparison = left.Cell.y.CompareTo(right.Cell.y);
            return rowComparison != 0
                ? rowComparison
                : left.Cell.x.CompareTo(right.Cell.x);
        }

        private static int ComparePointsOfInterest(
            CityMapPointOfInterest left,
            CityMapPointOfInterest right)
        {
            int districtComparison =
                left.District.CompareTo(right.District);
            return districtComparison != 0
                ? districtComparison
                : string.Compare(
                    left.StableId,
                    right.StableId,
                    StringComparison.Ordinal);
        }

        private static bool WasMapTogglePressed()
        {
            Keyboard keyboard = Keyboard.current;
            if (keyboard != null && keyboard.mKey.wasPressedThisFrame)
            {
                return true;
            }

            Gamepad gamepad = Gamepad.current;
            return gamepad != null &&
                   gamepad.selectButton.wasPressedThisFrame;
        }

        private static bool WasCancelPressed()
        {
            Keyboard keyboard = Keyboard.current;
            if (keyboard != null && keyboard.escapeKey.wasPressedThisFrame)
            {
                return true;
            }

            Gamepad gamepad = Gamepad.current;
            return gamepad != null &&
                   gamepad.buttonEast.wasPressedThisFrame;
        }

        private static bool WasConfirmPressed()
        {
            Keyboard keyboard = Keyboard.current;
            if (keyboard != null &&
                (keyboard.eKey.wasPressedThisFrame ||
                 keyboard.enterKey.wasPressedThisFrame))
            {
                return true;
            }

            Gamepad gamepad = Gamepad.current;
            return gamepad != null &&
                   gamepad.buttonSouth.wasPressedThisFrame;
        }

        private static bool WasClearPressed()
        {
            Keyboard keyboard = Keyboard.current;
            if (keyboard != null && keyboard.rKey.wasPressedThisFrame)
            {
                return true;
            }

            Gamepad gamepad = Gamepad.current;
            return gamepad != null &&
                   gamepad.buttonWest.wasPressedThisFrame;
        }

        private static int ReadSelectionDelta()
        {
            Keyboard keyboard = Keyboard.current;
            if (keyboard != null)
            {
                if (keyboard.leftArrowKey.wasPressedThisFrame)
                {
                    return -1;
                }

                if (keyboard.rightArrowKey.wasPressedThisFrame)
                {
                    return 1;
                }
            }

            Gamepad gamepad = Gamepad.current;
            if (gamepad != null)
            {
                if (gamepad.dpad.left.wasPressedThisFrame)
                {
                    return -1;
                }

                if (gamepad.dpad.right.wasPressedThisFrame)
                {
                    return 1;
                }
            }

            return 0;
        }

        private static int ReadRouteMove()
        {
            Keyboard keyboard = Keyboard.current;
            if (keyboard != null)
            {
                if (keyboard.upArrowKey.wasPressedThisFrame)
                {
                    return -1;
                }

                if (keyboard.downArrowKey.wasPressedThisFrame)
                {
                    return 1;
                }
            }

            Gamepad gamepad = Gamepad.current;
            if (gamepad != null)
            {
                if (gamepad.leftShoulder.wasPressedThisFrame)
                {
                    return -1;
                }

                if (gamepad.rightShoulder.wasPressedThisFrame)
                {
                    return 1;
                }
            }

            return 0;
        }
    }
}

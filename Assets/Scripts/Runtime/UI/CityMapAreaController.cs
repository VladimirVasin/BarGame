using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using UnityEngine;
using UnityEngine.InputSystem;

namespace BarPromenade
{
    public enum CityMapPointKind
    {
        MapObject,
        Bar,
        Home,
        Supermarket,
        PointOfInterest,
        OpenArea,
        BusStop,
        Player,
        Tunnel,
        BoatStation,
        Hairpin,
        Bridge,
        Plateau,
        Cafe,
        Cableway
    }

    /// <summary>
    /// One semantic point on either map tab. This is deliberately separate
    /// from the debug-teleport selection: WorldPosition is the coordinate the
    /// chart presents, not a promise that it is a safe spawn destination.
    /// </summary>
    public readonly struct CityMapPointDescriptor
    {
        internal CityMapPointDescriptor(
            string stableId,
            GameAreaId area,
            CityMapPointKind kind,
            string label,
            Vector3 worldPosition,
            int priority,
            Vector2 screenHitSize,
            Rect worldXZHitBounds = default,
            bool usesWorldHitBounds = false)
        {
            StableId = stableId ?? string.Empty;
            Area = area;
            Kind = kind;
            Label = label ?? string.Empty;
            WorldPosition = worldPosition;
            Priority = priority;
            ScreenHitSize = screenHitSize;
            WorldXZHitBounds = worldXZHitBounds;
            UsesWorldHitBounds = usesWorldHitBounds;
        }

        public string StableId { get; }
        public GameAreaId Area { get; }
        public CityMapPointKind Kind { get; }
        public string Label { get; }
        public Vector3 WorldPosition { get; }
        internal int Priority { get; }
        internal Vector2 ScreenHitSize { get; }
        internal Rect WorldXZHitBounds { get; }
        internal bool UsesWorldHitBounds { get; }
    }

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
        private readonly List<CityMapPointDescriptor> cityMapPoints =
            new List<CityMapPointDescriptor>(176);
        private readonly List<CityMapPointDescriptor> mountainMapPoints =
            new List<CityMapPointDescriptor>(20);

        private Func<GameAreaId, AreaArrivalToken, bool>
            areaTravelRequested;
        private bool areaTabsConfigured;
        private GameAreaId currentArea = GameAreaId.City;
        private GameAreaId selectedArea = GameAreaId.City;
        private CityMapMountainRoadOverlay mountainRoadOverlay =
            CityMapMountainRoadOverlay.Empty;
        private int selectedMapPointIndex = -1;
        private int mapPointFocusRevision;

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
        public bool MapPointInspectionEnabled { get; private set; }
        public int SelectedMapPointIndex => selectedMapPointIndex;
        public int MapPointFocusRevision => mapPointFocusRevision;
        public IReadOnlyList<CityMapPointDescriptor> ActiveMapPoints =>
            GetMapPoints(selectedArea);

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
            selectedMapPointIndex = -1;
            RebuildMapPointCatalogs();
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
            selectedMapPointIndex = -1;
            mapPointFocusRevision++;
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

        public IReadOnlyList<CityMapPointDescriptor> GetMapPoints(
            GameAreaId area)
        {
            switch (area)
            {
                case GameAreaId.City:
                    return cityMapPoints;
                case GameAreaId.MountainRoad:
                    return mountainMapPoints;
                default:
                    return Array.Empty<CityMapPointDescriptor>();
            }
        }

        public bool SetMapPointInspectionEnabled(bool enabled)
        {
            if (MapPointInspectionEnabled == enabled)
            {
                return false;
            }

            MapPointInspectionEnabled = enabled;
            selectedMapPointIndex = -1;
            mapPointFocusRevision++;
            if (enabled)
            {
                // Only the LOT selection is dropped, not debug mode itself.
                // The two used to cancel each other on the grounds that a
                // map click cannot mean two things at once - which is true
                // of the click and false of the modes. Turning the
                // inspector on meant losing the ability to teleport at all,
                // and picking a precise point is exactly when you most want
                // it. The click stays unambiguous because the inspector
                // takes it: while it is on, markers pick POINTS and the
                // whole-lot buttons go quiet.
                SelectedMapObjectIndex = -1;
            }

            RetroAudio.Play(RetroSfxId.UiMove);
            GameLog.Info(
                "map",
                "point_inspection_mode_changed",
                GameLog.Field("enabled", enabled),
                GameLog.Field("area", selectedArea.ToString()));
            return true;
        }

        public bool SelectMapPoint(int pointIndex)
        {
            IReadOnlyList<CityMapPointDescriptor> points = ActiveMapPoints;
            if (!MapPointInspectionEnabled ||
                pointIndex < 0 ||
                pointIndex >= points.Count)
            {
                return false;
            }

            bool changed = selectedMapPointIndex != pointIndex;
            selectedMapPointIndex = pointIndex;
            if (changed)
            {
                mapPointFocusRevision++;
                RetroAudio.Play(RetroSfxId.UiMove);
                CityMapPointDescriptor point = points[pointIndex];
                Vector3 position = ResolveMapPointWorldPosition(point);
                GameLog.Info(
                    "map",
                    "point_selected",
                    GameLog.Field("area", point.Area.ToString()),
                    GameLog.Field("point_id", point.StableId),
                    GameLog.Field("world_x", position.x),
                    GameLog.Field("world_y", position.y),
                    GameLog.Field("world_z", position.z));
            }

            return true;
        }

        public bool TryGetSelectedMapPoint(
            out CityMapPointDescriptor point,
            out Vector3 worldPosition)
        {
            IReadOnlyList<CityMapPointDescriptor> points = ActiveMapPoints;
            if (!MapPointInspectionEnabled ||
                selectedMapPointIndex < 0 ||
                selectedMapPointIndex >= points.Count)
            {
                point = default;
                worldPosition = default;
                return false;
            }

            point = points[selectedMapPointIndex];
            worldPosition = ResolveMapPointWorldPosition(point);
            return true;
        }

        /// <summary>
        /// Whether the selected point is somewhere the debug teleport can
        /// actually put the player right now.
        ///
        /// Two things have to hold. Debug mode has to be on, because this is
        /// a debug tool and not a fast-travel system. And the point has to
        /// belong to the area the player is standing in: the other tab's
        /// points are a chart of somewhere else, and moving between areas is
        /// a scene transition rather than a `Motor.Teleport` - that is what
        /// <see cref="RequestAreaTravel"/> is for.
        /// </summary>
        public bool CanTeleportToSelectedMapPoint =>
            DebugTeleportEnabled &&
            IsOpen &&
            player.GameObject != null &&
            player.Motor != null &&
            TryGetSelectedMapPoint(
                out CityMapPointDescriptor point,
                out _) &&
            point.Area == currentArea;

        /// <summary>
        /// Sends the player to the exact point that is selected, rather than
        /// to the middle of whatever region contains it.
        ///
        /// This is the half the whole-lot teleport never had. A precinct like
        /// the cemetery or the yards is one entry in
        /// <see cref="MapObjects"/> covering a whole area, so confirming it
        /// could only ever mean "somewhere in there". A map point is a
        /// coordinate, and this puts the player on it.
        ///
        /// The arrival is still clamped to walkable ground, exactly as the
        /// area teleport clamps its own: a point is a place the chart draws,
        /// not a promise that a capsule fits there. A point that cannot be
        /// clamped is refused and logged rather than dropping the hero into
        /// scenery.
        /// </summary>
        public bool ConfirmMapPointTeleport()
        {
            if (!CanTeleportToSelectedMapPoint ||
                !TryGetSelectedMapPoint(
                    out CityMapPointDescriptor point,
                    out Vector3 worldPosition))
            {
                return false;
            }

            if (!TryClampToWalkableGround(
                    worldPosition,
                    out Vector3 destination))
            {
                RetroAudio.Play(RetroSfxId.UiCancel);
                GameLog.Warning(
                    "map",
                    "map_point_teleport_unreachable",
                    GameLog.Field("point_id", point.StableId),
                    GameLog.Field("world_x", worldPosition.x),
                    GameLog.Field("world_z", worldPosition.z));
                return false;
            }

            Close(false, "map_point_teleport");
            player.Motor.Teleport(destination);
            RefreshPath("map_point_teleport");
            RetroAudio.Play(RetroSfxId.UiConfirm);
            GameLog.Info(
                "map",
                "map_point_teleported",
                GameLog.Field("area", point.Area.ToString()),
                GameLog.Field("point_id", point.StableId),
                GameLog.Field("x", destination.x),
                GameLog.Field("z", destination.z));
            return true;
        }

        public Vector3 ResolveMapPointWorldPosition(
            CityMapPointDescriptor point)
        {
            return point.Kind == CityMapPointKind.Player &&
                   point.Area == currentArea &&
                   player.GameObject != null
                ? PlayerWorldPosition
                : point.WorldPosition;
        }

        private void MoveMapPointSelection(int delta)
        {
            IReadOnlyList<CityMapPointDescriptor> points = ActiveMapPoints;
            if (!MapPointInspectionEnabled || points.Count == 0 || delta == 0)
            {
                selectedMapPointIndex = -1;
                return;
            }

            int next = selectedMapPointIndex < 0
                ? delta > 0 ? 0 : points.Count - 1
                : (selectedMapPointIndex + Math.Sign(delta)) % points.Count;
            if (next < 0)
            {
                next += points.Count;
            }

            SelectMapPoint(next);
        }

        private void ResetMapPointInspection()
        {
            MapPointInspectionEnabled = false;
            selectedMapPointIndex = -1;
            mapPointFocusRevision++;
        }

        private void RebuildMapPointCatalogs()
        {
            cityMapPoints.Clear();
            mountainMapPoints.Clear();
            selectedMapPointIndex = -1;
            mapPointFocusRevision++;

            BuildCityMapPoints();
            BuildMountainMapPoints();
        }

        private void BuildCityMapPoints()
        {
            if (Layout == null)
            {
                return;
            }

            var claimedLots = new HashSet<int>();
            if (currentArea == GameAreaId.City && player.GameObject != null)
            {
                AddMapPoint(
                    cityMapPoints,
                    "city:player",
                    GameAreaId.City,
                    CityMapPointKind.Player,
                    LocalizationService.Get("map.player"),
                    PlayerWorldPosition,
                    40,
                    new Vector2(17f, 17f));
            }

            for (int index = 0; index < bars.Count; index++)
            {
                BuildingLot bar = bars[index];
                int lotIndex = FindMapObjectIndex(bar);
                if (lotIndex >= 0 && !claimedLots.Add(lotIndex))
                {
                    continue;
                }

                AddMapPoint(
                    cityMapPoints,
                    "city:bar:" + bar.BarId,
                    GameAreaId.City,
                    CityMapPointKind.Bar,
                    GetBarLabel(index),
                    bar.ReturnPosition,
                    20,
                    new Vector2(17f, 17f));
            }

            AddSpecialLotPoint(
                PlayerHome,
                "city:home",
                CityMapPointKind.Home,
                LocalizationService.Get("map.home"),
                30,
                new Vector2(17f, 17f),
                claimedLots);
            AddSpecialLotPoint(
                Supermarket,
                "city:supermarket",
                CityMapPointKind.Supermarket,
                GetSupermarketLabel(),
                30,
                new Vector2(19f, 19f),
                claimedLots);

            for (int index = 0; index < pointsOfInterest.Count; index++)
            {
                CityMapPointOfInterest point = pointsOfInterest[index];
                int lotIndex = FindMapObjectIndex(point.LotCell);
                if (lotIndex >= 0 && !claimedLots.Add(lotIndex))
                {
                    continue;
                }

                AddMapPoint(
                    cityMapPoints,
                    "city:poi:" + point.StableId,
                    GameAreaId.City,
                    CityMapPointKind.PointOfInterest,
                    GetPointOfInterestLabel(index),
                    point.WorldPosition,
                    10,
                    new Vector2(17f, 17f));
            }

            for (int index = 0; index < BusOverlay.Stops.Count; index++)
            {
                CityMapBusStopMarker stop = BusOverlay.Stops[index];
                AddMapPoint(
                    cityMapPoints,
                    "city:bus-stop:" + stop.StableId,
                    GameAreaId.City,
                    CityMapPointKind.BusStop,
                    GetBusStopLabel(index),
                    stop.WorldPosition,
                    15,
                    new Vector2(17f, 17f));
            }

            if (MountainBoundaryPlan != null &&
                MountainBoundaryPlan.IsEnabled &&
                MountainBoundaryPlan.HasTunnel)
            {
                AddMapPoint(
                    cityMapPoints,
                    "city:mountain-tunnel",
                    GameAreaId.City,
                    CityMapPointKind.Tunnel,
                    LocalizationService.Get("map.mountain.tunnel"),
                    MountainBoundaryPlan.Tunnel.PortalGroundCenter,
                    30,
                    new Vector2(23f, 21f));
            }

            if (TryGetSeacoastPartBounds(
                    CitySeacoastPartKind.Hut,
                    out Bounds hutBounds))
            {
                AddMapPoint(
                    cityMapPoints,
                    "city:boat-station",
                    GameAreaId.City,
                    CityMapPointKind.BoatStation,
                    LocalizationService.Get("map.seacoast.boat_station"),
                    hutBounds.center,
                    30,
                    new Vector2(21f, 21f));
            }

            for (int index = 0; index < mapAreaTargets.Count; index++)
            {
                CityMapAreaTarget target = mapAreaTargets[index];
                Rect areaBounds = CreateAreaWorldXZBounds(target.Region);
                AddMapPoint(
                    cityMapPoints,
                    "city:open-area:" + target.Region.AreaId,
                    GameAreaId.City,
                    CityMapPointKind.OpenArea,
                    GetMapObjectLabel(target.SelectionIndex),
                    target.ArrivalPosition,
                    -10,
                    Vector2.zero,
                    areaBounds,
                    true);
            }

            // Keep every legacy debug destination available, but put
            // anonymous lots after the useful named markers in key cycling.
            for (int index = 0; index < MapObjects.Count; index++)
            {
                if (claimedLots.Contains(index))
                {
                    continue;
                }

                BuildingLot lot = MapObjects[index];
                Rect bounds = CreateLotWorldXZBounds(lot);
                AddMapPoint(
                    cityMapPoints,
                    $"city:lot:{lot.Cell.x}:{lot.Cell.y}",
                    GameAreaId.City,
                    CityMapPointKind.MapObject,
                    GetMapObjectLabel(index),
                    lot.Center,
                    0,
                    Vector2.zero,
                    bounds,
                    true);
            }
        }

        private void BuildMountainMapPoints()
        {
            if (mountainRoadOverlay.IsEmpty)
            {
                return;
            }

            if (currentArea == GameAreaId.MountainRoad &&
                player.GameObject != null)
            {
                AddMapPoint(
                    mountainMapPoints,
                    "mountain-road:player",
                    GameAreaId.MountainRoad,
                    CityMapPointKind.Player,
                    LocalizationService.Get("map.player"),
                    PlayerWorldPosition,
                    40,
                    new Vector2(17f, 17f));
            }

            AddMapPoint(
                mountainMapPoints,
                "mountain-road:tunnel-exit",
                GameAreaId.MountainRoad,
                CityMapPointKind.Tunnel,
                LocalizationService.Get("map.mountain_road.tunnel_exit"),
                mountainRoadOverlay.TunnelPosition,
                30,
                new Vector2(20f, 18f));

            for (int index = 0;
                 index < mountainRoadOverlay.HairpinPositions.Count;
                 index++)
            {
                AddMapPoint(
                    mountainMapPoints,
                    $"mountain-road:hairpin:{index + 1:00}",
                    GameAreaId.MountainRoad,
                    CityMapPointKind.Hairpin,
                    string.Format(
                        LocalizationService.Get(
                            "map.mountain_road.hairpin"),
                        index + 1),
                    mountainRoadOverlay.HairpinPositions[index],
                    30,
                    new Vector2(15f, 15f));
            }

            if (mountainRoadOverlay.HasBridge)
            {
                AddMapPoint(
                    mountainMapPoints,
                    "mountain-road:bridge",
                    GameAreaId.MountainRoad,
                    CityMapPointKind.Bridge,
                    LocalizationService.Get("map.mountain_road.bridge"),
                    mountainRoadOverlay.BridgePosition,
                    30,
                    new Vector2(30f, 22f));
            }

            AddMapPoint(
                mountainMapPoints,
                "mountain-road:plateau",
                GameAreaId.MountainRoad,
                CityMapPointKind.Plateau,
                LocalizationService.Get("map.mountain_road.plateau"),
                mountainRoadOverlay.EndpointPosition,
                30,
                Vector2.zero,
                mountainRoadOverlay.PlateauBounds,
                true);

            for (int index = 0;
                 index < mountainRoadOverlay.TerminalLandmarks.Count;
                 index++)
            {
                MountainRoadTerminalLandmark landmark =
                    mountainRoadOverlay.TerminalLandmarks[index];
                CityMapPointKind kind = landmark.Kind ==
                                        MountainRoadTerminalLandmarkKind.Cafe
                    ? CityMapPointKind.Cafe
                    : CityMapPointKind.Cableway;
                AddMapPoint(
                    mountainMapPoints,
                    "mountain-road:terminal:" + index,
                    GameAreaId.MountainRoad,
                    kind,
                    LocalizationService.Get(landmark.LocalizationKey),
                    landmark.Position,
                    30,
                    new Vector2(24f, 24f));
            }
        }

        private void AddSpecialLotPoint(
            BuildingLot lot,
            string stableId,
            CityMapPointKind kind,
            string label,
            int priority,
            Vector2 screenHitSize,
            ISet<int> claimedLots)
        {
            if (lot == null)
            {
                return;
            }

            int lotIndex = FindMapObjectIndex(lot);
            if (lotIndex >= 0 && !claimedLots.Add(lotIndex))
            {
                return;
            }

            AddMapPoint(
                cityMapPoints,
                stableId,
                GameAreaId.City,
                kind,
                label,
                lot.Center,
                priority,
                screenHitSize);
        }

        private static void AddMapPoint(
            ICollection<CityMapPointDescriptor> destination,
            string stableId,
            GameAreaId area,
            CityMapPointKind kind,
            string label,
            Vector3 position,
            int priority,
            Vector2 screenHitSize,
            Rect worldXZHitBounds = default,
            bool usesWorldHitBounds = false)
        {
            destination.Add(
                new CityMapPointDescriptor(
                    stableId,
                    area,
                    kind,
                    label,
                    position,
                    priority,
                    screenHitSize,
                    worldXZHitBounds,
                    usesWorldHitBounds));
        }

        private int FindMapObjectIndex(Vector2Int cell)
        {
            for (int index = 0; index < MapObjects.Count; index++)
            {
                if (MapObjects[index].Cell == cell)
                {
                    return index;
                }
            }

            return -1;
        }

        private bool TryGetSeacoastPartBounds(
            CitySeacoastPartKind kind,
            out Bounds bounds)
        {
            if (SeacoastPlan == null)
            {
                bounds = default;
                return false;
            }

            Vector3 minimum = new Vector3(
                float.PositiveInfinity,
                float.PositiveInfinity,
                float.PositiveInfinity);
            Vector3 maximum = new Vector3(
                float.NegativeInfinity,
                float.NegativeInfinity,
                float.NegativeInfinity);
            for (int index = 0; index < SeacoastPlan.Parts.Count; index++)
            {
                CitySeacoastPartDescriptor part = SeacoastPlan.Parts[index];
                if (part.Kind != kind)
                {
                    continue;
                }

                Vector3 extents = part.Rotation * part.Size;
                extents = new Vector3(
                    Mathf.Abs(extents.x),
                    Mathf.Abs(extents.y),
                    Mathf.Abs(extents.z)) * 0.5f;
                minimum = Vector3.Min(minimum, part.Center - extents);
                maximum = Vector3.Max(maximum, part.Center + extents);
            }

            if (float.IsPositiveInfinity(minimum.x))
            {
                bounds = default;
                return false;
            }

            bounds = new Bounds(
                (minimum + maximum) * 0.5f,
                maximum - minimum);
            return true;
        }

        private static Rect CreateLotWorldXZBounds(BuildingLot lot)
        {
            return new Rect(
                lot.Center.x - lot.Size.x * 0.5f,
                lot.Center.z - lot.Size.y * 0.5f,
                lot.Size.x,
                lot.Size.y);
        }

        private static Rect CreateAreaWorldXZBounds(
            CityMapAreaRegion region)
        {
            float minimumX = float.PositiveInfinity;
            float minimumZ = float.PositiveInfinity;
            float maximumX = float.NegativeInfinity;
            float maximumZ = float.NegativeInfinity;
            IncludeWorldXZBounds(
                region.LandBounds,
                ref minimumX,
                ref minimumZ,
                ref maximumX,
                ref maximumZ);
            IncludeWorldXZBounds(
                region.WaterBounds,
                ref minimumX,
                ref minimumZ,
                ref maximumX,
                ref maximumZ);
            return float.IsPositiveInfinity(minimumX)
                ? new Rect(
                    region.Gates.Count > 0
                        ? region.Gates[0].center
                        : Vector2.zero,
                    Vector2.one)
                : Rect.MinMaxRect(
                    minimumX,
                    minimumZ,
                    maximumX,
                    maximumZ);
        }

        private static void IncludeWorldXZBounds(
            IReadOnlyList<Rect> source,
            ref float minimumX,
            ref float minimumZ,
            ref float maximumX,
            ref float maximumZ)
        {
            for (int index = 0; index < source.Count; index++)
            {
                Rect bounds = source[index];
                minimumX = Mathf.Min(minimumX, bounds.xMin);
                minimumZ = Mathf.Min(minimumZ, bounds.yMin);
                maximumX = Mathf.Max(maximumX, bounds.xMax);
                maximumZ = Mathf.Max(maximumZ, bounds.yMax);
            }
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

        private static bool WasMapPointInspectionTogglePressed()
        {
            Keyboard keyboard = Keyboard.current;
            if (keyboard != null && keyboard.cKey.wasPressedThisFrame)
            {
                return true;
            }

            Gamepad gamepad = Gamepad.current;
            return gamepad != null &&
                   gamepad.buttonNorth.wasPressedThisFrame;
        }

        private static bool IsKnownArea(GameAreaId area)
        {
            return area == GameAreaId.City ||
                   area == GameAreaId.MountainRoad;
        }
    }
}

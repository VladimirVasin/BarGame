using System;
using System.Collections.Generic;
using UnityEngine;

namespace BarPromenade
{
    public enum BarInteriorZoneKind
    {
        Entrance = 0,
        MainHall = 1,
        BoothLounge = 2,
        Stage = 3,
        ActivityBay = 4,
        BarService = 5,
        DanceFloor = 6
    }

    public enum BarInteriorPathKind
    {
        Main = 0,
        CrossAisle = 1,
        LoungeAccess = 2,
        ActivityAccess = 3
    }

    public enum BarInteriorFurnitureKind
    {
        Counter = 0,
        BackBar = 1,
        Booth = 2,
        HighTopTable = 3,
        Stage = 4,
        ActivityFixture = 5,
        CoatRack = 6,
        CounterReturn = 7,
        PubTable = 8
    }

    public enum BarNpcRole
    {
        Bartender = 0,
        SeatedPatron = 1,
        StandingPatron = 2,
        Performer = 3,
        Walker = 4
    }

    public enum BarInteriorLightKind
    {
        CounterPendant = 0,
        BoothPool = 1,
        StageKey = 2,
        StageRim = 3,
        EntryFill = 4
    }

    public enum BarInteriorAudioKind
    {
        Music = 0,
        CrowdBed = 1,
        Stage = 2,
        BarService = 3,
        Activity = 4
    }

    public readonly struct BarInteriorZone
    {
        public BarInteriorZone(
            string id,
            BarInteriorZoneKind kind,
            Rect bounds)
        {
            Id = id;
            Kind = kind;
            Bounds = bounds;
        }

        public string Id { get; }
        public BarInteriorZoneKind Kind { get; }
        public Rect Bounds { get; }
    }

    public readonly struct BarInteriorPath
    {
        public BarInteriorPath(
            string id,
            BarInteriorPathKind kind,
            Rect bounds,
            float clearance)
        {
            Id = id;
            Kind = kind;
            Bounds = bounds;
            Clearance = clearance;
        }

        public string Id { get; }
        public BarInteriorPathKind Kind { get; }
        public Rect Bounds { get; }
        public float Clearance { get; }
        public bool IsPrimary => Kind == BarInteriorPathKind.Main;
    }

    public readonly struct BarInteriorFurnitureFootprint
    {
        public BarInteriorFurnitureFootprint(
            string id,
            BarInteriorFurnitureKind kind,
            Rect bounds,
            float height,
            bool blocksMovement,
            BarActivityKind activity = BarActivityKind.None)
        {
            Id = id;
            Kind = kind;
            Bounds = bounds;
            Height = height;
            BlocksMovement = blocksMovement;
            Activity = activity;
        }

        public string Id { get; }
        public BarInteriorFurnitureKind Kind { get; }
        public Rect Bounds { get; }
        public float Height { get; }
        public bool BlocksMovement { get; }
        public BarActivityKind Activity { get; }
        public Vector3 Center =>
            new Vector3(Bounds.center.x, Height * 0.5f, Bounds.center.y);
        public Vector3 Size =>
            new Vector3(Bounds.width, Height, Bounds.height);
    }

    public readonly struct BarNpcAnchor
    {
        public BarNpcAnchor(
            string id,
            BarNpcRole role,
            Vector3 position,
            float yawDegrees,
            int visualVariant,
            float animationPhase,
            string routeId = "")
        {
            Id = id;
            Role = role;
            Position = position;
            YawDegrees = yawDegrees;
            VisualVariant = visualVariant;
            AnimationPhase = animationPhase;
            RouteId = routeId ?? string.Empty;
        }

        public string Id { get; }
        public BarNpcRole Role { get; }
        public Vector3 Position { get; }
        public float YawDegrees { get; }
        public int VisualVariant { get; }
        public float AnimationPhase { get; }
        public string RouteId { get; }
        public bool IsMobile => Role == BarNpcRole.Walker;
    }

    public readonly struct BarInteriorLightAnchor
    {
        public BarInteriorLightAnchor(
            string id,
            BarInteriorLightKind kind,
            Vector3 position,
            Vector3 direction,
            Color color,
            float intensity,
            float range,
            float spotAngle)
        {
            Id = id;
            Kind = kind;
            Position = position;
            Direction = direction;
            Color = color;
            Intensity = intensity;
            Range = range;
            SpotAngle = spotAngle;
        }

        public string Id { get; }
        public BarInteriorLightKind Kind { get; }
        public Vector3 Position { get; }
        public Vector3 Direction { get; }
        public Color Color { get; }
        public float Intensity { get; }
        public float Range { get; }
        public float SpotAngle { get; }
        public bool IsSpot => SpotAngle > 0f;
    }

    public readonly struct BarInteriorAudioAnchor
    {
        public BarInteriorAudioAnchor(
            string id,
            BarInteriorAudioKind kind,
            Vector3 position,
            float radius,
            float gain)
        {
            Id = id;
            Kind = kind;
            Position = position;
            Radius = radius;
            Gain = gain;
        }

        public string Id { get; }
        public BarInteriorAudioKind Kind { get; }
        public Vector3 Position { get; }
        public float Radius { get; }
        public float Gain { get; }
    }

    public sealed class BarInteriorLayoutPlan
    {
        public BarInteriorLayoutPlan(
            int citySeed,
            uint stableSeed,
            string barId,
            BarActivityKind activity,
            Vector2 roomSize,
            float roomHeight,
            float wallThickness,
            Rect walkableBounds,
            Vector3 playerSpawn,
            Vector3 exitPosition,
            Vector3 exitApproachPosition,
            Vector3 exitTriggerSize,
            Vector3 counterPosition,
            Vector3 counterSize,
            Vector3 counterStationPosition,
            Vector3 counterStationTriggerSize,
            Vector3 activityStationPosition,
            Vector3 activityStationTriggerSize,
            IReadOnlyList<BarInteriorZone> zones,
            IReadOnlyList<BarInteriorPath> paths,
            IReadOnlyList<BarInteriorFurnitureFootprint> furnitureFootprints,
            IReadOnlyList<BarNpcAnchor> npcAnchors,
            IReadOnlyList<BarInteriorLightAnchor> lightAnchors,
            IReadOnlyList<BarInteriorAudioAnchor> audioAnchors)
        {
            CitySeed = citySeed;
            StableSeed = stableSeed;
            BarId = barId ?? string.Empty;
            Activity = activity;
            RoomSize = roomSize;
            RoomHeight = roomHeight;
            WallThickness = wallThickness;
            RoomBounds = Rect.MinMaxRect(
                roomSize.x * -0.5f,
                roomSize.y * -0.5f,
                roomSize.x * 0.5f,
                roomSize.y * 0.5f);
            WalkableBounds = walkableBounds;
            PlayerSpawn = playerSpawn;
            ExitPosition = exitPosition;
            ExitApproachPosition = exitApproachPosition;
            ExitTriggerSize = exitTriggerSize;
            CounterPosition = counterPosition;
            CounterSize = counterSize;
            CounterStationPosition = counterStationPosition;
            CounterStationTriggerSize = counterStationTriggerSize;
            ActivityStationPosition = activityStationPosition;
            ActivityStationTriggerSize = activityStationTriggerSize;
            Zones = Copy(zones, nameof(zones));
            Paths = Copy(paths, nameof(paths));
            FurnitureFootprints = Copy(
                furnitureFootprints,
                nameof(furnitureFootprints));
            NpcAnchors = Copy(npcAnchors, nameof(npcAnchors));
            LightAnchors = Copy(lightAnchors, nameof(lightAnchors));
            AudioAnchors = Copy(audioAnchors, nameof(audioAnchors));
        }

        public int CitySeed { get; }
        public uint StableSeed { get; }
        public string BarId { get; }
        public BarActivityKind Activity { get; }

        /// <summary>
        /// The district whose identity this interior wears. Assigned
        /// after construction by the planner (the ctor predates the
        /// district split); always normalized to a bar district.
        /// </summary>
        public CityDistrictKind District { get; private set; } =
            BarDistrictIdentityCatalog.FallbackDistrict;

        /// <summary>The resolved per-district character sheet.</summary>
        public BarDistrictIdentity DistrictIdentity =>
            BarDistrictIdentityCatalog.Get(District);

        internal void AssignDistrict(CityDistrictKind district)
        {
            District = BarDistrictIdentityCatalog.Normalize(district);
        }
        public Vector2 RoomSize { get; }
        public float RoomHeight { get; }
        public float WallThickness { get; }
        public Rect RoomBounds { get; }
        public Rect WalkableBounds { get; }
        public Vector3 PlayerSpawn { get; }
        public Vector3 ExitPosition { get; }
        public Vector3 ExitApproachPosition { get; }
        public Vector3 ExitTriggerSize { get; }
        public Vector3 CounterPosition { get; }
        public Vector3 CounterSize { get; }
        public Vector3 CounterStationPosition { get; }
        public Vector3 CounterStationTriggerSize { get; }
        public Vector3 ActivityStationPosition { get; }
        public Vector3 ActivityStationTriggerSize { get; }
        public IReadOnlyList<BarInteriorZone> Zones { get; }
        public IReadOnlyList<BarInteriorPath> Paths { get; }
        public IReadOnlyList<BarInteriorFurnitureFootprint>
            FurnitureFootprints { get; }
        public IReadOnlyList<BarNpcAnchor> NpcAnchors { get; }
        public IReadOnlyList<BarInteriorLightAnchor> LightAnchors { get; }
        public IReadOnlyList<BarInteriorAudioAnchor> AudioAnchors { get; }

        public bool TryGetZone(
            BarInteriorZoneKind kind,
            out BarInteriorZone zone)
        {
            for (int index = 0; index < Zones.Count; index++)
            {
                if (Zones[index].Kind == kind)
                {
                    zone = Zones[index];
                    return true;
                }
            }

            zone = default;
            return false;
        }

        public bool TryGetFurniture(
            BarInteriorFurnitureKind kind,
            out BarInteriorFurnitureFootprint footprint)
        {
            for (int index = 0;
                 index < FurnitureFootprints.Count;
                 index++)
            {
                if (FurnitureFootprints[index].Kind == kind)
                {
                    footprint = FurnitureFootprints[index];
                    return true;
                }
            }

            footprint = default;
            return false;
        }

        public bool TryGetFurniture(
            string id,
            out BarInteriorFurnitureFootprint footprint)
        {
            for (int index = 0;
                 index < FurnitureFootprints.Count;
                 index++)
            {
                if (string.Equals(
                        FurnitureFootprints[index].Id,
                        id,
                        StringComparison.Ordinal))
                {
                    footprint = FurnitureFootprints[index];
                    return true;
                }
            }

            footprint = default;
            return false;
        }

        private static IReadOnlyList<T> Copy<T>(
            IReadOnlyList<T> source,
            string parameterName)
        {
            if (source == null)
            {
                throw new ArgumentNullException(parameterName);
            }

            var copy = new T[source.Count];
            for (int index = 0; index < source.Count; index++)
            {
                copy[index] = source[index];
            }

            return Array.AsReadOnly(copy);
        }
    }
}

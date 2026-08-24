using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using UnityEngine;

namespace BarPromenade
{
    public enum MountainRoadTerminalLandmarkKind
    {
        Cafe = 0,
        Cableway = 1
    }

    public enum MountainCablewayNodeKind
    {
        LowerStation = 0,
        Support = 1,
        UpperTurn = 2
    }

    public readonly struct MountainRoadTerminalRect
    {
        internal MountainRoadTerminalRect(
            Vector3 center,
            Vector3 right,
            Vector3 forward,
            Vector2 size)
        {
            Center = center;
            Right = right.normalized;
            Forward = forward.normalized;
            Size = size;
        }

        public Vector3 Center { get; }
        public Vector3 Right { get; }
        public Vector3 Forward { get; }
        public Vector2 Size { get; }
        public Vector2 HalfSize => Size * 0.5f;

        public bool ContainsXZ(Vector3 point, float inset = 0f)
        {
            Vector3 offset = point - Center;
            float halfRight = Mathf.Max(0f, Size.x * 0.5f - inset);
            float halfForward = Mathf.Max(0f, Size.y * 0.5f - inset);
            return Mathf.Abs(Vector3.Dot(offset, Right)) <= halfRight &&
                   Mathf.Abs(Vector3.Dot(offset, Forward)) <= halfForward;
        }

        public Vector3 GetCorner(int index)
        {
            float right = (index & 1) == 0 ? -1f : 1f;
            float forward = (index & 2) == 0 ? -1f : 1f;
            return Center +
                   Right * (right * Size.x * 0.5f) +
                   Forward * (forward * Size.y * 0.5f);
        }
    }

    public readonly struct MountainRoadTerminalLandmark
    {
        internal MountainRoadTerminalLandmark(
            string stableId,
            MountainRoadTerminalLandmarkKind kind,
            Vector3 position,
            string localizationKey)
        {
            StableId = stableId ?? string.Empty;
            Kind = kind;
            Position = position;
            LocalizationKey = localizationKey ?? string.Empty;
        }

        public string StableId { get; }
        public MountainRoadTerminalLandmarkKind Kind { get; }
        public Vector3 Position { get; }
        public string LocalizationKey { get; }
    }

    public sealed class MountainRoadVehicleApronPlan
    {
        internal MountainRoadVehicleApronPlan(
            Vector3 center,
            Vector3 entryCenter,
            Vector3 forward,
            float entryWidth,
            float turningRadius)
        {
            Center = center;
            EntryCenter = entryCenter;
            Forward = forward.normalized;
            Right = Vector3.Cross(Vector3.up, Forward).normalized;
            EntryWidth = entryWidth;
            TurningRadius = turningRadius;
        }

        public Vector3 Center { get; }
        public Vector3 EntryCenter { get; }
        public Vector3 Forward { get; }
        public Vector3 Right { get; }
        public float EntryWidth { get; }
        public float TurningRadius { get; }
    }

    public sealed class MountainRoadCafePlan
    {
        private readonly ReadOnlyCollection<Vector2> footprintXZ;

        internal MountainRoadCafePlan(
            string stableId,
            Vector3 center,
            Vector3 right,
            Vector3 forward,
            float floorY,
            float height,
            float chamferDepth,
            Vector3 doorCenter,
            float doorWidth,
            IList<Vector2> sourceFootprintXZ)
        {
            StableId = stableId ?? string.Empty;
            Center = center;
            Right = right.normalized;
            Forward = forward.normalized;
            FloorY = floorY;
            Height = height;
            ChamferDepth = chamferDepth;
            DoorCenter = doorCenter;
            DoorForward = -Forward;
            DoorWidth = doorWidth;
            footprintXZ = new ReadOnlyCollection<Vector2>(
                new List<Vector2>(sourceFootprintXZ));
        }

        public string StableId { get; }
        public Vector3 Center { get; }
        public Vector3 Right { get; }
        public Vector3 Forward { get; }
        public float FloorY { get; }
        public float Height { get; }
        public float ChamferDepth { get; }
        public Vector3 DoorCenter { get; }
        public Vector3 DoorForward { get; }
        public float DoorWidth { get; }
        public IReadOnlyList<Vector2> FootprintXZ => footprintXZ;

        public bool ContainsInterior(Vector3 point, float edgeInset = 0.18f)
        {
            if (point.y < FloorY - 0.2f ||
                point.y > FloorY + Height + 0.2f)
            {
                return false;
            }

            Vector2 tested = new Vector2(point.x, point.z);
            if (!Contains(footprintXZ, tested))
            {
                return false;
            }

            if (edgeInset <= 0f)
            {
                return true;
            }

            float insetSquared = edgeInset * edgeInset;
            for (int index = 0; index < footprintXZ.Count; index++)
            {
                Vector2 first = footprintXZ[index];
                Vector2 second = footprintXZ[
                    (index + 1) % footprintXZ.Count];
                if (DistanceToSegmentSquared(tested, first, second) <
                    insetSquared)
                {
                    return false;
                }
            }

            return true;
        }

        private static bool Contains(
            IReadOnlyList<Vector2> polygon,
            Vector2 point)
        {
            bool inside = false;
            for (int first = 0, second = polygon.Count - 1;
                 first < polygon.Count;
                 second = first++)
            {
                Vector2 a = polygon[first];
                Vector2 b = polygon[second];
                bool crosses = (a.y > point.y) != (b.y > point.y) &&
                    point.x < (b.x - a.x) *
                    (point.y - a.y) /
                    ((b.y - a.y) + Mathf.Epsilon) + a.x;
                if (crosses)
                {
                    inside = !inside;
                }
            }

            return inside;
        }

        private static float DistanceToSegmentSquared(
            Vector2 point,
            Vector2 first,
            Vector2 second)
        {
            Vector2 segment = second - first;
            float denominator = segment.sqrMagnitude;
            float t = denominator <= 0.000001f
                ? 0f
                : Mathf.Clamp01(Vector2.Dot(point - first, segment) /
                                denominator);
            return (point - Vector2.Lerp(first, second, t)).sqrMagnitude;
        }
    }

    public readonly struct MountainCablewayNodeDescriptor
    {
        internal MountainCablewayNodeDescriptor(
            string stableId,
            MountainCablewayNodeKind kind,
            float distance,
            Vector3 cableCenter,
            Vector3 groundPosition)
        {
            StableId = stableId ?? string.Empty;
            Kind = kind;
            Distance = distance;
            CableCenter = cableCenter;
            GroundPosition = groundPosition;
        }

        public string StableId { get; }
        public MountainCablewayNodeKind Kind { get; }
        public float Distance { get; }
        public Vector3 CableCenter { get; }
        public Vector3 GroundPosition { get; }
    }

    public readonly struct MountainCablewayCabinDescriptor
    {
        internal MountainCablewayCabinDescriptor(
            string stableId,
            float phase)
        {
            StableId = stableId ?? string.Empty;
            Phase = phase;
        }

        public string StableId { get; }
        public float Phase { get; }
    }

    public sealed class MountainRoadCablewayPlan
    {
        public const float CabinRoofDrop = 1.08f;

        private readonly ReadOnlyCollection<MountainCablewayNodeDescriptor>
            nodes;
        private readonly ReadOnlyCollection<MountainCablewayCabinDescriptor>
            cabins;

        internal MountainRoadCablewayPlan(
            string stableId,
            MountainRoadTerminalRect stationArea,
            Vector3 lineForward,
            Vector3 lineRight,
            float trackSeparation,
            float lineLength,
            float cabinSpeed,
            Vector3 cabinSize,
            IList<MountainCablewayNodeDescriptor> sourceNodes,
            IList<MountainCablewayCabinDescriptor> sourceCabins,
            string upperOccluderStableId)
        {
            StableId = stableId ?? string.Empty;
            StationArea = stationArea;
            LineForward = lineForward.normalized;
            LineRight = lineRight.normalized;
            TrackSeparation = trackSeparation;
            LineLength = lineLength;
            CabinSpeed = cabinSpeed;
            CabinSize = cabinSize;
            nodes = new ReadOnlyCollection<MountainCablewayNodeDescriptor>(
                new List<MountainCablewayNodeDescriptor>(sourceNodes));
            cabins = new ReadOnlyCollection<MountainCablewayCabinDescriptor>(
                new List<MountainCablewayCabinDescriptor>(sourceCabins));
            UpperOccluderStableId = upperOccluderStableId ?? string.Empty;
        }

        public string StableId { get; }
        public MountainRoadTerminalRect StationArea { get; }
        public Vector3 LineForward { get; }
        public Vector3 LineRight { get; }
        public float TrackSeparation { get; }
        public float LineLength { get; }
        public float CabinSpeed { get; }
        public Vector3 CabinSize { get; }
        public IReadOnlyList<MountainCablewayNodeDescriptor> Nodes => nodes;
        public IReadOnlyList<MountainCablewayCabinDescriptor> Cabins => cabins;
        public string UpperOccluderStableId { get; }
        public Vector3 LowerCableCenter => nodes[0].CableCenter;
        public Vector3 UpperCableCenter => nodes[nodes.Count - 1].CableCenter;
        public float TurnRadius => TrackSeparation * 0.5f;
        public float CabinAttachmentToBottom =>
            CabinRoofDrop + CabinSize.y;
        public float LoopLength => LineLength * 2f +
                                   Mathf.PI * TrackSeparation;

        public bool ContainsClearanceXZ(Vector2 point, float clearance)
        {
            Vector2 start = new Vector2(
                LowerCableCenter.x,
                LowerCableCenter.z);
            Vector2 end = new Vector2(
                UpperCableCenter.x,
                UpperCableCenter.z);
            Vector2 segment = end - start;
            float denominator = segment.sqrMagnitude;
            float t = denominator <= 0.000001f
                ? 0f
                : Mathf.Clamp01(Vector2.Dot(point - start, segment) /
                                denominator);
            float radius = TrackSeparation * 0.5f + clearance;
            return (point - Vector2.Lerp(start, end, t)).sqrMagnitude <=
                   radius * radius;
        }
    }

    public sealed class MountainRoadTerminalPlan
    {
        private readonly ReadOnlyCollection<MountainRoadTerminalLandmark>
            landmarks;

        internal MountainRoadTerminalPlan(
            MountainRoadVehicleApronPlan vehicleApron,
            MountainRoadCafePlan cafe,
            MountainRoadCablewayPlan cableway,
            IList<MountainRoadTerminalLandmark> sourceLandmarks)
        {
            VehicleApron = vehicleApron ??
                throw new ArgumentNullException(nameof(vehicleApron));
            Cafe = cafe ?? throw new ArgumentNullException(nameof(cafe));
            Cableway = cableway ??
                throw new ArgumentNullException(nameof(cableway));
            landmarks = new ReadOnlyCollection<MountainRoadTerminalLandmark>(
                new List<MountainRoadTerminalLandmark>(sourceLandmarks));
        }

        public MountainRoadVehicleApronPlan VehicleApron { get; }
        public MountainRoadCafePlan Cafe { get; }
        public MountainRoadCablewayPlan Cableway { get; }
        public IReadOnlyList<MountainRoadTerminalLandmark> Landmarks =>
            landmarks;

        public bool IsSheltered(Vector3 position)
        {
            return Cafe.ContainsInterior(position) ||
                   Cableway.StationArea.ContainsXZ(position, 0.2f) &&
                   position.y >= Cableway.StationArea.Center.y - 0.3f &&
                   position.y <= Cableway.StationArea.Center.y + 5.4f;
        }
    }
}

using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using UnityEngine;

namespace BarPromenade
{
    public enum RoadFenceOpeningKind
    {
        BarEntrance = 0,
        ParkGate = 1
    }

    public readonly struct RoadFenceSegmentDescriptor :
        IEquatable<RoadFenceSegmentDescriptor>
    {
        internal RoadFenceSegmentDescriptor(
            Vector3 start,
            Vector3 end,
            Vector3 outwardNormal)
        {
            Start = start;
            End = end;
            OutwardNormal = outwardNormal;
        }

        public Vector3 Start { get; }
        public Vector3 End { get; }
        public Vector3 OutwardNormal { get; }
        public bool IsHorizontal => Mathf.Abs(End.x - Start.x) > 0f;
        public float Length => Vector3.Distance(Start, End);
        public Vector3 Center => (Start + End) * 0.5f;
        public float FixedCoordinate => IsHorizontal ? Start.z : Start.x;
        public float MinimumCoordinate => IsHorizontal ? Start.x : Start.z;
        public float MaximumCoordinate => IsHorizontal ? End.x : End.z;

        public bool Equals(RoadFenceSegmentDescriptor other)
        {
            return Start.Equals(other.Start) &&
                   End.Equals(other.End) &&
                   OutwardNormal.Equals(other.OutwardNormal);
        }

        public override bool Equals(object obj)
        {
            return obj is RoadFenceSegmentDescriptor other &&
                   Equals(other);
        }

        public override int GetHashCode()
        {
            unchecked
            {
                int hash = 17;
                hash = (hash * 31) + Start.GetHashCode();
                hash = (hash * 31) + End.GetHashCode();
                hash = (hash * 31) + OutwardNormal.GetHashCode();
                return hash;
            }
        }

        public static bool operator ==(
            RoadFenceSegmentDescriptor left,
            RoadFenceSegmentDescriptor right)
        {
            return left.Equals(right);
        }

        public static bool operator !=(
            RoadFenceSegmentDescriptor left,
            RoadFenceSegmentDescriptor right)
        {
            return !left.Equals(right);
        }
    }

    public readonly struct RoadFenceOpeningDescriptor :
        IEquatable<RoadFenceOpeningDescriptor>
    {
        internal RoadFenceOpeningDescriptor(
            string barId,
            Vector3 center,
            Vector3 outwardNormal,
            float width)
            : this(
                RoadFenceOpeningKind.BarEntrance,
                barId,
                center,
                outwardNormal,
                width)
        {
        }

        internal RoadFenceOpeningDescriptor(
            RoadFenceOpeningKind kind,
            string id,
            Vector3 center,
            Vector3 outwardNormal,
            float width)
        {
            Kind = kind;
            Id = id ?? string.Empty;
            Center = center;
            OutwardNormal = outwardNormal;
            Width = width;
        }

        public RoadFenceOpeningKind Kind { get; }
        public string Id { get; }
        public string BarId =>
            Kind == RoadFenceOpeningKind.BarEntrance
                ? Id
                : string.Empty;
        public string ParkGateId =>
            Kind == RoadFenceOpeningKind.ParkGate
                ? Id
                : string.Empty;
        public Vector3 Center { get; }
        public Vector3 OutwardNormal { get; }
        public float Width { get; }
        public bool IsHorizontal => Mathf.Abs(OutwardNormal.z) > 0f;
        public float FixedCoordinate => IsHorizontal ? Center.z : Center.x;
        public float MinimumCoordinate =>
            (IsHorizontal ? Center.x : Center.z) - (Width * 0.5f);
        public float MaximumCoordinate =>
            (IsHorizontal ? Center.x : Center.z) + (Width * 0.5f);

        public bool Equals(RoadFenceOpeningDescriptor other)
        {
            return Kind == other.Kind &&
                   string.Equals(
                       Id,
                       other.Id,
                       StringComparison.Ordinal) &&
                   Center.Equals(other.Center) &&
                   OutwardNormal.Equals(other.OutwardNormal) &&
                   Width.Equals(other.Width);
        }

        public override bool Equals(object obj)
        {
            return obj is RoadFenceOpeningDescriptor other &&
                   Equals(other);
        }

        public override int GetHashCode()
        {
            unchecked
            {
                int hash = 17;
                hash = (hash * 31) + (int)Kind;
                hash = (hash * 31) +
                       (Id == null
                           ? 0
                           : StringComparer.Ordinal.GetHashCode(Id));
                hash = (hash * 31) + Center.GetHashCode();
                hash = (hash * 31) + OutwardNormal.GetHashCode();
                hash = (hash * 31) + Width.GetHashCode();
                return hash;
            }
        }

        public static bool operator ==(
            RoadFenceOpeningDescriptor left,
            RoadFenceOpeningDescriptor right)
        {
            return left.Equals(right);
        }

        public static bool operator !=(
            RoadFenceOpeningDescriptor left,
            RoadFenceOpeningDescriptor right)
        {
            return !left.Equals(right);
        }
    }

    public sealed class RoadFencePlan
    {
        internal RoadFencePlan(
            IList<RoadFenceSegmentDescriptor> segments,
            IList<RoadFenceOpeningDescriptor> openings)
        {
            Segments =
                new ReadOnlyCollection<RoadFenceSegmentDescriptor>(
                    new List<RoadFenceSegmentDescriptor>(segments));
            var allOpenings =
                new List<RoadFenceOpeningDescriptor>(openings);
            var entranceOpenings =
                new List<RoadFenceOpeningDescriptor>();
            var parkGateOpenings =
                new List<RoadFenceOpeningDescriptor>();
            for (int index = 0; index < allOpenings.Count; index++)
            {
                RoadFenceOpeningDescriptor opening =
                    allOpenings[index];
                if (opening.Kind ==
                    RoadFenceOpeningKind.BarEntrance)
                {
                    entranceOpenings.Add(opening);
                }
                else if (opening.Kind ==
                         RoadFenceOpeningKind.ParkGate)
                {
                    parkGateOpenings.Add(opening);
                }
            }

            Openings =
                new ReadOnlyCollection<RoadFenceOpeningDescriptor>(
                    allOpenings);
            EntranceOpenings =
                new ReadOnlyCollection<RoadFenceOpeningDescriptor>(
                    entranceOpenings);
            ParkGateOpenings =
                new ReadOnlyCollection<RoadFenceOpeningDescriptor>(
                    parkGateOpenings);
        }

        public IReadOnlyList<RoadFenceSegmentDescriptor> Segments { get; }
        public IReadOnlyList<RoadFenceOpeningDescriptor> Openings { get; }
        public IReadOnlyList<RoadFenceOpeningDescriptor> EntranceOpenings
        {
            get;
        }
        public IReadOnlyList<RoadFenceOpeningDescriptor> ParkGateOpenings
        {
            get;
        }
    }
}

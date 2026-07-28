using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using UnityEngine;

namespace BarPromenade
{
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
        {
            BarId = barId;
            Center = center;
            OutwardNormal = outwardNormal;
            Width = width;
        }

        public string BarId { get; }
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
            return string.Equals(
                   BarId,
                       other.BarId,
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
                hash = (hash * 31) +
                       (BarId == null
                           ? 0
                           : StringComparer.Ordinal.GetHashCode(BarId));
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
            IList<RoadFenceOpeningDescriptor> entranceOpenings)
        {
            Segments =
                new ReadOnlyCollection<RoadFenceSegmentDescriptor>(
                    new List<RoadFenceSegmentDescriptor>(segments));
            EntranceOpenings =
                new ReadOnlyCollection<RoadFenceOpeningDescriptor>(
                    new List<RoadFenceOpeningDescriptor>(entranceOpenings));
        }

        public IReadOnlyList<RoadFenceSegmentDescriptor> Segments { get; }
        public IReadOnlyList<RoadFenceOpeningDescriptor> EntranceOpenings
        {
            get;
        }
    }
}

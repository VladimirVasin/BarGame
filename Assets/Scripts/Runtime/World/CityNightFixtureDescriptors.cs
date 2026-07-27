using System;
using UnityEngine;

namespace BarPromenade
{
    public enum StreetLampSide
    {
        Left = -1,
        Right = 1
    }

    public readonly struct StreetLampDescriptor :
        IEquatable<StreetLampDescriptor>
    {
        internal StreetLampDescriptor(
            RoadEdge edge,
            float edgeT,
            StreetLampSide side,
            Vector3 position,
            Vector3 forward)
        {
            Edge = edge;
            EdgeT = edgeT;
            Side = side;
            Position = position;
            Forward = forward;
        }

        public RoadEdge Edge { get; }
        public float EdgeT { get; }
        public StreetLampSide Side { get; }
        public Vector3 Position { get; }
        public Vector3 Forward { get; }

        public bool Equals(StreetLampDescriptor other)
        {
            return Edge == other.Edge &&
                   EdgeT.Equals(other.EdgeT) &&
                   Side == other.Side &&
                   Position == other.Position &&
                   Forward == other.Forward;
        }

        public override bool Equals(object obj)
        {
            return obj is StreetLampDescriptor other && Equals(other);
        }

        public override int GetHashCode()
        {
            unchecked
            {
                int hash = 17;
                hash = (hash * 31) + Edge.GetHashCode();
                hash = (hash * 31) + EdgeT.GetHashCode();
                hash = (hash * 31) + (int)Side;
                hash = (hash * 31) + Position.GetHashCode();
                hash = (hash * 31) + Forward.GetHashCode();
                return hash;
            }
        }

        public static bool operator ==(
            StreetLampDescriptor left,
            StreetLampDescriptor right)
        {
            return left.Equals(right);
        }

        public static bool operator !=(
            StreetLampDescriptor left,
            StreetLampDescriptor right)
        {
            return !left.Equals(right);
        }
    }

    public readonly struct TrafficSignalDescriptor :
        IEquatable<TrafficSignalDescriptor>
    {
        internal TrafficSignalDescriptor(
            Vector2Int intersectionNode,
            int pairIndex,
            Vector3 position,
            Vector3 forward,
            float blinkPhase01)
        {
            IntersectionNode = intersectionNode;
            PairIndex = pairIndex;
            Position = position;
            Forward = forward;
            BlinkPhase01 = blinkPhase01;
        }

        public Vector2Int IntersectionNode { get; }
        public int PairIndex { get; }
        public Vector3 Position { get; }
        public Vector3 Forward { get; }
        public float BlinkPhase01 { get; }

        public bool Equals(TrafficSignalDescriptor other)
        {
            return IntersectionNode == other.IntersectionNode &&
                   PairIndex == other.PairIndex &&
                   Position == other.Position &&
                   Forward == other.Forward &&
                   BlinkPhase01.Equals(other.BlinkPhase01);
        }

        public override bool Equals(object obj)
        {
            return obj is TrafficSignalDescriptor other && Equals(other);
        }

        public override int GetHashCode()
        {
            unchecked
            {
                int hash = 17;
                hash = (hash * 31) + IntersectionNode.GetHashCode();
                hash = (hash * 31) + PairIndex;
                hash = (hash * 31) + Position.GetHashCode();
                hash = (hash * 31) + Forward.GetHashCode();
                hash = (hash * 31) + BlinkPhase01.GetHashCode();
                return hash;
            }
        }

        public static bool operator ==(
            TrafficSignalDescriptor left,
            TrafficSignalDescriptor right)
        {
            return left.Equals(right);
        }

        public static bool operator !=(
            TrafficSignalDescriptor left,
            TrafficSignalDescriptor right)
        {
            return !left.Equals(right);
        }
    }
}

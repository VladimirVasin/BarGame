using System;
using UnityEngine;

namespace BarPromenade
{
    public readonly struct RoadEdge : IEquatable<RoadEdge>
    {
        public RoadEdge(Vector2Int first, Vector2Int second)
        {
            int distance =
                Mathf.Abs(first.x - second.x) +
                Mathf.Abs(first.y - second.y);
            if (distance != 1)
            {
                throw new ArgumentException(
                    "A road edge must join adjacent orthogonal grid nodes.");
            }

            if (CompareNodes(first, second) <= 0)
            {
                A = first;
                B = second;
            }
            else
            {
                A = second;
                B = first;
            }
        }

        public Vector2Int A { get; }
        public Vector2Int B { get; }
        public Vector2Int Start => A;
        public Vector2Int End => B;
        public bool IsHorizontal => A.y == B.y;
        public bool IsVertical => A.x == B.x;

        public bool Contains(Vector2Int node)
        {
            return A == node || B == node;
        }

        public Vector2Int Other(Vector2Int node)
        {
            if (node == A)
            {
                return B;
            }

            if (node == B)
            {
                return A;
            }

            throw new ArgumentException("The node does not belong to this edge.", nameof(node));
        }

        public static RoadEdge ForCellFrontage(
            Vector2Int cell,
            Vector2Int frontageDirection)
        {
            if (frontageDirection == Vector2Int.left)
            {
                return new RoadEdge(cell, cell + Vector2Int.up);
            }

            if (frontageDirection == Vector2Int.right)
            {
                Vector2Int bottomRight = cell + Vector2Int.right;
                return new RoadEdge(bottomRight, bottomRight + Vector2Int.up);
            }

            if (frontageDirection == Vector2Int.down)
            {
                return new RoadEdge(cell, cell + Vector2Int.right);
            }

            if (frontageDirection == Vector2Int.up)
            {
                Vector2Int topLeft = cell + Vector2Int.up;
                return new RoadEdge(topLeft, topLeft + Vector2Int.right);
            }

            throw new ArgumentException(
                "Frontage must be one of the four cardinal unit directions.",
                nameof(frontageDirection));
        }

        public bool Equals(RoadEdge other)
        {
            return A == other.A && B == other.B;
        }

        public override bool Equals(object obj)
        {
            return obj is RoadEdge other && Equals(other);
        }

        public override int GetHashCode()
        {
            unchecked
            {
                int hash = 17;
                hash = (hash * 31) + A.x;
                hash = (hash * 31) + A.y;
                hash = (hash * 31) + B.x;
                hash = (hash * 31) + B.y;
                return hash;
            }
        }

        public override string ToString()
        {
            return $"{A}->{B}";
        }

        public static bool operator ==(RoadEdge left, RoadEdge right)
        {
            return left.Equals(right);
        }

        public static bool operator !=(RoadEdge left, RoadEdge right)
        {
            return !left.Equals(right);
        }

        internal static int Compare(RoadEdge left, RoadEdge right)
        {
            int startComparison = CompareNodes(left.A, right.A);
            return startComparison != 0
                ? startComparison
                : CompareNodes(left.B, right.B);
        }

        private static int CompareNodes(Vector2Int left, Vector2Int right)
        {
            int xComparison = left.x.CompareTo(right.x);
            return xComparison != 0 ? xComparison : left.y.CompareTo(right.y);
        }
    }
}

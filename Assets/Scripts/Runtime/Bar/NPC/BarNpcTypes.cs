using System;
using UnityEngine;

namespace BarPromenade
{
    public enum BarNpcAction
    {
        Idle = 0,
        Talk = 1,
        Listen = 2,
        Sip = 3,
        Gesture = 4,
        WipeCounter = 5,
        Serve = 6,
        WatchActivity = 7,
        Perform = 8,
        Walk = 9
    }

    /// <summary>
    /// Optional route adapter for a mobile anchor from BarInteriorLayoutPlan.
    /// Positions are local to the same interior root as the source anchor.
    /// </summary>
    public readonly struct BarNpcRoute : IEquatable<BarNpcRoute>
    {
        public BarNpcRoute(string id, Vector3 endPosition)
        {
            if (string.IsNullOrWhiteSpace(id))
            {
                throw new ArgumentException(
                    "A bar NPC route ID is required.",
                    nameof(id));
            }

            if (!IsFinite(endPosition))
            {
                throw new ArgumentException(
                    "A bar NPC route end must be finite.",
                    nameof(endPosition));
            }

            Id = id.Trim();
            EndPosition = endPosition;
        }

        public string Id { get; }
        public Vector3 EndPosition { get; }

        public bool Equals(BarNpcRoute other)
        {
            return string.Equals(
                       Id,
                       other.Id,
                       StringComparison.Ordinal) &&
                   EndPosition == other.EndPosition;
        }

        public override bool Equals(object obj)
        {
            return obj is BarNpcRoute other && Equals(other);
        }

        public override int GetHashCode()
        {
            unchecked
            {
                return (
                    StringComparer.Ordinal.GetHashCode(Id) *
                    397) ^
                    EndPosition.GetHashCode();
            }
        }

        private static bool IsFinite(Vector3 value)
        {
            return IsFinite(value.x) &&
                   IsFinite(value.y) &&
                   IsFinite(value.z);
        }

        private static bool IsFinite(float value)
        {
            return !float.IsNaN(value) &&
                   !float.IsInfinity(value);
        }
    }

    public readonly struct BarNpcDefinition :
        IEquatable<BarNpcDefinition>
    {
        internal BarNpcDefinition(
            string id,
            BarNpcAnchor anchor,
            Vector3 forward,
            Vector3 routeEnd,
            bool mobile,
            int visualVariant,
            uint behaviorSeed,
            float animationPhase01,
            float scale)
        {
            Id = id;
            Role = anchor.Role;
            AnchorId = anchor.Id;
            Position = anchor.Position;
            Forward = forward;
            RouteEnd = routeEnd;
            RouteId = anchor.RouteId;
            Mobile = mobile;
            VisualVariant = visualVariant;
            BehaviorSeed = behaviorSeed;
            AnimationPhase01 = animationPhase01;
            Scale = scale;
        }

        public string Id { get; }
        public BarNpcRole Role { get; }
        public string AnchorId { get; }
        public Vector3 Position { get; }
        public Vector3 Forward { get; }
        public Vector3 RouteEnd { get; }
        public string RouteId { get; }
        public bool Mobile { get; }
        public int VisualVariant { get; }
        public uint BehaviorSeed { get; }
        public float AnimationPhase01 { get; }
        public float Scale { get; }

        public bool Equals(BarNpcDefinition other)
        {
            return string.Equals(
                       Id,
                       other.Id,
                       StringComparison.Ordinal) &&
                   Role == other.Role &&
                   string.Equals(
                       AnchorId,
                       other.AnchorId,
                       StringComparison.Ordinal) &&
                   Position == other.Position &&
                   Forward == other.Forward &&
                   RouteEnd == other.RouteEnd &&
                   string.Equals(
                       RouteId,
                       other.RouteId,
                       StringComparison.Ordinal) &&
                   Mobile == other.Mobile &&
                   VisualVariant == other.VisualVariant &&
                   BehaviorSeed == other.BehaviorSeed &&
                   AnimationPhase01.Equals(
                       other.AnimationPhase01) &&
                   Scale.Equals(other.Scale);
        }

        public override bool Equals(object obj)
        {
            return obj is BarNpcDefinition other &&
                   Equals(other);
        }

        public override int GetHashCode()
        {
            unchecked
            {
                int hash = StringComparer.Ordinal.GetHashCode(Id);
                hash = (hash * 397) ^ (int)Role;
                hash = (hash * 397) ^
                       StringComparer.Ordinal.GetHashCode(AnchorId);
                hash = (hash * 397) ^ Position.GetHashCode();
                hash = (hash * 397) ^ Forward.GetHashCode();
                hash = (hash * 397) ^ RouteEnd.GetHashCode();
                hash = (hash * 397) ^
                       StringComparer.Ordinal.GetHashCode(
                           RouteId ?? string.Empty);
                hash = (hash * 397) ^ Mobile.GetHashCode();
                hash = (hash * 397) ^ VisualVariant;
                hash = (hash * 397) ^ BehaviorSeed.GetHashCode();
                hash = (hash * 397) ^
                       AnimationPhase01.GetHashCode();
                hash = (hash * 397) ^ Scale.GetHashCode();
                return hash;
            }
        }

        public static bool operator ==(
            BarNpcDefinition left,
            BarNpcDefinition right)
        {
            return left.Equals(right);
        }

        public static bool operator !=(
            BarNpcDefinition left,
            BarNpcDefinition right)
        {
            return !left.Equals(right);
        }
    }
}

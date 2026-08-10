using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using UnityEngine;

namespace BarPromenade
{
    public sealed class CityPedestrianDefinition :
        IEquatable<CityPedestrianDefinition>
    {
        private readonly ReadOnlyCollection<RoadEdge> routeEdges;
        private readonly ReadOnlyCollection<Vector3> waypoints;

        internal CityPedestrianDefinition(
            string id,
            IList<RoadEdge> routeEdges,
            IList<Vector3> waypoints,
            float speed,
            float animationSpeed,
            float animationPhase01,
            int paletteVariant,
            uint behaviorSeed,
            bool startsReversed)
        {
            Id = id ?? string.Empty;
            this.routeEdges = new ReadOnlyCollection<RoadEdge>(
                new List<RoadEdge>(
                    routeEdges ??
                    throw new ArgumentNullException(nameof(routeEdges))));
            this.waypoints = new ReadOnlyCollection<Vector3>(
                new List<Vector3>(
                    waypoints ??
                    throw new ArgumentNullException(nameof(waypoints))));
            Speed = speed;
            AnimationSpeed = animationSpeed;
            AnimationPhase01 = animationPhase01;
            PaletteVariant = paletteVariant;
            BehaviorSeed = behaviorSeed;
            StartsReversed = startsReversed;

            float length = 0f;
            for (int index = 1; index < this.waypoints.Count; index++)
            {
                length += Vector3.Distance(
                    this.waypoints[index - 1],
                    this.waypoints[index]);
            }

            RouteLength = length;
        }

        public string Id { get; }
        public IReadOnlyList<RoadEdge> RouteEdges => routeEdges;
        public IReadOnlyList<Vector3> Waypoints => waypoints;
        public float RouteLength { get; }
        public float Speed { get; }
        public float AnimationSpeed { get; }
        public float AnimationPhase01 { get; }
        public int PaletteVariant { get; }
        public uint BehaviorSeed { get; }
        public bool StartsReversed { get; }

        public bool Equals(CityPedestrianDefinition other)
        {
            if (ReferenceEquals(null, other))
            {
                return false;
            }

            if (ReferenceEquals(this, other))
            {
                return true;
            }

            return string.Equals(Id, other.Id, StringComparison.Ordinal) &&
                   SequenceEqual(routeEdges, other.routeEdges) &&
                   SequenceEqual(waypoints, other.waypoints) &&
                   RouteLength.Equals(other.RouteLength) &&
                   Speed.Equals(other.Speed) &&
                   AnimationSpeed.Equals(other.AnimationSpeed) &&
                   AnimationPhase01.Equals(other.AnimationPhase01) &&
                   PaletteVariant == other.PaletteVariant &&
                   BehaviorSeed == other.BehaviorSeed &&
                   StartsReversed == other.StartsReversed;
        }

        public override bool Equals(object obj)
        {
            return obj is CityPedestrianDefinition other && Equals(other);
        }

        public override int GetHashCode()
        {
            unchecked
            {
                int hash = StringComparer.Ordinal.GetHashCode(Id);
                for (int index = 0; index < routeEdges.Count; index++)
                {
                    hash = (hash * 397) ^ routeEdges[index].GetHashCode();
                }

                for (int index = 0; index < waypoints.Count; index++)
                {
                    hash = (hash * 397) ^ waypoints[index].GetHashCode();
                }

                hash = (hash * 397) ^ RouteLength.GetHashCode();
                hash = (hash * 397) ^ Speed.GetHashCode();
                hash = (hash * 397) ^ AnimationSpeed.GetHashCode();
                hash = (hash * 397) ^ AnimationPhase01.GetHashCode();
                hash = (hash * 397) ^ PaletteVariant;
                hash = (hash * 397) ^ BehaviorSeed.GetHashCode();
                return (hash * 397) ^ StartsReversed.GetHashCode();
            }
        }

        private static bool SequenceEqual<T>(
            IReadOnlyList<T> first,
            IReadOnlyList<T> second)
        {
            if (first.Count != second.Count)
            {
                return false;
            }

            EqualityComparer<T> comparer = EqualityComparer<T>.Default;
            for (int index = 0; index < first.Count; index++)
            {
                if (!comparer.Equals(first[index], second[index]))
                {
                    return false;
                }
            }

            return true;
        }
    }

    public sealed class CityPedestrianPlan
    {
        internal CityPedestrianPlan(
            int layoutSeed,
            int populationSeed,
            uint stableSeed,
            int desiredCount,
            float agentRadius,
            IList<CityPedestrianDefinition> definitions)
        {
            LayoutSeed = layoutSeed;
            PopulationSeed = populationSeed;
            StableSeed = stableSeed;
            DesiredCount = desiredCount;
            AgentRadius = agentRadius;
            Definitions =
                new ReadOnlyCollection<CityPedestrianDefinition>(
                    new List<CityPedestrianDefinition>(
                        definitions ??
                        throw new ArgumentNullException(
                            nameof(definitions))));
        }

        public int LayoutSeed { get; }
        public int PopulationSeed { get; }
        public uint StableSeed { get; }
        public int DesiredCount { get; }
        public float AgentRadius { get; }
        public IReadOnlyList<CityPedestrianDefinition> Definitions { get; }
        public int Count => Definitions.Count;
    }
}

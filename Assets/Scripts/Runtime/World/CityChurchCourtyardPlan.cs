using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using UnityEngine;

namespace BarPromenade
{
    public enum CityChurchCourtyardSurfaceKind
    {
        Stone = 0,
        Gravel = 1,
        Lawn = 2
    }

    public enum CityChurchCourtyardFixtureKind
    {
        Shrub = 0,
        FlowerBed = 1,
        Bench = 2,
        Tree = 3
    }

    public readonly struct CityChurchCourtyardSurfaceDescriptor :
        IEquatable<CityChurchCourtyardSurfaceDescriptor>
    {
        public CityChurchCourtyardSurfaceDescriptor(
            string id,
            CityChurchCourtyardSurfaceKind kind,
            Rect bounds)
        {
            Id = id ?? string.Empty;
            Kind = kind;
            Bounds = bounds;
        }

        public string Id { get; }
        public CityChurchCourtyardSurfaceKind Kind { get; }
        public Rect Bounds { get; }

        public bool Equals(CityChurchCourtyardSurfaceDescriptor other)
        {
            return string.Equals(Id, other.Id, StringComparison.Ordinal) &&
                   Kind == other.Kind &&
                   Bounds.Equals(other.Bounds);
        }

        public override bool Equals(object obj)
        {
            return obj is CityChurchCourtyardSurfaceDescriptor other &&
                   Equals(other);
        }

        public override int GetHashCode()
        {
            unchecked
            {
                int hash = StringComparer.Ordinal.GetHashCode(
                    Id ?? string.Empty);
                hash = (hash * 397) ^ (int)Kind;
                return (hash * 397) ^ Bounds.GetHashCode();
            }
        }
    }

    public readonly struct CityChurchCourtyardFixtureDescriptor :
        IEquatable<CityChurchCourtyardFixtureDescriptor>
    {
        public CityChurchCourtyardFixtureDescriptor(
            string id,
            CityChurchCourtyardFixtureKind kind,
            int variant,
            Vector3 groundPosition,
            Quaternion rotation,
            Vector3 scale,
            Rect blockerBounds)
        {
            Id = id ?? string.Empty;
            Kind = kind;
            Variant = variant;
            GroundPosition = groundPosition;
            Rotation = rotation;
            Scale = scale;
            BlockerBounds = blockerBounds;
        }

        public string Id { get; }
        public CityChurchCourtyardFixtureKind Kind { get; }
        public int Variant { get; }
        public Vector3 GroundPosition { get; }
        public Quaternion Rotation { get; }
        public Vector3 Scale { get; }
        public Rect BlockerBounds { get; }

        public bool Equals(CityChurchCourtyardFixtureDescriptor other)
        {
            return string.Equals(Id, other.Id, StringComparison.Ordinal) &&
                   Kind == other.Kind &&
                   Variant == other.Variant &&
                   GroundPosition.Equals(other.GroundPosition) &&
                   Rotation.Equals(other.Rotation) &&
                   Scale.Equals(other.Scale) &&
                   BlockerBounds.Equals(other.BlockerBounds);
        }

        public override bool Equals(object obj)
        {
            return obj is CityChurchCourtyardFixtureDescriptor other &&
                   Equals(other);
        }

        public override int GetHashCode()
        {
            unchecked
            {
                int hash = StringComparer.Ordinal.GetHashCode(
                    Id ?? string.Empty);
                hash = (hash * 397) ^ (int)Kind;
                hash = (hash * 397) ^ Variant;
                hash = (hash * 397) ^ GroundPosition.GetHashCode();
                hash = (hash * 397) ^ Rotation.GetHashCode();
                hash = (hash * 397) ^ Scale.GetHashCode();
                return (hash * 397) ^ BlockerBounds.GetHashCode();
            }
        }
    }

    /// <summary>
    /// Pure presentation and collision contract for the maintained church
    /// yard. Every visible mesh named here is authored in Blender; Unity owns
    /// only placement, batching, collision proxies and interactions.
    /// </summary>
    public sealed class CityChurchCourtyardPlan
    {
        internal CityChurchCourtyardPlan(
            Rect grounds,
            float groundTopY,
            Rect forecourtBounds,
            Rect gardenBounds,
            CityChurchCemeteryPassagePlan passage,
            IList<CityChurchCourtyardSurfaceDescriptor> surfaces,
            IList<CityChurchCourtyardFixtureDescriptor> fixtures)
        {
            Grounds = grounds;
            GroundTopY = groundTopY;
            ForecourtBounds = forecourtBounds;
            GardenBounds = gardenBounds;
            Passage = passage;
            Surfaces = new ReadOnlyCollection<
                CityChurchCourtyardSurfaceDescriptor>(
                new List<CityChurchCourtyardSurfaceDescriptor>(surfaces));
            Fixtures = new ReadOnlyCollection<
                CityChurchCourtyardFixtureDescriptor>(
                new List<CityChurchCourtyardFixtureDescriptor>(fixtures));
        }

        public Rect Grounds { get; }
        public float GroundTopY { get; }
        public Rect ForecourtBounds { get; }
        public Rect GardenBounds { get; }
        public CityChurchCemeteryPassagePlan Passage { get; }
        public IReadOnlyList<CityChurchCourtyardSurfaceDescriptor> Surfaces
        {
            get;
        }
        public IReadOnlyList<CityChurchCourtyardFixtureDescriptor> Fixtures
        {
            get;
        }

        public int GetFixtureCount(CityChurchCourtyardFixtureKind kind)
        {
            int count = 0;
            for (int index = 0; index < Fixtures.Count; index++)
            {
                if (Fixtures[index].Kind == kind)
                {
                    count++;
                }
            }

            return count;
        }
    }

    public static class CityChurchCourtyardPlanner
    {
        public const float ForecourtDepth = 6f;
        public const float ForecourtWidth = 8f;
        public const float SouthPathWidth = 2.4f;
        public const float GardenBuildingClearance = 2f;
        public const float GardenBoundaryInset = 4f;

        private const float GeometryTolerance = 0.001f;

        private static readonly Vector2 RoundShrubSize =
            new Vector2(1.30f, 1.10f);
        private static readonly Vector2 HedgeShrubSize =
            new Vector2(2.20f, 0.85f);
        private static readonly Vector2 SmallBedSize =
            new Vector2(3f, 1f);
        private static readonly Vector2 LargeBedSize =
            new Vector2(4.2f, 1f);
        private static readonly Vector2 BenchBlockerSize =
            new Vector2(1.72f, 0.62f);
        private static readonly Vector2 TreeBlockerSize =
            new Vector2(1.10f, 1.10f);

        public static CityChurchCourtyardPlan Create(
            CityLayout layout,
            CityChurchPlan church,
            CityChurchCemeteryPassagePlan passage)
        {
            if (layout == null)
            {
                throw new ArgumentNullException(nameof(layout));
            }

            if (church == null)
            {
                return null;
            }

            Rect grounds = church.Grounds;
            Vector3 door = church.DoorGroundPosition;
            float halfForecourt = ForecourtWidth * 0.5f;
            Rect forecourt = Rect.MinMaxRect(
                door.x - ForecourtDepth,
                door.z - halfForecourt,
                door.x,
                door.z + halfForecourt);
            Rect streetApproach = Rect.MinMaxRect(
                grounds.xMin,
                door.z - CityChurchPlanner.ApproachWidth * 0.5f,
                forecourt.xMin,
                door.z + CityChurchPlanner.ApproachWidth * 0.5f);

            float gardenEast = Mathf.Min(
                grounds.xMax - GardenBoundaryInset,
                Mathf.Max(
                    church.ModelFootprint.xMax + 6f,
                    passage != null
                        ? passage.AxisX + 4f
                        : church.ModelFootprint.xMax + 10f));
            Rect garden = Rect.MinMaxRect(
                grounds.xMin + 2f,
                church.ModelFootprint.yMax + GardenBuildingClearance,
                gardenEast,
                grounds.yMax - GardenBoundaryInset);

            var surfaces = new List<
                CityChurchCourtyardSurfaceDescriptor>(6)
            {
                new CityChurchCourtyardSurfaceDescriptor(
                    "church-courtyard-street-approach",
                    CityChurchCourtyardSurfaceKind.Stone,
                    streetApproach),
                new CityChurchCourtyardSurfaceDescriptor(
                    "church-courtyard-forecourt",
                    CityChurchCourtyardSurfaceKind.Stone,
                    forecourt),
                new CityChurchCourtyardSurfaceDescriptor(
                    "church-courtyard-garden-lawn",
                    CityChurchCourtyardSurfaceKind.Lawn,
                    garden)
            };

            if (passage != null)
            {
                float southPathTop = grounds.yMin + SouthPathWidth;
                surfaces.Add(new CityChurchCourtyardSurfaceDescriptor(
                    "church-courtyard-cemetery-link",
                    CityChurchCourtyardSurfaceKind.Gravel,
                    Rect.MinMaxRect(
                        forecourt.xMin,
                        grounds.yMin,
                        passage.FenceOpeningBounds.xMax,
                        southPathTop)));
                surfaces.Add(new CityChurchCourtyardSurfaceDescriptor(
                    "church-courtyard-south-turn",
                    CityChurchCourtyardSurfaceKind.Gravel,
                    Rect.MinMaxRect(
                        forecourt.xMin,
                        southPathTop,
                        forecourt.xMin + SouthPathWidth,
                        forecourt.yMin)));
            }

            var fixtures = new List<
                CityChurchCourtyardFixtureDescriptor>(12);
            AddFlowerBed(
                fixtures,
                "church-courtyard-bed-south",
                0,
                new Vector2(
                    forecourt.xMax - SmallBedSize.x * 0.5f - 0.45f,
                    forecourt.yMin - 0.80f),
                church.GroundTopY,
                SmallBedSize);
            AddFlowerBed(
                fixtures,
                "church-courtyard-bed-north",
                1,
                new Vector2(
                    forecourt.xMax - LargeBedSize.x * 0.5f - 0.10f,
                    forecourt.yMax + 0.80f),
                church.GroundTopY,
                LargeBedSize);

            float hedgeZ = garden.yMin + 2f;
            AddShrub(fixtures, 0, 1,
                new Vector2(garden.xMin + 7f, hedgeZ),
                church.GroundTopY, HedgeShrubSize);
            AddShrub(fixtures, 1, 1,
                new Vector2(garden.center.x, hedgeZ),
                church.GroundTopY, HedgeShrubSize);
            AddShrub(fixtures, 2, 1,
                new Vector2(garden.xMax - 7f, hedgeZ),
                church.GroundTopY, HedgeShrubSize);
            AddShrub(fixtures, 3, 0,
                new Vector2(garden.xMin + 5f, garden.center.y + 1f),
                church.GroundTopY, RoundShrubSize);
            AddShrub(fixtures, 4, 0,
                new Vector2(garden.center.x, garden.center.y + 5f),
                church.GroundTopY, RoundShrubSize);
            AddShrub(fixtures, 5, 0,
                new Vector2(garden.xMax - 5f, garden.center.y - 1f),
                church.GroundTopY, RoundShrubSize);

            float benchZ = garden.yMin + 10f;
            AddFixture(
                fixtures,
                "church-courtyard-bench-west",
                CityChurchCourtyardFixtureKind.Bench,
                0,
                new Vector2(garden.xMin + 10f, benchZ),
                church.GroundTopY,
                Quaternion.LookRotation(Vector3.back),
                BenchBlockerSize);
            AddFixture(
                fixtures,
                "church-courtyard-bench-east",
                CityChurchCourtyardFixtureKind.Bench,
                0,
                new Vector2(garden.xMax - 10f, benchZ),
                church.GroundTopY,
                Quaternion.LookRotation(Vector3.back),
                BenchBlockerSize);

            AddFixture(
                fixtures,
                "church-courtyard-tree-west",
                CityChurchCourtyardFixtureKind.Tree,
                1,
                new Vector2(garden.xMin + 2.5f, garden.yMax - 5f),
                church.GroundTopY,
                Quaternion.identity,
                TreeBlockerSize);
            AddFixture(
                fixtures,
                "church-courtyard-tree-east",
                CityChurchCourtyardFixtureKind.Tree,
                3,
                new Vector2(garden.xMax - 3f, garden.yMax - 7f),
                church.GroundTopY,
                Quaternion.identity,
                TreeBlockerSize);

            var plan = new CityChurchCourtyardPlan(
                grounds,
                church.GroundTopY,
                forecourt,
                garden,
                passage,
                surfaces,
                fixtures);
            ValidateOrThrow(layout, church, plan);
            return plan;
        }

        public static void ValidateOrThrow(
            CityLayout layout,
            CityChurchPlan church,
            CityChurchCourtyardPlan plan)
        {
            if (layout == null)
            {
                throw new ArgumentNullException(nameof(layout));
            }

            if (church == null || plan == null)
            {
                throw new ArgumentNullException(
                    church == null ? nameof(church) : nameof(plan));
            }

            if (!Contains(plan.Grounds, plan.ForecourtBounds) ||
                !Contains(plan.Grounds, plan.GardenBounds) ||
                plan.ForecourtBounds.Overlaps(church.ModelFootprint) ||
                plan.GardenBounds.Overlaps(church.ModelFootprint) ||
                plan.GetFixtureCount(
                    CityChurchCourtyardFixtureKind.Bench) != 2 ||
                plan.GetFixtureCount(
                    CityChurchCourtyardFixtureKind.Tree) != 2 ||
                plan.GetFixtureCount(
                    CityChurchCourtyardFixtureKind.Shrub) != 6 ||
                plan.GetFixtureCount(
                    CityChurchCourtyardFixtureKind.FlowerBed) != 2)
            {
                throw new InvalidOperationException(
                    "The church courtyard lost its authored composition.");
            }

            bool passageCovered = plan.Passage == null;
            for (int index = 0; index < plan.Surfaces.Count; index++)
            {
                CityChurchCourtyardSurfaceDescriptor surface =
                    plan.Surfaces[index];
                if (string.IsNullOrWhiteSpace(surface.Id) ||
                    surface.Bounds.width <= GeometryTolerance ||
                    surface.Bounds.height <= GeometryTolerance ||
                    !Contains(plan.Grounds, surface.Bounds) ||
                    surface.Bounds.Overlaps(church.ModelFootprint))
                {
                    throw new InvalidOperationException(
                        "A church courtyard surface leaves its grounds " +
                        "or intersects the church.");
                }

                if (plan.Passage != null &&
                    surface.Kind ==
                        CityChurchCourtyardSurfaceKind.Gravel &&
                    Contains(
                        surface.Bounds,
                        new Vector2(
                            plan.Passage.AxisX,
                            plan.Passage.BoundaryZ + 0.01f)))
                {
                    passageCovered = true;
                }
            }

            for (int index = 0; index < plan.Fixtures.Count; index++)
            {
                CityChurchCourtyardFixtureDescriptor fixture =
                    plan.Fixtures[index];
                if (string.IsNullOrWhiteSpace(fixture.Id) ||
                    fixture.Variant < 0 ||
                    !Contains(plan.Grounds, fixture.BlockerBounds) ||
                    fixture.BlockerBounds.Overlaps(
                        church.ModelFootprint))
                {
                    throw new InvalidOperationException(
                        "A church courtyard fixture blocks the building " +
                        "or leaves the precinct.");
                }

                for (int surfaceIndex = 0;
                     surfaceIndex < plan.Surfaces.Count;
                     surfaceIndex++)
                {
                    CityChurchCourtyardSurfaceDescriptor surface =
                        plan.Surfaces[surfaceIndex];
                    if (surface.Kind !=
                            CityChurchCourtyardSurfaceKind.Lawn &&
                        fixture.BlockerBounds.Overlaps(surface.Bounds))
                    {
                        throw new InvalidOperationException(
                            "A church courtyard fixture blocks a path.");
                    }
                }
            }

            if (!passageCovered)
            {
                throw new InvalidOperationException(
                    "The cemetery opening does not land on the church " +
                    "courtyard path.");
            }
        }

        private static void AddShrub(
            ICollection<CityChurchCourtyardFixtureDescriptor> target,
            int index,
            int variant,
            Vector2 position,
            float groundTopY,
            Vector2 size)
        {
            AddFixture(
                target,
                $"church-courtyard-shrub-{index:D2}",
                CityChurchCourtyardFixtureKind.Shrub,
                variant,
                position,
                groundTopY,
                Quaternion.identity,
                size);
        }

        private static void AddFlowerBed(
            ICollection<CityChurchCourtyardFixtureDescriptor> target,
            string id,
            int variant,
            Vector2 position,
            float groundTopY,
            Vector2 size)
        {
            AddFixture(
                target,
                id,
                CityChurchCourtyardFixtureKind.FlowerBed,
                variant,
                position,
                groundTopY,
                Quaternion.identity,
                size);
        }

        private static void AddFixture(
            ICollection<CityChurchCourtyardFixtureDescriptor> target,
            string id,
            CityChurchCourtyardFixtureKind kind,
            int variant,
            Vector2 position,
            float groundTopY,
            Quaternion rotation,
            Vector2 blockerSize)
        {
            target.Add(new CityChurchCourtyardFixtureDescriptor(
                id,
                kind,
                variant,
                new Vector3(position.x, groundTopY, position.y),
                rotation,
                Vector3.one,
                Rect.MinMaxRect(
                    position.x - blockerSize.x * 0.5f,
                    position.y - blockerSize.y * 0.5f,
                    position.x + blockerSize.x * 0.5f,
                    position.y + blockerSize.y * 0.5f)));
        }

        private static bool Contains(Rect outer, Rect inner)
        {
            return inner.xMin >= outer.xMin - GeometryTolerance &&
                   inner.xMax <= outer.xMax + GeometryTolerance &&
                   inner.yMin >= outer.yMin - GeometryTolerance &&
                   inner.yMax <= outer.yMax + GeometryTolerance;
        }

        private static bool Contains(Rect outer, Vector2 point)
        {
            return point.x >= outer.xMin - GeometryTolerance &&
                   point.x <= outer.xMax + GeometryTolerance &&
                   point.y >= outer.yMin - GeometryTolerance &&
                   point.y <= outer.yMax + GeometryTolerance;
        }
    }
}

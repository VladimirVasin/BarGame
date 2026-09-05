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
        Tree = 3,
        Fountain = 4,
        Statue = 5,
        PottingLedge = 6,
        PotSmall = 7,
        PotLarge = 8,
        Hedge = 9,
        Uplight = 10
    }

    public readonly struct CityChurchCourtyardSurfaceDescriptor :
        IEquatable<CityChurchCourtyardSurfaceDescriptor>
    {
        public CityChurchCourtyardSurfaceDescriptor(
            string id,
            CityChurchCourtyardSurfaceKind kind,
            Rect bounds,
            bool reservesPassage = true)
        {
            Id = id ?? string.Empty;
            Kind = kind;
            Bounds = bounds;
            ReservesPassage = kind != CityChurchCourtyardSurfaceKind.Lawn &&
                reservesPassage;
        }

        public string Id { get; }
        public CityChurchCourtyardSurfaceKind Kind { get; }
        public Rect Bounds { get; }
        public bool ReservesPassage { get; }

        public bool Equals(CityChurchCourtyardSurfaceDescriptor other)
        {
            return string.Equals(Id, other.Id, StringComparison.Ordinal) &&
                   Kind == other.Kind &&
                   Bounds.Equals(other.Bounds) &&
                   ReservesPassage == other.ReservesPassage;
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
                hash = (hash * 397) ^ Bounds.GetHashCode();
                return (hash * 397) ^ ReservesPassage.GetHashCode();
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
        public const float LoopPathWidth = 2.4f;
        public const float GardenNorthExtent = 37.5f;

        public static CityChurchCourtyardFixtureDescriptor GetFixture(
            CityChurchCourtyardPlan plan, CityChurchCourtyardFixtureKind kind)
        {
            for (int i = 0; i < plan.Fixtures.Count; i++)
            {
                if (plan.Fixtures[i].Kind == kind) return plan.Fixtures[i];
            }
            throw new InvalidOperationException("Missing church garden fixture: " + kind);
        }

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
            float west = grounds.xMin;
            float south = grounds.yMin;
            float groundY = church.GroundTopY;
            Rect forecourt = Rect.MinMaxRect(
                door.x - ForecourtDepth, door.z - ForecourtWidth * 0.5f,
                door.x, door.z + ForecourtWidth * 0.5f);
            Rect streetApproach = Rect.MinMaxRect(
                west, door.z - church.Access.Width * 0.5f,
                forecourt.xMin, door.z + church.Access.Width * 0.5f);
            float eastAxis = passage != null
                ? passage.AxisX : church.ModelFootprint.xMax + 5.8f;
            float northPath = church.ModelFootprint.yMax + 9.675f;
            Rect garden = Rect.MinMaxRect(
                west + 2f,
                church.ModelFootprint.yMax + GardenBuildingClearance,
                eastAxis + 4f,
                Mathf.Min(south + GardenNorthExtent,
                    grounds.yMax - GardenBoundaryInset));
            float eastPathLeft = eastAxis - LoopPathWidth * 0.5f;
            float eastPathRight = eastAxis + LoopPathWidth * 0.5f;
            float westPathRight = forecourt.xMin + LoopPathWidth;
            float southPathTop = south + SouthPathWidth;

            // A single continuous route joins the existing door, two
            // inhabited garden pockets and the only cemetery passage.
            // Pads may contain furniture; route strips must stay clear.
            var surfaces = new List<CityChurchCourtyardSurfaceDescriptor>
            {
                Surface("street-approach", CityChurchCourtyardSurfaceKind.Stone,
                    streetApproach),
                Surface("forecourt", CityChurchCourtyardSurfaceKind.Stone,
                    forecourt),
                Surface("south-turn", CityChurchCourtyardSurfaceKind.Gravel,
                    Rect.MinMaxRect(forecourt.xMin, southPathTop,
                        westPathRight, forecourt.yMin)),
                Surface("north-turn", CityChurchCourtyardSurfaceKind.Gravel,
                    Rect.MinMaxRect(forecourt.xMin, forecourt.yMax,
                        westPathRight, northPath)),
                Surface("north-walk", CityChurchCourtyardSurfaceKind.Gravel,
                    Rect.MinMaxRect(forecourt.xMin, northPath,
                        eastPathRight, northPath + LoopPathWidth)),
                Surface("apse-walk", CityChurchCourtyardSurfaceKind.Gravel,
                    Rect.MinMaxRect(eastPathLeft, southPathTop,
                        eastPathRight, northPath)),
                Surface("cemetery-link", CityChurchCourtyardSurfaceKind.Gravel,
                    Rect.MinMaxRect(forecourt.xMin, south,
                        Mathf.Max(eastPathRight,
                            passage != null ? passage.FenceOpeningBounds.xMax : eastPathRight),
                        southPathTop)),
                Surface("west-seat-pad", CityChurchCourtyardSurfaceKind.Gravel,
                    LocalRect(grounds, 13.8f, 31.4f, 20.3f, 34.3f), false),
                Surface("east-seat-pad", CityChurchCourtyardSurfaceKind.Gravel,
                    LocalRect(grounds, 34f, 24f, 38.8f, 27.8f), false),
                Surface("fountain-pad", CityChurchCourtyardSurfaceKind.Stone,
                    LocalRect(grounds, 18f, 24.6f, 23f, 29f), false),
                Surface("potting-pad", CityChurchCourtyardSurfaceKind.Gravel,
                    LocalRect(grounds, 9f, 24.3f, 12.6f, 29f), false),
                Surface("statue-pad", CityChurchCourtyardSurfaceKind.Stone,
                    LocalRect(grounds, 31.4f, 31.4f, 33.4f, 33.6f), false)
            };
            // The continuous church ground owns the grass presentation.
            // This descriptor identifies its garden use without tiled slabs.
            surfaces.Add(Surface("garden-lawn", CityChurchCourtyardSurfaceKind.Lawn,
                garden, false));

            var fixtures = new List<CityChurchCourtyardFixtureDescriptor>(17);
            AddFlowerBed(fixtures, "church-courtyard-bed-south", 0,
                new Vector2(west + 9.2f, south + 22f), groundY, SmallBedSize);
            AddFlowerBed(fixtures, "church-courtyard-bed-north", 1,
                new Vector2(west + 32.4f, south + 35f), groundY, LargeBedSize);

            // Two unequal planting groups frame the pockets. The nave
            // and the west entrance keep their unobstructed sight lines.
            AddShrub(fixtures, 0, 0,
                new Vector2(west + 9f, south + 33.9f), groundY, RoundShrubSize);
            AddShrub(fixtures, 1, 0,
                new Vector2(west + 10.5f, south + 35.3f), groundY, RoundShrubSize);
            AddShrub(fixtures, 2, 1,
                new Vector2(west + 12f, south + 33.8f), groundY, HedgeShrubSize);
            AddShrub(fixtures, 3, 0,
                new Vector2(west + 35.5f, south + 34.8f), groundY, RoundShrubSize);
            AddShrub(fixtures, 4, 0,
                new Vector2(west + 37.2f, south + 33.5f), groundY, RoundShrubSize);
            AddShrub(fixtures, 5, 1,
                new Vector2(west + 38.3f, south + 35.3f), groundY, HedgeShrubSize);

            AddFixture(fixtures, "church-courtyard-bench-west",
                CityChurchCourtyardFixtureKind.Bench, 0,
                new Vector2(west + 16.5f, south + 33.3f), groundY,
                Quaternion.LookRotation(Vector3.back), BenchBlockerSize);
            AddFixture(fixtures, "church-courtyard-bench-east",
                CityChurchCourtyardFixtureKind.Bench, 0,
                new Vector2(west + 35.4f, south + 26f), groundY,
                Quaternion.LookRotation(Vector3.left),
                new Vector2(BenchBlockerSize.y, BenchBlockerSize.x));
            AddFixture(fixtures, "church-courtyard-tree-west",
                CityChurchCourtyardFixtureKind.Tree, 1,
                new Vector2(west + 12.2f, south + 36.1f), groundY,
                Quaternion.identity, TreeBlockerSize);
            AddFixture(fixtures, "church-courtyard-tree-east",
                CityChurchCourtyardFixtureKind.Tree, 3,
                new Vector2(west + 37.7f, south + 37f), groundY,
                Quaternion.identity, TreeBlockerSize);
            AddFixture(fixtures, "church-courtyard-fountain",
                CityChurchCourtyardFixtureKind.Fountain, 0,
                new Vector2(west + 20.5f, south + 26.5f), groundY,
                Quaternion.identity, new Vector2(1.6f, 1.6f));
            AddFixture(fixtures, "church-courtyard-mary",
                CityChurchCourtyardFixtureKind.Statue, 0,
                new Vector2(west + 32.4f, south + 32.7f), groundY,
                Quaternion.LookRotation(Vector3.back), new Vector2(0.72f, 0.72f));
            AddFixture(fixtures, "church-courtyard-potting-ledge",
                CityChurchCourtyardFixtureKind.PottingLedge, 0,
                new Vector2(west + 10.8f, south + 26f), groundY,
                Quaternion.identity, new Vector2(1.15f, 0.52f));
            AddFixture(fixtures, "church-courtyard-pot-small",
                CityChurchCourtyardFixtureKind.PotSmall, 0,
                new Vector2(west + 11.65f, south + 26.25f), groundY,
                Quaternion.identity, new Vector2(0.24f, 0.24f));
            AddFixture(fixtures, "church-courtyard-pot-large",
                CityChurchCourtyardFixtureKind.PotLarge, 0,
                new Vector2(west + 11.85f, south + 26.8f), groundY,
                Quaternion.identity, new Vector2(0.46f, 0.46f));

            ChurchGardenBorderPlan.Append(church, fixtures);

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
                    CityChurchCourtyardFixtureKind.FlowerBed) != 2 ||
                plan.GetFixtureCount(CityChurchCourtyardFixtureKind.Fountain) != 1 ||
                plan.GetFixtureCount(CityChurchCourtyardFixtureKind.Statue) != 1 ||
                plan.GetFixtureCount(CityChurchCourtyardFixtureKind.PottingLedge) != 1)
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
                    if (surface.ReservesPassage &&
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

            ChurchGardenBorderPlan.ValidateOrThrow(layout, plan);

            IReadOnlyList<Vector3> route = GetLoopWaypoints(plan);
            for (int i = 1; i < route.Count; i++)
            {
                float distance = Vector3.Distance(route[i - 1], route[i]);
                int steps = Mathf.CeilToInt(distance / 0.25f);
                for (int step = 0; step <= steps; step++)
                {
                    Vector3 position = Vector3.Lerp(route[i - 1], route[i],
                        step / (float)Mathf.Max(1, steps));
                    var point = new Vector2(position.x, position.z);
                    bool paved = false;
                    for (int s = 0; s < plan.Surfaces.Count; s++)
                        paved |= plan.Surfaces[s].Kind !=
                            CityChurchCourtyardSurfaceKind.Lawn &&
                            Contains(plan.Surfaces[s].Bounds, point);
                    if (!paved)
                        throw new InvalidOperationException("Church garden loop is disconnected.");
                    for (int f = 0; f < plan.Fixtures.Count; f++)
                    {
                        Rect blocker = plan.Fixtures[f].BlockerBounds;
                        blocker.xMin -= 0.45f;
                        blocker.yMin -= 0.45f;
                        blocker.xMax += 0.45f;
                        blocker.yMax += 0.45f;
                        if (blocker.Contains(point))
                            throw new InvalidOperationException("Church garden loop lacks capsule clearance.");
                    }
                }
            }
        }

        public static IReadOnlyList<Vector3> GetLoopWaypoints(CityChurchCourtyardPlan plan)
        {
            float y = plan.GroundTopY;
            float west = plan.ForecourtBounds.xMin + LoopPathWidth * 0.5f;
            float east = plan.Passage != null ? plan.Passage.AxisX : plan.GardenBounds.xMax - 4f;
            float north = plan.GardenBounds.yMin - GardenBuildingClearance + 9.675f + LoopPathWidth * 0.5f;
            float south = plan.Grounds.yMin + SouthPathWidth * 0.5f;
            return new[]
            {
                new Vector3(west, y, plan.ForecourtBounds.center.y),
                new Vector3(west, y, north),
                new Vector3(east, y, north),
                new Vector3(east, y, south),
                new Vector3(west, y, south),
                new Vector3(west, y, plan.ForecourtBounds.center.y)
            };
        }

        private static CityChurchCourtyardSurfaceDescriptor Surface(
            string name, CityChurchCourtyardSurfaceKind kind, Rect bounds,
            bool reservesPassage = true)
        {
            return new CityChurchCourtyardSurfaceDescriptor(
                "church-courtyard-" + name, kind, bounds, reservesPassage);
        }

        private static Rect LocalRect(Rect grounds, float x0, float z0,
            float x1, float z1)
        {
            return Rect.MinMaxRect(grounds.xMin + x0, grounds.yMin + z0,
                grounds.xMin + x1, grounds.yMin + z1);
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

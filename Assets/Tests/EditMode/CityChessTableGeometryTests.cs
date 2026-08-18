using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;

namespace BarPromenade.Tests.EditMode
{
    /// <summary>
    /// The park chess set stands on a lawn that keeps sloping under it,
    /// and it carries the one piece of city dressing that has to be
    /// literally correct rather than merely suggestive: a chess board.
    /// Both are pinned here.
    /// </summary>
    public sealed class CityChessTableGeometryTests
    {
        private const int Seed = GameSessionState.DefaultCitySeed;

        // The recipe's own contract, restated: two tables that far
        // apart, a board of eight squares that big inside a rim, and
        // benches standing off both long sides of each table.
        private const float TableOffset = 1.85f;
        private const int SquaresPerSide = 8;
        private const float SquareSize = 0.15f;
        private const float FieldSize = SquareSize * SquaresPerSide;
        private const float BenchZ = 1.10f;
        private const float BenchWidth = 1.12f;
        private const float BenchLegInset = 0.18f;
        private const float BoardBandBottom = 0.90f;
        private const float BoardBandTop = 0.97f;

        /// <summary>
        /// Every foot of the set reaches the ground it stands over. The
        /// anchor is one sampled point; the lawn under the far table is
        /// a tenth of a metre lower, which is exactly the gap a foot
        /// drawn to the anchor plane would hang by.
        /// </summary>
        [Test]
        public void EveryFoot_ReachesTheLawnUnderIt()
        {
            List<Bounds> parts = CollectChessParts(
                out CityLayout layout,
                out Vector3 origin,
                out Vector3 tangent,
                out Vector3 forward);

            foreach (Vector3 foot in DescribeFeet(origin, tangent, forward))
            {
                Assert.That(
                    CityTerrainSurfacePlan.TrySampleGroundTop(
                        layout,
                        new Vector2(foot.x, foot.z),
                        out float groundTop,
                        out _),
                    Is.True,
                    "The chess set stands on park terrain.");

                bool bedded = false;
                for (int index = 0; index < parts.Count; index++)
                {
                    Bounds part = parts[index];
                    if (foot.x < part.min.x ||
                        foot.x > part.max.x ||
                        foot.z < part.min.z ||
                        foot.z > part.max.z ||
                        part.min.y > groundTop - 0.05f ||
                        part.max.y < groundTop)
                    {
                        continue;
                    }

                    bedded = true;
                    break;
                }

                Assert.That(
                    bedded,
                    Is.True,
                    $"Nothing bridges the lawn at {foot:F2}: the part " +
                    "above it hangs clear of the grass.");
            }
        }

        /// <summary>
        /// The board is a board. Thirty-two dark squares per table, on
        /// the lattice, in the colouring a real set has: a light square
        /// in the near-right corner of each of the two occupied planks,
        /// and no two dark squares touching along an edge.
        /// </summary>
        [Test]
        public void EachBoard_LaysThirtyTwoDarkSquaresOnTheLattice()
        {
            List<Bounds> parts = CollectChessParts(
                out _,
                out Vector3 origin,
                out Vector3 tangent,
                out Vector3 forward);

            for (int table = -1; table <= 1; table += 2)
            {
                Vector3 center = origin + tangent * (table * TableOffset);
                var occupied = new HashSet<Vector2Int>();
                for (int index = 0; index < parts.Count; index++)
                {
                    Bounds part = parts[index];
                    float top = part.max.y - origin.y;
                    if (top < BoardBandBottom ||
                        top > BoardBandTop ||
                        Mathf.Abs(part.size.x - SquareSize) > 0.001f ||
                        Mathf.Abs(part.size.z - SquareSize) > 0.001f)
                    {
                        continue;
                    }

                    Vector3 offset = part.center - center;
                    offset.y = 0f;
                    if (offset.magnitude > FieldSize)
                    {
                        continue;
                    }

                    Vector2Int cell = ResolveCell(offset, tangent, forward);
                    Assert.That(
                        cell.x,
                        Is.InRange(0, SquaresPerSide - 1),
                        "A dark square sits off the board.");
                    Assert.That(
                        cell.y,
                        Is.InRange(0, SquaresPerSide - 1),
                        "A dark square sits off the board.");
                    Assert.That(
                        occupied.Add(cell),
                        Is.True,
                        $"Two dark squares share {cell}.");
                    Assert.That(
                        (cell.x + cell.y) % 2,
                        Is.EqualTo(1),
                        $"{cell} breaks the alternation. The dark parity " +
                        "is the odd one, because both planks face along " +
                        "+Forward with the tangent to their left, so each " +
                        "man's near-right corner is (0,0) or (7,7) and " +
                        "both have to be light.");
                }

                Assert.That(
                    occupied,
                    Has.Count.EqualTo(
                        SquaresPerSide * SquaresPerSide / 2),
                    "A chess board carries thirty-two dark squares.");
            }
        }

        /// <summary>
        /// The board is drawn, not tiled. The park sheets ride world
        /// UVs, so the two halves have to reach the batcher as separate
        /// colours or the pattern is not there at all.
        /// </summary>
        [Test]
        public void Board_DrawsItsTwoColoursAsSeparateTimberBatches()
        {
            CityLayout layout = CreateLayout();
            CityDecorationPlan plan = CreatePlan(layout);
            var parent = new GameObject("Chess Board Batch Test");
            try
            {
                GameObject root = CityDecorationWorldBuilder.Build(
                    parent.transform,
                    layout,
                    plan);
                var names = new HashSet<string>();
                foreach (Renderer renderer in
                         root.GetComponentsInChildren<Renderer>(true))
                {
                    names.Add(renderer.name);
                }

                Assert.That(
                    names,
                    Contains.Item("Park Timber Masonry Details"),
                    "The light squares are the timber sheet on the " +
                    "masonry batch colour.");
                Assert.That(
                    names,
                    Contains.Item("Park Timber Street Details"),
                    "The dark squares are the timber sheet on the " +
                    "street batch colour.");
            }
            finally
            {
                Object.DestroyImmediate(parent);
            }
        }

        /// <summary>
        /// The set grew, and with it the clearance it reserves. A park
        /// feature is placed with TryAdd, so an over-large footprint
        /// does not fail loudly - it just stops being planted.
        /// </summary>
        [Test]
        public void DefaultCity_StillPlantsTheChessSetInsideItsRadius()
        {
            CityLayout layout = CreateLayout();
            CityDecorationPlan plan = CreatePlan(layout);
            int count = 0;
            for (int index = 0; index < plan.Descriptors.Count; index++)
            {
                if (plan.Descriptors[index].Kind ==
                    CityDecorationKind.ParkChessTables)
                {
                    count++;
                }
            }

            Assert.That(count, Is.EqualTo(1));

            List<Bounds> parts = CollectChessParts(
                out _,
                out Vector3 origin,
                out _,
                out _);
            float radius = CityDecorationValidator.ResolveProtectionRadius(
                CityDecorationKind.ParkChessTables);
            for (int index = 0; index < parts.Count; index++)
            {
                Bounds part = parts[index];
                foreach (Vector3 corner in DescribeFootprint(part))
                {
                    float distance = new Vector2(
                        corner.x - origin.x,
                        corner.z - origin.z).magnitude;
                    Assert.That(
                        distance,
                        Is.LessThanOrEqualTo(radius),
                        "The set reaches outside the ground it " +
                        "reserves against paths, trees and neighbours.");
                }
            }
        }

        private static Vector2Int ResolveCell(
            Vector3 offset,
            Vector3 tangent,
            Vector3 forward)
        {
            const float edge = (SquaresPerSide - 1) * 0.5f;
            return new Vector2Int(
                Mathf.RoundToInt(
                    Vector3.Dot(offset, tangent) / SquareSize + edge),
                Mathf.RoundToInt(
                    Vector3.Dot(offset, forward) / SquareSize + edge));
        }

        private static IEnumerable<Vector3> DescribeFeet(
            Vector3 origin,
            Vector3 tangent,
            Vector3 forward)
        {
            for (int table = -1; table <= 1; table += 2)
            {
                Vector3 center = origin + tangent * (table * TableOffset);
                yield return center;
                for (int side = -1; side <= 1; side += 2)
                {
                    for (int end = -1; end <= 1; end += 2)
                    {
                        yield return center +
                            tangent * (end *
                                (BenchWidth * 0.5f - BenchLegInset)) +
                            forward * (side * BenchZ);
                    }
                }
            }
        }

        private static IEnumerable<Vector3> DescribeFootprint(Bounds part)
        {
            yield return new Vector3(part.min.x, 0f, part.min.z);
            yield return new Vector3(part.min.x, 0f, part.max.z);
            yield return new Vector3(part.max.x, 0f, part.min.z);
            yield return new Vector3(part.max.x, 0f, part.max.z);
        }

        private static List<Bounds> CollectChessParts(
            out CityLayout layout,
            out Vector3 origin,
            out Vector3 tangent,
            out Vector3 forward)
        {
            layout = CreateLayout();
            CityDecorationPlan plan = CreatePlan(layout);
            for (int index = 0; index < plan.Descriptors.Count; index++)
            {
                CityDecorationDescriptor descriptor =
                    plan.Descriptors[index];
                if (descriptor.Kind !=
                    CityDecorationKind.ParkChessTables)
                {
                    continue;
                }

                Assert.That(
                    CityDecorationWorldBuilder.TryDescribeRecipeBasis(
                        layout,
                        descriptor,
                        out origin,
                        out forward),
                    Is.True);
                tangent = new Vector3(-forward.z, 0f, forward.x);
                var parts = new List<Bounds>();
                CityDecorationWorldBuilder.AppendPartBounds(
                    layout,
                    descriptor,
                    parts);
                Assert.That(parts, Is.Not.Empty);
                return parts;
            }

            Assert.Fail("The default city plants no chess set.");
            origin = default;
            tangent = default;
            forward = default;
            return null;
        }

        private static CityLayout CreateLayout()
        {
            return CityLayoutGenerator.Generate(
                CityBlueprintCatalog.Default,
                CityGenerationSettings.Default,
                Seed);
        }

        private static CityDecorationPlan CreatePlan(CityLayout layout)
        {
            return CityDecorationPlanner.CreatePlan(
                layout,
                RoadFencePlanner.CreatePlan(layout),
                CityNightFixturePlanner.CreatePlan(layout));
        }
    }
}

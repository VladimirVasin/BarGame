using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEngine;

namespace BarPromenade.Tests.EditMode
{
    public sealed class CityLakePlannerTests
    {
        private static CityLayout CreateDefaultLayout()
        {
            return CityLayoutGenerator.Generate(
                CityBlueprintCatalog.Default,
                CityGenerationSettings.Default,
                GameSessionState.DefaultCitySeed);
        }

        [Test]
        public void DefaultCity_PlansDeterministicBoatStation()
        {
            CityLayout layout = CreateDefaultLayout();

            CityLakePlan first = CityLakePlanner.Create(layout);
            CityLakePlan second = CityLakePlanner.Create(layout);

            Assert.That(first, Is.Not.Null);
            Assert.That(second, Is.Not.Null);
            CollectionAssert.AreEqual(first.Parts, second.Parts);
            CollectionAssert.AreEqual(first.Lamps, second.Lamps);
            Assert.That(
                first.Count,
                Is.LessThanOrEqualTo(CityLakePlan.MaximumPartCount));

            // The three silhouette anchors the concept is built on: a
            // pier over the water, hulls on the bank, a hut with a
            // board. Without all three this is a pond, not a station.
            Assert.That(
                first.GetCount(CityLakePartKind.PierDeck),
                Is.GreaterThan(8));
            Assert.That(
                first.GetCount(CityLakePartKind.PierPile),
                Is.GreaterThan(8));
            Assert.That(first.BoatCount, Is.GreaterThanOrEqualTo(4));
            Assert.That(
                first.GetCount(CityLakePartKind.Hut),
                Is.GreaterThan(0));
            Assert.That(
                first.GetCount(CityLakePartKind.HutSign),
                Is.GreaterThan(10),
                "The hire board should carry its lettering.");

            // A full row shows the whole vocabulary, the way the
            // cemetery's nearest grave row does.
            foreach (CityLakeBoatVariant variant in
                     Enum.GetValues(typeof(CityLakeBoatVariant)))
            {
                Assert.That(
                    first.GetBoatVariantCount(variant),
                    Is.GreaterThanOrEqualTo(1),
                    $"Missing boat variant {variant}.");
            }
        }

        [Test]
        public void DefaultCity_InsetsTheWaterlineAndCutsItsCorners()
        {
            CityLayout layout = CreateDefaultLayout();
            CityLakePlan plan = CityLakePlanner.Create(layout);
            CityLakeBasin basin = plan.Basin;

            // The whole point of the geometry pass: the 52 m square of
            // Water cells becomes a materially smaller body of water.
            Assert.That(basin.WaterCellBounds.width, Is.GreaterThan(50f));
            Assert.That(
                basin.WaterlineBounds.width,
                Is.LessThan(basin.WaterCellBounds.width * 0.75f),
                "The waterline must be inset well inside its cells.");
            Assert.That(basin.BevelMeters, Is.GreaterThan(3f));

            // A corner of the waterline rect is land; the middle of the
            // near edge is water. That is what makes it an octagon.
            Rect water = basin.WaterlineBounds;
            Assert.That(
                basin.ContainsWaterline(
                    new Vector2(water.xMin + 0.5f, water.yMin + 0.5f)),
                Is.False,
                "The cut corners must not be open water.");
            Assert.That(
                basin.ContainsWaterline(water.center),
                Is.True);

            // The bank falls, and the bed lies under the water.
            Assert.That(basin.BankTopY, Is.GreaterThan(basin.WaterTopY));
            Assert.That(basin.BedTopY, Is.LessThan(basin.WaterTopY));
            Assert.That(
                basin.BankTopY - basin.WaterTopY,
                Is.InRange(0.30f, 0.60f),
                "The lake basin drop is fixed by the elevation plan.");
        }

        [Test]
        public void DefaultCity_ClosesItsWaterlineWithVisibleGeometry()
        {
            CityLayout layout = CreateDefaultLayout();
            CityLakePlan plan = CityLakePlanner.Create(layout);
            CityLakeBasin basin = plan.Basin;

            // The revetment is what replaces the generic guard rail the
            // shore used to get, so it has to stand clear of the safe
            // step or we removed a barrier and put back a kerb.
            IReadOnlyList<CityLakePartDescriptor> boards = plan.Parts
                .Where(part =>
                    part.Kind == CityLakePartKind.Revetment)
                .ToArray();
            Assert.That(boards.Count, Is.GreaterThan(20));

            float bankToeY = basin.WaterTopY + 0.06f;
            foreach (CityLakePartDescriptor board in boards)
            {
                float top = board.Center.y + board.Size.y * 0.5f;
                float bottom = board.Center.y - board.Size.y * 0.5f;
                Assert.That(
                    top - bankToeY,
                    Is.GreaterThan(0.48f),
                    "A revetment board must read as a barrier.");
                Assert.That(
                    bottom,
                    Is.LessThan(basin.BedTopY + 0.06f),
                    "A revetment board must lap the bed, not float " +
                    "above it.");
            }

            // Every board blocks movement: the whole ring is physical.
            Assert.That(
                boards.All(board => board.BlocksMovement),
                Is.True);
        }

        [Test]
        public void DefaultCity_KeepsTheHullsAshoreAndThePierAboveWater()
        {
            CityLayout layout = CreateDefaultLayout();
            CityLakePlan plan = CityLakePlanner.Create(layout);
            CityLakeBasin basin = plan.Basin;

            foreach (CityLakePartDescriptor part in plan.Parts)
            {
                if (part.Kind == CityLakePartKind.Boat ||
                    part.Kind == CityLakePartKind.BoatRest)
                {
                    Assert.That(
                        basin.ContainsWaterline(new Vector2(
                            part.Center.x,
                            part.Center.z)),
                        Is.False,
                        $"Hire hull '{part.StableId}' is afloat.");
                    Assert.That(
                        part.Center.y,
                        Is.GreaterThan(basin.WaterTopY),
                        $"Hire hull '{part.StableId}' is under water.");
                }

                if (part.Kind == CityLakePartKind.PierDeck)
                {
                    Assert.That(
                        part.Center.y - part.Size.y * 0.5f,
                        Is.GreaterThan(basin.WaterTopY + 0.40f));
                }

                if (part.Kind == CityLakePartKind.PierPile)
                {
                    Assert.That(
                        part.Center.y - part.Size.y * 0.5f,
                        Is.LessThanOrEqualTo(basin.BedTopY + 0.05f),
                        $"Pile '{part.StableId}' does not reach the bed.");
                }
            }

            // The step up from the bank onto the deck stays walkable.
            CityLakePartDescriptor deck = plan.Parts.First(part =>
                part.Kind == CityLakePartKind.PierDeck);
            float deckTop = deck.Center.y + deck.Size.y * 0.5f;
            Assert.That(
                deckTop - basin.BankTopY,
                Is.InRange(0.0f, 0.28f),
                "Stepping onto the pier must not need a ramp.");
        }

        [Test]
        public void DefaultCity_LightsTheStationAndKeepsItsApproachClear()
        {
            CityLayout layout = CreateDefaultLayout();
            CityLakePlan plan = CityLakePlanner.Create(layout);

            // The shore carries no lamps at all. The station's only
            // light on the water is the one at the head of the pier.
            Assert.That(
                plan.GetLampCount(CityLakeLampKind.PierHead),
                Is.EqualTo(1));

            // The hut and its bulb stand or fall together, exactly as
            // the cemetery lodge and its porch bulb do.
            Assert.That(
                plan.GetLampCount(CityLakeLampKind.HutDoor),
                Is.EqualTo(
                    plan.GetCount(CityLakePartKind.Hut) > 0 ? 1 : 0));

            Assert.That(plan.Lamps.Count, Is.LessThanOrEqualTo(2));

            // The head lamp stands out over the water, not on the bank:
            // that is what gives it the whole pond to reflect in.
            CityLakeLampDescriptor head = plan.Lamps.First(
                lamp => lamp.Kind == CityLakeLampKind.PierHead);
            Assert.That(
                plan.Basin.ContainsWaterline(new Vector2(
                    head.GroundPosition.x,
                    head.GroundPosition.z)),
                Is.True,
                "The head lamp must stand over open water.");

            CityOpenAreaAccessDescriptor access = layout.OpenAreaAccesses
                .First(item => item.Feature == CityAreaFeatureKind.Lake);
            Rect approach = new Rect(
                access.ApproachBounds.x - 0.45f,
                access.ApproachBounds.y - 0.45f,
                access.ApproachBounds.width + 0.9f,
                access.ApproachBounds.height + 0.9f);
            foreach (CityLakePartDescriptor part in plan.Parts)
            {
                if (!part.BlocksMovement)
                {
                    continue;
                }

                Rect footprint = new Rect(
                    part.Center.x - part.Size.x * 0.5f -
                        part.Size.z * 0.5f,
                    part.Center.z - part.Size.x * 0.5f -
                        part.Size.z * 0.5f,
                    part.Size.x + part.Size.z,
                    part.Size.x + part.Size.z);
                bool overlaps =
                    footprint.xMin < approach.xMax &&
                    footprint.xMax > approach.xMin &&
                    footprint.yMin < approach.yMax &&
                    footprint.yMax > approach.yMin;
                Assert.That(
                    overlaps && part.Center.y < plan.GroundTopY + 2.1f,
                    Is.False,
                    $"Lake part '{part.StableId}' blocks the approach.");
            }
        }

        /// <summary>
        /// The regression this precinct was shipped broken by once: the
        /// blueprint marks the lake's inner cells `Water`, a water cell
        /// is not walkable, and every metre of the bank, the pier, the
        /// hut and the boats sits inside those cells. Without the
        /// planner's walkable footprints the player is clamped at the
        /// water-cell boundary and the whole boat station is sealed
        /// behind a `52 x 52 m` invisible box.
        ///
        /// Walks it at the full agent radius, from the street approach
        /// to the head of the pier, and checks the pond stays closed.
        /// </summary>
        [Test]
        public void DefaultCity_LetsThePlayerReachThePierAndNotTheWater()
        {
            const float radius = 0.35f;
            CityLayout layout = CreateDefaultLayout();
            CityLakePlan plan = CityLakePlanner.Create(layout);
            RoadWalkableArea area = RoadWalkableArea.FromLayout(layout);
            CityLakeBasin basin = plan.Basin;
            float y = plan.GroundTopY;

            // In from the street through the lake's canonical approach.
            CityOpenAreaAccessDescriptor access = layout.OpenAreaAccesses
                .First(item => item.Feature == CityAreaFeatureKind.Lake);
            Vector3 inward = access.OutwardNormal.normalized;
            for (int step = 0; step <= 40; step++)
            {
                Vector3 probe = access.Center +
                                inward * Mathf.Lerp(-4f, 16f, step / 40f);
                probe.y = y;
                Assert.That(
                    area.Contains(probe, radius),
                    Is.True,
                    $"The lake approach is blocked at {probe}.");
            }

            // The bank ring is walkable the whole way round, corners
            // included — the corners are where four strips have to
            // overlap rather than merely meet.
            Rect cells = basin.WaterCellBounds;
            foreach (Vector2 spot in new[]
                     {
                         new Vector2(cells.xMin + 4f, cells.center.y),
                         new Vector2(cells.xMax - 4f, cells.center.y),
                         new Vector2(cells.center.x, cells.yMin + 4f),
                         new Vector2(cells.center.x, cells.yMax - 4f),
                         new Vector2(cells.xMin + 4f, cells.yMin + 4f),
                         new Vector2(cells.xMax - 4f, cells.yMax - 4f),
                     })
            {
                Assert.That(
                    area.Contains(new Vector3(spot.x, y, spot.y), radius),
                    Is.True,
                    $"The lake bank is not walkable at {spot}.");
            }

            // Out along the pier to its head.
            Assert.That(
                plan.TryGetPart(
                    CityLakePlanner.PierDeckRootId,
                    out CityLakePartDescriptor root),
                Is.True);
            Assert.That(
                plan.TryGetPart(
                    CityLakePlanner.PierDeckHeadId,
                    out CityLakePartDescriptor head),
                Is.True);
            for (int step = 0; step <= 20; step++)
            {
                Vector3 probe = Vector3.Lerp(
                    root.Center, head.Center, step / 20f);
                probe.y = y;
                Assert.That(
                    area.Contains(probe, radius),
                    Is.True,
                    $"The pier is blocked at {probe}.");
            }

            // The four cut corners are real bank, and each is its own
            // 5.2 m triangle: covering only the waterline's axis-aligned
            // bounds leaves them as four invisible corners.
            Rect water = basin.WaterlineBounds;
            float bevel = basin.BevelMeters;
            foreach (Vector2 corner in new[]
                     {
                         new Vector2(water.xMin, water.yMin),
                         new Vector2(water.xMax, water.yMin),
                         new Vector2(water.xMin, water.yMax),
                         new Vector2(water.xMax, water.yMax),
                     })
            {
                float towardX = corner.x <= water.center.x ? 1f : -1f;
                float towardZ = corner.y <= water.center.y ? 1f : -1f;

                // Well inside the triangle: a point a third of the way
                // along the bevel on both axes is unambiguously land.
                var probe = new Vector3(
                    corner.x + towardX * bevel * 0.30f,
                    y,
                    corner.y + towardZ * bevel * 0.30f);
                Assert.That(
                    area.Contains(probe, radius),
                    Is.True,
                    $"The cut corner at {corner} is not walkable.");
            }

            // And the pond itself stays shut. Every sample that the
            // basin calls open water, and that is clear of the pier's
            // own deck, must be outside the mask.
            Rect pier = new Rect(
                Mathf.Min(root.Center.x, head.Center.x) - 2f,
                Mathf.Min(root.Center.z, head.Center.z) - 2f,
                Mathf.Abs(head.Center.x - root.Center.x) + 4f,
                Mathf.Abs(head.Center.z - root.Center.z) + 4f);
            int wet = 0;
            for (int ix = 1; ix < 24; ix++)
            {
                for (int iz = 1; iz < 24; iz++)
                {
                    var spot = new Vector2(
                        Mathf.Lerp(water.xMin, water.xMax, ix / 24f),
                        Mathf.Lerp(water.yMin, water.yMax, iz / 24f));
                    if (!basin.ContainsWaterline(spot) ||
                        pier.Contains(spot))
                    {
                        continue;
                    }

                    if (area.Contains(new Vector3(spot.x, y, spot.y), radius))
                    {
                        wet++;
                    }
                }
            }

            Assert.That(
                wet,
                Is.Zero,
                "Open water became walkable away from the pier.");
        }

        [Test]
        public void LakeWithoutWater_PlansNothingRatherThanThrowing()
        {
            CityLayout layout = CityLayoutGenerator.Generate(
                CityBlueprintCatalog.CreateLegacy(
                    CityGenerationSettings.Default),
                CityGenerationSettings.Default,
                GameSessionState.DefaultCitySeed);

            Assert.That(CityLakePlanner.Create(layout), Is.Null);
        }

        [Test]
        public void DefaultCity_SurvivesEveryValidationContract()
        {
            // Create already validates; this pins that the validator is
            // reachable and green for a handful of seeds, so a seed that
            // moves the pier or the hut fails here rather than in play.
            foreach (int seed in new[] { 1, 7, 512, 9137, 40213 })
            {
                CityLayout layout = CityLayoutGenerator.Generate(
                    CityBlueprintCatalog.Default,
                    CityGenerationSettings.Default,
                    seed);
                CityLakePlan plan = CityLakePlanner.Create(layout);
                Assert.That(plan, Is.Not.Null, $"Seed {seed}.");
                Assert.DoesNotThrow(
                    () => CityLakePlanner.ValidateOrThrow(layout, plan),
                    $"Seed {seed}.");
            }
        }
    }
}

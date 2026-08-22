using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEngine;

namespace BarPromenade.Tests.EditMode
{
    public sealed class CitySeacoastPlannerTests
    {
        private static CityLayout CreateDefaultLayout()
        {
            return CityLayoutGenerator.Generate(
                CityBlueprintCatalog.Default,
                CityGenerationSettings.Default,
                GameSessionState.DefaultCitySeed);
        }

        [Test]
        public void DefaultCity_PlansDeterministicSeacoast()
        {
            CityLayout layout = CreateDefaultLayout();

            CitySeacoastPlan first = CitySeacoastPlanner.Create(layout);
            CitySeacoastPlan second = CitySeacoastPlanner.Create(layout);

            Assert.That(first, Is.Not.Null);
            Assert.That(second, Is.Not.Null);
            CollectionAssert.AreEqual(first.Parts, second.Parts);
            CollectionAssert.AreEqual(first.Lamps, second.Lamps);
            Assert.That(
                first.Count,
                Is.LessThanOrEqualTo(CitySeacoastPlan.MaximumPartCount));

            // The silhouette anchors the concept is built on: the mol
            // with its beacon, the transplanted boat station, the
            // footbridge over the mouth, and the wild shore's piles
            // and barge. Without them this is a sand strip, not a
            // coast.
            Assert.That(
                first.GetCount(CitySeacoastPartKind.MolDeck),
                Is.GreaterThan(10));
            Assert.That(
                first.GetCount(CitySeacoastPartKind.BeaconTower),
                Is.GreaterThan(3));
            Assert.That(
                first.GetCount(CitySeacoastPartKind.PierDeck),
                Is.GreaterThan(8));
            Assert.That(
                first.GetCount(CitySeacoastPartKind.Hut),
                Is.GreaterThan(0));
            Assert.That(
                first.GetCount(CitySeacoastPartKind.HutSign),
                Is.GreaterThan(10),
                "The hire board should carry its lettering.");
            Assert.That(
                first.GetCount(CitySeacoastPartKind.EsplanadeSlab),
                Is.GreaterThan(15));
            Assert.That(
                first.GetCount(CitySeacoastPartKind.EsplanadeParapet),
                Is.GreaterThan(15));
            Assert.That(
                first.GetCount(CitySeacoastPartKind.FootbridgeDeck),
                Is.GreaterThan(8));
            Assert.That(
                first.GetCount(CitySeacoastPartKind.RottenPile),
                Is.GreaterThanOrEqualTo(8));
            Assert.That(
                first.GetCount(CitySeacoastPartKind.Barge),
                Is.GreaterThanOrEqualTo(7));
            Assert.That(
                first.GetCount(CitySeacoastPartKind.Bench),
                Is.GreaterThanOrEqualTo(9));
            Assert.That(first.BoatCount, Is.GreaterThanOrEqualTo(4));

            // A full row shows the whole vocabulary, the way the
            // lake's did before the station moved.
            foreach (CitySeacoastBoatVariant variant in
                     Enum.GetValues(typeof(CitySeacoastBoatVariant)))
            {
                Assert.That(
                    first.GetBoatVariantCount(variant),
                    Is.GreaterThanOrEqualTo(1),
                    $"Missing boat variant {variant}.");
            }
        }

        [Test]
        public void DefaultCity_ZonesItsShoreAroundTheMouth()
        {
            CityLayout layout = CreateDefaultLayout();
            CitySeacoastPlan plan = CitySeacoastPlanner.Create(layout);
            CitySeacoastFrame frame = plan.Frame;

            // The waterline is the single straight line between the
            // sand row and the sea row.
            Assert.That(
                frame.WaterlineZ,
                Is.EqualTo(frame.SeaRowBounds.yMin).Within(0.001f));
            Assert.That(
                frame.WaterlineZ,
                Is.EqualTo(frame.BeachRowBounds.yMax).Within(0.001f));
            Assert.That(frame.SeaTopY, Is.LessThan(frame.BeachEdgeTopY));

            // The river corridor cuts the zones: port west of it, the
            // esplanade east, the wild shore beyond.
            Assert.That(
                frame.WestZone.xMax,
                Is.EqualTo(frame.ChannelXMin).Within(0.001f));
            Assert.That(
                frame.CenterZone.xMin,
                Is.EqualTo(frame.ChannelXMax).Within(0.001f));
            Assert.That(
                frame.EastZone.xMin,
                Is.EqualTo(frame.CenterZone.xMax).Within(0.001f));

            Assert.That(
                plan.TryGetPart(
                    CitySeacoastPlanner.MolDeckRootId,
                    out CitySeacoastPartDescriptor molRoot),
                Is.True);
            Assert.That(
                frame.WestZone.Contains(new Vector2(
                    molRoot.Center.x,
                    molRoot.Center.z)),
                Is.True,
                "The mol belongs to the dead port west of the mouth.");

            Assert.That(
                plan.TryGetPart(
                    CitySeacoastPlanner.PierDeckRootId,
                    out CitySeacoastPartDescriptor pierRoot),
                Is.True);
            Assert.That(
                frame.CenterZone.Contains(new Vector2(
                    pierRoot.Center.x,
                    pierRoot.Center.z)),
                Is.True,
                "The boat station belongs to the esplanade zone.");

            CitySeacoastPartDescriptor barge = plan.Parts.First(
                part => part.Kind == CitySeacoastPartKind.Barge);
            Assert.That(
                frame.EastZone.Contains(new Vector2(
                    barge.Center.x,
                    barge.Center.z)),
                Is.True,
                "The barge strands off the wild east shore.");

            // The training sill spans the mouth itself, crest above
            // both waters, so the river sheet and the sea sheet never
            // touch.
            CitySeacoastPartDescriptor sill = plan.Parts.First(part =>
                part.StableId == "seacoast-mouth-sill");
            Assert.That(
                sill.Center.x,
                Is.EqualTo(
                    (frame.ChannelXMin + frame.ChannelXMax) * 0.5f)
                    .Within(1.5f));
            float sillCrest = sill.Center.y + sill.Size.y * 0.5f;
            Assert.That(
                sillCrest,
                Is.GreaterThan(frame.SeaTopY + 0.25f),
                "The sill crest must clear the sea's highest swell.");
            Assert.That(
                sillCrest,
                Is.GreaterThan(0.15f),
                "The sill crest must clear the river's mouth water.");
        }

        [Test]
        public void DefaultCity_KeepsHullsAshoreAndDecksAboveWater()
        {
            CityLayout layout = CreateDefaultLayout();
            CitySeacoastPlan plan = CitySeacoastPlanner.Create(layout);
            CitySeacoastFrame frame = plan.Frame;

            foreach (CitySeacoastPartDescriptor part in plan.Parts)
            {
                if (part.Kind == CitySeacoastPartKind.Boat ||
                    part.Kind == CitySeacoastPartKind.Driftwood)
                {
                    Assert.That(
                        part.Center.z,
                        Is.LessThan(frame.WaterlineZ),
                        $"'{part.StableId}' reaches into the sea.");
                }

                if (part.Kind == CitySeacoastPartKind.PierDeck)
                {
                    Assert.That(
                        part.Center.y - part.Size.y * 0.5f,
                        Is.GreaterThan(frame.SeaTopY + 0.40f));
                }

                if (part.Kind == CitySeacoastPartKind.MolDeck)
                {
                    Assert.That(
                        part.Center.y + part.Size.y * 0.5f,
                        Is.GreaterThan(frame.SeaTopY + 1.2f),
                        "The mol deck rides well above the swell.");
                }
            }

            // The barge is stranded, not afloat: her bottom sits below
            // the water's top.
            float bargeBottom = plan.Parts
                .Where(part => part.Kind == CitySeacoastPartKind.Barge)
                .Min(part => part.Center.y - part.Size.y * 0.5f);
            Assert.That(bargeBottom, Is.LessThan(frame.SeaTopY - 0.2f));

            // The step from the sand onto the pier root stays a step.
            Assert.That(
                plan.TryGetPart(
                    CitySeacoastPlanner.PierDeckRootId,
                    out CitySeacoastPartDescriptor root),
                Is.True);
            float deckTop = root.Center.y + root.Size.y * 0.5f;
            float sandAtRoot = CityTerrainSurfacePlan.SampleTop(
                layout,
                layout.Surfaces.First(surface =>
                    surface.Feature ==
                        CityAreaFeatureKind.NorthWaterfront &&
                    surface.Kind == CitySurfaceKind.Beach &&
                    surface.WorldBounds.Contains(new Vector2(
                        root.Center.x,
                        root.Center.z))),
                new Vector2(root.Center.x, root.Center.z));
            Assert.That(
                Mathf.Abs(deckTop - sandAtRoot),
                Is.LessThanOrEqualTo(0.28f),
                "Stepping onto the pier must not need a ramp.");
        }

        [Test]
        public void DefaultCity_ClosesItsEdgesWithVisibleGeometry()
        {
            CityLayout layout = CreateDefaultLayout();
            CitySeacoastPlan plan = CitySeacoastPlanner.Create(layout);
            CitySeacoastFrame frame = plan.Frame;

            // The sea wall is what marks the esplanade's water edge;
            // it has to stand clear of the safe step over the sand at
            // the waterline or it is a kerb, not a wall.
            IReadOnlyList<CitySeacoastPartDescriptor> wall = plan.Parts
                .Where(part =>
                    part.Kind == CitySeacoastPartKind.EsplanadeParapet)
                .ToArray();
            Assert.That(wall.Count, Is.GreaterThan(15));
            foreach (CitySeacoastPartDescriptor board in wall)
            {
                Assert.That(
                    board.Center.y + board.Size.y * 0.5f -
                    frame.BeachEdgeTopY,
                    Is.GreaterThan(0.48f),
                    "A sea-wall board must read as a barrier.");
                Assert.That(board.BlocksMovement, Is.True);
            }

            // The wall's coverage plus its two bridged gaps accounts
            // for the whole esplanade span.
            float walled = wall.Sum(part =>
                Mathf.Max(part.Size.x, part.Size.z));
            float span = frame.CenterZone.width - 1f;
            Assert.That(
                walled + (2.2f + 0.3f) + (4f + 0.3f),
                Is.GreaterThanOrEqualTo(span - 0.05f),
                "The esplanade waterline has an unbridged gap.");

            // The mol parapets both sides plus the head, with only the
            // stair-bridged root open.
            float parapeted = plan.Parts
                .Where(part =>
                    part.Kind == CitySeacoastPartKind.MolParapet)
                .Sum(part => Mathf.Max(part.Size.x, part.Size.z));
            Assert.That(parapeted, Is.GreaterThan(40f));
            Assert.That(
                plan.GetCount(CitySeacoastPartKind.MolStair),
                Is.GreaterThan(0),
                "The mol's open root end needs its visible stair.");

            // The footbridge carries a full rail on each side of its
            // open water.
            float bridgeSpan =
                frame.ChannelXMax - frame.ChannelXMin + 2.6f;
            int fullRails = plan.Parts.Count(part =>
                part.Kind == CitySeacoastPartKind.FootbridgeRail &&
                Mathf.Max(part.Size.x, part.Size.z) >=
                    bridgeSpan - 0.7f);
            Assert.That(fullRails, Is.GreaterThanOrEqualTo(2));
        }

        [Test]
        public void DefaultCity_LightsTheShoreAndKeepsItsApproachClear()
        {
            CityLayout layout = CreateDefaultLayout();
            CitySeacoastPlan plan = CitySeacoastPlanner.Create(layout);
            CitySeacoastFrame frame = plan.Frame;

            // One beacon on the mol head, one hand lamp on the pier,
            // the hut bulb with its hut, and a sparse esplanade string:
            // enough to walk by, too few to call the place maintained.
            Assert.That(
                plan.GetLampCount(CitySeacoastLampKind.Beacon),
                Is.EqualTo(1));
            Assert.That(
                plan.GetLampCount(CitySeacoastLampKind.PierHead),
                Is.EqualTo(1));
            Assert.That(
                plan.GetLampCount(CitySeacoastLampKind.HutDoor),
                Is.EqualTo(
                    plan.GetCount(CitySeacoastPartKind.Hut) > 0
                        ? 1
                        : 0));
            Assert.That(
                plan.GetLampCount(CitySeacoastLampKind.Esplanade),
                Is.InRange(3, 8));

            // The beacon stands out over the sea on the mol head — it
            // is navigation light, and the water is what it is for.
            CitySeacoastLampDescriptor beacon = plan.Lamps.First(
                lamp => lamp.Kind == CitySeacoastLampKind.Beacon);
            Assert.That(
                beacon.GroundPosition.z,
                Is.GreaterThan(frame.WaterlineZ + 8f));

            CityOpenAreaAccessDescriptor access = layout
                .OpenAreaAccesses
                .First(item =>
                    item.Feature ==
                    CityAreaFeatureKind.NorthWaterfront);
            Rect approach = new Rect(
                access.ApproachBounds.x - 0.45f,
                access.ApproachBounds.y - 0.45f,
                access.ApproachBounds.width + 0.9f,
                access.ApproachBounds.height + 0.9f);
            foreach (CitySeacoastPartDescriptor part in plan.Parts)
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
                    overlaps &&
                    part.Center.y < frame.BeachEdgeTopY + 4f,
                    Is.False,
                    $"Seacoast part '{part.StableId}' blocks the " +
                    "approach.");
            }
        }

        /// <summary>
        /// The waterfront's version of the lake's sealed-pond walk:
        /// the sand row is walkable, the three authored decks — mol,
        /// pier, footbridge — carry a person out over water cells,
        /// and the open sea stays clamped shut everywhere else.
        /// </summary>
        [Test]
        public void DefaultCity_LetsThePlayerWalkTheDecksAndNotTheSea()
        {
            const float radius = 0.35f;
            CityLayout layout = CreateDefaultLayout();
            CitySeacoastPlan plan = CitySeacoastPlanner.Create(layout);
            RoadWalkableArea area = RoadWalkableArea.FromLayout(layout);
            CitySeacoastFrame frame = plan.Frame;
            float y = frame.BeachEdgeTopY;

            // In from the street through the coast's canonical
            // approach.
            CityOpenAreaAccessDescriptor access = layout
                .OpenAreaAccesses
                .First(item =>
                    item.Feature ==
                    CityAreaFeatureKind.NorthWaterfront);
            Vector3 inward = access.OutwardNormal.normalized;
            for (int step = 0; step <= 40; step++)
            {
                Vector3 probe = access.Center +
                                inward * Mathf.Lerp(
                                    -4f, 16f, step / 40f);
                probe.y = y;
                Assert.That(
                    area.Contains(probe, radius),
                    Is.True,
                    $"The coast approach is blocked at {probe}.");
            }

            // The sand itself, in all three zones, back from the
            // river channel's cut.
            foreach (Vector2 spot in new[]
                     {
                         new Vector2(
                             frame.WestZone.center.x,
                             frame.WaterlineZ - 8f),
                         new Vector2(
                             frame.CenterZone.center.x,
                             frame.WaterlineZ - 3f),
                         new Vector2(
                             frame.EastZone.center.x,
                             frame.WaterlineZ - 12f),
                     })
            {
                Assert.That(
                    area.Contains(
                        new Vector3(spot.x, y, spot.y),
                        radius),
                    Is.True,
                    $"The sand is not walkable at {spot}.");
            }

            // Out along the mol to the beacon, along the pier to the
            // head, and over the footbridge from bank to bank.
            WalkBetween(
                plan, area, radius,
                CitySeacoastPlanner.MolDeckRootId,
                CitySeacoastPlanner.MolDeckHeadId,
                "mol");
            WalkBetween(
                plan, area, radius,
                CitySeacoastPlanner.PierDeckRootId,
                CitySeacoastPlanner.PierDeckHeadId,
                "pier");
            WalkBetween(
                plan, area, radius,
                CitySeacoastPlanner.FootbridgeDeckWestId,
                CitySeacoastPlanner.FootbridgeDeckEastId,
                "footbridge");

            // And the sea stays shut. Every sample in the sea row
            // clear of the two authored decks must be outside the
            // mask.
            Assert.That(
                plan.TryGetPart(
                    CitySeacoastPlanner.MolDeckHeadId,
                    out CitySeacoastPartDescriptor molHead),
                Is.True);
            Assert.That(
                plan.TryGetPart(
                    CitySeacoastPlanner.PierDeckHeadId,
                    out CitySeacoastPartDescriptor pierHead),
                Is.True);
            Rect molClear = new Rect(
                molHead.Center.x - 4f,
                frame.WaterlineZ - 8f,
                8f,
                frame.SeaRowBounds.height + 8f);
            Rect pierClear = new Rect(
                pierHead.Center.x - 4f,
                frame.WaterlineZ - 8f,
                8f,
                frame.SeaRowBounds.height + 8f);
            int wet = 0;
            for (int ix = 1; ix < 40; ix++)
            {
                for (int iz = 1; iz < 10; iz++)
                {
                    var spot = new Vector2(
                        Mathf.Lerp(
                            frame.SeaRowBounds.xMin,
                            frame.SeaRowBounds.xMax,
                            ix / 40f),
                        Mathf.Lerp(
                            frame.WaterlineZ + 0.8f,
                            frame.SeaRowBounds.yMax - 0.8f,
                            iz / 10f));
                    if (molClear.Contains(spot) ||
                        pierClear.Contains(spot))
                    {
                        continue;
                    }

                    if (area.Contains(
                            new Vector3(spot.x, y, spot.y),
                            radius))
                    {
                        wet++;
                    }
                }
            }

            Assert.That(
                wet,
                Is.Zero,
                "Open sea became walkable away from the decks.");
        }

        private static void WalkBetween(
            CitySeacoastPlan plan,
            RoadWalkableArea area,
            float radius,
            string fromId,
            string toId,
            string label)
        {
            Assert.That(
                plan.TryGetPart(
                    fromId,
                    out CitySeacoastPartDescriptor from),
                Is.True,
                $"Missing part '{fromId}'.");
            Assert.That(
                plan.TryGetPart(
                    toId,
                    out CitySeacoastPartDescriptor to),
                Is.True,
                $"Missing part '{toId}'.");
            for (int step = 0; step <= 20; step++)
            {
                Vector3 probe = Vector3.Lerp(
                    from.Center, to.Center, step / 20f);
                probe.y = from.Center.y + 0.4f;
                Assert.That(
                    area.Contains(probe, radius),
                    Is.True,
                    $"The {label} is blocked at {probe}.");
            }
        }

        [Test]
        public void LegacyCity_PlansNothingRatherThanThrowing()
        {
            CityLayout layout = CityLayoutGenerator.Generate(
                CityBlueprintCatalog.CreateLegacy(
                    CityGenerationSettings.Default),
                CityGenerationSettings.Default,
                GameSessionState.DefaultCitySeed);

            Assert.That(CitySeacoastPlanner.Create(layout), Is.Null);
        }

        [Test]
        public void DefaultCity_SurvivesEveryValidationContract()
        {
            // Create already validates; this pins that the validator
            // is reachable and green across seeds, so a seed that
            // drifts a hull into the sea or a crate into the approach
            // fails here rather than in play.
            foreach (int seed in new[] { 1, 7, 512, 9137, 40213 })
            {
                CityLayout layout = CityLayoutGenerator.Generate(
                    CityBlueprintCatalog.Default,
                    CityGenerationSettings.Default,
                    seed);
                CitySeacoastPlan plan =
                    CitySeacoastPlanner.Create(layout);
                Assert.That(plan, Is.Not.Null, $"Seed {seed}.");
                Assert.DoesNotThrow(
                    () => CitySeacoastPlanner.ValidateOrThrow(
                        layout,
                        plan),
                    $"Seed {seed}.");
            }
        }
    }
}

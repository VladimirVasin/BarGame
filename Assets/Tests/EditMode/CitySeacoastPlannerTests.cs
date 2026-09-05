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

            // The silhouette anchors the concept is built on: the
            // mol, the transplanted boat station, the footbridge
            // over the mouth, and the wild shore's piles and barge.
            // Without them this is a sand strip, not a coast. (The
            // navigation light moved offshore: the lighthouse island
            // pins its own silhouette in its own tests.)
            Assert.That(
                first.GetCount(CitySeacoastPartKind.MolDeck),
                Is.GreaterThan(10));
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

            // The mouth banks close the channel's cut through the
            // sand row on both sides — the granite quay walls end at
            // the promenade, and without the banks the bare terrain
            // edge is a hole in the world.
            foreach (int side in new[] { 0, 1 })
            {
                float edgeX = side == 0
                    ? frame.ChannelXMin
                    : frame.ChannelXMax;
                Assert.That(
                    plan.Parts.Any(part =>
                        part.Kind == CitySeacoastPartKind.MouthBank &&
                        Mathf.Abs(part.Center.x - edgeX) < 1f),
                    Is.True,
                    $"The mouth's side {side} has no sand bank.");
            }
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

            // One hand lamp on the pier, the hut bulb with its hut,
            // and a sparse esplanade string: enough to walk by, too
            // few to call the place maintained.
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

        [Test]
        public void WorldBuilder_LetsTheProductionControllerCrossBothQuayShoreJunctions()
        {
            CityLayout layout = CreateDefaultLayout();
            CitySeacoastPlan coast = CitySeacoastPlanner.Create(layout);
            RoadWalkableArea walkable = RoadWalkableArea.FromLayout(layout);
            var root = new GameObject("Quay Shore Collision Test");
            try
            {
                CityWorldBuilder.Build(
                    root.transform,
                    layout,
                    CityGenerationSettings.Default);
                Physics.SyncTransforms();

                foreach (CityRiverPromenadeDescriptor promenade in
                         layout.River.Promenades)
                {
                    string stairId = promenade.WestBank
                        ? CitySeacoastPlanner.PromenadeStairWestId
                        : CitySeacoastPlanner.PromenadeStairEastId;
                    List<CitySeacoastPartDescriptor> steps = coast.Parts
                        .Where(part =>
                            part.Kind ==
                                CitySeacoastPartKind.EsplanadeStair &&
                            (part.StableId == stairId ||
                             part.StableId.StartsWith(stairId + "-")))
                        .OrderBy(part => part.Center.z)
                        .ToList();
                    Assert.That(steps, Is.Not.Empty);
                    foreach (CitySeacoastPartDescriptor step in steps)
                    {
                        Assert.That(
                            step.Center.x,
                            Is.EqualTo(promenade.Bounds.center.x)
                                .Within(0.001f),
                            $"{step.StableId} is not centred on its quay.");
                        Assert.That(
                            step.Size.x,
                            Is.EqualTo(promenade.Bounds.width)
                                .Within(0.001f),
                            $"{step.StableId} does not cover the full " +
                            "visible quay opening.");
                    }

                    Rect junction = CitySeacoastPlanner
                        .CreatePromenadeShoreJunctionBounds(promenade);
                    const float auditRadius =
                        CityGroundTraversalPlanner.MaximumAgentRadius;
                    for (int sample = 0; sample <= 8; sample++)
                    {
                        float x = Mathf.Lerp(
                            promenade.Bounds.xMin + auditRadius,
                            promenade.Bounds.xMax - auditRadius,
                            sample / 8f);
                        for (int depth = 0; depth <= 8; depth++)
                        {
                            float z = Mathf.Lerp(
                                junction.yMin + auditRadius,
                                junction.yMax - auditRadius,
                                depth / 8f);
                            Assert.That(
                                walkable.Contains(
                                    new Vector3(x, promenade.NorthY, z),
                                    auditRadius),
                                Is.True,
                                $"{stairId} has a navigation clamp at " +
                                $"({x:F2}, {z:F2}).");
                        }
                    }

                    float promenadeZ = promenade.Bounds.yMax - 0.6f;
                    float promenadeTop = Mathf.Lerp(
                        promenade.SouthY,
                        promenade.NorthY,
                        Mathf.InverseLerp(
                            promenade.Bounds.yMin,
                            promenade.Bounds.yMax,
                            promenadeZ));
                    CitySeacoastPartDescriptor last =
                        steps[steps.Count - 1];
                    float shoreZ = last.Center.z +
                                   last.Size.z * 0.5f + 0.6f;
                    const float physicalInset = 0.34f;
                    foreach (float x in new[]
                             {
                                 promenade.Bounds.xMin + physicalInset,
                                 promenade.Bounds.center.x,
                                 promenade.Bounds.xMax - physicalInset
                             })
                    {
                        float shoreTop =
                            CitySeacoastPlanner.SampleShoreWalkTop(
                                layout,
                                coast.Frame,
                                x,
                                shoreZ);
                        AssertControllerCrosses(
                            root.transform,
                            walkable,
                            new Vector3(x, promenadeTop, promenadeZ),
                            new Vector3(x, shoreTop, shoreZ),
                            stairId + $" promenade-to-shore x={x:F2}");
                        AssertControllerCrosses(
                            root.transform,
                            walkable,
                            new Vector3(x, shoreTop, shoreZ),
                            new Vector3(x, promenadeTop, promenadeZ),
                            stairId + $" shore-to-promenade x={x:F2}");
                    }
                }
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(root);
            }
        }

        private static void AssertControllerCrosses(
            Transform parent,
            RoadWalkableArea walkable,
            Vector3 startGround,
            Vector3 targetGround,
            string label)
        {
            var actor = new GameObject(label);
            actor.transform.SetParent(parent, false);
            CharacterController controller =
                actor.AddComponent<CharacterController>();
            controller.height = 1.7f;
            controller.radius = 0.32f;
            controller.center = new Vector3(0f, 0.85f, 0f);
            controller.stepOffset = 0.28f;
            controller.slopeLimit = 45f;
            controller.skinWidth = PlayerFactory.GroundedRootOffset;
            actor.transform.position = startGround + Vector3.up * 0.12f;
            Physics.SyncTransforms();
            for (int settle = 0; settle < 12; settle++)
            {
                controller.Move(Vector3.down * 0.08f);
            }

            int stalls = 0;
            for (int move = 0; move < 180; move++)
            {
                Vector3 current = actor.transform.position;
                if (Mathf.Abs(current.z - targetGround.z) <= 0.08f)
                {
                    UnityEngine.Object.DestroyImmediate(actor);
                    return;
                }

                Vector3 desired = current;
                desired.x = targetGround.x;
                desired.z = Mathf.MoveTowards(
                    current.z,
                    targetGround.z,
                    0.08f);
                Vector3 constrained = walkable.Constrain(
                    current,
                    desired,
                    controller.radius);
                float beforeZ = current.z;
                controller.Move(
                    constrained - current + Vector3.down * 0.08f);
                stalls = Mathf.Abs(actor.transform.position.z - beforeZ) <
                         0.005f
                    ? stalls + 1
                    : 0;
                if (stalls >= 8)
                {
                    break;
                }
            }

            Vector3 stopped = actor.transform.position;
            UnityEngine.Object.DestroyImmediate(actor);
            Assert.Fail(
                $"{label} stopped at ({stopped.x:F2}, " +
                $"{stopped.y:F2}, {stopped.z:F2}) before " +
                $"Z={targetGround.z:F2}.");
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
        public void SeaMaterial_RollsShorewardGlintsAndFitsTheCrestRoom()
        {
            Material sea = CitySeaResources.WaterMaterial;

            // Structural, not art direction: the swell travels
            // shoreward (negative Z - the waterline is the sea row's
            // south edge) so the rollers come in instead of standing
            // and breathing, the additional-specular path is what
            // carries the beacon's glitter, and the summed crest stays
            // inside the surface factory's culling headroom so
            // retuning the swell can never reintroduce pops.
            Vector4 flow = sea.GetVector("_FlowDirection");
            Assert.That(flow.x, Is.Zero);
            Assert.That(
                flow.y,
                Is.LessThan(0f),
                "The sea's swell must travel toward the shore.");
            Assert.That(
                sea.GetFloat("_AdditionalSpecular"),
                Is.GreaterThan(0f));
            Assert.That(
                CitySeaResources.WaveHeight *
                CitySeaResources.CrestFactor,
                Is.LessThan(CityWaterSurfaceFactory.CrestAllowance));

            // The tallest crest must also pass under the lowest deck
            // the planner will validate: pier decks are held at least
            // 0.40 m over the sea, and a crest above that would break
            // through the boards.
            Assert.That(
                CitySeaResources.WaveHeight *
                CitySeaResources.CrestFactor,
                Is.LessThan(0.40f),
                "The swell reaches the pier deck's validated floor.");

            // The near sand remains legible through the water, with
            // foam reaching less deeply than the water's colour fade.
            Assert.That(
                CitySeaResources.FoamDistance,
                Is.LessThan(CitySeaResources.DepthFadeDistance));
        }

        [Test]
        public void DefaultCity_PoursTheRiverIntoTheSea()
        {
            CityLayout layout = CreateDefaultLayout();
            CitySeacoastPlan plan = CitySeacoastPlanner.Create(layout);
            CitySeacoastFrame frame = plan.Frame;

            // The frame carries the river's own surface height at the
            // waterline, and it is the real one: the north edge of
            // the mouth segment's sheet.
            CityRiverSegmentDescriptor mouth = layout.River.Segments
                .OrderBy(segment => segment.Cell.y)
                .Last();
            Assert.That(
                frame.MouthWaterY,
                Is.EqualTo(mouth.NorthWaterY).Within(0.001f));

            // The spill continues that sheet: same start height (the
            // same material carries the same world-driven waves, so
            // the joint is invisible), the exact channel width, and a
            // far edge that dives under the sea's datum so the sea
            // sheet always covers its end.
            Rect spill =
                CitySeacoastSeaLayout.CreateMouthSpillRect(frame);
            Assert.That(
                spill.xMin,
                Is.EqualTo(frame.ChannelXMin).Within(0.001f));
            Assert.That(
                spill.xMax,
                Is.EqualTo(frame.ChannelXMax).Within(0.001f));
            Assert.That(
                spill.yMin,
                Is.EqualTo(frame.WaterlineZ).Within(0.001f));
            Assert.That(spill.height, Is.GreaterThan(2f));
            Assert.That(
                frame.SeaTopY - CitySeacoastSeaLayout.SpillDip,
                Is.LessThan(frame.SeaTopY));
            Assert.That(
                frame.MouthWaterY,
                Is.GreaterThanOrEqualTo(frame.SeaTopY),
                "The river must arrive at or above the sea, " +
                "or the spill would climb.");

            // The banks close the cut continuously on both sides,
            // from the promenade's last quay wall to the waterline,
            // feet buried below the river's surface.
            for (int side = 0; side < 2; side++)
            {
                float edgeX = side == 0
                    ? frame.ChannelXMin
                    : frame.ChannelXMax;
                var banks = plan.Parts
                    .Where(part =>
                        part.Kind ==
                            CitySeacoastPartKind.MouthBank &&
                        Mathf.Abs(part.Center.x - edgeX) < 1f)
                    .OrderBy(part => part.Center.z)
                    .ToList();
                Assert.That(banks.Count, Is.GreaterThanOrEqualTo(4));

                float reach = frame.BeachRowBounds.yMin + 0.05f;
                foreach (CitySeacoastPartDescriptor bank in banks)
                {
                    float z0 = bank.Center.z - bank.Size.z * 0.5f;
                    float z1 = bank.Center.z + bank.Size.z * 0.5f;
                    Assert.That(
                        z0,
                        Is.LessThanOrEqualTo(reach),
                        $"Gap in the mouth bank before '{bank.StableId}'.");
                    reach = Mathf.Max(reach, z1 + 0.05f);

                    Assert.That(
                        bank.Center.y - bank.Size.y * 0.5f,
                        Is.LessThan(frame.MouthWaterY - 0.5f),
                        $"'{bank.StableId}' floats above the bed.");
                    Assert.That(
                        bank.Center.y + bank.Size.y * 0.5f,
                        Is.LessThanOrEqualTo(
                            frame.BeachEdgeTopY + 2.5f),
                        $"'{bank.StableId}' towers over the sand.");
                }

                Assert.That(
                    reach,
                    Is.GreaterThanOrEqualTo(frame.WaterlineZ - 0.1f),
                    $"The side {side} bank stops short of the sea.");
            }
        }

        [Test]
        public void DefaultCity_ChunksItsSeaSheetsAndShelvesTheShore()
        {
            CityLayout layout = CreateDefaultLayout();
            CitySeacoastPlan plan = CitySeacoastPlanner.Create(layout);
            CitySeacoastFrame frame = plan.Frame;

            var sheets = new List<Rect>();
            CitySeacoastSeaLayout.CreateSheetRects(frame, sheets);
            Assert.That(sheets.Count, Is.GreaterThan(1));
            float covered = 0f;
            foreach (Rect sheet in sheets)
            {
                Assert.That(
                    sheet.width,
                    Is.LessThanOrEqualTo(
                        CitySeacoastSeaLayout.MaximumSheetWidth +
                        0.01f),
                    "A sheet wider than a world chunk never culls.");
                Assert.That(
                    sheet.yMin,
                    Is.EqualTo(frame.WaterlineZ).Within(0.001f));
                Assert.That(
                    sheet.yMax,
                    Is.EqualTo(
                        frame.SeaRowBounds.yMax +
                        CitySeacoastSeaLayout.ApronReach)
                        .Within(0.001f));
                covered += sheet.width;
            }

            Assert.That(
                covered,
                Is.EqualTo(frame.SeaRowBounds.width).Within(0.05f),
                "The sheets must tile the sea row exactly.");

            int shores = 0;
            foreach (CitySurfaceDescriptor surface in layout.Surfaces)
            {
                if (surface.Kind != CitySurfaceKind.Beach ||
                    surface.Feature != CityAreaFeatureKind.NorthWaterfront)
                    continue;
                shores++;
                for (int across = 0; across <= 4; across++)
                {
                    var edge = new Vector2(
                        Mathf.Lerp(surface.WorldBounds.xMin, surface.WorldBounds.xMax, across / 4f),
                        surface.WorldBounds.yMax);
                    float previous = CityTerrainSurfacePlan.SampleTop(layout, surface, edge);
                    Assert.That(
                        CitySeacoastSeaLayout.SampleSeabedTop(layout, surface, edge),
                        Is.EqualTo(previous).Within(0.0001f),
                        "The sand must meet its own underwater continuation without a drop.");
                    Assert.That(Vector3.Dot(
                            CityTerrainSurfacePlan.SampleNormal(layout, surface, edge),
                            CitySeacoastSeaLayout.SampleSeabedNormal(layout, surface, edge)),
                        Is.GreaterThan(0.9999f), "The shore join must not carry a lighting seam.");
                    for (float distance = 0.25f;
                         distance <= CitySeacoastSeaLayout.SeabedReach;
                         distance += 0.25f)
                    {
                        float top = CitySeacoastSeaLayout.SampleSeabedTop(
                            layout, surface, edge + Vector2.up * distance);
                        Assert.That(top, Is.LessThanOrEqualTo(previous + 0.0001f),
                            "The seabed must descend continuously without a raised outer shelf.");
                        previous = top;
                    }
                    Assert.That(frame.SeaTopY - previous,
                        Is.GreaterThan(CitySeaResources.DepthFadeDistance +
                                       CitySeaResources.WaveHeight * CitySeaResources.CrestFactor),
                        "The bed must end below visibility even in a wave trough.");
                }
            }
            Assert.That(shores, Is.GreaterThan(0));
        }

        [Test]
        public void SurfBed_LoopsSeamlesslyAndSwells()
        {
            float[] first = CitySurfAmbienceSynthesis.GenerateSamples();
            float[] second = CitySurfAmbienceSynthesis.GenerateSamples();
            CollectionAssert.AreEqual(first, second);

            // Bounded, and the loop seam is not a click.
            float peak = 0f;
            foreach (float sample in first)
            {
                peak = Mathf.Max(peak, Mathf.Abs(sample));
            }

            Assert.That(peak, Is.LessThanOrEqualTo(0.72f));
            Assert.That(peak, Is.GreaterThan(0.1f));

            // The seam is not a click: wrapping from the last sample
            // to the first steps no further than the signal steps
            // anywhere inside the loop. (The breaker hiss makes the
            // bed legitimately jumpy sample to sample; a click is a
            // jump LARGER than that.)
            float largestInteriorStep = 0f;
            for (int index = 1; index < first.Length; index++)
            {
                largestInteriorStep = Mathf.Max(
                    largestInteriorStep,
                    Mathf.Abs(first[index] - first[index - 1]));
            }

            Assert.That(
                Mathf.Abs(first[0] - first[first.Length - 1]),
                Is.LessThanOrEqualTo(largestInteriorStep + 0.001f),
                "The surf loop clicks at its seam.");

            // The swell is audible: the loudest half-second of the bed
            // stands clearly over the quietest.
            int window = CitySurfAmbienceSynthesis.SampleRate / 2;
            float loudest = 0f;
            float quietest = float.MaxValue;
            for (int start = 0;
                 start + window <= first.Length;
                 start += window)
            {
                float sum = 0f;
                for (int index = 0; index < window; index++)
                {
                    float sample = first[start + index];
                    sum += sample * sample;
                }

                float rms = Mathf.Sqrt(sum / window);
                loudest = Mathf.Max(loudest, rms);
                quietest = Mathf.Min(quietest, rms);
            }

            Assert.That(
                loudest / Mathf.Max(0.0001f, quietest),
                Is.GreaterThan(1.25f),
                "The sea breathes; a flat bed is rain, not surf.");
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

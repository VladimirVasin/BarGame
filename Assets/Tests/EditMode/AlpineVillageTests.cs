using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;

namespace BarPromenade.Tests.EditMode
{
    public sealed class AlpineVillageTests
    {
        private static AlpineVillagePlan CreatePlan()
        {
            return AlpineVillagePlanner.Create(
                GameSessionState.DefaultCitySeed);
        }

        /// <summary>
        /// The design, as one assertion: gentle enough to walk without a
        /// single step, and a real climb rather than a flat yard.
        /// </summary>
        [Test]
        [Category("AlpineVillage")]
        public void Lane_ClimbsGentlyAndNeverSteps()
        {
            AlpineVillagePlan plan = CreatePlan();
            Assert.That(
                plan.Lane.ElevationGain,
                Is.GreaterThan(AlpineVillageValidator.MinimumElevationGain));
            Assert.That(
                plan.Lane.AverageGrade,
                Is.LessThan(AlpineVillageValidator.MaximumAverageGrade));

            // The hero's CharacterController resolves a `0.28 m` step. Nothing
            // on the lane may come near it, or the "no stairs anywhere" rule
            // is only true on average.
            float worstStep = 0f;
            float worstGrade = 0f;
            IReadOnlyList<AlpineVillageLaneSample> samples =
                plan.Lane.Samples;
            for (int index = 0; index < samples.Count - 1; index++)
            {
                float rise = Mathf.Abs(
                    samples[index + 1].Position.y -
                    samples[index].Position.y);
                float run = samples[index + 1].Distance -
                            samples[index].Distance;
                worstStep = Mathf.Max(worstStep, rise);
                worstGrade = Mathf.Max(worstGrade, rise / Mathf.Max(0.01f, run));
            }

            Assert.That(
                worstStep,
                Is.LessThan(AlpineVillageValidator.MaximumLaneStep));
            Assert.That(
                worstGrade,
                Is.LessThan(AlpineVillageValidator.MaximumLocalGrade));
        }

        /// <summary>
        /// The composition. If anything ever stands higher than the house at
        /// the head of the lane, the village stops pointing at it.
        /// </summary>
        [Test]
        [Category("AlpineVillage")]
        public void MothersHouse_IsTheHighestThingInTheVillage()
        {
            AlpineVillagePlan plan = CreatePlan();
            AlpineVillagePlotDescriptor house = plan.MothersHouse;
            Assert.That(
                house.Kind,
                Is.EqualTo(AlpineVillagePlotKind.MothersHouse));
            Assert.That(
                house.LaneDistance,
                Is.EqualTo(plan.Lane.Length).Within(0.01f));

            for (int index = 0; index < plan.Plots.Count; index++)
            {
                AlpineVillagePlotDescriptor other = plan.Plots[index];
                if (ReferenceEquals(other, house))
                {
                    continue;
                }

                Assert.That(
                    other.GroundCenter.y,
                    Is.LessThanOrEqualTo(house.GroundCenter.y),
                    $"'{other.StableId}' stands above the house at the top.");
            }

            // And it is uphill of the station, not merely far from it.
            Assert.That(
                house.GroundCenter.y - plan.Station.PadArea.Center.y,
                Is.GreaterThan(AlpineVillageValidator.MinimumElevationGain));
        }

        /// <summary>
        /// Everything the plan places has to be reachable on foot. The spurs
        /// matter most: the chapel, the adit and the graves stand more than
        /// twenty metres off the lane and would otherwise be scenery.
        /// </summary>
        [Test]
        [Category("AlpineVillage")]
        public void WalkableArea_ReachesTheStationTheLaneAndEverySpur()
        {
            AlpineVillagePlan plan = CreatePlan();
            var area = new AlpineVillageWalkableArea(plan);
            float radius = CityGroundTraversalPlanner.MaximumAgentRadius;

            Assert.That(
                area.Contains(plan.SpawnPosition, radius),
                Is.True,
                "The spawn is outside the village's own mask.");
            Assert.That(
                area.Contains(plan.Station.BoardingDockPosition, radius),
                Is.True,
                "The boarding dock is not standable.");

            for (float distance = 0f;
                 distance <= plan.Lane.Length;
                 distance += 1f)
            {
                Vector3 point = plan.Lane.Sample(distance).Position;
                Assert.That(
                    area.Contains(point, radius),
                    Is.True,
                    $"The lane is not walkable at {distance:0.0} m.");
            }

            for (int index = 0; index < plan.Plots.Count; index++)
            {
                AlpineVillagePlotDescriptor plot = plan.Plots[index];
                Assert.That(
                    area.Contains(plot.DoorDockPosition, radius),
                    Is.True,
                    $"'{plot.StableId}' cannot be stood in front of.");
            }
        }

        /// <summary>
        /// Boarding must be a step. A dock further than the motor's vertical
        /// tolerance from the hero's root is refused SILENTLY - the prompt
        /// shows and the key does nothing, forever - so this number is the
        /// difference between a working cabin and a mystery.
        /// </summary>
        [Test]
        [Category("AlpineVillage")]
        public void Station_TurnsTheCabinFloorIntoAStepRatherThanAClimb()
        {
            AlpineVillagePlan plan = CreatePlan();
            AlpineVillageStationPlan station = plan.Station;

            Assert.That(
                station.BoardingStepHeight,
                Is.InRange(
                    AlpineVillageValidator.MinimumBoardingStep,
                    AlpineVillageValidator.MaximumBoardingStep));

            // Without the raised platform it would be the full hang, which is
            // the thing the platform exists to remove. Proving the gap is what
            // makes the platform's height meaningful rather than decorative.
            float fromBarePad =
                station.Cableway.LowerCableCenter.y -
                station.Cableway.CabinAttachmentToBottom -
                station.PadTopY;
            Assert.That(
                fromBarePad,
                Is.GreaterThan(AlpineVillageValidator.MaximumBoardingStep),
                "The platform is not earning its place.");

            // This end of the line is the TOP: the rope runs away downhill and
            // disappears, which is the mirror of the mountain terminal.
            Assert.That(
                station.Cableway.UpperCableCenter.y,
                Is.LessThan(station.Cableway.LowerCableCenter.y));
        }

        /// <summary>
        /// The ground the mesh is built from and the ground the teleport lands
        /// on are the same function, so a plot's shelf has to actually be flat
        /// under its own door.
        /// </summary>
        [Test]
        [Category("AlpineVillage")]
        public void Terrain_KeepsEveryThresholdLevel()
        {
            AlpineVillagePlan plan = CreatePlan();
            for (int index = 0; index < plan.Plots.Count; index++)
            {
                AlpineVillagePlotDescriptor plot = plan.Plots[index];
                var doorXZ = new Vector2(
                    plot.DoorGroundPosition.x,
                    plot.DoorGroundPosition.z);
                var dockXZ = new Vector2(
                    plot.DoorDockPosition.x,
                    plot.DoorDockPosition.z);
                float atDoor = AlpineVillageTerrainSampler.SampleHeight(
                    plan,
                    doorXZ);
                float atDock = AlpineVillageTerrainSampler.SampleHeight(
                    plan,
                    dockXZ);
                Assert.That(
                    Mathf.Abs(atDoor - atDock),
                    Is.LessThan(PlayerMotor.InteractionVerticalTolerance * 6f),
                    $"'{plot.StableId}' has a sloping threshold.");
            }
        }

        /// <summary>
        /// The bowl. Past the walkable extent the ground climbs and keeps
        /// climbing - that is what makes the cabin the only way in, rather
        /// than an invisible wall doing it.
        /// </summary>
        [Test]
        [Category("AlpineVillage")]
        public void Terrain_ClosesTheVillageWithARidgeOnEverySide()
        {
            AlpineVillagePlan plan = CreatePlan();
            Rect bounds = plan.TerrainBounds;
            Vector2 center = bounds.center;
            Vector2[] outward =
            {
                new Vector2(bounds.xMax + 24f, center.y),
                new Vector2(bounds.xMin - 24f, center.y),
                new Vector2(center.x, bounds.yMax + 24f),
                new Vector2(center.x, bounds.yMin - 24f)
            };

            float inside = AlpineVillageTerrainSampler.SampleHeight(
                plan,
                new Vector2(plan.Lane.Start.x, plan.Lane.Start.z));
            MountainRoadCablewayPlan cableway = plan.Station.Cableway;
            Vector2 origin = new Vector2(
                cableway.StationArea.Center.x,
                cableway.StationArea.Center.z);
            Vector2 lineForward = new Vector2(
                cableway.LineForward.x,
                cableway.LineForward.z).normalized;
            Vector2 lineRight = new Vector2(
                cableway.LineRight.x,
                cableway.LineRight.z).normalized;
            for (int index = 0; index < outward.Length; index++)
            {
                // The one honest opening: the cableway's valley leaves the
                // bowl on its own side, and the rope inside it runs out of
                // the draw range rather than into a wall. A sample standing
                // in the valley is moved to the valley's shoulder, where the
                // ridge has to be as real as on the other three sides.
                Vector2 sample = outward[index];
                Vector2 delta = sample - origin;
                float across = Vector2.Dot(delta, lineRight);
                if (Vector2.Dot(delta, lineForward) > 0f &&
                    Mathf.Abs(across) <
                    AlpineVillageTerrainSampler.CablewayCutOuterHalfWidth + 4f)
                {
                    sample += lineRight * (Mathf.Sign(across == 0f ? 1f : across) *
                        (AlpineVillageTerrainSampler.CablewayCutOuterHalfWidth +
                         10f - Mathf.Abs(across)));
                }

                float height = AlpineVillageTerrainSampler.SampleHeight(
                    plan,
                    sample);
                Assert.That(
                    height - inside,
                    Is.GreaterThan(8f),
                    $"The village is open at {sample}.");
            }

            // And the wall is real geometry, not the mask doing the work: the
            // ridge has to out-climb the hero's own slope limit, or a hole in
            // the mask would let him walk out of the village.
            float slopeLimitGradient = Mathf.Tan(
                PlayerFactory.SlopeLimitDegrees * Mathf.Deg2Rad);
            Assert.That(
                AlpineVillageTerrainSampler.RidgeRisePerMeter,
                Is.GreaterThan(slopeLimitGradient));
        }

        /// <summary>
        /// Regression for the enclosure that used to exist only in the pure
        /// sampler. The mesh stopped at TerrainBounds, exactly before the
        /// first non-zero ridge sample, while the descending cable supports
        /// were grounded on the untouched macro slope above their rope.
        /// </summary>
        /// <summary>
        /// The descent, measured: wherever the player can still see the
        /// cabin, the cabin is in the air - and by the time he cannot, the
        /// mountain has closed over the rope.
        ///
        /// The village had no such rule and the mountain road's equivalent
        /// (`CablewayCabinBody_ClearsSampledTerrainOnBothTracks`) only ever
        /// looked at the road. So the village line, authored as a mirror of
        /// the mountain's climb and never checked against the village's own
        /// ground, dived into the hillside a metre off the platform and sat
        /// up to `16 m` inside it - and every test stayed green, because the
        /// only thing anybody could see was a return leg that cut to black
        /// after one metre.
        ///
        /// Both halves matter. Clearance alone would pass a line that ends in
        /// open air; closure alone would pass a line buried the whole way.
        /// </summary>
        [Test]
        [Category("AlpineVillage")]
        public void CablewayDescent_FliesWhileVisibleAndEndsInsideTheMountain()
        {
            AlpineVillagePlan plan = CreatePlan();
            MountainRoadCablewayPlan cableway = plan.Station.Cableway;
            float lastVisible = cableway.LastVisibleDistance;
            Assert.That(
                lastVisible,
                Is.GreaterThan(60f),
                "The descent is over before it has been a ride.");
            Assert.That(
                cableway.HiddenRunMeters,
                Is.GreaterThanOrEqualTo(
                    RuntimeSceneSetup.AlpineVillageFarClipPlane +
                    MountainRoadCablewayPlan.HiddenRunMargin),
                "The far turn stands inside the village's draw range.");

            float worst = float.MaxValue;
            float worstAt = 0f;
            float worstAway = float.MaxValue;
            float worstAwayAt = 0f;
            for (float distance = 0f;
                 distance <= lastVisible;
                 distance += 0.25f)
            {
                for (int side = -1; side <= 1; side += 2)
                {
                    Vector3 attachment =
                        MountainCablewayMotion.SampleTrackPosition(
                            cableway,
                            distance,
                            side);
                    float ground = AlpineVillageTerrainSampler.SampleHeight(
                        plan,
                        new Vector2(attachment.x, attachment.z));
                    float clearance = attachment.y -
                                      cableway.CabinAttachmentToBottom -
                                      ground;
                    if (clearance < worst)
                    {
                        worst = clearance;
                        worstAt = distance;
                    }

                    // Over the pad the cabin all but rests on the boarding
                    // strip - that is the whole point of the step - and the
                    // cut needs its ramp, so the flying rule is measured once
                    // the apron and its ramp are both behind him.
                    if (distance >= 10f && clearance < worstAway)
                    {
                        worstAway = clearance;
                        worstAwayAt = distance;
                    }
                }
            }

            Assert.That(
                worst,
                Is.GreaterThan(0.15f),
                $"The cabin is in the village ground at d={worstAt}.");

            // The supports are given `4.8 m` of ground under a rope the cabin
            // hangs `3.13 m` below, so the flat answer would be `1.67`. The
            // rope sags up to `0.61 m` inside a span while the cut follows the
            // chord, and that difference is what this number is.
            Assert.That(
                worstAway,
                Is.GreaterThan(0.9f),
                "Off the apron the cabin must genuinely fly; worst " +
                $"{worstAway} m at d={worstAwayAt}.");

            // And the line never ends in anything: past the cut the cabin
            // goes on flying over its own bed all the way to the turn, which
            // stands beyond the draw range. The hill closing over the rope
            // was the old cut's whole idea; a mountainside rising back over
            // a rope the passenger has watched run into the haze would be a
            // wall at the end of a road that claimed to have none.
            for (float distance = lastVisible;
                 distance <= cableway.LineLength;
                 distance += 1f)
            {
                for (int side = -1; side <= 1; side += 2)
                {
                    Vector3 attachment =
                        MountainCablewayMotion.SampleTrackPosition(
                            cableway,
                            distance,
                            side);
                    float ground = AlpineVillageTerrainSampler.SampleHeight(
                        plan,
                        new Vector2(attachment.x, attachment.z));
                    // Half a metre: the long spans past the cut sag the
                    // full `0.82 m` against a bed cut on the chord, and
                    // nobody sees this stretch - it only has to be air.
                    Assert.That(
                        attachment.y - cableway.CabinAttachmentToBottom -
                        ground,
                        Is.GreaterThan(0.5f),
                        "The mountain closes over the rope past the cut at " +
                        $"d={distance}.");
                }
            }
        }

        [Test]
        [Category("AlpineVillage")]
        public void TerrainMesh_BuildsTheRidgeAndTheCablewayBrink()
        {
            AlpineVillagePlan plan = CreatePlan();
            float fullRiseDistance =
                AlpineVillageTerrainSampler.RidgeStandoff +
                AlpineVillageTerrainSampler.RidgeMaximumRise /
                AlpineVillageTerrainSampler.RidgeRisePerMeter +
                AlpineVillageTerrainSampler.TerrainCell;
            Rect inner = plan.TerrainBounds;
            Vector2 center = inner.center;
            Vector2[] ridgeSamples =
            {
                new Vector2(inner.xMax + fullRiseDistance, center.y),
                new Vector2(inner.xMin - fullRiseDistance, center.y),
                new Vector2(center.x, inner.yMax + fullRiseDistance),
                new Vector2(center.x, inner.yMin - fullRiseDistance)
            };

            for (int index = 0; index < ridgeSamples.Length; index++)
            {
                Assert.That(
                    plan.TerrainMeshBounds.Contains(ridgeSamples[index]),
                    Is.True,
                    $"The physical mesh ends before ridge {index} crests.");
                Assert.That(
                    AlpineVillageTerrainSampler.SampleRidgeRise(
                        plan,
                        ridgeSamples[index]),
                    Is.EqualTo(AlpineVillageTerrainSampler.RidgeMaximumRise)
                        .Within(0.001f));
            }

            MountainRoadCablewayPlan cableway = plan.Station.Cableway;
            Vector2 farTurn = new Vector2(
                cableway.UpperCableCenter.x,
                cableway.UpperCableCenter.z);
            Assert.That(
                plan.TerrainMeshBounds.Contains(farTurn),
                Is.True,
                "The hidden cable turn still lies beyond the ground mesh.");

            var host = new GameObject("Alpine Terrain Mesh Test");
            try
            {
                AlpineVillageWorldResult world =
                    AlpineVillageWorldBuilder.Build(host.transform, plan);
                MeshFilter filter =
                    world.TerrainRoot.GetComponent<MeshFilter>();
                MeshCollider collider =
                    world.TerrainRoot.GetComponent<MeshCollider>();
                Assert.That(filter, Is.Not.Null);
                Assert.That(collider, Is.Not.Null);

                Bounds built = filter.sharedMesh.bounds;
                Assert.That(
                    built.min.x,
                    Is.EqualTo(plan.TerrainMeshBounds.xMin).Within(0.01f));
                Assert.That(
                    built.max.x,
                    Is.EqualTo(plan.TerrainMeshBounds.xMax).Within(0.01f));
                Assert.That(
                    built.min.z,
                    Is.EqualTo(plan.TerrainMeshBounds.yMin).Within(0.01f));
                Assert.That(
                    built.max.z,
                    Is.EqualTo(plan.TerrainMeshBounds.yMax).Within(0.01f));

                for (int index = 0; index < cableway.Nodes.Count; index++)
                {
                    MountainCablewayNodeDescriptor node =
                        cableway.Nodes[index];
                    if (node.Kind != MountainCablewayNodeKind.Support)
                    {
                        continue;
                    }

                    var point = new Vector2(
                        node.GroundPosition.x,
                        node.GroundPosition.z);
                    float sampled = AlpineVillageTerrainSampler.SampleHeight(
                        plan,
                        point);
                    Assert.That(
                        sampled,
                        Is.EqualTo(node.GroundPosition.y).Within(0.001f),
                        $"{node.StableId} does not own sampler-ground.");
                    Assert.That(
                        node.CableCenter.y - sampled,
                        Is.EqualTo(
                                AlpineVillageTerrainSampler
                                    .CablewaySupportClearance)
                            .Within(0.001f));

                    var ray = new Ray(
                        node.GroundPosition + Vector3.up * 80f,
                        Vector3.down);
                    Assert.That(
                        collider.Raycast(ray, out RaycastHit hit, 160f),
                        Is.True,
                        $"No physical ground under {node.StableId}.");
                    Assert.That(
                        hit.point.y,
                        Is.EqualTo(node.GroundPosition.y).Within(0.03f),
                        $"The built mesh floats under {node.StableId}.");
                }

                var farRay = new Ray(
                    cableway.UpperCableCenter + Vector3.up * 100f,
                    Vector3.down);
                Assert.That(
                    collider.Raycast(farRay, out RaycastHit farHit, 200f),
                    Is.True,
                    "There is no physical ground under the far turn.");
                Assert.That(
                    farHit.point.y,
                    Is.LessThan(
                        cableway.UpperCableCenter.y -
                        cableway.CabinAttachmentToBottom),
                    "The built mesh rises into the cabin at the far turn.");
            }
            finally
            {
                Object.DestroyImmediate(host);
            }
        }

        [Test]
        [Category("AlpineVillage")]
        public void SeededHouseRhythm_NeverOverlapsRotatedFootprints()
        {
            for (int seed = -128; seed <= 128; seed++)
            {
                AssertSeededHouseRhythm(seed);
            }

            // These came from the independent 200,001-seed mirror sweep and
            // exercise the old greedy-depth cascade, its furthest local trim,
            // and the former house/adit collision.
            foreach (int seed in new[]
                     {
                         -96746, -87107, -58640, -29563,
                         57657, 89380
                     })
            {
                AssertSeededHouseRhythm(seed);
            }
        }

        private static void AssertSeededHouseRhythm(int seed)
        {
            AlpineVillagePlan plan = AlpineVillagePlanner.Create(seed);
            for (int first = 0; first < plan.Plots.Count; first++)
            {
                AlpineVillagePlotDescriptor plot = plan.Plots[first];
                if (plot.Kind != AlpineVillagePlotKind.MothersHouse)
                {
                    Assert.That(
                        AlpineVillageValidator.MeasureLaneClearance(
                            plan.Lane,
                            plot),
                        Is.GreaterThanOrEqualTo(
                            AlpineVillageValidator.LaneKeepClear - 0.001f),
                        $"seed {seed}: {plot.StableId} enters lane");
                }

                for (int second = first + 1;
                     second < plan.Plots.Count;
                     second++)
                {
                    Assert.That(
                        AlpineVillageValidator.FootprintsOverlap(
                            plan.Plots[first],
                            plan.Plots[second]),
                        Is.False,
                        $"seed {seed}: {plan.Plots[first].StableId} / " +
                        plan.Plots[second].StableId);
                }
            }
        }

        [Test]
        [Category("AlpineVillage")]
        public void GarlandAnchors_StayOnTheStreetFrontage()
        {
            foreach (int seed in new[]
                     {
                         GameSessionState.DefaultCitySeed,
                         -96746,
                         -87107,
                         57657
                     })
            {
                AlpineVillagePlan plan = AlpineVillagePlanner.Create(seed);
                for (int span = 0;
                     span < AlpineVillageWorldBuilder.GarlandSpanCount;
                     span++)
                {
                    AlpineVillageWorldBuilder.GetGarlandSpan(
                        plan,
                        span,
                        out Vector3 left,
                        out Vector3 right);
                    AssertGarlandAnchorReach(plan, seed, span, left, "left");
                    AssertGarlandAnchorReach(plan, seed, span, right, "right");
                }
            }
        }

        private static void AssertGarlandAnchorReach(
            AlpineVillagePlan plan,
            int seed,
            int span,
            Vector3 anchor,
            string side)
        {
            float laneDistance = plan.Lane.FindNearest(
                new Vector2(anchor.x, anchor.z),
                out float lateralReach);
            AlpineVillageLaneSample sample = plan.Lane.Sample(laneDistance);
            float maximumReach =
                sample.Width * 0.5f +
                AlpineVillageWorldBuilder.GarlandAnchorReach +
                AlpineVillageWorldBuilder.GarlandHouseAnchorSlack;
            Assert.That(
                lateralReach,
                Is.LessThanOrEqualTo(maximumReach + 0.001f),
                $"seed {seed}: garland {span} {side} reaches into a rear yard");
        }

        /// <summary>
        /// The user-requested storm is a property of the place, not a lucky
        /// schedule roll: even Clear must begin in the heavy band, while the
        /// shared slot and the short climb can still make it worse.
        /// </summary>
        [Test]
        [Category("AlpineVillage")]
        [Category("AlpineVillageStorm")]
        public void Weather_AlwaysCarriesVeryHeavySnow()
        {
            for (int slot = 0; slot <= 10; slot++)
            {
                float rain = slot / 10f;
                for (int step = 0; step <= 4; step++)
                {
                    float climb = step / 4f;
                    float snow =
                        AlpineVillageWeatherRules.EvaluateSnowIntensity(
                            rain,
                            climb);
                    Assert.That(
                        snow,
                        Is.InRange(
                            AlpineVillageWeatherRules.SnowFloor,
                            AlpineVillageWeatherRules.SnowCeiling),
                        $"rain {rain:0.0} at climb {climb:0.00}");
                }
            }

            // Higher is heavier, and a slot already at the ceiling stays there
            // rather than being pushed past it.
            Assert.That(
                AlpineVillageWeatherRules.EvaluateSnowIntensity(0.4f, 1f),
                Is.GreaterThan(
                    AlpineVillageWeatherRules.EvaluateSnowIntensity(0.4f, 0f)));
            Assert.That(
                AlpineVillageWeatherRules.EvaluateSnowIntensity(1f, 1f),
                Is.EqualTo(AlpineVillageWeatherRules.SnowCeiling)
                    .Within(0.0001f));

            Assert.That(
                AlpineVillageWeatherRules.EvaluateSnowIntensity(0f, 0f),
                Is.EqualTo(AlpineVillageWeatherRules.SnowFloor)
                    .Within(0.0001f));
            Assert.That(
                AlpineVillageWeatherRules.SnowFloor,
                Is.GreaterThanOrEqualTo(0.85f),
                "A Clear city slot has fallen below heavy village snow.");

            CityPrecipitationProfile roadSnow =
                CityPrecipitationProfile.For(
                    CityPrecipitationKind.Snow);
            CityPrecipitationProfile blizzard =
                CityPrecipitationProfile.For(
                    CityPrecipitationKind.Blizzard);
            Assert.That(
                AlpineVillageWeatherRules.PrecipitationKind,
                Is.EqualTo(CityPrecipitationKind.Blizzard));
            Assert.That(blizzard.Stretched, Is.True);
            Assert.That(
                blizzard.MaximumParticles,
                Is.GreaterThan(roadSnow.MaximumParticles * 2));
            Assert.That(
                blizzard.MaximumEmissionRate,
                Is.GreaterThan(roadSnow.MaximumEmissionRate * 3f));
            Assert.That(
                blizzard.MaximumEmissionRate * blizzard.LifetimeSeconds,
                Is.LessThan(blizzard.MaximumParticles),
                "The blizzard cap clips the authored full-strength density.");
            Assert.That(
                blizzard.DriftScaleRange.y,
                Is.GreaterThan(roadSnow.DriftScaleRange.y));
        }

        /// <summary>
        /// The enclosing ridge closes the view, not the air. Every base sample
        /// becomes a gale, keeps its bearing, and drives a second terrain-low
        /// layer fast enough to read as wind in a still frame.
        /// </summary>
        [Test]
        [Category("AlpineVillage")]
        [Category("AlpineVillageStorm")]
        public void Weather_AlwaysCarriesAVeryStrongCoherentWind()
        {
            var gale = new WindSample(120f, 1f);
            float village = AlpineVillageWeatherRules.EvaluateStrength(
                gale,
                1f);
            Assert.That(
                village,
                Is.LessThanOrEqualTo(
                    AlpineVillageWeatherRules.WindCeiling + 0.0001f));
            Assert.That(
                AlpineVillageWeatherRules.EvaluateStrength(
                    new WindSample(120f, 0f),
                    0f),
                Is.GreaterThanOrEqualTo(
                    AlpineVillageWeatherRules.WindFloor - 0.0001f));

            for (int slot = 0; slot <= 10; slot++)
            {
                var wind = new WindSample(120f, slot / 10f);
                for (int step = 0; step <= 4; step++)
                {
                    float strength =
                        AlpineVillageWeatherRules.EvaluateStrength(
                            wind,
                            step / 4f);
                    Assert.That(
                        strength,
                        Is.InRange(
                            AlpineVillageWeatherRules.WindFloor,
                            AlpineVillageWeatherRules.WindCeiling),
                        $"wind {slot / 10f:0.0} at climb {step / 4f:0.00}");
                }
            }

            // The bearing is shared with the city so cloth, snow and crowns
            // all agree; only the strength is a village decision.
            WindSample shaped =
                AlpineVillageWeatherRules.EvaluateWind(gale, 0.5f);
            Assert.That(
                shaped.DirectionDegrees,
                Is.EqualTo(gale.DirectionDegrees).Within(0.0001f));
            Assert.That(
                AlpineVillageWeatherRules.WindFloor,
                Is.GreaterThanOrEqualTo(0.8f));
            float clearGustFloor =
                AlpineVillageWeatherRules.EvaluateStrength(
                    new WindSample(120f, 0f),
                    0f);
            float clearGustCrest =
                AlpineVillageWeatherRules.EvaluateStrength(
                    new WindSample(
                        120f,
                        GameWeatherRules.ClearWindStrength),
                    0f);
            Assert.That(
                clearGustCrest - clearGustFloor,
                Is.GreaterThanOrEqualTo(0.075f),
                "The permanent gale flattened the schedule's visible gusts.");

            WindSample minimum = AlpineVillageWeatherRules.EvaluateWind(
                new WindSample(120f, 0f),
                0f);
            Vector3 transport =
                AlpineVillageStormFieldRules.EvaluateTransport(minimum);
            Assert.That(transport.magnitude, Is.GreaterThan(6f));
            Assert.That(
                Vector3.Angle(
                    transport,
                    minimum.HorizontalDirection),
                Is.LessThan(0.01f));

            float exposed =
                AlpineVillageStormFieldRules.EvaluateEmissionRate(
                    minimum.Strength01,
                    false,
                    false);
            float sheltered =
                AlpineVillageStormFieldRules.EvaluateEmissionRate(
                    minimum.Strength01,
                    true,
                    false);
            Assert.That(
                exposed,
                Is.EqualTo(
                        AlpineVillageStormFieldRules.MinimumEmissionRate)
                    .Within(0.0001f));
            float gustCrestEmission =
                AlpineVillageStormFieldRules.EvaluateEmissionRate(
                    clearGustCrest,
                    false,
                    false);
            Assert.That(
                gustCrestEmission - exposed,
                Is.GreaterThan(100f),
                "The low snow sheet does not visibly pulse with Clear gusts.");
            Assert.That(
                sheltered,
                Is.EqualTo(
                        exposed *
                        AlpineVillageStormFieldRules.ShelterEmissionFactor)
                    .Within(0.0001f));
            Assert.That(
                AlpineVillageStormFieldRules.EvaluateEmissionRate(
                    minimum.Strength01,
                    false,
                    true),
                Is.Zero,
                "Ground spindrift follows the riding hero into open air.");

            // The same shaped wind must also read on the built place. The
            // electrical cord stays fixed at both attachments and gives only
            // its free middle enough travel to show a gale.
            const float sampleTime = 1.75f;
            const float samplePhase = 0.63f;
            Vector3 leftAnchor =
                AlpineVillageGarlandWindRules.EvaluateOffset(
                    minimum,
                    0f,
                    sampleTime,
                    samplePhase);
            Vector3 rightAnchor =
                AlpineVillageGarlandWindRules.EvaluateOffset(
                    minimum,
                    1f,
                    sampleTime,
                    samplePhase);
            Vector3 minimumMidpoint =
                AlpineVillageGarlandWindRules.EvaluateOffset(
                    minimum,
                    0.5f,
                    sampleTime,
                    samplePhase);
            WindSample maximum = AlpineVillageWeatherRules.EvaluateWind(
                new WindSample(120f, 1f),
                1f);
            Vector3 maximumMidpoint =
                AlpineVillageGarlandWindRules.EvaluateOffset(
                    maximum,
                    0.5f,
                    sampleTime,
                    samplePhase);
            Assert.That(leftAnchor, Is.EqualTo(Vector3.zero));
            Assert.That(rightAnchor, Is.EqualTo(Vector3.zero));
            Assert.That(
                Vector3.Dot(
                    minimumMidpoint,
                    minimum.HorizontalDirection),
                Is.GreaterThan(0.08f));
            Assert.That(
                maximumMidpoint.magnitude,
                Is.GreaterThan(minimumMidpoint.magnitude));
            Assert.That(
                maximumMidpoint.magnitude,
                Is.LessThanOrEqualTo(
                    AlpineVillageGarlandWindRules.MaximumDisplacement +
                    0.0001f));
            Assert.That(
                AlpineVillageGarlandWindRules.EvaluateOffset(
                    minimum,
                    0.5f,
                    sampleTime,
                    samplePhase),
                Is.EqualTo(minimumMidpoint));
        }

        [Test]
        [Category("AlpineVillage")]
        public void Planner_IsDeterministicForOneSeed()
        {
            AlpineVillagePlan first = CreatePlan();
            AlpineVillagePlan second = CreatePlan();

            Assert.That(
                second.Plots,
                Has.Count.EqualTo(first.Plots.Count));
            for (int index = 0; index < first.Plots.Count; index++)
            {
                Assert.That(
                    second.Plots[index].StableId,
                    Is.EqualTo(first.Plots[index].StableId));

                // Compared as a DISTANCE, never Is.EqualTo(Vector3): NUnit
                // compares bitwise and prints "Expected (0,0,0) But was
                // (0,0,0)" when it disagrees.
                Assert.That(
                    Vector3.Distance(
                        second.Plots[index].GroundCenter,
                        first.Plots[index].GroundCenter),
                    Is.LessThan(0.0001f));
            }

            Assert.That(
                Mathf.Abs(second.Lane.Length - first.Lane.Length),
                Is.LessThan(0.0001f));
        }

        [Test]
        [Category("AlpineVillage")]
        public void MapOverlay_ChartsTheStationAndTheWholeLane()
        {
            AlpineVillagePlan plan = CreatePlan();
            CityMapMountainRoadOverlay overlay =
                CityMapAlpineVillageOverlayBuilder.Create(plan);

            Assert.That(overlay.IsEmpty, Is.False);

            // Point zero is the station, because that is the tab's travel
            // target and the place the cabin puts the player down.
            Assert.That(
                Vector3.Distance(
                    overlay.TunnelPosition,
                    plan.Station.PadArea.Center),
                Is.LessThan(0.01f));
            Assert.That(
                Vector3.Distance(overlay.EndpointPosition, plan.Lane.End),
                Is.LessThan(0.01f));
            Assert.That(
                overlay.DisplayWorldXZBounds.Contains(
                    new Vector2(
                        plan.MothersHouse.GroundCenter.x,
                        plan.MothersHouse.GroundCenter.z)),
                Is.True);
        }

        [Test]
        [Category("AlpineVillage")]
        public void TeleportGround_LandsOnTheGroundTheMeshWasBuiltFrom()
        {
            AlpineVillagePlan plan = CreatePlan();
            var ground = new CityMapAlpineVillageTeleportGround(plan);

            Assert.That(ground.Area, Is.EqualTo(GameAreaId.AlpineVillage));
            AlpineVillageLaneSample middle = plan.Lane.Sample(
                plan.Lane.Length * 0.5f);
            Assert.That(
                ground.TryResolveStandingPosition(
                    new Vector2(middle.Position.x, middle.Position.z),
                    out Vector3 standing),
                Is.True);
            Assert.That(
                standing.y -
                AlpineVillageTerrainSampler.SampleHeight(
                    plan,
                    new Vector2(standing.x, standing.z)),
                Is.EqualTo(PlayerFactory.GroundedRootOffset).Within(0.0001f));

            // Far outside the bowl there is nothing to stand on, and the
            // chart must say so rather than dropping the hero into rock.
            Assert.That(
                ground.TryResolveStandingPosition(
                    new Vector2(
                        plan.TerrainBounds.xMax + 400f,
                        plan.TerrainBounds.yMax + 400f),
                    out _),
                Is.False);
        }

        /// <summary>
        /// The station stands on GROUND, not in the air.
        ///
        /// It did not. `CreateStation` sets the pad `7 m` downhill of the lane
        /// foot and then forces its height to the foot's, and nothing
        /// flattened anything underneath: the slab hung `0.19 m` to `1.32 m`
        /// clear of the snow and every edge was a lip of `0.34 m` to `1.50 m`
        /// against a `0.28 m` step offset. The drop was ONE-WAY - a hero who
        /// got off could never get back on - which is what "there are no steps
        /// and I cannot leave the station" was.
        /// </summary>
        [Test]
        [Category("AlpineVillage")]
        public void Station_StandsOnItsOwnFlattenedShelf()
        {
            AlpineVillagePlan plan = AlpineVillagePlanner.Create(
                GameSessionState.DefaultCitySeed);
            MountainRoadTerminalRect pad = plan.Station.PadArea;
            float padBase = pad.Center.y;

            // Under the slab, and a stride outside each edge, the ground is
            // the pad's own base.
            for (int corner = 0; corner < 4; corner++)
            {
                Vector3 point = pad.GetCorner(corner);
                float under = AlpineVillageTerrainSampler.SampleHeight(
                    plan,
                    new Vector2(point.x, point.z));
                Assert.That(
                    under,
                    Is.EqualTo(padBase).Within(0.05f),
                    $"Corner {corner} of the pad stands " +
                    $"{padBase - under:0.00} m clear of the ground.");
            }

            // And the step OFF the pad is one a person takes. Sampled a stride
            // out from every edge, all the way round.
            for (int side = 0; side < 4; side++)
            {
                Vector3 outward = side switch
                {
                    0 => pad.Right,
                    1 => -pad.Right,
                    2 => pad.Forward,
                    _ => -pad.Forward
                };
                float reach = side < 2
                    ? pad.Size.x * 0.5f
                    : pad.Size.y * 0.5f;
                for (float slide = -0.4f; slide <= 0.41f; slide += 0.4f)
                {
                    Vector3 across = side < 2 ? pad.Forward : pad.Right;
                    float halfAcross = side < 2
                        ? pad.Size.y * 0.5f
                        : pad.Size.x * 0.5f;
                    Vector3 point = pad.Center +
                                    outward * (reach + 0.35f) +
                                    across * (slide * halfAcross);
                    float outside = AlpineVillageTerrainSampler.SampleHeight(
                        plan,
                        new Vector2(point.x, point.z));
                    float lip = padBase +
                                AlpineVillagePlanner.StationPadTopOffset -
                                outside;
                    Assert.That(
                        lip,
                        Is.LessThan(PlayerFactory.StepOffset),
                        $"A {lip:0.00} m lip off side {side}; the hero " +
                        "cannot step down it and can never climb back.");
                }
            }
        }

        /// <summary>
        /// The mask is square to the CONCRETE.
        ///
        /// `MountainCablewayWorldBuilder` poses the station with
        /// `LookRotation(plan.LineForward)` and lays every solid box on the
        /// line axes, while this rectangle used to be built on `right`/
        /// `uphill` - `19.9°` apart at the village. The mask refused `3.71 m²`
        /// of real pad at its corners and granted `7.59 m²` of thin air off
        /// its sides. That is an invisible wall on visible ground.
        /// </summary>
        [Test]
        [Category("AlpineVillage")]
        public void Station_MaskIsSquareToTheStationItself()
        {
            AlpineVillagePlan plan = AlpineVillagePlanner.Create(
                GameSessionState.DefaultCitySeed);
            MountainRoadCablewayPlan cableway = plan.Station.Cableway;

            Assert.That(
                Vector3.Dot(plan.Station.PadArea.Right, cableway.LineRight),
                Is.GreaterThan(0.9999f),
                "The pad rectangle is skewed against the station built on it.");
            Assert.That(
                Vector3.Dot(plan.Station.PadArea.Forward, cableway.LineForward),
                Is.GreaterThan(0.9999f));

            // Every corner of the real pad is walkable ground. Inset half a
            // metre ON EACH AXIS - the mask holds a capsule off its own edge,
            // so a diagonal inset of the same length would be measuring the
            // test's arithmetic rather than the mask.
            var area = new AlpineVillageWalkableArea(plan);
            MountainRoadTerminalRect pad = plan.Station.PadArea;
            for (int corner = 0; corner < 4; corner++)
            {
                Vector3 point = pad.GetCorner(corner);
                Vector3 inset = point +
                                pad.Right *
                                ((corner & 1) == 0 ? 0.5f : -0.5f) +
                                pad.Forward *
                                ((corner & 2) == 0 ? 0.5f : -0.5f);
                Assert.That(
                    area.Contains(inset, 0.32f),
                    Is.True,
                    $"The mask refuses corner {corner} of its own pad.");
            }
        }

        /// <summary>
        /// The one that would have caught all of it: a hero who arrives by
        /// cabin can WALK from the boarding dock into the village.
        ///
        /// There was no such check anywhere. `AlpineVillageValidator` asserts
        /// three things about the station and not one of them is the ground,
        /// the mask or the concrete.
        /// </summary>
        [Test]
        [Category("AlpineVillage")]
        public void Station_LetsTheHeroWalkFromTheDockIntoTheVillage()
        {
            AlpineVillagePlan plan = AlpineVillagePlanner.Create(
                GameSessionState.DefaultCitySeed);
            var area = new AlpineVillageWalkableArea(plan);
            MountainRoadCablewayPlan cableway = plan.Station.Cableway;
            Vector3 pad = plan.Station.PadArea.Center;

            // Dock -> back along the strip -> across the pad -> the lane foot.
            // Straight legs between points the PLAN names; what is measured is
            // whether the mask and the ground allow a body to do it.
            Vector3[] route =
            {
                cableway.BoardingDockPosition,
                pad +
                cableway.LineRight * cableway.BoardingDockRightOffset +
                cableway.LineForward * cableway.BoardingFenceForward,
                pad,
                plan.Lane.Start
            };

            for (int leg = 0; leg + 1 < route.Length; leg++)
            {
                Vector3 from = route[leg];
                Vector3 to = route[leg + 1];
                int steps = Mathf.CeilToInt(
                    Vector3.Distance(from, to) / 0.2f) + 1;
                for (int step = 0; step <= steps; step++)
                {
                    Vector3 point = Vector3.Lerp(
                        from,
                        to,
                        step / (float)steps);
                    Assert.That(
                        area.Contains(point, 0.32f),
                        Is.True,
                        $"Leg {leg} leaves the walkable mask at {point} - " +
                        "an invisible wall on the way out of the station.");
                }
            }
        }
    }
}

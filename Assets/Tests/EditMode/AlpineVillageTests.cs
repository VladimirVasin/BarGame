using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEditor;
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

        [Test]
        [Category("AlpineVillage")]
        public void MothersHouseDoor_PlanOwnsVisibleLeafDockAndReturnAxis()
        {
            AlpineVillagePlan plan = CreatePlan();
            AlpineVillagePlotDescriptor house = plan.MothersHouse;
            Vector3 frontCenter = house.GroundCenter +
                                  house.Facing *
                                  (house.FootprintSize.y * 0.5f);

            Assert.That(
                house.DoorAcrossOffset,
                Is.EqualTo(AlpineVillagePlanner.MothersHouseDoorAcross)
                    .Within(0.001f));
            Assert.That(
                Vector3.Dot(
                    house.DoorGroundPosition - frontCenter,
                    house.Facing),
                Is.Zero.Within(0.001f),
                "The shifted leaf must stay on the front wall.");
            Assert.That(
                Vector3.Distance(
                    house.DoorDockPosition - house.DoorGroundPosition,
                    house.Facing *
                    AlpineVillagePlanner.DoorDockStandoff),
                Is.LessThan(0.001f));
            Assert.That(
                new AlpineVillageWalkableArea(plan).Contains(
                    plan.MothersHouseReturnPosition,
                    CityGroundTraversalPlanner.MaximumAgentRadius),
                Is.True,
                "The return point must remain safely standable.");
            Assert.That(
                Vector3.Distance(
                    plan.MothersHouseReturnPosition,
                    house.DoorDockPosition),
                Is.GreaterThan(0.1f),
                "A return is not the interaction dock.");

            Vector3 fromTrigger =
                plan.MothersHouseReturnPosition -
                house.DoorGroundPosition;
            fromTrigger.y = 0f;
            Assert.That(
                fromTrigger.magnitude,
                Is.GreaterThanOrEqualTo(
                    AlpineVillagePlanner.MothersHouseEntranceTriggerRadius +
                    PlayerDoorActionPlan.DockBoundaryClearance),
                "The returned capsule must not overlap the entrance trigger.");
        }

        /// <summary>
        /// The offset that stops twelve doors reading as one stamped row is
        /// the PLAN's, not the world builder's guess from a mesh variant.
        ///
        /// It matters now in a way it never did while a door was scenery:
        /// the hero walks to a threshold, so the leaf, the trigger, the dock
        /// and the trodden path have to be the same place. The offset stays
        /// well inside the door step, so every centreline path still meets a
        /// stone rather than snow.
        /// </summary>
        [Test]
        [Category("AlpineVillage")]
        public void HouseDoors_SitOffCentreByThePlansOwnOffset()
        {
            AlpineVillagePlan plan = CreatePlan();
            var area = new AlpineVillageWalkableArea(plan);
            var offsets = new List<float>();
            foreach (AlpineVillagePlotDescriptor plot in plan.Plots)
            {
                if (plot.Kind != AlpineVillagePlotKind.House)
                {
                    continue;
                }

                float across = plot.DoorAcrossOffset;
                offsets.Add(across);
                Assert.That(
                    Mathf.Abs(across),
                    Is.LessThanOrEqualTo(
                        AlpineVillagePlanner.HouseDoorAcross + 0.001f),
                    $"'{plot.StableId}' opens outside its own front wall.");

                // The step is `DoorWidth + 0.5` wide and centred on the
                // leaf, so the offset may never carry the threshold path off
                // it.
                Assert.That(
                    Mathf.Abs(across),
                    Is.LessThan(
                        (AlpineVillageWorldBuilder.DoorWidth + 0.5f) * 0.5f),
                    $"'{plot.StableId}' steps off its own step.");

                Vector3 frontCenter = plot.GroundCenter +
                                      plot.Facing *
                                      (plot.FootprintSize.y * 0.5f);
                Assert.That(
                    Vector3.Dot(
                        plot.DoorGroundPosition - frontCenter,
                        plot.Facing),
                    Is.Zero.Within(0.001f),
                    $"'{plot.StableId}' moved its door off the front wall.");
                Assert.That(
                    Vector3.Distance(
                        plot.DoorDockPosition - plot.DoorGroundPosition,
                        plot.Facing * AlpineVillagePlanner.DoorDockStandoff),
                    Is.LessThan(0.001f),
                    $"'{plot.StableId}' does not stand in front of its door.");
                Assert.That(
                    area.Contains(
                        plot.DoorDockPosition,
                        CityGroundTraversalPlanner.MaximumAgentRadius),
                    Is.True,
                    $"'{plot.StableId}' cannot be reached on foot.");
            }

            Assert.That(offsets.Count, Is.EqualTo(AlpineVillagePlanner.HouseCount));
            Assert.That(
                offsets.Count(offset => offset < -0.05f),
                Is.GreaterThan(1),
                "The row leans one way only if the seed never varies it.");
            Assert.That(
                offsets.Count(offset => offset > 0.05f),
                Is.GreaterThan(1));
        }

        /// <summary>
        /// Everything the plan places has to be reachable on foot. The spurs
        /// matter most: the chapel and the spring's head stand more than
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
        /// The bowl LOOMS, not merely closes. A `49°` rise starting `36 m`
        /// out subtended `16-20°` from the lane and was a pale smear in the
        /// haze - the enclosure existed for the walkable mask and for nobody
        /// looking. From mid-lane sideways and from the head straight up the
        /// axis, the toe has to be near and the crest has to stand high in
        /// the frame. The lateral bars are lower because the hull is a
        /// world-axis rectangle around a village turned `19.9°`: an oriented
        /// hull is the recorded follow-up, not a looser number.
        ///
        /// Raised 2026-08-31, on the lead's "make the mountains closer":
        /// the wall stood at the old bars and read as far country. The toe
        /// came in (`RidgeStandoff` `6 -> 3`), the face went from `58°` to
        /// `74°` (`RidgeRisePerMeter` `1.6 -> 3.6`) and the crest from
        /// `50 m` to `60 m`, which took the mean silhouette from mid-lane
        /// from `26.7°` to `34.1°`. The bars move with it, or the next
        /// retune could quietly give the distance back.
        /// </summary>
        [Test]
        [Category("AlpineVillage")]
        public void Bowl_LoomsOverTheLaneOnEverySide()
        {
            AlpineVillagePlan plan = CreatePlan();
            AlpineVillageLaneSample middle =
                plan.Lane.Sample(plan.Lane.Length * 0.5f);
            AlpineVillageLaneSample head = plan.Lane.Sample(plan.Lane.Length);
            const float eyeHeight = 1.72f;
            (Vector3 origin, Vector3 direction, float toeLimit,
                float elevationBar, string label)[] rays =
                {
                    (middle.Position, middle.Right, 59f, 32f,
                        "mid-lane toward +Right"),
                    (middle.Position, -middle.Right, 59f, 32f,
                        "mid-lane toward -Right"),
                    (head.Position, plan.Uphill, 33f, 40f,
                        "lane head uphill")
                };
            foreach ((Vector3 origin, Vector3 direction, float toeLimit,
                         float elevationBar, string label) in rays)
            {
                Vector3 flat = new Vector3(
                    direction.x,
                    0f,
                    direction.z).normalized;
                float eye = AlpineVillageTerrainSampler.SampleHeight(
                                plan,
                                new Vector2(origin.x, origin.z)) +
                            eyeHeight;
                float toe = 0f;
                float elevation = 0f;
                for (float distance = 0.5f;
                     distance <= RuntimeSceneSetup.AlpineVillageFarClipPlane;
                     distance += 0.5f)
                {
                    Vector3 point = origin + flat * distance;
                    var pointXZ = new Vector2(point.x, point.z);
                    if (toe <= 0f &&
                        AlpineVillageTerrainSampler.SampleRidgeRise(
                            plan,
                            pointXZ) > 0f)
                    {
                        toe = distance;
                    }

                    float height = AlpineVillageTerrainSampler.SampleHeight(
                        plan,
                        pointXZ);
                    elevation = Mathf.Max(
                        elevation,
                        Mathf.Atan2(height - eye, distance) * Mathf.Rad2Deg);
                }

                Assert.That(
                    toe,
                    Is.GreaterThan(0f).And.LessThanOrEqualTo(toeLimit),
                    $"The wall's toe stands at {toe} m {label}.");
                Assert.That(
                    elevation,
                    Is.GreaterThanOrEqualTo(elevationBar),
                    $"The wall subtends only {elevation:0.0} deg {label}.");
            }
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
            var generatedMeshes = new List<Mesh>();
            Mesh importedMesh = null;
            try
            {
                AlpineVillageWorldResult world =
                    AlpineVillageWorldBuilder.Build(host.transform, plan);
                foreach (MeshFilter candidate in world.Root.GetComponentsInChildren<MeshFilter>(true))
                {
                    Mesh mesh = candidate.sharedMesh;
                    if (mesh == null) continue;
                    if (AssetDatabase.Contains(mesh))
                    {
                        importedMesh = mesh;
                        continue;
                    }
                    if (mesh.name != "Alpine Village Ground" &&
                        mesh.name != "Alpine Village Lane" &&
                        mesh.name != "Alpine Village Snow Drifts" &&
                        !candidate.name.StartsWith("Visible Path - ", System.StringComparison.Ordinal))
                        continue;
                    Assert.That(candidate.GetComponent<RuntimeGeneratedMeshOwner>(), Is.Not.Null,
                        candidate.name + " creates a scene-owned mesh.");
                    generatedMeshes.Add(mesh);
                }
                Assert.That(generatedMeshes.Count, Is.GreaterThanOrEqualTo(4));
                Assert.That(importedMesh, Is.Not.Null);
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

                    // The ground mesh is a 2 m grid and the cut bed bends AT
                    // each pylon (0.125 -> 0.500 m/m at support-01), so the
                    // triangle spanning the node cuts that corner: its chord
                    // sags by the downhill grade times a quarter of the cell
                    // diagonal, because the line runs diagonally to the grid.
                    // No shelf flattens it away without taking the clearance
                    // the cabin needs (the sampler, which everything physical
                    // reads, is exact to a millimetre above). So the bound is
                    // one-sided: the mesh may sag under the footing by that
                    // chord, it may never rise INTO the tower.
                    float downhillGrade = index + 1 < cableway.Nodes.Count
                        ? Mathf.Abs(
                            (cableway.Nodes[index + 1].GroundPosition.y -
                             node.GroundPosition.y) /
                            Mathf.Max(
                                0.001f,
                                cableway.Nodes[index + 1].Distance -
                                node.Distance))
                        : 0f;
                    float chordSag = downhillGrade *
                                     AlpineVillageTerrainSampler.TerrainCell *
                                     Mathf.Sqrt(2f) * 0.25f;
                    Assert.That(
                        hit.point.y,
                        Is.LessThanOrEqualTo(node.GroundPosition.y + 0.03f),
                        $"The built mesh rises through {node.StableId}.");
                    Assert.That(
                        hit.point.y,
                        Is.GreaterThanOrEqualTo(
                            node.GroundPosition.y - chordSag - 0.03f),
                        $"The built mesh floats under {node.StableId}: " +
                        $"{node.GroundPosition.y - hit.point.y:0.###} m " +
                        $"against a {chordSag:0.###} m chord sag.");
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

                AssertTerrainSubmeshes(plan, world, filter, collider);
            }
            finally
            {
                Object.DestroyImmediate(host);
            }
            foreach (Mesh mesh in generatedMeshes)
                Assert.That(mesh == null, Is.True, "Generated village meshes must die with the world.");
            Assert.That(importedMesh != null, Is.True, "Imported kit meshes must survive world teardown.");
        }

        /// <summary>
        /// The two-submesh split of the ground: floor and rise on their own
        /// materials, the cableway cut carved into the rise, one collider on
        /// the one mesh and shared toe vertices. The art pass exposes stone
        /// above the snow floor without changing this physical boundary.
        /// </summary>
        private static void AssertTerrainSubmeshes(
            AlpineVillagePlan plan,
            AlpineVillageWorldResult world,
            MeshFilter filter,
            MeshCollider collider)
        {
            Mesh mesh = filter.sharedMesh;
            Assert.That(collider.sharedMesh, Is.SameAs(mesh));
            Assert.That(mesh.subMeshCount, Is.EqualTo(2));

            MeshRenderer renderer =
                world.TerrainRoot.GetComponent<MeshRenderer>();
            Material[] materials = renderer.sharedMaterials;
            Assert.That(materials.Length, Is.EqualTo(2));
            Assert.That(
                materials[AlpineVillageWorldBuilder.TerrainFloorMaterialIndex],
                Is.SameAs(RuntimePrimitiveFactory.DefaultMaterial));
            Assert.That(
                materials[AlpineVillageWorldBuilder.TerrainRiseMaterialIndex],
                Is.SameAs(AlpineVillageRidgeAppearance.RidgeMaterial));

            // The shared axes retain coarse vertices and refine the brook.
            AlpineVillageTerrainGrid grid = AlpineVillageTerrainGrid.Get(plan);
            int columns = grid.Columns;
            int rows = grid.Rows;
            int gridVertexCount = (columns + 1) * (rows + 1);
            var riseCells = new bool[rows, columns];
            for (int row = 0; row < rows; row++)
            {
                for (int column = 0; column < columns; column++)
                {
                    riseCells[row, column] =
                        AlpineVillageWorldBuilder.IsRiseCellCentre(
                            plan,
                            CellCentre(grid, row, column));
                }
            }

            int[] floor = mesh.GetTriangles(
                AlpineVillageWorldBuilder.TerrainFloorMaterialIndex);
            int[] rise = mesh.GetTriangles(
                AlpineVillageWorldBuilder.TerrainRiseMaterialIndex);
            Assert.That(
                floor.Length + rise.Length,
                Is.EqualTo(columns * rows * 6),
                "A terrain cell is missing or drawn in both submeshes.");
            Assert.That(floor, Is.Not.Empty);
            Assert.That(rise, Is.Not.Empty);

            Vector3[] vertices = mesh.vertices;
            Assert.That(
                vertices.Length,
                Is.EqualTo(gridVertexCount),
                "The old duplicated toe ring returned.");

            var sharedToeVertices = new HashSet<int>(floor);
            sharedToeVertices.IntersectWith(rise);
            Assert.That(
                sharedToeVertices,
                Is.Not.Empty,
                "Floor and rise do not share exact indices at their toe.");

            // Every floor triangle is a floor cell, and no floor cell is on
            // the rise at all: the cableway cut is carved INTO the wall and
            // stays part of it. It used to be handed to the floor material,
            // and a `38 m` bright band up a dark wall read as a hole in the
            // mountain rather than as a valley in it.
            for (int index = 0; index < floor.Length; index += 3)
            {
                (int row, int column) = TriangleCell(
                    vertices,
                    floor,
                    index,
                    grid);
                Vector2 centre = CellCentre(grid, row, column);
                Assert.That(
                    riseCells[row, column],
                    Is.False,
                    $"Floor triangle at {centre} is a rise cell.");
                Assert.That(
                    floor[index] < gridVertexCount &&
                    floor[index + 1] < gridVertexCount &&
                    floor[index + 2] < gridVertexCount,
                    Is.True,
                    "A floor triangle leaves the one terrain grid.");
                Assert.That(
                    AlpineVillageTerrainSampler.SampleRidgeRise(plan, centre),
                    Is.Zero,
                    $"A floor cell at {centre} stands on the rise.");
            }

            // Every rise triangle is a rise cell - the cableway cut included.
            for (int index = 0; index < rise.Length; index += 3)
            {
                (int row, int column) = TriangleCell(
                    vertices,
                    rise,
                    index,
                    grid);
                Vector2 centre = CellCentre(grid, row, column);
                Assert.That(
                    rise[index] < gridVertexCount &&
                    rise[index + 1] < gridVertexCount &&
                    rise[index + 2] < gridVertexCount,
                    Is.True,
                    "A rise triangle leaves the one terrain grid.");
                Assert.That(
                    riseCells[row, column],
                    Is.True,
                    $"Rise triangle at {centre} is not a rise cell.");
                Assert.That(
                    AlpineVillageTerrainSampler.SampleRidgeRise(plan, centre),
                    Is.GreaterThan(0f));
            }

            // The UVs already encode the recipe's metre pitch. The indexed
            // material blocks must therefore stay identity; applying the
            // primitive transform here scales the texture twice.
            Vector2[] uv = mesh.uv;
            Assert.That(uv.Length, Is.EqualTo(vertices.Length));
            float expectedUvScale = 1f /
                MountainRoadSurfaceAppearance
                    .GetRecipe(AlpineVillageRidgeAppearance.Surface)
                    .MetersPerTile;
            Assert.That(
                AlpineVillageRidgeAppearance.UvUnitsPerMeter,
                Is.EqualTo(expectedUvScale).Within(0.000001f));
            for (int index = 0; index < vertices.Length; index++)
            {
                Assert.That(
                    uv[index].x,
                    Is.EqualTo(vertices[index].x * expectedUvScale)
                        .Within(0.0001f));
                Assert.That(
                    uv[index].y,
                    Is.EqualTo(vertices[index].z * expectedUvScale)
                        .Within(0.0001f));
            }

            var floorProperties = new MaterialPropertyBlock();
            var riseProperties = new MaterialPropertyBlock();
            renderer.GetPropertyBlock(
                floorProperties,
                AlpineVillageWorldBuilder.TerrainFloorMaterialIndex);
            renderer.GetPropertyBlock(
                riseProperties,
                AlpineVillageWorldBuilder.TerrainRiseMaterialIndex);
            Vector4 floorTransform = floorProperties.GetVector("_BaseMap_ST");
            Vector4 riseTransform = riseProperties.GetVector("_BaseMap_ST");
            Assert.That(
                floorTransform,
                Is.EqualTo(AlpineVillageRidgeAppearance.BakedUvTransform));
            Assert.That(
                riseTransform,
                Is.EqualTo(AlpineVillageRidgeAppearance.BakedUvTransform));
            Assert.That(
                riseProperties.GetTexture("_BaseMap"),
                Is.SameAs(MountainRoadSurfaceAppearance.GetTexture(
                    AlpineVillageRidgeAppearance.RockSurface)));
            Assert.That(
                floorProperties.GetTexture("_BaseMap"),
                Is.SameAs(MountainRoadSurfaceAppearance.GetTexture(
                    AlpineVillageRidgeAppearance.Surface)));
        }

        private static Vector2 CellCentre(
            AlpineVillageTerrainGrid grid,
            int row,
            int column)
        {
            return new Vector2(
                (grid.XCoordinates[column] + grid.XCoordinates[column + 1]) * 0.5f,
                (grid.ZCoordinates[row] + grid.ZCoordinates[row + 1]) * 0.5f);
        }

        /// <summary>
        /// The grid cell a triangle belongs to, from its centroid.
        /// </summary>
        private static (int row, int column) TriangleCell(
            Vector3[] vertices,
            int[] triangles,
            int index,
            AlpineVillageTerrainGrid grid)
        {
            Vector3 centroid = (vertices[triangles[index]] +
                                vertices[triangles[index + 1]] +
                                vertices[triangles[index + 2]]) / 3f;
            return (grid.FindRow(centroid.z), grid.FindColumn(centroid.x));
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
            // and the former house collision.
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

        /// <summary>
        /// THE VILLAGE IS A PLACE AND NOT A CORRIDOR.
        ///
        /// The mask used to be the lane centreline plus one capsule per
        /// visible path - `2.38 m` of usable half-width on the street,
        /// `0.78 m` on a household branch - inside a bowl `93 x 125 m`
        /// across. That is six per cent of the village walkable, and stepping
        /// off the path was impossible everywhere: the burial ground could be
        /// faced but never entered, and no house could be walked round.
        ///
        /// The number is deliberately coarse. What it pins is the SHAPE of
        /// the rule - ground by default, minus what stands in it - so a later
        /// change that quietly reintroduces corridors fails here rather than
        /// in a play session.
        /// </summary>
        [Test]
        [Category("AlpineVillage")]
        public void WalkableArea_OpensTheWholeBowlInsteadOfACorridor()
        {
            AlpineVillagePlan plan = CreatePlan();
            var area = new AlpineVillageWalkableArea(plan);
            float radius = CityGroundTraversalPlanner.MaximumAgentRadius;

            Rect bowl = plan.TerrainBounds;
            int walkable = 0;
            int total = 0;
            for (float x = bowl.xMin + 0.5f; x < bowl.xMax; x += 1f)
            {
                for (float z = bowl.yMin + 0.5f; z < bowl.yMax; z += 1f)
                {
                    total++;
                    if (area.Contains(new Vector3(x, 0f, z), radius))
                    {
                        walkable++;
                    }
                }
            }

            Assert.That(
                walkable / (float)total,
                Is.GreaterThan(0.8f),
                "The inhabited bowl is a corridor again.");

            // Every house can be walked round. This is the one that the old
            // capsule chain could never pass, and the reason the complaint
            // was "I cannot step off the path".
            for (int index = 0; index < plan.Plots.Count; index++)
            {
                AlpineVillagePlotDescriptor plot = plan.Plots[index];
                Vector3 behind = plot.GroundCenter -
                                 plot.Facing *
                                 (plot.FootprintSize.y * 0.5f + 1.5f);
                Assert.That(
                    area.Contains(behind, radius),
                    Is.True,
                    $"There is no ground behind '{plot.StableId}'.");
            }

            // And the spring is ground: the one plot with no shell, walked
            // up to rather than looked at from a path.
            AlpineVillagePlotDescriptor spring = null;
            for (int index = 0; index < plan.Plots.Count; index++)
            {
                if (plan.Plots[index].Kind ==
                    AlpineVillagePlotKind.Spring)
                {
                    spring = plan.Plots[index];
                    break;
                }
            }

            Assert.That(spring, Is.Not.Null);
            Assert.That(
                area.Contains(spring.GroundCenter, radius),
                Is.True,
                "The hero cannot walk up to the water.");
        }

        /// <summary>
        /// What an open bowl still has to refuse, and why each one is not an
        /// invisible wall.
        ///
        /// A building is refused on the exact rectangle its own
        /// `Physical Shell` collider stands on, so the mask agrees with the
        /// physics instead of leaving the hero to graze it - contact is read
        /// back as achieved movement and a graze reads as a crawl.
        ///
        /// The mountain is refused on the line where the ground starts to
        /// climb at `74°` against a `45°` slope limit, so past the boundary
        /// the slope is already doing the work.
        ///
        /// The cableway brink is the exception that has to be held by the
        /// mask alone: the cut falls at `7-28°`, which is walkable, and it
        /// is the only way out of the village on foot.
        /// </summary>
        [Test]
        [Category("AlpineVillage")]
        public void WalkableArea_RefusesTheBuildingsTheMountainAndTheBrink()
        {
            AlpineVillagePlan plan = CreatePlan();
            var area = new AlpineVillageWalkableArea(plan);

            for (int index = 0; index < plan.Plots.Count; index++)
            {
                AlpineVillagePlotDescriptor plot = plan.Plots[index];
                bool solid = plot.Kind != AlpineVillagePlotKind.Spring;
                Assert.That(
                    area.Contains(plot.GroundCenter),
                    Is.EqualTo(!solid),
                    $"'{plot.StableId}' disagrees with its own collider.");
            }

            MountainRoadCablewayPlan cableway = plan.Station.Cableway;
            Vector3 pad = plan.Station.PadArea.Center;

            // Down the gorge. Twelve metres out is past the entrance ramp and
            // already well below the bowl floor.
            Assert.That(
                area.Contains(pad + cableway.LineForward * 12f, 0.32f),
                Is.False,
                "The hero can walk out of the village down the cableway cut.");

            // But not one centimetre of the boarding side is lost to it: the
            // strip runs off the FRONT of the pad and its far end stands
            // within half a metre of the cut's own entrance line.
            Vector3 platformFar = pad +
                                  cableway.LineRight *
                                  cableway.BoardingDockRightOffset +
                                  cableway.LineForward *
                                  cableway.BoardingPlatformFarForward;
            Assert.That(
                area.Contains(platformFar, 0.32f),
                Is.True,
                "The brink has eaten the far end of the boarding platform.");

            // The mountain. Beyond the standoff the ground climbs steeper
            // than the hero can walk, and the mask stops on the same line.
            Rect bowl = plan.TerrainBounds;
            float outside = AlpineVillageWalkableArea.GroundOutset + 4f;
            Vector3[] beyond =
            {
                new Vector3(bowl.center.x, 0f, bowl.yMax + outside),
                new Vector3(bowl.center.x, 0f, bowl.yMin - outside),
                new Vector3(bowl.xMax + outside, 0f, bowl.center.y),
                new Vector3(bowl.xMin - outside, 0f, bowl.center.y)
            };
            for (int index = 0; index < beyond.Length; index++)
            {
                Assert.That(
                    area.Contains(beyond[index]),
                    Is.False,
                    $"The mask reaches into the enclosing ridge at " +
                    $"{beyond[index]}.");
            }
        }

        /// <summary>
        /// A run at a wall has to become a slide along it, not a stop against
        /// it: planar velocity is read back from achieved movement, so a mask
        /// that refuses the step without offering the tangent one costs the
        /// hero his whole speed on a graze.
        /// </summary>
        [Test]
        [Category("AlpineVillage")]
        public void WalkableArea_SlidesAlongAFootprintRatherThanStopping()
        {
            AlpineVillagePlan plan = CreatePlan();
            var area = new AlpineVillageWalkableArea(plan);
            AlpineVillagePlotDescriptor house = plan.Plots[1];
            const float radius = 0.32f;

            Vector3 across = Vector3.Cross(Vector3.up, house.Facing)
                .normalized;
            Vector3 start = house.GroundCenter +
                            house.Facing *
                            (house.FootprintSize.y * 0.5f + radius + 0.35f) +
                            across * (house.FootprintSize.x * 0.25f);
            Assert.That(
                area.Contains(start, radius),
                Is.True,
                "The approach point is not standable to begin with.");

            // Straight at the facade, a quarter of the way along it. He is
            // brought up against the wall - all `0.35 m` of the gap he had -
            // and not one centimetre sideways.
            Vector3 desired = start - house.Facing * 0.6f;
            Vector3 constrained = area.Constrain(start, desired, radius);
            Assert.That(
                area.Contains(constrained, radius - 0.001f),
                Is.True,
                "The slide left the mask.");
            Assert.That(
                Vector3.Dot(constrained - start, -house.Facing),
                Is.EqualTo(0.35f).Within(0.01f),
                "The wall is not where the collider stands.");
            Assert.That(
                Mathf.Abs(Vector3.Dot(constrained - start, across)),
                Is.LessThan(0.01f),
                "A head-on step should not slide sideways.");

            // And at forty-five degrees it keeps the tangent component.
            Vector3 diagonal = start + (across - house.Facing).normalized * 0.6f;
            Vector3 slid = area.Constrain(start, diagonal, radius);
            Assert.That(
                area.Contains(slid, radius - 0.001f),
                Is.True,
                "The diagonal slide left the mask.");
            Assert.That(
                Vector3.Dot(slid - start, across),
                Is.GreaterThan(0.2f),
                "The hero stopped dead against the wall instead of sliding.");
        }

        /// <summary>
        /// A CHARTED arrival has room to walk out of.
        ///
        /// The map carries each plot's `DoorDockPosition`, and a dock is an
        /// interaction pose - `1.1 m` from the threshold, facing it - not a
        /// place to put a person down. Landing on the one at the house at the
        /// top of the lane left `1.26 m` of ground ahead on the arrival's own
        /// heading: hold forward, the wall takes the whole planar velocity,
        /// and because the gait is weighted by ACHIEVED speed the walk cycle
        /// stops. It reads as the animation breaking, and it was reported as
        /// exactly that.
        /// </summary>
        [Test]
        [Category("AlpineVillage")]
        public void ChartedArrivals_StandBackFarEnoughToWalkOut()
        {
            AlpineVillagePlan plan = CreatePlan();
            var area = new AlpineVillageWalkableArea(plan);
            var ground = new CityMapAlpineVillageTeleportGround(area);
            const float radius = 0.32f;

            for (int index = 0; index < plan.Plots.Count; index++)
            {
                AlpineVillagePlotDescriptor plot = plan.Plots[index];
                Assert.That(
                    ground.TryClampArrival(
                        plot.DoorDockPosition,
                        out Vector3 landing),
                    Is.True,
                    $"'{plot.StableId}' cannot be arrived at.");

                // He is put down looking at the place he asked for, so the
                // room that matters is the room on THAT heading.
                Vector3 towards = plot.DoorDockPosition - landing;
                towards.y = 0f;
                Assert.That(
                    towards.magnitude,
                    Is.GreaterThan(0.5f),
                    $"'{plot.StableId}' lands on its own dock, nose to the " +
                    "threshold.");

                Vector3 heading = towards.normalized;
                Vector3 at = landing;
                float walked = 0f;
                for (int step = 0; step < 200 && walked < 2f; step++)
                {
                    Vector3 next = area.Constrain(
                        at,
                        at + heading * 0.02f,
                        radius);
                    Vector3 moved = next - at;
                    moved.y = 0f;
                    if (moved.magnitude < 0.0005f)
                    {
                        break;
                    }

                    walked += moved.magnitude;
                    at = next;
                }

                Assert.That(
                    walked,
                    Is.GreaterThan(1.6f),
                    $"'{plot.StableId}' arrives with {walked:0.00} m of " +
                    "ground ahead - the hero holds forward and nothing " +
                    "happens.");
            }
        }

        /// <summary>
        /// Snow lies BESIDE what feet have worn and never on it. That is the
        /// whole shape of the field: the route reads as a trodden hollow
        /// because the ground either side of it is deeper, not because a wall
        /// was raked up along its edge.
        /// </summary>
        [Test]
        [Category("AlpineVillage")]
        public void SnowDrifts_LieBesideEveryRouteAndNowhereOnIt()
        {
            AlpineVillagePlan plan = CreatePlan();
            IReadOnlyList<AlpineVillagePathDescriptor> paths =
                AlpineVillagePathPlanner.Create(plan);

            for (float distance = 0f;
                 distance <= plan.Lane.Length;
                 distance += 1f)
            {
                Vector3 centre = plan.Lane.Sample(distance).Position;
                Assert.That(
                    AlpineVillageSnowDrift.SampleDepth(
                        plan,
                        paths,
                        new Vector2(centre.x, centre.z)),
                    Is.LessThan(0.01f),
                    $"Snow is lying on the lane at {distance:0.0} m.");
            }

            int deepShoulders = 0;
            for (int index = 0; index < paths.Count; index++)
            {
                AlpineVillagePathDescriptor path = paths[index];
                Vector3 direction = path.End - path.Start;
                direction.y = 0f;
                direction.Normalize();
                Vector3 right = Vector3.Cross(Vector3.up, direction)
                    .normalized;

                int samples = Mathf.Max(2, Mathf.CeilToInt(path.LengthXZ));
                for (int step = 0; step <= samples; step++)
                {
                    Vector3 centre = Vector3.Lerp(
                        path.Start,
                        path.End,
                        step / (float)samples);
                    Assert.That(
                        AlpineVillageSnowDrift.SampleDepth(
                            plan,
                            paths,
                            new Vector2(centre.x, centre.z)),
                        Is.LessThan(0.01f),
                        $"Snow is lying on '{path.StableId}'.");
                }

                // The middle of the route, one crest offset out on the side
                // the gale unloads into.
                Vector3 middle = Vector3.Lerp(path.Start, path.End, 0.5f);
                for (int side = -1; side <= 1; side += 2)
                {
                    Vector3 outward = right * side;
                    var outwardXZ = new Vector2(outward.x, outward.z);
                    if (AlpineVillageSnowDrift.MeasureExposure(
                            plan,
                            outwardXZ) < 0.75f)
                    {
                        continue;
                    }

                    Vector3 probe = middle +
                                    outward *
                                    (path.SurfaceHalfWidth +
                                     AlpineVillagePathPlanner
                                         .BareSkirtHalfWidth +
                                     AlpineVillageSnowDrift.LeeRiseRun);
                    if (AlpineVillageSnowDrift.SampleDepth(
                            plan,
                            paths,
                            new Vector2(probe.x, probe.z)) >= 0.2f)
                    {
                        deepShoulders++;
                    }
                }
            }

            Assert.That(
                deepShoulders,
                Is.GreaterThan(3),
                "No route in the village has a deep shoulder at all.");
        }

        /// <summary>
        /// The gale scours one lip and loads the other. Symmetric snow is
        /// snow nobody put there - it reads as moulding along a kerb rather
        /// than as weather, which is the failure this whole shape avoids.
        /// </summary>
        [Test]
        [Category("AlpineVillage")]
        public void SnowDrifts_AreScouredToWindwardAndDeepInTheLee()
        {
            AlpineVillagePlan plan = CreatePlan();
            Vector2 wind = AlpineVillageSnowDrift.PrevailingWind(plan);

            Assert.That(
                AlpineVillageSnowDrift.MeasureExposure(plan, wind),
                Is.EqualTo(1f).Within(0.001f),
                "The downwind face is not the one that fills.");
            Assert.That(
                AlpineVillageSnowDrift.MeasureExposure(plan, -wind),
                Is.EqualTo(0f).Within(0.001f));

            AlpineVillageSnowDrift.CrossSection(
                1f,
                out float leeToe,
                out _,
                out float leeFull,
                out _);
            AlpineVillageSnowDrift.CrossSection(
                0f,
                out _,
                out _,
                out float windwardFull,
                out _);

            // The asymmetry lives in the RUN now, not in two crest heights:
            // the gale packs the snow against the trodden edge on the face it
            // unloads into and pushes it back on the face it scours, and the
            // far field is one depth on both because that is what it is.
            Assert.That(
                AlpineVillageSnowDrift.WindwardRiseRun,
                Is.GreaterThan(AlpineVillageSnowDrift.LeeRiseRun * 2f),
                "The two faces of a trodden route deepen at the same rate.");
            Assert.That(leeFull, Is.LessThan(windwardFull));
            Assert.That(leeToe, Is.EqualTo(
                AlpineVillagePathPlanner.BareSkirtHalfWidth).Within(0.0001f),
                "The snow does not start where the bare soil ends.");

            // And it is there in the built field, not only in the constants.
            //
            // Measured on a BRANCH and never on the street: the gale runs
            // down the bowl, so it runs ALONG the lane, and the lane's two
            // shoulders face across it at the same angle. The street is
            // symmetric by construction and always will be; the asymmetry
            // belongs to the routes that cross the wind.
            IReadOnlyList<AlpineVillagePathDescriptor> paths =
                AlpineVillagePathPlanner.Create(plan);
            bool measured = false;
            for (int index = 0; index < paths.Count && !measured; index++)
            {
                AlpineVillagePathDescriptor path = paths[index];
                Vector3 direction = path.End - path.Start;
                direction.y = 0f;
                if (direction.sqrMagnitude <= 0.000001f)
                {
                    continue;
                }

                Vector3 across = Vector3.Cross(
                    Vector3.up,
                    direction.normalized).normalized;
                var acrossXZ = new Vector2(across.x, across.z);
                if (AlpineVillageSnowDrift.MeasureExposure(
                        plan,
                        acrossXZ) < 0.9f)
                {
                    continue;
                }

                Vector3 middle = Vector3.Lerp(path.Start, path.End, 0.5f);
                float offset = path.SurfaceHalfWidth + leeFull;
                Vector3 leeProbe = middle + across * offset;
                Vector3 windwardProbe = middle - across * offset;
                float lee = AlpineVillageSnowDrift.SampleDepth(
                    plan,
                    paths,
                    new Vector2(leeProbe.x, leeProbe.z));
                float windward = AlpineVillageSnowDrift.SampleDepth(
                    plan,
                    paths,
                    new Vector2(windwardProbe.x, windwardProbe.z));
                if (lee <= 0f && windward <= 0f)
                {
                    continue;
                }

                Assert.That(
                    lee,
                    Is.GreaterThan(windward),
                    $"'{path.StableId}' carries the same snow on the face " +
                    "the gale loads and the face it scours.");
                measured = true;
            }

            Assert.That(
                measured,
                Is.True,
                "No branch in the village crosses the prevailing wind, so " +
                "the asymmetry was never actually measured.");
        }

        /// <summary>
        /// THE SNOW ONLY EVER GETS DEEPER AS YOU LEAVE A ROUTE.
        ///
        /// The first cut rose to a lip and died back to bare ground over
        /// three metres, because the snow existed only beside the routes.
        /// That reads as a drift laid along a kerb; a village standing in
        /// deep snow needs the street to be the low place. Monotonic, and
        /// saturating at one field depth on both faces - anything else means
        /// the field has a shape, and a field does not.
        /// </summary>
        [Test]
        [Category("AlpineVillage")]
        public void SnowDepth_RisesWithDistanceAndNeverFallsBack()
        {
            AlpineVillagePlan plan = CreatePlan();
            IReadOnlyList<AlpineVillagePathDescriptor> paths =
                AlpineVillagePathPlanner.Create(plan);

            // A ray out of the lane that meets no apron: a door keeps its own
            // ground clear of snow, so a probe that walks into one measures
            // the threshold rule rather than the field.
            const float Reach = 9f;
            AlpineVillageLaneSample open = default;
            Vector3 outward = Vector3.zero;
            bool found = false;
            for (float distance = 4f;
                 distance <= plan.Lane.Length - 4f && !found;
                 distance += 1f)
            {
                AlpineVillageLaneSample sample = plan.Lane.Sample(distance);
                for (int side = -1; side <= 1 && !found; side += 2)
                {
                    Vector3 candidate = sample.Right * side;
                    if (!IsClearOfEveryApron(
                            plan,
                            sample.Position,
                            candidate,
                            Reach))
                    {
                        continue;
                    }

                    open = sample;
                    outward = candidate;
                    found = true;
                }
            }

            Assert.That(
                found,
                Is.True,
                "No stretch of the lane has nine metres of open snow beside " +
                "it, so this proves nothing about the field.");

            // The field wanders by design - `Variation` is what keeps it from
            // reading as extruded moulding - so "never falls back" is a claim
            // about the ENVELOPE and not about every quarter metre. One step
            // of that wander is the tolerance here; what actually kills the
            // old shape is the last assertion, because a profile that dies
            // back to bare ground cannot be knee-deep nine metres out.
            float wander = AlpineVillageSnowDrift.UntouchedDepth *
                           AlpineVillageSnowDrift.CrestVariation * 0.2f;
            float previous = -1f;
            float last = 0f;
            for (float step = 0f; step <= Reach; step += 0.25f)
            {
                Vector3 probe = open.Position + outward * step;
                float depth = AlpineVillageSnowDrift.SampleDepth(
                    plan,
                    paths,
                    new Vector2(probe.x, probe.z));

                Assert.That(
                    depth,
                    Is.GreaterThanOrEqualTo(previous - wander),
                    $"The snow falls away {step:0.00} m out from the lane " +
                    "by more than its own wander can explain.");
                previous = depth;
                last = depth;
            }

            Assert.That(
                last,
                Is.GreaterThan(AlpineVillageSnowDrift.UntouchedDepth * 0.6f),
                $"Nine metres out from the lane the snow is {last:0.00} m " +
                "deep - it dies back to nothing, so this is a bank beside a " +
                "kerb rather than a field with a trench worn in it.");

        }

        private static bool IsClearOfEveryApron(
            AlpineVillagePlan plan,
            Vector3 origin,
            Vector3 outward,
            float reach)
        {
            for (float step = 0f; step <= reach; step += 0.25f)
            {
                Vector3 world = origin + outward * step;
                var point = new Vector2(world.x, world.z);
                if (AlpineVillageTerrainSampler.DistanceOutsideStation(
                        plan.Station,
                        point) <
                    AlpineVillageSnowDrift.ApronClearance)
                {
                    return false;
                }

                for (int index = 0; index < plan.Plots.Count; index++)
                {
                    if (AlpineVillageTerrainSampler.DistanceOutsidePlot(
                            plan.Plots[index],
                            point) <
                        AlpineVillageSnowDrift.ApronClearance)
                    {
                        return false;
                    }
                }

                // And clear of the spring's water, for exactly the reason
                // the aprons are excluded: snow cannot lie on running water
                // or on ground that never dries, so a ray crossing the brook
                // or its seep line measures that rule instead of the field
                // this is here to measure.
                if (plan.Brook != null &&
                    plan.Brook.DistanceOutsideWetGround(point) <
                    AlpineVillageSnowDrift.WetGroundClearance)
                {
                    return false;
                }
            }

            return true;
        }

        /// <summary>
        /// Nothing lies on a threshold or on the station. A door dock refuses
        /// silently past two centimetres of vertical tolerance, and
        /// knee-deep snow drawn over one is a bug report about a door that
        /// does not work.
        /// </summary>
        [Test]
        [Category("AlpineVillage")]
        public void SnowDrifts_ClearEveryDoorApronAndTheStationPad()
        {
            AlpineVillagePlan plan = CreatePlan();
            IReadOnlyList<AlpineVillagePathDescriptor> paths =
                AlpineVillagePathPlanner.Create(plan);

            for (int index = 0; index < plan.Plots.Count; index++)
            {
                AlpineVillagePlotDescriptor plot = plan.Plots[index];
                Assert.That(
                    AlpineVillageSnowDrift.SampleDepth(
                        plan,
                        paths,
                        new Vector2(
                            plot.DoorDockPosition.x,
                            plot.DoorDockPosition.z)),
                    Is.LessThan(0.01f),
                    $"Snow is lying on '{plot.StableId}''s threshold.");
            }

            MountainRoadTerminalRect pad = plan.Station.PadArea;
            Assert.That(
                AlpineVillageSnowDrift.SampleDepth(
                    plan,
                    paths,
                    new Vector2(pad.Center.x, pad.Center.z)),
                Is.LessThan(0.01f),
                "Snow is lying on the station pad.");
            Vector3 dock = plan.Station.BoardingDockPosition;
            Assert.That(
                AlpineVillageSnowDrift.SampleDepth(
                    plan,
                    paths,
                    new Vector2(dock.x, dock.z)),
                Is.LessThan(0.01f),
                "Snow is lying on the boarding dock.");

            // And none of it climbs the mountain: past the standoff there is
            // no route to drift against and the wall is `74°`.
            Rect bowl = plan.TerrainBounds;
            float outside = AlpineVillageWalkableArea.GroundOutset + 6f;
            Assert.That(
                AlpineVillageSnowDrift.SampleDepth(
                    plan,
                    paths,
                    new Vector2(bowl.center.x, bowl.yMax + outside)),
                Is.LessThan(0.01f),
                "Snow is lying on the enclosing rise.");
        }

        /// <summary>
        /// A drift beside one route disappears where another crosses it. The
        /// depth field is a minimum over EVERY route for exactly this reason;
        /// a per-segment measure cannot see the crossing and leaves a bank
        /// laid across the path it joins.
        /// </summary>
        [Test]
        [Category("AlpineVillage")]
        public void SnowDrifts_VanishWhereRoutesCross()
        {
            AlpineVillagePlan plan = CreatePlan();
            IReadOnlyList<AlpineVillagePathDescriptor> paths =
                AlpineVillagePathPlanner.Create(plan);
            var laneOnly = new AlpineVillagePathDescriptor[0];

            int proven = 0;
            for (int index = 0; index < paths.Count; index++)
            {
                AlpineVillagePathDescriptor path = paths[index];

                // Walk the branch out from the street until it is under the
                // lane's own shoulder - measured against the lane alone,
                // which is exactly what a per-segment field would see there.
                int samples = Mathf.Max(2, Mathf.CeilToInt(path.LengthXZ * 4f));
                for (int step = 0; step <= samples; step++)
                {
                    Vector3 point = Vector3.Lerp(
                        path.Start,
                        path.End,
                        step / (float)samples);
                    var pointXZ = new Vector2(point.x, point.z);
                    if (AlpineVillageSnowDrift.SampleDepth(
                            plan,
                            laneOnly,
                            pointXZ) <= 0.05f)
                    {
                        continue;
                    }

                    // Under the lane's snow, and standing on this branch.
                    // The field has to answer for the branch.
                    Assert.That(
                        AlpineVillageSnowDrift.SampleDepth(
                            plan,
                            paths,
                            pointXZ),
                        Is.LessThan(0.01f),
                        $"The lane's drift lies across '{path.StableId}'.");
                    proven++;
                    break;
                }
            }

            Assert.That(
                proven,
                Is.GreaterThan(2),
                "No branch leaves the street through the lane's own " +
                "shoulder, so this proves nothing about crossings.");
        }

        /// <summary>
        /// THE LANE IS NOT CUT BY ITS OWN GROUND.
        ///
        /// The lane skin is laid flat at the PLAN's centreline height plus a
        /// couple of centimetres, while the ground under it is the sampler's
        /// and is built on a `2 m` grid. Wherever that ground rises above the
        /// skin the terrain wins the depth test and shows through as a pale
        /// wedge with hard polygon edges lying across the street - which is
        /// exactly what was reported as "snow on the path", and is not snow
        /// at all: the same wedges are there with the snow renderer off.
        /// </summary>
        [Test]
        [Category("AlpineVillage")]
        public void LaneSurface_IsNeverCutByItsOwnGround()
        {
            AlpineVillagePlan plan = CreatePlan();
            float lift = AlpineVillageWorldBuilder.LaneSkinLift;

            float worst = 0f;
            Vector3 where = Vector3.zero;
            int cut = 0;
            int probes = 0;
            int across = AlpineVillageWorldBuilder.LaneSkinCrossSteps;
            for (float distance = 0f;
                 distance <= plan.Lane.Length;
                 distance += 0.5f)
            {
                AlpineVillageLaneSample sample = plan.Lane.Sample(distance);
                float half = sample.Width * 0.5f;
                for (int step = 0; step < across; step++)
                {
                    // The chord between two neighbouring skin vertices is
                    // what actually gets drawn; the ground bulging above THAT
                    // is what shows through.
                    float left = Mathf.Lerp(-half, half, step / (float)across);
                    float right = Mathf.Lerp(
                        -half,
                        half,
                        (step + 1) / (float)across);
                    Vector3 a = sample.Position + sample.Right * left;
                    Vector3 b = sample.Position + sample.Right * right;
                    float skinA = SampleGround(plan, a) + lift;
                    float skinB = SampleGround(plan, b) + lift;
                    for (float t = 0f; t <= 1f; t += 0.25f)
                    {
                        Vector3 point = Vector3.Lerp(a, b, t);
                        float ground = SampleGround(plan, point);
                        float skin = Mathf.Lerp(skinA, skinB, t);
                        probes++;
                        float proud = ground - skin;
                        if (proud <= 0f)
                        {
                            continue;
                        }

                        cut++;
                        if (proud > worst)
                        {
                            worst = proud;
                            where = point;
                        }
                    }
                }
            }

            Assert.That(
                cut,
                Is.Zero,
                $"The ground rises through the lane skin at {cut} of " +
                $"{probes} probes; the worst stands {worst:0.000} m proud " +
                $"at {where}.");
        }

        private static float SampleGround(
            AlpineVillagePlan plan,
            Vector3 point)
        {
            return AlpineVillageTerrainSampler.SampleHeight(
                plan,
                new Vector2(point.x, point.z));
        }

        /// <summary>
        /// NO SNOW LIES ON A ROUTE, and the mesh has to say so - not just
        /// the field.
        ///
        /// The depth field is zero over every trodden surface and always was.
        /// A mesh only knows what its vertices know, though, and the first
        /// cut carried four across a ribbon `4.5 m` wide: a route crossing
        /// inside that three-metre gap was BRIDGED, one quad of full-depth
        /// snow laid straight over trodden ground. This walks the built
        /// triangles instead of the field, which is the only way to see it.
        /// </summary>
        [Test]
        [Category("AlpineVillage")]
        public void SnowMesh_NeverLiesOverATroddenRoute()
        {
            AlpineVillagePlan plan = CreatePlan();
            IReadOnlyList<AlpineVillagePathDescriptor> paths =
                AlpineVillagePathPlanner.Create(plan);
            var host = new GameObject("Snow Bridging Probe");
            try
            {
                AlpineVillageWorldResult world =
                    AlpineVillageWorldBuilder.Build(host.transform, plan);
                Transform drifts = world.Root.transform.Find(
                    AlpineVillageWorldBuilder.SnowDriftObjectName);
                Assert.That(drifts, Is.Not.Null);
                Mesh mesh = drifts.GetComponent<MeshFilter>().sharedMesh;
                Vector3[] vertices = mesh.vertices;
                int[] triangles = mesh.triangles;

                // ACROSS THE WHOLE TRIANGLE, not at its centre. A quad a
                // metre wide can cover most of a path while its centroid
                // sits comfortably off it - which is exactly what the first
                // version of this test missed and the screen did not.
                var weights = new[]
                {
                    new Vector3(1f, 0f, 0f),
                    new Vector3(0f, 1f, 0f),
                    new Vector3(0f, 0f, 1f),
                    new Vector3(0.5f, 0.5f, 0f),
                    new Vector3(0f, 0.5f, 0.5f),
                    new Vector3(0.5f, 0f, 0.5f),
                    new Vector3(1f / 3f, 1f / 3f, 1f / 3f),
                    new Vector3(0.6f, 0.2f, 0.2f),
                    new Vector3(0.2f, 0.6f, 0.2f),
                    new Vector3(0.2f, 0.2f, 0.6f)
                };

                int bridged = 0;
                Vector3 worst = Vector3.zero;
                float worstLift = 0f;
                for (int index = 0; index + 2 < triangles.Length; index += 3)
                {
                    Vector3 a = vertices[triangles[index]];
                    Vector3 b = vertices[triangles[index + 1]];
                    Vector3 c = vertices[triangles[index + 2]];
                    bool flagged = false;
                    for (int step = 0;
                         step < weights.Length && !flagged;
                         step++)
                    {
                        Vector3 w = weights[step];
                        Vector3 point = a * w.x + b * w.y + c * w.z;
                        var pointXZ = new Vector2(point.x, point.z);
                        // Over the COMPACTED RIBBON, which is what a path
                        // is. Not the bare skirt beyond it:
                        // `BareSkirtHalfWidth` exists precisely to say where
                        // snow may start again.
                        float outside = AlpineVillagePathPlanner
                            .MeasureDistanceOutsideTrodden(
                                plan,
                                paths,
                                pointXZ,
                                out _);
                        if (outside > 0f)
                        {
                            continue;
                        }

                        float ground =
                            AlpineVillageTerrainSampler.SampleHeight(
                                plan,
                                pointXZ);
                        float lift = point.y - ground;
                        if (lift <= 0.02f)
                        {
                            continue;
                        }

                        bridged++;
                        flagged = true;
                        if (lift > worstLift)
                        {
                            worstLift = lift;
                            worst = point;
                        }
                    }
                }

                Assert.That(
                    bridged,
                    Is.Zero,
                    $"{bridged} snow triangles lie over trodden ground; the " +
                    $"worst stands {worstLift:0.00} m proud at {worst}.");
            }
            finally
            {
                Object.DestroyImmediate(host);
            }
        }

        /// <summary>
        /// Walking through the snow presses it down, and the snow says what
        /// is left rather than what the plan wanted.
        /// </summary>
        [Test]
        [Category("AlpineVillage")]
        public void SnowTreading_PressesDownWhereHeWalks()
        {
            AlpineVillagePlan plan = CreatePlan();
            var host = new GameObject("Snow Treading Probe");
            try
            {
                AlpineVillageWorldResult world =
                    AlpineVillageWorldBuilder.Build(host.transform, plan);
                AlpineVillageSnowTreading treading = world.SnowTreading;
                Assert.That(
                    treading,
                    Is.Not.Null,
                    "The village built no treadable snow.");

                // Open snow beside the lane, clear of every apron.
                Vector3 spot = Vector3.zero;
                float before = 0f;
                for (float distance = 4f;
                     distance <= plan.Lane.Length - 4f;
                     distance += 1f)
                {
                    AlpineVillageLaneSample sample =
                        plan.Lane.Sample(distance);
                    for (int side = -1; side <= 1; side += 2)
                    {
                        Vector3 outward = sample.Right * side;
                        if (!IsClearOfEveryApron(
                                plan,
                                sample.Position,
                                outward,
                                8f))
                        {
                            continue;
                        }

                        for (float out3 = 3f; out3 <= 8f; out3 += 0.5f)
                        {
                            Vector3 probe = sample.Position + outward * out3;
                            float depth =
                                treading.SampleVisibleDepth(probe);
                            if (depth > before)
                            {
                                before = depth;
                                spot = probe;
                            }
                        }
                    }
                }

                Assert.That(
                    before,
                    Is.GreaterThan(0.2f),
                    "Found no deep snow beside the lane to tread on.");

                // A PASS, NOT A STAMP - and the difference is the whole test.
                //
                // `SampleVisibleDepth` reads the NEAREST field vertex and
                // accepts one out to `FieldCellSize` (a metre), while `Press`
                // reaches only `TreadRadius` (0.55 m). So a single press at a
                // probe point can be measured against a vertex it never
                // touched, and whether it does is decided by where the metre
                // grid happens to fall - the assertion passed on alignment
                // luck rather than on the snow being pressed. Walking a short
                // pass through the spot, which is what the name claims and
                // what a hero does, covers the vertex under it either way.
                // A cross is not enough either: the field vertex can sit
                // diagonally off the probe, which a pair of axis lines never
                // comes closer to than a quarter metre. Trample the cell.
                for (float east = -0.6f; east <= 0.6f; east += 0.2f)
                {
                    for (float north = -0.6f; north <= 0.6f; north += 0.2f)
                    {
                        treading.Press(
                            spot +
                            Vector3.right * east +
                            Vector3.forward * north);
                    }
                }

                float after = treading.SampleVisibleDepth(spot);
                Assert.That(
                    after,
                    Is.LessThan(before * 0.5f),
                    $"A pass through {before:0.00} m of snow left " +
                    $"{after:0.00} m - it is not being pressed down.");

                // And a step there sounds like snow, not like the path.
                Assert.That(
                    treading.TryPlayFootstep(spot, 0f),
                    Is.True,
                    "The snow did not claim its own footstep.");
            }
            finally
            {
                Object.DestroyImmediate(host);
            }
        }

        /// <summary>
        /// The contract that keeps the decision honest: the snow is a look
        /// and not a shape. Give it a collider and the hero catches a boot on
        /// every shoulder - planar velocity is read back from achieved
        /// movement, so a graze reads as a crawl - and the walkable bowl this
        /// scene just opened closes again from underneath.
        /// </summary>
        [Test]
        [Category("AlpineVillage")]
        public void SnowDrifts_CarryNoCollision()
        {
            AlpineVillagePlan plan = CreatePlan();
            var host = new GameObject("Snow Drift Collision Probe");
            try
            {
                AlpineVillageWorldResult world =
                    AlpineVillageWorldBuilder.Build(host.transform, plan);
                Transform drifts = world.Root.transform.Find(
                    AlpineVillageWorldBuilder.SnowDriftObjectName);
                Assert.That(
                    drifts,
                    Is.Not.Null,
                    "The village built no lying snow at all.");
                Assert.That(
                    drifts.GetComponentsInChildren<Collider>(true),
                    Is.Empty,
                    "The lying snow has become geometry the hero can trip " +
                    "on.");

                MeshFilter filter = drifts.GetComponent<MeshFilter>();
                Assert.That(filter, Is.Not.Null);
                Assert.That(
                    filter.sharedMesh.vertexCount,
                    Is.GreaterThan(500),
                    "The drift mesh is too coarse to hold a profile.");

                Mesh mesh = filter.sharedMesh;
                Vector3[] vertices = mesh.vertices;
                Vector2[] uv = mesh.uv;
                Assert.That(uv.Length, Is.EqualTo(vertices.Length));
                float expectedUvScale = 1f /
                    MountainRoadSurfaceAppearance
                        .GetRecipe(AlpineVillageRidgeAppearance.Surface)
                        .MetersPerTile;
                for (int index = 0; index < vertices.Length; index++)
                {
                    Assert.That(
                        uv[index].x,
                        Is.EqualTo(vertices[index].x * expectedUvScale)
                            .Within(0.0001f));
                    Assert.That(
                        uv[index].y,
                        Is.EqualTo(vertices[index].z * expectedUvScale)
                            .Within(0.0001f));
                }

                var properties = new MaterialPropertyBlock();
                drifts.GetComponent<MeshRenderer>().GetPropertyBlock(
                    properties,
                    0);
                Assert.That(
                    properties.GetVector("_BaseMap_ST"),
                    Is.EqualTo(
                        AlpineVillageRidgeAppearance.BakedUvTransform));
            }
            finally
            {
                Object.DestroyImmediate(host);
            }
        }
    }
}

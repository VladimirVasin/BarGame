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
            for (int index = 0; index < outward.Length; index++)
            {
                float height = AlpineVillageTerrainSampler.SampleHeight(
                    plan,
                    outward[index]);
                Assert.That(
                    height - inside,
                    Is.GreaterThan(8f),
                    $"The village is open at {outward[index]}.");
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
        /// Snow with a ceiling. §12 bans the storm outright, so no schedule
        /// slot and no altitude may produce one - and the altitude multiplier
        /// has to land AFTER the clamp, or the worst weather arrives no
        /// heavier than it left.
        /// </summary>
        [Test]
        [Category("AlpineVillage")]
        public void Weather_AlwaysSnowsALittleAndNeverStorms()
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
        }

        /// <summary>
        /// The village is sheltered where the road was exposed: the same city
        /// wind arrives weaker here than it does on the climb below.
        /// </summary>
        [Test]
        [Category("AlpineVillage")]
        public void Weather_TakesTheWindOutOfTheCitysWeather()
        {
            var gale = new WindSample(120f, 1f);
            float village = AlpineVillageWeatherRules.EvaluateStrength(
                gale,
                1f);
            float road = MountainRoadWeatherRules.EvaluateSwayAmplitude(
                gale,
                1f);

            Assert.That(village, Is.LessThan(road));
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

            // The bearing is shared with the city so cloth, snow and crowns
            // all agree; only the strength is a village decision.
            Assert.That(
                AlpineVillageWeatherRules.EvaluateWind(gale, 0.5f)
                    .DirectionDegrees,
                Is.EqualTo(gale.DirectionDegrees).Within(0.0001f));
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
    }
}

using System;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;

namespace BarPromenade.Tests.EditMode
{
    /// <summary>
    /// The way back: the two-point turn off the mountain apron, the descent it
    /// opens, the city road read the other way round, and the one session
    /// value that closes the ring.
    ///
    /// Everything here is pure data. The manoeuvre in particular is asserted
    /// against the same ground the arrival is - the terminal's own turning
    /// pocket, the cafe footprint and the cableway station - because the whole
    /// argument for backing round rather than looping is that a loop does not
    /// fit and this does.
    /// </summary>
    public sealed class LastRouteReturnRideTests
    {
        /// <summary>The project's own number for this car's body, and the one
        /// the drivable-surface tests use.</summary>
        private const float CarHalfWidth = 1.05f;

        /// <summary>Half the authored body length, for sweeping the car
        /// rather than its centre through the manoeuvre.</summary>
        private const float CarHalfLength = 2.415f;

        private static MountainRoadPlan BuildMountainPlan()
        {
            return MountainRoadPlanner.Create(
                MountainRoadPlanner.DefaultSeed);
        }

        private static void CreateCityContext(
            out CityLayout layout,
            out CityTunnelForecourtDescriptor forecourt,
            out CityTunnelTravelPlan tunnelPlan,
            out LastRouteCarPlan carPlan)
        {
            layout = CityLayoutGenerator.Generate(
                CityBlueprintCatalog.Resolve(
                    GameSessionState.DefaultCityBlueprintId),
                CityGenerationSettings.Default,
                GameSessionState.DefaultCitySeed);

            CityMountainBoundaryPlan mountains =
                CityMountainBoundaryPlanner.Create(layout);
            Assert.That(mountains.HasTunnel, Is.True);
            CityFringeYardPlan yards =
                CityFringeYardPlanner.Create(layout, mountains);
            Assert.That(yards.HasTunnelForecourt, Is.True);
            forecourt = yards.TunnelForecourt;
            tunnelPlan = CityTunnelTravelPlanner.Create(mountains.Tunnel);
            carPlan = LastRouteCarPlan.Create(layout);
            Assert.That(carPlan.IsPresent, Is.True);
        }

        [SetUp]
        public void SetUp()
        {
            GameSessionState.BeginNewGame();
        }

        [TearDown]
        public void TearDown()
        {
            GameSessionState.BeginNewGame();
        }

        [Test]
        [Category("MountainRoad")]
        public void Descent_StartsParkedAndEndsWhereTheClimbBegan()
        {
            MountainRoadPlan plan = BuildMountainPlan();
            LastRouteMountainDrivePlanner.ResolveParkedPose(
                plan,
                out Vector3 parked,
                out Vector3 parkedFacing);
            LastRouteCarDrivePath path =
                LastRouteMountainDrivePlanner.CreateDeparture(plan);

            Assert.That(
                Vector3.Distance(path.Start, parked),
                Is.LessThan(0.01f),
                "It has to begin under the car that is standing there, or " +
                "the first frame of the drive is a jump.");

            path.Sample(0f, out _, out Vector3 startFacing);
            startFacing.y = 0f;
            Assert.That(
                Vector3.Angle(startFacing.normalized, parkedFacing),
                Is.LessThan(3.5f),
                "And pointing the way he parked - he backs out of the bay " +
                "rather than being turned round before the beat begins. " +
                "The slack is a measured artefact rather than a guess: an " +
                "arc cut into chords reports its first vertex's heading as " +
                "that chord, which is half a segment off the true tangent, " +
                "and half a segment here is 2.81 degrees. Anything this is " +
                "meant to catch - a manoeuvre built off the wrong flank, a " +
                "car turned round before the beat - is tens of degrees.");

            Assert.That(
                Vector3.Distance(path.End, plan.Tunnel.SpawnPosition),
                Is.LessThan(0.01f),
                "The two halves of the round trip meet at ONE point in the " +
                "tunnel: the descent stops exactly where the climb starts.");

            path.Sample(path.Length, out _, out Vector3 endFacing);
            Assert.That(
                Vector3.Angle(endFacing, -plan.Tunnel.SpawnForward),
                Is.LessThan(3f),
                "Facing into the mountain, which is the direction the load " +
                "is asked for from.");
        }

        [Test]
        [Category("MountainRoad")]
        public void Descent_OpensWithATwoPointTurnAndThenGoesForwards()
        {
            MountainRoadPlan plan = BuildMountainPlan();
            MountainRoadVehicleApronPlan apron = plan.Terminal.VehicleApron;
            LastRouteCarDrivePath path =
                LastRouteMountainDrivePlanner.CreateDeparture(plan);

            Assert.That(
                path.HasReverseLead,
                Is.True,
                "There is no room in the pocket for a U-turn, so the beat " +
                "IS the reverse leg. Losing it loses the whole manoeuvre.");

            float quarter = Mathf.PI * 0.5f *
                            LastRouteMountainDrivePlanner
                                .ApronTurnRadiusMeters;
            Assert.That(
                path.ReverseLength,
                Is.EqualTo(quarter).Within(0.05f),
                "A quarter turn of the authored radius and nothing more - " +
                "he backs round, he does not reverse down the mountain.");

            Assert.That(path.IsReversingAt(0.1f), Is.True);
            Assert.That(
                path.IsReversingAt(path.ReverseLength + 0.1f),
                Is.False,
                "Past the cusp he is driving.");
            Assert.That(
                path.IsReversingAt(path.Length - 1f),
                Is.False,
                "And the whole descent is forwards.");

            // The cusp: standing across the pocket, nose toward the cafe.
            path.Sample(
                path.ReverseLength,
                out Vector3 cuspPosition,
                out Vector3 cuspFacing);
            cuspFacing.y = 0f;
            Assert.That(
                Vector3.Angle(cuspFacing.normalized, -apron.Right),
                Is.LessThan(3f),
                "The reverse leg ends square across the pocket; anything " +
                "else means the two arcs no longer meet at a quarter turn.");

            Vector3 expectedCusp = apron.Center +
                                   (apron.Right *
                                    LastRouteMountainDrivePlanner
                                        .ApronTurnRadiusMeters) -
                                   (apron.Forward *
                                    LastRouteMountainDrivePlanner
                                        .ApronTurnRadiusMeters);
            Assert.That(
                Vector3.Distance(cuspPosition, expectedCusp),
                Is.LessThan(0.05f),
                "And it ends where the geometry says: one radius across and " +
                "one radius back.");
        }

        [Test]
        [Category("MountainRoad")]
        public void Descent_KeepsOneHeadingThroughTheChangeOfGear()
        {
            MountainRoadPlan plan = BuildMountainPlan();
            LastRouteCarDrivePath path =
                LastRouteMountainDrivePlanner.CreateDeparture(plan);

            // The cusp is a reversal of TRAVEL, never of the body. A path
            // that averaged raw segment directions would swing the car
            // through a hundred and eighty degrees over the two segments
            // around it; the reverse leg's headings are negated at build
            // time precisely so this cannot happen.
            path.Sample(
                path.ReverseLength - 0.4f,
                out _,
                out Vector3 before);
            path.Sample(
                path.ReverseLength + 0.4f,
                out _,
                out Vector3 after);
            Assert.That(
                Vector3.Angle(before, after),
                Is.LessThan(15f),
                "The car pivots through the change of gear instead of " +
                "driving through it.");

            // And nowhere on the whole road does the heading break: a cusp
            // that slipped through would show up as one enormous turn rate.
            float worst = 0f;
            for (int index = 0; index < path.PointCount; index++)
            {
                worst = Mathf.Max(worst, path.GetTurnRate(index));
            }

            Assert.That(
                worst,
                Is.LessThan(30f),
                "Degrees of heading per metre. The manoeuvre's own arcs run " +
                "at about eleven and a half; anything near a right angle in " +
                "a single vertex is a seam.");
        }

        [Test]
        [Category("MountainRoad")]
        public void Descent_ManoeuvreStaysOnGroundTheTerminalHoldsClear()
        {
            MountainRoadPlan plan = BuildMountainPlan();
            MountainRoadVehicleApronPlan apron = plan.Terminal.VehicleApron;
            MountainRoadCafePlan cafe = plan.Terminal.Cafe;
            LastRouteCarDrivePath path =
                LastRouteMountainDrivePlanner.CreateDeparture(plan);
            var walkable = new MountainRoadWalkableArea(plan);

            // The manoeuvre plus a little of the road it leaves on.
            float end = path.ReverseLength * 2f + 4f;
            for (float distance = 0f; distance <= end; distance += 0.25f)
            {
                path.Sample(
                    distance,
                    out Vector3 position,
                    out Vector3 forward);
                Vector3 right = Vector3.Cross(Vector3.up, forward).normalized;

                Assert.That(
                    walkable.Contains(position, CarHalfWidth),
                    Is.True,
                    $"The manoeuvre leaves drivable ground at {distance:0.0} m.");

                // The body, not the centreline: the four corners of a car
                // this long swung about this heading.
                foreach (float along in new[] { -CarHalfLength, 0f, CarHalfLength })
                {
                    foreach (float across in new[] { -CarHalfWidth, CarHalfWidth })
                    {
                        Vector3 corner = position +
                                         (forward * along) +
                                         (right * across);
                        corner.y = cafe.FloorY;
                        Assert.That(
                            cafe.ContainsInterior(corner, 0f),
                            Is.False,
                            $"The car reaches the cafe at {distance:0.0} m " +
                            "of the manoeuvre.");
                        Assert.That(
                            plan.Terminal.Cableway.ContainsClearanceXZ(
                                new Vector2(corner.x, corner.z),
                                0f),
                            Is.False,
                            $"The car reaches the cableway station at " +
                            $"{distance:0.0} m of the manoeuvre.");
                    }
                }
            }

            // And the claim the radius was chosen for: the cusp is inside
            // the apron's own validated disc rather than out on the snow.
            path.Sample(path.ReverseLength, out Vector3 cusp, out _);
            Assert.That(
                Vector2.Distance(
                    new Vector2(cusp.x, cusp.z),
                    new Vector2(apron.Center.x, apron.Center.z)),
                Is.LessThanOrEqualTo(apron.TurningRadius),
                "Widen the turn and the tail swings off the paving.");
        }

        [Test]
        [Category("MountainRoad")]
        public void Descent_FollowsTheSameCorridorTheClimbDoes()
        {
            MountainRoadPlan plan = BuildMountainPlan();
            LastRouteCarDrivePath path =
                LastRouteMountainDrivePlanner.CreateDeparture(plan);
            var walkable = new MountainRoadWalkableArea(plan);

            for (float distance = 0f;
                 distance <= path.Length;
                 distance += 1f)
            {
                path.Sample(distance, out Vector3 position, out _);
                Assert.That(
                    walkable.Contains(position, CarHalfWidth),
                    Is.True,
                    $"The descent corridor breaks at {distance:0.0} m.");
            }

            Assert.That(
                path.Length,
                Is.GreaterThan(plan.Route.Length),
                "The whole road, plus the manoeuvre and the run into the " +
                "tunnel.");
        }

        [Test]
        public void DriveModel_StopsAtTheCuspAndChangesDirectionOnce()
        {
            // A right angle backed round, then a straight away from it, at a
            // metre a vertex. Synthetic on purpose: this is about the model,
            // not about any one mountain.
            var points = new List<Vector3>();
            for (int step = 0; step <= 8; step++)
            {
                points.Add(new Vector3(step, 0f, 0f));
            }

            for (int step = 1; step <= 20; step++)
            {
                points.Add(new Vector3(8f, 0f, step));
            }

            var path = new LastRouteCarDrivePath(points, 9);
            var model = new LastRouteCarDriveModel(
                path,
                LastRouteCarDriveProfile.Mountain);
            model.Resume(0f);

            Assert.That(model.IsReversing, Is.True);

            bool sawStop = false;
            bool sawForwardAfterStop = false;
            float topReverseSpeed = 0f;
            for (int frame = 0; frame < 3000 && !model.HasArrived; frame++)
            {
                if (model.IsReversing)
                {
                    topReverseSpeed = Mathf.Max(topReverseSpeed, model.Speed);
                }

                model.Advance(1f / 60f);
                if (model.IsChangingDirection)
                {
                    sawStop = true;
                    Assert.That(
                        model.Speed,
                        Is.EqualTo(0f).Within(0.0001f),
                        "A car cannot be in two gears at once.");
                    Assert.That(
                        model.Distance,
                        Is.EqualTo(path.ReverseLength).Within(0.01f),
                        "And it has to stop ON the cusp, not past it.");
                }
                else if (sawStop && model.Speed > 0.5f)
                {
                    sawForwardAfterStop = true;
                    Assert.That(
                        model.IsReversing,
                        Is.False,
                        "Once he has changed gear he does not change back.");
                }
            }

            Assert.That(sawStop, Is.True, "He never stopped to change gear.");
            Assert.That(
                sawForwardAfterStop,
                Is.True,
                "He stopped and never pulled away again.");
            Assert.That(
                model.HasArrived,
                Is.True,
                "The manoeuvre swallowed the drive.");
            Assert.That(
                topReverseSpeed,
                Is.LessThanOrEqualTo(
                    LastRouteCarDriveModel.ReverseSpeed + 0.01f),
                "Nobody reverses at road speed.");
        }

        [Test]
        public void DriveModel_SkippedPastTheCuspIsAlreadyInGear()
        {
            var points = new List<Vector3>();
            for (int step = 0; step <= 6; step++)
            {
                points.Add(new Vector3(step, 0f, 0f));
            }

            for (int step = 1; step <= 30; step++)
            {
                points.Add(new Vector3(6f, 0f, step));
            }

            var path = new LastRouteCarDrivePath(points, 7);
            var model = new LastRouteCarDriveModel(
                path,
                LastRouteCarDriveProfile.Mountain);

            // What a skip does: the distance moves and nothing else. A model
            // that still thought the manoeuvre was ahead of it would stop
            // dead in the middle of the road looking for a gear.
            model.Resume(0f, path.Length * 0.5f);
            Assert.That(model.IsReversing, Is.False);
            Assert.That(model.IsChangingDirection, Is.False);
            for (int frame = 0; frame < 60; frame++)
            {
                model.Advance(1f / 60f);
            }

            Assert.That(
                model.Speed,
                Is.GreaterThan(0.5f),
                "It pulls away from a skip rather than hunting for a cusp " +
                "it is already past.");
        }

        [Test]
        public void CityReturn_ComesOutOfTheTunnelAndStopsInTheBay()
        {
            CreateCityContext(
                out CityLayout layout,
                out CityTunnelForecourtDescriptor forecourt,
                out CityTunnelTravelPlan tunnelPlan,
                out LastRouteCarPlan carPlan);

            LastRouteCityDrivePlanner.ResolveReturnEntryPose(
                forecourt,
                tunnelPlan.FloorSurfaceY,
                out Vector3 entry,
                out Vector3 entryFacing);
            LastRouteCarDrivePath path =
                LastRouteCityDrivePlanner.CreateReturn(
                    carPlan,
                    layout,
                    forecourt,
                    tunnelPlan.FloorSurfaceY);

            Assert.That(
                Vector3.Distance(path.Start, entry),
                Is.LessThan(0.01f),
                "The homecoming begins where the departure ended - one " +
                "point in one tunnel, so the two cannot drift apart.");

            path.Sample(0f, out _, out Vector3 startFacing);
            Assert.That(
                Vector3.Angle(startFacing, entryFacing),
                Is.LessThan(5f),
                "Pointing out of the mountain, which is the way it is about " +
                "to drive.");

            Assert.That(
                Vector3.Distance(path.End, carPlan.Position),
                Is.LessThan(0.01f),
                "And it ends in the bay he pulled out of, so the island is " +
                "not left with a car parked beside it.");
            Assert.That(
                path.HasReverseLead,
                Is.False,
                "Nothing on this side is reversed: the bay can be driven " +
                "into and only backed out of.");
        }

        [Test]
        public void CityReturn_DrivesTheOtherHalfOfTheCarriageway()
        {
            CreateCityContext(
                out CityLayout layout,
                out CityTunnelForecourtDescriptor forecourt,
                out CityTunnelTravelPlan tunnelPlan,
                out LastRouteCarPlan carPlan);

            LastRouteCarDrivePath outbound =
                LastRouteCityDrivePlanner.CreateDeparture(
                    carPlan,
                    layout,
                    forecourt,
                    tunnelPlan.FloorSurfaceY);
            LastRouteCarDrivePath inbound =
                LastRouteCityDrivePlanner.CreateReturn(
                    carPlan,
                    layout,
                    forecourt,
                    tunnelPlan.FloorSurfaceY);

            Assert.That(
                inbound.Length,
                Is.EqualTo(outbound.Length).Within(outbound.Length * 0.25f),
                "It is the same road: the two lanes of one street are not a " +
                "quarter of its length apart.");

            // The lane is what changes. Walk the middle of the drive - past
            // the lot exit and short of the forecourt, where both roads are
            // simply following the same street - and check that the two
            // never share a line.
            int sampled = 0;
            int separated = 0;
            for (float t = 0.35f; t <= 0.65f; t += 0.01f)
            {
                outbound.Sample(
                    outbound.Length * t,
                    out Vector3 there,
                    out _);
                float nearest = float.PositiveInfinity;
                for (float u = 0f; u <= 1f; u += 0.002f)
                {
                    inbound.Sample(
                        inbound.Length * u,
                        out Vector3 back,
                        out _);
                    nearest = Mathf.Min(
                        nearest,
                        Vector2.Distance(
                            new Vector2(there.x, there.z),
                            new Vector2(back.x, back.z)));
                }

                sampled++;
                if (nearest > 1f)
                {
                    separated++;
                }
            }

            Assert.That(sampled, Is.GreaterThan(20));
            Assert.That(
                separated,
                Is.GreaterThan(sampled / 2),
                "Most of the way home he is on his own side of the crown. " +
                "Reversing the outbound points without negating the lane " +
                "offset drives him home down the oncoming half.");
        }

        [Test]
        public void CityReturn_StillLooksBeforeItJoinsTheStreet()
        {
            CreateCityContext(
                out CityLayout layout,
                out CityTunnelForecourtDescriptor forecourt,
                out CityTunnelTravelPlan tunnelPlan,
                out LastRouteCarPlan carPlan);

            LastRouteCarDrivePath path =
                LastRouteCityDrivePlanner.CreateReturn(
                    carPlan,
                    layout,
                    forecourt,
                    tunnelPlan.FloorSurfaceY);

            LastRouteCarGiveWayPoint crossing = path.GiveWay;
            Assert.That(
                crossing.IsPresent,
                Is.True,
                "Coming out of the forecourt crosses the same carriageway " +
                "going into it did, and the bus uses that street.");
            Assert.That(
                crossing.Distance,
                Is.GreaterThan(0f).And.LessThan(path.Length),
                "The stop line has to be somewhere on the road.");

            path.Sample(crossing.Distance, out Vector3 stopped, out _);
            Assert.That(
                Vector2.Distance(
                    new Vector2(stopped.x, stopped.z),
                    new Vector2(
                        forecourt.StreetAnchor.x,
                        forecourt.StreetAnchor.z)),
                Is.LessThan(
                    LastRouteCityDrivePlanner.GiveWayStandoffMeters + 4f),
                "And it waits on the forecourt run, within a car or two of " +
                "the mouth - not somewhere back inside the tunnel.");
        }

        [Test]
        public void RideStage_ClosesTheRingAtExactlyOnePlace()
        {
            Assert.That(
                GameSessionState.FerrymanRide,
                Is.EqualTo(LastRouteFerrymanRideStage.NotTaken));

            Assert.That(
                GameSessionState.TryAdvanceFerrymanRide(
                    LastRouteFerrymanRideStage.NotTaken),
                Is.False,
                "Standing still is not a step.");

            foreach (LastRouteFerrymanRideStage stage in new[]
                     {
                         LastRouteFerrymanRideStage.InTransit,
                         LastRouteFerrymanRideStage.Arrived,
                         LastRouteFerrymanRideStage.Returning
                     })
            {
                Assert.That(
                    GameSessionState.TryAdvanceFerrymanRide(stage),
                    Is.True,
                    $"The ring must reach {stage}.");
            }

            Assert.That(
                GameSessionState.IsRidingTheFerryman,
                Is.True,
                "Coming down is as much a ride as going up: nothing that " +
                "teleports the hero may fire while he is in that seat.");

            Assert.That(
                GameSessionState.TryAdvanceFerrymanRide(
                    LastRouteFerrymanRideStage.Arrived),
                Is.False,
                "Only ONE step goes backwards, and it is not this one.");

            Assert.That(
                GameSessionState.TryAdvanceFerrymanRide(
                    LastRouteFerrymanRideStage.NotTaken),
                Is.True,
                "The car reaching the island again is the ring closing.");
            Assert.That(
                GameSessionState.FerrymanRide,
                Is.EqualTo(LastRouteFerrymanRideStage.NotTaken));
            Assert.That(GameSessionState.IsRidingTheFerryman, Is.False);
        }

        [Test]
        public void RideStage_CannotBeUnwoundFromAnywhereElse()
        {
            Assert.That(
                GameSessionState.TryAdvanceFerrymanRide(
                    LastRouteFerrymanRideStage.Arrived),
                Is.True);
            Assert.That(
                GameSessionState.TryAdvanceFerrymanRide(
                    LastRouteFerrymanRideStage.NotTaken),
                Is.False,
                "A car parked on the mountain does not reappear on the " +
                "island; it has to be DRIVEN back, which is the Returning " +
                "stage this refuses to skip.");
        }

        [Test]
        public void ReturnOffer_ResolvesInBothCatalogues()
        {
            Dictionary<string, string> russian =
                LoadCatalog("Localization/ru");
            Dictionary<string, string> english =
                LoadCatalog("Localization/en");

            string key =
                LastRouteFerrymanInteraction.ReturnConfirmationPromptKey;
            Assert.That(russian.ContainsKey(key), Is.True, $"ru: {key}");
            Assert.That(english.ContainsKey(key), Is.True, $"en: {key}");
            Assert.That(russian[key], Is.Not.Null.And.Not.Empty);
            Assert.That(english[key], Is.Not.Null.And.Not.Empty);
            Assert.That(
                russian[key].EndsWith("?", StringComparison.Ordinal),
                Is.True,
                "The second option on his menu is a question, exactly as " +
                "the island's is.");
        }

        [Test]
        public void Voices_AreOneChoiceTakenInOnePlace()
        {
            LastRouteFerrymanVoice island =
                LastRouteFerrymanVoice.Island(GameSessionState.DefaultCitySeed);
            LastRouteFerrymanVoice mountain =
                LastRouteFerrymanVoice.Mountain(
                    GameSessionState.DefaultCitySeed);

            Assert.That(island.IsPresent, Is.True);
            Assert.That(mountain.IsPresent, Is.True);
            Assert.That(
                island.ConfirmationPromptKey,
                Is.Not.EqualTo(mountain.ConfirmationPromptKey),
                "The two ends do not ask the same question.");
            Assert.That(
                island.LineKeys,
                Is.Not.SameAs(mountain.LineKeys),
                "Nor speak from the same pool.");
            Assert.That(
                island.QuipStream,
                Is.Not.EqualTo(mountain.QuipStream),
                "And above all not off the same stream - one stream serves " +
                "the same ordinal answer in both places on the same visit.");
        }

        private static Dictionary<string, string> LoadCatalog(string path)
        {
            var asset = Resources.Load<TextAsset>(path);
            Assert.That(asset, Is.Not.Null, $"Missing catalog '{path}'.");
            var catalog = JsonUtility.FromJson<Catalog>(asset.text);
            var map = new Dictionary<string, string>(StringComparer.Ordinal);
            for (int index = 0; index < catalog.entries.Length; index++)
            {
                map[catalog.entries[index].key] =
                    catalog.entries[index].value;
            }

            return map;
        }

        [Serializable]
        private sealed class Catalog
        {
            public Entry[] entries;
        }

        [Serializable]
        private sealed class Entry
        {
            public string key;
            public string value;
        }
    }
}

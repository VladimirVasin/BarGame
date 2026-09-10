using System;
using System.Collections;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

namespace BarPromenade.Tests.PlayMode
{
    public sealed partial class AreaCaptureFixture
    {
        [UnityTest]
        [Explicit("Local trolley access, earlier canopy greetings and its work lamp, with clear worker return routes.")]
        public IEnumerator CityPortHandlingArrival() => CaptureFocusedPort(CapturePortHandlingArrival);

        private static IEnumerator CapturePortHandlingArrival(Camera camera, CityGameRoot city,
            CityPortController port, CityPortCrew crew)
        {
            CityFishSupplySession.TryStart(true);
            var failures = new List<Exception>();
            yield return CaptureCanneryPortGreeting(city, city.Cannery, port, crew, camera, failures);
            yield return ValidatePortTrolleyApproach(camera, city, port, crew);
            yield return ValidatePortShelter(camera, city, port, crew);
            if (failures.Count > 0) throw new AggregateException("Port handling arrival contracts failed.", failures);
        }

        [UnityTest]
        [Explicit("Vessel-arrival dispatch, local loading jacks, grounded lift, clear warehouse door and driver delivery, with direct frames.")]
        public IEnumerator CityCanneryDriverDelivery()
        {
            Type setup = Type.GetType("BarPromenade.Editor.CityCanneryAssetSetup, BarPromenade.Editor");
            Assert.That(setup, Is.Not.Null);
            setup.GetMethod("ValidateOrThrow").Invoke(null, null);
            GameSessionState.BeginNewGame();
            GameSessionState.TryStartGameTimeFromWake();
            yield return SceneManager.LoadSceneAsync(SceneIds.City, LoadSceneMode.Single);
            CityGameRoot city = null;
            float deadline = Time.realtimeSinceStartup + TimeoutSeconds;
            while (Time.realtimeSinceStartup < deadline)
            {
                city = Object.FindAnyObjectByType<CityGameRoot>();
                if (city != null && city.IsInitialized && !AreaTravelService.IsComposing) break;
                yield return null;
            }
            Assert.That(city != null && city.IsInitialized, Is.True);
            GameSessionState.AdvanceGameTime((float)(12d * 60d - GameSessionState.GameTimeOfDayMinutes));
            city.DayNight.ApplyCurrentTime(true);
            CityCanneryController cannery = city.Cannery;
            Assert.That(cannery, Is.Not.Null);
            cannery.AutoAdvance = false;
            cannery.ForcePresentation = true;
            city.BusPassengers.enabled = false;
            city.Bus.enabled = false;
            CityPortController port = city.World.Root.GetComponentInChildren<CityPortController>();
            CityPortCrew crew = port.GetComponentInChildren<CityPortCrew>();
            port.AutoAdvance = false;
            port.ForcePresentation = true;
            crew.UseManualClock = true;
            city.Player.Motor.SetInputEnabled(false);
            foreach (Renderer renderer in city.Player.GameObject.GetComponentsInChildren<Renderer>(true)) renderer.enabled = false;
            Camera camera = Camera.main;
            Assert.That(camera, Is.Not.Null);
            var failures = new List<Exception>();
            DeferCanneryContract(failures, "complete randomized conversation rounds and authored replies", ValidatePortConversationRounds);
            DeferCanneryContract(failures, "expanded port dialogue localization", ValidatePortSocialLocalization);
            double load = cannery.Cycle.StageStart(CityFishSupplyStage.LoadFish);
            double firstFetch = cannery.Cycle.TransferUnitStart(CityFishSupplyStage.LoadFish, 0);
            Debug.Log($"DRIVER DELIVERY: parked={load:F3}, first fetch={firstFetch:F3}, factory road={cannery.Cycle.StageDuration(CityFishSupplyStage.FactoryToPort):F3}, port approach={cannery.Cycle.StageDuration(CityFishSupplyStage.PortArrive):F3}, reverse={cannery.Cycle.StageDuration(CityFishSupplyStage.PortReverse):F3}");
            DeferCanneryContract(failures, "hidden first dispatch timed to first stored crate", () =>
            {
                Vector3 originalHero = city.Player.GameObject.transform.position;
                try
                {
                    cannery.AutoAdvance = true;
                    cannery.ForcePresentation = false;
                    cannery.ApplyAt(0);
                    Assert.That(cannery.Truck.gameObject.activeSelf, Is.False, "No truck body before the first dock event.");
                    city.Player.Motor.Teleport(port.Plan.World(new Vector3(6, CityPortPlan.DeckHeight, -8)));
                    Assert.That(CityFishSupplySession.TryStart(port.Plan.IsAtDocks(city.Player.GameObject.transform.position)), Is.True);
                    cannery.ApplyAt(0);
                    Assert.That(cannery.Truck.gameObject.activeSelf, Is.True);
                    Assert.That(cannery.TruckPresentationActive, Is.False, "The initial truck is outside dock sight.");
                    foreach (float x in new[] { port.Plan.LandBounds.xMin, port.Plan.LandBounds.xMax })
                        foreach (float z in new[] { port.Plan.LandBounds.yMin, port.Plan.LandBounds.yMax })
                            Assert.That(cannery.TruckPresentationBounds.SqrDistance(new Vector3(x, port.Plan.QuayTopY + 1f, z)),
                                Is.GreaterThan(WorldDistancePresentation.ExitDistance * WorldDistancePresentation.ExitDistance));
                    Assert.That(cannery.Snapshot.Stage, Is.EqualTo(CityFishSupplyStage.FactoryToPort));
                    Assert.That(CityPortCycle.Sample(cannery.Snapshot.PortSeconds).Stage, Is.EqualTo(CityPortCycleStage.Approach));
                    Assert.That(cannery.Snapshot.PortStored, Is.Zero);
                    Assert.That(load, Is.EqualTo(CityFishSupplyCycle.FirstPortCrateStoredAtSeconds).Within(.001d));
                    cannery.ApplyAt(load);
                    Assert.That(cannery.Snapshot.PortStored, Is.EqualTo(1));
                    Assert.That(Vector3.Distance(cannery.Truck.position, cannery.Route.PortLoadingPose.RearAxle), Is.LessThan(.01f));
                    double lastStored = cannery.Cycle.LastPortCrateStoredAtSeconds;
                    Assert.That(firstFetch + .18d * CityFishSupplyCycle.TransferUnitDuration, Is.LessThan(lastStored),
                        "The driver fetches received cargo before the docker stores the final crate.");
                }
                finally
                {
                    city.Player.Motor.Teleport(originalHero);
                    cannery.AutoAdvance = false;
                    cannery.ForcePresentation = true;
                    cannery.ApplyAt(0);
                }
            });
            yield return CaptureCanneryPortGreeting(city, cannery, port, crew, camera, failures);
            DeferCanneryContract(failures, "crane conversation does not snap at the lever reach limit", () =>
                AssertCanneryCraneTurnContinuity(cannery, port, crew));
            DeferCanneryContract(failures, "continuous truck route", () =>
            {
                ValidateCanneryRoutes(city, cannery);
                foreach (CityFishSupplyStage stage in Enum.GetValues(typeof(CityFishSupplyStage)))
                {
                    double at = cannery.Cycle.StageStart(stage);
                    if (at == 0) at = cannery.Cycle.Duration;
                    CityPortTruckPose before = cannery.TruckPose(cannery.Cycle.Sample(at - .0001d));
                    CityPortTruckPose after = cannery.TruckPose(cannery.Cycle.Sample(at + .0001d));
                    Assert.That(Vector3.Distance(before.RearAxle, after.RearAxle), Is.LessThan(.01f), stage.ToString());
                    Assert.That(Quaternion.Angle(before.Rotation, after.Rotation), Is.LessThan(.1f), stage.ToString());
                }
            });
            DeferCanneryContract(failures, "queue arrival speaks once to the docker", () => AssertCanneryQueueWaitSpeech(city, cannery, port, crew));
            DeferCanneryContract(failures, "simultaneous port loading", () => ValidateCanneryConcurrentPortLoading(cannery, port));
            DeferCanneryContract(failures, "warehouse access clears at the actual docker exit", () =>
                ValidatePortWarehouseExitRelease(cannery, port, crew));
            DeferCanneryContract(failures, "cranes never wait for a driver pickup", () =>
            {
                for (int batch = 0; batch < 2; batch++)
                    for (double t = 0d; t < CityPortCycle.CycleDurationSeconds; t += 2d)
                    {
                        CityFishSupplySnapshot state = cannery.Cycle.Sample(cannery.Cycle.BatchStart(batch) + t);
                        Assert.That(state.PortSeconds - batch * CityPortCycle.CycleDurationSeconds,
                            Is.EqualTo(t).Within(.001d), "The dock clock must not hold a crane for truck custody, batch " + batch);
                    }
            });
            DeferCanneryContract(failures, "driver only addresses docker", CityPortDriverConversation_OnlyDockerAndObservedDeliveryWindows);
            DeferCanneryContract(failures, "local traffic reservations", () => ValidateCanneryTraffic(city, cannery));
            DeferCanneryContract(failures, "compact imported truck", () =>
            {
                cannery.ApplyAt(0);
                Bounds body = PortLocalMeshBounds(cannery.Truck);
                Assert.That(body.size.x, Is.InRange(2.38f, 2.42f));
                Assert.That(body.size.z, Is.InRange(6.35f, 6.52f));
                Assert.That(body.max.y, Is.LessThanOrEqualTo(3.23f));
                Debug.Log($"COMPACT TRUCK: bounds={body}, road={city.Layout.RoadWidth}, lane={cannery.Route.LaneCenterOffset}");
            });
            DeferCanneryContract(failures, "short lift supports complete feet and both grips", () =>
                AssertCanneryShortLiftContacts(cannery));
            DeferCanneryContract(failures, "open warehouse aperture has no floating fittings", () =>
                AssertCanneryWarehouseOpening(cannery, port));
            DeferCanneryContract(failures, "complete lift clears physical ground", () =>
                AssertCanneryLiftGroundClearance(cannery));
            DeferCanneryContract(failures, "column lift keeps its imported joints and hydraulic overlap", () =>
                AssertCanneryLiftMechanism(cannery));
            DeferCanneryContract(failures, "driver borrows and returns each local trolley", () =>
                AssertCanneryLocalTrolleys(cannery, port));

            var driver = cannery.transform.Find("Fish Delivery Driver").GetComponent<VillageResidentPresentation>();
            cannery.ApplyAt(0d);
            Transform cabDoor = CityCanneryAssetProvider.FindPart(cannery.Truck.gameObject, "MOVE_DriverDoor");
            Quaternion closedCabDoor = Quaternion.Inverse(cannery.Truck.rotation) * cabDoor.rotation;
            foreach (CityFishSupplyStage stage in new[] { CityFishSupplyStage.PortReverse,
                CityFishSupplyStage.FactoryReverse, CityFishSupplyStage.FactoryReturnReverse })
            {
                DeferCanneryContract(failures, "reverse body and alarm " + stage, () =>
                {
                    foreach (float progress in new[] { .08f, .2f, .5f, .8f, .95f })
                    {
                        cannery.ApplyAt(CanneryTime(cannery, stage, progress));
                        Assert.That(cannery.IsReversing, Is.True);
                        Assert.That(cannery.WorkerHandsMatch, Is.True, $"{stage}/{progress}: {cannery.LastCrewContactFailure}");
                        Assert.That(cannery.DriverSeatedContactsMatch, Is.True, "Feet stay at pedals, pelvis stays on seat.");
                        Assert.That(Vector3.Distance(driver.RightGrip.position, cannery.DriverSteeringContact), Is.LessThan(.025f));
                        Assert.That(Quaternion.Angle(closedCabDoor, Quaternion.Inverse(cannery.Truck.rotation) * cabDoor.rotation),
                            Is.LessThanOrEqualTo(25f), "Reversing only cracks the door open within arm's reach.");
                    }
                    cannery.ApplyAt(CanneryTime(cannery, stage, .5f));
                    Assert.That(cannery.Truck.InverseTransformPoint(driver.Head.position).x, Is.LessThan(-1.1f), "Head clears open left cabin side.");
                    Assert.That(Quaternion.Angle(closedCabDoor, Quaternion.Inverse(cannery.Truck.rotation) * cabDoor.rotation),
                        Is.InRange(20f, 25f));
                    Assert.That(Vector3.Distance(driver.LeftGrip.position, cannery.DriverDoorContact), Is.LessThan(.025f),
                        "The left hand holds the moving door while the right hand steers.");
                    Vector3 doorHand = cabDoor.InverseTransformPoint(driver.LeftGrip.position);
                    Bounds doorBounds = PortLocalMeshBounds(cabDoor);
                    Assert.That(doorHand.x, Is.InRange(doorBounds.min.x - .025f, doorBounds.max.x + .025f));
                    Assert.That(doorHand.z, Is.InRange(doorBounds.min.z, doorBounds.max.z));
                    Assert.That(doorHand.y, Is.InRange(.63f, .71f), "Grip sits on the solid upper lip below the glass.");
                    Assert.That(cabDoor.InverseTransformPoint(driver.Head.position).x, Is.GreaterThan(.13f),
                        "The head remains clear of the partly opened window and door frame.");
                    cannery.AdvanceSounds(true);
                    Assert.That(cannery.ReverseAlarmSource.isPlaying, Is.True);
                    Assert.That(cannery.ReverseAlarmSource.volume, Is.GreaterThan(0));
                    Assert.That(cannery.ReverseAlarmSource.clip.channels, Is.EqualTo(1));
                    cannery.AdvanceSounds(false);
                    Assert.That(cannery.ReverseAlarmSource.isPlaying, Is.False);
                });
            }
            cannery.ApplyAt(CanneryTime(cannery, CityFishSupplyStage.PortArrive, .5f));
            cannery.AdvanceSounds(true);
            DeferCanneryContract(failures, "beeper only in reverse", () => Assert.That(cannery.ReverseAlarmSource.isPlaying, Is.False));
            cannery.AdvanceSounds(false);

            DeferCanneryContract(failures, "two restrained arrival honks", () => AssertCanneryArrivalHorn(cannery, port));
            DeferCanneryContract(failures, "doors clear the lift before unfolding", () => AssertCanneryDoorLiftOrder(cannery));
            DeferCanneryContract(failures, "first trolley goes directly to stored cargo", () => AssertCanneryDirectFirstFetch(cannery));
            DeferCanneryContract(failures, "driver pushes the trolley forward and steps backwards only during withdrawal", () => AssertCanneryTrolleyGaitDirection(cannery));

            double pushing = firstFetch + CityFishSupplyCycle.TransferUnitDuration * .02d;
            cannery.ApplyAt(pushing);
            Vector3 pushingDriver = driver.transform.position;
            yield return CaptureCannery(camera, city, cannery, pushing, "driver-23-forward-trolley-fetch",
                port.Plan.World(new Vector3(10f, 3.4f, -10f)),
                (pushingDriver + cannery.PortTrolley.position) * .5f + Vector3.up * .7f);
            double withdrawal = firstFetch + CityFishSupplyCycle.TransferUnitDuration * .1918421d;
            cannery.ApplyAt(withdrawal);
            yield return CaptureCannery(camera, city, cannery, withdrawal, "driver-24-short-pallet-withdrawal",
                port.Plan.World(new Vector3(-3.7f, 3.2f, -18.2f)),
                (driver.transform.position + cannery.PortTrolley.position) * .5f + Vector3.up * .7f);

            yield return CaptureCanneryBusPassing(city, cannery, camera, failures);

            double shortLift = TransferTime(cannery, CityFishSupplyStage.LoadFish, 0, .41f);
            cannery.ApplyAt(shortLift);
            yield return CaptureCannery(camera, city, cannery, shortLift, "driver-06-short-lift",
                cannery.Truck.TransformPoint(new Vector3(-3.4f, 1.9f, -5.2f)),
                cannery.TailLift.position-cannery.Truck.forward*1.25f+Vector3.up*.65f);

            foreach (var liftShot in new[] {
                (time: load + 2d, name: "driver-18-doors-before-lift"),
                (time: load + 5.5d, name: "driver-19-unfold-after-doors"),
                (time: TransferTime(cannery, CityFishSupplyStage.LoadFish, 0, .35f), name: "driver-13-column-lift-low"),
                (time: TransferTime(cannery, CityFishSupplyStage.LoadFish, 0, .41f), name: "driver-14-column-lift-mid"),
                (time: TransferTime(cannery, CityFishSupplyStage.LoadFish, 0, .47f), name: "driver-15-column-lift-high"),
                (time: load, name: "driver-16-column-lift-folded") })
            {
                cannery.ApplyAt(liftShot.time);
                yield return CaptureCannery(camera, city, cannery, liftShot.time, liftShot.name,
                    cannery.Truck.TransformPoint(new Vector3(-3.4f, .5f, -4.9f)),
                    cannery.Truck.TransformPoint(new Vector3(-.45f, .9f, -2.05f)));
            }

            cannery.ApplyAt(load + 8d);
            yield return CaptureCannery(camera, city, cannery, load + 8d, "driver-17-column-hydraulics",
                cannery.Truck.TransformPoint(new Vector3(-3.6f, 1.5f, -2.8f)),
                cannery.Truck.TransformPoint(new Vector3(-1.1f, 1.45f, -1.86f)));

            foreach (var shot in new[] {
                (stage: CityFishSupplyStage.FactoryToPort, phase: .02f, name: "driver-00-initial-road-entry", offset: new Vector3(-6, 2.1f, 8)),
                (stage: CityFishSupplyStage.PortArrive, phase: .75f, name: "driver-01-forward-arrival", offset: new Vector3(-7, 3, 9)),
                (stage: CityFishSupplyStage.PortReverse, phase: .5f, name: "driver-02-reversing-cab", offset: new Vector3(-4, 2.15f, 2.8f)),
                (stage: CityFishSupplyStage.PortReverse, phase: .5f, name: "driver-12-ajar-door-grip", offset: new Vector3(-3.3f, 2.7f, 1f)),
                (stage: CityFishSupplyStage.PortReverse, phase: .82f, name: "driver-03-reversing-yard", offset: new Vector3(-7, 3.5f, -8)) })
            {
                double at = CanneryTime(cannery, shot.stage, shot.phase);
                cannery.ApplyAt(at);
                Vector3 from = cannery.Truck.TransformPoint(shot.offset);
                Vector3 target = cannery.Truck.TransformPoint(new Vector3(-.65f, 1.8f, shot.stage == CityFishSupplyStage.PortReverse ? 2.9f : 1.2f));
                yield return CaptureCannery(camera, city, cannery, at, shot.name, from, target);
            }
            crew.ApplyAt(port.ElapsedSeconds);
            CityPortTruckPose parked = cannery.Route.PortLoadingPose;
            yield return CaptureCannery(camera, city, cannery, firstFetch + 18d,
                "driver-04-concurrent-loading", parked.RearAxle + parked.Rotation * new Vector3(-8, 3f, -7),
                port.Plan.World(new Vector3(0, 1f, -14f)));
            double queueWait = cannery.Cycle.StageStart(CityFishSupplyStage.LoadFish, 1) +
                CityFishSupplyCycle.TrolleyQueueArrivalDuration + 1d;
            yield return CaptureCannery(camera, city, cannery, queueWait, "driver-20-wait-at-store-entrance",
                port.Plan.World(new Vector3(10f, 3.6f, -9f)), port.Plan.World(new Vector3(3f, 2.2f, -14.5f)));
            yield return CaptureCannery(camera, city, cannery, 0d, "driver-07-port-trolley-parked",
                port.Plan.World(new Vector3(11f, 3f, -10f)), port.Plan.World(new Vector3(4f, 2.3f, -14f)));
            double portReturn = load + cannery.Cycle.StageDuration(CityFishSupplyStage.LoadFish) - 8d;
            yield return CaptureCannery(camera, city, cannery, portReturn, "driver-08-open-store-returned-trolley",
                port.Plan.World(new Vector3(5.4f, 3f, -10.7f)), port.Plan.World(new Vector3(3.6f, 2.7f, -14.4f)));
            yield return CaptureCannery(camera, city, cannery, 0d, "driver-09-factory-trolley-parked",
                cannery.Plan.World(new Vector3(8f, 2.8f, -8f)), cannery.FactoryTrolley.position + Vector3.up * .8f);
            double lowLift = TransferTime(cannery, CityFishSupplyStage.LoadFish, 0, .94f);
            cannery.ApplyAt(lowLift);
            yield return CaptureCannery(camera, city, cannery, lowLift, "driver-10-lift-above-ground",
                cannery.Truck.TransformPoint(new Vector3(-3.6f, .65f, -5.1f)),
                cannery.TailLift.position - cannery.Truck.forward * 1.1f);
            double curb = cannery.Cycle.StageStart(CityFishSupplyStage.UnloadShop) + 18d;
            cannery.ApplyAt(curb);
            yield return CaptureCannery(camera, city, cannery, curb, "driver-11-shop-curb-hands",
                driver.transform.position - cannery.Truck.right * 3.5f + cannery.Truck.forward * 2f + Vector3.up * 1.6f,
                (driver.transform.position + cannery.ActiveTrolley.position) * .5f + Vector3.up * .8f);
            yield return ValidatePortShelter(camera, city, port, crew);
            yield return ValidatePortTrolleyApproach(camera, city, port, crew);
            yield return ValidatePortHornAndSearchlight(camera, city, port);
            if (failures.Count > 0) throw new AggregateException("Driver delivery contracts failed; focused frames retained.", failures);
        }

        private static IEnumerator CaptureCanneryPortGreeting(CityGameRoot city, CityCanneryController cannery,
            CityPortController port, CityPortCrew crew, Camera camera, ICollection<Exception> failures)
        {
            CityPortConversationController speech = port.GetComponentInChildren<CityPortConversationController>(true);
            Vector3 savedHero = city.Player.GameObject.transform.position;
            double saved = cannery.WorkingSeconds;
            double firstGreeting = -1d;
            float firstDistance = float.PositiveInfinity;
            bool earlyWave = false;
            double closeAt = CityPortCycle.ApproachDurationSeconds * CityPortConversationSchedule.GreetingApproachProgress;
            try
            {
                city.Player.Motor.Teleport(port.Plan.World(new Vector3(0f, CityPortPlan.DeckHeight, -5f)));
                for (double time = 0d; time <= CityPortCycle.UnloadStartSeconds; time += .2d)
                {
                    cannery.ApplyAt(time);
                    crew.ApplyAt(port.ElapsedSeconds, time);
                    speech.ApplyAt();
                    if (time < closeAt)
                        for (int role = 0; role < 5; role++) earlyWave |= crew.GetGesture(role).IsWaving;
                    var turn = speech.Schedule.Current;
                    if (firstGreeting >= 0d || !turn.IsSpeaking || turn.Exchange.Kind != CityPortConversationKind.Greeting) continue;
                    firstGreeting = time;
                    firstDistance = Vector3.ProjectOnPlane(port.Vessel.position - port.Plan.World(port.Plan.VesselBerthLocal), Vector3.up).magnitude;
                    yield return CaptureCannery(camera, city, cannery, time, "driver-21-close-ship-greeting",
                        port.Plan.World(new Vector3(-6f, 5.5f, -10f)), port.Vessel.position + Vector3.up * 2f);
                    yield return CaptureCannery(camera, city, cannery, time, "driver-25-earlier-canopy-greeting",
                        port.Plan.World(new Vector3(13.3f, 3.2f, -2.5f)), port.Plan.World(new Vector3(9.5f, 2.7f, -5.7f)));
                    city.Player.Motor.Teleport(port.Plan.World(new Vector3(0f, CityPortPlan.DeckHeight, -5f)));
                }
                DeferCanneryContract(failures, "greet the ship only on its final close approach", () =>
                {
                    Assert.That(firstGreeting, Is.InRange(closeAt, CityPortCycle.ApproachDurationSeconds * .9d + .001d),
                        "The canopy group greets during the newly earlier window, before the old close-arrival threshold.");
                    Assert.That(firstDistance, Is.LessThan(9f), "Measure the actual hull against its berth, not only the timeline.");
                    Assert.That(earlyWave, Is.False, "Hands must not greet before the close-approach speech window.");
                    Debug.Log($"PORT GREETING: first line={firstGreeting:F3}, berth distance={firstDistance:F3}, window={closeAt:F3}");
                    var restored = new CityPortConversationSchedule(3197);
                    for (double time = closeAt + 1d; time < CityPortCycle.UnloadStartSeconds; time += .2d)
                    {
                        var turn = restored.Advance(time, time, CityPortCycle.Sample(time), 31, 31, 0, uint.MaxValue, 0, true);
                        Assert.That(turn.HasExchange && turn.Exchange.Kind == CityPortConversationKind.Greeting, Is.False,
                            "Rebuilding after close approach must not replay a missed greeting.");
                    }
                });
                double runAt = CityPortCycle.ApproachDurationSeconds + CityPortCycle.MoorDurationSeconds +
                    CityPortCycle.PrepareDurationSeconds * .5d;
                DeferCanneryContract(failures, "quay worker runs with the hero clip after mooring", () =>
                {
                    var heroRig = city.Player.GameObject.GetComponentInChildren<Player3DAssetRegistry>(true);
                    Assert.That(heroRig.TryGetAnimation("Run", out Player3DAnimationBinding run), Is.True);
                    cannery.ApplyAt(runAt - .01d);
                    crew.ApplyAt(port.ElapsedSeconds, runAt - .01d);
                    Vector3 before = crew.ShoreWorker.transform.position;
                    float cycleBefore = crew.ShoreWorker.CurrentLocomotionCycle;
                    cannery.ApplyAt(runAt + .01d);
                    crew.ApplyAt(port.ElapsedSeconds, runAt + .01d);
                    float measuredSpeed = Vector3.Distance(before, crew.ShoreWorker.transform.position) / .02f;
                    Assert.That(crew.ShoreWorker.CurrentRunWeight, Is.GreaterThan(.99f));
                    Assert.That(crew.ShoreWorker.FreeRunClip, Is.SameAs(run.Clip), "Reuse the exact Shift-run asset from the hero rig.");
                    Assert.That(crew.ShoreWorker.CurrentLocomotionSpeed, Is.EqualTo(measuredSpeed).Within(.03f));
                    float playbackRate = (crew.ShoreWorker.CurrentLocomotionCycle - cycleBefore) / .02f * run.Clip.length;
                    Assert.That(playbackRate, Is.InRange(.8f, 1.05f), "Use a running stride, not an accelerated walking or running clip.");
                    AssertCanneryDockerRunBones(crew.ShoreWorker);
                    cannery.ApplyAt(CityPortCycle.UnloadStartSeconds);
                    crew.ApplyAt(port.ElapsedSeconds, CityPortCycle.UnloadStartSeconds);
                    Assert.That(crew.ShoreWorker.CurrentRunWeight, Is.Zero, "The run layer must release when the docker takes the trolley.");
                    Assert.That(crew.TrolleyHandsMatch, Is.True);
                });
                cannery.ApplyAt(runAt);
                crew.ApplyAt(port.ElapsedSeconds, runAt);
                Vector3 runner = crew.ShoreWorker.transform.position;
                yield return CaptureCannery(camera, city, cannery, runAt, "driver-22-docker-runs-after-mooring",
                    runner + crew.ShoreWorker.transform.right * 3.4f + Vector3.up * 1.5f,
                    runner + Vector3.up * .9f);
            }
            finally
            {
                city.Player.Motor.Teleport(savedHero);
                cannery.ApplyAt(saved);
                crew.ApplyAt(port.ElapsedSeconds, saved);
                speech.ApplyAt();
            }
        }

        private static void AssertCanneryDockerRunBones(VillageResidentPresentation actor)
        {
            float savedSpeed = actor.CurrentLocomotionSpeed, savedCycle = actor.CurrentLocomotionCycle;
            Transform Bone(string name) => CityPedestrianHandProps.FindSocket(actor.ModelRoot, name);
            float Bend(string upper, string middle, string end) => Vector3.Angle(
                Bone(upper).position - Bone(middle).position, Bone(end).position - Bone(middle).position);
            try
            {
                actor.ApplyFreeLocomotion(3.9f, .125f);
                Vector3 pelvis = Bone("pelvis").localPosition;
                actor.ApplyFreeLocomotion(3.9f, .125f);
                Assert.That(Vector3.Distance(pelvis, Bone("pelvis").localPosition), Is.LessThan(.0001f),
                    "Repeated pose evaluation must not accumulate a running pelvis offset.");
                Quaternion left = Bone("thigh.L").localRotation, right = Bone("thigh.R").localRotation;
                for (int phase = 0; phase < 2; ++phase)
                {
                    actor.ApplyFreeLocomotion(3.9f, phase == 0 ? .125f : .625f);
                    Assert.That(Bend("upper_arm.L", "forearm.L", "hand.L"), Is.LessThan(155f),
                        "The actual left elbow must bend in Run; a mixer weight cannot prove clip binding.");
                    Assert.That(Bend("upper_arm.R", "forearm.R", "hand.R"), Is.LessThan(155f),
                        "The actual right elbow must bend in Run.");
                    Assert.That(Mathf.Min(Bend("thigh.L", "shin.L", "foot.L"),
                        Bend("thigh.R", "shin.R", "foot.R")), Is.LessThan(145f),
                        "At least one knee must fold through the running step.");
                }
                Assert.That(Quaternion.Angle(left, Bone("thigh.L").localRotation), Is.GreaterThan(35f),
                    "The left leg must actually change lead between opposite Run phases.");
                Assert.That(Quaternion.Angle(right, Bone("thigh.R").localRotation), Is.GreaterThan(35f),
                    "The right leg must actually change lead between opposite Run phases.");
            }
            finally { actor.ApplyFreeLocomotion(savedSpeed, savedCycle); }
        }

        private static void AssertCanneryCraneTurnContinuity(CityCanneryController cannery, CityPortController port, CityPortCrew crew)
        {
            double saved = cannery.WorkingSeconds;
            float maximumRetreat = 0f, maximumTurn = 0f;
            try
            {
                for (int cargo = 0; cargo < 2; cargo++)
                {
                    double start = CityPortCycle.UnloadStartSeconds + cargo * CityPortCycle.CargoDurationSeconds;
                    cannery.ApplyAt(start);
                    crew.ApplyAt(port.ElapsedSeconds, start);
                    var previous = new float[2];
                    for (int sample = 1; sample <= 400; sample++)
                    {
                        double time = start + sample * .05d;
                        cannery.ApplyAt(time);
                        crew.SetSpeech(2, 3, true, false);
                        crew.SetSpeech(3, 2, false, false);
                        crew.ApplyAt(port.ElapsedSeconds, time);
                        Assert.That(crew.CraneHandsMatch, Is.True, "Both moving lever grips must survive the conversation turn.");
                        for (int role = 2; role <= 3; role++)
                        {
                            var worker = crew.GetWorker(role);
                            Transform right = CityPedestrianHandProps.FindSocket(worker.ModelRoot, "upper_arm.R");
                            Transform left = CityPedestrianHandProps.FindSocket(worker.ModelRoot, "upper_arm.L");
                            Vector3 forward = Vector3.Cross(right.position - left.position, worker.transform.up);
                            float yaw = Mathf.Abs(Vector3.SignedAngle(worker.transform.forward, forward, worker.transform.up));
                            maximumTurn = Mathf.Max(maximumTurn, yaw);
                            if (sample > 30) maximumRetreat = Mathf.Max(maximumRetreat, previous[role - 2] - yaw);
                            previous[role - 2] = yaw;
                        }
                    }
                }
                Assert.That(maximumTurn, Is.GreaterThan(2f), "Exercise an actual planted conversation turn.");
                Assert.That(maximumRetreat, Is.LessThan(1.5f), "A stationary conversation target must not make the torso repeatedly snap back from the reach limit.");
                Debug.Log($"CRANE CONVERSATION: max torso retreat per frame={maximumRetreat:F3}, observed turn={maximumTurn:F3}");
            }
            finally
            {
                crew.SetSpeech(2, -1, false, false);
                crew.SetSpeech(3, -1, false, false);
                cannery.ApplyAt(saved);
                crew.ApplyAt(port.ElapsedSeconds, saved);
            }
        }

        private static void AssertCanneryDoorLiftOrder(CityCanneryController cannery)
        {
            Transform left = CityCanneryAssetProvider.FindPart(cannery.Truck.gameObject, "MOVE_TruckRearDoorLeft");
            Transform right = CityCanneryAssetProvider.FindPart(cannery.Truck.gameObject, "MOVE_TruckRearDoorRight");
            foreach (CityFishSupplyStage stage in new[] { CityFishSupplyStage.LoadFish, CityFishSupplyStage.UnloadFish,
                CityFishSupplyStage.LoadFinished, CityFishSupplyStage.UnloadShop })
            {
                double start = cannery.Cycle.StageStart(stage), end = start + cannery.Cycle.StageDuration(stage);
                cannery.ApplyAt(start);
                Quaternion closedLeft = left.rotation, closedRight = right.rotation, folded = cannery.TailLift.rotation;
                foreach (double time in new[] { start + 2d, end - 2d })
                {
                    cannery.ApplyAt(time);
                    Assert.That(Quaternion.Angle(left.rotation, closedLeft), Is.InRange(1f, 109f));
                    Assert.That(Quaternion.Angle(cannery.TailLift.rotation, folded), Is.LessThan(.01f), stage + " lift must stay folded while doors move");
                    Assert.That(cannery.CurrentLiftTravel, Is.EqualTo(0f).Within(.001f));
                }
                foreach (double time in new[] { start + 5.5d, end - 5.5d })
                {
                    cannery.ApplyAt(time);
                    Assert.That(Quaternion.Angle(left.rotation, closedLeft), Is.EqualTo(110f).Within(.02f));
                    Assert.That(Quaternion.Angle(right.rotation, closedRight), Is.EqualTo(110f).Within(.02f));
                    Assert.That(Quaternion.Angle(cannery.TailLift.rotation, folded), Is.InRange(5f, 85f), stage + " folding needs open doors");
                    Assert.That(cannery.CurrentLiftTravel, Is.EqualTo(0f).Within(.001f), stage + " fold only at body height");
                }
                cannery.ApplyAt(start + 9d);
                Assert.That(Quaternion.Angle(cannery.TailLift.rotation, folded), Is.EqualTo(90f).Within(.02f));
                Assert.That(cannery.CurrentLiftTravel, Is.LessThan(-.01f), stage + " descent follows unfolding");
            }
        }

        private static void AssertCanneryDirectFirstFetch(CityCanneryController cannery)
        {
            double first = cannery.Cycle.TransferUnitStart(CityFishSupplyStage.LoadFish, 0);
            cannery.ApplyAt(first);
            Assert.That(Vector3.Distance(cannery.PortTrolley.position, cannery.PortTrolleyQueuePosition), Is.LessThan(.002f),
                "The first fetch continues from the entrance queue directly to cargo.");
            MeshFilter deck = CityCanneryAssetProvider.FindPart(cannery.TailLift.gameObject,
                "TailLiftVisible__Deck").GetComponent<MeshFilter>();
            Bounds deckBounds = new Bounds(cannery.Truck.InverseTransformPoint(deck.transform.TransformPoint(deck.sharedMesh.vertices[0])), Vector3.zero);
            foreach (Vector3 vertex in deck.sharedMesh.vertices)
                deckBounds.Encapsulate(cannery.Truck.InverseTransformPoint(deck.transform.TransformPoint(vertex)));
            MeshFilter[] cartMeshes = cannery.PortTrolley.GetComponentsInChildren<MeshFilter>(true);
            for (float phase = 0f; phase < .18f; phase += .01f)
            {
                cannery.ApplyAt(first + phase * CityFishSupplyCycle.TransferUnitDuration);
                Bounds cartBounds = new Bounds(cannery.Truck.InverseTransformPoint(cannery.PortTrolley.position), Vector3.zero);
                foreach (MeshFilter mesh in cartMeshes)
                    foreach (Vector3 vertex in mesh.sharedMesh.vertices)
                        cartBounds.Encapsulate(cannery.Truck.InverseTransformPoint(mesh.transform.TransformPoint(vertex)));
                float clearance = Mathf.Max(Mathf.Max(deckBounds.min.z - cartBounds.max.z, cartBounds.min.z - deckBounds.max.z),
                    Mathf.Max(deckBounds.min.x - cartBounds.max.x, cartBounds.min.x - deckBounds.max.x));
                Assert.That(clearance, Is.GreaterThan(.02f),
                    "The complete empty trolley, including its forks, must clear the lowered deck during the first fetch.");
                Assert.That(cannery.WorkerHandsMatch, Is.True, cannery.LastCrewContactFailure);
            }
        }

        private static void AssertCanneryTrolleyGaitDirection(CityCanneryController cannery)
        {
            var driver = cannery.transform.Find("Fish Delivery Driver").GetComponent<VillageResidentPresentation>();
            double start = cannery.Cycle.TransferUnitStart(CityFishSupplyStage.LoadFish, 0);
            foreach (float phase in new[] { .05f, .09f, .13f, .20f, .29f })
            {
                double at = start + CityFishSupplyCycle.TransferUnitDuration * phase;
                cannery.ApplyAt(at - .005d);
                Vector3 before = driver.transform.position;
                float gaitBefore = driver.CurrentActionSeconds;
                cannery.ApplyAt(at + .005d);
                Vector3 velocity = Vector3.ProjectOnPlane(driver.transform.position - before, Vector3.up);
                Assert.That(velocity.magnitude, Is.GreaterThan(.001f), "Sample a travelling part of the route.");
                float facing = Vector3.Dot(velocity.normalized, driver.transform.forward);
                bool withdrawal = phase > .18f && phase < .235f;
                Assert.That(withdrawal ? -facing : facing, Is.GreaterThan(.35f),
                    "The empty fetch and loaded approach must be pushed forwards; only the pallet withdrawal reverses.");
                Assert.That(cannery.DriverTrolleyMovingBackward, Is.EqualTo(withdrawal));
                float length = driver.ClipLength(VillageResidentAction.Walk);
                float gaitAdvance = Mathf.DeltaAngle(gaitBefore / length * 360f, driver.CurrentActionSeconds / length * 360f);
                Assert.That(withdrawal ? -gaitAdvance : gaitAdvance, Is.GreaterThan(0f), "Foot animation must agree with actual travel.");
                Assert.That(cannery.WorkerHandsMatch, Is.True, cannery.LastCrewContactFailure);
            }
        }

        private static void AssertCanneryQueueWaitSpeech(CityGameRoot city, CityCanneryController cannery,
            CityPortController port, CityPortCrew crew)
        {
            CityPortConversationController conversation = port.GetComponentInChildren<CityPortConversationController>(true);
            Assert.That(conversation, Is.Not.Null);
            Vector3 savedHero = city.Player.GameObject.transform.position;
            double saved = cannery.WorkingSeconds;
            try
            {
                city.Player.Motor.Teleport(cannery.PortTrolleyQueuePosition + new Vector3(2f, 0f, 3f));
                double start = cannery.Cycle.StageStart(CityFishSupplyStage.LoadFish, 1);
                double fetch = cannery.Cycle.TransferUnitStart(CityFishSupplyStage.LoadFish, 0, 1);
                int before = conversation.AccessWaitLinesPlayed;
                bool observed = false;
                for (double time = start; time <= fetch; time += .25d)
                {
                    cannery.ApplyAt(time);
                    crew.ApplyAt(port.ElapsedSeconds, time);
                    conversation.ApplyAt();
                    if (!cannery.DriverWaitingForDockWorker) continue;
                    Assert.That(Vector3.Distance(cannery.PortTrolley.position, cannery.PortTrolleyQueuePosition), Is.LessThan(.002f));
                    Assert.That(cannery.WorkerHandsMatch, Is.True, cannery.LastCrewContactFailure);
                    if (observed) continue;
                    observed = true;
                    Assert.That(conversation.AccessWaitLinesPlayed, Is.EqualTo(before + 1));
                    Assert.That(conversation.LastLineKey, Is.EqualTo(CityPortConversationController.AccessWaitLineKey));
                    Assert.That(conversation.LastSpeakerRole, Is.EqualTo(CityPortConversationCatalog.DriverRole));
                    Assert.That(conversation.Schedule.Current.IsSpeaking, Is.False, "The wait cue shares the existing speech channel.");
                }
                Assert.That(observed, Is.True, "A recurring visit must exercise the occupied warehouse entrance.");
                Assert.That(conversation.AccessWaitLinesPlayed, Is.EqualTo(before + 1), "Do not repeat the line throughout a wait.");
                double waiting = start + CityFishSupplyCycle.TrolleyQueueArrivalDuration + 1d;
                cannery.ApplyAt(waiting);
                crew.ApplyAt(port.ElapsedSeconds, waiting);
                conversation.ApplyAt();
                Assert.That(conversation.AccessWaitLinesPlayed, Is.EqualTo(before + 1), "Reconstruction must not replay the cue.");
            }
            finally
            {
                city.Player.Motor.Teleport(savedHero);
                cannery.ApplyAt(saved);
                crew.ApplyAt(port.ElapsedSeconds, saved);
                conversation.ApplyAt();
            }
        }

        private static void AssertCanneryArrivalHorn(CityCanneryController cannery, CityPortController port)
        {
            bool automatic = cannery.AutoAdvance;
            try
            {
                double trigger = cannery.Cycle.StageStart(CityFishSupplyStage.PortArrive) +
                    cannery.Cycle.StageDuration(CityFishSupplyStage.PortArrive) - 1.2d;
                cannery.AutoAdvance = false;
                cannery.ApplyAt(trigger - 1d);
                int before = cannery.ArrivalHornPairsPlayed;
                cannery.AutoAdvance = true;
                cannery.ApplyAt(trigger - .02d);
                cannery.ApplyAt(trigger + .02d);
                AudioSource horn = cannery.ArrivalHornSource;
                Assert.That(horn.isPlaying, Is.True);
                Assert.That(cannery.ArrivalHornPairsPlayed, Is.EqualTo(before + 1));
                Assert.That(horn.loop, Is.False);
                Assert.That(horn.clip.channels, Is.EqualTo(1));
                Assert.That(horn.spatialBlend, Is.EqualTo(1f));
                Assert.That(horn.volume, Is.InRange(.2f, .32f));
                Assert.That(horn.outputAudioMixerGroup, Is.EqualTo(cannery.TruckEngineSource.outputAudioMixerGroup));
                Assert.That(Vector3.Distance(horn.transform.position,
                    CityCanneryAssetProvider.FindPart(cannery.Truck.gameObject, "ANCHOR_TruckEngine").position), Is.LessThan(.001f));
                var pcm = new float[horn.clip.samples];
                horn.clip.GetData(pcm, 0);
                float Rms(float start, float end)
                {
                    int first = (int)(start * horn.clip.frequency), last = (int)(end * horn.clip.frequency);
                    double sum = 0;
                    for (int i = first; i < last; i++) sum += pcm[i] * pcm[i];
                    return (float)Math.Sqrt(sum / (last - first));
                }
                Assert.That(Rms(.04f, .20f), Is.GreaterThan(.12f), "First short honk.");
                Assert.That(Rms(.28f, .39f), Is.LessThan(.00001f), "A real pause separates the two honks.");
                Assert.That(Rms(.46f, .65f), Is.GreaterThan(.12f), "Second short honk.");
                for (int crane = 0; crane < 2; crane++)
                {
                    Vector3 listener = port.Plan.World(port.Plan.CraneBaseLocal(crane)) + Vector3.up * 1.5f;
                    float distance = Vector3.Distance(listener, horn.transform.position);
                    float gain = horn.volume * Mathf.Clamp01((horn.maxDistance - distance) / (horn.maxDistance - horn.minDistance));
                    Assert.That(gain, Is.GreaterThan(.10f), "The quiet horn still reaches crane " + crane);
                }
                cannery.AdvanceSounds(true);
                Assert.That(cannery.ArrivalHornPairsPlayed, Is.EqualTo(before + 1), "Repeated samples do not repeat the pair.");
                cannery.AdvanceSounds(false);
                Assert.That(horn.isPlaying, Is.False, "Pause owns the horn.");
                cannery.AdvanceSounds(true);
                Assert.That(horn.isPlaying, Is.True);
                cannery.ApplyAt(trigger + 5d);
                Assert.That(horn.isPlaying, Is.False, "A clock seek does not catch up a horn.");
                cannery.ApplyAt(trigger - .02d);
                cannery.ApplyAt(trigger + .02d);
                Assert.That(cannery.ArrivalHornPairsPlayed, Is.EqualTo(before + 1), "A reconstructed arrival never replays its pair.");
            }
            finally
            {
                cannery.AdvanceSounds(false);
                cannery.AutoAdvance = automatic;
            }
        }

        private static void AssertCanneryLiftMechanism(CityCanneryController cannery)
        {
            double saved = cannery.WorkingSeconds;
            var parts = new Dictionary<string, Transform>();
            Transform Part(string name)
            {
                if (!parts.TryGetValue(name, out Transform part))
                    parts[name] = part = CityCanneryAssetProvider.FindPart(cannery.Truck.gameObject, name);
                return part;
            }
            var moving = new List<Transform> { Part("MOVE_LiftCarriage") };
            foreach (string side in new[] { "Left", "Right" })
            {
                moving.Add(Part("MOVE_LiftRam" + side));
                moving.Add(Part("MOVE_LiftFoldBarrel" + side));
                moving.Add(Part("MOVE_LiftFoldRod" + side));
            }
            var scales = new Dictionary<Transform, Vector3>();
            var meshes = new HashSet<MeshFilter>();
            foreach (Transform part in moving)
            {
                scales[part] = part.lossyScale;
                foreach (MeshFilter mesh in part.GetComponentsInChildren<MeshFilter>(true))
                    if (mesh.sharedMesh != null && mesh.GetComponent<Renderer>() != null &&
                        !mesh.name.StartsWith("COL_", StringComparison.Ordinal)) meshes.Add(mesh);
            }
            Assert.That(meshes.Count, Is.GreaterThanOrEqualTo(7), "Measure the imported mechanism, including both hydraulic pairs.");
            float lowestCarriage = float.PositiveInfinity, highestCarriage = float.NegativeInfinity;
            try
            {
                foreach (CityFishSupplyStage stage in new[] { CityFishSupplyStage.LoadFish, CityFishSupplyStage.UnloadFish,
                    CityFishSupplyStage.LoadFinished, CityFishSupplyStage.UnloadShop })
                {
                    double start = cannery.Cycle.StageStart(stage), end = start + cannery.Cycle.StageDuration(stage);
                    var times = new List<double>();
                    foreach (double edge in new[] { 0d, 2d, 4d, 6d, 8d, 12d, CityFishSupplyCycle.TransferEdgeDuration })
                    { times.Add(start + edge); times.Add(end - Math.Max(.001d, edge)); }
                    foreach (float phase in new[] { 0f, .08f, .12f, .28f, .35f, .41f, .47f, .55f, .62f, .75f, .85f, .94f })
                        times.Add(TransferTime(cannery, stage, 0, phase));
                    foreach (double time in times)
                    {
                        cannery.ApplyAt(time);
                        string context = $"{stage}/{time - start:F3}s";
                        Transform carriage = moving[0];
                        Vector3 carriageLocal = cannery.Truck.InverseTransformPoint(carriage.position);
                        lowestCarriage = Mathf.Min(lowestCarriage, carriageLocal.y);
                        highestCarriage = Mathf.Max(highestCarriage, carriageLocal.y);
                        Assert.That(carriageLocal.x, Is.EqualTo(0f).Within(.003f), context + " carriage lateral drift");
                        Assert.That(carriageLocal.z, Is.EqualTo(-1.88f).Within(.003f), context + " carriage leaves its columns");
                        foreach (Transform part in moving)
                            Assert.That(Vector3.Distance(part.lossyScale, scales[part]), Is.LessThan(.0001f),
                                context + " hydraulic parts must translate/rotate without stretching: " + part.name);
                        foreach (string side in new[] { "Left", "Right" })
                        {
                            Vector3 hinge = Part("ANCHOR_LiftHinge" + side).position;
                            Assert.That(Vector3.Distance(hinge, Part("ANCHOR_LiftPlatformHinge" + side).position),
                                Is.LessThan(.003f), context + " disconnected platform hinge " + side);
                            Vector3 railBottom = Part("ANCHOR_LiftRailBottom" + side).position;
                            Vector3 railTop = Part("ANCHOR_LiftRailTop" + side).position;
                            Vector3 rail = (railTop - railBottom).normalized;
                            foreach (string guide in new[] { "Lower", "Upper" })
                            {
                                Vector3 delta = Part("ANCHOR_LiftGuide" + guide + side).position - railBottom;
                                float along = Vector3.Dot(delta, rail);
                                Assert.That((delta - rail * along).magnitude, Is.LessThan(.003f), context + " guide leaves rail " + guide + side);
                                Assert.That(along, Is.InRange(.023f, Vector3.Distance(railBottom, railTop) - .023f),
                                    context + " complete guide roller remains between rail ends " + guide + side);
                            }
                            Vector3 ramBase = Part("ANCHOR_LiftRamBase" + side).position;
                            Transform ram = Part("MOVE_LiftRam" + side);
                            Vector3 ramTop = Part("ANCHOR_LiftRamTop" + side).position;
                            Vector3 bottom = Part("ANCHOR_LiftCylinderBottom" + side).position;
                            Vector3 top = Part("ANCHOR_LiftCylinderTop" + side).position;
                            Vector3 axis = (top - bottom).normalized;
                            Assert.That(Vector3.Distance(ram.position, ramBase), Is.LessThan(.003f), context + " ram detached from carriage " + side);
                            Assert.That(Vector3.Distance(ramBase, ramTop), Is.EqualTo(1.24f).Within(.003f), context + " imported ram length " + side);
                            float insertion = Vector3.Dot(ramTop - bottom, axis);
                            Assert.That((ramTop - bottom - axis * insertion).magnitude, Is.LessThan(.003f), context + " ram axis misses cylinder " + side);
                            Assert.That(insertion, Is.InRange(-.002f, Vector3.Distance(bottom, top) + .002f), context + " ram tip exits cylinder " + side);
                            Assert.That(Vector3.Dot(ramBase - bottom, axis), Is.LessThanOrEqualTo(.002f), context + " cylinder mouth loses its rod " + side);
                            Vector3 foldBase = Part("ANCHOR_LiftFoldBase" + side).position;
                            Vector3 foldTip = Part("ANCHOR_LiftFoldTip" + side).position;
                            Vector3 mouth = Part("ANCHOR_LiftFoldBarrelMouth" + side).position;
                            Vector3 rodEnd = Part("ANCHOR_LiftFoldRodEnd" + side).position;
                            Vector3 foldAxis = (foldTip - foldBase).normalized;
                            Assert.That(Vector3.Distance(Part("MOVE_LiftFoldBarrel" + side).position, foldBase), Is.LessThan(.003f), context + " fold barrel joint " + side);
                            Assert.That(Vector3.Distance(Part("MOVE_LiftFoldRod" + side).position, foldTip), Is.LessThan(.003f), context + " fold rod joint " + side);
                            Assert.That(Vector3.Distance(mouth, foldBase), Is.EqualTo(.5f).Within(.003f), context + " fixed fold barrel length " + side);
                            Assert.That(Vector3.Distance(foldTip, rodEnd), Is.EqualTo(.5f).Within(.003f), context + " fixed fold rod length " + side);
                            Assert.That(Vector3.Distance(mouth, foldBase + foldAxis * .5f), Is.LessThan(.003f), context + " fold barrel misses tip " + side);
                            float rodInsertion = Vector3.Dot(rodEnd - foldBase, foldAxis);
                            Assert.That((rodEnd - foldBase - foldAxis * rodInsertion).magnitude, Is.LessThan(.003f), context + " fold rod axis " + side);
                            Assert.That(rodInsertion, Is.InRange(.048f, .502f), context + " fold piston loses cylinder overlap " + side);
                        }
                        Physics.SyncTransforms();
                        foreach (MeshFilter mesh in meshes)
                        {
                            Vector3 lowest = Vector3.positiveInfinity;
                            foreach (Vector3 vertex in mesh.sharedMesh.vertices)
                            {
                                Vector3 point = mesh.transform.TransformPoint(vertex);
                                if (point.y < lowest.y) lowest = point;
                            }
                            float ground = float.NegativeInfinity;
                            foreach (RaycastHit hit in Physics.RaycastAll(new Vector3(lowest.x, cannery.Truck.position.y + 2f, lowest.z),
                                Vector3.down, 10f, ~0, QueryTriggerInteraction.Ignore))
                                if (!hit.transform.IsChildOf(cannery.transform) && hit.normal.y > .65f &&
                                    hit.point.y <= cannery.Truck.position.y + .5f) ground = Mathf.Max(ground, hit.point.y);
                            Assert.That(float.IsNegativeInfinity(ground), Is.False, context + " mechanism needs physical paving below " + mesh.name);
                            Assert.That(lowest.y, Is.GreaterThanOrEqualTo(ground - .002f),
                                $"{context}: moving {mesh.name} at {lowest:F3} enters paving {ground:F3}.");
                        }
                    }
                }
                Assert.That(highestCarriage - lowestCarriage, Is.GreaterThan(.7f), "Validate a real lift stroke, not only the folded parked pose.");
            }
            finally { cannery.ApplyAt(saved); }
        }

        private static void AssertCanneryShortLiftContacts(CityCanneryController cannery)
        {
            double saved=cannery.WorkingSeconds;
            var scratch=new Mesh();
            float minimumRearClearance=float.PositiveInfinity;
            try
            {
                foreach(CityFishSupplyStage stage in new[]{CityFishSupplyStage.LoadFish,CityFishSupplyStage.UnloadFish,
                    CityFishSupplyStage.LoadFinished,CityFishSupplyStage.UnloadShop})
                {
                    bool loading=stage==CityFishSupplyStage.LoadFish||stage==CityFishSupplyStage.LoadFinished;
                    var worker=cannery.transform.Find("Fish Delivery Driver")
                        .GetComponent<VillageResidentPresentation>();
                    float[] phases=loading?new[]{.36f,.41f,.46f,.76f,.80f,.84f}:new[]{.05f,.08f,.11f,.44f,.49f,.54f};
                    foreach(float phase in phases)
                    {
                        cannery.ApplyAt(TransferTime(cannery,stage,0,phase));
                        Transform right=CityCanneryAssetProvider.FindPart(cannery.ActiveTrolley.gameObject,"ANCHOR_TrolleyHandleRight");
                        Transform left=CityCanneryAssetProvider.FindPart(cannery.ActiveTrolley.gameObject,"ANCHOR_TrolleyHandleLeft");
                        string context=$"{stage}/{phase}: {cannery.LastCrewContactFailure}";
                        Assert.That(Vector3.Distance(worker.RightGrip.position,right.position),Is.LessThan(.025f),context);
                        Assert.That(Vector3.Distance(worker.LeftGrip.position,left.position),Is.LessThan(.025f),context);
                        MeshFilter deck=CityCanneryAssetProvider.FindPart(cannery.TailLift.gameObject,
                            "TailLiftVisible__Deck").GetComponent<MeshFilter>();
                        Assert.That(deck != null && deck.sharedMesh != null,Is.True,"The lift needs its authored working deck.");
                        Vector3[] deckVertices=deck.sharedMesh.vertices;
                        Assert.That(deckVertices.Length,Is.GreaterThan(0));
                        Bounds platform=new Bounds(cannery.TailLift.InverseTransformPoint(
                            deck.transform.TransformPoint(deckVertices[0])),Vector3.zero);
                        foreach(Vector3 vertex in deckVertices)
                            platform.Encapsulate(cannery.TailLift.InverseTransformPoint(deck.transform.TransformPoint(vertex)));
                        Assert.That(platform.size.z,Is.EqualTo(CityCanneryTruckDimensions.TailLiftLength).Within(.025f));
                        int footwearParts=0;
                        Vector3 low=Vector3.positiveInfinity,high=Vector3.negativeInfinity;
                        foreach(SkinnedMeshRenderer renderer in worker.ModelRoot.GetComponentsInChildren<SkinnedMeshRenderer>(true))
                        {
                            if(!renderer.name.StartsWith("GEO_Shoe",StringComparison.Ordinal)&&
                                !renderer.name.StartsWith("GEO_Boot",StringComparison.Ordinal))continue;
                            footwearParts++;
                            renderer.BakeMesh(scratch,true);
                            foreach(Vector3 vertex in scratch.vertices)
                            {
                                Vector3 world=renderer.localToWorldMatrix.MultiplyPoint3x4(vertex);
                                Vector3 local=cannery.TailLift.InverseTransformPoint(world);
                                low=Vector3.Min(low,local);high=Vector3.Max(high,local);
                            }
                        }
                        Assert.That(footwearParts,Is.GreaterThanOrEqualTo(8),"Measure both complete shoes, soles, heels and toe caps.");
                        string feet=$"{stage}/{phase}: feet {low:F3}..{high:F3}; platform {platform}; root {cannery.TailLift.InverseTransformPoint(worker.transform.position):F3}";
                        Assert.That(low.x,Is.GreaterThanOrEqualTo(platform.min.x-.01f),feet);
                        Assert.That(high.x,Is.LessThanOrEqualTo(platform.max.x+.01f),feet);
                        Assert.That(low.z,Is.GreaterThanOrEqualTo(platform.min.z-.01f),feet);
                        Assert.That(high.z,Is.LessThanOrEqualTo(platform.max.z+.01f),feet);
                        Assert.That(low.y,Is.InRange(-.015f,.07f),"The actual soles remain on the moving platform: "+feet);
                        minimumRearClearance=Mathf.Min(minimumRearClearance,low.z-platform.min.z);
                    }
                }
                Debug.Log($"COMPACT LIFT: complete footwear rear clearance={minimumRearClearance:F3} m; both grips retained through rise and descent.");
            }
            finally{Object.DestroyImmediate(scratch);cannery.ApplyAt(saved);}
        }

        private static void AssertCanneryWarehouseOpening(CityCanneryController cannery, CityPortController port)
        {
            cannery.ApplyAt(cannery.Cycle.TransferUnitStart(CityFishSupplyStage.LoadFish, 0));
            int measured = 0;
            foreach (MeshFilter mesh in port.Dock.GetComponentsInChildren<MeshFilter>(true))
            {
                if (mesh.GetComponent<Renderer>() == null) continue;
                foreach (Vector3 vertex in mesh.sharedMesh.vertices)
                {
                    Vector3 p = mesh.transform.TransformPoint(vertex) - port.Plan.Origin;
                    measured++;
                    bool inClearOpening = p.x > 4.14f && p.x < 4.4f && p.y > 1.6f && p.y < 4.4f &&
                        p.z > -15.43f && p.z < -12.57f;
                    Assert.That(inClearOpening, Is.False, $"Fixed fitting in open warehouse doorway: {mesh.name} at {p:F3}.");
                }
            }
            Assert.That(measured, Is.GreaterThan(100), "Inspect actual imported warehouse geometry.");
        }

        private static void AssertCanneryLiftGroundClearance(CityCanneryController cannery)
        {
            float minimum = float.PositiveInfinity;
            foreach (CityFishSupplyStage stage in new[] { CityFishSupplyStage.LoadFish, CityFishSupplyStage.UnloadFish,
                CityFishSupplyStage.LoadFinished, CityFishSupplyStage.UnloadShop })
            foreach (float phase in new[] { 0f, .08f, .34f, .41f, .54f, .8f, .94f, .999f })
            {
                cannery.ApplyAt(TransferTime(cannery, stage, 0, phase));
                Physics.SyncTransforms();
                foreach (MeshFilter mesh in cannery.TailLift.GetComponentsInChildren<MeshFilter>(true))
                {
                    if (mesh.GetComponent<Renderer>() == null) continue;
                    foreach (Vector3 vertex in mesh.sharedMesh.vertices)
                    {
                        Vector3 p = mesh.transform.TransformPoint(vertex);
                        float ground = float.NegativeInfinity;
                        Vector3 from = new Vector3(p.x, cannery.Truck.position.y + 2f, p.z);
                        foreach (RaycastHit hit in Physics.RaycastAll(from, Vector3.down, 10f, ~0, QueryTriggerInteraction.Ignore))
                            if (!hit.transform.IsChildOf(cannery.transform) && hit.normal.y > .65f &&
                                hit.point.y <= cannery.Truck.position.y + .5f)
                                ground = Mathf.Max(ground, hit.point.y);
                        Assert.That(float.IsNegativeInfinity(ground), Is.False, $"A paved surface must support {stage} at {p:F3}.");
                        float clearance = p.y - ground;
                        minimum = Mathf.Min(minimum, clearance);
                        Assert.That(clearance, Is.GreaterThanOrEqualTo(-.002f),
                            $"{stage}/{phase}: actual {mesh.name} underside at {p:F3} intersects surface {ground:F3}.");
                    }
                }
            }
            Debug.Log($"GROUNDED LIFT: minimum imported mesh clearance above physical paving={minimum:F3} m.");
        }

        private static void AssertCanneryLocalTrolleys(CityCanneryController cannery, CityPortController port)
        {
            cannery.ApplyAt(0);
            Transform[] carts = { cannery.PortTrolley, cannery.FactoryTrolley, cannery.ShopTrolley };
            Vector3[] parked = { carts[0].position, carts[1].position, carts[2].position };
            Assert.That(new HashSet<Transform>(carts).Count, Is.EqualTo(3), "Each receiving site owns its own jack.");
            Assert.That(Vector3.Distance(parked[0], port.Plan.World(new Vector3(4.14f, CityPortPlan.DeckHeight, -14f))), Is.LessThan(6f));
            Transform rawDoor = CityCanneryAssetProvider.FindPart(cannery.Equipment.gameObject, "ANCHOR_RawDoor");
            Assert.That(Vector3.Distance(parked[1], rawDoor.position), Is.LessThan(6f));
            var driver = cannery.transform.Find("Fish Delivery Driver").GetComponent<VillageResidentPresentation>();
            var contactFailures = new List<string>();
            foreach (Transform cart in carts)
            {
                Assert.That(cart.IsChildOf(cannery.Truck), Is.False);
                Assert.That(cart.gameObject.activeInHierarchy, Is.True, "Parked local jacks remain visible before arrival.");
            }
            foreach (CityFishSupplyStage stage in new[] { CityFishSupplyStage.LoadFish, CityFishSupplyStage.UnloadFish,
                CityFishSupplyStage.LoadFinished, CityFishSupplyStage.UnloadShop })
            {
                double start = cannery.Cycle.StageStart(stage), duration = cannery.Cycle.StageDuration(stage);
                int site = stage == CityFishSupplyStage.LoadFish ? 0 : stage == CityFishSupplyStage.UnloadShop ? 2 : 1;
                Transform cart = carts[site];
                foreach (bool returning in new[] { false, true })
                {
                    double edgeStart = returning ? duration - CityFishSupplyCycle.TransferEdgeDuration : 0d;
                    cannery.ApplyAt(start + edgeStart);
                    Vector3 previousCart = cart.position, previousDriver = driver.transform.position;
                    for (double t = .5d; t <= CityFishSupplyCycle.TransferEdgeDuration; t += .5d)
                    {
                        cannery.ApplyAt(start + edgeStart + t);
                        float moved = Vector3.Distance(previousCart, cart.position);
                        Assert.That(moved, Is.LessThan(1.7f), $"{stage}/{returning}/{t}: local jack must be wheeled, not moved instantly.");
                        Assert.That(Vector3.Distance(previousDriver, driver.transform.position), Is.LessThan(1.7f),
                            $"{stage}/{returning}/{t}: driver walks continuously between cab and jack.");
                        if (stage == CityFishSupplyStage.UnloadShop &&
                            (returning ? CityFishSupplyCycle.TransferEdgeDuration - t : t) >= 3.5d)
                        {
                            Physics.SyncTransforms();
                            Vector3 p = driver.transform.position;
                            foreach (Collider obstacle in Physics.OverlapCapsule(p + Vector3.up * .45f,
                                p + Vector3.up * 1.4f, .24f, ~0, QueryTriggerInteraction.Ignore))
                                Assert.That(obstacle.transform.IsChildOf(cannery.transform), Is.True,
                                    $"Shop approach/return {returning}/{t}: driver enters {obstacle.name} at {p:F3}.");
                            foreach (Collider obstacle in Physics.OverlapBox(cart.position + Vector3.up * .55f,
                                new Vector3(.34f, .4f, .7f), cart.rotation, ~0, QueryTriggerInteraction.Ignore))
                                Assert.That(obstacle.transform.IsChildOf(cannery.transform), Is.True,
                                    $"Shop approach/return {returning}/{t}: local trolley enters {obstacle.name} at {cart.position:F3}.");
                            // The body can step over the ordinary curb, but
                            // neither feet nor wheels may use the road height
                            // while their centre is already on the sidewalk.
                            foreach (Vector3 contact in new[] { p, cart.position })
                            {
                                float surface = float.NegativeInfinity;
                                foreach (RaycastHit hit in Physics.RaycastAll(contact + Vector3.up * 1.8f,
                                    Vector3.down, 4f, ~0, QueryTriggerInteraction.Ignore))
                                    if (!hit.transform.IsChildOf(cannery.transform) && hit.normal.y > .65f &&
                                        hit.point.y <= contact.y + .4f) surface = Mathf.Max(surface, hit.point.y);
                                Assert.That(float.IsNegativeInfinity(surface), Is.False, "The receiving route needs physical paving.");
                                Assert.That(contact.y, Is.GreaterThanOrEqualTo(surface - .02f),
                                    $"Shop approach/return {returning}/{t}: feet or wheels at {contact:F3} below physical surface {surface:F3}.");
                            }
                        }
                        if (moved > .008f && (cannery.TrolleyPhase == CityCanneryTrolleyPhase.Fetching ||
                            cannery.TrolleyPhase == CityCanneryTrolleyPhase.Returning))
                        {
                            Transform right = CityCanneryAssetProvider.FindPart(cart.gameObject, "ANCHOR_TrolleyHandleRight");
                            Transform left = CityCanneryAssetProvider.FindPart(cart.gameObject, "ANCHOR_TrolleyHandleLeft");
                            float rightError = Vector3.Distance(driver.RightGrip.position, right.position);
                            float leftError = Vector3.Distance(driver.LeftGrip.position, left.position);
                            if (rightError >= .025f || leftError >= .025f)
                                contactFailures.Add($"{stage}/{returning}/{t}: grip R={rightError:F3}, L={leftError:F3}, " +
                                    $"driver={driver.transform.position:F3}, jack={cart.position:F3}, handle={right.position:F3}");
                        }
                        for (int other = 0; other < carts.Length; other++)
                            if (other != site) Assert.That(Vector3.Distance(carts[other].position, parked[other]), Is.LessThan(.002f),
                                $"{stage}: jack at another site must remain parked.");
                        previousCart = cart.position; previousDriver = driver.transform.position;
                    }
                }
                Assert.That(Vector3.Distance(cart.position, parked[site]), Is.LessThan(.002f), $"{stage}: jack is returned before departure.");
                cannery.ApplyAt(TransferTime(cannery, stage, 0, .41f));
                Assert.That(cannery.ActiveTrolley, Is.SameAs(cart));
                Assert.That(Vector3.Distance(cart.position, parked[site]), Is.GreaterThan(1f));
            }
            cannery.ApplyAt(CanneryTime(cannery, CityFishSupplyStage.FactoryToPort, .5f));
            for (int site = 0; site < carts.Length; site++)
            {
                Assert.That(Vector3.Distance(carts[site].position, parked[site]), Is.LessThan(.002f));
                Assert.That(carts[site].gameObject.activeInHierarchy, Is.True, "Local jack remains at its site while truck is on the road.");
            }
            Assert.That(contactFailures, Is.Empty, "The driver keeps both actual handles through every pickup and return: " +
                string.Join("; ", contactFailures));
        }

        private IEnumerator CaptureCanneryBusPassing(CityGameRoot city, CityCanneryController cannery,
            Camera camera, List<Exception> failures)
        {
            CityBusActor bus = city.Bus.Actor;
            CityBusPresentation presentation = city.Bus.GetComponentInChildren<CityBusPresentation>(true);
            Assert.That(presentation, Is.Not.Null);
            double meeting = -1, bestTruckTime = -1;
            int meetingLink = -1, bestBusLink = -1;
            CityBusPathSample meetingSample = default;
            CityBusSpawnAnchor bestBus = null;
            Vector3 heading = default;
            float bestScore = float.NegativeInfinity;
            foreach (CityFishSupplyStage stage in new[] { CityFishSupplyStage.FactoryToPort,
                CityFishSupplyStage.PortToFactory, CityFishSupplyStage.FactoryToShop, CityFishSupplyStage.ShopToFactory })
            {
                double start = cannery.Cycle.StageStart(stage), duration = cannery.Cycle.StageDuration(stage);
                for (double time = start + 4d; time < start + duration - 4d; time += .25d)
                {
                    CityPortTruckPose truck = cannery.TruckPose(cannery.Cycle.Sample(time));
                    Vector3 forward = truck.Rotation * Vector3.forward;
                    if (Quaternion.Angle(truck.Rotation, cannery.TruckPose(cannery.Cycle.Sample(time - 2d)).Rotation) > 1f ||
                        Quaternion.Angle(truck.Rotation, cannery.TruckPose(cannery.Cycle.Sample(time + 2d)).Rotation) > 1f) continue;
                    foreach (int index in city.BusPlan.OrderedLinkIndices)
                    {
                        CityBusRouteLink link = city.BusPlan.Links[index];
                        if (link.Kind != CityBusRouteLinkKind.Straight) continue;
                        CityBusPathSample sample = link.Samples[link.Samples.Count / 2];
                        if (Vector3.Dot(forward, sample.Forward) > -.999f) continue;
                        Vector3 delta = sample.Position - truck.RearAxle;
                        if (Mathf.Abs(delta.y) > .5f || Mathf.Abs(Vector3.Dot(delta, forward)) > .4f ||
                            Mathf.Abs(Mathf.Abs(Vector3.Dot(delta, truck.Rotation * Vector3.right)) - 3f) > .06f) continue;
                        if (link.Length <= bestScore) continue;
                        bestScore = link.Length;
                        meeting = time; meetingLink = index; meetingSample = sample; heading = forward;
                    }
                }
            }
            DeferCanneryContract(failures, "opposing lanes exist", () => Assert.That(meeting, Is.GreaterThan(0),
                "A real shared street must carry opposite bus/truck routes with three metres between lane centres."));
            if (meetingLink < 0) yield break;
            CityBusRouteLink entry = city.BusPlan.Links[meetingLink];
            CityBusPathSample spawn = entry.Samples[0];
            foreach (CityBusPathSample sample in entry.Samples)
                if (sample.Distance <= meetingSample.Distance - 4f) spawn = sample;
            bus.PrepareSpawn(city.BusPlan, new CityBusSpawnAnchor("compact-passing", meetingLink, spawn.Distance,
                spawn.Position, spawn.Forward, city.BusPlan.Nodes[entry.FromNodeIndex].RoadEdge), 0x50415353u);
            bus.BindPresentation(presentation);
            MeshCollider truckBody = cannery.Truck.GetComponentInChildren<MeshCollider>();
            Assert.That(truckBody, Is.Not.Null);
            double working = meeting - 2d;
            cannery.ApplyAt(working);
            Vector3 busStart = bus.Position, truckStart = cannery.Truck.position;
            float closest = float.PositiveInfinity;
            bool passed = false, metWhileMoving = false, penetration = false;
            int truckWaits = 0;
            for (int step = 0; step < 700; step++)
            {
                Physics.SyncTransforms();
                bool truckClear = cannery.Traffic.TryAcquire(cannery.Snapshot) && !cannery.DetectObstacle();
                if (truckClear) working += .05d; else truckWaits++;
                float clearance = float.PositiveInfinity;
                cannery.Traffic.AccumulateObstacle(bus, Mathf.Max(12f, bus.GetRequiredStoppingDistance() + bus.Speed + 2f), ref clearance);
                bus.Advance(.05f, new CityBusObstacleState(!float.IsPositiveInfinity(clearance), clearance), 0f);
                cannery.ApplyAt(working);
                Physics.SyncTransforms();
                penetration |= Physics.ComputePenetration(bus.BodyCollider, bus.BodyCollider.transform.position, bus.BodyCollider.transform.rotation,
                    truckBody, truckBody.transform.position, truckBody.transform.rotation, out _, out _);
                Vector3 delta = bus.Position - cannery.Truck.position;
                float distance = new Vector2(delta.x, delta.z).magnitude;
                if (distance < closest)
                {
                    closest = distance; bestTruckTime = working; bestBusLink = bus.CurrentLinkIndex;
                    CityBusRouteLink current = city.BusPlan.Links[bestBusLink];
                    bestBus = new CityBusSpawnAnchor("compact-abreast", bestBusLink, bus.DistanceAlongLink,
                        bus.Position, bus.TravelDirection, city.BusPlan.Nodes[current.FromNodeIndex].RoadEdge);
                }
                if (distance < 4f && truckClear && bus.Speed > .3f) metWhileMoving = true;
                if (metWhileMoving && Vector3.Dot(delta, heading) < -12f &&
                    Vector3.Distance(busStart, bus.Position) > 8f &&
                    Vector3.Distance(truckStart, cannery.Truck.position) > 8f)
                { passed = true; break; }
            }
            DeferCanneryContract(failures, "moving truck and bus pass", () =>
            {
                Assert.That(penetration, Is.False, "The actual body colliders never intersect.");
                Assert.That(metWhileMoving, Is.True, $"Closest={closest:F2}, waits={truckWaits}, truck={cannery.LastObstacleName}");
                Assert.That(passed, Is.True, "Both vehicles continue beyond the encounter.");
                Assert.That(Vector3.Distance(busStart, bus.Position), Is.GreaterThan(8f));
                Assert.That(Vector3.Distance(truckStart, cannery.Truck.position), Is.GreaterThan(8f));
            });
            Debug.Log($"CANNERY BUS PASS: closest={closest:F3}, moving={metWhileMoving}, passed={passed}, penetration={penetration}, waits={truckWaits}, link={meetingLink}");
            bus.ReleasePresentation(city.Bus.transform);
            if (bestBus != null)
            {
                bus.PrepareSpawn(city.BusPlan, bestBus, 0x50415353u);
                bus.BindPresentation(presentation);
                cannery.ApplyAt(bestTruckTime);
                yield return CaptureCannery(camera, city, cannery, bestTruckTime, "driver-05-bus-passing",
                    cannery.Truck.TransformPoint(new Vector3(-1.5f, 5.2f, 10.5f)),
                    (cannery.Truck.position + bus.Position) * .5f + Vector3.up * 1.2f);
                bus.ReleasePresentation(city.Bus.transform);
            }
            cannery.Traffic.Release();
        }
    }
}

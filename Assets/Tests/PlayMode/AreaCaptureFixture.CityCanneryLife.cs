using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace BarPromenade.Tests.PlayMode
{
    public sealed partial class AreaCaptureFixture
    {
        [UnityTest]
        [Explicit("Factory doorway traversal, outside waiting and continuous work return with shared speech. Run without -batchmode for the actual Game view UI.")]
        public IEnumerator CityCanneryLivingShift()
        {
            Assert.That(Application.isBatchMode, Is.False, "Shared bubbles need an actual Game view capture.");
#if UNITY_EDITOR
            Type viewType = typeof(UnityEditor.EditorWindow).Assembly.GetType("UnityEditor.GameView");
            Assert.That(viewType, Is.Not.Null);
            UnityEditor.EditorWindow view = UnityEditor.EditorWindow.GetWindow(viewType);
            view.Show(); view.Focus();
#endif
            yield return CaptureFocusedPort(CaptureCanneryLivingShift);
        }

        private static IEnumerator CaptureCanneryLivingShift(Camera camera, CityGameRoot city,
            CityPortController port, CityPortCrew crew)
        {
            CityCanneryController cannery = city.Cannery;
            double savedWork = cannery.WorkingSeconds, savedLife = cannery.LifeSeconds;
            Vector3 savedHero = city.Player.GameObject.transform.position;
            Vector3 savedCamera = camera.transform.position;
            Quaternion savedRotation = camera.transform.rotation;
            float savedFov = camera.fieldOfView;
            bool savedSpeechManual = cannery.FactoryConversation.UseManualClock;
            var failures = new List<Exception>();
            try
            {
                GameSessionState.AdvanceGameTime((float)((12d * 60d - GameSessionState.GameTimeOfDayMinutes) /
                    GameTimeState.GameMinutesPerRealSecond));
                city.DayNight.ApplyCurrentTime(true);
                yield return ValidateCanneryLivingShift(camera, city, cannery, port, failures);
                CityCanneryPlan plan = cannery.Plan;
                cannery.ApplyLifeAt(30d);
                yield return CaptureCannery(camera, city, cannery, CanneryTime(cannery, CityCanneryProductionStage.Prepare, .45f),
                    "24-preparation-back-at-work", plan.World(new Vector3(-1.1f, 1.9f, -1.3f)),
                    plan.World(new Vector3(-6.1f, 1.35f, -2.05f)));
                DeferCanneryContract(failures, "production keeps its existing hand contacts", () =>
                    Assert.That(cannery.WorkerHandsMatch, Is.True, cannery.LastCrewContactFailure));
                double packStart = cannery.Cycle.ProductionStageStart(CityCanneryProductionStage.Pack) + 4d;
                double canSlot = (cannery.Cycle.ProductionStageDuration(CityCanneryProductionStage.Pack) - 5d) / 15d;
                yield return CaptureCannery(camera, city, cannery, packStart + 7.5d * canSlot,
                    "25-packing-keeps-the-same-can", plan.World(new Vector3(-1.1f, 1.9f, 5.7f)),
                    plan.World(new Vector3(-6.28f, 1.4f, 4.94f)));
                DeferCanneryContract(failures, "packing contact survives living pose transitions", () =>
                {
                    Assert.That(cannery.WorkerHandsMatch, Is.True, cannery.LastCrewContactFailure);
                    Transform can = CityCanneryAssetProvider.FindPart(cannery.transform.gameObject, "CanUnit07");
                    var packer = cannery.GetFactoryWorker(3);
                    Assert.That(Vector3.Distance(packer.RightGrip.position, can.position + Vector3.up * .075f), Is.LessThan(.08f));
                    Assert.That(Vector3.Distance(packer.LeftGrip.position, can.position + Vector3.up * .075f), Is.LessThan(.08f));
                });
            }
            finally
            {
                cannery.AutoAdvance = false;
                cannery.FactoryConversation.Suspend();
                cannery.FactoryConversation.UseManualClock = savedSpeechManual;
                cannery.ApplyAt(savedWork);
                cannery.ApplyLifeAt(savedLife);
                cannery.AdvanceSounds(false);
                city.Player.Motor.Teleport(savedHero);
                camera.transform.SetPositionAndRotation(savedCamera, savedRotation);
                camera.fieldOfView = savedFov;
                CityFishSupplySession.ResetForNewGame();
            }
            if (failures.Count > 0) throw new AggregateException("Factory living-shift capture failed.", failures);
            Debug.Log("CITY CANNERY LIVING SHIFT OK: independent life, task return and actual contacts, shared pair, physical sound and pause.");
        }

        private static IEnumerator ValidateCanneryLivingShift(Camera camera, CityGameRoot city,
            CityCanneryController cannery, CityPortController port, ICollection<Exception> failures)
        {
            Vector3 heroBefore = city.Player.GameObject.transform.position;
            CityCanneryPlan plan = cannery.Plan;
            CityFishSupplySession.ResetForNewGame();
            city.Player.Motor.Teleport(plan.World(new Vector3(-1.1f, .3f, 0f)));
            cannery.AutoAdvance = true;
            cannery.ApplyAt(0d);
            cannery.ApplyLifeAt(10d);
            double lifeBefore = cannery.LifeSeconds;
            Transform idleHead = cannery.GetFactoryWorker(2).Head;
            Quaternion headBefore = idleHead.rotation;
            yield return null;
            yield return null;
            DeferCanneryContract(failures, "living factory before first dock visit", () =>
            {
                Assert.That(CityFishSupplySession.HasStarted, Is.False);
                Assert.That(cannery.WorkingSeconds, Is.Zero);
                Assert.That(cannery.Truck.gameObject.activeSelf, Is.False);
                Assert.That(cannery.LifeSeconds, Is.GreaterThan(lifeBefore));
                Assert.That(Vector3.Distance(idleHead.forward, headBefore * Vector3.forward), Is.GreaterThan(.000001f),
                    "The rendered worker must keep breathing before the port starts.");
                Assert.That(cannery.Snapshot.FactoryFish + cannery.Snapshot.FactoryCases, Is.Zero);
                AssertCanneryWorkersOutside(cannery);
                Assert.That(cannery.PreparationClothInContact, Is.False, "An empty factory does not keep a worker tidying inside.");
            });
            cannery.AutoAdvance = false;
            yield return ValidateCanneryDoorPassages(camera, city, cannery, failures);
            cannery.AutoAdvance = true;
            DeferCanneryContract(failures, "audible contact follows the first filled can", () =>
            {
                double fillStart = cannery.Cycle.ProductionStageStart(CityCanneryProductionStage.Fill);
                double duration = cannery.Cycle.ProductionStageDuration(CityCanneryProductionStage.Fill) * CityFishSupplyCycle.ProductionSpeed;
                double contact = fillStart + (2d + (duration - 4d) * .35d / 15d) / CityFishSupplyCycle.ProductionSpeed;
                cannery.ApplyAt(contact - .02d);
                int sounds = cannery.ProcessContactsPlayed;
                Assert.That(cannery.VisibleFilledCanCount, Is.Zero);
                cannery.ApplyAt(contact + .02d);
                Assert.That(cannery.VisibleFilledCanCount, Is.EqualTo(1));
                Assert.That(cannery.ProcessContactsPlayed, Is.EqualTo(sounds + 1));
                Assert.That(cannery.CanContactSource.isPlaying, Is.True);
                Assert.That(cannery.CanContactSource.volume, Is.GreaterThan(0f));
                using (GameTimeScaleRuntime.AcquirePause())
                {
                    cannery.AdvanceSounds(false);
                    Assert.That(cannery.CanContactSource.isPlaying, Is.False);
                    Assert.That(cannery.ColdStoreSoundSource.isPlaying, Is.False);
                }
                cannery.ApplyAt(contact - .02d);
                cannery.ApplyAt(contact + .02d);
                Assert.That(cannery.ProcessContactsPlayed, Is.EqualTo(sounds + 1), "Reconstruction must not replay the same physical contact.");
            });
            cannery.AutoAdvance = false;
            cannery.ApplyLifeAt(24d);
            yield return CaptureCannery(camera, city, cannery, 0d, "18-before-delivery-all-outside",
                plan.World(new Vector3(2.8f, 2.35f, -6.8f)), plan.World(new Vector3(1.35f, 1.35f, -.6f)));

            // Waiting is outdoors. The old cloth remains at its work surface,
            // without a phantom indoor activity or an invented batch.
            Transform cloth = CityCanneryAssetProvider.FindPart(cannery.Equipment.gameObject, "MOVE_PreparationCloth");
            Vector3 clothDock = cloth.position;
            cannery.ApplyLifeAt(30d);
            DeferCanneryContract(failures, "all four workers wait outside instead of tidying an empty line", () =>
            {
                AssertCanneryWorkersOutside(cannery);
                Assert.That(cannery.PreparationClothInContact, Is.False);
                Assert.That(Vector3.Distance(cloth.position, clothDock), Is.LessThan(.002f));
                Assert.That(cannery.WorkerHandsMatch, Is.True, cannery.LastCrewContactFailure);
                Assert.That(cannery.Snapshot.FactoryFish, Is.Zero);
            });
            yield return ValidateCanneryShiftWalks(camera, city, cannery, failures);

            // One complete exchange beside the waiting group exercises the
            // same bubble/reply owner after the workers have moved outside.
            var speech = cannery.FactoryConversation;
            speech.UseManualClock = true;
            cannery.ApplyAt(0d);
            city.Player.Motor.Teleport(plan.World(new Vector3(3.05f, .3f, 0f)));
            int exchanges = speech.CompletedExchanges;
            int firstSpeaker = -1;
            bool capturedFirst = false, capturedReply = false;
            for (int step = 0; step < 160 && speech.CompletedExchanges == exchanges; step++)
            {
                cannery.ApplyLifeAt(step * .5d);
                speech.ApplyAt();
                if (!speech.HasExchange || !speech.Bubbles.TryGetSpeechFaceSample(
                    cannery.GetFactoryWorker(speech.LastSpeakerRole), out SpeechFaceSample face) ||
                    face.ElapsedSeconds < 1d ||
                    face.RevealedCharacters < LocalizationService.Get(speech.LastLineKey).Length) continue;
                if (!capturedFirst)
                {
                    firstSpeaker = speech.LastSpeakerRole;
                    capturedFirst = true;
                    Vector3 head = cannery.GetFactoryWorker(firstSpeaker).Head.position;
                    yield return CaptureCannerySpeech(camera, city, cannery, "22-factory-worker-speaks",
                        CanneryWaitingSpeechView(cannery, firstSpeaker), head);
                }
                else if (!capturedReply && speech.LastSpeakerRole != firstSpeaker)
                {
                    capturedReply = true;
                    Vector3 head = cannery.GetFactoryWorker(speech.LastSpeakerRole).Head.position;
                    yield return CaptureCannerySpeech(camera, city, cannery, "23-factory-worker-replies",
                        CanneryWaitingSpeechView(cannery, speech.LastSpeakerRole), head);
                }
            }
            DeferCanneryContract(failures, "one ordinary factory pair finishes", () =>
            {
                Assert.That(capturedFirst && capturedReply, Is.True);
                Assert.That(speech.CompletedExchanges, Is.GreaterThan(exchanges));
            });
            city.Player.Motor.Teleport(plan.World(new Vector3(40f, .3f, 0f)));
            speech.ApplyAt();
            DeferCanneryContract(failures, "factory speech releases on distance exit", () => Assert.That(speech.HasExchange, Is.False));
            speech.UseManualClock = false;

            // Reserve the next job early enough to walk back. Compare actual
            // pelvis/palm positions on both sides of the production boundary.
            cannery.ApplyLifeAt(30d);
            double prepare = cannery.Cycle.ProductionStageStart(CityCanneryProductionStage.Prepare);
            cannery.ApplyAt(prepare - .001d);
            VillageResidentPresentation preparation = cannery.GetFactoryWorker(1);
            Vector3 bodyBefore = preparation.transform.position, palmBefore = preparation.RightGrip.position;
            cannery.ApplyAt(prepare + .001d);
            DeferCanneryContract(failures, "outdoor waiting returns before production contact", () =>
            {
                Assert.That(Vector3.Distance(preparation.transform.position, bodyBefore), Is.LessThan(.01f));
                Assert.That(Vector3.Distance(preparation.RightGrip.position, palmBefore), Is.LessThan(.025f));
                Assert.That(Vector3.Distance(cloth.position, clothDock), Is.LessThan(.002f));
                Assert.That(cannery.FactoryWorkerHandsFree(1), Is.False);
            });
            double receive = TransferTime(cannery, CityFishSupplyStage.UnloadFish, 0, .86f);
            cannery.ApplyAt(receive);
            DeferCanneryContract(failures, "receiving leaves the outgoing scale empty", () =>
            {
                Assert.That(cannery.ShippingScaleWeight, Is.Zero,
                    "Receiving stock in the cold room must not weigh on the empty scale.");
                Assert.That(cannery.Snapshot.Inspection.IsActive, Is.False);
            });
            yield return CaptureCannery(camera, city, cannery, receive, "20-receiver-follows-incoming-cargo",
                plan.World(new Vector3(-1.1f, 1.9f, -5.3f)), plan.World(new Vector3(-3.8f, 1.2f, -6.0f)));

            // Block the actual driver/cart using the hero, and let Update run.
            // Free people continue to move while the shared physical load waits.
            CityFishSupplySession.ResetForNewGame();
            CityFishSupplySession.TryStart(true);
            double transfer = TransferTime(cannery, CityFishSupplyStage.UnloadFish, 0, .60f);
            GameSessionState.AdvanceGameTime((float)transfer);
            cannery.ApplyAt(CityFishSupplySession.Advance(false));
            Vector3 cartBefore = cannery.ActiveTrolley.position;
            Transform driver = cannery.transform.Find("Fish Delivery Driver");
            city.Player.Motor.Teleport(driver.position);
            Physics.SyncTransforms();
            cannery.ApplyLifeAt(10d);
            cannery.AutoAdvance = true;
            yield return null;
            double stoppedCargo = cannery.WorkingSeconds;
            lifeBefore = cannery.LifeSeconds;
            headBefore = cannery.GetFactoryWorker(2).Head.rotation;
            yield return null;
            yield return null;
            DeferCanneryContract(failures, "blocked delivery leaves free workers alive", () =>
            {
                Assert.That(cannery.IsBlocked, Is.True, cannery.LastObstacleName);
                Assert.That(cannery.WorkingSeconds, Is.EqualTo(stoppedCargo));
                Assert.That(Vector3.Distance(cannery.ActiveTrolley.position, cartBefore), Is.LessThan(.002f));
                Assert.That(cannery.LifeSeconds, Is.GreaterThan(lifeBefore));
                Assert.That(Vector3.Distance(cannery.GetFactoryWorker(2).Head.forward, headBefore * Vector3.forward), Is.GreaterThan(.000001f));
            });
            using (GameTimeScaleRuntime.AcquirePause())
            {
                lifeBefore = cannery.LifeSeconds;
                headBefore = cannery.GetFactoryWorker(2).Head.rotation;
                yield return null;
                yield return null;
                DeferCanneryContract(failures, "pause holds independent factory life", () =>
                {
                    Assert.That(cannery.LifeSeconds, Is.EqualTo(lifeBefore));
                    Assert.That(cannery.GetFactoryWorker(2).Head.rotation, Is.EqualTo(headBefore));
                    Assert.That(cannery.WorkingSeconds, Is.EqualTo(stoppedCargo));
                });
            }
            cannery.AutoAdvance = false;
            city.Player.Motor.Teleport(heroBefore);
            CityFishSupplySession.ResetForNewGame();
            cannery.AutoAdvance = true;
            yield return null;
            cannery.AutoAdvance = false;
            cannery.ApplyAt(0d);
            cannery.ApplyLifeAt(21d);
            yield return CaptureCannery(camera, city, cannery, 0d, "21-receiver-waits-outside",
                plan.World(new Vector3(2.7f, 1.9f, -4.8f)), cannery.FactoryWaitingPosition(0) + Vector3.up * 1.25f);
            DeferCanneryContract(failures, "bounded service work light", () =>
            {
                Assert.That(cannery.ServiceWorkLight, Is.Not.Null);
                Assert.That(cannery.ServiceWorkLight.enabled, Is.True);
                Assert.That(cannery.ServiceWorkLight.intensity, Is.GreaterThanOrEqualTo(5.5f * 2f / 3f - .001f));
                Assert.That(cannery.ServiceWorkLight.range, Is.LessThan(5f));
            });
        }

        private static void AssertCanneryWorkersOutside(CityCanneryController cannery)
        {
            for (int role = 0; role < 4; role++)
            {
                VillageResidentPresentation actor = cannery.GetFactoryWorker(role);
                Assert.That(actor, Is.Not.Null);
                Assert.That(actor.gameObject.activeInHierarchy, Is.True);
                // This animated resident rig has no physics capsule. Use the
                // same measured body clearance as the model's route validator.
                Vector3 local = cannery.Plan.Local(actor.transform.position);
                const float radius = .29f;
                bool outside = local.x - radius > .12f || local.x + radius < -8.12f ||
                    local.z - radius > 7.12f || local.z + radius < -7.12f;
                Assert.That(outside, Is.True, actor.name + " must wait wholly outside the actual hall, at " + local);
                Assert.That(local.x, Is.InRange(.4f, 3f), "All workers wait on the truck-facing side, clear of its parking lane.");
                Assert.That(local.z, Is.InRange(-5f, 3.5f), "The group stays clear of the receiving and shipping openings.");
                Assert.That(Vector3.Dot(actor.transform.forward, cannery.Plan.Right), Is.GreaterThan(.99f));
                Assert.That(actor.CurrentAction, Is.EqualTo(VillageResidentAction.Idle));
            }
        }

        private static IEnumerator ValidateCanneryDoorPassages(Camera camera, CityGameRoot city,
            CityCanneryController cannery, ICollection<Exception> failures)
        {
            PlayerMotor motor = city.Player.Motor;
            CharacterController body = city.Player.GameObject.GetComponent<CharacterController>();
            Assert.That(body.enabled, Is.True, "Use the real capsule with every site collider retained.");
            cannery.ApplyAt(0d);
            cannery.ApplyLifeAt(24d);
            foreach (int doorway in new[] { 0, 1, 2 })
            {
                float z = doorway == 0 ? -5.5f : doorway == 1 ? 5f : 2.65f;
                string name = doorway == 0 ? "receiving" : doorway == 1 ? "finished" : "west-staff";
                Vector3 outside = cannery.Plan.World(new Vector3(doorway == 2 ? -8.55f : 1.3f, CityCanneryPlan.YardTop + .04f, z));
                Vector3 inside = cannery.Plan.World(new Vector3(doorway == 2 ? -7.18f : -1.1f, CityCanneryPlan.FloorTop + .04f, z));
                // Reset only the case's starting position. In and back out are ordinary
                // constrained motor movement, never a teleport over the threshold.
                motor.Teleport(outside);
                motor.transform.rotation = Quaternion.LookRotation((doorway == 2 ? 1f : -1f) * cannery.Plan.Right, Vector3.up);
                Physics.SyncTransforms();
                for (int settle = 0; settle < 3; settle++) yield return null;
                foreach (Vector3 target in new[] { inside, outside })
                {
                    bool arrived = false;
                    for (int step = 0; step < 150 && !motor.InteractionPoseMoveStalled; step++)
                    {
                        arrived = motor.MoveTowardsApproachWaypoint(target, .045f, .04f);
                        if (arrived) break;
                        yield return null;
                    }
                    bool stalled = motor.InteractionPoseMoveStalled;
                    Vector3 stopped = cannery.Plan.Local(motor.transform.position);
                    motor.CancelInteractionPoseMove();
                    for (int settle = 0; settle < 3; settle++) yield return null;
                    Vector3 difference = motor.transform.position - target;
                    DeferCanneryContract(failures, name + " doorway " + (target == inside ? "entry" : "exit"), () =>
                    {
                        Assert.That(arrived && !stalled, Is.True, "The real motor stopped at " + stopped);
                        Assert.That(new Vector2(difference.x, difference.z).magnitude, Is.LessThan(.065f));
                        Assert.That(Mathf.Abs(difference.y), Is.LessThan(.12f), "The capsule follows the yard/floor step.");
                    });
                }
                yield return CaptureCannery(camera, city, cannery, 0d,
                    doorway == 0 ? "26-receiving-door-passable" : doorway == 1 ? "27-finished-door-passable" : "29-west-staff-door-passable",
                    cannery.Plan.World(new Vector3(doorway == 2 ? -10.5f : 2.8f, 1.85f, z + (z < 0f ? -1.7f : 1.7f))),
                    cannery.Plan.World(new Vector3(doorway == 2 ? -7.3f : -.85f, 1.25f, z)));
            }
            Debug.Log("CANNERY DOORS: the actual player motor crosses both goods openings and the staff door in both directions with physics and walkable constraints.");
        }

        private static IEnumerator ValidateCanneryShiftWalks(Camera camera, CityGameRoot city,
            CityCanneryController cannery, ICollection<Exception> failures)
        {
            cannery.AutoAdvance = false;
            cannery.FactoryConversation.UseManualClock = true;
            cannery.FactoryConversation.Suspend();
            cannery.ApplyAt(0d);
            var solids = new List<Collider>(cannery.GetComponentsInChildren<Collider>(true));
            foreach (Transform candidate in city.World.DistrictPointOfInterestRoot.GetComponentsInChildren<Transform>(true))
                if (candidate.name == "Industrial Cannery" && candidate.Find("Hall") != null)
                { solids.AddRange(candidate.GetComponentsInChildren<Collider>(true)); break; }
            var overlaps = new Collider[32];
            Collider wash = solids.Find(solid => solid != null && solid.name.StartsWith("COL_Wash", StringComparison.Ordinal));
            Assert.That(wash, Is.Not.Null);
            Physics.SyncTransforms();
            Vector3 forbidden = cannery.Plan.World(new Vector3(-3.9f, .18f, -2.05f));
            Assert.That(Array.Exists(Physics.OverlapCapsule(forbidden + Vector3.up * .35f,
                forbidden + Vector3.up * 1.5f, .29f, ~0, QueryTriggerInteraction.Ignore), hit => hit == wash), Is.True,
                "The route probe must actually detect a body pushed through the washing bench.");
            var entry = new double[4];
            var exit = new double[4];
            var duration = new double[4];
            for (int role = 0; role < 4; role++)
            {
                entry[role] = cannery.FactoryShiftEntryTime(role);
                exit[role] = cannery.FactoryShiftExitTime(role);
                duration[role] = cannery.FactoryShiftWalkDuration(role);
                Assert.That(duration[role], Is.GreaterThan(0d));
            }
            for (int journey = 0; journey < 2; journey++)
            {
                double begin = double.PositiveInfinity, end = 0d;
                for (int role = 0; role < 4; role++)
                {
                    double start = journey == 0 ? entry[role] : exit[role];
                    begin = Math.Min(begin, start);
                    end = Math.Max(end, start + duration[role]);
                }
                cannery.ApplyLifeAt(100d);
                cannery.ApplyAt(Math.Max(0d, begin - .1d));
                var previous = new Vector3[4];
                var maximumStep = new float[4];
                var walked = new bool[4];
                var crossedOpening = new bool[4];
                var collisions = new Dictionary<string, string>();
                for (int role = 0; role < 4; role++) previous[role] = cannery.GetFactoryWorker(role).transform.position;
                bool captured = false;
                int count = (int)Math.Ceiling((end - begin + .2d) * 10d);
                for (int sample = 0; sample <= count; sample++)
                {
                    double time = begin - .1d + sample * .1d;
                    cannery.ApplyLifeAt(100d + sample * .1d);
                    cannery.ApplyAt(Math.Max(0d, time));
                    Physics.SyncTransforms();
                    for (int role = 0; role < 4; role++)
                    {
                        var worker = cannery.GetFactoryWorker(role);
                        Vector3 position = worker.transform.position;
                        float distance = Vector3.Distance(position, previous[role]);
                        maximumStep[role] = Mathf.Max(maximumStep[role], distance);
                        walked[role] |= distance > .015f && worker.CurrentAction == VillageResidentAction.Walk;
                        previous[role] = position;
                        Vector3 local = cannery.Plan.Local(position);
                        bool eastOpening = Mathf.Abs(local.x) < .3f &&
                            (local.z > -6.5f && local.z < -4.5f || local.z > 3.9f && local.z < 6.1f);
                        bool publicOpening = Mathf.Abs(Mathf.Abs(local.z) - 7f) < .3f && local.x > -1.95f && local.x < -.25f;
                        bool staffOpening = Mathf.Abs(local.x + 8f) < .3f && local.z > 1.95f && local.z < 3.35f;
                        crossedOpening[role] |= eastOpening || publicOpening || staffOpening;
                        for (int other = role + 1; other < 4; other++)
                        {
                            Vector3 delta = cannery.GetFactoryWorker(other).transform.position - position;
                            if (Mathf.Abs(delta.y) < 1.5f && new Vector2(delta.x, delta.z).sqrMagnitude < .58f * .58f)
                                RecordCollision(role + "/worker/" + other,
                                    $"{worker.name} overlaps worker {other} at {time:F2} s, local {local:F3}.");
                        }
                        int hits = Physics.OverlapCapsuleNonAlloc(position + Vector3.up * .35f,
                            worker.Head.position, .29f, overlaps, ~0, QueryTriggerInteraction.Ignore);
                        Assert.That(hits, Is.LessThan(overlaps.Length), "Body clearance query must not truncate obstacles.");
                        for (int hit = 0; hit < hits; hit++)
                        {
                            Collider solid = overlaps[hit];
                            if (!solids.Contains(solid) || solid.transform.IsChildOf(worker.transform)) continue;
                            RecordCollision(role + "/solid/" + solid.name,
                                $"{worker.name} intersects {solid.name} at {time:F2} s, local {local:F3}.");
                        }
                    }
                    if (!captured && time >= begin + (end - begin) * .5d)
                    {
                        captured = true;
                        yield return CaptureCannery(camera, city, cannery, time,
                            journey == 0 ? "19-workers-walk-in-for-delivery" : "28-workers-walk-out-after-packing",
                            cannery.Plan.World(new Vector3(-10.5f, 2.2f, 4.5f)),
                            cannery.Plan.World(new Vector3(-7.4f, 1.35f, 2.4f)));
                    }
                    if (sample % 80 == 0) yield return null;
                }
                DeferCanneryContract(failures, journey == 0 ? "four continuous walks into the factory" : "four continuous walks back outside", () =>
                {
                    Assert.That(collisions, Is.Empty, string.Join("\n", collisions.Values));
                    for (int role = 0; role < 4; role++)
                    {
                        Assert.That(walked[role], Is.True, "Role " + role + " has a visible walking phase.");
                        Assert.That(crossedOpening[role], Is.True, "Role " + role + " uses a real doorway.");
                        Assert.That(maximumStep[role], Is.LessThan(.36f), "Role " + role + " must not jump between waiting and work.");
                        if (journey == 0)
                        {
                            Vector3 local = cannery.Plan.Local(cannery.GetFactoryWorker(role).transform.position);
                            Assert.That(local.x, Is.InRange(-8f, 0f));
                            Assert.That(local.z, Is.InRange(-7f, 7f));
                        }
                    }
                    if (journey == 1) AssertCanneryWorkersOutside(cannery);
                });
                void RecordCollision(string key, string description)
                {
                    if (!collisions.ContainsKey(key)) collisions.Add(key, description);
                }
            }
            DeferCanneryContract(failures, "the cleared line leaves every worker outdoors", () =>
            {
                Assert.That(cannery.Production.IsActive, Is.False);
                Assert.That(cannery.Snapshot.FactoryFish, Is.Zero);
                Assert.That(cannery.PreparationClothInContact, Is.False);
                for (int role = 0; role < 4; role++)
                    Assert.That(Vector3.Distance(cannery.GetFactoryWorker(role).transform.position,
                        cannery.FactoryWaitingPosition(role)), Is.LessThan(.04f));
            });
        }

        private static Vector3 CanneryWaitingSpeechView(CityCanneryController cannery, int role)
        {
            Vector3 head = cannery.GetFactoryWorker(role).Head.position;
            return head + cannery.Plan.Right * 1.7f + cannery.Plan.Forward * .65f + Vector3.up * .25f;
        }

        private static IEnumerator CaptureCannerySpeech(Camera camera, CityGameRoot city,
            CityCanneryController cannery, string name, Vector3 from, Vector3 target)
        {
            PlayerCameraFollow follow = camera.GetComponent<PlayerCameraFollow>();
            bool wasEnabled = follow != null && follow.enabled;
            if (follow != null) follow.enabled = false;
            try
            {
                city.Player.Motor.Teleport(from - Vector3.up * EyeHeight);
                camera.transform.SetPositionAndRotation(from, Quaternion.LookRotation(target - from));
                camera.fieldOfView = 54f;
                Physics.SyncTransforms();
                cannery.ApplyLifeAt(cannery.LifeSeconds);
                cannery.FactoryConversation.ApplyAt();
                for (int frame = 0; frame < 3; frame++) yield return null;
                var bubbles = cannery.FactoryConversation.Bubbles;
                Assert.That(bubbles.HasRenderedLayout, Is.True, "The actual shared Game view bubble must be rendered.");
                Assert.That(bubbles.LastRenderedBubbleCount, Is.GreaterThan(0));
                Assert.That(bubbles.LastRenderedRevealedText,
                    Is.EqualTo(LocalizationService.Get(cannery.FactoryConversation.LastLineKey)));
                string path = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "Captures", "CityCannery", name + ".png"));
                Directory.CreateDirectory(Path.GetDirectoryName(path));
                DateTime previous = File.Exists(path) ? File.GetLastWriteTimeUtc(path) : DateTime.MinValue;
                ScreenCapture.CaptureScreenshot(path);
                float deadline = Time.realtimeSinceStartup + 3f;
                do { yield return null; }
                while ((!File.Exists(path) || File.GetLastWriteTimeUtc(path) <= previous) && Time.realtimeSinceStartup < deadline);
                Assert.That(File.Exists(path) && File.GetLastWriteTimeUtc(path) > previous, Is.True,
                    "Capture the rendered world and its real speech UI in the same frame.");
                Debug.Log("CANNERY SPEECH FRAME: " + name);
            }
            finally { if (follow != null) follow.enabled = wasEnabled; }
        }
    }
}

using System;
using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace BarPromenade.Tests.PlayMode
{
    public sealed partial class AreaCaptureFixture
    {
        [UnityTest]
        [Explicit("Outdoor factory waiting before deliveries and after the shift: visible idle, nearby real speech, rooted feet and pause.")]
        public IEnumerator CityCanneryOutsideWait() => CaptureFocusedPort(CaptureCanneryOutsideWait);

        private static IEnumerator CaptureCanneryOutsideWait(Camera camera, CityGameRoot city,
            CityPortController port, CityPortCrew crew)
        {
            CityCanneryController cannery = city.Cannery;
            CityCanneryConversationController speech = cannery.FactoryConversation;
            PlayerCameraFollow follow = camera.GetComponent<PlayerCameraFollow>();
            bool cameraFollow = follow != null && follow.enabled;
            bool manualSpeech = speech.UseManualClock;
            Vector3 heroBefore = city.Player.GameObject.transform.position;
            Vector3 cameraBefore = camera.transform.position;
            Quaternion cameraRotation = camera.transform.rotation;
            float cameraFov = camera.fieldOfView;
            try
            {
                GameSessionState.AdvanceGameTime((float)((12d * 60d - GameSessionState.GameTimeOfDayMinutes) /
                    GameTimeState.GameMinutesPerRealSecond));
                city.DayNight.ApplyCurrentTime(true);
                CityFishSupplySession.ResetForNewGame();
                // The ordinary observer stands in front of the moved waiting
                // group, beyond the old x=4 room gate but within real earshot.
                city.Player.Motor.Teleport(cannery.Plan.World(new Vector3(5f, .3f, -2.5f)));
                if (follow != null) follow.enabled = false;
                Vector3 from = cannery.Plan.World(new Vector3(6.1f, 1.95f, -.6f));
                Vector3 target = cannery.Plan.World(new Vector3(2.75f, 1.35f, -2.8f));
                camera.transform.SetPositionAndRotation(from, Quaternion.LookRotation(target - from));
                camera.fieldOfView = 68f;
                cannery.AutoAdvance = false;
                cannery.ForcePresentation = false;
                speech.UseManualClock = false;
                speech.Suspend();
                cannery.ApplyAt(0d);
                cannery.ApplyLifeAt(0d);
                Physics.SyncTransforms();
                AssertCanneryWaitingBench(city, cannery);
                CaptureCanneryWaitingContext(camera, cannery, "outside-wait-bench-and-crew");
                cannery.AutoAdvance = true;
                yield return null;
                Assert.That(cannery.FactoryPresentationActive, Is.True, "Normal distance presentation includes the nearby yard.");
                var initial = new CanneryWaitingPose(cannery);
                var maximumHeadAngles = new float[4];
                double lifeStart = cannery.LifeSeconds;
                CaptureCurrentCamera(camera, "CityCannery", "outside-wait-00-before-delivery");
                float deadline = Time.realtimeSinceStartup + 20f;
                while (cannery.LifeSeconds < lifeStart + 4d && Time.realtimeSinceStartup < deadline)
                {
                    yield return null;
                    Assert.That(speech.HasExchange, Is.False, "Measure independent waiting before conversation starts.");
                    AssertCanneryWaitingPose(cannery, initial, maximumHeadAngles);
                }
                Assert.That(cannery.LifeSeconds, Is.GreaterThanOrEqualTo(lifeStart + 4d));
                Assert.That(CityFishSupplySession.HasStarted, Is.False);
                Assert.That(cannery.WorkingSeconds, Is.Zero);
                Assert.That(cannery.Truck.gameObject.activeSelf, Is.False);
                AssertCanneryWaitingHeadMotion(maximumHeadAngles);
                CaptureCurrentCamera(camera, "CityCannery", "outside-wait-01-looks-around");
                Debug.Log("CANNERY OUTSIDE WAIT: all four visibly move before the delivery latch, with rooted feet.");

                int exchanges = speech.CompletedExchanges;
                bool gestureSeen = false, paused = false;
                deadline = Time.realtimeSinceStartup + 45f;
                while (speech.CompletedExchanges == exchanges && Time.realtimeSinceStartup < deadline)
                {
                    yield return null;
                    AssertCanneryWaitingPose(cannery, initial, maximumHeadAngles);
                    int speaker = speech.LastSpeakerRole;
                    if (!speech.HasExchange || speaker < 0 || !speech.Bubbles.TryGetSpeechFaceSample(
                        cannery.GetFactoryWorker(speaker), out SpeechFaceSample face) || !face.IsTyping) continue;
                    VillageResidentPresentation actor = cannery.GetFactoryWorker(speaker);
                    Vector3 hand = actor.transform.InverseTransformPoint(actor.RightGrip.position);
                    if (Vector3.Distance(hand, initial.RightHands[speaker]) <= .08f) continue;
                    if (!gestureSeen)
                    {
                        gestureSeen = true;
                        CaptureCurrentCamera(camera, "CityCannery", "outside-wait-02-conversation-gesture");
                    }
                    if (paused) continue;
                    paused = true;
                    using (GameTimeScaleRuntime.AcquirePause())
                    {
                        yield return null;
                        var frozen = new CanneryWaitingPose(cannery);
                        double frozenLife = cannery.LifeSeconds, frozenWork = cannery.WorkingSeconds;
                        Assert.That(speech.Bubbles.TryGetSpeechFaceSample(actor, out SpeechFaceSample held), Is.True);
                        yield return null;
                        yield return null;
                        Assert.That(cannery.LifeSeconds, Is.EqualTo(frozenLife));
                        Assert.That(cannery.WorkingSeconds, Is.EqualTo(frozenWork));
                        Assert.That(speech.Bubbles.TryGetSpeechFaceSample(actor, out SpeechFaceSample still), Is.True);
                        Assert.That(still.ElapsedSeconds, Is.EqualTo(held.ElapsedSeconds));
                        for (int role = 0; role < 4; role++)
                        {
                            var worker = cannery.GetFactoryWorker(role);
                            Assert.That(worker.Head.rotation, Is.EqualTo(frozen.HeadRotations[role]));
                            Assert.That(worker.RightGrip.position, Is.EqualTo(frozen.RightHandWorld[role]));
                        }
                    }
                }
                Assert.That(gestureSeen && paused, Is.True, "A nearby observer sees the ordinary speaker's free-hand gesture.");
                Assert.That(speech.CompletedExchanges, Is.GreaterThan(exchanges), "Real Update/LateUpdate completes the pair outside the former room boundary.");
                Assert.That(CityFishSupplySession.HasStarted, Is.False);
                Assert.That(cannery.WorkingSeconds, Is.Zero);
                Debug.Log("CANNERY OUTSIDE WAIT: nearby real-time pair and pause passed.");

                // Reconstruct a completed shift, then sample the same living
                // clock without spending another delivery cycle in the test.
                cannery.AutoAdvance = false;
                speech.UseManualClock = true;
                speech.Suspend();
                double afterShift = cannery.Cycle.StageStart(CityFishSupplyStage.FactoryToShop);
                Assert.That(CityFishSupplySession.TrySetDebugWorkingSeconds(afterShift), Is.True);
                cannery.ApplyAt(afterShift);
                double laterLife = cannery.LifeSeconds + 10d;
                cannery.ApplyLifeAt(laterLife);
                speech.ApplyAt();
                var after = new CanneryWaitingPose(cannery);
                maximumHeadAngles = new float[4];
                exchanges = speech.CompletedExchanges;
                bool laterGesture = false;
                for (int step = 1; step <= 100 && speech.CompletedExchanges == exchanges; step++)
                {
                    cannery.ApplyLifeAt(laterLife + step * .5d);
                    speech.ApplyAt();
                    AssertCanneryWaitingPose(cannery, after, maximumHeadAngles);
                    Assert.That(cannery.WorkingSeconds, Is.EqualTo(afterShift));
                    int speaker = speech.LastSpeakerRole;
                    if (speech.HasExchange && speaker >= 0 && speech.Bubbles.TryGetSpeechFaceSample(
                        cannery.GetFactoryWorker(speaker), out SpeechFaceSample face) && face.IsTyping)
                    {
                        var actor = cannery.GetFactoryWorker(speaker);
                        laterGesture |= Vector3.Distance(actor.transform.InverseTransformPoint(actor.RightGrip.position),
                            after.RightHands[speaker]) > .08f;
                    }
                    if (step == 8)
                    {
                        Assert.That(speech.HasExchange, Is.False);
                        AssertCanneryWaitingHeadMotion(maximumHeadAngles);
                        CaptureCanneryWaitingContext(camera, cannery, "outside-wait-03-after-shift");
                    }
                    if (step % 8 == 0) yield return null;
                }
                Assert.That(speech.CompletedExchanges, Is.GreaterThan(exchanges), "Post-shift waiting retains ordinary pairs.");
                Assert.That(laterGesture, Is.True);
                Assert.That(cannery.Snapshot.Inspection.IsActive, Is.False);
                Assert.That(cannery.PreparationClothInContact, Is.False);
                Assert.That(cannery.ShippingScaleWeight, Is.Zero);
                Debug.Log("CITY CANNERY OUTSIDE WAIT OK: four visible idle poses, two waiting periods, nearby pairs/gestures and real pause.");
            }
            finally
            {
                cannery.AutoAdvance = false;
                speech.Suspend();
                speech.UseManualClock = manualSpeech;
                CityFishSupplySession.ResetForNewGame();
                cannery.ApplyAt(0d);
                cannery.AdvanceSounds(false);
                city.Player.Motor.Teleport(heroBefore);
                camera.transform.SetPositionAndRotation(cameraBefore, cameraRotation);
                camera.fieldOfView = cameraFov;
                if (follow != null) follow.enabled = cameraFollow;
            }
        }

        private static void AssertCanneryWaitingBench(CityGameRoot city, CityCanneryController cannery)
        {
            Transform bench = null;
            foreach (Transform candidate in city.World.DistrictPointOfInterestRoot.GetComponentsInChildren<Transform>(true))
                if (candidate.name == "Cannery Waiting Bench")
                {
                    Assert.That(bench, Is.Null, "The factory has one waiting bench.");
                    bench = candidate;
                }
            Assert.That(bench, Is.Not.Null);
            Vector3 low = Vector3.positiveInfinity, high = Vector3.negativeInfinity;
            foreach (MeshFilter mesh in bench.GetComponentsInChildren<MeshFilter>(true))
            {
                if (mesh.name.StartsWith("COL_", StringComparison.Ordinal)) continue;
                foreach (Vector3 vertex in mesh.sharedMesh.vertices)
                {
                    Vector3 local = cannery.Plan.Local(mesh.transform.TransformPoint(vertex));
                    low = Vector3.Min(low, local);
                    high = Vector3.Max(high, local);
                }
            }
            Assert.That(Vector3.Distance(low, new Vector3(.39f, .08f, -3.175f)), Is.LessThan(.025f),
                "Measure the placed imported mesh, including its real metre basis.");
            Assert.That(Vector3.Distance(high, new Vector3(.91f, .60f, -1.325f)), Is.LessThan(.025f));
            BoxCollider collision = bench.GetComponent<BoxCollider>();
            Assert.That(collision, Is.Not.Null);
            Assert.That(collision.enabled && collision.gameObject.activeInHierarchy, Is.True);
            Assert.That(collision.isTrigger, Is.False);
            Vector3 topProbe = cannery.Plan.World((low + high) * .5f) + Vector3.up * 2f;
            Assert.That(collision.Raycast(new Ray(topProbe, Vector3.down), out RaycastHit hit, 3f), Is.True,
                "The actual bench blocks a physics ray through its footprint.");
            Assert.That(cannery.Plan.Local(hit.point).y, Is.EqualTo(high.y).Within(.025f));
            Assert.That(low.x, Is.GreaterThan(.12f), "The bench stands beyond the hall wall.");
            Assert.That(low.z, Is.GreaterThan(-4.5f), "The receiving opening remains clear.");
            Assert.That(high.z, Is.LessThan(3.9f), "The shipping opening remains clear.");
            Assert.That(1.9f - .4f - high.x, Is.GreaterThanOrEqualTo(.58f),
                "The existing outdoor jack lane retains its measured clearance.");
            float nearestWorker = float.PositiveInfinity;
            for (int role = 0; role < 4; role++)
            {
                Vector3 waiting = cannery.Plan.Local(cannery.FactoryWaitingPosition(role));
                Assert.That(waiting.x - .29f - high.x, Is.GreaterThanOrEqualTo(1.5f),
                    "The bench clears the waiting bodies and their shared walking strip.");
                nearestWorker = Mathf.Min(nearestWorker, Vector2.Distance(new Vector2(waiting.x, waiting.z),
                    new Vector2((low.x + high.x) * .5f, (low.z + high.z) * .5f)));
            }
            Assert.That(nearestWorker, Is.LessThan(2.3f), "The bench belongs beside the waiting group.");
        }

        private static void CaptureCanneryWaitingContext(Camera camera, CityCanneryController cannery, string name)
        {
            Vector3 savedPosition = camera.transform.position;
            Quaternion savedRotation = camera.transform.rotation;
            float savedField = camera.fieldOfView;
            try
            {
                // This side of the waiting group also stays outside the truck
                // body after loading, unlike the ordinary pre-delivery view.
                Vector3 from = cannery.Plan.World(new Vector3(3.4f, 1.8f, .1f));
                Vector3 target = cannery.Plan.World(new Vector3(1.8f, 1f, -2.5f));
                camera.transform.SetPositionAndRotation(from, Quaternion.LookRotation(target - from));
                camera.fieldOfView = 70f;
                CaptureCurrentCamera(camera, "CityCannery", name);
            }
            finally
            {
                camera.transform.SetPositionAndRotation(savedPosition, savedRotation);
                camera.fieldOfView = savedField;
            }
        }

        private static void AssertCanneryWaitingHeadMotion(float[] angles)
        {
            for (int role = 0; role < 4; role++)
                Assert.That(angles[role], Is.GreaterThan(2f), "Waiting role " + role + " needs a visible gaze change, not microscopic breathing drift.");
        }

        private static void AssertCanneryWaitingPose(CityCanneryController cannery, CanneryWaitingPose initial, float[] angles)
        {
            for (int role = 0; role < 4; role++)
            {
                var actor = cannery.GetFactoryWorker(role);
                Assert.That(actor.gameObject.activeInHierarchy, Is.True);
                Assert.That(actor.CurrentAction, Is.EqualTo(VillageResidentAction.Idle));
                Assert.That(cannery.FactoryWorkerHandsFree(role), Is.True);
                Assert.That(Vector3.Distance(actor.transform.position, initial.Roots[role]), Is.LessThan(.002f));
                Assert.That(Vector3.Distance(actor.transform.position, cannery.FactoryWaitingPosition(role)), Is.LessThan(.002f));
                for (int foot = 0; foot < 2; foot++)
                    Assert.That(Vector3.Distance(initial.Feet[role, foot].position, initial.FootPositions[role, foot]), Is.LessThan(.025f),
                        "Idle keeps role " + role + " foot " + foot + " planted.");
                angles[role] = Mathf.Max(angles[role], Vector3.Angle(initial.HeadRotations[role] * Vector3.forward, actor.Head.forward));
            }
        }

        private sealed class CanneryWaitingPose
        {
            public readonly Vector3[] Roots = new Vector3[4], RightHands = new Vector3[4], RightHandWorld = new Vector3[4];
            public readonly Quaternion[] HeadRotations = new Quaternion[4];
            public readonly Transform[,] Feet = new Transform[4, 2];
            public readonly Vector3[,] FootPositions = new Vector3[4, 2];

            public CanneryWaitingPose(CityCanneryController cannery)
            {
                for (int role = 0; role < 4; role++)
                {
                    var actor = cannery.GetFactoryWorker(role);
                    Roots[role] = actor.transform.position;
                    HeadRotations[role] = actor.Head.rotation;
                    RightHands[role] = actor.transform.InverseTransformPoint(actor.RightGrip.position);
                    RightHandWorld[role] = actor.RightGrip.position;
                    for (int foot = 0; foot < 2; foot++)
                    {
                        Feet[role, foot] = CityPedestrianHandProps.FindSocket(actor.ModelRoot, foot == 0 ? "foot.L" : "foot.R");
                        Assert.That(Feet[role, foot], Is.Not.Null);
                        FootPositions[role, foot] = Feet[role, foot].position;
                    }
                }
            }
        }
    }
}

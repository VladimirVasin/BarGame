using System;
using System.Collections;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace BarPromenade.Tests.PlayMode
{
    public sealed class CanneryReceiverAssetsSetup : IPrebuildSetup
    {
        public void Setup()
        {
#if UNITY_EDITOR
            new CanneryWomanAssetsSetup().Setup();
            Type.GetType("BarPromenade.Editor.CanneryReceiverAssetSetup, BarPromenade.Editor", true)
                .GetMethod("BuildOrThrow", Type.EmptyTypes).Invoke(null, null);
#endif
        }
    }

    public sealed partial class AreaCaptureFixture
    {
        [UnityTest]
        [Explicit("Authored receiver: measured body, glasses, painted speech, three continuous cartons, real clearance, restoration and pause. Run without -batchmode.")]
        [PrebuildSetup(typeof(CanneryReceiverAssetsSetup))]
        public IEnumerator CityCanneryInspection()
        {
            Assert.That(Application.isBatchMode, Is.False, "Capture the actual shared speech UI.");
#if UNITY_EDITOR
            Type viewType = typeof(UnityEditor.EditorWindow).Assembly.GetType("UnityEditor.GameView");
            var view = UnityEditor.EditorWindow.GetWindow(viewType);
            view.Show(); view.Focus();
#endif
            yield return CaptureFocusedPort(CaptureCanneryInspection);
        }

        private static IEnumerator CaptureCanneryInspection(Camera camera, CityGameRoot city,
            CityPortController port, CityPortCrew crew)
        {
            CityCanneryController cannery = city.Cannery;
            CityCanneryPlan plan = cannery.Plan;
            var failures = new List<Exception>();
            bool manualSpeech = cannery.FactoryConversation.UseManualClock;
            Vector3 hero = city.Player.GameObject.transform.position;
            double savedWork = cannery.WorkingSeconds, savedLife = cannery.LifeSeconds;
            try
            {
                GameSessionState.AdvanceGameTime((float)((12d * 60d - GameSessionState.GameTimeOfDayMinutes) /
                    GameTimeState.GameMinutesPerRealSecond));
                city.DayNight.ApplyCurrentTime(true);
                cannery.FactoryConversation.UseManualClock = true;
                cannery.FactoryConversation.Suspend();
                yield return CaptureCanneryReceiver(camera, city, cannery, failures);
                yield return CaptureCanneryWomanHands(camera, city, cannery, failures);
                cannery.FactoryConversation.Suspend();
                cannery.ApplyLifeAt(100d);
                city.Player.Motor.Teleport(plan.World(new Vector3(8f, .3f, -7f)));
                var cartons = new Transform[CityFishSupplyCycle.HandlingUnits];
                for (int unit = 0; unit < cartons.Length; unit++) cartons[unit] = cannery.FinishedBox(unit);
                DeferCanneryContract(failures, "scale is physically outside shipping", () =>
                    AssertCanneryShippingScale(cannery));
                DeferCanneryContract(failures, "receiving never loads the outgoing scale", () =>
                {
                    cannery.ApplyAt(TransferTime(cannery, CityFishSupplyStage.UnloadFish, 0, .86f));
                    Assert.That(cannery.ShippingScaleWeight, Is.Zero);
                    Assert.That(cannery.Snapshot.Inspection.IsActive, Is.False);
                });
                for (int batch = 0; batch < 2; batch++)
                {
                    for (int unit = 0; unit < cartons.Length; unit++)
                    {
                        int currentUnit = unit, currentBatch = batch;
                        DeferCanneryContract(failures, "inspection batch " + batch + " carton " + unit, () =>
                            AssertCanneryCartonInspection(cannery, cartons, currentUnit, currentBatch));
                        Debug.Log("CANNERY INSPECTION: sampled batch " + batch + ", carton " + unit);
                        yield return null;
                    }
                    int sampledBatch = batch;
                    DeferCanneryContract(failures, "loading waits for final approval and clearance, batch " + batch, () =>
                    {
                        double load = cannery.Cycle.StageStart(CityFishSupplyStage.LoadFinished, sampledBatch);
                        Assert.That(load, Is.EqualTo(cannery.Cycle.InspectionFinishedAt(sampledBatch)).Within(.000001d));
                        cannery.ApplyAt(load - .001d);
                        Assert.That(cannery.Snapshot.Stage, Is.EqualTo(CityFishSupplyStage.WaitForProduction));
                        Assert.That(cannery.Snapshot.Inspection.StoredUnits, Is.EqualTo(3));
                        Assert.That(cannery.Snapshot.TruckCases, Is.Zero);
                        cannery.ApplyAt(load + .001d);
                        Assert.That(cannery.Snapshot.Stage, Is.EqualTo(CityFishSupplyStage.LoadFinished));
                        Assert.That(cannery.Snapshot.Inspection.ApprovedUnits, Is.EqualTo(3));
                        Assert.That(cannery.Snapshot.Inspection.IsActive, Is.False);
                        Assert.That(cannery.ShippingScaleWeight, Is.Zero);
                        cannery.ApplyAt(cannery.Cycle.StageStart(CityFishSupplyStage.FactoryToShop, sampledBatch));
                        Assert.That(cannery.Snapshot.TruckCases, Is.EqualTo(3));
                        for (int unit = 0; unit < cartons.Length; unit++)
                        {
                            Assert.That(cannery.FinishedBox(unit), Is.SameAs(cartons[unit]));
                            Assert.That(cartons[unit].gameObject.activeInHierarchy, Is.True);
                        }
                    });
                }
                DeferCanneryContract(failures, "inspector and moving cartons clear site collision", () =>
                    AssertCanneryInspectionRoute(city, cannery));
                DeferCanneryContract(failures, "approved supports and loading clear the outdoor equipment and waiting crew", () =>
                    AssertCanneryInspectedLoading(cannery));
                DeferCanneryContract(failures, "cold reconstruction keeps carton custody and pose", () =>
                    AssertCanneryInspectionRestore(city, cannery, port));

                foreach (CityCanneryInspectionStage phase in new[] { CityCanneryInspectionStage.Pickup,
                    CityCanneryInspectionStage.CarryToScale, CityCanneryInspectionStage.Settle,
                    CityCanneryInspectionStage.Approve, CityCanneryInspectionStage.CarryToReady })
                {
                    double time = InspectionTime(cannery, phase, 0,
                        phase == CityCanneryInspectionStage.CarryToScale ? .88d : .5d);
                    bool inside = phase == CityCanneryInspectionStage.Pickup;
                    Vector3 from = inside ? plan.World(new Vector3(-1.2f, 2.05f, 5.4f)) :
                        plan.World(new Vector3(4.7f, 2.2f, 8.6f));
                    cannery.ApplyAt(time);
                    Vector3 target = inside ? cannery.FinishedBox(0).position + Vector3.up * .45f :
                        cannery.ShippingScaleLoadPosition + Vector3.up * .7f;
                    yield return CaptureCannery(camera, city, cannery, time,
                        "inspection-" + phase.ToString().ToLowerInvariant(), from, target);
                }
                double loadingFrame = TransferTime(cannery, CityFishSupplyStage.LoadFinished, 0, .19f);
                cannery.ApplyAt(loadingFrame);
                yield return CaptureCannery(camera, city, cannery, loadingFrame, "inspection-approved-carton-loading",
                    plan.World(new Vector3(4.7f, 2.2f, 5.1f)), cannery.FinishedBox(0).position + Vector3.up * .45f);
                double heldTime = InspectionTime(cannery, CityCanneryInspectionStage.CarryToScale, 0, .5d);
                cannery.AutoAdvance = true;
                using (GameTimeScaleRuntime.AcquirePause())
                {
                    cannery.ApplyAt(heldTime);
                    yield return null;
                    Vector3 box = cannery.FinishedBox(0).position;
                    Vector3 hand = cannery.GetFactoryWorker(0).RightGrip.position;
                    Quaternion head = cannery.GetFactoryWorker(0).Head.rotation;
                    int face = cannery.Receiver.CurrentFaceCell;
                    Vector3 glasses = cannery.Receiver.GlassesRoot.position;
                    double life = cannery.LifeSeconds;
                    yield return null;
                    yield return null;
                    DeferCanneryContract(failures, "pause holds carried box, hands, nod and clocks", () =>
                    {
                        Assert.That(cannery.WorkingSeconds, Is.EqualTo(heldTime));
                        Assert.That(cannery.LifeSeconds, Is.EqualTo(life));
                        Assert.That(cannery.FinishedBox(0).position, Is.EqualTo(box));
                        Assert.That(cannery.GetFactoryWorker(0).RightGrip.position, Is.EqualTo(hand));
                        Assert.That(cannery.GetFactoryWorker(0).Head.rotation, Is.EqualTo(head));
                        Assert.That(cannery.Receiver.CurrentFaceCell, Is.EqualTo(face));
                        Assert.That(cannery.Receiver.GlassesRoot.position, Is.EqualTo(glasses));
                    });
                }
            }
            finally
            {
                cannery.AutoAdvance = false;
                cannery.FactoryConversation.Suspend();
                cannery.FactoryConversation.UseManualClock = manualSpeech;
                cannery.ApplyAt(savedWork);
                cannery.ApplyLifeAt(savedLife);
                cannery.AdvanceSounds(false);
                city.Player.Motor.Teleport(hero);
                CityFishSupplySession.ResetForNewGame();
            }
            if (failures.Count > 0) throw new AggregateException("Cannery inspection contracts failed.", failures);
            Debug.Log("CITY CANNERY INSPECTION OK: authored receiver, measured body/glasses, shared painted speech, continuous cartons, actual body clearance, restoration and pause.");
        }

        private static IEnumerator CaptureCanneryReceiver(Camera camera, CityGameRoot city,
            CityCanneryController cannery, List<Exception> failures)
        {
            var actor = cannery.GetFactoryWorker(0);
            var receiver = cannery.Receiver;
            var speech = cannery.FactoryConversation;
            var follow = camera.GetComponent<PlayerCameraFollow>();
            bool oldFollow = follow != null && follow.enabled;
            Vector3 oldCamera = camera.transform.position;
            Quaternion oldRotation = camera.transform.rotation;
            float oldFov = camera.fieldOfView;
            try
            {
                Assert.That(receiver, Is.Not.Null, "The existing receiving role owns the dedicated person.");
                Assert.That(receiver.Motion, Is.SameAs(actor));
                Assert.That(cannery.GetComponentsInChildren<CanneryReceiverPresentation>(true).Length, Is.EqualTo(1));
                Assert.That(receiver.IsFaceReady, Is.True);
                Assert.That(receiver.GlassesRoot, Is.Not.Null);
                Assert.That(receiver.GlassesRoot.IsChildOf(actor.ModelRoot), Is.True);
                if (follow != null) follow.enabled = false;
                cannery.AutoAdvance = false;
                cannery.ApplyAt(0d);
                cannery.ApplyLifeAt(10d);
                Material faceMaterial = receiver.FaceRenderer.sharedMaterial;
                Texture faceTexture = CanneryWomanFaceTexture(receiver.FaceRenderer);
                Assert.That(faceTexture, Is.Not.Null);
                Assert.That(faceTexture.width, Is.EqualTo(512));
                Assert.That(faceTexture.height, Is.EqualTo(256));
                Assert.That(faceTexture.filterMode, Is.EqualTo(FilterMode.Point));
                Renderer shirt = Array.Find(actor.GetComponentsInChildren<Renderer>(), value => value.name == "CLO_WorkTShirt");
                Assert.That(shirt, Is.Not.Null, "A continuous authored shirt carries the broad chest and tapered waist.");
                Texture shirtTexture = CanneryWomanFaceTexture(shirt);
                DeferCanneryContract(failures, "receiver garments are separate from the permanent body", () =>
                {
                    Assert.That(receiver.Wardrobe, Is.Not.Null);
                    receiver.Wardrobe.ValidateBindings();
                    Assert.That(receiver.Wardrobe.CurrentOutfitId, Is.EqualTo(CanneryReceiverPresentation.WorkwearId));
                    foreach (Renderer renderer in actor.GetComponentsInChildren<Renderer>())
                        Assert.That(receiver.Wardrobe.Owns(renderer), Is.EqualTo(
                            renderer.name.StartsWith("CLO_", StringComparison.Ordinal) ||
                            renderer.name.StartsWith("GEO_WorkBoot", StringComparison.Ordinal) ||
                            renderer.name.StartsWith("GEO_Boot", StringComparison.Ordinal)),
                            renderer.name + " wardrobe binding");
                    Assert.That(cannery.Woman.Wardrobe, Is.InstanceOf<CanneryWomanWardrobe>());
                    Assert.That(cannery.Woman.Wardrobe.CurrentOutfitId, Is.EqualTo(CanneryWomanWardrobe.WorkwearId));
                    cannery.Woman.Wardrobe.ValidateBindings();
                    Assert.That(cannery.Woman.Wardrobe.Garments.Count, Is.GreaterThan(0),
                        "The existing woman's serialized bindings survive inheritance by the shared wardrobe.");
                    foreach (NpcWardrobe.GarmentBinding garment in cannery.Woman.Wardrobe.Garments)
                    {
                        Assert.That(cannery.Woman.Wardrobe.Owns(garment.Renderer), Is.True);
                        Assert.That(CanneryWomanFaceTexture(garment.Renderer), Is.SameAs(cannery.Woman.Wardrobe.Atlas));
                    }
                    Assert.That(cannery.Woman.Wardrobe.Owns(cannery.Woman.FaceRenderer), Is.False);
                });
                DeferCanneryContract(failures, "receiver imported stature, body width and real spectacles", () =>
                {
                    Renderer[] renderers = actor.GetComponentsInChildren<Renderer>();
                    Bounds body = ReceiverMeshBounds(renderers, actor.transform);
                    Bounds anatomy = ReceiverMeshBounds(Array.FindAll(renderers, value =>
                        value.name != "CLO_SkiBeanie" && value.name != "CLO_BeanieCuff"), actor.transform);
                    Bounds torso = ReceiverMeshBounds(new[] { shirt }, actor.transform);
                    Bounds chest = ReceiverMeshBounds(new[] { shirt }, actor.transform, 1.39f, 1.54f);
                    Bounds waist = ReceiverMeshBounds(new[] { shirt }, actor.transform, 1.10f, 1.20f);
                    Assert.That(anatomy.size.y, Is.InRange(1.91f, 1.99f), "The body beneath the ski hat is about 196 cm tall.");
                    Assert.That(body.size.y, Is.InRange(1.95f, 2.12f), "The worn ski hat adds only its actual crown height.");
                    Assert.That(body.size.x, Is.GreaterThan(.68f), "Broad shoulders and substantial arms belong to the geometry.");
                    Assert.That(torso.size.x, Is.InRange(.47f, .52f), "Torso width stays compact; the deltoids carry the outer shoulder mass.");
                    Assert.That(waist.size.x, Is.InRange(.36f, .41f));
                    Assert.That(chest.size.x / waist.size.x, Is.GreaterThan(1.2f), "The actual dressed chest is broader than the waist.");
                    Assert.That(torso.size.z, Is.InRange(.32f, .37f), "The narrower chest retains volume without a round abdomen.");
                    Renderer beanie = Array.Find(renderers, value => value.name == "CLO_SkiBeanie");
                    Renderer cuff = Array.Find(renderers, value => value.name == "CLO_BeanieCuff");
                    Assert.That(beanie, Is.Not.Null); Assert.That(cuff, Is.Not.Null);
                    Assert.That(receiver.Wardrobe.Owns(beanie) && receiver.Wardrobe.Owns(cuff), Is.True);
                    Renderer[] glasses = Array.FindAll(actor.GetComponentsInChildren<Renderer>(),
                        value => value.name.StartsWith("GEO_Glasses", StringComparison.Ordinal));
                    Assert.That(glasses.Length, Is.GreaterThanOrEqualTo(5), "Separate rims, bridge and temples are modeled.");
                    Bounds frames = ReceiverMeshBounds(glasses, actor.transform);
                    Assert.That(frames.size.x, Is.GreaterThan(.15f));
                    Assert.That(frames.size.z, Is.GreaterThan(.10f), "Temples make the spectacles read in profile.");
                    Debug.Log($"CANNERY RECEIVER DIMENSIONS: dressed {body.size:F3} m, chest/waist {chest.size.x:F3}/{waist.size.x:F3} m, spectacles {frames.size:F3} m.");
                });
                DeferCanneryContract(failures, "receiver relaxed hands have forward thumbs and inward palms", () =>
                {
                    foreach (string side in new[] { "L", "R" })
                    {
                        CanneryHandSurface hand = ReadCanneryHand(actor, side);
                        AssertCanneryHandDirections(hand, -Vector3.up,
                            Vector3.ProjectOnPlane(actor.transform.position - hand.Center, Vector3.up),
                            actor.transform.forward, "neutral " + side);
                    }
                });
                yield return View("receiver-00-ordinary-distance", 4.1f, 0f, false);
                yield return View("receiver-00-athletic-three-quarter", 3.9f, 32f, false);
                yield return HandsView("receiver-08-neutral-hands");
                yield return View("receiver-01-face-front", 1.2f, 0f, true);
                yield return View("receiver-02-face-three-quarter", 1.25f, 35f, true);
                yield return View("receiver-03-profile", 1.25f, 82f, true);
                yield return HeightComparison();

                Transform leftFoot = CityPedestrianHandProps.FindSocket(actor.ModelRoot, "foot.L");
                Transform rightFoot = CityPedestrianHandProps.FindSocket(actor.ModelRoot, "foot.R");
                Vector3 left = leftFoot.position, right = rightFoot.position;
                var quietCells = new HashSet<int>();
                float footDrift = 0f;
                for (int sample = 0; sample <= 100; sample++)
                {
                    cannery.ApplyLifeAt(10d + sample * .05d);
                    quietCells.Add(receiver.CurrentFaceCell);
                    footDrift = Mathf.Max(footDrift, Vector3.Distance(left, leftFoot.position), Vector3.Distance(right, rightFoot.position));
                }
                DeferCanneryContract(failures, "receiver blinks while its idle feet stay planted", () =>
                {
                    Assert.That(quietCells.Count, Is.GreaterThan(1));
                    Assert.That(footDrift, Is.LessThan(.025f));
                });

                cannery.ApplyLifeAt(18d);
                Assert.That(cannery.FactoryWorkerHandsFree(0), Is.True);
                cannery.ApplyLifeAt(24.5d); // (life + 17) % 37 = 4.5: the existing rest task is fully weighted.
                DeferCanneryContract(failures, "outside rest adjusts the actual glasses before admitting speech", () =>
                {
                    Vector3 bridge = receiver.GlassesRoot.position + actor.transform.forward * .012f;
                    Assert.That(Vector3.Distance(cannery.ReceiverGlassesHandContact, bridge), Is.LessThan(.015f));
                    Transform wrist = CityPedestrianHandProps.FindSocket(actor.ModelRoot, "hand.R");
                    Transform elbow = CityPedestrianHandProps.FindSocket(actor.ModelRoot, "forearm.R");
                    Assert.That(elbow.position.y, Is.LessThan(wrist.position.y - .10f), "The elbow stays below the raised hand.");
                    Assert.That(Vector3.Dot((cannery.ReceiverGlassesHandContact - wrist.position).normalized, Vector3.up),
                        Is.GreaterThan(.6f), "Fingers rise toward the glasses instead of pointing into the face.");
                    CanneryHandSurface hand = ReadCanneryHand(actor, "R");
                    AssertCanneryHandDirections(hand, Vector3.up,
                        Vector3.ProjectOnPlane(actor.Head.position - hand.Center, Vector3.up),
                        actor.transform.right, "glasses R");
                    Assert.That(cannery.InspectionBoxInHands, Is.False);
                    Assert.That(cannery.FactoryWorkerHandsFree(0), Is.False);
                    Assert.That(cannery.FactoryWorkerAvailableSeconds(0), Is.Zero);
                    Assert.That(speech.HasExchange, Is.False);
                });
                yield return View("receiver-07-glasses-adjustment", 1.75f, 35f, true);
                cannery.ApplyLifeAt(30d);
                Assert.That(cannery.FactoryWorkerHandsFree(0), Is.True, "The completed adjustment releases its hand reservation.");

                speech.Initialize(cannery, city.Player.GameObject.transform, 84);
                speech.UseManualClock = true;
                city.Player.Motor.Teleport(cannery.Plan.World(new Vector3(4.5f, .3f, -1.8f)));
                bool captured = false, paused = false;
                var mouths = new HashSet<SpeechMouthPose>();
                double start = cannery.LifeSeconds + 10d;
                for (int sample = 0; sample < 2500 && (!captured || !paused || mouths.Count < 3); sample++)
                {
                    double life = start + sample * .1d;
                    cannery.ApplyLifeAt(life);
                    speech.ApplyAt();
                    cannery.ApplyLifeAt(life);
                    if (speech.Bubbles.TryGetSpeechFaceSample(actor, out SpeechFaceSample delivery))
                    {
                        mouths.Add(receiver.CurrentSpeechFace.Mouth);
                        if (delivery.IsTyping && !paused)
                        {
                            paused = true;
                            int cell = receiver.CurrentFaceCell;
                            Quaternion head = actor.Head.rotation;
                            Vector3 hand = actor.RightGrip.position;
                            using (GameTimeScaleRuntime.AcquirePause())
                            {
                                yield return null; yield return null;
                                DeferCanneryContract(failures, "pause freezes the receiver's shared speech and gesture", () =>
                                {
                                    Assert.That(receiver.CurrentFaceCell, Is.EqualTo(cell));
                                    Assert.That(actor.Head.rotation, Is.EqualTo(head));
                                    Assert.That(actor.RightGrip.position, Is.EqualTo(hand));
                                    Assert.That(speech.Bubbles.TryGetSpeechFaceSample(actor, out SpeechFaceSample frozen), Is.True);
                                    Assert.That(frozen.ElapsedSeconds, Is.EqualTo(delivery.ElapsedSeconds));
                                });
                            }
                        }
                        if (!delivery.IsTyping && delivery.RevealedCharacters == delivery.Text.Length && !captured)
                        {
                            captured = true;
                            yield return CaptureCannerySpeech(camera, city, cannery, "receiver-05-shared-speech",
                                actor.Head.position + actor.transform.forward * 1.55f + Vector3.up * .08f,
                                actor.Head.position);
                        }
                    }
                    if (sample % 40 == 0) yield return null;
                }
                DeferCanneryContract(failures, "receiver speaks through the existing factory channel", () =>
                {
                    Assert.That(captured && paused, Is.True);
                    Assert.That(mouths.Count, Is.GreaterThan(2), "Shared reveal selects different painted mouths.");
                });
                city.Player.Motor.Teleport(cannery.Plan.World(new Vector3(40f, .3f, 0f)));
                speech.ApplyAt();
                cannery.ApplyLifeAt(cannery.LifeSeconds);
                Assert.That(speech.HasExchange, Is.False);
                actor.gameObject.SetActive(false);
                actor.gameObject.SetActive(true);
                cannery.Woman.gameObject.SetActive(false);
                cannery.Woman.gameObject.SetActive(true);
                cannery.ApplyLifeAt(cannery.LifeSeconds + 10d);
                DeferCanneryContract(failures, "receiver wake retains its own atlas, clothes and glasses", () =>
                {
                    Assert.That(receiver.IsFaceReady, Is.True);
                    Assert.That(receiver.FaceRenderer.sharedMaterial, Is.SameAs(faceMaterial));
                    Assert.That(CanneryWomanFaceTexture(receiver.FaceRenderer), Is.SameAs(faceTexture));
                    Assert.That(CanneryWomanFaceTexture(shirt), Is.SameAs(shirtTexture));
                    Assert.That(receiver.GlassesRoot.gameObject.activeInHierarchy, Is.True);
                    cannery.Woman.Wardrobe.ValidateBindings();
                    foreach (NpcWardrobe.GarmentBinding garment in cannery.Woman.Wardrobe.Garments)
                    {
                        Assert.That(garment.Renderer.enabled, Is.True);
                        Assert.That(CanneryWomanFaceTexture(garment.Renderer), Is.SameAs(cannery.Woman.Wardrobe.Atlas),
                            "The inherited wardrobe restores the existing woman's authored clothing on wake.");
                    }
                });
                yield return View("receiver-06-restored-after-distance", 3.5f, 25f, false);
                speech.Suspend();
                cannery.ApplyLifeAt(100d);
                cannery.ApplyAt(InspectionTime(cannery, CityCanneryInspectionStage.CarryToScale, 0, .88d));
                DeferCanneryContract(failures, "receiver carries with inward palms and upward thumbs", () =>
                {
                    Assert.That(cannery.InspectionBoxInHands, Is.True);
                    foreach (string side in new[] { "L", "R" })
                    {
                        CanneryHandSurface hand = ReadCanneryHand(actor, side);
                        AssertCanneryHandDirections(hand, actor.transform.forward,
                            Vector3.ProjectOnPlane(cannery.FinishedBox(0).position - hand.Center, actor.transform.forward),
                            Vector3.up, "carry " + side);
                    }
                });
                yield return HandsView("receiver-09-carry-hands");
            }
            finally
            {
                speech.Suspend();
                camera.transform.SetPositionAndRotation(oldCamera, oldRotation);
                camera.fieldOfView = oldFov;
                if (follow != null) follow.enabled = oldFollow;
            }

            IEnumerator View(string name, float distance, float angle, bool portrait)
            {
                Vector3 target = portrait ? actor.Head.position + Vector3.up * .025f :
                    actor.transform.position + Vector3.up * 1.04f;
                Vector3 direction = Quaternion.AngleAxis(angle, Vector3.up) * actor.transform.forward;
                Vector3 from = target + direction * distance + Vector3.up * (portrait ? .01f : .18f);
                city.Player.Motor.Teleport(from - Vector3.up * EyeHeight);
                camera.transform.SetPositionAndRotation(from, Quaternion.LookRotation(target - from));
                camera.fieldOfView = portrait ? 34f : 48f;
                Physics.SyncTransforms();
                yield return null; yield return null;
                CaptureCurrentCamera(camera, "CityCannery", name);
            }

            IEnumerator HandsView(string name)
            {
                Vector3 target = (actor.LeftGrip.position + actor.RightGrip.position) * .5f;
                Vector3 from = target + actor.transform.forward * 1.4f + actor.transform.right * .55f + Vector3.up * .30f;
                city.Player.Motor.Teleport(from - Vector3.up * EyeHeight);
                camera.transform.SetPositionAndRotation(from, Quaternion.LookRotation(target - from));
                camera.fieldOfView = 40f;
                Physics.SyncTransforms();
                yield return null; yield return null;
                CaptureCurrentCamera(camera, "CityCannery", name);
            }

            IEnumerator HeightComparison()
            {
                Transform hero = city.Player.GameObject.transform;
                Transform woman = cannery.GetFactoryWorker(2).transform;
                Vector3 heroBefore = hero.position, womanBefore = woman.position;
                Quaternion heroRotation = hero.rotation, womanRotation = woman.rotation;
                Renderer[] heroRenderers = new List<Renderer>(city.Player.Visual.Renderers).ToArray();
                var visibility = Array.ConvertAll(heroRenderers, renderer => renderer.enabled);
                try
                {
                    city.Player.Motor.Teleport(actor.transform.position + actor.transform.right * .95f +
                        Vector3.up * PlayerFactory.GroundedRootOffset);
                    hero.rotation = actor.transform.rotation;
                    city.Player.Visual.SetMotion(PlayerMotionSample.Stationary);
                    woman.SetPositionAndRotation(actor.transform.position - actor.transform.right * .9f, actor.transform.rotation);
                    foreach (Renderer renderer in heroRenderers) renderer.enabled = true;
                    Vector3 target = actor.transform.position + Vector3.up * 1f;
                    Vector3 from = target + actor.transform.forward * 4.8f + Vector3.up * .1f;
                    camera.transform.SetPositionAndRotation(from, Quaternion.LookRotation(target - from));
                    camera.fieldOfView = 42f;
                    yield return null; yield return null;
                    float receiverHeight = MeasureVisibleHeight(actor.GetComponentsInChildren<Renderer>());
                    float heroHeight = MeasureVisibleHeight(heroRenderers);
                    float womanHeight = MeasureVisibleHeight(woman.GetComponentsInChildren<Renderer>());
                    DeferCanneryContract(failures, "receiver height beside actual hero and woman", () =>
                    {
                        Assert.That(receiverHeight - heroHeight, Is.InRange(.13f, .40f));
                        Assert.That(receiverHeight - womanHeight, Is.InRange(.27f, .50f));
                    });
                    CaptureCurrentCamera(camera, "CityCannery", "receiver-04-height-beside-hero-and-woman");
                }
                finally
                {
                    for (int i = 0; i < heroRenderers.Length; i++) heroRenderers[i].enabled = visibility[i];
                    city.Player.Motor.Teleport(heroBefore); hero.rotation = heroRotation;
                    woman.SetPositionAndRotation(womanBefore, womanRotation);
                }
            }
        }

        private static IEnumerator CaptureCanneryWomanHands(Camera camera, CityGameRoot city,
            CityCanneryController cannery, List<Exception> failures)
        {
            VillageResidentPresentation actor = cannery.GetFactoryWorker(CanneryWomanPresentation.WorkerSlot);
            var follow = camera.GetComponent<PlayerCameraFollow>();
            bool oldFollow = follow != null && follow.enabled;
            Vector3 oldPosition = camera.transform.position;
            Quaternion oldRotation = camera.transform.rotation;
            float oldFov = camera.fieldOfView;
            try
            {
                if (follow != null) follow.enabled = false;
                cannery.FactoryConversation.Suspend();
                cannery.AutoAdvance = false;
                cannery.ApplyLifeAt(10d);
                foreach (CityCanneryProductionStage? stage in new CityCanneryProductionStage?[]
                    { null, CityCanneryProductionStage.Fill, CityCanneryProductionStage.Seal })
                {
                    cannery.ApplyAt(stage.HasValue ? CanneryTime(cannery, stage.Value, .45f) : 0d);
                    string pose = stage.HasValue ? stage.Value.ToString().ToLowerInvariant() : "neutral";
                    DeferCanneryContract(failures, "woman hand anatomy during " + pose, () =>
                    {
                        foreach (string side in new[] { "L", "R" })
                            AssertCanneryHandDirections(ReadCanneryHand(actor, side), -Vector3.up,
                                actor.transform.right * (side == "L" ? 1f : -1f), actor.transform.forward,
                                "woman " + pose + " " + side);
                        Assert.That(cannery.WorkerHandsMatch, Is.True, cannery.LastCrewContactFailure);
                    });
                    Vector3 target = (actor.LeftGrip.position + actor.RightGrip.position) * .5f;
                    Vector3 from = stage.HasValue
                        ? target + Quaternion.AngleAxis(105f, Vector3.up) * actor.transform.forward * 1.3f + Vector3.up * .55f
                        : target + actor.transform.forward * 1.3f + actor.transform.right * .55f + Vector3.up * .25f;
                    city.Player.Motor.Teleport(from - Vector3.up * EyeHeight);
                    camera.transform.SetPositionAndRotation(from, Quaternion.LookRotation(target - from));
                    camera.fieldOfView = 40f;
                    Physics.SyncTransforms();
                    yield return null; yield return null;
                    CaptureCurrentCamera(camera, "CityCannery", "woman-14-hands-" + pose);
                }
            }
            finally
            {
                camera.transform.SetPositionAndRotation(oldPosition, oldRotation);
                camera.fieldOfView = oldFov;
                if (follow != null) follow.enabled = oldFollow;
            }
        }

        private readonly struct CanneryHandSurface
        {
            public readonly Vector3 Center, Fingers, Palm, Thumb;
            public CanneryHandSurface(Vector3 center, Vector3 fingers, Vector3 palm, Vector3 thumb)
            { Center = center; Fingers = fingers; Palm = palm; Thumb = thumb; }
        }

        private static CanneryHandSurface ReadCanneryHand(VillageResidentPresentation actor, string side)
        {
            using var probe = new ScarfContactProbe { Origin = actor.transform.position };
            Renderer[] renderers = actor.GetComponentsInChildren<Renderer>();
            Vector3 palm = Center("GEO_Palm." + side), thumb = Center("GEO_Thumb." + side);
            var fingers = new Vector3[4];
            Vector3 average = Vector3.zero;
            for (int i = 0; i < fingers.Length; i++)
            { fingers[i] = Center("GEO_Finger" + i + "." + side); average += fingers[i] * .25f; }
            Transform wrist = CityPedestrianHandProps.FindSocket(actor.ModelRoot, "hand." + side);
            Assert.That(wrist, Is.Not.Null);
            Vector3 wristPoint = wrist.position - probe.Origin;
            Vector3 distal = (palm - wristPoint).normalized;
            Vector3 radial = Vector3.ProjectOnPlane(fingers[0] - fingers[3], distal).normalized;
            Vector3 normal = Vector3.Cross(distal, radial).normalized;
            // The fingers visibly curl toward the palm. Their actual surface
            // centroids choose its sign, independently of side labels, authored
            // direction anchors and the runtime's cached hand orientation.
            Vector3 curl = Vector3.ProjectOnPlane(average - palm, distal);
            Assert.That(Mathf.Abs(Vector3.Dot(curl, normal)), Is.GreaterThan(.003f),
                side + " fingers expose the palmar side of the real geometry.");
            if (Vector3.Dot(curl, normal) < 0f) normal = -normal;
            Vector3 thumbSide = Vector3.ProjectOnPlane(Vector3.ProjectOnPlane(thumb - palm, distal), normal).normalized;
            Assert.That(Vector3.Dot(thumbSide, radial), Is.GreaterThan(.8f),
                side + " thumb is beside the index finger, with the little finger on the opposite edge.");
            return new CanneryHandSurface(palm + probe.Origin, (average - wristPoint).normalized, normal, thumbSide);

            Vector3 Center(string name)
            {
                Renderer renderer = Array.Find(renderers, value => value.name == name);
                Assert.That(renderer, Is.Not.Null, "Measure the actual hand part " + name);
                ScarfContactSurface surface = probe.Read(renderer);
                Vector3 sum = Vector3.zero;
                float area = 0f;
                // Triangle area weighting does not depend on FBX seam splits
                // or how many times a shared vertex was duplicated at import.
                for (int i = 0; i < surface.Triangles.Length; i += 3)
                {
                    Vector3 a = surface.Vertices[surface.Triangles[i]], b = surface.Vertices[surface.Triangles[i + 1]],
                        c = surface.Vertices[surface.Triangles[i + 2]];
                    float weight = Vector3.Cross(b - a, c - a).magnitude;
                    sum += (a + b + c) * (weight / 3f);
                    area += weight;
                }
                Assert.That(area, Is.GreaterThan(.0001f), name);
                return sum / area;
            }
        }

        private static void AssertCanneryHandDirections(CanneryHandSurface hand, Vector3 fingers,
            Vector3 palm, Vector3 thumb, string pose)
        {
            float distal = Vector3.Dot(hand.Fingers, fingers.normalized),
                palmar = Vector3.Dot(hand.Palm, palm.normalized), radial = Vector3.Dot(hand.Thumb, thumb.normalized);
            Debug.Log($"CANNERY HAND: {pose}, fingers {distal:F3}, palm {palmar:F3}, thumb {radial:F3}.");
            Assert.That(distal, Is.GreaterThan(.65f), pose + ": the rendered fingers point along the intended gesture.");
            Assert.That(palmar, Is.GreaterThan(.65f), pose + ": the rendered palm faces its contact or the body in rest.");
            Assert.That(radial, Is.GreaterThan(.65f), pose + ": the real thumb stays on the anatomical side of the hand.");
        }

        private static Bounds ReceiverMeshBounds(IEnumerable<Renderer> renderers, Transform localRoot,
            float minimumY = float.MinValue, float maximumY = float.MaxValue)
        {
            using var probe = new ScarfContactProbe();
            Bounds bounds = default;
            bool any = false;
            foreach (Renderer renderer in renderers)
            {
                if (!renderer.enabled || !renderer.gameObject.activeInHierarchy) continue;
                ScarfContactSurface surface = probe.Read(renderer);
                foreach (Vector3 world in surface.Vertices)
                {
                    Vector3 point = localRoot.InverseTransformPoint(world);
                    if (point.y < minimumY || point.y > maximumY) continue;
                    if (!any) bounds = new Bounds(point, Vector3.zero);
                    else bounds.Encapsulate(point);
                    any = true;
                }
            }
            Assert.That(any, Is.True, "Measure the imported visible vertices, not the skin culling bounds.");
            return bounds;
        }

        private static double InspectionTime(CityCanneryController cannery, CityCanneryInspectionStage phase,
            int unit, double progress, long batch = 0) => cannery.Cycle.InspectionPhaseStart(phase, unit, batch) +
                cannery.Cycle.InspectionPhaseDuration(phase, unit) * progress;

        private static void AssertCanneryShippingScale(CityCanneryController cannery)
        {
            Transform scale = CityCanneryAssetProvider.FindPart(cannery.Equipment.gameObject, "ShippingScale");
            Assert.That(scale, Is.Not.Null, "The complete authored weighing equipment moves together.");
            bool any = false;
            foreach (MeshFilter mesh in scale.GetComponentsInChildren<MeshFilter>(true))
                foreach (Vector3 vertex in mesh.sharedMesh.vertices)
                {
                    Vector3 local = cannery.Plan.Local(mesh.transform.TransformPoint(vertex));
                    Assert.That(local.x, Is.GreaterThan(.12f), "Every part of the scales lies beyond the hall wall.");
                    Assert.That(local.z, Is.GreaterThan(6.1f), "The shipping door and its ramp remain clear.");
                    Assert.That(local.x, Is.LessThan(2f), "Scale stays in its facade pocket, away from the truck lane.");
                    any = true;
                }
            Assert.That(any, Is.True);
            Vector3 load = cannery.Plan.Local(cannery.ShippingScaleLoadPosition);
            Assert.That(load.x, Is.GreaterThan(.12f));
            Assert.That(load.z, Is.InRange(6.1f, 8f));
        }

        private static void AssertCanneryCartonInspection(CityCanneryController cannery, Transform[] cartons,
            int unit, long batch)
        {
            var actor = cannery.GetFactoryWorker(0);
            double packed = cannery.Cycle.ProductionStageStart(CityCanneryProductionStage.Pack, unit, batch) +
                cannery.Cycle.ProductionStageDuration(CityCanneryProductionStage.Pack);
            Assert.That(cannery.Cycle.InspectionStart(unit, batch), Is.GreaterThanOrEqualTo(packed));
            foreach (CityCanneryInspectionStage phase in Enum.GetValues(typeof(CityCanneryInspectionStage)))
            {
                if (phase == CityCanneryInspectionStage.Idle) continue;
                double start = cannery.Cycle.InspectionPhaseStart(phase, unit, batch);
                cannery.ApplyAt(start - .0001d);
                Vector3 before = cartons[unit].position, receiverBefore = actor.transform.position;
                cannery.ApplyAt(start + .0001d);
                Assert.That(Vector3.Distance(cartons[unit].position, before), Is.LessThan(.025f), phase + " carton boundary");
                Assert.That(Vector3.Distance(actor.transform.position, receiverBefore), Is.LessThan(.025f), phase + " receiver boundary");
                foreach (double progress in new[] { .15d, .5d, .85d })
                {
                    cannery.ApplyAt(InspectionTime(cannery, phase, unit, progress, batch));
                    Assert.That(cannery.Snapshot.Inspection.Stage, Is.EqualTo(phase));
                    Assert.That(cannery.Snapshot.Inspection.UnitIndex, Is.EqualTo(unit));
                    Assert.That(cannery.Snapshot.AccountedUnits, Is.EqualTo(3));
                    Assert.That(cannery.Snapshot.TruckCases, Is.Zero);
                    Assert.That(cannery.FinishedBox(unit), Is.SameAs(cartons[unit]));
                    Assert.That(cartons[unit].gameObject.activeInHierarchy, Is.True);
                    Assert.That(cannery.FactoryWorkerHandsFree(0), Is.False, "Inspection owns the receiver's gestures.");
                    Assert.That(cannery.WorkerHandsMatch, Is.True, cannery.LastCrewContactFailure);
                    if (cannery.InspectionBoxInHands)
                    {
                        Assert.That(Vector3.Distance(actor.RightGrip.position, cannery.InspectionRightHandTarget), Is.LessThan(.04f));
                        Assert.That(Vector3.Distance(actor.LeftGrip.position, cannery.InspectionLeftHandTarget), Is.LessThan(.04f));
                        Bounds body = InspectionRenderedBounds(cartons[unit]);
                        Assert.That(body.SqrDistance(actor.RightGrip.position), Is.LessThan(.07f * .07f));
                        Assert.That(body.SqrDistance(actor.LeftGrip.position), Is.LessThan(.07f * .07f));
                    }
                    if (phase == CityCanneryInspectionStage.Settle || phase == CityCanneryInspectionStage.Approve)
                    {
                        Assert.That(cannery.InspectionBoxInHands, Is.False, "The platform supports the released carton.");
                        Assert.That(Vector3.Distance(cartons[unit].position, cannery.ShippingScaleLoadPosition), Is.LessThan(.01f));
                        Assert.That(cannery.ShippingScaleWeight, Is.GreaterThan(.5f));
                        Assert.That(cannery.Snapshot.Inspection.ApprovedUnits, Is.EqualTo(unit));
                    }
                    if (phase == CityCanneryInspectionStage.CarryToReady || phase == CityCanneryInspectionStage.PutAway ||
                        phase == CityCanneryInspectionStage.Clear)
                        Assert.That(cannery.ShippingScaleWeight, Is.Zero, "A removed box leaves the scale unloaded.");
                }
            }
            cannery.ApplyAt(InspectionTime(cannery, CityCanneryInspectionStage.Approve, unit, .01d, batch));
            Quaternion headBefore = actor.Head.rotation;
            cannery.ApplyAt(InspectionTime(cannery, CityCanneryInspectionStage.Approve, unit, .5d, batch));
            Assert.That(Quaternion.Angle(headBefore, actor.Head.rotation), Is.GreaterThan(2f), "Visible approval uses the existing receiver's head.");
            Bounds carton = InspectionRenderedBounds(cartons[unit]);
            Assert.That(carton.size.y, Is.InRange(.25f, .45f), "One carton replaces the old fused stack.");
            int count = 0;
            foreach (Transform part in cannery.GetComponentsInChildren<Transform>(true))
                if (part.name.StartsWith("Finished handling unit ", StringComparison.Ordinal)) count++;
            Assert.That(count, Is.EqualTo(3), "Finite box identities include hidden objects.");
        }

        private static Bounds InspectionRenderedBounds(Transform root)
        {
            Bounds bounds = default;
            bool found = false;
            foreach (Renderer renderer in root.GetComponentsInChildren<Renderer>(true))
            {
                if (!renderer.enabled || !renderer.gameObject.activeInHierarchy) continue;
                if (!found) bounds = renderer.bounds;
                else bounds.Encapsulate(renderer.bounds);
                found = true;
            }
            Assert.That(found, Is.True, root.name + " has real rendered geometry.");
            return bounds;
        }

        private static void AssertCanneryInspectionRoute(CityGameRoot city, CityCanneryController cannery)
        {
            var solids = new HashSet<Collider>(cannery.Equipment.GetComponentsInChildren<Collider>(true));
            foreach (Transform root in city.World.DistrictPointOfInterestRoot.GetComponentsInChildren<Transform>(true))
                if (root.name == "Industrial Cannery" && root.Find("Hall") != null)
                    foreach (Collider solid in root.GetComponentsInChildren<Collider>(true)) solids.Add(solid);
            var faults = new Dictionary<string, string>();
            var actor = cannery.GetFactoryWorker(0);
            using var probe = new ScarfContactProbe { Origin = cannery.Plan.Origin };
            var collisionSurfaces = new Dictionary<Collider, ReceiverCollisionSurface>();
            foreach (Collider solid in solids)
            {
                if (solid.isTrigger) continue;
                if (solid is BoxCollider)
                {
                    collisionSurfaces.Add(solid, null);
                    continue;
                }
                Assert.That(solid, Is.InstanceOf<MeshCollider>(), "Supported authored route collision: " + solid.name);
                Renderer renderer = solid.GetComponent<Renderer>();
                Assert.That(renderer, Is.Not.Null, "The authored collision mesh can be measured: " + solid.name);
                ScarfContactSurface surface = probe.Read(renderer);
                var measured = new ReceiverCollisionSurface(surface);
                collisionSurfaces.Add(solid, measured);
                if (solid.name == "COL_HallWalls")
                {
                    Assert.That(measured.Contains(cannery.Plan.World(new Vector3(-8f, 1.2f, -3f)) - probe.Origin), Is.True,
                        "The actual west wall remains solid.");
                    Assert.That(measured.Contains(cannery.Plan.World(new Vector3(-4f, 1.2f, 7f)) - probe.Origin), Is.True,
                        "The actual north wall remains solid.");
                    Assert.That(measured.Contains(cannery.Plan.World(new Vector3(0f, 1.2f, -5.5f)) - probe.Origin), Is.False,
                        "The receiving doorway remains open.");
                    Assert.That(measured.Contains(cannery.Plan.World(new Vector3(-4f, 1.2f, 0f)) - probe.Origin), Is.False,
                        "The combined hall bounds do not fill its empty room.");
                }
            }
            // A stock 29 cm capsule misses this person's broad shoulders and
            // substantial chest. Read the posed imported body against the real
            // closed site solids; intentional hand-to-carton contacts are not
            // site obstacles. Culling bounds are never anatomy.
            Renderer[] body = Array.FindAll(actor.GetComponentsInChildren<Renderer>(), value =>
                value.name.StartsWith("CLO_", StringComparison.Ordinal) || value.name.StartsWith("GEO_", StringComparison.Ordinal));
            cannery.ApplyAt(0d);
            SampleRange(cannery.FactoryShiftEntryTime(0), cannery.FactoryShiftEntryTime(0) + cannery.FactoryShiftWalkDuration(0), "shift entry");
            SampleRange(cannery.Cycle.StageStart(CityFishSupplyStage.UnloadFish),
                cannery.Cycle.StageStart(CityFishSupplyStage.WaitForProduction), "receiving");
            for (int unit = 0; unit < 3; unit++)
            {
                double start = cannery.Cycle.InspectionStart(unit), end = start + cannery.Cycle.InspectionPlan.UnitDuration(unit);
                SampleRange(start, end, "inspection " + unit);
            }
            SampleRange(cannery.FactoryShiftExitTime(0), cannery.Cycle.StageStart(CityFishSupplyStage.FactoryToShop), "shift exit");
            Assert.That(faults, Is.Empty, string.Join("\n", faults.Values));

            void SampleRange(double start, double end, string phase)
            {
                for (double time = start + .05d; time < end; time += .6d)
                {
                    cannery.ApplyAt(time);
                    foreach (Renderer renderer in body)
                    {
                        if (!renderer.enabled || !renderer.gameObject.activeInHierarchy) continue;
                        ScarfContactSurface skin = probe.Read(renderer);
                        foreach (var pair in collisionSurfaces)
                        {
                            Collider solid = pair.Key;
                            ReceiverCollisionSurface obstacle = pair.Value;
                            Bounds obstacleBounds = obstacle != null ? obstacle.Bounds :
                                new Bounds(solid.bounds.center - probe.Origin, solid.bounds.size);
                            if (!solid.enabled || !solid.gameObject.activeInHierarchy ||
                                solid.bounds.max.y <= actor.transform.position.y + .055f ||
                                !skin.Bounds.Intersects(obstacleBounds)) continue;
                            string key = renderer.name + " / " + solid.name;
                            if (faults.ContainsKey(key)) continue;
                            foreach (Vector3 point in skin.Vertices)
                            {
                                bool inside = obstacle != null ? obstacle.Contains(point) :
                                    ReceiverBoxContains((BoxCollider)solid, point + probe.Origin);
                                if (!inside) continue;
                                faults.Add(key, phase + ": " + key + " at " + time.ToString("F2") + ", " +
                                    cannery.Snapshot.Inspection.Stage + " " + cannery.Snapshot.Inspection.Progress.ToString("F3") + ", actor " +
                                    cannery.Plan.Local(actor.transform.position).ToString("F3") + ", vertex " +
                                    cannery.Plan.Local(point + probe.Origin).ToString("F3"));
                                break;
                            }
                        }
                    }
                }
                Debug.Log("CANNERY RECEIVER ROUTE: " + phase + " checked against posed imported body surfaces.");
            }
        }

        private static bool ReceiverBoxContains(BoxCollider box, Vector3 worldPoint)
        {
            Vector3 local = box.transform.InverseTransformPoint(worldPoint) - box.center;
            Vector3 half = box.size * .5f, scale = box.transform.lossyScale;
            // Use the real oriented collider frame. An inset in world metres
            // excludes boundary contact, matching the shared mesh oracle.
            return Mathf.Abs(local.x) < half.x - .001f / Mathf.Abs(scale.x) &&
                Mathf.Abs(local.y) < half.y - .001f / Mathf.Abs(scale.y) &&
                Mathf.Abs(local.z) < half.z - .001f / Mathf.Abs(scale.z);
        }

        private sealed class ReceiverCollisionSurface
        {
            private const float ContactInset = .001f;
            private static readonly Vector3 Direction = new Vector3(.734971f, .452313f, .504871f).normalized;
            private readonly ScarfContactSurface mesh;
            private readonly int[] faces;
            private readonly Node root;
            public Bounds Bounds => mesh.Bounds;

            private sealed class Node
            {
                public Bounds Bounds;
                public int First, Count;
                public Node Left, Right;
            }

            public ReceiverCollisionSurface(ScarfContactSurface surface)
            {
                mesh = surface;
                // The actual Hall FBX joins closed chamfered piers/lintels.
                // Welding their coincident edges gives valence four, although
                // each directed boundary still cancels. Requiring exactly two
                // incident faces incorrectly treats those real walls as open.
                AssertClosedBoundary();
                faces = new int[mesh.Triangles.Length / 3];
                for (int i = 0; i < faces.Length; i++) faces[i] = i;
                root = Build(0, faces.Length);
            }

            public bool Contains(Vector3 point)
            {
                if (!Bounds.Contains(point)) return false;
                int winding = 0;
                bool boundary = false;
                Trace(root, new Ray(point, Direction), ref winding, ref boundary);
                return !boundary && winding != 0;
            }

            private Node Build(int first, int count)
            {
                var node = new Node { First = first, Count = count, Bounds = mesh.TriangleBounds[faces[first]] };
                for (int i = first + 1; i < first + count; i++) node.Bounds.Encapsulate(mesh.TriangleBounds[faces[i]]);
                if (count <= 8) return node;
                Vector3 size = node.Bounds.size;
                int axis = size.x >= size.y && size.x >= size.z ? 0 : size.y >= size.z ? 1 : 2;
                Array.Sort(faces, first, count, Comparer<int>.Create((a, b) =>
                    mesh.TriangleBounds[a].center[axis].CompareTo(mesh.TriangleBounds[b].center[axis])));
                int leftCount = count / 2;
                node.Left = Build(first, leftCount);
                node.Right = Build(first + leftCount, count - leftCount);
                return node;
            }

            private void Trace(Node node, Ray ray, ref int winding, ref bool boundary)
            {
                bool near = node.Bounds.SqrDistance(ray.origin) <= ContactInset * ContactInset;
                bool crosses = node.Bounds.IntersectRay(ray);
                if (boundary || !near && !crosses) return;
                if (node.Left != null)
                {
                    Trace(node.Left, ray, ref winding, ref boundary);
                    Trace(node.Right, ray, ref winding, ref boundary);
                    return;
                }
                for (int i = node.First; i < node.First + node.Count; i++)
                {
                    int face = faces[i] * 3;
                    Vector3 a = mesh.Vertices[mesh.Triangles[face]], b = mesh.Vertices[mesh.Triangles[face + 1]],
                        c = mesh.Vertices[mesh.Triangles[face + 2]];
                    Vector3 ab = b - a, ac = c - a, normal = Vector3.Cross(ab, ac);
                    float area = normal.sqrMagnitude;
                    if (area < 1e-16f) continue;
                    if (near)
                    {
                        float signed = Vector3.Dot(ray.origin - a, normal);
                        if (signed * signed <= ContactInset * ContactInset * area)
                        {
                            Vector3 projected = ray.origin - normal * (signed / area);
                            if (OnSide(a, b) && OnSide(b, c) && OnSide(c, a))
                            { boundary = true; return; }
                            bool OnSide(Vector3 start, Vector3 end) => Vector3.Dot(
                                Vector3.Cross(end - start, projected - start), normal) >=
                                -ContactInset * Mathf.Sqrt(area * (end - start).sqrMagnitude);
                        }
                    }
                    if (!crosses) continue;
                    Vector3 p = Vector3.Cross(ray.direction, ac);
                    float determinant = Vector3.Dot(ab, p);
                    if (Mathf.Abs(determinant) < 1e-10f) continue;
                    Vector3 offset = ray.origin - a;
                    float u = Vector3.Dot(offset, p) / determinant;
                    if (u < 0f || u > 1f) continue;
                    Vector3 q = Vector3.Cross(offset, ab);
                    float v = Vector3.Dot(ray.direction, q) / determinant;
                    if (v < 0f || u + v > 1f) continue;
                    if (Vector3.Dot(ac, q) / determinant <= 0f) continue;
                    // Signed crossings retain overlapping closed solids;
                    // ordinary odd/even ray parity would cancel their overlap.
                    winding += determinant < 0f ? 1 : -1;
                }
            }

            private void AssertClosedBoundary()
            {
                var welded = new Dictionary<Vector3Int, int>();
                var mapping = new int[mesh.Vertices.Length];
                for (int i = 0; i < mapping.Length; i++)
                {
                    Vector3 p = mesh.Vertices[i] * 100000f;
                    var key = new Vector3Int(Mathf.RoundToInt(p.x), Mathf.RoundToInt(p.y), Mathf.RoundToInt(p.z));
                    if (!welded.TryGetValue(key, out int id)) { id = welded.Count; welded.Add(key, id); }
                    mapping[i] = id;
                }
                var balances = new Dictionary<(int, int), int>();
                for (int i = 0; i < mesh.Triangles.Length; i += 3)
                {
                    int a = mapping[mesh.Triangles[i]], b = mapping[mesh.Triangles[i + 1]], c = mapping[mesh.Triangles[i + 2]];
                    if (a == b || b == c || c == a) continue;
                    Add(a, b); Add(b, c); Add(c, a);
                }
                Assert.That(balances.Count, Is.GreaterThan(0), mesh.Name);
                foreach (int balance in balances.Values)
                    Assert.That(balance, Is.Zero, "Authored collision has an unmatched directed boundary: " + mesh.Name);
                void Add(int a, int b)
                {
                    var key = a < b ? (a, b) : (b, a);
                    balances.TryGetValue(key, out int balance);
                    balances[key] = balance + (a < b ? 1 : -1);
                }
            }
        }

        private static void AssertCanneryInspectionRestore(CityGameRoot city, CityCanneryController cannery, CityPortController port)
        {
            double seek = InspectionTime(cannery, CityCanneryInspectionStage.CarryToScale, 1, .5d, 1);
            cannery.ApplyAt(seek);
            Vector3 box = cannery.FinishedBox(1).position, receiver = cannery.GetFactoryWorker(0).transform.position;
            Quaternion head = cannery.GetFactoryWorker(0).Head.rotation;
            int approved = cannery.Snapshot.Inspection.ApprovedUnits;
            cannery.ApplyAt(0d);
            cannery.ApplyAt(seek);
            Assert.That(cannery.FinishedBox(1).position, Is.EqualTo(box));
            Assert.That(cannery.GetFactoryWorker(0).Head.rotation, Is.EqualTo(head));
            var host = new GameObject("Cannery inspection cold reconstruction probe");
            host.SetActive(false);
            try
            {
                CityCanneryController other = CityCanneryController.Build(host.transform, city.Layout, port, city.Player.GameObject.transform);
                other.AutoAdvance = false;
                other.ForcePresentation = true;
                other.ApplyLifeAt(cannery.LifeSeconds);
                other.ApplyAt(seek);
                Assert.That(Vector3.Distance(other.FinishedBox(1).position, box), Is.LessThan(.001f));
                Assert.That(Vector3.Distance(other.GetFactoryWorker(0).transform.position, receiver), Is.LessThan(.001f));
                Assert.That(other.Snapshot.Inspection.ApprovedUnits, Is.EqualTo(approved));
                Assert.That(other.Snapshot.AccountedUnits, Is.EqualTo(3));
                Assert.That(other.Receiver.Motion, Is.SameAs(other.GetFactoryWorker(0)));
                Assert.That(other.Receiver.Wardrobe.CurrentOutfitId, Is.EqualTo(CanneryReceiverPresentation.WorkwearId));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(host);
                port.AutoAdvance = false;
                port.IsSupplyDriven = true;
                cannery.ApplyAt(seek);
            }
        }

        private static void AssertCanneryInspectedLoading(CityCanneryController cannery)
        {
            var supports = new List<Transform>();
            foreach (Transform part in cannery.GetComponentsInChildren<Transform>(true))
                if (part.name == "ShippingPallet") supports.Add(part);
            Assert.That(supports.Count, Is.EqualTo(3), "Each approved carton has one independent support.");
            var driver = cannery.transform.Find("Fish Delivery Driver").GetComponent<VillageResidentPresentation>();
            var scaleSolids = new HashSet<Collider>();
            foreach (Collider collider in cannery.Equipment.GetComponentsInChildren<Collider>(true))
                if (collider.name.StartsWith("COL_ShippingScale", StringComparison.Ordinal)) scaleSolids.Add(collider);
            Assert.That(scaleSolids.Count, Is.EqualTo(2), "Platform and stand collisions accompany the moved visible scale.");
            var overlaps = new Collider[32];
            for (int unit = 0; unit < 3; unit++)
                foreach (float progress in new[] { .03f, .14f, .19f, .24f, .30f, .34f, .41f, .52f, .64f, .78f, .93f })
                {
                    cannery.ApplyAt(TransferTime(cannery, CityFishSupplyStage.LoadFinished, unit, progress));
                    Assert.That(cannery.Snapshot.Inspection.ApprovedUnits, Is.EqualTo(3));
                    Assert.That(cannery.WorkerHandsMatch, Is.True, cannery.LastCrewContactFailure);
                    Physics.SyncTransforms();
                    Bounds carton = InspectionRenderedBounds(cannery.FinishedBox(unit));
                    Transform support = null;
                    float nearest = float.PositiveInfinity;
                    foreach (Transform candidate in supports)
                    {
                        float distance = Vector3.Distance(candidate.position, cannery.FinishedBox(unit).position);
                        if (distance >= nearest) continue;
                        support = candidate; nearest = distance;
                    }
                    Assert.That(support, Is.Not.Null);
                    Bounds pallet = InspectionRenderedBounds(support);
                    Assert.That(carton.min.y, Is.EqualTo(pallet.max.y).Within(.025f), "The real carton bottom rests on its pallet top.");
                    Assert.That(cannery.Plan.Local(pallet.center).x, Is.GreaterThan(0f), "Loading uses the outdoor approved buffer.");
                    if (progress >= .35f) continue;
                    int count = Physics.OverlapCapsuleNonAlloc(driver.transform.position + Vector3.up * .35f,
                        driver.Head.position, .29f, overlaps, ~0, QueryTriggerInteraction.Ignore);
                    Assert.That(count, Is.LessThan(overlaps.Length));
                    for (int hit = 0; hit < count; hit++)
                        Assert.That(scaleSolids.Contains(overlaps[hit]), Is.False, "The loading operator clears the complete outdoor scale.");
                    for (int role = 0; role < 4; role++)
                    {
                        Vector3 difference = cannery.GetFactoryWorker(role).transform.position - driver.transform.position;
                        Assert.That(new Vector2(difference.x, difference.z).magnitude, Is.GreaterThan(.58f),
                            "Loading operator clears waiting worker " + role + " for carton " + unit + ", phase " + progress);
                    }
                }
        }
    }
}

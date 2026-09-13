using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace BarPromenade.Tests.PlayMode
{
    public sealed class CanneryWomanAssetsSetup : IPrebuildSetup
    {
        public void Setup()
        {
#if UNITY_EDITOR
            Type.GetType("BarPromenade.Editor.CanneryWomanAssetSetup, BarPromenade.Editor", true)
                .GetMethod("BuildOrThrow", Type.EmptyTypes).Invoke(null, null);
#endif
        }
    }

    public sealed partial class AreaCaptureFixture
    {
        [UnityTest]
        [Explicit("The authored cannery woman: sprite face, work contacts, shift walk, nearby RU/EN speech, pause and wake. Run without -batchmode for real speech UI.")]
        [PrebuildSetup(typeof(CanneryWomanAssetsSetup))]
        public IEnumerator CityCanneryWoman() => CaptureCanneryWomanScene(false);

        [UnityTest]
        [Explicit("Focused rendered clothing/hair contacts and gravity response, including the actual nearby speaking pose. Run without -batchmode.")]
        [PrebuildSetup(typeof(CanneryWomanAssetsSetup))]
        public IEnumerator CityCanneryWomanContacts() => CaptureCanneryWomanScene(true);

        private IEnumerator CaptureCanneryWomanScene(bool contactsOnly)
        {
            Assert.That(Application.isBatchMode, Is.False);
#if UNITY_EDITOR
            Type viewType = typeof(UnityEditor.EditorWindow).Assembly.GetType("UnityEditor.GameView");
            var view = UnityEditor.EditorWindow.GetWindow(viewType);
            view.Show(); view.Focus();
#endif
            yield return CaptureFocusedPort((camera, city, port, crew) => CaptureCanneryWoman(camera, city, port, crew, contactsOnly));
        }

        private static IEnumerator CaptureCanneryWoman(Camera camera, CityGameRoot city,
            CityPortController port, CityPortCrew crew, bool contactsOnly)
        {
            var cannery = city.Cannery;
            var actor = cannery.GetFactoryWorker(2);
            var woman = actor.GetComponent<CanneryWomanPresentation>();
            var speech = cannery.FactoryConversation;
            var follow = camera.GetComponent<PlayerCameraFollow>();
            bool oldFollow = follow != null && follow.enabled, oldManual = speech.UseManualClock;
            Vector3 oldHero = city.Player.GameObject.transform.position, oldCamera = camera.transform.position;
            Quaternion oldRotation = camera.transform.rotation;
            float oldFov = camera.fieldOfView;
            var motionFailures = new List<string>();
            using var surfaces = new CanneryWomanSurfaceProbe(actor);
            try
            {
                Assert.That(woman, Is.Not.Null, "The seamer is the dedicated authored person.");
                Assert.That(woman.Hair, Is.Not.Null, "Long authored hair keeps its own bounded physical motion.");
                Assert.That(cannery.GetComponentsInChildren<CanneryWomanPresentation>(true).Length, Is.EqualTo(1));
                for (int role = 0; role < 4; role++)
                    if (role != 2) Assert.That(cannery.GetFactoryWorker(role).GetComponent<CanneryWomanPresentation>(), Is.Null);
                GameSessionState.AdvanceGameTime((float)((12d * 60d - GameSessionState.GameTimeOfDayMinutes) /
                    GameTimeState.GameMinutesPerRealSecond));
                city.DayNight.ApplyCurrentTime(true);
                if (follow != null) follow.enabled = false;
                cannery.AutoAdvance = false;
                speech.UseManualClock = true;
                CityFishSupplySession.ResetForNewGame();
                cannery.ApplyAt(0d);
                cannery.ApplyLifeAt(10d);
                AssertCanneryWorkersOutside(cannery);
                Material faceMaterial = woman.FaceRenderer.sharedMaterial;
                Texture faceTexture = CanneryWomanFaceTexture(woman.FaceRenderer);
                Renderer top = Array.Find(actor.GetComponentsInChildren<Renderer>(), renderer => renderer.name == "CLO_LongSleeveTop");
                Assert.That(top, Is.Not.Null);
                Assert.That(woman.Wardrobe, Is.Not.Null);
                Assert.That(woman.Wardrobe.CurrentOutfitId, Is.EqualTo(CanneryWomanWardrobe.WorkwearId));
                Assert.That(woman.Wardrobe.Owns(top), Is.True);
                Assert.That(woman.Wardrobe.Owns(woman.FaceRenderer), Is.False);
                Renderer longHair = Array.Find(actor.GetComponentsInChildren<Renderer>(), renderer => renderer.name == "HAIR_LongBack");
                Assert.That(longHair, Is.Not.Null);
                Assert.That(woman.Wardrobe.Owns(longHair), Is.False,
                    "Clothing bindings keep the permanent face and physically driven hair separate.");
                Texture topTexture = CanneryWomanFaceTexture(top);
                Assert.That(faceTexture, Is.Not.Null);
                Assert.That(faceTexture.width, Is.EqualTo(512));
                Assert.That(faceTexture.height, Is.EqualTo(256));
                Assert.That(faceTexture.filterMode, Is.EqualTo(FilterMode.Point));

                yield return CaptureWomanView("woman-00-ordinary-distance", 4f, 0f, false);
                yield return CaptureWomanView("woman-01-face-front", 1.15f, 0f, true);
                yield return CaptureWomanView("woman-02-face-three-quarter", 1.2f, 35f, true);
                yield return CaptureWomanView("woman-03-profile", 1.2f, 78f, true);
                yield return CaptureWomanView("woman-03-long-hair-back", 2.6f, 180f, false);
                yield return CaptureWomanHeightComparison();

                Transform leftFoot = CityPedestrianHandProps.FindSocket(actor.ModelRoot, "foot.L");
                Transform rightFoot = CityPedestrianHandProps.FindSocket(actor.ModelRoot, "foot.R");
                Vector3 leftBefore = leftFoot.position, rightBefore = rightFoot.position;
                var quietCells = new HashSet<int>();
                for (int sample = 0; sample <= 100; sample++)
                {
                    cannery.ApplyLifeAt(10d + sample * .05d);
                    quietCells.Add(woman.CurrentFaceCell);
                    Assert.That(Vector3.Distance(leftBefore, leftFoot.position), Is.LessThan(.025f));
                    Assert.That(Vector3.Distance(rightBefore, rightFoot.position), Is.LessThan(.025f));
                    Assert.That(woman.IsSmiling, Is.False, "An unattended idle never repeats a social smile.");
                }
                Assert.That(quietCells.Count, Is.GreaterThan(1), "The same sprite surface visibly blinks.");
                Debug.Log("CANNERY WOMAN: dedicated sprite atlas, outdoor stance and real camera portraits captured.");

                foreach (string language in contactsOnly ? new[] { "ru" } : new[] { "ru", "en" })
                {
                    var catalog = JsonUtility.FromJson<NameplateCatalog>(Resources.Load<TextAsset>("Localization/" + language).text);
                    var lines = Array.FindAll(catalog.entries, entry => entry.key.StartsWith("city.cannery.", StringComparison.Ordinal));
                    using (new NameplateLanguageScope(lines))
                    {
                        int ownLines = 0;
                        foreach (CityCanneryConversationKind kind in Enum.GetValues(typeof(CityCanneryConversationKind)))
                        for (int variant = 0; variant < CityCanneryConversationCatalog.Count(kind); variant++)
                        {
                            var pair = CityCanneryConversationCatalog.Get(kind, variant);
                            for (int turn = 0; turn < 2; turn++)
                            {
                                string key = pair.Key(turn == 1), line = LocalizationService.Get(key);
                                Assert.That(line, Is.Not.Empty.And.Not.EqualTo(key));
                                Assert.That(line.Contains("!"), Is.False);
                                if ((turn == 0 ? pair.FirstRole : pair.SecondRole) == 2) ownLines++;
                            }
                        }
                        Assert.That(ownLines, Is.EqualTo(16), "The woman's lines belong to the actual factory pair catalog.");
                        // Each localized capture begins a real fresh channel. Ordinary
                        // suspend deliberately preserves its partial shuffle round.
                        speech.Initialize(cannery, city.Player.GameObject.transform, 84);
                        speech.UseManualClock = true;
                        city.Player.Motor.Teleport(cannery.Plan.World(new Vector3(4.5f, .3f, -1.8f)));
                        double start = cannery.LifeSeconds + 10d;
                        bool capturedSpeech = false, capturedSmile = false, paused = false;
                        var spokenMouths = new HashSet<SpeechMouthPose>();
                        var completed = new HashSet<int>();
                        int previousCompleted = speech.CompletedExchanges;
                        for (int sample = 0; sample < 5000 && (!capturedSpeech || (!contactsOnly && (!capturedSmile || !paused))); sample++)
                        {
                            double life = start + sample * .1d;
                            cannery.ApplyLifeAt(life);
                            speech.ApplyAt();
                            cannery.ApplyLifeAt(life);
                            if (speech.CompletedExchanges != previousCompleted)
                            {
                                Assert.That(completed.Add(speech.CurrentExchange.Variant), Is.True,
                                    "A waiting round cannot repeat an already completed pair.");
                                previousCompleted = speech.CompletedExchanges;
                                if (completed.Count == CityCanneryConversationCatalog.Count(CityCanneryConversationKind.Wait)) completed.Clear();
                            }
                            if (speech.Bubbles.TryGetSpeechFaceSample(actor, out SpeechFaceSample delivery))
                            {
                                spokenMouths.Add(woman.CurrentSpeechFace.Mouth);
                                if (delivery.IsTyping && !paused)
                                {
                                    paused = true;
                                    int cell = woman.CurrentFaceCell;
                                    Quaternion head = actor.Head.rotation;
                                    Vector3 hand = actor.RightGrip.position;
                                    var hairPoints = new Vector3[CanneryWomanHair.PointCount];
                                    for (int i = 0; i < hairPoints.Length; i++) hairPoints[i] = woman.Hair.WorldPoint(i);
                                    using (GameTimeScaleRuntime.AcquirePause())
                                    {
                                        yield return null; yield return null;
                                        Assert.That(woman.CurrentFaceCell, Is.EqualTo(cell));
                                        Assert.That(actor.Head.rotation, Is.EqualTo(head));
                                        Assert.That(actor.RightGrip.position, Is.EqualTo(hand));
                                        for (int i = 0; i < hairPoints.Length; i++)
                                            Assert.That(woman.Hair.WorldPoint(i), Is.EqualTo(hairPoints[i]));
                                        Assert.That(speech.Bubbles.TryGetSpeechFaceSample(actor, out SpeechFaceSample frozen), Is.True);
                                        Assert.That(frozen.ElapsedSeconds, Is.EqualTo(delivery.ElapsedSeconds));
                                    }
                                }
                                if (!delivery.IsTyping && delivery.RevealedCharacters == delivery.Text.Length && !capturedSpeech)
                                {
                                    capturedSpeech = true;
                                    surfaces.Observe("speech-" + language);
                                    yield return CaptureCannerySpeech(camera, city, cannery, "woman-04-speech-" + language,
                                        actor.Head.position + actor.transform.forward * 1.4f + Vector3.up * .06f,
                                        actor.Head.position);
                                }
                            }
                            if (woman.IsSmiling && !capturedSmile)
                            {
                                capturedSmile = true;
                                yield return CaptureWomanView("woman-05-rare-smile-" + language, 1.15f, 0f, true);
                            }
                            if (sample % 40 == 0) yield return null;
                        }
                        Assert.That(capturedSpeech, Is.True, language + " actual nearby speaking pose.");
                        if (!contactsOnly)
                            Assert.That(capturedSmile && paused, Is.True, language + " actual nearby smile and pause.");
                        Assert.That(spokenMouths.Count, Is.GreaterThan(2), "Painted mouth shapes follow the shared bubble's reveal.");
                    }
                }

                city.Player.Motor.Teleport(cannery.Plan.World(new Vector3(40f, .3f, 0f)));
                speech.ApplyAt();
                cannery.ApplyLifeAt(cannery.LifeSeconds);
                Assert.That(speech.HasExchange, Is.False);
                Assert.That(woman.IsSmiling, Is.False, "Losing the nearby conversation also releases its face.");
                actor.gameObject.SetActive(false);
                actor.gameObject.SetActive(true);
                cannery.ApplyLifeAt(cannery.LifeSeconds + 10d);
                Assert.That(woman.FaceRenderer.sharedMaterial, Is.SameAs(faceMaterial));
                Assert.That(CanneryWomanFaceTexture(woman.FaceRenderer), Is.SameAs(faceTexture),
                    "Waking the compatible worker rig must retain her own painted atlas.");
                Assert.That(CanneryWomanFaceTexture(top), Is.SameAs(topTexture),
                    "The generic crew fabric recipe must not replace her authored top and denim atlas.");
                Assert.That(woman.Hair.MaximumSpeed, Is.LessThan(.0001f), "Wake restores long hair without old momentum.");
                Assert.That(woman.Hair.LastStepSeconds, Is.Zero);
                yield return ValidateWomanHairInertia();
                yield return ValidateWomanHairPitch();

                double enter = cannery.FactoryShiftEntryTime(2), duration = cannery.FactoryShiftWalkDuration(2);
                cannery.ApplyAt(enter);
                Vector3 previous = actor.transform.position;
                bool walked = false;
                int walkSteps = (int)Math.Ceiling(duration * 10d);
                for (int sample = 1; sample <= walkSteps; sample++)
                {
                    cannery.ApplyAt(enter + duration * sample / walkSteps);
                    cannery.ApplyLifeAt(cannery.LifeSeconds + duration / walkSteps);
                    Assert.That(Vector3.Distance(previous, actor.transform.position), Is.LessThan(.2f));
                    previous = actor.transform.position;
                    walked |= actor.CurrentAction == VillageResidentAction.Walk;
                    if (sample == walkSteps / 2) yield return CaptureWomanView("woman-06-walk-to-shift", 3.8f, 30f, false);
                }
                Assert.That(walked, Is.True);
                foreach (CityCanneryProductionStage stage in new[] { CityCanneryProductionStage.Fill, CityCanneryProductionStage.Seal })
                {
                    for (int sample = 2; sample <= 38; sample++)
                    {
                        cannery.ApplyAt(CanneryTime(cannery, stage, sample / 40f));
                        cannery.ApplyLifeAt(cannery.LifeSeconds + .1d);
                        Assert.That(cannery.WorkerHandsMatch, Is.True, cannery.LastCrewContactFailure);
                        if (sample % 10 == 0) surfaces.Observe("work-" + stage + "-" + sample);
                    }
                    cannery.ApplyAt(CanneryTime(cannery, stage, .45f));
                    for (int frame = 0; frame < 60; frame++) cannery.ApplyLifeAt(cannery.LifeSeconds + 1d / 60d);
                    LogHairContacts("work-" + stage.ToString().ToLowerInvariant());
                    yield return CaptureWomanView("woman-07-work-" + stage.ToString().ToLowerInvariant(), 1.7f, 105f, false);
                }
                cannery.ApplyAt(cannery.Cycle.StageStart(CityFishSupplyStage.FactoryToShop));
                AssertCanneryWorkersOutside(cannery);
                yield return CaptureWomanView("woman-08-after-shift", 3.5f, 20f, false);
                surfaces.AssertClear();
                Assert.That(motionFailures, Is.Empty, string.Join("; ", motionFailures));
                Debug.Log(contactsOnly ? "CITY CANNERY WOMAN CONTACTS OK: actual rendered surfaces, gravity-driven hair, speaking pose, shift walk, work contacts and wake." :
                    "CITY CANNERY WOMAN OK: female authored body, shared sprite speech in RU/EN, restrained smile, inertial long hair, rooted idle, shift walk, work contacts, pause and wake.");
            }
            finally
            {
                cannery.AutoAdvance = false;
                speech.Suspend(); speech.UseManualClock = oldManual;
                cannery.ApplyAt(0d); cannery.AdvanceSounds(false);
                city.Player.Motor.Teleport(oldHero);
                camera.transform.SetPositionAndRotation(oldCamera, oldRotation);
                camera.fieldOfView = oldFov;
                if (follow != null) follow.enabled = oldFollow;
            }

            IEnumerator CaptureWomanView(string name, float distance, float angle, bool portrait)
            {
                Vector3 target = portrait ? actor.Head.position - Vector3.up * .025f : actor.transform.position + Vector3.up * 1.05f;
                Vector3 direction = Quaternion.AngleAxis(angle, Vector3.up) * actor.transform.forward;
                Vector3 from = target + direction * distance + Vector3.up * (portrait ? .015f : .5f);
                city.Player.Motor.Teleport(from - Vector3.up * EyeHeight);
                camera.transform.SetPositionAndRotation(from, Quaternion.LookRotation(target - from));
                camera.fieldOfView = portrait ? 34f : 52f;
                Physics.SyncTransforms();
                yield return null; yield return null;
                if (!portrait) surfaces.Observe(name);
                CaptureCurrentCamera(camera, "CityCannery", name);
            }

            IEnumerator ValidateWomanHairPitch()
            {
                var hair = woman.Hair;
                Quaternion headBefore = actor.Head.rotation;
                double start = cannery.LifeSeconds + 20d;
                Vector3 pitchAxis = actor.transform.right;
                try
                {
                    hair.ApplyAt(start, false, false, default);
                    Vector3 rigidTip = actor.Head.InverseTransformPoint(hair.WorldPoint(3));
                    Vector3 previous = hair.WorldPoint(3);
                    float settlingMotion = 0f, finalSpeed = 0f;
                    for (int frame = 1; frame <= 300; frame++)
                    {
                        float pitch = Mathf.SmoothStep(0f, 48f, Mathf.Clamp01(frame / 48f));
                        actor.Head.rotation = Quaternion.AngleAxis(pitch, pitchAxis) * headBefore;
                        hair.ApplyAt(start + frame / 60d, false, false, default);
                        if (frame > 48 && frame < 90)
                            settlingMotion += Vector3.Distance(hair.WorldPoint(3), previous);
                        if (frame > 280) finalSpeed = Mathf.Max(finalSpeed, Vector3.Distance(hair.WorldPoint(3), previous) * 60f);
                        previous = hair.WorldPoint(3);
                        if ((frame <= 90 && frame % 15 == 0) || frame % 60 == 0)
                            surfaces.Observe("head-pitch-" + frame);
                        if (frame == 48 || frame == 90)
                            yield return CaptureWomanView("woman-11-head-down-" + frame, 2.3f, 110f, false);
                    }
                    float drop = hair.WorldPoint(0).y - hair.WorldPoint(3).y;
                    float length = 0f;
                    for (int i = 1; i < 4; i++) length += Vector3.Distance(hair.WorldPoint(i - 1), hair.WorldPoint(i));
                    ExpectMotion(drop / length > .72f, $"Long hair hangs down at head pitch: drop/length {drop / length:F4} > .72.");
                    float rigidDistance = Vector3.Distance(hair.WorldPoint(3), actor.Head.TransformPoint(rigidTip));
                    ExpectMotion(rigidDistance > .08f, $"Hair is free from rigid head pitch: tip distance {rigidDistance:F4} > .08 m.");
                    ExpectMotion(settlingMotion > .004f, $"Loose length settles after the head stops: travel {settlingMotion:F4} > .004 m.");
                    ExpectMotion(finalSpeed < .04f, $"Pitch damping: final speed {finalSpeed:F5} < .04 m/s.");
                    yield return CaptureWomanView("woman-12-head-down-settled", 2.3f, 110f, false);
                    yield return CaptureWomanView("woman-13-nape-head-down", 1.5f, 175f, true);
                    Debug.Log($"CANNERY WOMAN PITCH: vertical drop/length {drop / length:F3}, settling travel {settlingMotion:F4} m.");
                    LogHairContacts("pitch");
                }
                finally
                {
                    actor.Head.rotation = headBefore;
                    cannery.ApplyLifeAt(cannery.LifeSeconds + 1d);
                }
            }

            IEnumerator ValidateWomanHairInertia()
            {
                var hair = woman.Hair;
                Quaternion headBefore = actor.Head.rotation;
                double start = cannery.LifeSeconds + 10d;
                float maximumMotion = 0f, settledSpeed = 0f;
                Vector3 previousTip = Vector3.zero;
                try
                {
                    hair.ApplyAt(start, false, false, default);
                    int resets = hair.ResetCount;
                    Debug.Log($"CANNERY WOMAN HAIR UNITS: head scale {actor.Head.lossyScale:F4}, first segment {Vector3.Distance(hair.WorldPoint(0), hair.WorldPoint(1)):F5} m.");
                    for (int chain = 0; chain < CanneryWomanHair.ChainCount; chain++)
                    {
                        int first = chain * CanneryWomanHair.PointsPerChain;
                        Assert.That(Vector3.Distance(hair.WorldPoint(first), hair.WorldPoint(first + 1)),
                            Is.InRange(.15f, .23f), "Hair bones retain their authored metre lengths after animation sampling.");
                    }
                    for (int frame = 1; frame <= 240; frame++)
                    {
                        float turn = Mathf.SmoothStep(0f, 22f, Mathf.Clamp01(frame / 36f));
                        actor.Head.rotation = Quaternion.AngleAxis(turn, Vector3.up) * headBefore;
                        hair.ApplyAt(start + frame / 60d, false, false, default);
                        maximumMotion = Mathf.Max(maximumMotion, hair.MaximumDisplacement);
                        Assert.That(hair.MaximumLengthError, Is.LessThan(.004f), "Hair retains its authored bone lengths.");
                        for (int i = 0; i < CanneryWomanHair.PointCount; i++)
                            if (i % CanneryWomanHair.PointsPerChain != 0)
                                Assert.That(hair.IsInsideBody(hair.WorldPoint(i), .025f), Is.False,
                                    "Visible hair joint penetrates its own body at point " + i);
                        Vector3 tip = hair.WorldPoint(CanneryWomanHair.PointsPerChain - 1);
                        if (frame > 220) settledSpeed = Mathf.Max(settledSpeed, Vector3.Distance(tip, previousTip) * 60f);
                        previousTip = tip;
                        if ((frame <= 90 && frame % 15 == 0) || frame % 60 == 0)
                            surfaces.Observe("head-yaw-" + frame);
                        if (frame == 30) yield return CaptureWomanView("woman-09-hair-turn-inertia", 2.3f, 155f, false);
                        if (frame % 60 == 0) yield return null;
                    }
                    Assert.That(hair.ResetCount, Is.EqualTo(resets), "An ordinary head turn must simulate continuously.");
                    Debug.Log($"CANNERY WOMAN HAIR SOLVE: target step {hair.PeakTargetStep:F5}, integrated {hair.PeakIntegratedDisplacement:F5}, constrained {hair.PeakConstrainedDisplacement:F5}, bone rotation {hair.PeakBoneCorrectionDegrees:F4}, visible {maximumMotion:F5}, settled speed {settledSpeed:F5}.");
                    ExpectMotion(maximumMotion > .003f, $"Yaw inertia: visible displacement {maximumMotion:F5} > .003 m.");
                    ExpectMotion(settledSpeed < .04f, $"Yaw damping: final speed {settledSpeed:F5} < .04 m/s.");
                    LogHairContacts("yaw");
                    yield return CaptureWomanView("woman-10-hair-settled", 2.3f, 155f, false);
                    hair.ApplyAt(start + 20d, false, false, default);
                    Assert.That(hair.ResetCount, Is.EqualTo(resets + 1));
                    Assert.That(hair.MaximumSpeed, Is.LessThan(.0001f), "A seek discards momentum while retaining static surface contacts.");
                    Assert.That(hair.LastStepSeconds, Is.Zero);
                    var resetPoints = new Vector3[CanneryWomanHair.PointCount];
                    for (int i = 0; i < resetPoints.Length; i++) resetPoints[i] = hair.WorldPoint(i);
                    hair.ApplyAt(start + 30d, false, false, default);
                    for (int i = 0; i < resetPoints.Length; i++)
                        Assert.That(Vector3.Distance(hair.WorldPoint(i), resetPoints[i]), Is.LessThan(.0002f),
                            "The same stationary pose must reconstruct the same contact-corrected hair.");
                    Debug.Log("CANNERY WOMAN HAIR: yaw motion and contact measurements collected; bone lengths and deterministic silent seek verified.");
                }
                finally
                {
                    actor.Head.rotation = headBefore;
                    cannery.ApplyLifeAt(cannery.LifeSeconds + 1d);
                }
            }

            void ExpectMotion(bool condition, string message)
            {
                if (condition) return;
                motionFailures.Add(message);
                Debug.LogWarning("CANNERY WOMAN MOTION: " + message);
            }

            void LogHairContacts(string phase)
            {
                var hair = woman.Hair;
                Debug.Log($"CANNERY WOMAN CONTACTS {phase}: body penetration {hair.MaximumSurfaceBodyPenetration:F5}, edge penetration {hair.MaximumContinuousBodyPenetration:F5}, strand overlap {hair.MaximumStrandOverlap:F5}, contacts {hair.LastSurfaceContactCount}, speed {hair.MaximumSpeed:F5}, samples {hair.CollisionPointCount}.");
                if (hair.MaximumSurfaceBodyPenetration > .001f || hair.MaximumContinuousBodyPenetration > .001f)
                    Debug.Log($"CANNERY WOMAN DEEPEST {phase}: {hair.DeepestBodyVolume} at {hair.DeepestBodyPoint:F5}, correction {hair.DeepestBodyCorrection:F5}.");
            }

            IEnumerator CaptureWomanHeightComparison()
            {
                Vector3 savedPosition = city.Player.GameObject.transform.position;
                Quaternion savedRotation = city.Player.GameObject.transform.rotation;
                var heroRenderers = city.Player.Visual.Renderers;
                try
                {
                    city.Player.Motor.Teleport(actor.transform.position + actor.transform.right * .68f +
                        Vector3.up * PlayerFactory.GroundedRootOffset);
                    city.Player.GameObject.transform.rotation = actor.transform.rotation;
                    city.Player.Visual.SetMotion(PlayerMotionSample.Stationary);
                    foreach (Renderer renderer in heroRenderers) renderer.enabled = true;
                    Vector3 target = actor.transform.position + actor.transform.right * .34f + Vector3.up * .9f;
                    Vector3 from = target + actor.transform.forward * 3.8f + Vector3.up * .15f;
                    camera.transform.SetPositionAndRotation(from, Quaternion.LookRotation(target - from));
                    camera.fieldOfView = 42f;
                    yield return null; yield return null;
                    float womanHeight = MeasureVisibleHeight(actor.GetComponentsInChildren<Renderer>());
                    float heroHeight = MeasureVisibleHeight(heroRenderers);
                    Assert.That(womanHeight, Is.InRange(1.60f, 1.66f));
                    Assert.That(heroHeight - womanHeight, Is.InRange(.08f, .17f),
                        "The actual dressed woman is visibly shorter than the production hero.");
                    CaptureCurrentCamera(camera, "CityCannery", "woman-03-height-beside-hero");
                    Debug.Log($"CANNERY WOMAN HEIGHT: dressed mesh {womanHeight:F3} m / hero {heroHeight:F3} m.");
                }
                finally
                {
                    foreach (Renderer renderer in heroRenderers) renderer.enabled = false;
                    city.Player.Motor.Teleport(savedPosition);
                    city.Player.GameObject.transform.rotation = savedRotation;
                }
            }
        }

        private static float MeasureVisibleHeight(IEnumerable<Renderer> renderers)
        {
            float low = float.PositiveInfinity, high = float.NegativeInfinity;
            Mesh baked = new Mesh();
            try
            {
                foreach (Renderer renderer in renderers)
                {
                    Mesh mesh;
                    // This FBX hierarchy retains its authored unit factors,
                    // exactly like the shared hero and production foot probes.
                    if (renderer is SkinnedMeshRenderer skin) { baked.Clear(false); skin.BakeMesh(baked, true); mesh = baked; }
                    else mesh = renderer.GetComponent<MeshFilter>()?.sharedMesh;
                    if (mesh == null) continue;
                    foreach (Vector3 vertex in mesh.vertices)
                    {
                        float y = renderer.transform.TransformPoint(vertex).y;
                        low = Mathf.Min(low, y); high = Mathf.Max(high, y);
                    }
                }
            }
            finally { UnityEngine.Object.DestroyImmediate(baked); }
            return high - low;
        }

        private static Texture CanneryWomanFaceTexture(Renderer face)
        {
            var block = new MaterialPropertyBlock();
            face.GetPropertyBlock(block);
            return block.GetTexture("_BaseMap") ?? face.sharedMaterial.GetTexture("_BaseMap");
        }

        // Reuse the scarf's independent rendered-triangle oracle. The hair
        // solver's particles and contact proxies do not decide this verdict.
        private sealed class CanneryWomanSurfaceProbe : IDisposable
        {
            private readonly ScarfContactProbe probe = new ScarfContactProbe();
            private readonly ScarfContactReport report = new ScarfContactReport();
            private readonly Renderer[] hair, body;
            private readonly Renderer[] fixedHair;
            private readonly Renderer[] head;
            private readonly Dictionary<Renderer, bool[]> freeHair = new Dictionary<Renderer, bool[]>();
            private readonly Renderer top, jeans;
            private readonly int[] hemVertices;
            private readonly HashSet<string> failures = new HashSet<string>();
            private readonly Transform root;
            private readonly CanneryWomanHair physics;
            private int observations;

            public CanneryWomanSurfaceProbe(VillageResidentPresentation actor)
            {
                root = actor.transform;
                physics = actor.GetComponent<CanneryWomanHair>();
                var renderers = actor.GetComponentsInChildren<Renderer>();
                hair = Array.FindAll(renderers, value => value.name.StartsWith("HAIR_Long", StringComparison.Ordinal));
                fixedHair = Array.FindAll(renderers, value => value.name.StartsWith("HAIR_", StringComparison.Ordinal) &&
                    !value.name.StartsWith("HAIR_Long", StringComparison.Ordinal));
                head = Array.FindAll(renderers, value => value.name == "GEO_Head" || value.name == "GEO_FaceSurface");
                foreach (Renderer renderer in hair)
                {
                    var skin = (SkinnedMeshRenderer)renderer;
                    int headIndex = Array.FindIndex(skin.bones, value => value.name == "head");
                    BoneWeight[] weights = skin.sharedMesh.boneWeights;
                    var free = new bool[weights.Length];
                    for (int i = 0; i < weights.Length; i++)
                    {
                        BoneWeight w = weights[i];
                        free[i] = !((w.boneIndex0 == headIndex && w.weight0 > .0001f) ||
                            (w.boneIndex1 == headIndex && w.weight1 > .0001f) ||
                            (w.boneIndex2 == headIndex && w.weight2 > .0001f) ||
                            (w.boneIndex3 == headIndex && w.weight3 > .0001f));
                    }
                    freeHair.Add(renderer, free);
                }
                body = Array.FindAll(renderers, value => value.name == "CLO_LongSleeveTop" ||
                    value.name == "CLO_HighWaistJeans" || value.name == "CLO_LeatherBelt" || value.name == "GEO_Neck" ||
                    value.name.StartsWith("CLO_Sleeve.", StringComparison.Ordinal) ||
                    value.name.StartsWith("CLO_Cuff.", StringComparison.Ordinal) ||
                    value.name.StartsWith("GEO_Palm.", StringComparison.Ordinal) ||
                    value.name.StartsWith("GEO_Finger", StringComparison.Ordinal) ||
                    value.name.StartsWith("GEO_Thumb.", StringComparison.Ordinal));
                top = Array.Find(renderers, value => value.name == "CLO_LongSleeveTop");
                jeans = Array.Find(renderers, value => value.name == "CLO_HighWaistJeans");
                Vector2[] topUv = ((SkinnedMeshRenderer)top).sharedMesh.uv;
                float minimumV = float.PositiveInfinity;
                foreach (Vector2 uv in topUv) minimumV = Mathf.Min(minimumV, uv.y);
                var indices = new List<int>();
                for (int i = 0; i < topUv.Length; i++)
                    if (topUv[i].y < minimumV + .0001f) indices.Add(i);
                hemVertices = indices.ToArray();
            }

            public void Observe(string phase)
            {
                observations++;
                if (physics.MaximumBendLimitExcessDegrees > .1f && failures.Add("hair bend limits"))
                    Debug.LogWarning("CANNERY WOMAN SURFACE: " + phase + ": visible hair exceeds its bend limits by " +
                        physics.MaximumBendLimitExcessDegrees.ToString("F3", CultureInfo.InvariantCulture) + " degrees");
                probe.Origin = root.position;
                var renderedHair = new Dictionary<string, ScarfContactSurface>();
                foreach (Renderer renderer in hair) renderedHair.Add(renderer.name, probe.Read(renderer));
                Assert.That(physics.HasSurfaceBindings, Is.True);
                Assert.That(physics.SurfacePointCount, Is.GreaterThan(600));
                for (int i = 0; i < physics.SurfacePointCount; i++)
                {
                    ScarfContactSurface surface = renderedHair[physics.SurfaceRendererName(i)];
                    Vector3 actual = surface.Vertices[physics.SurfaceVertexIndex(i)] + probe.Origin;
                    Assert.That(Vector3.Distance(actual, physics.SurfaceWorldPoint(i)), Is.LessThan(.0005f),
                        "Hair contact sample must match the actual skinned renderer in " + phase + ", vertex " + i);
                }
                var others = new List<ScarfContactSurface>();
                foreach (Renderer renderer in body) others.Add(probe.Read(renderer));
                ScarfContactSurface shirt = others.Find(value => value.Renderer == top);
                ScarfContactSurface denim = others.Find(value => value.Renderer == jeans);
                Assert.That(denim.Closed, Is.True, "The high-waisted jeans remain one closed imported surface.");
                foreach (int index in hemVertices)
                {
                    if (probe.Contains(shirt.Vertices[index], denim)) continue;
                    if (failures.Add("tucked top hem / jeans"))
                        Debug.LogWarning("CANNERY WOMAN SURFACE: " + phase + ": tucked hem escaped the actual jeans at vertex " + index);
                }
                foreach (Renderer renderer in hair)
                {
                    ScarfContactSurface strand = probe.Read(renderer);
                    foreach (ScarfContactSurface other in others)
                        Check(strand, other, phase);
                    // Only the head-weighted attachment rings may be buried
                    // beneath the crown. The whole remaining visible length is
                    // checked against the head and every other full strand.
                    ScarfContactSurface free = FreeSurface(strand, freeHair[renderer]);
                    foreach (Renderer other in head) Check(free, probe.Read(other), phase);
                    foreach (Renderer other in fixedHair) Check(free, probe.Read(other), phase);
                    foreach (Renderer other in hair)
                        if (other != renderer) Check(free, probe.Read(other), phase);
                }
                foreach (Renderer renderer in fixedHair)
                    foreach (ScarfContactSurface other in others) Check(probe.Read(renderer), other, phase);
            }

            private void Check(ScarfContactSurface strand, ScarfContactSurface other, string phase)
            {
                if (!probe.Intersects(strand, other, phase, report)) return;
                string pair = strand.Name + " / " + other.Name;
                if (!failures.Add(pair)) return;
                string folder = Path.Combine(Directory.GetCurrentDirectory(), "Captures", "CityCannery");
                Directory.CreateDirectory(folder);
                File.WriteAllText(Path.Combine(folder, "woman-contact-" + failures.Count + ".json"),
                    JsonUtility.ToJson(probe.Witness, true));
                Debug.LogWarning("CANNERY WOMAN SURFACE: " + phase + ": " + pair + ": " + probe.Witness.reason);
            }

            private static ScarfContactSurface FreeSurface(ScarfContactSurface source, bool[] free)
            {
                var vertices = new List<Vector3>();
                var triangles = new List<int>();
                var bounds = new List<Bounds>();
                var mapping = new int[free.Length];
                for (int i = 0; i < free.Length; i++)
                    if (free[i]) { mapping[i] = vertices.Count; vertices.Add(source.Vertices[i]); }
                for (int i = 0; i < source.Triangles.Length; i += 3)
                {
                    int a = source.Triangles[i], b = source.Triangles[i + 1], c = source.Triangles[i + 2];
                    if (!free[a] || !free[b] || !free[c]) continue;
                    triangles.Add(mapping[a]); triangles.Add(mapping[b]); triangles.Add(mapping[c]);
                    bounds.Add(source.TriangleBounds[i / 3]);
                }
                var result = new ScarfContactSurface { Name = source.Name, Renderer = source.Renderer,
                    Vertices = vertices.ToArray(), Triangles = triangles.ToArray(), TriangleBounds = bounds.ToArray(), Closed = false };
                result.Bounds = new Bounds(vertices[0], Vector3.zero);
                foreach (Vector3 vertex in vertices) result.Bounds.Encapsulate(vertex);
                return result;
            }

            public void AssertClear()
            {
                Assert.That(hair.Length, Is.EqualTo(3));
                Assert.That(hemVertices.Length, Is.GreaterThan(20));
                Assert.That(observations, Is.GreaterThan(8));
                Assert.That(failures, Is.Empty, "Rendered clothing/hair surface failures: " + string.Join(", ", failures));
                Debug.Log("CANNERY WOMAN SURFACES: actual skinned hair stays outside clothes, neck and hands in speech, walking, head turns/pitch and work.");
            }

            public void Dispose() => probe.Dispose();
        }
    }
}

using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

namespace BarPromenade.Tests.PlayMode
{
    public sealed class VillageNarrativeAssetsSetup : IPrebuildSetup
    {
        public void Setup()
        {
#if UNITY_EDITOR
            new VillageArtAssetsSetup().Setup();
            foreach (string name in new[] { "VillageNarrativeAssetSetup", "PlayerDialogueActionAssetSetup" })
                Type.GetType("BarPromenade.Editor." + name + ", BarPromenade.Editor", true)
                    .GetMethod("BuildOrThrow", Type.EmptyTypes).Invoke(null, null);
#endif
        }
    }

    public sealed partial class AreaCaptureFixture
    {
        [UnityTest]
        [Explicit("Narrative objects: owner context, all inspections, bilingual pages and owned cleanup.")]
        [PrebuildSetup(typeof(VillageNarrativeAssetsSetup))]
        public IEnumerator AlpineVillageNarrative()
        {
            Assert.That(Application.isBatchMode, Is.False, "A Game view is needed to inspect the real bottom UI.");
#if UNITY_EDITOR
            var gameView = UnityEditor.EditorWindow.GetWindow(typeof(UnityEditor.EditorWindow).Assembly.GetType("UnityEditor.GameView"));
            gameView.Show(); gameView.Focus();
#endif
            GameSessionState.BeginNewGame();
            GameSessionState.TryStartGameTimeFromWake();
            GameSessionState.TrySetDebugGameDay(2);
            GameSessionState.AdvanceGameTime((float)(100f / GameTimeState.GameMinutesPerRealSecond));
            yield return SceneManager.LoadSceneAsync(SceneIds.AlpineVillage, LoadSceneMode.Single);
            AlpineVillageRoot root = null;
            float deadline = Time.realtimeSinceStartup + TimeoutSeconds;
            while ((root == null || !root.IsInitialized) && Time.realtimeSinceStartup < deadline)
            { root = Object.FindAnyObjectByType<AlpineVillageRoot>(); yield return null; }
            Assert.That(root != null && root.IsInitialized, Is.True);
            for (int i = 0; i < SettleFrames; i++) yield return null;
            Camera camera = Camera.main;
            PlayerCameraFollow follow = camera.GetComponent<PlayerCameraFollow>();
            PlayerInteractor hero = root.Player.Interactor;
            NarrativeInteractionController session = NarrativeInteractionController.For(hero);
            var targets = root.World.Root.GetComponentsInChildren<VillageNarrativeInstance>().OrderBy(p => p.Point.Number).ToArray();
            var homes = root.World.Root.GetComponentsInChildren<VillageHouseholdIdentity>();
            int[] retained = { 1, 4, 5, 6, 8, 9, 10, 11, 14, 15, 16, 17,
                20, 21, 22, 24, 25, 26, 27, 28, 29, 30, 31, 32 };
            Assert.That(targets.Length, Is.EqualTo(24));
            CollectionAssert.AreEqual(retained.Select(number => "village.narrative." + number.ToString("00")),
                targets.Select(t => t.Point.Id), "Only the retained inspections may be placed.");
            Assert.That(targets.Count(t => !t.Point.Existing), Is.EqualTo(22));
            Assert.That(targets.Count(t => t.Point.Document), Is.EqualTo(2));
            Assert.That(homes.Length, Is.EqualTo(31));
            Assert.That(homes.Count(h => h.Occupied), Is.EqualTo(4));
            foreach (var home in homes.Where(h => h.HouseId.StartsWith("village-house-", StringComparison.Ordinal)))
                Assert.That(home.GetComponentsInChildren<MeshRenderer>().Any(r => r.name == "Lit Window"),
                    Is.EqualTo(home.Occupied), home.HouseId);
            Assert.That(VillageHouseholdCatalog.Residents.Count, Is.EqualTo(6));
            var roads = AlpineVillagePathPlanner.Create(root.Plan);
            var failures = new List<string>();
            foreach (var target in targets)
            {
                AbandonmentCheck(failures, target.Point.Id + " owner", () => AssertNarrativeOwner(root, target));
                AbandonmentCheck(failures, target.Point.Id + " approach", () =>
                {
                    Assert.That(root.World.WalkableArea.Contains(target.Approach, .30f), Is.True,
                        target.Point.Id + ": the inspection approach must remain walkable.");
                    Collider[] blockers = Physics.OverlapCapsule(target.Approach + Vector3.up * .41f,
                        target.Approach + Vector3.up * 1.48f, .30f,
                        PlayerInteractor.InteractionLayerMask, QueryTriggerInteraction.Ignore);
                    Assert.That(blockers.Where(c => !c.transform.IsChildOf(hero.transform)).Select(c => c.name), Is.Empty,
                        target.Point.Id + ": real scene colliders must leave room for the hero at the approach. " +
                        NarrativeDiagnostics(target, hero));
                });
            }
            foreach (var target in targets.Where(t => t.Point.GroundProp))
                AbandonmentCheck(failures, target.Point.Id + " road clearance", () =>
                {
                    Vector2 min = new Vector2(float.PositiveInfinity, float.PositiveInfinity), max = -min;
                    Quaternion inverse = Quaternion.Inverse(target.Point.Rotation);
                    foreach (MeshFilter mesh in target.Subject.GetComponentsInChildren<MeshFilter>())
                    foreach (Vector3 vertex in mesh.sharedMesh.vertices)
                    {
                        Vector3 local = inverse * (mesh.transform.TransformPoint(vertex) - target.Point.Position);
                        min = Vector2.Min(min, new Vector2(local.x, local.z));
                        max = Vector2.Max(max, new Vector2(local.x, local.z));
                    }
                    float clearance = AlpineVillageNarrativePlan.MeasureRoadClearance(root.Plan, roads,
                        target.Point.Position, target.Point.Rotation, Rect.MinMaxRect(min.x, min.y, max.x, max.y));
                    Assert.That(clearance, Is.GreaterThanOrEqualTo(AlpineVillageNarrativePlan.MinimumRoadClearance - .002f),
                        target.Point.Id + ": the entire real model must stay outside the road surface.");
                });

            float previousStep = Time.captureDeltaTime;
            var input = new InputTestFixture();
            input.Setup();
            Keyboard keyboard = InputSystem.AddDevice<Keyboard>();
            try
            {
                Time.captureDeltaTime = .05f;
                // Show each subject with its surroundings from its actual approach.
                // Discovery need not be on a road or satisfy a road-distance quota.
                foreach (var target in targets)
                {
                    follow.ClearFixedPose();
                    root.Player.Motor.Teleport(target.Approach + Vector3.up * PlayerFactory.GroundedRootOffset);
                    hero.transform.rotation = target.Interaction.Staging.Entry.RootRotation;
                    Vector3 look = target.FocusBounds.center - hero.transform.position;
                    float yaw = Quaternion.LookRotation(new Vector3(look.x, 0, look.z)).eulerAngles.y;
                    follow.RotateYaw(Mathf.DeltaAngle(camera.transform.eulerAngles.y, yaw));
                    follow.Snap();
                    for (int i = 0; i < 3; i++) yield return null;
                    CaptureCurrentCamera(camera, "VillageNarrative", "context-" + target.Point.Number.ToString("00"));
                }

                foreach (string language in new[] { "ru", "en" })
                {
                    var catalog = JsonUtility.FromJson<NameplateCatalog>(Resources.Load<TextAsset>("Localization/" + language).text);
                    using (new NameplateLanguageScope(catalog.entries.Where(e =>
                        e.key.StartsWith("village.narrative.", StringComparison.Ordinal) ||
                        e.key.StartsWith("narrative.", StringComparison.Ordinal)).ToArray()))
                    foreach (var target in targets.Where(t => language == "ru" || t.Point.Document || t.Point.Number == 29))
                    {
                        if (target.Point.Document)
                        {
                            var paper = target.Subject.GetComponentInChildren<TMPro.TextMeshPro>();
                            AlpineVillageNarrativeBuilder.RefreshPaperText(paper, target.Point.Number);
                            string paperOriginal = string.Join("\n\n", target.Interaction.Definition.Pages
                                .Where(p => p.Kind == NarrativePageKind.DocumentText).Select(p => LocalizationService.Get(p.TextKey)));
                            Assert.That(paper.text, Is.EqualTo(paperOriginal), "The physical paper retains every paragraph.");
                            Assert.That(paper.isTextTruncated, Is.False);
                            Bounds ink = paper.mesh.bounds;
                            Rect sheet = paper.rectTransform.rect;
                            Assert.That(ink.min.x, Is.GreaterThanOrEqualTo(sheet.xMin - .001f));
                            Assert.That(ink.max.x, Is.LessThanOrEqualTo(sheet.xMax + .001f));
                            Assert.That(ink.min.y, Is.GreaterThanOrEqualTo(sheet.yMin - .001f));
                            Assert.That(ink.max.y, Is.LessThanOrEqualTo(sheet.yMax + .001f));
                        }
                        Vector3 start = target.Interaction.Staging.Entry.RootPosition;
                        root.Player.Motor.Teleport(start);
                        hero.transform.rotation = target.Interaction.Staging.Entry.RootRotation;
                        Physics.SyncTransforms(); follow.Snap();
                        for (int i = 0; i < 3; i++) yield return null;
                        if (!target.Interaction.CanInteract(hero))
                        { failures.Add(target.Point.Id + ": accessible E anchor refused " + NarrativeDiagnostics(target, hero)); continue; }
                        if (!ReferenceEquals(hero.ActiveInteractable, target.Interaction))
                        { failures.Add(target.Point.Id + ": E selects " + hero.ActiveInteractable?.GetType().Name + " " + NarrativeDiagnostics(target, hero)); continue; }
                        bool measureFocus = language == "ru" && (target.Point.Number == 29 || target.Point.Number == 4);
                        var focusFrames = new List<NarrativeCameraFrame> { new NarrativeCameraFrame(camera, 0f) };
                        float framingSeconds = 0f;
                        int focusCapture = 0;
                        float[] focusCaptureTimes = { .2f, .5f, .85f };
                        // Use the real keyboard for the mandatory truck, pile and a
                        // document; other subjects exercise the identical target API.
                        bool timedKeyboard = measureFocus && target.Point.Number == 4;
                        if (timedKeyboard) input.Press(keyboard.eKey, queueEventOnly: true);
                        else if (target.Point.Number == 26 || target.Point.Number == 27 || target.Point.Number == 4)
                            yield return PressLodgeUse(input, keyboard);
                        else target.Interaction.Interact(hero);
                        for (int i = 0; i < 140 && (session.IsActive || timedKeyboard && i == 0) &&
                            session.Phase != NarrativeInteractionPhase.Reading; i++)
                        {
                            yield return null;
                            if (timedKeyboard && i == 0) input.Release(keyboard.eKey, queueEventOnly: true);
                            if (!measureFocus) continue;
                            if (session.Phase == NarrativeInteractionPhase.Positioning)
                            {
                                focusFrames.Clear();
                                focusFrames.Add(new NarrativeCameraFrame(camera, 0f));
                                continue;
                            }
                            framingSeconds += Time.deltaTime;
                            focusFrames.Add(new NarrativeCameraFrame(camera, framingSeconds));
                            if (focusCapture < focusCaptureTimes.Length && framingSeconds >= focusCaptureTimes[focusCapture])
                            {
                                CaptureCurrentCamera(camera, "VillageNarrative", "focus-" + target.Point.Number.ToString("00") +
                                    "-" + (++focusCapture));
                            }
                        }
                        if (session.Phase != NarrativeInteractionPhase.Reading)
                        {
                            failures.Add(target.Point.Id + ": failed to reach reading (" + session.Phase + ", " + session.LastFailureReason +
                                "; " + session.CameraDirector.LastRejectedShotReason + ") " + NarrativeDiagnostics(target, hero));
                            session.RestoreImmediate(); yield return null; continue;
                        }
                        if (measureFocus)
                            AbandonmentCheck(failures, target.Point.Id + " focus transition", () =>
                            {
                                AssertNarrativeCameraTransition(focusFrames, .9f, 1.3f, true);
                                Assert.That(focusCapture, Is.EqualTo(3));
                            });
                        Assert.That(session.PageIndex, Is.Zero, "The opening press must not skip a page.");
                        for (int page = 0; page < target.Interaction.Definition.Pages.Count; page++)
                        {
                            yield return null; yield return null;
                            AbandonmentCheck(failures, target.Point.Id + " " + language + " page " + page, () =>
                            {
                                Assert.That(session.CurrentPage.TextKey, Is.EqualTo(target.Interaction.Definition.Pages[page].TextKey));
                                Assert.That(root.InteractionPrompt.LastRenderedTextFits, Is.True, "Full text must fit the real bottom UI.");
                                Assert.That(root.InteractionPrompt.IsSpeaking, Is.False);
                                Assert.That(session.CameraDirector.CurrentShotIsClear, Is.True);
                                if (target.Point.Document)
                                {
                                    Assert.That(Vector3.Dot(camera.transform.forward, -target.Subject.forward), Is.GreaterThan(.95f),
                                        "A note gets its own frontal close-up.");
                                    Assert.That(Vector3.Distance(camera.transform.position, target.FocusBounds.center), Is.LessThan(2f));
                                    var focusVolume = Object.FindObjectsByType<UnityEngine.Rendering.Volume>()
                                        .Single(v => v.name == "Cinematic Depth Of Field");
                                    Assert.That(focusVolume.profile.TryGet<UnityEngine.Rendering.Universal.DepthOfField>(out var focus), Is.True);
                                    Vector3 paperCentre = target.Subject.GetComponentInChildren<TMPro.TextMeshPro>().transform.position;
                                    Assert.That(focus.focusDistance.value, Is.EqualTo(camera.WorldToViewportPoint(paperCentre).z).Within(.025f),
                                        "The focus plane must lie on the physical paper, even above the optical centre.");
                                    Assert.That(Vector3.Dot(camera.transform.forward,
                                        hero.transform.position + Vector3.up * 1.4f - camera.transform.position), Is.LessThan(0f),
                                        "The hero stays behind the note camera.");
                                }
                            });
                            if (page == 0 || target.Point.Document)
                                yield return CaptureNarrativeScreen("inspect-" + target.Point.Number.ToString("00") + "-" + language + "-" + page);
                            Assert.That(session.Confirm(), Is.True);
                        }
                        for (int i = 0; i < 100 && session.IsActive; i++) yield return null;
                        AbandonmentCheck(failures, target.Point.Id + " released", () =>
                        {
                            Assert.That(session.IsActive || root.InteractionPrompt.HasHeldPage || BarMinigameModalLock.IsAnyLocked, Is.False);
                            Assert.That(hero.InputEnabled && root.Player.Motor.InputEnabled, Is.True);
                            Assert.That(target.Subject.gameObject.activeInHierarchy, Is.True);
                        });
                        session.RestoreImmediate();
                    }
                }

                string report = Path.Combine(Directory.GetCurrentDirectory(), "Captures", "VillageNarrative", "diagnostics.txt");
                File.WriteAllLines(report, failures);
                Debug.Log("Narrative point checks: " + (failures.Count == 0 ? "clear" : string.Join("\n", failures)));
                var repeat = targets.First(t => t.Point.Number == 5);
                root.Player.Motor.Teleport(repeat.Interaction.Staging.Entry.RootPosition);
                Physics.SyncTransforms();
                yield return null; yield return null;
                var original = new Pose(camera.transform.position, camera.transform.rotation);
                follow.SetFixedPose(original.position, original.rotation, 61f);
                Assert.That(session.Begin(repeat.Interaction, hero), Is.True);
                for (int i = 0; i < 100 && session.Phase == NarrativeInteractionPhase.Positioning; i++) yield return null;
                for (int i = 0; i < 5; i++) yield return null;
                Assert.That(session.Phase, Is.EqualTo(NarrativeInteractionPhase.Framing));
                var transitionVolume = Object.FindObjectsByType<UnityEngine.Rendering.Volume>()
                    .Single(v => v.name == "Cinematic Depth Of Field");
                using (GameTimeScaleRuntime.AcquirePause())
                {
                    var heldPose = new Pose(camera.transform.position, camera.transform.rotation);
                    float heldFov = camera.fieldOfView, heldBlur = transitionVolume.weight;
                    for (int i = 0; i < 3; i++) yield return null;
                    Assert.That(session.Confirm(), Is.False);
                    Assert.That(session.Phase, Is.EqualTo(NarrativeInteractionPhase.Framing));
                    Assert.That(Vector3.Distance(camera.transform.position, heldPose.position), Is.LessThan(.001f));
                    Assert.That(Quaternion.Angle(camera.transform.rotation, heldPose.rotation), Is.LessThan(.01f));
                    Assert.That(camera.fieldOfView, Is.EqualTo(heldFov).Within(.001f));
                    Assert.That(transitionVolume.weight, Is.EqualTo(heldBlur).Within(.001f));
                }
                for (int i = 0; i < 100 && session.Phase != NarrativeInteractionPhase.Reading && session.IsActive; i++) yield return null;
                Assert.That(session.Phase, Is.EqualTo(NarrativeInteractionPhase.Reading));
                Assert.That(session.PageIndex, Is.Zero);
                var returnFrames = new List<NarrativeCameraFrame> { new NarrativeCameraFrame(camera, 0f) };
                float returnSeconds = 0f;
                input.Press(keyboard.escapeKey); yield return null;
                returnSeconds += Time.deltaTime;
                returnFrames.Add(new NarrativeCameraFrame(camera, returnSeconds));
                input.Release(keyboard.escapeKey); yield return null;
                returnSeconds += Time.deltaTime;
                returnFrames.Add(new NarrativeCameraFrame(camera, returnSeconds));
                for (int i = 0; i < 100 && !session.CameraDirector.IsFinished; i++)
                {
                    yield return null;
                    returnSeconds += Time.deltaTime;
                    returnFrames.Add(new NarrativeCameraFrame(camera, returnSeconds));
                }
                AssertNarrativeCameraTransition(returnFrames, .6f, 1f, false);
                for (int i = 0; i < 100 && session.IsActive; i++) yield return null;
                Assert.That(session.IsActive, Is.False);
                Assert.That(follow.FixedPoseActive, Is.True);
                Assert.That(Vector3.Distance(follow.FixedBasePosition, original.position), Is.LessThan(.001f));
                Assert.That(follow.FixedBaseFieldOfView, Is.EqualTo(61f));
                yield return null; yield return null;
                Assert.That(session.Begin(repeat.Interaction, hero), Is.True);
                for (int i = 0; i < 100 && session.Phase == NarrativeInteractionPhase.Positioning; i++) yield return null;
                for (int i = 0; i < 5; i++) yield return null;
                Assert.That(session.Phase, Is.EqualTo(NarrativeInteractionPhase.Framing));
                var cancelPose = new Pose(camera.transform.position, camera.transform.rotation);
                float cancelFov = camera.fieldOfView;
                session.Cancel();
                Assert.That(session.CameraDirector.IsReturning, Is.True);
                Assert.That(Vector3.Distance(camera.transform.position, cancelPose.position), Is.LessThan(.001f));
                Assert.That(Quaternion.Angle(camera.transform.rotation, cancelPose.rotation), Is.LessThan(.01f));
                Assert.That(camera.fieldOfView, Is.EqualTo(cancelFov).Within(.001f));
                for (int i = 0; i < 100 && session.IsActive; i++) yield return null;
                Assert.That(session.IsActive || root.InteractionPrompt.HasHeldPage || BarMinigameModalLock.IsAnyLocked ||
                    BarPromenade.Rendering.CinematicDepthOfField.IsActive, Is.False);
                Assert.That(follow.FixedPoseActive, Is.True);
                Assert.That(Vector3.Distance(follow.FixedBasePosition, original.position), Is.LessThan(.001f));
                Assert.That(Quaternion.Angle(follow.FixedBasePose.rotation, original.rotation), Is.LessThan(.01f));
                Assert.That(follow.FixedBaseFieldOfView, Is.EqualTo(61f));
                follow.ClearFixedPose();
                yield return null; yield return null;
                Assert.That(session.Begin(repeat.Interaction, hero), Is.True);
                session.Cancel(); // A cancel during the visible approach is always available.
                for (int i = 0; i < 100 && session.IsActive; i++) yield return null;
                Assert.That(session.IsActive, Is.False);
                yield return null; yield return null;
                Assert.That(session.Begin(repeat.Interaction, hero), Is.True);
                repeat.Interaction.enabled = false;
                Assert.That(session.IsActive || BarMinigameModalLock.IsAnyLocked, Is.False);
                repeat.Interaction.enabled = true;
                yield return null; yield return null;
                Assert.That(session.Begin(repeat.Interaction, hero), Is.True);
                Object.Destroy(repeat.Subject.gameObject);
                yield return null; yield return null;
                Assert.That(session.IsActive || root.InteractionPrompt.HasHeldPage || BarMinigameModalLock.IsAnyLocked, Is.False);
                var departing = targets.First(t => t.Point.Number == 1);
                root.Player.Motor.Teleport(departing.Interaction.Staging.Entry.RootPosition);
                Physics.SyncTransforms();
                yield return null; yield return null;
                Assert.That(session.Begin(departing.Interaction, hero), Is.True);
                for (int i = 0; i < 140 && session.Phase != NarrativeInteractionPhase.Reading && session.IsActive; i++) yield return null;
                Assert.That(session.Phase, Is.EqualTo(NarrativeInteractionPhase.Reading));
                yield return SceneManager.LoadSceneAsync(SceneIds.MainMenu, LoadSceneMode.Single);
                yield return null;
                Assert.That(BarMinigameModalLock.IsAnyLocked || BarPromenade.Rendering.CinematicDepthOfField.IsActive, Is.False);
            }
            finally
            {
                if (session != null) session.RestoreImmediate();
                input.TearDown();
                Time.captureDeltaTime = previousStep;
            }
            Assert.That(failures, Is.Empty, string.Join("\n", failures));
        }

        private readonly struct NarrativeCameraFrame
        {
            public readonly float Seconds, FieldOfView;
            public readonly Pose Pose;
            public NarrativeCameraFrame(Camera camera, float seconds)
            {
                Seconds = seconds;
                FieldOfView = camera.fieldOfView;
                Pose = new Pose(camera.transform.position, camera.transform.rotation);
            }
        }

        private static void AssertNarrativeCameraTransition(List<NarrativeCameraFrame> frames,
            float minimumSeconds, float maximumSeconds, bool checkSlowEnds)
        {
            Assert.That(frames.Count, Is.GreaterThan(5), "The focus must visibly travel over multiple frames.");
            NarrativeCameraFrame first = frames[0], last = frames[frames.Count - 1];
            Assert.That(last.Seconds, Is.InRange(minimumSeconds, maximumSeconds));
            float distance = Vector3.Distance(first.Pose.position, last.Pose.position);
            float angle = Quaternion.Angle(first.Pose.rotation, last.Pose.rotation);
            float lens = Mathf.Abs(last.FieldOfView - first.FieldOfView);
            Assert.That(distance, Is.GreaterThan(.1f), "This regression needs a real camera move.");
            float largestStep = 0f;
            for (int i = 1; i < frames.Count; i++)
            {
                NarrativeCameraFrame previous = frames[i - 1], current = frames[i];
                // At the capture's 50 ms step, no frame may consume more than
                // 15% of the whole move. This catches an obstacle fallback cut
                // even when IsSettled still waits for its nominal timer.
                float fraction = 3f * (current.Seconds - previous.Seconds);
                float step = Vector3.Distance(previous.Pose.position, current.Pose.position);
                largestStep = Mathf.Max(largestStep, step);
                Assert.That(step, Is.LessThanOrEqualTo(distance * fraction + .02f), "Focus position jumped at " + current.Seconds);
                Assert.That(Quaternion.Angle(previous.Pose.rotation, current.Pose.rotation),
                    Is.LessThanOrEqualTo(angle * fraction + .3f), "Focus rotation jumped at " + current.Seconds);
                Assert.That(Mathf.Abs(current.FieldOfView - previous.FieldOfView),
                    Is.LessThanOrEqualTo(lens * fraction + .1f), "Focus lens jumped at " + current.Seconds);
            }
            if (checkSlowEnds)
            {
                NarrativeCameraFrame early = frames.Last(frame => frame.Seconds <= .201f);
                NarrativeCameraFrame late = frames.First(frame => frame.Seconds >= last.Seconds - .201f);
                foreach (var pair in new[] { (first, early), (late, last) })
                {
                    Assert.That(Vector3.Distance(pair.Item1.Pose.position, pair.Item2.Pose.position),
                        Is.LessThan(distance * .12f + .02f), "The first and last 200 ms must ease gently.");
                    Assert.That(Quaternion.Angle(pair.Item1.Pose.rotation, pair.Item2.Pose.rotation),
                        Is.LessThan(angle * .12f + .3f));
                    Assert.That(Mathf.Abs(pair.Item1.FieldOfView - pair.Item2.FieldOfView),
                        Is.LessThan(lens * .12f + .1f));
                }
            }
            Debug.Log($"Narrative camera: {last.Seconds:F2}s, travel={distance:F3}m, maximum frame={largestStep:F3}m");
        }

        private static string NarrativeDiagnostics(VillageNarrativeInstance target, PlayerInteractor hero)
        {
            Collider[] overlaps = Physics.OverlapSphere(hero.transform.position + Vector3.up * .8f,
                PlayerInteractor.InteractionRadius, PlayerInteractor.InteractionLayerMask, QueryTriggerInteraction.Collide);
            return "hero=" + hero.transform.position + " approach=" + target.Approach + " anchor=" + target.Interaction.InteractionPosition +
                " bounds=" + target.FocusBounds + " overlaps=" + overlaps.Length + " [" + string.Join(",", overlaps.Select(c => c.name)) + "]";
        }

        private static void AssertNarrativeOwner(AlpineVillageRoot root, VillageNarrativeInstance target)
        {
            string house = target.Point.HouseId;
            if (string.IsNullOrEmpty(house)) return;
            bool core = house.StartsWith("village-house-", StringComparison.Ordinal);
            AlpineVillageAbandonedPlot plot = root.Plan.Expansion.Abandonment.Plots.FirstOrDefault(p => p.Id == house);
            string path = core ? "Village Plot - " + house : plot != null
                ? "Village Expansion/Abandoned Settlement/" + house
                : house == "ski-lodge" ? "Village Expansion/Ski Lodge"
                : house == "trade-warehouse" ? "Village Expansion/Former Trade Warehouse" : null;
            Assert.That(path, Is.Not.Null, target.Point.Id + ": unknown physical owner " + house);
            Transform owner = root.World.Root.transform.Find(path);
            Assert.That(owner, Is.Not.Null, target.Point.Id + ": missing physical owner " + house);
            Assert.That(owner.GetComponentsInChildren<Renderer>(), Is.Not.Empty,
                target.Point.Id + ": ownership requires an actual built place.");
            if (plot == null) return;

            // Measure the imported subject against its owner's own yard, in
            // metres. A road-visible model can still stand in the wrong place.
            Assert.That(owner.Find("Former Household Yard"), Is.Not.Null, house);
            Quaternion inverse = Quaternion.Inverse(owner.rotation);
            Rect yard = plot.YardBounds;
            Vector2 min = new Vector2(float.PositiveInfinity, float.PositiveInfinity), max = -min;
            foreach (MeshFilter filter in target.Subject.GetComponentsInChildren<MeshFilter>())
            foreach (Vector3 vertex in filter.sharedMesh.vertices)
            {
                Vector3 local = inverse * (filter.transform.TransformPoint(vertex) - owner.position);
                min = Vector2.Min(min, new Vector2(local.x, local.z));
                max = Vector2.Max(max, new Vector2(local.x, local.z));
            }
            string detail = target.Point.Id + ": actual model bounds " + Rect.MinMaxRect(min.x, min.y, max.x, max.y) +
                " must stay inside " + house + " yard " + yard;
            Assert.That(min.x, Is.GreaterThanOrEqualTo(yard.xMin - .02f), detail);
            Assert.That(max.x, Is.LessThanOrEqualTo(yard.xMax + .02f), detail);
            Assert.That(min.y, Is.GreaterThanOrEqualTo(yard.yMin - .02f), detail);
            Assert.That(max.y, Is.LessThanOrEqualTo(yard.yMax + .02f), detail);
        }

        private static IEnumerator CaptureNarrativeScreen(string name)
        {
            string folder = Path.Combine(Directory.GetCurrentDirectory(), "Captures", "VillageNarrative");
            Directory.CreateDirectory(folder);
            string path = Path.Combine(folder, name + ".png");
            DateTime previous = File.Exists(path) ? File.GetLastWriteTimeUtc(path) : DateTime.MinValue;
            ScreenCapture.CaptureScreenshot(path);
            float deadline = Time.realtimeSinceStartup + 3f;
            do { yield return null; }
            while ((!File.Exists(path) || File.GetLastWriteTimeUtc(path) <= previous) && Time.realtimeSinceStartup < deadline);
            Assert.That(File.Exists(path) && File.GetLastWriteTimeUtc(path) > previous, Is.True, name);
        }
    }
}

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
        [Explicit("Narrative objects: ordinary road cameras, all inspections, bilingual pages and owned cleanup.")]
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
            Assert.That(targets.Length, Is.EqualTo(32));
            Assert.That(targets.Select(t => t.Point.Id).Distinct().Count(), Is.EqualTo(32));
            Assert.That(targets.Count(t => !t.Point.Existing), Is.EqualTo(30));
            Assert.That(targets.Count(t => t.Point.Document), Is.EqualTo(2));
            Assert.That(targets.GroupBy(t => t.Point.Sector).All(g => g.Count() == 4), Is.True);
            Assert.That(targets.Any(t => t.Point.Asphalt) && targets.Any(t => !t.Point.Asphalt), Is.True);
            Assert.That(homes.Length, Is.EqualTo(31));
            Assert.That(homes.Count(h => h.Occupied), Is.EqualTo(4));
            foreach (var home in homes.Where(h => h.HouseId.StartsWith("village-house-", StringComparison.Ordinal)))
                Assert.That(home.GetComponentsInChildren<MeshRenderer>().Any(r => r.name == "Lit Window"),
                    Is.EqualTo(home.Occupied), home.HouseId);
            Assert.That(VillageHouseholdCatalog.Residents.Count, Is.EqualTo(6));
            var roads = AlpineVillagePathPlanner.Create(root.Plan);
            foreach (var target in targets.Where(t => t.Point.GroundProp))
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
            }

            var failures = new List<string>();
            float previousStep = Time.captureDeltaTime;
            var input = new InputTestFixture();
            input.Setup();
            Keyboard keyboard = InputSystem.AddDevice<Keyboard>();
            try
            {
                Time.captureDeltaTime = .05f;
                // Readability/discovery uses the actual follow camera at its ordinary
                // distance/pitch/FOV, with the hero still on the existing road.
                foreach (var target in targets)
                {
                    follow.ClearFixedPose();
                    root.Player.Motor.Teleport(target.Point.DiscoveryPosition + Vector3.up * PlayerFactory.GroundedRootOffset);
                    hero.transform.rotation = Quaternion.LookRotation(target.Point.RoadDirection);
                    Vector3 look = target.Point.DiscoveryLookDirection;
                    float yaw = Quaternion.LookRotation(new Vector3(look.x, 0, look.z)).eulerAngles.y;
                    follow.RotateYaw(Mathf.DeltaAngle(camera.transform.eulerAngles.y, yaw));
                    follow.Snap();
                    for (int i = 0; i < 3; i++) yield return null;
                    AbandonmentCheck(failures, target.Point.Id + " roadside discovery", () =>
                    {
                        Assert.That(root.World.WalkableArea.Contains(target.Point.DiscoveryPosition, .30f), Is.True);
                        Assert.That(NarrativeVisible(camera, target.Subject, target.FocusBounds), Is.True,
                            "No visible model surface from the normal road camera.");
                        Assert.That(Vector3.Distance(target.Point.Position, target.Point.DiscoveryPosition), Is.LessThan(32f));
                    });
                    CaptureCurrentCamera(camera, "VillageNarrative", "road-" + target.Point.Number.ToString("00"));
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
                        if (target.Point.Number == 5) start += target.Interaction.Staging.Entry.RootRotation * Vector3.right * .6f;
                        root.Player.Motor.Teleport(start);
                        hero.transform.rotation = target.Interaction.Staging.Entry.RootRotation;
                        Physics.SyncTransforms(); follow.Snap();
                        for (int i = 0; i < 3; i++) yield return null;
                        if (!target.Interaction.CanInteract(hero))
                        { failures.Add(target.Point.Id + ": accessible E anchor refused " + NarrativeDiagnostics(target, hero)); continue; }
                        if (!ReferenceEquals(hero.ActiveInteractable, target.Interaction))
                        { failures.Add(target.Point.Id + ": E selects " + hero.ActiveInteractable?.GetType().Name + " " + NarrativeDiagnostics(target, hero)); continue; }
                        // Use the real keyboard for the mandatory truck, pile and a
                        // document; other subjects exercise the identical target API.
                        if (target.Point.Number == 26 || target.Point.Number == 27 || target.Point.Number == 4)
                            yield return PressLodgeUse(input, keyboard);
                        else target.Interaction.Interact(hero);
                        for (int i = 0; i < 140 && session.IsActive && session.Phase != NarrativeInteractionPhase.Reading; i++) yield return null;
                        if (session.Phase != NarrativeInteractionPhase.Reading)
                        {
                            failures.Add(target.Point.Id + ": failed to reach reading (" + session.Phase + ", " + session.LastFailureReason +
                                "; " + session.CameraDirector.LastRejectedShotReason + ") " + NarrativeDiagnostics(target, hero));
                            session.RestoreImmediate(); yield return null; continue;
                        }
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
                                    var focusVolume = Object.FindObjectsByType<UnityEngine.Rendering.Volume>(FindObjectsSortMode.None)
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
                for (int i = 0; i < 140 && session.Phase != NarrativeInteractionPhase.Reading && session.IsActive; i++) yield return null;
                Assert.That(session.Phase, Is.EqualTo(NarrativeInteractionPhase.Reading));
                Assert.That(session.PageIndex, Is.Zero);
                using (GameTimeScaleRuntime.AcquirePause())
                {
                    var heldPose = new Pose(camera.transform.position, camera.transform.rotation);
                    for (int i = 0; i < 3; i++) yield return null;
                    Assert.That(session.Confirm(), Is.False);
                    Assert.That(Vector3.Distance(camera.transform.position, heldPose.position), Is.LessThan(.001f));
                }
                input.Press(keyboard.escapeKey); yield return null;
                input.Release(keyboard.escapeKey); yield return null;
                for (int i = 0; i < 100 && session.IsActive; i++) yield return null;
                Assert.That(session.IsActive, Is.False);
                Assert.That(follow.FixedPoseActive, Is.True);
                Assert.That(Vector3.Distance(follow.FixedBasePosition, original.position), Is.LessThan(.001f));
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
                var departing = targets.First(t => t.Point.Number == 2);
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

        private static string NarrativeDiagnostics(VillageNarrativeInstance target, PlayerInteractor hero)
        {
            Collider[] overlaps = Physics.OverlapSphere(hero.transform.position + Vector3.up * .8f,
                PlayerInteractor.InteractionRadius, PlayerInteractor.InteractionLayerMask, QueryTriggerInteraction.Collide);
            return "hero=" + hero.transform.position + " approach=" + target.Approach + " anchor=" + target.Interaction.InteractionPosition +
                " bounds=" + target.FocusBounds + " overlaps=" + overlaps.Length + " [" + string.Join(",", overlaps.Select(c => c.name)) + "]";
        }

        private static bool NarrativeVisible(Camera camera, Transform subject, Bounds bounds)
        {
            // A ray through the empty part of a frame's AABB proves nothing.
            // Sample real authored faces, including slender poles and paper.
            foreach (MeshFilter filter in subject.GetComponentsInChildren<MeshFilter>())
            {
                Mesh mesh = filter.sharedMesh;
                if (mesh == null || !mesh.isReadable) continue;
                Vector3[] vertices = mesh.vertices;
                int[] triangles = mesh.triangles;
                int step = Mathf.Max(1, triangles.Length / (3 * 32)) * 3;
                for (int i = 0; i + 2 < triangles.Length; i += step)
                {
                    Vector3 point = filter.transform.TransformPoint((vertices[triangles[i]] +
                        vertices[triangles[i + 1]] + vertices[triangles[i + 2]]) / 3f);
                    Vector3 viewport = camera.WorldToViewportPoint(point);
                    if (viewport.z <= 0 || viewport.x < .10f || viewport.x > .90f || viewport.y < .12f || viewport.y > .88f) continue;
                    Vector3 delta = point - camera.transform.position;
                    if (!Physics.Raycast(camera.transform.position, delta.normalized, out RaycastHit hit,
                            delta.magnitude + .03f, PlayerInteractor.InteractionLayerMask, QueryTriggerInteraction.Ignore) ||
                        hit.transform == subject || hit.transform.IsChildOf(subject)) return true;
                }
            }
            return false;
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

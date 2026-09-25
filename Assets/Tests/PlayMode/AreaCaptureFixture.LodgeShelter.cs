using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.TestTools;

namespace BarPromenade.Tests.PlayMode
{
    public sealed partial class AreaCaptureFixture
    {
        [UnityTest]
        [Explicit("Lodge surfaces, real threshold traversal and the closed-cellar confirmation/outcome flow.")]
        [PrebuildSetup(typeof(VillageArtAssetsSetup))]
        public IEnumerator AlpineVillageLodgeSurfaceClearance()
        {
            Assert.That(Application.isBatchMode, Is.False, "The compact cellar choices and outcome need a real Game view.");
#if UNITY_EDITOR
            var gameView = UnityEditor.EditorWindow.GetWindow(typeof(UnityEditor.EditorWindow).Assembly.GetType("UnityEditor.GameView"));
            gameView.Show(); gameView.Focus();
#endif
            GameSessionState.BeginNewGame();
            GameSessionState.TryStartGameTimeFromWake();
            AlpineVillageRoot root = null;
            yield return Capture(SceneIds.AlpineVillage, () =>
            {
                root = Object.FindAnyObjectByType<AlpineVillageRoot>();
                return root != null && root.IsInitialized ? root : null;
            }, () =>
            {
                PlaceLodgeHero(root, new Vector3(1.1f, .02f, -2f));
                root.LodgeShelter.SetLanternLit(true);
                return new[]
                {
                    Shot.At("lodge-surfaces-left", LodgePoint(root, -3.8f, 1.8f, -2.7f),
                        LodgePoint(root, -8f, 1.65f, 1.3f), 78f),
                    Shot.At("lodge-surfaces-right", LodgePoint(root, 3.8f, 1.8f, -.5f),
                        LodgePoint(root, 8.6f, 2.6f, 2f), 78f),
                    Shot.At("lodge-surfaces-cot", LodgePoint(root, -5.3f, 1.5f, -1.35f),
                        LodgePoint(root, -7.55f, .55f, .15f), 66f),
                    Shot.At("lodge-threshold-outside", LodgePoint(root, -.7f, 1.35f, -8f),
                        LodgePoint(root, 0f, .08f, -5.95f), 64f),
                    Shot.At("lodge-threshold-inside", LodgePoint(root, 1.1f, 1.35f, -4.1f),
                        LodgePoint(root, 0f, .08f, -6.1f), 64f),
                    Shot.At("lodge-cellar-corner", LodgePoint(root, -4.3f, 1.85f, -2.0f),
                        LodgePoint(root, -7.65f, .42f, -3.5f), 76f)
                };
            });

            Transform lodge = root.LodgeShelter.transform;
            Transform chest = lodge.Find("LodgeBlanketChest");
            Assert.That(chest, Is.Not.Null);
            Vector3[] vertices = chest.GetComponent<MeshFilter>().sharedMesh.vertices
                .Select(v => lodge.InverseTransformPoint(chest.TransformPoint(v))).ToArray();
            Assert.That(vertices.Min(v => v.x), Is.EqualTo(-8.68f).Within(.005f),
                "The imported chest back must meet the wall in actual metres.");
            Assert.That(vertices.Max(v => v.x), Is.EqualTo(-7.995f).Within(.005f));
            Assert.That(vertices.Max(v => v.z) - vertices.Min(v => v.z), Is.EqualTo(1.2f).Within(.005f));

            Physics.SyncTransforms();
            // Follow the former obstruction from the room to the cot dock.
            for (int step = 0; step <= 12; step++)
            {
                Vector3 point = LodgePoint(root, -5.86f, .02f, -2f + step * .2f);
                Assert.That(root.World.WalkableArea.Contains(point, .32f), Is.True,
                    "The pure walkable plan must release the old chest footprint.");
                Assert.That(AbandonmentCapsuleFree(point), Is.True,
                    "The imported chest collider must leave the full cot approach open.");
            }

            var input = new InputTestFixture();
            float previousStep = Time.captureDeltaTime;
            input.Setup();
            try
            {
                Time.captureDeltaTime = .05f;
                Keyboard keyboard = InputSystem.AddDevice<Keyboard>();
                yield return VerifyLodgeThreshold(root, input, keyboard);
                yield return VerifyLodgeCellar(root, input, keyboard);
            }
            finally
            {
                NarrativeInteractionController.For(root.Player.Interactor).RestoreImmediate();
                root.Player.Motor.CancelInteractionPoseMove();
                Time.captureDeltaTime = previousStep;
                input.TearDown();
            }
        }

        private static IEnumerator VerifyLodgeThreshold(AlpineVillageRoot root, InputTestFixture input, Keyboard keyboard)
        {
            Transform lodge = root.LodgeShelter.transform;
            Transform sill = lodge.Find("LodgeDoorThreshold");
            Assert.That(sill, Is.Not.Null);
            Vector3[] sillPoints = sill.GetComponent<MeshFilter>().sharedMesh.vertices
                .Select(v => lodge.InverseTransformPoint(sill.TransformPoint(v))).ToArray();
            Assert.That(sillPoints.Max(v => v.y), Is.EqualTo(AlpineVillageExpansionPlan.LodgeThresholdTop).Within(.002f));
            Assert.That(sillPoints.Min(v => v.z), Is.EqualTo(AlpineVillageExpansionPlan.LodgeThresholdOuterZ).Within(.002f));
            Assert.That(sillPoints.Max(v => v.z), Is.EqualTo(AlpineVillageExpansionPlan.LodgeThresholdInnerZ).Within(.002f));
            Assert.That(sillPoints.Max(v => v.x) - sillPoints.Min(v => v.x),
                Is.EqualTo(AlpineVillageExpansionPlan.LodgeThresholdWidth).Within(.002f));
            Assert.That(sill.GetComponent<MeshCollider>(), Is.Not.Null, "The visible sill must carry the hero.");

            MeshFilter path = root.World.Root.GetComponentsInChildren<MeshFilter>().Single(filter =>
                filter.name == "Visible Path - village-ski-base-approach-1");
            Vector3[] pathPoints = path.sharedMesh.vertices
                .Select(v => lodge.InverseTransformPoint(path.transform.TransformPoint(v))).ToArray();
            float boundary = AlpineVillageExpansionPlan.LodgeThresholdOuterZ;
            Assert.That(pathPoints.Max(v => v.z), Is.EqualTo(boundary).Within(.002f),
                "The path must end at the sill, without a round cap entering the hall.");
            Vector3[] seam = pathPoints.Where(v => Mathf.Abs(v.z - boundary) < .002f).ToArray();
            Assert.That(seam.Length, Is.GreaterThan(2));
            Assert.That(seam.Max(v => v.x) - seam.Min(v => v.x), Is.EqualTo(2.5f).Within(.003f));
            foreach (Vector3 point in seam)
                Assert.That(point.y, Is.EqualTo(AlpineVillageExpansionPlan.LodgeThresholdTop -
                    AlpineVillageExpansionPlan.LodgeThresholdChamfer).Within(.003f), "The two real surfaces must meet.");

            // Walk normally through each open half, in both directions. The
            // other leaf stays closed; a ray alone cannot prove step traversal.
            foreach (int openLeaf in new[] { 0, 1 })
            {
                PlaceLodgeHero(root, new Vector3(1.1f, .02f, -2f));
                SetLodgeDoors(root.LodgeShelter, 1 << openLeaf);
                float x = openLeaf == 0 ? -.654f : .654f;
                var closedRay = new Ray(LodgePoint(root, -x, 1.2f, -7f), lodge.forward);
                Assert.That(Physics.Raycast(closedRay, out RaycastHit closedHit, 2.1f,
                    PlayerInteractor.InteractionLayerMask, QueryTriggerInteraction.Ignore), Is.True);
                Assert.That(closedHit.transform.IsChildOf(root.LodgeShelter.Hinge(1 - openLeaf)), Is.True);
                for (float z = -6.8f; z <= -5.3f; z += .1f)
                {
                    Vector3 floorPoint = LodgePoint(root, x, 0f, z);
                    Assert.That(root.World.WalkableArea.Contains(floorPoint, .32f), Is.True);
                    Assert.That(Physics.Raycast(floorPoint + Vector3.up, Vector3.down, out RaycastHit support, 2f,
                        PlayerInteractor.InteractionLayerMask, QueryTriggerInteraction.Ignore), Is.True);
                    Assert.That(lodge.InverseTransformPoint(support.point).y, Is.InRange(-.10f, .075f),
                        "The threshold route has no hole or oversized step.");
                }
                PlaceLodgeHero(root, new Vector3(x, 0f, -7f));
                root.Player.Motor.transform.rotation = lodge.rotation;
                root.CameraFollow.ClearFixedPose(); root.CameraFollow.Snap();
                for (int frame = 0; frame < 4; frame++) yield return null;
                input.Press(keyboard.wKey, queueEventOnly: true);
                for (int frame = 0; frame < 100 && lodge.InverseTransformPoint(root.Player.Motor.transform.position).z < -5.1f; frame++)
                    yield return null;
                input.Release(keyboard.wKey, queueEventOnly: true);
                yield return null;
                Assert.That(lodge.InverseTransformPoint(root.Player.Motor.transform.position).z, Is.GreaterThanOrEqualTo(-5.1f),
                    "Held W must carry the ordinary hero over the sill through open leaf " + openLeaf);
                Assert.That(root.Player.Motor.InteractionPoseMoveActive, Is.False);
                input.Press(keyboard.sKey, queueEventOnly: true);
                for (int frame = 0; frame < 100 && lodge.InverseTransformPoint(root.Player.Motor.transform.position).z > -7f; frame++)
                    yield return null;
                input.Release(keyboard.sKey, queueEventOnly: true);
                yield return null;
                Assert.That(lodge.InverseTransformPoint(root.Player.Motor.transform.position).z, Is.LessThanOrEqualTo(-7f),
                    "Held S must carry the ordinary hero back outside through open leaf " + openLeaf);
            }
            PlaceLodgeHero(root, new Vector3(1.1f, .02f, -2f));
            SetLodgeDoors(root.LodgeShelter, 3);
        }

        private static IEnumerator VerifyLodgeCellar(AlpineVillageRoot root, InputTestFixture input, Keyboard keyboard)
        {
            NarrativeInteraction target = root.LodgeInterior.CellarHatch;
            Assert.That(target, Is.Not.Null);
            var hero = root.Player.Interactor;
            var session = NarrativeInteractionController.For(hero);
            InteractionPromptView view = root.InteractionPrompt;
            Transform hinge = root.LodgeShelter.GetComponentsInChildren<Transform>(true)
                .Single(child => child.name == "LodgeCellarHatchHinge");
            Assert.That(hinge, Is.Not.Null);
            Quaternion closed = hinge.localRotation;
            InventoryItemStack[] inventory = GameSessionState.InventoryItems.ToArray();
            Vector3 gameplayLook = Vector3.zero;
            float gameplayCameraHeight = 0f;
            bool capturedGameplayApproach = false;
            Assert.That(target.Staging.CameraMode, Is.EqualTo(NarrativeCameraMode.ObjectCloseUp));
            Assert.That(target.Confirmation.ReplyKey, Is.EqualTo("lodge.cellar.locked"));
            Assert.That(target.Confirmation.AttemptSeconds, Is.EqualTo(.95f).Within(.001f));
            try
            {
                foreach (string language in new[] { "ru", "en" })
                {
                    var catalog = JsonUtility.FromJson<NameplateCatalog>(Resources.Load<TextAsset>("Localization/" + language).text);
                    string[] keys = { target.PromptKey, "lodge.cellar.open_question", target.Confirmation.YesKey,
                        target.Confirmation.NoKey, target.Confirmation.ReplyKey };
                    using (new NameplateLanguageScope(catalog.entries.Where(entry => keys.Contains(entry.key)).ToArray()))
                    {
                        foreach (string key in keys) Assert.That(LocalizationService.Get(key), Is.Not.EqualTo(key).And.Not.Empty);
                        string reply = LocalizationService.Get(target.Confirmation.ReplyKey);
                        if (language == "ru") Assert.That(reply, Is.EqualTo("Заперто. Похоже... изнутри?"));
                        Assert.That(LocalizationService.Get(target.Confirmation.YesKey), Is.EqualTo(language == "ru" ? "Да" : "Yes"));
                        Assert.That(LocalizationService.Get(target.Confirmation.NoKey), Is.EqualTo(language == "ru" ? "Нет" : "No"));

                        yield return BeginReading();
                        Assert.That(session.SelectedAnswerYes || view.HeldYesSelected, Is.False, "The question defaults to No.");
                        yield return CaptureNarrativeScreen("lodge-cellar-question-" + language);
                        yield return PressLodgeUse(input, keyboard);
                        Assert.That(session.Phase, Is.EqualTo(NarrativeInteractionPhase.Exiting));
                        Assert.That(Quaternion.Angle(hinge.localRotation, closed), Is.LessThan(.001f));
                        Assert.That(view.IsSpeaking, Is.False, "No leaves the closed hatch without a reply.");
                        yield return WaitForExit();
                        AssertClean();

                        yield return BeginReading();
                        input.Press(keyboard.leftArrowKey, queueEventOnly: true);
                        yield return null;
                        input.Release(keyboard.leftArrowKey, queueEventOnly: true);
                        yield return null;
                        Assert.That(session.SelectedAnswerYes && view.HeldYesSelected, Is.True, "Keyboard changes the actual visible answer.");
                        yield return PressLodgeUse(input, keyboard);
                        Assert.That(session.Phase, Is.EqualTo(NarrativeInteractionPhase.Attempting));
                        Assert.That(view.HasHeldPage || view.IsSpeaking, Is.False, "Yes releases the question before the prop moves.");
                        for (int frame = 0; frame < 12 && Quaternion.Angle(hinge.localRotation, closed) < .5f; frame++) yield return null;
                        Assert.That(Quaternion.Angle(hinge.localRotation, closed), Is.InRange(.5f, 2.01f));
                        using (GameTimeScaleRuntime.AcquirePause())
                        {
                            Quaternion paused = hinge.localRotation;
                            for (int frame = 0; frame < 5; frame++) yield return null;
                            Assert.That(Quaternion.Angle(hinge.localRotation, paused), Is.LessThan(.001f));
                            Assert.That(session.Phase, Is.EqualTo(NarrativeInteractionPhase.Attempting));
                            Assert.That(session.Confirm() || session.SelectAnswer(false), Is.False);
                            Assert.That(view.IsSpeaking, Is.False);
                            if (language == "ru") yield return CaptureNarrativeScreen("lodge-cellar-lid-attempt");
                        }
                        for (int frame = 0; frame < 40 && session.Phase == NarrativeInteractionPhase.Attempting; frame++)
                        {
                            Assert.That(view.HasHeldPage || view.IsSpeaking, Is.False, "The outcome waits until the failed lid motion has ended.");
                            Assert.That(Quaternion.Angle(hinge.localRotation, closed), Is.LessThanOrEqualTo(2.01f));
                            yield return null;
                        }
                        Assert.That(session.Phase, Is.EqualTo(NarrativeInteractionPhase.Outcome));
                        Assert.That(Quaternion.Angle(hinge.localRotation, closed), Is.LessThan(.001f), "The lid shuts before the outcome appears.");
                        yield return null; yield return null;
                        Assert.That(view.HasHeldPage && view.IsHeldBy(session), Is.True);
                        Assert.That(view.HasHeldConfirmation, Is.False, "The result replaces the Yes/No choice.");
                        Assert.That(view.IsSpeaking || view.SpokenBubbles != null && view.SpokenBubbles.IsShowing(session), Is.False);
                        Assert.That(view.GetBottomPromptKeyAt(Time.unscaledTime), Is.EqualTo(target.Confirmation.ReplyKey));
                        Assert.That(view.LastRenderedText, Is.EqualTo(reply));
                        AssertCompactPanel();
                        AssertHatchCloseUp();
                        yield return CaptureNarrativeScreen("lodge-cellar-outcome-" + language);
                        Assert.That(session.Phase, Is.EqualTo(NarrativeInteractionPhase.Outcome), "The silent result stays until dismissed.");
                        yield return PressLodgeUse(input, keyboard);
                        yield return WaitForExit();
                        AssertClean();
                    }
                }

                // Esc has no outcome; source/controller disable must also put
                // a partially moved lid back without a late line or modal loan.
                yield return BeginReading();
                input.Press(keyboard.escapeKey, queueEventOnly: true);
                yield return null;
                input.Release(keyboard.escapeKey, queueEventOnly: true);
                yield return WaitForExit();
                AssertClean();
                foreach (bool disableSource in new[] { true, false })
                {
                    yield return BeginReading();
                    Assert.That(session.SelectAnswer(true) && session.Confirm(), Is.True);
                    for (int frame = 0; frame < 12 && Quaternion.Angle(hinge.localRotation, closed) < .5f; frame++) yield return null;
                    Assert.That(session.Phase, Is.EqualTo(NarrativeInteractionPhase.Attempting));
                    if (disableSource) target.enabled = false; else session.enabled = false;
                    AssertClean();
                    target.enabled = true; session.enabled = true;
                    yield return null;
                }

                NarrativeInteraction photograph = root.LodgeInterior.Photograph;
                root.Player.Motor.Teleport(photograph.Staging.Entry.RootPosition);
                hero.transform.rotation = photograph.Staging.Entry.RootRotation;
                Physics.SyncTransforms(); root.CameraFollow.Snap();
                for (int frame = 0; frame < 3; frame++) yield return null;
                Assert.That(hero.ActiveInteractable, Is.SameAs(photograph));
                yield return PressLodgeUse(input, keyboard);
                for (int frame = 0; frame < 200 && session.Phase != NarrativeInteractionPhase.Reading && session.IsActive; frame++) yield return null;
                Assert.That(session.Phase, Is.EqualTo(NarrativeInteractionPhase.Reading), session.LastFailureReason);
                yield return null; yield return null;
                Assert.That(view.HasHeldPage, Is.True);
                Assert.That(view.HasHeldConfirmation, Is.False, "Existing one-page inspections retain the ordinary page controls.");
                Assert.That(session.CurrentPage.TextKey, Is.EqualTo("lodge.photograph.inspect"));
                Assert.That(session.Confirm(), Is.True);
                yield return WaitForExit();
                AssertClean();
            }
            finally
            {
                session.RestoreImmediate();
                target.enabled = true; session.enabled = true;
            }

            IEnumerator BeginReading()
            {
                root.CameraFollow.ClearFixedPose();
                root.Player.Motor.Teleport(target.Staging.Entry.RootPosition);
                hero.transform.rotation = target.Staging.Entry.RootRotation;
                Physics.SyncTransforms(); root.CameraFollow.Snap();
                // Free orbit can start anywhere. Reproduce the usual approach
                // looking with the hero, then measure the loan from that view.
                root.CameraFollow.RotateYaw(Mathf.DeltaAngle(Camera.main.transform.eulerAngles.y,
                    target.Staging.Entry.RootRotation.eulerAngles.y));
                root.CameraFollow.Snap();
                for (int frame = 0; frame < 3; frame++) yield return null;
                Assert.That(hero.ActiveInteractable, Is.SameAs(target));
                gameplayLook = Vector3.ProjectOnPlane(Camera.main.transform.forward, Vector3.up).normalized;
                gameplayCameraHeight = Camera.main.transform.position.y;
                if (!capturedGameplayApproach)
                {
                    yield return CaptureNarrativeScreen("lodge-cellar-gameplay-approach");
                    capturedGameplayApproach = true;
                }
                Quaternion previousRotation = Camera.main.transform.rotation;
                int framingSamples = 0;
                input.Press(keyboard.eKey, queueEventOnly: true);
                for (int frame = 0; frame < 200 && session.Phase != NarrativeInteractionPhase.Reading &&
                    (session.IsActive || frame == 0); frame++)
                {
                    yield return null;
                    if (frame == 0) input.Release(keyboard.eKey, queueEventOnly: true);
                    if (session.Phase == NarrativeInteractionPhase.Framing || session.Phase == NarrativeInteractionPhase.Reading)
                    {
                        framingSamples++;
                        Assert.That(Quaternion.Angle(previousRotation, Camera.main.transform.rotation), Is.LessThan(12f),
                            "The oblique close-up must lower/approach continuously, without a camera cut.");
                        Assert.That(Vector3.Angle(gameplayLook, Vector3.ProjectOnPlane(Camera.main.transform.forward, Vector3.up)),
                            Is.LessThanOrEqualTo(65f), "The entry must not swing around to the far side of the hatch.");
                        Assert.That(DownwardPitch(), Is.LessThanOrEqualTo(65f), "The entry must never pass through an overhead view.");
                    }
                    previousRotation = Camera.main.transform.rotation;
                }
                Assert.That(session.Phase, Is.EqualTo(NarrativeInteractionPhase.Reading),
                    session.LastFailureReason + "; " + session.CameraDirector.LastRejectedShotReason);
                Assert.That(framingSamples, Is.GreaterThan(10), "The close-up remains a visible camera move.");
                yield return null; yield return null;
                Assert.That(session.CurrentPage.TextKey, Is.EqualTo("lodge.cellar.open_question"));
                Assert.That(view.HasHeldConfirmation && view.IsHeldBy(session), Is.True);
                Assert.That(view.IsSpeaking, Is.False);
                Assert.That(view.LastRenderedText, Is.EqualTo(LocalizationService.Get("lodge.cellar.open_question")));
                AssertCompactPanel();
                Assert.That(session.CameraDirector.CurrentShotIsClear, Is.True);
                yield return CaptureNarrativeScreen("lodge-cellar-oblique-framing");
                AssertHatchCloseUp();
            }

            IEnumerator WaitForExit()
            {
                float deadline = Time.realtimeSinceStartup + 12f;
                while (session.IsActive && Time.realtimeSinceStartup < deadline) yield return null;
                yield return null;
            }

            void AssertCompactPanel()
            {
                Assert.That(view.LastRenderedTextFits, Is.True);
                Assert.That(view.LastRenderedPanelRect.width, Is.InRange(120f, RetroUiTheme.LogicalWidth * .6f));
                Assert.That(view.LastRenderedPanelRect.height, Is.InRange(24f, 86f), "The short question/result uses a compact lower panel.");
            }

            void AssertHatchCloseUp()
            {
                Vector3 cameraOffset = Camera.main.transform.position - target.Staging.FocusBounds.center;
                Vector3 heroRight = target.Staging.Entry.RootRotation * Vector3.right;
                Assert.That(Vector3.Dot(cameraOffset, heroRight), Is.GreaterThan(.25f),
                    "The hatch is viewed obliquely from the approaching hero's right.");
                Vector3 lodgeOffset = root.LodgeShelter.transform.InverseTransformDirection(cameraOffset);
                float orbitAzimuth = Mathf.Atan2(lodgeOffset.z, lodgeOffset.x) * Mathf.Rad2Deg;
                Assert.That(orbitAzimuth, Is.InRange(38f, 47f), "The lens moves left around the hatch in the fixed lodge axes, beyond the previous 70-degree view.");
                Assert.That(cameraOffset.y, Is.InRange(1.05f, 1.35f), "The lens rises above the previous oblique view.");
                Assert.That(DownwardPitch(), Is.InRange(25f, 60f), "The close-up must not become a top-down view.");
                Assert.That(Mathf.Abs(Vector3.Dot(Camera.main.transform.right, Vector3.up)), Is.LessThan(.01f),
                    "The floor view must keep a level horizon.");
                Assert.That(Camera.main.transform.position.y, Is.LessThan(gameplayCameraHeight + .05f),
                    "The camera lowers from the usual gameplay view.");
                Assert.That(Vector3.Angle(gameplayLook, Vector3.ProjectOnPlane(Camera.main.transform.forward, Vector3.up)),
                    Is.LessThanOrEqualTo(65f));
                MeshFilter lid = target.SubjectRoot.GetComponentsInChildren<MeshFilter>()
                    .Single(filter => filter.name == "CellarHatchLid");
                Vector3 lidCenter = Camera.main.WorldToViewportPoint(lid.transform.TransformPoint(lid.sharedMesh.bounds.center));
                Assert.That(lidCenter.x, Is.InRange(.48f, .52f), "The real lid is centred horizontally.");
                Assert.That(lidCenter.y, Is.InRange(.48f, .52f), "The real lid is centred vertically, not panned above the middle.");
                Vector3[] projected = lid.sharedMesh.vertices
                    .Select(vertex => Camera.main.WorldToViewportPoint(lid.transform.TransformPoint(vertex))).ToArray();
                float panelTop = 1f - view.LastRenderedPanelRect.yMin / RetroUiTheme.LogicalHeight;
                foreach (Vector3 viewport in projected)
                {
                    Assert.That(viewport.z, Is.GreaterThan(.1f));
                    Assert.That(viewport.x, Is.InRange(.03f, .97f));
                    Assert.That(viewport.y, Is.InRange(panelTop + .015f, .97f), "The whole real lid stays above the current panel.");
                }
                Assert.That(projected.Max(point => point.x) - projected.Min(point => point.x), Is.GreaterThan(.32f),
                    "The hatch must occupy a meaningful width in its close-up.");
                Assert.That(projected.Max(point => point.y) - projected.Min(point => point.y), Is.GreaterThan(.20f));
                Player3DBoneAnchors body = hero.GetComponentInChildren<Player3DAssetRegistry>().Anchors;
                foreach (Transform anchor in new[] { body.Head, body.Pelvis, body.LeftFoot, body.RightFoot })
                {
                    Vector3 point = Camera.main.WorldToViewportPoint(anchor.position);
                    bool visible = point.z > 0f && point.x > 0f && point.x < 1f && point.y > 0f && point.y < 1f;
                    Assert.That(visible, Is.False, "The hatch close-up must leave the real hero outside the frame: " + anchor.name);
                }
            }

            float DownwardPitch() => Mathf.Asin(Mathf.Clamp(-Camera.main.transform.forward.y, -1f, 1f)) * Mathf.Rad2Deg;

            void AssertClean()
            {
                Assert.That(session.IsActive || view.HasHeldPage || BarMinigameModalLock.IsAnyLocked ||
                    BarPromenade.Rendering.CinematicDepthOfField.IsActive, Is.False);
                Assert.That(view.SpokenBubbles != null && view.SpokenBubbles.IsShowing(session) || view.IsSpeaking, Is.False);
                Assert.That(Quaternion.Angle(hinge.localRotation, closed), Is.LessThan(.001f));
                Assert.That(hero.InputEnabled && root.Player.Motor.InputEnabled, Is.True);
                Assert.That(GameSessionState.InventoryItems, Is.EqualTo(inventory));
                Assert.That(root.CameraFollow.FixedPoseActive, Is.False);
            }
        }

        [UnityTest]
        [Explicit("Focused lodge furniture, three seats, inspections and collectible group photograph.")]
        [PrebuildSetup(typeof(VillageArtAssetsSetup))]
        public IEnumerator AlpineVillageLodgeFurnishings()
        {
            Assert.That(Application.isBatchMode, Is.False, "A Game view is needed to inspect the real bottom UI.");
#if UNITY_EDITOR
            var gameView = UnityEditor.EditorWindow.GetWindow(typeof(UnityEditor.EditorWindow).Assembly.GetType("UnityEditor.GameView"));
            gameView.Show(); gameView.Focus();
#endif
            GameSessionState.BeginNewGame();
            GameSessionState.TryStartGameTimeFromWake();
            Assert.That(GameSessionState.TrySetDebugGameDay(2), Is.True);
            GameSessionState.AdvanceGameTime((float)(100f / GameTimeState.GameMinutesPerRealSecond));
            AlpineVillageRoot root = null;
            yield return Capture(SceneIds.AlpineVillage, () =>
            {
                root = Object.FindAnyObjectByType<AlpineVillageRoot>();
                return root != null && root.IsInitialized ? root : null;
            }, () =>
            {
                PlaceLodgeHero(root, new Vector3(1.1f, .02f, -2f));
                return new[] { Shot.At("lodge-00-interior-unlit", LodgePoint(root, -.8f, 1.85f, -4.5f),
                    LodgePoint(root, 0f, 1.05f, 2f), 94f) };
            });
            Assert.That(root.LodgeShelter.transform.Find("Benches"), Is.Null);
            yield return VerifyLodgeInterior(root);
        }

        [UnityTest]
        [Explicit("Focused furnished lodge, chair/inspections, physical doors and interior wind capture.")]
        [PrebuildSetup(typeof(VillageArtAssetsSetup))]
        public IEnumerator AlpineVillageLodgeShelter()
        {
            GameSessionState.BeginNewGame();
            GameSessionState.TryStartGameTimeFromWake();
            Assert.That(GameSessionState.TrySetDebugGameDay(2), Is.True);
            GameSessionState.AdvanceGameTime((float)(100f / GameTimeState.GameMinutesPerRealSecond));
            AlpineVillageRoot root = null;
            yield return Capture(SceneIds.AlpineVillage, () =>
            {
                root = Object.FindAnyObjectByType<AlpineVillageRoot>();
                return root != null && root.IsInitialized ? root : null;
            }, () =>
            {
                PlaceLodgeHero(root, new Vector3(1.1f, .02f, -2f));
                return new[]
                {
                    Shot.At("lodge-00-interior-unlit", LodgePoint(root, -1.1f, 1.72f, -4.8f),
                        LodgePoint(root, 1f, 1.25f, 1.3f), 80f),
                    Shot.At("lodge-01-exterior-open", LodgePoint(root, -2f, 1.72f, -11f),
                        LodgePoint(root, 0f, 1.5f, -5.8f), 67f)
                };
            });

            LodgeShelterController lodge = root.LodgeShelter;
            Assert.That(lodge, Is.Not.Null);
            Assert.That(lodge.transform.Find("OpenDoorLeaves"), Is.Null, "The old combined model must be replaced.");
            Assert.That(lodge.transform.Find("Benches"), Is.Null, "The two oversized freestanding benches must be removed.");
            Assert.That(lodge.Hinge(0), Is.Not.SameAs(lodge.Hinge(1)));
            Assert.That(lodge.LanternLight.enabled, Is.False);
            Assert.That(root.Music.IsPlaybackSuppressed, Is.True, "The initial open doors keep the village theme silent.");
            Assert.That(root.Music.Source.isPlaying, Is.False);
            Assert.That(root.Music.NormalizedGain, Is.Zero);
            AudioClip windClip = root.WindSound.ActiveClip;
            foreach (AudioListener listener in Object.FindObjectsByType<AudioListener>(FindObjectsInactive.Exclude))
                Assert.That(listener.GetComponents<AudioLowPassFilter>().Any(filter => filter.enabled), Is.False,
                    "Closing the lodge must not filter the listener or local indoor sounds.");

            // All four combinations use the actual imported mesh colliders.
            // Neither door may change the other door's pose or session state.
            foreach (int mask in new[] { 3, 1, 2, 0 })
            {
                PlaceLodgeHero(root, new Vector3(1.1f, .02f, -2f));
                SetLodgeDoors(lodge, mask);
                yield return null;
                Assert.That(root.Music.IsPlaybackSuppressed, Is.EqualTo(mask != 0),
                    "The actual door combination gates the village theme, mask " + mask);
                if (mask != 0)
                {
                    Assert.That(root.Music.NormalizedGain, Is.Zero);
                    Assert.That(root.Music.Source.isPlaying, Is.False);
                }
                for (int index = 0; index < 2; index++)
                {
                    bool open = (mask & (1 << index)) != 0;
                    Assert.That(LodgeShelterSessionState.IsDoorOpen(index), Is.EqualTo(open));
                    float x = index == 0 ? -.654f : .654f;
                    var ray = new Ray(LodgePoint(root, x, 1.2f, -7f), lodge.transform.forward);
                    Assert.That(Physics.Raycast(ray, out RaycastHit hit, 2.1f,
                        PlayerInteractor.InteractionLayerMask, QueryTriggerInteraction.Ignore), Is.EqualTo(!open),
                        "Actual half-opening collision, mask " + mask + ", leaf " + index);
                    if (!open) Assert.That(hit.transform.IsChildOf(lodge.Hinge(index)), Is.True);
                    float angle = Quaternion.Angle(lodge.Hinge(index).localRotation, Quaternion.identity);
                    Assert.That(angle, Is.EqualTo(open ? 180f : 0f).Within(.01f));
                }
                root.Workroom.Environment.Advance(1f);
                root.InteriorAcoustics.Advance(1f);
                root.WindSound.SetNormalizedStrength(.9f);
                float expected = VillageInteriorAcoustics.EvaluateLodgeEnclosure(true, lodge.OpenDoorCount);
                Assert.That(root.InteriorAcoustics.Enclosure, Is.EqualTo(expected).Within(.001f));
                Assert.That(root.WindSound.Enclosure, Is.EqualTo(expected).Within(.001f),
                    "The workroom must not overwrite the lodge enclosure.");
                float gain = Mathf.Lerp(1f, VillageInteriorAcoustics.LodgeClosedVolumeMultiplier, expected);
                float cutoff = Mathf.Lerp(1f, VillageInteriorAcoustics.LodgeClosedCutoffMultiplier, expected);
                Assert.That(root.WindSound.Source.volume,
                    Is.EqualTo(MountainRoadWindSoundPlayer.MaximumVolume * Mathf.Pow(.9f, .85f) * gain).Within(.0001f));
                Assert.That(root.WindSound.ToneFilter.cutoffFrequency,
                    Is.EqualTo(Mathf.Lerp(700f, 2600f, .9f) * cutoff).Within(1f));
                Assert.That(root.WindSound.ActiveClip, Is.SameAs(windClip));
            }
            yield return VerifyLodgeMusicTransitions(root);

            SetLodgeDoors(lodge, 3);
            foreach (Vector3 occupied in new[] { new Vector3(-.65f, .02f, -5.985f), new Vector3(-.16f, .02f, -6.43f) })
            {
                PlaceLodgeHero(root, occupied);
                Assert.That(lodge.TrySetDoorOpen(0, false), Is.False,
                    "Neither the panel nor its projecting grip may appear through the hero at " + occupied);
                Assert.That(LodgeShelterSessionState.LeftDoorOpen, Is.True);
            }
            PlaceLodgeHero(root, new Vector3(1.1f, .02f, -2f));

            var input = new InputTestFixture();
            input.Setup();
            try
            {
                Keyboard keyboard = InputSystem.AddDevice<Keyboard>();
                // The same shared E path works from inside and outside. In
                // particular, the guest can close both leaves after entering.
                foreach (bool inside in new[] { true, false })
                for (int index = 0; index < 2; index++)
                {
                    LodgeShelterInteraction door = lodge.Door(index);
                    Vector3 dock = door.InteractionPosition + lodge.transform.forward * (inside ? .88f : -.88f);
                    root.Player.Motor.Teleport(dock);
                    for (int frame = 0; frame < 3; frame++) yield return null;
                    Assert.That(root.Player.Interactor.ActiveInteractable, Is.SameAs(door),
                        "Door E target, inside=" + inside + ", leaf=" + index);
                    Assert.That(root.InteractionPrompt.PromptKey, Is.EqualTo(door.PromptKey));
                    bool wasOpen = LodgeShelterSessionState.IsDoorOpen(index);
                    bool otherOpen = LodgeShelterSessionState.IsDoorOpen(1 - index);
                    yield return PressLodgeUse(input, keyboard);
                    Assert.That(LodgeShelterSessionState.IsDoorOpen(index), Is.EqualTo(!wasOpen));
                    Assert.That(LodgeShelterSessionState.IsDoorOpen(1 - index), Is.EqualTo(otherOpen));
                }

                foreach (LodgeShelterInteraction stub in new[] { lodge.Cot, lodge.Kettle })
                {
                    root.Player.Motor.Teleport(stub.InteractionPosition);
                    for (int frame = 0; frame < 3; frame++) yield return null;
                    Assert.That(root.Player.Interactor.ActiveInteractable, Is.SameAs(stub));
                    InventoryItemStack[] before = GameSessionState.InventoryItems.ToArray();
                    double minutes = GameSessionState.GameTimeOfDayMinutes;
                    stub.Interact(root.Player.Interactor);
                    Assert.That(GameSessionState.GameTimeOfDayMinutes, Is.EqualTo(minutes));
                    Assert.That(GameSessionState.InventoryItems, Is.EqualTo(before));
                    Assert.That(root.InteractionPrompt.IsFeedbackVisible, Is.True);
                    Assert.That(root.InteractionPrompt.IsSpeaking, Is.False);
                    Assert.That(root.InteractionPrompt.GetBottomPromptKeyAt(Time.unscaledTime),
                        Is.EqualTo(stub == lodge.Cot ? "lodge.cot.inspect" : "lodge.kettle.inspect"));
                    root.InteractionPrompt.ClearFeedback();
                    yield return null;
                }

                root.Player.Motor.Teleport(lodge.Lantern.InteractionPosition);
                for (int frame = 0; frame < 3; frame++) yield return null;
                Assert.That(root.Player.Interactor.ActiveInteractable, Is.SameAs(lodge.Lantern));
                yield return PressLodgeUse(input, keyboard);
                Assert.That(lodge.LanternLight.enabled && LodgeShelterSessionState.LanternLit, Is.True);
                yield return PressLodgeUse(input, keyboard);
                Assert.That(lodge.LanternLight.enabled || LodgeShelterSessionState.LanternLit, Is.False);
            }
            finally { input.TearDown(); }

            PlaceLodgeHero(root, new Vector3(1.1f, .02f, -2f));
            SetLodgeDoors(lodge, 0);
            Assert.That(lodge.SetLanternLit(true), Is.True);
            root.InteriorAcoustics.Advance(1f);
            using (GameTimeScaleRuntime.AcquirePause())
            {
                Assert.That(lodge.TrySetDoorOpen(0, true), Is.False);
                Assert.That(lodge.SetLanternLit(false), Is.False);
                Assert.That(lodge.Cot.CanInteract(root.Player.Interactor), Is.False);
                float enclosure = root.InteriorAcoustics.Enclosure;
                root.InteriorAcoustics.Advance(2f);
                Assert.That(root.InteriorAcoustics.Enclosure, Is.EqualTo(enclosure));
            }
            lodge.enabled = false;
            Assert.That(lodge.LanternLight.enabled, Is.False);
            lodge.enabled = true;
            lodge.Hinge(0).localRotation = Quaternion.Euler(0f, 73f, 0f);
            lodge.RestoreState();
            Assert.That(lodge.OpenDoorCount, Is.Zero);
            Assert.That(Quaternion.Angle(lodge.Hinge(0).localRotation, Quaternion.identity), Is.LessThan(.01f));
            Assert.That(lodge.LanternLight.enabled, Is.True, "Disable/restore retains the current session's shelter choices.");
            VerifyLodgeWeatherBoundary(root);

            LodgeFrame(root, "lodge-02-interior-lit", new Vector3(-1.1f, 1.72f, -4.8f), new Vector3(1f, 1.25f, 1.3f), 80f);
            LodgeFrame(root, "lodge-03-cot", new Vector3(-3.7f, 1.85f, -1.4f), new Vector3(-7.2f, 1.0f, 3f), 72f);
            LodgeFrame(root, "lodge-04-tea-and-lantern", new Vector3(1.9f, 1.7f, -2.2f), new Vector3(5.4f, 1.0f, 1f), 68f);
            LodgeFrame(root, "lodge-05-exterior-closed", new Vector3(-2f, 1.72f, -11f), new Vector3(0f, 1.5f, -5.8f), 67f);
            yield return CaptureLodgeWind(root);

            PlaceLodgeHero(root, new Vector3(0f, .02f, -8f));
            root.Workroom.Environment.Advance(1f);
            root.InteriorAcoustics.Advance(VillageInteriorAcoustics.TransitionSeconds * .5f);
            Assert.That(root.WindSound.Enclosure, Is.InRange(.45f, .55f), "The acoustic boundary fades instead of snapping.");
            root.InteriorAcoustics.Advance(VillageInteriorAcoustics.TransitionSeconds * .5f);
            Assert.That(root.WindSound.Enclosure, Is.Zero);
            Assert.That(root.WindSound.ActiveClip, Is.SameAs(windClip));
            Assert.That(root.WindSound.Source.isPlaying, Is.True);
            yield return VerifyLodgeWarmth(root);
            GameSessionState.BeginNewGame();
            lodge.RestoreState();
            Assert.That(lodge.OpenDoorCount, Is.EqualTo(2));
            Assert.That(lodge.LanternLight.enabled || LodgeShelterSessionState.LanternLit, Is.False);
            Assert.That(LodgeStoveSessionState.IsBurning, Is.False);
            Assert.That(root.Stove.ProvidesWarmth(root.Stove.Plan.EntryPose.RootPosition), Is.False);
        }

        private static IEnumerator VerifyLodgeInterior(AlpineVillageRoot root)
        {
            LodgeInteriorInteractions room = root.LodgeInterior;
            Assert.That(room, Is.Not.Null);
            Assert.That(room.LoungeChairs.Count, Is.EqualTo(2));
            var hero = root.Player.Interactor;
            var session = NarrativeInteractionController.For(hero);
            var input = new InputTestFixture();
            float previousStep = Time.captureDeltaTime;
            input.Setup();
            try
            {
                Keyboard keyboard = InputSystem.AddDevice<Keyboard>();
                Time.captureDeltaTime = .05f;
                foreach (LodgeShelterInteraction stub in new[] { root.LodgeShelter.Cot, root.LodgeShelter.Kettle })
                {
                    root.Player.Motor.Teleport(stub.InteractionPosition);
                    for (int frame = 0; frame < 3; frame++) yield return null;
                    Assert.That(hero.ActiveInteractable, Is.SameAs(stub), "Moved prop remains reachable.");
                    InventoryItemStack[] before = GameSessionState.InventoryItems.ToArray();
                    yield return PressLodgeUse(input, keyboard);
                    Assert.That(root.InteractionPrompt.IsFeedbackVisible, Is.True);
                    Assert.That(GameSessionState.InventoryItems, Is.EqualTo(before));
                    root.InteractionPrompt.ClearFeedback();
                }
                root.Player.Motor.Teleport(root.LodgeShelter.Lantern.InteractionPosition);
                for (int frame = 0; frame < 3; frame++) yield return null;
                Assert.That(hero.ActiveInteractable, Is.SameAs(root.LodgeShelter.Lantern));
                root.LodgeShelter.SetLanternLit(false);
                yield return PressLodgeUse(input, keyboard);
                Assert.That(root.LodgeShelter.LanternLight.enabled, Is.True);
                yield return PressLodgeUse(input, keyboard);
                Assert.That(root.LodgeShelter.LanternLight.enabled, Is.False);
                root.LodgeShelter.SetLanternLit(true);
                // Warmth remains stove-owned, including while the seated rig
                // is offset from its grounded interaction root.
                PlaceLodgeHero(root, new Vector3(1.1f, .02f, -2f));
                SetLodgeDoors(root.LodgeShelter, 0);
                Assert.That(GameSessionState.TryAddInventoryItem(InventoryItemId.FirewoodLog), Is.True);
                Assert.That(LodgeStoveSessionState.TryPlaceLog(), Is.True);
                Assert.That(LodgeStoveSessionState.TryIgnite(), Is.True);
                foreach (CityBenchSitInteraction chair in new[] { room.Chair }.Concat(room.LoungeChairs))
                {
                    root.Player.Motor.Teleport(chair.Plan.EntryRootPosition);
                    hero.transform.rotation = chair.Plan.EntryRotation;
                    root.CameraFollow.ClearFixedPose(); root.CameraFollow.Snap();
                    Physics.SyncTransforms();
                    for (int frame = 0; frame < 3; frame++) yield return null;
                    Assert.That(hero.ActiveInteractable, Is.SameAs(chair));
                    Assert.That(root.World.WalkableArea.Contains(chair.Plan.EntryRootPosition, .32f), Is.True);
                    Collider[] occupied = Physics.OverlapCapsule(chair.Plan.EntryRootPosition + Vector3.up * .4f,
                        chair.Plan.EntryRootPosition + Vector3.up * 1.4f, .30f,
                        PlayerInteractor.InteractionLayerMask, QueryTriggerInteraction.Ignore);
                    Assert.That(occupied.Where(c => !c.transform.IsChildOf(hero.transform)).Select(c => c.name), Is.Empty,
                        "The physical chair approach must match the walkable plan.");
                    yield return PressLodgeUse(input, keyboard);
                    for (int frame = 0; frame < 200 && !chair.IsSeated; frame++) yield return null;
                    Assert.That(chair.IsSeated, Is.True);
                    Assert.That(root.Stove.ProvidesWarmth(hero.transform.position), Is.True);
                    for (int frame = 0; frame < 12; frame++) yield return null;
                    if (chair == room.Chair)
                        LodgeFrame(root, "lodge-06-chair-rest", new Vector3(-3.8f, 1.6f, -2.5f),
                            new Vector3(-1.1f, .85f, -.2f), 64f, true);
                    else
                        LodgeFrame(root, chair.Plan.Id, new Vector3(6.7f, 1.7f, -1.4f),
                            new Vector3(7f, .8f, -4.4f), 64f, true);
                    yield return PressLodgeUse(input, keyboard);
                    for (int frame = 0; frame < 200 && chair.OwnsActiveInteraction; frame++) yield return null;
                    Assert.That(chair.OwnsActiveInteraction, Is.False);
                    Assert.That(hero.InputEnabled && root.Player.Motor.InputEnabled, Is.True);
                }

                LodgeFrame(root, "lodge-09-minibar-corner", new Vector3(3.7f, 1.8f, -.8f),
                    new Vector3(5.2f, .85f, -4.8f), 72f);

                foreach (string language in new[] { "ru", "en" })
                {
                    var catalog = JsonUtility.FromJson<NameplateCatalog>(Resources.Load<TextAsset>("Localization/" + language).text);
                    using (new NameplateLanguageScope(catalog.entries.Where(e =>
                        e.key.StartsWith("lodge.", System.StringComparison.Ordinal) ||
                        e.key.StartsWith("interaction.lodge", System.StringComparison.Ordinal) ||
                        e.key.StartsWith("narrative.", System.StringComparison.Ordinal)).ToArray()))
                    foreach (NarrativeInteraction target in new[] { room.Photograph, room.SkiEquipment, room.GroupPhotograph })
                    {
                        root.CameraFollow.ClearFixedPose();
                        root.Player.Motor.Teleport(target.Staging.Entry.RootPosition);
                        hero.transform.rotation = target.Staging.Entry.RootRotation;
                        Physics.SyncTransforms(); root.CameraFollow.Snap();
                        for (int frame = 0; frame < 3; frame++) yield return null;
                        Assert.That(hero.ActiveInteractable, Is.SameAs(target), target.Definition.Id);
                        InventoryItemStack[] before = GameSessionState.InventoryItems.ToArray();
                        yield return PressLodgeUse(input, keyboard);
                        for (int frame = 0; frame < 200 && session.Phase != NarrativeInteractionPhase.Reading && session.IsActive; frame++)
                            yield return null;
                        Assert.That(session.Phase, Is.EqualTo(NarrativeInteractionPhase.Reading),
                            target.Definition.Id + ": " + session.LastFailureReason + "; " + session.CameraDirector.LastRejectedShotReason);
                        yield return null; yield return null;
                        Assert.That(session.CameraDirector.CurrentShotIsClear, Is.True);
                        Assert.That(root.InteractionPrompt.IsSpeaking, Is.False);
                        Assert.That(root.InteractionPrompt.LastRenderedTextFits, Is.True, target.Definition.Id + "/" + language);
                        if (target == room.Photograph)
                            Assert.That(Vector3.Dot(Camera.main.transform.forward, root.LodgeShelter.transform.right), Is.GreaterThan(.95f));
                        if (target == room.GroupPhotograph)
                        {
                            Assert.That(Vector3.Dot(Camera.main.transform.forward, -target.Staging.CameraFront), Is.GreaterThan(.995f),
                                "The document camera follows the tabletop frame's authored tilt.");
                            using (GameTimeScaleRuntime.AcquirePause())
                            {
                                Assert.That(session.Confirm(), Is.False);
                                yield return PressLodgeUse(input, keyboard);
                                Assert.That(session.Phase, Is.EqualTo(NarrativeInteractionPhase.Reading));
                                Assert.That(GameSessionState.InventoryItems, Is.EqualTo(before));
                            }
                        }
                        yield return CaptureNarrativeScreen(target.Definition.Id + "-" + language);
                        if (language == "ru") yield return PressLodgeUse(input, keyboard);
                        else session.Cancel();
                        for (int frame = 0; frame < 160 && session.IsActive; frame++) yield return null;
                        if (target == room.GroupPhotograph && language == "ru")
                        {
                            WorldItemFoundScreen found = WorldItemFoundScreen.For(hero);
                            Assert.That(found.IsPresenting, Is.True, "Confirmed final page hands off after releasing its modal.");
                            for (int frame = 0; frame < 120 && !found.IsShowing; frame++) yield return null;
                            Assert.That(found.IsShowing, Is.True);
                            yield return CaptureNarrativeScreen("lodge-group-photograph-item-leave");
                            input.Press(keyboard.escapeKey, queueEventOnly: true);
                            yield return null;
                            input.Release(keyboard.escapeKey, queueEventOnly: true);
                            for (int frame = 0; frame < 120 && found.IsPresenting; frame++) yield return null;
                            Assert.That(room.GroupPhotographModel.gameObject.activeInHierarchy, Is.True);
                            Assert.That(GameSessionState.IsWorldItemCollected(LodgeInteriorInteractions.GroupPhotographId), Is.False);
                        }
                        Assert.That(session.IsActive || root.InteractionPrompt.HasHeldPage || BarMinigameModalLock.IsAnyLocked, Is.False);
                        Assert.That(hero.InputEnabled && root.Player.Motor.InputEnabled, Is.True);
                        Assert.That(GameSessionState.InventoryItems, Is.EqualTo(before));
                    }
                }

                // Source disable restores the real frame before a later deliberate take.
                NarrativeInteraction collectible = room.GroupPhotograph;
                for (int attempt = 0; attempt < 2; attempt++)
                {
                    root.CameraFollow.ClearFixedPose();
                    root.Player.Motor.Teleport(collectible.Staging.Entry.RootPosition);
                    hero.transform.rotation = collectible.Staging.Entry.RootRotation;
                    Physics.SyncTransforms(); root.CameraFollow.Snap();
                    for (int frame = 0; frame < 3; frame++) yield return null;
                    yield return PressLodgeUse(input, keyboard);
                    for (int frame = 0; frame < 200 && session.Phase != NarrativeInteractionPhase.Reading && session.IsActive; frame++)
                        yield return null;
                    Assert.That(session.Phase, Is.EqualTo(NarrativeInteractionPhase.Reading), session.LastFailureReason);
                    yield return null;
                    yield return PressLodgeUse(input, keyboard);
                    for (int frame = 0; frame < 160 && session.IsActive; frame++) yield return null;
                    WorldItemFoundScreen pickup = WorldItemFoundScreen.For(hero);
                    for (int frame = 0; frame < 120 && !pickup.IsShowing; frame++) yield return null;
                    Assert.That(pickup.IsShowing, Is.True);
                    Assert.That(pickup.ActiveItemId, Is.EqualTo(InventoryItemId.LodgeGroupPhotograph));
                    if (attempt == 0)
                    {
                        room.enabled = false;
                        Assert.That(pickup.IsPresenting || BarMinigameModalLock.IsAnyLocked, Is.False);
                        Assert.That(room.GroupPhotographModel.parent, Is.SameAs(room.transform));
                        Assert.That(room.GroupPhotographModel.gameObject.activeInHierarchy, Is.True);
                        Assert.That(GameSessionState.GetInventoryItemCount(InventoryItemId.LodgeGroupPhotograph), Is.Zero);
                        room.enabled = true;
                        continue;
                    }
                    yield return CaptureNarrativeScreen("lodge-group-photograph-item-take");
                    yield return PressLodgeUse(input, keyboard);
                    for (int frame = 0; frame < 120 && pickup.IsPresenting; frame++) yield return null;
                    Assert.That(GameSessionState.GetInventoryItemCount(InventoryItemId.LodgeGroupPhotograph), Is.EqualTo(1));
                    Assert.That(GameSessionState.IsWorldItemCollected(LodgeInteriorInteractions.GroupPhotographId), Is.True);
                    Assert.That(room.GroupPhotographModel.gameObject.activeInHierarchy || collectible.gameObject.activeInHierarchy, Is.False);
                    Assert.That(GameSessionState.TryCollectWorldItem(LodgeInteriorInteractions.GroupPhotographId,
                        InventoryItemId.LodgeGroupPhotograph), Is.False, "A source remains consumed across visits.");
                    room.GroupPhotographModel.gameObject.SetActive(true);
                    collectible.gameObject.SetActive(true);
                    room.RestoreGroupPhotographState();
                    Assert.That(room.GroupPhotographModel.gameObject.activeInHierarchy || collectible.gameObject.activeInHierarchy, Is.False);
                    yield return PressLodgeUse(input, keyboard);
                    Assert.That(GameSessionState.GetInventoryItemCount(InventoryItemId.LodgeGroupPhotograph), Is.EqualTo(1));
                    Assert.That(hero.InputEnabled && root.Player.Motor.InputEnabled, Is.True);
                }
                Texture2D photographIcon = InventoryIconLibrary.GetIcon(InventoryItemId.LodgeGroupPhotograph);
                Assert.That(photographIcon, Is.Not.Null);
                Assert.That(photographIcon.width, Is.EqualTo(32));
                Assert.That(photographIcon.height, Is.EqualTo(32));
                Assert.That(photographIcon.filterMode, Is.EqualTo(FilterMode.Point));
                Assert.That(photographIcon.GetPixels32().Any(pixel => pixel.a > 0), Is.True,
                    "The collected photograph needs a visible inventory icon.");

                InventoryController inventory = root.Inventory;
                Assert.That(inventory.Open(), Is.True);
                int photographIndex = Enumerable.Range(0, GameSessionState.InventoryItems.Count).Single(index =>
                    GameSessionState.InventoryItems[index].ItemId == InventoryItemId.LodgeGroupPhotograph);
                if (inventory.SelectedItemIndex != photographIndex)
                    Assert.That(inventory.SelectItem(photographIndex), Is.True);
                InventoryItemPreviewRenderer preview = inventory.View.PreviewRenderer;
                Assert.That(preview.CurrentItemId, Is.EqualTo(InventoryItemId.LodgeGroupPhotograph));
                Assert.That(preview.IsRendering, Is.True);
                Assert.That(preview.ModelRoot, Is.Not.Null);
                Assert.That(preview.ModelRoot, Is.Not.SameAs(room.GroupPhotographModel),
                    "Inventory builds its own authored model after the world frame has been collected.");
                MeshRenderer[] previewParts = preview.ModelRoot.GetComponentsInChildren<MeshRenderer>();
                Assert.That(previewParts.Select(part => part.name), Is.EquivalentTo(new[]
                    { "LodgeGroupPhotographFrame", "LodgeGroupPhotographEasel", "LodgeGroupPhotographImage" }));
                Assert.That(preview.ModelRoot.GetComponentsInChildren<Collider>(), Is.Empty);
                MeshRenderer previewImage = previewParts.Single(part => part.name == "LodgeGroupPhotographImage");
                var photographBlock = new MaterialPropertyBlock();
                previewImage.GetPropertyBlock(photographBlock);
                Assert.That(photographBlock.GetTexture("_BaseMap"), Is.SameAs(Resources.Load<Texture2D>(
                    VillageExpansionAssetProvider.LodgeGroupPhotographTexturePath)),
                    "The actual inventory renderer must retain the group photograph, not a blank or substitute surface.");
                Bounds previewBounds = WorldItemInspectionPresenter.CalculateWorldBounds(preview.ModelRoot);
                Assert.That(Mathf.Max(previewBounds.size.x, previewBounds.size.y, previewBounds.size.z),
                    Is.InRange(.2f, .6f), "The detached imported frame retains its metre scale.");
                yield return null; yield return null;
                yield return CaptureNarrativeScreen("lodge-group-photograph-inventory");
                Assert.That(inventory.ExamineSelected(), Is.True);
                yield return null;
                yield return CaptureNarrativeScreen("lodge-group-photograph-inventory-examine");
                Assert.That(inventory.Close(), Is.True);
                Assert.That(room.GroupPhotographModel.gameObject.activeInHierarchy, Is.False);
                PlaceLodgeHero(root, new Vector3(1.1f, .02f, -2f));
                LodgeFrame(root, "lodge-07-furnished-warm", new Vector3(-.8f, 1.85f, -4.5f),
                    new Vector3(0f, 1.05f, 2f), 94f);
                LodgeFrame(root, "lodge-08-sleeping-corner", new Vector3(-4.4f, 2.25f, -1.75f),
                    new Vector3(-7.45f, 1.05f, 2.4f), 78f);
            }
            finally
            {
                session.RestoreImmediate();
                root.Inventory.Close();
                WorldItemFoundScreen.For(hero)?.Abandon();
                room.Chair.Controller.CancelActiveInteraction();
                Time.captureDeltaTime = previousStep;
                input.TearDown();
            }
        }

        private static Vector3 LodgePoint(AlpineVillageRoot root, float x, float y, float z) =>
            root.LodgeShelter.transform.TransformPoint(new Vector3(x, y, z));

        private static void PlaceLodgeHero(AlpineVillageRoot root, Vector3 localGround) =>
            root.Player.Motor.Teleport(root.LodgeShelter.transform.TransformPoint(localGround) +
                Vector3.up * PlayerFactory.GroundedRootOffset);

        private static void SetLodgeDoors(LodgeShelterController lodge, int mask)
        {
            for (int index = 0; index < 2; index++)
                Assert.That(lodge.TrySetDoorOpen(index, (mask & (1 << index)) != 0), Is.True);
            Physics.SyncTransforms();
        }

        private static IEnumerator PressLodgeUse(InputTestFixture input, Keyboard keyboard)
        {
            input.Press(keyboard.eKey, queueEventOnly: true);
            yield return null;
            input.Release(keyboard.eKey, queueEventOnly: true);
            yield return null;
        }

        private static IEnumerator VerifyLodgeMusicTransitions(AlpineVillageRoot root)
        {
            AlpineVillageMusicPlayer music = root.Music;
            float deadline = Time.realtimeSinceStartup + 10f;
            while ((music.ActiveClip == null || music.ActiveClip.loadState != AudioDataLoadState.Loaded ||
                    music.PlaybackState == SceneMusicPlaybackState.Loading || music.IsFadeInDeferred) &&
                   Time.realtimeSinceStartup < deadline)
                yield return null;
            Assert.That(music.ActiveClip, Is.Not.Null, "The closed lodge must load the existing village theme.");
            Assert.That(music.ActiveClip.loadState, Is.EqualTo(AudioDataLoadState.Loaded));
            Assert.That(music.IsFadeInDeferred, Is.False);
            AudioClip clip = music.ActiveClip;
            bool enabled = music.enabled;
            try
            {
                // The real Update processes each door change. Between those
                // frames, advance only the shared fade clock for bounded proof.
                music.enabled = false;
                music.AdvanceFade(.5f);
                Assert.That(music.NormalizedGain, Is.InRange(.45f, .95f), "Closing both doors fades in instead of jumping to full gain.");
                music.AdvanceFade(.5f);
                Assert.That(music.NormalizedGain, Is.EqualTo(1f));
                Assert.That(music.Source.volume, Is.EqualTo(1f).Within(.0001f), "The full village theme uses the requested louder source gain.");
                Assert.That(music.Source.isPlaying, Is.True);
                music.Source.timeSamples = Mathf.Min(clip.samples / 2, clip.frequency * 5);

                foreach (int mask in new[] { 1, 2 })
                {
                    SetLodgeDoors(root.LodgeShelter, mask);
                    music.enabled = true;
                    yield return null;
                    music.enabled = false;
                    Assert.That(music.IsPlaybackSuppressed, Is.True);
                    music.AdvanceFade(mask == 1 ? 1f : 1.9f);
                    Assert.That(music.NormalizedGain, mask == 1 ? Is.InRange(.65f, .9f) : Is.InRange(.4f, .6f),
                        "Either open leaf starts the shared four-second tail.");
                    if (mask == 2)
                    {
                        music.AdvanceFade(2.1f);
                        Assert.That(music.NormalizedGain, Is.Zero);
                        Assert.That(music.IsPaused, Is.True);
                        Assert.That(music.Source.isPlaying, Is.False);
                        int pausedSample = music.Source.timeSamples;
                        yield return null;
                        Assert.That(music.Source.timeSamples, Is.EqualTo(pausedSample));
                    }
                    float priorGain = music.NormalizedGain;
                    int priorSample = music.Source.timeSamples;
                    SetLodgeDoors(root.LodgeShelter, 0);
                    music.enabled = true;
                    yield return null;
                    music.enabled = false;
                    Assert.That(music.IsPlaybackSuppressed, Is.False);
                    Assert.That(music.ActiveClip, Is.SameAs(clip));
                    Assert.That(music.NormalizedGain, Is.InRange(priorGain - .001f, priorGain + .1f),
                        "A quick reversal or paused resume fades from the existing gain.");
                    Assert.That(music.Source.timeSamples, Is.GreaterThanOrEqualTo(priorSample - 2048),
                        "Neither quick re-close nor repeated close restarts the theme.");
                    music.AdvanceFade(1f);
                    Assert.That(music.NormalizedGain, Is.EqualTo(1f));
                }

                music.enabled = true;
                foreach (int mask in new[] { 1, 2, 0 })
                {
                    PlaceLodgeHero(root, new Vector3(1.1f, .02f, -2f));
                    SetLodgeDoors(root.LodgeShelter, 0);
                    deadline = Time.realtimeSinceStartup + MusicMix.FadeInSeconds + 2f;
                    while (music.PlaybackState != SceneMusicPlaybackState.Playing &&
                           Time.realtimeSinceStartup < deadline)
                        yield return null;
                    Assert.That(music.PlaybackState, Is.EqualTo(SceneMusicPlaybackState.Playing));
                    SetLodgeDoors(root.LodgeShelter, mask == 0 ? 1 : mask);
                    yield return null;
                    PlaceLodgeHero(root, new Vector3(0f, .02f, -8f));
                    if (mask == 0) SetLodgeDoors(root.LodgeShelter, 0);
                    yield return null;
                    // Reproduce leaving the actual lodge with Update enabled:
                    // either leaf can stay open, or both can be closed behind
                    // the hero before the fade ends. None may restart outside.
                    deadline = Time.realtimeSinceStartup + MusicMix.FadeOutSeconds + 2f;
                    while (!music.IsPaused && Time.realtimeSinceStartup < deadline)
                        yield return null;
                    Assert.That(music.IsPlaybackSuppressed, Is.True);
                    Assert.That(music.IsPaused, Is.True, "Leaving must finish the theme tail, door mask " + mask);
                    Assert.That(music.Source.volume, Is.Zero.Within(.0001f));
                    Assert.That(music.Source.isPlaying, Is.False);
                    foreach (AudioSource source in Object.FindObjectsByType<AudioSource>())
                        if (source.clip == clip)
                            Assert.That(source.isPlaying, Is.False,
                                "No duplicate village theme may continue outside: " + source.name);
                    int pausedSample = music.Source.timeSamples;
                    yield return null;
                    Assert.That(music.Source.timeSamples, Is.EqualTo(pausedSample));
                    Assert.That(music.Source.isPlaying, Is.False);
                }
            }
            finally
            {
                music.enabled = enabled;
                PlaceLodgeHero(root, new Vector3(1.1f, .02f, -2f));
            }
        }

        private static IEnumerator VerifyLodgeWarmth(AlpineVillageRoot root)
        {
            LodgeStoveInteraction stove = root.Stove;
            AlpineColdExposureDriver cold = Object.FindAnyObjectByType<AlpineColdExposureDriver>();
            var hero = root.Player.Visual as Player3DCharacterPresentation;
            Assert.That(cold, Is.Not.Null);
            Assert.That(hero, Is.Not.Null);
            Vector3 far = new Vector3(-5.5f, .02f, 2.5f);
            PlaceLodgeHero(root, far);
            SetLodgeDoors(root.LodgeShelter, 0);
            cold.ResetSession();
            cold.Model.Step(12f, false);
            try
            {
                Assert.That(stove.ProvidesWarmth(root.Player.GameObject.transform.position), Is.False,
                    "Shut doors without a fire do not create heat.");
                Assert.That(cold.IsSheltered, Is.False);
                yield return null;
                float unheatedExposure = cold.Model.ExposureSeconds;
                yield return null;
                Assert.That(cold.Model.ExposureSeconds, Is.GreaterThan(unheatedExposure));
                Assert.That(hero.ColdBodyWeight, Is.GreaterThan(0f));

                // Commit existing fuel/ignition rules directly: the separate
                // ignition capture already owns the three-strike presentation.
                Assert.That(GameSessionState.TryAddInventoryItem(InventoryItemId.FirewoodLog), Is.True);
                Assert.That(LodgeStoveSessionState.TryPlaceLog(), Is.True);
                Assert.That(LodgeStoveSessionState.TryIgnite(), Is.True);
                foreach (Vector3 point in new[] { new Vector3(-8.1f, .02f, -5.2f), new Vector3(8.1f, .02f, -5.2f),
                    new Vector3(-6.7f, .02f, 5.2f), new Vector3(8.1f, .02f, 5.2f),
                    new Vector3(0f, .02f, -5.3f), far })
                {
                    PlaceLodgeHero(root, point);
                    Assert.That(stove.ProvidesWarmth(root.Player.GameObject.transform.position), Is.True, "Heated lodge at " + point);
                    Assert.That(cold.IsSheltered, Is.True, "The live frost driver reads the whole room.");
                    float exposure = cold.Model.ExposureSeconds;
                    yield return null;
                    yield return null;
                    Assert.That(cold.Model.ExposureSeconds, Is.LessThanOrEqualTo(exposure));
                    Assert.That(hero.ColdBodyWeight, Is.Zero);
                    Assert.That(hero.ColdArmWeight, Is.Zero);
                    Assert.That(hero.ColdBreath.IsEmissionEnabled, Is.False);
                }
                Assert.That(cold.Model.ExposureSeconds, Is.LessThan(unheatedExposure), "The lit closed room thaws existing frost.");
                PlaceLodgeHero(root, far);
                foreach (int mask in new[] { 1, 2, 3 })
                {
                    SetLodgeDoors(root.LodgeShelter, mask);
                    Assert.That(stove.ProvidesWarmth(root.Player.GameObject.transform.position), Is.False);
                    Assert.That(stove.ProvidesWarmth(stove.Plan.EntryPose.RootPosition), Is.True,
                        "An open entrance preserves the original nearby stove warmth.");
                    Assert.That(cold.IsSheltered, Is.False);
                }
                SetLodgeDoors(root.LodgeShelter, 0);
                PlaceLodgeHero(root, new Vector3(0f, .02f, -8f));
                Assert.That(stove.ProvidesWarmth(root.Player.GameObject.transform.position), Is.False);
                Assert.That(cold.IsSheltered, Is.False, "A heated lodge cannot warm the outdoor listener.");
            }
            finally { LodgeStoveSessionState.ResetForNewSession(); cold.ResetSession(); }
        }

        private static void LodgeFrame(AlpineVillageRoot root, string name, Vector3 from, Vector3 to, float fov, bool includeHero = false)
        {
            Camera camera = Camera.main;
            Vector3 position = camera.transform.position;
            Quaternion rotation = camera.transform.rotation;
            float previousFov = camera.fieldOfView;
            Renderer[] hero = root.Player.GameObject.GetComponentsInChildren<Renderer>(true).Where(r => r.enabled).ToArray();
            try
            {
                if (!includeHero) foreach (Renderer renderer in hero) renderer.enabled = false;
                Vector3 world = root.LodgeShelter.transform.TransformPoint(from);
                camera.transform.SetPositionAndRotation(world,
                    Quaternion.LookRotation(root.LodgeShelter.transform.TransformPoint(to) - world));
                camera.fieldOfView = fov;
                CaptureCurrentCamera(camera, SceneIds.AlpineVillage, name);
            }
            finally
            {
                foreach (Renderer renderer in hero) if (renderer != null) renderer.enabled = true;
                camera.transform.SetPositionAndRotation(position, rotation);
                camera.fieldOfView = previousFov;
            }
        }

        private static void VerifyLodgeWeatherBoundary(AlpineVillageRoot root)
        {
            foreach (ParticleSystem field in new[] { root.Snow.Particles, root.Fog.Particles,
                         root.BlowingSnow.Particles, root.PeripheralBlizzard.Particles })
            {
                var saved = new ParticleSystem.Particle[field.particleCount];
                int count = field.GetParticles(saved);
                Vector3 Local(Vector3 world)
                {
                    var main = field.main;
                    if (main.simulationSpace == ParticleSystemSimulationSpace.Local) return field.transform.InverseTransformPoint(world);
                    if (main.simulationSpace == ParticleSystemSimulationSpace.Custom && main.customSimulationSpace != null)
                        return main.customSimulationSpace.InverseTransformPoint(world);
                    return world;
                }
                Vector3 inside = Local(LodgePoint(root, 1.1f, 1.5f, -2f));
                Vector3 outside = Local(LodgePoint(root, 1.1f, 1.5f, -8f));
                var probes = new[]
                {
                    new ParticleSystem.Particle { position = inside, remainingLifetime = 2f, startLifetime = 2f, startSize = .1f },
                    new ParticleSystem.Particle { position = outside, remainingLifetime = 2f, startLifetime = 2f, startSize = .1f }
                };
                try
                {
                    field.SetParticles(probes, probes.Length);
                    root.Workroom.Environment.CullInteriorWeather();
                    int kept = field.GetParticles(probes);
                    Assert.That(kept, Is.EqualTo(1), field.name + ": only the interior particle is culled.");
                    Assert.That(Vector3.Distance(probes[0].position, outside), Is.LessThan(.001f));
                }
                finally { field.SetParticles(saved, count); }
            }
        }

        private static IEnumerator CaptureLodgeWind(AlpineVillageRoot root)
        {
            AudioSource wind = root.WindSound.Source;
            AudioSource[] sources = Object.FindObjectsByType<AudioSource>(FindObjectsInactive.Include);
            bool[] muted = sources.Select(source => source.mute).ToArray();
            float volume = AudioListener.volume;
            bool paused = AudioListener.pause;
            bool capturing = false;
            int rate = AudioSettings.outputSampleRate;
            string folder = Path.Combine(Directory.GetCurrentDirectory(), "Captures", SceneIds.AlpineVillage);
            try
            {
                foreach (AudioSource source in sources) source.mute = source != wind;
                AudioListener.pause = false;
                AudioListener.volume = 0f;
                Assert.That(AudioRenderer.Start(), Is.True, "Offline audio warmup must start before unmuting its capture.");
                capturing = true;
                AudioListener.volume = 1f;
                yield return PumpFrostHearthAudio(rate / 5, null, 30, false);
                AudioListener.volume = 0f;
                AudioRenderer.Stop();
                capturing = false;
                yield return null;
                Assert.That(AudioRenderer.Start(), Is.True, "Offline audio capture must start before unmuting its render.");
                capturing = true;
                AudioListener.volume = 1f;
                double openRms = 0d;
                foreach (int mask in new[] { 3, 0 })
                {
                    SetLodgeDoors(root.LodgeShelter, mask);
                    root.Workroom.Environment.Advance(1f);
                    root.InteriorAcoustics.Advance(1f);
                    yield return PumpFrostHearthAudio(rate, null);
                    var samples = new List<float>();
                    yield return PumpFrostHearthAudio(rate * 5, samples);
                    float[] pcm = samples.ToArray();
                    double rms = FrostHearthRms(pcm, rate, false);
                    string name = mask == 3 ? "lodge-wind-open" : "lodge-wind-closed";
                    WritePcmWave(Path.Combine(folder, name + ".wav"), pcm, rate, 2, 1f);
                    TestContext.Out.WriteLine($"{name}: DSP RMS {rms:F6}, cutoff {root.WindSound.ToneFilter.cutoffFrequency:F1} Hz.");
                    Assert.That(rms, Is.GreaterThan(.000001d), "The actual wind remains audible through the walls.");
                    if (mask == 3) openRms = rms;
                    else Assert.That(rms, Is.LessThan(openRms * .10d), "The closed lodge leaves only a near-inaudible exterior wind trace.");
                }
            }
            finally
            {
                AudioListener.volume = 0f;
                if (capturing) AudioRenderer.Stop();
                for (int i = 0; i < sources.Length; i++) if (sources[i] != null) sources[i].mute = muted[i];
                AudioListener.pause = paused;
                AudioListener.volume = volume;
            }
        }
    }
}

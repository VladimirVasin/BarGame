using System;
using System.Collections;
using System.IO;
using BarPromenade.Rendering;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

namespace BarPromenade.Tests.PlayMode
{
    public sealed class PortForemanDialogueAssetsSetup : IPrebuildSetup
    {
        public void Setup()
        {
#if UNITY_EDITOR
            new PortForemanAssetsSetup().Setup();
            Type.GetType("BarPromenade.Editor.PlayerDialogueActionAssetSetup, BarPromenade.Editor", true)
                .GetMethod("BuildOrThrow", Type.EmptyTypes).Invoke(null, null);
#endif
        }
    }

    public sealed partial class AreaCaptureFixture
    {
        [UnityTest]
        [Explicit("Positioned dialogue, both real speakers and choices. Run without -batchmode for Game view UI.")]
        [PrebuildSetup(typeof(PortForemanDialogueAssetsSetup))]
        public IEnumerator CityPortForemanDialogue()
        {
            Assert.That(Application.isBatchMode, Is.False, "Shared speech and choices need an actual Game view capture.");
#if UNITY_EDITOR
            Type viewType = typeof(UnityEditor.EditorWindow).Assembly.GetType("UnityEditor.GameView");
            Assert.That(viewType, Is.Not.Null);
            UnityEditor.EditorWindow view = UnityEditor.EditorWindow.GetWindow(viewType);
            view.Show(); view.Focus();
#endif
            yield return CaptureFocusedPort(ValidatePortForemanDialogue);
        }

        private static IEnumerator ValidatePortForemanDialogue(Camera camera, CityGameRoot city,
            CityPortController port, CityPortCrew crew)
        {
            ValidatePortForemanConversationSchedule();
            ValidatePortForemanSnackTimeline();
            ValidateForemanDialogueGraph();
            yield return ValidateForemanDialogueReservation(camera, city, port, crew);
            var foreman = crew.Foreman;
            var interaction = foreman.Interaction;
            var session = interaction.Session;
            var channel = crew.GetComponent<CityPortConversationController>();
            var hero = city.Player.Interactor;
            var motor = city.Player.Motor;
            var animation = hero.GetComponent<PlayerAnimatedInteractionController>();
            var registry = hero.GetComponentInChildren<Player3DAssetRegistry>();
            var follow = camera.GetComponent<PlayerCameraFollow>();
            var visual = (Player3DCharacterPresentation)city.Player.Visual;
            float savedDelta = Time.captureDeltaTime;
            NameplateLanguageScope language = null;
            double held = CityPortCycle.CycleDurationSeconds - .001d, life = crew.LifeElapsedSeconds;
            GameObject blocker = null;
            // CaptureFocusedPort normally photographs props with the hero
            // hidden. Enable only the real registered body/accessory renderers.
            foreach (Renderer renderer in visual.Renderers) if (renderer != null) renderer.enabled = true;
            motor.SetInputEnabled(true); hero.SetInputEnabled(true); follow.enabled = true;
            follow.ClearFixedPose();
            Time.captureDeltaTime = .1f;
            try
            {
                SetPortForemanHour(12);
                for (int branch = 0; branch < 2; branch++)
                {
                    language?.Dispose();
                    var catalog = JsonUtility.FromJson<NameplateCatalog>(Resources.Load<TextAsset>(
                        "Localization/" + (branch == 0 ? "ru" : "en")).text);
                    language = new NameplateLanguageScope(Array.FindAll(catalog.entries, entry =>
                        entry.key.StartsWith("city.port.foreman.", StringComparison.Ordinal) ||
                        entry.key.StartsWith("interaction.port_foreman_", StringComparison.Ordinal) || entry.key == interaction.PromptKey));
                    PlaceAtStart();
                    Assert.That(interaction.TryResolveStaging(out DialogueStagingPlan promptDock), Is.True);
                    motor.Teleport(promptDock.Entry.RootPosition);
                    Physics.SyncTransforms(); follow.Snap();
                    yield return null; yield return null;
                    Assert.That(hero.ActiveInteractable, Is.SameAs(interaction));
                    var prompt = Object.FindFirstObjectByType<InteractionPromptView>();
                    Assert.That(prompt.GetDisplayedTextAt(Time.unscaledTime), Does.StartWith("E — "));
                    Assert.That(prompt.LastRenderedText, Is.EqualTo(LocalizationService.Get(interaction.PromptKey)));
                    Assert.That(prompt.LastRenderedTextFits, Is.True);
                    yield return CaptureDialogueScreenshot(branch == 0 ? "dialogue-ru-00-prompt" : "dialogue-en-00-prompt");
                    PlaceAtStart();
                    Vector3 start = hero.transform.position;
                    float entryStartedAt = -1f;
                    interaction.Interact(hero);
                    Assert.That(Vector3.Distance(hero.transform.position, start), Is.LessThan(.001f), "E is not a teleport.");
                    bool moved = false;
                    Vector3 previous = start;
                    yield return Until(() => IsLine("offer"), 180, () =>
                    {
                        float step = Vector3.Distance(previous, hero.transform.position);
                        Assert.That(step, Is.LessThan(.42f), "The ordinary motor carries each visible approach step.");
                        moved |= step > .005f;
                        previous = hero.transform.position;
                        if (entryStartedAt < 0f && animation.Phase == PlayerAnimatedInteractionPhase.Entering)
                            entryStartedAt = Time.time;
                    });
                    Assert.That(entryStartedAt, Is.GreaterThanOrEqualTo(0f));
                    Assert.That(Time.time - entryStartedAt, Is.LessThanOrEqualTo(.65f),
                        "The camera and entry settle run together after the visible approach.");
                    Assert.That(moved, Is.True);
                    Assert.That(Vector3.Distance(hero.transform.position, session.Staging.Entry.RootPosition), Is.LessThan(.025f));
                    Assert.That(Quaternion.Angle(hero.transform.rotation, session.Staging.Entry.RootRotation), Is.LessThan(.6f));
                    Assert.That(animation.Phase, Is.EqualTo(PlayerAnimatedInteractionPhase.Looping));
                    AssertOwned(); AssertShot(false);
                    int bites = foreman.Snack.BitesTaken;
                    Assert.That(foreman.EatingWeight, Is.LessThan(.05f));
                    yield return Until(() => session.Bubbles.RevealedTextOf(foreman) == LocalizationService.Get(session.CurrentNode.TextKey), 60);
                    yield return CaptureDialogueUiFrame(session, branch == 0 ? "dialogue-ru-01-foreman" : "dialogue-en-01-foreman", false);

                    if (branch == 0)
                    {
                        using (GameTimeScaleRuntime.AcquirePause())
                        {
                            yield return null;
                            float stopped = session.SpeechClock;
                            Pose pose = new Pose(camera.transform.position, camera.transform.rotation);
                            Vector3 hand = foreman.RightHand.position;
                            string text = session.Bubbles.RevealedTextOf(foreman);
                            for (int i = 0; i < 3; i++) yield return null;
                            Assert.That(session.SpeechClock, Is.EqualTo(stopped));
                            Assert.That(session.Bubbles.RevealedTextOf(foreman), Is.EqualTo(text));
                            Assert.That(session.Bubbles.RenderEnabled, Is.False);
                            Assert.That(Vector3.Distance(camera.transform.position, pose.position), Is.LessThan(.001f));
                            Assert.That(Quaternion.Angle(camera.transform.rotation, pose.rotation), Is.LessThan(.01f));
                            Assert.That(Vector3.Distance(foreman.RightHand.position, hand), Is.LessThan(.001f));
                        }
                    }
                    yield return Until(() => session.IsChoosing, 90);
                    AssertOwned(); AssertShot(true);
                    Assert.That(session.Bubbles.IsShowing(foreman), Is.False);
                    Assert.That(foreman.Snack.BitesTaken, Is.EqualTo(bites), "Listening preserves the interrupted carrot.");
                    double worldTime = GameSessionState.GameTimeOfDayMinutes;
                    for (int i = 0; i < 3; i++) { Sample(); yield return null; }
                    Assert.That(GameSessionState.GameTimeOfDayMinutes, Is.GreaterThan(worldTime), "Dialogue owns input, not the world clock.");
                    yield return CaptureDialogueUiFrame(session, branch == 0 ? "dialogue-ru-02-choice" : "dialogue-en-02-choice", true);
                    Assert.That(interaction.SelectChoice(branch == 0), Is.True);
                    Assert.That(interaction.Confirm(), Is.True);
                    AssertOwned();
                    string heroId = branch == 0 ? "hero_yes" : "hero_no";
                    yield return Until(() => IsLine(heroId), 45);
                    AssertShot(true);
                    Assert.That(animation.IsNestedLoopActionActive, Is.True);
                    Assert.That(animation.NestedLoopActionPhase, Is.EqualTo(PlayerAnimatedInteractionPhase.Looping));
                    float began = session.SpeechClock;
                    yield return Until(() => session.Bubbles.RevealedTextOf(hero) == LocalizationService.Get(session.CurrentNode.TextKey), 45);
                    yield return CaptureDialogueUiFrame(session, branch == 0 ? "dialogue-ru-03-hero" : "dialogue-en-03-hero", false);
                    bool heldPastOneCycle = false;
                    yield return Until(() => IsLine(branch == 0 ? "accept" : "decline"), 100, () =>
                    {
                        AssertOwned();
                        if (IsLine(heroId) && session.SpeechClock - began > PlayerDialogueActions.TalkSeconds + .1f)
                        {
                            heldPastOneCycle = true;
                            Assert.That(animation.NestedLoopActionPhase, Is.EqualTo(PlayerAnimatedInteractionPhase.Looping),
                                "A long spoken line holds the generic nested loop beyond its first cycle.");
                        }
                    });
                    Assert.That(heldPastOneCycle, Is.True);
                    AssertShot(false); AssertOwned();
                    Assert.That(animation.IsNestedLoopActionActive, Is.False);
                    Assert.That(foreman.Snack.BitesTaken, Is.EqualTo(bites));
                    yield return Until(() => session.Phase == DialoguePhase.Exiting, 120);
                    float exitStartedAt = Time.time;
                    yield return Until(() => !session.IsActive, 8);
                    Assert.That(Time.time - exitStartedAt, Is.LessThanOrEqualTo(.75f),
                        "Completion must not wait for the two-second listening loop.");
                    AssertReleased();
                    Assert.That(Vector3.Distance(hero.transform.position, session.Staging.Exit.RootPosition), Is.LessThan(.025f));
                    Debug.Log("FOREMAN DIALOGUE: " + (branch == 0 ? "RU accept" : "EN decline") + " framed, spoken and restored.");
                }

                PlaceAtStart(); interaction.Interact(hero);
                yield return Until(() => session.IsChoosing, 180);
                float cancelledAt = Time.time;
                interaction.Cancel();
                yield return Until(() => !session.IsActive, 8);
                Assert.That(Time.time - cancelledAt, Is.LessThanOrEqualTo(.75f));
                AssertReleased();

                // Cancelling mid-gesture uses the last rendered pose, not two loop seams.
                PlaceAtStart(); interaction.Interact(hero);
                yield return Until(() => session.IsChoosing, 180);
                Sample(); yield return null;
                Assert.That(interaction.Confirm(), Is.True);
                yield return Until(() => IsLine("hero_yes"), 20);
                for (int i = 0; i < 4; i++) { Sample(); yield return null; }
                // Recovery composes after raw clip sampling in LateUpdate.
                // Compare rendered poses, never the temporary pre-blend bones.
                yield return new WaitForEndOfFrame();
                Quaternion handBeforeExit = registry.Anchors.RightGrip.rotation;
                cancelledAt = Time.time;
                interaction.Cancel();
                for (int frame = 0; session.IsActive; frame++)
                {
                    Assert.That(frame, Is.LessThan(8), "Mid-gesture cancellation must release promptly.");
                    Sample(); yield return null;
                    yield return new WaitForEndOfFrame();
                    float handStep = Quaternion.Angle(handBeforeExit, registry.Anchors.RightGrip.rotation);
                    Assert.That(handStep, Is.LessThan(30f), "The held gesture blends into the authored exit without a hand snap.");
                    handBeforeExit = registry.Anchors.RightGrip.rotation;
                }
                Assert.That(Time.time - cancelledAt, Is.LessThanOrEqualTo(.75f));
                AssertReleased();

                PlaceAtStart(); interaction.Interact(hero);
                yield return Until(() => IsLine("offer"), 180);
                session.enabled = false;
                AssertReleased();
                session.enabled = true;

                // A real collider blocks the authored approach; no snap around it.
                PlaceAtStart(false);
                Assert.That(interaction.TryResolveStaging(out DialogueStagingPlan dock), Is.True);
                blocker = new GameObject("Blocked dialogue approach");
                blocker.transform.position = (hero.transform.position + dock.Entry.RootPosition) * .5f + Vector3.up * .75f;
                blocker.transform.rotation = foreman.transform.rotation;
                blocker.AddComponent<BoxCollider>().size = new Vector3(1.4f, 2f, .16f);
                Physics.SyncTransforms();
                interaction.Interact(hero);
                yield return Until(() => session.IsActive, 80);
                yield return Until(() => !session.IsActive, 80);
                AssertReleased();
                Assert.That(Vector3.Distance(hero.transform.position, dock.Entry.RootPosition), Is.GreaterThan(.1f));
                Object.Destroy(blocker); blocker = null;
                yield return null;

                PlaceAtStart(); interaction.Interact(hero);
                yield return Until(() => session.IsChoosing, 180);
                // Unload the actual owner while a choice holds camera and input.
                city.Cannery.ForcePresentation = true; city.Cannery.RefreshPresentation();
                Scene empty = SceneManager.CreateScene("Foreman dialogue cleanup");
                SceneManager.SetActiveScene(empty);
                yield return SceneManager.UnloadSceneAsync(SceneIds.City);
                Assert.That(session == null, Is.True);
                Assert.That(BarMinigameModalLock.IsAnyLocked, Is.False);
                Assert.That(CinematicDepthOfField.IsActive, Is.False);
                Debug.Log("FOREMAN DIALOGUE: cancel, disable, blocked approach and scene unload release their ownership.");
            }
            finally
            {
                if (blocker != null) Object.Destroy(blocker);
                if (session != null) { session.RestoreImmediate(); session.enabled = true; }
                if (channel != null) channel.CancelForemanInteraction();
                Time.captureDeltaTime = savedDelta;
                language?.Dispose();
            }

            void PlaceAtStart(bool angled = true)
            {
                channel.CancelForemanInteraction();
                // The reservation probe drives imported port geometry with a
                // manual clock; publish those transform changes before querying
                // the real floor, as a physics step does during ordinary play.
                Physics.SyncTransforms();
                bool resolved = interaction.TryResolveStaging(out DialogueStagingPlan dock);
                string diagnostic = "The actual dialogue dock must resolve its floor.";
                if (!resolved)
                {
                    Vector3 ground = foreman.transform.position + foreman.transform.forward * CityPortForemanInteraction.ConversationDistance;
                    Vector3 origin = ground + Vector3.up * .4f;
                    RaycastHit[] groundHits = Physics.RaycastAll(origin, Vector3.down, .8f, ~0, QueryTriggerInteraction.Ignore);
                    diagnostic += " Foreman=" + foreman.transform.position.ToString("F3") +
                        "; forward=" + foreman.transform.forward.ToString("F3") + "; origin=" + origin.ToString("F3") +
                        "; hits=" + groundHits.Length;
                    foreach (RaycastHit hit in groundHits)
                        diagnostic += " | " + hit.collider.name + ": point=" + hit.point.ToString("F3") +
                            ", normal=" + hit.normal.ToString("F3") + ", bounds=" + hit.collider.bounds;
                }
                Assert.That(resolved, Is.True, diagnostic);
                motor.SetInputEnabled(true); hero.SetInputEnabled(true);
                motor.Teleport(dock.Entry.RootPosition + foreman.transform.forward * .75f +
                    (angled ? foreman.transform.right * .3f : Vector3.zero));
                hero.transform.rotation = foreman.transform.rotation;
                follow.Snap(); Physics.SyncTransforms();
                channel.Initialize(port, crew, camera, hero.transform, 1537);
                Sample();
            }
            bool IsLine(string id) => session.IsActive && session.Phase == DialoguePhase.Speaking && session.CurrentNode.Id == id;
            void Sample()
            {
                if (GameTimeScaleRuntime.IsPaused) return;
                life += .1d;
                port.ApplyAt(held, 15f); crew.ApplyAt(held, life); channel.ApplyAt(); foreman.ApplyAt(life);
            }
            IEnumerator Until(Func<bool> condition, int maximum, Action inspect = null)
            {
                for (int frame = 0; !condition(); frame++)
                {
                    Assert.That(frame, Is.LessThan(maximum), "Dialogue stalled: " + session.Phase + "/" + session.CurrentNode?.Id);
                    Sample(); yield return null; inspect?.Invoke();
                }
            }
            void AssertOwned()
            {
                Assert.That(BarMinigameModalLock.IsAnyLocked, Is.True);
                Assert.That(hero.InputEnabled || motor.InputEnabled || follow.OrbitInputEnabled, Is.False);
                Assert.That(channel.ForemanInteractionPending, Is.True);
            }
            void AssertReleased()
            {
                Assert.That(session.IsActive || animation.IsActive || channel.ForemanInteractionPending, Is.False);
                Assert.That(BarMinigameModalLock.IsAnyLocked || follow.FixedPoseActive || CinematicDepthOfField.IsActive, Is.False);
                Assert.That(hero.InputEnabled && motor.InputEnabled && follow.OrbitInputEnabled, Is.True);
            }
            void AssertShot(bool player)
            {
                var director = session.CameraDirector;
                Assert.That(director.IsSettled && director.CurrentShotIsClear, Is.True);
                Assert.That(director.CurrentSpeakerIsHero, Is.EqualTo(player));
                Transform head = player ? registry.Anchors.Head : foreman.Head;
                Vector3 face = camera.WorldToViewportPoint(head.position + Vector3.up * .1f);
                Vector3 bubble = camera.WorldToViewportPoint(head.position + Vector3.up * NpcSpeechBubbleView.AnchorClearanceMeters);
                Assert.That(face.z, Is.GreaterThan(1f));
                Assert.That(face.x, Is.InRange(.25f, .75f));
                Assert.That(face.y, Is.InRange(.48f, .85f));
                Assert.That(bubble.y, Is.InRange(.65f, .96f));
                Assert.That(camera.fieldOfView, Is.InRange(28f, 48f));
                Assert.That(registry.Renderers[0].enabled, Is.True, "The production hero remains visible.");
            }
        }

        private static IEnumerator CaptureDialogueUiFrame(DialogueSessionController session, string name, bool choices)
        {
            for (int frame = 0; frame < 2; frame++) yield return null;
            if (choices)
            {
                Assert.That(session.IsChoosing, Is.True);
                Assert.That(session.LastChoicePanelRect.yMin, Is.GreaterThan(250f));
                Assert.That(session.LastChoicePanelRect.yMax, Is.LessThan(360f));
            }
            else
            {
                Assert.That(session.Bubbles.HasRenderedLayout, Is.True);
                Assert.That(session.Bubbles.LastRenderedBubbleCount, Is.EqualTo(1));
                Assert.That(session.Bubbles.LastRenderedPanelRect.yMax, Is.LessThan(180f));
            }
            yield return CaptureDialogueScreenshot(name);
        }

        private static IEnumerator CaptureDialogueScreenshot(string name)
        {
            string path = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "Captures", SceneIds.City, name + ".png"));
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            DateTime previous = File.Exists(path) ? File.GetLastWriteTimeUtc(path) : DateTime.MinValue;
            ScreenCapture.CaptureScreenshot(path);
            float deadline = Time.realtimeSinceStartup + 3f;
            do { yield return null; }
            while ((!File.Exists(path) || File.GetLastWriteTimeUtc(path) <= previous) && Time.realtimeSinceStartup < deadline);
            Assert.That(File.Exists(path) && File.GetLastWriteTimeUtc(path) > previous, Is.True, "Capture the actual shared UI, not a rebuilt overlay.");
            Debug.Log("FOREMAN DIALOGUE FRAME: " + name);
        }

        private static void ValidateForemanDialogueGraph()
        {
            for (int answer = 0; answer < 2; answer++)
            {
                var cursor = new DialogueCursor(CityPortForemanInteraction.Graph);
                Assert.That(cursor.Current.Id, Is.EqualTo("offer"));
                Assert.That(cursor.Select(answer), Is.False);
                Assert.That(cursor.CompleteLine(), Is.True);
                Assert.That(cursor.Select(-1), Is.False);
                Assert.That(cursor.Select(answer), Is.True);
                Assert.That(cursor.Current.Speaker, Is.EqualTo(DialogueSpeaker.Hero));
                Assert.That(cursor.CompleteLine(), Is.True);
                Assert.That(cursor.Current.Id, Is.EqualTo(answer == 0 ? "accept" : "decline"));
                Assert.That(cursor.CompleteLine(), Is.True);
                Assert.That(cursor.Current.Kind, Is.EqualTo(DialogueNodeKind.End));
                Assert.That(cursor.CompleteLine() || cursor.Select(0), Is.False);
            }
            Assert.Throws<ArgumentException>(() => new DialogueGraph("line", DialogueNode.Line("line", DialogueSpeaker.Npc, "text", "missing")));
            Assert.Throws<ArgumentException>(() => new DialogueGraph("line", DialogueNode.Line("line", DialogueSpeaker.Npc, "text", "line")));
        }
    }
}

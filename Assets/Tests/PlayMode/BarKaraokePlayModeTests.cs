using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

namespace BarPromenade.Tests.PlayMode
{
    public sealed class KaraokeAssetsSetup : IPrebuildSetup
    {
        public void Setup()
        {
#if UNITY_EDITOR
            Type.GetType("BarPromenade.Editor.PlayerKaraokeActionAssetSetup, BarPromenade.Editor", true)
                .GetMethod("BuildOrThrow", Type.EmptyTypes).Invoke(null, null);
#endif
        }
    }

    [PrebuildSetup(typeof(KaraokeAssetsSetup))]
    public sealed class BarKaraokePlayModeTests
    {
        private BarInteriorRoot bar;
        private InputTestFixture input;
        private Mouse mouse;
        private bool previousFocus;

        [UnitySetUp]
        public IEnumerator SetUp()
        {
            previousFocus = GameCursor.ApplicationFocused;
            input = new InputTestFixture(); input.Setup();
            mouse = InputSystem.AddDevice<Mouse>();
            InputSystem.AddDevice<Keyboard>();
            GameInput.HandleApplicationFocus(true);
            GameSessionState.BeginNewGame("karaoke-test");
            BarTestTime.StartFullAttendance("karaoke-test");
            GameSessionState.EnterBar("karaoke-test", BarActivityKind.Cocktail);
            yield return null;
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            if (bar != null) { bar.Karaoke.CancelInteraction(); if (bar.PauseMenu.IsOpen) bar.PauseMenu.Cancel(); }
            yield return null;
            yield return AreaCaptureFixture.ReleaseBarService();
            Scene scene = SceneManager.GetSceneByName(SceneIds.BarInterior);
            if (scene.IsValid() && scene.isLoaded)
            {
                Scene cleanup = SceneManager.CreateScene("Karaoke Fixture Exit");
                SceneManager.SetActiveScene(cleanup);
                yield return SceneManager.UnloadSceneAsync(scene);
            }
            input?.TearDown(); input = null;
            GameInput.HandleApplicationFocus(previousFocus);
            GameSessionState.BeginNewGame("karaoke-test-exit");
            bar = null;
            yield return null;
        }

        [Test]
        public void PitchControl_IsContinuousBoundedAndRequiresFreshClick()
        {
            var state = new KaraokeControlState();
            state.Advance(0f, true, true);
            Assert.That(state.IsSinging, Is.False, "Entry never inherits an already held click.");
            state.Advance(0f, false, true);
            state.Advance(.25f, true, true);
            Assert.That(state.IsSinging, Is.True);
            Assert.That(state.FrequencyHz, Is.EqualTo(220f * Mathf.Sqrt(2f)).Within(.001f));
            state.Advance(20f, true, true);
            Assert.That(state.FrequencyHz, Is.EqualTo(440f).Within(.001f));
            state.Advance(-40f, false, true);
            Assert.That(state.FrequencyHz, Is.EqualTo(110f).Within(.001f));
            state.Advance(.125f, true, true);
            Assert.That(state.FrequencyHz, Is.EqualTo(110f * Mathf.Pow(2f, .25f)).Within(.001f));
            state.Advance(0f, true, false);
            state.Advance(0f, true, true);
            Assert.That(state.IsSinging, Is.False, "Focus/pause restoration waits for release.");
            state.Advance(0f, false, true);
            state.Advance(0f, true, true);
            Assert.That(state.IsSinging, Is.True);
        }

        [UnityTest]
        public IEnumerator Singing_UsesContinuousHeroAndRestoresEveryOwner()
        {
            Assert.That(Application.isBatchMode, Is.False, "This focused gate reviews actual rendered Game views.");
            yield return SceneManager.LoadSceneAsync(SceneIds.BarInterior, LoadSceneMode.Single);
            yield return Until(() => (bar = Object.FindAnyObjectByType<BarInteriorRoot>()) != null && bar.IsInitialized,
                "Production bar initialization");
            bar.ArrivalPresentation?.Skip();
            bar.Crowd.AutoAdvance = false;
            foreach (BarPatron patron in bar.Patrons)
            {
                for (int phase = 0; phase < 4 && patron.Drinking.Timeline.Phase != BarPatronDrinkPhase.Rest; phase++)
                    patron.Drinking.Timeline.Advance(Mathf.Max(.001f,
                        patron.Drinking.Timeline.PhaseDuration - patron.Drinking.Timeline.PhaseElapsed + .0001f));
            }
            Assert.That(bar.Patrons.Count, Is.EqualTo(BarBusinessHours.MaximumGuests));
            var patronRoots = new Vector3[bar.Patrons.Count];
            for (int i = 0; i < patronRoots.Length; i++) patronRoots[i] = bar.Patrons[i].Presentation.transform.position;
            GameInput.HandleApplicationFocus(true);
            GameObject hero = bar.Player.GameObject;
            var presentation = (Player3DCharacterPresentation)bar.Player.Visual;
            Player3DAssetRegistry registry = presentation.Registry;
            PlayerCameraFollow follow = Camera.main.GetComponent<PlayerCameraFollow>();
            BarKaraokeInteraction karaoke = bar.Karaoke;
            Light key = null;
            foreach (Light practical in bar.Atmosphere.PracticalLights)
                if (practical.name == "stage-key") key = practical;
            Assert.That(key, Is.Not.Null);
            Assert.That(key.color, Is.EqualTo(new Color(1f, .86f, .65f)));
            Assert.That(key.spotAngle, Is.EqualTo(120f));
            Assert.That(key.transform.position.x, Is.LessThan(bar.Room.TransformPoint(
                BarInteriorLayoutPlanner.StageStandingPosition).x), "The practical lights the front of the singer.");
            Assert.That(bar.Atmosphere.PracticalLights.Count, Is.EqualTo(8));
            Assert.That(bar.Lighting.BounceRegionCount, Is.EqualTo(4));
            bar.Player.Motor.Teleport(bar.Room.TransformPoint(BarInteriorLayoutPlanner.StageApproachPosition));
            yield return null;
            input.Press(mouse.leftButton);
            Assert.That(karaoke.BeginInteraction(), Is.True, karaoke.LastFailureReason);
            Assert.That(bar.Player.Motor.InputEnabled, Is.False);
            yield return Until(() => karaoke.Phase == BarKaraokePhase.Singing, "Approach and camera: " + karaoke.LastFailureReason);
            Assert.That(bar.Player.GameObject, Is.SameAs(hero));
            Assert.That(Vector3.Distance(hero.transform.position, karaoke.Plan.EntryPose.RootPosition), Is.LessThan(.03f));
            Assert.That(karaoke.Voice.IsSinging, Is.False, "A held entry click stays silent.");
            Assert.That(karaoke.CameraDirector.IsSettled, Is.True);
            Assert.That(karaoke.View.IsOpen, Is.True);
            Assert.That(karaoke.IsPerforming, Is.False, "Approaching and selecting pitch are not a performance.");
            Assert.That(karaoke.Audience, Is.Not.Null);
            Assert.That(karaoke.Voice.Source.transform.parent, Is.SameAs(registry.Anchors.Head));
            Assert.That(karaoke.Voice.Source.pitch, Is.EqualTo(1f), "The DSP output applies rate once, after clip playback.");
            Assert.That(karaoke.Voice.AppliedPlaybackRate, Is.EqualTo(GameTimeScaleRuntime.PerceptionPlaybackRate).Within(.001f));
            Assert.That(karaoke.Voice.Source.GetComponent<KaraokeVoiceOutput>(), Is.Not.Null);
            foreach (BarKaraokeCameraShot shot in karaoke.Plan.CameraShots)
                Assert.That(shot.Seconds, Is.InRange(5f, 8f));
            Vector3 leftFoot = registry.Anchors.LeftFoot.position, rightFoot = registry.Anchors.RightFoot.position;
            yield return Capture("karaoke-ready");
            input.Release(mouse.leftButton);
            yield return null;
            input.Set(mouse.delta, new Vector2(0f, Screen.height * .1625f));
            yield return null;
            Assert.That(karaoke.View.CurrentNormalizedPitch, Is.GreaterThan(.65f), "Silent pitch selection remains visible.");
            yield return Until(() => bar.Conversation.ActiveLineCount > 0,
                "Silent karaoke preparation permits an actual hall line");
            input.Press(mouse.leftButton);
            yield return Until(() => karaoke.Voice.Envelope > .5f, "Live PCM attack");
            Assert.That(karaoke.Voice.IsSinging, Is.True);
            Assert.That(karaoke.Voice.SmoothedFrequencyHz, Is.GreaterThan(260f));
            // PCM may advance on its audio thread after this frame's Update;
            // allow the shared face's normal LateUpdate before inspecting the rendered pose.
            yield return null;
            Assert.That(presentation.CurrentSpeechFace.Mouth, Is.EqualTo(SpeechMouthPose.Open));
            Assert.That(karaoke.IsPerforming, Is.True);
            Assert.That(karaoke.Audience.IsPerforming, Is.True);
            Assert.That(bar.Conversation.ActiveLineCount, Is.Zero, "Speech and its shared bubbles stop together.");
            int speechStarts = bar.Conversation.StartedLineCount;
            Assert.That(Vector3.Distance(leftFoot, registry.Anchors.LeftFoot.position), Is.LessThan(.01f));
            Assert.That(Vector3.Distance(rightFoot, registry.Anchors.RightFoot.position), Is.LessThan(.01f));
            yield return Capture("karaoke-singing");
            yield return Until(() => karaoke.Audience.AttendingPatronCount == bar.Patrons.Count &&
                karaoke.Audience.IsBartenderAttending, "All settled guests and the free bartender attend");
            yield return Until(() =>
            {
                foreach (BarPatron patron in bar.Patrons) if (!patron.Drinking.KaraokeHandsReleased) return false;
                return true;
            }, "The audience releases supported cups before turning");
            for (int i = 0; i < patronRoots.Length; i++)
            {
                Assert.That(Vector3.Distance(patronRoots[i], bar.Patrons[i].Presentation.transform.position), Is.LessThan(.001f));
                Assert.That(Vector3.Distance(bar.Patrons[i].Drinking.BottleBasePosition,
                    bar.Patrons[i].Drinking.BottleRestPoint), Is.LessThan(.02f), "The released cup stays on the table.");
            }
            yield return Until(() => bar.Soundscape.KaraokeCrowdGain == 0f, "Crowd murmur fades out");
            Assert.That(bar.Soundscape.FirstCrowdSource.volume, Is.Zero);
            Assert.That(bar.Soundscape.SecondCrowdSource.volume, Is.Zero);
            Vector3 movingCameraStart = Camera.main.transform.position;
            yield return Until(() => Vector3.Distance(Camera.main.transform.position, movingCameraStart) > .06f,
                "The cinematic lens moves within the shot");
            yield return Capture("karaoke-tracking");
            yield return Until(() => karaoke.CurrentCameraShot == 1, "Close singing shot is clear", () =>
                " shot=" + karaoke.CurrentCameraShot + " progress=" + karaoke.CameraShotProgress +
                " performing=" + karaoke.IsPerforming + " singing=" + karaoke.Voice.IsSinging +
                " focused=" + GameCursor.ApplicationFocused + " safe=" + karaoke.CameraSequence.HasSafeShot);
            yield return Until(() => karaoke.CameraShotProgress > .45f, "Moving close profile");
            yield return Capture("karaoke-close");
            Assert.That(Camera.main.fieldOfView, Is.InRange(43f, 47f));
            yield return Until(() => karaoke.CurrentCameraShot == 2, "Reverse hall shot is clear");
            yield return Until(() => karaoke.CameraShotProgress > .25f, "Moving hall view");
            yield return Capture("karaoke-audience");
            Assert.That(karaoke.CameraCutCount, Is.GreaterThanOrEqualTo(2));
            Assert.That(bar.Conversation.StartedLineCount, Is.EqualTo(speechStarts), "No hall voice starts over the singer.");
            yield return ReviewAudienceShots(karaoke);
            yield return ReviewWashroomShots(karaoke);
            input.Release(mouse.leftButton);
            yield return null;
            Assert.That(karaoke.IsPerforming, Is.True, "A breath does not release the audience.");
            input.Press(mouse.leftButton);
            GameSessionState.UpdateDrinkingProgress(100, DrinkId.Vodka, 1);
            GameTimeScaleRuntime.SetIntoxicationLevel(100f);
            yield return Until(() => Mathf.Abs(karaoke.DriftSemitones) > .2f, "Intoxication actually detunes the source");
            Assert.That(karaoke.EffectiveFrequencyHz, Is.InRange(110f, 440f));
            yield return Capture("karaoke-drunk");
            Assert.That(bar.PauseMenu.Open(), Is.True, "Escape's ordinary pause remains available.");
            yield return null;
            Assert.That(karaoke.Voice.IsSinging, Is.False);
            Assert.That(karaoke.Voice.Envelope, Is.Zero);
            Assert.That(Cursor.lockState, Is.EqualTo(CursorLockMode.None));
            Assert.That(karaoke.IsPerforming, Is.False);
            Assert.That(karaoke.Audience.AttendingPatronCount, Is.Zero);
            float pausedDrift = karaoke.DriftSemitones;
            int pausedCutCount = karaoke.CameraCutCount;
            yield return null;
            Assert.That(karaoke.DriftSemitones, Is.EqualTo(pausedDrift));
            Assert.That(karaoke.CameraCutCount, Is.EqualTo(pausedCutCount));
            bar.PauseMenu.Cancel();
            yield return Until(() => !bar.PauseMenu.IsOpen, "Pause menu close");
            yield return null;
            Assert.That(karaoke.Voice.IsSinging, Is.False, "Unpause cannot inherit a held click.");
            Assert.That(karaoke.Voice.Source.isPlaying, Is.False, "Buffered PCM cannot resume without a fresh click.");
            input.Release(mouse.leftButton); yield return null;
            input.Press(mouse.leftButton);
            yield return Until(() => karaoke.Voice.Envelope > .5f, "Fresh click after pause");
            GameInput.HandleApplicationFocus(false); yield return null;
            Assert.That(karaoke.Voice.Envelope, Is.Zero);
            Assert.That(karaoke.IsPerforming, Is.False);
            GameInput.HandleApplicationFocus(true); yield return null;
            Assert.That(karaoke.Voice.IsSinging, Is.False);
            input.Release(mouse.leftButton); yield return null;
            input.Press(mouse.leftButton);
            yield return Until(() => karaoke.Voice.Envelope > .5f, "Fresh click after focus");
            speechStarts = bar.Conversation.StartedLineCount;
            input.Release(mouse.leftButton);
            yield return Until(() => karaoke.Voice.Envelope == 0f, "Release envelope");
            Assert.That(karaoke.Voice.IsSinging, Is.False);
            yield return Until(() => !karaoke.IsPerforming, "Long silence releases audience");
            Assert.That(karaoke.Audience.IsPerforming, Is.False);
            Assert.That(bar.Soundscape.IsKaraokeSilenced, Is.False);
            Assert.That(karaoke.CurrentCameraShot, Is.Zero);
            yield return Until(() =>
            {
                foreach (BarPatron patron in bar.Patrons) if (patron.Drinking.KaraokePoseWeight > .0001f) return false;
                return true;
            }, "Audience hands return before drinking resumes");
            yield return Until(() => bar.Conversation.StartedLineCount > speechStarts,
                "The hall can speak again while karaoke waits silently");
            karaoke.RequestExit();
            yield return Until(() => !karaoke.OwnsInteraction, "Authored exit and deferred unlock");
            Assert.That(bar.Player.Motor.InputEnabled, Is.True);
            Assert.That(bar.Player.Interactor.InputEnabled, Is.True);
            Assert.That(BarMinigameModalLock.IsAnyLocked, Is.False);
            Assert.That(follow.FixedPoseActive, Is.False);
            Assert.That(presentation.HasSpeechFace, Is.False);
            Assert.That(karaoke.View.IsOpen, Is.False);
            Assert.That(karaoke.Voice.Source.isPlaying, Is.False);
            Assert.That(karaoke.Audience.IsPerforming, Is.False);
            Assert.That(bar.Soundscape.IsKaraokeSilenced, Is.False);
            yield return Capture("karaoke-exit");
            Assert.That(karaoke.BeginInteraction(), Is.True, karaoke.LastFailureReason);
            yield return Until(() => karaoke.Phase == BarKaraokePhase.Singing, "Reusable session");
            karaoke.enabled = false;
            yield return null;
            Assert.That(BarMinigameModalLock.IsAnyLocked, Is.False);
            Assert.That(follow.FixedPoseActive, Is.False);
            Assert.That(karaoke.Voice.Source.isPlaying, Is.False);
            Assert.That(presentation.HasSpeechFace, Is.False);
            Assert.That(bar.Player.Motor.InputEnabled, Is.True);
            Assert.That(karaoke.Audience.IsPerforming, Is.False);
        }

        [Test]
        public void IntoxicationDrift_IsBoundedIndependentDeterministicAndFreezesWithoutCatchup()
        {
            var sober = new KaraokeControlState();
            var half = new KaraokeControlState();
            var full = new KaraokeControlState();
            var repeat = new KaraokeControlState();
            var partitioned = new KaraokeControlState();
            float minimum = 0, maximum = 0;
            for (int i = 0; i < 1200; i++)
            {
                sober.AdvanceDrift(.01f, 0f, true);
                half.AdvanceDrift(.01f, 50f, true);
                full.AdvanceDrift(.01f, 100f, true);
                repeat.AdvanceDrift(.01f, 100f, true);
                Assert.That(sober.DriftSemitones, Is.Zero);
                Assert.That(sober.EffectiveFrequencyHz, Is.EqualTo(sober.FrequencyHz));
                Assert.That(full.DriftSemitones, Is.EqualTo(repeat.DriftSemitones));
                Assert.That(half.DriftSemitones, Is.EqualTo(full.DriftSemitones * .5f).Within(.000001f));
                Assert.That(Math.Abs(full.DriftSemitones), Is.LessThanOrEqualTo(KaraokeControlState.MaximumDriftSemitones));
                Assert.That(full.EffectiveFrequencyHz,
                    Is.InRange(KaraokeControlState.MinimumFrequencyHz, KaraokeControlState.MaximumFrequencyHz));
                minimum = Math.Min(minimum, full.DriftSemitones);
                maximum = Math.Max(maximum, full.DriftSemitones);
            }
            Assert.That(minimum, Is.LessThan(-.5f));
            Assert.That(maximum, Is.GreaterThan(.5f));
            Assert.That(full.NormalizedPitch, Is.EqualTo(.5f), "Drift must not accumulate into mouse selection");
            Assert.That(full.FrequencyHz, Is.EqualTo(220f));
            Assert.That(12 * Math.Log(full.EffectiveFrequencyHz / full.FrequencyHz, 2),
                Is.EqualTo(full.DriftSemitones).Within(.00001));
            for (int i = 0; i < 600; i++) partitioned.AdvanceDrift(.02f, 100f, true);
            Assert.That(partitioned.DriftSemitones, Is.EqualTo(full.DriftSemitones).Within(.000001f));

            float frozenDrift = full.DriftSemitones, frozenFrequency = full.EffectiveFrequencyHz;
            full.Suspend();
            full.AdvanceDrift(600f, 0f, false);
            Assert.That(full.DriftSemitones, Is.EqualTo(frozenDrift));
            Assert.That(full.EffectiveFrequencyHz, Is.EqualTo(frozenFrequency));
            full.AdvanceDrift(.01f, 100f, true);
            repeat.AdvanceDrift(.01f, 100f, true);
            Assert.That(full.DriftSemitones, Is.EqualTo(repeat.DriftSemitones), "Resuming must not replay paused time");
            frozenDrift = full.DriftSemitones;
            full.Advance(.1f, false, true);
            Assert.That(full.NormalizedPitch, Is.EqualTo(.6f).Within(.000001f));
            Assert.That(full.DriftSemitones, Is.EqualTo(frozenDrift), "Mouse input remains independent and immediate");
            full.Advance(10f, false, true);
            Assert.That(full.FrequencyHz, Is.EqualTo(KaraokeControlState.MaximumFrequencyHz));
            Assert.That(full.EffectiveFrequencyHz,
                Is.InRange(KaraokeControlState.MinimumFrequencyHz, KaraokeControlState.MaximumFrequencyHz));
            full.Advance(-10f, false, true);
            Assert.That(full.FrequencyHz, Is.EqualTo(KaraokeControlState.MinimumFrequencyHz));
            Assert.That(full.EffectiveFrequencyHz,
                Is.InRange(KaraokeControlState.MinimumFrequencyHz, KaraokeControlState.MaximumFrequencyHz));
            full.AdvanceDrift(0f, 0f, true);
            Assert.That(full.DriftSemitones, Is.Zero);
            Assert.That(full.EffectiveFrequencyHz, Is.EqualTo(full.FrequencyHz));
        }

        private static IEnumerator Until(Func<bool> ready, string operation, Func<string> diagnostics = null)
        {
            float deadline = Time.realtimeSinceStartup + 15f;
            while (!ready())
            {
                Assert.That(Time.realtimeSinceStartup, Is.LessThan(deadline), operation + (diagnostics?.Invoke() ?? string.Empty));
                yield return null;
            }
        }

        private IEnumerator ReviewAudienceShots(BarKaraokeInteraction karaoke)
        {
            BarKaraokeCameraSequence sequence = karaoke.CameraSequence;
            Assert.That(sequence.ShotCount, Is.GreaterThanOrEqualTo(6 + bar.Patrons.Count + 1));
            var seen = new HashSet<Transform>();
            for (int attempt = 0; attempt < 100 && seen.Count < bar.Patrons.Count + 1; attempt++)
            {
                sequence.Advance(8f);
                yield return null;
                Assert.That(karaoke.View.IsOpen, Is.True, "Pitch HUD persists through every shot.");
                Assert.That(sequence.CurrentShot.Seconds, Is.InRange(5f, 8f));
                if (sequence.CurrentKind != BarKaraokeShotKind.Patron && sequence.CurrentKind != BarKaraokeShotKind.Bartender)
                    continue;
                Transform subject = sequence.CurrentSubject;
                Assert.That(subject, Is.Not.Null);
                if (!seen.Add(subject)) continue;
                sequence.Advance(sequence.CurrentShot.Seconds * .35f);
                yield return null;
                yield return Capture("karaoke-visitor-" + seen.Count);
            }
            Assert.That(seen.Count, Is.EqualTo(bar.Patrons.Count + 1), "Every visitor and the bartender has an actual clear close shot.");
            Assert.That(sequence.WashroomCutCount, Is.Zero, "An empty WC never receives a speculative insert.");
        }

        private IEnumerator ReviewWashroomShots(BarKaraokeInteraction karaoke)
        {
            BarCrowdController crowd = bar.Crowd;
            crowd.AutoScheduleWashroom = false;
            for (int visit = 0; visit < 2; visit++)
            {
                BarPatron guest = null;
                string anchor = visit == 0 ? "counter-seat-right" : "booth-2-far";
                foreach (BarPatron patron in bar.Patrons)
                    if (patron.Anchor.Id == anchor) { guest = patron; break; }
                Assert.That(guest, Is.Not.Null);
                Transform originalBody = guest.Registry.transform, originalModel = guest.Registry.ModelRoot;
                Transform originalDrink = guest.Drinking.BottleRoot;
                Vector3 drinkPosition = originalDrink.position;
                var originalVisibility = new Dictionary<Renderer, bool>();
                var originalCoatBones = new Dictionary<SkinnedMeshRenderer, Transform[]>();
                foreach (Renderer renderer in originalModel.GetComponentsInChildren<Renderer>(true))
                {
                    originalVisibility.Add(renderer, renderer.enabled);
                    if (renderer is SkinnedMeshRenderer skin && renderer.name.StartsWith("CLO_Outer_", StringComparison.Ordinal))
                        originalCoatBones.Add(skin, skin.bones);
                }
                crowd.Advance(visit == 0 ? 241f : 721f);
                Assert.That(crowd.TryBeginWashroomVisit(guest.Anchor.Id), Is.True, crowd.LastWashroomAbortReason);
                yield return DriveCrowdUntil(() => crowd.IsWashroomUsingToilet, "Same guest reaches the actual toilet");
                yield return null;
                Assert.That(crowd.WashroomPatron, Is.SameAs(guest));
                Assert.That(guest.Registry.ModelRoot, Is.SameAs(originalModel));
                Assert.That(guest.Registry.transform, Is.SameAs(originalBody));
                Assert.That(crowd.WashroomFootContactError, Is.LessThan(.04f), "Real NPC feet retain the authored floor contacts.");
                HomeToiletSeatedAppearance npcAppearance = null;
                if (visit == 1)
                {
                    Assert.That(Vector3.Distance(guest.Registry.PelvisAnchor.position,
                        bar.Washroom.Host.Toilet.SeatPelvis), Is.LessThan(.02f), "The same NPC pelvis meets the actual toilet seat.");
                    npcAppearance = guest.Registry.GetComponentInChildren<HomeToiletSeatedAppearance>(true);
                    Assert.That(npcAppearance, Is.Not.Null.And.Property("IsActive").True);
                    Assert.That(npcAppearance.TrousersDown, Is.GreaterThan(.99f));
                    Assert.That(crowd.WashroomSeatedTimeline, Is.TypeOf<HomeToiletSeatedTimeline>());
                    Assert.That(crowd.WashroomSeatedTimeline.Duration,
                        Is.EqualTo(HomeToiletActorPresentation.Duration(HomeToiletActorPhase.Seated)));
                    Assert.That(crowd.WashroomBowelEffect.EmissionCount, Is.Zero, "The actual seated clock, not a camera cut, admits the one emission.");
                    if (guest.Registry.GetComponentInChildren<NpcWardrobe>(true).GetEquippedItem("outerwear") == "outerwear.coat")
                        Assert.That(crowd.WashroomOuterwearLiftWeight, Is.GreaterThan(.99f), "The long coat is physically gathered above the real rear and outlet.");
                    Assert.That(crowd.WashroomBowelOutlet, Is.SameAs(npcAppearance.Outlet), "Falling waste uses the same authored outlet as the hero.");
                    SkinnedMeshRenderer barePelvis = null, heroPelvis = null;
                    foreach (Renderer renderer in npcAppearance.Renderers)
                        if (renderer.name == "Bare_Pelvis") barePelvis = renderer as SkinnedMeshRenderer;
                    foreach (SkinnedMeshRenderer renderer in Resources.Load<GameObject>(HomeToiletSeatedAppearance.ModelResourcePath)
                        .GetComponentsInChildren<SkinnedMeshRenderer>(true))
                        if (renderer.name == "Bare_Pelvis") heroPelvis = renderer;
                    Assert.That(barePelvis, Is.Not.Null.And.Property("enabled").True);
                    Assert.That(heroPelvis, Is.Not.Null);
                    Assert.That(barePelvis.sharedMesh, Is.SameAs(heroPelvis.sharedMesh), "NPC and hero use the exact same seated rear mesh.");
                    Assert.That(npcAppearance.Outlet.IsChildOf(originalModel), Is.True, "The real outlet remains on this NPC's continuous rig.");
                    Assert.That(Vector3.Distance(npcAppearance.Outlet.position, guest.Registry.PelvisAnchor.position), Is.LessThan(.25f));
                }
                Assert.That(Vector3.Distance(originalDrink.position, drinkPosition), Is.LessThan(.02f));
                var faceKind = visit == 0 ? BarKaraokeWashroomShotKind.PeeFace : BarKaraokeWashroomShotKind.PoopFace;
                var bowlKind = visit == 0 ? BarKaraokeWashroomShotKind.PeeBowl : BarKaraokeWashroomShotKind.PoopBowl;
                bool sawFace = false, sawBowl = false;
                for (int attempt = 0; attempt < 100 && (!sawFace || !sawBowl); attempt++)
                {
                    int oldCount = karaoke.CameraSequence.WashroomCutCount;
                    float remaining = karaoke.CameraSequence.SecondsUntilWashroomShot;
                    karaoke.CameraSequence.Advance(8f);
                    yield return null;
                    Assert.That(karaoke.View.IsOpen, Is.True);
                    if (remaining > 8.1f)
                        Assert.That(karaoke.CameraSequence.WashroomCutCount, Is.EqualTo(oldCount), "WC inserts respect their independent long cooldown.");
                    BarKaraokeWashroomShotKind kind = karaoke.CameraSequence.CurrentWashroomKind;
                    if (kind != faceKind && kind != bowlKind) continue;
                    Assert.That(karaoke.CameraSequence.CurrentSubject, Is.SameAs(originalBody));
                    if (kind == faceKind && !sawFace)
                    {
                        sawFace = true;
                        yield return Capture(visit == 0 ? "karaoke-wc-pee-face" : "karaoke-wc-poop-face");
                    }
                    if (kind != bowlKind || sawBowl) continue;
                    sawBowl = true;
                    HomeToiletUnderwaterEffect underwater = crowd.WashroomUnderwater;
                    Assert.That(underwater, Is.Not.Null.And.Property("IsActive").True);
                    Vector3 position = Camera.main.transform.position;
                    Quaternion rotation = Camera.main.transform.rotation;
                    Vector3 firstAim = default, previousAim = default;
                    float aimTravel = 0f;
                    bool sampledAim = false;
                    int previousPackets = crowd.WashroomUrine.EmittedPacketCount;
                    Transform leftHand = CityPedestrianHandProps.FindSocket(originalModel, "hand.L");
                    Transform rightHand = CityPedestrianHandProps.FindSocket(originalModel, "hand.R");
                    Vector3 firstLeftHand = leftHand.position, firstRightHand = rightHand.position;
                    float seatedHandTravel = 0f;
                    yield return Capture(visit == 0 ? "karaoke-wc-pee-bowl-entry" : "karaoke-wc-poop-bowl-entry");
                    for (int frame = 0; frame < 70; frame++)
                    {
                        crowd.Advance(.05f);
                        yield return null;
                        Assert.That(Vector3.Distance(Camera.main.transform.position, position), Is.LessThan(.001f), "The inside-bowl lens is static.");
                        Assert.That(Quaternion.Angle(Camera.main.transform.rotation, rotation), Is.LessThan(.01f));
                        Assert.That(karaoke.Voice.IsSinging && karaoke.Voice.Source.isPlaying, Is.True,
                            "The hero keeps singing throughout either underwater insert.");
                        if (visit == 0 && crowd.WashroomUrine.EmittedPacketCount > previousPackets)
                        {
                            Vector3 aim = crowd.WashroomPeeAimPoint;
                            Vector3 viewport = Camera.main.WorldToViewportPoint(aim);
                            Assert.That(viewport.z, Is.GreaterThan(0f));
                            Assert.That(viewport.x, Is.InRange(.1f, .9f));
                            Assert.That(viewport.y, Is.InRange(.1f, .9f), "Random aim stays inside the visible bowl opening.");
                            Assert.That(Vector3.ProjectOnPlane(aim - bar.Washroom.Host.Toilet.Water.position, Vector3.up).magnitude,
                                Is.LessThan(.05f), "Random aim stays within the real water.");
                            if (!sampledAim) { firstAim = aim; sampledAim = true; }
                            else Assert.That(Vector3.Distance(aim, previousAim), Is.LessThan(.015f), "Aim changes smoothly between packets.");
                            aimTravel = Mathf.Max(aimTravel, Vector3.Distance(aim, firstAim));
                            previousAim = aim;
                            previousPackets = crowd.WashroomUrine.EmittedPacketCount;
                        }
                        if (visit == 1 && crowd.WashroomSeatedPhase == HomeToiletSeatedPhase.Seated)
                        {
                            float seatedTime = crowd.WashroomSeatedTimeline.PhaseElapsed;
                            Assert.That(crowd.WashroomBowelEffect.EmissionCount,
                                Is.EqualTo(seatedTime < HomeToiletSeatedTimeline.EmissionStartsAt ? 0 : 1));
                            Assert.That(crowd.WashroomBowelEffect.HasReleased,
                                Is.EqualTo(seatedTime >= HomeToiletSeatedTimeline.ReleaseAt));
                            Assert.That(leftHand.position.y, Is.LessThan(guest.Registry.PelvisAnchor.position.y + .1f));
                            Assert.That(rightHand.position.y, Is.LessThan(guest.Registry.PelvisAnchor.position.y + .1f),
                                "Seated hands rest low near the lap and knees.");
                            Assert.That(crowd.WashroomSeatedHandContactError, Is.LessThan(.04f), "Relaxed palms retain their thigh supports.");
                            seatedHandTravel = Mathf.Max(seatedHandTravel, Vector3.Distance(leftHand.position, firstLeftHand),
                                Vector3.Distance(rightHand.position, firstRightHand));
                        }
                        if (frame == 26) yield return Capture(visit == 0 ? "karaoke-wc-pee-stream" : "karaoke-wc-poop-fall");
                    }
                    if (visit == 0) Assert.That(aimTravel, Is.GreaterThan(.01f), "The seeded aim visibly wanders instead of repeating one fixed stream.");
                    else Assert.That(seatedHandTravel, Is.GreaterThan(.002f), "Seated wrists retain subtle live movement.");
                    yield return AssertUnderwaterKaraokeOutput(karaoke, underwater);
                    Assert.That(karaoke.Voice.Envelope, Is.GreaterThan(.5f));
                    Assert.That(karaoke.Voice.Source.volume, Is.GreaterThan(0f));
                    Assert.That(Vector3.Distance(karaoke.Voice.Source.transform.position, Camera.main.transform.position),
                        Is.LessThan(karaoke.Voice.Source.maxDistance));
                    Assert.That(karaoke.Voice.Source.outputAudioMixerGroup, Is.SameAs(GameAudioMixer.SfxWorldGroup));
                    Assert.That(GameAudioMixer.Mixer.FindMatchingGroups(GameAudioMixer.PerceptionGroupPath),
                        Does.Contain(karaoke.Voice.Source.outputAudioMixerGroup), "The continuing voice passes through the shared underwater filter.");
                    Assert.That(underwater.AudioAmount, Is.GreaterThan(.97f));
                    Assert.That(GameAudioMixer.Mixer.GetFloat(HomeToiletUnderwaterEffect.CutoffParameter, out float cutoff), Is.True);
                    Assert.That(GameAudioMixer.Mixer.GetFloat(HomeToiletUnderwaterEffect.GainParameter, out float gain), Is.True);
                    Assert.That(cutoff, Is.LessThan(480f));
                    Assert.That(gain, Is.EqualTo(-5f).Within(.2f));
                    if (visit == 1) Assert.That(crowd.WashroomBowelEffect.HasReleased, Is.True);
                    else Assert.That(crowd.WashroomUrine.BowlHitCount, Is.GreaterThan(0),
                        "Urine packets=" + crowd.WashroomUrine.EmittedPacketCount + "; last surface=" + crowd.WashroomUrine.LastHitSurfaceId);
                    if (visit == 0)
                    {
                        Vector3 hit = Camera.main.WorldToViewportPoint(crowd.WashroomUrine.LastHitPoint);
                        Assert.That(hit.z, Is.GreaterThan(0f));
                        Assert.That(hit.x, Is.InRange(.05f, .95f));
                        Assert.That(hit.y, Is.InRange(.05f, .95f), "The actual liquid lands inside the bowl frame.");
                    }
                    yield return Capture(visit == 0 ? "karaoke-wc-pee-bowl" : "karaoke-wc-poop-bowl");
                    if (visit == 0)
                    {
                        Assert.That(bar.PauseMenu.Open(), Is.True);
                        yield return null;
                        Assert.That(underwater.IsActive, Is.False, "Pause releases underwater audio ownership.");
                        bar.PauseMenu.Cancel();
                    }
                    else
                    {
                        GameInput.HandleApplicationFocus(false);
                        yield return null;
                        Assert.That(underwater.IsActive, Is.False, "Focus loss releases underwater audio ownership.");
                        GameInput.HandleApplicationFocus(true);
                    }
                    yield return null;
                    input.Release(mouse.leftButton); yield return null;
                    input.Press(mouse.leftButton);
                    yield return Until(() => karaoke.Voice.Envelope > .5f, "Fresh singing after bowl interruption");
                    Assert.That(karaoke.View.IsOpen, Is.True);
                    karaoke.CameraSequence.ReturnToStand();
                    karaoke.CameraSequence.BeginPerformance();
                    yield return null;
                    Assert.That(underwater.IsActive, Is.False);
                    Assert.That(GameAudioMixer.Mixer.GetFloat(HomeToiletUnderwaterEffect.CutoffParameter, out cutoff), Is.True);
                    Assert.That(cutoff, Is.EqualTo(22000f).Within(.1f));
                }
                Assert.That(sawFace && sawBowl, Is.True, "Both rare face and bowl views must be selectable for the actual action.");
                karaoke.CameraSequence.ReturnToStand();
                karaoke.CameraSequence.BeginPerformance();
                var finishPhases = new HashSet<HomeToiletSeatedPhase>();
                int flushes = crowd.WashroomBowelEffect.FlushPlayCount;
                yield return DriveCrowdUntil(() => !crowd.HasWashroomVisitor, "Guest finishes and returns through the actual door", () =>
                {
                    if (visit != 1 || !crowd.HasWashroomVisitor) return;
                    finishPhases.Add(crowd.WashroomSeatedPhase);
                    if (crowd.WashroomBowelEffect.FlushPlayCount > flushes)
                    {
                        Assert.That(crowd.IsWashroomFlushContact, Is.True, "The shared flush cue waits for the NPC hand's physical contact.");
                        flushes = crowd.WashroomBowelEffect.FlushPlayCount;
                    }
                });
                Assert.That(crowd.CompletedWashroomVisits, Is.EqualTo(visit + 1));
                Assert.That(guest.IsTemporarilyAway, Is.False);
                if (npcAppearance != null) Assert.That(npcAppearance.IsActive, Is.False, "The seated appearance releases before the guest returns to the hall.");
                Assert.That(Vector3.Distance(originalDrink.position, drinkPosition), Is.LessThan(.02f));
                foreach (var source in originalVisibility)
                    Assert.That(source.Key.enabled, Is.EqualTo(source.Value), "The same NPC's original wardrobe returns before the hall.");
                foreach (var source in originalCoatBones)
                    Assert.That(source.Key.bones, Is.EqualTo(source.Value), "The folded coat restores its original skeletal bindings.");
                if (visit == 1)
                {
                    Assert.That(crowd.WashroomBowelEffect.EmissionCount, Is.EqualTo(1), "Camera interruptions never replay defecation.");
                    Assert.That(crowd.WashroomBowelEffect.FlushPlayCount, Is.EqualTo(1));
                    foreach (HomeToiletSeatedPhase phase in new[] { HomeToiletSeatedPhase.Rising, HomeToiletSeatedPhase.Inspecting,
                        HomeToiletSeatedPhase.Flushing, HomeToiletSeatedPhase.VortexHold, HomeToiletSeatedPhase.Exiting, HomeToiletSeatedPhase.ClosingLid })
                        Assert.That(finishPhases, Does.Contain(phase), "NPC follows the hero's full toilet choreography.");
                }
            }
        }

        private static IEnumerator AssertUnderwaterKaraokeOutput(BarKaraokeInteraction karaoke,
            HomeToiletUnderwaterEffect underwater)
        {
            // Realtime tests remain muted. Only offline rendering may unmute the
            // listener, so this proof includes spatial gain and the real mixer.
            Assert.That(AudioListener.volume, Is.Zero, "The user's speakers stay muted during the focused test.");
            AudioSource vocal = karaoke.Voice.Source;
            var muted = new Dictionary<AudioSource, bool>();
            float previousVolume = AudioListener.volume, previousCaptureDelta = Time.captureDeltaTime;
            bool previousPause = AudioListener.pause, capturing = false;
            int stereoRate = Math.Max(8000, AudioSettings.outputSampleRate) * 2;
            int warmupTarget = Mathf.CeilToInt(stereoRate * .2f);
            int captureTarget = Mathf.CeilToInt(stereoRate * .5f);
            try
            {
                Time.captureDeltaTime = 1f / 60f;
                AudioListener.pause = false;
                // The first native offline session can return no PCM. Prime it
                // once, then clear room returns with every source muted before
                // measuring half a second of the actual live vowel alone.
                for (int session = 0; session < 2; session++)
                {
                    Assert.That(AudioRenderer.Start(), Is.True, "Offline rendering must own output before unmuting.");
                    capturing = true;
                    AudioListener.volume = 1f;
                    int rendered = 0, measured = 0;
                    double energy = 0d;
                    int frameBudget = session == 0 ? 30 : 90;
                    for (int frame = 0; frame < frameBudget &&
                        (session == 0 ? rendered < warmupTarget : measured < captureTarget); frame++)
                    {
                        bool measuring = session == 1 && rendered >= warmupTarget;
                        foreach (AudioSource source in Object.FindObjectsByType<AudioSource>(FindObjectsInactive.Include))
                        {
                            if (!muted.ContainsKey(source)) muted.Add(source, source.mute);
                            source.mute = !measuring || source != vocal;
                        }
                        yield return null;
                        Assert.That(underwater.IsActive && karaoke.CameraSequence.CurrentKind == BarKaraokeShotKind.WashroomBowl,
                            Is.True, "The measured output must still belong to the submerged bowl shot.");
                        Assert.That(karaoke.Voice.IsSinging && vocal.isPlaying, Is.True);
                        int count = Math.Max(0, AudioRenderer.GetSampleCountForCaptureFrame());
                        using (var buffer = new Unity.Collections.NativeArray<float>(count * 2, Unity.Collections.Allocator.Temp))
                        {
                            bool success = AudioRenderer.Render(buffer);
                            if (session == 1 && buffer.Length > 0) Assert.That(success, Is.True);
                            rendered += buffer.Length;
                            if (!measuring) continue;
                            bool finite = true;
                            for (int sample = 0; sample < buffer.Length; sample++)
                            {
                                finite &= float.IsFinite(buffer[sample]);
                                energy += (double)buffer[sample] * buffer[sample];
                            }
                            Assert.That(finite, Is.True, "Native output must remain finite.");
                            measured += buffer.Length;
                        }
                    }
                    if (session == 1)
                    {
                        Assert.That(karaoke.Voice.Envelope, Is.GreaterThan(.5f));
                        Assert.That(vocal.isVirtual, Is.False, "The live vowel must retain an actual audio voice underwater.");
                    }
                    AudioListener.volume = 0f;
                    AudioRenderer.Stop();
                    capturing = false;
                    if (session == 0) { yield return null; continue; }
                    Assert.That(measured, Is.GreaterThanOrEqualTo(captureTarget), "Offline output must supply the bounded half-second capture.");
                    double rms = Math.Sqrt(energy / Math.Max(1, measured));
                    TestContext.Out.WriteLine("Underwater karaoke listener RMS=" + rms.ToString("F6"));
                    Assert.That(rms, Is.GreaterThan(.00001d), "The isolated live vowel must reach native listener output through the underwater mixer.");
                }
            }
            finally
            {
                AudioListener.volume = 0f;
                if (capturing) AudioRenderer.Stop();
                foreach (var state in muted) if (state.Key != null) state.Key.mute = state.Value;
                Time.captureDeltaTime = previousCaptureDelta;
                AudioListener.pause = previousPause;
                AudioListener.volume = previousVolume;
            }
        }

        private IEnumerator DriveCrowdUntil(Func<bool> ready, string operation, Action observe = null)
        {
            for (int step = 0; step < 1400 && !ready(); step++)
            {
                bar.Crowd.Advance(.1f);
                yield return null;
                observe?.Invoke();
            }
            Assert.That(ready(), Is.True, operation + ": " + bar.Crowd.WashroomPhaseName + "/" + bar.Crowd.LastWashroomAbortReason);
        }

        private static IEnumerator Capture(string name)
        {
#if UNITY_EDITOR
            Type type = typeof(UnityEditor.EditorWindow).Assembly.GetType("UnityEditor.GameView");
            var view = UnityEditor.EditorWindow.GetWindow(type, false, null, false);
            // Repeatedly focusing the native Game view can deliver a delayed
            // application-focus loss, correctly cancelling the held vowel.
            view.Repaint();
#endif
            yield return null;
            AreaCaptureFixture.CaptureCurrentCamera(Camera.main, SceneIds.BarInterior, name + "-world");
            string path = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "Captures", SceneIds.BarInterior, name + ".png"));
            DateTime previous = File.Exists(path) ? File.GetLastWriteTimeUtc(path) : DateTime.MinValue;
            ScreenCapture.CaptureScreenshot(path);
            yield return Until(() => File.Exists(path) && File.GetLastWriteTimeUtc(path) > previous, "Game-view capture");
        }
    }
}

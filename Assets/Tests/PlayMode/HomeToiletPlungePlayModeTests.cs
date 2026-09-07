using System;
using System.Collections;
using System.IO;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

namespace BarPromenade.Tests.PlayMode
{
    public sealed class HomeToiletPlungePlayModeTests
    {
        private const float TimeoutSeconds = 30f;
        private HomeInteriorRoot home;
        private Camera camera;
        private HomeBathroomPresentationProbe probe;
        private string CaptureDirectory => Path.Combine(Directory.GetCurrentDirectory(), "Captures", "HomeToiletPlunge");

        [UnitySetUp]
        public IEnumerator SetUp()
        {
            Time.timeScale = 1f;
            GameSessionState.BeginNewGame();
            GameSessionState.EnterHome();
            yield return null;
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            AudioListener.pause = false;
            if (probe != null) { probe.Sample = null; probe.Observe = null; }
            Time.timeScale = 1f;
            Scene cleanup = SceneManager.CreateScene("ToiletPlungeCleanup" + Guid.NewGuid().ToString("N"));
            SceneManager.SetActiveScene(cleanup);
            Scene loaded = SceneManager.GetSceneByName(SceneIds.HomeInterior);
            if (loaded.IsValid() && loaded.isLoaded)
            {
                AsyncOperation unload = SceneManager.UnloadSceneAsync(loaded);
                while (unload != null && !unload.isDone) yield return null;
            }
            home = null;
            camera = null;
            GameSessionState.BeginNewGame();
            yield return null;
        }

        [UnityTest]
        public IEnumerator ChoicePlungeReturnsAndPreservesSmallAction()
        {
            AssertTimelineBoundaries();
            AsyncOperation load = SceneManager.LoadSceneAsync(SceneIds.HomeInterior, LoadSceneMode.Single);
            while (load != null && !load.isDone) yield return null;
            yield return WaitFor(() =>
            {
                home = Object.FindAnyObjectByType<HomeInteriorRoot>();
                return home != null && home.IsInitialized;
            }, "Home did not initialize.");
            camera = home.CameraFollow.GetComponent<Camera>();
            AssertAudioRouting();
            Renderer waterSurface = home.Room.Find("Home Bathroom Toilet Water").GetComponentInChildren<Renderer>();
            Assert.That(waterSurface.sharedMaterial.shader.name, Is.EqualTo("Bar Promenade/Home Toilet Bowl Water"),
                "Room composition/occlusion must preserve the water's optical material.");
            Assert.That(waterSurface.sharedMaterial.renderQueue, Is.GreaterThanOrEqualTo(2900));
            probe = home.gameObject.AddComponent<HomeBathroomPresentationProbe>();
            Directory.CreateDirectory(CaptureDirectory);
            GameSessionState.UpdateNeeds(0, 40);

            yield return FindChoice();
            AssertCameraContinuity();
            OwnedState original = new OwnedState(home, camera);
            Vector3 originalHeroPosition = home.Player.Motor.transform.position;
            home.ToiletChoice.Interact(home.Player.Interactor);
            Assert.That(home.ToiletChoice.IsOpen, Is.True);
            Assert.That(home.ToiletChoice.SelectedChoice, Is.EqualTo(HomeToiletChoice.Small));
            Assert.That(home.ToiletScene.Lid.IsOpen, Is.False, "Choosing alone must not start either action.");
            Assert.That(home.ToiletPlunge.IsActive, Is.False);
            Assert.That(home.Player.Motor.InputEnabled, Is.False);
            yield return CaptureMenu();
            Assert.That(home.ToiletChoice.Cancel(), Is.True);
            AssertRestored(original);
            yield return WaitForAudioRestored(original);
            Assert.That(Vector3.ProjectOnPlane(home.Player.Motor.transform.position - originalHeroPosition,
                Vector3.up).magnitude, Is.LessThan(.005f),
                "The menu must not start a guided approach; ordinary gravity may finish grounding the test spawn.");

            yield return FindChoice();
            original = new OwnedState(home, camera);
            Choose(HomeToiletChoice.Large);
            Assert.That(home.ToiletPlunge.IsActive, Is.True);
            Assert.That(home.ToiletScene.FirstPerson.IsActive, Is.False);
            yield return AtPresentationWhen(() =>
                home.ToiletPlunge.Timeline.Phase == HomeToiletPlungePhase.Entering &&
                camera.transform.position.y > home.ToiletPlunge.WaterHeight + .015f &&
                camera.transform.position.y < home.ToiletPlunge.WaterHeight + .14f,
                () =>
                {
                    Assert.That(home.ToiletPlunge.Underwater.Amount, Is.Zero.Within(.001f));
                    Assert.That(home.ToiletPlunge.Underwater.EntryPlayCount, Is.Zero,
                        "The entry sound must wait for the actual surface crossing.");
                    CaptureWorld("01-above-water");
                }, "The lens never approached the water from above.");
            yield return AtPresentationWhen(() =>
                home.ToiletPlunge.Timeline.Phase == HomeToiletPlungePhase.SubmergedHold,
                () =>
                {
                    Assert.That(camera.transform.position.y, Is.LessThan(home.ToiletPlunge.WaterHeight - .05f));
                    Assert.That(Vector3.Distance(camera.transform.position, home.ToiletPlunge.SubmergedPosition), Is.LessThan(.003f));
                    Assert.That(Vector3.Dot(camera.transform.forward, home.transform.up), Is.GreaterThan(.98f));
                    Assert.That(camera.nearClipPlane, Is.EqualTo(HomeToiletPlungeInteraction.SubmergedNearClip).Within(.0001f));
                    Assert.That(home.ToiletPlunge.Underwater.IsActive, Is.True);
                    Assert.That(home.ToiletPlunge.Underwater.Amount, Is.GreaterThan(.95f));
                    AssertSubmergedAudio();
                    Assert.That(home.Player.Motor.InputEnabled, Is.False);
                    Assert.That(home.ToiletScene.FirstPerson.IsActive, Is.False);
                    CaptureWorld("02-underwater-up");
                }, "The plunge never held underwater looking up.");
            int entryCount = home.ToiletPlunge.Underwater.EntryPlayCount;
            float audioAmount = home.ToiletPlunge.Underwater.AudioAmount;
            AudioListener.pause = true;
            yield return null;
            yield return null;
            Assert.That(home.ToiletPlunge.Underwater.AudioAmount, Is.EqualTo(audioAmount).Within(.00001f));
            AudioListener.pause = false;
            Assert.That(home.ToiletPlunge.Underwater.EntryPlayCount, Is.EqualTo(entryCount));
            yield return WaitFor(() => !home.ToiletPlunge.IsActive, "The full plunge never returned control.");
            yield return AtPresentationWhen(() => true, () =>
            {
                AssertRestored(original);
                CaptureWorld("03-returned");
            }, "No restored presentation frame.");
            yield return WaitForAudioRestored(original);
            Assert.That(GameSessionState.StressLevel, Is.EqualTo(40), "The camera-only branch must not grant bathroom relief.");

            yield return FindChoice();
            original = new OwnedState(home, camera);
            Choose(HomeToiletChoice.Large);
            yield return AtPresentationWhen(() =>
                home.ToiletPlunge.Timeline.Phase == HomeToiletPlungePhase.Entering &&
                home.ToiletPlunge.Timeline.PhaseElapsed >= .55f,
                () =>
                {
                    float travel = home.ToiletPlunge.Timeline.Travel;
                    Vector3 position = camera.transform.position;
                    home.ToiletPlunge.RequestStop();
                    Assert.That(home.ToiletPlunge.Timeline.Phase, Is.EqualTo(HomeToiletPlungePhase.Exiting));
                    Assert.That(home.ToiletPlunge.Timeline.Travel, Is.EqualTo(travel).Within(.00001f));
                    Assert.That(camera.transform.position, Is.EqualTo(position), "Cancellation cannot jump to the submerged endpoint.");
                    Assert.That(home.ToiletPlunge.Underwater.EntryPlayCount, Is.Zero);
                    Assert.That(home.Player.Motor.InputEnabled, Is.False, "Input stays owned during the smooth return.");
                }, "The second plunge never became cancellable.");
            yield return WaitFor(() => !home.ToiletPlunge.IsActive, "The early return never completed.");
            yield return AtPresentationWhen(() => true, () =>
            {
                AssertRestored(original);
                CaptureWorld("04-early-return");
            }, "No cancellation restoration frame.");
            yield return WaitForAudioRestored(original);

            yield return FindChoice();
            original = new OwnedState(home, camera);
            Choose(HomeToiletChoice.Large);
            yield return AtPresentationWhen(() =>
                home.ToiletPlunge.Timeline.Phase == HomeToiletPlungePhase.SubmergedHold,
                () =>
                {
                    home.ToiletPlunge.enabled = false;
                    AssertRestored(original);
                    home.ToiletPlunge.enabled = true;
                }, "The disable cleanup never reached submerged presentation.");
            yield return WaitForAudioRestored(original);
            Assert.That(GameSessionState.StressLevel, Is.EqualTo(40));

            yield return FindChoice();
            original = new OwnedState(home, camera);
            Choose(HomeToiletChoice.Small);
            Assert.That(home.ToiletPlunge.IsActive, Is.False);
            yield return AtPresentationWhen(() =>
                home.ToiletScene.Timeline.Phase == HomeToiletScenePhase.Urinating &&
                home.ToiletScene.Urine.BowlHitCount > 0,
                () =>
                {
                    Assert.That(home.ToiletScene.FirstPerson.IsActive, Is.True);
                    Assert.That(home.ToiletScene.GaugeVisible, Is.True);
                    Assert.That(home.ToiletPlunge.Underwater.IsActive, Is.False);
                    Assert.That(camera.nearClipPlane, Is.EqualTo(original.Near).Within(.0001f));
                    CaptureWorld("05-small-preserved");
                }, "The small choice no longer reaches the existing bowl water.");
            home.ToiletScene.enabled = false;
            yield return AtPresentationWhen(() => true, () => AssertRestored(original), "The small action did not restore.");
            yield return WaitForAudioRestored(original);
        }

        private static void AssertTimelineBoundaries()
        {
            var timeline = new HomeToiletPlungeTimeline();
            timeline.Begin();
            timeline.Advance(HomeToiletPlungeTimeline.EnterSeconds);
            Assert.That(timeline.Phase, Is.EqualTo(HomeToiletPlungePhase.SubmergedHold));
            timeline.Advance(HomeToiletPlungeTimeline.HoldSeconds - .01f);
            Assert.That(timeline.Phase, Is.EqualTo(HomeToiletPlungePhase.SubmergedHold));
            timeline.Advance(.011f);
            Assert.That(timeline.Phase, Is.EqualTo(HomeToiletPlungePhase.Exiting));
            timeline.Advance(HomeToiletPlungeTimeline.ExitSeconds);
            Assert.That(timeline.IsCompleted, Is.True);
            Assert.That(timeline.Travel, Is.Zero);
            timeline.Begin();
            timeline.Advance(HomeToiletPlungeTimeline.EnterSeconds * .37f);
            float travel = timeline.Travel;
            float velocity = timeline.TravelVelocity;
            Assert.That(timeline.RequestFinish(), Is.True);
            Assert.That(timeline.Travel, Is.EqualTo(travel).Within(.000001f));
            Assert.That(timeline.TravelVelocity, Is.EqualTo(velocity).Within(.000001f),
                "Early return must brake the moving camera before reversing it.");
            timeline.Advance(30f);
            Assert.That(timeline.IsCompleted && timeline.WasCancelled, Is.True);
            timeline.Begin();
            timeline.Advance(30f);
            Assert.That(timeline.IsCompleted && !timeline.WasCancelled, Is.True);
            for (int step = 0; step <= 20; step++)
            {
                float t = HomeToiletPlungeTimeline.EnterSeconds * step / 20f;
                timeline.Begin();
                timeline.Advance(t);
                travel = timeline.Travel;
                velocity = timeline.TravelVelocity;
                timeline.Begin();
                timeline.Advance(HomeToiletPlungeTimeline.EnterSeconds + HomeToiletPlungeTimeline.HoldSeconds +
                    HomeToiletPlungeTimeline.ExitSeconds - t);
                Assert.That(timeline.Travel, Is.EqualTo(travel).Within(.00001f));
                Assert.That(timeline.TravelVelocity, Is.EqualTo(-velocity).Within(.00001f));
            }
        }

        private static void AssertAudioRouting()
        {
            var mixer = GameAudioMixer.Mixer;
            Assert.That(mixer.GetFloat(HomeToiletUnderwaterEffect.CutoffParameter, out float cutoff), Is.True,
                "The authored mixer must expose the water controls before gameplay.");
            Assert.That(cutoff, Is.EqualTo(22000f).Within(.1f));
            var worldGroups = mixer.FindMatchingGroups(GameAudioMixer.PerceptionGroupPath);
            foreach (GameAudioGroup group in new[] { GameAudioGroup.Music, GameAudioGroup.AmbienceBeds,
                GameAudioGroup.AmbienceDetails, GameAudioGroup.SfxWorld, GameAudioGroup.SfxGameplay })
                Assert.That(worldGroups, Does.Contain(GameAudioMixer.GetGroup(group)));
            Assert.That(worldGroups, Has.No.Member(GameAudioMixer.UiGroup));
            foreach (AudioSource source in RetroAudioService.Instance.GetComponentsInChildren<AudioSource>(true))
                Assert.That(source.outputAudioMixerGroup == GameAudioMixer.UiGroup ||
                    Array.IndexOf(worldGroups, source.outputAudioMixerGroup) >= 0, Is.True,
                    "Persistent pooled effects must participate in the same world-bus absorption.");
        }

        private void AssertSubmergedAudio()
        {
            HomeToiletUnderwaterEffect effect = home.ToiletPlunge.Underwater;
            Assert.That(effect.IsAudioConfigured, Is.True);
            Assert.That(effect.AudioAmount, Is.GreaterThan(.97f));
            Assert.That(effect.EntryPlayCount, Is.EqualTo(1));
            effect.Present(camera.transform.position.y, effect.Clock);
            effect.Present(camera.transform.position.y, effect.Clock);
            Assert.That(effect.EntryPlayCount, Is.EqualTo(1), "Holding underwater cannot retrigger the entry sound.");
            Assert.That(effect.SubmergedVoice.volume, Is.GreaterThan(.1f));
            Assert.That(effect.SubmergedVoice.loop, Is.True);
            Assert.That(effect.EntryVoice.ignoreListenerPause, Is.False);
            Assert.That(GameAudioMixer.Mixer.GetFloat(HomeToiletUnderwaterEffect.CutoffParameter, out float cutoff), Is.True);
            Assert.That(GameAudioMixer.Mixer.GetFloat(HomeToiletUnderwaterEffect.GainParameter, out float gain), Is.True);
            Assert.That(cutoff, Is.LessThan(480f));
            Assert.That(gain, Is.EqualTo(HomeToiletUnderwaterEffect.SubmergedGainDb).Within(.16f));
            SaveAudioPreview(effect.EntryVoice.clip, "water-entry.wav");
            SaveAudioPreview(effect.SubmergedVoice.clip, "underwater-texture.wav");
        }

        private void AssertCameraContinuity()
        {
            Vector3 start = home.CameraFollow.FixedBasePosition;
            Quaternion startRotation = home.CameraFollow.FixedBaseRotation;
            const float epsilon = .001f;
            Vector3[] positions = new Vector3[3];
            Quaternion[] rotations = new Quaternion[3];
            for (int i = 0; i < 3; i++)
                HomeToiletPlungeInteraction.EvaluatePath(HomeToiletPlungeInteraction.AboveBowlTravel + (i - 1) * epsilon,
                    start, startRotation, home.ToiletPlunge.AboveBowlPosition, home.ToiletPlunge.SubmergedPosition,
                    home.transform.up, home.transform.right, out positions[i], out rotations[i]);
            Vector3 incoming = (positions[1] - positions[0]) / epsilon;
            Vector3 outgoing = (positions[2] - positions[1]) / epsilon;
            Assert.That(incoming.magnitude, Is.GreaterThan(.5f), "The camera must flow through the above-bowl waypoint.");
            Assert.That(Vector3.Distance(incoming, outgoing), Is.LessThan(.015f), "The waypoint must not kink the path.");
            Assert.That(Quaternion.Angle(rotations[0], rotations[1]), Is.LessThan(2f));
            Assert.That(Quaternion.Angle(rotations[1], rotations[2]), Is.LessThan(2f));
        }

        private void SaveAudioPreview(AudioClip clip, string fileName)
        {
            var samples = new float[clip.samples * clip.channels];
            Assert.That(clip.GetData(samples, 0), Is.True);
            float peak = 0f;
            double energy = 0;
            foreach (float sample in samples)
            {
                peak = Mathf.Max(peak, Mathf.Abs(sample));
                energy += sample * sample;
            }
            Assert.That(double.IsNaN(energy) || double.IsInfinity(energy), Is.False);
            Assert.That(peak, Is.InRange(.01f, .95f));
            Assert.That(Math.Sqrt(energy / samples.Length), Is.GreaterThan(.005));
            using var writer = new BinaryWriter(File.Create(Path.Combine(CaptureDirectory, fileName)));
            writer.Write(System.Text.Encoding.ASCII.GetBytes("RIFF")); writer.Write(36 + samples.Length * 2);
            writer.Write(System.Text.Encoding.ASCII.GetBytes("WAVEfmt ")); writer.Write(16);
            writer.Write((short)1); writer.Write((short)clip.channels); writer.Write(clip.frequency);
            writer.Write(clip.frequency * clip.channels * 2); writer.Write((short)(clip.channels * 2)); writer.Write((short)16);
            writer.Write(System.Text.Encoding.ASCII.GetBytes("data")); writer.Write(samples.Length * 2);
            foreach (float sample in samples) writer.Write((short)Mathf.RoundToInt(sample * short.MaxValue));
        }

        private IEnumerator FindChoice()
        {
            home.Player.Motor.Teleport(new Vector3(3.10f, .12f, 1.20f));
            yield return WaitFor(() => ReferenceEquals(home.Player.Interactor.ActiveInteractable, home.ToiletChoice),
                "The toilet's single choice trigger was not discovered.");
            Assert.That(home.FixedCamera.ActiveShotKind, Is.EqualTo(HomeCameraShotKind.Bathroom));
        }

        private void Choose(HomeToiletChoice choice)
        {
            home.ToiletChoice.Interact(home.Player.Interactor);
            Assert.That(home.ToiletChoice.IsOpen, Is.True);
            Assert.That(home.ToiletChoice.SelectChoice(choice), Is.True);
            Assert.That(home.ToiletChoice.Confirm(), Is.True);
            Assert.That(home.ToiletChoice.IsOpen, Is.False);
            Assert.That(BarMinigameModalLock.IsAnyLocked, Is.True);
        }

        private void AssertRestored(OwnedState state)
        {
            Assert.That(home.ToiletChoice.IsOpen, Is.False);
            Assert.That(home.ToiletPlunge.IsActive, Is.False);
            Assert.That(home.ToiletPlunge.Underwater.IsActive, Is.False);
            Assert.That(home.ToiletPlunge.Underwater.Amount, Is.Zero);
            Assert.That(home.ToiletPlunge.Underwater.AudioAmount, Is.Zero);
            Assert.That(home.ToiletPlunge.Underwater.Clock, Is.Zero);
            Assert.That(home.ToiletPlunge.Underwater.IsAudioConfigured, Is.False);
            if (home.ToiletPlunge.Underwater.EntryVoice != null)
            {
                Assert.That(home.ToiletPlunge.Underwater.EntryVoice.isPlaying, Is.False);
                Assert.That(home.ToiletPlunge.Underwater.SubmergedVoice.isPlaying, Is.False);
                Assert.That(home.ToiletPlunge.Underwater.SubmergedVoice.volume, Is.Zero);
            }
            Assert.That(home.ToiletScene.Lid.IsOpen, Is.False);
            Assert.That(home.Player.Motor.InputEnabled, Is.EqualTo(state.MotorInput));
            Assert.That(home.Player.Interactor.InputEnabled, Is.EqualTo(state.InteractorInput));
            Assert.That(home.CameraFollow.OrbitInputEnabled, Is.EqualTo(state.Orbit));
            Assert.That(home.CameraFollow.CinematicMotionEnabled, Is.EqualTo(state.Cinematic));
            Assert.That(home.IntoxicationHud.Visible, Is.EqualTo(state.Hud));
            Assert.That(BarMinigameModalLock.IsAnyLocked, Is.False);
            Assert.That(camera.nearClipPlane, Is.EqualTo(state.Near).Within(.0001f));
            Assert.That(home.CameraFollow.FixedBaseFieldOfView, Is.EqualTo(state.Fov).Within(.0001f));
            Assert.That(Vector3.Distance(home.CameraFollow.FixedBasePosition, state.CameraPosition), Is.LessThan(.001f));
            Assert.That(Quaternion.Angle(home.CameraFollow.FixedBaseRotation, state.CameraRotation), Is.LessThan(.01f));
            Assert.That(Cursor.lockState, Is.EqualTo(state.CursorLock));
            Assert.That(Cursor.visible, Is.EqualTo(state.CursorVisible));
        }

        private static IEnumerator WaitForAudioRestored(OwnedState state)
        {
            // ClearFloat returns ownership to the snapshot on the audio update;
            // its reported interpolated value can lag the rendering frame.
            bool Restored() =>
                GameAudioMixer.Mixer.GetFloat(HomeToiletUnderwaterEffect.CutoffParameter, out float cutoff) &&
                GameAudioMixer.Mixer.GetFloat(HomeToiletUnderwaterEffect.GainParameter, out float gain) &&
                Mathf.Abs(cutoff - state.AudioCutoff) < 1f && Mathf.Abs(gain - state.AudioGain) < .001f;
            float deadline = Time.realtimeSinceStartup + .5f;
            while (!Restored() && Time.realtimeSinceStartup < deadline) yield return null;
            Assert.That(Restored(), Is.True, "World audio must return to its scene snapshot after releasing the camera.");
        }

        private IEnumerator AtPresentationWhen(Func<bool> condition, Action sample, string failure)
        {
            bool complete = false;
            Exception error = null;
            probe.Observe = () =>
            {
                if (complete) return;
                try
                {
                    if (!condition()) return;
                    complete = true;
                    sample();
                }
                catch (Exception exception) { error = exception; complete = true; }
            };
            yield return WaitFor(() => complete, failure);
            probe.Observe = null;
            if (error != null) System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(error).Throw();
        }

        private static IEnumerator WaitFor(Func<bool> condition, string failure)
        {
            float deadline = Time.realtimeSinceStartup + TimeoutSeconds;
            while (!condition() && Time.realtimeSinceStartup < deadline) yield return null;
            Assert.That(condition(), Is.True, failure);
        }

        private IEnumerator CaptureMenu()
        {
            // Batch mode does not render the Game View's OnGUI. The behavior
            // assertions and camera captures still run there; visual acceptance
            // launches this same selection with a real Game View.
            if (Application.isBatchMode) yield break;
#if UNITY_EDITOR
            Type viewType = typeof(UnityEditor.EditorWindow).Assembly.GetType("UnityEditor.GameView");
            UnityEditor.EditorWindow view = UnityEditor.EditorWindow.GetWindow(viewType);
            view.Show();
            view.Repaint();
            yield return null;
            yield return null;
#endif
            string path = Path.Combine(CaptureDirectory, "00-choice.png");
            DateTime previous = File.Exists(path) ? File.GetLastWriteTimeUtc(path) : DateTime.MinValue;
            ScreenCapture.CaptureScreenshot(path);
            yield return null;
            yield return null;
            float deadline = Time.realtimeSinceStartup + 3f;
            while ((!File.Exists(path) || File.GetLastWriteTimeUtc(path) <= previous) &&
                   Time.realtimeSinceStartup < deadline) yield return null;
            Assert.That(File.Exists(path) && File.GetLastWriteTimeUtc(path) > previous, Is.True,
                "The choice menu did not produce a fresh Game view capture.");
        }

        private void CaptureWorld(string name)
        {
            var target = new RenderTexture(1280, 720, 24);
            var frame = new Texture2D(1280, 720, TextureFormat.RGB24, false);
            RenderTexture previousTarget = camera.targetTexture;
            RenderTexture previousActive = RenderTexture.active;
            try
            {
                camera.targetTexture = target;
                camera.Render();
                RenderTexture.active = target;
                frame.ReadPixels(new Rect(0f, 0f, 1280, 720), 0, 0);
                frame.Apply();
                File.WriteAllBytes(Path.Combine(CaptureDirectory, name + ".png"), frame.EncodeToPNG());
            }
            finally
            {
                camera.targetTexture = previousTarget;
                RenderTexture.active = previousActive;
                Object.DestroyImmediate(frame);
                target.Release();
                Object.DestroyImmediate(target);
            }
        }

        private readonly struct OwnedState
        {
            public readonly bool MotorInput, InteractorInput, Orbit, Cinematic, Hud, CursorVisible;
            public readonly float Near, Fov, AudioCutoff, AudioGain;
            public readonly Vector3 CameraPosition;
            public readonly Quaternion CameraRotation;
            public readonly CursorLockMode CursorLock;
            public OwnedState(HomeInteriorRoot home, Camera camera)
            {
                MotorInput = home.Player.Motor.InputEnabled;
                InteractorInput = home.Player.Interactor.InputEnabled;
                Orbit = home.CameraFollow.OrbitInputEnabled;
                Cinematic = home.CameraFollow.CinematicMotionEnabled;
                Hud = home.IntoxicationHud.Visible;
                Near = camera.nearClipPlane;
                Fov = home.CameraFollow.FixedBaseFieldOfView;
                GameAudioMixer.Mixer.GetFloat(HomeToiletUnderwaterEffect.CutoffParameter, out AudioCutoff);
                GameAudioMixer.Mixer.GetFloat(HomeToiletUnderwaterEffect.GainParameter, out AudioGain);
                CameraPosition = home.CameraFollow.FixedBasePosition;
                CameraRotation = home.CameraFollow.FixedBaseRotation;
                CursorLock = Cursor.lockState;
                CursorVisible = Cursor.visible;
            }
        }
    }
}

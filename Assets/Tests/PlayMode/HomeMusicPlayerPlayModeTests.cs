using System;
using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace BarPromenade.Tests.PlayMode
{
    public sealed class HomeMusicPlayerPlayModeTests
    {
        private GameObject cameraObject;
        private GameObject controllerObject;
        private GameObject targetObject;
        private GameObject musicObject;
        private AudioClip testClip;

        [UnityTest]
        public IEnumerator OptionalTheme_TracksCameraHysteresisAndPauseState()
        {
            HomeFixedCameraController controller =
                CreateCameraController();
            musicObject = new GameObject("Home Music Test");
            HomeMusicPlayer music =
                musicObject.AddComponent<HomeMusicPlayer>();
            music.Initialize(controller);
            yield return null;

            Assert.That(
                HomeMusicPlayer.ResourcePath,
                Is.EqualTo("Audio/HomeMusic/home_theme"));
            bool hasTheme = music.ActiveClip != null;
            music.AdvanceFade(
                SceneMusicPlayer.DefaultFadeDurationSeconds);
            Assert.That(
                music.PlaybackState,
                hasTheme
                    ? Is.EqualTo(SceneMusicPlaybackState.Playing)
                    : Is.EqualTo(
                        SceneMusicPlaybackState.Unavailable));
            Assert.That(
                music.NormalizedGain,
                hasTheme ? Is.EqualTo(1f) : Is.Zero);
            Assert.That(
                music.Source.volume,
                hasTheme
                    ? Is.EqualTo(
                        HomeMusicPlayer.ThemeOutputVolume).Within(0.001f)
                    : Is.Zero);
            Assert.That(music.Source.loop, Is.True);
            Assert.That(
                music.Source.outputAudioMixerGroup,
                Is.SameAs(GameAudioMixer.MusicGroup));
            Assert.That(music.IsBalconyActive, Is.False);

            targetObject.transform.position =
                new Vector3(5.5f, 0f, 0f);
            Assert.That(controller.ReapplyActiveShot(), Is.True);
            Assert.That(
                controller.ActiveShotKind,
                Is.EqualTo(HomeCameraShotKind.Balcony));
            Assert.That(music.RefreshBalconyState(), Is.True);
            Assert.That(music.IsBalconyActive, Is.True);
            music.AdvanceFade(
                HomeMusicPlayer.BalconyFadeDurationSeconds);
            Assert.That(
                music.PlaybackState,
                hasTheme
                    ? Is.EqualTo(SceneMusicPlaybackState.Paused)
                    : Is.EqualTo(
                        SceneMusicPlaybackState.Unavailable));
            Assert.That(music.NormalizedGain, Is.Zero);

            targetObject.transform.position =
                new Vector3(4.75f, 0f, 0f);
            Assert.That(controller.ReapplyActiveShot(), Is.True);
            Assert.That(
                controller.ActiveShotKind,
                Is.EqualTo(HomeCameraShotKind.Balcony),
                "The Home music boundary must inherit the camera hold margin.");
            Assert.That(music.RefreshBalconyState(), Is.False);
            Assert.That(music.IsBalconyActive, Is.True);

            targetObject.transform.position = Vector3.zero;
            Assert.That(controller.ReapplyActiveShot(), Is.True);
            Assert.That(
                controller.ActiveShotKind,
                Is.EqualTo(HomeCameraShotKind.MainRoom));
            Assert.That(music.RefreshBalconyState(), Is.True);
            Assert.That(music.IsBalconyActive, Is.False);
            music.AdvanceFade(
                HomeMusicPlayer.BalconyFadeDurationSeconds);
            Assert.That(
                music.PlaybackState,
                hasTheme
                    ? Is.EqualTo(SceneMusicPlaybackState.Playing)
                    : Is.EqualTo(
                        SceneMusicPlaybackState.Unavailable),
                "The optional Home theme must resume indoors or remain " +
                "a silent no-op when its slot is empty.");
        }

        [UnityTest]
        public IEnumerator Initialize_RejectsMissingOrUninitializedCamera()
        {
            musicObject = new GameObject("Home Music Validation Test");
            HomeMusicPlayer music =
                musicObject.AddComponent<HomeMusicPlayer>();

            Assert.That(
                () => music.Initialize(null),
                Throws.TypeOf<ArgumentNullException>());

            controllerObject =
                new GameObject("Uninitialized Home Camera Test");
            HomeFixedCameraController uninitialized =
                controllerObject.AddComponent<
                    HomeFixedCameraController>();
            Assert.That(
                () => music.Initialize(uninitialized),
                Throws.TypeOf<ArgumentException>());
            Assert.That(music.IsInitialized, Is.False);
            yield return null;
        }

        [UnityTest]
        public IEnumerator LoadedTheme_PausesOnBalconyAndResumesSameSample()
        {
            HomeFixedCameraController controller =
                CreateCameraController();
            musicObject = new GameObject("Home Music Loaded Test");
            HomeMusicPlayer music =
                musicObject.AddComponent<HomeMusicPlayer>();
            testClip = AudioClip.Create(
                "home_theme_runtime_test",
                22050 * 4,
                1,
                22050,
                false);
            music.Source.clip = testClip;
            music.FadeOutAndPause(0f);
            Assert.That(
                music.Source.isPlaying,
                Is.False,
                "The regression setup requires a clip that has never " +
                "started before receiving a pause request.");
            music.ResumeWithFadeIn(0f);
            music.Initialize(controller);

            Assert.That(music.Source.isPlaying, Is.True);
            Assert.That(
                music.PlaybackState,
                Is.EqualTo(SceneMusicPlaybackState.Playing));
            Assert.That(
                () => music.Initialize(controller),
                Throws.TypeOf<InvalidOperationException>());

            music.Source.timeSamples = 4096;
            targetObject.transform.position =
                new Vector3(5.5f, 0f, 0f);
            Assert.That(controller.ReapplyActiveShot(), Is.True);
            Assert.That(music.RefreshBalconyState(), Is.True);
            Assert.That(
                music.PlaybackState,
                Is.EqualTo(SceneMusicPlaybackState.FadingOut));
            music.AdvanceFade(
                HomeMusicPlayer.BalconyFadeDurationSeconds);

            int pausedSample = music.Source.timeSamples;
            Assert.That(music.IsPaused, Is.True);
            Assert.That(music.Source.isPlaying, Is.False);
            Assert.That(pausedSample, Is.EqualTo(4096).Within(64));

            targetObject.transform.position = Vector3.zero;
            Assert.That(controller.ReapplyActiveShot(), Is.True);
            Assert.That(music.RefreshBalconyState(), Is.True);

            Assert.That(music.Source.isPlaying, Is.True);
            Assert.That(
                music.Source.timeSamples,
                Is.EqualTo(pausedSample).Within(64),
                "Returning indoors must resume instead of restarting.");
            Assert.That(
                music.PlaybackState,
                Is.EqualTo(SceneMusicPlaybackState.FadingIn));
            music.AdvanceFade(
                HomeMusicPlayer.BalconyFadeDurationSeconds);
            Assert.That(
                music.PlaybackState,
                Is.EqualTo(SceneMusicPlaybackState.Playing));
            Assert.That(music.NormalizedGain, Is.EqualTo(1f));
            yield return null;
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            DestroyObject(musicObject);
            DestroyObject(controllerObject);
            DestroyObject(cameraObject);
            DestroyObject(targetObject);
            if (testClip != null)
            {
                UnityEngine.Object.Destroy(testClip);
                testClip = null;
            }

            yield return null;
        }

        private HomeFixedCameraController CreateCameraController()
        {
            targetObject = new GameObject("Home Music Camera Target");
            cameraObject = new GameObject("Home Music Camera");
            Camera camera = cameraObject.AddComponent<Camera>();
            PlayerCameraFollow follow =
                cameraObject.AddComponent<PlayerCameraFollow>();
            follow.Initialize(
                camera,
                targetObject.transform,
                true);

            controllerObject =
                new GameObject("Home Music Fixed Camera");
            HomeFixedCameraController controller =
                controllerObject.AddComponent<
                    HomeFixedCameraController>();
            controller.Initialize(
                follow,
                targetObject.transform,
                new[]
                {
                    new HomeCameraShot(
                        HomeCameraShotKind.MainRoom,
                        Rect.MinMaxRect(-1f, -1f, 1f, 1f),
                        Rect.MinMaxRect(-1.5f, -1.2f, 1.5f, 1.2f),
                        new Vector3(0f, 2f, -3f),
                        new Vector3(20f, 0f, 0f),
                        60f),
                    new HomeCameraShot(
                        HomeCameraShotKind.Balcony,
                        Rect.MinMaxRect(5f, -1f, 7f, 1f),
                        Rect.MinMaxRect(4.5f, -1.2f, 7.2f, 1.2f),
                        new Vector3(4f, 2f, -3f),
                        new Vector3(20f, 20f, 0f),
                        70f)
                });
            return controller;
        }

        private static void DestroyObject(GameObject target)
        {
            if (target != null)
            {
                UnityEngine.Object.Destroy(target);
            }
        }
    }
}

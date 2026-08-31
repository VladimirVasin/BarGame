using System;
using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace BarPromenade.Tests.PlayMode
{
    /// <summary>
    /// The village's haze breathing with the gale, watched through the REAL
    /// scene for ten seconds of pinned frames.
    ///
    /// The EditMode suite proves the pure pieces - the gust, the wave, the
    /// density formula, the wall's material - one at a time. What it cannot
    /// prove is that the one per-frame writer in <c>AlpineVillageRoot</c>
    /// actually runs them in order on every frame, that nothing else in the
    /// loaded scene writes the fog or the far plane over it, and that the
    /// wall's own fog term is told the same number the sky was. A wall hazed
    /// on last frame's density against this frame's sky is a silhouette that
    /// comes and goes on its own, and only a running scene can show that.
    ///
    /// Every assertion here is tied to the root's own <c>StormWave</c> rather
    /// than to a simulated one: the test reads what the scene did, not what
    /// the numbers say it should have done.
    ///
    /// Loads a full gameplay scene: run it ALONE, never batched with another
    /// scene-loading fixture (see the note on <c>AreaCaptureFixture</c>).
    /// </summary>
    public sealed class AlpineVillageStormVisibilityPlayModeTests
    {
        /// <summary>
        /// Batch mode runs frames as fast as it can, so the wave's attack
        /// and release - both in seconds - only mean anything against a
        /// pinned clock. The game clock advances on the same delta, so the
        /// gust rhythm and the wave move together under the pin.
        /// </summary>
        private const float PinnedFrameSeconds = 1f / 60f;

        /// <summary>
        /// Ten seconds at the pinned rate: longer than the primary gust
        /// period (`7.3 s`), so at least one crest and one trough pass.
        /// </summary>
        private const int ObservedFrames = 600;

        private const float TimeoutSeconds = 60f;
        private const float DensityTolerance = 0.00001f;
        private const float FarPlaneTolerance = 0.01f;

        /// <summary>
        /// Where the run's thinnest haze must reach. The base is `0.017`;
        /// the simulated running trough of the wave is about `0.05`, which
        /// is `0.0184` - so `0.0195` gives a frame budget of one trough
        /// without letting a wave that never reopens pass.
        /// </summary>
        private const float TroughDensityCeiling = 0.0195f;

        /// <summary>
        /// How far toward the storm peak the run's thickest haze must climb,
        /// as a fraction of the base-to-peak span. The wave's attack is
        /// `0.5 s` and a crest holds for seconds, so anything below this
        /// means the wave is not being driven.
        /// </summary>
        private const float CrestFraction = 0.4f;

        [SetUp]
        public void PinTheClock()
        {
            Time.captureDeltaTime = PinnedFrameSeconds;
            GameSessionState.BeginNewGame();

            // The game clock only advances once it has been started, and the
            // gust rides the game clock. Day two at 07:40 is the capture's own
            // slot, so a frame here and a frame in `Captures/AlpineVillage/`
            // describe the same weather.
            Assert.That(GameSessionState.TryStartGameTimeFromWake(), Is.True);
            Assert.That(GameSessionState.TrySetDebugGameDay(2), Is.True);
            GameSessionState.AdvanceGameTime(100f);
        }

        [TearDown]
        public void ReleaseTheClock()
        {
            Time.captureDeltaTime = 0f;
            GameSessionState.BeginNewGame();
        }

        /// <summary>
        /// Six hundred frames of the loaded village: the far plane, the fog
        /// mode, the density against the root's own wave, the wall's fog
        /// term against the scene's, and the background against the fog -
        /// on every frame; then the run as a whole must have both breathed
        /// in and breathed out.
        /// </summary>
        [UnityTest]
        public IEnumerator Haze_BreathesWithTheGaleOnEveryFrameAndReopens()
        {
            AlpineVillageRoot village = null;
            yield return LoadSceneAndWaitForRoot<AlpineVillageRoot>(
                SceneIds.AlpineVillage,
                root => village = root);
            yield return null;

            Assert.That(village.IsInitialized, Is.True);
            Assert.That(village.PeripheralBlizzard, Is.Not.Null);
            Assert.That(village.PeripheralBlizzard.IsInitialized, Is.True);
            Assert.That(village.PeripheralBlizzard.SpatialPlan, Is.Not.Null);
            Assert.That(
                village.PeripheralBlizzard.ParticleRenderer.sharedMaterial,
                Is.SameAs(CityNightResources.AtmosphereMaterial));
            Assert.That(
                village.WarmthGrade,
                Is.EqualTo(0f),
                "The trough and crest thresholds below are derived for the " +
                "warm baseline; a driven warmth grade needs its own numbers.");

            float minimumDensity = float.PositiveInfinity;
            float maximumDensity = float.NegativeInfinity;
            for (int frame = 0; frame < ObservedFrames; frame++)
            {
                Camera camera = Camera.main;
                Assert.That(
                    camera,
                    Is.Not.Null,
                    $"Frame {frame}: the village lost its main camera.");
                Assert.That(
                    camera.farClipPlane,
                    Is.EqualTo(RuntimeSceneSetup.AlpineVillageFarClipPlane)
                        .Within(FarPlaneTolerance),
                    $"Frame {frame}: the far plane is not the village's.");
                Assert.That(RenderSettings.fog, Is.True, $"Frame {frame}.");
                Assert.That(
                    RenderSettings.fogMode,
                    Is.EqualTo(FogMode.ExponentialSquared),
                    $"Frame {frame}: the fog is not Exp2.");

                float expectedDensity =
                    RuntimeSceneSetup.EvaluateAlpineVillageFogDensity(
                        village.StormWave,
                        village.WarmthGrade);
                Assert.That(
                    RenderSettings.fogDensity,
                    Is.EqualTo(expectedDensity).Within(DensityTolerance),
                    $"Frame {frame}: the fog density does not follow the " +
                    $"root's storm wave {village.StormWave:0.000}. " +
                    "Something else is writing the fog, or the writer runs " +
                    "out of order.");
                Assert.That(
                    AlpineVillageRidgeAppearance.RidgeMaterial
                        .GetFloat("_FogDensity"),
                    Is.EqualTo(RenderSettings.fogDensity)
                        .Within(DensityTolerance),
                    $"Frame {frame}: the wall's fog term lags the scene's; " +
                    "the bowl would come and go on its own.");
                Assert.That(
                    camera.backgroundColor,
                    Is.EqualTo(RenderSettings.fogColor),
                    $"Frame {frame}: pixels past the plane must resolve " +
                    "to the haze, not to a dark world edge.");
                Assert.That(
                    village.PeripheralBlizzard.AppliedStormWave,
                    Is.EqualTo(village.StormWave).Within(0.000001f),
                    $"Frame {frame}: the side whiteout is not reading the " +
                    "same gust wave as the landmark haze.");

                minimumDensity = Mathf.Min(
                    minimumDensity,
                    RenderSettings.fogDensity);
                maximumDensity = Mathf.Max(
                    maximumDensity,
                    RenderSettings.fogDensity);
                yield return null;
            }

            float crestFloor = Mathf.Lerp(
                RuntimeSceneSetup.AlpineVillageFogDensity,
                RuntimeSceneSetup.AlpineVillageStormFogDensity,
                CrestFraction);
            Assert.That(
                minimumDensity,
                Is.LessThanOrEqualTo(TroughDensityCeiling),
                "The haze never thinned back toward the base in ten " +
                "seconds: the top house is not coming back between gusts.");
            Assert.That(
                maximumDensity,
                Is.GreaterThanOrEqualTo(crestFloor),
                "The haze never closed toward the storm peak in ten " +
                "seconds: the wave is not being driven by the gust.");
        }

        private static IEnumerator LoadSceneAndWaitForRoot<T>(
            string sceneName,
            Action<T> capture)
            where T : Component
        {
            AsyncOperation operation = SceneManager.LoadSceneAsync(
                sceneName,
                LoadSceneMode.Single);
            Assert.That(operation, Is.Not.Null);
            float deadline = Time.realtimeSinceStartup + TimeoutSeconds;
            while (!operation.isDone &&
                   Time.realtimeSinceStartup < deadline)
            {
                yield return null;
            }

            Assert.That(operation.isDone, Is.True);
            deadline = Time.realtimeSinceStartup + TimeoutSeconds;
            while (Time.realtimeSinceStartup < deadline)
            {
                T root = UnityEngine.Object.FindAnyObjectByType<T>();
                if (root != null)
                {
                    capture(root);
                    yield break;
                }

                yield return null;
            }

            Assert.Fail(
                $"Scene '{sceneName}' did not create {typeof(T).Name}.");
        }
    }
}

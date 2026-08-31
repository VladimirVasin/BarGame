using System;
using NUnit.Framework;
using UnityEngine;

namespace BarPromenade.Tests.EditMode
{
    /// <summary>
    /// Pins the pure profile/motion contract and the runtime camera shell.
    /// Imported mesh, texture and shader assets have their own focused
    /// contract fixture.
    /// </summary>
    public sealed class ExteriorCloudFieldTests
    {
        private const int Seed = GameSessionState.DefaultCitySeed;

        [Test]
        public void Profiles_StayBehindEachWorldAndInsideItsFarPlane()
        {
            AssertProfile(
                ExteriorCloudProfiles.City,
                ExteriorCloudProfileKind.City,
                RuntimeSceneSetup.CityFarClipPlane,
                RuntimeSceneSetup.CityFogColor,
                0.90f,
                true);
            AssertProfile(
                ExteriorCloudProfiles.MountainRoad,
                ExteriorCloudProfileKind.MountainRoad,
                RuntimeSceneSetup.MountainRoadFarClipPlane,
                RuntimeSceneSetup.MountainRoadFogColor,
                0.80f,
                false);
            AssertProfile(
                ExteriorCloudProfiles.AlpineVillage,
                ExteriorCloudProfileKind.AlpineVillage,
                RuntimeSceneSetup.AlpineVillageFarClipPlane,
                RuntimeSceneSetup.AlpineVillageFogColor,
                0.95f,
                false);

            Assert.That(
                ExteriorCloudProfiles.City.ShellRadius,
                Is.GreaterThan(
                    CityMountainBackdropWorldBuilder.FarLayerRadius),
                "The cloud ceiling must remain behind City's faint ridge " +
                "shell, not paint over it.");
            Assert.That(
                ExteriorCloudProfiles.AlpineVillage.StormContrastLoss,
                Is.GreaterThan(
                    ExteriorCloudProfiles.City.StormContrastLoss),
                "The village gust must flatten cloud contrast instead of " +
                "stacking a second whiteout over the breathing haze.");

            foreach (ExteriorCloudProfileKind kind in
                     Enum.GetValues(typeof(ExteriorCloudProfileKind)))
            {
                Assert.That(
                    ExteriorCloudProfiles.Resolve(kind).Kind,
                    Is.EqualTo(kind));
            }

            Assert.That(
                () => ExteriorCloudProfiles.Resolve(
                    (ExteriorCloudProfileKind)99),
                Throws.TypeOf<ArgumentOutOfRangeException>());
        }

        [Test]
        public void Motion_IsDeterministicWrappedAndIndependentOfCallOrder()
        {
            ExteriorCloudProfile[] profiles =
            {
                ExteriorCloudProfiles.City,
                ExteriorCloudProfiles.MountainRoad,
                ExteriorCloudProfiles.AlpineVillage
            };
            double[] minutes =
            {
                0d,
                5d,
                GameWeatherRules.SlotMinutes - 0.25d,
                GameWeatherRules.SlotMinutes,
                GameTimeDayNightRules.MinutesPerDay,
                7d * GameTimeDayNightRules.MinutesPerDay + 83.25d
            };

            WindSample firstStepWind = GameWeatherRules.EvaluateWind(
                Seed,
                ExteriorCloudMotionRules.IntegrationStepMinutes * 0.5d);
            Vector3 windDirection = firstStepWind.HorizontalDirection;
            Vector2 expectedFirstStep = new Vector2(
                windDirection.x,
                windDirection.z) *
                (firstStepWind.Strength01 *
                 (float)ExteriorCloudMotionRules.IntegrationStepMinutes);
            Vector2 measuredFirstStep =
                ExteriorCloudMotionRules.EvaluateCanonicalDisplacement(
                    Seed,
                    ExteriorCloudMotionRules.IntegrationStepMinutes);
            Assert.That(expectedFirstStep.magnitude, Is.GreaterThan(0f));
            Assert.That(
                Vector2.Distance(measuredFirstStep, expectedFirstStep),
                Is.LessThan(0.00001f),
                "Cloud advection must integrate the shared weather wind's " +
                "direction and strength, not a second sky-only schedule.");
            ExteriorCloudMotionSample phaseAtStart =
                ExteriorCloudMotionRules.Evaluate(
                    Seed,
                    0d,
                    ExteriorCloudProfiles.City);
            ExteriorCloudMotionSample phaseAfterFirstStep =
                ExteriorCloudMotionRules.Evaluate(
                    Seed,
                    ExteriorCloudMotionRules.IntegrationStepMinutes,
                    ExteriorCloudProfiles.City);
            Vector2 measuredSamplingShift = SignedWrappedDelta(
                phaseAtStart.BroadPhase,
                phaseAfterFirstStep.BroadPhase);
            Vector2 expectedSamplingShift =
                -expectedFirstStep * ExteriorCloudProfiles.City.BroadSpeed;
            Assert.That(
                Vector2.Distance(
                    measuredSamplingShift,
                    expectedSamplingShift),
                Is.LessThan(0.00001f),
                "The UV sampling phase must oppose displacement so the " +
                "visible cloud pattern travels with the wind.");

            for (int profileIndex = 0;
                 profileIndex < profiles.Length;
                 profileIndex++)
            {
                ExteriorCloudProfile profile = profiles[profileIndex];
                for (int timeIndex = 0;
                     timeIndex < minutes.Length;
                     timeIndex++)
                {
                    double minute = minutes[timeIndex];
                    ExteriorCloudMotionSample first =
                        ExteriorCloudMotionRules.Evaluate(
                            Seed,
                            minute,
                            profile);

                    // Grow the shared prefix cache past this sample, then
                    // ask for the old instant again. Cache order must not
                    // become presentation state.
                    ExteriorCloudMotionRules.Evaluate(
                        Seed,
                        minute + 900d,
                        profile);
                    ExteriorCloudMotionSample repeated =
                        ExteriorCloudMotionRules.Evaluate(
                            Seed,
                            minute,
                            profile);

                    AssertPhaseInUnitRange(first);
                    Assert.That(
                        repeated.BroadPhase,
                        Is.EqualTo(first.BroadPhase));
                    Assert.That(
                        repeated.DetailPhase,
                        Is.EqualTo(first.DetailPhase));
                }
            }

            ExteriorCloudMotionSample seeded =
                ExteriorCloudMotionRules.Evaluate(
                    Seed,
                    723.5d,
                    ExteriorCloudProfiles.City);
            ExteriorCloudMotionSample otherSeed =
                ExteriorCloudMotionRules.Evaluate(
                    Seed + 1,
                    723.5d,
                    ExteriorCloudProfiles.City);
            Assert.That(
                WrappedDistance(
                    seeded.BroadPhase,
                    otherSeed.BroadPhase) +
                WrappedDistance(
                    seeded.DetailPhase,
                    otherSeed.DetailPhase),
                Is.GreaterThan(0.001f),
                "A different world seed must not inherit the same sky.");

            Assert.That(
                () => ExteriorCloudMotionRules.Evaluate(
                    Seed,
                    -0.01d,
                    ExteriorCloudProfiles.City),
                Throws.TypeOf<ArgumentOutOfRangeException>());
            Assert.That(
                () => ExteriorCloudMotionRules.Evaluate(
                    Seed,
                    double.NaN,
                    ExteriorCloudProfiles.City),
                Throws.TypeOf<ArgumentOutOfRangeException>());
            Assert.That(
                () => ExteriorCloudMotionRules.Evaluate(
                    Seed,
                    double.PositiveInfinity,
                    ExteriorCloudProfiles.City),
                Throws.TypeOf<ArgumentOutOfRangeException>());
        }

        [Test]
        public void Motion_RemainsContinuousAcrossSlotsStepsAndDayWrap()
        {
            double[] boundaries =
            {
                ExteriorCloudMotionRules.IntegrationStepMinutes,
                GameWeatherRules.SlotMinutes,
                GameTimeDayNightRules.MinutesPerDay
            };
            ExteriorCloudProfile[] profiles =
            {
                ExteriorCloudProfiles.City,
                ExteriorCloudProfiles.MountainRoad,
                ExteriorCloudProfiles.AlpineVillage
            };
            const double epsilonMinutes = 0.001d;

            for (int profileIndex = 0;
                 profileIndex < profiles.Length;
                 profileIndex++)
            {
                ExteriorCloudProfile profile = profiles[profileIndex];
                for (int boundaryIndex = 0;
                     boundaryIndex < boundaries.Length;
                     boundaryIndex++)
                {
                    double boundary = boundaries[boundaryIndex];
                    ExteriorCloudMotionSample before =
                        ExteriorCloudMotionRules.Evaluate(
                            Seed,
                            boundary - epsilonMinutes,
                            profile);
                    ExteriorCloudMotionSample after =
                        ExteriorCloudMotionRules.Evaluate(
                            Seed,
                            boundary + epsilonMinutes,
                            profile);

                    Assert.That(
                        WrappedDistance(
                            before.BroadPhase,
                            after.BroadPhase),
                        Is.LessThan(0.001f),
                        $"{profile.Kind} broad layer jumped at " +
                        $"minute {boundary:0.###}.");
                    Assert.That(
                        WrappedDistance(
                            before.DetailPhase,
                            after.DetailPhase),
                        Is.LessThan(0.001f),
                        $"{profile.Kind} detail layer jumped at " +
                        $"minute {boundary:0.###}.");
                }
            }
        }

        [Test]
        public void Field_FollowsGameplayAndMarkedCaptureCameras()
        {
            var parent = new GameObject("Cloud Field Test Parent");
            var gameplayObject = new GameObject("Cloud Gameplay Camera");
            var captureObject = new GameObject("Cloud Capture Camera");
            var previewObject = new GameObject("Cloud Preview Camera");
            Camera gameplayCamera = gameplayObject.AddComponent<Camera>();
            Camera captureCamera = captureObject.AddComponent<Camera>();
            Camera previewCamera = previewObject.AddComponent<Camera>();
            captureObject.AddComponent<ExteriorCloudCaptureCamera>();
            gameplayCamera.transform.position = new Vector3(4f, 7f, -12f);
            captureCamera.transform.position = new Vector3(-31f, 3f, 18f);
            previewCamera.cullingMask = 1 << 28;
            Quaternion canonicalFrame = Quaternion.Euler(0f, 73f, 0f);

            try
            {
                ExteriorCloudField field = ExteriorCloudField.Create(
                    parent.transform,
                    gameplayCamera,
                    ExteriorCloudProfiles.City,
                    Seed,
                    canonicalFrame);

                Assert.That(field, Is.Not.Null);
                Assert.That(field.IsInitialized, Is.True);
                Assert.That(
                    field.PrimaryCamera,
                    Is.SameAs(gameplayCamera));
                Assert.That(
                    field.Profile.Kind,
                    Is.EqualTo(ExteriorCloudProfileKind.City));
                Assert.That(field.Renderer, Is.Not.Null);
                Assert.That(field.IsVisible, Is.True);
                Assert.That(field.Renderer.enabled, Is.True);
                Assert.That(
                    field.transform.position,
                    Is.EqualTo(gameplayCamera.transform.position));
                Assert.That(
                    Quaternion.Angle(
                        field.transform.rotation,
                        canonicalFrame),
                    Is.LessThan(0.001f));
                Assert.That(
                    Quaternion.Angle(
                        field.CanonicalFrameRotation,
                        canonicalFrame),
                    Is.LessThan(0.001f));
                AssertPhaseInUnitRange(field.Phase);

                field.AlignToCamera(captureCamera);
                Assert.That(
                    field.transform.position,
                    Is.EqualTo(captureCamera.transform.position),
                    "The fountain cubemap would render a cloud shell still " +
                    "centred on the player camera.");
                Assert.That(
                    Quaternion.Angle(
                        field.transform.rotation,
                        canonicalFrame),
                    Is.LessThan(0.001f));

                field.AlignToCamera(gameplayCamera);
                Assert.That(
                    field.transform.position,
                    Is.EqualTo(gameplayCamera.transform.position));

                int cloudLayerMask = 1 << field.Renderer.gameObject.layer;
                Assert.That(
                    previewCamera.cullingMask & cloudLayerMask,
                    Is.Zero,
                    "The inventory preview camera must not acquire the " +
                    "world cloud dome.");
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(parent);
                UnityEngine.Object.DestroyImmediate(gameplayObject);
                UnityEngine.Object.DestroyImmediate(captureObject);
                UnityEngine.Object.DestroyImmediate(previewObject);
            }
        }

        [Test]
        public void Field_VisibilityAndHazeUseRendererStateNotNewMaterials()
        {
            var parent = new GameObject("Cloud Visibility Test Parent");
            var cameraObject = new GameObject("Cloud Visibility Camera");
            Camera camera = cameraObject.AddComponent<Camera>();
            var propertyBlock = new MaterialPropertyBlock();
            Color haze = new Color(0.18f, 0.23f, 0.21f, 1f);

            try
            {
                ExteriorCloudField field = ExteriorCloudField.Create(
                    parent.transform,
                    camera,
                    ExteriorCloudProfiles.AlpineVillage,
                    Seed);
                Material sharedMaterial = field.Renderer.sharedMaterial;

                field.SetVisibility(haze, 0.75f, 0.35f);
                field.Renderer.GetPropertyBlock(propertyBlock);
                Assert.That(
                    propertyBlock.GetColor("_HazeColor"),
                    Is.EqualTo(haze));
                Assert.That(
                    field.Renderer.sharedMaterial,
                    Is.SameAs(sharedMaterial),
                    "Per-area visibility must stay in a property block, " +
                    "not clone the shared cloud material.");

                field.SetVisible(false);
                Assert.That(field.IsVisible, Is.False);
                Assert.That(field.Renderer.enabled, Is.False);

                field.SetVisible(true);
                Assert.That(field.IsVisible, Is.True);
                Assert.That(field.Renderer.enabled, Is.True);
                Assert.That(
                    field.Renderer.sharedMaterial,
                    Is.SameAs(sharedMaterial));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(parent);
                UnityEngine.Object.DestroyImmediate(cameraObject);
            }
        }

        private static void AssertProfile(
            ExteriorCloudProfile profile,
            ExteriorCloudProfileKind expectedKind,
            float farClipPlane,
            Color expectedHaze,
            float coverageFloor,
            bool supportsLightning)
        {
            Assert.That(profile.Kind, Is.EqualTo(expectedKind));
            Assert.That(profile.HazeColor, Is.EqualTo(expectedHaze));
            Assert.That(
                farClipPlane - profile.ShellRadius,
                Is.InRange(0.5f, 2f),
                $"{expectedKind} shell must stay just inside its finite " +
                "far plane.");
            Assert.That(
                profile.Coverage,
                Is.GreaterThanOrEqualTo(coverageFloor)
                    .And.LessThanOrEqualTo(1f));
            Assert.That(
                profile.SupportsLightning,
                Is.EqualTo(supportsLightning));
            Assert.That(profile.BroadScale, Is.GreaterThan(0f));
            Assert.That(profile.DetailScale, Is.GreaterThan(0f));
            Assert.That(profile.BroadSpeed, Is.GreaterThan(0f));
            Assert.That(
                profile.DetailSpeed,
                Is.GreaterThan(profile.BroadSpeed));
            Assert.That(
                profile.HorizonFadeEnd,
                Is.GreaterThan(profile.HorizonFadeStart));
        }

        private static void AssertPhaseInUnitRange(
            ExteriorCloudMotionSample sample)
        {
            AssertUnitPhase(sample.BroadPhase);
            AssertUnitPhase(sample.DetailPhase);
        }

        private static void AssertUnitPhase(Vector2 phase)
        {
            Assert.That(
                phase.x,
                Is.GreaterThanOrEqualTo(0f).And.LessThan(1f));
            Assert.That(
                phase.y,
                Is.GreaterThanOrEqualTo(0f).And.LessThan(1f));
        }

        private static float WrappedDistance(Vector2 first, Vector2 second)
        {
            float x = WrappedAxisDistance(first.x, second.x);
            float y = WrappedAxisDistance(first.y, second.y);
            return Mathf.Sqrt(x * x + y * y);
        }

        private static float WrappedAxisDistance(float first, float second)
        {
            float difference = Mathf.Abs(first - second);
            return Mathf.Min(difference, 1f - difference);
        }

        private static Vector2 SignedWrappedDelta(
            Vector2 first,
            Vector2 second)
        {
            return new Vector2(
                SignedWrappedAxisDelta(first.x, second.x),
                SignedWrappedAxisDelta(first.y, second.y));
        }

        private static float SignedWrappedAxisDelta(float first, float second)
        {
            float difference = second - first;
            if (difference > 0.5f)
            {
                difference -= 1f;
            }
            else if (difference < -0.5f)
            {
                difference += 1f;
            }

            return difference;
        }
    }
}

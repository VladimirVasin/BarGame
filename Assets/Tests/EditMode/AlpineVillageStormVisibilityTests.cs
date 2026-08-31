using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;

namespace BarPromenade.Tests.EditMode
{
    /// <summary>
    /// The village's haze breathes with the gale, and these are the bounds
    /// that keep that a wave rather than a whiteout: the base density keeps
    /// the top house readable from the platform, the peak closes the far
    /// half of the lane only for the seconds of a gust, the wave always
    /// reopens, and the enclosing wall stays a present mass through all of
    /// it. Nothing here needs a scene; the PlayMode twin watches the same
    /// numbers reach `RenderSettings`.
    /// </summary>
    public sealed class AlpineVillageStormVisibilityTests
    {
        private const int Seed = GameSessionState.DefaultCitySeed;

        /// <summary>Eye height used for every viewpoint on the ground.</summary>
        private const float EyeHeight = 1.72f;

        /// <summary>
        /// The canon viewpoint: the station pad stands `StationSetback`
        /// behind the lane foot, the mother's door `MothersHouseSetback`
        /// past the lane head.
        /// </summary>
        private static float PlatformToDoorMeters =>
            AlpineVillagePlanner.StationSetback +
            AlpineVillagePlanner.LaneLength +
            AlpineVillagePlanner.MothersHouseSetback;

        /// <summary>The landmark rule: at the running trough the door still
        /// keeps this much of itself from the platform.</summary>
        private const float MinimumDoorTransmittance = 0.05f;

        private static AlpineVillagePlan CreatePlan()
        {
            return AlpineVillagePlanner.Create(Seed);
        }

        private static float Transmittance(float density, float distance)
        {
            float term = density * distance;
            return Mathf.Exp(-term * term);
        }

        [Test]
        [Category("AlpineVillageStorm")]
        public void Constants_PinTheStormWindowAndTheDrawRange()
        {
            Assert.That(
                RuntimeSceneSetup.AlpineVillageFogDensity,
                Is.EqualTo(0.017f).Within(0.000001f));
            Assert.That(
                RuntimeSceneSetup.AlpineVillageStormFogDensity,
                Is.EqualTo(0.045f).Within(0.000001f));
            Assert.That(
                RuntimeSceneSetup.AlpineVillageFarClipPlane,
                Is.EqualTo(110f).Within(0.001f));

            // The landmark is never clipped by the plane, only by the haze:
            // from the platform the house's back wall stands setback + lane
            // + setback + depth away, and the plane keeps a margin past it.
            float backWall = PlatformToDoorMeters +
                             AlpineVillagePlanner.MothersHouseFootprint.y;
            Assert.That(
                RuntimeSceneSetup.AlpineVillageFarClipPlane,
                Is.GreaterThanOrEqualTo(backWall + 2f),
                "The far plane cuts the mother's house from the platform.");

            // The cableway's bounds are lower bounds on the far plane and
            // must still hold at the new one.
            MountainRoadCablewayPlan cableway =
                CreatePlan().Station.Cableway;
            float reach = RuntimeSceneSetup.AlpineVillageFarClipPlane +
                          MountainRoadCablewayPlan.HiddenRunMargin;
            Assert.That(
                cableway.HiddenRunMeters,
                Is.GreaterThanOrEqualTo(reach),
                "The far turn stands inside the village's draw range.");
            Assert.That(
                Vector3.Distance(
                    cableway.LowerCableCenter,
                    cableway.UpperCableCenter),
                Is.GreaterThan(reach),
                "The far turn can be seen from the platform.");
        }

        [Test]
        [Category("AlpineVillageStorm")]
        public void FogDensity_RunsBaseToPeakAndTheDimIsClampedAtThePeak()
        {
            Assert.That(
                RuntimeSceneSetup.EvaluateAlpineVillageFogDensity(0f, 0f),
                Is.EqualTo(RuntimeSceneSetup.AlpineVillageFogDensity)
                    .Within(0.0000001f));
            Assert.That(
                RuntimeSceneSetup.EvaluateAlpineVillageFogDensity(1f, 0f),
                Is.EqualTo(RuntimeSceneSetup.AlpineVillageStormFogDensity)
                    .Within(0.0000001f));
            Assert.That(
                RuntimeSceneSetup.EvaluateAlpineVillageFogDensity(1f, 1f),
                Is.LessThanOrEqualTo(
                    RuntimeSceneSetup.AlpineVillageStormFogDensity),
                "The dim end stacks a second whiteout on the storm's.");

            float previous = -1f;
            for (float storm = 0f; storm <= 1.0001f; storm += 0.05f)
            {
                float density =
                    RuntimeSceneSetup.EvaluateAlpineVillageFogDensity(
                        storm,
                        0f);
                Assert.That(
                    density,
                    Is.GreaterThanOrEqualTo(previous),
                    $"The haze thins as the wave rises at {storm}.");
                previous = density;
            }

            // The base keeps the door from the platform; the peak closes
            // the far half of the lane.
            Assert.That(
                Transmittance(
                    RuntimeSceneSetup.AlpineVillageFogDensity,
                    PlatformToDoorMeters),
                Is.GreaterThanOrEqualTo(MinimumDoorTransmittance),
                "Between gusts the top house is gone from the platform.");
            Assert.That(
                Transmittance(
                    RuntimeSceneSetup.AlpineVillageStormFogDensity,
                    AlpineVillagePlanner.LaneLength * 0.5f),
                Is.LessThanOrEqualTo(0.05f),
                "A gust crest leaves the far half of the lane open.");
        }

        /// <summary>
        /// A material stores its colour as float32 while the setup constant
        /// is authored in decimal, so the two agree to a few thousandths and
        /// never to the bit.
        /// </summary>
        private const float HazeColorTolerance = 0.002f;

        private static float ColorDifference(Color actual, Color expected)
        {
            return Mathf.Max(
                Mathf.Abs(actual.r - expected.r),
                Mathf.Abs(actual.g - expected.g),
                Mathf.Abs(actual.b - expected.b),
                Mathf.Abs(actual.a - expected.a));
        }

        [Test]
        [Category("AlpineVillageStorm")]
        public void StormWave_FollowsTheSharedGustAndOpensBetweenGusts()
        {
            // The raw rhythm stays inside its authored band, and the wind
            // every scene already shakes to is the same function it was:
            // the extraction changed nothing.
            float gustFloor = (float)(
                GameWeatherRules.WindGustMean -
                GameWeatherRules.WindGustPrimaryAmplitude -
                GameWeatherRules.WindGustSecondaryAmplitude);
            float gustCrest = (float)(
                GameWeatherRules.WindGustMean +
                GameWeatherRules.WindGustPrimaryAmplitude +
                GameWeatherRules.WindGustSecondaryAmplitude);
            Assert.That(gustCrest, Is.EqualTo(1f).Within(0.0001f));
            for (double minutes = 0d; minutes < 120d; minutes += 0.05d)
            {
                float gust = GameWeatherRules.EvaluateGust(Seed, minutes);
                Assert.That(gust, Is.GreaterThanOrEqualTo(gustFloor - 0.0001f));
                Assert.That(gust, Is.LessThanOrEqualTo(gustCrest + 0.0001f));

                // A developed slot: past the transition, the strength is
                // the slot's target times the gust and nothing else. The
                // probe stays inside ONE slot's developed stretch - the
                // sweep is longer than a slot, and the first minutes of the
                // next slot lerp the strength from the previous slot's
                // target, which is not the relationship under test.
                double developedStretch =
                    GameWeatherRules.SlotMinutes -
                    GameWeatherRules.TransitionMinutes -
                    0.1d;
                double developed = 3d * GameWeatherRules.SlotMinutes +
                                   GameWeatherRules.TransitionMinutes +
                                   (minutes % developedStretch);
                float target = GameWeatherRules.GetTargetWindStrength(
                    GameWeatherRules.EvaluateSlotKind(
                        Seed,
                        (long)System.Math.Floor(
                            developed / GameWeatherRules.SlotMinutes)));
                Assert.That(
                    GameWeatherRules.EvaluateWind(Seed, developed).Strength01,
                    Is.EqualTo(
                            Mathf.Clamp01(
                                target *
                                GameWeatherRules.EvaluateGust(
                                    Seed,
                                    developed)))
                        .Within(0.00001f),
                    "EvaluateGust is not the gust EvaluateWind rides.");
            }

            // The target: closed at the crest, open at the floor, monotone.
            Assert.That(
                AlpineVillageStormFieldRules.EvaluateStormWaveTarget(
                    AlpineVillageStormFieldRules.StormWaveGustFloor),
                Is.EqualTo(0f).Within(0.0001f));
            Assert.That(
                AlpineVillageStormFieldRules.EvaluateStormWaveTarget(
                    AlpineVillageStormFieldRules.StormWaveGustCrest),
                Is.EqualTo(1f).Within(0.0001f));
            Assert.That(
                AlpineVillageStormFieldRules.EvaluateStormWaveTarget(
                    gustFloor),
                Is.EqualTo(0f));
            Assert.That(
                AlpineVillageStormFieldRules.EvaluateStormWaveTarget(
                    gustCrest),
                Is.EqualTo(1f));
            float previousTarget = -1f;
            for (float gust = 0f; gust <= 1.0001f; gust += 0.01f)
            {
                float waveTarget =
                    AlpineVillageStormFieldRules.EvaluateStormWaveTarget(
                        gust);
                Assert.That(waveTarget, Is.GreaterThanOrEqualTo(previousTarget));
                previousTarget = waveTarget;
            }

            Assert.That(
                AlpineVillageStormFieldRules.AdvanceStormWave(0.4f, 1f, 0f),
                Is.EqualTo(0.4f),
                "A frozen clock moved the wave.");

            // Six hundred game minutes at sixty frames a second, one minute
            // per real second: the haze must close for a real share of the
            // time, reach a crest and a trough inside every fifteen seconds,
            // and never - at its RUNNING trough, not at the pure base -
            // take the door below the landmark rule from the platform.
            const float frameSeconds = 1f / 60f;
            const int frameCount = 600 * 60;
            const int windowFrames = 15 * 60;
            double clock = 2d * GameTimeDayNightRules.MinutesPerDay + 460d;
            float wave = 0f;
            var samples = new List<float>(frameCount);
            for (int frame = 0; frame < frameCount; frame++)
            {
                float waveTarget =
                    AlpineVillageStormFieldRules.EvaluateStormWaveTarget(
                        GameWeatherRules.EvaluateGust(Seed, clock));
                wave = AlpineVillageStormFieldRules.AdvanceStormWave(
                    wave,
                    waveTarget,
                    frameSeconds);
                samples.Add(wave);
                clock += frameSeconds;
            }

            int closedFrames = 0;
            float minimumWave = 1f;
            for (int index = 0; index < samples.Count; index++)
            {
                if (samples[index] > 0.5f)
                {
                    closedFrames++;
                }

                minimumWave = Mathf.Min(minimumWave, samples[index]);
            }

            float closedFraction = closedFrames / (float)samples.Count;
            Assert.That(
                closedFraction,
                Is.InRange(0.20f, 0.55f),
                "The lane is closed for the wrong share of the storm.");

            for (int start = 0; start + windowFrames <= samples.Count;
                 start += windowFrames)
            {
                float windowMaximum = 0f;
                float windowMinimum = 1f;
                for (int index = start; index < start + windowFrames; index++)
                {
                    windowMaximum = Mathf.Max(windowMaximum, samples[index]);
                    windowMinimum = Mathf.Min(windowMinimum, samples[index]);
                }

                Assert.That(
                    windowMaximum,
                    Is.GreaterThanOrEqualTo(0.85f),
                    $"No gust crest in the window starting at frame {start}.");
                Assert.That(
                    windowMinimum,
                    Is.LessThanOrEqualTo(0.12f),
                    $"The haze never reopened in the window at frame {start}.");
            }

            float troughDensity =
                RuntimeSceneSetup.EvaluateAlpineVillageFogDensity(
                    minimumWave,
                    0f);
            Assert.That(
                Transmittance(troughDensity, PlatformToDoorMeters),
                Is.GreaterThanOrEqualTo(MinimumDoorTransmittance),
                "At the running trough the top house is gone from the " +
                $"platform (wave {minimumWave:0.###}, density " +
                $"{troughDensity:0.####}).");
        }

        [Test]
        [Category("AlpineVillageStorm")]
        public void Ridge_StaysAPresentMassThroughTheWave()
        {
            float[] densities =
            {
                RuntimeSceneSetup.AlpineVillageFogDensity,
                RuntimeSceneSetup.AlpineVillageStormFogDensity
            };
            foreach (float density in densities)
            {
                for (float distance =
                         AlpineVillageRidgeAppearance.NativeFogFarDistance;
                     distance <= RuntimeSceneSetup.AlpineVillageFarClipPlane;
                     distance += 1f)
                {
                    Assert.That(
                        AlpineVillageRidgeAppearance.EvaluateRidgeVisibility(
                            distance,
                            density),
                        Is.GreaterThanOrEqualTo(
                            AlpineVillageRidgeAppearance.VisibilityFloor -
                            0.0001f),
                        $"The wall dissolves at {distance} m under {density}.");
                }

                for (float distance = 0f;
                     distance <= AlpineVillageRidgeAppearance
                         .NativeFogNearDistance;
                     distance += 0.5f)
                {
                    Assert.That(
                        AlpineVillageRidgeAppearance.EvaluateRidgeVisibility(
                            distance,
                            density),
                        Is.EqualTo(Transmittance(density, distance))
                            .Within(0.0001f),
                        "Close in, the wall must be on native fog like the " +
                        "ground it meets.");
                }
            }

            // The stable opaque haze blend starts beyond every crest the
            // plan produces from the lane, and ends inside the plane.
            AlpineVillagePlan plan = CreatePlan();
            AlpineVillageLaneSample middle =
                plan.Lane.Sample(plan.Lane.Length * 0.5f);
            AlpineVillageLaneSample head = plan.Lane.Sample(plan.Lane.Length);
            (Vector3 origin, Vector3 direction, string label)[] rays =
            {
                (middle.Position, middle.Right, "mid-lane toward +Right"),
                (middle.Position, -middle.Right, "mid-lane toward -Right"),
                (head.Position, plan.Uphill, "lane head uphill")
            };
            foreach ((Vector3 origin, Vector3 direction, string label) in rays)
            {
                float crest = FindCrestDistance(plan, origin, direction);
                Assert.That(
                    crest,
                    Is.GreaterThan(0f),
                    $"No crest along {label} inside the draw range.");
                Assert.That(
                    AlpineVillageRidgeAppearance.HandoffNearDistance,
                    Is.GreaterThanOrEqualTo(crest),
                    $"The haze blend crosses the crest {label} at {crest} m.");
            }

            Assert.That(
                AlpineVillageRidgeAppearance.HandoffNearDistance,
                Is.LessThan(AlpineVillageRidgeAppearance.HandoffFarDistance));
            Assert.That(
                AlpineVillageRidgeAppearance.HandoffFarDistance,
                Is.LessThan(RuntimeSceneSetup.AlpineVillageFarClipPlane),
                "The wall reaches the far plane and is cut instead of handed off.");

            Material material = AlpineVillageRidgeAppearance.RidgeMaterial;
            Assert.That(
                material.shader.name,
                Is.EqualTo("Bar Promenade/City Mountain Physical"));
            Assert.That(
                material.GetFloat("_VisibilityFloor"),
                Is.EqualTo(AlpineVillageRidgeAppearance.VisibilityFloor)
                    .Within(0.0001f));
            Assert.That(
                material.GetFloat("_StableHazeHandoff"),
                Is.EqualTo(AlpineVillageRidgeAppearance.StableHazeHandoff));
            Assert.That(
                material.GetFloat("_Ps1VertexSnap"),
                Is.EqualTo(AlpineVillageRidgeAppearance.Ps1VertexSnap));

            // Both switches are zero-default shader properties. The City
            // material never writes them, so its existing screen-space
            // dither and unsnapped vertex paths remain selected.
            Material cityMaterial =
                CityMountainSurfaceAppearance.PhysicalRidgeMaterial;
            Assert.That(
                cityMaterial.GetFloat("_StableHazeHandoff"),
                Is.Zero);
            Assert.That(
                cityMaterial.GetFloat("_Ps1VertexSnap"),
                Is.Zero);
            Assert.That(
                ColorDifference(
                    material.GetColor("_HazeColor"),
                    RuntimeSceneSetup.AlpineVillageFogColor),
                Is.LessThan(HazeColorTolerance),
                "The ridge material must carry the village's own haze " +
                "colour.");

            // The apply writes what the tests above assumed.
            bool fog = RenderSettings.fog;
            Color fogColor = RenderSettings.fogColor;
            FogMode fogMode = RenderSettings.fogMode;
            float fogDensity = RenderSettings.fogDensity;
            var cameraObject = new GameObject("Alpine Storm Test Camera");
            try
            {
                Camera camera = cameraObject.AddComponent<Camera>();
                RuntimeSceneSetup.ApplyAlpineVillageVisibility(camera, 0f, 1f);
                Assert.That(
                    camera.farClipPlane,
                    Is.EqualTo(RuntimeSceneSetup.AlpineVillageFarClipPlane)
                        .Within(0.001f));
                Assert.That(
                    RenderSettings.fogDensity,
                    Is.EqualTo(RuntimeSceneSetup.AlpineVillageStormFogDensity)
                        .Within(0.000001f));
                Assert.That(
                    RenderSettings.fogMode,
                    Is.EqualTo(FogMode.ExponentialSquared));
                Assert.That(
                    camera.backgroundColor,
                    Is.EqualTo(RenderSettings.fogColor));
            }
            finally
            {
                Object.DestroyImmediate(cameraObject);
                RenderSettings.fog = fog;
                RenderSettings.fogColor = fogColor;
                RenderSettings.fogMode = fogMode;
                RenderSettings.fogDensity = fogDensity;
            }
        }

        /// <summary>
        /// Distance along the ray, from a viewpoint on the ground, at which
        /// the ridge term first reaches its full rise; `0` if it never does
        /// inside the draw range.
        /// </summary>
        private static float FindCrestDistance(
            AlpineVillagePlan plan,
            Vector3 origin,
            Vector3 direction)
        {
            Vector3 flat = new Vector3(direction.x, 0f, direction.z).normalized;
            for (float distance = 0.5f;
                 distance <= RuntimeSceneSetup.AlpineVillageFarClipPlane;
                 distance += 0.5f)
            {
                Vector3 point = origin + flat * distance;
                float rise = AlpineVillageTerrainSampler.SampleRidgeRise(
                    plan,
                    new Vector2(point.x, point.z));
                if (rise >= AlpineVillageTerrainSampler.RidgeMaximumRise -
                    0.001f)
                {
                    return distance;
                }
            }

            return 0f;
        }
    }
}

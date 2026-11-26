#if UNITY_EDITOR
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEditorInternal;
using UnityEngine;
using UnityEngine.Profiling;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

namespace BarPromenade.Tests.PlayMode
{
    public sealed partial class AreaCaptureFixture
    {
        private const string EastSlopeCapture = "CityEastSlopeFrameTime";

        [UnityTest]
        [Timeout(240000)]
        [Explicit("Rendered 02-01 after district streaming: compare frame time, reflection faces and platform shadow fade.")]
        public IEnumerator CityEastSlopeFrameTime()
        {
            Assert.That(SystemInfo.graphicsDeviceType, Is.Not.EqualTo(GraphicsDeviceType.Null));
            Assert.That(GraphicsEffectsSettings.CleanReflectionsEnabled, Is.True);
            bool wasRunningInBackground = Application.runInBackground;
            Application.runInBackground = true;
            try
            {
                yield return SceneManager.LoadSceneAsync(SceneIds.MainMenu, LoadSceneMode.Single);
                var blueprint = CityBlueprintCatalog.Resolve(GameSessionState.DefaultCityBlueprintId);
                var settings = CityGenerationSettings.Default;
                int seed = GameSessionState.DefaultCitySeed;
                CityLayoutCache.PrimeHomePlans(blueprint, settings, seed, EastSlopeCapture);
                var preparation = CityLayoutCache.WaitForPrimedPlans(blueprint, settings, seed);
                while (preparation != null && !preparation.IsCompleted) yield return null;
                var layout = CityLayoutCache.GetOrGenerate(blueprint, settings, seed);
                _ = layout.BuildingMasses;

                Assert.That(NewGameStartService.TryStart(NewGameLocation.Ridge, 6 * 60), Is.True);
                GameSessionState.TrySetDebugWeather(WeatherKind.LightRain);
                CityGameRoot city;
                do
                {
                    yield return null;
                    city = Object.FindAnyObjectByType<CityGameRoot>();
                } while (city == null || !city.IsInitialized || SceneTransitionService.IsTransitioning);

                var approach = city.BusPlans.Single(line => line.Number == 3);
                var hero = city.Player;
                hero.Motor.SetInputEnabled(false);
                hero.Interactor.SetInputEnabled(false);
                hero.Motor.Teleport(CityBusStopWaitPlanner.StandingPosition(approach, approach.Stops.Last(), city.Layout) +
                    Vector3.up * PlayerFactory.GroundedRootOffset);
                Physics.SyncTransforms();
                Debug.Log(EastSlopeCapture + " waiting for the lower district.");
                while (!city.LowerDistrictReady) yield return null;

                var line02 = city.BusPlans.Single(line => line.Number == 2);
                var stop = line02.Stops.Single(platform => platform.SequenceIndex == 0);
                Assert.That(stop.Id, Does.EndWith("transfer:east-slope"));
                hero.Motor.Teleport(CityBusStopWaitPlanner.StandingPosition(line02, stop, city.Layout, 3f) +
                    Vector3.up * PlayerFactory.GroundedRootOffset);
                Physics.SyncTransforms();
                Camera camera = city.Camera;
                var follow = camera.GetComponent<PlayerCameraFollow>();
                follow.SetOrbitInputEnabled(false);
                Vector3 aim = stop.Position - hero.GameObject.transform.position;
                aim.y = 0f;
                follow.RotateYaw(Vector3.SignedAngle(Vector3.ProjectOnPlane(camera.transform.forward, Vector3.up), aim, Vector3.up));
                follow.Snap();
                yield return new WaitForSecondsRealtime(4f);

                Debug.Log($"{EastSlopeCapture} ready at {stop.Id}, hero {hero.GameObject.transform.position:F3}, " +
                    $"{camera.pixelWidth}x{camera.pixelHeight}, {SystemInfo.graphicsDeviceType}/{SystemInfo.renderingThreadingMode}, " +
                    $"focused={Application.isFocused}, both districts={city.LowerDistrictReady && city.RidgeDistrictReady}.");
                var intervals = new List<float>(720);
                double until = Time.realtimeSinceStartupAsDouble + 12d;
                while (Time.realtimeSinceStartupAsDouble < until)
                {
                    intervals.Add(Time.unscaledDeltaTime * 1000f);
                    yield return null;
                }
                intervals.Sort();
                Debug.Log($"{EastSlopeCapture} unprofiled frame ms median/p95/max " +
                    $"{intervals[intervals.Count / 2]:F2}/{intervals[(intervals.Count - 1) * 95 / 100]:F2}/{intervals[^1]:F2}.");

                var reflection = city.Lighting.Reflection;
                reflection.Rearm();
                Assert.That(ProfilerDriver.enabled || Profiler.enabled || Profiler.enableBinaryLog, Is.False);
                bool oldDeep = ProfilerDriver.deepProfiling;
                bool oldEditor = ProfilerDriver.profileEditor;
                bool oldCpu = ProfilerDriver.IsAreaEnabled(ProfilerArea.CPU);
                try
                {
                    ProfilerDriver.ClearAllFrames();
                    ProfilerDriver.deepProfiling = false;
                    ProfilerDriver.profileEditor = true;
                    ProfilerDriver.SetAreaEnabled(ProfilerArea.CPU, true);
                    ProfilerDriver.enabled = true;
                    Profiler.enabled = true;
                    yield return new WaitForSecondsRealtime(6f);
                    ProfilerDriver.enabled = false;
                    Profiler.enabled = false;
                    ReportEastSlopeProfilerFrames();
                }
                finally
                {
                    ProfilerDriver.enabled = false;
                    Profiler.enabled = false;
                    ProfilerDriver.deepProfiling = oldDeep;
                    ProfilerDriver.profileEditor = oldEditor;
                    ProfilerDriver.SetAreaEnabled(ProfilerArea.CPU, oldCpu);
                }

                double captureDeadline = Time.realtimeSinceStartupAsDouble + 10d;
                while (!reflection.AllReady && Time.realtimeSinceStartupAsDouble < captureDeadline)
                    yield return null;
                Assert.That(reflection.AllReady, Is.True);
                foreach (var zone in CityReflectionSlots(city))
                {
                    var probe = zone.GetComponentInChildren<ReflectionProbe>();
                    Assert.That(probe.timeSlicingMode, Is.EqualTo(ReflectionProbeTimeSlicingMode.IndividualFaces));
                }
                CaptureCurrentCamera(camera, EastSlopeCapture, "east-slope-dawn");

                follow.enabled = false;
                var fixture = city.Night.Atmosphere.BusStopFixtures.Single(lamp =>
                    (lamp.transform.position - stop.ShelterPosition).sqrMagnitude < .0001f);
                var lightRanges = fixture.Lights.Select(light => light.range).ToArray();
                var lightPowers = fixture.Lights.Select(light => light.intensity).ToArray();
                var atmosphere = city.Night.Atmosphere;
                city.DayNight.enabled = false;
                Vector3 target = fixture.transform.position;
                Vector3 roadside = stop.RoadsideForward.normalized;
                foreach ((float distance, float strength) in new[] { (9f, 1f), (18f, .5f), (26f, 0f) })
                {
                    camera.transform.SetPositionAndRotation(target + roadside * distance + Vector3.up * 1.6f,
                        Quaternion.LookRotation(-roadside));
                    atmosphere.RefreshStopShadows(camera, 1f, true);
                    Assert.That(fixture.ShadowStrength, Is.EqualTo(strength).Within(.015f));
                    Assert.That(fixture.Lights.Select(light => light.range), Is.EqualTo(lightRanges));
                    Assert.That(fixture.Lights.Select(light => light.intensity), Is.EqualTo(lightPowers));
                    if (strength == 1f)
                        CaptureCurrentCamera(camera, EastSlopeCapture, $"platform-{distance:F0}m");
                }
            }
            finally
            {
                Application.runInBackground = wasRunningInBackground;
                GameSessionState.TrySetDebugWeather(null);
            }
        }

        private static void ReportEastSlopeProfilerFrames()
        {
            var frames = new List<float>();
            var localShadows = new List<double>();
            var gameView = new List<double>();
            var probeFrames = new List<double>();
            int maxFaces = 0;
            float slowestCaptureFrame = 0f;
            for (int frame = ProfilerDriver.firstFrameIndex; frame <= ProfilerDriver.lastFrameIndex; frame++)
            {
                using var data = ProfilerDriver.GetRawFrameDataView(frame, 0);
                if (!data.valid) continue;
                frames.Add(data.frameTimeMs);
                double shadowTime = 0, gameViewTime = 0, probeTime = 0;
                int probeEnd = -1, faces = 0;
                for (int sample = 0; sample < data.sampleCount; sample++)
                {
                    string marker = data.GetSampleName(sample);
                    if (marker == "ReflectionProbes.RenderProbe")
                    {
                        probeEnd = sample + data.GetSampleChildrenCountRecursive(sample);
                        probeTime += data.GetSampleTimeMs(sample);
                    }
                    else if (sample <= probeEnd && marker == "Inl_UniversalRenderTotal") faces++;
                    else if (marker == "Draw Additional Lights Shadowmap") shadowTime += data.GetSampleTimeMs(sample);
                    else if (marker == "GameView.Render") gameViewTime += data.GetSampleTimeMs(sample);
                }
                maxFaces = Math.Max(maxFaces, faces);
                if (faces > 0)
                {
                    probeFrames.Add(probeTime);
                    slowestCaptureFrame = Mathf.Max(slowestCaptureFrame, data.frameTimeMs);
                }
                else
                {
                    localShadows.Add(shadowTime);
                    gameView.Add(gameViewTime);
                }
            }
            Assert.That(probeFrames, Is.Not.Empty, "Capture real cubemap work in the rendered City.");
            Assert.That(maxFaces, Is.EqualTo(1), "Only one cubemap face may render in an engine frame.");
            Assert.That(gameView.Max(), Is.GreaterThan(0), "Measure a rendered Editor Game View.");
            frames.Sort();
            localShadows.Sort();
            gameView.Sort();
            Debug.Log($"{EastSlopeCapture} CPU Timeline frame ms median/p95/max " +
                $"{frames[frames.Count / 2]:F2}/{frames[(frames.Count - 1) * 95 / 100]:F2}/{frames[^1]:F2}, " +
                $"cubemap faces/frame {maxFaces}, peak cubemap {probeFrames.Max():F2} ms, " +
                $"longest frame carrying a face {slowestCaptureFrame:F2} ms, " +
                $"other frames median GameView.Render {gameView[gameView.Count / 2]:F2} ms and " +
                $"additional shadow maps {localShadows[localShadows.Count / 2]:F2} ms.");
        }
    }
}
#endif

using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

namespace BarPromenade.Tests.PlayMode
{
    public sealed partial class AreaCaptureFixture
    {
        [UnityTest]
        [Explicit("Compare street fog while walking into and out of an open-sided bus shelter.")]
        public IEnumerator CityStreetFogWalk()
        {
            GameSessionState.BeginNewGame();
            GameSessionState.TryStartGameTimeAt(720);
            yield return SceneManager.LoadSceneAsync(SceneIds.City);
            CityGameRoot city = null;
            yield return WaitHomeBalcony(() =>
            {
                city = Object.FindAnyObjectByType<CityGameRoot>();
                return city != null && city.IsInitialized;
            }, "City street fog");

            var camera = city.Camera;
            var follow = camera.GetComponent<PlayerCameraFollow>();
            var field = city.Night.FogField;
            Assert.That(field.FogRenderer.renderMode, Is.EqualTo(ParticleSystemRenderMode.Mesh));
            Assert.That(field.FogRenderer.sharedMaterial.shader.isSupported, Is.True);
            Assert.That(field.Particles.main.maxParticles, Is.EqualTo(36));
            Assert.That(camera.farClipPlane, Is.EqualTo(96f));
            Assert.That(RenderSettings.fogDensity, Is.EqualTo(.030f).Within(.0001f));

            var hero = city.Player.GameObject.transform;
            var stop = CityBusNetworkFurniture.Stops(city.BusPlans)
                .OrderBy(s => (s.ShelterPosition - hero.position).sqrMagnitude).First();
            var bench = CityBusStopWorldBuilder.DescribeShelterBenchSeat(stop);
            Vector3 underRoof = bench.SeatTopCenter + bench.FaceDirection * .33f;
            underRoof.y = bench.GroundY;
            Vector3 along = Vector3.Cross(Vector3.up, bench.FaceDirection);
            Vector3 approach = underRoof - along * 6f + bench.FaceDirection * .95f;
            Quaternion alongStreet = Quaternion.LookRotation(along * .79f + bench.FaceDirection * .61f - Vector3.up * .04f);
            Quaternion towardShelter = Quaternion.LookRotation(underRoof - approach - Vector3.up * .15f);
            var referenceHost = new GameObject("Reference street fog sheets");
            var reference = referenceHost.AddComponent<CityFogField>();
            float oldStep = Time.captureDeltaTime;
            bool input = city.Player.Motor.InputEnabled;
            var hidden = hero.GetComponentsInChildren<Renderer>().Where(r => r.enabled).ToArray();
            try
            {
                follow.enabled = false;
                city.Player.Motor.SetInputEnabled(false);
                foreach (var r in hidden) r.enabled = false;
                Time.captureDeltaTime = 1f / 20f;
                city.Player.Motor.Teleport(approach + Vector3.up * PlayerFactory.GroundedRootOffset);
                reference.Initialize(hero, CityNightResources.AtmosphereMaterial, city.Layout.Seed, true);
                reference.FogRenderer.forceRenderingOff = true;
                field.SetSheltered(true);
                field.SetSheltered(false);
                foreach (var (minute, light) in new[] { (720, "day"), (1260, "night") })
                {
                    AdvanceNightGradeClock(minute);
                    city.DayNight.ApplyCurrentTime(true);
                    city.Weather.ApplyCurrentWeather(true);
                    for (int frame = 0; frame <= 60; frame++)
                    {
                        float t = frame / 30f;
                        Vector3 feet = Vector3.Lerp(approach, underRoof, 1f - Mathf.Abs(t - 1f));
                        city.Player.Motor.Teleport(feet + Vector3.up * PlayerFactory.GroundedRootOffset);
                        camera.transform.SetPositionAndRotation(feet + Vector3.up * EyeHeight,
                            Quaternion.Slerp(towardShelter, alongStreet, 1f - Mathf.Abs(t - 1f)));
                        Vector3 wind = city.Weather.CurrentGust.Velocity(GameWeatherRules.WindSpeedAtFullStrength);
                        reference.SetWindDrift(new Vector2(wind.x, wind.z));
                        yield return null;
                        if (frame % 15 != 0) continue;
                        field.FogRenderer.forceRenderingOff = true;
                        reference.FogRenderer.forceRenderingOff = false;
                        CaptureCurrentCamera(camera, "CityStreetFog", $"{light}-{frame:D2}-before");
                        reference.FogRenderer.forceRenderingOff = true;
                        field.FogRenderer.forceRenderingOff = false;
                        CaptureCurrentCamera(camera, "CityStreetFog", $"{light}-{frame:D2}-after");
                    }
                    camera.transform.rotation = alongStreet;
                    yield return CaptureStationaryFog(camera, field.FogRenderer, light);
                }
                yield return MeasureStreetFogTime(reference.FogRenderer, field.FogRenderer);
            }
            finally
            {
                Object.Destroy(referenceHost);
                field.FogRenderer.forceRenderingOff = false;
                foreach (var r in hidden) if (r != null) r.enabled = true;
                Time.captureDeltaTime = oldStep;
                follow.enabled = true;
                city.Player.Motor.SetInputEnabled(input);
                GameSessionState.BeginNewGame();
            }
        }

        private static IEnumerator CaptureStationaryFog(Camera camera, Renderer volumes, string light)
        {
            const int width = 640, height = 360;
            string folder = Path.Combine(Directory.GetCurrentDirectory(), "Captures", "CityStreetFog");
            Color32[] initial = null;
            for (int frame = 0; frame <= 80; frame++)
            {
                yield return null;
                if (frame % 10 != 0) continue;
                string name = $"{light}-fixed-{frame:D2}";
                CaptureCurrentCamera(camera, "CityStreetFog", name, width, height);
                if (frame != 0 && frame != 80) continue;
                // Subtract the same rendered street with the volumes hidden. This isolates
                // changes in the fog from the moving people, rain and illuminated pavement.
                volumes.forceRenderingOff = true;
                CaptureCurrentCamera(camera, "CityStreetFog", name + "-clear", width, height);
                volumes.forceRenderingOff = false;
                Color32[] mist = Pixels(name), clear = Pixels(name + "-clear");
                for (int i = 0; i < mist.Length; i++)
                    mist[i].g = (byte)Mathf.Clamp(128 + mist[i].g - clear[i].g, 0, 255);
                if (initial == null) { initial = mist; continue; }
                int changing = 0;
                for (int i = 0; i < mist.Length; i++)
                    if (Mathf.Abs(mist[i].g - initial[i].g) >= 4) changing++;
                Debug.Log($"Stationary {light} street, pixels with changing fog: {100f * changing / mist.Length:F1}%");
                Assert.That(changing, Is.GreaterThan(mist.Length / 100), "Visible fog must move at a fixed camera.");
            }

            Color32[] Pixels(string name)
            {
                var image = new Texture2D(2, 2, TextureFormat.RGBA32, false);
                try
                {
                    Assert.That(image.LoadImage(File.ReadAllBytes(Path.Combine(folder, name + ".png"))), Is.True);
                    return image.GetPixels32();
                }
                finally { Object.DestroyImmediate(image); }
            }
        }

        private static IEnumerator MeasureStreetFogTime(Renderer sheets, Renderer volumes)
        {
            var samples = new[] { new List<double>(), new List<double>() };
            var timing = new FrameTiming[1];
            ulong lastFrame = 0;
            using (GameTimeScaleRuntime.AcquirePause())
            {
                for (int block = 0; block < 4; block++)
                {
                    int version = block == 0 || block == 3 ? 0 : 1;
                    sheets.forceRenderingOff = version != 0;
                    volumes.forceRenderingOff = version != 1;
                    for (int frame = 0; frame < 24; frame++)
                    {
                        FrameTimingManager.CaptureFrameTimings();
                        yield return null;
                        if (frame < 8 || FrameTimingManager.GetLatestTimings(1, timing) == 0 ||
                            timing[0].frameStartTimestamp == lastFrame || timing[0].gpuFrameTime <= 0) continue;
                        lastFrame = timing[0].frameStartTimestamp;
                        samples[version].Add(timing[0].gpuFrameTime);
                    }
                }
            }
            foreach (var values in samples) values.Sort();
            string Median(List<double> values) => values.Count == 0 ? "unavailable" : values[values.Count / 2].ToString("F2");
            Debug.Log($"Street fog GPU frame median, ms: sheets {Median(samples[0])}, volumes {Median(samples[1])}");
        }
    }
}

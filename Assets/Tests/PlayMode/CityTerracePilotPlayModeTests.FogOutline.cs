using System.Collections;
using System.IO;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

namespace BarPromenade.Tests.PlayMode
{
    public sealed partial class CityTerracePilotPlayModeTests
    {
        [UnityTest, Explicit("Check that distant silhouettes retain the lamplight in front of them.")]
        public IEnumerator CityRidgeNightFog()
        {
            const string folder = "CityRidgeNightFog";
            const int width = 1440;
            const int height = 720;
            Vector3 position = new(-25.5f, 40.1f, -349.2f);
            bool previousDof = GraphicsEffectsSettings.DepthOfFieldEnabled;
            bool previousAa = GraphicsEffectsSettings.CleanAntialiasingEnabled;
            bool previousVolume = GraphicsEffectsSettings.CleanVolumeEnabled;
            try
            {
                GraphicsEffectsSettings.DepthOfFieldEnabled = true;
                GraphicsEffectsSettings.CleanAntialiasingEnabled = true;
                GraphicsEffectsSettings.CleanVolumeEnabled = true;
                Assert.That(GameSessionState.TryStartGameTimeAt(0), Is.True);
                yield return LoadProductionCity();
                Object.FindAnyObjectByType<GameTimeRuntime>().enabled = false;
                city.Weather.enabled = false;
                city.Player.Motor.SetInputEnabled(false);
                Camera camera = city.Camera;
                camera.GetComponent<PlayerCameraFollow>().enabled = false;
                city.Player.Motor.Teleport(position);
                city.Player.Motor.transform.rotation = Quaternion.identity;
                SetFogView(camera, position, 260f, 0f);
                Physics.SyncTransforms();
                city.Night.Atmosphere.RefreshImmediate();
                yield return null;
                yield return null;
                Time.timeScale = 0f;

                foreach (Volume volume in Object.FindObjectsByType<Volume>())
                    if (volume.profile.TryGet(out FilmGrain grain)) grain.active = false;

                Assert.That(city.DayNight.CurrentSample.NightFactor, Is.EqualTo(1f));
                Capture("ridge-midnight");
                GraphicsEffectsSettings.CleanVolumeEnabled = false;
                Capture("no-air");
                GraphicsEffectsSettings.CleanVolumeEnabled = true;
                CheckScatterContinuity();

                GameSessionState.AdvanceGameTime((float)((12 * 60 - GameSessionState.GameTimeOfDayMinutes) /
                    GameTimeState.GameMinutesPerRealSecond));
                city.DayNight.ApplyCurrentTime(true);
                yield return null;
                Capture("ridge-noon");

                void Capture(string shot) => AreaCaptureFixture.CaptureCurrentCamera(camera, folder, shot, width, height);

                void CheckScatterContinuity()
                {
                    var texture = new Texture2D(2, 2, TextureFormat.RGB24, false);
                    try
                    {
                        Color32[] lit = Read("ridge-midnight");
                        Color32[] unlit = Read("no-air");
                        // Compare the air in front of the distant silhouettes with
                        // the open haze just to their right in the same rows.
                        var silhouettes = new RectInt(747, 245, 21, 88);
                        int darkened = 0;
                        float openHaze = 0;
                        for (int row = silhouettes.yMin; row < silhouettes.yMax; row++)
                        {
                            float reference = 0;
                            for (int x = 770; x < 790; x++) reference += Scatter(x, row);
                            reference /= 20;
                            openHaze += reference;
                            for (int x = silhouettes.xMin; x < silhouettes.xMax; x++)
                                if (Scatter(x, row) < .75f * reference) darkened++;
                        }

                        Assert.That(openHaze / silhouettes.height, Is.GreaterThan(10f),
                            "The foreground lamp must still light the haze.");
                        Assert.That(darkened, Is.LessThanOrEqualTo(silhouettes.width * silhouettes.height / 20),
                            "Distant geometry must not punch dark gaps into the lit air in front of it.");

                        Color32[] Read(string shot)
                        {
                            Assert.That(texture.LoadImage(File.ReadAllBytes(
                                Path.Combine("Captures", folder, shot + ".png"))), Is.True);
                            return texture.GetPixels32();
                        }

                        int Scatter(int x, int row)
                        {
                            int index = (height - 1 - row) * width + x;
                            return lit[index].g - unlit[index].g;
                        }
                    }
                    finally { Object.DestroyImmediate(texture); }
                }
            }
            finally
            {
                GraphicsEffectsSettings.DepthOfFieldEnabled = previousDof;
                GraphicsEffectsSettings.CleanAntialiasingEnabled = previousAa;
                GraphicsEffectsSettings.CleanVolumeEnabled = previousVolume;
            }
        }

        [UnityTest, Explicit("Compare the fogged Ridge roofs with the former and current street focus on a graphics device.")]
        public IEnumerator CityFogOutlineComparison()
        {
            const string folder = "CityFogOutline";
            var hero = new Vector3(-.9f, 31.6f, -307.9f);
            const float yaw = 99f;
            const float pitch = -10f;
            bool previousDof = GraphicsEffectsSettings.DepthOfFieldEnabled;
            bool previousAa = GraphicsEffectsSettings.CleanAntialiasingEnabled;
            try
            {
                GraphicsEffectsSettings.DepthOfFieldEnabled = true;
                GraphicsEffectsSettings.CleanAntialiasingEnabled = true;
                Assert.That(GameSessionState.TryStartGameTimeAt(12 * 60), Is.True);
                yield return LoadProductionCity();
                Object.FindAnyObjectByType<GameTimeRuntime>().enabled = false;
                city.Weather.enabled = false;
                city.Player.Motor.SetInputEnabled(false);
                Camera camera = city.Camera;
                camera.GetComponent<PlayerCameraFollow>().enabled = false;
                city.Player.Motor.Teleport(hero);
                SetFogView(camera, hero, yaw, pitch);
                Physics.SyncTransforms();
                city.Night.Atmosphere.RefreshImmediate();
                yield return null;
                yield return null;
                Time.timeScale = 0f;

                DepthOfField depth = city.DayNight.SceneDepthOfField;
                AssertStreetProfile();

                var focus = new Vector2(depth.gaussianStart.value, depth.gaussianEnd.value);
                float heightWeight = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(5f, 40f, hero.y));
                var formerFocus = Vector2.Lerp(focus, new Vector2(35f, 90f), heightWeight);
                CapturePair("ridge", 1440, 720);
                depth.mode.Override(DepthOfFieldMode.Off);
                Capture("ridge-without-dof");
                depth.mode.Override(DepthOfFieldMode.Gaussian);
                CheckRoofContrast();

                for (int i = 0; i < 13; i++)
                {
                    float progress = i / 12f;
                    Vector3 position = hero + Vector3.right * (3f * progress);
                    city.Player.Motor.Teleport(position);
                    SetFogView(camera, position, yaw - 14f * progress, pitch + 4f * progress);
                    yield return null;
                    CapturePair($"motion-{i:D2}", 960, 480);
                }

                var quay = city.Layout.River.Promenades[0];
                var lowland = new Vector3(quay.Bounds.center.x, (quay.NorthY + quay.SouthY) * .5f,
                    quay.Bounds.center.y);
                city.Player.Motor.Teleport(lowland + Vector3.up * PlayerFactory.GroundedRootOffset);
                foreach (Renderer renderer in city.Player.GameObject.GetComponentsInChildren<Renderer>())
                    renderer.enabled = false;
                camera.transform.SetPositionAndRotation(lowland + Vector3.up * 1.72f,
                    Quaternion.LookRotation(new Vector3(.64f, -.09f, .77f)));
                city.Night.Atmosphere.RefreshImmediate();
                yield return null;
                AssertStreetProfile();
                Capture("quay-noon");
                GameSessionState.AdvanceGameTime((float)((1440d - GameSessionState.GameTimeOfDayMinutes) /
                    GameTimeState.GameMinutesPerRealSecond));
                city.DayNight.ApplyCurrentTime(true);
                yield return null;
                Capture("quay-midnight");
                Debug.Log("CityFogOutlineComparison completed.");

                void Capture(string shot, int width = 1440, int height = 720) =>
                    AreaCaptureFixture.CaptureCurrentCamera(camera, folder, shot, width, height);

                void CapturePair(string shot, int width, int height)
                {
                    Capture(shot + "-after", width, height);
                    depth.gaussianStart.Override(formerFocus.x);
                    depth.gaussianEnd.Override(formerFocus.y);
                    try { Capture(shot + "-before", width, height); }
                    finally
                    {
                        depth.gaussianStart.Override(focus.x);
                        depth.gaussianEnd.Override(focus.y);
                    }
                }

                void AssertStreetProfile()
                {
                    Assert.That(RenderSettings.fogMode, Is.EqualTo(FogMode.ExponentialSquared));
                    Assert.That(RenderSettings.fogDensity, Is.EqualTo(.090f).Within(.0001f));
                    Assert.That(camera.farClipPlane, Is.EqualTo(48f).Within(.01f));
                    Assert.That(depth.active, Is.True);
                    Assert.That(depth.mode.value, Is.EqualTo(DepthOfFieldMode.Gaussian));
                    Assert.That(depth.gaussianStart.value, Is.EqualTo(8f));
                    Assert.That(depth.gaussianEnd.value, Is.EqualTo(28f));
                }

                void CheckRoofContrast()
                {
                    var texture = new Texture2D(2, 2, TextureFormat.RGB24, false);
                    try
                    {
                        Color32[] unblurred = Read("ridge-without-dof");
                        int before = CountDarkened("ridge-before");
                        int after = CountDarkened("ridge-after");
                        Debug.Log($"Fogged roof pixels darkened by DoF: {before} before, {after} after.");
                        Assert.That(before, Is.GreaterThan(0), "The former range must reproduce the roof fringe.");
                        Assert.That(after, Is.LessThan(before / 2f), "The roof fringe must recede.");

                        Color32[] Read(string shot)
                        {
                            Assert.That(texture.LoadImage(File.ReadAllBytes(
                                Path.Combine("Captures", folder, shot + ".png"))), Is.True);
                            return texture.GetPixels32();
                        }

                        int CountDarkened(string shot)
                        {
                            Color32[] pixels = Read(shot);
                            int darkened = 0;
                            // The left roof, above its windows and clear of the hero.
                            for (int row = 271; row < 300; row++)
                            for (int x = 94; x < 398; x++)
                            {
                                int index = (texture.height - 1 - row) * texture.width + x;
                                if (unblurred[index].g - pixels[index].g >= 5) darkened++;
                            }
                            return darkened;
                        }
                    }
                    finally { Object.DestroyImmediate(texture); }
                }
            }
            finally
            {
                GraphicsEffectsSettings.DepthOfFieldEnabled = previousDof;
                GraphicsEffectsSettings.CleanAntialiasingEnabled = previousAa;
            }
        }

        private static void SetFogView(Camera camera, Vector3 position, float heading, float tilt)
        {
            Quaternion rotation = Quaternion.Euler(tilt, heading, 0f);
            Vector3 offset = rotation * new Vector3(.5f, 0f, -1.9f);
            camera.fieldOfView = 53f;
            camera.transform.SetPositionAndRotation(position + Vector3.up * 1.6f + offset, rotation);
        }
    }
}

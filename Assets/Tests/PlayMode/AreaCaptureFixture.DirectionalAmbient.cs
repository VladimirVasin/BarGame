using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace BarPromenade.Tests.PlayMode
{
    public sealed partial class AreaCaptureFixture
    {
        [UnityTest]
        [Explicit("Compare the same gameplay views, then cross both sides of the Home balcony threshold.")]
        public IEnumerator DirectionalAmbientWorld()
        {
            const string preferenceKey = "graphics.directional_ambient";
            bool hadPreference = PlayerPrefs.HasKey(preferenceKey);
            bool previous = GraphicsEffectsSettings.DirectionalAmbientEnabled;
            GameSessionState.BeginNewGame("ambient-world");
            GameSessionState.TryStartGameTimeAt(720);
            try
            {
                yield return SceneManager.LoadSceneAsync(SceneIds.City);
                CityGameRoot city = null;
                yield return WaitFor(() => (city = Object.FindAnyObjectByType<CityGameRoot>()) && city.IsInitialized, "City");
                yield return CompareAmbientView("city", true);

                GameSessionState.EnterBar("ambient-world", BarActivityKind.SplitTheG, CityDistrictKind.Residential);
                yield return SceneManager.LoadSceneAsync(SceneIds.BarInterior);
                BarInteriorRoot bar = null;
                yield return WaitFor(() => (bar = Object.FindAnyObjectByType<BarInteriorRoot>()) && bar.IsInitialized, "Bar");
                bar.ArrivalPresentation?.Skip();
                yield return CompareAmbientView("bar", false);

                yield return SceneManager.LoadSceneAsync(SceneIds.MountainRoad);
                yield return WaitFor(() => Object.FindAnyObjectByType<MountainRoadRoot>() is { IsInitialized: true }, "Road");
                yield return CompareAmbientView("road", true);

                yield return SceneManager.LoadSceneAsync(SceneIds.AlpineVillage);
                yield return WaitFor(() => Object.FindAnyObjectByType<AlpineVillageRoot>() is { IsInitialized: true }, "Village");
                yield return CompareAmbientView("village", true);

                GameSessionState.PrepareHomeArrival(HomeArrivalKind.Normal);
                GameSessionState.EnterHome();
                yield return SceneManager.LoadSceneAsync(SceneIds.HomeInterior);
                HomeInteriorRoot home = null;
                yield return WaitFor(() => (home = Object.FindAnyObjectByType<HomeInteriorRoot>()) && home.IsInitialized, "Home");
                yield return CompareAmbientView("home", false);
                Vector3 inside = home.Player.GameObject.transform.position;
                var center = home.BalconyLayout.BalconyCameraActivationBounds.center;
                home.Player.Motor.Teleport(new Vector3(center.x, home.Layout.PlayerSpawn.y, center.y));
                yield return WaitFor(() => home.ExteriorAtmosphere.IsBalconyVisibilityActive, "Enter balcony");
                yield return CompareAmbientView("balcony", true);
                GraphicsEffectsSettings.DirectionalAmbientEnabled = false;
                home.Player.Motor.Teleport(inside);
                yield return WaitFor(() => !home.ExteriorAtmosphere.IsBalconyVisibilityActive, "Return indoors");
                Assert.That(RenderSettings.ambientMode, Is.EqualTo(AmbientMode.Flat));
                yield return CompareAmbientView("home-return", false);
            }
            finally
            {
                GraphicsEffectsSettings.DirectionalAmbientEnabled = previous;
                if (!hadPreference)
                {
                    PlayerPrefs.DeleteKey(preferenceKey);
                    PlayerPrefs.Save();
                }
                GameSessionState.BeginNewGame();
            }
        }

        private static IEnumerator CompareAmbientView(string label, bool exterior)
        {
            Camera camera = Camera.main;
            Assert.That(camera, Is.Not.Null);
            for (int i = 0; i < SettleFrames; i++) yield return null;
            float scale = Time.timeScale;
            var timing = new FrameTiming[1];
            var eye = camera.transform.position;
            var rotation = camera.transform.rotation;
            try
            {
                Time.timeScale = 0f;
                for (int phase = 0; phase < 2; phase++)
                {
                    bool gradient = phase == 1;
                    GraphicsEffectsSettings.DirectionalAmbientEnabled = gradient;
                    Assert.That(RenderSettings.ambientMode, Is.EqualTo(gradient ? AmbientMode.Trilight : AmbientMode.Flat));
                    if (gradient)
                    {
                        float reference = RenderSettings.ambientEquatorColor.r;
                        Assert.That(reference, Is.GreaterThan(0f));
                        float spread = exterior ? .27f : .16f;
                        Assert.That(RenderSettings.ambientSkyColor.r / reference, Is.EqualTo(1f + spread).Within(.001f));
                        Assert.That(RenderSettings.ambientGroundColor.r / reference, Is.EqualTo(1f - spread).Within(.001f));
                    }

                    double cpu = 0d, gpu = 0d;
                    int cpuCount = 0, gpuCount = 0;
                    ulong lastTimestamp = 0;
                    for (int frame = 0; frame < 28; frame++)
                    {
                        FrameTimingManager.CaptureFrameTimings();
                        yield return null;
                        if (frame < 10 || FrameTimingManager.GetLatestTimings(1, timing) == 0 ||
                            timing[0].frameStartTimestamp == lastTimestamp) continue;
                        lastTimestamp = timing[0].frameStartTimestamp;
                        if (timing[0].cpuFrameTime > 0) { cpu += timing[0].cpuFrameTime; cpuCount++; }
                        if (timing[0].gpuFrameTime > 0) { gpu += timing[0].gpuFrameTime; gpuCount++; }
                    }
                    string state = gradient ? "gradient" : "flat";
                    string Mean(double sum, int count) => count > 0 ? (sum / count).ToString("F2") : "unavailable";
                    Debug.Log($"Ambient timing {label} {state}: CPU {Mean(cpu, cpuCount)}, GPU {Mean(gpu, gpuCount)} ms");
                    var liveEye = camera.transform.position;
                    var liveRotation = camera.transform.rotation;
                    try
                    {
                        camera.transform.SetPositionAndRotation(eye, rotation);
                        CaptureCurrentCamera(camera, "DirectionalAmbient", label + "-" + state);
                    }
                    finally { camera.transform.SetPositionAndRotation(liveEye, liveRotation); }
                }
            }
            finally { Time.timeScale = scale; }
        }
    }
}

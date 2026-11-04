using System.Collections;
using System.IO;
using System.Linq;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

namespace BarPromenade.Tests.PlayMode
{
    public sealed partial class CityTerracePilotPlayModeTests
    {
        [UnityTest, Explicit("Photograph City altitude and the night street on a graphics device.")]
        public IEnumerator CityAltitudeAndNightStreet()
        {
            bool previousDof = GraphicsEffectsSettings.DepthOfFieldEnabled;
            try
            {
                GraphicsEffectsSettings.DepthOfFieldEnabled = true;
                yield return PhotographAltitudeAndNight();
            }
            finally { GraphicsEffectsSettings.DepthOfFieldEnabled = previousDof; }
        }

        private IEnumerator PhotographAltitudeAndNight()
        {
            Assert.That(GameSessionState.TryStartGameTimeAt(12 * 60), Is.True);
            yield return LoadProductionCity();
            Object.FindAnyObjectByType<GameTimeRuntime>().enabled = false;
            city.Weather.enabled = false;
            city.Player.Motor.SetInputEnabled(false);
            city.Camera.GetComponent<PlayerCameraFollow>().enabled = false;
            foreach (Renderer renderer in city.Player.GameObject.GetComponentsInChildren<Renderer>())
                renderer.enabled = false;

            var quay = city.Layout.River.Promenades[0];
            var low = new Vector3(quay.Bounds.center.x, (quay.NorthY + quay.SouthY) * .5f, quay.Bounds.center.y);
            var bridges = CityHillsideQuarterPlan.ForLayout(city.Layout).Bridges;
            var middle = BridgeFoot(bridges.Single(b => b.Id == "court"));
            var crest = BridgeFoot(bridges.Single(b => b.Id == "summit"));
            const string folder = "CityAltitudeAndNight";
            Vector3 downhill = new Vector3(.64f, -.09f, .77f);
            yield return Observe(low, downhill, "01-quay");
            float lowDensity = RenderSettings.fogDensity, lowClip = city.Camera.farClipPlane;
            Assert.That(lowDensity, Is.EqualTo(.090f).Within(.0001f));
            Assert.That(lowClip, Is.EqualTo(48f).Within(.01f));
            Assert.That(city.Night.FogField.LayerOpacityMultiplier, Is.EqualTo(1f).Within(.01f));
            yield return Observe(middle, downhill, "02-court");
            float midDensity = RenderSettings.fogDensity, midClip = city.Camera.farClipPlane;
            yield return Observe(crest, downhill, "03-crest");
            Assert.That(RenderSettings.fogDensity, Is.EqualTo(.025f).Within(.0005f));
            Assert.That(city.Camera.farClipPlane, Is.EqualTo(110f).Within(.1f));
            Assert.That(city.Night.FogField.LayerOpacityMultiplier, Is.LessThan(.25f));
            Assert.That(midDensity, Is.GreaterThan(RenderSettings.fogDensity).And.LessThan(lowDensity));
            Assert.That(midClip, Is.LessThan(city.Camera.farClipPlane).And.GreaterThan(lowClip));
            yield return Observe(middle, downhill, null);
            Assert.That(RenderSettings.fogDensity, Is.EqualTo(midDensity).Within(.0001f));
            yield return Observe(low, downhill, null);
            Assert.That(city.Camera.farClipPlane, Is.EqualTo(lowClip).Within(.001f));

            // Choose a straight ordinary low-city block. Both ground samples are on
            // the same strip of road, with the foreground sample outside the lamp pool.
            var lamps = city.Night.Plan.StreetLamps;
            var lamp = lamps.Where(l => l.Position.y < 3f && l.Edge.A != l.Edge.B &&
                    !city.Layout.RoadGeometry.IsCurved(l.Edge))
                .OrderBy(l => Vector3.SqrMagnitude(l.Position - low)).First();
            Vector3 along = Vector3.Cross(Vector3.up, lamp.Forward);
            Vector3 lit = lamp.Position + lamp.Forward * 5.2f;
            Vector3 observer = lit - along * 14f + lamp.Forward * 1.2f;
            Vector3 direction = (lit + Vector3.up * 1.25f) - (observer + Vector3.up * 1.72f);
            yield return Observe(observer, direction, "04-street-noon");
            float dayDensity = RenderSettings.fogDensity;
            Color dayHaze = RenderSettings.fogColor;
            GameSessionState.AdvanceGameTime((float)((1440d - GameSessionState.GameTimeOfDayMinutes) /
                GameTimeState.GameMinutesPerRealSecond));
            city.DayNight.ApplyCurrentTime(true);
            yield return Observe(observer, direction, "05-street-midnight");
            Assert.That(RenderSettings.fogDensity, Is.EqualTo(dayDensity).Within(.0001f));
            Assert.That(RenderSettings.fogColor.grayscale, Is.LessThan(dayHaze.grayscale));
            Assert.That(city.Camera.backgroundColor, Is.EqualTo(RenderSettings.fogColor));
            Assert.That(city.Night.Atmosphere.StreetLightPool.Any(light => light.enabled &&
                (light.transform.position - lamp.Position).sqrMagnitude < 30f), Is.True);

            // Same clock, view, real fixtures, wetness and grade. Isolate the
            // change to the sky contribution using the original shared light sample.
            try
            {
                RuntimeSceneSetup.ApplyCityExteriorLighting(GameTimeDayNightRules.Evaluate(0d), false);
                CityDayNightController.SetSkyFill(city.Camera, city.Clouds, 0f);
                AreaCaptureFixture.CaptureCurrentCamera(city.Camera, folder, "06-street-previous-fill");
            }
            finally { city.DayNight.ApplyCurrentTime(true); }

            // Two asphalt rectangles in this fixed, 1280 x 720 street frame.
            // Both avoid the centre line, curb and any halo around the lens.
            var oldRoad = SampleStreet("06-street-previous-fill");
            var nightRoad = SampleStreet("05-street-midnight");
            Assert.That(oldRoad.shadow / nightRoad.shadow, Is.InRange(2.5f, 5f));
            Assert.That(nightRoad.spill / nightRoad.shadow, Is.GreaterThan(1.6f));

            (float shadow, float spill) SampleStreet(string name)
            {
                var frame = new Texture2D(2, 2, TextureFormat.RGB24, false);
                try
                {
                    Assert.That(frame.LoadImage(File.ReadAllBytes(Path.Combine("Captures", folder, name + ".png"))), Is.True);
                    var pixels = frame.GetPixels32();
                    return (MeanLuminance(395, 515), MeanLuminance(1000, 1070));

                    float MeanLuminance(int xMin, int xMax)
                    {
                        float sum = 0f;
                        int count = 0;
                        for (int row = 610; row < 670; row += 4)
                        for (int x = xMin; x < xMax; x += 4)
                        {
                            Color linear = ((Color)pixels[(frame.height - 1 - row) * frame.width + x]).linear;
                            sum += .2126f * linear.r + .7152f * linear.g + .0722f * linear.b;
                            count++;
                        }
                        return sum / count;
                    }
                }
                finally { Object.DestroyImmediate(frame); }
            }

            IEnumerator Observe(Vector3 foot, Vector3 look, string name)
            {
                city.Player.Motor.Teleport(foot + Vector3.up * PlayerFactory.GroundedRootOffset);
                city.Night.Atmosphere.RefreshImmediate();
                var eye = foot + Vector3.up * 1.72f;
                city.Camera.transform.SetPositionAndRotation(eye, Quaternion.LookRotation(look));
                Physics.SyncTransforms();
                yield return null;
                yield return null;
                if (name == null) yield break;
                Debug.Log($"City atmosphere {name}: Y={foot.y:F2}, density={RenderSettings.fogDensity:F4}, " +
                    $"clip={city.Camera.farClipPlane:F1}, fog={RenderSettings.fogColor}.");
                AreaCaptureFixture.CaptureCurrentCamera(city.Camera, folder, name);
            }
        }

        private static Vector3 BridgeFoot(CityHillsideQuarterBridge bridge)
        {
            Vector3 foot = Vector3.Lerp(bridge.WestBearingWorld, bridge.EastBearingWorld, .22f);
            foot.z = bridge.DeckBounds.yMax - .85f;
            Assert.That(bridge.Link.TrySampleTop(new Vector2(foot.x, foot.z), out foot.y, out _), Is.True);
            return foot;
        }
    }
}

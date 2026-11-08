using System;
using System.Collections;
using System.Linq;
using System.IO;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

namespace BarPromenade.Tests.PlayMode
{
    public sealed partial class AreaCaptureFixture
    {
        [UnityTest]
        [Explicit("Glass return at the eastern hairpin: both sidewalks, window sorting, local light and rain cover.")]
        public IEnumerator CityGlassReturn()
        {
            GameSessionState.BeginNewGame();
            Assert.That(GameSessionState.TryStartGameTimeAt(12 * 60), Is.True);
#if UNITY_EDITOR
            if (Environment.GetEnvironmentVariable("BAR_EXPORT_GLASS_MAP") == "1")
                Type.GetType("BarPromenade.Editor.CityMapArtReferenceExport, BarPromenade.Editor", true)
                    .GetMethod("ExportCurrent", Type.EmptyTypes).Invoke(null, null);
#endif
            CityGameRoot city = null;
            CityGlassReturnWorker worker = null;
            CityGlassReturnSitePlan plan = null;
            PlayerCameraFollow follow = null;
            bool followEnabled = false, weatherEnabled = false, motorEnabled = false;
            Vector3 lower = default, upper = default;
            var screenshot = new Vector3(165.8f, 36f, -335.6f);
            Light floodlight = null;
            try
            {
                yield return Capture(SceneIds.City, () =>
                {
                    city = Object.FindAnyObjectByType<CityGameRoot>();
                    return city != null && city.IsInitialized && !CompositionDriver.IsComposing ? city : null;
                }, () =>
                {
                    var workers = Object.FindObjectsByType<CityGlassReturnWorker>();
                    Assert.That(workers.Length, Is.EqualTo(1));
                    worker = workers[0];
                    plan = CityGlassReturnSitePlan.ForLayout(city.Layout);
                    Assert.That(plan.IsEnabled, Is.True);
                    follow = city.Camera.GetComponent<PlayerCameraFollow>();
                    followEnabled = follow.enabled; follow.enabled = false;
                    weatherEnabled = city.Weather.enabled; city.Weather.enabled = false;
                    motorEnabled = city.Player.Motor.InputEnabled; city.Player.Motor.SetInputEnabled(false);
                    worker.AutoAdvance = false;
                    floodlight = city.Night.Atmosphere.StreetLightPool.Single(l => l &&
                        Vector3.Distance(l.transform.position, plan.FloodlightWallContact) < .85f);

                    var road = CityHillsideStreetPlan.ForLayout(city.Layout).Routes.Single(r => r.Bank == CityTerraceTransportBank.East);
                    lower = Branch(5, -4f);
                    upper = Branch(6, 4f);
                    return new[] {
                        Frame("glass-return-lower-approach", lower + Vector3.up * 1.6f, plan.CounterWorld, 58f, 12, 1.4),
                        Frame("glass-return-upper-approach", upper + Vector3.up * 1.6f, plan.YardOrigin + Vector3.up, 62f, 12, 5),
                        Frame("glass-return-window-contact", plan.HeroDock + plan.Rotation * new Vector3(.26f, 1.56f, 1.1f),
                            plan.CounterWorld, 47f, 12, 2.8),
                        Frame("glass-return-hud-night-rain", screenshot + Vector3.up * 1.58f,
                            (plan.CounterWorld + plan.YardOrigin + Vector3.up) * .5f, 64f, 21, 2.8),
                        Frame("glass-return-floodlight-off", new Vector3(166.9f, 37.78f, -340.8f),
                            (plan.CounterWorld + plan.YardOrigin + Vector3.up) * .5f, 64f, 21, 2.8, false),
                        Frame("glass-return-floodlight-on", new Vector3(166.9f, 37.78f, -340.8f),
                            (plan.CounterWorld + plan.YardOrigin + Vector3.up) * .5f, 64f, 21, 2.8),
                        Frame("glass-return-floodlight-mount", plan.Approach + new Vector3(0f, 3.8f, -2f),
                            plan.FloodlightWallContact, 55f, 21, 2.8)
                    };

                    Vector3 Branch(int lane, float inside)
                    {
                        var p = new Vector2(road.LaneMaxX - 3f, road.GetLaneZ(lane) + inside);
                        Assert.That(road.TrySampleTop(p, out float top, out var kind), Is.True);
                        Assert.That(kind, Is.EqualTo(CityStreetSupportKind.Sidewalk));
                        return new Vector3(p.x, top, p.y);
                    }
                    Shot Frame(string name, Vector3 eye, Vector3 target, float fov, int hour, double phase, bool floodOn = true)
                    {
                        Shot pose = Shot.At(name, eye, target, fov);
                        int frames = 0;
                        return Shot.At(name, eye, target, fov, readyWhen: () =>
                        {
                            if (frames++ == 0)
                            {
                                SetChurchRemodelTime(hour * 60);
                                city.DayNight.ApplyCurrentTime(true);
                                float rain = hour == 21 ? .8f : CityEternalRainShaper.FloorIntensity(0f);
                                city.Rain.SetIntensity(rain);
                                CityWaterResources.SetRainIntensity(rain);
                                CityWetSurfaceRegistry.SetImmediate(rain);
                                MoveCityLightingObserver(city, pose);
                            }
                            worker.ApplyAt(phase);
                            floodlight.enabled = floodOn;
                            return frames > SettleFrames;
                        });
                    }
                });

                Assert.That(city.World.WalkableArea.Contains(plan.Origin, .3f), Is.False, "Pavilion walls block movement.");
                Assert.That(city.World.WalkableArea.Contains(plan.YardOrigin, .3f), Is.False, "The fenced storage yard is private.");
                foreach (Vector3 foot in new[] { lower, upper, screenshot, plan.Approach, plan.HeroDock })
                    Assert.That(city.World.WalkableArea.Contains(foot, .30f), Is.True, "Public foot clearance at " + foot);
                var eastern = CityHillsideStreetPlan.ForLayout(city.Layout).Routes.Single(r => r.Bank == CityTerraceTransportBank.East);
                var inner = new[] { eastern.SidewalkPath, eastern.OppositeSidewalkPath }.OrderBy(path =>
                    path.Min(point => (point - lower).sqrMagnitude)).First();
                int Nearest(Vector3 target) => Enumerable.Range(0, inner.Count).OrderBy(i => (inner[i] - target).sqrMagnitude).First();
                int first = Nearest(lower), last = Nearest(upper);
                Assert.That(last, Is.GreaterThan(first));
                Vector3 apex = inner[first];
                for (int i = first; i < last; i++)
                {
                    CheckLeg(inner[i], inner[i + 1]);
                    if (inner[i].x > apex.x) apex = inner[i];
                }
                CheckLeg(apex, plan.Approach);
                CheckLeg(plan.Approach, plan.HeroDock);
                void CheckLeg(Vector3 a, Vector3 b)
                {
                    int steps = Mathf.Max(1, Mathf.CeilToInt(Vector3.Distance(a, b) / .4f));
                    for (int j = 0; j <= steps; j++)
                    {
                        Vector3 foot = Vector3.Lerp(a, b, (float)j / steps);
                        Assert.That(city.World.WalkableArea.Contains(foot, .30f), Is.True,
                            "The inner footway and the apron reach the window with capsule clearance at " + foot);
                    }
                }
                Assert.That(new CityMapCityTeleportGround(city.Layout).TryClampArrival(plan.Approach, out var arrival), Is.True);
                Assert.That(Vector3.Distance(plan.Approach, arrival), Is.LessThan(.35f), "Map arrival stays on the surveyed terrace.");
                Physics.SyncTransforms();
                Assert.That(city.Rain.LocalShelters.Any(shelter => shelter != null &&
                    shelter.Raycast(new Ray(plan.HeroDock + Vector3.up * 6f, Vector3.down), out var hit, 5f) &&
                    hit.normal.y > .9f), Is.True, "The rain system covers the customer under the actual canopy.");
                Assert.That(worker.Worker.GetComponent<DefaultNpcAppearance>().AppearanceKey, Is.EqualTo(DefaultNpcPopulation.CityGlassReturnWorker));
                worker.ApplyAt(1.4);
                Vector3 inHand = worker.Bottle.transform.position;
                Assert.That(Vector3.Distance(worker.Worker.LeftGrip.position, inHand + Vector3.up * .19f), Is.LessThan(.065f));
                worker.ApplyAt(2.8);
                Assert.That(worker.HandContactError, Is.LessThan(.065f), "The sorting hand reaches the actual bottle at the crate.");
                Vector3 placed = worker.Bottle.transform.position;
                Assert.That(Vector3.Distance(worker.Worker.RightGrip.position, placed + Vector3.up * .12f), Is.LessThan(.065f));
                Transform Part(string key) => CityGlassReturnAssetProvider.FindPart(worker.transform, key);
                Assert.That(Vector3.Distance(placed, Part("ANCHOR_CrateSlot").position), Is.LessThan(.01f));
                Renderer glass = worker.Bottle.GetComponentInChildren<MeshRenderer>();
                Assert.That(glass.bounds.size.y, Is.InRange(.26f, .29f), "The imported bottle remains full scale on the hand.");
                Assert.That(Vector3.Distance(placed, inHand), Is.GreaterThan(.06f), "The bottle crosses the work surface.");
                worker.ApplyAt(5);
                Assert.That(Vector3.Distance(placed, worker.Bottle.transform.position), Is.LessThan(.035f), "At rest glass stays in its bay.");

                city.Player.Motor.Teleport(plan.Approach + Vector3.up * PlayerFactory.GroundedRootOffset);
                city.Night.Atmosphere.RefreshImmediate();
                Vector3 lightSource = Part("ANCHOR_WorkLight").position;
                Light windowLight = city.Night.Atmosphere.StreetLightPool.FirstOrDefault(l => l != null && l.enabled &&
                    l.type == LightType.Spot && Vector3.Distance(l.transform.position, lightSource) < .025f);
                Assert.That(windowLight, Is.Not.Null, "The visible window receives a local City light lease.");
                Assert.That(windowLight.shadows, Is.EqualTo(LightShadows.None), "The new fitting does not consume shadow-atlas slots.");
                Assert.That(windowLight.intensity, Is.GreaterThan(0f));
                Assert.That(windowLight.range, Is.LessThanOrEqualTo(4.25f));
                Assert.That(worker.GetComponentsInChildren<AudioSource>(true).Any(s => s.spatialBlend > .95f && !s.loop &&
                    Vector3.Distance(s.transform.position, placed) < .025f), Is.True, "The clink originates at the crate where glass touches.");
                Assert.That(floodlight.type, Is.EqualTo(LightType.Spot));
                Assert.That(floodlight.shadows, Is.EqualTo(LightShadows.Soft));
                Assert.That(floodlight.spotAngle, Is.GreaterThanOrEqualTo(130f), "A flood covers the whole parcel without a narrow spot.");
                Assert.That(floodlight.shadowStrength, Is.LessThanOrEqualTo(.7f));
                Assert.That(floodlight.cookie, Is.Not.Null, "The reflector keeps the broad spill off its own mounting wall.");
                Assert.That(plan.FloodlightWallContact.y - plan.Origin.y, Is.GreaterThan(5f));
                foreach (Vector2 point in plan.Footprint.Concat(plan.YardFootprint))
                {
                    Vector3 delta = new Vector3(point.x, plan.Origin.y + 1f, point.y) - floodlight.transform.position;
                    Assert.That(delta.magnitude, Is.LessThan(floodlight.range));
                    Assert.That(Vector3.Angle(floodlight.transform.forward, delta),
                        Is.LessThan(floodlight.innerSpotAngle * .5f), "Every part lies inside the broad, even core.");
                }
                float night = floodlight.intensity;
                SetChurchRemodelTime(12 * 60); city.DayNight.ApplyCurrentTime(true);
                Assert.That(floodlight.intensity, Is.GreaterThanOrEqualTo(night * 2f / 3f - .01f));
                AssertGlassFloodlightPixels();
            }
            finally
            {
                if (worker != null) worker.AutoAdvance = true;
                if (follow != null) follow.enabled = followEnabled;
                if (city != null && follow != null)
                {
                    city.Weather.enabled = weatherEnabled;
                    city.Player.Motor.SetInputEnabled(motorEnabled);
                }
            }
        }

        private static void AssertGlassFloodlightPixels()
        {
            string folder = Path.Combine(Directory.GetCurrentDirectory(), "Captures", SceneIds.City);
            var off = new Texture2D(2, 2, TextureFormat.RGB24, false);
            var on = new Texture2D(2, 2, TextureFormat.RGB24, false);
            try
            {
                Assert.That(off.LoadImage(File.ReadAllBytes(Path.Combine(folder, "glass-return-floodlight-off.png"))), Is.True);
                Assert.That(on.LoadImage(File.ReadAllBytes(Path.Combine(folder, "glass-return-floodlight-on.png"))), Is.True);
                Color32[] a = off.GetPixels32(), b = on.GetPixels32();
                float delta = 0f; int lit = 0;
                for (int i = 0; i < a.Length; i++)
                {
                    float change = ((b[i].r - a[i].r) * .2126f + (b[i].g - a[i].g) * .7152f + (b[i].b - a[i].b) * .0722f) / 255f;
                    delta += Mathf.Max(0f, change);
                    if (change > .035f) lit++;
                }
                Debug.Log($"GlassReturn floodlight: mean visible lift={delta / a.Length:F4}, broad lit fraction={(float)lit / a.Length:F3}");
                Assert.That(delta / a.Length, Is.GreaterThan(.015f), "The light must make the night scene visibly readable.");
                Assert.That((float)lit / a.Length, Is.GreaterThan(.10f), "The light must brighten a broad part of the site.");
            }
            finally { Object.DestroyImmediate(off); Object.DestroyImmediate(on); }
        }
    }
}

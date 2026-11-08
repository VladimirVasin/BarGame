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
                        Frame("glass-return-floodlight-mount-off", plan.Approach + new Vector3(0f, 3.8f, -2f),
                            plan.FloodlightWallContact, 55f, 21, 2.8, false),
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
                            worker.ApplyAt(hour >= CityGlassReturnWorker.ShiftEndMinute / 60 ?
                                hour * 60d / GameTimeState.GameMinutesPerRealSecond : GlassSortingMoment(phase));
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
                worker.ApplyAt(GlassSortingMoment(1.4));
                Vector3 inHand = worker.Bottle.transform.position;
                Assert.That(Vector3.Distance(worker.Worker.LeftGrip.position, inHand + Vector3.up * .19f), Is.LessThan(.065f));
                worker.ApplyAt(GlassSortingMoment(2.8));
                Assert.That(worker.HandContactError, Is.LessThan(.065f), "The sorting hand reaches the actual bottle at the crate.");
                Vector3 placed = worker.Bottle.transform.position;
                Assert.That(Vector3.Distance(worker.Worker.RightGrip.position, placed + Vector3.up * .12f), Is.LessThan(.065f));
                Transform Part(string key) => CityGlassReturnAssetProvider.FindPart(worker.transform, key);
                Assert.That(Vector3.Distance(placed, Part("ANCHOR_CrateSlot").position), Is.LessThan(.01f));
                Renderer glass = worker.Bottle.GetComponentInChildren<MeshRenderer>();
                Assert.That(glass.bounds.size.y, Is.InRange(.26f, .29f), "The imported bottle remains full scale on the hand.");
                Assert.That(Vector3.Distance(placed, inHand), Is.GreaterThan(.06f), "The bottle crosses the work surface.");
                worker.ApplyAt(GlassSortingMoment(5));
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
                Assert.That(floodlight.shadowStrength, Is.EqualTo(1f), "Opaque pavilion walls fully occlude the exterior flood.");
                Assert.That(floodlight.cookie, Is.Not.Null, "The reflector feathers its weak grazing spill across the mounting wall.");
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
                AssertGlassFacadePixels(city.Camera, plan);
                VerifyGlassSlabClearance(city.Layout, plan);
                yield return VerifyGlassReturnCommute(city, worker);
                yield return VerifyGlassReturnOperating(city, worker);
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

        private static double GlassSortingMoment(double phase)
        {
            double opening = CityGlassReturnWorker.ShiftStartMinute / GameTimeState.GameMinutesPerRealSecond;
            double noon = 720d / GameTimeState.GameMinutesPerRealSecond;
            return opening + Math.Floor((noon - opening) / CityGlassReturnWorker.SortingCycleSeconds) *
                CityGlassReturnWorker.SortingCycleSeconds + phase;
        }

        private static void VerifyGlassSlabClearance(CityLayout layout, CityGlassReturnSitePlan plan)
        {
            var ground = CityHillsideGroundPlan.ForLayout(layout);
            for (int x = 0; x <= 12; x++)
            for (int z = 0; z <= 12; z++)
            {
                Vector3 foot = plan.YardOrigin + new Vector3(
                    Mathf.Lerp(-CityGlassReturnSitePlan.YardWidth * .5f, CityGlassReturnSitePlan.YardWidth * .5f, x / 12f),
                    0f, Mathf.Lerp(-CityGlassReturnSitePlan.YardDepth * .5f, CityGlassReturnSitePlan.YardDepth * .5f, z / 12f));
                Assert.That(ground.TrySampleTop(new Vector2(foot.x, foot.z), out float top), Is.True);
                Assert.That(foot.y - top, Is.GreaterThanOrEqualTo(CityGlassReturnSitePlan.YardSlabClearance - .002f),
                    "The concrete and existing terrain must never share a visible surface at " + foot);
            }
        }

        private static void AssertGlassFacadePixels(Camera camera, CityGlassReturnSitePlan plan)
        {
            string folder = Path.Combine(Directory.GetCurrentDirectory(), "Captures", SceneIds.City);
            var off = new Texture2D(2, 2, TextureFormat.RGB24, false);
            var on = new Texture2D(2, 2, TextureFormat.RGB24, false);
            Vector3 savedPosition = camera.transform.position;
            Quaternion savedRotation = camera.transform.rotation;
            float savedFov = camera.fieldOfView;
            try
            {
                Assert.That(off.LoadImage(File.ReadAllBytes(Path.Combine(folder, "glass-return-floodlight-mount-off.png"))), Is.True);
                Assert.That(on.LoadImage(File.ReadAllBytes(Path.Combine(folder, "glass-return-floodlight-mount.png"))), Is.True);
                Vector3 eye = plan.Approach + new Vector3(0f, 3.8f, -2f);
                camera.transform.SetPositionAndRotation(eye, Quaternion.LookRotation(plan.FloodlightWallContact - eye));
                camera.fieldOfView = 55f;
                float lift = 0f, brightness = 0f;
                int count = 0;
                for (int row = 0; row < 6; row++)
                for (int column = 0; column < 3; column++)
                {
                    Vector3 point = plan.FloodlightWallContact + Vector3.down * Mathf.Lerp(1.2f, 4f, row / 5f) +
                        Vector3.forward * Mathf.Lerp(-.6f, .8f, column / 2f);
                    Vector3 uv = camera.WorldToViewportPoint(point);
                    Assert.That(uv.z > 0f && uv.x > 0f && uv.x < 1f && uv.y > 0f && uv.y < 1f, Is.True);
                    int px = Mathf.RoundToInt(uv.x * (on.width - 1)), py = Mathf.RoundToInt(uv.y * (on.height - 1));
                    for (int dx = -2; dx <= 2; dx++)
                    for (int dy = -2; dy <= 2; dy++)
                    {
                        Color a = off.GetPixel(px + dx, py + dy), b = on.GetPixel(px + dx, py + dy);
                        float L(Color c) => c.r * .2126f + c.g * .7152f + c.b * .0722f;
                        lift += Mathf.Max(0f, L(b) - L(a)); brightness += L(b); count++;
                    }
                }
                Debug.Log($"Glass facade light lift {lift / count:F4}, brightness {brightness / count:F4}");
                Assert.That(lift / count, Is.GreaterThan(.003f), "A soft light must remain on the house itself.");
                Assert.That(brightness / count, Is.LessThan(.24f), "The wall must retain its dark masonry rather than become a bright pool.");
            }
            finally
            {
                camera.transform.SetPositionAndRotation(savedPosition, savedRotation); camera.fieldOfView = savedFov;
                Object.DestroyImmediate(off); Object.DestroyImmediate(on);
            }
        }

        private static IEnumerator VerifyGlassReturnCommute(CityGameRoot city, CityGlassReturnWorker worker)
        {
            var plan = worker.CommutePlan;
            var camera = city.Camera;
            Assert.That(plan, Is.Not.Null);
            Assert.That(worker.HomeStableId, Is.EqualTo("ridge-east-house:22:-7"));
            Assert.That(plan.House.Lot.IsPlayerHome, Is.False);
            Debug.Log("Glass sorter home " + worker.HomeStableId + " doorstep " + plan.HomeDoor);
            Vector3 savedHero = city.Player.GameObject.transform.position;
            city.Player.Motor.Teleport(CityGlassReturnSitePlan.ForLayout(city.Layout).Approach +
                new Vector3(2f, PlayerFactory.GroundedRootOffset, -2f));
            worker.PresentationObserver = city.Player.GameObject.transform;
            Physics.SyncTransforms();
            double close = CityGlassReturnWorker.ShiftEndMinute / GameTimeState.GameMinutesPerRealSecond;
            double opening = CityGlassReturnWorker.ShiftStartMinute / GameTimeState.GameMinutesPerRealSecond;
            try
            {
                worker.ApplyAt(GlassSortingMoment(2.8));
                yield return GlassReturnNativeFrame(camera, worker.Worker.Head.position +
                    worker.transform.forward * 1.7f + worker.transform.right * .6f,
                    worker.Bottle.position + Vector3.up * .12f, "glass-return-real-grip");
                Assert.That(worker.HandContactError, Is.LessThan(.065f));
                var yard = CityGlassReturnSitePlan.ForLayout(city.Layout).YardOrigin;
                yield return GlassReturnNativeFrame(camera, yard + new Vector3(3f, 1.4f, -3f),
                    yard + Vector3.up * .08f, "glass-return-concrete-clearance");
                worker.ApplyAt((22 * 60d + 45d) / GameTimeState.GameMinutesPerRealSecond);
                Assert.That(worker.IsWorking, Is.True, "The glass return remains staffed late in the evening, until 23:00.");
                SetChurchRemodelTime(CityGlassReturnWorker.ShiftEndMinute);
                city.DayNight.ApplyCurrentTime(true);
                worker.ApplyAt(close - .05d);
                Assert.That(worker.IsWorking, Is.True);
                worker.AdvanceAt(close + .10d);
                Vector3 held = worker.Worker.transform.position;
                Vector3 direction = (plan.WorkToDoor[1] - plan.WorkToDoor[0]).normalized;
                city.Player.Motor.Teleport(held + direction * .32f + Vector3.up * PlayerFactory.GroundedRootOffset);
                Physics.SyncTransforms();
                worker.AdvanceAt(close + .20d);
                Assert.That(Vector3.Distance(worker.Worker.transform.position, held), Is.LessThan(.001f),
                    "The sorter must wait for a real hero blocking his next step.");
                city.Player.Motor.Teleport(worker.Worker.transform.position + Vector3.right * 100f +
                    Vector3.up * PlayerFactory.GroundedRootOffset);
                Physics.SyncTransforms();
                worker.AdvanceAt(close + .40d);
                Assert.That(Vector3.Distance(worker.Worker.transform.position, held), Is.GreaterThan(.005f),
                    "Offscreen culling must preserve the commute's authoritative motion.");
                Assert.That(worker.Worker.GetComponentsInChildren<Renderer>(true).All(renderer => !renderer.enabled), Is.True);
                city.Player.Motor.Teleport(CityGlassReturnSitePlan.ForLayout(city.Layout).Approach +
                    new Vector3(2f, PlayerFactory.GroundedRootOffset, -2f));
                Physics.SyncTransforms();
                worker.ApplyAt(close - .05d);
                Physics.SyncTransforms();
                VerifyGlassWorkerRoute(worker, city.Player.GameObject.transform, false);
                Vector3 previous = worker.Worker.transform.position;
                bool capturedDoor = false, checkedRoute = false;
                for (int tick = 0; tick <= Mathf.CeilToInt((worker.CommuteSeconds + 2f) / .05f); tick++)
                {
                    double clock = close + tick * .05d;
                    worker.AdvanceAt(clock);
                    Vector3 point = worker.Worker.transform.position;
                    Assert.That(Vector3.Distance(previous, point), Is.LessThan(.16f), "The sorter must physically leave the pavilion.");
                    previous = point;
                    Assert.That(worker.IsWorking, Is.False);
                    if (tick == 8)
                    {
                        using (GameTimeScaleRuntime.AcquirePause()) worker.AdvanceAt(clock + .05d);
                        Assert.That(worker.Worker.transform.position, Is.EqualTo(point));
                    }
                    if (!capturedDoor && worker.IsUsingStaffDoor && worker.StaffDoorOpenness > .4f && worker.StaffDoorOpenness < .7f &&
                        Vector3.Distance(point, plan.DoorCrossing[plan.DoorCrossing.Count - 1]) < .08f)
                    {
                        capturedDoor = true;
                        Assert.That(worker.HandContactError, Is.LessThan(.045f));
                        yield return GlassReturnNativeFrame(camera, point + worker.transform.rotation * new Vector3(1.4f, 1.5f, 1.1f),
                            worker.Worker.Head.position - Vector3.up * .25f, "glass-return-staff-door");
                    }
                    if (!checkedRoute && worker.StaffDoorOpenness > .999f)
                    {
                        Physics.SyncTransforms();
                        VerifyGlassWorkerRoute(worker, city.Player.GameObject.transform, true);
                        checkedRoute = true;
                    }
                }
                Assert.That(capturedDoor && checkedRoute, Is.True);
                Assert.That(worker.IsAtHome, Is.True);
                Assert.That(Vector3.Distance(worker.Worker.transform.position, plan.InsideHome), Is.LessThan(.025f));
                Assert.That(worker.StaffDoorOpenness, Is.LessThan(.001f));
                var support = CityGlassReturnAssetProvider.FindPart(worker.transform, "ANCHOR_BottleRest");
                Assert.That(Vector3.Distance(worker.Bottle.position, support.position), Is.LessThan(.01f));
                foreach (var source in worker.GetComponentsInChildren<AudioSource>()) Assert.That(source.isPlaying, Is.False);
                var site = CityGlassReturnSitePlan.ForLayout(city.Layout);
                yield return GlassReturnNativeFrame(camera, new Vector3(166.5f, site.Origin.y + 1.7f, -338.2f),
                    site.CounterWorld, "glass-return-after-closing");
                yield return GlassReturnNativeFrame(camera, plan.HomeDoor + plan.House.Lot.FacadeForward * 3.5f + Vector3.up * 1.6f,
                    plan.HomeDoor + Vector3.up, "glass-return-worker-home");

                // Morning uses the same house, threshold and staff aperture in reverse.
                camera.transform.SetPositionAndRotation(CityGlassReturnSitePlan.ForLayout(city.Layout).Approach + Vector3.up * 1.7f,
                    Quaternion.LookRotation(worker.transform.position - CityGlassReturnSitePlan.ForLayout(city.Layout).Approach));
                double dawn = GameTimeState.RealSecondsPerGameDay + opening;
                worker.ApplyAt(dawn - worker.CommuteSeconds - .1d);
                previous = worker.Worker.transform.position;
                for (int tick = 0; tick <= Mathf.CeilToInt((worker.CommuteSeconds + .5f) / .05f); tick++)
                {
                    worker.AdvanceAt(dawn - worker.CommuteSeconds + tick * .05d);
                    Assert.That(Vector3.Distance(previous, worker.Worker.transform.position), Is.LessThan(.16f));
                    previous = worker.Worker.transform.position;
                }
                Assert.That(worker.IsWorking, Is.True, "The sorter is back at his window by 08:00.");
                Assert.That(Vector3.Distance(worker.Worker.transform.position, plan.WorkToDoor[0]), Is.LessThan(.01f));
                Assert.That(worker.StaffDoorOpenness, Is.LessThan(.001f));
            }
            finally
            {
                city.Player.Motor.Teleport(savedHero); Physics.SyncTransforms();
            }
        }

        private static IEnumerator VerifyGlassReturnOperating(CityGameRoot city, CityGlassReturnWorker worker)
        {
            var site = CityGlassReturnSitePlan.ForLayout(city.Layout);
            var circuit = worker.GetComponentInChildren<CityGlassReturnWorkLight>();
            var atmosphere = city.Night.Atmosphere;
            var camera = city.Camera;
            Transform Part(string key) => CityGlassReturnAssetProvider.FindPart(worker.transform, key);
            Transform contact = Part("ANCHOR_SwitchContact");
            Transform rocker = Part("MOVE_SwitchRocker"), grille = Part("MOVE_WindowGrille");
            var hand = CityPedestrianHandProps.FindSocket(worker.Worker.ModelRoot, "hand.R");
            var anatomy = worker.Worker.GetComponent<NpcHandPose>().GetAnatomicalFrame(false);
            Vector3 savedHero = city.Player.GameObject.transform.position;
            Vector3 apron = site.Approach + new Vector3(2f, PlayerFactory.GroundedRootOffset, -2f);
            Vector3 Local(float x, float y, float z) => worker.transform.TransformPoint(new Vector3(x, y, z));
            double close = CityGlassReturnWorker.ShiftEndMinute / GameTimeState.GameMinutesPerRealSecond;
            double press = close + worker.ClosingSwitchPressSeconds;
            int onEvents = 0, offEvents = 0;
            void PowerChanged(bool on) { if (on) onEvents++; else offEvents++; }
            void PalmContact()
            {
                Assert.That(worker.IsUsingSwitch, Is.True);
                Assert.That(worker.SwitchContactWeight, Is.GreaterThan(.999f));
                Assert.That(worker.SwitchContactError, Is.LessThan(.045f));
                Assert.That(Vector3.Distance(hand.position + hand.rotation * anatomy.PalmCentreInHand, contact.position),
                    Is.LessThan(.045f), "The rendered rig's open palm reaches the moving switch rocker.");
            }
            void CheckIndependentNightLights()
            {
                foreach (string key in new[] { "ANCHOR_SignLightLeft", "ANCHOR_SignLightRight", "ANCHOR_Source" })
                {
                    Transform source = key == "ANCHOR_Source" ?
                        CityGlassReturnAssetProvider.FindPart(worker.transform.parent.Find("Glass Return Wall Floodlight"), key) : Part(key);
                    Assert.That(atmosphere.StreetLightPool.Any(light => light && light.enabled && light.intensity > 0f &&
                        Vector3.Distance(light.transform.position, source.position) < .025f), Is.True,
                        "The switch must retain the independent night source " + key);
                }
            }
            Assert.That(circuit, Is.Not.Null);
            worker.WorkLightChanged += PowerChanged;
            try
            {
                SetChurchRemodelTime(CityGlassReturnWorker.ShiftEndMinute);
                city.DayNight.ApplyCurrentTime(true);
                city.Player.Motor.Teleport(apron); Physics.SyncTransforms();
                worker.ApplyAt(press - .02d);
                atmosphere.RefreshImmediate();
                onEvents = offEvents = 0;
                Assert.That(worker.WorkLightOn && circuit.IsPowered, Is.True);
                Assert.That(worker.WindowGrilleOpenness, Is.EqualTo(1f).Within(.0001f));
                Assert.That(circuit.PooledLight && circuit.PooledLight.enabled, Is.True);
                yield return GlassReturnNativeFrame(camera, Local(2.8f, 1.9f, 5.8f),
                    Local(0f, 1.7f, 1.50f), "glass-return-open-window-sign-night");
                worker.AdvanceAt(press + .01d);
                Assert.That(offEvents, Is.EqualTo(1));
                Assert.That(worker.WorkLightOn || circuit.IsPowered, Is.False);
                Assert.That(circuit.PooledLight.enabled, Is.False);
                Assert.That(circuit.PooledLight.intensity, Is.Zero);
                Assert.That(worker.TransitionTime, Is.EqualTo(.01f).Within(.003f));
                Assert.That(worker.WindowGrilleOpenness, Is.LessThan(1f),
                    "The same press that turns off the lamp starts the real grille.");
                PalmContact();
                yield return GlassReturnNativeFrame(camera, Local(-.55f, 1.60f, 3.2f),
                    contact.position + Vector3.up * .12f, "glass-return-switch-off-palm");
                PalmContact();

                float pausedGrille = worker.WindowGrilleOpenness;
                Quaternion pausedRocker = rocker.rotation;
                Vector3 pausedFeet = worker.Worker.transform.position;
                using (GameTimeScaleRuntime.AcquirePause())
                {
                    worker.AdvanceAt(press + .11d);
                    yield return null;
                    Assert.That(worker.WindowGrilleOpenness, Is.EqualTo(pausedGrille));
                    Assert.That(rocker.rotation, Is.EqualTo(pausedRocker));
                    Assert.That(worker.Worker.transform.position, Is.EqualTo(pausedFeet));
                    Assert.That(worker.WorkLightOn, Is.False);
                    PalmContact();
                }
                for (double clock = press + .06d; clock <= press + 1.51d; clock += .05d) worker.AdvanceAt(clock);
                Assert.That(worker.WindowGrilleOpenness, Is.EqualTo(.5f).Within(.035f));
                Assert.That(offEvents, Is.EqualTo(1));
                yield return GlassReturnNativeFrame(camera, Local(-.6f, 1.65f, 4.6f),
                    site.CounterWorld + Vector3.up * .52f, "glass-return-grille-half-closed");

                // Put the production capsule into the receiving aperture. The
                // powered-down motor must stop above it, then continue unchanged.
                worker.ApplyAt(press - .10d);
                onEvents = offEvents = 0;
                city.Player.Motor.Teleport(new Vector3(grille.position.x,
                    site.Origin.y + PlayerFactory.GroundedRootOffset, grille.position.z));
                Physics.SyncTransforms();
                for (double clock = press - .05d; clock <= press + 4.01d; clock += .05d) worker.AdvanceAt(clock);
                Assert.That(worker.WorkLightOn, Is.False);
                Assert.That(worker.WindowGrilleOpenness, Is.GreaterThan(.05f), "A closing grille must not crush the hero.");
                Assert.That(worker.IsUsingSwitch, Is.True, "The worker waits for the stalled motor at the switch.");
                float blocked = worker.WindowGrilleOpenness;
                for (double clock = press + 4.05d; clock <= press + 4.51d; clock += .05d) worker.AdvanceAt(clock);
                Assert.That(worker.WindowGrilleOpenness, Is.EqualTo(blocked).Within(.0001f));
                Assert.That(offEvents, Is.EqualTo(1));
                city.Player.Motor.Teleport(apron); Physics.SyncTransforms();
                for (double clock = press + 4.55d; clock <= press + 8.51d; clock += .05d) worker.AdvanceAt(clock);
                Assert.That(worker.WindowGrilleOpenness, Is.LessThan(.001f));
                Assert.That(worker.WorkLightOn, Is.False);
                Assert.That(offEvents, Is.EqualTo(1), "Clearing the aperture resumes the same motor event.");

                worker.ApplyAt(close + worker.CommuteSeconds + 1d);
                atmosphere.RefreshImmediate();
                Assert.That(worker.IsAtHome, Is.True);
                Assert.That(worker.WorkLightOn || circuit.IsPowered, Is.False);
                Assert.That(worker.WindowGrilleOpenness, Is.LessThan(.001f));
                CheckIndependentNightLights();
                yield return GlassReturnNativeFrame(camera, Local(2.8f, 1.9f, 5.8f),
                    Local(0f, 1.7f, 1.50f), "glass-return-closed-window-sign-night");
                Assert.That(circuit.PooledLight.enabled, Is.False, "The native frame must retain the switched-off source.");
                Assert.That(circuit.PooledLight.intensity, Is.Zero);

                double dawn = GameTimeState.RealSecondsPerGameDay +
                    CityGlassReturnWorker.ShiftStartMinute / GameTimeState.GameMinutesPerRealSecond;
                double morningPress = dawn - worker.OpeningSwitchPressBeforeShift;
                SetChurchRemodelTime(CityGlassReturnWorker.ShiftStartMinute);
                city.DayNight.ApplyCurrentTime(true);
                worker.ApplyAt(morningPress - .02d);
                Assert.That(worker.WorkLightOn, Is.False);
                Assert.That(worker.WindowGrilleOpenness, Is.LessThan(.001f));
                onEvents = offEvents = 0;
                worker.AdvanceAt(morningPress + .02d);
                Assert.That(onEvents, Is.EqualTo(1));
                Assert.That(worker.WorkLightOn && circuit.IsPowered, Is.True);
                Assert.That(circuit.PooledLight && circuit.PooledLight.enabled, Is.True);
                Assert.That(worker.WindowGrilleOpenness, Is.GreaterThan(0f));
                PalmContact();
                yield return GlassReturnNativeFrame(camera, Local(-.55f, 1.60f, 3.2f),
                    contact.position + Vector3.up * .12f, "glass-return-switch-on-morning");
                PalmContact();
                for (double clock = morningPress + .07d; clock <= dawn + .31d; clock += .05d) worker.AdvanceAt(clock);
                Assert.That(worker.IsWorking, Is.True, "The grille is open and the sorter is at his counter for08:00.");
                Assert.That(worker.WindowGrilleOpenness, Is.GreaterThan(.999f));
                Assert.That(Vector3.Distance(worker.Worker.transform.position, worker.CommutePlan.WorkToSwitch[0]), Is.LessThan(.01f));
                Assert.That(onEvents, Is.EqualTo(1));

                // Neither the circuit marker nor a switch event may revive a
                // light whose shared City pool has been disabled.
                atmosphere.enabled = false;
                circuit.enabled = false; circuit.enabled = true;
                Assert.That(circuit.PooledLight.enabled, Is.False);
                worker.ApplyAt(close + worker.CommuteSeconds + 1d);
                worker.ApplyAt(GlassSortingMoment(1.4));
                Assert.That(circuit.IsPowered, Is.True);
                Assert.That(circuit.PooledLight.enabled, Is.False);
                atmosphere.enabled = true; atmosphere.RefreshImmediate();
            }
            finally
            {
                worker.WorkLightChanged -= PowerChanged;
                circuit.enabled = true; atmosphere.enabled = true;
                city.Player.Motor.Teleport(savedHero); Physics.SyncTransforms();
            }
        }

        private static void VerifyGlassWorkerRoute(CityGlassReturnWorker worker, Transform hero, bool crossing)
        {
            var faults = new System.Collections.Generic.List<string>();
            // The door is closed before he approaches/leaves its apron, and
            // fully open while he traverses the measured aperture.
            var routes = crossing ? new[] { worker.CommutePlan.DoorCrossing } :
                new[] { worker.CommutePlan.WorkToDoor, worker.CommutePlan.DoorToHome };
            foreach (var route in routes)
                for (int i = 1; i < route.Count; i++)
                {
                    Vector3 span = route[i] - route[i - 1];
                    if (span.sqrMagnitude < .000001f) continue;
                    foreach (var hit in Physics.CapsuleCastAll(route[i - 1] + Vector3.up * .40f,
                        route[i - 1] + Vector3.up * 1.49f, .19f, span.normalized, span.magnitude,
                        Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore))
                    {
                        if (hit.transform.IsChildOf(worker.Worker.transform) || hit.transform.IsChildOf(hero)) continue;
                        if (hit.collider is CapsuleCollider || hit.collider is CharacterController) continue;
                        faults.Add(hit.collider.name + " at " + hit.point + " on " + route[i - 1] + " -> " + route[i]);
                    }
                }
            Assert.That(faults, Is.Empty, string.Join("\n", faults));
        }

        private static IEnumerator GlassReturnNativeFrame(Camera camera, Vector3 eye, Vector3 target, string name)
        {
            var texture = new RenderTexture(Width, Height, 24);
            var pixels = new Texture2D(Width, Height, TextureFormat.RGB24, false);
            RenderTexture previous = camera.targetTexture, previousActive = RenderTexture.active;
            camera.transform.SetPositionAndRotation(eye, Quaternion.LookRotation(target - eye));
            camera.fieldOfView = 60f;
            try
            {
                camera.targetTexture = texture;
                yield return null; yield return null;
                RenderTexture.active = texture;
                pixels.ReadPixels(new Rect(0, 0, Width, Height), 0, 0); pixels.Apply();
                Assert.That(IsBlank(pixels), Is.False);
                string folder = Path.Combine(Directory.GetCurrentDirectory(), "Captures", SceneIds.City);
                Directory.CreateDirectory(folder);
                File.WriteAllBytes(Path.Combine(folder, name + ".png"), pixels.EncodeToPNG());
            }
            finally
            {
                camera.targetTexture = previous; RenderTexture.active = previousActive;
                Object.DestroyImmediate(pixels); texture.Release(); Object.DestroyImmediate(texture);
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

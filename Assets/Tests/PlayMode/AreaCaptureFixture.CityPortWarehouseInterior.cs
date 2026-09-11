using System;
using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

namespace BarPromenade.Tests.PlayMode
{
    public sealed partial class AreaCaptureFixture
    {
        [UnityTest]
        [Explicit("The furnished cold store at actual cargo states, with local light and clear working routes.")]
        public IEnumerator CityPortWarehouseInterior() => CaptureFocusedPort(CaptureWarehouseInterior);

        private static IEnumerator CaptureWarehouseInterior(Camera camera, CityGameRoot city,
            CityPortController port, CityPortCrew crew)
        {
            var cannery = city.Cannery;
            double savedSupply = cannery.WorkingSeconds, savedPort = port.ElapsedSeconds, savedLife = crew.LifeElapsedSeconds;
            float savedNight = CityNightSiteLightRegistry.NightFactor, savedField = camera.fieldOfView;
            Pose savedCamera = new Pose(camera.transform.position, camera.transform.rotation);
            bool savedForce = port.ForcePresentation;
            Transform savedObserver = port.PresentationObserver;
            PlayerCameraFollow follow = camera.GetComponent<PlayerCameraFollow>();
            bool savedFollow = follow != null && follow.enabled;
            var sound = port.GetComponentInChildren<CityPortSound>();
            bool savedSound = sound.enabled;
            var lamps = new Light[3];
            var enabledLamps = new bool[3];
            var probeObject = new GameObject("Warehouse clearance probe") { hideFlags = HideFlags.HideAndDontSave };
            var probe = probeObject.AddComponent<CapsuleCollider>();
            probe.isTrigger = true;
            try
            {
                if (follow != null) follow.enabled = false;
                sound.enabled = false;
                for (int i = 0; i < lamps.Length; i++)
                {
                    string suffix = ((char)('A' + i)).ToString();
                    Transform host = port.transform.Find("Port Warehouse Work Light " + suffix);
                    Assert.That(host, Is.Not.Null);
                    lamps[i] = host.GetComponent<Light>();
                    Assert.That(lamps[i], Is.Not.Null);
                    enabledLamps[i] = lamps[i].enabled;
                    Transform anchor = CityPortAssetProvider.FindPart(port.Dock.gameObject, "ANCHOR_WarehouseLight" + suffix);
                    Assert.That(Vector3.Distance(lamps[i].transform.position, anchor.position), Is.LessThan(.002f));
                    Assert.That(lamps[i].type, Is.EqualTo(LightType.Spot));
                    Assert.That(lamps[i].lightmapBakeType, Is.EqualTo(LightmapBakeType.Realtime));
                    Assert.That(lamps[i].shadows, Is.EqualTo(LightShadows.Soft));
                    Assert.That(lamps[i].color.r, Is.GreaterThan(lamps[i].color.b * 1.5f));
                    Assert.That(lamps[i].range, Is.LessThanOrEqualTo(8f));
                    Renderer glass = CityPortAssetProvider.FindPart(port.Dock.gameObject, "WarehouseLampGlass" + suffix).GetComponent<Renderer>();
                    Assert.That(glass.bounds.size.x, Is.InRange(.3f, 1.5f), "Measure the imported lens in world metres.");
                    Assert.That(glass.bounds.min.y, Is.GreaterThan(lamps[i].transform.position.y));
                }
                AssertWarehouseMesh(port, "DockWarehousePanels", new Vector3(10f, 2f, 5f));
                AssertWarehouseMesh(port, "DockWarehouseRefrigeration", new Vector3(.4f, .4f, 1f));
                AssertWarehouseMesh(port, "DockWarehouseStorage", new Vector3(.3f, 1f, 1f));
                AssertWarehouseMesh(port, "DockWarehouseBench", new Vector3(.5f, .6f, 1f));
                var furniture = CityPortAssetProvider.FindPart(port.Dock.gameObject, "COL_WarehouseFurniture").GetComponent<MeshCollider>();
                Assert.That(furniture, Is.Not.Null);
                Assert.That(furniture.enabled && !furniture.isTrigger, Is.True);
                AssertPortBodyBlocksHero(city.Player.GameObject.GetComponent<CharacterController>(), furniture);
                yield return ValidateWarehouseClearance(city, port, crew, furniture, probe);

                // Pick the fullest real state: the first delivery can already
                // be collecting while the ship is still unloading. Never add
                // display-only crates to make an invented full warehouse.
                double fullest = 0d;
                int fullestCount = 0;
                for (int batch = 0; batch < 2; batch++)
                {
                    double end = cannery.Cycle.StageStart(CityFishSupplyStage.PortToFactory, batch);
                    for (double seconds = cannery.Cycle.BatchStart(batch); seconds < end; seconds += .5d)
                    {
                        CityFishSupplySnapshot snapshot = cannery.Cycle.Sample(seconds);
                        if (snapshot.PortFish <= fullestCount || (snapshot.TransferProgress >= .18f && snapshot.IsTransfer)) continue;
                        fullest = seconds; fullestCount = snapshot.PortFish;
                    }
                }
                Assert.That(fullestCount, Is.GreaterThan(0));
                Assert.That(CityPortCycle.CargoCount, Is.EqualTo(3));
                Assert.That(port.Cargo.Length, Is.EqualTo(3));
                double empty = CanneryTime(cannery, CityFishSupplyStage.PortToFactory, .5f);
                double pickup = TransferTime(cannery, CityFishSupplyStage.LoadFish, 2, .25f);
                foreach (int hour in new[] { 12, 21 })
                {
                    GameSessionState.AdvanceGameTime((float)((hour * 60d - GameSessionState.GameTimeOfDayMinutes) / GameTimeState.GameMinutesPerRealSecond));
                    city.DayNight.ApplyCurrentTime(true);
                    string light = hour == 12 ? "day" : "night";
                    foreach (var state in new[] { (time: empty, name: "empty"), (time: fullest, name: "stock"), (time: pickup, name: "pickup") })
                    {
                        cannery.ApplyAt(state.time);
                        crew.ApplyAt(port.ElapsedSeconds, savedLife + state.time);
                        Assert.That(cannery.Snapshot.PortFish, Is.InRange(0, 3));
                        if (state.name == "empty") Assert.That(cannery.Snapshot.PortFish, Is.Zero);
                        if (state.name == "stock") Assert.That(cannery.Snapshot.PortFish, Is.EqualTo(fullestCount));
                        Vector3 from = state.name == "empty" ? new Vector3(-1.6f, 3.18f, -12.8f) : new Vector3(-5.1f, 3.18f, -18.05f);
                        Vector3 target = state.name == "empty" ? new Vector3(-6.8f, 3f, -16.1f) : new Vector3(1.1f, 2.85f, -16.4f);
                        yield return CapturePortSocialPose(camera, "warehouse-" + light + "-" + state.name,
                            port.Plan.World(from), port.Plan.World(target), 72f);
                    }
                    cannery.ApplyAt(empty);
                    yield return CapturePortSocialPose(camera, "warehouse-" + light + "-receiving",
                        port.Plan.World(new Vector3(-3.6f, 3.18f, -14.65f)),
                        port.Plan.World(new Vector3(.2f, 3.1f, -13.1f)), 78f);
                    AssertWarehouseSurfaceLight(camera, lamps, hour == 12);
                }

                // Existing opaque transfer remains behind the same baffle.
                cannery.ApplyAt(CityPortCycle.UnloadStartSeconds + CityPortCycle.StoredAtSeconds + .01d);
                Physics.SyncTransforms();
                Vector3 source = port.Plan.World(new Vector3(0f, 2.7f, -10f));
                Vector3 destination = port.Plan.World(port.Plan.WarehouseDropLocal) + Vector3.up * 1.2f;
                Assert.That(Physics.Linecast(source, destination, out RaycastHit wall,
                    Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore), Is.True);
                Assert.That(wall.collider.name.StartsWith("COL_Warehouse", StringComparison.Ordinal), Is.True);
                Assert.That(port.Cargo[0].gameObject.activeInHierarchy, Is.False);

                AudioSource hum = sound.RefrigerationSource;
                Assert.That(hum, Is.Not.Null);
                Assert.That(hum.loop && !hum.playOnAwake, Is.True);
                Assert.That(hum.spatialBlend, Is.EqualTo(1f));
                Assert.That(hum.outputAudioMixerGroup, Is.SameAs(GameAudioMixer.SfxWorldGroup));
                Assert.That(hum.maxDistance, Is.LessThanOrEqualTo(16f));
                sound.Advance(port.ElapsedSeconds, .5f, true);
                Assert.That(Vector3.Distance(hum.transform.position,
                    CityPortAssetProvider.FindPart(port.Dock.gameObject, "ANCHOR_WarehouseRefrigeration").position), Is.LessThan(.002f));
                Assert.That(hum.volume, Is.GreaterThan(.01f));
                sound.Advance(port.ElapsedSeconds, .04f, false);
                Assert.That(sound.IsPaused, Is.True);
                Assert.That(hum.isPlaying, Is.False);
                sound.Advance(port.ElapsedSeconds, .04f, true);
                Assert.That(sound.IsPaused, Is.False);
                probeObject.transform.position = port.Plan.Origin + Vector3.one * 5000f;
                port.PresentationObserver = probeObject.transform;
                port.ForcePresentation = false;
                port.RefreshPresentation();
                sound.Advance(port.ElapsedSeconds, .04f, true);
                foreach (Light lamp in lamps) Assert.That(lamp.isActiveAndEnabled, Is.False);
                Assert.That(hum.isPlaying, Is.False);
                port.ForcePresentation = true;
                port.RefreshPresentation();
                sound.Advance(port.ElapsedSeconds, .5f, true);
                foreach (Light lamp in lamps) Assert.That(lamp.isActiveAndEnabled, Is.True);
                Assert.That(hum.volume, Is.GreaterThan(.01f));
                Debug.Log($"WAREHOUSE INTERIOR: imported fittings, lit daytime/night surfaces, clear working routes and local pause/distance audio; fullest actual buffer={fullestCount}.");
            }
            finally
            {
                port.PresentationObserver = savedObserver;
                port.ForcePresentation = savedForce;
                cannery.ApplyAt(savedSupply);
                port.ApplyAt(savedPort, 15f);
                crew.ApplyAt(savedPort, savedLife);
                for (int i = 0; i < lamps.Length; i++) if (lamps[i] != null) lamps[i].enabled = enabledLamps[i];
                CityNightSiteLightRegistry.SetNightFactor(savedNight);
                sound.enabled = savedSound;
                camera.transform.SetPositionAndRotation(savedCamera.position, savedCamera.rotation);
                camera.fieldOfView = savedField;
                if (follow != null) follow.enabled = savedFollow;
                Object.DestroyImmediate(probeObject);
            }
        }

        private static void AssertWarehouseMesh(CityPortController port, string prefix, Vector3 minimum)
        {
            bool found = false;
            Bounds measured = default;
            foreach (Renderer renderer in port.Dock.GetComponentsInChildren<Renderer>(true))
            {
                if (!renderer.name.StartsWith(prefix, StringComparison.Ordinal)) continue;
                if (!found) { measured = renderer.bounds; found = true; }
                else measured.Encapsulate(renderer.bounds);
            }
            Assert.That(found, Is.True, prefix);
            for (int axis = 0; axis < 3; axis++)
                Assert.That(measured.size[axis], Is.GreaterThan(minimum[axis]), prefix + " imported metres, axis " + axis);
        }

        private static IEnumerator ValidateWarehouseClearance(CityGameRoot city, CityPortController port,
            CityPortCrew crew, MeshCollider furniture, CapsuleCollider probe)
        {
            CharacterController hero = city.Player.GameObject.GetComponent<CharacterController>();
            probe.radius = hero.radius; probe.height = hero.height; probe.center = hero.center;
            Vector3[] walk = { new Vector3(0f, 1.52f, -12.7f), new Vector3(-5.7f, 1.52f, -13.4f),
                new Vector3(-5.7f, 1.52f, -17.8f), new Vector3(-3.4f, 1.52f, -17.8f) };
            for (int segment = 1; segment < walk.Length; segment++)
            for (int step = 0; step <= 40; step++)
            {
                probe.transform.position = port.Plan.World(Vector3.Lerp(walk[segment - 1], walk[segment], step / 40f));
                AssertWarehouseBodyClear(probe, furniture, "hero west aisle");
                Assert.That(city.World.WalkableArea.Contains(probe.transform.position, hero.radius), Is.True);
            }
            var docker = crew.ShoreWorker.GetComponent<CapsuleCollider>();
            Transform driver = city.Cannery.transform.Find("Fish Delivery Driver");
            probe.radius = .22f; probe.height = 1.8f; probe.center = Vector3.up * .9f;
            Bounds portCart = PortLocalMeshBounds(port.Trolley), deliveryCart = PortLocalMeshBounds(city.Cannery.PortTrolley);
            for (int unit = 0; unit < CityPortCycle.CargoCount; unit++)
            {
                double slot = CityPortCycle.UnloadStartSeconds + unit * CityPortCycle.CargoDurationSeconds;
                for (double time = CityPortCycle.UnhookedAtSeconds; time <= CityPortCycle.CargoDurationSeconds; time += .2d)
                {
                    port.ApplyAt(slot + time, 15f);
                    crew.ApplyAt(port.ElapsedSeconds, slot + time);
                    AssertWarehouseBodyClear(docker, furniture, "docker " + unit);
                    AssertWarehouseCartClear(port.Trolley, portCart, furniture);
                }
                double start = city.Cannery.Cycle.TransferUnitStart(CityFishSupplyStage.LoadFish, unit);
                for (double time = 0d; time <= CityFishSupplyCycle.TransferUnitDuration; time += .2d)
                {
                    city.Cannery.ApplyAt(start + time);
                    probe.transform.SetPositionAndRotation(driver.position, driver.rotation);
                    AssertWarehouseBodyClear(probe, furniture, "driver " + unit);
                    AssertWarehouseCartClear(city.Cannery.PortTrolley, deliveryCart, furniture);
                }
                yield return null;
            }
        }

        private static void AssertWarehouseBodyClear(CapsuleCollider body, Collider furniture, string role)
        {
            bool hit = Physics.ComputePenetration(body, body.transform.position, body.transform.rotation,
                furniture, furniture.transform.position, furniture.transform.rotation, out _, out float depth);
            Assert.That(hit && depth > .005f, Is.False, role + " crosses the new fittings: " + depth);
        }

        private static void AssertWarehouseCartClear(Transform cart, Bounds actual, Collider furniture)
        {
            Physics.SyncTransforms();
            foreach (Collider obstacle in Physics.OverlapBox(cart.TransformPoint(actual.center),
                Vector3.Scale(actual.extents, cart.lossyScale) * .99f, cart.rotation,
                Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore))
                Assert.That(obstacle, Is.Not.SameAs(furniture), "The imported cart envelope crosses the new furniture.");
        }

        private static void AssertWarehouseSurfaceLight(Camera camera, Light[] lamps, bool day)
        {
            float savedNight = CityNightSiteLightRegistry.NightFactor;
            try
            {
                CityNightSiteLightRegistry.SetNightFactor(1f);
                var nightIntensities = new float[lamps.Length];
                for (int i = 0; i < lamps.Length; i++) nightIntensities[i] = lamps[i].intensity;
                CityNightSiteLightRegistry.SetNightFactor(day ? 0f : 1f);
                for (int i = 0; i < lamps.Length; i++)
                {
                    Assert.That(lamps[i].enabled, Is.True);
                    Assert.That(lamps[i].intensity, Is.EqualTo(nightIntensities[i] *
                        (day ? GameTimeDayNightRules.DayFixtureFloor : 1f)).Within(.001f));
                }
                Color32[] lit = ReadCanopyLightPixels(camera);
                foreach (Light lamp in lamps) lamp.enabled = false;
                Color32[] dark = ReadCanopyLightPixels(camera);
                int changed = 0;
                long delta = 0;
                // The physical lamp glasses remain unchanged between frames.
                // A large pixel region can therefore only come from surface light.
                for (int i = 0; i < lit.Length; i++)
                {
                    int difference = lit[i].r + lit[i].g + lit[i].b - dark[i].r - dark[i].g - dark[i].b;
                    if (difference <= 9) continue;
                    changed++; delta += difference;
                }
                Assert.That(changed, Is.GreaterThan(250), "Warehouse light must illuminate real surfaces.");
                Assert.That(delta, Is.GreaterThan(4000));
                Debug.Log($"WAREHOUSE {(day ? "DAY" : "NIGHT")} LIGHT: surface pixels={changed}, RGB delta={delta}.");
            }
            finally
            {
                foreach (Light lamp in lamps) lamp.enabled = true;
                CityNightSiteLightRegistry.SetNightFactor(savedNight);
            }
        }
    }
}

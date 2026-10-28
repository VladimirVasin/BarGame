using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

namespace BarPromenade.Tests.PlayMode
{
    public sealed partial class AreaCaptureFixture
    {
        [UnityTest]
        [Explicit("One residential laundry: measured infill, clear approach, finite shared-rig work, lifecycle and day/night/state views.")]
        public IEnumerator CityLaundry()
        {
            GameSessionState.BeginNewGame();
            Assert.That(GameSessionState.TryStartGameTimeAt(12 * 60), Is.True);
            CityLayout layout = CityLayoutCache.GetOrGenerate(CityBlueprintCatalog.Resolve(GameSessionState.CityBlueprintId),
                CityGenerationSettings.Default, GameSessionState.CitySeed);
            CityLaundryPlan planned = CityLaundryPlan.ForLayout(layout);
            Assert.That(planned.IsEnabled, Is.True, "The default residential city must contain the approved laundry.");
            planned.ValidateOrThrow(layout);
            CityGameRoot city = null;
            CityLaundryController laundry = null;
            GameObject grayOwner = null;
            GameObject observer = null;
            VolumeProfile grayProfile = null;
            PlayerCameraFollow follow = null;
            bool followEnabled = false, weatherEnabled = false;
            float previousDelta = Time.captureDeltaTime;
            try
            {
                Time.captureDeltaTime = .05f;
                yield return Capture(SceneIds.City, () =>
                {
                    city = Object.FindAnyObjectByType<CityGameRoot>();
                    return city != null && city.IsInitialized && !CompositionDriver.IsComposing ? city : null;
                }, () =>
                {
                    var facilities = Object.FindObjectsByType<CityLaundryController>(FindObjectsSortMode.None);
                    Assert.That(facilities.Length, Is.EqualTo(1));
                    laundry = facilities[0];
                    Assert.That(laundry.Plan, Is.SameAs(planned));
                    laundry.AutoAdvance = false; laundry.ForcePresentation = true;
                    laundry.ApplyAt(0d);
                    Type.GetType("BarPromenade.Editor.CityLaundryAssetSetup, BarPromenade.Editor", true)
                        .GetMethod("ValidateOrThrow", Type.EmptyTypes).Invoke(null, null);
                    AssertLaundryImportedMetres(laundry);
                    AssertLaundryPublicGround(city, laundry);
                    follow = city.Camera.GetComponent<PlayerCameraFollow>();
                    followEnabled = follow != null && follow.enabled;
                    if (follow != null) follow.enabled = false;
                    weatherEnabled = city.Weather.enabled;
                    city.Weather.enabled = false;
                    city.Player.Motor.SetInputEnabled(false);
                    grayOwner = new GameObject("Laundry acceptance grayscale");
                    Volume gray = grayOwner.AddComponent<Volume>();
                    gray.isGlobal = true; gray.priority = 10000f; gray.weight = 0f;
                    grayProfile = ScriptableObject.CreateInstance<VolumeProfile>();
                    grayProfile.Add<ColorAdjustments>(true).saturation.Override(-100f);
                    gray.sharedProfile = grayProfile;
                    return LaundryShots(city, laundry, gray);
                });

                AssertLaundryFrameColours();
                AssertLaundryLightAndMaterialBindings(city, laundry);
                AssertLaundryWorkContacts(laundry);
                GameSessionState.UpdateDrinkingProgress(0, DrinkId.LightBeer, 0);
                laundry.AutoAdvance = true;
                yield return null;
                using (GameTimeScaleRuntime.AcquirePause())
                {
                    yield return null;
                    laundry.ApplyAt(16d);
                    double elapsed = laundry.ElapsedSeconds;
                    Vector3 basket = laundry.Basket.position;
                    Quaternion drum = CityLaundryAssetProvider.FindPart(laundry.ModelRoot, "MOVE_DrumA").rotation;
                    yield return null;
                    Assert.That(laundry.ElapsedSeconds, Is.EqualTo(elapsed));
                    Assert.That(Vector3.Distance(laundry.Basket.position, basket), Is.LessThan(.0001f));
                    Assert.That(Quaternion.Angle(CityLaundryAssetProvider.FindPart(laundry.ModelRoot, "MOVE_DrumA").rotation, drum),
                        Is.LessThan(.001f));
                    laundry.gameObject.SetActive(false);
                    foreach (AudioSource source in laundry.GetComponentsInChildren<AudioSource>(true))
                        Assert.That(source.isPlaying, Is.False, "Disable releases the laundry's positioned machine voice.");
                    laundry.gameObject.SetActive(true);
                    yield return null;
                    Assert.That(Vector3.Distance(laundry.Basket.position, basket), Is.LessThan(.0001f));
                    Assert.That(laundry.HandsMatch, Is.True, "Re-enable while paused restores the held rig without advancing time.");
                    Assert.That(laundry.HandContactError, Is.LessThan(.06f));
                }

                var appearance = laundry.Worker.GetComponent<DefaultNpcAppearance>();
                string identity = appearance.AppearanceKey;
                observer = new GameObject("Laundry distance acceptance observer");
                laundry.ForcePresentation = false;
                laundry.PresentationObserver = observer.transform;
                observer.transform.position = laundry.Plan.Position + new Vector3(180f, 0f, 180f);
                yield return null;
                double farTime = laundry.ElapsedSeconds;
                yield return null;
                Assert.That(laundry.IsPresentationActive, Is.False);
                Assert.That(laundry.ElapsedSeconds, Is.GreaterThan(farTime), "Distance sleeps presentation, retaining authoritative time.");
                Assert.That(appearance.AppearanceKey, Is.EqualTo(identity));
                Collider floor = CityLaundryAssetProvider.FindPart(laundry.ModelRoot, "COL_Floor").GetComponent<Collider>();
                Assert.That(floor.enabled && floor.gameObject.activeInHierarchy, Is.True);
                observer.transform.position = laundry.Plan.ApproachWorld;
                yield return null;
                Assert.That(laundry.IsPresentationActive, Is.True);
                Assert.That(laundry.Worker.GetComponent<DefaultNpcAppearance>().AppearanceKey, Is.EqualTo(identity));
                Debug.Log("CITY LAUNDRY ACCEPTANCE OK: one measured residential infill, clear public approach, physical room/court, permanent worker, finite hand/basket contacts, continuous cycle, pause/distance/disable restoration and native day/night/weather/state views.");
            }
            finally
            {
                if (grayOwner != null) Object.DestroyImmediate(grayOwner);
                if (grayProfile != null) Object.DestroyImmediate(grayProfile);
                if (observer != null) Object.DestroyImmediate(observer);
                if (follow != null) follow.enabled = followEnabled;
                if (city != null) { city.Weather.enabled = weatherEnabled; city.Player.Motor.SetInputEnabled(true); }
                GameSessionState.UpdateDrinkingProgress(0, DrinkId.LightBeer, 0);
                Time.captureDeltaTime = previousDelta;
            }
        }

        private static void AssertLaundryImportedMetres(CityLaundryController laundry)
        {
            Vector3 low = Vector3.positiveInfinity, high = Vector3.negativeInfinity;
            Quaternion inverse = Quaternion.Inverse(laundry.Plan.Rotation);
            foreach (MeshFilter filter in laundry.ModelRoot.GetComponentsInChildren<MeshFilter>(true))
            {
                if (filter.name.StartsWith("COL_", StringComparison.Ordinal) ||
                    filter.GetComponentInParent<VillageResidentPresentation>() != null) continue;
                foreach (Vector3 vertex in filter.sharedMesh.vertices)
                {
                    Vector3 metre = inverse * (filter.transform.TransformPoint(vertex) - laundry.Plan.Position);
                    low = Vector3.Min(low, metre); high = Vector3.Max(high, metre);
                }
            }
            Assert.That(Vector3.Distance(low, new Vector3(-4.3f, -.15f, -3.3f)), Is.LessThan(.03f));
            Assert.That(Vector3.Distance(high, new Vector3(4.3f, 4.2f, 3.3f)), Is.LessThan(.03f));
            // Setting a world pose must retain the authored horizontal basket,
            // rather than expose an FBX wrapper's quarter turn.
            low = Vector3.positiveInfinity; high = Vector3.negativeInfinity;
            Quaternion basketAxes = Quaternion.Inverse(laundry.Basket.rotation);
            foreach (MeshFilter filter in laundry.Basket.GetComponentsInChildren<MeshFilter>(true))
                foreach (Vector3 vertex in filter.sharedMesh.vertices)
                {
                    Vector3 metre = basketAxes * (filter.transform.TransformPoint(vertex) - laundry.Basket.position);
                    low = Vector3.Min(low, metre); high = Vector3.Max(high, metre);
                }
            Assert.That(Vector3.Distance(low, new Vector3(-.348f, -.18f, -.21f)), Is.LessThan(.03f));
            Assert.That(Vector3.Distance(high, new Vector3(.348f, .218f, .21f)), Is.LessThan(.03f));
            Assert.That(laundry.Worker.GetComponent<DefaultNpcAppearance>().AppearanceKey,
                Is.EqualTo(DefaultNpcPopulation.CityLaundryWorker));
            Assert.That(laundry.RainShelters.Count, Is.GreaterThan(0));
            Vector3 canopy = laundry.Plan.World(new Vector3(2.25f, 0f, 1.1f));
            bool covered = false;
            foreach (Collider shelter in laundry.RainShelters)
                covered |= shelter != null && shelter.Raycast(new Ray(canopy + Vector3.up * 5f, Vector3.down),
                    out RaycastHit hit, 4f) && hit.normal.y > .9f;
            Assert.That(covered, Is.True, "The service court has an actual horizontal roof above its worker.");
        }

        private static void AssertLaundryPublicGround(CityGameRoot city, CityLaundryController laundry)
        {
            Physics.SyncTransforms();
            foreach (Rect solid in laundry.Plan.SolidFootprints)
                Assert.That(city.World.WalkableArea.Contains(new Vector3(solid.center.x, laundry.Plan.Position.y, solid.center.y), .25f),
                    Is.False, "The laundry's solid room participates in shared navigation.");
            CharacterController hero = city.Player.GameObject.GetComponent<CharacterController>();
            for (int leg = 0; leg + 1 < laundry.Plan.AccessPath.Count; leg++)
            {
                Vector3 start = laundry.Plan.AccessPath[leg], end = laundry.Plan.AccessPath[leg + 1];
                int steps = Mathf.Max(1, Mathf.CeilToInt(Vector3.Distance(start, end) / .5f));
                for (int step = 0; step <= steps; step++)
                {
                    Vector3 point = Vector3.Lerp(start, end, step / (float)steps);
                    Assert.That(Physics.Raycast(point + Vector3.up * 1.2f, Vector3.down, out RaycastHit floor, 3f,
                        Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore), Is.True);
                    Assert.That(floor.normal.y, Is.GreaterThan(.8f));
                    point.y = floor.point.y;
                    Assert.That(city.World.WalkableArea.Contains(point, .30f), Is.True, "The laundry keeps a complete public approach.");
                    foreach (Collider obstacle in Physics.OverlapCapsule(point + Vector3.up * (hero.stepOffset + .3f),
                        point + Vector3.up * (hero.height - .3f), .3f, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore))
                    {
                        if (obstacle.transform.IsChildOf(city.Player.GameObject.transform) ||
                            obstacle.GetComponentInParent<DefaultNpcAppearance>() != null ||
                            obstacle.GetComponentInParent<CityPedestrianActor>() != null) continue;
                        Assert.Fail("Laundry approach is obstructed by " + obstacle.name + " at " + point);
                    }
                }
            }
        }

        private static void AssertLaundryWorkContacts(CityLaundryController laundry)
        {
            var body = laundry.Worker.GetComponent<CapsuleCollider>();
            BoxCollider basket = laundry.transform.Find("Laundry Carried Basket Body").GetComponent<BoxCollider>();
            MeshCollider[] solids = laundry.ModelRoot.GetComponentsInChildren<MeshCollider>(true);
            var reachFailures = new List<string>();
            for (double time = 0d; time < CityLaundryController.CycleSeconds; time += .5d)
            {
                laundry.ApplyAt(time); Physics.SyncTransforms();
                if (!laundry.HandsMatch || laundry.HandContactError >= .06f)
                    reachFailures.Add(time + "s: error " + laundry.HandContactError);
                foreach (MeshCollider solid in solids)
                {
                    Assert.That(Physics.ComputePenetration(body, body.transform.position, body.transform.rotation,
                        solid, solid.transform.position, solid.transform.rotation, out _, out float depth) && depth > .015f,
                        Is.False, "The worker crosses " + solid.name + " at " + time);
                    Assert.That(Physics.ComputePenetration(basket, basket.transform.position, basket.transform.rotation,
                        solid, solid.transform.position, solid.transform.rotation, out _, out float basketDepth) && basketDepth > .015f,
                        Is.False, "The carried basket crosses " + solid.name + " at " + time);
                }
            }
            Assert.That(reachFailures, Is.Empty, "Authored hand reach throughout the finite work cycle");
            foreach (double boundary in new[] { 8d, 12d, 20d, 24d, 28d, 32d, 40d, 44d, 48d })
            {
                laundry.ApplyAt(boundary - .01d);
                Vector3 worker = laundry.Worker.transform.position, basketPosition = laundry.Basket.position;
                Quaternion facing = laundry.Worker.transform.rotation;
                laundry.ApplyAt(boundary + .01d);
                Assert.That(Vector3.Distance(laundry.Worker.transform.position, worker), Is.LessThan(.03f));
                Assert.That(Vector3.Distance(laundry.Basket.position, basketPosition), Is.LessThan(.03f));
                Assert.That(Quaternion.Angle(laundry.Worker.transform.rotation, facing), Is.LessThan(4f));
            }
            laundry.ApplyAt(4d);
        }

        private static Shot[] LaundryShots(CityGameRoot city, CityLaundryController laundry, Volume gray)
        {
            var shots = new List<Shot>();
            Add("laundry-day-street", new Vector3(0f, 1.7f, 9f), new Vector3(0f, 1.5f, .8f), 64f, 4d);
            Add("laundry-day-machines", new Vector3(-.3f, 1.5f, 5.6f), new Vector3(-.2f, 1f, -1.5f), 58f, 4d);
            Add("laundry-day-sign", new Vector3(-2.65f, 3f, 6f), new Vector3(-2.65f, 3.04f, 3.19f), 48f, 4d);
            Add("laundry-day-folding", new Vector3(-6.3f, 1.5f, .4f), new Vector3(-2.65f, 1.1f, .45f), 58f, 4d);
            Add("laundry-day-carry", new Vector3(6.5f, 1.6f, 3.8f), new Vector3(2.6f, 1f, 1.1f), 62f, 22d);
            Add("laundry-night-street", new Vector3(0f, 1.7f, 9f), new Vector3(0f, 1.5f, .8f), 64f, 4d, night: true);
            Add("laundry-night-folding", new Vector3(-6.3f, 1.5f, .4f), new Vector3(-2.65f, 1.1f, .45f), 58f, 4d, night: true);
            Add("laundry-night-machines", new Vector3(-.3f, 1.5f, 5.6f), new Vector3(-.2f, 1f, -1.5f), 58f, 4d, night: true);
            Add("laundry-night-sign", new Vector3(-2.65f, 3f, 6f), new Vector3(-2.65f, 3.04f, 3.19f), 48f, 4d, night: true);
            Add("laundry-night-rain", new Vector3(6.5f, 1.6f, 3.8f), new Vector3(2.6f, 1f, 1.1f), 62f, 22d, night: true, rain: true);
            Add("laundry-grayscale", new Vector3(0f, 1.7f, 9f), new Vector3(0f, 1.5f, .8f), 64f, 4d, grayscale: true);
            Add("laundry-intoxication", new Vector3(0f, 1.7f, 9f), new Vector3(0f, 1.5f, .8f), 64f, 4d, intoxicated: true);
            return shots.ToArray();
            void Add(string name, Vector3 from, Vector3 target, float field, double phase,
                bool night = false, bool rain = false, bool grayscale = false, bool intoxicated = false)
            {
                Shot pose = Shot.At(name, laundry.Plan.World(from), laundry.Plan.World(target), field);
                int frames = 0;
                shots.Add(Shot.At(name, pose.Position, pose.Target, field, readyWhen: () =>
                {
                    if (frames++ == 0)
                    {
                        SetChurchRemodelTime((night ? 21 : 12) * 60); city.DayNight.ApplyCurrentTime(true);
                        gray.weight = grayscale ? 1f : 0f;
                        float intensity = rain ? 1f : CityEternalRainShaper.FloorIntensity(0f);
                        city.Rain.SetIntensity(intensity); CityWaterResources.SetRainIntensity(intensity);
                        CityWetSurfaceRegistry.SetImmediate(intensity);
                        GameSessionState.UpdateDrinkingProgress(intoxicated ? 100 : 0, DrinkId.LightBeer, intoxicated ? 1 : 0);
                        MoveCityLightingObserver(city, pose); laundry.ApplyAt(phase);
                    }
                    return frames > (intoxicated ? 60 : rain ? 20 : SettleFrames);
                }));
            }
        }

        private static void AssertLaundryLightAndMaterialBindings(CityGameRoot city, CityLaundryController laundry)
        {
            Assert.That(laundry.GetComponentsInChildren<Light>(true), Is.Empty,
                "Laundry fixtures lease City lights rather than creating a second pool.");
            CityNightAtmosphere night = city.Night.Atmosphere;
            Assert.That(night.RealtimeLightCount, Is.LessThanOrEqualTo(CityNightAtmosphere.MaximumRealtimeLights));
            city.Player.Motor.Teleport(laundry.Plan.World(new Vector3(0f, .1f, 1.8f)));
            night.RefreshImmediate();
            int fixtures = 0;
            foreach (Transform anchor in laundry.GetComponentsInChildren<Transform>(true))
            {
                if (!anchor.name.StartsWith("IndustrialTaskLight_Laundry", StringComparison.Ordinal)) continue;
                fixtures++;
                Light leased = null;
                foreach (Light light in night.StreetLightPool)
                    if ((light.transform.position - anchor.position).sqrMagnitude < .0001f) { leased = light; break; }
                Assert.That(leased, Is.Not.Null, anchor.name + " must receive a nearby pooled lease.");
                Assert.That(leased.shadows, Is.EqualTo(LightShadows.Soft), "Room/roof must occlude their own practical sources.");
                Assert.That(leased.spotAngle, Is.GreaterThanOrEqualTo(160f));
                Assert.That(leased.innerSpotAngle, Is.LessThanOrEqualTo(25f), "Diffusers retain a broad smooth falloff.");
                string suffix = anchor.name.Substring("IndustrialTaskLight_Laundry".Length);
                Renderer lens = CityLaundryAssetProvider.FindPart(laundry.ModelRoot, "LaundryLampLens" + suffix).GetComponent<Renderer>();
                Assert.That(Vector3.Distance(leased.transform.position, lens.bounds.center), Is.LessThan(.01f),
                    "Light must originate at the visible diffuser, not below the fixture.");
                Assert.That(Object.FindAnyObjectByType<CityLightingPresentation>().IsVolumeAllowed(leased), Is.False,
                    "Frosted laundry fixtures must not project a detached theatrical air beam.");
                Assert.That(leased.GetUniversalAdditionalLightData().additionalLightsShadowResolutionTier,
                    Is.EqualTo(UniversalAdditionalLightData.AdditionalLightsShadowResolutionTierLow));
                if (anchor.name.EndsWith("Entrance", StringComparison.Ordinal))
                { Assert.That(leased.intensity, Is.LessThanOrEqualTo(.16f)); Assert.That(leased.range, Is.EqualTo(4f)); }
            }
            Assert.That(fixtures, Is.EqualTo(4));
            Renderer sign = CityLaundryAssetProvider.FindPart(laundry.ModelRoot, "LaundrySignArtwork__Sign").GetComponent<Renderer>();
            Assert.That(sign.sharedMaterial.shader.name, Is.EqualTo("Bar Promenade/City Surface"));
            Assert.That(sign.sharedMaterial.GetTexture("_BaseMap").name, Is.EqualTo("LaundrySign"));
            Assert.That(sign.sharedMaterial.GetTexture("_SceneMaterialResponse"), Is.Not.Null);
            Assert.That(sign.sharedMaterial.IsKeywordEnabled("_EMISSION"), Is.False);
            Mesh signMesh = sign.GetComponent<MeshFilter>().sharedMesh;
            Vector3[] signVertices = signMesh.vertices; Vector2[] signUv = signMesh.uv;
            for (int i = 0; i < signVertices.Length; i++)
            {
                Vector3 metre = laundry.Plan.InverseWorld(sign.transform.TransformPoint(signVertices[i]));
                // A viewer outside +Z sees local -X to their right. The
                // Russian word must read left-to-right on that visible face.
                Assert.That(signUv[i].x, Is.EqualTo(Mathf.InverseLerp(-1.54f, -3.76f, metre.x)).Within(.005f),
                    "The laundry sign must not be mirrored from the public frontage.");
            }
            foreach (Renderer renderer in laundry.ModelRoot.GetComponentsInChildren<Renderer>(true))
            {
                if (!renderer.name.StartsWith("LaundryFloor", StringComparison.Ordinal)) continue;
                var properties = new MaterialPropertyBlock(); renderer.GetPropertyBlock(properties);
                Assert.That(properties.GetFloat("_GroundCityClimate"), Is.Zero, "Roof-covered floor stays dry in rain.");
                Assert.That(properties.GetTexture("_GroundResponse"), Is.Not.Null);
            }
        }

        private static void AssertLaundryFrameColours()
        {
            var image = new Texture2D(2, 2, TextureFormat.RGB24, false);
            try
            {
                foreach (string name in new[] { "laundry-day-street", "laundry-day-folding", "laundry-night-street" })
                {
                    Assert.That(image.LoadImage(File.ReadAllBytes(Path.Combine(Directory.GetCurrentDirectory(),
                        "Captures", SceneIds.City, name + ".png"))), Is.True);
                    int magenta = 0;
                    foreach (Color32 pixel in image.GetPixels32())
                        if (pixel.r > 160 && pixel.b > 160 && pixel.g < 140 && pixel.r - pixel.g > 65 &&
                            pixel.b - pixel.g > 65 && Mathf.Abs(pixel.r - pixel.b) < 32) magenta++;
                    Assert.That(magenta, Is.LessThan(128), name + " contains an unsupported shader draw.");
                }
            }
            finally { Object.DestroyImmediate(image); }
        }
    }
}

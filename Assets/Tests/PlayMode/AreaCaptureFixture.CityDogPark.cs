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
        [Explicit("One residential dog park: reserved public approach, imported metres, three moving dogs, two permanent owners, pause/distance lifecycle and focused native views.")]
        public IEnumerator CityDogPark()
        {
#if UNITY_EDITOR
            Type.GetType("BarPromenade.Editor.CityDogParkAssetSetup, BarPromenade.Editor", true)
                .GetMethod("ValidateOrThrow", Type.EmptyTypes).Invoke(null, null);
#endif
            GameSessionState.BeginNewGame();
            Assert.That(GameSessionState.TryStartGameTimeAt(12 * 60), Is.True);
            CityLayout layout = CityLayoutCache.GetOrGenerate(CityBlueprintCatalog.Resolve(GameSessionState.CityBlueprintId),
                CityGenerationSettings.Default, GameSessionState.CitySeed);
            CityLaundryPlan.ForLayout(layout).ValidateOrThrow(layout);
            CityDogParkPlan planned = CityDogParkPlan.ForLayout(layout);
            Assert.That(planned.IsEnabled, Is.True, "The default residential city must contain the approved dog park.");
            planned.ValidateOrThrow(layout);
            CityGameRoot city = null;
            CityDogParkController park = null;
            PlayerCameraFollow follow = null;
            GameObject grayOwner = null, observer = null;
            VolumeProfile grayProfile = null;
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
                    var parks = Object.FindObjectsByType<CityDogParkController>();
                    Assert.That(parks.Length, Is.EqualTo(1));
                    park = parks[0];
                    Assert.That(park.Plan, Is.SameAs(planned));
                    Assert.That(park.Dogs.Count, Is.EqualTo(3));
                    Assert.That(park.Owners.Count, Is.EqualTo(2));
                    park.AutoAdvance = false;
                    park.ForcePresentation = true;
                    park.ApplyAt(0d);
                    AssertDogParkImportedActors(park);
                    AssertDogParkAccess(city, park);
                    AssertDogParkMotion(park);
                    follow = city.Camera.GetComponent<PlayerCameraFollow>();
                    followEnabled = follow != null && follow.enabled;
                    if (follow != null) follow.enabled = false;
                    weatherEnabled = city.Weather.enabled;
                    city.Weather.enabled = false;
                    city.Player.Motor.SetInputEnabled(false);
                    grayOwner = new GameObject("Dog park acceptance grayscale");
                    Volume gray = grayOwner.AddComponent<Volume>();
                    gray.isGlobal = true; gray.priority = 10000f; gray.weight = 0f;
                    grayProfile = ScriptableObject.CreateInstance<VolumeProfile>();
                    grayProfile.Add<ColorAdjustments>(true).saturation.Override(-100f);
                    gray.sharedProfile = grayProfile;
                    return DogParkShots(city, park, gray);
                });

                AssertDogParkFrames();
                GameSessionState.UpdateDrinkingProgress(0, DrinkId.LightBeer, 0);
                park.AutoAdvance = false;
                int movingMinute = 12 * 60;
                double movingTime = 0d;
                bool foundMovingPhase = false;
                for (; movingMinute < 13 * 60; movingMinute++)
                {
                    movingTime = (GameSessionState.GameDayIndex * 1440d + movingMinute) /
                        GameTimeState.GameMinutesPerRealSecond;
                    for (int dog = 0; dog < park.Dogs.Count; dog++)
                        foundMovingPhase |= CityDogActivity.Evaluate(dog, movingTime).Speed > .2f;
                    if (foundMovingPhase) break;
                }
                Assert.That(foundMovingPhase, Is.True, "The ordinary calendar contains an active walking phase.");
                SetChurchRemodelTime(movingMinute); city.DayNight.ApplyCurrentTime(true);
                park.ApplyAt(movingTime);
                var movingFrom = new Vector3[park.Dogs.Count];
                for (int dog = 0; dog < movingFrom.Length; dog++) movingFrom[dog] = park.Dogs[dog].position;
                park.AutoAdvance = true;
                for (int frame = 0; frame < 10; frame++)
                {
                    yield return null;
                    AssertDogParkRootsInBounds(park);
                }
                float actualMovement = 0f;
                for (int dog = 0; dog < movingFrom.Length; dog++)
                    actualMovement += Vector3.Distance(movingFrom[dog], park.Dogs[dog].position);
                Assert.That(actualMovement, Is.GreaterThan(.01f),
                    "Live movement must survive the physical capsule casts, rather than only deterministic posing.");
                using (GameTimeScaleRuntime.AcquirePause())
                {
                    yield return null;
                    double elapsed = park.ElapsedSeconds;
                    var positions = new Vector3[park.Dogs.Count];
                    var rotations = new Quaternion[park.Dogs.Count];
                    for (int i = 0; i < positions.Length; i++)
                    { positions[i] = park.Dogs[i].position; rotations[i] = park.Dogs[i].rotation; }
                    yield return null;
                    Assert.That(park.ElapsedSeconds, Is.EqualTo(elapsed));
                    for (int i = 0; i < positions.Length; i++)
                    {
                        Assert.That(Vector3.Distance(park.Dogs[i].position, positions[i]), Is.LessThan(.0001f));
                        Assert.That(Quaternion.Angle(park.Dogs[i].rotation, rotations[i]), Is.LessThan(.001f));
                    }
                    park.gameObject.SetActive(false);
                    foreach (AudioSource source in park.GetComponentsInChildren<AudioSource>(true))
                        Assert.That(source.isPlaying, Is.False, "Disabled dogs release their positioned voices.");
                    park.gameObject.SetActive(true);
                    yield return null;
                    Assert.That(park.ElapsedSeconds, Is.EqualTo(elapsed));
                    for (int i = 0; i < positions.Length; i++)
                        Assert.That(Vector3.Distance(park.Dogs[i].position, positions[i]), Is.LessThan(.0001f),
                            "Re-enabling during pause must retain the dog's place and phase.");
                }

                string firstOwner = park.Owners[0].GetComponent<DefaultNpcAppearance>().AppearanceKey;
                observer = new GameObject("Dog park distance acceptance observer");
                park.ForcePresentation = false;
                park.PresentationObserver = observer.transform;
                observer.transform.position = park.Plan.Position + new Vector3(180f, 0f, 180f);
                yield return null;
                double farTime = park.ElapsedSeconds;
                yield return null;
                Assert.That(park.IsPresentationActive, Is.False);
                Assert.That(park.ElapsedSeconds, Is.GreaterThan(farTime), "Distance preserves authoritative dog time.");
                Assert.That(park.Owners[0].GetComponent<DefaultNpcAppearance>().AppearanceKey, Is.EqualTo(firstOwner));
                observer.transform.position = park.Plan.Position;
                yield return null;
                Assert.That(park.IsPresentationActive, Is.True);
                Assert.That(park.Owners[0].GetComponent<DefaultNpcAppearance>().AppearanceKey, Is.EqualTo(firstOwner));
                AssertDogParkRootsInBounds(park);
                Assert.That(park.GetComponentsInChildren<Light>(true), Is.Empty,
                    "The dog park uses the existing city light profile and pool.");
                Debug.Log("CITY DOG PARK ACCEPTANCE OK: reserved residential infill, clear entrance, imported metre-sized dogs, permanent owners, bounded independent movement, pause/disable/distance continuity and focused day/night/state views.");
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

        private static void AssertDogParkImportedActors(CityDogParkController park)
        {
            var identities = new HashSet<string>();
            foreach (Transform owner in park.Owners)
            {
                DefaultNpcAppearance appearance = owner.GetComponent<DefaultNpcAppearance>();
                Assert.That(appearance, Is.Not.Null);
                Assert.That(identities.Add(appearance.AppearanceKey), Is.True,
                    "Both owners retain separate global population identities.");
            }
            foreach (Transform dog in park.Dogs)
            {
                Renderer[] renderers = dog.GetComponentsInChildren<Renderer>(true);
                Assert.That(renderers, Is.Not.Empty);
                Bounds bounds = renderers[0].bounds;
                foreach (Renderer renderer in renderers)
                {
                    bounds.Encapsulate(renderer.bounds);
                    foreach (Material material in renderer.sharedMaterials)
                    {
                        Assert.That(material, Is.Not.Null);
                        Assert.That(material.shader.isSupported, Is.True);
                        Assert.That(material.name.EndsWith("(Instance)", StringComparison.Ordinal), Is.False,
                            "Each dog shares imported materials rather than cloning them.");
                    }
                }
                Assert.That(bounds.size.y, Is.InRange(.18f, 1.6f), "Dog meshes must retain authored metres after FBX import.");
                Assert.That(Mathf.Max(bounds.size.x, bounds.size.z), Is.InRange(.3f, 2.8f));
                Assert.That(dog.GetComponentsInChildren<Transform>(true).Length, Is.GreaterThan(14),
                    "Four articulated legs, torso/head and tail belong to the visible animal.");
            }
        }

        private static void AssertDogParkMotion(CityDogParkController park)
        {
            var previous = new Vector3[park.Dogs.Count];
            var travelled = new float[park.Dogs.Count];
            park.ApplyAt(0d);
            for (int i = 0; i < previous.Length; i++) previous[i] = park.Dogs[i].position;
            for (double time = .5d; time <= 60d; time += .5d)
            {
                park.ApplyAt(time);
                AssertDogParkRootsInBounds(park);
                Assert.That(park.MaximumPawContactError, Is.LessThan(.06f),
                    "Four-leg IK must reach the authored paw contacts at " + time + " seconds.");
                for (int i = 0; i < previous.Length; i++)
                {
                    float step = Vector3.Distance(park.Dogs[i].position, previous[i]);
                    Assert.That(step, Is.LessThan(3f), "The dog moves continuously instead of teleporting between actions.");
                    travelled[i] += step; previous[i] = park.Dogs[i].position;
                }
            }
            foreach (float distance in travelled)
                Assert.That(distance, Is.GreaterThan(1f), "Every visible dog has a real walking phase.");
            park.ApplyAt(0d);
        }

        private static void AssertDogParkRootsInBounds(CityDogParkController park)
        {
            foreach (Transform dog in park.Dogs)
            {
                Vector3 point = park.Plan.InverseWorld(dog.position);
                Assert.That(park.Plan.LocalInteriorBounds.Contains(new Vector2(point.x, point.z)), Is.True,
                    "The dog's body remains within its fenced local movement area.");
                Assert.That(float.IsNaN(point.y) || float.IsInfinity(point.y), Is.False);
            }
        }

        private static void AssertDogParkAccess(CityGameRoot city, CityDogParkController park)
        {
            Physics.SyncTransforms();
            foreach (Rect solid in park.Plan.SolidFootprints)
                Assert.That(city.World.WalkableArea.Contains(new Vector3(solid.center.x, park.Plan.Position.y, solid.center.y), .2f),
                    Is.False, "The physical dog-park fence also participates in shared navigation.");
            for (int leg = 0; leg + 1 < park.Plan.AccessPath.Count; leg++)
            {
                Vector3 start = park.Plan.AccessPath[leg], end = park.Plan.AccessPath[leg + 1];
                int steps = Mathf.Max(1, Mathf.CeilToInt(Vector3.Distance(start, end) / .5f));
                for (int step = 0; step <= steps; step++)
                {
                    Vector3 point = Vector3.Lerp(start, end, step / (float)steps);
                    Assert.That(city.World.WalkableArea.Contains(point, .25f), Is.True,
                        "The existing public route connects to the open dog-park entrance.");
                    Assert.That(Physics.Raycast(point + Vector3.up * 1.3f, Vector3.down,
                        out RaycastHit floor, 3f, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore), Is.True);
                    Assert.That(floor.normal.y, Is.GreaterThan(.8f));
                }
            }
        }

        private static Shot[] DogParkShots(CityGameRoot city, CityDogParkController park, Volume gray)
        {
            var shots = new List<Shot>();
            float front = CityDogParkPlan.Depth * .5f;
            float side = CityDogParkPlan.Width * .5f;
            Add("dog-park-day-street", new Vector3(0f, 2.5f, front + 4f), new Vector3(0f, .65f, 0f), 64f, 3d);
            Add("dog-park-dogs-close", new Vector3(-side + 1f, 1.15f, front - 1.2f), new Vector3(0f, .45f, 0f), 62f, 9d);
            Add("dog-park-moving", new Vector3(side - 1f, 1.5f, front - 1.2f), new Vector3(0f, .45f, -.5f), 64f, 21d);
            Add("dog-park-night-street", new Vector3(0f, 2.5f, front + 4f), new Vector3(0f, .65f, 0f), 64f, 3d, night: true);
            Add("dog-park-grayscale", new Vector3(0f, 2.5f, front + 4f), new Vector3(0f, .65f, 0f), 64f, 3d, grayscale: true);
            Add("dog-park-intoxication", new Vector3(0f, 2.5f, front + 4f), new Vector3(0f, .65f, 0f), 64f, 3d, intoxicated: true);
            return shots.ToArray();
            void Add(string name, Vector3 from, Vector3 target, float field, double phase,
                bool night = false, bool grayscale = false, bool intoxicated = false)
            {
                Shot pose = Shot.At(name, park.Plan.World(from), park.Plan.World(target), field);
                int frames = 0;
                shots.Add(Shot.At(name, pose.Position, pose.Target, field, readyWhen: () =>
                {
                    if (frames++ == 0)
                    {
                        SetChurchRemodelTime((night ? 21 : 12) * 60); city.DayNight.ApplyCurrentTime(true);
                        gray.weight = grayscale ? 1f : 0f;
                        float intensity = CityEternalRainShaper.FloorIntensity(0f);
                        city.Rain.SetIntensity(intensity); CityWaterResources.SetRainIntensity(intensity);
                        CityWetSurfaceRegistry.SetImmediate(intensity);
                        GameSessionState.UpdateDrinkingProgress(intoxicated ? 100 : 0, DrinkId.LightBeer, intoxicated ? 1 : 0);
                        MoveCityLightingObserver(city, pose); park.ApplyAt(phase);
                    }
                    return frames > (intoxicated ? 60 : SettleFrames);
                }));
            }
        }

        private static void AssertDogParkFrames()
        {
            var image = new Texture2D(2, 2, TextureFormat.RGB24, false);
            try
            {
                foreach (string name in new[] { "dog-park-day-street", "dog-park-dogs-close", "dog-park-night-street" })
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

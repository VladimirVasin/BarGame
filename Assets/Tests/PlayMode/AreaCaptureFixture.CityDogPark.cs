using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
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

        [UnityTest]
        [Explicit("Ground contacts of three dogs, sniffing, bench support and two-way entry.")]
        public IEnumerator CityDogParkFittings()
        {
            GameSessionState.BeginNewGame();
            Assert.That(GameSessionState.TryStartGameTimeAt(720), Is.True);
            CityGameRoot city = null;
            CityDogParkController park = null;
            PlayerCameraFollow follow = null;
            bool followEnabled = false, weatherEnabled = false, motorEnabled = false;
            try
            {
                yield return Capture(SceneIds.City, () =>
                {
                    city = Object.FindAnyObjectByType<CityGameRoot>();
                    return city && city.IsInitialized && !CompositionDriver.IsComposing ? city : null;
                }, () =>
                {
                    park = Object.FindAnyObjectByType<CityDogParkController>();
                    park.AutoAdvance = false;
                    park.ForcePresentation = true;
                    follow = city.Camera.GetComponent<PlayerCameraFollow>();
                    followEnabled = follow.enabled;
                    follow.enabled = false;
                    weatherEnabled = city.Weather.enabled;
                    city.Weather.enabled = false;
                    motorEnabled = city.Player.Motor.InputEnabled;
                    city.Player.Motor.SetInputEnabled(false);
                    SetChurchRemodelTime(720);
                    city.DayNight.ApplyCurrentTime(true);
                    return new[]
                    {
                        Frame("entry", park.transform, new Vector3(.32f,1.72f,8.6f), new Vector3(-.4f,.57f,-.55f), 57f, 8d),
                        Frame("terrier", park.Dogs[0], new Vector3(.89f,.35f,.72f), new Vector3(0,.25f,.05f), 40f, 8d),
                        Frame("terrier-profile", park.Dogs[0], new Vector3(-.91f,.37f,.68f), new Vector3(0,.26f,.08f), 39f, 13d),
                        Frame("sniff", park.Dogs[1], new Vector3(1.32f,.47f,.92f), new Vector3(0,.37f,.12f), 44f, 9d),
                        Frame("large", park.Dogs[2], new Vector3(1.83f,.60f,.81f), new Vector3(0,.43f,.04f), 44f, 24d),
                        Frame("bench", park.transform, new Vector3(-.95f,1.15f,-2.48f), new Vector3(-2.57f,.73f,-4.10f), 52f, 8d)
                    };
                    Shot Frame(string name, Transform basis, Vector3 eye, Vector3 target, float fov, double phase)
                    {
                        park.ApplyAt(phase);
                        Shot pose = Shot.At("dog-fittings-" + name, basis.TransformPoint(eye), basis.TransformPoint(target), fov);
                        int frames = 0;
                        return Shot.At(pose.Name, pose.Position, pose.Target, fov, readyWhen: () =>
                        {
                            if (frames++ == 0) MoveCityLightingObserver(city, pose);
                            park.ApplyAt(phase);
                            return frames > SettleFrames;
                        });
                    }
                });
                VerifyDogParkContacts(park);
                yield return WalkDogParkEntrance(city,park);
                VerifyDogParkOwnerSeat(park);
            }
            finally
            {
                if (park) { park.AutoAdvance = true; park.ForcePresentation = false; }
                if (follow) follow.enabled = followEnabled;
                if (city && follow) { city.Weather.enabled = weatherEnabled; city.Player.Motor.SetInputEnabled(motorEnabled); }
            }
        }

        private static void VerifyDogParkContacts(CityDogParkController park)
        {
            string[] legs = { "FL", "FR", "BL", "BR" };
            float ground = CityDogParkAssetProvider.FindPart(park.transform, "ParkGround__Ground")
                .GetComponent<Renderer>().bounds.max.y;
            for (int dog = 0; dog < park.Dogs.Count; dog++)
            {
                Transform host = park.Dogs[dog];
                Transform nose = CityDogParkAssetProvider.FindPart(host,"ANCHOR_Muzzle");
                var feet = legs.Select(id => CityDogParkAssetProvider.FindPart(host,"DogPaw"+id+"__Plain")
                    .GetComponent<MeshFilter>()).ToArray();
                var vertices = feet.Select(f => f.sharedMesh.vertices).ToArray();
                bool[] planted = new bool[4], swung = new bool[4];
                bool sniffed = false;
                for (double t = 0; t <= 73; t += .25)
                {
                    park.ApplyAt(t);
                    var state = CityDogActivity.Evaluate(dog,t);
                    int supports = 0;
                    for (int leg = 0; leg < feet.Length; leg++)
                    {
                        var matrix = feet[leg].transform.localToWorldMatrix;
                        float sole = float.PositiveInfinity;
                        foreach (Vector3 v in vertices[leg]) sole = Mathf.Min(sole,matrix.MultiplyPoint3x4(v).y-ground);
                        Assert.That(sole,Is.GreaterThan(-.012f),host.name+" sole penetrates earth at "+t);
                        if (sole < .02f) { supports++; planted[leg] |= state.Speed > .15f; }
                        swung[leg] |= sole > .027f && state.Speed > .15f;
                    }
                    Assert.That(supports,Is.GreaterThanOrEqualTo(2),host.name+" grounded paws at "+t);
                    if (state.Sniff > .60f && state.Speed < .01f)
                    {
                        Assert.That(nose.position.y-ground,Is.InRange(.009f,.052f),host.name+" nose over earth at "+t);
                        sniffed = true;
                    }
                }
                Assert.That(planted.All(x => x) && swung.All(x => x) && sniffed, Is.True, host.name);
            }
        }

        private static void VerifyDogParkOwnerSeat(CityDogParkController park)
        {
            string[] ids = { DefaultNpcPopulation.CityDogParkStandingOwner, DefaultNpcPopulation.CityDogParkSeatedOwner };
            for (int i=0; i<ids.Length; i++)
            {
                var assignment = DefaultNpcPopulation.GetAssignment(ids[i]);
                Assert.That(assignment.ModelId, Is.EqualTo(DefaultNpcCatalog.OrdinaryWorker));
                var expected = DefaultNpcCatalog.GetPrefab(assignment.ModelId).GetComponentsInChildren<SkinnedMeshRenderer>(true)
                    .Select(s => s.sharedMesh).ToHashSet();
                var owner = park.Owners[i].GetComponent<DefaultNpcAppearance>();
                Assert.That(owner.AppearanceKey, Is.EqualTo(ids[i]));
                Assert.That(owner.CurrentFaceId, Is.EqualTo(assignment.FaceId));
                Assert.That(owner.CurrentHairColorId, Is.EqualTo(assignment.HairColorId));
                Assert.That(owner.GetComponentsInChildren<SkinnedMeshRenderer>(true).Any(s =>
                    s.enabled && s.gameObject.activeInHierarchy && expected.Contains(s.sharedMesh)), Is.True);
            }
            Transform seat = CityDogParkAssetProvider.FindPart(park.transform,"ANCHOR_BenchSeat");
            var bench = CityDogParkAssetProvider.FindPart(park.transform,"ParkBench__Plain").GetComponent<MeshFilter>();
            float top = bench.sharedMesh.vertices.Select(v => park.Plan.InverseWorld(bench.transform.TransformPoint(v)))
                .Where(v => v.z > -4.39f && v.z < -3.85f && v.y < .465f).Max(v => v.y);
            Assert.That(top,Is.EqualTo(park.Plan.InverseWorld(seat.position).y).Within(.006f));
            var baked = new Mesh();
            try
            {
                foreach (double t in new[] { 0d, 9d, 27d })
                {
                    park.ApplyAt(t);
                    float clearance = float.PositiveInfinity;
                    foreach (var skin in park.Owners[1].GetComponentsInChildren<SkinnedMeshRenderer>())
                    {
                        if (!skin.enabled || !skin.sharedMesh) continue;
                        skin.BakeMesh(baked, true);
                        foreach (Vector3 v in baked.vertices)
                        {
                            Vector3 point = Quaternion.Inverse(park.Plan.Rotation) * (skin.localToWorldMatrix.MultiplyPoint3x4(v) - seat.position);
                            if (Mathf.Abs(point.x) < .24f && point.z > -.23f && point.z < .095f)
                                clearance = Mathf.Min(clearance,point.y);
                        }
                    }
                    Assert.That(clearance,Is.InRange(-.022f,.021f),"Clothing on seat slats at "+t);
                }
            }
            finally { Object.DestroyImmediate(baked); }
        }

        private static IEnumerator WalkDogParkEntrance(CityGameRoot city, CityDogParkController park)
        {
            var motor = city.Player.Motor;
            Vector3 offset = Vector3.up * PlayerFactory.GroundedRootOffset;
            var path = new[] { park.Plan.ApproachWorld, park.Plan.EntranceWorld, park.Plan.World(new Vector3(0,0,3.65f)) };
            motor.Teleport(path[0] + offset);
            Physics.SyncTransforms();
            for (int pass=0; pass<2; pass++)
            for (int i=1; i<path.Length; i++)
            {
                Vector3 target = path[pass==0 ? i : path.Length-1-i] + offset;
                bool reached = false;
                for (int step=0; step<90 && !reached; step++)
                {
                    reached = motor.MoveTowardsApproachWaypoint(target,.065f,.04f);
                    if (step%3 == 0) yield return null;
                }
                Assert.That(reached,Is.True,"Player crosses dog yard entrance, direction "+pass);
            }
            motor.CancelInteractionPoseMove();
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

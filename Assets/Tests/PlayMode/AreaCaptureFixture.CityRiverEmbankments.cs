using System;
using System.Collections;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

namespace BarPromenade.Tests.PlayMode
{
    public sealed class RiverEmbankmentAssetsSetup : IPrebuildSetup
    {
        public void Setup()
        {
#if UNITY_EDITOR
            new CompactCityAssetsSetup().Setup();
            Type.GetType("BarPromenade.Editor.CityMiscAssetSetup, BarPromenade.Editor", true)
                .GetMethod("BuildOrThrow", Type.EmptyTypes).Invoke(null, null);
#endif
        }
    }

    public sealed partial class AreaCaptureFixture
    {
        [UnityTest]
        [Explicit("Focused river: imported metre-scale details, supported clear routes, low lanterns and day/night/rain/grayscale/intoxication frames.")]
        [PrebuildSetup(typeof(RiverEmbankmentAssetsSetup))]
        public IEnumerator CityRiverEmbankments()
        {
            string[] keys = { "graphics.clean_aa", "graphics.clean_reflections", "graphics.clean_volume",
                "graphics.clean_halation", "graphics.clean_directional_highlights", "graphics.clean_bounce",
                "graphics.dof", "graphics.intoxication_fx", "graphics.aspect_4_3" };
            var preferences = new Dictionary<string, int?>();
            foreach (string key in keys)
                preferences[key] = PlayerPrefs.HasKey(key) ? PlayerPrefs.GetInt(key) : (int?)null;
            float previousDelta = Time.captureDeltaTime;
            CityGameRoot city = null;
            GameTimeRuntime clock = null;
            PlayerCameraFollow follow = null;
            bool clockEnabled = false, followEnabled = false;
            VolumeProfile grayscale = null;
            GameObject grayscaleOwner = null;
            try
            {
                Time.captureDeltaTime = 1f / 30f;
                GameSessionState.BeginNewGame();
                Assert.That(GameSessionState.TryStartGameTimeAt(12 * 60), Is.True);
                GraphicsEffectsSettings.CleanAntialiasingEnabled = true;
                GraphicsEffectsSettings.DepthOfFieldEnabled = false;
                GraphicsEffectsSettings.IntoxicationLensFxEnabled = true;
                GraphicsEffectsSettings.AspectRatio43Enabled = false;
                SetCityNativeEffects(true);
                yield return Capture(SceneIds.City, () =>
                {
                    city = Object.FindAnyObjectByType<CityGameRoot>();
                    return city != null && city.IsInitialized && !CompositionDriver.IsComposing ? city : null;
                }, () =>
                {
                    clock = Object.FindAnyObjectByType<GameTimeRuntime>();
                    clockEnabled = clock != null && clock.enabled;
                    if (clock != null) clock.enabled = false;
                    follow = city.Camera.GetComponent<PlayerCameraFollow>();
                    followEnabled = follow != null && follow.enabled;
                    if (follow != null) follow.enabled = false;
                    city.Player.Motor.SetInputEnabled(false);
                    city.DayNight.enabled = false;
                    city.Weather.enabled = false;
                    Physics.SyncTransforms();
                    Assert.That(city.World.RiverRoot, Is.Not.Null);
                    Transform river = city.World.RiverRoot.transform;
                    AssertRiverConstructionKit(city, river);
                    AssertRiverRoutes(city, river);
                    AssertRiverLanterns(city, river);
                    grayscaleOwner = new GameObject("River Acceptance Grayscale");
                    Volume volume = grayscaleOwner.AddComponent<Volume>();
                    volume.isGlobal = true;
                    volume.priority = 10000f;
                    volume.weight = 0f;
                    grayscale = ScriptableObject.CreateInstance<VolumeProfile>();
                    grayscale.Add<ColorAdjustments>(true).saturation.Override(-100f);
                    volume.sharedProfile = grayscale;
                    return RiverEmbankmentShots(city, volume);
                });
                yield return SceneManager.LoadSceneAsync(SceneIds.MainMenu, LoadSceneMode.Single);
                AssertCityLightingGlobalsCleared();
                Debug.Log("RIVER EMBANKMENTS ACCEPTANCE OK: placed imported details, supported clear banks/bridges/steps, constant lanterns, bounded effects and weather/state views.");
            }
            finally
            {
                if (grayscaleOwner != null) Object.DestroyImmediate(grayscaleOwner);
                if (grayscale != null) Object.DestroyImmediate(grayscale);
                if (clock != null) clock.enabled = clockEnabled;
                if (follow != null) follow.enabled = followEnabled;
                if (city != null)
                {
                    city.DayNight.enabled = true;
                    city.Weather.enabled = true;
                    city.Player.Motor.SetInputEnabled(true);
                }
                foreach (KeyValuePair<string, int?> preference in preferences)
                    if (preference.Value.HasValue) PlayerPrefs.SetInt(preference.Key, preference.Value.Value);
                    else PlayerPrefs.DeleteKey(preference.Key);
                PlayerPrefs.Save();
                GraphicsEffectsSettings.ResetLoadedStateForTests();
                Time.captureDeltaTime = previousDelta;
                GameSessionState.BeginNewGame();
            }
        }

        private static void AssertRiverConstructionKit(CityGameRoot city, Transform river)
        {
            CityRiverDressingPlan plan = CityRiverDressingPlan.Create(city.Layout);
            Assert.That(plan.Placements, Is.Not.Empty);
            Assert.That(plan.Placements.Count, Is.LessThanOrEqualTo(CityRiverDressingPlan.MaximumPlacementCount));
            Transform root = river.Find(CityRiverDressingWorldBuilder.RootName);
            Assert.That(root, Is.Not.Null);
            Assert.That(root.GetComponentsInChildren<Light>(true), Is.Empty);
            Assert.That(root.GetComponentsInChildren<CityLightHalo>(true), Is.Empty);
            Assert.That(root.GetComponentsInChildren<Collider>(true).Length, Is.EqualTo(8),
                "Only the two physical bollards on each lower landing add collision.");
            CityMiscAssetProvider provider = CityMiscAssetProvider.LoadOrThrow();
            var expected = new Dictionary<string, Bounds>();
            var kinds = new HashSet<CityMiscKind>();
            var ids = new HashSet<string>();
            foreach (CityRiverDressingPlacement placement in plan.Placements)
            {
                Assert.That(ids.Add(placement.Id), Is.True, placement.Id);
                Assert.That(placement.Scale.x, Is.GreaterThan(0f));
                Assert.That(placement.Scale.y, Is.GreaterThan(0f));
                Assert.That(placement.Scale.z, Is.GreaterThan(0f));
                kinds.Add(placement.Kind);
                if (placement.Kind == CityMiscKind.RiverMooringBollard)
                {
                    Transform physical = root.Find(placement.Id);
                    Assert.That(physical, Is.Not.Null, placement.Id);
                    BoxCollider box = physical.GetComponent<BoxCollider>();
                    Assert.That(box, Is.Not.Null, placement.Id);
                    Assert.That(Vector3.Distance(box.bounds.center, placement.Position + Vector3.up * .26f), Is.LessThan(.002f));
                    Assert.That(Vector3.Distance(box.bounds.size, new Vector3(.44f, .52f, .44f)), Is.LessThan(.002f));
                }
                Matrix4x4 pose = Matrix4x4.TRS(placement.Position, placement.Rotation, placement.Scale);
                for (int index = 0; index < CityMiscAssetProvider.GetPartCount(placement.Kind); index++)
                {
                    CityMiscMeshPart part = provider.GetPartOrThrow(placement.Kind, 0, index);
                    Assert.That(part.Mesh, Is.Not.Null, placement.Id);
                    float sourceExtent = Mathf.Max(part.Mesh.bounds.size.x, part.Mesh.bounds.size.y, part.Mesh.bounds.size.z);
                    Assert.That(sourceExtent, Is.InRange(.04f, 1.5f),
                        placement.Id + ": the imported source must be in metres before runtime batching.");
                    string batch = placement.Kind == CityMiscKind.RiverStairNosing ? "River Details Paving" :
                        part.Surface == CityMiscSurfaceKind.Stone ? "River Details Quay" : "River Details Iron_PaintedMetal";
                    Bounds bounds = RiverTransformBounds(part.Mesh.bounds, pose);
                    if (expected.TryGetValue(batch, out Bounds accumulated))
                    {
                        accumulated.Encapsulate(bounds);
                        expected[batch] = accumulated;
                    }
                    else expected.Add(batch, bounds);
                }
            }
            Assert.That(kinds, Is.EquivalentTo(new[] { CityMiscKind.RiverQuayRepair, CityMiscKind.RiverWorksFitting,
                CityMiscKind.RiverMouthCap, CityMiscKind.RiverTimberJoint, CityMiscKind.RiverMooringBollard,
                CityMiscKind.RiverLanternShell, CityMiscKind.RiverStairNosing }));
            Renderer[] renderers = root.GetComponentsInChildren<Renderer>(true);
            Assert.That(renderers.Length, Is.EqualTo(expected.Count).And.LessThanOrEqualTo(CityRiverDressingWorldBuilder.MaximumBatchCount));
            foreach (Renderer renderer in renderers)
            {
                Assert.That(expected.TryGetValue(renderer.name, out Bounds bounds), Is.True, renderer.name);
                Assert.That(Vector3.Distance(renderer.bounds.center, bounds.center), Is.LessThan(.04f), renderer.name);
                Assert.That(Vector3.Distance(renderer.bounds.size, bounds.size), Is.LessThan(.08f),
                    renderer.name + ": measuring the placed mesh catches a correct import placed at 1/100 scale.");
                AssertRiverSurfaceMaps(renderer);
            }
            AssertRiverBankFootContacts(plan, provider, root);
        }

        private static void AssertRiverBankFootContacts(CityRiverDressingPlan plan,
            CityMiscAssetProvider provider, Transform details)
        {
            // Read the rendered stone, not a collider or the placement's
            // nominal datum: the imported coping has real top faces/bevels.
            MeshFilter coping = details.Find("River Details Quay").GetComponent<MeshFilter>();
            Vector3[] vertices;
            int[] indices;
#if UNITY_EDITOR
            // Production batches release their CPU copy after upload. The
            // editor probe can inspect that actual mesh without retaining it.
            using (Mesh.MeshDataArray data = UnityEditor.MeshUtility.AcquireReadOnlyMeshData(coping.sharedMesh))
            using (var positions = new Unity.Collections.NativeArray<Vector3>(data[0].vertexCount, Unity.Collections.Allocator.Temp))
            using (var triangles = new Unity.Collections.NativeArray<int>(data[0].GetSubMesh(0).indexCount, Unity.Collections.Allocator.Temp))
            {
                data[0].GetVertices(positions);
                data[0].GetIndices(triangles, 0);
                vertices = positions.ToArray();
                indices = triangles.ToArray();
            }
#else
            vertices = coping.sharedMesh.vertices;
            indices = coping.sharedMesh.triangles;
#endif
            var tops = new List<RiverSupportTriangle>(indices.Length / 6);
            for (int index = 0; index < indices.Length; index += 3)
            {
                Vector3 a = coping.transform.TransformPoint(vertices[indices[index]]);
                Vector3 b = coping.transform.TransformPoint(vertices[indices[index + 1]]);
                Vector3 c = coping.transform.TransformPoint(vertices[indices[index + 2]]);
                Vector3 normal = Vector3.Cross(b - a, c - a).normalized;
                if (normal.y > .99f) tops.Add(new RiverSupportTriangle(a, b, c, normal));
            }
            Assert.That(tops, Is.Not.Empty, "Imported coping needs actual upward support faces.");
            Mesh foot = provider.GetPartOrThrow(CityMiscKind.RiverWorksFitting, 0, 0).Mesh;
            Assert.That(foot.isReadable, Is.True, "Imported source parts remain readable for runtime combining.");
            var bottom = new HashSet<Vector3>();
            Vector3 centroid = Vector3.zero;
            foreach (Vector3 vertex in foot.vertices)
                if (vertex.y <= foot.bounds.min.y + .0001f && bottom.Add(vertex)) centroid += vertex;
            Assert.That(bottom.Count, Is.GreaterThanOrEqualTo(4), "Measure the imported plate's contact face.");
            centroid /= bottom.Count;
            bottom.Add(centroid);
            int checkedFeet = 0;
            foreach (CityRiverDressingPlacement placement in plan.Placements)
            {
                if (placement.Id.IndexOf("-rail-foot-", StringComparison.Ordinal) < 0) continue;
                checkedFeet++;
                Matrix4x4 pose = Matrix4x4.TRS(placement.Position, placement.Rotation, placement.Scale);
                foreach (Vector3 local in bottom)
                {
                    Vector3 contact = pose.MultiplyPoint3x4(local);
                    bool found = false;
                    float topY = float.NegativeInfinity;
                    Vector3 topNormal = Vector3.up;
                    foreach (RiverSupportTriangle triangle in tops)
                        if (triangle.TryHeight(contact, out float y) && y > topY)
                        {
                            found = true;
                            topY = y;
                            topNormal = triangle.Normal;
                        }
                    Assert.That(found, Is.True, placement.Id + ": base footprint overhangs the actual imported coping at " + contact);
                    Assert.That(contact.y - topY, Is.InRange(-.002f, .008f),
                        placement.Id + ": plate bottom must meet visible coping instead of sinking into it or floating; point=" + contact);
                    Assert.That(Vector3.Angle(pose.MultiplyVector(Vector3.up), topNormal), Is.LessThan(.1f),
                        placement.Id + ": the complete contact plane must follow the actual coping slope.");
                }
            }
            Assert.That(checkedFeet, Is.GreaterThan(0));
        }

        private readonly struct RiverSupportTriangle
        {
            private readonly Vector3 a, b, c;
            private readonly Rect bounds;
            internal readonly Vector3 Normal;

            internal RiverSupportTriangle(Vector3 a, Vector3 b, Vector3 c, Vector3 normal)
            {
                this.a = a; this.b = b; this.c = c; Normal = normal;
                bounds = Rect.MinMaxRect(Mathf.Min(a.x, b.x, c.x), Mathf.Min(a.z, b.z, c.z),
                    Mathf.Max(a.x, b.x, c.x), Mathf.Max(a.z, b.z, c.z));
            }

            internal bool TryHeight(Vector3 point, out float y)
            {
                y = 0f;
                if (point.x < bounds.xMin - .0001f || point.x > bounds.xMax + .0001f ||
                    point.z < bounds.yMin - .0001f || point.z > bounds.yMax + .0001f) return false;
                float denominator = (b.z - c.z) * (a.x - c.x) + (c.x - b.x) * (a.z - c.z);
                if (Mathf.Abs(denominator) < .0000001f) return false;
                float first = ((b.z - c.z) * (point.x - c.x) + (c.x - b.x) * (point.z - c.z)) / denominator;
                float second = ((c.z - a.z) * (point.x - c.x) + (a.x - c.x) * (point.z - c.z)) / denominator;
                float third = 1f - first - second;
                if (first < -.0001f || second < -.0001f || third < -.0001f) return false;
                y = first * a.y + second * b.y + third * c.y;
                return true;
            }
        }

        private static Bounds RiverTransformBounds(Bounds source, Matrix4x4 pose)
        {
            var result = new Bounds(pose.MultiplyPoint3x4(source.center), Vector3.zero);
            for (int x = -1; x <= 1; x += 2)
                for (int y = -1; y <= 1; y += 2)
                    for (int z = -1; z <= 1; z += 2)
                        result.Encapsulate(pose.MultiplyPoint3x4(source.center +
                            Vector3.Scale(source.extents, new Vector3(x, y, z))));
            return result;
        }

        private static void AssertRiverSurfaceMaps(Renderer renderer)
        {
            var block = new MaterialPropertyBlock();
            renderer.GetPropertyBlock(block);
            Material material = renderer.sharedMaterial;
            Assert.That(material, Is.Not.Null, renderer.name);
            bool ground = renderer.name != "River Details Iron_PaintedMetal";
            string family = renderer.name == "River Details Quay" ? "CityRiverQuay" :
                renderer.name == "River Details Paving" ? "CityRiverPaving" : "CityRiverIron";
            CitySurfaceSheet sheet = null;
            foreach (CitySurfaceSheet candidate in CitySurfaceLibrary.Manifest.sheets)
                if (candidate.family == family) { sheet = candidate; break; }
            Assert.That(sheet, Is.Not.Null, renderer.name);
            // River stone keeps the shared layered-ground owner and its
            // wetness/ground response; only the iron uses City Surface.
            Assert.That(material.shader.name, Is.EqualTo(ground
                ? GroundSurfaceAppearance.SharedMaterial.shader.name : CitySurfaceLibrary.ShaderName), renderer.name);
            Assert.That(material.shader.isSupported, Is.True, renderer.name);
            int pigmentId = Shader.PropertyToID("_BaseMap");
            int responseId = Shader.PropertyToID(ground ? "_GroundResponse" : "_SceneMaterialResponse");
            Assert.That(block.GetTexture(pigmentId) ?? material.GetTexture(pigmentId),
                Is.SameAs(Resources.Load<Texture2D>(sheet.albedo)), renderer.name);
            Assert.That(block.GetTexture(responseId) ?? material.GetTexture(responseId),
                Is.SameAs(Resources.Load<Texture2D>(ground ? sheet.ground_response : sheet.response)), renderer.name);
            if (ground)
            {
                Assert.That(block.GetFloat("_GroundKind"), Is.EqualTo((float)GroundSurfaceKind.Paving), renderer.name);
                Assert.That(block.GetFloat("_GroundCityClimate"), Is.EqualTo(1f), renderer.name);
            }
        }

        private static void AssertRiverRoutes(CityGameRoot city, Transform river)
        {
            CityRiverPlan plan = city.Layout.River;
            Assert.That(plan.Definition.ChannelWidth, Is.EqualTo(10f));
            Assert.That(plan.Definition.PromenadeWidth, Is.EqualTo(3f));
            Assert.That(plan.Promenades.Count, Is.EqualTo(2));
            Assert.That(plan.Bridges.Count, Is.EqualTo(3));
            Assert.That(plan.Landings.Count, Is.EqualTo(4));
            foreach (CityRiverPromenadeDescriptor promenade in plan.Promenades)
            {
                Assert.That(promenade.Bounds.width, Is.EqualTo(3f).Within(.001f));
                float inset = Mathf.Min(promenade.Bounds.width * .5f, CityPedestrianPlanner.AgentRadius + .1f);
                float npcX = promenade.WestBank ? promenade.Bounds.xMin + inset : promenade.Bounds.xMax - inset;
                // Check both the central player lane and the actual landward
                // NPC lane, whose inset comes from CityPedestrianRiverPlanner.
                int stations = Mathf.CeilToInt(promenade.Bounds.height / 4f);
                for (int index = 0; index <= stations; index++)
                {
                    float z = Mathf.Lerp(promenade.Bounds.yMin + .7f, promenade.Bounds.yMax - .7f, index / (float)stations);
                    float y = RiverPromenadeY(promenade, z);
                    Vector3 central = new Vector3(promenade.Bounds.center.x, y, z);
                    bool upperLane = true;
                    foreach (CityRiverLandingDescriptor landing in plan.Landings)
                        if (landing.WestBank == promenade.WestBank &&
                            (RiverCutContains(landing.StairBounds, central, .45f) ||
                             RiverCutContains(landing.PlatformBounds, central, .45f)))
                        {
                            upperLane = false;
                            break;
                        }
                    // Stair/platform cuts deliberately descend through this
                    // part of the apron; their own treads are checked below.
                    if (upperLane) AssertRiverSupportedAndClear(city, river, central, promenade.Id);
                    AssertRiverSupportedAndClear(city, river, new Vector3(npcX, y, z),
                        promenade.Id + " NPC lane", CityPedestrianPlanner.AgentRadius);
                }
            }
            foreach (CityRiverBridgeDescriptor bridge in plan.Bridges)
                for (int index = 0; index <= 8; index++)
                {
                    float amount = index / 8f;
                    float x = Mathf.Lerp(bridge.DeckBounds.xMin + .6f, bridge.DeckBounds.xMax - .6f, amount);
                    float y = Mathf.Lerp(bridge.WestY, bridge.EastY, amount) + CityStreetSurfacePlanner.RoadTop;
                    AssertRiverSupportedAndClear(city, river, new Vector3(x, y, bridge.DeckBounds.center.y), bridge.Definition.Id);
                }
            foreach (CityRiverLandingDescriptor landing in plan.Landings)
            {
                float direction = Mathf.Sign(landing.DescentDirection.z);
                float upperZ = landing.StairBounds.center.y - direction * landing.StairBounds.height * .5f;
                CityRiverPromenadeDescriptor bank = plan.Promenades[landing.WestBank ? 0 : 1];
                float approachZ = upperZ - direction * .45f;
                // Outside the cut the approach follows the graded bank;
                // UpperY is the bridge sidewalk datum used by the stairs.
                AssertRiverSupportedAndClear(city, river,
                    new Vector3(landing.StairBounds.center.x, RiverPromenadeY(bank, approachZ), approachZ),
                    landing.Id + " upper approach");
                for (int index = 0; index < landing.StepCount; index++)
                {
                    float top = Mathf.Lerp(landing.UpperY, landing.LowerY, (index + 1f) / landing.StepCount);
                    float z = upperZ + direction * (index + .5f) * landing.StairBounds.height / landing.StepCount;
                    AssertRiverSupportedAndClear(city, river,
                        new Vector3(landing.StairBounds.center.x, top, z), landing.Id + " tread " + index);
                }
                AssertRiverSupportedAndClear(city, river, new Vector3(landing.PlatformBounds.center.x,
                    landing.LowerY, landing.PlatformBounds.center.y), landing.Id + " lower platform");
            }
        }

        private static void AssertRiverSupportedAndClear(CityGameRoot city, Transform river, Vector3 floor,
            string label, float radius = .22f)
        {
            RaycastHit nearest = default;
            bool supported = false;
            foreach (RaycastHit hit in Physics.RaycastAll(floor + Vector3.up * .4f, Vector3.down, .8f,
                Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore))
            {
                if (hit.collider.transform.IsChildOf(city.Player.GameObject.transform) || hit.normal.y < .7f) continue;
                if (!supported || hit.distance < nearest.distance) { nearest = hit; supported = true; }
            }
            Assert.That(supported, Is.True, label + " lacks physical floor at " + floor);
            Assert.That(nearest.point.y, Is.EqualTo(floor.y).Within(.15f), label + " floor datum at " + floor);
            Vector3 foot = new Vector3(floor.x, nearest.point.y, floor.z);
            // Keep the lower sphere above ordinary stair risers while still
            // detecting a rail, post or new bollard crossing the body lane.
            foreach (Collider obstruction in Physics.OverlapCapsule(foot + Vector3.up * (radius + .33f),
                foot + Vector3.up * 1.5f, radius, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore))
                if (obstruction.transform.IsChildOf(river))
                    Assert.Fail(label + ": river geometry blocks the walking capsule at " + floor + ": " + obstruction.name);
        }

        private static void AssertRiverLanterns(CityGameRoot city, Transform river)
        {
            IReadOnlyList<Vector3> planned = CityRiverWorldBuilder.CreateQuayWallLampPositions(city.Layout);
            Assert.That(city.World.RiverQuayLampAnchors.Count, Is.EqualTo(planned.Count).And.GreaterThan(0));
            Transform lamps = river.Find("Embankment Lamps");
            Assert.That(lamps.GetComponentsInChildren<Light>(true), Is.Empty,
                "Lanterns lease the existing nearest-light pool.");
            Assert.That(lamps.Find("Quay Wall Lamp Glow").GetComponent<Renderer>().sharedMaterial,
                Is.SameAs(CityNightResources.EmissiveMaterial));
            float channelX = city.Layout.River.Segments[0].WaterBounds.center.x;
            for (int index = 0; index < planned.Count; index++)
            {
                Vector3 p = planned[index];
                float sign = p.x < channelX ? 1f : -1f;
                Transform anchor = city.World.RiverQuayLampAnchors[index];
                Assert.That(Vector3.Distance(anchor.position, p + new Vector3(sign * .45f, .20f, 0f)), Is.LessThan(.002f));
                Assert.That(anchor.forward.y, Is.LessThan(-.5f));
                Assert.That(anchor.forward.x * sign, Is.GreaterThan(.7f), "The pool must spill across the real water receiver.");
            }
            CityRiverPromenadeDescriptor bank = city.Layout.River.Promenades[0];
            Transform first = city.World.RiverQuayLampAnchors[0];
            city.Player.Motor.Teleport(new Vector3(bank.Bounds.center.x, RiverPromenadeY(bank, first.position.z), first.position.z));
            city.Night.Atmosphere.SetNightFactor(1f, true);
            var night = new Dictionary<Light, float>();
            foreach (Light light in city.Night.Atmosphere.StreetLightPool)
                foreach (Transform anchor in city.World.RiverQuayLampAnchors)
                    if (Vector3.Distance(light.transform.position, anchor.position) < .002f)
                    {
                        Assert.That(light.enabled, Is.True);
                        Assert.That(light.type, Is.EqualTo(LightType.Spot));
                        Assert.That(light.range, Is.EqualTo(10f));
                        Assert.That(light.spotAngle, Is.EqualTo(130f));
                        Assert.That(Vector3.Angle(light.transform.forward, anchor.forward), Is.LessThan(.02f));
                        night.Add(light, light.intensity);
                        break;
                    }
            Assert.That(night.Count, Is.GreaterThan(0), "Standing at the quay must assign a real spill light.");
            city.Night.Atmosphere.SetNightFactor(0f, true);
            foreach (KeyValuePair<Light, float> pair in night)
            {
                Assert.That(pair.Key.enabled, Is.True);
                Assert.That(pair.Key.intensity, Is.GreaterThanOrEqualTo(pair.Value * (2f / 3f) - .001f));
            }
            int wallHalos = 0;
            foreach (CityLightHalo halo in lamps.GetComponentsInChildren<CityLightHalo>(true))
                if (halo.name == "Quay Wall Lamp Halo")
                {
                    wallHalos++;
                    Assert.That(halo.IsVisible, Is.True);
                    Assert.That(halo.IntensityFactor, Is.GreaterThanOrEqualTo(2f / 3f));
                }
            Assert.That(wallHalos, Is.EqualTo(planned.Count));
            city.DayNight.ApplyCurrentTime(true);
        }

        private static Shot[] RiverEmbankmentShots(CityGameRoot city, Volume grayscale)
        {
            var shots = new List<Shot>();
            var banks = new List<Shot>();
            CityRiverDressingPlan construction = CityRiverDressingPlan.Create(city.Layout);
            foreach (CityRiverPromenadeDescriptor bank in city.Layout.River.Promenades)
            {
                Transform lamp = null;
                float channelX = city.Layout.River.Segments[0].WaterBounds.center.x;
                foreach (Transform candidate in city.World.RiverQuayLampAnchors)
                    if ((candidate.position.x < channelX) == bank.WestBank &&
                        (lamp == null || Mathf.Abs(candidate.position.z - bank.Bounds.center.y) < Mathf.Abs(lamp.position.z - bank.Bounds.center.y)))
                        lamp = candidate;
                Assert.That(lamp, Is.Not.Null);
                float z = lamp.position.z - 4f;
                Shot pose = Shot.At(bank.WestBank ? "west-bank" : "east-bank",
                    new Vector3(bank.Bounds.center.x, RiverPromenadeY(bank, z) + EyeHeight, z),
                    lamp.position + Vector3.forward * 6f, 72f);
                banks.Add(pose);
                Add("day-river-" + pose.Name, pose, false);
                CityRiverDressingPlacement foot = default;
                bool hasFoot = false;
                foreach (CityRiverDressingPlacement candidate in construction.Placements)
                    if (candidate.Id.StartsWith(bank.Id + "-rail-foot-", StringComparison.Ordinal) &&
                        (!hasFoot || Mathf.Abs(candidate.Position.z - bank.Bounds.center.y) <
                            Mathf.Abs(foot.Position.z - bank.Bounds.center.y)))
                    {
                        foot = candidate;
                        hasFoot = true;
                    }
                Assert.That(hasFoot, Is.True);
                float landward = bank.WestBank ? -1f : 1f;
                Add("day-river-" + pose.Name + "-plate-contact", Shot.At("plate-contact",
                    foot.Position + new Vector3(landward * .95f, .28f, -.70f),
                    foot.Position + Vector3.up * .07f, 58f), false);
            }
            foreach (CityRiverBridgeDescriptor bridge in city.Layout.River.Bridges)
                Add("day-river-bridge-" + bridge.Definition.Id, Shot.At(bridge.Definition.Id,
                    new Vector3(bridge.SpanBounds.xMin - 3.2f, bridge.WestY + EyeHeight, bridge.DeckBounds.yMin - 4.5f),
                    new Vector3(bridge.SpanBounds.center.x, bridge.AverageY + .7f, bridge.SpanBounds.center.y), 74f), false);
            foreach (CityRiverLandingDescriptor landing in city.Layout.River.Landings)
            {
                Vector3 descent = landing.DescentDirection;
                float upperZ = landing.StairBounds.center.y - descent.z * landing.StairBounds.height * .5f;
                Add("day-river-" + landing.Id, Shot.At(landing.Id,
                    new Vector3(landing.StairBounds.center.x, landing.UpperY + EyeHeight, upperZ - descent.z * 1.1f),
                    new Vector3(landing.PlatformBounds.center.x, landing.LowerY + .3f, landing.PlatformBounds.center.y), 70f), false);
            }
            foreach (Shot bank in banks) Add("night-river-" + bank.Name, bank, true);
            CityRiverBridgeDescriptor works = city.Layout.River.Bridges[0];
            Add("night-river-bridge-structure", Shot.At("bridge-structure",
                new Vector3(works.SpanBounds.xMin + 1.6f, works.AverageY + .6f, works.DeckBounds.yMin - 3.5f),
                new Vector3(works.SpanBounds.center.x, works.AverageY - .3f, works.DeckBounds.yMin), 66f), true);
            Transform lowLamp = city.World.RiverQuayLampAnchors[0];
            float towardWater = lowLamp.position.x < city.Layout.River.Segments[0].WaterBounds.center.x ? 1f : -1f;
            Add("night-river-lantern-close", Shot.At("lantern-close",
                lowLamp.position + new Vector3(towardWater * 1.25f, .55f, -1.7f), lowLamp.position, 56f), true);
            Add("night-river-heavy-rain", banks[0], true, rain: true);
            Add("night-river-grayscale", banks[1], true, gray: true);
            Add("night-river-intoxication", banks[0], true, intoxicated: true);
            Assert.That(shots.Count, Is.EqualTo(18), "Keep this acceptance bounded to the affected river.");
            return shots.ToArray();

            void Add(string name, Shot pose, bool night, bool rain = false, bool gray = false, bool intoxicated = false)
            {
                int frames = 0;
                shots.Add(Shot.At(name, pose.Position, pose.Target, pose.FieldOfView, readyWhen: () =>
                {
                    if (frames++ == 0)
                    {
                        if (night && GameSessionState.GameTimeOfDayMinutes < 21 * 60)
                        {
                            GameSessionState.AdvanceGameTime((float)((21 * 60 - GameSessionState.GameTimeOfDayMinutes) /
                                GameTimeState.GameMinutesPerRealSecond));
                            city.DayNight.ApplyCurrentTime(true);
                        }
                        grayscale.weight = gray ? 1f : 0f;
                        if (rain)
                        {
                            city.Rain.SetIntensity(1f);
                            CityWaterResources.SetRainIntensity(1f);
                            CityWetSurfaceRegistry.SetImmediate(1f);
                        }
                        if (intoxicated) GameSessionState.UpdateDrinkingProgress(100, DrinkId.LightBeer, 1);
                        MoveCityLightingObserver(city, pose);
                    }
                    Assert.That(city.Night.Atmosphere.RealtimeLightCount, Is.LessThanOrEqualTo(12));
                    Assert.That(city.Lighting.HalationSourceCount, Is.LessThanOrEqualTo(24));
                    Assert.That(city.Lighting.VolumeSourceCount, Is.LessThanOrEqualTo(4));
                    Assert.That(city.Lighting.BounceRegionCount, Is.LessThanOrEqualTo(4));
                    return frames > (intoxicated ? 90 : rain ? 30 : SettleFrames);
                }));
            }
        }

        private static float RiverPromenadeY(CityRiverPromenadeDescriptor bank, float z) =>
            Mathf.Lerp(bank.SouthY, bank.NorthY, Mathf.InverseLerp(bank.Bounds.yMin, bank.Bounds.yMax, z));

        private static bool RiverCutContains(Rect cut, Vector3 point, float clearance) =>
            point.x >= cut.xMin - clearance && point.x <= cut.xMax + clearance &&
            point.z >= cut.yMin - clearance && point.z <= cut.yMax + clearance;
    }
}

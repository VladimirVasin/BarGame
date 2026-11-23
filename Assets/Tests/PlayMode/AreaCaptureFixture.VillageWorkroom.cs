using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

namespace BarPromenade.Tests.PlayMode
{
    public sealed class VillageWorkroomAssetsSetup : IPrebuildSetup
    {
        public void Setup()
        {
#if UNITY_EDITOR
            // Each importer owns only its separate asset family. In particular
            // this never rebuilds or saves the user-edited production hero.
            foreach (string name in new[] { "VillageLifePropAssetSetup", "VillageResidentDoorAssetSetup",
                "VillageWorkroomAssetSetup", "VillageResidentAssetSetup", "VillageWorkroomPlayerActionAssetSetup" })
            {
                Type setup = Type.GetType("BarPromenade.Editor." + name + ", BarPromenade.Editor", true);
                setup.GetMethod("BuildOrThrow", Type.EmptyTypes).Invoke(null, null);
            }
#endif
        }
    }

    public sealed class VillageWorkroomGeometrySetup : IPrebuildSetup
    {
        public void Setup()
        {
#if UNITY_EDITOR
            Type setup = Type.GetType("BarPromenade.Editor.VillageWorkroomAssetSetup, BarPromenade.Editor", true);
            setup.GetMethod("BuildOrThrow", Type.EmptyTypes).Invoke(null, null);
#endif
        }
    }

    public sealed partial class AreaCaptureFixture
    {
        [UnityTest]
        [Explicit("The wall-lining and local shadow-budget regressions with real room views; no household-action journey.")]
        [PrebuildSetup(typeof(VillageWorkroomGeometrySetup))]
        public IEnumerator VillageWorkroomWalls()
        {
            var shadowMessages = new List<string>();
            void RecordShadowMessage(string message, string stack, LogType type)
            {
                if (message.IndexOf("Reduced additional punctual light shadows", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    message.IndexOf("Too many additional punctual lights shadows", StringComparison.OrdinalIgnoreCase) >= 0)
                    shadowMessages.Add(message);
            }
            Application.logMessageReceived += RecordShadowMessage;
            try
            {
                GameSessionState.BeginNewGame();
                GameSessionState.TryStartGameTimeFromWake();
                AsyncOperation load = SceneManager.LoadSceneAsync(SceneIds.AlpineVillage, LoadSceneMode.Single);
                float deadline = Time.realtimeSinceStartup + TimeoutSeconds;
                AlpineVillageRoot village = null;
                while (Time.realtimeSinceStartup < deadline)
                {
                    village = Object.FindAnyObjectByType<AlpineVillageRoot>();
                    if (load.isDone && village != null && village.IsInitialized) break;
                    yield return null;
                }
                Assert.That(village != null && village.IsInitialized, Is.True);
                AssertWorkroomShadowBudget(village.Workroom.Environment);
                AssertWorkroomSnowGeometry(village);
                AssertWorkroomGroundGeometry(village);
                VillageWorkroomInstance room = village.Workroom.Room;
                Camera camera = Camera.main;
                bool followEnabled = village.CameraFollow.enabled;
                Vector3 previousPosition = camera.transform.position;
                Quaternion previousRotation = camera.transform.rotation;
                float previousFov = camera.fieldOfView;
                try
                {
                    village.CameraFollow.enabled = false;
                    foreach (float shift in new[] { -.035f, 0f, .035f })
                    {
                        Vector3 position = room.Plan.World(new Vector3(.1f + shift, 1.75f, 1.60f));
                        Vector3 target = room.Plan.World(new Vector3(-1.50f, 1.15f, -.8f));
                        camera.transform.SetPositionAndRotation(position, Quaternion.LookRotation(target - position));
                        camera.fieldOfView = 78f;
                        yield return null;
                        CaptureCurrentCamera(camera, "VillageWorkroomWalls", $"room-{shift + .035f:F3}");
                    }
                    camera.transform.SetPositionAndRotation(room.Plan.World(new Vector3(-2.1f, 1.8f, 5.2f)),
                        Quaternion.LookRotation(room.Plan.World(new Vector3(-2.1f, 1.1f, 1f)) - room.Plan.World(new Vector3(-2.1f, 1.8f, 5.2f))));
                    camera.fieldOfView = 52f;
                    CaptureCurrentCamera(camera, "VillageWorkroomWalls", "exterior-window");
                    Vector3 closePosition = room.Plan.World(new Vector3(-.8f, 1.3f, .3f));
                    camera.transform.SetPositionAndRotation(closePosition,
                        Quaternion.LookRotation(room.Plan.Anchor("ClothRest") - closePosition));
                    camera.fieldOfView = 48f;
                    yield return null;
                    CaptureCurrentCamera(camera, "VillageWorkroomWalls", "repairs-cloth-floor");

                    Physics.SyncTransforms();
                    MeshCollider[] shells = room.HouseRoot.GetComponentsInChildren<MeshCollider>().Where(c =>
                        c.sharedMesh != null && c.sharedMesh.name.StartsWith("GEO_Workroom_Shell", StringComparison.Ordinal)).ToArray();
                    Assert.That(shells.Length, Is.EqualTo(3));
                    int samples = 0, backedSamples = 0;
                    float minimumDepth = float.PositiveInfinity;
                    foreach (float height in new[] { .50f, 1.35f, 2.05f })
                    {
                        foreach (float x in new[] { -2.95f, -1.05f })
                            Probe("FrontLining", new Vector3(x, height, 2.53f), Vector3.forward);
                        foreach (float z in new[] { -.20f, .80f, 1.80f })
                            Probe("LeftLining", new Vector3(-3.30f, height, z), Vector3.left);
                        foreach (float x in new[] { -2.80f, -1.30f, -.30f })
                            Probe("RearLining", new Vector3(x, height, -2.27f), Vector3.back);
                    }
                    Assert.That(samples, Is.GreaterThan(20));
                    Assert.That(backedSamples, Is.GreaterThan(16), "Sample the retained shell behind each real wall.");
                    TestContext.Out.WriteLine($"Workroom lining: {samples} placed-mesh samples, {backedSamples} backed by shell, minimum shell setback {minimumDepth:F5} m.");

                    void Probe(string name, Vector3 localPoint, Vector3 localOutward)
                    {
                        Vector3 direction = room.Plan.Rotation * localOutward;
                        var ray = new Ray(room.Plan.World(localPoint) - direction * .12f, direction);
                        var lining = room.Part(name).GetComponentInChildren<MeshCollider>();
                        Assert.That(lining.Raycast(ray, out RaycastHit liningHit, .5f), Is.True, name);
                        bool foundShell = false;
                        foreach (MeshCollider shell in shells)
                        {
                            if (!shell.Raycast(ray, out RaycastHit shellHit, 1.2f)) continue;
                            float depth = shellHit.distance - liningHit.distance;
                            Assert.That(depth, Is.GreaterThan(.045f),
                                $"{name} is coplanar with or behind {shell.sharedMesh.name} at {localPoint}: {depth:F6} m.");
                            minimumDepth = Mathf.Min(minimumDepth, depth);
                            foundShell = true;
                        }
                        // The pre-existing shell has gaps in two low left-wall
                        // spans. Its absence cannot cause competing depth; the
                        // solid lining itself must still exist at every probe.
                        if (foundShell) backedSamples++;
                        samples++;
                    }
                }
                finally
                {
                    camera.transform.SetPositionAndRotation(previousPosition, previousRotation);
                    camera.fieldOfView = previousFov;
                    village.CameraFollow.enabled = followEnabled;
                }
                Assert.That(shadowMessages, Is.Empty, string.Join("\n", shadowMessages));
            }
            finally
            {
                Application.logMessageReceived -= RecordShadowMessage;
            }
        }

        private static void AssertWorkroomGroundGeometry(AlpineVillageRoot village)
        {
            VillageWorkroomPlan room = village.Workroom.Plan;
            Transform ground = village.World.TerrainRoot.transform;
            MeshCollider collision = ground.GetComponent<MeshCollider>();
            Assert.That(collision, Is.Not.Null);
            // These are the two actual terrain-triangle hits in the failed
            // repairs/cloth camera. Probe the final collider, not the analytic
            // depth field or a face centroid; chunk indices change after cuts.
            foreach (Vector2 local in new[] { new Vector2(-1.58926f, 2.51993f), new Vector2(-1.41435f, 2.51345f) })
            {
                Vector3 origin = room.World(new Vector3(local.x, 1f, local.y));
                Assert.That(collision.Raycast(new Ray(origin, Vector3.down), out RaycastHit hit, 2f), Is.True);
                Assert.That(room.Local(hit.point).y, Is.LessThanOrEqualTo(-.025f),
                    $"Terrain still pierces the floor at the reproduced peak {local}.");
                Assert.That(room.Local(hit.point).y, Is.EqualTo(-VillageWorkroomPlan.TerrainBedDepth).Within(.001f));
            }
            Vector2 half = room.House.FootprintSize * .5f;
            foreach (Vector2 local in new[] { new Vector2(-half.x - .05f, 0f), new Vector2(half.x + .05f, 0f),
                new Vector2(0f, -half.y - .05f), new Vector2(0f, half.y + .05f) })
            {
                Vector3 origin = room.World(new Vector3(local.x, 2f, local.y));
                Assert.That(collision.Raycast(new Ray(origin, Vector3.down), out RaycastHit hit, 4f), Is.True);
                Assert.That(hit.point.y, Is.EqualTo(AlpineVillageTerrainSampler.SampleMeshHeight(
                    village.Plan, new Vector2(hit.point.x, hit.point.z))).Within(.001f),
                    "The cavity must retain the outdoor collision plane.");
            }
            int checkedSectors = 0;
            foreach (MeshFilter filter in ground.GetComponentsInChildren<MeshFilter>())
            {
                Renderer renderer = filter.GetComponent<Renderer>();
                if (renderer == null || !renderer.enabled || !renderer.bounds.Intersects(
                    new Bounds(room.House.GroundCenter, new Vector3(12f, 4f, 12f)))) continue;
                AssertWorkroomFloorClearance(room, filter, "Visible terrain", false);
                checkedSectors++;
            }
            Assert.That(checkedSectors, Is.GreaterThan(0), "Probe the actual enabled ground sectors as well as collision.");
        }

        private static void AssertWorkroomSnowGeometry(AlpineVillageRoot village)
        {
            Transform drifts = village.World.Root.transform.Find(AlpineVillageWorldBuilder.SnowDriftObjectName);
            Assert.That(drifts, Is.Not.Null);
            AssertWorkroomFloorClearance(village.Workroom.Plan, drifts.GetComponent<MeshFilter>(), "Static snow", true);
        }

        private static void AssertWorkroomFloorClearance(VillageWorkroomPlan room, MeshFilter filter, string owner, bool fullThreshold)
        {
            Mesh mesh = filter.sharedMesh;
            Vector3[] positions = mesh.vertices;
            int[] indices = mesh.triangles;
            for (int index = 0; index < positions.Length; index++)
                positions[index] = room.Local(filter.transform.TransformPoint(positions[index]));
            Bounds main = VillageWorkroomPlan.LocalRoomBounds;
            var floors = new[]
            {
                Rect.MinMaxRect(main.min.x + .005f, main.min.z + .005f, main.max.x - .005f, main.max.z - .005f),
                Rect.MinMaxRect(1.205f, -2.265f, 3.495f, 1.395f),
                Rect.MinMaxRect(room.House.DoorAcrossOffset - .515f, 2.535f,
                    room.House.DoorAcrossOffset + .515f, fullThreshold ? 2.785f : room.House.FootprintSize.y * .445f - .005f)
            };
            int intrusions = 0;
            float highest = float.NegativeInfinity;
            for (int index = 0; index < indices.Length; index += 3)
            {
                Vector3 a = positions[indices[index]], b = positions[indices[index + 1]], c = positions[indices[index + 2]];
                if (Mathf.Max(a.y, Mathf.Max(b.y, c.y)) <= -.025f) continue;
                Rect bounds = Rect.MinMaxRect(Mathf.Min(a.x, Mathf.Min(b.x, c.x)), Mathf.Min(a.z, Mathf.Min(b.z, c.z)),
                    Mathf.Max(a.x, Mathf.Max(b.x, c.x)), Mathf.Max(a.z, Mathf.Max(b.z, c.z)));
                foreach (Rect floor in floors)
                {
                    if (!bounds.Overlaps(floor)) continue;
                    // Intersect actual faces with the occupied boards: checking
                    // depth samples or centroids misses a narrow wall-edge peak.
                    var face = new GroundSurfacePartitioner.Triangle(Pack(a), Pack(b), Pack(c), 0);
                    foreach (GroundSurfacePartitioner.Triangle piece in GroundSurfacePartitioner.ClipToRect(face, floor))
                    {
                        float top = Mathf.Max(piece.A.Position.y, Mathf.Max(piece.B.Position.y, piece.C.Position.y));
                        highest = Mathf.Max(highest, top);
                        if (top > -.025f) intrusions++;
                    }
                }
            }
            Assert.That(intrusions, Is.Zero,
                $"{owner} rises through the workroom boards: {intrusions} clipped faces, highest {highest:F4} m.");

            GroundSurfacePartitioner.Vertex Pack(Vector3 position) => new GroundSurfacePartitioner.Vertex(
                position, Vector3.up, Vector2.zero, Vector4.zero, Color.white);
        }

        private static void AssertWorkroomShadowBudget(VillageWorkroomEnvironment environment)
        {
            Light[] lights = environment.GetComponentsInChildren<Light>(true);
            Assert.That(lights.Length, Is.EqualTo(3));
            Light ambient = lights.SingleOrDefault(light => light.name == "Workroom AmbientLight");
            Assert.That(ambient, Is.Not.Null);
            Assert.That(ambient.gameObject.activeInHierarchy && ambient.enabled && ambient.intensity > 0f,
                Is.True, "The production room light must remain active.");
            Assert.That(ambient.type, Is.EqualTo(LightType.Point));
            Assert.That(ambient.shadows, Is.EqualTo(LightShadows.Soft));
            UniversalAdditionalLightData data = ambient.GetComponent<UniversalAdditionalLightData>();
            Assert.That(data, Is.Not.Null, "An unconfigured point light requests the High tier by default.");
            Assert.That(data.additionalLightsShadowResolutionTier,
                Is.EqualTo(UniversalAdditionalLightData.AdditionalLightsShadowResolutionTierMedium));
            var pipeline = GraphicsSettings.currentRenderPipeline as UniversalRenderPipelineAsset;
            Assert.That(pipeline, Is.Not.Null);
            Assert.That(pipeline.additionalLightsShadowResolutionTierMedium, Is.EqualTo(512));
            int tilesPerSide = pipeline.additionalLightsShadowmapResolution /
                pipeline.additionalLightsShadowResolutionTierMedium;
            Assert.That(tilesPerSide * tilesPerSide, Is.GreaterThanOrEqualTo(6),
                "All six faces of the point shadow must fit without atlas rescaling.");
            foreach (string name in new[] { "Workroom RepairLamp", "Workroom SewingLamp" })
            {
                Light helper = lights.SingleOrDefault(light => light.name == name);
                Assert.That(helper, Is.Not.Null, name);
                Assert.That(helper.shadows, Is.EqualTo(LightShadows.None), name);
            }
        }

        [Serializable]
        private sealed class VillageWorkroomReport
        {
            public int solid_parts, window_panes, npc_contact_samples, physical_prop_samples, player_contact_samples;
            public int door_contact_samples, culled_weather_particles, sewing_cycles, repair_strikes;
            public int indoor_walk_body_samples, indoor_walk_body_pairs;
            public float maximum_npc_grip_error_metres, maximum_prop_error_metres, maximum_player_grip_error_metres;
            public float maximum_door_grip_error_metres, maximum_player_root_step_metres, simulated_seconds;
            public float maximum_indoor_walk_overlap_metres;
            public float cloth_fold_range_degrees, box_lid_range_degrees, inside_cold_weight, outside_cold_weight;
            public bool floor_and_ceiling_solid, windows_physically_open, same_two_visible_residents;
            public bool cancelled_help_not_committed, help_completed, bench_seated_and_stood;
            public bool door_opened_outside, door_closed_inside, doorway_walked, chair_fixed, sewing_stowed;
            public bool evening_workers_home, evening_guest_door_stayed_open, evening_outside_worker_returned;
            public float evening_simulated_seconds;
            public int evening_close_attempts;
            public string[] observed_actions;
        }

        private sealed class WorkroomObservation
        {
            internal readonly Dictionary<VillageResidentPresentation, Renderer[]> VisibleParts = new Dictionary<VillageResidentPresentation, Renderer[]>();
            internal readonly HashSet<string> Actions = new HashSet<string>();
            internal readonly Dictionary<VillageResidentPresentation, Vector3> LastRoots = new Dictionary<VillageResidentPresentation, Vector3>();
            internal readonly Dictionary<string, Quaternion> FirstRotations = new Dictionary<string, Quaternion>();
            internal readonly HashSet<string> Captured = new HashSet<string>();
        }

        // Test the rendered fingers, palms, cuffs and forearms against the wooden panels.
        // Each core is inset by 2 mm so brushing the eased lip is a valid contact.
        private sealed class SewingArmClearance : IDisposable
        {
            private readonly SkinnedMeshRenderer[] skins;
            private readonly int[][] indices;
            private readonly int[][] forearmFaces;
            private readonly SkinnedMeshRenderer cardigan;
            private readonly ScarfContactProbe torsoProbe = new ScarfContactProbe();
            private readonly Transform[] shoulders, elbows;
            private readonly List<Vector3> world = new List<Vector3>();
            private readonly Mesh baked = new Mesh();
            private readonly List<Vector3> vertices = new List<Vector3>();
            private readonly List<Vector3> local = new List<Vector3>();
            private readonly HashSet<string> overlaps = new HashSet<string>();
            private readonly Transform box, lid;
            private readonly Bounds[] boards =
            {
                new Bounds(new Vector3(-.132f,.075f,0), new Vector3(.014f,.146f,.316f)),
                new Bounds(new Vector3( .132f,.075f,0), new Vector3(.014f,.146f,.316f)),
                new Bounds(new Vector3(0,.075f,-.152f), new Vector3(.242f,.146f,.012f)),
                new Bounds(new Vector3(0,.075f, .152f), new Vector3(.242f,.146f,.012f)),
                new Bounds(new Vector3(0,.081f,0), new Vector3(.240f,.006f,.284f)),
                new Bounds(new Vector3(0,.008f,-.16f), new Vector3(.278f,.012f,.316f))
            };

            internal SewingArmClearance(VillageResidentPresentation actor, Transform box, Transform lid)
            {
                this.box = box; this.lid = lid;
                skins = actor.ModelRoot.GetComponentsInChildren<SkinnedMeshRenderer>(true).Where(r => r.enabled &&
                    (r.name.StartsWith("GEO_Hand", StringComparison.Ordinal) || r.name.StartsWith("CLO_CardiganSleeve", StringComparison.Ordinal)
                        || r.name.StartsWith("CLO_CardiganCuff", StringComparison.Ordinal))).ToArray();
                Assert.That(skins.Count(r => r.name.StartsWith("GEO_Hand")), Is.EqualTo(12));
                Assert.That(skins.Length, Is.GreaterThan(12));
                indices = skins.Select(r => r.sharedMesh.triangles).ToArray();
                cardigan = actor.ModelRoot.GetComponentsInChildren<SkinnedMeshRenderer>(true).Single(r => r.enabled && r.name == "CLO_Cardigan");
                var bones = actor.ModelRoot.GetComponentsInChildren<Transform>(true);
                Transform[] Pair(string name) => new[] { ".L", ".R" }.Select(side => bones.Single(b => b.name == name + side)).ToArray();
                shoulders = Pair("upper_arm");
                elbows = Pair("forearm");
                forearmFaces = skins.Select((skin, index) =>
                {
                    if (!skin.name.StartsWith("CLO_CardiganSleeve", StringComparison.Ordinal)) return indices[index];
                    // The sleeve is sewn into the torso at the shoulder. Use its deformed elbow and forearm section.
                    int forearm = Array.FindIndex(skin.bones, b => b.name == "forearm" + skin.name.Substring(skin.name.Length - 2));
                    Assert.That(forearm, Is.GreaterThanOrEqualTo(0));
                    var weights = skin.sharedMesh.boneWeights;
                    bool AtElbow(int vertex)
                    {
                        var w = weights[vertex];
                        return (w.boneIndex0 == forearm && w.weight0 >= .5f) || (w.boneIndex1 == forearm && w.weight1 >= .5f) ||
                            (w.boneIndex2 == forearm && w.weight2 >= .5f) || (w.boneIndex3 == forearm && w.weight3 >= .5f);
                    }
                    var faces = new List<int>();
                    var tris = indices[index];
                    for (int i = 0; i < tris.Length; i += 3)
                        if (AtElbow(tris[i]) || AtElbow(tris[i+1]) || AtElbow(tris[i+2]))
                        { faces.Add(tris[i]); faces.Add(tris[i+1]); faces.Add(tris[i+2]); }
                    Assert.That(faces, Is.Not.Empty);
                    return faces.ToArray();
                }).ToArray();
            }

            internal void Observe(VillageResidentPresentation actor)
            {
                var torso = torsoProbe.Read(cardigan);
                Vector3 left = (shoulders[0].position - shoulders[1].position).normalized;
                Vector3 middle = (shoulders[0].position + shoulders[1].position) * .5f;
                for (int side = 0; side < elbows.Length; side++)
                {
                    Vector3 elbow = elbows[side].position;
                    if (Vector3.Dot(elbow - shoulders[side].position, actor.transform.up) > .025f ||
                        Vector3.Dot(elbow - middle, side == 0 ? left : -left) < .045f)
                        overlaps.Add($"{actor.CurrentAction}/{actor.CurrentActionSeconds:F2}/{elbows[side].name} outside working pose");
                }
                for (int s = 0; s < skins.Length; s++)
                {
                    var skin = skins[s];
                    skin.BakeMesh(baked, true);
                    baked.GetVertices(vertices);
                    world.Clear();
                    foreach (var vertex in vertices) world.Add(skin.localToWorldMatrix.MultiplyPoint3x4(vertex));
                    if (IntersectsTorso(world, forearmFaces[s], torso))
                        overlaps.Add($"{actor.CurrentAction}/{actor.CurrentActionSeconds:F2}/torso/{skin.name}");
                    if (skin.name.StartsWith("GEO_HandPalm", StringComparison.Ordinal))
                    {
                        Transform grip = skin.name.EndsWith(".L") ? actor.LeftGrip : actor.RightGrip;
                        Assert.That(Vector3.Distance(skin.localToWorldMatrix.MultiplyPoint3x4(baked.bounds.center), grip.position),
                            Is.LessThan(.15f), "Contact test must sample the visible palm in world metres");
                    }
                    for (int p = 0; p < boards.Length; p++)
                    {
                        Matrix4x4 space = (p == boards.Length - 1 ? lid : box).worldToLocalMatrix * skin.localToWorldMatrix;
                        if (p == 0 || p == boards.Length - 1)
                        {
                            local.Clear();
                            foreach (var v in vertices) local.Add(space.MultiplyPoint3x4(v));
                        }
                        var board = boards[p];
                        int[] tris = indices[s];
                        for (int i = 0; i < tris.Length; i += 3)
                        {
                            Vector3 a = local[tris[i]], b = local[tris[i+1]], c = local[tris[i+2]];
                            if (!InsideBoard(a,b,c,board)) continue;
                            overlaps.Add($"{actor.CurrentAction}/{Mathf.Floor(actor.CurrentActionSeconds * 4) / 4:F2}/{p}/{skin.name}");
                            break;
                        }
                    }
                }
            }

            private bool IntersectsTorso(List<Vector3> points, int[] faces, ScarfContactSurface torso)
            {
                for (int i = 0; i < faces.Length; i += 3)
                {
                    Vector3 a = points[faces[i]], b = points[faces[i+1]], c = points[faces[i+2]];
                    var bounds = new Bounds(a, Vector3.zero);
                    bounds.Encapsulate(b);
                    bounds.Encapsulate(c);
                    if (!bounds.Intersects(torso.Bounds)) continue;
                    if (torsoProbe.Contains(a, torso)) return true;
                    for (int j = 0; j < torso.Triangles.Length; j += 3)
                    {
                        if (!bounds.Intersects(torso.TriangleBounds[j / 3])) continue;
                        Vector3 x = torso.Vertices[torso.Triangles[j]], y = torso.Vertices[torso.Triangles[j+1]],
                            z = torso.Vertices[torso.Triangles[j+2]];
                        if (ScarfContactProbe.Crosses(a,b,x,y,z) || ScarfContactProbe.Crosses(b,c,x,y,z) || ScarfContactProbe.Crosses(c,a,x,y,z) ||
                            ScarfContactProbe.Crosses(x,y,a,b,c) || ScarfContactProbe.Crosses(y,z,a,b,c) || ScarfContactProbe.Crosses(z,x,a,b,c)) return true;
                    }
                }
                return false;
            }

            private static bool InsideBoard(Vector3 a, Vector3 b, Vector3 c, Bounds board)
            {
                // Triangle bounds, face normal, and nine edge/box-axis cross products (SAT).
                a -= board.center; b -= board.center; c -= board.center;
                Vector3 h = board.extents;
                if (Mathf.Min(a.x, Mathf.Min(b.x,c.x)) > h.x || Mathf.Max(a.x, Mathf.Max(b.x,c.x)) < -h.x ||
                    Mathf.Min(a.y, Mathf.Min(b.y,c.y)) > h.y || Mathf.Max(a.y, Mathf.Max(b.y,c.y)) < -h.y ||
                    Mathf.Min(a.z, Mathf.Min(b.z,c.z)) > h.z || Mathf.Max(a.z, Mathf.Max(b.z,c.z)) < -h.z) return false;
                bool Separate(Vector3 n)
                {
                    float r = Mathf.Abs(n.x)*h.x + Mathf.Abs(n.y)*h.y + Mathf.Abs(n.z)*h.z;
                    float x = Vector3.Dot(a,n), y = Vector3.Dot(b,n), z = Vector3.Dot(c,n);
                    return Mathf.Min(x,Mathf.Min(y,z)) > r || Mathf.Max(x,Mathf.Max(y,z)) < -r;
                }
                bool Edge(Vector3 edge) => Separate(Vector3.Cross(edge,Vector3.right)) ||
                    Separate(Vector3.Cross(edge,Vector3.up)) || Separate(Vector3.Cross(edge,Vector3.forward));
                return !Separate(Vector3.Cross(b-a,c-a)) && !Edge(b-a) && !Edge(c-b) && !Edge(a-c);
            }

            internal void AssertClear() => Assert.That(overlaps.Count, Is.Zero, string.Join(", ", overlaps.OrderBy(k => k)));
            public void Dispose() { torsoProbe.Dispose(); Object.DestroyImmediate(baked); }
        }

        [UnityTest]
        [Explicit("House 08 chair repair and sewing box at the two actual work docks.")]
        [PrebuildSetup(typeof(VillageWorkroomGeometrySetup))]
        public IEnumerator VillageWorkroom_RepairChairAndStowBox()
        {
            GameSessionState.BeginNewGame();
            GameSessionState.TryStartGameTimeFromWake();
            yield return SceneManager.LoadSceneAsync(SceneIds.AlpineVillage, LoadSceneMode.Single);
            var village = Object.FindAnyObjectByType<AlpineVillageRoot>();
            Assert.That(village, Is.Not.Null);
            float deadline = Time.realtimeSinceStartup + TimeoutSeconds;
            while (!village.IsInitialized && Time.realtimeSinceStartup < deadline) yield return null;
            Assert.That(village.IsInitialized, Is.True);

            VillageWorkroomController room = village.Workroom;
            var repairer = village.Life.Neighbours.Single(n => n.Role == VillageResidentRole.RepairNeighbor);
            var sewer = village.Life.Neighbours.Single(n => n.Role == VillageResidentRole.SewingWoman);
            var report = new VillageWorkroomReport();
            float originalDelta = Time.captureDeltaTime;
            bool lifeEnabled = village.Life.enabled;
            var camera = Camera.main;
            Vector3 cameraPosition = camera.transform.position;
            Quaternion cameraRotation = camera.transform.rotation;
            float fov = camera.fieldOfView;
            try
            {
                village.Life.enabled = false;
                Time.captureDeltaTime = 1f / 60f;
                void PlaceWorker(VillageNeighbourState worker, string dock)
                {
                    worker.Actor.gameObject.SetActive(true);
                    worker.Actor.transform.SetPositionAndRotation(room.Plan.Anchor(dock), Quaternion.LookRotation(room.Plan.Facing(dock)));
                    room.ReserveIndoorWork(worker);
                }
                PlaceWorker(repairer, "RepairDock");
                PlaceWorker(sewer, "SewingDock");
                village.Player.Motor.Teleport(room.Plan.Anchor("PlayerHelpDock") + Vector3.up * PlayerFactory.GroundedRootOffset);
                Physics.SyncTransforms();
                room.Environment.Advance(1f);
                var box = WorkroomBox(room);
                var lid = WorkroomBoxLid(room);
                Assert.That(room.Room.Part("BoxHinges").parent, Is.EqualTo(box));
                Assert.That(room.Room.Part("LidHinges").parent, Is.EqualTo(lid));
                using var clearance = new SewingArmClearance(sewer.Actor, box, lid);
                Assert.That(repairer.Actor.GetComponent<NpcWardrobe>().IsEquipped("apron.work"), Is.True);
                Assert.That(sewer.Actor.GetComponent<NpcWardrobe>().IsEquipped("outerwear.cardigan"), Is.True);
                var hammerFace = room.Room.Part("Hammer").Find("ANCHOR_StrikeFace");
                var workers = new[] { repairer, sewer };
                var witnessed = new HashSet<VillageWorkroomContact>();
                bool helping = false, held = false, boxOpen = false, fold = false, packed = false;
                int contactsAtStrike = 0;
                for (int step = 0; step < 4000 && !(room.ChairFixed && room.SewingCycles > 0); step++)
                {
                    int previousStrikes = room.RepairStrikes;
                    room.AdvanceResident(repairer, Time.captureDeltaTime, true);
                    room.AdvanceResident(sewer, Time.captureDeltaTime, true);
                    if (step % 2 == 0) clearance.Observe(sewer.Actor);
                    foreach (var worker in workers)
                    {
                        var actor = worker.Actor;
                        if (actor.CurrentAction == VillageResidentAction.Idle) continue;
                        var frame = VillageResidentPresentation.SampleWorkroomProps(actor.CurrentAction, actor.CurrentActionSeconds);
                        void Contact(Transform grip, float weight, VillageWorkroomContact kind, bool left)
                        {
                            if (weight < .9999f || kind == VillageWorkroomContact.None) return;
                            AssertWorkroomPhysicalContact(room, grip.position, kind, left, worker);
                            witnessed.Add(kind);
                        }
                        Contact(actor.LeftGrip, frame.LeftContactWeight, frame.LeftContactKind, true);
                        Contact(actor.RightGrip, frame.RightContactWeight, frame.RightContactKind, false);
                    }
                    if (room.RepairStrikes != previousStrikes)
                    {
                        Assert.That(Vector3.Distance(hammerFace.position, room.Plan.Anchor("RepairJoin")), Is.LessThan(.02f));
                        contactsAtStrike++;
                    }
                    if (!helping && room.RepairAction == VillageResidentAction.RepairWork)
                    {
                        Assert.That(room.Help.BeginHelp(), Is.True);
                        helping = true;
                    }
                    if (room.Help.IsHolding && room.Help.Controller.PhaseProgress > .05f)
                    {
                        AssertWorkroomHeroGrips(room, report);
                        if (!held && room.RepairStrikes != previousStrikes)
                        {
                            WorkroomFrame(camera, room.Plan, "refit-chair", new Vector3(-2.93f, 1.60f, -.76f),
                                room.Plan.Local(room.RepairRail.position) + Vector3.right * .08f, 60f);
                            held = true;
                        }
                    }
                    if (sewer.Actor.CurrentAction == VillageResidentAction.SewingUnpack && !boxOpen &&
                        Quaternion.Angle(lid.localRotation, Quaternion.identity) > 95f)
                    {
                        Assert.That(Vector3.Distance(box.position, room.Plan.Anchor("SewingBox")), Is.LessThan(.01f));
                        boxOpen = true;
                    }
                    if (sewer.Actor.CurrentAction == VillageResidentAction.SewingFold &&
                        Quaternion.Angle(room.Room.Part("ClothFlap").localRotation, Quaternion.identity) > 160f) fold = true;
                    if (sewer.Actor.CurrentAction == VillageResidentAction.SewingStow &&
                        sewer.Actor.CurrentActionSeconds >= 1.53f && sewer.Actor.CurrentActionSeconds < 1.53f + Time.captureDeltaTime)
                        WorkroomFrame(camera, room.Plan, "refit-box", new Vector3(-2.81f, 1.48f, 1.86f),
                            new Vector3(-1.98f, .83f, 1.25f), 55f);
                    if (sewer.Actor.CurrentAction == VillageResidentAction.SewingStow &&
                        sewer.Actor.CurrentActionSeconds >= 2.05f && sewer.Actor.CurrentActionSeconds < 2.05f + Time.captureDeltaTime)
                        WorkroomFrame(camera, room.Plan, "refit-lid", new Vector3(-1.40f, 1.55f, 2.36f),
                            new Vector3(-1.96f, .84f, 1.19f), 51f);
                    if (!packed && sewer.Actor.CurrentAction == VillageResidentAction.SewingStow && sewer.Actor.CurrentActionSeconds > 4.8f)
                    {
                        Assert.That(Quaternion.Angle(lid.localRotation, Quaternion.identity), Is.LessThan(1f));
                        Assert.That(Vector3.Distance(box.position, room.Plan.Anchor("BoxRest")), Is.LessThan(.01f));
                        var cavity = new Bounds(new Vector3(0, .087f, 0), new Vector3(.247f, .125f, .289f));
                        foreach (string item in new[] { "Cloth", "ClothFlap", "Mitten" })
                        {
                            var mesh = room.Room.Part(item).Find("Geometry").GetComponent<MeshFilter>();
                            foreach (Vector3 vertex in mesh.sharedMesh.vertices)
                                Assert.That(cavity.Contains(box.InverseTransformPoint(mesh.transform.TransformPoint(vertex))), Is.True,
                                    item + " protrudes through the sewing box");
                        }
                        packed = true;
                    }
                    if ((helping && room.Help.OwnsActiveInteraction) || step % 120 == 119) yield return null;
                }
                clearance.AssertClear();
                Assert.That(held && room.ChairFixed && contactsAtStrike > 0, Is.True, "Chair rail, assisted stroke and completed repair");
                Assert.That(Vector3.Distance(room.RepairRail.position, room.Plan.Anchor("RepairRailFixed")), Is.LessThan(.003f));
                Assert.That(boxOpen && fold && packed && room.SewingCycles > 0, Is.True, "Open, fold, pack, close, return");
                foreach (var contact in new[] { VillageWorkroomContact.Hammer, VillageWorkroomContact.Chair,
                    VillageWorkroomContact.Box, VillageWorkroomContact.BoxLid, VillageWorkroomContact.ClothFlap, VillageWorkroomContact.Mitten })
                    Assert.That(witnessed, Does.Contain(contact));
            }
            finally
            {
                room.Help.Cancel();
                village.Life.enabled = lifeEnabled;
                camera.transform.SetPositionAndRotation(cameraPosition, cameraRotation);
                camera.fieldOfView = fov;
                Time.captureDeltaTime = originalDelta;
            }
        }

        [UnityTest]
        [Explicit("One real house-08 room, two ordinary workers and the live hero's door/help/bench interactions. Run alone.")]
        [PrebuildSetup(typeof(VillageWorkroomAssetsSetup))]
        public IEnumerator VillageWorkroom()
        {
            var report = new VillageWorkroomReport();
            var observation = new WorkroomObservation();
            float previousDelta = Time.captureDeltaTime;
            AlpineVillageRoot village = null;
            Camera camera = null;
            Vector3 cameraPosition = default;
            Quaternion cameraRotation = default;
            float cameraFov = 60f;
            bool followEnabled = false, lifeEnabled = false;
            try
            {
                GameSessionState.BeginNewGame();
                GameSessionState.TryStartGameTimeFromWake();
                Time.captureDeltaTime = 1f / 60f;
                AsyncOperation load = SceneManager.LoadSceneAsync(SceneIds.AlpineVillage, LoadSceneMode.Single);
                Assert.That(load, Is.Not.Null);
                float deadline = Time.realtimeSinceStartup + TimeoutSeconds;
                while (Time.realtimeSinceStartup < deadline)
                {
                    village = Object.FindAnyObjectByType<AlpineVillageRoot>();
                    if (load.isDone && village != null && village.IsInitialized) break;
                    yield return null;
                }
                Assert.That(village, Is.Not.Null);
                Assert.That(village.IsInitialized, Is.True);
                VillageWorkroomController room = village.Workroom;
                Assert.That(room, Is.Not.Null);
                Assert.That(room.Room, Is.SameAs(village.World.Workroom));
                Dictionary<string, Transform> originalParts = room.Room.Parts.ToDictionary(pair => pair.Key, pair => pair.Value);
                Assert.That(room.Help, Is.Not.Null); Assert.That(room.Bench, Is.Not.Null); Assert.That(room.Door, Is.Not.Null);
                lifeEnabled = village.Life.enabled; village.Life.enabled = false;
                VillageNeighbourState[] workers = village.Life.Neighbours.Where(state =>
                    state.Role == VillageResidentRole.RepairNeighbor || state.Role == VillageResidentRole.SewingWoman).ToArray();
                Assert.That(workers.Length, Is.EqualTo(2));
                var hero = (Player3DCharacterPresentation)village.Player.Visual;
                int heroTriangles = VillageLifeTriangleCount(hero.Registry.transform);
                foreach (VillageNeighbourState state in workers)
                {
                    AssertVillageResidentDetail(state.Actor, heroTriangles);
                    observation.LastRoots.Add(state.Actor, state.Actor.transform.position);
                    observation.VisibleParts.Add(state.Actor, state.Actor.ModelRoot.GetComponentsInChildren<Renderer>()
                        .Where(r => r.enabled).ToArray());
                }
                camera = Camera.main;
                Assert.That(camera, Is.Not.Null);
                cameraPosition = camera.transform.position; cameraRotation = camera.transform.rotation; cameraFov = camera.fieldOfView;
                followEnabled = village.CameraFollow.enabled; village.CameraFollow.enabled = false;
                WorkroomFrame(camera, room.Plan, "00-exterior-window", new Vector3(-2.1f, 1.8f, 5.2f), new Vector3(-2.1f, 1.1f, 1f), 52f);
                WorkroomFrame(camera, room.Plan, "00-interior-entry", new Vector3(.5f, 1.6f, 1.5f), new Vector3(-2.1f, .8f, -.5f), 70f);
                AssertWorkroomGeometry(room, report);

                // The only placement in this scenario puts the test visitor on
                // the ordinary exterior approach. Every indoor leg is walked.
                VillageResidentDoor door = room.Door.Door;
                village.Player.Motor.Teleport(door.ExteriorDock + Vector3.up * PlayerFactory.GroundedRootOffset);
                village.Player.Motor.SetInputEnabled(true); village.Player.Interactor.SetInputEnabled(true);
                Physics.SyncTransforms();
                for (int frame = 0; frame < 45; frame++) yield return null;
                report.outside_cold_weight = hero.ColdBodyWeight;
                Assert.That(report.outside_cold_weight, Is.GreaterThan(.9f), "The ordinary exterior keeps the shared cold pose.");
                Assert.That(door.IsClosed, Is.True);
                Assert.That(room.Door.Begin(), Is.True, "The existing physical door must offer E to the visitor.");
                yield return WaitWorkroomDoor(village, report, true);
                report.door_opened_outside = door.IsOpen;
                Assert.That(report.door_opened_outside, Is.True);
                WorkroomFrame(camera, room.Plan, "01-door-open", new Vector3(1.4f, 1.6f, 4.5f), new Vector3(0f, 1f, 2.5f), 55f);
                yield return WalkWorkroomLeg(village, door.ThresholdDock, "threshold", report);
                yield return WalkWorkroomLeg(village, room.Plan.Anchor("RoomEntry"), "room entry", report);
                report.doorway_walked = true;
                Assert.That(room.Environment.IsInside, Is.True);
                village.Player.Motor.SetInputEnabled(true); village.Player.Interactor.SetInputEnabled(true);
                Assert.That(room.Door.Begin(), Is.True, "The guest must be able to close their own door from inside.");
                yield return WaitWorkroomDoor(village, report, false);
                Assert.That(door.IsClosed, Is.True, "The guest's safe operating dock must clear the real swept leaf.");
                report.door_closed_inside = true;
                yield return WalkWorkroomLeg(village, room.Plan.Anchor("RoomEntry"), "clear the inside door dock", report);
                for (int frame = 0; frame < 90; frame++) yield return null;
                room.Environment.Advance(1f);
                report.inside_cold_weight = hero.ColdBodyWeight;
                Assert.That(report.inside_cold_weight, Is.LessThan(.05f), "The closed room must suppress the same exterior cold layer.");
                Assert.That(room.Environment.Enclosure, Is.GreaterThan(.98f));
                AssertWorkroomParticleCulling(village, room, report);
                Assert.That(room.Environment.TryPlayFootstep(room.Plan.Anchor("RoomEntry"), 0f), Is.True);

                // 0.02-second actual state-machine steps retain contact windows;
                // batches advance four simulated seconds per rendered frame.
                for (int step = 0; step < 8000 &&
                    !(room.RepairAction == VillageResidentAction.RepairWork && room.RepairActive && observation.Actions.Contains("SewingWork")); step++)
                {
                    AdvanceWorkroom(village, workers, report, observation, camera);
                    if (step % 200 == 199) yield return null;
                }
                Assert.That(room.RepairActive && room.RepairAction == VillageResidentAction.RepairWork, Is.True,
                    "The same repair neighbour must reach the real workbench.");
                Assert.That(observation.Actions, Does.Contain("SewingWork"));
                Assert.That(room.ChairFixed, Is.False, "The finite chair must still be available for the early voluntary help.");
                yield return WalkWorkroomLeg(village, room.Plan.Anchor("PlayerHelpDock"), "helper dock", report);
                village.Player.Motor.SetInputEnabled(true); village.Player.Interactor.SetInputEnabled(true);
                Assert.That(room.Help.BeginHelp(), Is.True);
                yield return WaitWorkroomPlayer(village, workers, report, observation, camera,
                    () => room.Help.Controller.Phase == PlayerAnimatedInteractionPhase.Entering && room.Help.Controller.PhaseProgress > .35f,
                    "help entry before cancellation", 240);
                Assert.That(room.Help.Cancel(), Is.True);
                yield return null;
                Assert.That(room.HelpReserved, Is.False);
                Assert.That(room.ChairFixed, Is.False, "Cancelling entry cannot commit the chair repair.");
                Assert.That(village.Player.Motor.InputEnabled, Is.True);
                report.cancelled_help_not_committed = true;

                Assert.That(room.Help.BeginHelp(), Is.True);
                yield return WaitWorkroomPlayer(village, workers, report, observation, camera,
                    () => room.Help.IsHolding, "two-hand help loop", 240);
                yield return null;
                AssertWorkroomHeroGrips(room, report);
                WorkroomFrame(camera, room.Plan, "04-player-held-chair-part", new Vector3(.7f, 1.6f, -.45f), new Vector3(-1.4f, .95f, -1.7f), 66f);
                yield return WaitWorkroomPlayer(village, workers, report, observation, camera,
                    () => !room.Help.OwnsActiveInteraction, "full help exit", 650);
                yield return null;
                Assert.That(room.HelpReserved, Is.False);
                Assert.That(room.ChairFixed, Is.True, "The complete six-second hold and neighbouring stroke must fix this chair.");
                Assert.That(village.Player.Motor.InputEnabled, Is.True);
                Assert.That(Vector3.Distance(village.Player.GameObject.transform.position, room.Help.Plan.ExitRootPosition), Is.LessThan(.035f));
                report.help_completed = true;
                AssertWorkroomStandingFeet(hero.Registry, room.Plan.House.GroundCenter.y);

                yield return WalkWorkroomLeg(village, room.Plan.World(new Vector3(-.65f, 0f, -.1f)), "open middle aisle", report);
                yield return WalkWorkroomLeg(village, room.Plan.Anchor("BenchApproach"), "bench approach", report);
                village.Player.Motor.SetInputEnabled(true); village.Player.Interactor.SetInputEnabled(true);
                Assert.That(room.Bench.CanInteract(village.Player.Interactor), Is.True);
                room.Bench.Interact(village.Player.Interactor);
                yield return WaitWorkroomPlayer(village, workers, report, observation, camera,
                    () => room.Bench.IsSeated, "bench enter", 300);
                yield return null;
                hero.ReapplyLatePresentationPose();
                room.Bench.Controller.RefreshActiveClipAlignment();
                Assert.That(Vector3.Distance(hero.Registry.Anchors.Pelvis.position, room.Bench.Plan.ActionHipPosition), Is.LessThan(.035f));
                Assert.That(room.Bench.Plan.ActionHipPosition.y - room.Plan.Anchor("BenchSeat").y,
                    Is.EqualTo(CityBenchSitPlan.SeatClearance).Within(.002f));
                WorkroomFrame(camera, room.Plan, "05-player-bench", new Vector3(-.2f, 1.6f, .0f), new Vector3(-2.8f, .85f, -.1f), 62f);
                Assert.That(room.Bench.RequestExit(), Is.True);
                yield return WaitWorkroomPlayer(village, workers, report, observation, camera,
                    () => !room.Bench.OwnsActiveInteraction, "bench stand", 300);
                yield return null;
                Assert.That(village.Player.Motor.InputEnabled, Is.True);
                AssertWorkroomStandingFeet(hero.Registry, room.Plan.House.GroundCenter.y);
                report.bench_seated_and_stood = true;
                yield return WalkWorkroomLeg(village, room.Plan.World(new Vector3(-.65f, 0f, -.1f)), "leave the work lanes free", report);

                int completedSewing = room.SewingCycles;
                for (int step = 0; step < 5000; step++)
                {
                    AdvanceWorkroom(village, workers, report, observation, camera);
                    if (step % 200 == 199) yield return null;
                    if (room.SewingCycles > completedSewing && observation.Actions.Contains("SewingStow") &&
                        observation.Actions.Contains("RepairPutTool") && step > 600) break;
                }
                Assert.That(room.ChairFixed, Is.True);
                Assert.That(Vector3.Distance(room.RepairRail.position, room.Plan.Anchor("RepairRailFixed")), Is.LessThan(.003f));
                Assert.That(room.SewingCycles, Is.GreaterThan(0));
                Assert.That(observation.Actions, Does.Contain("SewingFold"));
                Assert.That(observation.Actions, Does.Contain("SewingStow"));
                Assert.That(observation.Actions, Does.Contain("RepairPutTool"));
                Assert.That(report.cloth_fold_range_degrees, Is.GreaterThan(40f), "The actual cloth flap must fold, rather than only changing task labels.");
                Assert.That(report.box_lid_range_degrees, Is.GreaterThan(30f), "The separate box lid must move on its real hinge.");
                Assert.That(report.npc_contact_samples, Is.GreaterThan(100));
                Assert.That(report.indoor_walk_body_samples, Is.GreaterThan(100));
                Assert.That(report.player_contact_samples, Is.GreaterThan(100));
                Assert.That(report.door_contact_samples, Is.GreaterThan(0));
                Assert.That(report.sewing_stowed, Is.True, "Observe the real final stow pose, not just the next task label.");
                Assert.That(room.Room.Parts.Count, Is.EqualTo(originalParts.Count));
                foreach (var pair in originalParts) Assert.That(room.Room.Part(pair.Key), Is.SameAs(pair.Value), "A finite work prop was replaced: " + pair.Key);
                report.chair_fixed = true; report.same_two_visible_residents = true;
                report.sewing_cycles = room.SewingCycles; report.repair_strikes = room.RepairStrikes;
                report.observed_actions = observation.Actions.OrderBy(value => value).ToArray();
                WorkroomFrame(camera, room.Plan, "08-room-after-help", new Vector3(.6f, 1.7f, .8f), new Vector3(-2f, 1f, -.2f), 78f);
                yield return VerifyWorkroomEvening(village, workers, report, observation, camera);
                // Finish with the actual room camera that a visitor receives,
                // rather than only the inspection viewpoints.
                camera.fieldOfView = cameraFov;
                village.CameraFollow.enabled = followEnabled;
                for (int settle = 0; settle < 30; settle++) yield return null;
                Assert.That(room.CameraController.OwnsCamera, Is.True, "The occupied room must own the ordinary player's interior camera.");
                Assert.That(room.CameraController.CurrentShot, Is.EqualTo(VillageWorkroomCameraShot.MainRoom));
                Assert.That(Vector3.Distance(camera.transform.position,
                    village.Player.GameObject.transform.position + Vector3.up * 1.4f), Is.GreaterThan(1.4f),
                    "The room camera must frame the visitor instead of entering his shoulder against the wall.");
                CaptureCurrentCamera(camera, "VillageWorkroom", "11-player-camera");
                report.sewing_cycles = room.SewingCycles; report.repair_strikes = room.RepairStrikes;
                report.observed_actions = observation.Actions.OrderBy(value => value).ToArray();
                string folder = Path.Combine(Directory.GetCurrentDirectory(), "Captures", "VillageWorkroom");
                Directory.CreateDirectory(folder);
                string json = JsonUtility.ToJson(report, true);
                File.WriteAllText(Path.Combine(folder, "verification.json"), json);
                TestContext.Out.WriteLine(json);
            }
            finally
            {
                if (village != null)
                {
                    village.Workroom?.Help?.Cancel(); village.Workroom?.Door?.Cancel();
                    village.Player.GameObject.GetComponent<PlayerAnimatedInteractionController>()?.CancelActiveInteraction();
                    village.Player.Motor.CancelInteractionPoseMove(); village.Player.Motor.SetInputEnabled(true);
                    village.Player.Interactor.SetInputEnabled(true); village.Life.enabled = lifeEnabled;
                    village.CameraFollow.enabled = followEnabled;
                }
                if (camera != null) { camera.transform.SetPositionAndRotation(cameraPosition, cameraRotation); camera.fieldOfView = cameraFov; }
                Time.captureDeltaTime = previousDelta;
            }
        }

        private static IEnumerator VerifyWorkroomEvening(AlpineVillageRoot village, VillageNeighbourState[] workers,
            VillageWorkroomReport report, WorkroomObservation observation, Camera camera)
        {
            VillageWorkroomController room = village.Workroom;
            VillageResidentDoor door = room.Door.Door;
            // Stand in the east side of the room, clear of both work routes
            // and the threshold-to-private-room passage. This remains a walked
            // leg of the same visitor's journey, with the same real capsule.
            yield return WalkWorkroomLeg(village, room.Plan.World(new Vector3(0f, 0f, -.1f)),
                "cross the free middle aisle toward the guest dock", report);
            yield return WalkWorkroomLeg(village, room.Plan.World(new Vector3(.85f, 0f, -.45f)),
                "guest waits clear of evening return routes", report);
            Vector3 guestPosition = village.Player.GameObject.transform.position;
            CharacterController guestBody = village.Player.GameObject.GetComponent<CharacterController>();
            Assert.That(guestBody, Is.Not.Null);
            void AssertGuestClear(Vector3 first, Vector3 second)
            {
                Vector3 from = first - guestPosition, segment = second - first;
                from.y = 0f; segment.y = 0f;
                float along = segment.sqrMagnitude > .000001f ? Mathf.Clamp01(-Vector3.Dot(from, segment) / segment.sqrMagnitude) : 0f;
                Assert.That((from + segment * along).magnitude, Is.GreaterThan(.84f),
                    "The arrived guest must clear the residents' real return route and HeroBlocks radius.");
            }
            AssertGuestClear(room.Plan.World(new Vector3(-.65f, 0f, -.65f)), door.InteriorDock);
            foreach (int slot in new[] { 0, 1 })
            {
                Vector3[] route = door.GetInteriorRoute(slot);
                for (int index = 1; index < route.Length; index++) AssertGuestClear(route[index - 1], route[index]);
            }
            AssertGuestClear(door.GetOperatingDock(0f, true), door.InteriorDock);
            AssertGuestClear(door.GetOperatingDock(1f, true), door.InteriorDock);
            Assert.That(room.Environment.IsInside, Is.True);
            bool openedForGuest = door.IsOpen;
            bool playerOwnsOpening = false;
            bool outsideWorkerObserved = workers.Any(worker => worker.IsOutside);
            bool returnCaptured = false;
            bool bothHome = false;
            try
            {
                for (int step = 0; step < 15000; step++)
                {
                    // Establish an already-open physical door, then release its
                    // reservation so either resident may finish their passage.
                    // Repeated NPC OpenDoor tasks must preserve this fraction;
                    // CloseDoor tasks must finish without locking the guest in.
                    if (!openedForGuest && (playerOwnsOpening || door.TryReserve(village.Player.GameObject.transform)))
                    {
                        if (!playerOwnsOpening)
                        {
                            playerOwnsOpening = true;
                            Assert.That(door.Open(village.Player.GameObject.transform), Is.True);
                        }
                        door.Advance(VillageLifeStep);
                        if (door.IsOpen)
                        {
                            door.Release(village.Player.GameObject.transform);
                            playerOwnsOpening = false; openedForGuest = true;
                        }
                    }
                    foreach (VillageNeighbourState worker in workers)
                    {
                        outsideWorkerObserved |= worker.IsOutside;
                        if (worker.Task == VillageNeighbourTask.CloseDoor) report.evening_close_attempts++;
                    }
                    village.Life.Advance(VillageLifeStep, 1200d, .05f, Vector3.forward);
                    report.simulated_seconds += VillageLifeStep;
                    report.evening_simulated_seconds += VillageLifeStep;
                    ObserveWorkroom(room, workers, report, observation, camera);
                    Assert.That(guestBody.enabled && guestBody.detectCollisions, Is.True,
                        "The waiting guest must retain the real player collision body.");
                    Assert.That(room.Environment.IsInside, Is.True, "The guest must stay in the same real room throughout the evening return.");
                    Assert.That(Vector3.Distance(guestPosition, village.Player.GameObject.transform.position), Is.LessThan(.025f),
                        "Evening scheduling must not relocate the guest to clear a passage.");
                    if (openedForGuest)
                        Assert.That(door.OpenFraction, Is.GreaterThanOrEqualTo(.999f),
                            "A worker closed or restarted an already-open house-08 door while its guest remained inside.");
                    if (!returnCaptured && workers.Any(worker => worker.Task == VillageNeighbourTask.Walk &&
                        room.Plan.ContainsInterior(worker.Actor.transform.position)))
                    {
                        WorkroomFrame(camera, room.Plan, "09-evening-return-passage", new Vector3(.65f, 1.65f, .85f),
                            new Vector3(-.25f, 1f, -1.3f), 72f);
                        returnCaptured = true;
                    }
                    bothHome = workers.All(worker => worker.IsHome &&
                        Vector3.Distance(worker.Actor.transform.position,
                            door.HiddenDocks[worker.Role == VillageResidentRole.RepairNeighbor ? 0 : 1]) < .06f);
                    if (bothHome && openedForGuest) break;
                    if (step % 200 == 199) yield return null;
                }
                Assert.That(bothHome, Is.True, "The two workers must finish work or their current outside visit and walk home by 20:00: " +
                    string.Join("; ", workers.Select(worker => $"{worker.Role}/{worker.Task} outside={worker.IsOutside} " +
                        $"blocked={worker.IsBlocked} position={worker.Actor.transform.position:F4}")));
                Assert.That(room.RepairActive || room.SewingActive, Is.False);
                Assert.That(openedForGuest && door.IsOpen, Is.True);
                foreach (VillageNeighbourState worker in workers)
                {
                    Assert.That(worker.Actor.gameObject.activeInHierarchy, Is.True);
                    Assert.That(door.IsConcealed(worker.Actor.transform.position), Is.True);
                    AssertVillageHomeOcclusion(door, worker.Actor.transform.position + Vector3.up * .95f);
                    AssertVillageHomeOcclusion(door, worker.Actor.Head.position);
                }
                if (outsideWorkerObserved)
                    Assert.That(report.evening_close_attempts, Is.GreaterThan(0),
                        "An outside worker must exercise the guarded door-close step on the way home.");
                report.evening_workers_home = true;
                report.evening_guest_door_stayed_open = true;
                report.evening_outside_worker_returned = outsideWorkerObserved;
                WorkroomFrame(camera, room.Plan, "10-evening-guest-open-door", new Vector3(-.5f, 1.65f, -.5f),
                    new Vector3(.2f, 1.1f, 2.7f), 70f);
                TestContext.Out.WriteLine($"Workroom evening: both original workers walked behind their own solid partition in " +
                    $"{report.evening_simulated_seconds:F2}s; guest remained inside; guarded close samples={report.evening_close_attempts}.");
            }
            finally
            {
                if (playerOwnsOpening) door.Release(village.Player.GameObject.transform);
            }
        }

        private static void AssertWorkroomGeometry(VillageWorkroomController room, VillageWorkroomReport report)
        {
            Physics.SyncTransforms();
            Assert.That(room.Room.HouseRoot, Is.SameAs(room.Door.Door.HouseRoot));
            report.solid_parts = room.Room.SolidColliders.Count;
            Assert.That(report.solid_parts, Is.GreaterThan(12));
            foreach (Collider solid in room.Room.SolidColliders)
            {
                Assert.That(solid, Is.TypeOf<MeshCollider>());
                Assert.That(solid.enabled && !solid.isTrigger && solid.gameObject.activeInHierarchy, Is.True);
                Assert.That(((MeshCollider)solid).sharedMesh, Is.Not.Null);
            }
            foreach (string name in new[] { "Floor", "Ceiling", "Bench", "Workbench", "SewingSeat", "RepairRail", "ClothFlap" })
                Assert.That(room.Room.Part(name).GetComponentInChildren<MeshRenderer>().enabled, Is.True, name);
            Transform roof = room.Room.HouseRoot.Find("House Roof");
            Assert.That(roof, Is.Not.Null);
            Assert.That(roof.GetComponent<MeshRenderer>().enabled && roof.gameObject.activeInHierarchy, Is.True,
                "The room must retain the real exterior roof above its solid ceiling.");
            Assert.That(roof.GetComponent<MeshFilter>().sharedMesh.triangles.Length, Is.GreaterThan(30));
            Vector3 clear = room.Plan.Anchor("RoomEntry") + Vector3.up;
            Assert.That(Physics.Raycast(clear, Vector3.down, out RaycastHit floor, 1.5f, ~0, QueryTriggerInteraction.Ignore), Is.True);
            Assert.That(floor.transform.IsChildOf(room.Room.HouseRoot), Is.True,
                $"A real workroom floor must support the entry; hit {floor.transform.parent?.name}/{floor.transform.name} at {floor.point}, floor origin {room.Plan.House.GroundCenter}.");
            Assert.That(Physics.Raycast(clear, Vector3.up, out RaycastHit ceiling, 5f, ~0, QueryTriggerInteraction.Ignore), Is.True);
            Assert.That(ceiling.transform.IsChildOf(room.Room.HouseRoot), Is.True, "The finished lower storey must retain a solid overhead boundary.");
            Assert.That(ceiling.point.y - room.Plan.House.GroundCenter.y, Is.InRange(2.28f, 2.42f));
            report.floor_and_ceiling_solid = true;
            foreach (string name in new[] { "FrontWindow", "LeftWindow" })
            {
                Transform glass = room.Room.Part(name + "Glass");
                Assert.That(glass.GetComponentInChildren<Renderer>().enabled, Is.True);
                // Aim clear of the centre mullion; all intervening solid hits
                // must be the thin real pane, not an uncut facade behind it.
                Vector3 outward = room.Plan.Rotation * (name == "FrontWindow" ? Vector3.forward : Vector3.left);
                Vector3 offset = room.Plan.Rotation * (name == "FrontWindow" ? new Vector3(.22f, 0f, 0f) : new Vector3(0f, 0f, .18f));
                Vector3 middle = room.Plan.Anchor(name) + offset;
                RaycastHit[] hits = Physics.RaycastAll(middle - outward * .5f, outward, 1f, ~0, QueryTriggerInteraction.Ignore);
                Assert.That(hits.Any(hit => hit.transform.IsChildOf(glass)), Is.True, "The window has no real solid pane: " + name);
                foreach (RaycastHit hit in hits)
                    Assert.That(hit.transform.IsChildOf(glass), Is.True, $"Uncut wall or other obstruction behind {name}: {hit.transform.name}");
                report.window_panes++;
            }
            report.windows_physically_open = true;
        }

        private static void AdvanceWorkroom(AlpineVillageRoot village, VillageNeighbourState[] workers,
            VillageWorkroomReport report, WorkroomObservation observation, Camera camera, float dt = VillageLifeStep)
        {
            village.Life.Advance(dt, 600d, .05f, Vector3.forward);
            report.simulated_seconds += dt;
            ObserveWorkroom(village.Workroom, workers, report, observation, camera);
        }

        private static void ObserveWorkroom(VillageWorkroomController room, VillageNeighbourState[] workers,
            VillageWorkroomReport report, WorkroomObservation observation, Camera camera)
        {
            bool collisionPosesSynced = false;
            foreach (VillageNeighbourState state in workers)
            {
                VillageResidentPresentation actor = state.Actor;
                Assert.That(observation.LastRoots.ContainsKey(actor), Is.True, "A workroom resident was replaced.");
                Assert.That(actor.gameObject.activeInHierarchy, Is.True);
                foreach (Renderer renderer in observation.VisibleParts[actor])
                    Assert.That(renderer.enabled && renderer.gameObject.activeInHierarchy, Is.True, "A worker was hidden instead of walking.");
                Assert.That(Vector3.Distance(observation.LastRoots[actor], actor.transform.position), Is.LessThan(.08f),
                    $"Resident root jumped: {state.Role}/{state.Task} at {actor.transform.position}");
                observation.LastRoots[actor] = actor.transform.position;
                // Use the live walking capsule only. Seated and working poses
                // have separate visible-body/contact contracts below.
                if (state.Task == VillageNeighbourTask.Walk && room.Plan.ContainsInterior(actor.transform.position))
                {
                    if (!collisionPosesSynced) { Physics.SyncTransforms(); collisionPosesSynced = true; }
                    CapsuleCollider body = state.Body;
                    Assert.That(body, Is.Not.Null, "The indoor walker needs its actual body capsule: " + state.Role);
                    Assert.That(body.enabled && !body.isTrigger && body.gameObject.activeInHierarchy, Is.True);
                    report.indoor_walk_body_samples++;
                    foreach (Collider solid in room.Room.SolidColliders)
                    {
                        if (solid == null || !solid.enabled || solid.isTrigger || !solid.gameObject.activeInHierarchy ||
                            solid == body || solid.transform.IsChildOf(actor.transform) || !body.bounds.Intersects(solid.bounds)) continue;
                        report.indoor_walk_body_pairs++;
                        if (!Physics.ComputePenetration(body, body.transform.position, body.transform.rotation,
                            solid, solid.transform.position, solid.transform.rotation, out Vector3 direction, out float depth)) continue;
                        report.maximum_indoor_walk_overlap_metres = Mathf.Max(report.maximum_indoor_walk_overlap_metres, depth);
                        // The same numerical allowance as AssertVillageNeighbourBodyClearance.
                        if (depth <= .025f) continue;
                        string path = solid.name;
                        for (Transform parent = solid.transform.parent; parent != null; parent = parent.parent)
                            path = parent.name + "/" + path;
                        Assert.Fail($"Indoor walking body intersects real room geometry: Role={state.Role}; Task={state.Task}; " +
                            $"Collider={path}; Root={actor.transform.position:F4}; LocalRoot={room.Plan.Local(actor.transform.position):F4}; " +
                            $"CapsuleCenter={body.transform.TransformPoint(body.center):F4}; " +
                            $"Overlap={depth:F4}m; SeparationDirection={direction:F4}. Allowed numerical overlap is 0.025m.");
                    }
                }
                string action = actor.CurrentAction.ToString();
                if (!action.StartsWith("Repair", StringComparison.Ordinal) && !action.StartsWith("Sewing", StringComparison.Ordinal)) continue;
                observation.Actions.Add(action);
                VillageWorkroomFrame frame = VillageResidentPresentation.SampleWorkroomProps(actor.CurrentAction, actor.CurrentActionSeconds);
                if (frame.RightContactWeight >= .9999f)
                {
                    WorkroomContact(actor.RightGrip.position, actor.transform.position + actor.transform.rotation * frame.RightContact, state, report);
                    AssertWorkroomPhysicalContact(room, actor.RightGrip.position, frame.RightContactKind, false, state);
                }
                if (frame.LeftContactWeight >= .9999f)
                {
                    Vector3 target = actor.transform.position + actor.transform.rotation * frame.LeftContact;
                    if (frame.LeftContactKind == VillageWorkroomContact.Chair)
                        target += room.RepairRail.position - room.Plan.Anchor("RepairRailRest");
                    WorkroomContact(actor.LeftGrip.position, target, state, report);
                    AssertWorkroomPhysicalContact(room, actor.LeftGrip.position, frame.LeftContactKind, true, state);
                }
                if (action.StartsWith("Repair", StringComparison.Ordinal))
                    WorkroomProp(room.Room.Part("Hammer"), frame.Hammer, actor.transform, report);
                else
                {
                    WorkroomProp(room.Room.Part("Mitten"), frame.Mitten, actor.transform, report);
                    WorkroomProp(room.Room.Part("Cloth"), frame.Cloth, actor.transform, report);
                    WorkroomProp(WorkroomBox(room), frame.Box, actor.transform, report);
                    if (action == "SewingWork")
                    {
                        Transform pelvis = CityPedestrianHandProps.FindSocket(actor.ModelRoot, "pelvis");
                        Vector3 seat = room.Plan.Anchor("SewingSeat");
                        Assert.That(Vector2.Distance(new Vector2(pelvis.position.x, pelvis.position.z),
                            new Vector2(seat.x, seat.z)), Is.LessThan(.06f), "The actual seated pelvis must remain above its stool.");
                        Assert.That(pelvis.position.y - seat.y, Is.InRange(.025f, .17f));
                        foreach (string side in new[] { "L", "R" })
                        {
                            Transform foot = CityPedestrianHandProps.FindSocket(actor.ModelRoot, "foot." + side);
                            Assert.That(foot.position.y - room.Plan.House.GroundCenter.y, Is.InRange(.005f, .24f),
                                "A seated worker's feet must remain at the floor, not follow the descending pelvis.");
                        }
                    }
                    if (action == "SewingStow" && actor.CurrentActionSeconds > 4.8f)
                    {
                        report.sewing_stowed = true;
                        if (observation.Captured.Add("SewingStowed"))
                            WorkroomFrame(camera, room.Plan, "07-real-sewing-stow", new Vector3(-.4f, 1.45f, .55f),
                                room.Plan.Local(WorkroomBox(room).position) + Vector3.up * .08f, 48f);
                    }
                }
                if (actor.CurrentActionSeconds > .7f &&
                    (action == "RepairWork" || action == "SewingWork" || action == "SewingFold" || action == "SewingStow") && observation.Captured.Add(action))
                    WorkroomFrame(camera, room.Plan, "worker-" + action.ToLowerInvariant(), new Vector3(.7f, 1.65f, .15f),
                        room.Plan.Local(actor.Head.position) - Vector3.up * .45f, 70f);
            }
            foreach (string key in new[] { "ClothFlap", "BoxLid" })
            {
                Transform part = key == "ClothFlap" ? room.Room.Part(key) : WorkroomBoxLid(room);
                if (!observation.FirstRotations.TryGetValue(key, out Quaternion initial))
                    observation.FirstRotations.Add(key, initial = part.localRotation);
                float range = Quaternion.Angle(initial, part.localRotation);
                if (key == "ClothFlap") report.cloth_fold_range_degrees = Mathf.Max(report.cloth_fold_range_degrees, range);
                else report.box_lid_range_degrees = Mathf.Max(report.box_lid_range_degrees, range);
            }
        }

        private static Transform WorkroomBox(VillageWorkroomController room) => room.Room.Parts.TryGetValue("Box", out Transform part)
            ? part : room.Room.Part("SewingBox");
        private static Transform WorkroomBoxLid(VillageWorkroomController room) => room.Room.Parts.TryGetValue("BoxLid", out Transform part)
            ? part : room.Room.Part("SewingBoxLid");

        private static void AssertWorkroomPhysicalContact(VillageWorkroomController room, Vector3 grip,
            VillageWorkroomContact kind, bool left, VillageNeighbourState state)
        {
            Transform item;
            string anchor;
            switch (kind)
            {
                case VillageWorkroomContact.None: return;
                case VillageWorkroomContact.Hammer: item = room.Room.Part("Hammer"); anchor = "Grip"; break;
                case VillageWorkroomContact.Chair: item = room.RepairRail; anchor = "LeftSupport"; break;
                case VillageWorkroomContact.Mitten: item = room.Room.Part("Mitten"); anchor = left ? "Grip" : "Stitch"; break;
                case VillageWorkroomContact.Cloth:
                    item = room.Room.Part("Cloth");
                    anchor = left ? "PacketLeftGrip" : "PacketRightGrip";
                    if (!left && Vector3.Distance(item.Find("ANCHOR_HoldEdge").position, grip) <
                        Vector3.Distance(item.Find("ANCHOR_PacketRightGrip").position, grip)) anchor = "HoldEdge";
                    break;
                case VillageWorkroomContact.Box: item = WorkroomBox(room); anchor = left ? "LeftGrip" : "RightGrip"; break;
                case VillageWorkroomContact.BoxLid: item = WorkroomBoxLid(room); anchor = "LidGrip"; break;
                case VillageWorkroomContact.ClothFlap: item = room.Room.Part("ClothFlap"); anchor = "EdgeGrip"; break;
                case VillageWorkroomContact.Thread: item = room.Room.Part("Thread"); anchor = "End"; break;
                default: throw new ArgumentOutOfRangeException(nameof(kind));
            }
            Transform physical = item.Find("ANCHOR_" + anchor);
            Assert.That(physical, Is.Not.Null, $"Missing real contact on {item.name}: {anchor}");
            Assert.That(Vector3.Distance(physical.position, grip), Is.LessThan(.035f),
                $"Hand and visible prop disagree: {state.Role}/{state.Actor.CurrentAction}@{state.Actor.CurrentActionSeconds:F3}, {kind}/{anchor}");
        }

        private static void WorkroomContact(Vector3 actual, Vector3 target, VillageNeighbourState state, VillageWorkroomReport report)
        {
            float error = Vector3.Distance(actual, target);
            report.npc_contact_samples++; report.maximum_npc_grip_error_metres = Mathf.Max(report.maximum_npc_grip_error_metres, error);
            Assert.That(error, Is.LessThan(.035f), $"Actual hand contact: {state.Role}/{state.Actor.CurrentAction}@{state.Actor.CurrentActionSeconds:F3}, {actual} vs {target}");
        }

        private static void WorkroomProp(Transform actual, Pose authored, Transform actor, VillageWorkroomReport report)
        {
            float error = Vector3.Distance(actual.position, actor.position + actor.rotation * authored.position);
            report.physical_prop_samples++; report.maximum_prop_error_metres = Mathf.Max(report.maximum_prop_error_metres, error);
            Assert.That(error, Is.LessThan(.004f), "Authored frame disagrees with the separate actual " + actual.name);
            Assert.That(Quaternion.Angle(actual.rotation, actor.rotation * authored.rotation), Is.LessThan(.5f), actual.name);
        }

        private static IEnumerator WaitWorkroomPlayer(AlpineVillageRoot village, VillageNeighbourState[] workers,
            VillageWorkroomReport report, WorkroomObservation observation, Camera camera, Func<bool> ready, string label, int maximumFrames)
        {
            Vector3 previous = village.Player.GameObject.transform.position;
            for (int frame = 0; frame < maximumFrames && !ready(); frame++)
            {
                AdvanceWorkroom(village, workers, report, observation, camera, Time.deltaTime);
                yield return null;
                ObserveWorkroomHeroStep(village, previous, report, label);
                previous = village.Player.GameObject.transform.position;
                if (village.Workroom.Help.IsHolding && village.Workroom.Help.Controller.PhaseProgress > .015f)
                    AssertWorkroomHeroGrips(village.Workroom, report);
                Assert.That(village.Player.PresentationVisibility.RenderersHidden, Is.False, "Contextual work must retain the visible production hero.");
            }
            Assert.That(ready(), Is.True, "Workroom interaction stalled: " + label);
        }

        private static IEnumerator WaitWorkroomDoor(AlpineVillageRoot village, VillageWorkroomReport report, bool opening)
        {
            VillageWorkroomDoorInteraction interaction = village.Workroom.Door;
            Vector3 previous = village.Player.GameObject.transform.position;
            float maximumError = 0f;
            int contactSamples = 0;
            string maximumDiagnostic = string.Empty;
            for (int frame = 0; frame < 650 && interaction.OwnsActiveInteraction; frame++)
            {
                yield return null;
                ObserveWorkroomHeroStep(village, previous, report, "door"); previous = village.Player.GameObject.transform.position;
                // Coroutine continuation runs after Update's graph sampling but
                // before LateUpdate. Apply the production late seam to this
                // same frame; read neither stale ContactWeight nor pre-IK bones.
                if (interaction.Controller.Phase != PlayerAnimatedInteractionPhase.Looping) continue;
                interaction.RefreshHandContact();
                if (interaction.ContactWeight >= .9999f)
                {
                    float error = Vector3.Distance(interaction.RightGrip.position, interaction.Door.Handle.position);
                    report.door_contact_samples++; report.maximum_door_grip_error_metres = Mathf.Max(report.maximum_door_grip_error_metres, error);
                    contactSamples++;
                    if (error >= maximumError)
                    {
                        maximumError = error;
                        maximumDiagnostic = $"{(opening ? "opening" : "closing")} phase={interaction.Controller.Phase} " +
                            $"progress={interaction.Controller.PhaseProgress:F4}, root={previous:F4}, dock={interaction.Plan.EntryRootPosition:F4}, " +
                            $"grip={interaction.RightGrip.position:F4}, handle={interaction.Door.Handle.position:F4}, " +
                            $"wristDistance={interaction.RightWristDistance:F4}, armReach={interaction.RightReachLimit:F4}";
                    }
                    if (contactSamples == 1)
                    {
                        Transform actor = village.Player.GameObject.transform;
                        WorkroomFrame(Camera.main, village.Workroom.Plan, opening ? "door-hand-open" : "door-hand-close",
                            opening ? village.Workroom.Plan.Local(actor.position + actor.right * 1.2f - actor.forward * .8f + Vector3.up * 1.45f)
                                : new Vector3(-.8f, 1.5f, .5f),
                            village.Workroom.Plan.Local(interaction.Door.Handle.position + Vector3.up * .14f), 46f);
                        TestContext.Out.WriteLine("Post-LateUpdate door contact: " + maximumDiagnostic + $", error={error:F6}m.");
                    }
                }
            }
            Assert.That(contactSamples, Is.GreaterThan(0), "Observe the actual complete household handle grip.");
            Assert.That(maximumError, Is.LessThan(.05f), "The visible hero's hand must reach the actual household handle. " + maximumDiagnostic);
            Assert.That(interaction.OwnsActiveInteraction, Is.False, "Physical door ownership leaked.");
            Assert.That(opening ? interaction.Door.IsOpen : interaction.Door.IsClosed, Is.True,
                "The real door did not finish its unobstructed " + (opening ? "opening" : "closing"));
            Assert.That(interaction.Door.Occupant, Is.Null);
            Assert.That(village.Player.Motor.InputEnabled, Is.True);
        }

        private static IEnumerator WalkWorkroomLeg(AlpineVillageRoot village, Vector3 target, string label, VillageWorkroomReport report)
        {
            CharacterController body = village.Player.GameObject.GetComponent<CharacterController>();
            Assert.That(body, Is.Not.Null);
            village.Player.Motor.SetInputEnabled(false);
            Vector3 previous = village.Player.GameObject.transform.position;
            bool arrived = false;
            for (int frame = 0; frame < 300 && !arrived; frame++)
            {
                Assert.That(body.enabled && body.detectCollisions, Is.True, "The real player capsule must collide while walking " + label);
                arrived = village.Player.Motor.MoveTowardsApproachWaypoint(target, .035f, .04f);
                yield return null;
                Assert.That(body.enabled && body.detectCollisions, Is.True, "Player collisions were disabled during " + label);
                ObserveWorkroomHeroStep(village, previous, report, label); previous = village.Player.GameObject.transform.position;
                Assert.That(village.Player.Motor.InteractionPoseMoveStalled, Is.False,
                    $"Real room passage blocked: {label}, {previous}, target {target}");
            }
            village.Player.Motor.CancelInteractionPoseMove(); village.Player.Motor.SetInputEnabled(true);
            Assert.That(arrived, Is.True, "The real capsule could not walk " + label);
        }

        private static void ObserveWorkroomHeroStep(AlpineVillageRoot village, Vector3 previous, VillageWorkroomReport report, string label)
        {
            float distance = Vector3.Distance(previous, village.Player.GameObject.transform.position);
            report.maximum_player_root_step_metres = Mathf.Max(report.maximum_player_root_step_metres, distance);
            Assert.That(distance, Is.LessThan(.17f), $"Hero root discontinuity during {label}: {distance:F4} m");
        }

        private static void AssertWorkroomHeroGrips(VillageWorkroomController room, VillageWorkroomReport report)
        {
            room.Help.RefreshHandContacts();
            Transform left = room.RepairRail.Find("ANCHOR_ChairPartLeftGrip"), right = room.RepairRail.Find("ANCHOR_ChairPartRightGrip");
            Assert.That(left, Is.Not.Null); Assert.That(right, Is.Not.Null);
            float error = Mathf.Max(Vector3.Distance(left.position, room.Help.LeftGrip.position), Vector3.Distance(right.position, room.Help.RightGrip.position));
            report.player_contact_samples++; report.maximum_player_grip_error_metres = Mathf.Max(report.maximum_player_grip_error_metres, error);
            Assert.That(error, Is.LessThan(.025f), "The helper must hold the same real chair rail while the neighbour fixes it.");
        }

        private static void AssertWorkroomStandingFeet(Player3DAssetRegistry hero, float floorY)
        {
            hero.GetComponentInParent<Player3DCharacterPresentation>()?.ReapplyLatePresentationPose();
            Assert.That(hero.Anchors.LeftFoot.position.y - floorY, Is.InRange(.01f, .25f));
            Assert.That(hero.Anchors.RightFoot.position.y - floorY, Is.InRange(.01f, .25f));
            Assert.That(Vector3.Distance(hero.Anchors.LeftFoot.position, hero.Anchors.RightFoot.position), Is.InRange(.08f, .4f));
        }

        private static void AssertWorkroomParticleCulling(AlpineVillageRoot village, VillageWorkroomController room, VillageWorkroomReport report)
        {
            int before = room.Environment.CulledParticles;
            foreach (ParticleSystem system in new[] { village.Snow.Particles, village.Fog.Particles,
                village.BlowingSnow.Particles, village.PeripheralBlizzard.Particles })
            {
                var saved = new ParticleSystem.Particle[system.particleCount]; int count = system.GetParticles(saved);
                var main = system.main;
                Vector3 ParticlePoint(Vector3 world) => main.simulationSpace == ParticleSystemSimulationSpace.Local
                    ? system.transform.InverseTransformPoint(world) : main.simulationSpace == ParticleSystemSimulationSpace.Custom && main.customSimulationSpace != null
                    ? main.customSimulationSpace.InverseTransformPoint(world) : world;
                var samples = new[] {
                    new ParticleSystem.Particle { position = ParticlePoint(room.Plan.World(new Vector3(-.5f, 1f, 0f))), startLifetime = 5f, remainingLifetime = 5f, startSize = .03f },
                    new ParticleSystem.Particle { position = ParticlePoint(room.Plan.World(new Vector3(0f, 1f, 5f))), startLifetime = 5f, remainingLifetime = 5f, startSize = .03f }
                };
                system.SetParticles(samples, 2); room.Environment.CullInteriorWeather();
                Assert.That(system.particleCount, Is.EqualTo(1), "Only the flake actually inside the solid room should be removed.");
                var remaining = new ParticleSystem.Particle[1]; system.GetParticles(remaining);
                Assert.That(Vector3.Distance(remaining[0].position, samples[1].position), Is.LessThan(.0001f));
                system.SetParticles(saved, count);
            }
            report.culled_weather_particles = room.Environment.CulledParticles - before;
            Assert.That(report.culled_weather_particles, Is.GreaterThanOrEqualTo(4));
        }

        private static void WorkroomFrame(Camera camera, VillageWorkroomPlan plan, string name, Vector3 localPosition, Vector3 localTarget, float fov)
        {
            Vector3 position = plan.World(localPosition), target = plan.World(localTarget);
            camera.transform.SetPositionAndRotation(position, Quaternion.LookRotation(target - position, Vector3.up));
            camera.fieldOfView = fov;
            CaptureCurrentCamera(camera, "VillageWorkroom", name);
        }
    }
}

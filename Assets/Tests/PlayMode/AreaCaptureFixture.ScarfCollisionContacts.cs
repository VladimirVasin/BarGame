using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

namespace BarPromenade.Tests.PlayMode
{
    // A bounded test input, after the controller's environmental sample and
    // before the real presentation/cloth step. The production solver is intact.
    [DefaultExecutionOrder(310)]
    public sealed class ScarfCollisionWindInput : MonoBehaviour
    {
        public PlayerScarfPresentation Scarf;
        public WindSample Wind;
        private void LateUpdate() { if (Scarf != null) Scarf.SetEnvironment(true, Wind); }
    }

    public sealed partial class AreaCaptureFixture
    {
        private const float ScarfContactTolerance = .001f;
        private static string ScarfCollisionFolder => Path.Combine(Directory.GetCurrentDirectory(), "Captures",
            Environment.GetEnvironmentVariable("BARPROMENADE_SCARF_CAPTURE") == "optimization"
                ? "ScarfCollisionOptimization" : "ScarfCollision");

        [Serializable]
        private sealed class ScarfContactReport
        {
            public int rendered_frames, body_pairs, obstacle_pairs, npc_pairs;
            public int near_triangle_pairs, penetrating_frames, unreadable_meshes;
            public bool unprotected_pose_hits_wall, unprotected_pose_hits_gpu_model;
            public bool unprotected_pose_hits_npc;
            public bool pause_verified;
            public bool mirror_verified;
            public float maximum_tail_motion_metres, maximum_seam_error_metres;
            public float maximum_simulation_step_ms;
            public float maximum_collision_update_ms;
            public double maximum_whole_geometry_ms, maximum_surface_ms;
            public bool native_execution = true;
            public int native_execution_samples;
            public int maximum_collision_triangles;
            public int maximum_last_contact_passes;
            public float minimum_npc_surface_distance_metres = -1f;
            public string phase = "not-started", status = "running";
            public double elapsed_seconds, phase_elapsed_seconds;
            [NonSerialized] public long StartedTicks, PhaseStartedTicks;
            public List<ScarfPhaseTiming> phase_timings = new List<ScarfPhaseTiming>();
            public string failure;
        }

        [Serializable]
        private sealed class ScarfTimingStatistics
        {
            public int sample_count;
            public double mean_ms, p95_ms, max_ms;
            [NonSerialized] private readonly List<double> samples = new List<double>();

            public void Add(double milliseconds)
            {
                samples.Add(milliseconds);
                sample_count++;
                mean_ms += (milliseconds - mean_ms) / sample_count;
                max_ms = Math.Max(max_ms, milliseconds);
            }

            public void RefreshPercentile()
            {
                if (sample_count == 0) return;
                double[] sorted = samples.ToArray();
                Array.Sort(sorted);
                // Nearest-rank p95 includes actual measured frames only.
                p95_ms = sorted[Math.Max(0, (int)Math.Ceiling(sample_count * .95) - 1)];
            }
        }

        [Serializable]
        private sealed class ScarfPhaseTiming
        {
            public string phase;
            public bool first_frame_is_baseline = true;
            public int baseline_rendered_frame;
            public double baseline_simulation_step_ms, baseline_collision_update_ms;
            public ScarfTimingStatistics simulation_step_measured = new ScarfTimingStatistics();
            public ScarfTimingStatistics collision_update_measured = new ScarfTimingStatistics();
            public ScarfTimingStatistics simulation_step_steady = new ScarfTimingStatistics();
            public ScarfTimingStatistics collision_update_steady = new ScarfTimingStatistics();

            public void Add(int renderedFrame, double simulationMs, double collisionMs)
            {
                bool baseline = simulation_step_measured.sample_count == 0;
                simulation_step_measured.Add(simulationMs);
                collision_update_measured.Add(collisionMs);
                if (baseline)
                {
                    baseline_rendered_frame = renderedFrame;
                    baseline_simulation_step_ms = simulationMs;
                    baseline_collision_update_ms = collisionMs;
                    return;
                }
                simulation_step_steady.Add(simulationMs);
                collision_update_steady.Add(collisionMs);
            }

            public void RefreshPercentiles()
            {
                simulation_step_measured.RefreshPercentile();
                collision_update_measured.RefreshPercentile();
                simulation_step_steady.RefreshPercentile();
                collision_update_steady.RefreshPercentile();
            }
        }

        [Serializable]
        private sealed class ScarfContactWitness
        {
            public string phase, cloth_mesh, other_mesh, reason;
            public Vector3 origin;
            public Vector3[] cloth_vertices, other_vertices;
            public int[] cloth_triangles, other_triangles;
            public Vector3[] collision_vertices, collision_normals;
            public bool[] collision_closed, collision_one_sided;
        }

        private sealed class ScarfContactSurface
        {
            public string Name;
            public Renderer Renderer;
            public Vector3[] Vertices;
            public int[] Triangles;
            public Bounds Bounds;
            public Bounds[] TriangleBounds;
            public bool Closed;
        }

        /// <summary>
        /// Independent rendered-surface oracle. It deliberately has no access
        /// to the solver's contacts, collider list or collision resolution.
        /// Only source mesh decoding is shared for unreadable imported assets.
        /// </summary>
        private sealed class ScarfContactProbe : IDisposable
        {
            private readonly Mesh scratch = new Mesh { name = "Scarf Contact Rendered Readback" };
            private readonly Dictionary<Renderer, ScarfContactSurface> surfaces =
                new Dictionary<Renderer, ScarfContactSurface>();
            public Vector3 Origin;
            public ScarfContactWitness Witness;

            public ScarfContactSurface Read(Renderer renderer)
            {
                if (!surfaces.TryGetValue(renderer, out var surface))
                {
                    surface = new ScarfContactSurface { Name = renderer.name, Renderer = renderer };
                    surfaces.Add(renderer, surface);
                }
                Vector3[] vertices;
                int[] triangles;
                if (renderer is SkinnedMeshRenderer skinned)
                {
                    scratch.Clear(false);
                    skinned.BakeMesh(scratch, true);
                    vertices = scratch.vertices;
                    triangles = scratch.triangles;
                }
                else
                {
                    MeshFilter filter = renderer.GetComponent<MeshFilter>();
                    Assert.That(filter != null && filter.sharedMesh != null, Is.True, renderer.name);
                    PlayerScarfCollisionWorld.ReadMesh(filter.sharedMesh, out vertices, out triangles);
                    vertices = (Vector3[])vertices.Clone();
                }
                Assert.That(vertices.Length, Is.GreaterThan(0), renderer.name);
                Matrix4x4 matrix = renderer.transform.localToWorldMatrix;
                matrix.m03 -= Origin.x; matrix.m13 -= Origin.y; matrix.m23 -= Origin.z;
                for (int index = 0; index < vertices.Length; index++)
                {
                    vertices[index] = matrix.MultiplyPoint3x4(vertices[index]);
                    Vector3 point = vertices[index];
                    Assert.That(float.IsNaN(point.x) || float.IsNaN(point.y) || float.IsNaN(point.z) ||
                        float.IsInfinity(point.x) || float.IsInfinity(point.y) || float.IsInfinity(point.z),
                        Is.False, renderer.name + " has nonfinite rendered geometry.");
                }
                surface.Vertices = vertices;
                surface.Triangles = triangles;
                surface.TriangleBounds = new Bounds[triangles.Length / 3];
                surface.Bounds = new Bounds(vertices[0], Vector3.zero);
                foreach (Vector3 vertex in vertices) surface.Bounds.Encapsulate(vertex);
                for (int face = 0; face < triangles.Length; face += 3)
                {
                    Bounds bounds = new Bounds(vertices[triangles[face]], Vector3.zero);
                    bounds.Encapsulate(vertices[triangles[face + 1]]);
                    bounds.Encapsulate(vertices[triangles[face + 2]]);
                    surface.TriangleBounds[face / 3] = bounds;
                }
                surface.Closed = ClosedTopology(surface);
                return surface;
            }

            public ScarfContactSurface Copy(ScarfContactSurface source) => new ScarfContactSurface
            {
                Name = source.Name, Renderer = source.Renderer,
                Vertices = (Vector3[])source.Vertices.Clone(),
                Triangles = (int[])source.Triangles.Clone(), Bounds = source.Bounds,
                TriangleBounds = (Bounds[])source.TriangleBounds.Clone(), Closed = source.Closed
            };

            public float SurfaceDistanceUpperBound(ScarfContactSurface cloth, ScarfContactSurface other)
            {
                float closest = float.PositiveInfinity;
                foreach (Vector3 point in cloth.Vertices)
                    for (int index = 0; index < other.Triangles.Length; index += 3)
                    {
                        // A triangle cannot improve a distance already nearer
                        // than its enclosing box. The exact distance is unchanged.
                        if (other.TriangleBounds[index / 3].SqrDistance(point) >= closest) continue;
                        closest = Mathf.Min(closest, DistanceSquaredToTriangle(point,
                            other.Vertices[other.Triangles[index]], other.Vertices[other.Triangles[index + 1]],
                            other.Vertices[other.Triangles[index + 2]]));
                    }
                return Mathf.Sqrt(closest);
            }

            public bool Intersects(ScarfContactSurface cloth, ScarfContactSurface other,
                string phase, ScarfContactReport report)
            {
                if (!cloth.Bounds.Intersects(other.Bounds)) return false;
                if (other.Closed)
                {
                    foreach (Vector3 point in cloth.Vertices)
                        if (other.Bounds.Contains(point) && Inside(point, other))
                            return Record(cloth, other, phase, "rendered vertex inside closed model");
                }
                for (int first = 0; first < cloth.Triangles.Length; first += 3)
                {
                    if (!cloth.TriangleBounds[first / 3].Intersects(other.Bounds)) continue;
                    Vector3 a = cloth.Vertices[cloth.Triangles[first]];
                    Vector3 b = cloth.Vertices[cloth.Triangles[first + 1]];
                    Vector3 c = cloth.Vertices[cloth.Triangles[first + 2]];
                    if (other.Closed && Inside((a + b + c) / 3f, other))
                        return Record(cloth, other, phase, "rendered triangle centre inside closed model " + first / 3);
                    for (int second = 0; second < other.Triangles.Length; second += 3)
                    {
                        if (!cloth.TriangleBounds[first / 3].Intersects(other.TriangleBounds[second / 3])) continue;
                        report.near_triangle_pairs++;
                        Vector3 x = other.Vertices[other.Triangles[second]];
                        Vector3 y = other.Vertices[other.Triangles[second + 1]];
                        Vector3 z = other.Vertices[other.Triangles[second + 2]];
                        if (Crosses(a, b, x, y, z) || Crosses(b, c, x, y, z) || Crosses(c, a, x, y, z) ||
                            Crosses(x, y, a, b, c) || Crosses(y, z, a, b, c) || Crosses(z, x, a, b, c))
                            return Record(cloth, other, phase, "rendered edges cross triangles " + first / 3 + "/" + second / 3);
                    }
                }
                return false;
            }

            private bool Record(ScarfContactSurface cloth, ScarfContactSurface other, string phase, string reason)
            {
                Witness = new ScarfContactWitness
                {
                    phase = phase, cloth_mesh = cloth.Name, other_mesh = other.Name, reason = reason,
                    origin = Origin, cloth_vertices = cloth.Vertices, cloth_triangles = cloth.Triangles,
                    other_vertices = other.Vertices, other_triangles = other.Triangles
                };
                return true;
            }

            private bool Inside(Vector3 point, ScarfContactSurface surface)
            {
                if (!surface.Bounds.Contains(point)) return false;
                // The full solid angle avoids ray/edge coincidences and very
                // close entry/exit pairs in animated, folded closed meshes.
                // Every face contributes; AABBs prune only the boundary check.
                float toleranceSquared = ScarfContactTolerance * ScarfContactTolerance;
                double solidAngle = 0d, compensation = 0d;
                for (int index = 0; index < surface.Triangles.Length; index += 3)
                {
                    Vector3 a = surface.Vertices[surface.Triangles[index]];
                    Vector3 b = surface.Vertices[surface.Triangles[index + 1]];
                    Vector3 c = surface.Vertices[surface.Triangles[index + 2]];
                    if (surface.TriangleBounds[index / 3].SqrDistance(point) <= toleranceSquared &&
                        DistanceSquaredToTriangle(point, a, b, c) <= toleranceSquared)
                        return false;
                    // Cast before subtraction so all angle arithmetic stays
                    // in double precision, independent of the runtime solver.
                    double ax = (double)a.x - point.x, ay = (double)a.y - point.y, az = (double)a.z - point.z;
                    double bx = (double)b.x - point.x, by = (double)b.y - point.y, bz = (double)b.z - point.z;
                    double cx = (double)c.x - point.x, cy = (double)c.y - point.y, cz = (double)c.z - point.z;
                    double la = Math.Sqrt(ax * ax + ay * ay + az * az);
                    double lb = Math.Sqrt(bx * bx + by * by + bz * bz);
                    double lc = Math.Sqrt(cx * cx + cy * cy + cz * cz);
                    double determinant = ax * (by * cz - bz * cy) +
                        ay * (bz * cx - bx * cz) + az * (bx * cy - by * cx);
                    double denominator = la * lb * lc +
                        (ax * bx + ay * by + az * bz) * lc +
                        (bx * cx + by * cy + bz * cz) * la +
                        (cx * ax + cy * ay + cz * az) * lb;
                    double contribution = 2d * Math.Atan2(determinant, denominator) - compensation;
                    double accumulated = solidAngle + contribution;
                    compensation = (accumulated - solidAngle) - contribution;
                    solidAngle = accumulated;
                }
                return Math.Abs(solidAngle) > 2d * Math.PI;
            }

            private static bool Crosses(Vector3 start, Vector3 end, Vector3 a, Vector3 b, Vector3 c)
            {
                Vector3 direction = end - start;
                float length = direction.magnitude;
                if (length <= ScarfContactTolerance * 2f) return false;
                Vector3 normal = Vector3.Cross(b - a, c - a).normalized;
                float startSide = Vector3.Dot(start - a, normal), endSide = Vector3.Dot(end - a, normal);
                if (Mathf.Min(startSide, endSide) >= -ScarfContactTolerance ||
                    Mathf.Max(startSide, endSide) <= ScarfContactTolerance) return false;
                return RayTriangle(start, direction / length, a, b, c, out float distance) &&
                    distance > ScarfContactTolerance && distance < length - ScarfContactTolerance;
            }

            private static bool RayTriangle(Vector3 start, Vector3 direction, Vector3 a, Vector3 b, Vector3 c,
                out float distance)
            {
                distance = 0f;
                Vector3 first = b - a, second = c - a, p = Vector3.Cross(direction, second);
                float determinant = Vector3.Dot(first, p);
                if (Mathf.Abs(determinant) < 1e-10f) return false;
                Vector3 offset = start - a;
                float u = Vector3.Dot(offset, p) / determinant;
                if (u < -1e-6f || u > 1f + 1e-6f) return false;
                Vector3 q = Vector3.Cross(offset, first);
                float v = Vector3.Dot(direction, q) / determinant;
                if (v < -1e-6f || u + v > 1f + 1e-6f) return false;
                distance = Vector3.Dot(second, q) / determinant;
                return distance >= 0f;
            }

            private static float DistanceSquaredToTriangle(Vector3 p, Vector3 a, Vector3 b, Vector3 c)
            {
                Vector3 ab = b - a, ac = c - a, ap = p - a;
                float d1 = Vector3.Dot(ab, ap), d2 = Vector3.Dot(ac, ap);
                if (d1 <= 0f && d2 <= 0f) return ap.sqrMagnitude;
                Vector3 bp = p - b;
                float d3 = Vector3.Dot(ab, bp), d4 = Vector3.Dot(ac, bp);
                if (d3 >= 0f && d4 <= d3) return bp.sqrMagnitude;
                float vc = d1 * d4 - d3 * d2;
                if (vc <= 0f && d1 >= 0f && d3 <= 0f) return (p - (a + ab * (d1 / (d1 - d3)))).sqrMagnitude;
                Vector3 cp = p - c;
                float d5 = Vector3.Dot(ab, cp), d6 = Vector3.Dot(ac, cp);
                if (d6 >= 0f && d5 <= d6) return cp.sqrMagnitude;
                float vb = d5 * d2 - d1 * d6;
                if (vb <= 0f && d2 >= 0f && d6 <= 0f) return (p - (a + ac * (d2 / (d2 - d6)))).sqrMagnitude;
                float va = d3 * d6 - d5 * d4;
                if (va <= 0f && d4 - d3 >= 0f && d5 - d6 >= 0f)
                    return (p - (b + (c - b) * ((d4 - d3) / (d4 - d3 + d5 - d6)))).sqrMagnitude;
                float sum = va + vb + vc;
                if (Mathf.Abs(sum) < 1e-20f) return Mathf.Min(ap.sqrMagnitude, Mathf.Min(bp.sqrMagnitude, cp.sqrMagnitude));
                return (p - (a + ab * (vb / sum) + ac * (vc / sum))).sqrMagnitude;
            }

            private static bool ClosedTopology(ScarfContactSurface surface)
            {
                var welded = new Dictionary<Vector3Int, int>();
                var mapping = new int[surface.Vertices.Length];
                for (int i = 0; i < mapping.Length; i++)
                {
                    Vector3 point = surface.Vertices[i] * 100000f;
                    var key = new Vector3Int(Mathf.RoundToInt(point.x), Mathf.RoundToInt(point.y), Mathf.RoundToInt(point.z));
                    if (!welded.TryGetValue(key, out int id)) { id = welded.Count; welded.Add(key, id); }
                    mapping[i] = id;
                }
                var counts = new Dictionary<(int, int), int>();
                for (int i = 0; i < surface.Triangles.Length; i += 3)
                {
                    int a = mapping[surface.Triangles[i]], b = mapping[surface.Triangles[i + 1]], c = mapping[surface.Triangles[i + 2]];
                    if (a == b || b == c || c == a) continue;
                    Count(a, b); Count(b, c); Count(c, a);
                }
                foreach (int count in counts.Values) if (count != 2) return false;
                return counts.Count > 0;
                void Count(int a, int b)
                {
                    var key = a < b ? (a, b) : (b, a);
                    counts.TryGetValue(key, out int count); counts[key] = count + 1;
                }
            }

            public void Dispose() => Object.DestroyImmediate(scratch);
        }

        [UnityTest]
        [Timeout(180000)]
        [Explicit("Rendered scarf/body/wall/corner/moving NPC/GPU-only model contact regression. Run alone.")]
        [PrebuildSetup(typeof(ScarfAssetsSetup))]
        public IEnumerator ScarfCollisionContacts()
        {
            var report = new ScarfContactReport();
            using var probe = new ScarfContactProbe();
            float previousDelta = Time.captureDeltaTime;
            AlpineVillageRoot village = null;
            Camera camera = null;
            var target = new RenderTexture(640, 360, 24);
            var ownedMeshes = new List<Mesh>();
            GameObject fixtures = null;
            ScarfCollisionWindInput input = null;
            IDisposable pause = null;
            PlayerScarfController.MouthAccess mouth = null;
            VillageResidentPresentation npc = null;
            Vector3 npcPosition = default;
            Quaternion npcRotation = default;
            bool lifeEnabled = false;
            bool firstScarfFrameLogged = false;
            void FirstScarfFrame(ScriptableRenderContext context, Camera rendered)
            {
                if (firstScarfFrameLogged) return;
                PlayerScarfPresentation found = FindCollisionScarf();
                if (found == null) return;
                firstScarfFrameLogged = true;
                ScarfCollisionProgress(report, report.phase, found, "first-available-scarf-frame");
            }
            try
            {
                ScarfCollisionProgress(report, "session-setup", null, "begin");
                GameSessionState.BeginNewGame();
                GameSessionState.TryStartGameTimeFromWake();
                GameSessionState.TryAddInventoryItem(InventoryItemId.Scarf);
                GameSessionState.TrySetInventoryItemEquipped(InventoryItemId.Scarf, true);
                Time.captureDeltaTime = 1f / 60f;
                RenderPipelineManager.endCameraRendering += FirstScarfFrame;
                ScarfCollisionProgress(report, "load-alpine-village", null, "before-load");
                yield return TraceScarfCollisionLoad(LoadScarfScene<AlpineVillageRoot>(SceneIds.AlpineVillage,
                    value => value.IsInitialized, value => village = value), report);
                ScarfCollisionProgress(report, "prepare-production-hero", FindCollisionScarf(), "scene-ready");
                lifeEnabled = village.Life.enabled;
                village.Life.enabled = false;
                village.CameraFollow.enabled = false;
                village.Player.Motor.enabled = false;
                AlpineVillageLaneSample lane = village.Plan.Lane.Sample(10f);
                village.Player.Motor.Teleport(lane.Position + Vector3.up * PlayerFactory.GroundedRootOffset);
                village.Player.GameObject.transform.rotation = Quaternion.LookRotation(lane.Forward);
                var hero = (Player3DCharacterPresentation)village.Player.Visual;
                hero.SetMotion(PlayerMotionSample.Stationary);
                var controller = village.Player.GameObject.GetComponent<PlayerScarfController>();
                Assert.That(controller, Is.Not.Null);
                PlayerScarfPresentation scarf = controller.Presentation;
                probe.Origin = village.Player.GameObject.transform.position;
                camera = Camera.main;
                Assert.That(camera, Is.Not.Null);
                target.Create(); camera.targetTexture = target;
                AimScarfHero(camera, village.Player, new Vector3(.95f, 1.50f, -1.45f));
                input = village.gameObject.AddComponent<ScarfCollisionWindInput>();
                input.Scarf = scarf;
                Vector3 back = Vector3.ProjectOnPlane(hero.Registry.Anchors.Head.position -
                    scarf.GetFrontGripPosition(0f), Vector3.up).normalized;
                Vector3 side = Vector3.Cross(Vector3.up, back).normalized;
                input.Wind = new WindSample(Mathf.Atan2(back.x, back.z) * Mathf.Rad2Deg, 1f);
                var body = new List<Renderer>();
                foreach (Player3DMeshBinding binding in hero.Registry.MeshBindings)
                    if (binding?.Renderer != null) body.Add(binding.Renderer);
                var obstacles = new List<Renderer>();
                var people = new List<Renderer>();
                Quaternion facing = village.Player.GameObject.transform.rotation;
                Vector3[] previousTail = null;
                ScarfCollisionProgress(report, "body-turns", scarf, "begin");
                for (int frame = 0; frame < 90; frame++)
                {
                    float yaw = frame < 30 ? 0f : 65f * Mathf.Sin((frame - 30) / 60f * Mathf.PI * 2f);
                    village.Player.GameObject.transform.rotation = Quaternion.AngleAxis(yaw, Vector3.up) * facing;
                    if (frame == 30) mouth = PlayerScarfController.RequireMouthAccess(village.Player, village);
                    yield return ScarfRenderedFrame(camera, () =>
                    {
                        AssertScarfContacts(probe, scarf, body, obstacles, people, report, "body-turns", camera);
                        Vector3[] current = probe.Read(scarf.TailRenderer).Vertices;
                        if (previousTail != null)
                            for (int i = 0; i < current.Length; i++)
                                report.maximum_tail_motion_metres = Mathf.Max(report.maximum_tail_motion_metres,
                                    Vector3.Distance(current[i], previousTail[i]));
                        previousTail = (Vector3[])current.Clone();
                        if (frame == 20 || frame == 70) SaveScarfCollisionFrame(camera, "body-" + frame);
                    });
                }
                mouth?.Dispose(); mouth = null;
                village.Player.GameObject.transform.rotation = facing;
                ScarfCollisionProgress(report, "restore-mouth-and-settle", scarf, "begin");
                yield return ScarfCollisionSettle(70, report, scarf);
                Assert.That(report.maximum_tail_motion_metres, Is.GreaterThan(.001f));

                AimScarfHero(camera, village.Player, new Vector3(-.85f, 1.58f, 1.25f));
                yield return ScarfRenderedFrame(camera, () => SaveScarfCollisionFrame(camera, "front"));
                AimScarfHero(camera, village.Player, new Vector3(.95f, 1.50f, -1.45f));

                ScarfContactSurface unprotected = null;
                ScarfCollisionProgress(report, "gate-wall-setup", scarf, "begin");
                yield return ScarfRenderedFrame(camera, () => unprotected = probe.Copy(probe.Read(scarf.TailRenderer)));
                fixtures = new GameObject("Scarf collision authored test models");
                fixtures.transform.SetParent(village.transform, false);
                GameObject wall = VillageLifePropLibrary.Create(VillageLifePropKind.GateLeaf, fixtures.transform, "Scarf test gate wall");
                PlaceScarfBarrier(wall.transform, VillageLifePropKind.GateLeaf, unprotected, probe.Origin, back);
                obstacles.AddRange(wall.GetComponentsInChildren<MeshRenderer>());
                foreach (Renderer renderer in obstacles)
                    if (probe.Intersects(unprotected, probe.Read(renderer), "unprotected-wall-witness", report))
                    { report.unprotected_pose_hits_wall = true; break; }
                Assert.That(report.unprotected_pose_hits_wall, Is.True,
                    "The recorded free-wind cloth pose must cross the authored obstacle; collider registration alone proves nothing.");
                probe.Witness = null;
                ScarfCollisionProgress(report, "gate-wall", scarf, "begin");
                for (int frame = 0; frame < 60; frame++)
                    yield return ScarfRenderedFrame(camera, () =>
                    {
                        AssertScarfContacts(probe, scarf, body, obstacles, people, report, "gate-wall", camera);
                        if (frame == 10 || frame == 59) SaveScarfCollisionFrame(camera, "wall-" + frame);
                    });

                GameObject corner = VillageLifePropLibrary.Create(VillageLifePropKind.GateLeaf, fixtures.transform, "Scarf test gate corner");
                // The return wall starts at its hinge edge and extends away
                // behind the hero; it must not cut through the hero's torso.
                PlaceScarfBarrier(corner.transform, VillageLifePropKind.GateLeaf, unprotected, probe.Origin, -side, true);
                obstacles.AddRange(corner.GetComponentsInChildren<MeshRenderer>());
                ScarfCollisionProgress(report, "gate-corner-turn", scarf, "begin");
                for (int frame = 0; frame < 60; frame++)
                {
                    village.Player.GameObject.transform.rotation = Quaternion.AngleAxis(18f * Mathf.Sin(frame / 60f * Mathf.PI * 2f), Vector3.up) * facing;
                    yield return ScarfRenderedFrame(camera, () =>
                    {
                        AssertScarfContacts(probe, scarf, body, obstacles, people, report, "gate-corner-turn", camera);
                        if (frame == 15 || frame == 59) SaveScarfCollisionFrame(camera, "corner-" + frame);
                    });
                }
                wall.SetActive(false); corner.SetActive(false); obstacles.Clear();
                village.Player.GameObject.transform.rotation = facing;
                ScarfCollisionProgress(report, "moving-npc-setup", scarf, "begin");
                yield return ScarfCollisionSettle(20, report, scarf);
                ScarfContactSurface beforeNpc = null;
                yield return ScarfRenderedFrame(camera, () => beforeNpc = probe.Copy(probe.Read(scarf.TailRenderer)));

                npc = village.Life.Neighbours[0].Actor;
                npcPosition = npc.transform.position; npcRotation = npc.transform.rotation;
                people.AddRange(npc.GetComponentsInChildren<SkinnedMeshRenderer>(true));
                npc.transform.SetPositionAndRotation(probe.Origin, Quaternion.LookRotation(-back));
                npc.Apply(VillageResidentAction.Idle, 0f);
                float heroRear = ScarfProjectionExtreme(probe, body, back, true);
                float npcFront = ScarfProjectionExtreme(probe, people, back, false);
                float closestRootGap = Mathf.Max(.20f, heroRear - npcFront + .01f);
                npc.transform.position = probe.Origin + back * (closestRootGap + .12f);
                ScarfCollisionProgress(report, "moving-npc", scarf, "begin");
                // Approach gradually; the first placement is outside the tail.
                for (int frame = 0; frame < 90; frame++)
                {
                    float phase = frame / 89f;
                    npc.ApplyLocomotion(.75f, false, frame / 60f);
                    // Keep a separating plane between the actual body meshes.
                    // Broad gameplay capsule radii would prevent exercising
                    // cloth contact with a close animated shoulder or sleeve.
                    heroRear = ScarfProjectionExtreme(probe, body, back, true);
                    npcFront = ScarfProjectionExtreme(probe, people, back, false) -
                        Vector3.Dot(npc.transform.position - probe.Origin, back);
                    closestRootGap = Mathf.Max(.20f, heroRear - npcFront + .01f);
                    npc.transform.position = probe.Origin + back * (closestRootGap + .12f * (1f - Mathf.Sin(phase * Mathf.PI))) +
                        side * Mathf.Lerp(-.35f, .35f, phase);
                    yield return ScarfRenderedFrame(camera, () =>
                    {
                        AssertScarfContacts(probe, scarf, body, obstacles, people, report, "moving-npc", camera);
                        var currentTail = probe.Read(scarf.TailRenderer);
                        foreach (Renderer person in people)
                        {
                            if (!ContactRendererVisible(person)) continue;
                            ScarfContactSurface personSurface = probe.Read(person);
                            float proximity = probe.SurfaceDistanceUpperBound(currentTail, personSurface);
                            report.minimum_npc_surface_distance_metres = report.minimum_npc_surface_distance_metres < 0f
                                ? proximity : Mathf.Min(report.minimum_npc_surface_distance_metres, proximity);
                            if (!report.unprotected_pose_hits_npc &&
                                probe.Intersects(beforeNpc, personSurface, "unprotected-npc-witness", report))
                                report.unprotected_pose_hits_npc = true;
                        }
                        probe.Witness = null;
                        if (frame == 30 || frame == 60) SaveScarfCollisionFrame(camera, "npc-" + frame);
                    });
                }
                Assert.That(report.unprotected_pose_hits_npc ||
                    report.minimum_npc_surface_distance_metres >= 0f && report.minimum_npc_surface_distance_metres <= .02f,
                    Is.True, "The moving NPC must obstruct the recorded free cloth or come within 2 cm of its actual rendered surface; AABB overlap alone is insufficient.");
                npc.transform.SetPositionAndRotation(npcPosition, npcRotation);
                npc.Apply(VillageResidentAction.Idle, 0f); people.Clear();
                ScarfCollisionProgress(report, "gpu-only-model-setup", scarf, "begin");
                yield return ScarfCollisionSettle(20, report, scarf);

                yield return ScarfRenderedFrame(camera, () => unprotected = probe.Copy(probe.Read(scarf.TailRenderer)));
                GameObject gpu = VillageLifePropLibrary.Create(VillageLifePropKind.StationLid, fixtures.transform, "Scarf test GPU-only lid");
                // This remains the real authored lid geometry: only CPU access
                // is removed. No synthetic box or collider stands in for it.
                foreach (MeshFilter filter in gpu.GetComponentsInChildren<MeshFilter>())
                {
                    Mesh clone = Object.Instantiate(filter.sharedMesh);
                    clone.name = "Scarf GPU-only " + filter.sharedMesh.name;
                    filter.sharedMesh = clone; ownedMeshes.Add(clone);
                    clone.UploadMeshData(true);
                    Assert.That(clone.isReadable, Is.False);
                    report.unreadable_meshes++;
                }
                Assert.That(gpu.GetComponentsInChildren<Collider>(), Is.Empty);
                PlaceScarfLid(gpu.transform, unprotected, probe.Origin, back);
                obstacles.AddRange(gpu.GetComponentsInChildren<MeshRenderer>());
                foreach (Renderer renderer in obstacles)
                    if (probe.Intersects(unprotected, probe.Read(renderer), "unprotected-gpu-witness", report))
                    { report.unprotected_pose_hits_gpu_model = true; break; }
                Assert.That(report.unprotected_pose_hits_gpu_model, Is.True,
                    "A rendered triangle of the GPU-only lid must obstruct the previously free cloth.");
                probe.Witness = null;
                ScarfCollisionProgress(report, "gpu-only-no-collider", scarf, "begin");
                for (int frame = 0; frame < 60; frame++)
                    yield return ScarfRenderedFrame(camera, () =>
                    {
                        AssertScarfContacts(probe, scarf, body, obstacles, people, report, "gpu-only-no-collider", camera);
                        if (frame == 10 || frame == 59) SaveScarfCollisionFrame(camera, "gpu-" + frame);
                    });

                ScarfCollisionProgress(report, "pause", scarf, "begin");
                pause = GameTimeScaleRuntime.AcquirePause();
                Vector3[] paused = null;
                yield return ScarfRenderedFrame(camera, () => paused = (Vector3[])probe.Read(scarf.TailRenderer).Vertices.Clone());
                yield return ScarfFrames(4);
                yield return ScarfRenderedFrame(camera, () =>
                {
                    Vector3[] after = probe.Read(scarf.TailRenderer).Vertices;
                    for (int i = 0; i < paused.Length; i++) Assert.That(Vector3.Distance(paused[i], after[i]), Is.LessThan(.001f));
                    report.pause_verified = true;
                });
                pause.Dispose(); pause = null;
                Assert.That(report.body_pairs, Is.GreaterThan(0));
                Assert.That(report.obstacle_pairs, Is.GreaterThan(0));
                Assert.That(report.npc_pairs, Is.GreaterThan(0));
                Assert.That(report.unreadable_meshes, Is.GreaterThan(0));
                Assert.That(report.penetrating_frames, Is.Zero);
                Assert.That(report.maximum_seam_error_metres, Is.LessThan(.015f));
                Assert.That(report.native_execution_samples, Is.GreaterThan(0));
                Assert.That(report.native_execution, Is.True, "Rendered contact frames must use the native solver.");
                ScarfCollisionProgress(report, "mirror-copy", scarf, "begin");
                HomeInteriorRoot home = null;
                yield return TraceScarfCollisionLoad(LoadScarfScene<HomeInteriorRoot>(SceneIds.HomeInterior,
                    value => value.IsInitialized, value => home = value), report);
                home.Player.Motor.Teleport(new Vector3(2.075f, .12f, 2.78f));
                camera = Camera.main; camera.targetTexture = target;
                yield return ScarfFrames(12);
                yield return ScarfRenderedFrame(camera, () =>
                {
                    Assert.That(home.BathroomMirror.IsActive, Is.True);
                    var source = home.Player.GameObject.GetComponent<PlayerScarfController>().Presentation;
                    var twin = home.BathroomMirror.Twin.GetComponent<PlayerScarfPresentation>();
                    Assert.That(twin.IsVisible, Is.True);
                    Assert.That(twin.TailSimulation, Is.Null);
                    Assert.That(twin.Renderers.Count, Is.EqualTo(source.Renderers.Count));
                    for (int part = 0; part < source.Renderers.Count; part++)
                    {
                        var first = (SkinnedMeshRenderer)source.Renderers[part];
                        var second = (SkinnedMeshRenderer)twin.Renderers[part];
                        Vector3[] a = first.sharedMesh.vertices, b = second.sharedMesh.vertices;
                        Assert.That(a.Length, Is.EqualTo(b.Length));
                        Matrix4x4 sourceToActor = source.Registry.transform.worldToLocalMatrix * first.localToWorldMatrix;
                        Matrix4x4 twinToActor = twin.Registry.transform.worldToLocalMatrix * second.localToWorldMatrix;
                        for (int i = 0; i < a.Length; i++)
                            Assert.That(Vector3.Distance(sourceToActor.MultiplyPoint3x4(a[i]),
                                twinToActor.MultiplyPoint3x4(b[i])), Is.LessThan(.001f), first.name);
                    }
                    report.mirror_verified = true;
                    SaveScarfCollisionFrame(camera, "mirror");
                });
                report.status = "passed";
                ScarfCollisionProgress(report, "complete", FindCollisionScarf(), "passed");
            }
            finally
            {
                RenderPipelineManager.endCameraRendering -= FirstScarfFrame;
                if (report.status == "running") report.status = "failed-or-interrupted";
                ScarfCollisionProgress(report, report.phase, FindCollisionScarf(), "cleanup");
                mouth?.Dispose(); pause?.Dispose();
                string folder = ScarfCollisionFolder;
                Directory.CreateDirectory(folder);
                File.WriteAllText(Path.Combine(folder, "collision-report.json"), JsonUtility.ToJson(report, true));
                if (probe.Witness != null)
                    File.WriteAllText(Path.Combine(folder, "collision-witness.json"), JsonUtility.ToJson(probe.Witness, true));
                if (input != null) Object.DestroyImmediate(input);
                if (npc != null) { npc.transform.SetPositionAndRotation(npcPosition, npcRotation); npc.Apply(VillageResidentAction.Idle, 0f); }
                if (fixtures != null) Object.DestroyImmediate(fixtures);
                foreach (Mesh mesh in ownedMeshes) Object.DestroyImmediate(mesh);
                if (camera != null) camera.targetTexture = null;
                target.Release(); Object.DestroyImmediate(target);
                if (village != null)
                {
                    village.Life.enabled = lifeEnabled;
                    village.Player.Motor.enabled = true;
                    village.CameraFollow.enabled = true;
                }
                Time.captureDeltaTime = previousDelta;
                GameSessionState.BeginNewGame();
            }
        }

        private static PlayerScarfPresentation FindCollisionScarf()
        {
            PlayerScarfController controller = Object.FindAnyObjectByType<PlayerScarfController>();
            return controller != null ? controller.Presentation : null;
        }

        private static IEnumerator TraceScarfCollisionLoad(IEnumerator load, ScarfContactReport report)
        {
            int frames = 0;
            try
            {
                while (true)
                {
                    bool next;
                    try { next = load.MoveNext(); }
                    catch (Exception exception)
                    {
                        // Unity can abort a nested iterator without resuming the
                        // outer test. Persist this failure at its actual source.
                        report.status = "failed";
                        report.failure = exception.ToString();
                        ScarfCollisionProgress(report, report.phase, FindCollisionScarf(), "load-failure");
                        throw;
                    }
                    if (!next) break;
                    if (++frames % 30 == 0)
                        ScarfCollisionProgress(report, report.phase, FindCollisionScarf(), "load-frame-" + frames);
                    yield return load.Current;
                }
            }
            finally { (load as IDisposable)?.Dispose(); }
        }

        private static IEnumerator ScarfCollisionSettle(int count, ScarfContactReport report,
            PlayerScarfPresentation scarf)
        {
            for (int frame = 1; frame <= count; frame++)
            {
                yield return null;
                if (frame % 30 == 0 || frame == count)
                    ScarfCollisionProgress(report, report.phase, scarf, "settle-" + frame + "/" + count);
            }
        }

        private static void ScarfCollisionProgress(ScarfContactReport report, string phase,
            PlayerScarfPresentation scarf, string detail)
        {
            long now = System.Diagnostics.Stopwatch.GetTimestamp();
            if (report.StartedTicks == 0)
                report.StartedTicks = report.PhaseStartedTicks = now;
            if (report.phase != phase)
            {
                report.phase = phase;
                report.PhaseStartedTicks = now;
            }
            report.elapsed_seconds = (now - report.StartedTicks) / (double)System.Diagnostics.Stopwatch.Frequency;
            report.phase_elapsed_seconds = (now - report.PhaseStartedTicks) / (double)System.Diagnostics.Stopwatch.Frequency;
            float simulationMs = scarf?.TailSimulation != null ? scarf.TailSimulation.LastStepMilliseconds : 0f;
            double worldMs = scarf?.CollisionWorld != null ? scarf.CollisionWorld.LastUpdateMilliseconds : 0d;
            int triangles = scarf?.CollisionWorld != null ? scarf.CollisionWorld.CollectedTriangleCount : 0;
            int candidates = scarf?.CollisionWorld != null ? scarf.CollisionWorld.CandidateRendererCount : 0;
            int scanned = scarf?.CollisionWorld != null ? scarf.CollisionWorld.ScannedRendererCount : 0;
            int contactPasses = scarf?.TailSimulation != null ? scarf.TailSimulation.LastContactPassCount : 0;
            report.maximum_simulation_step_ms = Mathf.Max(report.maximum_simulation_step_ms, simulationMs);
            report.maximum_collision_update_ms = Mathf.Max(report.maximum_collision_update_ms, (float)worldMs);
            report.maximum_whole_geometry_ms = Math.Max(report.maximum_whole_geometry_ms,
                scarf != null ? scarf.LastGeometryMilliseconds : 0d);
            report.maximum_surface_ms = Math.Max(report.maximum_surface_ms,
                scarf != null ? scarf.LastSurfaceMilliseconds : 0d);
            report.maximum_collision_triangles = Mathf.Max(report.maximum_collision_triangles, triangles);
            foreach (ScarfPhaseTiming timing in report.phase_timings) timing.RefreshPercentiles();
            Debug.Log($"SCARF_CONTACT_PROGRESS phase={phase} detail={detail} frames={report.rendered_frames} " +
                $"elapsed={report.elapsed_seconds:F3}s phase_elapsed={report.phase_elapsed_seconds:F3}s " +
                $"triangles={triangles} candidates={candidates} scanned={scanned} sim_ms={simulationMs:F3} world_ms={worldMs:F3} " +
                $"contact_passes={contactPasses} " +
                $"npc_distance_m={report.minimum_npc_surface_distance_metres:F4} npc_witness={report.unprotected_pose_hits_npc}");
            string folder = ScarfCollisionFolder;
            Directory.CreateDirectory(folder);
            File.WriteAllText(Path.Combine(folder, "collision-report.json"), JsonUtility.ToJson(report, true));
        }

        private static void AssertScarfContacts(ScarfContactProbe probe, PlayerScarfPresentation scarf,
            List<Renderer> body, List<Renderer> obstacles, List<Renderer> people,
            ScarfContactReport report, string phase, Camera camera)
        {
            report.rendered_frames++;
            report.maximum_whole_geometry_ms = Math.Max(report.maximum_whole_geometry_ms, scarf.LastGeometryMilliseconds);
            report.maximum_surface_ms = Math.Max(report.maximum_surface_ms, scarf.LastSurfaceMilliseconds);
            if (scarf.LastGeometryMilliseconds > 0d)
            {
                report.native_execution_samples++;
                report.native_execution &= PlayerScarfContactSolver.LastExecutionWasNative;
            }
            report.maximum_last_contact_passes = Math.Max(report.maximum_last_contact_passes,
                scarf.TailSimulation.LastContactPassCount);
            ScarfPhaseTiming timing = report.phase_timings.Find(value => value.phase == phase);
            if (timing == null)
            {
                timing = new ScarfPhaseTiming { phase = phase };
                report.phase_timings.Add(timing);
            }
            // Only these final rendered collision samples enter phase metrics.
            // Loading, setup, settling and pause logs never inflate the sample
            // count. Keep each phase's first reset separately from steady work.
            timing.Add(report.rendered_frames, scarf.TailSimulation.LastStepMilliseconds,
                scarf.CollisionWorld != null ? scarf.CollisionWorld.LastUpdateMilliseconds : 0d);
            report.maximum_simulation_step_ms = Mathf.Max(report.maximum_simulation_step_ms,
                scarf.TailSimulation.LastStepMilliseconds);
            if (scarf.CollisionWorld != null)
            {
                report.maximum_collision_update_ms = Mathf.Max(report.maximum_collision_update_ms,
                    (float)scarf.CollisionWorld.LastUpdateMilliseconds);
                report.maximum_collision_triangles = Mathf.Max(report.maximum_collision_triangles,
                    scarf.CollisionWorld.Triangles.Count);
            }
            if (report.rendered_frames == 1 || report.rendered_frames % 30 == 0)
                ScarfCollisionProgress(report, phase, scarf, "rendered-frame");
            var targets = new List<(ScarfContactSurface Surface, int Kind)>();
            Add(body, 0); Add(obstacles, 1); Add(people, 2);
            foreach (Renderer renderer in scarf.Renderers)
            {
                if (!ContactRendererVisible(renderer)) continue;
                ScarfContactSurface cloth = probe.Read(renderer);
                if (renderer == scarf.TailRenderer)
                {
                    Vector3[] rest = scarf.TailRestVertices;
                    for (int index = 0; index < rest.Length; index++)
                        if ((1.567f - rest[index].y) / PlayerScarfPresentation.TailLength < .035f)
                            report.maximum_seam_error_metres = Mathf.Max(report.maximum_seam_error_metres,
                                Vector3.Distance(renderer.transform.TransformPoint(rest[index]) - probe.Origin,
                                    cloth.Vertices[index]));
                }
                foreach (var other in targets)
                {
                    Bounds near = cloth.Bounds; near.Expand(.04f);
                    if (!near.Intersects(other.Surface.Bounds)) continue;
                    if (other.Kind == 0) report.body_pairs++;
                    else if (other.Kind == 1) report.obstacle_pairs++;
                    else report.npc_pairs++;
                    if (!probe.Intersects(cloth, other.Surface, phase, report)) continue;
                    report.penetrating_frames++;
                    report.failure = phase + ": " + probe.Witness.cloth_mesh + " / " +
                        probe.Witness.other_mesh + ": " + probe.Witness.reason;
                    var contactVertices = new List<Vector3>();
                    var contactNormals = new List<Vector3>();
                    var contactClosed = new List<bool>();
                    var contactSides = new List<bool>();
                    foreach (var face in scarf.CollisionWorld.Triangles)
                        if (face.Owner == other.Surface.Renderer)
                        {
                            contactVertices.Add(face.A - probe.Origin);
                            contactVertices.Add(face.B - probe.Origin);
                            contactVertices.Add(face.C - probe.Origin);
                            contactNormals.Add(face.Normal);
                            contactClosed.Add(face.ClosedSurface);
                            contactSides.Add(face.OneSided);
                        }
                    probe.Witness.collision_vertices = contactVertices.ToArray();
                    probe.Witness.collision_normals = contactNormals.ToArray();
                    probe.Witness.collision_closed = contactClosed.ToArray();
                    probe.Witness.collision_one_sided = contactSides.ToArray();
                    File.WriteAllText(Path.Combine(ScarfCollisionFolder, "collision-witness.json"), JsonUtility.ToJson(probe.Witness, true));
                    report.status = "failed";
                    ScarfCollisionProgress(report, phase, scarf, "contact-failure");
                    string folder = ScarfCollisionFolder;
                    File.WriteAllText(Path.Combine(folder, "collision-witness.json"), JsonUtility.ToJson(probe.Witness, true));
                    SaveScarfCollisionFrame(camera, "FAIL-" + phase);
                    Assert.Fail(report.failure);
                }
            }
            void Add(List<Renderer> values, int kind)
            {
                foreach (Renderer renderer in values)
                    if (ContactRendererVisible(renderer)) targets.Add((probe.Read(renderer), kind));
            }
        }

        private static bool ContactRendererVisible(Renderer renderer) => renderer != null &&
            renderer.enabled && renderer.gameObject.activeInHierarchy &&
            renderer.shadowCastingMode != UnityEngine.Rendering.ShadowCastingMode.ShadowsOnly;

        private static float ScarfProjectionExtreme(ScarfContactProbe probe, List<Renderer> renderers,
            Vector3 axis, bool maximum)
        {
            float result = maximum ? float.NegativeInfinity : float.PositiveInfinity;
            foreach (Renderer renderer in renderers)
                if (ContactRendererVisible(renderer))
                    foreach (Vector3 point in probe.Read(renderer).Vertices)
                        result = maximum ? Mathf.Max(result, Vector3.Dot(point, axis)) :
                            Mathf.Min(result, Vector3.Dot(point, axis));
            Assert.That(float.IsInfinity(result), Is.False, "Body separation must measure real visible geometry.");
            return result;
        }

        private static void PlaceScarfBarrier(Transform target, VillageLifePropKind kind,
            ScarfContactSurface freeTail, Vector3 origin, Vector3 normal, bool hingeEdge = false)
        {
            VillageLifePropDefinition definition = VillageLifePropLibrary.GetDefinition(kind);
            Vector3 min = VillageLifePropLibrary.Vector(definition.bounds_min);
            Vector3 max = VillageLifePropLibrary.Vector(definition.bounds_max);
            Quaternion rotation = Quaternion.LookRotation(normal);
            Vector3 localContact = new Vector3(hingeEdge ? min.x + .035f : (min.x + max.x) * .5f,
                (min.y + max.y) * .5f, min.z + .012f);
            // The barrier intersects a recorded real free-wind face, so the
            // oracle has a concrete failing witness for missing world contact.
            Vector3 point = origin + ScarfFreeTailPoint(freeTail);
            target.SetPositionAndRotation(point - rotation * localContact, rotation);
        }

        private static void PlaceScarfLid(Transform target, ScarfContactSurface freeTail, Vector3 origin, Vector3 back)
        {
            VillageLifePropDefinition definition = VillageLifePropLibrary.GetDefinition(VillageLifePropKind.StationLid);
            Vector3 min = VillageLifePropLibrary.Vector(definition.bounds_min);
            Vector3 max = VillageLifePropLibrary.Vector(definition.bounds_max);
            Vector3 localContact = new Vector3((min.x + max.x) * .5f, (min.y + max.y) * .5f, min.z + .035f);
            Quaternion rotation = Quaternion.LookRotation(back);
            target.SetPositionAndRotation(origin + ScarfFreeTailPoint(freeTail) - rotation * localContact, rotation);
        }

        private static Vector3 ScarfFreeTailPoint(ScarfContactSurface tail)
        {
            Vector3 point = tail.Vertices[0];
            foreach (Vector3 vertex in tail.Vertices) if (vertex.y < point.y) point = vertex;
            // A triangle-centre target avoids placing only an isolated edge
            // tangency into the obstacle during the negative control.
            int best = 0; float distance = float.PositiveInfinity;
            for (int index = 0; index < tail.Triangles.Length; index += 3)
            {
                Vector3 centre = (tail.Vertices[tail.Triangles[index]] + tail.Vertices[tail.Triangles[index + 1]] +
                    tail.Vertices[tail.Triangles[index + 2]]) / 3f;
                float d = (centre - point).sqrMagnitude;
                if (d < distance) { distance = d; best = index; }
            }
            return (tail.Vertices[tail.Triangles[best]] + tail.Vertices[tail.Triangles[best + 1]] +
                tail.Vertices[tail.Triangles[best + 2]]) / 3f;
        }

        private static void SaveScarfCollisionFrame(Camera camera, string name)
        {
            var pixels = new Texture2D(camera.targetTexture.width, camera.targetTexture.height, TextureFormat.RGB24, false);
            RenderTexture previous = RenderTexture.active;
            try
            {
                RenderTexture.active = camera.targetTexture;
                pixels.ReadPixels(new Rect(0f, 0f, pixels.width, pixels.height), 0, 0); pixels.Apply();
                Assert.That(IsBlank(pixels), Is.False);
                string folder = ScarfCollisionFolder;
                Directory.CreateDirectory(folder);
                File.WriteAllBytes(Path.Combine(folder, name + ".png"), pixels.EncodeToPNG());
            }
            finally { RenderTexture.active = previous; Object.DestroyImmediate(pixels); }
        }
    }
}

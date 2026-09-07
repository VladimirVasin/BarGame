using System;
using System.Collections.Generic;
using System.IO;
using NUnit.Framework;
using UnityEngine;
using Object = UnityEngine.Object;

namespace BarPromenade.Tests.PlayMode
{
    /// <summary>
    /// Measures the rendered arm volumes after Unity's actual cold/locomotion
    /// mixing. SAT is used only after checking closed topology and convexity;
    /// no collider radius or wrist-distance approximation decides penetration.
    /// </summary>
    internal sealed class PlayerColdArmSeparationProbe : IDisposable
    {
        internal const float PenetrationTolerance = 0.002f;
        private const float PlaneTolerance = 0.00002f;
        private const float AxisLengthSquared = 0.0000000001f;

        private sealed class Surface
        {
            internal string Name;
            internal SkinnedMeshRenderer Renderer;
            internal int[] SourceVertices;
            internal int[] Triangles;
            internal Vector3[] Vertices;
            internal readonly List<Vector3> Normals = new List<Vector3>(64);
            internal readonly List<Vector3> Edges = new List<Vector3>(64);
            internal Bounds Bounds;
        }

        [Serializable]
        private sealed class PairReport
        {
            public string left_mesh;
            public string right_mesh;
            public float worst_signed_clearance_m = float.PositiveInfinity;
            public int penetrating_samples;
            public int sample;
            public double cold_time_seconds;
            public float cold_arm_weight;
            public float rub_normalized_time;
            public string locomotion_state;
            public Vector3 pair_center_world;
        }

        [Serializable]
        private sealed class ProbeReport
        {
            public string method = "Closed, convex, final-pose baked meshes; separating face and edge-cross axes";
            public float tolerance_m = PenetrationTolerance;
            public int sample_count;
            public int penetrating_pair_samples;
            public string geometry_diagnostic;
            public PairReport[] pairs;
        }

        [Serializable]
        private sealed class SurfaceDiagnostic
        {
            public string mesh;
            public string reason;
            public Vector3 lossy_scale;
            public Vector3 actor_relative_bounds_size;
            public Vector3[] actor_relative_vertices;
            public int[] triangles;
        }

        private readonly List<Surface> left = new List<Surface>(6);
        private readonly List<Surface> right = new List<Surface>(6);
        private readonly List<Vector3> readback = new List<Vector3>(256);
        private readonly Mesh baked = new Mesh { name = "Cold Arm Separation Readback" };
        private int samples;
        private int penetratingPairs;
        private float worstClearance = float.PositiveInfinity;
        private string worstWitness = string.Empty;
        private PairReport[] pairReports;
        private string geometryDiagnostic = string.Empty;

        internal PlayerColdArmSeparationProbe(Player3DAssetRegistry registry)
        {
            foreach (Player3DMeshBinding binding in registry.MeshBindings)
            {
                bool isLeft = binding.BodyGroup == "LeftUpperArm" ||
                              binding.BodyGroup == "LeftLowerArm";
                bool isRight = binding.BodyGroup == "RightUpperArm" ||
                               binding.BodyGroup == "RightLowerArm";
                if (!isLeft && !isRight) continue;
                Assert.That(binding.Renderer, Is.TypeOf<SkinnedMeshRenderer>(), binding.MeshName);
                var renderer = (SkinnedMeshRenderer)binding.Renderer;
                Surface surface = BuildSurface(binding.MeshName, renderer);
                (isLeft ? left : right).Add(surface);
            }
            Assert.That(left.Count, Is.EqualTo(6), "Measure all left arm skin, sleeve, thumb and bandage meshes.");
            Assert.That(right.Count, Is.EqualTo(6), "Measure all right arm skin, sleeve and thumb meshes.");
            pairReports = new PairReport[left.Count * right.Count];
            for (int a = 0; a < left.Count; a++)
                for (int b = 0; b < right.Count; b++)
                    pairReports[a * right.Count + b] = new PairReport
                    {
                        left_mesh = left[a].Name, right_mesh = right[b].Name
                    };
        }

        internal void Sample(Player3DCharacterPresentation hero)
        {
            hero.ReapplyLatePresentationPose();
            // Actor-relative world metres avoid losing the 2 mm tolerance to
            // cancellation against the village's absolute world coordinates.
            Vector3 origin = hero.VisualRoot.position;
            foreach (Surface surface in left) Refresh(surface, origin);
            foreach (Surface surface in right) Refresh(surface, origin);
            samples++;
            int pairIndex = 0;
            foreach (Surface a in left)
            {
                foreach (Surface b in right)
                {
                    float clearance = SignedClearance(a, b);
                    PairReport pair = pairReports[pairIndex++];
                    if (clearance < pair.worst_signed_clearance_m)
                    {
                        pair.worst_signed_clearance_m = clearance;
                        pair.sample = samples;
                        pair.cold_time_seconds = hero.ColdModel.ElapsedSeconds;
                        pair.cold_arm_weight = hero.ColdArmWeight;
                        pair.rub_normalized_time = hero.ColdModel.RubNormalizedTime;
                        pair.locomotion_state = hero.CurrentLocomotionState.ToString();
                        pair.pair_center_world = origin + (a.Bounds.center + b.Bounds.center) * 0.5f;
                    }
                    if (clearance < worstClearance)
                    {
                        worstClearance = clearance;
                        Vector3 witness = origin + (a.Bounds.center + b.Bounds.center) * 0.5f;
                        worstWitness = $"sample={samples}, coldTime={hero.ColdModel.ElapsedSeconds:F4}s, " +
                            $"state={hero.CurrentLocomotionState}, arms={hero.ColdArmWeight:F4}, " +
                            $"rub={hero.ColdModel.RubNormalizedTime:F4}, {a.Name} / {b.Name}, " +
                            $"pairCenter={witness:F4}";
                    }
                    // Tangency, including a palm resting on the opposite
                    // shoulder, passes. No opposing hand/sleeve pair is exempt.
                    if (clearance < -PenetrationTolerance)
                    {
                        penetratingPairs++;
                        pair.penetrating_samples++;
                    }
                }
            }
        }

        internal void AssertNoInterpenetration()
        {
            SaveReport();
            string report = $"Cold arm volumes: {samples} final-pose samples, " +
                $"{penetratingPairs} penetrating pairs, worst signed SAT clearance " +
                $"{worstClearance * 1000f:F3} mm; {worstWitness}.";
            Debug.Log(report);
            Assert.That(samples, Is.GreaterThan(0));
            Assert.That(penetratingPairs, Is.Zero, report);
        }

        private Surface BuildSurface(string name, SkinnedMeshRenderer renderer)
        {
            BakeLocal(renderer);
            // Unity's FBX importer splits vertices at UV and normal seams.
            // Weld positions for topology only; actual samples still read the
            // original skinned vertex so this never replaces the rendered mesh.
            var welded = new Dictionary<Vector3Int, int>();
            var representatives = new List<int>();
            var remap = new int[readback.Count];
            Vector3 localSize = baked.bounds.size;
            float weldCell = Mathf.Max(0.0000000001f,
                Mathf.Max(localSize.x, Mathf.Max(localSize.y, localSize.z)) * 0.000001f);
            for (int index = 0; index < readback.Count; index++)
            {
                // Topology must not depend on absolute village coordinates.
                // This tolerance is one millionth of this local mesh's size.
                Vector3 vertex = readback[index] / weldCell;
                var key = new Vector3Int(Mathf.RoundToInt(vertex.x),
                    Mathf.RoundToInt(vertex.y), Mathf.RoundToInt(vertex.z));
                if (!welded.TryGetValue(key, out int canonical))
                {
                    canonical = representatives.Count;
                    welded.Add(key, canonical);
                    representatives.Add(index);
                }
                remap[index] = canonical;
            }
            var triangles = new List<int>();
            var edgeCounts = new Dictionary<long, int>();
            int[] originalTriangles = baked.triangles;
            for (int index = 0; index < originalTriangles.Length; index += 3)
            {
                int a = remap[originalTriangles[index]];
                int b = remap[originalTriangles[index + 1]];
                int c = remap[originalTriangles[index + 2]];
                if (a == b || b == c || a == c) continue;
                triangles.Add(a); triangles.Add(b); triangles.Add(c);
                CountEdge(edgeCounts, a, b);
                CountEdge(edgeCounts, b, c);
                CountEdge(edgeCounts, c, a);
            }
            foreach (KeyValuePair<long, int> edge in edgeCounts)
                Assert.That(edge.Value, Is.EqualTo(2),
                    name + " must have closed welded topology before volume SAT is valid.");
            Assert.That(triangles.Count, Is.GreaterThan(0), name);
            return new Surface
            {
                Name = name, Renderer = renderer,
                SourceVertices = representatives.ToArray(),
                Vertices = new Vector3[representatives.Count],
                Triangles = triangles.ToArray()
            };
        }

        private static void CountEdge(Dictionary<long, int> counts, int a, int b)
        {
            long key = ((long)Mathf.Min(a, b) << 32) | (uint)Mathf.Max(a, b);
            counts.TryGetValue(key, out int count);
            counts[key] = count + 1;
        }

        private void BakeLocal(SkinnedMeshRenderer renderer)
        {
            baked.Clear(false);
            renderer.BakeMesh(baked, true);
            readback.Clear();
            baked.GetVertices(readback);
        }

        private void BakeWorld(SkinnedMeshRenderer renderer, Vector3 origin)
        {
            BakeLocal(renderer);
            Matrix4x4 matrix = renderer.transform.localToWorldMatrix;
            // Remove the large translation before transforming individual
            // vertices. TransformPoint(vertex)-origin already lost its low
            // bits while adding the absolute world translation.
            matrix.m03 -= origin.x;
            matrix.m13 -= origin.y;
            matrix.m23 -= origin.z;
            for (int index = 0; index < readback.Count; index++)
                readback[index] = matrix.MultiplyPoint3x4(readback[index]);
        }

        private void Refresh(Surface surface, Vector3 origin)
        {
            BakeWorld(surface.Renderer, origin);
            for (int index = 0; index < surface.Vertices.Length; index++)
                surface.Vertices[index] = readback[surface.SourceVertices[index]];
            surface.Bounds = new Bounds(surface.Vertices[0], Vector3.zero);
            foreach (Vector3 vertex in surface.Vertices) surface.Bounds.Encapsulate(vertex);
            surface.Normals.Clear();
            surface.Edges.Clear();
            for (int index = 0; index < surface.Triangles.Length; index += 3)
            {
                Vector3 a = surface.Vertices[surface.Triangles[index]];
                Vector3 b = surface.Vertices[surface.Triangles[index + 1]];
                Vector3 c = surface.Vertices[surface.Triangles[index + 2]];
                Vector3 normal = Vector3.Cross(b - a, c - a);
                if (normal.sqrMagnitude <= AxisLengthSquared) continue;
                normal.Normalize();
                float minimum = 0f, maximum = 0f;
                foreach (Vector3 point in surface.Vertices)
                {
                    float side = Vector3.Dot(normal, point - a);
                    minimum = Mathf.Min(minimum, side);
                    maximum = Mathf.Max(maximum, side);
                }
                if (minimum < -PlaneTolerance && maximum > PlaneTolerance)
                {
                    geometryDiagnostic = $"{surface.Name} sample={samples + 1} face={index / 3}: " +
                        $"plane min={minimum:G9}m max={maximum:G9}m, " +
                        $"bounds={surface.Bounds.size:F6}m, " +
                        $"lossyScale={surface.Renderer.transform.lossyScale:F6}. " +
                        "Not convex in the final pose; convex-hull penetration " +
                        "cannot prove rendered-mesh penetration.";
                    SaveSurfaceDiagnostic(surface);
                    SaveReport();
                    Assert.Fail(geometryDiagnostic);
                }
                AddAxis(surface.Normals, normal);
                AddAxis(surface.Edges, b - a);
                AddAxis(surface.Edges, c - b);
                AddAxis(surface.Edges, a - c);
            }
            if (surface.Normals.Count == 0)
                Assert.Fail(surface.Name + " has no nondegenerate convex face axes.");
        }

        private static void AddAxis(List<Vector3> axes, Vector3 axis)
        {
            float squared = axis.sqrMagnitude;
            if (squared <= AxisLengthSquared) return;
            axis /= Mathf.Sqrt(squared);
            foreach (Vector3 existing in axes)
                if (Mathf.Abs(Vector3.Dot(existing, axis)) > 0.999999f) return;
            axes.Add(axis);
        }

        private static float SignedClearance(Surface a, Surface b)
        {
            Vector3 gap = Vector3.Max(b.Bounds.min - a.Bounds.max,
                a.Bounds.min - b.Bounds.max);
            float bestGap = Mathf.Max(gap.x, Mathf.Max(gap.y, gap.z));
            if (bestGap > 0.02f) return bestGap;
            float minimumOverlap = float.PositiveInfinity;
            foreach (Vector3 normal in a.Normals)
            {
                minimumOverlap = Mathf.Min(minimumOverlap, Overlap(a, b, normal));
                if (minimumOverlap < 0f) return -minimumOverlap;
            }
            foreach (Vector3 normal in b.Normals)
            {
                minimumOverlap = Mathf.Min(minimumOverlap, Overlap(a, b, normal));
                if (minimumOverlap < 0f) return -minimumOverlap;
            }
            foreach (Vector3 first in a.Edges)
            {
                foreach (Vector3 second in b.Edges)
                {
                    Vector3 axis = Vector3.Cross(first, second);
                    float squared = axis.sqrMagnitude;
                    if (squared <= AxisLengthSquared) continue;
                    axis /= Mathf.Sqrt(squared);
                    minimumOverlap = Mathf.Min(minimumOverlap, Overlap(a, b, axis));
                    if (minimumOverlap < 0f) return -minimumOverlap;
                }
            }
            // This is an exact convex separation depth for overlap, and a
            // conservative separating-axis gap (not Euclidean distance) outside.
            return -minimumOverlap;
        }

        private static float Overlap(Surface a, Surface b, Vector3 axis)
        {
            float aMinimum = float.PositiveInfinity, aMaximum = float.NegativeInfinity;
            float bMinimum = float.PositiveInfinity, bMaximum = float.NegativeInfinity;
            foreach (Vector3 point in a.Vertices)
            {
                float projection = Vector3.Dot(point, axis);
                aMinimum = Mathf.Min(aMinimum, projection);
                aMaximum = Mathf.Max(aMaximum, projection);
            }
            foreach (Vector3 point in b.Vertices)
            {
                float projection = Vector3.Dot(point, axis);
                bMinimum = Mathf.Min(bMinimum, projection);
                bMaximum = Mathf.Max(bMaximum, projection);
            }
            return Mathf.Min(aMaximum - bMinimum, bMaximum - aMinimum);
        }

        public void Dispose()
        {
            SaveReport();
            Object.DestroyImmediate(baked);
        }

        private void SaveReport()
        {
            if (pairReports == null) return;
            string directory = Path.Combine(Directory.GetCurrentDirectory(),
                "Captures", "ColdHeroVerification");
            Directory.CreateDirectory(directory);
            File.WriteAllText(Path.Combine(directory, "arm-separation.json"),
                JsonUtility.ToJson(new ProbeReport
                {
                    sample_count = samples,
                    penetrating_pair_samples = penetratingPairs,
                    geometry_diagnostic = geometryDiagnostic,
                    pairs = samples > 0 ? pairReports : Array.Empty<PairReport>()
                }, true));
        }

        private void SaveSurfaceDiagnostic(Surface surface)
        {
            string directory = Path.Combine(Directory.GetCurrentDirectory(),
                "Captures", "ColdHeroVerification");
            Directory.CreateDirectory(directory);
            File.WriteAllText(Path.Combine(directory, "arm-surface-diagnostic.json"),
                JsonUtility.ToJson(new SurfaceDiagnostic
                {
                    mesh = surface.Name,
                    reason = geometryDiagnostic,
                    lossy_scale = surface.Renderer.transform.lossyScale,
                    actor_relative_bounds_size = surface.Bounds.size,
                    actor_relative_vertices = surface.Vertices,
                    triangles = surface.Triangles
                }, true));
        }
    }
}

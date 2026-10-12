using System;
using System.Collections.Generic;
using Unity.Collections;
using Unity.Profiling;
using UnityEngine;

namespace BarPromenade
{
    /// <summary>Reuses the scarf's measured anatomy; adds bounded worn collar, sleeve and scarf envelopes.</summary>
    internal sealed class PlayerHairContacts : IDisposable
    {
        private static readonly ProfilerMarker NativeMarker = new ProfilerMarker("BarPromenade.SecondaryMotion.Hair.NativeContacts");
        internal static long TotalNativeSolves { get; private set; }
        internal static long TotalBurstNativeSolves { get; private set; }
        private NativeArray<Vector3> nativePoints, nativeSamples, nativeOriginals;
        private NativeArray<PlayerJacketSurfaceJob.BodyProxy> nativeBodies;
        private NativeArray<Plane> nativePlanes;
        private NativeArray<PlayerEquipmentContacts.BoxSnapshot> nativeBoxes;
        private NativeArray<PlayerHairContactJob.Garment> nativeGarments;
        private NativeArray<PlayerHairContactJob.TailTriangle> nativeTail;
        private NativeArray<int> nativeHints;
        private int nativeBodyCount, nativePlaneCount, nativeBoxCount, nativeGarmentCount, nativeTailCount;
        private bool nativeReady, nativeHasEquipmentBounds;
        private Bounds nativeEquipmentBounds;
        private bool lastNativeBurst;
        private sealed class GarmentProxy
        {
            public Renderer Renderer;
            public Transform Bone;
            public Matrix4x4 UnitToBone, UnitToWorld, WorldToUnit;
            public bool Active;
            public bool FlatPanel;
        }

        private static readonly Vector3[] SampleDirections =
        { Vector3.zero, Vector3.right, Vector3.left, Vector3.up, Vector3.down, Vector3.forward, Vector3.back };
        private readonly PlayerScarfBodyContacts body;
        private readonly PlayerScarfBodyContacts.PointQueryCache pointQueryCache;
        private readonly PlayerScarfPresentation scarf;
        private readonly int[] tailTriangles;
        private readonly Bounds[] tailBounds;
        private bool tailActive;
        private readonly List<GarmentProxy> garments = new List<GarmentProxy>(8);
        private readonly Vector3[] samples = new Vector3[7];
        private readonly Vector3[] originals = new Vector3[7];

        public PlayerHairContacts(Player3DAssetRegistry registry, PlayerScarfPresentation scarf)
        {
            body = new PlayerScarfBodyContacts(registry, useMeshSupportPlanes: true, pointQuerySlots: 1);
            pointQueryCache = body.CreatePointQueryCache(PlayerHair.PointCount * SampleDirections.Length);
            this.scarf = scarf;
            tailTriangles = scarf != null && scarf.TailRenderer != null && scarf.TailRenderer.sharedMesh != null
                ? scarf.TailRenderer.sharedMesh.triangles : Array.Empty<int>();
            tailBounds = new Bounds[tailTriangles.Length / 3];
            var scratch = new Mesh { name = "Hero Hair Contact Measurement", hideFlags = HideFlags.HideAndDontSave };
            try
            {
                foreach (Player3DMeshBinding binding in registry.MeshBindings)
                {
                    if (binding == null || binding.Role != "clothing" || binding.Renderer == null || binding.Bone == null) continue;
                    bool collar = binding.MeshName.IndexOf("Collar", StringComparison.OrdinalIgnoreCase) >= 0;
                    // Primary sleeves already belong to the measured body shells.
                    bool epaulette = binding.MeshName.IndexOf("Epaulette", StringComparison.OrdinalIgnoreCase) >= 0;
                    if (collar || epaulette) Measure(binding.Renderer, binding.Bone, scratch, true);
                }
                if (scarf != null)
                    foreach (Renderer renderer in scarf.Renderers)
                        if (renderer != null && renderer != scarf.TailRenderer)
                            Measure(renderer, registry.Anchors.Head, scratch);
            }
            finally { PlayerScarfResources.DestroyOwned(scratch); }
            UpdatePose();
        }

        public void UpdatePose()
        {
            body.UpdatePose();
            foreach (GarmentProxy garment in garments)
            {
                garment.Active = garment.Renderer != null && garment.Renderer.enabled &&
                    garment.Renderer.gameObject.activeInHierarchy && garment.Bone != null;
                if (!garment.Active) continue;
                garment.UnitToWorld = garment.Bone.localToWorldMatrix * garment.UnitToBone;
                garment.WorldToUnit = garment.UnitToWorld.inverse;
            }
            // The scarf already owns these deformed vertices. Reading its bounded tail
            // needs no additional mesh bake, deformation buffer or scene-geometry scan.
            tailActive = scarf != null && scarf.TailSimulation != null && scarf.TailSimulation.IsActive;
            if (tailActive)
            {
                Vector3[] vertices = scarf.TailSimulation.WorldVertices;
                for (int triangle = 0; triangle < tailBounds.Length; triangle++)
                {
                    int i = triangle * 3;
                    var bounds = new Bounds(vertices[tailTriangles[i]], Vector3.zero);
                    bounds.Encapsulate(vertices[tailTriangles[i + 1]]);
                    bounds.Encapsulate(vertices[tailTriangles[i + 2]]);
                    bounds.Expand(.034f);
                    tailBounds[triangle] = bounds;
                }
            }
            PrepareNativeSnapshot();
        }

        public int Resolve(Vector3[] points)
            => TryNativeResolve(points, out int count) ? count : ResolveManaged(points);

        private int ResolveManaged(Vector3[] points)
        {
            int count = 0;
            for (int i = 0; i < points.Length; i++)
            {
                if (i % PlayerHair.PointsPerChain == 0) continue;
                Vector3 corrected = ResolvePoint(points[i], i % PlayerHair.PointsPerChain, i);
                if ((corrected - points[i]).sqrMagnitude > .00000001f) count++;
                points[i] = corrected;
            }
            // Different locks have a small thickness even where their tapered free ends meet.
            for (int i = 1; i < points.Length; i++)
            for (int j = i + 1; j < points.Length; j++)
            {
                if (i / PlayerHair.PointsPerChain == j / PlayerHair.PointsPerChain ||
                    i % PlayerHair.PointsPerChain == 0 || j % PlayerHair.PointsPerChain == 0) continue;
                float separation = Radius(i % PlayerHair.PointsPerChain) + Radius(j % PlayerHair.PointsPerChain);
                Vector3 difference = points[i] - points[j];
                float distance = difference.magnitude;
                if (distance >= separation) continue;
                Vector3 correction = (distance > .00001f ? difference / distance : Vector3.right) * ((separation - distance) * .5f);
                points[i] += correction; points[j] -= correction; count++;
            }
            return count;
        }

        private void PrepareNativeSnapshot()
        {
            try { PrepareNativeSnapshotCore(); }
            catch { Dispose(); throw; }
        }

        private void PrepareNativeSnapshotCore()
        {
            nativeReady = false;
            if (!body.TryGetClothSnapshotSizes(out nativeBodyCount, out nativePlaneCount, out nativeBoxCount)) return;
            Ensure(ref nativePoints, PlayerHair.PointCount); Ensure(ref nativeSamples, 7); Ensure(ref nativeOriginals, 7);
            Ensure(ref nativeBodies, nativeBodyCount); Ensure(ref nativePlanes, Math.Max(1, nativePlaneCount));
            Ensure(ref nativeBoxes, Math.Max(64, nativeBoxCount)); Ensure(ref nativeGarments, Math.Max(1, garments.Count));
            nativeTailCount = tailActive ? tailBounds.Length : 0;
            Ensure(ref nativeTail, Math.Max(1, nativeTailCount));
            int hintCount = PlayerHair.PointCount * 7 * nativeBodyCount;
            if (!nativeHints.IsCreated || nativeHints.Length != hintCount)
            { Release(ref nativeHints); nativeHints = new NativeArray<int>(hintCount, Allocator.Persistent); }
            body.CopyClothSnapshot(nativeBodies, nativePlanes, nativeBoxes);
            nativeHasEquipmentBounds = body.TryGetClothEquipmentBounds(out nativeEquipmentBounds);
            nativeGarmentCount = 0;
            foreach (GarmentProxy garment in garments)
                if (garment.Active) nativeGarments[nativeGarmentCount++] = new PlayerHairContactJob.Garment
                { ToUnit = garment.WorldToUnit, ToWorld = garment.UnitToWorld, Flat = garment.FlatPanel ? 1 : 0 };
            if (tailActive)
            {
                Vector3[] vertices = scarf.TailSimulation.WorldVertices;
                for (int triangle = 0; triangle < nativeTailCount; triangle++)
                {
                    int i = triangle * 3;
                    nativeTail[triangle] = new PlayerHairContactJob.TailTriangle
                    { A = vertices[tailTriangles[i]], B = vertices[tailTriangles[i + 1]], C = vertices[tailTriangles[i + 2]],
                        Minimum = tailBounds[triangle].min, Maximum = tailBounds[triangle].max };
                }
            }
            nativeReady = true;
        }

        private bool TryNativeResolve(Vector3[] points, out int count)
        {
            count = 0;
            if (!nativeReady || points.Length != PlayerHair.PointCount) return false;
            foreach (Vector3 point in points)
                if (!float.IsFinite(point.x) || !float.IsFinite(point.y) || !float.IsFinite(point.z)) return false;
            using var profile = NativeMarker.Auto();
            NativeArray<Vector3>.Copy(points, 0, nativePoints, 0, points.Length);
            var job = new PlayerHairContactJob
            {
                Points = nativePoints.GetSubArray(0, points.Length), Samples = nativeSamples.GetSubArray(0, 7),
                Originals = nativeOriginals.GetSubArray(0, 7), Garments = nativeGarments.GetSubArray(0, nativeGarmentCount),
                Tail = nativeTail.GetSubArray(0, nativeTailCount),
                Body = new PlayerJacketSurfaceJob
                {
                    Bodies = nativeBodies.GetSubArray(0, nativeBodyCount), Planes = nativePlanes.GetSubArray(0, nativePlaneCount),
                    Boxes = nativeBoxes.GetSubArray(0, nativeBoxCount), PlaneHints = nativeHints,
                    HasEquipmentBounds = nativeHasEquipmentBounds ? 1 : 0,
                    EquipmentMinimum = nativeEquipmentBounds.min, EquipmentMaximum = nativeEquipmentBounds.max
                }
            };
            PlayerHairContactJob.RunSynchronous(ref job);
            NativeArray<Vector3>.Copy(nativePoints, 0, points, 0, points.Length);
            count = job.ContactCount; lastNativeBurst = job.ManagedExecution == 0;
            TotalNativeSolves++; if (lastNativeBurst) TotalBurstNativeSolves++;
            return true;
        }

        internal bool TryCompareForDiagnostics(Vector3[] points, out Vector3[] managed, out Vector3[] native,
            out int managedContacts, out int nativeContacts, out bool executedInBurst)
        {
            managed = (Vector3[])points.Clone(); native = (Vector3[])points.Clone();
            managedContacts = ResolveManaged(managed); executedInBurst = false;
            if (!TryNativeResolve(native, out nativeContacts)) return false;
            executedInBurst = lastNativeBurst; return true;
        }

        private static void Ensure<T>(ref NativeArray<T> buffer, int count) where T : struct
        {
            if (buffer.IsCreated && buffer.Length >= count) return;
            Release(ref buffer);
            buffer = new NativeArray<T>(Mathf.NextPowerOfTwo(Math.Max(1, count)), Allocator.Persistent, NativeArrayOptions.UninitializedMemory);
        }
        private static void Release<T>(ref NativeArray<T> buffer) where T : struct
        { if (buffer.IsCreated) buffer.Dispose(); buffer = default; }
        public void Dispose()
        {
            body?.Dispose();
            Release(ref nativePoints); Release(ref nativeSamples); Release(ref nativeOriginals); Release(ref nativeBodies);
            Release(ref nativePlanes); Release(ref nativeBoxes); Release(ref nativeGarments); Release(ref nativeTail); Release(ref nativeHints);
            nativeReady = false;
        }

        public Vector3 ResolvePoint(Vector3 point, int chainPoint, int location = -1)
        {
            float radius = Radius(chainPoint);
            for (int pass = 0; pass < 2; pass++)
            {
                for (int sample = 0; sample < samples.Length; sample++)
                    samples[sample] = originals[sample] = point + SampleDirections[sample] * radius;
                if (location < 0) body.Resolve(samples);
                else
                    for (int sample = 0; sample < samples.Length; sample++)
                        body.ResolvePointCached(ref samples[sample], pointQueryCache, location * samples.Length + sample);
                foreach (GarmentProxy garment in garments)
                {
                    if (!garment.Active) continue;
                    for (int sample = 0; sample < samples.Length; sample++)
                    {
                        Vector3 unit = garment.WorldToUnit.MultiplyPoint3x4(samples[sample]);
                        if (garment.FlatPanel)
                        {
                            Vector3 absolute = new Vector3(Mathf.Abs(unit.x), Mathf.Abs(unit.y), Mathf.Abs(unit.z));
                            if (Mathf.Max(absolute.x, Mathf.Max(absolute.y, absolute.z)) >= 1f) continue;
                            int axis = absolute.x >= absolute.y && absolute.x >= absolute.z ? 0 : absolute.y >= absolute.z ? 1 : 2;
                            unit[axis] = unit[axis] >= 0f ? 1.0001f : -1.0001f;
                            samples[sample] = garment.UnitToWorld.MultiplyPoint3x4(unit);
                            continue;
                        }
                        if (unit.sqrMagnitude >= 1f) continue;
                        unit = unit.sqrMagnitude > .00000001f ? unit.normalized : Vector3.back;
                        samples[sample] = garment.UnitToWorld.MultiplyPoint3x4(unit * 1.0001f);
                    }
                }
                Vector3 correction = Vector3.zero;
                for (int sample = 0; sample < samples.Length; sample++)
                {
                    Vector3 candidate = samples[sample] - originals[sample];
                    if (candidate.sqrMagnitude > correction.sqrMagnitude) correction = candidate;
                }
                point += correction;
                point = ResolveTail(point, radius + .002f);
                if (correction.sqrMagnitude < .00000001f) break;
            }
            return point;
        }

        private static float Radius(int point) => point == 1 ? .014f : point == 2 ? .010f : .005f;

        private Vector3 ResolveTail(Vector3 point, float radius)
        {
            if (!tailActive) return point;
            Vector3[] vertices = scarf.TailSimulation.WorldVertices;
            for (int triangle = 0; triangle < tailBounds.Length; triangle++)
            {
                if (!tailBounds[triangle].Contains(point)) continue;
                int i = triangle * 3;
                Vector3 a = vertices[tailTriangles[i]], b = vertices[tailTriangles[i + 1]], c = vertices[tailTriangles[i + 2]];
                Vector3 closest = PlayerScarfContactSolver.ClosestPoint(point, a, b, c);
                Vector3 offset = point - closest;
                float square = offset.sqrMagnitude;
                if (square >= radius * radius) continue;
                Vector3 normal = square > .00000001f ? offset / Mathf.Sqrt(square) : Vector3.Cross(b - a, c - a).normalized;
                point = closest + normal * radius;
            }
            return point;
        }

        private void Measure(Renderer renderer, Transform bone, Mesh scratch, bool flatPanel = false)
        {
            Vector3[] vertices;
            if (renderer is SkinnedMeshRenderer skin && skin.sharedMesh != null)
            { skin.BakeMesh(scratch, true); vertices = scratch.vertices; }
            else
            {
                Mesh source = renderer.GetComponent<MeshFilter>()?.sharedMesh;
                if (source == null) return;
                vertices = source.vertices;
            }
            if (vertices.Length == 0) return;
            Matrix4x4 toBone = bone.worldToLocalMatrix * renderer.transform.localToWorldMatrix;
            Bounds bounds = new Bounds(toBone.MultiplyPoint3x4(vertices[0]), Vector3.zero);
            for (int i = 0; i < vertices.Length; i++)
            { vertices[i] = toBone.MultiplyPoint3x4(vertices[i]); bounds.Encapsulate(vertices[i]); }
            Vector3 radius = bounds.extents;
            // Metre padding is converted through the actual imported bone scale.
            Matrix4x4 matrix = bone.localToWorldMatrix;
            radius += new Vector3(.002f / Mathf.Max(.00001f, matrix.MultiplyVector(Vector3.right).magnitude),
                .002f / Mathf.Max(.00001f, matrix.MultiplyVector(Vector3.up).magnitude),
                .002f / Mathf.Max(.00001f, matrix.MultiplyVector(Vector3.forward).magnitude));
            float expansion = 1f;
            foreach (Vector3 vertex in vertices)
            {
                Vector3 offset = vertex - bounds.center;
                expansion = Mathf.Max(expansion, new Vector3(offset.x / radius.x, offset.y / radius.y, offset.z / radius.z).magnitude);
            }
            garments.Add(new GarmentProxy { Renderer = renderer, Bone = bone, FlatPanel = flatPanel,
                UnitToBone = Matrix4x4.TRS(bounds.center, Quaternion.identity, radius * (flatPanel ? 1f : expansion)) });
        }
    }
}

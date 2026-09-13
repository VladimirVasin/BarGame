using System;
using System.Collections.Generic;
using UnityEngine;

namespace BarPromenade
{
    /// <summary>Reuses the scarf's measured anatomy; adds bounded worn collar, sleeve and scarf envelopes.</summary>
    internal sealed class PlayerHairContacts
    {
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
        private readonly PlayerScarfPresentation scarf;
        private readonly int[] tailTriangles;
        private readonly Bounds[] tailBounds;
        private bool tailActive;
        private readonly List<GarmentProxy> garments = new List<GarmentProxy>(8);
        private readonly Vector3[] samples = new Vector3[7];
        private readonly Vector3[] originals = new Vector3[7];

        public PlayerHairContacts(Player3DAssetRegistry registry, PlayerScarfPresentation scarf)
        {
            body = new PlayerScarfBodyContacts(registry, useMeshSupportPlanes: true);
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
            if (!tailActive) return;
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

        public int Resolve(Vector3[] points)
        {
            int count = 0;
            for (int i = 0; i < points.Length; i++)
            {
                if (i % PlayerHair.PointsPerChain == 0) continue;
                Vector3 corrected = ResolvePoint(points[i], i % PlayerHair.PointsPerChain);
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

        public Vector3 ResolvePoint(Vector3 point, int chainPoint)
        {
            float radius = Radius(chainPoint);
            for (int pass = 0; pass < 2; pass++)
            {
                for (int sample = 0; sample < samples.Length; sample++)
                    samples[sample] = originals[sample] = point + SampleDirections[sample] * radius;
                body.Resolve(samples);
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

using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace BarPromenade
{
    /// <summary>Bounded authored overlays retain wounds on the original combat/ragdoll skin.</summary>
    public sealed class CombatDamageMarks
    {
        public const int MaximumProjectileWounds = 64;
        private const int HolesPerLayer = 8;
        private static Material sharedProjectileMaterial;
        private static readonly int BulletHolesId = Shader.PropertyToID("_BulletHoles");
        private static readonly int BulletHoleCountId = Shader.PropertyToID("_BulletHoleCount");
        private sealed class Patch
        {
            public SkinnedMeshRenderer Renderer, Source;
            public Vector3 Vertex, Normal;
            public BoneWeight Weight;
            public Matrix4x4[] BindPoses;
            public Transform[] Bones;
            public Vector3[] Vertices, Normals, SkinnedVertices;
            public Vector2[] Uv;
            public BoneWeight[] Weights;
            public int[] Triangles;
            public bool Active;
            public int Grade;

            public Vector3 Position => Skin(false);
            public Vector3 Direction => Skin(true).normalized;
            private Vector3 Skin(bool normal) => Skin(Vertex, Normal, Weight, normal);
            public Vector3 SkinVertex(int index, bool normal) => Skin(Vertices[index], Normals[index], Weights[index], normal);
            private Vector3 Skin(Vector3 vertex, Vector3 vertexNormal, BoneWeight weights, bool normal)
            {
                Vector3 result = Vector3.zero;
                Add(weights.boneIndex0, weights.weight0); Add(weights.boneIndex1, weights.weight1);
                Add(weights.boneIndex2, weights.weight2); Add(weights.boneIndex3, weights.weight3);
                return result;
                void Add(int index, float weight)
                {
                    if (weight <= 0f) return;
                    Matrix4x4 matrix = Bones[index].localToWorldMatrix * BindPoses[index];
                    result += (normal ? matrix.MultiplyVector(vertexNormal) : matrix.MultiplyPoint3x4(vertex)) * weight;
                }
            }
        }

        private sealed class ProjectileWound
        {
            public Patch Patch;
            public int A, B, C;
            public Vector3 Barycentric;
            public Vector2 Uv;
            public float Radius;
            public Vector3 Position => Sample(false);
            public Vector3 Direction => Sample(true).normalized;
            private Vector3 Sample(bool normal) => Patch.SkinVertex(A, normal) * Barycentric.x +
                Patch.SkinVertex(B, normal) * Barycentric.y + Patch.SkinVertex(C, normal) * Barycentric.z;
        }

        private sealed class ProjectileLayer
        {
            public Patch Patch;
            public SkinnedMeshRenderer Renderer;
            public readonly Vector4[] Holes = new Vector4[HolesPerLayer];
            public int Count;
        }

        private readonly List<Patch> patches = new List<Patch>();
        private readonly List<ProjectileWound> projectileWounds = new List<ProjectileWound>(MaximumProjectileWounds);
        private readonly List<ProjectileLayer> projectileLayers = new List<ProjectileLayer>();
        private readonly MaterialPropertyBlock properties = new MaterialPropertyBlock();
        private readonly Material bloodMaterial;
        private Patch last;
        private ProjectileWound lastProjectile;
        public int Count { get; private set; }
        public int ProjectileCount => projectileWounds.Count;
        public Vector3 BleedPosition => lastProjectile != null ? lastProjectile.Position : last != null ? last.Position : Vector3.zero;
        public Vector3 BleedDirection => lastProjectile != null ? lastProjectile.Direction : last != null ? last.Direction : Vector3.up;

        public bool TryGetProjectileBleed(int index, out Vector3 position, out Vector3 direction)
        {
            position = Vector3.zero; direction = Vector3.up;
            if (index < 0 || index >= projectileWounds.Count) return false;
            ProjectileWound wound = projectileWounds[index];
            if (wound.Patch.Source == null || !wound.Patch.Source.enabled || !wound.Patch.Source.gameObject.activeInHierarchy) return false;
            position = wound.Position; direction = wound.Direction;
            return true;
        }

        public CombatDamageMarks(CombatActor actor, Material material)
        {
            bloodMaterial = material;
            Transform rig = actor.DamageRigRoot;
            if (rig == null) throw new InvalidOperationException("Blood requires the actor's original rendered rig.");
            GameObject model = Resources.Load<GameObject>("CombatBlood/Wounds" + (actor.IsHero ? "Hero" : "Npc"));
            if (model == null) throw new InvalidOperationException("Missing authored combat wound model.");
            var sources = new Dictionary<string, SkinnedMeshRenderer>(StringComparer.Ordinal);
            foreach (SkinnedMeshRenderer renderer in rig.GetComponentsInChildren<SkinnedMeshRenderer>(true))
                sources[renderer.name] = renderer;
            foreach (SkinnedMeshRenderer template in model.GetComponentsInChildren<SkinnedMeshRenderer>(true))
            {
                string[] name = template.name.Split(new[] { "__" }, StringSplitOptions.None);
                if (name.Length != 3 || !sources.TryGetValue(name[1], out SkinnedMeshRenderer source))
                    throw new InvalidOperationException("Wound surface missing from live rig: " + template.name);
                var host = new GameObject(template.name);
                host.transform.SetParent(source.transform, false);
                SkinnedMeshRenderer renderer = host.AddComponent<SkinnedMeshRenderer>();
                Mesh mesh = template.sharedMesh;
                Transform[] sourceBones = source.bones;
                renderer.sharedMesh = mesh; renderer.bones = sourceBones; renderer.rootBone = source.rootBone;
                renderer.sharedMaterial = material; renderer.localBounds = source.localBounds;
                renderer.shadowCastingMode = ShadowCastingMode.Off; renderer.receiveShadows = true;
                renderer.lightProbeUsage = LightProbeUsage.BlendProbes; renderer.reflectionProbeUsage = ReflectionProbeUsage.Off;
                renderer.updateWhenOffscreen = true; renderer.enabled = false;
                Vector2[] uv = mesh.uv;
                int centre = 0;
                for (int i = 1; i < uv.Length; i++)
                    if ((uv[i] - Vector2.one * .5f).sqrMagnitude < (uv[centre] - Vector2.one * .5f).sqrMagnitude) centre = i;
                patches.Add(new Patch { Renderer = renderer, Source = source, Vertex = mesh.vertices[centre],
                    Normal = mesh.normals[centre], Weight = mesh.boneWeights[centre], BindPoses = mesh.bindposes, Bones = sourceBones,
                    Vertices = mesh.vertices, Normals = mesh.normals, Uv = uv, Weights = mesh.boneWeights,
                    Triangles = mesh.triangles, SkinnedVertices = new Vector3[mesh.vertexCount] });
            }
        }

        public void Add(Vector3 point, Vector3 incoming, bool projectile = false)
        {
            Patch nearest = null;
            float best = float.PositiveInfinity;
            foreach (Patch patch in patches)
            {
                if (patch.Source == null || !patch.Source.enabled || !patch.Source.gameObject.activeInHierarchy) continue;
                float distance = (patch.Position - point).sqrMagnitude;
                float score = distance + Mathf.Max(0f, Vector3.Dot(patch.Direction, incoming)) * .15f;
                if (patch.Active && !projectile) score += .045f;
                if (score >= best) continue;
                nearest = patch; best = score;
            }
            if (nearest == null) return;
            if (projectile)
            {
                AddProjectile(nearest, point);
                RefreshVisibility();
                return;
            }
            if (!nearest.Active) { nearest.Active = true; Count++; }
            nearest.Grade = Mathf.Min(3, nearest.Grade + 1);
            float scale = 1f - (nearest.Grade - 1) * .15f;
            nearest.Renderer.GetPropertyBlock(properties);
            properties.SetVector("_BaseMap_ST", new Vector4(scale, scale, (1f - scale) * .5f, (1f - scale) * .5f));
            nearest.Renderer.SetPropertyBlock(properties); properties.Clear();
            last = nearest;
            lastProjectile = null;
            RefreshVisibility();
        }

        private void AddProjectile(Patch patch, Vector3 point)
        {
            ProjectileWound wound = LocateProjectile(patch, point);
            last = patch; lastProjectile = wound;
            ProjectileWound closest = null;
            float best = float.PositiveInfinity;
            foreach (ProjectileWound existing in projectileWounds)
            {
                float distance = (existing.Position - wound.Position).sqrMagnitude;
                if (distance >= best) continue;
                closest = existing; best = distance;
            }
            // A repeated shot through the same opening keeps that opening. At
            // saturation retain every old hole rather than overwriting history.
            if (closest != null && (best < .012f * .012f || projectileWounds.Count >= MaximumProjectileWounds)) return;
            ProjectileLayer layer = null;
            foreach (ProjectileLayer candidate in projectileLayers)
                if (candidate.Patch == patch && candidate.Count < HolesPerLayer) { layer = candidate; break; }
            if (layer == null)
            {
                foreach (ProjectileLayer candidate in projectileLayers)
                    if (candidate.Count == 0) { layer = candidate; break; }
                if (layer == null)
                {
                    var host = new GameObject("Projectile Wounds__" + patch.Source.name + "__" + projectileLayers.Count);
                    layer = new ProjectileLayer { Renderer = host.AddComponent<SkinnedMeshRenderer>() };
                    projectileLayers.Add(layer);
                }
                ConfigureProjectileLayer(layer, patch);
            }
            layer.Holes[layer.Count++] = new Vector4(wound.Uv.x, wound.Uv.y, wound.Radius, 0f);
            properties.Clear();
            properties.SetFloat("_BulletWound", 1f);
            properties.SetFloat(BulletHoleCountId, layer.Count);
            properties.SetVectorArray(BulletHolesId, layer.Holes);
            layer.Renderer.SetPropertyBlock(properties); properties.Clear();
            projectileWounds.Add(wound);
            Count++;
        }

        private void ConfigureProjectileLayer(ProjectileLayer layer, Patch patch)
        {
            layer.Patch = patch;
            SkinnedMeshRenderer renderer = layer.Renderer;
            renderer.transform.SetParent(patch.Source.transform, false);
            renderer.sharedMesh = patch.Renderer.sharedMesh;
            renderer.bones = patch.Bones; renderer.rootBone = patch.Source.rootBone;
            renderer.sharedMaterial = RequireProjectileMaterial(bloodMaterial);
            renderer.localBounds = patch.Source.localBounds;
            renderer.shadowCastingMode = ShadowCastingMode.Off; renderer.receiveShadows = true;
            renderer.lightProbeUsage = LightProbeUsage.BlendProbes; renderer.reflectionProbeUsage = ReflectionProbeUsage.Off;
            renderer.updateWhenOffscreen = true;
        }

        private static ProjectileWound LocateProjectile(Patch patch, Vector3 point)
        {
            for (int i = 0; i < patch.Vertices.Length; i++) patch.SkinnedVertices[i] = patch.SkinVertex(i, false);
            var wound = new ProjectileWound { Patch = patch, Barycentric = Vector3.right };
            float best = float.PositiveInfinity;
            for (int i = 0; i < patch.Triangles.Length; i += 3)
            {
                int a = patch.Triangles[i], b = patch.Triangles[i + 1], c = patch.Triangles[i + 2];
                Vector3 barycentric = ClosestTriangle(patch.SkinnedVertices[a], patch.SkinnedVertices[b], patch.SkinnedVertices[c], point);
                Vector3 surface = patch.SkinnedVertices[a] * barycentric.x + patch.SkinnedVertices[b] * barycentric.y + patch.SkinnedVertices[c] * barycentric.z;
                float distance = (surface - point).sqrMagnitude;
                if (distance >= best) continue;
                best = distance;
                wound.A = a; wound.B = b; wound.C = c; wound.Barycentric = barycentric;
            }
            wound.Uv = patch.Uv[wound.A] * wound.Barycentric.x + patch.Uv[wound.B] * wound.Barycentric.y + patch.Uv[wound.C] * wound.Barycentric.z;
            Vector2 uvA = patch.Uv[wound.B] - patch.Uv[wound.A], uvB = patch.Uv[wound.C] - patch.Uv[wound.A];
            bool useA = uvA.sqrMagnitude >= uvB.sqrMagnitude;
            float uvLength = Mathf.Max(.0001f, useA ? uvA.magnitude : uvB.magnitude);
            float worldLength = Vector3.Distance(patch.SkinnedVertices[wound.A], patch.SkinnedVertices[useA ? wound.B : wound.C]);
            wound.Radius = Mathf.Clamp(.014f * uvLength / Mathf.Max(.0001f, worldLength), .015f, .18f);
            return wound;
        }

        private static Vector3 ClosestTriangle(Vector3 a, Vector3 b, Vector3 c, Vector3 point)
        {
            Vector3 ab = b - a, ac = c - a, ap = point - a;
            float d1 = Vector3.Dot(ab, ap), d2 = Vector3.Dot(ac, ap);
            if (d1 <= 0f && d2 <= 0f) return Vector3.right;
            Vector3 bp = point - b;
            float d3 = Vector3.Dot(ab, bp), d4 = Vector3.Dot(ac, bp);
            if (d3 >= 0f && d4 <= d3) return Vector3.up;
            float vc = d1 * d4 - d3 * d2;
            if (vc <= 0f && d1 >= 0f && d3 <= 0f) { float v = d1 / (d1 - d3); return new Vector3(1f - v, v, 0f); }
            Vector3 cp = point - c;
            float d5 = Vector3.Dot(ab, cp), d6 = Vector3.Dot(ac, cp);
            if (d6 >= 0f && d5 <= d6) return Vector3.forward;
            float vb = d5 * d2 - d1 * d6;
            if (vb <= 0f && d2 >= 0f && d6 <= 0f) { float w = d2 / (d2 - d6); return new Vector3(1f - w, 0f, w); }
            float va = d3 * d6 - d5 * d4;
            if (va <= 0f && d4 >= d3 && d5 >= d6)
            { float w = (d4 - d3) / (d4 - d3 + d5 - d6); return new Vector3(0f, 1f - w, w); }
            float total = va + vb + vc;
            return Mathf.Abs(total) > 1e-14f ? new Vector3(va, vb, vc) / total : Vector3.right;
        }

        public void RefreshVisibility()
        {
            foreach (Patch patch in patches)
                if (patch.Renderer != null)
                    patch.Renderer.enabled = patch.Active && patch.Source != null && patch.Source.enabled &&
                        patch.Source.gameObject.activeInHierarchy;
            foreach (ProjectileLayer layer in projectileLayers)
                if (layer.Renderer != null)
                    layer.Renderer.enabled = layer.Count > 0 && layer.Patch.Source != null && layer.Patch.Source.enabled &&
                        layer.Patch.Source.gameObject.activeInHierarchy;
        }

        public void Reset()
        {
            foreach (Patch patch in patches)
            {
                patch.Active = false; patch.Grade = 0;
                if (patch.Renderer != null) { patch.Renderer.enabled = false; patch.Renderer.SetPropertyBlock(null); }
            }
            foreach (ProjectileLayer layer in projectileLayers)
            {
                layer.Count = 0; Array.Clear(layer.Holes, 0, layer.Holes.Length);
                if (layer.Renderer != null) { layer.Renderer.enabled = false; layer.Renderer.SetPropertyBlock(null); }
            }
            projectileWounds.Clear();
            Count = 0; last = null; lastProjectile = null;
        }

        public void Dispose()
        {
            foreach (Patch patch in patches)
                if (patch.Renderer != null) UnityEngine.Object.Destroy(patch.Renderer.gameObject);
            foreach (ProjectileLayer layer in projectileLayers)
                if (layer.Renderer != null) UnityEngine.Object.Destroy(layer.Renderer.gameObject);
            projectileLayers.Clear(); projectileWounds.Clear(); patches.Clear(); last = null; lastProjectile = null; Count = 0;
        }

        private static Material RequireProjectileMaterial(Material blood)
        {
            if (sharedProjectileMaterial != null) return sharedProjectileMaterial;
            Shader shader = Resources.Load<Shader>("Shaders/CombatWound");
            if (shader == null) throw new InvalidOperationException("Projectile holes require the shared CombatWound shader.");
            sharedProjectileMaterial = new Material(shader) { name = "Combat Projectile Wounds Shared", hideFlags = HideFlags.HideAndDontSave };
            sharedProjectileMaterial.SetTexture("_BaseMap", blood.GetTexture("_BaseMap"));
            return sharedProjectileMaterial;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetSharedMaterial()
        {
            if (sharedProjectileMaterial != null) UnityEngine.Object.Destroy(sharedProjectileMaterial);
            sharedProjectileMaterial = null;
        }
    }
}

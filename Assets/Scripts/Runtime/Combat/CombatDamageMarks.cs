using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace BarPromenade
{
    /// <summary>Bounded authored overlays retain wounds on the original combat/ragdoll skin.</summary>
    public sealed class CombatDamageMarks
    {
        private sealed class Patch
        {
            public SkinnedMeshRenderer Renderer, Source;
            public Vector3 Vertex, Normal;
            public BoneWeight Weight;
            public Matrix4x4[] BindPoses;
            public Transform[] Bones;
            public bool Active;
            public int Grade;

            public Vector3 Position => Skin(false);
            public Vector3 Direction => Skin(true).normalized;
            private Vector3 Skin(bool normal)
            {
                Vector3 result = Vector3.zero;
                Add(Weight.boneIndex0, Weight.weight0); Add(Weight.boneIndex1, Weight.weight1);
                Add(Weight.boneIndex2, Weight.weight2); Add(Weight.boneIndex3, Weight.weight3);
                return result;
                void Add(int index, float weight)
                {
                    if (weight <= 0f) return;
                    Matrix4x4 matrix = Bones[index].localToWorldMatrix * BindPoses[index];
                    result += (normal ? matrix.MultiplyVector(Normal) : matrix.MultiplyPoint3x4(Vertex)) * weight;
                }
            }
        }

        private readonly List<Patch> patches = new List<Patch>();
        private readonly MaterialPropertyBlock properties = new MaterialPropertyBlock();
        private Patch last;
        public int Count { get; private set; }
        public Vector3 BleedPosition => last != null ? last.Position : Vector3.zero;
        public Vector3 BleedDirection => last != null ? last.Direction : Vector3.up;

        public CombatDamageMarks(CombatActor actor, Material material)
        {
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
                    Normal = mesh.normals[centre], Weight = mesh.boneWeights[centre], BindPoses = mesh.bindposes, Bones = sourceBones });
            }
        }

        public void Add(Vector3 point, Vector3 incoming)
        {
            Patch nearest = null;
            float best = float.PositiveInfinity;
            foreach (Patch patch in patches)
            {
                if (patch.Source == null || !patch.Source.enabled || !patch.Source.gameObject.activeInHierarchy) continue;
                float distance = (patch.Position - point).sqrMagnitude;
                float score = distance + Mathf.Max(0f, Vector3.Dot(patch.Direction, incoming)) * .15f;
                if (patch.Active) score += .045f;
                if (score >= best) continue;
                nearest = patch; best = score;
            }
            if (nearest == null) return;
            if (!nearest.Active) { nearest.Active = true; Count++; }
            nearest.Grade = Mathf.Min(3, nearest.Grade + 1);
            float scale = 1f - (nearest.Grade - 1) * .15f;
            nearest.Renderer.GetPropertyBlock(properties);
            properties.SetVector("_BaseMap_ST", new Vector4(scale, scale, (1f - scale) * .5f, (1f - scale) * .5f));
            nearest.Renderer.SetPropertyBlock(properties); properties.Clear();
            last = nearest;
            RefreshVisibility();
        }

        public void RefreshVisibility()
        {
            foreach (Patch patch in patches)
                if (patch.Renderer != null)
                    patch.Renderer.enabled = patch.Active && patch.Source != null && patch.Source.enabled &&
                        patch.Source.gameObject.activeInHierarchy;
        }

        public void Reset()
        {
            foreach (Patch patch in patches)
            {
                patch.Active = false; patch.Grade = 0;
                if (patch.Renderer != null) { patch.Renderer.enabled = false; patch.Renderer.SetPropertyBlock(null); }
            }
            Count = 0; last = null;
        }

        public void Dispose()
        {
            foreach (Patch patch in patches)
                if (patch.Renderer != null) UnityEngine.Object.Destroy(patch.Renderer.gameObject);
            patches.Clear(); last = null; Count = 0;
        }
    }
}

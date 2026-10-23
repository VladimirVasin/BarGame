using System;
using System.Collections.Generic;
using UnityEngine;

namespace BarPromenade
{
    /// <summary>Stable stature and authored body/garment volume on the shared default rig.</summary>
    [DisallowMultipleComponent]
    public sealed class DefaultNpcBodyShape : MonoBehaviour
    {
        public const string SlimShapeName = "BodyShape.Slim";
        public const string ChubbyShapeName = "BodyShape.Chubby";
        public const int MinimumHeightCm = 160;
        public const int MaximumHeightCm = 185;

        [Serializable]
        private sealed class Binding
        {
            public SkinnedMeshRenderer Renderer;
            public int Slim, Chubby;
            public Bounds Bounds;
        }

        [SerializeField] private Binding[] bindings = Array.Empty<Binding>();
        [SerializeField] private float referenceHeightMetres;
        [SerializeField] private Vector3 referenceScale = Vector3.one;
        [SerializeField] private int heightCm;
        [SerializeField] private float build = .5f;
        [SerializeField] private bool hasSelection;
        [SerializeField] private bool usesAuthoredAnatomy;
        private int selectionVersion;

        public bool IsConfigured => bindings.Length > 0 && referenceHeightMetres > 0f;
        public float ReferenceHeightMetres => referenceHeightMetres;
        public Vector3 ReferenceScale => referenceScale;
        public int HeightCm => heightCm;
        public float Build => build;
        public bool HasSelection => hasSelection;
        public bool UsesAuthoredAnatomy => usesAuthoredAnatomy;
        public int SelectionVersion => selectionVersion;
        public float HeightRatio => hasSelection ? heightCm * .01f / referenceHeightMetres : 1f;

        /// <summary>Authoring only: cache shape indices and conservative bounds without modifying shared meshes.</summary>
        public void Configure(SkinnedMeshRenderer[] renderers, float referenceHeightMetres)
        {
            if (renderers == null) throw new ArgumentNullException(nameof(renderers));
            if (!float.IsFinite(referenceHeightMetres) || referenceHeightMetres <= 0f)
                throw new ArgumentOutOfRangeException(nameof(referenceHeightMetres));
            var authored = new List<Binding>();
            var unique = new HashSet<SkinnedMeshRenderer>();
            foreach (SkinnedMeshRenderer renderer in renderers)
            {
                if (renderer == null || !renderer.transform.IsChildOf(transform) || !unique.Add(renderer))
                    throw new ArgumentException("Body-shape renderers must be unique parts of this actor.", nameof(renderers));
                Mesh mesh = renderer.sharedMesh;
                int slim = CharacterJointDeformation.FindShape(mesh, SlimShapeName);
                int chubby = CharacterJointDeformation.FindShape(mesh, ChubbyShapeName);
                if (slim < 0 && chubby < 0) continue;
                if (slim < 0 || chubby < 0)
                    throw new InvalidOperationException("An affected body/garment requires both authored body shapes: " + renderer.name);
                authored.Add(new Binding { Renderer = renderer, Slim = slim, Chubby = chubby,
                    Bounds = CalculateBounds(renderer, mesh, slim, chubby) });
            }
            if (authored.Count == 0)
                throw new InvalidOperationException("A default NPC requires authored body and garment shapes.");
            bindings = authored.ToArray();
            this.referenceHeightMetres = referenceHeightMetres;
            referenceScale = transform.localScale;
            heightCm = Mathf.RoundToInt(referenceHeightMetres * 100f);
            build = .5f; hasSelection = false; usesAuthoredAnatomy = false;
            RestoreShapes();
        }

        public void ValidateBindings()
        {
            if (!IsConfigured || !Finite(referenceScale) || referenceScale.x <= 0f || referenceScale.y <= 0f || referenceScale.z <= 0f)
                throw new InvalidOperationException("The default NPC has no measured body-shape configuration.");
            foreach (Binding binding in bindings)
            {
                Mesh mesh = binding.Renderer != null ? binding.Renderer.sharedMesh : null;
                if (mesh == null || !binding.Renderer.transform.IsChildOf(transform) ||
                    binding.Slim != CharacterJointDeformation.FindShape(mesh, SlimShapeName) ||
                    binding.Chubby != CharacterJointDeformation.FindShape(mesh, ChubbyShapeName) ||
                    binding.Slim < 0 || binding.Chubby < 0 || !Finite(binding.Bounds.center) || !Finite(binding.Bounds.size))
                    throw new InvalidOperationException("A default NPC body-shape binding no longer matches its mesh.");
            }
        }

        public void Apply(int heightCm, float build)
        {
            if (!IsConfigured) throw new InvalidOperationException("Configure the default NPC body before assigning its proportions.");
            if (heightCm < MinimumHeightCm || heightCm > MaximumHeightCm)
                throw new ArgumentOutOfRangeException(nameof(heightCm));
            if (!float.IsFinite(build) || build < 0f || build > 1f)
                throw new ArgumentOutOfRangeException(nameof(build));
            if (!hasSelection || this.heightCm != heightCm || this.build != build || usesAuthoredAnatomy)
                selectionVersion = unchecked(selectionVersion + 1);
            this.heightCm = heightCm; this.build = build;
            hasSelection = true; usesAuthoredAnatomy = false;
            // Scale from the captured reference, never from a previously selected stature.
            transform.localScale = referenceScale * HeightRatio;
            RestoreShapes();
        }

        /// <summary>Combat derivatives own their authored dimensions after the factory creates their neutral source.</summary>
        public void PreserveAuthoredAnatomy()
        {
            if (!IsConfigured) throw new InvalidOperationException("Configure the default NPC body first.");
            heightCm = Mathf.RoundToInt(referenceHeightMetres * 100f); build = .5f;
            hasSelection = false; usesAuthoredAnatomy = false;
            transform.localScale = referenceScale;
            RestoreShapes();
            usesAuthoredAnatomy = true;
            selectionVersion = unchecked(selectionVersion + 1);
        }

        /// <summary>One-time CPU contact snapshots use the selected shape on their own vertex buffer.</summary>
        public static void ApplyShapeDeltas(SkinnedMeshRenderer renderer, Vector3[] vertices)
        {
            if (renderer == null || vertices == null || renderer.sharedMesh == null || vertices.Length != renderer.sharedMesh.vertexCount)
                throw new ArgumentException("A contact snapshot requires the renderer's complete vertex buffer.");
            Mesh mesh = renderer.sharedMesh;
            Vector3[] delta = null;
            foreach (string name in new[] { SlimShapeName, ChubbyShapeName })
            {
                int shape = CharacterJointDeformation.FindShape(mesh, name);
                if (shape < 0) continue;
                float weight = renderer.GetBlendShapeWeight(shape) * .01f;
                if (Mathf.Abs(weight) < .00001f) continue;
                delta ??= new Vector3[vertices.Length];
                mesh.GetBlendShapeFrameVertices(shape, mesh.GetBlendShapeFrameCount(shape) - 1, delta, null, null);
                for (int i = 0; i < vertices.Length; i++) vertices[i] += delta[i] * weight;
            }
        }

        private void RestoreShapes()
        {
            if (usesAuthoredAnatomy) return;
            float slim = Mathf.Max(0f, 1f - build * 2f) * 100f;
            float chubby = Mathf.Max(0f, build * 2f - 1f) * 100f;
            foreach (Binding binding in bindings)
            {
                if (binding.Renderer == null) continue;
                binding.Renderer.SetBlendShapeWeight(binding.Slim, slim);
                binding.Renderer.SetBlendShapeWeight(binding.Chubby, chubby);
                binding.Renderer.localBounds = binding.Bounds;
            }
        }

        private static Bounds CalculateBounds(SkinnedMeshRenderer renderer, Mesh mesh, int slim, int chubby)
        {
            Bounds bounds = renderer.localBounds;
            var delta = new Vector3[mesh.vertexCount];
            Vector3 expansion = Vector3.zero;
            foreach (int shape in new[] { slim, chubby })
            {
                mesh.GetBlendShapeFrameVertices(shape, mesh.GetBlendShapeFrameCount(shape) - 1, delta, null, null);
                for (int i = 0; i < delta.Length; i++)
                    expansion = Vector3.Max(expansion, new Vector3(Mathf.Abs(delta[i].x), Mathf.Abs(delta[i].y), Mathf.Abs(delta[i].z)));
            }
            // Retain the importer's animated envelope, including joint corrections,
            // then add the largest mutually exclusive girth displacement on each axis.
            bounds.Expand(expansion * 2f);
            return bounds;
        }

        private static bool Finite(Vector3 value) => float.IsFinite(value.x) && float.IsFinite(value.y) && float.IsFinite(value.z);
        // Selection and bounds survive pooling. Height is not reapplied here: an authored owner may have placed/scaled its copy.
        private void OnEnable() => RestoreShapes();
    }
}

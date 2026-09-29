using System;
using System.Collections.Generic;
using UnityEngine;

namespace BarPromenade
{
    /// <summary>Owns a deformable mesh; imported geometry, materials and skin weights stay shared and immutable.</summary>
    internal sealed class PlayerJacketClothSurface : IDisposable
    {
        private readonly Matrix4x4[] bindposes;
        private readonly Matrix4x4[] matrices;
        private readonly Transform[] bones;
        private readonly BoneWeight[] skinWeights;
        private readonly int[] vertexSkins;
        private readonly Matrix4x4[] skins;
        private readonly Matrix4x4[] inverseSkins;
        private readonly bool[] deformSkins;
        private readonly Vector3[] original;
        private readonly Vector3[] output;
        private readonly Vector3[] world;
        private readonly bool normalMapped;
        private readonly PlayerJacketCloth.SurfaceBinding binding;
        public Mesh Mesh { get; }
        public Mesh Source => binding.Source;
        public SkinnedMeshRenderer Renderer => binding.Renderer;
        public int VertexCount => original.Length;
        public Vector3 Original(int index) => original[index];
        public Vector3 World(int index) => world[index];

        public PlayerJacketClothSurface(PlayerJacketCloth.SurfaceBinding source)
        {
            binding = source;
            Material material = source.Renderer.sharedMaterial;
            normalMapped = material != null && material.IsKeywordEnabled("_NORMALMAP");
            original = source.Source.vertices;
            output = (Vector3[])original.Clone();
            world = new Vector3[original.Length];
            // Identical authored weights share exactly the same posed skin matrix.
            // Cache their inverse too: every free vertex used to rebuild and invert it.
            BoneWeight[] weights = source.Source.boneWeights;
            vertexSkins = new int[weights.Length];
            var weightIndices = new Dictionary<BoneWeight, int>(ExactWeights.Instance);
            var uniqueWeights = new List<BoneWeight>();
            for (int i = 0; i < weights.Length; i++)
            {
                if (!weightIndices.TryGetValue(weights[i], out int skin))
                {
                    skin = uniqueWeights.Count;
                    weightIndices.Add(weights[i], skin);
                    uniqueWeights.Add(weights[i]);
                }
                vertexSkins[i] = skin;
            }
            skinWeights = uniqueWeights.ToArray();
            skins = new Matrix4x4[skinWeights.Length];
            inverseSkins = new Matrix4x4[skinWeights.Length];
            deformSkins = new bool[skinWeights.Length];
            for (int i = 0; i < weights.Length; i++)
                if (source.Freedom[i] > 0f) deformSkins[vertexSkins[i]] = true;
            bindposes = source.Source.bindposes;
            bones = source.Renderer.bones;
            matrices = new Matrix4x4[bindposes.Length];
            Mesh = UnityEngine.Object.Instantiate(source.Source);
            Mesh.name = source.Source.name + " Jacket Motion";
            Mesh.hideFlags = HideFlags.HideAndDontSave;
            Mesh.MarkDynamic();
            source.Renderer.sharedMesh = Mesh;
            UpdatePose();
        }

        public void UpdatePose()
        {
            for (int i = 0; i < matrices.Length; i++) matrices[i] = bones[i].localToWorldMatrix * bindposes[i];
            for (int i = 0; i < skins.Length; i++)
            {
                skins[i] = BuildSkin(skinWeights[i]);
                if (deformSkins[i]) inverseSkins[i] = skins[i].inverse;
            }
            for (int i = 0; i < original.Length; i++) world[i] = Skin(i).MultiplyPoint3x4(original[i]);
        }

        public Matrix4x4 Skin(int vertex) => skins[vertexSkins[vertex]];

        private Matrix4x4 BuildSkin(BoneWeight weight)
        {
            Matrix4x4 result = Scale(matrices[weight.boneIndex0], weight.weight0);
            Add(ref result, weight.boneIndex1, weight.weight1);
            Add(ref result, weight.boneIndex2, weight.weight2);
            Add(ref result, weight.boneIndex3, weight.weight3);
            return result;
        }

        private void Add(ref Matrix4x4 result, int bone, float weight)
        {
            if (weight <= 0f) return;
            Matrix4x4 value = matrices[bone];
            for (int i = 0; i < 4; i++) result.SetColumn(i, result.GetColumn(i) + value.GetColumn(i) * weight);
        }

        private static Matrix4x4 Scale(Matrix4x4 value, float weight)
        {
            for (int i = 0; i < 4; i++) value.SetColumn(i, value.GetColumn(i) * weight);
            return value;
        }

        public void Deform(Vector3[] displacements, PlayerScarfBodyContacts contacts)
        {
            for (int i = 0; i < world.Length; i++)
            {
                float freedom = binding.Freedom[i];
                if (freedom <= 0f) continue;
                world[i] += Vector3.Lerp(displacements[binding.FirstNode[i]], displacements[binding.SecondNode[i]],
                    binding.SecondWeight[i]) * freedom;
            }
            contacts.Resolve(world, binding.Freedom);
            for (int i = 0; i < output.Length; i++)
                output[i] = binding.Freedom[i] <= 0f ? original[i] : inverseSkins[vertexSkins[i]].MultiplyPoint3x4(world[i]);
            Write();
        }

        private sealed class ExactWeights : IEqualityComparer<BoneWeight>
        {
            internal static readonly ExactWeights Instance = new ExactWeights();
            public bool Equals(BoneWeight a, BoneWeight b) =>
                a.boneIndex0 == b.boneIndex0 && a.boneIndex1 == b.boneIndex1 &&
                a.boneIndex2 == b.boneIndex2 && a.boneIndex3 == b.boneIndex3 &&
                a.weight0.Equals(b.weight0) && a.weight1.Equals(b.weight1) &&
                a.weight2.Equals(b.weight2) && a.weight3.Equals(b.weight3);

            public int GetHashCode(BoneWeight weight)
            {
                unchecked
                {
                    int hash = weight.boneIndex0;
                    hash = hash * 397 ^ weight.boneIndex1;
                    hash = hash * 397 ^ weight.boneIndex2;
                    hash = hash * 397 ^ weight.boneIndex3;
                    hash = hash * 397 ^ weight.weight0.GetHashCode();
                    hash = hash * 397 ^ weight.weight1.GetHashCode();
                    hash = hash * 397 ^ weight.weight2.GetHashCode();
                    return hash * 397 ^ weight.weight3.GetHashCode();
                }
            }
        }

        public void Restore()
        {
            Array.Copy(original, output, original.Length);
            Write();
        }

        public void CopyTo(PlayerJacketClothSurface target)
        {
            if (target == null || target.output.Length != output.Length) return;
            Array.Copy(output, target.output, output.Length);
            target.Write();
        }

        private void Write()
        {
            Mesh.vertices = output;
            Mesh.RecalculateNormals();
            // Keep the lighting basis attached to the deformed cuff/hem, also
            // when these vertices are copied into the mirror or first person.
            if (normalMapped) Mesh.RecalculateTangents();
            Mesh.RecalculateBounds();
            Renderer.sharedMesh = Mesh;
            // Skinned bounds are renderer-local; retain the authored animated
            // bounds and add the maximum free-cloth excursion on every axis.
            Bounds bounds = binding.AuthoredBounds;
            Vector3 scale = Renderer.transform.lossyScale;
            bounds.Expand(new Vector3(.18f / Mathf.Max(.0001f, Mathf.Abs(scale.x)),
                .18f / Mathf.Max(.0001f, Mathf.Abs(scale.y)), .18f / Mathf.Max(.0001f, Mathf.Abs(scale.z))));
            Renderer.localBounds = bounds;
        }

        public void Dispose()
        {
            if (Renderer != null && Renderer.sharedMesh == Mesh)
            { Renderer.sharedMesh = Source; Renderer.localBounds = binding.AuthoredBounds; }
            PlayerScarfResources.DestroyOwned(Mesh);
        }
    }
}

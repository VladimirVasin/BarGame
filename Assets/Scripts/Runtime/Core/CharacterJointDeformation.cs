using System;
using System.Collections.Generic;
using UnityEngine;

namespace BarPromenade
{
    /// <summary>Authored body volume and garment folds on the existing rig. Bindings are measured during authoring.</summary>
    [DefaultExecutionOrder(407)]
    [DisallowMultipleComponent]
    public sealed class CharacterJointDeformation : MonoBehaviour
    {
        [Serializable]
        private sealed class Binding
        {
            public SkinnedMeshRenderer Renderer;
            public Transform Bone;
            public Quaternion RestRotation;
            public Vector3 LongitudinalAxis;
            public int Shape;
        }

        [SerializeField] private Binding[] bindings = Array.Empty<Binding>();
        public bool HasBindings => bindings.Length > 0;

        /// <summary>Authoring entry point; gameplay uses serialized references, never hierarchy searches.</summary>
        public static void Configure(GameObject root)
        {
            if (root == null) throw new ArgumentNullException(nameof(root));
            var bones = new Dictionary<string, Transform>(StringComparer.Ordinal);
            foreach (Transform bone in root.GetComponentsInChildren<Transform>(true))
                if (!bones.ContainsKey(bone.name)) bones.Add(bone.name, bone);
            var authored = new List<Binding>();
            foreach (SkinnedMeshRenderer renderer in root.GetComponentsInChildren<SkinnedMeshRenderer>(true))
            {
                Mesh mesh = renderer.sharedMesh;
                if (mesh == null) continue;
                for (int shape = 0; shape < mesh.blendShapeCount; shape++)
                {
                    string name = mesh.GetBlendShapeName(shape);
                    int marker = name.IndexOf("JointVolume.", StringComparison.Ordinal);
                    bool trouserFold = false;
                    if (marker < 0 || marker > 0 && name[marker - 1] != '.')
                    {
                        marker = name.IndexOf("TrouserKneeFold.", StringComparison.Ordinal);
                        trouserFold = true;
                    }
                    if (marker < 0 || marker > 0 && name[marker - 1] != '.') continue;
                    name = name.Substring(marker);
                    string[] pieces = name.Split('.');
                    string kind, side;
                    if (trouserFold)
                    {
                        if (pieces.Length != 2 || (pieces[1] != "L" && pieces[1] != "R"))
                            throw new InvalidOperationException("Unsupported authored trouser fold: " + name);
                        kind = "Knee"; side = pieces[1];
                    }
                    else
                    {
                        if (pieces.Length != 3 || (pieces[2] != "L" && pieces[2] != "R") ||
                            (pieces[1] != "Elbow" && pieces[1] != "Knee" && pieces[1] != "Wrist" && pieces[1] != "Hip"))
                            throw new InvalidOperationException("Unsupported authored joint correction: " + name);
                        kind = pieces[1]; side = pieces[2];
                    }
                    string boneName = (kind == "Elbow" ? "forearm." :
                        kind == "Wrist" ? "hand." : kind == "Hip" ? "thigh." : "shin.") + side;
                    if (!bones.TryGetValue(boneName, out Transform bone))
                        throw new InvalidOperationException("Joint correction has no deforming bone: " + boneName);
                    Vector3 axis;
                    if (kind == "Wrist") axis = bone.InverseTransformDirection(bone.position - bone.parent.position);
                    else
                    {
                        string distalName = (kind == "Elbow" ? "hand." : kind == "Hip" ? "shin." : "foot.") + side;
                        if (!bones.TryGetValue(distalName, out Transform distal))
                            throw new InvalidOperationException("Joint correction has no distal anchor: " + distalName);
                        axis = bone.InverseTransformDirection(distal.position - bone.position);
                    }
                    authored.Add(new Binding { Renderer = renderer, Bone = bone,
                        RestRotation = bone.localRotation,
                        LongitudinalAxis = axis.normalized,
                        Shape = shape });
                }
            }
            if (authored.Count == 0) return;
            CharacterJointDeformation controller = root.GetComponent<CharacterJointDeformation>();
            if (controller == null) controller = root.AddComponent<CharacterJointDeformation>();
            controller.bindings = authored.ToArray();
            controller.ApplyPose();
        }

        public void ApplyPose()
        {
            float minimumCosine = Mathf.Cos(135f * Mathf.Deg2Rad * .5f);
            float maximumExpansion = 1f / minimumCosine - 1f;
            foreach (Binding binding in bindings)
            {
                if (binding.Renderer == null || binding.Bone == null) continue;
                Quaternion bend = Quaternion.Inverse(binding.RestRotation) * binding.Bone.localRotation;
                float angle = Vector3.Angle(binding.LongitudinalAxis, bend * binding.LongitudinalAxis);
                float cosine = Mathf.Max(minimumCosine, Mathf.Cos(angle * Mathf.Deg2Rad * .5f));
                float weight = Mathf.Clamp01((1f / cosine - 1f) / maximumExpansion) * 100f;
                binding.Renderer.SetBlendShapeWeight(binding.Shape, weight);
            }
        }

        public static int FindShape(Mesh mesh, string name)
        {
            if (mesh == null || string.IsNullOrEmpty(name)) return -1;
            int exact = mesh.GetBlendShapeIndex(name);
            if (exact >= 0) return exact;
            for (int shape = 0; shape < mesh.blendShapeCount; shape++)
                if (mesh.GetBlendShapeName(shape).EndsWith("." + name, StringComparison.Ordinal)) return shape;
            return -1;
        }

        private void LateUpdate() => ApplyPose();
    }
}

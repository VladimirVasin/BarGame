using System;
using System.Collections.Generic;
using UnityEngine;

namespace BarPromenade
{
    /// <summary>A reversible lower-body costume on the production hero's own bones.</summary>
    [DisallowMultipleComponent]
    public sealed class HomeToiletSeatedAppearance : MonoBehaviour
    {
        public const string ModelResourcePath = "HomeToiletSeated/Models/SeatedLowerBody";
        public const float SeatPelvisHeight = .70483f;
        private static readonly string[] SourceNames =
            { "GEO_Pelvis", "GEO_Thigh.L", "GEO_Thigh.R", "GEO_Shin.L", "GEO_Shin.R" };
        private static HomeToiletSeatedAppearance active;
        private static readonly int BaseMap = Shader.PropertyToID("_BaseMap");
        private static readonly int MainTex = Shader.PropertyToID("_MainTex");
        private static readonly int BaseColor = Shader.PropertyToID("_BaseColor");
        private static readonly int ColorId = Shader.PropertyToID("_Color");
        private static readonly int BaseMapSt = Shader.PropertyToID("_BaseMap_ST");
        private static readonly int MainTexSt = Shader.PropertyToID("_MainTex_ST");
        private readonly List<Snapshot> snapshots = new List<Snapshot>(5);
        private readonly List<ClothPart> cloth = new List<ClothPart>(5);
        private readonly List<Renderer> renderers = new List<Renderer>(10);
        private readonly Dictionary<Renderer, Renderer> sourceByReplacement = new Dictionary<Renderer, Renderer>();
        private HomeInteriorRoot home;
        private Player3DAssetRegistry registry;
        private GameObject module;
        private Transform pelvis;
        private Transform leftKnee;
        private Transform rightKnee;
        private Vector3 outletInPelvis;
        private Quaternion outletRotationInPelvis;

        public Transform Outlet { get; private set; }
        public bool IsActive { get; private set; }
        public bool IsPrepared => module != null && cloth.Count == SourceNames.Length;
        public float TrousersDown { get; private set; }
        public IReadOnlyList<Renderer> Renderers => renderers;

        public void Initialize(HomeInteriorRoot value)
        {
            End();
            ReleaseModule();
            home = value;
        }

        /// <summary>All fallible preparation occurs before clothing or control is captured.</summary>
        public bool Prepare()
        {
            if (IsPrepared) return true;
            if (home == null || !(home.Player.Visual is Player3DCharacterPresentation visual) ||
                visual.Registry == null || visual.Registry.ModelRoot == null) return false;
            registry = visual.Registry;
            GameObject template = Resources.Load<GameObject>(ModelResourcePath);
            Texture2D skinAtlas = Player3DBathingAppearance.BareSkinAtlas;
            if (template == null || skinAtlas == null) return false;
            var sources = new Dictionary<string, Player3DMeshBinding>(StringComparer.Ordinal);
            var actualBones = new Dictionary<string, Transform>(StringComparer.Ordinal);
            // Imported skin palettes include non-deforming ancestors such as
            // root. The mesh bindings alone only describe gameplay anchors.
            foreach (Transform bone in registry.ModelRoot.GetComponentsInChildren<Transform>(true))
                if (!actualBones.ContainsKey(bone.name)) actualBones.Add(bone.name, bone);
            Material skin = null;
            foreach (Player3DMeshBinding binding in registry.MeshBindings)
            {
                if (binding?.Renderer == null || binding.Bone == null) continue;
                sources[binding.MeshName] = binding;
                actualBones[binding.BoneName] = binding.Bone;
                if (binding.Renderer is SkinnedMeshRenderer sourceSkin)
                    foreach (Transform bone in sourceSkin.bones)
                        if (bone != null) actualBones[bone.name] = bone;
                if (binding.PaletteMaterialName == Player3DBathingAppearance.SkinMaterialName)
                    skin = binding.Renderer.sharedMaterial;
            }
            foreach (string name in SourceNames)
                if (!sources.TryGetValue(name, out Player3DMeshBinding binding) ||
                    !(binding.Renderer is SkinnedMeshRenderer)) return false;
            if (skin == null || !actualBones.TryGetValue("pelvis", out pelvis) ||
                !actualBones.TryGetValue("shin.L", out leftKnee) ||
                !actualBones.TryGetValue("shin.R", out rightKnee)) return false;

            try
            {
                module = Instantiate(template, registry.ModelRoot, false);
                module.name = "Home Toilet Seated Lower Body";
                // Match the same production FBX import root normalization.
                module.transform.localPosition = Vector3.zero;
                module.transform.localRotation = Quaternion.identity;
                module.transform.localScale = Vector3.one;
                foreach (Animator importedAnimator in module.GetComponentsInChildren<Animator>(true))
                    importedAnimator.enabled = false;
                var authoredBones = new Dictionary<string, Transform>(StringComparer.Ordinal);
                foreach (Transform child in module.GetComponentsInChildren<Transform>(true))
                    authoredBones[child.name] = child;
                if (!authoredBones.TryGetValue("ToiletOutlet", out Transform outlet) ||
                    !authoredBones.TryGetValue("pelvis", out Transform authoredPelvis))
                    throw new InvalidOperationException("Missing seated toilet outlet or pelvis.");
                Outlet = outlet;
                outletInPelvis = authoredPelvis.InverseTransformPoint(outlet.position);
                outletRotationInPelvis = Quaternion.Inverse(authoredPelvis.rotation) * outlet.rotation;
                foreach (SkinnedMeshRenderer replacement in module.GetComponentsInChildren<SkinnedMeshRenderer>(true))
                {
                    bool isCloth = replacement.name.StartsWith("Trousers_", StringComparison.Ordinal);
                    string prefix = isCloth ? "Trousers_" : "Bare_";
                    if (!replacement.name.StartsWith(prefix, StringComparison.Ordinal) ||
                        !sources.TryGetValue("GEO_" + replacement.name.Substring(prefix.Length), out Player3DMeshBinding source))
                        throw new InvalidOperationException("Unknown seated costume part: " + replacement.name);
                    Transform[] bones = replacement.bones;
                    var block = new MaterialPropertyBlock();
                    source.Renderer.GetPropertyBlock(block);
                    if (isCloth)
                    {
                        int shape = FindLoweredShape(replacement.sharedMesh);
                        if (shape < 0) throw new InvalidOperationException("Missing authored trousers Lowered shape.");
                        var proxyObject = new GameObject("Toilet Fabric " + source.BoneName);
                        proxyObject.transform.SetParent(module.transform, false);
                        Transform target = source.BoneName.EndsWith(".L", StringComparison.Ordinal) ? leftKnee : rightKnee;
                        Transform authoredSource = authoredBones[source.BoneName];
                        Transform authoredTarget = authoredBones[target.name];
                        Quaternion kneeToSource = Quaternion.Inverse(authoredTarget.rotation) * authoredSource.rotation;
                        cloth.Add(new ClothPart(replacement, shape, source.Bone, target,
                            proxyObject.transform, kneeToSource, source.BoneName == "pelvis"));
                        for (int i = 0; i < bones.Length; i++)
                        {
                            if (!actualBones.TryGetValue(bones[i].name, out Transform actual))
                                throw new InvalidOperationException("Unknown production costume bone: " + bones[i].name);
                            bones[i] = bones[i].name == source.BoneName ? proxyObject.transform : actual;
                        }
                        replacement.sharedMaterials = source.Renderer.sharedMaterials;
                    }
                    else
                    {
                        for (int i = 0; i < bones.Length; i++)
                        {
                            if (!actualBones.TryGetValue(bones[i].name, out Transform actual))
                                throw new InvalidOperationException("Unknown production costume bone: " + bones[i].name);
                            bones[i] = actual;
                        }
                        replacement.sharedMaterial = skin;
                        block.SetTexture(BaseMap, skinAtlas);
                        block.SetTexture(MainTex, skinAtlas);
                        block.SetVector(BaseMapSt, new Vector4(1, 1, 0, 0));
                        block.SetVector(MainTexSt, new Vector4(1, 1, 0, 0));
                        block.SetColor(BaseColor, Color.white);
                        block.SetColor(ColorId, Color.white);
                    }
                    replacement.bones = bones;
                    replacement.rootBone = ((SkinnedMeshRenderer)source.Renderer).rootBone;
                    replacement.SetPropertyBlock(block);
                    replacement.shadowCastingMode = source.Renderer.shadowCastingMode;
                    replacement.receiveShadows = source.Renderer.receiveShadows;
                    replacement.renderingLayerMask = source.Renderer.renderingLayerMask;
                    replacement.updateWhenOffscreen = true;
                    Bounds bounds = replacement.localBounds;
                    bounds.Expand(.8f);
                    replacement.localBounds = bounds;
                    renderers.Add(replacement);
                    sourceByReplacement.Add(replacement, source.Renderer);
                }
                if (cloth.Count != 5 || renderers.Count != 10)
                    throw new InvalidOperationException("Incomplete seated lower-body module.");
                module.SetActive(false);
                return true;
            }
            catch (Exception exception)
            {
                Debug.LogWarning("Seated toilet appearance is unavailable: " + exception.Message, this);
                ReleaseModule();
                return false;
            }
        }

        public bool Begin()
        {
            if (IsActive) return true;
            if (active != null || Player3DBathingAppearance.IsActive || !Prepare()) return false;
            // Preparation may precede the walk to the fixture. Borrow the
            // current costume at the actual capture, preserving hidden parts.
            foreach (KeyValuePair<Renderer, Renderer> pair in sourceByReplacement)
            {
                pair.Key.enabled = pair.Value.enabled;
                pair.Key.renderingLayerMask = pair.Value.renderingLayerMask;
                if (!pair.Key.name.StartsWith("Trousers_", StringComparison.Ordinal)) continue;
                var currentBlock = new MaterialPropertyBlock();
                pair.Value.GetPropertyBlock(currentBlock);
                pair.Key.sharedMaterials = pair.Value.sharedMaterials;
                pair.Key.SetPropertyBlock(currentBlock);
            }
            foreach (Player3DMeshBinding binding in registry.MeshBindings)
            {
                if (binding?.Renderer == null || Array.IndexOf(SourceNames, binding.MeshName) < 0) continue;
                snapshots.Add(new Snapshot(binding.Renderer));
                binding.Renderer.enabled = false;
            }
            active = this;
            IsActive = true;
            module.SetActive(true);
            Present(0);
            return true;
        }

        /// <summary>Call after the actor's current authored pose has been applied.</summary>
        public void Present(float trousersDown)
        {
            if (!IsActive) return;
            TrousersDown = Mathf.Clamp01(trousersDown);
            foreach (ClothPart part in cloth)
            {
                Vector3 targetPosition = part.IsPelvis ?
                    (leftKnee.position + rightKnee.position) * .5f - home.transform.up * .02f : part.Target.position;
                Quaternion targetRotation = part.IsPelvis ? part.Source.rotation :
                    part.Target.rotation * part.KneeToSource;
                part.Proxy.SetPositionAndRotation(Vector3.Lerp(part.Source.position, targetPosition, TrousersDown),
                    Quaternion.Slerp(part.Source.rotation, targetRotation, TrousersDown));
                // A production bone inherits the FBX's 100x authoring scale;
                // the costume module's normalized import root does not.
                // Preserve the source bone's full world scale in the proxy.
                Vector3 parentScale = part.Proxy.parent.lossyScale;
                Vector3 sourceScale = part.Source.lossyScale;
                part.Proxy.localScale = new Vector3(sourceScale.x / parentScale.x,
                    sourceScale.y / parentScale.y, sourceScale.z / parentScale.z);
                part.Renderer.SetBlendShapeWeight(part.Shape, TrousersDown * 100f);
            }
            Outlet.SetPositionAndRotation(pelvis.TransformPoint(outletInPelvis),
                pelvis.rotation * outletRotationInPelvis);
        }

        public void End()
        {
            if (module != null) module.SetActive(false);
            foreach (Snapshot snapshot in snapshots) snapshot.Restore();
            snapshots.Clear();
            IsActive = false;
            TrousersDown = 0;
            if (ReferenceEquals(active, this)) active = null;
        }

        private void OnDisable() => End();
        private void OnDestroy() { End(); ReleaseModule(); }
        private void ReleaseModule()
        {
            if (module != null)
            {
                if (Application.isPlaying) Destroy(module); else DestroyImmediate(module);
            }
            module = null; Outlet = null; cloth.Clear(); renderers.Clear(); sourceByReplacement.Clear();
        }

        private static int FindLoweredShape(Mesh mesh)
        {
            for (int i = 0; i < mesh.blendShapeCount; i++)
                if (mesh.GetBlendShapeName(i).EndsWith("Lowered", StringComparison.Ordinal)) return i;
            return -1;
        }

        private readonly struct ClothPart
        {
            public readonly SkinnedMeshRenderer Renderer;
            public readonly int Shape;
            public readonly Transform Source, Target, Proxy;
            public readonly Quaternion KneeToSource;
            public readonly bool IsPelvis;
            public ClothPart(SkinnedMeshRenderer renderer, int shape, Transform source, Transform target,
                Transform proxy, Quaternion kneeToSource, bool isPelvis)
            { Renderer = renderer; Shape = shape; Source = source; Target = target; Proxy = proxy;
                KneeToSource = kneeToSource; IsPelvis = isPelvis; }
        }

        private readonly struct Snapshot
        {
            private readonly Renderer renderer;
            private readonly bool enabled;
            private readonly Material[] materials;
            private readonly MaterialPropertyBlock block;
            public Snapshot(Renderer source)
            {
                renderer = source; enabled = source.enabled; materials = source.sharedMaterials;
                block = new MaterialPropertyBlock(); source.GetPropertyBlock(block);
            }
            public void Restore()
            {
                if (renderer == null) return;
                renderer.sharedMaterials = materials; renderer.SetPropertyBlock(block); renderer.enabled = enabled;
            }
        }
    }
}

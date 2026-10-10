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
        private readonly List<ClothPart> beltCloth = new List<ClothPart>(2);
        private readonly List<Renderer> renderers = new List<Renderer>(10);
        private readonly Dictionary<Renderer, Renderer> sourceByReplacement = new Dictionary<Renderer, Renderer>();
        private readonly Dictionary<Renderer, Renderer> beltSourceByReplacement = new Dictionary<Renderer, Renderer>();
        private readonly List<JointShapeCopy> jointShapes = new List<JointShapeCopy>();
        private CharacterJointDeformation jointDeformation;
        private HomeInteriorRoot home;
        private Player3DAssetRegistry registry;
        private PlayerWardrobe wardrobe;
        private PlayerWardrobe.AppearanceLease wardrobeLease;
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
            jointDeformation = registry.GetComponent<CharacterJointDeformation>();
            wardrobe = registry.GetComponent<PlayerWardrobe>();
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
            var beltSources = new HashSet<Renderer>();
            if (wardrobe != null && wardrobe.IsConfigured)
                foreach (PlayerWardrobe.GarmentBinding garment in wardrobe.Garments)
                    if (garment.Slot == "belt")
                        foreach (Renderer renderer in garment.Renderers) beltSources.Add(renderer);

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
                var pendingBelts = new Dictionary<SkinnedMeshRenderer, Player3DMeshBinding>();
                foreach (SkinnedMeshRenderer replacement in module.GetComponentsInChildren<SkinnedMeshRenderer>(true))
                {
                    bool isCloth = replacement.name.StartsWith("Trousers_", StringComparison.Ordinal);
                    string prefix = isCloth ? "Trousers_" : "Bare_";
                    if (isCloth && sources.TryGetValue("CLO_" + replacement.name.Substring(prefix.Length),
                        out Player3DMeshBinding beltSource) && beltSources.Contains(beltSource.Renderer))
                    {
                        pendingBelts.Add(replacement, beltSource);
                        continue;
                    }
                    if (!replacement.name.StartsWith(prefix, StringComparison.Ordinal) ||
                        !sources.TryGetValue("GEO_" + replacement.name.Substring(prefix.Length), out Player3DMeshBinding source))
                        throw new InvalidOperationException("Unknown seated costume part: " + replacement.name);
                    Transform[] bones = replacement.bones;
                    var block = new MaterialPropertyBlock();
                    source.Renderer.GetPropertyBlock(block);
                    if (isCloth)
                    {
                        Player3DMeshBinding garmentSource = FindTrousersSource(source.BoneName);
                        if (garmentSource != null)
                        {
                            source = garmentSource;
                            block.Clear();
                            source.Renderer.GetPropertyBlock(block);
                        }
                        int shape = FindLoweredShape(replacement.sharedMesh);
                        if (shape < 0) throw new InvalidOperationException("Missing authored trousers Lowered shape.");
                        var proxyObject = new GameObject("Toilet Fabric " + source.BoneName);
                        proxyObject.transform.SetParent(module.transform, false);
                        Transform target = source.BoneName.EndsWith(".L", StringComparison.Ordinal) ? leftKnee : rightKnee;
                        Transform authoredSource = authoredBones[source.BoneName];
                        Transform authoredTarget = authoredBones[target.name];
                        Quaternion kneeToSource = Quaternion.Inverse(authoredTarget.rotation) * authoredSource.rotation;
                        var liveBones = new Transform[bones.Length];
                        var transportedBones = new Transform[bones.Length];
                        for (int i = 0; i < bones.Length; i++)
                        {
                            if (!actualBones.TryGetValue(bones[i].name, out Transform actual))
                                throw new InvalidOperationException("Unknown production costume bone: " + bones[i].name);
                            liveBones[i] = actual;
                            if (actual == source.Bone) transportedBones[i] = proxyObject.transform;
                            else
                            {
                                var influence = new GameObject("Toilet Fabric " + actual.name);
                                influence.transform.SetParent(proxyObject.transform, false);
                                transportedBones[i] = influence.transform;
                            }
                            bones[i] = transportedBones[i];
                        }
                        cloth.Add(new ClothPart(replacement, shape, source.Bone, target,
                            proxyObject.transform, kneeToSource, source.BoneName == "pelvis", liveBones, transportedBones));
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
                        replacement.sharedMaterial = source.Renderer.sharedMaterial;
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
                    var sourceSkin = (SkinnedMeshRenderer)source.Renderer;
                    for (int shape = 0; shape < replacement.sharedMesh.blendShapeCount; shape++)
                    {
                        string shapeName = replacement.sharedMesh.GetBlendShapeName(shape);
                        int marker = shapeName.IndexOf("JointVolume.", StringComparison.Ordinal);
                        if (marker < 0 || marker > 0 && shapeName[marker - 1] != '.')
                            marker = shapeName.IndexOf("TrouserKneeFold.", StringComparison.Ordinal);
                        if (marker < 0 || marker > 0 && shapeName[marker - 1] != '.') continue;
                        shapeName = shapeName.Substring(marker);
                        int sourceShape = CharacterJointDeformation.FindShape(sourceSkin.sharedMesh, shapeName);
                        if (sourceShape < 0)
                            throw new InvalidOperationException("Missing source joint correction: " + shapeName);
                        jointShapes.Add(new JointShapeCopy(sourceSkin, replacement, sourceShape, shape, isCloth));
                    }
                }
                if (cloth.Count != 5 || renderers.Count != 10)
                    throw new InvalidOperationException("Incomplete seated lower-body module.");
                if (pendingBelts.Count != beltSources.Count)
                    throw new InvalidOperationException("The seated module must retain every independently removable belt renderer.");
                ClothPart pelvisCloth = cloth.Find(part => part.IsPelvis);
                foreach (KeyValuePair<SkinnedMeshRenderer, Player3DMeshBinding> pair in pendingBelts)
                {
                    beltCloth.Add(BindBeltReplacement(pair.Key, pair.Value, actualBones, pelvisCloth));
                    beltSourceByReplacement.Add(pair.Key, pair.Value.Renderer);
                }
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
            if (active != null || Player3DBathingAppearance.IsActive || !Prepare() ||
                wardrobe != null && (wardrobe.HasAppearanceLease || wardrobe.IsVisibilityLocked)) return false;
            if (wardrobe != null && wardrobe.IsConfigured) wardrobeLease = wardrobe.CaptureAppearance();
            // Preparation may precede the walk to the fixture. Borrow the
            // current costume at the actual capture, preserving hidden parts.
            foreach (KeyValuePair<Renderer, Renderer> pair in sourceByReplacement)
            {
                bool isCloth = pair.Key.name.StartsWith("Trousers_", StringComparison.Ordinal);
                Renderer currentSource = pair.Value;
                string bodyName = "GEO_" + pair.Key.name.Substring(isCloth ? "Trousers_".Length : "Bare_".Length);
                Player3DMeshBinding bodySource = FindBodySource(bodyName);
                Player3DMeshBinding trousers = bodySource != null ? FindTrousersSource(bodySource.BoneName) : null;
                if (isCloth && trousers != null) currentSource = trousers.Renderer;
                pair.Key.enabled = isCloth ? currentSource.enabled :
                    currentSource.enabled || trousers != null && trousers.Renderer.enabled;
                pair.Key.renderingLayerMask = pair.Value.renderingLayerMask;
                if (!isCloth) continue;
                var currentBlock = new MaterialPropertyBlock();
                currentSource.GetPropertyBlock(currentBlock);
                pair.Key.sharedMaterials = currentSource.sharedMaterials;
                pair.Key.SetPropertyBlock(currentBlock);
            }
            foreach (KeyValuePair<Renderer, Renderer> pair in beltSourceByReplacement)
            {
                // A removed belt remains removed; borrowing trousers must not
                // equip the independent belt slot for this interaction.
                pair.Key.enabled = pair.Value.enabled;
                pair.Key.sharedMaterials = pair.Value.sharedMaterials;
                pair.Key.renderingLayerMask = pair.Value.renderingLayerMask;
                var block = new MaterialPropertyBlock();
                pair.Value.GetPropertyBlock(block);
                pair.Key.SetPropertyBlock(block);
            }
            if (wardrobe != null && wardrobe.IsConfigured)
                foreach (PlayerWardrobe.GarmentBinding garment in wardrobe.Garments)
                    if (garment.Slot == "trousers" || garment.Slot == "belt")
                        foreach (Renderer renderer in garment.Renderers) renderer.enabled = false;
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
            jointDeformation?.ApplyPose();
            foreach (JointShapeCopy shape in jointShapes) shape.Copy(TrousersDown);
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
                TransportInfluences(part);
                part.Renderer.SetBlendShapeWeight(part.Shape, TrousersDown * 100f);
            }
            // Belt and buckle use the same pelvis proxy and authored waist
            // compression as the fabric, retaining their separate visibility.
            foreach (ClothPart part in beltCloth)
            {
                TransportInfluences(part);
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
            wardrobeLease?.Dispose();
            wardrobeLease = null;
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
            beltCloth.Clear(); beltSourceByReplacement.Clear();
            jointShapes.Clear(); jointDeformation = null;
        }

        private static ClothPart BindBeltReplacement(SkinnedMeshRenderer replacement, Player3DMeshBinding source,
            IReadOnlyDictionary<string, Transform> actualBones, ClothPart pelvisCloth)
        {
            if (!(source.Renderer is SkinnedMeshRenderer sourceSkin) || source.Bone != pelvisCloth.Source)
                throw new InvalidOperationException("A seated belt must retain its production pelvis skin binding.");
            int shape = FindLoweredShape(replacement.sharedMesh);
            if (shape < 0) throw new InvalidOperationException("Missing authored belt Lowered shape.");
            Transform[] bones = replacement.bones;
            var liveBones = new Transform[bones.Length];
            var transportedBones = new Transform[bones.Length];
            for (int i = 0; i < bones.Length; i++)
            {
                if (!actualBones.TryGetValue(bones[i].name, out Transform actual))
                    throw new InvalidOperationException("Unknown production belt bone: " + bones[i].name);
                liveBones[i] = actual;
                if (actual == pelvisCloth.Source) transportedBones[i] = pelvisCloth.Proxy;
                else
                {
                    var influence = new GameObject("Toilet Belt " + actual.name);
                    influence.transform.SetParent(pelvisCloth.Proxy, false);
                    transportedBones[i] = influence.transform;
                }
                bones[i] = transportedBones[i];
            }
            replacement.bones = bones;
            replacement.rootBone = sourceSkin.rootBone;
            replacement.sharedMaterials = sourceSkin.sharedMaterials;
            replacement.shadowCastingMode = sourceSkin.shadowCastingMode;
            replacement.receiveShadows = sourceSkin.receiveShadows;
            replacement.renderingLayerMask = sourceSkin.renderingLayerMask;
            replacement.updateWhenOffscreen = true;
            Bounds bounds = replacement.localBounds;
            bounds.Expand(.8f);
            replacement.localBounds = bounds;
            return new ClothPart(replacement, shape, pelvisCloth.Source, pelvisCloth.Target, pelvisCloth.Proxy,
                pelvisCloth.KneeToSource, true, liveBones, transportedBones);
        }

        private static void TransportInfluences(ClothPart part)
        {
            Vector3 sourceScale = part.Source.lossyScale;
            for (int i = 0; i < part.TransportedBones.Length; i++)
            {
                Transform influence = part.TransportedBones[i], live = part.LiveBones[i];
                if (influence == part.Proxy) continue;
                influence.localPosition = part.Source.InverseTransformPoint(live.position);
                influence.localRotation = Quaternion.Inverse(part.Source.rotation) * live.rotation;
                Vector3 scale = live.lossyScale;
                influence.localScale = new Vector3(scale.x / sourceScale.x,
                    scale.y / sourceScale.y, scale.z / sourceScale.z);
            }
        }

        private static int FindLoweredShape(Mesh mesh)
        {
            for (int i = 0; i < mesh.blendShapeCount; i++)
                if (mesh.GetBlendShapeName(i).EndsWith("Lowered", StringComparison.Ordinal)) return i;
            return -1;
        }

        private Player3DMeshBinding FindBodySource(string name)
        {
            foreach (Player3DMeshBinding binding in registry.MeshBindings)
                if (binding != null && binding.MeshName == name) return binding;
            return null;
        }

        private Player3DMeshBinding FindTrousersSource(string boneName)
        {
            if (wardrobe == null || !wardrobe.IsConfigured) return null;
            string selected = wardrobe.GetEquippedItem("trousers");
            foreach (PlayerWardrobe.GarmentBinding garment in wardrobe.Garments)
            {
                if (garment.Slot != "trousers" || selected != null && garment.Id != selected) continue;
                foreach (Player3DMeshBinding binding in registry.MeshBindings)
                {
                    if (binding?.Renderer == null || binding.BoneName != boneName) continue;
                    foreach (Renderer renderer in garment.Renderers)
                        if (binding.Renderer == renderer) return binding;
                }
            }
            return null;
        }

        private readonly struct JointShapeCopy
        {
            private readonly SkinnedMeshRenderer source, target;
            private readonly int sourceShape, targetShape;
            private readonly bool loweredCloth;
            public JointShapeCopy(SkinnedMeshRenderer source, SkinnedMeshRenderer target, int sourceShape, int targetShape, bool loweredCloth)
            { this.source = source; this.target = target; this.sourceShape = sourceShape; this.targetShape = targetShape;
                this.loweredCloth = loweredCloth; }
            public void Copy(float trousersDown) => target.SetBlendShapeWeight(targetShape,
                source.GetBlendShapeWeight(sourceShape) * (loweredCloth ? 1f - trousersDown : 1f));
        }

        private readonly struct ClothPart
        {
            public readonly SkinnedMeshRenderer Renderer;
            public readonly int Shape;
            public readonly Transform Source, Target, Proxy;
            public readonly Quaternion KneeToSource;
            public readonly bool IsPelvis;
            public readonly Transform[] LiveBones, TransportedBones;
            public ClothPart(SkinnedMeshRenderer renderer, int shape, Transform source, Transform target,
                Transform proxy, Quaternion kneeToSource, bool isPelvis, Transform[] liveBones, Transform[] transportedBones)
            { Renderer = renderer; Shape = shape; Source = source; Target = target; Proxy = proxy;
                KneeToSource = kneeToSource; IsPelvis = isPelvis; LiveBones = liveBones; TransportedBones = transportedBones; }
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

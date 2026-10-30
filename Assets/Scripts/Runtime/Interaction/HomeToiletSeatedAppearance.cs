using System;
using System.Collections.Generic;
using UnityEngine;

namespace BarPromenade
{
    /// <summary>A reversible shared lower-body costume bound to the current actor's own bones.</summary>
    [DisallowMultipleComponent]
    public sealed class HomeToiletSeatedAppearance : MonoBehaviour
    {
        public const string ModelResourcePath = "HomeToiletSeated/Models/SeatedLowerBody";
        public const float SeatPelvisHeight = .70483f;
        private static readonly string[] SourceNames =
            { "GEO_Pelvis", "GEO_Thigh.L", "GEO_Thigh.R", "GEO_Shin.L", "GEO_Shin.R" };
        private static HomeToiletSeatedAppearance active;
        private static readonly Dictionary<Mesh, HashSet<int>> WeightedBonePalettes = new Dictionary<Mesh, HashSet<int>>();
        private static readonly int BaseMap = Shader.PropertyToID("_BaseMap");
        private static readonly int MainTex = Shader.PropertyToID("_MainTex");
        private static readonly int BaseColor = Shader.PropertyToID("_BaseColor");
        private static readonly int ColorId = Shader.PropertyToID("_Color");
        private static readonly int BaseMapSt = Shader.PropertyToID("_BaseMap_ST");
        private static readonly int MainTexSt = Shader.PropertyToID("_MainTex_ST");
        private readonly List<Snapshot> snapshots = new List<Snapshot>(5);
        private readonly HashSet<Renderer> capturedRenderers = new HashSet<Renderer>();
        private readonly List<ClothPart> cloth = new List<ClothPart>(5);
        private readonly List<ClothPart> beltCloth = new List<ClothPart>(2);
        private readonly List<Renderer> renderers = new List<Renderer>(10);
        private readonly Dictionary<Renderer, Renderer> sourceByReplacement = new Dictionary<Renderer, Renderer>();
        private readonly Dictionary<Renderer, Renderer> beltSourceByReplacement = new Dictionary<Renderer, Renderer>();
        private readonly List<JointShapeCopy> jointShapes = new List<JointShapeCopy>();
        private readonly List<GarmentLiftPart> outerwearLift = new List<GarmentLiftPart>();
        private Transform outerwearPelvisProxy;
        private CharacterJointDeformation jointDeformation;
        private BathroomInteractionHost home;
        private Player3DAssetRegistry registry;
        private CityPedestrianAssetRegistry npcRegistry;
        private NpcWardrobe npcWardrobe;
        private Transform appearanceFrame, modelRoot;
        private IReadOnlyList<Player3DMeshBinding> meshBindings;
        private readonly Dictionary<Renderer, Vector2> npcPigmentUv = new Dictionary<Renderer, Vector2>();
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
        public float OuterwearLiftWeight => IsActive && outerwearPelvisProxy != null ? TrousersDown : 0f;
        public IReadOnlyList<Renderer> Renderers => renderers;

        public void Initialize(HomeInteriorRoot value) => Initialize(BathroomInteractionHost.FromHome(value));

        public void Initialize(BathroomInteractionHost value)
        {
            End();
            ReleaseModule();
            home = value;
            npcRegistry = null; npcWardrobe = null;
        }

        /// <summary>Uses the same seated mesh, lowered fabric and outlet on an existing default NPC.</summary>
        public void Initialize(CityPedestrianAssetRegistry actor, Transform frame)
        {
            if (actor == null || frame == null) throw new ArgumentNullException(actor == null ? nameof(actor) : nameof(frame));
            if (npcRegistry == actor && appearanceFrame == frame) return;
            End();
            ReleaseModule();
            home = null; registry = null; wardrobe = null;
            npcRegistry = actor; appearanceFrame = frame;
            npcWardrobe = actor.GetComponentInChildren<NpcWardrobe>(true);
        }

        /// <summary>All fallible preparation occurs before clothing or control is captured.</summary>
        public bool Prepare()
        {
            if (IsPrepared) return true;
            if (npcRegistry != null)
            {
                modelRoot = npcRegistry.ModelRoot;
                if (modelRoot == null || npcWardrobe == null) return false;
                meshBindings = BuildNpcBindings();
                if (meshBindings == null) return false;
                jointDeformation = npcRegistry.GetComponentInChildren<CharacterJointDeformation>(true);
            }
            else
            {
                if (home == null || !(home.Player.Visual is Player3DCharacterPresentation visual) ||
                    visual.Registry == null || visual.Registry.ModelRoot == null) return false;
                registry = visual.Registry;
                modelRoot = registry.ModelRoot; appearanceFrame = home.transform;
                meshBindings = registry.MeshBindings;
                jointDeformation = registry.GetComponent<CharacterJointDeformation>();
                wardrobe = registry.GetComponent<PlayerWardrobe>();
            }
            GameObject template = Resources.Load<GameObject>(ModelResourcePath);
            Texture2D skinAtlas = Player3DBathingAppearance.BareSkinAtlas;
            if (registry != null)
                skinAtlas = PlayerCorruptionAppearance.Ensure(registry).BareSkinTexture ?? skinAtlas;
            if (template == null || skinAtlas == null) return false;
            var sources = new Dictionary<string, Player3DMeshBinding>(StringComparer.Ordinal);
            var actualBones = new Dictionary<string, Transform>(StringComparer.Ordinal);
            // Imported skin palettes include non-deforming ancestors such as
            // root. The mesh bindings alone only describe gameplay anchors.
            foreach (Transform bone in modelRoot.GetComponentsInChildren<Transform>(true))
                if (!actualBones.ContainsKey(bone.name)) actualBones.Add(bone.name, bone);
            Material skin = null;
            foreach (Player3DMeshBinding binding in meshBindings)
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
                module = Instantiate(template, modelRoot, false);
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
                    // The default population has no independent hero belt. Its
                    // existing trousers keep their own identity and pigment.
                    if (npcRegistry != null && (replacement.name == "Trousers_Belt" || replacement.name == "Trousers_BeltBuckle"))
                    { replacement.enabled = false; continue; }
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
                            Transform actual = ResolveCostumeBone(replacement.sharedMesh, i, bones[i], actualBones);
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
                            bones[i] = ResolveCostumeBone(replacement.sharedMesh, i, bones[i], actualBones);
                        }
                        replacement.sharedMaterial = source.Renderer.sharedMaterial;
                        if (npcRegistry == null)
                        {
                            block.SetTexture(BaseMap, skinAtlas);
                            block.SetTexture(MainTex, skinAtlas);
                            block.SetVector(BaseMapSt, new Vector4(1, 1, 0, 0));
                            block.SetVector(MainTexSt, new Vector4(1, 1, 0, 0));
                            block.SetColor(BaseColor, Color.white);
                            block.SetColor(ColorId, Color.white);
                        }
                    }
                    if (npcRegistry != null) ApplyNpcPigment(source.Renderer, block);
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
                        if (marker < 0 || marker > 0 && shapeName[marker - 1] != '.')
                            marker = shapeName.IndexOf(PlayerCorruptionAppearance.ShapeName, StringComparison.Ordinal);
                        if (marker < 0 || marker > 0 && shapeName[marker - 1] != '.') continue;
                        shapeName = shapeName.Substring(marker);
                        int sourceShape = CharacterJointDeformation.FindShape(sourceSkin.sharedMesh, shapeName);
                        // Default bodies combine pelvis/thigh surfaces and have
                        // their own authored joint coverage, rather than the
                        // hero's exact separate-renderer shape catalogue.
                        if (sourceShape < 0 && npcRegistry != null) continue;
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
                PrepareOuterwearLift();
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
                if (npcRegistry != null) ApplyNpcPigment(currentSource, currentBlock);
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
            if (npcWardrobe != null)
                foreach (NpcWardrobe.GarmentBinding garment in npcWardrobe.Garments)
                    if (garment.Slot == "trousers") CaptureAndHide(garment.Renderer);
            foreach (Player3DMeshBinding binding in meshBindings)
            {
                if (binding?.Renderer == null || Array.IndexOf(SourceNames, binding.MeshName) < 0) continue;
                CaptureAndHide(binding.Renderer);
            }
            foreach (GarmentLiftPart part in outerwearLift) part.Renderer.bones = part.LiftedBones;
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
            if (outerwearPelvisProxy != null)
            {
                // The long coat's closed hem is below the bare seat surface.
                // Gather only its pelvis-weighted cloth upwards; the spine,
                // sleeves and the visitor's actual skeleton retain their pose.
                outerwearPelvisProxy.SetPositionAndRotation(pelvis.position + appearanceFrame.up * (.28f * TrousersDown), pelvis.rotation);
                Vector3 scale = pelvis.lossyScale, parentScale = outerwearPelvisProxy.parent.lossyScale;
                outerwearPelvisProxy.localScale = new Vector3(scale.x / parentScale.x, scale.y / parentScale.y, scale.z / parentScale.z);
            }
            jointDeformation?.ApplyPose();
            foreach (JointShapeCopy shape in jointShapes) shape.Copy(TrousersDown);
            foreach (ClothPart part in cloth)
            {
                Vector3 targetPosition = part.IsPelvis ?
                    (leftKnee.position + rightKnee.position) * .5f - appearanceFrame.up * .02f : part.Target.position;
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
            foreach (GarmentLiftPart part in outerwearLift)
                if (part.Renderer != null) part.Renderer.bones = part.OriginalBones;
            foreach (Snapshot snapshot in snapshots) snapshot.Restore();
            snapshots.Clear();
            capturedRenderers.Clear();
            wardrobeLease?.Dispose();
            wardrobeLease = null;
            IsActive = false;
            TrousersDown = 0;
            if (ReferenceEquals(active, this)) active = null;
        }

        private void OnDisable() => End();
        private void OnDestroy() { End(); ReleaseModule(); }
        /// <summary>Ends the loan and drops its prepared renderers when a visit leaves the fixture.</summary>
        public void ReleasePrepared() { End(); ReleaseModule(); }
        private void ReleaseModule()
        {
            if (module != null)
            {
                if (Application.isPlaying) Destroy(module); else DestroyImmediate(module);
            }
            module = null; Outlet = null; cloth.Clear(); renderers.Clear(); sourceByReplacement.Clear();
            beltCloth.Clear(); beltSourceByReplacement.Clear();
            jointShapes.Clear(); jointDeformation = null;
            meshBindings = null; npcPigmentUv.Clear();
            outerwearLift.Clear(); outerwearPelvisProxy = null;
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

        private Transform ResolveCostumeBone(Mesh mesh, int index, Transform authored,
            IReadOnlyDictionary<string, Transform> actualBones)
        {
            if (authored != null && actualBones.TryGetValue(authored.name, out Transform actual)) return actual;
            if (npcRegistry != null)
            {
                // FBX keeps the hero's complete palette, including unused hair
                // bones. Admit an absent NPC slot only after proving that no
                // vertex references it; a missing deforming bone still fails.
                HashSet<int> weighted = WeightedBones(mesh);
                if (!weighted.Contains(index)) return pelvis;
            }
            throw new InvalidOperationException("Unknown production costume bone: " +
                (authored != null ? authored.name : "<null>"));
        }

        private static HashSet<int> WeightedBones(Mesh mesh)
        {
            if (WeightedBonePalettes.TryGetValue(mesh, out HashSet<int> weighted)) return weighted;
            weighted = new HashSet<int>();
            using (var weights = mesh.GetAllBoneWeights())
                for (int influence = 0; influence < weights.Length; influence++)
                    if (weights[influence].weight != 0f) weighted.Add(weights[influence].boneIndex);
            WeightedBonePalettes.Add(mesh, weighted);
            return weighted;
        }

        private void PrepareOuterwearLift()
        {
            if (npcWardrobe == null || npcWardrobe.GetEquippedItem("outerwear") != "outerwear.coat") return;
            foreach (NpcWardrobe.ItemBinding item in npcWardrobe.Items)
            {
                if (item.Id != "outerwear.coat") continue;
                foreach (NpcWardrobe.GarmentBinding garment in item.Parts)
                {
                    if (!(garment.Renderer is SkinnedMeshRenderer renderer)) continue;
                    Transform[] original = renderer.bones;
                    int bone = Array.IndexOf(original, pelvis);
                    if (bone < 0 || !WeightedBones(renderer.sharedMesh).Contains(bone)) continue;
                    if (outerwearPelvisProxy == null)
                    {
                        var proxy = new GameObject("Toilet Lifted Coat Pelvis");
                        proxy.transform.SetParent(module.transform, false);
                        outerwearPelvisProxy = proxy.transform;
                    }
                    var lifted = (Transform[])original.Clone();
                    lifted[bone] = outerwearPelvisProxy;
                    outerwearLift.Add(new GarmentLiftPart(renderer, original, lifted));
                }
            }
        }

        private Player3DMeshBinding FindBodySource(string name)
        {
            foreach (Player3DMeshBinding binding in meshBindings)
                if (binding != null && binding.MeshName == name) return binding;
            return null;
        }

        private IReadOnlyList<Player3DMeshBinding> BuildNpcBindings()
        {
            var result = new Player3DMeshBinding[SourceNames.Length];
            for (int index = 0; index < result.Length; index++)
            {
                string boneName = index == 0 ? "pelvis" : index < 3 ? "thigh." + (index == 1 ? "L" : "R")
                    : "shin." + (index == 3 ? "L" : "R");
                string rendererName = index < 3 ? "GEO_LegsUpper" : "GEO_LegsLower." + (index == 3 ? "L" : "R");
                foreach (Renderer candidate in npcWardrobe.BodyRenderers)
                    if (candidate != null && candidate.name == rendererName)
                    { result[index] = NpcBinding(SourceNames[index], boneName, candidate); break; }
                if (result[index]?.Bone == null || !(result[index].Renderer is SkinnedMeshRenderer)) return null;
            }
            return result;
        }

        private Player3DMeshBinding NpcBinding(string name, string boneName, Renderer renderer) =>
            new Player3DMeshBinding(name, "body", boneName, "lower", string.Empty,
                Player3DBathingAppearance.SkinMaterialName, renderer,
                CityPedestrianHandProps.FindSocket(modelRoot, boneName), Color.white);

        private void ApplyNpcPigment(Renderer source, MaterialPropertyBlock block)
        {
            // The NPC's atlas charts differ from the hero module's UV layout.
            // Reuse its own authored pigment chart without inventing a material
            // or changing the persistent skin/outfit selection.
            if (!npcPigmentUv.TryGetValue(source, out Vector2 uv))
            {
                Vector2[] coordinates = ((SkinnedMeshRenderer)source).sharedMesh.uv;
                uv = coordinates.Length > 0 ? coordinates[0] : Vector2.zero;
                npcPigmentUv.Add(source, uv);
            }
            Texture texture = block.GetTexture(BaseMap);
            if (texture == null && source.sharedMaterial != null) texture = source.sharedMaterial.mainTexture;
            block.SetTexture(BaseMap, texture); block.SetTexture(MainTex, texture);
            Vector4 chart = new Vector4(0f, 0f, uv.x, uv.y);
            block.SetVector(BaseMapSt, chart); block.SetVector(MainTexSt, chart);
        }

        private void CaptureAndHide(Renderer source)
        {
            if (source == null || !capturedRenderers.Add(source)) return;
            snapshots.Add(new Snapshot(source));
            source.enabled = false;
        }

        private Player3DMeshBinding FindTrousersSource(string boneName)
        {
            if (npcRegistry != null)
            {
                string npcSelected = npcWardrobe.GetEquippedItem("trousers");
                if (npcSelected == null) return null;
                string part = boneName.StartsWith("shin.", StringComparison.Ordinal) ? "Lower." + boneName[boneName.Length - 1] : "Upper";
                foreach (NpcWardrobe.ItemBinding item in npcWardrobe.Items)
                    if (item.Id == npcSelected)
                        foreach (NpcWardrobe.GarmentBinding garment in item.Parts)
                            if (garment.Renderer != null && garment.Renderer.name.EndsWith(part, StringComparison.Ordinal))
                                return NpcBinding("CLO_" + boneName, boneName, garment.Renderer);
                return null;
            }
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

        private readonly struct GarmentLiftPart
        {
            public readonly SkinnedMeshRenderer Renderer;
            public readonly Transform[] OriginalBones, LiftedBones;
            public GarmentLiftPart(SkinnedMeshRenderer renderer, Transform[] original, Transform[] lifted)
            { Renderer = renderer; OriginalBones = original; LiftedBones = lifted; }
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

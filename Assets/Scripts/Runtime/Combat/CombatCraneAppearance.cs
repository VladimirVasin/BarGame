using System;
using System.Collections.Generic;
using UnityEngine;

namespace BarPromenade
{
    /// <summary>Crane-only authored surfaces on the retained anatomical renderers and skeleton.</summary>
    [DisallowMultipleComponent]
    public sealed class CombatCraneAppearance : MonoBehaviour
    {
        private SkinnedMeshRenderer[] surfaces;
        private Material[][] materials;
        private bool[] visible;
        private MaterialPropertyBlock properties;
        private Renderer[] fittings = Array.Empty<Renderer>();
        private BodyDamageRegion[] fittingRegions = Array.Empty<BodyDamageRegion>();
        private CombatActor actor;

        public void Configure()
        {
            if (surfaces != null) { Restore(); return; }
            var wardrobe = GetComponent<NpcWardrobe>();
            var face = GetComponent<DefaultNpcAppearance>();
            var presentation = GetComponent<VillageResidentPresentation>();
            if (wardrobe == null || !wardrobe.IsModular || face == null || presentation == null || GetComponent<CombatZombieAppearance>() != null)
                throw new InvalidOperationException("The crane requires its retained modular body and its own surface owner.");
            CombatCraneAssetProvider.BodyManifest manifest = CombatCraneAssetProvider.LoadBodyManifest();
            // The ordinary worker's correction component stores mesh-specific
            // shape indices. This independently sculpted anatomy owns its volume.
            CharacterJointDeformation joints = GetComponent<CharacterJointDeformation>();
            if (joints != null) joints.enabled = false;
            Renderer[] originals = GetComponentsInChildren<Renderer>(true);
            var available = new Dictionary<string, SkinnedMeshRenderer>(StringComparer.Ordinal);
            foreach (Renderer renderer in originals)
                if (renderer is SkinnedMeshRenderer skin && !available.TryAdd(skin.name, skin))
                    throw new InvalidOperationException("Repeated original crane renderer: " + skin.name);
            surfaces = new SkinnedMeshRenderer[manifest.meshes.Length];
            materials = new Material[surfaces.Length][];
            visible = new bool[surfaces.Length];
            var anatomy = new List<Renderer>(surfaces.Length);
            for (int i = 0; i < surfaces.Length; i++)
            {
                CombatCraneAssetProvider.BodyMesh part = manifest.meshes[i];
                if (!available.TryGetValue(part.name, out SkinnedMeshRenderer target))
                    throw new InvalidOperationException("The crane lost its original anatomical renderer: " + part.name);
                SkinnedMeshRenderer source = CombatCraneAssetProvider.LoadBodyRenderer(part.name);
                ValidateBodyBinding(target, source);
                // Only the authored shared mesh changes. Existing bone references,
                // grip owners and later damage-source references keep their identity.
                target.sharedMesh = source.sharedMesh;
                target.localBounds = source.localBounds;
                target.updateWhenOffscreen = true;
                surfaces[i] = target; visible[i] = part.visible;
                materials[i] = new Material[part.surfaces.Length];
                for (int slot = 0; slot < materials[i].Length; slot++)
                    materials[i][slot] = CombatCraneAssetProvider.LoadBodyMaterial(part.surfaces[slot]);
                if (part.visible) anatomy.Add(target);
            }
            Texture2D atlas = CombatCraneAssetProvider.LoadBodyAtlas();
            Material material = CombatCraneAssetProvider.LoadBodyMaterial();
            var items = new NpcWardrobe.ItemBinding[wardrobe.Items.Count];
            for (int i = 0; i < items.Length; i++)
            {
                NpcWardrobe.ItemBinding item = wardrobe.Items[i];
                var parts = new NpcWardrobe.GarmentBinding[item.Parts.Count];
                for (int j = 0; j < parts.Length; j++)
                    parts[j] = new NpcWardrobe.GarmentBinding(item.Slot, item.Parts[j].Renderer, Color.white);
                items[i] = new NpcWardrobe.ItemBinding(item.Id, item.Slot, parts, Array.Empty<Renderer>());
            }
            // Retain the shared wardrobe lifecycle with an authored empty outfit.
            // Civilian garments remain available to existing damage references,
            // but no wardrobe restore can make one part of this visual set.
            wardrobe.ConfigureModular(DefaultNpcCatalog.OrdinaryWorker, atlas, material, anatomy.ToArray(), items,
                new[] { new NpcWardrobe.OutfitPreset("crane", Array.Empty<string>()) }, "crane");
            face.Configure(new[] { new DefaultNpcAppearance.FaceBinding("crane", atlas) }, "crane");
            presentation.ConfigureSurfacePalette(atlas, Color.white);
            var selected = new HashSet<Renderer>(anatomy);
            foreach (Renderer renderer in originals) renderer.enabled = selected.Contains(renderer);
            Restore();
            ConfigureHardware(manifest.hardware);
            // Reconcile the retained grip weights on the authored hand meshes
            // without replacing any of their rig or contact anchors.
            GetComponent<NpcHandPose>()?.ResetGrip();
        }

        private void ConfigureHardware(CombatCraneAssetProvider.HardwareMesh[] parts)
        {
            if (parts == null || parts.Length == 0) return;
            fittings = new Renderer[parts.Length]; fittingRegions = new BodyDamageRegion[parts.Length];
            for (int i = 0; i < parts.Length; i++)
            {
                Transform bone = CityPedestrianHandProps.FindSocket(transform, parts[i].bone);
                fittings[i] = CombatCraneAssetProvider.CreateHardware(parts[i], bone);
                bool found = false;
                for (Transform owner = bone; owner != null && !found; owner = owner.parent)
                    for (int region = 0; region < CombatBodyDamageState.RegionCount; region++)
                        if (CombatBodyAnatomy.BoneName((BodyDamageRegion)region) == owner.name)
                        { fittingRegions[i] = (BodyDamageRegion)region; found = true; break; }
                if (!found) throw new InvalidOperationException("Crane fitting has no retained anatomical owner: " + parts[i].bone);
            }
        }

        private void BindActor()
        {
            if (actor != null || fittings.Length == 0) return;
            actor = GetComponentInParent<CombatActor>();
            if (actor == null) return;
            actor.ImpactReceived += OnImpact; actor.DamageReset += OnDamageReset;
            RefreshHardware();
        }

        private void OnImpact(CombatImpact impact) => RefreshHardware();
        private void OnDamageReset(CombatActor owner) => RefreshHardware();
        private void RefreshHardware()
        {
            if (actor == null) return;
            for (int i = 0; i < fittings.Length; i++)
                if (fittings[i] != null) fittings[i].enabled = actor.BodyDamage.IsAttached(fittingRegions[i]);
        }

        private void UnbindActor()
        {
            if (actor == null) return;
            actor.ImpactReceived -= OnImpact; actor.DamageReset -= OnDamageReset; actor = null;
        }

        public static void ValidateBodyBinding(SkinnedMeshRenderer target, SkinnedMeshRenderer source)
        {
            if (target == null || source == null || target.sharedMesh == null || source.sharedMesh == null)
                throw new InvalidOperationException("A crane surface requires both its original and authored skin.");
            Transform[] oldBones = target.bones, newBones = source.bones;
            Matrix4x4[] original = target.sharedMesh.bindposes, replacement = source.sharedMesh.bindposes;
            if (oldBones.Length != newBones.Length || original.Length != replacement.Length || original.Length != oldBones.Length)
                throw new InvalidOperationException("Crane surface changed its retained bone population: " + target.name);
            for (int bone = 0; bone < oldBones.Length; bone++)
            {
                if (oldBones[bone] == null || newBones[bone] == null || oldBones[bone].name != newBones[bone].name)
                    throw new InvalidOperationException("Crane surface changed its retained bone order: " + target.name);
                for (int cell = 0; cell < 16; cell++)
                    if (Mathf.Abs(original[bone][cell] - replacement[bone][cell]) > .0001f)
                        throw new InvalidOperationException("Crane surface changed its retained bind pose: " + target.name);
            }
            // Only the retained hand owner has a shape-index contract. The new
            // body does not inherit human joint-volume or facial shape recipes.
            if (target.name.StartsWith("GEO_Hand", StringComparison.Ordinal))
            {
                int originalGrip = CharacterJointDeformation.FindShape(target.sharedMesh, "CylindricalGrip");
                int replacementGrip = CharacterJointDeformation.FindShape(source.sharedMesh, "CylindricalGrip");
                if (originalGrip < 0 || replacementGrip != originalGrip)
                    throw new InvalidOperationException("Crane hand lost its retained cylindrical grip binding: " + target.name);
            }
        }

        public void Restore()
        {
            if (surfaces == null) return;
            properties ??= new MaterialPropertyBlock();
            Texture2D atlas = CombatCraneAssetProvider.LoadBodyAtlas();
            for (int i = 0; i < surfaces.Length; i++)
            {
                SkinnedMeshRenderer renderer = surfaces[i];
                if (renderer == null) continue;
                if (!visible[i]) renderer.enabled = false;
                renderer.sharedMaterials = materials[i];
                renderer.GetPropertyBlock(properties);
                properties.SetColor("_BaseColor", Color.white); properties.SetColor("_Color", Color.white);
                properties.SetTexture("_BaseMap", atlas); properties.SetTexture("_MainTex", atlas);
                properties.SetVector("_BaseMap_ST", new Vector4(1f, 1f, 0f, 0f));
                properties.SetVector("_MainTex_ST", new Vector4(1f, 1f, 0f, 0f));
                renderer.SetPropertyBlock(properties); properties.Clear();
            }
        }

        private void Start() => BindActor();
        private void OnEnable() { Restore(); BindActor(); }
        private void OnDisable() => UnbindActor();
        private void OnDestroy() => UnbindActor();
    }
}

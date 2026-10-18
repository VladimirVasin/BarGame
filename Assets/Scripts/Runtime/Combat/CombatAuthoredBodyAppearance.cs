using System;
using System.Collections.Generic;
using UnityEngine;

namespace BarPromenade
{
    /// <summary>Shared authored body binding and lifecycle; each enemy retains its own bank and identity.</summary>
    internal sealed class CombatAuthoredBodyAppearance
    {
        private SkinnedMeshRenderer[] surfaces;
        private Material[][] materials;
        private bool[] visible;
        private MaterialPropertyBlock properties;
        private CombatAuthoredBodyBank bank;

        public void Configure(GameObject owner, CombatAuthoredBodyBank sourceBank, string characterId, string outfit)
        {
            if (surfaces != null) { Restore(); return; }
            bank = sourceBank;
            var wardrobe = owner.GetComponent<NpcWardrobe>();
            var face = owner.GetComponent<DefaultNpcAppearance>();
            var presentation = owner.GetComponent<VillageResidentPresentation>();
            if (wardrobe == null || !wardrobe.IsModular || face == null || presentation == null ||
                owner.GetComponent<CombatZombieAppearance>() != null || owner.GetComponent<CombatCraneAppearance>() != null)
                throw new InvalidOperationException("The authored enemy requires its own retained modular body owner.");
            int owners = (owner.GetComponent<CombatRoomAppearance>() != null ? 1 : 0) +
                (owner.GetComponent<CombatImprintAppearance>() != null ? 1 : 0) +
                (owner.GetComponent<CombatLongExposureAppearance>() != null ? 1 : 0);
            if (owners != 1)
                throw new InvalidOperationException("Each authored enemy requires exactly one appearance owner.");
            var manifest = bank.LoadBodyManifest();
            // Its independent sculpt owns joint volume. The human controller's
            // cached shape indices cannot be applied to unrelated body topology.
            CharacterJointDeformation joints = owner.GetComponent<CharacterJointDeformation>();
            if (joints != null) joints.enabled = false;
            Renderer[] originals = owner.GetComponentsInChildren<Renderer>(true);
            var available = new Dictionary<string, SkinnedMeshRenderer>(StringComparer.Ordinal);
            foreach (Renderer renderer in originals)
                if (renderer is SkinnedMeshRenderer skin && !available.TryAdd(skin.name, skin))
                    throw new InvalidOperationException("Repeated original authored renderer: " + skin.name);
            surfaces = new SkinnedMeshRenderer[manifest.meshes.Length];
            materials = new Material[surfaces.Length][]; visible = new bool[surfaces.Length];
            var anatomy = new List<Renderer>(surfaces.Length);
            for (int i = 0; i < surfaces.Length; i++)
            {
                CombatAuthoredBodyBank.BodyMesh part = manifest.meshes[i];
                if (!available.TryGetValue(part.name, out SkinnedMeshRenderer target))
                    throw new InvalidOperationException("Authored body lost its original anatomical renderer: " + part.name);
                SkinnedMeshRenderer source = bank.LoadBodyRenderer(part.name);
                ValidateBodyBinding(target, source);
                target.sharedMesh = source.sharedMesh; target.localBounds = source.localBounds;
                target.updateWhenOffscreen = true;
                surfaces[i] = target; visible[i] = part.visible;
                materials[i] = new Material[part.surfaces.Length];
                for (int slot = 0; slot < materials[i].Length; slot++)
                    materials[i][slot] = bank.LoadBodyMaterial(part.surfaces[slot]);
                if (part.visible) anatomy.Add(target);
            }
            // Keep shared wardrobe/face restoration, with the empty authored
            // outfit. Civilian clothes remain only as hidden damage references.
            var items = new NpcWardrobe.ItemBinding[wardrobe.Items.Count];
            for (int i = 0; i < items.Length; i++)
            {
                NpcWardrobe.ItemBinding item = wardrobe.Items[i];
                var parts = new NpcWardrobe.GarmentBinding[item.Parts.Count];
                for (int j = 0; j < parts.Length; j++)
                    parts[j] = new NpcWardrobe.GarmentBinding(item.Slot, item.Parts[j].Renderer, Color.white);
                items[i] = new NpcWardrobe.ItemBinding(item.Id, item.Slot, parts, Array.Empty<Renderer>());
            }
            Texture2D atlas = bank.LoadBodyAtlas();
            wardrobe.ConfigureModular(DefaultNpcCatalog.OrdinaryWorker, atlas, bank.LoadBodyMaterial(),
                anatomy.ToArray(), items, new[] { new NpcWardrobe.OutfitPreset(outfit, Array.Empty<string>()) }, outfit);
            face.Configure(new[] { new DefaultNpcAppearance.FaceBinding(outfit, atlas) }, outfit);
            face.ApplySelection(characterId, outfit, Array.Empty<string>());
            presentation.ConfigureSurfacePalette(atlas, Color.white);
            var selected = new HashSet<Renderer>(anatomy);
            foreach (Renderer renderer in originals) renderer.enabled = selected.Contains(renderer);
            Restore();
            owner.GetComponent<NpcHandPose>()?.ResetGrip();
        }

        public static void ValidateBodyBinding(SkinnedMeshRenderer target, SkinnedMeshRenderer source)
        {
            if (target == null || source == null || target.sharedMesh == null || source.sharedMesh == null)
                throw new InvalidOperationException("Authored surface requires its original and authored skin.");
            Transform[] oldBones = target.bones, newBones = source.bones;
            Matrix4x4[] original = target.sharedMesh.bindposes, replacement = source.sharedMesh.bindposes;
            if (oldBones.Length != newBones.Length || original.Length != replacement.Length || original.Length != oldBones.Length)
                throw new InvalidOperationException("Authored surface changed its retained bone population: " + target.name);
            for (int bone = 0; bone < oldBones.Length; bone++)
            {
                if (oldBones[bone] == null || newBones[bone] == null || oldBones[bone].name != newBones[bone].name)
                    throw new InvalidOperationException("Authored surface changed its retained bone order: " + target.name);
                for (int cell = 0; cell < 16; cell++)
                    if (Mathf.Abs(original[bone][cell] - replacement[bone][cell]) > .0001f)
                        throw new InvalidOperationException("Authored surface changed its retained bind pose: " + target.name);
            }
            if (target.name.StartsWith("GEO_Hand", StringComparison.Ordinal))
            {
                int grip = CharacterJointDeformation.FindShape(target.sharedMesh, "CylindricalGrip");
                if (grip < 0 || CharacterJointDeformation.FindShape(source.sharedMesh, "CylindricalGrip") != grip)
                    throw new InvalidOperationException("Authored hand lost its retained cylindrical grip binding: " + target.name);
            }
        }

        public void Restore()
        {
            if (surfaces == null) return;
            properties ??= new MaterialPropertyBlock();
            Texture2D atlas = bank.LoadBodyAtlas();
            for (int i = 0; i < surfaces.Length; i++)
            {
                SkinnedMeshRenderer renderer = surfaces[i];
                if (renderer == null) continue;
                // Damage owns enabled for visible surfaces; restoring appearance
                // must never resurrect an original over its active derivative.
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

    }
}

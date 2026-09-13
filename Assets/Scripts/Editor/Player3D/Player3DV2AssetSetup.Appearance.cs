using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace BarPromenade.Editor
{
    public static partial class Player3DV2AssetSetup
    {
        private static void ValidateAppearanceManifest(Player3DV2Manifest manifest)
        {
            if (manifest.body_bone_count != 31 || manifest.hair_bone_count != 12 ||
                manifest.quality == null || manifest.quality.worn_triangle_count <= 0 || manifest.quality.worn_triangle_count > 8000 ||
                manifest.quality.hidden_body_triangle_count <= 0 ||
                manifest.quality.worn_triangle_count + manifest.quality.hidden_body_triangle_count != manifest.triangle_count)
                throw new InvalidOperationException("Hero appearance requires 31 body/12 hair bones and measured worn/hidden geometry within budget.");

            PlayerWardrobeManifest wardrobe = manifest.wardrobe;
            if (wardrobe == null || wardrobe.contract != "hero_outfit_v1" ||
                wardrobe.default_outfit_id != "hero_field_workwear" || wardrobe.items == null || wardrobe.items.Length != 4)
                throw new InvalidOperationException("Hero requires the four independent original wardrobe items.");
            var parts = manifest.parts.ToDictionary(part => part.name, StringComparer.Ordinal);
            var ids = new HashSet<string>(StringComparer.Ordinal);
            var slots = new HashSet<string>(StringComparer.Ordinal);
            var garments = new HashSet<string>(StringComparer.Ordinal);
            foreach (PlayerWardrobeItem item in wardrobe.items)
            {
                if (item == null || string.IsNullOrWhiteSpace(item.id) || !ids.Add(item.id) ||
                    string.IsNullOrWhiteSpace(item.slot) || !slots.Add(item.slot) ||
                    item.renderers == null || item.renderers.Length == 0 || item.covered_body_renderers == null)
                    throw new InvalidOperationException("Hero wardrobe item has invalid identity, slot or renderer bindings.");
                foreach (string name in item.renderers)
                    if (!garments.Add(name) || !parts.TryGetValue(name, out Player3DV2ManifestPart part) || part.role != "clothing")
                        throw new InvalidOperationException($"Garment '{name}' must belong to exactly one wardrobe item.");
                var covered = new HashSet<string>(StringComparer.Ordinal);
                foreach (string name in item.covered_body_renderers)
                    if (!covered.Add(name) || !parts.TryGetValue(name, out Player3DV2ManifestPart part) || part.role != "body_part" ||
                        (part.material != "MAT_Skin" && part.material != "MAT_SkinShadow" && part.material != "MAT_SkinDark"))
                        throw new InvalidOperationException($"Wardrobe coverage '{name}' must name independent body geometry.");
            }
            if (!slots.SetEquals(new[] { "shirt", "jacket", "trousers", "boots" }) ||
                !garments.SetEquals(manifest.parts.Where(part => part.role == "clothing").Select(part => part.name)))
                throw new InvalidOperationException("Every hero garment must be covered by one of the four original wardrobe slots.");

            if (manifest.hair?.contract != "hero_collar_hair_v1" ||
                manifest.hair.chains == null || manifest.hair.chains.Length != 3)
                throw new InvalidOperationException("Hero requires three authored hair chains.");
            var hairBones = new HashSet<string>(StringComparer.Ordinal);
            var hairMeshes = new HashSet<string>(StringComparer.Ordinal);
            foreach (PlayerHairChain chain in manifest.hair.chains)
            {
                if (chain == null || chain.bones == null || chain.bones.Length != 4 ||
                    chain.renderers == null || chain.renderers.Length == 0)
                    throw new InvalidOperationException("Hair chains need three links, a tip and bound hair geometry.");
                for (int i = 0; i < chain.bones.Length; i++)
                    if (chain.bones[i] != chain.name + (i == 3 ? ".Tip" : "." + i.ToString("D2")) || !hairBones.Add(chain.bones[i]))
                        throw new InvalidOperationException("Hair chains contain missing or duplicate authored joints.");
                foreach (string name in chain.renderers)
                    if (!hairMeshes.Add(name) || !parts.TryGetValue(name, out Player3DV2ManifestPart part) ||
                        part.bone != chain.bones[0] || part.role != "hair")
                        throw new InvalidOperationException($"Hair geometry '{name}' must bind to its own authored chain.");
            }
            ValidateJacketClothManifest(manifest, parts);
        }

        private static void ConfigureAppearance(GameObject prefabRoot, Player3DAssetRegistry registry,
            Player3DV2Manifest manifest, IReadOnlyDictionary<string, Renderer> renderers,
            IReadOnlyDictionary<string, Transform> transforms)
        {
            var garments = manifest.wardrobe.items.Select(item => new PlayerWardrobe.GarmentBinding(
                item.id, item.slot, item.renderers.Select(name => renderers[name]).ToArray(),
                item.covered_body_renderers.Select(name => renderers[name]).ToArray())).ToArray();
            Renderer[] body = manifest.parts.Where(part => part.role == "body_part")
                .Select(part => renderers[part.name]).ToArray();
            prefabRoot.AddComponent<PlayerWardrobe>().Configure(manifest.wardrobe.default_outfit_id, body, garments);

            Texture2D skin = AssetDatabase.LoadAssetAtPath<Texture2D>(BareSkinAtlasPath);
            registry.ConfigureBareSkinAtlas(skin, manifest.bare_skin_atlas.regions
                .Select(region => renderers[region.renderer]).ToArray());

            var joints = new List<Transform>(12);
            foreach (PlayerHairChain chain in manifest.hair.chains)
            {
                Transform parent = registry.Anchors.Head;
                foreach (string name in chain.bones)
                {
                    Transform joint = transforms.TryGetValue(name, out Transform found) ? found : null;
                    if (joint == null || joint.parent != parent)
                        throw new InvalidOperationException($"Hero hair joint '{name}' has a missing or incorrect parent.");
                    joints.Add(joint);
                    parent = joint;
                }
            }
            prefabRoot.AddComponent<PlayerHair>().Configure(registry, joints.ToArray());
            ConfigureJacketCloth(prefabRoot, registry, manifest.jacket_cloth, renderers);
            int visibleTriangles = renderers.Values.Where(renderer => renderer.enabled).Sum(RendererTriangles);
            if (visibleTriangles != manifest.quality.worn_triangle_count)
                throw new InvalidOperationException($"Imported dressed hero has {visibleTriangles} triangles, expected {manifest.quality.worn_triangle_count}.");
        }

        private static int RendererTriangles(Renderer renderer)
        {
            Mesh mesh = GetRendererMesh(renderer);
            int count = 0;
            for (int i = 0; i < mesh.subMeshCount; i++) count += (int)mesh.GetIndexCount(i) / 3;
            return count;
        }

        private static void ValidateJacketClothManifest(Player3DV2Manifest manifest,
            IReadOnlyDictionary<string, Player3DV2ManifestPart> parts)
        {
            PlayerJacketClothManifest cloth = manifest.jacket_cloth;
            if (cloth == null || cloth.contract != "hero_jacket_cloth_v1" ||
                cloth.source_space != "blender_z_up_minus_y_forward" || cloth.deformation != "skinned_mesh_bind_delta" ||
                cloth.mask_curve != "smoothstep" || cloth.hem == null || cloth.hem.closed ||
                cloth.hem.nodes_blender == null || cloth.hem.nodes_blender.Length != 8 ||
                cloth.hem.pin_z_m <= cloth.hem.free_z_m || cloth.cuffs == null || cloth.cuffs.Length != 2 ||
                cloth.pinned_renderers == null)
                throw new InvalidOperationException("Jacket cloth requires its authored open hem and two bounded cuff fields.");
            PlayerWardrobeItem jacket = manifest.wardrobe.items.Single(item => item.slot == "jacket");
            if (cloth.wardrobe_item_id != jacket.id)
                throw new InvalidOperationException("Physical jacket must reference the selected wardrobe item.");
            var assigned = new HashSet<string>(StringComparer.Ordinal);
            void Include(string[] names, string bone = null)
            {
                if (names == null || names.Length == 0)
                    throw new InvalidOperationException("Jacket cloth has an empty renderer field.");
                foreach (string name in names)
                    if (!assigned.Add(name) || !parts.TryGetValue(name, out Player3DV2ManifestPart part) ||
                        part.role != "clothing" || bone != null && part.bone != bone)
                        throw new InvalidOperationException("Invalid physical jacket surface: " + name);
            }
            Include(cloth.hem.renderers);
            for (int i = 0; i < cloth.cuffs.Length; i++)
            {
                PlayerJacketCuffManifest cuff = cloth.cuffs[i];
                if (cuff == null || cuff.bone != (i == 0 ? "forearm.L" : "forearm.R") ||
                    cuff.node_count != 4 || cuff.pin_axis_fraction <= 0f || cuff.pin_axis_fraction >= 1f ||
                    cuff.free_tip_offset_m < 0f ||
                    cuff.pin_axis_fraction != cloth.cuffs[0].pin_axis_fraction ||
                    cuff.free_tip_offset_m != cloth.cuffs[0].free_tip_offset_m)
                    throw new InvalidOperationException("Jacket cuffs must retain their original forearm anchors.");
                Include(cuff.renderers, cuff.bone);
            }
            Include(cloth.pinned_renderers);
            if (!assigned.SetEquals(jacket.renderers))
                throw new InvalidOperationException("Every jacket renderer must be moving or explicitly pinned.");
        }

        private static void ConfigureJacketCloth(GameObject root, Player3DAssetRegistry registry,
            PlayerJacketClothManifest cloth, IReadOnlyDictionary<string, Renderer> renderers)
        {
            var bindings = new List<PlayerJacketCloth.SurfaceBinding>();
            void Bind(string[] names, int region)
            {
                foreach (string name in names)
                {
                    if (!(renderers[name] is SkinnedMeshRenderer renderer))
                        throw new InvalidOperationException("Physical jacket surface must retain its source skin: " + name);
                    bindings.Add(new PlayerJacketCloth.SurfaceBinding(renderer, region));
                }
            }
            Bind(cloth.hem.renderers, 0);
            for (int i = 0; i < cloth.cuffs.Length; i++) Bind(cloth.cuffs[i].renderers, i + 1);
            // FBX Y-up conversion followed by the model's authored 180-degree yaw.
            Vector3[] nodes = cloth.hem.nodes_blender.Select(point => new Vector3(-point.x, point.z, -point.y)).ToArray();
            root.AddComponent<PlayerJacketCloth>().Configure(registry, bindings.ToArray(), nodes,
                cloth.hem.pin_z_m, cloth.hem.free_z_m,
                cloth.cuffs[0].pin_axis_fraction, cloth.cuffs[0].free_tip_offset_m);
        }

        [Serializable] private sealed class PlayerWardrobeManifest
        {
            public string contract, default_outfit_id;
            public PlayerWardrobeItem[] items;
        }
        [Serializable] private sealed class PlayerWardrobeItem
        {
            public string id, slot;
            public string[] renderers, covered_body_renderers;
        }
        [Serializable] private sealed class PlayerHairManifest { public string contract; public PlayerHairChain[] chains; }
        [Serializable] private sealed class PlayerHairChain { public string name; public string[] bones, renderers; }
        [Serializable] private sealed class PlayerAppearanceQuality { public int worn_triangle_count, hidden_body_triangle_count; }
        [Serializable] private sealed class PlayerJacketClothManifest
        {
            public string contract, source_space, deformation, mask_curve, wardrobe_item_id;
            public PlayerJacketHemManifest hem;
            public PlayerJacketCuffManifest[] cuffs;
            public string[] pinned_renderers;
        }
        [Serializable] private sealed class PlayerJacketHemManifest
        {
            public string[] renderers;
            public Vector3[] nodes_blender;
            public bool closed;
            public float pin_z_m, free_z_m;
        }
        [Serializable] private sealed class PlayerJacketCuffManifest
        {
            public string bone;
            public string[] renderers;
            public int node_count;
            public float pin_axis_fraction, free_tip_offset_m;
        }
    }
}

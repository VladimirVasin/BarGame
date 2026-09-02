using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace BarPromenade.Tests.EditMode
{
    /// <summary>
    /// Pins the passive prototype catalog boundary. Runtime lot replacement
    /// is intentionally outside this fixture and outside the first asset wave.
    /// </summary>
    public sealed class CityBuildingAssetTests
    {
        private const string ModelPath =
            "Assets/City/Models/CityBuildings3D.fbx";
        private const string ManifestPath =
            "Assets/City/Models/CityBuildings3D.json";
        private const float Tolerance = 0.02f;

        private static readonly ExpectedPrototype[] Expected =
        {
            new ExpectedPrototype(
                "old-town-prototype-01",
                CityDistrictKind.OldTown,
                new Vector3(14f, 42f, 13.5f),
                "Assets/Resources/City/Buildings/" +
                "OldTownPrototype01.prefab"),
            new ExpectedPrototype(
                "residential-prototype-01",
                CityDistrictKind.Residential,
                new Vector3(11.5f, 40f, 11.5f),
                "Assets/Resources/City/Buildings/" +
                "ResidentialPrototype01.prefab"),
            new ExpectedPrototype(
                "industrial-prototype-01",
                CityDistrictKind.Industrial,
                new Vector3(14f, 36f, 13.5f),
                "Assets/Resources/City/Buildings/" +
                "IndustrialPrototype01.prefab"),
            new ExpectedPrototype(
                "nightlife-prototype-01",
                CityDistrictKind.Nightlife,
                new Vector3(12.5f, 48f, 12f),
                "Assets/Resources/City/Buildings/" +
                "NightlifePrototype01.prefab")
        };

        [Test]
        public void Manifest_DeclaresFourStableGroundedPrototypeAssemblies()
        {
            ContractManifest manifest = LoadManifest();

            Assert.That(
                manifest.design_id,
                Is.EqualTo(CityBuildingAssetProvider.ExpectedDesignId));
            Assert.That(manifest.generator_version, Is.EqualTo("2.1.0"));
            Assert.That(manifest.fbx_asset_path, Is.EqualTo(ModelPath));
            Assert.That(manifest.unit_factor, Is.EqualTo(1f));
            Assert.That(manifest.unity_axes, Is.Not.Null);
            Assert.That(manifest.unity_axes.right, Is.EqualTo("+X"));
            Assert.That(manifest.unity_axes.forward, Is.EqualTo("+Z"));
            Assert.That(manifest.unity_axes.up, Is.EqualTo("+Y"));
            Assert.That(
                manifest.unity_axes.fbx_axis_forward,
                Is.EqualTo("-Z"));
            Assert.That(manifest.unity_axes.fbx_axis_up, Is.EqualTo("+Y"));
            Assert.That(
                manifest.unity_axes.bake_space_transform,
                Is.False);
            Assert.That(
                manifest.prototype_count,
                Is.EqualTo(
                    CityBuildingAssetProvider.ExpectedPrototypeCount));
            Assert.That(
                manifest.mesh_count,
                Is.EqualTo(
                    CityBuildingAssetProvider.ExpectedPrototypeCount *
                    CityBuildingAssetRegistry.ExpectedRoleCount));
            Assert.That(manifest.prototypes, Has.Length.EqualTo(4));
            Assert.That(IsSha256(manifest.build_signature), Is.True);
            Assert.That(manifest.root_contract, Is.Not.Null);
            Assert.That(
                manifest.root_contract.catalog_root,
                Is.EqualTo("ROOT_CityBuildings3D"));
            Assert.That(
                manifest.root_contract.origin,
                Is.EqualTo("footprint_center_ground"));
            Assert.That(
                manifest.root_contract.scale_mode,
                Is.EqualTo("fixed_meters"));
            Assert.That(
                manifest.root_contract.source_forward_axis,
                Is.EqualTo("+Y"));
            Assert.That(
                manifest.root_contract.unity_forward_axis,
                Is.EqualTo("+Z"));
            Assert.That(manifest.passive, Is.Not.Null);
            Assert.That(manifest.passive.colliders, Is.False);
            Assert.That(manifest.passive.lights, Is.False);
            Assert.That(manifest.passive.cameras, Is.False);
            Assert.That(manifest.passive.materials, Is.False);
            Assert.That(manifest.passive.animation_count, Is.Zero);
            Assert.That(manifest.uv2_encoding, Is.Not.Null);
            Assert.That(manifest.uv2_encoding.channel_index, Is.EqualTo(1));
            Assert.That(
                manifest.uv2_encoding.scheme,
                Is.EqualTo("u_centered_uint8"));
            Assert.That(
                manifest.uv2_encoding.divisor,
                Is.EqualTo(
                    CityBuildingAssetRegistry.WindowSlotUv2Divisor));
            Assert.That(
                manifest.uv2_encoding.zero_means,
                Is.EqualTo("non_window_geometry"));
            Assert.That(manifest.uv0_encoding, Is.Not.Null);
            Assert.That(
                manifest.uv0_encoding.window_glass_scheme,
                Is.EqualTo("per_window_face_projected_0_1"));
            Assert.That(
                manifest.uv0_encoding.building_side_atlas_scheme,
                Is.EqualTo("building_side_atlas_0_1"));
            Assert.That(
                manifest.uv0_encoding.full_face_surface_scheme,
                Is.EqualTo("full_face_projected_0_1"));
            Assert.That(
                manifest.uv0_encoding.metric_surface_scheme,
                Is.EqualTo("world_metre_projected"));

            int triangleTotal = 0;
            for (int index = 0; index < Expected.Length; index++)
            {
                ExpectedPrototype expected = Expected[index];
                ContractPrototype prototype = manifest.prototypes[index];
                Assert.That(prototype.stable_id, Is.EqualTo(expected.StableId));
                Assert.That(
                    prototype.district,
                    Is.EqualTo(expected.District.ToString()));
                Assert.That(prototype.grammar, Is.Not.Empty);
                Assert.That(
                    prototype.root_name,
                    Is.EqualTo("ROOT_" + expected.StableId));
                Assert.That(
                    prototype.triangle_count,
                    Is.InRange(
                        1,
                        CityBuildingAssetRegistry.MaximumTriangleCount));
                Assert.That(
                    prototype.parts,
                    Has.Length.EqualTo(
                        CityBuildingAssetRegistry.ExpectedRoleCount));
                Assert.That(
                    prototype.frontage_width_m,
                    Is.EqualTo(expected.UnityEnvelope.x).Within(.003f));
                Assert.That(
                    prototype.height_m,
                    Is.EqualTo(expected.UnityEnvelope.y).Within(.003f));
                Assert.That(
                    prototype.depth_m,
                    Is.EqualTo(expected.UnityEnvelope.z).Within(.003f));

                Bounds source = BoundsFromArrays(
                    prototype.bounds_min_source,
                    prototype.bounds_max_source);
                Bounds unity = BoundsFromArrays(
                    prototype.bounds_min_unity,
                    prototype.bounds_max_unity);
                Assert.That(source.min.z, Is.EqualTo(0f).Within(.003f));
                Assert.That(unity.min.y, Is.EqualTo(0f).Within(.003f));
                Assert.That(
                    unity.size.x,
                    Is.InRange(
                        expected.UnityEnvelope.x - .02f,
                        expected.UnityEnvelope.x + .16f));
                Assert.That(
                    unity.size.y,
                    Is.EqualTo(expected.UnityEnvelope.y).Within(.02f));
                Assert.That(
                    unity.size.z,
                    Is.InRange(
                        expected.UnityEnvelope.z - .02f,
                        expected.UnityEnvelope.z + .16f));
                AssertBoundsNear(
                    unity,
                    ConvertSourceBoundsToUnity(
                        prototype.bounds_min_source,
                        prototype.bounds_max_source));
                AssertFrontAnchor(prototype, unity);
                AssertAttachmentMetadata(prototype);
                AssertStableParts(prototype);

                Assert.That(
                    prototype.parts.Sum(part => part.triangles),
                    Is.EqualTo(prototype.triangle_count));
                triangleTotal += prototype.triangle_count;
            }

            Assert.That(triangleTotal, Is.EqualTo(manifest.triangle_count));
        }

        [Test]
        public void ImportedFbx_IsPassiveUnreadableFixedMetreGeometry()
        {
            ContractManifest manifest = LoadManifest();
            ModelImporter importer =
                AssetImporter.GetAtPath(ModelPath) as ModelImporter;
            Assert.That(importer, Is.Not.Null);
            Assert.That(importer.globalScale, Is.EqualTo(1f));
            Assert.That(importer.useFileScale, Is.True);
            Assert.That(importer.bakeAxisConversion, Is.True);
            Assert.That(importer.preserveHierarchy, Is.True);
            Assert.That(importer.optimizeGameObjects, Is.False);
            Assert.That(
                importer.animationType,
                Is.EqualTo(ModelImporterAnimationType.None));
            Assert.That(importer.importAnimation, Is.False);
            Assert.That(importer.importCameras, Is.False);
            Assert.That(importer.importLights, Is.False);
            Assert.That(importer.importBlendShapes, Is.False);
            Assert.That(importer.addCollider, Is.False);
            Assert.That(importer.isReadable, Is.False);
            Assert.That(
                importer.materialImportMode,
                Is.EqualTo(ModelImporterMaterialImportMode.None));

            UnityEngine.Object[] assets =
                AssetDatabase.LoadAllAssetsAtPath(ModelPath);
            Mesh[] meshes = assets.OfType<Mesh>().ToArray();
            Assert.That(
                meshes,
                Has.Length.EqualTo(
                    CityBuildingAssetProvider.ExpectedPrototypeCount *
                    CityBuildingAssetRegistry.ExpectedRoleCount));
            Assert.That(meshes.All(mesh => !mesh.isReadable), Is.True);
            Assert.That(meshes.All(mesh => mesh.vertexCount > 0), Is.True);
            Assert.That(assets.OfType<Material>(), Is.Empty);
            Assert.That(assets.OfType<AnimationClip>(), Is.Empty);

            GameObject model =
                AssetDatabase.LoadAssetAtPath<GameObject>(ModelPath);
            Assert.That(model, Is.Not.Null);
            AssertPassive(model);
            Transform catalog = FindUnique(
                model.transform,
                "ROOT_CityBuildings3D");
            Assert.That(catalog.childCount, Is.EqualTo(4));

            for (int index = 0; index < Expected.Length; index++)
            {
                ContractPrototype prototype = manifest.prototypes[index];
                Transform root = FindDirect(catalog, prototype.root_name);
                Assert.That(
                    root.childCount,
                    Is.EqualTo(
                        CityBuildingAssetRegistry.ExpectedRoleCount));
                foreach (ContractPart part in prototype.parts)
                {
                    Transform child = FindDirect(root, part.object_name);
                    Assert.That(child.childCount, Is.Zero);
                    MeshFilter filter = child.GetComponent<MeshFilter>();
                    Assert.That(filter, Is.Not.Null);
                    Assert.That(filter.sharedMesh, Is.Not.Null);
                    Assert.That(child.GetComponent<MeshRenderer>(), Is.Not.Null);
                    Assert.That(
                        CountTriangles(filter.sharedMesh),
                        Is.EqualTo(part.triangles));
                }
            }
        }

        [Test]
        public void Provider_BindsFourPassiveWrappersAndCurrentSignature()
        {
            ContractManifest manifest = LoadManifest();
            CityBuildingAssetProvider provider =
                CityBuildingAssetProvider.Load();
            Assert.That(provider, Is.Not.Null);
            Assert.That(provider.HasCompletePrefabs, Is.True);
            Assert.That(provider.DesignId, Is.EqualTo(manifest.design_id));
            Assert.That(
                provider.BuildSignature,
                Is.EqualTo(manifest.build_signature));
            Assert.That(provider.Entries.Count, Is.EqualTo(4));
            Assert.DoesNotThrow(provider.ValidateOrThrow);

            GameObject sourceModel =
                AssetDatabase.LoadAssetAtPath<GameObject>(ModelPath);
            Transform sourceCatalog = FindUnique(
                sourceModel.transform,
                "ROOT_CityBuildings3D");

            for (int index = 0; index < Expected.Length; index++)
            {
                ExpectedPrototype expected = Expected[index];
                ContractPrototype prototype = manifest.prototypes[index];
                CityBuildingPrefabEntry entry = provider.Entries[index];
                Assert.That(entry.StableId, Is.EqualTo(expected.StableId));
                Assert.That(entry.District, Is.EqualTo(expected.District));
                Assert.That(entry.Prefab, Is.Not.Null);
                Assert.That(
                    AssetDatabase.GetAssetPath(entry.Prefab),
                    Is.EqualTo(expected.PrefabPath));
                Assert.That(
                    provider.GetPrefabOrThrow(expected.District),
                    Is.SameAs(entry.Prefab));

                CityBuildingAssetRegistry registry =
                    entry.Prefab.GetComponent<CityBuildingAssetRegistry>();
                Assert.That(registry, Is.Not.Null);
                Assert.That(registry.StableId, Is.EqualTo(expected.StableId));
                Assert.That(registry.District, Is.EqualTo(expected.District));
                Assert.That(registry.Grammar, Is.EqualTo(prototype.grammar));
                Assert.That(registry.UnitFactor, Is.EqualTo(1f));
                Assert.That(
                    registry.SourceTriangleCount,
                    Is.EqualTo(prototype.triangle_count));
                Assert.That(
                    registry.BuildSignature,
                    Is.EqualTo(manifest.build_signature));
                Assert.That(
                    registry.Parts.Count,
                    Is.EqualTo(
                        CityBuildingAssetRegistry.ExpectedRoleCount));
                Assert.That(
                    registry.FacadeAttachments.Count,
                    Is.EqualTo(prototype.facade_attachment_bounds.Length));
                Assert.That(
                    registry.WindowSlots.Count,
                    Is.EqualTo(prototype.window_slots.Length));
                Assert.That(
                    registry.BalconySlots.Count,
                    Is.EqualTo(prototype.balcony_slots.Length));
                for (int slotIndex = 0;
                     slotIndex < prototype.window_slots.Length;
                     slotIndex++)
                {
                    Assert.That(
                        registry.WindowSlots[slotIndex].OpeningKind.ToString(),
                        Is.EqualTo(
                            prototype.window_slots[slotIndex].opening_kind));
                }
                for (int slotIndex = 0;
                     slotIndex < prototype.balcony_slots.Length;
                     slotIndex++)
                {
                    ContractBalconySlot sourceSlot =
                        prototype.balcony_slots[slotIndex];
                    CityBuildingBalconySlot runtimeSlot =
                        registry.BalconySlots[slotIndex];
                    Assert.That(
                        runtimeSlot.StableId,
                        Is.EqualTo(sourceSlot.stable_id));
                    Assert.That(
                        runtimeSlot.DoorSlotId,
                        Is.EqualTo(sourceSlot.door_slot_id));
                    AssertBoundsNear(
                        runtimeSlot.LocalDeckBounds,
                        ConvertSourceBoundsToUnity(
                            sourceSlot.deck_bounds_min_source,
                            sourceSlot.deck_bounds_max_source));
                    AssertVectorNear(
                        runtimeSlot.LocalNpcDock,
                        ConvertSourceVectorToUnity(
                            sourceSlot.npc_dock_source));
                    AssertVectorNear(
                        runtimeSlot.LocalOutward,
                        ConvertSourceVectorToUnity(
                            sourceSlot.outward_source));
                }
                AssertVectorNear(
                    registry.LocalBounds.size,
                    BoundsFromArrays(
                        prototype.bounds_min_unity,
                        prototype.bounds_max_unity).size);
                Assert.That(
                    registry.LocalBounds.min.y,
                    Is.EqualTo(0f).Within(.003f));

                Vector3 frontPosition = entry.Prefab.transform
                    .InverseTransformPoint(registry.FrontAnchor.position);
                Vector3 frontForward = entry.Prefab.transform
                    .InverseTransformDirection(registry.FrontAnchor.forward);
                AssertVectorNear(
                    frontPosition,
                    Vector3FromArray(
                        prototype.front_anchor.position_unity));
                AssertVectorNear(frontForward, Vector3.forward);
                Assert.That(
                    frontPosition.z,
                    Is.EqualTo(prototype.depth_m * .5f).Within(.003f));
                Assert.That(
                    registry.LocalBounds.max.z - frontPosition.z,
                    Is.LessThanOrEqualTo(.08f));

                Transform sourceRoot = FindDirect(
                    sourceCatalog,
                    prototype.root_name);
                AssertMatrixNear(
                    registry.ModelRoot.localToWorldMatrix,
                    sourceRoot.localToWorldMatrix);
                AssertPassive(entry.Prefab);
                Assert.DoesNotThrow(registry.ValidateOrThrow);

                for (int roleIndex = 0;
                     roleIndex < CityBuildingAssetRegistry.ExpectedRoleCount;
                     roleIndex++)
                {
                    CityBuildingMeshRole role =
                        CityBuildingAssetRegistry.GetExpectedRole(roleIndex);
                    CityBuildingPartBinding binding =
                        registry.Parts[roleIndex];
                    Assert.That(binding.Role, Is.EqualTo(role));
                    Assert.That(
                        binding.SurfaceKind,
                        Is.EqualTo(role.ToString()));
                    Assert.That(binding.UvScheme, Is.Not.Empty);
                    Assert.That(
                        binding.SourceName,
                        Is.EqualTo(expected.StableId + "__" + role));
                    Assert.That(binding.Renderer, Is.Not.Null);
                    MeshFilter filter =
                        binding.Renderer.GetComponent<MeshFilter>();
                    Assert.That(filter, Is.Not.Null);
                    Assert.That(
                        AssetDatabase.GetAssetPath(filter.sharedMesh),
                        Is.EqualTo(ModelPath));
                }
            }
        }

        private static void AssertStableParts(ContractPrototype prototype)
        {
            var expectedRoles = new HashSet<string>(
                Enumerable.Range(
                    0,
                    CityBuildingAssetRegistry.ExpectedRoleCount).Select(index =>
                    CityBuildingAssetRegistry.GetExpectedRole(index)
                        .ToString()),
                StringComparer.Ordinal);
            Assert.That(
                prototype.parts.Select(part => part.role),
                Is.EquivalentTo(expectedRoles));
            Assert.That(
                prototype.parts.Select(part => part.object_name),
                Is.EquivalentTo(expectedRoles.Select(role =>
                    prototype.stable_id + "__" + role)));
            foreach (ContractPart part in prototype.parts)
            {
                Assert.That(part.surface_kind, Is.EqualTo(part.role));
                bool sideAtlas =
                    part.role == CityBuildingMeshRole.FacadePrimary.ToString() ||
                    part.role == CityBuildingMeshRole.FacadeSecondary.ToString();
                bool window =
                    part.role == CityBuildingMeshRole.WindowGlass.ToString();
                bool fullFace =
                    part.role == CityBuildingMeshRole.Plinth.ToString();
                Assert.That(
                    part.uv_scheme,
                    Is.EqualTo(
                        sideAtlas
                            ? "building_side_atlas_0_1"
                            : fullFace
                                ? "full_face_projected_0_1"
                            : window
                                ? "per_window_face_projected_0_1"
                                : "world_metre_projected"));
                if (sideAtlas || fullFace || window)
                {
                    Assert.That(part.meters_per_tile, Is.EqualTo(0f));
                }
                else
                {
                    Assert.That(part.meters_per_tile, Is.GreaterThan(0f));
                }
            }

            var windowUv2 = new HashSet<int>(
                prototype.window_slots.Select(slot => slot.uv2_slot_id));
            var partUv2 = new HashSet<int>(
                prototype.parts
                    .SelectMany(part => part.uv2_slot_ids)
                    .Where(slotId => slotId > 0));
            Assert.That(partUv2, Is.EquivalentTo(windowUv2));
        }

        private static void AssertFrontAnchor(
            ContractPrototype prototype,
            Bounds unityBounds)
        {
            Assert.That(prototype.front_anchor, Is.Not.Null);
            Vector3 sourcePosition = Vector3FromArray(
                prototype.front_anchor.position_source);
            Vector3 unityPosition = Vector3FromArray(
                prototype.front_anchor.position_unity);
            Vector3 sourceForward = Vector3FromArray(
                prototype.front_anchor.forward_source);
            Vector3 unityForward = Vector3FromArray(
                prototype.front_anchor.forward_unity);
            AssertVectorNear(
                unityPosition,
                ConvertSourceVectorToUnity(
                    prototype.front_anchor.position_source));
            AssertVectorNear(
                unityForward,
                ConvertSourceVectorToUnity(
                    prototype.front_anchor.forward_source));
            AssertVectorNear(sourceForward, Vector3.up);
            AssertVectorNear(unityForward, Vector3.forward);
            Assert.That(sourcePosition.z, Is.EqualTo(0f).Within(.003f));
            Assert.That(unityPosition.y, Is.EqualTo(0f).Within(.003f));
            Assert.That(
                unityPosition.z,
                Is.EqualTo(prototype.depth_m * .5f).Within(.003f));
            Assert.That(
                unityBounds.max.z - unityPosition.z,
                Is.LessThanOrEqualTo(.08f));
        }

        private static void AssertAttachmentMetadata(
            ContractPrototype prototype)
        {
            Bounds roof = BoundsFromArrays(
                prototype.roof_attachment_bounds_min_source,
                prototype.roof_attachment_bounds_max_source);
            Assert.That(roof.size.x, Is.GreaterThan(0f));
            Assert.That(roof.size.y, Is.GreaterThan(0f));
            Assert.That(
                prototype.facade_attachment_bounds,
                Is.Not.Empty);
            Assert.That(prototype.window_slots, Is.Not.Empty);
            Assert.That(prototype.balcony_slots, Is.Not.Null);
            Assert.That(
                prototype.facade_attachment_bounds
                    .Select(attachment => attachment.side)
                    .Distinct(StringComparer.Ordinal)
                    .Count(),
                Is.EqualTo(prototype.facade_attachment_bounds.Length));
            Assert.That(
                prototype.window_slots
                    .Select(slot => slot.slot_id)
                    .Distinct()
                    .Count(),
                Is.EqualTo(prototype.window_slots.Length));

            var slotsById = prototype.window_slots.ToDictionary(
                slot => slot.slot_id);
            int[] declaredDoors = prototype.window_slots
                .Where(slot => string.Equals(
                    slot.opening_kind,
                    CityBuildingOpeningKind.BalconyDoor.ToString(),
                    StringComparison.Ordinal))
                .Select(slot => slot.slot_id)
                .ToArray();
            Assert.That(
                prototype.window_slots.All(slot =>
                    string.Equals(
                        slot.opening_kind,
                        CityBuildingOpeningKind.Window.ToString(),
                        StringComparison.Ordinal) ||
                    string.Equals(
                        slot.opening_kind,
                        CityBuildingOpeningKind.BalconyDoor.ToString(),
                        StringComparison.Ordinal)),
                Is.True);
            Assert.That(
                prototype.balcony_slots.Select(slot => slot.stable_id),
                Is.Unique);
            Assert.That(
                prototype.balcony_slots.Select(slot => slot.door_slot_id),
                Is.EquivalentTo(declaredDoors));

            foreach (ContractBalconySlot balcony in prototype.balcony_slots)
            {
                Assert.That(
                    slotsById.TryGetValue(
                        balcony.door_slot_id,
                        out ContractWindowSlot door),
                    Is.True);
                Assert.That(
                    door.opening_kind,
                    Is.EqualTo(
                        CityBuildingOpeningKind.BalconyDoor.ToString()));
                Assert.That(door.side, Is.EqualTo(balcony.side));
                Assert.That(door.floor, Is.EqualTo(balcony.floor));
                Bounds deck = BoundsFromArrays(
                    balcony.deck_bounds_min_source,
                    balcony.deck_bounds_max_source);
                Vector3 dock = Vector3FromArray(balcony.npc_dock_source);
                Vector3 doorCenter = Vector3FromArray(door.center_source);
                Assert.That(deck.Contains(dock), Is.True);
                Assert.That(dock.z, Is.EqualTo(deck.max.z).Within(.0001f));
                Assert.That(
                    doorCenter.z - door.size_m[1] * .5f,
                    Is.EqualTo(deck.max.z).Within(.0001f));
                AssertVectorNear(
                    Vector3FromArray(balcony.outward_source),
                    Vector3.up);
            }

            bool residential = string.Equals(
                prototype.district,
                CityDistrictKind.Residential.ToString(),
                StringComparison.Ordinal);
            Assert.That(
                prototype.balcony_slots.Length,
                Is.EqualTo(residential ? 8 : 0));
            if (residential)
            {
                float[] levels = { 7f, 12f, 17f, 22f };
                for (int floor = 1; floor <= levels.Length; floor++)
                {
                    ContractBalconySlot[] floorSlots =
                        prototype.balcony_slots
                            .Where(slot => slot.floor == floor)
                            .ToArray();
                    Assert.That(floorSlots, Has.Length.EqualTo(2));
                    foreach (ContractBalconySlot balcony in floorSlots)
                    {
                        Bounds deck = BoundsFromArrays(
                            balcony.deck_bounds_min_source,
                            balcony.deck_bounds_max_source);
                        Assert.That(balcony.side, Is.EqualTo("Front"));
                        Assert.That(
                            deck.max.z,
                            Is.EqualTo(levels[floor - 1]).Within(.0001f));
                        Assert.That(
                            deck.size.x,
                            Is.EqualTo(2.5f).Within(.0001f));
                        Assert.That(
                            deck.size.y,
                            Is.EqualTo(1.2f).Within(.0001f));
                    }
                }
            }
            Assert.That(
                prototype.window_slots
                    .Select(slot => slot.uv2_slot_id)
                    .Distinct()
                    .Count(),
                Is.EqualTo(prototype.window_slots.Length));
        }

        private static ContractManifest LoadManifest()
        {
            TextAsset source =
                AssetDatabase.LoadAssetAtPath<TextAsset>(ManifestPath);
            Assert.That(
                source,
                Is.Not.Null,
                "Run tools/build-city-buildings-3d-model.py first.");
            ContractManifest manifest =
                JsonUtility.FromJson<ContractManifest>(source.text);
            Assert.That(manifest, Is.Not.Null);
            return manifest;
        }

        private static void AssertPassive(GameObject root)
        {
            Assert.That(root.GetComponentsInChildren<Collider>(true), Is.Empty);
            Assert.That(
                root.GetComponentsInChildren<Rigidbody>(true),
                Is.Empty);
            Assert.That(root.GetComponentsInChildren<Light>(true), Is.Empty);
            Assert.That(root.GetComponentsInChildren<Camera>(true), Is.Empty);
            Assert.That(root.GetComponentsInChildren<Animator>(true), Is.Empty);
            Assert.That(
                root.GetComponentsInChildren<Animation>(true),
                Is.Empty);
            Assert.That(
                root.GetComponentsInChildren<AudioSource>(true),
                Is.Empty);
            Assert.That(
                root.GetComponentsInChildren<ParticleSystem>(true),
                Is.Empty);
            Assert.That(
                root.GetComponentsInChildren<SkinnedMeshRenderer>(true),
                Is.Empty);
        }

        private static Transform FindUnique(Transform root, string name)
        {
            Transform[] matches = root
                .GetComponentsInChildren<Transform>(true)
                .Where(candidate => string.Equals(
                    candidate.name,
                    name,
                    StringComparison.Ordinal))
                .ToArray();
            Assert.That(matches, Has.Length.EqualTo(1), name);
            return matches[0];
        }

        private static Transform FindDirect(Transform root, string name)
        {
            Transform[] matches = Enumerable.Range(0, root.childCount)
                .Select(root.GetChild)
                .Where(candidate => string.Equals(
                    candidate.name,
                    name,
                    StringComparison.Ordinal))
                .ToArray();
            Assert.That(matches, Has.Length.EqualTo(1), name);
            return matches[0];
        }

        private static int CountTriangles(Mesh mesh)
        {
            long indices = 0;
            for (int index = 0; index < mesh.subMeshCount; index++)
            {
                indices += mesh.GetIndexCount(index);
            }

            Assert.That(indices % 3L, Is.Zero, mesh.name);
            return (int)(indices / 3L);
        }

        private static Bounds ConvertSourceBoundsToUnity(
            float[] minimum,
            float[] maximum)
        {
            Vector3 sourceMinimum = Vector3FromArray(minimum);
            Vector3 sourceMaximum = Vector3FromArray(maximum);
            Vector3 unityMinimum = new Vector3(
                sourceMinimum.x,
                sourceMinimum.z,
                sourceMinimum.y);
            Vector3 unityMaximum = new Vector3(
                sourceMaximum.x,
                sourceMaximum.z,
                sourceMaximum.y);
            return new Bounds(
                (unityMinimum + unityMaximum) * .5f,
                unityMaximum - unityMinimum);
        }

        private static Vector3 ConvertSourceVectorToUnity(float[] value)
        {
            Vector3 source = Vector3FromArray(value);
            return new Vector3(source.x, source.z, source.y);
        }

        private static Bounds BoundsFromArrays(
            float[] minimum,
            float[] maximum)
        {
            Vector3 min = Vector3FromArray(minimum);
            Vector3 max = Vector3FromArray(maximum);
            return new Bounds((min + max) * .5f, max - min);
        }

        private static Vector3 Vector3FromArray(float[] value)
        {
            Assert.That(value, Has.Length.EqualTo(3));
            return new Vector3(value[0], value[1], value[2]);
        }

        private static void AssertBoundsNear(Bounds actual, Bounds expected)
        {
            AssertVectorNear(actual.min, expected.min);
            AssertVectorNear(actual.max, expected.max);
        }

        private static void AssertVectorNear(
            Vector3 actual,
            Vector3 expected)
        {
            Assert.That(
                Vector3.Distance(actual, expected),
                Is.LessThanOrEqualTo(Tolerance),
                $"Actual {actual}, expected {expected}.");
        }

        private static void AssertMatrixNear(
            Matrix4x4 actual,
            Matrix4x4 expected)
        {
            for (int index = 0; index < 16; index++)
            {
                Assert.That(
                    actual[index],
                    Is.EqualTo(expected[index]).Within(.003f),
                    "matrix index " + index);
            }
        }

        private static bool IsSha256(string value)
        {
            return !string.IsNullOrWhiteSpace(value) &&
                value.Length == 64 &&
                value.All(character =>
                    character >= '0' && character <= '9' ||
                    character >= 'a' && character <= 'f');
        }

        private readonly struct ExpectedPrototype
        {
            public ExpectedPrototype(
                string stableId,
                CityDistrictKind district,
                Vector3 unityEnvelope,
                string prefabPath)
            {
                StableId = stableId;
                District = district;
                UnityEnvelope = unityEnvelope;
                PrefabPath = prefabPath;
            }

            public string StableId { get; }
            public CityDistrictKind District { get; }
            public Vector3 UnityEnvelope { get; }
            public string PrefabPath { get; }
        }

        [Serializable]
        private sealed class ContractManifest
        {
            public string generator_version;
            public string design_id;
            public string fbx_asset_path;
            public float unit_factor;
            public ContractUnityAxes unity_axes;
            public ContractRoot root_contract;
            public ContractPassive passive;
            public ContractUv0Encoding uv0_encoding;
            public ContractUv2Encoding uv2_encoding;
            public int prototype_count;
            public int mesh_count;
            public int triangle_count;
            public ContractPrototype[] prototypes;
            public string build_signature;
        }

        [Serializable]
        private sealed class ContractUnityAxes
        {
            public string right;
            public string forward;
            public string up;
            public string fbx_axis_forward;
            public string fbx_axis_up;
            public bool bake_space_transform;
        }

        [Serializable]
        private sealed class ContractRoot
        {
            public string catalog_root;
            public string origin;
            public string scale_mode;
            public string source_forward_axis;
            public string unity_forward_axis;
        }

        [Serializable]
        private sealed class ContractPassive
        {
            public bool colliders;
            public bool lights;
            public bool cameras;
            public bool materials;
            public int animation_count;
        }

        [Serializable]
        private sealed class ContractUv2Encoding
        {
            public int channel_index;
            public string scheme;
            public float divisor;
            public string zero_means;
        }

        [Serializable]
        private sealed class ContractUv0Encoding
        {
            public string window_glass_scheme;
            public string building_side_atlas_scheme;
            public string full_face_surface_scheme;
            public string metric_surface_scheme;
        }

        [Serializable]
        private sealed class ContractPrototype
        {
            public string stable_id;
            public string district;
            public string grammar;
            public string root_name;
            public float frontage_width_m;
            public float depth_m;
            public float height_m;
            public int triangle_count;
            public float[] bounds_min_source;
            public float[] bounds_max_source;
            public float[] bounds_min_unity;
            public float[] bounds_max_unity;
            public ContractFrontAnchor front_anchor;
            public float[] roof_attachment_bounds_min_source;
            public float[] roof_attachment_bounds_max_source;
            public ContractFacadeAttachment[] facade_attachment_bounds;
            public ContractWindowSlot[] window_slots;
            public ContractBalconySlot[] balcony_slots;
            public ContractPart[] parts;
        }

        [Serializable]
        private sealed class ContractFrontAnchor
        {
            public float[] position_source;
            public float[] forward_source;
            public float[] position_unity;
            public float[] forward_unity;
        }

        [Serializable]
        private sealed class ContractFacadeAttachment
        {
            public string side;
        }

        [Serializable]
        private sealed class ContractWindowSlot
        {
            public int slot_id;
            public string side;
            public int floor;
            public int bay;
            public string opening_kind;
            public float[] center_source;
            public float[] size_m;
            public int uv2_slot_id;
        }

        [Serializable]
        private sealed class ContractBalconySlot
        {
            public string stable_id;
            public int floor;
            public string side;
            public int door_slot_id;
            public float[] deck_bounds_min_source;
            public float[] deck_bounds_max_source;
            public float[] npc_dock_source;
            public float[] outward_source;
        }

        [Serializable]
        private sealed class ContractPart
        {
            public string object_name;
            public string role;
            public string surface_kind;
            public string uv_scheme;
            public float meters_per_tile;
            public int triangles;
            public int[] uv2_slot_ids;
        }
    }
}

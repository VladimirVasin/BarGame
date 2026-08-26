using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using BarPromenade;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using Object = UnityEngine.Object;

namespace BarPromenade.Editor
{
    [InitializeOnLoad]
    public static class ChurchAssetSetup
    {
        public const string ExteriorModelPath =
            "Assets/Church/Models/ChurchExterior3D.fbx";
        public const string InteriorModelPath =
            "Assets/Church/Models/ChurchInterior3D.fbx";
        public const string ManifestPath =
            "Assets/Church/Models/Church3D.json";
        public const string ExteriorPrefabPath =
            "Assets/Resources/Church/ChurchExterior3D.prefab";
        public const string InteriorPrefabPath =
            "Assets/Resources/Church/ChurchInterior3D.prefab";
        public const string MaterialFolder =
            "Assets/Church/Materials";
        public const string TextureFolder =
            "Assets/Church/Textures";
        public const string SharedLitMaterialPath =
            "Assets/Resources/Materials/RuntimePrimitiveLit.mat";
        public const string SharedEmissionMaterialPath =
            "Assets/Resources/Materials/CityNoirEmission.mat";

        private const string ExpectedDesignId =
            "provincial_catholic_gothic_basilica_v1";
        private const float ExpectedWidth = 23f;
        private const float ExpectedLength = 44f;
        private const float ExpectedHeight = 32f;
        private const float ExpectedDoorWidth = 2.8f;
        private const float ExpectedDoorHeight = 4.2f;
        private const int ExteriorMaximumTriangles = 12000;
        private const int ExteriorMaximumRenderers = 18;
        private const int InteriorMaximumTriangles = 22000;
        private const int InteriorMaximumRenderers = 24;
        private const int RequiredInteriorLayoutContractCount = 12;

        private static readonly Dictionary<
            string,
            ChurchInteriorFixtureKind> InteriorLayoutContractKinds =
                new Dictionary<string, ChurchInteriorFixtureKind>(
                    StringComparer.Ordinal)
                {
                    { "nave_piers", ChurchInteriorFixtureKind.Pier },
                    { "pew_halves", ChurchInteriorFixtureKind.Pew },
                    { "communion_rail", ChurchInteriorFixtureKind.AltarRail },
                    { "altar_table", ChurchInteriorFixtureKind.AltarTable },
                    { "high_altar", ChurchInteriorFixtureKind.HighAltar },
                    { "crucifix", ChurchInteriorFixtureKind.Crucifix },
                    { "confessionals", ChurchInteriorFixtureKind.Confessional },
                    { "votive_stands", ChurchInteriorFixtureKind.VotiveCandleStand },
                    { "baptismal_font", ChurchInteriorFixtureKind.BaptismalFont },
                    { "choir_loft", ChurchInteriorFixtureKind.ChoirLoft },
                    { "choir_loft_supports", ChurchInteriorFixtureKind.ChoirLoftSupport },
                    { "pipe_organ", ChurchInteriorFixtureKind.Organ }
                };

        private static readonly string[] TextureFileNames =
        {
            "ChurchPlasterAlbedo.png",
            "ChurchStoneAlbedo.png",
            "ChurchWoodAlbedo.png",
            "ChurchMetalAlbedo.png",
            "ChurchFloorAlbedo.png",
            "ChurchTextileAlbedo.png",
            "ChurchSacredArtAtlasAlbedo.png",
            "ChurchMuralAtlasAlbedo.png",
            "ChurchGlassAtlasAlbedo.png"
        };

        private static readonly string[] AtlasTextureFileNames =
        {
            "ChurchSacredArtAtlasAlbedo.png",
            "ChurchMuralAtlasAlbedo.png",
            "ChurchGlassAtlasAlbedo.png"
        };

        private static bool isBuilding;
        private static bool buildQueued;

        public static bool IsBuilding => isBuilding;

        static ChurchAssetSetup()
        {
            if (!Application.isBatchMode)
            {
                EditorApplication.delayCall += ValidateDependencyStamp;
            }
        }

        [MenuItem("Bar Promenade/Church 3D/Build Runtime Prefabs")]
        public static void Run()
        {
            BuildOrThrow();
            Debug.Log(
                $"Church prefabs rebuilt at '{ExteriorPrefabPath}' and " +
                $"'{InteriorPrefabPath}'.");
        }

        public static void RunBatch()
        {
            BuildOrThrow();
            Debug.Log("CHURCH UNITY ASSET BUILD OK");
        }

        [MenuItem("Bar Promenade/Church 3D/Validate Imported Contract")]
        public static void RunValidation()
        {
            ValidateOrThrow();
            Debug.Log("Church imported assets and prefab contracts are valid.");
        }

        public static bool IsModelPath(string path)
        {
            return string.Equals(
                    path,
                    ExteriorModelPath,
                    StringComparison.OrdinalIgnoreCase) ||
                string.Equals(
                    path,
                    InteriorModelPath,
                    StringComparison.OrdinalIgnoreCase);
        }

        public static bool IsTexturePath(string path)
        {
            if (string.IsNullOrWhiteSpace(path) ||
                !path.StartsWith(
                    TextureFolder + "/",
                    StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            return TextureFileNames.Any(
                name => string.Equals(
                    path,
                    $"{TextureFolder}/{name}",
                    StringComparison.OrdinalIgnoreCase));
        }

        public static bool IsAtlasTexturePath(string path)
        {
            return IsTexturePath(path) &&
                AtlasTextureFileNames.Any(
                    name => string.Equals(
                        path,
                        $"{TextureFolder}/{name}",
                        StringComparison.OrdinalIgnoreCase));
        }

        public static bool SourcesExist()
        {
            return File.Exists(ExteriorModelPath) &&
                File.Exists(InteriorModelPath) &&
                File.Exists(ManifestPath) &&
                File.Exists(SharedLitMaterialPath) &&
                File.Exists(SharedEmissionMaterialPath) &&
                TextureFileNames.All(
                    name => File.Exists($"{TextureFolder}/{name}"));
        }

        public static void QueueBuildWhenSourcesExist()
        {
            if (isBuilding || buildQueued || !SourcesExist())
            {
                return;
            }

            buildQueued = true;
            EditorApplication.delayCall += RunQueuedBuild;
        }

        public static void BuildOrThrow()
        {
            if (isBuilding)
            {
                return;
            }

            if (!SourcesExist())
            {
                throw new InvalidOperationException(
                    "Church build requires both FBX files, the manifest, " +
                    "all generated textures and the two shared materials.");
            }

            isBuilding = true;
            try
            {
                ImportSources();
                ChurchManifest manifest = LoadAndValidateManifest();
                Dictionary<ChurchMaterialSlot, Material> materials =
                    BuildMaterials();
                BuildPrefab(
                    ExteriorModelPath,
                    ExteriorPrefabPath,
                    ChurchAssetKind.Exterior,
                    RequireAsset(manifest, "Exterior"),
                    manifest,
                    materials);
                BuildPrefab(
                    InteriorModelPath,
                    InteriorPrefabPath,
                    ChurchAssetKind.Interior,
                    RequireAsset(manifest, "Interior"),
                    manifest,
                    materials);
                AssetDatabase.SaveAssets();
                ValidateOrThrow();
            }
            finally
            {
                isBuilding = false;
            }
        }

        public static void ValidateOrThrow()
        {
            ChurchManifest manifest = LoadAndValidateManifest();
            ValidateTextureImportContracts();
            ValidatePrefab(
                ExteriorPrefabPath,
                ChurchAssetKind.Exterior,
                RequireAsset(manifest, "Exterior"),
                manifest);
            ValidatePrefab(
                InteriorPrefabPath,
                ChurchAssetKind.Interior,
                RequireAsset(manifest, "Interior"),
                manifest);
        }

        private static void ValidateTextureImportContracts()
        {
            foreach (string name in TextureFileNames)
            {
                string path = $"{TextureFolder}/{name}";
                TextureImporter importer =
                    AssetImporter.GetAtPath(path) as TextureImporter;
                bool isAtlas = IsAtlasTexturePath(path);
                TextureWrapMode expectedWrap = isAtlas
                    ? TextureWrapMode.Clamp
                    : TextureWrapMode.Repeat;
                if (importer == null ||
                    importer.wrapMode != expectedWrap ||
                    importer.mipmapEnabled == isAtlas)
                {
                    throw new InvalidOperationException(
                        $"Church texture importer contract drifted for '{path}'.");
                }
            }
        }

        private static void ImportSources()
        {
            EnsureFolderForAsset(ExteriorPrefabPath);
            EnsureFolderForAsset(InteriorPrefabPath);
            EnsureFolderForAsset($"{MaterialFolder}/placeholder.mat");
            foreach (string path in TextureFileNames.Select(
                         name => $"{TextureFolder}/{name}"))
            {
                AssetDatabase.ImportAsset(
                    path,
                    ImportAssetOptions.ForceUpdate |
                    ImportAssetOptions.ForceSynchronousImport);
            }

            foreach (string path in new[]
                     {
                         ExteriorModelPath,
                         InteriorModelPath,
                         ManifestPath
                     })
            {
                AssetDatabase.ImportAsset(
                    path,
                    ImportAssetOptions.ForceUpdate |
                    ImportAssetOptions.ForceSynchronousImport);
            }
        }

        private static Dictionary<ChurchMaterialSlot, Material>
            BuildMaterials()
        {
            Material sharedLit =
                AssetDatabase.LoadAssetAtPath<Material>(
                    SharedLitMaterialPath);
            Material sharedEmission =
                AssetDatabase.LoadAssetAtPath<Material>(
                    SharedEmissionMaterialPath);
            if (sharedLit == null || sharedEmission == null)
            {
                throw new InvalidOperationException(
                    "Church shared material dependencies failed to load.");
            }

            var result =
                new Dictionary<ChurchMaterialSlot, Material>();
            foreach (ChurchMaterialSlot slot in
                     Enum.GetValues(typeof(ChurchMaterialSlot)))
            {
                bool emissive = IsEmissive(slot);
                Material source = emissive ? sharedEmission : sharedLit;
                string path = MaterialPath(slot);
                Material material =
                    AssetDatabase.LoadAssetAtPath<Material>(path);
                if (material == null)
                {
                    material = new Material(source)
                    {
                        name = $"Church{slot}"
                    };
                    AssetDatabase.CreateAsset(material, path);
                }
                else
                {
                    material.shader = source.shader;
                    material.CopyPropertiesFromMaterial(source);
                    material.name = $"Church{slot}";
                }

                material.enableInstancing = true;
                Texture2D texture = LoadTexture(slot);
                SetTextureIfPresent(material, "_BaseMap", texture);
                SetTextureIfPresent(material, "_MainTex", texture);
                Color color = MaterialColor(slot);
                SetColorIfPresent(material, "_BaseColor", color);
                SetColorIfPresent(material, "_Color", color);
                if (!emissive)
                {
                    SetFloatIfPresent(
                        material,
                        "_Metallic",
                        slot == ChurchMaterialSlot.Gold ? .55f :
                        slot == ChurchMaterialSlot.Roof ||
                        slot == ChurchMaterialSlot.Iron ? .28f : .02f);
                    SetFloatIfPresent(
                        material,
                        "_Smoothness",
                        slot == ChurchMaterialSlot.Gold ? .62f : .32f);
                }

                EditorUtility.SetDirty(material);
                result.Add(slot, material);
            }

            return result;
        }

        private static void BuildPrefab(
            string modelPath,
            string prefabPath,
            ChurchAssetKind kind,
            ChurchManifestAsset sourceAsset,
            ChurchManifest manifest,
            IReadOnlyDictionary<ChurchMaterialSlot, Material> materials)
        {
            GameObject modelAsset =
                AssetDatabase.LoadAssetAtPath<GameObject>(modelPath);
            if (modelAsset == null)
            {
                throw new InvalidOperationException(
                    $"Unity did not import '{modelPath}' as a model.");
            }

            GameObject root = new GameObject($"Church{kind}3D");
            try
            {
                GameObject model =
                    PrefabUtility.InstantiatePrefab(modelAsset) as GameObject;
                if (model == null)
                {
                    throw new InvalidOperationException(
                        $"Could not instantiate imported Church {kind} model.");
                }

                model.name = "Model";
                model.transform.SetParent(root.transform, false);
                model.transform.localPosition = Vector3.zero;
                model.transform.localRotation = Quaternion.Euler(
                    0f,
                    sourceAsset.runtime_wrapper_yaw_degrees,
                    0f);
                model.transform.localScale = Vector3.one;

                Dictionary<string, Renderer> renderers =
                    IndexUniqueRenderers(model);
                Dictionary<string, Transform> transforms =
                    IndexUniqueTransforms(model);
                if (renderers.Count != sourceAsset.renderer_count ||
                    sourceAsset.parts == null ||
                    sourceAsset.parts.Length != renderers.Count)
                {
                    throw new InvalidOperationException(
                        $"Church {kind} renderer count differs from manifest.");
                }

                var bindings = new List<ChurchRendererBinding>();
                foreach (ChurchManifestPart part in sourceAsset.parts)
                {
                    if (!renderers.TryGetValue(
                            part.name,
                            out Renderer renderer) ||
                        !Enum.TryParse(
                            part.material_slot,
                            false,
                            out ChurchMaterialSlot slot))
                    {
                        throw new InvalidOperationException(
                            $"Church {kind} part '{part.name}' is missing or " +
                            "has an unknown material slot.");
                    }

                    renderer.sharedMaterial = materials[slot];
                    renderer.shadowCastingMode = ShadowCastingMode.On;
                    renderer.receiveShadows = true;
                    bindings.Add(new ChurchRendererBinding(
                        part.name,
                        part.role,
                        slot,
                        renderer));
                }

                Bounds calculatedLocalBounds = CalculateLocalBounds(
                    root.transform,
                    renderers.Values);
                Bounds manifestLocalBounds = ConvertManifestBoundsToUnity(
                    sourceAsset);
                AssertBoundsNear(
                    calculatedLocalBounds,
                    manifestLocalBounds,
                    $"{kind} imported model");

                ChurchAssetRegistry registry =
                    root.AddComponent<ChurchAssetRegistry>();
                registry.Configure(
                    kind,
                    model.transform,
                    FindTransform(transforms, "ANCHOR_Exterior.Entrance"),
                    FindTransform(transforms, "ANCHOR_Exterior.Approach"),
                    FindTransform(transforms, "ANCHOR_Exterior.Return"),
                    FindTransform(transforms, "ANCHOR_Interior.Spawn"),
                    FindTransform(transforms, "ANCHOR_Interior.Exit"),
                    FindTransform(transforms, "ANCHOR_Interior.NarthexLight"),
                    FindTransform(transforms, "ANCHOR_Interior.NaveLight"),
                    FindTransform(transforms, "ANCHOR_Interior.SanctuaryLight"),
                    renderers.Values.OrderBy(
                        renderer => renderer.name,
                        StringComparer.Ordinal).ToArray(),
                    bindings.OrderBy(
                        binding => binding.SourceName,
                        StringComparer.Ordinal).ToArray(),
                    calculatedLocalBounds,
                    new ChurchDimensions(
                        manifest.dimensions_m.width,
                        manifest.dimensions_m.length,
                        manifest.dimensions_m.height,
                        manifest.door_opening_m.width,
                        manifest.door_opening_m.height),
                    sourceAsset.triangle_count,
                    manifest.generator_version,
                    manifest.design_id,
                    manifest.build_signature);

                PrefabUtility.SaveAsPrefabAsset(root, prefabPath);
            }
            finally
            {
                Object.DestroyImmediate(root);
            }
        }

        private static void ValidatePrefab(
            string prefabPath,
            ChurchAssetKind kind,
            ChurchManifestAsset sourceAsset,
            ChurchManifest manifest)
        {
            GameObject prefab =
                AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
            if (prefab == null)
            {
                throw new InvalidOperationException(
                    $"Church {kind} prefab is missing at '{prefabPath}'.");
            }

            ChurchAssetRegistry registry =
                prefab.GetComponent<ChurchAssetRegistry>();
            if (registry == null ||
                registry.Kind != kind ||
                registry.ModelRoot == null ||
                registry.Renderers.Count != sourceAsset.renderer_count ||
                registry.RendererBindings.Count != sourceAsset.renderer_count ||
                registry.SourceTriangleCount != sourceAsset.triangle_count ||
                !string.Equals(
                    registry.DesignId,
                    manifest.design_id,
                    StringComparison.Ordinal) ||
                !string.Equals(
                    registry.BuildSignature,
                    manifest.build_signature,
                    StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    $"Church {kind} registry source contract is stale.");
            }

            Bounds manifestLocalBounds = ConvertManifestBoundsToUnity(
                sourceAsset);
            AssertBoundsNear(
                registry.LocalBounds,
                manifestLocalBounds,
                $"{kind} registry");
            AssertBoundsNear(
                CalculateLocalBounds(prefab.transform, registry.Renderers),
                manifestLocalBounds,
                $"{kind} prefab renderers");

            if (kind == ChurchAssetKind.Exterior)
            {
                RequireAnchor(registry.EntranceAnchor, "exterior entrance");
                RequireAnchor(registry.ApproachAnchor, "exterior approach");
                RequireAnchor(registry.ReturnAnchor, "exterior return");
                Vector3 entrance = prefab.transform.InverseTransformPoint(
                    registry.EntranceAnchor.position);
                if (Mathf.Abs(entrance.x) > .001f ||
                    Mathf.Abs(entrance.z - 22.05f) > .001f)
                {
                    throw new InvalidOperationException(
                        $"Exterior entrance is {entrance}; expected local " +
                        "XZ (0,+22.05) after the runtime wrapper.");
                }
            }
            else
            {
                RequireAnchor(registry.SpawnAnchor, "interior spawn");
                RequireAnchor(registry.ExitAnchor, "interior exit");
                RequireAnchor(
                    registry.NarthexLightAnchor,
                    "narthex light");
                RequireAnchor(registry.NaveLightAnchor, "nave light");
                RequireAnchor(
                    registry.SanctuaryLightAnchor,
                    "sanctuary light");
            }

            if (prefab.GetComponentsInChildren<Collider>(true).Length != 0 ||
                prefab.GetComponentsInChildren<Light>(true).Length != 0 ||
                prefab.GetComponentsInChildren<Camera>(true).Length != 0 ||
                prefab.GetComponentsInChildren<Rigidbody>(true).Length != 0 ||
                prefab.GetComponentsInChildren<Animator>(true).Length != 0 ||
                prefab.GetComponentsInChildren<AudioSource>(true).Length != 0)
            {
                throw new InvalidOperationException(
                    $"Passive Church {kind} prefab contains a gameplay, " +
                    "lighting, camera, animation or audio component.");
            }

            foreach (ChurchRendererBinding binding in
                     registry.RendererBindings)
            {
                Material expected =
                    AssetDatabase.LoadAssetAtPath<Material>(
                        MaterialPath(binding.MaterialSlot));
                if (binding == null ||
                    binding.Renderer == null ||
                    string.IsNullOrWhiteSpace(binding.Role) ||
                    binding.Renderer.sharedMaterials.Length != 1 ||
                    binding.Renderer.sharedMaterial != expected)
                {
                    throw new InvalidOperationException(
                        $"Church {kind} renderer/material binding is invalid.");
                }
            }

            AssertNear(registry.Dimensions.Width, ExpectedWidth, "width");
            AssertNear(registry.Dimensions.Length, ExpectedLength, "length");
            AssertNear(registry.Dimensions.Height, ExpectedHeight, "height");
            AssertNear(
                registry.Dimensions.DoorWidth,
                ExpectedDoorWidth,
                "door width");
            AssertNear(
                registry.Dimensions.DoorHeight,
                ExpectedDoorHeight,
                "door height");
        }

        private static ChurchManifest LoadAndValidateManifest()
        {
            TextAsset source =
                AssetDatabase.LoadAssetAtPath<TextAsset>(ManifestPath);
            if (source == null)
            {
                throw new InvalidOperationException(
                    $"Could not load Church manifest '{ManifestPath}'.");
            }

            ChurchManifest manifest =
                JsonUtility.FromJson<ChurchManifest>(source.text);
            if (manifest == null ||
                manifest.dimensions_m == null ||
                manifest.door_opening_m == null ||
                manifest.assets == null ||
                manifest.assets.Length != 2 ||
                manifest.textures == null)
            {
                throw new InvalidOperationException(
                    "Church manifest is malformed.");
            }

            if (!string.Equals(
                    manifest.design_id,
                    ExpectedDesignId,
                    StringComparison.Ordinal) ||
                !string.Equals(
                    manifest.blender_forward_axis,
                    "-Y",
                    StringComparison.Ordinal) ||
                !string.Equals(
                    manifest.unity_exterior_entrance_outward_axis,
                    "+Z",
                    StringComparison.Ordinal) ||
                !string.Equals(
                    manifest.unity_interior_entrance_to_altar_axis,
                    "+Z",
                    StringComparison.Ordinal) ||
                manifest.colliders ||
                manifest.lights ||
                manifest.cameras ||
                manifest.animation_count != 0)
            {
                throw new InvalidOperationException(
                    "Church manifest design, axes or passive-asset flags " +
                    "differ from the approved contract.");
            }

            AssertNear(manifest.dimensions_m.width, ExpectedWidth, "width");
            AssertNear(manifest.dimensions_m.length, ExpectedLength, "length");
            AssertNear(manifest.dimensions_m.height, ExpectedHeight, "height");
            AssertNear(
                manifest.door_opening_m.width,
                ExpectedDoorWidth,
                "door width");
            AssertNear(
                manifest.door_opening_m.height,
                ExpectedDoorHeight,
                "door height");
            if (string.IsNullOrWhiteSpace(manifest.build_signature) ||
                manifest.build_signature.Length != 64)
            {
                throw new InvalidOperationException(
                    "Church manifest has no deterministic build signature.");
            }

            ChurchManifestAsset exterior =
                RequireAsset(manifest, "Exterior");
            ChurchManifestAsset interior =
                RequireAsset(manifest, "Interior");
            ValidateManifestAsset(
                exterior,
                180f,
                ExteriorMaximumRenderers,
                ExteriorMaximumTriangles);
            ValidateManifestAsset(
                interior,
                0f,
                InteriorMaximumRenderers,
                InteriorMaximumTriangles);

            foreach (ChurchMaterialSlot slot in
                     Enum.GetValues(typeof(ChurchMaterialSlot)))
            {
                ChurchManifestTexture texture = manifest.textures.FirstOrDefault(
                    item => string.Equals(
                        item.material_slot,
                        slot.ToString(),
                        StringComparison.Ordinal));
                if (texture == null ||
                    !string.Equals(
                        texture.asset_path,
                        TexturePath(slot),
                        StringComparison.Ordinal) ||
                    string.IsNullOrWhiteSpace(texture.sha256) ||
                    texture.sha256.Length != 64)
                {
                    throw new InvalidOperationException(
                        $"Church texture contract for '{slot}' is invalid.");
                }
            }

            return manifest;
        }

        private static void ValidateManifestAsset(
            ChurchManifestAsset asset,
            float expectedYaw,
            int maximumRenderers,
            int maximumTriangles)
        {
            if (asset.parts == null ||
                asset.anchors == null ||
                !IsValidManifestBounds(asset.bounds_min, asset.bounds_max) ||
                asset.mesh_count != asset.parts.Length ||
                asset.renderer_count != asset.parts.Length ||
                asset.renderer_count <= 0 ||
                asset.renderer_count > maximumRenderers ||
                asset.triangle_count <= 0 ||
                asset.triangle_count > maximumTriangles ||
                Mathf.Abs(asset.runtime_wrapper_yaw_degrees - expectedYaw) >
                .001f)
            {
                throw new InvalidOperationException(
                    $"Church {asset.kind} mesh, renderer, triangle or " +
                    "wrapper contract is invalid.");
            }

            var names = new HashSet<string>(StringComparer.Ordinal);
            foreach (ChurchManifestPart part in asset.parts)
            {
                if (part == null ||
                    !names.Add(part.name) ||
                    string.IsNullOrWhiteSpace(part.role) ||
                    !Enum.TryParse(
                        part.material_slot,
                        false,
                        out ChurchMaterialSlot _) ||
                    part.vertices <= 0 ||
                    part.triangles <= 0)
                {
                    throw new InvalidOperationException(
                        $"Church {asset.kind} part contract is invalid.");
                }
            }

            if (string.Equals(
                    asset.kind,
                    "Interior",
                    StringComparison.Ordinal))
            {
                ValidateInteriorLayoutContracts(asset);
            }
        }

        private static bool IsValidManifestBounds(
            float[] minimum,
            float[] maximum)
        {
            if (minimum == null || maximum == null ||
                minimum.Length != 3 || maximum.Length != 3)
            {
                return false;
            }

            for (int axis = 0; axis < 3; axis++)
            {
                if (float.IsNaN(minimum[axis]) ||
                    float.IsInfinity(minimum[axis]) ||
                    float.IsNaN(maximum[axis]) ||
                    float.IsInfinity(maximum[axis]) ||
                    minimum[axis] > maximum[axis])
                {
                    return false;
                }
            }

            return true;
        }

        private static void ValidateInteriorLayoutContracts(
            ChurchManifestAsset asset)
        {
            if (asset.layout_contract == null ||
                asset.layout_contract.Length !=
                RequiredInteriorLayoutContractCount ||
                InteriorLayoutContractKinds.Count !=
                RequiredInteriorLayoutContractCount)
            {
                throw new InvalidOperationException(
                    "Church Interior manifest must keep all " +
                    $"{RequiredInteriorLayoutContractCount} layout " +
                    "contracts.");
            }

            var names = new HashSet<string>(StringComparer.Ordinal);
            foreach (ChurchManifestLayoutContract contract in
                     asset.layout_contract)
            {
                if (contract == null ||
                    string.IsNullOrWhiteSpace(contract.name) ||
                    !names.Add(contract.name) ||
                    contract.count <= 0 ||
                    contract.centers_xz_flat == null ||
                    contract.centers_xz_flat.Length != contract.count * 2 ||
                    contract.footprint_xz_m == null ||
                    contract.footprint_xz_m.Length != 2 ||
                    contract.vertical_envelope_m == null ||
                    contract.vertical_envelope_m.Length != 2)
                {
                    throw new InvalidOperationException(
                        "Church Interior layout contract is malformed.");
                }
            }

            ChurchInteriorLayoutPlan plan =
                ChurchInteriorLayoutPlanner.Generate(0);
            foreach (KeyValuePair<string, ChurchInteriorFixtureKind> entry in
                     InteriorLayoutContractKinds)
            {
                ValidateLayoutContract(asset, entry.Key, plan, entry.Value);
            }
        }

        private static void ValidateLayoutContract(
            ChurchManifestAsset asset,
            string name,
            ChurchInteriorLayoutPlan plan,
            ChurchInteriorFixtureKind fixtureKind)
        {
            ChurchInteriorFixturePlan[] fixtures = plan.Fixtures
                .Where(fixture => fixture.Kind == fixtureKind)
                .ToArray();
            if (fixtures.Length == 0)
            {
                throw new InvalidOperationException(
                    $"Church Interior plan has no fixtures for '{name}'.");
            }

            ChurchInteriorFixturePlan first = fixtures[0];
            if (fixtures.Any(
                    fixture =>
                        Mathf.Abs(
                            fixture.Bounds.width - first.Bounds.width) >
                        .0001f ||
                        Mathf.Abs(
                            fixture.Bounds.height - first.Bounds.height) >
                        .0001f ||
                        Mathf.Abs(fixture.BaseHeight - first.BaseHeight) >
                        .0001f ||
                        Mathf.Abs(fixture.Height - first.Height) > .0001f))
            {
                throw new InvalidOperationException(
                    $"Church Interior plan contract '{name}' is not uniform.");
            }

            float[] expectedCenters = fixtures
                .SelectMany(
                    fixture => new[]
                    {
                        fixture.Bounds.center.x,
                        fixture.Bounds.center.y
                    })
                .ToArray();
            float[] expectedFootprint =
            {
                first.Bounds.width,
                first.Bounds.height
            };
            float[] expectedVerticalEnvelope =
            {
                first.BaseHeight,
                first.BaseHeight + first.Height
            };
            ChurchManifestLayoutContract contract =
                asset.layout_contract.SingleOrDefault(
                    item => string.Equals(
                        item.name,
                        name,
                        StringComparison.Ordinal));
            if (contract == null || contract.count != fixtures.Length ||
                !ArraysNear(contract.centers_xz_flat, expectedCenters) ||
                !ArraysNear(contract.footprint_xz_m, expectedFootprint) ||
                !ArraysNear(
                    contract.vertical_envelope_m,
                    expectedVerticalEnvelope))
            {
                throw new InvalidOperationException(
                    $"Church Interior layout contract '{name}' drifted.");
            }
        }

        private static bool ArraysNear(float[] actual, float[] expected)
        {
            if (actual == null || actual.Length != expected.Length)
            {
                return false;
            }

            for (int index = 0; index < expected.Length; index++)
            {
                if (Mathf.Abs(actual[index] - expected[index]) > .0001f)
                {
                    return false;
                }
            }

            return true;
        }

        private static ChurchManifestAsset RequireAsset(
            ChurchManifest manifest,
            string kind)
        {
            ChurchManifestAsset result = manifest.assets.FirstOrDefault(
                asset => string.Equals(
                    asset.kind,
                    kind,
                    StringComparison.Ordinal));
            if (result == null)
            {
                throw new InvalidOperationException(
                    $"Church manifest lacks its {kind} asset contract.");
            }

            return result;
        }

        private static Dictionary<string, Renderer> IndexUniqueRenderers(
            GameObject root)
        {
            var result = new Dictionary<string, Renderer>(
                StringComparer.Ordinal);
            foreach (Renderer renderer in
                     root.GetComponentsInChildren<Renderer>(true))
            {
                if (!result.TryAdd(renderer.name, renderer))
                {
                    throw new InvalidOperationException(
                        $"Church contains duplicate renderer " +
                        $"'{renderer.name}'.");
                }
            }

            return result;
        }

        private static Dictionary<string, Transform> IndexUniqueTransforms(
            GameObject root)
        {
            var result = new Dictionary<string, Transform>(
                StringComparer.Ordinal);
            foreach (Transform transform in
                     root.GetComponentsInChildren<Transform>(true))
            {
                if (!result.TryAdd(transform.name, transform))
                {
                    throw new InvalidOperationException(
                        $"Church contains duplicate transform " +
                        $"'{transform.name}'.");
                }
            }

            return result;
        }

        private static Transform FindTransform(
            IReadOnlyDictionary<string, Transform> transforms,
            string name)
        {
            transforms.TryGetValue(name, out Transform result);
            return result;
        }

        private static Bounds CalculateLocalBounds(
            Transform root,
            IEnumerable<Renderer> renderers)
        {
            bool initialized = false;
            Bounds result = default;
            foreach (Renderer renderer in renderers)
            {
                Bounds world = renderer.bounds;
                for (int x = -1; x <= 1; x += 2)
                {
                    for (int y = -1; y <= 1; y += 2)
                    {
                        for (int z = -1; z <= 1; z += 2)
                        {
                            Vector3 point = root.InverseTransformPoint(
                                world.center + Vector3.Scale(
                                    world.extents,
                                    new Vector3(x, y, z)));
                            if (!initialized)
                            {
                                result = new Bounds(point, Vector3.zero);
                                initialized = true;
                            }
                            else
                            {
                                result.Encapsulate(point);
                            }
                        }
                    }
                }
            }

            if (!initialized)
            {
                throw new InvalidOperationException(
                    "Church model has no renderer bounds.");
            }

            return result;
        }

        private static Bounds ConvertManifestBoundsToUnity(
            ChurchManifestAsset asset)
        {
            Vector3 sourceMinimum = new Vector3(
                asset.bounds_min[0],
                asset.bounds_min[1],
                asset.bounds_min[2]);
            Vector3 sourceMaximum = new Vector3(
                asset.bounds_max[0],
                asset.bounds_max[1],
                asset.bounds_max[2]);
            Quaternion wrapperRotation = Quaternion.Euler(
                0f,
                asset.runtime_wrapper_yaw_degrees,
                0f);
            bool initialized = false;
            Bounds result = default;
            for (int x = 0; x <= 1; x++)
            {
                for (int y = 0; y <= 1; y++)
                {
                    for (int z = 0; z <= 1; z++)
                    {
                        Vector3 blender = new Vector3(
                            x == 0 ? sourceMinimum.x : sourceMaximum.x,
                            y == 0 ? sourceMinimum.y : sourceMaximum.y,
                            z == 0 ? sourceMinimum.z : sourceMaximum.z);
                        Vector3 unity = wrapperRotation * new Vector3(
                            blender.x,
                            blender.z,
                            blender.y);
                        if (!initialized)
                        {
                            result = new Bounds(unity, Vector3.zero);
                            initialized = true;
                        }
                        else
                        {
                            result.Encapsulate(unity);
                        }
                    }
                }
            }

            return result;
        }

        private static MaterialColorContract ColorContract(
            ChurchMaterialSlot slot)
        {
            switch (slot)
            {
                case ChurchMaterialSlot.Plaster:
                    return new MaterialColorContract("#C7C6B3", 1f);
                case ChurchMaterialSlot.Stone:
                    return new MaterialColorContract("#7F8580", 1f);
                case ChurchMaterialSlot.Wood:
                    return new MaterialColorContract("#593019", 1f);
                case ChurchMaterialSlot.Roof:
                    return new MaterialColorContract("#1F302A", 1f);
                case ChurchMaterialSlot.Iron:
                    return new MaterialColorContract("#151816", 1f);
                case ChurchMaterialSlot.Gold:
                    return new MaterialColorContract("#B87519", 1f);
                case ChurchMaterialSlot.Floor:
                    return new MaterialColorContract("#6E6960", 1f);
                case ChurchMaterialSlot.Textile:
                    return new MaterialColorContract("#661512", 1f);
                case ChurchMaterialSlot.SacredArt:
                    return new MaterialColorContract("#DAB873", 1f);
                case ChurchMaterialSlot.Mural:
                    return new MaterialColorContract("#899997", 1f);
                case ChurchMaterialSlot.GlassCold:
                    return new MaterialColorContract("#69AABF", 1.35f);
                case ChurchMaterialSlot.GlassWarm:
                    return new MaterialColorContract("#F2A04E", 1.65f);
                case ChurchMaterialSlot.CandleFlame:
                    return new MaterialColorContract("#FF5E18", 2.8f);
                default:
                    throw new ArgumentOutOfRangeException(nameof(slot));
            }
        }

        private static Color MaterialColor(ChurchMaterialSlot slot)
        {
            MaterialColorContract contract = ColorContract(slot);
            if (!ColorUtility.TryParseHtmlString(
                    contract.Html,
                    out Color color))
            {
                throw new InvalidOperationException(
                    $"Invalid Church color '{contract.Html}'.");
            }

            return new Color(
                color.r * contract.Multiplier,
                color.g * contract.Multiplier,
                color.b * contract.Multiplier,
                1f);
        }

        private static bool IsEmissive(ChurchMaterialSlot slot)
        {
            return slot == ChurchMaterialSlot.GlassCold ||
                slot == ChurchMaterialSlot.GlassWarm ||
                slot == ChurchMaterialSlot.CandleFlame;
        }

        private static Texture2D LoadTexture(ChurchMaterialSlot slot)
        {
            string path = TexturePath(slot);
            Texture2D texture =
                AssetDatabase.LoadAssetAtPath<Texture2D>(path);
            if (texture == null)
            {
                throw new InvalidOperationException(
                    $"Church texture '{path}' is missing.");
            }

            return texture;
        }

        private static string TexturePath(ChurchMaterialSlot slot)
        {
            switch (slot)
            {
                case ChurchMaterialSlot.Plaster:
                    return $"{TextureFolder}/ChurchPlasterAlbedo.png";
                case ChurchMaterialSlot.Stone:
                    return $"{TextureFolder}/ChurchStoneAlbedo.png";
                case ChurchMaterialSlot.Wood:
                    return $"{TextureFolder}/ChurchWoodAlbedo.png";
                case ChurchMaterialSlot.Roof:
                case ChurchMaterialSlot.Iron:
                case ChurchMaterialSlot.Gold:
                    return $"{TextureFolder}/ChurchMetalAlbedo.png";
                case ChurchMaterialSlot.Floor:
                    return $"{TextureFolder}/ChurchFloorAlbedo.png";
                case ChurchMaterialSlot.Textile:
                    return $"{TextureFolder}/ChurchTextileAlbedo.png";
                case ChurchMaterialSlot.SacredArt:
                    return $"{TextureFolder}/ChurchSacredArtAtlasAlbedo.png";
                case ChurchMaterialSlot.Mural:
                    return $"{TextureFolder}/ChurchMuralAtlasAlbedo.png";
                case ChurchMaterialSlot.GlassCold:
                case ChurchMaterialSlot.GlassWarm:
                case ChurchMaterialSlot.CandleFlame:
                    return $"{TextureFolder}/ChurchGlassAtlasAlbedo.png";
                default:
                    throw new ArgumentOutOfRangeException(nameof(slot));
            }
        }

        private static string MaterialPath(ChurchMaterialSlot slot)
        {
            return $"{MaterialFolder}/Church{slot}.mat";
        }

        private static void RequireAnchor(Transform anchor, string label)
        {
            if (anchor == null)
            {
                throw new InvalidOperationException(
                    $"Church prefab lacks its {label} anchor.");
            }
        }

        private static void SetTextureIfPresent(
            Material material,
            string property,
            Texture texture)
        {
            if (material.HasProperty(property))
            {
                material.SetTexture(property, texture);
            }
        }

        private static void SetColorIfPresent(
            Material material,
            string property,
            Color color)
        {
            if (material.HasProperty(property))
            {
                material.SetColor(property, color);
            }
        }

        private static void SetFloatIfPresent(
            Material material,
            string property,
            float value)
        {
            if (material.HasProperty(property))
            {
                material.SetFloat(property, value);
            }
        }

        private static void AssertNear(
            float actual,
            float expected,
            string label)
        {
            if (Mathf.Abs(actual - expected) > .0001f)
            {
                throw new InvalidOperationException(
                    $"Church {label} is {actual}, expected {expected}.");
            }
        }

        private static void AssertBoundsNear(
            Bounds actual,
            Bounds expected,
            string label)
        {
            const float tolerance = .002f;
            if ((actual.min - expected.min).sqrMagnitude >
                    tolerance * tolerance ||
                (actual.max - expected.max).sqrMagnitude >
                    tolerance * tolerance)
            {
                throw new InvalidOperationException(
                    $"Church {label} bounds are min {actual.min}, max " +
                    $"{actual.max}; expected min {expected.min}, max " +
                    $"{expected.max} from the Blender manifest.");
            }
        }

        private static void ValidateDependencyStamp()
        {
            if (!SourcesExist())
            {
                return;
            }

            try
            {
                ChurchManifest manifest = LoadAndValidateManifest();
                foreach ((string path, ChurchAssetKind kind) in new[]
                         {
                             (ExteriorPrefabPath, ChurchAssetKind.Exterior),
                             (InteriorPrefabPath, ChurchAssetKind.Interior)
                         })
                {
                    GameObject prefab =
                        AssetDatabase.LoadAssetAtPath<GameObject>(path);
                    ChurchAssetRegistry registry = prefab != null
                        ? prefab.GetComponent<ChurchAssetRegistry>()
                        : null;
                    if (registry == null ||
                        registry.Kind != kind ||
                        !string.Equals(
                            registry.BuildSignature,
                            manifest.build_signature,
                            StringComparison.Ordinal))
                    {
                        QueueBuildWhenSourcesExist();
                        return;
                    }
                }
            }
            catch (Exception exception)
            {
                Debug.LogError(
                    $"Could not validate Church dependency stamp: " +
                    $"{exception}");
            }
        }

        private static void RunQueuedBuild()
        {
            buildQueued = false;
            if (!SourcesExist())
            {
                return;
            }

            try
            {
                BuildOrThrow();
            }
            catch (Exception exception)
            {
                Debug.LogError(
                    $"Could not build Church runtime prefabs: {exception}");
            }
        }

        private static void EnsureFolderForAsset(string assetPath)
        {
            string directory = Path.GetDirectoryName(assetPath)
                ?.Replace('\\', '/');
            if (string.IsNullOrEmpty(directory))
            {
                return;
            }

            string[] segments = directory.Split('/');
            string current = segments[0];
            for (int index = 1; index < segments.Length; index++)
            {
                string next = $"{current}/{segments[index]}";
                if (!AssetDatabase.IsValidFolder(next))
                {
                    AssetDatabase.CreateFolder(current, segments[index]);
                }

                current = next;
            }
        }

        private readonly struct MaterialColorContract
        {
            public MaterialColorContract(string html, float multiplier)
            {
                Html = html;
                Multiplier = multiplier;
            }

            public string Html { get; }
            public float Multiplier { get; }
        }

        [Serializable]
        private sealed class ChurchManifest
        {
            public string generator_version;
            public string design_id;
            public ChurchManifestDimensions dimensions_m;
            public ChurchManifestDoor door_opening_m;
            public string blender_forward_axis;
            public string unity_exterior_entrance_outward_axis;
            public string unity_interior_entrance_to_altar_axis;
            public bool colliders;
            public bool lights;
            public bool cameras;
            public int animation_count;
            public ChurchManifestTexture[] textures;
            public ChurchManifestAsset[] assets;
            public string build_signature;
        }

        [Serializable]
        private sealed class ChurchManifestDimensions
        {
            public float width;
            public float length;
            public float height;
        }

        [Serializable]
        private sealed class ChurchManifestDoor
        {
            public float width;
            public float height;
        }

        [Serializable]
        private sealed class ChurchManifestTexture
        {
            public string material_slot;
            public string asset_path;
            public string sha256;
            public string base_material_asset;
        }

        [Serializable]
        private sealed class ChurchManifestAsset
        {
            public string kind;
            public string root_name;
            public float runtime_wrapper_yaw_degrees;
            public int mesh_count;
            public int renderer_count;
            public int triangle_count;
            public float[] bounds_min;
            public float[] bounds_max;
            public ChurchManifestAnchor[] anchors;
            public ChurchManifestPart[] parts;
            public ChurchManifestLayoutContract[] layout_contract;
        }

        [Serializable]
        private sealed class ChurchManifestAnchor
        {
            public string name;
            public string role;
            public float[] local_position;
            public float[] local_rotation_degrees;
        }

        [Serializable]
        private sealed class ChurchManifestPart
        {
            public string name;
            public string role;
            public string material_slot;
            public int vertices;
            public int triangles;
        }

        [Serializable]
        private sealed class ChurchManifestLayoutContract
        {
            public string name;
            public int count;
            public float[] centers_xz_flat;
            public float[] footprint_xz_m;
            public float[] vertical_envelope_m;
        }
    }
}

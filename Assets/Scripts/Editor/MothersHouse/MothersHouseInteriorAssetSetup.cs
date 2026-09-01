using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using Object = UnityEngine.Object;

namespace BarPromenade.Editor
{
    /// <summary>
    /// Imports the measured Blender room, binds its semantic manifest and
    /// emits the passive Resources prefab consumed by the pure interior plan.
    /// </summary>
    [InitializeOnLoad]
    public static class MothersHouseInteriorAssetSetup
    {
        public const string ModelPath = "Assets/MothersHouse/Models/MothersHouseInterior3D.fbx";
        public const string ManifestPath = "Assets/MothersHouse/Models/MothersHouseInterior3D.json";
        public const string PrefabPath = "Assets/Resources/MothersHouse/MothersHouseInterior3D.prefab";
        public const string PositiveAtlasPath = "Assets/Resources/MothersHouse/Textures/MothersHousePositiveAtlas.png";
        public const string SharedLitMaterialPath = "Assets/Resources/Materials/RuntimePrimitiveLit.mat";
        public const string SharedEmissionMaterialPath = "Assets/Resources/Materials/CityNoirEmission.mat";

        private const string ExpectedDesignId = "mothers_house_interior_v1";
        private const string ExpectedGeneratorVersion = "1.4.1";
        private const int ExpectedAnchorCount = 10;
        private const int MaximumRenderers = 64;
        private const int MaximumTriangles = 14000;
        private const float MeasureTolerance = 0.02f;
        private const float RotationToleranceDegrees = 0.1f;
        private const float UvTolerance = 0.00001f;

        private static readonly MothersHouseInteriorAtlasCell[] AtlasCells = {
            new MothersHouseInteriorAtlasCell("Wallpaper", 0, 3),
            new MothersHouseInteriorAtlasCell("CeilingPlaster", 1, 3),
            new MothersHouseInteriorAtlasCell("PlankFloor", 2, 3),
            new MothersHouseInteriorAtlasCell("DarkWood", 3, 3),
            new MothersHouseInteriorAtlasCell("Upholstery", 0, 2),
            new MothersHouseInteriorAtlasCell("BedLinen", 1, 2),
            new MothersHouseInteriorAtlasCell("Rug", 2, 2),
            new MothersHouseInteriorAtlasCell("Concrete", 3, 2),
            new MothersHouseInteriorAtlasCell("Ceramic", 0, 1),
            new MothersHouseInteriorAtlasCell("PaintedMetal", 1, 1),
            new MothersHouseInteriorAtlasCell("Glass", 2, 1),
            new MothersHouseInteriorAtlasCell("Fire", 3, 1)
        };

        private static readonly string[] RequiredAnchorRoles = {
            "entry",
            "spawn",
            "exit",
            "camera",
            "camera_target",
            "fireplace",
            "fire_light",
            "floor_lamp_light",
            "tabletop",
            "teapot_dock"
        };

        private static readonly string[] RequiredFireParts = {
            "FIX_Fire.Embers",
            "FIX_Fire.Flame.Back",
            "FIX_Fire.Flame.Front"
        };

        private static readonly string[] RequiredPracticalParts = {
            "DRESS_FloorLamp.Frame",
            "DRESS_FloorLamp.Shade",
            "DRESS_FloorLamp.Bulb"
        };

        private static readonly string[] RequiredUpperParts = {
            "FIX_InterstoreyCeiling",
            "FIX_UpperFloor",
            "FIX_Stair.Steps",
            "FIX_Stair.Rail",
            "FIX_UpperWalls",
            "FIX_UpperPartitions",
            "FIX_UpperDoorFrames",
            "FIX_UpperStairGuards",
            "FIX_UpperCeiling"
        };

        private static readonly HashSet<string> AllowedSheets = new HashSet<string>(StringComparer.Ordinal)
            {
                "Wallpaper",
                "CeilingPlaster",
                "PlankFloor",
                "DarkWood",
                "Upholstery",
                "BedLinen",
                "PaintedMetal",
                "Concrete",
                "Rug",
                "Glass",
                "Ceramic",
                "Fire"
            };

        private static bool buildQueued;

        public static bool IsBuilding { get; private set; }

        static MothersHouseInteriorAssetSetup()
        {
            QueueBuildWhenSourcesExist();
        }

        [MenuItem("Bar Promenade/Mother's House/Build Runtime Prefab")]
        public static void Run()
        {
            BuildOrThrow();
            AssetDatabase.SaveAssets();
        }

        [MenuItem("Bar Promenade/Mother's House/Validate Imported Contract")]
        public static void RunValidation()
        {
            ValidateOrThrow();
            Debug.Log("Mother's house interior model contract is valid.");
        }

        public static void RunBatch()
        {
            try
            {
                buildQueued = false;
                EditorApplication.delayCall -= RunQueuedBuild;
                BuildOrThrow();
                ProjectSceneSetup.Run();
                AssetDatabase.SaveAssets();
                EditorApplication.Exit(0);
            }
            catch (Exception error)
            {
                Debug.LogError(error);
                EditorApplication.Exit(1);
            }
        }

        public static bool IsModelPath(string path)
        {
            return !string.IsNullOrEmpty(path) &&
                string.Equals(
                    path,
                    ModelPath,
                    StringComparison.OrdinalIgnoreCase);
        }

        public static bool IsManifestPath(string path)
        {
            return !string.IsNullOrEmpty(path) &&
                string.Equals(
                    path,
                    ManifestPath,
                    StringComparison.OrdinalIgnoreCase);
        }

        public static bool IsPositiveAtlasPath(string path)
        {
            return !string.IsNullOrEmpty(path) &&
                string.Equals(
                    path,
                    PositiveAtlasPath,
                    StringComparison.OrdinalIgnoreCase);
        }

        public static bool SourcesExist()
        {
            return File.Exists(ModelPath) &&
                   File.Exists(ManifestPath) &&
                   File.Exists(PositiveAtlasPath);
        }

        public static void QueueBuildWhenSourcesExist()
        {
            if (buildQueued || !SourcesExist())
            {
                return;
            }

            buildQueued = true;
            EditorApplication.delayCall += RunQueuedBuild;
        }

        public static void BuildOrThrow()
        {
            if (!SourcesExist())
            {
                throw new InvalidOperationException(
                    "Mother's house model sources are missing. Run " +
                    "tools/build-mothers-house-interior-3d-model.py " +
                    "through Blender first.");
            }

            IsBuilding = true;
            try
            {
                EnsureFolderForAsset(PrefabPath);
                AssetDatabase.ImportAsset(
                    ModelPath,
                    ImportAssetOptions.ForceUpdate |
                    ImportAssetOptions.ForceSynchronousImport);
                AssetDatabase.ImportAsset(
                    ManifestPath,
                    ImportAssetOptions.ForceUpdate |
                    ImportAssetOptions.ForceSynchronousImport);
                AssetDatabase.ImportAsset(
                    PositiveAtlasPath,
                    ImportAssetOptions.ForceUpdate |
                    ImportAssetOptions.ForceSynchronousImport);
                MothersHouseManifest manifest =
                    LoadAndValidateManifest();
                ValidatePositiveAtlasOrThrow();
                BuildPrefab(manifest);
                AssetDatabase.SaveAssets();
            }
            finally
            {
                IsBuilding = false;
            }

            AssetDatabase.Refresh();
            ValidateOrThrow();
        }

        public static void ValidateOrThrow()
        {
            MothersHouseManifest manifest = LoadAndValidateManifest();
            Texture2D positiveAtlas = ValidatePositiveAtlasOrThrow();
            GameObject prefab =
                AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
            if (prefab == null)
            {
                throw new InvalidOperationException(
                    $"The mother's house prefab is missing at " +
                    $"'{PrefabPath}'.");
            }

            var problems = new List<string>();
            MothersHouseInteriorAssetRegistry registry =
                prefab.GetComponent<MothersHouseInteriorAssetRegistry>();
            if (registry == null)
            {
                throw new InvalidOperationException(
                    "The mother's house prefab has no typed registry.");
            }

            AppendIfDifferent(
                registry.DesignId,
                manifest.design_id,
                "design id",
                problems);
            AppendIfDifferent(
                registry.SourceGeneratorVersion,
                manifest.generator_version,
                "generator version",
                problems);
            AppendIfDifferent(
                registry.BuildSignature,
                manifest.build_signature,
                "build signature",
                problems);
            if (registry.SourceTriangleCount != manifest.triangle_count)
            {
                problems.Add(
                    $"registry has {registry.SourceTriangleCount} " +
                    $"triangles against manifest {manifest.triangle_count}");
            }

            ValidatePositiveAtlasRegistry(
                registry,
                positiveAtlas,
                problems);

            AppendDimensionProblems(registry.Dimensions, problems);
            AppendForbidden<Collider>(prefab, "collider", problems);
            AppendForbidden<Light>(prefab, "light", problems);
            AppendForbidden<Camera>(prefab, "camera", problems);
            AppendForbidden<Rigidbody>(prefab, "rigidbody", problems);
            AppendForbidden<Animator>(prefab, "animator", problems);

            Renderer[] rendererArray =
                prefab.GetComponentsInChildren<Renderer>(true);
            if (rendererArray.Length != manifest.parts.Length)
            {
                problems.Add(
                    $"prefab has {rendererArray.Length} renderers against " +
                    $"manifest {manifest.parts.Length}");
            }
            if (rendererArray.Length > MaximumRenderers)
            {
                problems.Add(
                    $"{rendererArray.Length} renderers exceed " +
                    $"{MaximumRenderers}");
            }
            int importedTriangleCount = CountImportedTriangles(
                prefab,
                problems);
            if (importedTriangleCount != manifest.triangle_count)
            {
                problems.Add(
                    $"imported meshes contain {importedTriangleCount} " +
                    $"triangles against manifest " +
                    $"{manifest.triangle_count}; Unity discarded or " +
                    "changed authored polygons");
            }

            Dictionary<string, Renderer> renderers =
                IndexUniqueRenderers(prefab);
            Material sharedLit =
                AssetDatabase.LoadAssetAtPath<Material>(
                    SharedLitMaterialPath);
            Material sharedEmission =
                AssetDatabase.LoadAssetAtPath<Material>(
                    SharedEmissionMaterialPath);
            ValidatePartBindings(
                registry,
                manifest,
                renderers,
                sharedLit,
                sharedEmission,
                problems);
            ValidateAnchorBindings(registry, manifest, prefab, problems);

            Bounds measured = CalculateLocalBounds(
                prefab.transform,
                rendererArray);
            if (!BoundsClose(measured, registry.LocalBounds))
            {
                problems.Add(
                    $"registry bounds {registry.LocalBounds} differ from " +
                    $"measured renderer bounds {measured}");
            }
            Bounds expected = BoundsFromManifest(manifest);
            if (!BoundsClose(measured, expected))
            {
                problems.Add(
                    $"prefab bounds {measured} differ from manifest " +
                    $"{expected}");
            }

            if (problems.Count > 0)
            {
                throw new InvalidOperationException(
                    "Mother's house interior prefab failed validation:" +
                    Environment.NewLine + "  " +
                    string.Join(Environment.NewLine + "  ", problems));
            }
        }

        private static Texture2D ValidatePositiveAtlasOrThrow()
        {
            Texture2D atlas = AssetDatabase.LoadAssetAtPath<Texture2D>(PositiveAtlasPath);
            TextureImporter importer = AssetImporter.GetAtPath(PositiveAtlasPath) as TextureImporter;
            if (atlas == null || importer == null)
            {
                throw new InvalidOperationException(
                    $"The mother's-house positive atlas failed to load " +
                    $"from '{PositiveAtlasPath}'.");
            }

            if (atlas.width != MothersHouseInteriorAssetRegistry.PositiveAtlasWidth ||
                atlas.height != MothersHouseInteriorAssetRegistry.PositiveAtlasHeight ||
                atlas.mipmapCount != 1 ||
                atlas.wrapMode != TextureWrapMode.Clamp ||
                atlas.filterMode != FilterMode.Bilinear)
            {
                throw new InvalidOperationException(
                    "The mother's-house positive atlas runtime import " +
                    $"contract drifted: got {atlas.width}x{atlas.height}, " +
                    $"{atlas.mipmapCount} mip levels, {atlas.wrapMode} " +
                    $"wrap and {atlas.filterMode} filtering.");
            }

            if (importer.textureType != TextureImporterType.Default ||
                importer.textureShape != TextureImporterShape.Texture2D ||
                !importer.sRGBTexture ||
                importer.alphaSource != TextureImporterAlphaSource.None ||
                importer.mipmapEnabled ||
                importer.wrapMode != TextureWrapMode.Clamp ||
                importer.filterMode != FilterMode.Bilinear ||
                importer.textureCompression != TextureImporterCompression.Uncompressed ||
                importer.crunchedCompression ||
                importer.npotScale != TextureImporterNPOTScale.None ||
                importer.maxTextureSize < MothersHouseInteriorAssetRegistry.PositiveAtlasWidth ||
                importer.isReadable)
            {
                throw new InvalidOperationException(
                    "The mother's-house positive atlas importer must stay " +
                    "sRGB, 2D, clamp, bilinear, no-mips, uncompressed, " +
                    "non-readable and preserve its NPOT dimensions.");
            }

            return atlas;
        }

        private static MothersHouseInteriorAtlasContract CreatePositiveAtlasContract(Texture2D atlas)
        {
            return new MothersHouseInteriorAtlasContract(
                atlas, MothersHouseInteriorAssetRegistry.PositiveAtlasResourcePath,
                MothersHouseInteriorAssetRegistry.PositiveAtlasWidth,
                MothersHouseInteriorAssetRegistry.PositiveAtlasHeight,
                MothersHouseInteriorAssetRegistry.PositiveAtlasColumns,
                MothersHouseInteriorAssetRegistry.PositiveAtlasRows,
                MothersHouseInteriorAssetRegistry.PositiveAtlasInsetPixels,
                true, false, true, TextureWrapMode.Clamp, FilterMode.Bilinear,
                (MothersHouseInteriorAtlasCell[])AtlasCells.Clone());
        }

        private static void ValidatePositiveAtlasRegistry(
            MothersHouseInteriorAssetRegistry registry, Texture2D expectedTexture,
            ICollection<string> problems)
        {
            MothersHouseInteriorAtlasContract atlas = registry.PositiveAtlas;
            if (atlas == null)
            {
                problems.Add("registry has no positive atlas contract");
                return;
            }

            if (!atlas.IsConfigured ||
                atlas.Texture != expectedTexture ||
                !string.Equals(atlas.ResourcePath,
                    MothersHouseInteriorAssetRegistry.PositiveAtlasResourcePath,
                    StringComparison.Ordinal) ||
                atlas.Width != MothersHouseInteriorAssetRegistry.PositiveAtlasWidth ||
                atlas.Height != MothersHouseInteriorAssetRegistry.PositiveAtlasHeight ||
                atlas.Columns != MothersHouseInteriorAssetRegistry.PositiveAtlasColumns ||
                atlas.Rows != MothersHouseInteriorAssetRegistry.PositiveAtlasRows ||
                atlas.InsetPixels != MothersHouseInteriorAssetRegistry.PositiveAtlasInsetPixels ||
                !atlas.SRgb ||
                atlas.Mipmaps ||
                !atlas.Uncompressed ||
                atlas.WrapMode != TextureWrapMode.Clamp ||
                atlas.FilterMode != FilterMode.Bilinear)
            {
                problems.Add("registry positive atlas metadata drifted");
            }

            if (atlas.Cells.Count != AtlasCells.Length)
            {
                problems.Add(
                    $"registry has {atlas.Cells.Count} atlas cells " +
                    $"against {AtlasCells.Length}");
                return;
            }

            for (int index = 0; index < AtlasCells.Length; index++)
            {
                MothersHouseInteriorAtlasCell expected = AtlasCells[index];
                if (!atlas.TryGetCell(expected.Sheet,
                        out MothersHouseInteriorAtlasCell actual) ||
                    actual.Column != expected.Column ||
                    actual.Row != expected.Row)
                {
                    problems.Add(
                        $"atlas sheet '{expected.Sheet}' is not in cell " +
                        $"({expected.Column}, {expected.Row})");
                }
            }
        }

        private static void RunQueuedBuild()
        {
            buildQueued = false;
            if (IsBuilding || !SourcesExist())
            {
                return;
            }

            try
            {
                BuildOrThrow();
            }
            catch (Exception error)
            {
                Debug.LogException(error);
            }
        }

        private static void BuildPrefab(MothersHouseManifest manifest)
        {
            GameObject modelAsset =
                AssetDatabase.LoadAssetAtPath<GameObject>(ModelPath);
            if (modelAsset == null)
            {
                throw new InvalidOperationException(
                    $"Unity did not import '{ModelPath}'.");
            }

            Material sharedLit =
                AssetDatabase.LoadAssetAtPath<Material>(
                    SharedLitMaterialPath);
            Material sharedEmission =
                AssetDatabase.LoadAssetAtPath<Material>(
                    SharedEmissionMaterialPath);
            if (sharedLit == null || sharedEmission == null)
            {
                throw new InvalidOperationException(
                    "The two shared runtime materials failed to load.");
            }
            Texture2D positiveAtlas = ValidatePositiveAtlasOrThrow();
            MothersHouseInteriorAtlasContract atlasContract =
                CreatePositiveAtlasContract(positiveAtlas);

            var root = new GameObject("MothersHouseInterior3D");
            try
            {
                var model =
                    PrefabUtility.InstantiatePrefab(modelAsset) as GameObject;
                if (model == null)
                {
                    throw new InvalidOperationException(
                        $"Could not instantiate '{ModelPath}'.");
                }

                model.name = "Model";
                model.transform.SetParent(root.transform, false);
                model.transform.localPosition = Vector3.zero;
                model.transform.localRotation = Quaternion.identity;
                model.transform.localScale = Vector3.one;

                Dictionary<string, Renderer> renderers =
                    IndexUniqueRenderers(model);
                Dictionary<string, Transform> transforms =
                    IndexTransforms(model);
                Transform authoringRoot =
                    ResolveAuthoringRoot(model.transform);
                EnsureExactRendererSet(manifest, renderers);

                var parts = new List<MothersHouseInteriorPartBinding>();
                foreach (MothersHouseManifestPart source in manifest.parts)
                {
                    Renderer renderer = renderers[source.name];
                    renderer.sharedMaterial = source.emissive
                        ? sharedEmission
                        : sharedLit;
                    renderer.shadowCastingMode = source.casts_shadows
                        ? ShadowCastingMode.On
                        : ShadowCastingMode.Off;
                    renderer.receiveShadows = source.casts_shadows;
                    Rect sourceUvBounds = CalculateUvBounds(renderer);
                    if (!atlasContract.TryCreateBaseMapTransform(
                            source.sheet,
                            sourceUvBounds,
                            out Vector4 baseMapTransform))
                    {
                        throw new InvalidOperationException(
                            $"Renderer '{source.name}' cannot normalize " +
                            $"UV bounds {sourceUvBounds} into atlas sheet " +
                            $"'{source.sheet}'.");
                    }
                    parts.Add(new MothersHouseInteriorPartBinding(
                        source.name,
                        source.role,
                        source.group,
                        source.sheet,
                        source.emissive,
                        source.casts_shadows,
                        ReadColor(source.tint, source.name),
                        sourceUvBounds,
                        baseMapTransform,
                        renderer));
                }

                var anchors = new List<MothersHouseInteriorAnchorBinding>();
                foreach (MothersHouseManifestAnchor source in
                         manifest.anchors)
                {
                    if (!transforms.TryGetValue(
                            source.name,
                            out Transform anchor))
                    {
                        throw new InvalidOperationException(
                            $"Manifest anchor '{source.name}' is absent " +
                            "from the imported model.");
                    }

                    AssertAnchorPosition(root.transform, anchor, source);
                    AssertTeapotDockRotation(
                        authoringRoot,
                        anchor,
                        source);
                    anchors.Add(new MothersHouseInteriorAnchorBinding(
                        source.name,
                        source.role,
                        anchor));
                }

                Bounds measured = CalculateLocalBounds(
                    root.transform,
                    renderers.Values);
                Bounds expected = BoundsFromManifest(manifest);
                if (!BoundsClose(measured, expected))
                {
                    throw new InvalidOperationException(
                        $"Imported renderer bounds {measured} differ from " +
                        $"manifest {expected}. Preserve the FBX root scale.");
                }

                MothersHouseInteriorAssetRegistry registry =
                    root.AddComponent<MothersHouseInteriorAssetRegistry>();
                registry.Configure(
                    authoringRoot,
                    anchors
                        .OrderBy(
                            binding => binding.AnchorName,
                            StringComparer.Ordinal)
                        .ToArray(),
                    parts
                        .OrderBy(
                            binding => binding.SourceName,
                            StringComparer.Ordinal)
                        .ToArray(),
                    atlasContract,
                    measured,
                    new MothersHouseInteriorDimensions(
                        manifest.dimensions_m.width,
                        manifest.dimensions_m.depth,
                        manifest.dimensions_m.height,
                        manifest.wall_thickness_m,
                        manifest.door_opening_m.width,
                        manifest.door_opening_m.height),
                    manifest.triangle_count,
                    manifest.generator_version,
                    manifest.design_id,
                    manifest.build_signature);

                PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
            }
            finally
            {
                Object.DestroyImmediate(root);
            }
        }

        private static MothersHouseManifest LoadAndValidateManifest()
        {
            if (!File.Exists(ManifestPath))
            {
                throw new InvalidOperationException(
                    $"Manifest '{ManifestPath}' is missing.");
            }

            MothersHouseManifest manifest = JsonUtility.FromJson<
                MothersHouseManifest>(File.ReadAllText(ManifestPath));
            if (manifest == null)
            {
                throw new InvalidOperationException(
                    "The mother's house manifest could not be parsed.");
            }

            if (!string.Equals(
                    manifest.design_id,
                    ExpectedDesignId,
                    StringComparison.Ordinal) ||
                !string.Equals(
                    manifest.generator_version,
                    ExpectedGeneratorVersion,
                    StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    $"Unexpected model contract '{manifest.design_id}' / " +
                    $"'{manifest.generator_version}'.");
            }

            RequireClose(manifest.dimensions_m.width, 10f, "room width");
            RequireClose(manifest.dimensions_m.depth, 8f, "room depth");
            RequireClose(manifest.dimensions_m.height, 3.4f, "room height");
            if (manifest.upper_storey_m == null)
            {
                throw new InvalidOperationException(
                    "The manifest is missing its traversable upper storey.");
            }
            RequireClose(
                manifest.upper_storey_m.floor_elevation,
                3.54f,
                "upper floor elevation");
            RequireClose(
                manifest.upper_storey_m.ceiling_height,
                5.90f,
                "upper ceiling height");
            RequireClose(
                manifest.upper_storey_m.stair_width,
                1.30f,
                "stair width");
            RequireClose(
                manifest.upper_storey_m.door_width,
                1.20f,
                "upper door width");
            RequireClose(
                manifest.upper_storey_m.door_height,
                2.20f,
                "upper door height");
            if (manifest.upper_storey_m.stair_step_count != 19 ||
                manifest.upper_storey_m.room_count != 2 ||
                manifest.upper_storey_m.furnished ||
                manifest.upper_storey_m.stair_opening == null ||
                manifest.upper_storey_m.stair_opening.Length != 4 ||
                manifest.upper_storey_m.door_centers_z == null ||
                manifest.upper_storey_m.door_centers_z.Length != 2)
            {
                throw new InvalidOperationException(
                    "The upper-storey circulation contract drifted.");
            }
            RequireClose(
                manifest.wall_thickness_m,
                0.24f,
                "wall thickness");
            RequireClose(
                manifest.door_opening_m.width,
                1.30f,
                "door width");
            RequireClose(
                manifest.door_opening_m.height,
                2.20f,
                "door height");
            if (!string.Equals(
                    manifest.door_opening_m.wall,
                    "south",
                    StringComparison.Ordinal) ||
                Mathf.Abs(manifest.door_opening_m.center_x) > 0.001f)
            {
                throw new InvalidOperationException(
                    "The entrance opening must stay centred in the south " +
                    "wall opposite the fireplace.");
            }

            if (manifest.colliders || manifest.lights || manifest.cameras ||
                manifest.animation_count != 0)
            {
                throw new InvalidOperationException(
                    "The authored room must remain passive: no collider, " +
                    "light, camera or animation may arrive from Blender.");
            }
            if (manifest.kettle_contract == null ||
                manifest.kettle_contract.geometry_included)
            {
                throw new InvalidOperationException(
                    "The room FBX must not duplicate the NPC kettle.");
            }
            if (manifest.anchors == null ||
                manifest.anchors.Length != ExpectedAnchorCount)
            {
                throw new InvalidOperationException(
                    $"The model must expose exactly {ExpectedAnchorCount} " +
                    "semantic anchors.");
            }
            ValidateAnchors(manifest);
            ValidateParts(manifest);

            Bounds bounds = BoundsFromManifest(manifest);
            Bounds planBounds = MothersHouseInteriorLayoutPlanner
                .ModelLocalBounds;
            if (!BoundsClose(bounds, planBounds))
            {
                throw new InvalidOperationException(
                    $"Manifest bounds {bounds} differ from pure plan " +
                    $"bounds {planBounds}.");
            }
            return manifest;
        }

        private static void ValidateAnchors(MothersHouseManifest manifest)
        {
            var names = new HashSet<string>(StringComparer.Ordinal);
            var roles = new HashSet<string>(StringComparer.Ordinal);
            foreach (MothersHouseManifestAnchor anchor in manifest.anchors)
            {
                if (anchor == null ||
                    string.IsNullOrWhiteSpace(anchor.name) ||
                    string.IsNullOrWhiteSpace(anchor.role) ||
                    !IsFiniteVector(anchor.unity_local_position))
                {
                    throw new InvalidOperationException(
                        "Every model anchor needs a name, role and finite " +
                        "Unity-local position.");
                }
                if (!anchor.name.StartsWith(
                        "ANCHOR_",
                        StringComparison.Ordinal))
                {
                    throw new InvalidOperationException(
                        $"Anchor '{anchor.name}' lacks the ANCHOR_ prefix.");
                }
                if (!names.Add(anchor.name) || !roles.Add(anchor.role))
                {
                    throw new InvalidOperationException(
                        $"Anchor '{anchor.name}' repeats a name or role.");
                }
            }

            foreach (string role in RequiredAnchorRoles)
            {
                if (!roles.Contains(role))
                {
                    throw new InvalidOperationException(
                        $"Required anchor role '{role}' is missing.");
                }
            }
        }

        private static void ValidateParts(MothersHouseManifest manifest)
        {
            if (manifest.parts == null || manifest.parts.Length == 0 ||
                manifest.parts.Length > MaximumRenderers)
            {
                throw new InvalidOperationException(
                    $"Manifest part count is outside 1..{MaximumRenderers}.");
            }

            var names = new HashSet<string>(StringComparer.Ordinal);
            var sheets = new HashSet<string>(StringComparer.Ordinal);
            int triangleTotal = 0;
            foreach (MothersHouseManifestPart part in manifest.parts)
            {
                if (part == null ||
                    string.IsNullOrWhiteSpace(part.name) ||
                    string.IsNullOrWhiteSpace(part.role) ||
                    !AllowedSheets.Contains(part.sheet) ||
                    part.tint == null || part.tint.Length != 4 ||
                    part.vertices <= 0 || part.triangles <= 0)
                {
                    throw new InvalidOperationException(
                        "Every model part needs a unique name, semantic " +
                        "role, supported sheet, RGBA tint and geometry.");
                }
                if (!names.Add(part.name))
                {
                    throw new InvalidOperationException(
                        $"Manifest repeats part '{part.name}'.");
                }
                sheets.Add(part.sheet);
                if (part.name.IndexOf(
                        "Kettle",
                        StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    throw new InvalidOperationException(
                        $"Part '{part.name}' duplicates the runtime kettle.");
                }
                triangleTotal += part.triangles;
            }

            foreach (string requiredPart in
                     RequiredFireParts
                         .Concat(RequiredPracticalParts)
                         .Concat(RequiredUpperParts))
            {
                if (!names.Contains(requiredPart))
                {
                    throw new InvalidOperationException(
                        $"Required renderer '{requiredPart}' is " +
                        "missing.");
                }
            }
            foreach (string sheet in AllowedSheets)
            {
                if (!sheets.Contains(sheet))
                {
                    throw new InvalidOperationException(
                        $"Required positive-atlas sheet '{sheet}' is " +
                        "unused by the room.");
                }
            }
            if (triangleTotal != manifest.triangle_count ||
                triangleTotal <= 0 || triangleTotal > MaximumTriangles)
            {
                throw new InvalidOperationException(
                    $"Manifest triangle total {triangleTotal} disagrees " +
                    $"with {manifest.triangle_count} or exceeds " +
                    $"{MaximumTriangles}.");
            }
        }

        private static void EnsureExactRendererSet(
            MothersHouseManifest manifest,
            IReadOnlyDictionary<string, Renderer> renderers)
        {
            if (renderers.Count != manifest.parts.Length)
            {
                throw new InvalidOperationException(
                    $"Imported FBX has {renderers.Count} renderers against " +
                    $"manifest {manifest.parts.Length}.");
            }
            foreach (MothersHouseManifestPart part in manifest.parts)
            {
                if (!renderers.ContainsKey(part.name))
                {
                    throw new InvalidOperationException(
                        $"Manifest part '{part.name}' has no renderer.");
                }
            }
        }

        private static Dictionary<string, Renderer> IndexUniqueRenderers(
            GameObject model)
        {
            var result = new Dictionary<string, Renderer>(
                StringComparer.Ordinal);
            foreach (Renderer renderer in
                     model.GetComponentsInChildren<Renderer>(true))
            {
                if (!result.TryAdd(renderer.gameObject.name, renderer))
                {
                    throw new InvalidOperationException(
                        $"Two renderers are named '{renderer.name}'.");
                }
            }
            return result;
        }

        private static int CountImportedTriangles(
            GameObject model,
            List<string> problems)
        {
            int total = 0;
            MeshFilter[] filters =
                model.GetComponentsInChildren<MeshFilter>(true);
            for (int index = 0; index < filters.Length; index++)
            {
                Mesh mesh = filters[index].sharedMesh;
                if (mesh == null)
                {
                    problems.Add(
                        $"mesh filter '{filters[index].name}' has no mesh");
                    continue;
                }

                for (int subMesh = 0;
                     subMesh < mesh.subMeshCount;
                     subMesh++)
                {
                    if (mesh.GetTopology(subMesh) != MeshTopology.Triangles)
                    {
                        problems.Add(
                            $"mesh '{mesh.name}' submesh {subMesh} is not " +
                            "triangulated");
                        continue;
                    }

                    uint indices = mesh.GetIndexCount(subMesh);
                    if (indices % 3u != 0u)
                    {
                        problems.Add(
                            $"mesh '{mesh.name}' submesh {subMesh} has " +
                            $"{indices} triangle indices");
                        continue;
                    }

                    total += checked((int)(indices / 3u));
                }
            }
            return total;
        }

        private static Rect CalculateUvBounds(Renderer renderer)
        {
            Mesh mesh = RequireRendererMesh(renderer);
            var uvs = new List<Vector2>(mesh.vertexCount);
            mesh.GetUVs(0, uvs);
            if (uvs.Count != mesh.vertexCount || uvs.Count == 0)
            {
                throw new InvalidOperationException(
                    $"Renderer '{renderer.name}' must expose one finite " +
                    "UV0 coordinate per vertex.");
            }

            Vector2 minimum = uvs[0];
            Vector2 maximum = uvs[0];
            for (int index = 0; index < uvs.Count; index++)
            {
                Vector2 uv = uvs[index];
                if (!IsFinite(uv.x) || !IsFinite(uv.y))
                {
                    throw new InvalidOperationException(
                        $"Renderer '{renderer.name}' has a non-finite " +
                        "UV0 coordinate.");
                }

                minimum = Vector2.Min(minimum, uv);
                maximum = Vector2.Max(maximum, uv);
            }

            Rect bounds = Rect.MinMaxRect(
                minimum.x,
                minimum.y,
                maximum.x,
                maximum.y);
            if (bounds.width <= UvTolerance ||
                bounds.height <= UvTolerance)
            {
                throw new InvalidOperationException(
                    $"Renderer '{renderer.name}' UV0 bounds {bounds} " +
                    "cannot be normalized into an atlas cell.");
            }

            return bounds;
        }

        private static Mesh RequireRendererMesh(Renderer renderer)
        {
            if (renderer is SkinnedMeshRenderer skinned &&
                skinned.sharedMesh != null)
            {
                return skinned.sharedMesh;
            }

            MeshFilter filter = renderer.GetComponent<MeshFilter>();
            if (filter != null && filter.sharedMesh != null)
            {
                return filter.sharedMesh;
            }

            throw new InvalidOperationException(
                $"Renderer '{renderer.name}' has no source mesh.");
        }

        private static Dictionary<string, Transform> IndexTransforms(
            GameObject model)
        {
            var result = new Dictionary<string, Transform>(
                StringComparer.Ordinal);
            foreach (Transform transform in
                     model.GetComponentsInChildren<Transform>(true))
            {
                if (!result.TryAdd(transform.name, transform) &&
                    transform.name.StartsWith(
                        "ANCHOR_",
                        StringComparison.Ordinal))
                {
                    throw new InvalidOperationException(
                        $"Two anchors are named '{transform.name}'.");
                }
            }
            return result;
        }

        private static void ValidatePartBindings(
            MothersHouseInteriorAssetRegistry registry,
            MothersHouseManifest manifest,
            IReadOnlyDictionary<string, Renderer> renderers,
            Material sharedLit,
            Material sharedEmission,
            List<string> problems)
        {
            if (registry.Parts.Count != manifest.parts.Length)
            {
                problems.Add(
                    $"registry has {registry.Parts.Count} parts against " +
                    $"manifest {manifest.parts.Length}");
            }

            var byName = manifest.parts.ToDictionary(
                part => part.name,
                StringComparer.Ordinal);
            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (MothersHouseInteriorPartBinding binding in registry.Parts)
            {
                if (binding == null ||
                    !byName.TryGetValue(
                        binding.SourceName,
                        out MothersHouseManifestPart part))
                {
                    problems.Add("registry contains an unknown part");
                    continue;
                }
                if (!seen.Add(binding.SourceName))
                {
                    problems.Add(
                        $"registry repeats part '{binding.SourceName}'");
                }
                if (binding.Renderer == null ||
                    !renderers.TryGetValue(
                        binding.SourceName,
                        out Renderer renderer) ||
                    renderer != binding.Renderer)
                {
                    problems.Add(
                        $"part '{binding.SourceName}' has wrong renderer");
                    continue;
                }
                if (!string.Equals(binding.Role, part.role) ||
                    !string.Equals(binding.Group, part.group) ||
                    !string.Equals(binding.Sheet, part.sheet) ||
                    binding.Emissive != part.emissive ||
                    binding.CastsShadows != part.casts_shadows)
                {
                    problems.Add(
                        $"part '{binding.SourceName}' metadata drifted");
                }
                Material expected = part.emissive
                    ? sharedEmission
                    : sharedLit;
                if (renderer.sharedMaterial != expected)
                {
                    problems.Add(
                        $"part '{binding.SourceName}' uses wrong shared " +
                        "material");
                }

                Rect expectedUvBounds = CalculateUvBounds(renderer);
                if (!RectClose(
                        binding.SourceUvBounds,
                        expectedUvBounds,
                        UvTolerance))
                {
                    problems.Add(
                        $"part '{binding.SourceName}' serialized UV " +
                        $"bounds {binding.SourceUvBounds} against mesh " +
                        $"{expectedUvBounds}");
                }

                MothersHouseInteriorAtlasContract atlas =
                    registry.PositiveAtlas;
                if (atlas == null ||
                    !atlas.TryCreateBaseMapTransform(
                        binding.Sheet,
                        expectedUvBounds,
                        out Vector4 expectedTransform) ||
                    Vector4.Distance(
                        binding.BaseMapTransform,
                        expectedTransform) > UvTolerance ||
                    !atlas.TryGetInsetCellBounds(
                        binding.Sheet,
                        out Rect cellBounds) ||
                    !Contains(
                        cellBounds,
                        binding.TransformedUvBounds,
                        UvTolerance))
                {
                    problems.Add(
                        $"part '{binding.SourceName}' UV transform leaves " +
                        $"its '{binding.Sheet}' atlas cell");
                }
            }
        }

        private static void ValidateAnchorBindings(
            MothersHouseInteriorAssetRegistry registry,
            MothersHouseManifest manifest,
            GameObject prefab,
            List<string> problems)
        {
            if (registry.Anchors.Count != manifest.anchors.Length)
            {
                problems.Add(
                    $"registry has {registry.Anchors.Count} anchors against " +
                    $"manifest {manifest.anchors.Length}");
            }
            foreach (MothersHouseManifestAnchor source in manifest.anchors)
            {
                if (!registry.TryGetAnchor(source.role, out Transform anchor) ||
                    anchor == null)
                {
                    problems.Add(
                        $"anchor role '{source.role}' is not bound");
                    continue;
                }
                Vector3 actual = prefab.transform.InverseTransformPoint(
                    anchor.position);
                Vector3 expected = ReadVector(
                    source.unity_local_position,
                    source.name);
                if (Vector3.Distance(actual, expected) > MeasureTolerance)
                {
                    problems.Add(
                        $"anchor '{source.role}' is at {actual}, " +
                        $"expected {expected}");
                }
                AppendTeapotDockRotationProblem(
                    registry.ModelRoot,
                    anchor,
                    source,
                    problems);
            }
        }

        private static void AssertAnchorPosition(
            Transform root,
            Transform anchor,
            MothersHouseManifestAnchor source)
        {
            Vector3 actual = root.InverseTransformPoint(anchor.position);
            Vector3 expected = ReadVector(
                source.unity_local_position,
                source.name);
            if (Vector3.Distance(actual, expected) > MeasureTolerance)
            {
                throw new InvalidOperationException(
                    $"Imported anchor '{source.name}' is at {actual}, " +
                    $"expected {expected}. Axis conversion or root scale " +
                    "is wrong.");
            }
        }

        private static void AssertTeapotDockRotation(
            Transform root,
            Transform anchor,
            MothersHouseManifestAnchor source)
        {
            if (!string.Equals(
                    source.role,
                    "teapot_dock",
                    StringComparison.Ordinal))
            {
                return;
            }

            if (root == null)
            {
                throw new InvalidOperationException(
                    "The imported authoring root for teapot_dock is " +
                    "missing.");
            }

            float angle = MeasureRelativeRotation(root, anchor);
            if (angle > RotationToleranceDegrees)
            {
                throw new InvalidOperationException(
                    $"Imported anchor '{source.name}' is rotated " +
                    $"{angle:0.###} degrees relative to the room; the " +
                    "table-kettle dock must remain upright and identity.");
            }
        }

        private static void AppendTeapotDockRotationProblem(
            Transform root,
            Transform anchor,
            MothersHouseManifestAnchor source,
            ICollection<string> problems)
        {
            if (!string.Equals(
                    source.role,
                    "teapot_dock",
                    StringComparison.Ordinal))
            {
                return;
            }

            if (root == null)
            {
                problems.Add(
                    "the imported authoring root for teapot_dock is " +
                    "missing");
                return;
            }

            float angle = MeasureRelativeRotation(root, anchor);
            if (angle > RotationToleranceDegrees)
            {
                problems.Add(
                    $"anchor '{source.role}' is rotated {angle:0.###} " +
                    "degrees relative to the room instead of identity");
            }
        }

        private static float MeasureRelativeRotation(
            Transform root,
            Transform anchor)
        {
            Quaternion relative = Quaternion.Inverse(root.rotation) *
                                  anchor.rotation;
            return Quaternion.Angle(relative, Quaternion.identity);
        }

        private static Bounds CalculateLocalBounds(
            Transform root,
            IEnumerable<Renderer> renderers)
        {
            bool hasPoint = false;
            Bounds result = default;
            foreach (Renderer renderer in renderers)
            {
                Bounds world = renderer.bounds;
                Vector3 min = world.min;
                Vector3 max = world.max;
                for (int x = 0; x < 2; x++)
                {
                    for (int y = 0; y < 2; y++)
                    {
                        for (int z = 0; z < 2; z++)
                        {
                            Vector3 worldCorner = new Vector3(
                                x == 0 ? min.x : max.x,
                                y == 0 ? min.y : max.y,
                                z == 0 ? min.z : max.z);
                            Vector3 local = root.InverseTransformPoint(
                                worldCorner);
                            if (!hasPoint)
                            {
                                result = new Bounds(local, Vector3.zero);
                                hasPoint = true;
                            }
                            else
                            {
                                result.Encapsulate(local);
                            }
                        }
                    }
                }
            }

            if (!hasPoint)
            {
                throw new InvalidOperationException(
                    "The imported model has no renderer bounds.");
            }
            return result;
        }

        private static Bounds BoundsFromManifest(
            MothersHouseManifest manifest)
        {
            Vector3 min = ReadVector(
                manifest.unity_bounds_min,
                "unity_bounds_min");
            Vector3 max = ReadVector(
                manifest.unity_bounds_max,
                "unity_bounds_max");
            var bounds = new Bounds();
            bounds.SetMinMax(min, max);
            return bounds;
        }

        private static Transform ResolveAuthoringRoot(Transform model)
        {
            foreach (Transform transform in
                     model.GetComponentsInChildren<Transform>(true))
            {
                if (string.Equals(
                        transform.name,
                        "MothersHouseInterior3D",
                        StringComparison.Ordinal))
                {
                    return transform;
                }
            }

            throw new InvalidOperationException(
                "The imported FBX lost its authored root.");
        }

        private static Color ReadColor(float[] values, string label)
        {
            if (values == null || values.Length != 4 ||
                values.Any(value => !IsFinite(value)))
            {
                throw new InvalidOperationException(
                    $"'{label}' has an invalid RGBA tint.");
            }
            return new Color(values[0], values[1], values[2], values[3]);
        }

        private static Vector3 ReadVector(float[] values, string label)
        {
            if (!IsFiniteVector(values))
            {
                throw new InvalidOperationException(
                    $"'{label}' is not a finite three-component vector.");
            }
            return new Vector3(values[0], values[1], values[2]);
        }

        private static bool IsFiniteVector(float[] values)
        {
            return values != null && values.Length == 3 &&
                IsFinite(values[0]) &&
                IsFinite(values[1]) &&
                IsFinite(values[2]);
        }

        private static bool IsFinite(float value)
        {
            return !float.IsNaN(value) && !float.IsInfinity(value);
        }

        private static bool BoundsClose(Bounds first, Bounds second)
        {
            return Vector3.Distance(first.min, second.min) <=
                       MeasureTolerance &&
                   Vector3.Distance(first.max, second.max) <=
                       MeasureTolerance;
        }

        private static bool RectClose(
            Rect first,
            Rect second,
            float tolerance)
        {
            return Mathf.Abs(first.xMin - second.xMin) <= tolerance &&
                   Mathf.Abs(first.xMax - second.xMax) <= tolerance &&
                   Mathf.Abs(first.yMin - second.yMin) <= tolerance &&
                   Mathf.Abs(first.yMax - second.yMax) <= tolerance;
        }

        private static bool Contains(
            Rect outer,
            Rect inner,
            float tolerance)
        {
            return inner.xMin >= outer.xMin - tolerance &&
                   inner.xMax <= outer.xMax + tolerance &&
                   inner.yMin >= outer.yMin - tolerance &&
                   inner.yMax <= outer.yMax + tolerance;
        }

        private static void AppendDimensionProblems(
            MothersHouseInteriorDimensions actual,
            List<string> problems)
        {
            AppendIfFar(actual.Width, 10f, "room width", problems);
            AppendIfFar(actual.Depth, 8f, "room depth", problems);
            AppendIfFar(actual.Height, 3.4f, "room height", problems);
            AppendIfFar(
                actual.WallThickness,
                0.24f,
                "wall thickness",
                problems);
            AppendIfFar(actual.DoorWidth, 1.3f, "door width", problems);
            AppendIfFar(actual.DoorHeight, 2.2f, "door height", problems);
        }

        private static void AppendIfFar(
            float actual,
            float expected,
            string label,
            List<string> problems)
        {
            if (Mathf.Abs(actual - expected) > 0.001f)
            {
                problems.Add(
                    $"{label} is {actual:0.###}, expected " +
                    $"{expected:0.###}");
            }
        }

        private static void RequireClose(
            float actual,
            float expected,
            string label)
        {
            if (Mathf.Abs(actual - expected) > 0.001f)
            {
                throw new InvalidOperationException(
                    $"Manifest {label} is {actual:0.###}, expected " +
                    $"{expected:0.###}.");
            }
        }

        private static void AppendIfDifferent(
            string actual,
            string expected,
            string label,
            List<string> problems)
        {
            if (!string.Equals(actual, expected, StringComparison.Ordinal))
            {
                problems.Add(
                    $"{label} is '{actual}', expected '{expected}'");
            }
        }

        private static void AppendForbidden<TComponent>(
            GameObject prefab,
            string label,
            List<string> problems)
            where TComponent : Component
        {
            int count = prefab.GetComponentsInChildren<TComponent>(true)
                .Length;
            if (count != 0)
            {
                problems.Add($"authored prefab contains {count} {label}(s)");
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
                string next = current + "/" + segments[index];
                if (!AssetDatabase.IsValidFolder(next))
                {
                    AssetDatabase.CreateFolder(current, segments[index]);
                }
                current = next;
            }
        }

        [Serializable]
        private sealed class MothersHouseManifest
        {
            public string generator_version;
            public string design_id;
            public MothersHouseDimensions dimensions_m;
            public MothersHouseUpperStorey upper_storey_m;
            public float wall_thickness_m;
            public MothersHouseDoor door_opening_m;
            public bool colliders;
            public bool lights;
            public bool cameras;
            public int animation_count;
            public int mesh_count;
            public int triangle_count;
            public float[] unity_bounds_min;
            public float[] unity_bounds_max;
            public MothersHouseKettleContract kettle_contract;
            public MothersHouseManifestAnchor[] anchors;
            public MothersHouseManifestPart[] parts;
            public string build_signature;
        }

        [Serializable]
        private sealed class MothersHouseDimensions
        {
            public float width;
            public float depth;
            public float height;
        }

        [Serializable]
        private sealed class MothersHouseUpperStorey
        {
            public float floor_elevation;
            public float ceiling_height;
            public float stair_width;
            public int stair_step_count;
            public float[] stair_opening;
            public float door_width;
            public float door_height;
            public float[] door_centers_z;
            public int room_count;
            public bool furnished;
        }

        [Serializable]
        private sealed class MothersHouseDoor
        {
            public string wall;
            public float center_x;
            public float width;
            public float height;
        }

        [Serializable]
        private sealed class MothersHouseKettleContract
        {
            public bool geometry_included;
        }

        [Serializable]
        private sealed class MothersHouseManifestAnchor
        {
            public string name;
            public string role;
            public float[] unity_local_position;
        }

        [Serializable]
        private sealed class MothersHouseManifestPart
        {
            public string name;
            public string role;
            public string group;
            public string sheet;
            public bool emissive;
            public bool casts_shadows;
            public float[] tint;
            public int vertices;
            public int triangles;
        }
    }
}

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using BarPromenade;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace BarPromenade.Editor
{
    /// <summary>
    /// Packages the production Hero V2 model, animation bank and prefab.
    /// </summary>
    [InitializeOnLoad]
    public static class Player3DV2AssetSetup
    {
        public const string ModelPath =
            "Assets/Player3D/V2/Models/PlayerCharacter3DV2.fbx";
        public const string ManifestPath =
            "Assets/Player3D/V2/Models/PlayerCharacter3DV2.json";
        public const string AnimationPath =
            "Assets/Player3D/V2/Animations/PlayerCharacter3DV2Animations.fbx";
        public const string AtlasPath =
            "Assets/Player3D/V2/Textures/PlayerFaceAtlas.png";
        public const string ClothingAtlasPath =
            Player3DV2StaticTextureContract.AssetPath;
        public const string ClothingMaterialPath =
            Player3DV2StaticTextureContract.MaterialPath;
        public const string PortraitPath =
            "Assets/Resources/Player/Player3DV2Portrait.png";
        public const string MaterialPath =
            "Assets/Player3D/Materials/Player3DLit.mat";
        public const string PrefabPath =
            "Assets/Resources/Player/Player3DV2.prefab";

        private const int BuildSchemaVersion = 5;
        private const string ExpectedDesignVersion = "HeroV2";
        private const string ExpectedAtlasRenderer = "GEO_FaceSurface";
        private const string ExpectedAtlasOrigin = "bottom_left";
        private const string ExpectedAtlasFilter = "Point";
        private const string SetupScriptPath =
            "Assets/Scripts/Editor/Player3D/Player3DV2AssetSetup.cs";
        private const string ModelImporterScriptPath =
            "Assets/Scripts/Editor/Player3D/Player3DV2ModelImporter.cs";
        private const string TextureImporterScriptPath =
            "Assets/Scripts/Editor/Player3D/Player3DV2TextureImporter.cs";
        private const string StaticTextureContractScriptPath =
            "Assets/Scripts/Editor/Player3D/" +
            "Player3DV2StaticTextureContract.cs";
        private const string RegistryScriptPath =
            "Assets/Scripts/Runtime/Player3D/Player3DAssetRegistry.cs";
        private const float ExpectedHeight = 1.75f;
        private const int MaximumTriangleCount = 4500;
        // Eight columns: the expressions on the left, their soiled twins four
        // columns to the right, so the atlas imports at 512x256.
        private const int ExpectedAtlasColumns = 8;
        private const int ExpectedAtlasRows = 4;
        private const int SoiledAtlasColumnOffset = 4;
        private const int ExpectedAtlasCellSize = 64;
        private const int ExpectedPortraitWidth = 192;
        private const int ExpectedPortraitHeight = 256;
        private const float ExpectedRunDurationSeconds = 0.75f;
        private const int ExpectedRunSourceFrameCount = 18;
        private const float ExpectedRunSourceFps = 24f;

        private static readonly IReadOnlyDictionary<string, string>
            PaletteHex = new Dictionary<string, string>(StringComparer.Ordinal)
            {
                { "MAT_Skin", "AE8D7B" },
                { "MAT_SkinShadow", "67504B" },
                { "MAT_SkinDark", "392D2E" },
                { "MAT_Hair", "08080B" },
                { "MAT_HairHighlight", "202129" },
                { "MAT_Shirt", "2A2D30" },
                { "MAT_Patch", "99743A" },
                { "MAT_EyeWhite", "8F8780" },
                { "MAT_Eye", "141317" },
                { "MAT_Metal", "58514A" },
                { "MAT_JacketAtlas", "FFFFFF" },
                { "MAT_JeansAtlas", "FFFFFF" },
                { "MAT_BandageAtlas", "FFFFFF" },
                // The atlas contains final sRGB face colours. White prevents
                // the registry's _BaseColor property block from tinting it a
                // second time.
                { "MAT_FaceAtlas", "FFFFFF" }
            };

        private static readonly IReadOnlyDictionary<
            string,
            Player3DAnatomicalPart> RequiredBodyParts =
            new Dictionary<string, Player3DAnatomicalPart>(StringComparer.Ordinal)
            {
                { "GEO_Head", Player3DAnatomicalPart.Head },
                { "GEO_Neck", Player3DAnatomicalPart.Neck },
                { "GEO_Torso", Player3DAnatomicalPart.Torso },
                { "GEO_Pelvis", Player3DAnatomicalPart.Pelvis },
                { "GEO_UpperArm.L", Player3DAnatomicalPart.LeftUpperArm },
                { "GEO_Forearm.L", Player3DAnatomicalPart.LeftForearm },
                { "GEO_Hand.L", Player3DAnatomicalPart.LeftHand },
                { "GEO_UpperArm.R", Player3DAnatomicalPart.RightUpperArm },
                { "GEO_Forearm.R", Player3DAnatomicalPart.RightForearm },
                { "GEO_Hand.R", Player3DAnatomicalPart.RightHand },
                { "GEO_Thigh.L", Player3DAnatomicalPart.LeftThigh },
                { "GEO_Shin.L", Player3DAnatomicalPart.LeftShin },
                { "GEO_Foot.L", Player3DAnatomicalPart.LeftFoot },
                { "GEO_Thigh.R", Player3DAnatomicalPart.RightThigh },
                { "GEO_Shin.R", Player3DAnatomicalPart.RightShin },
                { "GEO_Foot.R", Player3DAnatomicalPart.RightFoot }
            };
        private static readonly IReadOnlyDictionary<string, ActionContract>
            RequiredActions =
            new Dictionary<string, ActionContract>(StringComparer.Ordinal)
            {
                { "BedEnter", new ActionContract("bed", false) },
                { "BedExit", new ActionContract("bed", false) },
                { "BedSleepLoop", new ActionContract("bed", true) },
                { "BusAlightExit", new ActionContract("bus_ride", false) },
                { "BusBoardEnter", new ActionContract("bus_ride", false) },
                { "BusRideLoop", new ActionContract("bus_ride", true) },
                { "BarDrinkPickupEnter", new ActionContract("bar_drink", false) },
                { "BarDrinkReturnExit", new ActionContract("bar_drink", false) },
                { "BarDrinkSipLoop", new ActionContract("bar_drink", true) },
                { "CarAlightExit", new ActionContract("car_ride", false) },
                { "CarBoardEnter", new ActionContract("car_ride", false) },
                { "CatFeedEnter", new ActionContract("cat_feeding", false) },
                { "CatFeedExit", new ActionContract("cat_feeding", false) },
                { "CatFeedLoop", new ActionContract("cat_feeding", true) },
                { "ChessSeatEnter", new ActionContract("chess_seat", false) },
                { "ChessSeatExit", new ActionContract("chess_seat", false) },
                { "ChessSeatPlayLoop", new ActionContract("chess_seat", true) },
                { "DoorUseEnter", new ActionContract("door_use", false) },
                { "DoorUseExit", new ActionContract("door_use", false) },
                { "DoorUseLoop", new ActionContract("door_use", true) },
                { "DownLeft", new ActionContract("fall", false) },
                { "DownRight", new ActionContract("fall", false) },
                { "Face_ClosedBlink", new ActionContract("facial", false) },
                { "Face_HalfBlink", new ActionContract("facial", false) },
                { "Face_Neutral", new ActionContract("facial", false) },
                { "Face_Tense", new ActionContract("facial", false) },
                { "Face_Watchful", new ActionContract("facial", false) },
                { "FallLeft", new ActionContract("fall", false) },
                { "FallRight", new ActionContract("fall", false) },
                { "Idle", new ActionContract("locomotion", true) },
                { "Relaxed", new ActionContract("locomotion", false) },
                {
                    "Run",
                    new ActionContract(
                        "locomotion",
                        true,
                        ExpectedRunDurationSeconds,
                        ExpectedRunSourceFrameCount,
                        ExpectedRunSourceFps)
                },
                { "RiseLeft", new ActionContract("fall", false) },
                { "RiseRight", new ActionContract("fall", false) },
                { "SmokeEnter", new ActionContract("smoking", false) },
                { "SmokeExit", new ActionContract("smoking", false) },
                { "SmokeLoop", new ActionContract("smoking", true) },
                { "TurnLeft", new ActionContract("locomotion", true) },
                { "TurnRight", new ActionContract("locomotion", true) },
                { "Walk", new ActionContract("locomotion", true) },
                { "WalkBack", new ActionContract("locomotion", true) }
            };
        private static readonly CanonicalFaceCell[] CanonicalFaceCells =
        {
            new CanonicalFaceCell(PlayerFacialExpression.Neutral, 0, 3),
            new CanonicalFaceCell(PlayerFacialExpression.HalfBlink, 1, 3),
            new CanonicalFaceCell(PlayerFacialExpression.ClosedBlink, 2, 3),
            new CanonicalFaceCell(PlayerFacialExpression.Watchful, 0, 2),
            new CanonicalFaceCell(PlayerFacialExpression.Tense, 1, 2),
            new CanonicalFaceCell(PlayerFacialExpression.Drowsy, 2, 2),
            new CanonicalFaceCell(PlayerFacialExpression.Glazed, 3, 2),
            new CanonicalFaceCell(PlayerFacialExpression.Slack, 0, 1),
            new CanonicalFaceCell(PlayerFacialExpression.Grimace, 1, 1),
            new CanonicalFaceCell(PlayerFacialExpression.TeethDisplay, 2, 1),
            new CanonicalFaceCell(PlayerFacialExpression.Spit, 3, 1),
            new CanonicalFaceCell(
                PlayerFacialExpression.Neutral, SoiledAtlasColumnOffset + 0, 3, true),
            new CanonicalFaceCell(
                PlayerFacialExpression.HalfBlink, SoiledAtlasColumnOffset + 1, 3, true),
            new CanonicalFaceCell(
                PlayerFacialExpression.ClosedBlink, SoiledAtlasColumnOffset + 2, 3, true),
            new CanonicalFaceCell(
                PlayerFacialExpression.Watchful, SoiledAtlasColumnOffset + 0, 2, true),
            new CanonicalFaceCell(
                PlayerFacialExpression.Tense, SoiledAtlasColumnOffset + 1, 2, true),
            new CanonicalFaceCell(
                PlayerFacialExpression.Drowsy, SoiledAtlasColumnOffset + 2, 2, true),
            new CanonicalFaceCell(
                PlayerFacialExpression.Glazed, SoiledAtlasColumnOffset + 3, 2, true),
            new CanonicalFaceCell(
                PlayerFacialExpression.Slack, SoiledAtlasColumnOffset + 0, 1, true),
            new CanonicalFaceCell(
                PlayerFacialExpression.Grimace, SoiledAtlasColumnOffset + 1, 1, true),
            new CanonicalFaceCell(
                PlayerFacialExpression.TeethDisplay, SoiledAtlasColumnOffset + 2, 1, true),
            new CanonicalFaceCell(
                PlayerFacialExpression.Spit, SoiledAtlasColumnOffset + 3, 1, true)
        };

        private static bool isBuilding;
        private static bool buildQueued;

        public static bool IsBuilding => isBuilding;

        static Player3DV2AssetSetup()
        {
            if (!Application.isBatchMode)
            {
                EditorApplication.delayCall += ValidatePrefabDependencyStamp;
            }
        }

        [MenuItem("Bar Promenade/Player 3D/Build Hero V2 Production Prefab")]
        public static void Run()
        {
            BuildOrThrow();
            Debug.Log($"Hero V2 production prefab rebuilt at '{PrefabPath}'.");
        }

        /// <summary>
        /// Command-line entry point:
        /// -executeMethod BarPromenade.Editor.Player3DV2AssetSetup.RunBatch
        /// </summary>
        public static void RunBatch()
        {
            BuildOrThrow();
            Debug.Log($"Hero V2 production prefab rebuilt at '{PrefabPath}'.");
        }

        public static bool SourcesExist()
        {
            return File.Exists(ModelPath) &&
                   File.Exists(ManifestPath) &&
                   File.Exists(AnimationPath) &&
                   File.Exists(AtlasPath) &&
                   File.Exists(ClothingAtlasPath) &&
                   File.Exists(PortraitPath);
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
                    "All Hero V2 model, manifest, animation, face/clothing " +
                    "atlas and portrait sources must exist before the preview " +
                    "prefab is built.");
            }

            isBuilding = true;
            try
            {
                EnsureFolderForAsset(PrefabPath);
                ImportSource(ModelPath);
                ImportSource(ManifestPath);
                ImportSource(AnimationPath);
                ImportSource(AtlasPath);
                ImportSource(ClothingAtlasPath);
                ImportSource(PortraitPath);

                Player3DV2Manifest manifest = LoadAndValidateManifest();
                GameObject modelAsset =
                    AssetDatabase.LoadAssetAtPath<GameObject>(ModelPath);
                Texture2D atlas =
                    AssetDatabase.LoadAssetAtPath<Texture2D>(AtlasPath);
                Texture2D clothingAtlas =
                    AssetDatabase.LoadAssetAtPath<Texture2D>(ClothingAtlasPath);
                Texture2D portrait =
                    AssetDatabase.LoadAssetAtPath<Texture2D>(PortraitPath);
                Material sharedMaterial =
                    AssetDatabase.LoadAssetAtPath<Material>(MaterialPath);

                if (modelAsset == null)
                {
                    throw new InvalidOperationException(
                        $"Unity did not import a model from '{ModelPath}'.");
                }

                if (sharedMaterial == null)
                {
                    throw new InvalidOperationException(
                        "Hero V2 reuses the production Player3DLit material, " +
                        $"but it is missing at '{MaterialPath}'.");
                }

                ValidateTextureAssets(atlas, clothingAtlas, portrait);
                Material clothingMaterial =
                    Player3DV2StaticTextureContract.EnsureSharedMaterial(
                        sharedMaterial,
                        clothingAtlas);
                Player3DAnimationBinding[] animations =
                    LoadAnimationBindings(manifest);
                BuildPrefab(
                    modelAsset,
                    manifest,
                    sharedMaterial,
                    clothingMaterial,
                    atlas,
                    animations);
                AssetDatabase.SaveAssets();
            }
            finally
            {
                isBuilding = false;
            }
        }

        private static void ValidatePrefabDependencyStamp()
        {
            if (!SourcesExist())
            {
                return;
            }

            GameObject prefab =
                AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
            Player3DAssetRegistry registry = prefab != null
                ? prefab.GetComponent<Player3DAssetRegistry>()
                : null;
            Material clothingMaterial =
                AssetDatabase.LoadAssetAtPath<Material>(ClothingMaterialPath);
            Material productionMaterial =
                AssetDatabase.LoadAssetAtPath<Material>(MaterialPath);
            Texture2D clothingAtlas =
                AssetDatabase.LoadAssetAtPath<Texture2D>(ClothingAtlasPath);
            if (registry == null ||
                !Player3DV2StaticTextureContract.IsSharedMaterialCanonical(
                    clothingMaterial,
                    productionMaterial,
                    clothingAtlas) ||
                !string.Equals(
                    registry.BuildSignature,
                    CalculateBuildSignature(),
                    StringComparison.Ordinal))
            {
                QueueBuildWhenSourcesExist();
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
                    $"Could not build Hero V2 production prefab: {exception}");
            }
        }

        private static void ImportSource(string assetPath)
        {
            AssetDatabase.ImportAsset(assetPath, ImportAssetOptions.ForceUpdate);
        }

        private static Player3DV2Manifest LoadAndValidateManifest()
        {
            TextAsset text =
                AssetDatabase.LoadAssetAtPath<TextAsset>(ManifestPath);
            if (text == null)
            {
                throw new InvalidOperationException(
                    $"Could not import Hero V2 manifest '{ManifestPath}'.");
            }

            Player3DV2Manifest manifest =
                JsonUtility.FromJson<Player3DV2Manifest>(text.text);
            if (manifest == null || manifest.parts == null)
            {
                throw new InvalidOperationException(
                    "Hero V2 manifest is malformed or has no parts.");
            }

            if (!string.Equals(
                    manifest.design_version,
                    ExpectedDesignVersion,
                    StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    $"Hero V2 manifest design_version must be " +
                    $"'{ExpectedDesignVersion}', not '{manifest.design_version}'.");
            }

            if (!manifest.runtime_integrated)
            {
                throw new InvalidOperationException(
                    "Hero V2 manifest must be marked runtime_integrated after " +
                    "the isolated Unity packaging pipeline exists.");
            }

            if (!string.Equals(
                    manifest.pose,
                    "apose",
                    StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException(
                    "Hero V2 must use the A-pose as its bind pose.");
            }

            if (Mathf.Abs(manifest.height_m - ExpectedHeight) > 0.001f)
            {
                throw new InvalidOperationException(
                    $"Hero V2 manifest height is {manifest.height_m:F3} m; " +
                    $"expected {ExpectedHeight:F3} m.");
            }

            if (!string.Equals(manifest.forward_axis, "-Y", StringComparison.Ordinal) ||
                !string.Equals(
                    manifest.anatomical_left_axis,
                    "+X",
                    StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    "Hero V2 source axes must be forward -Y and anatomical " +
                    "left +X.");
            }

            if (manifest.mesh_count != manifest.parts.Length)
            {
                throw new InvalidOperationException(
                    $"Hero V2 declares {manifest.mesh_count} meshes but has " +
                    $"{manifest.parts.Length} part records.");
            }

            if (manifest.triangle_count <= 0 ||
                manifest.triangle_count > MaximumTriangleCount)
            {
                throw new InvalidOperationException(
                    $"Hero V2 triangle count {manifest.triangle_count} is " +
                    $"outside 1..{MaximumTriangleCount}.");
            }

            if (manifest.actions == null ||
                manifest.action_count != manifest.actions.Length ||
                manifest.action_count == 0)
            {
                throw new InvalidOperationException(
                    $"Hero V2 declares {manifest.action_count} actions but " +
                    $"has {manifest.actions?.Length ?? 0} records.");
            }

            ValidateParts(manifest);
            Player3DV2StaticTextureContract.ValidateManifest(
                manifest.texture_bindings,
                manifest.parts.ToDictionary(
                    part => part.name,
                    part => part.material,
                    StringComparer.Ordinal));
            ValidateActions(manifest.actions);
            ValidateFaceAtlas(manifest.face_atlas);
            return manifest;
        }

        private static void ValidateParts(Player3DV2Manifest manifest)
        {
            HashSet<string> names = new HashSet<string>(StringComparer.Ordinal);
            Player3DV2ManifestPart faceSurface = null;
            for (int index = 0; index < manifest.parts.Length; index++)
            {
                Player3DV2ManifestPart part = manifest.parts[index];
                if (part == null ||
                    string.IsNullOrWhiteSpace(part.name) ||
                    string.IsNullOrWhiteSpace(part.bone) ||
                    string.IsNullOrWhiteSpace(part.material))
                {
                    throw new InvalidOperationException(
                        $"Hero V2 manifest part {index} is incomplete.");
                }

                if (!names.Add(part.name))
                {
                    throw new InvalidOperationException(
                        $"Hero V2 contains duplicate part '{part.name}'.");
                }

                if (part.material == "MAT_Jacket" ||
                    part.material == "MAT_JacketDark" ||
                    part.material == "MAT_JacketEdge" ||
                    part.material == "MAT_Jeans" ||
                    part.material == "MAT_JeansEdge" ||
                    part.material == "MAT_BootLeather" ||
                    part.material == "MAT_BootSole" ||
                    part.material == "MAT_Bandage" ||
                    part.material == "MAT_BandageDark")
                {
                    throw new InvalidOperationException(
                        $"Hero V2 part '{part.name}' still uses obsolete " +
                        $"solid-colour material '{part.material}'; jacket, " +
                        "trousers and boots must use the full-colour atlas.");
                }

                if (!PaletteHex.ContainsKey(part.material))
                {
                    throw new InvalidOperationException(
                        $"Part '{part.name}' uses unknown palette material " +
                        $"'{part.material}'.");
                }

                if (part.name.IndexOf("Boot", StringComparison.Ordinal) >= 0 ||
                    ((part.bone == "foot.L" || part.bone == "foot.R") &&
                     part.name != "GEO_Foot.L" &&
                     part.name != "GEO_Foot.R"))
                {
                    throw new InvalidOperationException(
                        $"Hero V2 part '{part.name}' is an extra foot-bound " +
                        "mesh; boot laces, eyelets, seams, toe and sole edge " +
                        "must be painted into the GEO_Foot atlas regions.");
                }

                if (part.role == "clothing_detail")
                {
                    throw new InvalidOperationException(
                        $"Hero V2 part '{part.name}' is obsolete protruding " +
                        "clothing detail; paint it into the shared atlas.");
                }

                if (part.name == ExpectedAtlasRenderer)
                {
                    faceSurface = part;
                }
            }

            foreach (string requiredName in RequiredBodyParts.Keys)
            {
                if (!names.Contains(requiredName))
                {
                    throw new InvalidOperationException(
                        $"Hero V2 is missing anatomical mesh '{requiredName}'.");
                }
            }

            if (faceSurface == null ||
                faceSurface.bone != "head" ||
                faceSurface.material != "MAT_FaceAtlas")
            {
                throw new InvalidOperationException(
                    "GEO_FaceSurface must be a head-bound MAT_FaceAtlas part.");
            }
        }

        private static void ValidateActions(Player3DV2ManifestAction[] actions)
        {
            if (actions.Length != RequiredActions.Count)
            {
                throw new InvalidOperationException(
                    $"Hero V2 must preserve the complete {RequiredActions.Count}-" +
                    $"action runtime contract; manifest has {actions.Length}.");
            }

            HashSet<string> names = new HashSet<string>(StringComparer.Ordinal);
            for (int actionIndex = 0;
                 actionIndex < actions.Length;
                 actionIndex++)
            {
                Player3DV2ManifestAction action = actions[actionIndex];
                if (action == null ||
                    string.IsNullOrWhiteSpace(action.name) ||
                    string.IsNullOrWhiteSpace(action.category) ||
                    action.duration_seconds <= 0f ||
                    action.source_frame_count <= 0 ||
                    action.source_fps <= 0f ||
                    Mathf.Abs(action.frame_start) > 0.0001f ||
                    action.frame_end <= action.frame_start)
                {
                    throw new InvalidOperationException(
                        $"Hero V2 action {actionIndex} is incomplete.");
                }

                if (!names.Add(action.name))
                {
                    throw new InvalidOperationException(
                        $"Hero V2 contains duplicate action '{action.name}'.");
                }

                if (!RequiredActions.TryGetValue(
                        action.name,
                        out ActionContract expected) ||
                    !string.Equals(
                        action.category,
                        expected.Category,
                        StringComparison.Ordinal) ||
                    action.loop != expected.Looping)
                {
                    throw new InvalidOperationException(
                        $"Hero V2 action '{action.name}' does not match the " +
                        "production runtime name/category/loop contract.");
                }

                if (action.root_motion || action.event_count != 0)
                {
                    throw new InvalidOperationException(
                        $"Hero V2 action '{action.name}' must remain " +
                        "in-place and free of Animation Events.");
                }

                if (expected.HasExactTiming &&
                    (Mathf.Abs(
                         action.duration_seconds -
                         expected.DurationSeconds) > 0.0001f ||
                     action.source_frame_count != expected.SourceFrameCount ||
                     Mathf.Abs(action.source_fps - expected.SourceFps) >
                         0.0001f ||
                     Mathf.Abs(
                         action.frame_end -
                         expected.SourceFrameCount) > 0.0001f))
                {
                    throw new InvalidOperationException(
                        $"Hero V2 action '{action.name}' must be authored as " +
                        $"{expected.SourceFrameCount} source frames / " +
                        $"{expected.DurationSeconds:F2} s at " +
                        $"{expected.SourceFps:F0} FPS.");
                }

                if (action.name == "Run" &&
                    (!action.bone_only ||
                     !action.in_place ||
                     action.gait_style != "heavy_weary" ||
                     action.landmark_count != 8 ||
                     !action.short_flight))
                {
                    throw new InvalidOperationException(
                        "Hero V2 Run must keep its bone-only heavy-weary " +
                        "eight-landmark gait and short flight phase.");
                }

                ValidateFaceKeys(action);
            }
        }

        private static void ValidateFaceKeys(Player3DV2ManifestAction action)
        {
            Player3DV2ManifestFaceKey[] keys = action.face_keys;
            if (keys == null || keys.Length == 0)
            {
                return;
            }

            if (Mathf.Abs(keys[0].normalized_time) > 0.0001f)
            {
                throw new InvalidOperationException(
                    $"Action '{action.name}' face_keys must start at time 0.");
            }

            float previousTime = -1f;
            for (int index = 0; index < keys.Length; index++)
            {
                Player3DV2ManifestFaceKey key = keys[index];
                if (key == null ||
                    float.IsNaN(key.normalized_time) ||
                    float.IsInfinity(key.normalized_time) ||
                    key.normalized_time < 0f ||
                    key.normalized_time > 1f ||
                    key.normalized_time < previousTime)
                {
                    throw new InvalidOperationException(
                        $"Action '{action.name}' has unsorted or out-of-range " +
                        $"face_keys at index {index}.");
                }

                ParseExpression(key.expression, action.name);
                previousTime = key.normalized_time;
            }
        }

        private static void ValidateFaceAtlas(Player3DV2ManifestFaceAtlas atlas)
        {
            if (atlas == null)
            {
                throw new InvalidOperationException(
                    "Hero V2 manifest has no face_atlas contract.");
            }

            if (atlas.texture_asset != AtlasPath ||
                atlas.renderer != ExpectedAtlasRenderer ||
                atlas.columns != ExpectedAtlasColumns ||
                atlas.rows != ExpectedAtlasRows ||
                atlas.cell_size_px != ExpectedAtlasCellSize ||
                atlas.uv_origin != ExpectedAtlasOrigin ||
                atlas.filter_mode != ExpectedAtlasFilter)
            {
                throw new InvalidOperationException(
                    "Hero V2 face_atlas path/renderer/grid/origin/filter does " +
                    "not match the canonical Unity contract.");
            }

            if (atlas.cells == null ||
                atlas.cells.Length != CanonicalFaceCells.Length)
            {
                throw new InvalidOperationException(
                    $"Hero V2 face_atlas must define exactly " +
                    $"{CanonicalFaceCells.Length} canonical cells.");
            }

            // A face and its soiled twin are two cells of the same
            // expression, so duplicates are keyed by the pair.
            HashSet<(PlayerFacialExpression, bool)> expressions =
                new HashSet<(PlayerFacialExpression, bool)>();
            for (int index = 0; index < atlas.cells.Length; index++)
            {
                Player3DV2ManifestFaceCell cell = atlas.cells[index];
                if (cell == null)
                {
                    throw new InvalidOperationException(
                        $"Hero V2 face_atlas cell {index} is null.");
                }

                PlayerFacialExpression expression =
                    ParseExpression(cell.expression, "face_atlas");
                if (!expressions.Add((expression, cell.soiled)))
                {
                    throw new InvalidOperationException(
                        $"Hero V2 face_atlas duplicates '{expression}' " +
                        $"(soiled: {cell.soiled}).");
                }

                CanonicalFaceCell expected =
                    FindCanonicalCell(expression, cell.soiled);
                if (cell.column != expected.Column ||
                    cell.row != expected.Row)
                {
                    throw new InvalidOperationException(
                        $"Hero V2 face_atlas maps '{expression}' " +
                        $"(soiled: {cell.soiled}) to " +
                        $"({cell.column},{cell.row}); expected " +
                        $"({expected.Column},{expected.Row}).");
                }
            }
        }

        private static PlayerFacialExpression ParseExpression(
            string value,
            string owner)
        {
            if (!Enum.TryParse(
                    value,
                    false,
                    out PlayerFacialExpression expression) ||
                !Enum.IsDefined(typeof(PlayerFacialExpression), expression))
            {
                throw new InvalidOperationException(
                    $"'{owner}' uses unknown facial expression '{value}'.");
            }

            return expression;
        }

        private static CanonicalFaceCell FindCanonicalCell(
            PlayerFacialExpression expression,
            bool soiled)
        {
            for (int index = 0; index < CanonicalFaceCells.Length; index++)
            {
                if (CanonicalFaceCells[index].Expression == expression &&
                    CanonicalFaceCells[index].Soiled == soiled)
                {
                    return CanonicalFaceCells[index];
                }
            }

            throw new InvalidOperationException(
                $"No canonical atlas cell exists for '{expression}' " +
                $"(soiled: {soiled}).");
        }

        private static void ValidateTextureAssets(
            Texture2D atlas,
            Texture2D clothingAtlas,
            Texture2D portrait)
        {
            int atlasWidth = ExpectedAtlasColumns * ExpectedAtlasCellSize;
            int atlasHeight = ExpectedAtlasRows * ExpectedAtlasCellSize;
            if (atlas == null ||
                atlas.width != atlasWidth ||
                atlas.height != atlasHeight)
            {
                throw new InvalidOperationException(
                    $"Hero V2 face atlas must import as {atlasWidth}x" +
                    $"{atlasHeight}, not {atlas?.width ?? 0}x" +
                    $"{atlas?.height ?? 0}.");
            }

            Player3DV2StaticTextureContract.ValidateTexture(clothingAtlas);

            if (portrait == null ||
                portrait.width != ExpectedPortraitWidth ||
                portrait.height != ExpectedPortraitHeight)
            {
                throw new InvalidOperationException(
                    $"Hero V2 portrait must import as {ExpectedPortraitWidth}x" +
                    $"{ExpectedPortraitHeight}, not {portrait?.width ?? 0}x" +
                    $"{portrait?.height ?? 0}.");
            }
        }

        private static Player3DAnimationBinding[] LoadAnimationBindings(
            Player3DV2Manifest manifest)
        {
            Dictionary<string, AnimationClip> clips =
                new Dictionary<string, AnimationClip>(StringComparer.Ordinal);
            UnityEngine.Object[] assets =
                AssetDatabase.LoadAllAssetsAtPath(AnimationPath);
            for (int index = 0; index < assets.Length; index++)
            {
                if (!(assets[index] is AnimationClip clip) ||
                    clip.name.StartsWith("__preview__", StringComparison.Ordinal))
                {
                    continue;
                }

                string clipName = NormalizeClipName(clip.name);
                if (clips.ContainsKey(clipName))
                {
                    throw new InvalidOperationException(
                        $"Hero V2 animation FBX contains duplicate clip " +
                        $"'{clipName}'.");
                }

                clips.Add(clipName, clip);
            }

            if (clips.Count != manifest.action_count)
            {
                throw new InvalidOperationException(
                    $"Unity imported {clips.Count} Hero V2 clips; manifest " +
                    $"declares {manifest.action_count}.");
            }

            Player3DAnimationBinding[] bindings =
                new Player3DAnimationBinding[manifest.actions.Length];
            for (int index = 0; index < manifest.actions.Length; index++)
            {
                Player3DV2ManifestAction action = manifest.actions[index];
                if (!clips.TryGetValue(action.name, out AnimationClip clip))
                {
                    throw new InvalidOperationException(
                        $"Unity import is missing Hero V2 clip '{action.name}'.");
                }

                if (clip.name != action.name)
                {
                    throw new InvalidOperationException(
                        $"Imported clip '{clip.name}' retained an FBX stack " +
                        $"prefix; expected '{action.name}'.");
                }

                if (clip.isLooping != action.loop)
                {
                    throw new InvalidOperationException(
                        $"Imported clip '{action.name}' loop flag differs " +
                        "from the Hero V2 manifest.");
                }

                if (AnimationUtility.GetAnimationEvents(clip).Length != 0)
                {
                    throw new InvalidOperationException(
                        $"Imported Hero V2 clip '{action.name}' must not " +
                        "contain Animation Events.");
                }

                if (Mathf.Abs(clip.length - action.duration_seconds) > 1f / 24f)
                {
                    throw new InvalidOperationException(
                        $"Imported clip '{action.name}' lasts {clip.length:F3} " +
                        $"s; manifest declares {action.duration_seconds:F3} s.");
                }

                bindings[index] = new Player3DAnimationBinding(
                    action.name,
                    action.category,
                    clip,
                    action.duration_seconds,
                    action.loop,
                    BuildExpressionKeys(action.face_keys));
            }

            return bindings;
        }

        private static Player3DFacialExpressionKey[] BuildExpressionKeys(
            Player3DV2ManifestFaceKey[] source)
        {
            if (source == null || source.Length == 0)
            {
                return Array.Empty<Player3DFacialExpressionKey>();
            }

            Player3DFacialExpressionKey[] result =
                new Player3DFacialExpressionKey[source.Length];
            for (int index = 0; index < source.Length; index++)
            {
                result[index] = new Player3DFacialExpressionKey(
                    source[index].normalized_time,
                    ParseExpression(source[index].expression, "face_keys"));
            }

            return result;
        }

        private static void BuildPrefab(
            GameObject modelAsset,
            Player3DV2Manifest manifest,
            Material sharedMaterial,
            Material clothingMaterial,
            Texture2D atlas,
            Player3DAnimationBinding[] animations)
        {
            GameObject prefabRoot = new GameObject("Player3DV2");
            try
            {
                GameObject model =
                    PrefabUtility.InstantiatePrefab(modelAsset) as GameObject;
                if (model == null)
                {
                    throw new InvalidOperationException(
                        "Could not instantiate imported Hero V2 model.");
                }

                model.name = "Model";
                model.transform.SetParent(prefabRoot.transform, false);
                model.transform.localPosition = Vector3.zero;
                model.transform.localRotation = Quaternion.Euler(0f, 180f, 0f);
                model.transform.localScale = Vector3.one;

                Dictionary<string, Renderer> renderersByName = IndexByName(
                    model.GetComponentsInChildren<Renderer>(true),
                    renderer => renderer.name,
                    "renderer");
                Dictionary<string, Transform> transformsByName = IndexByName(
                    model.GetComponentsInChildren<Transform>(true),
                    item => item.name,
                    "transform");

                if (renderersByName.Count != manifest.mesh_count)
                {
                    throw new InvalidOperationException(
                        $"Hero V2 import exposes {renderersByName.Count} " +
                        $"renderers; manifest declares {manifest.mesh_count}.");
                }

                Player3DV2StaticTextureContract.ValidateRendererUvs(
                    manifest.texture_bindings[0],
                    renderersByName);

                List<Player3DMeshBinding> meshBindings =
                    new List<Player3DMeshBinding>(manifest.parts.Length);
                List<Player3DAnatomicalPartBinding> anatomicalBindings =
                    new List<Player3DAnatomicalPartBinding>(
                        RequiredBodyParts.Count);

                for (int index = 0; index < manifest.parts.Length; index++)
                {
                    Player3DV2ManifestPart source = manifest.parts[index];
                    if (!renderersByName.TryGetValue(
                            source.name,
                            out Renderer renderer))
                    {
                        throw new InvalidOperationException(
                            $"Hero V2 import is missing renderer '{source.name}'.");
                    }

                    if (!transformsByName.TryGetValue(
                            source.bone,
                            out Transform bone))
                    {
                        throw new InvalidOperationException(
                            $"Part '{source.name}' references missing bone " +
                            $"'{source.bone}'.");
                    }

                    Material partMaterial =
                        Player3DV2StaticTextureContract.UsesClothingAtlas(
                            source.material)
                            ? clothingMaterial
                            : sharedMaterial;
                    renderer.sharedMaterials = Enumerable
                        .Repeat(
                            partMaterial,
                            Math.Max(1, renderer.sharedMaterials.Length))
                        .ToArray();
                    renderer.shadowCastingMode = ShadowCastingMode.On;
                    renderer.receiveShadows = true;
                    renderer.motionVectorGenerationMode =
                        MotionVectorGenerationMode.Object;

                    if (source.name == "GEO_Torso" ||
                        source.name == "CLO_JacketBody")
                    {
                        ValidateTorsoSkinning(renderer);
                    }

                    Player3DMeshBinding meshBinding = new Player3DMeshBinding(
                        source.name,
                        source.role,
                        source.bone,
                        source.sprite_part,
                        source.side,
                        source.material,
                        renderer,
                        bone,
                        ParsePaletteColor(source.material));
                    meshBindings.Add(meshBinding);

                    if (RequiredBodyParts.TryGetValue(
                            source.name,
                            out Player3DAnatomicalPart anatomicalPart))
                    {
                        anatomicalBindings.Add(
                            new Player3DAnatomicalPartBinding(
                                anatomicalPart,
                                renderer,
                                bone));
                    }
                }

                anatomicalBindings.Sort(
                    (left, right) => left.Part.CompareTo(right.Part));

                Animator animator = model.GetComponentInChildren<Animator>(true);
                if (animator == null)
                {
                    animator = model.AddComponent<Animator>();
                }

                if (animator.avatar == null)
                {
                    animator.avatar = FindModelAvatar();
                }

                if (animator.avatar == null || !animator.avatar.isValid)
                {
                    throw new InvalidOperationException(
                        "Imported Hero V2 model has no valid Generic Avatar.");
                }

                animator.applyRootMotion = false;
                animator.cullingMode = AnimatorCullingMode.CullUpdateTransforms;

                Transform head = RequireTransform(transformsByName, "head");
                Transform chest = RequireTransform(transformsByName, "chest");
                Transform spine = RequireTransform(transformsByName, "spine");
                Transform pelvis = RequireTransform(transformsByName, "pelvis");
                if (spine.parent != pelvis || chest.parent != spine)
                {
                    throw new InvalidOperationException(
                        "Hero V2 must preserve the pelvis -> spine -> chest " +
                        "chain shared by its production animation Avatar.");
                }
                Transform leftFoot = RequireTransform(transformsByName, "foot.L");
                Transform rightFoot = RequireTransform(transformsByName, "foot.R");
                Transform leftGrip = FindOptionalTransform(
                    transformsByName,
                    "SOCKET_Grip.L",
                    "hand.L");
                Transform rightGrip = FindOptionalTransform(
                    transformsByName,
                    "SOCKET_Grip.R",
                    "hand.R");
                Transform leftVessel = RequireTransform(
                    transformsByName,
                    "SOCKET_Vessel.L");
                Transform rightCigarette = RequireTransform(
                    transformsByName,
                    "SOCKET_Cigarette.R");
                Transform mouth = FindOptionalTransform(
                    transformsByName,
                    "SOCKET_Mouth",
                    "head");

                Renderer faceRenderer =
                    renderersByName[ExpectedAtlasRenderer];
                Mesh faceMesh = GetRendererMesh(faceRenderer);
                if (faceMesh == null ||
                    !faceMesh.HasVertexAttribute(VertexAttribute.TexCoord0))
                {
                    throw new InvalidOperationException(
                        "GEO_FaceSurface must import a local 0..1 UV0 channel.");
                }

                ValidateFaceUvRange(faceMesh);

                Player3DFaceAtlasBinding faceAtlas =
                    BuildFaceAtlasBinding(
                        manifest.face_atlas,
                        faceRenderer,
                        atlas);
                Renderer[] rendererArray = meshBindings
                    .Select(binding => binding.Renderer)
                    .ToArray();
                Bounds localBounds = CalculateLocalBounds(
                    prefabRoot.transform,
                    rendererArray);
                ValidateImportedGeometry(localBounds, manifest, rendererArray);

                Player3DAssetRegistry registry =
                    prefabRoot.AddComponent<Player3DAssetRegistry>();
                registry.Configure(
                    animator,
                    model.transform,
                    rendererArray,
                    meshBindings.ToArray(),
                    anatomicalBindings.ToArray(),
                    animations,
                    new Player3DBoneAnchors(
                        head,
                        chest,
                        pelvis,
                        leftFoot,
                        rightFoot,
                        leftGrip,
                        rightGrip,
                        leftVessel,
                        rightCigarette,
                        mouth,
                        spine),
                    new Player3DMetrics(
                        manifest.height_m,
                        localBounds,
                        Vector3.forward),
                    manifest.generator_version,
                    manifest.pose,
                    manifest.triangle_count,
                    CalculateBuildSignature(),
                    faceAtlas);
                registry.ApplyPalette();

                GameObject saved = PrefabUtility.SaveAsPrefabAsset(
                    prefabRoot,
                    PrefabPath,
                    out bool success);
                if (!success || saved == null)
                {
                    throw new InvalidOperationException(
                        $"Could not save Hero V2 prefab at '{PrefabPath}'.");
                }
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(prefabRoot);
            }
        }

        private static void ValidateTorsoSkinning(Renderer renderer)
        {
            if (!(renderer is SkinnedMeshRenderer skinned) ||
                skinned.sharedMesh == null)
            {
                throw new InvalidOperationException(
                    $"Hero V2 '{renderer.name}' must remain a skinned torso.");
            }

            Transform[] bones = skinned.bones;
            BoneWeight[] weights = skinned.sharedMesh.boneWeights;
            HashSet<string> usedBones = new HashSet<string>(StringComparer.Ordinal);
            int blendedTransitions = 0;
            if (weights.Length != skinned.sharedMesh.vertexCount)
            {
                throw new InvalidOperationException(
                    $"Hero V2 '{renderer.name}' has missing vertex weights.");
            }

            foreach (BoneWeight weight in weights)
            {
                if (weight.weight0 <= 0f || weight.weight1 < 0f ||
                    weight.weight2 != 0f || weight.weight3 != 0f ||
                    Mathf.Abs(weight.weight0 + weight.weight1 - 1f) > 0.0001f)
                {
                    throw new InvalidOperationException(
                        $"Hero V2 '{renderer.name}' must use normalized " +
                        "weights with at most two influences per vertex.");
                }

                string first = RequireTorsoInfluence(bones, weight.boneIndex0);
                usedBones.Add(first);
                if (weight.weight1 <= 0f)
                {
                    continue;
                }

                string second = RequireTorsoInfluence(bones, weight.boneIndex1);
                usedBones.Add(second);
                if (first == second ||
                    (first != "spine" && second != "spine"))
                {
                    throw new InvalidOperationException(
                        $"Hero V2 '{renderer.name}' may only blend adjacent " +
                        "pelvis/spine or spine/chest influences.");
                }

                blendedTransitions |= first == "pelvis" || second == "pelvis"
                    ? 1 : 2;
            }

            if (blendedTransitions != 3 ||
                !usedBones.SetEquals(new[] { "pelvis", "spine", "chest" }))
            {
                throw new InvalidOperationException(
                    $"Hero V2 '{renderer.name}' must deform through the " +
                    "pelvis, lower spine and chest, with blended transitions.");
            }

            // Keep the two authored influences even if a quality preset uses
            // single-bone skinning. Other parts retain their rigid weights.
            skinned.quality = SkinQuality.Bone2;
        }

        private static string RequireTorsoInfluence(
            Transform[] bones,
            int index)
        {
            if (index < 0 || index >= bones.Length || bones[index] == null ||
                (bones[index].name != "pelvis" &&
                 bones[index].name != "spine" &&
                 bones[index].name != "chest"))
            {
                throw new InvalidOperationException(
                    "Hero V2 torso weights reference a missing or " +
                    "non-torso bone.");
            }

            return bones[index].name;
        }

        private static Player3DFaceAtlasBinding BuildFaceAtlasBinding(
            Player3DV2ManifestFaceAtlas source,
            Renderer renderer,
            Texture2D atlas)
        {
            Player3DFaceAtlasCell[] cells =
                new Player3DFaceAtlasCell[source.cells.Length];
            for (int index = 0; index < source.cells.Length; index++)
            {
                Player3DV2ManifestFaceCell cell = source.cells[index];
                cells[index] = new Player3DFaceAtlasCell(
                    ParseExpression(cell.expression, "face_atlas"),
                    cell.column,
                    cell.row,
                    cell.soiled);
            }

            return new Player3DFaceAtlasBinding(
                renderer,
                atlas,
                source.columns,
                source.rows,
                cells);
        }

        private static void ValidateFaceUvRange(Mesh faceMesh)
        {
            Vector2[] uv = faceMesh.uv;
            if (uv == null || uv.Length != faceMesh.vertexCount || uv.Length == 0)
            {
                throw new InvalidOperationException(
                    "GEO_FaceSurface UV0 must contain one coordinate per vertex.");
            }

            Vector2 minimum = uv[0];
            Vector2 maximum = uv[0];
            for (int index = 0; index < uv.Length; index++)
            {
                Vector2 point = uv[index];
                if (point.x < -0.0001f ||
                    point.x > 1.0001f ||
                    point.y < -0.0001f ||
                    point.y > 1.0001f)
                {
                    throw new InvalidOperationException(
                        $"GEO_FaceSurface UV0[{index}]={point} lies outside " +
                        "the promised local 0..1 range.");
                }

                minimum = Vector2.Min(minimum, point);
                maximum = Vector2.Max(maximum, point);
            }

            if (Vector2.Distance(minimum, Vector2.zero) > 0.001f ||
                Vector2.Distance(maximum, Vector2.one) > 0.001f)
            {
                throw new InvalidOperationException(
                    $"GEO_FaceSurface UV0 must span the complete local 0..1 " +
                    $"square; imported range is {minimum}..{maximum}.");
            }
        }

        private static void ValidateImportedGeometry(
            Bounds localBounds,
            Player3DV2Manifest manifest,
            IReadOnlyList<Renderer> renderers)
        {
            if (Mathf.Abs(localBounds.size.y - manifest.height_m) > 0.035f)
            {
                throw new InvalidOperationException(
                    $"Imported Hero V2 height is {localBounds.size.y:F3} m; " +
                    $"manifest declares {manifest.height_m:F3} m.");
            }

            if (Mathf.Abs(localBounds.min.y) > 0.025f)
            {
                throw new InvalidOperationException(
                    $"Imported Hero V2 feet are at local Y " +
                    $"{localBounds.min.y:F3}, expected ground Y=0.");
            }

            int triangleCount = 0;
            for (int index = 0; index < renderers.Count; index++)
            {
                Mesh mesh = GetRendererMesh(renderers[index]);
                if (mesh == null)
                {
                    throw new InvalidOperationException(
                        $"Renderer '{renderers[index].name}' has no mesh.");
                }

                for (int subMesh = 0; subMesh < mesh.subMeshCount; subMesh++)
                {
                    triangleCount += (int)(mesh.GetIndexCount(subMesh) / 3);
                }
            }

            if (triangleCount != manifest.triangle_count)
            {
                throw new InvalidOperationException(
                    $"Hero V2 meshes contain {triangleCount} triangles; " +
                    $"manifest declares {manifest.triangle_count}.");
            }
        }

        private static Mesh GetRendererMesh(Renderer renderer)
        {
            if (renderer is SkinnedMeshRenderer skinned)
            {
                return skinned.sharedMesh;
            }

            MeshFilter filter = renderer.GetComponent<MeshFilter>();
            return filter != null ? filter.sharedMesh : null;
        }

        private static string CalculateBuildSignature()
        {
            string[] inputs =
            {
                BuildSchemaVersion.ToString(),
                DependencyStamp(ModelPath),
                DependencyStamp(ManifestPath),
                DependencyStamp(AnimationPath),
                DependencyStamp(AtlasPath),
                DependencyStamp(ClothingAtlasPath),
                DependencyStamp(PortraitPath),
                DependencyStamp(MaterialPath),
                DependencyStamp(SetupScriptPath),
                DependencyStamp(ModelImporterScriptPath),
                DependencyStamp(TextureImporterScriptPath),
                DependencyStamp(StaticTextureContractScriptPath),
                DependencyStamp(RegistryScriptPath)
            };
            return Hash128.Compute(string.Join("|", inputs)).ToString();
        }

        private static string DependencyStamp(string assetPath)
        {
            return AssetDatabase.GetAssetDependencyHash(assetPath).ToString();
        }

        private static Avatar FindModelAvatar()
        {
            UnityEngine.Object[] assets =
                AssetDatabase.LoadAllAssetsAtPath(ModelPath);
            for (int index = 0; index < assets.Length; index++)
            {
                if (assets[index] is Avatar avatar)
                {
                    return avatar;
                }
            }

            return null;
        }

        private static Bounds CalculateLocalBounds(
            Transform root,
            IReadOnlyList<Renderer> renderers)
        {
            if (renderers.Count == 0)
            {
                throw new InvalidOperationException(
                    "Hero V2 contains no renderers.");
            }

            Bounds result = default;
            bool initialized = false;
            for (int index = 0; index < renderers.Count; index++)
            {
                Renderer renderer = renderers[index];
                Mesh mesh = GetRendererMesh(renderer);
                if (mesh == null)
                {
                    throw new InvalidOperationException(
                        $"Renderer '{renderer.name}' has no mesh.");
                }

                Bounds meshBounds = mesh.bounds;
                Matrix4x4 rendererToRoot =
                    root.worldToLocalMatrix * renderer.localToWorldMatrix;
                for (int corner = 0; corner < 8; corner++)
                {
                    Vector3 point = new Vector3(
                        (corner & 1) == 0 ? meshBounds.min.x : meshBounds.max.x,
                        (corner & 2) == 0 ? meshBounds.min.y : meshBounds.max.y,
                        (corner & 4) == 0 ? meshBounds.min.z : meshBounds.max.z);
                    point = rendererToRoot.MultiplyPoint3x4(point);
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

            return result;
        }

        private static Dictionary<string, T> IndexByName<T>(
            IEnumerable<T> items,
            Func<T, string> getName,
            string itemLabel)
        {
            Dictionary<string, T> result =
                new Dictionary<string, T>(StringComparer.Ordinal);
            foreach (T item in items)
            {
                string name = getName(item);
                if (result.ContainsKey(name))
                {
                    throw new InvalidOperationException(
                        $"Hero V2 hierarchy contains duplicate {itemLabel} " +
                        $"name '{name}'.");
                }

                result.Add(name, item);
            }

            return result;
        }

        private static Transform RequireTransform(
            IReadOnlyDictionary<string, Transform> transforms,
            string name)
        {
            if (transforms.TryGetValue(name, out Transform result))
            {
                return result;
            }

            throw new InvalidOperationException(
                $"Hero V2 hierarchy is missing bone '{name}'.");
        }

        private static Transform FindOptionalTransform(
            IReadOnlyDictionary<string, Transform> transforms,
            string preferredName,
            string fallbackName)
        {
            return transforms.TryGetValue(preferredName, out Transform preferred)
                ? preferred
                : RequireTransform(transforms, fallbackName);
        }

        private static Color ParsePaletteColor(string materialName)
        {
            string hex = PaletteHex[materialName];
            if (!ColorUtility.TryParseHtmlString($"#{hex}", out Color color))
            {
                throw new InvalidOperationException(
                    $"Invalid Hero V2 palette colour '{hex}'.");
            }

            return color;
        }

        private static string NormalizeClipName(string sourceName)
        {
            int separator = sourceName.LastIndexOf('|');
            return separator >= 0 && separator + 1 < sourceName.Length
                ? sourceName.Substring(separator + 1)
                : sourceName;
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

        private readonly struct CanonicalFaceCell
        {
            public CanonicalFaceCell(
                PlayerFacialExpression expression,
                int column,
                int row,
                bool soiled = false)
            {
                Expression = expression;
                Column = column;
                Row = row;
                Soiled = soiled;
            }

            public PlayerFacialExpression Expression { get; }
            public int Column { get; }
            public int Row { get; }
            public bool Soiled { get; }
        }

        private readonly struct ActionContract
        {
            public ActionContract(
                string category,
                bool looping,
                float durationSeconds = 0f,
                int sourceFrameCount = 0,
                float sourceFps = 0f)
            {
                Category = category;
                Looping = looping;
                DurationSeconds = durationSeconds;
                SourceFrameCount = sourceFrameCount;
                SourceFps = sourceFps;
            }

            public string Category { get; }
            public bool Looping { get; }
            public float DurationSeconds { get; }
            public int SourceFrameCount { get; }
            public float SourceFps { get; }
            public bool HasExactTiming => SourceFrameCount > 0;
        }

        [Serializable]
        private sealed class Player3DV2Manifest
        {
            public string generator_version;
            public string design_version;
            public bool runtime_integrated;
            public float height_m;
            public string pose;
            public string forward_axis;
            public string anatomical_left_axis;
            public int mesh_count;
            public int triangle_count;
            public int action_count;
            public Player3DV2ManifestPart[] parts;
            public Player3DV2ManifestAction[] actions;
            public Player3DV2ManifestFaceAtlas face_atlas;
            public Player3DV2ManifestTextureBinding[] texture_bindings;
        }

        [Serializable]
        private sealed class Player3DV2ManifestPart
        {
            public string name;
            public string role;
            public string bone;
            public string sprite_part;
            public string side;
            public string material;
        }

        [Serializable]
        private sealed class Player3DV2ManifestAction
        {
            public string name;
            public string category;
            public float duration_seconds;
            public bool loop;
            public int source_frame_count;
            public float source_fps;
            public float frame_start;
            public float frame_end;
            public bool root_motion;
            public int event_count;
            public bool bone_only;
            public bool in_place;
            public string gait_style;
            public int landmark_count;
            public bool short_flight;
            public Player3DV2ManifestFaceKey[] face_keys;
        }

        [Serializable]
        private sealed class Player3DV2ManifestFaceKey
        {
            public float normalized_time;
            public string expression;
        }

        [Serializable]
        private sealed class Player3DV2ManifestFaceAtlas
        {
            public string texture_asset;
            public string renderer;
            public int columns;
            public int rows;
            public int cell_size_px;
            public string uv_origin;
            public string filter_mode;
            public Player3DV2ManifestFaceCell[] cells;
        }

        [Serializable]
        private sealed class Player3DV2ManifestFaceCell
        {
            public string expression;
            public int column;
            public int row;
            public bool soiled;
        }
    }
}

using System;
using System.Collections.Generic;
using BarPromenade;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace BarPromenade.Tests.EditMode
{
    public sealed class Player3DV2AssetPipelineTests
    {
        private const string V2ModelPath =
            "Assets/Player3D/V2/Models/PlayerCharacter3DV2.fbx";
        private const string V2ManifestPath =
            "Assets/Player3D/V2/Models/PlayerCharacter3DV2.json";
        private const string V2AnimationPath =
            "Assets/Player3D/V2/Animations/PlayerCharacter3DV2Animations.fbx";
        private const string V2AtlasPath =
            "Assets/Player3D/V2/Textures/PlayerFaceAtlas.png";
        private const string V2ClothingAtlasPath =
            "Assets/Player3D/V2/Textures/PlayerClothingAtlas.png";
        private const string V2ClothingMaterialPath =
            "Assets/Player3D/V2/Materials/Player3DV2Clothing.mat";
        private const string ProductionMaterialPath =
            "Assets/Player3D/Materials/Player3DLit.mat";
        private const string V2PortraitPath =
            "Assets/Resources/Player/Player3DV2Portrait.png";
        private const string V2PrefabPath =
            "Assets/Resources/Player/Player3DV2.prefab";

        [Test]
        public void ExplicitV2BuildEntryPoint_RemainsPublicAndCallable()
        {
            Type setup = Type.GetType(
                "BarPromenade.Editor.Player3DV2AssetSetup, " +
                "BarPromenade.Editor");
            Assert.That(setup, Is.Not.Null);
            var runBatch = setup.GetMethod("RunBatch", Type.EmptyTypes);
            var buildOrThrow = setup.GetMethod("BuildOrThrow", Type.EmptyTypes);
            Assert.That(runBatch, Is.Not.Null);
            Assert.That(runBatch.IsPublic, Is.True);
            Assert.That(runBatch.IsStatic, Is.True);
            Assert.That(runBatch.ReturnType, Is.EqualTo(typeof(void)));
            Assert.That(buildOrThrow, Is.Not.Null);
            Assert.That(buildOrThrow.IsPublic, Is.True);
            Assert.That(buildOrThrow.IsStatic, Is.True);
        }

        [Test]
        public void ProductionResources_ContainOnlyHeroV2()
        {
            Assert.That(
                Player3DResources.PrefabResourcePath,
                Is.EqualTo("Player/Player3DV2"));

            GameObject defaultPrefab = Player3DResources.LoadPrefab();
            Assert.That(defaultPrefab, Is.Not.Null);
            Assert.That(
                AssetDatabase.GetAssetPath(defaultPrefab),
                Is.EqualTo(V2PrefabPath));

            string[] retiredV1Assets =
            {
                "Assets/Resources/Player/Player3D.prefab",
                "Assets/Resources/Player/Player3DPortrait.png",
                "Assets/Player3D/Models/PlayerCharacter3D.fbx",
                "Assets/Player3D/Models/PlayerCharacter3D.json",
                "Assets/Player3D/Animations/PlayerCharacter3DAnimations.fbx"
            };
            for (int index = 0; index < retiredV1Assets.Length; index++)
            {
                Assert.That(
                    AssetDatabase.LoadMainAssetAtPath(retiredV1Assets[index]),
                    Is.Null,
                    $"Retired Hero V1 asset is still packaged: " +
                    retiredV1Assets[index]);
            }

            Assert.That(Resources.Load<GameObject>("Player/Player3D"), Is.Null);
        }

        [Test]
        public void PlayerFactory_CreatesProductionV2()
        {
            RequireGeneratedSources();

            GameObject root = new GameObject("Hero variant factory test");
            GameObject cameraObject = new GameObject("Hero variant test camera");
            PlayerRuntime defaultPlayer = default;
            try
            {
                Camera camera = cameraObject.AddComponent<Camera>();
                camera.enabled = false;
                defaultPlayer = PlayerFactory.Create(
                    root.transform,
                    Vector3.zero,
                    camera,
                    null,
                    null);
                Player3DAssetRegistry defaultRegistry =
                    defaultPlayer.GameObject.GetComponentInChildren<
                        Player3DAssetRegistry>(true);
                Assert.That(defaultRegistry, Is.Not.Null);
                Assert.That(defaultRegistry.HasFaceAtlas, Is.True);

            }
            finally
            {
                if (defaultPlayer.GameObject != null)
                {
                    UnityEngine.Object.DestroyImmediate(
                        defaultPlayer.GameObject);
                }

                UnityEngine.Object.DestroyImmediate(cameraObject);
                UnityEngine.Object.DestroyImmediate(root);
            }
        }

        [Test]
        public void BedTimingConstants_ReproduceTheAuthoredClipDurations()
        {
            V2Manifest manifest = LoadManifest();
            AssertRuntimeTiming(
                manifest,
                "BedEnter",
                HomeBedInteraction.BedEnterFrameCount,
                HomeBedInteraction.BedTransitionFramesPerSecond);
            AssertRuntimeTiming(
                manifest,
                "BedSleepLoop",
                HomeBedInteraction.SleepLoopFrameCount,
                HomeBedInteraction.SleepLoopFramesPerSecond);
            AssertRuntimeTiming(
                manifest,
                "BedExit",
                HomeBedInteraction.BedExitFrameCount,
                HomeBedInteraction.BedTransitionFramesPerSecond);
        }

        [Test]
        public void DoorTimingConstants_ReproduceTheAuthoredClipDurations()
        {
            V2Manifest manifest = LoadManifest();
            AssertRuntimeTiming(
                manifest,
                "DoorUseEnter",
                PlayerDoorActionController.EnterFrameCount,
                PlayerDoorActionController.TransitionFramesPerSecond);
            AssertRuntimeTiming(
                manifest,
                "DoorUseLoop",
                PlayerDoorActionController.LoopFrameCount,
                PlayerDoorActionController.LoopFramesPerSecond);
            AssertRuntimeTiming(
                manifest,
                "DoorUseExit",
                PlayerDoorActionController.ExitFrameCount,
                PlayerDoorActionController.TransitionFramesPerSecond);
        }

        [Test]
        public void BedContract_MatchesTheMeasuredGeneratorValues()
        {
            V2ManifestBedContract bed = LoadManifest().bed_contract;
            Assert.That(
                bed,
                Is.Not.Null,
                "The manifest must publish the measured bed contract.");

            AssertMirrored(
                "supine pelvis support",
                PlayerCharacterDimensions.SupinePelvisSupportOffset,
                bed.supine_pelvis_offset_m);
            AssertMirrored(
                "supine head support",
                PlayerCharacterDimensions.SupineHeadSupportOffset,
                bed.supine_head_offset_m);
            AssertMirrored(
                "seated pelvis lift",
                PlayerCharacterDimensions.SeatedPelvisSupportOffset,
                bed.seated_pelvis_lift_m);
            AssertMirrored(
                "mattress height",
                HomeInteriorWorldBuilder.BedMattressSurfaceHeight,
                bed.mattress_above_floor_m);
            AssertMirrored(
                "enter seat arrival",
                HomeBedInteractionPlan.EnterSeatArrivalProgress,
                bed.enter_seat_arrival);
            AssertMirrored(
                "enter seat departure",
                HomeBedInteractionPlan.EnterSeatDepartureProgress,
                bed.enter_seat_departure);
            AssertMirrored(
                "exit seat arrival",
                HomeBedInteractionPlan.ExitSeatArrivalProgress,
                bed.exit_seat_arrival);
            AssertMirrored(
                "exit seat departure",
                HomeBedInteractionPlan.ExitSeatDepartureProgress,
                bed.exit_seat_departure);
        }

        [Test]
        public void GeneratedV2_UsesOwnAvatarAndCanonicalFacialAtlas()
        {
            RequireGeneratedSources();

            TextAsset manifestAsset =
                AssetDatabase.LoadAssetAtPath<TextAsset>(V2ManifestPath);
            V2Manifest manifest =
                JsonUtility.FromJson<V2Manifest>(manifestAsset.text);
            AssertManifestContract(manifest);
            AssertModelImport();
            AssertAnimationImport(manifest);
            AssertTextureImport(V2AtlasPath, false);
            AssertTextureImport(V2ClothingAtlasPath, false);
            AssertTextureImport(V2PortraitPath, true);

            Texture2D atlas =
                AssetDatabase.LoadAssetAtPath<Texture2D>(V2AtlasPath);
            Texture2D portrait =
                AssetDatabase.LoadAssetAtPath<Texture2D>(V2PortraitPath);
            Texture2D clothingAtlas =
                AssetDatabase.LoadAssetAtPath<Texture2D>(V2ClothingAtlasPath);
            Assert.That(atlas.width, Is.EqualTo(256));
            Assert.That(atlas.height, Is.EqualTo(256));
            Assert.That(portrait.width, Is.EqualTo(192));
            Assert.That(portrait.height, Is.EqualTo(256));
            Assert.That(clothingAtlas.width, Is.EqualTo(256));
            Assert.That(clothingAtlas.height, Is.EqualTo(256));
            Assert.That(
                Resources.Load<Texture2D>(
                    "Player/Player3DV2Portrait"),
                Is.SameAs(portrait));

            GameObject prefab = Player3DResources.LoadPrefab();
            Assert.That(prefab, Is.Not.Null);
            Assert.That(AssetDatabase.GetAssetPath(prefab), Is.EqualTo(V2PrefabPath));

            GameObject owner = new GameObject("Hero V2 test owner");
            Player3DAssetRegistry directRegistry =
                Player3DResources.Instantiate(owner.transform);
            try
            {
                Player3DAssetRegistry registry = directRegistry;
                Assert.That(registry, Is.Not.Null);
                Assert.That(registry.HasFaceAtlas, Is.True);
                Assert.That(registry.FaceAtlas.Renderer.name, Is.EqualTo("GEO_FaceSurface"));
                Assert.That(registry.FaceAtlas.Texture, Is.SameAs(atlas));
                Assert.That(registry.FaceAtlas.Columns, Is.EqualTo(4));
                Assert.That(registry.FaceAtlas.Rows, Is.EqualTo(4));
                Assert.That(registry.FaceAtlas.Cells.Count, Is.EqualTo(9));
                Assert.That(registry.Anchors.LeftVessel, Is.Not.Null);
                Assert.That(
                    registry.Anchors.LeftVessel.name,
                    Is.EqualTo("SOCKET_Vessel.L"));

                AssertAtlasTransform(
                    registry.FaceAtlas,
                    PlayerFacialExpression.Neutral,
                    0,
                    3);
                AssertAtlasTransform(
                    registry.FaceAtlas,
                    PlayerFacialExpression.HalfBlink,
                    1,
                    3);
                AssertAtlasTransform(
                    registry.FaceAtlas,
                    PlayerFacialExpression.ClosedBlink,
                    2,
                    3);
                AssertAtlasTransform(
                    registry.FaceAtlas,
                    PlayerFacialExpression.Watchful,
                    0,
                    2);
                AssertAtlasTransform(
                    registry.FaceAtlas,
                    PlayerFacialExpression.Tense,
                    1,
                    2);
                AssertAtlasTransform(
                    registry.FaceAtlas,
                    PlayerFacialExpression.Drowsy,
                    2,
                    2);
                AssertAtlasTransform(
                    registry.FaceAtlas,
                    PlayerFacialExpression.Glazed,
                    3,
                    2);
                AssertAtlasTransform(
                    registry.FaceAtlas,
                    PlayerFacialExpression.Slack,
                    0,
                    1);
                AssertAtlasTransform(
                    registry.FaceAtlas,
                    PlayerFacialExpression.Grimace,
                    1,
                    1);

                Player3DMeshBinding faceBinding =
                    FindBinding(registry, "GEO_FaceSurface");
                Assert.That(faceBinding.BoneName, Is.EqualTo("head"));
                Assert.That(faceBinding.Bone, Is.SameAs(registry.Anchors.Head));
                Assert.That(
                    faceBinding.PaletteMaterialName,
                    Is.EqualTo("MAT_FaceAtlas"));
                Assert.That(faceBinding.BaseColor, Is.EqualTo(Color.white));
                AssertNeutralAtlasBootstrapped(faceBinding.Renderer, atlas);

                Mesh faceMesh = GetMesh(faceBinding.Renderer);
                Assert.That(faceMesh, Is.Not.Null);
                Assert.That(
                    faceMesh.HasVertexAttribute(VertexAttribute.TexCoord0),
                    Is.True,
                    "GEO_FaceSurface must retain its authored local UV0.");
                AssertFaceUvRange(faceMesh);
                AssertFacePointsAlongDeclaredForward(registry, faceBinding);
                AssertFacialKeys(registry, manifest);
                AssertStaticTextureBindings(
                    registry,
                    manifest,
                    clothingAtlas);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(owner);
            }

        }

        [TestCase("GEO_Torso")]
        [TestCase("CLO_JacketBody")]
        public void TorsoSkinning_DeformsThreeSectionsAcrossProductionActions(
            string meshName)
        {
            V2Manifest manifest = LoadManifest();
            GameObject owner = new GameObject("Hero V2 spine skinning test");
            Mesh baked = new Mesh();
            try
            {
                Player3DAssetRegistry registry =
                    Player3DResources.Instantiate(owner.transform);
                Assert.That(registry, Is.Not.Null);
                Player3DBoneAnchors anchors = registry.Anchors;
                Assert.That(anchors.Pelvis.name, Is.EqualTo("pelvis"));
                Assert.That(anchors.Spine, Is.Not.Null);
                Assert.That(anchors.Spine.name, Is.EqualTo("spine"));
                Assert.That(anchors.Chest.name, Is.EqualTo("chest"));
                Assert.That(anchors.Spine.parent, Is.SameAs(anchors.Pelvis));
                Assert.That(anchors.Chest.parent, Is.SameAs(anchors.Spine));
                Assert.That(registry.AnatomicalParts.Count, Is.EqualTo(16));
                Assert.That(registry.Animations.Count, Is.EqualTo(41));
                Assert.That(manifest.actions, Has.Length.EqualTo(41));

                SkinnedMeshRenderer renderer =
                    FindBinding(registry, meshName).Renderer as SkinnedMeshRenderer;
                Assert.That(renderer, Is.Not.Null);
                Assert.That(renderer.quality, Is.EqualTo(SkinQuality.Bone2));
                Mesh source = renderer.sharedMesh;
                Vector3[] vertices = source.vertices;
                BoneWeight[] weights = source.boneWeights;
                Matrix4x4[] bindposes = source.bindposes;
                Transform[] bones = renderer.bones;
                Assert.That(weights, Has.Length.EqualTo(vertices.Length));
                Assert.That(bindposes, Has.Length.EqualTo(bones.Length));
                HashSet<Transform> sections = new HashSet<Transform>();
                int blendedVertexCount = 0;
                foreach (BoneWeight weight in weights)
                {
                    Assert.That(weight.weight0, Is.GreaterThan(0f));
                    Assert.That(weight.weight1, Is.GreaterThanOrEqualTo(0f));
                    Assert.That(weight.weight2 + weight.weight3, Is.Zero);
                    Assert.That(weight.weight0 + weight.weight1,
                        Is.EqualTo(1f).Within(0.0001f));
                    sections.Add(bones[weight.boneIndex0]);
                    if (weight.weight1 > 0f)
                    {
                        sections.Add(bones[weight.boneIndex1]);
                        blendedVertexCount++;
                    }
                }

                Assert.That(sections, Is.EquivalentTo(new[]
                {
                    anchors.Pelvis, anchors.Spine, anchors.Chest
                }));
                Assert.That(blendedVertexCount, Is.GreaterThan(0));
                int chestIndex = Array.IndexOf(bones, anchors.Chest);
                float maximumDepartureFromRigidChest = 0f;
                Matrix4x4[] posedBones = new Matrix4x4[bones.Length];
                foreach (V2Action action in manifest.actions)
                {
                    Assert.That(registry.TryGetAnimation(action.name,
                        out Player3DAnimationBinding animation), Is.True);
                    Assert.That(animation.Clip, Is.Not.Null, action.name);
                    HashSet<string> curvePaths = new HashSet<string>();
                    foreach (EditorCurveBinding curve in
                             AnimationUtility.GetCurveBindings(animation.Clip))
                    {
                        if (curve.type == typeof(Transform))
                        {
                            curvePaths.Add(curve.path);
                        }
                    }

                    foreach (Transform section in sections)
                    {
                        string path = AnimationUtility.CalculateTransformPath(
                            section, registry.Animator.transform);
                        Assert.That(curvePaths, Does.Contain(path),
                            $"{action.name} lost its {section.name} bone tracks.");
                    }

                    // Include endpoints and interior poses of every exported
                    // action, including facial and terminal hold actions.
                    for (int sample = 0; sample <= 8; sample++)
                    {
                        animation.Clip.SampleAnimation(
                            registry.Animator.gameObject,
                            animation.Clip.length * sample / 8f);
                        // Match production ground probing: compensate the FBX
                        // renderer's local scale before converting to world.
                        renderer.BakeMesh(baked, true);
                        Vector3[] actual = baked.vertices;
                        Assert.That(actual, Has.Length.EqualTo(vertices.Length));
                        for (int bone = 0; bone < bones.Length; bone++)
                        {
                            posedBones[bone] =
                                bones[bone].localToWorldMatrix * bindposes[bone];
                        }

                        float maximumSkinningError = 0f;
                        Vector3 firstActualWorld = Vector3.zero;
                        Vector3 firstExpectedWorld = Vector3.zero;
                        for (int vertex = 0; vertex < vertices.Length; vertex++)
                        {
                            BoneWeight weight = weights[vertex];
                            Vector3 expected = posedBones[weight.boneIndex0]
                                .MultiplyPoint3x4(vertices[vertex]) * weight.weight0;
                            if (weight.weight1 > 0f)
                            {
                                expected += posedBones[weight.boneIndex1]
                                    .MultiplyPoint3x4(vertices[vertex]) * weight.weight1;
                            }

                            Vector3 world = renderer.transform.TransformPoint(
                                actual[vertex]);
                            if (vertex == 0)
                            {
                                firstActualWorld = world;
                                firstExpectedWorld = expected;
                            }

                            maximumSkinningError = Mathf.Max(maximumSkinningError,
                                Vector3.Distance(world, expected));
                            Vector3 rigidChest = posedBones[chestIndex]
                                .MultiplyPoint3x4(vertices[vertex]);
                            maximumDepartureFromRigidChest = Mathf.Max(
                                maximumDepartureFromRigidChest,
                                Vector3.Distance(world, rigidChest));
                        }

                        Assert.That(maximumSkinningError, Is.LessThan(0.0001f),
                            $"{meshName}, {action.name}, sample {sample}: " +
                            "the imported mesh does not follow its spine weights. " +
                            $"Renderer local scale={renderer.transform.localScale}, " +
                            $"lossy scale={renderer.transform.lossyScale}; " +
                            $"first vertex actual={firstActualWorld:F6}, " +
                            $"expected={firstExpectedWorld:F6}.");
                    }
                }

                Assert.That(maximumDepartureFromRigidChest, Is.GreaterThan(0.01f),
                    $"{meshName} still moves as a rigid chest through all 41 actions.");
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(baked);
                UnityEngine.Object.DestroyImmediate(owner);
            }
        }

        private static V2Manifest LoadManifest()
        {
            RequireGeneratedSources();
            TextAsset manifestAsset =
                AssetDatabase.LoadAssetAtPath<TextAsset>(V2ManifestPath);
            Assert.That(manifestAsset, Is.Not.Null);
            V2Manifest manifest =
                JsonUtility.FromJson<V2Manifest>(manifestAsset.text);
            Assert.That(manifest, Is.Not.Null);
            return manifest;
        }

        private static void AssertRuntimeTiming(
            V2Manifest manifest,
            string name,
            int runtimeFrameCount,
            float runtimeFramesPerSecond)
        {
            V2Action action = Array.Find(
                manifest.actions,
                candidate => string.Equals(
                    candidate.name,
                    name,
                    StringComparison.Ordinal));
            Assert.That(action, Is.Not.Null, $"Missing action '{name}'.");
            Assert.That(
                runtimeFrameCount / runtimeFramesPerSecond,
                Is.EqualTo(action.duration_seconds).Within(0.0001f),
                $"The runtime timeline plays '{name}' over " +
                $"{runtimeFrameCount / runtimeFramesPerSecond:F3} s, but the " +
                $"authored clip is {action.duration_seconds:F3} s long.");
            Assert.That(
                runtimeFrameCount,
                Is.EqualTo(action.source_frame_count),
                $"The runtime frame count for '{name}' must match the " +
                "authored source frames.");
        }

        private static void AssertMirrored(
            string what,
            float runtimeValue,
            float authoredValue)
        {
            Assert.That(
                runtimeValue,
                Is.EqualTo(authoredValue).Within(0.0001f),
                $"The runtime {what} is {runtimeValue:F4}, but the generator " +
                $"measured {authoredValue:F4}.");
        }

        private static void RequireGeneratedSources()
        {
            string[] required =
            {
                V2ModelPath,
                V2ManifestPath,
                V2AnimationPath,
                V2AtlasPath,
                V2ClothingAtlasPath,
                V2PortraitPath
            };
            for (int index = 0; index < required.Length; index++)
            {
                if (AssetDatabase.LoadMainAssetAtPath(required[index]) == null)
                {
                    Assert.Ignore(
                        "Hero V2 generator output has not been imported yet: " +
                        required[index]);
                }
            }
        }

        private static void AssertManifestContract(V2Manifest manifest)
        {
            Assert.That(manifest, Is.Not.Null);
            Assert.That(manifest.design_version, Is.EqualTo("HeroV2"));
            Assert.That(manifest.runtime_integrated, Is.True);
            Assert.That(manifest.action_count, Is.EqualTo(41));
            Assert.That(manifest.actions, Has.Length.EqualTo(41));
            Assert.That(manifest.face_atlas, Is.Not.Null);
            Assert.That(
                manifest.face_atlas.texture_asset,
                Is.EqualTo(V2AtlasPath));
            Assert.That(manifest.face_atlas.renderer, Is.EqualTo("GEO_FaceSurface"));
            Assert.That(manifest.face_atlas.columns, Is.EqualTo(4));
            Assert.That(manifest.face_atlas.rows, Is.EqualTo(4));
            Assert.That(manifest.face_atlas.cell_size_px, Is.EqualTo(64));
            Assert.That(manifest.face_atlas.uv_origin, Is.EqualTo("bottom_left"));
            Assert.That(manifest.face_atlas.filter_mode, Is.EqualTo("Point"));
            Assert.That(manifest.face_atlas.cells, Has.Length.EqualTo(9));
            Assert.That(manifest.design_metrics, Is.Not.Null);
            Assert.That(
                manifest.design_metrics.pelvis_height_m,
                Is.EqualTo(PlayerCharacterDimensions.PelvisHeight)
                    .Within(0.0001f),
                "Production contextual anchors must match the V2 pelvis.");

            AssertCell(manifest, "Neutral", 0, 3);
            AssertCell(manifest, "HalfBlink", 1, 3);
            AssertCell(manifest, "ClosedBlink", 2, 3);
            AssertCell(manifest, "Watchful", 0, 2);
            AssertCell(manifest, "Tense", 1, 2);
            AssertCell(manifest, "Drowsy", 2, 2);
            AssertCell(manifest, "Glazed", 3, 2);
            AssertCell(manifest, "Slack", 0, 1);
            AssertCell(manifest, "Grimace", 1, 1);
            AssertRunManifestContract(manifest);
            AssertBarDrinkManifestContract(manifest);
            AssertStaticTextureManifestContract(manifest);

            V2Part facePart = Array.Find(
                manifest.parts,
                part => part.name == "GEO_FaceSurface");
            Assert.That(facePart, Is.Not.Null);
            Assert.That(facePart.bone, Is.EqualTo("head"));
            Assert.That(facePart.material, Is.EqualTo("MAT_FaceAtlas"));
        }

        private static void AssertRunManifestContract(V2Manifest manifest)
        {
            HashSet<string> names = new HashSet<string>(StringComparer.Ordinal);
            for (int index = 0; index < manifest.actions.Length; index++)
            {
                Assert.That(names.Add(manifest.actions[index].name), Is.True);
            }

            Assert.That(names, Does.Contain("Run"));
            V2Action run = Array.Find(
                manifest.actions,
                action => action.name == "Run");
            Assert.That(run, Is.Not.Null);
            Assert.That(run.category, Is.EqualTo("locomotion"));
            Assert.That(run.duration_seconds, Is.EqualTo(0.75f).Within(0.0001f));
            Assert.That(run.loop, Is.True);
            Assert.That(run.source_frame_count, Is.EqualTo(18));
            Assert.That(run.source_fps, Is.EqualTo(24f).Within(0.0001f));
            Assert.That(run.frame_start, Is.EqualTo(0f).Within(0.0001f));
            Assert.That(run.frame_end, Is.EqualTo(18f).Within(0.0001f));
            Assert.That(run.root_motion, Is.False);
            Assert.That(run.event_count, Is.Zero);
            Assert.That(run.bone_only, Is.True);
            Assert.That(run.in_place, Is.True);
            Assert.That(run.gait_style, Is.EqualTo("heavy_weary"));
            Assert.That(run.landmark_count, Is.EqualTo(8));
            Assert.That(run.short_flight, Is.True);
        }

        private static void AssertBarDrinkManifestContract(
            V2Manifest manifest)
        {
            AssertBarDrinkAction(
                manifest,
                "BarDrinkPickupEnter",
                expectedDuration: 2f,
                expectedFramesPerSecond: 12f,
                expectedLoop: false);
            AssertBarDrinkAction(
                manifest,
                "BarDrinkSipLoop",
                expectedDuration: 3f,
                expectedFramesPerSecond: 8f,
                expectedLoop: true);
            AssertBarDrinkAction(
                manifest,
                "BarDrinkReturnExit",
                expectedDuration: 2f,
                expectedFramesPerSecond: 12f,
                expectedLoop: false);
        }

        private static void AssertBarDrinkAction(
            V2Manifest manifest,
            string name,
            float expectedDuration,
            float expectedFramesPerSecond,
            bool expectedLoop)
        {
            V2Action action = Array.Find(
                manifest.actions,
                candidate => candidate.name == name);
            Assert.That(action, Is.Not.Null, $"Missing Hero V2 action {name}.");
            Assert.That(action.category, Is.EqualTo("bar_drink"));
            Assert.That(
                action.duration_seconds,
                Is.EqualTo(expectedDuration).Within(0.0001f));
            Assert.That(action.loop, Is.EqualTo(expectedLoop));
            Assert.That(action.source_frame_count, Is.EqualTo(24));
            Assert.That(
                action.source_fps,
                Is.EqualTo(expectedFramesPerSecond).Within(0.0001f));
            Assert.That(action.root_motion, Is.False);
        }

        private static void AssertCell(
            V2Manifest manifest,
            string expression,
            int column,
            int row)
        {
            V2FaceCell cell = Array.Find(
                manifest.face_atlas.cells,
                candidate => candidate.expression == expression);
            Assert.That(cell, Is.Not.Null, $"Missing atlas cell {expression}.");
            Assert.That(cell.column, Is.EqualTo(column));
            Assert.That(cell.row, Is.EqualTo(row));
        }

        private static void AssertStaticTextureManifestContract(
            V2Manifest manifest)
        {
            Assert.That(manifest.texture_bindings, Has.Length.EqualTo(1));
            V2TextureBinding binding = manifest.texture_bindings[0];
            Assert.That(binding, Is.Not.Null);
            Assert.That(binding.texture_asset, Is.EqualTo(V2ClothingAtlasPath));
            Assert.That(binding.width_px, Is.EqualTo(256));
            Assert.That(binding.height_px, Is.EqualTo(256));
            Assert.That(
                binding.materials,
                Is.EqualTo(new[]
                {
                    "MAT_JacketAtlas",
                    "MAT_JeansAtlas",
                    "MAT_BandageAtlas"
                }));
            Assert.That(binding.shader_property, Is.EqualTo("_BaseMap"));
            Assert.That(binding.color_space, Is.EqualTo("sRGB"));
            Assert.That(binding.filter_mode, Is.EqualTo("Point"));
            Assert.That(binding.wrap_mode, Is.EqualTo("Clamp"));
            Assert.That(binding.mipmaps, Is.False);
            Assert.That(binding.compression, Is.EqualTo("Uncompressed"));
            Assert.That(binding.uv_channel, Is.EqualTo(0));
            Assert.That(binding.uv_origin, Is.EqualTo("bottom_left"));
            Assert.That(binding.uv_safe_inset_px, Is.EqualTo(1));
            Assert.That(binding.material_tint_hex, Is.EqualTo("FFFFFF"));
            Assert.That(binding.sha256, Does.Match("^[0-9a-f]{64}$"));
            Assert.That(binding.regions, Is.Not.Null.And.Not.Empty);

            HashSet<string> regionRenderers =
                new HashSet<string>(StringComparer.Ordinal);
            for (int index = 0; index < binding.regions.Length; index++)
            {
                V2TextureRegion region = binding.regions[index];
                Assert.That(region, Is.Not.Null);
                Assert.That(region.name, Is.Not.Null.And.Not.Empty);
                Assert.That(regionRenderers.Add(region.renderer), Is.True);
                Assert.That(region.x_px, Is.GreaterThanOrEqualTo(0));
                Assert.That(region.y_px, Is.GreaterThanOrEqualTo(0));
                Assert.That(
                    region.width_px,
                    Is.GreaterThan(binding.uv_safe_inset_px * 2));
                Assert.That(
                    region.height_px,
                    Is.GreaterThan(binding.uv_safe_inset_px * 2));
                Assert.That(
                    region.x_px + region.width_px,
                    Is.LessThanOrEqualTo(binding.width_px));
                Assert.That(
                    region.y_px + region.height_px,
                    Is.LessThanOrEqualTo(binding.height_px));
                for (int previousIndex = 0;
                     previousIndex < index;
                     previousIndex++)
                {
                    V2TextureRegion previous = binding.regions[previousIndex];
                    bool overlaps =
                        region.x_px < previous.x_px + previous.width_px &&
                        previous.x_px < region.x_px + region.width_px &&
                        region.y_px < previous.y_px + previous.height_px &&
                        previous.y_px < region.y_px + region.height_px;
                    Assert.That(
                        overlaps,
                        Is.False,
                        $"Regions {previous.name} and {region.name} overlap.");
                }
            }

            int texturedPartCount = 0;
            HashSet<string> jacketRenderers =
                new HashSet<string>(StringComparer.Ordinal);
            HashSet<string> jeansRenderers =
                new HashSet<string>(StringComparer.Ordinal);
            HashSet<string> bandageRenderers =
                new HashSet<string>(StringComparer.Ordinal);
            for (int index = 0; index < manifest.parts.Length; index++)
            {
                V2Part part = manifest.parts[index];
                Assert.That(
                    part.role,
                    Is.Not.EqualTo("clothing_detail"),
                    $"{part.name} should be painted into the clothing atlas.");
                Assert.That(part.name, Does.Not.StartWith("ACC_Strap"));
                Assert.That(part.name, Does.Not.Contain("Buckle"));
                Assert.That(
                    part.material,
                    Is.Not.EqualTo("MAT_Jacket")
                        .And.Not.EqualTo("MAT_JacketDark")
                        .And.Not.EqualTo("MAT_JacketEdge")
                        .And.Not.EqualTo("MAT_Jeans")
                        .And.Not.EqualTo("MAT_JeansEdge")
                        .And.Not.EqualTo("MAT_BootLeather")
                        .And.Not.EqualTo("MAT_BootSole")
                        .And.Not.EqualTo("MAT_Bandage")
                        .And.Not.EqualTo("MAT_BandageDark"));
                if (part.name.IndexOf("Boot", StringComparison.Ordinal) >= 0 ||
                    ((part.bone == "foot.L" || part.bone == "foot.R") &&
                     part.name != "GEO_Foot.L" &&
                     part.name != "GEO_Foot.R"))
                {
                    Assert.Fail(
                        $"Boot detail must be texture-authored; unexpected " +
                        $"foot-bound mesh '{part.name}'.");
                }

                if (!UsesClothingAtlas(part.material))
                {
                    continue;
                }

                texturedPartCount++;
                Assert.That(regionRenderers.Contains(part.name), Is.True);
                if (part.material == "MAT_JacketAtlas")
                {
                    jacketRenderers.Add(part.name);
                }
                else if (part.material == "MAT_JeansAtlas")
                {
                    jeansRenderers.Add(part.name);
                }
                else if (part.material == "MAT_BandageAtlas")
                {
                    bandageRenderers.Add(part.name);
                }
            }

            Assert.That(binding.regions.Length, Is.EqualTo(texturedPartCount));
            Assert.That(
                jacketRenderers,
                Is.EquivalentTo(new[]
                {
                    "CLO_JacketBody",
                    "CLO_JacketSleeve.L",
                    "CLO_JacketSleeve.R",
                    "CLO_JacketForearm.R"
                }));
            Assert.That(
                jeansRenderers,
                Is.EquivalentTo(new[]
                {
                    "GEO_Pelvis",
                    "GEO_Thigh.L",
                    "GEO_Shin.L",
                    "GEO_Foot.L",
                    "GEO_Thigh.R",
                    "GEO_Shin.R",
                    "GEO_Foot.R"
                }));
            Assert.That(
                bandageRenderers,
                Is.EquivalentTo(new[] { "CLO_Bandage.L" }));
            V2TextureRegion rightForearm = Array.Find(
                binding.regions,
                region => region.name == "JacketForearmRight");
            Assert.That(rightForearm, Is.Not.Null);
            Assert.That(
                rightForearm.renderer,
                Is.EqualTo("CLO_JacketForearm.R"));
        }

        private static void AssertStaticTextureBindings(
            Player3DAssetRegistry registry,
            V2Manifest manifest,
            Texture2D atlas)
        {
            Material clothingMaterial =
                AssetDatabase.LoadAssetAtPath<Material>(
                    V2ClothingMaterialPath);
            Material productionMaterial =
                AssetDatabase.LoadAssetAtPath<Material>(ProductionMaterialPath);
            Assert.That(clothingMaterial, Is.Not.Null);
            Assert.That(productionMaterial, Is.Not.Null);
            Assert.That(clothingMaterial, Is.Not.SameAs(productionMaterial));
            Assert.That(clothingMaterial.shader, Is.SameAs(productionMaterial.shader));
            Assert.That(clothingMaterial.color, Is.EqualTo(Color.white));
            Assert.That(
                clothingMaterial.GetColor("_BaseColor"),
                Is.EqualTo(Color.white));
            Assert.That(clothingMaterial.enableInstancing, Is.True);
            Assert.That(clothingMaterial.GetTexture("_BaseMap"), Is.SameAs(atlas));
            Assert.That(
                clothingMaterial.GetTextureScale("_BaseMap"),
                Is.EqualTo(Vector2.one));
            Assert.That(
                clothingMaterial.GetTextureOffset("_BaseMap"),
                Is.EqualTo(Vector2.zero));

            Dictionary<string, V2Part> parts =
                new Dictionary<string, V2Part>(StringComparer.Ordinal);
            for (int index = 0; index < manifest.parts.Length; index++)
            {
                parts.Add(manifest.parts[index].name, manifest.parts[index]);
            }

            Dictionary<string, V2TextureRegion> regions =
                new Dictionary<string, V2TextureRegion>(StringComparer.Ordinal);
            V2TextureBinding textureBinding = manifest.texture_bindings[0];
            for (int index = 0; index < textureBinding.regions.Length; index++)
            {
                V2TextureRegion region = textureBinding.regions[index];
                regions.Add(region.renderer, region);
            }

            for (int index = 0; index < registry.MeshBindings.Count; index++)
            {
                Player3DMeshBinding meshBinding = registry.MeshBindings[index];
                V2Part part = parts[meshBinding.MeshName];
                bool textured = UsesClothingAtlas(part.material);
                Material expected = textured
                    ? clothingMaterial
                    : productionMaterial;
                Material[] materials = meshBinding.Renderer.sharedMaterials;
                Assert.That(materials, Is.Not.Empty);
                for (int materialIndex = 0;
                     materialIndex < materials.Length;
                     materialIndex++)
                {
                    Assert.That(materials[materialIndex], Is.SameAs(expected));
                }

                if (!textured)
                {
                    continue;
                }

                Assert.That(meshBinding.BaseColor, Is.EqualTo(Color.white));
                MaterialPropertyBlock properties = new MaterialPropertyBlock();
                meshBinding.Renderer.GetPropertyBlock(properties);
                Assert.That(
                    properties.GetColor(Shader.PropertyToID("_BaseColor")),
                    Is.EqualTo(Color.white),
                    $"{meshBinding.MeshName} must not tint its full-colour atlas.");
                Assert.That(
                    regions.TryGetValue(
                        meshBinding.MeshName,
                        out V2TextureRegion region),
                    Is.True);
                AssertUvInsideRegion(
                    GetMesh(meshBinding.Renderer),
                    textureBinding,
                    region);
            }
        }

        private static void AssertUvInsideRegion(
            Mesh mesh,
            V2TextureBinding binding,
            V2TextureRegion region)
        {
            Assert.That(mesh, Is.Not.Null);
            Vector2[] uv = mesh.uv;
            Assert.That(uv, Has.Length.EqualTo(mesh.vertexCount));
            Vector2 minimum = new Vector2(
                (float)(region.x_px + binding.uv_safe_inset_px) /
                binding.width_px,
                (float)(region.y_px + binding.uv_safe_inset_px) /
                binding.height_px);
            Vector2 maximum = new Vector2(
                (float)(region.x_px + region.width_px -
                        binding.uv_safe_inset_px) /
                    binding.width_px,
                (float)(region.y_px + region.height_px -
                        binding.uv_safe_inset_px) /
                    binding.height_px);
            for (int index = 0; index < uv.Length; index++)
            {
                Assert.That(
                    uv[index].x,
                    Is.InRange(minimum.x - 0.0001f, maximum.x + 0.0001f));
                Assert.That(
                    uv[index].y,
                    Is.InRange(minimum.y - 0.0001f, maximum.y + 0.0001f));
            }
        }

        private static bool UsesClothingAtlas(string materialName)
        {
            return materialName == "MAT_JacketAtlas" ||
                   materialName == "MAT_JeansAtlas" ||
                   materialName == "MAT_BandageAtlas";
        }

        private static void AssertModelImport()
        {
            ModelImporter importer =
                AssetImporter.GetAtPath(V2ModelPath) as ModelImporter;
            Assert.That(importer, Is.Not.Null);
            Assert.That(
                importer.animationType,
                Is.EqualTo(ModelImporterAnimationType.Generic));
            Assert.That(importer.importAnimation, Is.False);
            Assert.That(
                importer.avatarSetup,
                Is.EqualTo(ModelImporterAvatarSetup.CreateFromThisModel));
            Assert.That(
                importer.materialImportMode,
                Is.EqualTo(ModelImporterMaterialImportMode.None));
            Assert.That(importer.preserveHierarchy, Is.True);
            Assert.That(importer.optimizeGameObjects, Is.False);
            Assert.That(importer.generateSecondaryUV, Is.False);
        }

        private static void AssertAnimationImport(V2Manifest manifest)
        {
            ModelImporter importer =
                AssetImporter.GetAtPath(V2AnimationPath) as ModelImporter;
            Assert.That(importer, Is.Not.Null);
            Assert.That(importer.importAnimation, Is.True);
            Assert.That(
                importer.avatarSetup,
                Is.EqualTo(ModelImporterAvatarSetup.CopyFromOther));
            Assert.That(importer.sourceAvatar, Is.Not.Null);
            Assert.That(
                AssetDatabase.GetAssetPath(importer.sourceAvatar),
                Is.EqualTo(V2ModelPath),
                "V2 animation must use the production model Avatar.");

            ModelImporterClipAnimation runSettings = Array.Find(
                importer.clipAnimations,
                clip => clip.name == "Run");
            Assert.That(runSettings, Is.Not.Null);
            Assert.That(runSettings.loopTime, Is.True);
            Assert.That(runSettings.loopPose, Is.True);

            ModelImporterClipAnimation sipSettings = Array.Find(
                importer.clipAnimations,
                clip => clip.name == "BarDrinkSipLoop");
            Assert.That(sipSettings, Is.Not.Null);
            Assert.That(sipSettings.loopTime, Is.True);

            AnimationClip runClip = null;
            int clipCount = 0;
            UnityEngine.Object[] assets =
                AssetDatabase.LoadAllAssetsAtPath(V2AnimationPath);
            for (int index = 0; index < assets.Length; index++)
            {
                if (!(assets[index] is AnimationClip clip) ||
                    clip.name.StartsWith("__preview__", StringComparison.Ordinal))
                {
                    continue;
                }

                clipCount++;
                if (clip.name == "Run")
                {
                    runClip = clip;
                }
            }

            Assert.That(clipCount, Is.EqualTo(manifest.action_count));
            Assert.That(runClip, Is.Not.Null);
            Assert.That(runClip.isLooping, Is.True);
            Assert.That(runClip.length, Is.EqualTo(0.75f).Within(1f / 24f));
            Assert.That(AnimationUtility.GetAnimationEvents(runClip), Is.Empty);
        }

        private static void AssertTextureImport(
            string path,
            bool alphaIsTransparency)
        {
            TextureImporter importer =
                AssetImporter.GetAtPath(path) as TextureImporter;
            Assert.That(importer, Is.Not.Null);
            Assert.That(importer.textureType, Is.EqualTo(TextureImporterType.Default));
            Assert.That(importer.textureShape, Is.EqualTo(TextureImporterShape.Texture2D));
            Assert.That(importer.sRGBTexture, Is.True);
            Assert.That(
                importer.alphaSource,
                Is.EqualTo(TextureImporterAlphaSource.FromInput));
            Assert.That(importer.alphaIsTransparency, Is.EqualTo(alphaIsTransparency));
            Assert.That(importer.filterMode, Is.EqualTo(FilterMode.Point));
            Assert.That(importer.wrapMode, Is.EqualTo(TextureWrapMode.Clamp));
            Assert.That(importer.mipmapEnabled, Is.False);
            Assert.That(importer.isReadable, Is.False);
            Assert.That(importer.npotScale, Is.EqualTo(TextureImporterNPOTScale.None));
            Assert.That(importer.maxTextureSize, Is.EqualTo(256));
            Assert.That(
                importer.textureCompression,
                Is.EqualTo(TextureImporterCompression.Uncompressed));
            TextureImporterPlatformSettings standalone =
                importer.GetPlatformTextureSettings("Standalone");
            Assert.That(standalone.overridden, Is.True);
            Assert.That(standalone.maxTextureSize, Is.EqualTo(256));
            Assert.That(
                standalone.textureCompression,
                Is.EqualTo(TextureImporterCompression.Uncompressed));
            Assert.That(standalone.crunchedCompression, Is.False);
        }

        private static void AssertAtlasTransform(
            Player3DFaceAtlasBinding atlas,
            PlayerFacialExpression expression,
            int column,
            int row)
        {
            Assert.That(
                atlas.TryGetTextureTransform(expression, out Vector4 transform),
                Is.True);
            Assert.That(transform.x, Is.EqualTo(0.25f).Within(0.0001f));
            Assert.That(transform.y, Is.EqualTo(0.25f).Within(0.0001f));
            Assert.That(transform.z, Is.EqualTo(column * 0.25f).Within(0.0001f));
            Assert.That(transform.w, Is.EqualTo(row * 0.25f).Within(0.0001f));
        }

        private static void AssertNeutralAtlasBootstrapped(
            Renderer renderer,
            Texture2D atlas)
        {
            MaterialPropertyBlock properties = new MaterialPropertyBlock();
            renderer.GetPropertyBlock(properties);
            Assert.That(
                properties.GetTexture(Shader.PropertyToID("_BaseMap")),
                Is.SameAs(atlas),
                "Direct V2 resource instantiation must never show a blank " +
                "face before PlayerFactory adds its presentation component.");
            Vector4 transform = properties.GetVector(
                Shader.PropertyToID("_BaseMap_ST"));
            Assert.That(transform.x, Is.EqualTo(0.25f).Within(0.0001f));
            Assert.That(transform.y, Is.EqualTo(0.25f).Within(0.0001f));
            Assert.That(transform.z, Is.EqualTo(0f).Within(0.0001f));
            Assert.That(transform.w, Is.EqualTo(0.75f).Within(0.0001f));
        }

        private static void AssertFacePointsAlongDeclaredForward(
            Player3DAssetRegistry registry,
            Player3DMeshBinding face)
        {
            Player3DMeshBinding head = FindBinding(registry, "GEO_Head");
            Vector3 headToFace = Vector3.ProjectOnPlane(
                face.Renderer.bounds.center - head.Renderer.bounds.center,
                Vector3.up);
            Assert.That(headToFace.sqrMagnitude, Is.GreaterThan(0.000001f));
            Vector3 forward = registry.transform.TransformDirection(
                registry.Metrics.LocalForward).normalized;
            Assert.That(
                Vector3.Dot(headToFace.normalized, forward),
                Is.GreaterThan(0.9f),
                "The V2 face surface, not a legacy GEO_Nose marker, defines " +
                "the visible facial direction.");
        }

        private static void AssertFaceUvRange(Mesh faceMesh)
        {
            Vector2[] uv = faceMesh.uv;
            Assert.That(uv, Has.Length.EqualTo(faceMesh.vertexCount));
            Vector2 minimum = uv[0];
            Vector2 maximum = uv[0];
            for (int index = 0; index < uv.Length; index++)
            {
                minimum = Vector2.Min(minimum, uv[index]);
                maximum = Vector2.Max(maximum, uv[index]);
                Assert.That(uv[index].x, Is.InRange(-0.0001f, 1.0001f));
                Assert.That(uv[index].y, Is.InRange(-0.0001f, 1.0001f));
            }

            Assert.That(minimum.x, Is.EqualTo(0f).Within(0.001f));
            Assert.That(minimum.y, Is.EqualTo(0f).Within(0.001f));
            Assert.That(maximum.x, Is.EqualTo(1f).Within(0.001f));
            Assert.That(maximum.y, Is.EqualTo(1f).Within(0.001f));
        }

        private static void AssertFacialKeys(
            Player3DAssetRegistry registry,
            V2Manifest manifest)
        {
            Dictionary<string, V2Action> actions =
                new Dictionary<string, V2Action>(StringComparer.Ordinal);
            for (int index = 0; index < manifest.actions.Length; index++)
            {
                actions.Add(manifest.actions[index].name, manifest.actions[index]);
            }

            Assert.That(registry.Animations.Count, Is.EqualTo(actions.Count));
            for (int bindingIndex = 0;
                 bindingIndex < registry.Animations.Count;
                 bindingIndex++)
            {
                Player3DAnimationBinding binding =
                    registry.Animations[bindingIndex];
                V2Action action = actions[binding.ClipName];
                int expectedCount = action.face_keys?.Length ?? 0;
                Assert.That(
                    binding.FacialExpressionKeys.Count,
                    Is.EqualTo(expectedCount),
                    $"Facial key count differs for {action.name}.");

                for (int keyIndex = 0; keyIndex < expectedCount; keyIndex++)
                {
                    V2FaceKey source = action.face_keys[keyIndex];
                    Player3DFacialExpressionKey key =
                        binding.FacialExpressionKeys[keyIndex];
                    Assert.That(
                        key.NormalizedTime,
                        Is.EqualTo(source.normalized_time).Within(0.0001f));
                    Assert.That(key.Expression.ToString(), Is.EqualTo(source.expression));
                }
            }
        }

        private static Player3DMeshBinding FindBinding(
            Player3DAssetRegistry registry,
            string name)
        {
            for (int index = 0; index < registry.MeshBindings.Count; index++)
            {
                Player3DMeshBinding binding = registry.MeshBindings[index];
                if (binding.MeshName == name)
                {
                    return binding;
                }
            }

            Assert.Fail($"Missing Hero V2 mesh binding '{name}'.");
            return null;
        }

        private static Mesh GetMesh(Renderer renderer)
        {
            if (renderer is SkinnedMeshRenderer skinned)
            {
                return skinned.sharedMesh;
            }

            MeshFilter filter = renderer.GetComponent<MeshFilter>();
            return filter != null ? filter.sharedMesh : null;
        }

        [Serializable]
        private sealed class V2Manifest
        {
            public string design_version;
            public bool runtime_integrated;
            public int action_count;
            public V2Part[] parts;
            public V2Action[] actions;
            public V2FaceAtlas face_atlas;
            public V2TextureBinding[] texture_bindings;
            public V2DesignMetrics design_metrics;
            public V2ManifestBedContract bed_contract;
        }

        [Serializable]
        private sealed class V2ManifestBedContract
        {
            public float supine_pelvis_offset_m;
            public float supine_head_offset_m;
            public float seated_pelvis_lift_m;
            public float mattress_above_floor_m;
            public float enter_seat_arrival;
            public float enter_seat_departure;
            public float exit_seat_arrival;
            public float exit_seat_departure;
        }

        [Serializable]
        private sealed class V2DesignMetrics
        {
            public float pelvis_height_m;
        }

        [Serializable]
        private sealed class V2Part
        {
            public string name;
            public string role;
            public string bone;
            public string material;
        }

        [Serializable]
        private sealed class V2Action
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
            public V2FaceKey[] face_keys;
        }

        [Serializable]
        private sealed class V2FaceKey
        {
            public float normalized_time;
            public string expression;
        }

        [Serializable]
        private sealed class V2FaceAtlas
        {
            public string texture_asset;
            public string renderer;
            public int columns;
            public int rows;
            public int cell_size_px;
            public string uv_origin;
            public string filter_mode;
            public V2FaceCell[] cells;
        }

        [Serializable]
        private sealed class V2FaceCell
        {
            public string expression;
            public int column;
            public int row;
        }

        [Serializable]
        private sealed class V2TextureBinding
        {
            public string texture_asset;
            public int width_px;
            public int height_px;
            public string[] materials;
            public string shader_property;
            public string color_space;
            public string filter_mode;
            public string wrap_mode;
            public bool mipmaps;
            public string compression;
            public int uv_channel;
            public string uv_origin;
            public int uv_safe_inset_px;
            public string material_tint_hex;
            public string sha256;
            public V2TextureRegion[] regions;
        }

        [Serializable]
        private sealed class V2TextureRegion
        {
            public string name;
            public string renderer;
            public int x_px;
            public int y_px;
            public int width_px;
            public int height_px;
        }
    }
}

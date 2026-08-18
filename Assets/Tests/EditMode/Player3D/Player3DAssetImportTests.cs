using System;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace BarPromenade.Tests
{
    public sealed class Player3DAssetImportTests
    {
        private const string ModelPath =
            "Assets/Player3D/Models/PlayerCharacter3D.fbx";
        private const string ManifestPath =
            "Assets/Player3D/Models/PlayerCharacter3D.json";
        private const string AnimationPath =
            "Assets/Player3D/Animations/PlayerCharacter3DAnimations.fbx";
        private const string MaterialPath =
            "Assets/Player3D/Materials/Player3DLit.mat";

        [Test]
        public void ProductionModel_HasDeterministicRuntimePrefabContract()
        {
            GameObject model =
                AssetDatabase.LoadAssetAtPath<GameObject>(ModelPath);
            if (model == null)
            {
                Assert.Ignore(
                    "Production Player 3D FBX has not been generated yet.");
            }

            TextAsset manifestAsset =
                AssetDatabase.LoadAssetAtPath<TextAsset>(ManifestPath);
            Assert.That(
                manifestAsset,
                Is.Not.Null,
                "Production Player 3D manifest is missing.");
            Player3DManifest manifest =
                JsonUtility.FromJson<Player3DManifest>(manifestAsset.text);
            Assert.That(manifest, Is.Not.Null);
            Assert.That(manifest.runtime_integrated, Is.True);
            Assert.That(manifest.pose, Is.EqualTo("apose").IgnoreCase);
            Assert.That(manifest.height_m, Is.EqualTo(1.75f).Within(0.001f));
            Assert.That(manifest.mesh_count, Is.GreaterThanOrEqualTo(16));
            Assert.That(manifest.parts, Has.Length.EqualTo(manifest.mesh_count));
            Assert.That(manifest.triangle_count, Is.InRange(1, 4500));
            Assert.That(manifest.actions, Has.Length.EqualTo(manifest.action_count));
            Assert.That(manifest.action_count, Is.EqualTo(29));
            Assert.That(manifest.forward_axis, Is.EqualTo("-Y"));
            Assert.That(manifest.anatomical_left_axis, Is.EqualTo("+X"));
            AssertActionMetadata(
                manifest,
                "BusBoardEnter",
                "bus_ride",
                3f,
                false,
                36,
                12f);
            AssertActionMetadata(
                manifest,
                "BusRideLoop",
                "bus_ride",
                2f,
                true,
                16,
                8f);
            AssertActionMetadata(
                manifest,
                "BusAlightExit",
                "bus_ride",
                3f,
                false,
                36,
                12f);
            AssertActionMetadata(
                manifest,
                "ChessSeatEnter",
                "chess_seat",
                3f,
                false,
                36,
                12f);
            AssertActionMetadata(
                manifest,
                "ChessSeatPlayLoop",
                "chess_seat",
                4f,
                true,
                32,
                8f);
            AssertActionMetadata(
                manifest,
                "ChessSeatExit",
                "chess_seat",
                3f,
                false,
                36,
                12f);

            ModelImporter importer =
                AssetImporter.GetAtPath(ModelPath) as ModelImporter;
            Assert.That(importer, Is.Not.Null);
            Assert.That(
                importer.animationType,
                Is.EqualTo(ModelImporterAnimationType.Generic));
            Assert.That(importer.importAnimation, Is.False);
            Assert.That(importer.optimizeGameObjects, Is.False);
            Assert.That(importer.preserveHierarchy, Is.True);
            Assert.That(importer.importCameras, Is.False);
            Assert.That(importer.importLights, Is.False);
            Assert.That(importer.addCollider, Is.False);
            Assert.That(importer.globalScale, Is.EqualTo(1f));
            Assert.That(
                importer.materialImportMode,
                Is.EqualTo(ModelImporterMaterialImportMode.None));
            AssertAnimationImport(manifest);

            Material material =
                AssetDatabase.LoadAssetAtPath<Material>(MaterialPath);
            Assert.That(material, Is.Not.Null);
            Assert.That(material.shader, Is.Not.Null);
            Assert.That(
                material.shader.name,
                Is.EqualTo("Universal Render Pipeline/Lit"));
            Assert.That(material.enableInstancing, Is.True);

            GameObject prefab = Player3DResources.LoadPrefab();
            Assert.That(
                prefab,
                Is.Not.Null,
                "Runtime Player 3D prefab was not generated.");
            Assert.That(
                AssetDatabase.GetAssetPath(prefab),
                Is.EqualTo(
                    "Assets/Resources/Player/Player3D.prefab"));

            GameObject instance = UnityEngine.Object.Instantiate(prefab);
            try
            {
                Player3DAssetRegistry registry =
                    instance.GetComponent<Player3DAssetRegistry>();
                Assert.That(registry, Is.Not.Null);
                Assert.That(registry.Animator, Is.Not.Null);
                Assert.That(registry.Animator.avatar, Is.Not.Null);
                Assert.That(registry.Animator.avatar.isValid, Is.True);
                Assert.That(registry.Animator.applyRootMotion, Is.False);
                Assert.That(registry.ModelRoot, Is.Not.Null);
                Assert.That(registry.SourcePose, Is.EqualTo("apose").IgnoreCase);
                Assert.That(
                    registry.SourceGeneratorVersion,
                    Is.Not.Null.And.Not.Empty);
                Assert.That(
                    registry.SourceTriangleCount,
                    Is.EqualTo(manifest.triangle_count));
                Assert.That(
                    registry.BuildSignature,
                    Is.Not.Null.And.Not.Empty,
                    "Runtime prefab must carry its source/schema stamp.");
                Assert.That(
                    registry.MeshBindings.Count,
                    Is.EqualTo(manifest.mesh_count));
                Assert.That(
                    registry.Renderers.Count,
                    Is.EqualTo(manifest.mesh_count));
                Assert.That(
                    registry.AnatomicalParts.Count,
                    Is.EqualTo(
                        Enum.GetValues(typeof(Player3DAnatomicalPart)).Length));
                Assert.That(
                    registry.Animations.Count,
                    Is.EqualTo(manifest.action_count));

                Assert.That(
                    registry.Metrics.CanonicalHeight,
                    Is.EqualTo(1.75f).Within(0.001f));
                Assert.That(
                    registry.Metrics.LocalBounds.size.y,
                    Is.EqualTo(1.75f).Within(0.035f));
                Assert.That(
                    registry.Metrics.LocalBounds.min.y,
                    Is.EqualTo(0f).Within(0.025f));
                Assert.That(
                    Vector3.Angle(
                        registry.Metrics.LocalForward,
                        Vector3.forward),
                    Is.LessThan(0.01f));

                AssertAnchors(registry.Anchors);
                AssertMeshBindings(registry, material);
                AssertAnatomicalParts(registry);
                AssertRegistryAnimations(registry, manifest);
                AssertVisualFacing(registry);
                AssertSignatureSides(registry);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(instance);
            }
        }

        private static void AssertMeshBindings(
            Player3DAssetRegistry registry,
            Material sharedMaterial)
        {
            HashSet<Renderer> uniqueRenderers = new HashSet<Renderer>();
            for (int index = 0; index < registry.MeshBindings.Count; index++)
            {
                Player3DMeshBinding binding = registry.MeshBindings[index];
                Assert.That(binding, Is.Not.Null);
                Assert.That(binding.Renderer, Is.Not.Null);
                Assert.That(binding.Bone, Is.Not.Null);
                Assert.That(binding.Renderer.name, Is.EqualTo(binding.MeshName));
                Assert.That(binding.Bone.name, Is.EqualTo(binding.BoneName));
                Assert.That(binding.Role, Is.Not.Null.And.Not.Empty);
                Assert.That(
                    binding.PaletteMaterialName,
                    Does.StartWith("MAT_"));
                Assert.That(uniqueRenderers.Add(binding.Renderer), Is.True);

                Material[] materials = binding.Renderer.sharedMaterials;
                Assert.That(materials, Is.Not.Empty);
                for (int materialIndex = 0;
                     materialIndex < materials.Length;
                     materialIndex++)
                {
                    Assert.That(
                        materials[materialIndex],
                        Is.SameAs(sharedMaterial));
                }
            }
        }

        private static void AssertAnimationImport(Player3DManifest manifest)
        {
            ModelImporter importer =
                AssetImporter.GetAtPath(AnimationPath) as ModelImporter;
            Assert.That(importer, Is.Not.Null);
            Assert.That(
                importer.animationType,
                Is.EqualTo(ModelImporterAnimationType.Generic));
            Assert.That(importer.importAnimation, Is.True);
            Assert.That(
                importer.avatarSetup,
                Is.EqualTo(ModelImporterAvatarSetup.CopyFromOther));
            Assert.That(importer.sourceAvatar, Is.Not.Null);
            Assert.That(importer.optimizeGameObjects, Is.False);
            Assert.That(importer.preserveHierarchy, Is.True);

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

                Assert.That(
                    clip.name,
                    Does.Not.Contain("|"),
                    "FBX stack prefix was not normalized.");
                Assert.That(
                    clips.ContainsKey(clip.name),
                    Is.False,
                    $"Duplicate imported animation clip '{clip.name}'.");
                clips.Add(clip.name, clip);
            }

            Assert.That(clips.Count, Is.EqualTo(manifest.action_count));
            for (int index = 0; index < manifest.actions.Length; index++)
            {
                Player3DManifestAction action = manifest.actions[index];
                Assert.That(
                    clips.TryGetValue(action.name, out AnimationClip clip),
                    Is.True,
                    $"Missing imported animation clip '{action.name}'.");
                Assert.That(clip.isLooping, Is.EqualTo(action.loop));
                Assert.That(
                    clip.length,
                    Is.EqualTo(action.duration_seconds).Within(1f / 24f));
            }
        }

        private static void AssertActionMetadata(
            Player3DManifest manifest,
            string name,
            string category,
            float durationSeconds,
            bool looping,
            int sourceFrameCount,
            float sourceFramesPerSecond)
        {
            Player3DManifestAction action = Array.Find(
                manifest.actions,
                candidate => string.Equals(
                    candidate.name,
                    name,
                    StringComparison.Ordinal));
            Assert.That(action, Is.Not.Null, $"Missing action '{name}'.");
            Assert.That(action.category, Is.EqualTo(category));
            Assert.That(
                action.duration_seconds,
                Is.EqualTo(durationSeconds).Within(0.0001f));
            Assert.That(action.loop, Is.EqualTo(looping));
            Assert.That(action.source_frame_count, Is.EqualTo(sourceFrameCount));
            Assert.That(
                action.source_fps,
                Is.EqualTo(sourceFramesPerSecond).Within(0.0001f));
        }

        private static void AssertAnatomicalParts(
            Player3DAssetRegistry registry)
        {
            HashSet<Renderer> uniqueRenderers = new HashSet<Renderer>();
            Array values = Enum.GetValues(typeof(Player3DAnatomicalPart));
            foreach (Player3DAnatomicalPart expected in values)
            {
                Assert.That(
                    registry.TryGetPart(expected, out var binding),
                    Is.True,
                    $"Missing anatomical part {expected}.");
                Assert.That(binding, Is.Not.Null);
                Assert.That(binding.Renderer, Is.Not.Null);
                Assert.That(binding.Bone, Is.Not.Null);
                Assert.That(uniqueRenderers.Add(binding.Renderer), Is.True);
            }
        }

        private static void AssertRegistryAnimations(
            Player3DAssetRegistry registry,
            Player3DManifest manifest)
        {
            for (int index = 0; index < manifest.actions.Length; index++)
            {
                Player3DManifestAction action = manifest.actions[index];
                Assert.That(
                    registry.TryGetAnimation(
                        action.name,
                        out Player3DAnimationBinding binding),
                    Is.True,
                    $"Prefab registry is missing clip '{action.name}'.");
                Assert.That(binding, Is.Not.Null);
                Assert.That(binding.Clip, Is.Not.Null);
                Assert.That(binding.Clip.name, Is.EqualTo(action.name));
                Assert.That(binding.Category, Is.EqualTo(action.category));
                Assert.That(binding.Looping, Is.EqualTo(action.loop));
                Assert.That(
                    binding.AuthoredDuration,
                    Is.EqualTo(action.duration_seconds).Within(0.0001f));
            }
        }

        private static void AssertAnchors(Player3DBoneAnchors anchors)
        {
            Assert.That(anchors.Head, Is.Not.Null);
            Assert.That(anchors.Chest, Is.Not.Null);
            Assert.That(anchors.Pelvis, Is.Not.Null);
            Assert.That(anchors.LeftFoot, Is.Not.Null);
            Assert.That(anchors.RightFoot, Is.Not.Null);
            Assert.That(anchors.LeftGrip, Is.Not.Null);
            Assert.That(anchors.RightGrip, Is.Not.Null);
            Assert.That(anchors.RightCigarette, Is.Not.Null);
            Assert.That(anchors.Mouth, Is.Not.Null);
        }

        private static void AssertSignatureSides(
            Player3DAssetRegistry registry)
        {
            Renderer bandage = FindRenderer(registry, "CLO_Bandage.L");
            Renderer patch = FindRenderer(registry, "ACC_ShoulderPatch.R");
            Vector3 forward = registry.transform.TransformDirection(
                registry.Metrics.LocalForward).normalized;
            Vector3 right = Vector3.Cross(Vector3.up, forward).normalized;
            Vector3 center = registry.Anchors.Chest.position;
            Assert.That(
                Vector3.Dot(bandage.bounds.center - center, right),
                Is.LessThan(0f),
                "Bandage must remain on the character's physical left.");
            Assert.That(
                Vector3.Dot(patch.bounds.center - center, right),
                Is.GreaterThan(0f),
                "Shoulder patch must remain on the character's physical right.");
        }

        private static void AssertVisualFacing(
            Player3DAssetRegistry registry)
        {
            Renderer head = FindRenderer(registry, "GEO_Head");
            Renderer nose = FindRenderer(registry, "GEO_Nose");
            Vector3 headToNose = Vector3.ProjectOnPlane(
                nose.bounds.center - head.bounds.center,
                Vector3.up);
            Assert.That(
                headToNose.sqrMagnitude,
                Is.GreaterThan(0.0001f),
                "The face marker must remain distinct from the head center.");

            Vector3 declaredForward =
                registry.transform.TransformDirection(
                    registry.Metrics.LocalForward).normalized;
            Assert.That(
                Vector3.Dot(headToNose.normalized, declaredForward),
                Is.GreaterThan(0.95f),
                "The visible face must point along the runtime player's " +
                "declared forward direction.");
        }

        private static Renderer FindRenderer(
            Player3DAssetRegistry registry,
            string meshName)
        {
            for (int index = 0; index < registry.MeshBindings.Count; index++)
            {
                Player3DMeshBinding binding = registry.MeshBindings[index];
                if (binding.MeshName == meshName)
                {
                    return binding.Renderer;
                }
            }

            Assert.Fail($"Missing Player 3D mesh '{meshName}'.");
            return null;
        }

        [Serializable]
        private sealed class Player3DManifest
        {
            public float height_m;
            public bool runtime_integrated;
            public string pose;
            public string forward_axis;
            public string anatomical_left_axis;
            public int mesh_count;
            public int triangle_count;
            public int action_count;
            public Player3DManifestPart[] parts;
            public Player3DManifestAction[] actions;
        }

        [Serializable]
        private sealed class Player3DManifestPart
        {
            public string name;
        }

        [Serializable]
        private sealed class Player3DManifestAction
        {
            public string name;
            public string category;
            public float duration_seconds;
            public bool loop;
            public int source_frame_count;
            public float source_fps;
        }
    }
}

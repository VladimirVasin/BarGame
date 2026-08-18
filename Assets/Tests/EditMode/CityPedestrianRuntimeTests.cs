using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace BarPromenade.Tests.EditMode
{
    public sealed class CityPedestrianRuntimeTests
    {
        private const string LampshadeModelPath =
            "Assets/Pedestrians/Models/CityPedestrian3D.fbx";
        private const string ChairCarrierModelPath =
            "Assets/Pedestrians/Models/ChairCarrierPedestrian3D.fbx";
        private const string KettleHatModelPath =
            "Assets/Pedestrians/Models/KettleHatPedestrian3D.fbx";
        private const string LongArmModelPath =
            "Assets/Pedestrians/Models/LongArmPedestrian3D.fbx";
        private const string HelmetLampModelPath =
            "Assets/Pedestrians/Models/HelmetLampPedestrian3D.fbx";
        private const string PipebackRollerModelPath =
            "Assets/Pedestrians/Staged/Models/PipebackRoller3D.fbx";
        private const string PipebackRollerManifestPath =
            "Assets/Pedestrians/Staged/Models/PipebackRoller3D.json";
        private const string PipebackRollerPrefabPath =
            "Assets/Pedestrians/Staged/Prefabs/PipebackRoller3D.prefab";
        private const string PipebackRollerDesignId =
            "pipeback_roller_v1";
        // The Pipeback Roller, the Yard Babushka, the Weigh Attendant,
        // the Cemetery Mourner, the Cemetery Watchman and the Lake
        // Fisherman contribute two staged loops each.
        private const int StagedLocomotionClipCount = 12;
        private const string LocomotionManifestPath =
            "Assets/Pedestrians/Animations/" +
            "CityPedestrianLocomotion.json";
        private const string LocomotionAnimationPath =
            "Assets/Pedestrians/Animations/" +
            "CityPedestrianLocomotion.fbx";
        private const string PlayerModelPath =
            "Assets/Player3D/Models/PlayerCharacter3D.fbx";
        private const string SharedMaterialPath =
            "Assets/Player3D/Materials/Player3DLit.mat";

        [TestCase(
            LampshadeModelPath,
            CityPedestrianResources.LampshadePrefabResourcePath,
            CityPedestrianResources.LampshadeDesignId,
            1160,
            38,
            "LampshadeIdle",
            "LampshadeWalk")]
        [TestCase(
            ChairCarrierModelPath,
            CityPedestrianResources.ChairCarrierPrefabResourcePath,
            CityPedestrianResources.ChairCarrierDesignId,
            1032,
            35,
            "ChairCarrierIdle",
            "ChairCarrierWalk")]
        [TestCase(
            KettleHatModelPath,
            CityPedestrianResources.KettleHatPrefabResourcePath,
            CityPedestrianResources.KettleHatDesignId,
            1356,
            42,
            "KettleHatIdle",
            "KettleHatWalk")]
        [TestCase(
            LongArmModelPath,
            CityPedestrianResources.LongArmPrefabResourcePath,
            CityPedestrianResources.LongArmDesignId,
            1044,
            35,
            "LongArmIdle",
            "LongArmWalk")]
        [TestCase(
            HelmetLampModelPath,
            CityPedestrianResources.HelmetLampPrefabResourcePath,
            CityPedestrianResources.HelmetLampDesignId,
            1084,
            37,
            "HelmetLampIdle",
            "HelmetLampHop")]
        public void ProductionPrefabs_UseCustomLocomotionAndGroundedWalk(
            string modelPath,
            string prefabResourcePath,
            string designId,
            int triangleCount,
            int rendererCount,
            string idleClipName,
            string walkClipName)
        {
            ModelImporter importer =
                AssetImporter.GetAtPath(modelPath) as ModelImporter;
            Assert.That(importer, Is.Not.Null);
            Assert.That(
                importer.animationType,
                Is.EqualTo(ModelImporterAnimationType.Generic));
            Assert.That(
                importer.avatarSetup,
                Is.EqualTo(ModelImporterAvatarSetup.CopyFromOther));
            Assert.That(importer.sourceAvatar, Is.Not.Null);
            Assert.That(
                AssetDatabase.GetAssetPath(importer.sourceAvatar),
                Is.EqualTo(PlayerModelPath));
            Assert.That(importer.importAnimation, Is.False);
            Assert.That(importer.preserveHierarchy, Is.True);
            Assert.That(importer.optimizeGameObjects, Is.False);
            Assert.That(importer.addCollider, Is.False);
            Assert.That(
                AssetDatabase.LoadAllAssetsAtPath(modelPath)
                    .OfType<AnimationClip>()
                    .Where(clip => !clip.name.StartsWith(
                        "__preview__",
                        StringComparison.Ordinal)),
                Is.Empty,
                "A pedestrian model FBX must remain animation-free.");

            ModelImporter locomotionImporter =
                AssetImporter.GetAtPath(LocomotionAnimationPath) as
                    ModelImporter;
            Assert.That(locomotionImporter, Is.Not.Null);
            Assert.That(
                locomotionImporter.animationType,
                Is.EqualTo(ModelImporterAnimationType.Generic));
            Assert.That(
                locomotionImporter.avatarSetup,
                Is.EqualTo(ModelImporterAvatarSetup.CopyFromOther));
            Assert.That(locomotionImporter.sourceAvatar, Is.Not.Null);
            Assert.That(
                AssetDatabase.GetAssetPath(locomotionImporter.sourceAvatar),
                Is.EqualTo(PlayerModelPath));
            Assert.That(locomotionImporter.importAnimation, Is.True);
            AnimationClip[] locomotionClips =
                AssetDatabase.LoadAllAssetsAtPath(LocomotionAnimationPath)
                    .OfType<AnimationClip>()
                    .Where(clip => !clip.name.StartsWith(
                        "__preview__",
                        StringComparison.Ordinal))
                    .OrderBy(clip => clip.name, StringComparer.Ordinal)
                    .ToArray();
            // One shared animation-only FBX carries an idle and a walk for
            // every production or staged design.
            CollectionAssert.AreEquivalent(
                new[]
                {
                    "ChairCarrierIdle",
                    "ChairCarrierWalk",
                    "KettleHatIdle",
                    "KettleHatWalk",
                    "LampshadeIdle",
                    "LampshadeWalk",
                    "LongArmIdle",
                    "LongArmWalk",
                    "HelmetLampIdle",
                    "HelmetLampHop",
                    "PipebackIdle",
                    "PipebackRoll",
                    "BabushkaSmoke",
                    "BabushkaBeat",
                    "WeigherCheck",
                    "WeighedPace",
                    "MournerMourn",
                    "MournerWalk",
                    "WatchmanWatch",
                    "WatchmanShuffle",
                    "FishermanLean",
                    "FishermanTrudge",
                    "LampshadeSit",
                    "ChairCarrierSit",
                    "KettleHatSit",
                    "LongArmSit"
                },
                locomotionClips.Select(
                    clip => NormalizeAnimationClipName(clip.name)));
            // Production owns two clips per catalog design and its declared
            // seated loops; the isolated Pipeback and Yard Babushka
            // contribute two more each.
            int seatedCount = CityPedestrianResources.Archetypes
                .Count(archetype => archetype.CanRideBus);
            Assert.That(
                locomotionClips,
                Has.Length.EqualTo(
                    (CityPedestrianResources.Archetypes.Count * 2) +
                    seatedCount +
                    StagedLocomotionClipCount));
            Assert.That(
                locomotionClips.All(clip => clip.isLooping),
                Is.True,
                "Every custom pedestrian locomotion clip must loop.");

            GameObject pedestrianPrefab =
                Resources.Load<GameObject>(prefabResourcePath);
            GameObject playerPrefab = Player3DResources.LoadPrefab();
            Material sharedMaterial =
                AssetDatabase.LoadAssetAtPath<Material>(SharedMaterialPath);
            Assert.That(pedestrianPrefab, Is.Not.Null);
            Assert.That(playerPrefab, Is.Not.Null);
            Assert.That(sharedMaterial, Is.Not.Null);

            Player3DAssetRegistry playerRegistry =
                playerPrefab.GetComponent<Player3DAssetRegistry>();
            CityPedestrianAssetRegistry registry =
                pedestrianPrefab.GetComponent<
                    CityPedestrianAssetRegistry>();
            Assert.That(playerRegistry, Is.Not.Null);
            Assert.That(registry, Is.Not.Null);
            Assert.That(registry.Animator, Is.Not.Null);
            Assert.That(
                registry.Animator.avatar,
                Is.SameAs(playerRegistry.Animator.avatar));
            Assert.That(registry.Animator.applyRootMotion, Is.False);
            Assert.That(registry.Animator.runtimeAnimatorController, Is.Null);
            Assert.That(
                registry.Animator.cullingMode,
                Is.EqualTo(AnimatorCullingMode.CullUpdateTransforms));
            Assert.That(registry.DesignId, Is.EqualTo(designId));
            Assert.That(
                registry.SourceTriangleCount,
                Is.EqualTo(triangleCount));
            Assert.That(registry.Renderers.Count, Is.EqualTo(rendererCount));
            Assert.That(
                registry.LocalBounds.min.y,
                Is.EqualTo(0f).Within(0.025f));
            Assert.That(
                registry.LocalBounds.size.y,
                Is.EqualTo(1.75f).Within(0.035f));
            Assert.That(
                AssetDatabase.GetAssetPath(registry.IdleClip),
                Is.EqualTo(LocomotionAnimationPath));
            Assert.That(
                AssetDatabase.GetAssetPath(registry.WalkClip),
                Is.EqualTo(LocomotionAnimationPath));
            Assert.That(
                NormalizeAnimationClipName(registry.IdleClip.name),
                Is.EqualTo(idleClipName));
            Assert.That(
                NormalizeAnimationClipName(registry.WalkClip.name),
                Is.EqualTo(walkClipName));
            Assert.That(registry.IdleClip.isLooping, Is.True);
            Assert.That(registry.WalkClip.isLooping, Is.True);

            // Seating aligns the shared rest pelvis to the cushion instead of
            // pinning the lowest sole, so a riding design has to bind that
            // bone and carry its own authored seated loop; a design that
            // declares no ride must carry neither.
            Assert.That(
                CityPedestrianResources.TryGetArchetype(
                    designId,
                    out CityPedestrianArchetype archetype),
                Is.True);
            if (archetype.CanRideBus)
            {
                Assert.That(registry.PelvisAnchor, Is.Not.Null);
                Assert.That(registry.PelvisAnchor.name, Does.Contain("pelvis"));
                Assert.That(registry.SitClip, Is.Not.Null);
                Assert.That(
                    AssetDatabase.GetAssetPath(registry.SitClip),
                    Is.EqualTo(LocomotionAnimationPath));
                string expectedSitClipName = idleClipName.Substring(
                    0,
                    idleClipName.Length - "Idle".Length) + "Sit";
                Assert.That(
                    NormalizeAnimationClipName(registry.SitClip.name),
                    Is.EqualTo(expectedSitClipName));
                Assert.That(registry.SitClip.isLooping, Is.True);
            }
            else
            {
                Assert.That(registry.SitClip, Is.Null);
            }

            AssertPassiveSharedPresentation(
                pedestrianPrefab,
                registry,
                sharedMaterial);
            AssertSolePresentationWiring(pedestrianPrefab);
            AssertLocomotionManifestContract(
                designId,
                idleClipName,
                walkClipName);
        }

        [Test]
        public void StagedPipebackRoller_ImportsPassiveWheelchairAndRemainsOutsidePool()
        {
            ModelImporter importer =
                AssetImporter.GetAtPath(PipebackRollerModelPath) as
                    ModelImporter;
            Assert.That(importer, Is.Not.Null);
            Assert.That(
                importer.animationType,
                Is.EqualTo(ModelImporterAnimationType.Generic));
            Assert.That(
                importer.avatarSetup,
                Is.EqualTo(ModelImporterAvatarSetup.CopyFromOther));
            Assert.That(importer.sourceAvatar, Is.Not.Null);
            Assert.That(
                AssetDatabase.GetAssetPath(importer.sourceAvatar),
                Is.EqualTo(PlayerModelPath));
            Assert.That(importer.importAnimation, Is.False);
            Assert.That(importer.preserveHierarchy, Is.True);
            Assert.That(importer.optimizeGameObjects, Is.False);
            Assert.That(importer.addCollider, Is.False);
            Assert.That(
                AssetDatabase.LoadAllAssetsAtPath(PipebackRollerModelPath)
                    .OfType<AnimationClip>()
                    .Where(clip => !clip.name.StartsWith(
                        "__preview__",
                        StringComparison.Ordinal)),
                Is.Empty,
                "The staged model FBX must remain animation-free.");

            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(
                PipebackRollerPrefabPath);
            Material sharedMaterial =
                AssetDatabase.LoadAssetAtPath<Material>(SharedMaterialPath);
            Assert.That(prefab, Is.Not.Null);
            Assert.That(sharedMaterial, Is.Not.Null);
            Assert.That(
                AssetDatabase.GetAssetPath(prefab),
                Is.EqualTo(PipebackRollerPrefabPath));
            Assert.That(
                PipebackRollerPrefabPath,
                Does.Not.Contain("/Resources/"),
                "The staged design must not be loadable by the ambient " +
                "Resources catalog.");
            Assert.That(
                Resources.Load<GameObject>("Pedestrians/PipebackRoller3D"),
                Is.Null);

            CityPedestrianAssetRegistry registry =
                prefab.GetComponent<CityPedestrianAssetRegistry>();
            CityWheelchairNpcAssetRegistry wheelchairRegistry =
                prefab.GetComponent<CityWheelchairNpcAssetRegistry>();
            Assert.That(registry, Is.Not.Null);
            Assert.That(wheelchairRegistry, Is.Not.Null);
            Assert.That(
                wheelchairRegistry.PedestrianRegistry,
                Is.SameAs(registry));
            Assert.That(registry.DesignId, Is.EqualTo(PipebackRollerDesignId));
            Assert.That(registry.Animator, Is.Not.Null);
            Assert.That(registry.Animator.avatar, Is.Not.Null);
            Assert.That(
                AssetDatabase.GetAssetPath(registry.Animator.avatar),
                Is.EqualTo(PlayerModelPath));
            Assert.That(registry.Animator.applyRootMotion, Is.False);
            Assert.That(registry.Animator.runtimeAnimatorController, Is.Null);
            Assert.That(registry.PreservesAirborneMotion, Is.False);
            Assert.That(registry.HeadLamp, Is.Null);
            Assert.That(registry.PelvisAnchor, Is.Not.Null);
            Assert.That(registry.LeftFootAnchor, Is.Not.Null);
            Assert.That(registry.RightFootAnchor, Is.Not.Null);
            Assert.That(registry.SitClip, Is.Null);
            Assert.That(
                NormalizeAnimationClipName(registry.IdleClip.name),
                Is.EqualTo("PipebackIdle"));
            Assert.That(
                NormalizeAnimationClipName(registry.WalkClip.name),
                Is.EqualTo("PipebackRoll"));
            Assert.That(
                registry.IdleClip.length,
                Is.EqualTo(3f).Within(1f / 24f));
            Assert.That(
                registry.WalkClip.length,
                Is.EqualTo(2f).Within(1f / 24f));
            Assert.That(registry.IdleClip.isLooping, Is.True);
            Assert.That(registry.WalkClip.isLooping, Is.True);
            Assert.That(
                AssetDatabase.GetAssetPath(registry.IdleClip),
                Is.EqualTo(LocomotionAnimationPath));
            Assert.That(
                AssetDatabase.GetAssetPath(registry.WalkClip),
                Is.EqualTo(LocomotionAnimationPath));
            Assert.That(
                registry.LocalBounds.min.y,
                Is.EqualTo(0f).Within(0.025f));
            Assert.That(
                registry.LocalBounds.size.y,
                Is.EqualTo(1.75f).Within(0.035f));
            AssertWheelchairVisualDimensions(prefab);

            var modelManifest = JsonUtility.FromJson<PedestrianModelManifest>(
                System.IO.File.ReadAllText(PipebackRollerManifestPath));
            Assert.That(modelManifest, Is.Not.Null);
            Assert.That(
                modelManifest.design_id,
                Is.EqualTo(PipebackRollerDesignId));
            Assert.That(
                modelManifest.triangle_budget,
                Is.EqualTo(new[] { 1400, 2400 }));
            Assert.That(modelManifest.staged, Is.True);
            Assert.That(modelManifest.pool_eligible, Is.False);
            Assert.That(
                modelManifest.wheel_radius_m,
                Is.EqualTo(0.30f).Within(0.0001f));
            CollectionAssert.AreEqual(
                new[]
                {
                    "PIVOT_Wheel.L",
                    "PIVOT_Wheel.R",
                    "PIVOT_Caster.L",
                    "PIVOT_Caster.R",
                    "PIVOT_Bellows",
                    "PIVOT_PipeBank"
                },
                modelManifest.pivot_names);
            Assert.That(
                registry.SourceTriangleCount,
                Is.EqualTo(modelManifest.triangle_count));
            Assert.That(
                registry.Renderers.Count,
                Is.EqualTo(modelManifest.mesh_count));
            CollectionAssert.AreEqual(
                new[] { "PipebackIdle", "PipebackRoll" },
                modelManifest.shared_clips);

            Transform[] pivots =
            {
                wheelchairRegistry.LeftWheelPivot,
                wheelchairRegistry.RightWheelPivot,
                wheelchairRegistry.LeftCasterPivot,
                wheelchairRegistry.RightCasterPivot,
                wheelchairRegistry.BellowsPivot,
                wheelchairRegistry.PipeBankPivot
            };
            Assert.That(pivots.All(pivot => pivot != null), Is.True);
            CollectionAssert.AreEqual(
                new[]
                {
                    "PIVOT_Wheel.L",
                    "PIVOT_Wheel.R",
                    "PIVOT_Caster.L",
                    "PIVOT_Caster.R",
                    "PIVOT_Bellows",
                    "PIVOT_PipeBank"
                },
                pivots.Select(pivot => pivot.name).ToArray());
            Assert.That(
                pivots.Distinct().Count(),
                Is.EqualTo(pivots.Length));
            Assert.That(
                pivots.All(pivot => pivot.IsChildOf(registry.ModelRoot)),
                Is.True);

            AssertPassiveSharedPresentation(
                prefab,
                registry,
                sharedMaterial);
            Assert.That(
                prefab.GetComponentsInChildren<Collider2D>(true),
                Is.Empty);
            Assert.That(
                prefab.GetComponentsInChildren<Rigidbody2D>(true),
                Is.Empty);
            Assert.That(prefab.GetComponentsInChildren<Camera>(true), Is.Empty);
            Assert.That(
                prefab.GetComponentsInChildren<AudioSource>(true),
                Is.Empty);
            Assert.That(
                prefab.GetComponentsInChildren<MonoBehaviour>(true)
                    .All(behaviour =>
                        behaviour is CityPedestrianAssetRegistry ||
                        behaviour is CityWheelchairNpcAssetRegistry),
                Is.True,
                "The staged prefab may carry passive asset registries only.");

            LocomotionManifest locomotionManifest =
                JsonUtility.FromJson<LocomotionManifest>(
                    System.IO.File.ReadAllText(LocomotionManifestPath));
            LocomotionClip[] ownedClips = locomotionManifest.clips
                .Where(clip => string.Equals(
                    clip.archetype,
                    PipebackRollerDesignId,
                    StringComparison.Ordinal))
                .ToArray();
            CollectionAssert.AreEquivalent(
                new[] { "PipebackIdle", "PipebackRoll" },
                ownedClips.Select(clip => clip.name));
            Assert.That(ownedClips.All(clip => clip.loop), Is.True);
            Assert.That(ownedClips.All(clip => clip.in_place), Is.True);
            Assert.That(
                ownedClips.All(clip => clip.keyed_bone_count == 31),
                Is.True);
            Assert.That(
                ownedClips.Single(clip => clip.name == "PipebackIdle")
                    .duration_seconds,
                Is.EqualTo(3f).Within(0.0001f));
            Assert.That(
                ownedClips.Single(clip => clip.name == "PipebackRoll")
                    .duration_seconds,
                Is.EqualTo(2f).Within(0.0001f));
            Assert.That(
                ownedClips.All(clip =>
                    clip.wheel_ground_min_m >= -0.002f),
                Is.True);
            Assert.That(
                ownedClips.All(clip =>
                    clip.wheel_ground_max_contact_gap_m <= 0.002f),
                Is.True);
            Assert.That(
                ownedClips.All(clip =>
                    clip.footrest_min_clearance_m >= 0.03f),
                Is.True);
            Assert.That(
                ownedClips.All(clip =>
                    clip.rim_hand_max_distance_m <= 0.10f),
                Is.True);

            Assert.That(
                CityPedestrianResources.TryGetArchetype(
                    PipebackRollerDesignId,
                    out _),
                Is.False);
            Assert.That(
                CityPedestrianResources.Archetypes.Any(archetype =>
                    string.Equals(
                        archetype.DesignId,
                        PipebackRollerDesignId,
                        StringComparison.Ordinal)),
                Is.False);
            Assert.That(
                CityPedestrianResources.LoadPrefabs().Any(candidate =>
                    candidate.GetComponent<CityPedestrianAssetRegistry>()
                        .DesignId == PipebackRollerDesignId),
                Is.False);
            foreach (CityPedestrianPopulationProfile profile in new[]
                     {
                         CityPedestrianPopulationProfile.City,
                         CityPedestrianPopulationProfile.HomeBalcony
                     })
            {
                Assert.That(
                    CityPedestrianResources.CreatePoolComposition(
                            profile.PoolSize)
                        .Any(archetype => archetype.DesignId ==
                            PipebackRollerDesignId),
                    Is.False,
                    $"The staged design leaked into the {profile.Id} pool.");
            }
        }

        [Test]
        public void Catalog_OffersEveryRegisteredDesignToSpawnSelection()
        {
            // The director pools exactly what LoadPrefabs returns, so proving
            // the catalog and that array agree one-to-one is what makes each
            // archetype reachable by the spawn seed.
            IReadOnlyList<CityPedestrianArchetype> archetypes =
                CityPedestrianResources.Archetypes;
            Assert.That(
                archetypes.Select(archetype => archetype.DesignId).ToArray(),
                Is.EqualTo(
                    new[]
                    {
                        CityPedestrianResources.LampshadeDesignId,
                        CityPedestrianResources.ChairCarrierDesignId,
                        CityPedestrianResources.KettleHatDesignId,
                        CityPedestrianResources.LongArmDesignId,
                        CityPedestrianResources.HelmetLampDesignId
                    }),
                "The stable catalog order is part of the deterministic " +
                "spawn contract.");

            GameObject[] prefabs = CityPedestrianResources.LoadPrefabs();
            Assert.That(prefabs.Length, Is.EqualTo(archetypes.Count));
            for (int index = 0; index < prefabs.Length; index++)
            {
                Assert.That(prefabs[index], Is.Not.Null);
                CityPedestrianAssetRegistry registry =
                    prefabs[index]
                        .GetComponent<CityPedestrianAssetRegistry>();
                Assert.That(
                    registry,
                    Is.Not.Null,
                    $"'{archetypes[index].DesignId}' has no asset registry.");
                Assert.That(
                    registry.DesignId,
                    Is.EqualTo(archetypes[index].DesignId),
                    "Catalog order must match the loaded prefab order.");
                Assert.That(registry.IdleClip, Is.Not.Null);
                Assert.That(registry.WalkClip, Is.Not.Null);
                Assert.That(
                    NormalizeAnimationClipName(registry.IdleClip.name),
                    Is.Not.EqualTo(
                        NormalizeAnimationClipName(registry.WalkClip.name)));
            }

            Assert.That(
                prefabs
                    .Select(prefab => prefab
                        .GetComponent<CityPedestrianAssetRegistry>()
                        .IdleClip.name)
                    .Distinct(StringComparer.Ordinal)
                    .Count(),
                Is.EqualTo(prefabs.Length),
                "Every archetype must bind its own idle clip.");
            CityPedestrianPopulationProfile cityProfile =
                CityPedestrianPopulationProfile.City;
            Assert.That(
                cityProfile.PoolSize,
                Is.GreaterThan(cityProfile.DaytimePopulation),
                "The pool is intentionally larger than the active limit so " +
                "repeat encounters can vary the visible mix.");

            IReadOnlyList<CityPedestrianArchetype> composition =
                CityPedestrianResources.CreatePoolComposition(
                    cityProfile.PoolSize);
            Assert.That(composition.Count, Is.EqualTo(cityProfile.PoolSize));
            for (int index = 0; index < archetypes.Count; index++)
            {
                CityPedestrianArchetype archetype = archetypes[index];
                int instances = composition.Count(
                    entry => string.Equals(
                        entry.DesignId,
                        archetype.DesignId,
                        StringComparison.Ordinal));
                Assert.That(
                    instances,
                    Is.GreaterThanOrEqualTo(1),
                    $"'{archetype.DesignId}' must stay reachable by the " +
                    "spawn seed.");
                Assert.That(
                    instances,
                    Is.LessThanOrEqualTo(archetype.MaximumPoolInstances),
                    $"'{archetype.DesignId}' declares at most " +
                    $"{archetype.MaximumPoolInstances} pooled instance(s).");
            }

            // The only design carrying a working light stays single, which is
            // what bounds the worn lights in the world now that ordinary
            // designs repeat.
            Assert.That(
                composition.Count(
                    entry => string.Equals(
                        entry.DesignId,
                        CityPedestrianResources.HelmetLampDesignId,
                        StringComparison.Ordinal)),
                Is.EqualTo(1));
        }

        [Test]
        public void Actor_TurnsAtGraphCornerAndKeepsRootUpright()
        {
            GameObject root = new GameObject("Corner Test Root");
            CityPedestrianActor actor = null;
            CityPedestrianPresentation presentation = null;
            try
            {
                CityPedestrianPlan plan = CreateCornerPlan();
                actor = CreateBoundActor(
                    root.transform,
                    plan,
                    plan.SpawnAnchors[0],
                    1,
                    out presentation);

                // Simulate a small vertical correction from curb contact.
                // Graph steering must remain planar as the actor turns.
                actor.CharacterController.enabled = false;
                actor.transform.position = new Vector3(-0.001f, 0.24f, 0f);
                actor.CharacterController.enabled = true;
                Physics.SyncTransforms();
                actor.Advance(0.02f);

                Assert.That(
                    actor.MotionState,
                    Is.EqualTo(CityPedestrianMotionState.Walking));
                Assert.That(actor.PreviousNodeIndex, Is.EqualTo(1));
                Assert.That(actor.TargetNodeIndex, Is.EqualTo(2));
                AssertActorRootIsUpright(actor);

                actor.Advance(0.02f);
                Assert.That(actor.LastDisplacement.z, Is.GreaterThan(0f));
                AssertActorRootIsUpright(actor);
            }
            finally
            {
                ReleaseBoundActor(actor, presentation, root.transform);
                UnityEngine.Object.DestroyImmediate(root);
            }
        }

        [Test]
        public void Actor_InitialApproachChoosesClosestBranch()
        {
            GameObject root = new GameObject("Approach Branch Test Root");
            CityPedestrianActor actor = null;
            CityPedestrianPresentation presentation = null;
            try
            {
                CityPedestrianPlan plan = CreateApproachBranchPlan();
                actor = CreateBoundActor(
                    root.transform,
                    plan,
                    plan.SpawnAnchors[0],
                    1,
                    out presentation,
                    0xA341316Cu);

                actor.Advance(
                    2.1f,
                    false,
                    Vector3.zero,
                    new[] { 2f, 1f, 0f, 2f });

                Assert.That(
                    actor.PreviousNodeIndex,
                    Is.EqualTo(1));
                Assert.That(
                    actor.TargetNodeIndex,
                    Is.EqualTo(2),
                    "A hidden walker must take the branch that continues " +
                    "toward the stationary player.");
            }
            finally
            {
                ReleaseBoundActor(actor, presentation, root.transform);
                UnityEngine.Object.DestroyImmediate(root);
            }
        }

        [TestCase(0f, true)]
        [TestCase(1f, false)]
        public void Actor_AtCrosswalkUsesForcedChoice(
            float crosswalkRoll,
            bool shouldCross)
        {
            GameObject root = new GameObject("Crosswalk Choice Test Root");
            CityPedestrianActor actor = null;
            CityPedestrianPresentation presentation = null;
            try
            {
                CityPedestrianPlan plan = CreateCrosswalkChoicePlan();
                actor = CreateBoundActor(
                    root.transform,
                    plan,
                    plan.SpawnAnchors[0],
                    1,
                    out presentation);
                actor.ForceNextCrosswalkRoll(crosswalkRoll);

                actor.Advance(
                    1.1f,
                    false,
                    Vector3.zero);

                Assert.That(actor.CrosswalkDecisionCount, Is.EqualTo(1));
                Assert.That(
                    actor.CrosswalksTaken,
                    Is.EqualTo(shouldCross ? 1 : 0));
                Assert.That(
                    actor.TargetNodeIndex,
                    Is.EqualTo(shouldCross ? 3 : 2));
                Assert.That(
                    actor.MotionState,
                    Is.EqualTo(CityPedestrianMotionState.Walking));
            }
            finally
            {
                ReleaseBoundActor(actor, presentation, root.transform);
                UnityEngine.Object.DestroyImmediate(root);
            }
        }

        [Test]
        public void Factory_StaggersRandomizedFogBandSpawns()
        {
            GameObject root = new GameObject("Spawn Cap Test Root");
            CityPedestrianDirector director = null;
            try
            {
                CityPedestrianPopulationProfile profile =
                    CityPedestrianPopulationProfile.City;
                Transform player = CreatePlayer(root.transform);
                CityPedestrianPlan plan = CreateDistanceSpawnPlan(
                    CreateRingAnchorPositions(
                        profile.DaytimePopulation));
                director = CityPedestrianFactory.Create(
                    root.transform,
                    plan,
                    player,
                    CityPedestrianPlanner.CreateWalkableArea(plan),
                    profile,
                    () => false);

                Assert.That(
                    director.Count,
                    Is.EqualTo(profile.DaytimePopulation));
                Assert.That(
                    director.PoolCapacity,
                    Is.EqualTo(profile.PoolSize));
                Assert.That(
                    director.PoolCapacity,
                    Is.GreaterThan(director.Count),
                    "Spare presentations let a repeat encounter vary the mix.");
                Assert.That(director.ActiveCount, Is.Zero);

                float initialDelay = director.TimeUntilNextSpawn;
                Assert.That(
                    initialDelay,
                    Is.InRange(
                        CityPedestrianDirector.MinimumInitialSpawnDelay,
                        CityPedestrianDirector.MaximumInitialSpawnDelay));
                director.Advance(initialDelay * 0.5f);
                Assert.That(
                    director.ActiveCount,
                    Is.Zero,
                    "The first pedestrian must honor its random delay.");
                director.Advance((initialDelay * 0.5f) + 0.01f);
                Assert.That(
                    director.ActiveCount,
                    Is.EqualTo(profile.MaximumSpawnsPerEvent),
                    "One event activates a bounded batch, not the street.");

                // Below the target population the next event follows on the
                // short fill cadence, so a street or a bus ride does not stay
                // empty for the long replacement delay.
                float fillDelay = director.TimeUntilNextSpawn;
                Assert.That(
                    fillDelay,
                    Is.InRange(
                        CityPedestrianDirector.MinimumFillSpawnDelay,
                        CityPedestrianDirector.MaximumFillSpawnDelay));
                director.Advance(fillDelay * 0.5f);
                Assert.That(
                    director.ActiveCount,
                    Is.EqualTo(profile.MaximumSpawnsPerEvent),
                    "Each event must honor its own random delay.");

                for (int step = 0;
                     step < profile.DaytimePopulation &&
                     director.ActiveCount < profile.DaytimePopulation;
                     step++)
                {
                    director.Advance(
                        director.TimeUntilNextSpawn + 0.01f);
                }

                Assert.That(
                    director.ActiveCount,
                    Is.EqualTo(profile.DaytimePopulation),
                    "The daytime population must reach the profile target.");
                Assert.That(
                    director.TimeUntilNextSpawn,
                    Is.InRange(
                        CityPedestrianDirector.MinimumSpawnCooldown,
                        CityPedestrianDirector.MaximumSpawnCooldown),
                    "At the target only replacements remain, so the long " +
                    "cadence returns.");
                CityPedestrianActor[] active = director.Actors
                    .Where(candidate => candidate.IsSpawned)
                    .ToArray();
                // No design owns enough pooled instances to carry a full
                // street alone, so a full population is always a mix.
                Assert.That(
                    active.Select(candidate => candidate.DesignId)
                        .Distinct(StringComparer.Ordinal)
                        .Count(),
                    Is.GreaterThanOrEqualTo(3),
                    "A full street must mix several registered designs.");
                Assert.That(
                    active.Count(
                        candidate => string.Equals(
                            candidate.DesignId,
                            CityPedestrianResources.HelmetLampDesignId,
                            StringComparison.Ordinal)),
                    Is.LessThanOrEqualTo(1),
                    "Only one pooled hopper exists, so only one worn light " +
                    "can be in the world.");
                // Ordinary designs now repeat within their pooled instance
                // limits, so compare the distinct set against the catalog.
                CollectionAssert.IsSubsetOf(
                    active.Select(candidate => candidate.DesignId)
                        .Distinct(StringComparer.Ordinal)
                        .ToArray(),
                    CityPedestrianResources.Archetypes
                        .Select(archetype => archetype.DesignId)
                        .ToArray(),
                    "Every active design must come from the shared catalog.");
                for (int index = 0; index < active.Length; index++)
                {
                    Assert.That(
                        CityPedestrianResources.TryGetArchetype(
                            active[index].DesignId,
                            out CityPedestrianArchetype archetype),
                        Is.True);
                    Assert.That(
                        active[index].MovementSpeed,
                        Is.InRange(
                            archetype.MinimumMovementSpeed,
                            archetype.MaximumMovementSpeed));
                    Assert.That(
                        active[index].AnimationSpeed,
                        Is.InRange(
                            archetype.MinimumAnimationSpeed,
                            archetype.MaximumAnimationSpeed));
                }

                Assert.That(
                    active.Select(candidate => candidate.SpawnAnchorId)
                        .Distinct(StringComparer.Ordinal).Count(),
                    Is.EqualTo(active.Length));
                for (int first = 0; first < active.Length; first++)
                {
                    for (int second = first + 1;
                         second < active.Length;
                         second++)
                    {
                        Assert.That(
                            PlanarDistance(
                                active[first].Position,
                                active[second].Position),
                            Is.GreaterThan(
                                (CityPedestrianPlanner.AgentRadius * 2f) +
                                CityPedestrianDirector
                                    .CollisionActivationPadding));
                    }
                }

                // The whole population, not just the first pair, must stay
                // inside the band proven hidden at the widest production 16:9
                // frustum corner after a combined camera and visual-envelope
                // depth offset.
                Assert.That(
                    ConservativeFogTransmittance(
                        CityPedestrianDirector.MinimumSpawnDistance),
                    Is.LessThan(0.002f),
                    "The spawn band must stay hidden.");
                for (int index = 0; index < active.Length; index++)
                {
                    CityPedestrianSpawnAnchor spawnAnchor =
                        plan.SpawnAnchors.Single(
                            candidate => string.Equals(
                                candidate.Id,
                                active[index].SpawnAnchorId,
                                StringComparison.Ordinal));
                    Assert.That(
                        PlanarDistance(
                            player.position,
                            spawnAnchor.Position),
                        Is.InRange(
                            CityPedestrianDirector.MinimumSpawnDistance,
                            CityPedestrianDirector.MaximumSpawnDistance));
                }

                AssertRuntimeCollisionContract(director);
                Vector3[] frozen = active
                    .Select(candidate => candidate.Position)
                    .ToArray();
                director.Advance(0f);
                CollectionAssert.AreEqual(
                    frozen,
                    active.Select(candidate => candidate.Position).ToArray());
            }
            finally
            {
                director?.Shutdown();
                UnityEngine.Object.DestroyImmediate(root);
            }
        }

        [Test]
        public void Factory_NightSpawnsMuchLessOftenAndOnlyOneActor()
        {
            GameObject root = new GameObject("Night Spawn Test Root");
            CityPedestrianDirector director = null;
            bool isNight = true;
            try
            {
                const int anchorCount = 5;
                Transform player = CreatePlayer(root.transform);
                CityPedestrianPlan plan = CreateDistanceSpawnPlan(
                    CreateRingAnchorPositions(anchorCount));
                director = CityPedestrianFactory.Create(
                    root.transform,
                    plan,
                    player,
                    CityPedestrianPlanner.CreateWalkableArea(plan),
                    CityPedestrianResources.LoadPrefab(),
                    () => isNight);

                Assert.That(director.IsNightSpawnMode, Is.True);
                Assert.That(
                    director.CurrentActiveLimit,
                    Is.EqualTo(
                        CityPedestrianDirector.NightMaximumActiveModels));
                Assert.That(
                    CityPedestrianDirector.NightMaximumActiveModels,
                    Is.LessThan(
                        CityPedestrianDirector.MaximumActiveModels),
                    "Night must stay markedly sparser than the day street.");
                Assert.That(
                    director.TimeUntilNextSpawn,
                    Is.InRange(
                        CityPedestrianDirector.MinimumNightInitialSpawnDelay,
                        CityPedestrianDirector.MaximumNightInitialSpawnDelay));
                Assert.That(
                    CityPedestrianDirector.MinimumNightInitialSpawnDelay,
                    Is.GreaterThanOrEqualTo(
                        CityPedestrianDirector.MaximumInitialSpawnDelay *
                        2f));

                AdvanceToNextSpawn(director);
                Assert.That(
                    director.ActiveCount,
                    Is.EqualTo(1),
                    "Night activates one walker per event, never a batch.");
                Assert.That(
                    director.TimeUntilNextSpawn,
                    Is.InRange(
                        CityPedestrianDirector.MinimumNightSpawnCooldown,
                        CityPedestrianDirector.MaximumNightSpawnCooldown),
                    "Night keeps its long cadence instead of the day fill " +
                    "cadence, even while below its own population.");
                Assert.That(
                    CityPedestrianDirector.MinimumNightSpawnCooldown,
                    Is.GreaterThanOrEqualTo(
                        CityPedestrianDirector.MaximumSpawnCooldown * 2f));

                for (int step = 0; step < anchorCount; step++)
                {
                    director.Advance(
                        CityPedestrianDirector.MaximumNightSpawnCooldown +
                        1f);
                }

                Assert.That(
                    director.ActiveCount,
                    Is.EqualTo(
                        CityPedestrianDirector.NightMaximumActiveModels),
                    "Night must settle exactly on its own population.");

                isNight = false;
                director.Advance(0f);
                Assert.That(director.IsNightSpawnMode, Is.False);
                Assert.That(
                    director.CurrentActiveLimit,
                    Is.GreaterThan(
                        CityPedestrianDirector.NightMaximumActiveModels));
                Assert.That(
                    director.TimeUntilNextSpawn,
                    Is.InRange(
                        CityPedestrianDirector.MinimumInitialSpawnDelay,
                        CityPedestrianDirector.MaximumInitialSpawnDelay));
                AdvanceToNextSpawn(director);
                Assert.That(
                    director.ActiveCount,
                    Is.GreaterThan(
                        CityPedestrianDirector.NightMaximumActiveModels),
                    "Day mode fills past the night population.");
            }
            finally
            {
                director?.Shutdown();
                UnityEngine.Object.DestroyImmediate(root);
            }
        }

        [Test]
        public void Factory_DaytimeFastForwardsAndCompletesInitialApproach()
        {
            GameObject root = new GameObject(
                "Distant Pedestrian Simulation Test Root");
            CityPedestrianDirector director = null;
            bool isNight = false;
            try
            {
                Transform player = CreatePlayer(root.transform);
                CityPedestrianPlan plan = CreateLongApproachPlan();
                director = CityPedestrianFactory.Create(
                    root.transform,
                    plan,
                    player,
                    CityPedestrianPlanner.CreateWalkableArea(plan),
                    CityPedestrianResources.LoadPrefab(),
                    () => isNight);
                AdvanceToNextSpawn(director);

                CityPedestrianActor actor = director.Actors.Single(
                    candidate => candidate.IsSpawned);
                const float approachBudget = 50f;
                const float simulationStep = 0.1f;
                float elapsed = 0f;
                while (PlanarDistance(actor.Position, player.position) >
                           CityPedestrianDirector
                               .InitialApproachCompletionDistance &&
                       elapsed < approachBudget)
                {
                    director.Advance(simulationStep);
                    elapsed += simulationStep;
                }

                Assert.That(
                    PlanarDistance(actor.Position, player.position),
                    Is.LessThanOrEqualTo(
                        CityPedestrianDirector
                            .InitialApproachCompletionDistance + 0.01f),
                    "A daytime walker must enter the encounter radius " +
                    "while the player remains stationary.");
                const int actorIndex = 0;
                Assert.That(
                    director.IsActorInInitialApproach(actorIndex),
                    Is.False,
                    "Approach guidance must end after the first encounter.");

                actor.transform.position = new Vector3(0f, 0f, -70f);
                Physics.SyncTransforms();
                director.Advance(0f);
                Assert.That(
                    director.IsActorInInitialApproach(actorIndex),
                    Is.False,
                    "A walker must not resume pursuing the player after " +
                    "ordinary roaming carries it away.");

                actor.transform.position = new Vector3(
                    0f,
                    0f,
                    -CityPedestrianDirector
                        .DaytimeDistantSimulationInnerDistance + 1f);
                Physics.SyncTransforms();
                director.Advance(1f);
                Assert.That(
                    actor.LastDisplacement.magnitude,
                    Is.InRange(
                        CityPedestrianPlanner.MinimumSpeed - 0.01f,
                        CityPedestrianPlanner.MaximumSpeed + 0.01f),
                    "A walker near the renderable range must use its authored " +
                    "walking speed.");

                isNight = true;
                actor.transform.position = new Vector3(
                    0f,
                    0f,
                    -CityPedestrianDirector.MaximumSpawnDistance);
                Physics.SyncTransforms();
                director.Advance(1f);
                Assert.That(director.IsNightSpawnMode, Is.True);
                Assert.That(
                    actor.LastDisplacement.magnitude,
                    Is.InRange(
                        CityPedestrianPlanner.MinimumSpeed - 0.01f,
                        CityPedestrianPlanner.MaximumSpeed + 0.01f),
                    "Night walkers must retain their sparse authored pace.");
            }
            finally
            {
                director?.Shutdown();
                UnityEngine.Object.DestroyImmediate(root);
            }
        }

        [Test]
        public void Factory_DefaultHomeReturnReachesStationaryPlayer()
        {
            GameObject root = new GameObject(
                "Production Pedestrian Approach Test Root");
            CityPedestrianDirector director = null;
            try
            {
                const int citySeed = 20260727;
                CityLayout layout = CityLayoutGenerator.Generate(
                    CityBlueprintCatalog.Default,
                    CityGenerationSettings.Default,
                    citySeed);
                CityStreetSurfacePlan surfaces =
                    CityStreetSurfacePlanner.Create(layout);
                CityPedestrianPlan plan = CityPedestrianPlanner.Create(
                    layout,
                    citySeed,
                    surfaces);
                Transform player = CreatePlayer(root.transform);
                player.position =
                    layout.PlayerHome.SidewalkArrivalPosition +
                    Vector3.up *
                    (CityStreetSurfacePlanner.SidewalkTop +
                     PlayerFactory.GroundedRootOffset);
                int closestPlanNodeIndex = Enumerable.Range(
                        0,
                        plan.Nodes.Count)
                    .OrderBy(index => PlanarDistance(
                        plan.Nodes[index].Position,
                        player.position))
                    .First();
                CityPedestrianNode closestPlanNode =
                    plan.Nodes[closestPlanNodeIndex];
                float closestPlanNodeDistance = PlanarDistance(
                    closestPlanNode.Position,
                    player.position);
                HashSet<int> playerComponent = CollectReachableNodes(
                    plan,
                    closestPlanNodeIndex);
                float[] playerComponentAnchorDistances = plan.SpawnAnchors
                    .Where(anchor => playerComponent.Contains(
                        anchor.FirstNodeIndex))
                    .Select(anchor => PlanarDistance(
                        anchor.Position,
                        player.position))
                    .OrderBy(distance => distance)
                    .ToArray();
                int anchorsInSpawnBand = plan.SpawnAnchors.Count(
                    anchor => PlanarDistance(
                        anchor.Position,
                        player.position) >=
                              CityPedestrianDirector.MinimumSpawnDistance &&
                              PlanarDistance(
                                  anchor.Position,
                                  player.position) <=
                              CityPedestrianDirector.MaximumSpawnDistance);
                Physics.SyncTransforms();
                director = CityPedestrianFactory.Create(
                    root.transform,
                    plan,
                    player,
                    CityPedestrianPlanner.CreateWalkableArea(plan),
                    CityPedestrianResources.LoadPrefab(),
                    () => false);

                const float encounterBudget = 60f;
                const float simulationStep = 0.1f;
                bool encountered = false;
                int maximumActiveCount = 0;
                float closestActorDistance = float.PositiveInfinity;
                float elapsed = 0f;
                while (!encountered && elapsed < encounterBudget)
                {
                    director.Advance(simulationStep);
                    elapsed += simulationStep;
                    maximumActiveCount = Mathf.Max(
                        maximumActiveCount,
                        director.ActiveCount);
                    for (int index = 0;
                         index < director.Actors.Count;
                         index++)
                    {
                        CityPedestrianActor actor =
                            director.Actors[index];
                        if (!actor.IsSpawned)
                        {
                            continue;
                        }

                        float actorDistance = PlanarDistance(
                            actor.Position,
                            player.position);
                        closestActorDistance = Mathf.Min(
                            closestActorDistance,
                            actorDistance);
                        if (actorDistance <=
                            CityPedestrianDirector
                                .InitialApproachCompletionDistance + 0.01f)
                        {
                            encountered = true;
                            break;
                        }
                    }
                }

                Assert.That(
                    encountered,
                    Is.True,
                    "The production home-return graph must deliver a " +
                    "walker to a stationary player within the daytime " +
                    $"encounter budget. Anchors in band: " +
                    $"{anchorsInSpawnBand}; maximum active: " +
                    $"{maximumActiveCount}; closest distance: " +
                    $"{closestActorDistance:0.00} m; closest graph node: " +
                    $"{closestPlanNode.Id} at " +
                    $"{closestPlanNodeDistance:0.00} m; player-component " +
                    $"anchor distances: " +
                    $"{string.Join(",", playerComponentAnchorDistances)}; " +
                    "actors: " +
                    string.Join(
                        "; ",
                        director.Actors.Select(actor =>
                            $"{actor.Position} target=" +
                            $"{actor.TargetNodeIndex} previous=" +
                            $"{actor.PreviousNodeIndex} state=" +
                            $"{actor.MotionState}")));
            }
            finally
            {
                director?.Shutdown();
                UnityEngine.Object.DestroyImmediate(root);
            }
        }

        [Test]
        public void Factory_DelaysSpawnUntilStaticOverlapClears()
        {
            GameObject root = new GameObject("Overlap Test Root");
            GameObject blocker = null;
            CityPedestrianDirector director = null;
            try
            {
                Transform player = CreatePlayer(root.transform);
                CityPedestrianPlan plan = CreateDistanceSpawnPlan(
                    new[] { new Vector3(0f, 0f, -76f) });
                blocker = GameObject.CreatePrimitive(PrimitiveType.Cube);
                blocker.name = "Spawn Blocker";
                blocker.transform.SetParent(root.transform, false);
                blocker.transform.position = new Vector3(0f, 0.85f, -76f);
                blocker.transform.localScale = new Vector3(2f, 2f, 2f);
                Physics.SyncTransforms();

                director = CityPedestrianFactory.Create(
                    root.transform,
                    plan,
                    player,
                    CityPedestrianPlanner.CreateWalkableArea(plan),
                    CityPedestrianResources.LoadPrefab(),
                    () => false);
                AdvanceToNextSpawn(director);

                Assert.That(director.ActiveCount, Is.Zero);
                Assert.That(director.Actors[0].CollisionEnabled, Is.False);

                UnityEngine.Object.DestroyImmediate(blocker);
                blocker = null;
                Physics.SyncTransforms();
                AdvanceToNextSpawn(director);

                Assert.That(director.ActiveCount, Is.EqualTo(1));
                Assert.That(director.Actors[0].CollisionEnabled, Is.True);
            }
            finally
            {
                director?.Shutdown();
                if (blocker != null)
                {
                    UnityEngine.Object.DestroyImmediate(blocker);
                }

                UnityEngine.Object.DestroyImmediate(root);
            }
        }

        [Test]
        public void HeadOnActors_UseStableSlotOrderToAvoidMutualYield()
        {
            GameObject root = new GameObject("Yield Priority Test Root");
            CityPedestrianDirector director = null;
            try
            {
                Transform player = CreatePlayer(root.transform);
                CityPedestrianPlan plan = CreateHeadOnSpawnPlan();
                director = CityPedestrianFactory.Create(
                    root.transform,
                    plan,
                    player,
                    CityPedestrianPlanner.CreateWalkableArea(plan),
                    CityPedestrianResources.LoadPrefab(),
                    () => false);
                AdvanceToNextSpawn(director);
                AdvanceToNextSpawn(director);

                Assert.That(director.ActiveCount, Is.EqualTo(2));
                for (int index = 0; index < director.Actors.Count; index++)
                {
                    CityPedestrianActor actor = director.Actors[index];
                    actor.transform.position = new Vector3(
                        actor.TravelDirection.x > 0f ? -0.35f : 0.35f,
                        0f,
                        -72f);
                }

                Physics.SyncTransforms();
                Assert.That(
                    Vector3.Dot(
                        director.Actors[0].TravelDirection,
                        director.Actors[1].TravelDirection),
                    Is.LessThan(-0.9f));
                director.Advance(0.1f);

                Assert.That(director.Actors[0].IsYielding, Is.False);
                Assert.That(
                    director.Actors[0].LastDisplacement.sqrMagnitude,
                    Is.GreaterThan(0f));
                Assert.That(director.Actors[1].IsYielding, Is.True);
                Assert.That(
                    director.Actors[1].LastDisplacement,
                    Is.EqualTo(Vector3.zero));
            }
            finally
            {
                director?.Shutdown();
                UnityEngine.Object.DestroyImmediate(root);
            }
        }

        [Test]
        public void CollisionLayer_IsPhysicalButExcludedFromQueries()
        {
            Assert.That(
                LayerMask.NameToLayer(CityPedestrianCollision.LayerName),
                Is.EqualTo(CityPedestrianCollision.LayerIndex));
            CityPedestrianCollision.EnsureRuntimePolicy();
            Assert.That(
                Physics.GetIgnoreLayerCollision(
                    CityPedestrianCollision.DefaultLayerIndex,
                    CityPedestrianCollision.LayerIndex),
                Is.False);
            Assert.That(
                Physics.GetIgnoreLayerCollision(
                    CityPedestrianCollision.LayerIndex,
                    CityPedestrianCollision.LayerIndex),
                Is.True);

            int pedestrianBit =
                1 << CityPedestrianCollision.LayerIndex;
            int busBit = 1 << CityBusCollision.LayerIndex;
            Assert.That(
                PlayerInteractor.InteractionLayerMask & pedestrianBit,
                Is.Zero);
            Assert.That(
                PlayerInteractor.InteractionLayerMask & busBit,
                Is.Zero);

            GameObject root = new GameObject("Camera Mask Test Root");
            try
            {
                Camera camera = root.AddComponent<Camera>();
                GameObject target = new GameObject("Target");
                target.transform.SetParent(root.transform, false);
                PlayerCameraFollow follow =
                    root.AddComponent<PlayerCameraFollow>();
                follow.Initialize(camera, target.transform, false);
                Assert.That(
                    follow.CollisionLayerMask & pedestrianBit,
                    Is.Zero);
                Assert.That(
                    follow.CollisionLayerMask & busBit,
                    Is.Zero);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(root);
            }
        }

        [Test]
        public void Director_IgnoresCameraAndReleasesBeyondPlayerRange()
        {
            GameObject root = new GameObject("Distance Lifecycle Test Root");
            CityPedestrianDirector director = null;
            try
            {
                Transform player = CreatePlayer(root.transform);
                Camera camera = CreateTestCamera(root.transform);
                Vector3 spawnPosition = new Vector3(0f, 0f, -76f);
                camera.transform.LookAt(
                    spawnPosition +
                    (Vector3.up *
                     CityPedestrianActor.CollisionCenterHeight));
                CityPedestrianPlan plan = CreateDistanceSpawnPlan(
                    new[] { spawnPosition });
                director = CityPedestrianFactory.Create(
                    root.transform,
                    plan,
                    player,
                    CityPedestrianPlanner.CreateWalkableArea(plan),
                    CityPedestrianResources.LoadPrefab(),
                    () => false);
                AdvanceToNextSpawn(director);

                Assert.That(director.ActiveCount, Is.EqualTo(1));
                CityPedestrianActor actor = director.Actors[0];
                Vector3 viewport = camera.WorldToViewportPoint(
                    actor.Position +
                    (Vector3.up *
                     CityPedestrianActor.CollisionCenterHeight));
                Assert.That(
                    viewport.z,
                    Is.GreaterThan(0f));
                Assert.That(viewport.x, Is.InRange(0f, 1f));
                Assert.That(viewport.y, Is.InRange(0f, 1f));

                camera.transform.rotation = Quaternion.identity;
                director.Advance(0.2f);
                Assert.That(
                    director.ActiveCount,
                    Is.EqualTo(1),
                    "Camera direction must not affect pedestrian lifetime.");

                player.position = actor.Position +
                    (Vector3.forward *
                     (CityPedestrianDirector.DespawnDistance + 0.01f));
                director.Advance(0f);
                Assert.That(
                    director.ActiveCount,
                    Is.Zero,
                    "A pedestrian must despawn beyond the player radius.");
                Assert.That(
                    director.TimeUntilNextSpawn,
                    Is.InRange(
                        CityPedestrianDirector.MinimumFillSpawnDelay,
                        CityPedestrianDirector.MaximumFillSpawnDelay),
                    "A released slot drops the population below target, so " +
                    "its replacement uses the short randomized fill delay.");
                Assert.That(
                    actor.MotionState,
                    Is.EqualTo(CityPedestrianMotionState.Dormant));
                Assert.That(actor.SpawnAnchorId, Is.Empty);
                Assert.That(actor.CollisionEnabled, Is.False);
            }
            finally
            {
                director?.Shutdown();
                UnityEngine.Object.DestroyImmediate(root);
            }
        }

        [Test]
        public void BlockedActor_TurnsBackInsteadOfStayingJammed()
        {
            GameObject root = new GameObject("Deadlock Test Root");
            CityPedestrianActor actor = null;
            CityPedestrianPresentation presentation = null;
            try
            {
                CityPedestrianPlan plan = CreateApproachBranchPlan();
                actor = CreateBoundActor(
                    root.transform,
                    plan,
                    plan.SpawnAnchors[0],
                    1,
                    out presentation,
                    0xA341316Cu);
                int blockedTarget = actor.TargetNodeIndex;
                int blockedPrevious = actor.PreviousNodeIndex;

                // A yield nothing ever clears is the shape of two walkers nose
                // to nose on a pavement too narrow to pass on.
                const float step = 0.1f;
                for (float elapsed = 0f;
                     elapsed <
                     CityPedestrianActor.BlockedEscapeSeconds - step;
                     elapsed += step)
                {
                    actor.Advance(step, true);
                }

                Assert.That(
                    actor.DetourCount,
                    Is.Zero,
                    "A brief wait must not be treated as a deadlock.");
                Assert.That(
                    actor.TargetNodeIndex,
                    Is.EqualTo(blockedTarget));

                actor.Advance(step * 2f, true);

                Assert.That(
                    actor.DetourCount,
                    Is.EqualTo(1),
                    "A walker held indefinitely must give up and turn back.");
                Assert.That(
                    actor.TargetNodeIndex,
                    Is.EqualTo(blockedPrevious),
                    "Turning back means heading for the node behind it.");
                Assert.That(
                    actor.PreviousNodeIndex,
                    Is.EqualTo(blockedTarget));
                Assert.That(
                    actor.MotionState,
                    Is.EqualTo(CityPedestrianMotionState.Walking));
                Assert.That(actor.BlockedTime, Is.Zero);
            }
            finally
            {
                ReleaseBoundActor(actor, presentation, root.transform);
                UnityEngine.Object.DestroyImmediate(root);
            }
        }

        [Test]
        public void Actor_QueuesAndLeansAsideWithoutLeavingItsLane()
        {
            GameObject root = new GameObject("Avoidance Test Root");
            CityPedestrianActor actor = null;
            CityPedestrianPresentation presentation = null;
            try
            {
                CityPedestrianPlan plan = CreateApproachBranchPlan();
                actor = CreateBoundActor(
                    root.transform,
                    plan,
                    plan.SpawnAnchors[0],
                    1,
                    out presentation,
                    0xA341316Cu);

                actor.SetAvoidance(1f, 0f);
                actor.Advance(0.25f);
                float openPace = actor.LastDisplacement.magnitude;
                Assert.That(openPace, Is.GreaterThan(0f));

                // Queueing behind someone slower keeps a walker moving; the
                // old behaviour stopped dead and set off again.
                actor.SetAvoidance(0.4f, 1f);
                actor.Advance(0.25f);
                float queuedPace = actor.LastDisplacement.magnitude;
                Assert.That(
                    queuedPace,
                    Is.GreaterThan(0f),
                    "Giving way must not mean standing still.");
                Assert.That(
                    queuedPace,
                    Is.LessThan(openPace),
                    "A queued walker must fall in behind, not push through.");

                for (int step = 0; step < 12; step++)
                {
                    actor.Advance(0.1f);
                }

                Assert.That(
                    Mathf.Abs(actor.LateralOffset),
                    Is.GreaterThan(0.01f),
                    "The walker must actually lean aside.");
                Assert.That(
                    Mathf.Abs(actor.LateralOffset),
                    Is.LessThanOrEqualTo(
                        CityPedestrianActor.MaximumLateralOffset + 0.0001f),
                    "A 1 m pavement affords a shoulder-shift, no more.");

                actor.SetAvoidance(1f, 0f);
                for (int step = 0; step < 12; step++)
                {
                    actor.Advance(0.1f);
                }

                Assert.That(
                    Mathf.Abs(actor.LateralOffset),
                    Is.LessThan(0.01f),
                    "A walker must re-centre once the way is clear.");
            }
            finally
            {
                ReleaseBoundActor(actor, presentation, root.transform);
                UnityEngine.Object.DestroyImmediate(root);
            }
        }

        [Test]
        public void Director_SpreadsPopulationAcrossLanesAndDirections()
        {
            GameObject root = new GameObject("Dispersion Test Root");
            CityPedestrianDirector director = null;
            try
            {
                CityPedestrianPopulationProfile profile =
                    CityPedestrianPopulationProfile.City;
                Transform player = CreatePlayer(root.transform);
                CityPedestrianPlan plan = CreateDistanceSpawnPlan(
                    CreateRingAnchorPositions(
                        profile.DaytimePopulation));
                director = CityPedestrianFactory.Create(
                    root.transform,
                    plan,
                    player,
                    CityPedestrianPlanner.CreateWalkableArea(plan),
                    profile,
                    () => false);
                for (int step = 0;
                     step <= profile.DaytimePopulation &&
                     director.ActiveCount < profile.DaytimePopulation;
                     step++)
                {
                    AdvanceToNextSpawn(director);
                }

                CityPedestrianActor[] active = director.Actors
                    .Where(candidate => candidate.IsSpawned)
                    .ToArray();
                Assert.That(
                    active.Length,
                    Is.EqualTo(profile.DaytimePopulation));

                // Every anchor in this plan is its own lane, so a full
                // population must occupy that many distinct lanes rather than
                // stacking on one street.
                Assert.That(
                    active.Select(candidate => candidate.SpawnAnchorId)
                        .Distinct(StringComparer.Ordinal)
                        .Count(),
                    Is.EqualTo(active.Length));
                for (int first = 0; first < active.Length; first++)
                {
                    for (int second = first + 1;
                         second < active.Length;
                         second++)
                    {
                        Assert.That(
                            PlanarDistance(
                                active[first].Position,
                                active[second].Position),
                            Is.GreaterThan(profile.MinimumPeerSeparation),
                            "Dispersion must keep the population spread out.");
                    }
                }

                // Only a small share may be steered at the hero; the rest take
                // a seeded direction, so the street shows opposing streams.
                Assert.That(
                    director.ApproachGuidedCount,
                    Is.LessThanOrEqualTo(profile.ApproachGuidedPopulation));
                Assert.That(
                    profile.ApproachGuidedPopulation,
                    Is.LessThan(profile.DaytimePopulation));

                int inbound = 0;
                int outbound = 0;
                for (int index = 0; index < active.Length; index++)
                {
                    Vector3 travel = active[index].TravelDirection;
                    Vector3 toPlayer = player.position - active[index].Position;
                    toPlayer.y = 0f;
                    if (travel.sqrMagnitude <= 0.0001f ||
                        toPlayer.sqrMagnitude <= 0.0001f)
                    {
                        continue;
                    }

                    if (Vector3.Dot(travel.normalized, toPlayer.normalized) >
                        0f)
                    {
                        inbound++;
                    }
                    else
                    {
                        outbound++;
                    }
                }

                Assert.That(
                    inbound + outbound,
                    Is.GreaterThan(0),
                    "The population must actually be walking.");
            }
            finally
            {
                director?.Shutdown();
                UnityEngine.Object.DestroyImmediate(root);
            }
        }

        private static void AssertPassiveSharedPresentation(
            GameObject prefab,
            CityPedestrianAssetRegistry registry,
            Material sharedMaterial)
        {
            Assert.That(
                prefab.GetComponentsInChildren<Collider>(true),
                Is.Empty);
            Assert.That(
                prefab.GetComponentsInChildren<Rigidbody>(true),
                Is.Empty);
            // A walker is light-free unless it registers exactly one worn
            // lamp, which must stay a bounded shadowless Spot on the head.
            Light[] lights = prefab.GetComponentsInChildren<Light>(true);
            if (registry.HeadLamp == null)
            {
                Assert.That(lights, Is.Empty);
            }
            else
            {
                Assert.That(lights, Has.Length.EqualTo(1));
                Assert.That(lights[0], Is.SameAs(registry.HeadLamp));
                Assert.That(
                    registry.HeadLamp.type,
                    Is.EqualTo(LightType.Spot));
                Assert.That(
                    registry.HeadLamp.shadows,
                    Is.EqualTo(LightShadows.None));
                Assert.That(
                    registry.HeadLamp.range,
                    Is.LessThanOrEqualTo(8f));
                Assert.That(
                    registry.HeadLamp.transform.parent,
                    Is.SameAs(registry.HeadAnchor),
                    "The worn lamp must follow the animated head bone.");
            }

            Assert.That(
                prefab.GetComponentsInChildren<MonoBehaviour>(true)
                    .Any(behaviour => behaviour is IInteractable),
                Is.False);
            for (int index = 0; index < registry.Renderers.Count; index++)
            {
                Material[] materials =
                    registry.Renderers[index].sharedMaterials;
                Assert.That(materials, Is.Not.Empty);
                Assert.That(
                    materials.All(material => material == sharedMaterial),
                    Is.True);
            }
        }

        private static void AssertWheelchairVisualDimensions(
            GameObject prefab)
        {
            GameObject instance = UnityEngine.Object.Instantiate(prefab);
            try
            {
                instance.transform.SetPositionAndRotation(
                    Vector3.zero,
                    Quaternion.identity);
                instance.transform.localScale = Vector3.one;
                Renderer[] renderers =
                    instance.GetComponentsInChildren<Renderer>(true);
                Renderer leftWheel = renderers.Single(renderer =>
                    renderer.name == "ACC_WheelTyre.L");
                Renderer rightWheel = renderers.Single(renderer =>
                    renderer.name == "ACC_WheelTyre.R");
                Renderer seat = renderers.Single(renderer =>
                    renderer.name == "ACC_SeatCushion");

                foreach (Renderer wheel in new[] { leftWheel, rightWheel })
                {
                    Assert.That(
                        wheel.bounds.size.x,
                        Is.InRange(0.04f, 0.075f),
                        $"{wheel.name} has a shrunken tyre width.");
                    Assert.That(
                        wheel.bounds.size.y,
                        Is.InRange(0.55f, 0.65f),
                        $"{wheel.name} has a shrunken diameter.");
                    Assert.That(
                        wheel.bounds.size.z,
                        Is.InRange(0.55f, 0.65f),
                        $"{wheel.name} has a shrunken diameter.");
                    Assert.That(
                        wheel.bounds.min.y,
                        Is.EqualTo(instance.transform.position.y)
                            .Within(0.025f),
                        $"{wheel.name} no longer contacts the ground.");
                }

                Assert.That(
                    seat.bounds.size.x,
                    Is.InRange(0.40f, 0.48f));
                Assert.That(
                    seat.bounds.size.y,
                    Is.InRange(0.05f, 0.09f));
                Assert.That(
                    seat.bounds.size.z,
                    Is.InRange(0.38f, 0.48f));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(instance);
            }
        }

        private static void AssertRuntimeCollisionContract(
            CityPedestrianDirector director)
        {
            Assert.That(
                director.Actors.Count(actor => actor.CollisionEnabled),
                Is.EqualTo(director.ActiveCount));
            for (int index = 0; index < director.Actors.Count; index++)
            {
                CityPedestrianActor actor = director.Actors[index];
                CharacterController controller = actor.CharacterController;
                Assert.That(controller, Is.Not.Null);
                Assert.That(
                    actor.gameObject.layer,
                    Is.EqualTo(CityPedestrianCollision.LayerIndex));
                Assert.That(
                    controller.height,
                    Is.EqualTo(
                        CityPedestrianActor.CollisionHeight).Within(0.0001f));
                Assert.That(
                    controller.radius,
                    Is.EqualTo(actor.AgentRadius).Within(0.0001f));
                Assert.That(
                    controller.center,
                    Is.EqualTo(new Vector3(
                        0f,
                        CityPedestrianActor.CollisionCenterHeight,
                        0f)));
                Assert.That(
                    actor.CollisionEnabled,
                    Is.EqualTo(actor.HasPresentation));
            }
        }

        private static void AssertActorRootIsUpright(
            CityPedestrianActor actor)
        {
            Assert.That(
                Vector3.Dot(actor.transform.up, Vector3.up),
                Is.EqualTo(1f).Within(0.0001f));
            Assert.That(
                actor.transform.forward.y,
                Is.EqualTo(0f).Within(0.0001f));
        }

        private static void AssertSolePresentationWiring(GameObject prefab)
        {
            // Deliberately narrow. A PlayableGraph writes no transforms in a
            // batch-mode EditMode run and AnimationClip.SampleAnimation drives
            // the rig on a different path than the runtime Avatar does, so a
            // motion claim made here would be either inert or misleading.
            // Grounding and hop height are proved instead by the deterministic
            // Blender validator and asserted from its shipped manifest in
            // AssertLocomotionManifestContract.
            GameObject instance = UnityEngine.Object.Instantiate(prefab);
            CityPedestrianPresentation presentation = null;
            try
            {
                CityPedestrianAssetRegistry registry =
                    instance.GetComponent<CityPedestrianAssetRegistry>();
                presentation =
                    instance.AddComponent<CityPedestrianPresentation>();
                presentation.Initialize(registry);
                presentation.SetMoving(true);
                Assert.That(presentation.WalkWeight, Is.EqualTo(1f));
                Assert.That(
                    float.IsFinite(GetLowestSoleHeight(registry)),
                    Is.True,
                    "The design exposes no sole renderer for grounding.");
                presentation.SetMoving(false);
                Assert.That(presentation.WalkWeight, Is.EqualTo(0f));
            }
            finally
            {
                presentation?.Shutdown();
                UnityEngine.Object.DestroyImmediate(instance);
            }
        }

        private static void AssertLocomotionManifestContract(
            string designId,
            string idleClipName,
            string walkClipName)
        {
            // The generator writes proven per-frame numbers into this manifest
            // and it ships beside the FBX, so asserting it here is a real
            // check on the data Unity actually imports.
            var manifest = JsonUtility.FromJson<LocomotionManifest>(
                System.IO.File.ReadAllText(LocomotionManifestPath));
            Assert.That(manifest, Is.Not.Null);
            Assert.That(manifest.clips, Is.Not.Null);

            LocomotionClip[] owned = manifest.clips
                .Where(clip => string.Equals(
                    clip.archetype,
                    designId,
                    StringComparison.Ordinal))
                .ToArray();
            Assert.That(
                CityPedestrianResources.TryGetArchetype(
                    designId,
                    out CityPedestrianArchetype archetype),
                Is.True);
            string sitClipName = idleClipName.Substring(
                0,
                idleClipName.Length - "Idle".Length) + "Sit";
            CollectionAssert.AreEquivalent(
                archetype.CanRideBus
                    ? new[] { idleClipName, walkClipName, sitClipName }
                    : new[] { idleClipName, walkClipName },
                owned.Select(clip => clip.name).ToArray(),
                "A design owns its two locomotion clips, plus one seated loop " +
                "when it declares a Route 01 ride.");

            bool airborne = Resources.Load<GameObject>(
                    ArchetypeResourcePath(designId))
                .GetComponent<CityPedestrianAssetRegistry>()
                .PreservesAirborneMotion;
            for (int index = 0; index < owned.Length; index++)
            {
                LocomotionClip clip = owned[index];
                if (clip.seated)
                {
                    // A seated clip leaves the pavement plane on purpose, so
                    // it proves cabin fit instead of sole contact: measured
                    // headroom above the seated pelvis, and nothing hanging
                    // past the 0.41 m cushion height below it.
                    Assert.That(
                        clip.seated_headroom_m,
                        Is.EqualTo(archetype.SeatedRide.SeatedHeadroom)
                            .Within(0.04f),
                        $"{clip.name} does not match its declared seated " +
                        "headroom.");
                    Assert.That(
                        clip.seated_drop_m,
                        Is.LessThanOrEqualTo(0.41f),
                        $"{clip.name} would pass through the cabin floor.");

                    // The runtime aligns the shared rest pelvis to the cushion
                    // anchor, so the declared lift has to match how far this
                    // design's own seated hips reach below that bone. A lift
                    // under the measurement buries the design in the seat -
                    // the nominal 0.015 sank the catalog by 4.6-11.1 cm - and
                    // a lift above it leaves the design hovering.
                    Assert.That(
                        archetype.SeatedRide.SeatLift,
                        Is.LessThanOrEqualTo(clip.seated_contact_m + 0.001f),
                        $"{clip.name} would hover above the cushion.");
                    Assert.That(
                        archetype.SeatedRide.SeatLift,
                        Is.GreaterThanOrEqualTo(
                            clip.seated_contact_m - 0.03f),
                        $"{clip.name} sinks more than 3 cm into the cushion; " +
                        $"its hips reach {clip.seated_contact_m:F4} m below " +
                        "the pelvis the runtime aligns.");
                    continue;
                }

                Assert.That(
                    clip.ground_min_m,
                    Is.GreaterThanOrEqualTo(-0.002f),
                    $"{clip.name} penetrates the pavement.");
                if (!airborne)
                {
                    Assert.That(
                        clip.ground_max_contact_gap_m,
                        Is.LessThanOrEqualTo(0.002f),
                        $"{clip.name} loses its grounded sole.");
                }
            }

            if (!airborne)
            {
                return;
            }

            float apex = owned.Max(clip => clip.apex_lift_m);
            Assert.That(
                apex,
                Is.InRange(0.08f, 0.40f),
                "An airborne design must ship a real hop, not a shuffle.");
        }

        private static string ArchetypeResourcePath(string designId)
        {
            CityPedestrianResources.TryGetArchetype(
                designId,
                out CityPedestrianArchetype archetype);
            return archetype.PrefabResourcePath;
        }

        [Serializable]
        private sealed class LocomotionManifest
        {
            public LocomotionClip[] clips;
        }

        [Serializable]
        private sealed class PedestrianModelManifest
        {
            public string design_id;
            public int mesh_count;
            public int triangle_count;
            public int[] triangle_budget;
            public string[] shared_clips;
            public bool staged;
            public bool pool_eligible;
            public float wheel_radius_m;
            public string[] pivot_names;
        }

        [Serializable]
        private sealed class LocomotionClip
        {
            public bool seated;
            public float seated_headroom_m;
            public float seated_drop_m;
            public float seated_contact_m;
            public string name;
            public string archetype;
            public float ground_min_m;
            public float ground_max_contact_gap_m;
            public float apex_lift_m;
            public float duration_seconds;
            public bool loop;
            public bool in_place;
            public int keyed_bone_count;
            public float wheel_ground_min_m;
            public float wheel_ground_max_contact_gap_m;
            public float footrest_min_clearance_m;
            public float rim_hand_max_distance_m;
        }

        private static float GetLowestSoleHeight(
            CityPedestrianAssetRegistry registry)
        {
            float lowest = float.PositiveInfinity;
            int soleRendererCount = 0;
            for (int index = 0;
                 index < registry.RendererBindings.Count;
                 index++)
            {
                CityPedestrianRendererBinding binding =
                    registry.RendererBindings[index];
                if (binding == null ||
                    !IsSoleRenderer(binding.RendererName) ||
                    !(binding.Renderer is SkinnedMeshRenderer renderer))
                {
                    continue;
                }

                soleRendererCount++;
                Mesh baked = new Mesh();
                try
                {
                    renderer.BakeMesh(baked);
                    Vector3[] vertices = baked.vertices;
                    for (int vertex = 0;
                         vertex < vertices.Length;
                         vertex++)
                    {
                        lowest = Mathf.Min(
                            lowest,
                            renderer.transform
                                .TransformPoint(vertices[vertex]).y);
                    }
                }
                finally
                {
                    UnityEngine.Object.DestroyImmediate(baked);
                }
            }

            Assert.That(
                soleRendererCount,
                Is.EqualTo(2),
                "The production pedestrian must expose both sole meshes.");
            Assert.That(float.IsPositiveInfinity(lowest), Is.False);
            return lowest;
        }

        private static bool IsSoleRenderer(string rendererName)
        {
            if (string.IsNullOrEmpty(rendererName))
            {
                return false;
            }

            return rendererName.IndexOf(
                       "BootSole",
                       StringComparison.Ordinal) >= 0 ||
                   rendererName.IndexOf(
                       "ShoeSole",
                       StringComparison.Ordinal) >= 0;
        }

        private static string NormalizeAnimationClipName(string clipName)
        {
            if (string.IsNullOrEmpty(clipName))
            {
                return clipName;
            }

            int separator = clipName.LastIndexOf('|');
            return separator >= 0 && separator + 1 < clipName.Length
                ? clipName.Substring(separator + 1)
                : clipName;
        }

        private static CityPedestrianActor CreateBoundActor(
            Transform parent,
            CityPedestrianPlan plan,
            CityPedestrianSpawnAnchor anchor,
            int targetNodeIndex,
            out CityPedestrianPresentation presentation,
            uint behaviorSeed = 1u)
        {
            GameObject actorObject = new GameObject("Pedestrian Actor");
            actorObject.layer = CityPedestrianCollision.LayerIndex;
            actorObject.transform.SetParent(parent, false);
            CityPedestrianActor actor =
                actorObject.AddComponent<CityPedestrianActor>();
            actor.Initialize(
                CityPedestrianPlanner.CreateWalkableArea(plan),
                plan.AgentRadius);

            CityPedestrianAssetRegistry registry =
                CityPedestrianResources.Instantiate(parent);
            presentation = registry.GetComponent<
                CityPedestrianPresentation>();
            if (presentation == null)
            {
                presentation = registry.gameObject.AddComponent<
                    CityPedestrianPresentation>();
            }

            presentation.Initialize(registry);
            actor.PrepareSpawn(
                plan,
                anchor,
                targetNodeIndex,
                1f,
                0.91f,
                0f,
                0,
                behaviorSeed);
            actor.BindPresentation(presentation);
            return actor;
        }

        private static void ReleaseBoundActor(
            CityPedestrianActor actor,
            CityPedestrianPresentation presentation,
            Transform poolRoot)
        {
            if (actor != null && actor.IsSpawned)
            {
                actor.ReleasePresentation(poolRoot);
            }

            presentation?.Shutdown();
        }

        private static Transform CreatePlayer(Transform parent)
        {
            GameObject player = new GameObject("Player");
            player.transform.SetParent(parent, false);
            return player.transform;
        }

        private static Camera CreateTestCamera(Transform parent)
        {
            GameObject cameraObject = new GameObject("Camera");
            cameraObject.transform.SetParent(parent, false);
            cameraObject.transform.position = new Vector3(
                0f,
                CityPedestrianActor.CollisionCenterHeight,
                0f);
            cameraObject.transform.rotation = Quaternion.identity;
            Camera camera = cameraObject.AddComponent<Camera>();
            camera.nearClipPlane = 0.1f;
            camera.farClipPlane = 100f;
            camera.fieldOfView = 60f;
            camera.aspect = 1f;
            return camera;
        }

        private static CityPedestrianPlan CreateCornerPlan()
        {
            return new CityPedestrianPlan(
                1,
                2,
                3u,
                CityPedestrianPlanner.AgentRadius,
                new[]
                {
                    new CityPedestrianNode(
                        "approach",
                        new Vector3(-1f, 0f, 0f),
                        false),
                    new CityPedestrianNode(
                        "corner",
                        Vector3.zero,
                        false),
                    new CityPedestrianNode(
                        "exit",
                        new Vector3(0f, 0f, 1f),
                        false)
                },
                new[]
                {
                    new CityPedestrianLink(
                        "approach",
                        0,
                        1,
                        CityPedestrianLinkKind.Sidewalk),
                    new CityPedestrianLink(
                        "turn",
                        1,
                        2,
                        CityPedestrianLinkKind.Turn)
                },
                new[]
                {
                    new CityPedestrianSpawnAnchor(
                        "spawn:approach",
                        new Vector3(-0.5f, 0f, 0f),
                        0,
                        1)
                },
                new[] { Rect.MinMaxRect(-2f, -1f, 1f, 2f) });
        }

        private static CityPedestrianPlan CreateCrosswalkChoicePlan()
        {
            return new CityPedestrianPlan(
                1,
                2,
                3u,
                CityPedestrianPlanner.AgentRadius,
                new[]
                {
                    new CityPedestrianNode(
                        "approach",
                        new Vector3(-2f, 0f, 0f),
                        false),
                    new CityPedestrianNode(
                        "zebra",
                        Vector3.zero,
                        true),
                    new CityPedestrianNode(
                        "continue",
                        new Vector3(2f, 0f, 0f),
                        false),
                    new CityPedestrianNode(
                        "cross",
                        new Vector3(0f, 0f, 2f),
                        false)
                },
                new[]
                {
                    new CityPedestrianLink(
                        "approach",
                        0,
                        1,
                        CityPedestrianLinkKind.Sidewalk),
                    new CityPedestrianLink(
                        "continue",
                        1,
                        2,
                        CityPedestrianLinkKind.Sidewalk),
                    new CityPedestrianLink(
                        "cross",
                        1,
                        3,
                        CityPedestrianLinkKind.Crosswalk)
                },
                new[]
                {
                    new CityPedestrianSpawnAnchor(
                        "spawn:approach",
                        new Vector3(-1f, 0f, 0f),
                        0,
                        1)
                },
                new[] { Rect.MinMaxRect(-3f, -1f, 3f, 3f) });
        }

        /// <summary>
        /// Rings the player with anchors inside the fog-hidden spawn band,
        /// spaced well beyond the director's dispersion separation so a full
        /// population can activate.
        /// </summary>
        private static Vector3[] CreateRingAnchorPositions(int count)
        {
            var positions = new Vector3[count];
            for (int index = 0; index < count; index++)
            {
                float radius = index % 2 == 0 ? 78f : 84f;
                float angle = index * (2f * Mathf.PI / count);
                positions[index] = new Vector3(
                    Mathf.Sin(angle) * radius,
                    0f,
                    Mathf.Cos(angle) * radius);
            }

            return positions;
        }

        private static float ConservativeFogTransmittance(
            float spawnDistance)
        {
            const float productionAspect = 16f / 9f;
            const float widestProductionFieldOfView = 70f;
            const float conservativeCameraAndVisualDepth = 6f;
            float verticalTangent = Mathf.Tan(
                widestProductionFieldOfView * 0.5f * Mathf.Deg2Rad);
            float horizontalTangent = verticalTangent * productionAspect;
            float cornerDepthRatio = 1f / Mathf.Sqrt(
                1f +
                (verticalTangent * verticalTangent) +
                (horizontalTangent * horizontalTangent));
            float depth = (spawnDistance * cornerDepthRatio) -
                          conservativeCameraAndVisualDepth;
            return Mathf.Exp(
                -Mathf.Pow(
                    RuntimeSceneSetup.CityFogDensity * depth,
                    2f));
        }

        private static CityPedestrianPlan CreateDistanceSpawnPlan(
            IReadOnlyList<Vector3> anchorPositions)
        {
            var nodes = new List<CityPedestrianNode>(
                anchorPositions.Count * 2);
            var links = new List<CityPedestrianLink>(
                anchorPositions.Count);
            var anchors = new List<CityPedestrianSpawnAnchor>(
                anchorPositions.Count);
            var rectangles = new List<Rect>(anchorPositions.Count);
            for (int index = 0; index < anchorPositions.Count; index++)
            {
                Vector3 anchorPosition = anchorPositions[index];
                int firstNode = nodes.Count;
                int secondNode = firstNode + 1;
                nodes.Add(new CityPedestrianNode(
                    $"route:{index}:first",
                    anchorPosition + (Vector3.back * 4f),
                    false));
                nodes.Add(new CityPedestrianNode(
                    $"route:{index}:second",
                    anchorPosition + (Vector3.forward * 4f),
                    false));
                links.Add(new CityPedestrianLink(
                    $"route:{index}",
                    firstNode,
                    secondNode,
                    CityPedestrianLinkKind.Sidewalk));
                // Mirrors the production ID shape `…:<lane>:<segment>`, so
                // each generated route reads as its own sidewalk lane for the
                // director's dispersion rule.
                anchors.Add(new CityPedestrianSpawnAnchor(
                    $"spawn:lane:{index}:0",
                    anchorPosition,
                    firstNode,
                    secondNode));
                rectangles.Add(Rect.MinMaxRect(
                    anchorPosition.x - 1f,
                    anchorPosition.z - 5f,
                    anchorPosition.x + 1f,
                    anchorPosition.z + 5f));
            }

            return new CityPedestrianPlan(
                1,
                2,
                3u,
                CityPedestrianPlanner.AgentRadius,
                nodes,
                links,
                anchors,
                rectangles);
        }

        private static CityPedestrianPlan CreateApproachBranchPlan()
        {
            return new CityPedestrianPlan(
                1,
                2,
                3u,
                CityPedestrianPlanner.AgentRadius,
                new[]
                {
                    new CityPedestrianNode(
                        "far",
                        new Vector3(0f, 0f, -80f),
                        false),
                    new CityPedestrianNode(
                        "junction",
                        new Vector3(0f, 0f, -76f),
                        false),
                    new CityPedestrianNode(
                        "near",
                        new Vector3(0f, 0f, -60f),
                        false),
                    new CityPedestrianNode(
                        "side",
                        new Vector3(20f, 0f, -76f),
                        false)
                },
                new[]
                {
                    new CityPedestrianLink(
                        "approach",
                        0,
                        1,
                        CityPedestrianLinkKind.Sidewalk),
                    new CityPedestrianLink(
                        "near",
                        1,
                        2,
                        CityPedestrianLinkKind.Sidewalk),
                    new CityPedestrianLink(
                        "side",
                        1,
                        3,
                        CityPedestrianLinkKind.Sidewalk)
                },
                new[]
                {
                    new CityPedestrianSpawnAnchor(
                        "spawn:approach",
                        new Vector3(0f, 0f, -78f),
                        0,
                        1)
                },
                new[] { Rect.MinMaxRect(-1f, -81f, 21f, -59f) });
        }

        private static CityPedestrianPlan CreateLongApproachPlan()
        {
            return new CityPedestrianPlan(
                1,
                2,
                3u,
                CityPedestrianPlanner.AgentRadius,
                new[]
                {
                    new CityPedestrianNode(
                        "far",
                        new Vector3(0f, 0f, -120f),
                        false),
                    new CityPedestrianNode(
                        "near",
                        Vector3.zero,
                        false)
                },
                new[]
                {
                    new CityPedestrianLink(
                        "approach",
                        0,
                        1,
                        CityPedestrianLinkKind.Sidewalk)
                },
                new[]
                {
                    new CityPedestrianSpawnAnchor(
                        "spawn:approach",
                        new Vector3(
                            0f,
                            0f,
                            -CityPedestrianDirector.MaximumSpawnDistance),
                        0,
                        1)
                },
                new[] { Rect.MinMaxRect(-1f, -121f, 1f, 1f) });
        }

        private static CityPedestrianPlan CreateHeadOnSpawnPlan()
        {
            return new CityPedestrianPlan(
                1,
                2,
                3u,
                CityPedestrianPlanner.AgentRadius,
                new[]
                {
                    new CityPedestrianNode(
                        "left",
                        new Vector3(-120f, 0f, -72f),
                        false),
                    new CityPedestrianNode(
                        "center",
                        new Vector3(0f, 0f, -72f),
                        false),
                    new CityPedestrianNode(
                        "right",
                        new Vector3(120f, 0f, -72f),
                        false)
                },
                new[]
                {
                    new CityPedestrianLink(
                        "left-half",
                        0,
                        1,
                        CityPedestrianLinkKind.Sidewalk),
                    new CityPedestrianLink(
                        "right-half",
                        1,
                        2,
                        CityPedestrianLinkKind.Sidewalk)
                },
                new[]
                {
                    new CityPedestrianSpawnAnchor(
                        "spawn:left",
                        new Vector3(-35f, 0f, -72f),
                        0,
                        1),
                    new CityPedestrianSpawnAnchor(
                        "spawn:right",
                        new Vector3(35f, 0f, -72f),
                        1,
                        2)
                },
                new[] { Rect.MinMaxRect(-121f, -73f, 121f, -71f) });
        }

        private static float PlanarDistance(Vector3 first, Vector3 second)
        {
            float deltaX = first.x - second.x;
            float deltaZ = first.z - second.z;
            return Mathf.Sqrt((deltaX * deltaX) + (deltaZ * deltaZ));
        }

        private static HashSet<int> CollectReachableNodes(
            CityPedestrianPlan plan,
            int startNode)
        {
            var result = new HashSet<int> { startNode };
            var pending = new Queue<int>();
            pending.Enqueue(startNode);
            while (pending.Count > 0)
            {
                int node = pending.Dequeue();
                IReadOnlyList<int> linkIndices = plan.GetLinkIndices(node);
                for (int index = 0; index < linkIndices.Count; index++)
                {
                    int other = plan.Links[linkIndices[index]].Other(node);
                    if (result.Add(other))
                    {
                        pending.Enqueue(other);
                    }
                }
            }

            return result;
        }

        private static void AdvanceToNextSpawn(
            CityPedestrianDirector director)
        {
            director.Advance(director.TimeUntilNextSpawn + 0.01f);
        }
    }
}

using System.Collections.Generic;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace BarPromenade.Tests.EditMode
{
    public sealed class SupermarketCashierAssetTests
    {
        private const string PlayerModelPath =
            "Assets/Player3D/V2/Models/PlayerCharacter3DV2.fbx";
        private const string ActivePrefabPath =
            "Assets/Supermarket/Cashier/Prefabs/SupermarketCashier.prefab";
        private const string WatcherPrefabPath =
            "Assets/Supermarket/Cashier/Prefabs/" +
            "SupermarketWatcherCashier.prefab";
        private const string WatcherDesignId = "watcher_cashier_v1";

        private static SupermarketCashierProvider LoadProvider()
        {
            var provider = SupermarketCashierProvider.Load();
            Assert.That(
                provider,
                Is.Not.Null,
                "Expected the cashier provider asset at " +
                $"Resources/{SupermarketCashierProvider.ResourcePath}.");
            return provider;
        }

        private static GameObject LoadWatcherPrefab()
        {
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(
                WatcherPrefabPath);
            Assert.That(
                prefab,
                Is.Not.Null,
                "The retained Watcher cashier prefab is missing at " +
                $"'{WatcherPrefabPath}'.");
            return prefab;
        }

        [Test]
        public void Provider_BindsOnlyTheOrdinaryPrefabWithoutPublishingIt()
        {
            SupermarketCashierProvider provider = LoadProvider();
            GameObject watcherPrefab = LoadWatcherPrefab();
            Assert.That(provider.CashierPrefab, Is.Not.Null);
            Assert.That(
                AssetDatabase.GetAssetPath(provider.CashierPrefab),
                Is.EqualTo(ActivePrefabPath));
            Assert.That(
                AssetDatabase.GetAssetPath(provider.CashierPrefab),
                Is.Not.EqualTo(AssetDatabase.GetAssetPath(watcherPrefab)),
                "The retained bizarre Watcher must not be the active " +
                "supermarket cashier.");

            // Both prefabs stay outside every addressable prefab path: only
            // the provider asset is Resources-loadable, and neither design
            // enters the pedestrian pool.
            Assert.That(
                Resources.Load<GameObject>(
                    "Supermarket/SupermarketCashier"),
                Is.Null);
            Assert.That(
                Resources.Load<GameObject>(
                    "Supermarket/SupermarketWatcherCashier"),
                Is.Null);
            Assert.That(
                Resources.Load<GameObject>(
                    "Pedestrians/SupermarketCashier3D"),
                Is.Null);
            Assert.That(
                Resources.Load<GameObject>(
                    "Pedestrians/SupermarketWatcherCashier3D"),
                Is.Null);
            Assert.That(
                CityPedestrianResources.TryGetArchetype(
                    SupermarketCashierProvider.DesignId,
                    out _),
                Is.False);
            Assert.That(
                CityPedestrianResources.TryGetArchetype(
                    WatcherDesignId,
                    out _),
                Is.False);
        }

        [Test]
        public void ActivePrefab_IsANormalFixedHumanCashier()
        {
            SupermarketCashierProvider provider = LoadProvider();
            GameObject prefab = provider.CashierPrefab;
            SupermarketCashierAssetRegistry registry =
                RequireRegistry(prefab);

            Assert.That(
                registry.DesignId,
                Is.EqualTo(SupermarketCashierProvider.DesignId));
            Assert.That(
                registry.NeckMode,
                Is.EqualTo(SupermarketCashierNeckMode.FixedHuman));
            Assert.That(registry.UsesExtensibleNeck, Is.False);
            Assert.That(registry.NeckPivots, Is.Empty);
            Assert.That(
                FindChild(prefab.transform, "GEO_Neck"),
                Is.Not.Null,
                "The ordinary cashier needs one visible human neck.");
            Assert.That(
                FindChild(prefab.transform, "PIVOT_Neck.01"),
                Is.Null,
                "The ordinary cashier must not retain a hidden " +
                "periscope pivot.");
            Assert.That(
                FindChild(prefab.transform, "NECK_Segment.01"),
                Is.Null,
                "The ordinary cashier must not retain a stretch segment.");
            AssertGroundedHeight(registry, 1.75f);

            Assert.That(
                NpcDesignAppearanceCatalog.TryGet(
                    registry.DesignId,
                    out NpcDesignAppearance appearance),
                Is.True);
            Assert.That(appearance, Is.EqualTo(NpcDesignAppearance.Normal));
        }

        [Test]
        public void RetainedWatcher_RemainsBizarreAndExtensibleButInactive()
        {
            SupermarketCashierProvider provider = LoadProvider();
            GameObject prefab = LoadWatcherPrefab();
            SupermarketCashierAssetRegistry registry =
                RequireRegistry(prefab);

            Assert.That(
                AssetDatabase.GetAssetPath(provider.CashierPrefab),
                Is.Not.EqualTo(WatcherPrefabPath));
            Assert.That(registry.DesignId, Is.EqualTo(WatcherDesignId));
            Assert.That(
                registry.NeckMode,
                Is.EqualTo(
                    SupermarketCashierNeckMode.ExtensibleWatcher));
            Assert.That(registry.UsesExtensibleNeck, Is.True);
            AssertGroundedHeight(registry, 2.05f);

            IReadOnlyList<Transform> pivots = registry.NeckPivots;
            Assert.That(
                pivots.Count,
                Is.EqualTo(
                    SupermarketCashierAssetRegistry
                        .WatcherNeckSegmentCount));
            for (int index = 0; index < pivots.Count; index++)
            {
                Assert.That(
                    pivots[index],
                    Is.Not.Null,
                    $"Neck pivot {index + 1} is unbound.");
                Assert.That(
                    pivots[index].name,
                    Is.EqualTo($"PIVOT_Neck.{index + 1:00}"));
                Assert.That(
                    pivots[index].IsChildOf(registry.ModelRoot),
                    Is.True);
                Assert.That(
                    FindChild(
                        prefab.transform,
                        $"NECK_Segment.{index + 1:00}"),
                    Is.Not.Null,
                    "Every Watcher pivot needs its rigid neck segment.");
            }

            Assert.That(
                NpcDesignAppearanceCatalog.TryGet(
                    registry.DesignId,
                    out NpcDesignAppearance appearance),
                Is.True);
            Assert.That(appearance, Is.EqualTo(NpcDesignAppearance.Bizarre));
        }

        [Test]
        public void BothPrefabs_ShareThePassiveRigAndEyeContract()
        {
            SupermarketCashierProvider provider = LoadProvider();
            AssertPassiveRigAndEyes(
                provider.CashierPrefab,
                "ordinary cashier");
            AssertPassiveRigAndEyes(
                LoadWatcherPrefab(),
                "retained Watcher");
        }

        [Test]
        public void ActivePresentation_IgnoresPeriscopeExtensionCommands()
        {
            SupermarketCashierProvider provider = LoadProvider();
            GameObject instance = Object.Instantiate(
                provider.CashierPrefab);
            try
            {
                SupermarketCashierAssetRegistry registry =
                    RequireRegistry(instance);
                var presentation = instance
                    .AddComponent<SupermarketCashierPresentation>();
                presentation.Initialize(registry);

                Assert.That(registry.UsesExtensibleNeck, Is.False);
                Assert.That(presentation.UsesExtensibleNeck, Is.False);

                Vector3 farFocus = registry.Head.position +
                    (instance.transform.forward * 30f) +
                    (instance.transform.up * 4f);
                var baseline = new SupermarketCashierPoseCommand(
                    0f,
                    0f,
                    false,
                    false,
                    farFocus,
                    true);

                // Establish the shared authored hunch first. The regression
                // compares only what changing extension from zero to one can
                // do, rather than mistaking the ordinary checkout pose for
                // neck travel.
                presentation.Apply(0f, baseline);
                Vector3 headPosition = registry.Head.position;
                Vector3 neckScale = registry.Neck.localScale;

                var attemptedExtension =
                    new SupermarketCashierPoseCommand(
                        1f,
                        1f,
                        true,
                        true,
                        farFocus,
                        true);
                presentation.Apply(1f / 60f, attemptedExtension);

                Assert.That(
                    Vector3.Distance(
                        registry.Head.position,
                        headPosition),
                    Is.LessThan(0.00001f),
                    "An extension command moved the ordinary cashier's " +
                    "head instead of only allowing rotation.");
                Assert.That(
                    Vector3.Distance(registry.Neck.localScale, neckScale),
                    Is.LessThan(0.00001f),
                    "An extension command scaled the ordinary human neck.");
                Assert.That(presentation.CurrentExtension, Is.Zero);
                Assert.That(presentation.NeckStretchRatio, Is.EqualTo(1f));
            }
            finally
            {
                Object.DestroyImmediate(instance);
            }
        }

        [Test]
        public void Probe_ReportsBothCashierPrefabs()
        {
            SupermarketCashierProvider provider = LoadProvider();
            ProbePrefab("active ordinary", provider.CashierPrefab);
            ProbePrefab("retained Watcher", LoadWatcherPrefab());
        }

        private static SupermarketCashierAssetRegistry RequireRegistry(
            GameObject prefab)
        {
            Assert.That(prefab, Is.Not.Null);
            var registry = prefab.GetComponent<
                SupermarketCashierAssetRegistry>();
            Assert.That(
                registry,
                Is.Not.Null,
                $"Cashier prefab '{prefab.name}' has no asset registry.");
            return registry;
        }

        private static void AssertGroundedHeight(
            SupermarketCashierAssetRegistry registry,
            float expectedHeight)
        {
            Assert.That(
                registry.LocalBounds.min.y,
                Is.EqualTo(0f).Within(0.025f));
            Assert.That(
                registry.LocalBounds.size.y,
                Is.EqualTo(expectedHeight).Within(0.035f));
        }

        private static void AssertPassiveRigAndEyes(
            GameObject prefab,
            string label)
        {
            SupermarketCashierAssetRegistry registry =
                RequireRegistry(prefab);
            Assert.That(registry.Animator, Is.Not.Null, label);
            Assert.That(
                registry.Animator.applyRootMotion,
                Is.False,
                label);
            Assert.That(
                registry.Animator.runtimeAnimatorController,
                Is.Null,
                label);
            Assert.That(registry.Animator.avatar, Is.Not.Null, label);
            Assert.That(
                AssetDatabase.GetAssetPath(registry.Animator.avatar),
                Is.EqualTo(PlayerModelPath),
                label);

            Assert.That(
                prefab.GetComponentsInChildren<Collider>(true),
                Is.Empty,
                label);
            Assert.That(
                prefab.GetComponentsInChildren<Rigidbody>(true),
                Is.Empty,
                label);
            Assert.That(
                prefab.GetComponentsInChildren<Light>(true),
                Is.Empty,
                label);
            Assert.That(
                prefab.GetComponentsInChildren<AudioSource>(true),
                Is.Empty,
                label);
            Assert.That(
                prefab.GetComponentsInChildren<Camera>(true),
                Is.Empty,
                label);

            Transform boneRoot = FindChild(prefab.transform, "root");
            Assert.That(boneRoot, Is.Not.Null, label);
            Assert.That(
                boneRoot.GetComponentsInChildren<Transform>(true).Length,
                Is.EqualTo(31),
                label);

            int eyeWhites = 0;
            int pupils = 0;
            Renderer leftEye = null;
            Renderer rightEye = null;
            foreach (SupermarketCashierRendererBinding binding in
                registry.RendererBindings)
            {
                if (binding.Role ==
                    SupermarketCashierPresentation.EyeWhiteRole)
                {
                    eyeWhites++;
                    if (binding.RendererName == "FACE_EyeWhite.L")
                    {
                        leftEye = binding.Renderer;
                    }
                    else if (binding.RendererName == "FACE_EyeWhite.R")
                    {
                        rightEye = binding.Renderer;
                    }
                }
                else if (binding.Role ==
                         SupermarketCashierPresentation.PupilRole)
                {
                    pupils++;
                    Assert.That(
                        binding.BoneName,
                        Does.StartWith("face.eye."),
                        label);
                    Assert.That(
                        binding.BaseColor.maxColorComponent,
                        Is.LessThan(0.08f),
                        label);
                }
            }

            Assert.That(eyeWhites, Is.EqualTo(2), label);
            Assert.That(pupils, Is.EqualTo(2), label);
            Assert.That(leftEye, Is.Not.Null, label);
            Assert.That(rightEye, Is.Not.Null, label);

            var leftMesh =
                (leftEye as SkinnedMeshRenderer)?.sharedMesh;
            var rightMesh =
                (rightEye as SkinnedMeshRenderer)?.sharedMesh;
            Assert.That(leftMesh, Is.Not.Null, label);
            Assert.That(rightMesh, Is.Not.Null, label);
            Assert.That(
                rightMesh.bounds.size.x,
                Is.GreaterThan(leftMesh.bounds.size.x * 1.05f),
                label);
        }

        private static void ProbePrefab(string label, GameObject prefab)
        {
            GameObject instance = Object.Instantiate(prefab);
            try
            {
                SupermarketCashierAssetRegistry registry =
                    RequireRegistry(instance);
                Renderer[] renderers = instance
                    .GetComponentsInChildren<Renderer>(true);
                var neckAndFaceParts = new List<string>();
                for (int index = 0; index < renderers.Length; index++)
                {
                    string name = renderers[index].name;
                    if (name.Contains("NECK") ||
                        name.Contains("Neck") ||
                        name.Contains("Eye") ||
                        name.Contains("Pupil") ||
                        name.Contains("Collar") ||
                        name.Contains("NameTag"))
                    {
                        neckAndFaceParts.Add(name);
                    }
                }

                Debug.Log(
                    $"Cashier probe ({label}): " +
                    $"design={registry.DesignId}, " +
                    $"neckMode={registry.NeckMode}, " +
                    $"renderers={renderers.Length}, parts=[" +
                    string.Join(", ", neckAndFaceParts) + "]");
                Assert.That(neckAndFaceParts, Is.Not.Empty, label);
            }
            finally
            {
                Object.DestroyImmediate(instance);
            }
        }

        private static Transform FindChild(
            Transform root,
            string name)
        {
            Transform[] children =
                root.GetComponentsInChildren<Transform>(true);
            for (int index = 0; index < children.Length; index++)
            {
                if (string.Equals(
                        children[index].name,
                        name,
                        System.StringComparison.Ordinal))
                {
                    return children[index];
                }
            }

            return null;
        }
    }
}

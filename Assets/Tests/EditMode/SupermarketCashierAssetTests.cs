using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;

namespace BarPromenade.Tests.EditMode
{
    public sealed class SupermarketCashierAssetTests
    {
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

        [Test]
        public void Provider_BindsThePrefabWithoutPublishingIt()
        {
            SupermarketCashierProvider provider = LoadProvider();
            Assert.That(provider.CashierPrefab, Is.Not.Null);

            // The prefab itself must stay outside every addressable
            // path: not loadable from Resources and unknown to the
            // pedestrian pool.
            Assert.That(
                Resources.Load<GameObject>(
                    "Supermarket/SupermarketCashier"),
                Is.Null);
            Assert.That(
                Resources.Load<GameObject>(
                    "Pedestrians/SupermarketCashier3D"),
                Is.Null);
            Assert.That(
                CityPedestrianResources.TryGetArchetype(
                    SupermarketCashierProvider.DesignId,
                    out _),
                Is.False);
        }

        [Test]
        public void Prefab_ExposesPassiveWatcherContract()
        {
            SupermarketCashierProvider provider = LoadProvider();
            GameObject prefab = provider.CashierPrefab;

            var registry = prefab
                .GetComponent<SupermarketCashierAssetRegistry>();
            Assert.That(registry, Is.Not.Null);
            Assert.That(
                registry.DesignId,
                Is.EqualTo(SupermarketCashierProvider.DesignId));

            Assert.That(registry.Animator, Is.Not.Null);
            Assert.That(registry.Animator.applyRootMotion, Is.False);
            Assert.That(
                registry.Animator.runtimeAnimatorController,
                Is.Null);
            Assert.That(registry.Animator.avatar, Is.Not.Null);

            // Passive on purpose: no physics, light, audio or camera.
            Assert.That(
                prefab.GetComponentsInChildren<Collider>(true),
                Is.Empty);
            Assert.That(
                prefab.GetComponentsInChildren<Rigidbody>(true),
                Is.Empty);
            Assert.That(
                prefab.GetComponentsInChildren<Light>(true),
                Is.Empty);
            Assert.That(
                prefab.GetComponentsInChildren<AudioSource>(true),
                Is.Empty);
            Assert.That(
                prefab.GetComponentsInChildren<Camera>(true),
                Is.Empty);

            // The exact 31-bone shared skeleton under the bone root.
            Transform boneRoot = FindChild(prefab.transform, "root");
            Assert.That(boneRoot, Is.Not.Null);
            Assert.That(
                boneRoot.GetComponentsInChildren<Transform>(true)
                    .Length,
                Is.EqualTo(31));

            // Grounded at zero, resting head top at the authored
            // 2.05 m silhouette.
            Assert.That(
                registry.LocalBounds.min.y,
                Is.EqualTo(0f).Within(0.025f));
            Assert.That(
                registry.LocalBounds.size.y,
                Is.EqualTo(2.05f).Within(0.035f));
        }

        [Test]
        public void Prefab_BindsFiveNeckPivotsWithSegments()
        {
            SupermarketCashierProvider provider = LoadProvider();
            GameObject prefab = provider.CashierPrefab;
            var registry = prefab
                .GetComponent<SupermarketCashierAssetRegistry>();

            IReadOnlyList<Transform> pivots = registry.NeckPivots;
            Assert.That(
                pivots.Count,
                Is.EqualTo(
                    SupermarketCashierAssetRegistry.NeckSegmentCount));
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
                    "Every pivot needs its rigid neck segment mesh.");
            }
        }

        [Test]
        public void Prefab_CarriesWatcherEyesWithAsymmetry()
        {
            SupermarketCashierProvider provider = LoadProvider();
            var registry = provider.CashierPrefab
                .GetComponent<SupermarketCashierAssetRegistry>();

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
                        "Pupils must ride the poseable eye bones.");
                    Assert.That(
                        binding.BaseColor.maxColorComponent,
                        Is.LessThan(0.08f),
                        "Pupils must stay pinprick dark.");
                }
            }

            Assert.That(eyeWhites, Is.EqualTo(2));
            Assert.That(pupils, Is.EqualTo(2));
            Assert.That(leftEye, Is.Not.Null);
            Assert.That(rightEye, Is.Not.Null);

            // The right eye runs visibly larger than the left.
            var leftMesh =
                (leftEye as SkinnedMeshRenderer)?.sharedMesh;
            var rightMesh =
                (rightEye as SkinnedMeshRenderer)?.sharedMesh;
            Assert.That(leftMesh, Is.Not.Null);
            Assert.That(rightMesh, Is.Not.Null);
            Assert.That(
                rightMesh.bounds.size.x,
                Is.GreaterThan(leftMesh.bounds.size.x * 1.05f));
        }

        [Test]
        public void Probe_ReportsWhatTheCashierPrefabContains()
        {
            SupermarketCashierProvider provider = LoadProvider();
            GameObject instance = Object.Instantiate(
                provider.CashierPrefab);
            try
            {
                Renderer[] renderers = instance
                    .GetComponentsInChildren<Renderer>(true);
                var neckParts = new List<string>();
                for (int index = 0; index < renderers.Length; index++)
                {
                    string name = renderers[index].name;
                    if (name.Contains("NECK") ||
                        name.Contains("Eye") ||
                        name.Contains("Pupil") ||
                        name.Contains("Collar") ||
                        name.Contains("NameTag"))
                    {
                        neckParts.Add(name);
                    }
                }

                Debug.Log(
                    "Cashier probe: " +
                    $"renderers={renderers.Length}, " +
                    "watcher parts=[" +
                    string.Join(", ", neckParts) + "]");
                Assert.Pass();
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

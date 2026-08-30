using System;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace BarPromenade.Tests.EditMode
{
    /// <summary>
    /// Contracts of the BUILT raven asset chain: the addressable
    /// provider, the passive prefab it binds, the shared shadowless
    /// material, the detail atlas, and the measured bounds against
    /// the generator's own manifest. Guarded like the Player3D asset
    /// suites: while the prefab has not been built yet the tests
    /// ignore themselves instead of failing, because this code
    /// compiles before the editor setup that builds the asset ever
    /// runs.
    /// </summary>
    public sealed class CemeteryRavenAssetTests
    {
        private const string SharedMaterialPath =
            "Assets/Player3D/Materials/Player3DLit.mat";
        private const string ManifestPath =
            "Assets/Cemetery/Raven/Models/CemeteryRaven3D.json";
        private const string AtlasPath =
            "Assets/Cemetery/Raven/Textures/" +
            "CemeteryRavenDetailAtlas.png";
        private const int MinimumTriangleCount = 350;
        private const int MaximumTriangleCount = 700;

        /// <summary>The asset setup's own measured-bounds gate: the
        /// one number that catches a hundredth-scale import.</summary>
        private const float StandingHeightToleranceMeters = 0.035f;

        private static CemeteryRavenProvider LoadBuiltProvider()
        {
            CemeteryRavenProvider provider =
                CemeteryRavenProvider.Load();
            if (provider == null || provider.RavenPrefab == null)
            {
                Assert.Ignore(
                    "The cemetery raven prefab is not built yet.");
            }

            return provider;
        }

        private static RavenManifestSlice LoadManifest()
        {
            var source =
                AssetDatabase.LoadAssetAtPath<TextAsset>(
                    ManifestPath);
            Assert.That(
                source,
                Is.Not.Null,
                "A built raven prefab without its manifest is a " +
                "broken pipeline.");
            RavenManifestSlice manifest =
                JsonUtility.FromJson<RavenManifestSlice>(source.text);
            Assert.That(manifest, Is.Not.Null);
            return manifest;
        }

        [Test]
        public void Provider_BindsThePrefabWithoutPublishingIt()
        {
            CemeteryRavenProvider provider = LoadBuiltProvider();
            Assert.That(provider.RavenPrefab, Is.Not.Null);

            // The prefab itself stays outside every addressable path:
            // only the provider asset is loadable by name.
            Assert.That(
                Resources.Load<GameObject>("Cemetery/CemeteryRaven"),
                Is.Null);
            Assert.That(
                Resources.Load<GameObject>(
                    "Cemetery/Raven/Prefabs/CemeteryRaven"),
                Is.Null);
        }

        [Test]
        public void Prefab_ExposesPassivePivotArticulation()
        {
            CemeteryRavenProvider provider = LoadBuiltProvider();
            GameObject prefab = provider.RavenPrefab;

            var anchors =
                prefab.GetComponent<CemeteryRavenRigAnchors>();
            Assert.That(anchors, Is.Not.Null);
            Assert.That(anchors.IsBound, Is.True);
            Assert.That(
                anchors.DesignId,
                Is.EqualTo(CemeteryRavenProvider.DesignId));

            // Passive on purpose: the five-component ban, and no
            // Animator either — the raven has no armature.
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
            Assert.That(
                prefab.GetComponentsInChildren<Animator>(true),
                Is.Empty);

            Assert.That(
                anchors.BodyRootPivot.name,
                Is.EqualTo(
                    CemeteryRavenRigAnchors.BodyRootPivotName));
            Assert.That(
                anchors.HeadPivot.name,
                Is.EqualTo(CemeteryRavenRigAnchors.HeadPivotName));
            Assert.That(
                anchors.WingLeftPivot.name,
                Is.EqualTo(
                    CemeteryRavenRigAnchors.WingLeftPivotName));
            Assert.That(
                anchors.WingRightPivot.name,
                Is.EqualTo(
                    CemeteryRavenRigAnchors.WingRightPivotName));
            Assert.That(
                anchors.TailPivot.name,
                Is.EqualTo(CemeteryRavenRigAnchors.TailPivotName));
            Assert.That(
                anchors.FeetContactAnchor.name,
                Is.EqualTo(
                    CemeteryRavenRigAnchors.FeetContactAnchorName));

            // Every part articulates: each binding names one of the
            // five pivots, and each names a live renderer.
            Assert.That(anchors.RendererBindings, Is.Not.Empty);
            Assert.That(
                anchors.RendererBindings.Count,
                Is.EqualTo(anchors.Renderers.Count));
            foreach (CemeteryRavenRendererBinding binding in
                     anchors.RendererBindings)
            {
                Assert.That(binding.Renderer, Is.Not.Null);
                bool known =
                    binding.PivotName ==
                    CemeteryRavenRigAnchors.BodyRootPivotName ||
                    binding.PivotName ==
                    CemeteryRavenRigAnchors.HeadPivotName ||
                    binding.PivotName ==
                    CemeteryRavenRigAnchors.WingLeftPivotName ||
                    binding.PivotName ==
                    CemeteryRavenRigAnchors.WingRightPivotName ||
                    binding.PivotName ==
                    CemeteryRavenRigAnchors.TailPivotName;
                Assert.That(
                    known,
                    Is.True,
                    $"{binding.RendererName} names unknown pivot " +
                    $"'{binding.PivotName}'.");
            }
        }

        [Test]
        public void Prefab_MatchesTheManifestBoundsBudgetAndSignature()
        {
            CemeteryRavenProvider provider = LoadBuiltProvider();
            var anchors = provider.RavenPrefab
                .GetComponent<CemeteryRavenRigAnchors>();
            RavenManifestSlice manifest = LoadManifest();

            Assert.That(
                manifest.design_id,
                Is.EqualTo(CemeteryRavenProvider.DesignId));

            // MEASURED bounds against the authored standing height:
            // the check that catches a hundredth-scale bird, an axis
            // mishap, or a re-authored model whose prefab was never
            // rebuilt. The sole plane must hold the origin too, or
            // the feet-contact rule is a lie.
            Assert.That(
                anchors.LocalBounds.max.y,
                Is.EqualTo(manifest.standing_height_m)
                    .Within(StandingHeightToleranceMeters));
            Assert.That(
                anchors.LocalBounds.min.y,
                Is.GreaterThanOrEqualTo(-0.05f));

            Assert.That(
                anchors.SourceTriangleCount,
                Is.InRange(
                    MinimumTriangleCount,
                    MaximumTriangleCount));
            Assert.That(
                anchors.SourceTriangleCount,
                Is.EqualTo(manifest.triangle_count));

            Assert.That(
                anchors.BuildSignature,
                Has.Length.EqualTo(64));
            Assert.That(
                anchors.BuildSignature,
                Is.EqualTo(manifest.build_signature));
        }

        [Test]
        public void Prefab_SharesTheShadowlessPlayerMaterialAndBindsTheAtlas()
        {
            CemeteryRavenProvider provider = LoadBuiltProvider();
            var anchors = provider.RavenPrefab
                .GetComponent<CemeteryRavenRigAnchors>();

            Material sharedMaterial =
                AssetDatabase.LoadAssetAtPath<Material>(
                    SharedMaterialPath);
            Assert.That(sharedMaterial, Is.Not.Null);
            Assert.That(anchors.Renderers, Is.Not.Empty);
            foreach (Renderer renderer in anchors.Renderers)
            {
                Assert.That(
                    renderer.sharedMaterials.Length,
                    Is.EqualTo(1));
                Assert.That(
                    renderer.sharedMaterial,
                    Is.SameAs(sharedMaterial),
                    $"{renderer.name} must share Player3DLit.");
                Assert.That(
                    renderer.shadowCastingMode,
                    Is.EqualTo(ShadowCastingMode.Off),
                    $"{renderer.name} must not cast shadows.");
            }

            // The atlas rides the rig anchors and reaches the shared
            // material through a property block, never through a
            // material of the bird's own.
            Texture2D atlas =
                AssetDatabase.LoadAssetAtPath<Texture2D>(AtlasPath);
            Assert.That(atlas, Is.Not.Null);
            Assert.That(anchors.DetailAtlas, Is.SameAs(atlas));

            bool anyAtlasBinding = false;
            foreach (CemeteryRavenRendererBinding binding in
                     anchors.RendererBindings)
            {
                if (binding.UsesDetailAtlas)
                {
                    anyAtlasBinding = true;
                    break;
                }
            }

            Assert.That(
                anyAtlasBinding,
                Is.True,
                "No renderer samples the detail atlas; the texture " +
                "would be dead weight.");
        }

        /// <summary>The few manifest fields these tests measure the
        /// prefab against; the editor setup validates the rest.</summary>
        [Serializable]
        private sealed class RavenManifestSlice
        {
            public string design_id;
            public float standing_height_m;
            public int triangle_count;
            public string build_signature;
        }
    }
}

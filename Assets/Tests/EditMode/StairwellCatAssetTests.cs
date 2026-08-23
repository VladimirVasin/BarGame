using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace BarPromenade.Tests.EditMode
{
    public sealed class StairwellCatAssetTests
    {
        private const string GrinMaterialPath =
            "Assets/Resources/Materials/StairwellCatGrin.mat";
        private const string SharedMaterialPath =
            "Assets/Player3D/Materials/Player3DLit.mat";

        private static StairwellCatProvider LoadProvider()
        {
            var provider = StairwellCatProvider.Load();
            Assert.That(
                provider,
                Is.Not.Null,
                "Expected the cat provider asset at " +
                $"Resources/{StairwellCatProvider.ResourcePath}.");
            return provider;
        }

        [Test]
        public void Provider_BindsThePrefabWithoutPublishingIt()
        {
            StairwellCatProvider provider = LoadProvider();
            Assert.That(provider.CatPrefab, Is.Not.Null);

            // The prefab itself stays outside every addressable path.
            Assert.That(
                Resources.Load<GameObject>("Stairwell/StairwellCat"),
                Is.Null);
            Assert.That(
                Resources.Load<GameObject>(
                    "Pedestrians/StairwellCat3D"),
                Is.Null);
        }

        [Test]
        public void Prefab_ExposesPassivePivotArticulation()
        {
            StairwellCatProvider provider = LoadProvider();
            GameObject prefab = provider.CatPrefab;

            var anchors =
                prefab.GetComponent<StairwellCatRigAnchors>();
            Assert.That(anchors, Is.Not.Null);
            Assert.That(anchors.IsBound, Is.True);
            Assert.That(
                anchors.DesignId,
                Is.EqualTo(StairwellCatProvider.DesignId));

            // Passive on purpose: no physics, light, audio, camera
            // and no Animator either - the cat has no armature.
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
                anchors.ChestPivot.name,
                Is.EqualTo(StairwellCatRigAnchors.ChestPivotName));
            Assert.That(
                anchors.HeadPivot.name,
                Is.EqualTo(StairwellCatRigAnchors.HeadPivotName));
            Assert.That(
                anchors.EarLeftPivot.name,
                Is.EqualTo(StairwellCatRigAnchors.EarLeftPivotName));
            Assert.That(
                anchors.EarRightPivot.name,
                Is.EqualTo(
                    StairwellCatRigAnchors.EarRightPivotName));
            for (int index = 0;
                 index < StairwellCatRigAnchors.TailPivotCount;
                 index++)
            {
                Assert.That(
                    anchors.TailPivots[index].name,
                    Is.EqualTo(
                        StairwellCatRigAnchors
                            .TailPivotNames[index]));
            }

            Assert.That(
                anchors.MuzzleAnchor.name,
                Is.EqualTo(StairwellCatRigAnchors.MuzzleAnchorName));

            // Sitting-cat proportions: ear tips around 0.56 m over
            // the rail contact, the tail allowed to hang below it.
            Assert.That(
                anchors.LocalBounds.max.y,
                Is.InRange(0.50f, 0.62f));
            Assert.That(
                anchors.LocalBounds.min.y,
                Is.GreaterThanOrEqualTo(-0.35f));
        }

        [Test]
        public void Prefab_ShipsTheGrinDisabledOnItsOwnMaterial()
        {
            StairwellCatProvider provider = LoadProvider();
            var anchors = provider.CatPrefab
                .GetComponent<StairwellCatRigAnchors>();

            Renderer grin = anchors.GrinRenderer;
            Assert.That(
                grin.name,
                Is.EqualTo(StairwellCatRigAnchors.GrinRendererName));
            // Default: the grin does not exist.
            Assert.That(grin.enabled, Is.False);

            Material grinMaterial =
                AssetDatabase.LoadAssetAtPath<Material>(
                    GrinMaterialPath);
            Assert.That(grinMaterial, Is.Not.Null);
            Assert.That(grin.sharedMaterial, Is.SameAs(grinMaterial));
            Assert.That(
                grinMaterial.shader.name,
                Is.EqualTo("Bar Promenade/Stairwell Cat Grin"));
            Assert.That(
                grinMaterial.GetFloat("_GrinProgress"),
                Is.Zero);

            // The comically-wide contract, measured off the meshes:
            // the grin band is wider than the head.
            MeshFilter grinFilter =
                grin.GetComponent<MeshFilter>();
            MeshFilter headFilter = anchors.Renderers
                .First(renderer => renderer.name == "GEO_Head")
                .GetComponent<MeshFilter>();
            Assert.That(
                grinFilter.sharedMesh.bounds.size.x,
                Is.GreaterThan(
                    headFilter.sharedMesh.bounds.size.x));

            // The arc-length UV contract the reveal shader walks.
            Mesh grinMesh = grinFilter.sharedMesh;
            Assert.That(grinMesh.uv, Is.Not.Empty);
            Assert.That(
                grinMesh.uv.Min(uv => uv.x),
                Is.EqualTo(0f).Within(0.001f));
            Assert.That(
                grinMesh.uv.Max(uv => uv.x),
                Is.EqualTo(1f).Within(0.001f));
        }

        [Test]
        public void Prefab_BodyRenderersShareThePlayerMaterialShadowless()
        {
            StairwellCatProvider provider = LoadProvider();
            var anchors = provider.CatPrefab
                .GetComponent<StairwellCatRigAnchors>();

            Material sharedMaterial =
                AssetDatabase.LoadAssetAtPath<Material>(
                    SharedMaterialPath);
            Assert.That(sharedMaterial, Is.Not.Null);
            Assert.That(anchors.Renderers, Is.Not.Empty);
            Assert.That(
                anchors.RendererBindings.Count,
                Is.EqualTo(anchors.Renderers.Count));

            foreach (Renderer renderer in anchors.Renderers)
            {
                Assert.That(
                    renderer.sharedMaterials.Length,
                    Is.EqualTo(1));
                if (renderer != anchors.GrinRenderer)
                {
                    Assert.That(
                        renderer.sharedMaterial,
                        Is.SameAs(sharedMaterial),
                        $"{renderer.name} must share Player3DLit.");
                }

                Assert.That(
                    renderer.shadowCastingMode,
                    Is.EqualTo(ShadowCastingMode.Off),
                    $"{renderer.name} must not cast shadows.");
            }

            Assert.That(
                anchors.BodyRenderer.name,
                Is.EqualTo("GEO_Haunches"));

            // Every articulated binding names a real pivot.
            foreach (StairwellCatRendererBinding binding in
                anchors.RendererBindings)
            {
                Assert.That(binding.Renderer, Is.Not.Null);
                if (string.IsNullOrEmpty(binding.PivotName))
                {
                    continue;
                }

                bool known =
                    binding.PivotName ==
                    StairwellCatRigAnchors.ChestPivotName ||
                    binding.PivotName ==
                    StairwellCatRigAnchors.HeadPivotName ||
                    binding.PivotName ==
                    StairwellCatRigAnchors.EarLeftPivotName ||
                    binding.PivotName ==
                    StairwellCatRigAnchors.EarRightPivotName ||
                    StairwellCatRigAnchors.TailPivotNames
                        .Contains(binding.PivotName);
                Assert.That(
                    known,
                    Is.True,
                    $"{binding.RendererName} names unknown pivot " +
                    $"'{binding.PivotName}'.");
            }
        }

        [Test]
        public void GrinShader_CompilesClean()
        {
            Material grinMaterial =
                AssetDatabase.LoadAssetAtPath<Material>(
                    GrinMaterialPath);
            Assert.That(grinMaterial, Is.Not.Null);
            Shader shader = grinMaterial.shader;
            Assert.That(shader, Is.Not.Null);
            Assert.That(
                ShaderUtil.ShaderHasError(shader),
                Is.False,
                "The grin shader has compile errors.");
        }
    }
}

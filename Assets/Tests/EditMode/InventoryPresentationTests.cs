using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace BarPromenade.Tests.EditMode
{
    public sealed class InventoryPresentationTests
    {
        [TestCase(InventoryItemId.ApartmentKeys)]
        [TestCase(InventoryItemId.Lighter)]
        [TestCase(InventoryItemId.VodkaBottle)]
        [TestCase(InventoryItemId.ChickenEgg)]
        [TestCase(InventoryItemId.OpenStewCan)]
        [TestCase(InventoryItemId.ClosedStewCan)]
        [TestCase(InventoryItemId.InstantNoodles)]
        [TestCase(InventoryItemId.DayOldLoaf)]
        public void ItemIcon_HasVisiblePointFilteredPixels(
            InventoryItemId itemId)
        {
            Texture2D icon = InventoryIconLibrary.GetIcon(itemId);

            Assert.That(icon, Is.Not.Null);
            Assert.That(icon.width, Is.EqualTo(32));
            Assert.That(icon.height, Is.EqualTo(32));
            Assert.That(icon.filterMode, Is.EqualTo(FilterMode.Point));
            Color32[] pixels = icon.GetPixels32();
            bool hasVisiblePixel = false;
            for (int index = 0; index < pixels.Length; index++)
            {
                if (pixels[index].a > 0)
                {
                    hasVisiblePixel = true;
                    break;
                }
            }

            Assert.That(
                hasVisiblePixel,
                Is.True,
                $"{itemId} must have a visible icon silhouette.");
        }

        [Test]
        public void HeroPortrait_UsesDedicatedFull3DTexture()
        {
            Texture2D portrait = Resources.Load<Texture2D>(
                InventoryIconLibrary.HeroPortraitResourcePath);

            Assert.That(portrait, Is.Not.Null);
            Assert.That(
                InventoryIconLibrary.GetHeroPortrait(),
                Is.SameAs(portrait));
            Assert.That(portrait.width, Is.EqualTo(192));
            Assert.That(portrait.height, Is.EqualTo(256));
            Assert.That(
                InventoryIconLibrary.HeroPortraitUv,
                Is.EqualTo(new Rect(0f, 0f, 1f, 1f)));
            Assert.That(portrait.filterMode, Is.EqualTo(FilterMode.Point));
            Assert.That(portrait.wrapMode, Is.EqualTo(TextureWrapMode.Clamp));

            string assetPath = AssetDatabase.GetAssetPath(portrait);
            Assert.That(
                assetPath,
                Is.EqualTo(
                    "Assets/Resources/Player/Player3DPortrait.png"));
            TextureImporter importer =
                AssetImporter.GetAtPath(assetPath) as TextureImporter;
            Assert.That(importer, Is.Not.Null);
            Assert.That(
                importer.textureType,
                Is.EqualTo(TextureImporterType.Default));
            Assert.That(importer.sRGBTexture, Is.True);
            Assert.That(
                importer.alphaSource,
                Is.EqualTo(TextureImporterAlphaSource.FromInput));
            Assert.That(importer.alphaIsTransparency, Is.True);
            Assert.That(importer.mipmapEnabled, Is.False);
            Assert.That(importer.npotScale, Is.EqualTo(TextureImporterNPOTScale.None));
            Assert.That(
                importer.textureCompression,
                Is.EqualTo(TextureImporterCompression.Uncompressed));
        }

        [TestCase(InventoryItemId.ApartmentKeys)]
        [TestCase(InventoryItemId.Lighter)]
        [TestCase(InventoryItemId.VodkaBottle)]
        [TestCase(InventoryItemId.ChickenEgg)]
        [TestCase(InventoryItemId.OpenStewCan)]
        [TestCase(InventoryItemId.ClosedStewCan)]
        [TestCase(InventoryItemId.InstantNoodles)]
        [TestCase(InventoryItemId.DayOldLoaf)]
        public void PreviewModel_HasFiniteGeometryAndNoColliders(
            InventoryItemId itemId)
        {
            GameObject owner = new GameObject(
                "Inventory Presentation Test Owner");
            try
            {
                Transform model =
                    InventoryItemModelFactory.BuildPreviewModel(
                        itemId,
                        owner.transform);
                Renderer[] renderers =
                    model.GetComponentsInChildren<Renderer>(true);

                Assert.That(model.parent, Is.EqualTo(owner.transform));
                Assert.That(renderers, Is.Not.Empty);
                Assert.That(
                    model.GetComponentsInChildren<Collider>(true),
                    Is.Empty);
                Bounds bounds = renderers[0].bounds;
                for (int index = 1; index < renderers.Length; index++)
                {
                    bounds.Encapsulate(renderers[index].bounds);
                }

                AssertPositiveFinite(bounds.size.x, "width");
                AssertPositiveFinite(bounds.size.y, "height");
                AssertPositiveFinite(bounds.size.z, "depth");
            }
            finally
            {
                Object.DestroyImmediate(owner);
            }
        }

        [Test]
        public void PreviewModel_RejectsMissingItem()
        {
            GameObject owner = new GameObject(
                "Invalid Inventory Presentation Test Owner");
            try
            {
                Assert.That(
                    () => InventoryItemModelFactory.BuildPreviewModel(
                        InventoryItemId.None,
                        owner.transform),
                    Throws.TypeOf<System.ArgumentOutOfRangeException>());
            }
            finally
            {
                Object.DestroyImmediate(owner);
            }
        }

        private static void AssertPositiveFinite(
            float value,
            string dimension)
        {
            Assert.That(value, Is.GreaterThan(0f), dimension);
            Assert.That(float.IsNaN(value), Is.False, dimension);
            Assert.That(float.IsInfinity(value), Is.False, dimension);
        }
    }
}

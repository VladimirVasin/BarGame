using NUnit.Framework;
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
        public void HeroPortrait_UsesFrontCropOfPlayerReferenceAtlas()
        {
            Texture2D atlas = Resources.Load<Texture2D>(
                PlayerSpriteRig.ReferenceAtlasResourcePath);

            Assert.That(atlas, Is.Not.Null);
            Assert.That(
                InventoryIconLibrary.GetHeroPortrait(),
                Is.SameAs(atlas));
            Assert.That(atlas.filterMode, Is.EqualTo(FilterMode.Point));
            Rect crop = InventoryIconLibrary.HeroPortraitUv;
            Assert.That(crop.xMin, Is.GreaterThanOrEqualTo(0f));
            Assert.That(crop.yMin, Is.GreaterThanOrEqualTo(0f));
            Assert.That(
                crop.xMax,
                Is.LessThanOrEqualTo(
                    PlayerSpriteRig.FrameWidth / (float)atlas.width));
            Assert.That(crop.yMax, Is.LessThanOrEqualTo(1f));
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

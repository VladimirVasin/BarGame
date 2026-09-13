using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEngine;

namespace BarPromenade.Tests.EditMode
{
    /// <summary>
    /// The production wardrobe reveals independent bare anatomy without
    /// repainting clothing, then restores the exact prior outfit and flags.
    /// Pure palette cases retain the compatibility contract for older rigs.
    /// </summary>
    public sealed class Player3DBathingAppearanceTests
    {
        private static readonly string[] MustBeHidden =
        {
            "CLO_JacketBody",
            "CLO_JacketSleeve.L",
            "CLO_JacketSleeve.R",
            "CLO_JacketForearm.L",
            "CLO_JacketForearm.R"
        };

        private static readonly string[] MustStayVisible =
        {
            // Both forearms are jacket now, so both bare arms show.
            "GEO_Forearm.L",
            "GEO_Forearm.R",
            "GEO_Torso",
            "GEO_Pelvis",
            "GEO_Head",
            "GEO_Hand.L",
            "GEO_Hand.R",
            "GEO_Foot.L"
        };

        private static readonly string[] AtlasBodyParts =
        {
            "GEO_Torso",
            "GEO_Pelvis",
            "GEO_Thigh.L",
            "GEO_Thigh.R",
            "GEO_Shin.L",
            "GEO_Shin.R",
            "GEO_Foot.L",
            "GEO_Foot.R"
        };

        [TestCase("clothing", true)]
        [TestCase("body_part", false)]
        [TestCase("hair", false)]
        [TestCase("signature_detail", false)]
        [TestCase("", false)]
        [TestCase(null, false)]
        public void TheHidingRule_IsStatedAgainstTheRole(string role, bool expected)
        {
            Assert.That(Player3DBathingAppearance.IsHidden(role), Is.EqualTo(expected));
        }

        [TestCase("MAT_Shirt", "chest", Player3DBathingAppearance.BareTone.Skin)]
        [TestCase("MAT_JeansAtlas", "pelvis", Player3DBathingAppearance.BareTone.SkinShadow)]
        [TestCase("MAT_JeansAtlas", "thigh.L", Player3DBathingAppearance.BareTone.Skin)]
        [TestCase("MAT_JeansAtlas", "thigh.R", Player3DBathingAppearance.BareTone.Skin)]
        [TestCase("MAT_JeansAtlas", "shin.R", Player3DBathingAppearance.BareTone.SkinShadow)]
        [TestCase("MAT_JeansAtlas", "foot.L", Player3DBathingAppearance.BareTone.SkinDark)]
        [TestCase("MAT_Skin", "hand.R", Player3DBathingAppearance.BareTone.None)]
        [TestCase("MAT_JacketAtlas", "chest", Player3DBathingAppearance.BareTone.None)]
        [TestCase("MAT_FaceAtlas", "head", Player3DBathingAppearance.BareTone.None)]
        [TestCase(null, "pelvis", Player3DBathingAppearance.BareTone.None)]
        public void TheTone_IsStatedAgainstTheMaterialAndTheBone(
            string material, string bone, Player3DBathingAppearance.BareTone expected)
        {
            Assert.That(Player3DBathingAppearance.ResolveBareTone(material, bone), Is.EqualTo(expected));
        }

        [Test]
        public void OnTheProductionRig_TheClothesComeOffTheBodyIsSkinAndEverythingComesBack()
        {
            GameObject prefab = Player3DResources.LoadPrefab();
            if (prefab == null)
            {
                Assert.Ignore("The Player 3D prefab is not built yet.");
            }

            GameObject instance = UnityEngine.Object.Instantiate(prefab);
            Player3DBathingAppearance lease = null;
            try
            {
                var registry = instance.GetComponentInChildren<Player3DAssetRegistry>(true);
                Assert.That(registry, Is.Not.Null);
                IReadOnlyList<Player3DMeshBinding> bindings = registry.MeshBindings;
                Assert.That(bindings.Count, Is.GreaterThanOrEqualTo(30));
                var wardrobe = registry.GetComponent<PlayerWardrobe>();
                Assert.That(wardrobe, Is.Not.Null);
                wardrobe.ValidateBindings();
                Renderer[] clothing = wardrobe.Garments.SelectMany(item => item.Renderers).ToArray();
                Assert.That(Find(bindings, "GEO_Torso").Renderer.enabled, Is.False, "The worn shirt covers bare torso.");

                Dictionary<string, Snapshot> before = Capture(bindings);
                Player3DMeshBinding skin = Find(bindings, "GEO_Hand.R");
                Assert.That(skin, Is.Not.Null);
                Assert.That(skin.PaletteMaterialName, Is.EqualTo(Player3DBathingAppearance.SkinMaterialName));

                Assert.That(Player3DBathingAppearance.IsActive, Is.False);
                lease = Player3DBathingAppearance.Apply(registry);
                Assert.That(Player3DBathingAppearance.IsActive, Is.True);
                Assert.That(lease.HiddenRendererCount, Is.EqualTo(clothing.Length));
                Assert.That(lease.RepaintedRendererCount, Is.Zero, "Bare geometry already has its skin appearance.");
                Assert.Throws<InvalidOperationException>(
                    () => Player3DBathingAppearance.Apply(registry),
                    "Only one owner may undress him at a time.");

                foreach (string name in MustBeHidden)
                {
                    Assert.That(Find(bindings, name).Renderer.enabled, Is.False, name + " must be off.");
                }
                Assert.That(clothing.All(renderer => !renderer.enabled), Is.True, "Every outfit piece comes off.");

                foreach (string name in MustStayVisible)
                {
                    Assert.That(Find(bindings, name).Renderer.enabled, Is.True, name + " must stay on.");
                }

                var block = new MaterialPropertyBlock();
                Texture2D atlas = Player3DBathingAppearance.BareSkinAtlas;
                Assert.That(lease.UsesBareSkinAtlas, Is.EqualTo(atlas != null));
                foreach (string name in AtlasBodyParts)
                {
                    Player3DMeshBinding binding = Find(bindings, name);
                    Assert.That(
                        ReferenceEquals(binding.Renderer.sharedMaterial, before[name].Material),
                        Is.True,
                        name + " retains its authored shared skin material.");
                    binding.Renderer.GetPropertyBlock(block);
                    Color tint = block.GetColor("_BaseColor");
                    if (atlas != null)
                    {
                        Assert.That(block.GetTexture("_BaseMap"), Is.EqualTo(atlas), name + " must wear the bare-skin atlas.");
                        AssertColor(tint, Color.white, name + " must not tint the atlas a second time.");
                    }
                    AssertColor(tint, before[name].Color, name + " skin was already configured before undressing.");
                }

                // Untouched parts are exactly as they were.
                foreach (string name in new[] { "GEO_Head", "GEO_Hand.L", "GEO_Hand.R" })
                {
                    AssertSame(before[name], Find(bindings, name), name);
                }

                lease.Restore();
                lease = null;
                Assert.That(Player3DBathingAppearance.IsActive, Is.False);
                foreach (Player3DMeshBinding binding in bindings)
                {
                    if (binding?.Renderer == null)
                    {
                        continue;
                    }

                    AssertSame(before[binding.MeshName], binding, binding.MeshName);
                }
            }
            finally
            {
                lease?.Restore();
                UnityEngine.Object.DestroyImmediate(instance);
            }
        }

        /// <summary>
        /// The generator packages the bare-skin atlas under Resources and
        /// bakes a torso strip into its own cell; every other body part
        /// keeps the jeans UV0, whose rects the bare atlas repaints as skin.
        /// </summary>
        [Test]
        public void TheBareSkinAtlasIsPackagedAndTheBodyPointsIntoIt()
        {
            GameObject prefab = Player3DResources.LoadPrefab();
            if (prefab == null)
            {
                Assert.Ignore("The Player 3D prefab is not built yet.");
            }

            Texture2D atlas = Player3DBathingAppearance.BareSkinAtlas;
            Assert.That(atlas, Is.Not.Null, "Resources/" + Player3DBathingAppearance.BareSkinAtlasResourcePath + ".png is generated with the hero.");
            Assert.That(atlas.width, Is.EqualTo(256));
            Assert.That(atlas.height, Is.EqualTo(256));
            Assert.That(atlas.filterMode, Is.EqualTo(FilterMode.Point), "Pixel art, like every atlas in the game.");
            Assert.That(atlas.mipmapCount, Is.EqualTo(1));

            GameObject instance = UnityEngine.Object.Instantiate(prefab);
            try
            {
                var registry = instance.GetComponentInChildren<Player3DAssetRegistry>(true);
                AssertUvInside(registry, "GEO_Torso", 0, 128, 128, 128);
                AssertUvInside(registry, "GEO_Pelvis", 192, 128, 64, 64);
                AssertUvInside(registry, "GEO_Thigh.L", 0, 64, 64, 64);
                AssertUvInside(registry, "GEO_Shin.R", 192, 64, 64, 64);
                AssertUvInside(registry, "GEO_Foot.L", 0, 0, 64, 64);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(instance);
            }
        }

        private static void AssertUvInside(
            Player3DAssetRegistry registry, string meshName, int x, int y, int width, int height)
        {
            var renderer = Find(registry.MeshBindings, meshName).Renderer as SkinnedMeshRenderer;
            Assert.That(renderer, Is.Not.Null, meshName);
            Vector2[] uv = renderer.sharedMesh.uv;
            Assert.That(uv, Is.Not.Null.And.Not.Empty, meshName + " must carry UV0 for the bare-skin atlas.");
            Assert.That(uv.Length, Is.EqualTo(renderer.sharedMesh.vertexCount));
            float minU = (x + 1) / 256f, maxU = (x + width - 1) / 256f;
            float minV = (y + 1) / 256f, maxV = (y + height - 1) / 256f;
            Vector2 low = uv[0], high = uv[0];
            foreach (Vector2 point in uv)
            {
                Assert.That(point.x, Is.InRange(minU - 0.0001f, maxU + 0.0001f), meshName + " u");
                Assert.That(point.y, Is.InRange(minV - 0.0001f, maxV + 0.0001f), meshName + " v");
                low = Vector2.Min(low, point);
                high = Vector2.Max(high, point);
            }

            Assert.That(high.x - low.x, Is.GreaterThan(0.05f), meshName + " spans its region");
            Assert.That(high.y - low.y, Is.GreaterThan(0.05f), meshName + " spans its region");
        }

        [Test]
        public void ARendererAlreadyOff_IsLeftAlone()
        {
            GameObject prefab = Player3DResources.LoadPrefab();
            if (prefab == null)
            {
                Assert.Ignore("The Player 3D prefab is not built yet.");
            }

            GameObject instance = UnityEngine.Object.Instantiate(prefab);
            Player3DBathingAppearance lease = null;
            try
            {
                var registry = instance.GetComponentInChildren<Player3DAssetRegistry>(true);
                Player3DMeshBinding jacket = Find(registry.MeshBindings, "CLO_JacketBody");
                jacket.Renderer.enabled = false;
                Renderer hand = Find(registry.MeshBindings, "GEO_Hand.R").Renderer;
                hand.enabled = false;
                int visibleClothes = registry.GetComponent<PlayerWardrobe>().Garments
                    .SelectMany(item => item.Renderers).Count(renderer => renderer.enabled);
                lease = Player3DBathingAppearance.Apply(registry);
                Assert.That(lease.HiddenRendererCount, Is.EqualTo(visibleClothes));
                Assert.That(hand.enabled, Is.False, "A skin renderer hidden by another owner stays hidden during washing.");
                Player3DHeadVisibility hiddenHead = Player3DHeadVisibility.Hide(registry);
                lease.Restore();
                lease = null;
                Assert.That(jacket.Renderer.enabled, Is.False, "It was off before and it stays off.");
                Assert.That(hand.enabled, Is.False);
                Assert.That(Find(registry.MeshBindings, "GEO_Head").Renderer.enabled, Is.False,
                    "Dressing must not release the independent first-person head lease.");
                hiddenHead.Restore();
            }
            finally
            {
                lease?.Restore();
                UnityEngine.Object.DestroyImmediate(instance);
            }
        }

        [Test]
        public void BothForearmsComeOffTogether()
        {
            GameObject prefab = Player3DResources.LoadPrefab();
            if (prefab == null)
            {
                Assert.Ignore("The Player 3D prefab is not built yet.");
            }

            GameObject instance = UnityEngine.Object.Instantiate(prefab);
            Player3DBathingAppearance lease = null;
            try
            {
                var registry = instance.GetComponentInChildren<Player3DAssetRegistry>(true);
                Assert.That(
                    registry.MeshBindings.Any(
                        binding => binding != null &&
                                   binding.MeshName.IndexOf(
                                       "Bandage",
                                       StringComparison.Ordinal) >= 0),
                    Is.False,
                    "The bandage was removed from the hero, mesh and all.");

                Player3DMeshBinding left =
                    Find(registry.MeshBindings, "CLO_JacketForearm.L");
                Player3DMeshBinding right =
                    Find(registry.MeshBindings, "CLO_JacketForearm.R");
                Assert.That(
                    left.Role,
                    Is.EqualTo(Player3DBathingAppearance.ClothingRole));
                Assert.That(left.Role, Is.EqualTo(right.Role));
                Assert.That(
                    left.PaletteMaterialName,
                    Is.EqualTo(right.PaletteMaterialName),
                    "One sleeve material dresses both forearms.");

                lease = Player3DBathingAppearance.Apply(registry);
                Assert.That(left.Renderer.enabled, Is.False);
                Assert.That(right.Renderer.enabled, Is.False);
                lease.Restore();
                lease = null;
                Assert.That(left.Renderer.enabled, Is.True);
                Assert.That(right.Renderer.enabled, Is.True);
            }
            finally
            {
                lease?.Restore();
                UnityEngine.Object.DestroyImmediate(instance);
            }
        }

        [Test]
        public void WardrobeReplacementIsAtomicAndTemporaryUndressRestoresTheSelectedOutfit()
        {
            GameObject prefab = Player3DResources.LoadPrefab();
            Assert.That(prefab, Is.Not.Null);
            GameObject instance = UnityEngine.Object.Instantiate(prefab);
            Player3DBathingAppearance lease = null;
            try
            {
                var registry = instance.GetComponentInChildren<Player3DAssetRegistry>(true);
                var wardrobe = registry.GetComponent<PlayerWardrobe>();
                Assert.That(wardrobe, Is.Not.Null);
                string originalJacket = wardrobe.GetEquippedItem("jacket");
                PlayerWardrobe.GarmentBinding jacket = wardrobe.Garments.First(item => item.Id == originalJacket);
                var alternative = new GameObject("Synthetic alternative jacket");
                alternative.transform.SetParent(instance.transform, false);
                Renderer replacement = alternative.AddComponent<MeshRenderer>();
                replacement.sharedMaterial = jacket.Renderers[0].sharedMaterial;
                PlayerWardrobe.GarmentBinding[] catalog = wardrobe.Garments.Concat(new[]
                {
                    new PlayerWardrobe.GarmentBinding("test_jacket", "jacket", new[] { replacement },
                        jacket.CoveredBodyRenderers.ToArray())
                }).ToArray();
                wardrobe.Configure(wardrobe.CurrentOutfitId, wardrobe.BodyRenderers.ToArray(), catalog);
                var original = wardrobe.CaptureOutfit();
                Assert.That(replacement.enabled, Is.False, "Only the first item in each slot starts equipped.");
                Dictionary<string, Snapshot> before = Capture(registry.MeshBindings);
                Assert.Throws<ArgumentException>(() => wardrobe.SetSlot("boots", "test_jacket"));
                Assert.Throws<InvalidOperationException>(() => wardrobe.Configure("invalid", wardrobe.BodyRenderers.ToArray(),
                    new[] { new PlayerWardrobe.GarmentBinding("bad", "jacket", new[] { replacement }, new[] { replacement }) }));
                Assert.That(wardrobe.GetEquippedItem("jacket"), Is.EqualTo(originalJacket));
                foreach (Player3DMeshBinding binding in registry.MeshBindings)
                    AssertSame(before[binding.MeshName], binding, binding.MeshName);

                wardrobe.SetSlot("jacket", "test_jacket");
                Assert.That(replacement.enabled, Is.True);
                Assert.That(jacket.Renderers.All(renderer => !renderer.enabled), Is.True);
                foreach (Renderer renderer in jacket.CoveredBodyRenderers) Assert.That(renderer.enabled, Is.False);
                lease = Player3DBathingAppearance.Apply(registry);
                Assert.That(replacement.enabled, Is.False);
                foreach (Renderer renderer in jacket.CoveredBodyRenderers) Assert.That(renderer.enabled, Is.True);
                Assert.Throws<InvalidOperationException>(() => wardrobe.SetSlot("jacket", originalJacket));
                lease.Restore();
                lease = null;
                Assert.That(wardrobe.GetEquippedItem("jacket"), Is.EqualTo("test_jacket"));
                Assert.That(replacement.enabled, Is.True);
                wardrobe.SetSlot("jacket", null);
                foreach (Renderer renderer in jacket.CoveredBodyRenderers) Assert.That(renderer.enabled, Is.True);
                wardrobe.RestoreOutfit(original);
                Assert.That(replacement.enabled, Is.False);
                Assert.That(jacket.Renderers.All(renderer => renderer.enabled), Is.True);
            }
            finally
            {
                lease?.Restore();
                UnityEngine.Object.DestroyImmediate(instance);
            }
        }

        private readonly struct Snapshot
        {
            public readonly bool Enabled;
            public readonly Material Material;
            public readonly Color Color;

            public Snapshot(bool enabled, Material material, Color color)
            {
                Enabled = enabled;
                Material = material;
                Color = color;
            }
        }

        private static Dictionary<string, Snapshot> Capture(IReadOnlyList<Player3DMeshBinding> bindings)
        {
            var block = new MaterialPropertyBlock();
            var result = new Dictionary<string, Snapshot>(bindings.Count);
            foreach (Player3DMeshBinding binding in bindings)
            {
                if (binding?.Renderer == null)
                {
                    continue;
                }

                binding.Renderer.GetPropertyBlock(block);
                result[binding.MeshName] = new Snapshot(
                    binding.Renderer.enabled,
                    binding.Renderer.sharedMaterial,
                    block.GetColor("_BaseColor"));
            }

            return result;
        }

        private static void AssertSame(Snapshot expected, Player3DMeshBinding binding, string name)
        {
            Assert.That(binding.Renderer.enabled, Is.EqualTo(expected.Enabled), name + " enabled flag");
            Assert.That(ReferenceEquals(binding.Renderer.sharedMaterial, expected.Material), Is.True, name + " material");
            var block = new MaterialPropertyBlock();
            binding.Renderer.GetPropertyBlock(block);
            AssertColor(block.GetColor("_BaseColor"), expected.Color, name + " tint");
        }

        private static void AssertColor(Color actual, Color expected, string message)
        {
            Assert.That(actual.r, Is.EqualTo(expected.r).Within(1e-5f), message);
            Assert.That(actual.g, Is.EqualTo(expected.g).Within(1e-5f), message);
            Assert.That(actual.b, Is.EqualTo(expected.b).Within(1e-5f), message);
        }

        private static Player3DMeshBinding Find(IReadOnlyList<Player3DMeshBinding> bindings, string meshName)
        {
            foreach (Player3DMeshBinding binding in bindings)
            {
                if (binding?.Renderer != null && binding.MeshName == meshName)
                {
                    return binding;
                }
            }

            Assert.Fail("The rig no longer has '" + meshName + "'.");
            return null;
        }
    }
}

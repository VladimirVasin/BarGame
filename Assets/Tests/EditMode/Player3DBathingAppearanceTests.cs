using System;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;

namespace BarPromenade.Tests.EditMode
{
    /// <summary>
    /// Undressing the production hero for the shower, and dressing him
    /// again exactly. The rule is stated against roles and bones; on the
    /// V2 prefab it takes off the four jacket parts, keeps the bandage,
    /// paints the shirt and the jeans-wearing body parts skin on the
    /// hero's own material, and puts every flag, material and tint back
    /// byte for byte.
    /// </summary>
    public sealed class Player3DBathingAppearanceTests
    {
        private static readonly string[] MustBeHidden =
        {
            "CLO_JacketBody",
            "CLO_JacketSleeve.L",
            "CLO_JacketSleeve.R",
            "CLO_JacketForearm.R"
        };

        private static readonly string[] MustStayVisible =
        {
            "CLO_Bandage.L",
            "GEO_Torso",
            "GEO_Pelvis",
            "GEO_Head",
            "GEO_Hand.L",
            "GEO_Hand.R",
            "GEO_Foot.L"
        };

        private static readonly string[] MustBeRepainted =
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

        [TestCase("clothing", true, true)]
        [TestCase("clothing", false, true)]
        [TestCase("signature_detail", true, false)]
        [TestCase("signature_detail", false, true)]
        [TestCase("body_part", true, false)]
        [TestCase("hair", false, false)]
        [TestCase("", true, false)]
        [TestCase(null, true, false)]
        public void TheHidingRule_IsStatedAgainstTheRole(string role, bool keepBandage, bool expected)
        {
            Assert.That(Player3DBathingAppearance.IsHidden(role, keepBandage), Is.EqualTo(expected));
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
                foreach (Player3DMeshBinding binding in bindings)
                {
                    if (binding?.Renderer != null)
                    {
                        binding.Renderer.enabled = true;
                    }
                }

                Dictionary<string, Snapshot> before = Capture(bindings);
                Player3DMeshBinding skin = Find(bindings, "GEO_Hand.R");
                Assert.That(skin, Is.Not.Null);
                Assert.That(skin.PaletteMaterialName, Is.EqualTo(Player3DBathingAppearance.SkinMaterialName));

                Assert.That(Player3DBathingAppearance.IsActive, Is.False);
                lease = Player3DBathingAppearance.Apply(registry);
                Assert.That(Player3DBathingAppearance.IsActive, Is.True);
                Assert.That(lease.HiddenRendererCount, Is.EqualTo(MustBeHidden.Length));
                Assert.That(lease.RepaintedRendererCount, Is.EqualTo(MustBeRepainted.Length));
                Assert.Throws<InvalidOperationException>(
                    () => Player3DBathingAppearance.Apply(registry),
                    "Only one owner may undress him at a time.");

                foreach (string name in MustBeHidden)
                {
                    Assert.That(Find(bindings, name).Renderer.enabled, Is.False, name + " must be off.");
                }

                foreach (string name in MustStayVisible)
                {
                    Assert.That(Find(bindings, name).Renderer.enabled, Is.True, name + " must stay on.");
                }

                var block = new MaterialPropertyBlock();
                Texture2D atlas = Player3DBathingAppearance.BareSkinAtlas;
                Assert.That(lease.UsesBareSkinAtlas, Is.EqualTo(atlas != null));
                foreach (string name in MustBeRepainted)
                {
                    Player3DMeshBinding binding = Find(bindings, name);
                    Assert.That(
                        ReferenceEquals(binding.Renderer.sharedMaterial, skin.Renderer.sharedMaterial),
                        Is.True,
                        name + " must borrow the hero's own skin material, never a new one.");
                    binding.Renderer.GetPropertyBlock(block);
                    Color tint = block.GetColor("_BaseColor");
                    if (atlas != null)
                    {
                        Assert.That(block.GetTexture("_BaseMap"), Is.EqualTo(atlas), name + " must wear the bare-skin atlas.");
                        AssertColor(tint, Color.white, name + " must not tint the atlas a second time.");
                    }
                    else
                    {
                        Assert.That(tint, Is.Not.EqualTo(Color.white), name + " must carry a skin tint.");
                        Assert.That(tint, Is.Not.EqualTo(before[name].Color), name + " must have changed colour.");
                    }
                }

                if (atlas == null)
                {
                    Find(bindings, "GEO_Torso").Renderer.GetPropertyBlock(block);
                    AssertColor(block.GetColor("_BaseColor"), skin.BaseColor, "The torso wears the hand's skin tone.");
                    Find(bindings, "GEO_Foot.L").Renderer.GetPropertyBlock(block);
                    AssertColor(block.GetColor("_BaseColor"), Player3DBathingAppearance.SkinDark, "The boots go dark skin.");
                }

                // Nothing that was never repainted carries the atlas.
                Find(bindings, "GEO_Hand.L").Renderer.GetPropertyBlock(block);
                Assert.That(block.GetTexture("_BaseMap"), Is.Null, "The hands were skin already.");

                // Untouched parts are exactly as they were.
                foreach (string name in new[] { "GEO_Head", "GEO_Hand.L", "GEO_Forearm.R", "CLO_Bandage.L" })
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
                lease = Player3DBathingAppearance.Apply(registry);
                Assert.That(lease.HiddenRendererCount, Is.EqualTo(MustBeHidden.Length - 1));
                lease.Restore();
                lease = null;
                Assert.That(jacket.Renderer.enabled, Is.False, "It was off before and it stays off.");
            }
            finally
            {
                lease?.Restore();
                UnityEngine.Object.DestroyImmediate(instance);
            }
        }

        [Test]
        public void TheBandageComesOffOnlyWhenAsked()
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
                Player3DMeshBinding bandage = Find(registry.MeshBindings, "CLO_Bandage.L");
                Assert.That(bandage.Role, Is.EqualTo(Player3DBathingAppearance.SignatureDetailRole));
                lease = Player3DBathingAppearance.Apply(registry, keepBandage: false);
                Assert.That(lease.HiddenRendererCount, Is.EqualTo(MustBeHidden.Length + 1));
                Assert.That(bandage.Renderer.enabled, Is.False);
                lease.Restore();
                lease = null;
                Assert.That(bandage.Renderer.enabled, Is.True);
                Assert.That(lease, Is.Null);
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

using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;

namespace BarPromenade.Tests.EditMode
{
    /// <summary>
    /// Taking the hero's head off from the inside.
    ///
    /// This is a regression fixture with a very specific failure behind
    /// it: the first seated first-person view hid only the two anatomical
    /// parts called Head and Neck and left the player looking at the
    /// inside of the separately bound face and hair meshes. What is pinned
    /// here is that the rule is stated against the bones — anything on
    /// `head`, `neck` or a `face.*` bone — and that on the production V2
    /// prefab it catches every one of them and nothing below the collar.
    /// </summary>
    public sealed class Player3DHeadVisibilityTests
    {
        /// <summary>The parts a player would notice immediately if they
        /// stayed on, named rather than counted.</summary>
        private static readonly string[] MustBeHidden =
        {
            "GEO_Head",
            "GEO_Neck",
            "GEO_FaceSurface",
            "GEO_HairCap",
            "GEO_HairBack",
            "GEO_HairTemple.L",
            "GEO_HairTemple.R",
            "GEO_HairTuft.01",
            "GEO_HairTuft.04",
            "GEO_Ear.L",
            "GEO_Ear.R"
        };

        /// <summary>And the ones that have to survive, because a seated
        /// man with no hands over the board is worse than one with a
        /// nose in the way.</summary>
        private static readonly string[] MustStayVisible =
        {
            "GEO_Torso",
            "GEO_Hand.L",
            "GEO_Hand.R",
            "GEO_Forearm.L",
            "GEO_Forearm.R",
            "CLO_JacketBody"
        };

        [TestCase("head", true)]
        [TestCase("neck", true)]
        [TestCase("face.eye.L", true)]
        [TestCase("face.brow.R", true)]
        [TestCase("face.mouth", true)]
        [TestCase("chest", false)]
        [TestCase("hand.L", false)]
        [TestCase("upper_arm.R", false)]
        [TestCase("pelvis", false)]
        [TestCase("", false)]
        [TestCase(null, false)]
        public void TheRule_IsStatedAgainstTheBone(
            string boneName,
            bool expected)
        {
            Assert.That(
                Player3DHeadVisibility.IsHeadGeometry(boneName),
                Is.EqualTo(expected));
        }

        [Test]
        public void OnTheProductionRig_TheWholeHeadComesOffAndGoesBack()
        {
            GameObject prefab = Player3DResources.LoadPrefab();
            if (prefab == null)
            {
                Assert.Ignore("The Player 3D prefab is not built yet.");
            }

            GameObject instance = Object.Instantiate(prefab);
            try
            {
                var registry =
                    instance.GetComponentInChildren<
                        Player3DAssetRegistry>(true);
                Assert.That(registry, Is.Not.Null);
                Assert.That(
                    registry.HasFaceAtlas,
                    Is.True,
                    "The production default must be the V2 hero.");

                var before = new Dictionary<string, bool>(96);
                var expectedHiddenRenderers = new HashSet<Renderer>();
                IReadOnlyList<Player3DMeshBinding> bindings =
                    registry.MeshBindings;
                Assert.That(bindings, Is.Not.Empty);
                for (int index = 0; index < bindings.Count; index++)
                {
                    Player3DMeshBinding binding = bindings[index];
                    if (binding?.Renderer != null)
                    {
                        binding.Renderer.enabled = true;
                        before[binding.MeshName] = true;
                        if (Player3DHeadVisibility.IsHeadGeometry(
                                binding.BoneName))
                        {
                            expectedHiddenRenderers.Add(binding.Renderer);
                        }
                    }
                }

                Assert.That(
                    expectedHiddenRenderers.Count,
                    Is.GreaterThanOrEqualTo(MustBeHidden.Length),
                    "The V2 head must remain a multi-mesh presentation.");

                Player3DHeadVisibility hidden =
                    Player3DHeadVisibility.Hide(registry);
                Assert.That(
                    hidden.HiddenRendererCount,
                    Is.EqualTo(expectedHiddenRenderers.Count),
                    "Every enabled renderer classified by the head-bone " +
                    "rule must be hidden exactly once.");

                for (int index = 0; index < bindings.Count; index++)
                {
                    Player3DMeshBinding binding = bindings[index];
                    if (binding?.Renderer == null)
                    {
                        continue;
                    }

                    bool isHead =
                        Player3DHeadVisibility.IsHeadGeometry(
                            binding.BoneName);
                    Assert.That(
                        binding.Renderer.enabled,
                        Is.EqualTo(!isHead),
                        $"'{binding.MeshName}' on bone " +
                        $"'{binding.BoneName}' is on the wrong side of " +
                        "the collar.");
                }

                AssertNamedParts(bindings, MustBeHidden, false);
                AssertNamedParts(bindings, MustStayVisible, true);

                hidden.Restore();
                for (int index = 0; index < bindings.Count; index++)
                {
                    Player3DMeshBinding binding = bindings[index];
                    if (binding?.Renderer != null &&
                        before.ContainsKey(binding.MeshName))
                    {
                        Assert.That(
                            binding.Renderer.enabled,
                            Is.True,
                            $"'{binding.MeshName}' never came back.");
                    }
                }

                Assert.That(hidden.HiddenRendererCount, Is.Zero);
            }
            finally
            {
                Object.DestroyImmediate(instance);
            }
        }

        /// <summary>
        /// A renderer somebody else had already switched off is not
        /// this class's to switch back on.
        /// </summary>
        [Test]
        public void ARendererAlreadyOff_IsLeftAlone()
        {
            GameObject prefab = Player3DResources.LoadPrefab();
            if (prefab == null)
            {
                Assert.Ignore("The Player 3D prefab is not built yet.");
            }

            GameObject instance = Object.Instantiate(prefab);
            try
            {
                var registry =
                    instance.GetComponentInChildren<
                        Player3DAssetRegistry>(true);
                Assert.That(registry, Is.Not.Null);
                Player3DMeshBinding head = FindFirstHeadBinding(
                    registry.MeshBindings);
                Assert.That(head, Is.Not.Null);
                head.Renderer.enabled = false;

                Player3DHeadVisibility hidden =
                    Player3DHeadVisibility.Hide(registry);
                hidden.Restore();

                Assert.That(
                    head.Renderer.enabled,
                    Is.False,
                    "It was off before and it stays off.");
            }
            finally
            {
                Object.DestroyImmediate(instance);
            }
        }

        private static void AssertNamedParts(
            IReadOnlyList<Player3DMeshBinding> bindings,
            IReadOnlyList<string> names,
            bool expectedEnabled)
        {
            for (int index = 0; index < names.Count; index++)
            {
                Player3DMeshBinding binding =
                    FindBinding(bindings, names[index]);
                Assert.That(
                    binding,
                    Is.Not.Null,
                    $"The rig no longer has '{names[index]}'.");
                Assert.That(
                    binding.Renderer.enabled,
                    Is.EqualTo(expectedEnabled),
                    $"'{names[index]}' is visible when it should not " +
                    "be, or the other way round.");
            }
        }

        private static Player3DMeshBinding FindBinding(
            IReadOnlyList<Player3DMeshBinding> bindings,
            string meshName)
        {
            for (int index = 0; index < bindings.Count; index++)
            {
                Player3DMeshBinding binding = bindings[index];
                if (binding?.Renderer != null &&
                    binding.MeshName == meshName)
                {
                    return binding;
                }
            }

            return null;
        }

        private static Player3DMeshBinding FindFirstHeadBinding(
            IReadOnlyList<Player3DMeshBinding> bindings)
        {
            for (int index = 0; index < bindings.Count; index++)
            {
                Player3DMeshBinding binding = bindings[index];
                if (binding?.Renderer != null &&
                    Player3DHeadVisibility.IsHeadGeometry(
                        binding.BoneName))
                {
                    return binding;
                }
            }

            return null;
        }
    }
}

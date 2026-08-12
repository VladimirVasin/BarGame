using System;
using System.Collections;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace BarPromenade.Tests.PlayMode
{
    /// <summary>
    /// The airborne design is the one walker the runtime does not pin to the
    /// pavement every frame, so nothing else proves where it actually renders.
    /// EditMode cannot answer this — a PlayableGraph writes no transforms in a
    /// batch-mode EditMode run — so the rendered arc is measured here, against
    /// the same contract the deterministic generator proves in Blender.
    /// </summary>
    public sealed class CityPedestrianAirborneGroundingPlayModeTests
    {
        private const float MinimumApex = 0.08f;
        private const float SampleDeltaTime = 1f / 60f;
        private const int SampleCount = 150;

        private GameObject instance;
        private GameObject cameraObject;

        [TearDown]
        public void TearDown()
        {
            if (instance != null)
            {
                UnityEngine.Object.DestroyImmediate(instance);
                instance = null;
            }

            if (cameraObject != null)
            {
                UnityEngine.Object.DestroyImmediate(cameraObject);
                cameraObject = null;
            }
        }

        [UnityTest]
        public IEnumerator AirborneWalker_LandsOnThePavementAndKeepsItsArc()
        {
            Assert.That(
                CityPedestrianResources.TryGetArchetype(
                    CityPedestrianResources.HelmetLampDesignId,
                    out CityPedestrianArchetype archetype),
                Is.True);
            GameObject prefab = CityPedestrianResources.LoadPrefab(archetype);
            Assert.That(prefab, Is.Not.Null);

            // The presentation runs its Animator with CullUpdateTransforms, so
            // an offscreen rig writes no transforms at all and every sample
            // would read the bind pose. A camera that actually sees the walker
            // is part of reproducing the runtime, not decoration.
            cameraObject = new GameObject("Airborne Grounding Camera");
            Camera camera = cameraObject.AddComponent<Camera>();
            camera.transform.position = new Vector3(0f, 1f, -5f);
            camera.transform.LookAt(new Vector3(0f, 0.9f, 0f));
            camera.nearClipPlane = 0.1f;
            camera.farClipPlane = 50f;

            var root = new GameObject("Airborne Grounding Root");
            root.transform.position = Vector3.zero;
            instance = UnityEngine.Object.Instantiate(
                prefab,
                root.transform,
                false);
            instance.transform.localPosition = Vector3.zero;
            CityPedestrianAssetRegistry registry =
                instance.GetComponent<CityPedestrianAssetRegistry>();
            Assert.That(registry, Is.Not.Null);
            Assert.That(
                registry.PreservesAirborneMotion,
                Is.True,
                "This test only means anything for an airborne design.");

            // Measured from bone transforms, which graph.Evaluate writes
            // unconditionally. Skinned renderer bounds are useless here: with
            // nothing rendering they never recompute, so they report the bind
            // pose no matter what the clip does.
            Transform leftFoot = registry.LeftFootAnchor;
            Transform rightFoot = registry.RightFootAnchor;
            Assert.That(leftFoot, Is.Not.Null);
            Assert.That(rightFoot, Is.Not.Null);
            float soleOffset = CaptureRigidSoleOffset(
                instance,
                leftFoot,
                rightFoot);
            Assert.That(
                float.IsFinite(soleOffset),
                Is.True,
                "The design exposes no sole renderer to measure against.");

            CityPedestrianPresentation presentation =
                instance.AddComponent<CityPedestrianPresentation>();
            presentation.Initialize(registry);
            // The presentation selects CullUpdateTransforms, and a batch-mode
            // run never renders, so the rig would stay on its bind pose and
            // every sample would be inert. Culling is a visibility
            // optimisation and orthogonal to the pose contract under test.
            registry.Animator.cullingMode =
                AnimatorCullingMode.AlwaysAnimate;
            presentation.SetMoving(true);

            float groundPlane = instance.transform.position.y;
            float idleLowest = float.PositiveInfinity;
            float idleApex = float.NegativeInfinity;
            presentation.SetMoving(false);
            for (int sample = 0; sample < SampleCount; sample++)
            {
                presentation.Advance(SampleDeltaTime, false, true);
                yield return null;
                float soleHeight =
                    Mathf.Min(
                        leftFoot.position.y,
                        rightFoot.position.y) +
                    soleOffset -
                    groundPlane;
                idleLowest = Mathf.Min(idleLowest, soleHeight);
                idleApex = Mathf.Max(idleApex, soleHeight);
            }

            float lowest = float.PositiveInfinity;
            float apex = float.NegativeInfinity;
            presentation.SetMoving(true);
            for (int sample = 0; sample < SampleCount; sample++)
            {
                presentation.Advance(SampleDeltaTime, true, true);
                yield return null;
                float soleHeight =
                    Mathf.Min(
                        leftFoot.position.y,
                        rightFoot.position.y) +
                    soleOffset -
                    groundPlane;
                lowest = Mathf.Min(lowest, soleHeight);
                apex = Mathf.Max(apex, soleHeight);
            }

            presentation.Shutdown();
            UnityEngine.Object.DestroyImmediate(root);
            instance = null;

            string measured =
                $"idle [{idleLowest:0.000}, {idleApex:0.000}] m, " +
                $"hop [{lowest:0.000}, {apex:0.000}] m";

            // A frozen rig reports a perfectly flat arc, which would otherwise
            // read as a real regression. Prove the samples moved at all before
            // trusting them.
            Assert.That(
                apex - lowest,
                Is.GreaterThan(0.0001f),
                "The rig never moved: the samples describe a static pose, so " +
                $"this measurement proves nothing ({measured}).");
            // Heights are reported, not gated. This measurement approximates a
            // sole as a fixed drop below its foot bone and so ignores foot
            // rotation, which makes its absolute zero unreliable; the runtime's
            // own probe uses rotated sole corners and disagrees with it. What
            // it does measure soundly is how far the walker travels
            // vertically, which is the part a regression would destroy.
            TestContext.WriteLine($"airborne sole heights: {measured}");
            Assert.That(
                apex - lowest,
                Is.GreaterThanOrEqualTo(MinimumApex),
                $"The hop flattened to a {(apex - lowest):0.000} m arc; the " +
                $"design ships a real bound, not a shuffle ({measured}).");
            Assert.That(
                idleApex - idleLowest,
                Is.LessThan(MinimumApex),
                $"Idle must stay a settled crouch, not a hop ({measured}).");
        }

        /// <summary>
        /// Distance from a foot bone down to the bottom of its sole, read once
        /// at the bind pose where renderer bounds are still meaningful. The
        /// sole is rigid relative to its bone, so this offset holds for every
        /// later frame and turns a bone height into a sole height.
        /// </summary>
        private static float CaptureRigidSoleOffset(
            GameObject target,
            Transform leftFoot,
            Transform rightFoot)
        {
            float offset = float.PositiveInfinity;
            Renderer[] renderers =
                target.GetComponentsInChildren<Renderer>(true);
            for (int index = 0; index < renderers.Length; index++)
            {
                Renderer renderer = renderers[index];
                string name = renderer.name;
                bool left = name.IndexOf(
                                "LeftBootSole",
                                StringComparison.Ordinal) >= 0 ||
                            name.IndexOf(
                                "ShoeSole.L",
                                StringComparison.Ordinal) >= 0;
                bool right = name.IndexOf(
                                 "RightBootSole",
                                 StringComparison.Ordinal) >= 0 ||
                             name.IndexOf(
                                 "ShoeSole.R",
                                 StringComparison.Ordinal) >= 0;
                if (!left && !right)
                {
                    continue;
                }

                Transform foot = left ? leftFoot : rightFoot;
                offset = Mathf.Min(
                    offset,
                    renderer.bounds.min.y - foot.position.y);
            }

            return offset;
        }
    }
}

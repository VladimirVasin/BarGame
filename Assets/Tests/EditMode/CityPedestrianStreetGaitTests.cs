using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;

namespace BarPromenade.Tests.EditMode
{
    /// <summary>
    /// The six designs that roam the street walk on the shared citizen
    /// gait - the hero's own Idle and Walk merged onto each body. Until
    /// 2026-09-05 that recipe re-expressed the hero's upper-arm aims as
    /// bone-local X rotations and merged them OVER each design's base
    /// arms; on the shared A-pose rig the upper arm's local X axis points
    /// back and up, so every street copy stood in the bind A-pose (arms
    /// 56 degrees out) and flapped them up and down while walking, and
    /// the two park players waited at crossings in their board perch, in
    /// mid-air. This pins the shipped clips against the walker's own
    /// bones, sampled through the production presentation: arms hang,
    /// swing fore and aft in opposition, and every street idle stands.
    /// </summary>
    public sealed class CityPedestrianStreetGaitTests
    {
        /// <summary>Hero reference: his idle arm hangs 11.5 degrees off
        /// vertical, his walk swings between 10 and 30 degrees.</summary>
        private const float IdleArmMaxDegreesFromVertical = 22f;
        private const float WalkArmMaxDegreesFromVertical = 40f;

        /// <summary>The sideways share of the arm's direction: the hero's
        /// is 0.17. The old A-pose arms measured 0.8 and above.</summary>
        private const float MaxLateralShare = 0.35f;

        /// <summary>A standing thigh; the board perch folds to 72 degrees.</summary>
        private const float StandingThighMaxDegreesFromVertical = 30f;

        private static readonly string[] RoamingPrefabPaths =
        {
            CityPedestrianResources.BabushkaPrefabResourcePath,
            CityPedestrianResources.WeighAttendantPrefabResourcePath,
            CityPedestrianResources.WatchmanPrefabResourcePath,
            CityPedestrianResources.ChessPlayerPrefabResourcePath,
            CityPedestrianResources.CheckersPlayerPrefabResourcePath,
            CityPedestrianResources.MournerPrefabResourcePath
        };

        [Test]
        public void RoamingWalkers_HangTheirArmsSwingThemAndStandOnTheStreet(
            [ValueSource(nameof(RoamingPrefabPaths))] string prefabPath)
        {
            GameObject prefab = Resources.Load<GameObject>(prefabPath);
            Assert.That(prefab, Is.Not.Null, $"{prefabPath} is missing.");
            var root = new GameObject("Street Gait Root");
            CityPedestrianPresentation presentation = null;
            try
            {
                GameObject instance = Object.Instantiate(prefab, root.transform);
                var registry = instance.GetComponent<CityPedestrianAssetRegistry>();
                Assert.That(registry, Is.Not.Null);
                presentation = instance.AddComponent<CityPedestrianPresentation>();
                presentation.Initialize(registry, CityPedestrianClipSource.Roaming);
                // A batch run draws nothing, and under CullUpdateTransforms
                // the Animator would leave every bone in its bind pose.
                registry.Animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
                Transform bones = registry.Animator.transform;
                Transform facing = instance.transform;

                // The street idle: arms hanging, both thighs under the hips.
                presentation.SetMoving(false, true);
                foreach (float phase in new[] { 0f, 0.45f })
                {
                    presentation.ConfigureCycle(1f, phase);
                    presentation.Advance(0f, false, true);
                    foreach (string side in new[] { "L", "R" })
                    {
                        Vector3 arm = ArmDirection(bones, side);
                        Assert.That(
                            Vector3.Angle(arm, Vector3.down),
                            Is.LessThan(IdleArmMaxDegreesFromVertical),
                            $"{prefab.name} idle phase {phase}: upper arm {side} " +
                            "does not hang.");
                        Assert.That(
                            Mathf.Abs(Vector3.Dot(arm, facing.right)),
                            Is.LessThan(MaxLateralShare),
                            $"{prefab.name} idle phase {phase}: upper arm {side} " +
                            "is held out sideways.");
                        Vector3 thigh = ThighDirection(bones, side);
                        Assert.That(
                            Vector3.Angle(thigh, Vector3.down),
                            Is.LessThan(StandingThighMaxDegreesFromVertical),
                            $"{prefab.name} idle phase {phase}: thigh {side} is " +
                            "not standing.");
                    }
                }

                // The street walk: arms stay down and sideways-quiet on all
                // eight of the hero's keys, swing fore and aft by a real
                // amount, and oppose each other at heel contact.
                presentation.SetMoving(true, true);
                var forwardShare = new Dictionary<string, List<float>>
                {
                    ["L"] = new List<float>(),
                    ["R"] = new List<float>()
                };
                for (int key = 0; key < 8; key++)
                {
                    float phase = key / 8f;
                    presentation.ConfigureCycle(1f, phase);
                    presentation.Advance(0f, true, true);
                    foreach (string side in new[] { "L", "R" })
                    {
                        Vector3 arm = ArmDirection(bones, side);
                        Assert.That(
                            Vector3.Angle(arm, Vector3.down),
                            Is.LessThan(WalkArmMaxDegreesFromVertical),
                            $"{prefab.name} walk phase {phase}: upper arm {side} " +
                            "leaves the hang.");
                        Assert.That(
                            Mathf.Abs(Vector3.Dot(arm, facing.right)),
                            Is.LessThan(MaxLateralShare),
                            $"{prefab.name} walk phase {phase}: upper arm {side} " +
                            "swings sideways.");
                        forwardShare[side].Add(Vector3.Dot(arm, facing.forward));
                    }
                }

                foreach (string side in new[] { "L", "R" })
                {
                    float range = Mathf.Max(forwardShare[side].ToArray()) -
                                  Mathf.Min(forwardShare[side].ToArray());
                    Assert.That(
                        range,
                        Is.GreaterThan(0.25f),
                        $"{prefab.name}: upper arm {side} does not swing fore " +
                        "and aft across the cycle.");
                }

                Assert.That(
                    forwardShare["L"][0] * forwardShare["R"][0],
                    Is.LessThan(0f),
                    $"{prefab.name}: the arms must oppose each other at heel " +
                    "contact.");
            }
            finally
            {
                presentation?.Shutdown();
                Object.DestroyImmediate(root);
            }
        }

        /// <summary>Shoulder to elbow, in world space.</summary>
        private static Vector3 ArmDirection(Transform bones, string side)
        {
            Transform upper = FindBone(bones, $"upper_arm.{side}");
            Transform forearm = FindBone(bones, $"forearm.{side}");
            Assert.That(upper, Is.Not.Null);
            Assert.That(forearm, Is.Not.Null);
            return (forearm.position - upper.position).normalized;
        }

        /// <summary>Hip to knee, in world space.</summary>
        private static Vector3 ThighDirection(Transform bones, string side)
        {
            Transform thigh = FindBone(bones, $"thigh.{side}");
            Transform shin = FindBone(bones, $"shin.{side}");
            Assert.That(thigh, Is.Not.Null);
            Assert.That(shin, Is.Not.Null);
            return (shin.position - thigh.position).normalized;
        }

        private static Transform FindBone(Transform root, string boneName)
        {
            if (root.name == boneName)
            {
                return root;
            }

            for (int index = 0; index < root.childCount; index++)
            {
                Transform found = FindBone(root.GetChild(index), boneName);
                if (found != null)
                {
                    return found;
                }
            }

            return null;
        }
    }
}

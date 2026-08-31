using System.Reflection;
using NUnit.Framework;
using UnityEngine;

namespace BarPromenade.Tests.EditMode
{
    /// <summary>
    /// The empirical arbiter of the raven's flight READ, run on the
    /// BUILT prefab rather than the hand-made test rig. The rig
    /// mirrors what we believe the FBX -Z-forward bake and the two
    /// stacked half turns produce; only the real imported meshes can
    /// prove the belief, because a wing whose fold sign swings it
    /// INWARD through the body, or a flap composed onto the folded
    /// slab's own long axis, still satisfies every rotation-angle
    /// assertion while the player sees a bird with no wings at all —
    /// which is exactly what the playtest reported. So these tests
    /// measure renderer BOUNDS in world space: deployment must move
    /// each wing's mass away from the body's centre plane, and the
    /// flap phase must move it vertically. Guarded like
    /// <see cref="CemeteryRavenAssetTests"/>: while the prefab has
    /// not been built yet they ignore themselves instead of failing.
    /// </summary>
    public sealed class RavenFlightReadabilityTests
    {
        /// <summary>The task's readability floor: a deploy that
        /// shifts a wing's bounds centre less than this laterally is
        /// a pinwheel, not an unfolding.</summary>
        private const float MinimumDeployLateralMeters = 0.04f;

        /// <summary>Opposite flap phases must separate each wing's
        /// bounds centre by at least this much vertically, or the
        /// beat is an invisible roll.</summary>
        private const float MinimumFlapHeightDeltaMeters = 0.03f;

        [Test]
        public void FullDeploy_MovesBothWingsOutwardToOppositeSides()
        {
            CemeteryRavenProvider provider = LoadBuiltProvider();
            GameObject host = new GameObject(
                "Raven Flight Readability Host");
            try
            {
                CemeteryRavenActor actor = BuildActor(host, provider);
                Renderer wingLeft = FindWingRenderer(
                    actor.Anchors,
                    CemeteryRavenRigAnchors.WingLeftPivotName);
                Renderer wingRight = FindWingRenderer(
                    actor.Anchors,
                    CemeteryRavenRigAnchors.WingRightPivotName);

                // A neutral flight pose rather than "whatever the
                // idle model woke into": the folded reference must
                // be the same on every machine and seed.
                ApplyPose(
                    actor,
                    CemeteryRavenPoseRules.FlightPose(0f, 0f, 0f, 0f));
                float restLeft = Lateral(host, wingLeft);
                float restRight = Lateral(host, wingRight);

                ApplyPose(
                    actor,
                    CemeteryRavenPoseRules.FlightPose(
                        1f,
                        Mathf.PI * 0.5f,
                        0f,
                        0f));
                float deployedLeft = Lateral(host, wingLeft);
                float deployedRight = Lateral(host, wingRight);

                Assert.That(
                    Mathf.Abs(deployedLeft),
                    Is.GreaterThanOrEqualTo(
                        Mathf.Abs(restLeft) +
                        MinimumDeployLateralMeters),
                    "The left wing's mass must travel away from the " +
                    "body's centre plane, outside the folded " +
                    "silhouette.");
                Assert.That(
                    Mathf.Abs(deployedRight),
                    Is.GreaterThanOrEqualTo(
                        Mathf.Abs(restRight) +
                        MinimumDeployLateralMeters),
                    "The right wing's mass must travel away from " +
                    "the body's centre plane, outside the folded " +
                    "silhouette.");

                // Away means AWAY: each wing deploys over its own
                // flank rather than crossing through the body to the
                // far side...
                Assert.That(
                    Mathf.Sign(deployedLeft),
                    Is.EqualTo(Mathf.Sign(restLeft)),
                    "The left wing must deploy over its own flank.");
                Assert.That(
                    Mathf.Sign(deployedRight),
                    Is.EqualTo(Mathf.Sign(restRight)),
                    "The right wing must deploy over its own flank.");

                // ...and the pair must open into a SPAN, one wing on
                // each side, which is the one thing a sign error on
                // the fold can never fake.
                Assert.That(
                    deployedLeft * deployedRight,
                    Is.LessThan(0f),
                    "Deployed wings must occupy opposite lateral " +
                    "sides of the body.");
            }
            finally
            {
                Object.DestroyImmediate(host);
            }
        }

        [Test]
        public void FlapPhase_SwingsTheDeployedWingsVertically()
        {
            CemeteryRavenProvider provider = LoadBuiltProvider();
            GameObject host = new GameObject(
                "Raven Flap Readability Host");
            try
            {
                CemeteryRavenActor actor = BuildActor(host, provider);
                Renderer wingLeft = FindWingRenderer(
                    actor.Anchors,
                    CemeteryRavenRigAnchors.WingLeftPivotName);
                Renderer wingRight = FindWingRenderer(
                    actor.Anchors,
                    CemeteryRavenRigAnchors.WingRightPivotName);

                // sin(+PI/2) and sin(-PI/2) are the two extremes of
                // the beat at full deployment. If the flap axis runs
                // along the folded slab — the shipped bug — both
                // poses land the wing mass at the same height and
                // this delta collapses to millimetres.
                ApplyPose(
                    actor,
                    CemeteryRavenPoseRules.FlightPose(
                        1f,
                        Mathf.PI * 0.5f,
                        0f,
                        0f));
                float upLeft = wingLeft.bounds.center.y;
                float upRight = wingRight.bounds.center.y;

                ApplyPose(
                    actor,
                    CemeteryRavenPoseRules.FlightPose(
                        1f,
                        -Mathf.PI * 0.5f,
                        0f,
                        0f));
                float downLeft = wingLeft.bounds.center.y;
                float downRight = wingRight.bounds.center.y;

                Assert.That(
                    Mathf.Abs(upLeft - downLeft),
                    Is.GreaterThanOrEqualTo(
                        MinimumFlapHeightDeltaMeters),
                    "Opposite flap phases must visibly raise and " +
                    "lower the left wing.");
                Assert.That(
                    Mathf.Abs(upRight - downRight),
                    Is.GreaterThanOrEqualTo(
                        MinimumFlapHeightDeltaMeters),
                    "Opposite flap phases must visibly raise and " +
                    "lower the right wing.");
            }
            finally
            {
                Object.DestroyImmediate(host);
            }
        }

        /// <summary>The asset suite's guard, verbatim: these tests
        /// measure the built prefab, and before the editor setup has
        /// built one there is nothing to measure.</summary>
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

        /// <summary>
        /// One bird the way the cemetery controller and the roost
        /// controller both spawn it: the factory's half-turned visual
        /// under a host, then the actor adopting the meshes under the
        /// exported pivots. The host stays at the origin with yaw 0,
        /// so the body's centre plane is the world YZ plane and
        /// lateral distance is a plain dot with the host's right.
        /// </summary>
        private static CemeteryRavenActor BuildActor(
            GameObject host,
            CemeteryRavenProvider provider)
        {
            CemeteryRavenRigAnchors anchors =
                CemeteryRavenFactory.CreateVisual(
                    host.transform,
                    provider);
            Assert.That(anchors, Is.Not.Null);

            CemeteryRavenActor actor =
                host.AddComponent<CemeteryRavenActor>();
            actor.Initialize(anchors, 0x0CA1, 0d);
            actor.SetPerched(Vector3.zero, 0f);
            return actor;
        }

        private static Renderer FindWingRenderer(
            CemeteryRavenRigAnchors anchors,
            string pivotName)
        {
            for (int index = 0;
                 index < anchors.RendererBindings.Count;
                 index++)
            {
                CemeteryRavenRendererBinding binding =
                    anchors.RendererBindings[index];
                if (binding != null &&
                    binding.PivotName == pivotName &&
                    binding.Renderer != null)
                {
                    return binding.Renderer;
                }
            }

            Assert.Fail(
                $"No renderer binds the wing pivot '{pivotName}'.");
            return null;
        }

        /// <summary>Signed distance of a renderer's bounds centre
        /// from the bird's centre plane, measured along the host's
        /// right axis so the test would keep meaning even under a
        /// yawed perch.</summary>
        private static float Lateral(GameObject host, Renderer wing)
        {
            return Vector3.Dot(
                wing.bounds.center - host.transform.position,
                host.transform.right);
        }

        /// <summary>
        /// Drives the actor's private pose write directly, the voice
        /// suite's reflection idiom: the public surface only reaches
        /// FlightPose through a whole timed flight, and this suite
        /// needs exact single poses, not an integration of them.
        /// </summary>
        private static void ApplyPose(
            CemeteryRavenActor actor,
            in CemeteryRavenPose pose)
        {
            MethodInfo method = typeof(CemeteryRavenActor).GetMethod(
                "ApplyPose",
                BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(
                method,
                Is.Not.Null,
                "CemeteryRavenActor.ApplyPose moved; update the " +
                "readability suite with it.");
            method.Invoke(actor, new object[] { pose });
        }
    }
}

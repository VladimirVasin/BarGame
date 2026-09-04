using NUnit.Framework;
using UnityEngine;

namespace BarPromenade.Tests.EditMode
{
    /// <summary>
    /// What the balance model hands the ragdoll: a rigid rotation about
    /// the support edge whose velocity field reproduces the centre of
    /// mass's speed at the centre of mass — nothing is added on top — and
    /// a legacy side-only shove that carries no rotation at all.
    /// </summary>
    public sealed class PlayerRagdollHandoffTests
    {
        private const float Tolerance = 0.0001f;

        [Test]
        public void VelocityAt_IsTheRigidRotationAboutThePivot()
        {
            // A fall to +X: the body rotates about the support edge, an
            // axis of up × right = −forward, at 1.5 rad/s. A point a
            // metre up moves at 1.5 m/s toward +X; the pivot does not
            // move.
            Vector3 axis = Vector3.right;
            float omega = 1.5f;
            Vector3 angular = Vector3.Cross(Vector3.up, axis) * omega;
            Vector3 pivot = new Vector3(2f, 0f, 3f);
            var handoff = new PlayerRagdollHandoff(
                axis * 1.2f,
                angular,
                axis,
                pivot,
                1f);

            Vector3 atHead = handoff.VelocityAt(pivot + Vector3.up * 1f);
            Assert.That(atHead.x, Is.EqualTo(omega).Within(Tolerance));
            Assert.That(atHead.y, Is.EqualTo(0f).Within(Tolerance));
            Assert.That(atHead.z, Is.EqualTo(0f).Within(Tolerance));

            Vector3 atHip = handoff.VelocityAt(pivot + Vector3.up * 0.5f);
            Assert.That(atHip.x, Is.EqualTo(omega * 0.5f).Within(Tolerance));

            Vector3 atPivot = handoff.VelocityAt(pivot);
            Assert.That(atPivot.magnitude, Is.EqualTo(0f).Within(Tolerance));

            // A point already out along the fall dips: the rotation takes
            // it down as well as on.
            Vector3 ahead = handoff.VelocityAt(pivot + Vector3.up * 1f + axis * 0.5f);
            Assert.That(ahead.x, Is.EqualTo(omega).Within(Tolerance));
            Assert.That(ahead.y, Is.LessThan(0f));

            Assert.That(handoff.AngularSpeed, Is.EqualTo(omega).Within(Tolerance));
            Assert.That(handoff.SignedDirection, Is.EqualTo(1f));
        }

        [Test]
        public void ModelHandoff_ReproducesTheComVelocityAtTheCom()
        {
            // The controller's arithmetic, done here by hand: a lean of
            // 30° at 1.2 m/s over a 0.95 m pendulum gives the angular
            // speed v / (h cos θ), and the rotation's velocity at the
            // centre of mass — a lever of h·cos θ up and h·sin θ along
            // the fall from the pivot — has the planar speed back.
            float lean = 30f * Mathf.Deg2Rad;
            float height = 0.95f;
            float speed = 1.2f;
            float omega = PlayerBalanceRules.FallAngularVelocity(speed, 30f, height);
            Vector3 axis = Vector3.forward;
            var handoff = new PlayerRagdollHandoff(
                axis * speed,
                Vector3.Cross(Vector3.up, axis) * omega,
                axis,
                Vector3.zero,
                1f);

            Vector3 com = Vector3.up * (height * Mathf.Cos(lean)) +
                          axis * (height * Mathf.Sin(lean));
            Vector3 velocity = handoff.VelocityAt(com);
            Assert.That(Vector3.Dot(velocity, axis), Is.EqualTo(speed).Within(0.001f));
            Assert.That(velocity.y, Is.LessThan(0f), "the centre of mass is on its way down");
        }

        [Test]
        public void Legacy_CarriesNoRotationAndKeepsTheSide()
        {
            var root = new GameObject("Handoff Root").transform;
            root.position = new Vector3(1f, 0.04f, -2f);
            root.rotation = Quaternion.Euler(0f, 90f, 0f);
            try
            {
                PlayerRagdollHandoff left = PlayerRagdollHandoff.Legacy(-0.3f, root);
                Assert.That(left.SignedDirection, Is.EqualTo(-1f));
                Assert.That(left.AngularSpeed, Is.EqualTo(0f));
                Assert.That(left.LinearVelocity, Is.EqualTo(Vector3.zero));
                Assert.That(left.PivotPoint, Is.EqualTo(root.position));
                Assert.That(Vector3.Dot(left.FallAxis, root.right), Is.EqualTo(-1f).Within(0.001f));
                Assert.That(left.FallAxis.y, Is.EqualTo(0f).Within(Tolerance));
                Assert.That(left.VelocityAt(root.position + Vector3.up), Is.EqualTo(Vector3.zero));

                PlayerRagdollHandoff tie = PlayerRagdollHandoff.Legacy(0f, root);
                Assert.That(tie.SignedDirection, Is.EqualTo(1f), "a tie is the right side, as the clips are");

                PlayerRagdollHandoff rootless = PlayerRagdollHandoff.Legacy(1f, null);
                Assert.That(rootless.FallAxis, Is.EqualTo(Vector3.right));
            }
            finally
            {
                Object.DestroyImmediate(root.gameObject);
            }
        }

        [Test]
        public void Constructor_SanitizesAndNormalizes()
        {
            var handoff = new PlayerRagdollHandoff(
                new Vector3(float.NaN, 1f, 0f),
                new Vector3(0f, float.PositiveInfinity, 0f),
                new Vector3(3f, 4f, 0f),
                new Vector3(0f, float.NaN, 0f),
                -2f);

            Assert.That(handoff.LinearVelocity, Is.EqualTo(Vector3.zero));
            Assert.That(handoff.AngularVelocity, Is.EqualTo(Vector3.zero));
            Assert.That(handoff.PivotPoint, Is.EqualTo(Vector3.zero));
            Assert.That(handoff.FallAxis, Is.EqualTo(Vector3.right), "a vertical axis is planar-normalized; nothing left means right");
            Assert.That(handoff.SignedDirection, Is.EqualTo(-1f));

            var diagonal = new PlayerRagdollHandoff(
                Vector3.zero,
                Vector3.zero,
                new Vector3(1f, 5f, 1f),
                Vector3.zero,
                1f);
            Assert.That(diagonal.FallAxis.y, Is.EqualTo(0f));
            Assert.That(diagonal.FallAxis.magnitude, Is.EqualTo(1f).Within(Tolerance));
            Assert.That(diagonal.FallAxis.x, Is.EqualTo(diagonal.FallAxis.z).Within(Tolerance));
        }
    }
}

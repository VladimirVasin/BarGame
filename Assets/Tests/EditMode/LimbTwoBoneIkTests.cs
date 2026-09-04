using NUnit.Framework;
using UnityEngine;

namespace BarPromenade.Tests.EditMode
{
    public sealed class LimbTwoBoneIkTests
    {
        private const float UpperLength = 0.45f;
        private const float LowerLength = 0.45f;
        private const float FullChainLength = UpperLength + LowerLength;

        private static readonly Vector3 ForwardHint = new Vector3(0f, -0.3f, 1f);

        private GameObject root;
        private Transform upper;
        private Transform lower;
        private Transform tip;

        [SetUp]
        public void SetUp()
        {
            root = new GameObject("Limb IK Test Root");
            BuildChain(root.transform, out upper, out lower, out tip);
        }

        [TearDown]
        public void TearDown()
        {
            if (root != null)
            {
                Object.DestroyImmediate(root);
            }

            root = null;
            upper = null;
            lower = null;
            tip = null;
        }

        [Test]
        public void Solve_ReachesReachableTargetWithinOneMillimetre()
        {
            // Six tenths of a metre below and in front of the hip: well
            // inside the 0.9 m chain, so the knee has to bend a long way.
            var target = new Vector3(0f, -0.5f, 0.33f);

            LimbTwoBoneIk.Solve(
                upper,
                lower,
                tip,
                target,
                Quaternion.identity,
                ForwardHint);

            Assert.That(
                Vector3.Distance(tip.position, target),
                Is.LessThan(0.001f));
            Assert.That(
                LimbTwoBoneIk.ChainLength(upper, lower, tip),
                Is.EqualTo(FullChainLength).Within(0.0001f));
            Assert.That(upper.position.magnitude, Is.LessThan(0.000001f));
        }

        [Test]
        public void Solve_BendsTowardHint()
        {
            var target = new Vector3(0f, -0.45f, 0.4f);
            var backwardHint = new Vector3(0f, -0.3f, -1f);

            LimbTwoBoneIk.Solve(
                upper,
                lower,
                tip,
                target,
                Quaternion.identity,
                ForwardHint);
            Assert.That(
                Vector3.Distance(tip.position, target),
                Is.LessThan(0.001f));
            Assert.That(KneeSide(target, ForwardHint), Is.GreaterThan(0.9f));
            Assert.That(KneeSide(target, backwardHint), Is.LessThan(-0.9f));

            ResetChain();
            LimbTwoBoneIk.Solve(
                upper,
                lower,
                tip,
                target,
                Quaternion.identity,
                backwardHint);
            Assert.That(
                Vector3.Distance(tip.position, target),
                Is.LessThan(0.001f));
            Assert.That(KneeSide(target, backwardHint), Is.GreaterThan(0.9f));
            Assert.That(KneeSide(target, ForwardHint), Is.LessThan(-0.9f));
        }

        [Test]
        public void Solve_ClampsUnreachableTargetToReachFraction()
        {
            Vector3 direction = new Vector3(0f, -1f, 1f).normalized;
            Vector3 target = direction * 2f;
            const float clampedReach =
                FullChainLength * LimbTwoBoneIk.DefaultReachFraction;

            // The clamp itself is exact: pulled in along the same ray, and
            // left alone when the fraction is unclamped or the target is
            // already inside it.
            Vector3 pulledIn = LimbTwoBoneIk.ClampReach(
                Vector3.zero,
                FullChainLength,
                target,
                LimbTwoBoneIk.DefaultReachFraction);
            Assert.That(
                pulledIn.magnitude,
                Is.EqualTo(clampedReach).Within(0.00001f));
            Assert.That(
                Vector3.Dot(pulledIn.normalized, direction),
                Is.EqualTo(1f).Within(0.00001f));
            Assert.That(
                LimbTwoBoneIk.ClampReach(
                    Vector3.zero,
                    FullChainLength,
                    target,
                    float.PositiveInfinity),
                Is.EqualTo(target));
            Assert.That(
                LimbTwoBoneIk.ClampReach(
                    Vector3.zero,
                    FullChainLength,
                    target,
                    1f),
                Is.EqualTo(target));
            Vector3 inside = direction * 0.5f;
            Assert.That(
                LimbTwoBoneIk.ClampReach(
                    Vector3.zero,
                    FullChainLength,
                    inside,
                    LimbTwoBoneIk.DefaultReachFraction),
                Is.EqualTo(inside));

            // Solved with the clamp the tip is pulled in along the ray and
            // the knee keeps a visible bend: the tip never reaches past the
            // clamped target, and lands well inside the chain length.
            LimbTwoBoneIk.Solve(
                upper,
                lower,
                tip,
                target,
                Quaternion.identity,
                ForwardHint,
                1f,
                LimbTwoBoneIk.DefaultReachFraction,
                false);
            float clampedDistance = tip.position.magnitude;
            Assert.That(
                clampedDistance,
                Is.LessThanOrEqualTo(clampedReach + 0.005f));
            Assert.That(clampedDistance, Is.GreaterThan(0.81f));
            Assert.That(
                Vector3.Dot(tip.position.normalized, direction),
                Is.GreaterThan(0.999f));

            // Unclamped, the chain straightens to its full length toward the
            // same target and never overshoots it.
            ResetChain();
            LimbTwoBoneIk.Solve(
                upper,
                lower,
                tip,
                target,
                Quaternion.identity,
                ForwardHint,
                1f,
                float.PositiveInfinity,
                false);
            float straightDistance = tip.position.magnitude;
            Assert.That(
                straightDistance,
                Is.LessThanOrEqualTo(FullChainLength + 0.0001f));
            Assert.That(
                straightDistance,
                Is.EqualTo(FullChainLength).Within(0.001f));
            Assert.That(clampedDistance, Is.LessThan(straightDistance - 0.01f));
        }

        [Test]
        public void Solve_WeightZeroLeavesChainUntouched()
        {
            var target = new Vector3(0f, -0.5f, 0.33f);
            Quaternion tipRotation = Quaternion.Euler(0f, 90f, 0f);
            Quaternion upperBefore = upper.rotation;
            Quaternion lowerBefore = lower.rotation;
            Quaternion tipBefore = tip.rotation;
            Vector3 tipPositionBefore = tip.position;

            LimbTwoBoneIk.Solve(
                upper,
                lower,
                tip,
                target,
                tipRotation,
                ForwardHint,
                0f,
                LimbTwoBoneIk.DefaultReachFraction,
                true);
            Assert.That(upper.rotation, Is.EqualTo(upperBefore));
            Assert.That(lower.rotation, Is.EqualTo(lowerBefore));
            Assert.That(tip.rotation, Is.EqualTo(tipBefore));
            Assert.That(tip.position, Is.EqualTo(tipPositionBefore));

            LimbTwoBoneIk.Solve(
                upper,
                lower,
                tip,
                target,
                tipRotation,
                ForwardHint,
                -1f,
                LimbTwoBoneIk.DefaultReachFraction,
                true);
            Assert.That(upper.rotation, Is.EqualTo(upperBefore));
            Assert.That(lower.rotation, Is.EqualTo(lowerBefore));
            Assert.That(tip.rotation, Is.EqualTo(tipBefore));
        }

        [Test]
        public void Solve_HalfWeightLandsBetween()
        {
            // Start from a clip-like pose with the knee already forward, so
            // the solve is a real bend rather than a flip out of the
            // straight-leg singularity, and pull the foot up and in.
            upper.rotation = Quaternion.AngleAxis(-22.5f, Vector3.right);
            lower.rotation = upper.rotation *
                             Quaternion.AngleAxis(45f, Vector3.right);
            var target = new Vector3(0.1f, -0.5f, 0.2f);
            Quaternion upperBase = upper.rotation;
            Quaternion lowerBase = lower.rotation;
            Vector3 tipBase = tip.position;

            LimbTwoBoneIk.Solve(
                upper,
                lower,
                tip,
                target,
                Quaternion.identity,
                ForwardHint,
                1f,
                LimbTwoBoneIk.DefaultReachFraction,
                false);
            Quaternion upperFull = upper.rotation;
            Vector3 tipFull = tip.position;
            float upperFullAngle = Quaternion.Angle(upperBase, upperFull);
            float lowerFullAngle = Quaternion.Angle(lowerBase, lower.rotation);
            // Both joints move a clear amount: the hip swings the leg toward
            // the target and the knee opens from 135 to 75 degrees interior
            // (law of cosines for a 0.548 m reach). The knee's world delta
            // is well under that when the hip does most of the aiming, so
            // only a coarse floor is pinned here.
            Assert.That(
                Vector3.Distance(tipFull, target),
                Is.LessThan(0.002f));
            Assert.That(upperFullAngle, Is.GreaterThan(20f));
            Assert.That(lowerFullAngle, Is.GreaterThan(10f));

            upper.rotation = upperBase;
            lower.rotation = lowerBase;
            Assert.That(Vector3.Distance(tip.position, tipBase), Is.LessThan(0.00001f));

            LimbTwoBoneIk.Solve(
                upper,
                lower,
                tip,
                target,
                Quaternion.identity,
                ForwardHint,
                0.5f,
                LimbTwoBoneIk.DefaultReachFraction,
                false);

            // The hip joint is Slerped exactly half way from its pre-solve
            // rotation toward the solved one.
            float upperHalfAngle = Quaternion.Angle(upperBase, upper.rotation);
            Assert.That(
                upperHalfAngle,
                Is.EqualTo(upperFullAngle * 0.5f).Within(1f));
            Assert.That(
                Quaternion.Angle(
                    upper.rotation,
                    Quaternion.Slerp(upperBase, upperFull, 0.5f)),
                Is.LessThan(0.5f));

            // The knee joint follows: part way, neither untouched nor fully
            // solved.
            float lowerHalfAngle = Quaternion.Angle(lowerBase, lower.rotation);
            Assert.That(
                lowerHalfAngle,
                Is.InRange(lowerFullAngle * 0.25f, lowerFullAngle * 0.75f));

            // And the foot lands between where it started and where the
            // full solve put it.
            float span = Vector3.Distance(tipBase, tipFull);
            Assert.That(Vector3.Distance(tipBase, tip.position), Is.LessThan(span));
            Assert.That(Vector3.Distance(tip.position, tipFull), Is.LessThan(span));
        }

        [Test]
        public void Solve_WritesTipRotationOnlyWhenAsked()
        {
            var target = new Vector3(0f, -0.5f, 0.33f);
            Quaternion tipRotation = Quaternion.Euler(0f, 90f, 0f);
            Quaternion tipLocalBefore = tip.localRotation;

            LimbTwoBoneIk.Solve(
                upper,
                lower,
                tip,
                target,
                tipRotation,
                ForwardHint,
                1f,
                LimbTwoBoneIk.DefaultReachFraction,
                false);
            Assert.That(
                Vector3.Distance(tip.position, target),
                Is.LessThan(0.001f));
            Assert.That(
                Quaternion.Angle(tip.localRotation, tipLocalBefore),
                Is.LessThan(0.001f));

            ResetChain();
            LimbTwoBoneIk.Solve(
                upper,
                lower,
                tip,
                target,
                tipRotation,
                ForwardHint,
                1f,
                LimbTwoBoneIk.DefaultReachFraction,
                true);
            Assert.That(
                Quaternion.Angle(tip.rotation, tipRotation),
                Is.LessThan(0.01f));

            // The six-argument seated-arm contract always writes the tip.
            ResetChain();
            LimbTwoBoneIk.Solve(
                upper,
                lower,
                tip,
                target,
                tipRotation,
                ForwardHint);
            Assert.That(
                Quaternion.Angle(tip.rotation, tipRotation),
                Is.LessThan(0.01f));
        }

        [Test]
        public void SeatedArmIk_WrapperMatchesCoreSolve()
        {
            var otherRoot = new GameObject("Limb IK Test Root (seated arm)");
            try
            {
                BuildChain(
                    otherRoot.transform,
                    out Transform otherUpper,
                    out Transform otherLower,
                    out Transform otherTip);
                var target = new Vector3(0.12f, -0.55f, 0.3f);
                var hint = new Vector3(0.3f, -0.4f, 0.8f);
                Quaternion tipRotation = Quaternion.Euler(20f, 45f, -10f);

                SeatedArmIk.SolveTwoBone(
                    otherUpper,
                    otherLower,
                    otherTip,
                    target,
                    tipRotation,
                    hint);
                LimbTwoBoneIk.Solve(
                    upper,
                    lower,
                    tip,
                    target,
                    tipRotation,
                    hint);

                Assert.That(
                    Quaternion.Angle(upper.rotation, otherUpper.rotation),
                    Is.LessThan(0.001f));
                Assert.That(
                    Quaternion.Angle(lower.rotation, otherLower.rotation),
                    Is.LessThan(0.001f));
                Assert.That(
                    Quaternion.Angle(tip.rotation, otherTip.rotation),
                    Is.LessThan(0.001f));
                Assert.That(
                    Vector3.Distance(tip.position, otherTip.position),
                    Is.LessThan(0.00001f));
                Assert.That(
                    Vector3.Distance(lower.position, otherLower.position),
                    Is.LessThan(0.00001f));
                Assert.That(
                    Quaternion.Angle(tip.rotation, tipRotation),
                    Is.LessThan(0.01f));
            }
            finally
            {
                Object.DestroyImmediate(otherRoot);
            }
        }

        [Test]
        public void Solve_CorrectsAKneeBentTheWrongWay()
        {
            // A knee folded 90 degrees BACKWARD — the thigh swung back,
            // the shin swung forward under it — with the foot straight
            // below the hip. A positive turn about right carries a
            // hanging bone backward.
            upper.rotation = Quaternion.AngleAxis(45f, Vector3.right);
            lower.rotation = upper.rotation *
                             Quaternion.AngleAxis(-90f, Vector3.right);
            Vector3 target = tip.position;
            Assert.That(target.z, Is.EqualTo(0f).Within(0.001f));
            Assert.That(KneeSide(target, ForwardHint), Is.LessThan(-0.9f));

            LimbTwoBoneIk.Solve(
                upper,
                lower,
                tip,
                target,
                Quaternion.identity,
                ForwardHint,
                1f,
                float.PositiveInfinity,
                false);

            // The foot stays where it was; the knee is in front of the
            // hip-to-foot line; and both bones simply swung in the bend
            // plane — the mirror image of the start, no roll about the
            // leg's own length that would twist the mesh.
            Assert.That(Vector3.Distance(tip.position, target), Is.LessThan(0.001f));
            Assert.That(KneeSide(target, ForwardHint), Is.GreaterThan(0.9f));
            Assert.That(
                Quaternion.Angle(
                    upper.rotation,
                    Quaternion.AngleAxis(-45f, Vector3.right)),
                Is.LessThan(1f));
            Assert.That(
                Quaternion.Angle(
                    lower.rotation,
                    Quaternion.AngleAxis(45f, Vector3.right)),
                Is.LessThan(1f));
        }

        [Test]
        public void Solve_KeepsARightSidedKneeUntouched()
        {
            upper.rotation = Quaternion.AngleAxis(-45f, Vector3.right);
            lower.rotation = upper.rotation *
                             Quaternion.AngleAxis(90f, Vector3.right);
            Vector3 target = tip.position;
            Quaternion upperBefore = upper.rotation;
            Quaternion lowerBefore = lower.rotation;
            Assert.That(KneeSide(target, ForwardHint), Is.GreaterThan(0.9f));

            LimbTwoBoneIk.Solve(
                upper,
                lower,
                tip,
                target,
                Quaternion.identity,
                ForwardHint,
                1f,
                float.PositiveInfinity,
                false);

            Assert.That(Vector3.Distance(tip.position, target), Is.LessThan(0.001f));
            Assert.That(Quaternion.Angle(upperBefore, upper.rotation), Is.LessThan(0.5f));
            Assert.That(Quaternion.Angle(lowerBefore, lower.rotation), Is.LessThan(0.5f));
        }

        [Test]
        public void Solve_NearlyStraightLegStillFollowsTheHint()
        {
            // Six millimetres of knee behind the line: past the side
            // epsilon, well under the hint's full-bend distance. The old
            // hint alone would have left it there.
            const float degrees = 0.764f;
            upper.rotation = Quaternion.AngleAxis(degrees, Vector3.right);
            lower.rotation = upper.rotation *
                             Quaternion.AngleAxis(-2f * degrees, Vector3.right);
            Vector3 target = tip.position;
            Assert.That(lower.position.z, Is.EqualTo(-0.006f).Within(0.0005f));

            LimbTwoBoneIk.Solve(
                upper,
                lower,
                tip,
                target,
                Quaternion.identity,
                ForwardHint,
                1f,
                float.PositiveInfinity,
                false);

            Assert.That(Vector3.Distance(tip.position, target), Is.LessThan(0.001f));
            // Six millimetres in front now, still straight below the hip.
            Assert.That(lower.position.z, Is.GreaterThan(0.004f));
            Assert.That(Mathf.Abs(lower.position.x), Is.LessThan(0.0005f));
            Assert.That(
                Quaternion.Angle(
                    upper.rotation,
                    Quaternion.AngleAxis(-degrees, Vector3.right)),
                Is.LessThan(0.2f));
        }

        [Test]
        public void Solve_IgnoresMissingJoints()
        {
            Quaternion upperBefore = upper.rotation;
            Quaternion lowerBefore = lower.rotation;

            Assert.DoesNotThrow(
                () => LimbTwoBoneIk.Solve(
                    upper,
                    null,
                    tip,
                    new Vector3(0f, -0.5f, 0.33f),
                    Quaternion.identity,
                    ForwardHint));
            Assert.DoesNotThrow(
                () => LimbTwoBoneIk.Solve(
                    null,
                    lower,
                    null,
                    new Vector3(0f, -0.5f, 0.33f),
                    Quaternion.identity,
                    ForwardHint));
            Assert.That(upper.rotation, Is.EqualTo(upperBefore));
            Assert.That(lower.rotation, Is.EqualTo(lowerBefore));
        }

        [Test]
        public void ChainLength_MeasuresWorld()
        {
            Assert.That(
                LimbTwoBoneIk.ChainLength(upper, lower, tip),
                Is.EqualTo(FullChainLength).Within(0.00001f));

            // Bending the knee does not change how much leg there is.
            lower.rotation = Quaternion.AngleAxis(60f, Vector3.right);
            Assert.That(
                LimbTwoBoneIk.ChainLength(upper, lower, tip),
                Is.EqualTo(FullChainLength).Within(0.00001f));

            // Nor does moving the whole limb.
            root.transform.position = new Vector3(3f, 1f, -2f);
            root.transform.rotation = Quaternion.Euler(10f, 80f, 5f);
            Assert.That(
                LimbTwoBoneIk.ChainLength(upper, lower, tip),
                Is.EqualTo(FullChainLength).Within(0.00001f));

            // An imported rig carries a 100x authoring root with
            // centimetre-scale local offsets: the world metres are what count.
            var scaledRoot = new GameObject("Limb IK Test Root (100x)");
            try
            {
                scaledRoot.transform.localScale = Vector3.one * 100f;
                Transform scaledUpper = CreateJoint(
                    "Upper",
                    scaledRoot.transform,
                    Vector3.zero);
                Transform scaledLower = CreateJoint(
                    "Lower",
                    scaledUpper,
                    new Vector3(0f, -UpperLength / 100f, 0f));
                Transform scaledTip = CreateJoint(
                    "Tip",
                    scaledLower,
                    new Vector3(0f, -LowerLength / 100f, 0f));
                Assert.That(
                    LimbTwoBoneIk.ChainLength(
                        scaledUpper,
                        scaledLower,
                        scaledTip),
                    Is.EqualTo(FullChainLength).Within(0.0001f));
            }
            finally
            {
                Object.DestroyImmediate(scaledRoot);
            }

            Assert.That(LimbTwoBoneIk.ChainLength(null, lower, tip), Is.Zero);
            Assert.That(LimbTwoBoneIk.ChainLength(upper, lower, null), Is.Zero);
        }

        private static void BuildChain(
            Transform parent,
            out Transform chainUpper,
            out Transform chainLower,
            out Transform chainTip)
        {
            chainUpper = CreateJoint("Upper", parent, Vector3.zero);
            chainLower = CreateJoint(
                "Lower",
                chainUpper,
                new Vector3(0f, -UpperLength, 0f));
            chainTip = CreateJoint(
                "Tip",
                chainLower,
                new Vector3(0f, -LowerLength, 0f));
        }

        private static Transform CreateJoint(
            string name,
            Transform parent,
            Vector3 localPosition)
        {
            var joint = new GameObject(name).transform;
            joint.SetParent(parent, false);
            joint.localPosition = localPosition;
            joint.localRotation = Quaternion.identity;
            return joint;
        }

        private void ResetChain()
        {
            upper.localRotation = Quaternion.identity;
            lower.localRotation = Quaternion.identity;
            tip.localRotation = Quaternion.identity;
        }

        /// <summary>
        /// Cosine between the knee's offset from the hip-to-target axis and
        /// the hint's: +1 when the knee points at the hint, -1 away from it.
        /// </summary>
        private float KneeSide(Vector3 target, Vector3 hint)
        {
            Vector3 axis = (target - upper.position).normalized;
            Vector3 knee = Vector3.ProjectOnPlane(
                lower.position - upper.position,
                axis);
            Vector3 desired = Vector3.ProjectOnPlane(
                hint - upper.position,
                axis);
            Assert.That(knee.magnitude, Is.GreaterThan(0.01f));
            Assert.That(desired.magnitude, Is.GreaterThan(0.01f));
            return Vector3.Dot(knee.normalized, desired.normalized);
        }
    }
}

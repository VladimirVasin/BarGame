using NUnit.Framework;
using UnityEngine;

namespace BarPromenade.Tests.EditMode
{
    /// <summary>
    /// The pure rules of a hand going out to a wall: which hand, where the
    /// palm lands, how far the arm may straighten, the hold/release
    /// hysteresis and the weight blend.
    /// </summary>
    public sealed class PlayerWallContactRulesTests
    {
        private const float Tolerance = 0.00001f;
        private const float ShoulderHeight = 1.4f;

        [Test]
        public void TryChooseHand_UsesSideOfNormal()
        {
            Vector3 heroRight = Vector3.right;

            // A wall on his right has a normal pointing back at him, against his right.
            Assert.That(
                PlayerWallContactRules.TryChooseHand(Vector3.left, heroRight, out bool rightHand),
                Is.True);
            Assert.That(rightHand, Is.True);

            // A wall on his left: the left hand.
            Assert.That(
                PlayerWallContactRules.TryChooseHand(Vector3.right, heroRight, out bool leftWall),
                Is.True);
            Assert.That(leftWall, Is.False);

            // A wall straight ahead or behind gives neither hand a side.
            Assert.That(
                PlayerWallContactRules.TryChooseHand(Vector3.forward, heroRight, out _),
                Is.False);
            Assert.That(
                PlayerWallContactRules.TryChooseHand(Vector3.back, heroRight, out _),
                Is.False);

            // A grazing normal (under a tenth along his right) is no side either;
            // an oblique one is.
            Assert.That(
                PlayerWallContactRules.TryChooseHand(
                    new Vector3(-0.05f, 0f, 0.9987f),
                    heroRight,
                    out _),
                Is.False);
            Assert.That(
                PlayerWallContactRules.TryChooseHand(
                    new Vector3(-0.3f, 0f, 0.954f),
                    heroRight,
                    out bool oblique),
                Is.True);
            Assert.That(oblique, Is.True);

            // The hero's own right is what counts, not the world's.
            Assert.That(
                PlayerWallContactRules.TryChooseHand(Vector3.forward, Vector3.back, out bool turned),
                Is.True);
            Assert.That(turned, Is.True);
        }

        [Test]
        public void PalmTarget_IsOffWallAtShoulderMinusTen()
        {
            Vector3 palm = PlayerWallContactRules.PalmTarget(
                new Vector3(1f, 1.2f, 0f),
                Vector3.left,
                Vector3.forward,
                ShoulderHeight);

            // 2 cm off the wall, 10 cm below the shoulder, 10 cm ahead along the wall.
            Assert.That(palm.x, Is.EqualTo(0.98f).Within(Tolerance));
            Assert.That(palm.y, Is.EqualTo(1.3f).Within(Tolerance));
            Assert.That(palm.z, Is.EqualTo(0.1f).Within(Tolerance));

            // The contact's own height is ignored: the shoulder sets it.
            Vector3 low = PlayerWallContactRules.PalmTarget(
                new Vector3(1f, 0.3f, 0f),
                Vector3.left,
                Vector3.forward,
                ShoulderHeight);
            Assert.That(low.y, Is.EqualTo(1.3f).Within(Tolerance));
            Vector3 tall = PlayerWallContactRules.PalmTarget(
                new Vector3(1f, 1.2f, 0f),
                Vector3.left,
                Vector3.forward,
                1.6f);
            Assert.That(tall.y, Is.EqualTo(1.5f).Within(Tolerance));

            // The offset follows the normal, whichever way the wall faces.
            Vector3 mirrored = PlayerWallContactRules.PalmTarget(
                new Vector3(-1f, 1.2f, 0f),
                Vector3.right,
                Vector3.forward,
                ShoulderHeight);
            Assert.That(mirrored.x, Is.EqualTo(-0.98f).Within(Tolerance));
            Assert.That(mirrored.y, Is.EqualTo(1.3f).Within(Tolerance));
            Assert.That(mirrored.z, Is.EqualTo(0.1f).Within(Tolerance));

            // An unnormalised normal is normalised before it is used.
            Vector3 scaled = PlayerWallContactRules.PalmTarget(
                new Vector3(1f, 1.2f, 0f),
                new Vector3(-5f, 0f, 0f),
                Vector3.forward,
                ShoulderHeight);
            Assert.That(scaled.x, Is.EqualTo(0.98f).Within(Tolerance));
            Assert.That(scaled.z, Is.EqualTo(0.1f).Within(Tolerance));
        }

        [Test]
        public void PalmTarget_SlidesAlongTheWall()
        {
            Vector3 contact = new Vector3(1f, 1.2f, 0f);

            // Walking diagonally into the wall: the slide is his forward
            // projected onto the wall plane, the full 10 cm along it and
            // none into the wall.
            Vector3 diagonal = PlayerWallContactRules.PalmTarget(
                contact,
                Vector3.left,
                new Vector3(1f, 0f, 1f).normalized,
                ShoulderHeight);
            Assert.That(diagonal.x, Is.EqualTo(0.98f).Within(Tolerance));
            Assert.That(diagonal.y, Is.EqualTo(1.3f).Within(Tolerance));
            Assert.That(diagonal.z, Is.EqualTo(0.1f).Within(Tolerance));

            // Walking the other way along the same wall slides the palm back.
            Vector3 reversed = PlayerWallContactRules.PalmTarget(
                contact,
                Vector3.left,
                new Vector3(1f, 0f, -1f).normalized,
                ShoulderHeight);
            Assert.That(reversed.x, Is.EqualTo(0.98f).Within(Tolerance));
            Assert.That(reversed.z, Is.EqualTo(-0.1f).Within(Tolerance));

            // A forward with a vertical component slides horizontally only.
            Vector3 climbing = PlayerWallContactRules.PalmTarget(
                contact,
                Vector3.left,
                new Vector3(0f, 1f, 1f).normalized,
                ShoulderHeight);
            Assert.That(climbing.y, Is.EqualTo(1.3f).Within(Tolerance));
            Assert.That(climbing.z, Is.EqualTo(0.1f).Within(Tolerance));

            // Facing straight into the wall there is nothing to slide along:
            // the palm lands at the contact.
            Vector3 headOn = PlayerWallContactRules.PalmTarget(
                contact,
                Vector3.left,
                Vector3.right,
                ShoulderHeight);
            Assert.That(headOn.x, Is.EqualTo(0.98f).Within(Tolerance));
            Assert.That(headOn.z, Is.EqualTo(0f).Within(Tolerance));

            // A wall across his path (normal against his forward) slides along x.
            Vector3 across = PlayerWallContactRules.PalmTarget(
                new Vector3(0f, 1.2f, 2f),
                Vector3.back,
                new Vector3(1f, 0f, 1f).normalized,
                ShoulderHeight);
            Assert.That(across.x, Is.EqualTo(0.1f).Within(Tolerance));
            Assert.That(across.y, Is.EqualTo(1.3f).Within(Tolerance));
            Assert.That(across.z, Is.EqualTo(1.98f).Within(Tolerance));
        }

        [Test]
        public void ClampToReach_SlidesTowardShoulder()
        {
            Vector3 shoulder = new Vector3(0f, ShoulderHeight, 0f);
            const float armLength = 0.6f;

            // A metre away with a 60 cm arm: 95 % of the arm along the same ray.
            Vector3 far = new Vector3(1f, ShoulderHeight, 0f);
            Vector3 clamped = PlayerWallContactRules.ClampToReach(
                shoulder,
                far,
                armLength);
            Assert.That(clamped.x, Is.EqualTo(0.57f).Within(Tolerance));
            Assert.That(clamped.y, Is.EqualTo(ShoulderHeight).Within(Tolerance));
            Assert.That(clamped.z, Is.EqualTo(0f).Within(Tolerance));
            Assert.That(
                Vector3.Distance(shoulder, clamped),
                Is.EqualTo(armLength * PlayerWallContactRules.ReachFraction)
                    .Within(Tolerance));

            // Within reach the target is untouched.
            Vector3 near = new Vector3(0.3f, 1.2f, 0.2f);
            Vector3 kept = PlayerWallContactRules.ClampToReach(
                shoulder,
                near,
                armLength);
            Assert.That(kept.x, Is.EqualTo(near.x));
            Assert.That(kept.y, Is.EqualTo(near.y));
            Assert.That(kept.z, Is.EqualTo(near.z));

            // The clamp stays on the shoulder-to-target ray in three dimensions.
            Vector3 lowAndAway = new Vector3(0.6f, 0.6f, 0.6f);
            Vector3 onRay = PlayerWallContactRules.ClampToReach(
                shoulder,
                lowAndAway,
                armLength);
            Vector3 expected =
                shoulder + (lowAndAway - shoulder).normalized * 0.57f;
            Assert.That(Vector3.Distance(onRay, expected), Is.LessThan(Tolerance));
            Assert.That(
                Vector3.Distance(shoulder, onRay),
                Is.EqualTo(0.57f).Within(Tolerance));
        }

        [Test]
        public void ShouldHold_Hysteresis()
        {
            // The constants the hysteresis is built from.
            Assert.That(PlayerWallContactRules.HoldInstability, Is.EqualTo(0.25f));
            Assert.That(PlayerWallContactRules.ReleaseInstability, Is.EqualTo(0.12f));
            Assert.That(PlayerWallContactRules.ReleaseDelaySeconds, Is.EqualTo(0.4f));
            Assert.That(PlayerWallContactRules.HoldDistance, Is.EqualTo(0.55f));
            Assert.That(PlayerWallContactRules.ReleaseDistance, Is.EqualTo(0.6f));
            Assert.That(PlayerWallContactRules.MaximumFacingDot, Is.EqualTo(0.7f));
            Assert.That(
                PlayerWallContactRules.ReleaseDistance,
                Is.GreaterThan(PlayerWallContactRules.HoldDistance));
            Assert.That(
                PlayerWallContactRules.ReleaseInstability,
                Is.LessThan(PlayerWallContactRules.HoldInstability));

            // --- Not holding: the hand goes out when he tips toward a close wall...
            Assert.That(Hold(false, true, 0.3f, 0.5f, 0f, false, 0f), Is.True);
            // ...or has already bumped it, however steady he is.
            Assert.That(Hold(false, true, 0f, 0.5f, 0f, true, 0f), Is.True);
            // Instability at the threshold is not enough; it has to exceed it.
            Assert.That(
                Hold(false, true, PlayerWallContactRules.HoldInstability, 0.5f, 0f, false, 0f),
                Is.False);
            Assert.That(Hold(false, true, 0.26f, 0.5f, 0f, false, 0f), Is.True);
            // The hold distance is inclusive; a centimetre more is too far, even bumping.
            Assert.That(
                Hold(false, true, 0.3f, PlayerWallContactRules.HoldDistance, 0f, false, 0f),
                Is.True);
            Assert.That(Hold(false, true, 0.3f, 0.56f, 0f, false, 0f), Is.False);
            Assert.That(Hold(false, true, 0f, 0.56f, 0f, true, 0f), Is.False);
            // No wall within reach, or a wall behind him, never takes the hand.
            Assert.That(Hold(false, false, 1f, 0.1f, 0f, true, 0f), Is.False);
            Assert.That(
                Hold(false, true, 1f, 0.1f, PlayerWallContactRules.MaximumFacingDot, true, 0f),
                Is.False);
            Assert.That(Hold(false, true, 1f, 0.1f, 0.95f, true, 0f), Is.False);
            Assert.That(Hold(false, true, 0.3f, 0.5f, 0.69f, false, 0f), Is.True);
            Assert.That(Hold(false, true, 0.3f, 0.5f, -1f, false, 0f), Is.True);
            // Steadiness so far does not matter for taking hold.
            Assert.That(Hold(false, true, 0.3f, 0.5f, 0f, false, 10f), Is.True);

            // --- Holding: keeps past the hold distance up to the release distance.
            Assert.That(Hold(true, true, 0.3f, 0.58f, 0f, false, 0f), Is.True);
            Assert.That(
                Hold(true, true, 0.3f, PlayerWallContactRules.ReleaseDistance, 0f, false, 0f),
                Is.True);
            Assert.That(Hold(true, true, 0.3f, 0.61f, 0f, false, 0f), Is.False);
            // Keeps while unsteady, however long.
            Assert.That(Hold(true, true, 0.3f, 0.5f, 0f, false, 10f), Is.True);
            Assert.That(Hold(true, true, 0.2f, 0.5f, 0f, false, 10f), Is.True);
            Assert.That(
                Hold(true, true, PlayerWallContactRules.ReleaseInstability, 0.5f, 0f, false, 10f),
                Is.True);
            // Lets go only once he has been steady for the delay.
            Assert.That(Hold(true, true, 0.11f, 0.5f, 0f, false, 0.39f), Is.True);
            Assert.That(
                Hold(true, true, 0.11f, 0.5f, 0f, false, PlayerWallContactRules.ReleaseDelaySeconds),
                Is.False);
            Assert.That(Hold(true, true, 0f, 0.5f, 0f, false, 1f), Is.False);
            // A steady hero lets go even while still brushing the wall.
            Assert.That(Hold(true, true, 0f, 0.5f, 0f, true, 1f), Is.False);
            // The wall gone, or turned behind him, releases at once.
            Assert.That(Hold(true, false, 1f, 0.1f, 0f, true, 0f), Is.False);
            Assert.That(
                Hold(true, true, 1f, 0.1f, PlayerWallContactRules.MaximumFacingDot, true, 0f),
                Is.False);
            Assert.That(Hold(true, true, 1f, 0.1f, 0.95f, true, 0f), Is.False);

            // --- The band between the two thresholds is what makes it hysteresis:
            // the same state keeps a hand that is already there and refuses one that is not.
            Assert.That(Hold(false, true, 0.2f, 0.58f, 0f, false, 0f), Is.False);
            Assert.That(Hold(true, true, 0.2f, 0.58f, 0f, false, 10f), Is.True);
        }

        [Test]
        public void AdvanceWeight_InFastOutSlow()
        {
            Assert.That(PlayerWallContactRules.WeightInSeconds, Is.EqualTo(0.12f));
            Assert.That(PlayerWallContactRules.WeightOutSeconds, Is.EqualTo(0.35f));

            // Single steps are linear in time toward the target.
            Assert.That(
                PlayerWallContactRules.AdvanceWeight(0f, true, 0.06f),
                Is.EqualTo(0.5f).Within(Tolerance));
            Assert.That(
                PlayerWallContactRules.AdvanceWeight(1f, false, 0.175f),
                Is.EqualTo(0.5f).Within(Tolerance));
            Assert.That(
                PlayerWallContactRules.AdvanceWeight(
                    0f,
                    true,
                    PlayerWallContactRules.WeightInSeconds),
                Is.EqualTo(1f).Within(Tolerance));
            Assert.That(
                PlayerWallContactRules.AdvanceWeight(
                    1f,
                    false,
                    PlayerWallContactRules.WeightOutSeconds),
                Is.EqualTo(0f).Within(Tolerance));

            // Never overshoots, holds at the target, and a negative delta does nothing.
            Assert.That(PlayerWallContactRules.AdvanceWeight(0.5f, true, 10f), Is.EqualTo(1f));
            Assert.That(PlayerWallContactRules.AdvanceWeight(0.5f, false, 10f), Is.EqualTo(0f));
            Assert.That(PlayerWallContactRules.AdvanceWeight(1f, true, 0.01f), Is.EqualTo(1f));
            Assert.That(PlayerWallContactRules.AdvanceWeight(0f, false, 0.01f), Is.EqualTo(0f));
            Assert.That(PlayerWallContactRules.AdvanceWeight(0.5f, true, -1f), Is.EqualTo(0.5f));
            Assert.That(PlayerWallContactRules.AdvanceWeight(0.5f, true, 0f), Is.EqualTo(0.5f));

            // Stepped at the model's rate the hand takes about 0.12 s to
            // arrive and about 0.35 s to leave.
            const float deltaTime = PlayerBalanceRules.FixedStep;
            float weight = 0f;
            float inSeconds = 0f;
            while (weight < 1f && inSeconds < 2f)
            {
                weight = PlayerWallContactRules.AdvanceWeight(weight, true, deltaTime);
                inSeconds += deltaTime;
            }

            Assert.That(weight, Is.EqualTo(1f));
            Assert.That(
                inSeconds,
                Is.EqualTo(PlayerWallContactRules.WeightInSeconds)
                    .Within(deltaTime * 1.5f));

            float outSeconds = 0f;
            while (weight > 0f && outSeconds < 2f)
            {
                weight = PlayerWallContactRules.AdvanceWeight(weight, false, deltaTime);
                outSeconds += deltaTime;
            }

            Assert.That(weight, Is.EqualTo(0f));
            Assert.That(
                outSeconds,
                Is.EqualTo(PlayerWallContactRules.WeightOutSeconds)
                    .Within(deltaTime * 1.5f));
            Assert.That(outSeconds, Is.GreaterThan(inSeconds * 2f));

            // Half-way through the blend-in, letting go takes proportionally less time.
            float halfWeight = PlayerWallContactRules.AdvanceWeight(0f, true, 0.06f);
            Assert.That(
                PlayerWallContactRules.AdvanceWeight(halfWeight, false, 0.175f),
                Is.EqualTo(0f).Within(Tolerance));
        }

        private static bool Hold(
            bool holding,
            bool wallWithinReach,
            float instability,
            float wallDistance,
            float facingDot,
            bool sideContact,
            float steadySeconds)
        {
            return PlayerWallContactRules.ShouldHold(
                holding,
                wallWithinReach,
                instability,
                wallDistance,
                facingDot,
                sideContact,
                steadySeconds);
        }
    }
}

using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;

namespace BarPromenade.Tests.EditMode
{
    /// <summary>
    /// The pure formulas of the hero's continuous balance model: the
    /// support polygon under his boots, the tuning table per intoxication,
    /// the inverted-pendulum capture point, when and where a step goes,
    /// what a kerb does to a swinging boot, and the episode seed.
    /// Hero frame throughout: x right, y forward, metres.
    /// </summary>
    public sealed class PlayerBalanceRulesTests
    {
        private const float Tolerance = 0.00001f;

        private static PlayerBalanceSettings Sober =>
            PlayerBalanceSettings.FromIntoxication(0f);

        private static PlayerBalanceSettings BlindDrunk =>
            PlayerBalanceSettings.FromIntoxication(1f);

        /// <summary>
        /// Two boots ten centimetres either side of the centre line, toes
        /// level: the polygon every test below stands on.
        /// </summary>
        private static BalanceSupportPolygon StancePolygon()
        {
            return BalanceSupportPolygon.FromFeet(
                new Vector2(-0.1f, 0f),
                new Vector2(0.1f, 0f),
                Sober);
        }

        private static void AssertSamePolygon(
            BalanceSupportPolygon actual,
            BalanceSupportPolygon expected)
        {
            Assert.That(actual.MinX, Is.EqualTo(expected.MinX));
            Assert.That(actual.MaxX, Is.EqualTo(expected.MaxX));
            Assert.That(actual.MinForward, Is.EqualTo(expected.MinForward));
            Assert.That(actual.MaxForward, Is.EqualTo(expected.MaxForward));
        }

        [Test]
        public void SupportPolygon_FromFeet_AddsPads()
        {
            BalanceSupportPolygon polygon = StancePolygon();

            // 5 cm of sole either side, 6 cm of heel behind, 12 cm of toe in front.
            Assert.That(polygon.MinX, Is.EqualTo(-0.15f).Within(Tolerance));
            Assert.That(polygon.MaxX, Is.EqualTo(0.15f).Within(Tolerance));
            Assert.That(polygon.MinForward, Is.EqualTo(-0.06f).Within(Tolerance));
            Assert.That(polygon.MaxForward, Is.EqualTo(0.12f).Within(Tolerance));
            Assert.That(polygon.HalfWidth, Is.EqualTo(0.15f).Within(Tolerance));

            // Which boot is called left does not matter.
            BalanceSupportPolygon swapped = BalanceSupportPolygon.FromFeet(
                new Vector2(0.1f, 0f),
                new Vector2(-0.1f, 0f),
                Sober);
            AssertSamePolygon(swapped, polygon);

            // The pads are the same at every intoxication.
            BalanceSupportPolygon drunk = BalanceSupportPolygon.FromFeet(
                new Vector2(-0.1f, 0f),
                new Vector2(0.1f, 0f),
                BlindDrunk);
            AssertSamePolygon(drunk, polygon);

            // One boot alone is the same rectangle collapsed onto it.
            BalanceSupportPolygon single = BalanceSupportPolygon.FromFoot(
                new Vector2(0.1f, 0.2f),
                Sober);
            Assert.That(single.MinX, Is.EqualTo(0.05f).Within(Tolerance));
            Assert.That(single.MaxX, Is.EqualTo(0.15f).Within(Tolerance));
            Assert.That(single.MinForward, Is.EqualTo(0.14f).Within(Tolerance));
            Assert.That(single.MaxForward, Is.EqualTo(0.32f).Within(Tolerance));
            Assert.That(single.HalfWidth, Is.EqualTo(0.05f).Within(Tolerance));

            // A trailing boot stretches the polygon back to its heel.
            BalanceSupportPolygon staggered = BalanceSupportPolygon.FromFeet(
                new Vector2(-0.1f, -0.2f),
                new Vector2(0.1f, 0f),
                Sober);
            Assert.That(staggered.MinForward, Is.EqualTo(-0.26f).Within(Tolerance));
            Assert.That(staggered.MaxForward, Is.EqualTo(0.12f).Within(Tolerance));
        }

        [Test]
        public void SupportPolygon_Excursion_IsZeroInsideAndDistanceOutside()
        {
            BalanceSupportPolygon polygon = StancePolygon();

            Vector2 inside = new Vector2(0.1f, 0.05f);
            Assert.That(polygon.Contains(inside), Is.True);
            Assert.That(polygon.Excursion(inside), Is.Zero);
            Vector2 clampedInside = polygon.Clamp(inside);
            Assert.That(clampedInside.x, Is.EqualTo(inside.x));
            Assert.That(clampedInside.y, Is.EqualTo(inside.y));

            // The edge itself counts as inside.
            Vector2 edge = new Vector2(polygon.MaxX, polygon.MaxForward);
            Assert.That(polygon.Contains(edge), Is.True);
            Assert.That(polygon.Excursion(edge), Is.Zero);

            // Out to the side: the distance to the nearest edge.
            Vector2 lateral = new Vector2(0.35f, 0f);
            Assert.That(polygon.Contains(lateral), Is.False);
            Assert.That(
                polygon.Excursion(lateral),
                Is.EqualTo(0.2f).Within(Tolerance));

            Vector2 behind = new Vector2(0f, -0.26f);
            Assert.That(polygon.Contains(behind), Is.False);
            Assert.That(
                polygon.Excursion(behind),
                Is.EqualTo(0.2f).Within(Tolerance));

            // Past a corner: the Euclidean distance to that corner (3-4-5).
            Vector2 diagonal = new Vector2(0.45f, 0.52f);
            Assert.That(polygon.Contains(diagonal), Is.False);
            Vector2 corner = polygon.Clamp(diagonal);
            Assert.That(corner.x, Is.EqualTo(0.15f).Within(Tolerance));
            Assert.That(corner.y, Is.EqualTo(0.12f).Within(Tolerance));
            Assert.That(
                polygon.Excursion(diagonal),
                Is.EqualTo(0.5f).Within(Tolerance));
        }

        [Test]
        public void SupportPolygon_ExtendedToward_GrowsOneSideOnly()
        {
            BalanceSupportPolygon polygon = StancePolygon();

            // A wall on the right: only MaxX moves.
            BalanceSupportPolygon right =
                polygon.ExtendedToward(Vector2.right, 0.35f);
            Assert.That(right.MinX, Is.EqualTo(polygon.MinX));
            Assert.That(right.MaxX, Is.EqualTo(0.5f).Within(Tolerance));
            Assert.That(right.MinForward, Is.EqualTo(polygon.MinForward));
            Assert.That(right.MaxForward, Is.EqualTo(polygon.MaxForward));

            // The direction is normalised, so its length is not a second distance.
            BalanceSupportPolygon left =
                polygon.ExtendedToward(new Vector2(-2f, 0f), 0.35f);
            Assert.That(left.MinX, Is.EqualTo(-0.5f).Within(Tolerance));
            Assert.That(left.MaxX, Is.EqualTo(polygon.MaxX));
            Assert.That(left.MinForward, Is.EqualTo(polygon.MinForward));
            Assert.That(left.MaxForward, Is.EqualTo(polygon.MaxForward));

            BalanceSupportPolygon forward =
                polygon.ExtendedToward(Vector2.up, 0.35f);
            Assert.That(forward.MinX, Is.EqualTo(polygon.MinX));
            Assert.That(forward.MaxX, Is.EqualTo(polygon.MaxX));
            Assert.That(forward.MinForward, Is.EqualTo(polygon.MinForward));
            Assert.That(
                forward.MaxForward,
                Is.EqualTo(0.47f).Within(Tolerance));

            // A diagonal grows the two sides it points at, by its components.
            BalanceSupportPolygon diagonal = polygon.ExtendedToward(
                new Vector2(1f, -1f),
                0.35f);
            float component = 0.35f / Mathf.Sqrt(2f);
            Assert.That(diagonal.MinX, Is.EqualTo(polygon.MinX));
            Assert.That(
                diagonal.MaxX,
                Is.EqualTo(0.15f + component).Within(Tolerance));
            Assert.That(
                diagonal.MinForward,
                Is.EqualTo(-0.06f - component).Within(Tolerance));
            Assert.That(diagonal.MaxForward, Is.EqualTo(polygon.MaxForward));

            // No distance, a negative distance or no direction leaves it alone.
            AssertSamePolygon(polygon.ExtendedToward(Vector2.right, 0f), polygon);
            AssertSamePolygon(polygon.ExtendedToward(Vector2.right, -0.2f), polygon);
            AssertSamePolygon(polygon.ExtendedToward(Vector2.zero, 0.35f), polygon);
        }

        [Test]
        public void Settings_SoberIsInert()
        {
            PlayerBalanceSettings sober = Sober;

            Assert.That(sober.Intoxication, Is.Zero);
            Assert.That(sober.NoiseAmplitude, Is.Zero);
            Assert.That(sober.InputCopShift, Is.Zero);
            Assert.That(sober.SlopeBias, Is.Zero);
            Assert.That(sober.HeadingWeaveDegrees, Is.Zero);
            Assert.That(
                sober.ComHeight,
                Is.EqualTo(PlayerBalanceSettings.DefaultComHeight));

            // Damping is still there: it is what keeps a sober hero still.
            Assert.That(sober.CopDamping, Is.GreaterThan(0f));
            Assert.That(sober.MaximumStepReach, Is.GreaterThan(0f));

            // Below zero clamps to sober.
            PlayerBalanceSettings negative =
                PlayerBalanceSettings.FromIntoxication(-0.5f);
            Assert.That(negative.Intoxication, Is.Zero);
            Assert.That(negative.NoiseAmplitude, Is.Zero);
            Assert.That(negative.InputCopShift, Is.Zero);
            Assert.That(negative.SlopeBias, Is.Zero);
            Assert.That(negative.HeadingWeaveDegrees, Is.Zero);

            // And so does the profile of a hero who has had nothing.
            PlayerBalanceSettings profile = PlayerBalanceSettings.FromProfile(
                IntoxicationStageRules.Evaluate(0));
            Assert.That(profile.Intoxication, Is.Zero);
            Assert.That(profile.NoiseAmplitude, Is.Zero);
            Assert.That(profile.InputCopShift, Is.Zero);
            Assert.That(profile.SlopeBias, Is.Zero);
            Assert.That(profile.HeadingWeaveDegrees, Is.Zero);
        }

        [Test]
        public void Settings_GrowWithIntoxication()
        {
            float[] levels = { 0f, 0.25f, 0.5f, 0.75f, 1f };
            PlayerBalanceSettings previous =
                PlayerBalanceSettings.FromIntoxication(levels[0]);

            for (int index = 1; index < levels.Length; index++)
            {
                PlayerBalanceSettings current =
                    PlayerBalanceSettings.FromIntoxication(levels[index]);

                Assert.That(current.Intoxication, Is.EqualTo(levels[index]));
                Assert.That(
                    current.NoiseAmplitude,
                    Is.GreaterThan(previous.NoiseAmplitude));
                Assert.That(
                    current.ReactionDelay,
                    Is.GreaterThan(previous.ReactionDelay));
                Assert.That(
                    current.StepDuration,
                    Is.GreaterThan(previous.StepDuration));
                Assert.That(
                    current.MaximumStepReach,
                    Is.LessThan(previous.MaximumStepReach));
                Assert.That(
                    current.CopDamping,
                    Is.LessThan(previous.CopDamping));
                Assert.That(
                    current.InputCopShift,
                    Is.GreaterThan(previous.InputCopShift));
                Assert.That(
                    current.SlopeBias,
                    Is.GreaterThan(previous.SlopeBias));

                previous = current;
            }

            // The ends of the table.
            PlayerBalanceSettings sober = Sober;
            Assert.That(sober.ReactionDelay, Is.EqualTo(0.08f).Within(Tolerance));
            Assert.That(sober.StepDuration, Is.EqualTo(0.24f).Within(Tolerance));
            Assert.That(sober.MaximumStepReach, Is.EqualTo(0.55f).Within(Tolerance));
            Assert.That(sober.CopDamping, Is.EqualTo(5f).Within(Tolerance));

            PlayerBalanceSettings drunk = BlindDrunk;
            Assert.That(drunk.Intoxication, Is.EqualTo(1f));
            Assert.That(drunk.NoiseAmplitude, Is.EqualTo(PlayerBalanceSettings.NoiseAmplitudeAtMaximum).Within(Tolerance));
            Assert.That(drunk.ReactionDelay, Is.EqualTo(0.26f).Within(Tolerance));
            Assert.That(drunk.StepDuration, Is.EqualTo(0.36f).Within(Tolerance));
            Assert.That(drunk.MaximumStepReach, Is.EqualTo(0.38f).Within(Tolerance));
            Assert.That(drunk.CopDamping, Is.EqualTo(1.6f).Within(Tolerance));
            Assert.That(drunk.InputCopShift, Is.EqualTo(0.05f).Within(Tolerance));
            Assert.That(drunk.SlopeBias, Is.EqualTo(0.15f).Within(Tolerance));
            Assert.That(drunk.HeadingWeaveDegrees, Is.EqualTo(3f).Within(Tolerance));

            // The heading weave saturates early: half drunk already weaves the full three degrees.
            Assert.That(
                PlayerBalanceSettings.FromIntoxication(0.5f).HeadingWeaveDegrees,
                Is.EqualTo(3f).Within(Tolerance));
            Assert.That(
                PlayerBalanceSettings.FromIntoxication(0.2f).HeadingWeaveDegrees,
                Is.EqualTo(1.5f).Within(Tolerance));

            // Above one clamps to blind drunk.
            PlayerBalanceSettings beyond =
                PlayerBalanceSettings.FromIntoxication(2f);
            Assert.That(beyond.Intoxication, Is.EqualTo(1f));
            Assert.That(beyond.NoiseAmplitude, Is.EqualTo(drunk.NoiseAmplitude));
            Assert.That(beyond.MaximumStepReach, Is.EqualTo(drunk.MaximumStepReach));
        }

        [Test]
        public void Omega_MatchesGravityOverHeight()
        {
            // sqrt(9.81 / 0.95)
            Assert.That(
                PlayerBalanceRules.Omega(0.95f),
                Is.EqualTo(3.21346f).Within(0.0001f));
            Assert.That(
                PlayerBalanceRules.Omega(PlayerBalanceSettings.DefaultComHeight),
                Is.EqualTo(
                    Mathf.Sqrt(
                        PlayerBalanceSettings.Gravity /
                        PlayerBalanceSettings.DefaultComHeight))
                    .Within(Tolerance));
            Assert.That(
                Sober.Omega,
                Is.EqualTo(PlayerBalanceRules.Omega(Sober.ComHeight)));

            // A taller pendulum is slower.
            Assert.That(
                PlayerBalanceRules.Omega(1.2f),
                Is.LessThan(PlayerBalanceRules.Omega(0.95f)));

            // Below 30 cm the height is clamped, so omega never gets stiffer than sqrt(9.81 / 0.3).
            Assert.That(
                PlayerBalanceRules.Omega(0.1f),
                Is.EqualTo(PlayerBalanceRules.Omega(0.3f)));
            Assert.That(
                PlayerBalanceRules.Omega(0.3f),
                Is.EqualTo(5.71839f).Within(0.0001f));

            // The capture point is the COM plus velocity over omega.
            Vector2 capture = PlayerBalanceRules.CapturePoint(
                new Vector2(0.1f, 0.02f),
                new Vector2(0.3f, -0.06f),
                3f);
            Assert.That(capture.x, Is.EqualTo(0.2f).Within(Tolerance));
            Assert.That(capture.y, Is.EqualTo(0f).Within(Tolerance));

            // At rest the capture point is the COM itself, exactly.
            Vector2 still = PlayerBalanceRules.CapturePoint(
                new Vector2(0.05f, -0.01f),
                Vector2.zero,
                Sober.Omega);
            Assert.That(still.x, Is.EqualTo(0.05f));
            Assert.That(still.y, Is.EqualTo(-0.01f));

            // A faster pendulum brings the capture point closer to the COM.
            Vector2 stiff = PlayerBalanceRules.CapturePoint(
                Vector2.zero,
                new Vector2(0.3f, 0f),
                PlayerBalanceRules.Omega(0.3f));
            Vector2 tall = PlayerBalanceRules.CapturePoint(
                Vector2.zero,
                new Vector2(0.3f, 0f),
                PlayerBalanceRules.Omega(0.95f));
            Assert.That(stiff.x, Is.LessThan(tall.x));
        }

        [Test]
        public void NeedsStep_UsesMargin()
        {
            BalanceSupportPolygon polygon = StancePolygon();
            const float margin = 0.03f;

            Assert.That(
                PlayerBalanceRules.NeedsStep(Vector2.zero, polygon, margin),
                Is.False);

            // Two centimetres out is inside the three centimetre margin; four is not.
            Assert.That(
                PlayerBalanceRules.NeedsStep(new Vector2(0.17f, 0f), polygon, margin),
                Is.False);
            Assert.That(
                PlayerBalanceRules.NeedsStep(new Vector2(0.19f, 0f), polygon, margin),
                Is.True);
            Assert.That(
                PlayerBalanceRules.NeedsStep(new Vector2(-0.19f, 0f), polygon, margin),
                Is.True);
            Assert.That(
                PlayerBalanceRules.NeedsStep(new Vector2(0f, 0.14f), polygon, margin),
                Is.False);
            Assert.That(
                PlayerBalanceRules.NeedsStep(new Vector2(0f, 0.16f), polygon, margin),
                Is.True);
            Assert.That(
                PlayerBalanceRules.NeedsStep(new Vector2(0f, -0.1f), polygon, margin),
                Is.True);

            // With no margin any excursion asks for a step, but the edge itself does not.
            Assert.That(
                PlayerBalanceRules.NeedsStep(new Vector2(0.17f, 0f), polygon, 0f),
                Is.True);
            Assert.That(
                PlayerBalanceRules.NeedsStep(
                    new Vector2(polygon.MaxX, polygon.MaxForward),
                    polygon,
                    0f),
                Is.False);

            // The tuning table keeps three centimetres of margin at every level.
            Assert.That(Sober.CaptureMargin, Is.EqualTo(0.03f));
            Assert.That(BlindDrunk.CaptureMargin, Is.EqualTo(0.03f));
        }

        [Test]
        public void StepSide_LateralWinsOverSagittal()
        {
            BalanceSupportPolygon polygon = StancePolygon();

            // 20 cm out to the right and 10 forward: the right boot goes, whatever the preference.
            Vector2 rightAndForward = new Vector2(0.35f, 0.22f);
            Assert.That(
                PlayerBalanceRules.StepSide(rightAndForward, polygon, FootSide.Left),
                Is.EqualTo(FootSide.Right));
            Assert.That(
                PlayerBalanceRules.StepSide(rightAndForward, polygon, FootSide.Right),
                Is.EqualTo(FootSide.Right));

            // 20 cm out to the left and 10 back: the left boot.
            Vector2 leftAndBack = new Vector2(-0.35f, -0.16f);
            Assert.That(
                PlayerBalanceRules.StepSide(leftAndBack, polygon, FootSide.Right),
                Is.EqualTo(FootSide.Left));
            Assert.That(
                PlayerBalanceRules.StepSide(leftAndBack, polygon, FootSide.Left),
                Is.EqualTo(FootSide.Left));

            // Purely lateral escapes, nothing sagittal at all.
            Assert.That(
                PlayerBalanceRules.StepSide(new Vector2(0.2f, 0f), polygon, FootSide.Left),
                Is.EqualTo(FootSide.Right));
            Assert.That(
                PlayerBalanceRules.StepSide(new Vector2(-0.2f, 0.05f), polygon, FootSide.Right),
                Is.EqualTo(FootSide.Left));
        }

        [Test]
        public void StepSide_SagittalUsesPreference()
        {
            BalanceSupportPolygon polygon = StancePolygon();

            Vector2 forward = new Vector2(0f, 0.4f);
            Assert.That(
                PlayerBalanceRules.StepSide(forward, polygon, FootSide.Left),
                Is.EqualTo(FootSide.Left));
            Assert.That(
                PlayerBalanceRules.StepSide(forward, polygon, FootSide.Right),
                Is.EqualTo(FootSide.Right));

            // Still inside laterally, well out behind.
            Vector2 back = new Vector2(0.05f, -0.4f);
            Assert.That(
                PlayerBalanceRules.StepSide(back, polygon, FootSide.Left),
                Is.EqualTo(FootSide.Left));
            Assert.That(
                PlayerBalanceRules.StepSide(back, polygon, FootSide.Right),
                Is.EqualTo(FootSide.Right));

            // A small lateral escape under a large sagittal one still defers to the preference.
            Vector2 mostlyForward = new Vector2(0.2f, 0.5f);
            Assert.That(
                PlayerBalanceRules.StepSide(mostlyForward, polygon, FootSide.Left),
                Is.EqualTo(FootSide.Left));
            Assert.That(
                PlayerBalanceRules.StepSide(mostlyForward, polygon, FootSide.Right),
                Is.EqualTo(FootSide.Right));

            // Nothing escaped: the preference is echoed back.
            Assert.That(
                PlayerBalanceRules.StepSide(Vector2.zero, polygon, FootSide.Right),
                Is.EqualTo(FootSide.Right));
            Assert.That(
                PlayerBalanceRules.StepSide(Vector2.zero, polygon, FootSide.Left),
                Is.EqualTo(FootSide.Left));
        }

        [Test]
        public void StepTarget_OvershootsClampsAndNeverCrossesTheOtherFoot()
        {
            Vector2 leftFoot = new Vector2(-0.1f, 0f);
            Vector2 rightFoot = new Vector2(0.1f, 0f);

            // Capture point 30 cm right: the right boot lands 1.25x past it plus the 6 cm pad.
            Vector2 sober = PlayerBalanceRules.StepTarget(
                new Vector2(0.3f, 0f),
                leftFoot,
                FootSide.Right,
                Sober);
            Assert.That(sober.x, Is.EqualTo(0.435f).Within(Tolerance));
            Assert.That(sober.y, Is.EqualTo(0f).Within(Tolerance));

            // Blind drunk the same step is clamped to the shorter reach.
            Vector2 drunk = PlayerBalanceRules.StepTarget(
                new Vector2(0.3f, 0f),
                leftFoot,
                FootSide.Right,
                BlindDrunk);
            Assert.That(
                drunk.x,
                Is.EqualTo(BlindDrunk.MaximumStepReach).Within(Tolerance));

            // A sagittal step reaches 15 % further, and the pad goes to the stepping side.
            Vector2 forward = PlayerBalanceRules.StepTarget(
                new Vector2(0f, 0.6f),
                leftFoot,
                FootSide.Right,
                BlindDrunk);
            Assert.That(
                forward.y,
                Is.EqualTo(
                    BlindDrunk.MaximumStepReach *
                    PlayerBalanceRules.SagittalReachMultiplier)
                    .Within(Tolerance));
            Assert.That(
                forward.x,
                Is.EqualTo(PlayerBalanceRules.StepOvershootPad).Within(Tolerance));
            Vector2 forwardLeft = PlayerBalanceRules.StepTarget(
                new Vector2(0f, 0.6f),
                rightFoot,
                FootSide.Left,
                BlindDrunk);
            Assert.That(
                forwardLeft.x,
                Is.EqualTo(-PlayerBalanceRules.StepOvershootPad).Within(Tolerance));

            // The right boot never crosses the left one, even when the capture point is over there.
            Vector2 crossing = PlayerBalanceRules.StepTarget(
                new Vector2(-0.3f, 0f),
                leftFoot,
                FootSide.Right,
                Sober);
            Assert.That(
                crossing.x,
                Is.EqualTo(leftFoot.x + PlayerBalanceRules.MinimumFootSeparation)
                    .Within(Tolerance));
            Vector2 crossingLeft = PlayerBalanceRules.StepTarget(
                new Vector2(0.3f, 0f),
                rightFoot,
                FootSide.Left,
                Sober);
            Assert.That(
                crossingLeft.x,
                Is.EqualTo(rightFoot.x - PlayerBalanceRules.MinimumFootSeparation)
                    .Within(Tolerance));
        }

        [Test]
        public void CanRecoverByStep_RejectsBeyondReach()
        {
            BalanceSupportPolygon polygon = StancePolygon();
            PlayerBalanceSettings drunk = BlindDrunk;

            // Blind drunk the step reaches 0.38 m and 90 % of it counts: 0.342 m
            // past the polygon laterally, 0.393 m sagittally.
            Assert.That(
                PlayerBalanceRules.CanRecoverByStep(Vector2.zero, polygon, drunk),
                Is.True);
            Assert.That(
                PlayerBalanceRules.CanRecoverByStep(new Vector2(0.45f, 0f), polygon, drunk),
                Is.True);
            Assert.That(
                PlayerBalanceRules.CanRecoverByStep(new Vector2(0.55f, 0f), polygon, drunk),
                Is.False);
            Assert.That(
                PlayerBalanceRules.CanRecoverByStep(new Vector2(-0.45f, 0f), polygon, drunk),
                Is.True);
            Assert.That(
                PlayerBalanceRules.CanRecoverByStep(new Vector2(-0.55f, 0f), polygon, drunk),
                Is.False);
            Assert.That(
                PlayerBalanceRules.CanRecoverByStep(new Vector2(0f, 0.5f), polygon, drunk),
                Is.True);
            Assert.That(
                PlayerBalanceRules.CanRecoverByStep(new Vector2(0f, 0.6f), polygon, drunk),
                Is.False);
            Assert.That(
                PlayerBalanceRules.CanRecoverByStep(new Vector2(0f, -0.4f), polygon, drunk),
                Is.True);
            Assert.That(
                PlayerBalanceRules.CanRecoverByStep(new Vector2(0f, -0.5f), polygon, drunk),
                Is.False);

            // Both axes must be within reach.
            Assert.That(
                PlayerBalanceRules.CanRecoverByStep(new Vector2(0.45f, 0.6f), polygon, drunk),
                Is.False);

            // Sober the same lateral escape is still catchable (reach 0.55 m, 0.495 counted).
            Assert.That(
                PlayerBalanceRules.CanRecoverByStep(new Vector2(0.55f, 0f), polygon, Sober),
                Is.True);
            Assert.That(
                PlayerBalanceRules.CanRecoverByStep(new Vector2(0.7f, 0f), polygon, Sober),
                Is.False);
        }

        [Test]
        public void RecoverablePolygon_ContainsSupport()
        {
            // An asymmetric stance: the left boot trails and sits further out.
            BalanceSupportPolygon support = BalanceSupportPolygon.FromFeet(
                new Vector2(-0.12f, -0.1f),
                new Vector2(0.08f, 0.05f),
                BlindDrunk);
            BalanceSupportPolygon recoverable =
                PlayerBalanceRules.RecoverablePolygon(support, BlindDrunk);

            float reach =
                BlindDrunk.MaximumStepReach *
                PlayerBalanceRules.RecoverableReachFraction;
            float sagittalReach =
                reach * PlayerBalanceRules.SagittalReachMultiplier;
            Assert.That(
                recoverable.MinX,
                Is.EqualTo(support.MinX - reach).Within(Tolerance));
            Assert.That(
                recoverable.MaxX,
                Is.EqualTo(support.MaxX + reach).Within(Tolerance));
            Assert.That(
                recoverable.MinForward,
                Is.EqualTo(support.MinForward - sagittalReach).Within(Tolerance));
            Assert.That(
                recoverable.MaxForward,
                Is.EqualTo(support.MaxForward + sagittalReach).Within(Tolerance));

            // Every corner of the support lies inside it.
            Assert.That(
                recoverable.Contains(new Vector2(support.MinX, support.MinForward)),
                Is.True);
            Assert.That(
                recoverable.Contains(new Vector2(support.MaxX, support.MinForward)),
                Is.True);
            Assert.That(
                recoverable.Contains(new Vector2(support.MinX, support.MaxForward)),
                Is.True);
            Assert.That(
                recoverable.Contains(new Vector2(support.MaxX, support.MaxForward)),
                Is.True);

            // And it is exactly the region CanRecoverByStep accepts.
            Vector2[] probes =
            {
                Vector2.zero,
                new Vector2(0.3f, 0f),
                new Vector2(0.6f, 0f),
                new Vector2(-0.6f, 0f),
                new Vector2(0f, 0.5f),
                new Vector2(0f, 0.7f),
                new Vector2(0f, -0.5f),
                new Vector2(0f, -0.7f),
                new Vector2(0.3f, 0.3f),
                new Vector2(0.5f, -0.5f),
                new Vector2(-0.5f, 0.5f)
            };
            foreach (Vector2 probe in probes)
            {
                Assert.That(
                    recoverable.Contains(probe),
                    Is.EqualTo(
                        PlayerBalanceRules.CanRecoverByStep(
                            probe,
                            support,
                            BlindDrunk)),
                    probe.ToString());
            }

            // Sober reaches further on every side.
            BalanceSupportPolygon soberReach =
                PlayerBalanceRules.RecoverablePolygon(support, Sober);
            Assert.That(soberReach.MinX, Is.LessThan(recoverable.MinX));
            Assert.That(soberReach.MaxX, Is.GreaterThan(recoverable.MaxX));
            Assert.That(soberReach.MinForward, Is.LessThan(recoverable.MinForward));
            Assert.That(soberReach.MaxForward, Is.GreaterThan(recoverable.MaxForward));
        }

        [TestCase(0.03f, 1f, 0f)]
        [TestCase(0.06f, 0.3f, 0f)]
        [TestCase(0.06f, 1f, 0.9f)]
        [TestCase(0.12f, 0.5f, 0.9f)]
        [TestCase(0.04f, 0.35f, 0.21f)]
        [TestCase(0.06f, 2f, 0.9f)]
        [TestCase(0f, 1f, 0f)]
        public void TripImpulse_ThresholdsAndScale(
            float riseMetres,
            float intoxication,
            float expected)
        {
            // Nothing below a 4 cm kerb or under 0.35 intoxication (both
            // thresholds inclusive), then 0.9 m/s² per 6 cm of kerb, scaled
            // by intoxication clamped to one.
            Assert.That(
                PlayerBalanceRules.TripImpulse(riseMetres, intoxication),
                Is.EqualTo(expected).Within(Tolerance));
        }

        [Test]
        public void TripImpulse_ConstantsMatchTheThresholds()
        {
            Assert.That(PlayerBalanceRules.TripRiseThreshold, Is.EqualTo(0.04f));
            Assert.That(PlayerBalanceRules.TripIntoxicationThreshold, Is.EqualTo(0.35f));
            Assert.That(PlayerBalanceRules.TripReferenceRise, Is.EqualTo(0.06f));
            Assert.That(PlayerBalanceRules.TripAcceleration, Is.EqualTo(0.9f));

            // Just under either threshold gives nothing.
            Assert.That(PlayerBalanceRules.TripImpulse(0.039f, 1f), Is.Zero);
            Assert.That(PlayerBalanceRules.TripImpulse(1f, 0.349f), Is.Zero);
        }

        [Test]
        public void LeanDegrees_AtanOfOffsetOverHeight()
        {
            Assert.That(PlayerBalanceRules.LeanDegrees(0f, 0.95f), Is.Zero);
            // atan(0.1)
            Assert.That(
                PlayerBalanceRules.LeanDegrees(0.095f, 0.95f),
                Is.EqualTo(5.7106f).Within(0.001f));
            Assert.That(
                PlayerBalanceRules.LeanDegrees(0.95f, 0.95f),
                Is.EqualTo(45f).Within(0.001f));
            Assert.That(
                PlayerBalanceRules.LeanDegrees(-0.95f, 0.95f),
                Is.EqualTo(-45f).Within(0.001f));

            // The same offset leans a shorter body more.
            Assert.That(
                PlayerBalanceRules.LeanDegrees(0.2f, 0.7f),
                Is.GreaterThan(PlayerBalanceRules.LeanDegrees(0.2f, 0.95f)));

            // Height clamps at 30 cm, as Omega does.
            Assert.That(
                PlayerBalanceRules.LeanDegrees(0.3f, 0.1f),
                Is.EqualTo(45f).Within(0.001f));

            // The fall lean of 28 degrees is about half a metre of COM offset at default height.
            Assert.That(BlindDrunk.FallLeanDegrees, Is.EqualTo(28f));
            Assert.That(
                PlayerBalanceRules.LeanDegrees(
                    0.505f,
                    PlayerBalanceSettings.DefaultComHeight),
                Is.EqualTo(28f).Within(0.1f));
        }

        [Test]
        public void FallDirection_TieIsRight()
        {
            Assert.That(PlayerBalanceRules.FallDirection(Vector2.zero), Is.EqualTo(1f));
            Assert.That(
                PlayerBalanceRules.FallDirection(new Vector2(0f, 0.4f)),
                Is.EqualTo(1f));
            Assert.That(
                PlayerBalanceRules.FallDirection(new Vector2(0f, -0.4f)),
                Is.EqualTo(1f));
            Assert.That(
                PlayerBalanceRules.FallDirection(new Vector2(0.001f, 0f)),
                Is.EqualTo(1f));
            Assert.That(
                PlayerBalanceRules.FallDirection(new Vector2(-0.001f, 0f)),
                Is.EqualTo(-1f));
            Assert.That(
                PlayerBalanceRules.FallDirection(new Vector2(-0.3f, 0.5f)),
                Is.EqualTo(-1f));
            Assert.That(
                PlayerBalanceRules.FallDirection(new Vector2(0.3f, -0.5f)),
                Is.EqualTo(1f));
        }

        [Test]
        public void EpisodeSeed_DeterministicAndSequenceSensitive()
        {
            const int citySeed = 887733;

            Assert.That(
                PlayerBalanceRules.EpisodeSeed(citySeed, 4),
                Is.EqualTo(PlayerBalanceRules.EpisodeSeed(citySeed, 4)));
            Assert.That(
                PlayerBalanceRules.EpisodeSeed(citySeed, 4),
                Is.Not.EqualTo(PlayerBalanceRules.EpisodeSeed(citySeed, 5)));
            Assert.That(
                PlayerBalanceRules.EpisodeSeed(citySeed, 4),
                Is.Not.EqualTo(PlayerBalanceRules.EpisodeSeed(citySeed + 1000, 4)));

            // A whole night of episodes never repeats a seed.
            HashSet<int> seen = new HashSet<int>();
            for (int sequence = 0; sequence < 64; sequence++)
            {
                Assert.That(
                    seen.Add(PlayerBalanceRules.EpisodeSeed(citySeed, sequence)),
                    Is.True,
                    $"sequence {sequence}");
            }

            // The first episode is mixed, not the city seed handed straight back.
            Assert.That(
                PlayerBalanceRules.EpisodeSeed(citySeed, 0),
                Is.Not.EqualTo(citySeed));

            // The mixer is the one the retired arrow challenge used, pinned
            // so logs stay comparable across the rewrite.
            Assert.That(
                PlayerBalanceRules.EpisodeSeed(887733, 4),
                Is.EqualTo(1178376607));
            Assert.That(
                PlayerBalanceRules.EpisodeSeed(887733, 5),
                Is.EqualTo(-1860178541));
            Assert.That(
                PlayerBalanceRules.EpisodeSeed(12345, 0),
                Is.EqualTo(-1859191561));
            Assert.That(
                PlayerBalanceRules.EpisodeSeed(-7, 3),
                Is.EqualTo(1861470818));
        }

        [Test]
        public void MaximumBalanceSurfaceAngle_IsTwelve()
        {
            Assert.That(
                PlayerBalanceRules.MaximumBalanceSurfaceAngle,
                Is.EqualTo(12f));
            Assert.That(
                PlayerBalanceRules.FixedStep,
                Is.EqualTo(1f / 120f));
        }
    }
}

using UnityEngine;

namespace BarPromenade
{
    /// <summary>
    /// The numbers behind the radio's two knobs and the binnacle's one
    /// moving needle, kept pure so they can be asserted without a scene.
    ///
    /// The tuning knob is a DETENT knob: each press turns it one click and
    /// the needle slides one eighth of the dial, wrapping back to the start
    /// off the far end. What the radio actually plays at any of those clicks
    /// is deliberately not decided here - the radio is silent for now, and
    /// whatever voice it is later given will read <c>Tuning01</c> and add its
    /// own curve rather than have this file guess one.
    /// </summary>
    public static class LastRouteCarRadioModel
    {
        public const int DetentCount = 8;

        /// <summary>Where the last owner left the needle. Off the ends, so a
        /// first press moves it visibly either way.</summary>
        public const int DefaultDetent = 2;

        /// <summary>A full turn of the tuning knob is the whole dial.</summary>
        public const float KnobDegreesPerDetent = 360f / DetentCount;

        /// <summary>How far the power knob turns from off to on: the click
        /// past the stop, not a volume sweep.</summary>
        public const float PowerKnobOnDegrees = 60f;

        /// <summary>The speed the needle pins at, in metres per second - the
        /// mountain leg cruises at about a fifth of it.</summary>
        public const float SpeedoFullScaleSpeed = 25f;

        /// <summary>Zero to full scale, clockwise as the driver sees it.</summary>
        public const float SpeedoSweepDegrees = 240f;

        public static int WrapDetent(int detent)
        {
            return ((detent % DetentCount) + DetentCount) % DetentCount;
        }

        public static int StepDetent(int detent)
        {
            return WrapDetent(detent + 1);
        }

        /// <summary>Needle position along the dial, `0` at the passenger's
        /// end and `1` at the driver's.</summary>
        public static float Tuning01FromDetent(int detent)
        {
            return WrapDetent(detent) / (float)(DetentCount - 1);
        }

        public static float TuningKnobDegrees(int detent)
        {
            return WrapDetent(detent) * KnobDegreesPerDetent;
        }

        public static float Speedometer01(float speed)
        {
            if (float.IsNaN(speed) || float.IsInfinity(speed) || speed <= 0f)
            {
                return 0f;
            }

            return Mathf.Clamp01(speed / SpeedoFullScaleSpeed);
        }
    }
}

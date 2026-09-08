using UnityEngine;

namespace BarPromenade.Rendering
{
    /// <summary>
    /// How long the print takes to arrive and to leave, as numbers.
    /// </summary>
    public static class BegottenRampRules
    {
        /// <summary>
        /// The print comes up over fifteen seconds of real time once the
        /// player is back in the game. Switching a mode on in a menu and
        /// finding the world already replaced when you close it tells you
        /// nothing about what changed; a quarter of a minute of it arriving
        /// is the mode introducing itself.
        /// </summary>
        public const float RampInSeconds = 15f;

        /// <summary>
        /// Leaving is faster. Coming in is the effect; going out is only
        /// getting out of the way, and a player who has switched it off is
        /// waiting for their own game back.
        /// </summary>
        public const float RampOutSeconds = 3f;

        /// <summary>A long stall - a scene load, a dragged window - moves
        /// the ramp by at most this much, as the projector's clock does.
        /// </summary>
        public const float MaximumStepSeconds = 0.25f;
    }

    /// <summary>
    /// The strength of the Begotten mode over time: zero is the ordinary
    /// picture, one is the print exactly as it has always looked.
    ///
    /// The ramp belongs to the transition, not to the setting. Turning the
    /// mode on arms it, and nothing moves while the menu is up - the world
    /// behind a paused menu is a still, and a mode that arrived over that
    /// still would have finished before the player saw a frame of it. It
    /// starts when the game does, and it runs on unscaled time so a paused
    /// game cannot spend it.
    ///
    /// A scene that loads with the mode already on starts at full strength:
    /// the ramp is what happens when the player changes their mind, not a
    /// thing to sit through at every door.
    /// </summary>
    public sealed class BegottenRampModel
    {
        private bool armed;
        private bool rising;
        private bool synchronised;
        private bool observed;

        public BegottenRampModel(bool enabledAtStart = false)
        {
            rising = enabledAtStart;
            observed = enabledAtStart;
            Strength = enabledAtStart ? 1f : 0f;
        }

        /// <summary>
        /// Reads the setting each frame and decides whether it is something
        /// to ramp.
        ///
        /// Only a change made with the menu up is an arrival: that is a
        /// player who will come back and watch it. The same flag moving
        /// with no menu open belongs to a test, a debug key or the first
        /// sight of a saved preference, and there is nothing for it to
        /// arrive from - it simply is what it is.
        /// </summary>
        public void Observe(bool enabled, bool paused)
        {
            if (!synchronised)
            {
                synchronised = true;
                observed = enabled;
                SnapTo(enabled);
                return;
            }

            if (enabled == observed)
            {
                return;
            }

            observed = enabled;
            if (!paused)
            {
                SnapTo(enabled);
                return;
            }

            if (enabled)
            {
                NotifyEnabled();
            }
            else
            {
                NotifyDisabled();
            }
        }

        /// <summary>Zero to one, linear in time. The renderer reads
        /// <see cref="EasedStrength"/> rather than this.</summary>
        public float Strength { get; private set; }

        /// <summary>
        /// Waiting for the player to close the menu. Turning the mode on
        /// sets it; starting the ramp or turning the mode off again clears
        /// it.
        /// </summary>
        public bool IsArmed => armed;

        /// <summary>True while the strength is still moving.</summary>
        public bool IsRamping =>
            !armed && !Mathf.Approximately(Strength, rising ? 1f : 0f);

        /// <summary>
        /// The mode was switched on. If the print is already up - the
        /// player turned it off and on again without leaving the menu -
        /// there is nothing to arrive, so it simply stays.
        /// </summary>
        public void NotifyEnabled()
        {
            if (Strength >= 1f)
            {
                armed = false;
                rising = true;
                return;
            }

            armed = true;
            rising = false;
        }

        /// <summary>The mode was switched off. The print leaves at once, at
        /// the leaving speed, from wherever it had got to.</summary>
        public void NotifyDisabled()
        {
            armed = false;
            rising = false;
        }

        /// <summary>The player closed the menu and the game is theirs
        /// again. An armed ramp starts here; anything else is untouched.
        /// </summary>
        public void NotifyResumed()
        {
            if (!armed)
            {
                return;
            }

            armed = false;
            rising = true;
        }

        /// <summary>Straight to the setting with no ramp: a scene that
        /// loaded with the mode already decided.</summary>
        public void SnapTo(bool enabled)
        {
            armed = false;
            rising = enabled;
            Strength = enabled ? 1f : 0f;
        }

        /// <summary>Back to a session that has never seen the setting, so
        /// the next <see cref="Observe"/> adopts it without an arrival.
        /// </summary>
        public void Reset()
        {
            armed = false;
            rising = false;
            synchronised = false;
            observed = false;
            Strength = 0f;
        }

        /// <summary>
        /// Moves the strength one frame. <paramref name="paused"/> holds it
        /// where it is: the ramp is something the player watches, so it
        /// cannot be spent behind a menu.
        /// </summary>
        public float Advance(float unscaledDeltaSeconds, bool paused)
        {
            if (paused || armed)
            {
                return Strength;
            }

            float step = unscaledDeltaSeconds;
            if (float.IsNaN(step) || float.IsInfinity(step) || step <= 0f)
            {
                return Strength;
            }

            step = Mathf.Min(step, BegottenRampRules.MaximumStepSeconds);
            float seconds = rising
                ? BegottenRampRules.RampInSeconds
                : BegottenRampRules.RampOutSeconds;
            Strength = Mathf.MoveTowards(
                Strength,
                rising ? 1f : 0f,
                step / Mathf.Max(seconds, 0.001f));
            return Strength;
        }

        /// <summary>
        /// The strength the renderer uses. The linear value is what the
        /// clock owns; this is what the eye wants, because a print that
        /// starts and stops abruptly reads as a cut rather than a dissolve.
        /// Both ends are exact: at zero there is no print, and at one the
        /// picture is the one the mode has always made.
        /// </summary>
        public float EasedStrength => Smooth01(Strength);

        private static float Smooth01(float value)
        {
            float clamped = Mathf.Clamp01(value);
            return clamped * clamped * (3f - 2f * clamped);
        }
    }

    /// <summary>
    /// The one ramp the renderer reads, in the shape
    /// <see cref="IntoxicationRenderState"/> already uses for a strength
    /// the composite consumes every frame.
    ///
    /// It is static because the fifteen seconds are the player's, not a
    /// scene's: walk through a door halfway through and the print keeps
    /// arriving on the other side. The subsystem reset clears it between
    /// play sessions, as every other static in the project does.
    /// </summary>
    public static class BegottenModeRamp
    {
        private static readonly BegottenRampModel Model =
            new BegottenRampModel();

        /// <summary>Zero to one, eased. Zero is the ordinary picture and
        /// one is the print exactly as it has always looked.</summary>
        public static float Weight =>
            DebugWeightOverride ?? Model.EasedStrength;

        public static bool IsRamping => Model.IsRamping;

        /// <summary>Test seam, beside the feature's own
        /// <c>DebugForceFilmFrame</c>: pins the weight so a fixture can
        /// photograph a half-arrived print without waiting for it.</summary>
        internal static float? DebugWeightOverride { get; set; }

        [RuntimeInitializeOnLoadMethod(
            RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            DebugWeightOverride = null;
            Model.Reset();
        }

        internal static void ResetForTests()
        {
            DebugWeightOverride = null;
            Model.Reset();
        }

        /// <summary>Reads the setting and moves the ramp one frame.</summary>
        public static void Advance(
            bool enabled,
            bool paused,
            float unscaledDeltaSeconds)
        {
            Model.Observe(enabled, paused);
            Model.Advance(unscaledDeltaSeconds, paused);
        }

        /// <summary>The player closed the pause menu and the game is
        /// theirs again: an armed arrival starts now.</summary>
        public static void NotifyReturnedToPlay()
        {
            Model.NotifyResumed();
        }
    }
}

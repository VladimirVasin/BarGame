using System;
using UnityEngine;

namespace BarPromenade
{
    public enum LastRouteCarEnginePhase
    {
        /// <summary>Key out. The car is parked and the man is on the
        /// bonnet.</summary>
        Off = 0,

        /// <summary>The starter is turning it over; it has not caught
        /// yet.</summary>
        Starting = 1,

        Running = 2,

        /// <summary>Key off: the revs are falling and the block is
        /// shuddering to a stop.</summary>
        Stopping = 3
    }

    /// <summary>
    /// What the engine is doing, as a pure function of what the car is
    /// doing.
    ///
    /// <see cref="LastRouteCarDriveModel"/> says how fast the car goes and
    /// how hard it is accelerating; this turns that into the two numbers an
    /// engine is heard by - revs and load - through the one thing a real
    /// gearbox adds, which is that the revs do not follow the speed. They
    /// climb with it, drop through a change, and climb again. On a straight
    /// city street that is the difference between a car and a slide whistle;
    /// on the mountain, where ten hairpins each pull the car down to
    /// `3.5 m/s` and let it go again, it is most of what the climb sounds
    /// like: the drop into second before the bend, and the grade held in it
    /// after.
    ///
    /// Pure, like the drive model and the suspension: no transform, no
    /// <c>Time</c>, no AudioSource. The MonoBehaviour over it hands it a
    /// delta and the car's own numbers and reads back the revs.
    /// </summary>
    public sealed class LastRouteCarEngineModel
    {
        /// <summary>Revs at idle, as a fraction of the audible range.</summary>
        public const float IdleRpm01 = 0.14f;

        /// <summary>How long the starter turns it over before it catches.
        /// Long enough to be a beat and not a click, short enough not to
        /// read as a car that will not start.</summary>
        public const float StarterSeconds = 1.05f;

        /// <summary>The revs it flares to the instant it catches, before
        /// settling back to idle.</summary>
        public const float CatchFlareRpm01 = 0.58f;

        public const float CatchSettleSeconds = 0.65f;

        /// <summary>Key off to silence.</summary>
        public const float ShutdownSeconds = 0.8f;

        /// <summary>The revs the starter turns it over at: well under idle,
        /// which is what makes the catch audible as a catch.</summary>
        public const float CrankingRpm01 = 0.05f;

        /// <summary>
        /// Top speed of each gear, in metres per second, at the top of the
        /// audible rev range. Three are enough for a car that never exceeds
        /// `8.2 m/s`: it pulls off in first, takes the streets in second and
        /// third, and the hairpins drop it back to second.
        /// </summary>
        public static readonly float[] GearTopSpeeds = { 3.4f, 6.1f, 9.6f };

        /// <summary>Revs at which it changes up, with a gear above.</summary>
        public const float ShiftUpRpm01 = 0.84f;

        /// <summary>Revs at which it changes down, with a gear below. The
        /// gap to the up-shift is the hysteresis that stops it hunting on
        /// a speed that sits exactly on a boundary.</summary>
        public const float ShiftDownRpm01 = 0.36f;

        /// <summary>Clutch in, throttle closed, clutch out: the audible dip
        /// of a change.</summary>
        public const float ShiftSeconds = 0.32f;

        /// <summary>Below this the car is standing and the box is in
        /// neutral, whatever gear it was in when it stopped.</summary>
        public const float StandingSpeed = 0.35f;

        /// <summary>How fast the revs chase their target, per second. An
        /// engine's flywheel is not a lookup table.</summary>
        public const float RevResponsePerSecond = 7f;

        public const float LoadResponsePerSecond = 4.5f;

        /// <summary>
        /// Metres per second squared that read as full throttle. The city
        /// profile accelerates at `1.9` and the mountain at `1.5`, so
        /// pulling away is most of the way to it and a grade on top goes the
        /// rest.
        /// </summary>
        public const float ReferenceAcceleration = 2.2f;

        /// <summary>What it costs just to keep rolling: tyres, air, the
        /// gearbox. Load at steady speed on the flat.</summary>
        public const float RollingLoad01 = 0.14f;

        /// <summary>Braking harder than this is overrun: throttle shut,
        /// no load at all.</summary>
        public const float OverrunAcceleration = -0.25f;

        private const float Gravity = 9.81f;

        public LastRouteCarEnginePhase Phase { get; private set; } =
            LastRouteCarEnginePhase.Off;

        /// <summary>Zero-based; `0` is first.</summary>
        public int Gear { get; private set; }

        /// <summary>Revs, `0` dead to `1` at the top of the audible range.
        /// Smoothed, so this is what the flywheel is doing rather than what
        /// the wheels are asking for.</summary>
        public float Rpm01 { get; private set; }

        /// <summary>How hard it is working, `0` overrun to `1` full
        /// throttle. Smoothed for the same reason.</summary>
        public float Load01 { get; private set; }

        /// <summary>Seconds since the phase began.</summary>
        public float PhaseSeconds { get; private set; }

        public float ShiftSecondsRemaining { get; private set; }

        public bool IsShifting => ShiftSecondsRemaining > 0f;

        public bool IsRunning =>
            Phase == LastRouteCarEnginePhase.Running;

        /// <summary>True while anything at all should be heard from the
        /// block: turning over, running, or dying.</summary>
        public bool IsAudible =>
            Phase != LastRouteCarEnginePhase.Off;

        /// <summary>Gear changes since the engine was started, for the
        /// tests that prove the hairpins are heard as hairpins.</summary>
        public int ShiftCount { get; private set; }

        /// <summary>
        /// Turns the key.
        ///
        /// <paramref name="alreadyRunning"/> is the mountain leg: the car
        /// comes out of the tunnel with the engine that has been running
        /// since the island, so there is no starter to hear and the revs
        /// are already where the speed puts them. On the island it is
        /// false, and the whole beat plays - crank, catch, flare, settle -
        /// while the hero is still walking round to his door.
        /// </summary>
        public void Start(bool alreadyRunning)
        {
            if (Phase == LastRouteCarEnginePhase.Running ||
                Phase == LastRouteCarEnginePhase.Starting)
            {
                return;
            }

            ShiftCount = 0;
            ShiftSecondsRemaining = 0f;
            Gear = 0;
            if (alreadyRunning)
            {
                EnterPhase(LastRouteCarEnginePhase.Running);
                Rpm01 = IdleRpm01;
                Load01 = RollingLoad01;
                return;
            }

            EnterPhase(LastRouteCarEnginePhase.Starting);
            Rpm01 = 0f;
            Load01 = 0f;
        }

        /// <summary>Key off. Does nothing to a cold engine.</summary>
        public void Stop()
        {
            if (Phase == LastRouteCarEnginePhase.Off ||
                Phase == LastRouteCarEnginePhase.Stopping)
            {
                return;
            }

            EnterPhase(LastRouteCarEnginePhase.Stopping);
            ShiftSecondsRemaining = 0f;
        }

        /// <summary>
        /// One tick. Speed and acceleration are the drive model's own;
        /// the grade is rise over run along the road, positive uphill, so
        /// the mountain's `8%` arrives as `0.08`.
        /// </summary>
        public void Advance(
            float deltaTime,
            float speed,
            float longitudinalAcceleration,
            float grade)
        {
            float step = Sanitize(deltaTime);
            if (step <= 0f)
            {
                return;
            }

            PhaseSeconds += step;
            float cleanSpeed = Mathf.Max(0f, Sanitize(speed));
            float acceleration = Sanitize(longitudinalAcceleration);
            float rise = Mathf.Clamp(Sanitize(grade), -0.5f, 0.5f);

            float targetRpm;
            float targetLoad;
            switch (Phase)
            {
                case LastRouteCarEnginePhase.Starting:
                    EvaluateStarting(out targetRpm, out targetLoad);
                    break;
                case LastRouteCarEnginePhase.Running:
                    AdvanceGearbox(step, cleanSpeed);
                    targetRpm = EvaluateRunningRpm(cleanSpeed);
                    targetLoad = EvaluateRunningLoad(
                        cleanSpeed,
                        acceleration,
                        rise);
                    break;
                case LastRouteCarEnginePhase.Stopping:
                    EvaluateStopping(out targetRpm, out targetLoad);
                    break;
                default:
                    targetRpm = 0f;
                    targetLoad = 0f;
                    break;
            }

            Rpm01 = Mathf.Lerp(
                Rpm01,
                targetRpm,
                Mathf.Clamp01(step * RevResponsePerSecond));
            Load01 = Mathf.Lerp(
                Load01,
                targetLoad,
                Mathf.Clamp01(step * LoadResponsePerSecond));
        }

        /// <summary>
        /// Pure: the revs a speed asks for in a gear, before smoothing,
        /// clamped to idle at the bottom because a running engine never
        /// goes below it.
        /// </summary>
        public static float EvaluateGearRpm01(float speed, int gear)
        {
            int index = Mathf.Clamp(gear, 0, GearTopSpeeds.Length - 1);
            float raw = Mathf.Max(0f, Sanitize(speed)) / GearTopSpeeds[index];
            return Mathf.Clamp(raw, IdleRpm01, 1f);
        }

        private void EvaluateStarting(
            out float targetRpm,
            out float targetLoad)
        {
            targetLoad = 0f;
            if (PhaseSeconds < StarterSeconds)
            {
                targetRpm = CrankingRpm01;
                return;
            }

            float settle = (PhaseSeconds - StarterSeconds) /
                           CatchSettleSeconds;
            targetRpm = Mathf.Lerp(
                CatchFlareRpm01,
                IdleRpm01,
                Mathf.Clamp01(settle));
            if (settle >= 1f)
            {
                EnterPhase(LastRouteCarEnginePhase.Running);
            }
        }

        private void EvaluateStopping(
            out float targetRpm,
            out float targetLoad)
        {
            targetLoad = 0f;
            float progress = PhaseSeconds / ShutdownSeconds;
            targetRpm = progress >= 1f
                ? 0f
                : Mathf.Lerp(IdleRpm01, 0f, progress);
            if (progress >= 1f)
            {
                EnterPhase(LastRouteCarEnginePhase.Off);
                Rpm01 = 0f;
            }
        }

        private void AdvanceGearbox(float step, float speed)
        {
            if (ShiftSecondsRemaining > 0f)
            {
                ShiftSecondsRemaining = Mathf.Max(
                    0f,
                    ShiftSecondsRemaining - step);
                return;
            }

            if (speed < StandingSpeed)
            {
                Gear = 0;
                return;
            }

            float raw = speed / GearTopSpeeds[Gear];
            if (raw > ShiftUpRpm01 && Gear < GearTopSpeeds.Length - 1)
            {
                Gear++;
                BeginShift();
            }
            else if (raw < ShiftDownRpm01 && Gear > 0)
            {
                Gear--;
                BeginShift();
            }
        }

        private void BeginShift()
        {
            ShiftSecondsRemaining = ShiftSeconds;
            ShiftCount++;
        }

        private float EvaluateRunningRpm(float speed)
        {
            if (IsShifting || speed < StandingSpeed)
            {
                // Clutch in: the flywheel falls toward idle, which is the
                // dip a change is heard by.
                return IdleRpm01;
            }

            return EvaluateGearRpm01(speed, Gear);
        }

        private float EvaluateRunningLoad(
            float speed,
            float acceleration,
            float rise)
        {
            if (IsShifting || speed < StandingSpeed)
            {
                return 0f;
            }

            if (acceleration < OverrunAcceleration)
            {
                return 0f;
            }

            float gradeLoad = Mathf.Max(0f, rise) * Gravity /
                              ReferenceAcceleration;
            float pullLoad = Mathf.Max(0f, acceleration) /
                             ReferenceAcceleration;
            return Mathf.Clamp01(RollingLoad01 + gradeLoad + pullLoad);
        }

        private void EnterPhase(LastRouteCarEnginePhase phase)
        {
            Phase = phase;
            PhaseSeconds = 0f;
        }

        private static float Sanitize(float value)
        {
            return float.IsNaN(value) || float.IsInfinity(value) ? 0f : value;
        }
    }
}

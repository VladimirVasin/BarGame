using System;

namespace BarPromenade
{
    /// <summary>Signed normalized blendshape travel: left, down and forward are positive.</summary>
    public readonly struct ForemanChinDisplacement
    {
        public readonly double X, Y, Z;
        public ForemanChinDisplacement(double x, double y, double z) { X = x; Y = y; Z = z; }
    }

    /// <summary>
    /// Inertial lower-face flesh on the existing life clock. Mouth changes move its attachment;
    /// head acceleration kicks its mass. Three underdamped springs lag, overshoot and settle.
    /// This model owns no clock, random sequence, collision body, geometry or speech duration.
    /// </summary>
    public sealed class ForemanChinDynamics
    {
        public const double MaximumTravel = 1d;
        public const double MaximumStepSeconds = .5d;
        private const double IntegrationStepSeconds = 1d / 120d;
        private const double MouthImpulse = 3.8d;
        private const double MaximumVelocity = 12d;
        private double previousSeconds = double.NaN;
        private double x, y, z, vx, vy, vz;
        private double targetX, targetY, targetZ;
        private double previousPitch, previousYaw, pitchVelocity, yawVelocity;

        public ForemanChinDisplacement Current => new ForemanChinDisplacement(x, y, z);
        public bool IsMoving => Math.Abs(x) + Math.Abs(y) + Math.Abs(z) > .0001d ||
            Math.Abs(vx) + Math.Abs(vy) + Math.Abs(vz) > .001d;

        public ForemanChinDisplacement Advance(double lifeSeconds, SpeechMouthPose mouth, bool speechDriven,
            double headPitchDegrees = 0d, double headYawDegrees = 0d)
        {
            if (!Finite(lifeSeconds)) return Current;
            double dt = lifeSeconds - previousSeconds;
            if (dt == 0d) return Current;
            headPitchDegrees = Finite(headPitchDegrees) ? headPitchDegrees % 360d : 0d;
            headYawDegrees = Finite(headYawDegrees) ? headYawDegrees % 360d : 0d;
            ForemanChinDisplacement target = speechDriven ? MouthTarget(mouth) : default;
            if (double.IsNaN(previousSeconds) || dt < 0d || dt > MaximumStepSeconds)
            {
                Reset();
                previousSeconds = lifeSeconds;
                previousPitch = headPitchDegrees; previousYaw = headYawDegrees;
                targetX = target.X; targetY = target.Y; targetZ = target.Z;
                return Current;
            }

            double newPitchVelocity = Clamp(DeltaAngle(previousPitch, headPitchDegrees) / dt, 400d);
            double newYawVelocity = Clamp(DeltaAngle(previousYaw, headYawDegrees) / dt, 400d);
            vx += (target.X - targetX) * MouthImpulse;
            vy += (target.Y - targetY) * MouthImpulse;
            vz += (target.Z - targetZ) * MouthImpulse;
            if (speechDriven)
            {
                // Angular velocity changes supply an impulse; no division by dt a second time.
                // The mass lags a turn and swings back when the head stops turning.
                vx -= (newYawVelocity - yawVelocity) * .014d;
                vy += (newPitchVelocity - pitchVelocity) * .009d;
                vz -= (newPitchVelocity - pitchVelocity) * .018d;
            }
            previousSeconds = lifeSeconds;
            previousPitch = headPitchDegrees; previousYaw = headYawDegrees;
            pitchVelocity = newPitchVelocity; yawVelocity = newYawVelocity;
            targetX = target.X; targetY = target.Y; targetZ = target.Z;
            vx = Clamp(vx, MaximumVelocity); vy = Clamp(vy, MaximumVelocity); vz = Clamp(vz, MaximumVelocity);
            int steps = (int)Math.Ceiling(dt / IntegrationStepSeconds);
            double step = dt / steps;
            for (int index = 0; index < steps; index++)
            {
                Integrate(ref x, ref vx, targetX, 2.05d, .34d, step);
                Integrate(ref y, ref vy, targetY, 2.7d, .36d, step);
                Integrate(ref z, ref vz, targetZ, 2.35d, .32d, step);
            }
            if (!speechDriven && !IsMoving) x = y = z = vx = vy = vz = 0d;
            return Current;
        }

        public void Reset()
        {
            previousSeconds = double.NaN;
            x = y = z = vx = vy = vz = targetX = targetY = targetZ = 0d;
            previousPitch = previousYaw = pitchVelocity = yawVelocity = 0d;
        }

        private static ForemanChinDisplacement MouthTarget(SpeechMouthPose mouth)
        {
            switch (mouth)
            {
                case SpeechMouthPose.Narrow: return new ForemanChinDisplacement(-.12d, .22d, .05d);
                case SpeechMouthPose.Open: return new ForemanChinDisplacement(.06d, .60d, .30d);
                case SpeechMouthPose.Round: return new ForemanChinDisplacement(-.08d, .40d, .55d);
                case SpeechMouthPose.Wide: return new ForemanChinDisplacement(.22d, .38d, -.20d);
                case SpeechMouthPose.Teeth: return new ForemanChinDisplacement(-.18d, .16d, -.15d);
                default: return default;
            }
        }

        private static void Integrate(ref double position, ref double velocity, double target,
            double frequency, double dampingRatio, double dt)
        {
            double omega = 2d * Math.PI * frequency;
            velocity += ((target - position) * omega * omega - 2d * dampingRatio * omega * velocity) * dt;
            velocity = Clamp(velocity, MaximumVelocity);
            position += velocity * dt;
            if (Math.Abs(position) <= MaximumTravel) return;
            position = Clamp(position, MaximumTravel);
            // The authored limits remain hard even after a large head impulse. A small damped
            // rebound avoids sticking to that limit without stretching the pinned upper edge.
            if (position * velocity > 0d) velocity *= -.18d;
        }

        private static double DeltaAngle(double previous, double current)
        {
            double delta = (current - previous) % 360d;
            if (delta > 180d) delta -= 360d;
            if (delta < -180d) delta += 360d;
            return delta;
        }
        private static double Clamp(double value, double limit) => Math.Max(-limit, Math.Min(limit, value));
        private static bool Finite(double value) => !double.IsNaN(value) && !double.IsInfinity(value);
    }
}

using UnityEngine;

namespace BarPromenade
{
    /// <summary>Bounded, simulation-owned hand drift and imperfect recovery; samples never advance it.</summary>
    internal sealed class CombatFirearmAimMotion
    {
        private float driftPhase, verticalPhase, irregularPhase, tremorPhase, finePhase, movement;
        private float recoveryElapsed = 1f, recoverySeconds = .72f;
        private Vector2 recoveryOrigin, kick;

        internal float MovementAmount => movement;
        internal Vector2 RecoveryOffset
        {
            get
            {
                if (recoveryElapsed >= recoverySeconds) return Vector2.zero;
                float t = recoveryElapsed;
                float response = t < .06f ? Mathf.SmoothStep(0f, 1f, t / .06f) :
                    Mathf.Exp(-(t - .06f) * 5.5f) * Mathf.Cos((t - .06f) * 8f);
                float tail = 1f - Mathf.SmoothStep(0f, 1f, (t - .55f) / (recoverySeconds - .55f));
                return Vector2.ClampMagnitude((recoveryOrigin * Mathf.Exp(-8f * t) + kick * response) * tail, 2.5f);
            }
        }

        internal Vector2 Offset(bool shotgun)
        {
            float weight = (shotgun ? .72f : 1f) * (1f + 1.7f * movement);
            float irregular = .65f + .35f * Mathf.Sin(irregularPhase);
            return new Vector2(
                (.28f * Mathf.Sin(driftPhase) + .12f * Mathf.Sin(tremorPhase) * irregular) * weight,
                (.16f * Mathf.Sin(verticalPhase) + .075f * Mathf.Sin(finePhase) * irregular) * weight) +
                RecoveryOffset;
        }

        internal void Advance(float seconds, float planarSpeed, bool aiming)
        {
            if (!float.IsFinite(seconds) || seconds <= 0f) return;
            float dt = Mathf.Min(seconds, SecondOrderFilter.MaximumAdvanceSeconds);
            // Reloading may hide the hands' offset, but cannot suspend a
            // previous shot and resurrect it when the magazine returns.
            recoveryElapsed = Mathf.Min(recoverySeconds, recoveryElapsed + dt);
            if (!aiming) return;
            driftPhase = Mathf.Repeat(driftPhase + dt * 1.31f, Mathf.PI * 2f);
            verticalPhase = Mathf.Repeat(verticalPhase + dt * 1.7947f, Mathf.PI * 2f);
            irregularPhase = Mathf.Repeat(irregularPhase + dt * 1.0349f, Mathf.PI * 2f);
            tremorPhase = Mathf.Repeat(tremorPhase + dt * 47f, Mathf.PI * 2f);
            finePhase = Mathf.Repeat(finePhase + dt * 39.01f, Mathf.PI * 2f);
            float target = float.IsFinite(planarSpeed) ? Mathf.Clamp01(planarSpeed / 2.2f) : 0f;
            movement = Mathf.Lerp(target, movement, Mathf.Exp(-dt * (target > movement ? 12f : 4f)));
        }

        internal void NotifyShot(bool shotgun, int sequence)
        {
            recoveryOrigin = RecoveryOffset;
            float side = sequence % 3 == 0 ? -1f : 1f;
            kick = new Vector2(side * (shotgun ? .85f : .55f) * (.85f + .15f * Mathf.Sin(driftPhase)),
                shotgun ? 1.25f : .9f);
            recoverySeconds = shotgun ? .95f : .72f;
            recoveryElapsed = 0f;
        }

        internal void Reset()
        {
            driftPhase = verticalPhase = irregularPhase = tremorPhase = finePhase = movement = 0f;
            recoveryOrigin = kick = Vector2.zero;
            recoveryElapsed = recoverySeconds;
        }
    }
}

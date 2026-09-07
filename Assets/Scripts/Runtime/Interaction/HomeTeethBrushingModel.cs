using System;
using UnityEngine;

namespace BarPromenade
{
    public enum HomeTeethBrushingPhase
    {
        Idle, CameraToEyes, OpenFaucet, RaiseBrush, Brushing, ShowTeeth,
        Spit, CloseFaucet, CameraReturn, Completed
    }

    /// <summary>Progress comes only from commanded brush travel confirmed at the teeth.</summary>
    public sealed class HomeTeethBrushingProgress
    {
        public const float RequiredDistance = 0.40f;
        public const float MaximumCreditSpeed = 0.08f;
        public static readonly Vector2 Reach = new Vector2(0.026f, 0.008f);
        private float pendingTravel;
        public Vector2 Offset { get; private set; }
        public float CleanedDistance { get; private set; }
        public float Amount => Mathf.Clamp01(CleanedDistance / RequiredDistance);
        public bool Complete => CleanedDistance >= RequiredDistance;
        public void Move(Vector2 mousePixels)
        {
            if (!float.IsFinite(mousePixels.x) || !float.IsFinite(mousePixels.y) || mousePixels.magnitude < 1.5f) return;
            Vector2 next = Offset + Vector2.ClampMagnitude(mousePixels, 300f) * 0.00016f;
            next = new Vector2(Mathf.Clamp(next.x, -Reach.x, Reach.x), Mathf.Clamp(next.y, -Reach.y, Reach.y));
            pendingTravel += Vector2.Distance(Offset, next);
            Offset = next;
        }
        public float Credit(float actualBrushTravel, bool contact, float seconds)
        {
            float credited = contact ? Mathf.Min(pendingTravel, actualBrushTravel,
                Mathf.Max(0f, seconds) * MaximumCreditSpeed) : 0f;
            CleanedDistance = Mathf.Min(RequiredDistance, CleanedDistance + credited);
            pendingTravel = 0f;
            return credited;
        }
        public void Reset() { Offset = Vector2.zero; CleanedDistance = pendingTravel = 0f; }
    }

    public sealed class HomeTeethBrushingTimeline
    {
        public const float CameraToEyesSeconds = 2f;
        public const float ValveReachSeconds = 0.55f;
        public const float ValveTurnSeconds = 0.7f;
        public const float ValveWithdrawSeconds = 0.45f;
        public const float OpenFaucetSeconds = ValveReachSeconds + ValveTurnSeconds + ValveWithdrawSeconds;
        public const float ArmRaiseSeconds = 0.8f;
        public const float ArmLowerSeconds = 0.45f;
        public const float ShowTeethSeconds = 1.5f;
        public const float SpitSeconds = 1.5f;
        public const float SpitStartSeconds = 0.55f;
        public const float SpitEndSeconds = 0.85f;
        public const float CameraReturnSeconds = 2.2f;
        public const float CloseFaucetSeconds = ArmLowerSeconds + OpenFaucetSeconds;
        public const float EntrySeconds = CameraToEyesSeconds + OpenFaucetSeconds + ArmRaiseSeconds;
        private float phaseElapsed;
        private float returnStartBlend = 1f;
        private float returnStartArm;
        private float closeStartOpen, closeStartReach;
        public HomeTeethBrushingPhase Phase { get; private set; }
        public float PhaseElapsed => phaseElapsed;
        public bool WasCancelled { get; private set; }
        public bool IsCompleted => Phase == HomeTeethBrushingPhase.Completed;
        public bool CanCommit => IsCompleted && !WasCancelled && Cleaned;
        public bool Cleaned { get; private set; }
        public float EmissionSeconds { get; private set; }
        public float CameraBlend => Phase == HomeTeethBrushingPhase.CameraToEyes ? Smooth((phaseElapsed - 0.35f) / (CameraToEyesSeconds - 0.35f)) :
            Phase == HomeTeethBrushingPhase.CameraReturn ? returnStartBlend * (1f - Smooth(phaseElapsed / CameraReturnSeconds)) :
            Phase >= HomeTeethBrushingPhase.OpenFaucet && Phase <= HomeTeethBrushingPhase.CloseFaucet ? 1f : 0f;
        public float ArmWeight => Phase == HomeTeethBrushingPhase.RaiseBrush ? Smooth(phaseElapsed / ArmRaiseSeconds) :
            Phase == HomeTeethBrushingPhase.Brushing ? 1f : Phase == HomeTeethBrushingPhase.ShowTeeth ? 1f - Smooth(phaseElapsed / ArmLowerSeconds) :
            Phase == HomeTeethBrushingPhase.CloseFaucet || Phase == HomeTeethBrushingPhase.CameraReturn ?
                returnStartArm * (1f - Smooth(phaseElapsed / ArmLowerSeconds)) : 0f;
        public float SpitBend => Phase == HomeTeethBrushingPhase.Spit ? Smooth(phaseElapsed / 0.5f) * (1f - Smooth((phaseElapsed - 1.05f) / 0.45f)) : 0f;
        public float ValveReach => Phase == HomeTeethBrushingPhase.OpenFaucet ? Reach(phaseElapsed, 0f) :
            Phase == HomeTeethBrushingPhase.CloseFaucet ?
                phaseElapsed < ArmLowerSeconds ? closeStartReach : Reach(phaseElapsed - ArmLowerSeconds, closeStartReach) : 0f;
        public float FaucetOpen => Phase == HomeTeethBrushingPhase.OpenFaucet ? Smooth((phaseElapsed - ValveReachSeconds) / ValveTurnSeconds) :
            Phase == HomeTeethBrushingPhase.CloseFaucet ? closeStartOpen *
                (1f - Smooth((phaseElapsed - ArmLowerSeconds - ValveReachSeconds) / ValveTurnSeconds)) :
            Phase >= HomeTeethBrushingPhase.RaiseBrush && Phase <= HomeTeethBrushingPhase.Spit ? 1f : 0f;
        public bool CanStop => Phase >= HomeTeethBrushingPhase.CameraToEyes && Phase <= HomeTeethBrushingPhase.Brushing;
        public void Begin() { Reset(); Phase = HomeTeethBrushingPhase.CameraToEyes; }
        public void CompleteBrushing()
        {
            if (Phase != HomeTeethBrushingPhase.Brushing) return;
            Cleaned = true; Phase = HomeTeethBrushingPhase.ShowTeeth; phaseElapsed = 0f;
        }
        public void Advance(float seconds)
        {
            if (!float.IsFinite(seconds)) throw new ArgumentOutOfRangeException(nameof(seconds));
            float remaining = Mathf.Max(0f, seconds);
            while (remaining > 0f && Phase != HomeTeethBrushingPhase.Idle && !IsCompleted)
            {
                if (Phase == HomeTeethBrushingPhase.Brushing) { phaseElapsed += remaining; break; }
                float duration = Duration;
                float step = Mathf.Min(remaining, duration - phaseElapsed);
                if (Phase == HomeTeethBrushingPhase.Spit)
                    EmissionSeconds += Mathf.Clamp(phaseElapsed + step - SpitStartSeconds, 0f, SpitEndSeconds - SpitStartSeconds) -
                        Mathf.Clamp(phaseElapsed - SpitStartSeconds, 0f, SpitEndSeconds - SpitStartSeconds);
                phaseElapsed += step; remaining -= step;
                if (phaseElapsed >= duration)
                {
                    if (Phase == HomeTeethBrushingPhase.Spit) BeginClosing();
                    else { Phase++; phaseElapsed = 0f; }
                    if (Phase == HomeTeethBrushingPhase.CameraReturn) { returnStartBlend = 1f; returnStartArm = 0f; }
                }
            }
        }
        public bool RequestFinish()
        {
            if (!CanStop) return false;
            returnStartBlend = CameraBlend; returnStartArm = ArmWeight;
            WasCancelled = true;
            if (Phase == HomeTeethBrushingPhase.CameraToEyes)
            {
                Phase = HomeTeethBrushingPhase.CameraReturn; phaseElapsed = 0f;
            }
            else BeginClosing();
            return true;
        }
        private void BeginClosing()
        {
            closeStartOpen = FaucetOpen; closeStartReach = ValveReach;
            returnStartArm = ArmWeight;
            Phase = HomeTeethBrushingPhase.CloseFaucet; phaseElapsed = 0f;
        }
        private float Duration => Phase == HomeTeethBrushingPhase.CameraToEyes ? CameraToEyesSeconds :
            Phase == HomeTeethBrushingPhase.OpenFaucet ? OpenFaucetSeconds :
            Phase == HomeTeethBrushingPhase.RaiseBrush ? ArmRaiseSeconds :
            Phase == HomeTeethBrushingPhase.ShowTeeth ? ShowTeethSeconds :
            Phase == HomeTeethBrushingPhase.Spit ? SpitSeconds :
            Phase == HomeTeethBrushingPhase.CloseFaucet ? CloseFaucetSeconds : CameraReturnSeconds;
        private static float Reach(float elapsed, float start) =>
            Mathf.Lerp(start, 1f, Smooth(elapsed / ValveReachSeconds)) *
            (1f - Smooth((elapsed - ValveReachSeconds - ValveTurnSeconds) / ValveWithdrawSeconds));
        public void Reset()
        {
            Phase = HomeTeethBrushingPhase.Idle; phaseElapsed = EmissionSeconds = returnStartArm = closeStartOpen = closeStartReach = 0f;
            returnStartBlend = 1f; WasCancelled = Cleaned = false;
        }
        private static float Smooth(float value) => Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(value));
    }
}

using System;
using UnityEngine;

namespace BarPromenade
{
    public enum HomeTeethBrushingPhase { Idle, CameraToMirror, Brushing, ShowTeeth, Spit, CameraReturn, Completed }

    /// <summary>Progress comes only from commanded brush travel confirmed at the teeth.</summary>
    public sealed class HomeTeethBrushingProgress
    {
        public const float RequiredDistance = 0.64f;
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
        public const float CameraToMirrorSeconds = 2.8f;
        public const float ArmRaiseStartSeconds = 2f;
        public const float ArmRaiseSeconds = 0.8f;
        public const float ArmLowerSeconds = 0.45f;
        public const float ShowTeethSeconds = 1.5f;
        public const float SpitSeconds = 1.5f;
        public const float SpitStartSeconds = 0.55f;
        public const float SpitEndSeconds = 0.85f;
        public const float CameraReturnSeconds = 2.2f;
        private float phaseElapsed;
        private float returnStartBlend = 1f;
        private float returnStartArm;
        public HomeTeethBrushingPhase Phase { get; private set; }
        public float PhaseElapsed => phaseElapsed;
        public bool WasCancelled { get; private set; }
        public bool IsCompleted => Phase == HomeTeethBrushingPhase.Completed;
        public bool CanCommit => IsCompleted && !WasCancelled && Cleaned;
        public bool Cleaned { get; private set; }
        public float EmissionSeconds { get; private set; }
        public float CameraBlend => Phase == HomeTeethBrushingPhase.CameraToMirror ? Smooth((phaseElapsed - 0.35f) / (CameraToMirrorSeconds - 0.35f)) :
            Phase == HomeTeethBrushingPhase.CameraReturn ? returnStartBlend * (1f - Smooth(phaseElapsed / CameraReturnSeconds)) :
            Phase >= HomeTeethBrushingPhase.Brushing && Phase <= HomeTeethBrushingPhase.Spit ? 1f : 0f;
        public float ArmWeight => Phase == HomeTeethBrushingPhase.CameraToMirror ? Smooth((phaseElapsed - ArmRaiseStartSeconds) / ArmRaiseSeconds) :
            Phase == HomeTeethBrushingPhase.Brushing ? 1f : Phase == HomeTeethBrushingPhase.ShowTeeth ? 1f - Smooth(phaseElapsed / ArmLowerSeconds) :
            Phase == HomeTeethBrushingPhase.CameraReturn ? returnStartArm * (1f - Smooth(phaseElapsed / ArmLowerSeconds)) : 0f;
        public float SpitBend => Phase == HomeTeethBrushingPhase.Spit ? Smooth(phaseElapsed / 0.5f) * (1f - Smooth((phaseElapsed - 1.05f) / 0.45f)) : 0f;
        public float SpitCameraWeight => Phase == HomeTeethBrushingPhase.Spit ? Smooth(phaseElapsed / 0.45f) :
            Phase == HomeTeethBrushingPhase.CameraReturn && Cleaned ? 1f - Smooth(phaseElapsed / CameraReturnSeconds) : 0f;
        public void Begin() { Reset(); Phase = HomeTeethBrushingPhase.CameraToMirror; }
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
                float duration = Phase == HomeTeethBrushingPhase.CameraToMirror ? CameraToMirrorSeconds :
                    Phase == HomeTeethBrushingPhase.ShowTeeth ? ShowTeethSeconds : Phase == HomeTeethBrushingPhase.Spit ? SpitSeconds : CameraReturnSeconds;
                float step = Mathf.Min(remaining, duration - phaseElapsed);
                if (Phase == HomeTeethBrushingPhase.Spit)
                    EmissionSeconds += Mathf.Clamp(phaseElapsed + step - SpitStartSeconds, 0f, SpitEndSeconds - SpitStartSeconds) -
                        Mathf.Clamp(phaseElapsed - SpitStartSeconds, 0f, SpitEndSeconds - SpitStartSeconds);
                phaseElapsed += step; remaining -= step;
                if (phaseElapsed >= duration)
                {
                    Phase++; phaseElapsed = 0f;
                    if (Phase == HomeTeethBrushingPhase.CameraReturn) { returnStartBlend = 1f; returnStartArm = 0f; }
                }
            }
        }
        public bool RequestFinish()
        {
            if (Phase != HomeTeethBrushingPhase.CameraToMirror && Phase != HomeTeethBrushingPhase.Brushing) return false;
            returnStartBlend = CameraBlend; returnStartArm = ArmWeight;
            WasCancelled = true; Phase = HomeTeethBrushingPhase.CameraReturn; phaseElapsed = 0f; return true;
        }
        public void Reset()
        {
            Phase = HomeTeethBrushingPhase.Idle; phaseElapsed = EmissionSeconds = returnStartArm = 0f;
            returnStartBlend = 1f; WasCancelled = Cleaned = false;
        }
        private static float Smooth(float value) => Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(value));
    }
}

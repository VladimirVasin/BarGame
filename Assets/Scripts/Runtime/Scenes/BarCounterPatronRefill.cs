using System;
using UnityEngine;

namespace BarPromenade
{
    /// <summary>The existing counter glass empties through real sips and requests one queued refill.</summary>
    public sealed class BarCounterPatronRefill : MonoBehaviour
    {
        private BarPatronDrinkingArmPose drinking;
        private BarDrinkShopController shop;
        private float attentionDuration;
        private bool requestSubmitted;
        private float waitingElapsed;
        private float fill = 1f;
        public float DepletionPerSipSecond { get; private set; }
        public float FillProgress => fill;
        public bool IsWaitingForRefill { get; private set; }
        public bool IsRefilling { get; private set; }
        public bool IsAttentionActive { get; private set; }
        public float AttentionElapsedSeconds { get; private set; }
        public int AttentionCount { get; private set; }
        public int CompletedRefills { get; private set; }
        public BarPatronDrinkingArmPose Drinking => drinking;
        public BarDrinkVesselView Vessel => drinking.DrinkProp.Vessel;
        public DrinkId DrinkId => drinking.DrinkProp.DrinkId;

        public void Configure(BarPatronDrinkingArmPose owner, BarDrinkShopController service, int slot)
        {
            drinking = owner != null ? owner : throw new ArgumentNullException(nameof(owner));
            shop = service != null ? service : throw new ArgumentNullException(nameof(service));
            if (drinking.DrinkProp?.Vessel == null) throw new InvalidOperationException("Counter refills require the existing glass.");
            DepletionPerSipSecond = slot == 0 ? .11f : .073f;
            attentionDuration = owner.ConfigureCounterAttention(this);
        }

        /// <summary>Production calls this from the patron clock. Tests may disable this component and drive it explicitly.</summary>
        public void Advance(float deltaTime)
        {
            if (!float.IsFinite(deltaTime) || deltaTime < 0f) throw new ArgumentOutOfRangeException(nameof(deltaTime));
            if (drinking == null || GameTimeScaleRuntime.IsPaused) return;
            if (IsWaitingForRefill)
            {
                if (IsRefilling) return;
                waitingElapsed += deltaTime;
                if (IsAttentionActive)
                {
                    AttentionElapsedSeconds = Mathf.Min(attentionDuration, AttentionElapsedSeconds + deltaTime);
                    if (AttentionElapsedSeconds >= attentionDuration)
                    {
                        IsAttentionActive = false;
                        if (!requestSubmitted)
                            requestSubmitted = shop.QueuePatronRefill(this);
                    }
                }
                else if (waitingElapsed >= 10f)
                {
                    waitingElapsed = 0f;
                    StartAttention();
                }
                return;
            }

            // Split only at the existing finite cadence boundaries; depletion never advances a separate sip clock.
            float remaining = deltaTime;
            while (remaining > 0f)
            {
                BarPatronDrinkTimeline clock = drinking.Timeline;
                float step = Mathf.Min(remaining, Mathf.Max(.00001f, clock.PhaseDuration - clock.PhaseElapsed));
                if (clock.Phase == BarPatronDrinkPhase.Sip)
                {
                    fill = Mathf.Max(0f, fill - step * DepletionPerSipSecond);
                    Vessel.SetFillProgress(fill);
                }
                clock.Advance(step);
                remaining -= step;
                if (fill <= 0f && clock.Phase == BarPatronDrinkPhase.Rest)
                {
                    IsWaitingForRefill = true;
                    waitingElapsed = 0f;
                    StartAttention();
                    return;
                }
            }
        }

        private void StartAttention()
        {
            IsAttentionActive = true;
            AttentionElapsedSeconds = 0f;
            AttentionCount++;
        }

        internal void BeginRefill()
        {
            drinking.BeginCounterRefillSupport();
            IsRefilling = true;
            IsAttentionActive = false;
        }

        internal void CompleteRefill()
        {
            // Only the real service timeline raises the physical liquid; this method resumes drinking afterwards.
            fill = Vessel.FillProgress;
            if (fill < .999f) throw new InvalidOperationException("A patron refill ended before the actual vessel was full.");
            CompletedRefills++;
            drinking.ClearCounterCupOffer();
            IsRefilling = IsWaitingForRefill = requestSubmitted = IsAttentionActive = false;
            waitingElapsed = 0f;
        }

        internal void AbortRefill()
        {
            drinking.ClearCounterCupOffer();
            IsRefilling = requestSubmitted = false;
            fill = Vessel != null ? Vessel.FillProgress : fill;
        }

        private void OnDestroy() { if (shop != null) shop.CancelCounterPatron(this); }
    }
}

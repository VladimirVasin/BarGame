using System;

namespace BarPromenade
{
    /// <summary>One semi-automatic shot per explicit request; no buffered or automatic fire.
    /// Runtime supplies only live duel seconds and owns physical aim, body availability and input edges.</summary>
    public sealed class PistolState
    {
        private double aimElapsed, reloadElapsed, cooldownRemaining;
        private bool reloading;

        public PistolState(PistolSettings settings = null)
        {
            Settings = settings ?? PistolSettings.Prototype;
            Reset();
        }

        public PistolSettings Settings { get; }
        public int Rounds { get; private set; }
        public int ShotSequence { get; private set; }
        public bool AimRequested { get; private set; }
        public bool IsReloading => reloading;
        public bool ReloadPending => reloading || reloadElapsed > 0d;
        public bool MagazineAttached => !ReloadPending || reloadElapsed + .000001d < Settings.MagazineHandoffSeconds ||
            reloadElapsed + .000001d >= Settings.MagazineInsertSeconds;
        public bool IsRaising => AimRequested && !ReloadPending && !IsAiming;
        public bool IsAiming => AimRequested && !ReloadPending && aimElapsed + .000001d >= Settings.RaiseSeconds;
        public bool CanFire => IsAiming && !ReloadPending && MagazineAttached && Rounds > 0 && cooldownRemaining <= .000001d;
        public float AimProgress => (float)Math.Min(1d, aimElapsed / Settings.RaiseSeconds);
        public float ReloadProgress => (float)Math.Min(1d, reloadElapsed / Settings.ReloadSeconds);
        public float ReloadElapsed => (float)reloadElapsed;
        public float CooldownRemaining => (float)Math.Max(0d, cooldownRemaining);
        public float ShotElapsed => Settings.FireCooldownSeconds - CooldownRemaining;
        public float SlideBack
        {
            get
            {
                float reload = ReloadProgress * 1.8f;
                if (ReloadPending && reload >= 1.42f)
                    return reload < 1.5f ? Lerp(Rounds == 0 ? 1f : 0f, 1f, (reload - 1.42f) / .08f) :
                        Lerp(1f, 0f, (reload - 1.5f) / .08f);
                if (cooldownRemaining > 0d && ShotElapsed < .035f) return Lerp(0f, 1f, ShotElapsed / .035f);
                if (Rounds == 0) return 1f;
                return cooldownRemaining > 0d ? Lerp(1f, 0f, (ShotElapsed - .035f) / .06f) : 0f;
            }
        }

        public void SetAim(bool requested)
        {
            if (AimRequested == requested) return;
            AimRequested = requested;
            aimElapsed = 0d;
        }

        /// <summary>Rejected requests are forgotten. Only this successful command spends a round.</summary>
        public bool TryFire()
        {
            if (!CanFire) return false;
            Rounds--;
            ShotSequence = unchecked(ShotSequence + 1);
            cooldownRemaining = Settings.FireCooldownSeconds;
            return true;
        }

        /// <summary>The infinite reserve is committed only at the reload's completed boundary.</summary>
        public bool TryReload()
        {
            if (reloading || !ReloadPending && Rounds == Settings.MagazineCapacity) return false;
            reloading = true;
            aimElapsed = 0d;
            return true;
        }

        public void CancelReload()
        {
            if (!reloading) return;
            reloading = false;
            // Once the magazine has reached the hand, it remains in that real
            // stage. Resuming finishes the exchange rather than snapping it back.
            if (reloadElapsed + .000001d < Settings.MagazineHandoffSeconds) reloadElapsed = 0d;
            aimElapsed = 0d;
        }

        /// <summary>Focus, injury or ownership loss cancels intent without refunding a fired round or cooldown.</summary>
        public void CancelAction()
        {
            CancelReload();
            AimRequested = false;
            aimElapsed = 0d;
        }

        public void Advance(float seconds)
        {
            if (float.IsNaN(seconds) || float.IsInfinity(seconds) || seconds < 0f)
                throw new ArgumentOutOfRangeException(nameof(seconds));
            cooldownRemaining = Math.Max(0d, cooldownRemaining - seconds);
            double remaining = seconds;
            if (reloading)
            {
                double untilComplete = Math.Max(0d, Settings.ReloadSeconds - reloadElapsed);
                if (remaining + .000001d < untilComplete)
                {
                    reloadElapsed += remaining;
                    return;
                }
                remaining = Math.Max(0d, remaining - untilComplete);
                Rounds = Settings.MagazineCapacity;
                reloading = false;
                reloadElapsed = aimElapsed = 0d;
            }
            if (AimRequested && !ReloadPending) aimElapsed = Math.Min(Settings.RaiseSeconds, aimElapsed + remaining);
        }

        public void Reset()
        {
            Rounds = Settings.MagazineCapacity;
            ShotSequence = unchecked(ShotSequence + 1);
            AimRequested = reloading = false;
            aimElapsed = reloadElapsed = cooldownRemaining = 0d;
        }

        private static float Lerp(float from, float to, float amount)
        {
            amount = Math.Max(0f, Math.Min(1f, amount));
            return from + (to - from) * amount;
        }
    }
}

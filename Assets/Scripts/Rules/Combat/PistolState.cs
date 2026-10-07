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
        public bool IsRaising => AimRequested && !reloading && !IsAiming;
        public bool IsAiming => AimRequested && !reloading && aimElapsed + .000001d >= Settings.RaiseSeconds;
        public bool CanFire => IsAiming && Rounds > 0 && cooldownRemaining <= .000001d;
        public float AimProgress => (float)Math.Min(1d, aimElapsed / Settings.RaiseSeconds);
        public float ReloadProgress => reloading ? (float)Math.Min(1d, reloadElapsed / Settings.ReloadSeconds) : 0f;
        public float CooldownRemaining => (float)Math.Max(0d, cooldownRemaining);
        public float ShotElapsed => Settings.FireCooldownSeconds - CooldownRemaining;

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
            if (reloading || Rounds == Settings.MagazineCapacity) return false;
            reloading = true;
            reloadElapsed = aimElapsed = 0d;
            return true;
        }

        public void CancelReload()
        {
            if (!reloading) return;
            reloading = false;
            reloadElapsed = aimElapsed = 0d;
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
            if (AimRequested) aimElapsed = Math.Min(Settings.RaiseSeconds, aimElapsed + remaining);
        }

        public void Reset()
        {
            Rounds = Settings.MagazineCapacity;
            ShotSequence = unchecked(ShotSequence + 1);
            AimRequested = reloading = false;
            aimElapsed = reloadElapsed = cooldownRemaining = 0d;
        }
    }
}

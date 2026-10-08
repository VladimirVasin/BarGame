using System;

namespace BarPromenade
{
    /// <summary>Two explicit shots; reload transactions follow real chamber stages and survive interruption.</summary>
    public sealed class ShotgunState : IFirearmState
    {
        private readonly bool[] loaded = new bool[2], spent = new bool[2];
        private double aimElapsed, reloadElapsed, cooldownRemaining;
        private bool reloading;

        public ShotgunState(ShotgunSettings settings = null)
        {
            Settings = settings ?? ShotgunSettings.Prototype;
            Reset();
        }

        public ShotgunSettings Settings { get; }
        FirearmSettings IFirearmState.Settings => Settings;
        public int Rounds => (loaded[0] ? 1 : 0) + (loaded[1] ? 1 : 0);
        public int ShotSequence { get; private set; }
        public int NextBarrel => loaded[0] ? 0 : loaded[1] ? 1 : -1;
        public int LastFiredBarrel { get; private set; }
        public int ReloadChamberMask { get; private set; }
        public bool AimRequested { get; private set; }
        public bool IsReloading => reloading;
        public bool ReloadPending => ReloadChamberMask != 0;
        public bool MagazineAttached => BreakOpen01 <= .000001f;
        public bool IsRaising => AimRequested && !ReloadPending && !IsAiming;
        public bool IsAiming => AimRequested && !ReloadPending && aimElapsed + .000001d >= Settings.RaiseSeconds;
        public bool CanFire => IsAiming && !ReloadPending && MagazineAttached && Rounds > 0 && cooldownRemaining <= .000001d;
        public float AimProgress => (float)Math.Min(1d, aimElapsed / Settings.RaiseSeconds);
        public float ReloadProgress => (float)Math.Min(1d, reloadElapsed / Settings.ReloadSeconds);
        public float ReloadElapsed => (float)reloadElapsed;
        public float CooldownRemaining => (float)Math.Max(0d, cooldownRemaining);
        public float ShotElapsed => Settings.FireCooldownSeconds - CooldownRemaining;
        public float BreakOpen01 => !ReloadPending ? 0f : reloadElapsed < Settings.OpenSeconds
            ? (float)(reloadElapsed / Settings.OpenSeconds)
            : reloadElapsed <= Settings.CloseStartSeconds ? 1f
            : (float)Math.Max(0d, (Settings.ReloadSeconds - reloadElapsed) /
                (Settings.ReloadSeconds - Settings.CloseStartSeconds));

        public bool ChamberLoaded(int barrel) => loaded[ValidateBarrel(barrel)];
        public bool ChamberSpent(int barrel) => spent[ValidateBarrel(barrel)];

        public void SetAim(bool requested)
        {
            if (AimRequested == requested) return;
            AimRequested = requested;
            aimElapsed = 0d;
        }

        public bool TryFire()
        {
            if (!CanFire) return false;
            int barrel = NextBarrel;
            loaded[barrel] = false;
            spent[barrel] = true;
            LastFiredBarrel = barrel;
            ShotSequence = unchecked(ShotSequence + 1);
            cooldownRemaining = Settings.FireCooldownSeconds;
            return true;
        }

        public bool TryReload()
        {
            if (reloading || !ReloadPending && Rounds == 2) return false;
            if (!ReloadPending) ReloadChamberMask = (loaded[0] ? 0 : 1) | (loaded[1] ? 0 : 2);
            reloading = true;
            aimElapsed = 0d;
            return true;
        }

        public void CancelReload()
        {
            reloading = false;
            // The open angle, empty chambers and inserted shells remain where the action stopped.
            aimElapsed = 0d;
        }

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
                double elapsed = Math.Min(remaining, untilComplete);
                reloadElapsed += elapsed;
                CommitChamberStages();
                if (remaining + .000001d < untilComplete) return;
                remaining = Math.Max(0d, remaining - untilComplete);
                reloading = false;
                ReloadChamberMask = 0;
                reloadElapsed = aimElapsed = 0d;
            }
            if (AimRequested && !ReloadPending) aimElapsed = Math.Min(Settings.RaiseSeconds, aimElapsed + remaining);
        }

        private void CommitChamberStages()
        {
            if (reloadElapsed + .000001d >= Settings.EjectSpentSeconds)
            {
                if ((ReloadChamberMask & 1) != 0) spent[0] = false;
                if ((ReloadChamberMask & 2) != 0) spent[1] = false;
            }
            if (reloadElapsed + .000001d >= Settings.LoadFirstSeconds && (ReloadChamberMask & 1) != 0)
                loaded[0] = true;
            if (reloadElapsed + .000001d >= Settings.LoadSecondSeconds && (ReloadChamberMask & 2) != 0)
                loaded[1] = true;
        }

        public void Reset()
        {
            loaded[0] = loaded[1] = true;
            spent[0] = spent[1] = false;
            LastFiredBarrel = -1;
            ReloadChamberMask = 0;
            ShotSequence = unchecked(ShotSequence + 1);
            AimRequested = reloading = false;
            aimElapsed = reloadElapsed = cooldownRemaining = 0d;
        }

        private static int ValidateBarrel(int barrel)
        {
            if (barrel < 0 || barrel > 1) throw new ArgumentOutOfRangeException(nameof(barrel));
            return barrel;
        }
    }
}

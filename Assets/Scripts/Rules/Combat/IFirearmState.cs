namespace BarPromenade
{
    /// <summary>Shared input and presentation contract; time alone never requests a shot.</summary>
    public interface IFirearmState
    {
        FirearmSettings Settings { get; }
        int Rounds { get; }
        int ReserveRounds { get; }
        int ReserveCapacity { get; }
        int ShotSequence { get; }
        bool AimRequested { get; }
        bool IsReloading { get; }
        bool ReloadPending { get; }
        bool MagazineAttached { get; }
        bool IsRaising { get; }
        bool IsAiming { get; }
        bool CanFire { get; }
        float AimProgress { get; }
        float ReloadProgress { get; }
        float ReloadElapsed { get; }
        float CooldownRemaining { get; }
        float ShotElapsed { get; }
        void SetAim(bool requested);
        bool TryFire();
        bool TryReload();
        void CancelReload();
        void CancelAction();
        void Advance(float seconds);
        /// <summary>Adds only the rounds that fit in the carried reserve.</summary>
        int AddReserveRounds(int amount);
        void Reset();
    }
}

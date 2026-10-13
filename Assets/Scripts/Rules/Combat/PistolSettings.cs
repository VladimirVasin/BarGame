namespace BarPromenade
{
    /// <summary>The isolated prototype's magazine and unpaused action timings.</summary>
    public sealed class PistolSettings : FirearmSettings
    {
        public static PistolSettings Prototype { get; } = new PistolSettings();

        public PistolSettings(int magazineCapacity = 8, float fireCooldownSeconds = .4f,
            float raiseSeconds = .25f, float reloadSeconds = 1.8f,
            int startingReserveRounds = 0, int reserveCapacity = 48)
            : base(magazineCapacity, fireCooldownSeconds, raiseSeconds, reloadSeconds, startingReserveRounds, reserveCapacity)
        {
        }

        // The authored 1.8-second exchange scales with the configured clock.
        public float MagazineHandoffSeconds => ReloadSeconds * (.25f / 1.8f);
        public float MagazineInsertSeconds => ReloadSeconds * (1.3f / 1.8f);
    }
}

namespace BarPromenade
{
    public sealed class AlpineVillageMusicPlayer : SceneMusicPlayer
    {
        public const string ResourceFolder = "Audio/AlpineVillageMusic";
        public const string TrackName = "alpine_village_theme";
        public const string ResourcePath = ResourceFolder + "/" + TrackName;
        // The village theme is deliberately louder than the shared default.
        public const float ThemeOutputVolume = 1.00f;

        protected override string TrackResourcePath => ResourcePath;
        protected override bool InitiallySuppressed => AnyLodgeDoorOpen;
        protected override float OutputVolume => ThemeOutputVolume;

        private static bool AnyLodgeDoorOpen =>
            LodgeShelterSessionState.LeftDoorOpen || LodgeShelterSessionState.RightDoorOpen;

        protected override void Update()
        {
            // Gate before advancing/loading so an open leaf cannot start the theme.
            // The shared player owns fades, pause/resume and scene-exit tails.
            SetPlaybackSuppressed(AnyLodgeDoorOpen);
            base.Update();
        }
    }
}

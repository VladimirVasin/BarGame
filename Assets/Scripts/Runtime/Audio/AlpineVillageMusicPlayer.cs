namespace BarPromenade
{
    public sealed class AlpineVillageMusicPlayer : SceneMusicPlayer
    {
        public const string ResourceFolder = "Audio/AlpineVillageMusic";
        public const string TrackName = "alpine_village_theme";
        public const string ResourcePath = ResourceFolder + "/" + TrackName;
        // The village theme is deliberately louder than the shared default.
        public const float ThemeOutputVolume = 0.70f;

        protected override string TrackResourcePath => ResourcePath;
        protected override float OutputVolume => ThemeOutputVolume;
    }
}

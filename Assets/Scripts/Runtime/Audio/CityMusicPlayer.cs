namespace BarPromenade
{
    public sealed class CityMusicPlayer : SceneMusicPlayer
    {
        public const string ResourceFolder = "Audio/CityMusic";
        public const string TrackName = "city_theme";
        public const string ResourcePath = ResourceFolder + "/" + TrackName;
        public const float ThemeOutputVolume =
            MusicMix.CityOutputVolume;

        protected override string TrackResourcePath => ResourcePath;
        protected override float OutputVolume => ThemeOutputVolume;
    }
}

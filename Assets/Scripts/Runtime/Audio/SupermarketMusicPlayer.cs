namespace BarPromenade
{
    public sealed class SupermarketMusicPlayer : SceneMusicPlayer
    {
        public const string ResourceFolder =
            "Audio/SupermarketMusic";
        public const string TrackName = "supermarket_theme";
        public const string ResourcePath =
            ResourceFolder + "/" + TrackName;
        public const float ThemeOutputVolume =
            MusicMix.SupermarketOutputVolume;

        protected override string TrackResourcePath =>
            ResourcePath;
        protected override float OutputVolume => ThemeOutputVolume;
    }
}

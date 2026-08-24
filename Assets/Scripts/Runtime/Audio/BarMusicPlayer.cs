namespace BarPromenade
{
    public sealed class BarMusicPlayer : SceneMusicPlayer
    {
        public const string ResourceFolder = "Audio/BarMusic";
        public const string TrackName = "bar_theme";
        public const string ResourcePath = ResourceFolder + "/" + TrackName;
        public const float ThemeOutputVolume =
            MusicMix.BarOutputVolume;

        protected override string TrackResourcePath => ResourcePath;
        protected override float OutputVolume => ThemeOutputVolume;
    }
}

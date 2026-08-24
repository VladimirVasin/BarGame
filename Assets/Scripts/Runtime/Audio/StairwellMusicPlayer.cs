namespace BarPromenade
{
    public sealed class StairwellMusicPlayer : SceneMusicPlayer
    {
        public const string ResourceFolder =
            "Audio/StairwellMusic";
        public const string TrackName = "stairwell_theme";
        public const string ResourcePath =
            ResourceFolder + "/" + TrackName;
        public const float ThemeOutputVolume =
            MusicMix.StairwellOutputVolume;

        protected override string TrackResourcePath =>
            ResourcePath;
        protected override float OutputVolume => ThemeOutputVolume;
    }
}

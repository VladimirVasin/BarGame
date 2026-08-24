namespace BarPromenade
{
    /// <summary>
    /// The optional theme of a place rather than of a scene. It lives in the
    /// City root beside `city_theme` and is parked silent until the hero is
    /// standing on the cemetery grounds.
    /// </summary>
    public sealed class CemeteryMusicPlayer : SceneMusicPlayer
    {
        public const string ResourceFolder = "Audio/CemeteryMusic";
        public const string TrackName = "cemetery_theme";
        public const string ResourcePath =
            ResourceFolder + "/" + TrackName;
        public const float ThemeOutputVolume =
            MusicMix.CemeteryOutputVolume;

        protected override string TrackResourcePath => ResourcePath;
        protected override float OutputVolume => ThemeOutputVolume;
    }
}

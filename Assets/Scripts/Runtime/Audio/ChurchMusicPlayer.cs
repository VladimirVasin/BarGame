namespace BarPromenade
{
    /// <summary>
    /// The theme of the church interior. A scene theme rather than a
    /// theme of a place: the church is its own scene, so this behaves
    /// like the bar's and the supermarket's — it starts when the hero
    /// walks in through the west door and hands its tail to the mix when
    /// he leaves — and not like <see cref="CemeteryMusicPlayer"/>, which
    /// waits silent inside City for him to cross onto the grounds.
    ///
    /// Nothing here is required for the scene to work. Until a
    /// `church_theme` file exists the player loads nothing, reports
    /// <see cref="SceneMusicPlaybackState.Unavailable"/> and the church
    /// is simply as quiet as it has always been.
    /// </summary>
    public sealed class ChurchMusicPlayer : SceneMusicPlayer
    {
        public const string ResourceFolder = "Audio/ChurchMusic";
        public const string TrackName = "church_theme";
        public const string ResourcePath =
            ResourceFolder + "/" + TrackName;
        public const float ThemeOutputVolume =
            MusicMix.ChurchOutputVolume;

        protected override string TrackResourcePath => ResourcePath;
        protected override float OutputVolume => ThemeOutputVolume;
    }
}

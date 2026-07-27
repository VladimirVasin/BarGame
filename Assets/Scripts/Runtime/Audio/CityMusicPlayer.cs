namespace BarPromenade
{
    public sealed class CityMusicPlayer : SceneMusicPlayer
    {
        public const string ResourceFolder = "Audio/CityMusic";
        public const string TrackName = "city_theme";
        public const string ResourcePath = ResourceFolder + "/" + TrackName;

        protected override string TrackResourcePath => ResourcePath;
    }
}

namespace BarPromenade
{
    public sealed class AlpineVillageMusicPlayer : SceneMusicPlayer
    {
        public const string ResourceFolder = "Audio/AlpineVillageMusic";
        public const string TrackName = "alpine_village_theme";
        public const string ResourcePath = ResourceFolder + "/" + TrackName;
        // The village theme is deliberately louder than the shared default.
        public const float ThemeOutputVolume = 1.00f;

        private AlpineVillageRoot village;

        protected override string TrackResourcePath => ResourcePath;
        protected override bool InitiallySuppressed => true;
        protected override float OutputVolume => ThemeOutputVolume;

        public void Initialize(AlpineVillageRoot owner)
        {
            village = owner != null ? owner : throw new System.ArgumentNullException(nameof(owner));
            SetPlaybackSuppressed(!IsInsideClosedLodge);
        }

        private bool IsInsideClosedLodge =>
            village != null && !village.IsDormant && village.LodgeShelter != null &&
            village.Player.GameObject != null &&
            !LodgeShelterSessionState.LeftDoorOpen && !LodgeShelterSessionState.RightDoorOpen &&
            village.LodgeShelter.ContainsInterior(village.Player.GameObject.transform.position);

        protected override void Update()
        {
            // Gate before loading/advancing. Closing the doors from outside
            // must not carry the lodge's theme into the rest of the village.
            // The shared player owns fades, pause/resume and scene-exit tails.
            SetPlaybackSuppressed(!IsInsideClosedLodge);
            base.Update();
        }
    }
}

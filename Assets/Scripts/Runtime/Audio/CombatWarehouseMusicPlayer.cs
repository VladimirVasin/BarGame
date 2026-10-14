using System;

namespace BarPromenade
{
    /// <summary>Optional warehouse theme on the shared scene music carrier.</summary>
    public sealed class CombatWarehouseMusicPlayer : SceneMusicPlayer
    {
        public const string ResourceFolder = "Audio/CombatWarehouseMusic";
        public const string TrackName = "warehouse_theme";
        public const string ResourcePath = ResourceFolder + "/" + TrackName;
        public const float PlaybackSpeed = .8f;

        private CombatTestRoot warehouse;

        protected override string TrackResourcePath => ResourcePath;
        protected override bool InitiallySuppressed => true;
        protected override float SuppressionFadeOutSeconds => 0f;

        protected override void Awake()
        {
            base.Awake();
            Source.pitch = PlaybackSpeed;
        }

        protected override void PrepareForPlayback() => Source.pitch = PlaybackSpeed;

        public void Initialize(CombatTestRoot owner)
        {
            warehouse = owner != null ? owner : throw new ArgumentNullException(nameof(owner));
            RefreshOwnerGate();
        }

        private void RefreshOwnerGate()
        {
            // A scene-exit tail has already detached from the owner's hierarchy.
            // The shared player owns that lifetime and ignores suppression then.
            SetPlaybackSuppressed(warehouse == null || !warehouse.isActiveAndEnabled);
        }

        protected override void Update()
        {
            RefreshOwnerGate();
            base.Update();
        }
    }
}

using UnityEngine;

namespace BarPromenade
{
    public sealed partial class CombatTestRoot
    {
        public CombatWarehouseMusicPlayer Music { get; private set; }
        public CombatWarehouseAmbiencePlayer Ambience { get; private set; }
        private GameAudioProfile previousWarehouseAudioProfile;
        private bool warehouseAudioOwned;

        private void AcquireWarehouseAcoustics()
        {
            if (WarehousePlan == null || warehouseAudioOwned) return;
            previousWarehouseAudioProfile = GameAudioMixer.CurrentProfile;
            warehouseAudioOwned = GameAudioMixer.ApplyProfile(GameAudioProfile.CombatWarehouse);
            RetroAudioService.EnsureInstalled().WarmWarehouseClips();
        }

        private void ReleaseWarehouseAcoustics()
        {
            if (!warehouseAudioOwned) return;
            warehouseAudioOwned = false;
            // A newly loaded area's profile already owns the mixer; an old root
            // must not replace it during Single-load teardown.
            if (GameAudioMixer.CurrentProfile != GameAudioProfile.CombatWarehouse) return;
            GameAudioProfile restore = previousWarehouseAudioProfile > GameAudioProfile.None &&
                previousWarehouseAudioProfile < GameAudioProfile.Count &&
                previousWarehouseAudioProfile != GameAudioProfile.CombatWarehouse
                ? previousWarehouseAudioProfile : GameAudioProfile.City;
            GameAudioMixer.ApplyProfile(restore);
        }

        private void InitializeWarehouseAudio()
        {
            if (WarehousePlan == null) return;
            // Keep the carrier outside combatLocation: resets and a replacement
            // layout retain the theme's playhead instead of starting another one.
            var carrier = new GameObject("Warehouse Music");
            carrier.transform.SetParent(transform, false);
            Music = carrier.AddComponent<CombatWarehouseMusicPlayer>();
            Music.Initialize(this);
            var ambienceCarrier = new GameObject("Warehouse Post Fan Ambience");
            ambienceCarrier.transform.SetParent(transform, false);
            Ambience = ambienceCarrier.AddComponent<CombatWarehouseAmbiencePlayer>();
            Ambience.Initialize(this);
        }

        private void SuspendWarehouseAudio()
        {
            // Disabling just this root component leaves child objects enabled.
            // The player's gate restores playback if the root resumes later.
            Music?.SetPlaybackSuppressed(true);
            Ambience?.Advance(0f);
        }
    }
}

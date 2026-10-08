using UnityEngine;

namespace BarPromenade
{
    public sealed partial class CombatActor
    {
        private readonly struct ReloadCue
        {
            public ReloadCue(float seconds, RetroSfxId sound, bool atHand = false)
            { Seconds = seconds; Sound = sound; AtHand = atHand; }
            public readonly float Seconds;
            public readonly RetroSfxId Sound;
            public readonly bool AtHand;
        }

        private static readonly ReloadCue[] PistolReloadCues =
        {
            new ReloadCue(.25f, RetroSfxId.PistolMagazineLatch),
            new ReloadCue(.30f, RetroSfxId.PistolMagazineRemove, true),
            new ReloadCue(.75f, RetroSfxId.PistolMagazineStow, true),
            new ReloadCue(.9f, RetroSfxId.PistolMagazineDraw, true),
            new ReloadCue(1.15f, RetroSfxId.PistolMagazineInsert, true),
            new ReloadCue(1.3f, RetroSfxId.PistolMagazineSeat),
            new ReloadCue(1.42f, RetroSfxId.PistolSlidePull),
            new ReloadCue(1.58f, RetroSfxId.PistolSlideRelease),
            new ReloadCue(1.8f, RetroSfxId.PistolReady)
        };
        internal int PistolReloadCueCount { get; private set; }
        internal RetroSfxId LastPistolReloadCue { get; private set; }
        private int pistolNextReloadCue;
        private float pistolReloadAudioClock;

        private void BeginPistolReloadAudio(bool resuming)
        {
            if (resuming) return;
            pistolNextReloadCue = 0;
            pistolReloadAudioClock = 0f;
        }

        private void AdvancePistolReloadAudio(bool wasReloading, float before)
        {
            if (!wasReloading) return;
            // Also handle an isolated rules-driven restart. A suspended reload
            // keeps the same clock and cursor, so it never repeats old actions.
            if (before + .000001f < pistolReloadAudioClock) BeginPistolReloadAudio(false);
            float through = Pistol.IsReloading ? Pistol.ReloadProgress * 1.8f : 1.8f;
            while (pistolNextReloadCue < PistolReloadCues.Length &&
                through + .000001f >= PistolReloadCues[pistolNextReloadCue].Seconds)
            {
                ReloadCue cue = PistolReloadCues[pistolNextReloadCue++];
                Vector3 position = cue.AtHand ? handPose.CylinderCentre(true) :
                    cue.Sound == RetroSfxId.PistolSlidePull || cue.Sound == RetroSfxId.PistolSlideRelease
                        ? PistolSlidePull.position : PistolMagazineSeat.position;
                RetroAudio.PlayAt(cue.Sound, position);
                PistolReloadCueCount++;
                LastPistolReloadCue = cue.Sound;
            }
            pistolReloadAudioClock = through;
        }

        private void ResetPistolReloadAudio()
        {
            pistolNextReloadCue = PistolReloadCueCount = 0;
            pistolReloadAudioClock = 0f;
            LastPistolReloadCue = RetroSfxId.None;
        }
    }
}

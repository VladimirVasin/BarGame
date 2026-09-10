using UnityEngine;

namespace BarPromenade
{
    /// <summary>Local machinery keeps a directional dry signal ahead of its
    /// short room response. No listener-wide zone or extra ambience bed.</summary>
    public static class CityWorkAudio
    {
        public static void Configure(AudioSource source, bool interior, float cutoff, float radius)
        {
            source.spatialBlend = 1f;
            source.panStereo = 0f;
            source.spread = 0f;
            source.dopplerLevel = 0f;
            source.minDistance = interior ? 2f : 3f;
            source.maxDistance = radius;
            source.rolloffMode = AudioRolloffMode.Linear;
            source.priority = 144;
            source.reverbZoneMix = 0f;
            GameAudioMixer.Route(source, GameAudioGroup.SfxWorld);
            var filter = source.gameObject.AddComponent<AudioLowPassFilter>();
            filter.cutoffFrequency = cutoff;
            filter.lowpassResonanceQ = 1f;
            var room = source.gameObject.AddComponent<AudioReverbFilter>();
            room.reverbPreset = AudioReverbPreset.User;
            room.dryLevel = 0f;
            room.room = interior ? -1400f : -2400f;
            room.roomHF = interior ? -2000f : -3000f;
            room.decayTime = interior ? .95f : .28f;
            room.decayHFRatio = interior ? .58f : .45f;
            room.reflectionsLevel = interior ? -1600f : -2400f;
            room.reflectionsDelay = interior ? .014f : .008f;
            room.reverbLevel = interior ? -1400f : -2200f;
            room.reverbDelay = interior ? .026f : .018f;
            room.diffusion = interior ? 75f : 45f;
            room.density = interior ? 70f : 35f;
        }
    }
}

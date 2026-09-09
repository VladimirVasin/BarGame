using System;
using UnityEngine;

namespace BarPromenade
{
    /// <summary>
    /// Folds the car speaker to mono and adds a quiet band-limited circuit hiss
    /// in the same source, so power, gain, pause and distance affect both.
    /// No allocations or Unity object access on the audio thread.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class LastRouteRadioSpeakerTexture : MonoBehaviour
    {
        private uint noiseState = 0x72e84a31u;
        private float lowNoise;
        private float bassNoise;
        private float lowCoefficient;
        private float bassCoefficient;
        private volatile float hissGain;

        public void SetHissGain(float gain)
        {
            hissGain = gain;
        }

        private void Awake()
        {
            int rate = Math.Max(8000, AudioSettings.outputSampleRate);
            lowCoefficient = 1f - (float)Math.Exp(-2.0 * Math.PI * 3500.0 / rate);
            bassCoefficient = 1f - (float)Math.Exp(-2.0 * Math.PI * 180.0 / rate);
        }

        private void OnAudioFilterRead(float[] data, int channels)
        {
            Process(data, channels);
        }

        internal void Process(float[] data, int channels)
        {
            if (channels <= 0)
            {
                return;
            }

            for (int frame = 0; frame + channels <= data.Length; frame += channels)
            {
                float mono = 0f;
                for (int channel = 0; channel < channels; channel++)
                {
                    mono += data[frame + channel];
                }

                noiseState ^= noiseState << 13;
                noiseState ^= noiseState >> 17;
                noiseState ^= noiseState << 5;
                float noise = (noiseState & 0xffff) / 32767.5f - 1f;
                lowNoise += lowCoefficient * (noise - lowNoise);
                bassNoise += bassCoefficient * (lowNoise - bassNoise);
                mono = mono / channels + (lowNoise - bassNoise) * 0.0018f * hissGain;
                for (int channel = 0; channel < channels; channel++)
                {
                    data[frame + channel] = mono;
                }
            }
        }
    }
}

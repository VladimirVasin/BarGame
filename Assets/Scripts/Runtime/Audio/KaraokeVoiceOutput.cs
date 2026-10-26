using System;
using System.Threading;
using UnityEngine;

namespace BarPromenade
{
    /// <summary>Runs on the actual source, before its low-pass, without clip prefetch.</summary>
    [DisallowMultipleComponent]
    public sealed class KaraokeVoiceOutput : MonoBehaviour
    {
        private Action<float[], int> renderer;
        internal void Bind(Action<float[], int> render) => Volatile.Write(ref renderer, render);

        private void OnAudioFilterRead(float[] data, int channels)
        {
            Action<float[], int> render = Volatile.Read(ref renderer);
            if (render == null) Array.Clear(data, 0, data.Length);
            else render(data, channels);
        }
    }
}

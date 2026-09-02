using System;
using System.Collections.Generic;
using UnityEngine;

namespace BarPromenade
{
    /// <summary>
    /// A refcounted single copy of the eight keystroke clips, one per
    /// authored voice in <see cref="NpcVoiceCatalog"/>.
    ///
    /// This is <see cref="RavenCallClipCache"/>'s pattern applied to a
    /// much smaller bank: eight clips of about a thousand samples is
    /// roughly `32 KB` for every line anybody says in the game, and the
    /// per-letter melody lives entirely in the source's pitch, so there
    /// is nothing per-speaker left to bake into a buffer.
    ///
    /// DISPOSAL CONTRACT — the same as the raven cache's: hold the
    /// lease for as long as anything plays its clips, and dispose the
    /// players first. Disposing a lease twice is a no-op by design
    /// rather than by caller discipline, and a fresh Acquire after the
    /// last release regenerates the bank, so an already-disposed lease
    /// must never be reused as a clip source. A domain reload resets
    /// the refcount with the rest of the statics.
    /// </summary>
    public static class NpcSpeechBlipClipCache
    {
        private static AudioClip[] sharedClips;
        private static int leaseCount;

        public static int LiveLeaseCount => leaseCount;

        /// <summary>
        /// Takes one lease, generating the bank if this is the first
        /// live one. The clips carry DontSave from the synthesis
        /// itself, so they can never be serialized into a scene no
        /// matter who holds them.
        /// </summary>
        public static Lease Acquire()
        {
            if (leaseCount == 0)
            {
                sharedClips = new AudioClip[NpcVoiceCatalog.Count];
                for (int index = 0;
                     index < sharedClips.Length;
                     index++)
                {
                    sharedClips[index] =
                        NpcSpeechBlipSynthesis.CreateRuntimeClip(
                            NpcVoiceCatalog.ProfileAt(index));
                }
            }

            leaseCount++;
            return new Lease(sharedClips);
        }

        /// <summary>
        /// The last release destroys by hand with the play/edit branch
        /// the whole synthesized-audio family uses: EditMode has no
        /// deferred frame for a queued Destroy to run in, and the leak
        /// guard would trip on every test otherwise.
        /// </summary>
        private static void Release()
        {
            leaseCount--;
            if (leaseCount > 0)
            {
                return;
            }

            leaseCount = 0;
            AudioClip[] clips = sharedClips;
            sharedClips = null;
            if (clips == null)
            {
                return;
            }

            for (int index = 0; index < clips.Length; index++)
            {
                AudioClip clip = clips[index];
                if (clip == null)
                {
                    continue;
                }

                if (Application.isPlaying)
                {
                    UnityEngine.Object.Destroy(clip);
                }
                else
                {
                    UnityEngine.Object.DestroyImmediate(clip);
                }

                clips[index] = null;
            }
        }

        [RuntimeInitializeOnLoadMethod(
            RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            sharedClips = null;
            leaseCount = 0;
        }

        /// <summary>One consumer's hold on the shared bank.</summary>
        public sealed class Lease : IDisposable
        {
            private readonly AudioClip[] clips;
            private bool disposed;

            internal Lease(AudioClip[] configuredClips)
            {
                clips = configuredClips;
            }

            /// <summary>The shared bank, indexed by catalog ordinal —
            /// the same instances for every lease alive at once, and
            /// dead the moment the last one is disposed.</summary>
            public IReadOnlyList<AudioClip> Clips => clips;

            public AudioClip ClipAt(int ordinal)
            {
                return clips == null ||
                       ordinal < 0 ||
                       ordinal >= clips.Length
                    ? null
                    : clips[ordinal];
            }

            public void Dispose()
            {
                if (disposed)
                {
                    return;
                }

                disposed = true;
                Release();
            }
        }
    }
}

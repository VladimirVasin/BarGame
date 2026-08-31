using System;
using System.Collections.Generic;
using UnityEngine;

namespace BarPromenade
{
    /// <summary>
    /// A refcounted single copy of the three raven caw clips.
    ///
    /// <see cref="CemeteryRavenCallSynthesis.GenerateCaw"/> takes no
    /// per-voice entropy: every voice that generates its own clips
    /// produces three byte-identical buffers, ~146 KB a voice, and
    /// the outdoor roosts put dozens of voices in a scene. All the
    /// per-voice character lives in the seeded variant and interval
    /// schedules, so the AUDIO memory can be one shared set: each
    /// roost controller takes one <see cref="Lease"/>, hands the
    /// leased clips to its voices through the shared-clips
    /// <c>CemeteryRavenVoice.Create</c> overload, and the clips are
    /// generated on the first lease and destroyed on the last. The
    /// cemetery pair keeps its own private clips — shipped behavior,
    /// untouched.
    ///
    /// DISPOSAL CONTRACT — voices before lease: a consumer must hold
    /// its lease for its voices' whole lifetime and dispose every
    /// voice BEFORE disposing the lease, because the last lease's
    /// disposal destroys the shared clips out from under any voice
    /// still holding them. Disposing a lease twice is a no-op, and a
    /// fresh Acquire after the last release regenerates the clips
    /// from scratch — which is also why a stale, already-disposed
    /// lease must never be reused as a clip source. A domain reload
    /// resets the refcount with the rest of the statics.
    /// </summary>
    public static class RavenCallClipCache
    {
        private static AudioClip[] sharedClips;
        private static int leaseCount;

        /// <summary>
        /// Takes one lease on the shared clip set, generating the
        /// clips if this is the first live lease. The clips carry
        /// DontSave from the synthesis itself, so they can never be
        /// serialized into a scene no matter who holds them.
        /// </summary>
        public static Lease Acquire()
        {
            if (leaseCount == 0)
            {
                sharedClips = new AudioClip[
                    CemeteryRavenCallSynthesis.VariantCount];
                for (int variant = 0;
                     variant < sharedClips.Length;
                     variant++)
                {
                    sharedClips[variant] =
                        CemeteryRavenCallSynthesis.CreateRuntimeClip(
                            variant);
                }
            }

            leaseCount++;
            return new Lease(sharedClips);
        }

        /// <summary>
        /// The last release destroys by hand with the play/edit
        /// branch <c>CemeteryRavenVoice.Dispose</c> uses, because
        /// EditMode has no deferred frame for a queued Destroy to
        /// run in and the EditMode leak guard would trip on every
        /// test otherwise.
        /// </summary>
        private static void Release()
        {
            leaseCount--;
            if (leaseCount > 0)
            {
                return;
            }

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

        /// <summary>
        /// One consumer's hold on the shared clips. Dispose exactly
        /// once when every voice built over <see cref="Clips"/> is
        /// already disposed; a second Dispose is a no-op by design
        /// rather than by caller discipline, so a teardown that runs
        /// twice cannot underflow the refcount and bury clips a
        /// still-live consumer is playing.
        /// </summary>
        public sealed class Lease : IDisposable
        {
            private readonly AudioClip[] clips;
            private bool disposed;

            internal Lease(AudioClip[] configuredClips)
            {
                clips = configuredClips;
            }

            /// <summary>The shared clip set, one clip per caw
            /// variant — the same instances for every lease alive at
            /// once. Dead the moment the last lease is disposed.
            /// </summary>
            public IReadOnlyList<AudioClip> Clips => clips;

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

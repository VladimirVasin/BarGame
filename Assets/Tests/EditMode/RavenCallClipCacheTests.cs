using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;

namespace BarPromenade.Tests.EditMode
{
    /// <summary>
    /// Holds the refcounted caw-clip cache to an honest ledger. The
    /// caw synthesis takes no per-voice entropy, so every voice's
    /// three clips are byte-identical and the cache's whole point is
    /// ONE shared set: two leases must expose the same instances,
    /// clips must outlive any single lease and die with the last
    /// one (through the edit-mode DestroyImmediate branch — these
    /// tests run with no deferred frame), a double dispose must be
    /// a no-op instead of an underflow that buries clips a live
    /// consumer is playing, and a fresh Acquire after the last
    /// release must regenerate from scratch. The voice half pins
    /// the ownership boundary: a shared-clips voice's Dispose
    /// leaves the cache's clips alone, while the shipped 2-arg
    /// voice still destroys the private set it generated.
    /// </summary>
    public sealed class RavenCallClipCacheTests
    {
        [Test]
        public void Leases_ShareOneClipSetAndTheLastDisposeBuriesIt()
        {
            RavenCallClipCache.Lease first =
                RavenCallClipCache.Acquire();
            RavenCallClipCache.Lease second =
                RavenCallClipCache.Acquire();
            var held = new List<AudioClip>();
            try
            {
                Assert.That(
                    first.Clips.Count,
                    Is.EqualTo(
                        CemeteryRavenCallSynthesis.VariantCount));
                for (int variant = 0;
                     variant < first.Clips.Count;
                     variant++)
                {
                    Assert.That(
                        first.Clips[variant] == null,
                        Is.False,
                        "variant " + variant);
                    // The same INSTANCES, not equal copies — the
                    // memory saving is the cache's whole point.
                    Assert.That(
                        second.Clips[variant],
                        Is.SameAs(first.Clips[variant]),
                        "variant " + variant);
                    held.Add(first.Clips[variant]);
                }
            }
            finally
            {
                // Disposing ONE of two leases must keep the clips
                // alive: the other lease's voices still play them.
                first.Dispose();
            }

            for (int variant = 0; variant < held.Count; variant++)
            {
                Assert.That(
                    held[variant] == null,
                    Is.False,
                    "variant " + variant +
                    " died with the first of two leases.");
            }

            // The LAST dispose destroys, immediately — the
            // edit-mode branch, or this very test would leak.
            second.Dispose();
            for (int variant = 0; variant < held.Count; variant++)
            {
                Assert.That(
                    held[variant] == null,
                    Is.True,
                    "variant " + variant +
                    " survived the last lease.");
            }

            // A second Dispose is a no-op by design: it must not
            // underflow the refcount underneath the NEXT consumer.
            second.Dispose();
            RavenCallClipCache.Lease reborn =
                RavenCallClipCache.Acquire();
            try
            {
                Assert.That(reborn.Clips[0] == null, Is.False,
                    "A fresh Acquire after the last release must " +
                    "regenerate the clips.");
                Assert.That(
                    ReferenceEquals(reborn.Clips[0], held[0]),
                    Is.False,
                    "Regeneration means new instances, never a " +
                    "resurrected corpse.");

                // The stale leases' extra disposes change nothing
                // for the live one.
                first.Dispose();
                second.Dispose();
                Assert.That(reborn.Clips[0] == null, Is.False);
            }
            finally
            {
                reborn.Dispose();
            }

            Assert.That(reborn.Clips[0] == null, Is.True);
        }

        [Test]
        public void Voice_OwnershipBoundaryDecidesWhoBuriesClips()
        {
            var sharedHost = new GameObject(
                "Roost Voice Test Shared Host");
            var ownedHost = new GameObject(
                "Roost Voice Test Owned Host");
            RavenCallClipCache.Lease lease =
                RavenCallClipCache.Acquire();
            try
            {
                // A leased voice plays the cache's clips but never
                // owns them: its Dispose tears down its own
                // GameObject and leaves the shared set for the
                // other voices still holding it.
                CemeteryRavenVoice sharedVoice =
                    CemeteryRavenVoice.Create(
                        sharedHost.transform,
                        7,
                        lease.Clips);
                sharedVoice.Dispose();
                for (int variant = 0;
                     variant < lease.Clips.Count;
                     variant++)
                {
                    Assert.That(
                        lease.Clips[variant] == null,
                        Is.False,
                        "A shared-clips voice buried the cache's " +
                        "clip " + variant + ".");
                }

                // The shipped 2-arg voice keeps its shipped
                // semantics: it generated its own three clips and
                // its Dispose destroys them.
                CemeteryRavenVoice ownedVoice =
                    CemeteryRavenVoice.Create(
                        ownedHost.transform,
                        7);
                var ownedClips = new List<AudioClip>();
                var clipsField = typeof(CemeteryRavenVoice)
                    .GetField(
                        "clips",
                        System.Reflection.BindingFlags.NonPublic |
                        System.Reflection.BindingFlags.Instance);
                Assert.That(clipsField, Is.Not.Null);
                var privateClips =
                    (AudioClip[])clipsField.GetValue(ownedVoice);
                for (int variant = 0;
                     variant < privateClips.Length;
                     variant++)
                {
                    Assert.That(
                        privateClips[variant] == null,
                        Is.False);
                    Assert.That(
                        ReferenceEquals(
                            privateClips[variant],
                            lease.Clips[variant]),
                        Is.False,
                        "The unleased voice must generate its own " +
                        "clips, never borrow the cache's.");
                    ownedClips.Add(privateClips[variant]);
                }

                ownedVoice.Dispose();
                for (int variant = 0;
                     variant < ownedClips.Count;
                     variant++)
                {
                    Assert.That(
                        ownedClips[variant] == null,
                        Is.True,
                        "An owned clip survived its voice.");
                }

                // And the cache's clips are still untouched by any
                // of it.
                Assert.That(lease.Clips[0] == null, Is.False);
            }
            finally
            {
                lease.Dispose();
                Object.DestroyImmediate(sharedHost);
                Object.DestroyImmediate(ownedHost);
            }
        }
    }
}

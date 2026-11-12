using System;
using UnityEngine;

namespace BarPromenade
{
    /// <summary>Street actions baked on the model's own rest rig.</summary>
    [DisallowMultipleComponent]
    public sealed class DefaultNpcStreetClips : MonoBehaviour
    {
        [SerializeField] private AnimationClip sit, guard, shove;

        public AnimationClip Sit => sit;
        public AnimationClip Guard => guard;
        public AnimationClip Shove => shove;

        public void Configure(AnimationClip seated, AnimationClip palm, AnimationClip push)
        {
            if (seated == null || palm == null || push == null || !seated.isLooping || palm.isLooping || push.isLooping)
                throw new ArgumentException("Street actions require a seated loop and two one-shot gestures.");
            sit = seated;
            guard = palm;
            shove = push;
        }
    }
}

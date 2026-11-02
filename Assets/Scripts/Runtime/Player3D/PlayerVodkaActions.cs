using System;
using UnityEngine;

namespace BarPromenade
{
    /// <summary>Optional authored bottle actions; the same rig retains ordinary gait below the torso mask.</summary>
    public static class PlayerVodkaActions
    {
        public const string ResourcePath = "Player/PlayerVodkaActions";
        public const string CarryClip = "VodkaCarry", DrawClip = "VodkaDraw", OpenClip = "VodkaOpen", SipClip = "VodkaSip",
            StowClip = "VodkaStow", ThrowClip = "VodkaThrow";
        public const float CarrySeconds = 1f, DrawSeconds = 1.5f, OpenSeconds = 1.25f, SipSeconds = 2f,
            StowSeconds = .75f, ThrowSeconds = 1f;
        public const float SipCommitProgress = .5f, ThrowReleaseProgress = .6f,
            DrawTransferProgress = .48f / 1.70f, CapContactProgress = .448f,
            CapTwistEndProgress = .664f, CapOpenProgress = .70f, CapReleaseProgress = .90f;
        public static readonly string[] ClipNames = { CarryClip, DrawClip, OpenClip, SipClip, StowClip, ThrowClip };
        public static readonly Vector3 DrawNeckSource = new Vector3(.08f, -.17f, 1.32f);
        private static AnimationClip[] clips;

        public static bool IsLoop(string name) => name == CarryClip;
        public static float Duration(string name) => name switch
        {
            CarryClip => CarrySeconds, DrawClip => DrawSeconds, OpenClip => OpenSeconds, SipClip => SipSeconds,
            StowClip => StowSeconds, ThrowClip => ThrowSeconds,
            _ => throw new ArgumentOutOfRangeException(nameof(name))
        };

        /// <summary>Preflight all assets before registering any action or allowing a sip transaction.</summary>
        public static bool TryAttach(Player3DAssetRegistry registry)
        {
            if (registry == null) return false;
            clips ??= Resources.LoadAll<AnimationClip>(ResourcePath);
            foreach (string name in ClipNames)
            {
                AnimationClip clip = Find(name);
                if (clip == null || clip.isLooping != IsLoop(name) || clip.events.Length != 0 ||
                    Mathf.Abs(clip.length - Duration(name)) > .003f) return false;
            }
            foreach (string name in ClipNames)
            {
                AnimationClip clip = Find(name);
                registry.RegisterRuntimeAnimation(new Player3DAnimationBinding(name, "vodka_equipment", clip, Duration(name), IsLoop(name)));
            }
            return true;
        }

        private static AnimationClip Find(string name)
        {
            foreach (AnimationClip clip in clips) if (clip != null && clip.name == name) return clip;
            return null;
        }
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetCache() => clips = null;
    }
}

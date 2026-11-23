using System;
using System.Collections.Generic;
using UnityEngine;

namespace BarPromenade
{
    [Serializable]
    public sealed class CafeRoleAnimations
    {
        [SerializeField] private MountainRoadCafeCastRole role;
        [SerializeField] private MountainRoadCafeCastClipBinding[] clips;
        public MountainRoadCafeCastRole Role => role;
        public IReadOnlyList<MountainRoadCafeCastClipBinding> Clips => clips;

        public CafeRoleAnimations(MountainRoadCafeCastRole role, MountainRoadCafeCastClipBinding[] clips)
        {
            this.role = role;
            this.clips = clips;
        }

        public bool TryGetClip(MountainRoadCafeCastClipKind kind, out AnimationClip clip, out bool loop)
        {
            foreach (var binding in clips)
                if (binding.Kind == kind)
                {
                    clip = binding.Clip;
                    loop = binding.Loop;
                    return clip != null;
                }
            clip = null;
            loop = false;
            return false;
        }
    }

    /// <summary>Shares the cafe's bone animations with the bar, independently of any body prefab.</summary>
    public sealed class MountainRoadCafeCastProvider : ScriptableObject
    {
        public const string ResourcePath = "MountainRoad/MountainRoadCafeCastProvider";
        [SerializeField] private CafeRoleAnimations[] roles = Array.Empty<CafeRoleAnimations>();
        public bool HasCompleteCast => roles.Length == 4;
        public static MountainRoadCafeCastProvider Load() => Resources.Load<MountainRoadCafeCastProvider>(ResourcePath);
        public void Configure(CafeRoleAnimations[] animations) => roles = animations;
        public CafeRoleAnimations ForRole(MountainRoadCafeCastRole role)
        {
            foreach (var entry in roles)
                if (entry.Role == role) return entry;
            throw new InvalidOperationException("Missing cafe animation set for " + role);
        }
    }
}

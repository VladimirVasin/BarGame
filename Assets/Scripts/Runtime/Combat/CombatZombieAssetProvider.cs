using System;
using System.Collections.Generic;
using UnityEngine;

namespace BarPromenade
{
    /// <summary>Bounded CombatTest-only bank on the retained ordinary worker skeleton.</summary>
    public static class CombatZombieAssetProvider
    {
        public const string ResourceFolder = "Combat/";
        public const string IdleClip = "ZombieIdle", WalkClip = "ZombieWalk",
            AttackRightClip = "ZombieAttackRight", AttackLeftClip = "ZombieAttackLeft", HitClip = "ZombieHit";
        public const float WalkCycleDistance = 1.2f;
        public static readonly string[] ClipNames = { IdleClip, WalkClip, AttackRightClip, AttackLeftClip, HitClip };
        private static readonly Dictionary<string, AnimationClip> Clips = new Dictionary<string, AnimationClip>(5);
        private static readonly Texture2D[] Faces = new Texture2D[3];
        private static AudioClip groan, lunge;
        private static bool loaded;

        public static bool IsLoop(string name) => name == IdleClip || name == WalkClip;
        public static float ClipDuration(string name) => name switch
        {
            IdleClip => 4f, WalkClip => 1.6f,
            AttackRightClip or AttackLeftClip => 1.77f, HitClip => .45f,
            _ => throw new ArgumentOutOfRangeException(nameof(name), name, "Unknown zombie action.")
        };

        public static AnimationClip LoadClip(string name)
        {
            LoadBank();
            if (!Clips.TryGetValue(name, out AnimationClip clip) || clip == null)
                throw new InvalidOperationException("Missing authored zombie action " + name);
            return clip;
        }

        private static void LoadBank()
        {
            if (loaded) return;
            foreach (AnimationClip clip in Resources.LoadAll<AnimationClip>(ResourceFolder + "ZombieActions"))
            {
                if (Array.IndexOf(ClipNames, clip.name) < 0) continue;
                if (clip.events.Length != 0 || clip.isLooping != IsLoop(clip.name) ||
                    Mathf.Abs(clip.length - ClipDuration(clip.name)) > .003f)
                    throw new InvalidOperationException("Invalid in-place zombie action " + clip.name);
                Clips.Add(clip.name, clip);
            }
            if (Clips.Count != ClipNames.Length) throw new InvalidOperationException("Zombie action bank is incomplete.");
            loaded = true;
        }

        public static Texture2D LoadFace(int variant)
        {
            if ((uint)variant >= Faces.Length) throw new ArgumentOutOfRangeException(nameof(variant));
            if (Faces[variant] == null) Faces[variant] = Resources.Load<Texture2D>(ResourceFolder + "ZombieFace" + (variant + 1));
            if (Faces[variant] == null) throw new InvalidOperationException("Missing authored corpse palette.");
            return Faces[variant];
        }

        /// <summary>Apply once per permanent slot; pooled resets retain the same civilian corpse.</summary>
        public static void ApplyAppearance(GameObject npc, int variant)
        {
            if (npc == null) throw new ArgumentNullException(nameof(npc));
            var appearance = npc.GetComponent<CombatZombieAppearance>() ?? npc.AddComponent<CombatZombieAppearance>();
            appearance.Configure(((variant % 3) + 3) % 3);
        }

        public static AudioClip LoadGroan() => groan != null ? groan : groan = RequireAudio("ZombieGroan");
        public static AudioClip LoadLunge() => lunge != null ? lunge : lunge = RequireAudio("ZombieLunge");
        private static AudioClip RequireAudio(string name) => Resources.Load<AudioClip>(ResourceFolder + name)
            ?? throw new InvalidOperationException("Missing authored zombie sound " + name);

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetCache()
        {
            Clips.Clear(); Array.Clear(Faces, 0, Faces.Length);
            loaded = false; groan = lunge = null;
        }
    }
}

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
            AttackRightClip = "ZombieAttackRight", AttackLeftClip = "ZombieAttackLeft", HitClip = "ZombieHit",
            LoadAdjustClip = "ZombieLoadAdjust", WatchClip = "ZombieWatch";
        public const float WalkCycleDistance = .72f, WalkCycleSeconds = 1.6f;
        private static readonly Vector2[] WalkTravelKeys =
        {
            new Vector2(0f, 0f), new Vector2(.10f, .03f), new Vector2(.30f, .17f),
            new Vector2(.42f, .44f), new Vector2(.50f, .50f), new Vector2(.60f, .53f),
            new Vector2(.80f, .67f), new Vector2(.92f, .94f), new Vector2(1f, 1f)
        };

        internal static float WalkTravel(float seconds)
        {
            float cycles = seconds / WalkCycleSeconds, whole = Mathf.Floor(cycles), phase = cycles - whole;
            for (int i = 1; i < WalkTravelKeys.Length; i++)
            {
                Vector2 a = WalkTravelKeys[i - 1], b = WalkTravelKeys[i];
                if (phase > b.x) continue;
                float u = Mathf.Clamp01((phase - a.x) / (b.x - a.x));
                float blend = u * u * u * (10f - 15f * u + 6f * u * u);
                return (whole + Mathf.Lerp(a.y, b.y, blend)) * WalkCycleDistance;
            }
            return (whole + 1f) * WalkCycleDistance;
        }
        public const string CatchClip = "ZombieCatchFlop";
        public const float CatchCycleSeconds = 6.4f;
        public static readonly string[] ClipNames = { IdleClip, WalkClip, AttackRightClip, AttackLeftClip, HitClip, LoadAdjustClip, WatchClip };
        private static readonly Dictionary<string, AnimationClip> Clips = new Dictionary<string, AnimationClip>(7);
        private static readonly Texture2D[] Faces = new Texture2D[3];
        private static AudioClip groan, lunge;
        private static AnimationClip catchClip;
        private static GameObject crate;
        private static Material crateMaterial;
        private static bool loaded;

        public static bool IsLoop(string name) => name == IdleClip || name == WalkClip;
        public static float ClipDuration(string name) => name switch
        {
            IdleClip => 4f, WalkClip => WalkCycleSeconds,
            AttackRightClip or AttackLeftClip => 1.77f, HitClip => .45f,
            LoadAdjustClip => 2.4f, WatchClip => 2.8f,
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

        public static GameObject LoadCrate() => crate != null ? crate : crate =
            Resources.Load<GameObject>(ResourceFolder + "ZombieCrate")
            ?? throw new InvalidOperationException("Missing authored porter load.");

        internal static AnimationClip LoadCatchClip()
        {
            if (catchClip != null) return catchClip;
            foreach (AnimationClip clip in Resources.LoadAll<AnimationClip>(ResourceFolder + "ZombieCrate"))
                if (clip.name == CatchClip) catchClip = clip;
            if (catchClip == null || !catchClip.isLooping || catchClip.events.Length != 0 ||
                Mathf.Abs(catchClip.length - CatchCycleSeconds) > .003f)
                throw new InvalidOperationException("The fused crate requires its bounded fish animation.");
            return catchClip;
        }

        internal static Material CrateMaterial
        {
            get
            {
                if (crateMaterial != null) return crateMaterial;
                Shader shader = Resources.Load<Shader>("Shaders/Ps1Lit");
                Texture2D atlas = Resources.Load<Texture2D>(ResourceFolder + "ZombieCrateAtlas");
                if (shader == null || atlas == null) throw new InvalidOperationException("Missing shared porter crate surface.");
                crateMaterial = new Material(shader) { name = "Porter Crate Shared", enableInstancing = true,
                    hideFlags = HideFlags.HideAndDontSave };
                crateMaterial.SetTexture("_BaseMap", atlas);
                crateMaterial.SetColor("_BaseColor", Color.white);
                crateMaterial.SetFloat("_Smoothness", .12f);
                return crateMaterial;
            }
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetCache()
        {
            Clips.Clear(); Array.Clear(Faces, 0, Faces.Length);
            loaded = false; groan = lunge = null; catchClip = null;
            crate = null;
            if (crateMaterial != null) UnityEngine.Object.Destroy(crateMaterial);
            crateMaterial = null;
        }
    }
}

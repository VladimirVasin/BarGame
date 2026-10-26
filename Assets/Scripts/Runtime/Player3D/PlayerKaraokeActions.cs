using System;
using System.Collections.Generic;
using UnityEngine;

namespace BarPromenade
{
    /// <summary>Optional continuous body actions; shared voice presentation owns the mouth.</summary>
    public static class PlayerKaraokeActions
    {
        public const string ResourcePath = "Player/PlayerKaraokeActions";
        public const string EnterClip = "KaraokeEnter", LoopClip = "KaraokeLoop", ExitClip = "KaraokeExit";
        public const float EnterSeconds = .5f, LoopSeconds = 2f, ExitSeconds = .5f;
        private static readonly string[] names = { EnterClip, LoopClip, ExitClip };
        private static readonly float[] durations = { EnterSeconds, LoopSeconds, ExitSeconds };
        private static readonly IReadOnlyList<string> readOnlyNames = Array.AsReadOnly(names);
        private static Manifest manifest;
        private static AnimationClip[] cachedClips;

        public static IReadOnlyList<string> ClipNames => readOnlyNames;
        public static Vector3 EntryPelvisFromGround => Point(Load().entry_pelvis_from_ground);
        public static Vector3 ActionPelvisFromGround => Point(Load().action_pelvis_from_ground);
        public static Vector3 ExitPelvisFromGround => Point(Load().exit_pelvis_from_ground);
        public static Vector3 EntryGroundOffset => Point(Load().entry_ground_offset);
        public static Vector3 ExitGroundOffset => Point(Load().exit_ground_offset);

        public static float Duration(string name)
        {
            int index = Array.IndexOf(names, name);
            if (index < 0) throw new ArgumentOutOfRangeException(nameof(name));
            return durations[index];
        }

        public static bool IsLoop(string name) => name == LoopClip;

        public static PlayerAnimatedInteractionDefinition CreateDefinition() =>
            new PlayerAnimatedInteractionDefinition(EnterClip, LoopClip, ExitClip,
                enterFrameCount: 12, enterFramesPerSecond: 24f,
                loopFrameCount: 48, loopFramesPerSecond: 24f,
                exitFrameCount: 12, exitFramesPerSecond: 24f,
                requiresMouthAccess: true);

        /// <summary>Check the complete bank before adding instance-owned bindings.</summary>
        public static bool TryAttach(Player3DAssetRegistry registry)
        {
            if (registry == null || !TryLoad(out _)) return false;
            cachedClips ??= Resources.LoadAll<AnimationClip>(ResourcePath);
            AnimationClip[] selected = new AnimationClip[names.Length];
            for (int index = 0; index < names.Length; index++)
            {
                foreach (AnimationClip candidate in cachedClips)
                    if (candidate != null && candidate.name == names[index]) { selected[index] = candidate; break; }
                AnimationClip clip = selected[index];
                if (clip == null || clip.isLooping != IsLoop(names[index]) || clip.events.Length != 0 ||
                    Mathf.Abs(clip.length - durations[index]) > .003f) return false;
                if (registry.TryGetAnimation(names[index], out Player3DAnimationBinding existing) &&
                    (existing.Clip != clip || existing.Looping != IsLoop(names[index]))) return false;
            }
            for (int index = 0; index < names.Length; index++)
            {
                if (registry.TryGetAnimation(names[index], out _)) continue;
                registry.RegisterRuntimeAnimation(new Player3DAnimationBinding(names[index], "karaoke",
                    selected[index], durations[index], IsLoop(names[index])));
            }
            return true;
        }

        private static Manifest Load() => TryLoad(out Manifest loaded) ? loaded :
            throw new InvalidOperationException("The production hero karaoke action bank is missing or invalid.");

        private static bool TryLoad(out Manifest loaded)
        {
            loaded = manifest;
            if (loaded != null) return true;
            TextAsset source = Resources.Load<TextAsset>(ResourcePath);
            if (source == null) return false;
            loaded = JsonUtility.FromJson<Manifest>(source.text);
            if (loaded == null || loaded.generator != "player_karaoke_v1" || loaded.rig != "HeroV2" ||
                loaded.bone_count != 43 || loaded.fps != 24 || loaded.root_motion || loaded.lip_sync ||
                loaded.animation_events != 0 || loaded.clips == null || loaded.clips.Length != names.Length ||
                !ValidPoint(loaded.entry_pelvis_from_ground) || !ValidPoint(loaded.action_pelvis_from_ground) ||
                !ValidPoint(loaded.exit_pelvis_from_ground) || !ValidPoint(loaded.entry_ground_offset) ||
                !ValidPoint(loaded.exit_ground_offset)) return false;
            for (int index = 0; index < names.Length; index++)
            {
                Clip clip = loaded.clips[index];
                if (clip == null || clip.name != names[index] || clip.loop != IsLoop(clip.name) ||
                    Mathf.Abs(clip.duration_seconds - durations[index]) > .00001f) return false;
            }
            manifest = loaded;
            return true;
        }

        private static bool ValidPoint(float[] p) => p != null && p.Length == 3 &&
            float.IsFinite(p[0]) && float.IsFinite(p[1]) && float.IsFinite(p[2]);
        private static Vector3 Point(float[] p) => new Vector3(p[0], p[1], p[2]);
        [Serializable] private sealed class Manifest
        {
            public string generator, rig;
            public int bone_count, fps, animation_events;
            public bool root_motion, lip_sync;
            public float[] entry_pelvis_from_ground, action_pelvis_from_ground, exit_pelvis_from_ground;
            public float[] entry_ground_offset, exit_ground_offset;
            public Clip[] clips;
        }
        [Serializable] private sealed class Clip { public string name; public float duration_seconds; public bool loop; }
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetCache() { manifest = null; cachedClips = null; }
    }
}

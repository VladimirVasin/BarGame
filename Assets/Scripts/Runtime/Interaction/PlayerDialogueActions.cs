using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace BarPromenade
{
    /// <summary>Optional authored body dialogue on the production hero; no second animator or model.</summary>
    public static class PlayerDialogueActions
    {
        public const string ResourcePath = "Player/PlayerDialogueActions";
        public const string EnterClip = "DialogueEnter", ListenClip = "DialogueListenLoop",
            TalkEnterClip = "DialogueTalkEnter", TalkClip = "DialogueTalkLoop",
            TalkExitClip = "DialogueTalkExit", ExitClip = "DialogueExit";
        public const float EnterSeconds = .75f, ListenSeconds = 2f, TalkEnterSeconds = .5f,
            TalkSeconds = 1.5f, TalkExitSeconds = .5f, ExitSeconds = .75f;
        // Keep the authored samples/endpoints; short conversational settles use a brisker clock.
        public const float EnterPlaybackSeconds = .35f, ExitPlaybackSeconds = .35f;
        private static readonly string[] names = { EnterClip, ListenClip, TalkEnterClip, TalkClip, TalkExitClip, ExitClip };
        private static readonly float[] durations = { EnterSeconds, ListenSeconds, TalkEnterSeconds, TalkSeconds, TalkExitSeconds, ExitSeconds };
        private static Manifest manifest;

        public static IReadOnlyList<string> ClipNames => names;
        public static Vector3 EntryPelvisFromGround => Point(Load().entry_pelvis_from_ground);
        public static Vector3 ActionPelvisFromGround => Point(Load().action_pelvis_from_ground);
        public static Vector3 ExitPelvisFromGround => Point(Load().exit_pelvis_from_ground);
        // Floor-relative offsets; the controller root still adds the shared
        // PlayerFactory.GroundedRootOffset above that floor.
        public static Vector3 EntryGroundOffset => Point(Load().entry_ground_offset);
        public static Vector3 ExitGroundOffset => Point(Load().exit_ground_offset);
        public static float Duration(string name)
        {
            int index = Array.IndexOf(names, name);
            if (index < 0) throw new ArgumentOutOfRangeException(nameof(name));
            return durations[index];
        }
        public static bool IsLoop(string name) => name == ListenClip || name == TalkClip;

        public static PlayerAnimatedInteractionDefinition CreateListeningDefinition() =>
            Definition(EnterClip, ListenClip, ExitClip, EnterPlaybackSeconds, ExitPlaybackSeconds);
        public static PlayerAnimatedInteractionDefinition CreateTalkingDefinition() =>
            Definition(TalkEnterClip, TalkClip, TalkExitClip, .25f, .25f);
        private static PlayerAnimatedInteractionDefinition Definition(string enter, string loop, string exit,
            float enterPlaybackSeconds, float exitPlaybackSeconds) =>
            new PlayerAnimatedInteractionDefinition(enter, loop, exit,
                enterFrameCount: Mathf.RoundToInt(Duration(enter) * 24f), enterFramesPerSecond: Mathf.RoundToInt(Duration(enter) * 24f) / enterPlaybackSeconds,
                loopFrameCount: Mathf.RoundToInt(Duration(loop) * 24f), loopFramesPerSecond: 24f,
                exitFrameCount: Mathf.RoundToInt(Duration(exit) * 24f), exitFramesPerSecond: Mathf.RoundToInt(Duration(exit) * 24f) / exitPlaybackSeconds);

        /// <summary>Validate the complete bank before changing any existing registry binding.</summary>
        public static bool TryAttach(Player3DAssetRegistry registry)
        {
            if (registry == null || !TryLoad(out _)) return false;
            AnimationClip[] clips = Resources.LoadAll<AnimationClip>(ResourcePath);
            var bindings = new List<Player3DAnimationBinding>(registry.Animations);
            for (int index = 0; index < names.Length; index++)
            {
                string name = names[index];
                AnimationClip clip = clips.FirstOrDefault(value => value.name == name);
                if (clip == null || clip.isLooping != IsLoop(name) || clip.events.Length != 0 ||
                    Mathf.Abs(clip.length - durations[index]) > .003f) return false;
                if (registry.TryGetAnimation(name, out Player3DAnimationBinding existing))
                {
                    if (existing.Clip != clip || existing.Looping != IsLoop(name)) return false;
                    continue;
                }
                // Keep the normal weary face. A brief authored blink uses the
                // existing atlas expressions; mouth motion is deliberately absent.
                Player3DFacialExpressionKey[] face = IsLoop(name) ? new[]
                {
                    new Player3DFacialExpressionKey(0f, PlayerFacialExpression.Neutral),
                    new Player3DFacialExpressionKey(.62f, PlayerFacialExpression.HalfBlink),
                    new Player3DFacialExpressionKey(.635f, PlayerFacialExpression.ClosedBlink),
                    new Player3DFacialExpressionKey(.665f, PlayerFacialExpression.HalfBlink),
                    new Player3DFacialExpressionKey(.68f, PlayerFacialExpression.Neutral)
                } : Array.Empty<Player3DFacialExpressionKey>();
                bindings.Add(new Player3DAnimationBinding(name, "dialogue", clip, durations[index], IsLoop(name), face));
            }
            registry.Configure(registry.Animator, registry.ModelRoot, registry.Renderers.ToArray(),
                registry.MeshBindings.ToArray(), registry.AnatomicalParts.ToArray(), bindings.ToArray(),
                registry.Anchors, registry.Metrics, registry.SourceGeneratorVersion, registry.SourcePose,
                registry.SourceTriangleCount, registry.BuildSignature, registry.FaceAtlas);
            return true;
        }

        private static Manifest Load() => TryLoad(out Manifest loaded) ? loaded :
            throw new InvalidOperationException("The production hero dialogue action bank is missing or invalid.");
        private static bool TryLoad(out Manifest loaded)
        {
            loaded = manifest;
            if (loaded != null) return true;
            TextAsset source = Resources.Load<TextAsset>(ResourcePath);
            if (source == null) return false;
            loaded = JsonUtility.FromJson<Manifest>(source.text);
            if (loaded == null || loaded.generator != "player_dialogue_v1" || loaded.rig != "HeroV2" ||
                loaded.bone_count != 31 || loaded.fps != 24 || loaded.root_motion || loaded.lip_sync ||
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
        private static bool ValidPoint(float[] point) => point != null && point.Length == 3 &&
            point.All(value => !float.IsNaN(value) && !float.IsInfinity(value));
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
        private static void ResetCache() => manifest = null;
    }
}

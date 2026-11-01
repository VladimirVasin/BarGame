using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace BarPromenade
{
    /// <summary>Optional measured pickups on the one production hero rig.</summary>
    public static class PlayerStairwellPickupActions
    {
        public const string ResourcePath = "Player/PlayerStairwellPickupActions";
        public const float EnterSeconds = 1.25f, LoopSeconds = 2f, ExitSeconds = .75f;
        public const float TakeProgress = .65f;
        private static readonly string[] items = { "Flashlight", "Crowbar", "Pistol" };
        private static readonly string[] phases = { "Enter", "Loop", "Exit" };
        private static readonly string[] names = items.SelectMany(item => phases.Select(phase => "Stairwell" + item + phase)).ToArray();
        private static Manifest manifest;
        private static AnimationClip[] clips;

        public static IReadOnlyList<string> ClipNames => names;
        public static Vector3 EntryPelvisFromGround => Point(Load().entry_pelvis_from_ground);
        public static Vector3 ActionPelvisFromGround => Point(Load().action_pelvis_from_ground);
        public static Vector3 ExitPelvisFromGround => Point(Load().exit_pelvis_from_ground);
        public static Vector3 ReachFromGround(InventoryItemId item) => Point(Load().items[ItemIndex(item)].reach_from_ground);
        public static Vector3 HeldFromGround(InventoryItemId item) => Point(Load().items[ItemIndex(item)].held_from_ground);
        public static string Prefix(InventoryItemId item) => "Stairwell" + items[ItemIndex(item)];
        public static bool IsLoop(string name) => name != null && name.EndsWith("Loop", StringComparison.Ordinal);
        public static float Duration(string name)
        {
            int index = Array.IndexOf(names, name);
            if (index < 0) throw new ArgumentOutOfRangeException(nameof(name));
            return index % 3 == 0 ? EnterSeconds : index % 3 == 1 ? LoopSeconds : ExitSeconds;
        }
        public static PlayerAnimatedInteractionDefinition CreateDefinition(InventoryItemId item)
        {
            string prefix = Prefix(item);
            return new PlayerAnimatedInteractionDefinition(prefix + "Enter", prefix + "Loop", prefix + "Exit",
                enterFrameCount: 30, enterFramesPerSecond: 24f,
                loopFrameCount: 48, loopFramesPerSecond: 24f,
                exitFrameCount: 18, exitFramesPerSecond: 24f);
        }

        public static bool TryAttach(Player3DAssetRegistry registry)
        {
            if (registry == null || !TryLoad(out _)) return false;
            clips ??= Resources.LoadAll<AnimationClip>(ResourcePath);
            var bindings = new List<Player3DAnimationBinding>(registry.Animations);
            foreach (string name in names)
            {
                AnimationClip clip = Array.Find(clips, value => value.name == name);
                if (clip == null || clip.isLooping != IsLoop(name) || clip.events.Length != 0 ||
                    Mathf.Abs(clip.length - Duration(name)) > .003f) return false;
                if (registry.TryGetAnimation(name, out Player3DAnimationBinding existing))
                {
                    if (existing.Clip != clip || existing.Looping != IsLoop(name)) return false;
                    continue;
                }
                bindings.Add(new Player3DAnimationBinding(name, "stairwell_pickup", clip, Duration(name), IsLoop(name)));
            }
            registry.Configure(registry.Animator, registry.ModelRoot, registry.Renderers.ToArray(),
                registry.MeshBindings.ToArray(), registry.AnatomicalParts.ToArray(), bindings.ToArray(),
                registry.Anchors, registry.Metrics, registry.SourceGeneratorVersion, registry.SourcePose,
                registry.SourceTriangleCount, registry.BuildSignature, registry.FaceAtlas);
            return true;
        }

        private static int ItemIndex(InventoryItemId item) => item switch
        {
            InventoryItemId.ChestFlashlight => 0,
            InventoryItemId.CombatCrowbar => 1,
            InventoryItemId.CombatPistol => 2,
            _ => throw new ArgumentOutOfRangeException(nameof(item))
        };
        private static Manifest Load() => TryLoad(out Manifest value) ? value :
            throw new InvalidOperationException("The production hero pickup action bank is missing or invalid.");
        private static bool TryLoad(out Manifest value)
        {
            value = manifest;
            if (value != null) return true;
            TextAsset asset = Resources.Load<TextAsset>(ResourcePath);
            if (asset == null) return false;
            value = JsonUtility.FromJson<Manifest>(asset.text);
            if (value == null || value.generator != "player_stairwell_pickup_v1" || value.rig != "HeroV2" ||
                value.bone_count != 43 || value.fps != 24 || value.root_motion || value.animation_events != 0 ||
                !ValidPoint(value.entry_pelvis_from_ground) || !ValidPoint(value.action_pelvis_from_ground) ||
                !ValidPoint(value.exit_pelvis_from_ground) || value.clips == null || value.clips.Length != 9 ||
                value.items == null || value.items.Length != 3 || !ValidError(value.maximum_lower_body_error) ||
                !ValidError(value.maximum_endpoint_error) || !ValidError(value.maximum_neutral_error) ||
                !float.IsFinite(value.maximum_grip_error) || value.maximum_grip_error > .0002f ||
                Mathf.Abs(value.take_progress - TakeProgress) > .00001f) return false;
            for (int index = 0; index < names.Length; index++)
            {
                Clip clip = value.clips[index];
                if (clip == null || clip.name != names[index] || clip.loop != IsLoop(clip.name) ||
                    Mathf.Abs(clip.duration_seconds - Duration(clip.name)) > .00001f) return false;
            }
            for (int index = 0; index < items.Length; index++)
                if (value.items[index] == null || value.items[index].name != items[index] ||
                    !ValidPoint(value.items[index].reach_from_ground) || !ValidPoint(value.items[index].held_from_ground)) return false;
            manifest = value;
            return true;
        }
        private static bool ValidError(float value) => float.IsFinite(value) && value >= 0f && value <= .00001f;
        private static bool ValidPoint(float[] p) => p != null && p.Length == 3 && p.All(float.IsFinite);
        private static Vector3 Point(float[] p) => new Vector3(p[0], p[1], p[2]);
        [Serializable] private sealed class Manifest
        {
            public string generator, rig;
            public int bone_count, fps, animation_events;
            public bool root_motion;
            public float take_progress, maximum_lower_body_error, maximum_endpoint_error, maximum_neutral_error, maximum_grip_error;
            public float[] entry_pelvis_from_ground, action_pelvis_from_ground, exit_pelvis_from_ground;
            public Item[] items;
            public Clip[] clips;
        }
        [Serializable] private sealed class Item { public string name; public float[] reach_from_ground, held_from_ground; }
        [Serializable] private sealed class Clip { public string name; public float duration_seconds; public bool loop; }
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetCache() { manifest = null; clips = null; }
    }
}

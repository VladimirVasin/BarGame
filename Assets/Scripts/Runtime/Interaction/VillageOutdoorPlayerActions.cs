using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace BarPromenade
{
    public enum VillageOutdoorPlayerAction
    {
        BasketPickup, BasketCarry, BasketPlace,
        ShovelPickup, ShovelCarry, ShovelWork, ShovelPutBack,
        LidEnter, LidHold, LidExit, GateEnter, GateHold, GateExit
    }

    public struct VillageOutdoorPlayerFrame
    {
        public Pose PropFromGround;
        public Vector3 LeftGripFromGround, RightGripFromGround;
        public float LeftContactWeight, RightContactWeight;
        public float ContactWeight => Mathf.Max(LeftContactWeight, RightContactWeight);
        public bool Held, BladeContact;
    }

    /// <summary>Optional bone-only actions and their matching, authored metre trajectories.</summary>
    public static class VillageOutdoorPlayerActions
    {
        public const string ResourcePath = "Player/VillageOutdoorPlayerActions";
        public const float PickupContactSeconds = 1.5f;
        private static Manifest manifest;
        private static Dictionary<VillageOutdoorPlayerAction, Track> tracks;
        public static string ClipName(VillageOutdoorPlayerAction action) => "Village" + action;
        public static float Duration(VillageOutdoorPlayerAction action) => Get(action).duration_seconds;
        public static bool IsLoop(VillageOutdoorPlayerAction action) => Get(action).loop;
        public static Vector3 EntryPelvisFromGround { get { Load(); return Point(manifest.entry_pelvis_from_ground); } }
        public static Vector3 ActionPelvisFromGround { get { Load(); return Point(manifest.action_pelvis_from_ground); } }
        public static Vector3 ExitPelvisFromGround { get { Load(); return Point(manifest.exit_pelvis_from_ground); } }
        public static Vector3 EntryGroundOffset { get { Load(); return Point(manifest.entry_ground_offset); } }
        public static Vector3 ExitGroundOffset { get { Load(); return Point(manifest.exit_ground_offset); } }

        public static VillageOutdoorPlayerFrame Sample(VillageOutdoorPlayerAction action, float elapsedSeconds)
        {
            Track track = Get(action);
            float time = track.loop ? Mathf.Repeat(Mathf.Max(0f, elapsedSeconds), track.duration_seconds) :
                Mathf.Clamp(elapsedSeconds, 0f, track.duration_seconds);
            float frame = time * 24f;
            int index = Mathf.Min(Mathf.FloorToInt(frame), track.frames.Length - 1);
            Frame a = track.frames[index], b = track.frames[Mathf.Min(index + 1, track.frames.Length - 1)];
            float t = frame - index;
            return new VillageOutdoorPlayerFrame
            {
                PropFromGround = new Pose(Vector3.Lerp(Point(a.prop.position), Point(b.prop.position), t),
                    Quaternion.Slerp(Rotation(a.prop.rotation), Rotation(b.prop.rotation), t)),
                LeftGripFromGround = Vector3.Lerp(Point(a.left), Point(b.left), t),
                RightGripFromGround = Vector3.Lerp(Point(a.right), Point(b.right), t),
                LeftContactWeight = Mathf.Lerp(a.left_weight, b.left_weight, t),
                RightContactWeight = Mathf.Lerp(a.right_weight, b.right_weight, t),
                Held = a.held, BladeContact = a.blade_contact
            };
        }

        public static PlayerAnimatedInteractionDefinition CreateDefinition(VillageOutdoorPlayerAction enter,
            VillageOutdoorPlayerAction loop, VillageOutdoorPlayerAction exit) =>
            new PlayerAnimatedInteractionDefinition(ClipName(enter), ClipName(loop), ClipName(exit),
                enterFrameCount: Mathf.RoundToInt(Duration(enter) * 24f), enterFramesPerSecond: 24f,
                loopFrameCount: Mathf.RoundToInt(Duration(loop) * 24f), loopFramesPerSecond: 24f,
                exitFrameCount: Mathf.RoundToInt(Duration(exit) * 24f), exitFramesPerSecond: 24f);

        public static bool TryAttach(Player3DAssetRegistry registry)
        {
            if (registry == null) return false;
            Load();
            AnimationClip[] clips = Resources.LoadAll<AnimationClip>(ResourcePath);
            var bindings = new List<Player3DAnimationBinding>(registry.Animations);
            foreach (VillageOutdoorPlayerAction action in Enum.GetValues(typeof(VillageOutdoorPlayerAction)))
            {
                string name = ClipName(action);
                if (registry.TryGetAnimation(name, out _)) continue;
                Track track = Get(action);
                AnimationClip clip = clips.FirstOrDefault(candidate => candidate.name == name);
                if (clip == null || clip.isLooping != track.loop || clip.events.Length != 0 ||
                    Mathf.Abs(clip.length - track.duration_seconds) > .003f) return false;
                bindings.Add(new Player3DAnimationBinding(name, "village_outdoor", clip, track.duration_seconds, track.loop));
            }
            registry.Configure(registry.Animator, registry.ModelRoot, registry.Renderers.ToArray(),
                registry.MeshBindings.ToArray(), registry.AnatomicalParts.ToArray(), bindings.ToArray(),
                registry.Anchors, registry.Metrics, registry.SourceGeneratorVersion, registry.SourcePose,
                registry.SourceTriangleCount, registry.BuildSignature, registry.FaceAtlas);
            return true;
        }

        private static Track Get(VillageOutdoorPlayerAction action)
        {
            Load();
            if (!tracks.TryGetValue(action, out Track track)) throw new ArgumentOutOfRangeException(nameof(action));
            return track;
        }
        private static void Load()
        {
            if (tracks != null) return;
            TextAsset text = Resources.Load<TextAsset>(ResourcePath);
            if (text == null) throw new InvalidOperationException("The outdoor hero action bank is missing.");
            Manifest loaded = JsonUtility.FromJson<Manifest>(text.text);
            if (loaded == null || loaded.generator != "village_outdoor_player_v1" || loaded.rig != "HeroV2" ||
                loaded.bone_count != 31 || loaded.root_motion || loaded.animation_events != 0 || loaded.fps != 24 ||
                loaded.tracks == null || loaded.tracks.Length != Enum.GetValues(typeof(VillageOutdoorPlayerAction)).Length)
                throw new InvalidOperationException("The outdoor hero bank violates its rig contract.");
            var parsed = new Dictionary<VillageOutdoorPlayerAction, Track>();
            foreach (Track track in loaded.tracks)
            {
                if (!Enum.TryParse(track.name, out VillageOutdoorPlayerAction action) || track.clip != ClipName(action) ||
                    track.frames == null || track.frames.Length != Mathf.RoundToInt(track.duration_seconds * 24f) + 1)
                    throw new InvalidOperationException("Invalid outdoor hero samples: " + track.name);
                parsed.Add(action, track);
            }
            manifest = loaded; tracks = parsed;
        }
        private static Vector3 Point(float[] p) => new Vector3(p[0], p[1], p[2]);
        private static Quaternion Rotation(float[] p) => new Quaternion(p[0], p[1], p[2], p[3]);
        [Serializable] private sealed class Manifest
        {
            public string generator, rig;
            public int bone_count, animation_events, fps;
            public bool root_motion;
            public float[] entry_pelvis_from_ground, action_pelvis_from_ground, exit_pelvis_from_ground;
            public float[] entry_ground_offset, exit_ground_offset;
            public Track[] tracks;
        }
        [Serializable] private sealed class Track
        {
            public string name, clip; public float duration_seconds; public bool loop; public Frame[] frames;
        }
        [Serializable] private sealed class Prop { public float[] position, rotation; }
        [Serializable] private sealed class Frame
        {
            public Prop prop; public float[] left, right; public float left_weight, right_weight;
            public bool held, blade_contact;
        }
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetCache() { manifest = null; tracks = null; }
    }
}

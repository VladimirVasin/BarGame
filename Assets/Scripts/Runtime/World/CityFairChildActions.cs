using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace BarPromenade
{
    public enum CityFairChildAction
    {
        Idle, Walk, Turn, Stop, GoodsEnter, GoodsLoop, GoodsExit,
        ToyEnter, ToyLoop, ToyExit, SitEnter, SitLoop, SitExit, AdjustSleeve, AdjustCap
    }

    public readonly struct CityFairChildFrame
    {
        public CityFairChildFrame(Vector3 pelvis, Vector3 left, Vector3 right, Vector3 leftFoot,
            Vector3 rightFoot, float contact)
        { Pelvis = pelvis; LeftGrip = left; RightGrip = right; LeftFoot = leftFoot; RightFoot = rightFoot; ContactWeight = contact; }
        public Vector3 Pelvis { get; }
        public Vector3 LeftGrip { get; }
        public Vector3 RightGrip { get; }
        public Vector3 LeftFoot { get; }
        public Vector3 RightFoot { get; }
        public float ContactWeight { get; }
    }

    /// <summary>Child-native metre tracks; route, support and toy ownership stay with the fair lifecycle.</summary>
    public static class CityFairChildActions
    {
        public const string ResourcePath = "City/FairChild/ChildActions";
        public const float Height = 1.30f;
        public static readonly Vector3 SeatedPelvisFromDock = new Vector3(0f, .57f, -.54f);
        private static Manifest manifest;
        private static Dictionary<CityFairChildAction, Track> tracks;
        public static string[] BoneNames { get { Get(CityFairChildAction.Idle); return manifest.bones; } }
        public static string ClipName(CityFairChildAction action) => "FairChild" + action;
        public static float Duration(CityFairChildAction action) => Get(action).duration_seconds;
        public static bool IsLoop(CityFairChildAction action) => Get(action).loop;
        public static AnimationClip[] LoadClips()
        {
            AnimationClip[] available = Resources.LoadAll<AnimationClip>(ResourcePath);
            return Enum.GetValues(typeof(CityFairChildAction)).Cast<CityFairChildAction>().Select(action =>
            {
                AnimationClip clip = available.SingleOrDefault(value => value.name == ClipName(action));
                if (clip == null || Mathf.Abs(clip.length - Duration(action)) > .003f || clip.isLooping != IsLoop(action) || clip.events.Length != 0)
                    throw new InvalidOperationException($"Child action {action}: expected duration={Duration(action)}, loop={IsLoop(action)}, no events; actual clip={clip?.name}, duration={clip?.length}, loop={clip?.isLooping}.");
                return clip;
            }).ToArray();
        }
        public static CityFairChildFrame Sample(CityFairChildAction action, float seconds)
        {
            Track track = Get(action);
            float time = track.loop ? Mathf.Repeat(Mathf.Max(0f, seconds), track.duration_seconds) : Mathf.Clamp(seconds, 0f, track.duration_seconds);
            float frame = time * 24f;
            int index = Mathf.Min(Mathf.FloorToInt(frame), track.frames.Length - 1);
            Frame a = track.frames[index], b = track.frames[Mathf.Min(index + 1, track.frames.Length - 1)];
            float t = frame - index;
            return new CityFairChildFrame(Vector3.Lerp(Point(a.pelvis), Point(b.pelvis), t),
                Vector3.Lerp(Point(a.left), Point(b.left), t), Vector3.Lerp(Point(a.right), Point(b.right), t),
                Vector3.Lerp(Point(a.left_foot), Point(b.left_foot), t), Vector3.Lerp(Point(a.right_foot), Point(b.right_foot), t),
                Mathf.Lerp(a.contact_weight, b.contact_weight, t));
        }
        private static Track Get(CityFairChildAction action)
        {
            if (tracks == null)
            {
                TextAsset text = Resources.Load<TextAsset>(ResourcePath);
                if (text == null) throw new InvalidOperationException("Missing child-native fair actions: " + ResourcePath);
                manifest = JsonUtility.FromJson<Manifest>(text.text);
                if (manifest == null || manifest.generator != "city_fair_child_actions_v1" || manifest.fps != 24 || manifest.tracks == null)
                    throw new InvalidOperationException("Invalid child action manifest: expected city_fair_child_actions_v1 at 24 fps.");
                tracks = new Dictionary<CityFairChildAction, Track>();
                foreach (Track track in manifest.tracks)
                {
                    if (!Enum.TryParse(track.name, out CityFairChildAction parsed) || track.clip != ClipName(parsed) || track.frames == null ||
                        track.duration_seconds <= 0f || track.frames.Length != Mathf.RoundToInt(track.duration_seconds * 24f) + 1)
                        throw new InvalidOperationException("Invalid child track name/duration/frames: " + track.name);
                    tracks.Add(parsed, track);
                }
            }
            if (!tracks.TryGetValue(action, out Track result)) throw new InvalidOperationException("Missing child action track: " + action);
            return result;
        }
        private static Vector3 Point(float[] p) => new Vector3(p[0], p[1], p[2]);
        [Serializable] private sealed class Manifest { public string generator; public int fps; public string[] bones; public Track[] tracks; }
        [Serializable] private sealed class Track { public string name, clip; public float duration_seconds; public bool loop; public Frame[] frames; }
        [Serializable] private sealed class Frame { public float[] pelvis, left, right, left_foot, right_foot; public float contact_weight; }
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetCache() { manifest = null; tracks = null; }
    }
}

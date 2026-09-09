using System;
using System.Collections.Generic;
using UnityEngine;

namespace BarPromenade
{
    public enum VillageWorkroomContact { None, Hammer, Chair, Mitten, Cloth, Box, BoxLid, ClothFlap, Thread }

    /// <summary>Fixed-metre poses relative to the resident dock; the room owns all separate props.</summary>
    public struct VillageWorkroomFrame
    {
        public Pose Hammer, Mitten, Cloth, Box;
        public float ClothFoldDegrees, BoxLidDegrees;
        public Vector3 RightContact, LeftContact;
        public Vector3 ThreadStart, ThreadEnd;
        public float RightContactWeight, LeftContactWeight;
        public VillageWorkroomContact RightContactKind, LeftContactKind;
        public bool HammerHeld, MittenHeld, ClothHeld, BoxHeld;
        public bool ThreadVisible;
    }

    public sealed partial class VillageResidentPresentation
    {
        private static Dictionary<VillageResidentAction, WorkroomTrack> workroomTracks;
        public static readonly float[] RepairStrikeSeconds = { .8f, 1.85f, 2.9f };

        public static VillageWorkroomFrame SampleWorkroomProps(VillageResidentAction action, float elapsedSeconds)
        {
            if (workroomTracks == null)
            {
                TextAsset text = Resources.Load<TextAsset>("VillageLife/VillageResidentWorkroomActions");
                if (text == null) throw new InvalidOperationException("The authored village workroom bank is missing.");
                WorkroomManifest manifest = JsonUtility.FromJson<WorkroomManifest>(text.text);
                workroomTracks = new Dictionary<VillageResidentAction, WorkroomTrack>();
                foreach (WorkroomTrack track in manifest.tracks)
                    workroomTracks.Add((VillageResidentAction)Enum.Parse(typeof(VillageResidentAction), track.name), track);
            }
            if (!workroomTracks.TryGetValue(action, out WorkroomTrack selected))
                throw new ArgumentException("This is not an authored village workroom action.", nameof(action));
            float time = selected.loop ? Mathf.Repeat(Mathf.Max(0, elapsedSeconds), selected.duration_seconds)
                : Mathf.Clamp(elapsedSeconds, 0, selected.duration_seconds);
            float frame = time * 24f;
            int index = Mathf.Min(Mathf.FloorToInt(frame), selected.frames.Length - 1);
            WorkroomSample a = selected.frames[index], b = selected.frames[Mathf.Min(index + 1, selected.frames.Length - 1)];
            float t = frame - index;
            return new VillageWorkroomFrame
            {
                Hammer = Mix(a.hammer, b.hammer, t), Mitten = Mix(a.mitten, b.mitten, t),
                Cloth = Mix(a.cloth, b.cloth, t), Box = Mix(a.box, b.box, t),
                ClothFoldDegrees = Mathf.Lerp(a.fold_degrees, b.fold_degrees, t),
                BoxLidDegrees = Mathf.Lerp(a.lid_degrees, b.lid_degrees, t),
                RightContact = Vector3.Lerp(Point(a.right_contact), Point(b.right_contact), t),
                LeftContact = Vector3.Lerp(Point(a.left_contact), Point(b.left_contact), t),
                ThreadStart = Vector3.Lerp(Point(a.thread_start), Point(b.thread_start), t),
                ThreadEnd = Vector3.Lerp(Point(a.thread_end), Point(b.thread_end), t),
                RightContactWeight = Mathf.Lerp(a.right_weight, b.right_weight, t),
                LeftContactWeight = Mathf.Lerp(a.left_weight, b.left_weight, t),
                RightContactKind = (VillageWorkroomContact)a.right_kind,
                LeftContactKind = (VillageWorkroomContact)a.left_kind,
                HammerHeld = a.hammer_held, MittenHeld = a.mitten_held,
                ClothHeld = a.cloth_held, BoxHeld = a.box_held, ThreadVisible = a.thread_visible
            };
        }

        public void ApplyWorkroom(VillageResidentAction action, float elapsedSeconds)
        {
            Apply(action, elapsedSeconds);
            VillageWorkroomFrame frame = SampleWorkroomProps(action, elapsedSeconds);
            if (frame.RightContactWeight >= .9999f)
                ApplyHandContacts(transform.position + transform.rotation * frame.RightContact, null, frame.RightContactWeight);
            if (frame.LeftContactWeight >= .9999f)
                ApplyHandContacts(null, transform.position + transform.rotation * frame.LeftContact, frame.LeftContactWeight);
        }

        private static Vector3 Point(float[] p) => new Vector3(p[0], p[1], p[2]);
        private static Pose Mix(WorkroomPose a, WorkroomPose b, float t) => new Pose(
            Vector3.Lerp(Point(a.position), Point(b.position), t),
            Quaternion.Slerp(new Quaternion(a.rotation[0], a.rotation[1], a.rotation[2], a.rotation[3]),
                new Quaternion(b.rotation[0], b.rotation[1], b.rotation[2], b.rotation[3]), t));

        [Serializable] private sealed class WorkroomManifest { public WorkroomTrack[] tracks; }
        [Serializable] private sealed class WorkroomTrack
        {
            public string name; public float duration_seconds; public bool loop; public WorkroomSample[] frames;
        }
        [Serializable] private sealed class WorkroomPose { public float[] position; public float[] rotation; }
        [Serializable] private sealed class WorkroomSample
        {
            public WorkroomPose hammer, mitten, cloth, box;
            public float fold_degrees, lid_degrees, right_weight, left_weight;
            public float[] right_contact, left_contact;
            public float[] thread_start, thread_end;
            public int right_kind, left_kind;
            public bool hammer_held, mitten_held, cloth_held, box_held;
            public bool thread_visible;
        }
    }
}

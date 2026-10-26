using System;
using System.Collections.Generic;
using UnityEngine;

namespace BarPromenade
{
    public enum BarKaraokeShotKind { Singer, Hall, Patron, Bartender, WashroomFace, WashroomBowl }

    public readonly struct BarKaraokeCameraShot
    {
        public Pose Pose { get; }
        public float FieldOfView { get; }
        public Vector3 Focus { get; }
        public float Seconds { get; }
        public Pose EndPose { get; }
        public float EndFieldOfView { get; }
        public Vector3 EndFocus { get; }
        public Transform Subject { get; }
        public BarKaraokeShotKind Kind { get; }
        public string SubjectId { get; }
        public float NearClipPlane { get; }
        public float OpticalScale { get; }
        public BarKaraokeCameraShot(Pose pose, float fieldOfView, Vector3 focus,
            Pose endPose, float endFieldOfView, Vector3 endFocus, float seconds,
            Transform subject = null, BarKaraokeShotKind kind = BarKaraokeShotKind.Singer,
            string subjectId = null, float nearClipPlane = float.NaN, float opticalScale = 1f)
        {
            Pose = pose; FieldOfView = fieldOfView; Focus = focus;
            EndPose = endPose; EndFieldOfView = endFieldOfView; EndFocus = endFocus; Seconds = seconds;
            Subject = subject; Kind = kind; SubjectId = subjectId ?? string.Empty;
            NearClipPlane = nearClipPlane; OpticalScale = opticalScale;
        }
        public void Sample(float progress, out Pose pose, out float fov, out Vector3 focus)
        {
            float t = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(progress));
            pose = new Pose(Vector3.Lerp(Pose.position, EndPose.position, t),
                Quaternion.Slerp(Pose.rotation, EndPose.rotation, t));
            fov = Mathf.Lerp(FieldOfView, EndFieldOfView, t);
            focus = Vector3.Lerp(Focus, EndFocus, t);
        }
    }

    /// <summary>Grounded endpoints and a finite authored sequence in the existing hall.</summary>
    public sealed class BarKaraokePlan
    {
        public const float CameraSeconds = .65f;
        public const float FieldOfView = 58f;
        public PlayerAnimatedInteractionPose EntryPose { get; }
        public PlayerAnimatedInteractionPose ExitPose { get; }
        public Vector3 ActionPelvis { get; }
        public Vector3[] ApproachWaypoints { get; }
        public Pose CameraPose { get; }
        public Vector3 CameraFocus { get; }
        public Vector3 Microphone { get; }
        public Vector3 Approach { get; }
        public IReadOnlyList<BarKaraokeCameraShot> CameraShots { get; }

        public BarKaraokePlan(Transform room)
        {
            if (room == null) throw new ArgumentNullException(nameof(room));
            Quaternion facing = room.rotation * Quaternion.Euler(0f, BarInteriorLayoutPlanner.StageYawDegrees, 0f);
            Vector3 floor = room.TransformPoint(BarInteriorLayoutPlanner.StageStandingPosition -
                Vector3.up * PlayerFactory.GroundedRootOffset);
            EntryPose = At(floor, facing, PlayerKaraokeActions.EntryGroundOffset,
                PlayerKaraokeActions.EntryPelvisFromGround);
            ExitPose = At(floor, facing, PlayerKaraokeActions.ExitGroundOffset,
                PlayerKaraokeActions.ExitPelvisFromGround);
            ActionPelvis = floor + facing * PlayerKaraokeActions.ActionPelvisFromGround;
            Approach = room.TransformPoint(BarInteriorLayoutPlanner.StageApproachPosition);
            ApproachWaypoints = new[] { Approach, room.TransformPoint(BarInteriorLayoutPlanner.StageEntryPosition) };
            Microphone = room.TransformPoint(BarInteriorLayoutPlanner.StageMicrophonePosition);
            CameraFocus = floor + room.rotation * new Vector3(-.08f, 1.12f, 0f);
            Vector3 camera = room.TransformPoint(new Vector3(2.35f, 1.8f, -1.85f));
            CameraPose = new Pose(camera, Quaternion.LookRotation(CameraFocus - camera, room.up) *
                Quaternion.Euler(0f, 0f, -2f));
            CameraShots = new[]
            {
                Shot(room, new Vector3(2.35f, 1.8f, -1.85f), new Vector3(2.75f, 1.9f, -2.05f),
                    CameraFocus, floor + room.rotation * new Vector3(-.08f, 1.28f, 0f), 58f, 51f, -2f, -1f, 6f),
                Shot(room, new Vector3(3.05f, 1.85f, -1.65f), new Vector3(3.2f, 1.9f, -3.25f),
                    floor + room.rotation * new Vector3(-.12f, 1.6f, 0f),
                    floor + room.rotation * new Vector3(-.12f, 1.6f, 0f), 43f, 47f, 1.5f, -1.5f, 5.5f),
                Shot(room, new Vector3(4.95f, 2.15f, -3.2f), new Vector3(4.8f, 2.4f, -1.65f),
                    room.TransformPoint(new Vector3(-1.6f, 1.1f, -.2f)),
                    room.TransformPoint(new Vector3(-2.3f, 1.15f, .2f)), 66f, 72f, -1f, 1f, 7.5f,
                    BarKaraokeShotKind.Hall)
            };
        }

        private static BarKaraokeCameraShot Shot(Transform room, Vector3 start, Vector3 end,
            Vector3 focus, Vector3 endFocus, float fov, float endFov, float roll, float endRoll, float seconds,
            BarKaraokeShotKind kind = BarKaraokeShotKind.Singer)
        {
            Vector3 position = room.TransformPoint(start), endPosition = room.TransformPoint(end);
            return new BarKaraokeCameraShot(new Pose(position,
                    Quaternion.LookRotation(focus - position, room.up) * Quaternion.Euler(0f, 0f, roll)), fov, focus,
                new Pose(endPosition, Quaternion.LookRotation(endFocus - endPosition, room.up) *
                    Quaternion.Euler(0f, 0f, endRoll)), endFov, endFocus, seconds, kind: kind);
        }

        private static PlayerAnimatedInteractionPose At(Vector3 floor, Quaternion facing,
            Vector3 groundOffset, Vector3 pelvis) => new PlayerAnimatedInteractionPose(
                floor + facing * groundOffset + Vector3.up * PlayerFactory.GroundedRootOffset,
                facing, floor + facing * pelvis);
    }
}

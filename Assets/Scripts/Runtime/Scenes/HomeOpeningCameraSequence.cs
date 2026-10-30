using UnityEngine;

namespace BarPromenade
{
    /// <summary>A finite set of quiet room views over the real sleeping hero.</summary>
    public sealed class HomeOpeningCameraSequence
    {
        public const int ShotCount = 5;
        public const float ShotSeconds = 6f;
        private readonly HomeInteriorRoot home;
        private readonly ContextualCameraDirector director;
        private readonly Transform head;
        private float elapsed;
        private Pose lastPose;
        private Vector3 lastFocus;
        private float lastFov;

        public int CurrentShotIndex { get; private set; }

        public HomeOpeningCameraSequence(HomeInteriorRoot owner, ContextualCameraDirector cameraDirector,
            Transform headAnchor)
        { home = owner; director = cameraDirector; head = headAnchor; }

        public bool Begin()
        {
            Resolve(0, 0f, out Pose pose, out float fov, out Vector3 focus);
            if (!director.BeginAuthored(home.CameraFollow, head, home.Player.GameObject.transform,
                    pose, fov, focus, .01f, HomeSleepController.CameraReturnSeconds, true, .035f)) return false;
            if (Apply(0, 0f)) return true;
            director.RestoreImmediate();
            return false;
        }

        public bool Advance(float seconds)
        {
            elapsed += Mathf.Max(0f, seconds);
            if (elapsed >= ShotSeconds)
            {
                // One visible cut per frame: a loading hitch never skips the deck.
                CurrentShotIndex = (CurrentShotIndex + 1) % ShotCount;
                elapsed = 0f;
                return Apply(CurrentShotIndex, 0f);
            }
            return SetShot(CurrentShotIndex, elapsed / ShotSeconds, false, seconds);
        }

        public bool Apply(int index, float progress)
        {
            if (index < 0 || index >= ShotCount || !float.IsFinite(progress)) return false;
            CurrentShotIndex = index;
            elapsed = Mathf.Clamp01(progress) * ShotSeconds;
            return SetShot(index, progress, true, 0f);
        }

        public bool AdvanceWake(float seconds, float clipProgress)
        {
            // Keep the room-space lens, gently lifting its aim as he sits up.
            // These are medium/wide room shots, not the corrupted face framing.
            Vector3 seatedFocus = head.position - Vector3.up * .14f;
            float rise = Mathf.SmoothStep(0f, 1f, clipProgress / HomeBedInteraction.AbruptWakeClipSplit);
            Vector3 focus = Vector3.Lerp(lastFocus, seatedFocus, rise);
            var pose = new Pose(lastPose.position, Quaternion.LookRotation(focus - lastPose.position, Vector3.up));
            if (!director.TrySetAuthoredShot(pose, lastFov, focus, sightSubject: head, nearClipPlane: .035f))
                return false;
            director.Tick(seconds);
            return true;
        }

        private bool SetShot(int index, float progress, bool cut, float seconds)
        {
            Resolve(index, progress, out Pose pose, out float fov, out Vector3 focus);
            if (!director.TrySetAuthoredShot(pose, fov, focus, cut: cut,
                    sightSubject: head, nearClipPlane: .035f)) return false;
            lastPose = pose; lastFocus = focus; lastFov = fov;
            director.Tick(seconds);
            return true;
        }

        private void Resolve(int index, float progress, out Pose pose, out float fov, out Vector3 focus)
        {
            Vector3 bed = home.BedInteractionPlan.ActionHipPosition;
            float x = bed.x, z = bed.z;
            Vector3 position, travel;
            switch (index)
            {
                case 0: position = new Vector3(x + 3.7f, 2.35f, z - 2f); travel = new Vector3(-.10f, 0f, .04f); fov = 56f; break;
                case 1: position = new Vector3(x + 2.8f, 1.8f, z + 1.8f); travel = new Vector3(-.08f, .02f, -.04f); fov = 56f; break;
                case 2: position = new Vector3(x + 2.65f, 1.65f, z - .1f); travel = new Vector3(-.08f, 0f, .06f); fov = 54f; break;
                case 3: position = new Vector3(x + 1.6f, 2.6f, z - 2.3f); travel = new Vector3(.06f, -.04f, .06f); fov = 54f; break;
                default: position = new Vector3(x + 6.6f, 2.25f, z - .9f); travel = new Vector3(-.10f, 0f, .05f); fov = 50f; break;
            }
            float t = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(progress));
            position = home.Room.TransformPoint(position + travel * t);
            focus = home.Room.TransformPoint(bed + Vector3.up * .16f);
            pose = new Pose(position, Quaternion.LookRotation(focus - position, home.Room.up));
        }
    }
}

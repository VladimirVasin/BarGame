using System;
using UnityEngine;

namespace BarPromenade
{
    /// <summary>
    /// Drives the spade, and only the spade.
    ///
    /// The hero is not rig-driven through this work — the camera sits
    /// on the rim looking down into the hole and he stands behind it —
    /// so everything the player sees of the digging is this one object
    /// moving. That puts the whole weight of the act on the tool, and
    /// it is why the swing is legible in the world as well as on the
    /// bar: while the marker runs, the blade rides up and down with
    /// it, so a player who never looks at the gauge can still time a
    /// strike off the spade itself.
    ///
    /// Poses are stated as a point for the blade's tip and a pitch off
    /// vertical, both in a frame built from where the digger stands.
    /// The assembly's origin is the tip, so a pose is simply "put the
    /// point here, lean the handle back that far".
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class CemeteryShovelAnimator : MonoBehaviour
    {
        /// <summary>Each leg of a biting stroke: in, lever, out, over
        /// the heap, back.</summary>
        public const float DriveSeconds = 0.14f;
        public const float LeverSeconds = 0.10f;
        public const float LiftSeconds = 0.16f;
        public const float DumpSeconds = 0.15f;
        public const float ReturnSeconds = 0.21f;

        /// <summary>A stroke that took nothing is shorter, and never
        /// goes near the heap.</summary>
        public const float SlipSeconds = 0.12f;
        public const float RecoverSeconds = 0.19f;

        /// <summary>How fast the blade settles onto a pose it is not
        /// being driven through.</summary>
        public const float FollowSharpness = 14f;

        /// <summary>
        /// Taking the spade up out of the ground and standing it back
        /// in. Long enough to read as a man picking a tool up rather
        /// than the tool teleporting into his hands — which is the
        /// whole point of there being only one of them.
        /// </summary>
        public const float TakeUpSeconds = 0.42f;
        public const float LayDownSeconds = 0.36f;

        /// <summary>How far the blade is pulled clear of the ground on
        /// the way out, before it swings to the work.</summary>
        public const float FreeLiftMeters = 0.26f;

        private const int MaximumKeys = 6;

        private readonly Vector3[] keyPositions =
            new Vector3[MaximumKeys];
        private readonly float[] keyTilts = new float[MaximumKeys];
        private readonly float[] keyDurations =
            new float[MaximumKeys];

        private Vector3 workDirection = Vector3.forward;
        private Vector3 face;
        private Vector3 heapTop;
        private float swing;
        private int keyCount;
        private int keyIndex;
        private float keyElapsed;
        private Vector3 fromPosition;
        private float fromTilt;
        private bool isStriking;
        private Vector3 restPosition;
        private Quaternion restRotation = Quaternion.identity;
        private bool hasRestPose;

        /// <summary>True while a stroke is playing out and the spade
        /// is nobody else's to move.</summary>
        public bool IsStriking => isStriking;

        /// <summary>
        /// Which way the digger faces. Everything else is built off
        /// it, so the spade is always worked from the side the camera
        /// is looking over.
        /// </summary>
        public void SetWorkDirection(Vector3 direction)
        {
            Vector3 flat = new Vector3(direction.x, 0f, direction.z);
            if (flat.sqrMagnitude > 0.0001f)
            {
                workDirection = flat.normalized;
            }
        }

        /// <summary>Where the blade is being aimed, and where the
        /// earth on it goes.</summary>
        public void SetTargets(Vector3 workFace, Vector3 heap)
        {
            face = workFace;
            heapTop = heap;
        }

        /// <summary>
        /// The swing marker, from <c>-1</c> to <c>1</c>. It rides the
        /// blade between low and high so the timing is readable
        /// without the gauge.
        /// </summary>
        public void SetSwing(float position)
        {
            swing = Mathf.Clamp(position, -1f, 1f);
        }

        /// <summary>
        /// Puts the spade somewhere and leaves it there — driven into
        /// the heap between acts, or laid down while a coffin goes
        /// past it.
        /// </summary>
        public void Rest(Vector3 position, Quaternion rotation)
        {
            isStriking = false;
            keyCount = 0;
            hasRestPose = false;
            swing = -1f;
            transform.SetPositionAndRotation(position, rotation);
        }

        /// <summary>
        /// Pulls the spade out of the ground it was standing in and
        /// brings it to the work. It is played from wherever the spade
        /// currently stands, so the object the player has been looking
        /// at beside the plot is the object that comes to hand — there
        /// is never a second one.
        /// </summary>
        public void PlayTakeUp()
        {
            fromPosition = transform.position;
            fromTilt = ReadTilt();
            keyCount = 0;
            keyIndex = 0;
            keyElapsed = 0f;
            isStriking = true;
            AddKey(
                fromPosition + (Vector3.up * FreeLiftMeters),
                26f,
                TakeUpSeconds * 0.45f);
            AddKey(Ready(), 18f, TakeUpSeconds * 0.55f);
        }

        /// <summary>
        /// Stands it back in the ground where it was. The pose is
        /// supplied rather than remembered, because between the taking
        /// up and the putting down the hole it stood beside may have
        /// changed shape.
        /// </summary>
        public void PlayLayDown(Vector3 position, Quaternion rotation)
        {
            fromPosition = transform.position;
            fromTilt = ReadTilt();
            keyCount = 0;
            keyIndex = 0;
            keyElapsed = 0f;
            isStriking = true;
            restPosition = position;
            restRotation = rotation;
            hasRestPose = true;
            AddKey(
                position + (Vector3.up * FreeLiftMeters),
                24f,
                LayDownSeconds * 0.55f);
            AddKey(position, 0f, LayDownSeconds * 0.45f);
        }

        /// <summary>
        /// Swings. A stroke that bit carries earth to the heap; one
        /// that grazed or jarred slides off the face and comes
        /// straight back.
        /// </summary>
        public void PlayStrike(CemeteryStrokeOutcome outcome)
        {
            fromPosition = transform.position;
            fromTilt = ReadTilt();
            keyCount = 0;
            keyIndex = 0;
            keyElapsed = 0f;
            isStriking = true;
            if (outcome == CemeteryStrokeOutcome.Bite)
            {
                AddKey(Drive(), 6f, DriveSeconds);
                AddKey(Lever(), -22f, LeverSeconds);
                AddKey(Lift(), -8f, LiftSeconds);
                AddKey(Dump(), -58f, DumpSeconds);
                AddKey(Ready(), 18f, ReturnSeconds);
                return;
            }

            // Off the face and away: the blade never gets under
            // anything, so there is nothing to carry.
            AddKey(Drive(), 22f, SlipSeconds);
            AddKey(Slip(), 34f, SlipSeconds);
            AddKey(Ready(), 18f, RecoverSeconds);
        }

        private void Update()
        {
            float deltaTime = Time.deltaTime;
            if (isStriking)
            {
                AdvanceStrike(deltaTime);
                return;
            }

            // Not striking: ride the swing, or simply hold the ready
            // pose while the hero decides which corner to work.
            float rise = (swing + 1f) * 0.5f;
            Vector3 target = Vector3.Lerp(
                SwingLow(),
                SwingHigh(),
                rise);
            float tilt = Mathf.Lerp(8f, 46f, rise);
            float blend = 1f - Mathf.Exp(
                -FollowSharpness * Mathf.Max(deltaTime, 0f));
            transform.SetPositionAndRotation(
                Vector3.Lerp(transform.position, target, blend),
                Quaternion.Slerp(
                    transform.rotation,
                    Rotation(tilt),
                    blend));
        }

        private void AdvanceStrike(float deltaTime)
        {
            if (keyIndex >= keyCount)
            {
                isStriking = false;
                return;
            }

            keyElapsed += Mathf.Max(deltaTime, 0f);
            float duration = Mathf.Max(
                keyDurations[keyIndex],
                0.0001f);
            float progress = Mathf.Clamp01(keyElapsed / duration);
            float eased = Mathf.SmoothStep(0f, 1f, progress);
            transform.SetPositionAndRotation(
                Vector3.Lerp(
                    fromPosition,
                    keyPositions[keyIndex],
                    eased),
                Quaternion.Slerp(
                    Rotation(fromTilt),
                    Rotation(keyTilts[keyIndex]),
                    eased));
            if (progress < 1f)
            {
                return;
            }

            fromPosition = keyPositions[keyIndex];
            fromTilt = keyTilts[keyIndex];
            keyElapsed = 0f;
            keyIndex++;
            if (keyIndex < keyCount)
            {
                return;
            }

            isStriking = false;
            if (!hasRestPose)
            {
                return;
            }

            // A lay-down ends on the authored resting pose rather than
            // on the last key. The key frames carry one pitch off the
            // working frame and the rest lean is stated in three axes,
            // so only the pose itself can close it exactly.
            hasRestPose = false;
            transform.SetPositionAndRotation(
                restPosition,
                restRotation);
            // Parked. Without this the idle branch below would pick it
            // straight back up and carry it to a hole nobody is
            // digging any more.
            enabled = false;
        }

        private void AddKey(
            Vector3 position,
            float tilt,
            float duration)
        {
            if (keyCount >= MaximumKeys)
            {
                throw new InvalidOperationException(
                    "A spade stroke is at most " +
                    MaximumKeys +
                    " poses long.");
            }

            keyPositions[keyCount] = position;
            keyTilts[keyCount] = tilt;
            keyDurations[keyCount] = duration;
            keyCount++;
        }

        private Vector3 Ready()
        {
            return face +
                   (Vector3.up * 0.34f) -
                   (workDirection * 0.18f);
        }

        private Vector3 SwingLow()
        {
            return face +
                   (Vector3.up * 0.18f) -
                   (workDirection * 0.06f);
        }

        private Vector3 SwingHigh()
        {
            return face +
                   (Vector3.up * 0.66f) -
                   (workDirection * 0.28f);
        }

        private Vector3 Drive()
        {
            return face - (Vector3.up * 0.10f);
        }

        private Vector3 Lever()
        {
            return face +
                   (Vector3.up * 0.02f) -
                   (workDirection * 0.05f);
        }

        private Vector3 Lift()
        {
            return face +
                   (Vector3.up * 0.52f) -
                   (workDirection * 0.30f);
        }

        private Vector3 Slip()
        {
            return face +
                   (Vector3.up * 0.12f) +
                   (workDirection * 0.20f);
        }

        private Vector3 Dump()
        {
            return heapTop + (Vector3.up * 0.34f);
        }

        /// <summary>
        /// The spade's own frame: local <c>+Z</c> looks the way the
        /// digger is working, so the shaft stands up local <c>+Y</c>
        /// and a positive pitch lays the handle back over his boot.
        /// </summary>
        private Quaternion Rotation(float tiltDegrees)
        {
            return Quaternion.LookRotation(
                       workDirection,
                       Vector3.up) *
                   Quaternion.Euler(tiltDegrees, 0f, 0f);
        }

        private float ReadTilt()
        {
            Quaternion local =
                Quaternion.Inverse(
                    Quaternion.LookRotation(
                        workDirection,
                        Vector3.up)) *
                transform.rotation;
            float pitch = local.eulerAngles.x;
            return pitch > 180f ? pitch - 360f : pitch;
        }
    }
}

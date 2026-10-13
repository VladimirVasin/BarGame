using System;
using UnityEngine;

namespace BarPromenade
{
    /// <summary>
    /// A bounded clearance pass on the original pelvis/leg chains. Actual root
    /// travel owns anticipation; repeated contact samples only pose the rig.
    /// Corpses are never gameplay supports and this does not move the controller.
    /// </summary>
    internal sealed class CombatCorpseStepPose
    {
        internal const float BootClearance = .09f;
        internal const float MaximumCorpseRise = .70f;
        private const float FootRadius = .075f;
        private const float PreviewDistance = .30f;
        private readonly Transform frame;
        private readonly Transform[] bones;
        private readonly Vector3[] basePositions = new Vector3[7];
        private readonly Quaternion[] baseRotations = new Quaternion[7];
        private readonly Vector3[] footForward = new Vector3[2];
        private readonly Vector3[] previousFeet = new Vector3[2], sampledFeet = new Vector3[2];
        private readonly Vector3[] targets = new Vector3[2];
        private readonly Quaternion[] rotations = new Quaternion[2];
        private readonly float[] legLengths = new float[2], lifts = new float[2];
        private readonly float[] surfaces = { float.NegativeInfinity, float.NegativeInfinity };
        private readonly float groundOffset;
        private Vector3 previousRoot, travelDirection;
        private float anticipationWeight;
        private bool hasSample, sweptFootTravel;

        internal bool IsApplied { get; private set; }
        internal float LeftLift => lifts[0];
        internal float RightLift => lifts[1];
        internal float PelvisLift { get; private set; }
        internal Vector3 LeftTarget => targets[0];
        internal Vector3 RightTarget => targets[1];
        internal float LeftSurfaceY => surfaces[0];
        internal float RightSurfaceY => surfaces[1];

        internal CombatCorpseStepPose(Transform rig, Transform actorFrame)
            : this(actorFrame,
                Find(rig, "pelvis"), Find(rig, "thigh.L"), Find(rig, "shin.L"), Find(rig, "foot.L"),
                Find(rig, "thigh.R"), Find(rig, "shin.R"), Find(rig, "foot.R")) { }

        internal CombatCorpseStepPose(Transform actorFrame, Transform pelvis,
            Transform leftThigh, Transform leftShin, Transform leftFoot,
            Transform rightThigh, Transform rightShin, Transform rightFoot)
        {
            frame = actorFrame;
            bones = new[] { pelvis, leftThigh, leftShin, leftFoot, rightThigh, rightShin, rightFoot };
            foreach (Transform bone in bones)
                if (bone == null) throw new InvalidOperationException("Corpse traversal needs the original pelvis and leg chains.");
            for (int side = 0; side < 2; side++)
            {
                int chain = 1 + side * 3;
                legLengths[side] = Vector3.Distance(bones[chain].position, bones[chain + 1].position) +
                    Vector3.Distance(bones[chain + 1].position, bones[chain + 2].position);
                footForward[side] = bones[chain + 2].InverseTransformDirection(frame.forward);
            }
            groundOffset = Mathf.Min(leftFoot.position.y, rightFoot.position.y) - frame.position.y - BootClearance;
            Reset();
        }

        private static Transform Find(Transform rig, string name) => CityPedestrianHandProps.FindSocket(rig, name);

        internal void Advance(float seconds)
        {
            if (seconds <= 0f) return;
            Vector3 movement = Vector3.ProjectOnPlane(frame.position - previousRoot, Vector3.up);
            previousRoot = frame.position;
            // Teleports/reset are not strides, and must not retain a path across
            // an entire arena. Ordinary movement remains measured, including runs.
            if (movement.sqrMagnitude > 2.25f) { ClearState(); return; }
            sweptFootTravel = hasSample;
            if (hasSample)
                for (int side = 0; side < 2; side++) previousFeet[side] = sampledFeet[side];
            if (movement.sqrMagnitude > .000001f)
            {
                travelDirection = movement.normalized;
                anticipationWeight = 1f;
            }
            else anticipationWeight = Mathf.MoveTowards(anticipationWeight, 0f, seconds / .18f);
        }

        internal void Apply()
        {
            Restore();
            float groundY = frame.position.y + groundOffset;
            bool needsClearance = false;
            for (int side = 0; side < 2; side++)
            {
                Transform foot = bones[3 + side * 3];
                sampledFeet[side] = targets[side] = foot.position;
                rotations[side] = foot.rotation;
                surfaces[side] = float.NegativeInfinity;
                Vector3 forward = Vector3.ProjectOnPlane(foot.TransformDirection(footForward[side]), Vector3.up).normalized;
                if (forward.sqrMagnitude < .001f) forward = frame.forward;
                Vector3 heel = foot.position - forward * .035f, toe = foot.position + forward * .15f;
                float lift = 0f;
                if (Clearance(heel, toe, groundY, out float surface))
                {
                    surfaces[side] = surface;
                    lift = Mathf.Max(0f, surface + BootClearance - foot.position.y);
                }
                // An authored sole can move while the root is stationary (a
                // combat step or lunge). Close that real swept contour too.
                if (sweptFootTravel && Clearance(previousFeet[side], foot.position, groundY, out float sweptSurface))
                {
                    surfaces[side] = Mathf.Max(surfaces[side], sweptSurface);
                    lift = Mathf.Max(lift, sweptSurface + BootClearance - foot.position.y);
                }
                if (anticipationWeight > 0f && travelDirection.sqrMagnitude > .001f)
                {
                    Vector3 lead = Vector3.Dot(forward, travelDirection) >= 0f ? toe : heel;
                    Vector3 trail = Vector3.Dot(forward, travelDirection) >= 0f ? heel : toe;
                    Vector3 end = lead + travelDirection * PreviewDistance;
                    if (Clearance(trail, end, groundY, out float previewSurface, out float firstHit))
                    {
                        // The service returns the first actual footprint hit in
                        // one bounded scan. Distance provides anticipation without
                        // another gait clock or repeated contour searches.
                        Vector3 firstContour = Vector3.Lerp(trail, end, firstHit);
                        float ahead = Mathf.Max(0f, Vector3.Dot(firstContour - lead, travelDirection));
                        float ramp = Mathf.SmoothStep(0f, 1f, 1f - ahead / PreviewDistance);
                        lift = Mathf.Max(lift, Mathf.Max(0f, previewSurface + BootClearance - foot.position.y) *
                            ramp * anticipationWeight);
                    }
                }
                lifts[side] = Mathf.Max(0f, lift);
                targets[side] += Vector3.up * lifts[side];
                needsClearance |= lifts[side] > .001f;
            }
            hasSample = true;
            PelvisLift = 0f;
            if (!needsClearance) return;
            for (int i = 0; i < bones.Length; i++)
            { basePositions[i] = bones[i].localPosition; baseRotations[i] = bones[i].localRotation; }
            IsApplied = true;
            // Raise the weight only while both contacts have anatomical reserve.
            // A ground-planted boot remains fixed rather than being pulled up by
            // the other knee's lift, and no chain is stretched or shortened.
            float pelvisRise = Mathf.Min(.10f, Mathf.Max(lifts[0], lifts[1]) * .25f);
            for (int side = 0; side < 2; side++)
            {
                Vector3 hip = bones[1 + side * 3].position;
                Vector3 horizontal = Vector3.ProjectOnPlane(hip - targets[side], Vector3.up);
                float reach = legLengths[side] * .998f;
                float reserve = targets[side].y + Mathf.Sqrt(Mathf.Max(0f, reach * reach - horizontal.sqrMagnitude)) - hip.y;
                pelvisRise = Mathf.Min(pelvisRise, Mathf.Max(0f, reserve));
            }
            PelvisLift = pelvisRise;
            bones[0].position += Vector3.up * pelvisRise;
            for (int side = 0; side < 2; side++)
            {
                int chain = 1 + side * 3;
                Vector3 kneeHint = bones[chain].position + frame.forward * .65f +
                    frame.right * (side == 0 ? -.08f : .08f);
                LimbTwoBoneIk.Solve(bones[chain], bones[chain + 1], bones[chain + 2],
                    targets[side], rotations[side], kneeHint, 1f, .999f, true);
            }
        }

        private static bool Clearance(Vector3 from, Vector3 to, float groundY, out float surface)
        {
            return Clearance(from, to, groundY, out surface, out _);
        }

        private static bool Clearance(Vector3 from, Vector3 to, float groundY, out float surface, out float firstHit)
        {
            return CombatCorpseTraversal.TryGetClearance(from, to, FootRadius, groundY + MaximumCorpseRise,
                out surface, out firstHit) && surface > groundY + .015f;
        }

        internal void Restore()
        {
            if (IsApplied)
                for (int i = 0; i < bones.Length; i++)
                    if (bones[i] != null)
                    { bones[i].localPosition = basePositions[i]; bones[i].localRotation = baseRotations[i]; }
            IsApplied = false;
        }

        internal void Reset()
        {
            Restore();
            ClearState();
        }

        // Physics inherits this complete pose; restoring the previous clip here
        // would remove the visible bent knee immediately before the fall.
        internal void Forget()
        {
            IsApplied = false;
            ClearState();
        }

        private void ClearState()
        {
            previousRoot = frame.position;
            travelDirection = Vector3.zero;
            anticipationWeight = PelvisLift = 0f;
            hasSample = sweptFootTravel = false;
            for (int side = 0; side < 2; side++)
            {
                lifts[side] = 0f;
                surfaces[side] = float.NegativeInfinity;
                previousFeet[side] = sampledFeet[side] = targets[side] = bones[3 + side * 3].position;
            }
        }
    }
}

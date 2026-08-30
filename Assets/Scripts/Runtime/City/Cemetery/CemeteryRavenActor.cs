using System;
using UnityEngine;

namespace BarPromenade
{
    /// <summary>
    /// One 3D cemetery raven. The visual is the passive authored
    /// prefab; this actor adopts its meshes under the exported pivot
    /// empties (the stairwell cat's wheelchair-mechanism pattern) and
    /// articulates them from the untouched pure timelines — the idle
    /// model while perched, a flight model's samples while flying.
    ///
    /// All pose writes are deltas over rest poses cached at
    /// initialization, about the bird's own world axes — never
    /// absolute local eulers, so the FBX axis conversion stays out of
    /// the pose math. The host root's origin is the feet-contact
    /// point, so perching is one SetPositionAndRotation and a flight
    /// path is written straight to the root.
    ///
    /// Sign conventions, stated once beside the writes: the pose
    /// struct's channels are anatomical (positive head yaw is the
    /// bird's LEFT, positive body pitch raises the breast, positive
    /// flap lifts that wing's tip), and this file is the single place
    /// they are turned into Unity's left-handed rotations.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class CemeteryRavenActor : MonoBehaviour
    {
        private struct RestPose
        {
            public Vector3 Position;
            public Quaternion Rotation;
            public Vector3 Scale;

            public static RestPose Capture(Transform target)
            {
                return new RestPose
                {
                    Position = target.localPosition,
                    Rotation = target.localRotation,
                    Scale = target.localScale
                };
            }
        }

        private CemeteryRavenRigAnchors anchors;
        private CemeteryRavenIdleModel idleModel;
        private CemeteryRavenHeadModel headModel;
        private CemeteryRavenFlightModel flight;
        private double flightElapsedSeconds;
        private bool flightDone;
        private bool perched;
        private bool hasHeadTarget;
        private Vector3 headTargetPoint;
        private RestPose bodyRootRest;
        private RestPose headRest;
        private RestPose wingLeftRest;
        private RestPose wingRightRest;
        private RestPose tailRest;

        public bool IsInitialized { get; private set; }

        public CemeteryRavenRigAnchors Anchors => anchors;

        /// <summary>True while the bird stands on its perch and the
        /// idle and head timelines are what moves it.</summary>
        public bool IsPerched => perched;

        /// <summary>True while a flight model owns the root.</summary>
        public bool HasFlight => flight != null;

        /// <summary>
        /// True once the last begun flight reported done. The
        /// controller resets its own per-phase bookkeeping; this flag
        /// only answers for the flight most recently handed over.
        /// </summary>
        public bool IsFlightDone => flightDone;

        public float HeadYawDegrees =>
            headModel != null ? headModel.CurrentYawDegrees : 0f;

        public CemeteryRavenIdleKind CurrentIdleKind =>
            idleModel != null
                ? idleModel.CurrentKind
                : CemeteryRavenIdleKind.Breathe;

        public void Initialize(
            CemeteryRavenRigAnchors rigAnchors,
            int seed,
            double idleStartOffsetSeconds)
        {
            if (IsInitialized)
            {
                throw new InvalidOperationException(
                    "The cemetery raven actor is already initialized.");
            }

            if (rigAnchors == null)
            {
                throw new ArgumentNullException(nameof(rigAnchors));
            }

            if (!rigAnchors.IsBound)
            {
                throw new ArgumentException(
                    "The cemetery raven rig anchors are not fully " +
                    "bound.",
                    nameof(rigAnchors));
            }

            anchors = rigAnchors;
            BindArticulation();
            CaptureRestPoses();

            idleModel = new CemeteryRavenIdleModel(
                seed,
                idleStartOffsetSeconds);
            headModel = new CemeteryRavenHeadModel();
            IsInitialized = true;
            ApplyCurrentPerchedPose();
        }

        /// <summary>
        /// Sets the bird down: the host root IS the feet-contact
        /// point, and the yaw is a plain compass heading because the
        /// factory's half turn already aims the prefab at host +Z.
        /// </summary>
        public void SetPerched(Vector3 point, float yawDegrees)
        {
            flight = null;
            flightDone = false;
            perched = true;
            transform.SetPositionAndRotation(
                point,
                Quaternion.Euler(0f, yawDegrees, 0f));
            if (IsInitialized)
            {
                ApplyCurrentPerchedPose();
            }
        }

        /// <summary>
        /// Hands the root to a flight. The actor owns every transform
        /// write from here: each frame it evaluates the pure timeline
        /// and copies position, yaw and pose out of the sample, so the
        /// path is exactly what the EditMode tests proved on the model.
        /// A finishing takeoff hides the bird (it is past the fog); a
        /// finishing return lands it back into perched mode on the
        /// model's own float-exact endpoint.
        /// </summary>
        public void BeginFlight(CemeteryRavenFlightModel flightModel)
        {
            if (flightModel == null)
            {
                throw new ArgumentNullException(nameof(flightModel));
            }

            flight = flightModel;
            flightElapsedSeconds = 0d;
            flightDone = false;
            perched = false;
            SetVisible(true);
            if (IsInitialized)
            {
                ApplyFlightSample(flight.Evaluate(0d));
            }
        }

        /// <summary>Renderer visibility across every binding — the
        /// Away state, and the hidden wait before an arrival.</summary>
        public void SetVisible(bool visible)
        {
            if (anchors == null)
            {
                return;
            }

            for (int index = 0;
                 index < anchors.Renderers.Count;
                 index++)
            {
                Renderer renderer = anchors.Renderers[index];
                if (renderer != null)
                {
                    renderer.enabled = visible;
                }
            }
        }

        /// <summary>
        /// What the head may follow this frame. The director owns
        /// target selection — during an owned grave-work session it
        /// hands in no target at all, and the head model walks itself
        /// back to neutral through its own hysteresis.
        /// </summary>
        public void SetHeadTarget(bool hasTarget, Vector3 worldPoint)
        {
            hasHeadTarget = hasTarget;
            headTargetPoint = worldPoint;
        }

        public void AdvancePresentation(float deltaTime)
        {
            if (!IsInitialized)
            {
                return;
            }

            if (flight != null)
            {
                if (deltaTime > 0f)
                {
                    flightElapsedSeconds += deltaTime;
                }

                CemeteryRavenFlightSample sample =
                    flight.Evaluate(flightElapsedSeconds);
                ApplyFlightSample(sample);
                if (sample.Done && !flightDone)
                {
                    flightDone = true;
                    if (flight.Kind ==
                        CemeteryRavenFlightKind.Takeoff)
                    {
                        // Past the fog: nothing left to draw.
                        SetVisible(false);
                        flight = null;
                    }
                    else
                    {
                        // The Done sample already wrote the exact
                        // perch with everything zeroed; the idle
                        // takes over from a clean rest.
                        flight = null;
                        perched = true;
                    }
                }

                return;
            }

            if (!perched)
            {
                return;
            }

            idleModel.Advance(deltaTime);
            if (!idleModel.IsPreening)
            {
                // A preening bird's beak is in its coverts: the head
                // model is frozen for the span (the cat's grooming
                // rule), and the pose rules replace the yaw anyway.
                headModel.Update(
                    hasHeadTarget,
                    ComputePlanarDistance(headTargetPoint),
                    ComputeYawToward(headTargetPoint),
                    deltaTime);
            }

            ApplyCurrentPerchedPose();
        }

        /// <summary>
        /// Adopts the flat-exported meshes under their pivots. The FBX
        /// deliberately ships every mesh beside the empties with its
        /// origin ON its pivot, so this runtime reparent is exact.
        /// </summary>
        private void BindArticulation()
        {
            for (int index = 0;
                 index < anchors.RendererBindings.Count;
                 index++)
            {
                CemeteryRavenRendererBinding binding =
                    anchors.RendererBindings[index];
                if (binding == null ||
                    binding.Renderer == null ||
                    string.IsNullOrEmpty(binding.PivotName))
                {
                    continue;
                }

                Transform pivot = ResolvePivot(binding.PivotName);
                if (pivot == null)
                {
                    throw new InvalidOperationException(
                        $"The raven part '{binding.RendererName}' " +
                        $"names unknown pivot '{binding.PivotName}'.");
                }

                binding.Renderer.transform.SetParent(pivot, true);
            }
        }

        private Transform ResolvePivot(string pivotName)
        {
            switch (pivotName)
            {
                case CemeteryRavenRigAnchors.BodyRootPivotName:
                    return anchors.BodyRootPivot;
                case CemeteryRavenRigAnchors.HeadPivotName:
                    return anchors.HeadPivot;
                case CemeteryRavenRigAnchors.WingLeftPivotName:
                    return anchors.WingLeftPivot;
                case CemeteryRavenRigAnchors.WingRightPivotName:
                    return anchors.WingRightPivot;
                case CemeteryRavenRigAnchors.TailPivotName:
                    return anchors.TailPivot;
                default:
                    return null;
            }
        }

        private void CaptureRestPoses()
        {
            bodyRootRest = RestPose.Capture(anchors.BodyRootPivot);
            headRest = RestPose.Capture(anchors.HeadPivot);
            wingLeftRest = RestPose.Capture(anchors.WingLeftPivot);
            wingRightRest = RestPose.Capture(anchors.WingRightPivot);
            tailRest = RestPose.Capture(anchors.TailPivot);
        }

        private void ApplyCurrentPerchedPose()
        {
            ApplyPose(CemeteryRavenPoseRules.IdlePose(
                idleModel.CurrentKind,
                idleModel.EventProgress01,
                idleModel.Breathe01,
                idleModel.EventSign,
                idleModel.PreenOnLeftWing,
                headModel.CurrentYawDegrees));
        }

        private void ApplyFlightSample(
            in CemeteryRavenFlightSample sample)
        {
            transform.SetPositionAndRotation(
                sample.Position,
                Quaternion.Euler(0f, sample.YawDegrees, 0f));
            ApplyPose(CemeteryRavenPoseRules.FlightPose(
                sample.WingFold01,
                sample.FlapPhaseRadians,
                sample.BodyPitchDegrees,
                sample.BodyDipMeters));
        }

        /// <summary>
        /// The direction the bird faces at rest. The FBX geometry
        /// faces model-local -Z (Blender -Y through the axis bake)
        /// and the prefab's inner half turn aims the HOST at +Z, so
        /// the live geometry always faces the negation of the model
        /// root's axes — the cat's rule, unchanged.
        /// </summary>
        private Vector3 RavenForward => -anchors.ModelRoot.forward;

        private Vector3 RavenRight => -anchors.ModelRoot.right;

        /// <summary>
        /// Yaw from the head to a world point, POSITIVE TOWARD THE
        /// BIRD'S LEFT — the pose struct's convention, so a preen
        /// (whose yaw is authored, +1 meaning the left wing) and a
        /// tracked hero pass through the same channel unconverted.
        /// </summary>
        private float ComputeYawToward(Vector3 worldTarget)
        {
            Vector3 forward = RavenForward;
            forward.y = 0f;
            Vector3 toTarget =
                worldTarget - anchors.HeadPivot.position;
            toTarget.y = 0f;
            if (forward.sqrMagnitude <= 0.000001f ||
                toTarget.sqrMagnitude <= 0.000001f)
            {
                return 0f;
            }

            return -Vector3.SignedAngle(
                forward,
                toTarget,
                Vector3.up);
        }

        private float ComputePlanarDistance(Vector3 worldTarget)
        {
            Vector3 delta = worldTarget - transform.position;
            delta.y = 0f;
            return delta.magnitude;
        }

        private void ApplyPose(in CemeteryRavenPose pose)
        {
            Vector3 ravenForward = RavenForward;
            Vector3 ravenRight = RavenRight;

            // Body root: positive dip sinks toward the feet; positive
            // pitch raises the breast, which in Unity's left-handed
            // frame is a NEGATIVE turn about the bird's right axis
            // (positive noses down); positive lean rolls about the
            // bird's own forward. The legs ride this pivot, so a dip
            // reads as the whole bird settling on its feet.
            Transform bodyRoot = anchors.BodyRootPivot;
            Quaternion bodyDelta =
                ParentSpaceRotation(
                    bodyRoot,
                    pose.BodyLeanDegrees,
                    ravenForward) *
                ParentSpaceRotation(
                    bodyRoot,
                    -pose.BodyPitchDegrees,
                    ravenRight);
            bodyRoot.localRotation =
                bodyDelta * bodyRootRest.Rotation;
            bodyRoot.localPosition =
                bodyRootRest.Position +
                ParentSpaceVector(
                    bodyRoot,
                    Vector3.up * -pose.BodyDipMeters);

            // Head: positive yaw is the bird's left. Unity's positive
            // turn about world up goes to the RIGHT, so the applied
            // angle is negated; ComputeYawToward already speaks the
            // same left-positive dialect, so the two cancel exactly.
            // Positive pitch dips the beak — Unity's own positive
            // about the right axis.
            Transform head = anchors.HeadPivot;
            Quaternion headDelta =
                ParentSpaceRotation(
                    head,
                    -pose.HeadYawDegrees,
                    Vector3.up) *
                ParentSpaceRotation(
                    head,
                    pose.HeadPitchDegrees,
                    ravenRight);
            head.localRotation = headDelta * headRest.Rotation;

            // Wings: the folded slabs point tail-ward, and a deploy
            // yaws each tip outward about the vertical through its
            // shoulder — from "behind" toward "that wing's side" is
            // +up for the left wing and -up for the right, whatever
            // the bird faces, because both directions turn with it.
            // The flap beats the rigid plane about the bird's long
            // axis; lifting a tip means -forward for the left wing
            // and +forward for the right, by the same argument.
            Transform wingLeft = anchors.WingLeftPivot;
            wingLeft.localRotation =
                ParentSpaceRotation(
                    wingLeft,
                    pose.WingFoldLeftDegrees,
                    Vector3.up) *
                ParentSpaceRotation(
                    wingLeft,
                    -pose.WingFlapLeftDegrees,
                    ravenForward) *
                wingLeftRest.Rotation;
            Transform wingRight = anchors.WingRightPivot;
            wingRight.localRotation =
                ParentSpaceRotation(
                    wingRight,
                    -pose.WingFoldRightDegrees,
                    Vector3.up) *
                ParentSpaceRotation(
                    wingRight,
                    pose.WingFlapRightDegrees,
                    ravenForward) *
                wingRightRest.Rotation;

            // Tail: it extends tail-ward, so a positive turn about
            // the bird's right axis carries the tip UP — which is
            // exactly what positive tail pitch promises.
            Transform tail = anchors.TailPivot;
            tail.localRotation =
                ParentSpaceRotation(
                    tail,
                    pose.TailPitchDegrees,
                    ravenRight) *
                tailRest.Rotation;
        }

        private static Quaternion ParentSpaceRotation(
            Transform target,
            float degrees,
            Vector3 worldAxis)
        {
            if (degrees == 0f)
            {
                return Quaternion.identity;
            }

            Vector3 axis = target.parent != null
                ? target.parent.InverseTransformDirection(worldAxis)
                : worldAxis;
            return Quaternion.AngleAxis(degrees, axis);
        }

        private static Vector3 ParentSpaceVector(
            Transform target,
            Vector3 worldVector)
        {
            return target.parent != null
                ? target.parent.InverseTransformVector(worldVector)
                : worldVector;
        }

        private void Update()
        {
            AdvancePresentation(Time.deltaTime);
        }

        private void OnDestroy()
        {
            IsInitialized = false;
        }
    }
}

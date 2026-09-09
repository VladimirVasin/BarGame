using UnityEngine;

namespace BarPromenade
{
    /// <summary>One hand owns a cabin action; the other remains on the wheel.</summary>
    [DefaultExecutionOrder(340)]
    [DisallowMultipleComponent]
    public sealed class LastRouteFerrymanCabinActions : MonoBehaviour
    {
        public const float ReactionDelaySeconds = 2f;
        public const float ReachSeconds = 0.8f;
        public const float PushSeconds = 0.65f;
        public const float ReturnSeconds = 0.75f;
        public const float RadioTurnSeconds = 1.2f;
        public const float ContactTolerance = 0.025f;

        private enum ActionKind { None, Glovebox, Radio }
        private enum ReachPhase { None, Reaching, Operating, Returning }
        private ActionKind actionKind;
        private ReachPhase phase;
        private float elapsed;
        private float gloveboxWait = -1f;
        private bool radioRequested;
        private int requestedStation;
        private bool committingRadio;
        private bool regripping;
        private float returnFromWeight;
        private float initialOpenness;
        private Vector3 returnWristLocal;
        private Quaternion returnRotationLocal;
        private Vector3 completedWristLocal;
        private Quaternion completedRotationLocal;
        private bool hasCompletedPose;
        private LastRouteFerrymanPresentation ferryman;
        private LastRouteCarDashboard dashboard;
        private LastRouteCarAssetRegistry car;
        private LastRouteCarSeatInteraction seat;
        private LastRouteRideController ride;
        private LastRouteFerrymanRigAnchors arms;
        private Transform spine;
        private Transform chest;
        private MeshFilter catchMesh;
        private MeshFilter lidMesh;
        private Vector3 lidNormalLocal;
        private Transform upper;
        private Transform forearm;
        private Transform hand;
        private Transform otherSocket;
        private Transform otherGrip;
        private bool useLeft;
        private LastRouteFerrymanHandFrame handFrame;
        private LastRouteFerrymanHandFrame leftFrame;
        private LastRouteFerrymanHandFrame rightFrame;
        private bool framesReady;
        private Vector3 radioRadialInBody;
        private Vector3 radioContactNormalInBody;
        private Vector3 activeContactInHand;
        private readonly NpcAttentionHeadLayer attention = new NpcAttentionHeadLayer();
        private Quaternion spineBase;
        private Quaternion chestBase;
        private Vector3 spineBasePosition;
        private bool bodyWritten;

        public bool IsClosingGlovebox => actionKind == ActionKind.Glovebox;
        public bool IsTuningRadio => radioRequested || actionKind == ActionKind.Radio;
        public bool HasLidContact => IsClosingGlovebox && phase == ReachPhase.Operating;
        public bool HasRadioContact => actionKind == ActionKind.Radio &&
            phase == ReachPhase.Operating && !regripping && HandContactDistance <= ContactTolerance;
        public float HandContactDistance { get; private set; }
        public float OtherHandWheelDistance { get; private set; }
        public float PalmFacingSurface { get; private set; }
        public float WristBendDegrees { get; private set; }
        public float WristFrameStepDegrees { get; private set; }
        public float WristAngularSpeed { get; private set; }
        public float ElbowHeightFromShoulder { get; private set; }
        public float LeanWeight { get; private set; }
        public float HeadTurnWeight => attention.Weight;
        public Transform ClosingHand => hand;
        public Vector3 PalmContactPoint => hand == null ? Vector3.zero :
            hand.position + hand.rotation * activeContactInHand;
        public Vector3 LidSurfaceNormal => lidMesh != null
            ? lidMesh.transform.TransformDirection(lidNormalLocal).normalized : Vector3.up;
        public Vector3 LidContactPoint => SurfacePoint(catchMesh, LidSurfaceNormal);
        public Vector3 RadioContactPoint => RadioGripPoint(car.Body.TransformDirection(radioContactNormalInBody));

        public static LastRouteFerrymanCabinActions Create(Transform parent,
            LastRouteFerrymanPresentation presentation, LastRouteCarDriver driver,
            LastRouteRideController controller)
        {
            if (presentation == null || driver == null || controller == null) return null;
            var action = parent.gameObject.AddComponent<LastRouteFerrymanCabinActions>();
            action.ferryman = presentation;
            action.ride = controller;
            action.dashboard = driver.GetComponent<LastRouteCarDashboard>();
            action.car = driver.GetComponentInChildren<LastRouteCarAssetRegistry>(true);
            action.seat = driver.GetComponentInChildren<LastRouteCarSeatInteraction>(true);
            action.arms = presentation.GetComponentInChildren<LastRouteFerrymanRigAnchors>(true);
            var registry = presentation.GetComponentInChildren<CityPedestrianAssetRegistry>(true);
            if (registry != null)
            {
                action.attention.Bind(presentation.transform, registry.HeadAnchor, registry.Animator);
                foreach (Transform bone in registry.Animator.GetComponentsInChildren<Transform>(true))
                {
                    if (bone.name == "spine") action.spine = bone;
                    if (bone.name == "chest") action.chest = bone;
                }
            }
            if (action.car != null)
                foreach (LastRouteCarRendererBinding binding in action.car.Bindings)
                {
                    if (binding.Role == "glovebox_catch")
                        action.catchMesh = binding.Renderer.GetComponent<MeshFilter>();
                    if (binding.Role == "glovebox_lid")
                        action.lidMesh = binding.Renderer.GetComponent<MeshFilter>();
                }
            if (action.lidMesh != null && action.catchMesh != null)
            {
                Vector3 size = action.lidMesh.sharedMesh.bounds.size;
                Vector3 axis = size.x < size.y && size.x < size.z ? Vector3.right :
                    size.y < size.z ? Vector3.up : Vector3.forward;
                Vector3 normal = action.lidMesh.transform.TransformDirection(axis);
                Vector3 offset = Center(action.catchMesh) - Center(action.lidMesh);
                action.lidNormalLocal = Vector3.Dot(normal, offset) >= 0f ? axis : -axis;
            }
            if (action.dashboard != null)
            {
                action.dashboard.Operated += action.OnDashboardOperated;
            }
            return action;
        }

        private bool HasPassenger => ferryman != null && ferryman.IsDriving &&
            seat != null && seat.IsSeated;

        public void RequestRadioRetune()
        {
            if (!HasPassenger || dashboard == null || !dashboard.RadioOn || IsTuningRadio) return;
            requestedStation = dashboard.TuningDetent;
            radioRequested = true;
        }

        private void OnDashboardOperated(LastRouteCarDashboardTarget target)
        {
            if (!HasPassenger) return;
            if (target == LastRouteCarDashboardTarget.RadioPower ||
                target == LastRouteCarDashboardTarget.RadioTuning)
            {
                ride.OnRadioSettingChanged();
                if (committingRadio) return;
                radioRequested = false;
                if (actionKind == ActionKind.Radio && phase != ReachPhase.Returning)
                    BeginReturn(LeanWeight);
            }
            else if (target == LastRouteCarDashboardTarget.Glovebox)
            {
                gloveboxWait = dashboard.GloveboxOpen ? ReactionDelaySeconds : -1f;
                if (dashboard.GloveboxOpen) ride.OnGloveboxOpened();
            }
        }

        // Restore before the manually evaluated driving graph, including bones
        // whose current clip has no translation curve.
        private void Update() => RestorePose();

        private void LateUpdate()
        {
            if (dashboard == null || car == null) return;
            bool hidden = SceneTransitionService.IsTransitioning || (ride.Fade != null && !ride.Fade.IsClear);
            if (!HasPassenger || hidden)
            {
                CancelReach();
                attention.Clear();
                return;
            }
            float step = PauseMenuController.IsAnyPaused || GameTimeScaleRuntime.IsPaused
                ? 0f : Mathf.Min(Time.unscaledDeltaTime, 0.1f);
            attention.SetFocus(ride.IsSpeaking
                ? car.PassengerSeatAnchor.position + car.transform.up * 0.65f : (Vector3?)null);
            if (gloveboxWait >= 0f) gloveboxWait = Mathf.Max(0f, gloveboxWait - step);
            if (phase == ReachPhase.None)
            {
                if (gloveboxWait == 0f)
                {
                    gloveboxWait = -1f;
                    if (dashboard.GloveboxOpen) PrepareReach(ActionKind.Glovebox);
                }
                else if (radioRequested)
                {
                    radioRequested = false;
                    if (dashboard.RadioOn && dashboard.TuningDetent == requestedStation)
                        PrepareReach(ActionKind.Radio);
                }
            }
            if (phase != ReachPhase.None)
            {
                elapsed += step;
                ApplyReach(step);
            }
            attention.Apply(step);
        }

        private void PrepareReach(ActionKind kind)
        {
            if (arms == null || spine == null || catchMesh == null || lidMesh == null ||
                dashboard.RadioTuningKnobMesh == null || arms.LeftGripSocket == null ||
                arms.RightGripSocket == null) return;
            if (!framesReady)
            {
                if (!LastRouteFerrymanHandFrame.TryCreate(arms.LeftHand, arms.LeftGripSocket, true,
                        ferryman.transform, out leftFrame) ||
                    !LastRouteFerrymanHandFrame.TryCreate(arms.RightHand, arms.RightGripSocket, false,
                        ferryman.transform, out rightFrame)) return;
                framesReady = true;
            }
            Vector3 target = kind == ActionKind.Glovebox ? LidContactPoint : Center(dashboard.RadioTuningKnobMesh);
            useLeft = Vector3.Distance(arms.LeftUpperArm.position, target) <
                      Vector3.Distance(arms.RightUpperArm.position, target);
            upper = useLeft ? arms.LeftUpperArm : arms.RightUpperArm;
            forearm = useLeft ? arms.LeftForearm : arms.RightForearm;
            hand = useLeft ? arms.LeftHand : arms.RightHand;
            handFrame = useLeft ? leftFrame : rightFrame;
            otherSocket = useLeft ? arms.RightGripSocket : arms.LeftGripSocket;
            otherGrip = useLeft ? car.RightSteeringGrip : car.LeftSteeringGrip;
            activeContactInHand = kind == ActionKind.Radio ? handFrame.KnobPointInHand : handFrame.PalmPointInHand;
            // Grasp the knob's side with the distal palm. Pressing a flat palm
            // against its face would require a sharply folded wrist.
            Vector3 radial = Vector3.ProjectOnPlane(forearm.position - target, dashboard.RadioKnobAxis).normalized;
            radioRadialInBody = car.Body.InverseTransformDirection(radial);
            radioContactNormalInBody = radioRadialInBody;
            initialOpenness = dashboard.GloveboxOpenness;
            actionKind = kind;
            hasCompletedPose = false;
            phase = ReachPhase.Reaching;
            elapsed = 0f;
            if (kind == ActionKind.Glovebox) dashboard.SetGloveboxInputLocked(true);
        }

        private void ApplyReach(float step)
        {
            float weight = phase == ReachPhase.Reaching ? Smooth(elapsed / ReachSeconds) :
                phase == ReachPhase.Returning ? returnFromWeight * (1f - Smooth(elapsed / ReturnSeconds)) : 1f;
            LeanWeight = weight;
            bool radio = actionKind == ActionKind.Radio;
            if (!radio && phase == ReachPhase.Operating)
                dashboard.SetGloveboxOpenness(initialOpenness * (1f - Smooth(elapsed / PushSeconds)));

            Vector3 normal = LidSurfaceNormal;
            Vector3 contact = LidContactPoint;
            regripping = false;
            if (radio)
            {
                // A detent is 120 degrees. Two short turns with a released
                // regrip keep the wrist within a 60-degree working arc.
                float turn = 0f;
                float progress = 0f;
                if (phase == ReachPhase.Operating)
                {
                    float stroke = Mathf.Clamp(elapsed / RadioTurnSeconds * 3f, 0f, 3f);
                    if (stroke < 1f) { turn = Smooth(stroke); progress = turn * 0.5f; }
                    else if (stroke < 2f)
                    {
                        regripping = true;
                        turn = 1f - Smooth(stroke - 1f);
                        progress = 0.5f;
                    }
                    else { turn = Smooth(stroke - 2f); progress = 0.5f + turn * 0.5f; }
                    dashboard.SetDriverTuningProgress(progress);
                }
                normal = Quaternion.AngleAxis(-30f + 60f * turn, dashboard.RadioKnobAxis) *
                    car.Body.TransformDirection(radioRadialInBody);
                radioContactNormalInBody = car.Body.InverseTransformDirection(normal);
                contact = RadioGripPoint(normal);
                if (regripping)
                    contact += normal * (0.025f * Mathf.Sin((elapsed / RadioTurnSeconds * 3f - 1f) * Mathf.PI));
            }
            Quaternion desiredRotation = ContactRotation(contact - upper.position, normal);
            Vector3 desiredWrist = contact - desiredRotation * activeContactInHand;
            if (phase == ReachPhase.Returning)
            {
                desiredWrist = car.Body.TransformPoint(returnWristLocal);
                desiredRotation = car.Body.rotation * returnRotationLocal;
            }

            spineBase = spine.localRotation;
            spineBasePosition = spine.localPosition;
            chestBase = chest != null ? chest.localRotation : Quaternion.identity;
            bodyWritten = true;
            Vector3 towardTarget = Vector3.ProjectOnPlane(contact - spine.position, Vector3.up).normalized;
            Vector3 leanAxis = Vector3.Cross(Vector3.up, towardTarget);
            spine.rotation = Quaternion.AngleAxis((radio ? 12f : 22f) * weight, leanAxis) * spine.rotation;
            if (chest != null)
                chest.rotation = Quaternion.AngleAxis((radio ? 8f : 12f) * weight, leanAxis) * chest.rotation;
            float armLength = Vector3.Distance(upper.position, forearm.position) +
                Vector3.Distance(forearm.position, hand.position) - 0.025f;
            Vector3 reach = desiredWrist - upper.position;
            spine.position += reach.normalized * Mathf.Min(Mathf.Max(0f, reach.magnitude - armLength), 0.18f) * weight;
            ferryman.ReapplySteeringHands();
            Quaternion neutralHandInForearm = Quaternion.Inverse(forearm.rotation) * hand.rotation;
            Vector3 wheelWrist = hand.position;
            Quaternion wheelRotation = hand.rotation;

            // Keep the elbow below and outside its shoulder. The forearm sets
            // the fingers' tangent, not the reverse: aiming an elbow from a
            // preselected hand direction can raise the whole arm unnaturally.
            Transform otherUpper = useLeft ? arms.RightUpperArm : arms.LeftUpperArm;
            Vector3 outboard = (upper.position - otherUpper.position).normalized;
            Vector3 elbowHint = upper.position + outboard * 0.16f + towardTarget * 0.08f - car.transform.up * 0.35f;
            if (phase != ReachPhase.Returning)
            {
                Quaternion upperBase = upper.localRotation;
                Quaternion forearmBase = forearm.localRotation;
                Quaternion handBase = hand.localRotation;
                // Solve the prospective contact first. These intermediate poses
                // are never rendered; the visible reach still blends from wheel.
                for (int pass = 0; pass < 5; pass++)
                {
                    SeatedArmIk.SolveTwoBone(upper, forearm, hand, desiredWrist, desiredRotation, elbowHint);
                    desiredRotation = ContactRotation(hand.position - forearm.position, normal);
                    desiredWrist = contact - desiredRotation * activeContactInHand;
                }
                upper.localRotation = upperBase;
                forearm.localRotation = forearmBase;
                hand.localRotation = handBase;
            }

            float handWeight = phase == ReachPhase.Returning ? 1f - Smooth(elapsed / ReturnSeconds) : weight;
            Quaternion wristRotation = Quaternion.Slerp(wheelRotation, desiredRotation, handWeight);
            Vector3 wrist = Vector3.Lerp(wheelWrist, desiredWrist, handWeight);
            elbowHint = Vector3.Lerp(forearm.position, elbowHint, handWeight);
            SeatedArmIk.SolveTwoBone(upper, forearm, hand, wrist, wristRotation, elbowHint);
            // Share pronation with the forearm instead of putting the entire
            // surface-facing roll into the wrist joint.
            Vector3 armAxis = (hand.position - forearm.position).normalized;
            Vector3 neutralThumb = Vector3.ProjectOnPlane(
                forearm.rotation * neutralHandInForearm * handFrame.ThumbAxisInHand, armAxis);
            Vector3 wantedThumb = Vector3.ProjectOnPlane(wristRotation * handFrame.ThumbAxisInHand, armAxis);
            if (neutralThumb.sqrMagnitude > 0.0001f && wantedThumb.sqrMagnitude > 0.0001f)
            {
                float twist = Vector3.SignedAngle(neutralThumb, wantedThumb, armAxis);
                forearm.rotation = Quaternion.AngleAxis(twist, armAxis) * forearm.rotation;
                hand.rotation = wristRotation;
            }
            WristBendDegrees = Vector3.Angle(armAxis, hand.rotation * handFrame.FingerAxisInHand);
            ElbowHeightFromShoulder = Vector3.Dot(forearm.position - upper.position, car.transform.up);
            Quaternion completedRotation = Quaternion.Inverse(car.Body.rotation) * hand.rotation;
            WristFrameStepDegrees = hasCompletedPose ? Quaternion.Angle(completedRotationLocal, completedRotation) : 0f;
            WristAngularSpeed = step > 0.000001f ? WristFrameStepDegrees / step : 0f;
            completedWristLocal = car.Body.InverseTransformPoint(hand.position);
            completedRotationLocal = completedRotation;
            hasCompletedPose = true;
            HandContactDistance = Vector3.Distance(PalmContactPoint, contact);
            PalmFacingSurface = Vector3.Dot(hand.rotation * handFrame.PalmNormalInHand, -normal);
            OtherHandWheelDistance = Vector3.Distance(otherSocket.position, otherGrip.position);

            if (phase == ReachPhase.Reaching && elapsed >= ReachSeconds)
            {
                if (HandContactDistance <= ContactTolerance)
                {
                    phase = ReachPhase.Operating;
                    elapsed = 0f;
                }
                else if (elapsed >= ReachSeconds + 0.8f) BeginReturn(weight);
            }
            else if (phase == ReachPhase.Operating && elapsed >= (radio ? RadioTurnSeconds : PushSeconds))
            {
                if (HandContactDistance <= ContactTolerance)
                {
                    if (radio && dashboard.RadioOn && dashboard.TuningDetent == requestedStation)
                    {
                        committingRadio = true;
                        dashboard.Operate(LastRouteCarDashboardTarget.RadioTuning);
                        committingRadio = false;
                    }
                    else if (!radio) dashboard.CompleteGloveboxDriverClose();
                }
                BeginReturn(1f);
            }
            else if (phase == ReachPhase.Returning && elapsed >= ReturnSeconds)
            {
                phase = ReachPhase.None;
                actionKind = ActionKind.None;
                LeanWeight = 0f;
                dashboard.SetGloveboxInputLocked(false);
            }
        }

        private void BeginReturn(float weight)
        {
            // Save the last completed pose so an E/Q cancellation does not
            // drag the hand with the newly selected knob's orientation.
            returnWristLocal = completedWristLocal;
            returnRotationLocal = completedRotationLocal;
            phase = ReachPhase.Returning;
            returnFromWeight = weight;
            elapsed = 0f;
            dashboard.SetDriverTuningProgress(0f);
        }

        private static Vector3 Center(MeshFilter mesh) => mesh.transform.TransformPoint(mesh.sharedMesh.bounds.center);

        private Quaternion ContactRotation(Vector3 forearmDirection, Vector3 normal)
        {
            Vector3 fingers = Vector3.ProjectOnPlane(forearmDirection, normal).normalized;
            if (fingers.sqrMagnitude < 0.01f)
                fingers = Vector3.ProjectOnPlane(-dashboard.RadioKnobAxis, normal).normalized;
            Vector3 frameRight = useLeft ? -normal : normal;
            return Quaternion.LookRotation(fingers, Vector3.Cross(fingers, frameRight)) *
                Quaternion.Inverse(handFrame.RotationInHand);
        }

        private Vector3 RadioGripPoint(Vector3 radial)
        {
            MeshFilter mesh = dashboard.RadioTuningKnobMesh;
            Vector3 center = Center(mesh);
            Vector3 axis = dashboard.RadioKnobAxis;
            // Bounds stay available on the production GPU-only mesh. Measure
            // the authored cylinder's axis and radius without enabling CPU
            // copies of every imported car mesh just for this contact.
            Vector3 extent = mesh.sharedMesh.bounds.extents;
            Vector3 x = mesh.transform.TransformVector(Vector3.right * extent.x);
            Vector3 y = mesh.transform.TransformVector(Vector3.up * extent.y);
            Vector3 z = mesh.transform.TransformVector(Vector3.forward * extent.z);
            float axialX = Mathf.Abs(Vector3.Dot(x.normalized, axis));
            float axialY = Mathf.Abs(Vector3.Dot(y.normalized, axis));
            float axialZ = Mathf.Abs(Vector3.Dot(z.normalized, axis));
            float radius = axialX > axialY && axialX > axialZ ? Mathf.Max(y.magnitude, z.magnitude) :
                axialY > axialZ ? Mathf.Max(x.magnitude, z.magnitude) : Mathf.Max(x.magnitude, y.magnitude);
            float depth = Mathf.Abs(Vector3.Dot(x, axis)) + Mathf.Abs(Vector3.Dot(y, axis)) + Mathf.Abs(Vector3.Dot(z, axis));
            return center + radial * radius + axis * (depth * 0.7f);
        }

        private static Vector3 SurfacePoint(MeshFilter mesh, Vector3 normal)
        {
            if (mesh == null) return Vector3.zero;
            Bounds bounds = mesh.sharedMesh.bounds;
            Vector3 extent = bounds.extents;
            float radius = Mathf.Abs(Vector3.Dot(normal, mesh.transform.TransformVector(Vector3.right * extent.x))) +
                Mathf.Abs(Vector3.Dot(normal, mesh.transform.TransformVector(Vector3.up * extent.y))) +
                Mathf.Abs(Vector3.Dot(normal, mesh.transform.TransformVector(Vector3.forward * extent.z)));
            return Center(mesh) + normal * radius;
        }

        private static float Smooth(float t)
        {
            t = Mathf.Clamp01(t);
            return t * t * (3f - 2f * t);
        }

        private void RestorePose()
        {
            attention.Restore();
            if (!bodyWritten) return;
            if (spine != null)
            {
                spine.localRotation = spineBase;
                spine.localPosition = spineBasePosition;
            }
            if (chest != null) chest.localRotation = chestBase;
            bodyWritten = false;
        }

        private void CancelReach()
        {
            phase = ReachPhase.None;
            actionKind = ActionKind.None;
            radioRequested = false;
            gloveboxWait = -1f;
            elapsed = 0f;
            LeanWeight = 0f;
            if (dashboard != null)
            {
                dashboard.SetGloveboxInputLocked(false);
                dashboard.SetDriverTuningProgress(0f);
            }
        }

        private void OnDisable() { RestorePose(); CancelReach(); attention.Clear(); }
        private void OnDestroy()
        {
            if (dashboard != null) dashboard.Operated -= OnDashboardOperated;
            RestorePose();
            CancelReach();
            attention.Unbind();
        }
    }
}

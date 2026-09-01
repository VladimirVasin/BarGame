using System;
using UnityEngine;

namespace BarPromenade
{
    /// <summary>
    /// Bridges the pure service frame to the two environment-owned cup
    /// bindings and optional pour stream. No scheduling lives here.
    /// </summary>
    [DefaultExecutionOrder(100)]
    [DisallowMultipleComponent]
    public sealed class MountainRoadCafeServicePresentation : MonoBehaviour
    {
        [SerializeField] private MountainRoadCafeCupView pairManCup;
        [SerializeField] private MountainRoadCafeCupView pairWomanCup;
        [SerializeField] private Transform pairManDrinkSocket;
        [SerializeField] private Transform pairWomanDrinkSocket;
        [SerializeField] private Transform attendantMotionRoot;
        [SerializeField] private Transform attendantDock;
        [SerializeField] private Transform pairManServiceMark;
        [SerializeField] private Transform pairWomanServiceMark;
        [SerializeField] private Transform potSpout;
        [SerializeField] private Transform pourStream;
        [SerializeField] private Renderer pourStreamRenderer;
        [SerializeField] private float authoredStreamLength = 1f;

        private Vector3 authoredStreamScale;
        private Vector3 authoredStreamLocalDirection = Vector3.forward;
        private Quaternion attendantAuthoredRotation;
        private MountainRoadCafeServiceFrame lastFrame;
        private bool hasFrame;

        public bool IsConfigured { get; private set; }
        public bool IncludesHeroCup => false;

        public static MountainRoadCafeServicePresentation CreateAndBind(
            MountainRoadCafeAssetRegistry environment,
            MountainRoadCafeCastController cast)
        {
            if (environment == null)
            {
                throw new ArgumentNullException(nameof(environment));
            }

            if (cast == null || !cast.IsInitialized)
            {
                throw new ArgumentException(
                    "Cafe service requires an initialized cast controller.",
                    nameof(cast));
            }

            MountainRoadCafeCupView pairMan = CreateCupView(
                environment,
                "Cup.PairMan",
                MountainRoadCafeCastRole.PairMan);
            MountainRoadCafeCupView pairWoman = CreateCupView(
                environment,
                "Cup.PairWoman",
                MountainRoadCafeCastRole.PairWoman);

            MountainRoadCafeDynamicPropBinding stream = RequireProp(
                environment,
                "PourStream");
            if (stream.PropRoot == null || stream.Renderers.Count != 1 ||
                stream.Renderers[0] == null)
            {
                throw new InvalidOperationException(
                    "Cafe PourStream requires one authored renderer root.");
            }

            Renderer streamRenderer = stream.Renderers[0];
            // The stream pivot is authored at the first cap. Its renderer
            // centre is therefore exactly half a stream length away, and is
            // also the reliable post-FBX direction of the long axis.
            Vector3 streamCenterOffset =
                streamRenderer.bounds.center - stream.PropRoot.position;
            float streamLength = streamCenterOffset.magnitude * 2f;
            if (streamLength <= 0.0001f)
            {
                throw new InvalidOperationException(
                    "Cafe PourStream has no measurable authored length.");
            }

            var presentation = environment.gameObject.AddComponent<
                MountainRoadCafeServicePresentation>();
            presentation.Configure(
                pairMan,
                pairWoman,
                cast.AttendantPourSpout,
                stream.PropRoot,
                streamRenderer,
                streamLength);
            if (!presentation.BindAttendantMotion(
                    cast.AttendantMotionRoot,
                    RequireAnchor(environment, "ServiceRail.00"),
                    RequireAnchor(environment, "ServiceRail.01"),
                    RequireAnchor(environment, "ServiceRail.02")) ||
                !cast.BindServicePresentation(presentation))
            {
                throw new InvalidOperationException(
                    "Cafe service could not bind authored cast motion or " +
                    "drink sockets.");
            }

            return presentation;
        }

        public void Configure(
            MountainRoadCafeCupView configuredPairManCup,
            MountainRoadCafeCupView configuredPairWomanCup,
            Transform configuredPotSpout = null,
            Transform configuredPourStream = null,
            Renderer configuredPourStreamRenderer = null,
            float configuredAuthoredStreamLength = 1f)
        {
            ValidateCup(
                configuredPairManCup,
                MountainRoadCafeCastRole.PairMan);
            ValidateCup(
                configuredPairWomanCup,
                MountainRoadCafeCastRole.PairWoman);
            if ((configuredPourStream == null) !=
                (configuredPourStreamRenderer == null))
            {
                throw new ArgumentException(
                    "Pour stream transform and renderer are one binding.");
            }

            if (configuredPourStream != null &&
                (configuredPotSpout == null ||
                 configuredAuthoredStreamLength <= 0f))
            {
                throw new ArgumentException(
                    "A visible pour stream requires a spout and positive " +
                    "authored length.");
            }

            pairManCup = configuredPairManCup;
            pairWomanCup = configuredPairWomanCup;
            potSpout = configuredPotSpout;
            pourStream = configuredPourStream;
            pourStreamRenderer = configuredPourStreamRenderer;
            authoredStreamLength = configuredAuthoredStreamLength;
            authoredStreamScale = pourStream != null
                ? pourStream.localScale
                : Vector3.one;
            if (pourStream != null)
            {
                Vector3 centerOffset =
                    pourStreamRenderer.bounds.center - pourStream.position;
                if (centerOffset.sqrMagnitude <= 0.00000001f)
                {
                    throw new ArgumentException(
                        "A visible pour stream requires its authored pivot " +
                        "at the first cap.");
                }

                authoredStreamLocalDirection =
                    pourStream.InverseTransformDirection(
                        centerOffset.normalized).normalized;
            }
            IsConfigured = true;
            SetPourStreamVisible(false, null);
        }

        public bool TryGetCup(
            MountainRoadCafeCastRole role,
            out MountainRoadCafeCupView cup)
        {
            switch (role)
            {
                case MountainRoadCafeCastRole.PairMan:
                    cup = pairManCup;
                    return cup != null;
                case MountainRoadCafeCastRole.PairWoman:
                    cup = pairWomanCup;
                    return cup != null;
                default:
                    cup = null;
                    return false;
            }
        }

        public bool BindDrinkSockets(
            Transform configuredPairManDrinkSocket,
            Transform configuredPairManMouthSocket,
            Transform configuredPairManRoot,
            Transform configuredPairWomanDrinkSocket,
            Transform configuredPairWomanMouthSocket,
            Transform configuredPairWomanRoot)
        {
            if (!IsConfigured ||
                configuredPairManDrinkSocket == null ||
                configuredPairManMouthSocket == null ||
                configuredPairManRoot == null ||
                configuredPairWomanDrinkSocket == null ||
                configuredPairWomanMouthSocket == null ||
                configuredPairWomanRoot == null)
            {
                return false;
            }

            if (!pairManCup.BindDrinkRig(
                    configuredPairManDrinkSocket,
                    configuredPairManMouthSocket,
                    configuredPairManRoot) ||
                !pairWomanCup.BindDrinkRig(
                    configuredPairWomanDrinkSocket,
                    configuredPairWomanMouthSocket,
                    configuredPairWomanRoot))
            {
                return false;
            }

            pairManDrinkSocket = configuredPairManDrinkSocket;
            pairWomanDrinkSocket = configuredPairWomanDrinkSocket;
            return true;
        }

        public bool BindAttendantMotion(
            Transform configuredAttendantMotionRoot,
            Transform configuredAttendantDock,
            Transform configuredPairManServiceMark,
            Transform configuredPairWomanServiceMark)
        {
            if (!IsConfigured ||
                configuredAttendantMotionRoot == null ||
                configuredAttendantDock == null ||
                configuredPairManServiceMark == null ||
                configuredPairWomanServiceMark == null)
            {
                return false;
            }

            attendantMotionRoot = configuredAttendantMotionRoot;
            attendantDock = configuredAttendantDock;
            pairManServiceMark = configuredPairManServiceMark;
            pairWomanServiceMark = configuredPairWomanServiceMark;
            attendantAuthoredRotation = attendantMotionRoot.rotation;
            SnapAttendantTo(attendantDock);
            return true;
        }

        public void SetFrame(MountainRoadCafeServiceFrame frame)
        {
            if (!IsConfigured)
            {
                return;
            }

            pairManCup.SetFill01(frame.PairManFill);
            pairWomanCup.SetFill01(frame.PairWomanFill);

            bool pairManDrinks = frame.IsDrinking(
                MountainRoadCafeCastRole.PairMan);
            bool pairWomanDrinks = frame.IsDrinking(
                MountainRoadCafeCastRole.PairWoman);
            pairManCup.SetDrinkPose(
                pairManDrinks,
                pairManDrinks
                    ? frame.GetDrinkNormalized(
                        MountainRoadCafeCastRole.PairMan)
                    : 0f,
                pairManDrinkSocket);
            pairWomanCup.SetDrinkPose(
                pairWomanDrinks,
                pairWomanDrinks
                    ? frame.GetDrinkNormalized(
                        MountainRoadCafeCastRole.PairWoman)
                    : 0f,
                pairWomanDrinkSocket);
            SetAttendantFrame(frame);

            lastFrame = frame;
            hasFrame = true;
            UpdatePourStream(frame);
        }

        private void UpdatePourStream(
            MountainRoadCafeServiceFrame frame)
        {
            MountainRoadCafeCupView target = null;
            bool pouring = frame.Phase ==
                           MountainRoadCafeServicePhase.Pour &&
                           MountainRoadCafeServiceTimeline.IsPourFlowActive(
                               frame.PhaseNormalized) &&
                           frame.HasServiceTarget &&
                           TryGetCup(frame.ServiceTarget, out target);
            SetPourStreamVisible(pouring, target);
        }

        private void LateUpdate()
        {
            if (IsConfigured && hasFrame)
            {
                // Cast PlayableGraphs have sampled the animated hand by now;
                // anchor the stream after them so it starts at this frame's
                // measured pot-spout position rather than one frame behind.
                UpdatePourStream(lastFrame);
            }
        }

        public void ResetExact()
        {
            if (!IsConfigured)
            {
                return;
            }

            pairManCup.ResetExact();
            pairWomanCup.ResetExact();
            SnapAttendantTo(attendantDock);
            SetPourStreamVisible(false, null);
            hasFrame = false;
        }

        private void SetAttendantFrame(
            MountainRoadCafeServiceFrame frame)
        {
            if (attendantMotionRoot == null || attendantDock == null)
            {
                return;
            }

            if (frame.Phase == MountainRoadCafeServicePhase.WalkToCup &&
                frame.HasServiceTarget)
            {
                Transform origin = frame.HasWalkOrigin
                    ? ResolveServiceMark(frame.WalkOrigin)
                    : attendantDock;
                Transform target = ResolveServiceMark(frame.ServiceTarget);
                SetAttendantBetween(
                    origin,
                    target,
                    Mathf.SmoothStep(0f, 1f, frame.PhaseNormalized));
                return;
            }

            if (frame.Phase == MountainRoadCafeServicePhase.Pour &&
                frame.HasServiceTarget)
            {
                SnapAttendantTo(ResolveServiceMark(frame.ServiceTarget));
                return;
            }

            if (frame.Phase == MountainRoadCafeServicePhase.WalkBack &&
                frame.HasServiceTarget)
            {
                SetAttendantBetween(
                    ResolveServiceMark(frame.ServiceTarget),
                    attendantDock,
                    Mathf.SmoothStep(0f, 1f, frame.PhaseNormalized));
                return;
            }

            SnapAttendantTo(attendantDock);
        }

        private Transform ResolveServiceMark(
            MountainRoadCafeCastRole role)
        {
            switch (role)
            {
                case MountainRoadCafeCastRole.PairMan:
                    return pairManServiceMark;
                case MountainRoadCafeCastRole.PairWoman:
                    return pairWomanServiceMark;
                default:
                    return attendantDock;
            }
        }

        private void SetAttendantBetween(
            Transform origin,
            Transform target,
            float amount)
        {
            if (origin == null || target == null ||
                attendantMotionRoot == null)
            {
                return;
            }

            attendantMotionRoot.SetPositionAndRotation(
                Vector3.Lerp(origin.position, target.position, amount),
                attendantAuthoredRotation);
        }

        private void SnapAttendantTo(Transform mark)
        {
            if (attendantMotionRoot != null && mark != null)
            {
                attendantMotionRoot.SetPositionAndRotation(
                    mark.position,
                    attendantAuthoredRotation);
            }
        }

        private void OnDisable()
        {
            ResetExact();
        }

        private void SetPourStreamVisible(
            bool visible,
            MountainRoadCafeCupView targetCup)
        {
            if (pourStreamRenderer == null || pourStream == null)
            {
                return;
            }

            pourStreamRenderer.enabled = visible;
            if (!visible || potSpout == null || targetCup == null ||
                targetCup.PourTarget == null)
            {
                return;
            }

            Vector3 start = potSpout.position;
            Vector3 delta = targetCup.PourTarget.position - start;
            float length = delta.magnitude;
            if (length <= 0.0001f)
            {
                pourStreamRenderer.enabled = false;
                return;
            }

            pourStream.position = start;
            Vector3 currentStreamDirection =
                pourStream.TransformDirection(
                    authoredStreamLocalDirection).normalized;
            pourStream.rotation = Quaternion.FromToRotation(
                currentStreamDirection,
                delta / length) * pourStream.rotation;
            // The flow is not baked into the attendant clip. This separate
            // mesh is re-anchored and resized from the animated spout to the
            // current cup target on every SetFrame/LateUpdate.
            pourStream.localScale = authoredStreamScale *
                                    (length / authoredStreamLength);
        }

        private static void ValidateCup(
            MountainRoadCafeCupView cup,
            MountainRoadCafeCastRole expectedRole)
        {
            if (cup == null)
            {
                throw new ArgumentNullException(nameof(cup));
            }

            if (!cup.IsConfigured || cup.Role != expectedRole)
            {
                throw new InvalidOperationException(
                    "Cafe service cup bindings must be configured once in " +
                    "PairMan, PairWoman order.");
            }
        }

        private static MountainRoadCafeCupView CreateCupView(
            MountainRoadCafeAssetRegistry environment,
            string propName,
            MountainRoadCafeCastRole role)
        {
            MountainRoadCafeDynamicPropBinding prop = RequireProp(
                environment,
                propName);
            if (prop.PropRoot == null ||
                prop.LiftRoot == null ||
                prop.GripAnchor == null ||
                prop.PourTarget == null ||
                prop.LiquidTransform == null ||
                prop.LiquidRenderer == null)
            {
                throw new InvalidOperationException(
                    $"Cafe prop '{propName}' has incomplete cup bindings.");
            }

            MountainRoadCafeCupView existing =
                prop.PropRoot.GetComponent<MountainRoadCafeCupView>();
            if (existing != null)
            {
                throw new InvalidOperationException(
                    $"Cafe prop '{propName}' already owns a cup view.");
            }

            var result = prop.PropRoot.gameObject.AddComponent<
                MountainRoadCafeCupView>();
            result.Configure(
                role,
                prop.LiquidTransform,
                prop.LiquidRenderer,
                prop.EmptyLocalPosition,
                prop.FullLocalPosition,
                prop.PourTarget,
                prop.LiftRoot,
                prop.GripAnchor);
            return result;
        }

        private static MountainRoadCafeDynamicPropBinding RequireProp(
            MountainRoadCafeAssetRegistry environment,
            string name)
        {
            if (environment.TryGetProp(
                    name,
                    out MountainRoadCafeDynamicPropBinding prop) &&
                prop != null)
            {
                return prop;
            }

            throw new InvalidOperationException(
                $"Authored cafe prop '{name}' is missing.");
        }

        private static Transform RequireAnchor(
            MountainRoadCafeAssetRegistry environment,
            string name)
        {
            if (environment.TryGetAnchor(name, out Transform anchor) &&
                anchor != null)
            {
                return anchor;
            }

            throw new InvalidOperationException(
                $"Authored cafe anchor '{name}' is missing.");
        }
    }
}

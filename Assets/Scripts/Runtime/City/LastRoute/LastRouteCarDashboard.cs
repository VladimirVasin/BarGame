using System;
using UnityEngine;

namespace BarPromenade
{
    /// <summary>
    /// The dash, driven: the glovebox lid on its hinge, the radio's two
    /// knobs and the needle between them, the speedometer's needle, and
    /// the dial that lights when the radio is on.
    ///
    /// Every moving part was authored on its own pivot by
    /// `tools/build-last-route-car-3d-model.py` so that "he turns the knob"
    /// is a rotation here rather than a re-author - the doors' rule. And
    /// like the doors, nothing here trusts an imported node's axes: the lid
    /// hinges about the RUNTIME ROOT's right, the knobs turn about an axis
    /// pointed at the sitter from the runtime root's forward, the needle
    /// slides along a direction measured between the two DRAWN seat
    /// anchors, and which way the lid opens is derived from where the lid
    /// is drawn relative to its hinge.
    ///
    /// The radio is SILENT for now. Its state - on, and which click the
    /// tuning knob stands at - is kept and shown (the knob, the needle, the
    /// lit dial) so that whatever voice it is later given has something to
    /// read; <see cref="Operated"/> is where that voice, and today the
    /// car's own click cues, hang off.
    ///
    /// State is written through to <see cref="GameSessionState"/> on every
    /// change and read back at <see cref="Initialize"/>, because the ride
    /// crosses an area boundary and the mountain raises a new car.
    /// </summary>
    [DisallowMultipleComponent]
    [DefaultExecutionOrder(275)]
    public sealed class LastRouteCarDashboard : MonoBehaviour
    {
        public const string RadioOnPromptKey = "interaction.radio_on";
        public const string RadioOffPromptKey = "interaction.radio_off";
        public const string RadioTunePromptKey = "interaction.radio_tune";
        public const string OpenGloveboxPromptKey = "interaction.open_glovebox";
        public const string CloseGloveboxPromptKey = "interaction.close_glovebox";

        /// <summary>The renderer whose drawn box the gaze pick uses for the
        /// radio: the chrome frame, which is the whole face of it.</summary>
        public const string RadioBezelRole = "radio_bezel";

        /// <summary>How quickly the speedometer needle chases the road, in
        /// full-scale sweeps per second. A tired instrument, not a digital
        /// one.</summary>
        public const float SpeedoResponsePerSecond = 1.5f;

        /// <summary>What the dial glows when there is nothing to read off
        /// the material - a build without an emission colour is a build
        /// somebody will notice, so this is only a floor.</summary>
        private static readonly Color FallbackDialLitColor =
            new Color(1.46f, 1.02f, 0.48f, 1f);

        private static readonly int EmissionColorId =
            Shader.PropertyToID("_EmissionColor");

        private sealed class RestPose
        {
            public Transform Target;
            public Vector3 LocalPosition;
            public Quaternion LocalRotation;

            public void Restore()
            {
                Target.localPosition = LocalPosition;
                Target.localRotation = LocalRotation;
            }
        }

        private LastRouteCarAssetRegistry registry;
        private LastRouteCarDriver driver;
        private RestPose lid;
        private RestPose powerKnob;
        private RestPose tuningKnob;
        private RestPose needle;
        private RestPose speedo;
        private float lidOpenSign = 1f;
        private Vector3 needleTravelLocal = Vector3.right;
        private Vector3 towardsDriver = Vector3.right;
        private Renderer radioBezel;
        private Renderer[] lidRenderers = Array.Empty<Renderer>();
        private Renderer dial;
        private MaterialPropertyBlock dialProperties;
        private Color dialLitColor = FallbackDialLitColor;
        private LastRouteCarDashboardState state =
            LastRouteCarDashboardState.Default;
        private float gloveboxOpenness;
        private float gloveboxProgress = 1f;
        private bool gloveboxSwinging;
        private float speed01;
        private int gazeFrame = -1;
        private Ray gazeRay;
        private LastRouteCarDashboardTarget gazeTarget;

        public bool IsInitialized { get; private set; }
        public LastRouteCarDashboardState State => state;
        public bool RadioOn => state.RadioOn;
        public int TuningDetent => state.TuningDetent;
        public float Tuning01 =>
            LastRouteCarRadioModel.Tuning01FromDetent(state.TuningDetent);
        public bool GloveboxOpen => state.GloveboxOpen;
        public float GloveboxOpenness => gloveboxOpenness;
        public bool IsGloveboxSwinging => gloveboxSwinging;
        public float Speed01 => speed01;

        /// <summary>Raised the frame he does something to the dash, with
        /// what he did it to. The car's voice clicks on it.</summary>
        public event Action<LastRouteCarDashboardTarget> Operated;

        public void Initialize(LastRouteCarAssetRegistry carRegistry)
        {
            if (carRegistry == null)
            {
                throw new ArgumentNullException(nameof(carRegistry));
            }

            if (!carRegistry.IsBound)
            {
                throw new ArgumentException(
                    "The car's dashboard cannot be driven before its " +
                    "registry is bound.",
                    nameof(carRegistry));
            }

            registry = carRegistry;
            // NOT resolved here. The factory raises this dash BEFORE the
            // engine, deliberately, and a `GetComponent` at this moment
            // therefore always answered null - so `wanted` below was always
            // zero and the speedometer needle never left its stop, on a car
            // doing five and a half metres a second. It reads as a dead
            // instrument rather than as an error, and every other thing on
            // the dash went on working. Resolved on first use instead.

            // Rest poses are captured AFTER the suspension has re-parented
            // the body under its sprung empty - the factory installs the
            // springs first for exactly this reason - so a local pose here
            // is a pose under the spring, and restoring it each frame keeps
            // the part riding the body.
            lid = Capture(registry.GloveboxLidPivot);
            powerKnob = Capture(registry.RadioPowerKnobPivot);
            tuningKnob = Capture(registry.RadioTuningKnobPivot);
            needle = Capture(registry.RadioNeedlePivot);
            speedo = Capture(registry.SpeedoNeedlePivot);
            lidRenderers = registry.GloveboxLidPivot
                .GetComponentsInChildren<Renderer>(true);
            dial = registry.RadioDialRenderer;
            radioBezel = FindRenderer(RadioBezelRole);

            ResolveAxes();
            ResolveDialColor();

            // What the last car looked like, or a fresh dash.
            state = GameSessionState.CarDashboard;
            ApplyRadio();
            gloveboxOpenness = state.GloveboxOpen ? 1f : 0f;
            gloveboxProgress = 1f;
            gloveboxSwinging = false;
            ApplyLid(gloveboxOpenness);
            speed01 = 0f;
            ApplySpeedometer(0f);
            IsInitialized = true;
        }

        /// <summary>
        /// He does the thing he is looking at: switches the radio, clicks
        /// the tuning knob one notch, or releases or shuts the lid. The
        /// session is told at once; the lid takes a third of a second to
        /// get there.
        /// </summary>
        public void Operate(LastRouteCarDashboardTarget target)
        {
            if (!IsInitialized)
            {
                return;
            }

            switch (target)
            {
                case LastRouteCarDashboardTarget.RadioPower:
                    state = state.WithRadioOn(!state.RadioOn);
                    ApplyRadio();
                    break;
                case LastRouteCarDashboardTarget.RadioTuning:
                    state = state.WithTuningDetent(
                        LastRouteCarRadioModel.StepDetent(state.TuningDetent));
                    ApplyRadio();
                    break;
                case LastRouteCarDashboardTarget.Glovebox:
                    state = state.WithGloveboxOpen(!state.GloveboxOpen);
                    gloveboxProgress =
                        LastRouteCarGloveboxTimeline.ProgressForOpenness(
                            gloveboxOpenness,
                            state.GloveboxOpen);
                    gloveboxSwinging = true;
                    break;
                default:
                    return;
            }

            GameSessionState.SetCarDashboard(state);
            GameLog.Info(
                "lastroute",
                "car_dashboard_operated",
                GameLog.Field("target", target.ToString()),
                GameLog.Field("radio_on", state.RadioOn),
                GameLog.Field("tuning_detent", state.TuningDetent),
                GameLog.Field("glovebox_open", state.GloveboxOpen));
            Operated?.Invoke(target);
        }

        /// <summary>
        /// The prompt for what he is looking at, given what it already is.
        /// Null for nothing, so the seat falls back to its own offer.
        /// </summary>
        public static string ResolvePromptKey(
            LastRouteCarDashboardTarget target,
            bool radioOn,
            bool gloveboxOpen)
        {
            switch (target)
            {
                case LastRouteCarDashboardTarget.RadioPower:
                    return radioOn ? RadioOffPromptKey : RadioOnPromptKey;
                case LastRouteCarDashboardTarget.RadioTuning:
                    return RadioTunePromptKey;
                case LastRouteCarDashboardTarget.Glovebox:
                    return gloveboxOpen
                        ? CloseGloveboxPromptKey
                        : OpenGloveboxPromptKey;
                default:
                    return null;
            }
        }

        /// <summary>
        /// What a ray from the passenger's eye lands on. Cached for the
        /// frame it was asked in, because the seat asks twice a frame - once
        /// for the prompt and once to answer the key - and both have to
        /// agree; a different ray in the same frame is answered afresh.
        /// </summary>
        public bool TryResolveGazeTarget(
            Ray ray,
            out LastRouteCarDashboardTarget target)
        {
            target = LastRouteCarDashboardTarget.None;
            if (!IsInitialized || radioBezel == null)
            {
                return false;
            }

            if (gazeFrame == Time.frameCount &&
                gazeRay.origin == ray.origin &&
                gazeRay.direction == ray.direction)
            {
                target = gazeTarget;
                return target != LastRouteCarDashboardTarget.None;
            }

            Bounds radio = radioBezel.bounds;
            gazeTarget = LastRouteCarDashboardGaze.Resolve(
                ray,
                radio,
                radio.center,
                towardsDriver,
                MeasureLidBounds());
            gazeFrame = Time.frameCount;
            gazeRay = ray;
            target = gazeTarget;
            return target != LastRouteCarDashboardTarget.None;
        }

        /// <summary>The direction from the passenger's seat to the driver's,
        /// on the car's own right axis - the dial runs along it.</summary>
        public Vector3 TowardsDriver => towardsDriver;

        /// <summary>The colour the dial is currently showing, for tests:
        /// black with the radio off.</summary>
        public Color ReadDialEmission()
        {
            if (dial == null)
            {
                return Color.black;
            }

            var block = new MaterialPropertyBlock();
            dial.GetPropertyBlock(block);
            return block.GetColor(EmissionColorId);
        }

        public void SetGloveboxOpenness(float openness)
        {
            gloveboxSwinging = false;
            gloveboxProgress = 1f;
            gloveboxOpenness = Sanitize(openness);
            ApplyLid(gloveboxOpenness);
        }

        public void SetSpeedometer01(float value)
        {
            speed01 = Sanitize(value);
            ApplySpeedometer(speed01);
        }

        private void Update()
        {
            if (!IsInitialized)
            {
                return;
            }

            float step = Time.deltaTime;
            if (gloveboxSwinging)
            {
                gloveboxProgress = Mathf.MoveTowards(
                    gloveboxProgress,
                    1f,
                    step / LastRouteCarGloveboxTimeline.SwingSeconds);
                gloveboxOpenness =
                    LastRouteCarGloveboxTimeline.EvaluateOpenness(
                        gloveboxProgress,
                        state.GloveboxOpen);
                if (gloveboxProgress >= 1f)
                {
                    gloveboxSwinging = false;
                }
            }

            // Every frame regardless, because the body under these pivots is
            // sprung and the rest pose is the only thing keeping a part on
            // it - the doors are written the same way.
            ApplyLid(gloveboxOpenness);

            LastRouteCarDriver engine = ResolveDriver();
            float wanted = engine != null && engine.IsDriving
                ? LastRouteCarRadioModel.Speedometer01(engine.Speed)
                : 0f;
            speed01 = Mathf.MoveTowards(
                speed01,
                wanted,
                step * SpeedoResponsePerSecond);
            ApplySpeedometer(speed01);
            ApplyRadioPose();
        }

        /// <summary>
        /// The engine, found the first frame anything asks for it.
        ///
        /// It cannot be found at <see cref="Initialize"/>: the factory raises
        /// the dash before the driver on purpose, so at that moment the
        /// component does not exist yet. Looking again each frame until one
        /// turns up costs a `GetComponent` on a car with no engine, which is
        /// a car that is never going to drive anyway.
        /// </summary>
        private LastRouteCarDriver ResolveDriver()
        {
            if (driver == null)
            {
                driver = GetComponent<LastRouteCarDriver>();
            }

            return driver;
        }

        private void ApplyRadio()
        {
            ApplyRadioPose();
            ApplyDialLight(state.RadioOn);
        }

        /// <summary>
        /// The knobs and the needle. A knob turns about an axis pointed AT
        /// the sitter - the runtime root's forward, reversed - so a positive
        /// angle is clockwise as he sees it; the needle slides along the
        /// measured seat-to-seat direction re-expressed in its own parent's
        /// space, the way the wheels take their roll axis.
        /// </summary>
        private void ApplyRadioPose()
        {
            Vector3 axis = -ResolveForward();
            Rotate(
                powerKnob,
                state.RadioOn ? LastRouteCarRadioModel.PowerKnobOnDegrees : 0f,
                axis);
            Rotate(
                tuningKnob,
                LastRouteCarRadioModel.TuningKnobDegrees(state.TuningDetent),
                axis);
            if (needle != null)
            {
                needle.Restore();
                needle.Target.localPosition =
                    needle.LocalPosition +
                    (needleTravelLocal *
                     LastRouteCarRadioModel.Tuning01FromDetent(
                         state.TuningDetent));
            }
        }

        private void ApplyLid(float openness)
        {
            if (lid == null)
            {
                return;
            }

            lid.Restore();
            if (openness <= 0f)
            {
                return;
            }

            lid.Target.rotation =
                Quaternion.AngleAxis(
                    LastRouteCarGloveboxTimeline.MaximumOpenDegrees *
                    openness *
                    lidOpenSign,
                    ResolveRight()) *
                lid.Target.rotation;
        }

        private void ApplySpeedometer(float value)
        {
            Rotate(
                speedo,
                LastRouteCarRadioModel.SpeedoSweepDegrees * value,
                -ResolveForward());
        }

        private static void Rotate(RestPose pose, float degrees, Vector3 axisWorld)
        {
            if (pose == null)
            {
                return;
            }

            pose.Restore();
            if (Mathf.Abs(degrees) < 0.0001f)
            {
                return;
            }

            pose.Target.rotation =
                Quaternion.AngleAxis(degrees, axisWorld) * pose.Target.rotation;
        }

        private void ApplyDialLight(bool lit)
        {
            if (dial == null)
            {
                return;
            }

            if (dialProperties == null)
            {
                dialProperties = new MaterialPropertyBlock();
            }

            dial.GetPropertyBlock(dialProperties);
            dialProperties.SetColor(
                EmissionColorId,
                lit ? dialLitColor : Color.black);
            dial.SetPropertyBlock(dialProperties);
        }

        /// <summary>
        /// Which way is out for the lid, and along for the needle, both from
        /// drawn points on the runtime root's axes. The lid's drawn centre
        /// stands above its hinge; rotating it about the car's right must
        /// carry it TOWARDS the sitter, and the sign that does so is read
        /// off the geometry rather than assumed - the doors' rule, and the
        /// seventh time this project was bitten by assuming it.
        /// </summary>
        private void ResolveAxes()
        {
            Vector3 right = ResolveRight();
            Vector3 forward = ResolveForward();

            towardsDriver = right;
            if (registry.DriverSeatAnchor != null &&
                registry.PassengerSeatAnchor != null &&
                Vector3.Dot(
                    registry.DriverSeatAnchor.position -
                    registry.PassengerSeatAnchor.position,
                    right) < 0f)
            {
                towardsDriver = -right;
            }

            if (needle != null)
            {
                // The whole travel as a VECTOR in the pivot's parent space,
                // not a direction times metres: the imported root carries
                // the FBX unit factor of 100, so a metre of world travel is
                // a centimetre of local position under it. The bus's door
                // button stores its travel the same way.
                Vector3 travelWorld = towardsDriver * registry.RadioNeedleTravel;
                needleTravelLocal = needle.Target.parent != null
                    ? needle.Target.parent.InverseTransformVector(travelWorld)
                    : travelWorld;
            }

            lidOpenSign = 1f;
            if (lid != null && lidRenderers.Length > 0)
            {
                Vector3 alongLeaf = MeasureLidBounds().center - lid.Target.position;
                if (alongLeaf.sqrMagnitude > 0.000001f)
                {
                    float swing = Vector3.Dot(
                        Vector3.Cross(right, alongLeaf.normalized),
                        -forward);
                    lidOpenSign = swing >= 0f ? 1f : -1f;
                }
            }
        }

        private void ResolveDialColor()
        {
            dialLitColor = FallbackDialLitColor;
            if (dial == null)
            {
                return;
            }

            Material material = dial.sharedMaterial;
            if (material != null && material.HasProperty(EmissionColorId))
            {
                Color authored = material.GetColor(EmissionColorId);
                if (authored.maxColorComponent > 0.01f)
                {
                    dialLitColor = authored;
                }
            }
        }

        private Bounds MeasureLidBounds()
        {
            if (lidRenderers.Length == 0)
            {
                return new Bounds(
                    lid != null ? lid.Target.position : transform.position,
                    Vector3.zero);
            }

            Bounds bounds = lidRenderers[0].bounds;
            for (int index = 1; index < lidRenderers.Length; index++)
            {
                bounds.Encapsulate(lidRenderers[index].bounds);
            }

            return bounds;
        }

        private Renderer FindRenderer(string role)
        {
            for (int index = 0; index < registry.Bindings.Count; index++)
            {
                LastRouteCarRendererBinding binding = registry.Bindings[index];
                if (binding.Role == role && binding.Renderer != null)
                {
                    return binding.Renderer;
                }
            }

            return null;
        }

        private static RestPose Capture(Transform target)
        {
            if (target == null)
            {
                return null;
            }

            return new RestPose
            {
                Target = target,
                LocalPosition = target.localPosition,
                LocalRotation = target.localRotation
            };
        }

        private Vector3 ResolveRight()
        {
            Vector3 right = transform.right;
            return right.sqrMagnitude > 0.000001f ? right.normalized : Vector3.right;
        }

        private Vector3 ResolveForward()
        {
            Vector3 forward = transform.forward;
            return forward.sqrMagnitude > 0.000001f
                ? forward.normalized
                : Vector3.forward;
        }

        private static float Sanitize(float value)
        {
            return float.IsNaN(value) || float.IsInfinity(value)
                ? 0f
                : Mathf.Clamp01(value);
        }
    }
}

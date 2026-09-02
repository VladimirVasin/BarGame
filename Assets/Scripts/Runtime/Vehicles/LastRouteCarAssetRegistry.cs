using System;
using System.Collections.Generic;
using UnityEngine;

namespace BarPromenade
{
    public enum LastRouteCarMaterialSlot
    {
        Body,
        AccentPaint,
        Rust,
        Trim,
        Chrome,
        Rubber,
        Metal,
        Glass,
        CrackedGlass,
        BrokenGlass,
        Interior,
        Seat,
        Dashboard,
        Headlight,
        TailLight,
        Plate,
        // Appended LAST on purpose: the bindings serialize this enum as an
        // int, and a member slipped in above would repaint the prefab.
        RadioDial,

        /// <summary>
        /// Every lit surface inside the cabin: the plafond lens over the
        /// seats, the bulb in the glovebox and the two instrument faces.
        ///
        /// One slot for all three, because the authored value only fixes
        /// the colour and keeps Unity's emission keyword alive - the
        /// runtime writes each renderer's actual level through its own
        /// property block, exactly as the radio dial is already driven.
        ///
        /// The faces used to sit on <see cref="Plate"/>, and that was the
        /// trap: the number plate hanging off the nose of the car is on
        /// the same slot, so lighting the dials there would have set the
        /// plate glowing in the dark.
        /// </summary>
        CabinLamp
    }

    [Serializable]
    public sealed class LastRouteCarRendererBinding
    {
        [SerializeField] private string sourceName;
        [SerializeField] private string role;
        [SerializeField] private LastRouteCarMaterialSlot materialSlot;
        [SerializeField] private Renderer renderer;

        public LastRouteCarRendererBinding(
            string configuredSourceName,
            string configuredRole,
            LastRouteCarMaterialSlot configuredMaterialSlot,
            Renderer configuredRenderer)
        {
            sourceName = configuredSourceName ?? string.Empty;
            role = configuredRole ?? string.Empty;
            materialSlot = configuredMaterialSlot;
            renderer = configuredRenderer;
        }

        public string SourceName => sourceName;
        public string Role => role;
        public LastRouteCarMaterialSlot MaterialSlot => materialSlot;
        public Renderer Renderer => renderer;
    }

    [Serializable]
    public struct LastRouteCarDimensions
    {
        [SerializeField] private float length;
        [SerializeField] private float width;
        [SerializeField] private float height;
        [SerializeField] private float wheelbase;
        [SerializeField] private float wheelRadius;

        public LastRouteCarDimensions(
            float configuredLength,
            float configuredWidth,
            float configuredHeight,
            float configuredWheelbase,
            float configuredWheelRadius)
        {
            length = configuredLength;
            width = configuredWidth;
            height = configuredHeight;
            wheelbase = configuredWheelbase;
            wheelRadius = configuredWheelRadius;
        }

        public float Length => length;
        public float Width => width;
        public float Height => height;
        public float Wheelbase => wheelbase;
        public float WheelRadius => wheelRadius;
    }

    /// <summary>
    /// The parked Last Route car, as the runtime sees it.
    ///
    /// Every transform the game will ever need is a serialized field bound
    /// once by the editor asset build, the way the bus does it - nothing is
    /// found by name at runtime, so a renamed mesh fails the asset build in
    /// the editor rather than throwing in the City scene.
    ///
    /// What the seat and door anchors guarantee, for the ride feature that
    /// does not exist yet:
    ///  - <see cref="DriverSeatAnchor"/> and <see cref="PassengerSeatAnchor"/>
    ///    are pelvis targets on exactly opposite sides of the body, sharing a
    ///    row and a height. The generator asserts the negation exactly, which
    ///    is the predicate a ride plan needs before it will seat anyone.
    ///  - The two door entry anchors sit on the cabin floor plane, which is
    ///    the plane a seated root stands on.
    ///  - Seated headroom is validated against the HERO's band, because he
    ///    will reuse the bus clips verbatim rather than get his own.
    ///  - The steering grips are children of the wheel pivot, so hand IK
    ///    survives a turned rim.
    ///
    /// One warning for that future plan: this car stands inside the point of
    /// interest's walkable rectangle, and the walkable mask knows nothing
    /// about props. <c>walkableArea.Contains</c> will happily approve a dock
    /// standing inside the bodywork, so a dock must also be tested against
    /// the car's own footprint.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class LastRouteCarAssetRegistry : MonoBehaviour
    {
        public const string PrefabResourcePath = "Vehicles/LastRouteCar3D";

        [SerializeField] private Transform modelRoot;
        [SerializeField] private Transform body;
        [SerializeField] private Transform driverDoorLeaf;
        [SerializeField] private Transform passengerDoorLeaf;
        [SerializeField] private Transform frontLeftWheel;
        [SerializeField] private Transform frontRightWheel;
        [SerializeField] private Transform rearLeftWheel;
        [SerializeField] private Transform rearRightWheel;
        [SerializeField] private Transform steeringWheelPivot;
        [SerializeField] private Transform leftSteeringGrip;
        [SerializeField] private Transform rightSteeringGrip;
        [SerializeField] private Transform driverSeatAnchor;
        [SerializeField] private Transform passengerSeatAnchor;
        [SerializeField] private Transform driverDoorEntryAnchor;
        [SerializeField] private Transform passengerDoorEntryAnchor;
        [SerializeField] private Transform perchSolesAnchor;
        [SerializeField] private Transform perchSeatAnchor;
        [SerializeField] private Transform gloveboxLidPivot;
        [SerializeField] private Transform radioPowerKnobPivot;
        [SerializeField] private Transform radioTuningKnobPivot;
        [SerializeField] private Transform radioNeedlePivot;
        [SerializeField] private Transform speedoNeedlePivot;
        [SerializeField] private float radioNeedleTravel;
        [SerializeField] private Renderer radioDialRenderer;
        [SerializeField] private Renderer[] renderers = Array.Empty<Renderer>();
        [SerializeField] private LastRouteCarRendererBinding[] bindings =
            Array.Empty<LastRouteCarRendererBinding>();
        [SerializeField] private Bounds localBounds;
        [SerializeField] private LastRouteCarDimensions dimensions;
        [SerializeField] private int triangleCount;
        [SerializeField] private string generatorVersion = string.Empty;
        [SerializeField] private string designId = string.Empty;
        [SerializeField] private string buildSignature = string.Empty;
        [SerializeField] private float perchSeatHeight;
        [SerializeField] private float perchDrop;

        public Transform ModelRoot => modelRoot;
        public Transform Body => body;
        public Transform DriverDoorLeaf => driverDoorLeaf;
        public Transform PassengerDoorLeaf => passengerDoorLeaf;
        public Transform FrontLeftWheel => frontLeftWheel;
        public Transform FrontRightWheel => frontRightWheel;
        public Transform RearLeftWheel => rearLeftWheel;
        public Transform RearRightWheel => rearRightWheel;
        public Transform SteeringWheelPivot => steeringWheelPivot;
        public Transform LeftSteeringGrip => leftSteeringGrip;
        public Transform RightSteeringGrip => rightSteeringGrip;
        public Transform DriverSeatAnchor => driverSeatAnchor;
        public Transform PassengerSeatAnchor => passengerSeatAnchor;
        public Transform DriverDoorEntryAnchor => driverDoorEntryAnchor;
        public Transform PassengerDoorEntryAnchor => passengerDoorEntryAnchor;

        /// <summary>
        /// Where the Ferryman's boots rest - on the front bumper. This is
        /// his stance root, because the rig measures a perched pose from the
        /// sitter's own soles. Its axes carry his facing, which is out over
        /// the nose at whoever is walking up; nothing re-derives it.
        /// </summary>
        public Transform PerchSolesAnchor => perchSolesAnchor;

        /// <summary>
        /// Where he actually sits: the bonnet skin. His authored pose has to
        /// land his backside here, and one test reads both manifests to
        /// prove the two generators still agree about the drop between this
        /// and his soles.
        /// </summary>
        public Transform PerchSeatAnchor => perchSeatAnchor;

        /// <summary>
        /// The dash's own movers, each authored on its own pivot so the
        /// runtime turns or swings it rather than re-authoring it. The lid
        /// hinges at the aperture's bottom edge; the two knobs and the
        /// speedometer needle turn about the car's fore-aft; the radio
        /// needle SLIDES along the dial by <see cref="RadioNeedleTravel"/>.
        /// </summary>
        public Transform GloveboxLidPivot => gloveboxLidPivot;

        public Transform RadioPowerKnobPivot => radioPowerKnobPivot;
        public Transform RadioTuningKnobPivot => radioTuningKnobPivot;
        public Transform RadioNeedlePivot => radioNeedlePivot;
        public Transform SpeedoNeedlePivot => speedoNeedlePivot;

        /// <summary>How far the radio needle slides from one end of its
        /// dial to the other, in metres, off the manifest.</summary>
        public float RadioNeedleTravel => radioNeedleTravel;

        /// <summary>The one thing in the cabin that lights: the radio's
        /// dial, bound by role at the asset build so the runtime never
        /// searches for it.</summary>
        public Renderer RadioDialRenderer => radioDialRenderer;

        public IReadOnlyList<Renderer> Renderers => renderers;
        public IReadOnlyList<LastRouteCarRendererBinding> Bindings => bindings;
        public Bounds LocalBounds => localBounds;
        public LastRouteCarDimensions Dimensions => dimensions;
        public int TriangleCount => triangleCount;
        public string GeneratorVersion => generatorVersion;
        public string DesignId => designId;
        public string BuildSignature => buildSignature;
        public float PerchSeatHeight => perchSeatHeight;
        public float PerchDrop => perchDrop;

        public bool IsBound =>
            modelRoot != null &&
            body != null &&
            driverDoorLeaf != null &&
            passengerDoorLeaf != null &&
            frontLeftWheel != null &&
            frontRightWheel != null &&
            rearLeftWheel != null &&
            rearRightWheel != null &&
            steeringWheelPivot != null &&
            leftSteeringGrip != null &&
            rightSteeringGrip != null &&
            driverSeatAnchor != null &&
            passengerSeatAnchor != null &&
            driverDoorEntryAnchor != null &&
            passengerDoorEntryAnchor != null &&
            perchSolesAnchor != null &&
            perchSeatAnchor != null &&
            gloveboxLidPivot != null &&
            radioPowerKnobPivot != null &&
            radioTuningKnobPivot != null &&
            radioNeedlePivot != null &&
            speedoNeedlePivot != null &&
            radioNeedleTravel > 0f &&
            radioDialRenderer != null &&
            renderers.Length > 0;

        public static GameObject LoadPrefab()
        {
            return Resources.Load<GameObject>(PrefabResourcePath);
        }

        public void Configure(
            Transform configuredModelRoot,
            Transform configuredBody,
            Transform configuredDriverDoorLeaf,
            Transform configuredPassengerDoorLeaf,
            Transform configuredFrontLeftWheel,
            Transform configuredFrontRightWheel,
            Transform configuredRearLeftWheel,
            Transform configuredRearRightWheel,
            Transform configuredSteeringWheelPivot,
            Transform configuredLeftSteeringGrip,
            Transform configuredRightSteeringGrip,
            Transform configuredDriverSeatAnchor,
            Transform configuredPassengerSeatAnchor,
            Transform configuredDriverDoorEntryAnchor,
            Transform configuredPassengerDoorEntryAnchor,
            Transform configuredPerchSolesAnchor,
            Transform configuredPerchSeatAnchor,
            Renderer[] configuredRenderers,
            LastRouteCarRendererBinding[] configuredBindings,
            Bounds configuredLocalBounds,
            LastRouteCarDimensions configuredDimensions,
            int configuredTriangleCount,
            string configuredGeneratorVersion,
            string configuredDesignId,
            string configuredBuildSignature,
            float configuredPerchSeatHeight,
            float configuredPerchDrop)
        {
            modelRoot = configuredModelRoot;
            body = configuredBody;
            driverDoorLeaf = configuredDriverDoorLeaf;
            passengerDoorLeaf = configuredPassengerDoorLeaf;
            frontLeftWheel = configuredFrontLeftWheel;
            frontRightWheel = configuredFrontRightWheel;
            rearLeftWheel = configuredRearLeftWheel;
            rearRightWheel = configuredRearRightWheel;
            steeringWheelPivot = configuredSteeringWheelPivot;
            leftSteeringGrip = configuredLeftSteeringGrip;
            rightSteeringGrip = configuredRightSteeringGrip;
            driverSeatAnchor = configuredDriverSeatAnchor;
            passengerSeatAnchor = configuredPassengerSeatAnchor;
            driverDoorEntryAnchor = configuredDriverDoorEntryAnchor;
            passengerDoorEntryAnchor = configuredPassengerDoorEntryAnchor;
            perchSolesAnchor = configuredPerchSolesAnchor;
            perchSeatAnchor = configuredPerchSeatAnchor;
            renderers = configuredRenderers ?? Array.Empty<Renderer>();
            bindings = configuredBindings
                ?? Array.Empty<LastRouteCarRendererBinding>();
            localBounds = configuredLocalBounds;
            dimensions = configuredDimensions;
            triangleCount = configuredTriangleCount;
            generatorVersion = configuredGeneratorVersion ?? string.Empty;
            designId = configuredDesignId ?? string.Empty;
            buildSignature = configuredBuildSignature ?? string.Empty;
            perchSeatHeight = configuredPerchSeatHeight;
            perchDrop = configuredPerchDrop;
        }

        /// <summary>
        /// The dash's bindings, kept off <see cref="Configure"/> rather than
        /// grown onto its twenty-seven positional arguments. Bound once by
        /// the editor asset build, the same as everything above.
        /// </summary>
        public void ConfigureDashboard(
            Transform configuredGloveboxLidPivot,
            Transform configuredRadioPowerKnobPivot,
            Transform configuredRadioTuningKnobPivot,
            Transform configuredRadioNeedlePivot,
            Transform configuredSpeedoNeedlePivot,
            float configuredRadioNeedleTravel,
            Renderer configuredRadioDialRenderer)
        {
            gloveboxLidPivot = configuredGloveboxLidPivot;
            radioPowerKnobPivot = configuredRadioPowerKnobPivot;
            radioTuningKnobPivot = configuredRadioTuningKnobPivot;
            radioNeedlePivot = configuredRadioNeedlePivot;
            speedoNeedlePivot = configuredSpeedoNeedlePivot;
            radioNeedleTravel = configuredRadioNeedleTravel;
            radioDialRenderer = configuredRadioDialRenderer;
        }
    }
}

using System;
using System.Collections.Generic;
using UnityEngine;

namespace BarPromenade
{
    public enum CityBusMaterialSlot
    {
        Body,
        Accent,
        Trim,
        Rubber,
        Metal,
        Glass,
        Interior,
        Seat,
        Rail,
        Dashboard,
        Headlight,
        TailLight,
        CabinLight,
        Destination
    }

    [Serializable]
    public sealed class CityBusRendererBinding
    {
        [SerializeField] private string sourceName;
        [SerializeField] private string role;
        [SerializeField] private CityBusMaterialSlot materialSlot;
        [SerializeField] private Renderer renderer;

        public CityBusRendererBinding(
            string configuredSourceName,
            string configuredRole,
            CityBusMaterialSlot configuredMaterialSlot,
            Renderer configuredRenderer)
        {
            sourceName = configuredSourceName ?? string.Empty;
            role = configuredRole ?? string.Empty;
            materialSlot = configuredMaterialSlot;
            renderer = configuredRenderer;
        }

        public string SourceName => sourceName;
        public string Role => role;
        public CityBusMaterialSlot MaterialSlot => materialSlot;
        public Renderer Renderer => renderer;
    }

    [Serializable]
    public struct CityBusDimensions
    {
        [SerializeField] private float length;
        [SerializeField] private float width;
        [SerializeField] private float height;
        [SerializeField] private float wheelbase;
        [SerializeField] private float wheelRadius;

        public CityBusDimensions(
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

    [DisallowMultipleComponent]
    public sealed class CityBusAssetRegistry : MonoBehaviour
    {
        public const string PrefabResourcePath = "Vehicles/CityBus3D";
        public const float DoorButtonTravelDistance = 0.012f;

        [SerializeField] private Transform modelRoot;
        [SerializeField] private Transform body;
        [SerializeField] private Transform frontDoorForwardLeaf;
        [SerializeField] private Transform frontDoorRearwardLeaf;
        [SerializeField] private Transform rearDoorForwardLeaf;
        [SerializeField] private Transform rearDoorRearwardLeaf;
        [SerializeField] private Transform frontLeftWheel;
        [SerializeField] private Transform frontRightWheel;
        [SerializeField] private Transform rearLeftWheel;
        [SerializeField] private Transform rearRightWheel;
        [SerializeField] private Transform frontLeftSteeringPivot;
        [SerializeField] private Transform frontRightSteeringPivot;
        [SerializeField] private Transform steeringWheelPivot;
        [SerializeField] private Transform leftSteeringGrip;
        [SerializeField] private Transform rightSteeringGrip;
        [SerializeField] private Transform doorButtonPivot;
        [SerializeField] private Transform doorButtonPressAnchor;
        [SerializeField] private Transform driverDoorLookAnchor;
        [SerializeField] private Vector3 steeringWheelAxisLocal =
            Vector3.forward;
        [SerializeField] private Vector3 doorButtonTravelLocal;
        [SerializeField] private Vector3 doorButtonRestLocalPosition;
        [SerializeField] private Quaternion steeringWheelRestLocalRotation =
            Quaternion.identity;
        [SerializeField] private Quaternion doorButtonRestLocalRotation =
            Quaternion.identity;
        [SerializeField] private Transform driverSeatAnchor;
        [SerializeField] private Transform frontDoorEntryAnchor;
        [SerializeField] private Transform rearDoorEntryAnchor;
        [SerializeField] private Transform[] passengerSeatAnchors =
            Array.Empty<Transform>();
        [SerializeField] private Renderer[] renderers =
            Array.Empty<Renderer>();
        [SerializeField] private CityBusRendererBinding[] rendererBindings =
            Array.Empty<CityBusRendererBinding>();
        [SerializeField] private Renderer[] headlights =
            Array.Empty<Renderer>();
        [SerializeField] private Renderer[] tailLights =
            Array.Empty<Renderer>();
        [SerializeField] private Renderer[] cabinLights =
            Array.Empty<Renderer>();
        [SerializeField] private Bounds localBounds;
        [SerializeField] private CityBusDimensions dimensions;
        [SerializeField] private int sourceTriangleCount;
        [SerializeField] private string sourceGeneratorVersion;
        [SerializeField] private string designId;
        [SerializeField] private string buildSignature;

        public Transform ModelRoot => modelRoot;
        public Transform Body => body;
        public Transform FrontDoorForwardLeaf => frontDoorForwardLeaf;
        public Transform FrontDoorRearwardLeaf => frontDoorRearwardLeaf;
        public Transform RearDoorForwardLeaf => rearDoorForwardLeaf;
        public Transform RearDoorRearwardLeaf => rearDoorRearwardLeaf;
        public Transform FrontLeftWheel => frontLeftWheel;
        public Transform FrontRightWheel => frontRightWheel;
        public Transform RearLeftWheel => rearLeftWheel;
        public Transform RearRightWheel => rearRightWheel;
        public Transform FrontLeftSteeringPivot => frontLeftSteeringPivot;
        public Transform FrontRightSteeringPivot => frontRightSteeringPivot;
        public Transform SteeringWheelPivot => steeringWheelPivot;
        public Transform LeftSteeringGrip => leftSteeringGrip;
        public Transform RightSteeringGrip => rightSteeringGrip;
        public Transform DoorButtonPivot => doorButtonPivot;
        public Transform DoorButtonPressAnchor => doorButtonPressAnchor;
        public Transform DriverDoorLookAnchor => driverDoorLookAnchor;
        public Vector3 SteeringWheelAxisLocal => steeringWheelAxisLocal;
        public Vector3 DoorButtonTravelLocal => doorButtonTravelLocal;
        public Transform DriverSeatAnchor => driverSeatAnchor;
        public Transform FrontDoorEntryAnchor => frontDoorEntryAnchor;
        public Transform RearDoorEntryAnchor => rearDoorEntryAnchor;
        public IReadOnlyList<Transform> PassengerSeatAnchors =>
            passengerSeatAnchors;
        public IReadOnlyList<Renderer> Renderers => renderers;
        public IReadOnlyList<CityBusRendererBinding> RendererBindings =>
            rendererBindings;
        public IReadOnlyList<Renderer> Headlights => headlights;
        public IReadOnlyList<Renderer> TailLights => tailLights;
        public IReadOnlyList<Renderer> CabinLights => cabinLights;
        public Bounds LocalBounds => localBounds;
        public CityBusDimensions Dimensions => dimensions;
        public int SourceTriangleCount => sourceTriangleCount;
        public string SourceGeneratorVersion => sourceGeneratorVersion;
        public string DesignId => designId;
        public string BuildSignature => buildSignature;

        public static GameObject LoadPrefab()
        {
            return Resources.Load<GameObject>(PrefabResourcePath);
        }

        public void Configure(
            Transform configuredModelRoot,
            Transform configuredBody,
            Transform configuredFrontDoorForwardLeaf,
            Transform configuredFrontDoorRearwardLeaf,
            Transform configuredRearDoorForwardLeaf,
            Transform configuredRearDoorRearwardLeaf,
            Transform configuredFrontLeftWheel,
            Transform configuredFrontRightWheel,
            Transform configuredRearLeftWheel,
            Transform configuredRearRightWheel,
            Transform configuredFrontLeftSteeringPivot,
            Transform configuredFrontRightSteeringPivot,
            Transform configuredDriverSeatAnchor,
            Transform configuredFrontDoorEntryAnchor,
            Transform configuredRearDoorEntryAnchor,
            Transform[] configuredPassengerSeatAnchors,
            Renderer[] configuredRenderers,
            CityBusRendererBinding[] configuredRendererBindings,
            Renderer[] configuredHeadlights,
            Renderer[] configuredTailLights,
            Renderer[] configuredCabinLights,
            Bounds configuredLocalBounds,
            CityBusDimensions configuredDimensions,
            int configuredSourceTriangleCount,
            string configuredSourceGeneratorVersion,
            string configuredDesignId,
            string configuredBuildSignature,
            Transform configuredSteeringWheelPivot = null,
            Transform configuredLeftSteeringGrip = null,
            Transform configuredRightSteeringGrip = null,
            Transform configuredDoorButtonPivot = null,
            Transform configuredDoorButtonPressAnchor = null,
            Transform configuredDriverDoorLookAnchor = null)
        {
            modelRoot = configuredModelRoot;
            body = configuredBody;
            frontDoorForwardLeaf = configuredFrontDoorForwardLeaf;
            frontDoorRearwardLeaf = configuredFrontDoorRearwardLeaf;
            rearDoorForwardLeaf = configuredRearDoorForwardLeaf;
            rearDoorRearwardLeaf = configuredRearDoorRearwardLeaf;
            frontLeftWheel = configuredFrontLeftWheel;
            frontRightWheel = configuredFrontRightWheel;
            rearLeftWheel = configuredRearLeftWheel;
            rearRightWheel = configuredRearRightWheel;
            frontLeftSteeringPivot = configuredFrontLeftSteeringPivot;
            frontRightSteeringPivot = configuredFrontRightSteeringPivot;
            steeringWheelPivot = configuredSteeringWheelPivot;
            leftSteeringGrip = configuredLeftSteeringGrip;
            rightSteeringGrip = configuredRightSteeringGrip;
            doorButtonPivot = configuredDoorButtonPivot;
            doorButtonPressAnchor = configuredDoorButtonPressAnchor;
            driverDoorLookAnchor = configuredDriverDoorLookAnchor;
            steeringWheelAxisLocal = Vector3.forward;
            doorButtonTravelLocal = ResolveParentLocalTravel(
                doorButtonPivot,
                doorButtonPressAnchor,
                DoorButtonTravelDistance);
            doorButtonRestLocalPosition = doorButtonPivot != null
                ? doorButtonPivot.localPosition
                : Vector3.zero;
            steeringWheelRestLocalRotation = steeringWheelPivot != null
                ? steeringWheelPivot.localRotation
                : Quaternion.identity;
            doorButtonRestLocalRotation = doorButtonPivot != null
                ? doorButtonPivot.localRotation
                : Quaternion.identity;
            driverSeatAnchor = configuredDriverSeatAnchor;
            frontDoorEntryAnchor = configuredFrontDoorEntryAnchor;
            rearDoorEntryAnchor = configuredRearDoorEntryAnchor;
            passengerSeatAnchors = configuredPassengerSeatAnchors ??
                Array.Empty<Transform>();
            renderers = configuredRenderers ?? Array.Empty<Renderer>();
            rendererBindings = configuredRendererBindings ??
                Array.Empty<CityBusRendererBinding>();
            headlights = configuredHeadlights ?? Array.Empty<Renderer>();
            tailLights = configuredTailLights ?? Array.Empty<Renderer>();
            cabinLights = configuredCabinLights ?? Array.Empty<Renderer>();
            localBounds = configuredLocalBounds;
            dimensions = configuredDimensions;
            sourceTriangleCount = configuredSourceTriangleCount;
            sourceGeneratorVersion = configuredSourceGeneratorVersion ??
                string.Empty;
            designId = configuredDesignId ?? string.Empty;
            buildSignature = configuredBuildSignature ?? string.Empty;
        }

        public void ResetArticulation()
        {
            ResetRotation(frontDoorForwardLeaf);
            ResetRotation(frontDoorRearwardLeaf);
            ResetRotation(rearDoorForwardLeaf);
            ResetRotation(rearDoorRearwardLeaf);
            ResetRotation(frontLeftSteeringPivot);
            ResetRotation(frontRightSteeringPivot);
            ResetRotation(
                steeringWheelPivot,
                steeringWheelRestLocalRotation);
            ResetRotation(doorButtonPivot, doorButtonRestLocalRotation);
            ResetPosition(doorButtonPivot, doorButtonRestLocalPosition);
            ResetRotation(frontLeftWheel);
            ResetRotation(frontRightWheel);
            ResetRotation(rearLeftWheel);
            ResetRotation(rearRightWheel);
        }

        private static void ResetRotation(Transform target)
        {
            if (target != null)
            {
                target.localRotation = Quaternion.identity;
            }
        }

        private static void ResetRotation(
            Transform target,
            Quaternion rotation)
        {
            if (target != null)
            {
                target.localRotation = rotation;
            }
        }

        private static void ResetPosition(Transform target, Vector3 position)
        {
            if (target != null)
            {
                target.localPosition = position;
            }
        }

        private static Vector3 ResolveParentLocalTravel(
            Transform target,
            Transform surfaceAnchor,
            float worldDistance)
        {
            if (target == null ||
                target.parent == null ||
                surfaceAnchor == null ||
                worldDistance <= 0f)
            {
                return Vector3.zero;
            }

            Vector3 worldDirection =
                target.position - surfaceAnchor.position;
            if (worldDirection.sqrMagnitude < 0.000001f)
            {
                return Vector3.zero;
            }

            return target.parent.InverseTransformVector(
                worldDirection.normalized * worldDistance);
        }
    }
}

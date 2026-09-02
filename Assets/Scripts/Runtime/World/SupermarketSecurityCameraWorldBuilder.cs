using System;
using System.Collections.Generic;
using UnityEngine;

namespace BarPromenade
{
    /// <summary>
    /// One ceiling-corner CCTV unit. The mount is static; the head
    /// pivot servos every frame so the lens is always trained on the
    /// hero, wherever he goes in the hall.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class SupermarketSecurityCamera : MonoBehaviour
    {
        public const float FocusHeightMeters = 1.5f;
        public const float ServoDegreesPerSecond = 240f;

        private Transform headPivot;
        private Transform target;
        private bool isInitialized;

        public Transform HeadPivot => headPivot;

        public void Initialize(
            Transform configuredHeadPivot,
            Transform trackedTarget)
        {
            headPivot = configuredHeadPivot != null
                ? configuredHeadPivot
                : throw new ArgumentNullException(
                    nameof(configuredHeadPivot));
            target = trackedTarget;
            isInitialized = true;

            // Aimed from the very first frame — a security camera is
            // never caught pointing at a wall.
            if (target != null)
            {
                headPivot.rotation = ResolveAim(
                    headPivot.position,
                    ResolveFocusPoint());
            }
        }

        /// <summary>
        /// The servo step. Public so EditMode tests can drive it
        /// without a player loop.
        /// </summary>
        public void Track(float deltaTime)
        {
            if (!isInitialized || target == null)
            {
                return;
            }

            Quaternion desired = ResolveAim(
                headPivot.position,
                ResolveFocusPoint());
            headPivot.rotation = Quaternion.RotateTowards(
                headPivot.rotation,
                desired,
                ServoDegreesPerSecond * Mathf.Max(0f, deltaTime));
        }

        public static Quaternion ResolveAim(
            Vector3 headPosition,
            Vector3 focusPoint)
        {
            Vector3 direction = focusPoint - headPosition;
            return direction.sqrMagnitude > 0.0001f
                ? Quaternion.LookRotation(direction, Vector3.up)
                : Quaternion.identity;
        }

        private Vector3 ResolveFocusPoint()
        {
            return target.position +
                Vector3.up * FocusHeightMeters;
        }

        private void LateUpdate()
        {
            Track(Time.deltaTime);
        }
    }

    /// <summary>
    /// Builds the four corner CCTV units of the supermarket: a short
    /// ceiling stem in each corner and a boxy camera head that always
    /// follows the hero. Pure dressing — no colliders, no real light;
    /// the recording LED is the usual fake emissive.
    /// </summary>
    public static class SupermarketSecurityCameraWorldBuilder
    {
        public const string RootName = "Supermarket Security Cameras";
        public const int CameraCount = 4;
        public const float CornerInsetMeters = 0.62f;
        public const float HeadDropMeters = 0.50f;

        private static readonly Color Housing =
            new Color(0.155f, 0.175f, 0.165f);
        private static readonly Color Body =
            new Color(0.235f, 0.255f, 0.245f);
        private static readonly Color Lens =
            new Color(0.028f, 0.032f, 0.035f);
        private static readonly Color RecordingLed =
            new Color(0.85f, 0.12f, 0.08f);

        public static IReadOnlyList<SupermarketSecurityCamera> Build(
            Transform parent,
            SupermarketInteriorLayoutPlan plan,
            Transform playerBody,
            SupermarketInteriorAssetRegistry assetRegistry = null)
        {
            if (parent == null)
            {
                throw new ArgumentNullException(nameof(parent));
            }

            if (plan == null)
            {
                throw new ArgumentNullException(nameof(plan));
            }

            Transform root = new GameObject(RootName).transform;
            root.SetParent(parent, false);

            Vector3[] headPositions = ResolveHeadPositions(plan);
            var cameras = new List<SupermarketSecurityCamera>(
                headPositions.Length);
            for (int index = 0; index < headPositions.Length; index++)
            {
                cameras.Add(assetRegistry != null
                    ? BindAuthoredCamera(
                        root,
                        assetRegistry,
                        index + 1,
                        playerBody)
                    : BuildCamera(
                        root,
                        plan,
                        headPositions[index],
                        index + 1,
                        playerBody));
            }

            GameLog.Info(
                "supermarket",
                "security_cameras_built",
                GameLog.Field("camera_count", cameras.Count));
            return cameras;
        }

        /// <summary>
        /// The four head-pivot positions: one per room corner, inset
        /// from both walls, hanging just below the ceiling.
        /// </summary>
        public static Vector3[] ResolveHeadPositions(
            SupermarketInteriorLayoutPlan plan)
        {
            float x = (plan.RoomSize.x * 0.5f) -
                plan.WallThickness -
                CornerInsetMeters;
            float z = (plan.RoomSize.y * 0.5f) -
                plan.WallThickness -
                CornerInsetMeters;
            float y = plan.RoomHeight - HeadDropMeters;
            return new[]
            {
                new Vector3(-x, y, -z),
                new Vector3(x, y, -z),
                new Vector3(-x, y, z),
                new Vector3(x, y, z)
            };
        }

        private static SupermarketSecurityCamera BuildCamera(
            Transform root,
            SupermarketInteriorLayoutPlan plan,
            Vector3 headPosition,
            int index,
            Transform playerBody)
        {
            var unit = new GameObject(
                $"Supermarket Security Camera {index}");
            unit.transform.SetParent(root, false);
            unit.transform.localPosition = headPosition;

            // Chunky enough to read from the shop floor: a thick
            // ceiling stem and a fat boxy head, PS1 CCTV.
            float stemHeight =
                plan.RoomHeight - headPosition.y;
            RuntimePrimitiveFactory.CreateBox(
                "Camera Ceiling Stem",
                unit.transform,
                new Vector3(0f, (stemHeight * 0.5f) + 0.02f, 0f),
                new Vector3(0.13f, stemHeight, 0.13f),
                Housing,
                false);

            var head = new GameObject("Camera Head");
            head.transform.SetParent(unit.transform, false);

            RuntimePrimitiveFactory.CreateBox(
                "Camera Body",
                head.transform,
                new Vector3(0f, 0f, 0.19f),
                new Vector3(0.27f, 0.27f, 0.62f),
                Body,
                false);
            RuntimePrimitiveFactory.CreateBox(
                "Camera Hood",
                head.transform,
                new Vector3(0f, 0.16f, 0.27f),
                new Vector3(0.31f, 0.06f, 0.58f),
                Housing,
                false);
            RuntimePrimitiveFactory.CreateBox(
                "Camera Lens",
                head.transform,
                new Vector3(0f, 0f, 0.545f),
                new Vector3(0.19f, 0.19f, 0.10f),
                Lens,
                false);
            RuntimePrimitiveFactory.CreateBox(
                "Camera Lens Iris",
                head.transform,
                new Vector3(0f, 0f, 0.60f),
                new Vector3(0.10f, 0.10f, 0.015f),
                Body,
                false);
            GameObject led = RuntimePrimitiveFactory.CreateBox(
                "Camera Recording Led",
                head.transform,
                new Vector3(0.09f, 0.105f, -0.08f),
                new Vector3(0.05f, 0.05f, 0.05f),
                RecordingLed,
                CityNightResources.EmissiveMaterial,
                false);
            DisableShadows(led);

            var camera = unit
                .AddComponent<SupermarketSecurityCamera>();
            camera.Initialize(head.transform, playerBody);
            return camera;
        }

        private static SupermarketSecurityCamera BindAuthoredCamera(
            Transform controllerRoot,
            SupermarketInteriorAssetRegistry assetRegistry,
            int index,
            Transform playerBody)
        {
            string role = $"cctv_head_{index:00}";
            Transform headPivot = assetRegistry.GetAnchor(role);
            var controller = new GameObject(
                $"Supermarket Security Camera {index}");
            controller.transform.SetParent(controllerRoot, false);
            var camera = controller.AddComponent<SupermarketSecurityCamera>();
            camera.Initialize(headPivot, playerBody);
            return camera;
        }

        private static void DisableShadows(GameObject target)
        {
            Renderer[] renderers =
                target.GetComponentsInChildren<Renderer>(true);
            for (int index = 0; index < renderers.Length; index++)
            {
                renderers[index].shadowCastingMode =
                    UnityEngine.Rendering.ShadowCastingMode.Off;
                renderers[index].receiveShadows = false;
            }
        }
    }
}

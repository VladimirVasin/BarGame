using System;
using System.Collections.Generic;
using UnityEngine;

namespace BarPromenade
{
    public enum StairwellCameraShotKind
    {
        GroundFlight = 0,
        MiddleFlight = 1,
        ApartmentLanding = 2
    }

    public readonly struct StairwellCameraShot
    {
        public StairwellCameraShot(
            StairwellCameraShotKind kind,
            Bounds activationBounds,
            Bounds holdBounds,
            Vector3 position,
            Vector3 eulerAngles,
            float fieldOfView)
        {
            if (!Enum.IsDefined(
                    typeof(StairwellCameraShotKind),
                    kind))
            {
                throw new ArgumentOutOfRangeException(nameof(kind));
            }

            if (!IsPositiveFinite(activationBounds.size) ||
                !IsPositiveFinite(holdBounds.size) ||
                !Contains(holdBounds, activationBounds) ||
                !IsFinite(position) ||
                !IsFinite(eulerAngles) ||
                !IsFinite(fieldOfView) ||
                fieldOfView < 20f ||
                fieldOfView > 100f)
            {
                throw new ArgumentException(
                    "A stairwell camera shot contains invalid data.");
            }

            Kind = kind;
            ActivationBounds = activationBounds;
            HoldBounds = holdBounds;
            Position = position;
            Rotation = Quaternion.Euler(eulerAngles);
            FieldOfView = fieldOfView;
        }

        public StairwellCameraShotKind Kind { get; }
        public Bounds ActivationBounds { get; }
        public Bounds HoldBounds { get; }
        public Vector3 Position { get; }
        public Quaternion Rotation { get; }
        public float FieldOfView { get; }

        public bool IsInActivationArea(Vector3 position)
        {
            return ActivationBounds.Contains(position);
        }

        public bool IsInHoldArea(Vector3 position)
        {
            return HoldBounds.Contains(position);
        }

        private static bool Contains(Bounds outer, Bounds inner)
        {
            return outer.min.x <= inner.min.x &&
                   outer.min.y <= inner.min.y &&
                   outer.min.z <= inner.min.z &&
                   outer.max.x >= inner.max.x &&
                   outer.max.y >= inner.max.y &&
                   outer.max.z >= inner.max.z;
        }

        private static bool IsPositiveFinite(Vector3 value)
        {
            return IsFinite(value) &&
                   value.x > 0f &&
                   value.y > 0f &&
                   value.z > 0f;
        }

        private static bool IsFinite(Vector3 value)
        {
            return IsFinite(value.x) &&
                   IsFinite(value.y) &&
                   IsFinite(value.z);
        }

        private static bool IsFinite(float value)
        {
            return !float.IsNaN(value) &&
                   !float.IsInfinity(value);
        }
    }

    public sealed class StairwellCameraShotSelector
    {
        private readonly StairwellCameraShot[] shots;
        private StairwellCameraShot currentShot;
        private bool hasCurrentShot;

        public StairwellCameraShotSelector(
            IReadOnlyList<StairwellCameraShot> cameraShots)
        {
            if (cameraShots == null)
            {
                throw new ArgumentNullException(
                    nameof(cameraShots));
            }

            if (cameraShots.Count == 0)
            {
                throw new ArgumentException(
                    "At least one stairwell camera shot is required.",
                    nameof(cameraShots));
            }

            shots = new StairwellCameraShot[cameraShots.Count];
            for (int index = 0;
                 index < cameraShots.Count;
                 index++)
            {
                StairwellCameraShot shot = cameraShots[index];
                for (int previous = 0;
                     previous < index;
                     previous++)
                {
                    if (shots[previous].Kind == shot.Kind)
                    {
                        throw new ArgumentException(
                            $"Stairwell camera shot '{shot.Kind}' is duplicated.",
                            nameof(cameraShots));
                    }
                }

                shots[index] = shot;
            }

            Array.Sort(
                shots,
                (left, right) =>
                    left.Kind.CompareTo(right.Kind));
        }

        public StairwellCameraShot Select(Vector3 worldPosition)
        {
            if (hasCurrentShot &&
                currentShot.IsInHoldArea(worldPosition))
            {
                return currentShot;
            }

            for (int index = 0; index < shots.Length; index++)
            {
                if (!shots[index].IsInActivationArea(
                        worldPosition))
                {
                    continue;
                }

                currentShot = shots[index];
                hasCurrentShot = true;
                return currentShot;
            }

            if (hasCurrentShot)
            {
                return currentShot;
            }

            throw new InvalidOperationException(
                "The player is outside every stairwell camera zone.");
        }
    }

    [DefaultExecutionOrder(-100)]
    [DisallowMultipleComponent]
    public sealed class StairwellFixedCameraController :
        MonoBehaviour
    {
        private PlayerCameraFollow cameraFollow;
        private Transform target;
        private BillboardSprite targetBillboard;
        private StairwellCameraShotSelector selector;

        public bool IsInitialized { get; private set; }
        public StairwellCameraShot ActiveShot { get; private set; }
        public StairwellCameraShotKind ActiveShotKind =>
            ActiveShot.Kind;

        public static IReadOnlyList<StairwellCameraShot>
            CreateDefaultShots(StairwellLayoutPlan plan)
        {
            if (plan == null)
            {
                throw new ArgumentNullException(nameof(plan));
            }

            float roomHalfWidth = plan.RoomSize.x * 0.5f;
            float roomHalfDepth = plan.RoomSize.y * 0.5f;
            return new[]
            {
                new StairwellCameraShot(
                    StairwellCameraShotKind.GroundFlight,
                    CreateZone(
                        roomHalfWidth - 0.25f,
                        roomHalfDepth - 0.30f,
                        -0.10f,
                        1.52f),
                    CreateZone(
                        roomHalfWidth - 0.15f,
                        roomHalfDepth - 0.25f,
                        -0.10f,
                        1.56f),
                    new Vector3(-3.72f, 2.62f, -4.18f),
                    new Vector3(24f, 38f, 0f),
                    68f),
                new StairwellCameraShot(
                    StairwellCameraShotKind.MiddleFlight,
                    CreateZone(
                        roomHalfWidth - 0.25f,
                        roomHalfDepth - 0.30f,
                        1.50f,
                        3.10f),
                    CreateZone(
                        roomHalfWidth - 0.15f,
                        roomHalfDepth - 0.25f,
                        1.44f,
                        3.14f),
                    new Vector3(-3.68f, 4.78f, 4.22f),
                    new Vector3(19f, 136f, 0f),
                    68f),
                new StairwellCameraShot(
                    StairwellCameraShotKind.ApartmentLanding,
                    CreateZone(
                        roomHalfWidth - 0.25f,
                        roomHalfDepth - 0.30f,
                        3.08f,
                        5.95f),
                    CreateZone(
                        roomHalfWidth - 0.15f,
                        roomHalfDepth - 0.25f,
                        3.02f,
                        6.10f),
                    new Vector3(-3.70f, 5.45f, -4.18f),
                    new Vector3(18f, 56f, 0f),
                    72f)
            };
        }

        public void Initialize(
            PlayerCameraFollow follow,
            Transform cameraTarget,
            IReadOnlyList<StairwellCameraShot> cameraShots)
        {
            if (follow == null)
            {
                throw new ArgumentNullException(nameof(follow));
            }

            if (cameraTarget == null)
            {
                throw new ArgumentNullException(
                    nameof(cameraTarget));
            }

            var nextSelector =
                new StairwellCameraShotSelector(cameraShots);
            StairwellCameraShot initialShot =
                nextSelector.Select(cameraTarget.position);
            if (IsInitialized &&
                cameraFollow != null &&
                cameraFollow != follow)
            {
                cameraFollow.ClearFixedPose();
            }

            cameraFollow = follow;
            target = cameraTarget;
            targetBillboard =
                cameraTarget.GetComponentInChildren<
                    BillboardSprite>(true);
            targetBillboard?.SetCameraPlaneAlignment(true);
            selector = nextSelector;
            ActiveShot = initialShot;
            IsInitialized = true;
            ApplyShot(initialShot);
        }

        private static Bounds CreateZone(
            float halfWidth,
            float halfDepth,
            float minimumY,
            float maximumY)
        {
            return new Bounds(
                new Vector3(
                    0f,
                    (minimumY + maximumY) * 0.5f,
                    0f),
                new Vector3(
                    halfWidth * 2f,
                    maximumY - minimumY,
                    halfDepth * 2f));
        }

        private void OnEnable()
        {
            if (!IsInitialized)
            {
                return;
            }

            targetBillboard?.SetCameraPlaneAlignment(true);
            RefreshSelection(true);
        }

        private void Update()
        {
            if (IsInitialized)
            {
                RefreshSelection(false);
            }
        }

        private void OnDisable()
        {
            ReleaseCamera();
        }

        private void OnDestroy()
        {
            ReleaseCamera();
        }

        private void RefreshSelection(bool forceApply)
        {
            if (cameraFollow == null ||
                target == null ||
                selector == null)
            {
                return;
            }

            StairwellCameraShot selected =
                selector.Select(target.position);
            bool changed =
                selected.Kind != ActiveShot.Kind;
            ActiveShot = selected;
            if (forceApply ||
                changed ||
                !cameraFollow.FixedPoseActive)
            {
                ApplyShot(selected);
            }
        }

        private void ApplyShot(StairwellCameraShot shot)
        {
            cameraFollow.SetFixedPose(
                shot.Position,
                shot.Rotation,
                shot.FieldOfView);
            targetBillboard?.FaceCameraNow();
        }

        private void ReleaseCamera()
        {
            if (!IsInitialized || cameraFollow == null)
            {
                return;
            }

            targetBillboard?.SetCameraPlaneAlignment(false);
            cameraFollow.ClearFixedPose();
        }
    }
}

using System;
using System.Collections.Generic;
using UnityEngine;

namespace BarPromenade
{
    public enum HomeCameraShotKind
    {
        MainRoom = 0,
        Bathroom = 1,
        Balcony = 2
    }

    public readonly struct HomeCameraShot
    {
        public HomeCameraShot(
            HomeCameraShotKind kind,
            Rect activationBounds,
            Rect holdBounds,
            Vector3 position,
            Vector3 eulerAngles,
            float fieldOfView)
            : this(
                kind,
                activationBounds,
                holdBounds,
                position,
                Quaternion.Euler(eulerAngles),
                fieldOfView)
        {
        }

        public HomeCameraShot(
            HomeCameraShotKind kind,
            Rect activationBounds,
            Rect holdBounds,
            Vector3 position,
            Quaternion rotation,
            float fieldOfView)
        {
            ValidateKind(kind);
            ValidateBounds(
                activationBounds,
                nameof(activationBounds));
            ValidateBounds(
                holdBounds,
                nameof(holdBounds));
            if (!Contains(holdBounds, activationBounds))
            {
                throw new ArgumentException(
                    "A home camera activation area must stay inside its hold area.",
                    nameof(activationBounds));
            }

            if (!IsFinite(position))
            {
                throw new ArgumentOutOfRangeException(
                    nameof(position),
                    "A home camera position must be finite.");
            }

            if (!IsValidRotation(rotation))
            {
                throw new ArgumentOutOfRangeException(
                    nameof(rotation),
                    "A home camera rotation must be finite and non-zero.");
            }

            if (!IsFinite(fieldOfView) ||
                fieldOfView < 20f ||
                fieldOfView > 100f)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(fieldOfView),
                    "A home camera field of view must be between 20 and 100 degrees.");
            }

            Kind = kind;
            ActivationBounds = activationBounds;
            HoldBounds = holdBounds;
            Position = position;
            Rotation = Normalize(rotation);
            FieldOfView = fieldOfView;
        }

        public HomeCameraShotKind Kind { get; }
        public Rect ActivationBounds { get; }
        public Rect HoldBounds { get; }
        public Vector3 Position { get; }
        public Quaternion Rotation { get; }
        public Vector3 EulerAngles => Rotation.eulerAngles;
        public float FieldOfView { get; }

        public bool IsInActivationArea(Vector3 worldPosition)
        {
            return Contains(ActivationBounds, worldPosition);
        }

        public bool IsInHoldArea(Vector3 worldPosition)
        {
            return Contains(HoldBounds, worldPosition);
        }

        internal void Validate()
        {
            ValidateKind(Kind);
            ValidateBounds(
                ActivationBounds,
                nameof(ActivationBounds));
            ValidateBounds(
                HoldBounds,
                nameof(HoldBounds));
            if (!Contains(HoldBounds, ActivationBounds) ||
                !IsFinite(Position) ||
                !IsValidRotation(Rotation) ||
                !IsFinite(FieldOfView) ||
                FieldOfView < 20f ||
                FieldOfView > 100f)
            {
                throw new ArgumentException(
                    "A home camera shot contains invalid data.");
            }
        }

        private static bool Contains(
            Rect bounds,
            Vector3 worldPosition)
        {
            return worldPosition.x >= bounds.xMin &&
                   worldPosition.x <= bounds.xMax &&
                   worldPosition.z >= bounds.yMin &&
                   worldPosition.z <= bounds.yMax;
        }

        private static bool Contains(
            Rect outer,
            Rect inner)
        {
            return inner.xMin >= outer.xMin &&
                   inner.xMax <= outer.xMax &&
                   inner.yMin >= outer.yMin &&
                   inner.yMax <= outer.yMax;
        }

        private static void ValidateKind(
            HomeCameraShotKind kind)
        {
            if (!Enum.IsDefined(
                    typeof(HomeCameraShotKind),
                    kind))
            {
                throw new ArgumentOutOfRangeException(
                    nameof(kind),
                    kind,
                    "Unknown home camera shot kind.");
            }
        }

        private static void ValidateBounds(
            Rect bounds,
            string parameterName)
        {
            if (!IsFinite(bounds.x) ||
                !IsFinite(bounds.y) ||
                !IsFinite(bounds.width) ||
                !IsFinite(bounds.height) ||
                !IsFinite(bounds.xMin) ||
                !IsFinite(bounds.xMax) ||
                !IsFinite(bounds.yMin) ||
                !IsFinite(bounds.yMax) ||
                bounds.width <= 0f ||
                bounds.height <= 0f)
            {
                throw new ArgumentOutOfRangeException(
                    parameterName,
                    "Home camera bounds must be finite and positive.");
            }
        }

        private static bool IsFinite(Vector3 value)
        {
            return IsFinite(value.x) &&
                   IsFinite(value.y) &&
                   IsFinite(value.z);
        }

        private static bool IsFinite(Quaternion value)
        {
            return IsFinite(value.x) &&
                   IsFinite(value.y) &&
                   IsFinite(value.z) &&
                   IsFinite(value.w);
        }

        private static bool IsValidRotation(
            Quaternion value)
        {
            if (!IsFinite(value))
            {
                return false;
            }

            float magnitudeSquared =
                QuaternionMagnitudeSquared(value);
            return IsFinite(magnitudeSquared) &&
                   magnitudeSquared > 0.000001f;
        }

        private static bool IsFinite(float value)
        {
            return !float.IsNaN(value) &&
                   !float.IsInfinity(value);
        }

        private static float QuaternionMagnitudeSquared(
            Quaternion value)
        {
            return value.x * value.x +
                   value.y * value.y +
                   value.z * value.z +
                   value.w * value.w;
        }

        private static Quaternion Normalize(Quaternion value)
        {
            float inverseMagnitude =
                1f /
                Mathf.Sqrt(
                    QuaternionMagnitudeSquared(value));
            return new Quaternion(
                value.x * inverseMagnitude,
                value.y * inverseMagnitude,
                value.z * inverseMagnitude,
                value.w * inverseMagnitude);
        }
    }

    public sealed class HomeCameraShotSelector
    {
        private readonly HomeCameraShot[] shots;
        private HomeCameraShot currentShot;
        private bool hasCurrentShot;

        public HomeCameraShotSelector(
            IReadOnlyList<HomeCameraShot> cameraShots)
        {
            if (cameraShots == null)
            {
                throw new ArgumentNullException(
                    nameof(cameraShots));
            }

            if (cameraShots.Count == 0)
            {
                throw new ArgumentException(
                    "At least one home camera shot is required.",
                    nameof(cameraShots));
            }

            shots = new HomeCameraShot[cameraShots.Count];
            for (int index = 0;
                 index < cameraShots.Count;
                 index++)
            {
                HomeCameraShot shot = cameraShots[index];
                shot.Validate();
                for (int previous = 0;
                     previous < index;
                     previous++)
                {
                    if (shots[previous].Kind == shot.Kind)
                    {
                        throw new ArgumentException(
                            $"Home camera shot kind '{shot.Kind}' is duplicated.",
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

        public bool HasCurrentShot => hasCurrentShot;
        public HomeCameraShot CurrentShot
        {
            get
            {
                if (!hasCurrentShot)
                {
                    throw new InvalidOperationException(
                        "No home camera shot has been selected.");
                }

                return currentShot;
            }
        }

        public HomeCameraShot Select(Vector3 worldPosition)
        {
            if (hasCurrentShot &&
                currentShot.IsInHoldArea(worldPosition))
            {
                return currentShot;
            }

            for (int index = 0;
                 index < shots.Length;
                 index++)
            {
                HomeCameraShot candidate = shots[index];
                if (!candidate.IsInActivationArea(
                        worldPosition))
                {
                    continue;
                }

                currentShot = candidate;
                hasCurrentShot = true;
                return currentShot;
            }

            if (TryGetShot(
                    HomeCameraShotKind.MainRoom,
                    out HomeCameraShot fallbackShot))
            {
                currentShot = fallbackShot;
                hasCurrentShot = true;
                return currentShot;
            }

            if (hasCurrentShot)
            {
                return currentShot;
            }

            throw new InvalidOperationException(
                "The target is outside every home camera activation area.");
        }

        public bool TryGetShot(
            HomeCameraShotKind kind,
            out HomeCameraShot shot)
        {
            for (int index = 0;
                 index < shots.Length;
                 index++)
            {
                if (shots[index].Kind != kind)
                {
                    continue;
                }

                shot = shots[index];
                return true;
            }

            shot = default;
            return false;
        }
    }

    [DefaultExecutionOrder(-100)]
    [DisallowMultipleComponent]
    public sealed class HomeFixedCameraController : MonoBehaviour
    {
        private PlayerCameraFollow cameraFollow;
        private Transform target;
        private BillboardSprite targetBillboard;
        private HomeCameraShotSelector selector;

        public bool IsInitialized { get; private set; }
        public HomeCameraShotKind ActiveShotKind =>
            ActiveShot.Kind;
        public HomeCameraShot ActiveShot { get; private set; }

        public void Initialize(
            PlayerCameraFollow follow,
            Transform cameraTarget,
            IReadOnlyList<HomeCameraShot> cameraShots)
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
                new HomeCameraShotSelector(cameraShots);
            if (!nextSelector.TryGetShot(
                    HomeCameraShotKind.MainRoom,
                    out HomeCameraShot mainShot))
            {
                throw new ArgumentException(
                    "A main-room home camera shot is required.",
                    nameof(cameraShots));
            }

            if (!mainShot.IsInActivationArea(
                    cameraTarget.position))
            {
                throw new ArgumentException(
                    "The home camera target must spawn inside the main-room activation area.",
                    nameof(cameraTarget));
            }

            HomeCameraShot initialShot =
                nextSelector.Select(
                    cameraTarget.position);
            if (initialShot.Kind !=
                HomeCameraShotKind.MainRoom)
            {
                throw new ArgumentException(
                    "The home camera must initialize with the main-room shot.",
                    nameof(cameraShots));
            }

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
            targetBillboard?
                .SetCameraPlaneAlignment(true);
            selector = nextSelector;
            ActiveShot = initialShot;
            IsInitialized = true;
            ApplyShot(ActiveShot);
        }

        private void OnEnable()
        {
            if (IsInitialized)
            {
                targetBillboard?
                    .SetCameraPlaneAlignment(true);
                RefreshSelection(true);
            }
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
            if (IsInitialized && cameraFollow != null)
            {
                targetBillboard?
                    .SetCameraPlaneAlignment(false);
                cameraFollow.ClearFixedPose();
            }
        }

        private void OnDestroy()
        {
            if (IsInitialized && cameraFollow != null)
            {
                targetBillboard?
                    .SetCameraPlaneAlignment(false);
                cameraFollow.ClearFixedPose();
            }
        }

        private void RefreshSelection(bool forceApply)
        {
            if (cameraFollow == null ||
                target == null ||
                selector == null)
            {
                return;
            }

            HomeCameraShot selected =
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

        private void ApplyShot(HomeCameraShot shot)
        {
            cameraFollow.SetFixedPose(
                shot.Position,
                shot.Rotation,
                shot.FieldOfView);
            targetBillboard?.FaceCameraNow();
        }
    }
}

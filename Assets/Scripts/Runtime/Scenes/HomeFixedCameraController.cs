using System;
using System.Collections.Generic;
using UnityEngine;

namespace BarPromenade
{
    public enum HomeCameraShotKind
    {
        MainRoom = 0,
        Bathroom = 1,
        Balcony = 2,
        StairAndUpperCorridor = 3,
        UpperSouthRoom = 4,
        UpperNorthRoom = 5
    }

    public readonly struct HomeCameraShot
    {
        private static readonly Vector2 UnboundedHeightRange =
            new Vector2(-10000f, 10000f);

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
                UnboundedHeightRange,
                UnboundedHeightRange,
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
            : this(
                kind,
                activationBounds,
                holdBounds,
                UnboundedHeightRange,
                UnboundedHeightRange,
                position,
                rotation,
                fieldOfView)
        {
        }

        public HomeCameraShot(
            HomeCameraShotKind kind,
            Rect activationBounds,
            Rect holdBounds,
            Vector2 activationHeightRange,
            Vector2 holdHeightRange,
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

            ValidateHeightRange(
                activationHeightRange,
                nameof(activationHeightRange));
            ValidateHeightRange(
                holdHeightRange,
                nameof(holdHeightRange));
            if (!Contains(holdHeightRange, activationHeightRange))
            {
                throw new ArgumentException(
                    "A home camera activation height range must stay inside its hold range.",
                    nameof(activationHeightRange));
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
            ActivationHeightRange = activationHeightRange;
            HoldHeightRange = holdHeightRange;
            Position = position;
            Rotation = Normalize(rotation);
            FieldOfView = fieldOfView;
            Focus = FixedCameraFocus.None;
        }

        private HomeCameraShot(
            in HomeCameraShot source,
            FixedCameraFocus focus)
        {
            Kind = source.Kind;
            ActivationBounds = source.ActivationBounds;
            HoldBounds = source.HoldBounds;
            ActivationHeightRange = source.ActivationHeightRange;
            HoldHeightRange = source.HoldHeightRange;
            Position = source.Position;
            Rotation = source.Rotation;
            FieldOfView = source.FieldOfView;
            Focus = focus;
        }

        public HomeCameraShotKind Kind { get; }
        public Rect ActivationBounds { get; }
        public Rect HoldBounds { get; }
        public Vector2 ActivationHeightRange { get; }
        public Vector2 HoldHeightRange { get; }
        public Vector3 Position { get; }
        public Quaternion Rotation { get; }
        public Vector3 EulerAngles => Rotation.eulerAngles;
        public float FieldOfView { get; }

        /// <summary>How far this shot may turn to keep the hero framed.
        /// A shot without one never moves.</summary>
        public FixedCameraFocus Focus { get; }

        /// <summary>This shot, allowed to pan onto the hero. The apartment
        /// is wider than any one aim from its corner covers, so the main
        /// room carries a focus while the tight rooms keep their authored
        /// frame exactly.</summary>
        public HomeCameraShot WithFocus(FixedCameraFocus focus)
        {
            return new HomeCameraShot(this, focus);
        }

        public bool IsInActivationArea(Vector3 worldPosition)
        {
            return Contains(
                ActivationBounds,
                ActivationHeightRange,
                worldPosition);
        }

        public bool IsInHoldArea(Vector3 worldPosition)
        {
            return Contains(
                HoldBounds,
                HoldHeightRange,
                worldPosition);
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
            ValidateHeightRange(
                ActivationHeightRange,
                nameof(ActivationHeightRange));
            ValidateHeightRange(
                HoldHeightRange,
                nameof(HoldHeightRange));
            if (!Contains(HoldBounds, ActivationBounds) ||
                !Contains(HoldHeightRange, ActivationHeightRange) ||
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
            Vector2 heightRange,
            Vector3 worldPosition)
        {
            return worldPosition.x >= bounds.xMin &&
                   worldPosition.x <= bounds.xMax &&
                   worldPosition.y >= heightRange.x &&
                   worldPosition.y <= heightRange.y &&
                   worldPosition.z >= bounds.yMin &&
                   worldPosition.z <= bounds.yMax;
        }

        private static bool Contains(Vector2 outer, Vector2 inner)
        {
            return inner.x >= outer.x && inner.y <= outer.y;
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

        private static void ValidateHeightRange(
            Vector2 range,
            string parameterName)
        {
            if (!IsFinite(range.x) ||
                !IsFinite(range.y) ||
                range.y <= range.x)
            {
                throw new ArgumentOutOfRangeException(
                    parameterName,
                    "Home camera height ranges must be finite and positive.");
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
            selector = nextSelector;
            ActiveShot = initialShot;
            IsInitialized = true;
            ApplyShot(ActiveShot);
        }

        public bool ReapplyActiveShot()
        {
            if (!IsInitialized ||
                cameraFollow == null ||
                target == null ||
                selector == null)
            {
                return false;
            }

            RefreshSelection(true);
            return true;
        }

        private void OnEnable()
        {
            if (IsInitialized)
            {
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
                cameraFollow.ClearFixedPose();
            }
        }

        private void OnDestroy()
        {
            if (IsInitialized && cameraFollow != null)
            {
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
            if (shot.Focus.Enabled)
            {
                cameraFollow.SetFixedFocus(shot.Focus);
            }
        }
    }
}

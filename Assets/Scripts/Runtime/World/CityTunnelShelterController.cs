using System;
using UnityEngine;

namespace BarPromenade
{
    /// <summary>
    /// Exposes the physically open tunnel's shelter state to the shared
    /// weather owner and suppresses the exterior fog. The test uses the tunnel
    /// descriptor so the same entrance volume survives when the real destination
    /// ships.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class CityTunnelShelterController : MonoBehaviour
    {
        public const float ShelterEntryInset = 0.65f;
        public const float ShelterExitInset = 0.25f;
        public const float LateralMargin = 0.35f;

        private Transform player;
        private CityMountainTunnelDescriptor tunnel;
        private CityFogField fog;
        private bool sheltered;

        public bool IsInitialized { get; private set; }
        public bool IsSheltered => sheltered;

        public void Initialize(
            Transform playerTransform,
            CityMountainTunnelDescriptor tunnelDescriptor,
            CityFogField fogField)
        {
            if (IsInitialized)
            {
                throw new InvalidOperationException(
                    "The tunnel shelter controller is already initialized.");
            }

            player = playerTransform != null
                ? playerTransform
                : throw new ArgumentNullException(nameof(playerTransform));
            tunnel = tunnelDescriptor;
            fog = fogField;
            IsInitialized = true;
            Refresh(true);
        }

        internal static bool Contains(
            CityMountainTunnelDescriptor descriptor,
            Vector3 worldPosition,
            bool wasSheltered)
        {
            Vector3 axis = Flatten(descriptor.Axis);
            Vector3 right = Vector3.Cross(Vector3.up, axis).normalized;
            Vector3 offset = worldPosition - descriptor.PortalGroundCenter;
            float depth = Vector3.Dot(offset, axis);
            float lateral = Mathf.Abs(Vector3.Dot(offset, right));
            float requiredDepth = wasSheltered
                ? ShelterExitInset
                : ShelterEntryInset;
            return depth >= requiredDepth &&
                   depth <= descriptor.WalkableDepth + 1f &&
                   lateral <= descriptor.OpeningWidth * 0.5f +
                       LateralMargin;
        }

        private void Update()
        {
            Refresh(false);
        }

        private void Refresh(bool force)
        {
            if (!IsInitialized || player == null)
            {
                return;
            }

            bool next = Contains(tunnel, player.position, sheltered);
            if (!force && next == sheltered)
            {
                return;
            }

            sheltered = next;
            if (fog != null)
            {
                fog.SetSheltered(sheltered);
            }
        }

        private void OnDisable()
        {
            RestoreExteriorPresentation();
        }

        private void OnDestroy()
        {
            RestoreExteriorPresentation();
            IsInitialized = false;
        }

        private void RestoreExteriorPresentation()
        {
            if (!sheltered)
            {
                return;
            }

            sheltered = false;
            if (fog != null)
            {
                fog.SetSheltered(false);
            }
        }

        private static Vector3 Flatten(Vector3 direction)
        {
            direction.y = 0f;
            return direction.sqrMagnitude > 0.0001f
                ? direction.normalized
                : Vector3.back;
        }
    }
}

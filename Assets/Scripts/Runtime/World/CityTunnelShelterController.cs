using System;
using UnityEngine;

namespace BarPromenade
{
    /// <summary>
    /// Exposes the physically open tunnel's shelter state to the shared
    /// weather owner, while directly suppressing the exterior-only fog and
    /// mountain shell. The test is descriptor-driven rather than a scene
    /// trigger so the same entrance volume survives when the real destination
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
        private CityMountainBackdropWorldResult backdrop;
        private bool sheltered;

        public bool IsInitialized { get; private set; }
        public bool IsSheltered => sheltered;

        public void Initialize(
            Transform playerTransform,
            CityMountainTunnelDescriptor tunnelDescriptor,
            CityFogField fogField,
            CityMountainBackdropWorldResult mountainBackdrop)
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
            backdrop = mountainBackdrop;
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

            SetBackdropVisible(!sheltered);
        }

        private void SetBackdropVisible(bool visible)
        {
            if (backdrop?.RidgeRenderers == null)
            {
                return;
            }

            for (int index = 0;
                 index < backdrop.RidgeRenderers.Count;
                 index++)
            {
                Renderer renderer = backdrop.RidgeRenderers[index];
                if (renderer != null)
                {
                    renderer.enabled = visible;
                }
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

            SetBackdropVisible(true);
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

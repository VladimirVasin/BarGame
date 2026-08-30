using System;
using UnityEngine;

namespace BarPromenade
{
    /// <summary>
    /// A silent, colliderless resident posed at one authored courtyard dock.
    /// The body and its Idle/Sit loops come from the ordinary Resources
    /// pedestrian catalog; this component only keeps that manual graph
    /// advancing while the resident remains outside the roaming population.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class CityCourtyardResidentPresentation : MonoBehaviour
    {
        public const float MaximumStepSeconds = 0.1f;

        private CityPedestrianPresentation pedestrian;

        public bool IsInitialized { get; private set; }
        public CityCourtyardResidentDescriptor Descriptor { get; private set; }
        public CityPedestrianPresentation Pedestrian => pedestrian;

        public void Initialize(
            CityPedestrianAssetRegistry registry,
            CityPedestrianArchetype archetype,
            CityCourtyardResidentDescriptor descriptor)
        {
            if (IsInitialized)
            {
                throw new InvalidOperationException(
                    "The courtyard resident presentation is already " +
                    "initialized.");
            }

            if (registry == null)
            {
                throw new ArgumentNullException(nameof(registry));
            }

            if (archetype == null)
            {
                throw new ArgumentNullException(nameof(archetype));
            }

            if (!string.Equals(
                    registry.DesignId,
                    descriptor.DesignId,
                    StringComparison.Ordinal) ||
                !string.Equals(
                    archetype.DesignId,
                    descriptor.DesignId,
                    StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    "The courtyard resident descriptor, archetype and " +
                    "prefab must name the same generic design.");
            }

            if (descriptor.IsSeated && archetype.SeatedRide == null)
            {
                throw new InvalidOperationException(
                    $"Generic design '{archetype.DesignId}' has no seated " +
                    "ride declaration.");
            }

            Descriptor = descriptor;
            transform.SetPositionAndRotation(
                descriptor.Position,
                Quaternion.LookRotation(descriptor.Facing, Vector3.up));
            registry.ApplyPaletteVariant(descriptor.PaletteVariant);

            pedestrian = registry.GetComponent<CityPedestrianPresentation>();
            if (pedestrian == null)
            {
                pedestrian = registry.gameObject.AddComponent<
                    CityPedestrianPresentation>();
            }

            pedestrian.Initialize(registry);
            pedestrian.ConfigureCycle(
                Mathf.Lerp(
                    archetype.MinimumAnimationSpeed,
                    archetype.MaximumAnimationSpeed,
                    0.5f),
                descriptor.AnimationPhase01);

            if (descriptor.IsSeated)
            {
                Transform seat = new GameObject("Seat Pose Anchor").transform;
                seat.SetParent(transform, true);
                seat.SetPositionAndRotation(
                    descriptor.SeatAnchorPosition,
                    transform.rotation);
                if (!pedestrian.TrySeat(seat, archetype.SeatedRide))
                {
                    DestroyObject(seat.gameObject);
                    pedestrian.Shutdown();
                    pedestrian = null;
                    throw new InvalidOperationException(
                        $"Generic design '{archetype.DesignId}' could not " +
                        "enter its declared seated pose.");
                }
            }

            // Apply the selected loop and stable phase before the first
            // rendered frame. Advance is bounded in LateUpdate afterwards.
            pedestrian.Advance(
                descriptor.IsSeated
                    ? descriptor.AnimationPhase01 * 0.83f
                    : 0f,
                false,
                true);
            IsInitialized = true;
        }

        public void Shutdown()
        {
            if (!IsInitialized && pedestrian == null)
            {
                return;
            }

            if (pedestrian != null)
            {
                pedestrian.Shutdown();
            }

            pedestrian = null;
            IsInitialized = false;
        }

        private void LateUpdate()
        {
            if (!IsInitialized || pedestrian == null)
            {
                return;
            }

            pedestrian.Advance(
                Mathf.Min(Time.deltaTime, MaximumStepSeconds),
                false,
                true);
        }

        private void OnDestroy()
        {
            Shutdown();
        }

        private static void DestroyObject(GameObject target)
        {
            if (target == null)
            {
                return;
            }

            if (Application.isPlaying)
            {
                UnityEngine.Object.Destroy(target);
            }
            else
            {
                UnityEngine.Object.DestroyImmediate(target);
            }
        }
    }
}

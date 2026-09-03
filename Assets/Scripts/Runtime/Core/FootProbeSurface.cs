using System;
using UnityEngine;

namespace BarPromenade
{
    /// <summary>
    /// Surfaces that exist for rays only: the visible treads of a stair.
    ///
    /// Every flight in the game walks its <c>CharacterController</c> up one
    /// continuous hidden ramp — the stairwell, the city's exterior stairs —
    /// so the visible treads are render-only boxes and the controller never
    /// catches on a nosing. That is right for movement and wrong for feet:
    /// a probe cast under a boot finds the ramp and the sole hangs above or
    /// sinks below the tread the player sees. So each tread also carries a
    /// collider on this layer, which the physics matrix hides from every
    /// walking body (the hero, the ragdoll, pedestrians, the bus) while
    /// <see cref="Physics.DefaultRaycastLayers"/> keeps it visible to the
    /// per-foot probes and the contact shadow.
    /// </summary>
    public static class FootProbeSurface
    {
        public const string LayerName = "FootProbe";
        public const int LayerIndex = 10;
        private const int DefaultLayerIndex = 0;

        private static bool policyApplied;

        /// <summary>The layers a foot probe casts against.</summary>
        public static int ProbeMask => Physics.DefaultRaycastLayers;

        /// <summary>
        /// Hides the layer from every body that walks, idempotently. Called
        /// by the world builders that create tread colliders; the project's
        /// pedestrian and bus factories follow the same pattern.
        /// </summary>
        public static void EnsureRuntimePolicy()
        {
            int configuredLayer = LayerMask.NameToLayer(LayerName);
            if (configuredLayer != LayerIndex)
            {
                throw new InvalidOperationException(
                    $"The {LayerName} layer must occupy layer {LayerIndex}.");
            }

            if (policyApplied)
            {
                return;
            }

            Physics.IgnoreLayerCollision(
                DefaultLayerIndex,
                LayerIndex,
                true);
            Physics.IgnoreLayerCollision(
                CityPedestrianCollision.LayerIndex,
                LayerIndex,
                true);
            Physics.IgnoreLayerCollision(
                CityBusCollision.LayerIndex,
                LayerIndex,
                true);
            Physics.IgnoreLayerCollision(
                LayerIndex,
                LayerIndex,
                true);
            policyApplied = true;
        }

        /// <summary>
        /// Gives a render-only primitive box a collider only rays can see.
        /// The box is a runtime primitive (a unit cube scaled by its
        /// transform), so the default <see cref="BoxCollider"/> fits it
        /// exactly; this is never used on an imported model part.
        /// </summary>
        /// <summary>Name of the child that carries a tread's probe collider.</summary>
        public const string ProbeChildName = "Foot Probe Surface";

        public static BoxCollider AddTreadCollider(GameObject tread)
        {
            if (tread == null)
            {
                throw new ArgumentNullException(nameof(tread));
            }

            EnsureRuntimePolicy();

            // The collider lives on a CHILD on the probe layer: the visible
            // tread keeps its own layer so every camera and light mask in
            // the game still draws it. The child inherits the tread's
            // transform, so a unit box collider on it is the tread's box.
            Transform existing = tread.transform.Find(ProbeChildName);
            GameObject child;
            if (existing != null)
            {
                child = existing.gameObject;
            }
            else
            {
                child = new GameObject(ProbeChildName);
                child.transform.SetParent(tread.transform, false);
                child.transform.localPosition = Vector3.zero;
                child.transform.localRotation = Quaternion.identity;
                child.transform.localScale = Vector3.one;
            }

            child.layer = LayerIndex;
            BoxCollider collider = child.GetComponent<BoxCollider>();
            if (collider == null)
            {
                collider = child.AddComponent<BoxCollider>();
            }

            // A trigger as well as a hidden layer: every cast in the
            // project that looks for obstacles passes
            // QueryTriggerInteraction.Ignore, so an interaction approach or
            // a clearance sweep on the stairs never sees a tread, while the
            // foot probes ask for triggers and accept this layer's.
            collider.center = Vector3.zero;
            collider.size = Vector3.one;
            collider.isTrigger = true;
            collider.enabled = true;
            return collider;
        }

        /// <summary>Whether a collider is a tread's probe surface.</summary>
        public static bool IsProbeSurface(Collider collider)
        {
            return collider != null &&
                   collider.isTrigger &&
                   collider.gameObject.layer == LayerIndex;
        }

        /// <summary>Test seam: forget that the policy was applied.</summary>
        internal static void ResetPolicyForTests()
        {
            policyApplied = false;
        }
    }
}

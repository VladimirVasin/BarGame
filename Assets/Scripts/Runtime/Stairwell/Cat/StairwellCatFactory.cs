using System;
using UnityEngine;

namespace BarPromenade
{
    /// <summary>
    /// Instantiates the one authored stairwell cat under the actor's
    /// GameObject. The prefab is validated passive - no collider,
    /// light, audio, physics or camera anywhere in it; the trigger,
    /// the interaction and the grin controller are components the
    /// stairwell root adds beside it, so the art stays art.
    /// </summary>
    public static class StairwellCatFactory
    {
        public const string ModelInstanceName = "Cat Model";

        public static StairwellCatRigAnchors CreateVisual(
            Transform parent)
        {
            return CreateVisual(parent, StairwellCatProvider.Load());
        }

        public static StairwellCatRigAnchors CreateVisual(
            Transform parent,
            StairwellCatProvider provider)
        {
            if (parent == null)
            {
                throw new ArgumentNullException(nameof(parent));
            }

            if (provider == null || provider.CatPrefab == null)
            {
                // Unlike the ambient city extras, the cat carries a
                // quest: a missing provider is a broken build, not a
                // quieter city.
                throw new InvalidOperationException(
                    "The stairwell cat provider or its prefab is " +
                    "missing; run the Stairwell Cat 3D asset build.");
            }

            GameObject instance = UnityEngine.Object.Instantiate(
                provider.CatPrefab,
                parent);
            instance.name = ModelInstanceName;
            instance.transform.localPosition = Vector3.zero;
            // The imported model faces prefab +Z; the cat must face
            // the landing (local -Z), back to the MiddleFlight shot.
            instance.transform.localRotation =
                Quaternion.Euler(0f, 180f, 0f);
            instance.transform.localScale = Vector3.one;

            var anchors = instance
                .GetComponentInChildren<StairwellCatRigAnchors>(true);
            if (anchors == null || !anchors.IsBound)
            {
                UnityEngine.Object.Destroy(instance);
                throw new InvalidOperationException(
                    "The stairwell cat prefab requires fully bound " +
                    nameof(StairwellCatRigAnchors) + ".");
            }

            ValidatePassivePresentation(instance);

            GameLog.Info(
                "stairwell",
                "stairwell_cat_spawned",
                GameLog.Field("design_id", anchors.DesignId),
                GameLog.Field(
                    "triangles",
                    anchors.SourceTriangleCount));
            return anchors;
        }

        /// <summary>
        /// The staged prefab is authored passive on purpose.
        /// Instantiating it must not smuggle in physics, audio,
        /// light or interaction - the trigger lives on the actor.
        /// </summary>
        private static void ValidatePassivePresentation(
            GameObject instance)
        {
            if (instance.GetComponentInChildren<Collider>(true) != null ||
                instance.GetComponentInChildren<Rigidbody>(true) != null ||
                instance.GetComponentInChildren<AudioSource>(true) != null ||
                instance.GetComponentInChildren<Light>(true) != null ||
                instance.GetComponentInChildren<Camera>(true) != null)
            {
                throw new InvalidOperationException(
                    "The stairwell cat presentation must stay passive.");
            }
        }
    }
}

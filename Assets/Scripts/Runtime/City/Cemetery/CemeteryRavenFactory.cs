using System;
using UnityEngine;

namespace BarPromenade
{
    /// <summary>
    /// Instantiates one authored cemetery raven under an actor's
    /// GameObject. The prefab is validated passive — no collider,
    /// light, audio, physics or camera anywhere in it; the voice is a
    /// component the raven controller adds beside it at runtime, so
    /// the art stays art.
    ///
    /// Unlike the stairwell cat, a missing provider degrades instead
    /// of throwing: the cat carries a quest, but these are background
    /// birds, and a city without them must still be a city — the
    /// mourner's own rule.
    /// </summary>
    public static class CemeteryRavenFactory
    {
        public const string ModelInstanceName = "Raven Model";

        public static CemeteryRavenRigAnchors CreateVisual(
            Transform parent)
        {
            return CreateVisual(parent, CemeteryRavenProvider.Load());
        }

        public static CemeteryRavenRigAnchors CreateVisual(
            Transform parent,
            CemeteryRavenProvider provider)
        {
            if (parent == null)
            {
                throw new ArgumentNullException(nameof(parent));
            }

            if (provider == null || provider.RavenPrefab == null)
            {
                GameLog.Warning(
                    "city",
                    "cemetery_raven_provider_missing");
                return null;
            }

            GameObject instance = UnityEngine.Object.Instantiate(
                provider.RavenPrefab,
                parent);
            instance.name = ModelInstanceName;
            instance.transform.localPosition = Vector3.zero;
            // The imported model faces prefab +Z; the half turn makes
            // the HOST's +Z the bird's facing, so perch yaws are plain
            // compass rotations on the host root.
            instance.transform.localRotation =
                Quaternion.Euler(0f, 180f, 0f);
            instance.transform.localScale = Vector3.one;

            var anchors = instance
                .GetComponentInChildren<CemeteryRavenRigAnchors>(true);
            if (anchors == null || !anchors.IsBound)
            {
                UnityEngine.Object.Destroy(instance);
                throw new InvalidOperationException(
                    "The cemetery raven prefab requires fully bound " +
                    nameof(CemeteryRavenRigAnchors) + ".");
            }

            ValidatePassivePresentation(instance);

            GameLog.Info(
                "city",
                "cemetery_raven_spawned",
                GameLog.Field("design_id", anchors.DesignId),
                GameLog.Field(
                    "triangles",
                    anchors.SourceTriangleCount));
            return anchors;
        }

        /// <summary>
        /// The staged prefab is authored passive on purpose.
        /// Instantiating it must not smuggle in physics, audio,
        /// light or interaction — the voice lives on the actor.
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
                    "The cemetery raven presentation must stay passive.");
            }
        }
    }
}

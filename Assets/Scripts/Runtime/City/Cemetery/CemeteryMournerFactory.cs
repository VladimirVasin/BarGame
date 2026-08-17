using System;
using UnityEngine;

namespace BarPromenade
{
    /// <summary>
    /// Instantiates one cemetery mourner for one visit. Unlike the
    /// always-present babushkas and weighbridge attendants, this
    /// staged NPC is scripted and transient: the mourner controller
    /// calls this when the hero nears the cemetery and destroys the
    /// instance when the rite completes. The prefab contract is the
    /// same staged one — passive, colliderless, magnet-focused.
    /// </summary>
    public static class CemeteryMournerFactory
    {
        public const string RuntimeInstanceName = "Cemetery Mourner";

        public static CemeteryMournerPresentation Create(
            Transform parent,
            Vector3 position,
            Vector3 facing,
            int paletteVariant,
            CemeteryMournerProvider provider)
        {
            if (parent == null)
            {
                throw new ArgumentNullException(nameof(parent));
            }

            if (provider == null || provider.StagedPrefab == null)
            {
                GameLog.Warning(
                    "city",
                    "cemetery_mourner_provider_missing");
                return null;
            }

            GameObject instance = UnityEngine.Object.Instantiate(
                provider.StagedPrefab,
                parent);
            instance.name = RuntimeInstanceName;
            Vector3 flatFacing = new Vector3(facing.x, 0f, facing.z);
            if (flatFacing.sqrMagnitude < 0.0001f)
            {
                flatFacing = Vector3.forward;
            }

            instance.transform.SetPositionAndRotation(
                position,
                Quaternion.LookRotation(
                    flatFacing.normalized,
                    Vector3.up));

            var registry = instance
                .GetComponentInChildren<CityPedestrianAssetRegistry>(
                    true);
            if (registry == null)
            {
                UnityEngine.Object.Destroy(instance);
                throw new InvalidOperationException(
                    "The staged mourner prefab requires a " +
                    nameof(CityPedestrianAssetRegistry) + ".");
            }

            ValidatePassivePresentation(instance);

            var presentation = instance
                .AddComponent<CemeteryMournerPresentation>();
            presentation.Initialize(registry, paletteVariant);

            // The mourner is colliderless like every staged NPC, so
            // the hero's attention finds her through a magnet at her
            // bowed head height.
            var magnet = instance.AddComponent<PlayerAttentionMagnet>();
            magnet.FocusHeight = 1.55f;

            GameLog.Info(
                "city",
                "cemetery_mourner_spawned",
                GameLog.Field("position_x", position.x),
                GameLog.Field("position_z", position.z),
                GameLog.Field("palette_variant", paletteVariant));
            return presentation;
        }

        /// <summary>
        /// The staged prefab is authored passive on purpose.
        /// Instantiating it must not smuggle in physics, audio, light
        /// or interaction.
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
                    "The staged mourner presentation must stay passive.");
            }
        }
    }
}

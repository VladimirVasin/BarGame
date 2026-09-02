using System;
using System.Collections.Generic;
using UnityEngine;

namespace BarPromenade
{
    /// <summary>
    /// Instantiates the one ordinary cashier behind the checkout. No pool or
    /// director: one clerk, one register. The retained Watcher prefab can use
    /// this same path when explicitly supplied by tooling or a test.
    /// </summary>
    public static class SupermarketCashierFactory
    {
        public const string RuntimeRootName =
            "Supermarket Cashier Runtime";
        public const float FallbackAttentionFocusHeight = 1.58f;

        public static SupermarketCashierActor Create(
            Transform parent,
            SupermarketCashierPlan plan,
            Transform playerBody,
            Transform playerHead,
            Bounds headLimits,
            IReadOnlyList<Bounds> obstacles)
        {
            return Create(
                parent,
                plan,
                playerBody,
                playerHead,
                headLimits,
                obstacles,
                SupermarketCashierProvider.Load());
        }

        public static SupermarketCashierActor Create(
            Transform parent,
            SupermarketCashierPlan plan,
            Transform playerBody,
            Transform playerHead,
            Bounds headLimits,
            IReadOnlyList<Bounds> obstacles,
            SupermarketCashierProvider provider)
        {
            if (parent == null)
            {
                throw new ArgumentNullException(nameof(parent));
            }

            if (provider == null || provider.CashierPrefab == null)
            {
                GameLog.Warning(
                    "supermarket",
                    "cashier_provider_missing");
                return null;
            }

            Transform root = new GameObject(RuntimeRootName).transform;
            root.SetParent(parent, false);
            root.localPosition = plan.Position;
            root.localRotation = Quaternion.Euler(
                0f,
                plan.YawDegrees,
                0f);

            GameObject instance = UnityEngine.Object.Instantiate(
                provider.CashierPrefab,
                root);
            instance.name = provider.CashierPrefab.name;
            instance.transform.localPosition = Vector3.zero;
            instance.transform.localRotation = Quaternion.identity;

            var registry = instance.GetComponentInChildren<
                SupermarketCashierAssetRegistry>(true);
            if (registry == null)
            {
                UnityEngine.Object.Destroy(root.gameObject);
                throw new InvalidOperationException(
                    "The cashier prefab requires a " +
                    nameof(SupermarketCashierAssetRegistry) + ".");
            }

            ValidatePassivePresentation(instance);

            var presentation = instance
                .AddComponent<SupermarketCashierPresentation>();
            presentation.Initialize(registry);
            ConfigurePoseContext(
                presentation,
                root,
                headLimits,
                obstacles);

            var actor = instance.AddComponent<SupermarketCashierActor>();
            actor.Initialize(presentation, playerBody, playerHead);

            // The clerk is colliderless, so the hero's attention notices him
            // through a magnet placed at the authored head height. This is
            // data-derived because the active human and retained Watcher have
            // deliberately different silhouettes.
            var magnet = instance.AddComponent<PlayerAttentionMagnet>();
            Vector3 localHead = instance.transform.InverseTransformPoint(
                registry.Head.position);
            magnet.FocusHeight = localHead.y > 0.1f
                ? localHead.y
                : FallbackAttentionFocusHeight;
            ReportSpawn(instance, plan);
            return actor;
        }

        /// <summary>
        /// World-space anchors for the dead-still palms on the counter
        /// edge, plus the room-wide box the pursuing head roams and the
        /// solid fixtures its neck must arc over.
        /// </summary>
        private static void ConfigurePoseContext(
            SupermarketCashierPresentation presentation,
            Transform root,
            Bounds headLimits,
            IReadOnlyList<Bounds> obstacles)
        {
            Vector3 forward = root.forward;
            Vector3 right = root.right;
            Vector3 counterEdge = root.position + forward * 0.30f;
            presentation.ConfigurePoseContext(
                counterEdge + right * 0.22f + Vector3.up * 1.07f,
                counterEdge - right * 0.22f + Vector3.up * 1.07f,
                headLimits,
                obstacles);
        }

        private static void ReportSpawn(
            GameObject instance,
            SupermarketCashierPlan plan)
        {
            Renderer[] renderers =
                instance.GetComponentsInChildren<Renderer>(true);
            var bounds = new Bounds(
                instance.transform.position,
                Vector3.zero);
            int visible = 0;
            for (int index = 0; index < renderers.Length; index++)
            {
                if (!renderers[index].enabled)
                {
                    continue;
                }

                if (visible == 0)
                {
                    bounds = renderers[index].bounds;
                }
                else
                {
                    bounds.Encapsulate(renderers[index].bounds);
                }

                visible++;
            }

            GameLog.Info(
                "supermarket",
                "cashier_spawned",
                GameLog.Field("cashier_id", plan.Id),
                GameLog.Field("renderer_count", renderers.Length),
                GameLog.Field("visible_renderer_count", visible),
                GameLog.Field(
                    "position_x",
                    instance.transform.position.x),
                GameLog.Field(
                    "position_y",
                    instance.transform.position.y),
                GameLog.Field(
                    "position_z",
                    instance.transform.position.z),
                GameLog.Field("bounds_size_x", bounds.size.x),
                GameLog.Field("bounds_size_y", bounds.size.y),
                GameLog.Field("bounds_size_z", bounds.size.z));
        }

        /// <summary>
        /// The cashier prefab is authored passive on purpose.
        /// Instantiating it must not smuggle in physics, audio, light
        /// or interaction; the talk stub lives on its own trigger.
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
                    "The cashier presentation must stay passive.");
            }
        }
    }
}

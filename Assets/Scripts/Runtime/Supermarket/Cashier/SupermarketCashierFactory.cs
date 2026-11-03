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
            IReadOnlyList<Bounds> obstacles,
            SupermarketInteriorAssetRegistry counterAssets = null)
        {
            return Create(
                parent,
                plan,
                playerBody,
                playerHead,
                headLimits,
                obstacles,
                SupermarketCashierProvider.Load(),
                counterAssets);
        }

        public static SupermarketCashierActor Create(
            Transform parent,
            SupermarketCashierPlan plan,
            Transform playerBody,
            Transform playerHead,
            Bounds headLimits,
            IReadOnlyList<Bounds> obstacles,
            SupermarketCashierProvider provider,
            SupermarketInteriorAssetRegistry counterAssets = null)
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
                registry,
                root,
                headLimits,
                obstacles,
                counterAssets);

            var actor = instance.AddComponent<SupermarketCashierActor>();
            actor.Initialize(presentation, playerBody, playerHead);
            NpcNameplateTarget.Attach(
                instance, "npc.name.cashier", registry.Head, "cashier");

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
            SupermarketCashierAssetRegistry registry,
            Transform root,
            Bounds headLimits,
            IReadOnlyList<Bounds> obstacles,
            SupermarketInteriorAssetRegistry counterAssets)
        {
            Vector3 forward = root.forward;
            Vector3 right = root.right;
            Vector3 counterEdge = root.position + forward * 0.30f;
            // Imported anatomical sides are measured from the shared rig, not
            // inferred from the presentation root's right axis.
            float leftSide = Mathf.Sign(Vector3.Dot(registry.LeftUpperArm.position - root.position, right));
            float rightSide = Mathf.Sign(Vector3.Dot(registry.RightUpperArm.position - root.position, right));
            Vector3 leftContact = counterEdge + right * (leftSide * 0.22f) + Vector3.up * 1.07f;
            Vector3 rightContact = counterEdge + right * (rightSide * 0.22f) + Vector3.up * 1.07f;
            if (counterAssets != null)
            {
                Vector3 a = counterAssets.GetAnchor("checkout_hand_a").position;
                Vector3 b = counterAssets.GetAnchor("checkout_hand_b").position;
                // Assign the two authored contacts to the imported anatomical
                // sides, which need not agree with the FBX root's right axis.
                bool aIsLeft = Vector3.Dot(a - b, right) * leftSide > 0f;
                leftContact = aIsLeft ? a : b;
                rightContact = aIsLeft ? b : a;
            }
            presentation.ConfigurePoseContext(
                leftContact,
                rightContact,
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

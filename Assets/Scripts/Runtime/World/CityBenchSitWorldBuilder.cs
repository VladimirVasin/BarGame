using System;
using System.Collections.Generic;
using UnityEngine;

namespace BarPromenade
{
    /// <summary>
    /// Installs the sit offer on every sittable city bench: one
    /// trigger volume per plank and the shared animated-interaction
    /// controller on the player, acquired the same way the bus ride
    /// acquires it.
    /// </summary>
    public static class CityBenchSitWorldBuilder
    {
        public const string RootName = "City Bench Sit Interactions";

        public static IReadOnlyList<CityBenchSitInteraction> Build(
            Transform parent,
            IReadOnlyList<CityBenchSitPlan> plans,
            PlayerRuntime player,
            Camera camera)
        {
            if (parent == null)
            {
                throw new ArgumentNullException(nameof(parent));
            }

            if (plans == null)
            {
                throw new ArgumentNullException(nameof(plans));
            }

            var interactions = new List<CityBenchSitInteraction>(
                plans.Count);
            if (plans.Count == 0 || player.GameObject == null)
            {
                GameLog.Warning("city", "bench_sit_none_installed");
                return interactions;
            }

            var controller = player.GameObject.GetComponent<
                PlayerAnimatedInteractionController>();
            if (controller == null)
            {
                controller = player.GameObject.AddComponent<
                    PlayerAnimatedInteractionController>();
            }

            if (!controller.IsInitialized)
            {
                controller.Initialize(player, camera);
            }

            Transform root = new GameObject(RootName).transform;
            root.SetParent(parent, false);
            for (int index = 0; index < plans.Count; index++)
            {
                CityBenchSitPlan plan = plans[index];
                if (!plan.IsPresent)
                {
                    continue;
                }

                var trigger = new GameObject(
                    $"Bench Sit {plan.Id}");
                trigger.transform.SetParent(root, false);
                trigger.transform.SetPositionAndRotation(
                    plan.TriggerCenter,
                    plan.TriggerRotation);
                BoxCollider collider =
                    trigger.AddComponent<BoxCollider>();
                collider.isTrigger = true;
                collider.size = plan.TriggerSize;
                var bench = trigger.AddComponent<
                    CityBenchSitInteraction>();
                bench.Initialize(player, controller, plan);
                interactions.Add(bench);
                GameLog.Info(
                    "city",
                    "bench_sit_installed",
                    GameLog.Field("bench_id", plan.Id),
                    GameLog.Field(
                        "seat_x",
                        plan.InteractionPosition.x),
                    GameLog.Field(
                        "seat_y",
                        plan.InteractionPosition.y),
                    GameLog.Field(
                        "seat_z",
                        plan.InteractionPosition.z));
            }

            return interactions;
        }
    }
}

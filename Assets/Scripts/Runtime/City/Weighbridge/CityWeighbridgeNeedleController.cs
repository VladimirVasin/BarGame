using System;
using System.Collections.Generic;
using UnityEngine;

namespace BarPromenade
{
    /// <summary>
    /// Makes the weighbridge's indicator answer weight: while the
    /// weighed worker holds his pause at the deck centre, or the hero
    /// stands on the walkable deck, the needle eases off its authored
    /// rest mark and settles back once the deck is empty. Owned by
    /// the City root, never by the staged NPC prefab — the attendants
    /// stay passive and are only polled. The carpet-registry pattern
    /// carries the needle transform over from the world builder.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class CityWeighbridgeNeedleController : MonoBehaviour
    {
        public const string RuntimeObjectName =
            "Weighbridge Needle Controller";

        /// <summary>Full-load sweep across the indicator face, rolled
        /// about the needle's local Z so the authored yaw is kept.</summary>
        public const float MaxDeflectionDegrees = 34f;

        /// <summary>The mechanism takes up load faster than it lets
        /// it go — a heavy dial, not a digital meter.</summary>
        public const float AttackSeconds = 0.45f;
        public const float ReleaseSeconds = 0.90f;

        /// <summary>Vertical band around the deck top inside which a
        /// standing hero counts as weight on the platform.</summary>
        public const float FootBandBelowDeckTop = 0.35f;
        public const float FootBandAboveDeckTop = 0.80f;

        private CityWeighbridgeDeckRect deck;
        private Transform needle;
        private Transform player;
        private WeighbridgeAttendantPresentation worker;
        private Quaternion restRotation;
        private float deflection01;

        public float Deflection01 => deflection01;

        public static CityWeighbridgeNeedleController Create(
            Transform parent,
            CityLayout layout,
            Transform player,
            IReadOnlyList<WeighbridgeAttendantPresentation> attendants)
        {
            if (parent == null)
            {
                throw new ArgumentNullException(nameof(parent));
            }

            if (layout == null)
            {
                throw new ArgumentNullException(nameof(layout));
            }

            if (player == null)
            {
                throw new ArgumentNullException(nameof(player));
            }

            CityWeighbridgeDeckRect deck = default;
            bool found = false;
            for (int index = 0;
                 index < layout.DistrictPointsOfInterest.Count;
                 index++)
            {
                if (CityDistrictPointOfInterestWorldBuilder
                        .TryDescribeWeighbridgeDeck(
                            layout.DistrictPointsOfInterest[index],
                            out deck))
                {
                    found = true;
                    break;
                }
            }

            if (!found)
            {
                return null;
            }

            Transform needle = CityWeighbridgeIndicatorRegistry.Find(
                CityWeighbridgeIndicatorRegistry.NeedleId);
            if (needle == null)
            {
                GameLog.Warning("city", "weighbridge_needle_missing");
                return null;
            }

            WeighbridgeAttendantPresentation worker = null;
            if (attendants != null)
            {
                for (int index = 0; index < attendants.Count; index++)
                {
                    WeighbridgeAttendantPresentation attendant =
                        attendants[index];
                    if (attendant != null &&
                        attendant.Role ==
                        WeighbridgeAttendantRole.WeighedWorker)
                    {
                        worker = attendant;
                        break;
                    }
                }
            }

            var controller = new GameObject(RuntimeObjectName)
                .AddComponent<CityWeighbridgeNeedleController>();
            controller.transform.SetParent(parent, false);
            controller.deck = deck;
            controller.needle = needle;
            controller.player = player;
            controller.worker = worker;
            // The authored rest pose (the 28-degree yaw) is captured,
            // never hard-coded, so the needle always settles back to
            // exactly the mark it was drawn at.
            controller.restRotation = needle.localRotation;
            return controller;
        }

        /// <summary>True when the point stands on the deck rectangle
        /// within the standing-foot band. Pure for tests.</summary>
        public static bool IsPointOnDeck(
            CityWeighbridgeDeckRect deck,
            Vector3 point)
        {
            Vector3 local = Quaternion.Inverse(deck.Yaw) *
                (point - deck.Center);
            return Mathf.Abs(local.x) <= deck.HalfWidth &&
                   Mathf.Abs(local.z) <= deck.HalfLength &&
                   local.y >= -FootBandBelowDeckTop &&
                   local.y <= FootBandAboveDeckTop;
        }

        /// <summary>One exponential-ease step of the 0..1 deflection
        /// toward its target, with the slower release. Pure for
        /// tests: explicit delta time, no clock.</summary>
        public static float AdvanceDeflection(
            float current,
            float target,
            float deltaTime)
        {
            float tau = target > current
                ? AttackSeconds
                : ReleaseSeconds;
            float blend = 1f - Mathf.Exp(-deltaTime / tau);
            return Mathf.Clamp01(
                current + (target - current) * blend);
        }

        private void Update()
        {
            if (needle == null || player == null)
            {
                return;
            }

            bool loaded = IsPointOnDeck(deck, player.position) ||
                          (worker != null && worker.IsWeighingNow);
            deflection01 = AdvanceDeflection(
                deflection01,
                loaded ? 1f : 0f,
                Time.deltaTime);
            needle.localRotation = restRotation *
                Quaternion.Euler(
                    0f,
                    0f,
                    -MaxDeflectionDegrees * deflection01);
        }
    }
}

using System;
using System.Collections.Generic;
using UnityEngine;

namespace BarPromenade
{
    /// <summary>
    /// One production 3D pedestrian standing in for a bar guest.
    /// </summary>
    public sealed class BarPatron
    {
        internal BarPatron(
            BarNpcAnchor anchor,
            CityPedestrianAssetRegistry registry,
            CityPedestrianPresentation presentation,
            bool isSeated)
        {
            Anchor = anchor;
            Registry = registry;
            Presentation = presentation;
            IsSeated = isSeated;
        }

        public BarNpcAnchor Anchor { get; }
        public CityPedestrianAssetRegistry Registry { get; }
        public CityPedestrianPresentation Presentation { get; }
        public bool IsSeated { get; }
    }

    /// <summary>
    /// Advances the guests' idle and seated loops. They go nowhere: a
    /// bar crowd is a tableau, not a simulation.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class BarPatronAnimator : MonoBehaviour
    {
        private IReadOnlyList<BarPatron> patrons =
            Array.Empty<BarPatron>();

        public void Initialize(IReadOnlyList<BarPatron> barPatrons)
        {
            patrons = barPatrons ??
                      throw new ArgumentNullException(
                          nameof(barPatrons));
        }

        private void Update()
        {
            float deltaTime = Time.deltaTime;
            for (int index = 0; index < patrons.Count; index++)
            {
                BarPatron patron = patrons[index];
                if (patron.Presentation != null)
                {
                    patron.Presentation.Advance(deltaTime, false);
                }
            }
        }
    }

    /// <summary>
    /// Replaces the retired sprite crowd: the same authored NPC anchors
    /// of the interior layout now seat and stand the production 3D
    /// pedestrian models around the bar. Bartender anchors stay empty
    /// until the dedicated 3D bartender pass.
    /// </summary>
    public static class BarPatronWorldBuilder
    {
        public const string RootName = "Bar Patrons";

        /// <summary>Booth and stool seat height under a guest.</summary>
        public const float SeatHeight = 0.46f;

        public static IReadOnlyList<BarPatron> Build(
            Transform parent,
            BarInteriorLayoutPlan layout)
        {
            if (parent == null)
            {
                throw new ArgumentNullException(nameof(parent));
            }

            if (layout == null)
            {
                throw new ArgumentNullException(nameof(layout));
            }

            var patrons = new List<BarPatron>(layout.NpcAnchors.Count);
            Transform root = new GameObject(RootName).transform;
            root.SetParent(parent, false);

            GameObject[] prefabs =
                CityPedestrianResources.LoadPooledPrefabs(
                    Mathf.Max(1, layout.NpcAnchors.Count));
            if (prefabs.Length == 0)
            {
                GameLog.Warning("bar", "patron_prefabs_missing");
                return patrons;
            }

            int patronIndex = 0;
            for (int index = 0; index < layout.NpcAnchors.Count; index++)
            {
                BarNpcAnchor anchor = layout.NpcAnchors[index];
                if (anchor.Role == BarNpcRole.Bartender)
                {
                    continue;
                }

                GameObject prefab =
                    prefabs[patronIndex % prefabs.Length];
                patronIndex++;
                if (!CityPedestrianResources.TryInstantiate(
                        prefab,
                        root,
                        out CityPedestrianAssetRegistry registry))
                {
                    continue;
                }

                registry.gameObject.name = $"Bar Patron {anchor.Id}";
                registry.transform.localPosition = anchor.Position;
                registry.transform.localRotation =
                    Quaternion.Euler(0f, anchor.YawDegrees, 0f);
                registry.ApplyPaletteVariant(anchor.VisualVariant);

                CityPedestrianPresentation presentation =
                    registry.GetComponent<CityPedestrianPresentation>();
                if (presentation == null)
                {
                    presentation = registry.gameObject.AddComponent<
                        CityPedestrianPresentation>();
                }

                presentation.Initialize(registry);
                presentation.SetMoving(false, true);

                bool seated = anchor.Role == BarNpcRole.SeatedPatron &&
                              TrySeat(root, anchor, registry, presentation);
                patrons.Add(new BarPatron(
                    anchor,
                    registry,
                    presentation,
                    seated));
            }

            var animator = root.gameObject.AddComponent<BarPatronAnimator>();
            animator.Initialize(patrons);
            GameLog.Info(
                "bar",
                "patrons_built",
                GameLog.Field("patron_count", patrons.Count),
                GameLog.Field(
                    "seated_count",
                    CountSeated(patrons)));
            return patrons;
        }

        private static bool TrySeat(
            Transform root,
            BarNpcAnchor anchor,
            CityPedestrianAssetRegistry registry,
            CityPedestrianPresentation presentation)
        {
            if (!CityPedestrianResources.TryGetArchetype(
                    registry.DesignId,
                    out CityPedestrianArchetype archetype) ||
                archetype.SeatedRide == null)
            {
                return false;
            }

            var seatAnchor = new GameObject(
                $"Bar Patron Seat {anchor.Id}");
            seatAnchor.transform.SetParent(root, false);
            seatAnchor.transform.localPosition =
                anchor.Position + (Vector3.up * SeatHeight);
            seatAnchor.transform.localRotation =
                Quaternion.Euler(0f, anchor.YawDegrees, 0f);
            return presentation.TrySeat(
                seatAnchor.transform,
                archetype.SeatedRide);
        }

        private static int CountSeated(IReadOnlyList<BarPatron> patrons)
        {
            int seated = 0;
            for (int index = 0; index < patrons.Count; index++)
            {
                if (patrons[index].IsSeated)
                {
                    seated++;
                }
            }

            return seated;
        }
    }
}

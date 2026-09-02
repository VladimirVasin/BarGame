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
            bool isSeated,
            BarPatronDrinkingArmPose drinking)
        {
            Anchor = anchor;
            Registry = registry;
            Presentation = presentation;
            IsSeated = isSeated;
            Drinking = drinking;
        }

        public BarNpcAnchor Anchor { get; }
        public CityPedestrianAssetRegistry Registry { get; }
        public CityPedestrianPresentation Presentation { get; }
        public bool IsSeated { get; }

        /// <summary>
        /// The guest's procedural drinking layer, or <c>null</c> for
        /// the deliberate empty-handed minority.
        /// </summary>
        public BarPatronDrinkingArmPose Drinking { get; }
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

        /// <summary>
        /// Shelf bottles are counter-scale showpieces; the same
        /// silhouette shrinks to a hand-sized prop in a guest's fist.
        /// </summary>
        public const float BottlePropScale = 0.42f;

        /// <summary>The fist wraps the bottle at this height share.</summary>
        public const float BottleGripHeightShare = 0.45f;

        /// <summary>Every Nth guest keeps their hands empty.</summary>
        public const int EmptyHandedEvery = 3;

        public const string BottleSocketName = "SOCKET_Bottle.R";
        public const string MouthSocketName = "SOCKET_Mouth";
        public const string RightUpperArmBoneName = "upper_arm.R";
        public const string RightForearmBoneName = "forearm.R";
        public const string HeadBoneName = "head";

        // A dim bar crowd drinks what the shelf actually sells: mostly
        // beer, the odd vodka and cognac. Water stays behind the bar.
        private static readonly DrinkId[] PatronDrinks =
        {
            DrinkId.LightBeer,
            DrinkId.DarkBeer,
            DrinkId.LightBeer,
            DrinkId.Vodka,
            DrinkId.DarkBeer,
            DrinkId.CognacVs,
            DrinkId.PepperVodka,
            DrinkId.LightBeer
        };

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

                // The bar mints its own pooled bodies rather than going
                // through CityPedestrianFactory, so the street's prop rule
                // has to be applied here too - miss it and the bar fills
                // with grandmothers holding carpet beaters.
                CityPedestrianHeldProps.ApplyRoamingRules(registry);

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
                BarPatronDrinkingArmPose drinking =
                    patronIndex % EmptyHandedEvery == 0
                        ? null
                        : TryAttachDrinking(
                            anchor,
                            registry,
                            patronIndex);
                patrons.Add(new BarPatron(
                    anchor,
                    registry,
                    presentation,
                    seated,
                    drinking));
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

        /// <summary>
        /// Puts one of the bar's own bottle designs into the guest's
        /// right hand and starts their seeded drinking cadence. A
        /// design without the canonical sockets simply drinks nothing.
        /// </summary>
        public static BarPatronDrinkingArmPose TryAttachDrinking(
            BarNpcAnchor anchor,
            CityPedestrianAssetRegistry registry,
            int patronIndex)
        {
            Transform socket = FindDeep(
                registry.transform,
                BottleSocketName);
            Transform mouth = FindDeep(
                registry.transform,
                MouthSocketName);
            Transform upperArm = FindDeep(
                registry.transform,
                RightUpperArmBoneName);
            Transform forearm = FindDeep(
                registry.transform,
                RightForearmBoneName);
            Transform headBone = FindDeep(
                registry.transform,
                HeadBoneName);
            if (socket == null ||
                mouth == null ||
                upperArm == null ||
                forearm == null ||
                headBone == null)
            {
                GameLog.Warning(
                    "bar",
                    "patron_drink_sockets_missing",
                    GameLog.Field("design", registry.DesignId));
                return null;
            }

            int seed = StableSeed(anchor.Id, patronIndex);
            BarDrinkPresentation presentation =
                BarDrinkPresentationCatalog.Get(
                    PatronDrinks[Mathf.Abs(seed) %
                                 PatronDrinks.Length]);

            var bottleObject = new GameObject(
                $"Patron Bottle {presentation.StableId}");
            Transform bottleRoot = bottleObject.transform;
            bottleRoot.SetParent(socket, false);
            bottleRoot.localScale = InverseScale(socket.lossyScale);

            // The socket bone's +Y runs from the palm toward the
            // ground at rest, so the rig flips it: the bottle stands
            // neck-up out of the fist, gripped at its body.
            var rigObject = new GameObject("Bottle Rig");
            Transform rig = rigObject.transform;
            rig.SetParent(bottleRoot, false);
            rig.localRotation = Quaternion.Euler(180f, 0f, 0f);
            rig.localScale = Vector3.one * BottlePropScale;
            float bottleHeight =
                BarDrinkServiceWorldBuilder.BuildBottleVisual(
                    rig,
                    presentation);
            rig.localPosition = Vector3.up *
                (bottleHeight *
                 BottlePropScale *
                 BottleGripHeightShare);

            var mouthAnchor = new GameObject("Bottle Mouth Anchor");
            mouthAnchor.transform.SetParent(rig, false);
            mouthAnchor.transform.localPosition =
                Vector3.up * bottleHeight;

            BarPatronDrinkingArmPose pose =
                registry.gameObject.AddComponent<
                    BarPatronDrinkingArmPose>();
            pose.Initialize(
                new BarPatronDrinkTimeline(seed),
                upperArm,
                forearm,
                headBone,
                mouth,
                bottleRoot,
                mouthAnchor.transform);
            return pose;
        }

        private static Transform FindDeep(
            Transform root,
            string childName)
        {
            Transform[] children =
                root.GetComponentsInChildren<Transform>(true);
            for (int index = 0; index < children.Length; index++)
            {
                if (string.Equals(
                        children[index].name,
                        childName,
                        StringComparison.Ordinal))
                {
                    return children[index];
                }
            }

            return null;
        }

        private static int StableSeed(string anchorId, int patronIndex)
        {
            unchecked
            {
                int hash = 17;
                string id = anchorId ?? string.Empty;
                for (int index = 0; index < id.Length; index++)
                {
                    hash = (hash * 31) + id[index];
                }

                return (hash * 397) ^ patronIndex;
            }
        }

        private static Vector3 InverseScale(Vector3 scale)
        {
            const float minimumAxis = 0.0001f;

            // Unity's Blender/FBX bone hierarchy carries a 100x
            // transform scale even though the skinned character
            // renders in metres; cancel it at the prop root.
            return new Vector3(
                Mathf.Abs(scale.x) < minimumAxis ? 1f : 1f / scale.x,
                Mathf.Abs(scale.y) < minimumAxis ? 1f : 1f / scale.y,
                Mathf.Abs(scale.z) < minimumAxis ? 1f : 1f / scale.z);
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

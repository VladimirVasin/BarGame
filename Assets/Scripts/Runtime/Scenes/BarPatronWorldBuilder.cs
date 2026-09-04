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
        /// The guest's bottle presentation. Booth guests deliberately keep
        /// their hands clear; every counter/table drinker owns one.
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

        public const float BoothSeatHeight = 0.48f;
        public const float CounterSeatHeight =
            MountainRoadCafeWorldBuilder.StoolSeatTopAboveFloor;
        public const float PubTableTopHeight = 0.82f;
        public const float PubTableHandInset = 0.28f;
        public const float PubTableBottleRestInset = 0.42f;
        public const float CounterTopBuildUp = 0.16f;
        public const float CounterBottleRestInset = 0.68f;
        public const float CounterSurfaceHandInset = 0.68f;
        public const float CounterSurfaceHandSideOffset = 0.16f;
        public const float PubTableHandSideOffset = 0.10f;
        public const float HandSurfaceClearance = 0.015f;
        public const float BottleSurfaceClearance = 0.002f;

        /// <summary>
        /// Shelf bottles are counter-scale showpieces; the same
        /// silhouette shrinks to a hand-sized prop in a guest's fist.
        /// </summary>
        public const float BottlePropScale = 0.42f;

        /// <summary>The fist wraps the bottle at this height share.</summary>
        public const float BottleGripHeightShare = 0.55f;

        public const string BottleSocketName = "SOCKET_Bottle.R";
        public const string MouthSocketName = "SOCKET_Mouth";
        public const string RightClavicleBoneName = "clavicle.R";
        public const string RightUpperArmBoneName = "upper_arm.R";
        public const string RightForearmBoneName = "forearm.R";
        public const string RightHandBoneName = "hand.R";
        public const string LeftClavicleBoneName = "clavicle.L";
        public const string LeftUpperArmBoneName = "upper_arm.L";
        public const string LeftForearmBoneName = "forearm.L";
        public const string LeftHandSocketName = "SOCKET_Vessel.L";
        public const string SpineBoneName = "spine";
        public const string ChestBoneName = "chest";
        public const string HeadBoneName = "head";

        private static readonly string[] SeatedDesignIds =
        {
            CityPedestrianResources.WeighAttendantDesignId,
            CityPedestrianResources.WatchmanDesignId
        };

        private static readonly string[] TableDesignIds =
        {
            CityPedestrianResources.WeighAttendantDesignId,
            CityPedestrianResources.WatchmanDesignId
        };

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

            int patronIndex = 0;
            int seatedDesignIndex = 0;
            int tableDesignIndex = 0;
            AnimationClip counterDrinkClip = LoadCafeDrinkClip();
            for (int index = 0; index < layout.NpcAnchors.Count; index++)
            {
                BarNpcAnchor anchor = layout.NpcAnchors[index];
                if (anchor.Role == BarNpcRole.Bartender)
                {
                    continue;
                }

                patronIndex++;
                string designId = ResolveDesignId(
                    anchor,
                    ref seatedDesignIndex,
                    ref tableDesignIndex);
                if (!CityPedestrianResources.TryGetArchetype(
                        designId,
                        out CityPedestrianArchetype archetype))
                {
                    GameLog.Warning(
                        "bar",
                        "patron_design_missing",
                        GameLog.Field("design", designId));
                    continue;
                }

                GameObject prefab =
                    CityPedestrianResources.LoadPrefab(archetype);
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

                bool requiresSeat =
                    anchor.Role == BarNpcRole.SeatedPatron ||
                    anchor.Role == BarNpcRole.CounterPatron;
                bool seated = requiresSeat &&
                              TrySeat(
                                  root,
                                  anchor,
                                  archetype,
                                  presentation);
                if (requiresSeat && !seated)
                {
                    GameLog.Warning(
                        "bar",
                        "patron_seat_contract_failed",
                        GameLog.Field("anchor", anchor.Id),
                        GameLog.Field("design", registry.DesignId));
                    UnityEngine.Object.Destroy(registry.gameObject);
                    continue;
                }

                if (seated)
                {
                    // Bind before the first camera frame; waiting for Update
                    // is what made a guest appear suspended on scene entry.
                    presentation.Advance(0f, false, true);
                }

                BarPatronDrinkingArmPose drinking = null;
                if (anchor.Role == BarNpcRole.CounterPatron &&
                    counterDrinkClip != null)
                {
                    drinking = TryAttachCounterDrinking(
                        anchor,
                        registry,
                        presentation,
                        counterDrinkClip,
                        layout,
                        patronIndex);
                }
                else if (anchor.Role == BarNpcRole.StandingPatron)
                {
                    drinking = TryAttachTableDrinking(
                        anchor,
                        registry,
                        patronIndex);
                }
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
            CityPedestrianArchetype archetype,
            CityPedestrianPresentation presentation)
        {
            if (archetype == null || archetype.SeatedRide == null)
            {
                return false;
            }

            Quaternion rotation =
                Quaternion.Euler(0f, anchor.YawDegrees, 0f);
            float seatHeight =
                anchor.Role == BarNpcRole.CounterPatron
                    ? CounterSeatHeight
                    : BoothSeatHeight;
            var seatAnchor = new GameObject(
                $"Bar Patron Seat {anchor.Id}");
            seatAnchor.transform.SetParent(root, false);
            seatAnchor.transform.localPosition =
                anchor.Position +
                (Vector3.up * seatHeight) +
                (rotation * Vector3.forward *
                 archetype.SeatedRide.SeatBackOffset);
            seatAnchor.transform.localRotation = rotation;
            return presentation.TrySeat(
                seatAnchor.transform,
                archetype.SeatedRide);
        }

        private static BarPatronDrinkingArmPose
            TryAttachCounterDrinking(
            BarNpcAnchor anchor,
            CityPedestrianAssetRegistry registry,
            CityPedestrianPresentation pedestrianPresentation,
            AnimationClip drinkClip,
            BarInteriorLayoutPlan layout,
            int patronIndex)
        {
            Transform socket = FindDeep(
                registry.transform,
                BottleSocketName);
            Transform mouth = FindDeep(
                registry.transform,
                MouthSocketName);
            Transform rightClavicle = FindDeep(
                registry.transform,
                RightClavicleBoneName);
            Transform rightUpperArm = FindDeep(
                registry.transform,
                RightUpperArmBoneName);
            Transform rightForearm = FindDeep(
                registry.transform,
                RightForearmBoneName);
            Transform rightHand = FindDeep(
                registry.transform,
                RightHandBoneName);
            Transform leftClavicle = FindDeep(
                registry.transform,
                LeftClavicleBoneName);
            Transform leftUpperArm = FindDeep(
                registry.transform,
                LeftUpperArmBoneName);
            Transform leftForearm = FindDeep(
                registry.transform,
                LeftForearmBoneName);
            Transform leftSocket = FindDeep(
                registry.transform,
                LeftHandSocketName);
            Transform spine = FindDeep(
                registry.transform,
                SpineBoneName);
            Transform chest = FindDeep(
                registry.transform,
                ChestBoneName);
            Transform head = FindDeep(
                registry.transform,
                HeadBoneName);
            if (socket == null ||
                mouth == null ||
                rightClavicle == null ||
                rightUpperArm == null ||
                rightForearm == null ||
                rightHand == null ||
                leftClavicle == null ||
                leftUpperArm == null ||
                leftForearm == null ||
                leftSocket == null ||
                spine == null ||
                chest == null ||
                head == null)
            {
                LogMissingDrinkRig(registry);
                return null;
            }

            int seed = StableSeed(anchor.Id, patronIndex);
            CreateBottle(
                registry,
                seed,
                out Transform bottleRoot,
                out Transform bottleMouth,
                out float gripToLipDistance,
                out float gripToBaseDistance);
            float counterSurfaceHeight =
                layout.CounterPosition.y +
                layout.CounterSize.y * 0.5f +
                CounterTopBuildUp;
            ResolveSurfaceRestPoints(
                anchor,
                registry,
                counterSurfaceHeight,
                CounterBottleRestInset,
                CounterSurfaceHandInset,
                CounterSurfaceHandSideOffset,
                out Vector3 bottleRestPoint,
                out Vector3 supportPoint);
            BarPatronDrinkingArmPose pose =
                registry.gameObject.AddComponent<
                    BarPatronDrinkingArmPose>();
            pose.InitializeCounter(
                new BarPatronDrinkTimeline(seed),
                pedestrianPresentation,
                drinkClip,
                registry.transform,
                rightClavicle,
                rightUpperArm,
                rightForearm,
                rightHand,
                socket,
                leftClavicle,
                leftUpperArm,
                leftForearm,
                leftSocket,
                spine,
                chest,
                head,
                mouth,
                bottleRoot,
                bottleMouth,
                gripToLipDistance,
                gripToBaseDistance,
                bottleRestPoint,
                supportPoint);
            return pose;
        }

        private static BarPatronDrinkingArmPose TryAttachTableDrinking(
            BarNpcAnchor anchor,
            CityPedestrianAssetRegistry registry,
            int patronIndex)
        {
            Transform rightSocket = FindDeep(
                registry.transform,
                BottleSocketName);
            Transform mouth = FindDeep(
                registry.transform,
                MouthSocketName);
            Transform rightClavicle = FindDeep(
                registry.transform,
                RightClavicleBoneName);
            Transform rightUpperArm = FindDeep(
                registry.transform,
                RightUpperArmBoneName);
            Transform rightForearm = FindDeep(
                registry.transform,
                RightForearmBoneName);
            Transform rightHand = FindDeep(
                registry.transform,
                RightHandBoneName);
            Transform leftClavicle = FindDeep(
                registry.transform,
                LeftClavicleBoneName);
            Transform leftUpperArm = FindDeep(
                registry.transform,
                LeftUpperArmBoneName);
            Transform leftForearm = FindDeep(
                registry.transform,
                LeftForearmBoneName);
            Transform leftSocket = FindDeep(
                registry.transform,
                LeftHandSocketName);
            Transform spine = FindDeep(
                registry.transform,
                SpineBoneName);
            Transform chest = FindDeep(
                registry.transform,
                ChestBoneName);
            Transform head = FindDeep(
                registry.transform,
                HeadBoneName);
            if (rightSocket == null ||
                mouth == null ||
                rightClavicle == null ||
                rightUpperArm == null ||
                rightForearm == null ||
                rightHand == null ||
                leftClavicle == null ||
                leftUpperArm == null ||
                leftForearm == null ||
                leftSocket == null ||
                spine == null ||
                chest == null ||
                head == null)
            {
                LogMissingDrinkRig(registry);
                return null;
            }

            int seed = StableSeed(anchor.Id, patronIndex);
            CreateBottle(
                registry,
                seed,
                out Transform bottleRoot,
                out Transform bottleMouth,
                out float gripToLipDistance,
                out float gripToBaseDistance);
            ResolveSurfaceRestPoints(
                anchor,
                registry,
                PubTableTopHeight,
                PubTableBottleRestInset,
                PubTableHandInset,
                PubTableHandSideOffset,
                out Vector3 bottleRestPoint,
                out Vector3 supportPoint);
            BarPatronDrinkingArmPose pose =
                registry.gameObject.AddComponent<
                    BarPatronDrinkingArmPose>();
            pose.InitializeTable(
                new BarPatronDrinkTimeline(seed),
                registry.transform,
                rightClavicle,
                rightUpperArm,
                rightForearm,
                rightHand,
                rightSocket,
                leftClavicle,
                leftUpperArm,
                leftForearm,
                leftSocket,
                spine,
                chest,
                head,
                mouth,
                bottleRoot,
                bottleMouth,
                gripToLipDistance,
                gripToBaseDistance,
                bottleRestPoint,
                supportPoint);
            return pose;
        }

        private static void CreateBottle(
            CityPedestrianAssetRegistry registry,
            int seed,
            out Transform bottleRoot,
            out Transform bottleMouth,
            out float gripToLipDistance,
            out float gripToBaseDistance)
        {
            BarDrinkPresentation drink =
                BarDrinkPresentationCatalog.Get(
                    PatronDrinks[(seed & int.MaxValue) %
                                 PatronDrinks.Length]);

            var bottleObject = new GameObject(
                $"Patron Bottle {drink.StableId}");
            bottleRoot = bottleObject.transform;
            bottleRoot.SetParent(registry.transform, false);

            var rigObject = new GameObject("Bottle Rig");
            Transform rig = rigObject.transform;
            rig.SetParent(bottleRoot, false);
            rig.localRotation = Quaternion.identity;
            rig.localScale = Vector3.one * BottlePropScale;
            float bottleHeight =
                BarDrinkServiceWorldBuilder.BuildBottleVisual(
                    rig,
                    drink);
            rig.localPosition = Vector3.down *
                (bottleHeight *
                 BottlePropScale *
                 BottleGripHeightShare);

            var mouthAnchor = new GameObject("Bottle Mouth Anchor");
            mouthAnchor.transform.SetParent(rig, false);
            mouthAnchor.transform.localPosition =
                Vector3.up * bottleHeight;
            bottleMouth = mouthAnchor.transform;
            gripToLipDistance =
                bottleHeight *
                BottlePropScale *
                (1f - BottleGripHeightShare);
            gripToBaseDistance =
                bottleHeight *
                BottlePropScale *
                BottleGripHeightShare;
        }

        private static void ResolveSurfaceRestPoints(
            BarNpcAnchor anchor,
            CityPedestrianAssetRegistry registry,
            float surfaceHeight,
            float bottleForwardInset,
            float supportForwardInset,
            float sideOffset,
            out Vector3 bottleRestPoint,
            out Vector3 supportPoint)
        {
            Quaternion rotation =
                Quaternion.Euler(0f, anchor.YawDegrees, 0f);
            Vector3 localCenter =
                anchor.Position +
                Vector3.up * surfaceHeight;
            Vector3 localBottlePoint =
                localCenter +
                rotation * Vector3.forward * bottleForwardInset +
                rotation * Vector3.right * sideOffset +
                Vector3.up * BottleSurfaceClearance;
            Vector3 localSupportPoint =
                localCenter +
                rotation * Vector3.forward * supportForwardInset +
                rotation * Vector3.left * sideOffset +
                Vector3.up * HandSurfaceClearance;
            Transform layoutRoot = registry.transform.parent;
            bottleRestPoint = layoutRoot != null
                ? layoutRoot.TransformPoint(localBottlePoint)
                : localBottlePoint;
            supportPoint = layoutRoot != null
                ? layoutRoot.TransformPoint(localSupportPoint)
                : localSupportPoint;
        }

        private static AnimationClip LoadCafeDrinkClip()
        {
            MountainRoadCafeCastProvider provider =
                MountainRoadCafeCastProvider.Load();
            MountainRoadCafeCastAssetRegistry source =
                provider != null && provider.PairManPrefab != null
                    ? provider.PairManPrefab.GetComponent<
                        MountainRoadCafeCastAssetRegistry>()
                    : null;
            AnimationClip clip = source?.GetClip(
                MountainRoadCafeCastClipKind.Drink);
            if (clip == null)
            {
                GameLog.Warning("bar", "cafe_drink_clip_missing");
            }

            return clip;
        }

        private static string ResolveDesignId(
            BarNpcAnchor anchor,
            ref int seatedIndex,
            ref int tableIndex)
        {
            if (anchor.Role == BarNpcRole.SeatedPatron ||
                anchor.Role == BarNpcRole.CounterPatron)
            {
                string design = SeatedDesignIds[
                    seatedIndex % SeatedDesignIds.Length];
                seatedIndex++;
                return design;
            }

            string tableDesign = TableDesignIds[
                tableIndex % TableDesignIds.Length];
            tableIndex++;
            return tableDesign;
        }

        private static void LogMissingDrinkRig(
            CityPedestrianAssetRegistry registry)
        {
            GameLog.Warning(
                "bar",
                "patron_drink_sockets_missing",
                GameLog.Field("design", registry.DesignId));
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

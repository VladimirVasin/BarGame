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
        /// One visit-owned drink shared by standing and seated guests.
        /// </summary>
        public BarPatronDrinkingArmPose Drinking { get; }
        public BarCounterPatronRefill Refill => Drinking != null ? Drinking.CounterRefill : null;
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
    /// A fresh default-model visitor cast is allocated for each bar entry.
    /// The global appearance/wardrobe selector owns the temporary assignments;
    /// this builder owns placement, seats and the silent drinking tableau.
    /// </summary>
    public static class BarPatronWorldBuilder
    {
        public const string RootName = "Bar Patrons";

        public const float BoothSeatHeight = 0.48f;
        public const float CounterSeatHeight =
            MountainRoadCafeWorldBuilder.StoolSeatTopAboveFloor;
        public const float PubTableTopHeight = 0.82f;
        public const float PubTableHandInset = 0.40f;
        public const float PubTableBottleRestInset = 0.50f;
        public const float CounterTopBuildUp = 0.16f;
        public const float CounterBottleRestInset = 0.68f;
        public const float CounterSurfaceHandInset = 0.68f;
        public const float CounterSurfaceHandSideOffset = 0.16f;
        public const float PubTableHandSideOffset = 0.10f;
        public const float BoothBottleRestInset = 0.49f;
        public const float BoothHandInset = 0.50f;
        public const float BoothHandSideOffset = 0.16f;
        public const float HandSurfaceClearance = 0.015f;
        public const float BottleSurfaceClearance = 0.002f;

        /// <summary>
        /// Shelf bottles are counter-scale showpieces; the same
        /// silhouette shrinks to a hand-sized prop in a guest's fist.
        /// </summary>
        public const float BottlePropScale = 0.42f;

        /// <summary>The fist wraps the bottle at this height share.</summary>

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
            BarInteriorLayoutPlan layout,
            Transform room)
        {
            if (parent == null)
            {
                throw new ArgumentNullException(nameof(parent));
            }

            if (layout == null)
            {
                throw new ArgumentNullException(nameof(layout));
            }

            if (room == null) throw new ArgumentNullException(nameof(room));
            var surfaces = new Dictionary<string, Renderer>(StringComparer.Ordinal)
            {
                { "counter", RequireSurface(room, "Counter Top") },
                { "booth-1", RequireSurface(room, "Booth Table Top 1") },
                { "booth-2", RequireSurface(room, "Booth Table Top 2") },
                { "pub-table-left-front", RequireSurface(room, "Social Pub Table 1") },
                { "pub-table-right-front", RequireSurface(room, "Social Pub Table 2") },
                { "pub-table-left-rear", RequireSurface(room, "Social Pub Table 3") }
            };

            var patrons = new List<BarPatron>(layout.NpcAnchors.Count);
            Transform root = new GameObject(RootName).transform;
            root.SetParent(parent, false);

            int visitorCount = 0;
            for (int index = 0; index < layout.NpcAnchors.Count; index++)
                if (layout.NpcAnchors[index].Role != BarNpcRole.Bartender) visitorCount++;
            BarPatronDrinkPropKind[] drinkKinds = AllocateDrinkKinds(visitorCount);
            IReadOnlyList<DefaultNpcPopulation.Assignment> visitors =
                DefaultNpcPopulation.AllocateVisitGroup("bar.visitors", visitorCount,
                    DefaultNpcPopulation.AnyCatalogModel,
                    new DefaultNpcPopulation.SlotConstraint("apron", new string[] { null }),
                    new DefaultNpcPopulation.SlotConstraint("gloves", null, "gloves.work"),
                    new DefaultNpcPopulation.SlotConstraint("scarf", null, "scarf.warm"),
                    new DefaultNpcPopulation.SlotConstraint("headwear", null,
                        "headwear.everyday", "headwear.work", "headwear.warm"));
            CityPedestrianAssetRegistry clipDonor =
                CityPedestrianResources.LoadStreetClipDonor();
            if (clipDonor == null || clipDonor.SitClip == null)
                throw new InvalidOperationException("Bar visitors require the shared seated clip donor.");
            int patronIndex = 0;
            AnimationClip counterDrinkClip = LoadCafeDrinkClip();
            for (int index = 0; index < layout.NpcAnchors.Count; index++)
            {
                BarNpcAnchor anchor = layout.NpcAnchors[index];
                if (anchor.Role == BarNpcRole.Bartender)
                {
                    continue;
                }

                CityPedestrianAssetRegistry registry = CityPedestrianDefaultNpcBody.Create(
                    root, visitors[patronIndex++], clipDonor);
                if (!CityPedestrianResources.TryGetArchetype(
                        registry.DesignId,
                        out CityPedestrianArchetype archetype))
                {
                    throw new InvalidOperationException(
                        $"Default bar visitor model '{registry.DesignId}' has no compatible seat contract.");
                }

                registry.gameObject.name = $"Bar Patron {anchor.Id}";
                registry.transform.localPosition = anchor.Position;
                registry.transform.localRotation =
                    Quaternion.Euler(0f, anchor.YawDegrees, 0f);
                // The body adapter has already released the village graph;
                // only this presentation drives the rig. Wardrobe colours
                // stay population-owned, without a scene palette repaint.
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
                        patronIndex,
                        surfaces["counter"],
                        drinkKinds[patronIndex - 1] == BarPatronDrinkPropKind.Bottle
                            ? BarPatronDrinkPropKind.ShotGlass : drinkKinds[patronIndex - 1]);
                }
                else if (anchor.Role == BarNpcRole.StandingPatron ||
                         anchor.Role == BarNpcRole.SeatedPatron)
                {
                    bool booth = anchor.Role == BarNpcRole.SeatedPatron;
                    string surfaceId = booth
                        ? anchor.Id.Substring(0, anchor.Id.LastIndexOf('-'))
                        : anchor.RouteId;
                    drinking = TryAttachTableDrinking(
                        anchor,
                        registry,
                        presentation,
                        layout,
                        patronIndex,
                        surfaces[surfaceId],
                        booth,
                        drinkKinds[patronIndex - 1]);
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
            int patronIndex,
            Renderer surface,
            BarPatronDrinkPropKind propKind)
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
            BarPatronDrinkProp prop = CreateDrinkProp(registry.transform, propKind, seed);
            float counterSurfaceHeight =
                registry.transform.parent.InverseTransformPoint(surface.bounds.max).y;
            ResolveSurfaceRestPoints(
                anchor,
                registry,
                counterSurfaceHeight,
                CounterBottleRestInset,
                CounterSurfaceHandInset,
                CounterSurfaceHandSideOffset,
                out Vector3 bottleRestPoint,
                out Vector3 supportPoint);
            ClampSurfacePoints(registry.transform, prop, surface, false,
                ref bottleRestPoint, ref supportPoint);
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
                prop.Root,
                prop.Mouth,
                prop.GripToLipDistance,
                prop.GripToBaseDistance,
                bottleRestPoint,
                supportPoint,
                restingSurface: surface,
                drinkProp: prop);
            return pose;
        }

        private static BarPatronDrinkingArmPose TryAttachTableDrinking(
            BarNpcAnchor anchor,
            CityPedestrianAssetRegistry registry,
            CityPedestrianPresentation pedestrianPresentation,
            BarInteriorLayoutPlan layout,
            int patronIndex,
            Renderer surface,
            bool isSeatedTable,
            BarPatronDrinkPropKind propKind)
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
            BarPatronDrinkProp prop = CreateDrinkProp(registry.transform, propKind, seed);
            if (!isSeatedTable && (!layout.TryGetFurniture(anchor.RouteId,
                    out BarInteriorFurnitureFootprint table) ||
                table.Kind != BarInteriorFurnitureKind.PubTable))
            {
                throw new InvalidOperationException(
                    $"Patron '{anchor.Id}' has no authored pub table.");
            }

            ResolveSurfaceRestPoints(
                anchor,
                registry,
                registry.transform.parent.InverseTransformPoint(surface.bounds.max).y,
                isSeatedTable ? BoothBottleRestInset : PubTableBottleRestInset,
                isSeatedTable ? BoothHandInset : PubTableHandInset,
                isSeatedTable ? BoothHandSideOffset : PubTableHandSideOffset,
                out Vector3 bottleRestPoint,
                out Vector3 supportPoint);
            ClampSurfacePoints(registry.transform, prop, surface, !isSeatedTable,
                ref bottleRestPoint, ref supportPoint);
            BarPatronDrinkingArmPose pose =
                registry.gameObject.AddComponent<
                    BarPatronDrinkingArmPose>();
            pose.InitializeTable(
                new BarPatronDrinkTimeline(seed),
                pedestrianPresentation,
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
                prop.Root,
                prop.Mouth,
                prop.GripToLipDistance,
                prop.GripToBaseDistance,
                bottleRestPoint,
                supportPoint,
                restingSurface: surface,
                isSeatedTable: isSeatedTable,
                drinkProp: prop);
            return pose;
        }

        private static Renderer RequireSurface(Transform room, string name)
        {
            Transform part = room.Find(name);
            Renderer surface = part != null ? part.GetComponent<Renderer>() : null;
            if (surface == null)
                throw new InvalidOperationException($"Bar guests require authored surface '{name}'.");
            return surface;
        }

        private static BarPatronDrinkPropKind[] AllocateDrinkKinds(int count)
        {
            // A fresh balanced bag keeps every visit varied, independently of
            // permanent character identities and the deterministic floor plan.
            var random = new System.Random(Guid.NewGuid().GetHashCode());
            var kinds = new BarPatronDrinkPropKind[count];
            int firstKind = random.Next(5);
            for (int index = 0; index < count; index++)
                kinds[index] = (BarPatronDrinkPropKind)((index + firstKind) % 5);
            for (int index = count - 1; index > 0; index--)
            {
                int other = random.Next(index + 1);
                BarPatronDrinkPropKind value = kinds[index];
                kinds[index] = kinds[other];
                kinds[other] = value;
            }
            return kinds;
        }

        private static BarPatronDrinkProp CreateDrinkProp(
            Transform parent, BarPatronDrinkPropKind kind, int seed)
        {
            bool alternate = (seed & 1) != 0;
            DrinkId drink = kind switch
            {
                BarPatronDrinkPropKind.Pint => alternate ? DrinkId.DarkBeer : DrinkId.LightBeer,
                BarPatronDrinkPropKind.WineGlass => alternate ? DrinkId.RedWine : DrinkId.WhiteWine,
                BarPatronDrinkPropKind.ShotGlass => alternate ? DrinkId.PepperVodka : DrinkId.Vodka,
                BarPatronDrinkPropKind.Snifter => alternate ? DrinkId.CognacVsop : DrinkId.CognacVs,
                _ => PatronDrinks[(seed & int.MaxValue) % PatronDrinks.Length]
            };
            return BarPatronDrinkProp.Create(parent, kind, BarDrinkPresentationCatalog.Get(drink));
        }

        private static void ClampSurfacePoints(Transform owner, BarPatronDrinkProp prop,
            Renderer surface, bool round, ref Vector3 basePoint, ref Vector3 supportPoint)
        {
            Vector3 worldForward = Vector3.Cross(owner.right, owner.up).normalized;
            Vector3 localForward = Vector3.Cross(prop.HandleLocalAxis, prop.OpeningLocalAxis).normalized;
            Quaternion rotation = Quaternion.LookRotation(worldForward, owner.up) *
                Quaternion.Inverse(Quaternion.LookRotation(localForward, prop.OpeningLocalAxis));
            Bounds bounds = prop.PhysicalLocalBounds;
            Bounds offsets = new Bounds(Vector3.zero, Vector3.zero);
            float footprintRadius = 0f;
            for (int index = 0; index < 8; index++)
            {
                Vector3 corner = bounds.center + Vector3.Scale(bounds.extents,
                    new Vector3((index & 1) == 0 ? -1f : 1f,
                        (index & 2) == 0 ? -1f : 1f, (index & 4) == 0 ? -1f : 1f));
                Vector3 offset = rotation * (corner - prop.BaseLocalPosition);
                offsets.Encapsulate(offset);
                footprintRadius = Mathf.Max(footprintRadius,
                    new Vector2(offset.x, offset.z).magnitude);
            }
            Bounds top = surface.bounds;
            if (round)
            {
                // The sixteen-sided round tabletop fits inside this inradius.
                float radius = Mathf.Min(top.extents.x, top.extents.z) * Mathf.Cos(Mathf.PI / 16f);
                Vector2 centre = new Vector2(top.center.x, top.center.z);
                Vector2 position = centre + Vector2.ClampMagnitude(
                    new Vector2(basePoint.x, basePoint.z) - centre,
                    Mathf.Max(0f, radius - footprintRadius - .01f));
                basePoint.x = position.x;
                basePoint.z = position.y;
                position = centre + Vector2.ClampMagnitude(
                    new Vector2(supportPoint.x, supportPoint.z) - centre, radius - .055f);
                supportPoint.x = position.x;
                supportPoint.z = position.y;
            }
            else
            {
                const float margin = .015f;
                float minX = top.min.x + margin - offsets.min.x;
                float maxX = top.max.x - margin - offsets.max.x;
                float minZ = top.min.z + margin - offsets.min.z;
                float maxZ = top.max.z - margin - offsets.max.z;
                if (minX > maxX || minZ > maxZ)
                    throw new InvalidOperationException("A guest's vessel cannot fit its real tabletop.");
                basePoint.x = Mathf.Clamp(basePoint.x, minX, maxX);
                basePoint.z = Mathf.Clamp(basePoint.z, minZ, maxZ);
                supportPoint.x = Mathf.Clamp(supportPoint.x, top.min.x + .055f, top.max.x - .055f);
                supportPoint.z = Mathf.Clamp(supportPoint.z, top.min.z + .055f, top.max.z - .055f);
            }
            basePoint.y = top.max.y + BottleSurfaceClearance;
            supportPoint.y = top.max.y + HandSurfaceClearance;
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

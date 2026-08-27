using System;
using System.Collections.Generic;
using UnityEngine;

namespace BarPromenade
{
    public static class CityDecorationCollisionCatalog
    {
        public static CityDecorationCollisionTier ResolveTier(
            CityDecorationKind kind)
        {
            switch (kind)
            {
                case CityDecorationKind.OldTownScaffolding:
                case CityDecorationKind.IndustrialPipeRack:
                case CityDecorationKind.NightlifeFireEscape:
                case CityDecorationKind.RoadsideRoadworkAndBicycle:
                    return CityDecorationCollisionTier.Detail;
                case CityDecorationKind.OldTownStreetMarket:
                case CityDecorationKind.ResidentialDiscardedFurniture:
                case CityDecorationKind.IndustrialCargo:
                case CityDecorationKind.NightlifeVendingAndQueue:
                case CityDecorationKind.RoadsideDumpsterAndUtility:
                case CityDecorationKind.RoadsidePhoneBooth:
                case CityDecorationKind.RoadsideBusShelter:
                case CityDecorationKind.ParkFountainAndStatue:
                case CityDecorationKind.ParkBandstand:
                case CityDecorationKind.ParkChessTables:
                case CityDecorationKind.ParkPlayground:
                    return CityDecorationCollisionTier.Blocking;
                case CityDecorationKind.OldTownChimneysAndDormers:
                case CityDecorationKind.OldTownClockTower:
                case CityDecorationKind.ResidentialBalconies:
                case CityDecorationKind.ResidentialLaundryAndAntenna:
                case CityDecorationKind.ResidentialRooftopGreenhouse:
                case CityDecorationKind.IndustrialStacksAndTanks:
                case CityDecorationKind.IndustrialGantry:
                case CityDecorationKind.NightlifeBillboard:
                case CityDecorationKind.NightlifeCinema:
                // The water network is flush ironwork: a grate and a lid
                // are walked over, and the capped column is slim enough
                // that a collider on it would only catch a shoulder at
                // the kerb. Nothing here takes a proxy.
                case CityDecorationKind.RoadsideDrainAndCover:
                case CityDecorationKind.RoadsideCappedStandpipe:
                case CityDecorationKind.LotGroundDownpipeOutfall:
                    return CityDecorationCollisionTier.None;
                default:
                    throw new ArgumentOutOfRangeException(
                        nameof(kind),
                        kind,
                        "Unsupported city decoration kind.");
            }
        }
    }

    internal static class CityStaticCollisionBuilder
    {
        internal const int MaximumDecorationProxyCount = 4;

        internal static void AddDecorationProxyBounds(
            CityLayout layout,
            CityDecorationDescriptor descriptor,
            ICollection<Bounds> target)
        {
            if (layout == null)
            {
                throw new ArgumentNullException(nameof(layout));
            }

            if (target == null)
            {
                throw new ArgumentNullException(nameof(target));
            }

            if (descriptor.CollisionTier ==
                CityDecorationCollisionTier.None)
            {
                return;
            }

            descriptor.TryResolveLot(layout, out BuildingLot lot);
            var context = new ProxyContext(descriptor, lot);
            int initialCount = target.Count;
            switch (descriptor.Kind)
            {
                case CityDecorationKind.OldTownScaffolding:
                    AddScaffolding(context, target);
                    break;
                case CityDecorationKind.OldTownStreetMarket:
                    AddMarket(context, target);
                    break;
                case CityDecorationKind.ResidentialDiscardedFurniture:
                    AddFurniture(context, target);
                    break;
                case CityDecorationKind.IndustrialPipeRack:
                    AddPipeRack(context, target);
                    break;
                case CityDecorationKind.IndustrialCargo:
                    AddCargo(context, target);
                    break;
                case CityDecorationKind.NightlifeFireEscape:
                    AddFireEscape(context, target);
                    break;
                case CityDecorationKind.NightlifeVendingAndQueue:
                    AddVendingMachines(context, target);
                    break;
                case CityDecorationKind.RoadsideDumpsterAndUtility:
                    AddDumpster(context, target);
                    break;
                case CityDecorationKind.RoadsidePhoneBooth:
                    context.Add(target, 0f, 1.35f, 0f, 1.45f, 2.70f, 1.35f);
                    break;
                case CityDecorationKind.RoadsideBusShelter:
                    AddBusShelter(context, target);
                    break;
                case CityDecorationKind.RoadsideRoadworkAndBicycle:
                    context.Add(target, 0f, 0.62f, 0f, 3.45f, 1.24f, 0.48f);
                    break;
                case CityDecorationKind.ParkFountainAndStatue:
                    context.Add(target, 0f, 0.48f, 0f, 6.40f, 0.96f, 6.40f);
                    break;
                case CityDecorationKind.ParkBandstand:
                    context.Add(target, 0f, 0.50f, 0f, 6.80f, 1.00f, 5.60f);
                    break;
                case CityDecorationKind.ParkChessTables:
                    // Table, board and both benches per side. The box
                    // starts below the anchor plane because the set
                    // beds its feet into a lawn that slopes under it.
                    // The extents are the set's own, so the walk that
                    // routes a sitter around this block cannot disagree
                    // with the block.
                    for (int table = -1; table <= 1; table += 2)
                    {
                        context.Add(
                            target,
                            table *
                            CityChessBoardGeometry.TableOffsetMeters,
                            0.45f,
                            0f,
                            CityChessBoardGeometry
                                .TableBlockHalfWidthMeters * 2f,
                            1.30f,
                            CityChessBoardGeometry
                                .TableBlockHalfDepthMeters * 2f);
                    }

                    break;
                case CityDecorationKind.ParkPlayground:
                    AddPlayground(context, target);
                    break;
                default:
                    throw new InvalidOperationException(
                        $"Decoration '{descriptor.StableId}' declares " +
                        $"'{descriptor.CollisionTier}' collision without " +
                        "a logical proxy recipe.");
            }

            int added = target.Count - initialCount;
            if (added < 1 || added > MaximumDecorationProxyCount)
            {
                throw new InvalidOperationException(
                    $"Decoration '{descriptor.StableId}' created {added} " +
                    "collision proxies; expected between one and four.");
            }
        }

        internal static void AddBoxColliders(
            Transform target,
            IReadOnlyList<Bounds> localBounds)
        {
            for (int index = 0; index < localBounds.Count; index++)
            {
                Bounds bounds = localBounds[index];
                BoxCollider collider =
                    target.gameObject.AddComponent<BoxCollider>();
                collider.center = bounds.center;
                collider.size = bounds.size;
            }
        }

        internal static void AddParkBenchColliders(
            Transform parent,
            IReadOnlyList<CityParkBenchDescriptor> benches)
        {
            var bounds = new List<Bounds>(benches.Count);
            for (int index = 0; index < benches.Count; index++)
            {
                CityParkBenchDescriptor bench = benches[index];
                bool runsAlongZ = Mathf.Abs(bench.Tangent.z) >
                                  Mathf.Abs(bench.Tangent.x);
                bounds.Add(new Bounds(
                    bench.Position + (Vector3.up * 0.48f),
                    runsAlongZ
                        ? new Vector3(0.72f, 0.96f, 2.30f)
                        : new Vector3(2.30f, 0.96f, 0.72f)));
            }

            AddColliderGroup(parent, "Park Bench Colliders", bounds);
        }

        internal static void AddColliderGroup(
            Transform parent,
            string name,
            IReadOnlyList<Bounds> localBounds)
        {
            if (localBounds.Count == 0)
            {
                return;
            }

            Transform group = new GameObject(name).transform;
            group.SetParent(parent, false);
            AddBoxColliders(group, localBounds);
        }

        internal static void AddHomeMailboxCollider(
            Transform parent,
            Vector3 localBase,
            bool frontageIsX)
        {
            Transform proxy = new GameObject("Home Mailbox Collider").transform;
            proxy.SetParent(parent, false);
            proxy.localPosition = localBase;
            BoxCollider collider = proxy.gameObject.AddComponent<BoxCollider>();
            collider.center = new Vector3(0f, 0.60f, 0f);
            collider.size = frontageIsX
                ? new Vector3(0.62f, 1.20f, 0.82f)
                : new Vector3(0.82f, 1.20f, 0.62f);
        }

        internal static Bounds CreateLowerPoleBounds(Vector3 localBase)
        {
            return new Bounds(
                localBase + (Vector3.up * 1.15f),
                new Vector3(0.42f, 2.30f, 0.42f));
        }

        internal static void AddLowerPoleCollider(Transform target)
        {
            Bounds bounds = CreateLowerPoleBounds(Vector3.zero);
            BoxCollider collider =
                target.gameObject.AddComponent<BoxCollider>();
            collider.center = bounds.center;
            collider.size = bounds.size;
        }

        private static void AddScaffolding(
            ProxyContext context,
            ICollection<Bounds> target)
        {
            float width = Mathf.Clamp(context.Width * 0.68f, 4.2f, 7.2f);
            context.Add(target, -width * 0.5f, 1.05f, 0.78f,
                0.38f, 2.10f, 0.38f);
            context.Add(target, width * 0.5f, 1.05f, 0.78f,
                0.38f, 2.10f, 0.38f);
        }

        private static void AddMarket(
            ProxyContext context,
            ICollection<Bounds> target)
        {
            float width = Mathf.Clamp(context.Width * 0.44f, 3.4f, 5.2f);
            context.Add(target, 0f, 0.70f, 0.18f,
                width, 1.40f, 1.20f);
        }

        private static void AddFurniture(
            ProxyContext context,
            ICollection<Bounds> target)
        {
            float mirror = context.Mirror;
            context.Add(target, -0.55f * mirror, 0.55f, 0f,
                2.35f, 1.10f, 1.00f);
            context.Add(target, 1.25f * mirror, 0.36f, 0.35f,
                1.40f, 0.72f, 2.20f);
        }

        private static void AddPipeRack(
            ProxyContext context,
            ICollection<Bounds> target)
        {
            float width = Mathf.Clamp(context.Width * 0.52f, 4.5f, 7.0f);
            for (int xSide = -1; xSide <= 1; xSide += 2)
            {
                for (int zSide = -1; zSide <= 1; zSide += 2)
                {
                    context.Add(target, xSide * width * 0.48f, 1.10f,
                        zSide * 0.75f, 0.40f, 2.20f, 0.40f);
                }
            }
        }

        private static void AddCargo(
            ProxyContext context,
            ICollection<Bounds> target)
        {
            float mirror = context.Mirror;
            context.Add(target, -1.65f * mirror, 1.15f, 0f,
                3.20f, 2.30f, 1.85f);
            context.Add(target, 1.35f * mirror, 0.82f, 0.35f,
                2.50f, 1.64f, 1.65f);
        }

        private static void AddFireEscape(
            ProxyContext context,
            ICollection<Bounds> target)
        {
            float width = Mathf.Clamp(context.Width * 0.34f, 3.0f, 4.4f);
            context.Add(target, -width * 0.48f, 1.10f, 1.12f,
                0.38f, 2.20f, 0.38f);
            context.Add(target, width * 0.48f, 1.10f, 1.12f,
                0.38f, 2.20f, 0.38f);
        }

        private static void AddVendingMachines(
            ProxyContext context,
            ICollection<Bounds> target)
        {
            context.Add(target, -0.72f, 1.02f, -0.35f,
                1.18f, 2.04f, 0.78f);
            context.Add(target, 0.72f, 1.02f, -0.35f,
                1.18f, 2.04f, 0.78f);
        }

        private static void AddDumpster(
            ProxyContext context,
            ICollection<Bounds> target)
        {
            context.Add(target, -0.65f, 0.70f, 0f,
                2.55f, 1.40f, 1.45f);
            context.Add(target, 1.35f, 0.92f, -0.22f,
                1.20f, 1.84f, 0.78f);
        }

        /// <summary>
        /// One proxy per A-frame plus the bench. The bay between them
        /// is deliberately open: the swings hanging there are real
        /// bodies the hero has to be able to walk into and push, and a
        /// proxy across the whole frame would have him bounce off thin
        /// air a metre short of the plank.
        /// </summary>
        private static void AddPlayground(
            ProxyContext context,
            ICollection<Bounds> target)
        {
            float frameDepth =
                (CityPlaygroundGeometry.PostOffsetZ * 2f) +
                CityPlaygroundGeometry.PostThickness;
            for (int xSide = -1; xSide <= 1; xSide += 2)
            {
                context.Add(
                    target,
                    xSide * CityPlaygroundGeometry.PostOffsetX,
                    CityPlaygroundGeometry.PostHeight * 0.5f,
                    0f,
                    CityPlaygroundGeometry.PostThickness + 0.34f,
                    CityPlaygroundGeometry.PostHeight,
                    frameDepth);
            }

            context.Add(
                target,
                0f,
                0.52f,
                CityPlaygroundGeometry.BenchZ,
                3.80f,
                1.04f,
                0.52f);
        }

        private static void AddBusShelter(
            ProxyContext context,
            ICollection<Bounds> target)
        {
            context.Add(target, 0f, 1.25f, -0.48f,
                4.35f, 2.50f, 0.28f);
            context.Add(target, 0f, 0.58f, 0.05f,
                2.75f, 1.16f, 0.72f);
            context.Add(target, 2.60f, 1.25f, -0.30f,
                0.42f, 2.50f, 0.42f);
        }

        private readonly struct ProxyContext
        {
            public ProxyContext(
                CityDecorationDescriptor descriptor,
                BuildingLot lot)
            {
                Descriptor = descriptor;
                Origin = descriptor.Position;
                if (descriptor.AnchorKind ==
                    CityDecorationAnchorKind.BuildingFacade)
                {
                    Origin = new Vector3(
                        Origin.x,
                        lot != null ? lot.Center.y : Origin.y,
                        Origin.z);
                }

                Forward = ResolveForward(descriptor.Forward, lot);
                Tangent = new Vector3(-Forward.z, 0f, Forward.x);
                Width = lot == null
                    ? 8f
                    : Mathf.Abs(Forward.x) > 0.5f
                        ? lot.Size.y
                        : lot.Size.x;
            }

            public CityDecorationDescriptor Descriptor { get; }
            public Vector3 Origin { get; }
            public Vector3 Forward { get; }
            public Vector3 Tangent { get; }
            public float Width { get; }
            public float Mirror =>
                (Descriptor.Variant & 1) == 0 ? 1f : -1f;

            public void Add(
                ICollection<Bounds> target,
                float x,
                float y,
                float z,
                float width,
                float height,
                float depth)
            {
                float depthScale = ResolveStreetDepthScale(
                    Descriptor.Kind);
                z *= depthScale;
                depth *= depthScale;
                Vector3 center =
                    Origin +
                    (Tangent * x) +
                    (Vector3.up * y) +
                    (Forward * z);
                Vector3 size = Mathf.Abs(Forward.x) > 0.5f
                    ? new Vector3(depth, height, width)
                    : new Vector3(width, height, depth);
                target.Add(new Bounds(center, size));
            }

            private static Vector3 ResolveForward(
                Vector3 candidate,
                BuildingLot lot)
            {
                candidate.y = 0f;
                if (!IsFinite(candidate.x) ||
                    !IsFinite(candidate.z) ||
                    candidate.sqrMagnitude < 0.25f)
                {
                    candidate = lot != null && lot.HasRoadFrontage
                        ? new Vector3(
                            lot.FrontageDirection.x,
                            0f,
                            lot.FrontageDirection.y)
                        : Vector3.back;
                }

                return Mathf.Abs(candidate.x) > Mathf.Abs(candidate.z)
                    ? new Vector3(Mathf.Sign(candidate.x), 0f, 0f)
                    : new Vector3(0f, 0f, Mathf.Sign(candidate.z));
            }

            private static float ResolveStreetDepthScale(
                CityDecorationKind kind)
            {
                switch (kind)
                {
                    case CityDecorationKind.OldTownStreetMarket:
                        return 0.55f;
                    case CityDecorationKind.ResidentialDiscardedFurniture:
                        return 0.42f;
                    case CityDecorationKind.IndustrialCargo:
                        return 0.28f;
                    case CityDecorationKind.NightlifeVendingAndQueue:
                        return 0.45f;
                    case CityDecorationKind.RoadsideDumpsterAndUtility:
                        return 0.72f;
                    case CityDecorationKind.RoadsidePhoneBooth:
                        return 0.82f;
                    case CityDecorationKind.RoadsideRoadworkAndBicycle:
                        return 0.44f;
                    default:
                        return 1f;
                }
            }

            private static bool IsFinite(float value)
            {
                return !float.IsNaN(value) && !float.IsInfinity(value);
            }
        }
    }
}

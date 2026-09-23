using System;
using UnityEngine;

namespace BarPromenade
{
    internal static class AlpineVillageExpansionBuilder
    {
        public const string RootName = "Village Expansion";

        public static void Build(Transform parent, AlpineVillagePlan plan)
        {
            if (parent == null) throw new ArgumentNullException(nameof(parent));
            if (plan == null) throw new ArgumentNullException(nameof(plan));
            AlpineVillageExpansionPlan expansion = plan.Expansion;
            var root = new GameObject(RootName);
            root.transform.SetParent(parent, false);
            VillageExpansionAssetProvider assets = VillageExpansionAssetProvider.LoadOrThrow();
            Quaternion facing = Quaternion.LookRotation(expansion.LodgeForward, Vector3.up);
            assets.Create("SkiLodge", "Ski Lodge", root.transform, expansion.LodgeCenter, facing);
            assets.Create("ServiceShed", "Service Shed", root.transform, expansion.ServiceShedCenter, facing);
            assets.Create("LiftBase", "Old Tow Base", root.transform,
                OnGround(plan, expansion.LiftBasePosition), facing);
            assets.Create("LiftTop", "Old Tow Top", root.transform,
                OnGround(plan, expansion.LiftTopPosition), facing);
            assets.Create("RoadBarrier", "Old Road Blockage", root.transform,
                OnGround(plan, expansion.CliffBarrierCenter), facing);
            assets.Create("RoadBrokenLip", "Broken Road Edge", root.transform,
                OnGround(plan, expansion.CliffEdge) + Vector3.up * .075f, facing);
            assets.Create("TradeWarehouse", "Former Trade Warehouse", root.transform,
                expansion.WarehouseCenter, facing);
            assets.Create("TradeYardProps", "Unused Cargo Equipment", root.transform,
                expansion.YardPropsCenter, facing);
            assets.Create("RustedTruck", "Abandoned Truck Wreck", root.transform,
                expansion.TruckWreckCenter, facing * Quaternion.Euler(0f, 90f, 0f));
            assets.Create("DiscardedChairPile", "Discarded Wooden Chairs", root.transform,
                expansion.ChairPileCenter, facing);
            assets.Create("ConservedRepair", "Conserved Road Repair", root.transform,
                OnGround(plan, expansion.CliffEdge), facing);

            var road = new GameObject("Former City Road");
            road.transform.SetParent(root.transform, false);
            foreach (AlpineVillagePathDescriptor path in expansion.Paths)
                if (path.Kind == AlpineVillagePathKind.AbandonedRoad)
                    BuildRoad(assets, road.transform, plan, path.Start, path.End, path.SurfaceHalfWidth * 2f);
            // The same width and axis continue across a thirteen-metre missing
            // shelf. The opposite road is scenery, absent from the walking graph.
            BuildRoad(assets, road.transform, plan, expansion.ToWorld(new Vector2(-130f, -52f)),
                expansion.CliffEdge, expansion.RoadWidth);
            Vector3 previous = expansion.FarRoadEdge;
            for (float along = -69f; along >= -107f; along -= 2f)
            {
                Vector3 next = expansion.FarRoadPoint(along);
                BuildRoad(assets, road.transform, plan, previous, next, expansion.RoadWidth);
                previous = next;
            }
            for (float along = -69f; along >= -89f; along -= 4f)
            {
                Vector3 first = expansion.FarRoadPoint(along + 2f);
                Vector3 next = expansion.FarRoadPoint(along - 2f);
                Vector3 direction = (next - first).normalized;
                Vector3 side = Vector3.Cross(Vector3.up, direction).normalized;
                foreach (float sign in new[] { -1f, 1f })
                    assets.Create("RoadsideRail", "Opposite Road Guardrail", road.transform,
                        (first + next) * .5f + side * (sign * 2.95f),
                        Quaternion.LookRotation(direction, Vector3.up),
                        new Vector3(1f, 1f, Vector3.Distance(first, next) / 4f));
            }
            assets.Create("RoadBrokenLip", "Opposite Broken Road Edge", root.transform,
                OnGround(plan, expansion.FarRoadEdge) + Vector3.up * .075f,
                facing * Quaternion.Euler(0f, 180f, 0f));
            AlpineVillageDistanceWorldBuilder.Build(root.transform, plan);
        }

        private static void BuildRoad(VillageExpansionAssetProvider assets, Transform parent,
            AlpineVillagePlan plan, Vector3 start, Vector3 end, float width)
        {
            // Short rigid authored strips conform to the actual sampler, including the cliff cut.
            // No runtime primitive geometry or new material is created.
            int steps = Mathf.Max(1, Mathf.CeilToInt(Vector3.Distance(start, end) / 1.5f));
            Vector3 first = OnGround(plan, start);
            for (int i = 1; i <= steps; i++)
            {
                Vector3 next = OnGround(plan, Vector3.Lerp(start, end, i / (float)steps));
                Vector3 direction = next - first;
                Vector3 center = (first + next) * .5f + Vector3.up * .065f;
                assets.Create("RoadSurface", "Old Asphalt", parent, center,
                    Quaternion.LookRotation(direction.normalized, Vector3.up),
                    new Vector3(width, 1f, direction.magnitude + .025f));
                first = next;
            }
        }

        private static Vector3 OnGround(AlpineVillagePlan plan, Vector3 point)
        {
            point.y = AlpineVillageTerrainSampler.SampleHeight(plan, new Vector2(point.x, point.z));
            return point;
        }
    }
}

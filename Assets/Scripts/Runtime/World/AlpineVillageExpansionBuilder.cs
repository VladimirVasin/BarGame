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

            var road = new GameObject("Former City Road");
            road.transform.SetParent(root.transform, false);
            foreach (AlpineVillagePathDescriptor path in expansion.Paths)
                if (path.Kind == AlpineVillagePathKind.AbandonedRoad)
                    BuildRoad(assets, road.transform, plan, path.Start, path.End, path.SurfaceHalfWidth * 2f);
            // The old strip reaches the blocked lip. The lower continuation is visible
            // inside the rock cut, well below the inaccessible edge.
            BuildRoad(assets, road.transform, plan, expansion.ToWorld(new Vector2(-130f, -52f)),
                expansion.CliffEdge, 5.4f);
            Vector3 lowerStart = expansion.ToWorld(new Vector2(-130f, -64f));
            Vector3 lowerEnd = expansion.ToWorld(new Vector2(-123f, -88f));
            BuildRoad(assets, road.transform, plan, lowerStart, lowerEnd, 5.4f);
            assets.Create("RoadBrokenLip", "Lower Broken Road Edge", root.transform,
                OnGround(plan, lowerStart) + Vector3.up * .075f, facing * Quaternion.Euler(0f, 180f, 0f));
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

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
            AlpineVillageFootbridgePlan footbridge = AlpineVillageFootbridgePlan.Create(plan);
            if (footbridge != null)
            {
                GameObject bridge = assets.Create("BrookFootbridge", AlpineVillageFootbridgePlan.ObjectName, root.transform,
                    footbridge.Position, footbridge.Rotation,
                    new Vector3(1f, 1f, footbridge.Length / AlpineVillageFootbridgePlan.ModelLength));
                FitFootbridgeApproaches(bridge, plan);
            }
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

            var settlement = new GameObject(AlpineVillageAbandonmentPlan.RootName);
            settlement.transform.SetParent(root.transform, false);
            foreach (AlpineVillageAbandonedPlot plot in expansion.Abandonment.Plots)
            {
                GameObject building = assets.Create(plot.Model, plot.Id, settlement.transform,
                    plot.GroundCenter, plot.Rotation);
                if (!string.IsNullOrEmpty(plot.Yard))
                    assets.Create(plot.Yard, "Former Household Yard", building.transform,
                        plot.GroundCenter, plot.Rotation);
            }

            var road = new GameObject("Former City Road");
            road.transform.SetParent(root.transform, false);
            // Asphalt belongs to the ground material partition, including the
            // inaccessible opposite shelf. Only actual structures have volume.
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

        private static void FitFootbridgeApproaches(GameObject bridge, AlpineVillagePlan plan)
        {
            // Keep the authored topology and thick deck. Only the ends bend
            // down to their own bank, instead of floating when the bearing
            // height needed above the stream is higher than the path surface.
            Transform deck = bridge.transform.Find("DeckAndApproaches");
            MeshFilter filter = deck.GetComponent<MeshFilter>();
            Mesh mesh = UnityEngine.Object.Instantiate(filter.sharedMesh);
            mesh.name = "Bank-fitted Footbridge Deck";
            Vector3[] vertices = mesh.vertices;
            for (int index = 0; index < vertices.Length; index++)
            {
                Vector3 world = deck.TransformPoint(vertices[index]);
                Vector3 local = bridge.transform.InverseTransformPoint(world);
                float blend = Mathf.Clamp01((Mathf.Abs(local.z) - 1.8f) / .6f);
                if (blend <= 0f) continue;
                Vector3 toe = bridge.transform.TransformPoint(
                    new Vector3(local.x, 0f, Mathf.Sign(local.z) * AlpineVillageFootbridgePlan.ModelLength * .5f));
                Vector2 point = new Vector2(toe.x, toe.z);
                float height = Mathf.Max(AlpineVillageTerrainSampler.SampleHeight(plan, point),
                    AlpineVillageTerrainSampler.SampleMeshHeight(plan, point)) +
                    AlpineVillageWorldBuilder.LaneSkinLift;
                world.y += (height - toe.y) * blend;
                vertices[index] = deck.InverseTransformPoint(world);
            }
            mesh.vertices = vertices;
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            filter.sharedMesh = mesh;
            deck.GetComponent<MeshCollider>().sharedMesh = mesh;
            deck.gameObject.AddComponent<RuntimeGeneratedMeshOwner>().Initialize(mesh);
        }

        private static Vector3 OnGround(AlpineVillagePlan plan, Vector3 point)
        {
            point.y = AlpineVillageTerrainSampler.SampleHeight(plan, new Vector2(point.x, point.z));
            return point;
        }
    }
}

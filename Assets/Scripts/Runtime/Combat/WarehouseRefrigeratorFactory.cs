using System;
using UnityEngine;

namespace BarPromenade
{
    /// <summary>The exact Home cabinet meshes, with warehouse-local finish and finite stock.</summary>
    public static class WarehouseRefrigeratorFactory
    {
        public static WarehouseRefrigerator Create(Transform parent, CombatTestRoot owner, int floor,
            Vector3 position, float yaw, string sourceId)
        {
            if (parent == null || owner == null) throw new ArgumentNullException();
            if (floor < 0 || floor > 2 || string.IsNullOrWhiteSpace(sourceId))
                throw new ArgumentOutOfRangeException(nameof(floor));
            var host = new GameObject("Warehouse Refrigerator");
            host.transform.SetParent(parent, false);
            host.transform.localPosition = position;
            host.transform.localRotation = Quaternion.Euler(0f, yaw, 0f);
            try
            {
                HomeRefrigeratorItemKind kind = floor == 0 ? HomeRefrigeratorItemKind.RottenFishTailCan :
                    floor == 1 ? HomeRefrigeratorItemKind.RottenFishBellyCan : HomeRefrigeratorItemKind.RottenFishHeadCan;
                HomeRefrigeratorPlan plan = HomeRefrigeratorPlan.CreatePlaced(kind, PlayerFactory.GroundedRootOffset);
                HomeRefrigeratorView view = HomeRefrigeratorWorldBuilder.Build(host.transform, plan, slot => sourceId);
                ApplyWarehouseFinish(view);
                var target = host.AddComponent<WarehouseRefrigerator>();
                target.Initialize(owner, floor, sourceId, plan, view);
                return target;
            }
            catch
            {
                host.SetActive(false);
                if (Application.isPlaying) UnityEngine.Object.Destroy(host);
                else UnityEngine.Object.DestroyImmediate(host);
                throw;
            }
        }

        private static void ApplyWarehouseFinish(HomeRefrigeratorView view)
        {
            foreach (MeshRenderer renderer in view.GetComponentsInChildren<MeshRenderer>(true))
            {
                if (renderer == view.InteriorLightStrip ||
                    renderer.GetComponentInParent<HomeRefrigeratorItemView>() != null) continue;
                string name = renderer.name;
                string role = name.Contains("Gasket") ? "Rubber" :
                    name.Contains("Rust") || name.Contains("Grime") || name.Contains("Stain") ||
                    name.Contains("Spill") || name.Contains("Drip") ? "Rust" :
                    name.Contains("Handle") || name.Contains("Hinge") || name.Contains("Rail") ? "WornSteel" : "Steel";
                renderer.sharedMaterial = WarehouseArenaAssetProvider.GetPropSurface(role);
                // Home's surface maps/tints live in property blocks. Clear the whole
                // block so that they cannot override the shared warehouse finish.
                renderer.SetPropertyBlock(null);
            }
        }
    }
}

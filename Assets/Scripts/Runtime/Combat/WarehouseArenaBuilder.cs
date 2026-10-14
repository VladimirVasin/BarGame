using System;
using UnityEngine;

namespace BarPromenade
{
    public static class WarehouseArenaBuilder
    {
        public static GameObject Create(WarehouseArenaPlan plan, Transform parent)
        {
            if (plan == null) throw new ArgumentNullException(nameof(plan));
            var host = new GameObject("Warehouse Shift");
            host.transform.SetParent(parent, false);
            try
            {
                foreach (WarehouseArenaPlacement part in plan.ModulePlacements)
                    WarehouseArenaAssetProvider.Create(part.Kind, host.transform, part.Position, Quaternion.Euler(0f, part.Yaw, 0f));
                foreach (WarehouseArenaStair stair in plan.Stairs)
                    WarehouseArenaAssetProvider.Create("Stair", host.transform, stair.Foot, Quaternion.identity);
                foreach (WarehouseArenaRailSpan span in plan.RailSpans)
                {
                    GameObject rail = WarehouseArenaAssetProvider.Create("Rail", host.transform, span.Position, Quaternion.Euler(0f, span.Yaw, 0f));
                    // Only the metre wrapper changes; the imported root retains its FBX unit factor.
                    rail.transform.localScale = new Vector3(span.Length, 1f, 1f);
                }
                foreach (WarehouseArenaLamp lamp in plan.Lamps)
                {
                    WarehouseArenaAssetProvider.Create("Lamp", host.transform, lamp.Position,
                        Quaternion.FromToRotation(Vector3.down, lamp.Direction));
                    WarehouseArenaLighting.CreatePractical(lamp, host.transform);
                }
                host.AddComponent<WarehouseArenaMechanisms>().Initialize(plan, parent.GetComponent<CombatTestRoot>());
                return host;
            }
            catch
            {
                host.SetActive(false);
                if (Application.isPlaying) UnityEngine.Object.Destroy(host);
                else UnityEngine.Object.DestroyImmediate(host);
                throw;
            }
        }
    }
}

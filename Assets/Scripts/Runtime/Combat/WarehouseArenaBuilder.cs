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
                CombatTestRoot owner = parent.GetComponent<CombatTestRoot>();
                foreach (WarehouseArenaPlacement part in plan.ModulePlacements)
                {
                    GameObject model = WarehouseArenaAssetProvider.Create(part.Kind, host.transform, part.Position,
                        Quaternion.Euler(0f, part.Yaw, 0f));
                    if (part.Kind == "GateControl") WarehouseValveInspection.Create(model, plan, owner);
                }
                foreach (WarehouseArenaStair stair in plan.Stairs)
                    WarehouseArenaAssetProvider.Create("Stair", host.transform, stair.Foot, Quaternion.identity);
                foreach (WarehouseArenaRailSpan span in plan.RailSpans)
                {
                    GameObject rail = WarehouseArenaAssetProvider.Create("Rail", host.transform, span.Position, Quaternion.Euler(0f, span.Yaw, 0f));
                    // Only the metre wrapper changes; the imported root retains its FBX unit factor.
                    rail.transform.localScale = new Vector3(span.Length, 1f, 1f);
                }
                for (int lampIndex = 0; lampIndex < plan.Lamps.Count; lampIndex++)
                {
                    WarehouseArenaLamp lamp = plan.Lamps[lampIndex];
                    GameObject fixture = WarehouseArenaAssetProvider.Create(lamp.Kind, host.transform,
                        lamp.Position, lamp.ModelRotation);
                    if (lamp.Kind == "EmergencyRoomLamp") WarehouseArenaAssetProvider.SetSignalLight(fixture, lamp.Enabled);
                    if (lamp.Enabled)
                    {
                        Light light = WarehouseArenaLighting.CreatePractical(lamp,
                            WarehouseArenaAssetProvider.FindAnchor(fixture, "Light"),
                            lamp.Kind == "FishStationLamp" || lamp.Kind == "EmergencyRoomLamp" ? fixture.transform : host.transform);
                        if (lamp.Kind == "FishStationLamp")
                            fixture.AddComponent<WarehouseFishLamp>().Initialize(fixture, light, owner);
                        else if (lamp.Kind == "EmergencyRoomLamp")
                            fixture.AddComponent<WarehouseRoomIndicator>().Initialize(fixture, light, owner,
                                plan.Refrigerators[(lampIndex - 6) / 3].SourceId);
                    }
                }
                foreach (WarehouseRefrigeratorPlacement fridge in plan.Refrigerators)
                    WarehouseRefrigeratorFactory.Create(host.transform, owner, fridge.Floor, fridge.Position,
                        fridge.Yaw, fridge.SourceId);
                host.AddComponent<WarehouseArenaMechanisms>().Initialize(plan, owner);
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

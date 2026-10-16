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
                GameObject gate = null, door = null, control = null;
                int roomDetailIndex = (int)(unchecked((uint)plan.Seed) % 3u);
                foreach (WarehouseArenaPlacement part in plan.ModulePlacements)
                {
                    bool gateDeck = part.Kind == "Platform" && Mathf.Abs(part.Position.y - 3.2f) < .01f &&
                        Mathf.Abs(part.Position.z - 10.5f) < .01f && Mathf.Abs(Mathf.Abs(part.Position.x) - 1.5f) < .01f;
                    GameObject model = gateDeck
                        ? WarehouseGateAssets.Create(part.Position.x < 0f ? "GatePlatformLeft" : "GatePlatformRight",
                            host.transform, part.Position, Quaternion.Euler(0f, part.Yaw, 0f))
                        : WarehouseArenaAssetProvider.Create(part.Kind, host.transform, part.Position, Quaternion.Euler(0f, part.Yaw, 0f));
                    if (part.Kind == "TechnicalProps" || part.Kind == "LockerProps" || part.Kind == "BreakProps")
                        SelectRoomDetails(model.transform, roomDetailIndex++ % 3);
                    if (part.Kind == "GateControl") control = model;
                    if (part.Kind == "Gate") gate = model;
                    if (part.Kind == "MainDoor") door = model;
                    if (part.Kind == "FishPreparationStation") WarehouseFishAssembly.Create(model, plan, owner);
                }
                WarehouseValveInspection.Create(control, plan, owner);
                control.GetComponent<WarehouseGateController>().BindWorld(gate, door, plan, owner);
                WarehouseEntranceDoor.Create(plan, host.transform, owner);
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
                host.AddComponent<WarehouseFishCratePopulation>().Initialize(plan);
                host.AddComponent<WarehouseArenaMechanisms>().Initialize(plan, owner);
                host.GetComponentInChildren<WarehouseFishLamp>().BindWarehouseLighting(host.transform);
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

        private static void SelectRoomDetails(Transform root, int variant)
        {
            // Only room dressing varies. This independent stream leaves cargo,
            // refrigerators, navigation and the eighteen mimic slots unchanged.
            string selected = "RoomVariant" + variant + "__";
            foreach (Renderer renderer in root.GetComponentsInChildren<Renderer>(true))
                if (renderer.name.StartsWith("RoomVariant", StringComparison.Ordinal))
                    renderer.gameObject.SetActive(renderer.name.StartsWith(selected, StringComparison.Ordinal));
        }
    }
}

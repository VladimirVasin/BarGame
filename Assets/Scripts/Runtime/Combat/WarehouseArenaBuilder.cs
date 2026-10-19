using System;
using System.Collections.Generic;
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
                Light valveLamp = null;
                Transform valveLampFixture = null;
                int roomDetailIndex = (int)(unchecked((uint)plan.Seed) % 3u);
                var surfaceOrdinals = new Dictionary<string, int>(StringComparer.Ordinal);
                foreach (WarehouseArenaPlacement part in plan.ModulePlacements)
                {
                    int surfaceSlot = NextSurfaceSlot(part.Kind, surfaceOrdinals);
                    if (part.Kind == "LockerProps")
                    {
                        WarehouseLockerFactory.Create(host.transform, owner, part.Position, part.Yaw);
                        // Keep the room detail stream independent of which passive models are replaced.
                        roomDetailIndex++;
                        continue;
                    }
                    // The bridge crane replaces the two decorative bent frames
                    // wherever the encounter includes it; all cargo/routes stay put.
                    if (owner != null && owner.HasCraneOpponent && part.Kind == "TwistedFrame") continue;
                    bool gateDeck = part.Kind == "Platform" && Mathf.Abs(part.Position.y - 3.2f) < .01f &&
                        Mathf.Abs(part.Position.z - 10.5f) < .01f && Mathf.Abs(Mathf.Abs(part.Position.x) - 1.5f) < .01f;
                    GameObject model = gateDeck
                        ? WarehouseGateAssets.Create(part.Position.x < 0f ? "GatePlatformLeft" : "GatePlatformRight",
                            host.transform, part.Position, Quaternion.Euler(0f, part.Yaw, 0f))
                        : WarehouseArenaAssetProvider.Create(part.Kind, host.transform, part.Position,
                            Quaternion.Euler(0f, part.Yaw, 0f), surfaceSlot);
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
                {
                    int surfaceSlot = NextSurfaceSlot("Stair", surfaceOrdinals);
                    if (stair.IsBroken) WarehouseBrokenStairFactory.Create(host.transform, stair.Foot);
                    else WarehouseArenaAssetProvider.Create("Stair", host.transform, stair.Foot, Quaternion.identity, surfaceSlot);
                }
                foreach (WarehouseArenaRailSpan span in plan.RailSpans)
                {
                    GameObject rail = WarehouseArenaAssetProvider.Create("Rail", host.transform, span.Position,
                        Quaternion.Euler(0f, span.Yaw, 0f), NextSurfaceSlot("Rail", surfaceOrdinals));
                    // Only the metre wrapper changes; the imported root retains its FBX unit factor.
                    rail.transform.localScale = new Vector3(span.Length, 1f, 1f);
                }
                for (int lampIndex = 0; lampIndex < plan.Lamps.Count; lampIndex++)
                {
                    WarehouseArenaLamp lamp = plan.Lamps[lampIndex];
                    GameObject fixture = WarehouseArenaAssetProvider.Create(lamp.Kind, host.transform,
                        lamp.Position, lamp.ModelRotation, NextSurfaceSlot(lamp.Kind, surfaceOrdinals));
                    if (lamp.Kind == "EmergencyRoomLamp") WarehouseArenaAssetProvider.SetSignalLight(fixture, lamp.Enabled);
                    if (lamp.Enabled)
                    {
                        Light light = WarehouseArenaLighting.CreatePractical(lamp,
                            WarehouseArenaAssetProvider.FindAnchor(fixture, "Light"),
                            lamp.Kind == "FishStationLamp" || lamp.Kind == "EmergencyRoomLamp" ? fixture.transform : host.transform);
                        // The plan's goal fixture is independent of the processing-station circuit.
                        if (lamp.Kind == "EmergencyLamp")
                        {
                            valveLamp = light; valveLampFixture = fixture.transform;
                            fixture.AddComponent<WarehouseValveLampFlicker>().Initialize(fixture, light, owner);
                        }
                        else if (lamp.Kind == "FishStationLamp")
                            fixture.AddComponent<WarehouseFishLamp>().Initialize(fixture, light, owner);
                        else if (lamp.Kind == "EmergencyRoomLamp")
                            fixture.AddComponent<WarehouseRoomIndicator>().Initialize(fixture, light, owner,
                                plan.Refrigerators[(lampIndex - 6) / 3].SourceId);
                    }
                }
                foreach (WarehouseRefrigeratorPlacement fridge in plan.Refrigerators)
                    WarehouseRefrigeratorFactory.Create(host.transform, owner, fridge.Floor, fridge.Position,
                        fridge.Yaw, fridge.SourceId);
                foreach (WarehouseLockerPlacement locker in plan.HallwayLockers)
                    WarehouseLockerFactory.Create(host.transform, owner, locker.Position, locker.Yaw);
                host.AddComponent<WarehouseFishCratePopulation>().Initialize(plan);
                var mechanisms = host.AddComponent<WarehouseArenaMechanisms>();
                mechanisms.Initialize(plan, owner);
                host.GetComponentInChildren<WarehouseFishLamp>().BindWarehouseLighting(host.transform,
                    valveLamp, valveLampFixture);
                host.AddComponent<WarehouseAtmosphere>().Initialize(plan, owner, mechanisms);
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

        private static int NextSurfaceSlot(string kind, Dictionary<string, int> ordinals)
        {
            ordinals.TryGetValue(kind, out int ordinal);
            ordinals[kind] = ordinal + 1;
            if (Array.IndexOf(WarehouseArenaAssetProvider.FishModelNames, kind) >= 0) return 0;
            int count = WarehouseArenaAssetProvider.SurfaceSlotCount(kind);
            if (ordinal >= count)
                throw new InvalidOperationException("Warehouse surface layout is stale for " + kind + "; export and author its placement slots again.");
            return ordinal;
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

using System;
using UnityEngine;

namespace BarPromenade
{
    public sealed partial class CombatTestRoot
    {
        /// <summary>
        /// Debug launch permanently completes the same finite prerequisites as
        /// ordinary play. It neither borrows an open gate nor rolls progress back.
        /// </summary>
        internal void PrepareDoorAmbushDebugProgress()
        {
            if (!IsInitialized || WarehousePlan == null || combatLocation == null)
                throw new InvalidOperationException("Door ambush preparation requires the loaded warehouse.");
            WarehouseFishAssembly assembly = combatLocation.GetComponentInChildren<WarehouseFishAssembly>(true);
            WarehouseGateController gate = combatLocation.GetComponentInChildren<WarehouseGateController>(true);
            WarehouseFishLamp lamp = combatLocation.GetComponentInChildren<WarehouseFishLamp>(true);
            WarehouseArenaMechanisms mechanisms = combatLocation.GetComponentInChildren<WarehouseArenaMechanisms>(true);
            if (assembly == null || gate == null || lamp == null || mechanisms == null)
                throw new InvalidOperationException("Door ambush prerequisites need their existing warehouse owners.");

            foreach (WarehouseRefrigeratorPlacement refrigerator in WarehousePlan.Refrigerators)
            {
                InventoryItemId item = refrigerator.Floor switch
                {
                    0 => InventoryItemId.RottenFishTailCan,
                    1 => InventoryItemId.RottenFishBellyCan,
                    2 => InventoryItemId.RottenFishHeadCan,
                    _ => throw new InvalidOperationException("Unexpected warehouse refrigerator floor.")
                };
                if (!GameSessionState.IsWorldItemCollected(refrigerator.SourceId) &&
                    !GameSessionState.TryCollectWorldItem(refrigerator.SourceId, item))
                    throw new InvalidOperationException("A warehouse fish can could not be collected.");
            }
            RemoveCommittedWarehouseCanModels();

            WarehouseFishAssemblyState fish = GameSessionState.WarehouseFishAssembly;
            if (!fish.HasTransferredCans && !GameSessionState.TryBeginWarehouseFishAssembly())
                throw new InvalidOperationException("The warehouse fish cans could not be transferred.");
            if (fish.Phase == WarehouseFishAssemblyPhase.Pouring)
                fish.Advance(WarehouseFishAssemblyState.PourSeconds);
            if (fish.Phase == WarehouseFishAssemblyPhase.Assembling)
            {
                WarehouseFishPartPose belly = fish.GetPose(WarehouseFishPart.Belly);
                float yaw = belly.Yaw * Mathf.Deg2Rad;
                for (int i = 0; i < 2; i++)
                {
                    WarehouseFishPart part = i == 0 ? WarehouseFishPart.Tail : WarehouseFishPart.Head;
                    if (fish.IsPartLinked(part)) continue;
                    float offset = (i == 0 ? -1f : 1f) * WarehouseFishAssemblyState.PartSpacing;
                    fish.SetPose(part, new WarehouseFishPartPose(belly.X + Mathf.Cos(yaw) * offset,
                        belly.Z - Mathf.Sin(yaw) * offset, belly.Yaw));
                }
                fish.TrySnap(WarehouseFishPart.Belly);
            }
            if (fish.Phase == WarehouseFishAssemblyPhase.Convulsing || fish.Phase == WarehouseFishAssemblyPhase.Spitting)
                fish.Advance(WarehouseFishAssemblyState.ConvulsionSeconds + WarehouseFishAssemblyState.SpitSeconds);
            if (!GameSessionState.TryGrantWarehouseFishKey())
                throw new InvalidOperationException("The completed warehouse fish could not grant its key.");
            if (fish.ReceiptPending) fish.MarkReceiptPresented();
            assembly.RestoreRetainedProgressForDebug();

            WarehouseFishLampState failure = GameSessionState.WarehouseFishLamp;
            if (failure.Phase == WarehouseFishLampPhase.Waiting) failure.TryBegin(true, true);
            if (failure.Phase == WarehouseFishLampPhase.Heating) failure.Advance(WarehouseFishLampState.HeatingSeconds);
            lamp.RestoreRetainedProgressForDebug();
            mechanisms.FinishShutdownForDebug();

            if (!GameSessionState.TryUnlockWarehouseGate())
                throw new InvalidOperationException("The warehouse fish key could not unlock the gate.");
            GameSessionState.WarehouseGate.Advance(WarehouseGateState.TurnSeconds, true);
            gate.RestoreRetainedProgressForDebug();
            Physics.SyncTransforms();
        }

        private void RemoveCommittedWarehouseCanModels()
        {
            foreach (WarehouseRefrigerator refrigerator in combatLocation.GetComponentsInChildren<WarehouseRefrigerator>(true))
            {
                HomeRefrigeratorView view = refrigerator.View;
                if (view == null) continue;
                for (int i = view.Items.Count - 1; i >= 0; i--)
                {
                    HomeRefrigeratorItemView item = view.Items[i];
                    if (!GameSessionState.IsWorldItemCollected(item.SourceId)) continue;
                    if (!view.RemoveItem(item))
                        throw new InvalidOperationException("A collected warehouse can was not registered in its refrigerator.");
                    item.gameObject.SetActive(false);
                    Destroy(item.gameObject);
                }
            }
        }
    }
}

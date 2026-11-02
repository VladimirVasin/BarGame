using UnityEngine;

namespace BarPromenade
{
    /// <summary>The existing authored apartment leaf, kept closed on the warehouse's loading wall.</summary>
    public static class WarehouseEntranceDoor
    {
        public static GameObject Create(WarehouseArenaPlan plan, Transform parent, CombatTestRoot owner)
        {
            GameObject door = ApartmentEntranceDoor.Create(parent, plan.EntranceDoorPosition, "Warehouse Entrance Door");
            var host = new GameObject("Warehouse Entrance Door Inspection");
            host.transform.SetParent(door.transform, false);
            var inspection = host.AddComponent<NarrativeInteraction>();
            MeshRenderer[] renderers = door.GetComponentsInChildren<MeshRenderer>();
            Bounds bounds = renderers[0].bounds;
            foreach (MeshRenderer renderer in renderers)
                bounds.Encapsulate(renderer.bounds);
            Vector3 floor = parent.TransformPoint(plan.EntranceDoorApproach);
            Quaternion facing = parent.rotation * Quaternion.Euler(0f, 180f, 0f);
            inspection.Configure(new NarrativeInteractionDefinition("warehouse-entrance-door", "interaction.warehouse_door",
                new[] { new NarrativePage("combat.warehouse.entrance_door.closed", compact: true) }, requiresRunningGameTime: false),
                door.transform, NarrativeStagingPlan.Standing(floor, facing, bounds, parent.right,
                    authoredFocusBoundsOnly: true), floor + parent.up * .85f, 1.8f);
            inspection.Admission = interactor => owner != null && owner.IsInitialized &&
                owner.Player.Interactor == interactor && owner.Hero != null && owner.Hero.CanBeginStandingInspection;
            bool ownsFocus = false, previousFocus = false;
            inspection.PreparingPlayer += interactor =>
            { previousFocus = owner.Hero.CombatFocused; ownsFocus = true; owner.Hero.PrepareStandingInspection(); };
            inspection.RestoringPlayer += interactor =>
            {
                if (!ownsFocus) return;
                ownsFocus = false;
                if (owner != null && owner.Hero != null) owner.Hero.RestoreStandingInspectionFocus(previousFocus);
            };
            return door;
        }
    }
}

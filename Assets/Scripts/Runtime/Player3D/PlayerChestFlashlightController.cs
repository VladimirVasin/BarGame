using UnityEngine;

namespace BarPromenade
{
    /// <summary>One session-owned chest lamp on the production hero, allocated only when needed.</summary>
    [DisallowMultipleComponent]
    public sealed class PlayerChestFlashlightController : MonoBehaviour
    {
        private Player3DCharacterPresentation presentation;
        private bool presentationSuppressed;
        private bool warehouseVisibility;

        public CombatChestFlashlight Flashlight { get; private set; }

        public static PlayerChestFlashlightController Install(PlayerRuntime runtime)
        {
            if (!(runtime.Visual is Player3DCharacterPresentation visual)) return null;
            var owner = runtime.GameObject.AddComponent<PlayerChestFlashlightController>();
            owner.presentation = visual;
            GameSessionState.InventoryEquipmentChanged += owner.RefreshEquipment;
            owner.RefreshEquipment();
            return owner;
        }

        public CombatChestFlashlight EnsureInitialized()
        {
            if (Flashlight != null) return Flashlight;
            if (presentation == null) return null;
            Flashlight = gameObject.AddComponent<CombatChestFlashlight>();
            // Prepare the actual source while disabled, so an unequipped lamp
            // never lights even the first frame after a scene load.
            Flashlight.enabled = false;
            Flashlight.SetWarehouseVisibility(warehouseVisibility);
            Flashlight.Initialize(presentation);
            RefreshEquipment();
            return Flashlight;
        }

        public void SetWarehouseVisibility(bool value)
        {
            warehouseVisibility = value;
            if (Flashlight != null) Flashlight.SetWarehouseVisibility(value);
        }

        public void SetPresentationSuppressed(bool value)
        {
            presentationSuppressed = value;
            RefreshEquipment();
        }

        public void RefreshEquipment()
        {
            bool equipped = isActiveAndEnabled && !presentationSuppressed &&
                GameSessionState.IsInventoryItemEquipped(InventoryItemId.ChestFlashlight);
            if (Flashlight == null)
            {
                if (equipped) EnsureInitialized();
                return;
            }
            Flashlight.enabled = equipped;
        }

        private void OnEnable()
        {
            if (presentation != null) RefreshEquipment();
        }

        private void OnDisable()
        {
            if (Flashlight != null) Flashlight.enabled = false;
        }

        private void OnDestroy()
        {
            GameSessionState.InventoryEquipmentChanged -= RefreshEquipment;
            if (Flashlight != null) Destroy(Flashlight);
        }
    }
}

using System;
using UnityEngine;

namespace BarPromenade
{
    /// <summary>Retained cabinet bank; each compartment owns its own door interaction.</summary>
    [DisallowMultipleComponent]
    public sealed class WarehouseLocker : MonoBehaviour
    {
        public const int DoorCount = 3;
        private readonly WarehouseLockerDoor[] doors = new WarehouseLockerDoor[DoorCount];
        private readonly Transform[] shelves = new Transform[DoorCount];

        // Retain the middle-shelf authoring/test API without a bank-wide target.
        public bool IsOpen => doors[1] != null && doors[1].IsOpen;
        public bool IsMoving => doors[1] != null && doors[1].IsMoving;
        public float DoorOpenAmount => doors[1] != null ? doors[1].DoorOpenAmount : 0f;
        public string PromptKey => GetDoor(1).PromptKey;
        public Vector3 InteractionPosition => GetDoor(1).InteractionPosition;

        internal void Initialize(CombatTestRoot owner)
        {
            for (int bay = 0; bay < DoorCount; bay++)
            {
                Transform pivot = WarehouseArenaAssetProvider.FindAnchor(gameObject, "LockerDoor" + bay);
                shelves[bay] = WarehouseArenaAssetProvider.FindAnchor(gameObject, "Shelf" + bay);
                var target = new GameObject("Locker door " + (bay + 1));
                target.transform.SetParent(transform, false);
                target.transform.localPosition = new Vector3((bay - 1) * .74f, 0f, 0f);
                var trigger = target.AddComponent<BoxCollider>();
                trigger.isTrigger = true;
                trigger.center = new Vector3(0f, .95f, -.70f);
                trigger.size = new Vector3(.74f, 1.9f, 1.15f);
                doors[bay] = target.AddComponent<WarehouseLockerDoor>();
                doors[bay].Initialize(this, owner, bay, pivot);
            }
        }

        public WarehouseLockerDoor GetDoor(int bay)
        {
            if (bay < 0 || bay >= DoorCount) throw new ArgumentOutOfRangeException(nameof(bay));
            return doors[bay];
        }

        public Vector3 ShelfPosition(int bay = 1)
        {
            if (bay < 0 || bay >= DoorCount) throw new ArgumentOutOfRangeException(nameof(bay));
            return shelves[bay].position;
        }

        public Transform DoorPivot(int bay) => GetDoor(bay).Pivot;

        public void BindAmmo(CombatAmmoPickup pickup, int bay = -1)
        {
            if (pickup == null) throw new ArgumentNullException(nameof(pickup));
            if (bay == -1)
            {
                float nearest = float.PositiveInfinity;
                for (int candidate = 0; candidate < DoorCount; candidate++)
                {
                    float distance = (ShelfPosition(candidate) - pickup.transform.position).sqrMagnitude;
                    if (distance < nearest) { nearest = distance; bay = candidate; }
                }
            }
            WarehouseLockerDoor source = GetDoor(bay);
            for (int candidate = 0; candidate < DoorCount; candidate++)
                if (candidate != bay) doors[candidate].UnbindAmmo(pickup);
            source.BindAmmo(pickup);
        }

        public bool CanInteract(PlayerInteractor interactor) => GetDoor(1).CanInteract(interactor);
        public void Interact(PlayerInteractor interactor) => GetDoor(1).Interact(interactor);
        public bool BeginOpen(int bay = 1) => GetDoor(bay).BeginOpen();
        public bool BeginClose(int bay = 1) => GetDoor(bay).BeginClose();

        public void Advance(float seconds)
        {
            for (int bay = 0; bay < DoorCount; bay++)
                if (doors[bay] != null) doors[bay].Advance(seconds);
        }

        public void ResetState()
        {
            for (int bay = 0; bay < DoorCount; bay++)
                if (doors[bay] != null) doors[bay].ResetState();
        }

        private void Update() => Advance(Time.deltaTime);
        private void OnDisable() => ResetState();
        private void OnDestroy() => ResetState();
    }
}

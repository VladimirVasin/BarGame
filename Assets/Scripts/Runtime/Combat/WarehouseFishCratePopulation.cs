using System;
using UnityEngine;

namespace BarPromenade
{
    /// <summary>Bounded passive fish cargo. Occupied anchors substitute one live mimic for the same crate.</summary>
    public sealed class WarehouseFishCratePopulation : MonoBehaviour
    {
        private WarehouseArenaPlan plan;
        private GameObject[] crates = Array.Empty<GameObject>();
        private bool[] occupied = Array.Empty<bool>();

        public void Initialize(WarehouseArenaPlan source)
        {
            if (plan != null) throw new InvalidOperationException("Fish crate population is already initialized.");
            plan = source ?? throw new ArgumentNullException(nameof(source));
            crates = new GameObject[plan.FishCrateAnchors.Count];
            occupied = new bool[crates.Length];
            foreach (WarehouseFishCrateAnchor anchor in plan.FishCrateAnchors)
            {
                if ((uint)anchor.Id >= crates.Length || crates[anchor.Id] != null)
                    throw new InvalidOperationException("Fish crate IDs must be dense and unique.");
                crates[anchor.Id] = CombatZombieAssetProvider.CreateDormantCrate(transform,
                    anchor.Position, Quaternion.Euler(0f, anchor.Yaw, 0f));
            }
        }

        public bool IsOccupied(int anchorId)
        {
            RequireAnchor(anchorId);
            return occupied[anchorId];
        }

        /// <summary>True hides the passive crate and its collider before placing a live mimic.</summary>
        public void SetOccupied(int anchorId, bool value)
        {
            RequireAnchor(anchorId);
            occupied[anchorId] = value;
            crates[anchorId].SetActive(!value);
        }

        public void ResetAllDecor()
        {
            for (int i = 0; i < crates.Length; i++)
            {
                occupied[i] = false;
                if (crates[i] != null) crates[i].SetActive(true);
            }
        }

        /// <summary>Accept either a floor origin or a grounded actor root at that same anchor.</summary>
        public bool TryAnchorAt(Vector3 position, out int anchorId)
        {
            if (plan != null)
                foreach (WarehouseFishCrateAnchor anchor in plan.FishCrateAnchors)
                    if (Mathf.Abs(position.y - anchor.Position.y) <= .35f &&
                        new Vector2(position.x - anchor.Position.x, position.z - anchor.Position.z).sqrMagnitude <= .0225f)
                    { anchorId = anchor.Id; return true; }
            anchorId = -1;
            return false;
        }

        private void RequireAnchor(int id)
        {
            if ((uint)id >= crates.Length || crates[id] == null)
                throw new ArgumentOutOfRangeException(nameof(id), id, "Unknown warehouse fish crate anchor.");
        }
    }
}

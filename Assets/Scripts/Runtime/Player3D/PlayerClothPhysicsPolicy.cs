using System;
using UnityEngine;

namespace BarPromenade
{
    /// <summary>Area-owned cloth mode; hair and body physics keep their own owners.</summary>
    [DisallowMultipleComponent]
    public sealed class PlayerClothPhysicsPolicy : MonoBehaviour
    {
        public bool SimulationEnabled { get; private set; } = true;
        public event Action<bool> Changed;

        internal static PlayerClothPhysicsPolicy Find(Transform owner) =>
            owner != null ? owner.GetComponentInParent<PlayerClothPhysicsPolicy>() : null;

        public void SetSimulationEnabled(bool enabled)
        {
            if (SimulationEnabled == enabled) return;
            SimulationEnabled = enabled;
            Changed?.Invoke(enabled);
        }
    }
}

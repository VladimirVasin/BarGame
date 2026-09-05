using System.Collections.Generic;
using UnityEngine;

namespace BarPromenade
{
    /// <summary>Only the last physically released dock survives scene changes.</summary>
    public static class ChurchGardenPotSessionState
    {
        private static readonly Dictionary<string, int> docks = new Dictionary<string, int>();

        public static int GetDock(string key) => docks.TryGetValue(key, out int dock) ? dock : 0;

        public static void SetDock(string key, int dock)
        {
            ChurchGardenPotPlan.ValidateDockIndex(dock);
            docks[key] = dock;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        public static void ResetForNewSession() => docks.Clear();
    }
}

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

        internal static KeyValuePair<string, int>[] CaptureCheckpoint()
        {
            var snapshot = new KeyValuePair<string, int>[docks.Count];
            ((ICollection<KeyValuePair<string, int>>)docks).CopyTo(snapshot, 0);
            return snapshot;
        }

        internal static void RestoreCheckpoint(KeyValuePair<string, int>[] snapshot)
        {
            docks.Clear();
            foreach (var dock in snapshot) docks.Add(dock.Key, dock.Value);
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        public static void ResetForNewSession() => docks.Clear();
    }
}

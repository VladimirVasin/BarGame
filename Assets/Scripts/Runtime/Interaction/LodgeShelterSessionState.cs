using UnityEngine;

namespace BarPromenade
{
    /// <summary>Physical shelter choices survive village visits, not a new game.</summary>
    public static class LodgeShelterSessionState
    {
        public static bool LeftDoorOpen { get; private set; } = true;
        public static bool RightDoorOpen { get; private set; } = true;
        public static bool LanternLit { get; private set; }

        public static bool IsDoorOpen(int index) => index == 0 ? LeftDoorOpen : RightDoorOpen;

        internal static void SetDoorOpen(int index, bool open)
        {
            if (index == 0) LeftDoorOpen = open;
            else RightDoorOpen = open;
        }

        internal static void SetLanternLit(bool lit) => LanternLit = lit;

        internal static (bool Left, bool Right, bool Lantern) CaptureCheckpoint()
            => (LeftDoorOpen, RightDoorOpen, LanternLit);

        internal static void RestoreCheckpoint((bool Left, bool Right, bool Lantern) state)
        {
            LeftDoorOpen = state.Left;
            RightDoorOpen = state.Right;
            LanternLit = state.Lantern;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        public static void ResetForNewSession()
        {
            LeftDoorOpen = RightDoorOpen = true;
            LanternLit = false;
        }
    }
}

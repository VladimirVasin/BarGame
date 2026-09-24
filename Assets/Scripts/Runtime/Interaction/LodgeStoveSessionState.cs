using UnityEngine;

namespace BarPromenade
{
    public enum LodgeStoveStage
    {
        Empty,
        LogPlaced,
        Burning
    }

    /// <summary>The lodge's committed fuel survives scene visits until a new game.</summary>
    public static class LodgeStoveSessionState
    {
        public static LodgeStoveStage Stage { get; private set; }
        public static bool HasLog => Stage != LodgeStoveStage.Empty;
        public static bool IsBurning => Stage == LodgeStoveStage.Burning;

        /// <summary>Call at physical placement, after all presentation is ready.</summary>
        public static bool TryPlaceLog()
        {
            if (Stage != LodgeStoveStage.Empty ||
                !GameSessionState.TryRemoveInventoryItem(InventoryItemId.FirewoodLog))
            {
                return false;
            }

            Stage = LodgeStoveStage.LogPlaced;
            return true;
        }

        /// <summary>The successful strike lights committed fuel without using up the lighter.</summary>
        public static bool TryIgnite()
        {
            if (Stage != LodgeStoveStage.LogPlaced ||
                !GameSessionState.HasInventoryItem(InventoryItemId.Lighter))
            {
                return false;
            }

            Stage = LodgeStoveStage.Burning;
            return true;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        public static void ResetForNewSession() => Stage = LodgeStoveStage.Empty;
    }
}

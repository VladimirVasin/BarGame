using UnityEngine;
using UnityEngine.SceneManagement;

namespace BarPromenade
{
    /// <summary>The post-sleep stair door enters the existing warehouse without starting another session.</summary>
    public static class WarehouseStoryEntryService
    {
        private static string pendingOperationId;
        private static int pendingSeed;

        internal static CombatTestSelection Selection => new CombatTestSelection(
            CombatLoadoutSelection.CrowbarOnly, CombatTestMode.Endless, CombatEncounterSettings.Default);

        /// <summary>Called only after the source door's shared world-rig gesture completes.</summary>
        public static bool TryEnter()
        {
            if (SceneManager.GetActiveScene().name != SceneIds.StairwellInterior ||
                !GameSessionState.HomeSleepAppearanceActive || !GameSessionState.HasInventoryItem(InventoryItemId.CombatCrowbar) ||
                SceneTransitionService.IsTransitioning ||
                !Application.CanStreamedLevelBeLoaded(SceneIds.CombatTest)) return false;

            ResetPending();
            if (!SceneTransitionService.RequestDoorLoad(SceneIds.CombatTest,
                    DoorTransitionDirection.EnterApartment, out string operationId)) return false;

            pendingSeed = GameSessionState.GetOrCreateWarehouseStorySeed();
            pendingOperationId = operationId;
            GameLog.Info("warehouse", "story_entry_requested",
                GameLog.Field("operation_id", operationId), GameLog.Field("seed", pendingSeed));
            return true;
        }

        internal static bool TryConsume(out int seed)
        {
            // A failed/abandoned load cannot turn a later direct test load into a story arrival.
            bool accepted = !string.IsNullOrEmpty(pendingOperationId) &&
                SceneTransitionService.IsTransitioning &&
                pendingOperationId == SceneTransitionService.CurrentOperationId;
            seed = accepted ? pendingSeed : default;
            ResetPending();
            return accepted;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        internal static void ResetPending()
        {
            pendingOperationId = null;
            pendingSeed = default;
        }
    }
}

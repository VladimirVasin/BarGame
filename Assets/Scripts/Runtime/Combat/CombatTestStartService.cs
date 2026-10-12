using UnityEngine;
using UnityEngine.SceneManagement;

namespace BarPromenade
{
    public static class CombatTestStartService
    {
        private static CombatTestSelection pendingSelection;
        private static bool hasPendingSelection;
        private static CombatTestSelection preparationSelection;
        private static bool hasPreparation;

        public static bool TryStart(CombatWeaponId weapon = CombatWeaponId.Crowbar)
            => TryStart(CombatLoadoutSelection.FromLegacyWeapon(weapon));

        public static bool TryStart(CombatLoadoutSelection loadout)
            => TryStart(new CombatTestSelection(loadout));

        public static bool TryStart(CombatTestSelection selection)
        {
            if (SceneManager.GetActiveScene().name != SceneIds.MainMenu ||
                SceneTransitionService.IsTransitioning || !Application.CanStreamedLevelBeLoaded(SceneIds.CombatTest))
                return false;
            // Do not start the narrative calendar or its scheduled events.
            GameSessionState.BeginNewGame("combat_test_start");
            pendingSelection = selection;
            hasPendingSelection = true;
            if (SceneTransitionService.RequestLoad(SceneIds.CombatTest)) return true;
            hasPendingSelection = false;
            return false;
        }

        /// <summary>One scene consumes mode and kit atomically. Direct loads use the standard default kit.</summary>
        public static CombatTestSelection ConsumeSelection()
        {
            CombatTestSelection selection = hasPendingSelection ? pendingSelection : CombatTestSelection.Default;
            hasPendingSelection = false;
            return selection;
        }

        public static CombatLoadoutSelection ConsumeLoadout() => ConsumeSelection().Loadout;

        public static CombatWeaponId ConsumeWeapon() => ConsumeLoadout().InitialHeld;

        public static bool ReturnToPreparation(CombatWeaponId weapon)
            => ReturnToPreparation(CombatLoadoutSelection.FromLegacyWeapon(weapon));

        public static bool ReturnToPreparation(CombatLoadoutSelection loadout)
            => ReturnToPreparation(new CombatTestSelection(loadout));

        public static bool ReturnToPreparation(CombatTestSelection selection)
        {
            if (SceneManager.GetActiveScene().name != SceneIds.CombatTest ||
                SceneTransitionService.IsTransitioning || !Application.CanStreamedLevelBeLoaded(SceneIds.MainMenu))
                return false;
            preparationSelection = selection;
            hasPreparation = true;
            if (SceneTransitionService.RequestLoad(SceneIds.MainMenu)) return true;
            hasPreparation = false;
            return false;
        }

        public static bool TryConsumePreparation(out CombatWeaponId weapon)
        {
            bool pending = TryConsumePreparationLoadout(out CombatLoadoutSelection loadout);
            weapon = loadout.InitialHeld;
            return pending;
        }

        public static bool TryConsumePreparationLoadout(out CombatLoadoutSelection loadout)
        {
            bool pending = TryConsumePreparationSelection(out CombatTestSelection selection);
            loadout = selection.Loadout;
            return pending;
        }

        public static bool TryConsumePreparationSelection(out CombatTestSelection selection)
        {
            selection = hasPreparation ? preparationSelection : CombatTestSelection.Default;
            bool pending = hasPreparation;
            hasPreparation = false;
            return pending;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void Reset()
        {
            pendingSelection = preparationSelection = CombatTestSelection.Default;
            hasPendingSelection = hasPreparation = false;
        }
    }
}

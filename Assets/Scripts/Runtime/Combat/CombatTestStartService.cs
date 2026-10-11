using UnityEngine;
using UnityEngine.SceneManagement;

namespace BarPromenade
{
    public static class CombatTestStartService
    {
        private static CombatLoadoutSelection pendingLoadout;
        private static bool hasPendingLoadout;
        private static CombatLoadoutSelection preparationLoadout;
        private static bool hasPreparation;

        public static bool TryStart(CombatWeaponId weapon = CombatWeaponId.Crowbar)
            => TryStart(CombatLoadoutSelection.FromLegacyWeapon(weapon));

        public static bool TryStart(CombatLoadoutSelection loadout)
        {
            if (SceneManager.GetActiveScene().name != SceneIds.MainMenu ||
                SceneTransitionService.IsTransitioning || !Application.CanStreamedLevelBeLoaded(SceneIds.CombatTest))
                return false;
            // Do not start the narrative calendar or its scheduled events.
            GameSessionState.BeginNewGame("combat_test_start");
            pendingLoadout = loadout;
            hasPendingLoadout = true;
            if (SceneTransitionService.RequestLoad(SceneIds.CombatTest)) return true;
            hasPendingLoadout = false;
            return false;
        }

        /// <summary>One scene consumes both choices atomically. Direct loads use the default complete kit.</summary>
        public static CombatLoadoutSelection ConsumeLoadout()
        {
            CombatLoadoutSelection loadout = hasPendingLoadout ? pendingLoadout : CombatLoadoutSelection.Default;
            hasPendingLoadout = false;
            return loadout;
        }

        public static CombatWeaponId ConsumeWeapon() => ConsumeLoadout().InitialHeld;

        public static bool ReturnToPreparation(CombatWeaponId weapon)
            => ReturnToPreparation(CombatLoadoutSelection.FromLegacyWeapon(weapon));

        public static bool ReturnToPreparation(CombatLoadoutSelection loadout)
        {
            if (SceneManager.GetActiveScene().name != SceneIds.CombatTest ||
                SceneTransitionService.IsTransitioning || !Application.CanStreamedLevelBeLoaded(SceneIds.MainMenu))
                return false;
            preparationLoadout = loadout;
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
            loadout = hasPreparation ? preparationLoadout : CombatLoadoutSelection.Default;
            bool pending = hasPreparation;
            hasPreparation = false;
            return pending;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void Reset()
        {
            pendingLoadout = preparationLoadout = CombatLoadoutSelection.Default;
            hasPendingLoadout = hasPreparation = false;
        }
    }
}

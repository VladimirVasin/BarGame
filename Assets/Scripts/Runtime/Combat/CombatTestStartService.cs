using System;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace BarPromenade
{
    public static class CombatTestStartService
    {
        private static CombatWeaponId pendingWeapon;
        private static bool hasPendingWeapon;
        private static CombatWeaponId preparationWeapon;
        private static bool hasPreparation;

        public static bool TryStart(CombatWeaponId weapon = CombatWeaponId.Crowbar)
        {
            ValidateWeapon(weapon);
            if (SceneManager.GetActiveScene().name != SceneIds.MainMenu ||
                SceneTransitionService.IsTransitioning || !Application.CanStreamedLevelBeLoaded(SceneIds.CombatTest))
                return false;
            // Do not start the narrative calendar or its scheduled events.
            GameSessionState.BeginNewGame("combat_test_start");
            pendingWeapon = weapon;
            hasPendingWeapon = true;
            if (SceneTransitionService.RequestLoad(SceneIds.CombatTest)) return true;
            hasPendingWeapon = false;
            return false;
        }

        /// <summary>One scene consumes the launch choice. Direct scene loads retain the crowbar default.</summary>
        public static CombatWeaponId ConsumeWeapon()
        {
            CombatWeaponId weapon = hasPendingWeapon ? pendingWeapon : CombatWeaponId.Crowbar;
            hasPendingWeapon = false;
            return weapon;
        }

        public static bool ReturnToPreparation(CombatWeaponId weapon)
        {
            ValidateWeapon(weapon);
            if (SceneManager.GetActiveScene().name != SceneIds.CombatTest ||
                SceneTransitionService.IsTransitioning || !Application.CanStreamedLevelBeLoaded(SceneIds.MainMenu))
                return false;
            preparationWeapon = weapon;
            hasPreparation = true;
            if (SceneTransitionService.RequestLoad(SceneIds.MainMenu)) return true;
            hasPreparation = false;
            return false;
        }

        public static bool TryConsumePreparation(out CombatWeaponId weapon)
        {
            weapon = hasPreparation ? preparationWeapon : CombatWeaponId.Crowbar;
            bool pending = hasPreparation;
            hasPreparation = false;
            return pending;
        }

        private static void ValidateWeapon(CombatWeaponId weapon)
        {
            if (weapon != CombatWeaponId.Crowbar && weapon != CombatWeaponId.Pistol)
                throw new ArgumentOutOfRangeException(nameof(weapon));
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void Reset()
        {
            pendingWeapon = preparationWeapon = CombatWeaponId.Crowbar;
            hasPendingWeapon = hasPreparation = false;
        }
    }
}

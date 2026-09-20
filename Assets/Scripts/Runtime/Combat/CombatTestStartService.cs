using UnityEngine;
using UnityEngine.SceneManagement;

namespace BarPromenade
{
    public static class CombatTestStartService
    {
        public static bool TryStart()
        {
            if (SceneManager.GetActiveScene().name != SceneIds.MainMenu ||
                SceneTransitionService.IsTransitioning || !Application.CanStreamedLevelBeLoaded(SceneIds.CombatTest))
                return false;
            // Do not start the narrative calendar or its scheduled events.
            GameSessionState.BeginNewGame();
            return SceneTransitionService.RequestLoad(SceneIds.CombatTest);
        }
    }
}

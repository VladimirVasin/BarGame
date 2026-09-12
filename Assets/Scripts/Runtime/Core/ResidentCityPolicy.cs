using System.Collections;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace BarPromenade
{
    /// <summary>
    /// Whether a bar door keeps the City resident instead of rebuilding it
    /// on the way back. A rebuild costs seconds on every return from the
    /// bar; a resident city goes dormant behind the door - hierarchy,
    /// camera and listener off, root object kept - and resumes in a frame.
    ///
    /// First slice: City and BarInterior only. Every other load stays
    /// Single, and a Single load discards a dormant City before it starts,
    /// so two areas are never resident at once and the map's Single-mode
    /// boundary is untouched.
    /// </summary>
    public static class ResidentCityPolicy
    {
        internal static bool ResidentCityAcrossBarDoors = true;

        [RuntimeInitializeOnLoadMethod(
            RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            ResidentCityAcrossBarDoors = true;
        }

        /// <summary>
        /// The City that a door into the bar should put to sleep rather than
        /// unload: the initialised, awake root of the active City scene.
        /// </summary>
        internal static bool TryFindCityToKeepResident(
            string fromScene,
            string toScene,
            out CityGameRoot city)
        {
            city = null;
            if (!ResidentCityAcrossBarDoors ||
                fromScene != SceneIds.City ||
                toScene != SceneIds.BarInterior)
            {
                return false;
            }

            CityGameRoot candidate = Object.FindAnyObjectByType<CityGameRoot>();
            if (candidate == null ||
                !candidate.IsInitialized ||
                candidate.IsDormant ||
                candidate.gameObject.scene != SceneManager.GetActiveScene())
            {
                return false;
            }

            city = candidate;
            return true;
        }

        /// <summary>
        /// The dormant City a door out of the bar should wake instead of
        /// building a new one.
        /// </summary>
        internal static bool TryFindDormantCityToResume(
            string fromScene,
            string toScene,
            out CityGameRoot city)
        {
            city = null;
            if (!ResidentCityAcrossBarDoors ||
                fromScene != SceneIds.BarInterior ||
                toScene != SceneIds.City)
            {
                return false;
            }

            return CityGameRoot.TryFindDormant(out city);
        }

        /// <summary>
        /// Unloads a dormant City before a Single load. Single mode would
        /// take it anyway; doing it here keeps the rule explicit - a dormant
        /// City survives exactly one thing, the door back out of the bar -
        /// and is what every other load, area travel, a restart and a new
        /// game go through.
        /// </summary>
        internal static IEnumerator DiscardDormantCity()
        {
            if (!CityGameRoot.TryFindDormant(out CityGameRoot city))
            {
                yield break;
            }

            Scene scene = city.gameObject.scene;
            if (!scene.IsValid() ||
                !scene.isLoaded ||
                scene == SceneManager.GetActiveScene() ||
                SceneManager.sceneCount < 2)
            {
                yield break;
            }

            GameLog.Info(
                "scene",
                "dormant_city_discarded",
                GameLog.Field("scene", scene.name),
                GameLog.Field(
                    "active_scene",
                    SceneManager.GetActiveScene().name));
            AsyncOperation unload = SceneManager.UnloadSceneAsync(scene);
            while (unload != null && !unload.isDone)
            {
                yield return null;
            }
        }
    }
}

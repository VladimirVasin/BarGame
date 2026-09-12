using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace BarPromenade
{
    /// <summary>
    /// An exterior root that can sleep behind an interior door instead of
    /// being unloaded: hierarchy, camera and listener off, the root object
    /// itself awake so the bootstrap and the transition still find it, and
    /// woken by the door back out in the state a fresh build would reach.
    /// The City and the Alpine Village implement it.
    /// </summary>
    public interface IResidentExteriorRoot
    {
        string SceneName { get; }
        Scene Scene { get; }
        bool IsInitialized { get; }
        bool IsDormant { get; }
        bool EnterDormant();
        void ResumeFromDormant();
    }

    /// <summary>Which resident chain a door request takes, if any.</summary>
    internal enum ResidentDoorChain
    {
        /// <summary>The Single chain: nothing stays resident.</summary>
        None = 0,

        /// <summary>Exterior to one of its interiors: the exterior sleeps.</summary>
        EnterInterior = 1,

        /// <summary>Interior to interior of the same exterior, which keeps
        /// sleeping (the stairwell and the flat).</summary>
        BetweenInteriors = 2,

        /// <summary>Interior back to the exterior that sleeps behind it.</summary>
        ReturnToExterior = 3
    }

    /// <summary>
    /// Whether a door keeps its exterior resident instead of rebuilding it on
    /// the way back. A rebuild costs seconds on every return from an
    /// interior; a resident exterior goes dormant behind the door and
    /// resumes in a frame.
    ///
    /// A dormant exterior survives any chain of doors between its own
    /// interiors and is woken by the door that leads back into it. Every
    /// other load - area travel, a direct load, a restart, a new game, a
    /// door into an interior of a different exterior - is Single and
    /// discards a dormant exterior before it starts, so two exteriors are
    /// never resident at once and the map's Single-mode boundary between
    /// areas is untouched.
    /// </summary>
    public static class ResidentExteriorPolicy
    {
        internal static bool ResidentExteriorAcrossDoors = true;

        private static readonly List<IResidentExteriorRoot> RootScratch =
            new List<IResidentExteriorRoot>();

        [RuntimeInitializeOnLoadMethod(
            RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            ResidentExteriorAcrossDoors = true;
            RootScratch.Clear();
        }

        /// <summary>
        /// The exterior an interior's doors lead back to, or null when the
        /// scene is not an interior. The mother's house exits to the village
        /// only; the five city interiors exit to the City only, and the
        /// stairwell and the flat exit to each other on the way.
        /// </summary>
        internal static string GetExteriorOfInterior(string sceneName)
        {
            switch (sceneName)
            {
                case SceneIds.BarInterior:
                case SceneIds.SupermarketInterior:
                case SceneIds.StairwellInterior:
                case SceneIds.HomeInterior:
                case SceneIds.ChurchInterior:
                    return SceneIds.City;
                case SceneIds.MothersHouseInterior:
                    return SceneIds.AlpineVillage;
                default:
                    return null;
            }
        }

        internal static bool IsResidentCapableExterior(string sceneName)
        {
            return sceneName == SceneIds.City ||
                   sceneName == SceneIds.AlpineVillage;
        }

        /// <summary>
        /// Decides the chain for a door from <paramref name="fromScene"/> to
        /// <paramref name="toScene"/>. <paramref name="exterior"/> is the root
        /// the chain acts on. <paramref name="refusal"/> names why a door
        /// that looked resident-capable takes the Single chain instead - a
        /// door into another exterior's interior (the City map opening the
        /// mother's house), a dormant exterior that is not the one the door
        /// leads to, or an exterior root the transition cannot find - and
        /// is null when the Single chain is simply the ordinary case.
        /// </summary>
        internal static ResidentDoorChain Resolve(
            string fromScene,
            string toScene,
            out IResidentExteriorRoot exterior,
            out string refusal)
        {
            exterior = null;
            refusal = null;
            if (!ResidentExteriorAcrossDoors)
            {
                return ResidentDoorChain.None;
            }

            string fromExterior = GetExteriorOfInterior(fromScene);
            string toExterior = GetExteriorOfInterior(toScene);
            if (IsResidentCapableExterior(fromScene) && toExterior != null)
            {
                if (toExterior != fromScene)
                {
                    refusal = "interior_belongs_to_other_exterior";
                    return ResidentDoorChain.None;
                }

                if (!TryFindActiveExterior(fromScene, out exterior))
                {
                    refusal = "exterior_root_unavailable";
                    return ResidentDoorChain.None;
                }

                return ResidentDoorChain.EnterInterior;
            }

            if (fromExterior == null ||
                !TryFindDormantExterior(out exterior))
            {
                return ResidentDoorChain.None;
            }

            if (toExterior != null)
            {
                if (toExterior != exterior.SceneName ||
                    fromExterior != toExterior)
                {
                    exterior = null;
                    refusal = "dormant_exterior_mismatch";
                    return ResidentDoorChain.None;
                }

                return ResidentDoorChain.BetweenInteriors;
            }

            if (IsResidentCapableExterior(toScene))
            {
                if (toScene != exterior.SceneName)
                {
                    exterior = null;
                    refusal = "dormant_exterior_mismatch";
                    return ResidentDoorChain.None;
                }

                return ResidentDoorChain.ReturnToExterior;
            }

            exterior = null;
            return ResidentDoorChain.None;
        }

        /// <summary>
        /// The exterior a door into one of its interiors should put to sleep
        /// rather than unload: the initialised, awake root of the active
        /// scene of that name.
        /// </summary>
        private static bool TryFindActiveExterior(
            string sceneName,
            out IResidentExteriorRoot exterior)
        {
            Scene active = SceneManager.GetActiveScene();
            CollectRoots();
            for (int index = 0; index < RootScratch.Count; index++)
            {
                IResidentExteriorRoot candidate = RootScratch[index];
                if (candidate.IsInitialized &&
                    !candidate.IsDormant &&
                    candidate.Scene == active &&
                    candidate.SceneName == sceneName)
                {
                    exterior = candidate;
                    return true;
                }
            }

            exterior = null;
            return false;
        }

        /// <summary>The dormant exterior a transition may wake, if one is
        /// loaded.</summary>
        internal static bool TryFindDormantExterior(
            out IResidentExteriorRoot exterior)
        {
            CollectRoots();
            for (int index = 0; index < RootScratch.Count; index++)
            {
                IResidentExteriorRoot candidate = RootScratch[index];
                if (candidate.IsDormant && candidate.Scene.isLoaded)
                {
                    exterior = candidate;
                    return true;
                }
            }

            exterior = null;
            return false;
        }

        /// <summary>
        /// Unloads a dormant exterior before a Single load. Single mode
        /// would take it anyway; doing it here keeps the rule explicit - a
        /// dormant exterior survives exactly its own interiors' doors - and
        /// is what every other load, area travel, a restart and a new game
        /// go through.
        /// </summary>
        internal static IEnumerator DiscardDormantExterior()
        {
            if (!TryFindDormantExterior(out IResidentExteriorRoot exterior))
            {
                yield break;
            }

            Scene scene = exterior.Scene;
            if (!scene.IsValid() ||
                !scene.isLoaded ||
                scene == SceneManager.GetActiveScene() ||
                SceneManager.sceneCount < 2)
            {
                yield break;
            }

            GameLog.Info(
                "scene",
                "dormant_exterior_discarded",
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

        private static void CollectRoots()
        {
            RootScratch.Clear();
            Collect(Object.FindObjectsByType<CityGameRoot>(
                FindObjectsInactive.Include,
                FindObjectsSortMode.None));
            Collect(Object.FindObjectsByType<AlpineVillageRoot>(
                FindObjectsInactive.Include,
                FindObjectsSortMode.None));
        }

        private static void Collect<T>(T[] roots)
            where T : MonoBehaviour, IResidentExteriorRoot
        {
            for (int index = 0; index < roots.Length; index++)
            {
                if (roots[index] != null)
                {
                    RootScratch.Add(roots[index]);
                }
            }
        }
    }
}

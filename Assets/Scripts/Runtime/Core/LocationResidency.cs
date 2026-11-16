using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace BarPromenade
{
    public interface IResidentExteriorRoot
    {
        string SceneName { get; }
        Scene Scene { get; }
        bool IsInitialized { get; }
        bool IsDormant { get; }
        bool EnterDormant();
        void ResumeFromDormant();
    }

    /// <summary>Direct in-world exits, shared by warmup and scene retention.</summary>
    internal static class LocationNeighbors
    {
        private static readonly string[] City =
        {
            SceneIds.BarInterior, SceneIds.SupermarketInterior, SceneIds.StairwellInterior,
            SceneIds.ChurchInterior, SceneIds.PawnshopInterior, SceneIds.MountainRoad
        };

        private static readonly string[] ToCity = { SceneIds.City };
        private static readonly string[] FromStairwell = { SceneIds.City, SceneIds.HomeInterior };
        private static readonly string[] FromCorruptedStairwell =
            { SceneIds.HomeInterior, SceneIds.CombatTest };
        private static readonly string[] FromHomeLanding = { SceneIds.HomeInterior };
        private static readonly string[] FromHome = { SceneIds.StairwellInterior };
        private static readonly string[] FromRoad = { SceneIds.City, SceneIds.AlpineVillage };
        private static readonly string[] FromVillage = { SceneIds.MothersHouseInterior, SceneIds.MountainRoad };
        private static readonly string[] FromMothersHouse = { SceneIds.AlpineVillage };

        internal static IReadOnlyList<string> Of(string sceneName)
        {
            switch (sceneName)
            {
                case SceneIds.City: return City;
                case SceneIds.BarInterior:
                case SceneIds.SupermarketInterior:
                case SceneIds.ChurchInterior:
                case SceneIds.PawnshopInterior: return ToCity;
                case SceneIds.StairwellInterior:
                    if (!GameSessionState.HomeSleepAppearanceActive) return FromStairwell;
                    return WarehouseStoryEntryService.IsAvailable ? FromCorruptedStairwell : FromHomeLanding;
                case SceneIds.HomeInterior: return FromHome;
                case SceneIds.MountainRoad: return FromRoad;
                case SceneIds.AlpineVillage: return FromVillage;
                case SceneIds.MothersHouseInterior: return FromMothersHouse;
                default: return Array.Empty<string>();
            }
        }

        internal static bool HasExitTo(string sceneName, string destination)
        {
            IReadOnlyList<string> exits = Of(sceneName);
            for (int index = 0; index < exits.Count; index++)
                if (exits[index] == destination) return true;
            return false;
        }

        internal static bool Keeps(string sceneName, string other) =>
            (other == SceneIds.City && IsGameplay(sceneName)) || HasExitTo(sceneName, other);

        internal static bool IsGameplay(string sceneName) =>
            Of(sceneName).Count != 0 || sceneName == SceneIds.CombatTest;
    }

    /// <summary>Owns one sleeping, fully composed scene per direct destination.</summary>
    internal static class LocationResidency
    {
        private static readonly Dictionary<string, ResidentLocation> Dormant = new();
        private static string activeBarId;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            Dormant.Clear();
            activeBarId = null;
            SceneManager.sceneUnloaded -= OnSceneUnloaded;
            SceneManager.sceneUnloaded += OnSceneUnloaded;
        }

        internal static void DestinationReady(string sceneName)
        {
            activeBarId = sceneName == SceneIds.BarInterior ? GameSessionState.ActiveBarId : null;
            AreaAssetWarmup.DestinationReady(sceneName);
        }

        internal static bool IsResident(string sceneName) =>
            Dormant.TryGetValue(sceneName, out ResidentLocation location) && location.Scene.isLoaded;

        internal static ResidentLocation Find(string sceneName) =>
            Dormant.TryGetValue(sceneName, out ResidentLocation location) &&
            location.Scene.isLoaded && location.CanEnter() ? location : null;

        internal static ResidentLocation CaptureSource(Scene scene, string destination)
        {
            if (!LocationNeighbors.Keeps(destination, scene.name)) return null;
            var source = new ResidentLocation(scene, activeBarId ?? GameSessionState.ActiveBarId);
            return source.CanSleep ? source : null;
        }

        internal static void Park(ResidentLocation source)
        {
            source.Sleep();
            Dormant.Add(source.Scene.name, source);
        }

        internal static void Enter(ResidentLocation destination)
        {
            Dormant.Remove(destination.Scene.name);
            destination.Wake();
        }

        internal static IEnumerator Trim(string destination)
        {
            var leaving = new List<Scene>();
            foreach (ResidentLocation location in Dormant.Values)
                if (location.Scene.name != destination &&
                    !LocationNeighbors.Keeps(destination, location.Scene.name))
                    leaving.Add(location.Scene);
            foreach (Scene scene in leaving)
                yield return Unload(scene);
        }

        internal static IEnumerator Unload(Scene scene)
        {
            if (!scene.isLoaded || scene == SceneManager.GetActiveScene()) yield break;
            bool evictsAssets = LocationNeighbors.IsGameplay(scene.name);
            AsyncOperation operation = SceneManager.UnloadSceneAsync(scene);
            while (operation != null && !operation.isDone) yield return null;
            if (evictsAssets) AreaAssetWarmup.NoteEviction();
        }

        private static void OnSceneUnloaded(Scene scene)
        {
            if (Dormant.TryGetValue(scene.name, out ResidentLocation location) && location.Scene == scene)
                Dormant.Remove(scene.name);
        }
    }

    internal sealed class ResidentLocation
    {
        private readonly List<GameObject> activeRoots = new();
        private readonly List<SceneMusicPlayer> themes = new();
        private readonly IResidentExteriorRoot exterior;
        private readonly IResidentArrival arrival;
        private readonly string barId;
        private readonly bool corruptedStairwell;

        internal Scene Scene { get; }
        internal bool CanSleep => exterior?.IsInitialized ?? (arrival != null);

        internal ResidentLocation(Scene scene, string barId)
        {
            Scene = scene;
            this.barId = barId;
            foreach (GameObject root in scene.GetRootGameObjects())
            {
                if (root.GetComponent<StairwellInteriorRoot>() is StairwellInteriorRoot stairwell)
                    corruptedStairwell = stairwell.Layout.IsCorrupted;
                exterior ??= root.GetComponent<IResidentExteriorRoot>();
                arrival ??= root.GetComponent<IResidentArrival>();
                foreach (SceneMusicPlayer theme in root.GetComponentsInChildren<SceneMusicPlayer>(false))
                    if (theme.isActiveAndEnabled) themes.Add(theme);
            }
        }

        internal bool CanEnter()
        {
            if (Scene.name == SceneIds.BarInterior) return barId == GameSessionState.ActiveBarId;
            if (Scene.name == SceneIds.StairwellInterior)
                return corruptedStairwell == GameSessionState.HomeSleepAppearanceActive;
            if (Scene.name == SceneIds.HomeInterior) return GameSessionState.HomeArrival == HomeArrivalKind.Normal;
            return true;
        }

        internal void FadeTheme(SceneMusicPlayer theme)
        {
            if (themes.Contains(theme)) theme.RequestResidentExitFade();
            else theme.BeginSceneExitFadeOut();
        }

        internal void Sleep()
        {
            if (exterior != null)
            {
                if (!exterior.EnterDormant())
                    throw new InvalidOperationException($"Could not suspend {Scene.name}.");
                return;
            }

            Scene.GetRootGameObjects(activeRoots);
            for (int index = activeRoots.Count - 1; index >= 0; index--)
            {
                GameObject root = activeRoots[index];
                if (root.activeSelf) root.SetActive(false);
                else activeRoots.RemoveAt(index);
            }
        }

        internal void Wake()
        {
            foreach (SceneMusicPlayer theme in themes)
                if (theme != null) theme.RestoreResidentCarrier();

            if (exterior != null)
            {
                exterior.ResumeFromDormant();
            }
            else
            {
                foreach (GameObject root in activeRoots)
                    if (root != null) root.SetActive(true);
                GameLog.SetScene(Scene.name);
                GameLog.SetCitySeed(GameSessionState.CitySeed);
                arrival?.Resume();
            }

            foreach (SceneMusicPlayer theme in themes)
                if (theme != null && theme.isActiveAndEnabled) theme.ResumeResidentPlayback();

            activeRoots.Clear();
            themes.Clear();
        }
    }
}

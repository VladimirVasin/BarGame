using System;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace BarPromenade.Editor
{
    public static class ProjectSceneSetup
    {
        private const string SampleScenePath = "Assets/Scenes/SampleScene.unity";
        private const string MainMenuScenePath =
            "Assets/Scenes/MainMenu.unity";
        private const string CityScenePath = "Assets/Scenes/City.unity";
        private const string DoorTransitionScenePath =
            "Assets/Scenes/DoorTransition.unity";
        private const string BarInteriorScenePath = "Assets/Scenes/BarInterior.unity";
        private const string StairwellInteriorScenePath =
            "Assets/Scenes/StairwellInterior.unity";
        private const string HomeInteriorScenePath =
            "Assets/Scenes/HomeInterior.unity";
        private const string TestBootstrapScenePrefix =
            "Assets/InitTestScene";

        private static SceneAsset mainMenuStartScene;

        [InitializeOnLoadMethod]
        private static void SchedulePlayModeStartScene()
        {
            EditorApplication.update -=
                SuppressStartSceneForTestBootstrap;
            EditorApplication.update +=
                SuppressStartSceneForTestBootstrap;
            EditorApplication.playModeStateChanged -=
                HandlePlayModeStateChanged;
            EditorApplication.playModeStateChanged +=
                HandlePlayModeStateChanged;
            EditorApplication.delayCall -=
                ConfigurePlayModeStartScene;
            EditorApplication.delayCall +=
                ConfigurePlayModeStartScene;
        }

        public static void Run()
        {
            EnsureMainMenuScene();
            ConfigurePlayModeStartScene();
            EnsureCityScene();
            EnsureDoorTransitionScene();
            EnsureInteriorScene();
            EnsureStairwellInteriorScene();
            EnsureHomeInteriorScene();
            ConfigureBuildScenes();
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            EditorSceneManager.OpenScene(
                MainMenuScenePath,
                OpenSceneMode.Single);
            Debug.Log("Bar Promenade scenes and Build Settings are configured.");
        }

        public static void BuildWindows()
        {
            ConfigureBuildScenes();
            var options = new BuildPlayerOptions
            {
                scenes = new[]
                {
                    MainMenuScenePath,
                    CityScenePath,
                    DoorTransitionScenePath,
                    BarInteriorScenePath,
                    StairwellInteriorScenePath,
                    HomeInteriorScenePath
                },
                locationPathName = "Build/Windows/BarPromenade.exe",
                target = BuildTarget.StandaloneWindows64,
                options = BuildOptions.None
            };
            BuildReport report = BuildPipeline.BuildPlayer(options);
            if (report.summary.result != BuildResult.Succeeded)
            {
                throw new InvalidOperationException(
                    $"Windows build failed: {report.summary.result}, " +
                    $"{report.summary.totalErrors} errors.");
            }

            Debug.Log(
                $"Windows build succeeded: {report.summary.totalSize} bytes, " +
                $"{report.summary.totalWarnings} warnings.");
        }

        public static void ConfigurePlayModeStartScene()
        {
            EditorApplication.delayCall -=
                ConfigurePlayModeStartScene;
            if (EditorApplication.isPlayingOrWillChangePlaymode)
            {
                return;
            }

            SetMainMenuAsPlayModeStartScene();
        }

        public static bool IsTestBootstrapScenePath(
            string scenePath)
        {
            const string suffix = ".unity";
            if (string.IsNullOrEmpty(scenePath) ||
                !scenePath.StartsWith(
                    TestBootstrapScenePrefix,
                    StringComparison.Ordinal) ||
                !scenePath.EndsWith(
                    suffix,
                    StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            int guidLength =
                scenePath.Length -
                TestBootstrapScenePrefix.Length -
                suffix.Length;
            return guidLength > 0 &&
                   Guid.TryParse(
                       scenePath.Substring(
                           TestBootstrapScenePrefix.Length,
                           guidLength),
                       out _);
        }

        private static void SuppressStartSceneForTestBootstrap()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
            {
                return;
            }

            TrySuppressStartSceneForTestBootstrap();
        }

        private static void HandlePlayModeStateChanged(
            PlayModeStateChange state)
        {
            if (state == PlayModeStateChange.ExitingEditMode)
            {
                if (!TrySuppressStartSceneForTestBootstrap())
                {
                    SetMainMenuAsPlayModeStartScene();
                }
            }
            else if (state ==
                     PlayModeStateChange.EnteredEditMode)
            {
                SetMainMenuAsPlayModeStartScene();
            }
        }

        private static bool
            TrySuppressStartSceneForTestBootstrap()
        {
            Scene activeScene =
                EditorSceneManager.GetActiveScene();
            if (!activeScene.IsValid() ||
                !IsTestBootstrapScenePath(activeScene.path))
            {
                return false;
            }

            if (EditorSceneManager.playModeStartScene != null)
            {
                EditorSceneManager.playModeStartScene = null;
            }

            return true;
        }

        private static void SetMainMenuAsPlayModeStartScene()
        {
            if (mainMenuStartScene == null)
            {
                mainMenuStartScene =
                    AssetDatabase.LoadAssetAtPath<SceneAsset>(
                        MainMenuScenePath);
            }

            SceneAsset mainMenu = mainMenuStartScene;
            if (mainMenu != null &&
                EditorSceneManager.playModeStartScene != mainMenu)
            {
                EditorSceneManager.playModeStartScene = mainMenu;
            }
        }

        private static void EnsureMainMenuScene()
        {
            if (AssetDatabase.LoadAssetAtPath<SceneAsset>(
                    MainMenuScenePath) != null)
            {
                return;
            }

            Scene scene = EditorSceneManager.NewScene(
                NewSceneSetup.EmptyScene,
                NewSceneMode.Single);
            if (!EditorSceneManager.SaveScene(
                    scene,
                    MainMenuScenePath))
            {
                throw new InvalidOperationException(
                    $"Failed to create '{MainMenuScenePath}'.");
            }
        }

        private static void EnsureCityScene()
        {
            if (AssetDatabase.LoadAssetAtPath<SceneAsset>(CityScenePath) != null)
            {
                return;
            }

            if (AssetDatabase.LoadAssetAtPath<SceneAsset>(SampleScenePath) == null)
            {
                throw new InvalidOperationException(
                    $"Neither '{SampleScenePath}' nor '{CityScenePath}' exists.");
            }

            string error = AssetDatabase.MoveAsset(SampleScenePath, CityScenePath);
            if (!string.IsNullOrEmpty(error))
            {
                throw new InvalidOperationException(error);
            }
        }

        private static void EnsureInteriorScene()
        {
            if (AssetDatabase.LoadAssetAtPath<SceneAsset>(BarInteriorScenePath) != null)
            {
                return;
            }

            Scene scene = EditorSceneManager.NewScene(
                NewSceneSetup.EmptyScene,
                NewSceneMode.Single);
            if (!EditorSceneManager.SaveScene(scene, BarInteriorScenePath))
            {
                throw new InvalidOperationException(
                    $"Failed to create '{BarInteriorScenePath}'.");
            }
        }

        private static void EnsureDoorTransitionScene()
        {
            if (AssetDatabase.LoadAssetAtPath<SceneAsset>(DoorTransitionScenePath) != null)
            {
                return;
            }

            Scene scene = EditorSceneManager.NewScene(
                NewSceneSetup.EmptyScene,
                NewSceneMode.Single);
            if (!EditorSceneManager.SaveScene(scene, DoorTransitionScenePath))
            {
                throw new InvalidOperationException(
                    $"Failed to create '{DoorTransitionScenePath}'.");
            }
        }

        private static void EnsureHomeInteriorScene()
        {
            if (AssetDatabase.LoadAssetAtPath<SceneAsset>(
                    HomeInteriorScenePath) != null)
            {
                return;
            }

            Scene scene = EditorSceneManager.NewScene(
                NewSceneSetup.EmptyScene,
                NewSceneMode.Single);
            if (!EditorSceneManager.SaveScene(
                    scene,
                    HomeInteriorScenePath))
            {
                throw new InvalidOperationException(
                    $"Failed to create '{HomeInteriorScenePath}'.");
            }
        }

        private static void EnsureStairwellInteriorScene()
        {
            if (AssetDatabase.LoadAssetAtPath<SceneAsset>(
                    StairwellInteriorScenePath) != null)
            {
                return;
            }

            Scene scene = EditorSceneManager.NewScene(
                NewSceneSetup.EmptyScene,
                NewSceneMode.Single);
            if (!EditorSceneManager.SaveScene(
                    scene,
                    StairwellInteriorScenePath))
            {
                throw new InvalidOperationException(
                    $"Failed to create '{StairwellInteriorScenePath}'.");
            }
        }

        private static void ConfigureBuildScenes()
        {
            EditorBuildSettings.scenes = new[]
            {
                new EditorBuildSettingsScene(
                    MainMenuScenePath,
                    true),
                new EditorBuildSettingsScene(CityScenePath, true),
                new EditorBuildSettingsScene(DoorTransitionScenePath, true),
                new EditorBuildSettingsScene(BarInteriorScenePath, true),
                new EditorBuildSettingsScene(
                    StairwellInteriorScenePath,
                    true),
                new EditorBuildSettingsScene(HomeInteriorScenePath, true)
            };
        }
    }
}

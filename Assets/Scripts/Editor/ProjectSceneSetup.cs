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
        private const string CityScenePath = "Assets/Scenes/City.unity";
        private const string DoorTransitionScenePath =
            "Assets/Scenes/DoorTransition.unity";
        private const string BarInteriorScenePath = "Assets/Scenes/BarInterior.unity";
        private const string HomeInteriorScenePath =
            "Assets/Scenes/HomeInterior.unity";

        public static void Run()
        {
            EnsureCityScene();
            EnsureDoorTransitionScene();
            EnsureInteriorScene();
            EnsureHomeInteriorScene();
            ConfigureBuildScenes();
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            EditorSceneManager.OpenScene(CityScenePath, OpenSceneMode.Single);
            Debug.Log("Bar Promenade scenes and Build Settings are configured.");
        }

        public static void BuildWindows()
        {
            ConfigureBuildScenes();
            var options = new BuildPlayerOptions
            {
                scenes = new[]
                {
                    CityScenePath,
                    DoorTransitionScenePath,
                    BarInteriorScenePath,
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

        private static void ConfigureBuildScenes()
        {
            EditorBuildSettings.scenes = new[]
            {
                new EditorBuildSettingsScene(CityScenePath, true),
                new EditorBuildSettingsScene(DoorTransitionScenePath, true),
                new EditorBuildSettingsScene(BarInteriorScenePath, true),
                new EditorBuildSettingsScene(HomeInteriorScenePath, true)
            };
        }
    }
}

using System;
using System.IO;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace BarPromenade.Tests.EditMode
{
    public sealed class ProjectBuildSceneTests
    {
        [Test]
        public void BuildScenes_StartAtMainMenuAndMatchTheAllowList()
        {
            string[] actual = EditorBuildSettings.scenes
                .Where(scene => scene.enabled)
                .Select(scene => scene.path)
                .ToArray();
            string[] expected =
            {
                "Assets/Scenes/MainMenu.unity",
                "Assets/Scenes/City.unity",
                "Assets/Scenes/DoorTransition.unity",
                "Assets/Scenes/BarInterior.unity",
                "Assets/Scenes/SupermarketInterior.unity",
                "Assets/Scenes/StairwellInterior.unity",
                "Assets/Scenes/HomeInterior.unity",
                "Assets/Scenes/MountainRoad.unity",
                "Assets/Scenes/AreaLoading.unity",
                "Assets/Scenes/ChurchInterior.unity",
                "Assets/Scenes/AlpineVillage.unity",
                "Assets/Scenes/MothersHouseInterior.unity"
            };

            CollectionAssert.AreEqual(expected, actual);
            for (int index = 0; index < expected.Length; index++)
            {
                Assert.That(
                    AssetDatabase.LoadAssetAtPath<SceneAsset>(
                        expected[index]),
                    Is.Not.Null,
                    $"Build scene asset is missing: {expected[index]}");
            }
        }

        [Test]
        public void BuildShaders_KeepRuntimeExponentialSquaredFogVariant()
        {
            string projectRoot = Directory.GetParent(
                Application.dataPath)?.FullName;
            Assert.That(projectRoot, Is.Not.Null);
            string settings = File.ReadAllText(
                    Path.Combine(
                        projectRoot,
                        "ProjectSettings",
                        "GraphicsSettings.asset"))
                .Replace("\r\n", "\n");

            Assert.That(
                settings,
                Does.Contain("\n  m_FogStripping: 1\n"),
                "Runtime-only City fog requires Custom fog stripping.");
            Assert.That(
                settings,
                Does.Contain("\n  m_FogKeepExp2: 1\n"),
                "The player must retain the Exponential Squared variant " +
                "enabled by RuntimeSceneSetup.");
            Assert.That(
                settings,
                Does.Contain("\n  m_FogKeepLinear: 0\n"));
            Assert.That(
                settings,
                Does.Contain("\n  m_FogKeepExp: 0\n"));
        }

        [Test]
        public void EditorPlayMode_StartsAtMainMenu()
        {
            SceneAsset city =
                AssetDatabase.LoadAssetAtPath<SceneAsset>(
                    "Assets/Scenes/City.unity");
            Assert.That(city, Is.Not.Null);

            try
            {
                EditorSceneManager.playModeStartScene = city;
                InvokeProjectSceneSetup(
                    "ConfigurePlayModeStartScene");

                SceneAsset startScene =
                    EditorSceneManager.playModeStartScene;
                Assert.That(startScene, Is.Not.Null);
                Assert.That(
                    AssetDatabase.GetAssetPath(startScene),
                    Is.EqualTo("Assets/Scenes/MainMenu.unity"));
            }
            finally
            {
                InvokeProjectSceneSetup(
                    "ConfigurePlayModeStartScene");
            }
        }

        [TestCase(
            "Assets/InitTestScenebe8b3cdb-0a20-4890-b700-06240170f009.unity",
            true)]
        [TestCase(
            "Assets/InitTestSceneNot-A-Guid.unity",
            false)]
        [TestCase(
            "Assets/Scenes/InitTestScenebe8b3cdb-0a20-4890-b700-06240170f009.unity",
            false)]
        [TestCase("Assets/Scenes/MainMenu.unity", false)]
        public void TestBootstrapSceneRecognition_IsExact(
            string path,
            bool expected)
        {
            Assert.That(
                InvokeProjectSceneSetup(
                    "IsTestBootstrapScenePath",
                    path),
                Is.EqualTo(expected));
        }

        private static object InvokeProjectSceneSetup(
            string methodName,
            params object[] arguments)
        {
            Type setupType = Type.GetType(
                "BarPromenade.Editor.ProjectSceneSetup, " +
                "BarPromenade.Editor",
                true);
            MethodInfo method = setupType.GetMethod(
                methodName,
                BindingFlags.Public | BindingFlags.Static);
            Assert.That(
                method,
                Is.Not.Null,
                $"Missing ProjectSceneSetup.{methodName}.");
            return method.Invoke(null, arguments);
        }
    }
}

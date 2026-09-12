using System;
using System.Reflection;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace BarPromenade.Tests.EditMode
{
    /// <summary>
    /// Every scene theme streams from disk and opens in the background, so no
    /// scene root ever decodes a whole MP3 on the main thread at Awake. The
    /// Editor assembly is reached through reflection, as the other import
    /// contracts do, because the EditMode assembly does not reference it.
    /// </summary>
    public sealed class SceneMusicImportTests
    {
        private const string ResourcesRoot = "Assets/Resources/";

        private static readonly string[] StationResourcePaths =
        {
            LastRouteRadioMusicPlayer.ResourcePathForStation(0),
            LastRouteRadioMusicPlayer.ResourcePathForStation(1),
            LastRouteRadioMusicPlayer.ResourcePathForStation(2)
        };

        private static readonly string[] ThemeResourcePaths =
        {
            CityMusicPlayer.ResourcePath,
            HomeMusicPlayer.ResourcePath,
            ChurchMusicPlayer.ResourcePath,
            BarMusicPlayer.ResourcePath,
            StairwellMusicPlayer.ResourcePath,
            SupermarketMusicPlayer.ResourcePath,
            HomeSmokingMusicPlayer.ResourcePath,
            StationResourcePaths[0],
            StationResourcePaths[1],
            StationResourcePaths[2]
        };

        [Test]
        public void EveryTheme_StreamsAndLoadsInTheBackground()
        {
            foreach (string resource in ThemeResourcePaths)
            {
                string path = FindAssetPath(resource);
                Assert.That(path, Is.Not.Null, resource);
                var importer = AssetImporter.GetAtPath(path) as AudioImporter;
                Assert.That(importer, Is.Not.Null, path);
                Assert.That(importer.loadInBackground, Is.True, path);
                Assert.That(
                    importer.defaultSampleSettings.loadType,
                    Is.EqualTo(AudioClipLoadType.Streaming),
                    path);
                if (Array.IndexOf(StationResourcePaths, resource) >= 0)
                {
                    Assert.That(
                        importer.forceToMono,
                        Is.True,
                        "A radio station plays from one dashboard speaker: " + path);
                }
            }

            // The three stations resolve on the fast path by name: a station
            // file named otherwise falls back to a folder scan every tuning.
            foreach (string station in StationResourcePaths)
            {
                Assert.That(
                    Resources.Load<AudioClip>(station),
                    Is.Not.Null,
                    "Station clip must answer to " + station);
            }

            Type importerType = Type.GetType(
                "BarPromenade.Editor.SceneMusicImporter, BarPromenade.Editor", true);
            try { importerType.GetMethod("ValidateOrThrow").Invoke(null, null); }
            catch (TargetInvocationException failure)
            {
                Assert.Fail((failure.InnerException ?? failure).ToString());
            }

            MethodInfo scope = importerType.GetMethod("IsThemePath");
            Assert.That(scope.Invoke(null, new object[] {
                "Assets/Resources/Audio/CityMusic/city_theme.mp3" }), Is.EqualTo(true));
            Assert.That(scope.Invoke(null, new object[] {
                "Assets/Resources/Audio/LastRouteRadio/Station2/radio_theme.ogg" }), Is.EqualTo(true));
            foreach (string unrelated in new[] {
                "Assets/Resources/Audio/CityMusic/other.mp3",
                "Assets/Resources/Audio/LastRouteRadio/radio_theme.mp3",
                "Assets/Audio/CityMusic/city_theme.mp3" })
            {
                Assert.That(scope.Invoke(null, new object[] { unrelated }), Is.EqualTo(false), unrelated);
            }
        }

        private static string FindAssetPath(string resource)
        {
            int slash = resource.LastIndexOf('/');
            string folder = ResourcesRoot + resource.Substring(0, slash);
            foreach (string guid in AssetDatabase.FindAssets("t:AudioClip", new[] { folder }))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                string relative = path.Substring(ResourcesRoot.Length);
                int dot = relative.LastIndexOf('.');
                if (dot > relative.LastIndexOf('/')) relative = relative.Substring(0, dot);
                if (string.Equals(relative, resource, StringComparison.OrdinalIgnoreCase))
                    return path;
            }

            return null;
        }
    }
}

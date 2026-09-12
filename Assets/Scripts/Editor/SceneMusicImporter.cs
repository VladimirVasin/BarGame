using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace BarPromenade.Editor
{
    /// <summary>
    /// Import profile for the ten scene themes. Every one of them is a long
    /// looping track that a scene root loads at Awake, so the profile is the
    /// one that never blocks the main thread: streamed from disk and opened
    /// in the background. A decompress-on-load city theme once cost the City
    /// root almost two seconds of synchronous MP3 decode.
    /// </summary>
    public sealed class SceneMusicImporter : AssetPostprocessor
    {
        private const string ResourcesRoot = "Assets/Resources/";

        /// <summary>
        /// The three car radio stations play from one dashboard speaker
        /// behind a 3500 Hz low-pass, fully spatialised, so a stereo master
        /// only doubles the stream for nothing.
        /// </summary>
        public static readonly string[] MonoResourcePaths =
        {
            LastRouteRadioMusicPlayer.ResourcePathForStation(0),
            LastRouteRadioMusicPlayer.ResourcePathForStation(1),
            LastRouteRadioMusicPlayer.ResourcePathForStation(2)
        };

        public static readonly string[] ResourcePaths =
        {
            CityMusicPlayer.ResourcePath,
            HomeMusicPlayer.ResourcePath,
            ChurchMusicPlayer.ResourcePath,
            BarMusicPlayer.ResourcePath,
            StairwellMusicPlayer.ResourcePath,
            SupermarketMusicPlayer.ResourcePath,
            HomeSmokingMusicPlayer.ResourcePath,
            MonoResourcePaths[0],
            MonoResourcePaths[1],
            MonoResourcePaths[2]
        };

        /// <summary>
        /// The resource path an asset path answers to, or null when the asset
        /// is not one of the themes. The station folders accept any audio
        /// format, so the extension is not part of the match.
        /// </summary>
        public static string GetResourcePath(string assetPath)
        {
            if (string.IsNullOrEmpty(assetPath) ||
                !assetPath.StartsWith(ResourcesRoot, StringComparison.OrdinalIgnoreCase))
                return null;

            string relative = assetPath.Substring(ResourcesRoot.Length);
            int dot = relative.LastIndexOf('.');
            if (dot > relative.LastIndexOf('/')) relative = relative.Substring(0, dot);
            foreach (string resource in ResourcePaths)
            {
                if (string.Equals(relative, resource, StringComparison.OrdinalIgnoreCase))
                    return resource;
            }

            // A station folder promises to play one track of any name
            // (LastRouteRadioMusicPlayer.LoadStationClip scans the folder),
            // so any clip sitting directly in it is that station's theme.
            for (int station = 0; station < MonoResourcePaths.Length; station++)
            {
                string folder = MonoResourcePaths[station].Substring(
                    0, MonoResourcePaths[station].LastIndexOf('/') + 1);
                if (relative.StartsWith(folder, StringComparison.OrdinalIgnoreCase) &&
                    relative.IndexOf('/', folder.Length) < 0)
                    return MonoResourcePaths[station];
            }

            return null;
        }

        /// <summary>
        /// Bumped whenever the profile above changes, so every theme is
        /// reimported instead of keeping yesterday's settings.
        /// </summary>
        public override uint GetVersion() => 1;

        public static bool IsThemePath(string assetPath) => GetResourcePath(assetPath) != null;

        public static bool IsMonoResource(string resourcePath) =>
            Array.IndexOf(MonoResourcePaths, resourcePath) >= 0;

        /// <summary>The audio asset currently filling a theme slot, or null.</summary>
        public static string FindAssetPath(string resourcePath)
        {
            int slash = resourcePath.LastIndexOf('/');
            string folder = ResourcesRoot + (slash >= 0 ? resourcePath.Substring(0, slash) : string.Empty);
            string fallback = null;
            foreach (string guid in AssetDatabase.FindAssets("t:AudioClip", new[] { folder }))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                if (GetResourcePath(path) != resourcePath) continue;
                // The exact name is the runtime fast path; any other clip
                // in a station folder is the documented fallback.
                if (string.Equals(
                        System.IO.Path.GetFileNameWithoutExtension(path),
                        resourcePath.Substring(slash + 1),
                        StringComparison.OrdinalIgnoreCase))
                    return path;
                if (fallback == null ||
                    string.CompareOrdinal(path, fallback) < 0) fallback = path;
            }

            return fallback;
        }

        private void OnPreprocessAudio()
        {
            string resource = GetResourcePath(assetPath);
            if (resource == null || !(assetImporter is AudioImporter importer)) return;

            importer.loadInBackground = true;
            importer.forceToMono = IsMonoResource(resource);
            AudioImporterSampleSettings settings = importer.defaultSampleSettings;
            settings.loadType = AudioClipLoadType.Streaming;
            settings.compressionFormat = AudioCompressionFormat.Vorbis;
            importer.defaultSampleSettings = settings;
            // A stale per-platform override would silently win over the
            // default on the only platform that ships.
            if (importer.ContainsSampleSettingsOverride(BuildTargetGroup.Standalone))
                importer.ClearSampleSettingOverride(BuildTargetGroup.Standalone);
        }

        public static void ValidateOrThrow()
        {
            foreach (string resource in ResourcePaths)
            {
                string path = FindAssetPath(resource);
                if (path == null)
                {
                    // An empty station folder is allowed by its README: the
                    // radio simply has nothing to play on that detent.
                    if (IsMonoResource(resource)) continue;
                    throw new InvalidOperationException($"Scene theme is missing: '{resource}'.");
                }

                var importer = AssetImporter.GetAtPath(path) as AudioImporter;
                if (importer == null)
                    throw new InvalidOperationException($"Scene theme has no audio importer: '{path}'.");
                if (!importer.loadInBackground ||
                    importer.defaultSampleSettings.loadType != AudioClipLoadType.Streaming)
                    throw new InvalidOperationException(
                        $"Scene theme must stream and load in the background: '{path}'.");
                if (importer.ContainsSampleSettingsOverride(BuildTargetGroup.Standalone) &&
                    importer.GetOverrideSampleSettings(BuildTargetGroup.Standalone).loadType !=
                    AudioClipLoadType.Streaming)
                    throw new InvalidOperationException(
                        $"Scene theme carries a Standalone override that does not stream: '{path}'.");
                if (IsMonoResource(resource) && !importer.forceToMono)
                    throw new InvalidOperationException($"Radio station must be forced to mono: '{path}'.");
            }
        }

        [MenuItem("Bar Promenade/Audio/Reimport Scene Themes")]
        public static void ReimportAll()
        {
            var paths = new List<string>();
            foreach (string resource in ResourcePaths)
            {
                string path = FindAssetPath(resource);
                if (path != null) paths.Add(path);
            }

            foreach (string path in paths)
            {
                AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);
            }

            ValidateOrThrow();
        }
    }
}

using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using UnityEditor;
using UnityEngine;

namespace BarPromenade.Editor
{
    /// <summary>Read-only failure details; the self-hashed bake owner and its gate remain authoritative.</summary>
    public static class SecondaryMotionBakeDiagnostics
    {
        private const string Bank = "Assets/Resources/Player/SecondaryMotionBakes";
        private const string ManifestPath = Bank + "/Provenance.json";
        private const string Repair = "-executeMethod BarPromenade.Editor.SecondaryMotionBakeAssetSetup.BakeAll";
        private const int MaximumDetails = 12;

        [Serializable] private sealed class Source { public string path, hash; public bool raw; }
        [Serializable] private sealed class Provenance
        {
            public int version;
            public Source[] sources;
            public string[] recordings;
        }

        [InitializeOnLoadMethod]
        private static void Register()
        {
            PlayerBuildAssetValidation.Register("Player secondary-motion provenance", ValidateOrThrow, Repair);
        }

        public static void ValidateOrThrow()
        {
            string json = File.Exists(ManifestPath) ? File.ReadAllText(ManifestPath) : null;
            string report = Inspect(json, File.Exists, RawHash,
                path => AssetDatabase.GetAssetDependencyHash(path).ToString());
            if (!string.IsNullOrEmpty(report)) throw new InvalidOperationException(report);
        }

        // Injected reads let the diagnostic contract be checked without touching
        // published recordings, importing assets, or rerunning physical solvers.
        public static string Inspect(string json, Func<string, bool> exists,
            Func<string, string> rawHash, Func<string, string> assetHash)
        {
            if (exists == null || rawHash == null || assetHash == null) throw new ArgumentNullException();
            var details = new List<string>(MaximumDetails);
            int failures = 0;
            void Add(string message)
            {
                failures++;
                if (details.Count < MaximumDetails) details.Add(message);
            }
            string Finish() => failures == 0 ? string.Empty :
                $"{ManifestPath}: {failures} issue(s), showing {details.Count}.\n" +
                string.Join("\n", details) + "\nRepair: " + Repair;

            if (json == null) { Add("Missing provenance manifest."); return Finish(); }
            Provenance manifest;
            try { manifest = JsonUtility.FromJson<Provenance>(json); }
            catch (ArgumentException exception)
            {
                Add("Malformed provenance JSON: " + Display(exception.Message));
                return Finish();
            }
            if (manifest == null || manifest.version != 1 || manifest.sources == null || manifest.recordings == null)
            {
                Add("Malformed provenance: expected version 1, sources and recordings arrays.");
                return Finish();
            }

            foreach (Source source in manifest.sources)
            {
                if (source == null || !SafeRelativePath(source.path) ||
                    !source.path.StartsWith("Assets/", StringComparison.Ordinal))
                {
                    Add("Unsafe or missing source.path: '" + Display(source?.path) + "'.");
                    continue;
                }
                string kind = source.raw ? "raw SHA-256" : "AssetDatabase dependency hash";
                string label = $"source '{Display(source.path)}' ({kind})";
                if (string.IsNullOrWhiteSpace(source.hash))
                {
                    Add(label + ": malformed saved hash '<missing>'.");
                    continue;
                }
                try
                {
                    if (!exists(source.path))
                        Add(label + $": saved '{Display(source.hash)}', current '<missing file>'.");
                    else
                    {
                        string current = source.raw ? rawHash(source.path) : assetHash(source.path);
                        if (!string.Equals(current, source.hash, StringComparison.Ordinal))
                            Add(label + $": saved '{Display(source.hash)}', current '{Display(current)}'.");
                    }
                }
                catch (Exception exception)
                {
                    Add(label + ": cannot read current hash: " + Display(exception.Message));
                }
            }
            foreach (string recording in manifest.recordings)
            {
                if (!SafeRelativePath(recording))
                {
                    Add("Unsafe or missing recording path: '" + Display(recording) + "'.");
                    continue;
                }
                string path = Bank + "/" + recording;
                try { if (!exists(path)) Add("Missing recording: '" + Display(path) + "'."); }
                catch (Exception exception)
                {
                    Add("Cannot read recording '" + Display(path) + "': " + Display(exception.Message));
                }
            }
            return Finish();
        }

        private static bool SafeRelativePath(string path)
        {
            if (string.IsNullOrWhiteSpace(path) || Path.IsPathRooted(path) ||
                path.IndexOf('\\') >= 0 || path.IndexOf(':') >= 0) return false;
            foreach (char character in path) if (char.IsControl(character)) return false;
            foreach (string part in path.Split('/'))
                if (part.Length == 0 || part == "." || part == "..") return false;
            return true;
        }

        private static string Display(string value)
        {
            if (string.IsNullOrEmpty(value)) return "<missing>";
            string line = value.Replace('\r', ' ').Replace('\n', ' ');
            return line.Length <= 384 ? line : line.Substring(0, 384) + "…";
        }

        private static string RawHash(string path)
        {
            using SHA256 hash = SHA256.Create();
            using FileStream stream = File.OpenRead(path);
            // Match SecondaryMotionBakeAssetSetup.ContentHash's uppercase SHA-256.
            return BitConverter.ToString(hash.ComputeHash(stream)).Replace("-", string.Empty);
        }
    }
}

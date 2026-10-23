using System;
using System.Collections.Generic;
using UnityEngine;

namespace BarPromenade
{
    public static class LocalizationService
    {
        [Serializable]
        private sealed class Catalog
        {
            public Entry[] entries = Array.Empty<Entry>();
        }

        [Serializable]
        private sealed class Entry
        {
            public string key = string.Empty;
            public string value = string.Empty;
        }

        private static readonly Dictionary<string, string> Values =
            new Dictionary<string, string>();
        private static bool loaded;

        public static string Get(string key)
        {
            EnsureLoaded();
            return Values.TryGetValue(key, out string value) ? value : key;
        }

        private static void EnsureLoaded()
        {
            if (loaded)
            {
                return;
            }

            loaded = true;
            Values.Clear();
            // New text is Russian. Existing English overlays it where present.
            LoadCatalog("ru");
            string language = Application.systemLanguage == SystemLanguage.Russian
                ? "ru"
                : "en";
            if (language != "ru") LoadCatalog(language);
        }

        private static void LoadCatalog(string language)
        {
            TextAsset asset = Resources.Load<TextAsset>(
                $"Localization/{language}");
            if (asset == null)
            {
                Debug.LogError(
                    $"Localization catalog '{language}' is missing.");
                return;
            }

            Catalog catalog = JsonUtility.FromJson<Catalog>(asset.text);
            if (catalog?.entries == null)
            {
                return;
            }

            for (int i = 0; i < catalog.entries.Length; i++)
            {
                Entry entry = catalog.entries[i];
                if (entry != null && !string.IsNullOrWhiteSpace(entry.key))
                {
                    if (!string.IsNullOrEmpty(entry.value)) Values[entry.key] = entry.value;
                }
            }
        }
            //  Domain reload is disabled on entering play mode, so a static
        //  field survives from one run to the next. A cached
        //  UnityEngine.Object survives as a DESTROYED one, which reads
        //  as null-ish but throws on use. This hook runs before the
        //  first scene of every run, reload or not.

        [RuntimeInitializeOnLoadMethod(
            RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetCachedResources()
        {
            loaded = false;
        }
}
}

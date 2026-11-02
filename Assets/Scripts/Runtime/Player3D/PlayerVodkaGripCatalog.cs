using System;
using System.Collections.Generic;
using UnityEngine;

namespace BarPromenade
{
    /// <summary>Shared original-hand mesh extensions with one Blender-authored bottle morph.</summary>
    public sealed class PlayerVodkaGripCatalog : ScriptableObject
    {
        [Serializable] public sealed class Entry
        {
            public Mesh Source, Extended;
            public int[] PalmPlane = Array.Empty<int>(), ThumbBaseRing = Array.Empty<int>(), ThumbTipRing = Array.Empty<int>();
            public Entry(Mesh source, Mesh extended, int[] palmPlane, int[] thumbBase, int[] thumbTip)
            { Source = source; Extended = extended; PalmPlane = palmPlane; ThumbBaseRing = thumbBase; ThumbTipRing = thumbTip; }
        }
        [SerializeField] private Entry[] entries = Array.Empty<Entry>();
        [SerializeField] private Vector3 centre, axis, palm;
        private static PlayerVodkaGripCatalog cached;
        private static bool loaded;
        public void Configure(Entry[] values, Vector3 cylinderCentre, Vector3 cylinderAxis, Vector3 palmNormal)
        { entries = values; centre = cylinderCentre; axis = cylinderAxis; palm = palmNormal; }
        private static PlayerVodkaGripCatalog Load()
        {
            if (!loaded) { cached = Resources.Load<PlayerVodkaGripCatalog>("Player/VodkaGripCatalog"); loaded = true; }
            return cached;
        }
        public static void Apply(IReadOnlyList<SkinnedMeshRenderer> renderers)
        {
            PlayerVodkaGripCatalog catalog = Load();
            if (catalog == null) return;
            foreach (SkinnedMeshRenderer renderer in renderers)
            {
                if (renderer == null) continue;
                foreach (Entry entry in catalog.entries)
                    if (renderer.sharedMesh == entry.Source) { renderer.sharedMesh = entry.Extended; break; }
            }
        }
        public static bool TryGetEntry(Mesh mesh, out Entry found)
        {
            PlayerVodkaGripCatalog catalog = Load();
            if (catalog != null)
                foreach (Entry entry in catalog.entries)
                    if (entry.Source == mesh || entry.Extended == mesh) { found = entry; return true; }
            found = null; return false;
        }
        public static AnatomicalHandFrame Frame(AnatomicalHandFrame neutral)
        {
            PlayerVodkaGripCatalog catalog = Load();
            if (catalog == null) throw new InvalidOperationException("Missing authored bottle-grip hand catalog.");
            return new AnatomicalHandFrame(neutral.PalmCentreInHand, neutral.FingerDirectionInHand,
                catalog.axis, catalog.palm, catalog.centre);
        }
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetShared() { loaded = false; cached = null; }
    }
}

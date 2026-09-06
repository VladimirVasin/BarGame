using System;
using System.Collections.Generic;
using UnityEngine;

namespace BarPromenade
{
    /// <summary>Only live loading overlays retain their selected illustration.</summary>
    internal sealed class AreaLoadingArtworkCache
    {
        internal sealed class Entry
        {
            public readonly string Path;
            public readonly Texture2D Texture;
            public int References;

            public Entry(string path, Texture2D texture)
            {
                Path = path;
                Texture = texture;
            }
        }

        internal sealed class Lease : IDisposable
        {
            private AreaLoadingArtworkCache owner;
            private readonly Entry entry;

            public Texture2D Texture => owner != null ? entry.Texture : null;

            internal Lease(AreaLoadingArtworkCache owner, Entry entry)
            {
                this.owner = owner;
                this.entry = entry;
                entry.References++;
            }

            public void Dispose()
            {
                AreaLoadingArtworkCache previous = owner;
                owner = null;
                previous?.Release(entry);
            }
        }

        internal static readonly AreaLoadingArtworkCache Shared =
            new AreaLoadingArtworkCache(
                Resources.Load<Texture2D>, Resources.UnloadAsset);

        private readonly Dictionary<string, Entry> entries =
            new Dictionary<string, Entry>(StringComparer.Ordinal);
        private readonly Func<string, Texture2D> load;
        private readonly Action<Texture2D> unload;

        internal AreaLoadingArtworkCache(
            Func<string, Texture2D> load, Action<Texture2D> unload)
        {
            this.load = load ?? throw new ArgumentNullException(nameof(load));
            this.unload = unload ?? throw new ArgumentNullException(nameof(unload));
        }

        internal Lease Acquire(string path)
        {
            if (string.IsNullOrEmpty(path)) return null;
            if (entries.TryGetValue(path, out Entry existing))
            {
                return new Lease(this, existing);
            }

            Texture2D texture = load(path);
            if (texture == null) return null;
            var entry = new Entry(path, texture);
            entries.Add(path, entry);
            return new Lease(this, entry);
        }

        private void Release(Entry entry)
        {
            if (--entry.References != 0) return;
            entries.Remove(entry.Path);
            if (entry.Texture != null) unload(entry.Texture);
        }
    }
}

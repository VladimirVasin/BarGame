using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using UnityEngine;

namespace BarPromenade
{
    public sealed class CityWorldResult
    {
        private readonly Dictionary<string, BarEntrance> barsById;

        internal CityWorldResult(
            GameObject root,
            RoadWalkableArea walkableArea,
            IList<BarEntrance> bars,
            Bounds bounds)
        {
            Root = root;
            WalkableArea = walkableArea;
            Bars = new ReadOnlyCollection<BarEntrance>(
                new List<BarEntrance>(bars));
            Bounds = bounds;
            barsById = new Dictionary<string, BarEntrance>(
                StringComparer.Ordinal);

            for (int i = 0; i < Bars.Count; i++)
            {
                BarEntrance bar = Bars[i];
                barsById.Add(bar.BarId, bar);
            }
        }

        public GameObject Root { get; }
        public RoadWalkableArea WalkableArea { get; }
        public IReadOnlyList<BarEntrance> Bars { get; }
        public Bounds Bounds { get; }

        public bool TryGetBar(string barId, out BarEntrance entrance)
        {
            return barsById.TryGetValue(barId ?? string.Empty, out entrance);
        }
    }
}

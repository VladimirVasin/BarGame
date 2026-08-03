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
            HomeEntrance playerHome,
            SupermarketEntrance supermarket,
            RoadFencePlan fencePlan,
            GameObject parkRoot,
            GameObject districtPointOfInterestRoot,
            CityDecorationPlan decorationPlan,
            GameObject decorationRoot,
            Bounds bounds)
        {
            Root = root;
            WalkableArea = walkableArea;
            Bars = new ReadOnlyCollection<BarEntrance>(
                new List<BarEntrance>(bars));
            PlayerHome = playerHome;
            Supermarket = supermarket;
            FencePlan = fencePlan ??
                throw new ArgumentNullException(nameof(fencePlan));
            ParkRoot = parkRoot;
            DistrictPointOfInterestRoot =
                districtPointOfInterestRoot != null
                    ? districtPointOfInterestRoot
                    : throw new ArgumentNullException(
                        nameof(districtPointOfInterestRoot));
            DecorationPlan = decorationPlan ??
                throw new ArgumentNullException(nameof(decorationPlan));
            DecorationRoot = decorationRoot != null
                ? decorationRoot
                : throw new ArgumentNullException(nameof(decorationRoot));
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
        public HomeEntrance PlayerHome { get; }
        public SupermarketEntrance Supermarket { get; }
        public RoadFencePlan FencePlan { get; }
        public GameObject ParkRoot { get; }
        public GameObject DistrictPointOfInterestRoot { get; }
        public CityDecorationPlan DecorationPlan { get; }
        public GameObject DecorationRoot { get; }
        public Bounds Bounds { get; }

        public bool TryGetBar(string barId, out BarEntrance entrance)
        {
            return barsById.TryGetValue(barId ?? string.Empty, out entrance);
        }
    }
}

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
            CityChurchPlan churchPlan,
            ChurchEntrance church,
            RoadFencePlan fencePlan,
            GameObject parkRoot,
            GameObject districtPointOfInterestRoot,
            CityOpenAreaDecorationPlan openAreaDecorationPlan,
            GameObject openAreaDecorationRoot,
            CityCemeteryPlan cemeteryPlan,
            CityCemeteryGroundExcavation cemeteryGroundExcavation,
            CitySeacoastPlan seacoastPlan,
            CityDecorationPlan decorationPlan,
            GameObject decorationRoot,
            GameObject riverRoot,
            IReadOnlyList<Transform> riverQuayLampAnchors,
            CityMountainBoundaryPlan mountainBoundaryPlan,
            GameObject mountainBoundaryRoot,
            CityFringeYardPlan fringeYardPlan,
            CityFringeYardWorldResult fringeYard,
            CityMountainBackdropWorldResult mountainBackdrop,
            CityWindDressingPlan windDressingPlan,
            GameObject windDressingRoot,
            Bounds bounds)
        {
            Root = root;
            WalkableArea = walkableArea;
            Bars = new ReadOnlyCollection<BarEntrance>(
                new List<BarEntrance>(bars));
            PlayerHome = playerHome;
            Supermarket = supermarket;
            ChurchPlan = churchPlan;
            Church = church;
            FencePlan = fencePlan ??
                throw new ArgumentNullException(nameof(fencePlan));
            ParkRoot = parkRoot;
            DistrictPointOfInterestRoot =
                districtPointOfInterestRoot != null
                    ? districtPointOfInterestRoot
                    : throw new ArgumentNullException(
                        nameof(districtPointOfInterestRoot));
            OpenAreaDecorationPlan = openAreaDecorationPlan ??
                throw new ArgumentNullException(
                    nameof(openAreaDecorationPlan));
            OpenAreaDecorationRoot = openAreaDecorationRoot != null
                ? openAreaDecorationRoot
                : throw new ArgumentNullException(
                    nameof(openAreaDecorationRoot));
            // Null when the blueprint carries no dressable cemetery.
            CemeteryPlan = cemeteryPlan;
            // Null with it: no cemetery ground, nothing to dig into.
            CemeteryGroundExcavation = cemeteryGroundExcavation;
            // Null when the blueprint carries no dressable seacoast.
            SeacoastPlan = seacoastPlan;
            DecorationPlan = decorationPlan ??
                throw new ArgumentNullException(nameof(decorationPlan));
            DecorationRoot = decorationRoot != null
                ? decorationRoot
                : throw new ArgumentNullException(nameof(decorationRoot));
            RiverRoot = riverRoot;
            RiverQuayLampAnchors = riverQuayLampAnchors ??
                Array.Empty<Transform>();
            MountainBoundaryPlan = mountainBoundaryPlan ??
                throw new ArgumentNullException(
                    nameof(mountainBoundaryPlan));
            MountainBoundaryRoot = mountainBoundaryRoot;
            FringeYardPlan = fringeYardPlan ??
                throw new ArgumentNullException(nameof(fringeYardPlan));
            FringeYard = fringeYard;
            MountainBackdrop = mountainBackdrop;
            WindDressingPlan = windDressingPlan ??
                throw new ArgumentNullException(
                    nameof(windDressingPlan));
            WindDressingRoot = windDressingRoot != null
                ? windDressingRoot
                : throw new ArgumentNullException(
                    nameof(windDressingRoot));
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
        public CityChurchPlan ChurchPlan { get; }
        public ChurchEntrance Church { get; }
        public RoadFencePlan FencePlan { get; }
        public GameObject ParkRoot { get; }
        public GameObject DistrictPointOfInterestRoot { get; }
        public CityOpenAreaDecorationPlan OpenAreaDecorationPlan { get; }
        public GameObject OpenAreaDecorationRoot { get; }
        public CityCemeteryPlan CemeteryPlan { get; }

        /// <summary>
        /// The register of holes cut out of the cemetery ground, and
        /// the only sanctioned way to open a new one.
        /// </summary>
        public CityCemeteryGroundExcavation CemeteryGroundExcavation
        {
            get;
        }

        public CitySeacoastPlan SeacoastPlan { get; }
        public CityDecorationPlan DecorationPlan { get; }
        public GameObject DecorationRoot { get; }
        public GameObject RiverRoot { get; }

        /// <summary>
        /// The waterside lantern anchors on the quay wall faces —
        /// candidates for the night atmosphere's pooled lights, so
        /// the fixtures nearest the player burn with real light.
        /// Empty when the layout carries no river.
        /// </summary>
        public IReadOnlyList<Transform> RiverQuayLampAnchors { get; }

        public CityMountainBoundaryPlan MountainBoundaryPlan { get; }
        public GameObject MountainBoundaryRoot { get; }
        public CityFringeYardPlan FringeYardPlan { get; }
        public CityFringeYardWorldResult FringeYard { get; }
        public GameObject FringeYardRoot => FringeYard?.Root;
        public IReadOnlyList<CityFringePracticalAnchor>
            FringePracticalAnchors => FringeYard != null
                ? FringeYard.PracticalAnchors
                : Array.Empty<CityFringePracticalAnchor>();
        public CityMountainBackdropWorldResult MountainBackdrop { get; }
        public CityWindDressingPlan WindDressingPlan { get; }
        public GameObject WindDressingRoot { get; }
        public Bounds Bounds { get; }

        public bool TryGetBar(string barId, out BarEntrance entrance)
        {
            return barsById.TryGetValue(barId ?? string.Empty, out entrance);
        }
    }
}

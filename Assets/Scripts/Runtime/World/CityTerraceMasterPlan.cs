using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using UnityEngine;

namespace BarPromenade
{
    public readonly struct CityTerraceRoadConflict
    {
        internal CityTerraceRoadConflict(RoadEdge edge, float currentGrade,
            float proposedGrade, float allowedGrade, float requiredHorizontalRun)
        {
            Edge = edge;
            CurrentGradePercent = currentGrade;
            ProposedGradePercent = proposedGrade;
            AllowedGradePercent = allowedGrade;
            RequiredHorizontalRun = requiredHorizontalRun;
        }

        public RoadEdge Edge { get; }
        public float CurrentGradePercent { get; }
        public float ProposedGradePercent { get; }
        public float AllowedGradePercent { get; }
        /// <summary>Required total planar length, including both existing flat node setbacks.</summary>
        public float RequiredHorizontalRun { get; }
    }

    /// <summary>
    /// Authoring study of the coastal city's proposed terraces. It neither
    /// replaces the live elevation sampler nor declares a viable road route.
    /// The editor consumes the proposal and its conflicts before any terrain,
    /// building, doorway or transport placement is changed.
    /// </summary>
    public sealed class CityTerraceMasterPlan
    {
        private const float GradeTolerance = .001f;

        private CityTerraceMasterPlan(CityLayout layout, float summitElevation)
        {
            BlueprintId = layout.BlueprintId;
            Seed = layout.Seed;
            TargetSummitElevation = summitElevation;
            CityRiverDefinition river = layout.Blueprint.River;
            int minimumX = int.MaxValue, maximumX = int.MinValue;
            foreach (CityBlueprintCell cell in layout.Blueprint.Cells)
            {
                if (cell.Area.Category != CityAreaCategory.UrbanBuilt) continue;
                minimumX = Mathf.Min(minimumX, cell.Cell.x);
                maximumX = Mathf.Max(maximumX, cell.Cell.x + 1);
            }

            Vector2 southWest = WorldXZ(layout, new Vector2Int(minimumX, river.CoreMinimumZ));
            Vector2 northEast = WorldXZ(layout, new Vector2Int(maximumX, river.CoreMaximumZExclusive));
            CoreWorldXZBounds = Rect.MinMaxRect(southWest.x, southWest.y, northEast.x, northEast.y);
            float westBankX = WorldXZ(layout, new Vector2Int(river.CorridorCellX, river.CoreMinimumZ)).x;
            float eastBankX = WorldXZ(layout, new Vector2Int(river.CorridorCellX + 1, river.CoreMinimumZ)).x;
            float westLowX = WorldXZ(layout, new Vector2Int(river.CorridorCellX - 1, river.CoreMinimumZ)).x;
            float eastLowX = WorldXZ(layout, new Vector2Int(river.CorridorCellX + 2, river.CoreMinimumZ)).x;
            var elevations = new Dictionary<Vector2Int, float>(layout.Nodes.Count);
            CurrentMinimumElevation = TargetMinimumElevation = float.PositiveInfinity;
            CurrentMaximumElevation = TargetMaximumElevation = float.NegativeInfinity;
            TransportClimbStartElevation = float.PositiveInfinity;

            foreach (Vector2Int node in layout.Nodes)
            {
                float current = layout.ElevationPlan.GetNodeElevation(node);
                float proposed = current;
                bool inCore = node.x >= minimumX && node.x <= maximumX &&
                    node.y >= river.CoreMinimumZ && node.y <= river.CoreMaximumZExclusive;
                if (inCore && node.y == river.CoreMaximumZExclusive)
                    TransportClimbStartElevation = Mathf.Min(TransportClimbStartElevation, current);

                // Preserve the shore, both river banks, their first adjacent
                // node and every external/service node exactly as surveyed.
                if (inCore && node.y < river.CoreMaximumZExclusive &&
                    (node.x < river.CorridorCellX - 1 || node.x > river.CorridorCellX + 2))
                {
                    Vector2 point = WorldXZ(layout, node);
                    bool west = point.x < westBankX;
                    float bankDistance = west ? westLowX - point.x : point.x - eastLowX;
                    float bankSpan = west ? westBankX - southWest.x : northEast.x - eastBankX;
                    float blend = Mathf.SmoothStep(0f, 1f, bankDistance / (bankSpan * .5f));
                    float inland = Mathf.InverseLerp(northEast.y, southWest.y, point.y);
                    // The banks' broad shelves turn at slightly different
                    // depths; these are an authored section, not random noise.
                    float stagger = west ? -.035f : .035f;
                    float section = Mathf.Clamp01(inland + stagger * Mathf.Sin(inland * Mathf.PI));
                    float target = SampleShelfFraction(section) * summitElevation;
                    proposed = Mathf.Max(current, Mathf.Lerp(current, target, blend));
                }

                elevations.Add(node, proposed);
                CurrentMinimumElevation = Mathf.Min(CurrentMinimumElevation, current);
                CurrentMaximumElevation = Mathf.Max(CurrentMaximumElevation, current);
                TargetMinimumElevation = Mathf.Min(TargetMinimumElevation, proposed);
                TargetMaximumElevation = Mathf.Max(TargetMaximumElevation, proposed);
            }

            NodeElevations = new ReadOnlyDictionary<Vector2Int, float>(elevations);
            MinimumTransportClimbLength = Mathf.Max(0f, summitElevation - TransportClimbStartElevation) /
                (CityElevationPlan.MaximumBusGradePercent * .01f);
            var conflicts = new List<CityTerraceRoadConflict>();
            var edges = new List<RoadEdge>(layout.RoadEdges);
            edges.Sort(RoadEdge.Compare);
            foreach (RoadEdge edge in edges)
            {
                float setbacks = layout.RoadGeometry.GetEndpointInset(edge, edge.A) +
                    layout.RoadGeometry.GetEndpointInset(edge, edge.B);
                float run = Mathf.Max(.01f, layout.RoadGeometry.Get(edge).Length - setbacks);
                float rise = Mathf.Abs(elevations[edge.B] - elevations[edge.A]);
                float grade = rise / run * 100f;
                float allowed = layout.GetPathKind(edge) == CityPathKind.Street
                    ? CityElevationPlan.MaximumBusGradePercent : CityElevationPlan.MaximumPedestrianGradePercent;
                if (grade <= allowed + GradeTolerance) continue;
                float currentGrade = Mathf.Abs(layout.ElevationPlan.GetNodeElevation(edge.B) -
                    layout.ElevationPlan.GetNodeElevation(edge.A)) / run * 100f;
                conflicts.Add(new CityTerraceRoadConflict(edge, currentGrade, grade, allowed,
                    rise / (allowed * .01f) + setbacks));
            }
            RoadConflicts = new ReadOnlyCollection<CityTerraceRoadConflict>(conflicts);
        }

        public string BlueprintId { get; }
        public int Seed { get; }
        public float TargetSummitElevation { get; }
        public Rect CoreWorldXZBounds { get; }
        public IReadOnlyDictionary<Vector2Int, float> NodeElevations { get; }
        public IReadOnlyList<CityTerraceRoadConflict> RoadConflicts { get; }
        public float CurrentMinimumElevation { get; }
        public float CurrentMaximumElevation { get; }
        public float TargetMinimumElevation { get; }
        public float TargetMaximumElevation { get; }
        public float TransportClimbStartElevation { get; }
        /// <summary>
        /// Lower bound from the lowest preserved coastal road node to the
        /// summit at six percent. Excludes landings, turns and route feasibility.
        /// </summary>
        public float MinimumTransportClimbLength { get; }

        public float GetNodeElevation(Vector2Int node) => NodeElevations[node];

        public static CityTerraceMasterPlan Create(CityLayout layout, float summitElevation = 40f)
        {
            if (layout == null) throw new ArgumentNullException(nameof(layout));
            if (float.IsNaN(summitElevation) || float.IsInfinity(summitElevation) ||
                summitElevation < 35f || summitElevation > 45f)
                throw new ArgumentOutOfRangeException(nameof(summitElevation), "The coastal terrace study requires a 35–45 m summit.");
            if (layout.BlueprintId != CityBlueprintCatalog.DefaultBlueprintId ||
                !layout.SpatialPlan.IsCompactCoastal || !layout.ElevationPlan.IsElevated || layout.Blueprint.River == null)
                throw new ArgumentException("The terrace study supports only the production coastal footprint.", nameof(layout));
            return new CityTerraceMasterPlan(layout, summitElevation);
        }

        private static float SampleShelfFraction(float inland)
        {
            if (inland < .18f) return Mathf.Lerp(0f, .25f, Mathf.SmoothStep(0f, 1f, inland / .18f));
            if (inland < .30f) return .25f;
            if (inland < .54f) return Mathf.Lerp(.25f, .60f, Mathf.SmoothStep(0f, 1f, (inland - .30f) / .24f));
            if (inland < .66f) return .60f;
            if (inland < .90f) return Mathf.Lerp(.60f, 1f, Mathf.SmoothStep(0f, 1f, (inland - .66f) / .24f));
            return 1f;
        }

        private static Vector2 WorldXZ(CityLayout layout, Vector2Int node) =>
            layout.SpatialPlan.GetCoordinateWorldOffset(node) + new Vector2(layout.WorldOrigin.x, layout.WorldOrigin.z);
    }
}

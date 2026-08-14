using System;
using System.Collections.Generic;
using UnityEngine;

namespace BarPromenade
{
    public static class CityElevationValidator
    {
        private const float Tolerance = 0.001f;

        public static void ValidateOrThrow(
            CityElevationPlan plan,
            CityBlueprint blueprint,
            IReadOnlyList<Vector2Int> nodes,
            IReadOnlyList<RoadEdge> roads,
            IReadOnlyDictionary<RoadEdge, CityPathKind> pathKinds)
        {
            if (plan == null)
            {
                throw new ArgumentNullException(nameof(plan));
            }

            if (blueprint == null || nodes == null || roads == null ||
                pathKinds == null)
            {
                throw new ArgumentNullException(
                    "Elevation validation requires complete layout input.");
            }

            if (plan.NodeElevations.Count != nodes.Count ||
                plan.CellElevations.Count != blueprint.Cells.Count ||
                plan.Transitions.Count != roads.Count)
            {
                throw new InvalidOperationException(
                    "Elevation plan coverage does not match city topology.");
            }

            for (int index = 0; index < nodes.Count; index++)
            {
                float elevation = plan.GetNodeElevation(nodes[index]);
                ValidateFinite(elevation, "node elevation");
            }

            for (int index = 0; index < blueprint.Cells.Count; index++)
            {
                CityBlueprintCell cell = blueprint.Cells[index];
                float elevation = plan.GetCellElevation(cell.Cell);
                ValidateFinite(elevation, "cell elevation");
                if (cell.IsWater)
                {
                    float expected = cell.Area.Feature ==
                                     CityAreaFeatureKind.Lake
                            ? (plan.IsElevated ? 1f : 0f)
                            : 0f;
                    if (Mathf.Abs(elevation - expected) > Tolerance)
                    {
                        throw new InvalidOperationException(
                            $"Water cell {cell.Cell} drifted from its datum.");
                    }
                }
            }

            ValidateRiverValley(plan, blueprint.River);

            for (int index = 0; index < roads.Count; index++)
            {
                RoadEdge edge = roads[index];
                CityElevationTransitionDescriptor transition =
                    plan.GetTransition(edge);
                if (transition.Edge != edge ||
                    transition.PathKind != pathKinds[edge] ||
                    transition.Kind ==
                    CityElevationTransitionKind.PedestrianStair ||
                    transition.Kind ==
                    CityElevationTransitionKind.ProtectedDrop)
                {
                    throw new InvalidOperationException(
                        $"Travel edge {edge} has invalid transition metadata.");
                }

                float maximum = transition.PathKind == CityPathKind.Street
                    ? CityElevationPlan.MaximumBusGradePercent
                    : CityElevationPlan.MaximumPedestrianGradePercent;
                if (!IsFinite(transition.GradePercent) ||
                    transition.GradePercent > maximum + Tolerance)
                {
                    throw new InvalidOperationException(
                        $"Travel edge {edge} exceeds its {maximum:0.0}% " +
                        "grade contract.");
                }
            }

            var stairDistricts = new HashSet<CityDistrictKind>();
            var stairEdges = new HashSet<RoadEdge>();
            for (int index = 0; index < plan.SignatureStairs.Count; index++)
            {
                CityElevationStairDescriptor stair =
                    plan.SignatureStairs[index];
                if (string.IsNullOrWhiteSpace(stair.Id) ||
                    !stairEdges.Add(stair.Edge) ||
                    !stairDistricts.Add(stair.District) ||
                    stair.Side == CityElevationStairSide.None ||
                    stair.StepCount < 6 || stair.StepCount > 12 ||
                    stair.StepRise < 0.15f - Tolerance ||
                    stair.StepRise > 0.17f + Tolerance ||
                    stair.TreadDepth < 0.30f - Tolerance ||
                    stair.TreadDepth > 0.34f + Tolerance ||
                    stair.Width < 1.6f - Tolerance ||
                    stair.LandingLength < 1.5f - Tolerance ||
                    Mathf.Abs(
                        stair.TotalRise -
                        Mathf.Abs(
                            plan.GetNodeElevation(stair.UpperNode) -
                            plan.GetNodeElevation(stair.LowerNode))) >
                    Tolerance)
                {
                    throw new InvalidOperationException(
                        $"Signature stair '{stair.Id}' is invalid.");
                }

                CityElevationTransitionDescriptor transition =
                    plan.GetTransition(stair.Edge);
                if ((transition.Mobility & CityTraversalMobility.Bus) == 0)
                {
                    throw new InvalidOperationException(
                        "The parallel road grade must preserve Route 01 " +
                        "mobility beside every signature stair.");
                }
            }

            if (plan.IsElevated)
            {
                ValidateRequiredDistrictStairs(plan, stairDistricts);
            }
        }

        private static void ValidateRiverValley(
            CityElevationPlan plan,
            CityRiverDefinition river)
        {
            if (river == null)
            {
                return;
            }

            float previousWater = float.PositiveInfinity;
            for (int z = river.CoreMinimumZ;
                 z <= river.CoreMaximumZExclusive;
                 z++)
            {
                float water = CityRiverPlanner.ResolveWaterY(river, z);
                if (water > previousWater + Tolerance)
                {
                    throw new InvalidOperationException(
                        "River water must descend monotonically towards " +
                        "the northern sea.");
                }

                var westBank = new Vector2Int(river.CorridorCellX, z);
                var eastBank = new Vector2Int(river.CorridorCellX + 1, z);
                if (!plan.TryGetNodeElevation(westBank, out float westY) ||
                    !plan.TryGetNodeElevation(eastBank, out float eastY) ||
                    Mathf.Abs(westY - eastY) > Tolerance ||
                    westY <= water + Tolerance)
                {
                    throw new InvalidOperationException(
                        $"River banks at row {z} require a shared safe " +
                        "promenade datum above the water.");
                }

                previousWater = water;
            }
        }

        private static void ValidateRequiredDistrictStairs(
            CityElevationPlan plan,
            ISet<CityDistrictKind> stairDistricts)
        {
            CityDistrictKind[] requiredDistricts =
            {
                CityDistrictKind.OldTown,
                CityDistrictKind.Residential,
                CityDistrictKind.Industrial,
                CityDistrictKind.Nightlife
            };
            for (int index = 0; index < requiredDistricts.Length; index++)
            {
                CityDistrictKind district = requiredDistricts[index];
                if (!plan.Profiles.TryGetValue(
                        district,
                        out DistrictElevationProfile profile) ||
                    profile.PreferredStairConnections <= 0 ||
                    !stairDistricts.Contains(district))
                {
                    throw new InvalidOperationException(
                        $"Elevated district '{district}' has no required " +
                        "signature stair connection.");
                }
            }
        }

        private static void ValidateFinite(float value, string label)
        {
            if (!IsFinite(value))
            {
                throw new InvalidOperationException(
                    $"The {label} must be finite.");
            }
        }

        private static bool IsFinite(float value)
        {
            return !float.IsNaN(value) && !float.IsInfinity(value);
        }
    }
}

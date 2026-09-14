using System;
using System.Collections.Generic;
using UnityEngine;

namespace BarPromenade
{
    internal sealed class CityFringeYardGroundWorldResult
    {
        internal CityFringeYardGroundWorldResult(
            GameObject genericGround,
            GameObject mountainGround)
        {
            GenericGround = genericGround;
            MountainGround = mountainGround;
        }

        internal GameObject GenericGround { get; }
        internal GameObject MountainGround { get; }
    }

    /// <summary>
    /// Splits OpenGround by the stable area IDs declared by the authored
    /// fringe plan. Both halves remain the authoritative conforming terrain
    /// skin; only the four mountain-facing source areas receive the measured
    /// forefield sheet. The eastern Yard and every unplanned/custom area stay
    /// in the ordinary flat-colour YardGround batch.
    /// </summary>
    internal static class CityFringeYardGroundWorldBuilder
    {
        internal const string GenericGroundObjectName = "Yard Ground";
        internal const string MountainGroundObjectName =
            "Mountain Forefield Ground";

        internal static CityFringeYardGroundWorldResult Build(
            Transform parent,
            CityLayout layout,
            CityFringeYardPlan fringePlan)
        {
            if (parent == null)
            {
                throw new ArgumentNullException(nameof(parent));
            }

            if (layout == null)
            {
                throw new ArgumentNullException(nameof(layout));
            }

            if (fringePlan == null)
            {
                throw new ArgumentNullException(nameof(fringePlan));
            }

            List<string> mountainAreaIds =
                CollectMountainAreaIds(fringePlan);
            // Both branches wear the forefield sheet. The split is about
            // which yards get the mountain belt's dressing, not about
            // whether their ground has a surface at all: the eastern yards
            // used to be a hundred by two hundred metres of flat brown
            // default material with a hard seam against the textured
            // asphalt two metres away. The sheet's own measured source
            // tint is YardGround, so it needs no new art to sit here.
            HomeSurfaceRecipe groundRecipe =
                CityFringeYardSurfaceAppearance.GetRecipe(
                    CityFringeYardSurfaceKind.ForefieldGround);
            CityEastExitPlan eastExit = CityEastExitPlanner.Create(layout);
            IReadOnlyList<Rect> roadCut = eastExit.IsEnabled
                ? new[] { eastExit.RoadBounds } : null;
            GameObject genericGround =
                CityTerrainSurfaceWorldBuilder.Build(
                    GenericGroundObjectName,
                    parent,
                    layout,
                    CitySurfaceKind.OpenGround,
                    CityExteriorAppearance.YardGround,
                    false,
                    groundRecipe.MetersPerTile,
                    roadCut,
                    CityTerrainSurfaceAreaFilter.Excluding(
                        mountainAreaIds));
            FootstepGround.Stamp(genericGround, FootstepGroundKind.Soil);
            if (genericGround != null)
            {
                CityFringeYardSurfaceAppearance.ApplyCombined(
                    genericGround.GetComponent<Renderer>(),
                    CityFringeYardSurfaceKind.ForefieldGround,
                    CityExteriorAppearance.YardGround);
            }

            GameObject mountainGround = null;
            if (mountainAreaIds.Count > 0)
            {
                HomeSurfaceRecipe recipe = groundRecipe;
                mountainGround = CityTerrainSurfaceWorldBuilder.Build(
                    MountainGroundObjectName,
                    parent,
                    layout,
                    CitySurfaceKind.OpenGround,
                    CityExteriorAppearance.YardGround,
                    false,
                    recipe.MetersPerTile,
                    null,
                    CityTerrainSurfaceAreaFilter.IncludeOnly(
                        mountainAreaIds));
                if (mountainGround != null)
                {
                    FootstepGround.Stamp(
                        mountainGround,
                        FootstepGroundKind.Soil);
                    CityFringeYardSurfaceAppearance.ApplyCombined(
                        mountainGround.GetComponent<Renderer>(),
                        CityFringeYardSurfaceKind.ForefieldGround,
                        CityExteriorAppearance.YardGround);
                }
            }

            return new CityFringeYardGroundWorldResult(
                genericGround,
                mountainGround);
        }

        private static List<string> CollectMountainAreaIds(
            CityFringeYardPlan fringePlan)
        {
            var result = new List<string>(4);
            var unique = new HashSet<string>(StringComparer.Ordinal);
            for (int index = 0; index < fringePlan.Yards.Count; index++)
            {
                CityFringeYardDescriptor yard = fringePlan.Yards[index];
                if (!IsMountainFacing(yard.Kind))
                {
                    continue;
                }

                if (string.IsNullOrWhiteSpace(yard.AreaId) ||
                    !unique.Add(yard.AreaId))
                {
                    throw new InvalidOperationException(
                        "Mountain fringe ground requires unique, non-empty " +
                        "source area IDs.");
                }

                result.Add(yard.AreaId);
            }

            return result;
        }

        private static bool IsMountainFacing(CityFringeYardKind kind)
        {
            switch (kind)
            {
                case CityFringeYardKind.WestStoneTerraces:
                case CityFringeYardKind.WestIndustrialBelt:
                case CityFringeYardKind.SouthTunnelForecourt:
                case CityFringeYardKind.SouthFloodWorks:
                    return true;
                case CityFringeYardKind.EastUtilityEdge:
                    return false;
                default:
                    throw new ArgumentOutOfRangeException(
                        nameof(kind),
                        kind,
                        null);
            }
        }
    }
}

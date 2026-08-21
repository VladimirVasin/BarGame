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
            GameObject genericGround =
                CityTerrainSurfaceWorldBuilder.Build(
                    GenericGroundObjectName,
                    parent,
                    layout,
                    CitySurfaceKind.OpenGround,
                    CityExteriorAppearance.YardGround,
                    false,
                    null,
                    null,
                    CityTerrainSurfaceAreaFilter.Excluding(
                        mountainAreaIds));

            GameObject mountainGround = null;
            if (mountainAreaIds.Count > 0)
            {
                HomeSurfaceRecipe recipe =
                    CityFringeYardSurfaceAppearance.GetRecipe(
                        CityFringeYardSurfaceKind.ForefieldGround);
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

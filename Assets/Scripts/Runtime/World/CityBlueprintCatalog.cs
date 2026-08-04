using System;
using System.Collections.Generic;
using UnityEngine;

namespace BarPromenade
{
    public static class CityBlueprintCatalog
    {
        public const string DefaultBlueprintId = "default-coastal";
        public const string LegacyBlueprintId = "legacy-rectangular";

        private static readonly Color OldTownMapColor =
            new Color32(113, 93, 72, 255);
        private static readonly Color ResidentialMapColor =
            new Color32(102, 119, 105, 255);
        private static readonly Color IndustrialMapColor =
            new Color32(88, 98, 94, 255);
        private static readonly Color NightlifeMapColor =
            new Color32(108, 77, 103, 255);
        private static readonly Color ParkMapColor =
            new Color32(70, 105, 72, 255);
        private static readonly Color WaterfrontMapColor =
            new Color32(112, 119, 103, 255);
        private static readonly Color LakeMapColor =
            new Color32(66, 91, 99, 255);
        private static readonly Color CemeteryMapColor =
            new Color32(82, 91, 78, 255);

        private static CityBlueprint defaultBlueprint;

        public static CityBlueprint Default
        {
            get
            {
                if (defaultBlueprint == null)
                {
                    defaultBlueprint = CreateDefaultCoastal(
                        CityGenerationSettings.Default);
                }

                return defaultBlueprint;
            }
        }

        public static CityBlueprint Resolve(string blueprintId)
        {
            string normalized = string.IsNullOrWhiteSpace(blueprintId)
                ? DefaultBlueprintId
                : blueprintId.Trim();
            if (string.Equals(
                    normalized,
                    DefaultBlueprintId,
                    StringComparison.Ordinal))
            {
                return Default;
            }

            if (string.Equals(
                    normalized,
                    LegacyBlueprintId,
                    StringComparison.Ordinal))
            {
                return CreateLegacy(CityGenerationSettings.Default);
            }

            throw new ArgumentOutOfRangeException(
                nameof(blueprintId),
                blueprintId,
                "Unknown city blueprint ID.");
        }

        public static CityBlueprint CreateDefaultCoastal(
            CityGenerationSettings settings)
        {
            if (settings == null)
            {
                throw new ArgumentNullException(nameof(settings));
            }

            settings.Validate();
            CityBlueprintBuilder builder = CreateUrbanCoreBuilder(
                DefaultBlueprintId,
                settings,
                true,
                out bool hasPark);
            if (!hasPark)
            {
                throw new InvalidOperationException(
                    "The default coastal blueprint requires a centered " +
                    "central park.");
            }

            CityAreaDefinition waterfront = CreateNorthWaterfront();
            builder.AddRectangle(
                waterfront,
                new RectInt(
                    0,
                    settings.BlocksZ,
                    settings.BlocksX,
                    1),
                CityCellTopologyKind.OpenLand);
            builder.AddRectangle(
                waterfront,
                new RectInt(
                    0,
                    settings.BlocksZ + 1,
                    settings.BlocksX,
                    1),
                CityCellTopologyKind.Water);
            return builder.Build(true, true);
        }

        public static CityBlueprint CreateLegacy(
            CityGenerationSettings settings)
        {
            if (settings == null)
            {
                throw new ArgumentNullException(nameof(settings));
            }

            settings.Validate();
            CityBlueprintBuilder builder = CreateUrbanCoreBuilder(
                LegacyBlueprintId,
                settings,
                false,
                out bool hasPark);
            return builder.Build(hasPark, false);
        }

        public static CityAreaDefinition CreateUrbanArea(
            string id,
            CityDistrictKind archetype,
            string localizationKey,
            Color mapColor,
            bool requiresBar = false)
        {
            if (archetype == CityDistrictKind.CentralPark ||
                archetype == CityDistrictKind.NorthWaterfront ||
                archetype == CityDistrictKind.Lake ||
                archetype == CityDistrictKind.Cemetery)
            {
                throw new ArgumentOutOfRangeException(nameof(archetype));
            }

            return new CityAreaDefinition(
                id,
                archetype,
                CityAreaCategory.UrbanBuilt,
                CityAreaFeatureKind.UrbanDistrict,
                CityAreaPlacementPolicy.Movable,
                localizationKey,
                mapColor,
                requiresBar);
        }

        public static CityAreaDefinition CreateLake(
            string id = "lake",
            string localizationKey = "map.district.lake")
        {
            return new CityAreaDefinition(
                id,
                CityDistrictKind.Lake,
                CityAreaCategory.NonUrbanOpen,
                CityAreaFeatureKind.Lake,
                CityAreaPlacementPolicy.Movable,
                localizationKey,
                LakeMapColor);
        }

        public static CityAreaDefinition CreateCemetery(
            string id = "cemetery",
            string localizationKey = "map.district.cemetery")
        {
            return new CityAreaDefinition(
                id,
                CityDistrictKind.Cemetery,
                CityAreaCategory.NonUrbanOpen,
                CityAreaFeatureKind.Cemetery,
                CityAreaPlacementPolicy.Movable,
                localizationKey,
                CemeteryMapColor);
        }

        private static CityBlueprintBuilder CreateUrbanCoreBuilder(
            string blueprintId,
            CityGenerationSettings settings,
            bool requiresBars,
            out bool hasPark)
        {
            var centerNode = new Vector2Int(
                settings.BlocksX / 2,
                settings.BlocksZ / 2);
            var builder = new CityBlueprintBuilder(
                blueprintId,
                centerNode);
            CityAreaDefinition oldTown = CreateUrbanArea(
                "old-town",
                CityDistrictKind.OldTown,
                "map.district.old_town",
                OldTownMapColor,
                requiresBars);
            CityAreaDefinition residential = CreateUrbanArea(
                "residential",
                CityDistrictKind.Residential,
                "map.district.residential",
                ResidentialMapColor,
                requiresBars);
            CityAreaDefinition industrial = CreateUrbanArea(
                "industrial",
                CityDistrictKind.Industrial,
                "map.district.industrial",
                IndustrialMapColor,
                requiresBars);
            CityAreaDefinition nightlife = CreateUrbanArea(
                "nightlife",
                CityDistrictKind.Nightlife,
                "map.district.nightlife",
                NightlifeMapColor,
                requiresBars);
            CityAreaDefinition park = CreateCentralPark();
            var oldTownCells = new List<Vector2Int>();
            var residentialCells = new List<Vector2Int>();
            var industrialCells = new List<Vector2Int>();
            var nightlifeCells = new List<Vector2Int>();
            var parkCells = new List<Vector2Int>();

            for (int z = 0; z < settings.BlocksZ; z++)
            {
                for (int x = 0; x < settings.BlocksX; x++)
                {
                    var cell = new Vector2Int(x, z);
                    if (settings.IsParkCell(cell))
                    {
                        parkCells.Add(cell);
                        continue;
                    }

                    bool east = x >= settings.BlocksX / 2;
                    bool north = z >= settings.BlocksZ / 2;
                    if (north && east)
                    {
                        residentialCells.Add(cell);
                    }
                    else if (north)
                    {
                        oldTownCells.Add(cell);
                    }
                    else if (east)
                    {
                        nightlifeCells.Add(cell);
                    }
                    else
                    {
                        industrialCells.Add(cell);
                    }
                }
            }

            AddIfAny(
                builder,
                oldTown,
                oldTownCells,
                CityCellTopologyKind.BuildableLand);
            AddIfAny(
                builder,
                residential,
                residentialCells,
                CityCellTopologyKind.BuildableLand);
            AddIfAny(
                builder,
                industrial,
                industrialCells,
                CityCellTopologyKind.BuildableLand);
            AddIfAny(
                builder,
                nightlife,
                nightlifeCells,
                CityCellTopologyKind.BuildableLand);
            AddIfAny(
                builder,
                park,
                parkCells,
                CityCellTopologyKind.ParkLand);
            hasPark = parkCells.Count > 0;
            return builder;
        }

        private static void AddIfAny(
            CityBlueprintBuilder builder,
            CityAreaDefinition definition,
            ICollection<Vector2Int> cells,
            CityCellTopologyKind topology)
        {
            if (cells.Count > 0)
            {
                builder.AddCells(definition, cells, topology);
            }
        }

        private static CityAreaDefinition CreateCentralPark()
        {
            return new CityAreaDefinition(
                "central-park",
                CityDistrictKind.CentralPark,
                CityAreaCategory.NonUrbanOpen,
                CityAreaFeatureKind.CentralPark,
                CityAreaPlacementPolicy.CenterAnchor,
                "map.district.central_park",
                ParkMapColor);
        }

        private static CityAreaDefinition CreateNorthWaterfront()
        {
            return new CityAreaDefinition(
                "north-waterfront",
                CityDistrictKind.NorthWaterfront,
                CityAreaCategory.NonUrbanOpen,
                CityAreaFeatureKind.NorthWaterfront,
                CityAreaPlacementPolicy.NorthEdge,
                "map.district.north_waterfront",
                WaterfrontMapColor);
        }
    }
}

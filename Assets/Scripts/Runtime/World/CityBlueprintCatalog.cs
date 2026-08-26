using System;
using System.Collections.Generic;
using UnityEngine;

namespace BarPromenade
{
    public static class CityBlueprintCatalog
    {
        public const string DefaultBlueprintId = "default-coastal";
        public const string LegacyBlueprintId = "legacy-rectangular";

        private const int EasternOpenAreaWidth = 4;

        // The block the lake used to hold, before its boat station
        // moved to the seacoast and the water was let go: a plain
        // north-east yard now, the same four-by-four footprint so
        // nothing south of it shifts.
        private const int NorthEastYardSize = 4;
        private const int DefaultCemeteryWidth = 3;
        private const int DefaultCemeteryDepth = 2;
        private const int DefaultChurchDepth = 2;

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
        private static readonly Color CemeteryMapColor =
            new Color32(82, 91, 78, 255);
        private static readonly Color YardMapColor =
            new Color32(116, 102, 78, 255);
        private static readonly Color ChurchMapColor =
            new Color32(126, 119, 102, 255);

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
                CityDistrictKind.Residential,
                out bool hasPark);
            if (!hasPark)
            {
                throw new InvalidOperationException(
                    "The default coastal blueprint requires a centered " +
                    "central park.");
            }

            AddEasternOpenAreas(builder, settings);
            int urbanWidth = settings.BlocksX + 1;
            CityAreaDefinition waterfront = CreateNorthWaterfront();
            builder.AddRectangle(
                waterfront,
                new RectInt(
                    0,
                    settings.BlocksZ,
                    urbanWidth + EasternOpenAreaWidth,
                    1),
                CityCellTopologyKind.OpenLand);
            builder.AddRectangle(
                waterfront,
                new RectInt(
                    0,
                    settings.BlocksZ + 1,
                    urbanWidth + EasternOpenAreaWidth,
                    1),
                CityCellTopologyKind.Water);
            // The church takes the two southern rows immediately above the
            // cemetery. The residual utility yard keeps the same eastern
            // edge and fills the residual rows between church and the
            // north-east yard, so neither the city envelope nor any urban
            // lot moves.
            builder.AddRectangle(
                CreateChurch(),
                new RectInt(
                    urbanWidth,
                    DefaultCemeteryDepth,
                    EasternOpenAreaWidth,
                    DefaultChurchDepth),
                CityCellTopologyKind.OpenLand);
            builder.AddRectangle(
                CreateYard("yard-east"),
                new RectInt(
                    urbanWidth,
                    DefaultCemeteryDepth + DefaultChurchDepth,
                    EasternOpenAreaWidth,
                    settings.BlocksZ - NorthEastYardSize -
                    DefaultCemeteryDepth - DefaultChurchDepth),
                CityCellTopologyKind.OpenLand);
            // The south and west fringes: one open row/column beyond the
            // boundary streets, split in halves so each yard aligns to its
            // own access datum on the terraced perimeter. The (-1,-1)
            // corner stays void.
            int halfBlocksX = settings.BlocksX / 2;
            int halfBlocksZ = settings.BlocksZ / 2;
            builder.AddRectangle(
                CreateYard("yard-south-west"),
                new RectInt(0, -1, halfBlocksX, 1),
                CityCellTopologyKind.OpenLand);
            builder.AddRectangle(
                CreateYard("yard-south-east"),
                new RectInt(
                    halfBlocksX + 1,
                    -1,
                    settings.BlocksX - halfBlocksX,
                    1),
                CityCellTopologyKind.OpenLand);
            builder.AddRectangle(
                CreateYard("yard-west-south"),
                new RectInt(-1, 0, 1, halfBlocksZ),
                CityCellTopologyKind.OpenLand);
            builder.AddRectangle(
                CreateYard("yard-west-north"),
                new RectInt(
                    -1,
                    halfBlocksZ,
                    1,
                    settings.BlocksZ - halfBlocksZ),
                CityCellTopologyKind.OpenLand);
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
                null,
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
                archetype == CityDistrictKind.Cemetery ||
                archetype == CityDistrictKind.Yard ||
                archetype == CityDistrictKind.Church)
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

        public static CityAreaDefinition CreateYard(
            string id,
            string localizationKey = "map.district.yard")
        {
            return new CityAreaDefinition(
                id,
                CityDistrictKind.Yard,
                CityAreaCategory.NonUrbanOpen,
                CityAreaFeatureKind.Yard,
                CityAreaPlacementPolicy.Movable,
                localizationKey,
                YardMapColor);
        }

        public static CityAreaDefinition CreateChurch(
            string id = "church",
            string localizationKey = "map.district.church")
        {
            return new CityAreaDefinition(
                id,
                CityDistrictKind.Church,
                CityAreaCategory.NonUrbanOpen,
                CityAreaFeatureKind.Church,
                CityAreaPlacementPolicy.Movable,
                localizationKey,
                ChurchMapColor);
        }

        private static CityBlueprintBuilder CreateUrbanCoreBuilder(
            string blueprintId,
            CityGenerationSettings settings,
            CityDistrictKind? requiredBarDistrict,
            out bool hasPark)
        {
            bool hasRiver = string.Equals(
                blueprintId,
                DefaultBlueprintId,
                StringComparison.Ordinal);
            int riverCorridorX = settings.BlocksX / 2;
            var centerNode = new Vector2Int(
                settings.BlocksX / 2,
                settings.BlocksZ / 2);
            var builder = new CityBlueprintBuilder(
                blueprintId,
                centerNode);
            if (hasRiver)
            {
                builder.WithRiver(CreateDefaultRiver(settings));
            }
            CityAreaDefinition oldTown = CreateUrbanArea(
                "old-town",
                CityDistrictKind.OldTown,
                "map.district.old_town",
                OldTownMapColor,
                requiredBarDistrict == CityDistrictKind.OldTown);
            CityAreaDefinition residential = CreateUrbanArea(
                "residential",
                CityDistrictKind.Residential,
                "map.district.residential",
                ResidentialMapColor,
                requiredBarDistrict == CityDistrictKind.Residential);
            CityAreaDefinition industrial = CreateUrbanArea(
                "industrial",
                CityDistrictKind.Industrial,
                "map.district.industrial",
                IndustrialMapColor,
                requiredBarDistrict == CityDistrictKind.Industrial);
            CityAreaDefinition nightlife = CreateUrbanArea(
                "nightlife",
                CityDistrictKind.Nightlife,
                "map.district.nightlife",
                NightlifeMapColor,
                requiredBarDistrict == CityDistrictKind.Nightlife);
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
                    var sourceCell = new Vector2Int(x, z);
                    var cell = new Vector2Int(
                        hasRiver && x >= riverCorridorX
                            ? x + 1
                            : x,
                        z);
                    if (settings.IsParkCell(sourceCell))
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

        private static void AddEasternOpenAreas(
            CityBlueprintBuilder builder,
            CityGenerationSettings settings)
        {
            CityAreaDefinition cemetery = CreateCemetery();
            builder.AddRectangle(
                cemetery,
                new RectInt(
                    settings.BlocksX + 1,
                    0,
                    DefaultCemeteryWidth,
                    DefaultCemeteryDepth),
                CityCellTopologyKind.OpenLand);

            // The vacated lake block: a plain yard against the eastern
            // boundary street, grown over. The station that made the
            // water worth keeping stands on the seacoast now.
            builder.AddRectangle(
                CreateYard("yard-north-east"),
                new RectInt(
                    settings.BlocksX + 1,
                    settings.BlocksZ - NorthEastYardSize,
                    NorthEastYardSize,
                    NorthEastYardSize),
                CityCellTopologyKind.OpenLand);
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

        private static CityRiverDefinition CreateDefaultRiver(
            CityGenerationSettings settings)
        {
            return new CityRiverDefinition(
                "central-river",
                settings.BlocksX / 2,
                0,
                settings.BlocksZ,
                new[]
                {
                    new CityBridgeDefinition(
                        "works-bridge",
                        new RoadEdge(
                            new Vector2Int(settings.BlocksX / 2, 1),
                            new Vector2Int(settings.BlocksX / 2 + 1, 1)),
                        CityBridgeRole.Road,
                        CityBridgeStyle.Works,
                        CityGenerationSettings.DefaultRoadWidth,
                        Vector2Int.up),
                    new CityBridgeDefinition(
                        "park-footbridge",
                        new RoadEdge(
                            new Vector2Int(
                                settings.BlocksX / 2,
                                settings.BlocksZ / 2),
                            new Vector2Int(
                                settings.BlocksX / 2 + 1,
                                settings.BlocksZ / 2)),
                        CityBridgeRole.ParkFootbridge,
                        CityBridgeStyle.TimberPark,
                        CityRiverDefinition.ParkFootbridgeWidth,
                        Vector2Int.zero),
                    new CityBridgeDefinition(
                        "mouth-bridge",
                        new RoadEdge(
                            new Vector2Int(
                                settings.BlocksX / 2,
                                settings.BlocksZ - 1),
                            new Vector2Int(
                                settings.BlocksX / 2 + 1,
                                settings.BlocksZ - 1)),
                        CityBridgeRole.Road,
                        CityBridgeStyle.Mouth,
                        CityGenerationSettings.DefaultRoadWidth,
                        Vector2Int.down)
                });
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
            //  Domain reload is disabled on entering play mode, so a static
        //  field survives from one run to the next. A cached
        //  UnityEngine.Object survives as a DESTROYED one, which reads
        //  as null-ish but throws on use. This hook runs before the
        //  first scene of every run, reload or not.

        [RuntimeInitializeOnLoadMethod(
            RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetCachedResources()
        {
            defaultBlueprint = null;
        }
}
}

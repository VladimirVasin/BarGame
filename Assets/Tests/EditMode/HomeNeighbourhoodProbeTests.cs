using System.Linq;
using NUnit.Framework;
using UnityEngine;

namespace BarPromenade.Tests.EditMode
{
    public sealed class HomeNeighbourhoodProbeTests
    {
        [Test]
        public void Probe_ReportsWhatSurroundsTheHome()
        {
            CityLayout layout = CityLayoutGenerator.Generate(
                CityBlueprintCatalog.Default,
                CityGenerationSettings.Default,
                GameSessionState.DefaultCitySeed);

            BuildingLot home = layout.PlayerHome;
            Debug.Log(
                $"[home-probe] home cell={home.Cell} " +
                $"center=({home.Center.x:F1},{home.Center.z:F1}) " +
                $"size=({home.Size.x:F1},{home.Size.y:F1}) " +
                $"frontage={home.FrontageDirection}");

            foreach (Vector2Int step in new[]
                     {
                         Vector2Int.left,
                         Vector2Int.right,
                         Vector2Int.up,
                         Vector2Int.down
                     })
            {
                Vector2Int cell = home.Cell + step;
                BuildingLot lot = layout.BuildingLots
                    .FirstOrDefault(candidate => candidate.Cell == cell);
                if (lot == null)
                {
                    Debug.Log($"[home-probe] {step} cell={cell} NO LOT");
                    continue;
                }

                Debug.Log(
                    $"[home-probe] {step} cell={cell} " +
                    $"center=({lot.Center.x:F1},{lot.Center.z:F1}) " +
                    $"landUse={lot.LandUse} hasBuilding={lot.HasBuilding} " +
                    $"isBar={lot.IsBar} isSupermarket={lot.IsSupermarket} " +
                    $"district={lot.District} " +
                    $"buildingSize=({lot.Size.x:F1},{lot.Size.y:F1})");
            }

            foreach (Vector2Int step in new[]
                     {
                         Vector2Int.left,
                         Vector2Int.right,
                         Vector2Int.up,
                         Vector2Int.down
                     })
            {
                RoadEdge edge = RoadEdge.ForCellFrontage(home.Cell, step);
                bool hasRoad = layout.HasRoad(edge);
                Debug.Log(
                    $"[home-probe] frontage {step} edge={edge.A}->{edge.B} " +
                    $"hasRoad={hasRoad} " +
                    $"kind={(hasRoad ? layout.GetPathKind(edge).ToString() : "-")}");
            }

            // What is actually underfoot where the player stopped.
            var stopped = new Vector2(132.87f, -15.63f);
            foreach (CitySurfaceDescriptor surface in layout.Surfaces)
            {
                if (!surface.WorldBounds.Contains(stopped))
                {
                    continue;
                }

                Debug.Log(
                    $"[home-probe] player stop surface cell={surface.Cell} " +
                    $"kind={surface.Kind} area={surface.AreaId} " +
                    $"walkable={surface.IsWalkable} " +
                    $"bounds x={surface.WorldBounds.xMin:F1}.." +
                    $"{surface.WorldBounds.xMax:F1} " +
                    $"z={surface.WorldBounds.yMin:F1}.." +
                    $"{surface.WorldBounds.yMax:F1}");
            }

            Debug.Log(
                $"[home-probe] home building x=" +
                $"{home.Center.x - home.Size.x * 0.5f:F1}.." +
                $"{home.Center.x + home.Size.x * 0.5f:F1} " +
                $"| west neighbour building x=" +
                $"{117f - 13.5f * 0.5f:F1}..{117f + 13.5f * 0.5f:F1}");

            foreach (var poi in layout.DistrictPointsOfInterest)
            {
                float distance = Vector2.Distance(
                    new Vector2(home.Center.x, home.Center.z),
                    new Vector2(poi.Center.x, poi.Center.z));
                Debug.Log(
                    $"[home-probe] POI {poi.Kind} cell={poi.Cell} " +
                    $"center=({poi.Center.x:F1},{poi.Center.z:F1}) " +
                    $"bounds=({poi.PublicBounds.width:F1}x" +
                    $"{poi.PublicBounds.height:F1}) " +
                    $"homeDistance={distance:F1}");
            }

            CitySurfaceDescriptor[] yard = layout.Surfaces
                .Where(surface =>
                    surface.Feature == CityAreaFeatureKind.Yard)
                .ToArray();
            foreach (string areaId in yard
                         .Select(surface => surface.AreaId)
                         .Distinct())
            {
                Rect bounds = yard
                    .Where(surface => surface.AreaId == areaId)
                    .Select(surface => surface.WorldBounds)
                    .Aggregate((left, right) => Rect.MinMaxRect(
                        Mathf.Min(left.xMin, right.xMin),
                        Mathf.Min(left.yMin, right.yMin),
                        Mathf.Max(left.xMax, right.xMax),
                        Mathf.Max(left.yMax, right.yMax)));
                float distance = Vector2.Distance(
                    new Vector2(home.Center.x, home.Center.z),
                    bounds.center);
                Debug.Log(
                    $"[home-probe] yard {areaId} " +
                    $"x={bounds.xMin:F0}..{bounds.xMax:F0} " +
                    $"z={bounds.yMin:F0}..{bounds.yMax:F0} " +
                    $"homeDistance={distance:F1}");
            }

            Assert.Pass();
        }
    }
}

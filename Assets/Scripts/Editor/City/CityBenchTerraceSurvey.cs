using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace BarPromenade.Editor
{
    /// <summary>Small numeric terrain survey; no scene construction or runtime simulation.</summary>
    public static class CityBenchTerraceSurvey
    {
        [Serializable] private sealed class Survey
        {
            public List<Bench> benches=new();
            public List<Sample> samples=new();
            public List<Cell> cells=new();
            public List<Obstacle> obstacles=new();
        }
        [Serializable] private sealed class Bench
        {
            public string id; public Vector3 position,forward; public Rect access;
            public Vector3 lampFoot,lampSource,lampForward,lampTarget;
            public Vector3 previousPathBiasedTarget,previousPathBiasedSource;
            public Vector3[] approach;
        }
        [Serializable] private sealed class Sample
        {
            public float x,z,earth,street;public bool hasEarth,hasStreet; public string kind;
        }
        [Serializable] private sealed class Cell
        {
            public Vector3 a,b,c,d;public string kind;
        }
        [Serializable] private sealed class Obstacle
        {
            public string id;public Rect bounds;
        }
        public static void Export()
        {
            var layout=CityLayoutCache.GetOrGenerate(CityBlueprintCatalog.Default,
                CityGenerationSettings.Default,GameSessionState.DefaultCitySeed);
            var area=Rect.MinMaxRect(-98,-314,-76,-296);
            var data=new Survey();
            foreach(var pocket in CityHillsideBenchPocketPlan.ForLayout(layout).Pockets)
            {
                Vector3 previousTarget=pocket.ApproachPath.Count>1?
                    Vector3.Lerp(pocket.LampTarget,pocket.ApproachPath[0],.32f):pocket.LampTarget;
                Vector3 previousForward=Vector3.ProjectOnPlane(previousTarget-pocket.LampFoot,Vector3.up).normalized;
                data.benches.Add(new Bench {id=pocket.Placement.Id,position=pocket.Placement.Position,
                    forward=pocket.Placement.Forward,access=pocket.Placement.AccessBounds,
                    approach=new List<Vector3>(pocket.ApproachPath).ToArray(),lampFoot=pocket.LampFoot,
                    lampSource=pocket.LampSource,lampForward=pocket.LampForward,lampTarget=pocket.LampTarget,
                    previousPathBiasedTarget=previousTarget,
                    previousPathBiasedSource=pocket.LampFoot+Vector3.up*3.2f+previousForward*.6848f});
            }
            var ground=CityHillsideGroundPlan.ForLayout(layout);
            var streets=CityHillsideStreetPlan.ForLayout(layout).Routes;
            for(float z=area.yMin;z<=area.yMax;z+=.5f)
            for(float x=area.xMin;x<=area.xMax;x+=.5f)
            {
                var p=new Vector2(x,z);var sample=new Sample {x=x,z=z};
                sample.hasEarth=ground.TrySampleTop(p,out sample.earth);
                foreach(var street in streets)
                    if(street.TrySampleTop(p,out sample.street,out var kind))
                    {sample.hasStreet=true;sample.kind=kind.ToString();break;}
                data.samples.Add(sample);
            }
            foreach(var grid in CityHillsideGroundGrid.ForLayout(layout))
            for(int row=0;row+1<grid.Rows;row++)
            for(int col=0;col+1<grid.Columns;col++)
            {
                Vector3 a=grid.Vertex(col,row),d=grid.Vertex(col+1,row+1);
                if(!area.Overlaps(Rect.MinMaxRect(a.x,a.z,d.x,d.z))) continue;
                data.cells.Add(new Cell {a=a,b=grid.Vertex(col,row+1),c=grid.Vertex(col+1,row),d=d,
                    kind=grid.CellSurface(col,row).ToString()});
            }
            foreach(var house in CityHillsideNeighborhoodPlan.ForLayout(layout).AllBuildings)
                if(area.Overlaps(house.FoundationBounds)) data.obstacles.Add(new Obstacle {id="house",bounds=house.FoundationBounds});
            foreach(var site in CityHillsideSiteDetails.ForLayout(layout))
                if(area.Overlaps(site.Bounds)) data.obstacles.Add(new Obstacle {id=site.Part.ToString(),bounds=site.Bounds});
            foreach(var rect in CityBusNetworkFurniture.Reservations(CityDistrictBusPlanner.CreateRoadRouting(layout),layout))
                if(area.Overlaps(rect)) data.obstacles.Add(new Obstacle {id="bus",bounds=rect});
            string path=Environment.GetEnvironmentVariable("BAR_PROMENADE_BENCH_SURVEY") ?? "/private/tmp/bargame-bench-survey.json";
            File.WriteAllText(path,JsonUtility.ToJson(data,true));
            Debug.Log("BENCH TERRACE SURVEY OK: " + path);
        }
    }
}

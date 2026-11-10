using System;
using System.Collections.Generic;
using UnityEngine;

namespace BarPromenade
{
    internal enum CityGrassSpecies { Lawn, Soft, Broad, Tussock, Tall, Dry, Sedge, Reed }
    internal enum CityGrassHabitat { Park, Courtyard, Ridge, Industrial, OldStreet, River, Beach, Cemetery, Garden, Fringe }

    /// <summary>Pure seeded ecology. Grid coordinates belong to City, including the Home view.</summary>
    internal static class CityGrassPlan
    {
        internal const float Spacing = .30f;
        internal const int CellsPerChunk = 32;
        internal const float ChunkSize = Spacing * CellsPerChunk;

        internal static uint Hash(int seed, int x, int z, uint salt = 0)
        {
            unchecked
            {
                uint h = (uint)seed ^ (uint)x * 0x9e3779b9u ^ (uint)z * 0x85ebca6bu ^ salt;
                h ^= h >> 16; h *= 0x7feb352du; h ^= h >> 15; h *= 0x846ca68bu;
                return h ^ (h >> 16);
            }
        }
        internal static float Unit(uint hash) => (hash & 0xffff) / 65535f;
        internal static Vector2 Root(int seed, int x, int z)
        {
            uint h = Hash(seed, x, z);
            return new Vector2((x + .18f + Unit(h) * .64f) * Spacing,
                (z + .18f + Unit(h >> 16) * .64f) * Spacing);
        }
        internal static float Field(int seed, Vector2 p, float size, uint salt)
        {
            Vector2 cell = p / size;
            int x = Mathf.FloorToInt(cell.x), z = Mathf.FloorToInt(cell.y);
            float u = Mathf.SmoothStep(0, 1, cell.x - x), v = Mathf.SmoothStep(0, 1, cell.y - z);
            return Mathf.Lerp(Mathf.Lerp(Unit(Hash(seed,x,z,salt)), Unit(Hash(seed,x+1,z,salt)),u),
                Mathf.Lerp(Unit(Hash(seed,x,z+1,salt)), Unit(Hash(seed,x+1,z+1,salt)),u),v);
        }
        internal static bool Plant(int seed, int x, int z, CityGrassHabitat habitat,
            out CityGrassSpecies species, out float scale, out int variant)
        {
            Vector2 p = Root(seed, x, z);
            uint h = Hash(seed, x, z, 0x12345678);
            float pick = Unit(h), patch = Field(seed, p, 5f, 3741);
            float density = habitat switch
            {
                CityGrassHabitat.Beach => patch > .48f ? .75f : patch > .35f ? .20f : 0,
                CityGrassHabitat.Cemetery => .30f,
                CityGrassHabitat.Fringe => .09f + .15f * patch,
                CityGrassHabitat.OldStreet => .64f,
                _ => .88f + .10f * patch
            };
            scale = .78f + Unit(Hash(seed,x,z,934)) * .44f;
            variant = (int)((h >> 16) % 3);
            species = habitat switch
            {
                CityGrassHabitat.Park => pick < .64f ? CityGrassSpecies.Lawn : pick < .93f ? CityGrassSpecies.Soft : CityGrassSpecies.Tall,
                CityGrassHabitat.Courtyard => pick < .42f ? CityGrassSpecies.Lawn : pick < .73f ? CityGrassSpecies.Broad : pick < .94f ? CityGrassSpecies.Soft : CityGrassSpecies.Tall,
                CityGrassHabitat.Ridge => pick < .48f ? CityGrassSpecies.Tussock : pick < .76f ? CityGrassSpecies.Dry : CityGrassSpecies.Soft,
                CityGrassHabitat.Industrial => pick < .48f ? CityGrassSpecies.Tall : pick < .77f ? CityGrassSpecies.Dry : CityGrassSpecies.Broad,
                CityGrassHabitat.OldStreet => pick < .75f ? CityGrassSpecies.Lawn : CityGrassSpecies.Broad,
                CityGrassHabitat.River => pick < .65f ? CityGrassSpecies.Sedge : CityGrassSpecies.Soft,
                CityGrassHabitat.Beach => patch > .56f && pick > .25f ? CityGrassSpecies.Reed : CityGrassSpecies.Tussock,
                CityGrassHabitat.Cemetery => pick < .75f ? CityGrassSpecies.Lawn : CityGrassSpecies.Soft,
                CityGrassHabitat.Garden => CityGrassSpecies.Lawn,
                _ => pick < .65f ? CityGrassSpecies.Dry : CityGrassSpecies.Tussock
            };
            if (habitat == CityGrassHabitat.Garden) scale *= .55f;
            if (habitat == CityGrassHabitat.Cemetery) scale *= .65f;
            // The composition varies in broad patches as well as individual leaves.
            scale *= .78f + .40f * patch;
            return Unit(Hash(seed,x,z,5623)) < density;
        }
    }

    /// <summary>Compact immutable index of final ground, not an approximation of source slabs.</summary>
    internal sealed class CityGrassSurface
    {
        private const float BucketSize = 1f;
        private readonly List<Face> faces = new List<Face>();
        private readonly Dictionary<Vector2Int,List<int>> buckets = new Dictionary<Vector2Int,List<int>>();
        private readonly Dictionary<Vector2Int,List<Obstacle>> obstacles = new Dictionary<Vector2Int,List<Obstacle>>();
        private readonly List<int>[] habitatFaces = new List<int>[10];
        private readonly CityLayout layout;
        private readonly CitySeacoastPlan coast;
        private readonly Rect garden;
        private readonly List<Rect> ridge = new List<Rect>();
        private CityCemeteryGroundExcavation excavation;
        private Rect cemetery;
        internal readonly HomeExteriorContextPlan Home;
        internal int FaceCount => faces.Count;
        internal Bounds LastBlocker {get;private set;}
        internal string DescribeHabitat(CityGrassHabitat habitat)
        {
            var list=habitatFaces[(int)habitat];
            if(list==null)return habitat+": no final-ground faces";
            Face f=faces[list[0]];Vector3 p=(f.A+f.B+f.C)/3;
            Sample(new Vector2(p.x,p.z),out var actual,out float top);
            return $"{habitat}: faces={list.Count}; centre={p}; kind={f.Kind}; top={top}/{actual.Kind}; blocker={LastBlocker}";
        }
        internal Rect Bounds { get; private set; }
        internal CityGrassSurface(CityLayout city, HomeExteriorContextPlan home = null, CitySeacoastPlan shore = null)
        {
            layout = city; Home = home; coast = shore;
            garden=CityWorldPlans.GetOrCreate(city).Church?.Grounds??default;
            foreach(var road in CityHillsideStreetPlan.ForLayout(city).Routes)
                ridge.Add(CityHillsideNeighborhoodPlan.TerrainBounds(road));
            ridge.Add(CityHillsideQuarterPlan.ForLayout(city).TerrainBounds);
        }
        internal void SetExcavation(CityCemeteryGroundExcavation owner,Rect bounds)
        {excavation=owner;cemetery=bounds;}

        internal Vector3 ToCity(Vector3 p) => Home == null ? p : PlayerHomeBalconyGeometry.ToCityWorld(Home.PlayerHome,p);
        internal Vector3 FromCity(Vector3 p) => Home == null ? p : PlayerHomeBalconyGeometry.ToHomeLocal(Home.PlayerHome,p);
        internal Quaternion FromCityRotation => Home == null ? Quaternion.identity :
            Quaternion.LookRotation(FromCity(Vector3.forward)-FromCity(Vector3.zero),Vector3.up);

        internal bool TryFindPlant(CityGrassHabitat habitat, out Vector3 root, out Vector3 tip)
        {
            var candidates=habitatFaces[(int)habitat];
            if(candidates==null){root=tip=default;return false;}
            for(int pass=0;pass<(habitat==CityGrassHabitat.Park?2:1);pass++)
            foreach (int index in candidates)
            {
                Face face=faces[index];
                if(habitat==CityGrassHabitat.Park && (face.Kind==FootstepGroundKind.Grass)!=(pass==0))continue;
                if (!Eligible(face.Kind)) continue;
                Vector3 centre=(face.A+face.B+face.C)/3;
                int gx=Mathf.FloorToInt(centre.x/CityGrassPlan.Spacing),gz=Mathf.FloorToInt(centre.z/CityGrassPlan.Spacing);
                for(int z=-1;z<=1;z++)for(int x=-1;x<=1;x++)
                {
                    Vector2 p=CityGrassPlan.Root(layout.Seed,gx+x,gz+z);
                    if(!TrySample(p,out Vector3 r,out _,out var kind)||kind!=habitat||
                        !CityGrassPlan.Plant(layout.Seed,gx+x,gz+z,kind,out var species,out float scale,out _))continue;
                    if(habitat==CityGrassHabitat.Beach&&species!=CityGrassSpecies.Reed)continue;
                    root=FromCity(r);
                    float h=species switch {CityGrassSpecies.Lawn=>.16f,CityGrassSpecies.Soft=>.32f,CityGrassSpecies.Broad=>.34f,
                        CityGrassSpecies.Tussock=>.40f,CityGrassSpecies.Tall=>.72f,CityGrassSpecies.Dry=>.47f,CityGrassSpecies.Sedge=>.57f,_=>1.30f};
                    tip=root+Vector3.up*h*scale;return true;
                }
            }
            root=tip=default;return false;
        }

        internal void Add(Vector3 a, Vector3 b, Vector3 c, FootstepGroundKind kind)
        {
            Vector3 normal = Vector3.Cross(b-a,c-a).normalized;
            if (Mathf.Abs(normal.y) < .65f) return;
            if (normal.y < 0) normal = -normal;
            var face = new Face(a,b,c,normal,kind);
            int id = faces.Count; faces.Add(face);
            if(Eligible(kind))
            {
                int habitat=(int)Habitat((a+b+c)/3,kind);
                if(habitatFaces[habitat]==null)habitatFaces[habitat]=new List<int>();
                habitatFaces[habitat].Add(id);
            }
            Rect rect = face.Rect;
            Bounds = id == 0 ? rect : Rect.MinMaxRect(Mathf.Min(Bounds.xMin,rect.xMin),Mathf.Min(Bounds.yMin,rect.yMin),
                Mathf.Max(Bounds.xMax,rect.xMax),Mathf.Max(Bounds.yMax,rect.yMax));
            for(int z=Bucket(rect.yMin);z<=Bucket(rect.yMax);z++)
            for(int x=Bucket(rect.xMin);x<=Bucket(rect.xMax);x++)
            {
                var key = new Vector2Int(x,z);
                if(!buckets.TryGetValue(key,out var list)) buckets.Add(key,list=new List<int>());
                list.Add(id);
            }
        }
        internal void AddObstacle(Bounds bounds,Collider preciseShape=null)
        {
            bounds.Expand(new Vector3(.28f,0,.28f));
            for(int z=Bucket(bounds.min.z);z<=Bucket(bounds.max.z);z++)
            for(int x=Bucket(bounds.min.x);x<=Bucket(bounds.max.x);x++)
            {
                var key = new Vector2Int(x,z);
                if(!obstacles.TryGetValue(key,out var list)) obstacles.Add(key,list=new List<Obstacle>());
                list.Add(new Obstacle(bounds,preciseShape));
            }
        }
        internal bool TrySample(Vector2 p,out Vector3 root,out Vector3 normal,out CityGrassHabitat habitat)
        {
            root=default; normal=Vector3.up; habitat=CityGrassHabitat.Fringe;
            if(excavation!=null)foreach(Rect mouth in excavation.Cuts)
            {
                Rect cut=mouth;cut.xMin-=.25f;cut.xMax+=.25f;cut.yMin-=.25f;cut.yMax+=.25f;
                if(cut.Contains(p))return false;
            }
            // Raised decks can cover soil below without belonging to the ground
            // compositor. Their complete physical spans remain plant-free.
            foreach(var bridge in layout.River.Bridges)
            {
                Rect span=bridge.SpanBounds;
                span.xMin-=.25f;span.xMax+=.25f;span.yMin-=.25f;span.yMax+=.25f;
                if(span.Contains(p))return false;
            }
            if(!Sample(p,out Face face,out float height) || !Eligible(face.Kind)) return false;
            root=new Vector3(p.x,height-.008f,p.y); normal=face.Normal;
            // Keep leaves clear of actual paving, including narrow paths and grade breaks.
            foreach (Vector2 offset in EdgeOffsets)
                if(!Sample(p+offset,out Face edge,out float edgeY) || !Eligible(edge.Kind) || Mathf.Abs(edgeY-height)>.22f) return false;
            if(obstacles.TryGetValue(new Vector2Int(Bucket(p.x),Bucket(p.y)),out var blocks))
                foreach(var obstacle in blocks)
                {
                    Bounds block=obstacle.Bounds;
                    if(p.x<block.min.x || p.x>block.max.x || p.y<block.min.z || p.y>block.max.z ||
                        height<block.min.y-.25f || height>=block.max.y-.08f)continue;
                    // A combined collision mesh can span the entire city with
                    // empty space between its pieces. Its AABB is only a broad
                    // phase; the exact vertical shape decides occupancy.
                    if(obstacle.Shape!=null)
                    {
                        Vector3 origin=FromCity(new Vector3(p.x,block.max.y+.1f,p.y));
                        if(!obstacle.Shape.Raycast(new Ray(origin,Vector3.down),out var hit,block.max.y-height+.2f)||
                            ToCity(hit.point).y<=height+.08f)continue;
                    }
                    LastBlocker=block;return false;
                }
            if(face.Kind==FootstepGroundKind.Sand)
            {
                if(coast==null || !coast.Frame.EastZone.Contains(p) ||
                    p.y < coast.Frame.BeachRowBounds.yMin+2f || p.y > coast.Frame.BeachRowBounds.yMax-10f) return false;
                habitat=CityGrassHabitat.Beach; return true;
            }
            habitat=Habitat(root,face.Kind);
            return true;
        }
        private CityGrassHabitat Habitat(Vector3 root,FootstepGroundKind kind)
        {
            if(kind==FootstepGroundKind.Sand)return CityGrassHabitat.Beach;
            Vector2 p=new Vector2(root.x,root.z);
            if(garden.Contains(p))return CityGrassHabitat.Garden;
            if(cemetery.Contains(p))return CityGrassHabitat.Cemetery;
            foreach(Rect bounds in ridge)if(bounds.Contains(p))return CityGrassHabitat.Ridge;
            CityGrassHabitat habitat=CityGrassHabitat.Fringe;
            Vector2 grid=layout.GetWorldGridCoordinate(root);
            var cell=new Vector2Int(Mathf.FloorToInt(grid.x),Mathf.FloorToInt(grid.y));
            if(layout.TryGetArea(cell,out var area))
                habitat=area.Archetype switch
                {
                    CityDistrictKind.CentralPark=>CityGrassHabitat.Park,
                    CityDistrictKind.Cemetery=>CityGrassHabitat.Cemetery,
                    CityDistrictKind.Church=>CityGrassHabitat.Garden,
                    CityDistrictKind.Industrial=>CityGrassHabitat.Industrial,
                    CityDistrictKind.OldTown=>CityGrassHabitat.OldStreet,
                    CityDistrictKind.Residential or CityDistrictKind.Nightlife=>CityGrassHabitat.Courtyard,
                    _=>CityGrassHabitat.Fringe
                };
            if(habitat==CityGrassHabitat.Courtyard && root.y>layout.WorldOrigin.y+4f) habitat=CityGrassHabitat.Ridge;
            if(habitat is CityGrassHabitat.Courtyard or CityGrassHabitat.Industrial or CityGrassHabitat.Fringe)
            {
                // River water uses the authoritative plan; only unpaved banks can reach here.
                foreach(Vector2 offset in RiverOffsets)
                    if(layout.IsWater(root+new Vector3(offset.x,0,offset.y))) { habitat=CityGrassHabitat.River; break; }
            }
            return habitat;
        }
        private static readonly Vector2[] EdgeOffsets={new Vector2(.18f,0),new Vector2(-.18f,0),new Vector2(0,.18f),new Vector2(0,-.18f)};
        private static readonly Vector2[] RiverOffsets={new Vector2(2,0),new Vector2(-2,0),new Vector2(0,2),new Vector2(0,-2)};
        private static bool Eligible(FootstepGroundKind k)=> k is FootstepGroundKind.Grass or FootstepGroundKind.Soil or FootstepGroundKind.Sand;
        private static int Bucket(float value)=>Mathf.FloorToInt(value/BucketSize);
        private bool Sample(Vector2 p,out Face face,out float height)
        {
            face=default; height=float.NegativeInfinity;
            if(!buckets.TryGetValue(new Vector2Int(Bucket(p.x),Bucket(p.y)),out var list))return false;
            foreach(int index in list)
            {
                Face f=faces[index];
                if(!f.Rect.Contains(p))continue;
                Vector2 a=new Vector2(f.A.x,f.A.z),b=new Vector2(f.B.x,f.B.z),c=new Vector2(f.C.x,f.C.z);
                float det=(b.y-c.y)*(a.x-c.x)+(c.x-b.x)*(a.y-c.y);
                if(Mathf.Abs(det)<1e-8f)continue;
                float u=((b.y-c.y)*(p.x-c.x)+(c.x-b.x)*(p.y-c.y))/det;
                float v=((c.y-a.y)*(p.x-c.x)+(a.x-c.x)*(p.y-c.y))/det;
                if(u<-.0001f||v<-.0001f||u+v>1.0001f)continue;
                float y=u*f.A.y+v*f.B.y+(1-u-v)*f.C.y;
                if(y<=height)continue;
                height=y;face=f;
            }
            return !float.IsNegativeInfinity(height);
        }
        private readonly struct Face
        {
            internal readonly Vector3 A,B,C,Normal; internal readonly FootstepGroundKind Kind; internal readonly Rect Rect;
            internal Face(Vector3 a,Vector3 b,Vector3 c,Vector3 n,FootstepGroundKind k)
            { A=a;B=b;C=c;Normal=n;Kind=k;Rect=UnityEngine.Rect.MinMaxRect(Mathf.Min(a.x,Mathf.Min(b.x,c.x)),
                Mathf.Min(a.z,Mathf.Min(b.z,c.z)),Mathf.Max(a.x,Mathf.Max(b.x,c.x)),Mathf.Max(a.z,Mathf.Max(b.z,c.z))); }
        }
        private readonly struct Obstacle
        {
            internal readonly Bounds Bounds;internal readonly Collider Shape;
            internal Obstacle(Bounds bounds,Collider shape){Bounds=bounds;Shape=shape;}
        }
    }
}

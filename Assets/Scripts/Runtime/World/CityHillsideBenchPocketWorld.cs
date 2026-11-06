using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace BarPromenade
{
    /// <summary>Imported municipal fixtures and street patch topology, resting on unchanged ground triangles.</summary>
    internal static class CityHillsideBenchPocketWorld
    {
        private static Vector3[] patchVertices;
        private static int[] patchIndices;
        private static readonly Color PavingColor = new(.37f,.385f,.35f);

        internal static void Build(Transform parent, CityLayout layout, Rect? retained)
        {
            var plan = CityHillsideBenchPocketPlan.ForLayout(layout);
            if (plan.Pockets.Count == 0) return;
            EnsurePatch();
            var root = new GameObject("Ridge bench pockets").transform;
            root.SetParent(parent, false);
            var polygons = new List<Vector2[]>();
            var lampParts = new List<RuntimeMeshPlacement>();
            var lenses = new List<Vector3[]>();
            var provider = CityMiscAssetProvider.LoadOrThrow();
            var lamp = provider.GetPartOrThrow(CityMiscKind.StreetLampShell,0,0);
            foreach (var pocket in plan.Pockets)
            {
                if (retained.HasValue && !retained.Value.Overlaps(pocket.Placement.AccessBounds)) continue;
                polygons.AddRange(pocket.SurfacePolygons);
                Quaternion rotation = Quaternion.LookRotation(pocket.LampForward);
                lampParts.Add(new RuntimeMeshPlacement(lamp.Mesh,pocket.LampFoot,rotation,Vector3.one*.64f));
                var fixture = new GameObject("Bench lamp " + pocket.Placement.Id).transform;
                fixture.SetParent(root,false);
                var anchor = new GameObject(CityHillsideBenchPocketPlan.LightAnchorName).transform;
                anchor.SetParent(fixture,false);
                anchor.localPosition = pocket.LampSource;
                anchor.localRotation = Quaternion.LookRotation(pocket.LampTarget-pocket.LampSource,Vector3.up);
                Vector3 r=rotation*Vector3.right*.1216f, f=rotation*Vector3.forward*.055f;
                Vector3 source=pocket.LampSource-Vector3.up*.005f;
                // The frosted underside is the same imported flat topology as the pavement.
                lenses.Add(new[] {source-r-f,source+r-f,source+r+f});
                lenses.Add(new[] {source-r-f,source+r+f,source-r+f});
                if (!retained.HasValue)
                {
                    var solid=fixture.gameObject.AddComponent<BoxCollider>();
                    solid.center=pocket.LampFoot+Vector3.up*1.55f;
                    solid.size=new Vector3(.24f,3.1f,.24f);
                }
            }
            if(lampParts.Count==0) return;
            var fixtures=RuntimePrimitiveFactory.CreateCombinedMeshes("Imported bench lamps",root,lampParts,
                new Color(.24f,.255f,.235f));
            CityParkSurfaceAppearance.ApplyCombined(fixtures.GetComponent<Renderer>(),CityParkSurfaceKind.PaintedMetal,
                new Color(.24f,.255f,.235f));
            BuildMesh("Bench lamp lenses",root,lenses,true);
            BuildPaving(root,layout,polygons,"Maintained bench paving",!retained.HasValue);
            CityPawnshopBenchTerraceWorld.Build(root,layout,retained);
        }

        internal static void BuildPaving(Transform root,CityLayout layout,IReadOnlyList<Vector2[]> footprints,
            string name,bool footsteps=true)
        {
            EnsurePatch();
            var faces=new List<Vector3[]>();
            var streets=CityHillsideStreetPlan.ForLayout(layout).Routes;
            var terrace=CityPawnshopBenchTerracePlan.ForLayout(layout);
            foreach(var grid in CityHillsideGroundGrid.ForLayout(layout))
            for(int row=0;row+1<grid.Rows;row++)
            for(int col=0;col+1<grid.Columns;col++)
            {
                if(grid.CellSurface(col,row)==CityHillsideGroundGrid.Surface.Cut) continue;
                Append(grid.Vertex(col,row),grid.Vertex(col,row+1),grid.Vertex(col+1,row));
                Append(grid.Vertex(col+1,row),grid.Vertex(col,row+1),grid.Vertex(col+1,row+1));
            }
            var quarter=CityHillsideQuarterGroundGrid.ForLayout(layout);
            foreach(var grid in new[] {quarter})
            for(int row=0;row+1<grid.Rows;row++)
            for(int col=0;col+1<grid.Columns;col++)
            {
                if(grid.CellSurface(col,row)!=CityHillsideQuarterGroundGrid.Surface.Earth) continue;
                Append(grid.Vertex(col,row),grid.Vertex(col,row+1),grid.Vertex(col+1,row));
                Append(grid.Vertex(col+1,row),grid.Vertex(col,row+1),grid.Vertex(col+1,row+1));
            }
            var paving=BuildMesh(name,root,faces,false);
            // Existing hillside earth retains its sole physical collider. This
            // maintained skin is render-only; it must also work in the passive
            // Home frame, before that frame has a compositor parent.
            if(paving!=null && footsteps)
                paving.AddComponent<CityHillsideBenchPavingGround>().Configure(layout,footprints);

            void Append(Vector3 a,Vector3 b,Vector3 c)
            {
                Vector2[] triangle=CityRoadPolygon.CounterClockwise(new[] {XZ(a),XZ(b),XZ(c)});
                Rect bounds=CityRoadPolygon.Bounds(triangle);
                foreach(var footprint in footprints)
                {
                    if(!bounds.Overlaps(CityRoadPolygon.Bounds(footprint))) continue;
                    IReadOnlyList<Vector2> piece=triangle;
                    for(int edge=0;edge<footprint.Length && piece.Count>=3;edge++)
                        piece=CityRoadPolygon.Clip(piece,footprint[edge],footprint[(edge+1)%footprint.Length]);
                    if(piece.Count<3 || CityRoadPolygon.Area(piece)<.00001f) continue;
                    var remaining=new List<Vector2[]> {new List<Vector2>(piece).ToArray()};
                    if(terrace.IsEnabled)
                        foreach(var cut in terrace.Footprints)
                        {
                            if(!bounds.Overlaps(CityRoadPolygon.Bounds(cut))) continue;
                            var next=new List<Vector2[]>();
                            foreach(var part in remaining) next.AddRange(CityRoadPolygon.Subtract(part,cut));
                            remaining=next;
                        }
                    // Keep the public footway's existing material and its exact seam.
                    foreach(var road in streets)
                    foreach(var cut in road.WalkablePolygons)
                    {
                        if(!bounds.Overlaps(CityRoadPolygon.Bounds(cut))) continue;
                        var next=new List<Vector2[]>();
                        foreach(var part in remaining) next.AddRange(CityRoadPolygon.Subtract(part,cut));
                        remaining=next;
                    }
                    foreach(var link in CityHillsideQuarterPlan.ForLayout(layout).Links)
                    {
                        if(!bounds.Overlaps(link.Bounds)) continue;
                        var next=new List<Vector2[]>();
                        foreach(var part in remaining) next.AddRange(CityRoadPolygon.Subtract(part,CityRoadPolygon.Rectangle(link.Bounds)));
                        remaining=next;
                    }
                    Vector3 normal=Vector3.Cross(b-a,c-a);
                    foreach(var part in remaining)
                    for(int fan=1;fan+1<part.Length;fan++)
                        faces.Add(new[] {At(part[0]),At(part[fan]),At(part[fan+1])});
                    Vector3 At(Vector2 point)=>new(point.x,a.y-(normal.x*(point.x-a.x)+normal.z*(point.y-a.z))/normal.y,point.y);
                }
            }
        }

        internal static GameObject BuildMesh(string name,Transform parent,IReadOnlyList<Vector3[]> faces,bool lens,
            bool raisedSkin=true,bool asphalt=false)
        {
            if(faces.Count==0) return null;
            EnsurePatch();
            var vertices=new List<Vector3>(faces.Count*patchVertices.Length);
            var normals=new List<Vector3>(vertices.Capacity);
            var uv=new List<Vector2>(vertices.Capacity);
            var indices=new List<int>(faces.Count*patchIndices.Length);
            float tile=asphalt?CityExteriorAppearance.RoadTextureTileSize:
                CityParkSurfaceAppearance.GetRecipe(CityParkSurfaceKind.Plaza).MetersPerTile;
            foreach(var face in faces)
            {
                Vector3 a=face[0],b=face[1],c=face[2];
                Vector3 normal=Vector3.Cross(c-a,b-a).normalized;
                if((lens && normal.y>0f)||(!lens && normal.y<0f)) {Vector3 swap=b;b=c;c=swap;normal=-normal;}
                int offset=vertices.Count;
                foreach(var coefficient in patchVertices)
                {
                    Vector3 p=a+(b-a)*coefficient.x+(c-a)*coefficient.z;
                    vertices.Add(lens||!raisedSkin?p:p+Vector3.up*.003f);normals.Add(normal);
                    Vector2 texture=Mathf.Abs(normal.y)>.5f?XZ(p):Mathf.Abs(normal.x)>.5f?new Vector2(p.z,p.y):new Vector2(p.x,p.y);
                    uv.Add(texture/tile);
                }
                foreach(int index in patchIndices) indices.Add(offset+index);
            }
            var mesh=new Mesh {name=name+" Mesh",indexFormat=vertices.Count>ushort.MaxValue?IndexFormat.UInt32:IndexFormat.UInt16};
            mesh.SetVertices(vertices);mesh.SetNormals(normals);mesh.SetUVs(0,uv);mesh.SetTriangles(indices,0);mesh.RecalculateBounds();
            var host=new GameObject(name);host.transform.SetParent(parent,false);
            host.AddComponent<MeshFilter>().sharedMesh=mesh;
            host.AddComponent<RuntimeGeneratedMeshOwner>().Initialize(mesh);
            var renderer=host.AddComponent<MeshRenderer>();
            renderer.sharedMaterial=lens?CityNightResources.EmissiveMaterial:RuntimePrimitiveFactory.DefaultMaterial;
            renderer.shadowCastingMode=lens?ShadowCastingMode.Off:ShadowCastingMode.On;
            renderer.receiveShadows=!lens;
            if(lens) RuntimePrimitiveFactory.SetColor(renderer,new Color(2.25f,2.4f,2.1f));
            else if(asphalt) CityExteriorAppearance.ApplyRoadSurface(renderer);
            else CityParkSurfaceAppearance.ApplyCombined(renderer,CityParkSurfaceKind.Plaza,PavingColor);
            return host;
        }

        private static void EnsurePatch()
        {
            if(patchVertices!=null) return;
            var model=Resources.Load<GameObject>("City/StreetProfile/CityStreetProfile3D");
            if(model==null) throw new InvalidOperationException("Missing imported City street patch.");
            foreach(var filter in model.GetComponentsInChildren<MeshFilter>(true))
            {
                if(filter.name!="SurfacePatch") continue;
                patchVertices=filter.sharedMesh.vertices;patchIndices=filter.sharedMesh.triangles;
                for(int i=0;i<patchVertices.Length;i++) patchVertices[i]=filter.transform.TransformPoint(patchVertices[i]);
                return;
            }
            throw new InvalidOperationException("The imported City street model needs SurfacePatch.");
        }
        private static Vector2 XZ(Vector3 point)=>new(point.x,point.z);
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void Reset() {patchVertices=null;patchIndices=null;}
    }

    internal sealed class CityHillsideBenchPavingGround : FootstepGroundOverlay
    {
        private CityHillsideGroundPlan ground;
        private CityPawnshopBenchTerracePlan terrace;
        private IReadOnlyList<Vector2[]> polygons;
        private Rect[] bounds;

        internal void Configure(CityLayout layout,IReadOnlyList<Vector2[]> footprints)
        {
            ground=CityHillsideGroundPlan.ForLayout(layout);terrace=CityPawnshopBenchTerracePlan.ForLayout(layout);polygons=footprints;
            bounds=new Rect[polygons.Count];
            for(int i=0;i<bounds.Length;i++) bounds[i]=CityRoadPolygon.Bounds(polygons[i]);
            Initialize(FootstepGroundKind.Stone,Array.Empty<RuntimeOrientedBox>());
        }
        private void OnEnable()
        {
            if(ground!=null) Initialize(FootstepGroundKind.Stone,Array.Empty<RuntimeOrientedBox>());
        }
        public override bool IsActiveAt(Vector3 feet)
        {
            var point=new Vector2(feet.x,feet.z);
            if(polygons==null) return false;
            // The extended upper terrace is asphalt with its own physical
            // concrete/asphalt cue, rather than the old stone-paving overlay.
            if(terrace.IsEnabled && terrace.UpperLanding.Bounds.Contains(point)) return false;
            for(int i=0;i<polygons.Count;i++)
                if(bounds[i].Contains(point) && CityRoadPolygon.Contains(polygons[i],point))
                    return (terrace.TrySampleTop(point,out float top)||ground.TrySampleTop(point,out top)) &&
                        Mathf.Abs(feet.y-top)<=MaximumContactOffset;
            return false;
        }
    }
}

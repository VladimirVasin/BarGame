using System;
using System.Collections.Generic;
using UnityEngine;

namespace BarPromenade
{
    /// <summary>Shared City/Home mesh for existing procedural pedestrian ground.</summary>
    internal static class CityEntranceForecourtWorldBuilder
    {
        internal static GameObject Build(string name,Transform parent,CityEntranceForecourtPlan plan,
            HomeExteriorContextPlan home=null)
        {
            var mesh=new SurfaceMesh(home);
            for(int row=0;row<plan.Rows-1;row++)
            for(int column=0;column<plan.Columns-1;column++)
            {
                int a=row*plan.Columns+column,b=a+1,c=a+plan.Columns+1,d=a+plan.Columns;
                mesh.Face(new[]{plan.Top[a],plan.Top[b],plan.Top[c],plan.Top[d]},Vector3.up);
                mesh.Face(new[]{plan.Bottom[a],plan.Bottom[b],plan.Bottom[c],plan.Bottom[d]},Vector3.down);
            }
            var boundary=new List<int>(plan.Rows*2+plan.Columns*2);
            for(int c=0;c<plan.Columns;c++)boundary.Add(c);
            for(int r=1;r<plan.Rows;r++)boundary.Add(r*plan.Columns+plan.Columns-1);
            for(int c=plan.Columns-2;c>=0;c--)boundary.Add((plan.Rows-1)*plan.Columns+c);
            for(int r=plan.Rows-2;r>0;r--)boundary.Add(r*plan.Columns);
            for(int i=0;i<boundary.Count;i++)
            {
                int a=boundary[i],b=boundary[(i+1)%boundary.Count];
                Vector3 outside=(plan.Top[a]+plan.Top[b])*.5f-plan.Bounds.center;outside.y=0f;
                mesh.Face(new[]{plan.Top[a],plan.Top[b],plan.Bottom[b],plan.Bottom[a]},outside);
            }
            return mesh.Finish(name,parent,home==null,31);
        }

        internal static GameObject BuildHomeSidewalks(Transform parent,HomeExteriorContextPlan home,
            CityStreetSurfacePlan plan,IReadOnlyList<Rect> nearbyRoads)
            =>BuildHomeSurfaces(parent,home,plan,nearbyRoads,false);

        internal static GameObject BuildHomeRoads(Transform parent,HomeExteriorContextPlan home,
            CityStreetSurfacePlan plan,IReadOnlyList<Rect> nearbyRoads)
            =>BuildHomeSurfaces(parent,home,plan,nearbyRoads,true);

        private static GameObject BuildHomeSurfaces(Transform parent,HomeExteriorContextPlan home,
            CityStreetSurfacePlan plan,IReadOnlyList<Rect> nearbyRoads,bool road)
        {
            var mesh=new SurfaceMesh(home,road?plan.StreetGeometry:null);
            foreach(RuntimeOrientedBox box in road?plan.StreetGeometry:plan.SidewalkGeometry)
            {
                var corners=new Vector3[8];Bounds bounds=default;
                for(int i=0;i<8;i++)
                {
                    corners[i]=box.Center+box.Rotation*Vector3.Scale(box.Size*.5f,new Vector3(
                        (i&1)==0?-1f:1f,(i&2)==0?-1f:1f,(i&4)==0?-1f:1f));
                    if(i==0)bounds=new Bounds(corners[i],Vector3.zero);else bounds.Encapsulate(corners[i]);
                }
                if(!Touches(bounds,nearbyRoads))continue;
                mesh.Face(new[]{corners[2],corners[3],corners[7],corners[6]},box.Rotation*Vector3.up);
                mesh.Face(new[]{corners[0],corners[1],corners[5],corners[4]},box.Rotation*Vector3.down);
                mesh.Face(new[]{corners[0],corners[2],corners[6],corners[4]},box.Rotation*Vector3.left);
                mesh.Face(new[]{corners[1],corners[3],corners[7],corners[5]},box.Rotation*Vector3.right);
                mesh.Face(new[]{corners[0],corners[1],corners[3],corners[2]},box.Rotation*Vector3.back);
                mesh.Face(new[]{corners[4],corners[5],corners[7],corners[6]},box.Rotation*Vector3.forward);
            }
            foreach(CityStreetRibbonDescriptor ribbon in road?plan.CurvedStreetRibbons:plan.CurvedSidewalkRibbons)
            foreach(Vector2[] polygon in ribbon.Polygons)
            {
                Rect bounds=CityRoadPolygon.Bounds(polygon);
                if(!Touches(new Bounds(new Vector3(bounds.center.x,0f,bounds.center.y),
                    new Vector3(bounds.width,1f,bounds.height)),nearbyRoads))continue;
                CityRoadPath path=home.Layout.RoadGeometry.Get(ribbon.Edge);
                var top=new Vector3[polygon.Length];var bottom=new Vector3[polygon.Length];
                for(int i=0;i<polygon.Length;i++)
                {
                    float datum=ribbon.FlatNode.HasValue?home.Layout.ElevationPlan.GetNodeElevation(ribbon.FlatNode.Value):
                        home.Layout.ElevationPlan.SampleRoadDatum(ribbon.Edge,path.Project(polygon[i]).DistanceAlong/path.Length);
                    top[i]=new Vector3(polygon[i].x,datum+ribbon.TopOffset,polygon[i].y);
                    bottom[i]=top[i]-Vector3.up*ribbon.Thickness;
                }
                mesh.Face(top,Vector3.up);mesh.Face(bottom,Vector3.down);
                for(int i=0;i<polygon.Length;i++)
                {
                    int next=(i+1)%polygon.Length;
                    Vector3 outside=(top[i]+top[next])*.5f-new Vector3(bounds.center.x,0f,bounds.center.y);outside.y=0f;
                    mesh.Face(new[]{top[i],top[next],bottom[next],bottom[i]},outside);
                }
            }
            return mesh.Finish(road?"Home Exterior Street Surfaces":"Home Exterior Sidewalk Surfaces",parent,false,road?20:30);
        }

        private static bool Touches(Bounds bounds,IReadOnlyList<Rect> roads)
        {
            foreach(Rect road in roads)
                if(bounds.max.x>=road.xMin && bounds.min.x<=road.xMax && bounds.max.z>=road.yMin && bounds.min.z<=road.yMax)return true;
            return false;
        }

        private sealed class SurfaceMesh
        {
            private readonly HomeExteriorContextPlan home;
            private readonly IReadOnlyList<RuntimeOrientedBox> roadGeometry;
            private readonly List<Vector3> vertices=new List<Vector3>();
            private readonly List<int> triangles=new List<int>();
            private readonly List<Vector2> uv=new List<Vector2>();
            internal SurfaceMesh(HomeExteriorContextPlan context,IReadOnlyList<RuntimeOrientedBox> roads=null)
            {home=context;roadGeometry=roads;}
            internal void Face(Vector3[] source,Vector3 outward)
            {
                if(Vector3.Dot(Vector3.Cross(source[1]-source[0],source[2]-source[0]),outward)<0f)
                    Array.Reverse(source,1,source.Length-1);
                var face=new List<(Vector3 point,Vector2 uv)>(source.Length+1);
                Vector3 normal=Vector3.Cross(source[1]-source[0],source[2]-source[0]).normalized;
                foreach(Vector3 city in source)
                {
                    Vector2 tex=Mathf.Abs(normal.y)>.5f?new Vector2(city.x,city.z):
                        Mathf.Abs(normal.x)>.5f?new Vector2(city.z,city.y):new Vector2(city.x,city.y);
                    face.Add((home==null?city:PlayerHomeBalconyGeometry.ToHomeLocal(home.PlayerHome,city),
                        tex/(roadGeometry==null?CityExteriorAppearance.SidewalkTextureTileSize:CityExteriorAppearance.RoadTextureTileSize)));
                }
                if(home!=null)
                {
                    // Preserve the City's original A-C diagonal before clipping.
                    // A graded quad can be non-planar: clipping it as a whole
                    // and choosing a new fan would change its physical planes.
                    for(int triangle=1;triangle<face.Count-1;triangle++)
                    {
                        var original=new[]{face[0],face[triangle],face[triangle+1]};
                        var clipped=new List<(Vector3 point,Vector2 uv)>(4);
                        for(int i=0;i<3;i++)
                        {
                            var a=original[i];var b=original[(i+1)%3];
                            bool inside=a.point.x>=HomeExteriorViewBuilder.ExteriorMinimumX;
                            bool nextInside=b.point.x>=HomeExteriorViewBuilder.ExteriorMinimumX;
                            if(inside)clipped.Add(a);
                            if(inside==nextInside)continue;
                            float t=(HomeExteriorViewBuilder.ExteriorMinimumX-a.point.x)/(b.point.x-a.point.x);
                            clipped.Add((Vector3.Lerp(a.point,b.point,t),Vector2.Lerp(a.uv,b.uv,t)));
                        }
                        AppendFace(clipped);
                    }
                    return;
                }
                AppendFace(face);
            }

            private void AppendFace(IReadOnlyList<(Vector3 point,Vector2 uv)> face)
            {
                if(face.Count<3)return;
                int start=vertices.Count;
                foreach(var point in face){vertices.Add(point.point);uv.Add(point.uv);}
                for(int i=1;i<face.Count-1;i++){triangles.Add(start);triangles.Add(start+i);triangles.Add(start+i+1);}
            }
            internal GameObject Finish(string name,Transform parent,bool collider,int priority=30)
            {
                if(vertices.Count==0)return null;
                var mesh=new Mesh{name=name+" Graded Mesh"};
                if(vertices.Count>ushort.MaxValue)mesh.indexFormat=UnityEngine.Rendering.IndexFormat.UInt32;
                mesh.SetVertices(vertices);mesh.SetUVs(0,uv);mesh.SetTriangles(triangles,0);
                mesh.RecalculateNormals();mesh.RecalculateBounds();
                var surface=new GameObject(name);surface.transform.SetParent(parent,false);
                surface.AddComponent<MeshFilter>().sharedMesh=mesh;
                MeshRenderer renderer=surface.AddComponent<MeshRenderer>();renderer.sharedMaterial=RuntimePrimitiveFactory.DefaultMaterial;
                if(roadGeometry==null)CityExteriorAppearance.ApplySidewalkSurface(renderer);
                else
                {
                    CityExteriorAppearance.ApplyRoadSurface(renderer);
                    GroundSurfaceCoordinates.AssignBoxes(mesh,roadGeometry,CityStreetSurfacePlanner.SidewalkWidth,
                        point=>PlayerHomeBalconyGeometry.ToCityWorld(home.PlayerHome,point));
                    GroundSurfaceCoordinates.Enable(renderer);
                }
                if(collider)surface.AddComponent<MeshCollider>().sharedMesh=mesh;
                FootstepGround.Stamp(surface,roadGeometry==null?FootstepGroundKind.Stone:FootstepGroundKind.Concrete);
                CityGroundSurfaceSystem.Register(surface,priority);
                surface.AddComponent<RuntimeGeneratedMeshOwner>().Initialize(mesh);
                return surface;
            }
        }
    }
}

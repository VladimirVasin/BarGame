using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using Triangle=BarPromenade.GroundSurfacePartitioner.Triangle;
using Vertex=BarPromenade.GroundSurfacePartitioner.Vertex;

namespace BarPromenade
{
    internal static class CityPawnshopBenchTerraceWorld
    {
        internal const string RootName="Pawnshop bench terrace";
        internal const string ModelPath="City/BenchAccess/BenchAccessStair";

        internal static void Build(Transform parent,CityLayout layout,Rect? retained)
        {
            var plan=CityPawnshopBenchTerracePlan.ForLayout(layout);
            if(!plan.IsEnabled || retained.HasValue && !retained.Value.Overlaps(plan.Bounds)) return;
            var root=new GameObject(RootName).transform;root.SetParent(parent,false);
            var template=Resources.Load<GameObject>(ModelPath);
            if(template==null) throw new System.InvalidOperationException("Missing Blender bench access staircase.");
            CityTerracePilotWorld.Model(template,root,"Broad bench access stair",plan.Flight.Start,
                Quaternion.LookRotation(plan.Flight.Direction),Vector3.one);
            var statue=Resources.Load<GameObject>("City/BenchAccess/BenchPigeonStatue");
            if(statue==null) throw new System.InvalidOperationException("Missing imported concrete bench pigeon.");
            CityTerracePilotWorld.Model(statue,root,"Concrete pigeon between benches",plan.StatuePosition,
                Quaternion.LookRotation(-plan.Flight.Direction),Vector3.one*CityPawnshopBenchTerracePlan.StatueScale);
            var flowerbedTemplate=Resources.Load<GameObject>("City/BenchAccess/BenchFlowerbed");
            if(flowerbedTemplate==null) throw new System.InvalidOperationException("Missing planted bench flowerbed.");
            for(int index=0;index<plan.FlowerbedPositions.Count;index++)
            {
                var flowerbed=CityTerracePilotWorld.Model(flowerbedTemplate,root,"Bench flowerbed "+index,
                    plan.FlowerbedPositions[index],Quaternion.identity,Vector3.one);
                foreach(Renderer renderer in flowerbed.GetComponentsInChildren<Renderer>())
                {
                    if(renderer.name.StartsWith("Soil__",System.StringComparison.Ordinal))
                        CityExteriorAppearance.ApplyGroundSurface(renderer,new Color(.32f,.30f,.25f));
                    else if(renderer.name.StartsWith("Leaves__",System.StringComparison.Ordinal))
                        CityParkSurfaceAppearance.ApplyCombined(renderer,CityParkSurfaceKind.Foliage,new Color(.25f,.36f,.23f));
                    else if(renderer.name.StartsWith("Flowers",System.StringComparison.Ordinal))
                    {
                        Color color=renderer.name.StartsWith("FlowersCream",System.StringComparison.Ordinal)
                            ?new Color(.84f,.79f,.64f):renderer.name.StartsWith("FlowersRed",System.StringComparison.Ordinal)
                                ?new Color(.58f,.22f,.25f):new Color(.46f,.34f,.56f);
                        renderer.sharedMaterial=CityPortAssetProvider.GetSurfaceMaterial("Plain");
                        RuntimePrimitiveFactory.SetColor(renderer,color);
                        var properties=new MaterialPropertyBlock();renderer.GetPropertyBlock(properties);
                        properties.SetTexture("_BaseMap",Texture2D.whiteTexture);
                        properties.SetFloat("_Metallic",0f);properties.SetFloat("_Smoothness",.08f);
                        renderer.SetPropertyBlock(properties);
                    }
                }
            }
            var ground=CityHillsideGroundPlan.ForLayout(layout);
            Rect upper=plan.UpperLanding.Bounds;float top=plan.UpperLanding.Center.y;
            BuildLanding("Shared upper bench landing",new[] {
                new Vector3(upper.xMin,top,upper.yMin),new Vector3(upper.xMax,top,upper.yMin),
                new Vector3(upper.xMax,top,upper.yMax),new Vector3(upper.xMin,top,upper.yMax) },asphalt:true);
            BuildLanding("Bench stair lower landing",plan.LowerLandingTopVertices);
            if(retained.HasValue) return;
            Collider("Concrete pigeon solid",plan.StatuePosition+Vector3.up*.99f,
                new Vector3(1.08f,1.98f,1.08f),Quaternion.identity);
            foreach(Vector3 flowerbed in plan.FlowerbedPositions)
                Collider("Bench flowerbed solid",flowerbed+Vector3.up*.3f,new Vector3(1.3f,.6f,.7f),Quaternion.identity);
            FootProbeSurface.EnsureRuntimePolicy();
            Vector3 delta=plan.Flight.End-plan.Flight.Start;
            Quaternion slope=Quaternion.LookRotation(delta.normalized,Vector3.up);
            StairWalkableSurface.Stamp(Collider("Bench stair continuous capsule support",(plan.Flight.Start+plan.Flight.End)*.5f-
                slope*Vector3.up*.06f,new Vector3(plan.Width,.12f,delta.magnitude),slope));
            for(int side=-1;side<=1;side+=2)
                Collider("Bench stair outer guard " + side,(plan.Flight.Start+plan.Flight.End)*.5f+
                    Vector3.up*.55f+plan.Flight.Right*(side*(plan.Width*.5f-.12f)),
                    new Vector3(.12f,1.1f,delta.magnitude),slope);
            var probes=new GameObject("Bench stair visible tread probes");
            probes.transform.SetParent(root,false);probes.transform.localPosition=plan.Flight.Start;
            probes.transform.localRotation=Quaternion.LookRotation(plan.Flight.Direction);
            probes.layer=FootProbeSurface.LayerIndex;
            FootstepGround.Stamp(probes,FootstepGroundKind.Stone);
            for(int step=0;step<plan.StairCount;step++)
            {
                var probe=probes.AddComponent<BoxCollider>();
                probe.center=new Vector3(0f,(step+1)*plan.Rise-.025f,(step+.5f)*plan.Tread);
                probe.size=new Vector3(plan.Width,.05f,plan.Tread);probe.isTrigger=true;
            }

            void BuildLanding(string name,IReadOnlyList<Vector3> quad,bool asphalt=false)
            {
                var faces=new List<Vector3[]> {new[] {quad[0],quad[1],quad[2]},new[] {quad[0],quad[2],quad[3]}};
                var retaining=asphalt?new List<Vector3[]>():faces;
                // Close each cut against the real neighbouring soil. These are
                // short retaining faces, not another flat skin under the stair.
                for(int i=0;i<quad.Count;i++)
                {
                    Vector3 a=quad[i],b=quad[(i+1)%quad.Count];
                    if(Mathf.Abs(a.z-plan.Flight.Start.z)<.001f && Mathf.Abs(b.z-plan.Flight.Start.z)<.001f) continue;
                    if(Mathf.Abs(a.z-plan.Flight.End.z)<.001f && Mathf.Abs(b.z-plan.Flight.End.z)<.001f)
                    {
                        float min=Mathf.Min(a.x,b.x),max=Mathf.Max(a.x,b.x);
                        if(min<plan.StairFootprint.xMin) Retaining(new Vector3(min,a.y,a.z),new Vector3(plan.StairFootprint.xMin,a.y,a.z));
                        if(max>plan.StairFootprint.xMax) Retaining(new Vector3(plan.StairFootprint.xMax,a.y,a.z),new Vector3(max,a.y,a.z));
                        continue;
                    }
                    Retaining(a,b);
                }
                GameObject surface=CityHillsideBenchPocketWorld.BuildMesh(name,root,faces,false,asphalt:asphalt);
                GameObject edges=asphalt?CityHillsideBenchPocketWorld.BuildMesh("Upper bench retaining edges",root,retaining,false):null;
                if(!retained.HasValue)
                {
                    surface.AddComponent<MeshCollider>().sharedMesh=surface.GetComponent<MeshFilter>().sharedMesh;
                    FootstepGround.Stamp(surface,asphalt?FootstepGroundKind.Concrete:FootstepGroundKind.Stone);
                    if(edges!=null)
                    {
                        edges.AddComponent<MeshCollider>().sharedMesh=edges.GetComponent<MeshFilter>().sharedMesh;
                        FootstepGround.Stamp(edges,FootstepGroundKind.Stone);
                    }
                }

                void Retaining(Vector3 a,Vector3 b)
                {
                    ground.TrySampleTop(XZ(a),out float ay);ground.TrySampleTop(XZ(b),out float by);
                    Vector3 highA=new(a.x,Mathf.Max(a.y,ay),a.z),highB=new(b.x,Mathf.Max(b.y,by),b.z);
                    Vector3 lowA=new(a.x,Mathf.Min(a.y,ay)-.08f,a.z),lowB=new(b.x,Mathf.Min(b.y,by)-.08f,b.z);
                    retaining.Add(new[] {highA,lowA,lowB});retaining.Add(new[] {highA,lowB,highB});
                }
            }
            BoxCollider Collider(string name,Vector3 center,Vector3 size,Quaternion rotation)
            {
                var host=new GameObject(name);host.transform.SetParent(root,false);
                host.transform.localPosition=center;host.transform.localRotation=rotation;
                BoxCollider collider=host.AddComponent<BoxCollider>();collider.size=size;
                FootstepGround.Stamp(host,FootstepGroundKind.Stone);
                return collider;
            }
        }

        /// <summary>Remove both visual and physical native earth inside the replacement surfaces.</summary>
        internal static void CutGroundMesh(CityLayout layout,Mesh mesh)
        {
            var plan=CityPawnshopBenchTerracePlan.ForLayout(layout);
            if(plan.IsEnabled) CutGroundMesh(mesh,plan.Footprints,plan.Bounds);
            var descent=CityPawnshopDescentPlan.ForLayout(layout);
            if(descent.IsEnabled) CutGroundMesh(mesh,descent.GroundCuts,descent.Bounds);
        }

        private static void CutGroundMesh(Mesh mesh,IReadOnlyList<Vector2[]> footprints,Rect bounds)
        {
            Rect meshBounds=Rect.MinMaxRect(mesh.bounds.min.x,mesh.bounds.min.z,mesh.bounds.max.x,mesh.bounds.max.z);
            if(!meshBounds.Overlaps(bounds)) return;
            var positions=new List<Vector3>(mesh.vertices);var normals=new List<Vector3>(mesh.normals);
            var uv=new List<Vector2>(mesh.uv);var colors=new List<Color>(mesh.colors);
            var road=new List<Vector4>();mesh.GetUVs(GroundSurfaceCoordinates.Channel,road);
            while(colors.Count<positions.Count) colors.Add(Color.white);
            while(road.Count<positions.Count) road.Add(Vector4.zero);
            var cuts=new List<GroundSurfacePartitioner.Footprint>();
            foreach(var polygon in footprints) cuts.Add(new GroundSurfacePartitioner.Footprint(polygon));
            var layers=new List<int>[mesh.subMeshCount];
            for(int slot=0;slot<layers.Length;slot++)
            {
                int[] original=mesh.GetTriangles(slot);var indices=new List<int>(original.Length);layers[slot]=indices;
                for(int i=0;i<original.Length;i+=3)
                {
                    int a=original[i],b=original[i+1],c=original[i+2];
                    Vector3 lo=Vector3.Min(positions[a],Vector3.Min(positions[b],positions[c]));
                    Vector3 hi=Vector3.Max(positions[a],Vector3.Max(positions[b],positions[c]));
                    Rect triangleBounds=Rect.MinMaxRect(lo.x,lo.z,hi.x,hi.z);
                    bool nearby=false;
                    foreach(var cut in cuts) if(triangleBounds.Overlaps(cut.Bounds)) {nearby=true;break;}
                    if(!nearby) {indices.Add(a);indices.Add(b);indices.Add(c);continue;}
                    var remaining=new List<Triangle> {new(At(a),At(b),At(c),slot)};
                    bool changed=false;
                    foreach(var cut in cuts)
                    {
                        if(!triangleBounds.Overlaps(cut.Bounds)) continue;
                        var next=new List<Triangle>();
                        foreach(var triangle in remaining) next.AddRange(GroundSurfacePartitioner.Subtract(triangle,cut));
                        remaining=next;changed=true;
                    }
                    if(!changed) {indices.Add(a);indices.Add(b);indices.Add(c);continue;}
                    foreach(var triangle in remaining)
                    {
                        Emit(triangle.A);Emit(triangle.B);Emit(triangle.C);
                    }
                }
                void Emit(Vertex vertex)
                {
                    indices.Add(positions.Count);positions.Add(vertex.Position);normals.Add(vertex.Normal);
                    uv.Add(vertex.Uv0);road.Add(vertex.RoadCoordinates);colors.Add(vertex.Color);
                }
            }
            mesh.Clear();mesh.indexFormat=positions.Count>ushort.MaxValue?IndexFormat.UInt32:IndexFormat.UInt16;
            mesh.SetVertices(positions);mesh.SetNormals(normals);mesh.SetUVs(0,uv);
            mesh.SetUVs(GroundSurfaceCoordinates.Channel,road);mesh.SetColors(colors);mesh.subMeshCount=layers.Length;
            for(int i=0;i<layers.Length;i++) mesh.SetTriangles(layers[i],i,false);
            mesh.RecalculateBounds();
            Vertex At(int index)=>new(positions[index],normals[index],uv[index],road[index],colors[index]);
        }

        private static Vector2 XZ(Vector3 p)=>new(p.x,p.z);
    }
}

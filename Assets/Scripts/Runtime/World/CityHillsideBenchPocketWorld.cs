using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace BarPromenade
{
    /// <summary>Imported municipal fixtures and the shared patch topology for their lenses and landings.</summary>
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
            var lampParts = new List<RuntimeMeshPlacement>();
            var lenses = new List<Vector3[]>();
            var provider = CityMiscAssetProvider.LoadOrThrow();
            var lamp = provider.GetPartOrThrow(CityMiscKind.StreetLampShell,0,0);
            foreach (var pocket in plan.Pockets)
            {
                if (retained.HasValue && !retained.Value.Overlaps(pocket.Placement.AccessBounds)) continue;
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
                // The frosted underside uses the imported landing topology.
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
            CityPawnshopBenchTerraceWorld.Build(root,layout,retained);
            CityPawnshopDescentWorld.Build(root,layout,retained);
        }

        internal static GameObject BuildMesh(string name,Transform parent,IReadOnlyList<Vector3[]> faces,bool lens,
            bool asphalt=false)
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
                    vertices.Add(p);normals.Add(normal);
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
}

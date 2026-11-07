using System;
using System.Collections.Generic;
using UnityEngine;

namespace BarPromenade
{
    internal static class CityPawnshopDescentWorld
    {
        internal const string RootName="Pawnshop hillside descent";
        internal static void Build(Transform parent,CityLayout layout,Rect? retained=null)
        {
            var plan=CityPawnshopDescentPlan.ForLayout(layout);
            if(!plan.IsEnabled || retained.HasValue && !retained.Value.Overlaps(plan.Bounds)) return;
            var root=new GameObject(RootName).transform;root.SetParent(parent,false);
            root.localPosition=plan.UpperDock;root.localRotation=plan.Rotation;
            FootstepGround.Stamp(root.gameObject,FootstepGroundKind.Stone);
            string folder=CityTerracePilotWorld.ResourceFolder;
            var flightModel=Load("TerraceFlight");var court=Load("TerraceCourt");
            var rail=Load("TerraceRail");var support=Load("TerraceSupport");
            float foundation=plan.LowerDock.y-plan.UpperDock.y-1.2f;
            FootProbeSurface.EnsureRuntimePolicy();
            foreach(var world in plan.Flights)
            {
                var local=new CityExteriorStairFlightDescriptor(world.Id,Local(world.Start),Vector3.back,
                    world.Width,world.StepCount,world.StepRise,world.TreadDepth,world.DropSides);
                CityTerracePilotWorld.BuildFlight(root,local,flightModel,support,foundation,
                    local.StepRise/CityTerracePilotPlan.StepRise);
            }
            foreach(var landing in plan.Landings)
            {
                Vector3 start=Local(landing.Start),end=Local(landing.End),center=(start+end)*.5f;
                var size=new Vector2(landing.Width,landing.Length);
                CityTerracePilotWorld.Model(court,root,landing.Id,center,Quaternion.identity,
                    new Vector3(size.x/CityTerracePilotPlan.CourtSize,1f,size.y/CityTerracePilotPlan.CourtSize));
                CityTerracePilotWorld.Box(root,landing.Id+" support",center-Vector3.up*.15f,
                    new Vector3(size.x,.30f,size.y),Quaternion.identity);
                CityTerracePilotWorld.Support(support,root,landing.Id+" foundation",center-Vector3.up*.30f,size,foundation);
                for(int side=-1;side<=1;side+=2)
                {
                    Vector3 offset=Vector3.right*(side*(landing.Width*.5f-.06f));
                    // The flush bench-side entrance is open at the rear mouth.
                    // Every flight and the remaining landing edges retain guards.
                    Vector3 railStart=landing==plan.Landings[0] && side==1
                        ?start+Vector3.forward*Mathf.Min(1.2f,landing.Length):start;
                    if(Vector3.Distance(railStart,end)>.03f)
                        CityTerracePilotWorld.BuildRailRun(root,rail,landing.Id+" rail "+side,railStart+offset,end+offset);
                }
            }
            foreach(var approach in plan.ApproachLandings)
            {
                Vector3 start=Local(approach.Start),end=Local(approach.End),delta=end-start;
                Quaternion slope=Quaternion.LookRotation(delta.normalized,Vector3.up);
                Vector3 center=(start+end)*.5f;
                CityTerracePilotWorld.Model(court,root,approach.Id,center,slope,
                    new Vector3(approach.Width/CityTerracePilotPlan.CourtSize,1f,delta.magnitude/CityTerracePilotPlan.CourtSize));
                CityTerracePilotWorld.Box(root,approach.Id+" support",center-slope*Vector3.up*.15f,
                    new Vector3(approach.Width,.30f,delta.magnitude),slope);
            }
            BuildRetaining(root,layout,plan);
            CitySurfaceLibrary.ApplyHierarchy(root);
            BuildLights(root,plan,retained.HasValue);
            if(retained.HasValue) foreach(var collider in root.GetComponentsInChildren<Collider>()) collider.enabled=false;

            Vector3 Local(Vector3 p)=>Quaternion.Inverse(plan.Rotation)*(p-plan.UpperDock);
            GameObject Load(string name)=>Resources.Load<GameObject>(folder+name) ??
                throw new InvalidOperationException("Missing shared municipal stair model "+name);
        }

        private static void BuildRetaining(Transform root,CityLayout layout,CityPawnshopDescentPlan plan)
        {
            var ground=CityHillsideGroundPlan.ForLayout(layout);
            var faces=new List<Vector3[]>();
            foreach(var polygon in plan.GroundCuts)
            for(int edge=0;edge<polygon.Length;edge++)
            {
                Vector2 a=polygon[edge],b=polygon[(edge+1)%polygon.Length];
                Vector2 middle=(a+b)*.5f;
                bool joined=false;
                foreach(var other in plan.GroundCuts)
                    if(!ReferenceEquals(other,polygon) && CityRoadPolygon.Contains(other,middle)) {joined=true;break;}
                if(joined) continue;
                int count=Mathf.Max(1,Mathf.CeilToInt(Vector2.Distance(a,b)/.5f));
                for(int i=0;i<count;i++)
                {
                    Vector2 from=Vector2.Lerp(a,b,(float)i/count),to=Vector2.Lerp(a,b,(float)(i+1)/count);
                    if(!plan.TrySampleTop(from,out float fy) || !plan.TrySampleTop(to,out float ty) ||
                        !ground.TrySampleTop(from,out float soilFrom) || !ground.TrySampleTop(to,out float soilTo)) continue;
                    Vector3 highA=Local(new Vector3(from.x,Mathf.Max(fy,soilFrom),from.y));
                    Vector3 highB=Local(new Vector3(to.x,Mathf.Max(ty,soilTo),to.y));
                    Vector3 lowA=Local(new Vector3(from.x,Mathf.Min(fy,soilFrom)-.10f,from.y));
                    Vector3 lowB=Local(new Vector3(to.x,Mathf.Min(ty,soilTo)-.10f,to.y));
                    faces.Add(new[] {highA,lowA,lowB});faces.Add(new[] {highA,lowB,highB});
                }
            }
            var retaining=CityHillsideBenchPocketWorld.BuildMesh("Descent fitted retaining edges",root,faces,false,raisedSkin:false);
            if(retaining!=null)
            {
                retaining.AddComponent<MeshCollider>().sharedMesh=retaining.GetComponent<MeshFilter>().sharedMesh;
                FootstepGround.Stamp(retaining,FootstepGroundKind.Stone);
            }
            Vector3 Local(Vector3 p)=>Quaternion.Inverse(plan.Rotation)*(p-plan.UpperDock);
        }

        private static void BuildLights(Transform root,CityPawnshopDescentPlan plan,bool passive)
        {
            var part=CityMiscAssetProvider.LoadOrThrow().GetPartOrThrow(CityMiscKind.StreetLampShell,0,0);
            var placements=new List<RuntimeMeshPlacement>(2);var lenses=new List<Vector3[]>(4);
            for(int i=0;i<plan.LampFeet.Count;i++)
            {
                Vector3 foot=Local(plan.LampFeet[i]);
                Vector3 target=Local(i==0?plan.StairRoute[2]:plan.StairRoute[4]);
                Vector3 forward=Vector3.ProjectOnPlane(target-foot,Vector3.up).normalized;
                Quaternion rotation=Quaternion.LookRotation(forward);
                placements.Add(new RuntimeMeshPlacement(part.Mesh,foot,rotation,Vector3.one*.64f));
                var fixture=new GameObject(i==0?"Descent upper lamp":"Descent landing lamp").transform;
                fixture.SetParent(root,false);
                Vector3 source=foot+Vector3.up*3.2f+forward*.6848f;
                var anchor=new GameObject(CityHillsideBenchPocketPlan.LightAnchorName).transform;
                anchor.SetParent(fixture,false);anchor.localPosition=source;
                anchor.localRotation=Quaternion.LookRotation(target-source,Vector3.up);
                Vector3 right=rotation*Vector3.right*.1216f,front=forward*.055f;
                source-=Vector3.up*.005f;
                lenses.Add(new[] {source-right-front,source+right-front,source+right+front});
                lenses.Add(new[] {source-right-front,source+right+front,source-right+front});
                if(!passive)
                {
                    var collider=fixture.gameObject.AddComponent<BoxCollider>();collider.center=foot+Vector3.up*1.55f;
                    collider.size=new Vector3(.24f,3.1f,.24f);
                }
            }
            var lamps=RuntimePrimitiveFactory.CreateCombinedMeshes("Imported descent lamps",root,placements,new Color(.24f,.255f,.235f));
            CityParkSurfaceAppearance.ApplyCombined(lamps.GetComponent<Renderer>(),CityParkSurfaceKind.PaintedMetal,new Color(.24f,.255f,.235f));
            CityHillsideBenchPocketWorld.BuildMesh("Descent lamp lenses",root,lenses,true);
            Vector3 Local(Vector3 p)=>Quaternion.Inverse(plan.Rotation)*(p-plan.UpperDock);
        }
    }
}

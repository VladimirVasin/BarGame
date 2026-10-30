using System;
using System.Collections.Generic;
using UnityEngine;

namespace BarPromenade
{
    public struct StairwellLabyrinthSupportPlan
    {
        public StairwellLabyrinthSupportPlan(string id, int stage, Vector3 start, Vector3 end, float width, Rect bounds)
        { Id=id; Stage=stage; Start=start; End=end; Width=width; Bounds=bounds; Connections=Array.Empty<int>(); }
        public string Id { get; }
        public int Stage { get; }
        public Vector3 Start { get; }
        public Vector3 End { get; }
        public float Width { get; }
        public Rect Bounds { get; }
        public bool IsRamp => Mathf.Abs(End.y-Start.y)>.01f;
        public IReadOnlyList<int> Connections { get; internal set; }
        public float HeightAt(Vector3 local)
        {
            Vector2 delta=new Vector2(End.x-Start.x,End.z-Start.z);
            float t=delta.sqrMagnitude>.001f ? Mathf.Clamp01(Vector2.Dot(new Vector2(local.x-Start.x,local.z-Start.z),delta)/delta.sqrMagnitude):0f;
            return Mathf.Lerp(Start.y,End.y,t);
        }
    }

    public sealed class StairwellLabyrinthBranchPlan
    {
        public StairwellLabyrinthBranchPlan(string id,int stage,bool isLoop,Vector3[] waypoints)
        { Id=id;Stage=stage;IsLoop=isLoop;Waypoints=Array.AsReadOnly(waypoints); }
        public string Id { get; }
        public int Stage { get; }
        public bool IsLoop { get; }
        public IReadOnlyList<Vector3> Waypoints { get; }
    }

    public readonly struct StairwellLabyrinthLightPlan
    {
        public StairwellLabyrinthLightPlan(Vector3 position,Vector3 direction,int stage,bool castsShadows,bool isScenery=false)
        { Position=position;Direction=direction;Stage=stage;CastsShadows=castsShadows;IsScenery=isScenery; }
        public Vector3 Position { get; }
        public Vector3 Direction { get; }
        public int Stage { get; }
        public bool CastsShadows { get; }
        public bool IsScenery { get; }
    }

    /// <summary>Pure metric graph shared with the deterministic Blender source through generated data.</summary>
    public sealed class StairwellLabyrinthPlan
    {
        public const float FlightWidth=3.2f;
        public IReadOnlyList<StairwellFlightPlan> Flights { get; }
        public IReadOnlyList<StairwellLabyrinthSupportPlan> Supports { get; }
        public IReadOnlyList<StairwellLabyrinthBranchPlan> Branches { get; }
        public IReadOnlyList<Rect> FlightBounds { get; }
        public IReadOnlyList<Rect> Landings { get; }
        public IReadOnlyList<Rect> TraversalBounds { get; }
        public IReadOnlyList<float> LandingElevations { get; }
        public IReadOnlyList<Vector3> RouteWaypoints { get; }
        public IReadOnlyList<Vector3> LightPositions { get; }
        public IReadOnlyList<StairwellLabyrinthLightPlan> LightDescriptors { get; }
        public IReadOnlyList<Vector3> CapturePositions { get; }
        public IReadOnlyList<Vector3> CaptureLookTargets { get; }
        public string ContractSignature => StairwellLabyrinthData.Signature;
        public float MainRouteLength => StairwellLabyrinthData.MainRouteLength;
        public Vector3 DoorPosition => new Vector3(45f,15.6f,74f);
        public Vector3 DoorForward => Vector3.forward;
        public Vector3 DoorActionRoot => DoorPosition+new Vector3(-.715724f,.04f,-.719976f);

        public StairwellLabyrinthPlan()
        {
            var supports=StairwellLabyrinthData.CreateSupports();
            var flights=new List<StairwellFlightPlan>(3);
            var flightBounds=new List<Rect>(3);var landings=new List<Rect>();var elevations=new List<float>();
            var allBounds=new List<Rect>(supports.Length);
            for(int i=0;i<supports.Length;i++)
            {
                var support=supports[i];var neighbours=new List<int>();
                for(int j=0;j<supports.Length;j++)
                {
                    if(i==j)continue;
                    Rect other=supports[j].Bounds;
                    float x0=Mathf.Max(support.Bounds.xMin,other.xMin),x1=Mathf.Min(support.Bounds.xMax,other.xMax);
                    float z0=Mathf.Max(support.Bounds.yMin,other.yMin),z1=Mathf.Min(support.Bounds.yMax,other.yMax);
                    if(x1<x0-.01f||z1<z0-.01f)continue;
                    var p=new Vector3((x0+x1)*.5f,0f,(z0+z1)*.5f);
                    float gap=Mathf.Abs(support.HeightAt(p)-supports[j].HeightAt(p));
                    for(int corner=0;corner<4;corner++)
                    {
                        p=new Vector3((corner&1)==0?x0:x1,0f,(corner&2)==0?z0:z1);
                        gap=Mathf.Min(gap,Mathf.Abs(support.HeightAt(p)-supports[j].HeightAt(p)));
                    }
                    if(gap<.15f)neighbours.Add(j);
                }
                support.Connections=neighbours.AsReadOnly();supports[i]=support;allBounds.Add(support.Bounds);
                if(support.IsRamp)
                {
                    Vector2 direction=new Vector2(support.End.x-support.Start.x,support.End.z-support.Start.z).normalized;
                    flights.Add(new StairwellFlightPlan(support.Id,new Vector2(support.Start.x,support.Start.z),direction,support.Start.y,36,.1f,.24f,FlightWidth));
                    flightBounds.Add(support.Bounds);
                }
                else {landings.Add(support.Bounds);elevations.Add(support.Start.y);}
            }
            Supports=Array.AsReadOnly(supports);Flights=flights.AsReadOnly();FlightBounds=flightBounds.AsReadOnly();
            Landings=landings.AsReadOnly();LandingElevations=elevations.AsReadOnly();TraversalBounds=allBounds.AsReadOnly();
            Branches=Array.AsReadOnly(StairwellLabyrinthData.CreateBranches());
            var route=StairwellLabyrinthData.CreateRoute();route[route.Length-1]=DoorActionRoot;RouteWaypoints=Array.AsReadOnly(route);
            var lights=StairwellLabyrinthData.CreateLights();LightDescriptors=Array.AsReadOnly(lights);
            var positions=new Vector3[lights.Length];for(int i=0;i<lights.Length;i++)positions[i]=lights[i].Position;
            LightPositions=Array.AsReadOnly(positions);
            CapturePositions=Array.AsReadOnly(new[]{new Vector3(-13f,8.44f,28f),new Vector3(29f,12.04f,32f),new Vector3(28f,15.64f,63f)});
            CaptureLookTargets=Array.AsReadOnly(new[]{new Vector3(-5f,15f,30f),new Vector3(25f,19f,36f),new Vector3(37f,22f,66f)});
        }

        public bool ContainsPosition(Vector3 position,float margin=0f)
        {
            if(position.z<2.3f-margin||position.y<4.6f)return false;
            for(int i=0;i<Supports.Count;i++)
            {
                var s=Supports[i];Rect r=s.Bounds;
                if(position.x>=r.xMin-margin&&position.x<=r.xMax+margin&&position.z>=r.yMin-margin&&position.z<=r.yMax+margin&&
                   position.y-PlayerFactory.GroundedRootOffset-s.HeightAt(position)>-.65f&&position.y-PlayerFactory.GroundedRootOffset-s.HeightAt(position)<3.2f)return true;
            }
            return false;
        }

        public int StageAt(Vector3 position)
        {
            if(position.z<2.3f||position.y<4.6f)return 0;
            int nearest=-1;float best=float.MaxValue;
            for(int i=0;i<Supports.Count;i++)
            {
                var s=Supports[i];Rect r=s.Bounds;
                float dx=position.x-Mathf.Clamp(position.x,r.xMin,r.xMax),dz=position.z-Mathf.Clamp(position.z,r.yMin,r.yMax);
                float dy=position.y-PlayerFactory.GroundedRootOffset-s.HeightAt(position);
                float score=dx*dx+dz*dz+dy*dy*(dy<-.65f?16f:.25f);
                if(score<best){best=score;nearest=i;}
            }
            return nearest>=0&&best<4f?Supports[nearest].Stage:0;
        }

        public float ProgressAt(Vector3 position)
        {
            int stage=StageAt(position);if(stage==0)return 0f;
            // Support height, rather than hero height, keeps jumps and horizontal detours in their authored phase.
            float best=float.MaxValue,height=4.8f;
            for(int i=0;i<Supports.Count;i++)
            {
                var s=Supports[i];if(s.Stage!=stage)continue;Rect r=s.Bounds;
                float dx=position.x-Mathf.Clamp(position.x,r.xMin,r.xMax),dz=position.z-Mathf.Clamp(position.z,r.yMin,r.yMax);
                float supportY=s.HeightAt(position),dy=position.y-PlayerFactory.GroundedRootOffset-supportY;
                float score=dx*dx+dz*dz+dy*dy*(dy<-.65f?16f:.25f);
                if(score<best){best=score;height=supportY;}
            }
            return Mathf.InverseLerp(4.8f,15.6f,height);
        }
    }
}

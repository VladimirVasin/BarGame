using System;
using System.Collections.Generic;
using UnityEngine;

namespace BarPromenade
{
    public sealed partial class CityWaterNetwork
    {
        private void SurveyStreets(CityLayout layout)
        {
            var nodes = new Dictionary<Vector2Int, int>();
            int Intersection(Vector2Int cell)
            {
                if (nodes.TryGetValue(cell, out int id)) return id;
                Vector3 surface = layout.GetNodeWorldPosition(cell) + Vector3.up * CityStreetSurfacePlanner.RoadTop;
                nodes.Add(cell, id = Node(surface));
                return id;
            }
            foreach (var road in layout.RoadEdges)
            {
                bool bridge = layout.TryGetBridge(road, out var crossing);
                if (bridge && crossing.Definition.Style != CityBridgeStyle.Works) continue;
                int previous = Intersection(road.A);
                int end = Intersection(road.B);
                if (bridge)
                {
                    var bounds = crossing.SpanBounds;
                    float z = crossing.DeckBounds.yMax - .30f;
                    int west = Node(new Vector3(bounds.xMin, crossing.WestY, z));
                    int east = Node(new Vector3(bounds.xMax, crossing.EastY, z));
                    // Potable sleeve hangs below the deck. The separate sanitary
                    // crossing is buried below the bed and lifted out on the west.
                    float invert = float.PositiveInfinity;
                    foreach (var segment in layout.River.Segments)
                        if (z >= segment.WaterBounds.yMin && z <= segment.WaterBounds.yMax)
                            invert = Mathf.Min(segment.NorthWaterY, segment.SouthWaterY) - 3.8f;
                    if (!float.IsFinite(invert)) invert = crossing.AverageY - 8f;
                    junctions[west].Sewer = AtHeight(junctions[west].Sewer, invert);
                    junctions[east].Sewer = AtHeight(junctions[east].Sewer, invert + .12f);
                    Edge(previous, west, crossing:true); Edge(west,east,crossing:true); Edge(east,end,crossing:true);
                    BridgePipe(west,east);
                    continue;
                }
                var path = layout.RoadGeometry.Get(road);
                float distance = 0f;
                for (int i = 1; i < path.Vertices.Count-1; i++)
                {
                    Vector2 p = path.Vertices[i];
                    distance += Vector2.Distance(p, path.Vertices[i-1]);
                    float y = layout.WorldOrigin.y + layout.ElevationPlan.SampleRoadDatum(road,distance/path.Length);
                    int next = Node(new Vector3(p.x,y+CityStreetSurfacePlanner.RoadTop,p.y));
                    Edge(previous,next); previous = next;
                }
                Edge(previous,end);
            }

            var summits = new Dictionary<CityTerraceTransportBank,int>();
            foreach (var route in CityHillsideStreetPlan.ForLayout(layout).Routes)
            {
                bool boosted = route.Bank == CityTerraceTransportBank.West;
                int previous = Intersection(route.MouthNode);
                for (int i=0; i<route.Path.Vertices.Count; i++)
                {
                    Vector2 p = route.Path.Vertices[i];
                    int next = Node(new Vector3(p.x, route.Profile.Stations[i].Height, p.y));
                    Edge(previous, next, supply:i != 0 || boosted, crossing:i==0, onRidge:true);
                    if (boosted && i==0)
                    {
                        // Subsurface booster at the pressure boundary, serviced through a flush cover.
                        Booster = junctions[next].Water;
                        Piece(CityWaterKit.StopCover, junctions[next].Surface, Quaternion.identity);
                    }
                    previous = next;
                }
                summits.Add(route.Bank,previous);
            }
            foreach (var bridge in CityHillsideQuarterPlan.ForLayout(layout).Bridges)
            {
                if (bridge.Id != "summit") continue;
                float z = bridge.DeckBounds.yMax - .34f;
                int west = Node(new Vector3(bridge.WestBearingWorld.x,bridge.Link.RoadTopAt(bridge.WestBearingWorld.x),z));
                int east = Node(new Vector3(bridge.EastBearingWorld.x,bridge.Link.RoadTopAt(bridge.EastBearingWorld.x),z));
                Edge(summits[CityTerraceTransportBank.West],west,onRidge:true);
                Edge(west,east,drain:false,crossing:true,onRidge:true);
                Edge(east,summits[CityTerraceTransportBank.East],onRidge:true);
                BridgePipe(west,east);
            }
        }

        private void BridgePipe(int west, int east)
        {
            Vector3 a = junctions[west].Water, b = junctions[east].Water;
            Vector3 d = b-a; var rotation = Quaternion.LookRotation(d);
            Piece(CityWaterKit.WaterBarrel,a,rotation,new Vector3(1f,1f,d.magnitude));
            int bays = Mathf.CeilToInt(d.magnitude/3.8f);
            for (int i=0;i<=bays;i++)
                Piece(CityWaterKit.PipeSaddle,Vector3.Lerp(a,b,(float)i/bays),rotation,
                    new Vector3(1f,1.55f,1f));
        }

        private void SurveyBuildings(CityLayout layout, CitySeacoastPlan coast,
            CityCoastalInfrastructurePlan infrastructure, CityFringeYardPlan fringe)
        {
            var rear = new HashSet<BuildingLot>();
            var wings = new Dictionary<BuildingLot,BuildingLot>();
            foreach (var block in layout.CourtyardBlocks)
                if (block.RearBuilding != null)
                {
                    rear.Add(block.RearBuilding);
                    wings.Add(block.Primary,block.RearBuilding);
                }
            bool boilerVent = false;
            foreach (BuildingLot lot in layout.BuildingMasses)
            {
                if (!lot.HasBuilding || rear.Contains(lot) || (layout.HasSummitHome && lot == layout.PlayerHome)) continue;
                string id = lot.IsBar ? lot.BarId : lot.IsSupermarket ? "supermarket" : $"city-{lot.Cell.x}-{lot.Cell.y}";
                if (lot.IsSupermarket)
                {
                    // The imported shop uses cardinal frontage and a centred door anchor.
                    var front = new Vector3(lot.FrontageDirection.x,0f,lot.FrontageDirection.y);
                    var side = Vector3.Cross(Vector3.up,front);
                    float half = SupermarketEntranceGeometry.ExteriorWidth*.5f;
                    var corner = lot.DoorPosition + side*(half-.08f) - front*.10f;
                    CornerService(id,corner,side,front,7.1f,3.04f,1.04f,.90f,riser:1.40f);
                    continue;
                }
                if (lot.IsOrdinaryBuilding)
                {
                    bool paired = wings.TryGetValue(lot,out var wing);
                    bool exterior = paired || lot.HasFunctionalIndustrialPurpose || (lot.Cell.x+lot.Cell.y)%3==0;
                    var pose = CityBuildingPrototypePlacement.ResolveExpectedCityPose(lot);
                    Vector3 size = CityBuildingAssetProvider.GetExpectedEnvelope(lot.District,lot.BuildingVariant);
                    var foot = pose.TransformPoint(new Vector3(-size.x*.5f,0f,-size.z*.18f));
                    var normal = pose.Rotation*Vector3.left;
                    if (exterior)
                    {
                        int tee = SurveyOrdinary(id,lot,false,-1,paired);
                        if (paired) SurveyOrdinary($"city-{wing.Cell.x}-{wing.Cell.y}-rear",wing,false,tee,false);
                    }
                    else AddBuilding(id,foot,normal);
                    if (!boilerVent && lot.IndustrialPurpose == CityIndustrialPurpose.BoilerHouse)
                    { Vent(foot,normal); boilerVent = true; }
                    continue;
                }
                AddBuilding(id,lot.DoorPosition+lot.FacadeRotation*Vector3.right*2.3f,lot.FacadeForward);
            }
            foreach (var consumer in CityElectricalConsumerPlan.ForLayout(layout).Consumers)
            {
                // The upper survey knows the actual wall face (including the home
                // gable and cobbler's annex). Bring that same face down to the plinth.
                if (consumer.Attachments.Count == 0)
                {
                    Vector2 a = consumer.Footprint[0], b = consumer.Footprint[1];
                    Vector2 axis = (b-a).normalized;
                    Vector2 wall = Vector2.Lerp(a,b,.3f);
                    AddBuilding(consumer.StableId,new Vector3(wall.x,consumer.BaseY,wall.y),
                        new Vector3(axis.y,0f,-axis.x),true);
                    continue;
                }
                var pin = consumer.Attachments[0];
                Vector3 foot = AtHeight(pin.Position - pin.Outward*.055f,consumer.BaseY);
                if (consumer.Lot?.IsPlayerHome == true)
                {
                    var lot = consumer.Lot;
                    // The refuse station occupies the left frontage. Use the right
                    // brick pier between the two ground-floor windows for the riser.
                    var side = lot.FacadeRotation*Vector3.right;
                    var corner = lot.DoorPosition+side*6.39f;
                    CornerService(consumer.StableId,corner,side,lot.FacadeForward,6.95f,3.20f,.84f,.83f,true,riser:1.42f);
                    Vent(foot,pin.Outward);
                }
                else if (consumer.Lot?.IsOrdinaryBuilding == true && (consumer.Lot.Cell.y & 1)==0)
                    SurveyOrdinary(consumer.StableId,consumer.Lot,true,-1,false);
                else AddBuilding(consumer.StableId,foot,pin.Outward,true);
            }
            foreach (var kiosk in CityKioskSites.ForLayout(layout).Sites)
                if (!kiosk.IsRidge)
                    AddBuilding(kiosk.StableId,kiosk.Origin+kiosk.Rotation*new Vector3(-CityKioskSitePlan.WallHalfWidth,0f,-.6f),
                        kiosk.Rotation*Vector3.left);
            foreach (var facility in infrastructure.Facilities)
                if (facility.Assembly!="MunicipalWorkshop")
                    AddBuilding(facility.StableId, facility.Position + facility.Rotation*new Vector3(-facility.Size.x*.5f,0f,-.6f),
                        facility.Rotation*Vector3.left);
            var cafe = CityCafePlan.Create(layout);
            if (cafe != null) BoundsBuilding("city-diner",cafe.Bounds,cafe.FloorY);
            var church = CityChurchPlanner.Create(layout);
            if (church != null) AddBuilding("church",church.DoorGroundPosition + church.ModelRotation*Vector3.right*1.8f,
                church.EntranceOutwardDirection);
            var laundry = CityLaundryPlan.ForLayout(layout);
            if (laundry.IsEnabled) AddBuilding("laundry", laundry.World(new Vector3(-4.12f,0f,-1f)),Vector3.left);
            var cannery = CityCanneryPlan.Create(layout);
            if (cannery!=null) AddBuilding("cannery",cannery.World(new Vector3(-8f,CityCanneryPlan.YardTop,2f)),-cannery.Right);
            if (coast.Port!=null) BoundsBuilding("port-warehouse",coast.Port.WarehouseBounds,coast.Port.QuayTopY);
            if (coast.TryGetPart("seacoast-hut-floor",out var hut))
                AddBuilding("boat-station",hut.Center+new Vector3(-hut.Size.x*.5f,hut.Size.y*.5f,0f),Vector3.left);
            var east = CityEastExitPlanner.Create(layout);
            if (east.IsEnabled) BoundsBuilding("east-guard-booth",east.BoothBounds,east.BoothPosition.y);
            fringe ??= CityFringeYardPlanner.Create(layout,CityMountainBoundaryPlanner.Create(layout));
            foreach (var yard in fringe.Yards)
                foreach (var part in yard.Parts)
                    if (part.Kind == CityFringeYardPartKind.UtilityShed)
                        BoundsBuilding(part.StableId,part.Footprint,part.Center.y-part.Size.y*.5f);
        }

        private void BoundsBuilding(string id, Rect footprint, float floor) =>
            AddBuilding(id,new Vector3(footprint.xMin,floor,footprint.center.y),Vector3.left);

        private void AddBuilding(string id, Vector3 foot, Vector3 normal, bool onRidge = false)
        {
            var rotation = Quaternion.LookRotation(normal);
            Vector3 sideways = rotation*Vector3.right;
            int tap = TapStreet(foot,onRidge);
            var inlet = foot + normal*.15f - Vector3.up*.20f;
            var drain = foot - sideways*.7f - Vector3.up*.73f;
            RegisterService(id,inlet,drain,tap,tap,onRidge);
            Piece(CityWaterKit.WallService,foot,rotation);
        }

        private void RegisterService(string id,Vector3 inlet,Vector3 drain,int tap,int waterTap,bool onRidge)
        {
            if (!buildingIds.Add(id)) throw new InvalidOperationException("Duplicate water service: "+id);
            services.Add(new CityWaterService(id,inlet,drain,tap,waterTap,onRidge));
        }

        private void SurveyRetainingSupply(CityLayout layout)
        {
            foreach (var wall in CityHillsideSiteDetails.ForLayout(layout))
            {
                if (wall.Part != CityHillsideSiteDetails.Kind.RetainingEnd) continue;
                Vector3 Point(float x,float y) => wall.Origin+wall.Rotation*new Vector3(x,y,.365f);
                var toward = wall.Rotation*Vector3.back;
                Vector3[] course = { Point(-2.58f,-.50f),Point(-2.58f,.76f),Point(-.26f,.76f),
                    Point(-.26f,.39f),Point(2.48f,.39f),Point(2.48f,-.5f) };
                int first=TapStreet(course[0],true), last=TapStreet(course[course.Length-1],true);
                int end=ConnectCourse(first,course,true);
                Edge(end,last,drain:false,crossing:true,onRidge:true);
                DressCourse(course,.76f,new[]{toward,toward,toward,toward,toward});
            }
            SurveyStairSupply(layout);
        }

        private void Vent(Vector3 foot, Vector3 outward)
        {
            var rotation = Quaternion.LookRotation(outward);
            Piece(CityWaterKit.VentGrille,foot + rotation*new Vector3(.76f,.42f,.045f),rotation);
        }

        private void ConnectSourceAndSea(CityLayout layout, CitySeacoastPlan coast, CityCoastalFacility works)
        {
            foreach (var poi in layout.DistrictPointsOfInterest)
                if (poi.Kind == CityDistrictPointOfInterestKind.OldTownWaterworksCourt)
                {
                    Quaternion basis = Quaternion.LookRotation(CityDistrictPointOfInterestWorldBuilder.ResolveForward(poi));
                    float scale = CityDistrictPointOfInterestWorldBuilder.ResolveHorizontalScale(poi.PublicBounds);
                    var standpipe = poi.Center + basis*new Vector3(.55f*scale,-.35f,.40f*scale);
                    var tee = standpipe - basis*Vector3.forward*2.5f;
                    var boundary = new Vector3(tee.x,tee.y-2f,layout.WorldXZBounds.yMin);
                    Pipe(boundary,tee,CityWaterPipeKind.SpringFeed);
                    Pipe(tee,standpipe,CityWaterPipeKind.SpringFeed);
                    Pipe(tee,junctions[station].Water,CityWaterPipeKind.Supply);
                }
            var frame = coast.Frame;
            float z = frame.WaterlineZ - OutfallBeachReach;
            float x = OutfallSiteX(coast, z);
            float sand = CitySeacoastPlanner.SampleShoreWalkTop(layout,frame,x,z);
            Outfall = new Vector3(x,sand-.09f,z);
            outfallSandAtEdge = CitySeacoastPlanner.SampleShoreWalkTop(layout,frame,x,frame.WaterlineZ-.01f);
            outfallSeaTop = frame.SeaTopY;
            Piece(CityWaterKit.OutfallHead,Outfall,Quaternion.identity);
            Piece(CityWaterKit.OutfallStream,Outfall,Quaternion.identity);
            // The sewer chamber behind the works discharges through a pipe below
            // the riverbed before following the east coastal road to the beach.
            Vector3 chamber = works.Position - works.Rotation*Vector3.forward*(works.Size.z*.5f)
                - Vector3.up*2.6f;
            discharge.Add(chamber);
            float coastRoadZ = frame.BeachRowBounds.yMin - 2f;
            float boreY = Mathf.Min(frame.SeaTopY,frame.MouthWaterY)-4.5f;
            discharge.Add(new Vector3(chamber.x,boreY,coastRoadZ));
            discharge.Add(new Vector3(x,boreY,coastRoadZ));
            discharge.Add(Outfall+new Vector3(0,-.85f,-4.6f));
            discharge.Add(Outfall+new Vector3(0,.45f,-2.2f));
            discharge.Add(Outfall+new Vector3(0,.80f,-1f));
            discharge.Add(Outfall+new Vector3(0,.86f,.12f));
            Vector3 previous = junctions[station].Sewer;
            foreach (var next in discharge) { Pipe(previous,next,CityWaterPipeKind.CoastalDischarge); previous = next; }
        }

        private const float OutfallBeachReach = 1.35f;
        private float outfallSandAtEdge, outfallSeaTop;

        internal float ReceivingBedOffset(float downstream)
        {
            float edge = outfallSandAtEdge - Outfall.y;
            float sand = Mathf.Lerp(.09f, edge, downstream / OutfallBeachReach);
            float offshore = Mathf.SmoothStep(0f, 1f,
                (downstream - OutfallBeachReach) / CitySeaResources.SwashSeaReach);
            return Mathf.Lerp(sand, outfallSeaTop - Outfall.y, offshore);
        }

        private static float OutfallSiteX(CitySeacoastPlan coast, float z)
        {
            // Keep the full exposed barrel and the mixing zone clear of beach dressing.
            float east = coast.Frame.EastZone.xMax - 6f;
            float bestX = east, bestGap = float.NegativeInfinity;
            for (float x = east; x >= Mathf.Max(east-28f, coast.Frame.EastZone.xMin+2f); x -= 2f)
            {
                float gap = float.PositiveInfinity;
                foreach (var part in coast.Parts)
                {
                    Vector3 right = part.Rotation * Vector3.right * part.Size.x * .5f;
                    Vector3 up = part.Rotation * Vector3.up * part.Size.y * .5f;
                    Vector3 forward = part.Rotation * Vector3.forward * part.Size.z * .5f;
                    float halfZ = Mathf.Abs(right.z) + Mathf.Abs(up.z) + Mathf.Abs(forward.z);
                    if (part.Center.z+halfZ < z-4.7f || part.Center.z-halfZ > z+5.2f) continue;
                    float halfX = Mathf.Abs(right.x) + Mathf.Abs(up.x) + Mathf.Abs(forward.x);
                    gap = Mathf.Min(gap, Mathf.Abs(part.Center.x-x)-halfX);
                }
                if (gap >= 2.25f) return x;
                if (gap > bestGap) { bestGap = gap; bestX = x; }
            }
            return bestX;
        }
    }
}

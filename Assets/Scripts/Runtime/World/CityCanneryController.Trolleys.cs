using System.Collections.Generic;
using UnityEngine;

namespace BarPromenade
{
    public enum CityCanneryTrolleyPhase { Parked, Approaching, Fetching, Handling, Returning, Leaving }

    public sealed partial class CityCanneryController
    {
        private const float TrolleyHandleArrival = 12f;
        private const float TrolleyGripDuration = .8f;
        private const float TrolleyTravelStart = (float)CityFishSupplyCycle.TrolleyReadyDuration;
        private const float PortBackOutEnd=.18f+1.8f/(float)CityFishSupplyCycle.TransferUnitDuration;
        private const float PortLoadTurnEnd=PortBackOutEnd+2.4f/(float)CityFishSupplyCycle.TransferUnitDuration;
        private readonly Transform[] siteTrolleys = new Transform[3];
        private readonly Transform[] siteForks = new Transform[3];
        private readonly Transform[] siteLeftHands = new Transform[3];
        private readonly Transform[] siteRightHands = new Transform[3];
        private readonly Vector3[] siteForkDocks = new Vector3[3];
        private readonly Vector3[] trolleyParkingPositions = new Vector3[3];
        private readonly Quaternion[] trolleyParkingRotations = new Quaternion[3];
        private readonly Vector3[] emptyTrolleyPath = new Vector3[4];
        private readonly Vector3[] firstTrolleyFetchPath = new Vector3[10];
        private readonly Vector3[] driverTrolleyApproach = new Vector3[8];
        private readonly Vector3[] factoryJackPoints = new Vector3[8];
        private readonly Quaternion[] factoryJackRotations = new Quaternion[8];
        private enum FactoryJackRoute { Loaded, FirstFetch, RepeatFetch, Return }
        private RuntimeOrientedBox[] deliverySidewalks;
        private CityLayout deliveryLayout;
        private float trolleyHandWeight;
        private float TransferEdge => (float)CityFishSupplyCycle.TransferEdgeDuration;
        private int TrolleySite => Snapshot.Stage == CityFishSupplyStage.LoadFish ? 0 :
            Snapshot.Stage == CityFishSupplyStage.UnloadShop ? 2 : 1;
        public Transform PortTrolley => siteTrolleys[0];
        public Transform FactoryTrolley => siteTrolleys[1];
        public Transform ShopTrolley => siteTrolleys[2];
        public Transform ActiveTrolley => Snapshot.IsTransfer ? trolley : null;
        public Vector3 PortTrolleyParkingPosition => trolleyParkingPositions[0];
        public Vector3 FactoryTrolleyParkingPosition => trolleyParkingPositions[1];
        public Vector3 ShopTrolleyParkingPosition => trolleyParkingPositions[2];
        public Vector3 LowLiftContact => LowLift;
        public Vector3 PortTrolleyQueuePosition => port.Plan.World(new Vector3(5.1f, CityPortPlan.DeckHeight, -12.35f));
        private Vector3 PortTrolleyApronPosition => port.Plan.World(new Vector3(5.1f, CityPortPlan.DeckHeight, -14f));
        private Quaternion PortQueueRotation => Quaternion.LookRotation(port.Plan.World(new Vector3(4.3f,
            CityPortPlan.DeckHeight,-14f))-PortTrolleyQueuePosition);
        public bool DriverWaitingForDockWorker => Snapshot.Stage == CityFishSupplyStage.LoadFish &&
            Snapshot.WaitingForDockWorker && Snapshot.Seconds >= CityFishSupplyCycle.TrolleyQueueArrivalDuration;
        public CityCanneryTrolleyPhase TrolleyPhase { get; private set; }

        private void CacheDeliverySidewalks(CityLayout layout)
        {
            var nearby = new List<RuntimeOrientedBox>();
            foreach (RuntimeOrientedBox sidewalk in CityStreetSurfacePlanner.Create(layout).SidewalkGeometry)
            {
                float reach = sidewalk.Size.magnitude * .5f + 20f;
                if ((sidewalk.Center - Route.ShopDoorPoint).sqrMagnitude <= reach * reach) nearby.Add(sidewalk);
            }
            deliverySidewalks = nearby.ToArray();
        }

        private void CreateLocalTrolleys()
        {
            // Beside the empty tare at the western edge of the canopy, clear
            // of its posts, the stack and the workers' southern return lanes.
            trolleyParkingPositions[0] = port.Plan.World(new Vector3(5.1f, CityPortPlan.DeckHeight, -8.4f));
            trolleyParkingPositions[1] = Plan.World(new Vector3(1.8f, CityCanneryPlan.YardTop, -6.4f));
            trolleyParkingPositions[2] = Route.ShopDoorPoint + ShopInward * 2.2f -
                (Route.ShopPose.Rotation * Vector3.forward) * 1.4f;
            trolleyParkingRotations[0] = Quaternion.LookRotation(PortTrolleyQueuePosition-trolleyParkingPositions[0]);
            trolleyParkingRotations[1] = Quaternion.LookRotation(Plan.Right);
            trolleyParkingRotations[2] = Quaternion.LookRotation(Route.ShopPose.Rotation * Vector3.forward);
            string[] names = { "Port warehouse trolley", "Cannery receiving trolley", "Shop receiving trolley" };
            for (int i = 0; i < siteTrolleys.Length; i++)
            {
                trolleyParkingPositions[i].y = HandlingGroundHeight(trolleyParkingPositions[i]);
                Transform cart = CityCanneryAssetProvider.Create("Trolley", transform).transform;
                cart.name = names[i];
                cart.SetPositionAndRotation(trolleyParkingPositions[i], trolleyParkingRotations[i]);
                siteTrolleys[i] = cart;
                siteForks[i] = Require(cart, "MOVE_Forks");
                siteForkDocks[i] = cart.InverseTransformPoint(siteForks[i].position);
                siteLeftHands[i] = Require(cart, "ANCHOR_TrolleyHandleLeft");
                siteRightHands[i] = Require(cart, "ANCHOR_TrolleyHandleRight");
            }
            SelectLocalTrolley();
        }

        private void SelectLocalTrolley()
        {
            int site = TrolleySite;
            trolley = siteTrolleys[site]; forks = siteForks[site]; forksDock = siteForkDocks[site];
            trolleyLeftHand = siteLeftHands[site]; trolleyRightHand = siteRightHands[site];
        }

        private void ApplyLocalTrolleys()
        {
            SelectLocalTrolley();
            TrolleyPhase = CityCanneryTrolleyPhase.Parked;
            trolleyHandWeight = 0f;
            for (int i = 0; i < siteTrolleys.Length; i++)
            {
                Transform cart = siteTrolleys[i];
                bool visible = i == 0 ? port.ShorePresentationActive : i == 1 ? FactoryPresentationActive :
                    WorldDistancePresentation.ShouldShow(hero, new Bounds(trolleyParkingPositions[i], Vector3.one * 20f),
                        cart.gameObject.activeSelf, ForcePresentation);
                if (Snapshot.IsTransfer && i == TrolleySite) visible |= TruckPresentationActive;
                cart.gameObject.SetActive(visible);
                cart.SetPositionAndRotation(trolleyParkingPositions[i], trolleyParkingRotations[i]);
                siteForks[i].position = cart.TransformPoint(siteForkDocks[i]);
            }
        }

        private void ApplyTrolleyEdge(float seconds, bool returning)
        {
            SampleTrolleyEdgePose(seconds,returning,out Vector3 cart,out Quaternion rotation);
            trolley.SetPositionAndRotation(cart,rotation);
            forks.position=trolley.TransformPoint(forksDock);
            trolleyOperatorPosition=cart-trolley.forward*CityCanneryTruckDimensions.GroundOperatorOffset;
            trolleyOperatorPosition.y=HandlingGroundHeight(trolleyOperatorPosition);
            trolleyOperatorRotation=Quaternion.LookRotation(Vector3.ProjectOnPlane(trolley.forward,Vector3.up));
            SampleTrolleyEdgePose(seconds-.01f,returning,out Vector3 before,out Quaternion beforeRotation);
            SampleTrolleyEdgePose(seconds+.01f,returning,out Vector3 after,out Quaternion afterRotation);
            before-=beforeRotation*Vector3.forward*CityCanneryTruckDimensions.GroundOperatorOffset;
            after-=afterRotation*Vector3.forward*CityCanneryTruckDimensions.GroundOperatorOffset;
            ApplyTrolleyMotion((after-before)*(returning ? -50f : 50f));
            trolleyHandWeight=Ease((seconds-TrolleyHandleArrival)/TrolleyGripDuration);
            TrolleyPhase=seconds<TrolleyTravelStart ?
                (returning ? CityCanneryTrolleyPhase.Leaving : CityCanneryTrolleyPhase.Approaching) :
                (returning ? CityCanneryTrolleyPhase.Returning : CityCanneryTrolleyPhase.Fetching);
        }

        private void SampleTrolleyEdgePose(float seconds,bool returning,out Vector3 cart,out Quaternion rotation)
        {
            if (Loading && !returning)
            {
                // Bring the jack to the entrance even if the dock worker
                // owns the aisle; waiting is at that physical queue point.
                float firstProgress = Snapshot.Stage == CityFishSupplyStage.LoadFinished ?
                    (seconds-TrolleyTravelStart)/(TransferEdge+(float)CityFishSupplyCycle.TransferUnitDuration*.18f-TrolleyTravelStart) : 0f;
                if (Snapshot.Stage == CityFishSupplyStage.LoadFish)
                {
                    float approach=QueueApproachProgress(seconds);
                    rotation=Quaternion.Slerp(trolleyParkingRotations[0],PortQueueRotation,Ease(approach));
                    cart=QueueApproachPosition(seconds);
                }
                else
                {
                    rotation=FirstTrolleyFetchRotation(firstProgress);
                    cart=FirstTrolleyFetchPosition(firstProgress);
                }
                return;
            }
            float drive = Mathf.InverseLerp(TrolleyTravelStart, TransferEdge, seconds);
            if (Snapshot.Stage == CityFishSupplyStage.LoadFinished)
            {
                SampleFactoryJackGround(0, Ease(drive), FactoryJackRoute.Return, out cart, out rotation);
                return;
            }
            cart = EmptyTrolleyPosition(Ease(drive));
            // Steer across each corner continuously; the operator must not
            // jump a handle-length when the polyline changes segment.
            Vector3 direction = EmptyTrolleyPosition(Mathf.Min(1f, Ease(drive) + .05f)) -
                EmptyTrolleyPosition(Mathf.Max(0f, Ease(drive) - .05f)); direction.y = 0;
            rotation = direction.sqrMagnitude > .000001f ? Quaternion.LookRotation(direction) :
                trolleyParkingRotations[TrolleySite];
            rotation = Quaternion.Slerp(trolleyParkingRotations[TrolleySite], rotation, Ease(drive / .15f));
            rotation = Quaternion.Slerp(rotation, Truck.rotation, Ease((drive - .85f) / .15f));
        }

        private float FirstFetchProgress(float transferProgress)
        {
            if (Snapshot.Stage == CityFishSupplyStage.LoadFish) return transferProgress/.18f;
            float lead = TransferEdge-TrolleyTravelStart;
            return (lead+transferProgress*(float)CityFishSupplyCycle.TransferUnitDuration) /
                (lead+.18f*(float)CityFishSupplyCycle.TransferUnitDuration);
        }

        private Vector3 FirstTrolleyFetchPosition(float progress)
            => TrolleyFetchPosition(0, progress);

        private Vector3 TrolleyFetchPosition(int unit, float progress)
        {
            if (Snapshot.Stage == CityFishSupplyStage.LoadFinished)
                return FactoryTrolleyFetchPosition(unit, progress, true);
            // Reuse the same door, aisle and shelf bends as the loaded trip,
            // replacing only the unnecessary empty detour across the lift.
            GroundPath(GroundCargo(unit), trolleyParkingPositions[TrolleySite], 0f);
            firstTrolleyFetchPath[0] = Snapshot.Stage==CityFishSupplyStage.LoadFish ? PortTrolleyQueuePosition : trolleyParkingPositions[TrolleySite];
            for (int i = 1; i < firstTrolleyFetchPath.Length; i++)
                firstTrolleyFetchPath[i] = groundPathPoints[9-i];
            // Enter diagonally from the queue alongside the lift. Going to
            // the apron first would put the pushing operator on the deck.
            if (Snapshot.Stage==CityFishSupplyStage.LoadFish) firstTrolleyFetchPath[1]=firstTrolleyFetchPath[2];
            Vector3 point = RoundLowerPortTurn(Along(firstTrolleyFetchPath, Ease(progress)),GroundCargo(unit));
            point.y = HandlingGroundHeight(point);
            return point;
        }

        private Quaternion FirstTrolleyFetchRotation(float progress)
            => TrolleyFetchRotation(0, progress);

        private Quaternion TrolleyFetchRotation(int unit, float progress)
        {
            if (Snapshot.Stage == CityFishSupplyStage.LoadFinished)
            {
                SampleFactoryJackGround(unit, progress, FactoryJackRoute.FirstFetch, out _, out Quaternion readyRotation);
                return readyRotation;
            }
            float p = Mathf.Clamp01(progress);
            Vector3 direction = TrolleyFetchPosition(unit,p-.035f)-TrolleyFetchPosition(unit,p+.035f);
            if (Snapshot.Stage==CityFishSupplyStage.LoadFish) direction=-direction;
            direction.y = 0f;
            Quaternion rotation = direction.sqrMagnitude > .000001f ? Quaternion.LookRotation(direction) :
                trolleyParkingRotations[TrolleySite];
            return Quaternion.Slerp(Snapshot.Stage==CityFishSupplyStage.LoadFish ? PortQueueRotation :
                trolleyParkingRotations[TrolleySite], rotation, Ease(p/.15f));
        }

        private float QueueApproachProgress(float seconds) => Mathf.InverseLerp(TrolleyTravelStart,
            (float)CityFishSupplyCycle.TrolleyQueueArrivalDuration, seconds);

        private Vector3 QueueApproachPosition(float seconds) => Vector3.Lerp(trolleyParkingPositions[0],
            PortTrolleyQueuePosition,Ease(QueueApproachProgress(seconds)));

        private Vector3 PortQueueReturnPosition(float progress)
        {
            if (progress<.25f) return Vector3.Lerp(LowLift,PortTrolleyApronPosition,Ease(progress/.25f));
            if (progress<.45f) return PortTrolleyApronPosition;
            if (progress<.72f) return Vector3.Lerp(PortTrolleyApronPosition,PortTrolleyQueuePosition,Ease((progress-.45f)/.27f));
            return PortTrolleyQueuePosition;
        }

        private Quaternion PortQueueReturnRotation(float progress)
        {
            Quaternion alongApron=Quaternion.LookRotation(PortTrolleyQueuePosition-PortTrolleyApronPosition);
            if(progress<.25f) return Truck.rotation;
            if(progress<.45f) return Quaternion.Slerp(Truck.rotation,alongApron,Ease((progress-.25f)/.2f));
            if(progress<.72f) return alongApron;
            return Quaternion.Slerp(alongApron,PortQueueRotation,Ease((progress-.72f)/.28f));
        }

        private Vector3 PortLoadedGroundPosition(int unit,float time)
        {
            SamplePortLoadedGround(unit,time,out Vector3 point,out _);
            return point;
        }

        private void SamplePortLoadedGround(int unit,float time,out Vector3 point,out Quaternion rotation)
        {
            Vector3 store=GroundCargo(unit);
            Vector3 extracted=store+Vector3.left*2f;
            if(time<PortBackOutEnd)
            {
                point=Vector3.Lerp(store,extracted,Ease(Mathf.InverseLerp(.18f,PortBackOutEnd,time)));
                rotation=Quaternion.LookRotation(Vector3.right);
                return;
            }
            if(time<PortLoadTurnEnd)
            {
                float progress=Ease(Mathf.InverseLerp(PortBackOutEnd,PortLoadTurnEnd,time));
                // Move the whole pallet clear of the baffle while turning.
                // The upper slot skirts south of it; the lowest slot moves
                // north early enough for its corner to clear the rear wall.
                float sideways=unit==0 ? -.4f*Mathf.Sin(Mathf.PI*progress)+.2f*progress : .6f*progress;
                point=extracted+new Vector3(-2.45f*progress,0f,sideways);
                rotation=Quaternion.LookRotation(new Vector3(Mathf.Cos(Mathf.PI*progress),0f,-Mathf.Sin(Mathf.PI*progress)));
                return;
            }
            float endZ=store.z+(unit==0 ? .2f : .6f);
            float aisleZ=port.Plan.Origin.z-14f;
            float radius=(aisleZ-endZ)*.5f;
            Vector3 centre=new Vector3(extracted.x-2.45f,store.y,endZ+radius);
            Vector3 arcEnd=new Vector3(centre.x,store.y,aisleZ);
            Vector3 low=LowLift;
            // Parameterize the bend by the operator's longer path. His body
            // follows the handle continuously instead of jumping a handle
            // length when a polyline changes tangent.
            float operatorRadius=Mathf.Sqrt(radius*radius+CityCanneryTruckDimensions.GroundOperatorOffset*
                CityCanneryTruckDimensions.GroundOperatorOffset);
            float turnLength=Mathf.PI*operatorRadius;
            float straightLength=Vector3.Distance(arcEnd,low);
            float distance=Ease(Mathf.InverseLerp(PortLoadTurnEnd,.35f,time))*(turnLength+straightLength);
            if(distance<turnLength)
            {
                float angle=distance/operatorRadius;
                point=centre+new Vector3(-radius*Mathf.Sin(angle),0f,-radius*Mathf.Cos(angle));
                rotation=Quaternion.LookRotation(new Vector3(-Mathf.Cos(angle),0f,Mathf.Sin(angle)));
                return;
            }
            float straight=distance-turnLength;
            point=Vector3.Lerp(arcEnd,low,straight/straightLength);
            float rise=low.y-HandlingGroundHeight(low);
            point.y=HandlingGroundHeight(point)+rise*Ease(1f-(straightLength-straight)/.6f);
            rotation=Quaternion.LookRotation(Vector3.right);
        }

        private Quaternion PortHandlingRotation(int unit,float time)
        {
            if(time<.18f) return TrolleyFetchRotation(unit,time/.18f);
            if(time<.35f)
            {
                SamplePortLoadedGround(unit,time,out _,out Quaternion rotation);
                return rotation;
            }
            if(time>=.85f && unit<CityFishSupplyCycle.HandlingUnits-1) return PortQueueReturnRotation((time-.85f)/.15f);
            return Truck.rotation;
        }

        private Vector3 FactoryTrolleyFetchPosition(int unit, float progress, bool fromStand)
        {
            SampleFactoryJackGround(unit, progress,
                fromStand ? FactoryJackRoute.FirstFetch : FactoryJackRoute.RepeatFetch, out Vector3 point, out _);
            return point;
        }

        private Quaternion FactoryHandlingRotation(int unit, float time)
        {
            if (time >= .35f) return Truck.rotation;
            bool fetching = time < .18f;
            float progress = fetching ? unit == 0 ? FirstFetchProgress(time) : time/.18f : (time-.18f)/.17f;
            FactoryJackRoute route = fetching ? unit == 0 ? FactoryJackRoute.FirstFetch : FactoryJackRoute.RepeatFetch :
                FactoryJackRoute.Loaded;
            SampleFactoryJackGround(unit, progress, route, out _, out Quaternion rotation);
            return rotation;
        }

        // The support's fork tunnels face the east approach. Its authored
        // orientation remains continuous when the jack starts pulling west-facing.
        private Quaternion FactoryShippingSupportRotation(int unit, float time) =>
            time < .18f ? Plan.Rotation * Quaternion.Euler(0f, 90f, 0f) :
            (time < .62f ? FactoryHandlingRotation(unit, time) : Truck.rotation) * Quaternion.Euler(0f, 180f, 0f);

        private void SampleFactoryJackGround(int unit, float progress, FactoryJackRoute route,
            out Vector3 point, out Quaternion rotation)
        {
            Vector3 store = GroundCargo(unit);
            Vector3 local = Plan.Local(store);
            // Side entry keeps the operator east of the pallet, outside the
            // facade. The x=1.9 lane leaves 0.1 m between the moving support
            // and the remaining east-facing supports, clear of people at x=2.75.
            Vector3 extracted = Plan.World(new Vector3(1.9f, CityCanneryPlan.YardTop, local.z));
            Vector3 corner = Plan.World(new Vector3(1.9f, CityCanneryPlan.YardTop, -6.15f));
            Vector3 low = LowLift;
            Vector3 across = Plan.World(new Vector3(Plan.Local(low).x, CityCanneryPlan.YardTop, -6.15f));
            Quaternion west = Quaternion.LookRotation(-Plan.Right);
            Quaternion south = Quaternion.LookRotation(-Plan.Forward);
            Quaternion east = Quaternion.LookRotation(Plan.Right);
            int count = 0;
            if (route == FactoryJackRoute.Loaded)
            {
                Add(store, west); Add(extracted, west); Add(extracted, south); Add(corner, south);
                Add(corner, east); Add(across, east); Add(across, Truck.rotation); Add(low, Truck.rotation);
            }
            else if (route == FactoryJackRoute.FirstFetch)
            {
                Add(trolleyParkingPositions[1], trolleyParkingRotations[1]); Add(corner, east);
                Add(corner, south); Add(extracted, south); Add(extracted, west); Add(store, west);
            }
            else if (route == FactoryJackRoute.RepeatFetch)
            {
                Add(low, Truck.rotation); Add(across, Truck.rotation); Add(across, east); Add(corner, east);
                Add(corner, south); Add(extracted, south); Add(extracted, west); Add(store, west);
            }
            else
            {
                // Sampled stand-to-lift; the returning edge traverses it in
                // reverse, through the open south yard rather than the hall.
                Add(trolleyParkingPositions[1], trolleyParkingRotations[1]); Add(corner, east);
                Add(across, east); Add(across, Truck.rotation); Add(low, Truck.rotation);
            }
            float total = 0f;
            for (int i = 1; i < count; i++) total += Length(i);
            float remaining = Mathf.Clamp01(progress) * total;
            point = factoryJackPoints[count - 1];
            rotation = factoryJackRotations[count - 1];
            for (int i = 1; i < count; i++)
            {
                float length = Length(i);
                if (remaining <= length || i == count - 1)
                {
                    float phase = Ease(length > .0001f ? remaining / length : 1f);
                    point = Vector3.Lerp(factoryJackPoints[i - 1], factoryJackPoints[i], phase);
                    rotation = Quaternion.Slerp(factoryJackRotations[i - 1], factoryJackRotations[i], phase);
                    break;
                }
                remaining -= length;
            }
            Vector3 liftDelta = point - low; liftDelta.y = 0f;
            float rise = low.y - HandlingGroundHeight(low);
            point.y = HandlingGroundHeight(point) + rise * Ease(1f - liftDelta.magnitude / .6f);

            void Add(Vector3 position, Quaternion facing)
            {
                factoryJackPoints[count] = position; factoryJackRotations[count++] = facing;
            }
            float Length(int index) => Vector3.Distance(factoryJackPoints[index - 1], factoryJackPoints[index]) +
                Quaternion.Angle(factoryJackRotations[index - 1], factoryJackRotations[index]) * Mathf.Deg2Rad *
                CityCanneryTruckDimensions.GroundOperatorOffset;
        }

        private Vector3 EmptyTrolleyPosition(float progress)
        {
            if (Snapshot.Stage == CityFishSupplyStage.LoadFinished)
            {
                SampleFactoryJackGround(0, progress, FactoryJackRoute.Return, out Vector3 point, out _);
                return point;
            }
            Vector3 park = trolleyParkingPositions[TrolleySite], low = LowLift;
            if (TrolleySite == 2) return GroundPath(park, low, progress);
            Vector3 near = low - Truck.forward * .85f;
            near.y = HandlingGroundHeight(near);
            Vector3 outside = TrolleySite == 0 ? port.Plan.World(new Vector3(5.8f, CityPortPlan.DeckHeight, -14f)) :
                Plan.World(new Vector3(2.2f, CityCanneryPlan.YardTop, -6.1f));
            emptyTrolleyPath[0] = park; emptyTrolleyPath[1] = outside;
            emptyTrolleyPath[2] = near; emptyTrolleyPath[3] = low;
            return Along(emptyTrolleyPath, progress);
        }

        private float HandlingGroundHeight(Vector3 point)
        {
            Vector2 xz = new Vector2(point.x, point.z);
            if (Plan.TrySampleYardTop(point, out float yard))
            {
                Vector3 local = Plan.Local(point);
                return local.x < 0f && local.x >= -8f && Mathf.Abs(local.z) <= 7f ?
                    Plan.Origin.y + CityCanneryPlan.FloorTop : yard;
            }
            if (port.Plan.Access.TrySampleTop(xz, out float access)) return access;
            if (port.Plan.LandBounds.Contains(xz)) return port.Plan.QuayTopY;
            // SidewalkTop is only a height role in ElevationPlan; it does not
            // delimit the pavement. Use the same graded boxes as the actual
            // sidewalk mesh, including its finite kerb and intersection cuts.
            float sidewalkTop = float.NegativeInfinity;
            foreach (RuntimeOrientedBox sidewalk in deliverySidewalks)
            {
                Vector3 normal = sidewalk.Rotation * Vector3.up;
                Vector3 top = sidewalk.Center + normal * (sidewalk.Size.y * .5f);
                Vector3 sample = point;
                sample.y = top.y - ((point.x - top.x) * normal.x + (point.z - top.z) * normal.z) / normal.y;
                Vector3 local = Quaternion.Inverse(sidewalk.Rotation) * (sample - sidewalk.Center);
                if (Mathf.Abs(local.x) <= sidewalk.Size.x * .5f + .0001f &&
                    Mathf.Abs(local.z) <= sidewalk.Size.z * .5f + .0001f)
                    sidewalkTop = Mathf.Max(sidewalkTop, sample.y);
            }
            if (!float.IsNegativeInfinity(sidewalkTop)) return sidewalkTop;
            if (deliveryLayout.ElevationPlan.TrySampleSurface(xz, CitySurfaceRole.RoadTop, out float road, out _)) return road;
            return Route.ShopDoorPoint.y;
        }
    }
}

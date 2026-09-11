using UnityEngine;

namespace BarPromenade
{
    public sealed partial class CityCanneryController
    {
        private Vector3 trolleyOperatorPosition;
        private Quaternion trolleyOperatorRotation;
        private float trolleyMotion;
        private float trolleyGaitDirection=1f;
        public bool DriverTrolleyMovingBackward => Snapshot.IsTransfer && trolleyMotion>.02f && trolleyGaitDirection<0f;
        private float currentLiftTravel;
        private bool handlingActive;
        private readonly Vector3[] groundPathPoints = new Vector3[11];
        private readonly Vector3[] trayRoute = new Vector3[5];
        public float TruckSteeringAngle { get; private set; }

        private Vector3 Anchor(string name) => anchors[name].position;
        private Vector3 RawStore(int i) => Anchor("RawStore" + i);
        private Vector3 ReadyStore(int i) => Anchor("ReadyCase" + i);
        // A single east-side row leaves the west aisle and the dock cart's
        // common drop clear, including a full three-unit buffer. The driver
        // stands behind each jack in the free strip along the east wall.
        private Vector3 PortStore(int i) => port.Plan.World(new Vector3(1.65f,
            CityPortPlan.DeckHeight,i==2 ? -18.28f : -16.6f-i*.85f));
        private bool Loading => Snapshot.Stage == CityFishSupplyStage.LoadFish || Snapshot.Stage == CityFishSupplyStage.LoadFinished;
        private Vector3 LowLift => MeasureLowLift();
        private Vector3 HighLift => Truck.TransformPoint(new Vector3(0, 1.22f, CityCanneryTruckDimensions.TailLiftLoadZ));

        private float LiftPivotTravel(Vector3 contact)
        {
            Vector3 pivot = Truck.TransformPoint(liftDock);
            Vector3 normal = Truck.rotation * liftRest * Vector3.up;
            // The platform follows the same axis as its rails. Resolve the
            // contact plane as travel along Truck.up, including road pitch.
            return Vector3.Dot(contact - pivot, normal) / Vector3.Dot(Truck.up, normal);
        }

        private Vector3 GroundCargo(int i)
        {
            if (Snapshot.Stage == CityFishSupplyStage.LoadFish)
                return PortStore(i);
            if (Snapshot.Stage == CityFishSupplyStage.LoadFinished) return ReadyStore(i);
            if (Snapshot.Stage == CityFishSupplyStage.UnloadShop)
                // The shared stock leaves presentation only around the inner
                // return behind the opaque rear wall, after the leaf opens.
                return Route.ShopDoorPoint + ShopInward * 2.2f +
                    (Route.ShopPose.Rotation * Vector3.forward) * 2.1f;
            return RawStore(i);
        }

        private Vector3 GroundPath(Vector3 store, Vector3 liftPoint, float progress)
        {
            Vector3 door = Snapshot.Stage == CityFishSupplyStage.LoadFish
                ? port.Plan.World(new Vector3(4.3f, CityPortPlan.DeckHeight, -14f))
                : Snapshot.Stage == CityFishSupplyStage.UnloadShop ? Route.ShopDoorPoint
                : Anchor(Snapshot.Stage == CityFishSupplyStage.LoadFinished ? "FinishedDoor" : "RawDoor");
            door.y = store.y;
            // A real doorway and the clear yard connect the shelf to the lift.
            Vector3 outside = Snapshot.Stage == CityFishSupplyStage.LoadFish
                ? PortTrolleyApronPosition
                : Snapshot.Stage == CityFishSupplyStage.UnloadShop ? door + Truck.right * 1.2f
                : door + Plan.Right * 1.5f;
            outside.y=HandlingGroundHeight(outside);
            Vector3 bend = store, aisle = store;
            if(Snapshot.Stage==CityFishSupplyStage.LoadFish)
            {
                // The opaque baffle has only one west-side passage. LoadFish
                // gives the first arrival access; the later cart waits outside.
                bend=port.Plan.World(new Vector3(-3.2f,CityPortPlan.DeckHeight,store.z-port.Plan.Origin.z));
                aisle=port.Plan.World(new Vector3(-3.2f,CityPortPlan.DeckHeight,-14.8f));
            }
            else if (Snapshot.Stage == CityFishSupplyStage.UnloadFish)
            {
                Vector3 local = Plan.Local(store);
                bend = Plan.World(new Vector3(local.x, CityCanneryPlan.FloorTop, -4.875f));
                aisle = Plan.World(new Vector3(-4.65f, CityCanneryPlan.FloorTop, -4.875f));
            }
            else if (Snapshot.Stage == CityFishSupplyStage.LoadFinished)
            {
                bend = Plan.World(new Vector3(-2.8f, CityCanneryPlan.FloorTop, 6.35f));
                aisle = Plan.World(new Vector3(-2.8f, CityCanneryPlan.FloorTop, 5f));
            }
            else if (Snapshot.Stage == CityFishSupplyStage.UnloadShop)
                bend = aisle = Route.ShopDoorPoint + ShopInward * 2.2f;
            Vector3 nearLift = liftPoint - Truck.forward * .6f;
            nearLift.y = HandlingGroundHeight(nearLift);
            groundPathPoints[0]=store;
            for (int i=1;i<=5;i++) groundPathPoints[i]=bend;
            if (Snapshot.Stage==CityFishSupplyStage.LoadFish && store.z-port.Plan.Origin.z < -18f)
            {
                // The lowest slot is close to the rear wall. A broad turn
                // moves the jack north before its long handle points south.
                Vector3 centre=new Vector3(port.Plan.Origin.x+.5f,store.y,store.z+3f);
                for (int i=0;i<5;i++)
                {
                    float angle=i*Mathf.PI/8f;
                    groundPathPoints[i+1]=centre+new Vector3(-3f*Mathf.Sin(angle),0f,-3f*Mathf.Cos(angle));
                }
            }
            groundPathPoints[6]=aisle; groundPathPoints[7]=door; groundPathPoints[8]=outside;
            groundPathPoints[9]=nearLift; groundPathPoints[10]=liftPoint;
            Vector3 point = RoundLowerPortTurn(Along(groundPathPoints, progress),store);
            float length = 0f;
            for (int i = 1; i < groundPathPoints.Length; i++)
                length += Vector3.Distance(groundPathPoints[i-1], groundPathPoints[i]);
            float lastLength = Vector3.Distance(nearLift, liftPoint);
            float remaining = (1f - Mathf.Clamp01(progress)) * length;
            float rise = liftPoint.y - HandlingGroundHeight(liftPoint);
            // Follow the actual curb between the doorway and the road. Only
            // the final approach climbs from that surface onto the low lift.
            point.y = HandlingGroundHeight(point) + rise * Ease(1f - remaining / Mathf.Max(lastLength, .001f));
            return point;
        }

        private Vector3 RoundLowerPortTurn(Vector3 point, Vector3 store)
        {
            if (Snapshot.Stage!=CityFishSupplyStage.LoadFish || store.z-port.Plan.Origin.z>=-18f) return point;
            Vector3 centre=new Vector3(port.Plan.Origin.x+.5f,point.y,store.z+3f);
            if (point.x>centre.x || point.z>centre.z) return point;
            // Project the sampled chords back to their authored arc, so the
            // long handle and the following body turn continuously at the wall.
            Vector3 radial=point-centre;
            return radial.sqrMagnitude>.0001f ? centre+radial.normalized*3f : point;
        }

        private static Vector3 Along(Vector3[] points, float progress)
            => AlongRange(points,progress,0,points.Length-1);

        private static Vector3 AlongRange(Vector3[] points, float progress, int first, int last)
        {
            float total = 0;
            for (int i = first+1; i <= last; i++) total += Vector3.Distance(points[i-1], points[i]);
            float left = Mathf.Clamp01(progress) * total;
            for (int i = first+1; i <= last; i++)
            {
                float length = Vector3.Distance(points[i-1], points[i]);
                if (left <= length || i == last) return Vector3.Lerp(points[i-1], points[i], length > .0001f ? left / length : 1);
                left -= length;
            }
            return points[last];
        }

        private Vector3 SampleTransfer(int i, float t, out Vector3 cart, out float liftTravel)
        {
            Vector3 ground = GroundCargo(i), low = LowLift, high = HighLift;
            Vector3 slot = Truck.TransformPoint(cargoSlots[i]);
            liftTravel = LiftPivotTravel(low);
            Vector3 cargo;
            if (Loading)
            {
                if (t < .18f)
                {
                    // The first empty jack comes from its own stand. Only
                    // later fetches start where the previous load left it.
                    cart = Snapshot.Stage==CityFishSupplyStage.LoadFish ? TrolleyFetchPosition(i,t/.18f) :
                        i == 0 ? FirstTrolleyFetchPosition(FirstFetchProgress(t)) :
                        GroundPath(ground, low, 1-Ease(t/.18f));
                    cargo = ground;
                }
                else if (t < .35f)
                {
                    cart=Snapshot.Stage==CityFishSupplyStage.LoadFish ? PortLoadedGroundPosition(i,t) :
                        GroundPath(ground, low, Ease((t-.18f)/.17f));
                    cargo=cart;
                }
                else if (t < .47f) { cart = Blend(low,high,(t-.35f)/.12f); cargo=cart; liftTravel=LiftPivotTravel(cart); }
                else if (t < .62f) { cart = Blend(high,slot,(t-.47f)/.15f); cargo=cart; liftTravel=LiftPivotTravel(high); }
                else if (t < .75f) { cart = Blend(slot,high,(t-.62f)/.13f); cargo=slot; liftTravel=LiftPivotTravel(high); }
                else if (Snapshot.Stage==CityFishSupplyStage.LoadFish && i<fish.Length-1 && t>=.85f)
                { cart=PortQueueReturnPosition((t-.85f)/.15f); cargo=slot; }
                else { cart=Blend(high,low,(t-.75f)/.1f); cargo=slot; liftTravel=LiftPivotTravel(cart); }
            }
            else
            {
                // A short step brings the same worker over the compact lift
                // before it rises; both hands keep the authored jack handle.
                if (t < .04f) { cart=low; cargo=slot; }
                else if (t < .12f) { cart=Blend(low,high,(t-.04f)/.08f); cargo=slot; liftTravel=LiftPivotTravel(cart); }
                else if (t < .28f) { cart=Blend(high,slot,(t-.12f)/.16f); cargo=slot; liftTravel=LiftPivotTravel(high); }
                else if (t < .43f) { cart=Blend(slot,high,(t-.28f)/.15f); cargo=cart; liftTravel=LiftPivotTravel(high); }
                else if (t < .55f) { cart=Blend(high,low,(t-.43f)/.12f); cargo=cart; liftTravel=LiftPivotTravel(cart); }
                else if (t < .75f) { cart=GroundPath(ground,low,1-Ease((t-.55f)/.20f)); cargo=cart; }
                else { cart=GroundPath(ground,low,Ease((t-.75f)/.18f)); cargo=ground; }
            }
            return cargo;
        }

        private float TrolleyOperatorOffset(float t)
        {
            return Mathf.Lerp(CityCanneryTruckDimensions.GroundOperatorOffset,
                CityCanneryTruckDimensions.LiftOperatorOffset,TrolleyOnLift(t));
        }

        private float TrolleyOnLift(float t) => Loading ?
            Ease((t-.30f)/.05f)*(1-Ease((t-.85f)/.15f)) :
            Ease(t/.04f)*(1-Ease((t-.55f)/.05f));

        private int ActiveUnit => Loading ? Mathf.Min(fish.Length - 1, Snapshot.Handled) :
            fish.Length - 1 - Mathf.Min(fish.Length - 1, Snapshot.Handled);

        private Quaternion TransferTrolleyRotation(int unit,float time)
        {
            if(Snapshot.Stage==CityFishSupplyStage.LoadFish) return PortHandlingRotation(unit,time);
            if(Loading && unit==0 && time<.18f) return FirstTrolleyFetchRotation(FirstFetchProgress(time));
            bool ground=Loading ? time<.35f : time>.55f && time<.93f;
            if(!ground) return Truck.rotation;
            SampleTransfer(unit,Mathf.Clamp01(time-.001f),out Vector3 before,out _);
            SampleTransfer(unit,Mathf.Clamp01(time+.001f),out Vector3 after,out _);
            Vector3 direction=after-before; direction.y=0;
            if(Loading ? time<.18f : time<.75f) direction=-direction;
            return direction.sqrMagnitude>.000001f ? Quaternion.LookRotation(direction) : Truck.rotation;
        }

        private void ApplyTrolleyMotion(Vector3 operatorVelocity)
        {
            operatorVelocity.y=0f;
            trolleyMotion=operatorVelocity.magnitude;
            trolleyGaitDirection=Vector3.Dot(operatorVelocity,trolleyOperatorRotation*Vector3.forward)<-.01f ? -1f : 1f;
        }

        private void ApplyTruckParts()
        {
            bool transfer = Snapshot.IsTransfer;
            float seconds = (float)Snapshot.Seconds, remaining = (float)(Snapshot.Duration-Snapshot.Seconds);
            float doorOpen = transfer ? Mathf.Min(Ease(seconds / 4f), Ease(remaining / 4f)) : 0f;
            // Each part clears the next part's sweep before it can move:
            // doors, unfolded platform, descent; the return reverses that order.
            float platformOpen = transfer ? Mathf.Min(Ease((seconds-4f)/3f), Ease((remaining-4f)/3f)) : 0f;
            leftDoor.SetPositionAndRotation(Truck.TransformPoint(leftDoorDock),
                Truck.rotation * Quaternion.AngleAxis(110*doorOpen, Vector3.up) * leftDoorRest);
            rightDoor.SetPositionAndRotation(Truck.TransformPoint(rightDoorDock),
                Truck.rotation * Quaternion.AngleAxis(-110*doorOpen, Vector3.up) * rightDoorRest);
            currentLiftTravel = 0f;
            if (transfer)
            {
                if (Snapshot.Seconds < TransferEdge) currentLiftTravel = Mathf.Lerp(0f, LiftPivotTravel(LowLift), Ease((seconds-7f)/4f));
                else if (Snapshot.Seconds > Snapshot.Duration-TransferEdge)
                    // The cart leaves first. Raise the empty lift only after
                    // the driver has returned the local trolley to its stand.
                    currentLiftTravel = Mathf.Lerp(LiftPivotTravel(LowLift),0f,Ease((TrolleyTravelStart-remaining)/4f));
                else SampleTransfer(ActiveUnit,Snapshot.TransferProgress,out _,out currentLiftTravel);
            }
            Vector3 liftPosition = Truck.TransformPoint(liftDock) + Truck.up * currentLiftTravel;
            lift.SetPositionAndRotation(liftPosition,
                Truck.rotation * Quaternion.AngleAxis(90*(1-platformOpen), Vector3.right) * liftRest);
            ApplyLiftMechanism(currentLiftTravel);
            float spin=(float)(WheelDistance()/.45d*Mathf.Rad2Deg%360d),steer=0;
            if(Snapshot.IsDriving)
            {
                double nextTime=System.Math.Min(WorkingSeconds+.25d,
                    WorkingSeconds+Snapshot.Duration-Snapshot.Seconds-.0001d);
                CityPortTruckPose next=TruckPose(Cycle.Sample(nextTime));
                float distance=Vector3.Distance(Truck.position,next.RearAxle);
                if(distance>.001f)
                {
                    float turn=Vector3.SignedAngle(Truck.forward,next.Rotation*Vector3.forward,Vector3.up)*Mathf.Deg2Rad;
                    bool reverse=Snapshot.Stage==CityFishSupplyStage.FactoryReverse||
                        Snapshot.Stage==CityFishSupplyStage.FactoryReturnReverse||Snapshot.Stage==CityFishSupplyStage.PortReverse;
                    steer=Mathf.Atan(CityCanneryTruckDimensions.Wheelbase*turn/distance*(reverse?-1:1))*Mathf.Rad2Deg;
                }
            }
            TruckSteeringAngle = steer;
            for (int i=0;i<wheels.Length;i++)
                wheels[i].rotation=Truck.rotation*Quaternion.Euler(0,i<2?steer:0,0)*
                    Quaternion.AngleAxis(spin,Vector3.right)*wheelRest[i];
        }

        private double WheelDistance()
        {
            double whole=0,current=0;
            for(int i=0;i<8;i++)
            {
                CityFishSupplyStage stage;
                CityCanneryTruckLeg leg;
                switch(i)
                {
                    case 0: stage=CityFishSupplyStage.FactoryToPort; leg=CityCanneryTruckLeg.FactoryToPort; break;
                    case 1: stage=CityFishSupplyStage.PortArrive; leg=CityCanneryTruckLeg.PortArrive; break;
                    case 2: stage=CityFishSupplyStage.PortReverse; leg=CityCanneryTruckLeg.PortReverse; break;
                    case 3: stage=CityFishSupplyStage.PortToFactory; leg=CityCanneryTruckLeg.PortToFactory; break;
                    case 4: stage=CityFishSupplyStage.FactoryReverse; leg=CityCanneryTruckLeg.FactoryReverse; break;
                    case 5: stage=CityFishSupplyStage.FactoryToShop; leg=CityCanneryTruckLeg.FactoryToShop; break;
                    case 6: stage=CityFishSupplyStage.ShopToFactory; leg=CityCanneryTruckLeg.ShopToFactory; break;
                    default: stage=CityFishSupplyStage.FactoryReturnReverse; leg=CityCanneryTruckLeg.FactoryReverse; break;
                }
                double length=Route.Length(leg)*(leg==CityCanneryTruckLeg.PortReverse||leg==CityCanneryTruckLeg.FactoryReverse?-1:1);
                whole+=length;
                current+=length*(Snapshot.Stage>stage?1:Snapshot.Stage==stage?Snapshot.Progress:0);
            }
            double initialSkip = Route.Length(CityCanneryTruckLeg.FactoryToPort)*Route.InitialFactoryToPortStartProgress;
            double skipped = Snapshot.Batch > 0 || Snapshot.Stage > CityFishSupplyStage.FactoryToPort ? initialSkip :
                Snapshot.Stage == CityFishSupplyStage.FactoryToPort ? initialSkip*Snapshot.Progress : 0d;
            return Snapshot.Batch*whole+current-skipped;
        }

        private void ApplyCargoAndLine()
        {
            double handlingStart = Snapshot.Stage == CityFishSupplyStage.LoadFish ?
                Cycle.TransferUnitStart(Snapshot.Stage, 0, Snapshot.Batch) - Cycle.StageStart(Snapshot.Stage, Snapshot.Batch) : TransferEdge;
            handlingActive = Snapshot.IsTransfer && Snapshot.Seconds >= handlingStart && Snapshot.Seconds < Snapshot.Duration-TransferEdge;
            tray.gameObject.SetActive(FactoryPresentationActive && Production.Stage >= CityCanneryProductionStage.Fill);
            basket.gameObject.SetActive(FactoryPresentationActive && (Production.Stage >= CityCanneryProductionStage.LoadRetort || Production.ReturnSeconds < 12));
            preparationFish.gameObject.SetActive(FactoryPresentationActive && Production.Stage == CityCanneryProductionStage.Prepare);
            if (!FactoryPresentationActive && !TruckPresentationActive && !port.ShorePresentationActive)
            {
                for (int i = 0; i < fish.Length; i++)
                { fish[i].gameObject.SetActive(false); cases[i].gameObject.SetActive(false); }
                return;
            }
            for (int i=0;i<fish.Length;i++)
            {
                int unloadOrdinal = fish.Length - 1 - i;
                bool fishOnTruck = Snapshot.Stage == CityFishSupplyStage.PortToFactory || Snapshot.Stage == CityFishSupplyStage.FactoryReverse ||
                    Snapshot.Stage == CityFishSupplyStage.LoadFish && i < Snapshot.Handled ||
                    Snapshot.Stage == CityFishSupplyStage.UnloadFish && unloadOrdinal >= Snapshot.Handled;
                bool caseOnTruck = Snapshot.Stage > CityFishSupplyStage.LoadFinished ||
                    Snapshot.Stage == CityFishSupplyStage.LoadFinished && i < Snapshot.Handled;
                bool activeTransfer = handlingActive && i == ActiveUnit;
                bool fishGroup = activeTransfer ? TruckPresentationActive : fishOnTruck ? TruckPresentationActive :
                    Snapshot.Stage <= CityFishSupplyStage.LoadFish ? port.ShorePresentationActive : FactoryPresentationActive;
                bool caseGroup = activeTransfer ? TruckPresentationActive : caseOnTruck ? TruckPresentationActive : FactoryPresentationActive;
                // Unload from the rear toward the nose. Production consumes
                // those received units in FIFO order while unloading continues.
                bool hasFish = Snapshot.Stage <= CityFishSupplyStage.WaitForProduction && unloadOrdinal >= Production.PreparedUnits;
                // PortStored is cumulative custody from this ship, unlike
                // PortFish, which decreases as the driver loads earlier units.
                // A still-suspended unit must never also appear in cold storage.
                bool fishVisible = fishGroup && hasFish &&
                    (Snapshot.Stage > CityFishSupplyStage.LoadFish || i < Snapshot.PortStored);
                Vector3 fishPosition = Snapshot.Stage <= CityFishSupplyStage.LoadFish
                    ? PortStore(i)
                    : Snapshot.Stage <= CityFishSupplyStage.FactoryReverse ? Truck.TransformPoint(cargoSlots[i]) : RawStore(i);
                if (Snapshot.Stage == CityFishSupplyStage.LoadFish && i < Snapshot.Handled) fishPosition=Truck.TransformPoint(cargoSlots[i]);
                if (Snapshot.Stage == CityFishSupplyStage.UnloadFish && unloadOrdinal >= Snapshot.Handled) fishPosition=Truck.TransformPoint(cargoSlots[i]);
                bool caseVisible = caseGroup && i < Production.CompletedUnits && Snapshot.Stage <= CityFishSupplyStage.UnloadShop &&
                    (Snapshot.Stage != CityFishSupplyStage.UnloadShop || unloadOrdinal >= Snapshot.Handled);
                Vector3 casePosition = Snapshot.Stage <= CityFishSupplyStage.LoadFinished ? ReadyStore(i) : Truck.TransformPoint(cargoSlots[i]);
                if (Snapshot.Stage == CityFishSupplyStage.LoadFinished && i < Snapshot.Handled) casePosition=Truck.TransformPoint(cargoSlots[i]);
                if (activeTransfer && TruckPresentationActive)
                {
                    Vector3 value=SampleTransfer(i,Snapshot.TransferProgress,out _,out _);
                    if (Snapshot.Stage == CityFishSupplyStage.LoadFish || Snapshot.Stage == CityFishSupplyStage.UnloadFish) fishPosition=value;
                    else casePosition=value;
                }
                fish[i].gameObject.SetActive(fishVisible);
                cases[i].gameObject.SetActive(caseVisible);
                Quaternion fishRotation = Snapshot.Stage <= CityFishSupplyStage.LoadFish ? Quaternion.Euler(0,90,0) : Plan.Rotation;
                if (Snapshot.Stage == CityFishSupplyStage.PortToFactory || Snapshot.Stage == CityFishSupplyStage.FactoryReverse ||
                    Snapshot.Stage == CityFishSupplyStage.LoadFish && i < Snapshot.Handled ||
                    Snapshot.Stage == CityFishSupplyStage.UnloadFish && unloadOrdinal >= Snapshot.Handled) fishRotation = Truck.rotation;
                if (fishVisible) fish[i].SetPositionAndRotation(fishPosition,fishRotation);
                if (caseVisible) cases[i].SetPositionAndRotation(casePosition,Snapshot.Stage < CityFishSupplyStage.LoadFinished ? Plan.Rotation : Truck.rotation);
            }
            if (Snapshot.IsTransfer && TruckPresentationActive)
            {
                if (!handlingActive)
                {
                    bool returning = Snapshot.Seconds >= Snapshot.Duration - TransferEdge;
                    ApplyTrolleyEdge(returning ? (float)(Snapshot.Duration - Snapshot.Seconds) : (float)Snapshot.Seconds, returning);
                }
                else
                {
                    TrolleyPhase = CityCanneryTrolleyPhase.Handling;
                    trolleyHandWeight = 1f;
                    float t = Snapshot.TransferProgress;
                    SampleTransfer(ActiveUnit,t,out Vector3 cart,out _);
                    Quaternion rotation=TransferTrolleyRotation(ActiveUnit,t);
                    trolley.SetPositionAndRotation(cart,rotation);
                    forks.position=trolley.TransformPoint(forksDock);
                    trolleyOperatorPosition=cart-trolley.forward*TrolleyOperatorOffset(t);
                    float onLift = TrolleyOnLift(t);
                    float operatorGround=HandlingGroundHeight(trolleyOperatorPosition);
                    // The jack can already be on the lower apron while its
                    // operator still stands on the higher quay. The blended
                    // lift contact may raise his feet, never lower them through it.
                    trolleyOperatorPosition.y = Mathf.Max(operatorGround,
                        Mathf.Lerp(operatorGround,trolleyOperatorPosition.y,onLift));
                    trolleyOperatorRotation=Quaternion.Slerp(Quaternion.LookRotation(
                        Vector3.ProjectOnPlane(trolley.forward,Vector3.up)),rotation,onLift);
                    trolleyMotion=0;
                    if(!Snapshot.WaitingForPortAccess)
                    {
                        SampleTransfer(ActiveUnit,Mathf.Clamp01(t-.001f),out Vector3 before,out _);
                        SampleTransfer(ActiveUnit,Mathf.Clamp01(t+.001f),out Vector3 after,out _);
                        before-=TransferTrolleyRotation(ActiveUnit,Mathf.Clamp01(t-.001f))*Vector3.forward*
                            TrolleyOperatorOffset(Mathf.Clamp01(t-.001f));
                        after-=TransferTrolleyRotation(ActiveUnit,Mathf.Clamp01(t+.001f))*Vector3.forward*
                            TrolleyOperatorOffset(Mathf.Clamp01(t+.001f));
                        ApplyTrolleyMotion((after-before)/(float)(CityFishSupplyCycle.TransferUnitDuration*.002d));
                    }
                    bool carried=Loading ? t>=.18f&&t<.62f : t>=.28f&&t<.75f;
                    if(carried) forks.position+=Vector3.up*.03f;
                    Transform load=(Snapshot.Stage==CityFishSupplyStage.LoadFish||Snapshot.Stage==CityFishSupplyStage.UnloadFish)
                        ? fish[ActiveUnit] : cases[ActiveUnit];
                    if(carried) load.SetPositionAndRotation(cart+Vector3.up*.03f,rotation);
                }
            }
            if (!FactoryPresentationActive) return;
            seamer.position=factory.TransformPoint(seamerDock)-Plan.Rotation*Vector3.up*
                (Production.Stage == CityCanneryProductionStage.Seal ? .1f*(.5f+.5f*Mathf.Sin((float)Production.Seconds*5)) : 0);
            preparationFish.gameObject.SetActive(Production.Stage==CityCanneryProductionStage.Prepare);
            float retortOpen=0;
            if(Production.Stage==CityCanneryProductionStage.LoadRetort||Production.Stage==CityCanneryProductionStage.Cool)
                retortOpen=Mathf.Min(Ease((float)Production.Seconds/3),Ease((float)(Production.Duration-Production.Seconds)/3));
            retortDoor.position=factory.TransformPoint(retortDoorDock)+Vector3.up*(1.7f*retortOpen);
            retortDoor.rotation=Plan.Rotation*retortDoorRest;
            tray.gameObject.SetActive(Production.Stage >= CityCanneryProductionStage.Fill && Production.Stage <= CityCanneryProductionStage.Pack);
            basket.gameObject.SetActive(Production.Stage >= CityCanneryProductionStage.LoadRetort || Production.ReturnSeconds < 12);
            Vector3 retort=Plan.World(new Vector3(-5,1.02f,1.66f));
            basket.position=retort+Plan.Forward*(Production.Stage == CityCanneryProductionStage.LoadRetort
                ? 1.68f*(1-Ease(((float)Production.Seconds-10)/15)) : Production.Stage == CityCanneryProductionStage.Cool
                ? 1.68f*Ease(((float)Production.Seconds-3)/15) : Production.Stage==CityCanneryProductionStage.Pack ? 1.68f :
                1.68f*(1-Ease((float)Production.ReturnSeconds/12)));
            retortRam.position=factory.TransformPoint(ramDock)+(basket.position-retort);
            if(Production.Stage==CityCanneryProductionStage.Fill)
                tray.position=Plan.World(new Vector3(Mathf.Lerp(-5.72f,-4.36f,Ease(Production.Progress)),1.19f,-.35f));
            else if(Production.Stage==CityCanneryProductionStage.Seal)
                tray.position=Plan.World(new Vector3(-4.36f,1.19f,-.35f));
            else if(Production.Stage==CityCanneryProductionStage.LoadRetort && Production.Seconds<10)
            {
                trayRoute[0]=Plan.World(new Vector3(-4.36f,1.19f,-.35f));
                trayRoute[1]=Plan.World(new Vector3(-3.1f,1.19f,-.35f));
                trayRoute[2]=Plan.World(new Vector3(-3.1f,1.19f,3.95f));
                trayRoute[3]=Plan.World(new Vector3(-5,1.19f,3.95f));
                trayRoute[4]=Plan.World(new Vector3(-5,1.19f,3.34f));
                tray.position=Along(trayRoute,Ease((float)Production.Seconds/10));
            }
            else if(Production.Stage==CityCanneryProductionStage.Pack)
            {
                trayRoute[0]=Plan.World(new Vector3(-5,1.19f,3.34f));
                trayRoute[1]=Plan.World(new Vector3(-5,1.19f,3.95f));
                trayRoute[2]=Plan.World(new Vector3(-5.75f,1.19f,3.95f));
                trayRoute[3]=trayRoute[4]=Anchor("CoolingLoad");
                tray.position=Along(trayRoute,Ease((float)Production.Seconds/8));
            }
            else tray.position=basket.position+Plan.Rotation*new Vector3(0,.17f,0);
        }
    }
}

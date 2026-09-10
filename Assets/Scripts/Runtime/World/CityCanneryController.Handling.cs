using UnityEngine;

namespace BarPromenade
{
    public sealed partial class CityCanneryController
    {
        private Vector3 trolleyOperatorPosition;
        private Quaternion trolleyOperatorRotation;
        private float trolleyMotion;
        private float currentLiftHeight;
        private bool handlingActive;
        private readonly Vector3[] groundPathPoints = new Vector3[7];
        private readonly Vector3[] trayRoute = new Vector3[5];

        private Vector3 Anchor(string name) => anchors[name].position;
        private Vector3 RawStore(int i) => Anchor("RawStore" + i);
        private Vector3 ReadyStore(int i) => Anchor("ReadyCase" + i);
        private Vector3 PortStore(int i) => port.Plan.World(new Vector3(.6f+(i%2)*1.4f,
            CityPortPlan.DeckHeight,-16.65f-(i/2)*.85f));
        private bool Loading => Snapshot.Stage == CityFishSupplyStage.LoadFish || Snapshot.Stage == CityFishSupplyStage.LoadFinished;
        private float GroundY => Snapshot.Stage == CityFishSupplyStage.LoadFish ? port.Plan.QuayTopY :
            Snapshot.Stage == CityFishSupplyStage.UnloadShop ? Truck.position.y + .1f : Plan.Origin.y + CityCanneryPlan.YardTop;

        private Vector3 LowLift
        {
            get { Vector3 p = Truck.TransformPoint(new Vector3(0, 0, -3.4f)); p.y = GroundY; return p; }
        }
        private Vector3 HighLift => Truck.TransformPoint(new Vector3(0, 1.22f, -3.4f));

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
                ? door + Vector3.right * 1.5f
                : Snapshot.Stage == CityFishSupplyStage.UnloadShop ? door + Truck.right * 1.2f
                : door + Plan.Right * 1.5f;
            outside.y=liftPoint.y;
            Vector3 bend = store, aisle = store;
            if(Snapshot.Stage==CityFishSupplyStage.LoadFish)
            {
                bend=port.Plan.World(new Vector3(-1.5f,CityPortPlan.DeckHeight,store.z-port.Plan.Origin.z));
                aisle=port.Plan.World(new Vector3(-1.5f,CityPortPlan.DeckHeight,-14.8f));
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
            groundPathPoints[0]=store; groundPathPoints[1]=bend; groundPathPoints[2]=aisle;
            groundPathPoints[3]=door; groundPathPoints[4]=outside;
            groundPathPoints[5]=nearLift; groundPathPoints[6]=liftPoint;
            return Along(groundPathPoints, progress);
        }

        private static Vector3 Along(Vector3[] points, float progress)
        {
            float total = 0;
            for (int i = 1; i < points.Length; i++) total += Vector3.Distance(points[i-1], points[i]);
            float left = Mathf.Clamp01(progress) * total;
            for (int i = 1; i < points.Length; i++)
            {
                float length = Vector3.Distance(points[i-1], points[i]);
                if (left <= length || i == points.Length - 1) return Vector3.Lerp(points[i-1], points[i], length > .0001f ? left / length : 1);
                left -= length;
            }
            return points[points.Length - 1];
        }

        private Vector3 SampleTransfer(int i, float t, out Vector3 cart, out float height)
        {
            Vector3 ground = GroundCargo(i), low = LowLift, high = HighLift;
            Vector3 slot = Truck.TransformPoint(cargoSlots[i]);
            height = low.y;
            Vector3 cargo;
            if (Loading)
            {
                if (t < .18f) { cart = GroundPath(ground, low, 1-Ease(t/.18f)); cargo = ground; }
                else if (t < .35f) { cart = GroundPath(ground, low, Ease((t-.18f)/.17f)); cargo = cart; }
                else if (t < .47f) { cart = Blend(low,high,(t-.35f)/.12f); cargo=cart; height=cart.y; }
                else if (t < .62f) { cart = Blend(high,slot,(t-.47f)/.15f); cargo=cart; height=high.y; }
                else if (t < .75f) { cart = Blend(slot,high,(t-.62f)/.13f); cargo=slot; height=high.y; }
                else { cart=Blend(high,low,(t-.75f)/.1f); cargo=slot; height=cart.y; }
            }
            else
            {
                if (t < .12f) { cart=Blend(low,high,t/.12f); cargo=slot; height=cart.y; }
                else if (t < .28f) { cart=Blend(high,slot,(t-.12f)/.16f); cargo=slot; height=high.y; }
                else if (t < .43f) { cart=Blend(slot,high,(t-.28f)/.15f); cargo=cart; height=high.y; }
                else if (t < .55f) { cart=Blend(high,low,(t-.43f)/.12f); cargo=cart; height=cart.y; }
                else if (t < .75f) { cart=GroundPath(ground,low,1-Ease((t-.55f)/.20f)); cargo=cart; }
                else { cart=GroundPath(ground,low,Ease((t-.75f)/.18f)); cargo=ground; }
            }
            return cargo;
        }

        private int ActiveUnit => Loading ? Mathf.Min(fish.Length - 1, Snapshot.Handled) :
            fish.Length - 1 - Mathf.Min(fish.Length - 1, Snapshot.Handled);

        private void ApplyTruckParts()
        {
            bool transfer = Snapshot.IsTransfer;
            float edge = transfer ? Mathf.Min(Ease((float)Snapshot.Seconds / 4),
                Ease((float)(Snapshot.Duration-Snapshot.Seconds)/4)) : 0;
            leftDoor.SetPositionAndRotation(Truck.TransformPoint(leftDoorDock),
                Truck.rotation * Quaternion.AngleAxis(110*edge, Vector3.up) * leftDoorRest);
            rightDoor.SetPositionAndRotation(Truck.TransformPoint(rightDoorDock),
                Truck.rotation * Quaternion.AngleAxis(-110*edge, Vector3.up) * rightDoorRest);
            currentLiftHeight = Truck.TransformPoint(liftDock).y;
            if (transfer)
            {
                if (Snapshot.Seconds < 12) currentLiftHeight = Mathf.Lerp(currentLiftHeight, LowLift.y, Ease(((float)Snapshot.Seconds-4)/4));
                else if (Snapshot.Seconds > Snapshot.Duration-12)
                    currentLiftHeight = Mathf.Lerp(LowLift.y,currentLiftHeight,Ease(((float)(Snapshot.Seconds-Snapshot.Duration)+12)/5));
                else SampleTransfer(ActiveUnit,Snapshot.TransferProgress,out _,out currentLiftHeight);
            }
            Vector3 liftPosition = Truck.TransformPoint(liftDock);
            liftPosition.y = currentLiftHeight;
            lift.SetPositionAndRotation(liftPosition,
                Truck.rotation * Quaternion.AngleAxis(90*(1-edge), Vector3.right) * liftRest);
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
                    bool reverse=Snapshot.Stage==CityFishSupplyStage.FactoryReverse||Snapshot.Stage==CityFishSupplyStage.PortReverse;
                    steer=Mathf.Atan(4.2f*turn/distance*(reverse?-1:1))*Mathf.Rad2Deg;
                }
            }
            for (int i=0;i<wheels.Length;i++)
                wheels[i].rotation=Truck.rotation*Quaternion.Euler(0,i<2?steer:0,0)*
                    Quaternion.AngleAxis(spin,Vector3.right)*wheelRest[i];
        }

        private double WheelDistance()
        {
            double whole=0,current=0;
            for(int i=0;i<6;i++)
            {
                var leg=(CityCanneryTruckLeg)i;
                CityFishSupplyStage stage=leg==CityCanneryTruckLeg.PortArrive?CityFishSupplyStage.PortArrive:
                    leg==CityCanneryTruckLeg.PortReverse?CityFishSupplyStage.PortReverse:
                    leg==CityCanneryTruckLeg.PortToFactory?CityFishSupplyStage.PortToFactory:
                    leg==CityCanneryTruckLeg.FactoryReverse?CityFishSupplyStage.FactoryReverse:
                    leg==CityCanneryTruckLeg.FactoryToShop?CityFishSupplyStage.FactoryToShop:CityFishSupplyStage.ShopToPort;
                double length=Route.Length(leg)*(leg==CityCanneryTruckLeg.PortReverse||leg==CityCanneryTruckLeg.FactoryReverse?-1:1);
                whole+=length;
                current+=length*(Snapshot.Stage>stage?1:Snapshot.Stage==stage?Snapshot.Progress:0);
            }
            return Snapshot.Batch*whole+current;
        }

        private void ApplyCargoAndLine()
        {
            handlingActive = Snapshot.IsTransfer && Snapshot.Seconds >= 12 && Snapshot.Seconds < Snapshot.Duration-12;
            trolley.gameObject.SetActive(Snapshot.IsTransfer && TruckPresentationActive);
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
                bool fishVisible = fishGroup && hasFish && (Snapshot.Stage != CityFishSupplyStage.PortVisit || i < Snapshot.PortFish);
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
                float t = Snapshot.Seconds < 12 ? 0 : Snapshot.Seconds >= Snapshot.Duration-12 ? 1 : Snapshot.TransferProgress;
                SampleTransfer(ActiveUnit,t,out Vector3 cart,out _);
                if (Snapshot.Seconds < 12 || Snapshot.Seconds >= Snapshot.Duration-12) cart=LowLift;
                Quaternion rotation = Truck.rotation;
                bool ground = Loading ? t < .35f : t > .55f && t < .93f;
                if (ground && handlingActive)
                {
                    SampleTransfer(ActiveUnit,Mathf.Clamp01(t-.001f),out Vector3 before,out _);
                    SampleTransfer(ActiveUnit,Mathf.Clamp01(t+.001f),out Vector3 after,out _);
                    Vector3 direction=after-before; direction.y=0;
                    bool backward = Loading ? t < .18f : t < .75f;
                    if(backward) direction=-direction;
                    if(direction.sqrMagnitude>.000001f) rotation=Quaternion.LookRotation(direction);
                }
                trolley.SetPositionAndRotation(cart,rotation);
                forks.position=trolley.TransformPoint(forksDock);
                trolleyOperatorPosition=cart-trolley.forward*1.43f;
                trolleyOperatorRotation=rotation;
                trolleyMotion=0;
                if(handlingActive)
                {
                    SampleTransfer(ActiveUnit,Mathf.Clamp01(t-.001f),out Vector3 before,out _);
                    SampleTransfer(ActiveUnit,Mathf.Clamp01(t+.001f),out Vector3 after,out _);
                    Vector3 travel=after-before; travel.y=0;
                    trolleyMotion=travel.magnitude/(float)((Snapshot.Duration-24)/CityFishSupplyCycle.HandlingUnits*.002d);
                }
                if(handlingActive)
                {
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

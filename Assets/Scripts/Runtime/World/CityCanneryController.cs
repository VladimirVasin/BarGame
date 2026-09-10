using System;
using System.Collections.Generic;
using UnityEngine;

namespace BarPromenade
{
    /// <summary>One truck and one finite production batch in the ordinary City scene.</summary>
    [DefaultExecutionOrder(100)]
    public sealed partial class CityCanneryController : MonoBehaviour
    {
        private CityPortController port;
        private Transform hero;
        private Transform factory, equipment, lift, leftDoor, rightDoor, seamer, retortDoor;
        private readonly Transform[] wheels = new Transform[4];
        private readonly Transform[] fish = new Transform[CityFishSupplyCycle.HandlingUnits];
        private readonly Transform[] cases = new Transform[CityFishSupplyCycle.HandlingUnits];
        private readonly Vector3[] cargoSlots = new Vector3[CityFishSupplyCycle.HandlingUnits];
        private readonly Quaternion[] wheelRest = new Quaternion[4];
        private readonly Dictionary<string, Transform> anchors = new Dictionary<string, Transform>();
        private Quaternion liftRest, leftDoorRest, rightDoorRest;
        private Quaternion retortDoorRest;
        private Vector3 liftDock, leftDoorDock, rightDoorDock, seamerDock, retortDoorDock;
        private Transform tray, basket, trolley, preparationFish, forks, retortRam;
        private Vector3 forksDock, ramDock;
        private readonly Collider[] obstacles = new Collider[64];
        private float movementRate = 1f;
        private float waveTime;
        private bool previousPortAutoAdvance, previousPortSupplyDriven;
        public bool AutoAdvance { get; set; } = true;
        public bool IsBlocked { get; private set; }
        public string LastObstacleName { get; private set; }
        public bool IsInitialized { get; private set; }
        public CityCanneryPlan Plan { get; private set; }
        public CityCanneryTruckRoute Route { get; private set; }
        public CityFishSupplyCycle Cycle { get; private set; }
        public CityCanneryTraffic Traffic { get; private set; }
        public CityFishSupplySnapshot Snapshot { get; private set; }
        public CityCanneryProductionSnapshot Production => Snapshot.Production;
        public Transform Truck { get; private set; }
        public Transform Factory => factory;
        public Transform Equipment => equipment;
        public Transform TailLift => lift;
        public double WorkingSeconds { get; private set; }
        public int WorkerCount => workers == null ? 0 : workers.Length;
        public bool WorkerHandsMatch { get; private set; }

        public static CityCanneryController Build(Transform parent, CityLayout layout,
            CityPortController port, Transform hero)
        {
            CityCanneryPlan plan = CityCanneryPlan.Create(layout);
            if (plan == null || port == null || port.Plan.Access == null) return null;
            var host = new GameObject("Working Fish Cannery");
            host.transform.SetParent(parent, false);
            var result = host.AddComponent<CityCanneryController>();
            result.Initialize(layout, plan, port, hero);
            return result;
        }

        private void Initialize(CityLayout layout, CityCanneryPlan plan, CityPortController source, Transform player)
        {
            Plan = plan; port = source; hero = player; deliveryLayout = layout;
            Route = CityCanneryTruckRoute.Create(layout, plan, port.Plan.Access);
            Cycle = new CityFishSupplyCycle(Travel(CityCanneryTruckLeg.PortToFactory),
                Travel(CityCanneryTruckLeg.FactoryReverse), Travel(CityCanneryTruckLeg.FactoryToShop),
                Travel(CityCanneryTruckLeg.ShopToFactory), Travel(CityCanneryTruckLeg.PortArrive),
                Travel(CityCanneryTruckLeg.PortReverse), Travel(CityCanneryTruckLeg.FactoryToPort),
                Route.InitialFactoryToPortDuration,port.DockWorkerStoreExitAtSeconds,port.TrolleyStoreEntryAtSeconds);
            // The site builder owns the passive shell, including the Home vista.
            // This owner adds only the working parts and the shared delivery vehicle.
            factory = new GameObject("Cannery Process").transform;
            factory.SetParent(transform, false);
            factory.SetPositionAndRotation(plan.Origin, plan.Rotation);
            equipment = CityCanneryAssetProvider.Create("Equipment", factory).transform;
            foreach (Transform part in equipment.GetComponentsInChildren<Transform>(true))
                if (part.name.StartsWith("ANCHOR_", StringComparison.Ordinal)) anchors[part.name.Substring(7)] = part;
            Truck = CityCanneryAssetProvider.Create("Truck", transform).transform;
            Traffic=new CityCanneryTraffic(this,GetComponentInParent<CityGameRoot>()?.Bus);
            CacheDeliverySidewalks(layout);
            CreateLocalTrolleys();
            tray = CityCanneryAssetProvider.Create("CanTray", factory).transform;
            basket = CityCanneryAssetProvider.Create("RetortBasket", factory).transform;
            for (int i = 0; i < fish.Length; i++)
            {
                fish[i] = CityCanneryAssetProvider.Create("Pallet", transform).transform;
                fish[i].name = "Fish handling unit " + i;
                cases[i] = CityCanneryAssetProvider.Create("CartonStack", transform).transform;
                cases[i].name = "Finished handling unit " + i;
                // Fill the nose first; unload in reverse order so later units
                // never need to pass through an already parked pallet.
                int slot = 4 - i / 2 * 2 + i % 2;
                cargoSlots[i] = Truck.InverseTransformPoint(Require(Truck, "ANCHOR_TruckCargo" + slot).position);
            }
            lift = Require(Truck, "MOVE_TailLift");
            leftDoor = Require(Truck, "MOVE_TruckRearDoorLeft");
            rightDoor = Require(Truck, "MOVE_TruckRearDoorRight");
            liftDock = Truck.InverseTransformPoint(lift.position);
            leftDoorDock = Truck.InverseTransformPoint(leftDoor.position);
            rightDoorDock = Truck.InverseTransformPoint(rightDoor.position);
            liftRest = Quaternion.Inverse(Truck.rotation) * lift.rotation;
            CreateLiftMechanism();
            leftDoorRest = Quaternion.Inverse(Truck.rotation) * leftDoor.rotation;
            rightDoorRest = Quaternion.Inverse(Truck.rotation) * rightDoor.rotation;
            string[] wheelNames = { "FL", "FR", "RL", "RR" };
            for (int i = 0; i < 4; i++)
            {
                wheels[i] = Require(Truck, "MOVE_Wheel" + wheelNames[i]);
                wheelRest[i] = Quaternion.Inverse(Truck.rotation) * wheels[i].rotation;
            }
            seamer = Require(equipment, "MOVE_SeamerHead");
            preparationFish = Require(equipment, "MOVE_PreparationFish");
            retortRam=Require(equipment,"MOVE_RetortRam");
            ramDock=factory.InverseTransformPoint(retortRam.position);
            retortDoor = Require(equipment, "MOVE_RetortDoor");
            seamerDock = factory.InverseTransformPoint(seamer.position);
            retortDoorDock = factory.InverseTransformPoint(retortDoor.position);
            retortDoorRest = Quaternion.Inverse(factory.rotation) * retortDoor.rotation;
            CreateWorkers();
            CreateVisualDetails();
            CreateSounds(layout.Seed);
            CreateTruckLights();
            CreateShopReceivingDoor();
            previousPortAutoAdvance = port.AutoAdvance;
            previousPortSupplyDriven = port.IsSupplyDriven;
            port.AutoAdvance = false;
            port.IsSupplyDriven = true;
            CreatePresentation();
            IsInitialized = true;
            ApplyAt(CityFishSupplySession.Advance(false));
        }

        private double Travel(CityCanneryTruckLeg leg)
        {
            bool reverse = leg == CityCanneryTruckLeg.FactoryReverse || leg == CityCanneryTruckLeg.PortReverse;
            return Math.Max(2d, Route.Length(leg) / (reverse ? 1.1d : 3d));
        }

        private void Update()
        {
            if (!IsInitialized) return;
            RefreshPresentation();
            if (!AutoAdvance || !GameSessionState.IsGameTimeRunning || GameTimeScaleRuntime.IsPaused) return;
            if (!CityFishSupplySession.HasStarted &&
                !CityFishSupplySession.TryStart(hero != null && port.Plan.IsAtDocks(hero.position))) return;
            bool trafficClear=Traffic.TryAcquire(Snapshot);
            IsBlocked = !trafficClear || (Snapshot.IsDriving || Snapshot.IsTransfer) && DetectObstacle();
            movementRate = Mathf.MoveTowards(movementRate, IsBlocked ? 0f : 1f, Time.deltaTime * 1.5f);
            // Stop at the sensor boundary; easing is used when setting off again.
            if (IsBlocked) movementRate = 0;
            waveTime = Time.timeSinceLevelLoad;
            ApplyAt(CityFishSupplySession.Advance(IsBlocked ? 0f : Snapshot.IsDriving ? movementRate : 1f));
        }

        public void ApplyAt(double seconds)
        {
            WorkingSeconds = seconds;
            Snapshot = Cycle.Sample(seconds);
            port.ForcePresentation = ForcePresentation;
            port.ApplyAt(Snapshot.PortSeconds, waveTime);
            CityPortTruckPose pose = TruckPose(Snapshot);
            Truck.SetPositionAndRotation(pose.RearAxle, pose.Rotation);
            UpdatePresentationVisibility();
            ApplyPresentation();
        }

        public CityPortTruckPose TruckPose(CityFishSupplySnapshot state)
        {
            switch (state.Stage)
            {
                case CityFishSupplyStage.FactoryToPort: return state.Batch == 0
                    ? Route.SampleInitialFactoryToPort(state.Progress)
                    : Route.Sample(CityCanneryTruckLeg.FactoryToPort, state.Progress);
                case CityFishSupplyStage.PortToFactory: return Route.Sample(CityCanneryTruckLeg.PortToFactory, state.Progress);
                case CityFishSupplyStage.FactoryReturnReverse:
                case CityFishSupplyStage.FactoryReverse: return Route.Sample(CityCanneryTruckLeg.FactoryReverse, state.Progress);
                case CityFishSupplyStage.FactoryToShop: return Route.Sample(CityCanneryTruckLeg.FactoryToShop, state.Progress);
                case CityFishSupplyStage.ShopToFactory: return Route.Sample(CityCanneryTruckLeg.ShopToFactory, state.Progress);
                case CityFishSupplyStage.PortArrive: return Route.Sample(CityCanneryTruckLeg.PortArrive, state.Progress);
                case CityFishSupplyStage.PortReverse: return Route.Sample(CityCanneryTruckLeg.PortReverse, state.Progress);
                case CityFishSupplyStage.UnloadShop: return Route.Sample(CityCanneryTruckLeg.FactoryToShop, 1);
                case CityFishSupplyStage.LoadFish: return Route.PortLoadingPose;
                default: return Route.Sample(CityCanneryTruckLeg.FactoryReverse, 1);
            }
        }

        public bool DetectObstacle()
        {
            if(Snapshot.IsTransfer)
            {
                LastObstacleName=null;
                // The hidden worker/cart pose is intentionally not animated.
                // A hero outside the whole handling volume cannot occupy it.
                if(hero==null||workers==null||!TruckPresentationActive) return false;
                Vector3 worker=workers[4].transform.position;
                Vector3 relative=hero.position-worker;
                bool nearWorker=Mathf.Abs(relative.y)<1.6f&&new Vector2(relative.x,relative.z).sqrMagnitude<.95f*.95f;
                relative=hero.position-trolley.position;
                bool nearCart=Mathf.Abs(relative.y)<1.2f&&new Vector2(relative.x,relative.z).sqrMagnitude<1.1f*1.1f;
                if(nearWorker||nearCart) { LastObstacleName=hero.name; return true; }
                return false;
            }
            // Follow the curved rear-axle path when looking ahead. A straight
            // box beyond the nose would see buildings outside a legal turn.
            double lookAhead = Math.Min(.7d, Math.Max(0,Snapshot.Duration-Snapshot.Seconds-.001d));
            CityPortTruckPose next = TruckPose(Cycle.Sample(WorkingSeconds+lookAhead));
            Vector3 center = next.RearAxle + next.Rotation * CityCanneryTruckDimensions.BodyCenter;
            int count = Physics.OverlapBoxNonAlloc(center, new Vector3(
                CityCanneryTruckDimensions.HalfWidth + .03f,CityCanneryTruckDimensions.BodyHalfExtents.y,
                (CityCanneryTruckDimensions.Front - CityCanneryTruckDimensions.Rear) * .5f + .03f),
                obstacles,next.Rotation,~0,QueryTriggerInteraction.Ignore);
            LastObstacleName = null;
            for (int i = 0; i < count; i++)
            {
                Collider hit = obstacles[i];
                if (hit == null || hit.transform.IsChildOf(transform) || hit.bounds.max.y < Truck.position.y + .45f) continue;
                LastObstacleName = hit.name;
                return true;
            }
            return false;
        }

        private static Transform Require(Transform root, string name)
        {
            foreach (Transform child in root.GetComponentsInChildren<Transform>(true))
                if (child.name == name) return child;
            throw new InvalidOperationException("Cannery model is missing " + name);
        }
        private static float Ease(float value) => Mathf.SmoothStep(0, 1, Mathf.Clamp01(value));
        private static Vector3 Blend(Vector3 a, Vector3 b, float t) => Vector3.Lerp(a, b, Ease(t));

        private void OnDestroy()
        {
            RestorePresentation();
            if (port != null) { port.IsSupplyDriven = previousPortSupplyDriven; port.AutoAdvance = previousPortAutoAdvance; }
            RestoreShopReceivingDoor();
            Traffic?.Release();
            DestroySounds();
        }
    }
}

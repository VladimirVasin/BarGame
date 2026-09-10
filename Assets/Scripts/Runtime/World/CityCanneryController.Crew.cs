using System;
using UnityEngine;

namespace BarPromenade
{
    public sealed partial class CityCanneryController
    {
        private VillageResidentPresentation[] workers;
        private Transform driverPelvis, driverSeat, driverLeftHand, driverRightHand, driverFoot;
        private Transform driverDoor, driverExit, driverRearWalk, trolleyLeftHand, trolleyRightHand;
        private Transform driverSteeringShoulder, driverMouth;
        private readonly Transform[] driverThighs = new Transform[2];
        private readonly Transform[] driverShins = new Transform[2];
        private readonly Transform[] driverFeet = new Transform[2];
        private readonly Transform[] driverTrolleyShoulders = new Transform[2];
        private readonly Transform[] driverTrolleyForearms = new Transform[2];
        private readonly Transform[] driverTrolleyHands = new Transform[2];
        private readonly Vector3[] driverPlantedFeet = new Vector3[2];
        private readonly Quaternion[] driverPlantedFootRotations = new Quaternion[2];
        private readonly Transform[] workerSpines = new Transform[5];
        private readonly Vector3[] workerRoute = new Vector3[6];
        private Vector3 driverDoorDock;
        private Quaternion driverDoorRest;
        public bool DriverSeatedContactsMatch { get; private set; }
        public string LastCrewContactFailure { get; private set; }

        private void CreateWorkers()
        {
            VillageResidentLibrary library = VillageResidentLibrary.Load();
            if (library == null || library.GetPrefab(VillageResidentRole.StationWorker) == null)
                throw new InvalidOperationException("The cannery requires the ordinary authored worker rig.");
            string[] names = { "Cannery Receiver", "Cannery Preparation Worker", "Cannery Seamer",
                "Cannery Retort and Packing Worker", "Fish Delivery Driver" };
            workers = new VillageResidentPresentation[names.Length];
            for (int i = 0; i < workers.Length; i++)
            {
                workers[i] = library.Create(VillageResidentRole.StationWorker, transform);
                workers[i].name = names[i];
                if (i == 4) CityPortCrew.AlignWorkerModelWithPlacement(workers[i]);
                workerSpines[i] = Require(workers[i].ModelRoot, "spine");
            }
            driverPelvis = Require(workers[4].ModelRoot, "pelvis");
            driverSteeringShoulder = Require(workers[4].ModelRoot, "upper_arm.R");
            driverMouth = Require(workers[4].ModelRoot, CityPedestrianHandProps.MouthSocketName);
            for (int i = 0; i < 2; i++)
            {
                string side = i == 0 ? ".L" : ".R";
                driverThighs[i] = Require(workers[4].ModelRoot, "thigh" + side);
                driverShins[i] = Require(workers[4].ModelRoot, "shin" + side);
                driverFeet[i] = Require(workers[4].ModelRoot, "foot" + side);
                driverTrolleyShoulders[i] = Require(workers[4].ModelRoot, "upper_arm" + side);
                driverTrolleyForearms[i] = Require(workers[4].ModelRoot, "forearm" + side);
                driverTrolleyHands[i] = Require(workers[4].ModelRoot, "hand" + side);
            }
            driverSeat = Require(Truck, "ANCHOR_TruckDriver");
            driverLeftHand = Require(Truck, "ANCHOR_DriverLeftHand");
            driverRightHand = Require(Truck, "ANCHOR_DriverRightHand");
            driverFoot = Require(Truck, "ANCHOR_DriverFoot");
            driverDoor = Require(Truck, "MOVE_DriverDoor");
            driverExit = Require(Truck, "ANCHOR_DriverExit");
            driverRearWalk = Require(Truck, "ANCHOR_DriverRearWalk");
            driverDoorDock = Truck.InverseTransformPoint(driverDoor.position);
            driverDoorRest = Quaternion.Inverse(Truck.rotation) * driverDoor.rotation;
            trolleyLeftHand = Require(trolley, "ANCHOR_TrolleyHandleLeft");
            trolleyRightHand = Require(trolley, "ANCHOR_TrolleyHandleRight");
            CreateCrewAppearance();
            portConversation = port.GetComponentInChildren<CityPortConversationController>();
            if (portConversation != null) portConversation.RegisterDriver(workers[4]);
        }

        private void ApplyWorkers()
        {
            WorkerHandsMatch = true;
            LastCrewContactFailure = null;
            DriverSeatedContactsMatch = true;
            for (int i = 0; i < workers.Length; i++)
            {
                bool visible = i == 4 ? TruckPresentationActive : FactoryPresentationActive;
                if (workers[i].gameObject.activeSelf == visible) continue;
                workers[i].gameObject.SetActive(visible);
                if (visible) workerAppearanceDirty[i] = true;
            }
            float seconds = (float)Snapshot.Seconds;
            if (FactoryPresentationActive)
            {
                StandWorker(workers[0], Anchor("Receiver"), Plan.Forward, Anchor("ReceivingLoad"));

                ApplyStationWorker(1, "Preparation", Production.Stage == CityCanneryProductionStage.Prepare);
                ApplyStationWorker(2, "Seamer", Production.Stage == CityCanneryProductionStage.Fill ||
                    Production.Stage == CityCanneryProductionStage.Seal);
                ApplyRetortWorker((float)Production.Seconds);
            }

            if (TruckPresentationActive)
            {
                bool driverHandling = Snapshot.IsTransfer;
                float open = driverHandling ? Mathf.Min(Ease(seconds), Ease((float)Snapshot.Duration - seconds)) :
                    IsReversing ? ReverseDoorWeight : 0;
                driverDoor.SetPositionAndRotation(Truck.TransformPoint(driverDoorDock),
                    Truck.rotation * Quaternion.AngleAxis((IsReversing ? ReverseDoorMaximumAngle : 78f) * open, Vector3.up) * driverDoorRest);
                if (IsReversing) ApplyReversingDriver();
                else if (!driverHandling) ApplySeatedDriver();
                else if (handlingActive) ApplyTrolleyWorker(workers[4], 1f);
                else ApplyDriverApproach(seconds >= Snapshot.Duration - TransferEdge
                    ? (float)(Snapshot.Duration - Snapshot.Seconds) : seconds,
                    seconds >= Snapshot.Duration - TransferEdge);
            }
            // Enabling the shared resident rig restores its village atlas.
            // Reapply this crew's clothes after that first pose, once per wake.
            for (int i = 0; i < workers.Length; i++)
                if (workers[i].gameObject.activeSelf && workerAppearanceDirty[i]) ApplyCrewAppearance(i);
            ApplyDriverConversation();
        }

        private void ApplyStationWorker(int index, string station, bool working)
        {
            VillageResidentPresentation actor = workers[index];
            Vector3 right = Anchor(station + "RightHand"), left = Anchor(station + "LeftHand");
            Vector3 load = index == 1 ? Anchor("PreparationLoad") : tray.position;
            StandWorker(actor, Anchor(station + "Worker"), Plan.Right, load);
            if (!working) return;
            // Preparation follows the available lot; the powered line doses
            // its fifteen visible cans. Only the active station works.
            int operations = Production.Stage == CityCanneryProductionStage.Prepare ? Production.UnitCount : 15;
            float unitDuration = (float)Production.Duration / operations;
            float unit = Mathf.Repeat((float)Production.Seconds, unitDuration) / unitDuration;
            float posture = CrewWorkWindow(unit, 0f, .96f, .13f);
            float rightWeight = posture, leftWeight = posture;
            Vector3 look = load;
            if (Production.Stage == CityCanneryProductionStage.Prepare)
            {
                // The left hand braces the near crate rim; the right sorts a
                // short section of its contents, then both release the unit.
                float sort = CrewWorkWindow(unit, .19f, .69f, .18f);
                right += Plan.Right * (.035f * sort) - Plan.Forward * (.07f * sort);
                rightWeight = CrewWorkWindow(unit, .07f, .83f, .14f);
                leftWeight = CrewWorkWindow(unit, .01f, .90f, .13f);
                look = Vector3.Lerp(load, right, sort * .5f);
            }
            else if (Production.Stage == CityCanneryProductionStage.Fill)
            {
                // One short dose command per unit; the other hand rests on
                // the guard while the worker watches the visible can tray.
                left = Anchor("FillControl");
                leftWeight = CrewWorkWindow(unit, .08f, .40f, .10f);
                rightWeight = CrewWorkWindow(unit, .02f, .85f, .14f);
                look = Vector3.Lerp(load, left, leftWeight * .55f);
            }
            else if (Production.Stage == CityCanneryProductionStage.Seal)
            {
                right = Anchor("SealControl");
                rightWeight = CrewWorkWindow(unit, .04f, .39f, .11f);
                leftWeight = CrewWorkWindow(unit, .02f, .85f, .14f);
                look = Vector3.Lerp(seamer.position, right, rightWeight * .5f);
            }
            ApplyStationPose(index, posture, 24f);
            ApplyCrewLook(actor, look);
            ApplyCrewContacts(actor, right, left, rightWeight, leftWeight);
        }

        private void ApplyStationPose(int index, float weight, float lean)
        {
            VillageResidentPresentation actor = workers[index];
            actor.Apply(VillageResidentAction.StationWork, 1.1f * weight);
            // Only the spine changes after the authored planted pose: the
            // pelvis, knees and feet keep their grounded station positions.
            workerSpines[index].rotation = Quaternion.AngleAxis(lean * weight, actor.transform.right) *
                workerSpines[index].rotation;
        }

        private static float CrewWorkWindow(float time, float start, float end, float ramp) =>
            Mathf.Min(Ease((time - start) / ramp), Ease((end - time) / ramp));

        private void ApplyRetortWorker(float seconds)
        {
            VillageResidentPresentation actor = workers[3];
            Vector3 retort = Anchor("RetortOperator"), packing = Anchor("PackingWorker");
            bool toPacking = Production.Stage == CityCanneryProductionStage.Cool && seconds >= Production.Duration - 12;
            bool toRetort = Production.ReturnSeconds < 12;
            if (toPacking || toRetort)
            {
                Vector3 from = toPacking ? retort : packing, to = toPacking ? packing : retort;
                workerRoute[0] = from;
                workerRoute[1] = Plan.World(new Vector3(-7.18f, CityCanneryPlan.FloorTop, Plan.Local(from).z));
                workerRoute[2] = Plan.World(new Vector3(-7.18f, CityCanneryPlan.FloorTop, Plan.Local(to).z));
                workerRoute[3] = workerRoute[4] = workerRoute[5] = to;
                float t = toPacking ? seconds - (float)Production.Duration + 12 : (float)Production.ReturnSeconds;
                WalkWorker(actor, t / 12f, 12f / (float)CityFishSupplyCycle.ProductionSpeed, Plan.Right, Plan.Right);
                return;
            }
            if (Production.Stage == CityCanneryProductionStage.Pack)
            {
                ApplyPackingWorker(seconds);
                return;
            }
            Vector3 hand = Anchor("RetortHand");
            StandWorker(actor, retort, Plan.Right, hand);
            if (Production.Stage != CityCanneryProductionStage.LoadRetort && Production.Stage != CityCanneryProductionStage.Cool) return;
            float duration = Production.Stage == CityCanneryProductionStage.Cool ? (float)Production.Duration - 12 : (float)Production.Duration;
            // A command to open/extend and one to close/retract. The long
            // middle is observation of the drawer, with the hand off the button.
            float weight = Mathf.Max(CrewWorkWindow(seconds, 0f, 3.4f, .7f),
                CrewWorkWindow(seconds, duration - 4f, duration, .7f));
            ApplyStationPose(3, weight, 0);
            ApplyCrewLook(actor, Vector3.Lerp(basket.position + Vector3.up * .25f, hand, weight * .6f));
            // The control is on the operator's left; the nearer hand reaches
            // it without crossing the chest or stretching the shoulder.
            ApplyCrewContacts(actor, null, hand, weight);
        }

        private void ApplyPackingWorker(float seconds)
        {
            VillageResidentPresentation actor = workers[3];
            Vector3 right = Anchor("PackingCartonRightHand"), left = Anchor("PackingCartonLeftHand");
            Vector3 rim = (right + left) * .5f;
            StandWorker(actor, Anchor("PackingWorker"), Plan.Right, tray.position);
            // The tray takes the first eight seconds to reach cooling. Its
            // fifteen actual cans then share the owner's feed/lift/drop path;
            // these hands never invent another can or another packing clock.
            if (seconds <= 8f) return;
            float posture = CrewWorkWindow(seconds, 8f, (float)Production.Duration, .7f);
            float reach = packingCanInHand ? Mathf.Clamp01(packingCanHandWeight) : 0f;
            // Keep the shoulders over the work throughout the lift. Standing
            // upright as the can rises pulls them away from the farther carton
            // row precisely when the hands need that remaining forward reach.
            ApplyStationPose(3, posture, Mathf.Lerp(8f, 34f, reach));
            Vector3 toCan = Vector3.ProjectOnPlane(packingCanContact - actor.transform.position, Vector3.up);
            // The pickup is south of the carton; turn the chest far enough for
            // both shoulders to face it instead of reaching one arm across it.
            float turn = Mathf.Clamp(Vector3.SignedAngle(actor.transform.forward, toCan, Vector3.up), -42f, 42f);
            workerSpines[3].rotation = Quaternion.AngleAxis(turn * reach, actor.transform.up) * workerSpines[3].rotation;
            ApplyCrewLook(actor, Vector3.Lerp(seconds < Production.Duration - 2 ? tray.position : rim,
                packingCanContact, reach));
            // First approach the stationary fed can, hold it throughout the
            // visible arc, then open/retract above the rim before it is lowered.
            Vector3 across = toCan.sqrMagnitude > .001f ?
                Vector3.Cross(toCan.normalized, Vector3.up) * .055f : Plan.Forward * .055f;
            ApplyCrewContacts(actor, Vector3.Lerp(right, packingCanContact + across, reach),
                Vector3.Lerp(left, packingCanContact - across, reach), posture);
        }

        private void ApplyTrolleyWorker(VillageResidentPresentation actor, float handWeight)
        {
            actor.transform.SetPositionAndRotation(trolleyOperatorPosition, trolleyOperatorRotation);
            float gaitTime=(float)(WorkingSeconds%120d);
            // The resident sampler clamps negative time. Count down through
            // a positive interval to sample a genuine backward step.
            if(trolleyGaitDirection<0f) gaitTime=120f-gaitTime;
            actor.ApplyLocomotion(trolleyMotion, false, gaitTime);
            Transform spine = workerSpines[actor == workers[0] ? 0 : 4];
            // Keep the shoulders within reach of the handle throughout the
            // walking clip, including the phases exposed by shorter deliveries.
            spine.rotation = Quaternion.AngleAxis(14f * handWeight, actor.transform.right) * spine.rotation;
            FitTrolleyContactStance(actor, handWeight);
            ApplyCrewLook(actor, trolley.position + trolley.forward + Vector3.up * .8f, .75f);
            ApplyCrewContacts(actor, trolleyRightHand.position, trolleyLeftHand.position, handWeight);
        }

        private void FitTrolleyContactStance(VillageResidentPresentation actor, float weight)
        {
            if (actor != workers[4] || weight <= 0f) return;
            Vector3 pelvisStart = driverPelvis.position, up = actor.transform.up;
            for (int i = 0; i < 2; i++)
            {
                driverPlantedFeet[i] = driverFeet[i].position;
                driverPlantedFootRotations[i] = driverFeet[i].rotation;
            }
            // At a kerb the driver's feet and jack wheels stand on different
            // levels. Bring the pelvis toward both grips instead of stretching
            // the arms or lowering the actor through the pavement.
            for (int iteration = 0; iteration < 4; iteration++)
            {
                Vector3 correction = Vector3.zero;
                for (int i = 0; i < 2; i++)
                {
                    Transform grip = i == 0 ? actor.LeftGrip : actor.RightGrip;
                    Vector3 handle = i == 0 ? trolleyLeftHand.position : trolleyRightHand.position;
                    Vector3 wrist = handle - (grip.position - driverTrolleyHands[i].position);
                    Vector3 delta = wrist - driverTrolleyShoulders[i].position;
                    float reach = LimbTwoBoneIk.ChainLength(driverTrolleyShoulders[i],
                        driverTrolleyForearms[i], driverTrolleyHands[i]) - .008f;
                    correction += delta.normalized * Mathf.Max(0f, delta.magnitude - reach);
                }
                correction *= .5f * weight;
                // This is a planted stoop: an upward reach never pulls the
                // pelvis beyond the legs' length or lifts a sole off its step.
                correction -= up * Mathf.Max(0f, Vector3.Dot(correction, up));
                driverPelvis.position += correction;
                float lower = 0f;
                for (int i = 0; i < 2; i++)
                {
                    Vector3 hip = driverThighs[i].position - driverPlantedFeet[i];
                    float height = Vector3.Dot(hip, up);
                    float reach = LimbTwoBoneIk.ChainLength(driverThighs[i], driverShins[i], driverFeet[i]) * .998f;
                    float availableHeight = Mathf.Sqrt(Mathf.Max(0f, reach * reach - Vector3.ProjectOnPlane(hip, up).sqrMagnitude));
                    lower = Mathf.Max(lower, height - availableHeight);
                }
                driverPelvis.position -= up * lower;
                if (correction.sqrMagnitude < .0000001f && lower < .0001f) break;
            }
            if ((driverPelvis.position - pelvisStart).sqrMagnitude < .0000001f) return;
            for (int i = 0; i < 2; i++)
                LimbTwoBoneIk.Solve(driverThighs[i], driverShins[i], driverFeet[i], driverPlantedFeet[i],
                    driverPlantedFootRotations[i], driverThighs[i].position + actor.transform.forward * .65f,
                    1f, 1f, true);
        }

        private void ApplyDriverApproach(float seconds, bool returning)
        {
            Vector3 exit = driverExit.position;
            exit.y = HandlingGroundHeight(exit);
            if (seconds < 3.5f)
            {
                ApplyDriverSeatTransition(exit, Ease((seconds - 1f) / 2.5f), returning,
                    1f - Ease(seconds / .8f));
                return;
            }
            if (seconds >= TrolleyHandleArrival)
            {
                ApplyTrolleyWorker(workers[4], trolleyHandWeight);
                return;
            }
            driverTrolleyApproach[0] = exit;
            driverTrolleyApproach[1] = driverRearWalk.position;
            driverTrolleyApproach[1].y = HandlingGroundHeight(driverTrolleyApproach[1]);
            if (TrolleySite == 2)
            {
                Vector3 inner = Route.ShopDoorPoint + ShopInward * 2.2f;
                // The service entrance is already on the cab's clear side.
                // Reach it directly instead of walking around the lowered lift.
                driverTrolleyApproach[1] = exit;
                driverTrolleyApproach[2] = Route.ShopDoorPoint - ShopInward * 1.8f;
                driverTrolleyApproach[3] = Route.ShopDoorPoint;
                driverTrolleyApproach[4] = inner;
                driverTrolleyApproach[5] = inner + ShopInward * 1.3f;
                driverTrolleyApproach[6] = trolleyOperatorPosition + ShopInward * 1.3f;
            }
            else if (TrolleySite == 0)
            {
                // Pass the complete western side before coming around behind
                // the handle. A diagonal from the truck to the handle's side
                // crosses the parked forks even when both endpoints are clear.
                driverTrolleyApproach[1] = port.Plan.World(new Vector3(8.4f, CityPortPlan.DeckHeight, -10.3f));
                driverTrolleyApproach[2] = port.Plan.World(new Vector3(4.2f, CityPortPlan.DeckHeight, -9.4f));
                driverTrolleyApproach[3] = port.Plan.World(new Vector3(4.2f, CityPortPlan.DeckHeight, -6.97f));
                driverTrolleyApproach[4] = trolleyOperatorPosition;
                driverTrolleyApproach[5] = trolleyOperatorPosition;
                driverTrolleyApproach[6] = trolleyOperatorPosition;
            }
            else
            {
                // Reach the handle around the side of the parked jack;
                // cutting behind its axis would cross the factory wall.
                driverTrolleyApproach[2] = trolleyOperatorPosition - trolley.right * .85f;
                driverTrolleyApproach[2].y = HandlingGroundHeight(driverTrolleyApproach[2]);
                for (int i = 3; i <= 6; i++) driverTrolleyApproach[i] = trolleyOperatorPosition;
            }
            driverTrolleyApproach[7] = trolleyOperatorPosition;
            WalkWorker(workers[4], (seconds - 3.5f) / (TrolleyHandleArrival - 3.5f), TrolleyHandleArrival - 3.5f,
                Truck.right * (returning ? 1f : -1f), trolleyOperatorRotation * Vector3.forward, returning, driverTrolleyApproach);
        }

        private void ApplySeatedDriver() => ApplyDriverSeatTransition(Vector3.zero, 0f, false, 1f);

        private void ApplyDriverSeatTransition(Vector3 exit, float standing, bool returning, float hands)
        {
            VillageResidentPresentation actor = workers[4];
            Quaternion exitRotation = Quaternion.LookRotation(Truck.right * (returning ? 1f : -1f), Vector3.up);
            actor.transform.SetPositionAndRotation(Truck.position,
                Quaternion.Slerp(Truck.rotation, exitRotation, standing));
            actor.Apply(VillageResidentAction.Idle, 0);
            Vector3 seatedRoot = actor.transform.position + driverSeat.position - driverPelvis.position;
            actor.transform.position = Vector3.Lerp(seatedRoot, exit, standing);
            for (int i = 0; i < 2; i++)
            {
                Vector3 pedal = driverFoot.position + Truck.right * (i == 0 ? -.14f : .14f);
                Vector3 target = Vector3.Lerp(pedal, driverFeet[i].position, standing);
                LimbTwoBoneIk.Solve(driverThighs[i], driverShins[i], driverFeet[i], target,
                    driverFeet[i].rotation, driverThighs[i].position + Truck.forward * .65f,
                    1f, .995f, true);
                if (standing <= .001f)
                    DriverSeatedContactsMatch &= Vector3.Distance(driverFeet[i].position, pedal) <= .025f;
            }
            if (standing <= .001f)
                DriverSeatedContactsMatch &= Vector3.Distance(driverPelvis.position, driverSeat.position) <= .01f;
            ApplyCrewLook(actor, driverSeat.position + Truck.forward * 12f + Vector3.up * .45f, 1f - standing);
            ApplyCrewContacts(actor, driverRightHand.position, driverLeftHand.position, hands);
        }

        private void ApplyCrewContacts(VillageResidentPresentation actor, Vector3? right, Vector3? left, float weight)
            => ApplyCrewContacts(actor, right, left, weight, weight);

        private void ApplyCrewContacts(VillageResidentPresentation actor, Vector3? right, Vector3? left,
            float rightWeight, float leftWeight)
        {
            // Measure the actual requested frame targets, including the entry
            // and release interpolation. Full contact still targets the model
            // anchor exactly; a partial reach never masquerades as contact.
            Vector3? rightTarget = right.HasValue ? Vector3.Lerp(actor.RightGrip.position, right.Value,
                Mathf.Clamp01(rightWeight)) : (Vector3?)null;
            Vector3? leftTarget = left.HasValue ? Vector3.Lerp(actor.LeftGrip.position, left.Value,
                Mathf.Clamp01(leftWeight)) : (Vector3?)null;
            bool matches = actor.ApplyHandContacts(rightTarget, leftTarget);
            if (rightTarget.HasValue) matches &= Vector3.Distance(actor.RightGrip.position, rightTarget.Value) <= .025f;
            if (leftTarget.HasValue) matches &= Vector3.Distance(actor.LeftGrip.position, leftTarget.Value) <= .025f;
            WorkerHandsMatch &= matches;
            if (!matches)
                LastCrewContactFailure = actor.name + ": right=" + (rightTarget.HasValue
                    ? Vector3.Distance(actor.RightGrip.position, rightTarget.Value).ToString("F3") : "n/a") +
                    ", left=" + (leftTarget.HasValue ? Vector3.Distance(actor.LeftGrip.position, leftTarget.Value).ToString("F3") : "n/a");
        }

        private void StandWorker(VillageResidentPresentation actor, Vector3 point, Vector3 forward, Vector3 look)
        {
            actor.transform.SetPositionAndRotation(point, Quaternion.LookRotation(forward, Vector3.up));
            actor.Apply(VillageResidentAction.Idle, (float)(WorkingSeconds % 120d));
            ApplyCrewLook(actor, look, .7f);
        }

        private static void ApplyCrewLook(VillageResidentPresentation actor, Vector3 target, float weight = 1f)
        {
            Vector3 direction = actor.transform.InverseTransformDirection(target - actor.Head.position);
            if (direction.sqrMagnitude < .001f) return;
            float yaw = Mathf.Clamp(Mathf.Atan2(direction.x, direction.z) * Mathf.Rad2Deg, -30f, 30f);
            float pitch = Mathf.Clamp(-Mathf.Atan2(direction.y,
                new Vector2(direction.x, direction.z).magnitude) * Mathf.Rad2Deg, -15f, 22f);
            actor.Head.rotation = Quaternion.AngleAxis(yaw * weight, actor.transform.up) *
                Quaternion.AngleAxis(pitch * weight, actor.transform.right) * actor.Head.rotation;
        }

        private void WalkWorker(VillageResidentPresentation actor, float progress, float duration,
            Vector3 startFacing, Vector3 endFacing, bool backwards = false, Vector3[] path = null)
        {
            if (path == null) path = workerRoute;
            float p = Mathf.Clamp01(progress), distance = 0;
            for (int i = 1; i < path.Length; i++) distance += Vector3.Distance(path[i - 1], path[i]);
            float eased = Ease(p);
            float speed = 6f * p * (1f - p) * distance / duration;
            if (path == driverTrolleyApproach && TrolleySite == 0)
            {
                // A normal, steady walk between short starts and stops fits
                // the longer canopy approach without speeding up its middle.
                // Integrate a .6-second acceleration/deceleration exactly.
                float ramp = .6f / duration;
                if (p < ramp) eased = p * p / (2f * ramp * (1f - ramp));
                else if (p > 1f - ramp) eased = 1f - (1f - p) * (1f - p) / (2f * ramp * (1f - ramp));
                else eased = (p - ramp * .5f) / (1f - ramp);
                speed = distance / (duration - .6f) * Mathf.Clamp01(Mathf.Min(p, 1f - p) / ramp);
            }
            Vector3 point = Along(path, eased);
            if (path == driverTrolleyApproach) point.y = HandlingGroundHeight(point);
            Vector3 forward = Along(path, Mathf.Min(1f, eased + .015f)) -
                Along(path, Mathf.Max(0f, eased - .015f));
            if (backwards) forward = -forward;
            if (forward.sqrMagnitude < .0001f) forward = endFacing;
            Quaternion rotation = Quaternion.LookRotation(forward, Vector3.up);
            if (p < .08f) rotation = Quaternion.Slerp(Quaternion.LookRotation(startFacing, Vector3.up), rotation, Ease(p / .08f));
            if (p > .92f) rotation = Quaternion.Slerp(rotation, Quaternion.LookRotation(endFacing, Vector3.up), Ease((p - .92f) / .08f));
            actor.transform.SetPositionAndRotation(point, rotation);
            actor.ApplyLocomotion(speed, false, (backwards ? 1 - eased : eased) * distance / .95f);
        }
    }
}

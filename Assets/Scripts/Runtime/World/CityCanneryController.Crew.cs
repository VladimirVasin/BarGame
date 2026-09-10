using System;
using UnityEngine;

namespace BarPromenade
{
    public sealed partial class CityCanneryController
    {
        private VillageResidentPresentation[] workers;
        private Transform driverPelvis, driverSeat, driverLeftHand, driverRightHand, driverFoot;
        private Transform driverDoor, trolleyLeftHand, trolleyRightHand;
        private readonly Transform[] driverThighs = new Transform[2];
        private readonly Transform[] driverShins = new Transform[2];
        private readonly Transform[] driverFeet = new Transform[2];
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
                workerSpines[i] = Require(workers[i].ModelRoot, "spine");
            }
            driverPelvis = Require(workers[4].ModelRoot, "pelvis");
            for (int i = 0; i < 2; i++)
            {
                string side = i == 0 ? ".L" : ".R";
                driverThighs[i] = Require(workers[4].ModelRoot, "thigh" + side);
                driverShins[i] = Require(workers[4].ModelRoot, "shin" + side);
                driverFeet[i] = Require(workers[4].ModelRoot, "foot" + side);
            }
            driverSeat = Require(Truck, "ANCHOR_TruckDriver");
            driverLeftHand = Require(Truck, "ANCHOR_DriverLeftHand");
            driverRightHand = Require(Truck, "ANCHOR_DriverRightHand");
            driverFoot = Require(Truck, "ANCHOR_DriverFoot");
            driverDoor = Require(Truck, "MOVE_DriverDoor");
            driverDoorDock = Truck.InverseTransformPoint(driverDoor.position);
            driverDoorRest = Quaternion.Inverse(Truck.rotation) * driverDoor.rotation;
            trolleyLeftHand = Require(trolley, "ANCHOR_TrolleyHandleLeft");
            trolleyRightHand = Require(trolley, "ANCHOR_TrolleyHandleRight");
            CreateCrewAppearance();
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
                bool factoryHandling = Snapshot.Stage == CityFishSupplyStage.UnloadFish ||
                    Snapshot.Stage == CityFishSupplyStage.LoadFinished;
                if (factoryHandling) ApplyReceiverTransfer(seconds);
                else StandWorker(workers[0], Anchor("Receiver"), Plan.Forward, Anchor("ReceivingLoad"));

                ApplyStationWorker(1, "Preparation", Snapshot.Stage == CityFishSupplyStage.Prepare);
                ApplyStationWorker(2, "Seamer", Snapshot.Stage == CityFishSupplyStage.Fill ||
                    Snapshot.Stage == CityFishSupplyStage.Seal);
                ApplyRetortWorker(seconds);
            }

            if (TruckPresentationActive)
            {
                bool driverHandling = Snapshot.Stage == CityFishSupplyStage.LoadFish ||
                    Snapshot.Stage == CityFishSupplyStage.UnloadShop;
                float open = driverHandling ? Mathf.Min(Ease(seconds), Ease((float)Snapshot.Duration - seconds)) : 0;
                driverDoor.SetPositionAndRotation(Truck.TransformPoint(driverDoorDock),
                    Truck.rotation * Quaternion.AngleAxis(78f * open, Vector3.up) * driverDoorRest);
                if (!driverHandling) ApplySeatedDriver();
                else if (handlingActive) ApplyTrolleyWorker(workers[4], 1f);
                else ApplyDriverApproach(seconds >= Snapshot.Duration - 12
                    ? (float)(Snapshot.Duration - Snapshot.Seconds) : seconds,
                    seconds >= Snapshot.Duration - 12);
            }
            // Enabling the shared resident rig restores its village atlas.
            // Reapply this crew's clothes after that first pose, once per wake.
            for (int i = 0; i < workers.Length; i++)
                if (workers[i].gameObject.activeSelf && workerAppearanceDirty[i]) ApplyCrewAppearance(i);
        }

        private void ApplyStationWorker(int index, string station, bool working)
        {
            VillageResidentPresentation actor = workers[index];
            Vector3 right = Anchor(station + "RightHand"), left = Anchor(station + "LeftHand");
            Vector3 load = index == 1 ? Anchor("PreparationLoad") : tray.position;
            StandWorker(actor, Anchor(station + "Worker"), Plan.Right, load);
            if (!working) return;
            // Six finite handling units, each with a reach, an operation and
            // a release. A sampled stage never leaves an idle worker dancing.
            float unitDuration = (float)Snapshot.Duration / 6f;
            float unit = Mathf.Repeat((float)Snapshot.Seconds, unitDuration) / unitDuration;
            float posture = CrewWorkWindow(unit, 0f, .96f, .13f);
            float rightWeight = posture, leftWeight = posture;
            Vector3 look = load;
            if (Snapshot.Stage == CityFishSupplyStage.Prepare)
            {
                // The left hand braces the near crate rim; the right sorts a
                // short section of its contents, then both release the unit.
                float sort = CrewWorkWindow(unit, .19f, .69f, .18f);
                right += Plan.Right * (.035f * sort) - Plan.Forward * (.07f * sort);
                rightWeight = CrewWorkWindow(unit, .07f, .83f, .14f);
                leftWeight = CrewWorkWindow(unit, .01f, .90f, .13f);
                look = Vector3.Lerp(load, right, sort * .5f);
            }
            else if (Snapshot.Stage == CityFishSupplyStage.Fill)
            {
                // One short dose command per unit; the other hand rests on
                // the guard while the worker watches the visible can tray.
                left = Anchor("FillControl");
                leftWeight = CrewWorkWindow(unit, .08f, .40f, .10f);
                rightWeight = CrewWorkWindow(unit, .02f, .85f, .14f);
                look = Vector3.Lerp(load, left, leftWeight * .55f);
            }
            else if (Snapshot.Stage == CityFishSupplyStage.Seal)
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
            bool toPacking = Snapshot.Stage == CityFishSupplyStage.Cool && seconds >= Snapshot.Duration - 12;
            bool toRetort = Snapshot.Stage == CityFishSupplyStage.LoadFinished && seconds < 12;
            if (toPacking || toRetort)
            {
                Vector3 from = toPacking ? retort : packing, to = toPacking ? packing : retort;
                workerRoute[0] = from;
                workerRoute[1] = Plan.World(new Vector3(-7.18f, CityCanneryPlan.FloorTop, Plan.Local(from).z));
                workerRoute[2] = Plan.World(new Vector3(-7.18f, CityCanneryPlan.FloorTop, Plan.Local(to).z));
                workerRoute[3] = workerRoute[4] = workerRoute[5] = to;
                float t = toPacking ? seconds - (float)Snapshot.Duration + 12 : seconds;
                WalkWorker(actor, t / 12f, 12f, Plan.Right, Plan.Right);
                return;
            }
            if (Snapshot.Stage == CityFishSupplyStage.Pack)
            {
                ApplyPackingWorker(seconds);
                return;
            }
            Vector3 hand = Anchor("RetortHand");
            StandWorker(actor, retort, Plan.Right, hand);
            if (Snapshot.Stage != CityFishSupplyStage.LoadRetort && Snapshot.Stage != CityFishSupplyStage.Cool) return;
            float duration = Snapshot.Stage == CityFishSupplyStage.Cool ? (float)Snapshot.Duration - 12 : (float)Snapshot.Duration;
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
            float posture = CrewWorkWindow(seconds, 8f, (float)Snapshot.Duration, .7f);
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
            ApplyCrewLook(actor, Vector3.Lerp(seconds < Snapshot.Duration - 2 ? tray.position : rim,
                packingCanContact, reach));
            // First approach the stationary fed can, hold it throughout the
            // visible arc, then open/retract above the rim before it is lowered.
            Vector3 across = toCan.sqrMagnitude > .001f ?
                Vector3.Cross(toCan.normalized, Vector3.up) * .055f : Plan.Forward * .055f;
            ApplyCrewContacts(actor, Vector3.Lerp(right, packingCanContact + across, reach),
                Vector3.Lerp(left, packingCanContact - across, reach), posture);
        }

        private void ApplyReceiverTransfer(float seconds)
        {
            if (handlingActive) { ApplyTrolleyWorker(workers[0], 1f); return; }
            bool returning = seconds >= Snapshot.Duration - 12;
            float t = returning ? (float)(Snapshot.Duration - Snapshot.Seconds) : seconds;
            Vector3 home = Anchor("Receiver"), door = Anchor(Loading ? "FinishedDoor" : "RawDoor");
            workerRoute[0] = home;
            workerRoute[1] = Plan.World(new Vector3(-2.75f, CityCanneryPlan.FloorTop, Plan.Local(home).z));
            workerRoute[2] = Plan.World(new Vector3(-2.75f, CityCanneryPlan.FloorTop, Plan.Local(door).z));
            workerRoute[3] = door;
            workerRoute[4] = door + Plan.Right * 1.5f;
            workerRoute[4].y = Plan.Origin.y + CityCanneryPlan.YardTop;
            workerRoute[5] = trolleyOperatorPosition;
            if (t >= 11.2f) ApplyTrolleyWorker(workers[0], Ease((t - 11.2f) / .8f));
            else WalkWorker(workers[0], t / 11.2f, 11.2f, Plan.Forward,
                trolleyOperatorRotation * Vector3.forward, returning);
        }

        private void ApplyTrolleyWorker(VillageResidentPresentation actor, float handWeight)
        {
            actor.transform.SetPositionAndRotation(trolleyOperatorPosition, trolleyOperatorRotation);
            actor.ApplyLocomotion(trolleyMotion, false, (float)(WorkingSeconds % 120d));
            Transform spine = workerSpines[actor == workers[0] ? 0 : 4];
            spine.rotation = Quaternion.AngleAxis(8f * handWeight, actor.transform.right) * spine.rotation;
            ApplyCrewLook(actor, trolley.position + trolley.forward + Vector3.up * .8f, .75f);
            ApplyCrewContacts(actor, trolleyRightHand.position, trolleyLeftHand.position, handWeight);
        }

        private void ApplyDriverApproach(float seconds, bool returning)
        {
            Vector3 exit = Truck.TransformPoint(new Vector3(-1.78f, 0, 4.05f));
            exit.y = GroundY;
            if (seconds < 3.5f)
            {
                ApplyDriverSeatTransition(exit, Ease((seconds - 1f) / 2.5f), returning,
                    1f - Ease(seconds / .8f));
                return;
            }
            if (seconds >= 11.2f)
            {
                ApplyTrolleyWorker(workers[4], Ease((seconds - 11.2f) / .8f));
                return;
            }
            workerRoute[0] = exit;
            workerRoute[1] = Truck.TransformPoint(new Vector3(-1.78f, 0, -4.95f));
            workerRoute[1].y = GroundY;
            workerRoute[2] = trolleyOperatorPosition - Truck.forward * .45f;
            workerRoute[3] = workerRoute[4] = workerRoute[5] = trolleyOperatorPosition;
            WalkWorker(workers[4], (seconds - 3.5f) / 7.7f, 7.7f,
                Truck.right * (returning ? 1f : -1f), trolleyOperatorRotation * Vector3.forward, returning);
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
            Vector3 startFacing, Vector3 endFacing, bool backwards = false)
        {
            float p = Mathf.Clamp01(progress), distance = 0;
            for (int i = 1; i < workerRoute.Length; i++) distance += Vector3.Distance(workerRoute[i - 1], workerRoute[i]);
            float eased = Ease(p);
            Vector3 point = Along(workerRoute, eased);
            Vector3 forward = Along(workerRoute, Mathf.Min(1f, eased + .015f)) -
                Along(workerRoute, Mathf.Max(0f, eased - .015f));
            if (backwards) forward = -forward;
            if (forward.sqrMagnitude < .0001f) forward = endFacing;
            Quaternion rotation = Quaternion.LookRotation(forward, Vector3.up);
            if (p < .08f) rotation = Quaternion.Slerp(Quaternion.LookRotation(startFacing, Vector3.up), rotation, Ease(p / .08f));
            if (p > .92f) rotation = Quaternion.Slerp(rotation, Quaternion.LookRotation(endFacing, Vector3.up), Ease((p - .92f) / .08f));
            actor.transform.SetPositionAndRotation(point, rotation);
            float speed = 6f * p * (1f - p) * distance / duration;
            actor.ApplyLocomotion(speed, false, (backwards ? 1 - eased : eased) * distance / .95f);
        }
    }
}

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
        }

        private void ApplyWorkers()
        {
            WorkerHandsMatch = true;
            LastCrewContactFailure = null;
            DriverSeatedContactsMatch = true;
            float seconds = (float)Snapshot.Seconds;
            bool factoryHandling = Snapshot.Stage == CityFishSupplyStage.UnloadFish ||
                Snapshot.Stage == CityFishSupplyStage.LoadFinished;
            if (factoryHandling) ApplyReceiverTransfer(seconds);
            else StandWorker(workers[0], Anchor("Receiver"), Plan.Forward, Anchor("ReceivingLoad"));

            ApplyStationWorker(workers[1], "Preparation", Snapshot.Stage == CityFishSupplyStage.Prepare);
            ApplyStationWorker(workers[2], "Seamer", Snapshot.Stage == CityFishSupplyStage.Fill ||
                Snapshot.Stage == CityFishSupplyStage.Seal);
            ApplyRetortWorker(seconds);

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

        private void ApplyStationWorker(VillageResidentPresentation actor, string station, bool working)
        {
            Vector3 right = Anchor(station + "RightHand"), left = Anchor(station + "LeftHand");
            StandWorker(actor, Anchor(station + "Worker"), Plan.Right, (right + left) * .5f);
            if (!working) return;
            // The authored work pose keeps feet planted; contact follows the
            // actual bench edge rather than an animation-space guess.
            actor.Apply(VillageResidentAction.StationWork,
                1.1f + .10f * Mathf.Sin((float)Snapshot.Seconds * 1.7f), (right + left) * .5f);
            float weight = Mathf.Min(Ease((float)Snapshot.Seconds / .8f),
                Ease((float)(Snapshot.Duration - Snapshot.Seconds) / .8f));
            Transform spine = workerSpines[actor == workers[1] ? 1 : actor == workers[2] ? 2 : 3];
            spine.rotation = Quaternion.AngleAxis(24f * weight, actor.transform.right) * spine.rotation;
            ApplyCrewContacts(actor, right, left, weight);
        }

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
                ApplyStationWorker(actor, "Packing", true);
                return;
            }
            Vector3 hand = Anchor("RetortHand");
            StandWorker(actor, retort, Plan.Right, hand);
            if (Snapshot.Stage != CityFishSupplyStage.LoadRetort && Snapshot.Stage != CityFishSupplyStage.Cool) return;
            actor.Apply(VillageResidentAction.StationWork, 1.1f, hand);
            float duration = Snapshot.Stage == CityFishSupplyStage.Cool ? (float)Snapshot.Duration - 12 : (float)Snapshot.Duration;
            float weight = Mathf.Min(Ease(seconds / .8f), Ease((duration - seconds) / .8f));
            // The control is on the operator's left; the nearer hand reaches
            // it without crossing the chest or stretching the shoulder.
            ApplyCrewContacts(actor, null, hand, weight);
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
            actor.ApplyLocomotion(trolleyMotion, false, (float)(WorkingSeconds % 120d),
                trolley.position + trolley.forward);
            Transform spine = workerSpines[actor == workers[0] ? 0 : 4];
            spine.rotation = Quaternion.AngleAxis(8f * handWeight, actor.transform.right) * spine.rotation;
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
            ApplyCrewContacts(actor, driverRightHand.position, driverLeftHand.position, hands);
        }

        private void ApplyCrewContacts(VillageResidentPresentation actor, Vector3? right, Vector3? left, float weight)
        {
            bool matches = actor.ApplyHandContacts(right, left, weight);
            WorkerHandsMatch &= matches;
            if (!matches)
                LastCrewContactFailure = actor.name + ": right=" + (right.HasValue
                    ? Vector3.Distance(actor.RightGrip.position, right.Value).ToString("F3") : "n/a") +
                    ", left=" + (left.HasValue ? Vector3.Distance(actor.LeftGrip.position, left.Value).ToString("F3") : "n/a");
        }

        private void StandWorker(VillageResidentPresentation actor, Vector3 point, Vector3 forward, Vector3 look)
        {
            actor.transform.SetPositionAndRotation(point, Quaternion.LookRotation(forward, Vector3.up));
            actor.Apply(VillageResidentAction.Idle, (float)(WorkingSeconds % 120d), look);
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

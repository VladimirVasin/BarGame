using System;
using UnityEngine;

namespace BarPromenade
{
    public sealed partial class CityCanneryController
    {
        private readonly Transform[] shippingPallets = new Transform[CityFishSupplyCycle.HandlingUnits];
        private readonly Quaternion[] shippingPalletRest = new Quaternion[CityFishSupplyCycle.HandlingUnits];
        private readonly Transform[,] cartonFlaps = new Transform[CityFishSupplyCycle.HandlingUnits, 2];
        private readonly Transform[] cartonSeals = new Transform[CityFishSupplyCycle.HandlingUnits];
        private readonly Transform[] cartonRightGrips = new Transform[CityFishSupplyCycle.HandlingUnits];
        private readonly Transform[] cartonLeftGrips = new Transform[CityFishSupplyCycle.HandlingUnits];
        private Vector3[] inspectionInitialRoute, inspectionRepeatRoute, inspectionScaleRoute;
        private readonly Vector3[][] inspectionReadyRoutes = new Vector3[CityFishSupplyCycle.HandlingUnits][];
        private readonly Vector3[][] inspectionClearRoutes = new Vector3[CityFishSupplyCycle.HandlingUnits][];
        private const float ShippingPalletHeight = .172f;
        private const float InspectionCarryHeight = 1.14f;
        private const float InspectionCarryReach = .49f;
        // The longer planted stance keeps a crouched knee beyond the real
        // platform edge (x=1.645), while the shared solver reaches its carton.
        private Vector3 InspectionScaleWorker => Anchor("WeighingWorker") + Plan.Right * .28f;

        public Transform FinishedBox(int index) => cases[index];
        public Vector3 ShippingScaleLoadPosition => Anchor("WeighingLoad");
        public float ShippingScaleWeight { get; private set; }
        public bool InspectionBoxInHands { get; private set; }
        public Vector3 InspectionRightHandTarget { get; private set; }
        public Vector3 InspectionLeftHandTarget { get; private set; }

        private CityCanneryInspectionPlan CreateInspectionRoutes()
        {
            Vector3 P(float x, float z, bool outside = false) => Plan.World(new Vector3(x,
                outside ? CityCanneryPlan.YardTop : CityCanneryPlan.FloorTop, z));
            Vector3 pickup = Anchor("BoxPickupWorker"), rest = Anchor("ShippingRestWorker");
            Vector3 west = P(-7.18f, 5f), north = P(-7.18f, 5.95f);
            Vector3 aisle = P(-2.8f, 5.95f), bend = P(-2.8f, 5f);
            Vector3 door = Anchor("FinishedDoor"), outsideDoor = P(2f, 5f, true);
            // The former indoor scale no longer occupies this receiving bay.
            // Cross the open partition gate before turning, instead of
            // squeezing broad shoulders along the south wall and its return.
            inspectionInitialRoute = new[] { Anchor("Receiver"), P(-3.4f, -5.5f), Anchor("RawDoor"),
                P(2f, -5.5f, true), outsideDoor, door, bend, aisle, north, west, pickup };
            inspectionRepeatRoute = new[] { rest, outsideDoor, door, bend, aisle, north, west, pickup };
            inspectionScaleRoute = new[] { pickup, west, north, aisle, bend, door, outsideDoor, InspectionScaleWorker };
            double readyDuration = 0d, clearDuration = 0d;
            for (int i = 0; i < cases.Length; i++)
            {
                Vector3 target = Anchor("ApprovedWorker" + i);
                Vector3 along = P(2f, Plan.Local(target).z, true);
                inspectionReadyRoutes[i] = new[] { InspectionScaleWorker, outsideDoor, along, target };
                inspectionClearRoutes[i] = new[] { target, along, rest };
                readyDuration = Math.Max(readyDuration, InspectionWalkDuration(inspectionReadyRoutes[i]));
                clearDuration = Math.Max(clearDuration, InspectionWalkDuration(inspectionClearRoutes[i]));
            }
            return new CityCanneryInspectionPlan(InspectionWalkDuration(inspectionInitialRoute),
                InspectionWalkDuration(inspectionRepeatRoute), InspectionWalkDuration(inspectionScaleRoute),
                readyDuration, clearDuration);
        }

        private static double InspectionWalkDuration(Vector3[] path)
        {
            float length = 0f;
            for (int i = 1; i < path.Length; i++) length += Vector3.Distance(path[i - 1], path[i]);
            // The smooth walk peaks at 1.5 times average speed. Carrying stays
            // at an ordinary walking pace, independently of the faster line.
            return Math.Max(2d, length * 1.5d / 1.35d);
        }

        private void CreateInspectionBox(int index)
        {
            Transform box = cases[index];
            shippingPallets[index] = Require(box, "ShippingPallet");
            shippingPalletRest[index] = Quaternion.Inverse(box.rotation) * shippingPallets[index].rotation;
            // Preserve the FBX metre basis while separating its passive support.
            shippingPallets[index].SetParent(transform, true);
            cartonFlaps[index, 0] = Require(box, "MOVE_CartonFlapLeft");
            cartonFlaps[index, 1] = Require(box, "MOVE_CartonFlapRight");
            cartonSeals[index] = Require(box, "CartonSeal");
            cartonRightGrips[index] = Require(box, "ANCHOR_CartonRightGrip");
            cartonLeftGrips[index] = Require(box, "ANCHOR_CartonLeftGrip");
        }

        private void ApplyCartonClosure(int index, float closed)
        {
            cartonFlaps[index, 0].localRotation = Quaternion.AngleAxis(110f * (1f - closed), Vector3.forward);
            cartonFlaps[index, 1].localRotation = Quaternion.AngleAxis(-110f * (1f - closed), Vector3.forward);
            cartonSeals[index].gameObject.SetActive(closed >= .999f);
        }

        private bool ApplyInspectionReceiver()
        {
            InspectionBoxInHands = false;
            CityCanneryInspectionSnapshot state = Snapshot.Inspection;
            if (!state.IsActive)
            {
                if (state.StoredUnits == 0) return false;
                Vector3 rest = Anchor("ShippingRestWorker");
                StandWorker(workers[0], rest, Plan.Right, ShippingScaleLoadPosition);
                return true;
            }
            factoryDutyBusy[0] = true;
            VillageResidentPresentation actor = workers[0];
            int unit = state.UnitIndex;
            float t = Mathf.Clamp01(state.Progress);
            var stage = state.Stage;
            bool walk = stage == CityCanneryInspectionStage.ApproachToPickup ||
                stage == CityCanneryInspectionStage.CarryToScale || stage == CityCanneryInspectionStage.CarryToReady ||
                stage == CityCanneryInspectionStage.Clear;
            if (walk)
            {
                Vector3[] path = stage == CityCanneryInspectionStage.ApproachToPickup
                    ? (unit == 0 ? inspectionInitialRoute : inspectionRepeatRoute)
                    : stage == CityCanneryInspectionStage.CarryToScale ? inspectionScaleRoute
                    : stage == CityCanneryInspectionStage.CarryToReady ? inspectionReadyRoutes[unit] : inspectionClearRoutes[unit];
                Vector3 from = stage == CityCanneryInspectionStage.ApproachToPickup
                    ? (unit == 0 ? Plan.Forward : Plan.Right)
                    : stage == CityCanneryInspectionStage.CarryToScale ? Plan.Right : -Plan.Right;
                Vector3 to = stage == CityCanneryInspectionStage.ApproachToPickup || stage == CityCanneryInspectionStage.Clear
                    ? Plan.Right : -Plan.Right;
                WalkWorker(actor, t, (float)state.Duration, from, to, false, path);
                Vector3 point = actor.transform.position;
                point.y = InspectionGroundHeight(point);
                actor.transform.position = point;
                FitReceiverGoodsRampFeet(actor);
            }
            else
            {
                Vector3 point = stage == CityCanneryInspectionStage.Pickup ? Anchor("BoxPickupWorker")
                    : stage == CityCanneryInspectionStage.PutAway ? Anchor("ApprovedWorker" + unit) : InspectionScaleWorker;
                StandWorker(actor, point, stage == CityCanneryInspectionStage.Pickup ? Plan.Right : -Plan.Right,
                    stage == CityCanneryInspectionStage.Pickup ? Anchor("PackingBox") : Anchor("WeighingDial"));
            }

            Transform box = cases[unit];
            Quaternion carryRotation = actor.transform.rotation * Quaternion.Euler(0f, -90f, 0f);
            Vector3 carry = actor.transform.position + actor.transform.forward * InspectionCarryReach + Vector3.up * InspectionCarryHeight;
            Vector3 supported = stage == CityCanneryInspectionStage.Pickup ? Anchor("PackingBox")
                : stage == CityCanneryInspectionStage.PutAway ? ReadyStore(unit) + Vector3.up * ShippingPalletHeight
                : ShippingScaleLoadPosition;
            Quaternion supportRotation = stage == CityCanneryInspectionStage.Pickup ? Plan.Rotation : Plan.Rotation * Quaternion.Euler(0, 180f, 0);
            float hands = 0f, lean = 0f;
            if (stage == CityCanneryInspectionStage.Pickup || stage == CityCanneryInspectionStage.Lift)
            {
                float lift = Ease((t - .25f) / .60f);
                box.SetPositionAndRotation(Vector3.Lerp(supported, carry, lift), Quaternion.Slerp(supportRotation, carryRotation, lift));
                hands = Ease(t / .20f);
                lean = Mathf.Lerp(stage == CityCanneryInspectionStage.Pickup ? 28f : 48f, 8f, lift) * hands;
                if (stage == CityCanneryInspectionStage.Lift) ShippingScaleWeight = 1f - Ease((t - .25f) / .08f);
            }
            else if (stage == CityCanneryInspectionStage.CarryToScale || stage == CityCanneryInspectionStage.CarryToReady)
            {
                box.SetPositionAndRotation(carry, carryRotation);
                hands = 1f;
                lean = 8f;
            }
            else if (stage == CityCanneryInspectionStage.SetDown || stage == CityCanneryInspectionStage.PutAway)
            {
                float lower = Ease(t / .70f);
                box.SetPositionAndRotation(Vector3.Lerp(carry, supported, lower), Quaternion.Slerp(carryRotation, supportRotation, lower));
                hands = 1f - Ease((t - .76f) / .24f);
                lean = Mathf.Lerp(8f, 48f, lower) * hands;
                if (stage == CityCanneryInspectionStage.SetDown) ShippingScaleWeight = Ease((t - .68f) / .05f);
            }
            else if (stage == CityCanneryInspectionStage.Settle || stage == CityCanneryInspectionStage.Approve)
            {
                box.SetPositionAndRotation(ShippingScaleLoadPosition, supportRotation);
                ShippingScaleWeight = 1f;
            }

            // Bend through the spine and lower the pelvis while keeping both
            // planted feet at the authored ground. The resident's shared limb
            // solver owns the actual contacts, just as it does at the machines.
            if (hands > 0f)
            {
                if (!walk) BlendFactoryWorkPose(CanneryReceiverPresentation.WorkerSlot, hands);
                workerSpines[0].rotation = Quaternion.AngleAxis(lean, actor.transform.right) * workerSpines[0].rotation;
                InspectionRightHandTarget = cartonRightGrips[unit].position;
                InspectionLeftHandTarget = cartonLeftGrips[unit].position;
                // A half turn swaps which end is on the worker's right. Assign
                // hands by the actual body basis rather than the carton name.
                if (Vector3.Dot(InspectionRightHandTarget - InspectionLeftHandTarget, actor.transform.right) < 0f)
                {
                    Vector3 swap = InspectionRightHandTarget;
                    InspectionRightHandTarget = InspectionLeftHandTarget;
                    InspectionLeftHandTarget = swap;
                }
                OrientReceiverCartonHands(hands);
                FitCrewContactStance(actor, 0, InspectionRightHandTarget, InspectionLeftHandTarget, hands);
                ApplyCrewContacts(actor, InspectionRightHandTarget, InspectionLeftHandTarget, hands);
                InspectionBoxInHands = hands >= .999f;
                ApplyCrewLook(actor, box.position + Vector3.up * .16f, .55f);
            }
            else if (stage == CityCanneryInspectionStage.Approve)
            {
                ApplyCrewLook(actor, Anchor("WeighingDial"));
                float nod = 13f * Mathf.Sin(t * Mathf.PI) * Mathf.Sin(t * Mathf.PI);
                actor.Head.rotation = Quaternion.AngleAxis(nod, actor.transform.right) * actor.Head.rotation;
            }

            float needle = ShippingScaleWeight;
            if (stage == CityCanneryInspectionStage.Settle)
                needle *= 1f + .09f * Mathf.Sin((float)state.Seconds * 11f) * Mathf.Exp(-(float)state.Seconds * 2.8f);
            receivingNeedle.localRotation = receivingNeedleRest * Quaternion.AngleAxis(-68f * needle, Vector3.right);
            return true;
        }

        private bool DetectInspectionObstacle()
        {
            var stage = Snapshot.Inspection.Stage;
            if (hero == null || !FactoryPresentationActive || workers == null ||
                (stage != CityCanneryInspectionStage.ApproachToPickup && stage != CityCanneryInspectionStage.CarryToScale &&
                 stage != CityCanneryInspectionStage.CarryToReady && stage != CityCanneryInspectionStage.Clear)) return false;
            Vector3 delta = hero.position - workers[0].transform.position;
            bool blocked = Mathf.Abs(delta.y) < 1.6f && new Vector2(delta.x, delta.z).sqrMagnitude < .70f * .70f;
            if (blocked) LastObstacleName = hero.name;
            return blocked;
        }

        private float InspectionGroundHeight(Vector3 point)
        {
            Vector3 local = Plan.Local(point);
            // Both real goods ramps rise from yard .08 to floor .18. Start
            // lifting the leading sole before the body centre reaches x=0.
            if (local.x >= -.15f && local.x <= 1.5f &&
                (Mathf.Abs(local.z + 5.5f) <= 1f || Mathf.Abs(local.z - 5f) <= 1.1f))
                return Plan.Origin.y + Mathf.Lerp(CityCanneryPlan.FloorTop, CityCanneryPlan.YardTop,
                    Mathf.Clamp01(local.x / 1.5f));
            return HandlingGroundHeight(point);
        }

        private void FitReceiverGoodsRampFeet(VillageResidentPresentation actor)
        {
            // Route segments carry endpoint elevations; their chord must not
            // pitch the whole person. The two soles fit the actual 1:15 plane
            // independently, preserving the authored swing height and stride.
            Vector3 heading = Vector3.ProjectOnPlane(actor.transform.forward, Vector3.up);
            if (heading.sqrMagnitude > .0001f) actor.transform.rotation = Quaternion.LookRotation(heading, Vector3.up);
            Vector3 local = Plan.Local(actor.transform.position);
            if (local.x < -.5f || local.x > 2f ||
                Mathf.Abs(local.z + 5.5f) > 1.2f && Mathf.Abs(local.z - 5f) > 1.3f) return;
            for (int side = 0; side < 2; side++)
            {
                Transform thigh = factoryPoseBones[0, 11 + side * 3];
                Transform shin = factoryPoseBones[0, 12 + side * 3];
                Transform foot = factoryPoseBones[0, 13 + side * 3];
                Vector3 groundPoint = new Vector3(foot.position.x, actor.transform.position.y, foot.position.z);
                Vector3 footLocal = Plan.Local(groundPoint);
                float slope = Ease((footLocal.x + .3f) / .3f) * Ease((1.8f - footLocal.x) / .3f);
                if (slope <= 0f) continue;
                Quaternion tilt = Quaternion.FromToRotation(Vector3.up, (Vector3.up + Plan.Right * (slope / 15f)).normalized);
                Vector3 ankleAboveGround = foot.position - groundPoint;
                groundPoint.y = InspectionGroundHeight(groundPoint) + .0015f * slope;
                Vector3 target = groundPoint + tilt * ankleAboveGround;
                LimbTwoBoneIk.Solve(thigh, shin, foot, target, tilt * foot.rotation,
                    shin.position + actor.transform.forward * .05f, 1f, 1f, true);
            }
        }
    }
}

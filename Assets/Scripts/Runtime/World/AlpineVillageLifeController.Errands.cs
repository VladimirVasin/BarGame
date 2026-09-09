using System;
using UnityEngine;

namespace BarPromenade
{
    public sealed partial class AlpineVillageLifeController
    {
        private bool gateHelpReserved, gateHelperHolding;
        private bool playerShovelReserved;
        private Transform bucket, bucketWater;
        private bool bucketFilled;
        public VillageErrandPlan Errands { get; private set; }
        public VillageSnowClearing SnowClearing { get; private set; }
        public VillageSnowClearing Clearing => SnowClearing;
        public Vector3 ShovelRestPosition => shovelRestPosition;
        public Quaternion ShovelRestRotation => shovelRestRotation;
        public bool CanReserveShovel
        {
            get
            {
                if (playerShovelReserved || Shovel == null || Vector3.Distance(Shovel.position, shovelRestPosition) > .025f) return false;
                foreach (var n in neighbours)
                {
                    if (n.ShovelHeld) return false;
                    foreach (var step in n.Steps) if (step.Kind == VillageNeighbourTask.PickShovel) return false;
                }
                return true;
            }
        }
        public bool ReserveShovel(bool reserve)
        {
            if (!reserve) { playerShovelReserved = false; return true; }
            if (!CanReserveShovel) return false;
            playerShovelReserved = true; return true;
        }
        public Transform Bucket => bucket;
        public bool BucketFilled => bucketFilled;
        public Transform Gate => gate;
        public Transform GateFrame => gateFrame;
        public float GateFraction => gateFraction;
        public int GatePassages { get; private set; }
        public bool GateHelpReserved => gateHelpReserved;
        public VillageNeighbourState GateVisitor => neighbours.Find(n => n.Role == VillageResidentRole.BasketVisitor);
        public bool CanReserveGateHelp => !gateHelpReserved && gateFraction > .95f &&
            HasImminentBasketGatePassage(GateVisitor);

        private bool HasImminentBasketGatePassage(VillageNeighbourState visitor)
        {
            if (visitor == null || !visitor.IsOutside || gateFrame == null || visitor.Steps.Count == 0) return false;
            bool picking = visitor.Task == VillageNeighbourTask.PickBasket;
            if (!picking && (visitor.Task != VillageNeighbourTask.Walk || !visitor.BasketHeld)) return false;
            Vector3 before = gateFrame.InverseTransformPoint(visitor.Actor.transform.position);
            float remainingDistance = 0f;
            bool current = true;
            foreach (var step in visitor.Steps)
            {
                // The current pickup may lead straight into a carried passage.
                // A later pickup, wait or placement belongs to a different visit.
                if (current && picking) { current = false; continue; }
                if (step.Kind != VillageNeighbourTask.Walk || step.Indoors || step.Points == null) return false;
                int firstPoint = current ? visitor.PointIndex : 0;
                current = false;
                for (int i = firstPoint; i < step.Points.Length; i++)
                {
                    Vector3 after = gateFrame.InverseTransformPoint(step.Points[i]);
                    float length = new Vector2(after.x - before.x, after.z - before.z).magnitude;
                    if (before.z * after.z < 0f)
                    {
                        float fraction = -before.z / (after.z - before.z);
                        float across = Mathf.Lerp(before.x, after.x, fraction);
                        if (across > .05f && across < 1.03f && remainingDistance + length * fraction <= 3f)
                            return true;
                    }
                    remainingDistance += length;
                    if (remainingDistance >= 3f) return false;
                    before = after;
                }
            }
            return false;
        }

        public bool ReserveGateHelp(bool reserve)
        {
            if (!reserve) { gateHelpReserved = false; gateHelperHolding = false; return true; }
            if (!CanReserveGateHelp) return false;
            gateHelpReserved = true; gateHelperHolding = false;
            return true;
        }
        public void SetGateHelperHolding(bool holding) { if (gateHelpReserved) gateHelperHolding = holding; }
        public void ApplyGateHelpFraction(float fraction)
        {
            if (!gateHelpReserved) return;
            gateFraction = Mathf.Clamp01(fraction);
            gate.localRotation = Quaternion.Euler(0f, 92f * gateFraction, 0f);
        }
        private bool PauseForGatePreparation(VillageNeighbourState n)
        {
            if (!gateHelpReserved || gateHelperHolding || n.Role != VillageResidentRole.BasketVisitor) return false;
            if (n.Task == VillageNeighbourTask.PickBasket)
            {
                n.Actor.Apply(VillageResidentAction.Reach, n.Time);
                if (n.BasketHeld) FollowClosedBasket(n);
            }
            else SampleStoppedResident(n);
            n.IsBlocked = true; n.IsYielding = true;
            return true;
        }
        private void ObserveGatePassage(VillageNeighbourState n, Vector3 before, Vector3 after)
        {
            if (!n.BasketHeld || n.Role != VillageResidentRole.BasketVisitor) return;
            Vector3 a = gateFrame.InverseTransformPoint(before), b = gateFrame.InverseTransformPoint(after);
            if (a.z * b.z <= 0f && Mathf.Abs(a.z - b.z) > .00001f && b.x > .05f && b.x < 1.03f) GatePassages++;
        }

        private void InitializeErrands()
        {
            Errands = new VillageErrandPlan(Plan, neighbourhood);
            SnowClearing = new VillageSnowClearing(snowTreading, Errands.SnowPatches);
            Place(VillageLifePropKind.BasketStand, Errands.BucketSupport,
                Quaternion.LookRotation(neighbourhood.QuietHouse.Facing), true);
            bucket = VillageErrandPropLibrary.Create(transform);
            bucket.SetPositionAndRotation(Errands.BucketSupport + Vector3.up * AlpineVillageLifePlan.StandHeight,
                Quaternion.LookRotation(-neighbourhood.QuietHouse.Facing));
            bucketWater = bucket.Find("Water");
            bucketFilled = GameSessionState.VillageHousehold.WaterFetched;
            bucketWater.gameObject.SetActive(bucketFilled);
        }

        private bool TryBuildErrand(VillageNeighbourState n)
        {
            if (n.CompletedOutings < 1) return false;
            if (n.Role == VillageResidentRole.BasketVisitor && !GameSessionState.VillageHousehold.WaterFetched)
            { BuildWaterVisit(n); return true; }
            if (n.Role != VillageResidentRole.SnowNeighbor || !SnowClearing.IsComplete(Errands.PorchPatch.StableId)) return false;
            if (!SnowClearing.IsComplete(Errands.ChapelPatch.StableId)) { BuildDistantClearing(n, Errands.ChapelPatch, false); return true; }
            if (!SnowClearing.IsComplete(Errands.StationPatch.StableId)) { BuildDistantClearing(n, Errands.StationPatch, true); return true; }
            return false;
        }

        private void QueueClearing(VillageNeighbourState n, VillageSnowPatch patch)
        {
            for (int stage = SnowClearing.Stage(patch.StableId); stage < VillageHouseholdProgress.ClearingStageCount; stage++)
                n.Steps.Enqueue(new VillageNeighbourStep { Kind = VillageNeighbourTask.Shovel, Duration = 4f,
                    ClearingId = patch.StableId, ClearingStage = stage });
        }

        private void BuildDistantClearing(VillageNeighbourState n, VillageSnowPatch patch, bool station)
        {
            var home = neighbourhood.QuietHouse;
            Walk(n, new[] { n.Door.ExteriorDock, neighbourhood.Yard(home, 0f, 2.3f), neighbourhood.Yard(home, 2.1f, 2.3f), neighbourhood.ShovelDock }, -home.Facing);
            Add(n, VillageNeighbourTask.PickShovel, 3f);
            Walk(n, new[] { neighbourhood.ShovelDock, neighbourhood.Yard(home, 2.1f, 2.3f), neighbourhood.Yard(home, 0f, 2.3f), home.DoorDockPosition,
                Plan.Village.Lane.Sample(home.LaneDistance).Position }, -home.Facing);
            float distance = station ? 0f : Errands.Chapel.LaneDistance;
            WalkStreet(n, home.LaneDistance, distance);
            if (!station) Walk(n, Errands.ChapelPath, -Errands.Chapel.Facing);
            Vector3 arrival = station ? Plan.Village.Lane.Start : Errands.ChapelPath[Errands.ChapelPath.Length - 1];
            Walk(n, new[] { arrival, patch.Dock }, patch.Facing);
            QueueClearing(n, patch);
            Walk(n, new[] { patch.Dock, arrival }, station ? Plan.StationForward : Errands.Chapel.Facing);
            if (!station) Walk(n, Reverse(Errands.ChapelPath), Errands.Chapel.Facing);
            WalkStreet(n, distance, home.LaneDistance);
            Walk(n, new[] { Plan.Village.Lane.Sample(home.LaneDistance).Position, home.DoorDockPosition,
                neighbourhood.Yard(home, 0f, 2.3f), neighbourhood.Yard(home, 2.1f, 2.3f), neighbourhood.ShovelDock }, -home.Facing);
            Add(n, VillageNeighbourTask.PutShovel, 3f);
            Walk(n, new[] { neighbourhood.ShovelDock, neighbourhood.Yard(home, 2.1f, 2.3f), neighbourhood.Yard(home, 0f, 2.3f), n.Door.ExteriorDock }, -home.Facing);
        }

        private void BuildWaterVisit(VillageNeighbourState n)
        {
            var home = neighbourhood.QuietHouse;
            Walk(n, new[] { n.Door.ExteriorDock, neighbourhood.Yard(home, 0f, 2.2f), neighbourhood.Yard(home, -.70f, 2.2f), Errands.BucketDock }, -home.Facing);
            Add(n, VillageNeighbourTask.PickBucket, 3f);
            Walk(n, new[] { Errands.BucketDock, neighbourhood.Yard(home, -.70f, 2.2f), neighbourhood.Yard(home, 0f, 2.2f), home.DoorDockPosition,
                Plan.Village.Lane.Sample(home.LaneDistance).Position }, home.Facing);
            WalkStreet(n, home.LaneDistance, AlpineVillagePathPlanner.SpringBypassLaneDistance);
            Walk(n, Errands.SpringPath, Errands.SpringFacing);
            Walk(n, new[] { Errands.SpringPath[Errands.SpringPath.Length - 1], Errands.SpringDock }, Errands.SpringFacing);
            Add(n, VillageNeighbourTask.FillBucket, 8f);
            Walk(n, new[] { Errands.SpringDock, Errands.SpringPath[Errands.SpringPath.Length - 1] }, -Errands.SpringFacing);
            Walk(n, Reverse(Errands.SpringPath), -Errands.SpringFacing);
            WalkStreet(n, AlpineVillagePathPlanner.SpringBypassLaneDistance, home.LaneDistance);
            Walk(n, new[] { Plan.Village.Lane.Sample(home.LaneDistance).Position, home.DoorDockPosition,
                neighbourhood.Yard(home, 0f, 2.2f), neighbourhood.Yard(home, -.70f, 2.2f), Errands.BucketDock }, -home.Facing);
            Add(n, VillageNeighbourTask.PutBucket, 3f);
            Add(n, VillageNeighbourTask.FinishWaterVisit);
            Walk(n, new[] { Errands.BucketDock, neighbourhood.Yard(home, -.70f, 2.2f), neighbourhood.Yard(home, 0f, 2.2f), n.Door.ExteriorDock }, -home.Facing);
        }
        private static Vector3[] Reverse(Vector3[] points)
        { var result = (Vector3[])points.Clone(); Array.Reverse(result); return result; }

        private void FollowBucket(VillageNeighbourState n, float supportWeight = 0f)
        {
            Vector3 left = n.Actor.LeftGrip.position, right = n.Actor.RightGrip.position;
            Quaternion rotation = Quaternion.LookRotation(Vector3.Cross((right - left).normalized, Vector3.up));
            Vector3 position = (right + left) * .5f - Vector3.up * .43f;
            if (supportWeight > 0f)
            {
                Vector3 support = Errands.BucketSupport + Vector3.up * AlpineVillageLifePlan.StandHeight;
                Vector3 authoredRest = n.Actor.transform.TransformPoint(VillageResidentPresentation.RestBasketLocalPosition);
                position += (support - authoredRest) * supportWeight;
                rotation = Quaternion.Slerp(rotation, Quaternion.LookRotation(-neighbourhood.QuietHouse.Facing), supportWeight);
                if (supportWeight >= .9999f) position = support;
            }
            bucket.SetPositionAndRotation(position, rotation);
            n.Actor.ApplyHandContacts(bucket.TransformPoint(new Vector3(.29f, .43f, 0f)), bucket.TransformPoint(new Vector3(-.29f, .43f, 0f)));
        }

        private void OffsetBucketReachToSupport(VillageNeighbourState n, float weight)
        {
            // A walking dock has a small arrival tolerance. Blend only its
            // difference from the authored support into the existing reach.
            Vector3 right = new Vector3(.29f, .43f, 0f), left = new Vector3(-.29f, .43f, 0f);
            Vector3 rest = VillageResidentPresentation.RestBasketLocalPosition;
            Vector3 rightOffset = bucket.TransformPoint(right) - n.Actor.transform.TransformPoint(rest + right);
            Vector3 leftOffset = bucket.TransformPoint(left) - n.Actor.transform.TransformPoint(rest + left);
            n.Actor.ApplyHandContacts(n.Actor.RightGrip.position + rightOffset * weight,
                n.Actor.LeftGrip.position + leftOffset * weight);
        }

        private bool AdvanceErrandTask(VillageNeighbourState n, VillageNeighbourStep step, float dt)
        {
            if (step.Kind == VillageNeighbourTask.PickBucket || step.Kind == VillageNeighbourTask.PutBucket)
            {
                bool picking = step.Kind == VillageNeighbourTask.PickBucket;
                n.Actor.Apply(picking ? VillageResidentAction.Reach : VillageResidentAction.Place, n.Time);
                if (picking && n.Time >= 1.5f) n.BucketHeld = true;
                if (n.BucketHeld)
                    FollowBucket(n, picking ? 1f - Mathf.SmoothStep(0f, 1f, Mathf.Clamp01((n.Time - 1.5f) / 1.5f)) :
                        Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(n.Time / 1.5f)));
                else OffsetBucketReachToSupport(n, picking ? Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(n.Time / 1.5f)) :
                    1f - Mathf.SmoothStep(0f, 1f, Mathf.Clamp01((n.Time - 1.5f) / 1.5f)));
                if (!picking && n.Time >= 1.5f && !n.ContactDone)
                { n.BucketHeld = false; n.ContactDone = true; sound.PlayWood(bucket.position); }
                if (n.Time >= step.Duration) FinishStep(n);
                return true;
            }
            if (step.Kind == VillageNeighbourTask.FillBucket)
            {
                n.Actor.Apply(VillageResidentAction.BucketFill, n.Time);
                Pose pose = VillageResidentPresentation.SampleBucketPose(n.Time, out float dipWeight);
                Vector3 authoredDip = n.Actor.transform.position + n.Actor.transform.rotation * new Vector3(0f, .265f, 1.194f);
                bucket.SetPositionAndRotation(n.Actor.transform.position + n.Actor.transform.rotation * pose.position +
                    (Errands.BucketDip - authoredDip) * dipWeight, n.Actor.transform.rotation * pose.rotation);
                n.Actor.ApplyHandContacts(bucket.TransformPoint(new Vector3(.29f, .43f, 0f)), bucket.TransformPoint(new Vector3(-.29f, .43f, 0f)));
                if (n.Time >= 4f && !bucketFilled)
                { bucketFilled = true; }
                // The retained shallow charge is visible once the mouth has
                // risen and the pail is upright again; no replacement vessel.
                bucketWater.gameObject.SetActive(bucketFilled && n.Time >= 6.5f);
                if (n.Time >= step.Duration) FinishStep(n);
                return true;
            }
            if (step.Kind == VillageNeighbourTask.FinishWaterVisit)
            {
                if (bucketFilled) GameSessionState.VillageHousehold.TryCompleteWaterFetch(GameSessionState.VillageHousehold.CompletedWaterVisits);
                FinishStep(n); return true;
            }
            return false;
        }
    }
}

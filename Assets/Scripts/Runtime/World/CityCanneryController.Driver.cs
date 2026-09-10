using UnityEngine;

namespace BarPromenade
{
    public sealed partial class CityCanneryController
    {
        private CityPortConversationController portConversation;
        private const float ReverseDoorMaximumAngle = 24f;
        public bool IsReversing => Snapshot.IsDriving && TruckPose(Snapshot).Reversing;
        public float DriverReverseLean { get; private set; }
        public Vector3 DriverSteeringContact { get; private set; }
        public Vector3 DriverDoorContact { get; private set; }
        public Vector3 DriverRearLookTarget { get; private set; }
        private float ReverseDoorWeight => Mathf.Min(Ease((float)Snapshot.Seconds / 1.2f),
            Ease((float)(Snapshot.Duration - Snapshot.Seconds) / 1.2f));

        private void ApplyReversingDriver()
        {
            VillageResidentPresentation actor = workers[4];
            // Open before leaning; withdraw before the door comes back. The
            // same seated pelvis, legs and rig remain in the cab throughout.
            float lean = Mathf.Min(Ease(((float)Snapshot.Seconds - .4f) / .9f),
                Ease(((float)(Snapshot.Duration - Snapshot.Seconds) - .4f) / .9f));
            DriverReverseLean = lean;
            actor.transform.SetPositionAndRotation(Truck.position, Truck.rotation);
            actor.Apply(VillageResidentAction.Idle, 0f);
            Vector3 seat = driverSeat.position + (-Truck.right * .12f + Truck.forward * .06f) * lean;
            actor.transform.position += seat - driverPelvis.position;
            driverPelvis.rotation = Quaternion.AngleAxis(12f * lean, Truck.forward) * driverPelvis.rotation;
            Transform spine = workerSpines[4];
            spine.rotation = Quaternion.AngleAxis(46f * lean, Truck.forward) *
                Quaternion.AngleAxis(-48f * lean, Truck.up) * spine.rotation;
            for (int i = 0; i < 2; i++)
            {
                Vector3 pedal = driverFoot.position + Truck.right * (i == 0 ? -.14f : .14f);
                LimbTwoBoneIk.Solve(driverThighs[i], driverShins[i], driverFeet[i], pedal,
                    driverFeet[i].rotation, driverThighs[i].position + Truck.forward * .65f,
                    1f, .995f, true);
                DriverSeatedContactsMatch &= Vector3.Distance(driverFeet[i].position, pedal) <= .025f;
            }
            DriverSeatedContactsMatch &= Vector3.Distance(driverPelvis.position, seat) <= .01f;

            Vector3 wheelCentre = (driverRightHand.position + driverLeftHand.position) * .5f;
            Vector3 wheelAxis = Truck.TransformDirection(new Vector3(0, .8f, .6f));
            // Grip the near portion of the authored 23 cm oblique ring when
            // leaning, instead of stretching across it to the forward grip.
            Vector3 nearRim = Vector3.ProjectOnPlane(driverSteeringShoulder.position - wheelCentre, wheelAxis).normalized * .23f;
            Quaternion steering = Quaternion.AngleAxis(TruckSteeringAngle * .35f, wheelAxis);
            DriverSteeringContact = Vector3.Lerp(driverRightHand.position, wheelCentre + steering * nearRim, lean);
            // Hold the inner upper lip of the partly opened door while the
            // other hand keeps correcting the steering. The grip follows the
            // actual leaf all the way through opening and closing.
            DriverDoorContact = driverDoor.TransformPoint(new Vector3(.08f, .67f, -1.18f));
            Vector3 left = Vector3.Lerp(driverLeftHand.position, DriverDoorContact, ReverseDoorWeight);
            ApplyCrewContacts(actor, DriverSteeringContact, left, 1f);

            DriverRearLookTarget = Truck.TransformPoint(new Vector3(-CityCanneryTruckDimensions.HalfWidth-1f,
                1.9f, CityCanneryTruckDimensions.Rear-5.4f));
            // Resolve from the already twisted skull, not the forward-facing
            // actor root, so he can actually look past the rear corner.
            Vector3 headForward = driverMouth.up;
            Vector3 wanted = DriverRearLookTarget - actor.Head.position;
            float yaw = Mathf.Clamp(Vector3.SignedAngle(Vector3.ProjectOnPlane(headForward, Truck.up),
                Vector3.ProjectOnPlane(wanted, Truck.up), Truck.up), -88f, 88f);
            actor.Head.rotation = Quaternion.AngleAxis(yaw * lean, Truck.up) * actor.Head.rotation;
        }

        private void ApplyDriverConversation()
        {
            if (!IsReversing) DriverReverseLean = 0;
            if (portConversation == null) return;
            bool present = TruckPresentationActive && Snapshot.Stage == CityFishSupplyStage.LoadFish;
            bool onFoot = present && Snapshot.Seconds >= 3.5d && Snapshot.Seconds < Snapshot.Duration - 3.5d;
            double edgeSeconds = System.Math.Min(Snapshot.Seconds, Snapshot.Duration - Snapshot.Seconds);
            bool handsFree = onFoot && !handlingActive && edgeSeconds < TrolleyHandleArrival;
            portConversation.SetDriverState(present, onFoot, handsFree, onFoot,
                present && Snapshot.Seconds < TransferEdge,
                present && Snapshot.Seconds >= Snapshot.Duration - TransferEdge);
            portConversation.SetStoreAccessWait(DriverWaitingForDockWorker ? CityPortConversationCatalog.DriverRole : -1,
                WorkingSeconds,Snapshot.Batch*CityFishSupplyCycle.HandlingUnits+Snapshot.Handled);
            portConversation.ApplyDriverSocialPose();
        }
    }
}

using System;
using UnityEngine;

namespace BarPromenade
{
    public sealed partial class CityCanneryController
    {
        public const float DriverWheelRadius = .23f;
        public const float DriverWheelTubeRadius = .022f;
        private NpcHandPose driverHandPose;
        public Vector3 DriverWheelCentre => (driverRightHand.position + driverLeftHand.position) * .5f;
        public Vector3 DriverWheelAxis => Truck.TransformDirection(new Vector3(0f, .8f, .6f));
        public float DriverSteeringPalmAlignment { get; private set; } = 1f;
        public float DriverSteeringGripAxisAlignment { get; private set; } = 1f;

        private Vector3 DriverWheelRimContact(Vector3 hint)
        {
            Vector3 radial = Vector3.ProjectOnPlane(hint - DriverWheelCentre, DriverWheelAxis).normalized;
            if (radial.sqrMagnitude < .9f) throw new InvalidOperationException("A steering grip needs a point on the rim.");
            return DriverWheelCentre + radial * DriverWheelRadius;
        }

        private Vector3 DriverWheelGripAxis(Vector3 contact, bool isLeft)
        {
            Vector3 radial = Vector3.ProjectOnPlane(contact - DriverWheelCentre, DriverWheelAxis).normalized;
            // Authored axis anchors point across the palm toward the thumb;
            // both contact frames run toward the upper portion of the rim.
            return Vector3.Cross(DriverWheelAxis, radial).normalized * (isLeft ? -1f : 1f);
        }

        private void ApplyDriverWheelContacts(Vector3 right, Vector3 left, float weight, float leftWheelWeight = 1f)
        {
            if (driverHandPose == null) driverHandPose = workers[4].GetComponent<NpcHandPose>();
            if (driverHandPose == null || Mathf.Abs(driverHandPose.CylinderRadius - DriverWheelTubeRadius) > .0005f)
                throw new InvalidOperationException("The delivery driver requires an authored grip matching the steering-rim tube.");
            weight = Mathf.Clamp01(weight); leftWheelWeight = Mathf.Clamp01(leftWheelWeight);
            Vector3 rightCentre = DriverWheelRimContact(right);
            Vector3 leftCentre = DriverWheelRimContact(driverLeftHand.position);
            Vector3 rightAxis = DriverWheelGripAxis(rightCentre, false);
            Vector3 leftAxis = DriverWheelGripAxis(leftCentre, true);
            Pose rightPose = driverHandPose.GetSocketPose(false, rightCentre, rightAxis, DriverWheelAxis);
            Pose leftPose = driverHandPose.GetSocketPose(true, leftCentre, leftAxis, DriverWheelAxis);
            Quaternion rightRotation = Quaternion.Slerp(driverTrolleyHands[1].rotation, rightPose.rotation, weight);
            Quaternion leftRotation = Quaternion.Slerp(driverTrolleyHands[0].rotation, leftPose.rotation, weight * leftWheelWeight);
            // The source grip centre, not the old socket inside the ring,
            // meets the real tube. Releasing the left hand blends to the door.
            Vector3 leftTarget = Vector3.Lerp(left, leftPose.position, leftWheelWeight);
            ApplyCrewContacts(workers[4], rightPose.position, leftTarget, weight, weight, rightRotation, leftRotation);
            driverHandPose.SetGrip(false, weight);
            driverHandPose.SetGrip(true, weight * leftWheelWeight);
            DriverSteeringContact = rightCentre;

            DriverSteeringPalmAlignment = Vector3.Dot(driverHandPose.PalmNormal(false), DriverWheelAxis);
            DriverSteeringGripAxisAlignment = Vector3.Dot(driverHandPose.CylinderAxis(false), rightAxis);
            bool centresMatch = Vector3.Distance(driverHandPose.CylinderCentre(false), rightCentre) <= .025f;
            if (leftWheelWeight >= .999f)
            {
                DriverSteeringPalmAlignment = Mathf.Min(DriverSteeringPalmAlignment,
                    Vector3.Dot(driverHandPose.PalmNormal(true), DriverWheelAxis));
                DriverSteeringGripAxisAlignment = Mathf.Min(DriverSteeringGripAxisAlignment,
                    Vector3.Dot(driverHandPose.CylinderAxis(true), leftAxis));
                centresMatch &= Vector3.Distance(driverHandPose.CylinderCentre(true), leftCentre) <= .025f;
            }
            if (weight >= .999f)
            {
                bool matches = centresMatch && DriverSteeringPalmAlignment > .98f && DriverSteeringGripAxisAlignment > .98f;
                DriverSeatedContactsMatch &= matches;
                if (!matches) LastCrewContactFailure = "Driver cylinder grip: centre=" + centresMatch +
                    ", palm=" + DriverSteeringPalmAlignment.ToString("F3") + ", axis=" + DriverSteeringGripAxisAlignment.ToString("F3");
            }
        }
    }
}

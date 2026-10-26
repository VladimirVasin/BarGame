using UnityEngine;

namespace BarPromenade
{
    public sealed partial class BarCrowdController
    {
        /// <summary>Samples the existing body graph first, then solves only this trip's visible contacts.</summary>
        public void RefreshWashroomPose()
        {
            WashroomVisit current = washroomVisit;
            if (current == null || current.Patron.Registry == null) return;
            current.Contacts.RestoreBasePose();
            WashroomHandContactError = 0f;
            bool releasing = current.Phase == WashroomPhase.ReleasingDrink;
            bool standing = current.Phase == WashroomPhase.Standing;
            bool sitting = current.Phase == WashroomPhase.Sitting;
            Transit transition = current.Transition;
            if (releasing || standing || sitting)
            {
                TransitFootContactError = 0f;
                float progress = Mathf.SmoothStep(0f, 1f, current.Seconds / SeatTransitionSeconds);
                float seated = releasing ? 1f : sitting ? progress : 1f - progress;
                Transform body = current.Patron.Registry.transform;
                body.localPosition = Vector3.Lerp(transition.Dock, current.Patron.Anchor.Position, seated);
                body.localRotation = Quaternion.Euler(0f, current.Patron.Anchor.YawDegrees, 0f);
                transition.Pose.Apply(seated);
                ApplySeatFootContacts(transition, seated);
                transition.Fingers?.SetGrip(false, releasing
                    ? 1f - Mathf.SmoothStep(0f, 1f, current.Seconds / ReleaseHandSeconds)
                    : sitting ? Mathf.SmoothStep(0f, 1f, current.Seconds / SeatTransitionSeconds) : 0f,
                    current.Patron.Drinking != null ? current.Patron.Drinking.GripKind : NpcHandGripKind.Cylindrical);
                if (releasing && current.Seconds >= ReleaseHandSeconds) transition.ReleaseEndpointShown = true;
                return;
            }
            transition.Fingers?.SetGrip(false, 0f);
            transition.Fingers?.SetGrip(true, 0f);
            switch (current.Phase)
            {
                case WashroomPhase.OpeningForEntry: DoorHand(false, true); break;
                case WashroomPhase.ClosingForVisit: DoorHand(true, false); break;
                case WashroomPhase.OpeningForExit: DoorHand(true, true); break;
                case WashroomPhase.ClosingAfterVisit: DoorHand(false, false); break;
            }
        }

        private void DoorHand(bool inside, bool open)
        {
            WashroomVisit current = washroomVisit;
            if (Mathf.Abs(current.DoorStartFraction - (open ? 1f : 0f)) <= .001f) return;
            Vector3 contact = washroom.Door.GetNpcContact(inside, open);
            // Opening keeps the handle until it starts to leave anatomical
            // reach. Closing is a measured push/release of the real leaf.
            float reach = Mathf.SmoothStep(0f, 1f, current.Seconds / HandReachSeconds);
            float travel = Mathf.Abs(washroom.Door.OpenFraction - current.DoorStartFraction);
            float release = 1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(open ? .22f : .08f,
                open ? .6f : .38f, travel));
            float weight = reach * release;
            current.Contacts.LeanToward(contact, 10f * weight);
            float error = current.Contacts.Palm(contact,
                (inside ? 1f : -1f) * washroom.transform.forward, washroom.transform.up, weight);
            if (weight >= .95f && error <= .06f) current.DoorContactReady = true;
            WashroomHandContactError = weight >= .95f ? error : 0f;
        }

        private sealed class NpcWashroomContactPose
        {
            private readonly Transform actor, spine, chest, upper, forearm, hand;
            private readonly AnatomicalHandFrame anatomy;
            private readonly Transform[] poseBones;
            private readonly Vector3[] basePositions;
            private readonly Quaternion[] baseRotations;

            public NpcWashroomContactPose(CityPedestrianAssetRegistry registry)
            {
                actor = registry.transform;
                spine = CityPedestrianHandProps.FindSocket(registry.ModelRoot, "spine");
                chest = CityPedestrianHandProps.FindSocket(registry.ModelRoot, "chest");
                upper = CityPedestrianHandProps.FindSocket(registry.ModelRoot, "upper_arm.R");
                forearm = CityPedestrianHandProps.FindSocket(registry.ModelRoot, "forearm.R");
                hand = CityPedestrianHandProps.FindSocket(registry.ModelRoot, "hand.R");
                anatomy = registry.GetComponentInChildren<NpcHandPose>().GetAnatomicalFrame(false);
                poseBones = new[] { spine, chest, upper, forearm, hand };
                basePositions = new Vector3[poseBones.Length];
                baseRotations = new Quaternion[poseBones.Length];
                CaptureBasePose();
            }

            public void CaptureBasePose()
            {
                for (int index = 0; index < poseBones.Length; index++)
                {
                    if (poseBones[index] == null) continue;
                    basePositions[index] = poseBones[index].localPosition;
                    baseRotations[index] = poseBones[index].localRotation;
                }
            }

            public void RestoreBasePose()
            {
                for (int index = 0; index < poseBones.Length; index++)
                    if (poseBones[index] != null)
                        poseBones[index].SetLocalPositionAndRotation(basePositions[index], baseRotations[index]);
            }

            public float Palm(Vector3 target, Vector3 palmNormal, Vector3 fingers, float weight)
            {
                Quaternion desired = Quaternion.LookRotation(fingers, palmNormal) *
                    Quaternion.Inverse(Quaternion.LookRotation(anatomy.FingerDirectionInHand, anatomy.PalmNormalInHand));
                Vector3 wrist = target - desired * anatomy.PalmCentreInHand;
                LimbTwoBoneIk.Solve(upper, forearm, hand, wrist, desired,
                    upper.position + actor.right * .35f - actor.up * .3f,
                    Mathf.Clamp01(weight), .995f, true);
                return weight >= .95f ? Vector3.Distance(hand.position +
                    hand.rotation * anatomy.PalmCentreInHand, target) : 0f;
            }

            public void LeanToward(Vector3 target, float degrees)
            {
                Vector3 toward = Vector3.ProjectOnPlane(target - chest.position, actor.up);
                if (toward.sqrMagnitude < .00001f) return;
                Vector3 axis = Vector3.Cross(actor.up, toward.normalized);
                spine.rotation = Quaternion.AngleAxis(degrees * .55f, axis) * spine.rotation;
                chest.rotation = Quaternion.AngleAxis(degrees * .45f, axis) * chest.rotation;
            }
        }
    }
}

using System.Collections.Generic;
using UnityEngine;

namespace BarPromenade
{
    public sealed partial class CityCobblerWorker
    {
        public Transform Awl => awl;
        public Transform ShoeContact => leftContact;
        private Transform seatAnchor, floorAnchor, leftContact, rightContact, awl, pelvis, spine, leftHand;
        private NpcHandPose fingers;
        private AnatomicalHandFrame leftPalm;
        private Pose awlWorking, awlRest;
        private Vector3 toolAxis;
        private readonly Transform[] feet = new Transform[2];
        private readonly float[] ankleHeights = new float[2];
        private readonly Quaternion[] flatFeet = new Quaternion[2];
        private Transform[] joints;
        private Pose[] heldJoints;
        private float seatLift = .09f, leftGripWeight, rightGripWeight;
        private bool held;

        private void ConfigureRig(Transform shell)
        {
            Transform Part(string key) => CityCobblerWorkerPlan.Anchor(shell, key);
            Transform Joint(string key) => CityCobblerWorkerPlan.Anchor(Worker.ModelRoot, key);
            seatAnchor = Part("ANCHOR_Seat");
            floorAnchor = Part("ANCHOR_Feet");
            leftContact = Part("ANCHOR_HandLeft");
            rightContact = Part("ANCHOR_HandRight");
            awl = Part("PIVOT_Awl");
            awlWorking = new Pose(awl.position, awl.rotation);
            pelvis = Joint("pelvis"); spine = Joint("spine"); leftHand = Joint("hand.L");
            fingers = Worker.GetComponent<NpcHandPose>();
            leftPalm = fingers.GetAnatomicalFrame(true);
            var boneSet = new HashSet<Transform>();
            SkinnedMeshRenderer trousers = null;
            for (int i = 0; i < 2; i++)
            {
                feet[i] = Joint(i == 0 ? "foot.L" : "foot.R");
                flatFeet[i] = Quaternion.Inverse(Worker.transform.rotation) * feet[i].rotation;
                ankleHeights[i] = feet[i].position.y - Worker.transform.position.y;
            }
            foreach (Renderer renderer in skins)
            {
                if (!(renderer is SkinnedMeshRenderer skin) || !skin.enabled || !skin.gameObject.activeInHierarchy) continue;
                foreach (var weighted in skin.bones)
                    for (Transform bone = weighted; bone && bone != Worker.ModelRoot && bone != Worker.transform; bone = bone.parent)
                        boneSet.Add(bone);
                if (skin.name.StartsWith("CLO_Trousers_", System.StringComparison.Ordinal) && skin.name.EndsWith("Upper"))
                    trousers = skin;
                for (int i = 0; i < 2; i++)
                {
                    if (!skin.name.EndsWith(i == 0 ? "Sole.L" : "Sole.R")) continue;
                    float lowest = float.PositiveInfinity;
                    foreach (var vertex in CityPedestrianShoeContacts.SkinWorldVertices(skin))
                        lowest = Mathf.Min(lowest, vertex.y);
                    if (!float.IsInfinity(lowest)) ankleHeights[i] = feet[i].position.y - lowest + .002f;
                }
            }
            joints = new Transform[boneSet.Count]; boneSet.CopyTo(joints);
            heldJoints = new Pose[joints.Length];
            // Fit the one assigned garment on the real seat once. The seat surface,
            // unlike the pelvis joint, is the bottom of the visible trousers.
            Worker.transform.SetPositionAndRotation(CommutePlan.HomeToWork[CommutePlan.HomeToWork.Count - 1], site.Rotation);
            PlaceOnSeat(seatAnchor.position, floorAnchor.position.y, 1f);
            if (trousers)
            {
                float bottom = float.PositiveInfinity;
                foreach (var vertex in CityPedestrianShoeContacts.SkinWorldVertices(trousers))
                {
                    Vector3 offset = Quaternion.Inverse(site.Rotation) * (vertex - seatAnchor.position);
                    if (Mathf.Abs(offset.x) < .16f && Mathf.Abs(offset.z) < .17f) bottom = Mathf.Min(bottom, offset.y);
                }
                if (!float.IsInfinity(bottom)) seatLift -= Mathf.Clamp(bottom + .003f, -.12f, .12f);
            }
            Worker.Apply(VillageResidentAction.Idle, 0f);
            toolAxis = (site.Rotation * new Vector3(-.12f, -.08f, .04f)).normalized;
            var rest = Part("ANCHOR_AwlRest");
            awlRest = new Pose(rest.position, Quaternion.AngleAxis(-34f, site.Rotation * Vector3.forward) * awlWorking.rotation);
        }

        private void RestTool() => awl.SetPositionAndRotation(awlRest.position, awlRest.rotation);

        private void SampleTool(double seconds, float grip)
        {
            if (grip <= 0f) { RestTool(); return; }
            // The steel point returns to the same seam and withdraws in pairs.
            // A short dwell gives the left palm time to hold the sole steady.
            float cycle = (float)(seconds % 3.2d);
            float pulse = cycle < 1.8f ? Mathf.Pow(Mathf.Sin(cycle * Mathf.PI / .9f), 2f) : 0f;
            Vector3 working = rightContact.position - toolAxis * (.056f * pulse);
            awl.SetPositionAndRotation(Vector3.Lerp(awlRest.position, working, grip),
                Quaternion.Slerp(awlRest.rotation, awlWorking.rotation, grip));
        }

        private void ApplyRig(float seat, float lunch, float grip)
        {
            HandContactError = SoleContactError = 0f;
            if (seat > 0f) PlaceOnSeat(seatAnchor.position, floorAnchor.position.y, seat);
            if (lunch > 0f)
            {
                Vector3 lunchSeat = site.ToWorld(new Vector3(-.52f, .92f, -.13f));
                PlaceOnSeat(lunchSeat, floorAnchor.position.y, lunch);
            }
            if (grip <= 0f) return;
            Vector3 forward = site.Rotation * Vector3.forward, right = site.Rotation * Vector3.right;
            Quaternion leftRotation = leftPalm.ResolveHandRotation(Vector3.down, forward);
            Vector3 socketOffset = Quaternion.Inverse(leftHand.rotation) * (Worker.LeftGrip.position - leftHand.position);
            Vector3 left = leftContact.position + leftRotation * (socketOffset - leftPalm.PalmCentreInHand);
            Pose hand = fingers.GetSocketPose(false, awl.position, toolAxis, -right);
            Quaternion upright = spine.rotation;
            // Real arm and garment dimensions come from the global assignment.
            // Lean from the spine, leaving the supported pelvis and feet in place.
            for (int tilt = 22; tilt <= 62; tilt += 5)
            {
                spine.rotation = Quaternion.AngleAxis(tilt * grip, right) * upright;
                if (Worker.ApplyHandContacts(hand.position, left, hand.rotation, leftRotation, grip) || grip < .999f) break;
            }
            fingers.SetGrip(false, grip);
            fingers.SetGrip(true, 0f);
            if (grip >= .999f)
                HandContactError = Mathf.Max(Vector3.Distance(fingers.CylinderCentre(false), awl.position),
                    Vector3.Distance(leftHand.position + leftHand.rotation * leftPalm.PalmCentreInHand, leftContact.position));
        }

        private void PlaceOnSeat(Vector3 surface, float floor, float seated)
        {
            Vector3 right = site.Rotation * Vector3.right, forward = site.Rotation * Vector3.forward;
            // The level idle clip supplies the standing ankle positions. Preserve
            // them before lowering the pelvis and keep each ankle on its own sole.
            Vector3 leftStanding = feet[0].position, rightStanding = feet[1].position;
            pelvis.position = Vector3.Lerp(pelvis.position, surface + Vector3.up * seatLift + forward * .075f, seated);
            for (int i = 0; i < 2; i++)
            {
                Transform ankle = feet[i], shin = ankle.parent, thigh = shin.parent;
                Vector3 target = surface + forward * .38f + right * (i == 0 ? -.14f : .14f);
                target.y = floor + ankleHeights[i];
                target = Vector3.Lerp(i == 0 ? leftStanding : rightStanding, target, seated);
                Quaternion flat = Quaternion.Slerp(ankle.rotation, site.Rotation * flatFeet[i], seated);
                LimbTwoBoneIk.Solve(thigh, shin, ankle, target, flat, thigh.position + forward * .65f, 1f, .999f, true);
                if (seated > .999f) SoleContactError = Mathf.Max(SoleContactError, Mathf.Abs(ankle.position.y - target.y));
            }
        }

        // The Animator can restore its sampled stream on intervening frames.
        // Retain completed hand/foot contacts without evaluating clips again.
        private void CapturePose()
        {
            for (int i = 0; i < joints.Length; i++)
                heldJoints[i] = new Pose(joints[i].localPosition, joints[i].localRotation);
            leftGripWeight = fingers.LeftGripWeight; rightGripWeight = fingers.RightGripWeight;
            held = true;
        }

        private void RestorePose()
        {
            if (!held || hidden) return;
            for (int i = 0; i < joints.Length; i++)
                joints[i].SetLocalPositionAndRotation(heldJoints[i].position, heldJoints[i].rotation);
            fingers.SetGrip(true, leftGripWeight); fingers.SetGrip(false, rightGripWeight);
        }
    }
}

using UnityEngine;

namespace BarPromenade
{
    // World-space contacts shared by street residents and balcony SmokeLoop actors.
    internal sealed class CityRailLean
    {
        private readonly CityPedestrianAssetRegistry body;
        private readonly Transform pelvis, spine, head;
        private readonly Transform[] joints;
        private readonly Quaternion[] before;
        private readonly AnatomicalHandFrame[] palms;
        private Vector3 beforePelvis;
        private CityRailRestSpot spot;
        private bool applied;

        internal Transform SmokingHand => joints[5];

        internal CityRailLean(CityPedestrianAssetRegistry body, NpcHandPose hands)
        {
            this.body = body;
            spine = CityPedestrianHandProps.FindSocket(body.ModelRoot, "spine");
            head = body.HeadAnchor;
            pelvis = body.PelvisAnchor;
            Transform left = CityPedestrianHandProps.FindSocket(body.ModelRoot, "hand.L");
            Transform right = CityPedestrianHandProps.FindSocket(body.ModelRoot, "hand.R");
            Transform leftFoot = body.LeftFoot, rightFoot = body.RightFoot;
            joints = new[] { left.parent.parent, left.parent, left, right.parent.parent, right.parent, right, spine, head,
                leftFoot.parent.parent, leftFoot.parent, leftFoot, rightFoot.parent.parent, rightFoot.parent, rightFoot };
            before = new Quaternion[joints.Length];
            palms = new[] { hands.GetAnatomicalFrame(true), hands.GetAnatomicalFrame(false) };
        }

        internal void Apply(CityRailRestSpot rest, bool smoking, float time, float breathePhase)
        {
            spot = rest;
            for (int i = 0; i < joints.Length; i++) before[i] = joints[i].localRotation;
            applied = true;
            Vector3 right = body.transform.right;
            // Bring the hips toward the beam while leaving both soles where
            // the ground layer planted them. With one hand busy the weight
            // shifts toward the supporting elbow.
            beforePelvis = pelvis.localPosition;
            Vector3 shift = spot.Facing * .145f - Vector3.up * .075f;
            if (smoking) shift += spot.Facing * .025f - right * .065f - Vector3.up * .018f;
            pelvis.position += shift;
            for (int i = 8; i < joints.Length; i += 3)
            {
                Transform foot = joints[i + 2];
                LimbTwoBoneIk.Solve(joints[i], joints[i + 1], foot, foot.position - shift, foot.rotation,
                    joints[i + 1].position + spot.Facing * .40f);
            }
            Vector3 restLine = spot.RailTop + Vector3.up * .043f;
            // Find a comfortable pitch at which both upper arms reach the
            // beam. The feet and pelvis keep the resident's grounded idle.
            Quaternion upright = spine.rotation;
            float breath = Mathf.Sin(time * (smoking ? .91f : 1.3f) + breathePhase);
            float pitch = (smoking ? 44f : 37f) + 1.4f * breath;
            if (smoking) upright = Quaternion.AngleAxis(7f + .8f * breath, spot.Facing) * upright;
            for (int i = 0; ; i++)
            {
                spine.rotation = Quaternion.AngleAxis(pitch, right) * upright;
                if (i == 5 || (CanRest(0, restLine) && CanRest(3, restLine))) break;
                pitch += 4f;
            }
            float glance = smoking ? 4f * Mathf.Sin(time * .19f + breathePhase) :
                15f * Mathf.Sin(time * .34f + breathePhase) + 6f * Mathf.Sin(time * .83f);
            // The observer scans the distance; the smoker looks down over the supporting arm.
            float headPitch = smoking ? -pitch * .30f + 4f * breath :
                -pitch * .76f + 3f * Mathf.Sin(time * .53f);
            head.rotation = Quaternion.AngleAxis(glance, Vector3.up) *
                Quaternion.AngleAxis(headPitch, right) * head.rotation;
            RestArm(0, -1f, restLine);
            RestArm(1, 1f, restLine);
        }

        internal void RaiseSmokingHand(Vector3 position, Quaternion rotation, float weight)
        {
            LimbTwoBoneIk.Solve(joints[3], joints[4], SmokingHand, position, rotation,
                joints[3].position + body.transform.right * .55f - Vector3.up * .5f, weight, 1f, true);
        }

        private bool CanRest(int index, Vector3 line)
        {
            Vector3 offset = Vector3.ProjectOnPlane(joints[index].position - line, spot.RailRight);
            return offset.sqrMagnitude < (joints[index + 1].position - joints[index].position).sqrMagnitude * .94f;
        }

        private void RestArm(int side, float sign, Vector3 line)
        {
            int i = side * 3;
            Transform shoulder = joints[i], elbow = joints[i + 1], wrist = joints[i + 2];
            Vector3 closest = line + Vector3.Project(shoulder.position - line, spot.RailRight);
            Vector3 upper = elbow.position - shoulder.position;
            float along = Mathf.Sqrt(Mathf.Max(0f, upper.sqrMagnitude - (closest - shoulder.position).sqrMagnitude));
            Vector3 target = closest + spot.RailRight * (sign * along);
            shoulder.rotation = Quaternion.FromToRotation(upper, target - shoulder.position) * shoulder.rotation;
            Vector3 direction = (-sign * spot.RailRight + spot.Facing * .17f).normalized;
            elbow.rotation = Quaternion.FromToRotation(wrist.position - elbow.position, direction) * elbow.rotation;
            wrist.rotation = palms[side].ResolveHandRotation(Vector3.down, direction + spot.Facing * .2f);
        }

        internal void BlendFromStanding(float weight)
        {
            if (weight >= 1f) return;
            pelvis.localPosition = Vector3.Lerp(beforePelvis, pelvis.localPosition, weight);
            for (int i = 0; i < joints.Length; i++)
                joints[i].localRotation = Quaternion.Slerp(before[i], joints[i].localRotation, weight);
        }

        internal void RestoreBones()
        {
            if (!applied) return;
            pelvis.localPosition = beforePelvis;
            for (int i = 0; i < joints.Length; i++) joints[i].localRotation = before[i];
            applied = false;
        }

    }
}

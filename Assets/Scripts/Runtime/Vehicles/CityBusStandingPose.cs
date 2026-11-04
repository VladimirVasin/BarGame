using UnityEngine;

namespace BarPromenade
{
    // The feet, hand and all sampled bones move in one rigid sprung-cabin frame.
    // Sampling an idle clip only changes the body inside that frame.
    internal sealed class CityBusStandingPose
    {
        private readonly CityPedestrianPresentation owner;
        private readonly Transform model, hand, elbow, shoulder;
        private readonly NpcHandPose fingers;
        private readonly Transform[] joints = new Transform[9];
        private readonly Quaternion[] before = new Quaternion[9];
        private readonly Vector3[] ankles = new Vector3[2];
        private readonly CityPedestrianShoeContacts shoes;
        private Transform floor, grip;
        private CityBusPresentation bus;
        private Quaternion basisInFloor, modelRotation;
        private Quaternion balance = Quaternion.identity;
        private Vector3 modelOffset;
        private bool applied;

        internal bool IsBound => floor != null;
        private Quaternion Basis => floor.rotation * basisInFloor;

        internal CityBusStandingPose(CityPedestrianPresentation body, CityPedestrianShoeContacts shoes)
        {
            owner = body;
            this.shoes = shoes;
            var registry = body.Registry;
            model = registry.ModelRoot;
            fingers = registry.GetComponentInChildren<NpcHandPose>(true);
            hand = CityPedestrianHandProps.FindSocket(model, "hand.R");
            elbow = hand != null ? hand.parent : null;
            shoulder = elbow != null ? elbow.parent : null;
            joints[0] = shoulder; joints[1] = elbow; joints[2] = hand;
            for (int i = 0; i < 2; i++)
            {
                Transform foot = i == 0 ? registry.LeftFoot : registry.RightFoot;
                int start = 3 + i * 3;
                joints[start + 2] = foot;
                joints[start + 1] = foot != null ? foot.parent : null;
                joints[start] = joints[start + 1] != null ? joints[start + 1].parent : null;
            }
        }

        internal bool Bind(Transform floorAnchor, Transform gripAnchor, Vector3 up)
        {
            if (fingers == null || fingers.Hands.Count != 2) return false;
            foreach (var joint in joints) if (joint == null) return false;
            if (!fingers.TryCaptureGrip(owner, false, NpcHandGripKind.Cylindrical)) return false;
            floor = floorAnchor;
            grip = gripAnchor;
            bus = floor.GetComponentInParent<CityBusPresentation>();
            balance = Quaternion.identity;
            Transform root = owner.transform;
            Quaternion basis = Quaternion.LookRotation(Vector3.ProjectOnPlane(root.forward, up), up);
            basisInFloor = Quaternion.Inverse(floor.rotation) * basis;
            Quaternion inverse = Quaternion.Inverse(root.rotation);
            modelOffset = inverse * (model.position - root.position) - Vector3.up * .025f;
            modelRotation = inverse * model.rotation;
            if (shoes == null || !shoes.Measure()) { Release(); return false; }
            for (int side = 0; side < 2; side++)
            {
                var ankle = inverse * (joints[5 + side * 3].position - root.position);
                ankle.y = shoes.Height(side);
                ankles[side] = ankle;
            }
            fingers.SetOwnedGrip(owner, false, 1f);
            Apply();
            return true;
        }

        internal void FollowFloor()
        {
            Quaternion basis = Basis;
            model.SetPositionAndRotation(floor.position + basis * modelOffset, basis * balance * modelRotation);
        }

        internal void Apply()
        {
            RestoreBones();
            // The suspension already filters acceleration and lateral load.
            // The body yields a few degrees while the floor targets stay planted.
            if (bus != null)
                balance = Quaternion.Euler(-2.2f * bus.SuspensionPitch, 0f, -2f * bus.SuspensionRoll);
            FollowFloor();
            Quaternion basis = Basis;
            Vector3 forward = basis * Vector3.forward, right = basis * Vector3.right;
            for (int i = 0; i < joints.Length; i++) before[i] = joints[i].localRotation;
            applied = true;
            for (int side = 0; side < 2; side++)
            {
                int i = 3 + side * 3;
                LimbTwoBoneIk.Solve(joints[i], joints[i + 1], joints[i + 2],
                    floor.position + basis * ankles[side], basis * shoes.Rotation(side), joints[i + 1].position + forward * .4f);
            }
            Pose target = fingers.GetSocketPose(false, grip.position, right, forward);
            Vector3 toCentre = Quaternion.Inverse(hand.rotation) * (fingers.CylinderCentre(false) - hand.position);
            LimbTwoBoneIk.Solve(shoulder, elbow, hand, grip.position - target.rotation * toCentre, target.rotation,
                shoulder.position + right * .35f - forward * .12f);
        }

        internal void RestoreBones()
        {
            if (!applied) return;
            for (int i = 0; i < joints.Length; i++) joints[i].localRotation = before[i];
            applied = false;
        }

        internal void Release()
        {
            if (!IsBound) return;
            RestoreBones();
            fingers.ReleaseGrip(owner, false);
            floor = null;
            grip = null;
            bus = null;
        }
    }
}

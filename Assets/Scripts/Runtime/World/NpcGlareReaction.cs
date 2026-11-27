using System.Collections.Generic;
using UnityEngine;

namespace BarPromenade
{
    /// <summary>Layers a flinch and an open palm over the current standing, walking or seated pose.</summary>
    [DefaultExecutionOrder(31000)]
    [DisallowMultipleComponent]
    internal sealed class NpcGlareReaction : MonoBehaviour
    {
        private const float ProbeInterval = .10f;
        private Transform person, mouth, leftEye, rightEye;
        private NpcHandPose grips;
        private CombatActor combat;
        private Arm left, right, shielding;
        private Bone head;
        private NpcGlarePoseRestore restore;
        private Light source;
        private Vector3 lastLens, forwardInHead;
        private float weight, probeIn, turnSide, targetSide;
        private bool initialized;

        internal static void Install(Transform person, Transform model, Transform head)
        {
            if (!model || !head) return; // Dynamically adapted street registries share the resident's rig.
            if (!model.TryGetComponent(out NpcGlareReaction layer))
                layer = model.gameObject.AddComponent<NpcGlareReaction>();
            layer.person = person;
            layer.head = new Bone(head);
        }

        private void Start()
        {
            if (!person || head == null) { enabled = false; return; }
            // The fisherman keeps his gaze on the water. The lone cafe patron sleeps on his arms.
            var cafe = person.GetComponent<MountainRoadCafeCastAssetRegistry>();
            combat = person.GetComponentInParent<CombatActor>();
            if (person.GetComponentInParent<SeacoastFishermanPresentation>() ||
                (cafe && cafe.Role == MountainRoadCafeCastRole.LonePatron) || (combat && combat.IsZombie))
            {
                enabled = false;
                return;
            }
            Transform Find(string name) => NpcAttentionHeadLayer.FindBone(head.Joint, name);
            mouth = Find(CityPedestrianHandProps.MouthSocketName);
            leftEye = Find("face.eye.L");
            rightEye = Find("face.eye.R");
            left = new Arm(transform, true);
            right = new Arm(transform, false);
            grips = person.GetComponent<NpcHandPose>();
            initialized = left.Valid && right.Valid;
            if (!initialized) { enabled = false; return; }
            forwardInHead = Quaternion.Inverse(head.Joint.rotation) *
                Vector3.Cross(right.Shoulder.position - left.Shoulder.position, person.up).normalized;
            // Spread the first physics probes when a whole crowd is loaded together.
            probeIn = (GetEntityId().GetHashCode() & 15) * (ProbeInterval / 16f);
        }

        private Vector3 FaceForward => mouth ? mouth.up : head.Joint.rotation * forwardInHead;
        private Vector3 Eyes => leftEye && rightEye ? (leftEye.position + rightEye.position) * .5f :
            mouth ? mouth.position + person.up * (.13f * ShoulderWidth) :
            head.Joint.position + (person.up * .30f + FaceForward * .22f) * ShoulderWidth;
        private float ShoulderWidth => Vector3.Distance(right.Shoulder.position, left.Shoulder.position);

        private void LateUpdate() => Advance(Time.deltaTime);

        internal void Advance(float seconds)
        {
            if (!initialized) return;
            RestorePose();
            if (combat && (combat.State.IsDefeated || combat.IsRagdollActive || combat.IsRestrained))
            {
                ResetReaction();
                return;
            }
            if (weight <= 0f && !NpcGlareSource.HasSources) return;
            bool paused = GameTimeScaleRuntime.IsPaused || PauseMenuController.IsAnyPaused;
            float dt = paused ? 0f : seconds;
            probeIn -= dt;
            if (probeIn <= 0f && dt > 0f)
            {
                probeIn = ProbeInterval;
                source = NpcGlareSource.Find(person, Eyes, FaceForward, head.Joint.gameObject.layer);
                if (source)
                {
                    left.CheckProps();
                    right.CheckProps();
                }
            }
            bool lit = source && source.isActiveAndEnabled && source.intensity > 0f;
            if (!lit && weight <= 0f) return;

            if (lit)
            {
                lastLens = source.transform.position;
                Vector3 toward = (lastLens - Eyes).normalized;
                Vector3 across = (right.Shoulder.position - left.Shoulder.position).normalized;
                float side = Vector3.Dot(across, toward);
                if (weight <= 0f || Mathf.Abs(side) > .14f)
                    targetSide = side > .015f ? 1f : -1f;
                if (weight <= 0f) turnSide = targetSide;
                turnSide = Mathf.MoveTowards(turnSide, targetSide, dt * 6f);
            }
            weight = Mathf.MoveTowards(weight, lit ? 1f : 0f, dt / (lit ? .23f : .40f));
            if (weight <= 0f) { ResetReaction(); return; }

            if (!restore)
            {
                restore = gameObject.AddComponent<NpcGlarePoseRestore>();
                restore.Reaction = this;
            }
            restore.enabled = true;
            float blend = Mathf.SmoothStep(0f, 1f, weight);
            Vector3 up = person.up;
            Vector3 faceRight = Vector3.Cross(up, FaceForward).normalized;
            head.Capture();
            head.Joint.rotation = Quaternion.AngleAxis(-turnSide * 48f * blend, up) *
                Quaternion.AngleAxis(13f * blend, faceRight) * head.Joint.rotation;
            head.Record();

            if (shielding == null || !shielding.Free(grips))
            {
                Arm first = targetSide > 0f ? right : left;
                Arm other = first == left ? right : left;
                shielding = first.Free(grips) ? first : other.Free(grips) ? other : null;
            }
            if (shielding == null) return;
            Vector3 eyes = Eyes;
            Vector3 lightDirection = (lastLens - eyes).normalized;
            Vector3 outward = (shielding.Shoulder.position - (shielding == left ? right : left).Shoulder.position).normalized;
            shielding.Shield(grips, eyes, lightDirection, outward, up, ShoulderWidth, blend);
        }

        internal void RestorePose()
        {
            head?.Restore();
            shielding?.Restore();
        }

        private void OnDisable()
        {
            RestorePose();
            ResetReaction();
        }

        private void ResetReaction()
        {
            if (restore) restore.enabled = false;
            weight = 0f;
            shielding = null;
            source = null;
            probeIn = 0f;
        }

        private sealed class Bone
        {
            internal readonly Transform Joint;
            private Quaternion original, written;
            private bool applied;
            internal Bone(Transform joint) => Joint = joint;
            internal void Capture() => original = Joint.localRotation;
            internal void Record() { written = Joint.localRotation; applied = true; }
            internal void Restore()
            {
                if (applied && Joint && Joint.localRotation.Equals(written)) Joint.localRotation = original;
                applied = false;
            }
        }

        private sealed class Arm
        {
            private readonly Bone upper, forearm, wrist;
            private readonly bool isLeft;
            private AnatomicalHandFrame anatomy;
            private bool measured, holdsProp;
            private static readonly List<CityPedestrianHandPropRegistry> props = new();
            internal Transform Shoulder => upper.Joint;
            internal bool Valid => upper.Joint && forearm.Joint && wrist.Joint;

            internal Arm(Transform model, bool left)
            {
                isLeft = left;
                string suffix = left ? ".L" : ".R";
                Transform Find(Transform root, string name) => NpcAttentionHeadLayer.FindBone(root, name + suffix);
                upper = new Bone(Find(model, "upper_arm"));
                forearm = new Bone(Find(upper.Joint, "forearm"));
                wrist = new Bone(Find(forearm.Joint, "hand"));
                if (!Valid) return;
                // Legacy rigid palms use the hand bone down the fingers and a grip socket
                // pointing across them towards the thumb. Children have articulated digits.
                Transform hand = wrist.Joint;
                Transform grip = Find(hand, "SOCKET_Grip");
                Transform index = Find(hand, "finger.index.01"), thumb = Find(hand, "finger.thumb.01");
                Vector3 distal = hand.up;
                Vector3 towardThumb = index && thumb ? thumb.position - index.position :
                    grip ? grip.up : Vector3.Cross(distal, model.up) * (left ? 1f : -1f);
                Vector3 normal = Vector3.Cross(distal, towardThumb).normalized * (left ? -1f : 1f);
                Quaternion inv = Quaternion.Inverse(hand.rotation);
                anatomy = new AnatomicalHandFrame(Vector3.up * .035f, inv * distal, inv * towardThumb, inv * normal, default);
            }

            internal bool Free(NpcHandPose grips) =>
                !holdsProp && (!grips || (isLeft ? grips.LeftGripWeight : grips.RightGripWeight) <= .01f);

            internal void CheckProps()
            {
                // Street props can occupy a grip, vessel or cigarette socket with uncurled fingers.
                holdsProp = false;
                wrist.Joint.GetComponentsInChildren(false, props);
                foreach (var prop in props)
                    if (prop.IsVisible) { holdsProp = true; break; }
                props.Clear();
            }

            internal void Shield(NpcHandPose grips, Vector3 eyes, Vector3 direction, Vector3 outward,
                Vector3 up, float width, float weight)
            {
                if (grips && !measured) { anatomy = grips.GetAnatomicalFrame(isLeft); measured = true; }
                Vector3 distal = Vector3.ProjectOnPlane(up - outward * .28f, direction).normalized;
                Quaternion rotation = anatomy.ResolveHandRotation(direction, distal);
                Vector3 palm = eyes + direction * (width * .39f) + outward * (width * .045f);
                Vector3 hint = Shoulder.position + outward * (width * .64f) - up * (width * .33f);
                upper.Capture(); forearm.Capture(); wrist.Capture();
                LimbTwoBoneIk.Solve(upper.Joint, forearm.Joint, wrist.Joint,
                    anatomy.ResolveHandPosition(palm, rotation), rotation, hint, weight, .98f, true);
                upper.Record(); forearm.Record(); wrist.Record();
            }

            internal void Restore() { upper.Restore(); forearm.Restore(); wrist.Restore(); }
        }
    }
}

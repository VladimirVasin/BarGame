using System;
using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.Playables;

namespace BarPromenade
{
    /// <summary>Absolute child pose sampler. It never advances time or owns a route/prop.</summary>
    [DisallowMultipleComponent]
    public sealed class CityFairChildPresentation : MonoBehaviour
    {
        private PlayableGraph graph;
        private AnimationMixerPlayable mixer;
        private AnimationClipPlayable[] clips;
        private Vector3 modelRestPosition;
        private Arm rightArm, leftArm, rightLeg, leftLeg;
        private Transform neck, fringe, leftHem, rightHem, hood;
        private Vector3 faceForwardInHead, faceUpInHead;
        private Quaternion neckPose, fringePose, leftHemPose, rightHemPose, hoodPose;
        private bool lookApplied, secondaryApplied;
        private Transform[] bones;
        private Vector3[] transitionPositions;
        private Quaternion[] transitionRotations;
        private bool hasSample, recovering;
        private float recoveryStartSeconds;
        public Animator Animator { get; private set; }
        public Transform ModelRoot { get; private set; }
        public Transform Pelvis { get; private set; }
        public Transform LeftFoot { get; private set; }
        public Transform RightFoot { get; private set; }
        public Transform Head { get; private set; }
        public Transform LeftGrip { get; private set; }
        public Transform RightGrip { get; private set; }
        public Vector3 FaceForward => Head.TransformDirection(faceForwardInHead).normalized;
        public Vector3 FaceUp => Head.TransformDirection(faceUpInHead).normalized;
        public const float MaximumLookYaw = 35f;
        public const float MaximumLookPitch = 18f;
        public CityFairChildAction CurrentAction { get; private set; }
        public float CurrentSeconds { get; private set; }
        public float ContactWeight { get; private set; }
        public int FaceCell { get; private set; } = -1;
        public bool IsInitialized => graph.IsValid();
        public static float Duration(CityFairChildAction action) => CityFairChildActions.Duration(action);

        public static CityFairChildPresentation Attach(GameObject actor)
        {
            if (actor == null) throw new ArgumentNullException(nameof(actor));
            var result = actor.GetComponent<CityFairChildPresentation>() ?? actor.AddComponent<CityFairChildPresentation>();
            result.Initialize(); return result;
        }
        public void Initialize()
        {
            if (IsInitialized) return;
            Animator = GetComponentInChildren<Animator>(true);
            if (Animator == null) throw new InvalidOperationException("The fair child provider must supply its own Generic animator.");
            ModelRoot = Animator.transform;
            modelRestPosition = ModelRoot.localPosition;
            Transform Bone(string name) => CityFairChildAssetProvider.FindBone(gameObject, name);
            Pelvis = Bone("pelvis"); LeftFoot = Bone("foot.L"); RightFoot = Bone("foot.R"); Head = Bone("head"); neck = Bone("neck");
            // Generic FBX bone axes describe the joint, not the face. Calibrate
            // the anatomical frame from the model's forward/up in its bind pose.
            faceForwardInHead = Head.InverseTransformDirection(transform.forward);
            faceUpInHead = Head.InverseTransformDirection(transform.up);
            LeftGrip = Bone("SOCKET_Grip.L"); RightGrip = Bone("SOCKET_Grip.R");
            fringe = Bone("Hair.Fringe"); leftHem = Bone("Cloth.Hem.L"); rightHem = Bone("Cloth.Hem.R"); hood = Bone("Cloth.Hood");
            rightArm = new Arm(Bone("upper_arm.R"), Bone("forearm.R"), Bone("hand.R"), RightGrip);
            leftArm = new Arm(Bone("upper_arm.L"), Bone("forearm.L"), Bone("hand.L"), LeftGrip);
            rightLeg = new Arm(Bone("thigh.R"), Bone("shin.R"), RightFoot, RightFoot);
            leftLeg = new Arm(Bone("thigh.L"), Bone("shin.L"), LeftFoot, LeftFoot);
            bones = Array.ConvertAll(CityFairChildActions.BoneNames, Bone);
            transitionPositions = new Vector3[bones.Length]; transitionRotations = new Quaternion[bones.Length];
            AnimationClip[] bank = CityFairChildActions.LoadClips();
            Animator.applyRootMotion = false; Animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
            graph = PlayableGraph.Create("Fair child " + gameObject.name);
            graph.SetTimeUpdateMode(DirectorUpdateMode.Manual);
            mixer = AnimationMixerPlayable.Create(graph, bank.Length);
            clips = new AnimationClipPlayable[bank.Length];
            for (int i = 0; i < bank.Length; i++)
            {
                clips[i] = AnimationClipPlayable.Create(graph, bank[i]);
                clips[i].SetApplyFootIK(false); clips[i].SetApplyPlayableIK(false); clips[i].SetSpeed(0);
                graph.Connect(clips[i], 0, mixer, i);
            }
            AnimationPlayableOutput.Create(graph, "Child body", Animator).SetSourcePlayable(mixer);
            graph.Play(); Sample(CityFairChildAction.Idle, 0f);
        }
        public void Sample(CityFairChildAction action, float seconds, Vector3? pelvisWorld = null)
        {
            Initialize();
            // Constant Generic tracks need not be written again by a zero-time
            // graph evaluation. Remove our offsets before sampling or blending.
            RestoreLookPose();
            RestoreSecondaryPose();
            if (hasSample && action != CurrentAction)
            {
                recovering = (CurrentAction == CityFairChildAction.Walk || CurrentAction == CityFairChildAction.Turn) &&
                    (action == CityFairChildAction.Idle || action == CityFairChildAction.Stop || action == CityFairChildAction.Turn);
                recoveryStartSeconds = seconds;
                if (recovering) for (int i = 0; i < bones.Length; i++)
                { transitionPositions[i] = bones[i].localPosition; transitionRotations[i] = bones[i].localRotation; }
            }
            ModelRoot.localPosition = modelRestPosition;
            CurrentAction = action; CurrentSeconds = Mathf.Max(0f, seconds);
            hasSample = true;
            float duration = CityFairChildActions.Duration(action);
            float time = CityFairChildActions.IsLoop(action) ? Mathf.Repeat(CurrentSeconds, duration) : Mathf.Min(CurrentSeconds, duration);
            for (int i = 0; i < clips.Length; i++) mixer.SetInputWeight(i, i == (int)action ? 1f : 0f);
            clips[(int)action].SetTime(time);
            graph.Evaluate(0f);
            if (recovering)
            {
                float blend = Mathf.SmoothStep(0f, 1f, (CurrentSeconds - recoveryStartSeconds) / .15f);
                for (int i = 0; i < bones.Length; i++)
                {
                    bones[i].localPosition = Vector3.Lerp(transitionPositions[i], bones[i].localPosition, blend);
                    bones[i].localRotation = Quaternion.Slerp(transitionRotations[i], bones[i].localRotation, blend);
                }
                recovering = blend < 1f;
            }
            if (pelvisWorld.HasValue) ModelRoot.position += pelvisWorld.Value - Pelvis.position;
            ContactWeight = CityFairChildActions.Sample(action, seconds).ContactWeight;
            ApplySecondaryMotion();
            ApplyExpression();
        }
        private void ApplySecondaryMotion()
        {
            fringePose = fringe.localRotation; leftHemPose = leftHem.localRotation;
            rightHemPose = rightHem.localRotation; hoodPose = hood.localRotation;
            secondaryApplied = true;
            // All four bones own weighted child geometry. The caller's scaled
            // sample time is the only clock, so repeated/paused samples agree.
            float energy = CurrentAction == CityFairChildAction.Walk ? 1f : .35f;
            float sway = Mathf.Sin(CurrentSeconds * Mathf.PI * 2f * 1.6f);
            float settle = Mathf.Sin(CurrentSeconds * Mathf.PI * 2f * .8f);
            fringe.localRotation *= Quaternion.Euler(3f * energy * settle, 0f, 1.2f * energy * sway);
            leftHem.localRotation *= Quaternion.Euler(2.5f * energy * sway, 0f, .7f * energy * settle);
            rightHem.localRotation *= Quaternion.Euler(-2.5f * energy * sway, 0f, -.7f * energy * settle);
            hood.localRotation *= Quaternion.Euler(2f * energy * settle, 0f, .5f * energy * sway);
        }
        private void RestoreSecondaryPose()
        {
            if (!secondaryApplied) return;
            fringe.localRotation = fringePose; leftHem.localRotation = leftHemPose;
            rightHem.localRotation = rightHemPose; hood.localRotation = hoodPose;
            secondaryApplied = false;
        }
        private void ApplyExpression()
        {
            bool attentive = CurrentAction == CityFairChildAction.GoodsEnter || CurrentAction == CityFairChildAction.GoodsLoop ||
                CurrentAction == CityFairChildAction.ToyEnter || CurrentAction == CityFairChildAction.ToyLoop ||
                CurrentAction == CityFairChildAction.AdjustCap;
            float blink = Mathf.Repeat(CurrentSeconds + 1.35f, 4.1f);
            int cell = blink >= 3.88f && blink < 3.98f ? 2 : blink >= 3.82f && blink < 4.04f ? 1 : attentive ? 3 : 0;
            if (FaceCell == cell) return;
            CityFairChildAssetProvider.SetFace(gameObject, cell);
            FaceCell = cell;
        }
        public void ApplyFootContacts(float leftSoleY, float rightSoleY, float weight = 1f)
        {
            if (!IsInitialized || CurrentAction == CityFairChildAction.SitEnter || CurrentAction == CityFairChildAction.SitLoop ||
                CurrentAction == CityFairChildAction.SitExit) return;
            weight = Mathf.Clamp01(weight);
            // Delta from the actor's grounded datum preserves both the authored
            // .065 m ankle-to-sole offset and each lifted walking foot.
            float leftDelta = (leftSoleY - transform.position.y) * weight;
            float rightDelta = (rightSoleY - transform.position.y) * weight;
            Vector3 leftTarget = LeftFoot.position + Vector3.up * leftDelta;
            Vector3 rightTarget = RightFoot.position + Vector3.up * rightDelta;
            // Lower the pelvis to the lower support rather than stretching a
            // straight downhill leg beyond the child's actual bone lengths.
            ModelRoot.position += Vector3.up * Mathf.Min(0f, leftDelta, rightDelta);
            Solve(leftLeg, leftTarget, 1f, true, true);
            Solve(rightLeg, rightTarget, 1f, false, true);
        }
        public bool ApplyHandContacts(Vector3? right, Vector3? left, float weight = 1f)
        {
            Initialize();
            bool matched = true;
            if (right.HasValue) matched &= Solve(rightArm, right.Value, weight, false);
            if (left.HasValue) matched &= Solve(leftArm, left.Value, weight, true);
            return matched;
        }
        public void ApplyLook(Vector3? worldPoint, float weight = 1f)
        {
            if (!IsInitialized) return;
            RestoreLookPose();
            weight = Mathf.Clamp01(weight);
            if (!worldPoint.HasValue || weight <= 0f) return;
            Vector3 target = worldPoint.Value - Head.position;
            if (target.sqrMagnitude < .000001f) return;
            Quaternion faceFrame = Quaternion.LookRotation(FaceForward, FaceUp);
            Vector3 direction = Quaternion.Inverse(faceFrame) * target;
            float yaw = Mathf.Clamp(Mathf.Atan2(direction.x, direction.z) * Mathf.Rad2Deg, -MaximumLookYaw, MaximumLookYaw);
            float pitch = Mathf.Clamp(-Mathf.Atan2(direction.y, new Vector2(direction.x, direction.z).magnitude) * Mathf.Rad2Deg, -MaximumLookPitch, MaximumLookPitch);
            // Only the remaining angle from the authored face direction is
            // added: a child bending over the table already looks down.
            Quaternion turn = faceFrame * Quaternion.Euler(pitch * weight, yaw * weight, 0f) * Quaternion.Inverse(faceFrame);
            neckPose = neck.localRotation;
            lookApplied = true;
            neck.rotation = turn * neck.rotation;
        }
        private void RestoreLookPose()
        {
            if (!lookApplied) return;
            neck.localRotation = neckPose;
            lookApplied = false;
        }
        public void ResetPose() { if (IsInitialized) { hasSample = recovering = false; Sample(CityFairChildAction.Idle, 0f); } }
        private bool Solve(Arm arm, Vector3 requested, float weight, bool left, bool leg = false)
        {
            Vector3 target = Vector3.Lerp(arm.Grip.position, requested, Mathf.Clamp01(weight));
            Quaternion handRotation = arm.Hand.rotation;
            Vector3 wrist = target - (arm.Grip.position - arm.Hand.position);
            Vector3 shoulder = arm.Upper.position;
            float a = Vector3.Distance(shoulder, arm.Forearm.position), b = Vector3.Distance(arm.Forearm.position, arm.Hand.position);
            Vector3 delta = wrist - shoulder;
            if (delta.sqrMagnitude < .000001f) return false;
            float distance = Mathf.Clamp(delta.magnitude, Mathf.Abs(a - b) + .001f, a + b - .001f);
            Vector3 axis = delta.normalized;
            wrist = shoulder + axis * distance;
            Vector3 bend = Vector3.ProjectOnPlane(leg ? transform.forward : transform.right * (left ? -1f : 1f) - transform.forward * .2f, axis).normalized;
            if (bend.sqrMagnitude < .1f) bend = Vector3.ProjectOnPlane(transform.up, axis).normalized;
            float along = (a * a - b * b + distance * distance) / (2f * distance);
            Vector3 elbow = shoulder + axis * along + bend * Mathf.Sqrt(Mathf.Max(0f, a * a - along * along));
            arm.Upper.rotation = Quaternion.FromToRotation(arm.Forearm.position - shoulder, elbow - shoulder) * arm.Upper.rotation;
            arm.Forearm.rotation = Quaternion.FromToRotation(arm.Hand.position - arm.Forearm.position, wrist - arm.Forearm.position) * arm.Forearm.rotation;
            arm.Hand.rotation = handRotation;
            return Vector3.Distance(arm.Grip.position, target) <= .02f;
        }
        private sealed class Arm
        {
            public readonly Transform Upper, Forearm, Hand, Grip;
            public Arm(Transform upper, Transform forearm, Transform hand, Transform grip)
            { Upper = upper; Forearm = forearm; Hand = hand; Grip = grip; }
        }
        private void OnDisable()
        {
            RestoreLookPose(); RestoreSecondaryPose();
            if (ModelRoot != null) ModelRoot.localPosition = modelRestPosition;
        }
        private void OnDestroy() { if (graph.IsValid()) graph.Destroy(); }
    }
}

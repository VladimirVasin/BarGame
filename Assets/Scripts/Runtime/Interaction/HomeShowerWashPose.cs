using System;
using System.Collections.Generic;
using UnityEngine;
using Stopwatch = System.Diagnostics.Stopwatch;

namespace BarPromenade
{
    /// <summary>
    /// The wash on the production hero: both palms brace at the tile,
    /// the right arm stays above the soap sightline, the torso leans in
    /// and the head hangs under the water; each hand reaches its own
    /// valve to open or close the water. The full-body clip set is closed, so this is
    /// solved every presentation frame on the actual rig, after the
    /// character presentation has written its own pose: capture the Idle
    /// neutral once, restore it, rebuild the pose on top, hand the arms
    /// to the shared two-bone solver. Every axis used here is a WORLD
    /// axis of the actor — the imported bones' local axes are not
    /// anatomical and are never reasoned about.
    ///
    /// The same component carries the authored pieces the undressed rig
    /// needs and the clothed rig hides: the three bridges that close the
    /// jacket's holes at the nape and the shoulders, and the toilet's
    /// authored anatomy, attached to the bare pelvis. The shaft keeps its
    /// shared rest pitch; the two lobes turn down from their fixed seams
    /// for this unclothed pose. All are placed from bone positions each frame.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class HomeShowerWashPose : MonoBehaviour
    {
        public const float SpinePitchDegrees = 20f;
        public const float ChestPitchDegrees = 12f;
        public const float NeckPitchDegrees = 18f;
        public const float HeadPitchDegrees = 20f;
        public const float SwayHertz = 0.45f;
        public const float SwayChestRollDegrees = 2.2f;
        public const float SwayHeadRollDegrees = 3.0f;
        public const float SwayPalmSlideMetres = 0.01f;
        public const float PalmFingerSplayDegrees = 12f;
        public const float ElbowHintOutMetres = 0.14f;
        public const float ElbowHintDownMetres = 0.12f;
        public const float ElbowHintForwardMetres = 0.25f;
        public const float ValveElbowHintOutMetres = 0.25f;
        public const float ValveElbowHintDownMetres = 0.30f;
        public const float YokeBelowShoulderMetres = 0.012f;
        public const float DeltoidAlongArmMetres = 0.03f;
        public const float PassiveContactLimitDegrees = 12f;
        // The toilet's forward-curved neck clears its coat. Turning the
        // same fixed-metre lobes down in the shower reduces their authored
        // 82 mm forward reach to 47.514/49.514 mm, without moving a seam.
        public const float ScrotumRestPitchDegrees = 30f;
        // The source's first two root rings and both broad neck welds fit
        // inside 32 mm of their fixed attachment; this volume joins the skin.
        private const float PassiveAttachmentRadius = 0.032f;

        /// <summary>
        /// The base height comes from the pelvis ANCHOR, not the lower cap
        /// of its mesh. The shower measures the bare front surface and
        /// keeps the toilet's shaft rest pitch. The lobe attachments stay
        /// fixed while their shower-only rest rotation removes the coat
        /// clearance built into those shared meshes.
        /// </summary>
        public const float AnatomyAbovePelvisMetres =
            HomeToiletFirstPersonView.AnatomyHeightAbovePelvis;
        public const float AnatomyBaseInsetMetres = 0.008f;
        public const float AnatomyRestPitchDegrees =
            HomeToiletFirstPersonView.RestAimPitchDegrees;
        public const float AnatomyFallbackForwardMetres = 0.07f;

        private Player3DAssetRegistry registry;
        private Transform actor;
        private Transform room;
        private Transform spine;
        private Transform chest;
        private Transform neck;
        private Transform head;
        private Transform pelvisAnchor;
        private Transform hotValve;
        private Transform coldValve;
        private Vector3 valveGripLocal;
        private readonly Arm left = new Arm();
        private readonly Arm right = new Arm();
        private readonly Quaternion[] neutral = new Quaternion[10];
        private readonly Vector3[] neutralEyePivots = new Vector3[5];
        private Transform yoke;
        private Transform deltoidLeft;
        private Transform deltoidRight;
        private Transform anatomyRoot;
        private Transform anatomyAimPivot;
        private Transform scrotumLeft;
        private Transform scrotumRight;
        private Vector3 anatomyBaseInPelvis;
        private Quaternion anatomyRotationInPelvis = Quaternion.identity;
        private bool captured;
        private bool poseApplied;
        private readonly PassivePart[] passiveParts = { new PassivePart(), new PassivePart(), new PassivePart() };
        private readonly List<PassiveObstacle> passiveObstacles = new List<PassiveObstacle>();
        private Vector3 passiveRayForward, passiveRayRight, passiveRayUp;
        private int passiveObstacleRevision;

        public bool IsInitialized => registry != null && actor != null;
        public bool IsCaptured => captured;
        public bool BridgesShown { get; private set; }
        public bool HasBridges => yoke != null && deltoidLeft != null && deltoidRight != null;
        public bool HasAnatomy => anatomyRoot != null && scrotumLeft != null && scrotumRight != null;
        public Transform AnatomyRoot => anatomyRoot;

        /// <summary>The two hanging masses, so a test can say out loud
        /// which of the three reads in front of the others.</summary>
        public Transform LeftScrotum => scrotumLeft;
        public Transform RightScrotum => scrotumRight;
        public float LeftPalmError { get; private set; }
        public float RightPalmError { get; private set; }
        public float LeftChainLength => left.ChainLength;
        public float RightChainLength => right.ChainLength;
        public Vector3 LeftPalmTarget { get; private set; }
        public Vector3 RightPalmTarget { get; private set; }
        public float PassiveContactAngleDegrees { get; private set; }
        public float PassiveContactDisplacementMetres { get; private set; }
        public bool PassiveContactActive { get; private set; }
        public int PassiveBlockedSteps { get; private set; }
        public float PassiveAnchorError { get; private set; }
        // Cumulative production work, sampled without including the test's mesh observer.
        public long PassiveCollisionWorkCandidates { get; private set; }
        public long PassiveCollisionTriangleTests { get; private set; }
        public long PassiveClearanceQueries { get; private set; }
        public long PassiveObstacleVertexUpdates { get; private set; }
        public long PassiveClearanceElapsedTicks { get; private set; }
        public long PassiveClearanceCalls { get; private set; }
        public long PassiveClearanceLastTicks { get; private set; }

        /// <summary>
        /// No-contact decay for pickup, transfers and an unselected surface.
        /// The legacy travel argument is deliberately not a source of force:
        /// only a measured contact point on an attached mesh can push it.
        /// </summary>
        public void ApplyPassiveSoapContact(Vector3 surfaceTravel, float deltaTime)
        {
            AdvancePassiveContact(null, Vector3.zero, Vector3.zero, Vector3.zero, deltaTime);
        }

        /// <summary>
        /// A real soap contact applies light normal pressure and sliding friction
        /// at its measured lever arm. The pending angular spring is presented
        /// on the next brace, before the hand solver validates that geometry.
        /// </summary>
        public void ApplyPassiveSoapContact(Transform surface, Vector3 point, Vector3 normal,
            Vector3 surfaceTravel, float deltaTime)
        {
            AdvancePassiveContact(surface, point, normal, surfaceTravel, deltaTime);
        }

        public void ResetPassiveSoapContact()
        {
            foreach (PassivePart part in passiveParts) part.Reset();
            PassiveContactAngleDegrees = PassiveContactDisplacementMetres = PassiveAnchorError = 0f;
            PassiveContactActive = false;
            PassiveBlockedSteps = 0;
            PassiveCollisionWorkCandidates = PassiveCollisionTriangleTests = PassiveClearanceQueries = 0;
            PassiveObstacleVertexUpdates = 0;
            PassiveClearanceElapsedTicks = PassiveClearanceCalls = PassiveClearanceLastTicks = 0;
        }

        /// <summary>All fallible preparation, before the modal capture.</summary>
        public bool Initialize(HomeInteriorRoot home)
        {
            if (home == null)
            {
                throw new ArgumentNullException(nameof(home));
            }

            Release();
            if (home.Player.GameObject == null ||
                !(home.Player.Visual is Player3DCharacterPresentation visual) ||
                visual.Registry == null)
            {
                return false;
            }

            registry = visual.Registry;
            actor = home.Player.GameObject.transform;
            room = home.Room != null ? home.Room : home.transform;
            hotValve = room.Find(HomeShowerInteraction.HotHandleName);
            coldValve = room.Find(HomeShowerInteraction.ColdHandleName);
            valveGripLocal = HomeBrushingResources.Anchor("FaucetHandle", "HandGrip");
            chest = ResolveBone(Player3DAnatomicalPart.Torso);
            spine = registry.Anchors.Spine;
            neck = ResolveBone(Player3DAnatomicalPart.Neck);
            head = ResolveBone(Player3DAnatomicalPart.Head);
            pelvisAnchor = registry.Anchors.Pelvis;
            if (chest == null || neck == null || head == null || pelvisAnchor == null ||
                !left.Resolve(registry, false) ||
                !right.Resolve(registry, true))
            {
                registry = null;
                return false;
            }

            if (!TryCreateBridges(home.transform))
            {
                Release();
                return false;
            }

            // The anatomy is a courtesy of the toilet's authored pieces; a
            // build without them still showers, bridges and all.
            TryCreateAnatomy(home.transform);
            SetBridgesShown(false);
            GameLog.Info(
                "home",
                "shower_wash_pose_ready",
                GameLog.Field("left_chain_m", left.ChainLength),
                GameLog.Field("right_chain_m", right.ChainLength),
                GameLog.Field("anatomy", HasAnatomy));
            return true;
        }

        /// <summary>
        /// Remembers the Idle neutral the pose is rebuilt over. Must run
        /// straight after the handoff lock is taken: the lock evaluates
        /// the Idle frame synchronously, so this reads a neutral, never
        /// the last stride of the walk.
        /// </summary>
        public void Capture()
        {
            if (!IsInitialized)
            {
                return;
            }

            neutral[0] = spine != null ? spine.localRotation : Quaternion.identity;
            neutral[1] = chest.localRotation;
            neutral[2] = neck.localRotation;
            neutral[3] = head.localRotation;
            neutral[4] = left.Upper.localRotation;
            neutral[5] = left.Forearm.localRotation;
            neutral[6] = left.Hand.localRotation;
            neutral[7] = right.Upper.localRotation;
            neutral[8] = right.Forearm.localRotation;
            neutral[9] = right.Hand.localRotation;
            Quaternion toActor = Quaternion.Inverse(actor.rotation);
            neutralEyePivots[0] = spine != null ? toActor * (spine.position - actor.position) : Vector3.zero;
            neutralEyePivots[1] = toActor * (chest.position - actor.position);
            neutralEyePivots[2] = toActor * (neck.position - actor.position);
            neutralEyePivots[3] = toActor * (head.position - actor.position);
            neutralEyePivots[4] = toActor * (registry.Anchors.Mouth.position - actor.position);
            captured = true;
        }

        /// <summary>
        /// Predict the eye at a grounded room-local dock before the actor
        /// arrives. Capture reads the locked Idle once; this method rotates
        /// only copied pivot positions, never a Transform or a hidden pose.
        /// It matches ApplyBrace with zero sway and the view's world-up eye offset.
        /// </summary>
        public bool TryPredictEyeLocal(Vector3 groundedDockLocal, Vector3 facingLocal,
            float poseWeight, out Vector3 eyeLocal)
        {
            eyeLocal = default;
            if (!IsInitialized || !captured || room == null || facingLocal.sqrMagnitude < 0.000001f) return false;
            Vector3[] points = (Vector3[])neutralEyePivots.Clone();
            float weight = Mathf.Clamp01(poseWeight);
            for (int pivot = 0; pivot < 4; pivot++)
            {
                float angle = pivot == 0 ? (spine != null ? SpinePitchDegrees : 0f)
                    : pivot == 1 ? ChestPitchDegrees : pivot == 2 ? NeckPitchDegrees : HeadPitchDegrees;
                Quaternion pitch = Quaternion.AngleAxis(angle * weight, Vector3.right);
                for (int child = pivot + 1; child < points.Length; child++)
                    points[child] = points[pivot] + pitch * (points[child] - points[pivot]);
            }
            Quaternion facing = Quaternion.LookRotation(room.TransformDirection(facingLocal), Vector3.up);
            Vector3 eyeWorld = room.TransformPoint(groundedDockLocal) + facing * points[4] +
                Vector3.up * HomeShowerFirstPersonView.EyeHeightAboveMouth;
            eyeLocal = room.InverseTransformPoint(eyeWorld);
            return true;
        }

        /// <summary>
        /// The brace, at <paramref name="weight"/>; the right hand blends
        /// from its tile brace to the tap by <paramref name="valveReach"/>; the
        /// left uses <paramref name="coldValveReach"/> for the cold tap.
        /// The sway envelope scales the slow rock.
        /// </summary>
        public void ApplyBrace(
            float weight,
            float valveReach,
            float sway,
            float elapsed,
            float coldValveReach = 0f)
        {
            if (!IsInitialized || !captured)
            {
                return;
            }

            float w = Mathf.Clamp01(weight);
            RestoreNeutral();
            if (w <= 0.0001f)
            {
                poseApplied = false;
                return;
            }

            poseApplied = true;
            Vector3 actorRight = actor.right;
            Vector3 actorForward = actor.forward;
            Vector3 up = Vector3.up;
            float phase = elapsed * SwayHertz * 2f * Mathf.PI;
            float rock = Mathf.Clamp01(sway) * w;

            if (spine != null)
            {
                Pitch(spine, SpinePitchDegrees * w, actorRight);
            }

            Pitch(chest, ChestPitchDegrees * w, actorRight);
            Roll(chest, SwayChestRollDegrees * Mathf.Sin(phase) * rock, actorForward);
            Pitch(neck, NeckPitchDegrees * w, actorRight);
            Pitch(head, HeadPitchDegrees * w, actorRight);
            Roll(head, SwayHeadRollDegrees * Mathf.Sin(phase + 0.6f) * rock, actorForward);

            float slide = SwayPalmSlideMetres * Mathf.Sin(phase) * rock;
            Vector3 leftPalm = room.TransformPoint(HomeShowerFraming.LeftPalm) +
                actorRight * slide;
            Vector3 rightPalm = room.TransformPoint(HomeShowerFraming.RightPalm) +
                actorRight * slide;
            LeftPalmTarget = leftPalm;
            RightPalmTarget = rightPalm;

            // Fingers up the tile, splayed a little outward; thumbs inward.
            // With fingers up and the thumb toward the body's centre, a
            // real palm faces the wall on either hand — no sign to guess.
            Vector3 leftOut = -actorRight;
            Vector3 rightOut = actorRight;
            Quaternion leftHandRotation = HandRotation(
                left,
                (up + leftOut * Mathf.Tan(PalmFingerSplayDegrees * Mathf.Deg2Rad)).normalized,
                -leftOut);
            Vector3 leftHint = left.Upper.position +
                leftOut * ElbowHintOutMetres -
                up * ElbowHintDownMetres +
                actorForward * ElbowHintForwardMetres;
            float coldReach = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(coldValveReach));
            Vector3 leftTarget = leftPalm;
            if (coldReach > 0.0001f)
            {
                Vector3 grip = coldValve != null ? coldValve.TransformPoint(valveGripLocal)
                    : room.TransformPoint(HomeShowerFraming.ColdHandleGrip);
                // The shared grip sits on the neighboring spoke for this hand.
                // right/-forward mirrors the right hand's forward/-right frame
                // across the mixer throughout the same 0..-90 degree wheel arc.
                Quaternion tapRotation = HandRotation(left,
                    coldValve != null ? coldValve.right : actorRight,
                    coldValve != null ? -coldValve.forward : -actorForward);
                Vector3 tapHint = left.Upper.position +
                    leftOut * ValveElbowHintOutMetres - up * ValveElbowHintDownMetres;
                leftTarget = Vector3.Lerp(leftPalm, grip, coldReach);
                leftHandRotation = Quaternion.Slerp(leftHandRotation, tapRotation, coldReach);
                leftHint = Vector3.Lerp(leftHint, tapHint, coldReach);
            }
            left.Solve(leftTarget, leftHandRotation, leftHint, w);
            LeftPalmTarget = leftTarget;
            LeftPalmError = Vector3.Distance(left.PalmPosition, leftTarget);

            float reach = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(valveReach));
            Quaternion rightRestRotation = HandRotation(right,
                (up + rightOut * Mathf.Tan(PalmFingerSplayDegrees * Mathf.Deg2Rad)).normalized,
                -rightOut);
            Vector3 rightHint = right.Upper.position +
                rightOut * ElbowHintOutMetres +
                actorForward * ElbowHintForwardMetres;
            Vector3 rightTarget = rightPalm;
            Quaternion rightRotation = rightRestRotation;
            if (reach > 0.0001f)
            {
                // Follow the shared sink wheel's authored grip and its turn,
                // including the offset from its rotation axis.
                Vector3 grip = hotValve != null ? hotValve.TransformPoint(valveGripLocal)
                    : room.TransformPoint(HomeShowerFraming.HotHandleGrip);
                Quaternion tapRotation = HandRotation(right,
                    hotValve != null ? hotValve.forward : actorForward,
                    hotValve != null ? -hotValve.right : -actorRight);
                Vector3 tapHint = right.Upper.position +
                    rightOut * ValveElbowHintOutMetres -
                    up * ValveElbowHintDownMetres;
                rightTarget = Vector3.Lerp(rightPalm, grip, reach);
                rightRotation = Quaternion.Slerp(rightRestRotation, tapRotation, reach);
                rightHint = Vector3.Lerp(rightHint, tapHint, reach);
            }

            right.Solve(rightTarget, rightRotation, rightHint, w);
            RightPalmTarget = rightTarget;
            RightPalmError = Vector3.Distance(right.PalmPosition, rightTarget);
            CommitPassivePose();
        }

        /// <summary>The bridges and the anatomy: on with the clothes off, off with them on.</summary>
        public void SetBridgesShown(bool shown)
        {
            BridgesShown = shown && HasBridges;
            if (yoke != null) yoke.gameObject.SetActive(BridgesShown);
            if (deltoidLeft != null) deltoidLeft.gameObject.SetActive(BridgesShown);
            if (deltoidRight != null) deltoidRight.gameObject.SetActive(BridgesShown);
            bool anatomyShown = BridgesShown && HasAnatomy;
            if (anatomyRoot != null) anatomyRoot.gameObject.SetActive(anatomyShown);
            if (scrotumLeft != null) scrotumLeft.gameObject.SetActive(anatomyShown);
            if (scrotumRight != null) scrotumRight.gameObject.SetActive(anatomyShown);
        }

        /// <summary>
        /// Seats the yoke on the torso cap between the shoulder joints,
        /// pointing at the neck, each deltoid a little way down its own
        /// arm, and the anatomy at its measured place on the pelvis — from
        /// bone positions, after the pose has been solved.
        /// </summary>
        public void FollowBridges()
        {
            if (!BridgesShown || !IsInitialized)
            {
                return;
            }

            Vector3 leftShoulder = left.Upper.position;
            Vector3 rightShoulder = right.Upper.position;
            Vector3 across = rightShoulder - leftShoulder;
            Vector3 origin = (leftShoulder + rightShoulder) * 0.5f -
                Vector3.up * YokeBelowShoulderMetres;
            Vector3 toNeck = neck.position - origin;
            if (across.sqrMagnitude > 0.000001f && toNeck.sqrMagnitude > 0.000001f)
            {
                Vector3 acrossDirection = across.normalized;
                Vector3 yokeUp = Vector3.ProjectOnPlane(toNeck, acrossDirection);
                if (yokeUp.sqrMagnitude < 0.000001f)
                {
                    yokeUp = Vector3.up;
                }

                yokeUp.Normalize();
                Vector3 yokeForward = Vector3.Cross(acrossDirection, yokeUp);
                yoke.SetPositionAndRotation(
                    origin,
                    Quaternion.LookRotation(yokeForward, yokeUp));
            }

            PlaceDeltoid(deltoidLeft, left);
            PlaceDeltoid(deltoidRight, right);
            PlaceAnatomy();
        }

        /// <summary>Back to the neutral. Idempotent; the pieces keep their own switch.</summary>
        public void End()
        {
            ResetPassiveSoapContact();
            if (captured && poseApplied)
            {
                RestoreNeutral();
            }

            poseApplied = false;
            captured = false;
            LeftPalmError = 0f;
            RightPalmError = 0f;
        }

        /// <summary>Drops the rig references and the authored pieces.</summary>
        public void Release()
        {
            End();
            SetBridgesShown(false);
            DestroyPivot(ref yoke);
            DestroyPivot(ref deltoidLeft);
            DestroyPivot(ref deltoidRight);
            DestroyPivot(ref anatomyRoot);
            DestroyPivot(ref scrotumLeft);
            DestroyPivot(ref scrotumRight);
            passiveObstacles.Clear();
            foreach (PassivePart part in passiveParts) part.Clear();
            anatomyAimPivot = null;
            hotValve = null;
            coldValve = null;
            registry = null;
            actor = null;
        }

        private void RestoreNeutral()
        {
            if (spine != null) spine.localRotation = neutral[0];
            chest.localRotation = neutral[1];
            neck.localRotation = neutral[2];
            head.localRotation = neutral[3];
            left.Upper.localRotation = neutral[4];
            left.Forearm.localRotation = neutral[5];
            left.Hand.localRotation = neutral[6];
            right.Upper.localRotation = neutral[7];
            right.Forearm.localRotation = neutral[8];
            right.Hand.localRotation = neutral[9];
        }

        private static void Pitch(Transform bone, float degrees, Vector3 actorRight)
        {
            // A positive turn about the actor's right axis nods forward
            // and down; measured against the world, never the bone.
            bone.rotation = Quaternion.AngleAxis(degrees, actorRight) * bone.rotation;
        }

        private static void Roll(Transform bone, float degrees, Vector3 actorForward)
        {
            bone.rotation = Quaternion.AngleAxis(degrees, actorForward) * bone.rotation;
        }

        private static Quaternion HandRotation(Arm arm, Vector3 fingers, Vector3 thumb)
        {
            return Quaternion.LookRotation(fingers, thumb) *
                Quaternion.Inverse(arm.HandFrameInHand);
        }

        private void PlaceDeltoid(Transform pivot, Arm arm)
        {
            if (pivot == null)
            {
                return;
            }

            Vector3 along = arm.Forearm.position - arm.Upper.position;
            Vector3 offset = along.sqrMagnitude > 0.000001f
                ? along.normalized * DeltoidAlongArmMetres
                : Vector3.zero;
            pivot.SetPositionAndRotation(
                arm.Upper.position + offset,
                actor.rotation);
        }

        private void PlaceAnatomy()
        {
            if (!HasAnatomy)
            {
                return;
            }

            Vector3 root = pelvisAnchor.TransformPoint(anatomyBaseInPelvis);
            Quaternion rotation = pelvisAnchor.rotation * anatomyRotationInPelvis;
            anatomyRoot.SetPositionAndRotation(root, PassiveRotation(passiveParts[0].Applied) * rotation);
            if (anatomyAimPivot != null)
            {
                // AimPivot is authored at zero; aligning by measured world
                // position keeps this right if the FBX root ever moves.
                anatomyRoot.position += root - anatomyAimPivot.position;
            }

            // Attachments follow body yaw, never the shaft's pitch. Only
            // the lobe meshes turn down, bringing their mass toward the
            // bare body while preserving the same fixed attachment points.
            Quaternion hang = Quaternion.LookRotation(
                Vector3.ProjectOnPlane(actor.forward, Vector3.up).sqrMagnitude > 0.000001f
                    ? Vector3.ProjectOnPlane(actor.forward, Vector3.up).normalized
                    : Vector3.forward,
                Vector3.up);
            Quaternion lobeRest = hang * Quaternion.Euler(ScrotumRestPitchDegrees, 0f, 0f);
            scrotumLeft.SetPositionAndRotation(
                root + hang * HomeToiletFirstPersonView.LeftScrotumAttachment,
                PassiveRotation(passiveParts[1].Applied) * lobeRest);
            scrotumRight.SetPositionAndRotation(
                root + hang * HomeToiletFirstPersonView.RightScrotumAttachment,
                PassiveRotation(passiveParts[2].Applied) * lobeRest);
            PassiveAnchorError = Vector3.Distance(anatomyAimPivot != null ? anatomyAimPivot.position : anatomyRoot.position, root);
            PassiveAnchorError = Mathf.Max(PassiveAnchorError,
                Vector3.Distance(scrotumLeft.position, root + hang * HomeToiletFirstPersonView.LeftScrotumAttachment));
            PassiveAnchorError = Mathf.Max(PassiveAnchorError,
                Vector3.Distance(scrotumRight.position, root + hang * HomeToiletFirstPersonView.RightScrotumAttachment));
        }

        private Quaternion PassiveRotation(Vector3 radians)
        {
            float angle = radians.magnitude;
            return angle > 0.000001f
                ? Quaternion.AngleAxis(angle * Mathf.Rad2Deg, actor.rotation * (radians / angle))
                : Quaternion.identity;
        }

        private void AdvancePassiveContact(Transform surface, Vector3 point, Vector3 normal,
            Vector3 travel, float seconds)
        {
            if (!HasAnatomy || actor == null || float.IsNaN(seconds) || float.IsInfinity(seconds) || seconds <= 0f) return;
            float dt = Mathf.Min(seconds, 0.05f);
            PassivePart touched = null;
            foreach (PassivePart part in passiveParts)
                if (surface != null && part.Pivot != null && (surface == part.Pivot || surface.IsChildOf(part.Pivot)))
                    touched = part;
            PassiveContactActive = touched != null;
            Vector3 force = touched != null
                ? Vector3.ClampMagnitude(-normal.normalized * 0.14f +
                    Vector3.ClampMagnitude(Vector3.ProjectOnPlane(travel, normal) / dt, 0.25f) * 0.8f, 0.28f)
                : Vector3.zero;
            foreach (PassivePart part in passiveParts)
            {
                float length = Mathf.Max(0.04f, part.Radius);
                float mass = part == passiveParts[0] ? 0.10f : 0.045f;
                float inertia = mass * length * length / 3f;
                // Gravity about the fixed attachment restores the ordinary
                // hanging pose; near-critical damping prevents a sustained wobble.
                float stiffness = mass * 9.81f * length * 0.5f;
                float damping = 1.9f * Mathf.Sqrt(inertia * stiffness);
                Vector3 torque = part == touched
                    ? Quaternion.Inverse(actor.rotation) * Vector3.Cross(
                        Vector3.ClampMagnitude(point - part.Pivot.position, length), force)
                    : Vector3.zero;
                float remaining = dt;
                while (remaining > 0.000001f)
                {
                    float step = Mathf.Min(remaining, 1f / 120f);
                    part.Velocity += (torque - part.Pending * stiffness - part.Velocity * damping) * (step / inertia);
                    part.Velocity = Vector3.ClampMagnitude(part.Velocity, 90f * Mathf.Deg2Rad);
                    part.Pending += part.Velocity * step;
                    float limit = PassiveContactLimitDegrees * Mathf.Deg2Rad;
                    if (part.Pending.sqrMagnitude > limit * limit)
                    {
                        Vector3 outward = part.Pending.normalized;
                        part.Pending = outward * limit;
                        part.Velocity -= outward * Mathf.Max(0f, Vector3.Dot(part.Velocity, outward));
                    }
                    remaining -= step;
                }
            }
        }

        private void CommitPassivePose()
        {
            if (!HasAnatomy) return;
            long started = Stopwatch.GetTimestamp();
            try
            {
                CommitPassivePoseCore();
            }
            finally
            {
                PassiveClearanceLastTicks = Stopwatch.GetTimestamp() - started;
                PassiveClearanceElapsedTicks += PassiveClearanceLastTicks;
                PassiveClearanceCalls++;
            }
        }

        private void CommitPassivePoseCore()
        {
            // The new skin geometry is present before SoapPose picks and solves.
            // FollowBridges later in the frame only reapplies this committed state.
            PlaceAnatomy();
            bool moving = false;
            foreach (PassivePart part in passiveParts)
                moving |= (part.Pending - part.Applied).sqrMagnitude > 0.0000000001f;
            if (moving)
            {
                Quaternion actorRotation = actor.rotation;
                passiveRayForward = actor.forward;
                passiveRayRight = actor.right;
                passiveRayUp = actor.up;
                bool refreshed = false;
                foreach (PassiveObstacle obstacle in passiveObstacles)
                {
                    int vertices = obstacle.Update(actorRotation, passiveRayForward, passiveRayRight, passiveRayUp);
                    PassiveObstacleVertexUpdates += vertices;
                    refreshed |= vertices > 0;
                }
                if (refreshed) passiveObstacleRevision++;
            }
            PassiveContactAngleDegrees = PassiveContactDisplacementMetres = 0f;
            foreach (PassivePart part in passiveParts)
            {
                if (part.Pivot == null) continue;
                Quaternion rest = Quaternion.Inverse(PassiveRotation(part.Applied)) * part.Pivot.rotation;
                Vector3 candidate = part.Pending;
                bool changed = (candidate - part.Applied).sqrMagnitude > 0.0000000001f;
                Vector3 origin = part.Pivot.position;
                if (changed && (part.ExposureRevision != passiveObstacleRevision ||
                    !part.ExposureOrigin.Equals(origin) || !part.ExposureRest.Equals(rest)))
                {
                    for (int i = 0; i < part.Samples.Length; i++)
                        part.Exposed[i] = part.Samples[i].sqrMagnitude > PassiveAttachmentRadius * PassiveAttachmentRadius &&
                            PassiveBodyClearance(origin + rest * part.Samples[i]) >= 0.0001f;
                    part.ExposureRevision = passiveObstacleRevision;
                    part.ExposureOrigin = origin;
                    part.ExposureRest = rest;
                }
                if (changed && !PassiveSweepClear(part, rest, candidate))
                {
                    PassiveBlockedSteps++;
                    // Back off within the same bounded angular step; the hinge
                    // remains fixed and no exposed sample may enter the body.
                    float lo = 0f, hi = 1f;
                    for (int i = 0; i < 5; i++)
                    {
                        float middle = (lo + hi) * 0.5f;
                        if (PassiveSweepClear(part, rest, Vector3.Lerp(part.Applied, candidate, middle))) lo = middle;
                        else hi = middle;
                    }
                    candidate = Vector3.Lerp(part.Applied, candidate, lo);
                    part.Pending = candidate;
                    part.Velocity = Vector3.zero;
                }
                part.Applied = candidate;
                Quaternion turned = PassiveRotation(candidate) * rest;
                if (candidate.sqrMagnitude > 0f)
                    foreach (Vector3 point in part.Samples)
                        PassiveContactDisplacementMetres = Mathf.Max(PassiveContactDisplacementMetres,
                            Vector3.Distance(turned * point, rest * point));
                PassiveContactAngleDegrees = Mathf.Max(PassiveContactAngleDegrees, candidate.magnitude * Mathf.Rad2Deg);
            }
            PlaceAnatomy();
        }

        private bool PassiveSweepClear(PassivePart part, Quaternion rest, Vector3 candidate)
        {
            Vector3 origin = part.Pivot.position;
            for (int step = 1; step <= 4; step++)
            {
                Quaternion turned = PassiveRotation(Vector3.Lerp(part.Applied, candidate, step * 0.25f)) * rest;
                for (int i = 0; i < part.Samples.Length; i++)
                {
                    // The authored root weld is inset into the pelvis. Preserve
                    // that seam, while every originally exposed sample stays out.
                    if (!part.Exposed[i]) continue;
                    if (PassiveBodyClearance(origin + turned * part.Samples[i]) < -0.0001f) return false;
                }
            }
            return true;
        }

        private float PassiveBodyClearance(Vector3 point)
        {
            PassiveClearanceQueries++;
            Ray ray = new Ray(point + passiveRayForward * 0.5f, -passiveRayForward);
            float x = Vector3.Dot(ray.origin, passiveRayRight);
            float y = Vector3.Dot(ray.origin, passiveRayUp);
            float nearest = float.PositiveInfinity;
            foreach (PassiveObstacle obstacle in passiveObstacles)
            {
                if (!obstacle.Bounds.IntersectRay(ray)) continue;
                PassiveCollisionWorkCandidates += obstacle.Prepared.Length;
                for (int i = 0; i < obstacle.Prepared.Length; i++)
                {
                    ref readonly PassiveTriangle triangle = ref obstacle.Prepared[i];
                    // Projection perpendicular to the common ray rejects only
                    // impossible hits. A small outward pad retains edge hits.
                    if (x < triangle.MinX || x > triangle.MaxX || y < triangle.MinY || y > triangle.MaxY) continue;
                    PassiveCollisionTriangleTests++;
                    if (triangle.Intersect(ray, out float distance))
                        nearest = Mathf.Min(nearest, distance);
                }
            }
            return nearest - 0.5f;
        }

        private bool TryCreateBridges(Transform parent)
        {
            if (!HomeShowerBridgeResources.TryCreate(
                    HomeShowerBridgeResources.ShoulderYoke, parent, out yoke) ||
                !HomeShowerBridgeResources.TryCreate(
                    HomeShowerBridgeResources.DeltoidLeft, parent, out deltoidLeft) ||
                !HomeShowerBridgeResources.TryCreate(
                    HomeShowerBridgeResources.DeltoidRight, parent, out deltoidRight))
            {
                return false;
            }

            return TryDressBridges();
        }

        /// <summary>
        /// The toilet's authored anatomy, instantiated the way the toilet
        /// does it (the FBX keeps its own unit factor under a pivot), dressed
        /// in the hero's skin and measured onto the bare pelvis once.
        /// </summary>
        private void TryCreateAnatomy(Transform parent)
        {
            GameObject template = Resources.Load<GameObject>(
                HomeToiletFirstPersonView.AnatomyResourcePath);
            GameObject leftTemplate = Resources.Load<GameObject>("HomeToiletAction/Models/ScrotumLeft");
            GameObject rightTemplate = Resources.Load<GameObject>("HomeToiletAction/Models/ScrotumRight");
            if (template == null || leftTemplate == null || rightTemplate == null)
            {
                return;
            }

            anatomyRoot = new GameObject("Home Shower Anatomy").transform;
            anatomyRoot.SetParent(parent, false);
            GameObject model = Instantiate(template, anatomyRoot, false);
            model.name = "Blender Authored Shower Anatomy";
            anatomyAimPivot = FindDescendant(model.transform, "AimPivot");
            scrotumLeft = new GameObject("Home Shower ScrotumLeft Pivot").transform;
            scrotumLeft.SetParent(parent, false);
            Instantiate(leftTemplate, scrotumLeft, false);
            scrotumRight = new GameObject("Home Shower ScrotumRight Pivot").transform;
            scrotumRight.SetParent(parent, false);
            Instantiate(rightTemplate, scrotumRight, false);

            Player3DMeshBinding skin = FindPalette(Player3DBathingAppearance.SkinMaterialName);
            if (skin == null || skin.Renderer == null || skin.Renderer.sharedMaterial == null ||
                !TryMeasureAnatomyBase())
            {
                DestroyPivot(ref anatomyRoot);
                DestroyPivot(ref scrotumLeft);
                DestroyPivot(ref scrotumRight);
                anatomyAimPivot = null;
                return;
            }

            DressPivot(anatomyRoot, skin.Renderer.sharedMaterial, skin.BaseColor, true);
            DressPivot(scrotumLeft, skin.Renderer.sharedMaterial, skin.BaseColor, false);
            DressPivot(scrotumRight, skin.Renderer.sharedMaterial, skin.BaseColor, false);
            passiveParts[0].Capture(anatomyRoot);
            passiveParts[1].Capture(scrotumLeft);
            passiveParts[2].Capture(scrotumRight);
            foreach (Player3DMeshBinding binding in registry.MeshBindings)
            {
                if (binding == null || binding.Bone == null || !(binding.Renderer is SkinnedMeshRenderer skinned)) continue;
                if (binding.MeshName != "GEO_Pelvis" && binding.MeshName != "GEO_Thigh.L" && binding.MeshName != "GEO_Thigh.R") continue;
                // These three rigid production parts surround the attachments;
                // preserve their actual triangles, not a pelvis-sized collider.
                Mesh sample = new Mesh();
                try
                {
                    skinned.BakeMesh(sample, true);
                    var obstacle = new PassiveObstacle
                    {
                        Bone = binding.Bone,
                        Local = sample.vertices,
                        Triangles = sample.triangles
                    };
                    Quaternion inverse = Quaternion.Inverse(binding.Bone.rotation);
                    for (int i = 0; i < obstacle.Local.Length; i++)
                        obstacle.Local[i] = inverse * (skinned.transform.TransformPoint(obstacle.Local[i]) - binding.Bone.position);
                    obstacle.World = new Vector3[obstacle.Local.Length];
                    obstacle.Prepared = new PassiveTriangle[obstacle.Triangles.Length / 3];
                    passiveObstacles.Add(obstacle);
                }
                finally
                {
                    if (Application.isPlaying) Destroy(sample);
                    else DestroyImmediate(sample);
                }
            }
        }

        /// <summary>
        /// Where the kit sits on the bare body. The HEIGHT comes off the
        /// pelvis anchor, the way the toilet takes it. The bare pelvis mesh
        /// is still baked once at preparation, because only the naked body
        /// can say where its FRONT surface is at that height — the toilet
        /// reads the coat instead, and there is no coat here. Both are then
        /// stored in the pelvis anchor's own frame so the walk, the lean
        /// and the sway carry them along.
        /// </summary>
        private bool TryMeasureAnatomyBase()
        {
            Renderer renderer = null;
            for (int index = 0; index < registry.MeshBindings.Count; index++)
            {
                Player3DMeshBinding binding = registry.MeshBindings[index];
                if (binding != null && binding.MeshName == "GEO_Pelvis")
                {
                    renderer = binding.Renderer;
                    break;
                }
            }

            if (!(renderer is SkinnedMeshRenderer skinned) || skinned.sharedMesh == null)
            {
                return false;
            }

            Mesh sample = new Mesh();
            try
            {
                skinned.BakeMesh(sample, true);
                Vector3[] vertices = sample.vertices;
                if (vertices.Length == 0)
                {
                    return false;
                }

                Matrix4x4 toWorld = skinned.transform.localToWorldMatrix;
                var local = new Vector3[vertices.Length];
                for (int index = 0; index < vertices.Length; index++)
                {
                    local[index] = actor.InverseTransformPoint(
                        toWorld.MultiplyPoint3x4(vertices[index]));
                }

                float baseHeight =
                    actor.InverseTransformPoint(pelvisAnchor.position).y +
                    AnatomyAbovePelvisMetres;
                float front = float.NegativeInfinity;
                for (int index = 0; index < local.Length; index++)
                {
                    if (Mathf.Abs(local[index].y - baseHeight) <= 0.04f)
                    {
                        front = Mathf.Max(front, local[index].z);
                    }
                }

                if (float.IsInfinity(front))
                {
                    front = AnatomyFallbackForwardMetres;
                }

                Vector3 baseLocal = new Vector3(0f, baseHeight, front - AnatomyBaseInsetMetres);
                Vector3 baseWorld = actor.TransformPoint(baseLocal);
                anatomyBaseInPelvis = pelvisAnchor.InverseTransformPoint(baseWorld);
                anatomyRotationInPelvis = Quaternion.Inverse(pelvisAnchor.rotation) *
                    actor.rotation * Quaternion.Euler(AnatomyRestPitchDegrees, 0f, 0f);
                return true;
            }
            finally
            {
                if (Application.isPlaying) Destroy(sample);
                else DestroyImmediate(sample);
            }
        }

        /// <summary>
        /// The pieces wear the hero's own skin: the borrowed skin
        /// material, tinted through a block like every hero part.
        /// </summary>
        private bool TryDressBridges()
        {
            Player3DMeshBinding skin = FindPalette(Player3DBathingAppearance.SkinMaterialName);
            if (skin == null || skin.Renderer == null || skin.Renderer.sharedMaterial == null)
            {
                return false;
            }

            Player3DMeshBinding shadow = FindPalette(Player3DBathingAppearance.SkinShadowMaterialName);
            Color shadowColor = shadow != null ? shadow.BaseColor : skin.BaseColor * 0.75f;
            DressPivot(yoke, skin.Renderer.sharedMaterial, skin.BaseColor, false);
            DressPivot(deltoidLeft, skin.Renderer.sharedMaterial, shadowColor, false);
            DressPivot(deltoidRight, skin.Renderer.sharedMaterial, shadowColor, false);
            return true;
        }

        private void DressPivot(Transform pivot, Material material, Color color, bool darkOutlet)
        {
            var block = new MaterialPropertyBlock();
            Renderer[] renderers = pivot.GetComponentsInChildren<Renderer>(true);
            for (int index = 0; index < renderers.Length; index++)
            {
                Renderer target = renderers[index];
                target.gameObject.layer = actor.gameObject.layer;
                target.sharedMaterial = material;
                target.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On;
                Color tint = darkOutlet && target.name == "Anatomy_Outlet"
                    ? Player3DBathingAppearance.SkinDark
                    : color;
                block.Clear();
                block.SetColor("_BaseColor", tint);
                block.SetColor("_Color", tint);
                target.SetPropertyBlock(block);
            }

            Collider[] colliders = pivot.GetComponentsInChildren<Collider>(true);
            for (int index = 0; index < colliders.Length; index++)
            {
                colliders[index].enabled = false;
            }
        }

        private Player3DMeshBinding FindPalette(string paletteMaterialName)
        {
            for (int index = 0; index < registry.MeshBindings.Count; index++)
            {
                Player3DMeshBinding candidate = registry.MeshBindings[index];
                if (candidate != null && candidate.Renderer != null &&
                    candidate.PaletteMaterialName == paletteMaterialName)
                {
                    return candidate;
                }
            }

            return null;
        }

        private Transform ResolveBone(Player3DAnatomicalPart part)
        {
            return registry.TryGetPart(part, out var binding) && binding != null
                ? binding.Bone
                : null;
        }

        private static Transform FindDescendant(Transform root, string name)
        {
            Transform[] descendants = root.GetComponentsInChildren<Transform>(true);
            for (int index = 0; index < descendants.Length; index++)
            {
                if (descendants[index].name == name) return descendants[index];
            }

            return null;
        }

        private static void DestroyPivot(ref Transform pivot)
        {
            if (pivot != null)
            {
                GameObject instance = pivot.gameObject;
                if (Application.isPlaying) Destroy(instance);
                else DestroyImmediate(instance);
            }

            pivot = null;
        }

        private void OnDisable()
        {
            End();
        }

        private void OnDestroy()
        {
            Release();
        }

        private sealed class PassivePart
        {
            public Transform Pivot;
            public Vector3 Pending, Applied, Velocity;
            public Vector3[] Samples = Array.Empty<Vector3>();
            public bool[] Exposed = Array.Empty<bool>();
            public float Radius;
            public int ExposureRevision = -1;
            public Vector3 ExposureOrigin;
            public Quaternion ExposureRest;

            public void Reset()
            {
                Pending = Applied = Velocity = Vector3.zero;
                ExposureRevision = -1;
            }
            public void Clear()
            {
                Reset(); Pivot = null; Radius = 0f;
                Samples = Array.Empty<Vector3>(); Exposed = Array.Empty<bool>();
            }

            public void Capture(Transform pivot)
            {
                Clear(); Pivot = pivot;
                var points = new HashSet<Vector3>();
                foreach (MeshFilter filter in pivot.GetComponentsInChildren<MeshFilter>(true))
                {
                    if (filter.sharedMesh == null) continue;
                    Vector3[] vertices = filter.sharedMesh.vertices;
                    int[] triangles = filter.sharedMesh.triangles;
                    for (int i = 0; i < vertices.Length; i++)
                    {
                        vertices[i] = pivot.InverseTransformPoint(filter.transform.TransformPoint(vertices[i]));
                        points.Add(vertices[i]);
                    }
                    for (int i = 0; i < triangles.Length; i += 3)
                    {
                        Vector3 a = vertices[triangles[i]], b = vertices[triangles[i + 1]], c = vertices[triangles[i + 2]];
                        points.Add((a + b) * 0.5f); points.Add((b + c) * 0.5f); points.Add((c + a) * 0.5f);
                        points.Add((a + b + c) / 3f);
                    }
                }
                Samples = new Vector3[points.Count]; points.CopyTo(Samples);
                Exposed = new bool[Samples.Length];
                foreach (Vector3 point in Samples) Radius = Mathf.Max(Radius, point.magnitude);
            }
        }

        private sealed class PassiveObstacle
        {
            public Transform Bone;
            public Vector3[] Local, World;
            public int[] Triangles;
            public Bounds Bounds;
            public PassiveTriangle[] Prepared;
            private bool updated;
            private Vector3 previousOrigin;
            private Quaternion previousRotation, previousActorRotation;

            public int Update(Quaternion actorRotation, Vector3 forward, Vector3 right, Vector3 up)
            {
                Quaternion rotation = Bone.rotation;
                Vector3 origin = Bone.position;
                // Exact component equality: even a small pose change refreshes
                // the real vertices and invalidates cached exposure tests.
                if (updated && origin.Equals(previousOrigin) && rotation.Equals(previousRotation) &&
                    actorRotation.Equals(previousActorRotation)) return 0;
                for (int i = 0; i < Local.Length; i++) World[i] = origin + rotation * Local[i];
                Bounds = new Bounds(World[0], Vector3.zero);
                foreach (Vector3 point in World) Bounds.Encapsulate(point);
                Vector3 direction = new Ray(Vector3.zero, -forward).direction;
                for (int i = 0; i < Prepared.Length; i++)
                    Prepared[i] = new PassiveTriangle(World[Triangles[i * 3]],
                        World[Triangles[i * 3 + 1]], World[Triangles[i * 3 + 2]], direction, right, up);
                previousOrigin = origin;
                previousRotation = rotation;
                previousActorRotation = actorRotation;
                updated = true;
                return Local.Length;
            }
        }

        private readonly struct PassiveTriangle
        {
            private readonly Vector3 a, edge, other, cross;
            private readonly float determinant;
            public readonly float MinX, MaxX, MinY, MaxY;

            public PassiveTriangle(Vector3 first, Vector3 second, Vector3 third,
                Vector3 direction, Vector3 right, Vector3 up)
            {
                a = first;
                edge = second - first;
                other = third - first;
                cross = Vector3.Cross(direction, other);
                determinant = Vector3.Dot(edge, cross);
                float ax = Vector3.Dot(first, right), bx = Vector3.Dot(second, right), cx = Vector3.Dot(third, right);
                float ay = Vector3.Dot(first, up), by = Vector3.Dot(second, up), cy = Vector3.Dot(third, up);
                const float pad = 0.00001f;
                MinX = Mathf.Min(ax, Mathf.Min(bx, cx)) - pad;
                MaxX = Mathf.Max(ax, Mathf.Max(bx, cx)) + pad;
                MinY = Mathf.Min(ay, Mathf.Min(by, cy)) - pad;
                MaxY = Mathf.Max(ay, Mathf.Max(by, cy)) + pad;
            }

            public bool Intersect(Ray ray, out float distance)
            {
                // Same triangle test and tolerances as before; only its
                // direction-invariant edge products are prepared once.
                distance = 0f;
                if (Mathf.Abs(determinant) < 0.00000001f) return false;
                Vector3 relative = ray.origin - a;
                float u = Vector3.Dot(relative, cross) / determinant;
                if (u < 0f || u > 1f) return false;
                Vector3 q = Vector3.Cross(relative, edge);
                float v = Vector3.Dot(ray.direction, q) / determinant;
                if (v < 0f || u + v > 1f) return false;
                distance = Vector3.Dot(other, q) / determinant;
                return distance > 0.000001f;
            }
        }

        /// <summary>One arm's bones, its measured hand frame and its solve.</summary>
        private sealed class Arm
        {
            public Transform Upper;
            public Transform Forearm;
            public Transform Hand;
            public Quaternion HandFrameInHand;
            public Vector3 PalmOffsetInHand;
            public float ChainLength;

            public Vector3 PalmPosition => Hand.position + Hand.rotation * PalmOffsetInHand;

            public bool Resolve(Player3DAssetRegistry registry, bool rightSide)
            {
                Upper = Bone(registry, rightSide ? Player3DAnatomicalPart.RightUpperArm : Player3DAnatomicalPart.LeftUpperArm);
                Forearm = Bone(registry, rightSide ? Player3DAnatomicalPart.RightForearm : Player3DAnatomicalPart.LeftForearm);
                Hand = Bone(registry, rightSide ? Player3DAnatomicalPart.RightHand : Player3DAnatomicalPart.LeftHand);
                Transform grip = rightSide ? registry.Anchors.RightGrip : registry.Anchors.LeftGrip;
                if (Upper == null || Forearm == null || Hand == null || grip == null)
                {
                    return false;
                }

                if (!TryRigidMeshCenter(registry, rightSide ? "GEO_Hand.R" : "GEO_Hand.L", out Vector3 handCenter) ||
                    !TryRigidMeshCenter(registry, rightSide ? "GEO_Thumb.R" : "GEO_Thumb.L", out Vector3 thumbCenter))
                {
                    return false;
                }

                // The same measured frame the toilet uses for the right
                // hand, taken for both: fingers run from the wrist to the
                // grip socket, the thumb sits off that line.
                Vector3 fingers = grip.position - Hand.position;
                Vector3 thumb = Vector3.ProjectOnPlane(thumbCenter - handCenter, fingers);
                if (fingers.sqrMagnitude < 0.000001f || thumb.sqrMagnitude < 0.000001f)
                {
                    return false;
                }

                HandFrameInHand = Quaternion.Inverse(Hand.rotation) *
                    Quaternion.LookRotation(fingers.normalized, thumb.normalized);
                PalmOffsetInHand = Quaternion.Inverse(Hand.rotation) * (handCenter - Hand.position);
                ChainLength = LimbTwoBoneIk.ChainLength(Upper, Forearm, Hand);
                return ChainLength > 0.1f;
            }

            public void Solve(Vector3 palm, Quaternion handRotation, Vector3 hint, float weight)
            {
                Vector3 wrist = palm - handRotation * PalmOffsetInHand;
                LimbTwoBoneIk.Solve(
                    Upper, Forearm, Hand,
                    wrist, handRotation, hint,
                    weight, LimbTwoBoneIk.DefaultReachFraction, true);
            }

            private static Transform Bone(Player3DAssetRegistry registry, Player3DAnatomicalPart part)
            {
                return registry.TryGetPart(part, out var binding) && binding != null
                    ? binding.Bone
                    : null;
            }

            private static bool TryRigidMeshCenter(
                Player3DAssetRegistry registry,
                string meshName,
                out Vector3 center)
            {
                center = Vector3.zero;
                Renderer renderer = null;
                for (int index = 0; index < registry.MeshBindings.Count; index++)
                {
                    Player3DMeshBinding binding = registry.MeshBindings[index];
                    if (binding != null && binding.MeshName == meshName)
                    {
                        renderer = binding.Renderer;
                        break;
                    }
                }

                if (!(renderer is SkinnedMeshRenderer skinned) || skinned.sharedMesh == null)
                {
                    return false;
                }

                // Production imports disable mesh Read/Write; bake once.
                Mesh sample = new Mesh();
                try
                {
                    skinned.BakeMesh(sample, true);
                    Vector3[] vertices = sample.vertices;
                    if (vertices.Length == 0)
                    {
                        return false;
                    }

                    for (int index = 0; index < vertices.Length; index++)
                    {
                        center += vertices[index];
                    }

                    center = skinned.transform.TransformPoint(center / vertices.Length);
                    return true;
                }
                finally
                {
                    if (Application.isPlaying) Destroy(sample);
                    else DestroyImmediate(sample);
                }
            }
        }
    }
}

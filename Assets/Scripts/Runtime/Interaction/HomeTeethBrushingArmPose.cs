using System.Collections.Generic;
using UnityEngine;

namespace BarPromenade
{
    /// <summary>A measured arm/contact guard for the brush or free-hand valve reach, with a connected spit bend.</summary>
    public sealed class HomeTeethBrushingArmPose : MonoBehaviour
    {
        public const float MinimumBodyClearance = 0.002f;
        // The authored Idle0 shoulder/axilla junction extends 0.19 m along
        // this 0.301 m bone. Exclude that joined seam, not the free arm.
        private const float ShoulderJoinLength = 0.195f;
        private static readonly Vector3 InsideRayDirection = new Vector3(0.173f, 0.469f, 0.866f).normalized;
        private sealed class BodySurface
        {
            public Renderer Renderer;
            public Vector3[] Vertices;
            public int[] Triangles;
            public Bounds Bounds;
            public Bounds[] TriangleBounds;
            public bool IsClosed;
            public bool TopologyChecked;
        }
        private sealed class ArmSurface
        {
            public int Kind;
            public bool IsPalm;
            public Transform Bone;
            // Independent clipped triangles in bone space, measured in metres.
            public Vector3[] LocalVertices;
            public Vector3[] WorldVertices;
        }
        private Player3DAssetRegistry registry;
        private Transform actor, upperArm, forearm, hand, head, neck, chest, spine;
        private Transform[] bones;
        private Quaternion[] neutral;
        private Quaternion socketInHand;
        private Quaternion handFrameInHand;
        private Quaternion handInForearm;
        private Vector3 handCenter, thumbCenter;
        private Vector3 measuredHandCenterInHand;
        private Vector3 rightInActor, forwardInActor, faceRightInHead, faceUpInHead, faceForwardInHead, outsideInHead;
        private Quaternion[] safePose;
        private float safeBend;
        private float safeValveLean;
        private bool hasSafePose;
        private Vector3 Outside => actor.rotation * rightInActor;
        private Vector3 Forward => actor.rotation * forwardInActor;
        private Vector3 tipInHand, handStartInHand, handEndInHand;
        private float upperArmRadius = 0.055f, forearmRadius = 0.05f, handRadius = 0.04f;
        private readonly List<BodySurface> bodySurfaces = new List<BodySurface>();
        private readonly List<ArmSurface> armSurfaces = new List<ArmSurface>();
        private readonly List<Vector3> sampledVertices = new List<Vector3>();
        private readonly List<Vector3> handVertices = new List<Vector3>();
        private Mesh sample;
        private Vector2 previousTip;
        private bool captured, sampled;
        private bool leftHand;
        private bool inputDrivenContact;
        private Transform Grip => leftHand ? registry.Anchors.LeftGrip : registry.Anchors.RightGrip;
        public float Weight { get; private set; }
        public Transform Effector { get; set; }
        public float ContactError { get; private set; }
        public float ActualBrushTravel { get; private set; }
        public float Bend { get; private set; }
        public float ValveLean { get; private set; }
        /// <summary>Measured broad-phase radii: upper arm, forearm and hand, in metres.</summary>
        public Vector3 ArmRadii => new Vector3(upperArmRadius, forearmRadius, handRadius);
        /// <summary>Clearances after mesh confirmation: upper arm, forearm and hand.</summary>
        public Vector3 ArmClearances { get; private set; }
        /// <summary>Actual body gap, capped at 1 cm after mesh confirmation; negative for an intersection.</summary>
        public float BodyClearance { get; private set; }
        /// <summary>Number of distal upper-arm/forearm/hand capsules crossing or contained in the posed body.</summary>
        public int BodyIntersectionCount { get; private set; }
        /// <summary>First confirmed body surface, limb and intersection type in the current pose.</summary>
        public string BodyIntersectionDetail { get; private set; } = string.Empty;
        public string LastContactRejectReason { get; private set; } = string.Empty;
        /// <summary>Measured anatomical frame: forward is fingers, up is thumb.</summary>
        public Quaternion PhysicalHandFrame => hand.rotation * handFrameInHand;
        public Vector3 PalmCenter => hand.position + hand.rotation * measuredHandCenterInHand;

        /// <summary>The actual bare palm surface along its measured inward normal.</summary>
        public bool TryGetPalmSurfacePoint(out Vector3 point)
        {
            point = PalmCenter;
            if (!captured) return false;
            Vector3 direction = (leftHand ? 1f : -1f) * (handFrameInHand * Vector3.right);
            float nearest = float.PositiveInfinity;
            foreach (ArmSurface surface in armSurfaces)
            {
                if (!surface.IsPalm) continue;
                for (int index = 0; index < surface.LocalVertices.Length; index += 3)
                    if (RayTriangle(measuredHandCenterInHand, direction, surface.LocalVertices[index],
                            surface.LocalVertices[index + 1], surface.LocalVertices[index + 2], out float distance) &&
                        distance >= 0f && distance < nearest) nearest = distance;
            }
            if (float.IsPositiveInfinity(nearest)) return false;
            point = hand.position + hand.rotation * (measuredHandCenterInHand + direction * nearest);
            return true;
        }

        /// <summary>
        /// A related contact interaction can check its own complete body set,
        /// including static authored attachments. Ordinary brushing keeps its
        /// existing torso/pelvis set unless this explicit opt-in is used.
        /// </summary>
        public void ConfigureContactBody(IEnumerable<Renderer> renderers)
        {
            bodySurfaces.Clear();
            foreach (Renderer renderer in renderers)
                if (renderer != null && (renderer is SkinnedMeshRenderer || renderer.GetComponent<MeshFilter>() != null))
                    bodySurfaces.Add(new BodySurface { Renderer = renderer });
        }
        public void Initialize(Player3DAssetRegistry value, Transform player, bool useLeftHand = false)
        {
            registry = value; actor = player; leftHand = useLeftHand;
            upperArm = Bone(leftHand ? Player3DAnatomicalPart.LeftUpperArm : Player3DAnatomicalPart.RightUpperArm);
            forearm = Bone(leftHand ? Player3DAnatomicalPart.LeftForearm : Player3DAnatomicalPart.RightForearm);
            hand = Bone(leftHand ? Player3DAnatomicalPart.LeftHand : Player3DAnatomicalPart.RightHand);
            head = Bone(Player3DAnatomicalPart.Head);
            neck = Bone(Player3DAnatomicalPart.Neck);
            chest = Bone(Player3DAnatomicalPart.Torso);
            // Hero V2's continuous torso has no LowerTorso mesh binding;
            // its authored lumbar bone is exposed by the anchor registry.
            spine = registry.Anchors.Spine;
            bones = new[] { spine, chest, neck, head, upperArm, forearm, hand };
            neutral = new Quaternion[bones.Length];
            safePose = new Quaternion[bones.Length];
            sample = new Mesh { name = "Brushing Pose Readback", hideFlags = HideFlags.HideAndDontSave };
            bodySurfaces.Clear();
            foreach (Player3DMeshBinding binding in registry.MeshBindings)
                if ((binding.MeshName == "GEO_Torso" || binding.MeshName == "CLO_JacketBody" ||
                     binding.MeshName == "GEO_Pelvis") && binding.Renderer is SkinnedMeshRenderer renderer)
                    bodySurfaces.Add(new BodySurface { Renderer = renderer });
        }
        public void Capture()
        {
            for (int index = 0; index < bones.Length; index++)
                if (bones[index] != null) neutral[index] = bones[index].localRotation;
            socketInHand = Quaternion.Inverse(hand.rotation) * Grip.rotation;
            handInForearm = Quaternion.Inverse(forearm.rotation) * hand.rotation;
            tipInHand = Quaternion.Inverse(hand.rotation) * (Effector.position - hand.position);
            // Neither imported bone axes nor actor.right name the anatomical
            // side: the production model has its own 180-degree root rotation.
            Transform otherShoulder = Bone(leftHand ? Player3DAnatomicalPart.RightUpperArm : Player3DAnatomicalPart.LeftUpperArm);
            Vector3 outside = Vector3.ProjectOnPlane(upperArm.position - otherShoulder.position, actor.up).normalized;
            Vector3 forward = Vector3.ProjectOnPlane(registry.Anchors.Mouth.position - head.position, actor.up).normalized;
            if (forward.sqrMagnitude < 0.001f) forward = actor.forward;
            rightInActor = Quaternion.Inverse(actor.rotation) * outside;
            outsideInHead = Quaternion.Inverse(head.rotation) * outside;
            forwardInActor = Quaternion.Inverse(actor.rotation) * forward;
            // The screen basis is geometric, separate from imported .R/.L.
            faceRightInHead = Quaternion.Inverse(head.rotation) * Vector3.Cross(actor.up, forward).normalized;
            faceUpInHead = Quaternion.Inverse(head.rotation) * actor.up;
            faceForwardInHead = Quaternion.Inverse(head.rotation) * forward;
            MeasureArmVolumes();
            measuredHandCenterInHand = Quaternion.Inverse(hand.rotation) * (handCenter - hand.position);
            Vector3 fingers = Grip.position - hand.position;
            Vector3 thumb = Vector3.ProjectOnPlane(thumbCenter - handCenter, fingers);
            handFrameInHand = Quaternion.Inverse(hand.rotation) *
                Quaternion.LookRotation(fingers.normalized, thumb.normalized);
            sampled = false; captured = true;
            RefreshBody();
            MeasureBodyClearance();
            hasSafePose = false;
            RememberSafePose();
        }

        /// <summary>
        /// Solve an input-driven world contact on the already captured body
        /// endpoint. The caller owns bounded contact travel. Every new pose,
        /// including intermediate joint blends from the last accepted pose,
        /// must pass the same real-mesh guard used by brushing.
        /// </summary>
        public bool ApplyWorldContact(Vector3 target, Quaternion physicalHandFrame)
        {
            if (!captured || Effector == null) return false;
            LastContactRejectReason = string.Empty;
            Quaternion[] previous = hasSafePose ? (Quaternion[])safePose.Clone() : null;
            // ApplyBrace has already written this frame's neutral arm. The
            // contact path must resume the last accepted arm in the current
            // torso frame, otherwise CCD can choose the opposite elbow branch
            // while the wrist turns and the joint blend cuts through the body.
            for (int index = 4; index < bones.Length; index++)
                if (bones[index] != null) bones[index].localRotation = previous != null ? previous[index] : neutral[index];
            Vector3 previousElbow = forearm.position;
            RefreshBody();
            Quaternion rotation = physicalHandFrame * Quaternion.Inverse(handFrameInHand);
            Vector3 wrist = target - rotation * tipInHand;
            Vector3 hint = previous != null ? previousElbow :
                upperArm.position + Outside * 0.36f + Forward * 0.34f - actor.up * 0.20f;
            inputDrivenContact = true;
            try { SolveClearPose(wrist, rotation, hint, hand.position, hand.rotation, forearm.position); }
            finally { inputDrivenContact = false; }
            Quaternion[] destination = new Quaternion[3];
            for (int index = 0; index < 3; index++) destination[index] = bones[index + 4].localRotation;
            bool clear = BodyIntersectionCount == 0;
            if (!clear) LastContactRejectReason = "candidate: " + BodyIntersectionDetail;
            if (clear && previous != null)
            {
                for (int step = 1; step <= 4 && clear; step++)
                {
                    for (int index = 0; index < 3; index++)
                        bones[index + 4].localRotation = Quaternion.Slerp(previous[index + 4], destination[index], step * 0.25f);
                    MeasureBodyClearance();
                    clear = BodyIntersectionCount == 0;
                    if (!clear) LastContactRejectReason = "swept blend " + step + ": " + BodyIntersectionDetail;
                }
            }
            if (!clear && previous != null)
                for (int index = 4; index < bones.Length; index++) bones[index].localRotation = previous[index];
            MeasureBodyClearance();
            RememberSafePose();
            ContactError = Vector3.Distance(Effector.position, target);
            if (ContactError > 0.015f)
                LastContactRejectReason = "target error " + ContactError.ToString("F4") + "; " + LastContactRejectReason;
            return clear && BodyIntersectionCount == 0 && ContactError <= 0.015f;
        }

        /// <summary>Restore only this arm; the parent interaction owns the body.</summary>
        public void RestoreContactArm()
        {
            if (!captured) return;
            for (int index = 4; index < bones.Length; index++)
                if (bones[index] != null) bones[index].localRotation = neutral[index];
            RefreshBody();
            MeasureBodyClearance();
            RememberSafePose();
        }

        /// <summary>Re-present the last accepted arm on the current torso before picking its visible skin.</summary>
        public bool RestoreLastContactArm()
        {
            if (!captured || !hasSafePose) return false;
            for (int index = 4; index < bones.Length; index++)
                if (bones[index] != null) bones[index].localRotation = safePose[index];
            return true;
        }
        public void Apply(Vector2 brushOffset, float weight, float bend, float valveReach = 0f)
        {
            if (!captured || Effector == null) return;
            RestoreBones();
            Weight = Mathf.Clamp01(weight); Bend = Mathf.Clamp01(bend); ValveLean = Mathf.Clamp01(valveReach);
            // Reaching the central tap needs a waist/chest lean, not the
            // predominantly neck/head dip used to spit. Feet and root stay
            // at the grounded dock while the shoulder moves within arm reach.
            Pitch(spine, 8f * Bend + 25f * ValveLean); Pitch(chest, 12f * Bend + 20f * ValveLean);
            Pitch(neck, 10f * Bend + 5f * ValveLean); Pitch(head, 18f * Bend + 8f * ValveLean);
            Vector3 faceRight = head.rotation * faceRightInHead;
            Vector3 faceUp = head.rotation * faceUpInHead;
            Vector3 faceForward = head.rotation * faceForwardInHead;
            Vector3 target = registry.Anchors.Mouth.position + faceRight * brushOffset.x +
                faceUp * brushOffset.y + faceForward * 0.007f;
            RefreshBody();
            if (Weight > 0f)
            {
                Quaternion brushRotation = Quaternion.LookRotation(-faceForward, -(head.rotation * outsideInHead));
                Quaternion destinationRotation = brushRotation * Quaternion.Inverse(socketInHand);
                Vector3 destination = target - destinationRotation * tipInHand;
                Vector3 restWrist = hand.position;
                Quaternion restRotation = hand.rotation;
                Vector3 restElbow = forearm.position;
                // Interpolate the wrist along a visible outside/front arc and
                // solve the complete chain. Blending joint rotations directly
                // made the wrist take a chord through the jacket.
                float arc = Mathf.Sin(Mathf.PI * Weight);
                Vector3 wrist = Vector3.Lerp(restWrist, destination, Weight) +
                    (Outside * 0.12f + Forward * 0.16f) * arc;
                Quaternion rotation = Quaternion.Slerp(restRotation, destinationRotation, Weight);
                Vector3 outsideHint = upperArm.position + Outside * 0.40f +
                    Forward * 0.24f - actor.up * 0.22f;
                Vector3 hint = Vector3.Lerp(restElbow, outsideHint, Mathf.Clamp01(Weight * 4f));
                SolveClearPose(wrist, rotation, hint, restWrist, restRotation, restElbow);
            }
            else
            {
                MeasureBodyClearance();
                if (BodyClearance < MinimumBodyClearance)
                    SolveClearPose(hand.position, hand.rotation,
                        upperArm.position + Outside * 0.40f + Forward * 0.24f - actor.up * 0.22f,
                        hand.position, hand.rotation, forearm.position);
            }
            if (BodyIntersectionCount > 0 && hasSafePose)
            {
                // An impossible target never replaces the last visibly safe
                // pose. This also rejects a bend that closes off every route.
                for (int index = 0; index < bones.Length; index++)
                    if (bones[index] != null) bones[index].localRotation = safePose[index];
                Bend = safeBend;
                ValveLean = safeValveLean;
                RefreshBody();
                MeasureBodyClearance();
            }
            RememberSafePose();
            Vector3 relative = Effector.position - registry.Anchors.Mouth.position;
            Vector2 tip = new Vector2(Vector3.Dot(relative, faceRight), Vector3.Dot(relative, faceUp));
            ContactError = Vector3.Distance(Effector.position, target);
            ActualBrushTravel = sampled && Weight > 0.99f && BodyIntersectionCount == 0 ? Vector2.Distance(tip, previousTip) : 0f;
            previousTip = tip; sampled = Weight > 0.99f && BodyIntersectionCount == 0;
        }

        /// <summary>The free hand follows the physical valve, after the brushing/body pose.</summary>
        public void ApplyValveGrip(Vector3 target, Quaternion gripRotation, float weight)
        {
            if (!captured || Effector == null) return;
            // The other pose owns the spine/head. Restore only this arm.
            for (int index = 4; index < bones.Length; index++)
                if (bones[index] != null) bones[index].localRotation = neutral[index];
            Weight = Mathf.Clamp01(weight);
            RefreshBody();
            if (Weight > 0f)
            {
                Vector3 rest = hand.position, restElbow = forearm.position;
                Quaternion restRotation = hand.rotation;
                // A valve's frame describes the physical fingers and thumb,
                // measured from this hand. The imported grip socket axes do
                // not name those anatomical directions.
                Quaternion destinationRotation = gripRotation * Quaternion.Inverse(handFrameInHand);
                Vector3 destination = target - destinationRotation * tipInHand;
                Vector3 wrist = Vector3.Lerp(rest, destination, Weight) +
                    (Outside * 0.10f + Forward * 0.08f) * Mathf.Sin(Mathf.PI * Weight);
                Vector3 hint = upperArm.position + Outside * 0.30f + Forward * 0.20f - actor.up * 0.25f;
                SolveClearPose(wrist, Quaternion.Slerp(restRotation, destinationRotation, Weight),
                    hint, rest, restRotation, restElbow);
            }
            else MeasureBodyClearance();
            if (BodyIntersectionCount > 0 && hasSafePose)
            {
                for (int index = 4; index < bones.Length; index++)
                    if (bones[index] != null) bones[index].localRotation = safePose[index];
                MeasureBodyClearance();
            }
            RememberSafePose();
            ContactError = Vector3.Distance(Effector.position, target);
        }
        private void SolveClearPose(Vector3 wrist, Quaternion rotation, Vector3 hint,
            Vector3 restWrist, Quaternion restRotation, Vector3 restElbow)
        {
            Quaternion baseUpper = upperArm.localRotation, baseLower = forearm.localRotation, baseHand = hand.localRotation;
            Quaternion bestUpper = baseUpper, bestLower = baseLower, bestHand = baseHand;
            float bestClearance = float.NegativeInfinity;
            // First retain the exact brush target, changing only the elbow's
            // outside bend plane. Only an obstructed target moves outwards.
            for (int attempt = 0; attempt < (inputDrivenContact ? 19 : 15); attempt++)
            {
                upperArm.localRotation = baseUpper; forearm.localRotation = baseLower; hand.localRotation = baseHand;
                Vector3 candidateWrist = wrist;
                Quaternion candidateRotation = rotation;
                Vector3 candidateHint = hint;
                // Soap can touch a low central surface while the forearm
                // stays in front of the belly. Search these extra elbow
                // planes at the exact target before moving the contact out.
                bool forwardContactPlane = inputDrivenContact && attempt >= 5 && attempt < 9;
                int fallbackAttempt = inputDrivenContact && attempt >= 9 ? attempt - 4 : attempt;
                if (forwardContactPlane)
                {
                    float outside = attempt == 5 ? 0.20f : attempt == 6 ? 0.08f : attempt == 7 ? -0.08f : 0.42f;
                    candidateHint = upperArm.position + Outside * outside + Forward * 0.75f - actor.up * 0.18f;
                }
                else if (fallbackAttempt > 0 && fallbackAttempt < 5)
                {
                    candidateHint = upperArm.position + Outside * (0.32f + fallbackAttempt * 0.05f) +
                        Forward * (0.12f + fallbackAttempt * 0.11f) - actor.up * (0.26f - fallbackAttempt * 0.035f);
                }
                else if (fallbackAttempt >= 5 && fallbackAttempt < 10)
                {
                    float distance = (fallbackAttempt - 4) * 0.025f;
                    candidateWrist += (Outside * 0.7f + Forward) * distance;
                    candidateHint = upperArm.position + Outside * 0.48f + Forward * 0.35f - actor.up * 0.15f;
                }
                else if (fallbackAttempt >= 10)
                {
                    // A blocked brushing target is rejected in favour of an
                    // outside holding pose. It cannot earn brushing progress.
                    float amount = (fallbackAttempt - 9) / 5f;
                    candidateWrist = Vector3.Lerp(wrist, restWrist + Outside * 0.06f + Forward * 0.10f, amount);
                    candidateRotation = Quaternion.Slerp(rotation, restRotation, amount);
                    candidateHint = Vector3.Lerp(hint, restElbow + Outside * 0.14f + Forward * 0.18f, amount);
                }
                if (leftHand && !inputDrivenContact) SolveValveWrist(candidateWrist, candidateRotation, candidateHint);
                else LimbTwoBoneIk.Solve(upperArm, forearm, hand, candidateWrist, candidateRotation,
                    candidateHint, 1f, float.PositiveInfinity, true);
                MeasureBodyClearance();
                if (inputDrivenContact && attempt == 0 && BodyIntersectionCount > 0)
                    LastContactRejectReason = "requested pose: " + BodyIntersectionDetail;
                if (BodyClearance > bestClearance)
                {
                    bestClearance = BodyClearance;
                    bestUpper = upperArm.localRotation; bestLower = forearm.localRotation; bestHand = hand.localRotation;
                }
                if (BodyClearance >= MinimumBodyClearance) return;
            }
            upperArm.localRotation = bestUpper; forearm.localRotation = bestLower; hand.localRotation = bestHand;
            MeasureBodyClearance();
        }
        private void SolveValveWrist(Vector3 wrist, Quaternion rotation, Vector3 hint)
        {
            // Keep the grip on its moving contact point while limiting wrist
            // deviation. Changing the hand angle also changes its wrist target,
            // so solve both together instead of rotating the hand after IK.
            Vector3 contact = wrist + rotation * tipInHand;
            Quaternion requestedFrame = rotation * handFrameInHand;
            Vector3 requestedFingers = requestedFrame * Vector3.forward;
            Vector3 requestedThumb = requestedFrame * Vector3.up;
            for (int pass = 0; pass < 6; pass++)
            {
                LimbTwoBoneIk.Solve(upperArm, forearm, hand, contact - rotation * tipInHand,
                    rotation, hint, 1f, float.PositiveInfinity, true);
                Vector3 armAxis = (hand.position - forearm.position).normalized;
                Vector3 fingers = Vector3.RotateTowards(armAxis, requestedFingers,
                    25f * Mathf.Deg2Rad, 0f);
                Vector3 thumb = Vector3.ProjectOnPlane(requestedThumb, fingers);
                if (thumb.sqrMagnitude < 0.0001f)
                    thumb = Vector3.ProjectOnPlane(
                        forearm.rotation * handInForearm * handFrameInHand * Vector3.up, fingers);
                Vector3.OrthoNormalize(ref fingers, ref thumb);
                Quaternion next = Quaternion.LookRotation(fingers, thumb) * Quaternion.Inverse(handFrameInHand);
                if (Quaternion.Angle(next, rotation) < 0.05f) break;
                rotation = next;
            }
            LimbTwoBoneIk.Solve(upperArm, forearm, hand, contact - rotation * tipInHand,
                rotation, hint, 1f, float.PositiveInfinity, true);

            // Pronation belongs to the forearm, not an axial kink in the wrist.
            // Rolling about elbow-to-wrist leaves the solved wrist in place.
            Vector3 axis = (hand.position - forearm.position).normalized;
            Quaternion neutralFrame = forearm.rotation * handInForearm * handFrameInHand;
            Vector3 neutralThumb = Vector3.ProjectOnPlane(neutralFrame * Vector3.up, axis);
            Vector3 turnedThumb = Vector3.ProjectOnPlane(rotation * handFrameInHand * Vector3.up, axis);
            float roll = Vector3.SignedAngle(neutralThumb, turnedThumb, axis);
            forearm.rotation = Quaternion.AngleAxis(roll, axis) * forearm.rotation;
            hand.rotation = rotation;
        }
        private void RememberSafePose()
        {
            if (BodyIntersectionCount != 0) return;
            for (int index = 0; index < bones.Length; index++)
                if (bones[index] != null) safePose[index] = bones[index].localRotation;
            safeBend = Bend;
            safeValveLean = ValveLean;
            hasSafePose = true;
        }
        private void MeasureArmVolumes()
        {
            upperArmRadius = forearmRadius = 0f;
            handCenter = thumbCenter = Vector3.zero;
            handVertices.Clear();
            armSurfaces.Clear();
            foreach (Player3DMeshBinding binding in registry.MeshBindings)
            {
                string side = leftHand ? ".L" : ".R";
                bool isUpper = binding.MeshName == "GEO_UpperArm" + side || binding.MeshName == "CLO_JacketSleeve" + side;
                bool isForearm = binding.MeshName == "GEO_Forearm" + side || binding.MeshName == "CLO_JacketForearm" + side ||
                    (leftHand && binding.MeshName == "CLO_Bandage.L");
                bool isHand = binding.MeshName == "GEO_Hand" + side || binding.MeshName == "GEO_Thumb" + side;
                if ((!isUpper && !isForearm && !isHand) || !(binding.Renderer is SkinnedMeshRenderer renderer)) continue;
                ReadWorldVertices(renderer);
                if (isHand)
                {
                    Vector3 center = Vector3.zero;
                    foreach (Vector3 vertex in sampledVertices) center += vertex;
                    center /= sampledVertices.Count;
                    if (binding.MeshName == "GEO_Hand" + side) handCenter = center;
                    else thumbCenter = center;
                }
                CaptureArmSurface(isUpper ? 0 : isForearm ? 1 : 2,
                    isUpper ? upperArm : isForearm ? forearm : hand, binding.MeshName == "GEO_Hand" + side);
                foreach (Vector3 vertex in sampledVertices)
                {
                    if (isUpper) upperArmRadius = Mathf.Max(upperArmRadius,
                        Mathf.Sqrt(PointSegmentSquared(vertex, upperArm.position, forearm.position)));
                    if (isForearm) forearmRadius = Mathf.Max(forearmRadius,
                        Mathf.Sqrt(PointSegmentSquared(vertex, forearm.position, hand.position)));
                    if (isHand) handVertices.Add(vertex);
                }
            }
            Vector3 axis = (Grip.position - hand.position).normalized;
            float start = 0f, end = 0f;
            foreach (Vector3 vertex in handVertices)
            {
                float projection = Vector3.Dot(vertex - hand.position, axis);
                start = Mathf.Min(start, projection); end = Mathf.Max(end, projection);
            }
            Vector3 worldStart = hand.position + axis * start, worldEnd = hand.position + axis * end;
            handRadius = 0f;
            foreach (Vector3 vertex in handVertices)
                handRadius = Mathf.Max(handRadius, Mathf.Sqrt(PointSegmentSquared(vertex, worldStart, worldEnd)));
            upperArmRadius = Mathf.Max(0.04f, upperArmRadius);
            forearmRadius = Mathf.Max(0.035f, forearmRadius);
            handRadius = Mathf.Max(0.03f, handRadius);
            handStartInHand = Quaternion.Inverse(hand.rotation) * (worldStart - hand.position);
            handEndInHand = Quaternion.Inverse(hand.rotation) * (worldEnd - hand.position);
        }
        private void CaptureArmSurface(int kind, Transform bone, bool isPalm)
        {
            int[] triangles = sample.triangles;
            var vertices = new List<Vector3>();
            var polygon = new List<Vector3>(4);
            Vector3 upperAxis = (forearm.position - upperArm.position).normalized;
            Quaternion inverse = Quaternion.Inverse(bone.rotation);
            for (int index = 0; index < triangles.Length; index += 3)
            {
                polygon.Clear();
                for (int corner = 0; corner < 3; corner++)
                {
                    Vector3 a = sampledVertices[triangles[index + corner]];
                    Vector3 b = sampledVertices[triangles[index + (corner + 1) % 3]];
                    if (kind != 0) { polygon.Add(a); continue; }
                    float from = Vector3.Dot(a - upperArm.position, upperAxis) - ShoulderJoinLength;
                    float to = Vector3.Dot(b - upperArm.position, upperAxis) - ShoulderJoinLength;
                    if (from >= 0f) polygon.Add(a);
                    if ((from >= 0f) != (to >= 0f)) polygon.Add(Vector3.Lerp(a, b, from / (from - to)));
                }
                for (int corner = 1; corner + 1 < polygon.Count; corner++)
                {
                    vertices.Add(inverse * (polygon[0] - bone.position));
                    vertices.Add(inverse * (polygon[corner] - bone.position));
                    vertices.Add(inverse * (polygon[corner + 1] - bone.position));
                }
            }
            armSurfaces.Add(new ArmSurface
            {
                Kind = kind, IsPalm = isPalm, Bone = bone, LocalVertices = vertices.ToArray(),
                WorldVertices = new Vector3[vertices.Count]
            });
        }
        private void ReadWorldVertices(SkinnedMeshRenderer renderer)
        {
            sample.Clear(false);
            // Match the production foot probe and rendered-body readback:
            // this FBX hierarchy needs useScale=true before TransformPoint.
            // false leaves a 100x-scale displacement in the sampled geometry.
            renderer.BakeMesh(sample, true);
            sample.GetVertices(sampledVertices);
            Matrix4x4 world = renderer.transform.localToWorldMatrix;
            for (int index = 0; index < sampledVertices.Count; index++) sampledVertices[index] = world.MultiplyPoint3x4(sampledVertices[index]);
        }
        private Mesh ReadWorldVertices(Renderer renderer)
        {
            if (renderer is SkinnedMeshRenderer skinned)
            {
                ReadWorldVertices(skinned);
                return sample;
            }
            MeshFilter filter = renderer.GetComponent<MeshFilter>();
            sampledVertices.Clear();
            if (filter == null || filter.sharedMesh == null) return null;
            // Static attachments only move as transforms. Reading their shared
            // vertices avoids uploading unchanged geometry to the bake mesh.
            Mesh source = filter.sharedMesh;
            source.GetVertices(sampledVertices);
            Matrix4x4 world = renderer.transform.localToWorldMatrix;
            for (int index = 0; index < sampledVertices.Count; index++) sampledVertices[index] = world.MultiplyPoint3x4(sampledVertices[index]);
            return source;
        }
        private void RefreshBody()
        {
            foreach (BodySurface surface in bodySurfaces)
            {
                Mesh source = ReadWorldVertices(surface.Renderer);
                if (source == null || sampledVertices.Count == 0) continue;
                if (surface.Vertices == null || surface.Vertices.Length != sampledVertices.Count)
                {
                    surface.Vertices = new Vector3[sampledVertices.Count];
                    surface.Triangles = source.triangles;
                    surface.TriangleBounds = new Bounds[surface.Triangles.Length / 3];
                    surface.TopologyChecked = false;
                }
                sampledVertices.CopyTo(surface.Vertices);
                if (!surface.TopologyChecked)
                {
                    surface.IsClosed = IsClosedSurface(surface);
                    surface.TopologyChecked = true;
                }
                surface.Bounds = new Bounds(surface.Vertices[0], Vector3.zero);
                for (int index = 0; index < surface.Vertices.Length; index++) surface.Bounds.Encapsulate(surface.Vertices[index]);
                for (int index = 0; index < surface.Triangles.Length; index += 3)
                {
                    Bounds bounds = new Bounds(surface.Vertices[surface.Triangles[index]], Vector3.zero);
                    bounds.Encapsulate(surface.Vertices[surface.Triangles[index + 1]]);
                    bounds.Encapsulate(surface.Vertices[surface.Triangles[index + 2]]);
                    surface.TriangleBounds[index / 3] = bounds;
                }
            }
        }
        private void MeasureBodyClearance()
        {
            BodyIntersectionDetail = string.Empty;
            Vector3 upperAxis = forearm.position - upperArm.position;
            // Omit the authored shoulder junction, including the broad-phase
            // cap. The elbow, forearm and hand remain fully checked.
            Vector3 upperStart = upperArm.position + upperAxis.normalized *
                Mathf.Min(upperAxis.magnitude, ShoulderJoinLength + upperArmRadius);
            float upper = CapsuleClearance(upperStart, forearm.position, upperArmRadius);
            float lower = CapsuleClearance(forearm.position, hand.position, forearmRadius);
            float palm = CapsuleClearance(hand.position + hand.rotation * handStartInHand,
                hand.position + hand.rotation * handEndInHand, handRadius);
            // A constant-radius capsule fills empty space around tapered
            // sleeves and fingers. It can reject a genuinely clear grip by
            // many centimetres, so only real mesh contact rejects that pose.
            if (upper < MinimumBodyClearance) upper = ConfirmMeshClearance(0);
            if (lower < MinimumBodyClearance) lower = ConfirmMeshClearance(1);
            if (palm < MinimumBodyClearance) palm = ConfirmMeshClearance(2);
            ArmClearances = new Vector3(upper, lower, palm);
            BodyClearance = Mathf.Min(upper, Mathf.Min(lower, palm));
            BodyIntersectionCount = (upper < 0f ? 1 : 0) + (lower < 0f ? 1 : 0) + (palm < 0f ? 1 : 0);
        }
        private float ConfirmMeshClearance(int kind)
        {
            const float nearDistance = 0.01f;
            float minimumSquared = nearDistance * nearDistance;
            foreach (ArmSurface arm in armSurfaces)
            {
                if (arm.Kind != kind) continue;
                Quaternion rotation = arm.Bone.rotation;
                Vector3 origin = arm.Bone.position;
                for (int index = 0; index < arm.LocalVertices.Length; index++)
                    arm.WorldVertices[index] = origin + rotation * arm.LocalVertices[index];
                foreach (BodySurface body in bodySurfaces)
                {
                    // This also catches a hand wholly inside the body, where
                    // no pair of surface triangles would cross.
                    foreach (Vector3 vertex in arm.WorldVertices)
                        if (body.Bounds.Contains(vertex) && IsInside(vertex, body))
                        {
                            RecordBodyIntersection(body, kind, "inside", vertex);
                            return -MinimumBodyClearance;
                        }
                    for (int index = 0; index < arm.WorldVertices.Length; index += 3)
                    {
                        Vector3 a = arm.WorldVertices[index], b = arm.WorldVertices[index + 1], c = arm.WorldVertices[index + 2];
                        Bounds bounds = new Bounds(a, Vector3.zero);
                        bounds.Encapsulate(b); bounds.Encapsulate(c); bounds.Expand(nearDistance * 2f);
                        if (!bounds.Intersects(body.Bounds)) continue;
                        for (int other = 0; other < body.Triangles.Length; other += 3)
                        {
                            if (!bounds.Intersects(body.TriangleBounds[other / 3])) continue;
                            Vector3 d = body.Vertices[body.Triangles[other]], e = body.Vertices[body.Triangles[other + 1]], f = body.Vertices[body.Triangles[other + 2]];
                            float squared = SegmentTriangleSquared(a, b, d, e, f);
                            squared = Mathf.Min(squared, SegmentTriangleSquared(b, c, d, e, f));
                            squared = Mathf.Min(squared, SegmentTriangleSquared(c, a, d, e, f));
                            squared = Mathf.Min(squared, SegmentTriangleSquared(d, e, a, b, c));
                            squared = Mathf.Min(squared, SegmentTriangleSquared(e, f, a, b, c));
                            squared = Mathf.Min(squared, SegmentTriangleSquared(f, d, a, b, c));
                            if (squared < 0.0000000001f)
                            {
                                RecordBodyIntersection(body, kind, "triangle", (a + b + c) / 3f);
                                return -0.00001f;
                            }
                            minimumSquared = Mathf.Min(minimumSquared, squared);
                        }
                    }
                }
            }
            return Mathf.Sqrt(minimumSquared);
        }
        private float CapsuleClearance(Vector3 start, Vector3 end, float radius)
        {
            float closest = float.PositiveInfinity;
            Bounds segmentBounds = new Bounds(start, Vector3.zero);
            segmentBounds.Encapsulate(end);
            foreach (BodySurface surface in bodySurfaces)
            {
                Vector3[] vertices = surface.Vertices;
                int[] triangles = surface.Triangles;
                if (vertices == null || triangles == null) continue;
                float possibleDistance = Mathf.Max(0f, closest + radius);
                if (!surface.Bounds.Contains(start) && !surface.Bounds.Contains((start + end) * .5f) &&
                    !surface.Bounds.Contains(end) &&
                    BoundsDistanceSquared(segmentBounds, surface.Bounds) > possibleDistance * possibleDistance + .000000001f)
                    continue;
                float distanceSquared = float.PositiveInfinity;
                for (int index = 0; index < triangles.Length; index += 3)
                {
                    // Both bounds contain the exact queried shapes. Their gap
                    // is a lower bound, so a farther pair cannot improve the
                    // current minimum and needs no triangle-distance evaluation.
                    if (BoundsDistanceSquared(segmentBounds, surface.TriangleBounds[index / 3]) > distanceSquared + .000000001f)
                        continue;
                    distanceSquared = Mathf.Min(distanceSquared, SegmentTriangleSquared(start, end,
                        vertices[triangles[index]], vertices[triangles[index + 1]], vertices[triangles[index + 2]]));
                }
                float distance = Mathf.Sqrt(distanceSquared);
                bool inside = IsInside(start, surface) || IsInside((start + end) * 0.5f, surface) || IsInside(end, surface);
                closest = Mathf.Min(closest, (inside ? -distance : distance) - radius);
            }
            return closest;
        }
        private void RecordBodyIntersection(BodySurface body, int kind, string intersection, Vector3 point)
        {
            if (BodyIntersectionDetail.Length != 0) return;
            string limb = kind == 0 ? "upper" : kind == 1 ? "forearm" : "hand";
            BodyIntersectionDetail = $"{body.Renderer.name} / {limb} / {intersection} at {point:F4}; closed={body.IsClosed}";
        }
        private static bool IsClosedSurface(BodySurface surface)
        {
            // FBX duplicates vertices at hard normals and UV seams. Weld only
            // coincident positions for this topology test; the visible mesh
            // and all triangle contact checks keep their original geometry.
            var welded = new Dictionary<Vector3Int, int>();
            int[] indices = new int[surface.Vertices.Length];
            for (int index = 0; index < indices.Length; index++)
            {
                Vector3 point = surface.Vertices[index] * 100000f;
                var key = new Vector3Int(Mathf.RoundToInt(point.x), Mathf.RoundToInt(point.y), Mathf.RoundToInt(point.z));
                if (!welded.TryGetValue(key, out int value))
                {
                    value = welded.Count;
                    welded.Add(key, value);
                }
                indices[index] = value;
            }
            var edges = new Dictionary<(int, int), int>();
            for (int index = 0; index < surface.Triangles.Length; index += 3)
            {
                int a = indices[surface.Triangles[index]], b = indices[surface.Triangles[index + 1]], c = indices[surface.Triangles[index + 2]];
                if (a == b || b == c || c == a) continue;
                CountEdge(a, b, edges); CountEdge(b, c, edges); CountEdge(c, a, edges);
            }
            if (edges.Count == 0) return false;
            foreach (int count in edges.Values) if (count != 2) return false;
            return true;
        }
        private static void CountEdge(int a, int b, Dictionary<(int, int), int> edges)
        {
            var edge = a < b ? (a, b) : (b, a);
            edges.TryGetValue(edge, out int count);
            edges[edge] = count + 1;
        }
        private static bool IsInside(Vector3 point, BodySurface surface)
        {
            // An open garment has a surface but does not enclose a volume.
            if (!surface.IsClosed || !surface.Bounds.Contains(point)) return false;
            Vector3 direction = InsideRayDirection;
            int crossings = 0;
            for (int index = 0; index < surface.Triangles.Length; index += 3)
                if (RayTriangle(point, direction, surface.Vertices[surface.Triangles[index]],
                    surface.Vertices[surface.Triangles[index + 1]], surface.Vertices[surface.Triangles[index + 2]], out float distance) && distance > 0.000001f)
                    crossings++;
            return (crossings & 1) != 0;
        }
        private static float BoundsDistanceSquared(Bounds a, Bounds b)
        {
            Vector3 gap = a.center - b.center;
            Vector3 extents = a.extents + b.extents;
            float x = Mathf.Max(0f, Mathf.Abs(gap.x) - extents.x);
            float y = Mathf.Max(0f, Mathf.Abs(gap.y) - extents.y);
            float z = Mathf.Max(0f, Mathf.Abs(gap.z) - extents.z);
            return x * x + y * y + z * z;
        }
        private static float SegmentTriangleSquared(Vector3 start, Vector3 end, Vector3 a, Vector3 b, Vector3 c)
        {
            Vector3 direction = end - start;
            if (RayTriangle(start, direction, a, b, c, out float fraction) && fraction >= 0f && fraction <= 1f) return 0f;
            float closest = Mathf.Min(PointTriangleSquared(start, a, b, c), PointTriangleSquared(end, a, b, c));
            closest = Mathf.Min(closest, SegmentSegmentSquared(start, end, a, b));
            closest = Mathf.Min(closest, SegmentSegmentSquared(start, end, b, c));
            return Mathf.Min(closest, SegmentSegmentSquared(start, end, c, a));
        }
        private static bool RayTriangle(Vector3 origin, Vector3 direction, Vector3 a, Vector3 b, Vector3 c, out float distance)
        {
            distance = 0f;
            Vector3 edge1 = b - a, edge2 = c - a, p = Vector3.Cross(direction, edge2);
            float determinant = Vector3.Dot(edge1, p);
            if (Mathf.Abs(determinant) < 0.00000001f) return false;
            float inverse = 1f / determinant;
            Vector3 relative = origin - a;
            float u = Vector3.Dot(relative, p) * inverse;
            if (u < 0f || u > 1f) return false;
            Vector3 q = Vector3.Cross(relative, edge1);
            float v = Vector3.Dot(direction, q) * inverse;
            if (v < 0f || u + v > 1f) return false;
            distance = Vector3.Dot(edge2, q) * inverse;
            return distance >= 0f;
        }
        private static float PointTriangleSquared(Vector3 point, Vector3 a, Vector3 b, Vector3 c)
        {
            Vector3 ab = b - a, ac = c - a, ap = point - a;
            float d1 = Vector3.Dot(ab, ap), d2 = Vector3.Dot(ac, ap);
            if (d1 <= 0f && d2 <= 0f) return ap.sqrMagnitude;
            Vector3 bp = point - b;
            float d3 = Vector3.Dot(ab, bp), d4 = Vector3.Dot(ac, bp);
            if (d3 >= 0f && d4 <= d3) return bp.sqrMagnitude;
            float vc = d1 * d4 - d3 * d2;
            if (vc <= 0f && d1 >= 0f && d3 <= 0f) return (point - (a + ab * (d1 / (d1 - d3)))).sqrMagnitude;
            Vector3 cp = point - c;
            float d5 = Vector3.Dot(ab, cp), d6 = Vector3.Dot(ac, cp);
            if (d6 >= 0f && d5 <= d6) return cp.sqrMagnitude;
            float vb = d5 * d2 - d1 * d6;
            if (vb <= 0f && d2 >= 0f && d6 <= 0f) return (point - (a + ac * (d2 / (d2 - d6)))).sqrMagnitude;
            float va = d3 * d6 - d5 * d4;
            if (va <= 0f && d4 - d3 >= 0f && d5 - d6 >= 0f)
                return (point - (b + (c - b) * ((d4 - d3) / (d4 - d3 + d5 - d6)))).sqrMagnitude;
            float denominator = va + vb + vc;
            if (Mathf.Abs(denominator) < 0.0000000001f)
                return Mathf.Min(PointSegmentSquared(point, a, b),
                    Mathf.Min(PointSegmentSquared(point, b, c), PointSegmentSquared(point, c, a)));
            return (point - (a + ab * (vb / denominator) + ac * (vc / denominator))).sqrMagnitude;
        }
        private static float PointSegmentSquared(Vector3 point, Vector3 start, Vector3 end)
        {
            Vector3 segment = end - start;
            float length = segment.sqrMagnitude;
            return (point - (start + segment * (length > 0f ? Mathf.Clamp01(Vector3.Dot(point - start, segment) / length) : 0f))).sqrMagnitude;
        }
        private static float SegmentSegmentSquared(Vector3 a, Vector3 b, Vector3 c, Vector3 d)
        {
            Vector3 u = b - a, v = d - c, w = a - c;
            float aa = Vector3.Dot(u, u), bb = Vector3.Dot(u, v), cc = Vector3.Dot(v, v);
            float dd = Vector3.Dot(u, w), ee = Vector3.Dot(v, w), denominator = aa * cc - bb * bb;
            float s = denominator > 0.000000001f ? Mathf.Clamp01((bb * ee - cc * dd) / denominator) : 0f;
            float t = cc > 0f ? (bb * s + ee) / cc : 0f;
            if (t < 0f) { t = 0f; s = aa > 0f ? Mathf.Clamp01(-dd / aa) : 0f; }
            else if (t > 1f) { t = 1f; s = aa > 0f ? Mathf.Clamp01((bb - dd) / aa) : 0f; }
            return (w + u * s - v * t).sqrMagnitude;
        }
        public void End()
        {
            if (captured)
            {
                RestoreBones();
                RefreshBody();
                MeasureBodyClearance();
            }
            Weight = Bend = ValveLean = ActualBrushTravel = 0f;
            captured = sampled = false;
        }
        private void RestoreBones()
        {
            for (int index = 0; index < bones.Length; index++)
                if (bones[index] != null) bones[index].localRotation = neutral[index];
        }
        private void Pitch(Transform bone, float degrees)
        {
            if (bone != null && degrees != 0f) bone.rotation = Quaternion.AngleAxis(degrees, Vector3.Cross(actor.up, Forward).normalized) * bone.rotation;
        }
        private Transform Bone(Player3DAnatomicalPart part) => registry.TryGetPart(part, out var binding) ? binding.Bone : null;
        private void OnDisable() => End();
        private void OnDestroy()
        {
            if (sample == null) return;
            if (Application.isPlaying) Destroy(sample); else DestroyImmediate(sample);
        }
    }
}

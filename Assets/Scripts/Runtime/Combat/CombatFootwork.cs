using System;
using UnityEngine;

namespace BarPromenade
{
    /// <summary>Authored shuffles, measured travel and world-space sole contacts share the duel clock.
    /// Sampling a weapon or presenting a frame never advances a step.</summary>
    internal sealed class CombatFootwork
    {
        private const int Samples = 80;
        private readonly Transform frame, pelvis;
        private readonly Transform[] bones = new Transform[7];
        private readonly Vector3[] basePositions = new Vector3[7];
        private readonly Quaternion[] baseRotations = new Quaternion[7];
        private readonly PoseFrame[][] curves = new PoseFrame[4][];
        private readonly Vector3[] restFeet = new Vector3[2], feet = new Vector3[2], correction = new Vector3[2];
        private readonly Quaternion[] restRotations = new Quaternion[2], rotations = new Quaternion[2], swingRotations = new Quaternion[2];
        private readonly float[] legLengths = new float[2];
        private readonly Vector3 readyPelvis;
        private Vector3 previousPosition, previousForward, gaitOffset, settleOffset, settleStart;
        private Quaternion settleRotation;
        private Vector3 travelDirection;
        private float cycle, settling, settleDuration, idleSeconds;
        private int direction, swing, attackSequence = -1;
        private bool initialized, moving, applied, yielded, settlingFoot, settlingAttack;
        private bool catching;
        private Vector3 catchTarget;
        private float impactPelvisFloor;
        public CombatImpactMotion ImpactMotion { get; set; }
        public bool TransferringFoot => moving || settlingFoot || catching;

        private struct PoseFrame
        {
            public Vector3 Pelvis, Left, Right;
            public Quaternion LeftRotation, RightRotation;
            public Vector3 Foot(int side) => side == 0 ? Left : Right;
            public Quaternion Rotation(int side) => side == 0 ? LeftRotation : RightRotation;
        }

        public CombatFootwork(Transform rig, GameObject animationRoot, Transform actorFrame, AnimationClip ready, bool npc)
        {
            frame = actorFrame;
            string[] names = { "pelvis", "thigh.L", "shin.L", "foot.L", "thigh.R", "shin.R", "foot.R" };
            Transform[] all = rig.GetComponentsInChildren<Transform>(true);
            foreach (Transform bone in all)
                for (int i = 0; i < names.Length; i++) if (bone.name == names[i]) bones[i] = bone;
            foreach (Transform bone in bones)
                if (bone == null) throw new InvalidOperationException("Combat footwork requires the original pelvis and both leg chains.");
            pelvis = bones[0];
            var positions = new Vector3[all.Length];
            var rotationsBefore = new Quaternion[all.Length];
            for (int i = 0; i < all.Length; i++) { positions[i] = all[i].localPosition; rotationsBefore[i] = all[i].localRotation; }
            try
            {
                ready.SampleAnimation(animationRoot, 0f);
                PoseFrame neutral = ReadPose();
                readyPelvis = neutral.Pelvis;
                for (int side = 0; side < 2; side++)
                {
                    restFeet[side] = neutral.Foot(side); restRotations[side] = neutral.Rotation(side);
                    int i = 1 + side * 3;
                    legLengths[side] = Vector3.Distance(bones[i].position, bones[i + 1].position) +
                        Vector3.Distance(bones[i + 1].position, bones[i + 2].position);
                }
                for (int d = 0; d < curves.Length; d++)
                {
                    AnimationClip clip = CombatAssetProvider.LoadClip(CombatAssetProvider.LocomotionClipNames[d], npc);
                    curves[d] = new PoseFrame[Samples + 1];
                    for (int sample = 0; sample <= Samples; sample++)
                    { clip.SampleAnimation(animationRoot, clip.length * sample / Samples); curves[d][sample] = ReadPose(); }
                }
            }
            finally
            {
                for (int i = 0; i < all.Length; i++) { all[i].localPosition = positions[i]; all[i].localRotation = rotationsBefore[i]; }
            }
            Reset();
        }

        private PoseFrame ReadPose() => new PoseFrame
        {
            Pelvis = frame.InverseTransformPoint(pelvis.position),
            Left = frame.InverseTransformPoint(bones[3].position), Right = frame.InverseTransformPoint(bones[6].position),
            LeftRotation = Quaternion.Inverse(frame.rotation) * bones[3].rotation,
            RightRotation = Quaternion.Inverse(frame.rotation) * bones[6].rotation
        };

        public void Reset()
        {
            Restore(); initialized = false; moving = settlingFoot = yielded = settlingAttack = catching = false;
            cycle = settling = 0f; gaitOffset = Vector3.zero; attackSequence = -1;
            idleSeconds = 0f;
            previousPosition = frame.position; previousForward = frame.forward;
            PlantReady();
        }

        private void PlantReady()
        {
            for (int side = 0; side < 2; side++)
            { feet[side] = frame.TransformPoint(restFeet[side]); rotations[side] = frame.rotation * restRotations[side]; }
            initialized = true;
        }

        public void Advance(float seconds, MeleeCombatant state)
        {
            if (seconds <= 0f) return;
            Vector3 displacement = frame.position - previousPosition;
            float turn = Vector3.Angle(previousForward, frame.forward) * Mathf.Deg2Rad;
            previousPosition = frame.position; previousForward = frame.forward;
            displacement.y = 0f;
            bool yield = state.Phase == MeleePhase.Step || state.IsDefeated || state.IsKnockedDown;
            if (yield)
            { yielded = true; initialized = false; moving = settlingFoot = catching = false; gaitOffset = Vector3.zero; return; }
            if (!initialized || displacement.sqrMagnitude > 1f)
            { PlantReady(); moving = settlingFoot = false; gaitOffset = Vector3.zero; cycle = 0f; }
            yielded = false;

            if (AdvanceCatchStep(seconds)) return;

            // Commit the current transfer inside the existing tell, including the short chain tell.
            // Its supporting foot stays at its actual contact; input never waits for a gait boundary.
            if (state.Phase == MeleePhase.Windup && attackSequence != state.AttackSequence)
            {
                attackSequence = state.AttackSequence;
                float remaining = Mathf.Max(.02f, state.AttackWindupSeconds - state.AttackElapsed);
                int side = moving || settlingFoot ? swing : FarthestFoot();
                if (moving || settlingFoot || Vector3.Distance(feet[side], frame.TransformPoint(restFeet[side])) > .025f)
                    BeginSettle(side, Mathf.Min(.13f, remaining * .45f), true);
                moving = false;
            }
            if (state.Phase == MeleePhase.Active)
            {
                if (settlingFoot) FinishSettle();
                gaitOffset = Vector3.zero; moving = false; return;
            }
            if (settlingFoot)
            {
                settling += seconds;
                float t = Mathf.Clamp01(settling / settleDuration);
                float blend = Smooth(t);
                Vector3 target = frame.TransformPoint(restFeet[swing]);
                float lift = Mathf.Sin(t * Mathf.PI);
                feet[swing] = Vector3.Lerp(settleStart, target, blend) + Vector3.up * (.025f * lift * lift);
                rotations[swing] = Quaternion.Slerp(settleRotation, frame.rotation * restRotations[swing], blend);
                gaitOffset = settleOffset * (1f - blend);
                if (t >= 1f)
                {
                    FinishSettle();
                    int other = 1 - swing;
                    float remaining = state.Phase == MeleePhase.Windup ? state.AttackWindupSeconds - state.AttackElapsed : .2f;
                    if (remaining > .025f && Vector3.Distance(feet[other], frame.TransformPoint(restFeet[other])) > .025f)
                        BeginSettle(other, Mathf.Min(.13f, remaining * .8f), settlingAttack);
                }
                return;
            }
            if (state.Phase == MeleePhase.Windup) return;
            bool allowed = state.Phase == MeleePhase.Ready || state.Phase == MeleePhase.Charging || state.Phase == MeleePhase.Recovery;
            float distance = displacement.magnitude;
            float travel = distance;
            // Turning on the spot lifts and replaces the more displaced boot;
            // it does not play a lateral metre-stride while the root stands still.
            if (allowed && distance < .00005f && turn > .00001f)
            {
                int side = moving ? swing : FarthestFoot();
                if (Vector3.Distance(feet[side], frame.TransformPoint(restFeet[side])) > .035f)
                { BeginSettle(side, .13f, false); moving = false; return; }
            }
            idleSeconds = travel < .00005f ? idleSeconds + seconds : 0f;
            if (!allowed || travel < .00005f)
            {
                // A rendered motor update can feed several duel substeps. An empty
                // substep is not a stop; consume each achieved displacement once.
                if (moving && (!allowed || idleSeconds >= .075f))
                { BeginSettle(swing, .13f, false); moving = false; }
                return;
            }
            Vector3 local = frame.InverseTransformDirection(displacement.normalized);
            local.y = 0f;
            if (!moving)
            {
                // On a diagonal the nearer side opens first. A forward-right
                // shuffle led by the left boot would cross the planted right leg.
                direction = Mathf.Abs(local.x) > Mathf.Abs(local.z) * .2f
                    ? (local.x < 0 ? 2 : 3) : (local.z >= 0 ? 0 : 1);
                travelDirection = local.normalized; cycle = 0f; moving = true;
                swing = direction == 0 || direction == 2 ? 0 : 1;
                BeginSwing();
            }
            // Direction changes are resolved through a short planted settle, not a new clip's first frame.
            else if (Vector3.Dot(local, travelDirection) < .9f)
            { BeginSettle(swing, .10f, false); moving = false; return; }

            float next = cycle + travel / CombatAssetProvider.LocomotionCycleDistance;
            if (next >= (cycle < .5f ? .5f : 1f))
            {
                float boundary = cycle < .5f ? .5f : 1f;
                EvaluateSwing(boundary);
                cycle = boundary == 1f ? 0f : .5f;
                swing = 1 - swing;
                BeginSwing();
                next = cycle + Mathf.Min(.45f, next - boundary);
            }
            cycle = next;
            EvaluateSwing(cycle);
        }

        private bool AdvanceCatchStep(float seconds)
        {
            if (ImpactMotion == null || (!ImpactMotion.IsActive && !catching)) return false;
            if (!catching)
            {
                int displaced = FarthestFoot();
                float error = Vector3.Distance(feet[displaced], frame.TransformPoint(restFeet[displaced]));
                if (ImpactMotion.BalanceLoad < .42f && error < .11f) return false;
                Vector3 capture = ImpactMotion.CaptureOffset;
                bool alreadyTransferring = moving || settlingFoot;
                if (!alreadyTransferring)
                {
                    Vector3 across = Vector3.ProjectOnPlane(feet[1] - feet[0], Vector3.up);
                    float lateral = Vector3.Dot(capture, across.normalized);
                    float rightLoad = across.sqrMagnitude > .001f ? Mathf.Clamp01(Vector3.Dot(
                        ImpactMotion.CentreOfMass - feet[0], across) / across.sqrMagnitude) : .5f;
                    // Keep the loaded boot down. A hit to that leg does not make
                    // it instantly airborne; support loss still governs the fall.
                    swing = Mathf.Abs(lateral) > .08f ? (lateral > 0f ? 1 : 0) :
                        Mathf.Abs(rightLoad - .5f) > .08f ? (rightLoad > .5f ? 0 : 1) : displaced;
                }
                if (!TryCatchTarget(swing, capture, out Vector3 target))
                {
                    if (alreadyTransferring || !TryCatchTarget(1 - swing, capture, out target)) return false;
                    swing = 1 - swing;
                }
                if (Vector3.ProjectOnPlane(target - feet[swing], Vector3.up).sqrMagnitude < .0025f) return false;
                // A reachable step has an actual landing; the other boot remains planted.
                settleStart = feet[swing]; settleRotation = rotations[swing]; catchTarget = target;
                settling = 0f; settleDuration = .26f; catching = true;
                moving = settlingFoot = false; gaitOffset = Vector3.zero;
            }
            settling += seconds;
            float t = Mathf.Clamp01(settling / settleDuration), blend = Smooth(t);
            float lift = Mathf.Sin(t * Mathf.PI);
            feet[swing] = Vector3.Lerp(settleStart, catchTarget, blend) + Vector3.up * (.07f * lift * lift);
            rotations[swing] = Quaternion.Slerp(settleRotation, frame.rotation * restRotations[swing], blend);
            // ImpactMotion owns the buckle. A second sinusoidal pelvis drop here
            // fought the planted-leg correction and produced a deep repeated bob.
            gaitOffset = Vector3.zero;
            if (t >= 1f)
            {
                catching = false; gaitOffset = Vector3.zero;
                RetroAudio.PlayAt(RetroSfxId.FootstepConcrete, feet[swing], .7f);
            }
            return true;
        }

        private bool TryCatchTarget(int side, Vector3 capture, out Vector3 target)
        {
            // Account for the root travel still to come during the short catch.
            // This landing stays fixed in world space once the boot leaves.
            target = frame.TransformPoint(restFeet[side]) + Vector3.ClampMagnitude(
                capture * .38f + ImpactMotion.Velocity * .04f, .32f);
            if (!TryCatchGround(target, side, out target)) return false;
            Vector3 outward = Vector3.ProjectOnPlane(frame.TransformVector(restFeet[side] - restFeet[1 - side]), Vector3.up).normalized;
            float separation = Vector3.Dot(target - feet[1 - side], outward);
            if (separation < .12f) target += outward * (.12f - separation);
            Vector3 hip = bones[1 + side * 3].position;
            Vector3 horizontal = Vector3.ProjectOnPlane(target - hip, Vector3.up);
            float length = legLengths[side] * .98f;
            float height = Mathf.Max(0f, hip.y - target.y - .10f);
            float reach = Mathf.Sqrt(Mathf.Max(0f, length * length - height * height));
            horizontal = Vector3.ClampMagnitude(horizontal, Mathf.Min(.44f, reach));
            target.x = hip.x + horizontal.x; target.z = hip.z + horizontal.z;
            if (Vector3.Dot(target - feet[1 - side], outward) < .115f ||
                !TryCatchGround(target, side, out target)) return false;
            // A floor beneath the destination is not evidence of an unobstructed
            // step: keep the swinging boot out of walls and low solid furniture.
            Vector3 start = feet[side] + Vector3.up * .045f;
            Vector3 travel = target - feet[side];
            float distance = travel.magnitude;
            if (distance < .001f) return true;
            foreach (RaycastHit hit in Physics.SphereCastAll(start, .045f, travel / distance,
                distance, ~0, QueryTriggerInteraction.Ignore))
                if (hit.collider.GetComponentInParent<CombatActor>() == null && hit.normal.y < .65f) return false;
            return true;
        }

        private bool TryCatchGround(Vector3 desired, int side, out Vector3 grounded)
        {
            grounded = desired;
            float nearest = float.PositiveInfinity;
            foreach (RaycastHit hit in Physics.RaycastAll(desired + Vector3.up * .55f, Vector3.down, 1.1f, ~0, QueryTriggerInteraction.Ignore))
            {
                if (hit.collider.GetComponentInParent<CombatActor>() != null || hit.normal.y < .65f || hit.distance >= nearest) continue;
                nearest = hit.distance;
                grounded = hit.point + Vector3.up * Mathf.Max(.025f, restFeet[side].y);
            }
            return nearest < float.PositiveInfinity;
        }

        private void BeginSwing()
        {
            PoseFrame pose = Sample(cycle);
            correction[swing] = feet[swing] - DesiredFoot(pose, swing);
            swingRotations[swing] = rotations[swing];
        }

        private void EvaluateSwing(float phase)
        {
            PoseFrame pose = Sample(phase);
            float half = phase <= .5f && cycle < .5f ? phase * 2f : (phase - .5f) * 2f;
            float blend = Smooth(Mathf.Clamp01(half));
            feet[swing] = DesiredFoot(pose, swing) + correction[swing] * (1f - blend);
            rotations[swing] = Quaternion.Slerp(swingRotations[swing], frame.rotation * pose.Rotation(swing), blend);
            gaitOffset = DirectionRotation() * (pose.Pelvis - readyPelvis);
        }

        private Quaternion DirectionRotation()
        {
            Vector3 axis = direction == 0 ? Vector3.forward : direction == 1 ? Vector3.back : direction == 2 ? Vector3.left : Vector3.right;
            return Quaternion.FromToRotation(axis, travelDirection);
        }

        private Vector3 DesiredFoot(PoseFrame pose, int side)
        {
            Vector3 excursion = pose.Foot(side) - restFeet[side];
            excursion = DirectionRotation() * excursion;
            return frame.TransformPoint(restFeet[side] + excursion);
        }

        private PoseFrame Sample(float phase)
        {
            float sample = Mathf.Clamp01(phase) * Samples;
            int index = Mathf.Min(Samples - 1, (int)sample);
            float t = sample - index;
            PoseFrame a = curves[direction][index], b = curves[direction][index + 1];
            return new PoseFrame { Pelvis = Vector3.Lerp(a.Pelvis, b.Pelvis, t),
                Left = Vector3.Lerp(a.Left, b.Left, t), Right = Vector3.Lerp(a.Right, b.Right, t),
                LeftRotation = Quaternion.Slerp(a.LeftRotation, b.LeftRotation, t), RightRotation = Quaternion.Slerp(a.RightRotation, b.RightRotation, t) };
        }

        private int FarthestFoot() => (feet[0] - frame.TransformPoint(restFeet[0])).sqrMagnitude >=
            (feet[1] - frame.TransformPoint(restFeet[1])).sqrMagnitude ? 0 : 1;

        private void BeginSettle(int side, float duration, bool attack)
        {
            swing = side; settling = 0f; settleDuration = Mathf.Max(.02f, duration);
            settleStart = feet[side]; settleRotation = rotations[side]; settleOffset = gaitOffset;
            settlingFoot = true; settlingAttack = attack;
        }

        private void FinishSettle()
        {
            feet[swing] = frame.TransformPoint(restFeet[swing]);
            rotations[swing] = frame.rotation * restRotations[swing];
            settlingFoot = false; gaitOffset = Vector3.zero;
        }

        public void Apply()
        {
            Restore();
            impactPelvisFloor = pelvis.position.y - .20f;
            if (!yielded) ImpactMotion?.Apply();
            if (!initialized || yielded) return;
            for (int i = 0; i < bones.Length; i++) { basePositions[i] = bones[i].localPosition; baseRotations[i] = bones[i].localRotation; }
            applied = true;
            pelvis.position += frame.TransformVector(gaitOffset);
            ConstrainContacts();
        }

        // Transition source/target poses already include the weight shift. Re-close
        // their contacts afterwards without applying that shift a second time.
        public void ConstrainContacts()
        {
            if (!initialized || yielded) return;
            // The animation owns the weight shift; only lower a hip if a planted leg would lock straight.
            float lower = 0f;
            for (int side = 0; side < 2; side++)
            {
                Vector3 delta = bones[1 + side * 3].position - feet[side];
                float horizontal = delta.x * delta.x + delta.z * delta.z;
                float length = legLengths[side] * .997f;
                lower = Mathf.Max(lower, delta.y - Mathf.Sqrt(Mathf.Max(.01f, length * length - horizontal)));
            }
            float correction = Mathf.Clamp(lower, 0f, .18f);
            if (ImpactMotion != null && ImpactMotion.IsActive)
                correction = Mathf.Min(correction, Mathf.Max(0f, pelvis.position.y - impactPelvisFloor));
            pelvis.position -= Vector3.up * correction;
            for (int side = 0; side < 2; side++)
            {
                int i = 1 + side * 3;
                LimbTwoBoneIk.Solve(bones[i], bones[i + 1], bones[i + 2], feet[side], rotations[side],
                    bones[i].position + frame.forward * .7f + (side == 0 ? -frame.right : frame.right) * .08f,
                    1f, .999f, true);
            }
        }

        public void Restore()
        {
            if (applied)
                for (int i = 0; i < bones.Length; i++)
                    if (bones[i] != null) { bones[i].localPosition = basePositions[i]; bones[i].localRotation = baseRotations[i]; }
            applied = false;
            ImpactMotion?.Restore();
        }
        public void Forget() { applied = false; ImpactMotion?.Forget(); }
        private static float Smooth(float value) => value * value * (3f - 2f * value);
    }
}

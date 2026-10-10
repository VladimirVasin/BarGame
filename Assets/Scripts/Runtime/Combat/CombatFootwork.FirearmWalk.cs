using UnityEngine;

namespace BarPromenade
{
    internal sealed partial class CombatFootwork
    {
        private PoseFrame[][] firearmWalkCurves;
        private readonly Vector3[] firearmWalkKnees = new Vector3[2];
        private readonly Vector3[] firearmKneeCorrection = new Vector3[2];
        private readonly Vector3[] firearmHipOffsets = new Vector3[2];
        private Vector3 firearmPelvisCorrection;
        private Vector3 firearmPassBow;
        private float firearmSwingStart;
        private float firearmLandingSeconds;
        private float firearmTickSeconds;
        private int firearmWalkDirection;
        internal bool UsesFirearmWalkClips { get; private set; }
        internal int FirearmWalkTransferSequence { get; private set; }
        internal float FirearmWalkLateralOffset => UsesFirearmWalkClips ? gaitOffset.x : 0f;

        private void LoadFirearmWalkCurves(GameObject animationRoot)
        {
            firearmWalkCurves = new PoseFrame[CombatAssetProvider.FirearmWalkClipNames.Length][];
            for (int directionIndex = 0; directionIndex < firearmWalkCurves.Length; directionIndex++)
            {
                AnimationClip clip = CombatAssetProvider.LoadFirearmWalkClip(CombatAssetProvider.FirearmWalkClipNames[directionIndex]);
                firearmWalkCurves[directionIndex] = new PoseFrame[Samples + 1];
                for (int sample = 0; sample <= Samples; sample++)
                {
                    clip.SampleAnimation(animationRoot, clip.length * sample / Samples);
                    firearmWalkCurves[directionIndex][sample] = ReadPose();
                    if (directionIndex == 0 && sample == 0)
                        for (int side = 0; side < 2; side++)
                            firearmHipOffsets[side] = frame.InverseTransformPoint(bones[1 + side * 3].position) -
                                firearmWalkCurves[0][0].Pelvis;
                }
            }
        }

        internal void SetFirearmWalkLocomotion(bool enabled)
        {
            enabled &= firearmWalkCurves != null;
            if (UsesFirearmWalkClips == enabled) return;
            // Keep the last real soles on entry and before a committed action
            // resumes. Replanting Ready here would invent a support contact.
            Restore();
            if (hasPresentedContacts)
                for (int side = 0; side < 2; side++)
                {
                    feet[side] = presentedFeet[side];
                    rotations[side] = bones[3 + side * 3].rotation;
                    supportConfirmed[side] = PresentedGrounded(side, out _);
                }
            UsesFirearmWalkClips = enabled;
            initialized = true; yielded = false; moving = settlingFoot = false;
            gaitOffset = Vector3.zero;
            previousPosition = frame.position; previousForward = frame.forward;
            // Enter at mid-transfer: a standing support foot only owes a short
            // first step, then both legs alternate the full authored stride.
            cycle = .25f;
            firearmLandingSeconds = 0f;
            firearmPelvisCorrection = Vector3.zero;
            ImpactMotion?.CancelMovementLanding();
        }

        private void AdvanceFirearmWalk(float seconds, Vector3 displacement)
        {
            firearmTickSeconds = seconds;
            float distance = displacement.magnitude;
            idleSeconds = distance < .00005f ? idleSeconds + seconds : 0f;
            if (distance < .00005f && firearmLandingSeconds <= 0f)
            {
                if (idleSeconds < .075f) return;
                if (!settlingFoot)
                {
                    int side = moving ? swing : FarthestFoot();
                    moving = false;
                    if (Vector3.Distance(feet[side], frame.TransformPoint(restFeet[side])) > .015f)
                        BeginSettle(side, .16f, false);
                    else { gaitOffset = Vector3.zero; return; }
                }
                settling += seconds;
                float t = Mathf.Clamp01(settling / settleDuration);
                float lift = Mathf.Sin(t * Mathf.PI);
                feet[swing] = Vector3.Lerp(settleStart, frame.TransformPoint(restFeet[swing]), Smooth(t)) +
                    frame.up * (.045f * lift * lift);
                rotations[swing] = Quaternion.Slerp(settleRotation, frame.rotation * restRotations[swing], Smooth(t));
                gaitOffset = settleOffset * (1f - Smooth(t));
                if (t >= 1f) FinishSettle();
                return;
            }

            if (distance >= .00005f)
            {
                Vector3 local = frame.InverseTransformDirection(displacement / distance);
                local.y = 0f;
                travelDirection = local.normalized;
            }
            if (!moving)
            {
                moving = true; settlingFoot = false;
                cycle = .25f;
                BeginFirearmSwing(Vector3.zero);
            }
            int selectedDirection = FirearmDirection();
            if (selectedDirection != firearmWalkDirection)
            {
                // Keep the live airborne sole and its phase, but stop carrying
                // an obsolete direction to touchdown. This is a correction of
                // one authored take, never a blend of crossing leg trajectories.
                firearmWalkDirection = selectedDirection;
                PoseFrame pose = SampleFirearmWalk(cycle);
                firearmSwingStart = Mathf.Repeat(cycle * 2f, 1f);
                correction[swing] = feet[swing] - frame.TransformPoint(pose.Foot(swing));
                swingRotations[swing] = rotations[swing];
                firearmKneeCorrection[0] = firearmWalkKnees[0] - pose.LeftKnee;
                firearmKneeCorrection[1] = firearmWalkKnees[1] - pose.RightKnee;
                firearmPelvisCorrection = gaitOffset - (pose.Pelvis - readyPelvis);
                PlanFirearmPass(Vector3.zero);
            }
            ProtectFirearmSupport(seconds, displacement);
            float increment = distance / CombatAssetProvider.FirearmWalkCycleDistance;
            if (firearmLandingSeconds > 0f)
            {
                float boundary = cycle < .5f ? .5f : 1f;
                increment = Mathf.Max(increment, (boundary - cycle) * Mathf.Min(1f, seconds / firearmLandingSeconds));
                firearmLandingSeconds = Mathf.Max(0f, firearmLandingSeconds - seconds);
            }
            float remaining = increment;
            while (remaining > .000001f)
            {
                float boundary = cycle < .5f ? .5f : 1f;
                float step = Mathf.Min(remaining, boundary - cycle);
                cycle += step;
                remaining -= step;
                Vector3 rootRemainder = displacement * (remaining / increment);
                EvaluateFirearmSwing(cycle, rootRemainder);
                if (cycle < boundary - .000001f) break;
                supportConfirmed[swing] = TryCatchGround(feet[swing], swing, out Vector3 ground) &&
                    Vector3.Distance(feet[swing], ground) <= .055f && LandingClear(ground);
                FirearmWalkTransferSequence++;
                if (cycle >= 1f) cycle = 0f;
                BeginFirearmSwing(rootRemainder);
            }
            achievedGaitVelocity = JournalActor != null ? JournalActor.AchievedPlanarVelocity : displacement / seconds;
        }

        private void BeginFirearmSwing(Vector3 rootRemainder)
        {
            swing = cycle < .5f ? 1 : 0;
            firearmSwingStart = Mathf.Repeat(cycle * 2f, 1f);
            firearmLandingSeconds = 0f;
            firearmWalkDirection = FirearmDirection();
            firearmKneeCorrection[0] = firearmKneeCorrection[1] = Vector3.zero;
            PoseFrame pose = SampleFirearmWalk(cycle);
            // Enter the lower walking stance from the last weight shift. At
            // subsequent transfers the authored seam already agrees, so this
            // carries only any unfinished live direction correction.
            firearmPelvisCorrection = gaitOffset - (pose.Pelvis - readyPelvis);
            correction[swing] = feet[swing] - (frame.TransformPoint(pose.Foot(swing)) - rootRemainder);
            swingRotations[swing] = rotations[swing];
            PlanFirearmPass(rootRemainder);
            movementLandingId++;
        }

        private void PlanFirearmPass(Vector3 rootRemainder)
        {
            firearmPassBow = Vector3.zero;
            if (Vector3.ProjectOnPlane(correction[swing], frame.up).sqrMagnitude < .0004f &&
                firearmPelvisCorrection.sqrMagnitude < .0004f) return;
            // A live correction can cut through the planted ankle even though
            // both isolated takes clear it. Preserve the authored front/back
            // passage, with a zero-endpoint bow only when that chord is unsafe.
            Vector3 pass = frame.forward * (swing == 0 ? 1f : -1f);
            Vector3 start = feet[swing] - feet[1 - swing];
            float along = Vector3.Dot(start, pass);
            if (start.magnitude < .3f && along < -.025f) pass = -pass;
            if (FirearmPassClear(Vector3.zero, rootRemainder)) return;
            firearmPassBow = pass * .25f;
            if (!FirearmPassClear(firearmPassBow, rootRemainder)) firearmPassBow = pass * .3f;
        }

        private bool FirearmPassClear(Vector3 bow, Vector3 rootRemainder)
        {
            float boundary = swing == 1 ? .5f : 1f;
            float startPhase = (swing == 1 ? 0f : .5f) + firearmSwingStart * .5f;
            Vector3 support = hasPresentedContacts ? presentedFeet[1 - swing] : feet[1 - swing];
            // Retargets may finish while braking. Probe both a still root and
            // ordinary distance-driven travel rather than promising old speed.
            for (int rootSample = 0; rootSample <= 2; rootSample++)
                for (int sample = 1; sample < 20; sample++)
                {
                    float t = sample / 20f, blend = Smooth(t);
                    float phase = Mathf.Lerp(startPhase, boundary, t);
                    PoseFrame pose = SampleFirearmWalk(phase);
                    Vector3 rootTravel = frame.TransformDirection(travelDirection) *
                        ((phase - startPhase) * CombatAssetProvider.FirearmWalkCycleDistance * rootSample * .5f);
                    Vector3 sole = frame.TransformPoint(pose.Foot(swing)) - rootRemainder + rootTravel +
                        correction[swing] * (1f - blend) + bow * FirearmPassLift(t);
                    Vector3 hip = frame.TransformPoint(pose.Pelvis + firearmPelvisCorrection * (1f - blend) +
                        firearmHipOffsets[swing]) - rootRemainder + rootTravel;
                    sole = hip + Vector3.ClampMagnitude(sole - hip, legLengths[swing] - .018f);
                    if (Vector3.Distance(sole, support) < .2f) return false;
                }
            return true;
        }

        private static float FirearmPassLift(float t)
        {
            float lift = Mathf.Sin(t * Mathf.PI);
            return lift * lift;
        }

        private int FirearmDirection()
        {
            float bearing = Mathf.Repeat(Mathf.Atan2(travelDirection.x, travelDirection.z) * Mathf.Rad2Deg, 360f);
            return Mathf.RoundToInt(bearing / 45f) % firearmWalkCurves.Length;
        }

        private void ShortenFirearmLanding(float seconds) => firearmLandingSeconds =
            firearmLandingSeconds > 0f ? Mathf.Min(firearmLandingSeconds, seconds) : seconds;

        private void ProtectFirearmSupport(float seconds, Vector3 displacement)
        {
            int support = 1 - swing;
            float boundary = cycle < .5f ? .5f : 1f;
            PoseFrame landing = SampleFirearmWalk(boundary);
            float remainingTravel = (boundary - cycle) * CombatAssetProvider.FirearmWalkCycleDistance;
            // The motor has already moved this render frame, while cycle still
            // describes the previous sole pose. Count that travel only once.
            Vector3 futureHip = frame.TransformPoint(landing.Pelvis + firearmHipOffsets[support]) - displacement +
                frame.TransformDirection(travelDirection) * remainingTravel;
            float reach = legLengths[support] - .018f; // Same bent-knee reserve as the authored bank.
            if ((futureHip - feet[support]).sqrMagnitude <= (reach + .002f) * (reach + .002f)) return;
            // Finish before the pinned support leaves its reachable sphere. A
            // short turn must not borrow the generic pelvis dip to hide a split.
            Vector3 hip = frame.TransformPoint(readyPelvis + gaitOffset + firearmHipOffsets[support]);
            Vector3 delta = hip - feet[support];
            // Hero movement arrives once per render frame; the duel may consume
            // several 120 Hz substeps. Dividing by one substep invents speed.
            Vector3 velocity = JournalActor != null ? JournalActor.AchievedPlanarVelocity : achievedGaitVelocity;
            float rate = velocity.sqrMagnitude;
            if (rate <= .0001f) return;
            float naturalSeconds = Mathf.Max(0f, remainingTravel - displacement.magnitude) / Mathf.Sqrt(rate);
            float along = Vector3.Dot(delta, velocity);
            float discriminant = along * along - rate * (delta.sqrMagnitude - reach * reach);
            float deadline = Mathf.Max(seconds,
                (-along + Mathf.Sqrt(Mathf.Max(0f, discriminant))) / rate * .85f);
            // An unsafe distant endpoint is not an imminent support loss. Keep
            // the ordinary stride when it can land before the current leg runs
            // out of reach; never turn every cautious prediction into a .12 s step.
            if (deadline >= naturalSeconds) return;
            ShortenFirearmLanding(deadline);
        }

        private void EvaluateFirearmSwing(float phase, Vector3 rootRemainder)
        {
            PoseFrame pose = SampleFirearmWalk(phase);
            float half = swing == 1 ? phase * 2f : (phase - .5f) * 2f;
            float blend = Smooth(Mathf.InverseLerp(firearmSwingStart, 1f, half));
            float pass = FirearmPassLift(Mathf.InverseLerp(firearmSwingStart, 1f, half));
            Vector3 sole = frame.TransformPoint(pose.Foot(swing)) - rootRemainder + correction[swing] * (1f - blend) + firearmPassBow * pass;
            feet[swing] = ClearFirearmPass(pose, sole, rootRemainder, blend);
            Vector3 reactivePass = feet[swing] - sole;
            rotations[swing] = Quaternion.Slerp(swingRotations[swing], frame.rotation * pose.Rotation(swing), blend);
            gaitOffset = pose.Pelvis - readyPelvis + firearmPelvisCorrection * (1f - blend);
            firearmWalkKnees[0] = pose.LeftKnee + firearmKneeCorrection[0] * (1f - blend);
            firearmWalkKnees[1] = pose.RightKnee + firearmKneeCorrection[1] * (1f - blend);
            firearmWalkKnees[swing] += frame.InverseTransformDirection(firearmPassBow * pass + reactivePass) * .5f;
            // The other foot stays at its accepted world contact throughout
            // support, including mouse turns and direction changes.
        }

        private Vector3 ClearFirearmPass(PoseFrame pose, Vector3 sole, Vector3 rootRemainder, float blend)
        {
            Vector3 pelvisPosition = pose.Pelvis + firearmPelvisCorrection * (1f - blend);
            Vector3 hip = frame.TransformPoint(pelvisPosition + firearmHipOffsets[swing]) - rootRemainder;
            sole = hip + Vector3.ClampMagnitude(sole - hip, legLengths[swing] - .018f);
            int supportSide = 1 - swing;
            Vector3 supportHip = frame.TransformPoint(pelvisPosition + firearmHipOffsets[supportSide]) - rootRemainder;
            Vector3 support = supportHip + Vector3.ClampMagnitude(feet[supportSide] - supportHip, legLengths[supportSide] - .018f);
            Vector3 gap = sole - support;
            Vector3 previousGap = feet[swing] - support, travel = sole - feet[swing];
            float closest = travel.sqrMagnitude > .000001f ? Mathf.Clamp01(-Vector3.Dot(previousGap, travel) / travel.sqrMagnitude) : 1f;
            if ((previousGap + travel * closest).sqrMagnitude >= .04f) return sole;
            // A braking root can depart from the planned arc. Walk around the
            // real reachable support instead of crossing its ankle. Limit the
            // angular carry from the previous sole so the avoidance cannot flip
            // sides abruptly when the requested chord passes through its centre.
            Vector3 previous = Vector3.ProjectOnPlane(feet[swing] - support, frame.up);
            Vector3 wanted = Vector3.ProjectOnPlane(gap, frame.up);
            if (previous.sqrMagnitude < .0001f) previous = frame.forward * (swing == 0 ? 1f : -1f);
            if (wanted.sqrMagnitude < .0001f) wanted = frame.forward * (swing == 0 ? 1f : -1f);
            float turn = Vector3.SignedAngle(previous, wanted, frame.up);
            float limit = firearmTickSeconds * 12f * Mathf.Rad2Deg;
            Vector3 outward = Quaternion.AngleAxis(Mathf.Clamp(turn, -limit, limit), frame.up) * previous.normalized;
            sole = support + outward * .2f + frame.up * Vector3.Dot(gap, frame.up);
            float reach = legLengths[swing] - .018f;
            if ((sole - hip).sqrMagnitude <= reach * reach) return sole;
            // Intersect the orbit with the reachable horizontal disc. A final
            // radial clamp alone could pull the ankle into the other boot again.
            Vector3 centre = support + frame.up * Vector3.Dot(gap, frame.up);
            Vector3 towardHip = Vector3.ProjectOnPlane(hip - centre, frame.up);
            float vertical = Vector3.Dot(hip - centre, frame.up);
            float radiusSquared = Mathf.Max(0f, reach * reach - vertical * vertical);
            float distance = towardHip.magnitude;
            if (distance > .0001f)
            {
                float cosine = (.04f + distance * distance - radiusSquared) / (.4f * distance);
                float allowed = Mathf.Acos(Mathf.Clamp(cosine, -1f, 1f)) * Mathf.Rad2Deg;
                float angle = Vector3.SignedAngle(towardHip, outward, frame.up);
                outward = Quaternion.AngleAxis(Mathf.Clamp(angle, -allowed, allowed), frame.up) * towardHip.normalized;
            }
            return centre + outward * .2f;
        }

        private void ConstrainFirearmKnees()
        {
            for (int pass = 0; pass < 2; pass++)
            {
                int side = pass == 0 ? swing : 1 - swing, i = 1 + side * 3;
                Vector3 hip = bones[i].position, knee = bones[i + 1].position, sole = bones[i + 2].position;
                Vector3 otherKnee = bones[2 + (1 - side) * 3].position;
                Vector3 thigh = (knee - hip).normalized;
                bool clearance = side == swing && Vector3.Distance(knee, otherKnee) < .08f;
                if (!clearance && Mathf.Abs(Vector3.Dot(thigh, frame.right)) <= .7071f &&
                    Vector3.Angle(-frame.up, thigh) <= 65f) continue;
                // Fixed hip/ankle and original segment lengths leave a circle
                // of possible knees. Choose the nearest forward bend which
                // clears the other knee and respects the authored hip limits.
                Vector3 axis = sole - hip;
                float distance = axis.magnitude;
                if (distance < .001f) continue;
                axis /= distance;
                float upper = Vector3.Distance(hip, knee), lower = Vector3.Distance(knee, sole);
                float along = (upper * upper - lower * lower + distance * distance) / (2f * distance);
                Vector3 centre = hip + axis * along;
                float radius = Mathf.Sqrt(Mathf.Max(0f, upper * upper - along * along));
                Vector3 forward = Vector3.ProjectOnPlane(frame.forward, axis).normalized;
                Vector3 lateral = Vector3.Cross(axis, forward).normalized;
                Vector3 hint = knee;
                float best = float.PositiveInfinity;
                for (int sample = 0; sample < 64; sample++)
                {
                    float angle = sample * Mathf.PI * 2f / 64f;
                    Vector3 bend = forward * Mathf.Cos(angle) + lateral * Mathf.Sin(angle);
                    if (Vector3.Dot(bend, forward) < 0f) continue;
                    Vector3 candidate = centre + bend * radius;
                    Vector3 candidateThigh = (candidate - hip).normalized;
                    if (Mathf.Abs(Vector3.Dot(candidateThigh, frame.right)) > .7071f ||
                        Vector3.Angle(-frame.up, candidateThigh) > 65f ||
                        Vector3.Distance(candidate, otherKnee) < .08f) continue;
                    float error = (candidate - knee).sqrMagnitude;
                    if (error >= best) continue;
                    best = error; hint = candidate;
                }
                if (float.IsPositiveInfinity(best)) continue;
                LimbTwoBoneIk.Solve(bones[i], bones[i + 1], bones[i + 2], sole, rotations[side],
                    hint, 1f, 1f - .018f / legLengths[side], true);
            }
        }

        private PoseFrame SampleFirearmWalk(float phase)
        {
            return SampleFirearmCurve(firearmWalkDirection, phase);
        }

        private PoseFrame SampleFirearmCurve(int directionIndex, float phase)
        {
            float sample = Mathf.Clamp01(phase) * Samples;
            int index = Mathf.Min(Samples - 1, (int)sample);
            PoseFrame[] curve = firearmWalkCurves[directionIndex];
            PoseFrame a = curve[index], b = curve[index + 1];
            PoseFrame previous = curve[(index + Samples - 1) % Samples];
            PoseFrame next = curve[(index + 2) % Samples];
            float t = sample - index;
            PoseFrame pose = BlendFirearmPose(a, b, t);
            // Imported poses are sampled once. Carry their neighbouring slopes
            // through playback instead of changing knee/pelvis velocity at
            // every linear sample boundary. The loop has a duplicated endpoint.
            pose.Pelvis = InterpolateFirearmPosition(previous.Pelvis, a.Pelvis, b.Pelvis, next.Pelvis, t);
            pose.Left = InterpolateFirearmPosition(previous.Left, a.Left, b.Left, next.Left, t);
            pose.Right = InterpolateFirearmPosition(previous.Right, a.Right, b.Right, next.Right, t);
            pose.LeftKnee = InterpolateFirearmPosition(previous.LeftKnee, a.LeftKnee, b.LeftKnee, next.LeftKnee, t);
            pose.RightKnee = InterpolateFirearmPosition(previous.RightKnee, a.RightKnee, b.RightKnee, next.RightKnee, t);
            return pose;
        }

        private static Vector3 InterpolateFirearmPosition(Vector3 previous, Vector3 a, Vector3 b, Vector3 next, float t)
        {
            float square = t * t, cube = square * t;
            return a * (2f * cube - 3f * square + 1f) + b * (-2f * cube + 3f * square) +
                (b - previous) * (.5f * (cube - 2f * square + t)) +
                (next - a) * (.5f * (cube - square));
        }

        private static PoseFrame BlendFirearmPose(PoseFrame a, PoseFrame b, float t) => new PoseFrame
        {
            Pelvis = Vector3.Lerp(a.Pelvis, b.Pelvis, t),
            Left = Vector3.Lerp(a.Left, b.Left, t), Right = Vector3.Lerp(a.Right, b.Right, t),
            LeftKnee = Vector3.Lerp(a.LeftKnee, b.LeftKnee, t), RightKnee = Vector3.Lerp(a.RightKnee, b.RightKnee, t),
            LeftRotation = Quaternion.Slerp(a.LeftRotation, b.LeftRotation, t),
            RightRotation = Quaternion.Slerp(a.RightRotation, b.RightRotation, t)
        };
    }
}

using UnityEngine;

namespace BarPromenade
{
    public sealed partial class CombatImpactMotion
    {
        private readonly Transform frame;
        private Vector3 leftSupport, rightSupport, recoveryTarget, handPoint, handNormal;
        private bool hasFootSupport, leftPlanted = true, rightPlanted = true, recoveryStepActive, handSupported;
        private float stamina = 1f, intoxication, baseSkill = .9f, recoveryElapsed, recoveryStepRemaining;
        private float handStrength, crouch;
        private Vector2 flywheelAngle, flywheelVelocity;
        private bool flywheelSpent;
        private int profileSeed, recoveryStepSide;
        public float RecoverySkill => Mathf.Clamp(baseSkill * Mathf.Lerp(.65f, 1f, stamina) -
            intoxication * .22f - legWeakness * .18f, .3f, .95f);
        public float RecoveryUrgency => IsActive ? Mathf.Clamp(BalanceLoad + velocity.magnitude * .08f, 0f, 2f) : 0f;
        public int RecoverySequence { get; private set; }
        public int LandedRecoverySteps { get; private set; }
        public int BlockedRecoverySteps { get; private set; }
        public float LastLandingSpeedBefore { get; private set; }
        public float LastLandingSpeedAfter { get; private set; }
        public bool RecoveryStepActive => recoveryStepActive;
        public bool HasHandSupport => handSupported;
        public float HandSupportSeconds { get; private set; }
        public Vector3 SupportCentre { get; private set; }
        public bool RecoveryInProgress => IsActive && (recoveryStepActive || handSupported || BalanceLoad > .65f);
        public float RecoveryVariation
        {
            get
            {
                uint value = unchecked((uint)(profileSeed ^ RecoverySequence * 374761393 ^ (int)StruckRegion * 668265263));
                value = unchecked((value ^ (value >> 13)) * 1274126177u);
                return (value & 1023u) / 511.5f - 1f;
            }
        }

        public void ConfigureRecovery(bool frightenedHero, int seed)
        { baseSkill = frightenedHero ? .73f : .91f; profileSeed = seed; }

        public void UpdateRecoveryCondition(float stamina01, float intoxication01)
        { stamina = Mathf.Clamp01(stamina01); intoxication = Mathf.Clamp01(intoxication01); }

        private void BeginBalanceResponse(bool fresh, float stamina01, float intoxication01)
        {
            UpdateRecoveryCondition(stamina01, intoxication01);
            RecoverySequence++;
            if (fresh) { recoveryElapsed = overload = 0f; WantsKnockdown = false; }
            // A second hit changes the next solution, but never resets the load,
            // steals a planted foot or grants another invulnerable grace period.
        }

        public void SetFootSupport(Vector3 left, Vector3 right, bool leftGrounded, bool rightGrounded)
        {
            if (!Finite(left) || !Finite(right)) return;
            leftSupport = left; rightSupport = right;
            leftPlanted = leftGrounded; rightPlanted = rightGrounded;
            hasFootSupport = true;
            MeasureBalance();
        }

        public void BeginRecoveryStep(int side, Vector3 target, float duration)
        {
            if (!Finite(target) || !float.IsFinite(duration) || duration <= 0f) return;
            recoveryStepActive = true; recoveryTarget = target; recoveryStepSide = side;
            recoveryStepRemaining = duration + .075f;
            if (side == 0) leftPlanted = false; else rightPlanted = false;
        }

        public void LandRecoveryStep(int side, Vector3 point)
        {
            if (!recoveryStepActive || side != recoveryStepSide || !Finite(point)) return;
            if (side == 0) { leftSupport = point; leftPlanted = true; }
            else { rightSupport = point; rightPlanted = true; }
            recoveryStepActive = false; recoveryStepRemaining = 0f;
            LastLandingSpeedBefore = velocity.magnitude;
            // The shared walking balance model's landing rule: a real contact
            // transfers support and absorbs momentum. An injured/tired leg yields.
            float retention = Mathf.Lerp(.72f, PlayerBalanceModel.LandingVelocityRetention, RecoverySkill);
            velocity *= retention;
            angularVelocity *= Mathf.Lerp(.78f, .52f, RecoverySkill);
            LastLandingSpeedAfter = velocity.magnitude;
            LandedRecoverySteps++;
            MeasureBalance();
            if (BalanceLoad < 1f) overload = Mathf.Max(0f, overload - .10f);
        }

        public void CancelRecoveryStep()
        { recoveryStepActive = false; recoveryStepRemaining = 0f; }

        public void StepBlocked()
        { BlockedRecoverySteps++; }

        public void ClearHandSupport()
        { handSupported = false; handStrength = 0f; }

        public void ApplyHandSupport(Vector3 point, Vector3 normal, float strength, float seconds)
        {
            if (!Finite(point) || !Finite(normal) || normal.sqrMagnitude < .5f ||
                !float.IsFinite(seconds) || seconds <= 0f || !float.IsFinite(strength) || strength <= 0f) return;
            handSupported = true; handPoint = point; handNormal = normal.normalized;
            handStrength = Mathf.Clamp01(strength);
            HandSupportSeconds += seconds;
            float capacity = (4f + RecoverySkill * 4f) * handStrength * seconds;
            if (handNormal.y > .6f)
            {
                // A grounded palm carries weight and supplies bounded friction.
                velocity = Vector3.MoveTowards(velocity, Vector3.zero, capacity * .65f);
                angularVelocity = Vector3.MoveTowards(angularVelocity, Vector3.zero, capacity * 1.8f);
            }
            else
            {
                Vector3 planarNormal = Vector3.ProjectOnPlane(handNormal, Vector3.up).normalized;
                float incoming = Vector3.Dot(velocity, planarNormal);
                if (incoming < 0f) velocity += planarNormal * Mathf.Min(-incoming, capacity);
                Vector3 axis = Vector3.Cross(Vector3.up, planarNormal);
                float incomingRotation = Vector3.Dot(angularVelocity, axis);
                if (incomingRotation < 0f)
                    angularVelocity += axis * Mathf.Min(-incomingRotation, capacity * 1.8f);
            }
            MeasureBalance();
        }

        private Vector2 LocalPlanar(Vector3 world) => new Vector2(Vector3.Dot(world, frame.right), Vector3.Dot(world, frame.forward));
        private Vector3 WorldPlanar(Vector2 local) => frame.right * local.x + frame.forward * local.y;

        public float RecoveryFootError(int side, Vector3 target)
        {
            Vector3 centre = CentreOfMass;
            Vector2 left = LocalPlanar((side == 0 ? target : leftSupport) - centre);
            Vector2 right = LocalPlanar((side == 1 ? target : rightSupport) - centre);
            return FootPolygon(left, right).Excursion(LocalPlanar(CaptureOffset));
        }

        private static BalanceSupportPolygon FootPolygon(Vector2 left, Vector2 right) =>
            new BalanceSupportPolygon(Mathf.Min(left.x, right.x) - .075f,
                Mathf.Max(left.x, right.x) + .075f, Mathf.Min(left.y, right.y) - .13f, Mathf.Max(left.y, right.y) + .13f);

        private BalanceSupportPolygon CurrentSupport()
        {
            Vector3 centre = CentreOfMass;
            Vector2 left = LocalPlanar((hasFootSupport ? leftSupport : bones[13].position) - centre);
            Vector2 right = LocalPlanar((hasFootSupport ? rightSupport : bones[16].position) - centre);
            if (!leftPlanted) left = right;
            if (!rightPlanted) right = left;
            SupportCentre = centre + WorldPlanar((left + right) * .5f);
            var support = FootPolygon(left, right);
            if (handSupported)
            {
                Vector2 toward = LocalPlanar(handPoint - centre);
                if (handNormal.y > .6f) support = support.ExtendedToward(toward, Mathf.Min(.3f, toward.magnitude) * handStrength);
                else if (Vector3.Dot(CaptureOffset, handNormal) < 0f)
                    support = support.ExtendedToward(-LocalPlanar(handNormal), .22f * handStrength);
            }
            return support;
        }

        private void MeasureBalance()
        {
            float omega = Mathf.Sqrt(9.81f / Mathf.Max(.65f, 1f - crouch));
            Vector2 lean = LocalPlanar(Vector3.Cross(rotation, Vector3.up) * .65f);
            Vector2 capture = PlayerBalanceRules.CapturePoint(lean, LocalPlanar(velocity), omega);
            CaptureOffset = WorldPlanar(capture);
            float excursion = CurrentSupport().Excursion(capture);
            float reserve = Mathf.Lerp(.14f, .29f, RecoverySkill);
            BalanceLoad = !leftPlanted && !rightPlanted ? 3f + excursion : excursion / reserve;
        }

        private void AdvanceCounterBalance(float seconds)
        {
            if (!IsActive) return;
            MeasureBalance();
            if (flywheelSpent && flywheelAngle.magnitude < .06f) flywheelSpent = false;
            Vector2 command = age > Mathf.Lerp(.09f, .035f, RecoverySkill) && !flywheelSpent
                ? PlayerBalanceRules.FlywheelCommand(LocalPlanar(CaptureOffset), CurrentSupport(), .035f, 7f + RecoverySkill * 7f)
                : Vector2.zero;
            Vector2 acceleration = command.sqrMagnitude > 0f ? command : PlayerBalanceRules.FlywheelReturn(flywheelAngle, flywheelVelocity);
            flywheelVelocity += acceleration * seconds;
            flywheelAngle += flywheelVelocity * seconds;
            // The combat torso shares space with a held bar. Its finite angular
            // budget is smaller than the empty-handed walking flywheel's stop.
            if (flywheelAngle.magnitude > .24f)
            {
                Vector2 axis = flywheelAngle.normalized;
                flywheelAngle = axis * .24f;
                flywheelVelocity -= axis * Mathf.Max(0f, Vector2.Dot(flywheelVelocity, axis));
                acceleration -= axis * Mathf.Max(0f, Vector2.Dot(acceleration, axis));
                flywheelSpent = true;
            }
            if (leftPlanted || rightPlanted)
                velocity -= WorldPlanar(acceleration) * (PlayerBalanceRules.FlywheelCopGain * 9.81f * seconds);
            crouch = Mathf.MoveTowards(crouch, Mathf.Clamp01(BalanceLoad) * .09f, seconds * .45f);
        }

        private void ApplyCounterBalancePose()
        {
            Vector3 rotationAxis = Vector3.Cross(Vector3.up, WorldPlanar(flywheelAngle));
            // Spin shoulders into the fall while the reaction brings the hips
            // underneath; feet remain owned by their world-space constraints.
            Rotate(1, rotationAxis * .55f);
            Rotate(2, rotationAxis * .45f);
            Rotate(3, rotationAxis * -.35f);
            bones[0].position -= Vector3.up * crouch;
        }

        public void EvaluateSupport(float seconds)
        {
            if (!float.IsFinite(seconds) || seconds <= 0f) return;
            if (!IsActive) { overload = BalanceLoad = 0f; WantsKnockdown = false; return; }
            MeasureBalance();
            bool reachableLanding = recoveryStepActive && recoveryStepRemaining > 0f && (leftPlanted || rightPlanted) &&
                Vector3.ProjectOnPlane(CentreOfMass + CaptureOffset - recoveryTarget, Vector3.up).magnitude < .62f;
            // A side step deliberately leaves the far boot carrying the body.
            // Judge its finite chance against the planned support, otherwise
            // lifting the needed foot itself looks like an immediate catastrophe.
            float prospectiveLoad = reachableLanding
                ? RecoveryFootError(recoveryStepSide, recoveryTarget) / Mathf.Lerp(.14f, .29f, RecoverySkill)
                : BalanceLoad;
            bool catastrophic = prospectiveLoad > 3.2f || rotation.magnitude > .68f;
            bool lost = BalanceLoad > 1f && (!reachableLanding || catastrophic);
            overload = lost ? overload + seconds : Mathf.Max(0f, overload - seconds * 2.5f);
            // Judge AFTER this step's actual landings/hand pressures, with no
            // per-hit reset. A reachable moving foot gets its bounded chance.
            WantsKnockdown = overload >= (catastrophic ? .10f : .18f) ||
                (recoveryElapsed > 1.4f && BalanceLoad > 1.3f && !reachableLanding);
        }

        private void ResetBalance()
        {
            hasFootSupport = recoveryStepActive = handSupported = flywheelSpent = false;
            leftPlanted = rightPlanted = true;
            recoveryElapsed = recoveryStepRemaining = handStrength = crouch = 0f;
            flywheelAngle = flywheelVelocity = Vector2.zero;
            RecoverySequence = LandedRecoverySteps = BlockedRecoverySteps = 0;
            LastLandingSpeedBefore = LastLandingSpeedAfter = HandSupportSeconds = 0f;
            SupportCentre = Vector3.zero; WantsKnockdown = false;
        }
    }
}

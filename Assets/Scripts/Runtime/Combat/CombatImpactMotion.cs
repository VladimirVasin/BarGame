using System;
using System.Collections.Generic;
using UnityEngine;

namespace BarPromenade
{
    /// <summary>Finite standing melee responses; heavy impacts retain momentum and support recovery. Applying a pose is read-only.
    /// Momentum is in N s, velocities in metres/radians per second; HP never scales this response.</summary>
    public sealed partial class CombatImpactMotion
    {
        public const float BodyMass = 70f;
        // Retain the rejected prototype for a controlled visual comparison.
        // Configure only on an idle/reset actor; gameplay uses the minimal response.
        internal bool ExperimentalRecovery { get; set; }
        internal int MaximumRecoverySteps => UsesStandingResponse ? 0 : ExperimentalRecovery ? 3 : 2;
        private readonly Transform[] bones;
        private readonly Vector3[] basePositions, localRotation, localVelocity;
        private readonly Quaternion[] baseRotations;
        private bool applied;
        private Vector3 velocity, rotation, angularVelocity;
        private float age = 10f, overload, drop, dropVelocity, legWeakness;
        private int struckBone = 2;
        private bool standingResponse, standingCrowbarContact;
        private float standingDuration, standingDistance, standingSnap;
        private Vector3 standingDirection, standingFromRotation, standingPeakRotation;
        internal bool UsesStandingResponse => standingResponse;
        internal bool UsesCrowbarContactReaction => standingResponse && standingCrowbarContact;
        public Vector3 Velocity => velocity;
        public Vector3 AngularVelocity => angularVelocity;
        public Vector3 Rotation => rotation;
        public Vector3 CaptureOffset { get; private set; }
        public float BalanceLoad { get; private set; }
        public float Age => age;
        public bool WantsKnockdown { get; private set; }
        public bool IsActive => standingResponse ? age < standingDuration :
            ProjectileReactionActive || age < .9f || velocity.sqrMagnitude > .0025f || rotation.sqrMagnitude > .0001f ||
            recoveryStepActive || (ExperimentalRecovery && flywheelAngle.sqrMagnitude > .0004f);
        public float ReactionAmount => Mathf.Clamp01(Mathf.Max(rotation.magnitude * 2f, velocity.magnitude / 2f));
        public MeleeBodyRegion StruckRegion { get; private set; }
        public CombatImpact LastImpact { get; private set; }
        public Vector3 CentreOfMass => bones[0].position + Vector3.up * .20f;

        public CombatImpactMotion(Transform rig, Transform actor)
        {
            frame = actor;
            string[] names = { "pelvis", "spine", "chest", "neck", "head", "upper_arm.L", "forearm.L",
                "hand.L", "upper_arm.R", "forearm.R", "hand.R", "thigh.L", "shin.L", "foot.L", "thigh.R", "shin.R", "foot.R" };
            var found = new Dictionary<string, Transform>(StringComparer.Ordinal);
            foreach (Transform bone in rig.GetComponentsInChildren<Transform>(true)) found[bone.name] = bone;
            bones = new Transform[names.Length];
            for (int i = 0; i < names.Length; i++)
                if (!found.TryGetValue(names[i], out bones[i])) throw new InvalidOperationException("Impact rig lacks " + names[i]);
            basePositions = new Vector3[bones.Length]; baseRotations = new Quaternion[bones.Length];
            localRotation = new Vector3[bones.Length]; localVelocity = new Vector3[bones.Length];
        }

        public static Vector3 ResolveImpulse(Vector3 direction, float power, float weaponSpeed, MeleeHitResult result)
        {
            if (!Finite(direction) || direction.sqrMagnitude < .000001f || result == MeleeHitResult.Ignored || result == MeleeHitResult.Parried)
                return Vector3.zero;
            // Sampling density or a first overlapping sample cannot amplify the weapon.
            float speed = weaponSpeed > .01f && float.IsFinite(weaponSpeed) ? Mathf.Clamp(weaponSpeed / 5f, .8f, 1.15f) : 1f;
            float momentum = Mathf.Lerp(100f, 205f, Mathf.Clamp01(power)) * speed;
            if (result == MeleeHitResult.Blocked) momentum *= .23f;
            else if (result == MeleeHitResult.GuardBroken) momentum *= .85f;
            return direction.normalized * momentum;
        }

        public void Hit(CombatImpact impact, Vector3 carryVelocity, float stamina01, float intoxication01, bool transferringFoot)
        {
            if (!Finite(impact.Impulse) || impact.Impulse.sqrMagnitude < .0001f) return;
            bool fresh = !IsActive;
            standingResponse = standingCrowbarContact = false;
            BeginBalanceResponse(fresh, stamina01, intoxication01);
            LastImpact = impact; age = 0f;
            StruckRegion = impact.Location.Region;
            if (impact.Kind == CombatImpactKind.Projectile) BeginProjectileReaction(impact);
            struckBone = ResponseBone(BoneIndex(impact.Part));
            Vector3 planar = Vector3.ProjectOnPlane(impact.Impulse, Vector3.up);
            if (fresh && Finite(carryVelocity)) velocity = Vector3.ProjectOnPlane(carryVelocity, Vector3.up) * .35f;
            velocity = Vector3.ClampMagnitude(velocity + planar / BodyMass, 4.2f);
            Vector3 arm = Vector3.ClampMagnitude(impact.Point - CentreOfMass, 1.1f);
            Vector3 torque = Vector3.Cross(arm, impact.Impulse) / 32f;
            // A centred strike also rocks the body about its loaded boot.
            torque += Vector3.Cross(Vector3.up, planar) / 115f;
            angularVelocity = Vector3.ClampMagnitude(angularVelocity + torque, 5.5f);
            // The shared spring moves a contacted hand/forearm at its shoulder;
            // projectile accents below own the exact joint. Legs belong to IK.
            // Exact contact still supplies the whole-body torque and support loss.
            if (struckBone >= 0)
            {
                Vector3 localAxis = Vector3.Cross(Vector3.up, impact.Impulse.normalized);
                Vector3 kick = localAxis * Mathf.Clamp(impact.Impulse.magnitude / 38f, .3f, 4.5f) + torque * .20f;
                // Projectiles have their own exact-joint envelope; the shared
                // spring still carries their small physical momentum.
                localVelocity[struckBone] = Vector3.ClampMagnitude(localVelocity[struckBone] + AnatomicalRotation(struckBone, kick), 6f);
            }
            bool leg = StruckRegion == MeleeBodyRegion.LeftLeg || StruckRegion == MeleeBodyRegion.RightLeg;
            Vector3 across = Vector3.ProjectOnPlane(bones[16].position - bones[13].position, Vector3.up);
            Vector3 massOffset = Vector3.ProjectOnPlane(CentreOfMass - bones[13].position, Vector3.up);
            float rightLoad = across.sqrMagnitude > .001f ? Mathf.Clamp01(Vector3.Dot(massOffset, across) / across.sqrMagnitude) : .5f;
            float struckLoad = StruckRegion == MeleeBodyRegion.LeftLeg ? 1f - rightLoad : rightLoad;
            legWeakness = Mathf.Max(legWeakness, leg ? .4f + .6f * struckLoad : 0f);
            if (leg && impact.Kind == CombatImpactKind.Projectile) drop = Mathf.Max(-.14f, drop - .045f);
            dropVelocity += leg ? -.5f - struckLoad * .9f : Mathf.Min(0f, impact.Impulse.y / BodyMass) * .25f;
            MeasureBalance();
        }

        internal void HitStanding(CombatImpact impact, bool hero)
        {
            // A light hit cannot replace an already committed heavy reaction.
            if (!standingResponse && IsActive) return;
            Vector3 direction = Vector3.ProjectOnPlane(impact.Direction, Vector3.up);
            if (!Finite(direction) || direction.sqrMagnitude < .000001f)
                direction = Vector3.ProjectOnPlane(impact.Impulse, Vector3.up);
            if (!Finite(direction) || direction.sqrMagnitude < .000001f) return;
            bool blocked = impact.Result == MeleeHitResult.Blocked;
            standingResponse = true;
            standingCrowbarContact = !hero && impact.Target != null && impact.Target.IsZombie && !impact.Target.State.IsDefeated &&
                impact.Source != null && impact.Source.IsHero && impact.Source.WeaponId == CombatWeaponId.Crowbar &&
                impact.Kind == CombatImpactKind.Weapon && impact.Result == MeleeHitResult.Hit &&
                impact.PrimaryResponse && !impact.DetachedPart;
            standingDirection = direction.normalized;
            standingDuration = hero ? .30f : standingCrowbarContact ? .62f : .38f;
            standingDistance = (hero ? .10f : standingCrowbarContact ? .42f : .22f) * (blocked ? .25f : 1f);
            standingSnap = hero ? .055f : standingCrowbarContact ? .06f : .07f;
            standingFromRotation = rotation;
            standingPeakRotation = Vector3.Cross(Vector3.up, standingDirection) *
                ((hero ? .075f : standingCrowbarContact ? .76f : .12f) * (blocked ? .4f : 1f));
            // The contact frame already breaks the attacking silhouette. The
            // paused body is visibly struck before its head and arms whip back.
            if (standingCrowbarContact)
                rotation = standingFromRotation = Vector3.Lerp(rotation, standingPeakRotation, .70f);
            LastImpact = impact; StruckRegion = impact.Location.Region; age = 0f;
            velocity = standingDirection * (3f * standingDistance / standingDuration);
            angularVelocity = Vector3.zero;
            drop = dropVelocity = legWeakness = overload = BalanceLoad = 0f;
            CaptureOffset = Vector3.zero; WantsKnockdown = false;
            Array.Clear(localRotation, 0, localRotation.Length);
            Array.Clear(localVelocity, 0, localVelocity.Length);
            CancelRecoveryStep(); ClearHandSupport(); CancelMovementLanding();
            ResetProjectileReaction();
            RecoverySequence++;
        }

        private Vector3 AdvanceStanding(float seconds)
        {
            float before = Mathf.Clamp01(age / standingDuration);
            float remainingBefore = 1f - before;
            age = Mathf.Min(standingDuration, age + seconds);
            float remainingAfter = 1f - age / standingDuration;
            Vector3 displacement = standingDirection * standingDistance *
                (remainingBefore * remainingBefore * remainingBefore - remainingAfter * remainingAfter * remainingAfter);
            Vector3 previousRotation = rotation;
            float release = standingCrowbarContact ? .13f : standingSnap;
            rotation = age <= standingSnap
                ? Vector3.Lerp(standingFromRotation, standingPeakRotation, Mathf.SmoothStep(0f, 1f, age / standingSnap))
                : standingPeakRotation * (1f - Mathf.SmoothStep(0f, 1f,
                    (age - release) / (standingDuration - release)));
            velocity = standingDirection * (3f * standingDistance / standingDuration * remainingAfter * remainingAfter);
            angularVelocity = (rotation - previousRotation) / seconds;
            if (age >= standingDuration) velocity = rotation = angularVelocity = Vector3.zero;
            return displacement;
        }

        public void Push(Vector3 momentum)
        {
            if (!Finite(momentum)) return;
            standingResponse = standingCrowbarContact = false;
            velocity = Vector3.ClampMagnitude(velocity + Vector3.ProjectOnPlane(momentum, Vector3.up) / BodyMass, 4.2f);
        }

        public Vector3 Advance(float seconds)
        {
            if (!float.IsFinite(seconds) || seconds <= 0f) return Vector3.zero;
            if (standingResponse) return AdvanceStanding(seconds);
            // Last tick's pressure was already integrated. A moved/removed
            // surface must be measured again before it can supply new support.
            ClearHandSupport();
            Vector3 displacement = Vector3.zero;
            float remaining = Mathf.Min(seconds, .25f);
            while (remaining > .000001f)
            {
                float dt = Mathf.Min(remaining, 1f / 120f); remaining -= dt; age += dt;
                AdvanceProjectileReaction(dt);
                recoveryElapsed += dt;
                recoveryStepRemaining = Mathf.Max(0f, recoveryStepRemaining - dt);
                if (ExperimentalRecovery) AdvanceCounterBalance(dt);
                float decay = Mathf.Exp(-4.2f * dt);
                displacement += velocity * ((1f - decay) / 4.2f); velocity *= decay;
                angularVelocity += (-rotation * 58f - angularVelocity * 10f) * dt;
                rotation = Vector3.ClampMagnitude(rotation + angularVelocity * dt, .72f);
                for (int i = 0; i < bones.Length; i++)
                {
                    localVelocity[i] += (-localRotation[i] * 75f - localVelocity[i] * 10f) * dt;
                    float limit = i == 5 || i == 8 ? .26f : i >= 3 ? .16f : .20f;
                    localRotation[i] = Vector3.ClampMagnitude(localRotation[i] + localVelocity[i] * dt, limit);
                }
                dropVelocity += (-drop * 65f - dropVelocity * 11f) * dt;
                drop = Mathf.Clamp(drop + dropVelocity * dt, -.14f, .025f);
                legWeakness = Mathf.MoveTowards(legWeakness, 0f, dt * 1.6f);
                MeasureBalance();
            }
            return displacement;
        }

        public void AcceptDisplacement(Vector3 requested, Vector3 achieved)
        {
            Vector3 refused = requested - achieved; refused.y = 0f;
            if (refused.magnitude < .002f || velocity.sqrMagnitude < .001f) return;
            Vector3 normal = refused.normalized;
            velocity -= normal * Mathf.Max(0f, Vector3.Dot(velocity, normal));
            if (standingResponse) standingDirection = Vector3.zero;
            // A wall takes the blocked momentum. Never bank refused distance for a later frame.
        }

        public void Apply()
        {
            Restore();
            if (!IsActive) return;
            for (int i = 0; i < bones.Length; i++) { basePositions[i] = bones[i].localPosition; baseRotations[i] = bones[i].localRotation; }
            applied = true;
            if (standingResponse)
            {
                if (standingCrowbarContact)
                {
                    ApplyCrowbarContactPose();
                    return;
                }
                Rotate(1, rotation * .35f);
                Rotate(2, rotation * .65f);
                Rotate(3, rotation * -.12f);
                return;
            }
            bones[0].position += ProjectileWeightShift + Vector3.up * Mathf.Max(ProjectileReactionActive ? -.20f : -.16f,
                drop - .055f * Mathf.Clamp01(BalanceLoad) - .055f * legWeakness - ProjectilePelvisDrop);
            // These are successive joints in one spine, so their bends add.
            // Bound the total added torso bend, including localized contacts.
            float torsoAmount = rotation.magnitude + localRotation[0].magnitude +
                localRotation[1].magnitude + localRotation[2].magnitude + ProjectileTorsoAmount;
            float torsoLimit = ProjectileReactionActive ? .70f : .55f;
            float torsoScale = torsoAmount > torsoLimit ? torsoLimit / torsoAmount : 1f;
            Rotate(0, rotation * (.25f * torsoScale));
            Rotate(1, rotation * (.30f * torsoScale));
            Rotate(2, rotation * (.45f * torsoScale));
            Rotate(3, rotation * -.10f);
            Rotate(4, rotation * .17f);
            for (int i = 0; i < bones.Length; i++) Rotate(i,
                AnatomicalRotation(i, localRotation[i]) * (i <= 2 ? torsoScale : 1f));
            ApplyProjectileReaction(torsoScale);
            if (ExperimentalRecovery) ApplyCounterBalancePose();
        }

        private void ApplyCrowbarContactPose()
        {
            // Layer on the authored hit, whose own spine recoil is only twelve
            // degrees. The legs keep their planted IK while the torso yields.
            float body = CrowbarResponseEnvelope(.06f, .70f, .13f);
            bones[0].position -= Vector3.up * (.09f * body);
            Rotate(1, rotation * .35f);
            Rotate(2, rotation * .65f);

            Vector3 axis = standingPeakRotation.normalized;
            Vector3 direction = Vector3.Cross(axis, Vector3.up);
            // The head briefly trails the shoulders, then overtakes them. This
            // is relative neck motion, not an extra turn of the actor root.
            float head = age <= .10f
                ? Mathf.Lerp(-.12f, .45f, Mathf.SmoothStep(0f, 1f, age / .10f))
                : .45f * (1f - Mathf.SmoothStep(0f, 1f, (age - .15f) / (standingDuration - .15f)));
            Rotate(3, axis * (head * .4f));
            Rotate(4, axis * (head * .6f));

            float arms = CrowbarResponseEnvelope(.085f, .60f, .15f);
            ApplyCrowbarArmFling(5, 6, direction - frame.right * .7f + Vector3.up * .35f,
                arms * (StruckRegion == MeleeBodyRegion.RightArm ? 1f : 1.13f));
            ApplyCrowbarArmFling(8, 9, direction + frame.right * .7f + Vector3.up * .35f,
                arms * (StruckRegion == MeleeBodyRegion.LeftArm ? 1f : 1.08f));
        }

        private float CrowbarResponseEnvelope(float peak, float initial, float release) => age <= peak
            ? Mathf.Lerp(initial, 1f, Mathf.SmoothStep(0f, 1f, age / peak))
            : 1f - Mathf.SmoothStep(0f, 1f, (age - release) / (standingDuration - release));

        private void ApplyCrowbarArmFling(int shoulder, int elbow, Vector3 direction, float radians)
        {
            // Swing the real upper-arm segment toward the impact/outward vector;
            // never twist its local axes or replace the authored elbow hinge.
            Vector3 axis = Vector3.Cross(bones[elbow].position - bones[shoulder].position, direction);
            if (axis.sqrMagnitude > .000001f) Rotate(shoulder, axis.normalized * radians);
        }

        private static int ResponseBone(int contactBone) => contactBone switch
        {
            6 or 7 => 5,
            9 or 10 => 8,
            >= 11 => -1,
            _ => contactBone
        };

        private Vector3 AnatomicalRotation(int index, Vector3 radians)
        {
            // Remove twist about the actual segment, including when the actor is
            // turned or the arm is raised. Never infer a hinge from imported axes.
            int next = index switch { 0 => 1, 1 => 2, 2 => 3, 3 => 4, 5 => 6, 6 => 7, 8 => 9, 9 => 10, _ => -1 };
            Vector3 axis = next >= 0 ? bones[next].position - bones[index].position :
                index == 4 ? bones[4].position - bones[3].position :
                index == 7 ? bones[7].position - bones[6].position :
                index == 10 ? bones[10].position - bones[9].position : Vector3.zero;
            return axis.sqrMagnitude > .00001f ? Vector3.ProjectOnPlane(radians, axis.normalized) : Vector3.zero;
        }

        private void Rotate(int i, Vector3 radians)
        {
            if (radians.sqrMagnitude > .0000001f)
                bones[i].rotation = Quaternion.AngleAxis(radians.magnitude * Mathf.Rad2Deg, radians.normalized) * bones[i].rotation;
        }

        public void Restore()
        {
            if (!applied) return;
            for (int i = 0; i < bones.Length; i++) if (bones[i] != null)
            { bones[i].localPosition = basePositions[i]; bones[i].localRotation = baseRotations[i]; }
            applied = false;
        }
        public void Forget() => applied = false;
        public void Reset()
        {
            Restore(); velocity = rotation = angularVelocity = Vector3.zero;
            Array.Clear(localRotation, 0, localRotation.Length); Array.Clear(localVelocity, 0, localVelocity.Length);
            age = 10f; overload = drop = dropVelocity = legWeakness = BalanceLoad = 0f; CaptureOffset = Vector3.zero;
            standingResponse = standingCrowbarContact = false;
            standingDuration = standingDistance = standingSnap = 0f;
            standingDirection = standingFromRotation = standingPeakRotation = Vector3.zero;
            LastImpact = default; StruckRegion = default; struckBone = 2;
            ResetProjectileReaction();
            ResetBalance();
        }
        private static int BoneIndex(Player3DAnatomicalPart part) => part switch
        {
            Player3DAnatomicalPart.Pelvis => 0, Player3DAnatomicalPart.LowerTorso => 1, Player3DAnatomicalPart.Neck => 3,
            Player3DAnatomicalPart.Head => 4, Player3DAnatomicalPart.LeftUpperArm => 5, Player3DAnatomicalPart.LeftForearm => 6,
            Player3DAnatomicalPart.LeftHand => 7, Player3DAnatomicalPart.RightUpperArm => 8, Player3DAnatomicalPart.RightForearm => 9,
            Player3DAnatomicalPart.RightHand => 10, Player3DAnatomicalPart.LeftThigh => 11, Player3DAnatomicalPart.LeftShin => 12,
            Player3DAnatomicalPart.LeftFoot => 13, Player3DAnatomicalPart.RightThigh => 14, Player3DAnatomicalPart.RightShin => 15,
            Player3DAnatomicalPart.RightFoot => 16, _ => 2
        };
        private static bool Finite(Vector3 v) => float.IsFinite(v.x) && float.IsFinite(v.y) && float.IsFinite(v.z);
    }
}

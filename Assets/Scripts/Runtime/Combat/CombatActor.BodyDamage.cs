using System;
using UnityEngine;

namespace BarPromenade
{
    public sealed partial class CombatActor
    {
        public CombatBodyDamageState BodyDamage { get; } = new CombatBodyDamageState();
        private bool bodySurvivorFall, bodyCrawlInputOwned;
        private Vector2 bodyCrawlInput;
        private float bodyCrawlClock;
        private readonly Transform[] bodyCrawlHands = new Transform[2], bodyCrawlShoulders = new Transform[2];
        private readonly Rigidbody[] bodyCrawlArms = new Rigidbody[2];
        public bool IsBodyGrounded => !State.IsDefeated && IsKnockedDown && IsRagdollActive && !BodyDamage.CanRise;
        public bool CanUseGroundedFirearm => IsBodyGrounded && BodyDamage.CanUseRightHand &&
            (!IsShotgun || BodyDamage.CanUseLeftHand) && !weaponDropped && (Ragdoll?.HasGroundContact ?? false);
        public float BodyCrawlStrokeSeconds => bodyCrawlClock;
        public Vector3 BodyWorldPosition => IsRagdollActive && Ragdoll.PelvisBody != null ? Ragdoll.PelvisBody.position : transform.position;
        public Vector3 GroundedAimDirection
        {
            get
            {
                Vector3 direction = hero != null && Camera.main != null ? Camera.main.transform.forward :
                    contactTarget != null ? contactTarget.BodyWorldPosition - BodyWorldPosition : transform.forward;
                return direction.sqrMagnitude > .0001f ? direction.normalized : transform.forward;
            }
        }

        /// <summary>Scripted/AI input uses the same physical crawling path as WASD.
        /// The input is held until replaced or released, and is frozen by duel pause.</summary>
        public void SetCrawlInput(Vector2 input)
        {
            if (!float.IsFinite(input.x) || !float.IsFinite(input.y)) throw new ArgumentOutOfRangeException(nameof(input));
            bodyCrawlInput = Vector2.ClampMagnitude(input, 1f);
            bodyCrawlInputOwned = true;
        }
        public void ClearCrawlInput() { bodyCrawlInput = Vector2.zero; bodyCrawlInputOwned = false; }

        /// <summary>Called after rules resolve the structural contact and before it is
        /// published. Visibility never decides HP, death, movement or action support.</summary>
        public void ApplyBodyCapabilities(CombatImpact impact)
        {
            State.SetBodyCapabilities(BodyDamage.CanUseRightHand, BodyDamage.CanUseLeftHand, BodyDamage.CanStand);
            if (!BodyDamage.CanUseLeftHand)
            {
                guardHeld = false;
                supportGrip?.SetTarget(false, false);
                supportGrip?.AllowRegrip(false);
            }
            if (!BodyDamage.CanUseRightHand || IsShotgun && !BodyDamage.CanUseLeftHand)
            {
                CancelPendingPistolShot("grip_lost");
                Firearm?.CancelAction();
                if (!weaponDropped) ReleaseWeapon(impact.Direction * .35f, Vector3.Cross(Vector3.up, impact.Direction));
            }
            else if (IsFirearm && !BodyDamage.CanUseLeftHand)
            {
                Firearm?.CancelReload();
            }
            if (!BodyDamage.CanStand)
            {
                CancelPendingKick("leg_support_lost");
                stepBlocked = true;
            }
            if (BodyDamage.IsTerminal)
            {
                if (State.ApplyStructuralFailure()) BeginProjectileDefeat(impact);
                return;
            }
            if (!State.IsDefeated && !BodyDamage.CanStand && !IsKnockedDown)
            {
                Vector3 linear = motor != null ? motor.PlanarVelocity : locomotionVelocity;
                linear += ImpactMotion?.Velocity ?? Vector3.zero;
                bodySurvivorFall = true;
                TryBeginKnockdown(impact, linear, ImpactMotion?.AngularVelocity ?? Vector3.zero);
                bodySurvivorFall = false;
            }
        }

        /// <summary>The intact left palm remains usable after losing the weapon arm.</summary>
        public bool RequestBodyShove()
        {
            if (!BodyDamage.CanUseLeftHand || !CanAttemptBodyAction || !CombatFocused || roundEnded ||
                !GameInput.CanRead(GameInputContext.Gameplay)) return false;
            return TryBeginShove();
        }

        internal void ResetBodyCapabilities()
        {
            bodySurvivorFall = bodyCrawlInputOwned = false;
            bodyCrawlInput = Vector2.zero;
            bodyCrawlClock = 0f;
            State.SetBodyCapabilities(true, true, true);
        }

        private void AdvanceBodySurvivor(float seconds)
        {
            if (!IsBodyGrounded || !BodyDamage.CanCrawl || seconds <= 0f || knockdownFrozen ||
                PauseMenuController.IsAnyPaused || !GameInput.CanRead(GameInputContext.Gameplay)) return;
            // The remaining arm cannot plant a crawl stroke and aim a firearm
            // simultaneously. Releasing aim gives that physical support back.
            if (IsFirearm && (Firearm?.AimRequested ?? false)) return;
            Vector2 input = bodyCrawlInputOwned ? bodyCrawlInput : hero != null ? GameInput.ReadMovement() : Vector2.zero;
            if (input.sqrMagnitude < .0001f || !Ragdoll.HasGroundContact) return;
            Vector3 forward = hero != null && Camera.main != null ? Camera.main.transform.forward : transform.forward;
            forward.y = 0f;
            if (forward.sqrMagnitude < .001f) forward = transform.forward;
            forward.Normalize();
            Vector3 direction = Vector3.ClampMagnitude(forward * input.y + Vector3.Cross(Vector3.up, forward) * input.x, 1f);
            if (direction.sqrMagnitude < .0001f || !Ragdoll.WakeLivingBody()) return;
            bodyCrawlClock += seconds;
            bool left = BodyDamage.CanUseLeftHand;
            bool right = BodyDamage.CanUseRightHand;
            // A loaded pistol uses the right palm. The left arm alone can still
            // pull; a bare usable arm joins the alternating stroke.
            bool rightStroke = right && (weaponDropped || !IsFirearm || !(Firearm?.AimRequested ?? false));
            int arms = (left ? 1 : 0) + (rightStroke ? 1 : 0);
            if (arms == 0) return;
            float cadence = .95f;
            Vector3 planarVelocity = Vector3.ProjectOnPlane(Ragdoll.PelvisBody.linearVelocity, Vector3.up);
            float speed = arms == 2 ? .65f : .38f;
            float force = Mathf.Clamp((speed - Vector3.Dot(planarVelocity, direction.normalized)) * 380f, -90f, 260f);
            for (int side = 0; side < 2; side++)
            {
                if (side == 0 ? !left : !rightStroke) continue;
                RequireCrawlArm(side);
                if (bodyCrawlArms[side] == null || bodyCrawlHands[side] == null || bodyCrawlShoulders[side] == null) continue;
                float phase = Mathf.Repeat(bodyCrawlClock / cadence + (arms == 2 && side == 1 ? .5f : 0f), 1f);
                bool pulling = phase < .65f;
                float reach = pulling ? Mathf.Lerp(.32f, -.15f, phase / .65f) :
                    Mathf.Lerp(-.15f, .32f, (phase - .65f) / .35f);
                Vector3 sideways = Vector3.Cross(Vector3.up, direction.normalized) * (side == 0 ? -.13f : .13f);
                Vector3 target = bodyCrawlShoulders[side].position + direction.normalized * reach + sideways;
                target.y = Ragdoll.GroundContactPoint.y + (pulling ? .07f : .19f);
                Vector3 error = Vector3.ClampMagnitude(target - bodyCrawlHands[side].position, .45f);
                Vector3 armForce = error * 85f - bodyCrawlArms[side].linearVelocity * 5f;
                Player3DAnatomicalPart part = side == 0 ? Player3DAnatomicalPart.LeftForearm : Player3DAnatomicalPart.RightForearm;
                Ragdoll.PhysicsController.AddCombatImpulse(part, bodyCrawlHands[side].position,
                    Vector3.ClampMagnitude(armForce, 60f) * seconds);
                // Visible planted-arm strokes own the body's pull. The collider
                // graph resolves walls/floor; no actor transform is translated.
                if (pulling)
                {
                    float pulse = Mathf.Sin(Mathf.PI * phase / .65f);
                    Ragdoll.PhysicsController.AddCombatImpulse(Player3DAnatomicalPart.Torso,
                        Ragdoll.PhysicsController.ChestBody.worldCenterOfMass,
                        direction.normalized * (force * pulse / arms * seconds));
                }
            }
        }

        private void RequireCrawlArm(int side)
        {
            if (bodyCrawlArms[side] != null) return;
            string suffix = side == 0 ? ".L" : ".R";
            bodyCrawlHands[side] = CityPedestrianHandProps.FindSocket(DamageRigRoot, "hand" + suffix);
            bodyCrawlShoulders[side] = CityPedestrianHandProps.FindSocket(DamageRigRoot, "upper_arm" + suffix);
            Transform forearm = CityPedestrianHandProps.FindSocket(DamageRigRoot, "forearm" + suffix);
            bodyCrawlArms[side] = forearm != null ? forearm.GetComponent<Rigidbody>() : null;
        }

        private void AdvanceGroundedFirearmAim(float seconds)
        {
            if (!IsBodyGrounded)
            {
                Ragdoll?.PhysicsController?.ResetCombatSurvivorArmAim();
                return;
            }
            BindGroundedFirearm();
            pistolAimReachable = false;
            if (Firearm == null || !Firearm.AimRequested)
            {
                Ragdoll.PhysicsController.ResetCombatSurvivorArmAim();
                return;
            }
            if (!CanUseGroundedFirearm || Firearm.ReloadPending || PistolMuzzle == null)
            {
                Ragdoll.PhysicsController.ResetCombatSurvivorArmAim();
                return;
            }
            if (seconds <= 0f || !Ragdoll.WakeLivingBody()) return;
            Vector3 target = PistolAimPoint;
            if (!FinitePistolVector(target) || (target - PistolMuzzle.position).sqrMagnitude < .01f)
            {
                Ragdoll.PhysicsController.ResetCombatSurvivorArmAim();
                return;
            }
            Ragdoll.PhysicsController.AimCombatSurvivorArm(target, PistolMuzzle,
                BodyDamage.CanUseLeftHand ? pistolSupport : null, seconds);
            // Readiness is measured from the actual visible muzzle after the
            // next physical step; a trigger before alignment spends no round.
            pistolAimReachable = true;
        }

        private void BindGroundedFirearm()
        {
            if (!IsBodyGrounded || !IsFirearm || Weapon == null || weaponDropped || !BodyDamage.CanUseRightHand) return;
            handPose.SetGrip(false, 1f);
            handPose.SetGrip(true, BodyDamage.CanUseLeftHand && (Firearm?.AimRequested ?? false) ? FirearmSupportGripWeight : 0f);
            // The kinematic prop's old world pose is not an aiming source. Derive
            // it from this same physical palm before reading the muzzle or
            // committing the body, including while the arm crawls without aim.
            PlaceHeldFirearm();
            CommitHeldPistolPose();
        }

        private void ApplyGroundedFirearmRecoil(Vector3 forward)
        {
            if (!IsBodyGrounded || !CanUseGroundedFirearm) return;
            Ragdoll.PhysicsController.AddCombatImpulse(Player3DAnatomicalPart.RightForearm,
                handPose.CylinderCentre(false), -forward * (IsShotgun ? 7f : 1.25f));
        }
    }
}

using UnityEngine;

namespace BarPromenade
{
    public sealed partial class CombatActor
    {
        public PistolState Pistol { get; private set; }
        public Transform PistolMuzzle { get; private set; }
        internal float PistolSupportError => IsPistol && pistolSupport != null
            ? Vector3.Distance(handPose.CylinderCentre(true), pistolSupport.position) : 0f;
        private AnimationClip pistolRest, pistolRaise, pistolAim, pistolFire, pistolReload, pistolLower;
        private bool pistolShotRequested;
        private int pistolShotRequest;
        private float pistolLowerProgress = 1f;
        private float pistolLeftClosure;
        private Transform pistolFlash, pistolSupport;
        private Transform[] pistolArmBones;
        private Quaternion[] pistolArmRotations;
        private bool pistolAimApplied;
        private Vector3 pistolFreeAimPoint;
        private bool pistolAimReachable;
        internal const float MaximumPistolAimErrorDegrees = 5f;
        internal bool IsFreePistolAiming => !CombatFocused && (Pistol?.AimRequested ?? false);
        internal Vector3 PistolAimPoint => CombatFocused && contactTarget != null
            ? contactTarget.Ragdoll.PhysicsController.ChestBody.position : pistolFreeAimPoint;
        internal float PistolAimErrorDegrees => PistolMuzzle != null && FinitePistolVector(PistolAimPoint)
            ? Vector3.Angle(PistolMuzzle.forward, PistolAimPoint - PistolMuzzle.position) : 180f;
        internal bool PistolAimAligned => pistolAimReachable && PistolAimErrorDegrees <= MaximumPistolAimErrorDegrees;

        private void InitializePistol()
        {
            Pistol = new PistolState();
            pistolRest = CombatPistolAssetProvider.LoadClip("PistolRest");
            pistolRaise = CombatPistolAssetProvider.LoadClip("PistolRaise");
            pistolAim = CombatPistolAssetProvider.LoadClip("PistolAim");
            pistolFire = CombatPistolAssetProvider.LoadClip("PistolFire");
            pistolReload = CombatPistolAssetProvider.LoadClip("PistolReload");
            pistolLower = CombatPistolAssetProvider.LoadClip("PistolLower");
            foreach (AnimationClip clip in new[] { pistolRest, pistolRaise, pistolAim, pistolFire, pistolReload, pistolLower })
                hero.Registry.RegisterRuntimeAnimation(new Player3DAnimationBinding(clip.name, "Combat", clip, clip.length, clip.isLooping));
            PistolMuzzle = CombatPistolAssetProvider.FindAnchor(Weapon, "Muzzle");
            pistolSupport = CombatPistolAssetProvider.FindAnchor(Weapon, "SupportGrip");
            foreach (Transform part in Weapon.GetComponentsInChildren<Transform>(true))
                if (part.name == "MuzzleFlash") { pistolFlash = part; pistolFlash.gameObject.SetActive(false); break; }
            pistolArmBones = new Transform[6];
            pistolArmRotations = new Quaternion[6];
            string[] names = { "upper_arm.R", "forearm.R", "hand.R", "upper_arm.L", "forearm.L", "hand.L" };
            for (int i = 0; i < names.Length; i++)
                pistolArmBones[i] = CityPedestrianHandProps.FindSocket(DamageRigRoot, names[i]);
        }

        internal bool PistolBodyAvailable => IsPistol && Pistol != null && !weaponDropped && (!roundEnded || hero != null) &&
            CanAttemptBodyAction && IsAvailable && State.Phase == MeleePhase.Ready &&
            !(footwork?.RecoveryEpisodeActive ?? false);

        public void SetPistolAim(bool held, Vector3? worldAimPoint = null)
        {
            if (Pistol == null) return;
            if (!CombatFocused)
                pistolFreeAimPoint = worldAimPoint ?? (transform.position + Vector3.up * 1.4f + transform.forward * 40f);
            bool requested = held && PistolBodyAvailable && GameInput.CanRead(GameInputContext.Gameplay);
            if (Pistol.AimRequested && !requested) pistolLowerProgress = 0f;
            Pistol.SetAim(requested);
        }

        /// <summary>Queue one trigger edge for a single attempt on the next live step; true does not promise a shot.</summary>
        public bool RequestPistolShot()
        {
            int request = BeginPistolShotRequest();
            if (!IsPistol || Pistol == null) return RejectPistolShot(request, "no_pistol", "input");
            if (!GameInput.CanRead(GameInputContext.Gameplay)) return RejectPistolShot(request, "input_gate", "input");
            if (pistolShotRequested) return RejectPistolShot(request, "already_requested", "input");
            // This is an input edge, not a promise to fire. The first live step
            // decides readiness after advancing its timers and completed pose.
            pistolShotRequested = true;
            pistolShotRequest = request;
            return true;
        }

        private int BeginPistolShotRequest()
        {
            int request = ++JournalRequestId;
            JournalEvent("pistol_shot_requested", request: request,
                f0: GameLog.Field("rounds", Pistol?.Rounds ?? 0),
                f1: GameLog.Field("aim_requested", Pistol?.AimRequested ?? false));
            return request;
        }

        internal bool RejectPistolInput(string reason) => RejectPistolShot(BeginPistolShotRequest(), reason, "input");

        private bool RejectPistolShot(int request, string reason, string stage)
        {
            JournalEvent("pistol_shot_rejected", request: request,
                f0: GameLog.Field("reason", reason), f1: GameLog.Field("stage", stage),
                f2: GameLog.Field("rounds", Pistol?.Rounds ?? 0),
                f3: GameLog.Field("cooldown_seconds", Pistol?.CooldownRemaining ?? 0f),
                f4: GameLog.Field("aim_progress", Pistol?.AimProgress ?? 0f),
                f5: GameLog.Field("aim_error_degrees", PistolAimErrorDegrees),
                f6: GameLog.Field("phase", (int)State.Phase), f7: GameLog.Field("body_available", PistolBodyAvailable));
            return false;
        }

        internal void CancelPendingPistolShot(string reason)
        {
            if (!pistolShotRequested) return;
            pistolShotRequested = false;
            RejectPistolShot(pistolShotRequest, reason, "cancel");
        }

        private string PistolBodyRejection => !IsPistol || Pistol == null ? "no_pistol" :
            weaponDropped ? "weapon_dropped" : State.IsDefeated ? "defeated" :
            IsKnockedDown || State.IsKnockedDown || IsRagdollActive ? "knocked_down" :
            !CanAttemptBodyAction || !IsAvailable ? "body_unavailable" :
            State.Phase != MeleePhase.Ready ? "phase" : "balance_recovery";

        public bool TryReloadPistol()
        {
            if (!PistolBodyAvailable || !GameInput.CanRead(GameInputContext.Gameplay) || !Pistol.TryReload()) return false;
            CancelPendingPistolShot("reload_started");
            JournalEvent("pistol_reload_started", f0: GameLog.Field("rounds", Pistol.Rounds));
            RetroAudio.PlayAt(RetroSfxId.PistolReload, Weapon.transform.position, .65f);
            Present();
            return true;
        }

        internal bool CommitPistolShot(CombatProjectilePool projectiles)
        {
            if (!pistolShotRequested) return false;
            pistolShotRequested = false;
            int request = pistolShotRequest;
            if (!GameInput.CanRead(GameInputContext.Gameplay)) return RejectPistolShot(request, "input_gate", "commit");
            if (!PistolBodyAvailable) return RejectPistolShot(request, PistolBodyRejection, "commit");
            if (Pistol.IsReloading) return RejectPistolShot(request, "reloading", "commit");
            if (!Pistol.AimRequested) return RejectPistolShot(request, "not_aiming", "commit");
            if (!Pistol.IsAiming) return RejectPistolShot(request, "raising", "commit");
            if (Pistol.Rounds == 0)
            {
                RetroAudio.PlayAt(RetroSfxId.PistolEmpty, PistolMuzzle.position, .65f);
                return RejectPistolShot(request, "empty", "commit");
            }
            if (!Pistol.CanFire) return RejectPistolShot(request, "cooldown", "commit");
            Vector3 target = PistolAimPoint, muzzle = PistolMuzzle.position;
            JournalEvent("pistol_shot_pose", request: request,
                f0: GameLog.Field("x", muzzle.x), f1: GameLog.Field("y", muzzle.y), f2: GameLog.Field("z", muzzle.z),
                f3: GameLog.Field("target_x", target.x), f4: GameLog.Field("target_y", target.y), f5: GameLog.Field("target_z", target.z),
                f6: GameLog.Field("aim_error_degrees", PistolAimErrorDegrees), f7: GameLog.Field("reachable", pistolAimReachable));
            if (!pistolAimReachable) return RejectPistolShot(request, "aim_unreachable", "commit");
            if (!PistolAimAligned) return RejectPistolShot(request, "aim_unaligned", "commit");
            if (projectiles == null || !projectiles.HasCapacity) return RejectPistolShot(request, "projectile_capacity", "commit");
            if (!projectiles.MuzzleIsClear(this, muzzle)) return RejectPistolShot(request, "muzzle_blocked", "commit");
            Vector3 position = PistolMuzzle.position, velocity = PistolMuzzle.forward * CombatProjectilePool.MuzzleSpeed;
            // Both operations run synchronously after CanFire; spawn failure
            // must not consume ammunition or start the firing cooldown.
            if (!projectiles.TrySpawn(this, position, velocity, unchecked(Pistol.ShotSequence + 1)))
                return RejectPistolShot(request, "projectile_spawn", "commit");
            Pistol.TryFire();
            RetroAudio.PlayAt(RetroSfxId.PistolFire, position, 1f);
            JournalEvent("pistol_fired", action: Pistol.ShotSequence, request: request, f0: GameLog.Field("rounds", Pistol.Rounds),
                f1: GameLog.Field("x", position.x), f2: GameLog.Field("y", position.y), f3: GameLog.Field("z", position.z),
                f4: GameLog.Field("dx", velocity.x), f5: GameLog.Field("dy", velocity.y), f6: GameLog.Field("dz", velocity.z),
                f7: GameLog.Field("aim_error_degrees", PistolAimErrorDegrees));
            return true;
        }

        private void AdvancePistol(float seconds)
        {
            if (Pistol == null) return;
            if (!PistolBodyAvailable) { CancelPendingPistolShot(PistolBodyRejection); Pistol.CancelAction(); }
            bool reloading = Pistol.IsReloading;
            Pistol.Advance(seconds);
            pistolLowerProgress = Mathf.Min(1f, pistolLowerProgress + seconds / Mathf.Max(.001f, pistolLower.length));
            if (reloading && !Pistol.IsReloading)
            {
                JournalEvent("pistol_reload_completed", f0: GameLog.Field("rounds", Pistol.Rounds));
                RetroAudio.PlayAt(RetroSfxId.PistolReload, Weapon.transform.position, .75f);
            }
            if (pistolFlash != null) pistolFlash.gameObject.SetActive(!weaponDropped &&
                Pistol.CooldownRemaining > 0f && Pistol.ShotElapsed < .025f);
        }

        internal void SuspendPistolInput()
        {
            CancelPendingPistolShot("input_suspended");
            SetPistolAim(false);
        }

        private void EndPistolAction()
        {
            CancelPendingPistolShot("action_ended");
            Pistol?.CancelAction();
            if (pistolFlash != null) pistolFlash.gameObject.SetActive(false);
        }

        private void ResetPistol()
        {
            CancelPendingPistolShot("reset");
            Pistol?.Reset();
            pistolLowerProgress = 1f;
            if (pistolFlash != null) pistolFlash.gameObject.SetActive(false);
        }

        private AnimationClip ChoosePistolClip() => weaponDropped || State.IsDefeated ? pistolRest : Pistol.IsReloading ? pistolReload :
            Pistol.IsRaising ? pistolRaise : Pistol.IsAiming ?
            (Pistol.CooldownRemaining > 0f && Pistol.ShotElapsed < pistolFire.length ? pistolFire : pistolAim) :
            pistolLowerProgress < 1f ? pistolLower : pistolRest;

        private float PistolClipProgress(AnimationClip clip) => clip == pistolReload ? Pistol.ReloadProgress :
            clip == pistolRaise ? Pistol.AimProgress : clip == pistolFire ? Mathf.Clamp01(Pistol.ShotElapsed / clip.length) :
            clip == pistolLower ? pistolLowerProgress : Mathf.Repeat(poseClock, clip.length) / clip.length;

        private float PistolSupportClosure(AnimationClip clip) => clip == pistolRaise
            ? CombatPistolAssetProvider.SupportGripWeight * Mathf.SmoothStep(0f, 1f, Pistol.AimProgress)
            : clip == pistolAim || clip == pistolFire ? CombatPistolAssetProvider.SupportGripWeight
            : clip == pistolLower ? CombatPistolAssetProvider.SupportGripWeight * (1f - Mathf.SmoothStep(0f, 1f, pistolLowerProgress)) : 0f;

        // The late render and the contact sample use the same constrained, additive arm aim.
        // Restore before any graph evaluation; never accumulate corrections across samples.
        internal void RestorePistolAimPose()
        {
            if (!pistolAimApplied) return;
            for (int i = 0; i < pistolArmBones.Length; i++)
                if (pistolArmBones[i] != null) pistolArmBones[i].localRotation = pistolArmRotations[i];
            pistolAimApplied = false;
        }

        private void ForgetPistolAimPose() => pistolAimApplied = false;

        internal void ApplyPistolAimPose()
        {
            RestorePistolAimPose();
            pistolAimReachable = false;
            if (!IsPistol || weaponDropped || IsRagdollActive || Weapon == null) return;
            // Reassert the live palm contact after graph and physics transform updates.
            CombatPistolAssetProvider.PlacePistol(Weapon, weaponGrip, handPose);
            handPose.SetGrip(false, 1f);
            handPose.SetGrip(true, pistolLeftClosure);
            if (!PistolBodyAvailable || !Pistol.AimRequested || Pistol.IsReloading ||
                pistolArmBones[0] == null || pistolArmBones[1] == null || pistolArmBones[2] == null ||
                pistolArmBones[3] == null || pistolArmBones[4] == null || pistolArmBones[5] == null)
            { CommitHeldPistolPose(); return; }
            for (int i = 0; i < pistolArmBones.Length; i++)
                if (pistolArmBones[i] != null) pistolArmRotations[i] = pistolArmBones[i].localRotation;
            pistolAimApplied = true;
            Vector3 target = PistolAimPoint;
            Vector3 shoulderToTarget = target - pistolArmBones[0].position;
            float sway = .25f + (motor != null ? motor.PlanarVelocity.magnitude * .3f : 0f);
            shoulderToTarget = Quaternion.AngleAxis(Mathf.Sin(poseClock * 8.3f) * sway, transform.up) * shoulderToTarget;
            shoulderToTarget = Quaternion.AngleAxis(Mathf.Sin(poseClock * 6.1f) * sway * .6f, transform.right) * shoulderToTarget;
            // Keep the authored firing kick visible instead of correcting it back to the target.
            float recoil = Pistol.CooldownRemaining > 0f ? Mathf.Max(0f, 1f - Pistol.ShotElapsed / .16f) : 0f;
            shoulderToTarget = Quaternion.AngleAxis(-recoil * 6f, transform.right) * shoulderToTarget;
            pistolAimReachable = TrySolvePistolAim(pistolArmBones[0].position, PistolMuzzle.position,
                PistolMuzzle.forward, pistolArmBones[0].position + shoulderToTarget, out Quaternion solved);
            Quaternion delta = Quaternion.RotateTowards(Quaternion.identity, solved,
                (CombatFocused ? 22f : 80f) * Pistol.AimProgress);
            Vector3 grip = handPose.CylinderCentre(false);
            Vector3 aimedGrip = pistolArmBones[0].position + delta * (grip - pistolArmBones[0].position);
            float forward = Vector3.Dot(aimedGrip - transform.position, transform.forward);
            float downward = -Vector3.Dot(delta * PistolMuzzle.forward, transform.up);
            float minimumForward = Mathf.Lerp(.48f, .40f, Mathf.Clamp01(downward / .5f));
            bool lowAim = downward > .35f;
            bool keepGripForward = forward < minimumForward;
            if (keepGripForward)
            {
                // A low aim cannot fold the whole arm back into the coat. Keep
                // the lowered grip in front, then solve the barrel and arm from
                // that contact point rather than rotating through the torso.
                aimedGrip += transform.forward * (minimumForward - forward);
                Transform hand = pistolArmBones[2];
                for (int pass = 0; pass < 4; pass++)
                {
                    pistolAimReachable &= TrySolvePistolAim(aimedGrip,
                        aimedGrip + PistolMuzzle.position - grip, PistolMuzzle.forward,
                        pistolArmBones[0].position + shoulderToTarget, out Quaternion gripRotation);
                    delta = Quaternion.RotateTowards(Quaternion.identity, gripRotation,
                        (CombatFocused ? 22f : 80f) * Pistol.AimProgress);
                    if (pass == 3) break;
                    Vector3 rightWrist = aimedGrip + delta * (hand.position - grip);
                    Quaternion supportRotation = delta * pistolSupport.rotation;
                    Vector3 supportPosition = aimedGrip + delta * (pistolSupport.position - grip);
                    Pose leftSocket = handPose.GetSocketPose(true, supportPosition,
                        supportRotation * Vector3.up, supportRotation * Vector3.forward);
                    Transform left = pistolArmBones[5];
                    Vector3 leftOffset = Quaternion.Inverse(left.rotation) * (hero.Registry.Anchors.LeftGrip.position - left.position);
                    Vector3 leftWrist = leftSocket.position - leftSocket.rotation * leftOffset;
                    float lift = Mathf.Max(PistolReachLift(0, rightWrist), PistolReachLift(3, leftWrist));
                    if (lift <= .0001f) break;
                    aimedGrip += transform.up * lift;
                }
                Vector3 wrist = aimedGrip + delta * (hand.position - grip);
                Quaternion rotation = delta * hand.rotation;
                Vector3 shoulder = pistolArmBones[0].position;
                Vector3 hint = lowAim ? PistolLowElbowHint(0, wrist, delta * PistolMuzzle.forward)
                    : shoulder + Quaternion.FromToRotation(hand.position - shoulder, wrist - shoulder) *
                        (pistolArmBones[1].position - shoulder) + transform.right * .03f;
                LimbTwoBoneIk.Solve(pistolArmBones[0], pistolArmBones[1], hand, wrist, rotation,
                    hint, Mathf.SmoothStep(0f, 1f, Pistol.AimProgress), .999f, true);
                if (Pistol.AimProgress >= .999f)
                    pistolAimReachable &= Vector3.Distance(handPose.CylinderCentre(false), aimedGrip) <= .003f;
            }
            else pistolArmBones[0].rotation = delta * pistolArmBones[0].rotation;
            Transform leftHand = pistolArmBones[5];
            if (leftHand != null && pistolArmBones[4] != null)
            {
                Pose support = handPose.GetSocketPose(true, pistolSupport.position, pistolSupport.up, pistolSupport.forward);
                Vector3 socketOffset = Quaternion.Inverse(leftHand.rotation) * (hero.Registry.Anchors.LeftGrip.position - leftHand.position);
                Vector3 wrist = support.position - support.rotation * socketOffset;
                Vector3 shoulder = pistolArmBones[3].position;
                Vector3 hint = keepGripForward && lowAim
                    ? PistolLowElbowHint(3, wrist, PistolMuzzle.forward)
                    : shoulder + Quaternion.FromToRotation(leftHand.position - shoulder, wrist - shoulder) *
                        (pistolArmBones[4].position - shoulder);
                float supportWeight = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(.6f, 1f, Pistol.AimProgress));
                // Blend the reaching wrist in world space, then solve the arm.
                // Blending solved joint rotations can arc the sleeve through
                // the chest even when both endpoint poses are clear.
                wrist = Vector3.Lerp(leftHand.position, wrist, supportWeight);
                Quaternion rotation = Quaternion.Slerp(leftHand.rotation, support.rotation, supportWeight);
                hint = Vector3.Lerp(pistolArmBones[4].position, hint, supportWeight) -
                    transform.right * (.12f * Mathf.Sin(Mathf.PI * supportWeight));
                LimbTwoBoneIk.Solve(pistolArmBones[3], pistolArmBones[4], leftHand, wrist, rotation,
                    hint, 1f, .999f, true);
                if (Pistol.AimProgress >= .999f)
                    pistolAimReachable &= PistolSupportError <= .003f;
            }
            CommitHeldPistolPose();
        }

        private Vector3 PistolLowElbowHint(int root, Vector3 wrist, Vector3 distal)
        {
            Vector3 shoulder = pistolArmBones[root].position;
            float forearm = Vector3.Distance(pistolArmBones[root + 1].position, pistolArmBones[root + 2].position);
            Vector3 offset = wrist - distal * forearm - shoulder;
            float lateral = Vector3.Dot(offset, transform.right);
            // Follow the barrel with the wrist while keeping the elbow on the
            // outside branch of its reach circle, ahead of the jacket.
            offset += transform.right * (root == 0 ? Mathf.Max(0f, .30f - lateral) : -Mathf.Max(0f, lateral));
            return shoulder + offset + transform.forward * .06f;
        }

        private float PistolReachLift(int root, Vector3 wrist)
        {
            Vector3 offset = wrist - pistolArmBones[root].position;
            float reach = (Vector3.Distance(pistolArmBones[root].position, pistolArmBones[root + 1].position) +
                Vector3.Distance(pistolArmBones[root + 1].position, pistolArmBones[root + 2].position)) * .98f;
            float horizontal = Vector3.ProjectOnPlane(offset, transform.up).sqrMagnitude;
            if (horizontal >= reach * reach) { pistolAimReachable = false; return 0f; }
            // Only lift a low hold enough for both arms to reach it. The barrel
            // is re-solved after each bounded adjustment to keep its real ray.
            return Mathf.Max(0f, -Vector3.Dot(offset, transform.up) - Mathf.Sqrt(reach * reach - horizontal));
        }

        // A rigid arm rotation moves the muzzle as well as its direction.
        // Intersect the original barrel ray with the shoulder-centred sphere
        // through the target, then rotate that whole point onto the target.
        private static bool TrySolvePistolAim(Vector3 shoulder, Vector3 muzzle, Vector3 forward,
            Vector3 target, out Quaternion rotation)
        {
            rotation = Quaternion.identity;
            if (!FinitePistolVector(shoulder) || !FinitePistolVector(muzzle) ||
                !FinitePistolVector(forward) || !FinitePistolVector(target)) return false;
            Vector3 offset = muzzle - shoulder, direction = target - shoulder;
            float along = Vector3.Dot(offset, forward);
            float discriminant = along * along + direction.sqrMagnitude - offset.sqrMagnitude;
            if (discriminant < 0f || direction.sqrMagnitude < .000001f) return false;
            float distance = -along + Mathf.Sqrt(discriminant);
            if (distance <= .0001f || float.IsInfinity(distance)) return false;
            rotation = Quaternion.FromToRotation(offset + forward * distance, direction);
            return true;
        }

        private static bool FinitePistolVector(Vector3 value) =>
            !float.IsNaN(value.x) && !float.IsInfinity(value.x) &&
            !float.IsNaN(value.y) && !float.IsInfinity(value.y) &&
            !float.IsNaN(value.z) && !float.IsInfinity(value.z);

        private void CommitHeldPistolPose()
        {
            // A parented kinematic body must publish the same completed palm pose
            // as its rendered transform, including between physical steps.
            weaponBody.position = Weapon.transform.position;
            weaponBody.rotation = Weapon.transform.rotation;
        }

        internal void ReceiveProjectile(CombatActor source, int sequence, CombatHurtboxes.Hit hit, Vector3 velocity)
        {
            MeleePhase phaseBefore = State.Phase;
            float health = State.Health;
            MeleeHitResult result = State.ReceiveProjectileHit(25f, hit.Location);
            if (result == MeleeHitResult.Ignored) return;
            reaction = null;
            reactionClock = 0f;
            PublishImpact(new CombatImpact(source, this, sequence, hit.Point, hit.Normal, velocity.normalized,
                health, State.Health, result, hit.Location, 0f, hit.Part, hit.LocalPoint, velocity.magnitude,
                velocity.normalized * 4f, CombatImpactKind.Projectile), phaseBefore);
            if (State.IsDefeated) BeginDefeat(velocity.normalized, hit.Point);
            Present();
        }

        private void IgnorePistolBodyCollisions(bool ignore)
        {
            if (Weapon == null || Ragdoll == null) return;
            foreach (Collider weaponShape in Weapon.GetComponentsInChildren<Collider>())
            {
                if (weaponShape.isTrigger) continue;
                foreach (var bodyShape in Ragdoll.PhysicsController.AnatomicalColliders)
                    Physics.IgnoreCollision(weaponShape, bodyShape.Key, ignore);
                if (Body != null) Physics.IgnoreCollision(weaponShape, Body, ignore);
            }
        }
    }
}

using System;
using Unity.Profiling;
using UnityEngine;

namespace BarPromenade
{
    public enum CombatWeaponCarryLocation { Stowed, Held, Dropped }

    public sealed partial class CombatActor
    {
        // Three bounded records own real models and live ammunition for the round.
        // CombatActor's existing contact adapter binds only the current record.
        private sealed class CarriedWeaponRecord
        {
            internal CombatWeaponId Id;
            internal GameObject Model;
            internal CombatWeaponCarryAssetProvider.Mount Mount;
            internal Rigidbody Body;
            internal Transform Grip, StrikeBase, StrikeTip, Muzzle, Support, Flash;
            internal Vector3 HandPosition, HandScale;
            internal Quaternion HandRotation;
            internal IFirearmState Firearm;
            internal AnimationClip[] FirearmClips, CarryClips;
            internal CombatSupportGrip SupportGrip;
            internal CombatWeaponConstraint Constraint;
            internal CombatHeldWeaponPhysics HeldPhysics;
            internal CombatShotgunSling Sling;
            internal Renderer[] ModelRenderers, MountRenderers;
            internal bool[] ModelForcedOff, MountForcedOff;
            internal CombatWeaponCarryLocation Location;
        }

        private CarriedWeaponRecord[] carriedWeapons;
        private CarriedWeaponRecord activeCarry, carryAction;
        private CombatWeaponId? requestedCarry;
        private bool carryDraw, carryTransferred, carrySettling, resettingCarry, disposingCarry;
        private float carryElapsed, carrySettleElapsed;
        private int carryTerminalFrame = -1;
        private int shotgunDrawStage;
        // Retiming preserves authored contact phases for the rig, jacket and sling.
        private const float CarryPlaybackSpeed = 2f;
        private const float CarrySettleSeconds = .16f / CarryPlaybackSpeed;
        private static readonly ProfilerMarker WeaponCarryPresentMarker = new ProfilerMarker("BarPromenade.WeaponCarry.Present");
        private int projectileSequence;

        public CombatLoadoutSelection LoadoutSelection { get; private set; }
        public bool IsSwitchingWeapon => carryAction != null;
        public bool HasWeaponInHand => activeCarry == null ? !weaponDropped && Weapon != null :
            activeCarry.Location == CombatWeaponCarryLocation.Held;
        private bool HasWeaponSwitchRequest => IsSwitchingWeapon || requestedCarry.HasValue;
        public CombatWeaponId? WeaponCarryActionWeapon => carryAction?.Id;
        public bool WeaponCarryActionIsDraw => carryDraw;
        public float WeaponCarryActionProgress => carryAction == null ? 0f : Mathf.Clamp01(
            carryElapsed / CarryPhaseDuration(carryAction.Id, carryDraw));
        public GameObject CarriedWeapon(CombatWeaponId id) => FindCarriedWeapon(id)?.Model;
        public Transform CarryMount(CombatWeaponId id) => FindCarriedWeapon(id)?.Mount.WeaponDock;
        public IFirearmState CarriedFirearm(CombatWeaponId id) => FindCarriedWeapon(id)?.Firearm;
        public CombatWeaponCarryLocation CarriedWeaponLocation(CombatWeaponId id) =>
            FindCarriedWeapon(id)?.Location ?? throw new ArgumentOutOfRangeException(nameof(id));

        private static float CarryPhaseDuration(CombatWeaponId id, bool draw) =>
            CombatWeaponCarryAssetProvider.PhaseDuration(id, draw) / CarryPlaybackSpeed;

        private CarriedWeaponRecord FindCarriedWeapon(CombatWeaponId id)
        {
            if (carriedWeapons != null)
                foreach (CarriedWeaponRecord record in carriedWeapons)
                    if (record != null && record.Id == id) return record;
            return null;
        }

        private void InitializeWeaponCarry(CombatLoadoutSelection selection)
        {
            LoadoutSelection = selection;
            carriedWeapons = new CarriedWeaponRecord[3];
            carriedWeapons[0] = CreateCarriedWeapon(CombatWeaponId.Pistol);
            carriedWeapons[1] = CreateCarriedWeapon(CombatWeaponId.Crowbar);
            carriedWeapons[2] = CreateCarriedWeapon(CombatWeaponId.Shotgun);
            BindCarriedWeapon(FindCarriedWeapon(selection.InitialHeld));
            activeCarry.Location = CombatWeaponCarryLocation.Held;
            PlaceCarriedWeaponInHand(activeCarry);
            weaponDropped = false;
            var equipment = hero.Registry.GetComponent<PlayerEquipmentContacts>() ??
                hero.Registry.gameObject.AddComponent<PlayerEquipmentContacts>();
            RegisterWeaponEquipment(equipment);
            RefreshShotgunSling();
        }

        private CarriedWeaponRecord CreateCarriedWeapon(CombatWeaponId id)
        {
            WeaponId = id;
            AttachWeapon(hero.Registry.Anchors.RightGrip);
            if (IsFirearm) InitializePistol();
            else Firearm = null;
            var record = new CarriedWeaponRecord
            {
                Id = id, Model = Weapon, Body = weaponBody, Grip = weaponGrip,
                HandPosition = weaponPosition, HandRotation = weaponRotation, HandScale = weaponScale,
                StrikeBase = IsFirearm ? null : strikeBase, StrikeTip = IsFirearm ? null : strikeTip,
                Firearm = Firearm, Muzzle = IsFirearm ? PistolMuzzle : null,
                Support = IsFirearm ? pistolSupport : null, Flash = IsFirearm ? pistolFlash : null,
                FirearmClips = IsFirearm ? new[] { pistolRest, pistolRaise, pistolAim, pistolFire, pistolReload, pistolLower } : null,
                SupportGrip = IsFirearm ? null : supportGrip, Constraint = IsFirearm ? null : weaponConstraint,
                HeldPhysics = IsFirearm ? null : heldWeaponPhysics,
                Mount = CombatWeaponCarryAssetProvider.CreateMount(id, hero.Registry),
                CarryClips = new[] { CombatWeaponCarryAssetProvider.LoadClip(id, false),
                    CombatWeaponCarryAssetProvider.LoadClip(id, true) },
                Location = CombatWeaponCarryLocation.Stowed
            };
            foreach (AnimationClip clip in record.CarryClips)
                hero.Registry.RegisterRuntimeAnimation(new Player3DAnimationBinding(
                    clip.name, "WeaponCarry", clip, clip.length, false));
            if (id == CombatWeaponId.Shotgun)
            {
                record.Sling = record.Model.AddComponent<CombatShotgunSling>();
                record.Sling.Initialize(this, hero.Registry, record.Mount);
            }
            record.ModelRenderers = record.Model.GetComponentsInChildren<Renderer>(true);
            record.MountRenderers = record.Mount.Root.GetComponentsInChildren<Renderer>(true);
            record.ModelForcedOff = CaptureForcedRendering(record.ModelRenderers);
            record.MountForcedOff = CaptureForcedRendering(record.MountRenderers);
            hero.RegisterAccessoryRenderers(record.ModelRenderers);
            hero.RegisterAccessoryRenderers(record.MountRenderers);
            CombatWeaponCarryAssetProvider.PlaceOnMount(record.Model, record.Mount.WeaponDock);
            return record;
        }

        private void BindCarriedWeapon(CarriedWeaponRecord record)
        {
            RestorePistolAimPose();
            activeCarry = record;
            WeaponId = record.Id; Weapon = record.Model;
            weaponBody = record.Body; weaponGrip = record.Grip;
            weaponPosition = record.HandPosition; weaponRotation = record.HandRotation; weaponScale = record.HandScale;
            weaponDropped = record.Location != CombatWeaponCarryLocation.Held;
            strikeBase = record.StrikeBase; strikeTip = record.StrikeTip;
            supportGrip = record.SupportGrip; weaponConstraint = record.Constraint; heldWeaponPhysics = record.HeldPhysics;
            Firearm = record.Firearm; PistolMuzzle = record.Muzzle; pistolSupport = record.Support; pistolFlash = record.Flash;
            if (record.FirearmClips != null)
            {
                pistolRest = record.FirearmClips[0]; pistolRaise = record.FirearmClips[1]; pistolAim = record.FirearmClips[2];
                pistolFire = record.FirearmClips[3]; pistolReload = record.FirearmClips[4]; pistolLower = record.FirearmClips[5];
            }
            pistolAimApplied = false; pistolVisualAimProgress = pistolLeftClosure = pistolBlendClosure = 0f;
            weaponConstraint?.SetOpponent(contactTarget); heldWeaponPhysics?.SetOpponent(contactTarget);
            supportGrip?.Reset(); weaponConstraint?.Reset();
            ResetWeaponContacts(); visibleClip = null; winnerPresentationReleased = false; freeLocomotionReleased = false;
        }

        private void PlaceCarriedWeaponInHand(CarriedWeaponRecord record)
        {
            if (record.Id == CombatWeaponId.Shotgun) CombatShotgunAssetProvider.PlaceShotgun(record.Model, record.Grip, handPose);
            else if (record.Id == CombatWeaponId.Pistol) CombatPistolAssetProvider.PlacePistol(record.Model, record.Grip, handPose);
            else CombatAssetProvider.PlaceCrowbar(record.Model, record.Grip, handPose, false);
        }

        public bool TrySelectWeapon(CombatWeaponId id)
        {
            CarriedWeaponRecord record = FindCarriedWeapon(id);
            int request = JournalCommand("select_weapon");
            string reason = record == null ? "not_carried" : record.Location == CombatWeaponCarryLocation.Dropped ? "dropped" :
                resettingCarry || disposingCarry ? "lifecycle" : !BodyDamage.CanUseRightHand || !BodyDamage.CanUseLeftHand &&
                (id != CombatWeaponId.Crowbar || HasWeaponInHand && activeCarry?.Id != CombatWeaponId.Crowbar) ? "hands_unavailable" :
                State.IsDefeated || IsKnockedDown || IsRagdollActive ? "body_unavailable" :
                !isActiveAndEnabled ? "disabled" : !GameInput.CanRead(GameInputContext.Gameplay) ? "input_gate" : null;
            if (reason != null) { JournalWeaponCarry("request", record, reason); return false; }
            JournalWeaponCarry("request", record);
            if (!IsSwitchingWeapon && !requestedCarry.HasValue && activeCarry == record && HasWeaponInHand) return true;
            requestedCarry = id;
            journalCarryQueuedRequest = request;
            CancelPendingPistolShot("weapon_selection"); SetPistolAim(false);
            guardHeld = false; State.SetBlocking(false);
            State.CancelCharge(); State.CancelBufferedAction(State.BufferedAction);
            CancelPendingKick("weapon_selection");
            TryBeginPendingWeaponCarry();
            return true;
        }

        internal bool TrySelectNextWeapon()
        {
            if (carriedWeapons == null) return false;
            CombatWeaponId next = requestedCarry ?? WeaponId;
            for (int index = 0; index < carriedWeapons.Length; index++)
            {
                next = next switch
                {
                    CombatWeaponId.Pistol => CombatWeaponId.Crowbar,
                    CombatWeaponId.Crowbar => CombatWeaponId.Shotgun,
                    _ => CombatWeaponId.Pistol
                };
                CarriedWeaponRecord record = FindCarriedWeapon(next);
                if (record != null && record.Location != CombatWeaponCarryLocation.Dropped && TrySelectWeapon(next))
                    return true;
            }
            return false;
        }

        private bool CanStartWeaponCarry => hero != null && BodyDamage.CanStand && BodyDamage.CanUseRightHand &&
            !State.IsDefeated && !IsKnockedDown && !IsRagdollActive && State.Phase == MeleePhase.Ready &&
            reaction == null && !(ImpactMotion?.IsActive ?? false) && !(footwork?.RecoveryEpisodeActive ?? false) &&
            !(Firearm?.ReloadPending ?? false) && pistolVisualAimProgress <= .001f && IsAvailable &&
            GameInput.CanRead(GameInputContext.Gameplay);

        private void TryBeginPendingWeaponCarry()
        {
            if (IsSwitchingWeapon || !requestedCarry.HasValue || !CanStartWeaponCarry) return;
            CarriedWeaponRecord target = FindCarriedWeapon(requestedCarry.Value);
            if (target == null || target.Location == CombatWeaponCarryLocation.Dropped)
            { requestedCarry = null; return; }
            if (activeCarry == target && HasWeaponInHand) { requestedCarry = null; return; }
            BeginWeaponCarryPhase(HasWeaponInHand ? activeCarry : target, !HasWeaponInHand);
        }

        private void BeginWeaponCarryPhase(CarriedWeaponRecord record, bool draw)
        {
            EndPistolAction(); ReleasePresentation(preservePresentedPose: true);
            if (draw) BindCarriedWeapon(record);
            carryAction = record; carryDraw = draw; carryTransferred = false;
            journalCarryActionRequest = journalCarryQueuedRequest;
            shotgunDrawStage = 0;
            carryElapsed = carrySettleElapsed = 0f; carryTerminalFrame = -1; carrySettling = true;
            JournalWeaponCarry("begin", record);
            AnimationClip clip = record.CarryClips[draw ? 1 : 0];
            if (!hero.TryAcquireClip(this, clip.name)) { CancelWeaponCarry(); return; }
            hero.SetOwnedClipLocomotion(this, true);
            visibleClip = clip.name; winnerPresentationReleased = false; freeLocomotionReleased = false;
            BeginPoseBlend(CarrySettleSeconds, force: true);
            PresentWeaponCarry();
        }

        private void AdvanceWeaponCarry(float seconds)
        {
            UpdateConcealedPistol();
            if (carriedWeapons == null || presentationFrozen || PauseMenuController.IsAnyPaused || GameTimeScaleRuntime.IsPaused) return;
            foreach (CarriedWeaponRecord record in carriedWeapons)
                if (record != activeCarry)
                {
                    record.Firearm?.Advance(seconds);
                    UpdateStowedWeaponMechanics(record);
                }
            if (IsSwitchingWeapon && (!BodyDamage.CanStand || !BodyDamage.CanUseRightHand ||
                carryAction.Id != CombatWeaponId.Crowbar && !BodyDamage.CanUseLeftHand ||
                State.IsDefeated || IsKnockedDown || IsRagdollActive || State.Phase != MeleePhase.Ready || !IsAvailable))
            { CancelWeaponCarry(); return; }
            if (!IsSwitchingWeapon) { TryBeginPendingWeaponCarry(); return; }
            if (carryTerminalFrame >= 0)
            {
                if (Time.frameCount <= carryTerminalFrame) return;
                bool drew = carryDraw;
                JournalWeaponCarry("complete", carryAction);
                carryAction = null;
                if (drew)
                {
                    ReleaseStandingPresentation();
                    freeLocomotionReleased = !CombatFocused;
                }
                else ReleasePresentation(preservePresentedPose: true);
                CombatWeaponCarryAssetProvider.SetJacketAccess(hero.Registry, 0f);
                if (!drew && requestedCarry.HasValue)
                {
                    CarriedWeaponRecord target = FindCarriedWeapon(requestedCarry.Value);
                    if (target != null && target.Location == CombatWeaponCarryLocation.Stowed)
                    { BeginWeaponCarryPhase(target, true); return; }
                }
                if (drew && requestedCarry == activeCarry.Id) requestedCarry = null;
                TryBeginPendingWeaponCarry();
                return;
            }
            if (carrySettling)
            {
                carrySettleElapsed = Mathf.Min(CarrySettleSeconds, carrySettleElapsed + seconds);
                hero.SetOwnedRecoveryPoseClock(this, carrySettleElapsed);
                if (carrySettleElapsed < CarrySettleSeconds) return;
                carrySettling = false; CancelPoseBlend();
                return;
            }
            float duration = CarryPhaseDuration(carryAction.Id, carryDraw);
            float next = Mathf.Min(duration, carryElapsed + seconds);
            if (carryAction.Id == CombatWeaponId.Shotgun && carryDraw)
            {
                while (shotgunDrawStage < 3)
                {
                    float contact = CombatWeaponCarryAssetProvider.ShotgunDrawTransferSeconds(shotgunDrawStage + 1) / CarryPlaybackSpeed;
                    if (next + .000001f < contact) break;
                    carryElapsed = contact;
                    PresentWeaponCarry();
                    PlaceShotgunDrawStage(++shotgunDrawStage);
                }
                carryElapsed = next;
                if (next + .000001f >= duration) carryTerminalFrame = Time.frameCount;
                return;
            }
            float transfer = duration * CombatWeaponCarryAssetProvider.TransferNormalizedTime(carryAction.Id, carryDraw);
            if (!carryTransferred && next + .000001f >= transfer)
            {
                carryElapsed = transfer;
                PresentWeaponCarry();
                if (carryDraw)
                {
                    PlaceCarriedWeaponInHand(carryAction);
                    carryAction.Location = CombatWeaponCarryLocation.Held; weaponDropped = false;
                }
                else
                {
                    carryAction.HeldPhysics?.DisableHeld();
                    CombatWeaponCarryAssetProvider.PlaceOnMount(carryAction.Model, carryAction.Mount.WeaponDock);
                    carryAction.Location = CombatWeaponCarryLocation.Stowed; weaponDropped = true;
                    ResetWeaponContacts();
                }
                carryTransferred = true;
                JournalWeaponCarry("transfer", carryAction);
                UpdateConcealedPistol();
            }
            carryElapsed = next;
            if (next + .000001f >= duration) carryTerminalFrame = Time.frameCount;
        }

        private void PlaceShotgunDrawStage(int stage)
        {
            CombatWeaponCarryBindings.Slot slot = carryAction.Mount.Binding;
            if (stage == 1)
                CombatWeaponCarryAssetProvider.PlaceInTransferHand(carryAction.Model, hero.Registry.Anchors.RightGrip,
                    slot.PushHandLocalPosition, slot.PushHandLocalRotation);
            else if (stage == 2)
                CombatWeaponCarryAssetProvider.PlaceInTransferHand(carryAction.Model, hero.Registry.Anchors.LeftGrip,
                    slot.CatchHandLocalPosition, slot.CatchHandLocalRotation);
            else PlaceCarriedWeaponInHand(carryAction);
            carryAction.Location = CombatWeaponCarryLocation.Held;
            weaponDropped = false;
            carryTransferred = true;
            JournalWeaponCarry("transfer", carryAction, stage: stage);
        }

        private bool PresentWeaponCarry()
        {
            if (!IsSwitchingWeapon || hero == null || IsRagdollActive || State.IsDefeated) return false;
            using var profile = WeaponCarryPresentMarker.Auto();
            AnimationClip clip = carryAction.CarryClips[carryDraw ? 1 : 0];
            if (!hero.OwnsClip(this) && !hero.TryAcquireClip(this, clip.name)) return false;
            hero.SetOwnedClipLocomotion(this, true);
            hero.SampleOwnedClip(this, WeaponCarryActionProgress);
            hero.ClearCombatFirearm(this); hero.ClearCombatSupportGrip(this);
            bool shotgunDraw = carryAction.Id == CombatWeaponId.Shotgun && carryDraw;
            handPose.SetGrip(false, shotgunDraw ? CombatWeaponCarryAssetProvider.ShotgunDrawHandClosure(false,
                WeaponCarryActionProgress) : HasWeaponInHand ? 1f : 0f);
            handPose.SetGrip(true, shotgunDraw ? CombatWeaponCarryAssetProvider.ShotgunDrawHandClosure(true,
                WeaponCarryActionProgress) : carryAction.Id == CombatWeaponId.Shotgun ? .55f *
                CombatWeaponCarryAssetProvider.SlingLeftHandAmount(carryDraw, WeaponCarryActionProgress) : 0f);
            CombatWeaponCarryAssetProvider.SetJacketAccess(hero.Registry,
                CombatWeaponCarryAssetProvider.JacketAccessAmount(carryAction.Id, carryDraw, WeaponCarryActionProgress));
            motor.ReleaseMovementConstraint(this);
            motor.SetOwnedMovementConstraint(this, 1f, 1f, sprintBlocked: true);
            hero.ReapplyLatePresentationPose();
            hero.RememberOwnedRecoveryPose(this, poseClock);
            RefreshShotgunSling();
            return true;
        }

        private bool CancelWeaponCarry(bool retainPose = false)
        {
            requestedCarry = null;
            if (!IsSwitchingWeapon) return false;
            bool releaseTransientHold = carryAction.Id == CombatWeaponId.Shotgun && carryDraw &&
                shotgunDrawStage > 0 && shotgunDrawStage < 3 && !resettingCarry && !disposingCarry && gameObject.activeInHierarchy;
            JournalWeaponCarry("cancel", carryAction, resettingCarry || disposingCarry ? "lifecycle" : "interrupted");
            carryAction = null; carryTerminalFrame = -1;
            // A palm push or upper catch is not a stable firing hold. Interruptions
            // release the same real gun at its live pose instead of snapping it to
            // the distant trigger hand; normal drop/pickup retains its ammunition.
            if (releaseTransientHold) ReleaseWeapon(Vector3.zero, Vector3.zero);
            if (hero != null) CombatWeaponCarryAssetProvider.SetJacketAccess(hero.Registry, 0f);
            motor?.ReleaseMovementConstraint(this);
            if (!retainPose) ReleasePresentation(preservePresentedPose: true);
            return releaseTransientHold;
        }

        private void ResetWeaponCarry()
        {
            if (carriedWeapons == null) return;
            resettingCarry = true;
            CancelWeaponCarry(); projectileSequence = 0;
            foreach (CarriedWeaponRecord record in carriedWeapons)
            {
                record.Model.GetComponent<CombatDroppedWeaponPickup>()?.CancelPickup();
                record.HeldPhysics?.ResetWeapon();
                if (!record.Body.isKinematic) record.Body.linearVelocity = record.Body.angularVelocity = Vector3.zero;
                record.Body.isKinematic = true; record.Body.useGravity = record.Body.detectCollisions = false;
                record.Body.interpolation = RigidbodyInterpolation.None;
                SetCarriedBodyCollisionIgnored(record, false);
                record.Firearm?.Reset(); record.Location = CombatWeaponCarryLocation.Stowed;
                UpdateStowedWeaponMechanics(record);
                record.Model.SetActive(true);
                CombatWeaponCarryAssetProvider.PlaceOnMount(record.Model, record.Mount.WeaponDock);
            }
            BindCarriedWeapon(FindCarriedWeapon(LoadoutSelection.InitialHeld));
            activeCarry.Location = CombatWeaponCarryLocation.Held; weaponDropped = false;
            PlaceCarriedWeaponInHand(activeCarry);
            pistolHandMagazine?.SetActive(false); shotgunHandShell?.SetActive(false);
            resettingCarry = false;
            RefreshShotgunSling();
        }

        private void UpdateStowedWeaponMechanics(CarriedWeaponRecord record)
        {
            if (record.Id == CombatWeaponId.Pistol && record.Firearm is PistolState pistol && pistolSlide != null)
            {
                pistolSlide.position = record.Model.transform.TransformPoint(pistolSlideRestPosition -
                    Vector3.forward * (PistolSlideTravel * pistol.SlideBack));
                pistolMountedMagazine.gameObject.SetActive(pistol.MagazineAttached);
            }
            else if (record.Id == CombatWeaponId.Shotgun && record.Firearm is ShotgunState shotgun && shotgunHinge != null)
            {
                shotgunHinge.localRotation = shotgunHingeRest * Quaternion.AngleAxis(48f * shotgun.BreakOpen01, Vector3.right);
                for (int i = 0; i < 2; i++) shotgunShells[i].gameObject.SetActive(shotgun.ChamberLoaded(i) || shotgun.ChamberSpent(i));
            }
            if (record.Flash != null) record.Flash.gameObject.SetActive(false);
        }

        private void OnEnable()
        {
            if (carriedWeapons == null || hero == null) return;
            RegisterWeaponEquipment(hero.Registry.GetComponent<PlayerEquipmentContacts>());
            UpdateConcealedPistol();
        }

        private void RegisterWeaponEquipment(PlayerEquipmentContacts equipment)
        {
            if (equipment == null) return;
            // Concealed chest gear does not drive jacket cloth. The authored
            // hand opens the coat; no inflated collision envelope is needed.
            equipment.Register(this, new[] { carriedWeapons[0].Model.transform, carriedWeapons[0].Mount.Root.transform },
                carriedWeapons[0].Model, carriedWeapons[1].Model, carriedWeapons[2].Model,
                carriedWeapons[0].Mount.Root, carriedWeapons[1].Mount.Root, carriedWeapons[2].Mount.Root);
        }

        private void UpdateConcealedPistol()
        {
            CarriedWeaponRecord pistol = FindCarriedWeapon(CombatWeaponId.Pistol);
            if (pistol == null || hero == null) return;
            PlayerWardrobe wardrobe = hero.Registry.GetComponentInParent<PlayerWardrobe>();
            if (wardrobe == null) wardrobe = hero.Registry.GetComponentInChildren<PlayerWardrobe>();
            bool jacketWorn = wardrobe != null && wardrobe.GetEquippedItem("jacket") != null;
            // Garment coverage owns rendering only. Model identity, dock pose,
            // ammunition and the authored grip transfer remain continuous.
            SetConcealedRendering(pistol.ModelRenderers, pistol.ModelForcedOff,
                jacketWorn && pistol.Location == CombatWeaponCarryLocation.Stowed);
            SetConcealedRendering(pistol.MountRenderers, pistol.MountForcedOff, jacketWorn);
        }

        private static bool[] CaptureForcedRendering(Renderer[] renderers)
        {
            var original = new bool[renderers.Length];
            for (int i = 0; i < original.Length; i++) original[i] = renderers[i].forceRenderingOff;
            return original;
        }

        private static void SetConcealedRendering(Renderer[] renderers, bool[] original, bool concealed)
        {
            for (int i = 0; i < renderers.Length; i++)
                if (renderers[i] != null) renderers[i].forceRenderingOff = concealed || original[i];
        }

        private void SetCarriedBodyCollisionIgnored(CarriedWeaponRecord record, bool ignore)
        {
            if (record.Id == CombatWeaponId.Crowbar || Ragdoll == null) return;
            foreach (Collider shape in record.Model.GetComponentsInChildren<Collider>(true))
                foreach (var anatomical in Ragdoll.PhysicsController.AnatomicalColliders)
                    if (shape != null && anatomical.Key != null) Physics.IgnoreCollision(shape, anatomical.Key, ignore);
        }

        private void DisposeWeaponCarry()
        {
            if (carriedWeapons == null || disposingCarry) return;
            disposingCarry = true; CancelWeaponCarry();
            UnregisterWeaponEquipment();
            foreach (CarriedWeaponRecord record in carriedWeapons)
            {
                SetConcealedRendering(record.ModelRenderers, record.ModelForcedOff, false);
                SetConcealedRendering(record.MountRenderers, record.MountForcedOff, false);
                if (record.Model != null) record.Model.GetComponent<CombatDroppedWeaponPickup>()?.CancelPickup();
                record.Constraint?.Dispose(); record.HeldPhysics?.Dispose();
                if (record.HeldPhysics != null) Destroy(record.HeldPhysics);
                if (record.Model != null && record.Location == CombatWeaponCarryLocation.Dropped) Destroy(record.Model);
            }
            heldWeaponPhysics = null; weaponConstraint = null;
            carriedWeapons = null;
        }

        private void UnregisterWeaponEquipment()
        {
            FindCarriedWeapon(CombatWeaponId.Shotgun)?.Sling?.ReleaseContacts();
            if (hero != null && hero.Registry != null)
                hero.Registry.GetComponent<PlayerEquipmentContacts>()?.Unregister(this);
        }

        private void RefreshShotgunSling()
        {
            UpdateConcealedPistol();
            FindCarriedWeapon(CombatWeaponId.Shotgun)?.Sling?.RequestPresent();
        }
    }
}

using System;
using System.Collections.Generic;
using UnityEngine;

namespace BarPromenade
{
    /// <summary>A finite paired scene at the raised gate's actual door, on the existing world rigs.</summary>
    [DefaultExecutionOrder(600)]
    public sealed class WarehouseDoorAmbushScene : MonoBehaviour
    {
        public const string CutsceneId = "warehouse.door-ambush";
        internal const float ActionStretch = 2f;
        internal const double ScriptDurationSeconds = 8d;
        internal const double AttackCueSeconds = 3d;
        private CombatTestRoot owner;
        private WarehouseGateController gate;
        private WarehouseArenaMechanisms mechanisms;
        private WarehouseValveLampFlicker valveLamp;
        private GameObject guides;
        private Playback playback;
        private Playback hangingPlayback;
        private PlayerAnimatedInteractionController interaction;
        private Player3DCharacterPresentation hero;
        private Camera sceneCamera;
        private CombatActor suspension;

        public bool IsActive => playback != null || hangingPlayback != null;
        internal bool HangingGameplayActive => hangingPlayback != null;
        public Vector3 EntryRootPosition => owner.WarehousePlan.GoalDoorPosition +
            new Vector3(-.01f, PlayerFactory.GroundedRootOffset, -.70f);
        public CombatActor Source { get; private set; }
        internal NarrativeInteraction DoorInteraction => gate != null ? gate.DoorInteraction : null;
        internal bool AttackStarted => playback != null && playback.AttackStarted;

        internal void Initialize(CombatTestRoot root, CombatActor source, CombatActor existingSuspension)
        {
            owner = root; Source = source; suspension = existingSuspension;
            hero = (Player3DCharacterPresentation)root.Player.Visual;
            interaction = root.Player.GameObject.GetComponent<PlayerAnimatedInteractionController>();
            sceneCamera = root.CameraFollow.GetComponent<Camera>();
            hero.Registry.RegisterRuntimeAnimationBank("CombatDoorAmbush/HeroActions", "Contextual");
            WarehouseDoorAmbushAssets.Clip(WarehouseDoorAmbushAssets.Attack);
            _ = WarehouseDoorAmbushAssets.Plan;
            guides = WarehouseDoorAmbushAssets.CreateGuides(root.transform, out Transform[] anchors);
            Source.ConfigureDoorAmbushCableGuides(anchors);
            guides.SetActive(false);
            BindLocation();
        }

        internal void BindLocation()
        {
            gate = owner.GetComponentInChildren<WarehouseGateController>();
            mechanisms = owner.GetComponentInChildren<WarehouseArenaMechanisms>();
            valveLamp = mechanisms != null ? mechanisms.GetComponentInChildren<WarehouseValveLampFlicker>(true) : null;
            if (gate == null || mechanisms == null || valveLamp == null)
                throw new InvalidOperationException("Door ambush requires the warehouse's real gate, red lamp and fan.");
            gate.DoorInteraction.InteractionHandler = GameSessionState.WarehouseDoorAmbushPlayed ? null : OnDoorInteraction;
        }

        private void OnDoorInteraction(PlayerInteractor interactor)
        {
            if (interactor == owner.Player.Interactor) TryStart();
        }

        public bool TryStart()
        {
            if (gate == null || !gate.IsOpen || IsActive || GameSessionState.WarehouseDoorAmbushPlayed ||
                !owner.Hero.CanBeginStandingInspection) return false;
            return owner.Cutscenes.TryPlayGameplay(CutsceneId, completed =>
            {
                if (completed)
                {
                    GameSessionState.MarkWarehouseDoorAmbushPlayed();
                    if (gate != null) gate.DoorInteraction.InteractionHandler = null;
                }
            });
        }

        internal ICutscenePlayback CreatePlayback() => new Playback(this, owner.Cutscenes.IsDebugPreview);

        internal void ResetEpisode()
        {
            owner?.Cutscenes?.Stop();
            ClearHangingGameplay();
        }

        private void Update()
        {
            // The retained physical episode owns both actors after the movie
            // clock ends; ordinary combat must not advance them a second time.
            if (hangingPlayback != null && GameInput.MovementFocused && !GameTimeScaleRuntime.IsPaused &&
                !PauseMenuController.IsAnyPaused && !SceneTransitionService.IsTransitioning)
                Source.AdvanceDoorAmbush(Time.deltaTime);
        }

        private void LateUpdate()
        {
            // The hook contact is final only after the hero's rig presentation.
            if (IsActive && Source != null)
            {
                Source.Present();
                playback?.PresentCamera();
            }
        }

        private void OnDisable()
        {
            if (IsActive) owner?.Cutscenes?.Stop();
            ClearHangingGameplay();
            if (gate != null && gate.DoorInteraction != null) gate.DoorInteraction.InteractionHandler = null;
        }

        private void OnDestroy()
        {
            ClearHangingGameplay();
            if (Source != null) Source.EndDoorAmbush();
            if (guides != null) Destroy(guides);
        }

        private void ClearHangingGameplay(bool prepareAgain = false)
        {
            Playback retained = hangingPlayback;
            hangingPlayback = null;
            retained?.ReleaseHangingGameplay(prepareAgain);
        }

        private sealed class Playback : ICutscenePreparationPlayback, ICutsceneInteractivePlayback
        {
            private readonly WarehouseDoorAmbushScene scene;
            private readonly bool preview;
            private readonly List<Vector3> route = new List<Vector3>(64);
            private bool prepared, handoff, previousFocus, stopped, claimed, fanOverdrive, committed, lit;
            private Vector3 approachPosition;
            private double sampled;
            private int terminalFrame = -1;
            private Pose sourcePose;
            private float interactiveSeconds;
            private float cameraInversionSeconds;
            private Pose previousCamera;
            private Pose previousHero;
            private float previousHealth, previousStamina, previousFallDamage;
            private int previousFallCount;
            private float previousFieldOfView;
            private bool previousFixed;
            private Pose previousFixedPose;
            private float previousFixedLens;
            private readonly AnimationClip attack, hold, retreat, heroHold;

            internal Playback(WarehouseDoorAmbushScene owner, bool debug)
            {
                scene = owner; preview = debug;
                attack = WarehouseDoorAmbushAssets.Clip(WarehouseDoorAmbushAssets.Attack);
                hold = WarehouseDoorAmbushAssets.Clip(WarehouseDoorAmbushAssets.CraneHold);
                retreat = WarehouseDoorAmbushAssets.Clip(WarehouseDoorAmbushAssets.Retreat);
                heroHold = WarehouseDoorAmbushAssets.Clip(WarehouseDoorAmbushAssets.Hold);
            }

            public double DurationSeconds => ScriptDurationSeconds;
            public bool IsReady => prepared && scene.interaction.Phase != PlayerAnimatedInteractionPhase.Positioning;
            public bool WantsGameplay => handoff;
            public bool IsComplete => handoff && scene.Source.DoorAmbushHoistComplete && cameraInversionSeconds >= 2f;
            public bool KeepsGameplayOnCompletion => true;
            internal bool AttackStarted => sampled >= AttackCueSeconds;

            public void Prepare()
            {
                scene.ClearHangingGameplay(prepareAgain: true);
                scene.owner.Cutscenes.RefreshReturnCameraSnapshot();
                if (scene.IsActive) throw new InvalidOperationException("The warehouse door scene is already active.");
                if (preview) scene.owner.PrepareDoorAmbushDebugProgress();
                if ((!preview && !scene.gate.IsOpen) || !scene.owner.Hero.CanBeginStandingInspection)
                    throw new InvalidOperationException("The hero is not ready for the warehouse door.");
                scene.playback = this;
                claimed = true;
                previousFocus = scene.owner.Hero.CombatFocused;
                previousHero = new Pose(scene.owner.Hero.transform.position, scene.owner.Hero.transform.rotation);
                previousHealth = scene.owner.Hero.State.Health;
                previousStamina = scene.owner.Hero.State.Stamina;
                previousFallCount = scene.owner.Hero.RestraintFallDamageCount;
                previousFallDamage = scene.owner.Hero.LastRestraintFallDamage;
                previousCamera = new Pose(scene.sceneCamera.transform.position, scene.sceneCamera.transform.rotation);
                previousFieldOfView = scene.sceneCamera.fieldOfView;
                approachPosition = scene.owner.Player.GameObject.transform.position;
                previousFixed = scene.owner.CameraFollow.FixedPoseActive;
                previousFixedPose = scene.owner.CameraFollow.FixedBasePose;
                previousFixedLens = scene.owner.CameraFollow.FixedBaseFieldOfView;
                if (!scene.valveLamp.BeginDoorAmbushLighting(this, scene.EntryRootPosition + new Vector3(-.02f, 1.26f, -.22f)))
                    throw new InvalidOperationException("The door's red light could not frame the paired actors.");
                lit = true;
                if (preview)
                {
                    // A debug boundary stages the shot directly; gameplay still
                    // enters through the shared grounded approach at the door.
                    scene.owner.Player.Motor.Teleport(scene.EntryRootPosition);
                    scene.owner.Hero.transform.rotation = Quaternion.identity;
                    approachPosition = scene.EntryRootPosition;
                    Physics.SyncTransforms();
                    SetOpeningCamera(0f);
                }
                if (!scene.owner.Hero.BeginDoorAmbushWeaponStow(this))
                    throw new InvalidOperationException("The hero could not free a hand for the door.");
                if (scene.owner.Hero.IsDoorAmbushWeaponStowed) BeginDoorApproach();
            }

            private void BeginDoorApproach()
            {
                scene.owner.Hero.PrepareStandingInspection();
                var plan = WarehouseDoorAmbushAssets.Plan;
                Vector3 entry = scene.EntryRootPosition;
                Vector3 feet = entry - Vector3.up * PlayerFactory.GroundedRootOffset;
                Vector3 currentFeet = scene.owner.Player.GameObject.transform.position - Vector3.up * PlayerFactory.GroundedRootOffset;
                CharacterController capsule = scene.owner.Hero.Body;
                if (!scene.owner.WarehouseArea.FindRoute(currentFeet, feet, route, capsule.radius + .015f, capsule.height))
                    throw new InvalidOperationException("The door has no reachable grounded approach.");
                for (int i = 0; i < route.Count; i++) route[i] += Vector3.up * PlayerFactory.GroundedRootOffset;
                var definition = new PlayerAnimatedInteractionDefinition(WarehouseDoorAmbushAssets.Enter,
                    WarehouseDoorAmbushAssets.Hold, WarehouseDoorAmbushAssets.Exit,
                    enterFrameCount: 800, enterFramesPerSecond: 100f, loopFrameCount: 100, loopFramesPerSecond: 50f,
                    exitFrameCount: 65, exitFramesPerSecond: 50f);
                var entryPose = new PlayerAnimatedInteractionPose(entry, Quaternion.identity,
                    entry + WarehouseDoorAmbushAssets.Vector(plan.hero_neutral_pelvis));
                var exitPose = new PlayerAnimatedInteractionPose(entry, Quaternion.identity,
                    entry + WarehouseDoorAmbushAssets.Vector(plan.hero_exit_pelvis));
                if (!scene.interaction.BeginExternalClock(this) || !scene.interaction.BeginPositioned(definition, entryPose,
                    entry + WarehouseDoorAmbushAssets.Vector(plan.hero_action_pelvis), exitPose,
                    WarehouseArenaPlan.TopHeight + 1f, route, route.Count))
                    throw new InvalidOperationException("The shared hero interaction could not prepare the door action.");
                sourcePose = new Pose(entry + WarehouseDoorAmbushAssets.Vector(plan.crane_root_offset), Quaternion.identity);
                if (!scene.Source.PrepareDoorAmbush(sourcePose, scene.suspension))
                    throw new InvalidOperationException("The paired crane actor could not prepare.");
                // It starts in the dark behind the hero. The cue owns its first visible movement.
                scene.Source.SetDoorAmbushVisible(false);
                scene.guides.SetActive(true);
                prepared = true;
            }

            public void AdvancePreparation(float seconds)
            {
                if (!prepared)
                {
                    if (!scene.owner.Hero.AdvanceDoorAmbushWeaponStow(this, seconds / ActionStretch))
                        throw new InvalidOperationException("The hero's weapon stow was interrupted.");
                    if (scene.owner.Hero.IsDoorAmbushWeaponStowed) BeginDoorApproach();
                    return;
                }
                if (!scene.interaction.AdvanceExternal(this, seconds / ActionStretch) || !scene.interaction.IsActive)
                    throw new InvalidOperationException("The hero's grounded approach was interrupted.");
                Vector3 current = scene.owner.Player.GameObject.transform.position;
                scene.sceneCamera.transform.position += current - approachPosition;
                approachPosition = current;
            }

            public void Sample(double elapsedSeconds)
            {
                if (!prepared) return;
                if (handoff) return;
                if (!IsReady) return;
                float next = Mathf.Min((float)ScriptDurationSeconds, (float)elapsedSeconds);
                float delta = Mathf.Max(0f, next - (float)sampled);
                if (!scene.interaction.AdvanceExternal(this, delta)) throw new InvalidOperationException("The shared door action lost its clock.");
                sampled = next;
                SetOpeningCamera(next);
                if (next >= .65f && next - delta < .65f) PlayCue(RetroSfxId.DoorCreak, scene.EntryRootPosition);
                if (next >= 7.7f && next - delta < 7.7f) PlayCue(RetroSfxId.RopeCreak, scene.owner.Hero.BodyWorldPosition);
                if (next >= (float)AttackCueSeconds)
                {
                    scene.Source.SetDoorAmbushVisible(true);
                    float attackSeconds = next - (float)AttackCueSeconds;
                    Pose pose = sourcePose;
                    pose.position += Vector3.back * (.30f * (1f - Mathf.SmoothStep(0f, 1f, attackSeconds)));
                    if (!scene.Source.SampleDoorAmbush(attack, attackSeconds, pose, 0f, scene.owner.Hero))
                        throw new InvalidOperationException("The crane's authored entry is obstructed.");
                }
                if (next < (float)ScriptDurationSeconds) return;
                if (terminalFrame < 0) { terminalFrame = Time.frameCount; return; }
                if (Time.frameCount <= terminalFrame) return;
                if (!scene.interaction.ReleaseForHandoff(this) ||
                    !scene.Source.BeginScriptedLegCapture(scene.owner.Hero, heroHold, hold, retreat, applyDamage: !preview))
                    throw new InvalidOperationException("The leg capture could not accept the paired terminal pose.");
                if (!scene.mechanisms.BeginDoorAmbushOverdrive(this))
                    throw new InvalidOperationException("The ceiling fan could not restart with the hook strike.");
                fanOverdrive = true;
                handoff = true;
                PresentCamera();
            }

            private void PlayCue(RetroSfxId cue, Vector3 position)
            {
                object previous = RetroAudio.SetPresentationCueOwner(this);
                try { RetroAudio.PlayAt(cue, position); }
                finally { RetroAudio.SetPresentationCueOwner(previous); }
            }

            private void SetOpeningCamera(float seconds)
            {
                Vector3 entry = scene.EntryRootPosition;
                float legShot = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(3.3f, 6.5f, seconds));
                Vector3 position = entry + Vector3.Lerp(new Vector3(.06f, 1.18f, .55f),
                    new Vector3(.12f, .34f, .55f), legShot);
                Vector3 target = entry + Vector3.Lerp(new Vector3(0f, 1.35f, -.05f),
                    new Vector3(.1f, .35f, -.03f), legShot);
                float attempt = Mathf.SmoothStep(0f, 1f, seconds / 3f);
                float attack = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(3f, 3.18f, seconds));
                float capture = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(3.18f, 8f, seconds));
                float roll = -12f * attack + 8f * capture;
                Quaternion rotation = Quaternion.LookRotation(target - position) * Quaternion.AngleAxis(roll, Vector3.forward);
                scene.sceneCamera.transform.SetPositionAndRotation(position, rotation);
                scene.sceneCamera.fieldOfView = Mathf.Lerp(79f, 75f, attempt) + 10f * attack - 7f * capture;
            }

            internal void PresentCamera()
            {
                if (!handoff || committed) return;
                CombatActor actor = scene.owner.Hero;
                Vector3 centre = actor.IsRagdollActive
                    ? Vector3.Lerp(actor.Ragdoll.PhysicsController.PelvisBody.position,
                        actor.Ragdoll.PhysicsController.ChestBody.position, .5f)
                    : actor.transform.position + Vector3.up * 1.05f;
                float raised = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(1.5f, 5.5f, centre.y));
                Vector3 position = centre + new Vector3(.06f, Mathf.Lerp(-.27f, -.1f, raised),
                    Mathf.Lerp(.55f, 2f, raised));
                Vector3 target = centre + Vector3.up * Mathf.Lerp(.2f, 0f, raised);
                float inversion = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(cameraInversionSeconds / 2f));
                Quaternion rotation = Quaternion.LookRotation(target - position) *
                    Quaternion.AngleAxis(Mathf.Lerp(-4f, 180f, inversion), Vector3.forward);
                float lens = Mathf.Lerp(78f, 70f, raised);
                scene.owner.CameraFollow.SetFixedPose(position, rotation, lens);
                scene.sceneCamera.transform.SetPositionAndRotation(position, rotation);
                scene.sceneCamera.fieldOfView = lens;
            }

            public void AdvanceInteractive(float seconds)
            {
                interactiveSeconds += seconds;
                scene.Source.AdvanceDoorAmbush(seconds);
                if (scene.Source.DoorAmbushHoistComplete && scene.Source.DoorAmbushVictimInverted)
                    cameraInversionSeconds += seconds;
            }

            public void CompleteIntoGameplay()
            {
                if (committed) return;
                if (!IsComplete || !scene.owner.Hero.Ragdoll.PhysicsController.TryGetCombatBone(
                    Player3DAnatomicalPart.Torso, out Transform chest))
                    throw new InvalidOperationException("The hanging hero is not ready for free look.");
                PresentCamera();
                Pose finalCamera = new Pose(scene.sceneCamera.transform.position, scene.sceneCamera.transform.rotation);
                float finalLens = scene.sceneCamera.fieldOfView;
                scene.owner.CameraFollow.ClearFixedPose();
                scene.sceneCamera.transform.SetPositionAndRotation(finalCamera.position, finalCamera.rotation);
                scene.sceneCamera.fieldOfView = finalLens;
                if (!scene.owner.CameraFollow.SetFreeAim(this, chest, preserveCurrentPose: true, followPhysicalBody: true) ||
                    !scene.Source.CommitDoorAmbushHangingGameplay())
                    throw new InvalidOperationException("The hanging hero could not accept gameplay camera ownership.");
                committed = true;
            }

            public void SetPaused(bool paused)
            {
                scene.interaction.SetExternalPaused(this, paused);
                RetroAudioService.Instance?.SetPresentationPaused(this, paused);
            }

            public void Stop()
            {
                if (stopped) return;
                stopped = true;
                if (!claimed) return;
                ReleaseLighting();
                if (fanOverdrive)
                    scene.mechanisms.EndDoorAmbushOverdrive(this, keepRunning: committed);
                if (committed)
                {
                    scene.interaction.ReleaseExternalClock(this);
                    RetroAudioService.Instance?.ReleasePresentation(this);
                    scene.hangingPlayback = this;
                    if (scene.playback == this) scene.playback = null;
                    return;
                }
                ReleaseHangingGameplay();
            }

            internal void ReleaseHangingGameplay(bool prepareAgain = false)
            {
                ReleaseLighting();
                scene.owner.CameraFollow?.ClearFreeAim(this);
                scene.Source.EndDoorAmbush();
                if (scene.interaction.IsActive) scene.interaction.CancelActiveInteraction();
                scene.interaction.ReleaseExternalClock(this);
                RetroAudioService.Instance?.ReleasePresentation(this);
                scene.guides.SetActive(false);
                if (preview) scene.owner.Hero.RestoreDoorAmbushPreview(previousHero.position, previousHero.rotation,
                    previousHealth, previousStamina, previousFallCount, previousFallDamage);
                else if (prepareAgain) scene.owner.Hero.RestoreDoorAmbushPreview(scene.EntryRootPosition,
                    Quaternion.identity, scene.owner.Hero.State.Health, scene.owner.Hero.State.Stamina,
                    scene.owner.Hero.RestraintFallDamageCount, scene.owner.Hero.LastRestraintFallDamage);
                if (preview) scene.owner.Hero.RestoreDoorAmbushWeaponStow(this);
                else scene.owner.Hero.CancelDoorAmbushWeaponStow(this);
                scene.owner.Hero.RestoreStandingInspectionFocus(previousFocus);
                if (scene.owner.CameraFollow != null)
                {
                    if (previousFixed) scene.owner.CameraFollow.SetFixedPose(previousFixedPose.position, previousFixedPose.rotation, previousFixedLens);
                    else scene.owner.CameraFollow.ClearFixedPose();
                }
                // The core restores the preview camera snapshot; gameplay follows the hero's real exit.
                if (preview && scene.sceneCamera != null)
                {
                    scene.sceneCamera.transform.SetPositionAndRotation(previousCamera.position, previousCamera.rotation);
                    scene.sceneCamera.fieldOfView = previousFieldOfView;
                }
                if (scene.playback == this) scene.playback = null;
            }

            private void ReleaseLighting()
            {
                if (!lit) return;
                scene.valveLamp?.EndDoorAmbushLighting(this);
                lit = false;
            }
        }
    }
}

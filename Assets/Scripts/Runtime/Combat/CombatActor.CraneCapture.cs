using UnityEngine;

namespace BarPromenade
{
    public sealed partial class CombatActor
    {
        public const float CraneHookHoldSeconds = 5f;
        public const float CraneWatchSeconds = 10f;
        public const float CraneFatalSpasmSeconds = 3f;
        private CombatActor craneVictim;
        private Quaternion craneVictimHookRotation;
        private Vector3 craneVictimPenetration;
        private Vector3 craneCaptureStart, craneCaptureHaulDirection;
        private bool craneRetreatRequested, craneRegenerating;

        public bool HasCraneVictim => craneVictim != null;
        public bool IsCraneRegenerating => IsCrane && craneRegenerating;
        public Vector3 CraneHomePosition { get; private set; }
        public Vector3 CraneHookPullOrigin => DoorAmbushPrepared ? doorAmbushPullOrigin : CraneHookCableExit;
        public float CraneCaptureElapsed => CranePhase == CombatCranePhase.Capturing ? craneClock : 0f;
        public float CraneWatchElapsed => CranePhase is CombatCranePhase.Watching or CombatCranePhase.Regenerating ? craneClock : 0f;

        // Called only after the common contact resolver accepts this precise
        // anatomical contact. A guarded or ignored hit cannot embed the hook.
        internal void ConfirmCraneHookContact(CombatActor target, int sequence, MeleeHitResult result,
            Player3DAnatomicalPart part, Vector3 localPoint, Vector3 worldPoint)
        {
            if (!CraneAttached || CranePhase != CombatCranePhase.Striking || sequence != State.AttackSequence) return;
            craneContactValid = collectSweep = false;
            craneStrikeStopped = true;
            if (result is not (MeleeHitResult.Hit or MeleeHitResult.GuardBroken) || target == null || !target.IsHero ||
                target.State.IsDefeated || !target.TryBeginCraneRestraint(this, part, localPoint, worldPoint))
            {
                BeginCraneHookReturn();
                SetCranePhase(CombatCranePhase.Recovering);
                return;
            }
            craneVictim = target;
            if (!target.TryGetCraneRestraintAnchor(this, out Pose anchor))
            {
                ReleaseCraneVictim(false);
                BeginCraneHookReturn();
                SetCranePhase(CombatCranePhase.Recovering);
                return;
            }
            craneVictimHookRotation = Quaternion.Inverse(anchor.rotation) * craneHookRotation;
            craneVictimPenetration = Quaternion.Inverse(anchor.rotation) * (CraneHookAimDirection * .035f);
            craneCaptureStart = craneCommand;
            craneCaptureHaulDirection = Vector3.ProjectOnPlane(CraneHookCableExit - anchor.position, Vector3.up).normalized;
            craneHookVelocity = Vector3.zero;
            craneRetreatRequested = true;
            State.CancelAction();
            CraneHookPhase = CombatCraneHookPhase.Embedded;
            SetCranePhase(CombatCranePhase.Capturing);
            UpdateCraneEmbeddedHook();
            // PublishImpact can already have frozen ordinary presentation for
            // hit-stop. Commit this new contact's attachment immediately, so
            // that freeze holds the embedded hook rather than its flight pose.
            PresentCraneHook();
            JournalEvent("crane_hook_embedded", target.JournalActorId, sequence);
        }

        private bool UpdateCraneEmbeddedHook()
        {
            if (craneVictim == null || !craneVictim.TryGetCraneRestraintAnchor(this, out Pose anchor)) return false;
            // The scene's freely swivelling eye stays above the puncture while
            // the real leg rotates around it; ordinary thrown contacts retain
            // their original bone-relative hook orientation.
            craneHookRotation = DoorAmbushLegCapture ? doorReleasedHookRotation : anchor.rotation * craneVictimHookRotation;
            craneHookEye = anchor.position + anchor.rotation * craneVictimPenetration - craneHookRotation * craneHookImpactOffset;
            Vector3 aim = anchor.position - CraneHookCableExit;
            if (aim.sqrMagnitude > .001f) craneDirection = aim.normalized;
            return true;
        }

        private void AdvanceCraneCapture(float seconds)
        {
            if (craneVictim == null || craneVictim.State.IsDefeated || !craneVictim.isActiveAndEnabled ||
                !UpdateCraneEmbeddedHook() || !CranePathVisible(CraneHookCableExit, craneHookEye) ||
                (CraneHookCableExit - craneHookEye).sqrMagnitude > (CraneHookReach + .15f) * (CraneHookReach + .15f) ||
                craneClock + .000001f >= CraneHookHoldSeconds)
            {
                ReleaseCraneVictim(false);
                BeginCraneReturnHome();
                return;
            }
            // Move the real suspended source: the victim follows only through
            // tension at the embedded wound. Ceiling, anatomy, cable and world
            // clearance can stop either axis without moving the hero through it.
            float lift = Mathf.SmoothStep(0f, 1f, craneClock / 1.2f) * 1.4f;
            Vector3 vertical = craneCommand;
            vertical.y = Mathf.MoveTowards(vertical.y, craneCaptureStart.y + lift, seconds * 2.4f);
            bool moved = TryMoveCraneCaptureSource(vertical, 2);
            float haul = Mathf.SmoothStep(0f, 1f, (craneClock - 4f) / 1f) * 1.6f;
            Vector3 lateral = craneCaptureStart + craneCaptureHaulDirection * haul;
            lateral.x = Mathf.Clamp(lateral.x, -CraneWellHalfWidth, CraneWellHalfWidth);
            lateral.z = Mathf.Clamp(lateral.z, -CraneWellHalfWidth, CraneWellHalfWidth);
            lateral.y = craneCommand.y;
            lateral = Vector3.MoveTowards(craneCommand, lateral, seconds * 2.8f);
            moved |= TryMoveCraneCaptureSource(lateral, 1);
            if (!moved) StopCraneMotors();
        }

        private bool TryMoveCraneCaptureSource(Vector3 candidate, int axis)
        {
            Vector3 delta = candidate - craneCommand;
            if (delta.sqrMagnitude < .00000001f ||
                (CraneHookCableExit + delta - craneHookEye).sqrMagnitude > CraneHookReach * CraneHookReach ||
                !MoveCraneRoot(candidate + CraneLoadOffset, transform.rotation, candidate)) return false;
            craneCommand = candidate;
            craneMoveSpeed = axis == 2 ? 2.4f : 2.8f;
            UpdateCraneMotorSound(axis, delta.y);
            return true;
        }

        internal void ReleaseCraneVictimEarly()
        {
            if (DoorAmbushPrepared) { ReleaseDoorAmbushCapture(); return; }
            if (!CraneAttached || craneVictim == null) return;
            ReleaseCraneVictim(false);
            BeginCraneReturnHome();
        }

        private void ReleaseCraneVictim(bool returnHome)
        {
            CombatActor released = craneVictim;
            // Clear the owner before the victim releases its input/physics;
            // callbacks from that handoff cannot release it a second time.
            craneVictim = null;
            if (released != null) released.EndCraneRestraint(this);
            if (CraneHookPhase == CombatCraneHookPhase.Embedded) BeginCraneHookReturn();
            if (returnHome && craneRetreatRequested && CraneAttached) BeginCraneReturnHome();
        }

        private void BeginCraneReturnHome()
        {
            if (!CraneAttached) return;
            ReleaseCraneVictim(false);
            craneRetreatRequested = true;
            craneContactValid = collectSweep = sweepValid = false;
            craneStrikeStopped = true;
            craneHurtHold = craneHurtClock = 0f;
            State.CancelAction();
            if (CraneHookPhase is CombatCraneHookPhase.Outbound or CombatCraneHookPhase.Holding)
                BeginCraneHookReturn();
            craneDestinationFloor = 2;
            craneChangingFloors = true;
            SetCranePhase(CombatCranePhase.ReturningHome);
            BeginCraneTravel(CraneHomePosition);
        }

        private void AdvanceCraneHomeArrival(float seconds)
        {
            AdvanceCraneHook(seconds);
            // Reaching the travel command is insufficient: the real suspended
            // body and retracted hook must finish arriving before either timer.
            if (craneTravelling || CraneHookPhase != CombatCraneHookPhase.Stowed ||
                (craneCommand - CraneHomePosition).sqrMagnitude > .000001f ||
                (transform.position - CraneHomePosition).sqrMagnitude > .04f * .04f ||
                craneSwayVelocity.sqrMagnitude > .1f * .1f) return;
            craneRetreatRequested = false;
            craneFloor = craneDestinationFloor = 2;
            craneStationPosition = CraneHomePosition;
            StopCraneMotors();
            SetCranePhase(craneRegenerating ? CombatCranePhase.Regenerating : CombatCranePhase.Watching);
        }

        private void AdvanceCraneWatch()
        {
            if (contactTarget != null && !contactTarget.State.IsDefeated)
            {
                Vector3 aim = CraneTargetPoint(contactTarget) - CraneHookCableExit;
                if (aim.sqrMagnitude > .001f) craneDirection = aim.normalized;
            }
            if (craneClock + .000001f < CraneWatchSeconds) return;
            if (craneRegenerating)
            {
                RestoreCraneAfterRegeneration();
                craneRegenerating = false;
            }
            State.CancelAction();
            SetCranePhase(CombatCranePhase.Waiting);
        }

        internal bool TryBeginCraneRegeneration()
        {
            if (!CraneAttached) return false;
            if (craneRegenerating) return true;
            ReleaseCraneVictim(false);
            craneRegenerating = craneRetreatRequested = true;
            // Remove the transient terminal rules state before the encounter
            // can count a kill, retire a corpse or stop this living actor.
            State.BeginRegeneration();
            roundEnded = false;
            craneTravelling = craneChangingFloors = craneStarting = false;
            craneWaypointCount = craneWaypointIndex = 0;
            CraneMotionAxis = 0;
            craneMoveSpeed = craneBrakeClock = craneMotorClock = 0f;
            craneHurtHold = craneHurtClock = 0f;
            craneContactValid = collectSweep = sweepValid = collectShove = collectKick = false;
            reaction = null;
            BeginCraneFatalSpasm();
            return true;
        }

        private void ResetCraneAttackLifecycle()
        {
            ReleaseCraneVictim(false);
            craneRetreatRequested = craneRegenerating = false;
        }
    }
}

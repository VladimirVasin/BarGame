using UnityEngine;

namespace BarPromenade
{
    public sealed partial class CombatTestRoot
    {
        private object doorCombatOwner;
        private CombatActor doorCombatSource;
        private readonly CombatActor[] doorCombatParticipants = new CombatActor[2];
        internal bool DoorAmbushCombatActive => Hero != null && Hero.DoorAmbushCombatActive;

        internal bool PrepareDoorAmbushCombat(object owner, CombatActor source)
        {
            if (owner == null || source == null || !Hero.PrepareDoorAmbushFirearm(owner)) return false;
            doorCombatOwner = owner;
            doorCombatSource = source;
            doorCombatParticipants[0] = Hero; doorCombatParticipants[1] = source;
            if (pistolProjectiles == null) pistolProjectiles = new CombatProjectilePool(transform);
            if (pistolCasings == null) pistolCasings = new CombatCasingPool(transform);
            return true;
        }

        internal bool BeginDoorAmbushCombat(object owner)
        {
            if (!ReferenceEquals(owner, doorCombatOwner) || !Hero.BeginDoorAmbushFirearm(owner)) return false;
            ResetCombatAttackInput();
            requirePistolAimRelease = GameInput.IsHeld(GameInputAction.MeleeBlock, GameInputContext.PauseMenu);
            return true;
        }

        internal void AdvanceDoorAmbushCombat(float seconds)
        {
            if (!DoorAmbushCombatActive || !isActiveAndEnabled || !pistolApplicationFocused ||
                !GameInput.MovementFocused || GameTimeScaleRuntime.IsPaused || PauseMenuController.IsAnyPaused ||
                SceneTransitionService.IsTransitioning || !GameInput.CanRead(GameInputContext.Gameplay))
            {
                if (Hero != null) Hero.SuspendPistolInput();
                return;
            }
            CameraFollow.PrepareFreeAimFrame(doorCombatOwner);
            bool held = ReadCombatPointerAction(GameInputAction.MeleeAttack, true);
            if (!held) requireAttackRelease = false;
            bool aimHeld = ReadCombatPointerAction(GameInputAction.MeleeBlock, true);
            if (!aimHeld) requirePistolAimRelease = false;
            Hero.SetPistolAim(aimHeld && !requirePistolAimRelease,
                ResolveFreePistolAim(CameraFollow.Camera.ViewportPointToRay(new Vector3(.5f, .5f, 0f))));
            if (GameInput.WasPressed(GameInputAction.CombatReload, GameInputContext.Gameplay)) Hero.TryReloadPistol();
            if (ReadCombatPointerAction(GameInputAction.MeleeAttack, false) && !requireAttackRelease) Hero.RequestPistolShot();
            AdvanceDoorAmbushCombatSimulation(seconds);
        }

        internal void AdvanceDoorAmbushCombatSimulation(float seconds)
        {
            if (!DoorAmbushCombatActive || seconds <= 0f) return;
            float remaining = Mathf.Min(seconds, MaximumFrameSubsteps * SimulationStep);
            while (remaining > .000001f)
            {
                float step = Mathf.Min(remaining, SimulationStep);
                Hero.AdvanceDoorAmbushFirearm(step);
                Hero.Present();
                doorCombatSource.Present();
                Hero.CaptureContactPose();
                doorCombatSource.CaptureContactPose();
                pistolProjectiles.Advance(step, doorCombatParticipants);
                pistolCasings.Tick(step);
                AdvancePistolCrosshair(step);
                if (Hero.CommitPistolShot(pistolProjectiles))
                {
                    pistolCasings.BeginShot(Hero);
                    PulsePistolCrosshair();
                }
                pistolProjectiles.ApplyContacts();
                pistolProjectiles.SurfaceEffects.Tick(step);
                remaining -= step;
            }
            pistolProjectiles.AdvancePresentation(seconds);
        }

        internal void EndDoorAmbushCombat(object owner)
        {
            if (owner == null || !ReferenceEquals(owner, doorCombatOwner)) return;
            if (Hero != null) Hero.EndDoorAmbushFirearm(owner);
            pistolProjectiles?.ResetRound();
            pistolCasings?.ResetRound();
            doorCombatOwner = null; doorCombatSource = null;
            doorCombatParticipants[0] = doorCombatParticipants[1] = null;
            ResetPistolCrosshair();
        }
    }
}

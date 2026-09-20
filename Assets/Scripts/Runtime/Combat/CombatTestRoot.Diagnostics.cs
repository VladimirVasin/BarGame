using UnityEngine;
using UnityEngine.InputSystem;

namespace BarPromenade
{
    public sealed partial class CombatTestRoot
    {
        private int movementSamples, keyboardSamples, requestedSamples, requestedKeys;
        private int gatedSamples, motorOffSamples, inputOffSamples, capsuleOffSamples, zeroScaleSamples;
        private bool sampledYaw, requestedTurnChanged, movementSummaryWritten;
        private bool lastMovementAllowed, lastMotorEnabled, lastInputEnabled, lastCapsuleEnabled;
        private float sampledRootYaw, maximumRequestedSpeed, minimumRequestedScale = 1f, lastRequestedScale;
        private MeleePhase lastRequestedPhase;

        private void LateUpdate()
        {
            if (GameLog.Profile == GameLogProfile.Off || !IsInitialized || Hero == null || Player.Motor == null) return;
            movementSamples++;
            Keyboard keyboard = Keyboard.current;
            if (keyboard != null) keyboardSamples++;
            int keys = keyboard == null ? 0 : (keyboard.wKey.isPressed ? 1 : 0) |
                (keyboard.sKey.isPressed ? 2 : 0) | (keyboard.aKey.isPressed ? 4 : 0) | (keyboard.dKey.isPressed ? 8 : 0);
            float yaw = Hero.transform.eulerAngles.y;
            bool turned = sampledYaw && Mathf.Abs(Mathf.DeltaAngle(sampledRootYaw, yaw)) > .01f;
            sampledRootYaw = yaw; sampledYaw = true;
            if (keys == 0) return;
            requestedSamples++; requestedKeys |= keys;
            requestedTurnChanged |= (keys & 12) != 0 && turned;
            lastMovementAllowed = GameInput.CanRead(GameInputContext.Movement);
            lastMotorEnabled = Player.Motor.enabled;
            lastInputEnabled = Player.Motor.InputEnabled;
            lastCapsuleEnabled = Hero.Body != null && Hero.Body.enabled;
            lastRequestedPhase = Hero.State.Phase;
            lastRequestedScale = Hero.MovementScale;
            if (!lastMovementAllowed) gatedSamples++;
            if (!lastMotorEnabled) motorOffSamples++;
            if (!lastInputEnabled) inputOffSamples++;
            if (!lastCapsuleEnabled) capsuleOffSamples++;
            if (lastRequestedScale <= 0f) zeroScaleSamples++;
            minimumRequestedScale = Mathf.Min(minimumRequestedScale, lastRequestedScale);
            maximumRequestedSpeed = Mathf.Max(maximumRequestedSpeed, Player.Motor.PlanarVelocity.magnitude);
        }

        private void OnApplicationQuit() => WriteMovementSummary();
        private void OnDestroy()
        {
            if (CameraFollow != null) CameraFollow.ClearTargetLock(this);
            if (Player.Motor != null) Player.Motor.ClearMovementTarget(this);
            WriteMovementSummary();
        }

        private void WriteMovementSummary()
        {
            // Scene destruction may already have removed the actor. Read cached values only.
            if (movementSummaryWritten || GameLog.Profile == GameLogProfile.Off) return;
            movementSummaryWritten = true;
            GameLog.Info("combat", "movement_summary",
                GameLog.Field("source_scene", SceneIds.CombatTest),
                GameLog.Field("sampled_frames", movementSamples),
                GameLog.Field("keyboard_frames", keyboardSamples),
                GameLog.Field("requested_frames", requestedSamples),
                GameLog.Field("w_seen", (requestedKeys & 1) != 0),
                GameLog.Field("s_seen", (requestedKeys & 2) != 0),
                GameLog.Field("a_seen", (requestedKeys & 4) != 0),
                GameLog.Field("d_seen", (requestedKeys & 8) != 0),
                GameLog.Field("gated_frames", gatedSamples),
                GameLog.Field("motor_disabled_frames", motorOffSamples),
                GameLog.Field("input_disabled_frames", inputOffSamples),
                GameLog.Field("capsule_disabled_frames", capsuleOffSamples),
                GameLog.Field("zero_movement_scale_frames", zeroScaleSamples),
                GameLog.Field("last_movement_allowed", lastMovementAllowed),
                GameLog.Field("last_motor_enabled", lastMotorEnabled),
                GameLog.Field("last_input_enabled", lastInputEnabled),
                GameLog.Field("last_capsule_enabled", lastCapsuleEnabled),
                GameLog.Field("last_phase", lastRequestedPhase.ToString()),
                GameLog.Field("last_movement_scale", lastRequestedScale),
                GameLog.Field("minimum_movement_scale", minimumRequestedScale),
                GameLog.Field("maximum_requested_speed", maximumRequestedSpeed),
                GameLog.Field("requested_turn_changed", requestedTurnChanged));
        }
    }
}

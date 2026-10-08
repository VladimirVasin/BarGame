using System;
using UnityEngine;
using UnityEngine.InputSystem;

namespace BarPromenade
{
    public sealed partial class CombatTestRoot
    {
        private sealed class JournalIdentity
        {
            internal string Code = "unavailable", WorkspaceCommit = "unavailable", WorkspaceState = "unavailable";
            internal string CodeSource = "unavailable";
            internal string HeroAnimation = "unavailable", NpcAnimation = "unavailable";
        }

        private static JournalIdentity cachedJournalIdentity;
        private JournalIdentity journalIdentity;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetJournalIdentity() => cachedJournalIdentity = null;

        private static JournalIdentity CaptureJournalIdentity()
        {
            if (cachedJournalIdentity != null) return cachedJournalIdentity;
            var identity = new JournalIdentity();
            try
            {
                Guid runtime = typeof(CombatTestRoot).Module.ModuleVersionId;
                Guid rules = typeof(MeleeCombatant).Module.ModuleVersionId;
                if (runtime != Guid.Empty && rules != Guid.Empty)
                {
                    identity.Code = "modules:" + runtime.ToString("N") + ":" + rules.ToString("N");
                    identity.CodeSource = Application.isEditor ? "editor_compiled_modules" : "player_compiled_modules";
                }
            }
            catch { /* Some Player backends cannot expose managed module identities. */ }
#if UNITY_EDITOR
            identity.HeroAnimation = JournalEditorAnimationRevision("CombatActions");
            identity.NpcAnimation = JournalEditorAnimationRevision("CombatNpcActions");
            // One bounded, read-only local query per play session, never a frame or
            // round operation. Only commit/state survive; no output paths are logged.
            try
            {
                using var process = new System.Diagnostics.Process();
                process.StartInfo = new System.Diagnostics.ProcessStartInfo
                {
                    FileName = "git", Arguments = "status --porcelain=v2 --branch --untracked-files=normal",
                    WorkingDirectory = System.IO.Path.GetFullPath(System.IO.Path.Combine(Application.dataPath, "..")),
                    UseShellExecute = false, CreateNoWindow = true,
                    RedirectStandardOutput = true, RedirectStandardError = true
                };
                if (process.Start())
                {
                    var output = process.StandardOutput.ReadToEndAsync();
                    _ = process.StandardError.ReadToEndAsync();
                    if (!process.WaitForExit(1000)) process.Kill();
                    else if (process.ExitCode == 0)
                    {
                        bool dirty = false;
                        foreach (string rawLine in output.GetAwaiter().GetResult().Split('\n'))
                        {
                            string line = rawLine.TrimEnd('\r');
                            if (line.StartsWith("# branch.oid ", StringComparison.Ordinal))
                            {
                                string commit = line.Substring(13).Trim();
                                if ((commit.Length == 40 || commit.Length == 64) &&
                                    Array.TrueForAll(commit.ToCharArray(), Uri.IsHexDigit)) identity.WorkspaceCommit = commit;
                            }
                            else if (line.Length > 0 && line[0] != '#') dirty = true;
                        }
                        identity.WorkspaceState = dirty ? "dirty" : "clean";
                    }
                }
            }
            catch { /* Missing git or a timeout is unavailable, not a clean checkout. */ }
#else
            identity.WorkspaceState = "packaged";
            string build = Application.buildGUID;
            if (Guid.TryParse(build, out Guid buildId) && buildId != Guid.Empty)
            {
                identity.HeroAnimation = "build:" + build + ":CombatActions";
                identity.NpcAnimation = "build:" + build + ":CombatNpcActions";
                if (identity.Code == "unavailable")
                { identity.Code = "build:" + build; identity.CodeSource = "packaged_build_guid"; }
            }
#endif
            return cachedJournalIdentity = identity;
        }

#if UNITY_EDITOR
        private static string JournalEditorAnimationRevision(string bank)
        {
            try
            {
                string hash = UnityEditor.AssetDatabase.GetAssetDependencyHash(
                    "Assets/Resources/Combat/" + bank + ".fbx").ToString();
                return hash.Trim('0').Length > 0 ? "dependency:" + hash : "unavailable";
            }
            catch { return "unavailable"; }
        }
#endif

        private int movementSamples, keyboardSamples, requestedSamples, requestedKeys;
        private int gatedSamples, motorOffSamples, inputOffSamples, capsuleOffSamples, zeroScaleSamples;
        private bool sampledYaw, requestedTurnChanged, movementSummaryWritten;
        private bool lastMovementAllowed, lastMotorEnabled, lastInputEnabled, lastCapsuleEnabled;
        private float sampledRootYaw, maximumRequestedSpeed, minimumRequestedScale = 1f, lastRequestedScale;
        private MeleePhase lastRequestedPhase;

        private void LateUpdate()
        {
            if (IsInitialized && GameInput.CanRead(GameInputContext.Gameplay) && hitStopSubsteps == 0 && roundEndFreeze <= 0d)
                Projectiles?.AdvancePresentation(Time.deltaTime);
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
            lastMovementAllowed = GameInput.CanRead(GameInputContext.Movement) &&
                GameInput.MovementFocused && !GameInput.MovementAwaitingNeutral;
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

        private void OnApplicationQuit() { CloseDuelJournal("quit"); WriteMovementSummary(); }
        private void OnDestroy()
        {
            Projectiles?.Clear();
            Casings?.Clear();
            ReleaseFreePistolAim();
            CloseDuelJournal("unload");
            ReleaseDamageEffects();
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
                GameLog.Field("has_request_sample", requestedSamples > 0),
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

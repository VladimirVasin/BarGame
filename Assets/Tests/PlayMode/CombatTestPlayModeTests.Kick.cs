using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;
using static BarPromenade.Tests.PlayMode.CombatTuning;

namespace BarPromenade.Tests.PlayMode
{
    public sealed class CombatKickAssetsSetup : IPrebuildSetup
    {
        public void Setup()
        {
#if UNITY_EDITOR
            System.Type.GetType("BarPromenade.Editor.CityPedestrianAssetSetup, BarPromenade.Editor", true)
                .GetMethod("ValidateFaceAtlasesOrThrow", System.Type.EmptyTypes).Invoke(null, null);
            System.Type.GetType("BarPromenade.Editor.CombatTestAssetSetup, BarPromenade.Editor", true)
                .GetMethod("BuildOrThrow", System.Type.EmptyTypes).Invoke(null, null);
#endif
        }
    }

    [PrebuildSetup(typeof(CombatKickAssetsSetup))]
    public sealed class CombatKickPlayModeTests
    {
        private CombatTestRoot root;
        private float previousCaptureDelta;

        [UnitySetUp]
        public IEnumerator SetUp()
        {
            previousCaptureDelta = Time.captureDeltaTime;
            Time.captureDeltaTime = 1f / 60f;
            GameSessionState.BeginNewGame();
            yield return null;
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            if (root != null) root.AutomaticSimulation = false;
            RetroAudioService.Instance?.StopAll();
            yield return SceneManager.LoadSceneAsync(SceneIds.MainMenu);
            GameSessionState.BeginNewGame();
            Time.captureDeltaTime = previousCaptureDelta;
            root = null;
        }

        private void PlacePair(float distance)
        {
            root.SetSparring(false);
            Vector3 position = Vector3.up * PlayerFactory.GroundedRootOffset;
            root.Hero.ResetActor(position, Vector3.forward);
            root.Opponent.ResetActor(position + Vector3.forward * distance, Vector3.back);
            Physics.SyncTransforms();
        }

        [UnityTest]
        public IEnumerator Range_KickUsesSoleContactSupportInputAndOwnedRecovery()
        {
            var input = new InputTestFixture();
            GameObject wall = null;
            try
            {
                input.Setup();
                Keyboard keyboard = InputSystem.AddDevice<Keyboard>();
                Gamepad pad = InputSystem.AddDevice<Gamepad>();
                yield return SceneManager.LoadSceneAsync(SceneIds.CombatTest);
                root = Object.FindAnyObjectByType<CombatTestRoot>();
                root.AutomaticSimulation = false;
                PlacePair(.80f);
                root.Opponent.SetBlock(true);
                root.CameraFollow.Snap();
                yield return null;
                Vector3 left = FindKickBone("foot.L").position;
                root.AutomaticSimulation = true;
                input.Press(keyboard.qKey, queueEventOnly: true);
                yield return null;
                root.AutomaticSimulation = false;
                Assert.That(root.Hero.State.IsKicking, Is.True, "Q is consumed by the real gameplay Update.");
                Assert.That(root.Hero.State.Stamina, Is.EqualTo(S.MaxStamina - S.KickCost).Within(.001f));
                input.Release(keyboard.qKey, queueEventOnly: true);
                yield return null;
                Assert.That(root.Hero.TryKick(), Is.False, "The same kick cannot restart itself.");
                root.Tick(.25f);
                yield return null;
                Assert.That(Vector3.Distance(left, FindKickBone("foot.L").position), Is.LessThan(.025f),
                    "The loaded left foot stays in world contact while the right foot lifts.");
                CaptureKick("kick-prepare");
                float held = root.Hero.State.KickElapsed;
                using (GameTimeScaleRuntime.AcquirePause())
                {
                    root.Tick(.3f);
                    Assert.That(root.Hero.State.KickElapsed, Is.EqualTo(held));
                    Assert.That(root.Hero.TryKick(), Is.False);
                }
                for (int i = 0; i < 24 && root.Opponent.State.Health == S.MaxHealth; i++)
                { root.Tick(CombatTestRoot.SimulationStep); yield return null; }
                Assert.That(root.Opponent.State.Health, Is.EqualTo(S.MaxHealth - S.KickDamage),
                    "The actual boot crosses the posed anatomy beneath the raised face guard.");
                Assert.That(root.Opponent.LastImpact.Kind, Is.EqualTo(CombatImpactKind.Kick));
                Assert.That(root.BloodEffects.WoundCountFor(root.Opponent), Is.Zero);
                Assert.That(root.Opponent.LastImpact.Impulse.magnitude, Is.EqualTo(CombatActor.KickImpulse).Within(.001f));
                CaptureKick("kick-contact");
                for (int i = 0; i < 60 && (root.Hero.State.KickRecoveryRemaining > .15f ||
                    root.Hero.State.KickElapsed < root.Hero.State.KickActiveEnd); i++)
                    root.Tick(CombatTestRoot.SimulationStep);
                Assert.That(root.Opponent.ReceivedImpactCount, Is.EqualTo(1), "One foot sweep grants one impact.");
                Assert.That(root.Hero.RequestCharge(), Is.True);
                root.Hero.ReleaseCharge();
                Assert.That(root.Hero.State.IsKicking, Is.True, "A queued tap preserves the whole kick return.");
                for (int i = 0; i < 40 && root.Hero.State.IsKicking; i++) root.Tick(CombatTestRoot.SimulationStep);
                for (int i = 0; i < 12 && !root.Hero.State.IsAttacking; i++) root.Tick(CombatTestRoot.SimulationStep);
                Assert.That(root.Hero.State.Phase, Is.EqualTo(MeleePhase.Windup));
                Assert.That(root.Hero.State.IsChained, Is.False);
                CaptureKick("kick-return-attack");

                PlacePair(2.4f);
                yield return null;
                Assert.That(root.Hero.TryKick(), Is.True);
                root.Tick(.76f);
                Assert.That(root.Hero.State.KickOutcome, Is.EqualTo(MeleeAttackOutcome.Miss));
                Assert.That(root.Hero.TryStep(Vector2.down), Is.True);
                Assert.That(root.Hero.State.IsKicking, Is.True);
                for (int i = 0; i < 40 && root.Hero.State.Phase != MeleePhase.Step; i++) root.Tick(CombatTestRoot.SimulationStep);
                Assert.That(root.Hero.State.Phase, Is.EqualTo(MeleePhase.Step), "The queued step starts after supported landing.");

                PlacePair(1f);
                wall = new GameObject("Kick test wall");
                wall.transform.position = new Vector3(0f, 1f, .5f);
                wall.AddComponent<BoxCollider>().size = new Vector3(3f, 2f, .05f);
                Physics.SyncTransforms();
                yield return null;
                Assert.That(root.Hero.TryKick(), Is.True);
                root.Tick(.45f);
                yield return null;
                Assert.That(root.Opponent.State.Health, Is.EqualTo(S.MaxHealth));
                Assert.That(root.Hero.State.KickOutcome, Is.EqualTo(MeleeAttackOutcome.Obstacle));
                Assert.That(root.Hero.KickBootPosition.z, Is.LessThan(.51f));
                CaptureKick("kick-wall-return");
                Object.Destroy(wall); wall = null;
                root.ResetRound();
                Assert.That(root.Hero.State.IsKicking, Is.False);
                Assert.That(root.Hero.State.BufferedAction, Is.EqualTo(MeleeBufferedAction.None));

                PlacePair(2.4f);
                root.AutomaticSimulation = true;
                input.Press(pad.buttonEast, queueEventOnly: true);
                yield return null;
                root.AutomaticSimulation = false;
                Assert.That(root.Hero.State.IsKicking, Is.True, "Gamepad east has the same scoped kick action.");
                root.Hero.State.ReceiveKick();
                root.Tick(.01f);
                Assert.That(root.Hero.State.IsKicking, Is.False);
                Assert.That(root.Hero.State.HasBufferedAttack, Is.False);
                root.Hero.enabled = false;
                Assert.That(root.Hero.State.IsKicking, Is.False);
                root.Hero.enabled = true;
                foreach (MeleeSwing side in new[] { MeleeSwing.Forehand, MeleeSwing.Backhand })
                {
                    PlacePair(3f);
                    root.Hero.State.ObserveLateralCue(side == MeleeSwing.Forehand ? -1 : 1);
                    Assert.That(root.Hero.TryAttack(), Is.True);
                    root.Tick(S.WindupSeconds + .01f);
                    Assert.That(root.Hero.State.RecordAttackOutcome(MeleeHitResult.Parried, root.Hero.State.AttackSequence), Is.True);
                    root.Hero.ShowParried(root.Hero.Weapon.transform.position, Vector3.back);
                    root.Hero.Present();
                    root.Tick(.5f);
                    Assert.That(root.Hero.State.Phase, Is.EqualTo(MeleePhase.Recovery));
                    var presentation = (Player3DCharacterPresentation)root.Player.Visual;
                    Assert.That(presentation.ActiveClipName, Is.EqualTo(CombatAssetProvider.SwingClips(side).Recoil),
                        "A finished recoil holds its Ready endpoint through the remaining forced recovery.");
                    CaptureKick("recoil-" + side.ToString().ToLowerInvariant());
                }
            }
            finally
            {
                if (wall != null) Object.Destroy(wall);
                if (root != null) root.AutomaticSimulation = false;
                input.TearDown();
            }
        }

        [UnityTest]
        public IEnumerator Range_KickWaitsForWalkingSupportAndCancelsStaleRequests()
        {
            var input = new InputTestFixture();
            try
            {
                input.Setup();
                Keyboard keyboard = InputSystem.AddDevice<Keyboard>();
                Mouse mouse = InputSystem.AddDevice<Mouse>();
                yield return SceneManager.LoadSceneAsync(SceneIds.CombatTest);
                root = Object.FindAnyObjectByType<CombatTestRoot>();
                root.AutomaticSimulation = false;
                PlacePair(3f);
                yield return null;
                WalkToLeftFootLift();
                root.AutomaticSimulation = true;
                input.Press(keyboard.qKey, queueEventOnly: true);
                input.Press(mouse.leftButton, queueEventOnly: true);
                yield return null;
                root.AutomaticSimulation = false;
                Assert.That(root.Hero.HasPendingKick, Is.True, "A real Q press waits for the walking left foot. " + KickWaitDiagnostics());
                Assert.That(root.Hero.State.IsKicking || root.Hero.State.IsCharging, Is.False);
                Assert.That(root.Hero.State.Stamina, Is.EqualTo(S.MaxStamina), "Waiting spends no kick effort.");
                Assert.That(root.Hero.Footwork.LastKickSupportFailure, Is.EqualTo(CombatFootwork.KickSupportFailure.FootGap));
                Assert.That(root.Hero.Footwork.LastKickSupportGap, Is.GreaterThan(.08f));
                input.Release(keyboard.qKey, queueEventOnly: true);
                yield return null;
                float remaining = root.Hero.PendingKickSeconds;
                using (GameTimeScaleRuntime.AcquirePause())
                {
                    root.Tick(.5f);
                    Assert.That(root.Hero.PendingKickSeconds, Is.EqualTo(remaining), "The support wait uses the duel clock.");
                }
                remaining = root.Hero.PendingKickSeconds;
                Assert.That(root.Hero.TryKick(), Is.True, "The existing pending request still owns Q. " + KickWaitDiagnostics());
                Assert.That(root.Hero.PendingKickSeconds, Is.EqualTo(remaining), "Another press cannot extend the existing wait.");
                for (int tick = 0; tick < 24 && root.Hero.HasPendingKick; tick++)
                {
                    root.Tick(CombatTestRoot.SimulationStep);
                    if (root.Hero.HasPendingKick)
                        Assert.That(root.Hero.State.Stamina, Is.EqualTo(S.MaxStamina));
                }
                Assert.That(root.Hero.State.IsKicking, Is.True, "The constrained walking settle must actually plant the foot. " + KickWaitDiagnostics());
                Assert.That(root.Hero.Footwork.LastKickSupportGap, Is.LessThanOrEqualTo(.08f));
                Assert.That(root.Hero.Footwork.JournalLeftSupport, Is.True);
                Assert.That(root.Hero.Footwork.JournalRightSupport, Is.False);
                Assert.That(root.Hero.State.Stamina, Is.EqualTo(S.MaxStamina - S.KickCost).Within(.001f));
                Assert.That(root.Hero.State.BufferedAction, Is.EqualTo(MeleeBufferedAction.None),
                    "Q owns the request even when attack was pressed in the same frame.");
                input.Release(mouse.leftButton, queueEventOnly: true);
                yield return null;

                PlacePair(3f);
                yield return null;
                WalkToLeftFootLift();
                Assert.That(root.Hero.TryKick(), Is.True, "Walking Q is accepted before reset. " + KickWaitDiagnostics());
                Assert.That(root.Hero.HasPendingKick, Is.True, KickWaitDiagnostics());
                root.ResetRound();
                Assert.That(root.Hero.HasPendingKick, Is.False);
                root.Tick(.25f);
                Assert.That(root.Hero.State.IsKicking, Is.False, "Reset cannot retain an old Q request.");

                PlacePair(3f);
                yield return null;
                WalkToLeftFootLift();
                Assert.That(root.Hero.TryKick(), Is.True, "Walking Q is accepted before interruption. " + KickWaitDiagnostics());
                root.Hero.State.ReceiveKick();
                root.Tick(CombatTestRoot.SimulationStep);
                Assert.That(root.Hero.HasPendingKick, Is.False, "An interruption cannot postpone the kick until recovery.");
                root.Tick(.5f);
                Assert.That(root.Hero.State.IsKicking, Is.False);

                PlacePair(3f);
                yield return null;
                WalkToLeftFootLift();
                Assert.That(root.Hero.TryAttack(), Is.True);
                Assert.That(root.Hero.TryKick(), Is.False);
                Assert.That(root.Hero.HasPendingKick, Is.False, "A released weapon swing cannot queue Q.");

                PlacePair(3f);
                yield return null;
                WalkToLeftFootLift();
                Assert.That(root.Hero.TryKick(), Is.True, "Walking Q is accepted before input suspension. " + KickWaitDiagnostics());
                using (GameTimeScaleRuntime.AcquirePause())
                {
                    root.AutomaticSimulation = true;
                    yield return null;
                    root.AutomaticSimulation = false;
                    Assert.That(root.Hero.HasPendingKick, Is.False, "Gameplay input ownership cancels the pending request.");
                }
                root.Tick(.25f);
                Assert.That(root.Hero.State.IsKicking, Is.False);
                LogAssert.NoUnexpectedReceived();
            }
            finally
            {
                if (root != null) root.AutomaticSimulation = false;
                input.TearDown();
            }
        }

        private void WalkToLeftFootLift()
        {
            Transform left = FindKickBone("foot.L");
            float plantedY = left.position.y;
            for (int tick = 0; tick < 32; tick++)
            {
                // Feed achieved controller travel, as the existing footwork
                // coverage does. The production gait and IK move the real ankle.
                root.Hero.Body.Move(Vector3.forward * (1.6f * CombatTestRoot.SimulationStep));
                root.Tick(CombatTestRoot.SimulationStep);
                if (root.Hero.Footwork.TransferringFoot && left.position.y > plantedY + .055f) return;
            }
            Assert.Fail("The production walking gait did not lift the left foot enough to exercise support waiting. " + KickWaitDiagnostics());
        }

        private string KickWaitDiagnostics()
        {
            CombatActor actor = root.Hero;
            CombatFootwork feet = actor.Footwork;
            return $"phase={actor.State.Phase}, pending={actor.HasPendingKick}, remaining={actor.PendingKickSeconds:F4}, " +
                $"support_reason={feet.LastKickSupportFailure}, gap={feet.LastKickSupportGap:F4}, " +
                $"actual_ankle={FindKickBone("foot.L").position:F4}, measured_ankle={feet.LastKickSupportAnkle:F4}, " +
                $"ground={feet.LastKickSupportGround:F4}, wait_eligible={feet.CanWaitForKickSupport}, " +
                $"balance_ready={actor.HasAttackBalance}, stamina={actor.State.Stamina:F2}, {feet.SupportDiagnostics}";
        }

        [UnityTest]
        public IEnumerator Range_OpponentStylesObserveKickAndResetWithoutChangingCombatStats()
        {
            yield return SceneManager.LoadSceneAsync(SceneIds.CombatTest);
            root = Object.FindAnyObjectByType<CombatTestRoot>();
            root.AutomaticSimulation = false;
            foreach (CombatOpponentStyle style in new[] { CombatOpponentStyle.Cautious, CombatOpponentStyle.Pressuring, CombatOpponentStyle.Patient })
            {
                Assert.That(root.SetOpponentStyle(style), Is.True);
                root.Hero.ResetActor(Vector3.up * PlayerFactory.GroundedRootOffset, Vector3.forward);
                root.Opponent.ResetActor(Vector3.up * PlayerFactory.GroundedRootOffset + Vector3.forward * 1.2f, Vector3.back);
                Physics.SyncTransforms();
                yield return null;
                Assert.That(root.Hero.TryKick(), Is.True);
                root.Tick(.18f);
                Assert.That(root.Opponent.State.Phase, Is.EqualTo(MeleePhase.Ready), "No style evades before seeing the .20s tell.");
                root.Tick(.06f);
                Assert.That(root.Opponent.State.Phase, Is.EqualTo(MeleePhase.Step));
                Assert.That(root.Opponent.State.Settings.MaxHealth, Is.EqualTo(S.MaxHealth));
                Assert.That(root.Opponent.State.Settings.Damage, Is.EqualTo(S.Damage));
                root.ResetRound();
                Assert.That(root.OpponentStyle, Is.EqualTo(style));
                Assert.That(root.Opponent.State.Phase, Is.EqualTo(MeleePhase.Ready));
            }
        }

        private Transform FindKickBone(string name)
        {
            foreach (Transform bone in root.Hero.DamageRigRoot.GetComponentsInChildren<Transform>(true))
                if (bone.name == name) return bone;
            Assert.Fail("Missing production bone " + name); return null;
        }

        private void CaptureKick(string name)
        {
            Camera camera = root.CameraFollow.Camera;
            AreaCaptureFixture.CaptureCurrentCamera(camera, SceneIds.CombatTest, name + "-shoulder");
            Vector3 position = camera.transform.position; Quaternion rotation = camera.transform.rotation;
            float fieldOfView = camera.fieldOfView;
            Vector3 target = root.Hero.transform.position + Vector3.up * .85f + Vector3.forward * .4f;
            try
            {
                Vector3 eye = target + Vector3.left * 2.6f + Vector3.up * .3f - Vector3.forward * .4f;
                camera.transform.SetPositionAndRotation(eye, Quaternion.LookRotation(target - eye));
                camera.fieldOfView = 50f;
                AreaCaptureFixture.CaptureCurrentCamera(camera, SceneIds.CombatTest, name + "-side");
            }
            finally { camera.transform.SetPositionAndRotation(position, rotation); camera.fieldOfView = fieldOfView; }
        }
    }
}

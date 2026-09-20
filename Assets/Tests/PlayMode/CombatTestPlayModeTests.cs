using System;
using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

namespace BarPromenade.Tests.PlayMode
{
    /// <summary>One bounded trip through the actual menu, authored rigs, weapon contacts and cleanup.</summary>
    [PrebuildSetup(typeof(CombatTestAssetsSetup))]
    public sealed partial class CombatTestPlayModeTests
    {
        private CombatTestRoot root;
        private float previousCaptureDelta;
        private bool previousAudioPause;

        [UnitySetUp]
        public IEnumerator SetUp()
        {
            previousCaptureDelta = Time.captureDeltaTime;
            previousAudioPause = AudioListener.pause;
            Time.captureDeltaTime = 1f / 60f;
            RetroAudioService.Instance?.StopAll();
            EnsureFixtureListener();
            yield return WaitFor(() => !SceneTransitionService.IsTransitioning &&
                !AreaTravelService.IsTraveling && !AreaTravelService.HasPendingTravel,
                "A previous scene transition did not settle.");
            GameSessionState.BeginNewGame();
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            if (root != null) root.AutomaticSimulation = false;
            // Contact voices are pooled under DontDestroyOnLoad. End them before
            // removing the scene's ear, then keep the synthetic cleanup scene valid
            // until the next Single load replaces it together with its listener.
            RetroAudioService.Instance?.StopAll();
            Scene active = SceneManager.GetActiveScene();
            if (active.IsValid() && (active.name == SceneIds.CombatTest || active.name == SceneIds.MainMenu))
            {
                Scene cleanup = SceneManager.CreateScene("Combat Test Cleanup");
                SceneManager.SetActiveScene(cleanup);
                foreach (AudioListener listener in Object.FindObjectsByType<AudioListener>())
                    if (listener.gameObject.scene == active) listener.enabled = false;
                EnsureFixtureListener();
                AsyncOperation unload = SceneManager.UnloadSceneAsync(active);
                if (unload != null) yield return unload;
            }
            GameSessionState.BeginNewGame();
            Time.captureDeltaTime = previousCaptureDelta;
            AudioListener.pause = previousAudioPause;
            root = null;
            yield return null;
        }

        private static void EnsureFixtureListener()
        {
            foreach (AudioListener listener in Object.FindObjectsByType<AudioListener>())
                if (listener.isActiveAndEnabled) return;
            new GameObject("Combat Fixture Audio Listener").AddComponent<AudioListener>();
        }

        [UnityTest]
        public IEnumerator Range_AuthoredContactsRespectGuardWallsPauseAndOwnerCleanup()
        {
            yield return SceneManager.LoadSceneAsync(SceneIds.MainMenu, LoadSceneMode.Single);
            yield return EnterRange();
            Assert.That(root.Player.Visual, Is.InstanceOf<Player3DCharacterPresentation>());
            Assert.That(root.Hero.Weapon, Is.Not.Null);
            Assert.That(root.Opponent.Weapon, Is.Not.Null);
            Assert.That(root.CameraFollow.TargetLockActive, Is.True,
                "The combat camera keeps the opponent in view without manual orbit.");
            Assert.That(root.Sparring, Is.True);
            Vector3 expectedSpawn = CombatAssetProvider.FindAnchor(root.gameObject, "OpponentSpawn").position;
            Assert.That(Vector2.Distance(new Vector2(expectedSpawn.x, expectedSpawn.z),
                new Vector2(root.Opponent.transform.position.x, root.Opponent.transform.position.z)), Is.LessThan(.15f),
                "The opponent starts at its authored spawn, allowing the first unlocked pursuit frames.");

            // The production attack reaches z=1.08 at the active midpoint; both bodies stand on its real floor.
            PlacePair(1.1f);
            var extraBody = new GameObject("Duplicate target colliders");
            extraBody.transform.SetParent(root.Opponent.transform, false);
            SphereCollider upper = extraBody.AddComponent<SphereCollider>();
            upper.center = Vector3.up * 1.2f; upper.radius = .3f;
            BoxCollider middle = extraBody.AddComponent<BoxCollider>();
            middle.center = Vector3.up; middle.size = new Vector3(.5f, .5f, .5f);
            Physics.SyncTransforms();
            Assert.That(root.Hero.TryAttack(), Is.True);
            for (int i = 0; i < 130; i++) root.Hero.Step(.01f);
            Assert.That(root.Opponent.State.Health, Is.EqualTo(75f),
                "Actual authored crowbar contact must hit once across target colliders and arc samples.");
            upper.enabled = middle.enabled = false;
            Object.Destroy(extraBody);

            PlacePair(1.1f);
            Assert.That(root.Hero.TryAttack(), Is.True);
            root.Hero.Step(2f);
            Assert.That(root.Opponent.State.Health, Is.EqualTo(75f),
                "A hitch crossing all three phases must still sweep the authored active arc.");

            PlacePair(4f);
            Assert.That(root.Hero.TryAttack(), Is.True);
            root.Hero.Step(.7f);
            Assert.That(root.Opponent.State.Health, Is.EqualTo(100f));
            Assert.That(root.Hero.State.Stamina, Is.EqualTo(70f), "A miss pays at attack initiation.");
            Assert.That(root.Hero.TryAttack(), Is.False, "Recovery commits the player after a miss.");

            PlacePair(1.1f);
            var wallObject = new GameObject("Solid melee occlusion fixture");
            wallObject.transform.SetParent(root.transform, false);
            wallObject.transform.position = root.Hero.transform.position + new Vector3(0f, 1.1f, .55f);
            BoxCollider wall = wallObject.AddComponent<BoxCollider>();
            wall.size = new Vector3(3f, 2.4f, .12f);
            Physics.SyncTransforms();
            Assert.That(root.Hero.TryAttack(), Is.True);
            for (int i = 0; i < 90 && root.Hero.State.Phase != MeleePhase.Recovery; i++)
                root.Hero.Step(1f / 120f);
            Assert.That(root.Hero.State.Phase, Is.EqualTo(MeleePhase.Recovery),
                "The visible weapon hitting a solid wall must stop its active swing.");
            Assert.That(((Player3DCharacterPresentation)root.Player.Visual).ActiveClipName,
                Is.EqualTo("CombatRecoil"), "A wall needs the authored recoil, not a normal missed swing.");
            Assert.That(root.Opponent.State.Health, Is.EqualTo(100f),
                "Weapon overlap beyond a wall must not transmit damage.");
            root.Hero.Step(2f);
            wall.enabled = false;
            Object.Destroy(wallObject);
            Physics.SyncTransforms();

            PlacePair(1.1f);
            root.Opponent.SetBlock(true);
            Assert.That(root.Hero.TryAttack(), Is.True);
            root.Hero.Step(2f);
            Assert.That(root.Opponent.State.Health, Is.EqualTo(100f));
            Assert.That(root.Opponent.State.Stamina, Is.EqualTo(75f), "Frontal guard pays exactly one contact.");
            // Exercise the other authored rig too, with the hero receiving the same frontal rules.
            PlacePair(1.1f);
            root.Hero.SetBlock(true);
            Assert.That(root.Opponent.TryAttack(), Is.True);
            root.Opponent.Step(2f);
            Assert.That(root.Hero.State.Health, Is.EqualTo(100f));
            Assert.That(root.Hero.State.Stamina, Is.EqualTo(75f));

            PlacePair(4f);
            var presentation = (Player3DCharacterPresentation)root.Player.Visual;
            object otherOwner = new object();
            Assert.That(presentation.TryAcquireClip(otherOwner, "CombatHit"), Is.True);
            Assert.That(root.Hero.TryAttack(), Is.False, "Combat may not steal an unrelated full-body clip.");
            Assert.That(root.Hero.State.Stamina, Is.EqualTo(100f), "Rejected presentation does not spend stamina.");
            root.Hero.enabled = false;
            Assert.That(presentation.OwnsClip(otherOwner), Is.True, "Actor cleanup only releases its own clip.");
            Assert.That(presentation.HasCarryPose, Is.False);
            object movementProbe = new object();
            Assert.That(root.Player.Motor.SetOwnedMovementConstraint(movementProbe, 1f, 1f), Is.True,
                "Disabling combat must release its movement constraint.");
            root.Player.Motor.ReleaseMovementConstraint(movementProbe);
            presentation.ReleaseOwnedClip(otherOwner);
            root.Hero.enabled = true;
            PlacePair(1.1f);
            Assert.That(root.Hero.TryAttack(), Is.True);
            root.Hero.Step(.2f);
            root.Player.Motor.SetInputEnabled(false);
            root.Hero.Step(2f);
            Assert.That(root.Hero.State.Phase, Is.EqualTo(MeleePhase.Ready));
            Assert.That(root.Hero.State.Stamina, Is.EqualTo(70f));
            Assert.That(presentation.OwnsClip(root.Hero), Is.False);
            root.Player.Motor.SetInputEnabled(true);
            root.Hero.Step(2f);
            Assert.That(root.Opponent.State.Health, Is.EqualTo(100f),
                "A swing interrupted by another input owner must not replay on release.");
            PlacePair(4f);

            Assert.That(root.Hero.TryAttack(), Is.True);
            root.Hero.Step(.2f);
            float pausedElapsed = root.Hero.State.AttackElapsed;
            float pausedStamina = root.Hero.State.Stamina;
            Assert.That(root.PauseMenu.Open(), Is.True);
            root.Tick(2f);
            root.Hero.Step(2f);
            yield return null;
            Assert.That(root.Hero.State.AttackElapsed, Is.EqualTo(pausedElapsed));
            Assert.That(root.Hero.State.Stamina, Is.EqualTo(pausedStamina));
            Assert.That(presentation.OwnsClip(root.Hero), Is.True, "Pause preserves the held attack pose.");
            Assert.That(root.PauseMenu.Cancel(), Is.True);
            yield return WaitFor(() => !root.PauseMenu.IsOpen && GameInput.CanRead(GameInputContext.Gameplay),
                "Pause did not release gameplay input.");
            root.ResetRound();
            Assert.That(root.Hero.State.Health, Is.EqualTo(100f));
            Assert.That(root.Hero.State.Stamina, Is.EqualTo(100f));
            Assert.That(root.Hero.State.Phase, Is.EqualTo(MeleePhase.Ready));
            Assert.That(presentation.OwnsClip(root.Hero), Is.False);

            int minute = GameSessionState.GameMinuteOfDay;
            int hunger = GameSessionState.HungerLevel;
            root.Tick(120f);
            Assert.That(GameSessionState.GameMinuteOfDay, Is.EqualTo(minute));
            Assert.That(GameSessionState.HungerLevel, Is.EqualTo(hunger));
            PlacePair(1.1f);
            for (int i = 0; i < 4; i++)
            {
                root.Hero.ResetActor(Vector3.up * PlayerFactory.GroundedRootOffset, Vector3.forward);
                Assert.That(root.Hero.TryAttack(), Is.True);
                root.Hero.Step(2f);
            }
            Assert.That(root.RoundFinished, Is.True);
            float finishedElapsed = root.Opponent.State.AttackElapsed;
            root.Tick(10f);
            Assert.That(root.Opponent.State.AttackElapsed, Is.EqualTo(finishedElapsed));
            root.ResetRound();
            Assert.That(root.RoundFinished, Is.False);

            root.SetSparring(true);
            root.Hero.ResetActor(Vector3.up * .04f, Vector3.forward);
            root.Opponent.ResetActor(new Vector3(0f, .04f, 3f), Vector3.back);
            Physics.SyncTransforms();
            for (int i = 0; i < 240 && root.Hero.State.Health == 100f; i++) root.Tick(1f / 60f);
            Assert.That(root.Opponent.transform.position.z, Is.LessThan(2f), "Sparring closes the distance.");
            Assert.That(root.Hero.State.Health, Is.LessThan(100f), "The real opponent then lands its authored strike.");
            root.SetSparring(false);

            CombatActor formerHero = root.Hero;
            Assert.That(root.ReturnToMenu(), Is.True);
            yield return WaitFor(() => SceneManager.GetActiveScene().name == SceneIds.MainMenu &&
                !SceneTransitionService.IsTransitioning, "Combat return did not reach the menu.");
            Assert.That(formerHero == null, Is.True);
            Assert.That(Object.FindAnyObjectByType<CombatActor>(), Is.Null);
            Assert.That(PauseMenuController.IsAnyPaused, Is.False);
            yield return EnterRange();
            Assert.That(root.Hero.State.Health, Is.EqualTo(100f));
            Assert.That(root.Hero.State.Stamina, Is.EqualTo(100f));
            Assert.That(root.Hero.State.Phase, Is.EqualTo(MeleePhase.Ready));
            Assert.That(root.Hero.TryAttack(), Is.True, "A fresh range must not inherit an earlier owner lock.");
            LogAssert.NoUnexpectedReceived();
        }

        [UnityTest]
        public IEnumerator Range_AutomaticUpdateConsumesMouseAndKeyboardWithoutGuiEvent()
        {
            var input = new InputTestFixture();
            Mouse mouse = null;
            Keyboard keyboard = null;
            try
            {
                input.Setup();
                keyboard = InputSystem.AddDevice<Keyboard>();
                yield return SceneManager.LoadSceneAsync(SceneIds.MainMenu, LoadSceneMode.Single);
                yield return EnterRange();
                PlacePair(1.1f);
                root.AutomaticSimulation = true;
                Assert.That(Mouse.current, Is.Null);
                for (int frame = 0; frame < 3; frame++) yield return null;
                LogAssert.NoUnexpectedReceived();
                mouse = InputSystem.AddDevice<Mouse>();
                RetroUiCanvas canvas = RetroUiTheme.CalculateCanvas(Screen.width, Screen.height);
                Vector2 actionPointer = canvas.LogicalToScreen(new Vector2(320f, 180f));
                actionPointer.y = Screen.height - actionPointer.y;
                input.Set(mouse.position, actionPointer, queueEventOnly: true);
                for (int frame = 0; frame < 6; frame++) yield return null;
                Assert.That(root.Hero.State.Phase, Is.EqualTo(MeleePhase.Ready));
                LogAssert.NoUnexpectedReceived();

                // Exercise MonoBehaviour.Update through actual device events: no direct combat stepping.
                input.Press(mouse.leftButton, queueEventOnly: true);
                yield return null;
                Assert.That(root.Hero.State.Phase, Is.EqualTo(MeleePhase.Windup),
                    "The real Update loop must consume LMB without requiring an IMGUI Event.current.");
                Assert.That(root.Hero.State.Stamina, Is.EqualTo(70f));
                Assert.That(root.Hero.State.AttackElapsed, Is.GreaterThan(0f));
                input.Release(mouse.leftButton, queueEventOnly: true);
                for (int frame = 0; frame < 90 && root.Opponent.State.Health == 100f; frame++) yield return null;
                Assert.That(root.Opponent.State.Health, Is.EqualTo(75f),
                    "Automatic frame advancement must carry the authored swing through real contact.");

                input.Press(keyboard.rKey, queueEventOnly: true);
                yield return null;
                Assert.That(root.Hero.State.Phase, Is.EqualTo(MeleePhase.Ready));
                Assert.That(root.Hero.State.Stamina, Is.EqualTo(100f));
                Assert.That(root.Opponent.State.Health, Is.EqualTo(100f), "R resets the damaged target.");
                input.Release(keyboard.rKey, queueEventOnly: true);
                yield return null;

                // The left toolbar margin belongs to the toolbar but no button: a GUI reset cannot mask a swing.
                Vector2 toolbarPointer = canvas.LogicalToScreen(new Vector2(17f, 55f));
                toolbarPointer.y = Screen.height - toolbarPointer.y;
                input.Set(mouse.position, toolbarPointer, queueEventOnly: true);
                yield return null;
                int sequenceBeforeToolbar = root.Hero.State.AttackSequence;
                input.Press(mouse.leftButton, queueEventOnly: true);
                yield return null;
                Assert.That(root.Hero.State.AttackSequence, Is.EqualTo(sequenceBeforeToolbar));
                Assert.That(root.Hero.State.Phase, Is.EqualTo(MeleePhase.Ready));
                Assert.That(root.Hero.State.Stamina, Is.EqualTo(100f),
                    "Toolbar pixels must suppress melee after top-left canvas/bottom-left input conversion.");
                input.Release(mouse.leftButton, queueEventOnly: true);
                input.Set(mouse.position, actionPointer, queueEventOnly: true);
                yield return null;

                input.Press(mouse.rightButton, queueEventOnly: true);
                for (int frame = 0; frame < 4; frame++) yield return null;
                Assert.That(root.Hero.State.IsBlocking, Is.True, "Held RMB is read on automatic updates.");
                input.Release(mouse.rightButton, queueEventOnly: true);
                yield return null;
                Assert.That(root.Hero.State.IsBlocking, Is.False);

                // A fresh, distant target isolates movement and the one-click recovery buffer.
                PlacePair(4f);
                Vector3 beforeWindup = root.Hero.transform.position;
                input.Press(keyboard.wKey, queueEventOnly: true);
                input.Press(mouse.leftButton, queueEventOnly: true);
                yield return null;
                int firstSequence = root.Hero.State.AttackSequence;
                input.Release(mouse.leftButton, queueEventOnly: true);
                for (int frame = 0; frame < 12; frame++) yield return null;
                Assert.That(root.Hero.State.Phase, Is.EqualTo(MeleePhase.Windup));
                Assert.That(root.Hero.transform.position.z, Is.GreaterThan(beforeWindup.z + .01f),
                    "W must still move the constrained capsule during windup.");
                input.Release(keyboard.wKey, queueEventOnly: true);
                yield return WaitFor(() => root.Hero.State.Phase == MeleePhase.Recovery,
                    "Automatic input attack never reached recovery.");
                Vector3 beforeRecovery = root.Hero.transform.position;
                input.Press(keyboard.wKey, queueEventOnly: true);
                for (int frame = 0; frame < 9; frame++) yield return null;
                Assert.That(root.Hero.State.Phase, Is.EqualTo(MeleePhase.Recovery));
                Assert.That(root.Hero.transform.position.z, Is.GreaterThan(beforeRecovery.z + .01f),
                    "Recovery permits restricted movement without cancelling the committed swing.");
                input.Release(keyboard.wKey, queueEventOnly: true);
                float bufferAt = root.Hero.State.Settings.AttackDurationSeconds - .12f;
                yield return WaitFor(() => root.Hero.State.AttackElapsed >= bufferAt,
                    "The attack never entered its final recovery input window.");
                input.Press(mouse.leftButton, queueEventOnly: true);
                yield return null;
                Assert.That(root.Hero.State.AttackSequence, Is.EqualTo(firstSequence),
                    "A buffered click must not cancel the first attack's recovery.");
                input.Release(mouse.leftButton, queueEventOnly: true);
                yield return WaitFor(() => root.Hero.State.AttackSequence == firstSequence + 1,
                    "The released click in final recovery must start one subsequent attack.");
                Assert.That(root.Hero.State.Phase, Is.EqualTo(MeleePhase.Windup));
                Assert.That(root.Hero.State.Stamina, Is.EqualTo(40f).Within(.01f));
                yield return WaitFor(() => root.Hero.State.Phase == MeleePhase.Ready,
                    "The buffered attack did not finish.");
                for (int frame = 0; frame < 12; frame++) yield return null;
                Assert.That(root.Hero.State.AttackSequence, Is.EqualTo(firstSequence + 1),
                    "A single buffered click cannot become an automatic attack chain.");

                input.Press(keyboard.tabKey, queueEventOnly: true);
                yield return null;
                Assert.That(root.Sparring, Is.True, "Tab switches the live simulation mode.");
                input.Release(keyboard.tabKey, queueEventOnly: true);
                yield return null;
                input.Press(keyboard.tabKey, queueEventOnly: true);
                yield return null;
                Assert.That(root.Sparring, Is.False);
                input.Release(keyboard.tabKey, queueEventOnly: true);
                yield return null;
                LogAssert.NoUnexpectedReceived();
            }
            finally
            {
                if (root != null) root.AutomaticSimulation = false;
                if (mouse != null && mouse.added) InputSystem.RemoveDevice(mouse);
                if (keyboard != null && keyboard.added) InputSystem.RemoveDevice(keyboard);
                input.TearDown();
            }
        }

        [UnityTest]
        public IEnumerator Range_TargetLockedShoulderCameraTracksOpponentAndResets()
        {
            var input = new InputTestFixture();
            Mouse mouse = null;
            Keyboard keyboard = null;
            Gamepad gamepad = null;
            GameObject wall = null;
            try
            {
                input.Setup();
                mouse = InputSystem.AddDevice<Mouse>();
                keyboard = InputSystem.AddDevice<Keyboard>();
                gamepad = InputSystem.AddDevice<Gamepad>();
                yield return SceneManager.LoadSceneAsync(SceneIds.MainMenu, LoadSceneMode.Single);
                yield return EnterRange();
                PlayerCameraFollow follow = root.CameraFollow;
                Camera camera = follow.Camera;
                Assert.That(follow.TargetLockActive, Is.True, "Fresh menu entry installs the scene-owned target lock.");
                for (int frame = 0; frame < 42; frame++) yield return null;
                AssertOpponentFramed(camera);
                AssertRightShoulder(camera);

                root.SetSparring(false);
                foreach (bool moveRight in new[] { true, false })
                {
                    var key = moveRight ? keyboard.dKey : keyboard.aKey;
                    input.Press(key, queueEventOnly: true);
                    for (int frame = 0; frame < 8; frame++) yield return null;
                    Vector3 start = root.Hero.transform.position;
                    Vector3 right = Vector3.Cross(Vector3.up, root.Hero.transform.forward);
                    var strafe = new WalkingLegProbe(root.Hero);
                    for (int frame = 0; frame < 24; frame++)
                    {
                        yield return null;
                        strafe.Sample();
                        AssertOpponentFramed(camera);
                    }
                    Assert.That(Vector3.Dot(root.Hero.transform.position - start, right) * (moveRight ? 1f : -1f),
                        Is.GreaterThan(.25f), WalkingDiagnostic(moveRight ? "D strafe" : "A strafe", start));
                    Vector3 toOpponent = root.Opponent.transform.position - root.Hero.transform.position;
                    toOpponent.y = 0f;
                    Assert.That(Vector3.Dot(root.Hero.transform.forward, toOpponent.normalized), Is.GreaterThan(.97f),
                        "Lateral movement keeps the hero facing the opponent.");
                    strafe.AssertMoving(2f, "Strafing must move both legs beneath the combat-ready torso.");
                    Assert.That(root.Hero.ActiveClipName, Does.Contain("CombatReady"));
                    input.Release(key, queueEventOnly: true);
                    for (int frame = 0; frame < 12; frame++) yield return null;
                }

                foreach (Vector3 opponentPosition in new[]
                {
                    new Vector3(0f, 0f, 4f), new Vector3(0f, 0f, 1.1f),
                    new Vector3(2f, 0f, -1f), new Vector3(-2f, 0f, -1f)
                })
                {
                    PlacePair(opponentPosition.magnitude);
                    root.Opponent.ResetActor(opponentPosition + Vector3.up * PlayerFactory.GroundedRootOffset,
                        -opponentPosition.normalized);
                    Physics.SyncTransforms();
                    for (int frame = 0; frame < 42; frame++) yield return null;
                    AssertOpponentFramed(camera);
                    AssertRightShoulder(camera);
                }
                for (int frame = 0; frame < 20; frame++)
                {
                    root.Hero.Body.Move(Vector3.right * .02f);
                    root.Opponent.Body.Move(Vector3.left * .025f);
                    Physics.SyncTransforms();
                    yield return null;
                    AssertOpponentFramed(camera);
                }
                for (int frame = 0; frame < 42; frame++) yield return null;
                AssertRightShoulder(camera);

                Vector3 cameraPosition = camera.transform.position;
                Quaternion cameraRotation = camera.transform.rotation;
                input.Press(mouse.middleButton, queueEventOnly: true);
                input.Press(keyboard.rightArrowKey, queueEventOnly: true);
                input.Press(keyboard.upArrowKey, queueEventOnly: true);
                input.Set(gamepad.rightStick, new Vector2(1f, .7f), queueEventOnly: true);
                for (int frame = 0; frame < 24; frame++)
                {
                    input.Set(mouse.delta, new Vector2(18f, -8f), queueEventOnly: true);
                    yield return null;
                }
                Assert.That(Vector3.Distance(camera.transform.position, cameraPosition), Is.LessThan(.05f),
                    "MMB, arrows and right stick must not orbit a target-locked camera.");
                Assert.That(Quaternion.Angle(camera.transform.rotation, cameraRotation), Is.LessThan(1f));
                input.Release(mouse.middleButton, queueEventOnly: true);
                input.Release(keyboard.rightArrowKey, queueEventOnly: true);
                input.Release(keyboard.upArrowKey, queueEventOnly: true);
                input.Set(gamepad.rightStick, Vector2.zero, queueEventOnly: true);
                yield return null;

                Assert.That(root.PauseMenu.Open(), Is.True);
                yield return null;
                cameraPosition = camera.transform.position;
                cameraRotation = camera.transform.rotation;
                root.Opponent.transform.position += Vector3.right * 2f;
                Physics.SyncTransforms();
                for (int frame = 0; frame < 12; frame++) yield return null;
                Assert.That(Vector3.Distance(camera.transform.position, cameraPosition), Is.LessThan(.0001f));
                Assert.That(Quaternion.Angle(camera.transform.rotation, cameraRotation), Is.LessThan(.01f),
                    "Pause freezes tracking even when a target transform changes externally.");
                Assert.That(root.PauseMenu.Cancel(), Is.True);
                yield return WaitFor(() => !root.PauseMenu.IsOpen && GameInput.CanRead(GameInputContext.Gameplay),
                    "Pause did not release the locked camera.");
                for (int frame = 0; frame < 42; frame++) yield return null;
                AssertOpponentFramed(camera);

                PlacePair(3.5f);
                wall = new GameObject("Camera obstruction fixture");
                wall.transform.SetParent(root.transform, false);
                wall.transform.position = new Vector3(0f, 1.5f, -1.1f);
                BoxCollider wallCollider = wall.AddComponent<BoxCollider>();
                wallCollider.size = new Vector3(5f, 3f, .2f);
                Physics.SyncTransforms();
                follow.Snap();
                yield return null;
                Assert.That(camera.transform.position.z, Is.GreaterThan(-.95f),
                    "The shoulder boom must stop on the hero's side of a wall.");
                Assert.That(Vector3.Distance(wallCollider.ClosestPoint(camera.transform.position), camera.transform.position),
                    Is.GreaterThan(.05f), "The resolved camera may not sit inside its obstruction.");
                AssertOpponentFramed(camera);
                Object.Destroy(wall);
                wall = null;
                yield return null;

                PlacePair(1.1f);
                root.AutomaticSimulation = true;
                Transform chest = root.Opponent.Ragdoll.PhysicsController.ChestBody.transform;
                float standingChestHeight = chest.position.y;
                yield return StrikeToDefeat(root.Hero, root.Opponent);
                yield return WaitFor(() => root.Opponent.IsRagdollActive, "The camera fixture never reached a real fallen target.");
                for (int frame = 0; frame < 120; frame++) yield return null;
                Assert.That(chest.position.y, Is.LessThan(standingChestHeight - .25f));
                AssertOpponentFramed(camera);
                input.Press(keyboard.rKey, queueEventOnly: true);
                yield return null;
                input.Release(keyboard.rKey, queueEventOnly: true);
                Assert.That(root.RoundFinished, Is.False);
                Assert.That(follow.TargetLockActive, Is.True);
                AssertOpponentFramed(camera);
                AssertRightShoulder(camera);
                yield return null;

                Assert.That(root.ReturnToMenu(), Is.True);
                yield return WaitFor(() => SceneManager.GetActiveScene().name == SceneIds.MainMenu &&
                    !SceneTransitionService.IsTransitioning, "The target-locked arena could not return to the menu.");
                Assert.That(follow == null, Is.True, "Scene exit destroys the camera's target lease with its owner.");
                var freeTarget = new GameObject("Free camera target fixture");
                var freeCameraObject = new GameObject("Free camera fixture");
                var freeCamera = freeCameraObject.AddComponent<Camera>();
                var freeFollow = freeCameraObject.AddComponent<PlayerCameraFollow>();
                freeFollow.Initialize(freeCamera, freeTarget.transform, false);
                Assert.That(freeFollow.TargetLockActive, Is.False, "An ordinary camera cannot inherit the old arena target.");
                Quaternion beforeOrbit = freeCamera.transform.rotation;
                freeFollow.RotateYaw(25f);
                freeFollow.Snap();
                Assert.That(Quaternion.Angle(beforeOrbit, freeCamera.transform.rotation), Is.GreaterThan(15f));
                Object.Destroy(freeCameraObject);
                Object.Destroy(freeTarget);
                LogAssert.NoUnexpectedReceived();
            }
            finally
            {
                if (root != null) root.AutomaticSimulation = false;
                if (wall != null) Object.Destroy(wall);
                if (gamepad != null && gamepad.added) InputSystem.RemoveDevice(gamepad);
                if (mouse != null && mouse.added) InputSystem.RemoveDevice(mouse);
                if (keyboard != null && keyboard.added) InputSystem.RemoveDevice(keyboard);
                input.TearDown();
            }
        }

        private void AssertOpponentFramed(Camera camera)
        {
            Vector3 chest = root.Opponent.Ragdoll.PhysicsController.ChestBody.transform.position;
            Vector3 viewport = camera.WorldToViewportPoint(chest);
            Assert.That(viewport.z, Is.GreaterThan(camera.nearClipPlane));
            Assert.That(viewport.x, Is.EqualTo(.5f).Within(.08f), "The opponent's live chest stays horizontally centred.");
            Assert.That(viewport.y, Is.EqualTo(.5f).Within(.08f), "Tracking must follow the live chest, including ragdoll height.");
            Vector3 heroChest = root.Hero.Ragdoll.PhysicsController.ChestBody.transform.position;
            Vector3 duelAxis = chest - heroChest;
            duelAxis.y = 0f;
            duelAxis.Normalize();
            Vector3 ray = chest - camera.transform.position;
            float depth = Vector3.Dot(ray, duelAxis);
            float fraction = Vector3.Dot(heroChest - camera.transform.position, duelAxis) / depth;
            if (depth > .01f && fraction > 0f && fraction < 1f)
            {
                Vector3 atHero = camera.transform.position + ray * fraction - heroChest;
                Assert.That(Vector3.Dot(atHero, Vector3.Cross(Vector3.up, duelAxis)), Is.GreaterThan(.55f),
                    "Keeping the enemy centred must also clear the hero's torso and raised weapon arm.");
            }
        }

        private void AssertRightShoulder(Camera camera)
        {
            Vector3 forward = root.Opponent.transform.position - root.Hero.transform.position;
            forward.y = 0f;
            forward.Normalize();
            Vector3 offset = camera.transform.position -
                ((Player3DCharacterPresentation)root.Player.Visual).Registry.Anchors.Chest.position;
            Assert.That(Vector3.Dot(offset, forward), Is.LessThan(-.2f), "The camera stays behind the hero on the duel axis.");
            Assert.That(Vector3.Dot(offset, Vector3.Cross(Vector3.up, forward)), Is.GreaterThan(.15f),
                "The camera stays over the right shoulder when the opponent crosses sides.");
        }

        [UnityTest]
        public IEnumerator Range_CombatWalkingUsesLiveInputAndMovingLegs()
        {
            var input = new InputTestFixture();
            Mouse mouse = null;
            Keyboard keyboard = null;
            try
            {
                input.Setup();
                mouse = InputSystem.AddDevice<Mouse>();
                keyboard = InputSystem.AddDevice<Keyboard>();
                yield return SceneManager.LoadSceneAsync(SceneIds.MainMenu, LoadSceneMode.Single);
                yield return EnterRange();
                // Keep the real menu-entry placement and initialization. A custom
                // ResetActor/teleport here could conceal the reported frozen hero.
                root.AutomaticSimulation = true;
                for (int frame = 0; frame < 6; frame++) yield return null;
                var presentation = (Player3DCharacterPresentation)root.Player.Visual;
                Vector3 forward = root.Hero.transform.forward;
                input.Press(keyboard.wKey, queueEventOnly: true);
                for (int frame = 0; frame < 8; frame++) yield return null;
                Vector3 start = root.Hero.transform.position;
                var walk = new WalkingLegProbe(root.Hero);
                for (int frame = 0; frame < 24; frame++) { yield return null; walk.Sample(); }
                float forwardDistance = Vector3.Dot(root.Hero.transform.position - start, forward);
                Assert.That(forwardDistance, Is.GreaterThan(.45f), WalkingDiagnostic("W", start));
                Assert.That(root.Hero.State.Phase, Is.EqualTo(MeleePhase.Ready));
                Assert.That(root.Hero.ActiveClipName, Does.Contain("CombatReady"));
                Assert.That(presentation.CurrentLocomotionState, Is.EqualTo(Player3DLocomotionState.Walk));
                walk.AssertMoving(2f, "The ready torso must leave both legs walking under live W.");
                input.Release(keyboard.wKey, queueEventOnly: true);
                // Only after the unmodified first-entry regression is proved may
                // target mode reset the pair to isolate the remaining controls.
                root.SetSparring(false);
                for (int frame = 0; frame < 12; frame++) yield return null;

                input.Press(keyboard.sKey, queueEventOnly: true);
                for (int frame = 0; frame < 8; frame++) yield return null;
                start = root.Hero.transform.position;
                var backward = new WalkingLegProbe(root.Hero);
                for (int frame = 0; frame < 24; frame++) { yield return null; backward.Sample(); }
                Assert.That(Vector3.Dot(root.Hero.transform.position - start, forward), Is.LessThan(-.25f),
                    WalkingDiagnostic("S", start));
                Assert.That(presentation.CurrentLocomotionState, Is.EqualTo(Player3DLocomotionState.WalkBack));
                backward.AssertMoving(2f, "S must animate backward steps, not slide a frozen combat pose.");
                input.Release(keyboard.sKey, queueEventOnly: true);
                for (int frame = 0; frame < 12; frame++) yield return null;

                Vector3 strafeRight = Vector3.Cross(Vector3.up, root.Hero.transform.forward);
                start = root.Hero.transform.position;
                input.Press(keyboard.dKey, queueEventOnly: true);
                for (int frame = 0; frame < 12; frame++) yield return null;
                Assert.That(Vector3.Dot(root.Hero.transform.position - start, strafeRight), Is.GreaterThan(.1f),
                    WalkingDiagnostic("D strafe", start));
                input.Release(keyboard.dKey, queueEventOnly: true);
                for (int frame = 0; frame < 12; frame++) yield return null;
                strafeRight = Vector3.Cross(Vector3.up, root.Hero.transform.forward);
                start = root.Hero.transform.position;
                input.Press(keyboard.aKey, queueEventOnly: true);
                for (int frame = 0; frame < 12; frame++) yield return null;
                Assert.That(Vector3.Dot(root.Hero.transform.position - start, strafeRight), Is.LessThan(-.1f),
                    WalkingDiagnostic("A strafe", start));
                input.Release(keyboard.aKey, queueEventOnly: true);
                for (int frame = 0; frame < 12; frame++) yield return null;

                input.Press(mouse.rightButton, queueEventOnly: true);
                input.Press(keyboard.wKey, queueEventOnly: true);
                for (int frame = 0; frame < 8; frame++) yield return null;
                start = root.Hero.transform.position;
                forward = root.Hero.transform.forward;
                var guard = new WalkingLegProbe(root.Hero);
                for (int frame = 0; frame < 24; frame++) { yield return null; guard.Sample(); }
                float guardDistance = Vector3.Dot(root.Hero.transform.position - start, forward);
                Assert.That(guardDistance, Is.GreaterThan(.15f), WalkingDiagnostic("RMB+W", start));
                Assert.That(guardDistance, Is.LessThan(forwardDistance * .75f), "Held guard slows voluntary walking.");
                Assert.That(root.Hero.State.IsBlocking, Is.True);
                Assert.That(root.Hero.ActiveClipName, Does.Contain("CombatBlock"));
                guard.AssertMoving(1f, "The held guard must preserve moving legs.");

                Assert.That(root.PauseMenu.Open(), Is.True);
                for (int frame = 0; frame < 2; frame++) yield return null;
                start = root.Hero.transform.position;
                var paused = new WalkingLegProbe(root.Hero);
                for (int frame = 0; frame < 12; frame++) { yield return null; paused.Sample(); }
                Assert.That(Vector3.Distance(root.Hero.transform.position, start), Is.LessThan(.001f),
                    "Pause must freeze the capsule even while W remains held.");
                Assert.That(paused.GreatestAngle, Is.LessThan(.1f), "Pause must freeze the visible walking pose.");
                Assert.That(root.PauseMenu.Cancel(), Is.True);
                yield return WaitFor(() => !root.PauseMenu.IsOpen && GameInput.CanRead(GameInputContext.Gameplay),
                    "Pause did not release walking input.");
                start = root.Hero.transform.position;
                for (int frame = 0; frame < 12; frame++) yield return null;
                Assert.That(Vector3.Dot(root.Hero.transform.position - start, forward), Is.GreaterThan(.04f),
                    WalkingDiagnostic("W after pause", start));
                input.Release(keyboard.wKey, queueEventOnly: true);
                input.Release(mouse.rightButton, queueEventOnly: true);
                yield return null;

                PlacePair(1.1f);
                yield return StrikeToDefeat(root.Hero, root.Opponent);
                yield return WaitFor(() => root.Hero.State.Phase == MeleePhase.Ready && root.Opponent.IsRagdollActive,
                    "The winning hero never finished the lethal swing beside the fallen opponent.");
                Assert.That(root.RoundFinished, Is.True);
                int winningSequence = root.Hero.State.AttackSequence;
                // Walk away from the physical opponent so contact with its body
                // cannot conceal a movement lease retained after victory.
                input.Press(keyboard.sKey, queueEventOnly: true);
                input.Press(mouse.leftButton, queueEventOnly: true);
                yield return null;
                input.Release(mouse.leftButton, queueEventOnly: true);
                for (int frame = 0; frame < 7; frame++) yield return null;
                start = root.Hero.transform.position;
                forward = root.Hero.transform.forward;
                Transform pelvis = presentation.Registry.Anchors.Pelvis;
                Vector3 pelvisStart = pelvis.position;
                var winnerWalk = new WalkingLegProbe(root.Hero);
                for (int frame = 0; frame < 24; frame++) { yield return null; winnerWalk.Sample(); }
                Assert.That(Vector3.Dot(root.Hero.transform.position - start, forward), Is.LessThan(-.25f),
                    WalkingDiagnostic("S after victory", start));
                Assert.That(Vector3.Dot(pelvis.position - pelvisStart, forward), Is.LessThan(-.2f),
                    "The winning hero's visible pelvis must follow its moving capsule.");
                Assert.That(Vector3.ProjectOnPlane((pelvis.position - pelvisStart) -
                    (root.Hero.transform.position - start), Vector3.up).magnitude, Is.LessThan(.2f),
                    "A retained physics pose may not pin the winning model in place.");
                winnerWalk.AssertMoving(2f, "The winning hero must keep animated steps after the round ends.");
                Assert.That(root.Hero.State.AttackSequence, Is.EqualTo(winningSequence),
                    "Walking after victory must not reopen the finished round's attack gate.");
                input.Release(keyboard.sKey, queueEventOnly: true);
                for (int frame = 0; frame < 12; frame++) yield return null;
                strafeRight = Vector3.Cross(Vector3.up, root.Hero.transform.forward);
                start = root.Hero.transform.position;
                input.Press(keyboard.dKey, queueEventOnly: true);
                for (int frame = 0; frame < 12; frame++) yield return null;
                Assert.That(Vector3.Dot(root.Hero.transform.position - start, strafeRight), Is.GreaterThan(.1f),
                    WalkingDiagnostic("D strafe after victory", start));
                input.Release(keyboard.dKey, queueEventOnly: true);

                input.Press(keyboard.rKey, queueEventOnly: true);
                yield return null;
                input.Release(keyboard.rKey, queueEventOnly: true);
                yield return null;
                Assert.That(root.RoundFinished, Is.False);
                yield return StrikeToDefeat(root.Opponent, root.Hero);
                yield return WaitFor(() => root.Hero.IsRagdollActive,
                    "The defeated hero never entered the physical pose needed to test R cleanup.");
                for (int frame = 0; frame < 12; frame++) yield return null;
                input.Press(keyboard.rKey, queueEventOnly: true);
                yield return null;
                input.Release(keyboard.rKey, queueEventOnly: true);
                yield return null;
                Assert.That(root.Hero.IsRagdollActive, Is.False);
                Assert.That(root.Hero.Body.enabled, Is.True);
                Assert.That(root.Player.Motor.InputEnabled, Is.True);
                Assert.That(root.Hero.State.Phase, Is.EqualTo(MeleePhase.Ready));
                // No helper reset or reposition after R: the real input path must
                // restore both root motion and the skinned model's world motion.
                input.Press(keyboard.wKey, queueEventOnly: true);
                for (int frame = 0; frame < 8; frame++) yield return null;
                start = root.Hero.transform.position;
                forward = root.Hero.transform.forward;
                pelvisStart = pelvis.position;
                var resetWalk = new WalkingLegProbe(root.Hero);
                for (int frame = 0; frame < 24; frame++) { yield return null; resetWalk.Sample(); }
                Assert.That(Vector3.Dot(root.Hero.transform.position - start, forward), Is.GreaterThan(.45f),
                    WalkingDiagnostic("W after ragdoll reset", start));
                Assert.That(Vector3.Dot(pelvis.position - pelvisStart, forward), Is.GreaterThan(.4f),
                    "R must release the ragdoll's pinned world pose as well as enable the motor.");
                Assert.That(Vector3.ProjectOnPlane((pelvis.position - pelvisStart) -
                    (root.Hero.transform.position - start), Vector3.up).magnitude, Is.LessThan(.2f));
                resetWalk.AssertMoving(2f, "The reset hero must resume walking with both legs.");
                input.Release(keyboard.wKey, queueEventOnly: true);
                yield return null;
                LogAssert.NoUnexpectedReceived();
            }
            finally
            {
                if (root != null) root.AutomaticSimulation = false;
                if (mouse != null && mouse.added) InputSystem.RemoveDevice(mouse);
                if (keyboard != null && keyboard.added) InputSystem.RemoveDevice(keyboard);
                input.TearDown();
            }
        }

        private string WalkingDiagnostic(string action, Vector3 start)
        {
            PlayerMotor motor = root.Player.Motor;
            return $"{action}: delta={root.Hero.transform.position - start}, motor={motor.enabled}, " +
                $"input={motor.InputEnabled}, capsule={root.Hero.Body.enabled}, grounded={motor.IsGrounded}, " +
                $"speedMultiplier={motor.SpeedMultiplier}, movementScale={root.Hero.MovementScale}, " +
                $"phase={root.Hero.State.Phase}, movementInput={GameInput.CanRead(GameInputContext.Movement)}, " +
                $"gameplayInput={GameInput.CanRead(GameInputContext.Gameplay)}, velocity={motor.PlanarVelocity}";
        }

        private sealed class WalkingLegProbe
        {
            private readonly Transform[] bones = new Transform[4];
            private readonly Quaternion[] initial = new Quaternion[4];
            private readonly float[] angles = new float[4];
            public float GreatestAngle => Mathf.Max(Mathf.Max(angles[0], angles[1]), Mathf.Max(angles[2], angles[3]));

            public WalkingLegProbe(CombatActor actor)
            {
                string[] names = { "thigh.L", "shin.L", "thigh.R", "shin.R" };
                Transform[] all = actor.GetComponentsInChildren<Transform>(true);
                for (int i = 0; i < names.Length; i++)
                {
                    string name = names[i];
                    bones[i] = Array.Find(all, bone => bone.name == name);
                    Assert.That(bones[i], Is.Not.Null, "The production rig must expose " + name);
                    initial[i] = bones[i].localRotation;
                }
            }

            public void Sample()
            {
                for (int i = 0; i < bones.Length; i++)
                    angles[i] = Mathf.Max(angles[i], Quaternion.Angle(initial[i], bones[i].localRotation));
            }

            public void AssertMoving(float minimumAngle, string message)
            {
                Assert.That(Mathf.Max(angles[0], angles[1]), Is.GreaterThan(minimumAngle), "Left leg: " + message);
                Assert.That(Mathf.Max(angles[2], angles[3]), Is.GreaterThan(minimumAngle), "Right leg: " + message);
            }
        }

        [UnityTest]
        public IEnumerator Range_DefaultAiAttacksBlocksAndModeChangesThroughLiveInput()
        {
            var input = new InputTestFixture();
            Mouse mouse = null;
            Keyboard keyboard = null;
            try
            {
                input.Setup();
                mouse = InputSystem.AddDevice<Mouse>();
                keyboard = InputSystem.AddDevice<Keyboard>();
                yield return SceneManager.LoadSceneAsync(SceneIds.MainMenu, LoadSceneMode.Single);
                yield return EnterRange();
                Assert.That(root.Sparring, Is.True, "Entering Combat Test must immediately select an active opponent.");
                float initialDistance = Vector3.Distance(root.Hero.transform.position, root.Opponent.transform.position);
                int initialSequence = root.Opponent.State.AttackSequence;
                root.AutomaticSimulation = true;
                yield return WaitFor(() => root.Hero.State.Health < 100f,
                    "The default opponent never approached and hit the idle hero through ordinary Update.");
                Assert.That(root.Opponent.State.AttackSequence, Is.GreaterThan(initialSequence));
                Assert.That(Vector3.Distance(root.Hero.transform.position, root.Opponent.transform.position),
                    Is.LessThan(initialDistance - .5f), "An active opponent must close the authored starting gap.");

                input.Press(keyboard.rKey, queueEventOnly: true);
                yield return null;
                Assert.That(root.Sparring, Is.True, "R must retain the selected active mode.");
                Assert.That(root.Hero.State.Health, Is.EqualTo(100f));
                Assert.That(root.Opponent.State.Health, Is.EqualTo(100f));
                input.Release(keyboard.rKey, queueEventOnly: true);
                input.Press(mouse.rightButton, queueEventOnly: true);
                yield return WaitFor(() => root.Hero.State.Stamina < 100f,
                    "The opponent never attacked the hero's held RMB guard.");
                Assert.That(root.Hero.State.IsBlocking, Is.True);
                Assert.That(root.Hero.State.Health, Is.EqualTo(100f),
                    "The live opponent's frontal strike must be stopped by a held guard.");
                int blockedSequence = root.Opponent.State.AttackSequence;
                input.Release(mouse.rightButton, queueEventOnly: true);
                yield return WaitFor(() => root.Opponent.State.AttackSequence > blockedSequence && root.Hero.State.Health < 100f,
                    "After the blocked strike and cooldown, the opponent must launch another real attack.");

                input.Press(keyboard.tabKey, queueEventOnly: true);
                yield return null;
                Assert.That(root.Sparring, Is.False, "Tab switches the running fight to a passive target.");
                input.Release(keyboard.tabKey, queueEventOnly: true);
                Vector3 passivePosition = root.Opponent.transform.position;
                int passiveSequence = root.Opponent.State.AttackSequence;
                for (int frame = 0; frame < 240; frame++) yield return null;
                Assert.That(root.Hero.State.Health, Is.EqualTo(100f));
                Assert.That(root.Opponent.State.AttackSequence, Is.EqualTo(passiveSequence));
                Assert.That(Vector2.Distance(new Vector2(passivePosition.x, passivePosition.z),
                    new Vector2(root.Opponent.transform.position.x, root.Opponent.transform.position.z)), Is.LessThan(.001f),
                    "Target mode must remain stationary beyond the AI's ordinary attack cooldown.");
                input.Press(keyboard.rKey, queueEventOnly: true);
                yield return null;
                Assert.That(root.Sparring, Is.False, "Reset must retain passive mode when explicitly selected.");
                input.Release(keyboard.rKey, queueEventOnly: true);
                yield return null;

                input.Press(keyboard.tabKey, queueEventOnly: true);
                yield return null;
                Assert.That(root.Sparring, Is.True);
                input.Release(keyboard.tabKey, queueEventOnly: true);
                yield return WaitFor(() => root.Hero.State.Health < 100f,
                    "Switching back to AI must restart autonomous pursuit and attacks.");

                input.Press(keyboard.tabKey, queueEventOnly: true);
                yield return null;
                Assert.That(root.Sparring, Is.False);
                input.Release(keyboard.tabKey, queueEventOnly: true);
                yield return null;
                Assert.That(root.ReturnToMenu(), Is.True);
                yield return WaitFor(() => SceneManager.GetActiveScene().name == SceneIds.MainMenu &&
                    !SceneTransitionService.IsTransitioning, "The default-AI regression could not return to the menu.");
                yield return EnterRange();
                Assert.That(root.Sparring, Is.True, "A fresh entry must use active AI even after leaving passive mode.");
                Assert.That(root.Hero.State.Health, Is.EqualTo(100f));
                root.AutomaticSimulation = true;
                yield return WaitFor(() => root.Hero.State.Health < 100f,
                    "The opponent must actively fight again after a fresh entry.");
                LogAssert.NoUnexpectedReceived();
            }
            finally
            {
                if (root != null) root.AutomaticSimulation = false;
                if (mouse != null && mouse.added) InputSystem.RemoveDevice(mouse);
                if (keyboard != null && keyboard.added) InputSystem.RemoveDevice(keyboard);
                input.TearDown();
            }
        }

        [UnityTest]
        public IEnumerator Range_LethalContactsHandOffToRagdollPauseResetAndCleanUp()
        {
            var input = new InputTestFixture();
            Keyboard keyboard = null;
            try
            {
                input.Setup();
                keyboard = InputSystem.AddDevice<Keyboard>();
                yield return SceneManager.LoadSceneAsync(SceneIds.MainMenu, LoadSceneMode.Single);
                yield return EnterRange();
                foreach (bool defeatHero in new[] { true, false })
                {
                    PlacePair(1.1f);
                    CombatActor victim = defeatHero ? root.Hero : root.Opponent;
                    CombatActor striker = defeatHero ? root.Opponent : root.Hero;
                    Transform grip = victim.Weapon.transform.parent;
                    Vector3 gripPosition = victim.Weapon.transform.localPosition;
                    Quaternion gripRotation = victim.Weapon.transform.localRotation;
                    root.AutomaticSimulation = true;
                    yield return StrikeToDefeat(striker, victim);
                    Assert.That(root.RoundFinished, Is.True);
                    Assert.That(victim.ActiveClipName, Is.EqualTo("CombatDefeat"),
                        "A lethal contact first presents the authored defeat on the same rig.");
                    Assert.That(victim.IsRagdollActive, Is.False,
                        "Physics must not skip the brief visible authored handoff.");
                    yield return null;
                    Assert.That(victim.ActiveClipName, Is.EqualTo("CombatDefeat"));
                    yield return WaitFor(() => victim.IsRagdollActive,
                        "A defeated actor never handed its visible rig to physics.");
                    Assert.That(victim.Body.enabled, Is.False);
                    Assert.That(victim.IsWeaponDropped, Is.True);
                    Assert.That(victim.Weapon.transform.parent, Is.Not.SameAs(grip));
                    Rigidbody weaponBody = victim.Weapon.GetComponent<Rigidbody>();
                    Assert.That(weaponBody, Is.Not.Null);
                    Assert.That(weaponBody.isKinematic, Is.False,
                        "The released crowbar must fall under physics separately from the hand.");
                    Rigidbody[] bodies = Array.FindAll(victim.GetComponentsInChildren<Rigidbody>(true),
                        body => !body.isKinematic);
                    Assert.That(bodies.Length, Is.GreaterThanOrEqualTo(8),
                        "The articulated body, rather than a single falling root, must enter physics.");
                    float handoffHeight = AverageBodyHeight(bodies);
                    Vector3 fallOrigin = victim.transform.position;

                    // Pause while the body is still falling, not only after it sleeps.
                    Assert.That(root.PauseMenu.Open(), Is.True);
                    yield return null;
                    var pausedPositions = new Vector3[bodies.Length];
                    var pausedRotations = new Quaternion[bodies.Length];
                    for (int i = 0; i < bodies.Length; i++)
                    { pausedPositions[i] = bodies[i].position; pausedRotations[i] = bodies[i].rotation; }
                    Vector3 pausedWeaponPosition = weaponBody.position;
                    Quaternion pausedWeaponRotation = weaponBody.rotation;
                    for (int frame = 0; frame < 20; frame++) yield return null;
                    for (int i = 0; i < bodies.Length; i++)
                    {
                        Assert.That(Vector3.Distance(bodies[i].position, pausedPositions[i]), Is.LessThan(.0001f));
                        Assert.That(Quaternion.Angle(bodies[i].rotation, pausedRotations[i]), Is.LessThan(.01f));
                    }
                    Assert.That(Vector3.Distance(weaponBody.position, pausedWeaponPosition), Is.LessThan(.0001f));
                    Assert.That(Quaternion.Angle(weaponBody.rotation, pausedWeaponRotation), Is.LessThan(.01f));
                    Assert.That(root.PauseMenu.Cancel(), Is.True);
                    yield return WaitFor(() => !root.PauseMenu.IsOpen && GameInput.CanRead(GameInputContext.Gameplay),
                        "Pause did not release the ragdoll round.");
                    for (int frame = 0; frame < 300; frame++)
                    {
                        yield return null;
                        if (frame % 10 == 0) AssertPhysicalBodyBounds(bodies, fallOrigin);
                    }
                    float settledHeight = AverageBodyHeight(bodies);
                    Assert.That(settledHeight, Is.LessThan(handoffHeight - .25f),
                        "Both rigs must visibly fall instead of remaining in a standing defeat pose.");
                    Assert.That(settledHeight, Is.LessThan(.75f));
                    Assert.That(victim.Ragdoll.MaximumBodySpeed, Is.LessThan(1f),
                        "The fallen body must settle without a continuing joint explosion.");
                    AssertPhysicalBodyBounds(new[] { weaponBody }, fallOrigin);
                    Assert.That(weaponBody.GetComponent<Collider>().bounds.min.y, Is.GreaterThan(-.08f),
                        "The released crowbar must rest on the arena floor rather than fall through it.");

                    input.Press(keyboard.rKey, queueEventOnly: true);
                    yield return null;
                    input.Release(keyboard.rKey, queueEventOnly: true);
                    yield return null;
                    Assert.That(victim.IsRagdollActive, Is.False);
                    Assert.That(victim.IsWeaponDropped, Is.False);
                    Assert.That(weaponBody.isKinematic, Is.True);
                    Assert.That(root.RoundFinished, Is.False);
                    Assert.That(victim.Body.enabled, Is.True);
                    Assert.That(victim.State.Health, Is.EqualTo(100f));
                    Assert.That(victim.State.Phase, Is.EqualTo(MeleePhase.Ready));
                    Assert.That(root.Player.Motor.InputEnabled, Is.True);
                    foreach (Rigidbody body in bodies) Assert.That(body.isKinematic, Is.True);
                    Assert.That(AverageBodyHeight(bodies), Is.GreaterThan(settledHeight + .25f),
                        "R must restore the standing skeleton as well as the health counter.");
                    Assert.That(victim.Weapon.activeInHierarchy, Is.True);
                    Assert.That(victim.Weapon.transform.parent, Is.SameAs(grip));
                    Assert.That(Vector3.Distance(victim.Weapon.transform.localPosition, gripPosition), Is.LessThan(.001f));
                    Assert.That(Quaternion.Angle(victim.Weapon.transform.localRotation, gripRotation), Is.LessThan(.1f));

                    // Reposition through the restored capsule; do not call ResetActor here,
                    // which would hide a broken reset or an unreleased animation owner.
                    PositionStriker(striker, victim);
                    Assert.That(striker.TryAttack(), Is.True, "Reset must release the earlier animation/physics owner.");
                    yield return WaitFor(() => victim.State.Health < 100f,
                        "The reset actor could not participate in another actual weapon contact.");
                }

                // Leave while the new opponent body is physical, so destruction also
                // exercises joints and rigidbodies instead of only a restored rig.
                yield return StrikeToDefeat(root.Hero, root.Opponent);
                yield return WaitFor(() => root.Opponent.IsRagdollActive, "Cleanup fixture did not reach physics.");
                Rigidbody[] formerBodies = root.Opponent.GetComponentsInChildren<Rigidbody>(true);
                GameObject formerWeapon = root.Opponent.Weapon;
                Assert.That(root.Opponent.IsWeaponDropped, Is.True);
                Assert.That(root.ReturnToMenu(), Is.True);
                yield return WaitFor(() => SceneManager.GetActiveScene().name == SceneIds.MainMenu &&
                    !SceneTransitionService.IsTransitioning, "The physical round did not return to the menu.");
                foreach (Rigidbody body in formerBodies) Assert.That(body == null, Is.True);
                Assert.That(formerWeapon == null, Is.True, "A detached crowbar must not survive scene cleanup.");
                Assert.That(Object.FindAnyObjectByType<CombatActor>(), Is.Null);
                Assert.That(PauseMenuController.IsAnyPaused, Is.False);
                yield return EnterRange();
                Assert.That(root.Hero.IsRagdollActive || root.Opponent.IsRagdollActive, Is.False);
                Assert.That(root.Hero.Body.enabled && root.Opponent.Body.enabled, Is.True);
                Assert.That(root.Hero.TryAttack(), Is.True, "A fresh scene must not inherit a physical presentation lock.");
                LogAssert.NoUnexpectedReceived();
            }
            finally
            {
                if (root != null) root.AutomaticSimulation = false;
                if (keyboard != null && keyboard.added) InputSystem.RemoveDevice(keyboard);
                input.TearDown();
            }
        }

        private IEnumerator StrikeToDefeat(CombatActor striker, CombatActor victim)
        {
            for (int contact = 0; contact < 4 && !victim.State.IsDefeated; contact++)
            {
                yield return WaitFor(() => victim.State.Phase == MeleePhase.Ready,
                    "The victim never recovered from its preceding nonlethal contact.");
                Vector3 facing = victim.IsHero ? Vector3.back : Vector3.forward;
                striker.ResetActor(victim.transform.position - facing * 1.1f, facing);
                Physics.SyncTransforms();
                float health = victim.State.Health;
                Assert.That(striker.TryAttack(), Is.True);
                yield return WaitFor(() => victim.State.Health < health,
                    "The lethal-contact fixture missed the actual opponent rig.");
            }
            Assert.That(victim.State.IsDefeated, Is.True);
        }

        private static void PositionStriker(CombatActor striker, CombatActor victim)
        {
            Vector3 facing = victim.IsHero ? Vector3.back : Vector3.forward;
            striker.Body.Move(victim.transform.position - facing * 1.1f - striker.transform.position);
            striker.transform.rotation = Quaternion.LookRotation(facing);
            Physics.SyncTransforms();
        }

        private static float AverageBodyHeight(Rigidbody[] bodies)
        {
            float height = 0f;
            foreach (Rigidbody body in bodies) height += body.worldCenterOfMass.y;
            return height / bodies.Length;
        }

        private static void AssertPhysicalBodyBounds(Rigidbody[] bodies, Vector3 origin)
        {
            foreach (Rigidbody body in bodies)
            {
                Vector3 point = body.worldCenterOfMass;
                Assert.That(float.IsNaN(point.sqrMagnitude) || float.IsInfinity(point.sqrMagnitude), Is.False);
                Assert.That(point.y, Is.GreaterThan(-.15f), "The physical rig must stay above the arena floor.");
                Assert.That(Vector3.Distance(point, origin), Is.LessThan(4f), "A joint may not launch a body out of the duel.");
                Assert.That(body.linearVelocity.magnitude, Is.LessThan(15f));
            }
        }

        [UnityTest]
        public IEnumerator Duel_SimultaneousContactsInterruptsAndOpponentDecisionsAreDeterministic()
        {
            yield return SceneManager.LoadSceneAsync(SceneIds.MainMenu, LoadSceneMode.Single);
            yield return EnterRange();
            ConfigureDuelHurtbox(root.Hero);
            ConfigureDuelHurtbox(root.Opponent);
            foreach (float step in new[] { 1f / 120f, 1f / 60f, 1f / 30f, .2f })
            {
                foreach (bool swapped in new[] { false, true })
                {
                    // The rigs have different proportions. Simultaneity means their real
                    // contacts share a simulation tick, not that their buttons shared a frame.
                    int heroContactTick = MeasureContactTick(true, swapped);
                    int opponentContactTick = MeasureContactTick(false, swapped);
                    PlaceDuelPair(swapped);
                    if (heroContactTick >= opponentContactTick)
                    {
                        Assert.That(root.Hero.TryAttack(), Is.True);
                        AdvanceDuel((heroContactTick - opponentContactTick) / 120f, step);
                        Assert.That(root.Opponent.TryAttack(), Is.True);
                    }
                    else
                    {
                        Assert.That(root.Opponent.TryAttack(), Is.True);
                        AdvanceDuel((opponentContactTick - heroContactTick) / 120f, step);
                        Assert.That(root.Hero.TryAttack(), Is.True);
                    }
                    AdvanceDuel(.72f, step);
                    Assert.That(root.Hero.State.Health, Is.EqualTo(75f),
                        $"Both contact candidates commit together: step={step}, swapped={swapped}.");
                    Assert.That(root.Opponent.State.Health, Is.EqualTo(75f),
                        "A first-applied hit must not erase an equally timed opposing contact.");

                    foreach (bool heroFirst in new[] { true, false })
                    {
                        PlaceDuelPair(swapped);
                        CombatActor earlier = heroFirst ? root.Hero : root.Opponent;
                        CombatActor later = heroFirst ? root.Opponent : root.Hero;
                        Assert.That(earlier.TryAttack(), Is.True);
                        AdvanceDuel(.2f, step);
                        Assert.That(later.TryAttack(), Is.True);
                        AdvanceDuel(.95f, step);
                        Assert.That(earlier.State.Health, Is.EqualTo(100f),
                            $"An earlier contact interrupts the later windup: step={step}, heroFirst={heroFirst}.");
                        Assert.That(later.State.Health, Is.EqualTo(75f));
                        Assert.That(later.State.IsAttacking, Is.False,
                            "Interrupted damage may not replay after stagger ends.");
                    }
                }
            }

            foreach (float step in new[] { 1f / 120f, .05f })
            {
                root.SetSparring(true);
                Vector3 ground = Vector3.up * PlayerFactory.GroundedRootOffset;
                root.Hero.ResetActor(ground, Vector3.forward);
                root.Opponent.ResetActor(ground + Vector3.forward * 1.1f, Vector3.back);
                Physics.SyncTransforms();
                Assert.That(root.Hero.TryAttack(), Is.True);
                AdvanceDuel(.1f, step);
                Assert.That(root.OpponentIntent, Is.Not.EqualTo(CombatOpponentIntent.Guard),
                    "The opponent cannot read the player's attack before its reaction delay.");
                AdvanceDuel(.2f, step);
                Assert.That(root.OpponentIntent, Is.EqualTo(CombatOpponentIntent.Guard));
                Assert.That(root.Opponent.State.IsBlocking, Is.True);
                AdvanceDuel(.35f, step);
                Assert.That(root.Opponent.State.Health, Is.EqualTo(100f),
                    "A reaction started during windup must guard the actual weapon contact.");
                Assert.That(root.Opponent.State.Stamina, Is.EqualTo(75f));

                root.SetSparring(true);
                // Setup only: one costly guard leaves too little for an attack.
                root.Opponent.State.SetBlocking(true);
                Assert.That(root.Opponent.State.ReceiveHit(25f, 75f, true), Is.EqualTo(MeleeHitResult.Blocked));
                root.Opponent.State.SetBlocking(false);
                AdvanceDuel(.25f, step);
                Assert.That(root.OpponentIntent, Is.EqualTo(CombatOpponentIntent.Recover),
                    "A tired opponent chooses recovery from its own resources, independently of frame partition.");
                Assert.That(root.Opponent.State.IsAttacking, Is.False);
                Assert.That(root.Opponent.State.Stamina, Is.EqualTo(25f));
            }
            LogAssert.NoUnexpectedReceived();
        }

        private static void ConfigureDuelHurtbox(CombatActor actor)
        {
            // The hero also carries a wider cloth trigger. Give this ordering fixture
            // exactly one identical body capsule per target; normal-range tests retain all colliders.
            foreach (Collider collider in actor.GetComponentsInChildren<Collider>(true))
                if (collider != actor.Body) collider.enabled = false;
            actor.Body.height = 1.7f;
            actor.Body.radius = .32f;
            actor.Body.center = Vector3.up * .85f;
            Physics.SyncTransforms();
        }

        private int MeasureContactTick(bool heroAttacks, bool swapped)
        {
            PlaceDuelPair(swapped);
            CombatActor attacker = heroAttacks ? root.Hero : root.Opponent;
            CombatActor receiver = heroAttacks ? root.Opponent : root.Hero;
            Assert.That(attacker.TryAttack(), Is.True);
            for (int tick = 1; tick <= 90; tick++)
            {
                root.Tick(1f / 120f);
                if (receiver.State.Health < receiver.State.Settings.MaxHealth) return tick;
            }
            Assert.Fail("The authored weapon never reached the calibrated duel capsule.");
            return 0;
        }

        private void PlaceDuelPair(bool swapped)
        {
            PlacePair(1.1f);
            if (!swapped) return;
            Vector3 ground = Vector3.up * PlayerFactory.GroundedRootOffset;
            root.Hero.ResetActor(ground + Vector3.forward * 1.1f, Vector3.back);
            root.Opponent.ResetActor(ground, Vector3.forward);
            Physics.SyncTransforms();
        }

        private void AdvanceDuel(float duration, float step)
        {
            double remaining = duration;
            while (remaining > .000001d)
            {
                float delta = (float)Math.Min(step, remaining);
                root.Tick(delta);
                remaining -= delta;
            }
        }

        private IEnumerator EnterRange()
        {
            Assert.That(CombatTestStartService.TryStart(), Is.True);
            yield return WaitFor(() =>
            {
                root = Object.FindAnyObjectByType<CombatTestRoot>();
                return root != null && root.IsInitialized && !SceneTransitionService.IsTransitioning &&
                    GameInput.CanRead(GameInputContext.Gameplay);
            }, "Combat range did not initialize and release its entry transition.");
            root.AutomaticSimulation = false;
        }

        private void PlacePair(float distance)
        {
            root.SetSparring(false);
            Vector3 position = Vector3.up * PlayerFactory.GroundedRootOffset;
            root.Hero.ResetActor(position, Vector3.forward);
            root.Opponent.ResetActor(position + Vector3.forward * distance, Vector3.back);
            Physics.SyncTransforms();
        }

        private static IEnumerator WaitFor(Func<bool> condition, string failure)
        {
            float deadline = Time.realtimeSinceStartup + 30f;
            while (!condition() && Time.realtimeSinceStartup < deadline) yield return null;
            Assert.That(condition(), Is.True, failure);
        }
    }
}

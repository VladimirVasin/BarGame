using System.Collections;
using System.IO;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace BarPromenade.Tests.PlayMode
{
    /// <summary>The inventory and real world adapter share one visible crowbar through ordinary gait and actions.</summary>
    public sealed class CrowbarEquipmentPlayModeTests
    {
        private const int CaptureLayer = 28;
        private const int TileSize = 384;
        private const int SheetRows = 9;
        private const float FrameSeconds = 1f / 60f;
        private readonly object cursorOwner = new object();
        private InputTestFixture input;
        private Keyboard keyboard;
        private Mouse mouse;
        private GameObject fixture;
        private PlayerRuntime player;
        private Player3DCharacterPresentation presentation;
        private PlayerCrowbarCombatController controller;
        private InventoryController inventory;
        private Camera camera;
        private RenderTexture target;
        private Texture2D tile, sheet;
        private int captured;
        private float previousDelta, previousTimeScale;
        private bool previousFocus;
        private Scene previousScene, fixtureScene;

        [UnitySetUp]
        public IEnumerator SetUp()
        {
            previousDelta = Time.captureDeltaTime;
            previousTimeScale = Time.timeScale;
            previousFocus = GameInput.MovementFocused;
            Time.captureDeltaTime = FrameSeconds;
            Time.timeScale = 1f;
            input = new InputTestFixture();
            input.Setup();
            keyboard = InputSystem.AddDevice<Keyboard>();
            mouse = InputSystem.AddDevice<Mouse>();
            GameSessionState.BeginNewGame();
            GameInput.HandleApplicationFocus(true);
            GameCursor.Capture(cursorOwner, CursorLockMode.Locked, false, 0);
            previousScene = SceneManager.GetActiveScene();
            fixtureScene = SceneManager.CreateScene("CrowbarEquipmentFixture");
            SceneManager.SetActiveScene(fixtureScene);
            fixture = new GameObject("Crowbar equipment fixture");
            Vector3 origin = new Vector3(2000f, 0f, 2000f);
            GameObject floor = GameObject.CreatePrimitive(PrimitiveType.Cube);
            floor.name = "Crowbar test floor";
            floor.transform.SetParent(fixture.transform, false);
            floor.transform.position = origin + Vector3.down * .1f;
            floor.transform.localScale = new Vector3(40f, .2f, 40f);
            floor.layer = CaptureLayer;
            var cameraObject = new GameObject("Crowbar inspection camera");
            cameraObject.transform.SetParent(fixture.transform, false);
            camera = cameraObject.AddComponent<Camera>();
            if (Object.FindAnyObjectByType<AudioListener>() == null) cameraObject.AddComponent<AudioListener>();
            camera.enabled = false;
            camera.orthographic = true;
            camera.orthographicSize = 1.45f;
            camera.nearClipPlane = .03f;
            camera.farClipPlane = 30f;
            camera.aspect = 1f;
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = new Color(.19f, .21f, .24f);
            camera.cullingMask = 1 << CaptureLayer;
            camera.allowHDR = camera.allowMSAA = false;
            UniversalAdditionalCameraData data = camera.GetUniversalAdditionalCameraData();
            data.renderPostProcessing = false;
            data.volumeLayerMask = 0;
            var lightObject = new GameObject("Crowbar inspection key");
            lightObject.transform.SetParent(fixture.transform, false);
            Light key = lightObject.AddComponent<Light>();
            key.type = LightType.Directional;
            key.intensity = 1.7f;
            key.color = new Color(1f, .9f, .8f);
            key.cullingMask = 1 << CaptureLayer;
            key.shadows = LightShadows.Hard;
            lightObject.transform.rotation = Quaternion.Euler(48f, 145f, 0f);
            player = PlayerFactory.Create(fixture.transform, origin + Vector3.up * PlayerFactory.GroundedRootOffset,
                camera, null, null);
            presentation = (Player3DCharacterPresentation)player.Visual;
            controller = player.GameObject.GetComponent<PlayerCrowbarCombatController>();
            Assert.That(controller, Is.Not.Null, "This fixture must exercise the ordinary-world owner, outside CombatTest.");
            var follow = cameraObject.AddComponent<PlayerCameraFollow>();
            follow.Initialize(camera, player.GameObject.transform, true);
            follow.enabled = false;
            GameCursor.Capture(cursorOwner, CursorLockMode.Locked, false, 0);
            var ui = new GameObject("Crowbar equipment inventory");
            ui.transform.SetParent(fixture.transform, false);
            inventory = ui.AddComponent<InventoryController>();
            inventory.Initialize(player, follow, ui.AddComponent<IntoxicationHudView>());
            target = new RenderTexture(TileSize, TileSize, 24, RenderTextureFormat.ARGB32);
            target.Create();
            camera.targetTexture = target;
            tile = new Texture2D(TileSize, TileSize, TextureFormat.RGB24, false);
            sheet = new Texture2D(TileSize * 4, TileSize * SheetRows, TextureFormat.RGB24, false);
            Assert.That(GameSessionState.TryCollectWorldItem("test-crowbar-equipment", InventoryItemId.CombatCrowbar), Is.True);
            Physics.SyncTransforms();
            yield return Await(() => controller.Actor != null, "The collected crowbar did not initialize its real actor.");
            yield return null;
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            if (inventory != null) inventory.enabled = false;
            GameCursor.Release(cursorOwner);
            if (keyboard != null && keyboard.added) InputSystem.RemoveDevice(keyboard);
            if (mouse != null && mouse.added) InputSystem.RemoveDevice(mouse);
            input?.TearDown();
            if (fixture != null) Object.Destroy(fixture);
            if (target != null) { target.Release(); Object.Destroy(target); }
            if (tile != null) Object.Destroy(tile);
            if (sheet != null) Object.Destroy(sheet);
            GameSessionState.BeginNewGame();
            GameInput.HandleApplicationFocus(previousFocus);
            Time.captureDeltaTime = previousDelta;
            Time.timeScale = previousTimeScale;
            if (previousScene.IsValid() && previousScene.isLoaded) SceneManager.SetActiveScene(previousScene);
            if (fixtureScene.IsValid() && fixtureScene.isLoaded)
                yield return SceneManager.UnloadSceneAsync(fixtureScene);
            yield return null;
        }

        [UnityTest, Category("CurrentCombat")]
        public IEnumerator InventoryDrawWalkStrikeAndStowPreserveOrdinaryGait()
        {
            CombatActor actor = controller.Actor;
            Assert.That(actor.HasWeaponInHand, Is.False);
            AssertHidden(actor.CarriedWeapon(CombatWeaponId.Crowbar));
            AssertHidden(actor.CarryMount(CombatWeaponId.Crowbar).parent.gameObject);
            Capture("00-stowed");
            int sequence = actor.State.AttackSequence;
            yield return InventoryToggle(true);
            yield return AwaitCarry(true, "draw-transfer");
            Assert.That(actor.State.AttackSequence, Is.EqualTo(sequence), "Equipping is not an attack command.");
            input.Release(mouse.leftButton, queueEventOnly: true);
            yield return null;
            Assert.That(actor.State.AttackSequence, Is.EqualTo(sequence), "LMB held through the paused inventory cannot launch on close or release.");
            AssertVisible(actor.CarriedWeapon(CombatWeaponId.Crowbar));
            for (int frame = 0; frame < 30; frame++) yield return null;
            Capture("01-held");
            CaptureReadyViews("ready-carried");
            actor.SetCombatFocused(true);
            CaptureReadyViews("ready-focused");
            actor.SetCombatFocused(false);
            yield return OrdinaryWalk("02-walk-before-strike");

            input.Press(mouse.leftButton, queueEventOnly: true);
            yield return Await(() => actor.State.AttackSequence == sequence + 1, "Fresh LMB did not start the equipped strike.");
            Transform tip = CombatAssetProvider.FindAnchor(actor.Weapon, "StrikeTip");
            Vector3 previousTip = tip.position;
            float path = 0f, fastest = 0f;
            int actionFrames = 0;
            float windup = MeleeCombatSettings.Crowbar.WindupSeconds;
            float activeEnd = windup + MeleeCombatSettings.Crowbar.ActiveSeconds;
            float[] captureTimes = { 0f, windup * .5f, windup - .02f, windup + .02f,
                windup + .06f, windup + .10f, activeEnd - .01f, activeEnd + .08f,
                activeEnd + .25f, MeleeCombatSettings.Crowbar.AnimationAttackDurationSeconds - .05f };
            int nextCapture = 0;
            do
            {
                if (nextCapture < captureTimes.Length && actor.State.AttackElapsed >= captureTimes[nextCapture])
                {
                    Capture("strike-" + nextCapture.ToString("D2") + "-" + actor.State.Phase + "-" +
                        Mathf.RoundToInt(actor.State.AttackElapsed * 1000f) + "ms");
                    nextCapture++;
                }
                yield return null;
                float distance = Vector3.Distance(previousTip, tip.position);
                if (actor.State.IsAttacking)
                {
                    path += distance;
                    fastest = Mathf.Max(fastest, distance / FrameSeconds);
                }
                previousTip = tip.position;
                Assert.That(actor.State.AttackSequence, Is.EqualTo(sequence + 1), "Holding LMB must never repeat the fixed strike.");
                Assert.That(actor.State.IsCharging || actor.State.HasBufferedAttack || actor.State.HasBufferedCharge, Is.False);
                actionFrames++;
            } while ((actor.State.IsAttacking || !actor.GuardReady) && actionFrames < 180);
            Assert.That(actor.GuardReady, Is.True, "The full return did not settle its physical grip.");
            Assert.That(path, Is.GreaterThan(.75f), "The visible weapon needs a broad physical arc.");
            Assert.That(fastest, Is.GreaterThan(3f), "The swing must visibly accelerate, not only traverse the poses slowly.");
            input.Release(mouse.leftButton, queueEventOnly: true);
            for (int frame = 0; frame < 24; frame++) yield return null;
            Assert.That(actor.State.AttackSequence, Is.EqualTo(sequence + 1), "Release cannot launch an old charge.");
            CaptureReadyViews("ready-after-strike");
            yield return OrdinaryWalk("03-walk-after-strike");

            TestContext.Out.WriteLine("Before fresh RMB: " + CrowbarDiagnostics());
            input.Press(mouse.rightButton, queueEventOnly: true);
            for (int frame = 0; frame < 240 && !actor.State.IsBlocking; frame++)
            {
                yield return null;
                if (frame < 12) TestContext.Out.WriteLine("RMB frame " + frame + ": " + CrowbarDiagnostics());
            }
            Assert.That(actor.State.IsBlocking, Is.True, "Fresh RMB did not start the finite parry. " + CrowbarDiagnostics());
            Capture("04-parry");
            for (int frame = 0; frame < 30; frame++) yield return null;
            Assert.That(actor.State.IsBlocking, Is.False, "Held RMB must not extend the parry.");
            input.Release(mouse.rightButton, queueEventOnly: true);
            yield return Await(() => actor.GuardReady, "Parry did not return to the settled held pose.");

            var wall = new GameObject("Crowbar obstacle");
            wall.transform.SetParent(fixture.transform, false);
            // Match the current physical-contact fixture, beyond the resting hands but across the live arc.
            wall.transform.SetPositionAndRotation(player.GameObject.transform.position +
                player.GameObject.transform.forward * .65f + Vector3.up * 1.2f, player.GameObject.transform.rotation);
            BoxCollider obstacle = wall.AddComponent<BoxCollider>();
            obstacle.size = new Vector3(2f, 2f, .08f);
            Physics.SyncTransforms();
            int wallSequence = actor.State.AttackSequence;
            input.Press(mouse.leftButton, queueEventOnly: true);
            yield return Await(() => actor.State.AttackSequence == wallSequence + 1,
                "The fresh wall strike did not begin. " + CrowbarDiagnostics());
            input.Release(mouse.leftButton, queueEventOnly: true);
            for (int frame = 0; frame < 120 && actor.State.AttackOutcome == MeleeAttackOutcome.None; frame++) yield return null;
            // World contacts take StopWeaponOnSolid; BlockedWeaponSwingCount is only the rejected-body-pose diagnostic.
            Assert.That(actor.State.AttackOutcome, Is.EqualTo(MeleeAttackOutcome.Obstacle),
                "The real solid wall must stop the active strike. " + CrowbarDiagnostics());
            Assert.That(presentation.ActiveClipName, Is.EqualTo("CombatRecoil"));
            Capture("wall-stop");
            obstacle.enabled = false;
            Object.Destroy(wall);
            yield return Await(() => actor.GuardReady, "Wall recoil did not finish.");

            CombatActor opponent = CreateOpponent();
            Vector3 direction = -actor.transform.forward;
            new CombatActor.Contact(opponent, actor, presentation.Registry.Anchors.Chest.position, -direction, direction,
                new MeleeHitLocation(MeleeBodyRegion.Torso, MeleeHitSide.Front), Player3DAnatomicalPart.Torso,
                Vector3.zero, physicalBody: true, bodyRegion: BodyDamageRegion.Chest).Apply();
            Assert.That(actor.State.Phase, Is.EqualTo(MeleePhase.Stagger));
            yield return Await(() => actor.GuardReady && !actor.ImpactMotion.IsActive,
                "The ordinary hit must return the held rig to its natural baseball stance.");
            for (int frame = 0; frame < 24; frame++) yield return null;
            CaptureReadyViews("ready-after-hit");

            // The laboratory winner releases full-body combat, while the
            // ordinary carry must still close the supporting hand.
            opponent.State.ReceiveHit(opponent.State.Settings.MaxHealth * 3f, 0f, false);
            Assert.That(opponent.State.IsDefeated, Is.True);
            actor.SetContactTarget(opponent);
            for (int frame = 0; frame < 45; frame++)
            {
                actor.AdvanceRoundEnd(FrameSeconds);
                yield return null;
            }
            Assert.That(actor.SupportGripWeight, Is.GreaterThan(.9f),
                "The winner's supporting fingers must finish closing after the carry handoff.");
            CaptureReadyViews("ready-victory");

            yield return InventoryToggle(false);
            yield return AwaitCarry(false, "stow-transfer");
            AssertHidden(actor.CarriedWeapon(CombatWeaponId.Crowbar));
            AssertHidden(actor.CarryMount(CombatWeaponId.Crowbar).parent.gameObject);
            Assert.That(GameSessionState.GetInventoryItemCount(InventoryItemId.CombatCrowbar), Is.EqualTo(1));
            Capture("05-stowed-again");
            yield return OrdinaryWalk("06-walk-stowed");
        }

        private IEnumerator InventoryToggle(bool equip)
        {
            CombatActor actor = controller.Actor;
            bool wasHeld = actor.HasWeaponInHand;
            Assert.That(inventory.Open(), Is.True);
            int item = -1;
            for (int index = 0; index < GameSessionState.InventoryItems.Count; index++)
                if (GameSessionState.InventoryItems[index].ItemId == InventoryItemId.CombatCrowbar) item = index;
            Assert.That(item, Is.GreaterThanOrEqualTo(0));
            if (inventory.SelectedItemIndex != item) Assert.That(inventory.SelectItem(item), Is.True);
            Assert.That(inventory.CanUseSelected, Is.True, "The inventory must expose the real crowbar equip action.");
            yield return null;
            input.Press(keyboard.uKey, queueEventOnly: true);
            yield return null;
            input.Release(keyboard.uKey, queueEventOnly: true);
            yield return null;
            if (equip)
            {
                // A mouse press belonging to the paused menu must not become a queued strike on its first gameplay frame.
                input.Press(mouse.leftButton, queueEventOnly: true);
                yield return null;
            }
            Assert.That(inventory.SelectedIsEquipped, Is.EqualTo(equip));
            Assert.That(Time.timeScale, Is.Zero);
            Assert.That(actor.HasWeaponInHand, Is.EqualTo(wasHeld), "The paused inventory cannot move the visible weapon.");
            Assert.That(actor.IsSwitchingWeapon, Is.False, "Draw/stow must wait for gameplay input ownership.");
            Assert.That(inventory.Close(), Is.True);
            yield return null;
        }

        private IEnumerator AwaitCarry(bool held, string captureName)
        {
            CombatActor actor = controller.Actor;
            bool sawTransfer = false;
            for (int frame = 0; frame < 240 && (actor.IsSwitchingWeapon || actor.HasWeaponInHand != held); frame++)
            {
                yield return null;
                if (!sawTransfer && actor.IsSwitchingWeapon && actor.WeaponCarryActionProgress >= .45f)
                {
                    Capture(captureName);
                    sawTransfer = true;
                }
            }
            Assert.That(sawTransfer, Is.True, "The requested transfer needs a visible intermediate authored pose.");
            Assert.That(actor.HasWeaponInHand, Is.EqualTo(held));
            Assert.That(actor.IsSwitchingWeapon, Is.False, "The inventory command did not finish its physical transfer.");
        }

        private IEnumerator OrdinaryWalk(string captureName)
        {
            CombatActor actor = controller.Actor;
            Transform root = player.GameObject.transform;
            Vector3 start = root.position;
            float minimumLead = float.PositiveInfinity, maximumLead = float.NegativeInfinity;
            input.Press(keyboard.wKey, queueEventOnly: true);
            for (int frame = 0; frame < 75; frame++)
            {
                yield return null;
                if (frame < 20) continue;
                Assert.That(presentation.OwnsClip(actor), Is.False, "An idle equipped crowbar must yield the full-body combat clip to ordinary gait.");
                float lead = Vector3.Dot(presentation.Registry.Anchors.LeftFoot.position -
                    presentation.Registry.Anchors.RightFoot.position, root.forward);
                minimumLead = Mathf.Min(minimumLead, lead);
                maximumLead = Mathf.Max(maximumLead, lead);
                if (frame == 40) Capture(captureName);
            }
            Assert.That(Vector3.Distance(root.position, start), Is.GreaterThan(.5f));
            Assert.That(presentation.LocomotionBlend, Is.GreaterThan(.7f));
            Assert.That(minimumLead, Is.LessThan(-.1f), "The left boot must pass behind the right boot in ordinary gait.");
            Assert.That(maximumLead, Is.GreaterThan(.1f), "The left boot must also pass ahead; a permanent leading foot is a shuffle.");
            input.Release(keyboard.wKey, queueEventOnly: true);
            for (int frame = 0; frame < 24; frame++) yield return null;
        }

        private CombatActor CreateOpponent()
        {
            var opponent = new GameObject("Crowbar stance opponent");
            opponent.transform.SetParent(fixture.transform, false);
            opponent.transform.position = player.GameObject.transform.position + player.GameObject.transform.forward * 4f;
            CharacterController body = opponent.AddComponent<CharacterController>();
            body.height = 1.75f; body.radius = .32f; body.center = Vector3.up * .875f;
            body.skinWidth = PlayerFactory.GroundedRootOffset;
            VillageResidentPresentation visual = DefaultNpcFactory.CreateForCharacter(opponent.transform,
                DefaultNpcPopulation.CombatTestOpponentId(0));
            CombatZombieAssetProvider.ApplyAppearance(visual.gameObject, 0);
            CombatActor actor = opponent.AddComponent<CombatActor>();
            actor.InitializeOpponent(visual, body, kind: CombatEnemyKind.Zombie);
            Physics.SyncTransforms();
            return actor;
        }

        private void CaptureReadyViews(string name)
        {
            presentation.ReapplyLatePresentationPose();
            CombatActor actor = controller.Actor;
            Transform root = player.GameObject.transform;
            Transform tip = CombatAssetProvider.FindAnchor(actor.Weapon, "StrikeTip");
            Vector3 shaft = actor.Weapon.transform.up;
            Assert.That(Vector3.Dot(shaft, Vector3.up), Is.GreaterThan(.65f), name + ": the held tip points upward.");
            Assert.That(Vector3.Dot(tip.position - root.position, root.right), Is.GreaterThan(.12f),
                name + ": the bat rests beside the hero's right shoulder.");
            Assert.That(actor.Weapon.transform.position.y - root.position.y, Is.GreaterThan(1.15f),
                name + ": the baseball grip is held above the waist.");
            NpcHandPose hands = presentation.Registry.GetComponentInChildren<NpcHandPose>(true);
            if (hands == null) hands = presentation.Registry.GetComponentInParent<NpcHandPose>();
            Assert.That(hands, Is.Not.Null);
            Assert.That(Vector3.Distance(hands.CylinderCentre(true),
                actor.Weapon.transform.TransformPoint(Vector3.up * CombatSupportGrip.ReadyDistance)), Is.LessThan(.012f),
                name + ": the left palm stays on its authored cylindrical contact.");
            foreach (NpcHandPose.HandBinding hand in hands.Hands)
            {
                Transform forearm = CityPedestrianHandProps.FindSocket(actor.DamageRigRoot, "forearm." + (hand.IsLeft ? "L" : "R"));
                Vector3 forearmDirection = (hand.Hand.position - forearm.position).normalized;
                Assert.That(Vector3.Angle(forearmDirection, hands.AnatomicalFingerDirection(hand.IsLeft)),
                    Is.LessThan(67f), name + ": neither wrist may invert to reach the shaft.");
            }
            Transform leftShoulder = CityPedestrianHandProps.FindSocket(actor.DamageRigRoot, "upper_arm.L");
            Transform leftElbow = CityPedestrianHandProps.FindSocket(actor.DamageRigRoot, "forearm.L");
            Assert.That(leftElbow.position.y - leftShoulder.position.y, Is.LessThan(.08f),
                name + ": the supporting elbow stays below its shoulder.");
            Capture(name + "-front", new Vector3(.7f, .1f, 3f), true);
            Capture(name + "-rear", new Vector3(-.6f, .1f, -3f), true);
            Capture(name + "-side", new Vector3(3f, .1f, .1f), true);
        }

        private void Capture(string name, Vector3? view = null, bool upperBody = false)
        {
            // Coroutine assertions precede LateUpdate. Manual Camera.Render
            // must receive the same completed rig/contact pass as gameplay.
            presentation.ReapplyLatePresentationPose();
            CombatActor actor = controller.Actor;
            if (actor != null && actor.HasWeaponInHand)
            {
                NpcHandPose hands = presentation.Registry.GetComponentInChildren<NpcHandPose>(true);
                if (hands == null) hands = presentation.Registry.GetComponentInParent<NpcHandPose>();
                Assert.That(hands, Is.Not.Null);
                Assert.That(Vector3.Distance(actor.Weapon.transform.TransformPoint(Vector3.up * CombatAssetProvider.RightGripStation), hands.CylinderCentre(false)),
                    Is.LessThan(.005f), name + ": the rendered bar must remain in its right palm.");
                Rigidbody weaponBody = actor.Weapon.GetComponent<Rigidbody>();
                Assert.That(Vector3.Distance(weaponBody.position, actor.Weapon.transform.position), Is.LessThan(.005f),
                    name + ": physics and rendered attachment must share the final pose.");
            }
            foreach (Transform child in player.GameObject.GetComponentsInChildren<Transform>(true)) child.gameObject.layer = CaptureLayer;
            Transform body = player.GameObject.transform;
            Vector3 focus = body.position + Vector3.up * (upperBody ? 1.3f : 1f);
            // PlayerCameraFollow.Initialize establishes its gameplay lens after fixture construction.
            // Every diagnostic render explicitly uses the same readable full-body inspection lens.
            camera.orthographic = true;
            camera.orthographicSize = upperBody ? .95f : 1.45f;
            camera.aspect = 1f;
            Vector3 direction = view.HasValue ? body.TransformDirection(view.Value) :
                body.forward * 1.8f + body.right * 3f + Vector3.up * .6f;
            camera.transform.position = focus + direction.normalized * 8f;
            camera.transform.LookAt(focus);
            camera.Render();
            RenderTexture previous = RenderTexture.active;
            try
            {
                RenderTexture.active = target;
                tile.ReadPixels(new Rect(0f, 0f, TileSize, TileSize), 0, 0);
                tile.Apply();
                string folder = Path.Combine(Directory.GetCurrentDirectory(), "Captures", "CrowbarEquipment");
                Directory.CreateDirectory(folder);
                File.WriteAllBytes(Path.Combine(folder, name + ".png"), tile.EncodeToPNG());
                if (captured < SheetRows * 4)
                {
                    sheet.SetPixels(captured % 4 * TileSize, (SheetRows - 1 - captured / 4) * TileSize, TileSize, TileSize, tile.GetPixels());
                    sheet.Apply();
                    File.WriteAllBytes(Path.Combine(folder, "sequence.png"), sheet.EncodeToPNG());
                    captured++;
                }
            }
            finally { RenderTexture.active = previous; }
        }

        private string CrowbarDiagnostics()
        {
            CombatActor actor = controller.Actor;
            CombatSupportGrip grip = actor.SupportGrip;
            return "phase=" + actor.State.Phase + ", outcome=" + actor.State.AttackOutcome +
                ", sequence=" + actor.State.AttackSequence + ", command=" + actor.State.LastCommandRejection +
                ", ready=" + actor.GuardReady +
                ", rejection=" + actor.GuardSupportRejection + ", pending=" + controller.HasPendingAction +
                ", clip=" + presentation.ActiveClipName + ", held=" + actor.HasWeaponInHand +
                ", focused=" + actor.CombatFocused + ", grip=" + grip?.State + "/" + grip?.Weight +
                ", gripReject=" + grip?.LastPoseRejection + ", error=" + grip?.JournalContactError +
                ", angle=" + grip?.JournalContactAngle + ", shape=" + actor.WeaponBlockingShape +
                ", penetration=" + actor.WeaponPenetrationDepth +
                ", input=" + GameInput.CanRead(GameInputContext.Gameplay) +
                ", mouse=" + GameCursor.GameplayMouseInputAllowed;
        }

        private static void AssertHidden(GameObject model)
        {
            Renderer[] renderers = model.GetComponentsInChildren<Renderer>(true);
            Assert.That(renderers, Is.Not.Empty);
            foreach (Renderer renderer in renderers)
                Assert.That(renderer.forceRenderingOff || !renderer.enabled || !renderer.gameObject.activeInHierarchy, Is.True,
                    renderer.name + " must be completely hidden while the crowbar is stowed.");
        }

        private static void AssertVisible(GameObject model)
        {
            bool visible = false;
            foreach (Renderer renderer in model.GetComponentsInChildren<Renderer>(true))
                visible |= renderer.enabled && !renderer.forceRenderingOff && renderer.gameObject.activeInHierarchy;
            Assert.That(visible, Is.True, "The completed draw must show the same real crowbar.");
        }

        private static IEnumerator Await(System.Func<bool> condition, string failure)
        {
            for (int frame = 0; frame < 240 && !condition(); frame++) yield return null;
            Assert.That(condition(), Is.True, failure);
        }
    }
}

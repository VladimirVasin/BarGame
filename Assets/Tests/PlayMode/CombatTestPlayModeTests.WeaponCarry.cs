using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace BarPromenade.Tests.PlayMode
{
    public sealed partial class CombatTestPlayModeTests
    {
        [UnityTest]
        public IEnumerator Range_WeaponCarrySwitchesKeepModelsAmmoAndPhysicalHandoffs()
        {
            var input = new InputTestFixture();
            Keyboard keyboard = null;
            Gamepad gamepad = null;
            try
            {
                input.Setup();
                keyboard = InputSystem.AddDevice<Keyboard>();
                gamepad = InputSystem.AddDevice<Gamepad>();
                foreach (CombatWeaponId secondary in new[] { CombatWeaponId.Crowbar, CombatWeaponId.Shotgun })
                {
                    yield return SceneManager.LoadSceneAsync(SceneIds.MainMenu, LoadSceneMode.Single);
                    var selection = new CombatLoadoutSelection(secondary, secondary);
                    Assert.That(CombatTestStartService.TryStart(selection), Is.True);
                    yield return AwaitSelectedCombatRange();
                    PlacePair(8f);
                    Assert.That(root.SetOpponentFocus(false), Is.True);
                    root.SendMessage("OnApplicationFocus", true);
                    GameInput.HandleApplicationFocus(true);
                    yield return null; // Let the rig and its later cloth pass settle after changing focus.
                    var hero = root.Hero;
                    var visual = (Player3DCharacterPresentation)root.Player.Visual;
                    GameObject pistol = hero.CarriedWeapon(CombatWeaponId.Pistol);
                    GameObject longWeapon = hero.CarriedWeapon(secondary);
                    IFirearmState pistolState = hero.CarriedFirearm(CombatWeaponId.Pistol);
                    Assert.That(hero.LoadoutSelection, Is.EqualTo(selection));
                    Assert.That(pistol != null && longWeapon != null && pistol != longWeapon, Is.True);
                    Assert.That(hero.CarriedWeapon(secondary == CombatWeaponId.Crowbar ?
                        CombatWeaponId.Shotgun : CombatWeaponId.Crowbar), Is.Null);
                    Assert.That(hero.TrySelectWeapon(secondary == CombatWeaponId.Crowbar ?
                        CombatWeaponId.Shotgun : CombatWeaponId.Crowbar), Is.False);
                    Assert.That(hero.Weapon, Is.SameAs(longWeapon));
                    Assert.That(root.Projectiles, Is.Not.Null, "The initially holstered pistol still needs live projectile support.");
                    AssertStowedCarry(hero, CombatWeaponId.Pistol);
                    AssertShotgunSling(hero);
                    CaptureWeaponCarryViews(secondary + "-start", true);
                    AssertPistolConcealedByJacket(hero);
                    if (secondary == CombatWeaponId.Crowbar)
                    {
                        var wardrobe = hero.GetComponentInChildren<PlayerWardrobe>();
                        Assert.That(wardrobe, Is.Not.Null);
                        PlayerWardrobe.OutfitSnapshot outfit = wardrobe.CaptureOutfit();
                        try
                        {
                            wardrobe.SetSlot("jacket", null);
                            root.Tick(1f / 60f);
                            yield return null;
                            AssertConcealedRenderers(pistol, false);
                            AssertConcealedRenderers(hero.CarryMount(CombatWeaponId.Pistol).parent.gameObject, false);
                            Assert.That(hero.CarriedWeapon(CombatWeaponId.Pistol), Is.SameAs(pistol));
                        }
                        finally { wardrobe.RestoreOutfit(outfit); }
                        root.Tick(1f / 60f);
                        yield return null;
                        AssertPistolConcealedByJacket(hero);
                    }

                    // Exercise the actual shared keyboard action rather than calling the actor only.
                    root.AutomaticSimulation = true;
                    input.Press(keyboard.digit1Key, queueEventOnly: true);
                    yield return null;
                    input.Release(keyboard.digit1Key, queueEventOnly: true);
                    yield return null;
                    root.AutomaticSimulation = false;
                    Assert.That(hero.IsSwitchingWeapon, Is.True, "Key 1 starts the physical pistol draw sequence.");
                    Assert.That(hero.TryAttack(), Is.False);
                    Assert.That(hero.RequestPistolShot(), Is.False);
                    root.Tick(.2f);
                    float pausedProgress = hero.WeaponCarryActionProgress;
                    Vector3 pausedPistol = pistol.transform.position;
                    CombatShotgunSling pausedSling = longWeapon.GetComponent<CombatShotgunSling>();
                    Vector3[] pausedFabric = pausedSling == null ? null : new Vector3[pausedSling.CenterlinePointCount];
                    if (pausedFabric != null)
                        for (int i = 0; i < pausedFabric.Length; i++) pausedFabric[i] = pausedSling.WorldCenterlinePoint(i);
                    Assert.That(root.PauseMenu.Open(), Is.True);
                    root.Tick(1f);
                    yield return null;
                    Assert.That(hero.WeaponCarryActionProgress, Is.EqualTo(pausedProgress));
                    Assert.That(pistol.transform.position, Is.EqualTo(pausedPistol));
                    if (pausedFabric != null)
                        for (int i = 0; i < pausedFabric.Length; i++)
                            Assert.That(pausedSling.WorldCenterlinePoint(i), Is.EqualTo(pausedFabric[i]), "Pause freezes the soft fabric as well as the gun.");
                    Assert.That(hero.TrySelectWeapon(secondary), Is.False);
                    Assert.That(root.PauseMenu.Cancel(), Is.True);
                    yield return WaitFor(() => GameInput.CanRead(GameInputContext.Gameplay), "Carry pause did not release input.");
                    yield return FinishWeaponCarry(CombatWeaponId.Pistol, secondary + "-to-pistol");
                    Assert.That(hero.Weapon, Is.SameAs(pistol));
                    Assert.That(hero.CarriedFirearm(CombatWeaponId.Pistol), Is.SameAs(pistolState));
                    AssertStowedCarry(hero, secondary);
                    if (secondary == CombatWeaponId.Shotgun)
                    {
                        AssertShotgunFrontRoute(hero);
                        yield return AssertShotgunFabricMoves(hero);
                        AssertShotgunFrontRoute(hero);
                    }

                    // Spend a real shot through the actor and live pool, then verify that the
                    // same state remains attached to the pistol after both directions of switching.
                    int beforeShot = pistolState.Rounds;
                    hero.SetPistolAim(true, new Vector3(0f, 1.4f, 20f));
                    root.Tick(.65f);
                    Assert.That(hero.RequestPistolShot(), Is.True);
                    root.Tick(CombatTestRoot.SimulationStep);
                    Assert.That(pistolState.Rounds, Is.EqualTo(beforeShot - 1));
                    int shotSequence = pistolState.ShotSequence;
                    root.Tick(.7f);
                    hero.SetPistolAim(false);
                    root.Tick(.45f);
                    root.AutomaticSimulation = true;
                    input.Press(keyboard.digit2Key, queueEventOnly: true);
                    yield return null;
                    input.Release(keyboard.digit2Key, queueEventOnly: true);
                    yield return null;
                    root.AutomaticSimulation = false;
                    Assert.That(hero.IsSwitchingWeapon, Is.True, "Key 2 returns to the equipped long weapon.");
                    yield return FinishWeaponCarry(secondary, secondary + "-from-pistol",
                        secondary == CombatWeaponId.Crowbar ? 1.8f : 2.5f);
                    Assert.That(hero.Weapon, Is.SameAs(longWeapon));
                    Assert.That(pistolState.Rounds, Is.EqualTo(beforeShot - 1));
                    Assert.That(pistolState.ShotSequence, Is.EqualTo(shotSequence));
                    if (secondary == CombatWeaponId.Shotgun)
                    {
                        hero.SetPistolAim(true, new Vector3(0f, 1.4f, 20f));
                        root.Tick(.65f);
                        Assert.That(hero.RequestPistolShot(), Is.True);
                        root.Tick(CombatTestRoot.SimulationStep);
                        Assert.That(hero.Shotgun.Rounds, Is.EqualTo(1));
                        root.Tick(.7f);
                        hero.SetPistolAim(false);
                        root.Tick(.45f);
                    }

                    // A floor prop keeps its own identity while the other slot is active.
                    hero.SendMessage("DropWeapon", SendMessageOptions.RequireReceiver);
                    Assert.That(hero.CarriedWeaponLocation(secondary), Is.EqualTo(CombatWeaponCarryLocation.Dropped));
                    Assert.That(hero.TrySelectWeapon(secondary), Is.False);
                    var pickup = longWeapon.GetComponent<CombatDroppedWeaponPickup>();
                    Assert.That(pickup, Is.Not.Null);
                    string pickupPrompt = pickup.PromptKey;
                    Assert.That(hero.TrySelectWeapon(CombatWeaponId.Pistol), Is.True);
                    TestContext.WriteLine(secondary + " carry: draw pistol after dropping secondary.");
                    yield return FinishWeaponCarry(CombatWeaponId.Pistol, null);
                    Assert.That(pickup.PromptKey, Is.EqualTo(pickupPrompt), "Changing the held slot cannot rename a floor prop.");
                    Assert.That(hero.CarriedWeaponLocation(secondary), Is.EqualTo(CombatWeaponCarryLocation.Dropped));
                    Assert.That(longWeapon.transform.IsChildOf(hero.transform), Is.False);
                    Assert.That(pistolState.Rounds, Is.EqualTo(beforeShot - 1));

                    // The shared screen reclaims the exact floor record even while
                    // the other weapon is in hand. Cancelling cannot equip it.
                    Rigidbody droppedBody = longWeapon.GetComponent<Rigidbody>();
                    droppedBody.linearVelocity = droppedBody.angularVelocity = Vector3.zero;
                    Vector3 stand = longWeapon.transform.position + Vector3.right * .75f;
                    stand.y = hero.transform.position.y;
                    root.Player.Motor.Teleport(stand);
                    Physics.SyncTransforms();
                    yield return null;
                    Assert.That(pickup.TryBeginPickup(root.Player.Interactor), Is.True);
                    WorldItemFoundScreen found = WorldItemFoundScreen.For(root.Player.Interactor);
                    yield return WaitFor(() => found.IsShowing, "The dropped slot did not reach the shared pickup panel.");
                    found.Abandon();
                    yield return null;
                    Assert.That(hero.CarriedWeaponLocation(secondary), Is.EqualTo(CombatWeaponCarryLocation.Dropped));
                    Assert.That(hero.Weapon, Is.SameAs(pistol));
                    yield return WaitFor(() => GameInput.CanRead(GameInputContext.Gameplay), "Cancelled pickup kept its input lock.");
                    Assert.That(pickup.TryBeginPickup(root.Player.Interactor), Is.True);
                    yield return WaitFor(() => found.IsShowing, "The same floor record could not be inspected again.");
                    yield return null;
                    Assert.That(found.Confirm(), Is.True);
                    yield return WaitFor(() => !found.IsPresenting, "Accepted pickup kept its presentation ownership.");
                    yield return null;
                    TestContext.WriteLine(secondary + " carry: draw recovered secondary after pickup.");
                    yield return FinishWeaponCarry(secondary, null);
                    Assert.That(hero.Weapon, Is.SameAs(longWeapon));
                    Assert.That(pistolState.Rounds, Is.EqualTo(beforeShot - 1));
                    if (secondary == CombatWeaponId.Shotgun) Assert.That(hero.Shotgun.Rounds, Is.EqualTo(1));

                    // Reset reclaims the two originals, including the detached prop.
                    root.ResetRound();
                    Assert.That(hero.LoadoutSelection, Is.EqualTo(selection));
                    Assert.That(hero.Weapon, Is.SameAs(longWeapon));
                    Assert.That(hero.CarriedWeapon(CombatWeaponId.Pistol), Is.SameAs(pistol));
                    Assert.That(hero.CarriedWeaponLocation(secondary), Is.EqualTo(CombatWeaponCarryLocation.Held));
                    Assert.That(pistolState.Rounds, Is.EqualTo(beforeShot));
                    AssertStowedCarry(hero, CombatWeaponId.Pistol);

                    // The gamepad uses the same selection path. A later request is queued
                    // until the visible action reaches a stable endpoint.
                    root.AutomaticSimulation = true;
                    input.Press(gamepad.dpad.right, queueEventOnly: true);
                    yield return null;
                    input.Release(gamepad.dpad.right, queueEventOnly: true);
                    yield return null;
                    root.AutomaticSimulation = false;
                    Assert.That(hero.IsSwitchingWeapon, Is.True);
                    Assert.That(hero.TrySelectWeapon(secondary), Is.True);
                    TestContext.WriteLine(secondary + " carry: queued round-trip after reset.");
                    yield return FinishWeaponCarry(secondary, null);
                    Assert.That(hero.Weapon, Is.SameAs(longWeapon));

                    Assert.That(hero.TrySelectWeapon(CombatWeaponId.Pistol), Is.True);
                    root.Tick(.3f);
                    hero.enabled = false;
                    Assert.That(hero.IsSwitchingWeapon, Is.False);
                    Assert.That(visual.OwnsClip(hero), Is.False, "Disable releases only the carry owner's presentation.");
                    hero.enabled = true;
                    root.ResetRound();
                    Assert.That(hero.Weapon, Is.SameAs(longWeapon));
                    AssertStowedCarry(hero, CombatWeaponId.Pistol);
                    root.AutomaticSimulation = false;
                    if (secondary == CombatWeaponId.Shotgun)
                    {
                        var rig = hero.GetComponentInChildren<Player3DAssetRegistry>();
                        foreach (Transform transientHand in new[] { rig.Anchors.RightGrip, rig.Anchors.LeftGrip })
                        {
                            Assert.That(hero.TrySelectWeapon(CombatWeaponId.Pistol), Is.True);
                            yield return FinishWeaponCarry(CombatWeaponId.Pistol, null);
                            IFirearmState shotgunState = hero.CarriedFirearm(CombatWeaponId.Shotgun);
                            int rounds = shotgunState.Rounds;
                            Assert.That(hero.TrySelectWeapon(CombatWeaponId.Shotgun), Is.True);
                            int seek = 0;
                            for (; seek < 240 && longWeapon.transform.parent != transientHand; seek++)
                            {
                                root.Tick(1f / 60f);
                                yield return null;
                            }
                            Assert.That(longWeapon.transform.parent, Is.SameAs(transientHand),
                                "Both temporary physical holds must be reachable before the firing grip.");
                            Vector3 position = longWeapon.transform.position;
                            Quaternion rotation = longWeapon.transform.rotation;
                            hero.enabled = false;
                            Assert.That(hero.CarriedWeaponLocation(CombatWeaponId.Shotgun), Is.EqualTo(CombatWeaponCarryLocation.Dropped));
                            Assert.That(Vector3.Distance(longWeapon.transform.position, position), Is.LessThan(.001f));
                            Assert.That(Quaternion.Angle(longWeapon.transform.rotation, rotation), Is.LessThan(.1f),
                                "Interrupted palm/catch ownership releases at its live pose without a hidden grip snap.");
                            Assert.That(longWeapon.GetComponent<CombatDroppedWeaponPickup>(), Is.Not.Null);
                            Assert.That(hero.CarriedFirearm(CombatWeaponId.Shotgun), Is.SameAs(shotgunState));
                            Assert.That(shotgunState.Rounds, Is.EqualTo(rounds));
                            Assert.That(visual.OwnsClip(hero), Is.False);
                            hero.enabled = true;
                            root.ResetRound();
                            Assert.That(hero.Weapon, Is.SameAs(longWeapon));
                            AssertStowedCarry(hero, CombatWeaponId.Pistol);
                            yield return null;
                        }
                    }
                }
            }
            finally
            {
                if (keyboard != null && keyboard.added) InputSystem.RemoveDevice(keyboard);
                if (gamepad != null && gamepad.added) InputSystem.RemoveDevice(gamepad);
                input.TearDown();
            }
        }

        private static void AssertStowedCarry(CombatActor hero, CombatWeaponId id)
        {
            GameObject model = hero.CarriedWeapon(id);
            Assert.That(hero.CarriedWeaponLocation(id), Is.EqualTo(CombatWeaponCarryLocation.Stowed));
            Assert.That(model.transform.parent, Is.SameAs(hero.CarryMount(id)));
            Assert.That(Vector3.Distance(model.transform.position, hero.CarryMount(id).position), Is.LessThan(.001f));
            Assert.That(model.activeInHierarchy, Is.True);
            Assert.That(model.GetComponent<Rigidbody>().isKinematic, Is.True);
            Assert.That(model.GetComponent<Rigidbody>().detectCollisions, Is.False);
            Transform dock = hero.CarryMount(id);
            TestContext.WriteLine("Stowed " + id + " actor-local dock " +
                hero.transform.InverseTransformPoint(dock.position).ToString("F6") + " rotation " +
                (Quaternion.Inverse(hero.transform.rotation) * dock.rotation).ToString("F6"));
        }

        private IEnumerator FinishWeaponCarry(CombatWeaponId expected, string capture, float? maximumSeconds = null)
        {
            var hero = root.Hero;
            GameObject pistol = hero.CarriedWeapon(CombatWeaponId.Pistol);
            GameObject secondary = hero.CarriedWeapon(hero.LoadoutSelection.Secondary);
            int transfers = 0;
            int shotgunDrawTransfers = 0;
            bool middleCaptured = false, pistolAccessCaptured = false, shotgunAboveCaptured = false, shotgunTurnCaptured = false;
            int frame = 0;
            for (; frame < 600 && (hero.IsSwitchingWeapon || !hero.HasWeaponInHand || hero.WeaponId != expected); frame++)
            {
                Transform pistolParent = pistol.transform.parent, secondaryParent = secondary.transform.parent;
                Vector3 pistolPosition = pistol.transform.position, secondaryPosition = secondary.transform.position;
                root.Tick(1f / 60f);
                yield return null;
                AssertShotgunSling(hero);
                if (pistol.transform.parent != pistolParent || secondary.transform.parent != secondaryParent)
                {
                    GameObject transferred = pistol.transform.parent != pistolParent ? pistol : secondary;
                    Vector3 before = transferred == pistol ? pistolPosition : secondaryPosition;
                    Assert.That(Vector3.Distance(before, transferred.transform.position), Is.LessThan(.1f),
                        "A body/hand parent transfer must retain the visible metric pose.");
                    if (transferred == pistol)
                        AssertConcealedRenderers(pistol,
                            hero.CarriedWeaponLocation(CombatWeaponId.Pistol) == CombatWeaponCarryLocation.Stowed);
                    else if (secondary == hero.CarriedWeapon(CombatWeaponId.Shotgun) && hero.WeaponCarryActionIsDraw)
                    {
                        shotgunDrawTransfers++;
                        var rig = hero.GetComponentInChildren<Player3DAssetRegistry>();
                        var hands = hero.GetComponentInChildren<NpcHandPose>();
                        Assert.That(transferred.transform.parent, Is.SameAs(shotgunDrawTransfers == 2
                            ? rig.Anchors.LeftGrip : rig.Anchors.RightGrip),
                            "The original shotgun passes from the right barrel push through the left upper catch into its firing grip.");
                        Assert.That(hero.HasWeaponInHand, Is.True, "Physical palm and catch ownership must retain the live held weapon.");
                        if (shotgunDrawTransfers == 1)
                        {
                            Vector3 palm = hands.CylinderCentre(false);
                            Vector3 contact = transferred.transform.position + transferred.transform.rotation * new Vector3(.020f, .140f, .300f);
                            Vector3 barrelAxis = transferred.transform.position + transferred.transform.rotation * new Vector3(.020f, .110f, .300f);
                            Assert.That(Vector3.Distance(palm, contact), Is.LessThan(.03f),
                                "The right palm reaches the lower barrel surface instead of twisting to the trigger behind the shoulder.");
                            Assert.That(Vector3.Dot(palm - barrelAxis, transferred.transform.up), Is.GreaterThan(.005f),
                                "The palm contacts the underside of the hanging barrels.");
                            Assert.That(Vector3.Dot(hands.PalmNormal(false), -transferred.transform.up), Is.GreaterThan(.90f),
                                "The open right palm pushes inward against the underside of the barrels.");
                            Assert.That(hands.RightGripWeight, Is.LessThan(.25f), "A barrel push keeps the palm open.");
                        }
                    }
                    transfers++;
                    if (capture != null) CaptureWeaponCarryViews(capture + "-contact-" + transfers, true);
                }
                if (!middleCaptured && hero.IsSwitchingWeapon && hero.WeaponCarryActionProgress > .25f)
                {
                    if (capture != null) CaptureWeaponCarryViews(capture + "-reach", false);
                    middleCaptured = true;
                }
                if (capture != null && hero.IsSwitchingWeapon && hero.WeaponCarryActionWeapon == CombatWeaponId.Shotgun &&
                    hero.WeaponCarryActionIsDraw)
                {
                    float authoredSeconds = hero.WeaponCarryActionProgress *
                        CombatWeaponCarryAssetProvider.PhaseDuration(CombatWeaponId.Shotgun, true);
                    if (!shotgunAboveCaptured && authoredSeconds >= .58f)
                    {
                        CaptureWeaponCarryViews(capture + "-above-stock", true);
                        shotgunAboveCaptured = true;
                    }
                    if (!shotgunTurnCaptured && authoredSeconds >= 1.15f)
                    {
                        CaptureWeaponCarryViews(capture + "-turn", true);
                        shotgunTurnCaptured = true;
                    }
                }
                if (!pistolAccessCaptured && capture != null && hero.IsSwitchingWeapon &&
                    hero.WeaponCarryActionWeapon == CombatWeaponId.Pistol && hero.WeaponCarryActionIsDraw &&
                    hero.WeaponCarryActionProgress > .43f)
                {
                    CaptureWeaponCarryViews(capture + "-jacket-access", true);
                    pistolAccessCaptured = true;
                }
                if (hero.IsSwitchingWeapon)
                {
                    Assert.That(hero.TryAttack(), Is.False);
                    Assert.That(hero.RequestPistolShot(), Is.False);
                }
            }
            Assert.That(hero.IsSwitchingWeapon, Is.False, "The switch must leave its terminal pose and release action ownership.");
            if (maximumSeconds.HasValue)
            {
                float elapsed = frame / 60f;
                TestContext.WriteLine("Pistol to " + expected + " switch completed in " + elapsed.ToString("F3") + " simulation seconds.");
                Assert.That(elapsed, Is.LessThanOrEqualTo(maximumSeconds.Value),
                    "The full stow/draw switch must return control within its responsive gameplay budget.");
            }
            Assert.That(hero.HasWeaponInHand, Is.True);
            Assert.That(hero.WeaponId, Is.EqualTo(expected));
            if (expected != CombatWeaponId.Pistol) AssertPistolConcealedByJacket(hero);
            else AssertConcealedRenderers(pistol, false);
            if (capture != null)
            {
                Assert.That(transfers, Is.EqualTo(expected == CombatWeaponId.Shotgun ? 4 : 2),
                    "A full switch retains its originals, including the shotgun's push, catch and final grip transfers.");
                if (expected == CombatWeaponId.Shotgun) Assert.That(shotgunDrawTransfers, Is.EqualTo(3));
                CaptureWeaponCarryViews(capture + "-ready", true);
            }
        }

        private void CaptureWeaponCarryViews(string name, bool includeSides)
        {
            Camera camera = root.CameraFollow.Camera;
            Vector3 position = camera.transform.position;
            Quaternion rotation = camera.transform.rotation;
            float fov = camera.fieldOfView;
            try
            {
                Capture("gameplay");
                if (!includeSides) return;
                Player3DAssetRegistry rig = ((Player3DCharacterPresentation)root.Player.Visual).Registry;
                Vector3 right = (CityPedestrianHandProps.FindSocket(rig.Animator.transform, "upper_arm.R").position -
                    CityPedestrianHandProps.FindSocket(rig.Animator.transform, "upper_arm.L").position).normalized;
                Vector3 up = (rig.Anchors.Head.position - rig.Anchors.Chest.position).normalized;
                Vector3 forward = Vector3.Cross(right, up).normalized;
                Vector3 frontFocus = root.Hero.transform.position + Vector3.up * 1.10f;
                camera.transform.SetPositionAndRotation(frontFocus + forward * 2.75f + Vector3.up * .15f,
                    Quaternion.LookRotation(-forward * 2.75f - Vector3.up * .15f));
                camera.fieldOfView = 43f;
                Capture("front");
                if (name.StartsWith("Shotgun", System.StringComparison.Ordinal) &&
                    (name.EndsWith("-ready", System.StringComparison.Ordinal) ||
                     name == "Shotgun-front-garment-occlusion")) CaptureSurface("front-surface");
                foreach (bool rear in new[] { false, true })
                {
                    Vector3 focus = root.Hero.transform.position + Vector3.up * 1.05f;
                    Vector3 eye = root.Hero.transform.TransformPoint(rear ?
                        new Vector3(1.65f, 1.4f, -2.3f) : new Vector3(-1.65f, 1.3f, 2.3f));
                    camera.transform.SetPositionAndRotation(eye, Quaternion.LookRotation(focus - eye));
                    camera.fieldOfView = 43f;
                    Capture(rear ? "back" : "left");
                    if (!rear && name.EndsWith("-start", System.StringComparison.Ordinal))
                        CaptureSurface("left-surface");
                }
                if (name.EndsWith("-start", System.StringComparison.Ordinal) ||
                    name.EndsWith("-ready", System.StringComparison.Ordinal))
                {
                    foreach (float side in new[] { -1f, 1f })
                    {
                        Vector3 focus = root.Hero.transform.position + Vector3.up * 1.12f;
                        Vector3 eye = root.Hero.transform.TransformPoint(new Vector3(side * 2.5f, 1.35f, 0f));
                        camera.transform.SetPositionAndRotation(eye, Quaternion.LookRotation(focus - eye));
                        camera.fieldOfView = 43f;
                        Capture(side < 0f ? "left-profile" : "right-profile");
                    }
                }
            }
            finally { camera.transform.SetPositionAndRotation(position, rotation); camera.fieldOfView = fov; }

            void Capture(string view)
            {
                LogAssert.Expect(LogType.Log, new System.Text.RegularExpressions.Regex("^Area capture wrote "));
                AreaCaptureFixture.CaptureCurrentCamera(camera, SceneIds.CombatTest + "/WeaponCarry", name + "-" + view);
            }

            void CaptureSurface(string view)
            {
                // Inspect cloth occlusion independently of cast shadows.
                Light[] lights = UnityEngine.Object.FindObjectsByType<Light>();
                var shadows = new LightShadows[lights.Length];
                try
                {
                    for (int i = 0; i < lights.Length; i++)
                    { shadows[i] = lights[i].shadows; lights[i].shadows = LightShadows.None; }
                    Capture(view);
                }
                finally
                {
                    for (int i = 0; i < lights.Length; i++) lights[i].shadows = shadows[i];
                }
            }
        }

        private void AssertShotgunSling(CombatActor hero)
        {
            GameObject shotgun = hero.CarriedWeapon(CombatWeaponId.Shotgun);
            if (shotgun == null) return;
            CombatShotgunSling sling = shotgun.GetComponent<CombatShotgunSling>();
            Assert.That(sling, Is.Not.Null, "The same shotgun carries its own sling.");
            Assert.That(sling.Ribbon.transform.IsChildOf(shotgun.transform), Is.True,
                "The strap travels with the original weapon through hand, back and floor ownership.");
            float lengthError = Mathf.Abs(sling.PresentedLength - sling.AuthoredLength);
            if (lengthError < .03f) return;
            if (lengthError >= .03f)
                CaptureWeaponCarryViews("Shotgun-fabric-contact-failure", true);
            Vector3 contactFromGrip = sling.MaximumContactErrorWorldPoint - sling.HandTargetWorld;
            Assert.That(lengthError, Is.LessThan(.03f),
                "The shoulder/hand route must fit the real strap without stretching it. " +
                hero.WeaponCarryActionWeapon + (hero.WeaponCarryActionIsDraw ? " draw " : " stow ") +
                hero.WeaponCarryActionProgress.ToString("F4") + "; " +
                hero.CarriedWeaponLocation(CombatWeaponId.Shotgun) + "; length " + sling.PresentedLength.ToString("F4") +
                "; link/contact " + sling.MaximumLinkError.ToString("F5") + "/" + sling.MaximumContactError.ToString("F5") +
                "; shoulder/hand " + sling.ShoulderAttachmentAmount.ToString("F3") + "/" + sling.LeftHandAttachmentAmount.ToString("F3") +
                "; worst link " + sling.MaximumLinkErrorIndex + " at " +
                hero.transform.InverseTransformPoint(sling.MaximumLinkErrorWorldCentre).ToString("F4") +
                "; worst contact " + sling.MaximumContactErrorIndex + " at " +
                hero.transform.InverseTransformPoint(sling.MaximumContactErrorWorldPoint).ToString("F4") +
                "; direct/material " + sling.StockToHandDistance.ToString("F4") + "/" + sling.StockToHandMaterialLength.ToString("F4") +
                ", " + sling.HandToForeendDistance.ToString("F4") + "/" + sling.HandToForeendMaterialLength.ToString("F4") +
                "; stock/foreend contact " + sling.StockBodyContactError.ToString("F4") + "/" + sling.ForeendBodyContactError.ToString("F4") +
                "; presented spans " + sling.PresentedStockToHandLength.ToString("F4") + "/" + sling.PresentedHandToForeendLength.ToString("F4") +
                "; hard grasp contact " + sling.HardGripBodyContactError.ToString("F4") +
                "; grasp " + hero.transform.InverseTransformPoint(sling.WorldCenterlinePoint(sling.HandCenterlineIndex)).ToString("F4") +
                "; contact grasp axial/radial " + Vector3.Dot(contactFromGrip, sling.HandAxisWorld).ToString("F4") + "/" +
                Vector3.ProjectOnPlane(contactFromGrip, sling.HandAxisWorld).magnitude.ToString("F4"));
        }

        private IEnumerator AssertShotgunFabricMoves(CombatActor hero)
        {
            CombatShotgunSling sling = hero.CarriedWeapon(CombatWeaponId.Shotgun).GetComponent<CombatShotgunSling>();
            Assert.That(sling.CenterlinePointCount, Is.EqualTo(81));
            Vector3 origin = hero.transform.position;
            var before = new Vector3[sling.CenterlinePointCount];
            for (int i = 0; i < before.Length; i++)
                before[i] = hero.transform.InverseTransformPoint(sling.WorldCenterlinePoint(i));
            float movement = 0f;
            try
            {
                // A short real movement stays below teleport-reset distance.
                // A rigid actor-local arch would retain every local point.
                hero.transform.position += hero.transform.right * .035f;
                Physics.SyncTransforms();
                for (int frame = 0; frame < 24; frame++)
                {
                    root.Tick(1f / 60f);
                    yield return null;
                    AssertShotgunSling(hero);
                    for (int i = 1; i < before.Length - 1; i++)
                    {
                        Vector3 local = hero.transform.InverseTransformPoint(sling.WorldCenterlinePoint(i));
                        Assert.That(float.IsNaN(local.x + local.y + local.z), Is.False);
                        movement = Mathf.Max(movement, Vector3.Distance(before[i], local));
                    }
                }
                Assert.That(movement, Is.GreaterThan(.0008f),
                    "Soft webbing must settle and lag behind movement rather than remain a rigid local shape.");
                Assert.That(movement, Is.LessThan(.25f), "Fabric inertia must remain bounded around the shoulder and gun.");
            }
            finally { hero.transform.position = origin; Physics.SyncTransforms(); }
            yield return null;
        }

        private void AssertShotgunFrontRoute(CombatActor hero)
        {
            CombatShotgunSling sling = hero.CarriedWeapon(CombatWeaponId.Shotgun).GetComponent<CombatShotgunSling>();
            Player3DAssetRegistry rig = ((Player3DCharacterPresentation)root.Player.Visual).Registry;
            Vector3 leftShoulder = CityPedestrianHandProps.FindSocket(rig.Animator.transform, "upper_arm.L").position;
            Vector3 rightShoulder = CityPedestrianHandProps.FindSocket(rig.Animator.transform, "upper_arm.R").position;
            Vector3 right = (rightShoulder - leftShoulder).normalized;
            Vector3 up = (rig.Anchors.Head.position - rig.Anchors.Chest.position).normalized;
            Vector3 forward = Vector3.Cross(right, up).normalized;
            float leftDistance = float.MaxValue, rightDistance = float.MaxValue;
            float frontLow = float.MaxValue, frontHigh = float.MinValue;
            int upperLeft = -1, lowerRight = -1;
            for (int i = 0; i < sling.CenterlinePointCount; i++)
            {
                Vector3 point = sling.WorldCenterlinePoint(i);
                leftDistance = Mathf.Min(leftDistance, Vector3.Distance(point, leftShoulder));
                rightDistance = Mathf.Min(rightDistance, Vector3.Distance(point, rightShoulder));
                Vector3 relative = point - rig.Anchors.Chest.position;
                float height = Vector3.Dot(relative, up), side = Vector3.Dot(relative, right);
                if (Vector3.Dot(relative, forward) < .075f) continue;
                frontLow = Mathf.Min(frontLow, height); frontHigh = Mathf.Max(frontHigh, height);
                if (upperLeft < 0 && side < -.025f && height > -.04f) upperLeft = i;
                if (upperLeft >= 0 && lowerRight < 0 && side > .04f && height < -.13f) lowerRight = i;
            }
            float frontDepth = float.MaxValue;
            for (int i = upperLeft; i >= 0 && i <= lowerRight; i++)
                frontDepth = Mathf.Min(frontDepth, Vector3.Dot(sling.WorldCenterlinePoint(i) - rig.Anchors.Chest.position, forward));
            TestContext.WriteLine($"Shotgun front route: shoulder L/R {leftDistance:F4}/{rightDistance:F4}; front heights {frontLow:F4}/{frontHigh:F4}; diagonal {upperLeft}/{lowerRight}.");
            if (upperLeft < 0 || lowerRight < 0 || frontDepth < .055f)
                CaptureWeaponCarryViews("Shotgun-front-route-failure", true);
            Assert.That(leftDistance, Is.LessThan(.115f), "The fabric loop must pass over the anatomical left shoulder.");
            Assert.That(leftDistance, Is.LessThan(rightDistance), "The support must remain on the left side of the hero.");
            Assert.That(upperLeft >= 0 && lowerRight > upperLeft, Is.True,
                "The same continuous fabric must cross from the left upper chest to the right abdomen in front of the coat.");
            Assert.That(frontDepth, Is.GreaterThan(.055f), "The full chest-to-abdomen section must remain in front of the coat.");
            Assert.That(frontHigh - frontLow, Is.GreaterThan(.25f), "The visible front diagonal must span the torso rather than sit beside the shoulder.");
            AssertShotgunRibbonAboveJacket(sling, rig, right, up, forward, upperLeft, lowerRight);
        }

        private void AssertShotgunRibbonAboveJacket(CombatShotgunSling sling, Player3DAssetRegistry rig,
            Vector3 right, Vector3 up, Vector3 forward, int first, int last)
        {
            // Independent visible-surface oracle: live cloth and outer patch
            // layers, not the solver's source-body hull or centreline depth.
            Vector3 origin = rig.Anchors.Chest.position;
            Vector3 Project(Vector3 point)
            {
                point -= origin;
                return new Vector3(Vector3.Dot(point, right), Vector3.Dot(point, up), Vector3.Dot(point, forward));
            }
            var triangles = new System.Collections.Generic.List<(Vector3 A, Vector3 B, Vector3 C, string Part, int Index)>();
            var baked = new Mesh { name = "Test visible jacket front" };
            try
            {
                foreach (Player3DMeshBinding binding in rig.MeshBindings)
                {
                    string name = binding.MeshName;
                    if (name != "CLO_JacketBody" && !name.StartsWith("CLO_JacketPocket", System.StringComparison.Ordinal) &&
                        !name.StartsWith("CLO_JacketFlap", System.StringComparison.Ordinal) &&
                        !name.StartsWith("CLO_JacketPlacket", System.StringComparison.Ordinal)) continue;
                    if (!(binding.Renderer is SkinnedMeshRenderer skin) || !skin.enabled || skin.forceRenderingOff ||
                        !skin.gameObject.activeInHierarchy || skin.sharedMesh == null) continue;
                    skin.BakeMesh(baked, true);
                    Vector3[] positions = baked.vertices;
                    for (int i = 0; i < positions.Length; i++) positions[i] = Project(skin.transform.TransformPoint(positions[i]));
                    int[] indices = baked.triangles;
                    for (int i = 0; i < indices.Length; i += 3)
                        triangles.Add((positions[indices[i]], positions[indices[i + 1]], positions[indices[i + 2]], name, i / 3));
                }
            }
            finally { UnityEngine.Object.DestroyImmediate(baked); }
            Assert.That(triangles.Count, Is.GreaterThan(0), "The front oracle needs the current visible M65 surfaces.");

            Mesh ribbon = sling.Ribbon.sharedMesh;
            Vector3[] vertices = ribbon.vertices;
            Vector2[] coordinates = ribbon.uv;
            Assert.That(coordinates.Length, Is.EqualTo(vertices.Length));
            float minimumGap = float.PositiveInfinity;
            int checkedCorners = 0, matchedCorners = 0, worstVertex = -1, worstTriangle = -1;
            string worstPart = "";
            for (int vertex = 0; vertex < vertices.Length; vertex++)
            {
                // UV.x is the authored material coordinate. Import may split
                // shared corners for normals; every actual corner is retained.
                float sample = Mathf.Clamp01(coordinates[vertex].x) * (sling.CenterlinePointCount - 1);
                if (sample < first - .0001f || sample > last + .0001f) continue;
                checkedCorners++;
                Vector3 point = Project(sling.Ribbon.transform.TransformPoint(vertices[vertex]));
                float highest = float.NegativeInfinity;
                string part = "";
                int triangleIndex = -1;
                foreach (var triangle in triangles)
                {
                    Vector3 ab = triangle.B - triangle.A, ac = triangle.C - triangle.A, relative = point - triangle.A;
                    float determinant = ab.x * ac.y - ab.y * ac.x;
                    if (Mathf.Abs(determinant) < .0000000001f) continue;
                    float u = (relative.x * ac.y - relative.y * ac.x) / determinant;
                    float v = (ab.x * relative.y - ab.y * relative.x) / determinant;
                    if (u < -.000001f || v < -.000001f || u + v > 1.000001f) continue;
                    float depth = triangle.A.z + u * ab.z + v * ac.z;
                    if (depth <= highest) continue;
                    highest = depth; part = triangle.Part; triangleIndex = triangle.Index;
                }
                // The open front and silhouette can have no jacket triangle.
                if (float.IsNegativeInfinity(highest)) continue;
                matchedCorners++;
                float gap = point.z - highest;
                if (gap >= minimumGap) continue;
                minimumGap = gap; worstVertex = vertex; worstPart = part; worstTriangle = triangleIndex;
            }
            TestContext.WriteLine($"Shotgun live jacket clearance: minimum {minimumGap:F6}m; ribbon vertex {worstVertex}; " +
                $"coat {worstPart} triangle {worstTriangle}; projected corners {matchedCorners}/{checkedCorners}.");
            Assert.That(checkedCorners, Is.GreaterThan(0));
            Assert.That(matchedCorners, Is.GreaterThan(0), "The full front diagonal must be checked against the actual jacket.");
            if (minimumGap < -.0005f) CaptureWeaponCarryViews("Shotgun-front-garment-occlusion", true);
            Assert.That(minimumGap, Is.GreaterThanOrEqualTo(-.0005f),
                "Every front ribbon corner, including its underside, must remain above the live coat and its outer pockets/placket.");
        }
    }
}

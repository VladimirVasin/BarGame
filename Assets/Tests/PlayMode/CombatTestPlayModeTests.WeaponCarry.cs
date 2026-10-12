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
        private static readonly CombatWeaponId[] CarryWeaponIds =
            { CombatWeaponId.Pistol, CombatWeaponId.Crowbar, CombatWeaponId.Shotgun };

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
                VerifyThreeWeaponPreparation();
                foreach (CombatWeaponId secondary in new[] { CombatWeaponId.Crowbar, CombatWeaponId.Shotgun })
                {
                    yield return SceneManager.LoadSceneAsync(SceneIds.MainMenu, LoadSceneMode.Single);
                    var selection = new CombatLoadoutSelection(secondary);
                    Assert.That(CombatTestStartService.TryStart(selection), Is.True);
                    yield return AwaitSelectedCombatRange();
                    PlacePair(8f);
                    // Keep control shots clear of the opponent so a round-end cannot cancel a carry action.
                    root.Opponent.ResetActor(root.Hero.transform.position + Vector3.right * 8f, Vector3.left);
                    Physics.SyncTransforms();
                    Assert.That(root.SetOpponentFocus(false), Is.True);
                    root.SendMessage("OnApplicationFocus", true);
                    GameInput.HandleApplicationFocus(true);
                    yield return null; // Let the rig and its later cloth pass settle after changing focus.
                    var hero = root.Hero;
                    var visual = (Player3DCharacterPresentation)root.Player.Visual;
                    GameObject pistol = hero.CarriedWeapon(CombatWeaponId.Pistol);
                    GameObject longWeapon = hero.CarriedWeapon(secondary);
                    CombatWeaponId other = secondary == CombatWeaponId.Crowbar ? CombatWeaponId.Shotgun : CombatWeaponId.Crowbar;
                    GameObject otherWeapon = hero.CarriedWeapon(other);
                    IFirearmState pistolState = hero.CarriedFirearm(CombatWeaponId.Pistol);
                    IFirearmState shotgunState = hero.CarriedFirearm(CombatWeaponId.Shotgun);
                    Assert.That(hero.LoadoutSelection, Is.EqualTo(selection));
                    Assert.That(pistol != null && longWeapon != null && otherWeapon != null &&
                        pistol != longWeapon && pistol != otherWeapon && longWeapon != otherWeapon, Is.True);
                    Assert.That(shotgunState, Is.Not.Null, "The stowed shotgun retains its own live firearm state even when the crowbar starts in hand.");
                    Assert.That(hero.Weapon, Is.SameAs(longWeapon));
                    Assert.That(root.Projectiles, Is.Not.Null, "The initially holstered pistol still needs live projectile support.");
                    AssertStowedCarry(hero, CombatWeaponId.Pistol);
                    AssertStowedCarry(hero, other);
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
                    CombatShotgunSling pausedSling = hero.CarriedWeapon(CombatWeaponId.Shotgun).GetComponent<CombatShotgunSling>();
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
                    AssertStowedCarry(hero, other);
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
                    var longWeaponKey = secondary == CombatWeaponId.Crowbar ? keyboard.digit2Key : keyboard.digit3Key;
                    input.Press(longWeaponKey, queueEventOnly: true);
                    yield return null;
                    input.Release(longWeaponKey, queueEventOnly: true);
                    yield return null;
                    root.AutomaticSimulation = false;
                    Assert.That(hero.IsSwitchingWeapon, Is.True, "Keys 2 and 3 select their fixed crowbar and shotgun records.");
                    yield return FinishWeaponCarry(secondary, secondary + "-from-pistol",
                        secondary == CombatWeaponId.Crowbar ? 1.8f : 2.5f);
                    Assert.That(hero.Weapon, Is.SameAs(longWeapon));
                    Assert.That(pistolState.Rounds, Is.EqualTo(beforeShot - 1));
                    Assert.That(pistolState.ShotSequence, Is.EqualTo(shotSequence));
                    AssertStowedCarry(hero, other);
                    if (secondary == CombatWeaponId.Crowbar)
                    {
                        // These direct long-weapon transfers complete all six ordered pairs.
                        root.AutomaticSimulation = true;
                        input.Press(keyboard.digit3Key, queueEventOnly: true);
                        yield return null;
                        input.Release(keyboard.digit3Key, queueEventOnly: true);
                        yield return null;
                        root.AutomaticSimulation = false;
                        yield return FinishWeaponCarry(CombatWeaponId.Shotgun, "Crowbar-to-Shotgun");
                        AssertStowedCarry(hero, CombatWeaponId.Crowbar);
                        hero.SetPistolAim(true, new Vector3(0f, 1.4f, 20f));
                        root.Tick(.65f);
                        Assert.That(hero.RequestPistolShot(), Is.True);
                        root.Tick(CombatTestRoot.SimulationStep);
                        Assert.That(shotgunState.Rounds, Is.EqualTo(1));
                        root.Tick(.7f);
                        hero.SetPistolAim(false);
                        root.Tick(.45f);
                        Assert.That(hero.TrySelectWeapon(CombatWeaponId.Crowbar), Is.True);
                        yield return FinishWeaponCarry(CombatWeaponId.Crowbar, "Shotgun-to-Crowbar");
                        Assert.That(shotgunState.Rounds, Is.EqualTo(1));
                        AssertStowedCarry(hero, CombatWeaponId.Shotgun);
                    }
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
                    Assert.That(hero.CarriedWeapon(other), Is.SameAs(otherWeapon));
                    AssertStowedCarry(hero, other);
                    Assert.That(hero.CarriedFirearm(CombatWeaponId.Shotgun), Is.SameAs(shotgunState));
                    Assert.That(shotgunState.Rounds, Is.EqualTo(1));
                    yield return PressCarryCycle(other);
                    yield return FinishWeaponCarry(other, null);
                    Assert.That(hero.CarriedWeaponLocation(secondary), Is.EqualTo(CombatWeaponCarryLocation.Dropped),
                        "Cycling skips the dropped record and leaves its original floor prop alone.");
                    Assert.That(hero.TrySelectWeapon(CombatWeaponId.Pistol), Is.True);
                    yield return FinishWeaponCarry(CombatWeaponId.Pistol, null);
                    Assert.That(pickup.PromptKey, Is.EqualTo(pickupPrompt));

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

                    // Reset reclaims all three originals, including the detached prop.
                    root.ResetRound();
                    Assert.That(hero.LoadoutSelection, Is.EqualTo(selection));
                    Assert.That(hero.Weapon, Is.SameAs(longWeapon));
                    Assert.That(hero.CarriedWeapon(CombatWeaponId.Pistol), Is.SameAs(pistol));
                    Assert.That(hero.CarriedWeaponLocation(secondary), Is.EqualTo(CombatWeaponCarryLocation.Held));
                    Assert.That(pistolState.Rounds, Is.EqualTo(beforeShot));
                    Assert.That(hero.CarriedWeapon(other), Is.SameAs(otherWeapon));
                    Assert.That(shotgunState.Rounds, Is.EqualTo(2));
                    AssertStowedCarry(hero, CombatWeaponId.Pistol);
                    AssertStowedCarry(hero, other);

                    // The gamepad uses the same selection path. A later request is queued
                    // until the visible action reaches a stable endpoint.
                    CombatWeaponId next = secondary == CombatWeaponId.Crowbar ? CombatWeaponId.Shotgun : CombatWeaponId.Pistol;
                    yield return PressCarryCycle(next);
                    yield return FinishWeaponCarry(next, null);
                    next = next == CombatWeaponId.Shotgun ? CombatWeaponId.Pistol : CombatWeaponId.Crowbar;
                    yield return PressCarryCycle(next);
                    yield return FinishWeaponCarry(next, null);
                    yield return PressCarryCycle(secondary);
                    yield return FinishWeaponCarry(secondary, null);
                    yield return PressCarryCycle(secondary == CombatWeaponId.Crowbar ? CombatWeaponId.Shotgun : CombatWeaponId.Pistol);
                    Assert.That(hero.TrySelectNextWeapon(), Is.True);
                    Assert.That(hero.TrySelectNextWeapon(), Is.True,
                        "Repeated cycles advance the pending choice across all three weapons while a physical switch is active.");
                    TestContext.WriteLine(secondary + " carry: queued round-trip after reset.");
                    yield return FinishWeaponCarry(secondary, null);
                    Assert.That(hero.Weapon, Is.SameAs(longWeapon));
                    AssertStowedCarry(hero, other);

                    Assert.That(hero.TrySelectWeapon(CombatWeaponId.Pistol), Is.True);
                    root.Tick(.3f);
                    hero.enabled = false;
                    Assert.That(hero.IsSwitchingWeapon, Is.False);
                    Assert.That(visual.OwnsClip(hero), Is.False, "Disable releases only the carry owner's presentation.");
                    hero.enabled = true;
                    root.ResetRound();
                    Assert.That(hero.Weapon, Is.SameAs(longWeapon));
                    AssertStowedCarry(hero, CombatWeaponId.Pistol);
                    AssertStowedCarry(hero, other);
                    root.AutomaticSimulation = false;
                    if (secondary == CombatWeaponId.Shotgun)
                    {
                        var rig = hero.GetComponentInChildren<Player3DAssetRegistry>();
                        foreach (Transform transientHand in new[] { rig.Anchors.RightGrip, rig.Anchors.LeftGrip })
                        {
                            Assert.That(hero.TrySelectWeapon(CombatWeaponId.Pistol), Is.True);
                            yield return FinishWeaponCarry(CombatWeaponId.Pistol, null);
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

                    IEnumerator PressCarryCycle(CombatWeaponId expected)
                    {
                        root.AutomaticSimulation = true;
                        input.Press(gamepad.dpad.right, queueEventOnly: true);
                        yield return null;
                        input.Release(gamepad.dpad.right, queueEventOnly: true);
                        yield return null;
                        root.AutomaticSimulation = false;
                        Assert.That(hero.IsSwitchingWeapon, Is.True, "The gamepad cycles Pistol, Crowbar and Shotgun.");
                        TestContext.WriteLine("Carry cycle queued " + expected + ".");
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

        private static void VerifyThreeWeaponPreparation()
        {
            foreach (CombatTestMode mode in new[] { CombatTestMode.Standard, CombatTestMode.Endless })
                foreach (CombatWeaponId initial in CarryWeaponIds)
                {
                    var selection = new CombatTestSelection(new CombatLoadoutSelection(initial), mode);
                    var model = new StartMenuModel();
                    model.OpenCombatPreparation(CombatTestSelection.Default);
                    model.SelectCombatOption(initial switch
                    {
                        CombatWeaponId.Pistol => CombatPreparationOption.Pistol,
                        CombatWeaponId.Shotgun => CombatPreparationOption.Shotgun,
                        _ => CombatPreparationOption.Crowbar
                    });
                    model.SelectCombatOption(mode == CombatTestMode.Endless ?
                        CombatPreparationOption.Endless : CombatPreparationOption.Standard);
                    Assert.That(model.SelectedCombatSelection, Is.EqualTo(selection));
                    foreach (CombatWeaponId id in CarryWeaponIds)
                        Assert.That(model.SelectedCombatLoadout.Contains(id), Is.True,
                            "Preparation chooses the first held weapon while retaining all three.");
                    model.SelectBack(); model.Confirm(); model.Confirm();
                    Assert.That(model.SelectedCombatSelection, Is.EqualTo(selection));
                    model.SelectCombatOption(CombatPreparationOption.Start); model.Confirm();
                    model.OpenCombatPreparation(selection);
                    Assert.That(model.IsCommitted, Is.False);
                    Assert.That(model.SelectedCombatSelection, Is.EqualTo(selection));
                    Assert.That(new CombatLoadoutSelection(CombatWeaponId.Crowbar, initial), Is.EqualTo(selection.Loadout));
                    Assert.That(new CombatLoadoutSelection(CombatWeaponId.Shotgun, initial), Is.EqualTo(selection.Loadout));
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
            var models = new GameObject[CarryWeaponIds.Length];
            var parents = new Transform[CarryWeaponIds.Length];
            var positions = new Vector3[CarryWeaponIds.Length];
            for (int index = 0; index < models.Length; index++) models[index] = hero.CarriedWeapon(CarryWeaponIds[index]);
            int transfers = 0;
            int shotgunDrawTransfers = 0;
            bool middleCaptured = false, pistolAccessCaptured = false, shotgunAboveCaptured = false, shotgunTurnCaptured = false;
            int frame = 0;
            for (; frame < 600 && (hero.IsSwitchingWeapon || !hero.HasWeaponInHand || hero.WeaponId != expected); frame++)
            {
                for (int index = 0; index < models.Length; index++)
                {
                    parents[index] = models[index].transform.parent;
                    positions[index] = models[index].transform.position;
                }
                root.Tick(1f / 60f);
                yield return null;
                AssertShotgunSling(hero);
                for (int index = 0; index < models.Length; index++)
                {
                    GameObject transferred = models[index];
                    Assert.That(hero.CarriedWeapon(CarryWeaponIds[index]), Is.SameAs(transferred));
                    if (transferred.transform.parent == parents[index]) continue;
                    Vector3 before = positions[index];
                    Assert.That(Vector3.Distance(before, transferred.transform.position), Is.LessThan(.1f),
                        "A body/hand parent transfer must retain the visible metric pose.");
                    if (transferred == pistol)
                        AssertConcealedRenderers(pistol,
                            hero.CarriedWeaponLocation(CombatWeaponId.Pistol) == CombatWeaponCarryLocation.Stowed);
                    else if (CarryWeaponIds[index] == CombatWeaponId.Shotgun && hero.WeaponCarryActionIsDraw)
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
            Assert.That(hero.Weapon, Is.SameAs(hero.CarriedWeapon(expected)));
            foreach (CombatWeaponId id in CarryWeaponIds)
                if (id != expected && hero.CarriedWeaponLocation(id) != CombatWeaponCarryLocation.Dropped)
                    AssertStowedCarry(hero, id);
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
                ((Player3DCharacterPresentation)root.Player.Visual).ReapplyLatePresentationPose();
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

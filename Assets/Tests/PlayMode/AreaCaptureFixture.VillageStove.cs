using System.Collections;
using System.IO;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.TestTools;

namespace BarPromenade.Tests.PlayMode
{
    public sealed partial class AreaCaptureFixture
    {
        [UnityTest]
        [Explicit("Mother's house woodpile placement and shared firewood capacity.")]
        [PrebuildSetup(typeof(VillageArtAssetsSetup))]
        public IEnumerator AlpineVillageMothersHouseWoodpile()
        {
            GameSessionState.TryStartGameTimeFromWake();
            Assert.That(GameSessionState.TrySetDebugGameDay(2), Is.True);
            GameSessionState.AdvanceGameTime((float)(100f / GameTimeState.GameMinutesPerRealSecond));
            AlpineVillageRoot root = null;
            yield return Capture(SceneIds.AlpineVillage, () =>
            {
                root = Object.FindAnyObjectByType<AlpineVillageRoot>();
                return root != null && root.IsInitialized ? root : null;
            }, () =>
            {
                AlpineVillageWoodpilePlan pile = root.Plan.MothersHouseWoodpile;
                AlpineVillagePlotDescriptor house = root.Plan.MothersHouse;
                return new[]
                {
                    AbandonmentShot(root, "mother-woodpile-00-entrance",
                        house.DoorGroundPosition + house.Facing * 9f,
                        house.DoorGroundPosition + Vector3.up * 1.6f, 70f, trough: true),
                    AbandonmentShot(root, "mother-woodpile-01-logs",
                        pile.Center - pile.Forward * 2.2f + pile.Right * 1.4f,
                        pile.Center + Vector3.up * .43f, 58f, trough: true)
                };
            });

            Assert.That(root.World.Root.GetComponentsInChildren<WoodpileInteraction>(true).Length, Is.EqualTo(2));
            Assert.That(root.MothersHouseWoodpile, Is.Not.SameAs(root.Woodpile));
            Transform stack = root.MothersHouseWoodpile.transform.parent;
            Bounds bounds = LocalRendererBounds(stack);
            Assert.That(bounds.size.x, Is.EqualTo(1.75f).Within(.03f));
            Assert.That(bounds.size.y, Is.InRange(.84f, .90f));
            Assert.That(stack.GetComponentsInChildren<MeshCollider>().Length, Is.GreaterThan(3));
            var paths = AlpineVillagePathPlanner.Create(root.Plan);
            var plan = root.Plan.MothersHouseWoodpile;
            Assert.That(AlpineVillageSnowDrift.SampleDepth(root.Plan, paths,
                new Vector2(stack.position.x, stack.position.z)), Is.LessThan(.03f));
            Assert.That(root.World.WalkableArea.Contains(plan.Center, .35f), Is.False);
            Assert.That(AbandonmentCapsuleFree(stack.position), Is.False);
            // Sample the entire approach from the door's return dock. This
            // catches a stack that works alone but cuts across the entrance.
            Physics.SyncTransforms();
            for (int step = 0; step <= 12; step++)
            {
                Vector3 point = Vector3.Lerp(root.Plan.MothersHouseReturnPosition, plan.Approach, step / 12f);
                point.y = AlpineVillageTerrainSampler.SampleMeshHeight(root.Plan, new Vector2(point.x, point.z));
                Assert.That(root.World.WalkableArea.Contains(point, .35f), Is.True, "Door-to-stack route mask.");
                Assert.That(AbandonmentCapsuleFree(point), Is.True, "Door-to-stack physical clearance at " + point);
            }

            GameSessionState.TryRemoveInventoryItem(InventoryItemId.FirewoodLog);
            InventoryTargetInteractionController menu = root.TargetInteraction;
            // Both directions matter: neither source owns its own capacity.
            foreach (bool motherFirst in new[] { true, false })
            {
                WoodpileInteraction first = motherFirst ? root.MothersHouseWoodpile : root.Woodpile;
                WoodpileInteraction second = motherFirst ? root.Woodpile : root.MothersHouseWoodpile;
                AlpineVillageWoodpilePlan firstPlan = motherFirst ? plan : root.Plan.LodgeWoodpile;
                AlpineVillageWoodpilePlan secondPlan = motherFirst ? root.Plan.LodgeWoodpile : plan;
                yield return ApproachWoodpile(root, firstPlan, first);
                first.Interact(root.Player.Interactor);
                Assert.That(menu.State, Is.EqualTo(InventoryTargetInteractionState.Confirmation));
                Assert.That(menu.Definition.ConfirmationPromptKey, Is.EqualTo(WoodpileInteraction.ConfirmationPromptKey));
                Assert.That(menu.ConfirmationYesSelected, Is.False);
                menu.Confirm();
                Assert.That(GameSessionState.GetInventoryItemCount(InventoryItemId.FirewoodLog), Is.Zero);
                yield return null;
                first.Interact(root.Player.Interactor);
                menu.SelectConfirmation(true);
                Assert.That(menu.Confirm(), Is.True);
                Assert.That(GameSessionState.GetInventoryItemCount(InventoryItemId.FirewoodLog), Is.EqualTo(1));
                yield return CloseWoodpileReceipt(root);

                yield return ApproachWoodpile(root, secondPlan, second);
                second.Interact(root.Player.Interactor);
                Assert.That(menu.IsOpen, Is.False, "A log from the other source also blocks pickup.");
                Assert.That(GameSessionState.GetInventoryItemCount(InventoryItemId.FirewoodLog), Is.EqualTo(1));
                Assert.That(GameSessionState.TryRemoveInventoryItem(InventoryItemId.FirewoodLog), Is.True);
                yield return null;
                second.Interact(root.Player.Interactor);
                Assert.That(menu.IsOpen, Is.True, "The source replenishes once inventory is empty.");
                menu.SelectConfirmation(true);
                Assert.That(menu.Confirm(), Is.True);
                Assert.That(GameSessionState.GetInventoryItemCount(InventoryItemId.FirewoodLog), Is.EqualTo(1));
                yield return CloseWoodpileReceipt(root);
                Assert.That(GameSessionState.TryRemoveInventoryItem(InventoryItemId.FirewoodLog), Is.True);
            }
            // Taking logs must not replace or claim the neighbouring door.
            root.Player.Motor.Teleport(root.Plan.MothersHouse.DoorDockPosition +
                Vector3.up * PlayerFactory.GroundedRootOffset);
            for (int frame = 0; frame < 3; frame++) yield return null;
            Assert.That(root.Player.Interactor.ActiveInteractable, Is.SameAs(root.MothersHouseEntrance));
        }

        private static IEnumerator ApproachWoodpile(AlpineVillageRoot root,
            AlpineVillageWoodpilePlan plan, WoodpileInteraction interaction)
        {
            root.Player.Motor.Teleport(plan.Approach + Vector3.up * PlayerFactory.GroundedRootOffset);
            for (int frame = 0; frame < 3; frame++) yield return null;
            Assert.That(root.Player.Interactor.ActiveInteractable, Is.SameAs(interaction), plan.Name + " offers E.");
        }

        [UnityTest]
        [Explicit("Focused cold stove, roof penetration, woodpile and pickup capture.")]
        [PrebuildSetup(typeof(VillageArtAssetsSetup))]
        public IEnumerator AlpineVillageStoveAndWoodpile()
        {
            GameSessionState.TryStartGameTimeFromWake();
            Assert.That(GameSessionState.TrySetDebugGameDay(2), Is.True);
            GameSessionState.AdvanceGameTime((float)(100f / GameTimeState.GameMinutesPerRealSecond));
            AlpineVillageRoot root = null;
            yield return Capture(SceneIds.AlpineVillage, () =>
            {
                root = Object.FindAnyObjectByType<AlpineVillageRoot>();
                return root != null && root.IsInitialized ? root : null;
            }, () => VillageStoveShots(root));
            VerifyVillageStove(root);
            var input = new InputTestFixture();
            input.Setup();
            try
            {
                Keyboard keyboard = InputSystem.AddDevice<Keyboard>();
                yield return VerifyVillageWoodpileInteraction(root, input, keyboard);
            }
            finally { input.TearDown(); }
        }

        private static Shot[] VillageStoveShots(AlpineVillageRoot root)
        {
            AlpineVillageExpansionPlan plan = root.Plan.Expansion;
            Vector3 centre = plan.LodgeCenter;
            Quaternion facing = Quaternion.LookRotation(plan.LodgeForward);
            Vector3 At(float x, float y, float z) => centre + facing * new Vector3(x, y, z);
            Vector3 pile = plan.LodgeWoodpileCenter;
            return new[]
            {
                AbandonmentShot(root, "stove-00-interior", At(-2.7f, .02f, -3.1f),
                    At(0f, 1.75f, 0f), 74f),
                Shot.At("stove-00b-door-grille", At(.65f, .95f, -1.8f),
                    At(0f, .71f, 0f), 52f),
                AbandonmentShot(root, "stove-01-front-and-chimney", At(-2f, 0f, -15f),
                    At(0f, 2.9f, 0f), 66f, trough: true),
                AbandonmentShot(root, "stove-02-woodpile", pile + facing * new Vector3(1.8f, 0f, -2f),
                    pile + Vector3.up * .43f, 55f, trough: true),
                Shot.At("stove-03-roof-penetration", At(2.7f, 7.2f, -3.2f),
                    At(0f, 5.55f, 0f), 56f)
            };
        }

        private static void VerifyVillageStove(AlpineVillageRoot root)
        {
            AlpineVillageExpansionPlan plan = root.Plan.Expansion;
            Transform lodge = root.World.Root.transform.Find("Village Expansion/Ski Lodge");
            Assert.That(lodge, Is.Not.Null);
            foreach (string name in new[] { "StoveBody", "StoveHearth", "StovePipe", "RoofFlashing", "ChimneyCap" })
            {
                Transform part = lodge.Find(name);
                Assert.That(part, Is.Not.Null, name);
                Assert.That(part.GetComponent<Renderer>(), Is.Not.Null, name);
            }
            Bounds body = LocalRendererBounds(lodge.Find("StoveBody"));
            Vector3 bodySize = Vector3.Scale(body.size, lodge.Find("StoveBody").lossyScale);
            Assert.That(bodySize.x, Is.InRange(.8f, 1.05f), "Imported stove metre scale.");
            Renderer pipe = lodge.Find("StovePipe").GetComponent<Renderer>();
            Assert.That(Vector2.Distance(new Vector2(pipe.bounds.center.x, pipe.bounds.center.z),
                new Vector2(plan.LodgeCenter.x, plan.LodgeCenter.z)), Is.LessThan(.02f),
                "The indoor and rooftop pipe must share the building's centre axis.");
            Assert.That(pipe.bounds.min.y - plan.LodgeFloorHeight, Is.InRange(1f, 1.3f));
            Assert.That(pipe.bounds.max.y - plan.LodgeFloorHeight, Is.GreaterThan(6f));
            Assert.That(lodge.GetComponentsInChildren<Light>(), Is.Empty, "The stove is cold.");

            Physics.SyncTransforms();
            Collider door = lodge.Find("StoveDoor").GetComponent<Collider>();
            Collider firebox = lodge.Find("StoveBody").GetComponent<Collider>();
            foreach (float x in new[] { -.16f, 0f, .16f })
            foreach (float y in new[] { .64f, .88f })
            {
                var sight = new Ray(lodge.TransformPoint(new Vector3(x, y, -.8f)), lodge.forward);
                Assert.That(door.Raycast(sight, out _, 1.3f), Is.False, "The grille has real sight openings.");
                Assert.That(firebox.Raycast(sight, out RaycastHit back, 1.3f), Is.True);
                Assert.That(lodge.InverseTransformPoint(back.point).z, Is.EqualTo(.375f).Within(.003f),
                    "The openings reveal a hollow firebox, not a solid block behind the door.");
            }
            Assert.That(door.Raycast(new Ray(lodge.TransformPoint(new Vector3(0f, .76f, -.8f)),
                lodge.forward), out _, 1.3f), Is.True, "The grille crossbar remains solid.");
            Collider roof = lodge.Find("Roof").GetComponent<Collider>();
            Assert.That(roof.Raycast(new Ray(plan.LodgeCenter + Vector3.up * 7f, Vector3.down),
                out _, 5f), Is.False, "The flue must pass through a real opening in the roof.");
            Assert.That(root.World.WalkableArea.Contains(plan.LodgeCenter, .35f), Is.False);
            Assert.That(AbandonmentCapsuleFree(plan.LodgeCenter), Is.False, "Visible iron must stop the hero.");
            foreach (float side in new[] { -1.25f, 1.25f })
            for (float along = -3.3f; along <= 3.3f; along += .3f)
            {
                Vector3 point = plan.LodgeCenter + root.Plan.SlopeRight * side + plan.LodgeForward * along;
                Assert.That(root.World.WalkableArea.Contains(point, .35f), Is.True, "Stove bypass movement mask.");
                Assert.That(AbandonmentCapsuleFree(point), Is.True, "Stove bypass physical capsule at " + point);
            }

            Transform pile = root.Woodpile.transform.parent;
            Bounds pileBounds = LocalRendererBounds(pile);
            Assert.That(pileBounds.size.x, Is.EqualTo(1.75f).Within(.03f), "Woodpile imported metre scale.");
            Assert.That(pileBounds.size.y, Is.InRange(.84f, .90f), "The stack has actual loose top logs.");
            Assert.That(pile.GetComponentsInChildren<MeshCollider>().Length, Is.GreaterThan(3));
            Assert.That(root.World.WalkableArea.Contains(plan.LodgeWoodpileApproach, .35f), Is.True);
            Assert.That(root.World.WalkableArea.Contains(plan.LodgeEntrance, .35f), Is.True);
            var paths = AlpineVillagePathPlanner.Create(root.Plan);
            Assert.That(AlpineVillageSnowDrift.SampleDepth(root.Plan, paths,
                new Vector2(pile.position.x, pile.position.z)), Is.LessThan(.03f), "Snow must not bury the logs.");
        }

        private static IEnumerator VerifyVillageWoodpileInteraction(AlpineVillageRoot root,
            InputTestFixture input, Keyboard keyboard)
        {
            PlayerInteractor interactor = root.Player.Interactor;
            InventoryTargetInteractionController menu = root.TargetInteraction;
            GameSessionState.TryRemoveInventoryItem(InventoryItemId.FirewoodLog);
            root.Player.Motor.Teleport(root.Plan.Expansion.LodgeWoodpileApproach +
                Vector3.up * PlayerFactory.GroundedRootOffset);
            for (int frame = 0; frame < 3; frame++) yield return null;
            Assert.That(interactor.ActiveInteractable, Is.SameAs(root.Woodpile), "The reachable pile must offer E.");
            Assert.That(LocalizationService.Get(root.Woodpile.PromptKey), Does.StartWith("E — "));
            yield return CaptureWoodpileScreen("woodpile-00-prompt");
            root.Woodpile.Interact(interactor);
            Assert.That(menu.State, Is.EqualTo(InventoryTargetInteractionState.Confirmation));
            Assert.That(menu.Definition.ConfirmationPromptKey, Is.EqualTo(WoodpileInteraction.ConfirmationPromptKey));
            Assert.That(menu.ConfirmationYesSelected, Is.False);
            yield return null;
            input.Press(keyboard.eKey, queueEventOnly: true);
            yield return null;
            Assert.That(menu.IsOpen, Is.False, "No closes directly.");
            input.Release(keyboard.eKey, queueEventOnly: true);
            Assert.That(GameSessionState.GetInventoryItemCount(InventoryItemId.FirewoodLog), Is.Zero);
            yield return null;

            root.Woodpile.Interact(interactor);
            menu.SelectConfirmation(true);
            yield return null;
            input.Press(keyboard.eKey, queueEventOnly: true);
            yield return null;
            Assert.That(GameSessionState.GetInventoryItemCount(InventoryItemId.FirewoodLog), Is.EqualTo(1));
            Assert.That(menu.Confirm(), Is.False, "A repeated confirmation cannot duplicate the log.");
            input.Release(keyboard.eKey, queueEventOnly: true);
            WorldItemFoundScreen receipt = WorldItemFoundScreen.For(interactor);
            Assert.That(receipt.IsPresenting, Is.True, "The confirmation press must leave the receipt open.");
            Assert.That(receipt.ActiveItemId, Is.EqualTo(InventoryItemId.FirewoodLog));
            Assert.That(receipt.FeedbackKey, Is.EqualTo(WoodpileInteraction.ReceivedFeedbackKey));
            Assert.That(receipt.ActionKey, Is.EqualTo(WorldItemFoundScreen.CloseActionKey));
            Assert.That(interactor.InputEnabled, Is.False);
            yield return WaitForScarfFound(receipt, screen => screen.IsShowing);
            yield return CaptureWoodpileScreen("woodpile-01-received");
            input.Press(keyboard.eKey, queueEventOnly: true);
            yield return null;
            input.Release(keyboard.eKey, queueEventOnly: true);
            yield return WaitForScarfFound(receipt, screen => !screen.IsPresenting);
            yield return null;
            Assert.That(interactor.InputEnabled, Is.True);
            Assert.That(Object.FindAnyObjectByType<InteractionPromptView>().PromptKey,
                Is.Not.EqualTo(WoodpileInteraction.AlreadyCarryingFeedbackKey),
                "Closing the receipt must not reuse E on the pile.");
            root.Woodpile.Interact(interactor);
            Assert.That(menu.IsOpen, Is.False, "An existing log prevents another offer.");
            Assert.That(GameSessionState.GetInventoryItemCount(InventoryItemId.FirewoodLog), Is.EqualTo(1));
            Assert.That(root.Woodpile.gameObject.activeInHierarchy, Is.True, "The source remains.");
            Assert.That(GameSessionState.TryRemoveInventoryItem(InventoryItemId.FirewoodLog), Is.True);
            yield return null;
            root.Woodpile.Interact(interactor);
            Assert.That(menu.IsOpen, Is.True, "An empty inventory may take another log from the same pile.");
            menu.SelectConfirmation(true);
            Assert.That(menu.Confirm(), Is.True);
            Assert.That(GameSessionState.GetInventoryItemCount(InventoryItemId.FirewoodLog), Is.EqualTo(1));
            // Escape dismisses a receipt, not the already accepted transaction.
            yield return null;
            input.Press(keyboard.escapeKey, queueEventOnly: true);
            yield return null;
            input.Release(keyboard.escapeKey, queueEventOnly: true);
            yield return WaitForScarfFound(receipt, screen => !screen.IsPresenting);
            Assert.That(GameSessionState.GetInventoryItemCount(InventoryItemId.FirewoodLog), Is.EqualTo(1));

            // MonoBehaviour lifecycle is exercised in PlayMode, where Unity
            // actually delivers these callbacks to ordinary runtime scripts.
            GameSessionState.TryRemoveInventoryItem(InventoryItemId.FirewoodLog);
            yield return null;
            root.Woodpile.Interact(interactor);
            Assert.That(menu.IsOpen, Is.True);
            root.Woodpile.enabled = false;
            Assert.That(menu.IsOpen, Is.False, "Disabling the source must close its modal.");
            Assert.That(interactor.InputEnabled, Is.True);
            Assert.That(GameSessionState.GetInventoryItemCount(InventoryItemId.FirewoodLog), Is.Zero);
            root.Woodpile.enabled = true;
            yield return null;
            root.Woodpile.Interact(interactor);
            menu.SelectConfirmation(true);
            menu.Confirm();
            Assert.That(receipt.IsPresenting, Is.True);
            root.Woodpile.enabled = false;
            Assert.That(receipt.IsPresenting, Is.False, "Disabling the source cleans up the held receipt model.");
            Assert.That(interactor.InputEnabled, Is.True);
            Assert.That(GameSessionState.GetInventoryItemCount(InventoryItemId.FirewoodLog), Is.EqualTo(1));
            GameSessionState.TryRemoveInventoryItem(InventoryItemId.FirewoodLog);
            root.Woodpile.enabled = true;
            yield return null;
            root.Woodpile.Interact(interactor);
            Assert.That(menu.IsOpen, Is.True);
            Object.Destroy(root.Woodpile.gameObject);
            yield return null;
            Assert.That(menu.IsOpen, Is.False, "Destroying the source must close its modal.");
            Assert.That(interactor.InputEnabled, Is.True);
            Assert.That(root.Player.Motor.InputEnabled, Is.True);
            Assert.That(BarMinigameModalLock.IsAnyLocked, Is.False);
            Assert.That(GameSessionState.GetInventoryItemCount(InventoryItemId.FirewoodLog), Is.Zero);
        }

        private static IEnumerator CloseWoodpileReceipt(AlpineVillageRoot root)
        {
            WorldItemFoundScreen receipt = WorldItemFoundScreen.For(root.Player.Interactor);
            Assert.That(receipt.IsPresenting, Is.True);
            Assert.That(receipt.FeedbackKey, Is.EqualTo(WoodpileInteraction.ReceivedFeedbackKey));
            Assert.That(receipt.Confirm(), Is.True);
            Assert.That(receipt.Confirm(), Is.False);
            yield return WaitForScarfFound(receipt, screen => !screen.IsPresenting);
            yield return null;
            Assert.That(GameSessionState.GetInventoryItemCount(InventoryItemId.FirewoodLog), Is.EqualTo(1));
        }

        private static IEnumerator CaptureWoodpileScreen(string name)
        {
            string path = Path.GetFullPath(Path.Combine(Application.dataPath, "..",
                "Captures", SceneIds.AlpineVillage, name + ".png"));
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            System.DateTime previous = File.Exists(path) ? File.GetLastWriteTimeUtc(path) : System.DateTime.MinValue;
            ScreenCapture.CaptureScreenshot(path);
            float deadline = Time.realtimeSinceStartup + 5f;
            do { yield return null; }
            while ((!File.Exists(path) || File.GetLastWriteTimeUtc(path) <= previous) &&
                   Time.realtimeSinceStartup < deadline);
            Assert.That(File.Exists(path) && File.GetLastWriteTimeUtc(path) > previous, Is.True);
        }
    }
}

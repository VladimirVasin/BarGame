using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

namespace BarPromenade.Tests.PlayMode
{
    public sealed partial class CombatTestPlayModeTests
    {
        [UnityTest]
        public IEnumerator Range_CorpseTraversalKeepsMovementFootClearanceAndPoolReuse()
        {
            var input = new InputTestFixture();
            bool inputStarted = false;
            try
            {
                yield return SceneManager.LoadSceneAsync(SceneIds.MainMenu, LoadSceneMode.Single);
                CombatTestRoot.UseLegacyArenaForTests = false;
                CombatTestRoot.WarehouseSeedForTests = 0;
                var pacing = new CombatEncounterSettings(3, .05f, .05f, 12f, 12f);
                Assert.That(CombatTestStartService.TryStart(new CombatTestSelection(
                    new CombatLoadoutSelection(CombatWeaponId.Pistol), CombatTestMode.Endless, pacing)), Is.True);
                yield return AwaitSelectedCombatRange();
                root.SendMessage("OnApplicationFocus", true);
                GameInput.HandleApplicationFocus(true);
                Assert.That(root.WarehousePlan, Is.Not.Null);

                // Arrange the prepared population through its production arrival method.
                // Advancing only this clock avoids an unrelated long fight during setup.
                MethodInfo arrive = typeof(CombatTestRoot).GetMethod("AdvanceEndlessInterval",
                    BindingFlags.Instance | BindingFlags.NonPublic);
                Assert.That(arrive, Is.Not.Null);
                for (int i = 0; i < 3; i++)
                    Assert.That((bool)arrive.Invoke(root, new object[] { root.NextOpponentSeconds + .01f }), Is.True);
                Assert.That(root.LiveOpponentCount, Is.EqualTo(3));
                CombatActor first = root.Opponents[0], second = root.Opponents[1], zombie = root.Opponents[2];
                zombie.ResetActor(new Vector3(9f, PlayerFactory.GroundedRootOffset, -7f), Vector3.left);
                first.ResetActor(new Vector3(-.15f, PlayerFactory.GroundedRootOffset, -2f), Vector3.right);
                second.ResetActor(new Vector3(.15f, PlayerFactory.GroundedRootOffset, -1.1f), Vector3.right);
                foreach (CombatActor corpse in new[] { first, second })
                {
                    corpse.State.ReceiveProjectileHit(corpse.State.Settings.MaxHealth);
                    corpse.Step(CombatAssetProvider.DefeatHandoffSeconds + .05f);
                    Assert.That(corpse.State.IsDefeated && corpse.IsRagdollActive, Is.True,
                        "Traversal must use an actual terminal physics rig.");
                }
                ApplyKnownBodyContact(second, root.Hero, BodyDamageRegion.LeftForearm, 0, 2f, 1);
                Assert.That(root.BodyEffects.DetachedPartCountFor(second), Is.GreaterThan(0));
                for (int frame = 0; frame < 240 && (!first.Ragdoll.IsSettled || !second.Ragdoll.IsSettled); frame++)
                {
                    root.BodyEffects.Tick(Time.fixedDeltaTime);
                    yield return new WaitForFixedUpdate();
                }
                Assert.That(first.Ragdoll.IsSettled && second.Ragdoll.IsSettled, Is.True);
                Physics.SyncTransforms();

                var remains = new List<Collider>();
                foreach (Collider collider in root.GetComponentsInChildren<Collider>())
                    if (collider.enabled && !collider.isTrigger && CombatCorpseTraversal.IsCorpse(collider))
                        remains.Add(collider);
                Assert.That(remains.Count, Is.GreaterThan(10), "Both complete ragdolls retain real collision shapes.");
                CombatBodyFragment[] fragments = root.BodyEffects.GetComponentsInChildren<CombatBodyFragment>();
                Assert.That(fragments.Length, Is.GreaterThan(0));
                foreach (CombatBodyFragment fragment in fragments)
                    Assert.That(CombatCorpseTraversal.IsCorpse(fragment.GetComponent<Collider>()), Is.True,
                        "A detached part retains its traversal identity outside the actor hierarchy.");
                AssertRemainsIgnoredBy(root.Hero.Body, remains);
                AssertRemainsIgnoredBy(zombie.Body, remains);

                Bounds firstBody = CorpseTorsoBounds(first);
                Bounds secondBody = CorpseTorsoBounds(second);
                float laneX = (firstBody.center.x + secondBody.center.x) * .5f;
                float startZ = Mathf.Min(firstBody.min.z, secondBody.min.z) - 1f;
                float endZ = Mathf.Max(firstBody.max.z, secondBody.max.z) + 1f;
                Assert.That(CombatCorpseTraversal.TryGetClearance(
                    new Vector3(laneX, 0f, startZ), new Vector3(laneX, 0f, endZ), .32f, out float top), Is.True);
                Assert.That(top, Is.InRange(.08f, .7f), "The crossing intersects actual low body contours.");

                // Establish devices after scene startup and physical settling;
                // controls must belong to the current fixture manager state.
                input.Setup();
                inputStarted = true;
                Keyboard keyboard = InputSystem.AddDevice<Keyboard>();
                InputSystem.AddDevice<Mouse>();
                root.SendMessage("OnApplicationFocus", true);
                GameInput.HandleApplicationFocus(true);
                yield return null;
                Assert.That(InputSystem.devices, Has.Member(keyboard));
                Assert.That(Keyboard.current, Is.SameAs(keyboard));
                yield return WalkHeroAcrossRemains(input, keyboard, laneX, startZ, endZ, aimed: false, pause: true);
                yield return WalkHeroAcrossRemains(input, keyboard, laneX, startZ, endZ, aimed: true, pause: false);

                Collider detached = fragments[0].GetComponent<Collider>();
                Vector3 partStart = detached.bounds.center + Vector3.back * .6f;
                partStart.y = PlayerFactory.GroundedRootOffset;
                root.Hero.ResetActor(partStart, Vector3.forward);
                root.Hero.Body.Move(Vector3.forward * 1.2f);
                Assert.That(root.Hero.transform.position.z - partStart.z, Is.GreaterThan(1.15f),
                    "A real separated part must not catch the moving capsule.");

                // The committed action uses its own displacement path; ignored corpse
                // shapes must not consume its progress or set the blocked latch.
                float stepZ = firstBody.center.z - root.Hero.State.Settings.StepDistance * .45f;
                root.Hero.ResetActor(new Vector3(laneX, PlayerFactory.GroundedRootOffset, stepZ), Vector3.forward);
                root.Hero.SetCombatFocused(false);
                root.Hero.SetPistolAim(true, root.Hero.transform.position + Vector3.forward * 20f + Vector3.up);
                root.Hero.Step(.25f);
                Vector3 stepStart = root.Hero.transform.position;
                Assert.That(root.Hero.TryStep(Vector2.up), Is.True);
                for (int frame = 0; frame < 90 && root.Hero.State.Phase == MeleePhase.Step; frame++)
                    root.Hero.Step(1f / 60f);
                Assert.That(root.Hero.StepTravelBlocked, Is.False);
                Assert.That(root.Hero.transform.position.z - stepStart.z,
                    Is.GreaterThan(root.Hero.State.Settings.StepDistance * .85f));

                // The live warehouse AI must pursue through both bodies and then
                // move its real capsule during the committed close-range lunge.
                root.Hero.ResetActor(new Vector3(laneX, PlayerFactory.GroundedRootOffset, endZ + 2.5f), Vector3.back);
                root.Hero.SetCombatFocused(false);
                zombie.ResetActor(new Vector3(laneX, PlayerFactory.GroundedRootOffset, startZ), Vector3.forward);
                // Let camera-independent equipment finish its normal late pass
                // after the setup teleport before the synchronous chase capture.
                yield return null;
                float maximumZombieLift = 0f;
                int zombieContourSamples = 0;
                bool zombieCapture = false;
                for (int frame = 0; frame < 480 && zombie.transform.position.z < endZ; frame++)
                {
                    root.Tick(1f / 60f);
                    ObserveCorpseFeet(zombie, ref maximumZombieLift, ref zombieContourSamples);
                    if (!zombieCapture && maximumZombieLift > .08f && zombieContourSamples > 0)
                    {
                        CaptureCorpseCrossing(zombie, "Test zombie crossing");
                        zombieCapture = true;
                    }
                }
                Assert.That(zombie.transform.position.z, Is.GreaterThanOrEqualTo(endZ),
                    "The actual pursuit must pass the far edge of the two corpses.");
                Assert.That(maximumZombieLift, Is.GreaterThan(.08f));
                Assert.That(zombieContourSamples, Is.GreaterThan(0));
                Assert.That(root.CorpseCount, Is.EqualTo(2), "The chase proof must retain both obstacles.");

                // Reposition only the living pair so the lunge itself crosses a
                // corpse contour; the fallen rig and its detached part stay put.
                zombie.ResetActor(new Vector3(laneX, PlayerFactory.GroundedRootOffset, firstBody.center.z - .48f), Vector3.forward);
                root.Hero.ResetActor(zombie.transform.position + Vector3.forward * 1.45f, Vector3.back);
                for (int frame = 0; frame < 150 && zombie.State.Phase != MeleePhase.Windup; frame++)
                    root.Tick(1f / 60f);
                Assert.That(zombie.State.Phase, Is.EqualTo(MeleePhase.Windup));
                Vector3 lungeStart = zombie.transform.position;
                for (int frame = 0; frame < 120 && zombie.State.Phase != MeleePhase.Recovery; frame++)
                    root.Tick(1f / 60f);
                Assert.That(zombie.transform.position.z - lungeStart.z, Is.GreaterThan(.15f),
                    "The lunge's solid sweep and capsule must both traverse the fallen torso.");

                // A living amputee keeps its own collisions while its remaining
                // physical limbs can move among the passable terminal bodies.
                zombie.ResetActor(new Vector3(laneX, PlayerFactory.GroundedRootOffset, startZ + .5f), Vector3.forward);
                root.Hero.ResetActor(new Vector3(laneX, PlayerFactory.GroundedRootOffset, endZ + 3f), Vector3.back);
                ApplyKnownBodyContact(zombie, root.Hero, BodyDamageRegion.RightShin, 0, 2f, 2);
                for (int frame = 0; frame < 120 && !zombie.Ragdoll.HasGroundContact; frame++)
                {
                    zombie.Step(1f / 60f);
                    yield return new WaitForFixedUpdate();
                }
                Assert.That(zombie.IsBodyGrounded && zombie.Ragdoll.HasGroundContact, Is.True);
                foreach (var entry in zombie.Ragdoll.PhysicsController.AnatomicalColliders)
                    if (entry.Key.enabled && zombie.Ragdoll.PhysicsController.IsCombatAnatomicalQueryAllowed(entry.Key))
                    {
                        Assert.That(CombatCorpseTraversal.IsCorpse(entry.Key), Is.False,
                            "A living grounded opponent must not be classified as a corpse.");
                        AssertRemainsIgnoredBy(entry.Key, remains);
                    }
                Vector3 crawlStart = zombie.BodyWorldPosition;
                zombie.SetCrawlInput(Vector2.up);
                for (int frame = 0; frame < 90; frame++)
                {
                    zombie.Step(1f / 60f);
                    yield return new WaitForFixedUpdate();
                }
                zombie.ClearCrawlInput();
                Assert.That(Vector3.ProjectOnPlane(zombie.BodyWorldPosition - crawlStart, Vector3.up).magnitude,
                    Is.GreaterThan(.025f), "The living physical crawl must retain progress beside the corpses.");

                VerifyWarehouseSolidAndStairTraversal(laneX);

                // Expiring the production arrival clock reuses the oldest corpse.
                root.Tick(0f);
                Assert.That((bool)arrive.Invoke(root, new object[] { root.NextOpponentSeconds + .01f }), Is.True);
                Assert.That(first.State.IsDefeated || first.IsRagdollActive, Is.False);
                foreach (var entry in first.Ragdoll.PhysicsController.AnatomicalColliders)
                {
                    Assert.That(CombatCorpseTraversal.IsCorpse(entry.Key), Is.False);
                    Assert.That(Physics.GetIgnoreCollision(root.Hero.Body, entry.Key), Is.False,
                        "A reused live rig cannot inherit corpse collision exclusions.");
                }
                root.ResetRound();
                Assert.That(root.CorpseCount, Is.Zero);
                Assert.That(root.BodyEffects.ActiveFragmentCount, Is.Zero);
                Assert.That(root.Hero.CorpseStepPose.IsApplied, Is.False);
                Assert.That(root.Hero.CorpseStepPose.LeftLift + root.Hero.CorpseStepPose.RightLift, Is.Zero);
                Assert.That(zombie.CorpseStepPose.IsApplied, Is.False);
                foreach (Collider collider in root.GetComponentsInChildren<Collider>(true))
                    Assert.That(CombatCorpseTraversal.IsCorpse(collider), Is.False,
                        "Reset must remove traversal registrations for retired and restored bodies.");
                LogAssert.NoUnexpectedReceived();
            }
            finally { if (inputStarted) input.TearDown(); }
        }

        private IEnumerator WalkHeroAcrossRemains(InputTestFixture input, Keyboard keyboard,
            float x, float startZ, float endZ, bool aimed, bool pause)
        {
            root.Hero.ResetActor(new Vector3(x, PlayerFactory.GroundedRootOffset, startZ), Vector3.forward);
            root.Hero.SetCombatFocused(false);
            root.Hero.SetPistolAim(aimed, root.Hero.transform.position + Vector3.forward * 20f + Vector3.up);
            root.Hero.Step(.25f);
            float maximumLift = 0f;
            int contourSamples = 0;
            bool captured = false, paused = false;
            input.Press(keyboard.wKey, queueEventOnly: true);
            try
            {
                for (int frame = 0; frame < 240 && root.Hero.transform.position.z < endZ; frame++)
                {
                    root.Hero.Step(1f / 60f);
                    yield return null;
                    ((Player3DCharacterPresentation)root.Player.Visual).ReapplyLatePresentationPose();
                    ObserveCorpseFeet(root.Hero, ref maximumLift, ref contourSamples);
                    if (!captured && maximumLift > .08f && contourSamples > 0)
                    {
                        CaptureCorpseCrossing(root.Hero, aimed ? "Test aimed hero crossing" : "Test walking hero crossing");
                        captured = true;
                    }
                    if (pause && !paused && root.Hero.CorpseStepPose.IsApplied)
                    {
                        input.Release(keyboard.wKey, queueEventOnly: true);
                        Vector3 heldRoot = root.Hero.transform.position;
                        Vector3 heldLeft = CombatBone(root.Hero, "foot.L").position;
                        Vector3 heldRight = CombatBone(root.Hero, "foot.R").position;
                        Assert.That(root.PauseMenu.Open(), Is.True);
                        root.Tick(1f); root.Hero.Step(1f);
                        yield return null;
                        ((Player3DCharacterPresentation)root.Player.Visual).ReapplyLatePresentationPose();
                        Assert.That(root.Hero.transform.position, Is.EqualTo(heldRoot));
                        Assert.That(Vector3.Distance(heldLeft, CombatBone(root.Hero, "foot.L").position), Is.LessThan(.002f));
                        Assert.That(Vector3.Distance(heldRight, CombatBone(root.Hero, "foot.R").position), Is.LessThan(.002f));
                        Assert.That(root.PauseMenu.Cancel(), Is.True);
                        yield return WaitFor(() => GameInput.CanRead(GameInputContext.Gameplay), "Corpse traversal pause did not release input.");
                        input.Press(keyboard.wKey, queueEventOnly: true);
                        paused = true;
                    }
                }
            }
            finally { input.Release(keyboard.wKey, queueEventOnly: true); }
            Assert.That(root.Hero.transform.position.z, Is.GreaterThanOrEqualTo(endZ),
                aimed ? "Aimed locomotion must cross both real bodies." : "Ordinary keyboard walking must cross both real bodies.");
            Assert.That(maximumLift, Is.GreaterThan(.08f), "The rig must visibly lift a foot rather than merely ignore collision.");
            Assert.That(contourSamples, Is.GreaterThan(0));
            if (pause) Assert.That(paused, Is.True, "Pause must interrupt an actual raised-foot pose.");
            yield return null;
        }

        private static void AssertRemainsIgnoredBy(Collider livingBody, List<Collider> remains)
        {
            foreach (Collider corpse in remains)
                if (corpse != null && corpse.enabled)
                    Assert.That(Physics.GetIgnoreCollision(livingBody, corpse), Is.True,
                        "Living locomotion must ignore each terminal or detached collision shape: " + corpse.name);
        }

        private static Bounds CorpseTorsoBounds(CombatActor corpse)
        {
            Player3DRagdollController physics = corpse.Ragdoll.PhysicsController;
            foreach (var entry in physics.AnatomicalColliders)
                if (entry.Value == Player3DAnatomicalPart.Torso && entry.Key.enabled)
                    return entry.Key.bounds;
            // Authored damage surfaces replace the envelope while physics owns
            // the body; these child proxies retain the chest rigidbody owner.
            bool found = false;
            Bounds bounds = default;
            foreach (Collider collider in physics.ChestBody.GetComponentsInChildren<Collider>())
                if (collider.enabled && !collider.isTrigger && collider.attachedRigidbody == physics.ChestBody &&
                    CombatCorpseTraversal.IsCorpse(collider))
                {
                    if (!found) { bounds = collider.bounds; found = true; }
                    else bounds.Encapsulate(collider.bounds);
                }
            Assert.That(found, Is.True, "The terminal chest must retain an enabled anatomical or authored collision shape.");
            return bounds;
        }

        private static void ObserveCorpseFeet(CombatActor actor, ref float maximumLift, ref int contourSamples)
        {
            CombatCorpseStepPose pose = actor.CorpseStepPose;
            Assert.That(pose, Is.Not.Null);
            maximumLift = Mathf.Max(maximumLift, pose.LeftLift, pose.RightLift);
            int observed = 0;
            Check("foot.L", pose.LeftSurfaceY, pose.LeftTarget);
            Check("foot.R", pose.RightSurfaceY, pose.RightTarget);
            contourSamples += observed;
            void Check(string boneName, float surface, Vector3 target)
            {
                if (float.IsNegativeInfinity(surface)) return;
                observed++;
                Vector3 foot = CombatBone(actor, boneName).position;
                Assert.That(foot.y, Is.GreaterThanOrEqualTo(surface + .075f),
                    $"{(actor.IsHero ? "Hero" : "Zombie")} {boneName}: displayed y={foot.y:F4}, corpse top={surface:F4}; " +
                    "the boot must clear the actual corpse contour.");
                Assert.That(Vector3.Distance(foot, target), Is.LessThan(.03f),
                    $"{(actor.IsHero ? "Hero" : "Zombie")} {boneName}: foot={foot}, target={target}; " +
                    "the displayed rig must reach its measured foot target.");
            }
        }

        private void VerifyWarehouseSolidAndStairTraversal(float laneX)
        {
            root.Hero.ResetActor(new Vector3(laneX, PlayerFactory.GroundedRootOffset, -7.5f), Vector3.back);
            root.Hero.Body.Move(Vector3.back * 4f);
            Assert.That(root.Hero.transform.position.z, Is.GreaterThan(-9.1f),
                "Corpse exclusions must not remove the warehouse shell collision.");
            WarehouseArenaStair stair = root.WarehousePlan.Stairs[0];
            root.Hero.ResetActor(stair.Foot + Vector3.back * .35f + Vector3.up * PlayerFactory.GroundedRootOffset,
                Vector3.forward);
            for (int i = 0; i < 60; i++) root.Hero.Body.Move(new Vector3(0f, -.02f, .05f));
            Assert.That(root.Hero.transform.position.z, Is.GreaterThan(stair.Foot.z + 1.5f));
            Assert.That(root.Hero.transform.position.y, Is.GreaterThan(.8f),
                "Ordinary authored stairs must still support and raise the capsule.");
        }

        private void CaptureCorpseCrossing(CombatActor actor, string name)
        {
            Camera camera = root.CameraFollow.Camera;
            Vector3 previousPosition = camera.transform.position;
            Quaternion previousRotation = camera.transform.rotation;
            float previousFov = camera.fieldOfView;
            try
            {
                Vector3 focus = actor.transform.position + Vector3.up * .65f;
                Vector3 eye = focus + new Vector3(-3.1f, 1.5f, -2.7f);
                camera.transform.SetPositionAndRotation(eye, Quaternion.LookRotation(focus - eye));
                camera.fieldOfView = 44f;
                string folder = SceneIds.CombatTest + "/corpse-traversal";
                string path = Path.Combine(Directory.GetCurrentDirectory(), "Captures", folder, name + ".png");
                LogAssert.Expect(LogType.Log, "Area capture wrote " + path);
                AreaCaptureFixture.CaptureCurrentCamera(camera, folder, name);
            }
            finally
            {
                camera.transform.SetPositionAndRotation(previousPosition, previousRotation);
                camera.fieldOfView = previousFov;
            }
        }
    }
}

using System;
using System.Collections;
using System.Linq;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace BarPromenade.Tests.PlayMode
{
    public sealed class ChurchInteriorPlayModeTests
    {
        private const string ChurchRootName =
            "[Bar Promenade] Church Interior Runtime";
        private const string DoorRootName =
            "[Bar Promenade] Door Transition Runtime";
        private const string CityRootName =
            "[Bar Promenade] City Runtime";
        private const int RoomColliderCount = 6;
        private const float TimeoutSeconds = 45f;

        [UnityTest]
        public IEnumerator ChurchInterior_BootsAndCompletesDoorRoundTrip()
        {
            Assert.That(
                Application.CanStreamedLevelBeLoaded(
                    SceneIds.ChurchInterior),
                Is.True);
            Assert.That(
                Application.CanStreamedLevelBeLoaded(
                    SceneIds.DoorTransition),
                Is.True);
            Assert.That(
                Application.CanStreamedLevelBeLoaded(SceneIds.City),
                Is.True);

            GameSessionState.BeginNewGame();
            bool accepted = SceneTransitionService.RequestDoorLoad(
                SceneIds.ChurchInterior,
                DoorTransitionDirection.EnterChurch,
                out string enterOperationId);
            Assert.That(accepted, Is.True, enterOperationId);
            GameSessionState.EnterChurch();

            DoorTransitionRoot enteringDoor = null;
            yield return WaitForLoadedRoot(
                SceneIds.DoorTransition,
                DoorRootName,
                (DoorTransitionRoot root) => enteringDoor = root);
            yield return WaitUntil(
                () => enteringDoor.IsInitialized,
                "Entering church door did not initialize.");
            Assert.That(
                enteringDoor.Direction,
                Is.EqualTo(DoorTransitionDirection.EnterChurch));

            ChurchInteriorRoot interior = null;
            yield return WaitForLoadedRoot(
                SceneIds.ChurchInterior,
                ChurchRootName,
                (ChurchInteriorRoot root) => interior = root);
            yield return WaitUntil(
                () => interior.IsInitialized &&
                      !SceneTransitionService.IsTransitioning,
                "ChurchInterior did not finish booting.");

            AssertInteriorContract(interior);
            PlacePlayerAtDoor(interior.Player, interior.Exit);
            Assert.That(
                interior.Exit.CanInteract(interior.Player.Interactor),
                Is.True);
            interior.Exit.Interact(interior.Player.Interactor);
            PlayerDoorActionController action =
                interior.Player.GameObject.GetComponent<
                    PlayerDoorActionController>();
            Assert.That(action, Is.Not.Null);
            Assert.That(action.IsPlaying, Is.True);
            yield return WaitUntil(
                () => SceneTransitionService.IsTransitioning,
                "Church exit DoorUse did not complete.");

            DoorTransitionRoot exitingDoor = null;
            yield return WaitForLoadedRoot(
                SceneIds.DoorTransition,
                DoorRootName,
                (DoorTransitionRoot root) => exitingDoor = root);
            yield return WaitUntil(
                () => exitingDoor.IsInitialized,
                "Exiting church door did not initialize.");
            Assert.That(
                exitingDoor.Direction,
                Is.EqualTo(DoorTransitionDirection.ExitChurch));
            Assert.That(
                GameSessionState.ReturnKind,
                Is.EqualTo(CityReturnKind.Church));

            CityGameRoot city = null;
            yield return WaitForLoadedRoot(
                SceneIds.City,
                CityRootName,
                (CityGameRoot root) => city = root);
            yield return WaitUntil(
                () => city.IsInitialized &&
                      !SceneTransitionService.IsTransitioning,
                "Church return to City did not settle.");

            Assert.That(city.World.ChurchPlan, Is.Not.Null);
            Vector3 expectedReturn = city.World.ChurchPlan.ReturnPosition;
            Vector3 actualReturn = city.Player.GameObject.transform.position;
            Assert.That(
                Vector2.Distance(
                    new Vector2(actualReturn.x, actualReturn.z),
                    new Vector2(expectedReturn.x, expectedReturn.z)),
                Is.LessThan(0.05f));
            Assert.That(GameSessionState.IsReturningToCity, Is.False);
            var teleportGround = new CityMapCityTeleportGround(city.Layout);
            Assert.That(
                teleportGround.TryResolveStandingPosition(
                    city.World.ChurchPlan.ModelFootprint.center,
                    out _),
                Is.False,
                "Map teleport must not land inside the church model.");
            LogAssert.NoUnexpectedReceived();
        }

        private static void AssertInteriorContract(
            ChurchInteriorRoot interior)
        {
            Assert.That(interior, Is.Not.Null);
            Assert.That(interior.World, Is.Not.Null);
            Assert.That(interior.World.Registry, Is.Not.Null);
            Assert.That(
                interior.World.Registry.Kind,
                Is.EqualTo(ChurchAssetKind.Interior));
            Assert.That(
                interior.World.Registry.BuildSignature,
                Is.Not.Empty);
            Assert.That(interior.Player.GameObject, Is.Not.Null);
            Assert.That(interior.Exit, Is.Not.Null);
            Assert.That(interior.Inventory, Is.Not.Null);
            Assert.That(interior.Journal, Is.Not.Null);
            Assert.That(interior.PauseMenu, Is.Not.Null);

            int blockingFixtureCount = interior.Layout.Fixtures.Count(
                fixture => fixture.BlocksMovement);
            Assert.That(
                interior.World.GameplayColliders,
                Has.Count.EqualTo(
                    RoomColliderCount + blockingFixtureCount));
            Assert.That(
                interior.World.GameplayColliders.All(
                    collider =>
                        collider != null &&
                        collider.enabled &&
                        collider.transform.IsChildOf(
                            interior.World.CollisionRoot)),
                Is.True);
            Assert.That(
                interior.World.Registry.GetComponentsInChildren<Collider>(
                    true),
                Is.Empty,
                "The imported church prefab must remain passive.");
        }

        private static void PlacePlayerAtDoor(
            PlayerRuntime player,
            Component door)
        {
            PlayerDoorActionTarget action =
                door.GetComponent<PlayerDoorActionTarget>();
            Assert.That(action, Is.Not.Null, door.name);
            Assert.That(action.IsConfigured, Is.True, door.name);
            player.Motor.Teleport(action.Plan.EntryRootPosition);
            player.GameObject.transform.rotation =
                action.Plan.EntryRotation;
            Physics.SyncTransforms();
        }

        private static IEnumerator WaitUntil(
            Func<bool> predicate,
            string failureMessage)
        {
            float deadline = Time.realtimeSinceStartup + TimeoutSeconds;
            while (!predicate() &&
                   Time.realtimeSinceStartup < deadline)
            {
                yield return null;
            }

            Assert.That(predicate(), Is.True, failureMessage);
        }

        private static IEnumerator WaitForLoadedRoot<T>(
            string sceneName,
            string exactRootName,
            Action<T> capture)
            where T : Component
        {
            float deadline = Time.realtimeSinceStartup + TimeoutSeconds;
            while (Time.realtimeSinceStartup < deadline)
            {
                Scene scene = SceneManager.GetActiveScene();
                T root = FindExactRoot<T>(scene, exactRootName);
                if (scene.name == sceneName && root != null)
                {
                    capture(root);
                    yield break;
                }

                yield return null;
            }

            Assert.Fail(
                $"Scene '{sceneName}' did not create root " +
                $"'{exactRootName}'.");
        }

        private static T FindExactRoot<T>(
            Scene scene,
            string exactRootName)
            where T : Component
        {
            if (!scene.IsValid() || !scene.isLoaded)
            {
                return null;
            }

            GameObject[] roots = scene.GetRootGameObjects();
            for (int index = 0; index < roots.Length; index++)
            {
                if (roots[index].name == exactRootName)
                {
                    return roots[index].GetComponent<T>();
                }
            }

            return null;
        }
    }
}

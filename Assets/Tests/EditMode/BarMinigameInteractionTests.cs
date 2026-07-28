using System;
using NUnit.Framework;
using UnityEngine;

namespace BarPromenade.Tests.EditMode
{
    public sealed class BarMinigameInteractionTests
    {
        private GameObject playerObject;
        private GameObject cameraObject;
        private GameObject hudObject;
        private GameObject stationObject;

        [TearDown]
        public void TearDown()
        {
            Destroy(stationObject);
            Destroy(hudObject);
            Destroy(cameraObject);
            Destroy(playerObject);
        }

        [Test]
        public void ModalLock_RestoresEveryCapturedStateExactly()
        {
            playerObject = new GameObject("Player");
            PlayerMotor motor =
                playerObject.AddComponent<PlayerMotor>();
            PlayerInteractor interactor =
                playerObject.AddComponent<PlayerInteractor>();

            cameraObject = new GameObject("Camera");
            cameraObject.AddComponent<Camera>();
            PlayerCameraFollow cameraFollow =
                cameraObject.AddComponent<PlayerCameraFollow>();

            hudObject = new GameObject("HUD");
            IntoxicationHudView hud =
                hudObject.AddComponent<IntoxicationHudView>();

            motor.SetInputEnabled(false);
            interactor.SetInputEnabled(true);
            cameraFollow.SetOrbitInputEnabled(false);
            cameraFollow.SetCinematicMotionEnabled(true);
            hud.Visible = true;

            BarMinigameModalLock modalLock =
                new BarMinigameModalLock();
            BarMinigameModalLock competingLock =
                new BarMinigameModalLock();
            Assert.That(
                modalLock.TryCaptureAndDisable(
                    interactor,
                    cameraFollow,
                    hud),
                Is.True);
            Assert.That(modalLock.IsLocked, Is.True);
            Assert.That(motor.InputEnabled, Is.False);
            Assert.That(interactor.InputEnabled, Is.False);
            Assert.That(cameraFollow.OrbitInputEnabled, Is.False);
            Assert.That(cameraFollow.CinematicMotionEnabled, Is.False);
            Assert.That(hud.Visible, Is.False);
            Assert.That(
                modalLock.TryCaptureAndDisable(
                    interactor,
                    cameraFollow,
                    hud),
                Is.False);
            Assert.That(
                competingLock.TryCaptureAndDisable(
                    interactor,
                    cameraFollow,
                    hud),
                Is.False);

            Assert.That(modalLock.Restore(), Is.True);
            Assert.That(modalLock.IsLocked, Is.False);
            Assert.That(motor.InputEnabled, Is.False);
            Assert.That(interactor.InputEnabled, Is.True);
            Assert.That(cameraFollow.OrbitInputEnabled, Is.False);
            Assert.That(cameraFollow.CinematicMotionEnabled, Is.True);
            Assert.That(hud.Visible, Is.True);
            Assert.That(modalLock.Restore(), Is.False);
            Assert.That(
                competingLock.TryCaptureAndDisable(
                    interactor,
                    cameraFollow,
                    hud),
                Is.True);
            Assert.That(competingLock.Restore(), Is.True);
        }

        [Test]
        public void ActivityStation_UsesConfiguredPromptAndMinigame()
        {
            playerObject = new GameObject("Player");
            PlayerInteractor interactor =
                playerObject.AddComponent<PlayerInteractor>();
            stationObject = new GameObject("Station");
            BarActivityStation station =
                stationObject.AddComponent<BarActivityStation>();
            StubMinigame minigame = new StubMinigame();

            station.Configure(
                minigame,
                "interaction.play_test_game");

            Assert.That(
                station.PromptKey,
                Is.EqualTo("interaction.play_test_game"));
            Assert.That(station.Minigame, Is.SameAs(minigame));
            Assert.That(station.CanInteract(interactor), Is.True);

            station.Interact(interactor);

            Assert.That(minigame.IsOpen, Is.True);
            Assert.That(minigame.OpenCount, Is.EqualTo(1));
            Assert.That(station.CanInteract(interactor), Is.False);
        }

        private static void Destroy(GameObject gameObject)
        {
            if (gameObject != null)
            {
                UnityEngine.Object.DestroyImmediate(gameObject);
            }
        }

        private sealed class StubMinigame : IBarMinigame
        {
            public bool IsOpen { get; private set; }
            public int OpenCount { get; private set; }
            public event Action Completed;

            public bool Open(PlayerInteractor interactor)
            {
                if (IsOpen || interactor == null)
                {
                    return false;
                }

                IsOpen = true;
                OpenCount++;
                return true;
            }

            public void Cancel()
            {
                IsOpen = false;
            }

            public void Complete()
            {
                Completed?.Invoke();
            }
        }
    }
}

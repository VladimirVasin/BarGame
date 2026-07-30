using System.Reflection;
using NUnit.Framework;
using UnityEngine;

namespace BarPromenade.Tests.EditMode
{
    public sealed class StairwellCatInteractionTests
    {
        private GameObject catObject;
        private GameObject playerObject;
        private StairwellCatInteraction cat;
        private PlayerInteractor interactor;
        private PlayerMotor motor;

        [SetUp]
        public void SetUp()
        {
            SetTransitioning(false);

            catObject = new GameObject("Stairwell Cat");
            cat = catObject.AddComponent<
                StairwellCatInteraction>();

            playerObject = new GameObject("Player");
            motor = playerObject.AddComponent<PlayerMotor>();
            interactor =
                playerObject.AddComponent<PlayerInteractor>();
        }

        [TearDown]
        public void TearDown()
        {
            SetTransitioning(false);
            Destroy(catObject);
            Destroy(playerObject);
        }

        [Test]
        public void Initialize_UsesAuthoredWorldInteractionPosition()
        {
            Vector3 authoredPosition =
                new Vector3(0.72f, 1.74f, 1.78f);

            cat.Initialize(authoredPosition);

            Assert.That(cat.IsInitialized, Is.True);
            Assert.That(
                cat.InteractionPosition,
                Is.EqualTo(authoredPosition));
            Assert.That(
                cat.GetPromptKeyAt(0f),
                Is.EqualTo(
                    StairwellCatInteraction.DefaultPromptKey));
            Assert.That(
                cat.CanInteract(interactor),
                Is.True);
        }

        [Test]
        public void InteractAt_ShowsResponseForExactlyTwoPointFiveSeconds()
        {
            cat.Initialize(Vector3.one);

            Assert.That(
                cat.InteractAt(interactor, 10f),
                Is.True);

            Assert.That(
                cat.GetPromptKeyAt(10f),
                Is.EqualTo(
                    StairwellCatInteraction.ResponsePromptKey));
            Assert.That(
                cat.GetPromptKeyAt(12.499f),
                Is.EqualTo(
                    StairwellCatInteraction.ResponsePromptKey));
            Assert.That(
                cat.GetPromptKeyAt(12.5f),
                Is.EqualTo(
                    StairwellCatInteraction.DefaultPromptKey));
            Assert.That(
                cat.GetPromptKeyAt(9.999f),
                Is.EqualTo(
                    StairwellCatInteraction.DefaultPromptKey));
        }

        [Test]
        public void RepeatedInteraction_RestartsResponseTimer()
        {
            cat.Initialize(Vector3.zero);

            Assert.That(
                cat.InteractAt(interactor, 3f),
                Is.True);
            Assert.That(
                cat.InteractAt(interactor, 5f),
                Is.True);

            Assert.That(
                cat.GetPromptKeyAt(7.499f),
                Is.EqualTo(
                    StairwellCatInteraction.ResponsePromptKey));
            Assert.That(
                cat.GetPromptKeyAt(7.5f),
                Is.EqualTo(
                    StairwellCatInteraction.DefaultPromptKey));
        }

        [Test]
        public void Interaction_RejectsUnavailableStatesWithoutLockingPlayer()
        {
            cat.Initialize(Vector3.zero);

            Assert.That(
                cat.InteractAt(null, 1f),
                Is.False);

            cat.enabled = false;
            Assert.That(
                cat.InteractAt(interactor, 2f),
                Is.False);
            cat.enabled = true;

            interactor.enabled = false;
            Assert.That(
                cat.InteractAt(interactor, 2.25f),
                Is.False);
            interactor.enabled = true;

            interactor.SetInputEnabled(false);
            Assert.That(
                cat.InteractAt(interactor, 2.5f),
                Is.False);
            interactor.SetInputEnabled(true);

            SetTransitioning(true);
            Assert.That(
                cat.InteractAt(interactor, 3f),
                Is.False);
            SetTransitioning(false);

            Assert.That(interactor.InputEnabled, Is.True);
            Assert.That(motor.InputEnabled, Is.True);
            Assert.That(
                cat.GetPromptKeyAt(3f),
                Is.EqualTo(
                    StairwellCatInteraction.DefaultPromptKey));

            Assert.That(
                cat.InteractAt(interactor, 4f),
                Is.True);
            Assert.That(interactor.InputEnabled, Is.True);
            Assert.That(motor.InputEnabled, Is.True);
        }

        private static void SetTransitioning(bool value)
        {
            PropertyInfo property = typeof(
                    SceneTransitionService)
                .GetProperty(
                    nameof(
                        SceneTransitionService.IsTransitioning),
                    BindingFlags.Public |
                    BindingFlags.Static);
            MethodInfo setter = property?.GetSetMethod(true);
            Assert.That(
                setter,
                Is.Not.Null,
                "Scene transition test hook is unavailable.");
            setter.Invoke(null, new object[] { value });
        }

        private static void Destroy(GameObject gameObject)
        {
            if (gameObject != null)
            {
                Object.DestroyImmediate(gameObject);
            }
        }
    }
}

using System;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using Object = UnityEngine.Object;

namespace BarPromenade.Tests.EditMode
{
    public sealed class PlayerPresentationVisibilityTests
    {
        private sealed class StubPresentation : IPlayerPresentation
        {
            public StubPresentation(params Renderer[] renderers)
            {
                Renderers = renderers;
            }

            public IReadOnlyList<Renderer> Renderers { get; }
            public Transform VisualRoot => null;
            public PlayerPresentationMetrics Metrics => default;
            public bool InteractionHandoffLocked { get; private set; }

            public void SetMotion(in PlayerMotionSample motion)
            {
            }

            public void SetIntoxication(float intensity)
            {
            }

            public void SetBalancePose(float signedLean)
            {
            }

            public void SetFallPose(float signedDirection, float amount)
            {
            }

            public void SetFallAnimation(
                PlayerFallAnimationPhase phase,
                float normalizedProgress)
            {
            }

            public void SetInteractionHandoffLocked(bool locked)
            {
                InteractionHandoffLocked = locked;
            }
        }

        private readonly List<GameObject> ownedObjects =
            new List<GameObject>();

        [TearDown]
        public void TearDown()
        {
            for (int index = ownedObjects.Count - 1;
                 index >= 0;
                 index--)
            {
                Object.DestroyImmediate(ownedObjects[index]);
            }

            ownedObjects.Clear();
        }

        [Test]
        public void HiddenLeases_NestByOwnerAndRestoreExactEnabledStates()
        {
            MeshRenderer enabledRenderer = CreateRenderer(true);
            MeshRenderer disabledRenderer = CreateRenderer(false);
            Light enabledShadow = CreateShadow(true);
            Light disabledShadow = CreateShadow(false);
            var visibility = new PlayerPresentationVisibility(
                new StubPresentation(
                    enabledRenderer,
                    disabledRenderer),
                enabledShadow,
                disabledShadow);
            object firstOwner = new object();
            object secondOwner = new object();

            IDisposable firstLease = visibility.AcquireHidden(firstOwner);
            IDisposable nestedLease = visibility.AcquireHidden(
                firstOwner,
                PlayerPresentationVisibilityScope.Renderers);
            IDisposable secondLease = visibility.AcquireHidden(secondOwner);

            Assert.That(visibility.IsHiddenBy(firstOwner), Is.True);
            Assert.That(visibility.IsHiddenBy(secondOwner), Is.True);
            Assert.That(enabledRenderer.enabled, Is.False);
            Assert.That(disabledRenderer.enabled, Is.False);
            Assert.That(enabledShadow.enabled, Is.False);
            Assert.That(disabledShadow.enabled, Is.False);

            firstLease.Dispose();
            nestedLease.Dispose();

            Assert.That(visibility.IsHiddenBy(firstOwner), Is.False);
            Assert.That(enabledRenderer.enabled, Is.False);
            Assert.That(enabledShadow.enabled, Is.False);

            secondLease.Dispose();

            Assert.That(visibility.IsHidden, Is.False);
            Assert.That(enabledRenderer.enabled, Is.True);
            Assert.That(disabledRenderer.enabled, Is.False);
            Assert.That(enabledShadow.enabled, Is.True);
            Assert.That(disabledShadow.enabled, Is.False);
        }

        private MeshRenderer CreateRenderer(bool enabled)
        {
            GameObject gameObject = Own("Presentation Renderer");
            MeshRenderer renderer =
                gameObject.AddComponent<MeshRenderer>();
            renderer.enabled = enabled;
            return renderer;
        }

        private Light CreateShadow(bool enabled)
        {
            GameObject gameObject = Own("Presentation Shadow");
            Light shadow = gameObject.AddComponent<Light>();
            shadow.enabled = enabled;
            return shadow;
        }

        private GameObject Own(string name)
        {
            var gameObject = new GameObject(name);
            ownedObjects.Add(gameObject);
            return gameObject;
        }
    }
}

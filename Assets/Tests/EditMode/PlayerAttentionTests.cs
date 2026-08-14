using NUnit.Framework;
using UnityEngine;

namespace BarPromenade.Tests.EditMode
{
    public sealed class PlayerAttentionTests
    {
        [Test]
        public void Rules_NoticeConeIsTightAndReleaseConeIsWide()
        {
            Vector3 player = Vector3.zero;

            // Ahead and close: noticed either way.
            Assert.That(
                PlayerAttentionRules.IsNoticeable(
                    player, 0f, new Vector3(0f, 1f, 3f), false),
                Is.True);

            // Past the notice radius but inside release: only a target
            // already held survives there.
            var farAhead = new Vector3(0f, 1f, 3.9f);
            Assert.That(
                PlayerAttentionRules.IsNoticeable(
                    player, 0f, farAhead, false),
                Is.False);
            Assert.That(
                PlayerAttentionRules.IsNoticeable(
                    player, 0f, farAhead, true),
                Is.True);

            // Wide off-axis: not fresh, held while current.
            var wideSide = Quaternion.Euler(0f, 85f, 0f) *
                           new Vector3(0f, 1f, 3f);
            Assert.That(
                PlayerAttentionRules.IsNoticeable(
                    player, 0f, wideSide, false),
                Is.False);
            Assert.That(
                PlayerAttentionRules.IsNoticeable(
                    player, 0f, wideSide, true),
                Is.True);

            // Straight behind: never.
            Assert.That(
                PlayerAttentionRules.IsNoticeable(
                    player, 0f, new Vector3(0f, 1f, -2f), true),
                Is.False);

            // Overhead: never a point of interest, even when held.
            Assert.That(
                PlayerAttentionRules.IsNoticeable(
                    player, 0f, new Vector3(0f, 2.6f, 2f), true),
                Is.False);
        }

        [Test]
        public void Rules_HeadAnglesClampToTheNeck()
        {
            PlayerAttentionRules.ResolveHeadAngles(
                new Vector3(0f, 1.6f, 0f),
                0f,
                new Vector3(3f, 1.6f, 0.2f),
                out float yaw,
                out float pitch);
            Assert.That(
                yaw,
                Is.EqualTo(PlayerAttentionRules.MaxHeadYawDegrees));
            Assert.That(pitch, Is.EqualTo(0f).Within(0.01f));

            // Downward at a floor item: the full chin drop.
            PlayerAttentionRules.ResolveHeadAngles(
                new Vector3(0f, 1.6f, 0f),
                0f,
                new Vector3(0f, -3f, 1f),
                out yaw,
                out pitch);
            Assert.That(yaw, Is.EqualTo(0f).Within(0.01f));
            Assert.That(
                pitch,
                Is.EqualTo(
                    -PlayerAttentionRules.MaxHeadDownPitchDegrees));

            // Upward: barely a lift, never a crane.
            PlayerAttentionRules.ResolveHeadAngles(
                new Vector3(0f, 1.6f, 0f),
                0f,
                new Vector3(0f, 6f, 1f),
                out yaw,
                out pitch);
            Assert.That(
                pitch,
                Is.EqualTo(PlayerAttentionRules.MaxHeadUpPitchDegrees));
        }

        [Test]
        public void Controller_PrefersCharactersAndReleasesBehind()
        {
            // Everything under one composition root, the way every
            // gameplay scene actually parents the player and the
            // world: sharing a root must never read as "self".
            var sceneRoot = new GameObject("Attention Test Root");
            var playerObject = new GameObject("Attention Test Player");
            var interactableObject = new GameObject("Stub Interactable");
            var characterObject = new GameObject("Stub Character");
            playerObject.transform.SetParent(sceneRoot.transform);
            interactableObject.transform.SetParent(sceneRoot.transform);
            characterObject.transform.SetParent(sceneRoot.transform);
            try
            {
                PlayerInteractor interactor =
                    playerObject.AddComponent<PlayerInteractor>();
                interactor.Initialize(null);
                PlayerAttentionController controller =
                    playerObject
                        .AddComponent<PlayerAttentionController>();
                controller.Initialize(interactor, null);

                interactableObject.transform.position =
                    new Vector3(0f, 0.8f, 2.4f);
                BoxCollider stubCollider =
                    interactableObject.AddComponent<BoxCollider>();
                stubCollider.isTrigger = true;
                interactableObject.AddComponent<StubInteractable>();

                characterObject.transform.position =
                    new Vector3(1.2f, 0f, 2.6f);
                PlayerAttentionMagnet magnet =
                    characterObject
                        .AddComponent<PlayerAttentionMagnet>();
                Physics.SyncTransforms();

                // A person outranks a closer object.
                controller.ScanNow();
                Assert.That(controller.CurrentFocus, Is.Not.Null);
                Assert.That(
                    Vector3.Distance(
                        controller.CurrentFocus.Value,
                        magnet.FocusPosition),
                    Is.LessThan(0.001f));

                // Without the person, the interactable takes the head.
                magnet.enabled = false;
                controller.ScanNow();
                Assert.That(controller.CurrentFocus, Is.Not.Null);
                Assert.That(
                    Vector3.Distance(
                        controller.CurrentFocus.Value,
                        interactableObject.transform.position),
                    Is.LessThan(0.001f));

                // Turned away, everything leaves the cone.
                playerObject.transform.rotation =
                    Quaternion.Euler(0f, 180f, 0f);
                controller.ScanNow();
                Assert.That(controller.CurrentFocus, Is.Null);
            }
            finally
            {
                Object.DestroyImmediate(sceneRoot);
            }
        }

        private sealed class StubInteractable :
            MonoBehaviour,
            IInteractable
        {
            public string PromptKey => "test.stub";
            public Vector3 InteractionPosition => transform.position;

            public bool CanInteract(PlayerInteractor interactor)
            {
                return true;
            }

            public void Interact(PlayerInteractor interactor)
            {
            }
        }
    }
}

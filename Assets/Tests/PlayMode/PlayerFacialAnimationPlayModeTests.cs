using System.Collections;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace BarPromenade.Tests
{
    public sealed class PlayerFacialAnimationPlayModeTests
    {
        private readonly List<GameObject> cleanupObjects =
            new List<GameObject>();

        [UnityTest]
        public IEnumerator Rig_BlinksBySwappingBodyWithoutAddingRenderers()
        {
            Camera camera = CreateCamera();
            GameObject actor = CreateObject("Facial Animation Actor");
            PlaceCameraAtRelativeYaw(camera, actor.transform, 0f);
            GameObject rigObject = CreateObject("Facial Animation Rig");
            rigObject.transform.SetParent(actor.transform, false);
            PlayerSpriteRig rig =
                rigObject.AddComponent<PlayerSpriteRig>();

            rig.Initialize(camera, actor.transform);
            yield return null;

            Assert.That(
                System.Enum.GetValues(
                    typeof(PlayerFacialExpression)),
                Has.Length.EqualTo(PlayerSpriteRig.ExpressionCount));
            Assert.That(
                rigObject.GetComponentsInChildren<SpriteRenderer>(true),
                Has.Length.EqualTo(PlayerSpriteRig.PartCount));
            Assert.That(
                rig.CurrentFacialExpression,
                Is.EqualTo(PlayerFacialExpression.Neutral));
            Assert.That(
                rig.BodyRenderer.sprite,
                Is.SameAs(rig.GetPartSprite(
                    PlayerPuppetPart.Body,
                    PlayerViewDirection.Front)));

            var expressionSprites = new HashSet<Sprite>();
            for (int expressionIndex = 0;
                 expressionIndex < PlayerSpriteRig.ExpressionCount;
                 expressionIndex++)
            {
                PlayerFacialExpression expression =
                    (PlayerFacialExpression)expressionIndex;
                for (int directionIndex = 0;
                     directionIndex < PlayerSpriteRig.DirectionCount;
                     directionIndex++)
                {
                    PlayerViewDirection direction =
                        (PlayerViewDirection)directionIndex;
                    Sprite sprite = rig.GetFacialExpressionSprite(
                        expression,
                        direction);
                    Assert.That(expressionSprites.Add(sprite), Is.True);
                    Assert.That(
                        sprite.rect.x,
                        Is.EqualTo(directionIndex *
                                   PlayerSpriteRig.FrameWidth));
                    Assert.That(
                        sprite.rect.y,
                        Is.EqualTo(expressionIndex *
                                   PlayerSpriteRig.FrameHeight));
                    Assert.That(
                        sprite.pixelsPerUnit,
                        Is.EqualTo(PlayerSpriteRig.PixelsPerUnit));
                }
            }

            Assert.That(
                expressionSprites,
                Has.Count.EqualTo(
                    PlayerSpriteRig.DirectionCount *
                    PlayerSpriteRig.ExpressionCount));

            float deadline = Time.realtimeSinceStartup + 6.5f;
            while (rig.CurrentFacialExpression !=
                   PlayerFacialExpression.ClosedBlink &&
                   Time.realtimeSinceStartup < deadline)
            {
                yield return null;
            }

            Assert.That(
                rig.CurrentFacialExpression,
                Is.EqualTo(PlayerFacialExpression.ClosedBlink));
            Assert.That(
                rig.BodyRenderer.sprite,
                Is.SameAs(rig.GetFacialExpressionSprite(
                    PlayerFacialExpression.ClosedBlink,
                    PlayerViewDirection.Front)));
            AssertNeutralLimbSprites(rig, PlayerViewDirection.Front);

            PlaceCameraAtRelativeYaw(camera, actor.transform, 180f);
            yield return null;

            Assert.That(
                rig.CurrentDirection,
                Is.EqualTo(PlayerViewDirection.Back));
            Assert.That(
                rig.CurrentFacialExpression,
                Is.Not.EqualTo(PlayerFacialExpression.Neutral));
            Assert.That(
                rig.BodyRenderer.sprite,
                Is.SameAs(rig.GetPartSprite(
                    PlayerPuppetPart.Body,
                    PlayerViewDirection.Back)),
                "Rear views must not display an invented face.");
            AssertNeutralLimbSprites(rig, PlayerViewDirection.Back);
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            for (int index = cleanupObjects.Count - 1;
                 index >= 0;
                 index--)
            {
                if (cleanupObjects[index] != null)
                {
                    Object.Destroy(cleanupObjects[index]);
                }
            }

            cleanupObjects.Clear();
            yield return null;
            yield return null;
        }

        private Camera CreateCamera()
        {
            return CreateObject("Facial Animation Camera")
                .AddComponent<Camera>();
        }

        private GameObject CreateObject(string objectName)
        {
            GameObject gameObject = new GameObject(objectName);
            cleanupObjects.Add(gameObject);
            return gameObject;
        }

        private static void PlaceCameraAtRelativeYaw(
            Camera camera,
            Transform actor,
            float relativeYaw)
        {
            Vector3 flatOffset =
                Quaternion.AngleAxis(relativeYaw, Vector3.up) *
                actor.forward *
                7f;
            Vector3 focusPoint = actor.position + Vector3.up;
            camera.transform.position =
                actor.position + flatOffset + Vector3.up * 4f;
            camera.transform.LookAt(focusPoint);
        }

        private static void AssertNeutralLimbSprites(
            PlayerSpriteRig rig,
            PlayerViewDirection direction)
        {
            for (int partIndex = 1;
                 partIndex < PlayerSpriteRig.PartCount;
                 partIndex++)
            {
                PlayerPuppetPart part =
                    (PlayerPuppetPart)partIndex;
                Assert.That(
                    rig.GetPartRenderer(part).sprite,
                    Is.SameAs(rig.GetPartSprite(part, direction)));
            }
        }
    }
}

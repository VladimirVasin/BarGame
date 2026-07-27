using System.Collections;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace BarPromenade.Tests
{
    public sealed class PlayerPresentationPlayModeTests
    {
        private readonly List<GameObject> cleanupObjects = new List<GameObject>();

        [UnityTest]
        public IEnumerator InitializedRig_CreatesThirteenVisualOnlyRenderers()
        {
            Camera camera = CreateCamera(new Vector3(6f, 5f, -8f));
            GameObject rigObject = CreateObject("Presentation Test Rig");
            PlayerSpriteRig rig = rigObject.AddComponent<PlayerSpriteRig>();

            rig.Initialize(camera);
            yield return null;

            SpriteRenderer[] renderers =
                rigObject.GetComponentsInChildren<SpriteRenderer>(true);
            Assert.That(renderers, Has.Length.EqualTo(13));
            Assert.That(
                rigObject.GetComponentsInChildren<Collider>(true),
                Is.Empty,
                "Generated visual descendants must not contain 3D colliders.");
            Assert.That(
                rigObject.GetComponentsInChildren<Rigidbody>(true),
                Is.Empty,
                "Generated visual descendants must not contain 3D rigidbodies.");
            Assert.That(
                rigObject.GetComponentsInChildren<Collider2D>(true),
                Is.Empty,
                "Generated visual descendants must not contain 2D colliders.");
            Assert.That(
                rigObject.GetComponentsInChildren<Rigidbody2D>(true),
                Is.Empty,
                "Generated visual descendants must not contain 2D rigidbodies.");
        }

        [UnityTest]
        public IEnumerator Billboard_FacesCameraAndKeepsWorldUp_AfterFrame()
        {
            Camera camera = CreateCamera(new Vector3(6f, 5f, -8f));
            GameObject billboardObject = CreateObject("Billboard Test");
            billboardObject.transform.position = new Vector3(-2f, 0.5f, 1f);
            BillboardSprite billboard = billboardObject.AddComponent<BillboardSprite>();

            billboard.Initialize(camera);
            yield return null;

            Vector3 expectedForward = Vector3.ProjectOnPlane(
                camera.transform.position - billboardObject.transform.position,
                Vector3.up).normalized;

            Assert.That(
                Vector3.Angle(billboardObject.transform.forward, expectedForward),
                Is.LessThan(0.1f));
            Assert.That(
                Vector3.Angle(billboardObject.transform.up, Vector3.up),
                Is.LessThan(0.1f));
        }

        [UnityTest]
        public IEnumerator SetMotion_AnimatesPoseAndFlipsTowardMovement()
        {
            Camera camera = CreateCamera(new Vector3(0f, 7f, -10f));
            camera.transform.rotation = Quaternion.Euler(35f, 0f, 0f);
            GameObject rigObject = CreateObject("Animated Presentation Test Rig");
            PlayerSpriteRig rig = rigObject.AddComponent<PlayerSpriteRig>();
            rig.Initialize(camera);

            Transform visualRoot = rigObject.transform.Find("GeneratedSpriteRig");
            Assert.That(visualRoot, Is.Not.Null);
            Transform leftUpperArm = visualRoot.Find("LeftUpperArm");
            Assert.That(leftUpperArm, Is.Not.Null);
            Quaternion idleArmRotation = leftUpperArm.localRotation;

            Assert.DoesNotThrow(() => rig.SetMotion(Vector3.right * 4f));
            float poseDeadline = Time.realtimeSinceStartup + 1f;
            while (Quaternion.Angle(
                       idleArmRotation,
                       leftUpperArm.localRotation) <= 0.05f &&
                   Time.realtimeSinceStartup < poseDeadline)
            {
                yield return null;
            }

            Assert.That(
                Quaternion.Angle(idleArmRotation, leftUpperArm.localRotation),
                Is.GreaterThan(0.05f),
                "Walking motion should produce a non-idle limb pose.");
            Assert.That(
                visualRoot.localScale.x,
                Is.GreaterThan(0f),
                "Positive camera-right movement should face right.");

            Assert.DoesNotThrow(() => rig.SetMotion(Vector3.left * 4f));
            yield return null;

            Assert.That(
                visualRoot.localScale.x,
                Is.LessThan(0f),
                "Negative camera-right movement should horizontally flip the rig.");
        }

        [UnityTest]
        public IEnumerator ExteriorCamera_RemainsBehindPlayerDuringYawChange()
        {
            Camera camera = CreateCamera(Vector3.zero);
            GameObject player = CreateObject("Third Person Camera Target");
            player.transform.position = new Vector3(0f, 100f, 0f);
            player.transform.rotation = Quaternion.Euler(0f, 90f, 0f);

            CharacterController controller =
                player.AddComponent<CharacterController>();
            PlayerMotor motor = player.AddComponent<PlayerMotor>();
            motor.Initialize(camera, null, null);

            GameObject billboardObject = CreateObject("Turning Billboard");
            billboardObject.transform.position =
                player.transform.position + Vector3.up;
            BillboardSprite billboard =
                billboardObject.AddComponent<BillboardSprite>();
            billboard.Initialize(camera);

            PlayerCameraFollow follow =
                camera.gameObject.AddComponent<PlayerCameraFollow>();
            follow.Initialize(camera, player.transform, false);
            yield return null;

            Vector3 cameraForward = Vector3.ProjectOnPlane(
                camera.transform.forward,
                Vector3.up).normalized;
            Vector3 playerToCamera = Vector3.ProjectOnPlane(
                camera.transform.position - player.transform.position,
                Vector3.up).normalized;

            Assert.That(camera.orthographic, Is.False);
            Assert.That(
                Vector3.Angle(player.transform.forward, cameraForward),
                Is.LessThan(0.1f),
                "The player heading should match the chase-camera heading.");
            Assert.That(
                Vector3.Angle(playerToCamera, -player.transform.forward),
                Is.LessThan(0.1f),
                "The camera should remain directly behind the player.");
            Assert.That(controller, Is.Not.Null);

            follow.RotateYaw(135f);
            yield return null;

            Vector3 focusPoint = player.transform.position + Vector3.up * 1.1f;
            cameraForward = Vector3.ProjectOnPlane(
                camera.transform.forward,
                Vector3.up).normalized;
            playerToCamera = Vector3.ProjectOnPlane(
                camera.transform.position - player.transform.position,
                Vector3.up).normalized;

            Assert.That(
                Vector3.Angle(
                    camera.transform.forward,
                    (focusPoint - camera.transform.position).normalized),
                Is.LessThan(0.1f),
                "Yaw smoothing must not turn the view away from the player.");
            Assert.That(
                Vector3.Angle(player.transform.forward, cameraForward),
                Is.LessThan(0.1f),
                "Player and camera heading must change together.");
            Assert.That(
                Vector3.Angle(playerToCamera, -player.transform.forward),
                Is.LessThan(0.1f),
                "The camera must stay behind the player throughout a turn.");

            Vector3 expectedBillboardForward = Vector3.ProjectOnPlane(
                camera.transform.position - billboardObject.transform.position,
                Vector3.up).normalized;
            Assert.That(
                Vector3.Angle(
                    billboardObject.transform.forward,
                    expectedBillboardForward),
                Is.LessThan(0.1f),
                "The billboard must update after the chase camera.");
        }

        [UnityTest]
        public IEnumerator ExteriorCamera_ObstacleImmediatelyShortensCameraArm()
        {
            Camera camera = CreateCamera(Vector3.zero);
            GameObject player = CreateObject("Obstructed Camera Target");
            player.transform.position = new Vector3(0f, 100f, 0f);

            PlayerCameraFollow follow =
                camera.gameObject.AddComponent<PlayerCameraFollow>();
            follow.Initialize(camera, player.transform, false);
            yield return null;

            Vector3 focusPoint = player.transform.position + Vector3.up * 1.1f;
            float unobstructedDistance = Vector3.Distance(
                focusPoint,
                camera.transform.position);
            Vector3 cameraDirection =
                (camera.transform.position - focusPoint).normalized;
            GameObject wall = CreateObject("Camera Obstacle");
            wall.transform.position = focusPoint + cameraDirection * 1.5f;
            BoxCollider wallCollider = wall.AddComponent<BoxCollider>();
            wallCollider.size = new Vector3(4f, 4f, 0.25f);
            Physics.SyncTransforms();
            yield return null;

            float actualDistance = Vector3.Distance(
                focusPoint,
                camera.transform.position);
            Assert.That(unobstructedDistance, Is.GreaterThan(5f));
            Assert.That(
                actualDistance,
                Is.LessThan(1.5f),
                "An obstacle must pull the camera inward before rendering.");
            Assert.That(
                Vector3.Angle(
                    camera.transform.forward,
                    (focusPoint - camera.transform.position).normalized),
                Is.LessThan(0.1f),
                "Collision handling must keep the player centered.");
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            for (int i = cleanupObjects.Count - 1; i >= 0; i--)
            {
                if (cleanupObjects[i] != null)
                {
                    Object.Destroy(cleanupObjects[i]);
                }
            }

            cleanupObjects.Clear();
            yield return null;
            yield return null;
        }

        private Camera CreateCamera(Vector3 position)
        {
            GameObject cameraObject = CreateObject("Presentation Test Camera");
            cameraObject.transform.position = position;
            return cameraObject.AddComponent<Camera>();
        }

        private GameObject CreateObject(string objectName)
        {
            GameObject gameObject = new GameObject(objectName);
            cleanupObjects.Add(gameObject);
            return gameObject;
        }
    }
}

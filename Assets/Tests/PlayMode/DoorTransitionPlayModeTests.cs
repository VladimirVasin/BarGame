using System;
using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace BarPromenade.Tests.PlayMode
{
    public sealed class DoorTransitionPlayModeTests
    {
        private const string RootName =
            "[Bar Promenade] Door Transition Runtime";
        private const float TimeoutSeconds = 15f;

        [UnityTest]
        public IEnumerator DirectLoad_IsIdleUntilExplicitInitialization()
        {
            Assert.That(
                Application.CanStreamedLevelBeLoaded(
                    SceneIds.DoorTransition),
                Is.True,
                "DoorTransition must be enabled in Build Settings.");

            DoorTransitionRoot root = null;
            yield return LoadSceneAndWaitForRoot(
                foundRoot => root = foundRoot);

            Assert.That(
                SceneManager.GetActiveScene().name,
                Is.EqualTo(SceneIds.DoorTransition));
            Assert.That(root, Is.Not.Null);
            Assert.That(CountExactRoots(), Is.EqualTo(1));
            Assert.That(root.IsInitialized, Is.False);
            Assert.That(root.IsComplete, Is.False);
            Assert.That(root.Camera, Is.Null);
            Assert.That(root.DoorPivot, Is.Null);
            Assert.That(root.HandlePivot, Is.Null);
            Assert.That(
                UnityEngine.Object.FindObjectsByType<CityGameRoot>(
                    FindObjectsInactive.Include),
                Is.Empty);
            Assert.That(
                UnityEngine.Object.FindObjectsByType<BarInteriorRoot>(
                    FindObjectsInactive.Include),
                Is.Empty);

            root.Initialize(DoorTransitionDirection.EnterBar);
            yield return null;

            Assert.That(root.IsInitialized, Is.True);
            Assert.That(root.IsComplete, Is.False);
            Assert.That(
                root.Direction,
                Is.EqualTo(DoorTransitionDirection.EnterBar));
            Assert.That(root.Camera, Is.Not.Null);
            Assert.That(root.Camera, Is.SameAs(Camera.main));
            Assert.That(
                root.Camera.transform.IsChildOf(root.transform),
                Is.True);
            Assert.That(
                root.Camera.clearFlags,
                Is.EqualTo(CameraClearFlags.SolidColor));
            Assert.That(root.Camera.backgroundColor, Is.EqualTo(Color.black));
            Assert.That(
                root.Camera.farClipPlane,
                Is.EqualTo(
                    RuntimeSceneSetup.DoorTransitionFarClipPlane)
                    .Within(0.01f));
            Assert.That(RenderSettings.fog, Is.False);
            Assert.That(root.DoorPivot, Is.Not.Null);
            Assert.That(root.HandlePivot, Is.Not.Null);
            Assert.That(root.OpeningRenderer, Is.Not.Null);
            Assert.That(root.OpeningRenderer.sprite, Is.Not.Null);
            Assert.That(
                root.OpeningRenderer.color,
                Is.EqualTo(Color.black));
            Assert.That(
                root.OpeningRenderer.gameObject.name,
                Is.EqualTo("Black Door Opening"));
            Assert.That(
                root.OpeningRenderer.transform.localScale.x,
                Is.GreaterThanOrEqualTo(1.82f));
            Assert.That(
                root.OpeningRenderer.transform.localScale.y,
                Is.GreaterThanOrEqualTo(2.86f));
            Assert.That(
                root.DoorPivot.IsChildOf(root.transform),
                Is.True);
            Assert.That(
                root.HandlePivot.IsChildOf(root.DoorPivot),
                Is.True);
            Assert.That(
                root.CurrentPose.BlackOpacity,
                Is.EqualTo(1f).Within(0.0001f));
            Assert.That(root.CurrentPose.DoorOpen, Is.Zero);

            Renderer[] renderers =
                root.GetComponentsInChildren<Renderer>(true);
            Assert.That(renderers, Is.Not.Empty);
            Assert.That(
                root.GetComponentsInChildren<Collider>(true),
                Is.Empty,
                "The door vignette must remain presentation-only.");

            AudioListener[] listeners =
                UnityEngine.Object.FindObjectsByType<AudioListener>(
                    FindObjectsInactive.Include);
            Assert.That(listeners, Has.Length.EqualTo(1));
            Assert.That(
                listeners[0].gameObject,
                Is.SameAs(root.Camera.gameObject));

            int presentationCount = root.transform.childCount;
            Camera originalCamera = root.Camera;
            root.Initialize(DoorTransitionDirection.ExitBar);

            Assert.That(
                root.Direction,
                Is.EqualTo(DoorTransitionDirection.EnterBar),
                "Repeated initialization must not rebuild or retheme " +
                "an active presentation.");
            Assert.That(root.Camera, Is.SameAs(originalCamera));
            Assert.That(
                root.transform.childCount,
                Is.EqualTo(presentationCount));

            yield return root.Play();

            Assert.That(
                Vector3.Dot(root.DoorPivot.right, Vector3.back),
                Is.GreaterThan(0.95f),
                "The door leaf must swing toward the camera/player.");
        }

        private static IEnumerator LoadSceneAndWaitForRoot(
            Action<DoorTransitionRoot> capture)
        {
            AsyncOperation operation = SceneManager.LoadSceneAsync(
                SceneIds.DoorTransition,
                LoadSceneMode.Single);
            Assert.That(operation, Is.Not.Null);

            float deadline =
                Time.realtimeSinceStartup + TimeoutSeconds;
            while (!operation.isDone &&
                   Time.realtimeSinceStartup < deadline)
            {
                yield return null;
            }

            Assert.That(
                operation.isDone,
                Is.True,
                "Timed out loading DoorTransition.");

            deadline = Time.realtimeSinceStartup + TimeoutSeconds;
            while (Time.realtimeSinceStartup < deadline)
            {
                Scene scene = SceneManager.GetActiveScene();
                DoorTransitionRoot root = FindExactRoot(scene);
                if (scene.name == SceneIds.DoorTransition &&
                    root != null)
                {
                    capture(root);
                    yield break;
                }

                yield return null;
            }

            Assert.Fail(
                $"Scene '{SceneIds.DoorTransition}' did not create " +
                $"exact root '{RootName}'.");
        }

        private static DoorTransitionRoot FindExactRoot(Scene scene)
        {
            if (!scene.IsValid() || !scene.isLoaded)
            {
                return null;
            }

            GameObject[] roots = scene.GetRootGameObjects();
            for (int index = 0; index < roots.Length; index++)
            {
                if (roots[index].name == RootName)
                {
                    return roots[index]
                        .GetComponent<DoorTransitionRoot>();
                }
            }

            return null;
        }

        private static int CountExactRoots()
        {
            Scene scene = SceneManager.GetActiveScene();
            int count = 0;
            GameObject[] roots = scene.GetRootGameObjects();
            for (int index = 0; index < roots.Length; index++)
            {
                if (roots[index].name == RootName)
                {
                    count++;
                }
            }

            return count;
        }
    }
}

using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace BarPromenade.Tests.PlayMode
{
    /// <summary>
    /// The mother is in her chair when the room opens, her hips are on the
    /// cushion, and the chair is rocking.
    ///
    /// A separate file from `MothersHouseInteriorPlayModeTests` for the same
    /// reason the sofa test is: that one pins the exact contents of
    /// `World.GameplayColliders` and the room's renderer count, and this
    /// feature's job is to leave both untouched rather than to join them.
    /// </summary>
    public sealed class MothersHouseMotherPlayModeTests
    {
        private const string InteriorRootName =
            "[Bar Promenade] Mother's House Interior Runtime";
        private const float TimeoutSeconds = 60f;

        [SetUp]
        public void SetUp()
        {
            Time.captureDeltaTime = 1f / 60f;
            GameSessionState.BeginNewGame();
        }

        [TearDown]
        public void TearDown()
        {
            Time.captureDeltaTime = 0f;
        }

        [UnityTest]
        public IEnumerator SheIsSeatedAndTheChairRocks()
        {
            MothersHouseInteriorRoot interior = null;
            yield return LoadRoom(found => interior = found);

            Assert.That(
                interior.Mother,
                Is.Not.Null,
                "She is present from the first visit.");
            Assert.That(interior.Mother.IsInitialized, Is.True);
            Assert.That(interior.ChairMotion, Is.Not.Null);
            Assert.That(interior.ChairMotion.IsInitialized, Is.True);
            Assert.That(
                interior.ChairMotion.RiderCount,
                Is.EqualTo(3),
                "The frame, the cushion and the woman ride one angle.");

            // THE CULLING TRAP. Batch mode renders nothing, so a rig left on
            // CullUpdateTransforms reads back in its BIND pose and every
            // assertion below would describe a standing A-pose while passing
            // happily. This must come before the first pose is read.
            CityPedestrianAssetRegistry registry =
                interior.Mother.Registry;
            registry.Animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
            yield return null;
            yield return null;

            Transform room = interior.World.Root;
            Vector3 localPelvis = room.InverseTransformPoint(
                registry.PelvisAnchor.position);
            Assert.That(
                localPelvis.y,
                Is.EqualTo(
                        MothersHouseMotherPresentation.CushionTopY +
                        MothersHouseMotherPresentation.PerchPelvisLiftMeters)
                    .Within(0.02f),
                "Her hips must sit on the drawn cushion, not in it.");
            Assert.That(
                localPelvis.x,
                Is.InRange(-0.27f, 0.31f),
                "Her hips must be over the cushion in X.");
            Assert.That(
                localPelvis.z,
                Is.InRange(1.26f, 1.80f),
                "Her hips must be over the cushion in Z.");

            // Her soles reach the boards. The generator measured the seat at
            // 0.5714 m over her own soles against a 0.5700 m cushion, so if
            // the hips are right the feet are too - unless something has
            // moved her vertically since.
            float lowestSole = Mathf.Min(
                room.InverseTransformPoint(
                    registry.LeftFootAnchor.position).y,
                room.InverseTransformPoint(
                    registry.RightFootAnchor.position).y);
            Assert.That(
                lowestSole,
                Is.LessThan(0.25f),
                "She is not sitting with her feet in the air.");

            // SHE FACES THE ROOM, NOT THE HEARTH.
            //
            // Measured on her ROOT, not on the face patch. Dynamic skinned
            // bounds now keep her modular parts visible, but a renderer AABB
            // is still a geometry envelope rather than a semantic facing
            // direction.
            Vector3 localForward = room.InverseTransformDirection(
                interior.Mother.transform.forward);
            Assert.That(
                Vector3.Dot(localForward, Vector3.back),
                Is.GreaterThan(0.999f),
                "The chair has its back to the hearth and she faces the " +
                "room; she cannot be turned the other way.");

            yield return null;
        }

        [UnityTest]
        public IEnumerator TheChairKeepsRockingAndCarriesHerWithIt()
        {
            MothersHouseInteriorRoot interior = null;
            yield return LoadRoom(found => interior = found);

            MothersHouseRockingChairMotion motion = interior.ChairMotion;
            CityPedestrianAssetRegistry registry =
                interior.Mother.Registry;
            registry.Animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
            yield return null;

            float firstAngle = motion.AngleDegrees;
            Vector3 firstHead = registry.HeadAnchor.position;
            Transform room = interior.World.Root;
            Assert.That(interior.World.Registry.TryGetPart(
                MothersHouseInteriorRoot.RockingChairFrameName, out var framePart), Is.True);
            Assert.That(interior.World.Registry.TryGetPart("DRESS_Rug", out var rug), Is.True);
            Transform frame = framePart.Renderer.transform;
            Vector3[] vertices = ReadVertices(frame.GetComponent<MeshFilter>().sharedMesh);
            float rugTop = room.InverseTransformPoint(rug.Renderer.bounds.max).y;
            Vector3 motherInFrame = frame.InverseTransformPoint(interior.Mother.transform.position);
            float minAngle = firstAngle, maxAngle = firstAngle;
            float maxContactError = 0f, maxSlip = 0f, headTravel = 0f;

            CaptureRockingFrame("mother-rocking-gameplay", false, room);

            // One complete cycle checks both support edges and the handover
            // through zero. Inspect real imported vertices, not renderer AABBs
            // or a repeated copy of the runtime pivot calculation.
            int frames = Mathf.CeilToInt(
                MothersHouseRockingChairMotion.PeriodSeconds * 60f);
            for (int index = 0; index < frames; index++)
            {
                yield return null;
                minAngle = Mathf.Min(minAngle, motion.AngleDegrees);
                maxAngle = Mathf.Max(maxAngle, motion.AngleDegrees);
                headTravel = Mathf.Max(headTravel,
                    Vector3.Distance(firstHead, registry.HeadAnchor.position));
                Vector3 left = new Vector3(0f, float.PositiveInfinity, 0f);
                Vector3 right = left;
                foreach (Vector3 vertex in vertices)
                {
                    Vector3 point = room.InverseTransformPoint(frame.TransformPoint(vertex));
                    if (point.x < 0f && point.y < left.y) left = point;
                    if (point.x > 0f && point.y < right.y) right = point;
                }

                maxContactError = Mathf.Max(maxContactError,
                    Mathf.Abs(left.y - rugTop), Mathf.Abs(right.y - rugTop));
                Assert.That(left.y, Is.EqualTo(rugTop).Within(0.0002f),
                    $"Left runner lost the rug at {motion.AngleDegrees:F3} degrees.");
                Assert.That(right.y, Is.EqualTo(rugTop).Within(0.0002f),
                    "Both runners must remain supported throughout the cycle.");
                if (Mathf.Abs(motion.AngleDegrees) > 0.02f)
                {
                    float expectedZ = motion.AngleDegrees > 0f ? 1.55697441f : 1.54302561f;
                    maxSlip = Mathf.Max(maxSlip,
                        Mathf.Abs(left.z - expectedZ), Mathf.Abs(right.z - expectedZ));
                    Assert.That(left.z, Is.EqualTo(expectedZ).Within(0.0002f),
                        "The supporting edge must stay planted, not slide over the rug.");
                    Assert.That(right.z, Is.EqualTo(expectedZ).Within(0.0002f));
                }

                Assert.That(Vector3.Distance(motherInFrame,
                        frame.InverseTransformPoint(interior.Mother.transform.position)),
                    Is.LessThan(0.000002f), "The chair and sitter share one rigid motion.");
                Assert.That(interior.Mother.transform.InverseTransformPoint(
                        registry.PelvisAnchor.position).y,
                    Is.EqualTo(MothersHouseMotherPresentation.CushionTopY +
                        MothersHouseMotherPresentation.PerchPelvisLiftMeters).Within(0.0001f),
                    "Pelvis height follows the tilted seat normal, not world vertical.");
                if (index % 3 == 0)
                {
                    CaptureRockingFrame($"mother-rocking-motion/{index / 3:D3}", true, room);
                }
            }

            Assert.That(
                maxAngle - minAngle,
                Is.GreaterThan(4.99f),
                "The complete quiet swing remains visible in both directions.");
            Assert.That(
                Mathf.Abs(motion.AngleDegrees),
                Is.LessThanOrEqualTo(
                    MothersHouseRockingChairMotion.AmplitudeDegrees + 0.001f),
                "The rock must stay inside its own amplitude.");

            // SHE RIDES IT. The whole design is one angle moving both, so her
            // head must travel with the timber rather than hang still while
            // the chair swings out from under her.
            Assert.That(
                headTravel,
                Is.GreaterThan(0.08f),
                "She must move with the chair, not sit through it.");

            float previousTimeScale = Time.timeScale;
            try
            {
                Time.timeScale = 0f;
                // A coroutine resumes before LateUpdate; the current frame
                // still carries the delta computed before timeScale changed.
                yield return null;
                float pausedAngle = motion.AngleDegrees;
                Vector3 pausedPosition = interior.Mother.transform.position;
                yield return null;
                yield return null;
                Assert.That(motion.AngleDegrees, Is.EqualTo(pausedAngle).Within(0.00001f));
                Assert.That(Vector3.Distance(pausedPosition, interior.Mother.transform.position),
                    Is.LessThan(0.00001f));
            }
            finally { Time.timeScale = previousTimeScale; }
            TestContext.Out.WriteLine($"Runner contact error {maxContactError:F7} m; " +
                $"support slip {maxSlip:F7} m; head travel {headTravel:F4} m; " +
                $"angles {minAngle:F3}..{maxAngle:F3}. Captured 64 frames at 20 fps.");
        }

        private static Vector3[] ReadVertices(Mesh mesh)
        {
#if UNITY_EDITOR
            using (Mesh.MeshDataArray data = UnityEditor.MeshUtility.AcquireReadOnlyMeshData(mesh))
            using (var positions = new Unity.Collections.NativeArray<Vector3>(
                       data[0].vertexCount, Unity.Collections.Allocator.Temp))
            {
                data[0].GetVertices(positions);
                return positions.ToArray();
            }
#else
            return mesh.vertices;
#endif
        }

        private static void CaptureRockingFrame(string name, bool profile, Transform room)
        {
            Camera camera = Camera.main;
            Assert.That(camera, Is.Not.Null);
            var target = new RenderTexture(1280, 720, 24, RenderTextureFormat.ARGB32);
            var pixels = new Texture2D(1280, 720, TextureFormat.RGB24, false);
            RenderTexture active = RenderTexture.active, previousTarget = camera.targetTexture;
            Vector3 position = camera.transform.position;
            Quaternion rotation = camera.transform.rotation;
            float aspect = camera.aspect, fov = camera.fieldOfView;
            try
            {
                if (profile)
                {
                    camera.transform.position = room.TransformPoint(new Vector3(-2.2f, 1.08f, 1.1f));
                    camera.transform.LookAt(room.TransformPoint(new Vector3(0f, 0.77f, 1.55f)), room.up);
                    camera.fieldOfView = 50f;
                }
                camera.aspect = 16f / 9f;
                camera.targetTexture = target;
                camera.Render();
                RenderTexture.active = target;
                pixels.ReadPixels(new Rect(0, 0, 1280, 720), 0, 0);
                pixels.Apply();
                string path = Path.GetFullPath(Path.Combine(Application.dataPath,
                    "../Captures/MothersHouseInterior", name + ".png"));
                Directory.CreateDirectory(Path.GetDirectoryName(path));
                File.WriteAllBytes(path, pixels.EncodeToPNG());
            }
            finally
            {
                camera.transform.SetPositionAndRotation(position, rotation);
                camera.fieldOfView = fov;
                camera.aspect = aspect;
                camera.targetTexture = previousTarget;
                RenderTexture.active = active;
                target.Release();
                UnityEngine.Object.DestroyImmediate(target);
                UnityEngine.Object.DestroyImmediate(pixels);
            }
        }

        [UnityTest]
        public IEnumerator SheAddsNoCollisionNoAudioAndNoPrompt()
        {
            MothersHouseInteriorRoot interior = null;
            yield return LoadRoom(found => interior = found);

            GameObject instance = interior.Mother.gameObject;
            Assert.That(
                instance.GetComponentsInChildren<Collider>(true),
                Is.Empty,
                "The chair's own blocker already stands there.");
            Assert.That(
                instance.GetComponentsInChildren<AudioSource>(true),
                Is.Empty,
                "The room holds exactly three, and she is silent by canon.");
            Assert.That(
                instance.GetComponentsInChildren<Light>(true),
                Is.Empty);
            Assert.That(
                instance.GetComponentsInChildren<IInteractable>(true),
                Is.Empty,
                "The hero's reaction to his mother is not written.");

            // Her expression is set once and nothing ever changes it. The
            // atlas ships complete and undriven, exactly as the stairwell
            // cat's grin ships with no scheduler.
            Assert.That(
                interior.Mother.Expression,
                Is.EqualTo(PlayerFacialExpression.Neutral));

            LogAssert.NoUnexpectedReceived();
            yield return null;
        }

        private static IEnumerator LoadRoom(
            Action<MothersHouseInteriorRoot> capture)
        {
            MothersHouseInteriorRoot interior = null;
            yield return LoadSceneAndWaitForRoot<MothersHouseInteriorRoot>(
                SceneIds.MothersHouseInterior,
                InteriorRootName,
                found => interior = found);
            yield return WaitUntil(
                () => interior.IsInitialized &&
                      !SceneTransitionService.IsTransitioning,
                "The mother's house never finished initializing.");
            capture(interior);
        }

        private static IEnumerator LoadSceneAndWaitForRoot<T>(
            string sceneName,
            string exactRootName,
            Action<T> capture)
            where T : Component
        {
            Assert.That(
                Application.CanStreamedLevelBeLoaded(sceneName),
                Is.True,
                $"Scene '{sceneName}' must be enabled in Build Settings.");
            AsyncOperation operation = SceneManager.LoadSceneAsync(
                sceneName,
                LoadSceneMode.Single);
            Assert.That(operation, Is.Not.Null);
            yield return WaitUntil(
                () => operation.isDone,
                $"Scene '{sceneName}' did not load.");

            T found = null;
            yield return WaitUntil(
                () =>
                {
                    Scene scene = SceneManager.GetActiveScene();
                    found = FindExactRoot<T>(scene, exactRootName);
                    return scene.name == sceneName && found != null;
                },
                $"Scene '{sceneName}' did not create root " +
                $"'{exactRootName}'.");
            capture(found);
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

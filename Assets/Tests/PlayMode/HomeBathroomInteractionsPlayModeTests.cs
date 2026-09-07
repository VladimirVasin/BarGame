using System.Collections;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace BarPromenade.Tests.PlayMode
{
    public sealed class HomeBathroomInteractionsPlayModeTests
    {
        private const float TimeoutSeconds = 30f;
        private const float FastTimeScale = 6f;

        private HomeInteriorRoot home;
        private readonly List<string> showerContactFailures = new List<string>();

        [UnitySetUp]
        public IEnumerator SetUp()
        {
            showerContactFailures.Clear();
            Time.timeScale = 1f;
            GameSessionState.BeginNewGame();
            GameSessionState.EnterHome();
            yield return null;
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            if (ShowerContactFailureDetails.Length > 0)
                TestContext.WriteLine(ShowerContactFailureDetails);
            Time.timeScale = 1f;
            Scene cleanupScene = SceneManager.CreateScene(
                "BathroomInteractionCleanup" +
                UnityEngine.Random.Range(0, 100000));
            SceneManager.SetActiveScene(cleanupScene);
            AsyncOperation unload =
                SceneManager.UnloadSceneAsync(SceneIds.HomeInterior);
            while (unload != null && !unload.isDone)
            {
                yield return null;
            }

            home = null;
            GameSessionState.BeginNewGame();
            yield return null;
        }

        [UnityTest]
        public IEnumerator Toilet_FirstPersonStreamStainsAndRestores()
        {
            AssertToiletSpringResponse();
            yield return LoadHome();
            GameSessionState.UpdateNeeds(0, 40);
            CursorLockMode previousCursor = Cursor.lockState;
            bool previousCursorVisible = Cursor.visible;
            yield return WalkToAndActivate(home.ToiletScene, new Vector3(3.10f, 0.12f, 1.20f));
            Assert.That(home.ToiletScene.Lid.IsOpen, Is.True, "The lid starts opening as soon as E is accepted.");

            Time.timeScale = 2f;
            yield return WaitUntil(() => home.ToiletScene.Timeline.Phase == HomeToiletScenePhase.Urinating,
                "The toilet never reached its first-person emission.");
            Time.timeScale = 1f;
            yield return null; // presentation owns the next rendered endpoint
            Assert.That(home.Player.Motor.InputEnabled, Is.False);
            Assert.That(home.ToiletScene.FirstPerson.IsActive, Is.True);
            Assert.That(home.ToiletScene.FirstPerson.HiddenHeadRendererCount, Is.GreaterThan(0));
            yield return AtPresentation(() =>
            {
                CaptureToilet("00-grip");
                Assert.That(home.ToiletScene.FirstPerson.GripError, Is.LessThan(0.025f));
                AssertToiletBodyContact();
                AssertScrotumVisible();
            });
            Assert.That(home.ToiletScene.GaugeVisible, Is.True);
            Assert.That(home.ToiletScene.Lid.Angle, Is.GreaterThan(85f));
            yield return AtPresentation(() =>
            {
                Bounds tank = home.Room.Find("Home Bathroom Toilet Cistern").GetComponent<Renderer>().bounds;
                Transform paper = home.Room.Find("Home Bathroom Toilet Paper");
                Assert.That(paper, Is.Not.Null);
                Bounds paperBounds = paper.GetComponentInChildren<Renderer>().bounds;
                Assert.That(paperBounds.min.y, Is.EqualTo(tank.max.y).Within(0.003f),
                    "The paper roll must rest on the cistern.");
                foreach (Renderer leaf in home.ToiletScene.Lid.GetComponentsInChildren<Renderer>())
                    Assert.That(leaf.bounds.max.x, Is.LessThan(tank.min.x - 0.002f),
                        "The open lid must remain in front of the cistern.");
            });
            yield return WaitUntil(() => home.ToiletScene.Urine.BowlHitCount > 0 ||
                home.ToiletScene.Timeline.Phase != HomeToiletScenePhase.Urinating,
                "Default aiming produced no liquid contact.");
            Assert.That(home.ToiletScene.Urine.BowlHitCount, Is.GreaterThan(0),
                "Default aim must reach the real bowl water, not its gameplay box. Last hit: " +
                home.ToiletScene.Urine.LastHitSurfaceId + " at " + home.ToiletScene.Urine.LastHitPoint +
                "; outlet " + home.ToiletScene.FirstPerson.OutletPosition + "; aim " +
                home.ToiletScene.FirstPerson.OutletDirection);
            yield return AtPresentation(() => CaptureToilet("01-bowl"));
            HomeToiletFirstPersonView firstPerson = home.ToiletScene.FirstPerson;
            Assert.That(firstPerson.LeftScrotum, Is.Not.Null);
            Assert.That(firstPerson.RightScrotum, Is.Not.Null);
            Quaternion leftRest = firstPerson.LeftScrotum.rotation;
            Quaternion rightRest = firstPerson.RightScrotum.rotation;
            float aimBeforeLook = firstPerson.AimYawDegrees;
            firstPerson.ApplyAimDelta(new Vector2(18f, 8f), true);
            yield return AtPresentation(() =>
            {
                CaptureToilet("07-camera-sway");
                Assert.That(firstPerson.AimYawDegrees, Is.EqualTo(aimBeforeLook),
                    "Independent camera motion must excite inertia without changing the player's aim input.");
                Assert.That(firstPerson.Dynamics.ShaftDegrees.magnitude, Is.GreaterThan(0.01f));
                Assert.That(Quaternion.Angle(leftRest, firstPerson.LeftScrotum.rotation), Is.GreaterThan(0.01f));
                Assert.That(Quaternion.Angle(rightRest, firstPerson.RightScrotum.rotation), Is.GreaterThan(0.01f));
                AssertToiletBodyContact();
                Assert.That(firstPerson.GripError, Is.LessThan(0.025f));
                Quaternion body = home.Player.GameObject.transform.rotation;
                Assert.That(Vector3.Distance(firstPerson.LeftScrotum.position,
                    firstPerson.AnatomyRoot.position + body * HomeToiletFirstPersonView.LeftScrotumAttachment),
                    Is.LessThan(0.001f));
                Assert.That(Vector3.Distance(firstPerson.RightScrotum.position,
                    firstPerson.AnatomyRoot.position + body * HomeToiletFirstPersonView.RightScrotumAttachment),
                    Is.LessThan(0.001f));
            });
            Vector3 originalDirection = home.ToiletScene.FirstPerson.OutletDirection;
            home.ToiletScene.FirstPerson.ApplyAimDelta(110f, 0f);
            int solidBefore = home.ToiletScene.Urine.SurfaceHitCount;
            yield return WaitUntil(() => home.ToiletScene.Urine.SurfaceHitCount > solidBefore ||
                home.ToiletScene.Timeline.Phase != HomeToiletScenePhase.Urinating,
                "Aiming away produced no solid-surface contact.");
            yield return null;
            Assert.That(Vector3.Angle(originalDirection, home.ToiletScene.FirstPerson.OutletDirection),
                Is.GreaterThan(50f), "Aiming is not constrained to the toilet.");
            Assert.That(home.ToiletScene.Urine.SurfaceHitCount, Is.GreaterThan(solidBefore));
            Assert.That(home.ToiletScene.Urine.ResidueCount, Is.GreaterThan(0));
            // The stains are CPU-rebuilt meshes: they must ride the pipeline's
            // GPU Resident Drawer opt-out, or the drawer submits invalid mesh IDs.
            Assert.That(HomeUrineEffect.GpuDrivenOptOutAvailable, Is.True, "DisallowGPUDrivenRendering was not found by name.");
            int optedOut = 0;
            // Stains re-parent onto the surfaces they mark, so look under the room.
            foreach (MeshRenderer stainRenderer in home.Room.GetComponentsInChildren<MeshRenderer>(true))
                if (stainRenderer.name.StartsWith("Urine Stain") &&
                    stainRenderer.GetComponent("DisallowGPUDrivenRendering") != null) optedOut++;
            Assert.That(optedOut, Is.GreaterThan(0), "Every stain visual carries the opt-out component.");
            yield return AtPresentation(() => CaptureToilet("02-miss"));

            // Real aim input at both pitch limits and after body turns must
            // keep the authored base in contact with the rendered clothing.
            float restingPitch = home.ToiletScene.FirstPerson.AimPitchDegrees;
            Vector2[] aimingSamples =
            {
                new Vector2(110f, HomeToiletFirstPersonView.MinimumAimPitchDegrees),
                new Vector2(110f, HomeToiletFirstPersonView.MaximumAimPitchDegrees),
                new Vector2(170f, restingPitch),
                new Vector2(50f, restingPitch),
                new Vector2(110f, restingPitch)
            };
            for (int index = 0; index < aimingSamples.Length; index++)
            {
                HomeToiletFirstPersonView view = home.ToiletScene.FirstPerson;
                view.ApplyAimDelta(aimingSamples[index].x - view.AimYawDegrees,
                    aimingSamples[index].y - view.AimPitchDegrees);
                string shot = "05-contact-" + index;
                yield return AtPresentation(() =>
                {
                    CaptureToilet(shot);
                    AssertToiletBodyContact();
                    Assert.That(view.GripError, Is.LessThan(0.025f));
                });
            }

            Time.timeScale = 2f;
            yield return WaitUntil(() => home.ToiletScene.Timeline.TotalUrinatingSeconds >= 5.4f,
                "The stream never reached its final 20 percent.");
            Time.timeScale = 1f;
            yield return AtPresentation(() =>
            {
                CaptureToilet("06-flow-fade");
                Assert.That(home.ToiletScene.Timeline.Phase, Is.EqualTo(HomeToiletScenePhase.Urinating));
                Assert.That(home.ToiletScene.Urine.LastEmissionFlow, Is.InRange(0.01f, 0.65f));
                Assert.That(home.ToiletScene.Urine.LastEmissionSpeed, Is.LessThan(HomeUrineEffect.StreamSpeed * 0.85f));
                Assert.That(home.ToiletScene.Urine.LastEmissionDiameter, Is.LessThan(0.0025f));
                Assert.That(home.ToiletScene.Urine.LastEmissionRate, Is.LessThan(HomeUrineEffect.PacketsPerSecond * 0.65f));
            });
            Time.timeScale = 2f;
            yield return WaitUntil(() => home.ToiletScene.Timeline.Phase == HomeToiletScenePhase.Shaking,
                "The six-second stream never entered the two-second shake.");
            Time.timeScale = 1f;
            yield return null;
            Assert.That(home.ToiletScene.Timeline.TotalUrinatingSeconds, Is.EqualTo(6f));
            Assert.That(home.ToiletScene.Timeline.RemainingAmount, Is.Zero);
            Assert.That(home.ToiletScene.GaugeVisible, Is.True);
            yield return AtPresentation(() => CaptureToilet("03-shake"));
            yield return WaitUntil(() => home.ToiletScene.Timeline.Phase == HomeToiletScenePhase.Exiting,
                "The two-second shake never reached the camera return.");
            Assert.That(home.ToiletScene.Timeline.TotalShakingSeconds, Is.EqualTo(2f));
            yield return WaitUntil(() => home.Player.Motor.InputEnabled,
                "The toilet never restored the player.");
            yield return null;
            Assert.That(GameSessionState.StressLevel, Is.EqualTo(40 - HomeToiletInteraction.StressRelief));
            Assert.That(home.CameraFollow.FixedBaseFieldOfView, Is.EqualTo(92f).Within(0.01f));
            Assert.That(home.ToiletScene.FirstPerson.IsActive, Is.False);
            Assert.That(home.ToiletScene.FirstPerson.Dynamics.MotionMagnitude, Is.Zero);
            Assert.That(home.Player.Visual.InteractionHandoffLocked, Is.False);
            Assert.That(Cursor.lockState, Is.EqualTo(previousCursor));
            Assert.That(Cursor.visible, Is.EqualTo(previousCursorVisible));
            int residueCount = home.ToiletScene.Urine.ResidueCount;
            yield return AtPresentation(() => CaptureToilet("04-restored"));

            // Rebuild the actual Home scene in the same session: marks remain.
            yield return LoadHome();
            Assert.That(home.ToiletScene.Urine.ResidueCount, Is.EqualTo(residueCount));
            GameSessionState.UpdateNeeds(0, 40);
            yield return WalkToAndActivate(home.ToiletScene, new Vector3(3.10f, 0.12f, 1.20f));
            Time.timeScale = 2f;
            yield return WaitUntil(() => home.ToiletScene.Timeline.Phase == HomeToiletScenePhase.Urinating,
                "A repeated visit could not start.");
            Time.timeScale = 1f;
            yield return null;
            home.ToiletScene.enabled = false;
            yield return null;
            Assert.That(GameSessionState.StressLevel, Is.EqualTo(40), "Disable must not commit relief.");
            Assert.That(home.Player.Motor.InputEnabled, Is.True);
            Assert.That(home.ToiletScene.FirstPerson.IsActive, Is.False);
            Assert.That(home.Player.Visual.InteractionHandoffLocked, Is.False);
            Assert.That(home.CameraFollow.FixedBaseFieldOfView, Is.EqualTo(92f).Within(0.01f));
            Assert.That(home.ToiletScene.Urine.ResidueCount, Is.GreaterThanOrEqualTo(residueCount));
        }

        private static void AssertToiletSpringResponse()
        {
            var dynamics = new HomeToiletAnatomyDynamics();
            dynamics.Reset(Quaternion.identity);
            for (int frame = 0; frame < 60; frame++)
                dynamics.Advance(1f / 60f, Quaternion.identity, Quaternion.identity, 0f);
            Assert.That(dynamics.MotionMagnitude, Is.Zero, "A stationary camera must not invent an idle oscillation.");
            Quaternion moved = Quaternion.Euler(16f, 24f, 0f);
            dynamics.Advance(1f / 60f, moved, Quaternion.identity, 0f);
            float firstMotion = dynamics.MotionMagnitude;
            Assert.That(firstMotion, Is.GreaterThan(0.1f));
            dynamics.Advance(1f / 60f, moved, Quaternion.identity, 0f);
            Assert.That(dynamics.MotionMagnitude, Is.GreaterThan(0.1f),
                "Stopping the camera leaves momentum rather than snapping back.");
            for (int frame = 0; frame < 180; frame++)
            {
                dynamics.Advance(1f / 60f, moved, Quaternion.identity, 0f);
                Assert.That(dynamics.ShaftDegrees.magnitude, Is.LessThanOrEqualTo(HomeToiletAnatomyDynamics.ShaftLimitDegrees + 0.001f));
                Assert.That(dynamics.LeftDegrees.magnitude, Is.LessThanOrEqualTo(HomeToiletAnatomyDynamics.ScrotumLimitDegrees + 0.001f));
                Assert.That(dynamics.RightDegrees.magnitude, Is.LessThanOrEqualTo(HomeToiletAnatomyDynamics.ScrotumLimitDegrees + 0.001f));
            }
            Assert.That(dynamics.MotionMagnitude, Is.LessThan(firstMotion * 0.05f), "Damping must settle the motion.");
            dynamics.Reset(moved);
            Assert.That(dynamics.MotionMagnitude, Is.Zero);
        }

        private void AssertScrotumVisible()
        {
            HomeToiletFirstPersonView view = home.ToiletScene.FirstPerson;
            Camera camera = home.CameraFollow.GetComponent<Camera>();
            var body = new System.Collections.Generic.List<(Vector3[] vertices, int[] triangles)>();
            var baked = new Mesh();
            try
            {
                foreach (Player3DMeshBinding binding in view.Registry.MeshBindings)
                {
                    if (!(binding?.Renderer is SkinnedMeshRenderer renderer) || !renderer.enabled ||
                        renderer.forceRenderingOff || !renderer.gameObject.activeInHierarchy) continue;
                    baked.Clear(false);
                    renderer.BakeMesh(baked, true);
                    Vector3[] vertices = baked.vertices;
                    for (int index = 0; index < vertices.Length; index++)
                        vertices[index] = renderer.transform.TransformPoint(vertices[index]);
                    body.Add((vertices, baked.triangles));
                }
            }
            finally { Object.DestroyImmediate(baked); }
            foreach (Transform lobe in new[] { view.LeftScrotum, view.RightScrotum })
            {
                float visibleArea = 0f;
                foreach (MeshFilter filter in lobe.GetComponentsInChildren<MeshFilter>())
                {
                    Vector3[] vertices = filter.sharedMesh.vertices;
                    int[] triangles = filter.sharedMesh.triangles;
                    for (int triangle = 0; triangle < triangles.Length; triangle += 3)
                    {
                        Vector3 a = filter.transform.TransformPoint(vertices[triangles[triangle]]);
                        Vector3 b = filter.transform.TransformPoint(vertices[triangles[triangle + 1]]);
                        Vector3 c = filter.transform.TransformPoint(vertices[triangles[triangle + 2]]);
                        Vector3 center = (a + b + c) / 3f;
                        if (Vector3.Dot(Vector3.Cross(b - a, c - a), camera.transform.position - center) <= 0f) continue;
                        Vector3 screen = camera.WorldToViewportPoint(center);
                        if (screen.z <= 0f || screen.x < 0f || screen.x > 1f || screen.y < 0f || screen.y > 1f) continue;
                        Vector3 toward = center - camera.transform.position;
                        var ray = new Ray(camera.transform.position, toward.normalized);
                        bool blocked = false;
                        foreach (var mesh in body)
                        {
                            for (int face = 0; face < mesh.triangles.Length; face += 3)
                            {
                                if (RayTriangleDistance(ray, mesh.vertices[mesh.triangles[face]],
                                    mesh.vertices[mesh.triangles[face + 1]], mesh.vertices[mesh.triangles[face + 2]],
                                    out float distance) && distance < toward.magnitude - 0.003f)
                                { blocked = true; break; }
                            }
                            if (blocked) break;
                        }
                        if (blocked) continue;
                        Vector3 pa = camera.WorldToViewportPoint(a);
                        Vector3 pb = camera.WorldToViewportPoint(b);
                        Vector3 pc = camera.WorldToViewportPoint(c);
                        visibleArea += Mathf.Abs((pb.x - pa.x) * (pc.y - pa.y) -
                            (pb.y - pa.y) * (pc.x - pa.x)) * (1280f * 720f * 0.5f);
                    }
                }
                Assert.That(visibleArea, Is.GreaterThan(30f),
                    lobe.name + " must show a visible volume outside the jacket and holding hand.");
            }
        }

        private void AssertToiletBodyContact()
        {
            HomeToiletFirstPersonView view = home.ToiletScene.FirstPerson;
            Vector3 attachment = view.AnatomyRoot.position;
            Vector3 forward = home.Player.GameObject.transform.forward;
            // Independently intersect the actual rendered torso geometry.
            // The old +55 mm clearance leaves this entire segment in air.
            Vector3 start = attachment - forward * 0.025f;
            var ray = new Ray(start, forward);
            float nearestContact = float.PositiveInfinity;
            var baked = new Mesh();
            try
            {
                foreach (Player3DMeshBinding binding in view.Registry.MeshBindings)
                {
                    if (binding == null ||
                        (binding.MeshName != "CLO_JacketBody" &&
                         binding.MeshName != "GEO_Torso" &&
                         binding.MeshName != "GEO_Pelvis") ||
                        !(binding.Renderer is SkinnedMeshRenderer renderer)) continue;
                    baked.Clear(false);
                    renderer.BakeMesh(baked, true);
                    Vector3[] vertices = baked.vertices;
                    for (int vertex = 0; vertex < vertices.Length; vertex++)
                        vertices[vertex] = renderer.transform.TransformPoint(vertices[vertex]);
                    int[] triangles = baked.triangles;
                    for (int triangle = 0; triangle < triangles.Length; triangle += 3)
                    {
                        if (RayTriangleDistance(ray, vertices[triangles[triangle]],
                            vertices[triangles[triangle + 1]], vertices[triangles[triangle + 2]],
                            out float distance))
                            nearestContact = Mathf.Min(nearestContact, Mathf.Abs(distance - 0.025f));
                    }
                }
            }
            finally { Object.DestroyImmediate(baked); }
            Assert.That(nearestContact, Is.LessThan(0.025f),
                "The anatomy base must meet the actual body surface at yaw " + view.AimYawDegrees +
                ", pitch " + view.AimPitchDegrees + "; attachment " + attachment);
        }

        private static bool RayTriangleDistance(Ray ray, Vector3 a, Vector3 b, Vector3 c,
            out float distance)
        {
            distance = 0f;
            Vector3 edge1 = b - a;
            Vector3 edge2 = c - a;
            Vector3 p = Vector3.Cross(ray.direction, edge2);
            float determinant = Vector3.Dot(edge1, p);
            if (Mathf.Abs(determinant) < 0.0000001f) return false;
            Vector3 offset = ray.origin - a;
            float u = Vector3.Dot(offset, p) / determinant;
            if (u < 0f || u > 1f) return false;
            Vector3 q = Vector3.Cross(offset, edge1);
            float v = Vector3.Dot(ray.direction, q) / determinant;
            if (v < 0f || u + v > 1f) return false;
            distance = Vector3.Dot(edge2, q) / determinant;
            return distance >= 0f;
        }

        private IEnumerator AtPresentation(System.Action sample)
        {
            // Test coroutines resume before LateUpdate; the actual rig and
            // camera must be sampled only after their presentation owners.
            var probe = home.GetComponent<HomeBathroomPresentationProbe>() ??
                home.gameObject.AddComponent<HomeBathroomPresentationProbe>();
            bool completed = false;
            System.Exception failure = null;
            probe.Sample = () =>
            {
                try { sample(); }
                catch (System.Exception exception)
                {
                    failure = exception is AssertionException && ShowerContactFailureDetails.Length > 0
                        ? new AssertionException(exception.Message + ShowerContactFailureDetails, exception)
                        : exception;
                }
                finally { completed = true; }
            };
            while (!completed) yield return null;
            if (failure != null) System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(failure).Throw();
        }

        private void CaptureToilet(string shot, string interaction = "HomeToilet")
        {
            CaptureFrame(interaction, shot);
        }

        private void CaptureShower(string shot)
        {
            CaptureFrame("HomeShower", shot);
        }

        private IEnumerator CaptureShowerUiFrame(string area, string shot)
        {
            string path = System.IO.Path.Combine(System.IO.Directory.GetCurrentDirectory(), "Captures", area, shot + ".png");
            System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(path));
            System.DateTime previousWrite = System.IO.File.Exists(path)
                ? System.IO.File.GetLastWriteTimeUtc(path) : System.DateTime.MinValue;
            ScreenCapture.CaptureScreenshot(path);
            yield return null;
            yield return null;
            float deadline = Time.realtimeSinceStartup + 2f;
            while ((!System.IO.File.Exists(path) || System.IO.File.GetLastWriteTimeUtc(path) <= previousWrite) &&
                Time.realtimeSinceStartup < deadline) yield return null;
            Assert.That(System.IO.File.Exists(path) && System.IO.File.GetLastWriteTimeUtc(path) > previousWrite,
                Is.True, "The real Game view, including its UI, must produce a fresh screenshot: " + path);
        }

        /// <summary>A frame from a throwaway lens, for looking at what the hero's own eyes cannot.</summary>
        private void CaptureWitness(string shot, Vector3 position, Vector3 lookAt, float fieldOfView, string area = "HomeShower")
        {
            Camera main = home.CameraFollow.GetComponent<Camera>();
            var witness = new GameObject("Shower Witness Camera");
            Camera camera = witness.AddComponent<Camera>();
            try
            {
                camera.CopyFrom(main);
                camera.transform.SetPositionAndRotation(
                    home.Room.TransformPoint(position),
                    Quaternion.LookRotation(home.Room.TransformPoint(lookAt) - home.Room.TransformPoint(position), Vector3.up));
                camera.fieldOfView = fieldOfView;
                camera.enabled = false;
                CaptureFrame(area, shot, camera);
            }
            finally
            {
                Object.DestroyImmediate(witness);
            }
        }

        private void CaptureFrame(string area, string shot, Camera lens = null)
        {
            Camera camera = lens != null ? lens : home.CameraFollow.GetComponent<Camera>();
            string folder = System.IO.Path.Combine(System.IO.Directory.GetCurrentDirectory(), "Captures", area);
            System.IO.Directory.CreateDirectory(folder);
            var target = new RenderTexture(1280, 720, 24);
            var frame = new Texture2D(1280, 720, TextureFormat.RGB24, false);
            RenderTexture previousTarget = camera.targetTexture;
            RenderTexture previousActive = RenderTexture.active;
            try
            {
                camera.targetTexture = target;
                camera.Render();
                RenderTexture.active = target;
                frame.ReadPixels(new Rect(0f, 0f, 1280, 720), 0, 0);
                frame.Apply();
                System.IO.File.WriteAllBytes(System.IO.Path.Combine(folder, shot + ".png"), frame.EncodeToPNG());
            }
            finally
            {
                camera.targetTexture = previousTarget;
                RenderTexture.active = previousActive;
                Object.DestroyImmediate(frame);
                target.Release();
                Object.DestroyImmediate(target);
            }
        }

        [UnityTest]
        public IEnumerator Shower_FirstPersonNakedWashDripsAndRestores()
        {
            yield return LoadHome();
            GameSessionState.UpdateNeeds(0, 50);
            GameSessionState.SetHeroMouthSoiled(true, "test");
            HomeShowerInteraction shower = home.ShowerScene;
            var presentation = home.Player.Visual as Player3DCharacterPresentation;
            Assert.That(presentation, Is.Not.Null, "The shower needs the production hero.");
            Player3DAssetRegistry registry = presentation.Registry;
            Dictionary<string, RendererSnapshot> before = SnapshotRig(registry);
            Transform curtain = home.Room.Find("Home Bathroom Shower Curtain");
            Assert.That(curtain, Is.Not.Null);
            Transform soap = home.Room.Find("Home Bathroom Shower Soap");
            Assert.That(soap, Is.Not.Null);
            Transform soapParent = soap.parent;
            Vector3 soapPosition = soap.localPosition, soapScale = soap.localScale;
            Quaternion soapRotation = soap.localRotation;
            Assert.That(curtain.localScale.x, Is.EqualTo(HomeShowerInteraction.ClosedCurtainScale).Within(0.001f));
            Bounds previousSideFold = default;
            for (int fold = 1; fold <= 5; fold++)
            {
                Transform side = home.Room.Find("Home Bathroom Shower Curtain Side " + fold);
                Assert.That(side, Is.Not.Null, "Five overlapping folds must reach the tap wall.");
                Bounds bounds = side.GetComponentInChildren<Renderer>().bounds;
                if (fold == 1)
                    Assert.That(home.Room.InverseTransformPoint(bounds.min).z, Is.LessThan(2.384f), "The side curtain overlaps the front curtain.");
                else
                    Assert.That(previousSideFold.max.z - bounds.min.z, Is.GreaterThan(0.01f), "Adjacent authored folds leave no visible slit.");
                if (fold == 5)
                    Assert.That(home.Room.InverseTransformPoint(bounds.max).z, Is.GreaterThan(HomeShowerFraming.WallZ), "The final fold overlaps the actual tile wall.");
                previousSideFold = bounds;
            }
            Camera camera = home.CameraFollow.GetComponent<Camera>();
            Transform hero = home.Player.GameObject.transform;
            foreach (string partName in new[] { "Mixer Body", "Riser", "Head", "Head Face" })
            {
                Transform part = home.Room.Find("Home Bathroom Shower " + partName);
                Assert.That(part, Is.Not.Null, partName);
                Assert.That(part.localPosition.x, Is.EqualTo(HomeShowerFraming.Dock.x).Within(0.005f),
                    partName + " belongs directly ahead of the hero, not beside his right shoulder.");
                Assert.That(part.localPosition.z, Is.GreaterThan(HomeShowerFraming.Dock.z + 0.10f), partName);
            }
            Transform hotHandle = home.Room.Find(HomeShowerInteraction.HotHandleName);
            MeshFilter hotWheel = hotHandle.GetComponentInChildren<MeshFilter>();
            Transform coldHandle = home.Room.Find("Home Bathroom Shower Mixer Handle Cold");
            MeshFilter coldWheel = coldHandle.GetComponentInChildren<MeshFilter>();
            Vector3 authoredValveGrip = HomeBrushingResources.Anchor("FaucetHandle", "HandGrip");
            Assert.That(Vector3.Distance(hotHandle.TransformPoint(HomeBrushingResources.Anchor("FaucetHandle", "HandGrip")),
                home.Room.TransformPoint(HomeShowerFraming.HotHandlePivot +
                    Quaternion.Euler(0f, HomeShowerInteraction.ValveTurnDegrees, 0f) * authoredValveGrip)), Is.LessThan(0.001f),
                "The shower starts with the imported sink wheel at its actual closed grip anchor.");
            Assert.That(shower.HotHandleTurn, Is.EqualTo(1f));
            Assert.That(shower.ColdHandleTurn, Is.EqualTo(1f));
            Assert.That(shower.WaterEffect.IsEmitting, Is.False);
            Transform nozzle = home.Room.Find("Home Bathroom Shower Head Face");
            Assert.That(Vector3.Distance(nozzle.localPosition, HomeShowerFraming.HeadFaceCenter), Is.LessThan(0.001f));
            Assert.That(Vector3.Distance(shower.WaterEffect.StreamParticles.transform.position,
                nozzle.position - nozzle.up * 0.014f), Is.LessThan(0.001f), "The live emitter starts just outside the actual tilted nozzle face.");
            Assert.That(Vector3.Dot(shower.WaterEffect.StreamParticles.transform.forward, -nozzle.up), Is.GreaterThan(0.999f));
            Assert.That(Vector3.Dot(shower.WaterEffect.StreamParticles.transform.forward,
                home.Room.TransformDirection(HomeShowerFraming.StreamDirection)), Is.GreaterThan(0.999f));
            ParticleSystem dripParticles = home.Room.Find("Shower Drip").GetComponent<ParticleSystem>();
            ParticleSystem.MainModule dripSettings = dripParticles.main;
            float dripFlight = dripSettings.startLifetime.constant;
            Assert.That(dripFlight, Is.EqualTo(HomeShowerDripModel.FallSeconds).Within(0.0001f));
            Vector3 actualDripLanding = dripParticles.transform.position +
                dripParticles.transform.forward * (dripSettings.startSpeed.constant * dripFlight) +
                Physics.gravity * (0.5f * dripSettings.gravityModifier.constant * dripFlight * dripFlight);
            Assert.That(Vector3.Distance(actualDripLanding, home.Room.TransformPoint(HomeShowerFraming.BasinLanding)),
                Is.LessThan(0.001f), "The real residual-drop emitter's speed, direction, gravity and lifetime must reach the basin landing.");
            Assert.That(home.Layout.TryGetFurniture(HomeFurnitureKind.Shower, out HomeFurnitureFootprint showerFootprint), Is.True);
            Assert.That(showerFootprint.Bounds.Contains(new Vector2(HomeShowerFraming.BasinLanding.x,
                HomeShowerFraming.BasinLanding.z)), Is.True, "The relocated nozzle still drips into the tray.");
            Texture2D atlas = Player3DBathingAppearance.BareSkinAtlas;
            var contactedGestures = new HashSet<HomeShowerScenePhase>();
            float maximumCurtainContactError = 0f;
            float maximumSoapGripGapMetres = 0f;
            Transform soapHand = Find(registry, "GEO_Hand.R").Bone;
            bool rightSoapGripCaptured = false;
            Vector3 rightSoapLocalPosition = default;
            Quaternion rightSoapLocalRotation = default;
            float maximumRightSoapPositionError = 0f, maximumRightSoapRotationError = 0f;
            double creditedWashSeconds = 0d;
            float previouslyObservedCleanedDistance = 0f;
            Vector3 firstOpeningGrip = default;
            float openingHandTravel = 0f;
            bool openingGripSeen = false;
            using var bodyContact = new ShowerBodyContactCheck(registry);
            string firstBodyIntersection = null;
            int bodyIntersectionFrames = 0;
            var measuredArmPhases = new HashSet<HomeShowerScenePhase>();
            var measuredSoapPhases = new HashSet<HomeShowerSoapPhase>();
            var measuredGestureBeats = new Dictionary<HomeShowerScenePhase, int>();
            bool cameraStartedDuringApproach = false, cameraArrivedBeforeHero = false;
            bool hasPreviousEntryCamera = false;
            Vector3 previousEntryCamera = default;
            int curtainCameraCrossings = 0;
            float maximumWaitingEyeError = 0f;
            Vector3 entryEye = default, previousExitCamera = default, exitedRoot = default;
            Quaternion entryEyeRotation = default;
            bool entryEyeCaptured = false, hasPreviousExitCamera = false, exitBodyMeasured = false;
            int returnCameraCrossings = 0, returnCameraFrames = 0, returnedCameraFrame = -1;
            float maximumReturnPathError = 0f, maximumReturnRotationError = 0f, maximumReturnFovError = 0f;
            float maximumParkedEyeError = 0f, exitedBodyMaximumZ = float.NegativeInfinity;
            string exitedBodyPart = null;
            var exitPhaseOrder = new List<HomeShowerScenePhase>();
            bool previouslyUndressed = false, detachedCameraCaptured = false, redressedOffscreen = false;
            int previousPresentationFrame = -1, nakedOffscreenFrame = -1, nakedOffscreenMeshCount = 0;
            Vector3 previousFixedCamera = home.CameraFollow.FixedBasePosition, detachStartPosition = default;
            Quaternion previousFixedRotation = home.CameraFollow.FixedBaseRotation, detachStartRotation = default;
            int detachedCameraFrames = 0;
            float maximumDetachPositionError = 0f, maximumDetachRotationError = 0f;
            string[] valveActions = { "open-hot", "open-cold", "close-cold", "close-hot" };
            string[] valveShots = { "33-open-front-tap", "34-open-cold-tap", "35-close-cold-tap", "36-close-hot-tap" };
            int[] valveContactFrames = new int[4], valveEndpointMasks = new int[4];
            float[] maximumValvePalmErrors = new float[4], maximumValveGaps = new float[4], maximumValveTargetErrors = new float[4];
            bool[] valveCaptured = new bool[4];
            string[] valveDiagnostics = new string[4];
            var valveTurnOrder = new List<string>();
            var audibleValveActions = new HashSet<string>();
            float previousHotTurn = 1f, previousColdTurn = 1f;
            int valveCuesBeforeShower = home.Soundscape.ValveTurnPlayCount;
            int dripLandingsBeforeShower = home.Soundscape.ShowerDripLandingCount;
            bool landedDripsAudible = false;
            var invariants = home.gameObject.AddComponent<HomeShowerInvariantProbe>();
            invariants.Check = () =>
            {
                HomeShowerScenePhase phase = shower.Timeline.Phase;
                if (phase == HomeShowerScenePhase.Straighten && !detachedCameraCaptured)
                {
                    detachedCameraCaptured = true;
                    // Detachment keeps the last real washing camera, while
                    // the hero remains undressed until he leaves its frame.
                    detachStartPosition = previousFixedCamera;
                    detachStartRotation = previousFixedRotation;
                }
                if (previouslyUndressed && !shower.IsUndressed)
                {
                    redressedOffscreen = phase == HomeShowerScenePhase.CameraOut && nakedOffscreenFrame >= 0 &&
                        nakedOffscreenFrame == previousPresentationFrame && nakedOffscreenMeshCount > 0 && exitBodyMeasured &&
                        shower.Timeline.ExitAppearanceReady;
                    if (!redressedOffscreen)
                        return $"clothes changed without a preceding rendered naked frame fully offscreen: phase={phase}, lastPresentation={previousPresentationFrame}, offscreenFrame={nakedOffscreenFrame}, meshes={nakedOffscreenMeshCount}";
                    Debug.Log($"Shower offscreen redress: nakedFrame={nakedOffscreenFrame}; dressedFrame={Time.frameCount}; actualMeshes={nakedOffscreenMeshCount}");
                }
                previouslyUndressed = shower.IsUndressed;
                previousPresentationFrame = Time.frameCount;
                previousFixedCamera = home.CameraFollow.FixedBasePosition;
                previousFixedRotation = home.CameraFollow.FixedBaseRotation;
                if (shower.IsUndressed && shower.Timeline.CameraBlend < 0.999f)
                    return "undressed while the camera was still travelling (blend " + shower.Timeline.CameraBlend + ")";
                if (shower.IsUndressed && phase <= HomeShowerScenePhase.WaterOff && !shower.View.IsHeadHidden)
                    return "undressed with the lens outside his head";
                if (phase >= HomeShowerScenePhase.Straighten && phase <= HomeShowerScenePhase.StepOut && !shower.IsUndressed)
                    return "clothes were restored while the hero was still leaving the stall";
                if (shower.Timeline.Phase == HomeShowerScenePhase.Wash &&
                    shower.Timeline.PhaseElapsed > HomeShowerSceneTimeline.PoseRaiseSeconds + 0.5f && !shower.IsUndressed)
                    return "washing with his clothes on";
                if (phase == HomeShowerScenePhase.Straighten || phase == HomeShowerScenePhase.DripHold)
                {
                    if (!detachedCameraCaptured || !entryEyeCaptured)
                        return "the detached camera is missing its last wash frame or rendered entry endpoint";
                    float amount = HomeShowerCameraPath.Ease(shower.Timeline.ExitViewBlend);
                    Vector3 expectedPosition = Vector3.Lerp(detachStartPosition, entryEye, amount);
                    Quaternion expectedRotation = Quaternion.Slerp(detachStartRotation, entryEyeRotation, amount);
                    detachedCameraFrames++;
                    maximumDetachPositionError = Mathf.Max(maximumDetachPositionError,
                        Vector3.Distance(camera.transform.position, expectedPosition));
                    maximumDetachRotationError = Mathf.Max(maximumDetachRotationError,
                        Quaternion.Angle(camera.transform.rotation, expectedRotation));
                    if (maximumDetachPositionError > 0.002f || maximumDetachRotationError > 0.1f)
                        return $"detached camera did not interpolate from the last wash frame to the entry eye: phase={phase}, blend={amount:F5}, positionError={maximumDetachPositionError:F5}, rotationError={maximumDetachRotationError:F5}";
                }
                landedDripsAudible |= home.Soundscape.ShowerDripLandingCount > dripLandingsBeforeShower &&
                    home.Soundscape.ShowerDripSource.isPlaying && home.Soundscape.ShowerDripSource.volume > 0f;
                if (phase < HomeShowerScenePhase.WaterOn ||
                    (phase == HomeShowerScenePhase.WaterOn &&
                     shower.Timeline.PhaseElapsed <= HomeShowerSceneTimeline.WaterOnReachEndSeconds))
                {
                    if (shower.Timeline.WaterAmount > 0.0001f || home.Soundscape.ShowerWaterAmount > 0.0001f ||
                        shower.WaterEffect.IsEmitting || shower.WaterEffect.StreamParticleCount > 0 ||
                        shower.WaterEffect.TrayWaterAmount > 0.0001f || Mathf.Abs(shower.HotHandleTurn - 1f) > 0.0001f ||
                        Mathf.Abs(shower.ColdHandleTurn - 1f) > 0.0001f)
                        return "water or valve moved before the opening hand had reached and rendered its grip: " + phase;
                    if (home.Soundscape.ValveTurnPlayCount != valveCuesBeforeShower && phase != HomeShowerScenePhase.Idle)
                        return "the opening valve sounded before its actual turn";
                }
                if (phase == HomeShowerScenePhase.WaterOn || phase == HomeShowerScenePhase.WaterOff)
                {
                    bool opening = phase == HomeShowerScenePhase.WaterOn;
                    bool cold = shower.Timeline.WorkingValveIsCold;
                    int action = opening ? (cold ? 1 : 0) : (cold ? 2 : 3);
                    bool hotMoved = Mathf.Abs(shower.HotHandleTurn - previousHotTurn) > 0.00001f;
                    bool coldMoved = Mathf.Abs(shower.ColdHandleTurn - previousColdTurn) > 0.00001f;
                    if (hotMoved && coldMoved) return "both valves moved together instead of one hand finishing before the other";
                    if (hotMoved || coldMoved)
                    {
                        if (coldMoved != cold) return "the moving valve did not match its active hand";
                        if (valveTurnOrder.Count == 0 || valveTurnOrder[valveTurnOrder.Count - 1] != valveActions[action])
                            valveTurnOrder.Add(valveActions[action]);
                        if (home.Soundscape.ValveTurnPlayCount != valveCuesBeforeShower + valveTurnOrder.Count ||
                            home.Soundscape.LastValveOpening != opening)
                            return "each actual valve turn must play exactly one matching cue: " + valveActions[action];
                        if (home.Soundscape.BathroomValveSource.isPlaying && home.Soundscape.BathroomValveSource.volume > 0f)
                            audibleValveActions.Add(valveActions[action]);
                    }
                    if (shower.Timeline.ValveReach > 0.001f && shower.Timeline.ColdValveReach > 0.001f)
                        return "both valve hands reached together instead of sequentially";
                    if (Mathf.Abs(shower.Timeline.WaterAmount - (2f - shower.HotHandleTurn - shower.ColdHandleTurn) * 0.5f) > 0.001f)
                        return "water did not follow both actual valve angles";
                    float reach = cold ? shower.Timeline.ColdValveReach : shower.Timeline.ValveReach;
                    float turn = cold ? shower.ColdHandleTurn : shower.HotHandleTurn;
                    if (reach >= 0.999f)
                    {
                        valveContactFrames[action]++;
                        maximumValvePalmErrors[action] = Mathf.Max(maximumValvePalmErrors[action],
                            cold ? shower.WashPose.LeftPalmError : shower.WashPose.RightPalmError);
                        maximumValveGaps[action] = Mathf.Max(maximumValveGaps[action],
                            MeasureHandToValveDistance(registry, cold ? coldWheel : hotWheel, cold));
                        Vector3 actualGrip = (cold ? coldHandle : hotHandle).TransformPoint(authoredValveGrip);
                        Vector3 target = cold ? shower.WashPose.LeftPalmTarget : shower.WashPose.RightPalmTarget;
                        maximumValveTargetErrors[action] = Mathf.Max(maximumValveTargetErrors[action], Vector3.Distance(actualGrip, target));
                        if (turn >= 0.999f) valveEndpointMasks[action] |= 1;
                        if (turn <= 0.001f) valveEndpointMasks[action] |= 2;
                        valveDiagnostics[action] = $"action={valveActions[action]}; elapsed={shower.Timeline.PhaseElapsed:F4}; turn={turn:F4}; " +
                            $"frames={valveContactFrames[action]}; palmError={maximumValvePalmErrors[action]:F5}; " +
                            $"meshGap={maximumValveGaps[action]:F5}; targetError={maximumValveTargetErrors[action]:F5}; " +
                            $"actualGrip={actualGrip:F5}; target={target:F5}; bodyViolation={firstBodyIntersection ?? "none"}";
                        if (!valveCaptured[action] && turn > 0.2f && turn < 0.8f)
                        {
                            valveCaptured[action] = true;
                            CaptureShower(valveShots[action]);
                        }
                    }
                }
                previousHotTurn = shower.HotHandleTurn;
                previousColdTurn = shower.ColdHandleTurn;
                float cleanedDistance = shower.WashingProgress.CleanedDistance;
                if (cleanedDistance > previouslyObservedCleanedDistance)
                {
                    if (!shower.SoapPose.IsContacting || shower.SoapPose.ContactTravelMetres <= 0f)
                        return "automatic washing earned progress without actual moving skin contact";
                    creditedWashSeconds += Time.deltaTime;
                }
                previouslyObservedCleanedDistance = cleanedDistance;
                if (phase >= HomeShowerScenePhase.Approach && phase <= HomeShowerScenePhase.CameraIn)
                {
                    Vector3 localCamera = home.Room.InverseTransformPoint(camera.transform.position);
                    if (phase == HomeShowerScenePhase.Approach && shower.Timeline.CameraBlend > 0f)
                        cameraStartedDuringApproach = true;
                    float curtainPlane = curtain.localPosition.z;
                    if (hasPreviousEntryCamera && previousEntryCamera.z < curtainPlane && localCamera.z >= curtainPlane)
                    {
                        curtainCameraCrossings++;
                        Vector3 crossing = Vector3.Lerp(previousEntryCamera, localCamera,
                            (curtainPlane - previousEntryCamera.z) / (localCamera.z - previousEntryCamera.z));
                        float rightEdge = float.NegativeInfinity, bottom = float.PositiveInfinity, top = float.NegativeInfinity;
                        foreach (Renderer fold in curtain.GetComponentsInChildren<Renderer>())
                        {
                            rightEdge = Mathf.Max(rightEdge, home.Room.InverseTransformPoint(fold.bounds.max).x);
                            bottom = Mathf.Min(bottom, home.Room.InverseTransformPoint(fold.bounds.min).y);
                            top = Mathf.Max(top, home.Room.InverseTransformPoint(fold.bounds.max).y);
                        }
                        if (crossing.x <= rightEdge + 0.025f || crossing.x >= showerFootprint.Bounds.xMax - 0.025f ||
                            crossing.y <= bottom + 0.1f || crossing.y >= top - 0.1f)
                            return $"camera crossed outside the real open curtain gap: point={crossing:F4}, hemX={rightEdge:F4}, height={bottom:F3}..{top:F3}";
                    }
                    previousEntryCamera = localCamera;
                    hasPreviousEntryCamera = true;
                    if (shower.CameraArrived && !shower.Timeline.DockReached)
                    {
                        cameraArrivedBeforeHero = true;
                        if (!entryEyeCaptured)
                        {
                            entryEyeCaptured = true;
                            entryEye = shower.TargetEyeWorld;
                            entryEyeRotation = camera.transform.rotation;
                        }
                        maximumWaitingEyeError = Mathf.Max(maximumWaitingEyeError, Vector3.Distance(camera.transform.position, shower.TargetEyeWorld));
                        if (!Player3DHeadVisibility.IsHeadDrawn(registry) || shower.IsUndressed)
                            return "the camera waiting at the future eyes hid or undressed the hero before he arrived";
                    }
                }
                if (phase >= HomeShowerScenePhase.DripHold && phase <= HomeShowerScenePhase.CloseExitCurtain &&
                    (exitPhaseOrder.Count == 0 || exitPhaseOrder[exitPhaseOrder.Count - 1] != phase))
                    exitPhaseOrder.Add(phase);
                if (phase >= HomeShowerScenePhase.ApproachExit && phase <= HomeShowerScenePhase.CloseExitCurtain)
                {
                    if (!entryEyeCaptured) return "the exit has no rendered entry eye endpoint to reverse";
                    Vector3 localCamera = home.Room.InverseTransformPoint(camera.transform.position);
                    if (phase < HomeShowerScenePhase.CameraOut)
                    {
                        maximumParkedEyeError = Mathf.Max(maximumParkedEyeError,
                            Vector3.Distance(camera.transform.position, entryEye));
                        if (shower.Timeline.CameraBlend < 0.9999f || maximumParkedEyeError > 0.002f ||
                            Quaternion.Angle(camera.transform.rotation, entryEyeRotation) > 0.1f)
                            return $"camera followed the exiting hero before he cleared the curtain: phase={phase}, eyeError={maximumParkedEyeError:F5}";
                    }
                    else if (phase == HomeShowerScenePhase.CameraOut)
                    {
                        returnCameraFrames++;
                        if (!exitBodyMeasured)
                        {
                            exitBodyMeasured = true;
                            exitedRoot = hero.position;
                            exitedBodyMaximumZ = bodyContact.MeasureVisibleMaximumLocalZ(home.Room, out exitedBodyPart);
                            CaptureShower("40-hero-out-before-camera-return");
                            if (float.IsNegativeInfinity(exitedBodyMaximumZ))
                                return "no visible production geometry was available to prove the completed step out";
                            if (exitedBodyMaximumZ >= curtain.localPosition.z)
                                return $"camera left before the whole visible hero cleared the curtain: part={exitedBodyPart}, maxZ={exitedBodyMaximumZ:F5}, plane={curtain.localPosition.z:F5}, root={home.Room.InverseTransformPoint(hero.position):F4}";
                        }
                        if (Vector3.Distance(hero.position, exitedRoot) > 0.005f)
                            return "the hero moved from his outside dock during the camera return";
                        if (Quaternion.Angle(hero.rotation, home.Room.rotation * HomeShowerFraming.ExitCameraFacing) > 1f)
                            return "the departing hero turned his toes back toward the curtain before the camera returned";
                        if (Mathf.Abs(curtain.localScale.x - HomeShowerInteraction.GatheredCurtainScale) > 0.001f)
                            return "the curtain began closing before the camera flew through its opening";
                        float amount = shower.Timeline.CameraBlend;
                        if (shower.IsUndressed)
                        {
                            if (shower.Timeline.ExitAppearanceReady || shower.Timeline.PhaseElapsed > 0f || amount < 0.9999f)
                                return "the camera flew back before the offscreen clothing handoff";
                            string visiblePart = bodyContact.FindVisibleGeometryInCamera(hero, camera, out int meshes);
                            if (visiblePart == null)
                            {
                                if (meshes == 0) return "the offscreen handoff must measure real enabled body geometry";
                                if (nakedOffscreenFrame < 0) CaptureShower("42-naked-hero-fully-out-of-frame");
                                nakedOffscreenFrame = Time.frameCount;
                                nakedOffscreenMeshCount = meshes;
                            }
                            else nakedOffscreenFrame = -1;
                        }
                        else if (!redressedOffscreen)
                            return "the camera return did not observe the offscreen clothing change";
                        Vector3 expectedPosition = HomeShowerCameraPath.Evaluate(shower.EntryCameraStartWorld,
                            home.Room.TransformPoint(HomeShowerCameraPath.BeforeCurtain),
                            home.Room.TransformPoint(HomeShowerCameraPath.AfterCurtain), entryEye, amount);
                        Quaternion expectedRotation = amount <= HomeShowerCameraPath.CurtainApproachEnd
                            ? Quaternion.Slerp(shower.EntryCameraStartRotation, home.Room.rotation,
                                HomeShowerCameraPath.Ease(amount / HomeShowerCameraPath.CurtainApproachEnd))
                            : Quaternion.Slerp(home.Room.rotation, entryEyeRotation,
                                HomeShowerCameraPath.Ease((amount - HomeShowerCameraPath.CurtainApproachEnd) /
                                    (1f - HomeShowerCameraPath.CurtainApproachEnd)));
                        maximumReturnPathError = Mathf.Max(maximumReturnPathError, Vector3.Distance(camera.transform.position, expectedPosition));
                        maximumReturnRotationError = Mathf.Max(maximumReturnRotationError, Quaternion.Angle(camera.transform.rotation, expectedRotation));
                        maximumReturnFovError = Mathf.Max(maximumReturnFovError, Mathf.Abs(camera.fieldOfView -
                            Mathf.Lerp(shower.EntryCameraStartFieldOfView, HomeShowerFirstPersonView.FieldOfView, amount)));
                        if (maximumReturnPathError > 0.002f || maximumReturnRotationError > 0.1f || maximumReturnFovError > 0.01f)
                            return $"return diverged from the actual entry path: t={shower.Timeline.PhaseElapsed:F4}, blend={amount:F5}, positionError={maximumReturnPathError:F5}, rotationError={maximumReturnRotationError:F4}, fovError={maximumReturnFovError:F4}";
                        float plane = curtain.localPosition.z;
                        if (hasPreviousExitCamera && previousExitCamera.z >= plane && localCamera.z < plane)
                        {
                            returnCameraCrossings++;
                            Vector3 crossing = Vector3.Lerp(previousExitCamera, localCamera,
                                (plane - previousExitCamera.z) / (localCamera.z - previousExitCamera.z));
                            float edge = float.NegativeInfinity, bottom = float.PositiveInfinity, top = float.NegativeInfinity;
                            foreach (Renderer fold in curtain.GetComponentsInChildren<Renderer>())
                            {
                                edge = Mathf.Max(edge, home.Room.InverseTransformPoint(fold.bounds.max).x);
                                bottom = Mathf.Min(bottom, home.Room.InverseTransformPoint(fold.bounds.min).y);
                                top = Mathf.Max(top, home.Room.InverseTransformPoint(fold.bounds.max).y);
                            }
                            if (crossing.x <= edge + 0.025f || crossing.x >= showerFootprint.Bounds.xMax - 0.025f ||
                                crossing.y <= bottom + 0.1f || crossing.y >= top - 0.1f)
                                return $"return camera missed the real curtain opening: point={crossing:F4}, edge={edge:F4}, height={bottom:F4}..{top:F4}";
                        }
                        if (amount <= 0.0001f && shower.Timeline.CameraReturned)
                        {
                            if (returnedCameraFrame < 0) CaptureShower("41-camera-back-at-entry-origin");
                            returnedCameraFrame = Time.frameCount;
                        }
                    }
                    else
                    {
                        if (!shower.Timeline.CameraReturned || returnedCameraFrame < 0 || Time.frameCount <= returnedCameraFrame)
                            return "the curtain closed before a complete camera return frame was presented";
                        if (Vector3.Distance(camera.transform.position, shower.EntryCameraStartWorld) > 0.002f ||
                            Quaternion.Angle(camera.transform.rotation, shower.EntryCameraStartRotation) > 0.1f ||
                            Mathf.Abs(camera.fieldOfView - shower.EntryCameraStartFieldOfView) > 0.01f)
                            return "the restored camera moved while the hero approached or closed the curtain";
                        if (phase == HomeShowerScenePhase.ApproachCloseCurtain &&
                            Mathf.Abs(curtain.localScale.x - HomeShowerInteraction.GatheredCurtainScale) > 0.001f)
                            return "the curtain closed before the hero returned to its authored gesture dock";
                    }
                    previousExitCamera = localCamera;
                    hasPreviousExitCamera = true;
                }
                if (shower.SoapHeld)
                {
                    maximumSoapGripGapMetres = Mathf.Max(maximumSoapGripGapMetres, Mathf.Abs(shower.SoapPose.SoapGripGapMetres));
                    Vector3 localPosition = soapHand.InverseTransformPoint(soap.position);
                    Quaternion localRotation = Quaternion.Inverse(soapHand.rotation) * soap.rotation;
                    if (!rightSoapGripCaptured)
                    {
                        rightSoapGripCaptured = true;
                        rightSoapLocalPosition = localPosition;
                        rightSoapLocalRotation = localRotation;
                    }
                    maximumRightSoapPositionError = Mathf.Max(maximumRightSoapPositionError,
                        Vector3.Distance(localPosition, rightSoapLocalPosition));
                    maximumRightSoapRotationError = Mathf.Max(maximumRightSoapRotationError,
                        Quaternion.Angle(localRotation, rightSoapLocalRotation));
                }
                if (shower.Timeline.IsCurtainGesture ||
                    (phase >= HomeShowerScenePhase.WaterOn && phase <= HomeShowerScenePhase.Straighten))
                {
                    measuredArmPhases.Add(phase);
                    if (phase == HomeShowerScenePhase.Wash) measuredSoapPhases.Add(shower.SoapPhase);
                    if (shower.Timeline.IsCurtainGesture)
                    {
                        float time = shower.Timeline.GestureNormalized;
                        int beat = time < HomeShowerCurtainPose.ReachEnd ? 1 :
                            time <= HomeShowerCurtainPose.ReleaseStart ? 2 : 4;
                        measuredGestureBeats.TryGetValue(phase, out int measured);
                        measuredGestureBeats[phase] = measured | beat;
                    }
                    string intersection = bodyContact.Measure();
                    if (intersection != null)
                    {
                        bodyIntersectionFrames++;
                        if (firstBodyIntersection == null)
                        {
                            firstBodyIntersection = phase + " t=" + shower.Timeline.PhaseElapsed.ToString("F4") +
                                " gesture=" + shower.Timeline.GestureNormalized.ToString("F4") + ": " + intersection;
                            Debug.Log("Shower arm contact: " + firstBodyIntersection);
                        }
                    }
                }
                if (shower.Timeline.IsCurtainGesture && shower.CurtainPose.IsInContact)
                {
                    contactedGestures.Add(phase);
                    maximumCurtainContactError = Mathf.Max(maximumCurtainContactError, shower.CurtainPose.ContactError);
                    if (phase == HomeShowerScenePhase.OpenCurtain)
                    {
                        if (!openingGripSeen)
                        {
                            firstOpeningGrip = shower.CurtainPose.ActiveGrip.position;
                            openingGripSeen = true;
                        }
                        openingHandTravel = Mathf.Max(openingHandTravel,
                            Vector3.Distance(firstOpeningGrip, shower.CurtainPose.ActiveGrip.position));
                    }
                }
                if ((phase == HomeShowerScenePhase.StepIn || phase == HomeShowerScenePhase.StepOut) &&
                    Mathf.Abs(curtain.localScale.x - HomeShowerInteraction.GatheredCurtainScale) > 0.001f)
                    return "hero crossed a curtain that was not fully gathered";
                if (phase == HomeShowerScenePhase.CloseExitCurtain &&
                    shower.Timeline.CameraBlend > 0f)
                    return "the outside closing gesture started before the camera returned to the room";
                return null;
            };
            float restFaceDown = FaceDown(registry);

            yield return WalkToAndActivate(shower, new Vector3(3.30f, 0.12f, 2.35f));
            yield return null;
            yield return null;
            Assert.That(shower.Timeline.Phase, Is.EqualTo(HomeShowerScenePhase.Approach));
            Assert.That(shower.Timeline.CameraBlend, Is.GreaterThan(0f), "The camera begins its approach as soon as E starts the scene.");
            Assert.That(shower.View.IsActive, Is.True);
            Assert.That(home.Player.Motor.InputEnabled, Is.False);
            Assert.That(shower.IsUndressed, Is.False);

            yield return WaitUntil(() => shower.Timeline.Phase == HomeShowerScenePhase.OpenCurtain &&
                shower.Timeline.GestureNormalized >= 0.45f, "No visible curtain opening before the camera move.");
            yield return AtPresentation(() =>
            {
                Assert.That(shower.Timeline.CameraBlend, Is.GreaterThan(0f));
                Assert.That(shower.CurtainPose.IsActive, Is.True);
                Assert.That(shower.CurtainPose.IsInContact, Is.True);
                Assert.That(curtain.localScale.x, Is.GreaterThan(HomeShowerInteraction.GatheredCurtainScale).And.LessThan(1f));
                Assert.That(Player3DHeadVisibility.IsHeadDrawn(registry), Is.True);
                CaptureShower("07-open-entry-curtain");
            });
            yield return WaitUntil(() => shower.Timeline.Phase == HomeShowerScenePhase.StepIn &&
                shower.Timeline.CameraBlend > 0.05f, "The camera never followed the step into the open stall.");
            yield return AtPresentation(() =>
            {
                Assert.That(invariants.Violation, Is.Null, "A presentation invariant failed before the hero entered.");
                Assert.That(shower.CameraArrived, Is.True, "The camera must finish at the future eyes before the hero enters.");
                Assert.That(cameraArrivedBeforeHero, Is.True,
                    $"No observed camera arrival ahead of the hero: phase={shower.Timeline.Phase}; " +
                    $"dockReached={shower.Timeline.DockReached}; cameraArrived={shower.CameraArrived}; " +
                    $"eyeError={Vector3.Distance(camera.transform.position, shower.TargetEyeWorld):F4}; " +
                    $"firstViolation={invariants.Violation ?? "none"}");
                CaptureShower("08-enter-stall");
            });
            yield return WaitUntil(() => shower.Timeline.Phase == HomeShowerScenePhase.CloseCurtain &&
                shower.Timeline.GestureNormalized >= 0.45f, "He never drew the curtain shut from inside.");
            yield return AtPresentation(() =>
            {
                Assert.That(shower.Timeline.CameraBlend, Is.EqualTo(1f), "The lens waits inside while the hero closes the curtain behind him.");
                Assert.That(shower.CurtainPose.IsInContact, Is.True);
                CaptureShower("09-close-entry-curtain");
            });
            Time.timeScale = 1f;
            yield return WaitUntil(() => shower.IsUndressed, "The hero never undressed.");
            yield return AtPresentation(() =>
            {
                Assert.That(shower.Timeline.CameraBlend, Is.EqualTo(1f));
                Assert.That(shower.View.IsHeadHidden, Is.True, "The lens is inside his head, so the head is off.");
                Assert.That(shower.View.HiddenHeadRendererCount, Is.GreaterThan(10));
                Assert.That(Player3DHeadVisibility.IsHeadDrawn(registry), Is.False);
                Assert.That(
                    Vector3.Distance(camera.transform.position, registry.Anchors.Mouth.position + Vector3.up * HomeShowerFirstPersonView.EyeHeightAboveMouth),
                    Is.LessThan(0.03f),
                    "The lens sits at his eyes.");
                Assert.That(Find(registry, "CLO_JacketBody").Renderer.enabled, Is.False);
                Assert.That(Find(registry, "CLO_Bandage.L").Renderer.enabled, Is.True);
                Assert.That(shower.WashPose.BridgesShown, Is.True);
                if (atlas != null)
                {
                    var block = new MaterialPropertyBlock();
                    Find(registry, "GEO_Torso").Renderer.GetPropertyBlock(block);
                    Assert.That(block.GetTexture("_BaseMap"), Is.EqualTo(atlas), "The torso wears the bare-skin atlas.");
                    Find(registry, "GEO_Foot.L").Renderer.GetPropertyBlock(block);
                    Assert.That(block.GetTexture("_BaseMap"), Is.EqualTo(atlas), "The feet wear it too.");
                }

                CaptureShower("00-first-person-in");
            });

            yield return WaitUntil(
                () => shower.Timeline.Phase == HomeShowerScenePhase.Wash && shower.Timeline.PhaseElapsed > 1.2f,
                "The wash never started.");
            yield return AtPresentation(() =>
            {
                Assert.That(invariants.Violation, Is.Null);
                Assert.That(valveTurnOrder, Is.EqualTo(new[] { "open-hot", "open-cold" }));
                for (int action = 0; action < 2; action++)
                {
                    Debug.Log("Shower opening contact: " + valveDiagnostics[action]);
                    Assert.That(valveContactFrames[action], Is.GreaterThanOrEqualTo(2), valveDiagnostics[action]);
                    Assert.That(valveEndpointMasks[action], Is.EqualTo(3), valveDiagnostics[action]);
                    Assert.That(valveCaptured[action], Is.True, valveDiagnostics[action]);
                    Assert.That(maximumValvePalmErrors[action], Is.LessThan(0.04f), valveDiagnostics[action]);
                    Assert.That(maximumValveTargetErrors[action], Is.LessThan(0.003f), valveDiagnostics[action]);
                    Assert.That(maximumValveGaps[action], Is.LessThan(0.015f), valveDiagnostics[action]);
                    Assert.That(audibleValveActions.Contains(valveActions[action]), Is.True, valveDiagnostics[action]);
                }
                Assert.That(shower.HotHandleTurn, Is.Zero.Within(0.001f));
                Assert.That(shower.ColdHandleTurn, Is.Zero.Within(0.001f));
                Assert.That(home.Soundscape.BathroomValveSource.clip, Is.SameAs(home.Audio.GetClip(RetroSfxId.RefrigeratorHinge)));
                CaptureShower("01-wash");
                Vector3 mixerView = camera.WorldToViewportPoint(home.Room.TransformPoint(HomeShowerFraming.Mixer));
                Assert.That(mixerView.z, Is.GreaterThan(0f), "The mixer must be in front of the lens.");
                Assert.That(mixerView.x, Is.InRange(0.35f, 0.65f), "The mixer stays in the centre of the first-person view.");
                Assert.That(mixerView.y, Is.InRange(0.05f, 0.95f), "The mixer is visible while washing.");
                Assert.That(shower.WashPose.LeftPalmError, Is.LessThan(0.04f), "Left palm on the tile.");
                Assert.That(shower.WashPose.RightPalmError, Is.LessThan(0.04f), "The right palm joins the left palm against the tile before taking the soap.");
                Vector3 leftHand = Bone(registry, Player3DAnatomicalPart.LeftHand).position;
                Vector3 rightHand = Bone(registry, Player3DAnatomicalPart.RightHand).position;
                Assert.That(leftHand.z, Is.GreaterThan(3.60f), "The left hand reaches the back tile.");
                Assert.That(Vector3.Distance(shower.WashPose.RightPalmTarget, home.Room.TransformPoint(HomeShowerFraming.RightPalm)), Is.LessThan(0.025f));
                Assert.That(home.Room.InverseTransformPoint(rightHand).z, Is.GreaterThan(HomeShowerFraming.WallZ - 0.06f),
                    "The right hand rests on the wall instead of hanging beside the body.");
                Assert.That(Vector3.Distance(leftHand, rightHand), Is.InRange(0.30f, 0.95f));
                AssertShowerSoapVisiblePastRightArm(camera, soap, registry);
                Assert.That(FaceDown(registry) - restFaceDown, Is.GreaterThan(0.15f), "The head hangs under the water.");
                Assert.That(
                    Vector3.Distance(camera.transform.position, registry.Anchors.Mouth.position + Vector3.up * HomeShowerFirstPersonView.EyeHeightAboveMouth),
                    Is.LessThan(0.08f),
                    "The lens hangs with the head (plus the breathing drift).");
                Assert.That(Vector3.Dot(camera.transform.forward, Vector3.down), Is.GreaterThan(0.4f), "The eyes look down at the tray.");
                Assert.That(Vector3.Dot(camera.transform.forward, hero.forward), Is.GreaterThan(0.3f), "...and forward, at the tile.");
                Assert.That(home.Soundscape.ShowerWaterAmount, Is.EqualTo(1f).Within(0.01f));
                Assert.That(home.Soundscape.ShowerWaterSource.isPlaying, Is.True);
                Assert.That(home.Soundscape.ShowerWaterSource.volume, Is.GreaterThan(0f));
                Assert.That(home.Soundscape.ShowerWaterSource.volume, Is.EqualTo(0.29f).Within(0.001f),
                    "The full shower loop retains the requested increased gain.");
                Assert.That(shower.WaterEffect.IsEmitting, Is.True);
                Assert.That(shower.WaterEffect.StreamParticleCount, Is.GreaterThan(5), "Water is actually falling, not just flagged.");
                AssertShowerStreamReachesBody(shower, camera, registry);
                CaptureShower("38-wall-visible-stream");
                CaptureWitness("39-nozzle-stream", new Vector3(3.36f, 2.20f, 2.66f),
                    new Vector3(3.88f, 1.62f, 3.52f), 64f);
                Assert.That(shower.WaterEffect.TrayWaterAmount, Is.GreaterThan(0.05f), "Running water gathers in the real tray.");
                Assert.That(shower.WaterEffect.TrayWaterRenderer, Is.Not.Null);
                Assert.That(shower.WaterEffect.TrayWaterRenderer.enabled, Is.True);
                Assert.That(shower.WaterEffect.TrayWaterRenderer.sharedMaterial.shader.name,
                    Is.EqualTo("Bar Promenade/Home Shower Tray Water"),
                    "The transparent flowing water must retain its shader after player-occlusion initialization.");
                Assert.That(shower.WaterEffect.TrayWaterRenderer.GetComponent<MeshFilter>().sharedMesh.vertexCount,
                    Is.GreaterThan(8), "The collected water uses the imported surface with its drain opening.");
                Assert.That(shower.WaterEffect.DrainFlowAmount, Is.GreaterThan(0f));
                Assert.That(home.Player.Visual.InteractionHandoffLocked, Is.True);
                Assert.That(home.PlayerOcclusion.enabled, Is.False);
                Assert.That(curtain.localScale.x, Is.EqualTo(HomeShowerInteraction.ClosedCurtainScale).Within(0.001f), "The entrance curtain is fully closed while he washes.");
                Assert.That(home.Room.InverseTransformPoint(hero.position).z, Is.EqualTo(HomeShowerFraming.Dock.z).Within(0.005f), "The feet remain at the farther-back wash dock.");
                Assert.That(registry.Anchors.Mouth.position.z - registry.Anchors.Pelvis.position.z,
                    Is.GreaterThan(0.12f), "The upper body inclines toward the tap above the grounded feet.");
                Assert.That(shower.IsUndressed, Is.True);
                Assert.That(shower.WashPose.HasAnatomy, Is.True, "The toilet's authored anatomy is packaged; the shower borrows it at rest.");
                Assert.That(shower.WashPose.AnatomyRoot.gameObject.activeInHierarchy, Is.True);
                Assert.That(
                    Vector3.Distance(shower.WashPose.AnatomyRoot.position, registry.Anchors.Pelvis.position),
                    Is.LessThan(0.35f),
                    "The resting anatomy hangs from the pelvis.");
                Assert.That(shower.WashPose.AnatomyRoot.position.y, Is.LessThan(registry.Anchors.Pelvis.position.y + 0.05f));
                Assert.That(
                    Vector3.Dot(shower.WashPose.AnatomyRoot.forward, Vector3.down),
                    Is.GreaterThan(0.4f),
                    "At rest it hangs, it does not aim.");
                // And it never hangs so steeply that the scrotum stands in
                // front of it. Each lobe's neck is authored curving forward
                // for the toilet's coat clearance, so past roughly 48
                // degrees the shaft no longer reaches past that mass and
                // the hero looks down at himself and sees only the pair.
                // A pitch check alone cannot say this: the old assertion
                // here passed every angle from 58 to 90 degrees, the whole
                // broken range included.
                Vector3 facing = hero.forward;
                Vector3 anatomyBase = shower.WashPose.AnatomyRoot.position;
                float shaftReach =
                    Vector3.Dot(shower.WashPose.AnatomyRoot.forward, facing) *
                    HomeToiletFirstPersonView.AnatomyShaftLengthMetres;
                float lobeReach = Mathf.Max(
                        Vector3.Dot(shower.WashPose.LeftScrotum.position - anatomyBase, facing),
                        Vector3.Dot(shower.WashPose.RightScrotum.position - anatomyBase, facing)) +
                    HomeToiletFirstPersonView.ScrotumForwardReachMetres;
                Assert.That(
                    shaftReach, Is.GreaterThan(lobeReach + 0.01f),
                    "The shaft has to read in front of the scrotum, never behind it.");
                foreach (Transform lobe in new[] { shower.WashPose.LeftScrotum, shower.WashPose.RightScrotum })
                {
                    float forwardReach = float.NegativeInfinity;
                    int measuredVertices = 0;
                    foreach (MeshFilter filter in lobe.GetComponentsInChildren<MeshFilter>())
                    {
                        foreach (Vector3 vertex in filter.sharedMesh.vertices)
                        {
                            forwardReach = Mathf.Max(forwardReach,
                                Vector3.Dot(filter.transform.TransformPoint(vertex) - lobe.position, facing));
                            measuredVertices++;
                        }
                    }
                    Assert.That(measuredVertices, Is.GreaterThan(0));
                    Assert.That(forwardReach, Is.LessThanOrEqualTo(0.052f),
                        "The actual resting lobe hangs down instead of projecting forward from its fixed attachment.");
                }
                // A witness lens in the stall's corner: the only way to look
                // at the bare body from outside, for the texture work.
                CaptureWitness("04-witness-wash", new Vector3(3.45f, 2.45f, 2.45f), new Vector3(3.95f, 1.20f, 3.30f), 56f);
                CaptureWitness("05-witness-front", new Vector3(4.05f, 1.15f, 3.75f), new Vector3(3.88f, 1.05f, 3.20f), 70f);
            });

            // A glance down at himself: the look cone tilts the lens, never the body.
            Quaternion facingBeforeLook = hero.rotation;
            shower.View.ApplyLookDelta(0f, 35f);
            yield return AtPresentation(() =>
            {
                Assert.That(shower.View.LookPitchDegrees, Is.EqualTo(35f).Within(0.01f));
                Assert.That(Quaternion.Angle(hero.rotation, facingBeforeLook), Is.LessThan(0.5f));
                Assert.That(Vector3.Dot(camera.transform.forward, Vector3.down), Is.GreaterThan(0.8f), "Looking down at the body.");
                CaptureShower("02-look-down");
            });
            AimShowerAt(shower, camera, shower.WaterEffect.DrainPosition);
            yield return AtPresentation(() =>
            {
                Vector3 drainViewport = camera.WorldToViewportPoint(shower.WaterEffect.DrainPosition);
                Assert.That(drainViewport.z > 0f && drainViewport.x > 0.03f && drainViewport.x < 0.97f &&
                    drainViewport.y > 0.03f && drainViewport.y < 0.97f, Is.True,
                    $"The actual drain is visible from the hero's eyes: {drainViewport:F4}");
                Assert.That(shower.WaterEffect.TrayWaterRenderer.enabled, Is.True);
                Assert.That(shower.WaterEffect.DrainFlowAmount, Is.GreaterThan(0f));
                CaptureShower("31-tray-water-drain");
            });
            shower.View.ApplyLookDelta(-shower.View.LookYawDegrees, 35f - shower.View.LookPitchDegrees);
            shower.View.ApplyLookDelta(0f, -35f);
            shower.View.ApplyLookDelta(500f, 0f);
            Assert.That(shower.View.LookYawDegrees, Is.EqualTo(HomeShowerFirstPersonView.MaximumLookYawDegrees).Within(0.01f), "The cone clamps.");
            shower.View.ApplyLookDelta(-500f, 0f);
            shower.View.ApplyLookDelta(HomeShowerFirstPersonView.MaximumLookYawDegrees, 0f);
            Assert.That(shower.View.LookYawDegrees, Is.Zero.Within(0.01f));

            Time.timeScale = 1f;
            yield return LookAtShowerHeadWithArrowKeys(shower, camera, nozzle);

            // Waiting and looking do not wash the body or start the former timed exit.
            Assert.That(shower.Timeline.Phase, Is.EqualTo(HomeShowerScenePhase.Wash));
            Assert.That(shower.WashingProgress.Amount, Is.Zero);
            Assert.That(shower.Timeline.ReachedMinimumWash, Is.False);
            Time.timeScale = 1f;
            yield return PickUpShowerSoap(shower, camera);
            float stillProgress = shower.WashingProgress.Amount;
            yield return new WaitForSeconds(0.4f);
            Assert.That(shower.WashingProgress.Amount, Is.EqualTo(stillProgress), "Taking the soap without selecting skin cannot fill the gauge.");
            Assert.That(shower.HasSelectedWashPoint, Is.False);
            Assert.That(shower.GaugeVisible, Is.True);
            yield return AtPresentation(() => CaptureShower("14-hold-soap"));
            yield return AtPresentation(() => AssertShowerRightArmExcluded(shower, camera, registry));
            Time.timeScale = 1f;
            // Each held click selects a stable skin point and starts its own
            // automatic cycle. These genuine contacts contribute to the gauge.
            foreach (HomeShowerWashRegion region in new[]
            {
                HomeShowerWashRegion.LeftArm,
                HomeShowerWashRegion.LeftLeg, HomeShowerWashRegion.RightLeg, HomeShowerWashRegion.Intimate
            })
                yield return ObserveSelectedShowerPoint(shower, camera, region, false, () => firstBodyIntersection);
            Time.timeScale = 1f;
            yield return ObserveSelectedShowerPoint(shower, camera, HomeShowerWashRegion.Torso, true, () => firstBodyIntersection);
            Assert.That(shower.WashingProgress.Complete, Is.True);
            Assert.That(shower.WashingProgress.Amount, Is.EqualTo(1f), "The selected Torso cycle completes the shared gauge without regional quotas.");
            Assert.That(creditedWashSeconds + 0.01d, Is.GreaterThanOrEqualTo(10d),
                "Full cleaning requires at least ten seconds in real frames that earned contact-based progress.");
            Time.timeScale = 1f;
            Assert.That(shower.SoapPhase, Is.EqualTo(HomeShowerSoapPhase.PutDown), "Full progress automatically starts the visible soap return.");
            Assert.That(shower.Timeline.Phase, Is.EqualTo(HomeShowerScenePhase.Wash), "The water stays on until the soap is back on its shelf.");
            Assert.That(shower.GaugeVisible, Is.False);
            yield return AtPresentation(() => CaptureShower("15-return-soap"));
            yield return WaitUntil(() => shower.Timeline.Phase == HomeShowerScenePhase.WaterOff,
                "Full cleaning never returned the soap and closed the tap.");
            AssertShowerSoapRest(soap, soapParent, soapPosition, soapRotation, soapScale);
            Assert.That(shower.SoapHeld, Is.False);
            Assert.That(shower.Timeline.ReachedMinimumWash, Is.True);
            yield return WaitUntil(() => shower.Timeline.ValveReach >= 0.99f, "The hand never reached the tap.");
            yield return AtPresentation(() =>
            {
                Assert.That(shower.WashPose.RightPalmError, Is.LessThan(0.04f), "The right hand reaches the relocated hot handle.");
                CaptureShower("06-close-front-tap");
            });
            Time.timeScale = 1f;
            yield return WaitUntil(() => shower.Timeline.Phase >= HomeShowerScenePhase.Straighten, "The tap never closed.");
            Assert.That(shower.HotHandleTurn, Is.EqualTo(1f).Within(0.01f));
            Assert.That(home.Soundscape.ShowerWaterAmount, Is.Zero.Within(0.001f));
            float trayAmountWhenClosed = shower.WaterEffect.TrayWaterAmount;

            yield return WaitUntil(() => shower.Timeline.Phase == HomeShowerScenePhase.DripHold, "He never straightened for the drips.");
            Vector3 heldRoot = default;
            yield return AtPresentation(() =>
            {
                heldRoot = hero.position;
                CaptureShower("03-drip");
                Assert.That(invariants.Violation, Is.Null);
                Assert.That(detachedCameraFrames, Is.GreaterThan(0), "The hold blends the final washing view toward the fixed entry eye.");
                Assert.That(shower.IsUndressed, Is.True, "He remains bare throughout the still hold and walk out.");
                Assert.That(redressedOffscreen, Is.False);
                Assert.That(Find(registry, "CLO_JacketBody").Renderer.enabled, Is.False);
                Assert.That(shower.WashPose.BridgesShown, Is.True);
                Assert.That(shower.HoldsHandoff, Is.False);
                Assert.That(home.Player.Motor.InputEnabled, Is.False, "Input stays locked through the hold.");
                Assert.That(home.InteractionPrompt.PromptKey, Is.Empty, "No prompt while he stands for the drips.");
                Assert.That(shower.Drips.HoldActive, Is.True);
            });
            yield return null;
            yield return AtPresentation(() =>
            {
                Assert.That(shower.Timeline.Phase, Is.EqualTo(HomeShowerScenePhase.DripHold));
                // The hero waits while the lens smoothly leaves the last live
                // look and returns to the captured entry eye endpoint.
                Assert.That(Vector3.Distance(hero.position, heldRoot), Is.LessThan(0.001f), "He stands still.");
                Assert.That(invariants.Violation, Is.Null);
                Assert.That(maximumDetachPositionError, Is.LessThan(0.002f));
                Assert.That(maximumDetachRotationError, Is.LessThan(0.1f));
            });
            yield return WaitUntil(
                () => shower.Drips.HoldEmitted >= 2 || shower.Timeline.Phase != HomeShowerScenePhase.DripHold,
                "The tap never dripped.");
            Assert.That(shower.WaterEffect.DropsEmitted, Is.GreaterThanOrEqualTo(2));
            yield return AtPresentation(() =>
            {
                Assert.That(shower.WaterEffect.StreamParticleCount, Is.Zero, "The stream is gone while he stands.");
                Assert.That(shower.WaterEffect.TrayWaterAmount, Is.LessThan(trayAmountWhenClosed),
                    "The collected water drains after the valve closes.");
                CaptureShower("32-tray-draining-after-tap");
            });

            Time.timeScale = 1f;
            yield return WaitUntil(() => shower.Timeline.Phase == HomeShowerScenePhase.OpenExitCurtain &&
                shower.Timeline.GestureNormalized >= 0.45f, "No visible curtain opening on exit.");
            yield return AtPresentation(() =>
            {
                Assert.That(invariants.Violation, Is.Null);
                Assert.That(shower.Timeline.CameraBlend, Is.EqualTo(1f));
                Assert.That(Vector3.Distance(camera.transform.position, entryEye), Is.LessThan(0.002f));
                Assert.That(shower.CurtainPose.IsInContact, Is.True);
                Assert.That(shower.IsUndressed, Is.True, "Clothes wait until he has left the camera frame.");
                CaptureShower("11-open-exit-curtain");
            });
            yield return WaitUntil(() => shower.Timeline.Phase == HomeShowerScenePhase.StepOut &&
                shower.Timeline.PhaseElapsed >= 0.10f, "He never walked through the open curtain.");
            yield return AtPresentation(() => CaptureShower("12-leave-stall"));
            yield return WaitUntil(() => shower.Timeline.Phase == HomeShowerScenePhase.CameraOut &&
                shower.Timeline.PhaseElapsed >= 0.5f, "The camera never followed the hero out through the opening.");
            yield return AtPresentation(() =>
            {
                Assert.That(invariants.Violation, Is.Null);
                Assert.That(exitBodyMeasured, Is.True);
                Assert.That(exitedBodyMaximumZ, Is.LessThan(curtain.localPosition.z), exitedBodyPart);
                Assert.That(Vector3.Distance(hero.position, exitedRoot), Is.LessThan(0.005f));
                CaptureShower("10-camera-return");
            });
            yield return WaitUntil(() => shower.Timeline.Phase == HomeShowerScenePhase.CloseExitCurtain &&
                shower.Timeline.GestureNormalized >= 0.45f, "He never closed the curtain from outside.");
            yield return AtPresentation(() =>
            {
                Assert.That(invariants.Violation, Is.Null);
                Assert.That(shower.Timeline.CameraReturned, Is.True);
                Assert.That(returnedCameraFrame, Is.GreaterThanOrEqualTo(0));
                Assert.That(Vector3.Distance(camera.transform.position, shower.EntryCameraStartWorld), Is.LessThan(0.002f));
                Assert.That(Quaternion.Angle(camera.transform.rotation, shower.EntryCameraStartRotation), Is.LessThan(0.1f));
                Assert.That(camera.fieldOfView, Is.EqualTo(shower.EntryCameraStartFieldOfView).Within(0.01f));
                Assert.That(shower.CurtainPose.IsInContact, Is.True);
                CaptureShower("13-close-exit-curtain");
            });

            yield return WaitUntil(() => home.Player.Motor.InputEnabled, "The shower scene never restored the player.");
            Time.timeScale = 1f;
            Assert.That(invariants.Violation, Is.Null);
            Assert.That(shower.Timeline.Phase, Is.EqualTo(HomeShowerScenePhase.Idle));
            Assert.That(cameraStartedDuringApproach, Is.True);
            Assert.That(cameraArrivedBeforeHero, Is.True);
            Assert.That(maximumWaitingEyeError, Is.LessThan(0.03f), "The camera waits at the future eyes while the hero approaches.");
            Assert.That(curtainCameraCrossings, Is.EqualTo(1), "The incoming lens passes through the front opening exactly once.");
            Assert.That(exitPhaseOrder, Is.EqualTo(new[] { HomeShowerScenePhase.DripHold, HomeShowerScenePhase.ApproachExit,
                HomeShowerScenePhase.OpenExitCurtain, HomeShowerScenePhase.StepOut, HomeShowerScenePhase.CameraOut,
                HomeShowerScenePhase.ApproachCloseCurtain, HomeShowerScenePhase.CloseExitCurtain }));
            Assert.That(returnCameraCrossings, Is.EqualTo(1), "The departing lens uses the same open curtain gap after the hero.");
            Assert.That(returnCameraFrames, Is.GreaterThan(2));
            Assert.That(returnedCameraFrame, Is.GreaterThanOrEqualTo(0));
            Assert.That(redressedOffscreen, Is.True);
            Assert.That(nakedOffscreenFrame, Is.GreaterThanOrEqualTo(0));
            Assert.That(nakedOffscreenMeshCount, Is.GreaterThan(0));
            Assert.That(detachedCameraFrames, Is.GreaterThan(2));
            Assert.That(maximumDetachPositionError, Is.LessThan(0.002f));
            Assert.That(maximumDetachRotationError, Is.LessThan(0.1f));
            Assert.That(maximumParkedEyeError, Is.LessThan(0.002f));
            Assert.That(maximumReturnPathError, Is.LessThan(0.002f));
            Assert.That(maximumReturnRotationError, Is.LessThan(0.1f));
            Assert.That(maximumReturnFovError, Is.LessThan(0.01f));
            Debug.Log($"Shower reverse camera: frames={returnCameraFrames}; crossings={returnCameraCrossings}; " +
                $"pathError={maximumReturnPathError:F6}; rotationError={maximumReturnRotationError:F5}; fovError={maximumReturnFovError:F5}; " +
                $"outsidePart={exitedBodyPart}; outsideMaxZ={exitedBodyMaximumZ:F5}; returnFrame={returnedCameraFrame}");
            AssertRigRestored(before, registry);
            Assert.That(Player3DHeadVisibility.IsHeadDrawn(registry), Is.True, "The head is back once the lens has left.");
            Assert.That(shower.View.IsActive, Is.False);
            Assert.That(home.CameraFollow.FixedBaseFieldOfView, Is.EqualTo(92f).Within(0.01f), "The camera is home.");
            Assert.That(home.PlayerOcclusion.enabled, Is.True);
            Assert.That(home.Soundscape.ShowerWaterAmount, Is.Zero.Within(0.001f));
            Assert.That(shower.WaterEffect.IsDripping, Is.False);
            Assert.That(home.Soundscape.ValveTurnPlayCount, Is.EqualTo(valveCuesBeforeShower + 4));
            Assert.That(valveTurnOrder, Is.EqualTo(valveActions), "Open hot then cold, close cold then hot.");
            Assert.That(audibleValveActions, Is.EquivalentTo(valveActions));
            Assert.That(shower.HotHandleTurn, Is.EqualTo(1f));
            Assert.That(shower.ColdHandleTurn, Is.EqualTo(1f));
            for (int action = 2; action < 4; action++)
            {
                Debug.Log("Shower closing contact: " + valveDiagnostics[action]);
                Assert.That(valveContactFrames[action], Is.GreaterThanOrEqualTo(2), valveDiagnostics[action]);
                Assert.That(valveEndpointMasks[action], Is.EqualTo(3), valveDiagnostics[action]);
                Assert.That(valveCaptured[action], Is.True, valveDiagnostics[action]);
                Assert.That(maximumValvePalmErrors[action], Is.LessThan(0.04f), valveDiagnostics[action]);
                Assert.That(maximumValveTargetErrors[action], Is.LessThan(0.003f), valveDiagnostics[action]);
                Assert.That(maximumValveGaps[action], Is.LessThan(0.015f), valveDiagnostics[action]);
            }
            Assert.That(landedDripsAudible, Is.True, "The actual final drop landings produce sound.");
            Assert.That(home.Soundscape.ShowerDripLandingCount, Is.GreaterThan(dripLandingsBeforeShower));
            Assert.That(home.Soundscape.ShowerWaterSource.volume, Is.Zero.Within(0.001f));
            Assert.That(home.Soundscape.BathroomValveSource.isPlaying, Is.False);
            Assert.That(home.Soundscape.ShowerDripSource.isPlaying, Is.False);
            Assert.That(shower.WaterEffect.TrayWaterAmount, Is.Zero.Within(0.001f));
            Assert.That(shower.WaterEffect.TrayWaterRenderer.enabled, Is.False);
            Assert.That(shower.WaterEffect.DrainFlowAmount, Is.Zero.Within(0.001f));
            Assert.That(shower.WashPose.BridgesShown, Is.False);
            Assert.That(shower.CurtainPose.IsActive, Is.False);
            Assert.That(curtain.localScale.x, Is.EqualTo(HomeShowerInteraction.ClosedCurtainScale).Within(0.001f));
            Assert.That(contactedGestures, Is.EquivalentTo(new[]
            {
                HomeShowerScenePhase.OpenCurtain, HomeShowerScenePhase.CloseCurtain,
                HomeShowerScenePhase.OpenExitCurtain, HomeShowerScenePhase.CloseExitCurtain
            }), "Every entry and exit curtain change must have visible hand contact.");
            Assert.That(maximumCurtainContactError, Is.LessThan(0.04f), "The production grip must follow the actual moving curtain edge.");
            Assert.That(maximumSoapGripGapMetres, Is.LessThan(0.005f),
                "The held soap must stay within five millimetres of the actual right palm through pickup, washing and return.");
            Assert.That(rightSoapGripCaptured, Is.True);
            Assert.That(maximumRightSoapPositionError, Is.LessThan(0.002f), "The held bar remains fixed to the production right hand for the entire wash.");
            Assert.That(maximumRightSoapRotationError, Is.LessThan(0.5f), "The bar never changes hands or slides through the right palm.");
            Assert.That(openingHandTravel, Is.GreaterThan(0.20f), "The first room shot shows the hand drawing the curtain across the rail.");
            Assert.That(measuredSoapPhases, Is.SupersetOf(new[]
            {
                HomeShowerSoapPhase.SelectSoap, HomeShowerSoapPhase.Pickup,
                HomeShowerSoapPhase.Washing, HomeShowerSoapPhase.PutDown
            }), "The real hand/body guard covers soap reach, rubbing and return too.");
            AssertShowerSoapRest(soap, soapParent, soapPosition, soapRotation, soapScale);
            Assert.That(bodyIntersectionFrames, Is.Zero,
                "Both rendered hands and forearms must remain outside the actual visible body through every reach, pull, release and valve transition. " + firstBodyIntersection);
            foreach (HomeShowerScenePhase gesture in contactedGestures)
                Assert.That(measuredGestureBeats[gesture], Is.EqualTo(7), gesture + " must measure reach, pull and release, including the neutral endpoints.");
            foreach (HomeShowerScenePhase phase in new[]
                { HomeShowerScenePhase.WaterOn, HomeShowerScenePhase.Wash, HomeShowerScenePhase.WaterOff, HomeShowerScenePhase.Straighten })
                Assert.That(measuredArmPhases.Contains(phase), Is.True, phase + " must check actual hand/body geometry.");
            Assert.That(HomeShowerFraming.IsInsideStall(home.Room.InverseTransformPoint(hero.position)), Is.False, "He ends outside the curtain.");
            Assert.That(Quaternion.Angle(hero.rotation, home.Room.rotation * HomeShowerCurtainPose.OutsideFacing), Is.LessThan(1f),
                "Control returns at the authored outside closing endpoint, with no extra turn.");
            Assert.That(GameSessionState.StressLevel, Is.EqualTo(50 - HomeShowerInteraction.StressRelief));
            Assert.That(GameSessionState.HeroMouthSoiled, Is.False, "A wash always washes the face.");
            Assert.That(Player3DBathingAppearance.IsActive, Is.False);
            invariants.Check = null;
            Object.Destroy(invariants);
            Assert.That(showerContactFailures, Is.Empty,
                "Every supported soap contact must succeed." + ShowerContactFailureDetails);
        }

        [UnityTest]
        public IEnumerator Shower_CancelMidWashDressesHimAndShutsTheWater()
        {
            yield return LoadHome();
            GameSessionState.UpdateNeeds(0, 50);
            HomeShowerInteraction shower = home.ShowerScene;
            var presentation = home.Player.Visual as Player3DCharacterPresentation;
            Assert.That(presentation, Is.Not.Null);
            Player3DAssetRegistry registry = presentation.Registry;
            Dictionary<string, RendererSnapshot> before = SnapshotRig(registry);
            CursorLockMode cursorBefore = Cursor.lockState;
            Transform soap = home.Room.Find("Home Bathroom Shower Soap");
            Transform soapParent = soap.parent;
            Vector3 soapPosition = soap.localPosition, soapScale = soap.localScale;
            Quaternion soapRotation = soap.localRotation;

            Camera camera = home.CameraFollow.GetComponent<Camera>();
            MeshFilter hotWheel = home.Room.Find(HomeShowerInteraction.HotHandleName).GetComponentInChildren<MeshFilter>();
            AssertShowerHardware(hotWheel);

            yield return WalkToAndActivate(shower, new Vector3(3.30f, 0.12f, 2.35f));
            Time.timeScale = FastTimeScale;
            yield return WaitUntil(
                () => shower.Timeline.Phase == HomeShowerScenePhase.Wash && shower.Timeline.PhaseElapsed > 1f,
                "The wash never started.");
            Assert.That(shower.IsUndressed, Is.True);
            Assert.That(shower.View.IsHeadHidden, Is.True);
            Assert.That(home.Soundscape.ShowerWaterAmount, Is.GreaterThan(0.5f));
            Assert.That(HomeBathroomMirrorWorld.IsSelectable(HomeShowerWaterEffect.TrayWaterName), Is.False,
                "Shower water stays outside the separate mirrored bathroom, as do the existing shower particles.");

            Time.timeScale = 1f;
            yield return AtPresentation(() =>
            {
                CaptureFrame("HomeShowerHardware", "00-front-wash");
                Assert.That(shower.WaterEffect.TrayWaterRenderer.sharedMaterial.shader.name,
                    Is.EqualTo("Bar Promenade/Home Shower Tray Water"),
                    "Player occlusion must not replace the transparent water material with its dither shader.");
                Bounds lamp = home.Room.Find("Home Bathroom Cold Tube").GetComponent<Renderer>().bounds;
                Bounds head = home.Room.Find("Home Bathroom Shower Head").GetComponent<Renderer>().bounds;
                CaptureWitness("01-lamp-above-divider", new Vector3(4.26f, 1.50f, 2.70f),
                    home.Room.InverseTransformPoint((lamp.center + head.center) * 0.5f), 78f, "HomeShowerHardware");
            });
            AimShowerAt(shower, camera, shower.WaterEffect.DrainPosition);
            yield return AtPresentation(() =>
            {
                Vector3 drainViewport = camera.WorldToViewportPoint(shower.WaterEffect.DrainPosition);
                Assert.That(drainViewport.z > 0f && drainViewport.x > 0.03f && drainViewport.x < 0.97f &&
                    drainViewport.y > 0.03f && drainViewport.y < 0.97f, Is.True,
                    $"The real drain remains visible through the collected water: {drainViewport:F4}");
                Assert.That(shower.WaterEffect.TrayWaterRenderer.enabled, Is.True);
                Assert.That(shower.WaterEffect.TrayWaterAmount, Is.GreaterThan(0.05f));
                Assert.That(shower.WaterEffect.DrainFlowAmount, Is.GreaterThan(0f));
                CaptureFrame("HomeShowerHardware", "31-tray-water-drain");
            });
            yield return new WaitForSeconds(0.4f);
            yield return AtPresentation(() => CaptureFrame("HomeShowerHardware", "31-tray-water-flow"));
            yield return LookAtShowerHeadWithArrowKeys(shower, camera, home.Room.Find("Home Bathroom Shower Head Face"),
                "HomeShowerHardware");
            yield return PickUpShowerSoap(shower, camera, "HomeShowerHardware");
            Assert.That(shower.SoapHeld, Is.True, "The cancellation must exercise an actually held soap.");
            yield return RequestShowerExitWithQ(shower, camera, false);
            Assert.That(shower.SoapPhase, Is.EqualTo(HomeShowerSoapPhase.PutDown), "Q returns held soap before either valve closes.");
            Assert.That(shower.Timeline.Phase, Is.EqualTo(HomeShowerScenePhase.Wash));
            shower.enabled = false; // OnDisable → CancelScene: owned idempotent cleanup
            yield return null;
            Time.timeScale = 1f;
            Assert.That(shower.IsUndressed, Is.False, "A cancelled wash never leaves him undressed.");
            AssertRigRestored(before, registry);
            Assert.That(Player3DHeadVisibility.IsHeadDrawn(registry), Is.True, "A cancelled wash never leaves him headless.");
            Assert.That(shower.View.IsActive, Is.False);
            Assert.That(Cursor.lockState, Is.EqualTo(cursorBefore));
            Assert.That(home.Player.Motor.InputEnabled, Is.True);
            Assert.That(home.Soundscape.ShowerWaterAmount, Is.Zero.Within(0.001f));
            Assert.That(shower.WaterEffect.IsEmitting, Is.False);
            Assert.That(shower.WaterEffect.IsDripping, Is.False);
            Assert.That(shower.WaterEffect.TrayWaterAmount, Is.Zero.Within(0.001f));
            Assert.That(shower.WaterEffect.TrayWaterRenderer.enabled, Is.False);
            Assert.That(shower.WaterEffect.DrainFlowAmount, Is.Zero.Within(0.001f));
            Assert.That(home.PlayerOcclusion.enabled, Is.True);
            Assert.That(shower.HoldsHandoff, Is.False);
            Assert.That(home.Player.Visual.InteractionHandoffLocked, Is.False);
            Assert.That(shower.HotHandleTurn, Is.EqualTo(1f), "Cancellation leaves the valve closed.");
            Assert.That(shower.ColdHandleTurn, Is.EqualTo(1f));
            Assert.That(shower.WashPose.BridgesShown, Is.False);
            Assert.That(shower.CurtainPose.IsActive, Is.False);
            Assert.That(shower.SoapHeld, Is.False);
            Assert.That(shower.GaugeVisible, Is.False);
            Assert.That(shower.SoapHighlighted, Is.False);
            Assert.That(shower.WashingProgress.Amount, Is.Zero);
            AssertShowerSoapRest(soap, soapParent, soapPosition, soapRotation, soapScale);
            Assert.That(home.Room.Find("Home Bathroom Shower Curtain").localScale.x,
                Is.EqualTo(HomeShowerInteraction.ClosedCurtainScale).Within(0.001f), "Cancellation restores the entrance curtain too.");
            Assert.That(shower.Timeline.Phase, Is.EqualTo(HomeShowerScenePhase.Idle));
            Assert.That(GameSessionState.StressLevel, Is.EqualTo(50), "A cancel commits nothing.");
            Assert.That(Player3DBathingAppearance.IsActive, Is.False);
            shower.enabled = true;

            // Preserve the held-soap cancellation above, then exercise the
            // short, real early-stop path against the imported valve wheel.
            yield return WalkToAndActivate(shower, HomeShowerFraming.Dock);
            Time.timeScale = FastTimeScale;
            yield return WaitUntil(() => shower.Timeline.Phase == HomeShowerScenePhase.Wash &&
                shower.Timeline.PhaseElapsed > 1.2f, "The second wash never reached its early-stop prompt.");
            Time.timeScale = 1f;
            Assert.That(shower.SoapPhase, Is.EqualTo(HomeShowerSoapPhase.SelectSoap));
            Assert.That(shower.WashingProgress.Amount, Is.Zero);
            yield return RequestShowerExitWithQ(shower, camera, true);
            Assert.That(shower.Timeline.Phase, Is.EqualTo(HomeShowerScenePhase.WaterOff));
            using var bodyContact = new ShowerBodyContactCheck(registry);
            int contactFrames = 0;
            float maximumValveGap = 0f, maximumPalmError = 0f, maximumGripTargetError = 0f;
            string bodyIntersection = null;
            string maximumPalmDiagnostic = "No full-reach presentation sampled.";
            bool closingCaptured = false;
            yield return ObserveShowerPresentationUntil(() =>
            {
                string intersection = bodyContact.Measure();
                if (bodyIntersection == null) bodyIntersection = intersection;
                if (shower.Timeline.ValveReach < 0.999f) return;
                contactFrames++;
                float valveGap = MeasureHandToValveDistance(registry, hotWheel);
                maximumValveGap = Mathf.Max(maximumValveGap, valveGap);
                Vector3 actualGrip = home.Room.Find(HomeShowerInteraction.HotHandleName).TransformPoint(
                    HomeBrushingResources.Anchor("FaucetHandle", "HandGrip"));
                maximumGripTargetError = Mathf.Max(maximumGripTargetError,
                    Vector3.Distance(actualGrip, shower.WashPose.RightPalmTarget));
                if (shower.WashPose.RightPalmError >= maximumPalmError)
                {
                    maximumPalmError = shower.WashPose.RightPalmError;
                    Vector3 shoulder = Find(registry, "GEO_UpperArm.R").Bone.position;
                    Vector3 forearm = Find(registry, "GEO_Forearm.R").Bone.position;
                    Vector3 hand = Find(registry, "GEO_Hand.R").Bone.position;
                    float upperLength = Vector3.Distance(shoulder, forearm), lowerLength = Vector3.Distance(forearm, hand);
                    maximumPalmDiagnostic = $"frame={Time.frameCount}; phaseElapsed={shower.Timeline.PhaseElapsed:F4}; " +
                        $"valveTurn={shower.HotHandleTurn:F4}; valveReach={shower.Timeline.ValveReach:F4}; " +
                        $"actualGrip={actualGrip:F5}; palmTarget={shower.WashPose.RightPalmTarget:F5}; " +
                        $"camera={camera.transform.position:F5}; root={home.Player.GameObject.transform.position:F5}; " +
                        $"shoulder={shoulder:F5}; forearm={forearm:F5}; hand={hand:F5}; " +
                        $"upperLength={upperLength:F5}; lowerLength={lowerLength:F5}; chainLength={upperLength + lowerLength:F5}; " +
                        $"shoulderToGrip={Vector3.Distance(shoulder, actualGrip):F5}; valveGap={valveGap:F5}";
                }
                if (!closingCaptured && shower.HotHandleTurn >= 0.35f)
                {
                    closingCaptured = true;
                    CaptureFrame("HomeShowerHardware", "02-hand-closes-new-wheel");
                }
            }, () => shower.Timeline.Phase != HomeShowerScenePhase.WaterOff, Time.realtimeSinceStartup + TimeoutSeconds);
            string valveDiagnostic = $"contactFrames={contactFrames}; maxPalmError={maximumPalmError:F5}; " +
                $"maxValveGap={maximumValveGap:F5}; maxGripTargetError={maximumGripTargetError:F5}; " +
                $"bodyIntersection={bodyIntersection ?? "none"}; maxPalmFrame={{" + maximumPalmDiagnostic + "}";
            Debug.Log("Shower hardware closing contact: " + valveDiagnostic);
            Assert.That(contactFrames, Is.GreaterThanOrEqualTo(2), "The real valve turn must have rendered contact frames. " + valveDiagnostic);
            Assert.That(closingCaptured, Is.True, valveDiagnostic);
            Assert.That(maximumPalmError, Is.LessThan(0.04f), "The hand must follow the new wheel's rotating grip. " + valveDiagnostic);
            Assert.That(maximumGripTargetError, Is.LessThan(0.003f), "The contact target must rotate with the actual imported handle. " + valveDiagnostic);
            Assert.That(maximumValveGap, Is.LessThan(0.015f), "The actual hand mesh must remain in contact with the imported wheel surface. " + valveDiagnostic);
            Assert.That(bodyIntersection, Is.Null, "The closing arm must not cross the body. " + valveDiagnostic);
            Assert.That(shower.HotHandleTurn, Is.EqualTo(1f).Within(0.01f), valveDiagnostic);
            Assert.That(shower.ColdHandleTurn, Is.EqualTo(1f).Within(0.01f), valveDiagnostic);
            Assert.That(home.Soundscape.ShowerWaterAmount, Is.Zero.Within(0.001f), valveDiagnostic);
            float trayAfterClosing = shower.WaterEffect.TrayWaterAmount;
            yield return WaitUntil(() => shower.Timeline.Phase == HomeShowerScenePhase.DripHold,
                "The early stop never reached the drain-down hold.");
            AimShowerAt(shower, camera, shower.WaterEffect.DrainPosition);
            yield return AtPresentation(() =>
            {
                Assert.That(shower.WaterEffect.TrayWaterRenderer.sharedMaterial.shader.name,
                    Is.EqualTo("Bar Promenade/Home Shower Tray Water"));
                Assert.That(shower.WaterEffect.TrayWaterRenderer.enabled, Is.True,
                    "The remaining water is still visible as it drains after closing the tap.");
                Assert.That(shower.WaterEffect.TrayWaterAmount, Is.GreaterThan(0f).And.LessThan(trayAfterClosing));
                Assert.That(shower.WaterEffect.DrainFlowAmount, Is.GreaterThan(0f));
                CaptureFrame("HomeShowerHardware", "32-tray-draining-after-tap");
            });
            Time.timeScale = FastTimeScale;
            yield return WaitUntil(() => !shower.View.IsActive, "The early stop never returned control.");
            Assert.That(home.Player.Motor.InputEnabled, Is.True);
            Assert.That(shower.IsUndressed, Is.False);
            Assert.That(GameSessionState.StressLevel, Is.EqualTo(50), "Stopping before washing must not commit relief.");
            AssertShowerSoapRest(soap, soapParent, soapPosition, soapRotation, soapScale);
            AssertRigRestored(before, registry);
        }

        private IEnumerator RequestShowerExitWithQ(HomeShowerInteraction shower, Camera camera, bool verifyPrompt)
        {
            if (verifyPrompt)
            {
                var affordance = shower.GetComponent<HomeShowerSoapAffordance>();
                MeshFilter cold = home.Room.Find("Home Bathroom Shower Mixer Handle Cold").GetComponentInChildren<MeshFilter>();
                AimShowerAt(shower, camera, cold.GetComponent<Renderer>().bounds.center);
                int earliestFrame = Time.frameCount + 1;
                float deadline = Time.realtimeSinceStartup + 2f;
                while (shower.ExitPromptRenderedFrame < earliestFrame && Time.realtimeSinceStartup < deadline) yield return null;
                yield return AtPresentation(() =>
                {
                    Assert.That(shower.ExitPromptVisible, Is.True);
                    Assert.That(shower.ExitPromptText, Is.EqualTo(LocalizationService.Get(HomeShowerInteraction.ExitPromptKey)));
                    StringAssert.Contains("Q", shower.ExitPromptText);
                    Assert.That(shower.ExitPromptRenderedFrame, Is.GreaterThanOrEqualTo(earliestFrame));
                    Assert.That(affordance.ExitOutlineMesh, Is.SameAs(cold), "The exit outline belongs to the actual cold wheel.");
                    Assert.That(affordance.ExitOutlineCount, Is.GreaterThanOrEqualTo(3));
                    Rect label = shower.ExitPromptRenderedScreenRect, outline = affordance.ExitOutlineScreenRect;
                    Assert.That(label.width, Is.GreaterThan(20f));
                    Assert.That(label.xMin >= 0f && label.yMin >= 0f && label.xMax <= Screen.width && label.yMax <= Screen.height, Is.True);
                    Assert.That(label.xMax, Is.LessThan(outline.xMin), "Q is drawn to the left of the cold-wheel outline.");
                    Assert.That(affordance.ExitPromptLeaderStart.x, Is.EqualTo(label.xMax).Within(2f));
                    outline.xMin -= 2f; outline.xMax += 2f; outline.yMin -= 2f; outline.yMax += 2f;
                    Assert.That(outline.Contains(affordance.ExitPromptLeaderEnd), Is.True, "The actual leader reaches the drawn wheel outline.");
                    Assert.That(Vector2.Distance(affordance.ExitPromptLeaderStart, affordance.ExitPromptLeaderEnd), Is.GreaterThan(5f));
                });
                yield return CaptureShowerUiFrame("HomeShowerHardware", "37-q-exit-cold-ui");
            }
            var input = new InputTestFixture();
            Keyboard keyboard = null;
            bool heldSoap = shower.SoapHeld;
            try
            {
                input.Setup();
                keyboard = InputSystem.AddDevice<Keyboard>();
                if (heldSoap) input.Press(keyboard.eKey, queueEventOnly: true);
                input.Press(keyboard.qKey, queueEventOnly: true);
                yield return null;
                yield return AtPresentation(() =>
                {
                    if (heldSoap) Assert.That(shower.SoapPhase, Is.EqualTo(HomeShowerSoapPhase.PutDown));
                    else Assert.That(shower.Timeline.Phase, Is.EqualTo(HomeShowerScenePhase.WaterOff));
                    Assert.That(shower.ExitPromptVisible, Is.False, "The real Q key consumes the exit prompt.");
                });
                input.Release(keyboard.qKey, queueEventOnly: true);
                if (heldSoap) input.Release(keyboard.eKey, queueEventOnly: true);
                yield return null;
            }
            finally
            {
                if (keyboard != null && keyboard.added) InputSystem.RemoveDevice(keyboard);
                input.TearDown();
            }
        }

        private void AssertShowerHardware(MeshFilter hotWheel)
        {
            Assert.That(hotWheel, Is.Not.Null);
            Assert.That(hotWheel.sharedMesh, Is.Not.Null);
            Assert.That(hotWheel.sharedMesh.triangles.Length / 3, Is.EqualTo(180), "Use the authored sink wheel, not the old shower button.");
            Mesh sinkMesh = home.Room.GetComponentInChildren<HomeSinkFaucet>().Handle.GetComponent<MeshFilter>().sharedMesh;
            Mesh coldMesh = home.Room.Find("Home Bathroom Shower Mixer Handle Cold").GetComponentInChildren<MeshFilter>().sharedMesh;
            Assert.That(hotWheel.sharedMesh, Is.SameAs(sinkMesh));
            Assert.That(coldMesh, Is.SameAs(sinkMesh));
            float highestDivider = float.NegativeInfinity;
            foreach (Transform part in home.Room.GetComponentsInChildren<Transform>(true))
            {
                Assert.That(part.name.StartsWith("Home Bathroom Shower Hose ", System.StringComparison.Ordinal), Is.False,
                    "The removed green hose segments must not remain as hidden runtime nodes: " + part.name);
                if (!part.name.StartsWith("Home Bathroom Shower Curtain", System.StringComparison.Ordinal) &&
                    !part.name.StartsWith("Home Bathroom Shower Rail", System.StringComparison.Ordinal)) continue;
                Renderer renderer = part.GetComponent<Renderer>();
                if (renderer != null) highestDivider = Mathf.Max(highestDivider, renderer.bounds.max.y);
            }
            Assert.That(float.IsNegativeInfinity(highestDivider), Is.False);
            Renderer lamp = home.Room.Find("Home Bathroom Cold Tube").GetComponent<Renderer>();
            Renderer housing = home.Room.Find("Home Bathroom Cold Fixture").GetComponent<Renderer>();
            Assert.That(Mathf.Min(lamp.bounds.min.y, housing.bounds.min.y), Is.GreaterThan(highestDivider),
                "The whole lamp, including its housing, must be above the curtain and its rail.");
        }

        private static void AssertShowerStreamReachesBody(HomeShowerInteraction shower, Camera camera, Player3DAssetRegistry registry)
        {
            ParticleSystem stream = shower.WaterEffect.StreamParticles;
            Assert.That(stream.main.simulationSpace, Is.EqualTo(ParticleSystemSimulationSpace.World));
            var particles = new ParticleSystem.Particle[stream.main.maxParticles];
            int count = stream.GetParticles(particles), visible = 0;
            for (int index = 0; index < count; index++)
            {
                Vector3 viewport = camera.WorldToViewportPoint(particles[index].position);
                if (viewport.z > camera.nearClipPlane && viewport.x > 0.03f && viewport.x < 0.97f &&
                    viewport.y > 0.03f && viewport.y < 0.97f && particles[index].GetCurrentColor(stream).a > 0 &&
                    particles[index].GetCurrentSize(stream) > 0.001f) visible++;
            }
            Assert.That(visible, Is.GreaterThanOrEqualTo(3),
                $"Real visible water must pass in front of the ordinary wall-facing lens, before any look input: {visible}/{count} particles.");
            float nearestWaterSquared = float.PositiveInfinity, nearestNozzleSquared = float.PositiveInfinity;
            var baked = new Mesh();
            try
            {
                // One bounded measurement of the actual upper-body skin,
                // separate from the continuous hand/body safety observer.
                foreach (string name in new[] { "GEO_Torso", "GEO_UpperArm.L", "GEO_UpperArm.R", "GEO_Forearm.L", "GEO_Forearm.R" })
                {
                    var skin = (SkinnedMeshRenderer)Find(registry, name).Renderer;
                    Assert.That(skin.enabled, Is.True, name);
                    baked.Clear(false);
                    skin.BakeMesh(baked, true);
                    Vector3[] vertices = baked.vertices;
                    for (int index = 0; index < vertices.Length; index++) vertices[index] = skin.transform.TransformPoint(vertices[index]);
                    int[] triangles = baked.triangles;
                    for (int triangle = 0; triangle < triangles.Length; triangle += 3)
                    {
                        Vector3 a = vertices[triangles[triangle]], b = vertices[triangles[triangle + 1]], c = vertices[triangles[triangle + 2]];
                        nearestNozzleSquared = Mathf.Min(nearestNozzleSquared,
                            PointTriangleSquaredDistance(stream.transform.position, a, b, c));
                        for (int index = 0; index < count; index++)
                            nearestWaterSquared = Mathf.Min(nearestWaterSquared,
                                PointTriangleSquaredDistance(particles[index].position, a, b, c));
                    }
                }
            }
            finally { Object.DestroyImmediate(baked); }
            float waterDistance = Mathf.Sqrt(nearestWaterSquared), nozzleDistance = Mathf.Sqrt(nearestNozzleSquared);
            string measured = $"visible={visible}/{count}; nearestWaterSkin={waterDistance:F4}m; nozzleSkin={nozzleDistance:F4}m";
            Debug.Log("Shower stream framing: " + measured);
            Assert.That(waterDistance, Is.LessThan(0.10f), "The actual fan reaches the upper-body/forearm area. " + measured);
            Assert.That(waterDistance, Is.LessThan(nozzleDistance - 0.02f), "The particles travel toward the hero from the tilted nozzle. " + measured);
        }

        private static float MeasureHandToValveDistance(Player3DAssetRegistry registry, MeshFilter valve, bool left = false)
        {
            var hand = (SkinnedMeshRenderer)Find(registry, left ? "GEO_Hand.L" : "GEO_Hand.R").Renderer;
            var baked = new Mesh();
            try
            {
                hand.BakeMesh(baked, true);
                Vector3[] handVertices = baked.vertices, valveVertices = valve.sharedMesh.vertices;
                for (int index = 0; index < handVertices.Length; index++) handVertices[index] = hand.transform.TransformPoint(handVertices[index]);
                for (int index = 0; index < valveVertices.Length; index++) valveVertices[index] = valve.transform.TransformPoint(valveVertices[index]);
                float minimum = float.PositiveInfinity;
                int[] valveTriangles = valve.sharedMesh.triangles, handTriangles = baked.triangles;
                foreach (Vector3 point in handVertices)
                    for (int triangle = 0; triangle < valveTriangles.Length; triangle += 3)
                        minimum = Mathf.Min(minimum, PointTriangleSquaredDistance(point, valveVertices[valveTriangles[triangle]],
                            valveVertices[valveTriangles[triangle + 1]], valveVertices[valveTriangles[triangle + 2]]));
                foreach (Vector3 point in valveVertices)
                    for (int triangle = 0; triangle < handTriangles.Length; triangle += 3)
                        minimum = Mathf.Min(minimum, PointTriangleSquaredDistance(point, handVertices[handTriangles[triangle]],
                            handVertices[handTriangles[triangle + 1]], handVertices[handTriangles[triangle + 2]]));
                return Mathf.Sqrt(minimum);
            }
            finally { Object.DestroyImmediate(baked); }
        }

        private static float PointTriangleSquaredDistance(Vector3 point, Vector3 a, Vector3 b, Vector3 c)
        {
            Vector3 ab = b - a, ac = c - a, ap = point - a;
            float d1 = Vector3.Dot(ab, ap), d2 = Vector3.Dot(ac, ap);
            if (d1 <= 0f && d2 <= 0f) return ap.sqrMagnitude;
            Vector3 bp = point - b;
            float d3 = Vector3.Dot(ab, bp), d4 = Vector3.Dot(ac, bp);
            if (d3 >= 0f && d4 <= d3) return bp.sqrMagnitude;
            float vc = d1 * d4 - d3 * d2;
            if (vc <= 0f && d1 >= 0f && d3 <= 0f) return (point - (a + ab * (d1 / (d1 - d3)))).sqrMagnitude;
            Vector3 cp = point - c;
            float d5 = Vector3.Dot(ab, cp), d6 = Vector3.Dot(ac, cp);
            if (d6 >= 0f && d5 <= d6) return cp.sqrMagnitude;
            float vb = d5 * d2 - d1 * d6;
            if (vb <= 0f && d2 >= 0f && d6 <= 0f) return (point - (a + ac * (d2 / (d2 - d6)))).sqrMagnitude;
            float va = d3 * d6 - d5 * d4;
            if (va <= 0f && d4 - d3 >= 0f && d5 - d6 >= 0f)
                return (point - (b + (c - b) * ((d4 - d3) / ((d4 - d3) + (d5 - d6))))).sqrMagnitude;
            float sum = va + vb + vc;
            return (point - (a + ab * (vb / sum) + ac * (vc / sum))).sqrMagnitude;
        }

        private IEnumerator LookAtShowerHeadWithArrowKeys(HomeShowerInteraction shower, Camera camera, Transform nozzle,
            string captureArea = "HomeShower")
        {
            Vector3 rootPosition = home.Player.GameObject.transform.position;
            Quaternion rootRotation = home.Player.GameObject.transform.rotation;
            Renderer headRenderer = home.Room.Find("Home Bathroom Shower Head").GetComponent<Renderer>();
            Assert.That(headRenderer, Is.Not.Null);
            Bounds headBounds = headRenderer.bounds;
            headBounds.Encapsulate(nozzle.GetComponent<Renderer>().bounds);
            float originalYaw = shower.View.LookYawDegrees, originalPitch = shower.View.LookPitchDegrees;
            var input = new InputTestFixture();
            Keyboard keyboard = null;
            try
            {
                input.Setup();
                keyboard = InputSystem.AddDevice<Keyboard>();
                Assert.That(shower.View.IsPointerMode, Is.True, "Arrow look must work while the mouse remains the soap pointer.");
                float beforeUp = shower.View.LookPitchDegrees;
                input.Press(keyboard.upArrowKey, queueEventOnly: true);
                float deadline = Time.realtimeSinceStartup + 2f;
                bool headVisible = false;
                while (!headVisible && Time.realtimeSinceStartup < deadline)
                {
                    yield return AtPresentation(() =>
                    {
                        headVisible = shower.View.LookPitchDegrees < beforeUp - 5f &&
                            BoundsFullyInsideViewport(camera, headBounds, 0.03f);
                    });
                }
                input.Release(keyboard.upArrowKey, queueEventOnly: true);
                yield return AtPresentation(() =>
                {
                    Assert.That(headVisible && BoundsFullyInsideViewport(camera, headBounds, 0.03f), Is.True,
                        "Holding the real Up Arrow must bring the entire shower head, including its face, into the permitted view.");
                    Assert.That(shower.View.LookPitchDegrees, Is.LessThan(beforeUp - 5f));
                    CaptureFrame(captureArea, "19-look-at-shower-head");
                });
                float beforeDown = shower.View.LookPitchDegrees;
                input.Press(keyboard.downArrowKey, queueEventOnly: true);
                yield return new WaitForSeconds(0.12f);
                input.Release(keyboard.downArrowKey, queueEventOnly: true);
                yield return AtPresentation(() => Assert.That(shower.View.LookPitchDegrees, Is.GreaterThan(beforeDown + 3f)));
                shower.View.ApplyLookDelta(-shower.View.LookYawDegrees, 0f);
                yield return AtPresentation(() => Assert.That(shower.View.LookYawDegrees, Is.Zero.Within(0.01f),
                    "Horizontal arrow probes start inside the look cone, after the prior drain glance."));
                float beforeLeft = shower.View.LookYawDegrees;
                input.Press(keyboard.leftArrowKey, queueEventOnly: true);
                yield return new WaitForSeconds(0.12f);
                input.Release(keyboard.leftArrowKey, queueEventOnly: true);
                yield return AtPresentation(() => Assert.That(shower.View.LookYawDegrees, Is.LessThan(beforeLeft - 3f)));
                float beforeRight = shower.View.LookYawDegrees;
                input.Press(keyboard.rightArrowKey, queueEventOnly: true);
                yield return new WaitForSeconds(0.12f);
                input.Release(keyboard.rightArrowKey, queueEventOnly: true);
                yield return AtPresentation(() =>
                {
                    Assert.That(shower.View.LookYawDegrees, Is.GreaterThan(beforeRight + 3f));
                    Assert.That(Vector3.Distance(home.Player.GameObject.transform.position, rootPosition), Is.LessThan(0.005f));
                    Assert.That(Quaternion.Angle(home.Player.GameObject.transform.rotation, rootRotation), Is.LessThan(0.5f));
                    Assert.That(shower.WashingProgress.Amount, Is.Zero, "Looking around does not wash the body.");
                    Assert.That(shower.View.BasePitchDegrees + shower.View.LookPitchDegrees,
                        Is.InRange(HomeShowerFirstPersonView.MinimumViewPitchDegrees, HomeShowerFirstPersonView.MaximumViewPitchDegrees));
                });
            }
            finally
            {
                if (keyboard != null && keyboard.added) InputSystem.RemoveDevice(keyboard);
                input.TearDown();
                shower.View.ApplyLookDelta(originalYaw - shower.View.LookYawDegrees, originalPitch - shower.View.LookPitchDegrees);
            }
        }

        private static bool BoundsFullyInsideViewport(Camera camera, Bounds bounds, float inset)
        {
            for (int corner = 0; corner < 8; corner++)
            {
                Vector3 point = camera.WorldToViewportPoint(bounds.center + Vector3.Scale(bounds.extents,
                    new Vector3((corner & 1) == 0 ? -1f : 1f, (corner & 2) == 0 ? -1f : 1f,
                        (corner & 4) == 0 ? -1f : 1f)));
                if (point.z <= 0f || point.x < inset || point.x > 1f - inset ||
                    point.y < inset || point.y > 1f - inset) return false;
            }
            return true;
        }

        private static void AssertShowerRightArmExcluded(HomeShowerInteraction shower, Camera camera, Player3DAssetRegistry registry)
        {
            Assert.That(shower.SoapHeld, Is.True);
            Assert.That(shower.SoapPose.TryGetRegionTarget(HomeShowerWashRegion.RightArm, out _), Is.False,
                "The right arm holding the soap must not offer a washing target.");
            float progress = shower.WashingProgress.Amount;
            Assert.That(shower.WashingProgress.Credit(HomeShowerWashRegion.RightArm, 0.01f, 0.01f, true, 0.05f), Is.Zero,
                "Even a reported contact on the excluded right arm must earn no progress.");
            Assert.That(shower.WashingProgress.Amount, Is.EqualTo(progress));
            var baked = new Mesh();
            try
            {
                foreach (string name in new[] { "GEO_UpperArm.R", "GEO_Forearm.R", "GEO_Hand.R", "GEO_Thumb.R" })
                {
                    var renderer = (SkinnedMeshRenderer)Find(registry, name).Renderer;
                    Assert.That(renderer.enabled, Is.True, name);
                    baked.Clear(false);
                    renderer.BakeMesh(baked, true);
                    Vector3[] vertices = baked.vertices;
                    int[] triangles = baked.triangles;
                    Assert.That(triangles.Length, Is.GreaterThanOrEqualTo(3), name);
                    for (int index = 0; index < vertices.Length; index++) vertices[index] = renderer.transform.TransformPoint(vertices[index]);
                    int stride = Mathf.Max(1, triangles.Length / 24) * 3;
                    for (int triangle = 0; triangle < triangles.Length; triangle += stride)
                    {
                        Vector3 a = vertices[triangles[triangle]], b = vertices[triangles[triangle + 1]], c = vertices[triangles[triangle + 2]];
                        Vector3 point = (a + b + c) / 3f, normal = Vector3.Cross(b - a, c - a).normalized;
                        foreach (Ray ray in new[] { new Ray(camera.transform.position, point - camera.transform.position),
                            new Ray(point + normal * 0.025f, -normal) })
                            Assert.That(shower.SoapPose.TryPickBody(ray, out HomeShowerSoapTarget hit) &&
                                hit.Region == HomeShowerWashRegion.RightArm, Is.False,
                                name + " must be excluded from real body picking, including its proximal mesh.");
                    }
                }
            }
            finally { Object.DestroyImmediate(baked); }
        }

        private static void AssertShowerSoapVisiblePastRightArm(Camera camera, Transform soap, Player3DAssetRegistry registry)
        {
            Bounds bounds = soap.GetComponentInChildren<Renderer>().bounds;
            Vector3 viewport = camera.WorldToViewportPoint(bounds.center);
            Assert.That(viewport.z, Is.GreaterThan(0f));
            Assert.That(viewport.x, Is.InRange(0.03f, 0.97f), "The soap must remain in the first-person frame.");
            Assert.That(viewport.y, Is.InRange(0.03f, 0.97f));
            var sample = new Mesh();
            try
            {
                foreach (Player3DMeshBinding binding in registry.MeshBindings)
                {
                    if (binding == null || !(binding.Renderer is SkinnedMeshRenderer arm) || !arm.enabled ||
                        (binding.MeshName != "GEO_UpperArm.R" && binding.MeshName != "GEO_Forearm.R" &&
                         binding.MeshName != "GEO_Hand.R" && binding.MeshName != "GEO_Thumb.R")) continue;
                    sample.Clear(false);
                    arm.BakeMesh(sample, true);
                    Vector3[] vertices = sample.vertices;
                    for (int index = 0; index < vertices.Length; index++) vertices[index] = arm.transform.TransformPoint(vertices[index]);
                    int[] triangles = sample.triangles;
                    foreach (Vector2 offset in new[] { Vector2.zero, new Vector2(-1f, -1f), new Vector2(1f, -1f),
                        new Vector2(-1f, 1f), new Vector2(1f, 1f) })
                    {
                        Vector3 target = bounds.center + Vector3.right * (offset.x * bounds.size.x * 0.25f) +
                            Vector3.up * (offset.y * bounds.size.y * 0.25f);
                        Vector3 direction = target - camera.transform.position;
                        var ray = new Ray(camera.transform.position, direction.normalized);
                        for (int triangle = 0; triangle < triangles.Length; triangle += 3)
                            Assert.That(RayTriangleDistance(ray, vertices[triangles[triangle]], vertices[triangles[triangle + 1]],
                                vertices[triangles[triangle + 2]], out float distance) && distance < direction.magnitude - 0.002f,
                                Is.False, binding.MeshName + " must not cover the soap or its shelf from the hero's eyes.");
                    }
                }
            }
            finally { Object.DestroyImmediate(sample); }
        }

        private IEnumerator PickUpShowerSoap(HomeShowerInteraction shower, Camera camera, string captureArea = "HomeShower")
        {
            Assert.That(shower.SoapPhase, Is.EqualTo(HomeShowerSoapPhase.SelectSoap));
            HomeShowerSoapAffordance affordance = shower.GetComponent<HomeShowerSoapAffordance>();
            Assert.That(affordance, Is.Not.Null);
            int promptStartFrame = Time.frameCount + 1;
            AimShowerAt(shower, camera, shower.Soap.position);
            float promptDeadline = Time.realtimeSinceStartup + 2f;
            while (affordance.SoapPromptRenderedFrame < promptStartFrame && Time.realtimeSinceStartup < promptDeadline)
                yield return null;
            yield return AtPresentation(() =>
            {
                Assert.That(shower.SoapPromptVisible, Is.True, "The nearby soap advertises E without requiring mouse hover.");
                Assert.That(shower.SoapPromptText, Is.Not.Empty);
                Assert.That(shower.SoapPromptText, Is.EqualTo(LocalizationService.Get(HomeShowerInteraction.SoapPromptKey)));
                StringAssert.Contains("E", shower.SoapPromptText);
                Vector2 soapScreen = camera.WorldToScreenPoint(shower.Soap.GetComponentInChildren<Renderer>().bounds.center);
                Assert.That(Vector2.Distance(shower.SoapPromptScreenPosition, soapScreen), Is.LessThan(100f),
                    "The localized pickup label belongs beside the visible soap.");
                Assert.That(affordance.SoapPromptRenderedFrame, Is.GreaterThanOrEqualTo(promptStartFrame),
                    "The E label must be drawn by the real OnGUI repaint, not just declared visible.");
                Rect label = affordance.SoapPromptRenderedScreenRect;
                Assert.That(label.width, Is.GreaterThan(20f));
                Assert.That(label.height, Is.GreaterThan(8f));
                Assert.That(label.xMin >= 0f && label.yMin >= 0f && label.xMax <= Screen.width && label.yMax <= Screen.height,
                    Is.True, "The actually drawn prompt remains inside the screen.");
                Vector2 guiSoap = new Vector2(soapScreen.x, Screen.height - soapScreen.y);
                Vector2 nearest = new Vector2(Mathf.Clamp(guiSoap.x, label.xMin, label.xMax), Mathf.Clamp(guiSoap.y, label.yMin, label.yMax));
                Assert.That(Vector2.Distance(guiSoap, nearest), Is.LessThan(Screen.height * 0.15f),
                    "The rendered label is anchored beside the soap, not a distant screen prompt.");
                // Camera.Render records scene framing only. The actual IMGUI
                // label is proved by its current repaint/rectangle above.
                CaptureFrame(captureArea, "27-soap-e-prompt");
            });
            yield return CaptureShowerUiFrame(captureArea, "27-soap-e-ui");
            AimShowerAt(shower, camera, home.Room.Find("Home Bathroom Shower Head Face").position);
            yield return AtPresentation(() => Assert.That(shower.SoapPromptVisible, Is.False,
                "This E pickup is deliberately performed while looking away from the soap."));
            var input = new InputTestFixture();
            Keyboard keyboard = null;
            string latestPreCleanup = "No pickup presentation sampled.";
            try
            {
                input.Setup();
                keyboard = InputSystem.AddDevice<Keyboard>();
                input.Press(keyboard.eKey, queueEventOnly: true);
                yield return null;
                yield return AtPresentation(() =>
                {
                    Assert.That(shower.SoapPhase, Is.EqualTo(HomeShowerSoapPhase.Pickup),
                        "The real E key must enter the shared pickup handler before it can request scene exit.");
                    Assert.That(shower.Timeline.Phase, Is.EqualTo(HomeShowerScenePhase.Wash));
                    Assert.That(shower.SoapPromptVisible, Is.False);
                });
                input.Release(keyboard.eKey, queueEventOnly: true);
                yield return null;
                yield return AtPresentation(() => { });
                float pickupStarted = Time.time;
                yield return ObserveShowerPresentationUntil(() =>
                {
                    latestPreCleanup = $"soapPhase={shower.SoapPhase}; phase={shower.Timeline.Phase}; " +
                        $"phaseElapsed={shower.Timeline.PhaseElapsed:F4}; pickupElapsed={Time.time - pickupStarted:F4}; " +
                        $"viewActive={shower.View.IsActive}; soapHeld={shower.SoapHeld}; " + shower.SoapPose.ContactDiagnostic;
                }, () => shower.SoapPhase == HomeShowerSoapPhase.Washing || !shower.View.IsActive,
                    Time.realtimeSinceStartup + TimeoutSeconds);
                if (shower.SoapPhase != HomeShowerSoapPhase.Washing || !shower.View.IsActive)
                {
                    yield return AtPresentation(() => CaptureFrame(captureArea, "24-pickup-failed"));
                    Assert.Fail("The real E pickup never finished. Latest presentation before cleanup: " +
                        latestPreCleanup + ShowerContactFailureDetails);
                }
                Assert.That(shower.SoapHeld, Is.True);
                Assert.That(shower.HasSelectedWashPoint, Is.False, "Taking the soap does not choose a body point.");
                Assert.That(shower.Timeline.Phase, Is.EqualTo(HomeShowerScenePhase.Wash));
            }
            finally
            {
                if (keyboard != null && keyboard.added) InputSystem.RemoveDevice(keyboard);
                input.TearDown();
            }
        }

        private IEnumerator ObserveSelectedShowerPoint(HomeShowerInteraction shower, Camera camera,
            HomeShowerWashRegion region, bool finishGauge, System.Func<string> bodyViolation)
        {
            HomeShowerSoapTarget target = default;
            float startedProgress = shower.WashingProgress.Amount;
            float actualTravel = 0f, contactSeconds = 0f, maximumResponseAngle = 0f,
                maximumResponseDisplacement = 0f, maximumAnchorError = 0f;
            int contactFrames = 0, creditedFrames = 0;
            float previousCleaned = shower.WashingProgress.CleanedDistance;
            float nextLogAt = Time.realtimeSinceStartup + 8f;
            long passiveCandidatesBefore = shower.WashPose.PassiveCollisionWorkCandidates;
            long passiveTrianglesBefore = shower.WashPose.PassiveCollisionTriangleTests;
            long passiveCallsBefore = shower.WashPose.PassiveClearanceCalls;
            long passiveTicksBefore = shower.WashPose.PassiveClearanceElapsedTicks;
            long passiveVerticesBefore = shower.WashPose.PassiveObstacleVertexUpdates;
            long maximumPassiveTicks = 0;
            long solveCountBefore = shower.SoapPose.ContactSolveCount;
            double solveMillisecondsBefore = shower.SoapPose.ContactSolveTotalMilliseconds;
            string lastPresentation = "No selected contact sampled.";
            bool completed = false;
            bool capturedCycle = false, capturedResponse = false;
            var input = new InputTestFixture();
            Mouse mouse = null;
            try
            {
                input.Setup();
                mouse = InputSystem.AddDevice<Mouse>();
                yield return AtPresentation(() =>
                {
                    Assert.That(shower.SoapPose.TryGetRegionTarget(region, out target), Is.True, region.ToString());
                    AimShowerAt(shower, camera, target.WorldPoint);
                });
                Vector2 stationaryPointer = default;
                yield return AtPresentation(() =>
                {
                    Vector3 viewport = camera.WorldToViewportPoint(target.WorldPoint);
                    Assert.That(viewport.z > 0f && viewport.x > 0f && viewport.x < 1f && viewport.y > 0f && viewport.y < 1f,
                        Is.True, $"Selected {region} must be visible: {viewport:F4}");
                    stationaryPointer = camera.WorldToScreenPoint(target.WorldPoint);
                    input.Set(mouse.position, stationaryPointer, queueEventOnly: true);
                    input.Press(mouse.leftButton, queueEventOnly: true);
                });
                yield return null;
                yield return AtPresentation(() =>
                {
                    Assert.That(mouse.leftButton.isPressed, Is.True);
                    if (!shower.HasSelectedWashPoint) CaptureShower("29-selection-" + region + "-failed");
                    Assert.That(shower.HasSelectedWashPoint, Is.True,
                        $"The actual mouse press selects {region}: pointer={stationaryPointer:F3}, livePointer={shower.WashingPointer:F3}, " +
                        $"targetViewport={camera.WorldToViewportPoint(target.WorldPoint):F4}, soapPhase={shower.SoapPhase}, scenePhase={shower.Timeline.Phase}; " +
                        shower.SelectionDiagnostic);
                    Assert.That(shower.SelectedWashTarget.Region, Is.EqualTo(region));
                    target = shower.SelectedWashTarget;
                });
                float deadline = Time.realtimeSinceStartup + (finishGauge
                    ? TimeoutSeconds + HomeShowerWashingProgress.MinimumActiveSeconds : 8f);
                // Keep the physical LMB held, with no more mouse events or
                // synthetic deltas. Only the automatic hand cycle can move.
                yield return ObserveShowerPresentationUntil(() =>
                {
                    HomeShowerSoapPose pose = shower.SoapPose;
                    Assert.That(mouse.leftButton.isPressed, Is.True);
                    Assert.That(Vector2.Distance(mouse.position.ReadValue(), stationaryPointer), Is.LessThan(0.01f));
                    Assert.That(shower.HasSelectedWashPoint, Is.True);
                    Assert.That(shower.SelectedWashTarget.Region, Is.EqualTo(region));
                    Assert.That(shower.SelectedWashTarget.Anchor, Is.SameAs(target.Anchor));
                    Assert.That(Vector3.Distance(shower.SelectedWashTarget.LocalPoint, target.LocalPoint), Is.LessThan(0.001f),
                        "Holding LMB with a stationary cursor retains the chosen body point.");
                    if (pose.IsContacting && pose.CurrentRegion == region)
                    {
                        contactFrames++;
                        contactSeconds += Time.deltaTime;
                        actualTravel += pose.ContactTravelMetres;
                    }
                    float cleaned = shower.WashingProgress.CleanedDistance;
                    if (cleaned > previousCleaned) creditedFrames++;
                    previousCleaned = cleaned;
                    maximumResponseAngle = Mathf.Max(maximumResponseAngle, shower.WashPose.PassiveContactAngleDegrees);
                    maximumResponseDisplacement = Mathf.Max(maximumResponseDisplacement, shower.WashPose.PassiveContactDisplacementMetres);
                    maximumAnchorError = Mathf.Max(maximumAnchorError, shower.WashPose.PassiveAnchorError);
                    maximumPassiveTicks = System.Math.Max(maximumPassiveTicks, shower.WashPose.PassiveClearanceLastTicks);
                    if (finishGauge && !capturedCycle && shower.WashingProgress.Amount >= 0.25f)
                    {
                        capturedCycle = true;
                        CaptureShower("16-scrub-body");
                    }
                    if (region == HomeShowerWashRegion.Intimate && !capturedResponse &&
                        pose.IsContacting && maximumResponseAngle > 0.5f && maximumResponseDisplacement > 0.001f)
                    {
                        capturedResponse = true;
                        CaptureShower("28-passive-soap-contact");
                    }
                    lastPresentation = $"region={region}; progress={shower.WashingProgress.Amount:F4}; contactFrames={contactFrames}; contactSeconds={contactSeconds:F3}; " +
                        $"creditedFrames={creditedFrames}; actualTravel={actualTravel:F5}; target={target.SurfaceName}/{target.WorldPoint:F5}; " +
                        $"projected={camera.WorldToViewportPoint(target.WorldPoint):F4}; responseAngle={maximumResponseAngle:F4}; " +
                        $"responseDisplacement={maximumResponseDisplacement:F5}; anchorError={maximumAnchorError:F5}; " +
                        "bodyViolation=" + (bodyViolation?.Invoke() ?? "none") + "; " + pose.ContactDiagnostic;
                    if (Time.realtimeSinceStartup >= nextLogAt)
                    {
                        nextLogAt = Time.realtimeSinceStartup + 8f;
                        Debug.Log("Shower held automatic scrub: " + lastPresentation);
                    }
                }, () => shower.SoapPhase != HomeShowerSoapPhase.Washing || shower.WashingProgress.Complete ||
                    (!finishGauge && contactFrames >= 4 && actualTravel >= 0.02f && creditedFrames >= 2 &&
                        (region != HomeShowerWashRegion.Intimate ||
                         (contactSeconds >= 1.25f && maximumResponseAngle > 0.5f && maximumResponseDisplacement > 0.001f))), deadline);
                completed = finishGauge ? shower.WashingProgress.Complete :
                    contactFrames >= 4 && actualTravel >= 0.02f && creditedFrames >= 2 &&
                    (region != HomeShowerWashRegion.Intimate ||
                     (contactSeconds >= 1.25f && maximumResponseAngle > 0.5f && maximumResponseDisplacement > 0.001f));
                if (!completed)
                {
                    yield return AtPresentation(() => CaptureShower("17-auto-contact-" + region + "-failed"));
                    Debug.Log("Shower automatic contact failed: " + lastPresentation);
                    showerContactFailures.Add(region + ": " + lastPresentation);
                }
                if (region == HomeShowerWashRegion.Intimate)
                {
                    long candidates = shower.WashPose.PassiveCollisionWorkCandidates - passiveCandidatesBefore;
                    long triangles = shower.WashPose.PassiveCollisionTriangleTests - passiveTrianglesBefore;
                    long calls = shower.WashPose.PassiveClearanceCalls - passiveCallsBefore;
                    long ticks = shower.WashPose.PassiveClearanceElapsedTicks - passiveTicksBefore;
                    long vertices = shower.WashPose.PassiveObstacleVertexUpdates - passiveVerticesBefore;
                    long solves = shower.SoapPose.ContactSolveCount - solveCountBefore;
                    double solveMilliseconds = shower.SoapPose.ContactSolveTotalMilliseconds - solveMillisecondsBefore;
                    double passiveMilliseconds = ticks * 1000d / System.Diagnostics.Stopwatch.Frequency;
                    string performance = $"passiveCalls={calls}; passiveMeanMs={passiveMilliseconds / System.Math.Max(1L, calls):F4}; " +
                        $"passiveSampleMaxMs={maximumPassiveTicks * 1000d / System.Diagnostics.Stopwatch.Frequency:F4}; " +
                        $"equivalentUnfilteredTriangles={candidates}; actualTriangleTests={triangles}; " +
                        $"triangleWorkRatio={(double)triangles / System.Math.Max(1L, candidates):F6}; obstacleVertexUpdates={vertices}; " +
                        $"stepHandCalls={solves}; stepHandMeanMs={solveMilliseconds / System.Math.Max(1L, solves):F4}; " +
                        $"stepHandLifetimeMaxMs={shower.SoapPose.ContactSolveMaxMilliseconds:F4}; " +
                        $"contactFrames={contactFrames}; contactSeconds={contactSeconds:F3}; contactTravel={actualTravel:F5}";
                    // These clocks are inside production code. The independent
                    // mesh-baking safety observer and captures are not timed.
                    Debug.Log("Shower intimate production performance: " + performance);
                    if (completed)
                    {
                        Assert.That(calls, Is.GreaterThan(0), performance);
                        Assert.That(ticks, Is.GreaterThan(0), performance);
                        Assert.That(solves, Is.GreaterThan(0), performance);
                        Assert.That(candidates, Is.GreaterThan(0), performance);
                        Assert.That(triangles, Is.GreaterThan(0).And.LessThan(candidates),
                            "The active physical response must retain exact triangle checks while avoiding the old unfiltered work. " + performance);
                    }
                }
                input.Release(mouse.leftButton, queueEventOnly: true);
                yield return null;
                yield return AtPresentation(() => Assert.That(mouse.leftButton.isPressed, Is.False));
                if (!finishGauge)
                {
                    if (completed) Assert.That(shower.WashingProgress.Amount, Is.GreaterThan(startedProgress),
                        "The automatic cycle earns progress with a held button and a stationary cursor.");
                    Assert.That(maximumResponseAngle, Is.LessThanOrEqualTo(HomeShowerWashPose.PassiveContactLimitDegrees + 0.05f));
                    Assert.That(maximumAnchorError, Is.LessThan(0.001f), "Contact may rotate hanging geometry but cannot detach its root.");
                    float releasedProgress = shower.WashingProgress.Amount;
                    yield return new WaitForSeconds(1f);
                    yield return AtPresentation(() =>
                    {
                        Assert.That(shower.WashingProgress.Amount, Is.EqualTo(releasedProgress), "Releasing LMB stops automatic washing and its progress.");
                        Assert.That(shower.SoapPose.IsContacting, Is.False, "The released hand withdraws from skin contact.");
                        if (region == HomeShowerWashRegion.Intimate)
                            Assert.That(shower.WashPose.PassiveContactAngleDegrees, Is.LessThan(0.5f),
                                "The passive response settles after real contact stops.");
                    });
                }
            }
            finally
            {
                if (mouse != null && mouse.added) InputSystem.RemoveDevice(mouse);
                input.TearDown();
            }
            if (finishGauge)
                Assert.That(completed, Is.True, "Holding LMB over one stationary Torso point must finish the shared gauge. " +
                    lastPresentation + ShowerContactFailureDetails);
        }

        private string ShowerContactFailureDetails
        {
            get
            {
                string details = showerContactFailures.Count == 0 ? string.Empty :
                    "\nDeferred shower contact failures:\n" + string.Join("\n", showerContactFailures);
                string invariant = home != null ? home.GetComponent<HomeShowerInvariantProbe>()?.Violation : null;
                return invariant == null ? details : details + "\nFirst shower presentation violation: " + invariant;
            }
        }

        private IEnumerator ObserveShowerPresentationUntil(System.Action sample, System.Func<bool> completed, float deadline)
        {
            var probe = home.GetComponent<HomeBathroomPresentationProbe>() ??
                home.gameObject.AddComponent<HomeBathroomPresentationProbe>();
            System.Action previousObserver = probe.Observe;
            System.Exception failure = null;
            probe.Observe = () =>
            {
                try
                {
                    previousObserver?.Invoke();
                    if (failure == null && Time.realtimeSinceStartup < deadline && !completed()) sample();
                }
                catch (System.Exception exception) { failure = exception; }
            };
            try
            {
                // Observe every final presentation frame independently of
                // coroutine polling; native input remains owned by its device.
                while (failure == null && Time.realtimeSinceStartup < deadline && !completed()) yield return null;
            }
            finally { probe.Observe = previousObserver; }
            if (failure is AssertionException && ShowerContactFailureDetails.Length > 0)
                throw new AssertionException(failure.Message + ShowerContactFailureDetails, failure);
            if (failure != null) System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(failure).Throw();
        }

        private void AimShowerAt(HomeShowerInteraction shower, Camera camera, Vector3 worldTarget)
        {
            Vector3 direction = home.Player.GameObject.transform.InverseTransformDirection(worldTarget - camera.transform.position);
            float yaw = Mathf.Atan2(direction.x, direction.z) * Mathf.Rad2Deg;
            float pitch = Mathf.Atan2(-direction.y, new Vector2(direction.x, direction.z).magnitude) * Mathf.Rad2Deg;
            Vector2 best = default;
            float bestFrameExtent = float.PositiveInfinity;
            foreach (Vector2 candidate in new[]
            {
                new Vector2(yaw, pitch),
                new Vector2(Mathf.DeltaAngle(0f, yaw + 180f), 180f - pitch)
            })
            {
                float allowedYaw = Mathf.Clamp(candidate.x, -HomeShowerFirstPersonView.MaximumLookYawDegrees,
                    HomeShowerFirstPersonView.MaximumLookYawDegrees);
                float allowedPitch = Mathf.Clamp(candidate.y - shower.Timeline.ViewPitchDegrees,
                    HomeShowerFirstPersonView.MinimumLookPitchDegrees, HomeShowerFirstPersonView.MaximumLookPitchDegrees);
                Vector3 cameraDirection = Quaternion.Inverse(Quaternion.Euler(
                    shower.Timeline.ViewPitchDegrees + allowedPitch, allowedYaw, 0f)) * direction;
                if (cameraDirection.z <= 0.0001f) continue;
                float verticalTangent = Mathf.Tan(camera.fieldOfView * Mathf.Deg2Rad * 0.5f);
                float frameExtent = Mathf.Max(Mathf.Abs(cameraDirection.x) / (cameraDirection.z * verticalTangent * camera.aspect),
                    Mathf.Abs(cameraDirection.y) / (cameraDirection.z * verticalTangent));
                if (frameExtent >= bestFrameExtent) continue;
                bestFrameExtent = frameExtent;
                best = new Vector2(allowedYaw, allowedPitch);
            }
            shower.View.ApplyLookDelta(best.x - shower.View.LookYawDegrees, best.y - shower.View.LookPitchDegrees);
        }

        private static void AssertShowerSoapRest(Transform soap, Transform parent, Vector3 position, Quaternion rotation, Vector3 scale)
        {
            Assert.That(soap.parent, Is.SameAs(parent), "The soap returns to its original shelf parent.");
            Assert.That(Vector3.Distance(soap.localPosition, position), Is.LessThan(0.0001f));
            Assert.That(Quaternion.Angle(soap.localRotation, rotation), Is.LessThan(0.01f));
            Assert.That(Vector3.Distance(soap.localScale, scale), Is.LessThan(0.0001f));
        }

        /// <summary>
        /// Independent readback of the rendered hand/forearm and body meshes.
        /// Like brushing's narrow phase, this checks real triangles rather than
        /// capsule radii that fill empty space around sleeves and fingers.
        /// Hidden clothes are omitted during the wash. Open garments are tested
        /// as surfaces; only watertight meshes can contain an entire hand.
        /// </summary>
        private sealed class ShowerBodyContactCheck : System.IDisposable
        {
            private const float ContactTolerance = 0.0005f;
            private sealed class Surface
            {
                public string Name;
                public SkinnedMeshRenderer Renderer;
                public Vector3[] Vertices;
                public int[] Triangles;
                public Bounds Bounds;
                public Bounds[] TriangleBounds;
                public bool IsClosed;
                public bool Visible => Renderer.enabled && Renderer.gameObject.activeInHierarchy;
            }

            private readonly List<Surface> arms = new List<Surface>();
            private readonly List<Surface> bodies = new List<Surface>();
            private readonly Player3DAssetRegistry registry;
            private readonly Mesh sample = new Mesh { name = "Shower Body Contact Readback", hideFlags = HideFlags.HideAndDontSave };

            public ShowerBodyContactCheck(Player3DAssetRegistry registry)
            {
                this.registry = registry;
                foreach (Player3DMeshBinding binding in registry.MeshBindings)
                {
                    if (binding == null || !(binding.Renderer is SkinnedMeshRenderer renderer)) continue;
                    string name = binding.MeshName;
                    bool body = name == "GEO_Torso" || name == "GEO_Pelvis" || name == "CLO_JacketBody" ||
                        name.StartsWith("GEO_Thigh.") || name.StartsWith("GEO_Shin.") ||
                        name.StartsWith("GEO_UpperArm.") || name.StartsWith("GEO_Forearm.");
                    bool arm = name.StartsWith("GEO_Hand.") || name.StartsWith("GEO_Thumb.") ||
                        name.StartsWith("GEO_Forearm.") || name.StartsWith("CLO_JacketForearm.") || name == "CLO_Bandage.L";
                    if (!body && !arm) continue;
                    if (body) bodies.Add(new Surface { Name = name, Renderer = renderer });
                    if (arm) arms.Add(new Surface { Name = name, Renderer = renderer });
                }
                Assert.That(bodies.Count, Is.GreaterThanOrEqualTo(2));
                Assert.That(arms.Count, Is.GreaterThanOrEqualTo(6), "Both hands, thumbs and forearms must be measured.");
            }

            public float MeasureVisibleMaximumLocalZ(Transform room, out string farthestPart)
            {
                float maximum = float.NegativeInfinity;
                farthestPart = null;
                foreach (Player3DMeshBinding binding in registry.MeshBindings)
                {
                    Renderer renderer = binding?.Renderer;
                    if (renderer == null || !renderer.enabled || !renderer.gameObject.activeInHierarchy) continue;
                    Mesh mesh;
                    if (renderer is SkinnedMeshRenderer skinned)
                    {
                        skinned.BakeMesh(sample, true);
                        mesh = sample;
                    }
                    else mesh = renderer.GetComponent<MeshFilter>()?.sharedMesh;
                    if (mesh == null) continue;
                    foreach (Vector3 vertex in mesh.vertices)
                    {
                        float z = room.InverseTransformPoint(renderer.transform.TransformPoint(vertex)).z;
                        if (z <= maximum) continue;
                        maximum = z;
                        farthestPart = binding.MeshName;
                    }
                }
                return maximum;
            }

            public string FindVisibleGeometryInCamera(Transform actor, Camera camera, out int measuredMeshes)
            {
                Plane[] planes = GeometryUtility.CalculateFrustumPlanes(camera);
                measuredMeshes = 0;
                foreach (Renderer renderer in actor.GetComponentsInChildren<Renderer>(true))
                {
                    if (!renderer.enabled || !renderer.gameObject.activeInHierarchy ||
                        renderer.shadowCastingMode == UnityEngine.Rendering.ShadowCastingMode.ShadowsOnly) continue;
                    Mesh mesh;
                    if (renderer is SkinnedMeshRenderer skinned)
                    {
                        skinned.BakeMesh(sample, true);
                        mesh = sample;
                    }
                    else mesh = renderer.GetComponent<MeshFilter>()?.sharedMesh;
                    Bounds bounds = renderer.bounds;
                    if (mesh != null && mesh.isReadable && mesh.vertexCount > 0)
                    {
                        Vector3[] vertices = mesh.vertices;
                        bounds = new Bounds(renderer.transform.TransformPoint(vertices[0]), Vector3.zero);
                        for (int index = 1; index < vertices.Length; index++)
                            bounds.Encapsulate(renderer.transform.TransformPoint(vertices[index]));
                        measuredMeshes++;
                    }
                    // A baked per-mesh AABB wholly outside a frustum plane
                    // proves its triangles cannot be present in that frame.
                    // Unreadable auxiliary meshes retain their conservative
                    // renderer bounds, as the production visibility gate does.
                    if (GeometryUtility.TestPlanesAABB(planes, bounds)) return renderer.name;
                }
                return null;
            }

            public string Measure()
            {
                if (sample == null) return null; // Coroutine disposal can precede scene teardown after an assertion.
                foreach (Surface body in bodies) if (body.Visible) Read(body);
                foreach (Surface arm in arms)
                {
                    if (!arm.Visible) continue;
                    Read(arm);
                    foreach (Surface body in bodies)
                    {
                        if (!body.Visible || !arm.Bounds.Intersects(body.Bounds)) continue;
                        // Adjacent meshes of one arm share authored elbow/wrist
                        // seams; the opposite arm remains a real washing target.
                        if ((body.Name.StartsWith("GEO_UpperArm.") || body.Name.StartsWith("GEO_Forearm.")) &&
                            ((body.Name.EndsWith(".L") && arm.Name.EndsWith(".L")) ||
                             (body.Name.EndsWith(".R") && arm.Name.EndsWith(".R")))) continue;
                        if (body.IsClosed)
                            foreach (Vector3 vertex in arm.Vertices)
                                if (body.Bounds.Contains(vertex) && Inside(vertex, body))
                                    return arm.Name + " inside " + body.Name + " at " + vertex.ToString("F5");
                        for (int a = 0; a < arm.Triangles.Length; a += 3)
                        {
                            if (!arm.TriangleBounds[a / 3].Intersects(body.Bounds)) continue;
                            Vector3 p = arm.Vertices[arm.Triangles[a]], q = arm.Vertices[arm.Triangles[a + 1]], r = arm.Vertices[arm.Triangles[a + 2]];
                            for (int b = 0; b < body.Triangles.Length; b += 3)
                            {
                                if (!arm.TriangleBounds[a / 3].Intersects(body.TriangleBounds[b / 3])) continue;
                                Vector3 x = body.Vertices[body.Triangles[b]], y = body.Vertices[body.Triangles[b + 1]], z = body.Vertices[body.Triangles[b + 2]];
                                if (Crosses(p, q, x, y, z) || Crosses(q, r, x, y, z) || Crosses(r, p, x, y, z) ||
                                    Crosses(x, y, p, q, r) || Crosses(y, z, p, q, r) || Crosses(z, x, p, q, r))
                                    return arm.Name + " crosses " + body.Name + " near " + ((p + q + r) / 3f).ToString("F5");
                            }
                        }
                    }
                }
                return null;
            }

            private void Read(Surface surface)
            {
                sample.Clear(false);
                // The imported FBX root carries its unit factor. This matches
                // production rendered-body and foot readback, in world metres.
                surface.Renderer.BakeMesh(sample, true);
                surface.Vertices = sample.vertices;
                for (int index = 0; index < surface.Vertices.Length; index++)
                    surface.Vertices[index] = surface.Renderer.transform.TransformPoint(surface.Vertices[index]);
                if (surface.Triangles == null)
                {
                    surface.Triangles = sample.triangles;
                    surface.TriangleBounds = new Bounds[surface.Triangles.Length / 3];
                    surface.IsClosed = IsClosed(surface);
                }
                surface.Bounds = new Bounds(surface.Vertices[0], Vector3.zero);
                foreach (Vector3 vertex in surface.Vertices) surface.Bounds.Encapsulate(vertex);
                for (int index = 0; index < surface.Triangles.Length; index += 3)
                {
                    Bounds bounds = new Bounds(surface.Vertices[surface.Triangles[index]], Vector3.zero);
                    bounds.Encapsulate(surface.Vertices[surface.Triangles[index + 1]]);
                    bounds.Encapsulate(surface.Vertices[surface.Triangles[index + 2]]);
                    surface.TriangleBounds[index / 3] = bounds;
                }
            }

            private static bool Crosses(Vector3 start, Vector3 end, Vector3 a, Vector3 b, Vector3 c)
            {
                Vector3 line = end - start;
                float length = line.magnitude;
                return length > ContactTolerance * 2f &&
                    RayTriangleDistance(new Ray(start, line / length), a, b, c, out float distance) &&
                    distance > ContactTolerance && distance < length - ContactTolerance;
            }

            private static bool Inside(Vector3 point, Surface surface)
            {
                var ray = new Ray(point, new Vector3(0.173f, 0.469f, 0.866f).normalized);
                int crossings = 0;
                for (int index = 0; index < surface.Triangles.Length; index += 3)
                    if (RayTriangleDistance(ray, surface.Vertices[surface.Triangles[index]],
                        surface.Vertices[surface.Triangles[index + 1]], surface.Vertices[surface.Triangles[index + 2]], out float distance))
                    {
                        if (distance < ContactTolerance) return false;
                        crossings++;
                    }
                return (crossings & 1) != 0;
            }

            private static bool IsClosed(Surface surface)
            {
                // Weld FBX hard-normal/UV duplicates only for topology.
                var welded = new Dictionary<Vector3Int, int>();
                var indices = new int[surface.Vertices.Length];
                for (int index = 0; index < indices.Length; index++)
                {
                    Vector3 vertex = surface.Vertices[index] * 100000f;
                    var key = new Vector3Int(Mathf.RoundToInt(vertex.x), Mathf.RoundToInt(vertex.y), Mathf.RoundToInt(vertex.z));
                    if (!welded.TryGetValue(key, out int unique)) { unique = welded.Count; welded.Add(key, unique); }
                    indices[index] = unique;
                }
                var edges = new Dictionary<(int, int), int>();
                for (int index = 0; index < surface.Triangles.Length; index += 3)
                {
                    int a = indices[surface.Triangles[index]], b = indices[surface.Triangles[index + 1]], c = indices[surface.Triangles[index + 2]];
                    if (a == b || b == c || c == a) continue;
                    Edge(a, b, edges); Edge(b, c, edges); Edge(c, a, edges);
                }
                foreach (int count in edges.Values) if (count != 2) return false;
                return edges.Count > 0;
            }

            private static void Edge(int a, int b, Dictionary<(int, int), int> edges)
            {
                var key = a < b ? (a, b) : (b, a);
                edges.TryGetValue(key, out int count);
                edges[key] = count + 1;
            }

            public void Dispose() => Object.DestroyImmediate(sample);
        }

        private readonly struct RendererSnapshot
        {
            public readonly bool Enabled;
            public readonly Material Material;
            public readonly Color Color;

            public RendererSnapshot(bool enabled, Material material, Color color)
            {
                Enabled = enabled;
                Material = material;
                Color = color;
            }
        }

        private static Dictionary<string, RendererSnapshot> SnapshotRig(Player3DAssetRegistry registry)
        {
            var block = new MaterialPropertyBlock();
            var result = new Dictionary<string, RendererSnapshot>(40);
            foreach (Player3DMeshBinding binding in registry.MeshBindings)
            {
                if (binding?.Renderer == null) continue;
                binding.Renderer.GetPropertyBlock(block);
                result[binding.MeshName] = new RendererSnapshot(
                    binding.Renderer.enabled, binding.Renderer.sharedMaterial, block.GetColor("_BaseColor"));
            }

            Assert.That(result.Count, Is.GreaterThanOrEqualTo(30));
            return result;
        }

        private static void AssertRigRestored(Dictionary<string, RendererSnapshot> before, Player3DAssetRegistry registry)
        {
            var block = new MaterialPropertyBlock();
            int compared = 0;
            foreach (Player3DMeshBinding binding in registry.MeshBindings)
            {
                if (binding?.Renderer == null) continue;
                RendererSnapshot expected = before[binding.MeshName];
                Assert.That(binding.Renderer.enabled, Is.EqualTo(expected.Enabled), binding.MeshName + " enabled flag");
                Assert.That(ReferenceEquals(binding.Renderer.sharedMaterial, expected.Material), Is.True, binding.MeshName + " material");
                binding.Renderer.GetPropertyBlock(block);
                Color tint = block.GetColor("_BaseColor");
                Assert.That(tint.r, Is.EqualTo(expected.Color.r).Within(1e-5f), binding.MeshName + " tint");
                Assert.That(tint.g, Is.EqualTo(expected.Color.g).Within(1e-5f), binding.MeshName + " tint");
                Assert.That(tint.b, Is.EqualTo(expected.Color.b).Within(1e-5f), binding.MeshName + " tint");
                compared++;
            }

            Assert.That(compared, Is.EqualTo(before.Count));
        }

        private static Player3DMeshBinding Find(Player3DAssetRegistry registry, string meshName)
        {
            foreach (Player3DMeshBinding binding in registry.MeshBindings)
            {
                if (binding?.Renderer != null && binding.MeshName == meshName) return binding;
            }

            Assert.Fail("The rig no longer has '" + meshName + "'.");
            return null;
        }

        private static Transform Bone(Player3DAssetRegistry registry, Player3DAnatomicalPart part)
        {
            Assert.That(registry.TryGetPart(part, out Player3DAnatomicalPartBinding binding), Is.True, part.ToString());
            return binding.Bone;
        }

        /// <summary>How far the face points down: the head-to-mouth direction against gravity.</summary>
        private static float FaceDown(Player3DAssetRegistry registry)
        {
            Vector3 direction = registry.Anchors.Mouth.position - registry.Anchors.Head.position;
            return Vector3.Dot(direction.normalized, Vector3.down);
        }

        [UnityTest]
        [PrebuildSetup(typeof(HomeBrushingAssetsSetup))]
        public IEnumerator Brushing_MirrorSceneGatesReliefPerDay()
        {
            yield return LoadHome();
            GameSessionState.UpdateNeeds(0, 30);
            GameSessionState.SetHeroMouthSoiled(true, "brushing_test");
            var brushing = home.TeethBrushing;
            var visual = (Player3DCharacterPresentation)home.Player.Visual;
            HomeBathroomMirrorWorld mirror = home.BathroomMirror;
            Camera camera = home.CameraFollow.GetComponent<Camera>();
            int originalPropCount = mirror.RegisteredPropCount;
            int bodyIntersectionFrames = 0;
            int valveContactFrames = 0;
            float maximumValveError = 0f;
            float maximumValveWristBend = 0f;
            string maximumValveDetail = null;
            float maximumEyeError = 0f;
            float maximumExitFacingError = 0f;
            float maximumExitPositionError = 0f;
            int backwardGaitFrames = 0;
            bool capturedOpening = false, capturedClosing = false;
            string presentationViolation = null;
            float minimumBodyClearance = float.PositiveInfinity;
            int valveCuesBeforeBrushing = home.Soundscape.ValveTurnPlayCount;
            bool openingSoundHeard = false, closingSoundHeard = false, brushingSoundHeard = false, spitSoundHeard = false;
            AudioSource[] brushVoices = home.Audio.GetComponentsInChildren<AudioSource>(true);
            AudioClip brushClip = home.Audio.GetClip(RetroSfxId.TeethBrushScrub);
            AudioSource spitVoice = brushing.SpitEffect.GetComponentInChildren<AudioSource>(true);
            var armProbe = home.gameObject.AddComponent<HomeBathroomPresentationProbe>();
            armProbe.Observe = () =>
            {
                if (brushing.ArmPose == null || brushing.Timeline.Phase == HomeTeethBrushingPhase.Idle) return;
                // The 0.24-second voice can finish while a synchronous PNG is
                // encoded, so observe actual playback before any capture work.
                spitSoundHeard |= brushing.Timeline.Phase == HomeTeethBrushingPhase.Spit &&
                    brushing.SpitEffect.EmittedCount > 0 && spitVoice != null && spitVoice.clip != null &&
                    spitVoice.isPlaying && spitVoice.volume > 0f;
                if (brushing.Timeline.Phase >= HomeTeethBrushingPhase.CameraReturn)
                {
                    maximumExitFacingError = Mathf.Max(maximumExitFacingError, Quaternion.Angle(
                        home.Player.GameObject.transform.rotation, Quaternion.LookRotation(Vector3.forward)));
                    if (brushing.Timeline.Phase == HomeTeethBrushingPhase.CameraReturn)
                    {
                        Vector3 position = home.Player.GameObject.transform.position;
                        maximumExitPositionError = Mathf.Max(maximumExitPositionError,
                            Vector2.Distance(new Vector2(position.x, position.z), new Vector2(2.075f, 2.86f)));
                    }
                    else if (Vector3.Dot(home.Player.Motor.PlanarVelocity, Vector3.forward) < -0.01f &&
                        visual.CurrentLocomotionState == Player3DLocomotionState.WalkBack)
                    {
                        if (backwardGaitFrames++ == 0)
                            CaptureToilet("05a-step-back", "HomeBrushing");
                    }
                }
                minimumBodyClearance = Mathf.Min(minimumBodyClearance,
                    Mathf.Min(brushing.ArmPose.BodyClearance, brushing.ValvePose.BodyClearance));
                if (brushing.ArmPose.BodyIntersectionCount > 0 || brushing.ValvePose.BodyIntersectionCount > 0)
                {
                    if (bodyIntersectionFrames == 0)
                    {
                        CaptureToilet("arm-body-intersection", "HomeBrushing");
                        Debug.Log($"Brushing arm contact: phase={brushing.Timeline.Phase}, right={brushing.ArmPose.BodyIntersectionDetail}, left={brushing.ValvePose.BodyIntersectionDetail}");
                    }
                    bodyIntersectionFrames++;
                }
                if (brushing.ValvePose.Weight > 0.05f)
                {
                    visual.Registry.TryGetPart(Player3DAnatomicalPart.LeftForearm, out var lower);
                    visual.Registry.TryGetPart(Player3DAnatomicalPart.LeftHand, out var wrist);
                    maximumValveWristBend = Mathf.Max(maximumValveWristBend, Vector3.Angle(
                        visual.Registry.Anchors.LeftGrip.position - wrist.Bone.position,
                        wrist.Bone.position - lower.Bone.position));
                }
                if (brushing.Timeline.ValveReach > 0.999f)
                {
                    valveContactFrames++;
                    if (brushing.ValvePose.ContactError > maximumValveError)
                    {
                        maximumValveError = brushing.ValvePose.ContactError;
                        visual.Registry.TryGetPart(Player3DAnatomicalPart.LeftUpperArm, out var upper);
                        visual.Registry.TryGetPart(Player3DAnatomicalPart.LeftHand, out var hand);
                        maximumValveDetail = $"phase={brushing.Timeline.Phase}, time={brushing.Timeline.PhaseElapsed:F3}, valveLean={brushing.ArmPose.ValveLean:F3}, " +
                            $"upper={upper.Bone.position:F4}, wrist={hand.Bone.position:F4}, grip={visual.Registry.Anchors.LeftGrip.position:F4}, " +
                            $"target={brushing.Faucet.GripPosition:F4}, clearance={brushing.ValvePose.BodyClearance:F5}";
                    }
                }
                if (brushing.Timeline.CameraBlend > 0.9999f)
                {
                    maximumEyeError = Mathf.Max(maximumEyeError,
                        Vector3.Distance(camera.transform.position, brushing.FirstPersonView.EyePosition));
                    if (presentationViolation == null && (!brushing.FirstPersonView.IsHeadHidden ||
                        Player3DHeadVisibility.IsHeadDrawn(visual.Registry) ||
                        !Player3DHeadVisibility.IsHeadDrawn(mirror.Twin) || !mirror.IsActive))
                        presentationViolation = "First person must retain the reflected head while hiding only the real head: " + brushing.Timeline.Phase;
                }
                bool turning = brushing.Faucet.OpenAmount > 0.05f && brushing.Faucet.OpenAmount < 0.95f &&
                    brushing.Timeline.ValveReach > 0.999f;
                if (turning && home.Soundscape.BathroomValveSource.isPlaying && home.Soundscape.BathroomValveSource.volume > 0f)
                {
                    openingSoundHeard |= brushing.Timeline.Phase == HomeTeethBrushingPhase.OpenFaucet && home.Soundscape.LastValveOpening;
                    closingSoundHeard |= brushing.Timeline.Phase == HomeTeethBrushingPhase.CloseFaucet && !home.Soundscape.LastValveOpening;
                }
                if (brushing.Timeline.Phase == HomeTeethBrushingPhase.Brushing)
                    foreach (AudioSource source in brushVoices)
                        brushingSoundHeard |= source.clip == brushClip && source.isPlaying && source.volume > 0f;
                if (turning && !capturedOpening && brushing.Timeline.Phase == HomeTeethBrushingPhase.OpenFaucet)
                {
                    capturedOpening = true;
                    CaptureToilet("00a-open-faucet", "HomeBrushing");
                }
                if (turning && !capturedClosing && brushing.Timeline.Phase == HomeTeethBrushingPhase.CloseFaucet)
                {
                    capturedClosing = true;
                    CaptureToilet("05-close-faucet", "HomeBrushing");
                }
            };
            CursorLockMode previousCursor = Cursor.lockState;
            bool previousCursorVisible = Cursor.visible;
            yield return WalkToAndActivate(brushing, new Vector3(2.075f, 0.12f, 2.55f));
            Time.timeScale = 1f;
            yield return WaitUntil(() => brushing.Timeline.Phase == HomeTeethBrushingPhase.Brushing,
                "The brushing scene never reached mouse control.");
            Time.timeScale = 2f;
            yield return new WaitForSeconds(0.4f);
            yield return AtPresentation(() =>
            {
                CaptureToilet("00-brush-contact", "HomeBrushing");
                Assert.That(capturedOpening, Is.True, "The free hand must visibly turn the faucet before manual brushing.");
                Assert.That(openingSoundHeard, Is.True, "The opening hand turn plays its real spatial valve voice.");
                Assert.That(home.Soundscape.ValveTurnPlayCount, Is.EqualTo(valveCuesBeforeBrushing + 1));
                AssertBrushingFaucetReflection(true);
                Assert.That(brushing.Faucet.OpenAmount, Is.EqualTo(1f));
                Assert.That(mirror.RegisteredPropCount, Is.EqualTo(originalPropCount + 3), "Brush, mouth foam and spit each register once.");
                Assert.That(mirror.RegisterReflectedProp(brushing.Toothbrush.transform), Is.SameAs(brushing.ReflectedToothbrush));
                Assert.That(mirror.RegisterReflectedProp(brushing.SpitEffect.transform), Is.SameAs(brushing.ReflectedSpit));
                Assert.That(mirror.RegisteredPropCount, Is.EqualTo(originalPropCount + 3), "Repeated registration cannot duplicate reflected props.");
                AssertBrushingPropReflection(brushing.Toothbrush.transform, brushing.ReflectedToothbrush);
                Assert.That(brushing.ReflectedToothbrush.gameObject.activeInHierarchy, Is.True);
                Assert.That(brushing.ArmPose.ArmRadii.x, Is.InRange(0.04f, 0.09f), "FBX readback must retain metre-scale arm geometry.");
                Assert.That(brushing.ArmPose.ArmRadii.y, Is.InRange(0.035f, 0.08f));
                Assert.That(brushing.ArmPose.ArmRadii.z, Is.InRange(0.025f, 0.10f));
                Assert.That(brushing.Progress.Amount, Is.Zero, "An idle mouse cannot clean the teeth.");
                Assert.That(brushing.ArmPose.ContactError, Is.LessThan(0.012f),
                    $"The actual bristles must reach the teeth. Arm radii={brushing.ArmPose.ArmRadii:F4}; clearances={brushing.ArmPose.ArmClearances:F4}.");
                Assert.That(brushing.Toothbrush.activeSelf, Is.True);
                Assert.That(brushing.GaugeVisible, Is.True);
                Assert.That(visual.CurrentFacialExpression, Is.EqualTo(PlayerFacialExpression.TeethDisplay));
                Assert.That(visual.HasContextualFacialExpression, Is.True);
                Assert.That(home.Player.Motor.InputEnabled, Is.False);
            });
            brushing.ApplyBrushDelta(Vector2.left * 300f);
            brushing.ApplyBrushDelta(Vector2.left * 300f);
            float reflectedLeftX = 0f;
            yield return AtPresentation(() => reflectedLeftX = camera.WorldToViewportPoint(
                brushing.ReflectedToothbrush.Find("Brush Tip").position).x);
            brushing.ApplyBrushDelta(Vector2.right * 300f);
            brushing.ApplyBrushDelta(Vector2.right * 300f);
            yield return AtPresentation(() =>
            {
                float reflectedRightX = camera.WorldToViewportPoint(brushing.ReflectedToothbrush.Find("Brush Tip").position).x;
                Assert.That(reflectedRightX - reflectedLeftX, Is.GreaterThan(0.005f), "Mouse-right must move the reflected bristles right on screen.");
                AssertBrushingPropReflection(brushing.Toothbrush.transform, brushing.ReflectedToothbrush);
            });
            foreach (Vector2 corner in new[] { new Vector2(-1f, -1f), new Vector2(1f, 1f),
                new Vector2(-1f, 1f), new Vector2(1f, -1f) })
            {
                brushing.ApplyBrushDelta(corner * 300f);
                brushing.ApplyBrushDelta(corner * 300f);
                yield return AtPresentation(() =>
                {
                    Assert.That(brushing.ArmPose.ContactError, Is.LessThan(0.012f), "Every permitted mouse corner must remain reachable outside the body.");
                    // The continuous probe reports any bad corner together
                    // with entry, lowering and spit after the complete cycle.
                });
            }
            yield return BrushToCompletion(true);
            Time.timeScale = 1f;
            yield return new WaitForSeconds(HomeTeethBrushingTimeline.ArmLowerSeconds + 0.05f);
            yield return AtPresentation(() =>
            {
                CaptureToilet("02-clean-teeth", "HomeBrushing");
                Assert.That(brushing.Timeline.Phase, Is.EqualTo(HomeTeethBrushingPhase.ShowTeeth));
                Assert.That(brushing.Progress.Amount, Is.EqualTo(1f));
                Assert.That(brushing.Toothbrush.activeSelf, Is.False, "The clean teeth must be unobstructed.");
                Assert.That(brushing.ReflectedToothbrush.gameObject.activeInHierarchy, Is.False);
                Assert.That(brushing.ReflectedFoam.gameObject.activeInHierarchy, Is.False);
                AssertBrushingFaucetReflection(true);
                Assert.That(brushing.SpitEffect.EmittedCount, Is.Zero, "Show the teeth before spitting.");
                Assert.That(brushingSoundHeard, Is.True, "Actual moving brush contact must play the scrub clip through an AudioSource.");
                Assert.That(visual.IsMouthSoiledVisible, Is.False, "The finishing shot must show the clean teeth atlas cell.");
                Assert.That(GameSessionState.HeroMouthSoiled, Is.True, "State commits only after the whole action.");
            });
            yield return WaitUntil(() => brushing.SpitEffect.EmittedCount > 0, "No visible foam left the mouth.");
            yield return AtPresentation(() =>
            {
                CaptureToilet("03-spit-flight", "HomeBrushing");
                Assert.That(brushing.ArmPose.Bend, Is.GreaterThan(0.9f));
                Assert.That(visual.CurrentFacialExpression, Is.EqualTo(PlayerFacialExpression.Spit));
                Assert.That(spitVoice, Is.Not.Null);
                Assert.That(spitVoice.clip, Is.Not.Null);
                Assert.That(spitSoundHeard, Is.True,
                    "The real spit AudioSource must play during emission, before synchronous screenshot work can finish the short clip.");
                Assert.That(spitVoice.volume, Is.GreaterThan(0f));
                Assert.That(Vector3.Distance(brushing.SpitEffect.LastMouth, visual.Registry.Anchors.Mouth.position), Is.LessThan(0.03f));
                AssertBrushingFaucetReflection(true);
                AssertBrushingPropReflection(brushing.SpitEffect.transform, brushing.ReflectedSpit);
            });
            yield return WaitUntil(() => brushing.SpitEffect.BasinHitCount > 0, "The mouth-origin foam missed the real sink mesh.");
            yield return AtPresentation(() => CaptureToilet("04-sink-impact", "HomeBrushing"));
            Assert.That(home.transform.InverseTransformPoint(brushing.SpitEffect.LastImpact).y, Is.LessThan(0.84f),
                "The foam must enter the cavity below the rim.");
            yield return WaitUntil(() => home.Player.Motor.InputEnabled, "The brushing scene never restored the player.");
            yield return AtPresentation(() =>
            {
                CaptureToilet("06-restored", "HomeBrushing");
                Assert.That(capturedClosing, Is.True, "The hand must turn the running faucet back after spitting.");
                Assert.That(closingSoundHeard, Is.True);
                Assert.That(home.Soundscape.ValveTurnPlayCount, Is.EqualTo(valveCuesBeforeBrushing + 2));
                Assert.That(home.Soundscape.BathroomValveSource.isPlaying, Is.False);
                Assert.That(spitVoice.isPlaying, Is.False);
                AssertBrushingRestored();
            });
            Assert.That(brushing.Toothbrush.activeSelf, Is.False);
            Assert.That(brushing.GaugeVisible, Is.False);
            Assert.That(visual.HasContextualFacialExpression, Is.False);
            Assert.That(visual.InteractionHandoffLocked, Is.False);
            Assert.That(GameSessionState.HeroMouthSoiled, Is.False);
            Assert.That(Cursor.lockState, Is.EqualTo(previousCursor));
            Assert.That(Cursor.visible, Is.EqualTo(previousCursorVisible));
            int stressAfterFirst = GameSessionState.StressLevel;
            Assert.That(stressAfterFirst, Is.EqualTo(30 - HomeTeethBrushingInteraction.StressRelief));

            // Daily relief is gated; a second complete brushing still cleans the mouth.
            GameSessionState.SetHeroMouthSoiled(true, "brushing_test_repeat");
            yield return WalkToAndActivate(brushing, new Vector3(2.075f, 0.12f, 2.55f));
            Time.timeScale = 3f;
            yield return WaitUntil(() => brushing.Timeline.Phase == HomeTeethBrushingPhase.Brushing, "No second brushing.");
            Assert.That(mirror.RegisteredPropCount, Is.EqualTo(originalPropCount + 3), "A repeated action reuses its reflected pools.");
            yield return BrushToCompletion(false);
            yield return WaitUntil(() => home.Player.Motor.InputEnabled, "The second brushing never restored the player.");
            Time.timeScale = 1f;
            Assert.That(GameSessionState.StressLevel, Is.EqualTo(stressAfterFirst));
            Assert.That(GameSessionState.HeroMouthSoiled, Is.False);
            yield return AtPresentation(AssertBrushingRestored);

            GameSessionState.SetHeroMouthSoiled(true, "brushing_test_cancel");
            yield return WalkToAndActivate(brushing, new Vector3(2.075f, 0.12f, 2.55f));
            Time.timeScale = 3f;
            yield return WaitUntil(() => brushing.Timeline.Phase == HomeTeethBrushingPhase.Brushing, "No cancellable brushing.");
            brushing.ApplyBrushDelta(new Vector2(80f, 0f));
            yield return null;
            brushing.RequestStop();
            Assert.That(brushing.Timeline.Phase, Is.EqualTo(HomeTeethBrushingPhase.CloseFaucet), "A manual stop closes the running faucet visibly.");
            Assert.That(home.Player.Motor.InputEnabled, Is.False, "Control returns only after closure and camera exit.");
            yield return WaitUntil(() => home.Player.Motor.InputEnabled, "Cancel did not restore control.");
            yield return AtPresentation(AssertBrushingRestored);
            Assert.That(GameSessionState.HeroMouthSoiled, Is.True, "A cancelled brush must not clean the mouth.");
            Assert.That(GameSessionState.StressLevel, Is.EqualTo(stressAfterFirst));
            Assert.That(visual.HasContextualFacialExpression, Is.False);
            Assert.That(visual.InteractionHandoffLocked, Is.False);
            Assert.That(brushing.SpitEffect.EmittedCount, Is.Zero, "Cancellation skips the completion spit.");
            // Disable uses the immediate owned cleanup path, unlike a visible manual stop.
            yield return WalkToAndActivate(brushing, new Vector3(2.075f, 0.12f, 2.55f));
            yield return WaitUntil(() => brushing.Timeline.Phase == HomeTeethBrushingPhase.Brushing, "No brushing for disable cleanup.");
            brushing.enabled = false;
            Assert.That(brushing.Faucet.OpenAmount, Is.Zero, "Disable closes the water synchronously.");
            Assert.That(brushing.Faucet.WaterSource.isPlaying, Is.False);
            Assert.That(home.Soundscape.BathroomValveSource.isPlaying, Is.False);
            Assert.That(spitVoice.isPlaying, Is.False);
            Assert.That(Player3DHeadVisibility.IsHeadDrawn(visual.Registry), Is.True);
            yield return AtPresentation(() => AssertBrushingRestored(true));
            brushing.enabled = true;
            Assert.That(GameSessionState.HeroMouthSoiled, Is.True);
            Assert.That(GameSessionState.StressLevel, Is.EqualTo(stressAfterFirst));
            Assert.That(bodyIntersectionFrames, Is.Zero, "Both actual arms stay outside the torso through entry, valve turns, strokes, lowering and spit.");
            Assert.That(minimumBodyClearance, Is.GreaterThanOrEqualTo(-0.0005f));
            Assert.That(valveContactFrames, Is.GreaterThan(0));
            Assert.That(maximumValveError, Is.LessThan(0.025f), "The left grip must stay on the real rotating valve. " + maximumValveDetail);
            Assert.That(maximumValveWristBend, Is.LessThan(28f), "The wrist must stay aligned with the forearm through reach, turn and withdrawal.");
            Assert.That(maximumEyeError, Is.LessThan(0.005f), "The lens remains at the posed eyes, including spitting and valve turns.");
            Assert.That(maximumExitFacingError, Is.LessThan(1f), "Camera return and pose handoff must not turn the hero away from the sink.");
            Assert.That(maximumExitPositionError, Is.LessThan(0.005f), "The hero remains at the sink until the camera has returned.");
            Assert.That(backwardGaitFrames, Is.GreaterThan(0), "The exit uses the existing WalkBack gait while travelling away from the sink.");
            Assert.That(presentationViolation, Is.Null);
            armProbe.Observe = null;
        }

        private IEnumerator BrushToCompletion(bool capture)
        {
            float deadline = Time.realtimeSinceStartup + TimeoutSeconds;
            float started = Time.time;
            int stroke = 0;
            bool captured = false;
            var brushing = home.TeethBrushing;
            float remainingSeconds = (1f - brushing.Progress.Amount) *
                HomeTeethBrushingProgress.RequiredDistance / HomeTeethBrushingProgress.MaximumCreditSpeed;
            while (brushing.Timeline.Phase == HomeTeethBrushingPhase.Brushing && Time.realtimeSinceStartup < deadline)
            {
                brushing.ApplyBrushDelta(new Vector2((stroke++ % 2 == 0 ? 1f : -1f) * 160f, 0f));
                if (capture && !captured && brushing.Progress.Amount >= 0.5f)
                {
                    captured = true;
                    yield return AtPresentation(() =>
                    {
                        CaptureToilet("01-brushing", "HomeBrushing");
                        var visual = (Player3DCharacterPresentation)home.Player.Visual;
                        Transform foam = visual.Registry.Anchors.Mouth.Find("Player Brushing Foam");
                        Assert.That(foam, Is.Not.Null);
                        Assert.That(brushing.ReflectedFoam.gameObject.activeInHierarchy, Is.True, "Mouth foam must be visible in the mirror during cleaning.");
                        AssertBrushingPropReflection(foam, brushing.ReflectedFoam);
                        AssertBrushingPropReflection(brushing.Toothbrush.transform, brushing.ReflectedToothbrush);
                    });
                }
                else yield return null;
            }
            Assert.That(brushing.Timeline.Phase, Is.EqualTo(HomeTeethBrushingPhase.ShowTeeth),
                $"Mouse strokes did not finish brushing: {brushing.Progress.Amount:F3}, tip error {brushing.ArmPose.ContactError:F4}, radii {brushing.ArmPose.ArmRadii:F4}, clearances {brushing.ArmPose.ArmClearances:F4}, {brushing.ArmPose.BodyIntersectionDetail}.");
            // The first input is consumed by the current LateUpdate, whose
            // delta has already advanced Time.time before the coroutine starts.
            Assert.That(Time.time - started + Time.maximumDeltaTime, Is.GreaterThanOrEqualTo(remainingSeconds - 0.01f),
                "Large mouse spikes cannot finish instantly.");
        }

        private void AssertBrushingPropReflection(Transform source, Transform reflected, bool compareMaterials = true)
        {
            Assert.That(reflected, Is.Not.Null, source.name);
            MeshRenderer[] originals = source.GetComponentsInChildren<MeshRenderer>(true);
            MeshRenderer[] copies = reflected.GetComponentsInChildren<MeshRenderer>(true);
            Assert.That(copies.Length, Is.EqualTo(originals.Length), source.name);
            for (int index = 0; index < originals.Length; index++)
            {
                MeshRenderer original = originals[index], copy = copies[index];
                Assert.That(copy.enabled, Is.EqualTo(original.enabled), original.name);
                Assert.That(copy.gameObject.activeInHierarchy, Is.EqualTo(original.gameObject.activeInHierarchy), original.name);
                if (compareMaterials) Assert.That(copy.sharedMaterial, Is.SameAs(original.sharedMaterial), original.name);
                foreach (Vector3 point in new[] { Vector3.zero, new Vector3(0.03f, 0.05f, 0.01f) })
                {
                    Vector3 originalPoint = home.transform.InverseTransformPoint(original.transform.TransformPoint(point));
                    Vector3 copyPoint = home.transform.InverseTransformPoint(copy.transform.TransformPoint(point));
                    Assert.That(Vector3.Distance(copyPoint, HomeBathroomMirrorPlane.Reflect(originalPoint)),
                        Is.LessThan(0.001f), original.name + " must follow its real pose and scale through the mirror.");
                }
            }
            Assert.That(reflected.GetComponentsInChildren<AudioSource>(true), Is.Empty);
            Assert.That(reflected.GetComponentsInChildren<Collider>(true), Is.Empty);
            Assert.That(reflected.GetComponentsInChildren<MonoBehaviour>(true), Is.Empty);
        }

        private void AssertBrushingFaucetReflection(bool running)
        {
            HomeSinkFaucet faucet = home.TeethBrushing.Faucet;
            Transform reflected = home.BathroomMirror.RegisterReflectedProp(faucet.transform);
            Assert.That(faucet.OpenAmount, Is.EqualTo(running ? 1f : 0f).Within(0.0001f));
            Transform handle = reflected.Find(faucet.Handle.name);
            Assert.That(handle, Is.Not.Null);
            Assert.That(Quaternion.Angle(handle.localRotation, faucet.Handle.localRotation), Is.LessThan(0.01f), "The reflected valve turns with the physical handle.");
            MeshRenderer sourceWater = faucet.transform.Find("Sink Tap Water Stream 0").GetComponent<MeshRenderer>();
            MeshRenderer mirrorWater = reflected.Find("Sink Tap Water Stream 0").GetComponent<MeshRenderer>();
            Assert.That(sourceWater.enabled, Is.EqualTo(running));
            Assert.That(mirrorWater.enabled, Is.EqualTo(running), "Water starts and stops in the mirror with the source.");
            // The room's occlusion controller wraps source fixture materials
            // after mirror construction. Static reflections retain the opaque
            // authored material; they must not inherit camera-side dithering.
            AssertBrushingPropReflection(faucet.transform, reflected, false);
            if (running)
            {
                Assert.That(faucet.WaterSource.clip, Is.Not.Null);
                Assert.That(faucet.WaterSource.volume, Is.GreaterThan(0f));
            }
            else Assert.That(faucet.WaterSource.isPlaying, Is.False);
        }

        private void AssertBrushingRestored() => AssertBrushingRestored(false);

        private void AssertBrushingRestored(bool atSink)
        {
            var brushing = home.TeethBrushing;
            var visual = (Player3DCharacterPresentation)home.Player.Visual;
            AssertBrushingFaucetReflection(false);
            Assert.That(home.Player.Motor.InputEnabled, Is.True);
            Transform hero = home.Player.GameObject.transform;
            Assert.That(Quaternion.Angle(hero.rotation, Quaternion.LookRotation(Vector3.forward)), Is.LessThan(1f),
                "Returning control preserves the hero's facing at the sink.");
            Assert.That(Vector2.Distance(new Vector2(hero.position.x, hero.position.z), new Vector2(2.075f, atSink ? 2.86f : 2.50f)),
                Is.LessThan(0.005f), "A normal finish/cancel steps back; disable restores immediately at the sink.");
            Assert.That(brushing.FirstPersonView.IsHeadHidden, Is.False);
            Assert.That(Player3DHeadVisibility.IsHeadDrawn(visual.Registry), Is.True);
            Assert.That(brushing.ReflectedToothbrush.gameObject.activeInHierarchy, Is.False);
            Assert.That(brushing.ReflectedFoam.gameObject.activeInHierarchy, Is.False);
            foreach (MeshRenderer renderer in brushing.ReflectedSpit.GetComponentsInChildren<MeshRenderer>(true))
                Assert.That(renderer.enabled, Is.False, "Finished and cancelled brushing leave no reflected foam flight.");
        }

        private IEnumerator LoadHome()
        {
            AsyncOperation load = SceneManager.LoadSceneAsync(
                SceneIds.HomeInterior,
                LoadSceneMode.Single);
            while (load != null && !load.isDone)
            {
                yield return null;
            }

            float deadline =
                Time.realtimeSinceStartup + TimeoutSeconds;
            while (Time.realtimeSinceStartup < deadline)
            {
                home = Object.FindAnyObjectByType<HomeInteriorRoot>();
                if (home != null && home.IsInitialized)
                {
                    yield break;
                }

                yield return null;
            }

            Assert.Fail("HomeInterior never finished initializing.");
        }

        private IEnumerator WalkToAndActivate(
            HomeBathroomSceneInteraction scene,
            Vector3 approachPosition)
        {
            home.Player.Motor.Teleport(approachPosition);
            float deadline =
                Time.realtimeSinceStartup + TimeoutSeconds;
            while (Time.realtimeSinceStartup < deadline)
            {
                if (ReferenceEquals(
                        home.Player.Interactor.ActiveInteractable,
                        scene))
                {
                    scene.Interact(home.Player.Interactor);
                    yield break;
                }

                yield return null;
            }

            Assert.Fail(
                $"{scene.GetType().Name} was never discovered by " +
                "the interactor.");
        }

        private static IEnumerator WaitUntil(
            System.Func<bool> condition,
            string failureMessage)
        {
            float deadline =
                Time.realtimeSinceStartup + TimeoutSeconds;
            while (Time.realtimeSinceStartup < deadline)
            {
                if (condition())
                {
                    yield break;
                }

                yield return null;
            }

            Assert.Fail(failureMessage);
        }
    }

    public sealed class HomeBrushingAssetsSetup : IPrebuildSetup
    {
        public void Setup()
        {
#if UNITY_EDITOR
            System.Type setup = System.Type.GetType("BarPromenade.Editor.Player3DV2AssetSetup, BarPromenade.Editor", true);
            setup.GetMethod("BuildOrThrow", System.Type.EmptyTypes).Invoke(null, null);
#endif
        }
    }

    [DefaultExecutionOrder(20000)]
    public sealed class HomeBathroomPresentationProbe : MonoBehaviour
    {
        public System.Action Sample;
        public System.Action Observe;
        private void LateUpdate()
        {
            Observe?.Invoke();
            System.Action pending = Sample;
            Sample = null;
            pending?.Invoke();
        }
    }

    /// <summary>
    /// Samples an invariant after every presentation frame and keeps the
    /// first violation, so a rule like "never undressed in shot" is held
    /// on every frame of the scene rather than at two chosen moments.
    /// </summary>
    [DefaultExecutionOrder(20000)]
    public sealed class HomeShowerInvariantProbe : MonoBehaviour
    {
        public System.Func<string> Check;
        public string Violation { get; private set; }

        private void LateUpdate()
        {
            if (Check == null)
            {
                return;
            }

            string violation = Check();
            if (violation != null && Violation == null)
            {
                Violation = violation + " at t=" + Time.time.ToString("F2");
                Debug.Log("First shower presentation violation: " + Violation);
            }
        }
    }
}

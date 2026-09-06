using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace BarPromenade.Tests.PlayMode
{
    public sealed class HomeBathroomInteractionsPlayModeTests
    {
        private const float TimeoutSeconds = 30f;
        private const float FastTimeScale = 6f;

        private HomeInteriorRoot home;

        [UnitySetUp]
        public IEnumerator SetUp()
        {
            Time.timeScale = 1f;
            GameSessionState.BeginNewGame();
            GameSessionState.EnterHome();
            yield return null;
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
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
                catch (System.Exception exception) { failure = exception; }
                finally { completed = true; }
            };
            while (!completed) yield return null;
            if (failure != null) System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(failure).Throw();
        }

        private void CaptureToilet(string shot, string interaction = "HomeToilet")
        {
            Camera camera = home.CameraFollow.GetComponent<Camera>();
            string folder = System.IO.Path.Combine(System.IO.Directory.GetCurrentDirectory(), "Captures", interaction);
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
        public IEnumerator Shower_DrawsCurtainRunsWaterAndRestores()
        {
            yield return LoadHome();
            GameSessionState.UpdateNeeds(0, 50);
            Transform curtain =
                home.Room.Find("Home Bathroom Shower Curtain");
            Assert.That(curtain, Is.Not.Null);

            yield return WalkToAndActivate(
                home.ShowerScene,
                new Vector3(3.30f, 0.12f, 2.35f));

            Time.timeScale = FastTimeScale;
            yield return WaitUntil(
                () => home.ShowerScene.Timeline.Phase ==
                      HomeShowerScenePhase.Hold,
                "The shower never reached its running hold.");
            Assert.That(
                curtain.localScale.x,
                Is.EqualTo(1f).Within(0.01f),
                "The curtain must be fully drawn while the water " +
                "runs.");
            Assert.That(
                home.Soundscape.ShowerWaterAmount,
                Is.EqualTo(1f).Within(0.01f));
            Assert.That(home.ShowerScene.WaterEffect.IsEmitting,
                Is.True);

            yield return WaitUntil(
                () => home.Player.Motor.InputEnabled,
                "The shower scene never restored the player.");
            Time.timeScale = 1f;
            Assert.That(
                curtain.localScale.x,
                Is.EqualTo(
                    HomeShowerSceneTimeline.GatheredCurtainScale)
                    .Within(0.01f),
                "The curtain must gather back after the scene.");
            Assert.That(
                home.Soundscape.ShowerWaterAmount,
                Is.Zero.Within(0.001f));
            Assert.That(
                GameSessionState.StressLevel,
                Is.EqualTo(50 - HomeShowerInteraction.StressRelief));
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
            int bodyIntersectionFrames = 0;
            float minimumBodyClearance = float.PositiveInfinity;
            var armProbe = home.gameObject.AddComponent<HomeBathroomPresentationProbe>();
            armProbe.Observe = () =>
            {
                if (brushing.ArmPose == null || brushing.Timeline.Phase == HomeTeethBrushingPhase.Idle) return;
                minimumBodyClearance = Mathf.Min(minimumBodyClearance, brushing.ArmPose.BodyClearance);
                if (brushing.ArmPose.BodyIntersectionCount > 0)
                {
                    if (bodyIntersectionFrames == 0)
                    {
                        CaptureToilet("arm-body-intersection", "HomeBrushing");
                        Debug.Log($"Brushing arm contact: phase={brushing.Timeline.Phase}, radii={brushing.ArmPose.ArmRadii:F4}, clearances={brushing.ArmPose.ArmClearances:F4}, detail={brushing.ArmPose.BodyIntersectionDetail}");
                    }
                    bodyIntersectionFrames++;
                }
            };
            CursorLockMode previousCursor = Cursor.lockState;
            bool previousCursorVisible = Cursor.visible;
            yield return WalkToAndActivate(brushing, new Vector3(2.075f, 0.12f, 2.55f));
            Time.timeScale = 2f;
            yield return WaitUntil(() => brushing.Timeline.Phase == HomeTeethBrushingPhase.Brushing,
                "The brushing scene never reached mouse control.");
            yield return new WaitForSeconds(0.4f);
            yield return AtPresentation(() =>
            {
                CaptureToilet("00-brush-contact", "HomeBrushing");
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
                Assert.That(brushing.SpitEffect.EmittedCount, Is.Zero, "Show the teeth before spitting.");
                Assert.That(visual.IsMouthSoiledVisible, Is.False, "The finishing shot must show the clean teeth atlas cell.");
                Assert.That(GameSessionState.HeroMouthSoiled, Is.True, "State commits only after the whole action.");
            });
            yield return WaitUntil(() => brushing.SpitEffect.EmittedCount > 0, "No visible foam left the mouth.");
            yield return AtPresentation(() =>
            {
                CaptureToilet("03-spit-flight", "HomeBrushing");
                Assert.That(brushing.ArmPose.Bend, Is.GreaterThan(0.9f));
                Assert.That(visual.CurrentFacialExpression, Is.EqualTo(PlayerFacialExpression.Spit));
                Assert.That(Vector3.Distance(brushing.SpitEffect.LastMouth, visual.Registry.Anchors.Mouth.position), Is.LessThan(0.03f));
            });
            yield return WaitUntil(() => brushing.SpitEffect.BasinHitCount > 0, "The mouth-origin foam missed the real sink mesh.");
            yield return AtPresentation(() => CaptureToilet("04-sink-impact", "HomeBrushing"));
            Assert.That(home.transform.InverseTransformPoint(brushing.SpitEffect.LastImpact).y, Is.LessThan(0.84f),
                "The foam must enter the cavity below the rim.");
            yield return WaitUntil(() => home.Player.Motor.InputEnabled, "The brushing scene never restored the player.");
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
            yield return BrushToCompletion(false);
            yield return WaitUntil(() => home.Player.Motor.InputEnabled, "The second brushing never restored the player.");
            Time.timeScale = 1f;
            Assert.That(GameSessionState.StressLevel, Is.EqualTo(stressAfterFirst));
            Assert.That(GameSessionState.HeroMouthSoiled, Is.False);

            GameSessionState.SetHeroMouthSoiled(true, "brushing_test_cancel");
            yield return WalkToAndActivate(brushing, new Vector3(2.075f, 0.12f, 2.55f));
            Time.timeScale = 3f;
            yield return WaitUntil(() => brushing.Timeline.Phase == HomeTeethBrushingPhase.Brushing, "No cancellable brushing.");
            brushing.ApplyBrushDelta(new Vector2(80f, 0f));
            yield return null;
            brushing.RequestStop();
            yield return WaitUntil(() => home.Player.Motor.InputEnabled, "Cancel did not restore control.");
            Assert.That(GameSessionState.HeroMouthSoiled, Is.True, "A cancelled brush must not clean the mouth.");
            Assert.That(GameSessionState.StressLevel, Is.EqualTo(stressAfterFirst));
            Assert.That(visual.HasContextualFacialExpression, Is.False);
            Assert.That(visual.InteractionHandoffLocked, Is.False);
            Assert.That(brushing.SpitEffect.EmittedCount, Is.Zero, "Cancellation skips the completion spit.");
            Assert.That(bodyIntersectionFrames, Is.Zero, "The actual arm must stay outside the torso throughout entry, strokes, lowering and spit.");
            Assert.That(minimumBodyClearance, Is.GreaterThanOrEqualTo(-0.0005f));
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
                    yield return AtPresentation(() => CaptureToilet("01-brushing", "HomeBrushing"));
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
}

using System.Collections;
using System.Collections.Generic;
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
                catch (System.Exception exception) { failure = exception; }
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

        /// <summary>A frame from a throwaway lens, for looking at what the hero's own eyes cannot.</summary>
        private void CaptureWitness(string shot, Vector3 position, Vector3 lookAt, float fieldOfView)
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
                CaptureFrame("HomeShower", shot, camera);
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
            Assert.That(curtain.localScale.x, Is.EqualTo(HomeShowerInteraction.GatheredCurtainScale).Within(0.001f));
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
            Assert.That(Vector3.Distance(hotHandle.localPosition + Vector3.up * 0.025f,
                HomeShowerFraming.HotHandleGrip), Is.LessThan(0.001f));
            Transform nozzle = home.Room.Find("Home Bathroom Shower Head Face");
            Assert.That(Vector3.Distance(nozzle.localPosition - Vector3.up * 0.015f,
                HomeShowerFraming.DripOrigin), Is.LessThan(0.001f));
            Assert.That(home.Layout.TryGetFurniture(HomeFurnitureKind.Shower, out HomeFurnitureFootprint showerFootprint), Is.True);
            Assert.That(showerFootprint.Bounds.Contains(new Vector2(HomeShowerFraming.BasinLanding.x,
                HomeShowerFraming.BasinLanding.z)), Is.True, "The relocated nozzle still drips into the tray.");
            Texture2D atlas = Player3DBathingAppearance.BareSkinAtlas;
            var invariants = home.gameObject.AddComponent<HomeShowerInvariantProbe>();
            invariants.Check = () =>
            {
                if (shower.IsUndressed && shower.Timeline.CameraBlend < 0.999f)
                    return "undressed while the camera was still travelling (blend " + shower.Timeline.CameraBlend + ")";
                if (shower.IsUndressed && !shower.View.IsHeadHidden)
                    return "undressed with the lens outside his head";
                if (shower.Timeline.Phase == HomeShowerScenePhase.Wash && !shower.IsUndressed)
                    return "washing with his clothes on";
                return null;
            };
            float restFaceDown = FaceDown(registry);

            yield return WalkToAndActivate(shower, new Vector3(3.30f, 0.12f, 2.35f));
            yield return null;
            yield return null;
            Assert.That(shower.Timeline.Phase, Is.EqualTo(HomeShowerScenePhase.CameraIn));
            Assert.That(shower.Timeline.CameraBlend, Is.GreaterThan(0f), "The camera flies before the hero has arrived anywhere.");
            Assert.That(shower.View.IsActive, Is.True);
            Assert.That(home.Player.Motor.InputEnabled, Is.False);
            Assert.That(shower.IsUndressed, Is.False);

            Time.timeScale = FastTimeScale;
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
                CaptureShower("01-wash");
                Vector3 mixerView = camera.WorldToViewportPoint(home.Room.TransformPoint(HomeShowerFraming.Mixer));
                Assert.That(mixerView.z, Is.GreaterThan(0f), "The mixer must be in front of the lens.");
                Assert.That(mixerView.x, Is.InRange(0.35f, 0.65f), "The mixer stays in the centre of the first-person view.");
                Assert.That(mixerView.y, Is.InRange(0.05f, 0.95f), "The mixer is visible while washing.");
                Assert.That(shower.WashPose.LeftPalmError, Is.LessThan(0.04f), "Left palm on the tile.");
                Assert.That(shower.WashPose.RightPalmError, Is.LessThan(0.04f), "Right palm on the tile.");
                Vector3 leftHand = Bone(registry, Player3DAnatomicalPart.LeftHand).position;
                Vector3 rightHand = Bone(registry, Player3DAnatomicalPart.RightHand).position;
                Assert.That(leftHand.z, Is.GreaterThan(3.60f), "The left hand reaches the back tile.");
                Assert.That(rightHand.z, Is.GreaterThan(3.60f), "The right hand reaches the back tile.");
                Assert.That(Vector3.Distance(leftHand, rightHand), Is.InRange(0.30f, 0.60f));
                Assert.That(FaceDown(registry) - restFaceDown, Is.GreaterThan(0.15f), "The head hangs under the water.");
                Assert.That(
                    Vector3.Distance(camera.transform.position, registry.Anchors.Mouth.position + Vector3.up * HomeShowerFirstPersonView.EyeHeightAboveMouth),
                    Is.LessThan(0.08f),
                    "The lens hangs with the head (plus the breathing drift).");
                Assert.That(Vector3.Dot(camera.transform.forward, Vector3.down), Is.GreaterThan(0.4f), "The eyes look down at the tray.");
                Assert.That(Vector3.Dot(camera.transform.forward, hero.forward), Is.GreaterThan(0.3f), "...and forward, at the tile.");
                Assert.That(home.Soundscape.ShowerWaterAmount, Is.EqualTo(1f).Within(0.01f));
                Assert.That(shower.WaterEffect.IsEmitting, Is.True);
                Assert.That(shower.WaterEffect.StreamParticleCount, Is.GreaterThan(5), "Water is actually falling, not just flagged.");
                Assert.That(home.Player.Visual.InteractionHandoffLocked, Is.True);
                Assert.That(home.PlayerOcclusion.enabled, Is.False);
                Assert.That(curtain.localScale.x, Is.EqualTo(HomeShowerInteraction.GatheredCurtainScale).Within(0.001f), "The curtain never moves.");
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
                    Is.GreaterThan(0.85f),
                    "At rest it hangs, it does not aim.");
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
            shower.View.ApplyLookDelta(0f, -35f);
            shower.View.ApplyLookDelta(500f, 0f);
            Assert.That(shower.View.LookYawDegrees, Is.EqualTo(HomeShowerFirstPersonView.MaximumLookYawDegrees).Within(0.01f), "The cone clamps.");
            shower.View.ApplyLookDelta(-500f, 0f);
            shower.View.ApplyLookDelta(HomeShowerFirstPersonView.MaximumLookYawDegrees, 0f);
            Assert.That(shower.View.LookYawDegrees, Is.Zero.Within(0.01f));

            // The reward needs the minimum wash; stop only once it is earned.
            yield return WaitUntil(() => shower.Timeline.ReachedMinimumWash, "The wash never reached its minimum.");
            Assert.That(shower.Timeline.Phase, Is.EqualTo(HomeShowerScenePhase.Wash));
            shower.RequestStop();
            Assert.That(shower.Timeline.Phase, Is.EqualTo(HomeShowerScenePhase.WaterOff), "E closes the tap.");
            Time.timeScale = 1f;
            yield return WaitUntil(() => shower.Timeline.ValveReach >= 0.99f, "The hand never reached the tap.");
            yield return AtPresentation(() =>
            {
                Assert.That(shower.WashPose.RightPalmError, Is.LessThan(0.04f), "The right hand reaches the relocated hot handle.");
                CaptureShower("06-close-front-tap");
            });
            Time.timeScale = FastTimeScale;
            yield return WaitUntil(() => shower.Timeline.Phase >= HomeShowerScenePhase.Straighten, "The tap never closed.");
            Assert.That(shower.HotHandleTurn, Is.EqualTo(1f).Within(0.01f));
            Assert.That(home.Soundscape.ShowerWaterAmount, Is.Zero.Within(0.001f));

            yield return WaitUntil(() => shower.Timeline.Phase == HomeShowerScenePhase.DripHold, "He never straightened for the drips.");
            Vector3 heldPosition = default;
            Quaternion heldRotation = default;
            Vector3 heldRoot = default;
            yield return AtPresentation(() =>
            {
                heldPosition = camera.transform.position;
                heldRotation = camera.transform.rotation;
                heldRoot = hero.position;
                CaptureShower("03-drip");
                Assert.That(Vector3.Dot(camera.transform.forward, Vector3.down), Is.GreaterThan(0.5f), "He looks down at the tray while the tap drips.");
                Assert.That(shower.IsUndressed, Is.True, "Still bare while the lens is in his head.");
                Assert.That(shower.View.IsHeadHidden, Is.True);
                Assert.That(shower.HoldsHandoff, Is.False);
                Assert.That(home.Player.Motor.InputEnabled, Is.False, "Input stays locked through the hold.");
                Assert.That(home.InteractionPrompt.PromptKey, Is.Empty, "No prompt while he stands for the drips.");
                Assert.That(shower.Drips.HoldActive, Is.True);
            });
            yield return null;
            yield return AtPresentation(() =>
            {
                Assert.That(shower.Timeline.Phase, Is.EqualTo(HomeShowerScenePhase.DripHold));
                // The root does not move; the lens rides the idle's breathing
                // and the release of the brace, a few centimetres per frame at
                // the test's time scale, never a step or a turn.
                Assert.That(Vector3.Distance(hero.position, heldRoot), Is.LessThan(0.001f), "He stands still.");
                Assert.That(Vector3.Distance(camera.transform.position, heldPosition), Is.LessThan(0.08f), "Only the idle breathes.");
                Assert.That(Quaternion.Angle(camera.transform.rotation, heldRotation), Is.LessThan(6f), "Only the idle breathes.");
            });
            yield return WaitUntil(
                () => shower.Drips.HoldEmitted >= 2 || shower.Timeline.Phase != HomeShowerScenePhase.DripHold,
                "The tap never dripped.");
            Assert.That(shower.WaterEffect.DropsEmitted, Is.GreaterThanOrEqualTo(2));
            yield return AtPresentation(() =>
            {
                Assert.That(shower.WaterEffect.StreamParticleCount, Is.Zero, "The stream is gone while he stands.");
            });

            yield return WaitUntil(() => !shower.IsUndressed, "He never dressed again.");
            yield return AtPresentation(() =>
            {
                Assert.That(shower.Timeline.Phase, Is.GreaterThanOrEqualTo(HomeShowerScenePhase.StepOut));
                Assert.That(shower.Timeline.CameraBlend, Is.GreaterThanOrEqualTo(HomeShowerFirstPersonView.HeadHideBlend), "He dresses with the lens still in his head.");
                Assert.That(Find(registry, "CLO_JacketBody").Renderer.enabled, Is.True);
                Assert.That(shower.WashPose.BridgesShown, Is.False);
            });

            yield return WaitUntil(() => home.Player.Motor.InputEnabled, "The shower scene never restored the player.");
            Time.timeScale = 1f;
            Assert.That(invariants.Violation, Is.Null);
            Assert.That(shower.Timeline.Phase, Is.EqualTo(HomeShowerScenePhase.Idle));
            AssertRigRestored(before, registry);
            Assert.That(Player3DHeadVisibility.IsHeadDrawn(registry), Is.True, "The head is back once the lens has left.");
            Assert.That(shower.View.IsActive, Is.False);
            Assert.That(home.CameraFollow.FixedBaseFieldOfView, Is.EqualTo(92f).Within(0.01f), "The camera is home.");
            Assert.That(home.PlayerOcclusion.enabled, Is.True);
            Assert.That(home.Soundscape.ShowerWaterAmount, Is.Zero.Within(0.001f));
            Assert.That(shower.WaterEffect.IsDripping, Is.False);
            Assert.That(shower.WashPose.BridgesShown, Is.False);
            Assert.That(HomeShowerFraming.IsInsideStall(home.Room.InverseTransformPoint(hero.position)), Is.False, "He ends in the opening, facing the room.");
            Assert.That(GameSessionState.StressLevel, Is.EqualTo(50 - HomeShowerInteraction.StressRelief));
            Assert.That(GameSessionState.HeroMouthSoiled, Is.False, "A wash always washes the face.");
            Assert.That(Player3DBathingAppearance.IsActive, Is.False);
            Object.Destroy(invariants);
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

            yield return WalkToAndActivate(shower, new Vector3(3.30f, 0.12f, 2.35f));
            Time.timeScale = FastTimeScale;
            yield return WaitUntil(
                () => shower.Timeline.Phase == HomeShowerScenePhase.Wash && shower.Timeline.PhaseElapsed > 1f,
                "The wash never started.");
            Assert.That(shower.IsUndressed, Is.True);
            Assert.That(shower.View.IsHeadHidden, Is.True);
            Assert.That(home.Soundscape.ShowerWaterAmount, Is.GreaterThan(0.5f));

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
            Assert.That(home.PlayerOcclusion.enabled, Is.True);
            Assert.That(shower.HoldsHandoff, Is.False);
            Assert.That(home.Player.Visual.InteractionHandoffLocked, Is.False);
            Assert.That(shower.HotHandleTurn, Is.Zero);
            Assert.That(shower.WashPose.BridgesShown, Is.False);
            Assert.That(shower.Timeline.Phase, Is.EqualTo(HomeShowerScenePhase.Idle));
            Assert.That(GameSessionState.StressLevel, Is.EqualTo(50), "A cancel commits nothing.");
            Assert.That(Player3DBathingAppearance.IsActive, Is.False);
            shower.enabled = true;
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
            if (Violation != null || Check == null)
            {
                return;
            }

            string violation = Check();
            if (violation != null)
            {
                Violation = violation + " at t=" + Time.time.ToString("F2");
            }
        }
    }
}

using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Text;
using NUnit.Framework;
using Unity.Profiling;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace BarPromenade.Tests.PlayMode
{
    public sealed partial class CombatTestPlayModeTests
    {
        private static readonly string[] CarryPerformanceMarkers =
        {
            "BarPromenade.WeaponCarry.Present", "BarPromenade.WeaponCarry.Sling.Present",
            "BarPromenade.WeaponCarry.Sling.Pose", "BarPromenade.WeaponCarry.Sling.Solve",
            "BarPromenade.WeaponCarry.Sling.Seed", "BarPromenade.WeaponCarry.Sling.Render",
            "BarPromenade.WeaponCarry.BodyContacts.Skin", "BarPromenade.WeaponCarry.BodyContacts.Support",
            "BarPromenade.WeaponCarry.BodyContacts.Planes", "BarPromenade.WeaponCarry.Sling.Contacts",
            "BarPromenade.WeaponCarry.Sling.Measure", "BarPromenade.WeaponCarry.BodyContacts.Ray",
            "BarPromenade.WeaponCarry.BodyContacts.Support.LocalSkin", "BarPromenade.WeaponCarry.BodyContacts.Support.Bounds",
            "BarPromenade.WeaponCarry.BodyContacts.Support.Native", "BarPromenade.WeaponCarry.BodyContacts.Support.Mono",
            "BarPromenade.WeaponCarry.BodyContacts.Support.Native.Execute",
            "BarPromenade.WeaponCarry.BodyContacts.Ray.Native.Execute", "BarPromenade.WeaponCarry.BodyContacts.Ray.Mono",
            "BarPromenade.WeaponCarry.BodyContacts.Ray.Prepare",
            "BarPromenade.WeaponCarry.BodyContacts.Ray.Native.Containing", "BarPromenade.WeaponCarry.BodyContacts.Ray.Native.Remaining",
            "BarPromenade.WeaponCarry.Sling.Projection",
            "BarPromenade.WeaponCarry.Sling.Targets",
            "BarPromenade.WeaponCarry.Sling.Snapshot",
            "BarPromenade.WeaponCarry.Sling.Projection.Workspace",
            "BarPromenade.WeaponCarry.BodyContacts.Ray.Workspace",
            "BarPromenade.SecondaryMotion.Jacket.Apply", "BarPromenade.SecondaryMotion.Jacket.Pose",
            "BarPromenade.SecondaryMotion.Jacket.Surface", "BarPromenade.SecondaryMotion.Jacket.Constrain",
            "BarPromenade.SecondaryMotion.Jacket.NativeSolver", "BarPromenade.SecondaryMotion.Jacket.NativeArea",
            "BarPromenade.SecondaryMotion.Jacket.Write", "BarPromenade.SecondaryMotion.Jacket.Material",
            "BarPromenade.SecondaryMotion.Jacket.Area", "BarPromenade.SecondaryMotion.Jacket.FreeSurface"
        };

        [UnityTest]
        public IEnumerator Range_BodyRayWorkspaceReusesFreshPoseAndReleasesOwnedBuffers()
        {
            yield return SceneManager.LoadSceneAsync(SceneIds.MainMenu, LoadSceneMode.Single);
            Assert.That(CombatTestStartService.TryStart(new CombatLoadoutSelection(
                CombatWeaponId.Shotgun, CombatWeaponId.Shotgun)), Is.True);
            yield return AwaitSelectedCombatRange();
            PlacePair(8f);
            Assert.That(root.SetOpponentFocus(false), Is.True);
            yield return null;
            CombatActor hero = root.Hero;
            GameObject shotgun = hero.CarriedWeapon(CombatWeaponId.Shotgun);
            CombatShotgunSling sling = shotgun.GetComponent<CombatShotgunSling>();
            var contacts = (PlayerScarfBodyContacts)CarrySupportField(sling, "bodyContacts");
            contacts.UpdatePose();
            using (contacts.BeginNativeQueries()) { }
            object workspace = CarrySupportField(contacts, "retainedNativeRayQueries");
            int allocations = contacts.NativeRayBufferAllocationCount;
            Assert.That(contacts.NativeRayWorkspaceCreateCount, Is.EqualTo(1));
            Assert.That(allocations, Is.GreaterThan(0));
            Assert.That(contacts.HasRetainedRayWorkspace, Is.True);

            AssertCarryRayScopeLifetime(contacts, workspace);
            var proxies = (Array)CarrySupportField(contacts, "proxies");
            AssertCarryRayBroadPhase(contacts, proxies);
            AssertCarryNativeNearest(contacts, proxies);
            Vector3 probe = ((Player3DCharacterPresentation)root.Player.Visual).Registry.Anchors.Chest.position;
            uint mask = proxies.Length == 32 ? uint.MaxValue : (1u << proxies.Length) - 1u;
            using var recorder = CreateBodyAllocationRecorder();
            // Warm the typed call path independently of reflection-based proofs.
            RunCarryRayScopes(contacts, probe, mask, 1);
            recorder.Start();
            long started = Stopwatch.GetTimestamp();
            RunCarryRayScopes(contacts, probe, mask, 32);
            double milliseconds = (Stopwatch.GetTimestamp() - started) * 1000d / Stopwatch.Frequency;
            long managedAllocations = StopBodyAllocationRecorder(recorder);
            Assert.That(managedAllocations, Is.Zero,
                "Warm body ray scopes must reuse both their native buffers and managed query storage.");
            AssertCarryRetainedRayWorkspace(contacts, workspace, allocations);

            Vector3 position = hero.transform.position;
            Quaternion rotation = hero.transform.rotation;
            int changedHull = -1;
            Plane precedingPlane = default;
            for (int index = 0; index < proxies.Length; index++)
            {
                var planes = CarrySupportField(proxies.GetValue(index), "WorldPlanes") as Plane[];
                if (planes == null || planes.Length == 0 || !(bool)CarrySupportField(proxies.GetValue(index), "Active")) continue;
                changedHull = index; precedingPlane = planes[0]; break;
            }
            Assert.That(changedHull, Is.GreaterThanOrEqualTo(0));
            try
            {
                hero.transform.SetPositionAndRotation(position + new Vector3(.33f, .15f, -.21f),
                    Quaternion.Euler(0f, 27f, 0f) * rotation);
                contacts.UpdatePose();
                var movedPlanes = (Plane[])CarrySupportField(proxies.GetValue(changedHull), "WorldPlanes");
                Assert.That(movedPlanes[0].Equals(precedingPlane), Is.False,
                    "The fresh-pose proof must exercise a changed world hull.");
                // The same retained workspace must capture the moved hulls, not
                // pass an equivalence proof against its preceding frame's pose.
                AssertCarryRayBroadPhase(contacts, proxies);
                AssertCarryNativeNearest(contacts, proxies);
                AssertCarryRetainedRayWorkspace(contacts, workspace, allocations);
            }
            finally
            {
                hero.transform.SetPositionAndRotation(position, rotation);
                contacts.UpdatePose();
            }
            root.ResetRound();
            yield return null;
            Assert.That(hero.CarriedWeapon(CombatWeaponId.Shotgun), Is.SameAs(shotgun));
            Assert.That(CarrySupportField(sling, "bodyContacts"), Is.SameAs(contacts));
            AssertCarrySupportExtrema(hero);
            AssertCarryRetainedRayWorkspace(contacts, workspace, allocations);
            AssertCarrySharedNormalUploads(((Player3DCharacterPresentation)root.Player.Visual).Registry);

            // Disposing another owner with an open lease must release all of
            // its buffers without invalidating this live sling's workspace.
            var registry = ((Player3DCharacterPresentation)root.Player.Visual).Registry;
            var disposable = new PlayerScarfBodyContacts(registry, useMeshSupportPlanes: true,
                includeEquipment: false, poseSourceSkin: true, pointQuerySlots: 41);
            try
            {
                disposable.UpdatePose();
                var active = disposable.BeginNativeQueries();
                object disposedWorkspace = CarrySupportField(disposable, "retainedNativeRayQueries");
                disposable.Dispose();
                active.Dispose();
                disposable.Dispose();
                Assert.Throws<ObjectDisposedException>(() => disposable.BeginNativeQueries());
                AssertCarryReleasedRayWorkspace(disposable, disposedWorkspace);
            }
            finally { disposable.Dispose(); }
            AssertCarryRetainedRayWorkspace(contacts, workspace, allocations);

            if (Array.IndexOf(Environment.GetCommandLineArgs(), "-weaponCarryPerformanceCapture") >= 0)
                CaptureCarryRayWorkspaceViews();
            string report = string.Format(CultureInfo.InvariantCulture,
                "Test body ray workspace: warm scope CPU={0:F4} ms/scope; managed allocations={1}; " +
                "workspace creations={2}; native buffer allocations={3}; fresh pose/reset and exact ray/nearest proofs preserved.",
                milliseconds / 32d, managedAllocations, contacts.NativeRayWorkspaceCreateCount, allocations);
            string reportPath = Path.GetFullPath("TestResults/body-ray-workspace-performance.txt");
            Directory.CreateDirectory(Path.GetDirectoryName(reportPath));
            File.WriteAllText(reportPath, report + Environment.NewLine);
            TestContext.Progress.WriteLine(report);
            yield return SceneManager.LoadSceneAsync(SceneIds.MainMenu, LoadSceneMode.Single);
            yield return null;
            AssertCarryReleasedRayWorkspace(contacts, workspace);
        }

        private static void RunCarryRayScopes(PlayerScarfBodyContacts contacts, Vector3 point, uint mask, int count)
        {
            for (int iteration = 0; iteration < count; iteration++)
            {
                using var scope = contacts.BeginNativeQueries();
                scope.Clip(point, Vector3.right, null, float.PositiveInfinity);
                scope.TryInterval(0, out _, out _);
                scope.PrepareNearest(point, mask, 0);
                scope.TryNearest(0, out _, out _, out _);
            }
        }

        private static void AssertCarryRayScopeLifetime(PlayerScarfBodyContacts contacts, object workspace)
        {
            var first = contacts.BeginNativeQueries();
            try
            {
                Assert.Throws<InvalidOperationException>(() => contacts.BeginNativeQueries(),
                    "An active query scope must reject an overlapping lease.");
            }
            finally { first.Dispose(); }
            first.Dispose();
            Assert.Throws<InvalidOperationException>(() => first.Clip(Vector3.zero, Vector3.right, null, float.PositiveInfinity),
                "A closed lease cannot issue a query against a retained workspace.");
            var second = contacts.BeginNativeQueries();
            try
            {
                first.Dispose();
                Assert.That(CarrySupportField(contacts, "nativeRayQueries"), Is.SameAs(workspace),
                    "Closing an old or copied lease cannot close a newer scope.");
                Assert.Throws<InvalidOperationException>(() => first.Clip(Vector3.zero, Vector3.right, null, float.PositiveInfinity),
                    "An old lease cannot query another presentation's retained workspace.");
            }
            finally { second.Dispose(); }
            Assert.That(CarrySupportField(contacts, "nativeRayQueries"), Is.Null);
        }

        private static void AssertCarryRetainedRayWorkspace(PlayerScarfBodyContacts contacts, object workspace, int allocations)
        {
            Assert.That(contacts.HasRetainedRayWorkspace, Is.True);
            Assert.That(CarrySupportField(contacts, "retainedNativeRayQueries"), Is.SameAs(workspace));
            Assert.That(contacts.NativeRayWorkspaceCreateCount, Is.EqualTo(1));
            Assert.That(contacts.NativeRayBufferAllocationCount, Is.EqualTo(allocations),
                "Movement, reset and repeated proofs must retain the warmed native query buffers.");
        }

        private static void AssertCarryReleasedRayWorkspace(PlayerScarfBodyContacts contacts, object workspace)
        {
            Assert.That(contacts.HasRetainedRayWorkspace, Is.False);
            Assert.That(CarrySupportField(contacts, "nativeRayQueries"), Is.Null);
            int buffers = 0;
            foreach (FieldInfo field in workspace.GetType().GetFields(BindingFlags.Instance | BindingFlags.NonPublic))
            {
                if (!field.FieldType.IsGenericType || field.FieldType.GetGenericTypeDefinition() != typeof(Unity.Collections.NativeArray<>)) continue;
                buffers++;
                object buffer = field.GetValue(workspace);
                Assert.That((bool)field.FieldType.GetProperty("IsCreated").GetValue(buffer), Is.False,
                    "Destroyed body contact ownership retained its native buffer: " + field.Name);
            }
            Assert.That(buffers, Is.GreaterThan(0));
        }

        private static void AssertCarrySharedNormalUploads(Player3DAssetRegistry registry)
        {
            PlayerJacketCloth cloth = registry.GetComponent<PlayerJacketCloth>();
            Assert.That(cloth, Is.Not.Null);
            var surfaces = (PlayerJacketClothSurface[])CarrySupportField(cloth, "surfaces");
            foreach (PlayerJacketClothSurface surface in surfaces)
            {
                surface.CommitSharedNormals();
                long normals = surface.SharedNormalUploadCount, tangents = surface.SharedTangentUploadCount;
                surface.CommitSharedNormals();
                Assert.That(surface.SharedNormalUploadCount, Is.EqualTo(normals));
                Assert.That(surface.SharedTangentUploadCount, Is.EqualTo(tangents),
                    "An unchanged shared lighting basis must not upload another mesh array.");
                CollectionAssert.AreEqual((List<Vector3>)CarrySupportField(surface, "normals"), surface.Mesh.normals);
                var expectedTangents = (List<Vector4>)CarrySupportField(surface, "tangents");
                if (expectedTangents.Count > 0) CollectionAssert.AreEqual(expectedTangents, surface.Mesh.tangents);
            }
        }

        private void CaptureCarryRayWorkspaceViews()
        {
            Camera camera = root.CameraFollow.Camera;
            Vector3 position = camera.transform.position;
            Quaternion rotation = camera.transform.rotation;
            float fieldOfView = camera.fieldOfView;
            RenderTexture previousTarget = camera.targetTexture, previousActive = RenderTexture.active;
            var target = new RenderTexture(640, 360, 24);
            var frame = new Texture2D(640, 360, TextureFormat.RGB24, false);
            try
            {
                camera.targetTexture = target;
                camera.fieldOfView = 43f;
                foreach (bool rear in new[] { false, true })
                {
                    Vector3 focus = root.Hero.transform.position + Vector3.up * 1.10f;
                    Vector3 eye = root.Hero.transform.TransformPoint(rear ?
                        new Vector3(1.65f, 1.4f, -2.3f) : new Vector3(-.8f, 1.35f, 2.75f));
                    camera.transform.SetPositionAndRotation(eye, Quaternion.LookRotation(focus - eye));
                    camera.Render();
                    RenderTexture.active = target;
                    frame.ReadPixels(new Rect(0f, 0f, 640, 360), 0, 0);
                    frame.Apply();
                    bool visible = false;
                    foreach (Color32 pixel in frame.GetPixels32())
                        if (pixel.r > 4 || pixel.g > 4 || pixel.b > 4) { visible = true; break; }
                    Assert.That(visible, Is.True, "The cloth view requires a rendered production camera frame.");
                    string path = Path.GetFullPath(Path.Combine("Captures", SceneIds.CombatTest, "WeaponCarry",
                        rear ? "Test shotgun back.png" : "Test shotgun front.png"));
                    Directory.CreateDirectory(Path.GetDirectoryName(path));
                    File.WriteAllBytes(path, frame.EncodeToPNG());
                    TestContext.Progress.WriteLine("Test cloth view: " + path);
                }
            }
            finally
            {
                camera.targetTexture = previousTarget;
                RenderTexture.active = previousActive;
                camera.transform.SetPositionAndRotation(position, rotation);
                camera.fieldOfView = fieldOfView;
                UnityEngine.Object.DestroyImmediate(frame);
                target.Release();
                UnityEngine.Object.DestroyImmediate(target);
            }
        }

        [UnityTest]
        public IEnumerator Range_WeaponCarryHasBoundedPresentationWork()
        {
            bool baseline = Array.IndexOf(Environment.GetCommandLineArgs(), "-weaponCarryPerformanceBaseline") >= 0;
            var observations = new List<CarryPerformanceBucket>();
            var nativeRayReports = new List<string>();
            var shotgunSteady = new CarryPerformanceBucket("Shotgun both directions steady");
            double slowestShotgunMilliseconds = -1d;
            string slowestShotgunFrame = null;
            PropertyInfo projectionIterations = typeof(CombatShotgunSling).GetProperty("SteadyProjectionIterations", BindingFlags.Instance | BindingFlags.NonPublic);
            FieldInfo projectionBudget = typeof(CombatShotgunSling).GetField("SteadyProjectionIterationBudget", BindingFlags.Static | BindingFlags.NonPublic);
            PropertyInfo repairIterations = typeof(CombatShotgunSling).GetProperty("SteadyLengthRepairIterations", BindingFlags.Instance | BindingFlags.NonPublic);
            FieldInfo repairBudget = typeof(CombatShotgunSling).GetField("SteadyLengthRepairIterationBudget", BindingFlags.Static | BindingFlags.NonPublic);
            Assert.That(projectionIterations, Is.Not.Null); Assert.That(projectionBudget, Is.Not.Null);
            Assert.That(repairIterations, Is.Not.Null); Assert.That(repairBudget, Is.Not.Null);
            var readProjectionIterations = (Func<CombatShotgunSling, int>)Delegate.CreateDelegate(
                typeof(Func<CombatShotgunSling, int>), projectionIterations.GetGetMethod(true));
            var readRepairIterations = (Func<CombatShotgunSling, int>)Delegate.CreateDelegate(
                typeof(Func<CombatShotgunSling, int>), repairIterations.GetGetMethod(true));
            int iterationBudget = (int)projectionBudget.GetRawConstantValue(), maximumSteadyIterations = 0;
            int lengthRepairBudget = (int)repairBudget.GetRawConstantValue(), maximumRepairIterations = 0, maximumTotalIterations = 0;
            long totalRepairIterations = 0;
            float maximumMaterialLengthError = 0f, maximumLinkError = 0f, maximumContactError = 0f;
            var input = new InputTestFixture();
            Keyboard keyboard = null;
            try
            {
                input.Setup();
                keyboard = InputSystem.AddDevice<Keyboard>();
                foreach (CombatWeaponId secondary in new[] { CombatWeaponId.Shotgun, CombatWeaponId.Crowbar })
                {
                    yield return SceneManager.LoadSceneAsync(SceneIds.MainMenu, LoadSceneMode.Single);
                    var selection = new CombatLoadoutSelection(secondary, secondary);
                    Assert.That(CombatTestStartService.TryStart(selection), Is.True);
                    yield return AwaitSelectedCombatRange();
                    PlacePair(8f);
                    Assert.That(root.SetOpponentFocus(false), Is.True);
                    root.SendMessage("OnApplicationFocus", true);
                    GameInput.HandleApplicationFocus(true);
                    yield return null;
                    CombatActor hero = root.Hero;
                    GameObject pistol = hero.CarriedWeapon(CombatWeaponId.Pistol);
                    GameObject longWeapon = hero.CarriedWeapon(secondary);
                    IFirearmState ammunition = hero.CarriedFirearm(CombatWeaponId.Pistol);
                    int rounds = ammunition.Rounds;
                    foreach (CombatWeaponId target in new[] { CombatWeaponId.Pistol, secondary })
                    {
                        string direction = (target == CombatWeaponId.Pistol ? secondary : CombatWeaponId.Pistol) + " to " + target;
                        var startup = new CarryPerformanceBucket(direction + " startup or seed");
                        var steady = new CarryPerformanceBucket(direction + " steady");
                        observations.Add(startup); observations.Add(steady);
                        PlayerScarfBodyContacts rayContacts = secondary == CombatWeaponId.Shotgun ?
                            CarrySupportField(longWeapon.GetComponent<CombatShotgunSling>(), "bodyContacts") as PlayerScarfBodyContacts : null;
                        if (rayContacts != null)
                        {
                            MethodInfo reset = rayContacts.GetType().GetMethod("ResetNativeRayDiagnostics", BindingFlags.Instance | BindingFlags.NonPublic);
                            Assert.That(reset, Is.Not.Null);
                            reset.Invoke(rayContacts, null);
                        }
                        long[] rayBefore = rayContacts != null ? CarryRayStatistics(rayContacts) : null;
                        var recorders = new ProfilerRecorder[CarryPerformanceMarkers.Length];
                        try
                        {
                            for (int i = 0; i < recorders.Length; i++)
                            {
                                recorders[i] = ProfilerRecorder.StartNew(ProfilerCategory.Scripts,
                                    CarryPerformanceMarkers[i], 1, ProfilerRecorderOptions.Default |
                                    ProfilerRecorderOptions.CollectOnlyOnCurrentThread |
                                    ProfilerRecorderOptions.SumAllSamplesInFrame);
                                Assert.That(recorders[i].Valid, Is.True, "Missing carry profiling marker: " + CarryPerformanceMarkers[i]);
                            }
                            Assert.That(hero.TrySelectWeapon(target), Is.True);
                            int transfers = 0;
                            var samples = new ProfilerRecorderSample[recorders.Length];
                            for (int frame = 0; frame < 420 && hero.IsSwitchingWeapon; frame++)
                            {
                                // Reset also stops collection. Restart before the next measured frame,
                                // so sparse Seed markers cannot reuse a previous frame's last sample.
                                if (frame > 0)
                                    for (int i = 0; i < recorders.Length; i++)
                                    { recorders[i].Reset(); recorders[i].Start(); }
                                Transform pistolParent = pistol.transform.parent, longParent = longWeapon.transform.parent;
                                Vector3 pistolPosition = pistol.transform.position, longPosition = longWeapon.transform.position;
                                long began = Stopwatch.GetTimestamp();
                                root.Tick(1f / 60f);
                                double tickMilliseconds = (Stopwatch.GetTimestamp() - began) * 1000d / Stopwatch.Frequency;
                                yield return null;
                                for (int i = 0; i < recorders.Length; i++)
                                    samples[i] = recorders[i].Count > 0 ? recorders[i].GetSample(recorders[i].Count - 1) : default;
                                // One-time graph/first-use work and actual cold seeds are reported,
                                // never silently discarded or mixed into the steady-state budget.
                                bool cold = frame < 2 || samples[4].Count > 0;
                                (cold ? startup : steady).Add(tickMilliseconds, samples);
                                if (secondary == CombatWeaponId.Shotgun && !cold) shotgunSteady.Add(tickMilliseconds, samples);
                                GameObject shotgun = hero.CarriedWeapon(CombatWeaponId.Shotgun);
                                if (shotgun != null)
                                {
                                    CombatShotgunSling sling = shotgun.GetComponent<CombatShotgunSling>();
                                    Assert.That(sling, Is.Not.Null);
                                    if (!cold)
                                    {
                                        int iterations = readProjectionIterations(sling);
                                        maximumSteadyIterations = Math.Max(maximumSteadyIterations, iterations);
                                        Assert.That(iterations, Is.LessThanOrEqualTo(iterationBudget),
                                            "Integration and final live-pin fitting must share one steady frame budget.");
                                        int repair = readRepairIterations(sling);
                                        maximumRepairIterations = Math.Max(maximumRepairIterations, repair);
                                        maximumTotalIterations = Math.Max(maximumTotalIterations, iterations + repair);
                                        totalRepairIterations += repair;
                                        Assert.That(repair, Is.LessThanOrEqualTo(lengthRepairBudget),
                                            "Final material repair must stay within its separate contact-free budget.");
                                        Assert.That(iterations + repair, Is.LessThanOrEqualTo(iterationBudget + lengthRepairBudget),
                                            "The frame must bound contact-aware and contact-free projections together.");
                                        maximumMaterialLengthError = Mathf.Max(maximumMaterialLengthError,
                                            Mathf.Abs(sling.PresentedLength - sling.AuthoredLength));
                                        maximumLinkError = Mathf.Max(maximumLinkError, sling.MaximumLinkError);
                                        maximumContactError = Mathf.Max(maximumContactError, sling.MaximumContactError);
                                    }
                                    double slingMilliseconds = samples[1].Value * .000001d;
                                    if (!cold && samples[1].Count > 0 && slingMilliseconds > slowestShotgunMilliseconds)
                                    {
                                        slowestShotgunMilliseconds = slingMilliseconds;
                                        slowestShotgunFrame = string.Format(CultureInfo.InvariantCulture,
                                            "Slowest steady shotgun frame: {0}; Sling.Present={1:F4}ms; action={2} {3} progress={4:F4}; " +
                                            "link/contact={5:F6}/{6:F6}m; shoulder/hand={7:F3}/{8:F3}; " +
                                            "stock-hand direct/material={9:F6}/{10:F6}m; hand-foreend direct/material={11:F6}/{12:F6}m",
                                            direction, slingMilliseconds, hero.WeaponCarryActionWeapon?.ToString() ?? "none",
                                            hero.WeaponCarryActionIsDraw ? "draw" : "stow", hero.WeaponCarryActionProgress,
                                            sling.MaximumLinkError, sling.MaximumContactError, sling.ShoulderAttachmentAmount,
                                            sling.LeftHandAttachmentAmount, sling.StockToHandDistance, sling.StockToHandMaterialLength,
                                            sling.HandToForeendDistance, sling.HandToForeendMaterialLength);
                                    }
                                    // The shared helper captures only on length failure. Fail here first:
                                    // this CPU regression produces no screenshots, including failure paths.
                                    Assert.That(Mathf.Abs(sling.PresentedLength - sling.AuthoredLength), Is.LessThan(.03f),
                                        direction + ": the measured soft sling must preserve its real material length.");
                                }
                                AssertShotgunSling(hero);
                                if (pistol.transform.parent != pistolParent || longWeapon.transform.parent != longParent)
                                {
                                    GameObject moved = pistol.transform.parent != pistolParent ? pistol : longWeapon;
                                    Vector3 before = moved == pistol ? pistolPosition : longPosition;
                                    Assert.That(Vector3.Distance(before, moved.transform.position), Is.LessThan(.1f),
                                        "Profiling must retain the authored body/hand transfer pose.");
                                    transfers++;
                                }
                                if (hero.IsSwitchingWeapon)
                                {
                                    Assert.That(hero.TryAttack(), Is.False);
                                    Assert.That(hero.RequestPistolShot(), Is.False);
                                }
                            }
                            Assert.That(hero.IsSwitchingWeapon, Is.False, direction + ": carry did not finish within its authored timeline.");
                            Assert.That(hero.HasWeaponInHand, Is.True);
                            Assert.That(hero.WeaponId, Is.EqualTo(target));
                            Assert.That(hero.Weapon, Is.SameAs(target == CombatWeaponId.Pistol ? pistol : longWeapon));
                            Assert.That(transfers, Is.EqualTo(2));
                            Assert.That(hero.CarriedWeapon(CombatWeaponId.Pistol), Is.SameAs(pistol));
                            Assert.That(hero.CarriedWeapon(secondary), Is.SameAs(longWeapon));
                            Assert.That(hero.CarriedFirearm(CombatWeaponId.Pistol), Is.SameAs(ammunition));
                            Assert.That(ammunition.Rounds, Is.EqualTo(rounds));
                            Assert.That(steady.FrameCount, Is.GreaterThan(30), "Steady work cannot be hidden by perpetual cold resets.");
                        }
                        finally
                        {
                            for (int i = 0; i < recorders.Length; i++) recorders[i].Dispose();
                            if (rayContacts != null)
                            {
                                long[] after = CarryRayStatistics(rayContacts);
                                long runs = after[0] - rayBefore[0], verified = after[1] - rayBefore[1];
                                long overflows = after[2] - rayBefore[2], misses = after[3] - rayBefore[3], candidates = after[4] - rayBefore[4];
                                long selected = verified - overflows - misses;
                                PropertyInfo mode = rayContacts.GetType().GetProperty("NativeRayArithmeticMode", BindingFlags.Instance | BindingFlags.NonPublic);
                                Assert.That(mode, Is.Not.Null);
                                nativeRayReports.Add(string.Format(CultureInfo.InvariantCulture,
                                    "Gameplay native rays: {0}; runs={1}; verified intervals={2}; overflows={3}; certified misses={4}; " +
                                    "candidate sum={5}; mean selected candidates={6:F3}; maximum selected candidates={7}; " +
                                    "direct intervals={8}; arithmetic mode={9}; includes startup.",
                                    direction, runs, verified, overflows, misses, candidates, selected > 0 ? candidates / (double)selected : 0d,
                                    after[5], after[6] - rayBefore[6], mode.GetValue(rayContacts)));
                            }
                        }
                        // Check the optimized support query independently of timing. This
                        // complete dot-product scan must never enter the measured frames.
                        AssertCarrySupportExtrema(hero);
                    }
                }
                Assert.That(shotgunSteady.Stages[1].Calls, Is.GreaterThan(0L), "The shotgun CPU budget requires real recorded sling presentation.");
                if (!baseline)
                {
                    Assert.That(shotgunSteady.Stages[1].Percentile(.95), Is.LessThanOrEqualTo(4d),
                        "Steady shotgun sling presentation must cost at most 4 ms per rendered frame at p95.");
                    Assert.That(shotgunSteady.Stages[1].Maximum, Is.LessThanOrEqualTo(12d),
                        "A steady shotgun sling frame must remain below 12 ms.");
                    Assert.That(shotgunSteady.Stages[1].MaximumCalls, Is.LessThanOrEqualTo(1L),
                        "Render-only sling work must run once per rendered frame while rig contact sampling retains its own clock.");
                }
            }
            finally
            {
                var report = new StringBuilder();
                report.AppendLine("Weapon carry CPU; baseline=" + baseline + "; manual Tick=1/60s; current thread; summed marker samples per rendered frame.");
                report.AppendLine("Startup includes the first two rendered frames per switch and any frame containing an actual Seed sample. No camera captures.");
                foreach (CarryPerformanceBucket observation in observations) observation.AppendTo(report);
                shotgunSteady.AppendTo(report);
                report.AppendLine("Steady projection iterations: maximum=" + maximumSteadyIterations + "; frame budget=" + iterationBudget);
                report.AppendLine("Steady contact-free length repair: maximum=" + maximumRepairIterations + "; frame budget=" + lengthRepairBudget +
                    "; total iterations=" + totalRepairIterations + "; maximum combined/frame=" + maximumTotalIterations);
                report.AppendLine(string.Format(CultureInfo.InvariantCulture,
                    "Steady maximum errors: material length={0:F6}m; link={1:F6}m; contact={2:F6}m",
                    maximumMaterialLengthError, maximumLinkError, maximumContactError));
                foreach (string nativeRayReport in nativeRayReports) report.AppendLine(nativeRayReport);
                if (slowestShotgunFrame != null) report.AppendLine(slowestShotgunFrame);
                string path = Path.GetFullPath("TestResults/weapon-carry-performance.txt");
                Directory.CreateDirectory(Path.GetDirectoryName(path));
                File.WriteAllText(path, report.ToString());
                TestContext.Out.WriteLine(report.ToString());
                if (keyboard != null && keyboard.added) InputSystem.RemoveDevice(keyboard);
                input.TearDown();
            }
        }

        private static long[] CarryRayStatistics(PlayerScarfBodyContacts contacts)
        {
            string[] names = { "NativeRayRunCount", "NativeRayVerifiedIntervalCount", "NativeRayOverflowCount",
                "NativeRayCertifiedMissCount", "NativeRayCandidateCount", "NativeRayMaximumCandidateCount", "NativeRayDirectIntervalCount" };
            var values = new long[names.Length];
            for (int i = 0; i < names.Length; i++)
            {
                PropertyInfo property = contacts.GetType().GetProperty(names[i], BindingFlags.Instance | BindingFlags.NonPublic);
                Assert.That(property, Is.Not.Null, "The native ray report cannot find " + names[i]);
                values[i] = Convert.ToInt64(property.GetValue(contacts), CultureInfo.InvariantCulture);
            }
            return values;
        }

        private static void AssertCarrySupportExtrema(CombatActor hero)
        {
            GameObject shotgun = hero.CarriedWeapon(CombatWeaponId.Shotgun);
            if (shotgun == null) return;
            var sling = shotgun.GetComponent<CombatShotgunSling>();
            AssertCarryProjectionGroups(sling);
            var contacts = CarrySupportField(sling, "bodyContacts") as PlayerScarfBodyContacts;
            Assert.That(contacts, Is.Not.Null, "The live soft sling must retain its posed body contacts.");
            AssertCarrySupportExtrema(contacts);
        }

        private static void AssertCarrySupportExtrema(PlayerScarfBodyContacts contacts, bool nativePose = true)
        {
            contacts.UpdatePose();
            var proxies = CarrySupportField(contacts, "proxies") as Array;
            Assert.That(proxies, Is.Not.Null);
            int clouds = 0, planes = 0;
            float maximumError = 0f, maximumSourceError = 0f, maximumCacheLag = 0f;
            foreach (object proxy in proxies)
            {
                object selected = CarrySupportField(proxy, "Selected");
                Assert.That(selected, Is.Not.Null, "A body contact proxy needs its selected source envelope.");
                object skin = CarrySupportField(selected, "Skin");
                if (skin == null) continue; // Rigid hulls have no posed cloud.
                var vertices = CarrySupportField(selected, "PosedVertices") as Vector3[];
                var normals = CarrySupportField(selected, "SupportNormals") as Vector3[];
                var offsets = CarrySupportField(selected, "SupportOffsets") as float[];
                var witnesses = CarrySupportField(selected, "SupportPoints") as int[];
                Assert.That(vertices, Is.Not.Null.And.Not.Empty);
                Assert.That(normals, Is.Not.Null.And.Not.Empty);
                Assert.That(offsets, Is.Not.Null);
                Assert.That(witnesses, Is.Not.Null);
                Assert.That(offsets.Length, Is.EqualTo(normals.Length));
                Assert.That(witnesses.Length, Is.EqualTo(normals.Length));
                string part = CarrySupportField(proxy, "Part").ToString();
                var next = CarrySupportField(selected, "NextPosedVertices") as Vector3[];
                var indices = CarrySupportField(selected, "SupportVertices") as int[];
                PropertyInfo worldCloud = skin.GetType().GetProperty("WorldVertices", BindingFlags.Instance | BindingFlags.Public);
                Assert.That(worldCloud, Is.Not.Null, "The independent world skin must be available on demand.");
                var source = worldCloud.GetValue(skin) as Vector3[];
                var bone = CarrySupportField(proxy, "Bone") as Transform;
                Assert.That(next, Is.Not.Null); Assert.That(indices, Is.Not.Null);
                Assert.That(source, Is.Not.Null); Assert.That(bone, Is.Not.Null);
                Assert.That(next.Length, Is.EqualTo(vertices.Length));
                Assert.That(indices.Length, Is.EqualTo(vertices.Length));
                Matrix4x4 toWorld = bone.localToWorldMatrix;
                float sourceSquare = 0f, lagSquare = 0f;
                bool finite = true;
                for (int i = 0; i < vertices.Length; i++)
                {
                    if ((uint)indices[i] >= source.Length) Assert.Fail(part + ": support source index is outside the original skin.");
                    float sourceError = (toWorld.MultiplyPoint3x4(vertices[i]) - source[indices[i]]).sqrMagnitude;
                    float lag = toWorld.MultiplyVector(next[i] - vertices[i]).sqrMagnitude;
                    if (float.IsNaN(sourceError) || float.IsInfinity(sourceError) || float.IsNaN(lag) || float.IsInfinity(lag))
                        finite = false;
                    sourceSquare = Mathf.Max(sourceSquare, sourceError); lagSquare = Mathf.Max(lagSquare, lag);
                }
                if (!finite || sourceSquare > .00001f * .00001f || lagSquare > .00000101f * .00000101f)
                    Assert.Fail(part + ": relative support cloud changed the original weighted world skin or exceeded cache lag. " +
                        "finite=" + finite + "; max source error=" + Mathf.Sqrt(sourceSquare) + "m; max cache lag=" + Mathf.Sqrt(lagSquare) + "m.");
                maximumSourceError = Mathf.Max(maximumSourceError, Mathf.Sqrt(sourceSquare));
                maximumCacheLag = Mathf.Max(maximumCacheLag, Mathf.Sqrt(lagSquare));
                var worldPlanes = CarrySupportField(proxy, "WorldPlanes") as Plane[];
                Assert.That(worldPlanes, Is.Not.Null);
                Assert.That(worldPlanes.Length, Is.EqualTo(normals.Length));
                float padding = (float)CarrySupportField(selected, "SurfacePadding");
                Matrix4x4 normalMatrix = bone.worldToLocalMatrix.transpose;
                for (int i = 0; i < normals.Length; i++)
                {
                    float extreme = float.NegativeInfinity;
                    foreach (Vector3 vertex in vertices) extreme = Mathf.Max(extreme, Vector3.Dot(normals[i], vertex));
                    float error = Mathf.Abs(offsets[i] - extreme);
                    if (float.IsNaN(extreme) || float.IsInfinity(extreme) ||
                        float.IsNaN(offsets[i]) || float.IsInfinity(offsets[i]) || offsets[i] != extreme)
                    {
                        int witness = witnesses[i];
                        float monoWitness = (uint)witness < vertices.Length ? Vector3.Dot(normals[i], vertices[witness]) : float.NaN;
                        Assert.Fail(part + " support plane " + i + ": cached=" + CarryFloatEvidence(offsets[i]) +
                            "; brute=" + CarryFloatEvidence(extreme) + "; witness index=" + witness +
                            "; MonoDot(normal, witness)=" + CarryFloatEvidence(monoWitness) +
                            ". Support must exactly equal the maximum over every supported source vertex.");
                    }
                    maximumError = Mathf.Max(maximumError, error);
                    // Rebuild the original constructor expression independently
                    // of the cached direction path, including its normalization.
                    Vector3 normal = normalMatrix.MultiplyVector(normals[i]).normalized;
                    Vector3 support = toWorld.MultiplyPoint3x4(normals[i] * offsets[i]);
                    Plane expectedPlane = new Plane(normal, support + normal * padding);
                    Plane actualPlane = worldPlanes[i];
                    if (BitConverter.SingleToInt32Bits(expectedPlane.normal.x) != BitConverter.SingleToInt32Bits(actualPlane.normal.x) ||
                        BitConverter.SingleToInt32Bits(expectedPlane.normal.y) != BitConverter.SingleToInt32Bits(actualPlane.normal.y) ||
                        BitConverter.SingleToInt32Bits(expectedPlane.normal.z) != BitConverter.SingleToInt32Bits(actualPlane.normal.z) ||
                        BitConverter.SingleToInt32Bits(expectedPlane.distance) != BitConverter.SingleToInt32Bits(actualPlane.distance))
                        Assert.Fail(part + " world plane " + i + ": cached directions changed the original normalized world plane.");
                }
                clouds++; planes += normals.Length;
            }
            Assert.That(clouds, Is.GreaterThan(0), "The support cache proof must exercise a real selected skinned source cloud.");
            TestContext.Out.WriteLine("Body support extrema: clouds=" + clouds + "; planes=" + planes +
                "; max offset error=" + maximumError.ToString("G9", CultureInfo.InvariantCulture) +
                "; max world source error=" + maximumSourceError.ToString("G9", CultureInfo.InvariantCulture) +
                "m; max cache lag=" + maximumCacheLag.ToString("G9", CultureInfo.InvariantCulture) + "m");
            AssertCarryRayBroadPhase(contacts, proxies);
            AssertCarryPlaneQueries(contacts, proxies, nativePose);
            AssertCarryNativeNearest(contacts, proxies);
            AssertCarryNativeBurst(contacts);
            AssertCarryFreeSpaceQueries(contacts, proxies);
        }

        private static void AssertCarryNativeNearest(PlayerScarfBodyContacts contacts, Array proxies)
        {
            MethodInfo begin = contacts.GetType().GetMethod("BeginNativeQueries", BindingFlags.Instance | BindingFlags.NonPublic);
            MethodInfo resolve = contacts.GetType().GetMethod("ResolvePointOutward", BindingFlags.Instance | BindingFlags.NonPublic);
            PropertyInfo modeProperty = contacts.GetType().GetProperty("NativeRayArithmeticMode", BindingFlags.Instance | BindingFlags.NonPublic);
            PropertyInfo burstProperty = contacts.GetType().GetProperty("NativeNearestExecutedInBurst", BindingFlags.Instance | BindingFlags.NonPublic);
            PropertyInfo supportProperty = contacts.GetType().GetProperty("NativeNearestSupported", BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(begin, Is.Not.Null); Assert.That(resolve, Is.Not.Null);
            Assert.That(modeProperty, Is.Not.Null); Assert.That(burstProperty, Is.Not.Null);
            Assert.That(supportProperty, Is.Not.Null);
            int mode = (int)modeProperty.GetValue(contacts);
            bool supported = (bool)supportProperty.GetValue(contacts);
            using var scope = begin.Invoke(contacts, null) as IDisposable;
            Assert.That(scope, Is.Not.Null);
            MethodInfo prepare = scope.GetType().GetMethod("PrepareNearest", BindingFlags.Instance | BindingFlags.Public);
            MethodInfo nearest = scope.GetType().GetMethod("TryNearest", BindingFlags.Instance | BindingFlags.Public);
            Assert.That(prepare, Is.Not.Null); Assert.That(nearest, Is.Not.Null);
            Assert.That(proxies.Length, Is.InRange(1, 32));
            uint all = proxies.Length == 32 ? uint.MaxValue : (1u << proxies.Length) - 1u;
            int insideQueries = 0, outsideQueries = 0, excludedQueries = 0, contactQueries = 0;
            var contactPoints = new List<Vector3>();
            var savedHints = new int[proxies.Length];
            var hints = new int[proxies.Length][];
            for (int i = 0; i < proxies.Length; i++)
            {
                hints[i] = CarrySupportField(proxies.GetValue(i), "SeparatingPlanes") as int[];
                if (hints[i] != null && hints[i].Length > 0) savedHints[i] = hints[i][0];
            }
            try
            {
                for (int index = 0; index < proxies.Length; index++)
                {
                    object proxy = proxies.GetValue(index);
                    bool active = (bool)CarrySupportField(proxy, "Active");
                    var planes = CarrySupportField(proxy, "WorldPlanes") as Plane[];
                    if (!active || planes == null || planes.Length == 0) continue;
                    Vector3 low = (Vector3)CarrySupportField(proxy, "WorldMinimum"), high = (Vector3)CarrySupportField(proxy, "WorldMaximum");
                    Vector3 centre = (low + high) * .5f, half = (high - low) * .5f;
                    var points = new List<Vector3> { centre, centre + half * .5f, centre - half * .5f };
                    foreach (Vector3 axis in new[] { Vector3.right, Vector3.up, Vector3.forward })
                    {
                        float extent = Vector3.Dot(half, axis);
                        points.Add(centre + axis * (extent * .99f)); points.Add(centre - axis * (extent * .99f));
                        points.Add(centre + axis * (extent + .01f)); points.Add(centre - axis * (extent + .01f));
                    }
                    contactPoints.Add(centre); contactPoints.Add(points[3]); contactPoints.Add(points[5]);
                    foreach (int hint in new[] { 0, planes.Length - 1 })
                        foreach (Vector3 point in points)
                        {
                            if (hints[index] != null && hints[index].Length > 0) hints[index][0] = hint;
                            bool expected = ExhaustiveCarryNearestPlane(active, low, high, planes, point, out _, out float depth, out int winner);
                            prepare.Invoke(scope, new object[] { point, all, 0 });
                            object[] result = { index, false, -1, 0f };
                            bool available = (bool)nearest.Invoke(scope, result);
                            if (!available)
                            {
                                if (supported && CarrySupportField(proxy, "ScoreTree") != null)
                                    Assert.Fail(CarrySupportField(proxy, "Part") + ": the calibrated native nearest proof lost an eligible hull.");
                                continue;
                            }
                            bool actual = (bool)result[1]; int plane = (int)result[2]; float distance = (float)result[3];
                            bool exact = actual == expected;
                            if (expected) exact &= plane == winner && SameFloat(distance, -depth);
                            else if (plane >= 0)
                                exact &= plane < planes.Length && planes[plane].GetDistanceToPoint(point) >= 0f &&
                                    SameFloat(distance, planes[plane].GetDistanceToPoint(point));
                            else exact &= point.x < low.x || point.x > high.x || point.y < low.y || point.y > high.y || point.z < low.z || point.z > high.z;
                            if (!exact)
                                Assert.Fail(CarrySupportField(proxy, "Part") + ": native nearest BVH changed the original query. point=" + point.ToString("F9") +
                                    "; hint=" + hint + "; inside=" + expected + "/" + actual + "; plane=" + winner + "/" + plane +
                                    "; distance=" + CarryFloatEvidence(-depth) + "/" + CarryFloatEvidence(distance));
                            if (expected) insideQueries++; else outsideQueries++;
                        }
                    prepare.Invoke(scope, new object[] { centre, all & ~(1u << index), 0 });
                    object[] excludedResult = { index, false, -1, 0f };
                    Assert.That((bool)nearest.Invoke(scope, excludedResult), Is.False, "An excluded proxy must not consume a stale native nearest result.");
                    excludedQueries++;
                }
                foreach (Vector3 point in new[] { new Vector3(float.NaN, 0f, 0f), new Vector3(0f, float.PositiveInfinity, 0f),
                    new Vector3(0f, 0f, float.NegativeInfinity) })
                {
                    prepare.Invoke(scope, new object[] { point, all, 0 });
                    for (int i = 0; i < proxies.Length; i++)
                        Assert.That((bool)nearest.Invoke(scope, new object[] { i, false, -1, 0f }), Is.False,
                            "Nonfinite inputs must retain the original managed fallback.");
                }
                var radii = (double[])CarrySupportField(contacts, "freeSpaceRadii");
                foreach (Vector3 point in contactPoints)
                    foreach (Player3DAnatomicalPart? excluded in new Player3DAnatomicalPart?[] { null, Player3DAnatomicalPart.LeftHand })
                    {
                        // Force the slotted initial query, rather than proving
                        // only an already certified free-space cache hit.
                        radii[0] = 0d;
                        object[] reference = { point, Vector3.forward, excluded, -1 };
                        object[] selected = { point, Vector3.forward, excluded, 0 };
                        bool expected = (bool)resolve.Invoke(contacts, reference), actual = (bool)resolve.Invoke(contacts, selected);
                        Vector3 a = (Vector3)reference[0], b = (Vector3)selected[0];
                        if (expected != actual || !SameFloat(a.x, b.x) || !SameFloat(a.y, b.y) || !SameFloat(a.z, b.z))
                            Assert.Fail("Native nearest changed union escape. input=" + point.ToString("F9") + "; excluded=" + excluded +
                                "; changed=" + expected + "/" + actual + "; original=" + a.ToString("F9") + "; selected=" + b.ToString("F9"));
                        contactQueries++;
                    }
            }
            finally
            {
                for (int i = 0; i < hints.Length; i++)
                    if (hints[i] != null && hints[i].Length > 0) hints[i][0] = savedHints[i];
            }
            bool burst = (bool)burstProperty.GetValue(contacts);
            if (supported)
            {
                Assert.That(insideQueries, Is.GreaterThan(0), "The native nearest BVH must search real containing body hulls.");
                Assert.That(outsideQueries, Is.GreaterThan(0));
                Assert.That(burst, Is.True, "The calibrated nearest proof must execute compiled Burst.");
            }
            Assert.That(excludedQueries, Is.GreaterThan(0)); Assert.That(contactQueries, Is.GreaterThan(0));
            TestContext.Out.WriteLine($"Body native nearest: inside/outside={insideQueries}/{outsideQueries}; excluded={excludedQueries}; " +
                $"exact slot0/managed escapes={contactQueries}; first/last warm hints; nonfinite fallback; mode={mode}; Burst={burst}.");
            bool SameFloat(float a, float b) => BitConverter.SingleToInt32Bits(a) == BitConverter.SingleToInt32Bits(b);
        }

        private static void AssertCarryProjectionGroups(CombatShotgunSling sling)
        {
            MethodInfo managed = typeof(CombatShotgunSling).GetMethod("RunManagedProjectionGroup", BindingFlags.Static | BindingFlags.NonPublic);
            MethodInfo native = typeof(CombatShotgunSling).GetMethod("RunNativeProjectionGroup", BindingFlags.Static | BindingFlags.NonPublic);
            Assert.That(managed, Is.Not.Null); Assert.That(native, Is.Not.Null);
            PropertyInfo nativeSupport = typeof(CombatShotgunSling).GetProperty("NativeProjectionSupported", BindingFlags.Static | BindingFlags.NonPublic);
            Assert.That(nativeSupport, Is.Not.Null);
            bool supported = (bool)nativeSupport.GetValue(null);
            Type settingsType = managed.GetParameters()[2].ParameterType;
            var source = (Vector3[])CarrySupportField(sling, "points");
            var sourceMass = (float[])CarrySupportField(sling, "inverseMass");
            int shoulderPoint = (int)CarrySupportField(sling, "shoulderPoint"), handPoint = sling.HandCenterlineIndex / 2;
            Vector3 from = (Vector3)CarrySupportField(sling, "from"), to = (Vector3)CarrySupportField(sling, "to");
            int groups = 0;
            for (int state = 0; state < 5; state++)
                for (int reverseStart = 0; reverseStart < 2; reverseStart++)
                    for (int pinMode = 0; pinMode < 2; pinMode++)
                        foreach (int groupIterations in new[] { 4, 8 })
                    {
                        var points = (Vector3[])source.Clone();
                        var masses = (float[])sourceMass.Clone();
                        for (int i = 0; i < points.Length; i++)
                        {
                            if (state == 1) points[i] += new Vector3((i % 3 - 1) * .07f, (i % 5 - 2) * .035f, (i % 2 == 0 ? 1f : -1f) * .055f);
                            else if (state == 2) points[i] = from;
                            else if (state == 3) points[i] = from + new Vector3((i % 2 == 0 ? 1f : -1f) * .0000004f, (i % 3 - 1) * .0000003f, 0f);
                            if (state == 4) masses[i] = 0f;
                        }
                        if (pinMode == 1) masses[shoulderPoint] = masses[handPoint] = 0f;
                        object settings = Activator.CreateInstance(settingsType);
                        Set("StartIteration", reverseStart); Set("IterationCount", groupIterations);
                        Set("Rest", sling.AuthoredLength / (source.Length - 1));
                        Set("ShoulderPoint", shoulderPoint); Set("HandPoint", handPoint);
                        Set("PinnedFrom", from); Set("PinnedTo", to);
                        Set("PinnedShoulder", CarrySupportField(sling, "shoulderTarget"));
                        Set("PinnedHand", CarrySupportField(sling, "handTarget"));
                        Set("PinShoulder", masses[shoulderPoint] == 0f); Set("PinHand", masses[handPoint] == 0f);
                        Set("FromToAxis", state == 2 || state == 3 ? Vector3.zero : to - from);
                        Set("BodyForward", state == 3 ? Vector3.zero : (Vector3)CarrySupportField(sling, "bodyForward"));
                        var expected = (Vector3[])points.Clone();
                        var actual = (Vector3[])points.Clone();
                        managed.Invoke(null, new[] { (object)expected, masses, settings });
                        bool burst = (bool)native.Invoke(null, new[] { (object)actual, masses, settings });
                        Assert.That(burst, Is.EqualTo(supported), supported ?
                            "The calibrated wide projection proof must execute compiled Burst." :
                            "An unsupported projection arithmetic profile must retain the original managed group.");
                        for (int i = 0; i < points.Length; i++)
                            for (int axis = 0; axis < 3; axis++)
                                if (BitConverter.SingleToInt32Bits(actual[i][axis]) != BitConverter.SingleToInt32Bits(expected[i][axis]))
                                    Assert.Fail("Native sling projection changed original float bits: state=" + state + "; reverse start=" + reverseStart +
                                        "; pin mode=" + pinMode + "; iterations=" + groupIterations + "; point/axis=" + i + "/" + axis + "; Mono=" + CarryFloatEvidence(expected[i][axis]) +
                                        "; native=" + CarryFloatEvidence(actual[i][axis]));
                        groups++;

                        void Set(string name, object value)
                        {
                            FieldInfo field = settingsType.GetField(name, BindingFlags.Instance | BindingFlags.Public);
                            Assert.That(field, Is.Not.Null, "Projection proof cannot find " + name);
                            field.SetValue(settings, value);
                        }
                    }
            TestContext.Out.WriteLine("Sling projection groups: exact selected/Mono groups=" + groups + "; points=" + source.Length +
                "; actual/distorted/degenerate/zero-mass states; both iteration parities and pin modes; 4/8 iterations; Burst=" + supported);
        }

        private static void AssertCarryNativeBurst(PlayerScarfBodyContacts contacts)
        {
            PropertyInfo execution = contacts.GetType().GetProperty("NativeRayExecutedInBurst", BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(execution, Is.Not.Null);
            bool executed = (bool)execution.GetValue(contacts);
            PropertyInfo enabled = Type.GetType("Unity.Burst.BurstCompiler, Unity.Burst")?.GetProperty("IsEnabled", BindingFlags.Static | BindingFlags.Public);
            string diagnostic = "Body native ray execution: Burst=" + executed + "; BurstCompiler.IsEnabled=" +
                (enabled?.GetValue(null)?.ToString() ?? "unavailable");
            PropertyInfo simd = contacts.GetType().GetProperty("NativeRayExecutedWithSimd", BindingFlags.Instance | BindingFlags.NonPublic) ??
                contacts.GetType().GetProperty("NativeRayUsedSimd", BindingFlags.Instance | BindingFlags.NonPublic);
            if (simd != null) diagnostic += "; SIMD=" + simd.GetValue(contacts);
            PropertyInfo mode = contacts.GetType().GetProperty("NativeRayArithmeticMode", BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(mode, Is.Not.Null);
            diagnostic += "; arithmetic mode=" + mode.GetValue(contacts);
            TestContext.Out.WriteLine(diagnostic);
            Assert.That(executed, Is.True, diagnostic + ". The inside-ray proof must exercise compiled native execution.");
        }

        private static void AssertCarryFreeSpaceQueries(PlayerScarfBodyContacts contacts, Array proxies)
        {
            MethodInfo resolve = contacts.GetType().GetMethod("ResolvePointOutward", BindingFlags.Instance | BindingFlags.NonPublic);
            MethodInfo certified = contacts.GetType().GetMethod("InCertifiedFreeSpace", BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(resolve, Is.Not.Null); Assert.That(certified, Is.Not.Null);
            var seeds = new List<Vector3>();
            Vector3 minimum = Vector3.one * float.PositiveInfinity, maximum = Vector3.one * float.NegativeInfinity;
            Vector3? palm = null;
            foreach (object proxy in proxies)
            {
                if (!(bool)CarrySupportField(proxy, "Active")) continue;
                Vector3 low = (Vector3)CarrySupportField(proxy, "WorldMinimum"), high = (Vector3)CarrySupportField(proxy, "WorldMaximum");
                minimum = Vector3.Min(minimum, low); maximum = Vector3.Max(maximum, high);
                Vector3 centre = (low + high) * .5f, half = (high - low) * .5f;
                seeds.Add(centre);
                seeds.Add(centre + Vector3.right * (half.x + .04f));
                seeds.Add(centre + Vector3.up * (half.y + .04f));
                if (CarrySupportField(proxy, "Part").ToString() == "LeftHand") palm = centre;
            }
            Assert.That(seeds, Is.Not.Empty);
            Vector3 unionCentre = (minimum + maximum) * .5f;
            seeds.Add(new Vector3(unionCentre.x, maximum.y + .2f, unionCentre.z));
            seeds.Add(maximum + Vector3.one * .2f);
            Vector3[] offsets = { Vector3.zero, Vector3.right * .0005f, Vector3.left * .0005f,
                Vector3.up * .0005f, Vector3.down * .0005f, Vector3.forward * .0005f, Vector3.back * .0005f,
                new Vector3(.08f, -.06f, .04f) };
            int queries = 0, cacheHits = 0;
            object[] referenceArguments = { Vector3.zero, Vector3.right, null, -1 };
            object[] cachedArguments = { Vector3.zero, Vector3.right, null, 0 };
            foreach (Vector3 seed in seeds)
                foreach (Vector3 offset in offsets)
                    Compare(seed + offset, (queries & 1) == 0 ? (Player3DAnatomicalPart?)null : Player3DAnatomicalPart.LeftHand);
            // A certificate seeded with an open palm must never skip a later
            // query that closes the finger cavity at the same material slot.
            if (palm.HasValue) { Compare(palm.Value, Player3DAnatomicalPart.LeftHand); Compare(palm.Value, null); }
            Assert.That(cacheHits, Is.GreaterThan(0), "Free-space proof must exercise a real certified cache hit.");
            TestContext.Out.WriteLine("Body free-space cache: exact reference queries=" + queries + "; certified hits=" + cacheHits +
                "; nearby/disjoint points and alternating palm exclusion; uncached slot=-1");

            void Compare(Vector3 input, Player3DAnatomicalPart? excluded)
            {
                referenceArguments[0] = input; referenceArguments[2] = excluded;
                cachedArguments[0] = input; cachedArguments[2] = excluded;
                bool wasCached = (bool)certified.Invoke(contacts, new object[] { input, 0 });
                bool expected = (bool)resolve.Invoke(contacts, referenceArguments);
                bool actual = (bool)resolve.Invoke(contacts, cachedArguments);
                Vector3 referencePoint = (Vector3)referenceArguments[0], cachedPoint = (Vector3)cachedArguments[0];
                if (expected != actual ||
                    BitConverter.SingleToInt32Bits(referencePoint.x) != BitConverter.SingleToInt32Bits(cachedPoint.x) ||
                    BitConverter.SingleToInt32Bits(referencePoint.y) != BitConverter.SingleToInt32Bits(cachedPoint.y) ||
                    BitConverter.SingleToInt32Bits(referencePoint.z) != BitConverter.SingleToInt32Bits(cachedPoint.z))
                    Assert.Fail("Free-space cache changed an exact body contact. input=" + input.ToString("F9") +
                        "; excluded=" + excluded + "; cached=" + wasCached + "; reference/actual changed=" + expected + "/" + actual +
                        "; reference/actual point=" + referencePoint.ToString("F9") + "/" + cachedPoint.ToString("F9"));
                queries++; if (wasCached) cacheHits++;
            }
        }

        private static void AssertCarryPlaneQueries(PlayerScarfBodyContacts contacts, Array proxies, bool nativePose = true)
        {
            MethodInfo nearest = contacts.GetType().GetMethod("TryNearestProxyExit", BindingFlags.Static | BindingFlags.NonPublic);
            MethodInfo contains = contacts.GetType().GetMethod("ProxyContains", BindingFlags.Static | BindingFlags.NonPublic);
            Assert.That(nearest, Is.Not.Null); Assert.That(contains, Is.Not.Null);
            MethodInfo beginNative = contacts.GetType().GetMethod("BeginNativeQueries", BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(beginNative, Is.Not.Null);
            using var nativeScope = beginNative.Invoke(contacts, null) as IDisposable;
            Assert.That(nativeScope, Is.Not.Null);
            MethodInfo nativeInside = nativeScope.GetType().GetMethod("ClipFromInside", BindingFlags.Instance | BindingFlags.Public);
            MethodInfo nativeInterval = nativeScope.GetType().GetMethod("TryInterval", BindingFlags.Instance | BindingFlags.Public);
            Assert.That(nativeInside, Is.Not.Null); Assert.That(nativeInterval, Is.Not.Null);
            int shapes = 0, inside = 0, outside = 0, treeQueries = 0, insideRays = 0, nativeInsideRays = 0;
            Vector3[] directions = { Vector3.right, Vector3.left, Vector3.up, Vector3.down,
                Vector3.forward, Vector3.back, new Vector3(1f, 1f, 1f).normalized,
                new Vector3(-1f, 1f, 1f).normalized, new Vector3(1f, -1f, .5f).normalized };
            var nearestArguments = new object[5];
            var containsArguments = new object[3];
            for (int proxyIndex = 0; proxyIndex < proxies.Length; proxyIndex++)
            {
                object proxy = proxies.GetValue(proxyIndex);
                bool active = (bool)CarrySupportField(proxy, "Active");
                var planes = CarrySupportField(proxy, "WorldPlanes") as Plane[];
                if (!active || planes == null || planes.Length == 0) continue;
                Vector3 minimum = (Vector3)CarrySupportField(proxy, "WorldMinimum");
                Vector3 maximum = (Vector3)CarrySupportField(proxy, "WorldMaximum");
                object tree = CarrySupportField(proxy, "ScoreTree");
                MethodInfo treeNearest = tree?.GetType().GetMethod("NearestInside", BindingFlags.Instance | BindingFlags.Public);
                MethodInfo treeContains = tree?.GetType().GetMethod("Contains", BindingFlags.Instance | BindingFlags.Public);
                MethodInfo treeRay = tree?.GetType().GetMethod("RayFromInside", BindingFlags.Instance | BindingFlags.Public);
                if (tree != null)
                { Assert.That(treeNearest, Is.Not.Null); Assert.That(treeContains, Is.Not.Null); Assert.That(treeRay, Is.Not.Null); }
                Vector3 centre = (minimum + maximum) * .5f, half = (maximum - minimum) * .5f;
                Vector3[] points = { centre,
                    centre + Vector3.right * (half.x * .99f), centre - Vector3.right * (half.x * .99f),
                    centre + Vector3.up * (half.y * .99f), centre - Vector3.up * (half.y * .99f),
                    centre + Vector3.forward * (half.z * .99f), centre - Vector3.forward * (half.z * .99f),
                    centre + Vector3.right * (half.x + .01f), centre - Vector3.right * (half.x + .01f),
                    centre + Vector3.up * (half.y + .01f), centre - Vector3.up * (half.y + .01f),
                    centre + Vector3.forward * (half.z + .01f), centre - Vector3.forward * (half.z + .01f),
                    centre + half * .5f, centre - half * .5f,
                    centre + new Vector3(-half.x, half.y, -half.z) * .75f, centre };
                nearestArguments[0] = proxy; containsArguments[0] = proxy;
                for (int i = 0; i < points.Length; i++)
                {
                    Vector3 point = points[i];
                    int slot = (i & 1) == 0 ? -1 : 0;
                    bool reference = ExhaustiveCarryNearestPlane(active, minimum, maximum, planes, point,
                        out Vector3 normal, out float depth, out int planeIndex);
                    nearestArguments[1] = point; nearestArguments[2] = Vector3.zero;
                    nearestArguments[3] = 0f; nearestArguments[4] = slot;
                    containsArguments[1] = point; containsArguments[2] = slot;
                    bool actualNearest = (bool)nearest.Invoke(null, nearestArguments);
                    bool actualContains = (bool)contains.Invoke(null, containsArguments);
                    Vector3 actualNormal = (Vector3)nearestArguments[2];
                    float actualDepth = (float)nearestArguments[3];
                    bool exact = !reference ||
                        BitConverter.SingleToInt32Bits(normal.x) == BitConverter.SingleToInt32Bits(actualNormal.x) &&
                        BitConverter.SingleToInt32Bits(normal.y) == BitConverter.SingleToInt32Bits(actualNormal.y) &&
                        BitConverter.SingleToInt32Bits(normal.z) == BitConverter.SingleToInt32Bits(actualNormal.z) &&
                        BitConverter.SingleToInt32Bits(depth) == BitConverter.SingleToInt32Bits(actualDepth);
                    if (actualNearest != reference || actualContains != reference || !exact)
                        Assert.Fail(CarrySupportField(proxy, "Part") + ": plane query differs from the original ordered scan. " +
                            "point=" + point.ToString("F6") + "; slot=" + slot + "; reference/nearest/contains=" +
                            reference + "/" + actualNearest + "/" + actualContains + "; normal=" + normal.ToString("F9") +
                            "/" + actualNormal.ToString("F9") + "; depth=" + depth.ToString("G9", CultureInfo.InvariantCulture) +
                            "/" + actualDepth.ToString("G9", CultureInfo.InvariantCulture));
                    if (tree != null && point.x >= minimum.x && point.x <= maximum.x &&
                        point.y >= minimum.y && point.y <= maximum.y && point.z >= minimum.z && point.z <= maximum.z)
                    {
                        // Start at the last original plane to prove that a warm
                        // hint cannot replace the first-index winner of a tie.
                        object[] treeArguments = { point, planes.Length - 1, 0f };
                        object[] treeContainsArguments = { point, planes.Length - 1 };
                        bool treeInside = (bool)treeNearest.Invoke(tree, treeArguments);
                        bool treeContained = (bool)treeContains.Invoke(tree, treeContainsArguments);
                        int winner = (int)treeArguments[1];
                        float score = (float)treeArguments[2];
                        if (treeInside != reference || treeContained != reference || reference &&
                            (winner != planeIndex || BitConverter.SingleToInt32Bits(score) != BitConverter.SingleToInt32Bits(-depth)))
                            Assert.Fail(CarrySupportField(proxy, "Part") + ": plane score tree changed the original ordered scan. " +
                                "point=" + point.ToString("F6") + "; reference/nearest/contains=" + reference + "/" + treeInside +
                                "/" + treeContained + "; winner=" + planeIndex + "/" + winner + "; score=" +
                                (-depth).ToString("G9", CultureInfo.InvariantCulture) + "/" + score.ToString("G9", CultureInfo.InvariantCulture));
                        treeQueries++;
                        if (reference)
                        {
                            foreach (Vector3 direction in directions)
                            {
                                bool interval = ExhaustiveCarryPlaneInterval(planes, point, direction, out float entry, out float exit);
                                object[] rayArguments = { point, direction, planes.Length - 1, 0f, 0f };
                                bool actualInterval = (bool)treeRay.Invoke(tree, rayArguments);
                                float actualEntry = (float)rayArguments[3], actualExit = (float)rayArguments[4];
                                if (actualInterval != interval ||
                                    BitConverter.SingleToInt32Bits(actualEntry) != BitConverter.SingleToInt32Bits(entry) ||
                                    BitConverter.SingleToInt32Bits(actualExit) != BitConverter.SingleToInt32Bits(exit))
                                    Assert.Fail(CarrySupportField(proxy, "Part") + ": inside ray changed exhaustive float plane clipping. " +
                                        "point=" + point.ToString("F6") + "; direction=" + direction.ToString("F6") +
                                        "; reference/actual interval=" + interval + "/" + actualInterval + "; entry=" +
                                        entry.ToString("G9", CultureInfo.InvariantCulture) + "/" + actualEntry.ToString("G9", CultureInfo.InvariantCulture) +
                                        "; exit=" + exit.ToString("G9", CultureInfo.InvariantCulture) + "/" + actualExit.ToString("G9", CultureInfo.InvariantCulture));
                                insideRays++;
                                object[] nativeArguments = { point, direction, proxyIndex };
                                nativeInside.Invoke(nativeScope, nativeArguments);
                                object[] nativeResult = { proxyIndex, 0f, 0f };
                                bool nativeHit = (bool)nativeInterval.Invoke(nativeScope, nativeResult);
                                float nativeEntry = (float)nativeResult[1], nativeExit = (float)nativeResult[2];
                                if (nativeHit != interval || interval &&
                                    (BitConverter.SingleToInt32Bits(nativeEntry) != BitConverter.SingleToInt32Bits(entry) ||
                                     BitConverter.SingleToInt32Bits(nativeExit) != BitConverter.SingleToInt32Bits(exit)))
                                    Assert.Fail(CarrySupportField(proxy, "Part") + ": native inside BVH changed exhaustive float plane clipping. " +
                                        "point=" + point.ToString("F6") + "; direction=" + direction.ToString("F6") +
                                        "; reference/native hit=" + interval + "/" + nativeHit + "; entry=" + CarryFloatEvidence(entry) +
                                        "/" + CarryFloatEvidence(nativeEntry) + "; exit=" + CarryFloatEvidence(exit) + "/" + CarryFloatEvidence(nativeExit));
                                nativeInsideRays++;
                            }
                        }
                    }
                    if (reference) inside++; else outside++;
                }
                shapes++;
            }
            Assert.That(inside, Is.GreaterThan(0), "The plane query proof needs real inside points.");
            Assert.That(outside, Is.GreaterThan(0), "The plane query proof needs real outside points.");
            Assert.That(treeQueries, Is.GreaterThan(0), "The plane query proof must exercise the optimized score tree.");
            Assert.That(insideRays, Is.GreaterThan(0), "The inside-ray proof must exercise real containing body hulls.");
            Assert.That(nativeInsideRays, Is.GreaterThan(0), "The direct native inside BVH must exercise real containing body hulls.");
            TestContext.Out.WriteLine("Body plane queries: shapes=" + shapes + "; inside=" + inside + "; outside=" + outside +
                "; tree queries=" + treeQueries + "; inside rays=" + insideRays + "; native inside rays=" + nativeInsideRays +
                "; exact ordered normal/depth/index, contains and float ray interval; query slots=-1/0");
            AssertCarryPlaneTreeRefit(proxies);
            if (nativePose) AssertCarryEnvelopeBounds(proxies);
            else foreach (object proxy in proxies)
            {
                object selected = CarrySupportField(proxy, "Selected");
                if (selected != null)
                    Assert.That((bool)CarrySupportField(selected, "HasNativeBounds"), Is.False,
                        "Jacket bounds must retain the original managed pose calculation.");
            }
        }

        private static void AssertCarryEnvelopeBounds(Array proxies)
        {
            MethodInfo managedBounds = typeof(PlayerScarfBodyContacts).GetMethod("SetEnvelopeBoundsWithPadding",
                BindingFlags.Static | BindingFlags.NonPublic);
            Assert.That(managedBounds, Is.Not.Null);
            int envelopes = 0;
            foreach (object proxy in proxies)
            {
                object selected = CarrySupportField(proxy, "Selected");
                if (selected == null || !(bool)CarrySupportField(selected, "HasNativeBounds")) continue;
                var vertices = (Vector3[])CarrySupportField(selected, "PosedVertices");
                Vector3 padding = (Vector3)CarrySupportField(selected, "BoundsInputPadding");
                Vector3 nativeCenter = (Vector3)CarrySupportField(selected, "BoundsCenter");
                Vector3 nativeRadii = (Vector3)CarrySupportField(selected, "BoundsRadii");
                Matrix4x4 nativeForward = (Matrix4x4)CarrySupportField(selected, "UnitToBone");
                Matrix4x4 nativeInverse = (Matrix4x4)CarrySupportField(selected, "BoneToUnit");
                // Keep the independent original sequential managed loops as
                // the reference, without adding work to measured frames.
                managedBounds.Invoke(null, new object[] { selected, vertices, padding });
                CompareVector(nativeCenter, (Vector3)CarrySupportField(selected, "BoundsCenter"), "center");
                CompareVector(nativeRadii, (Vector3)CarrySupportField(selected, "BoundsRadii"), "radii");
                CompareMatrix(nativeForward, (Matrix4x4)CarrySupportField(selected, "UnitToBone"), "UnitToBone");
                CompareMatrix(nativeInverse, (Matrix4x4)CarrySupportField(selected, "BoneToUnit"), "BoneToUnit");
                envelopes++;

                void CompareVector(Vector3 native, Vector3 mono, string field)
                {
                    for (int axis = 0; axis < 3; axis++) CompareFloat(native[axis], mono[axis], field + "[" + axis + "]");
                }
                void CompareMatrix(Matrix4x4 native, Matrix4x4 mono, string field)
                {
                    for (int i = 0; i < 16; i++) CompareFloat(native[i], mono[i], field + "[" + i + "]");
                }
                void CompareFloat(float native, float mono, string field)
                {
                    if (BitConverter.SingleToInt32Bits(native) != BitConverter.SingleToInt32Bits(mono))
                        Assert.Fail(CarrySupportField(proxy, "Part") + ": native bounds changed the original sequential calculation at " +
                            field + "; native=" + CarryFloatEvidence(native) + "; Mono=" + CarryFloatEvidence(mono) + ".");
                }
            }
            Assert.That(envelopes, Is.GreaterThan(0), "The bounds proof must exercise live native envelope bounds.");
            TestContext.Out.WriteLine("Body envelope bounds: exact center/radii and forward/inverse matrix bits=" + envelopes);
        }

        private static void AssertCarryPlaneTreeRefit(Array proxies)
        {
            int trees = 0, comparedNodes = 0;
            foreach (object proxy in proxies)
            {
                object tree = CarrySupportField(proxy, "ScoreTree");
                if (tree == null) continue;
                PropertyInfo nativeRefit = tree.GetType().GetProperty("HasNativeRefit", BindingFlags.Instance | BindingFlags.Public);
                Assert.That(nativeRefit, Is.Not.Null);
                if (!(bool)nativeRefit.GetValue(tree)) continue;
                var nodes = (Array)CarrySupportField(tree, "nodes");
                var nativeNodes = (Array)nodes.Clone();
                int count = (int)CarrySupportField(tree, "count");
                Vector3 origin = (Vector3)CarrySupportField(tree, "origin");
                MethodInfo managedRefit = tree.GetType().GetMethod("Refit", BindingFlags.Instance | BindingFlags.Public,
                    null, new[] { typeof(Vector3) }, null);
                Assert.That(managedRefit, Is.Not.Null);
                // Outside the measured frames, rerun the original recursive
                // managed construction over the same canonical world planes.
                managedRefit.Invoke(tree, new object[] { origin });
                for (int i = 0; i < count; i++)
                {
                    object actual = nativeNodes.GetValue(i), expected = nodes.GetValue(i);
                    foreach (string name in new[] { "First", "Count", "Left", "Right" })
                        if ((int)CarrySupportField(actual, name) != (int)CarrySupportField(expected, name))
                            Fail(name);
                    foreach (string name in new[] { "Minimum", "Maximum", "Absolute" })
                    {
                        Vector3 native = (Vector3)CarrySupportField(actual, name), mono = (Vector3)CarrySupportField(expected, name);
                        for (int axis = 0; axis < 3; axis++)
                            if (BitConverter.SingleToInt32Bits(native[axis]) != BitConverter.SingleToInt32Bits(mono[axis]))
                                Fail(name + "[" + axis + "]: native=" + CarryFloatEvidence(native[axis]) + "; Mono=" + CarryFloatEvidence(mono[axis]));
                    }
                    foreach (string name in new[] { "MaximumCentredOffset", "MaximumAbsoluteDistance" })
                    {
                        double native = (double)CarrySupportField(actual, name), mono = (double)CarrySupportField(expected, name);
                        if (BitConverter.DoubleToInt64Bits(native) != BitConverter.DoubleToInt64Bits(mono))
                            Fail(name + ": native=" + native.ToString("G17", CultureInfo.InvariantCulture) +
                                "[" + BitConverter.DoubleToInt64Bits(native).ToString("X16") + "]; Mono=" +
                                mono.ToString("G17", CultureInfo.InvariantCulture) + "[" + BitConverter.DoubleToInt64Bits(mono).ToString("X16") + "]");
                    }
                    comparedNodes++;

                    void Fail(string field) => Assert.Fail(CarrySupportField(proxy, "Part") +
                        ": native plane-tree refit changed original node bits at node " + i + ", " + field + ".");
                }
                trees++;
            }
            Assert.That(trees, Is.GreaterThan(0), "The refit proof must exercise a live native plane score tree.");
            TestContext.Out.WriteLine("Body plane-tree refit: trees=" + trees + "; exact node float/double bits=" + comparedNodes);
        }

        private static bool ExhaustiveCarryNearestPlane(bool active, Vector3 minimum, Vector3 maximum,
            Plane[] planes, Vector3 point, out Vector3 normal, out float depth, out int planeIndex)
        {
            normal = Vector3.zero; depth = 0f; planeIndex = -1;
            if (!active || point.x < minimum.x || point.x > maximum.x ||
                point.y < minimum.y || point.y > maximum.y || point.z < minimum.z || point.z > maximum.z) return false;
            float nearest = float.NegativeInfinity;
            for (int i = 0; i < planes.Length; i++)
            {
                Plane plane = planes[i];
                float distance = plane.GetDistanceToPoint(point);
                if (distance >= 0f) return false;
                if (distance > nearest) { nearest = distance; normal = plane.normal; planeIndex = i; }
            }
            depth = -nearest;
            return true;
        }

        private static void AssertCarryRayBroadPhase(PlayerScarfBodyContacts contacts, Array proxies)
        {
            MethodInfo broadPhase = contacts.GetType().GetMethod("RayMayReachBounds", BindingFlags.Static | BindingFlags.NonPublic);
            MethodInfo beginNative = contacts.GetType().GetMethod("BeginNativeQueries", BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(broadPhase, Is.Not.Null); Assert.That(beginNative, Is.Not.Null);
            using var nativeScope = beginNative.Invoke(contacts, null) as IDisposable;
            Assert.That(nativeScope, Is.Not.Null, "The live sling must exercise the native ray query scope.");
            MethodInfo nativeClip = nativeScope.GetType().GetMethod("Clip", BindingFlags.Instance | BindingFlags.Public);
            MethodInfo nativeInterval = nativeScope.GetType().GetMethod("TryInterval", BindingFlags.Instance | BindingFlags.Public);
            Assert.That(nativeClip, Is.Not.Null); Assert.That(nativeInterval, Is.Not.Null);
            Vector3[] directions = { Vector3.right, Vector3.left, Vector3.up, Vector3.down,
                Vector3.forward, Vector3.back, new Vector3(1f, 1f, 1f).normalized,
                new Vector3(-1f, 1f, 1f).normalized, new Vector3(1f, -1f, .5f).normalized };
            float[] budgets = { .025f, .25f, float.PositiveInfinity };
            int hits = 0, misses = 0, rejected = 0, nativeHits = 0, nativeMisses = 0;
            var arguments = new object[4];
            object[] clipArguments = { Vector3.zero, Vector3.zero, null, float.PositiveInfinity };
            var intervalArguments = new object[3];
            for (int proxyIndex = 0; proxyIndex < proxies.Length; proxyIndex++)
            {
                object proxy = proxies.GetValue(proxyIndex);
                if (!(bool)CarrySupportField(proxy, "Active")) continue;
                var planes = CarrySupportField(proxy, "WorldPlanes") as Plane[];
                if (planes == null || planes.Length == 0) continue;
                Vector3 minimum = (Vector3)CarrySupportField(proxy, "RayMinimum");
                Vector3 maximum = (Vector3)CarrySupportField(proxy, "RayMaximum");
                Matrix4x4 toWorld = ((Matrix4x4)CarrySupportField(proxy, "RayWorldToBone")).inverse;
                Vector3 centre = (minimum + maximum) * .5f, half = (maximum - minimum) * .5f;
                Vector3[] origins = { centre, centre + Vector3.right * (half.x * .99f),
                    centre + Vector3.right * (half.x + .01f), centre - Vector3.right * (half.x + .01f),
                    centre + Vector3.up * (half.y + .01f), centre - Vector3.up * (half.y + .01f),
                    centre + Vector3.forward * (half.z + .01f), centre - Vector3.forward * (half.z + .01f) };
                arguments[0] = proxy;
                foreach (Vector3 localOrigin in origins)
                {
                    Vector3 origin = toWorld.MultiplyPoint3x4(localOrigin);
                    arguments[1] = origin;
                    foreach (Vector3 direction in directions)
                    {
                        bool interval = ExhaustiveCarryPlaneInterval(planes, origin, direction, out float entry, out float exit);
                        clipArguments[0] = origin; clipArguments[1] = direction;
                        nativeClip.Invoke(nativeScope, clipArguments);
                        intervalArguments[0] = proxyIndex; intervalArguments[1] = 0f; intervalArguments[2] = 0f;
                        bool nativeHit = (bool)nativeInterval.Invoke(nativeScope, intervalArguments);
                        float nativeEntry = (float)intervalArguments[1], nativeExit = (float)intervalArguments[2];
                        if (nativeHit != interval || interval &&
                            (BitConverter.SingleToInt32Bits(nativeEntry) != BitConverter.SingleToInt32Bits(entry) ||
                             BitConverter.SingleToInt32Bits(nativeExit) != BitConverter.SingleToInt32Bits(exit)))
                            Assert.Fail(CarrySupportField(proxy, "Part") + ": native ray differs from exhaustive float plane clipping. " +
                                "origin=" + origin.ToString("F6") + "; direction=" + direction.ToString("F6") +
                                "; reference/native hit=" + interval + "/" + nativeHit + "; entry=" + CarryFloatEvidence(entry) +
                                "/" + CarryFloatEvidence(nativeEntry) + "; exit=" + CarryFloatEvidence(exit) + "/" + CarryFloatEvidence(nativeExit));
                        if (interval) nativeHits++; else nativeMisses++;
                        arguments[2] = direction;
                        foreach (float budget in budgets)
                        {
                            bool referenceHit = interval && exit > 0f && entry <= budget;
                            arguments[3] = budget;
                            bool allowed = (bool)broadPhase.Invoke(null, arguments);
                            if (referenceHit) hits++; else misses++;
                            if (!allowed) rejected++;
                            // A broad phase may retain false positives, but it must never
                            // discard an old plane interval intersecting [0, travel budget].
                            if (referenceHit && !allowed)
                                Assert.Fail(CarrySupportField(proxy, "Part") + ": ray bounds lost a real plane hit. Origin=" +
                                    origin.ToString("F6") + "; direction=" + direction.ToString("F6") +
                                    "; entry/exit=" + entry + "/" + exit + "; budget=" + budget);
                        }
                    }
                }
            }
            Assert.That(hits, Is.GreaterThan(0), "The ray broad-phase proof must include real body intervals.");
            Assert.That(misses, Is.GreaterThan(0), "The ray proof must also exercise rays outside the body hulls.");
            Assert.That(rejected, Is.GreaterThan(0), "The optimized bounds must actually reject representative off-body rays.");
            Assert.That(nativeHits, Is.GreaterThan(0), "Native intervals must include real hull hits.");
            Assert.That(nativeMisses, Is.GreaterThan(0), "Native intervals must include off-body rays.");
            TestContext.Out.WriteLine("Body ray bounds: reference hits=" + hits + "; misses=" + misses + "; rejected=" + rejected +
                "; exact native intervals=" + nativeHits + "; native misses=" + nativeMisses);
        }

        private static bool ExhaustiveCarryPlaneInterval(Plane[] planes, Vector3 point, Vector3 direction,
            out float entry, out float exit)
        {
            entry = float.NegativeInfinity; exit = float.PositiveInfinity;
            foreach (Plane plane in planes)
            {
                float distance = plane.GetDistanceToPoint(point), slope = Vector3.Dot(plane.normal, direction);
                if (Mathf.Abs(slope) < .0000001f)
                { if (distance > 0f) return false; continue; }
                float crossing = -distance / slope;
                if (slope > 0f) exit = Mathf.Min(exit, crossing); else entry = Mathf.Max(entry, crossing);
                if (entry > exit) return false;
            }
            return exit > 0f;
        }

        private static string CarryFloatEvidence(float value) => value.ToString("G9", CultureInfo.InvariantCulture) +
            "[0x" + BitConverter.SingleToInt32Bits(value).ToString("X8", CultureInfo.InvariantCulture) + "]";

        private static object CarrySupportField(object owner, string name)
        {
            Assert.That(owner, Is.Not.Null);
            FieldInfo field = owner.GetType().GetField(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            Assert.That(field, Is.Not.Null, "The focused support proof cannot find " + owner.GetType().Name + "." + name);
            return field.GetValue(owner);
        }

        private sealed class CarryPerformanceBucket
        {
            private readonly string name;
            private readonly CarryPerformanceStage tick = new CarryPerformanceStage();
            internal readonly CarryPerformanceStage[] Stages = new CarryPerformanceStage[CarryPerformanceMarkers.Length];
            internal int FrameCount => tick.Values.Count;
            internal CarryPerformanceBucket(string name)
            {
                this.name = name;
                for (int i = 0; i < Stages.Length; i++) Stages[i] = new CarryPerformanceStage();
            }
            internal void Add(double milliseconds, ProfilerRecorderSample[] samples)
            {
                tick.Add(milliseconds, 1);
                for (int i = 0; i < Stages.Length; i++) Stages[i].Add(samples[i].Value * .000001d, samples[i].Count);
            }
            internal void AppendTo(StringBuilder text)
            {
                text.AppendLine(name + "; frames=" + FrameCount);
                tick.AppendTo(text, "Root.Tick");
                for (int i = 0; i < Stages.Length; i++) Stages[i].AppendTo(text, CarryPerformanceMarkers[i]);
            }
        }

        private sealed class CarryPerformanceStage
        {
            internal readonly List<double> Values = new List<double>(840);
            internal long Calls, MaximumCalls;
            internal double Total, Maximum;
            internal void Add(double milliseconds, long calls)
            {
                Values.Add(milliseconds); Total += milliseconds; Calls += calls;
                Maximum = Math.Max(Maximum, milliseconds); MaximumCalls = Math.Max(MaximumCalls, calls);
            }
            internal double Percentile(double fraction)
            {
                if (Values.Count == 0) return 0d;
                var sorted = new List<double>(Values); sorted.Sort();
                return sorted[Math.Max(0, (int)Math.Ceiling(sorted.Count * fraction) - 1)];
            }
            internal void AppendTo(StringBuilder text, string label) => text.AppendLine(string.Format(CultureInfo.InvariantCulture,
                "  {0}: p50={1:F4}ms; p95={2:F4}ms; max={3:F4}ms; sum={4:F4}ms; calls={5}; max calls/frame={6}",
                label, Percentile(.5), Percentile(.95), Maximum, Total, Calls, MaximumCalls));
        }
    }
}

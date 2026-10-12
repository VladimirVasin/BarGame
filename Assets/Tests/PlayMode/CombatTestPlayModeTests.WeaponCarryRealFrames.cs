using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text;
using NUnit.Framework;
using Unity.Profiling;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

namespace BarPromenade.Tests.PlayMode
{
    public sealed partial class CombatTestPlayModeTests
    {
        [UnityTest]
        public IEnumerator Range_WeaponCarryKeepsRealFramesBoundedThroughWalkingSwitchesAndHeadshot()
        {
            bool baseline = Array.IndexOf(Environment.GetCommandLineArgs(), "-weaponCarryRealFrameBaseline") >= 0;
            float captureDelta = Time.captureDeltaTime;
            int frameRate = Application.targetFrameRate, vSync = QualitySettings.vSyncCount;
            var input = new InputTestFixture();
            Keyboard keyboard = null;
            Mouse mouse = null;
            RealCarryFrameRecorder measurement = null;
            string journalDirectory = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "TestResults", "weapon-carry-real-frames-journal"));
            string journalSession = null;
            int measuredJournalRound = -1;
            AssertionException frontRouteFailure = null;
            var buckets = new List<RealCarryFrameBucket>();
            try
            {
                // The shared fixture normally captures fixed 60 Hz simulation.
                // This regression must retain the actual Update/LateUpdate clock.
                Time.captureDeltaTime = 0f;
                Application.targetFrameRate = 60;
                QualitySettings.vSyncCount = 0;
                input.Setup();
                keyboard = InputSystem.AddDevice<Keyboard>();
                mouse = InputSystem.AddDevice<Mouse>();
                yield return SceneManager.LoadSceneAsync(SceneIds.MainMenu, LoadSceneMode.Single);
                Assert.That(CombatTestStartService.TryStart(new CombatLoadoutSelection(CombatWeaponId.Shotgun, CombatWeaponId.Pistol)), Is.True);
                yield return AwaitSelectedCombatRange();
                PlacePair(8f);
                Assert.That(root.SetOpponentFocus(false), Is.True);
                root.SendMessage("OnApplicationFocus", true);
                GameInput.HandleApplicationFocus(true);
                root.SetDuelLogging(true, journalDirectory);
                Assert.That(root.JournalForDiagnostics, Is.Not.Null);
                journalSession = root.JournalForDiagnostics.SessionId;
                measuredJournalRound = root.JournalForDiagnostics.Round;
                root.AutomaticSimulation = true;
                measurement = new RealCarryFrameRecorder(root);
                long nativeSolvesBeforeFrames = PlayerJacketClothSurface.TotalNativeSolves;
                long burstSolvesBeforeFrames = PlayerJacketClothSurface.TotalBurstNativeSolves;
                long areaNativeBeforeFrames = PlayerJacketClothSurface.TotalNativeAreaSolves;
                long areaBurstBeforeFrames = PlayerJacketClothSurface.TotalBurstNativeAreaSolves;
                long hairNativeBeforeFrames = PlayerHairContacts.TotalNativeSolves;
                long hairBurstBeforeFrames = PlayerHairContacts.TotalBurstNativeSolves;
                long bodyNativeBoundsBeforeFrames = root.BodyEffects.ProxyNativeBoundsFolds;
                long bodyBurstBoundsBeforeFrames = root.BodyEffects.ProxyBurstBoundsFolds;
                long freeSurfaceMemoBeforeFrames = PlayerJacketClothSurface.TotalFreeSurfaceMemoHits;
                Assert.That(CombatShotgunSling.MaximumFrameSubsteps, Is.EqualTo(2));
                Assert.That(PlayerJacketCloth.MaximumFrameSubsteps, Is.EqualTo(3));
                Assert.That(PlayerHair.MaximumFrameSubsteps, Is.EqualTo(2));
                CombatActor hero = root.Hero;
                GameObject pistol = hero.CarriedWeapon(CombatWeaponId.Pistol), shotgun = hero.CarriedWeapon(CombatWeaponId.Shotgun);
                IFirearmState ammunition = hero.CarriedFirearm(CombatWeaponId.Pistol);
                int rounds = ammunition.Rounds;

                RealCarryFrameBucket startup = AddBucket("Initial real frames", false);
                yield return MeasureRealCarryFrames(measurement, startup, 20);
                CombatShotgunSling sling = shotgun.GetComponent<CombatShotgunSling>();
                Assert.That(sling, Is.Not.Null);
                int slingWorkspaces = sling.ProjectionWorkspaceAllocations;
                Assert.That(slingWorkspaces, Is.EqualTo(1), "Warm sling frames must share one retained projection workspace.");
                var slingContacts = (PlayerScarfBodyContacts)CarrySupportField(sling, "bodyContacts");
                Assert.That(slingContacts.SupportArenaCreateCount, Is.EqualTo(1));
                object supportWorkspace = CarrySupportField(slingContacts, "retainedSupportWork");
                Assert.That(supportWorkspace, Is.Not.Null);
                AssertConcealedRenderers(pistol, false);
                AssertPistolChestFit(hero);
                AssertShotgunFrontRoute(hero);
                yield return MeasureRealCarryFrames(measurement, AddBucket("Initial physical proof recovery", false), 5);
                AssertRealCarryPointQueryEquivalence(((Player3DCharacterPresentation)root.Player.Visual).Registry);
                AssertRealCarrySurfaceSkinFreshness(measurement.Jackets);
                yield return MeasureRealCarryFrames(measurement, AddBucket("Exact contact proof recovery", false), 5);
                Vector3 walkingStart = hero.transform.position;
                input.Press(keyboard.wKey, queueEventOnly: true);
                yield return MeasureRealCarryFrames(measurement, AddBucket("Walking with shotgun on the body", true), 100);
                Assert.That(Vector3.Distance(hero.transform.position, walkingStart), Is.GreaterThan(.3f),
                    "The performance scenario must move the live hero with gameplay input.");
                CaptureRealCarryViews("Test walking hem-ready");
                input.Release(keyboard.wKey, queueEventOnly: true);
                yield return MeasureRealCarryFrames(measurement, AddBucket("Camera capture recovery after walking", false), 5);

                input.Press(keyboard.sKey, queueEventOnly: true);
                yield return MeasureRealCarrySwitch(input, keyboard, CombatWeaponId.Shotgun, measurement,
                    AddBucket("Walking pistol to shotgun", true));
                input.Release(keyboard.sKey, queueEventOnly: true);
                yield return null;
                long nativeBeforeConcealmentProof = PlayerJacketClothSurface.TotalNativeSolves;
                long burstBeforeConcealmentProof = PlayerJacketClothSurface.TotalBurstNativeSolves;
                long areaNativeBeforeConcealmentProof = PlayerJacketClothSurface.TotalNativeAreaSolves;
                long areaBurstBeforeConcealmentProof = PlayerJacketClothSurface.TotalBurstNativeAreaSolves;
                AssertPistolConcealedByJacket(hero, settled: false);
                nativeSolvesBeforeFrames += PlayerJacketClothSurface.TotalNativeSolves - nativeBeforeConcealmentProof;
                burstSolvesBeforeFrames += PlayerJacketClothSurface.TotalBurstNativeSolves - burstBeforeConcealmentProof;
                areaNativeBeforeFrames += PlayerJacketClothSurface.TotalNativeAreaSolves - areaNativeBeforeConcealmentProof;
                areaBurstBeforeFrames += PlayerJacketClothSurface.TotalBurstNativeAreaSolves - areaBurstBeforeConcealmentProof;
                yield return MeasureRealCarryFrames(measurement, AddBucket("Concealment proof recovery", false), 5);

                input.Press(keyboard.wKey, queueEventOnly: true);
                yield return MeasureRealCarrySwitch(input, keyboard, CombatWeaponId.Pistol, measurement,
                    AddBucket("Walking shotgun to pistol", true));
                input.Release(keyboard.wKey, queueEventOnly: true);
                yield return null;
                try { AssertShotgunFrontRoute(hero); }
                catch (AssertionException error)
                {
                    // Retain this mandatory physical failure while collecting
                    // the remaining frame gates and independent contact proofs.
                    frontRouteFailure = error;
                    TestContext.Out.WriteLine("Deferred front-route failure: " + error.Message);
                }
                AssertConcealedRenderers(pistol, false);
                CaptureRealCarryViews("Shotgun Test real frame front-ready");
                yield return MeasureRealCarryFrames(measurement, AddBucket("Camera capture recovery after front route", false), 5);
                Assert.That(hero.CarriedWeapon(CombatWeaponId.Pistol), Is.SameAs(pistol));
                Assert.That(hero.CarriedWeapon(CombatWeaponId.Shotgun), Is.SameAs(shotgun));
                Assert.That(hero.CarriedFirearm(CombatWeaponId.Pistol), Is.SameAs(ammunition));
                Assert.That(ammunition.Rounds, Is.EqualTo(rounds), "Physical carry cannot spend or recreate ammunition.");

                // Restore a clear, standing target outside the measured frames.
                // The shot itself still goes through RMB/LMB, muzzle and flight.
                PlacePair(6f);
                Assert.That(root.SetOpponentFocus(false), Is.True);
                input.Press(mouse.rightButton, queueEventOnly: true);
                yield return null;
                yield return AimPistolCameraAt(input, mouse, FindPistolCameraTarget(Player3DAnatomicalPart.Head));
                AssertPistolCameraShotReady("Real-frame headshot");
                yield return MeasureRealCarryFrames(measurement, AddBucket("Live pistol aim before hit", true), 30);
                RealCarryFrameBucket impact = AddBucket("Lethal headshot and immediate effects", false);
                int impacts = root.Opponent.ReceivedImpactCount, spawns = root.Projectiles.SpawnCount;
                int shotRounds = hero.Pistol.Rounds;
                input.Press(mouse.leftButton, queueEventOnly: true);
                for (int frame = 0; frame < 90 && root.Opponent.ReceivedImpactCount == impacts; frame++)
                {
                    measurement.Begin();
                    yield return null;
                    measurement.End(impact);
                    if (frame == 0) input.Release(mouse.leftButton, queueEventOnly: true);
                }
                Assert.That(root.Projectiles.SpawnCount, Is.EqualTo(spawns + 1));
                Assert.That(hero.Pistol.Rounds, Is.EqualTo(shotRounds - 1));
                Assert.That(root.Opponent.ReceivedImpactCount, Is.EqualTo(impacts + 1));
                TestContext.Out.WriteLine("Real head contact observed after automatic Update at frame " + Time.frameCount +
                    "; the cold bucket retains that Tick and the following completed LateUpdate frames.");
                Assert.That(root.Opponent.LastImpact.Location.Region, Is.EqualTo(MeleeBodyRegion.Head));
                Assert.That(root.Opponent.State.IsDefeated && root.RoundFinished, Is.True);
                Assert.That(root.Opponent.IsRagdollActive, Is.True);
                Assert.That(root.HeadEffects.DetachedSectorCountFor(root.Opponent), Is.GreaterThan(0),
                    "The lethal transaction must publish its real head destruction on the contact frame.");
                Assert.That(root.BloodEffects.ProjectileWoundCountFor(root.Opponent), Is.EqualTo(1));
                yield return MeasureRealCarryFrames(measurement, impact, 12);
                yield return MeasureRealCarryFrames(measurement, AddBucket("Post-death live frames", true), 120);
                input.Release(mouse.rightButton, queueEventOnly: true);
                yield return null;
                yield return MeasureRealCarrySwitch(input, keyboard, CombatWeaponId.Shotgun, measurement,
                    AddBucket("Post-death pistol to shotgun", true));
                Vector3 diagonalStart = hero.transform.position;
                input.Press(keyboard.wKey, queueEventOnly: true);
                input.Press(keyboard.aKey, queueEventOnly: true);
                yield return MeasureRealCarryFrames(measurement, AddBucket("Post-death diagonal walking with shotgun", true), 120);
                input.Release(keyboard.wKey, queueEventOnly: true);
                input.Release(keyboard.aKey, queueEventOnly: true);
                Assert.That(Vector3.Distance(hero.transform.position, diagonalStart), Is.GreaterThan(.3f));
                Assert.That(hero.WeaponId, Is.EqualTo(CombatWeaponId.Shotgun));
                Assert.That(hero.Firearm.AimProgress, Is.Zero, "The fresh-log walking reproduction must retain its unaimed shotgun pose.");
                Assert.That(root.Opponent.State.Health, Is.Zero);
                Assert.That(sling.ProjectionWorkspaceAllocations, Is.EqualTo(slingWorkspaces),
                    "Walking, weapon switches and the lethal contact must reuse the sling workspace.");
                Assert.That(slingContacts.SupportArenaCreateCount, Is.EqualTo(1));
                Assert.That(CarrySupportField(slingContacts, "retainedSupportWork"), Is.SameAs(supportWorkspace),
                    "Live poses must reuse support scratch buffers through walking, transfers and impact.");
                long gameplayBodyNativeBounds = root.BodyEffects.ProxyNativeBoundsFolds - bodyNativeBoundsBeforeFrames;
                long gameplayBodyBurstBounds = root.BodyEffects.ProxyBurstBoundsFolds - bodyBurstBoundsBeforeFrames;
                TestContext.Out.WriteLine("Gameplay body proxy bound folds: native=" + gameplayBodyNativeBounds +
                    "; Burst=" + gameplayBodyBurstBounds);
                Assert.That(gameplayBodyNativeBounds, Is.GreaterThan(0), "The actual timed damage must exercise native body proxy bounds before diagnostics.");
                Assert.That(gameplayBodyBurstBounds, Is.GreaterThan(0), "The actual timed body proxy bound folds must execute in Burst.");

                // Independent correctness and artificial hitch probes are kept
                // outside all CPU buckets, including their allocations/baking.
                AssertRealCarryFreeSurfaceMemoEquivalence(measurement.Jackets, freeSurfaceMemoBeforeFrames);
                AssertRealCarryNativeAreaEquivalence(measurement.Jackets, areaNativeBeforeFrames, areaBurstBeforeFrames);
                AssertRealCarryNativeSurfaceEquivalence(measurement.Jackets, nativeSolvesBeforeFrames, burstSolvesBeforeFrames);
                AssertRealCarryNativeHairContactEquivalence(measurement.Hairs, hairNativeBeforeFrames, hairBurstBeforeFrames);
                AssertRealCarryHitchBounds(measurement.Jackets, measurement.Hairs);
                int finishedSteps = root.FinishedRoundSubsteps;
                root.TickFrame(.20f);
                Assert.That(root.FinishedRoundSubsteps - finishedSteps, Is.InRange(1, 4),
                    "A real frame hitch must also bound finished-round combat/effects work.");
                AssertCarrySupportExtrema(hero);
                foreach (PlayerJacketCloth jacket in measurement.Jackets)
                    if (jacket.HasAuthoredBindings)
                    {
                        var jacketContacts = (PlayerScarfBodyContacts)CarrySupportField(jacket, "contacts");
                        Assert.That(jacketContacts.SupportArenaCreateCount, Is.EqualTo(1));
                        AssertCarrySupportExtrema(jacketContacts, nativePose: false);
                    }
                AssertRealCarrySharedClothSnapshot();
                AssertBodyProxyCache();
                var flush = root.JournalForDiagnostics.FlushAsync();
                yield return WaitFor(() => flush.IsCompleted, "Real-frame cloth journal did not flush.");
                Assert.That(flush.IsFaulted, Is.False);
                AssertRealCarryJournal(journalDirectory, journalSession, measuredJournalRound);
                if (!baseline) Assert.That(impact.Wall.Maximum, Is.LessThanOrEqualTo(100d),
                    "The first lethal headshot and its immediate effects must not stall a real frame.");
                foreach (RealCarryFrameBucket bucket in buckets)
                {
                    bucket.AssertPhysicalBounds();
                    if (!baseline && bucket.Warm)
                    {
                        Assert.That(bucket.Wall.Percentile(.95), Is.LessThanOrEqualTo(33.4d), bucket.Name + ": actual p95 frame wall time must sustain at least 30 FPS at target 60.");
                        Assert.That(bucket.Wall.Maximum, Is.LessThanOrEqualTo(100d), bucket.Name + ": a normal frame cannot contain a severe stall.");
                        if (bucket.Recorded[0].Values.Count > 0)
                        {
                            Assert.That(bucket.Recorded[0].Percentile(.95), Is.LessThanOrEqualTo(33.4d), bucket.Name + ": the full main thread must remain bounded, including other game systems.");
                            Assert.That(bucket.Recorded[0].Maximum, Is.LessThanOrEqualTo(100d), bucket.Name + ": the main thread cannot hide a normal-frame stall.");
                        }
                        Assert.That(bucket.Cloth.Percentile(.95), Is.LessThanOrEqualTo(16.7d),
                            bucket.Name + ": secondary motion must fit within half of the 30 FPS frame budget; the full-frame gate remains mandatory.");
                    }
                }
                if (frontRouteFailure != null)
                    System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(frontRouteFailure).Throw();

                RealCarryFrameBucket AddBucket(string name, bool warm)
                {
                    var bucket = new RealCarryFrameBucket(name, warm);
                    buckets.Add(bucket);
                    return bucket;
                }
            }
            finally
            {
                try { WriteRealCarryReport(measurement, buckets, baseline); }
                finally
                {
                    measurement?.Dispose();
                    if (root != null) { root.AutomaticSimulation = false; root.SetDuelLogging(false); }
                    if (mouse != null && mouse.added) InputSystem.RemoveDevice(mouse);
                    if (keyboard != null && keyboard.added) InputSystem.RemoveDevice(keyboard);
                    try { input.TearDown(); }
                    finally
                    {
                        Time.captureDeltaTime = captureDelta;
                        Application.targetFrameRate = frameRate;
                        QualitySettings.vSyncCount = vSync;
                    }
                }
            }
        }

        private static void WriteRealCarryReport(RealCarryFrameRecorder measurement, List<RealCarryFrameBucket> buckets, bool baseline)
        {
            var report = new StringBuilder();
            report.AppendLine("Weapon carry real frames; baseline=" + baseline + "; automatic simulation; captureDeltaTime=0; target=60; vSync=0.");
            report.AppendLine("Wall includes the complete coroutine-to-coroutine frame. Main Thread/GC are recorded only with real samples; PlayerLoop is a diagnostic marker that may cover only part of the loop. Cloth clocks include every live jacket/hair and scarf plus sling, not just manual Tick.");
            report.AppendLine("Startup and lethal-contact work are separate published buckets; camera captures and independent proofs are outside measurement. Cloth markers are nested and must not be summed.");
            measurement?.AppendAvailability(report);
            foreach (RealCarryFrameBucket bucket in buckets) bucket.AppendTo(report);
            string path = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "TestResults", "weapon-carry-real-frames.txt"));
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            File.WriteAllText(path, report.ToString());
            var frames = new StringBuilder("phase,update_frame,completed_cloth_frame,wall_ms,tracked_work_ms,cloth_ms,jacket_ms,hair_ms,scarf_ms,sling_ms,main_thread_ms,player_loop_marker_ms,gc_bytes\n");
            foreach (RealCarryFrameBucket bucket in buckets) foreach (RealCarryFrameSample sample in bucket.Samples)
                frames.AppendFormat(CultureInfo.InvariantCulture,
                    "{0},{1},{2},{3:F5},{4:F5},{5:F5},{6:F5},{7:F5},{8:F5},{9:F5},{10:F5},{11:F5},{12}\n",
                    bucket.Name, sample.UpdateFrame, sample.UpdateFrame - 1, sample.Wall, sample.TrackedWork,
                    sample.Cloth, sample.Jacket, sample.Hair, sample.Scarf, sample.Sling, sample.MainThread, sample.PlayerLoop, sample.GcBytes);
            File.WriteAllText(Path.ChangeExtension(path, ".csv"), frames.ToString());
            TestContext.Out.WriteLine(report.ToString());
        }

        private static IEnumerator MeasureRealCarryFrames(RealCarryFrameRecorder recorder, RealCarryFrameBucket bucket, int frames)
        {
            for (int frame = 0; frame < frames; frame++)
            {
                recorder.Begin();
                yield return null;
                recorder.End(bucket);
            }
        }

        private void CaptureRealCarryViews(string name)
        {
            // Preserve the live walking cloth pose; the distant sparring target
            // must not cover the hero in these inspection-only camera angles.
            Renderer[] renderers = root.Opponent.GetComponentsInChildren<Renderer>(true);
            var enabled = new bool[renderers.Length];
            try
            {
                for (int i = 0; i < renderers.Length; i++)
                {
                    enabled[i] = renderers[i].enabled;
                    renderers[i].enabled = false;
                }
                CaptureWeaponCarryViews(name, true);
            }
            finally
            {
                for (int i = 0; i < renderers.Length; i++)
                    if (renderers[i] != null) renderers[i].enabled = enabled[i];
            }
        }

        private IEnumerator MeasureRealCarrySwitch(InputTestFixture input, Keyboard keyboard, CombatWeaponId target,
            RealCarryFrameRecorder recorder, RealCarryFrameBucket bucket)
        {
            var key = target == CombatWeaponId.Pistol ? keyboard.digit1Key : keyboard.digit2Key;
            input.Press(key, queueEventOnly: true);
            for (int frame = 0; frame < 600; frame++)
            {
                recorder.Begin();
                yield return null;
                recorder.End(bucket);
                if (frame == 0) input.Release(key, queueEventOnly: true);
                if (frame > 0 && !root.Hero.IsSwitchingWeapon && root.Hero.HasWeaponInHand && root.Hero.WeaponId == target) break;
            }
            Assert.That(root.Hero.IsSwitchingWeapon, Is.False, "The real-clock weapon switch did not reach its authored endpoint.");
            Assert.That(root.Hero.HasWeaponInHand && root.Hero.WeaponId == target, Is.True);
            Assert.That(bucket.Wall.Values.Count, Is.GreaterThan(30), "A real switch must sample its visible timeline.");
            yield return MeasureRealCarryFrames(recorder, bucket, 12);
        }

        private sealed class RealCarryFrameRecorder : IDisposable
        {
            internal readonly PlayerJacketCloth[] Jackets = Object.FindObjectsByType<PlayerJacketCloth>(FindObjectsInactive.Exclude);
            internal readonly PlayerHair[] Hairs = Object.FindObjectsByType<PlayerHair>(FindObjectsInactive.Exclude);
            private readonly PlayerScarfPresentation[] scarves = Object.FindObjectsByType<PlayerScarfPresentation>(FindObjectsInactive.Exclude);
            private readonly CombatShotgunSling sling;
            private readonly CombatTestRoot root;
            private readonly ProfilerRecorder[] recorders;
            private readonly long[] observedCalls;
            private static readonly string[] MarkerNames =
            {
                "Main Thread", "PlayerLoop", "GC Allocated In Frame", "WaitForTargetFPS",
                "BarPromenade.SecondaryMotion.Jacket.Apply", "BarPromenade.SecondaryMotion.Jacket.Pose", "BarPromenade.SecondaryMotion.Jacket.Surface",
                "BarPromenade.SecondaryMotion.Hair.Apply", "BarPromenade.SecondaryMotion.Hair.Pose", "BarPromenade.SecondaryMotion.Hair.Constrain",
                "BarPromenade.SecondaryMotion.Jacket.Constrain", "BarPromenade.SecondaryMotion.Jacket.Material",
                "BarPromenade.SecondaryMotion.Jacket.Area", "BarPromenade.SecondaryMotion.Jacket.Write",
                "BarPromenade.SecondaryMotion.Jacket.FreeSurface", "BarPromenade.SecondaryMotion.Jacket.NativeSolver",
                "BarPromenade.SecondaryMotion.Hair.NativeContacts", "BarPromenade.SecondaryMotion.Jacket.NativeArea",
                "BarPromenade.WeaponCarry.Sling.Pose", "BarPromenade.WeaponCarry.Sling.Solve",
                "BarPromenade.WeaponCarry.Sling.Projection.Workspace", "BarPromenade.WeaponCarry.BodyContacts.Ray.Workspace"
            };
            private long began, jacketTicks, hairTicks, scarfTicks, slingTicks, slingCalls, jacketCalls, hairCalls, simulationTicks, latePoseTicks;
            internal RealCarryFrameRecorder(CombatTestRoot owner)
            {
                root = owner;
                sling = owner.Hero.CarriedWeapon(CombatWeaponId.Shotgun).GetComponent<CombatShotgunSling>();
                recorders = new ProfilerRecorder[MarkerNames.Length];
                observedCalls = new long[recorders.Length];
                for (int i = 0; i < recorders.Length; i++)
                    recorders[i] = ProfilerRecorder.StartNew(i == 2 ? ProfilerCategory.Memory : i < 4 ? ProfilerCategory.Internal : ProfilerCategory.Scripts,
                        MarkerNames[i], 1, ProfilerRecorderOptions.Default | ProfilerRecorderOptions.SumAllSamplesInFrame |
                        (i >= 4 ? ProfilerRecorderOptions.CollectOnlyOnCurrentThread : (ProfilerRecorderOptions)0));
                Assert.That(Jackets, Is.Not.Empty); Assert.That(Hairs, Is.Not.Empty);
            }
            internal void Begin()
            {
                jacketTicks = hairTicks = scarfTicks = jacketCalls = hairCalls = 0L;
                foreach (PlayerJacketCloth cloth in Jackets) { jacketTicks += cloth.TotalApplyTicks; jacketCalls += cloth.TotalApplyCalls; }
                foreach (PlayerHair hair in Hairs) { hairTicks += hair.TotalApplyTicks; hairCalls += hair.TotalApplyCalls; }
                foreach (PlayerScarfPresentation scarf in scarves) scarfTicks += scarf.TotalGeometryTicks;
                slingTicks = sling.TotalPresentTicks; slingCalls = sling.TotalPresentCalls;
                simulationTicks = root.TotalSimulationTicks; latePoseTicks = root.TotalLatePoseTicks;
                foreach (ProfilerRecorder recorder in recorders) if (recorder.Valid) { recorder.Reset(); recorder.Start(); }
                began = Stopwatch.GetTimestamp();
            }
            internal void End(RealCarryFrameBucket bucket)
            {
                long ended = Stopwatch.GetTimestamp(), jacket = -jacketTicks, hair = -hairTicks, scarf = -scarfTicks;
                long jacketCount = -jacketCalls, hairCount = -hairCalls;
                foreach (PlayerJacketCloth cloth in Jackets)
                {
                    jacket += cloth.TotalApplyTicks; jacketCount += cloth.TotalApplyCalls;
                    bucket.MaximumJacketSteps = Math.Max(bucket.MaximumJacketSteps, cloth.LastSimulationSteps);
                }
                foreach (PlayerHair item in Hairs)
                {
                    hair += item.TotalApplyTicks; hairCount += item.TotalApplyCalls;
                    bucket.MaximumHairSteps = Math.Max(bucket.MaximumHairSteps, item.LastSimulationSteps);
                }
                foreach (PlayerScarfPresentation item in scarves) scarf += item.TotalGeometryTicks;
                long slingElapsed = sling.TotalPresentTicks - slingTicks, slingCount = sling.TotalPresentCalls - slingCalls;
                var evidence = new RealCarryFrameSample
                {
                    UpdateFrame = Time.frameCount, Wall = Milliseconds(ended - began),
                    Cloth = Milliseconds(jacket + hair + scarf + slingElapsed),
                    TrackedWork = Milliseconds(jacket + hair + scarf + slingElapsed +
                        root.TotalSimulationTicks - simulationTicks + root.TotalLatePoseTicks - latePoseTicks),
                    Jacket = Milliseconds(jacket), Hair = Milliseconds(hair), Scarf = Milliseconds(scarf), Sling = Milliseconds(slingElapsed),
                    MainThread = -1d, PlayerLoop = -1d, GcBytes = -1L
                };
                bucket.Wall.Add(evidence.Wall, 1); bucket.Cloth.Add(evidence.Cloth, 1); bucket.TrackedWork.Add(evidence.TrackedWork, 1);
                bucket.Jacket.Add(evidence.Jacket, jacketCount); bucket.Hair.Add(evidence.Hair, hairCount);
                bucket.Scarf.Add(evidence.Scarf, 1); bucket.Sling.Add(evidence.Sling, slingCount);
                bucket.MaximumSlingSteps = Math.Max(bucket.MaximumSlingSteps, sling.LastSimulationSteps);
                bucket.MaterialError = Mathf.Max(bucket.MaterialError, Mathf.Abs(sling.PresentedLength - sling.AuthoredLength));
                for (int i = 0; i < recorders.Length; i++)
                    if (recorders[i].Valid && recorders[i].Count > 0)
                    {
                        ProfilerRecorderSample sample = recorders[i].GetSample(recorders[i].Count - 1);
                        if (sample.Count <= 0) continue;
                        observedCalls[i] += sample.Count;
                        bucket.Recorded[i].Add(i == 2 ? sample.Value : sample.Value * .000001d, sample.Count);
                        if (i == 0) evidence.MainThread = sample.Value * .000001d;
                        else if (i == 1) evidence.PlayerLoop = sample.Value * .000001d;
                        else if (i == 2) evidence.GcBytes = sample.Value;
                    }
                bucket.Samples.Add(evidence);
            }
            internal void AppendAvailability(StringBuilder report)
            {
                for (int i = 0; i < recorders.Length; i++) report.AppendLine(MarkerNames[i] + ": available=" + recorders[i].Valid +
                    "; has_samples=" + (observedCalls[i] > 0) + "; observed_calls=" + observedCalls[i]);
            }
            public void Dispose() { for (int i = 0; i < recorders.Length; i++) recorders[i].Dispose(); }
            private static double Milliseconds(long ticks) => ticks * 1000d / Stopwatch.Frequency;
            internal static string MarkerName(int index) => MarkerNames[index];
            internal static int MarkerCount => MarkerNames.Length;
        }

        private sealed class RealCarryFrameBucket
        {
            internal readonly string Name;
            internal readonly bool Warm;
            internal readonly CarryPerformanceStage Wall = new CarryPerformanceStage(), Cloth = new CarryPerformanceStage();
            internal readonly CarryPerformanceStage TrackedWork = new CarryPerformanceStage();
            internal readonly CarryPerformanceStage Jacket = new CarryPerformanceStage(), Hair = new CarryPerformanceStage();
            internal readonly CarryPerformanceStage Scarf = new CarryPerformanceStage(), Sling = new CarryPerformanceStage();
            internal readonly CarryPerformanceStage[] Recorded = new CarryPerformanceStage[RealCarryFrameRecorder.MarkerCount];
            internal readonly List<RealCarryFrameSample> Samples = new List<RealCarryFrameSample>(840);
            internal int MaximumJacketSteps, MaximumHairSteps, MaximumSlingSteps;
            internal float MaterialError;
            internal RealCarryFrameBucket(string name, bool warm)
            {
                Name = name; Warm = warm;
                for (int i = 0; i < Recorded.Length; i++) Recorded[i] = new CarryPerformanceStage();
            }
            internal void AssertPhysicalBounds()
            {
                Assert.That(MaximumJacketSteps, Is.LessThanOrEqualTo(PlayerJacketCloth.MaximumFrameSubsteps), Name);
                Assert.That(MaximumHairSteps, Is.LessThanOrEqualTo(PlayerHair.MaximumFrameSubsteps), Name);
                Assert.That(MaximumSlingSteps, Is.LessThanOrEqualTo(CombatShotgunSling.MaximumFrameSubsteps), Name);
                Assert.That(Sling.MaximumCalls, Is.LessThanOrEqualTo(1L), Name + ": sling presentation is once per rendered frame.");
                Assert.That(MaterialError, Is.LessThan(.03f), Name + ": the measured physical strap cannot stretch to buy frame time.");
            }
            internal void AppendTo(StringBuilder report)
            {
                report.AppendLine(Name + "; warm=" + Warm + "; frames=" + Wall.Values.Count);
                if (Wall.Values.Count == 0) return;
                Wall.AppendTo(report, "Full frame wall"); Cloth.AppendTo(report, "All secondary motion CPU");
                TrackedWork.AppendTo(report, "Tracked game work CPU (excludes input, journal writes, joint deformation and other Updates)");
                Jacket.AppendTo(report, "All jacket CPU"); Hair.AppendTo(report, "All hair CPU");
                Scarf.AppendTo(report, "All scarf geometry CPU"); Sling.AppendTo(report, "Sling CPU");
                for (int i = 0; i < Recorded.Length; i++) if (Recorded[i].Values.Count > 0)
                {
                    if (i == 2) report.AppendLine(string.Format(CultureInfo.InvariantCulture,
                        "GC Allocated In Frame: p50={0:F0}B; p95={1:F0}B; max={2:F0}B; total={3:F0}B",
                        Recorded[i].Percentile(.5), Recorded[i].Percentile(.95), Recorded[i].Maximum, Recorded[i].Total));
                    else Recorded[i].AppendTo(report, RealCarryFrameRecorder.MarkerName(i) + (i == 1 ? " (diagnostic marker only)" : ""));
                }
                report.AppendLine(string.Format(CultureInfo.InvariantCulture, "Maximum steps jacket/hair/sling={0}/{1}/{2}; maximum material error={3:F6}m", MaximumJacketSteps, MaximumHairSteps, MaximumSlingSteps, MaterialError));
            }
        }

        private struct RealCarryFrameSample
        {
            internal int UpdateFrame;
            internal double Wall, TrackedWork, Cloth, Jacket, Hair, Scarf, Sling, MainThread, PlayerLoop;
            internal long GcBytes;
        }

        private static void AssertRealCarryHitchBounds(PlayerJacketCloth[] jackets, PlayerHair[] hairs)
        {
            var wind = new WindSample(0f, 0f);
            foreach (PlayerJacketCloth cloth in jackets)
            {
                cloth.RequestReset(); cloth.ApplyAt(0d, false, false, wind);
                int resets = cloth.ResetCount;
                cloth.ApplyAt(.20d, false, false, wind);
                Assert.That(cloth.LastSimulationSteps, Is.EqualTo(PlayerJacketCloth.MaximumFrameSubsteps));
                Assert.That(cloth.LastDiscardedSeconds, Is.GreaterThan(.17d));
                Assert.That(cloth.ResetCount, Is.EqualTo(resets), "A bounded hitch must retain cosmetic history.");
                cloth.ApplyAt(.20d + 1d / 120d, false, false, wind);
                Assert.That(cloth.LastSimulationSteps, Is.EqualTo(1), "Discarded wall time must not become next-frame backlog.");
                Assert.That(cloth.LastDiscardedSeconds, Is.EqualTo(0d).Within(.0000001d));
                for (int i = 0; i < PlayerJacketCloth.NodeCount; i++) AssertRealCarryFinite(cloth.WorldPoint(i));
                cloth.RequestReset();
            }
            foreach (PlayerHair hair in hairs)
            {
                hair.RequestReset(); hair.ApplyAt(0d, false, false, wind);
                int resets = hair.ResetCount;
                hair.ApplyAt(.20d, false, false, wind);
                Assert.That(hair.LastSimulationSteps, Is.EqualTo(PlayerHair.MaximumFrameSubsteps));
                Assert.That(hair.LastDiscardedSeconds, Is.GreaterThan(.17d));
                Assert.That(hair.ResetCount, Is.EqualTo(resets));
                hair.ApplyAt(.20d + 1d / 120d, false, false, wind);
                Assert.That(hair.LastSimulationSteps, Is.EqualTo(1));
                Assert.That(hair.LastDiscardedSeconds, Is.EqualTo(0d).Within(.0000001d));
                Assert.That(hair.MaximumLengthError, Is.LessThan(.01f));
                for (int i = 0; i < PlayerHair.PointCount; i++) AssertRealCarryFinite(hair.WorldPoint(i));
                hair.RequestReset();
            }
        }

        private static void AssertRealCarryFinite(Vector3 point) => Assert.That(
            float.IsNaN(point.x + point.y + point.z) || float.IsInfinity(point.x) || float.IsInfinity(point.y) || float.IsInfinity(point.z), Is.False);

        private static void AssertRealCarryFreeSurfaceMemoEquivalence(PlayerJacketCloth[] jackets, long hitsBeforeFrames)
        {
            long gameplayHits = PlayerJacketClothSurface.TotalFreeSurfaceMemoHits - hitsBeforeFrames;
            TestContext.Out.WriteLine("Gameplay free jacket surface memo hits=" + gameplayHits);
            Assert.That(gameplayHits, Is.GreaterThan(0), "Live free panels must reuse a real unchanged false query before diagnostics.");
            int compared = 0;
            var comparedVariants = new int[5];
            foreach (PlayerJacketCloth cloth in jackets)
            {
                var surfaces = (PlayerJacketClothSurface[])CarrySupportField(cloth, "surfaces");
                var contacts = (PlayerScarfBodyContacts)CarrySupportField(cloth, "contacts");
                var equipment = (PlayerEquipmentContacts)CarrySupportField(contacts, "equipment");
                for (int surface = 0; surface < surfaces.Length; surface++)
                {
                    for (int variant = 0; variant < 5; variant++)
                    {
                        Vector3 originalOffset = variant == 2 ? Vector3.right * .001f : Vector3.zero;
                        Vector3 inputOffset = variant == 1 ? Vector3.right * .001f : Vector3.zero;
                        Action invalidate = variant == 3 ? (Action)contacts.UpdatePose :
                            variant == 4 && equipment != null ? (Action)equipment.UpdatePose : null;
                        if (variant == 4 && equipment == null) continue;
                        if (!cloth.TryCompareFreeSurfaceMemoForDiagnostics(surface, originalOffset, inputOffset, invalidate,
                            out Vector3[] reference, out Vector3[] cached, out bool referenceResult,
                            out bool cachedResult, out long reuseDelta)) continue;
                        string context = "Free jacket panel " + surface + ": memo variant " + variant;
                        Assert.That(cachedResult, Is.EqualTo(referenceResult), context + " must preserve the independent result.");
                        Assert.That(cached.Length, Is.EqualTo(reference.Length));
                        for (int vertex = 0; vertex < reference.Length; vertex++)
                        {
                            AssertRealCarryFinite(reference[vertex]); AssertRealCarryFinite(cached[vertex]);
                            for (int axis = 0; axis < 3; axis++)
                                Assert.That(BitConverter.SingleToInt32Bits(cached[vertex][axis]),
                                    Is.EqualTo(BitConverter.SingleToInt32Bits(reference[vertex][axis])),
                                    context + " must preserve current geometry bits.");
                        }
                        if (variant == 0)
                        {
                            Assert.That(referenceResult, Is.False, context + " must exercise an unchanged false result.");
                            Assert.That(reuseDelta, Is.GreaterThan(0), context + " must use the memo.");
                        }
                        else Assert.That(reuseDelta, Is.Zero, context + " must invalidate changed input, original geometry or contacts.");
                        compared++; comparedVariants[variant]++;
                    }
                }
            }
            Assert.That(compared, Is.GreaterThan(0), "The false-query memo comparison must inspect a real free panel.");
            for (int variant = 0; variant < comparedVariants.Length; variant++)
                Assert.That(comparedVariants[variant], Is.GreaterThan(0), "The memo proof must exercise geometry and contact invalidation variant " + variant + ".");
        }

        private static void AssertRealCarryNativeAreaEquivalence(PlayerJacketCloth[] jackets,
            long nativeBeforeFrames, long burstBeforeFrames)
        {
            long gameplayNative = PlayerJacketClothSurface.TotalNativeAreaSolves - nativeBeforeFrames;
            long gameplayBurst = PlayerJacketClothSurface.TotalBurstNativeAreaSolves - burstBeforeFrames;
            TestContext.Out.WriteLine("Gameplay jacket area solves: native=" + gameplayNative + "; Burst=" + gameplayBurst);
            Assert.That(gameplayNative, Is.GreaterThan(0), "Live free panels must exercise the area batch before diagnostics.");
            Assert.That(gameplayBurst, Is.GreaterThan(0), "Live area batches must actually execute in Burst.");
            int ordinary = 0, deepFold = 0;
            foreach (PlayerJacketCloth cloth in jackets)
            {
                var surfaces = (PlayerJacketClothSurface[])CarrySupportField(cloth, "surfaces");
                var bindings = (PlayerJacketCloth.SurfaceBinding[])CarrySupportField(cloth, "bindings");
                for (int surface = 0; surface < surfaces.Length; surface++)
                {
                    if (bindings[surface].Region != 0 || surfaces[surface].IsHemPanel) continue;
                    for (int variant = 0; variant < 2; variant++)
                    {
                        bool constrainMaterial = variant == 0;
                        int areaPasses = constrainMaterial ? 2 : 4;
                        if (!cloth.TryCompareAreaSolverForDiagnostics(surface, constrainMaterial, areaPasses,
                            out Vector3[] managed, out Vector3[] native, out int managedContacts,
                            out int nativeContacts, out bool executedInBurst)) continue;
                        string name = bindings[surface].Renderer.name + (constrainMaterial ? " ordinary area" : " deep-fold area passes");
                        Assert.That(executedInBurst, Is.True, name + ": the comparison must run the real Burst area batch.");
                        Assert.That(native.Length, Is.EqualTo(managed.Length));
                        float maximumDrift = 0f; int changedBits = 0;
                        for (int vertex = 0; vertex < managed.Length; vertex++)
                        {
                            AssertRealCarryFinite(managed[vertex]); AssertRealCarryFinite(native[vertex]);
                            maximumDrift = Mathf.Max(maximumDrift, Vector3.Distance(managed[vertex], native[vertex]));
                            for (int axis = 0; axis < 3; axis++)
                                if (BitConverter.SingleToInt32Bits(managed[vertex][axis]) !=
                                    BitConverter.SingleToInt32Bits(native[vertex][axis])) changedBits++;
                        }
                        TestContext.Out.WriteLine(name + ": managed/native contacts=" + managedContacts + "/" + nativeContacts +
                            "; changed component bits=" + changedBits + "; maximum drift m=" + maximumDrift.ToString("G9", CultureInfo.InvariantCulture));
                        Assert.That(nativeContacts, Is.EqualTo(managedContacts), name + ": original area correction counts must remain unchanged.");
                        Assert.That(maximumDrift, Is.LessThanOrEqualTo(.00001f), name + ": identical area inputs may differ by at most 10 micrometres.");
                        if (constrainMaterial) ordinary++; else deepFold++;
                    }
                }
            }
            Assert.That(ordinary, Is.GreaterThan(0), "The comparison must inspect actual free jacket panels.");
            Assert.That(deepFold, Is.GreaterThan(0), "The original four-pass deep-fold area contract must also be compared.");
        }

        private static void AssertRealCarryNativeHairContactEquivalence(PlayerHair[] hairs,
            long nativeBeforeFrames, long burstBeforeFrames)
        {
            long gameplayNative = PlayerHairContacts.TotalNativeSolves - nativeBeforeFrames;
            long gameplayBurst = PlayerHairContacts.TotalBurstNativeSolves - burstBeforeFrames;
            TestContext.Out.WriteLine("Gameplay hair contact solves: native=" + gameplayNative + "; Burst=" + gameplayBurst);
            Assert.That(gameplayNative, Is.GreaterThan(0), "Live hair must exercise the batched contact solver before diagnostics.");
            Assert.That(gameplayBurst, Is.GreaterThan(0), "Live hair contacts must actually execute in Burst.");
            int compared = 0;
            foreach (PlayerHair hair in hairs)
            {
                var input = new Vector3[PlayerHair.PointCount];
                for (int i = 0; i < input.Length; i++) input[i] = hair.WorldPoint(i);
                Assert.That(hair.TryCompareContactSolverForDiagnostics(out Vector3[] managed, out Vector3[] native,
                    out int managedContacts, out int nativeContacts, out bool executedInBurst), Is.True);
                Compare("Live hair points", input, managed, native, managedContacts, nativeContacts, executedInBurst);
                var contacts = (PlayerHairContacts)CarrySupportField(hair, "contacts");
                var body = (PlayerScarfBodyContacts)CarrySupportField(contacts, "body");
                bool foundHead = false;
                foreach (object proxy in (Array)CarrySupportField(body, "proxies"))
                {
                    if (!(bool)CarrySupportField(proxy, "Active") ||
                        (Player3DAnatomicalPart)CarrySupportField(proxy, "Part") != Player3DAnatomicalPart.Head) continue;
                    Vector3 minimum = (Vector3)CarrySupportField(proxy, "WorldMinimum");
                    Vector3 maximum = (Vector3)CarrySupportField(proxy, "WorldMaximum");
                    Vector3 penetrating = (minimum + maximum) * .5f, probe = penetrating;
                    Assert.That(body.ResolvePoint(ref probe), Is.True,
                        "The hair regression must exercise a real penetrating free point, not only clear live points.");
                    input[1] = penetrating; // Every chain's root remains unchanged.
                    foundHead = true; break;
                }
                Assert.That(foundHead, Is.True);
                Assert.That(contacts.TryCompareForDiagnostics(input, out managed, out native,
                    out managedContacts, out nativeContacts, out executedInBurst), Is.True);
                Assert.That(managedContacts, Is.GreaterThan(0), "The penetrating hair input must produce actual contact corrections.");
                Compare("Penetrating free hair point", input, managed, native, managedContacts, nativeContacts, executedInBurst);
                compared++;
            }
            Assert.That(compared, Is.GreaterThan(0));

            static void Compare(string name, Vector3[] input, Vector3[] managed, Vector3[] native,
                int managedContacts, int nativeContacts, bool executedInBurst)
            {
                Assert.That(executedInBurst, Is.True); Assert.That(managed.Length, Is.EqualTo(input.Length));
                Assert.That(native.Length, Is.EqualTo(input.Length));
                float maximumDrift = 0f; int changedBits = 0;
                for (int vertex = 0; vertex < managed.Length; vertex++)
                {
                    AssertRealCarryFinite(managed[vertex]); AssertRealCarryFinite(native[vertex]);
                    maximumDrift = Mathf.Max(maximumDrift, Vector3.Distance(managed[vertex], native[vertex]));
                    for (int axis = 0; axis < 3; axis++)
                    {
                        int managedBits = BitConverter.SingleToInt32Bits(managed[vertex][axis]);
                        int nativeBits = BitConverter.SingleToInt32Bits(native[vertex][axis]);
                        if (managedBits != nativeBits) changedBits++;
                        if (vertex % PlayerHair.PointsPerChain == 0)
                        {
                            int inputBits = BitConverter.SingleToInt32Bits(input[vertex][axis]);
                            Assert.That(managedBits, Is.EqualTo(inputBits), name + ": managed contacts must retain hair roots.");
                            Assert.That(nativeBits, Is.EqualTo(inputBits), name + ": native contacts must retain hair roots.");
                        }
                    }
                }
                TestContext.Out.WriteLine(name + ": managed/native contacts=" + managedContacts + "/" + nativeContacts +
                    "; changed component bits=" + changedBits + "; maximum drift m=" + maximumDrift.ToString("G9", CultureInfo.InvariantCulture));
                Assert.That(nativeContacts, Is.EqualTo(managedContacts), name + ": batched contacts must preserve original correction semantics.");
                Assert.That(maximumDrift, Is.LessThanOrEqualTo(.00001f), name + ": hair contacts may differ by at most 10 micrometres.");
            }
        }

        private static void AssertRealCarryNativeSurfaceEquivalence(PlayerJacketCloth[] jackets,
            long nativeBeforeFrames, long burstBeforeFrames)
        {
            // Capture gameplay execution before the comparison itself can run
            // a native solver. Diagnostic calls cannot satisfy this evidence.
            long gameplayNative = PlayerJacketClothSurface.TotalNativeSolves - nativeBeforeFrames;
            long gameplayBurst = PlayerJacketClothSurface.TotalBurstNativeSolves - burstBeforeFrames;
            TestContext.Out.WriteLine("Gameplay jacket surface solves: native=" + gameplayNative + "; Burst=" + gameplayBurst);
            Assert.That(PlayerScarfBodyContacts.NativeClothSupported, Is.True,
                "The production profile must calibrate the native cloth path.");
            Assert.That(gameplayNative, Is.GreaterThan(0), "The live frames must exercise the native panel solver.");
            Assert.That(gameplayBurst, Is.GreaterThan(0), "The live frames must execute the panel solver in Burst.");
            int compared = 0, bodies = 0, plackets = 0, pinned = 0;
            foreach (PlayerJacketCloth cloth in jackets)
            {
                var surfaces = (PlayerJacketClothSurface[])CarrySupportField(cloth, "surfaces");
                var bindings = (PlayerJacketCloth.SurfaceBinding[])CarrySupportField(cloth, "bindings");
                for (int surface = 0; surface < surfaces.Length; surface++)
                {
                    if (!cloth.TryCompareSurfaceSolverForDiagnostics(surface, out Vector3[] managed, out Vector3[] native,
                        out int managedContacts, out int nativeContacts, out bool executedInBurst)) continue;
                    string name = bindings[surface].Renderer.name;
                    Assert.That(executedInBurst, Is.True, name + ": comparison must run the actual Burst solver.");
                    Assert.That(native.Length, Is.EqualTo(managed.Length));
                    var freedom = (float[])CarrySupportField(surfaces[surface], "contactFreedom");
                    var raw = (Vector3[])CarrySupportField(CarrySupportField(surfaces[surface], "panelShape"), "Raw");
                    float maximumDrift = 0f;
                    int changedBits = 0;
                    for (int vertex = 0; vertex < managed.Length; vertex++)
                    {
                        AssertRealCarryFinite(managed[vertex]); AssertRealCarryFinite(native[vertex]);
                        maximumDrift = Mathf.Max(maximumDrift, Vector3.Distance(managed[vertex], native[vertex]));
                        for (int axis = 0; axis < 3; axis++)
                        {
                            int managedBits = BitConverter.SingleToInt32Bits(managed[vertex][axis]);
                            int nativeBits = BitConverter.SingleToInt32Bits(native[vertex][axis]);
                            if (managedBits != nativeBits) changedBits++;
                            if (freedom[vertex] <= 0f)
                            {
                                int rawBits = BitConverter.SingleToInt32Bits(raw[vertex][axis]);
                                Assert.That(managedBits, Is.EqualTo(rawBits), name + ": the managed reference must preserve its pinned input.");
                                Assert.That(nativeBits, Is.EqualTo(rawBits), name + ": the native solver must preserve its pinned input.");
                            }
                        }
                        if (freedom[vertex] <= 0f) pinned++;
                    }
                    TestContext.Out.WriteLine(name + ": managed/native contacts=" + managedContacts + "/" + nativeContacts +
                        "; changed component bits=" + changedBits + "; maximum drift m=" + maximumDrift.ToString("G9", CultureInfo.InvariantCulture));
                    Assert.That(maximumDrift, Is.LessThanOrEqualTo(.00001f),
                        name + ": identical panel inputs must retain the original managed geometry within 10 micrometres.");
                    Assert.That(managedContacts, Is.GreaterThanOrEqualTo(0)); Assert.That(nativeContacts, Is.GreaterThanOrEqualTo(0));
                    if (name.IndexOf("JacketBody", StringComparison.OrdinalIgnoreCase) >= 0) bodies++;
                    if (name.IndexOf("Placket", StringComparison.OrdinalIgnoreCase) >= 0) plackets++;
                    compared++;
                }
            }
            Assert.That(compared, Is.GreaterThan(0)); Assert.That(pinned, Is.GreaterThan(0));
            Assert.That(bodies, Is.GreaterThan(0), "The main jacket body must exercise the native comparison, not only small patches.");
            Assert.That(plackets, Is.GreaterThan(0), "The pinned plackets must also retain the original panel solver geometry.");
        }

        private static void AssertRealCarryPointQueryEquivalence(Player3DAssetRegistry rig)
        {
            foreach (string excludedSlot in new[] { "jacket", null })
            {
                using var optimized = new PlayerScarfBodyContacts(rig, excludedSlot, useMeshSupportPlanes: true, pointQuerySlots: 1);
                using var scalar = new PlayerScarfBodyContacts(rig, excludedSlot, useMeshSupportPlanes: true, pointQuerySlots: 0);
                optimized.UpdatePose(); scalar.UpdatePose();
                var points = new List<Vector3>();
                var exclusions = new List<Player3DAnatomicalPart?>();
                foreach (object proxy in (Array)CarrySupportField(scalar, "proxies"))
                {
                    if (!(bool)CarrySupportField(proxy, "Active")) continue;
                    Vector3 minimum = (Vector3)CarrySupportField(proxy, "WorldMinimum"), maximum = (Vector3)CarrySupportField(proxy, "WorldMaximum");
                    Vector3 centre = (minimum + maximum) * .5f, radius = (maximum - minimum) * .5f;
                    foreach (Player3DAnatomicalPart? excluded in new Player3DAnatomicalPart?[] { null, Player3DAnatomicalPart.LeftHand })
                    foreach (Vector3 direction in new[] { Vector3.zero, Vector3.left, Vector3.right, Vector3.up, Vector3.down, Vector3.forward, Vector3.back })
                    {
                        points.Add(centre + Vector3.Scale(radius, direction) * .99f);
                        exclusions.Add(excluded);
                    }
                }
                var cache = optimized.CreatePointQueryCache(points.Count);
                Vector3[] hemPoints = points.ToArray();
                var hemFreedom = new float[hemPoints.Length];
                Array.Fill(hemFreedom, 1f);
                Transform arm = CityPedestrianHandProps.FindSocket(rig.Animator.transform, "upper_arm.L");
                Assert.That(arm, Is.Not.Null);
                Quaternion rotation = arm.localRotation;
                int corrected = 0, checkedPoints = 0;
                try
                {
                    // Stable location IDs retain their hints while both the
                    // tested point and actual rig planes move. Each hint must
                    // be verified against this pose before it can reject work.
                    for (int pose = 0; pose < 3; pose++)
                    {
                        arm.localRotation = pose == 1 ? rotation * Quaternion.Euler(0f, 0f, 12f) : rotation;
                        optimized.UpdatePose(); scalar.UpdatePose();
                        AssertRealCarrySourceSupport(optimized, points);
                        foreach (float side in new[] { -1f, 0f, 1f })
                        {
                            bool fastHem = optimized.TryHemObstacle(hemPoints, hemFreedom, rig.transform,
                                out Bounds fastBounds, out float fastSide, side);
                            bool referenceHem = scalar.TryHemObstacle(hemPoints, hemFreedom, rig.transform,
                                out Bounds referenceBounds, out float referenceSide, side);
                            Assert.That(fastHem, Is.EqualTo(referenceHem), "Hem selection must match the exhaustive current-plane scan.");
                            Assert.That(fastBounds.Equals(referenceBounds), Is.True);
                            Assert.That(BitConverter.SingleToInt32Bits(fastSide), Is.EqualTo(BitConverter.SingleToInt32Bits(referenceSide)));
                        }
                        for (int index = 0; index < points.Count; index++)
                        foreach (Vector3 offset in new[] { Vector3.zero, Vector3.right * .0007f, Vector3.left * .0004f })
                        for (int repeat = 0; repeat < 2; repeat++)
                        {
                            Vector3 original = points[index] + offset;
                            Vector3 fast = original, reference = original, uncached = original;
                            bool fastMoved = optimized.ResolvePointCached(ref fast, cache, index, exclusions[index]);
                            bool referenceMoved = scalar.ResolvePoint(ref reference, exclusions[index]);
                            bool uncachedMoved = optimized.ResolvePoint(ref uncached, exclusions[index]);
                            Assert.That(fastMoved, Is.EqualTo(referenceMoved));
                            Assert.That(uncachedMoved, Is.EqualTo(referenceMoved));
                            for (int axis = 0; axis < 3; axis++)
                            {
                                Assert.That(BitConverter.SingleToInt32Bits(fast[axis]), Is.EqualTo(BitConverter.SingleToInt32Bits(reference[axis])),
                                    "Location hints must retain the original exact cloth correction across repeated points and changed rig poses.");
                                Assert.That(BitConverter.SingleToInt32Bits(uncached[axis]), Is.EqualTo(BitConverter.SingleToInt32Bits(reference[axis])));
                            }
                            if (referenceMoved) corrected++;
                            checkedPoints++;
                        }
                    }
                }
                finally
                {
                    arm.localRotation = rotation;
                    optimized.UpdatePose(); scalar.UpdatePose();
                }
                Assert.That(checkedPoints, Is.GreaterThan(0)); Assert.That(corrected, Is.GreaterThan(0));
            }
        }

        private static void AssertRealCarrySourceSupport(PlayerScarfBodyContacts contacts, List<Vector3> points)
        {
            var equipment = CarrySupportField(contacts, "equipment") as PlayerEquipmentContacts;
            var proxies = (Array)CarrySupportField(contacts, "proxies");
            foreach (Vector3 point in points)
            {
                bool outside = equipment == null || !equipment.Contains(point, .0001f);
                foreach (object proxy in proxies)
                {
                    if (!outside) break;
                    if (!(bool)CarrySupportField(proxy, "Active")) continue;
                    var planes = CarrySupportField(proxy, "WorldPlanes") as Plane[];
                    if (planes == null) { outside = false; break; }
                    float padding = (float)CarrySupportField(CarrySupportField(proxy, "Selected"), "SurfacePadding");
                    bool separated = false;
                    foreach (Plane plane in planes)
                        if (plane.GetDistanceToPoint(point) >= .0001f - padding) { separated = true; break; }
                    if (!separated) { outside = false; break; }
                }
                Assert.That(contacts.OutsideSourceSupport(point), Is.EqualTo(outside),
                    "Source-hull clearance must match the original live exhaustive scan without a bounds shortcut.");
            }
            int checkedTrees = 0;
            foreach (object proxy in proxies)
            {
                if (!(bool)CarrySupportField(proxy, "Active")) continue;
                object tree = CarrySupportField(proxy, "ScoreTree");
                if (tree == null) continue;
                var planes = (Plane[])CarrySupportField(proxy, "WorldPlanes");
                float padding = (float)CarrySupportField(CarrySupportField(proxy, "Selected"), "SurfacePadding");
                Vector3 centre = ((Vector3)CarrySupportField(proxy, "WorldMinimum") +
                    (Vector3)CarrySupportField(proxy, "WorldMaximum")) * .5f;
                var contains = tree.GetType().GetMethod("ContainsAtThreshold");
                Assert.That(contains, Is.Not.Null);
                foreach (float threshold in new[] { .0001f - padding, 0f, .0001f })
                foreach (int planeIndex in new[] { 0, planes.Length / 2, planes.Length - 1 })
                foreach (float offset in new[] { -.000001f, 0f, .000001f })
                {
                    Plane boundary = planes[planeIndex];
                    Vector3 point = centre + boundary.normal *
                        ((threshold + offset - boundary.GetDistanceToPoint(centre)) / boundary.normal.sqrMagnitude);
                    bool reference = true;
                    foreach (Plane plane in planes)
                        if (plane.GetDistanceToPoint(point) >= threshold) { reference = false; break; }
                    object[] arguments = { point, planes.Length - 1, threshold };
                    Assert.That((bool)contains.Invoke(tree, arguments), Is.EqualTo(reference),
                        "Shifted plane-tree clearance must retain exact float comparisons at boundary crossings.");
                }
                checkedTrees++;
            }
            Assert.That(checkedTrees, Is.GreaterThan(0));
        }

        private static void AssertRealCarrySurfaceSkinFreshness(PlayerJacketCloth[] jackets)
        {
            foreach (PlayerJacketCloth cloth in jackets)
            foreach (var surface in (PlayerJacketClothSurface[])CarrySupportField(cloth, "surfaces"))
            {
                BoneWeight[] weights = surface.Source.boneWeights;
                Matrix4x4[] bind = surface.Source.bindposes;
                Transform[] bones = surface.Renderer.bones;
                var matrices = new Matrix4x4[bind.Length];
                var normals = (List<Vector3>)CarrySupportField(surface, "normals");
                Vector3[] bindNormals = surface.Source.normals;
                Transform changedBone = bones[weights[0].boneIndex0];
                Quaternion rotation = changedBone.localRotation;
                try
                {
                    for (int pose = 0; pose < 3; pose++)
                    {
                        changedBone.localRotation = pose == 1 ? rotation * Quaternion.Euler(3f, 5f, 0f) : rotation;
                        surface.UpdatePose();
                        for (int bone = 0; bone < matrices.Length; bone++) matrices[bone] = bones[bone].localToWorldMatrix * bind[bone];
                        var checkedWeights = new HashSet<BoneWeight>();
                        for (int vertex = 0; vertex < weights.Length; vertex++)
                        {
                            BoneWeight weight = weights[vertex];
                            if (!checkedWeights.Add(weight)) continue;
                            Matrix4x4 reference = matrices[weight.boneIndex0];
                            for (int column = 0; column < 4; column++) reference.SetColumn(column, reference.GetColumn(column) * weight.weight0);
                            Add(weight.boneIndex1, weight.weight1); Add(weight.boneIndex2, weight.weight2); Add(weight.boneIndex3, weight.weight3);
                            Assert.That(surface.Skin(vertex).Equals(reference), Is.True,
                                "Referenced-bone capture must preserve full-rig skin arithmetic across same-frame poses.");
                            Matrix4x4 normalMatrix = reference.inverse.transpose;
                            Assert.That(surface.WorldNormal(vertex).Equals(normalMatrix.MultiplyVector(normals[vertex]).normalized), Is.True);
                            Assert.That(surface.BindWorldNormal(vertex).Equals(normalMatrix.MultiplyVector(bindNormals[vertex]).normalized), Is.True);

                            void Add(int bone, float influence)
                            {
                                if (influence <= 0f) return;
                                for (int column = 0; column < 4; column++)
                                    reference.SetColumn(column, reference.GetColumn(column) + matrices[bone].GetColumn(column) * influence);
                            }
                        }
                    }
                }
                finally { changedBone.localRotation = rotation; surface.UpdatePose(); }
            }
        }

        private void AssertRealCarrySharedClothSnapshot()
        {
            PlayerJacketCloth cloth = root.Hero.DamageRigRoot.GetComponentInParent<PlayerJacketCloth>();
            SkinnedMeshRenderer source = null, template = null;
            foreach (SkinnedMeshRenderer skin in root.Hero.DamageRigRoot.GetComponentsInChildren<SkinnedMeshRenderer>(true))
                if (skin.name == "CLO_JacketBody") source = skin;
                else if (skin.name.EndsWith("__CLO_JacketBody", StringComparison.Ordinal)) template = skin;
            Assert.That(source, Is.Not.Null); Assert.That(template, Is.Not.Null); Assert.That(cloth, Is.Not.Null);
            var snapshot = new CombatBodySourceDeformation.SourceSnapshot();
            var hosts = new GameObject[3];
            var outputs = new SkinnedMeshRenderer[3];
            var derivatives = new CombatBodySourceDeformation[3];
            try
            {
                for (int i = 0; i < hosts.Length; i++)
                {
                    hosts[i] = new GameObject("Test cloth derivative " + (i + 1));
                    outputs[i] = hosts[i].AddComponent<SkinnedMeshRenderer>(); outputs[i].sharedMesh = template.sharedMesh;
                    derivatives[i] = i < 2 ? new CombatBodySourceDeformation(template.sharedMesh, source, snapshot) : new CombatBodySourceDeformation(template.sharedMesh, source);
                }
                cloth.RequestReset(); cloth.ApplyAt(0d, false, true, new WindSample(4f, 1f));
                foreach (int revision in new[] { 1, 2 })
                {
                    if (revision == 2) cloth.ApplyAt(.02d, false, true, new WindSample(4f, 1f));
                    foreach (int index in new[] { 0, 1, 2 }) derivatives[index].Refresh(outputs[index]);
                    Assert.That(snapshot.CaptureCount, Is.EqualTo(revision), "Sibling derivatives must capture one full source mesh per exact cloth revision.");
                    for (int i = 0; i < 2; i++)
                    {
                        CollectionAssert.AreEqual(outputs[2].sharedMesh.vertices, outputs[i].sharedMesh.vertices);
                        CollectionAssert.AreEqual(outputs[2].sharedMesh.normals, outputs[i].sharedMesh.normals);
                        CollectionAssert.AreEqual(outputs[2].sharedMesh.tangents, outputs[i].sharedMesh.tangents);
                        Assert.That(derivatives[i].GeometryVersion, Is.EqualTo((uint)revision));
                    }
                }
            }
            finally
            {
                foreach (CombatBodySourceDeformation derivative in derivatives) derivative?.Dispose();
                foreach (GameObject host in hosts) if (host != null) Object.DestroyImmediate(host);
                cloth.RequestReset();
            }
        }

        private static void AssertRealCarryJournal(string directory, string session, int measuredRound)
        {
            bool found = false, budgetFound = false;
            foreach (string path in Directory.GetFiles(directory, "*.ndjson", SearchOption.AllDirectories))
            {
                if (path.IndexOf(session, StringComparison.Ordinal) < 0) continue;
                if (!Path.GetFileName(Path.GetDirectoryName(path)).EndsWith("_" + measuredRound.ToString("D6", CultureInfo.InvariantCulture), StringComparison.Ordinal)) continue;
                // The logger keeps this round open for appends. A barrier
                // flushes our intended records without changing game ownership.
                using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                using var reader = new StreamReader(stream);
                string line;
                while ((line = reader.ReadLine()) != null)
                {
                    if (line.IndexOf("cloth_budget_sample", StringComparison.Ordinal) >= 0)
                    {
                        foreach (string field in new[] { "sling_steps", "sling_contact_iterations", "sling_length_iterations", "jacket_discarded_ms", "hair_discarded_ms" })
                            Assert.That(line, Does.Contain("\"" + field + "\""));
                        budgetFound = true;
                    }
                    if (line.IndexOf("cloth_stage_sample", StringComparison.Ordinal) < 0) continue;
                    foreach (string field in new[] { "sling_ms", "jacket_ms", "hair_ms", "scarf_ms", "sling_calls", "jacket_calls", "jacket_steps", "hair_steps" })
                        Assert.That(line, Does.Contain("\"" + field + "\""), "The real frame journal must expose every cloth stage and bounded work.");
                    found = true;
                }
            }
            Assert.That(found, Is.True, "The real-frame scenario must produce readable cloth_stage_sample diagnostics.");
            Assert.That(budgetFound, Is.True, "The same real-frame journal must expose simulation caps and discarded cosmetic time.");
        }
    }
}

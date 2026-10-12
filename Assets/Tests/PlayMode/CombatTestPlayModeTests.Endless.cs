using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using NUnit.Framework;
using Unity.Profiling;
using UnityEngine;
using UnityEngine.Profiling;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
#if UNITY_EDITOR
using UnityEditorInternal;
#endif
using Object = UnityEngine.Object;
using Stopwatch = System.Diagnostics.Stopwatch;

namespace BarPromenade.Tests.PlayMode
{
    public sealed partial class CombatTestPlayModeTests
    {
        [UnityTest]
        public IEnumerator Range_EndlessPopulationSpawnsGraduallyPreservesSurvivorsAndStopsOnExit()
        {
            yield return SceneManager.LoadSceneAsync(SceneIds.MainMenu, LoadSceneMode.Single);
            CombatTestRoot.UseLegacyArenaForTests = false;
            CombatTestRoot.WarehouseSeedForTests = 0;
            var settings = new CombatEncounterSettings(5, .1f, .1f, .15f, .15f);
            var selection = new CombatTestSelection(new CombatLoadoutSelection(
                CombatWeaponId.Shotgun, CombatWeaponId.Shotgun), CombatTestMode.Endless, settings);
            Assert.That(CombatTestStartService.TryStart(selection), Is.True);
            yield return AwaitSelectedCombatRange();
            Assert.That(root.WarehousePlan, Is.Not.Null, "The population contract uses the real warehouse.");
            Assert.That(root.Mode, Is.EqualTo(CombatTestMode.Endless));
            Assert.That(root.EncounterSettings, Is.EqualTo(settings));
            Assert.That(root.Sparring, Is.True);
            root.SetSparring(false);
            Assert.That(root.Sparring, Is.True);
            Assert.That(root.LiveOpponentCount, Is.Zero, "The first opponent is delayed too.");
            Assert.That(CombatEncounterSettings.Default.MaximumLiveOpponents, Is.EqualTo(2));
            Assert.That(root.EncounterSettings.MaximumCorpses, Is.EqualTo(2));
            Assert.That(root.CorpseCount, Is.Zero);
            Assert.That(root.Opponents.Count, Is.EqualTo(5));
            Assert.That(root.CameraFollow.TargetLockActive, Is.False, "There is no invisible target before arrival.");
            for (int index = 0; index < root.Opponents.Count; index++)
            {
                CombatActor actor = root.Opponents[index];
                Assert.That(actor.gameObject.activeInHierarchy, Is.False);
                Assert.That(actor.NpcAnimationGraphBuilds, Is.EqualTo(1),
                    "Every prepared inactive opponent retains its authored combat graph.");
                Assert.That(actor.GetComponentInChildren<DefaultNpcAppearance>(true).AppearanceKey,
                    Is.EqualTo(DefaultNpcPopulation.CombatTestOpponentId(index)));
            }

            root.Tick(.025f);
            float remaining = root.NextOpponentSeconds;
            Assert.That(root.PauseMenu.Open(), Is.True);
            root.Tick(10f);
            yield return null;
            Assert.That(root.NextOpponentSeconds, Is.EqualTo(remaining).Within(.00001f));
            Assert.That(root.LiveOpponentCount, Is.Zero);
            Assert.That(root.PauseMenu.Cancel(), Is.True);
            yield return WaitFor(() => GameInput.CanRead(GameInputContext.Gameplay), "Population pause did not release input.");
            for (int expected = 1; expected <= 5; expected++)
            {
                float untilArrival = root.NextOpponentSeconds;
                root.Tick(Mathf.Max(0f, untilArrival - .025f));
                Assert.That(root.LiveOpponentCount, Is.EqualTo(expected - 1), "An arrival cannot precede its active-time interval.");
                root.Tick(.04f);
                Assert.That(root.LiveOpponentCount, Is.EqualTo(expected), "One interval creates one additional opponent.");
                AssertEndlessPreparedAnimationGraphs();
                Assert.That(root.RoundFinished || root.EndlessRunFinished, Is.False);
            }
            Assert.That(root.WaitingForNextOpponent, Is.False);
            root.Tick(.025f);
            Assert.That(root.LiveOpponentCount, Is.EqualTo(5), "A filled limit cannot accumulate another arrival.");
            var routes = new List<Vector3>();
            var area = new WarehouseArenaArea(root.WarehousePlan);
            foreach (CombatActor actor in root.Opponents)
            {
                Assert.That(Vector3.ProjectOnPlane(actor.transform.position - root.Hero.transform.position, Vector3.up).magnitude,
                    Is.GreaterThanOrEqualTo(settings.MinimumHeroDistance));
                Assert.That(area.FindRoute(actor.transform.position, root.Hero.transform.position, routes, actor.Body.radius + .04f),
                    Is.True, "Every actual spawned body has a warehouse route to the hero.");
            }
            yield return CaptureEndlessPopulationGameView();

            CombatActor hero = root.Hero, defeated = root.Opponents[0], survivor = root.Opponents[1];
            GameObject defeatedWeapon = defeated.Weapon, heldWeapon = hero.Weapon;
            IFirearmState pistol = hero.CarriedFirearm(CombatWeaponId.Pistol);
            IFirearmState shotgun = hero.CarriedFirearm(CombatWeaponId.Shotgun);
            hero.State.ReceiveProjectileHit(13f);
            hero.State.Advance(.2f);
            Assert.That(hero.BodyDamage.Apply(BodyDamageRegion.LeftForearm, 0, .25f, 1, 1, 0), Is.True);
            root.BodyEffects.SynchronizeActor(hero, hero.LastImpact);
            hero.Body.Move(new Vector3(.4f, 0f, -.3f));
            survivor.State.ReceiveProjectileHit(9f);
            survivor.State.Advance(.2f);
            Assert.That(survivor.BodyDamage.Apply(BodyDamageRegion.LeftForearm, 0, .2f, 1, 1, 0), Is.True);
            root.BodyEffects.SynchronizeActor(survivor, survivor.LastImpact);
            Physics.SyncTransforms();
            float health = hero.State.Health, survivorHealth = survivor.State.Health;
            float trauma = hero.BodyDamage.TissueLoss(BodyDamageRegion.LeftForearm, 0);
            float survivorTrauma = survivor.BodyDamage.TissueLoss(BodyDamageRegion.LeftForearm, 0);
            Vector3 position = hero.transform.position;
            SpendEndlessRound(pistol);
            SpendEndlessRound(shotgun);
            int pistolSequence = pistol.ShotSequence, shotgunSequence = shotgun.ShotSequence;
            var participants = EndlessParticipants();

            // A head projectile creates the real terminal body and contact hold.
            QueueEndlessHeadshot(hero, defeated, 1);
            root.Projectiles.Advance(CombatTestRoot.SimulationStep, participants);
            root.Projectiles.ApplyContacts();
            Assert.That(defeated.State.IsDefeated && defeated.IsRagdollActive, Is.True);
            Assert.That(root.HeadEffects.DetachedSectorCountFor(defeated), Is.GreaterThan(0));
            root.Tick(0f);
            Assert.That(root.EndlessKills, Is.EqualTo(1));
            Assert.That(root.LiveOpponentCount, Is.EqualTo(4));
            Assert.That(root.CorpseCount, Is.EqualTo(1));
            Assert.That(root.WaitingForNextOpponent, Is.True);
            Assert.That(root.RoundFinished || root.EndlessRunFinished, Is.False);
            Assert.That(root.Taunt.CanInteract(root.Player.Interactor), Is.False);
            float beforeHold = root.NextOpponentSeconds;
            root.Tick(CombatTestRoot.SimulationStep);
            Assert.That(root.NextOpponentSeconds, Is.EqualTo(beforeHold).Within(.00001f), "Contact hold cannot advance the spawn clock.");
            Assert.That(pistol.Rounds, Is.EqualTo(pistol.Settings.MagazineCapacity - 1));
            Assert.That(shotgun.Rounds, Is.EqualTo(shotgun.Settings.MagazineCapacity - 1), "A survivor prevents supply.");
            Assert.That(root.Projectiles.TrySpawn(hero, new Vector3(-4f, 8f, -4f), Vector3.up, 20), Is.True);
            root.Tick(.35f);
            Assert.That(root.LiveOpponentCount, Is.EqualTo(5));
            Assert.That(root.CorpseCount, Is.Zero, "Reusing the shared pool frees the old corpse place.");
            Assert.That(root.EndlessKills, Is.EqualTo(1), "The same defeated participant is credited only once.");
            Assert.That(defeated.State.IsDefeated || defeated.IsRagdollActive || defeated.IsWeaponDropped, Is.False);
            Assert.That(defeated.Weapon, Is.SameAs(defeatedWeapon));
            AssertEndlessPreparedAnimationGraphs();
            Assert.That(root.GetComponentsInChildren<CombatActor>(true).Length, Is.EqualTo(6));
            Assert.That(root.HeadEffects.DetachedSectorCountFor(defeated), Is.Zero);
            Assert.That(root.Projectiles.ActiveCount, Is.GreaterThan(0), "An unrelated slow flight survives the recycled body.");
            Assert.That(survivor.State.Health, Is.EqualTo(survivorHealth).Within(.0001f));
            Assert.That(survivor.BodyDamage.TissueLoss(BodyDamageRegion.LeftForearm, 0), Is.EqualTo(survivorTrauma));
            Assert.That(hero.State.Health, Is.EqualTo(health).Within(.0001f));
            Assert.That(hero.BodyDamage.TissueLoss(BodyDamageRegion.LeftForearm, 0), Is.EqualTo(trauma));
            Assert.That(Vector3.Distance(hero.transform.position, position), Is.LessThan(.03f));
            Assert.That(hero.Weapon, Is.SameAs(heldWeapon));
            Assert.That(pistol.Rounds, Is.EqualTo(pistol.Settings.MagazineCapacity - 1));
            Assert.That(shotgun.Rounds, Is.EqualTo(shotgun.Settings.MagazineCapacity - 1));

            // Defeat in reverse pool order: the oldest death, rather than the
            // lowest actor index, must release its body, detached head and prop.
            CombatActor oldestCorpse = root.Opponents[4];
            GameObject droppedProp = oldestCorpse.Weapon;
            for (int index = 4; index >= 2; index--)
            {
                CombatActor victim = root.Opponents[index];
                QueueEndlessHeadshot(hero, victim, 30 + index);
                root.Projectiles.Advance(CombatTestRoot.SimulationStep, participants);
                root.Projectiles.ApplyContacts();
                Assert.That(victim.IsRagdollActive && victim.IsWeaponDropped, Is.True);
                root.Tick(0f);
                Assert.That(root.CorpseCount, Is.EqualTo(Mathf.Min(5 - index, settings.MaximumCorpses)));
                Assert.That(root.LiveOpponentCount, Is.EqualTo(index));
                Assert.That(root.EndlessKills, Is.EqualTo(6 - index));
            }
            Assert.That(oldestCorpse.gameObject.activeInHierarchy, Is.False);
            Assert.That(oldestCorpse.IsRagdollActive || oldestCorpse.IsWeaponDropped, Is.False);
            Assert.That(droppedProp.activeInHierarchy, Is.False, "A detached dropped prop must retire with its owner.");
            Assert.That(droppedProp.transform.IsChildOf(oldestCorpse.transform), Is.True);
            Assert.That(root.HeadEffects.DetachedSectorCountFor(oldestCorpse), Is.Zero);
            Assert.That(root.Opponents[2].gameObject.activeInHierarchy && root.Opponents[3].gameObject.activeInHierarchy, Is.True,
                "The newest two corpses stay in the world.");
            Assert.That(root.Projectiles.ActiveCount, Is.GreaterThan(0), "Corpse cleanup cannot erase an unrelated flight.");
            Assert.That(survivor.State.Health, Is.EqualTo(survivorHealth).Within(.0001f));
            Assert.That(survivor.BodyDamage.TissueLoss(BodyDamageRegion.LeftForearm, 0), Is.EqualTo(survivorTrauma));
            AssertEndlessPreparedAnimationGraphs();
            root.Tick(0f);
            Assert.That(root.EndlessKills, Is.EqualTo(4), "Retired corpses cannot be credited a second time.");
            yield return CaptureEndlessPopulationGameView("endless-corpses-ru");

            // Clearing every live place opens the separate supply transaction;
            // this observation neither resets the surviving rig nor its flights.
            foreach (CombatActor actor in root.Opponents) actor.State.ReceiveProjectileHit(actor.State.Settings.MaxHealth);
            // The preceding real headshots still own their twelve-substep hold;
            // supply commits only once active simulation resumes after it.
            root.Tick(13f * CombatTestRoot.SimulationStep);
            Assert.That(root.LiveOpponentCount, Is.Zero);
            Assert.That(root.CorpseCount, Is.EqualTo(settings.MaximumCorpses));
            Assert.That(root.EndlessKills, Is.EqualTo(6));
            AssertEndlessAmmunition(pistol, pistolSequence);
            AssertEndlessAmmunition(shotgun, shotgunSequence);
            Assert.That(hero.State.Health, Is.EqualTo(health).Within(.0001f));
            Assert.That(hero.BodyDamage.TissueLoss(BodyDamageRegion.LeftForearm, 0), Is.EqualTo(trauma));
            Assert.That(root.Projectiles.ActiveCount, Is.GreaterThan(0));
            Assert.That(root.NextOpponentSeconds, Is.GreaterThan(0f));
            hero.State.ReceiveProjectileHit(hero.State.Settings.MaxHealth);
            root.Tick(0f);
            Assert.That(root.EndlessRunFinished, Is.True);
            Assert.That(root.WaitingForNextOpponent, Is.False);
            root.Tick(1f);
            Assert.That(root.LiveOpponentCount, Is.Zero, "Hero defeat cancels the pending refill arrival.");
            Assert.That(root.EndlessKills, Is.EqualTo(6));

            root.ResetRound();
            AssertEndlessPreparedAnimationGraphs();
            Assert.That(root.EndlessKills, Is.Zero);
            Assert.That(root.LiveOpponentCount, Is.Zero);
            Assert.That(root.CorpseCount, Is.Zero, "Reset releases every retained body.");
            Assert.That(root.WaitingForNextOpponent, Is.True);
            Assert.That(hero.State.Health, Is.EqualTo(hero.State.Settings.MaxHealth));
            Assert.That(hero.BodyDamage.TissueLoss(BodyDamageRegion.LeftForearm, 0), Is.Zero);
            Assert.That(root.ReturnToWeapons(), Is.True);
            yield return WaitFor(() => SceneManager.GetActiveScene().name == SceneIds.MainMenu &&
                !SceneTransitionService.IsTransitioning, "Endless population did not return to preparation.");
            Assert.That(defeated == null && defeatedWeapon == null && heldWeapon == null, Is.True);
            Assert.That(Object.FindAnyObjectByType<CombatActor>(), Is.Null);
            StartMenuRoot menu = Object.FindAnyObjectByType<StartMenuRoot>();
            Assert.That(menu.IsChoosingCombatWeapon, Is.True);
            Assert.That(menu.SelectedCombatSelection, Is.EqualTo(selection), "Preparation retains capacity and pacing with mode and kit.");
            yield return CaptureEndlessGameView("endless-preparation-ru", "ru");
            yield return CaptureEndlessGameView("endless-preparation-en", "en");
            Assert.That(PauseMenuController.IsAnyPaused, Is.False);
            LogAssert.NoUnexpectedReceived();
        }

        // A headless Tick benchmark cannot reproduce the log's long intervals
        // after render submission. Keep this opt-in test on real Editor frames.
        [UnityTest, Explicit("Rendered warehouse performance evidence with optional CPU Timeline; no machine-dependent FPS gate.")]
        public IEnumerator Range_EndlessWarehouseRetainsRenderedPerformanceEvidence()
        {
            Assert.That(Application.isBatchMode, Is.False, "Run this reproduction in the rendered Editor, without -batchmode/-nographics.");
            float captureDelta = Time.captureDeltaTime;
            int targetRate = Application.targetFrameRate, vSync = QualitySettings.vSyncCount;
            bool useLegacy = CombatTestRoot.UseLegacyArenaForTests;
            int? warehouseSeed = CombatTestRoot.WarehouseSeedForTests;
            string folder = Path.GetFullPath("TestResults/Test combat performance");
            var phases = new List<EndlessPerformancePhase>();
            var timelinePhases = new Dictionary<int, string>();
            var arrivals = new List<string>();
            EndlessPerformanceRecorder recorder = null;
            EndlessCpuTimelineScope timeline = null;
            IDisposable gameView = null;
            try
            {
                Directory.CreateDirectory(folder);
                Time.captureDeltaTime = 0f;
                Application.targetFrameRate = 60;
                QualitySettings.vSyncCount = 0;
#if UNITY_EDITOR
                Type sizeOwner = typeof(AreaLoadingVisualCapture).GetNestedType("GameViewResolution", BindingFlags.NonPublic);
                Assert.That(sizeOwner, Is.Not.Null);
                gameView = (IDisposable)Activator.CreateInstance(sizeOwner, true);
                sizeOwner.GetMethod("Set").Invoke(gameView, new object[] { 1920, 1080 });
#endif
                yield return SceneManager.LoadSceneAsync(SceneIds.MainMenu, LoadSceneMode.Single);
                CombatTestRoot.UseLegacyArenaForTests = false;
                CombatTestRoot.WarehouseSeedForTests = -41128863;
                var schedule = new CombatEncounterSettings(5, 60f, 60f, 60f, 60f);
                var selection = new CombatTestSelection(new CombatLoadoutSelection(
                    CombatWeaponId.Shotgun, CombatWeaponId.Pistol), CombatTestMode.Endless, schedule);
                Assert.That(CombatTestStartService.TryStart(selection), Is.True);
                yield return AwaitSelectedCombatRange();
                Assert.That(root.WarehousePlan.Seed, Is.EqualTo(-41128863));
                Assert.That(root.Opponents.Count, Is.EqualTo(5));
                Assert.That(root.Hero.WeaponId, Is.EqualTo(CombatWeaponId.Pistol));
                foreach (CombatActor actor in root.Opponents) Assert.That(actor.WeaponId, Is.EqualTo(CombatWeaponId.Crowbar));
                AssertEndlessPreparedAnimationGraphs();
                root.SendMessage("OnApplicationFocus", true);
                GameInput.HandleApplicationFocus(true);
                root.SetDuelLogging(true, Path.Combine(folder, "Duel"));
                // Warm rendering/cloth without advancing the population timer.
                for (int warm = 0; warm < 12; warm++) yield return null;
                Assert.That(new Vector2Int(Screen.width, Screen.height), Is.EqualTo(new Vector2Int(1920, 1080)),
                    "Main measurements retain the session's real 1080p framebuffer.");
                recorder = new EndlessPerformanceRecorder(root);
                AssertRealCarrySurfaceSkinFreshness(recorder.Jackets);
                // Independent same-frame skin proofs precede every measured
                // interval, including their transform writes and allocations.
                for (int warm = 0; warm < 4; warm++) yield return null;
                root.AutomaticSimulation = true;
                yield return MeasureEndlessPerformancePhase(recorder, phases, timelinePhases, "Test empty warehouse", 30);
                Assert.That(root.LiveOpponentCount, Is.Zero);
                Assert.That(phases[0].BakeResults[(int)SecondaryMotionBakePart.Jacket, (int)SecondaryMotionBakeResult.FinalPose], Is.Zero,
                    "Production completed Idle with its real stored shotgun must not alternate between recorded cloth and physical contact solving.");
                Assert.That(phases[0].Cloth.JacketBaked, Is.GreaterThanOrEqualTo(15),
                    "The proof must actually consume the recorded jacket through the production LateUpdate path.");
                Assert.That(phases[0].Cloth.SlingBaked, Is.GreaterThanOrEqualTo(15),
                    "The production stored-shotgun attachment must consume its recorded material seat rather than reject it because of its initial pose.");

                PrepareEndlessPerformanceArrival();
                yield return MeasureEndlessPerformancePhase(recorder, phases, timelinePhases, "Test one live opponent", 30);
                Assert.That(root.LiveOpponentCount, Is.EqualTo(1));
                CaptureArrival("Test first arrival");
                // Prepare only the spawn timer between phases. The following
                // bounded production Tick owns activation/reset and graph use.
                for (int expected = 2; expected <= 5; expected++)
                {
                    root.AutomaticSimulation = false;
                    PrepareEndlessPerformanceArrival();
                    root.Tick(CombatTestRoot.SimulationStep);
                    Assert.That(root.LiveOpponentCount, Is.EqualTo(expected));
                    CaptureArrival("Test population arrival");
                }
                root.AutomaticSimulation = true;
                yield return MeasureEndlessPerformancePhase(recorder, phases, timelinePhases, "Test five live opponents", 30);
                Assert.That(root.LiveOpponentCount, Is.EqualTo(5));
                AssertEndlessPreparedAnimationGraphs();

                if (Array.IndexOf(Environment.GetCommandLineArgs(), "-endlessPerformanceCpuTimeline") >= 0)
                    timeline = new EndlessCpuTimelineScope(folder);
                CombatActor victim = root.Opponents[0];
                QueueEndlessHeadshot(root.Hero, victim, 1);
                yield return MeasureEndlessPerformancePhase(recorder, phases, timelinePhases, "Test headshot and damaged population", 30);
                Assert.That(victim.State.IsDefeated && victim.IsRagdollActive, Is.True);
                Assert.That(root.HeadEffects.DetachedSectorCountFor(victim), Is.GreaterThan(0));
                Assert.That(root.EndlessKills, Is.EqualTo(1));
                // Settlement is preparation, outside the timed buckets. PhysX
                // keeps running while the combat/spawn clock stays unchanged.
                root.AutomaticSimulation = false;
                float settleDeadline = Time.realtimeSinceStartup + 8f;
                while (!victim.Ragdoll.IsSettled && Time.realtimeSinceStartup < settleDeadline) yield return null;
                Assert.That(victim.Ragdoll.IsSettled, Is.True, "The damage tail must include an actually settled terminal body.");
                root.AutomaticSimulation = true;
                yield return MeasureEndlessPerformancePhase(recorder, phases, timelinePhases, "Test settled damaged population", 30);
                Assert.That(root.LiveOpponentCount, Is.EqualTo(4));

                GameObject retainedWeapon = victim.Weapon;
                PrepareEndlessPerformanceArrival();
                yield return MeasureEndlessPerformancePhase(recorder, phases, timelinePhases, "Test recycled opponent", 30);
                Assert.That(root.LiveOpponentCount, Is.EqualTo(5));
                Assert.That(victim.State.IsDefeated || victim.IsRagdollActive, Is.False);
                Assert.That(victim.Weapon, Is.SameAs(retainedWeapon));
                Assert.That(root.GetComponentsInChildren<CombatActor>(true).Length, Is.EqualTo(6));
                AssertEndlessPreparedAnimationGraphs();
                CaptureArrival("Test recycled arrival");
                root.AutomaticSimulation = false;
                timeline?.Dispose();
                if (timeline != null) ExportEndlessPerformanceTimeline(folder, timelinePhases);
                var flush = root.JournalForDiagnostics.FlushAsync();
                yield return WaitFor(() => flush.IsCompleted, "Endless performance journal did not flush.");
                Assert.That(flush.IsFaulted, Is.False);
                foreach (EndlessPerformancePhase phase in phases) phase.Cloth.AssertPhysicalBounds();
                LogAssert.NoUnexpectedReceived();

                void CaptureArrival(string name)
                {
                    AssertEndlessPreparedAnimationGraphs();
                    arrivals.Add(FormattableString.Invariant($"{name}: search={EndlessMilliseconds(root.LastSpawnSearchTicks):F5}ms; activation={EndlessMilliseconds(root.LastSpawnActivationTicks):F5}ms; reset={EndlessMilliseconds(root.LastSpawnResetTicks):F5}ms; graphs=1 per prepared body."));
                }
            }
            finally
            {
                try
                {
                    timeline?.Dispose();
                    WriteEndlessPerformanceReport(folder, recorder, phases, arrivals, timeline?.Directory);
                }
                finally
                {
                    try
                    {
                        recorder?.Dispose();
                        if (root != null) { root.AutomaticSimulation = false; root.SetDuelLogging(false); }
                    }
                    finally
                    {
                        try { gameView?.Dispose(); }
                        finally
                        {
                            Time.captureDeltaTime = captureDelta;
                            Application.targetFrameRate = targetRate;
                            QualitySettings.vSyncCount = vSync;
                            CombatTestRoot.UseLegacyArenaForTests = useLegacy;
                            CombatTestRoot.WarehouseSeedForTests = warehouseSeed;
                        }
                    }
                }
            }
        }

        private void AssertEndlessPreparedAnimationGraphs()
        {
            foreach (CombatActor actor in root.Opponents)
                Assert.That(actor.NpcAnimationGraphBuilds, Is.EqualTo(1),
                    "Arrival, reset and recycling must reuse each prepared opponent's native animation graph.");
        }

        private void PrepareEndlessPerformanceArrival()
        {
            var state = (EndlessCombatState)typeof(CombatTestRoot).GetField("endless",
                BindingFlags.Instance | BindingFlags.NonPublic).GetValue(root);
            Assert.That(state.Advance(root.NextOpponentSeconds + CombatTestRoot.SimulationStep, root.LiveOpponentCount), Is.True);
        }

        private static double EndlessMilliseconds(long ticks) => ticks * 1000d / Stopwatch.Frequency;

        private static IEnumerator MeasureEndlessPerformancePhase(EndlessPerformanceRecorder recorder,
            List<EndlessPerformancePhase> phases, Dictionary<int, string> timelinePhases, string name, int frames)
        {
            var phase = new EndlessPerformancePhase(name);
            phases.Add(phase);
            for (int sample = 0; sample < frames; sample++)
            {
                int unityFrame = Time.frameCount;
                timelinePhases[unityFrame] = name;
                recorder.Begin();
                Profiler.BeginSample(EndlessPerformanceFramePrefix + unityFrame.ToString(CultureInfo.InvariantCulture));
                Profiler.EndSample();
                yield return null;
                recorder.End(phase, unityFrame);
            }
            phase.Renderers = recorder.RendererCounts();
            phase.BakeContext = recorder.BakeContext();
        }

        private const string EndlessPerformanceFramePrefix = "Test endless performance frame ";

        private sealed class EndlessPerformancePhase
        {
            internal readonly RealCarryFrameBucket Cloth;
            internal readonly CarryPerformanceStage Tick = new CarryPerformanceStage(), Pose = new CarryPerformanceStage();
            internal readonly CarryPerformanceStage Capture = new CarryPerformanceStage(), Effects = new CarryPerformanceStage();
            internal readonly CarryPerformanceStage NpcSampling = new CarryPerformanceStage(), NpcComposition = new CarryPerformanceStage();
            internal readonly CarryPerformanceStage NpcSnapshot = new CarryPerformanceStage(), Gpu = new CarryPerformanceStage();
            internal readonly List<string> Frames = new List<string>(30);
            internal string Renderers, BakeContext;
            internal readonly int[,] BakeResults = new int[3, Enum.GetValues(typeof(SecondaryMotionBakeResult)).Length];
            internal readonly int[] SlingDecisions = new int[Enum.GetValues(typeof(CombatShotgunSling.BakedPlaybackDecision)).Length];
            internal float MaximumSlingEntryFit, MaximumSlingEntryElapsed;
            internal EndlessPerformancePhase(string name) => Cloth = new RealCarryFrameBucket(name, true);
        }

        private sealed class EndlessPerformanceRecorder : IDisposable
        {
            private readonly CombatTestRoot root;
            private readonly RealCarryFrameRecorder cloth;
            private readonly CombatShotgunSling sling;
            private readonly FrameTiming[] timings = new FrameTiming[1];
            private readonly bool gpuEnabled = FrameTimingManager.IsFeatureEnabled();
            private readonly SkinnedMeshRenderer[] skins;
            private readonly CombatActor[] skinOwners;
            private readonly bool previousBakeRejectionCapture;
            private long tick, pose, capture, effects, sampling, composition, snapshot;
            private ulong timingStamp;
            private int timingRepeats = -1;
            internal EndlessPerformanceRecorder(CombatTestRoot root)
            {
                this.root = root; cloth = new RealCarryFrameRecorder(root);
                sling = root.Hero.CarriedWeapon(CombatWeaponId.Shotgun).GetComponent<CombatShotgunSling>();
                previousBakeRejectionCapture = SecondaryMotionBakeRuntime.CaptureRejectionDetails;
                SecondaryMotionBakeRuntime.CaptureRejectionDetails = true;
                skins = root.GetComponentsInChildren<SkinnedMeshRenderer>(true);
                skinOwners = new CombatActor[skins.Length];
                for (int i = 0; i < skins.Length; i++) skinOwners[i] = skins[i].GetComponentInParent<CombatActor>(true);
            }
            internal PlayerJacketCloth[] Jackets => cloth.Jackets;
            internal void Begin()
            {
                tick = root.TotalSimulationTicks; pose = root.SimulationPoseTicks;
                capture = root.SimulationCaptureTicks; effects = root.SimulationEffectsTicks;
                ReadNpcCounters(out sampling, out composition, out snapshot);
                if (gpuEnabled) FrameTimingManager.CaptureFrameTimings();
                cloth.Begin();
            }
            internal void End(EndlessPerformancePhase phase, int unityFrame)
            {
                cloth.End(phase.Cloth);
                if (root.Player.Visual is Player3DCharacterPresentation hero)
                    foreach (SecondaryMotionBakePart part in Enum.GetValues(typeof(SecondaryMotionBakePart)))
                        phase.BakeResults[(int)part, (int)SecondaryMotionBakeRuntime.LastResult(hero.Registry, part)]++;
                phase.SlingDecisions[(int)sling.LastBakedDecision]++;
                phase.MaximumSlingEntryFit = Mathf.Max(phase.MaximumSlingEntryFit, sling.BakedEntryFitError);
                phase.MaximumSlingEntryElapsed = Mathf.Max(phase.MaximumSlingEntryElapsed, sling.BakedEntryElapsedSeconds);
                ReadNpcCounters(out long sampled, out long composed, out long captured);
                double tickMs = EndlessMilliseconds(root.TotalSimulationTicks - tick), poseMs = EndlessMilliseconds(root.SimulationPoseTicks - pose);
                double captureMs = EndlessMilliseconds(root.SimulationCaptureTicks - capture), effectsMs = EndlessMilliseconds(root.SimulationEffectsTicks - effects);
                phase.Tick.Add(tickMs, 1); phase.Pose.Add(poseMs, 1); phase.Capture.Add(captureMs, 1); phase.Effects.Add(effectsMs, 1);
                phase.NpcSampling.Add(EndlessMilliseconds(sampled - sampling), 1);
                phase.NpcComposition.Add(EndlessMilliseconds(composed - composition), 1);
                phase.NpcSnapshot.Add(EndlessMilliseconds(captured - snapshot), 1);
                double gpu = double.NaN, presentWait = double.NaN;
                if (gpuEnabled && FrameTimingManager.GetLatestTimings(1, timings) > 0 && timings[0].cpuFrameTime > 0d)
                {
                    timingRepeats = timings[0].frameStartTimestamp == timingStamp ? timingRepeats + 1 : 0;
                    timingStamp = timings[0].frameStartTimestamp;
                    if (timings[0].gpuFrameTime > 0d) { gpu = timings[0].gpuFrameTime; phase.Gpu.Add(gpu, 1); }
                    presentWait = timings[0].cpuMainThreadPresentWaitTime;
                }
                RealCarryFrameSample frame = phase.Cloth.Samples[phase.Cloth.Samples.Count - 1];
                phase.Frames.Add(FormattableString.Invariant($"{phase.Cloth.Name},{unityFrame},{Time.frameCount},{frame.Wall:F5},{tickMs:F5},{poseMs:F5},{captureMs:F5},{effectsMs:F5},{frame.Cloth:F5},{frame.Jacket:F5},{frame.Hair:F5},{frame.Scarf:F5},{frame.Sling:F5},{frame.MainThread:F5},{frame.PlayerLoop:F5},{frame.GcBytes},{gpu:F5},{presentWait:F5},{timingRepeats},{root.LiveOpponentCount}"));
            }
            private void ReadNpcCounters(out long sampled, out long composed, out long captured)
            {
                sampled = composed = captured = 0;
                foreach (CombatActor actor in root.Opponents)
                { sampled += actor.NpcAnimationSampleTicks; composed += actor.NpcPoseCompositionTicks; captured += actor.NpcPoseSnapshotTicks; }
            }
            internal string RendererCounts()
            {
                int allEnabled = 0, bodyPrepared = 0, bodyEnabled = 0, heroEnabled = 0, npcEnabled = 0;
                for (int i = 0; i < skins.Length; i++)
                {
                    SkinnedMeshRenderer skin = skins[i];
                    if (skin == null) continue;
                    bool partition = skin.name.StartsWith("Body ", StringComparison.Ordinal);
                    if (partition) bodyPrepared++;
                    if (!skin.enabled || !skin.gameObject.activeInHierarchy) continue;
                    allEnabled++;
                    if (partition) bodyEnabled++;
                    if (skinOwners[i] == root.Hero) heroEnabled++;
                    else if (skinOwners[i] != null) npcEnabled++;
                }
                return FormattableString.Invariant($"Skinned renderers at phase end: prepared all={skins.Length}, prepared Body partitions={bodyPrepared}; enabled and active all={allEnabled}, Body partitions={bodyEnabled}, source/other={allEnabled - bodyEnabled}, hero={heroEnabled}, NPC={npcEnabled}; hero health={root.Hero.State.Health:F3}. Renderer inventory is captured once, not scanned per frame.");
            }
            internal void AppendAvailability(StringBuilder report)
            {
                cloth.AppendAvailability(report); report.AppendLine("FrameTiming feature enabled=" + gpuEnabled + "; unsupported latest timings remain NaN and absent recorder samples use -1; repeated latest timing is not an exact frame age.");
                if (root?.Player.Visual is Player3DCharacterPresentation bakedHero)
                    foreach (SecondaryMotionBakePart part in Enum.GetValues(typeof(SecondaryMotionBakePart)))
                        report.AppendLine("Secondary motion context " + part + ": " + SecondaryMotionBakeRuntime.DescribeContext(bakedHero.Registry, part));
            }
            internal string BakeContext() => root?.Player.Visual is Player3DCharacterPresentation hero
                ? "Phase-end jacket context: " + SecondaryMotionBakeRuntime.DescribeContext(hero.Registry, SecondaryMotionBakePart.Jacket)
                : "No hero secondary-motion context.";
            internal string Rejection(SecondaryMotionBakePart part) => root.Player.Visual is Player3DCharacterPresentation hero
                ? SecondaryMotionBakeRuntime.LastRejection(hero.Registry, part) : null;
            public void Dispose()
            { SecondaryMotionBakeRuntime.CaptureRejectionDetails = previousBakeRejectionCapture; cloth.Dispose(); }
        }

        private sealed class EndlessCpuTimelineScope : IDisposable
        {
            private readonly Type type;
            private bool stopped;
            internal string Directory { get; private set; }
            internal EndlessCpuTimelineScope(string folder)
            {
                foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
                    type ??= assembly.GetType("BarPromenade.Editor.CombatCpuTimelineCapture");
                Assert.That(type, Is.Not.Null);
                LogAssert.Expect(LogType.Log, new Regex("^Combat CPU Timeline recording for 15 seconds: " +
                    Regex.Escape(Path.GetFullPath(folder)) + @"[\\/]Manual duel [0-9]{4}-[0-9]{2}-[0-9]{2} [0-9]{2}-[0-9]{2}-[0-9]{2}-[0-9]{3}(?: [1-9][0-9]*)?$"));
                Assert.That(type.GetMethod("StartCapture", BindingFlags.Static | BindingFlags.NonPublic)
                    .Invoke(null, new object[] { folder }), Is.True, "The optional Timeline must start without taking another recording's ownership.");
                Directory = (string)type.GetProperty("LastCaptureDirectory", BindingFlags.Static | BindingFlags.NonPublic).GetValue(null);
                LogAssert.Expect(LogType.Log, new Regex(@"^Combat CPU Timeline saved \((?:endless_performance_completed|duration_reached)\): " +
                    Regex.Escape(Directory) + "$"));
            }
            public void Dispose()
            {
                if (stopped) return;
                stopped = true;
                type.GetMethod("StopCapture", BindingFlags.Static | BindingFlags.NonPublic)
                    .Invoke(null, new object[] { "endless_performance_completed" });
            }
        }

        private static void WriteEndlessPerformanceReport(string folder, EndlessPerformanceRecorder recorder,
            List<EndlessPerformancePhase> phases, List<string> arrivals, string timelineDirectory)
        {
            Directory.CreateDirectory(folder);
            var report = new StringBuilder();
            report.AppendLine("Test rendered Endless warehouse; seed=-41128863; hero=Pistol plus carried Shotgun; NPC=Crowbar; capacity=5; controlled first/refill schedule=60s; captureDeltaTime=0; target=60; vSync=0; framebuffer=" + Screen.width + "x" + Screen.height + ".");
            report.AppendLine("Machine-specific measurements, without absolute FPS assertions. Coroutine wall covers actual rendered intervals; Tick/pose/contacts/cloth scopes overlap and must not be summed. Latest GPU/present timings can refer to older frames. Duel frame_delivery partitions after-LateUpdate/render submission/next-Update wall time, not GPU completion.");
            report.AppendLine("Between phases only spawn readiness is prepared; activation/reset still belongs to the production Tick. The settlement preparation pauses combat/spawn advancement while real PhysX settles the damaged body, outside timing buckets. Main/PlayerLoop recorder samples can lag; exact source attribution is in the optional CPU Timeline and matching Unity-frame markers.");
            report.AppendLine("CPU Timeline: " + (timelineDirectory ?? "not requested; use -endlessPerformanceCpuTimeline"));
            recorder?.AppendAvailability(report);
            foreach (string arrival in arrivals) report.AppendLine(arrival);
            var csv = new StringBuilder("phase,marker_unity_frame,resume_unity_frame,wall_ms,tick_ms,population_pose_ms,capture_ms,effects_ms,cloth_ms,jacket_ms,hair_ms,scarf_ms,sling_ms,latest_main_thread_ms,player_loop_ms,gc_bytes,latest_gpu_ms,latest_present_wait_ms,latest_timing_repeat_frames,live_opponents\n");
            foreach (EndlessPerformancePhase phase in phases)
            {
                phase.Cloth.AppendTo(report);
                report.AppendLine(phase.BakeContext);
                report.Append("Sling playback decisions:");
                foreach (CombatShotgunSling.BakedPlaybackDecision decision in Enum.GetValues(typeof(CombatShotgunSling.BakedPlaybackDecision)))
                    if (phase.SlingDecisions[(int)decision] > 0)
                        report.Append(" " + decision + "=" + phase.SlingDecisions[(int)decision]);
                report.AppendLine(FormattableString.Invariant($"; max entry elapsed={phase.MaximumSlingEntryElapsed:F5}s; max entry fit={phase.MaximumSlingEntryFit:F6}m."));
                foreach (SecondaryMotionBakePart part in Enum.GetValues(typeof(SecondaryMotionBakePart)))
                {
                    report.Append("Sample decisions " + part + ":");
                    foreach (SecondaryMotionBakeResult decision in Enum.GetValues(typeof(SecondaryMotionBakeResult)))
                        if (phase.BakeResults[(int)part, (int)decision] > 0)
                            report.Append(" " + decision + "=" + phase.BakeResults[(int)part, (int)decision]);
                    report.AppendLine();
                    if (recorder?.Rejection(part) is string rejection) report.AppendLine("First rejected pose " + part + ": " + rejection);
                }
                report.AppendLine(phase.Renderers ?? "Renderer snapshot unavailable: phase did not finish.");
                phase.Tick.AppendTo(report, "Full production Tick CPU"); phase.Pose.AppendTo(report, "Population Present CPU (nested in Tick)");
                phase.Capture.AppendTo(report, "Population contact capture CPU (nested in Tick)"); phase.Effects.AppendTo(report, "Population effects CPU (nested in Tick)");
                phase.NpcSampling.AppendTo(report, "NPC animation sample CPU (nested in Present)");
                phase.NpcComposition.AppendTo(report, "NPC pose composition CPU (nested in Present)");
                phase.NpcSnapshot.AppendTo(report, "NPC pose snapshot CPU (nested in Present)");
                if (phase.Gpu.Values.Count > 0) phase.Gpu.AppendTo(report, "Latest available GPU timing");
                foreach (string frame in phase.Frames) csv.AppendLine(frame);
            }
            File.WriteAllText(Path.Combine(folder, "Report.txt"), report.ToString());
            File.WriteAllText(Path.Combine(folder, "Frames.csv"), csv.ToString());
            TestContext.Progress.WriteLine("Test combat performance evidence: " + folder + "; CPU Timeline: " + (timelineDirectory ?? "not requested"));
        }

        private static void ExportEndlessPerformanceTimeline(string folder, Dictionary<int, string> phases)
        {
#if UNITY_EDITOR
            var csv = new StringBuilder("profiler_frame,unity_frame,phase,thread,sample,parent,start_ms,duration_ms,name\n");
            int correlated = 0;
            for (int frame = ProfilerDriver.firstFrameIndex; frame <= ProfilerDriver.lastFrameIndex; frame++)
            {
                int unityFrame = -1;
                using (var main = ProfilerDriver.GetRawFrameDataView(frame, 0))
                {
                    if (!main.valid) continue;
                    for (int sample = 0; sample < main.sampleCount; sample++)
                    {
                        string name = main.GetSampleName(sample);
                        if (name.StartsWith(EndlessPerformanceFramePrefix, StringComparison.Ordinal) &&
                            int.TryParse(name.Substring(EndlessPerformanceFramePrefix.Length), out int parsed)) unityFrame = parsed;
                    }
                }
                if (!phases.TryGetValue(unityFrame, out string phase)) continue;
                correlated++;
                for (int thread = 0; thread < 32; thread++)
                {
                    using var data = ProfilerDriver.GetRawFrameDataView(frame, thread);
                    if (!data.valid) break;
                    if (data.threadName != "Main Thread" && data.threadName != "Render Thread") continue;
                    var parents = new Stack<(int sample, int end)>();
                    for (int sample = 0; sample < data.sampleCount; sample++)
                    {
                        while (parents.Count > 0 && sample > parents.Peek().end) parents.Pop();
                        int parent = parents.Count > 0 ? parents.Peek().sample : -1;
                        double duration = data.GetSampleTimeMs(sample);
                        if (duration >= .05d)
                            csv.AppendLine(FormattableString.Invariant($"{frame},{unityFrame},{phase},{data.threadName},{sample},{parent},{data.GetSampleStartTimeMs(sample):F5},{duration:F5},\"{data.GetSampleName(sample).Replace("\"", "\"\"")}\""));
                        int children = data.GetSampleChildrenCountRecursive(sample);
                        if (children > 0) parents.Push((sample, sample + children));
                    }
                }
            }
            File.WriteAllText(Path.Combine(folder, "Timeline.csv"), csv.ToString());
            TestContext.Progress.WriteLine("Test Endless Timeline correlated frames retained in history: " + correlated + "; the complete streamed trace retains older frames too.");
            Assert.That(correlated, Is.GreaterThan(0), "The optional CPU Timeline must retain the measured damaged/recycled tail.");
#endif
        }

        [UnityTest]
        public IEnumerator Range_EndlessProjectilesResolveEveryVictim()
        {
            yield return SceneManager.LoadSceneAsync(SceneIds.MainMenu, LoadSceneMode.Single);
            var selection = new CombatTestSelection(new CombatLoadoutSelection(
                CombatWeaponId.Shotgun, CombatWeaponId.Shotgun), CombatTestMode.Endless,
                new CombatEncounterSettings(5, .05f, .05f, .08f, .08f));
            Assert.That(CombatTestStartService.TryStart(selection), Is.True);
            yield return AwaitSelectedCombatRange();
            root.Tick(.4f);
            Assert.That(root.LiveOpponentCount, Is.EqualTo(5));
            CombatActor selected = root.Opponent;
            CombatActor nearer = root.Opponents[0] == selected ? root.Opponents[1] : root.Opponents[0];
            var participants = EndlessParticipants();
            PlaceEndlessProjectileActors(selected, nearer);
            Assert.That(nearer, Is.Not.SameAs(root.Opponent), "The nearest victim is deliberately outside camera selection.");
            nearer.CaptureContactPose();
            Assert.That(nearer.Hurtboxes.GetRegionFrame(MeleeBodyRegion.Head, out Vector3 head, out _, out _), Is.True);
            Assert.That(root.Projectiles.TrySpawn(root.Hero, head - Vector3.forward * .75f,
                Vector3.forward * CombatProjectilePool.MuzzleSpeed, 10), Is.True);
            root.Projectiles.Advance(.025f, participants);
            root.Projectiles.ApplyContacts();
            Assert.That(nearer.State.IsDefeated, Is.True, "A nonselected nearer body stops and receives the real head projectile.");
            Assert.That(selected.State.Health, Is.EqualTo(selected.State.Settings.MaxHealth), "Roster order cannot send a flight through the nearer body.");
            Assert.That(nearer.ReceivedImpactCount, Is.EqualTo(1));

            PlaceEndlessProjectileActors(selected, nearer);
            nearer.Hurtboxes.GetRegionFrame(MeleeBodyRegion.Head, out head, out _, out _);
            var wallObject = new GameObject("Test projectile barrier");
            wallObject.transform.SetParent(root.transform, false);
            wallObject.transform.position = head - Vector3.forward * .35f;
            BoxCollider wall = wallObject.AddComponent<BoxCollider>();
            wall.size = new Vector3(3f, 3f, .1f);
            Physics.SyncTransforms();
            int surfaceResponses = root.Projectiles.SurfaceEffects.EmissionCount;
            Assert.That(root.Projectiles.TrySpawn(root.Hero, head - Vector3.forward * .75f,
                Vector3.forward * CombatProjectilePool.MuzzleSpeed, 11), Is.True);
            root.Projectiles.Advance(.025f, participants);
            root.Projectiles.ApplyContacts();
            Assert.That(nearer.State.Health, Is.EqualTo(nearer.State.Settings.MaxHealth));
            Assert.That(selected.State.Health, Is.EqualTo(selected.State.Settings.MaxHealth));
            Assert.That(root.Projectiles.SurfaceEffects.EmissionCount, Is.GreaterThan(surfaceResponses), "The closest wall owns the contact ahead of every body.");
            wall.enabled = false;
            Object.Destroy(wallObject);

            // A twelve-pellet spread reaches two separate bodies on one plane.
            // Contacts on later flight segments must not repeat either response.
            PlaceEndlessProjectileActors(selected, nearer);
            selected.ResetActor(new Vector3(-.45f, PlayerFactory.GroundedRootOffset, 5f), Vector3.back);
            nearer.ResetActor(new Vector3(.45f, PlayerFactory.GroundedRootOffset, 5f), Vector3.back);
            CaptureEndlessProjectilePoses();
            float chestHeight = (selected.Ragdoll.PhysicsController.ChestBody.worldCenterOfMass.y +
                nearer.Ragdoll.PhysicsController.ChestBody.worldCenterOfMass.y) * .5f;
            var firstImpacts = new List<CombatImpact>();
            var secondImpacts = new List<CombatImpact>();
            selected.ImpactReceived += firstImpacts.Add;
            nearer.ImpactReceived += secondImpacts.Add;
            try
            {
                Assert.That(root.Projectiles.TrySpawnVolley(root.Hero, new Vector3(0f, chestHeight, -6.5f),
                    Quaternion.identity, 12, root.Hero.Shotgun.Settings), Is.True);
                for (int tick = 0; tick < 60 && root.Projectiles.ActiveCount > 0; tick++)
                {
                    root.Projectiles.Advance(CombatTestRoot.SimulationStep, participants);
                    root.Projectiles.ApplyContacts();
                }
                Assert.That(root.Projectiles.ActiveCount, Is.Zero);
                Assert.That(firstImpacts, Is.Not.Empty, "The same spread must reach the left body.");
                Assert.That(secondImpacts, Is.Not.Empty, "The same spread must reach the right body.");
                Assert.That(selected.ShotgunVolleyResponseCount, Is.EqualTo(1));
                Assert.That(nearer.ShotgunVolleyResponseCount, Is.EqualTo(1));
                var pelletIds = new HashSet<int>();
                foreach (CombatImpact impact in firstImpacts)
                {
                    Assert.That(impact.Target, Is.SameAs(selected));
                    Assert.That(impact.AttackSequence, Is.EqualTo(12));
                    Assert.That(impact.IsPellet && pelletIds.Add(impact.PelletIndex), Is.True);
                }
                foreach (CombatImpact impact in secondImpacts)
                {
                    Assert.That(impact.Target, Is.SameAs(nearer));
                    Assert.That(impact.AttackSequence, Is.EqualTo(12));
                    Assert.That(impact.IsPellet && pelletIds.Add(impact.PelletIndex), Is.True,
                        "One physical pellet cannot be granted to both victims.");
                }
                int firstResponses = firstImpacts.FindAll(impact => impact.PrimaryResponse).Count;
                int secondResponses = secondImpacts.FindAll(impact => impact.PrimaryResponse).Count;
                Assert.That(firstResponses, Is.EqualTo(1));
                Assert.That(secondResponses, Is.EqualTo(1));
                Assert.That(selected.State.Health, Is.LessThan(selected.State.Settings.MaxHealth));
                Assert.That(nearer.State.Health, Is.LessThan(nearer.State.Settings.MaxHealth));
            }
            finally
            {
                selected.ImpactReceived -= firstImpacts.Add;
                nearer.ImpactReceived -= secondImpacts.Add;
            }

            // Recycling cancels only one body's queued old-generation contact.
            // Another queued victim and a still-flying object retain ownership.
            PlaceEndlessProjectileActors(selected, nearer);
            selected.ResetActor(new Vector3(-2f, PlayerFactory.GroundedRootOffset, 0f), Vector3.back);
            nearer.ResetActor(new Vector3(2f, PlayerFactory.GroundedRootOffset, 0f), Vector3.back);
            CaptureEndlessProjectilePoses();
            QueueEndlessHeadshot(root.Hero, selected, 20);
            QueueEndlessHeadshot(root.Hero, nearer, 21);
            root.Projectiles.Advance(CombatTestRoot.SimulationStep, participants);
            Assert.That(root.Projectiles.TrySpawn(root.Hero, new Vector3(0f, 8f, 0f), Vector3.up, 22), Is.True);
            root.Projectiles.RetireActorContacts(selected);
            selected.ResetActor(selected.transform.position, Vector3.back);
            root.Projectiles.ApplyContacts();
            Assert.That(selected.State.Health, Is.EqualTo(selected.State.Settings.MaxHealth), "An old-generation impact cannot wound the new body.");
            Assert.That(nearer.State.IsDefeated, Is.True, "Retiring another participant cannot discard this contact.");
            Assert.That(root.Projectiles.ActiveCount, Is.EqualTo(1), "Retiring a body cannot erase an unrelated flight.");
            LogAssert.NoUnexpectedReceived();
        }

        private List<CombatActor> EndlessParticipants()
        {
            var participants = new List<CombatActor>(root.Opponents.Count + 1) { root.Hero };
            foreach (CombatActor actor in root.Opponents) participants.Add(actor);
            return participants;
        }

        private void PlaceEndlessProjectileActors(CombatActor selected, CombatActor nearer)
        {
            root.Hero.ResetActor(new Vector3(0f, PlayerFactory.GroundedRootOffset, -5f), Vector3.forward);
            int parked = 0;
            foreach (CombatActor actor in root.Opponents)
            {
                Vector3 position = actor == selected ? new Vector3(0f, PlayerFactory.GroundedRootOffset, 3f) :
                    actor == nearer ? Vector3.up * PlayerFactory.GroundedRootOffset :
                    new Vector3(5f, PlayerFactory.GroundedRootOffset, -4f + parked++ * 3f);
                actor.ResetActor(position, Vector3.back);
            }
            CaptureEndlessProjectilePoses();
        }

        private void CaptureEndlessProjectilePoses()
        {
            Physics.SyncTransforms();
            root.Hero.CaptureContactPose();
            foreach (CombatActor actor in root.Opponents) actor.CaptureContactPose();
        }

        private IEnumerator CaptureEndlessPopulationGameView(string name = "endless-population-ru")
        {
            Camera camera = root.CameraFollow.Camera;
            bool following = root.CameraFollow.enabled;
            Vector3 position = camera.transform.position;
            Quaternion rotation = camera.transform.rotation;
            float fov = camera.fieldOfView;
            try
            {
                // The existing AreaCaptureFixture warehouse room view, with the
                // active population and HUD retained in the real framebuffer.
                root.CameraFollow.enabled = false;
                Vector3 overview = new Vector3(-10.8f, 4.8f, -7.8f);
                camera.transform.SetPositionAndRotation(overview,
                    Quaternion.LookRotation(new Vector3(0f, 2.2f, 2.5f) - overview));
                camera.fieldOfView = 68f;
                yield return CaptureEndlessGameView(name, "ru");
            }
            finally
            {
                camera.transform.SetPositionAndRotation(position, rotation);
                camera.fieldOfView = fov;
                root.CameraFollow.enabled = following;
            }
        }

        private static void SpendEndlessRound(IFirearmState firearm)
        {
            firearm.SetAim(true);
            firearm.Advance(firearm.Settings.RaiseSeconds + firearm.Settings.FireCooldownSeconds);
            Assert.That(firearm.TryFire(), Is.True);
            firearm.CancelAction();
            Assert.That(firearm.Rounds, Is.EqualTo(firearm.Settings.MagazineCapacity - 1));
        }

        private void QueueEndlessHeadshot(CombatActor source, CombatActor target, int sequence)
        {
            target.CaptureContactPose();
            Assert.That(target.Hurtboxes.GetRegionFrame(MeleeBodyRegion.Head, out Vector3 point,
                out Vector3 forward, out Vector3 up), Is.True);
            Vector3 outward = target.IsHero ? Vector3.Cross(up, forward).normalized : forward;
            Assert.That(root.Projectiles.TrySpawn(source, point + outward * .3f,
                -outward * CombatProjectilePool.MuzzleSpeed, sequence), Is.True);
        }

        private static void AssertEndlessAmmunition(IFirearmState firearm, int sequence)
        {
            Assert.That(firearm.Rounds, Is.EqualTo(firearm.Settings.MagazineCapacity));
            Assert.That(firearm.ShotSequence, Is.EqualTo(sequence), "Refilling must retain the monotonic shot identity.");
            Assert.That(firearm.IsReloading || firearm.ReloadPending, Is.False);
            Assert.That(firearm.MagazineAttached, Is.True);
            if (firearm is ShotgunState shotgun)
            {
                Assert.That(shotgun.ChamberLoaded(0) && shotgun.ChamberLoaded(1), Is.True);
                Assert.That(shotgun.ChamberSpent(0) || shotgun.ChamberSpent(1), Is.False);
            }
        }

        private static IEnumerator CaptureEndlessGameView(string name, string language)
        {
            const int width = 640, height = 360;
            int previousWidth = Screen.width, previousHeight = Screen.height;
            FullScreenMode previousMode = Screen.fullScreenMode;
            IDisposable gameView = null;
            using (var localized = new EndlessLanguageScope(language))
            {
                try
                {
#if UNITY_EDITOR
                    // Reuse the existing temporary Game View size owner, including
                    // its restoration, rather than introduce another editor helper.
                    Type sizeOwner = typeof(AreaLoadingVisualCapture).GetNestedType("GameViewResolution", BindingFlags.NonPublic);
                    Assert.That(sizeOwner, Is.Not.Null);
                    gameView = (IDisposable)Activator.CreateInstance(sizeOwner, true);
                    sizeOwner.GetMethod("Set").Invoke(gameView, new object[] { width, height });
#else
                    Screen.SetResolution(width, height, FullScreenMode.Windowed);
#endif
                    float deadline = Time.realtimeSinceStartup + 1f;
                    while ((Screen.width != width || Screen.height != height) && Time.realtimeSinceStartup < deadline)
                        yield return null;
                    // Batch Editor does not execute WaitForEndOfFrame. Attempt its
                    // real framebuffer as well; no camera-only substitute is used.
                    if (Application.isBatchMode) { yield return null; yield return null; }
                    else yield return new WaitForEndOfFrame();
                    Texture2D frame = ScreenCapture.CaptureScreenshotAsTexture();
                    try
                    {
                        Assert.That(frame, Is.Not.Null, "The endless UI capture requires a rendered framebuffer.");
                        string path = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "Captures", SceneIds.CombatTest, name + ".png"));
                        Directory.CreateDirectory(Path.GetDirectoryName(path));
                        File.WriteAllBytes(path, frame.EncodeToPNG());
                        TestContext.Progress.WriteLine("Endless UI frame: " + path + "; requested " + width + "x" + height +
                            "; framebuffer " + frame.width + "x" + frame.height + "; batch=" + Application.isBatchMode);
                        if (!Application.isBatchMode)
                            Assert.That(new Vector2Int(frame.width, frame.height), Is.EqualTo(new Vector2Int(width, height)));
                    }
                    finally { if (frame != null) Object.Destroy(frame); }
                }
                finally
                {
                    gameView?.Dispose();
#if !UNITY_EDITOR
                    Screen.SetResolution(previousWidth, previousHeight, previousMode);
#endif
                }
            }
        }

        [Serializable]
        private sealed class EndlessCatalog { public EndlessCatalogEntry[] entries; }
        [Serializable]
        private sealed class EndlessCatalogEntry { public string key; public string value; }

        private sealed class EndlessLanguageScope : IDisposable
        {
            private readonly Dictionary<string, string> values, previous;
            public EndlessLanguageScope(string language)
            {
                LocalizationService.Get("combat.title");
                values = (Dictionary<string, string>)typeof(LocalizationService).GetField("Values",
                    BindingFlags.NonPublic | BindingFlags.Static).GetValue(null);
                previous = new Dictionary<string, string>(values);
                TextAsset asset = Resources.Load<TextAsset>("Localization/" + language);
                Assert.That(asset, Is.Not.Null);
                foreach (EndlessCatalogEntry entry in JsonUtility.FromJson<EndlessCatalog>(asset.text).entries)
                    values[entry.key] = entry.value;
            }
            public void Dispose()
            {
                values.Clear();
                foreach (var entry in previous) values.Add(entry.Key, entry.Value);
            }
        }
    }
}

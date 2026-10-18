using System;
using System.Collections;
using System.IO;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

namespace BarPromenade.Tests.PlayMode
{
    public sealed class CombatCraneAssetsSetup : IPrebuildSetup
    {
        public void Setup()
        {
#if UNITY_EDITOR
            Type.GetType("BarPromenade.Editor.CombatCraneAssetSetup, BarPromenade.Editor", true)
                .GetMethod("BuildOrThrow", Type.EmptyTypes).Invoke(null, null);
#endif
        }
    }

    public sealed partial class CombatTestPlayModeTests
    {
        private Transform[,] preparedCraneCables;
        private Transform preparedCraneHook, preparedCraneHookCable;
        private int observedCraneThrows, craneMovieFrames;
        private Vector3 observedCraneAim, observedCraneOrigin;
        private float maximumCraneSwing, maximumCraneLimbEnergy, craneOriginalYaw;
        private bool recordCraneMotion;
        private AudioSource[] craneVoices;
        private Vector3 craneOriginalAnchor, craneMinimumCommand, craneMaximumCommand;

        [UnityTest]
        public IEnumerator Range_CraneOwnHookKnockdownTriggersOneSuspendedFrenzy()
        {
            CombatTestRoot.UseLegacyArenaForTests = false;
            CombatTestRoot.WarehouseSeedForTests = 0;
            yield return SceneManager.LoadSceneAsync(SceneIds.MainMenu, LoadSceneMode.Single);
            Assert.That(CombatTestStartService.TryStart(new CombatTestSelection(
                CombatLoadoutSelection.Default, CombatTestMode.Crane)), Is.True);
            yield return WaitFor(() =>
            {
                root = Object.FindAnyObjectByType<CombatTestRoot>();
                return root != null && root.IsInitialized && !SceneTransitionService.IsTransitioning &&
                    GameInput.CanRead(GameInputContext.Gameplay);
            }, "The crane knockdown regression did not reach live gameplay.");
            root.AutomaticSimulation = false;
            root.SetSparring(true);
            CombatActor crane = root.Opponent, hero = root.Hero;
            Assert.That(crane.IsCrane, Is.True);
            Assert.That(crane.GetComponent<CombatZombieLoad>(), Is.Null,
                "The suspended actor must reach the fall callback without a porter's loaded pelvis state.");
            preparedCraneCables = new Transform[crane.CraneCableCount, CombatActor.CraneCableSegments];
            for (int i = 0; i < crane.CraneCableCount; i++)
                for (int segment = 0; segment < CombatActor.CraneCableSegments; segment++)
                    preparedCraneCables[i, segment] = crane.GetCraneCableTransform(i, segment);
            preparedCraneHook = crane.GetCraneHookTransform();
            preparedCraneHookCable = crane.GetCraneHookCableTransform();
            observedCraneThrows = crane.CraneHookThrowCount;
            craneOriginalAnchor = crane.CraneAnchorPosition;
            craneOriginalYaw = crane.transform.eulerAngles.y;
            craneMinimumCommand = craneMaximumCommand = crane.CraneCarriagePosition;
            var cableStarts = new Vector3[crane.CraneCableCount];
            for (int i = 0; i < cableStarts.Length; i++) cableStarts[i] = crane.GetCraneCableStart(i);
            int animationGraphs = crane.NpcAnimationGraphBuilds, knockdowns = 0;
            void RecordFall(CombatImpact impact) { knockdowns++; }
            hero.KnockedDown += RecordFall;
            try
            {
                yield return BeginCraneHookKnockdown(crane);
                CombatImpact realHit = hero.LastImpact;
                Assert.That(knockdowns, Is.EqualTo(1));
                Assert.That(crane.IsCraneFrenzied, Is.True);
                Assert.That(crane.CraneFrenzyPlayCount, Is.EqualTo(1));
                Assert.That(crane.CraneFrenzySecondsElapsed, Is.LessThanOrEqualTo(CombatTestRoot.SimulationStep));
                AudioSource sound = Array.Find(crane.GetComponentsInChildren<AudioSource>(true),
                    voice => voice.clip != null && voice.clip.name.StartsWith("ZombieFrenzy", StringComparison.Ordinal));
                Assert.That(sound, Is.Not.Null, "The physical knockdown starts the shared organic frenzy cue.");
                Assert.That(sound.loop || sound.ignoreListenerPause, Is.False);
                Vector3 heldCommand = crane.CraneCarriagePosition;
                int heldThrows = crane.CraneHookThrowCount, attack = crane.State.AttackSequence;
                Transform head = FindAnatomicalBone(crane, "head"), hand = FindAnatomicalBone(crane, "hand.L");
                Quaternion firstHead = head.localRotation, firstHand = hand.localRotation;
                float headMotion = 0f, handMotion = 0f;
                bool sampledEarly = false, sampledLate = false, paused = false;
                for (int frame = 0; frame < 80 && crane.IsCraneFrenzied; frame++)
                {
                    Assert.That(crane.TryBeginCraneCycle(hero), Is.False, "The outburst owns the attack and patrol opening.");
                    Assert.That(crane.State.AttackSequence, Is.EqualTo(attack));
                    Assert.That(crane.CraneHookThrowCount, Is.EqualTo(heldThrows));
                    Assert.That(crane.CraneCarriagePosition, Is.EqualTo(heldCommand));
                    Assert.That(crane.CraneFrenzyPlayCount, Is.EqualTo(1));
                    Assert.That(hero.ReceivedImpactCount, Is.EqualTo(1), "The returning hook remains harmless.");
                    AssertCraneCablesFollowRig(crane, cableStarts);
                    if (crane.CraneFrenzySecondsElapsed >= 1f)
                    {
                        headMotion = Mathf.Max(headMotion, Quaternion.Angle(firstHead, head.localRotation));
                        handMotion = Mathf.Max(handMotion, Quaternion.Angle(firstHand, hand.localRotation));
                        if (!sampledEarly)
                        {
                            CaptureCraneFrenzyView(crane, "Test crane - Knockdown frenzy");
                            sampledEarly = true;
                        }
                        if (!sampledLate && crane.CraneFrenzySecondsElapsed >= 2.5f)
                        {
                            CaptureCraneFrenzyView(crane, "Test crane - Suspended frenzy motion");
                            sampledLate = true;
                        }
                    }
                    if (!paused && crane.CraneFrenzySecondsElapsed >= .5f)
                    {
                        float clock = crane.CraneFrenzySecondsElapsed;
                        Assert.That(hero.TryBeginKnockdown(realHit, realHit.Direction, Vector3.zero), Is.False);
                        Assert.That(knockdowns, Is.EqualTo(1));
                        Assert.That(crane.CraneFrenzySecondsElapsed, Is.EqualTo(clock));
                        Quaternion heldHead = head.localRotation, heldHand = hand.localRotation;
                        Vector3 heldBody = crane.transform.position, heldHook = crane.CraneHookPosition;
                        Assert.That(root.PauseMenu.Open(), Is.True);
                        TickCrane(crane, 1f);
                        yield return null;
                        int samples = sound.timeSamples;
                        yield return null;
                        Assert.That(crane.CraneFrenzySecondsElapsed, Is.EqualTo(clock));
                        Assert.That(crane.transform.position, Is.EqualTo(heldBody));
                        Assert.That(crane.CraneHookPosition, Is.EqualTo(heldHook));
                        Assert.That(Quaternion.Angle(head.localRotation, heldHead), Is.LessThan(.01f));
                        Assert.That(Quaternion.Angle(hand.localRotation, heldHand), Is.LessThan(.01f));
                        Assert.That(sound.timeSamples, Is.EqualTo(samples));
                        AssertCraneCablesFollowRig(crane, cableStarts);
                        Assert.That(root.PauseMenu.Cancel(), Is.True);
                        yield return WaitFor(() => GameInput.CanRead(GameInputContext.Gameplay), "Pause did not release the crane frenzy.");
                        paused = true;
                    }
                    float remaining = CombatActor.ZombieFrenzyDurationSeconds - crane.CraneFrenzySecondsElapsed;
                    if (remaining <= .1001f) crane.Step(remaining);
                    else TickCrane(crane, .1f);
                }
                Assert.That(sampledEarly && sampledLate && paused, Is.True);
                Assert.That(headMotion, Is.GreaterThan(5f), "The suspended head visibly participates in the outburst.");
                Assert.That(handMotion, Is.GreaterThan(5f), "The same continuous suspended rig drives the arm motion.");
                Assert.That(crane.IsCraneFrenzied, Is.False);
                Assert.That(crane.CraneFrenzySecondsElapsed, Is.EqualTo(CombatActor.ZombieFrenzyDurationSeconds).Within(.001f));
                Assert.That(sound.isPlaying, Is.False, "Simulation expiry stops the cue even when DSP playback has not caught up.");
                Assert.That(crane.NpcAnimationGraphBuilds, Is.EqualTo(animationGraphs));
                PlaceCraneHero(new Vector3(0f, WarehouseArenaPlan.TopHeight, -6.6f), Vector3.forward);
                Assert.That(hero.TryBeginKnockdown(realHit, realHit.Direction * .8f, Vector3.zero), Is.True);
                Assert.That(crane.IsCraneFrenzied, Is.False, "A new physical fall attributed to the same old strike cannot restart the cue.");
                Assert.That(crane.CraneFrenzyPlayCount, Is.EqualTo(1));
                PlaceCraneHero(new Vector3(11f, 0f, 0f), Vector3.left);
                TickCrane(crane, 3.2f);
                Assert.That(crane.CranePatrolTravelling, Is.True, "The ordinary independent patrol resumes after the complete outburst.");

                root.ResetRound();
                observedCraneThrows = crane.CraneHookThrowCount;
                yield return BeginCraneHookKnockdown(crane);
                TickCrane(crane, .4f);
                Assert.That(crane.IsCraneFrenzied, Is.True);
                root.ResetRound();
                observedCraneThrows = crane.CraneHookThrowCount;
                Assert.That(crane.IsCraneFrenzied, Is.False);
                Assert.That(crane.CraneFrenzySecondsElapsed, Is.Zero);
                Assert.That(crane.CraneFrenzyPlayCount, Is.Zero);
                Assert.That(sound.isPlaying, Is.False, "Reset stops the retained voice immediately.");
                Assert.That(crane.CraneHookPhase, Is.EqualTo(CombatCraneHookPhase.Stowed));
                AssertCraneCablesFollowRig(crane, cableStarts);
                LogAssert.NoUnexpectedReceived();
            }
            finally
            {
                if (hero != null) hero.KnockedDown -= RecordFall;
            }
        }

        private IEnumerator BeginCraneHookKnockdown(CombatActor crane)
        {
            // The initial top stop can reach this real gallery without a long patrol setup.
            PlaceCraneHero(new Vector3(0f, WarehouseArenaPlan.TopHeight, -6.6f), Vector3.forward);
            for (int step = 0; step < 480 && root.Hero.ReceivedImpactCount == 0; step++)
            {
                TickCrane(crane, CombatTestRoot.SimulationStep);
                if (step % 120 == 119) yield return null;
            }
            Assert.That(root.Hero.ReceivedImpactCount, Is.EqualTo(1),
                "The genuine hook must hit before the physical fall; phase=" + crane.CranePhase +
                ", hook=" + crane.CraneHookPhase + ", actor=" + crane.transform.position + ".");
            CombatImpact impact = root.Hero.LastImpact;
            Assert.That(impact.Source, Is.SameAs(crane));
            Assert.That(impact.Target, Is.SameAs(root.Hero));
            Assert.That(impact.Kind, Is.EqualTo(CombatImpactKind.Unarmed));
            // A balance-preserving hit is followed through the same production
            // physical handoff as the porter regression; the callback is never invoked directly.
            if (!root.Hero.IsKnockedDown)
            {
                Assert.That(impact.AttackSequence, Is.EqualTo(crane.State.AttackSequence));
                Assert.That(root.Hero.TryBeginKnockdown(impact, impact.Direction * .8f, Vector3.zero), Is.True);
            }
            Assert.That(root.Hero.IsKnockedDown && root.Hero.IsRagdollActive, Is.True);
            Assert.That(crane.State.AttackSequence, Is.EqualTo(impact.AttackSequence + 1),
                "Entering frenzy invalidates the old strike before harmless hook return.");
        }

        private void CaptureCraneFrenzyView(CombatActor crane, string name)
        {
            Camera camera = root.CameraFollow.Camera;
            Vector3 position = camera.transform.position;
            Quaternion rotation = camera.transform.rotation;
            float fov = camera.fieldOfView;
            try
            {
                Vector3 focus = crane.transform.position + Vector3.up * 1.1f;
                camera.transform.SetPositionAndRotation(focus + new Vector3(3.2f, 1.1f, 3.5f),
                    Quaternion.LookRotation(-new Vector3(3.2f, 1.1f, 3.5f)));
                camera.fieldOfView = 55f;
                CaptureCraneView(crane, name, gameplayCamera: true);
            }
            finally
            {
                camera.transform.SetPositionAndRotation(position, rotation);
                camera.fieldOfView = fov;
            }
        }

        [UnityTest]
        [PrebuildSetup(typeof(CombatCraneAssetsSetup))]
        public IEnumerator Range_CraneSuspensionKeepsContactOcclusionPauseAndReset()
        {
            CombatTestRoot.UseLegacyArenaForTests = false;
            CombatTestRoot.WarehouseSeedForTests = 0;
            yield return SceneManager.LoadSceneAsync(SceneIds.MainMenu, LoadSceneMode.Single);
            Assert.That(CombatTestStartService.TryStart(new CombatTestSelection(
                CombatLoadoutSelection.Default, CombatTestMode.Crane)), Is.True);
            yield return WaitFor(() =>
            {
                root = Object.FindAnyObjectByType<CombatTestRoot>();
                return root != null && root.IsInitialized && !SceneTransitionService.IsTransitioning &&
                    GameInput.CanRead(GameInputContext.Gameplay);
            }, "The ceiling encounter did not release its entry transition.");
            root.AutomaticSimulation = false;
            CombatActor crane = root.Opponent;
            Assert.That(root.Mode, Is.EqualTo(CombatTestMode.Crane));
            Assert.That(root.WarehousePlan, Is.Not.Null);
            Assert.That(root.CameraFollow.MinimumOrbitPitch, Is.EqualTo(-70f),
                "The ordinary mouse orbit must allow the hero to look up at this encounter.");
            Assert.That(root.Opponents.Count, Is.EqualTo(1), "The prototype prepares one suspended actor.");
            Assert.That(crane.IsCrane, Is.True);
            Assert.That(crane.Weapon, Is.Null);
            Assert.That(crane.GetComponent<CombatZombieLoad>(), Is.Null,
                "The suspended worker must not retain a hidden porter crate.");
            Assert.That(crane.CranePhase, Is.EqualTo(CombatCranePhase.Waiting));
            Assert.That(crane.CraneAttached, Is.True);
            Assert.That(crane.CraneCableCount, Is.EqualTo(6));
            AssertCraneAudioBank(crane);
            preparedCraneCables = new Transform[crane.CraneCableCount, CombatActor.CraneCableSegments];
            for (int i = 0; i < crane.CraneCableCount; i++)
                for (int segment = 0; segment < CombatActor.CraneCableSegments; segment++)
                    preparedCraneCables[i, segment] = crane.GetCraneCableTransform(i, segment);
            preparedCraneHook = crane.GetCraneHookTransform();
            preparedCraneHookCable = crane.GetCraneHookCableTransform();
            observedCraneThrows = crane.CraneHookThrowCount;
            maximumCraneSwing = maximumCraneLimbEnergy = 0f;
            craneMovieFrames = 0;
            recordCraneMotion = Array.IndexOf(Environment.GetCommandLineArgs(), "-craneMotionCapture") >= 0;
            int animationGraphs = crane.NpcAnimationGraphBuilds;
            craneOriginalAnchor = crane.CraneAnchorPosition;
            craneOriginalYaw = crane.transform.eulerAngles.y;
            craneMinimumCommand = craneMaximumCommand = crane.CraneCarriagePosition;
            var cableStarts = new Vector3[crane.CraneCableCount];
            for (int i = 0; i < cableStarts.Length; i++) cableStarts[i] = crane.GetCraneCableStart(i);
            AssertCraneCablesFollowRig(crane, cableStarts);
            AssertCraneHookGeometry(crane);
            AssertCraneAppearance(crane, intact: true);
            Transform leftElbow = FindAnatomicalBone(crane, "forearm.L"), rightElbow = FindAnatomicalBone(crane, "forearm.R");
            Assert.That(Vector3.Distance(leftElbow.position, rightElbow.position), Is.GreaterThan(1.6f),
                "The folded insect silhouette must be visibly broader than the ordinary worker.");
            Assert.That(Vector3.Distance(leftElbow.position, FindAnatomicalBone(crane, "hand.L").position), Is.GreaterThan(.9f),
                "The forearm must be deliberately elongated, beyond uniform worker enlargement.");
            Assert.That(Vector3.Distance(FindAnatomicalBone(crane, "shin.L").position,
                FindAnatomicalBone(crane, "foot.L").position), Is.GreaterThan(.8f),
                "The reverse-folded shin retains its stretched insect proportion.");
            CraneAnatomicalFrame headFrame = CraneAnatomicalFrame.Create(crane, "GEO_Head", "head");
            CraneAnatomicalFrame torsoFrame = CraneAnatomicalFrame.Create(crane, "GEO_Torso", "chest");
            float previousHeadYaw = CraneAnatomicalHeadYaw(headFrame, torsoFrame), headYaw = 0f;
            float minimumHeadYaw = 0f, maximumHeadYaw = 0f, fastestHeadTurn = 0f;
            for (int frame = 0; frame < 105; frame++)
            {
                TickCrane(crane, 1f / 60f);
                float yaw = CraneAnatomicalHeadYaw(headFrame, torsoFrame), turn = Mathf.DeltaAngle(previousHeadYaw, yaw);
                headYaw += turn;
                minimumHeadYaw = Mathf.Min(minimumHeadYaw, headYaw);
                maximumHeadYaw = Mathf.Max(maximumHeadYaw, headYaw);
                fastestHeadTurn = Mathf.Max(fastestHeadTurn, Mathf.Abs(turn));
                previousHeadYaw = yaw;
            }
            CaptureCraneView(crane, "crane-01-suspended");
            Assert.That(maximumHeadYaw - minimumHeadYaw, Is.GreaterThan(150f),
                "The face must look left and right around the anatomical torso-up axis; long-axis bone roll cannot substitute for yaw.");
            Assert.That(fastestHeadTurn, Is.GreaterThan(12f), "Mantis face yaw needs short snaps between held stares.");
            Assert.That(crane.CraneFloorIndex, Is.EqualTo(2));
            PlaceCraneHero(crane.CraneGroundPosition + Vector3.forward * 1.2f, Vector3.back);
            root.CameraFollow.RotatePitch(-60f - root.CameraFollow.TargetOrbitPitch);
            root.CameraFollow.Snap();
            yield return null;
            CaptureCraneView(crane, "crane-04-ceiling-shoulder", gameplayCamera: true);
            root.CameraFollow.RotatePitch(-root.CameraFollow.TargetOrbitPitch);

            // A distant hero cannot choose or hold the worker's independent route.
            PlaceCraneHero(new Vector3(11f, 0f, 0f), Vector3.left);
            yield return VerifyCraneBlockedGoal(crane, cableStarts);
            if (recordCraneMotion)
            {
                // Stand inside the void so gallery slabs cannot hide the lift.
                // This point stays beyond six metres from the first middle stop.
                PlaceCraneHero(new Vector3(0f, 0f, -5f), Vector3.forward);
                yield return CaptureCraneMotion(crane, cableStarts, 192, transport: true);
            }
            yield return AdvanceCraneFloor(crane, 1, cableStarts);
            CaptureCraneView(crane, "crane-patrol-middle");
            Assert.That(root.Hero.ReceivedImpactCount, Is.Zero);
            Assert.That(maximumCraneSwing, Is.GreaterThan(.02f), "The transported body must lag and swing below the carriage.");
            Assert.That(maximumCraneLimbEnergy, Is.GreaterThan(.01f), "The four suspended limbs need their own inertial response.");
            Vector3 rootPose = crane.transform.position;
            var limbPose = new Vector3[crane.CraneCableCount];
            for (int i = 0; i < limbPose.Length; i++) limbPose[i] = crane.GetCraneCableEnd(i);
            for (int i = 0; i < 3; i++) crane.Present();
            Assert.That(Vector3.Distance(rootPose, crane.transform.position), Is.LessThan(.001f));
            for (int i = 0; i < limbPose.Length; i++)
                Assert.That(Vector3.Distance(limbPose[i], crane.GetCraneCableEnd(i)), Is.LessThan(.001f),
                    "Repeated presentation cannot integrate suspended dynamics again.");

            // A real downward shot crosses the open well, not a solid floor.
            Vector3 downwardHero = new Vector3(4.5f, 0f, 3f);
            yield return VerifyCraneHookHit(crane, downwardHero, cableStarts, "crane-hook-downward", captureMotion: true);
            Assert.That(observedCraneAim.y, Is.LessThan(-.2f));
            PlaceCraneHero(new Vector3(11f, 0f, 0f), Vector3.left);
            yield return AdvanceCraneFloor(crane, 0, cableStarts);
            CaptureCraneView(crane, "crane-patrol-ground");

            Vector3 ringCenter = new Vector3(crane.CraneCarriagePosition.x, 0f, crane.CraneCarriagePosition.z);
            // Leave real cable payout between the enlarged folded arm and the
            // target so pause/inserted-wall checks precede an actual body hit.
            yield return VerifyCraneHookOcclusionAndPause(crane, ringCenter + Vector3.right * 4f, cableStarts);
            PlaceCraneHero(ringCenter + Vector3.right * 2.2f, Vector3.left);
            yield return AdvanceCraneTo(crane, CombatCranePhase.Striking, cableStarts);
            int lockedThrow = crane.CraneHookThrowCount;
            Vector3 lockedOrigin = crane.CraneHookFlightOrigin;
            Vector3 lockedHorizontal = Vector3.ProjectOnPlane(crane.CraneHookAimDirection, Vector3.up).normalized;
            PlaceCraneHero(ringCenter + Vector3.forward * 2.2f, Vector3.back);
            for (int step = 0; crane.CraneHookPhase == CombatCraneHookPhase.Outbound && step < 240; step++)
            {
                TickCrane(crane, CombatTestRoot.SimulationStep);
                Vector3 horizontalFlight = Vector3.ProjectOnPlane(crane.CraneHookEyePosition - lockedOrigin, Vector3.up);
                Assert.That(Vector3.Cross(horizontalFlight, lockedHorizontal).magnitude, Is.LessThan(.08f),
                    "Moving the target after release cannot steer the physical hook toward its new position.");
            }
            yield return AdvanceCraneTo(crane, CombatCranePhase.Waiting, cableStarts);
            Assert.That(root.Hero.ReceivedImpactCount, Is.Zero, "A hero who leaves the locked flight path must evade the hook.");
            Assert.That(crane.CraneHookThrowCount, Is.EqualTo(lockedThrow));
            PlaceCraneHero(ringCenter + Vector3.forward * 6.5f, Vector3.back);
            TickCrane(crane, .8f);
            Assert.That(crane.CranePhase, Is.EqualTo(CombatCranePhase.Waiting));
            Assert.That(crane.CraneHookThrowCount, Is.EqualTo(lockedThrow), "A target beyond six metres cannot start a new throw.");
            for (int angle = 0; angle < 8; angle++)
            {
                float radians = angle * Mathf.PI * .25f;
                Vector3 heroPosition = ringCenter + new Vector3(Mathf.Sin(radians), 0f, Mathf.Cos(radians)) * 2.2f;
                yield return VerifyCraneHookHit(crane, heroPosition, cableStarts,
                    angle == 0 ? "crane-hook-ground" : null);
                Assert.That(crane.CraneFloorIndex, Is.Zero, "The same achieved stop can throw through the full horizontal circle.");
            }

            PlaceCraneHero(new Vector3(11f, 0f, 0f), Vector3.left);
            yield return AdvanceCraneFloor(crane, 1, cableStarts);
            WarehouseArenaStair upperStair = default;
            foreach (WarehouseArenaStair stair in root.WarehousePlan.Stairs)
                if (stair.Foot.x < 0f && stair.Foot.y > 0f) upperStair = stair;
            Assert.That(upperStair.Foot.y, Is.GreaterThan(0f));
            // Use a nearer real tread: the longer authored wrist offset and
            // inset patrol must still keep this upward shot within six metres.
            yield return VerifyCraneHookHit(crane, new Vector3(-6.95f, upperStair.HeightAt(-.6f), -.6f),
                cableStarts, "crane-hook-upward");
            Assert.That(observedCraneAim.y, Is.GreaterThan(.05f), "The actual throw must aim upward at the supported stair tread.");
            PlaceCraneHero(new Vector3(11f, 0f, 0f), Vector3.left);
            yield return AdvanceCraneFloor(crane, 2, cableStarts);
            yield return VerifyCraneHookHit(crane, new Vector3(4.5f, WarehouseArenaPlan.LevelRise, -6.45f),
                cableStarts, "crane-hook-top-to-middle");
            Assert.That(observedCraneAim.y, Is.LessThan(-.2f));
            Assert.That(root.Opponent, Is.SameAs(crane), "All storeys use the same living actor without resetting its round.");
            Assert.That(crane.NpcAnimationGraphBuilds, Is.EqualTo(animationGraphs));

            PlaceCraneHero(new Vector3(11f, 0f, 0f), Vector3.left);
            yield return AdvanceCraneFloor(crane, 0, cableStarts);
            Vector3 coverage = craneMaximumCommand - craneMinimumCommand;
            Assert.That(coverage.x, Is.GreaterThan(7f));
            Assert.That(coverage.z, Is.GreaterThan(7f));
            Assert.That(coverage.y, Is.GreaterThan(6f));
            CaptureCraneView(crane, "crane-return-ground");
            if (Array.IndexOf(Environment.GetCommandLineArgs(), "-craneAppearanceCapture") >= 0)
                yield return CaptureCraneAppearance(crane);
            crane.CaptureContactPose();
            Assert.That(crane.Hurtboxes.GetRegionFrame(MeleeBodyRegion.Head, out Vector3 head, out _, out _), Is.True);
            var hit = new CombatHurtboxes.Hit(head, Vector3.up, Vector3.down, 0f,
                new MeleeHitLocation(MeleeBodyRegion.Head, MeleeHitSide.Front),
                Player3DAnatomicalPart.Head, Vector3.zero);
            crane.ReceiveProjectile(root.Hero, 1, hit, Vector3.down * CombatProjectilePool.MuzzleSpeed);
            Assert.That(crane.State.Health, Is.EqualTo(crane.State.Settings.MaxHealth * .5f));
            Assert.That(crane.State.IsDefeated, Is.False);
            Assert.That(crane.CraneAttached, Is.True, "The first head wound retains the continuous suspended rig.");
            AssertCraneAppearance(crane, intact: false);
            crane.ReceiveProjectile(root.Hero, 2, hit, Vector3.down * CombatProjectilePool.MuzzleSpeed);
            Assert.That(crane.State.IsDefeated, Is.True, "The suspended worker keeps common anatomical damage.");
            root.Tick(CombatTestRoot.SimulationStep);
            Assert.That(crane.IsRagdollActive, Is.True);
            Assert.That(crane.CraneAttached, Is.False);
            Assert.That(crane.CranePhase, Is.EqualTo(CombatCranePhase.Released));
            Assert.That(crane.CraneHookPhase, Is.EqualTo(CombatCraneHookPhase.Released));
            foreach (Transform cable in preparedCraneCables) Assert.That(cable.gameObject.activeSelf, Is.False);
            Assert.That(preparedCraneHookCable.gameObject.activeSelf, Is.False);
            yield return null;
            CaptureCraneView(crane, "crane-08-released");
            root.ResetRound();
            observedCraneThrows = crane.CraneHookThrowCount;
            Assert.That(root.Opponent, Is.SameAs(crane));
            Assert.That(crane.State.IsDefeated || crane.IsRagdollActive, Is.False);
            Assert.That(crane.CranePhase, Is.EqualTo(CombatCranePhase.Waiting));
            Assert.That(crane.CraneFloorIndex, Is.EqualTo(2));
            Assert.That(crane.CraneHookPhase, Is.EqualTo(CombatCraneHookPhase.Stowed));
            AssertCraneCablesFollowRig(crane, cableStarts);
            AssertCraneHookGeometry(crane);
            AssertCraneAppearance(crane, intact: true);
            Assert.That(crane.NpcAnimationGraphBuilds, Is.EqualTo(animationGraphs));
            CaptureCraneView(crane, "crane-09-reset");
            if (recordCraneMotion)
            {
                string manifest = Path.Combine(Directory.GetCurrentDirectory(), "Captures", SceneIds.CombatTest, "crane-motion", "capture.json");
                File.WriteAllText(manifest, "{\"fps\":24,\"frames\":" + craneMovieFrames + "}");
                TestContext.Out.WriteLine("Crane gameplay capture: " + craneMovieFrames + " frames at 24 fps.");
            }

            Assert.That(root.ReturnToMenu(), Is.True);
            yield return WaitFor(() => SceneManager.GetActiveScene().name == SceneIds.MainMenu &&
                !SceneTransitionService.IsTransitioning, "The suspension scene did not unload.");
            Assert.That(crane == null, Is.True, "The suspended actor is owned by its unloaded scene.");
            LogAssert.NoUnexpectedReceived();
        }

        private IEnumerator VerifyCraneHookHit(CombatActor crane, Vector3 heroPosition, Vector3[] starts,
            string captureName, bool captureMotion = false)
        {
            PlaceCraneHero(heroPosition, Vector3.ProjectOnPlane(crane.transform.position - heroPosition, Vector3.up).normalized);
            int throws = crane.CraneHookThrowCount;
            Assert.That(Vector3.Distance(crane.CraneHookCableExit, root.Hero.transform.position + Vector3.up * 1.2f),
                Is.LessThan(CombatActor.CraneHookMaximumLength), "The supported test target must be in actual three-dimensional cable reach.");
            yield return AdvanceCraneTo(crane, CombatCranePhase.Striking, starts);
            Assert.That(crane.CraneHookThrowCount, Is.EqualTo(throws + 1));
            Assert.That(crane.CraneHookPhase, Is.EqualTo(CombatCraneHookPhase.Outbound).Or.EqualTo(CombatCraneHookPhase.Holding));
            if (crane.CraneHookPhase == CombatCraneHookPhase.Holding)
                Assert.That(root.Hero.ReceivedImpactCount, Is.EqualTo(1),
                    "A close target may stop the released hook in its first contact substep, through one real hit. " +
                    $"Target={heroPosition}, blocked anatomy={crane.CraneBlockedAnatomy}, cuff={crane.CraneHookCableExit}.");
            if (captureMotion && recordCraneMotion)
                yield return CaptureCraneMotion(crane, starts, 96, transport: false);
            else
            {
                int steps = 0;
                while (crane.CraneHookPhase == CombatCraneHookPhase.Outbound && crane.CraneHookCableLength < 1f && steps++ < 120)
                    TickCrane(crane, CombatTestRoot.SimulationStep);
                if (captureName != null)
                {
                    AimCraneGameplayCamera(crane);
                    yield return null;
                    CaptureCraneView(crane, captureName + "-shoulder", gameplayCamera: true);
                    CaptureCraneView(crane, captureName);
                }
            }
            yield return AdvanceCraneTo(crane, CombatCranePhase.Waiting, starts);
            Assert.That(root.Hero.ReceivedImpactCount, Is.EqualTo(1),
                "The thrown hook must produce one real contact and its return must remain harmless: " + (captureName ?? "horizontal circle") + ".");
            Assert.That(root.Hero.State.Health, Is.LessThan(root.Hero.State.Settings.MaxHealth));
            Assert.That(float.IsFinite(root.Hero.LastImpact.Point.sqrMagnitude), Is.True);
            Assert.That(crane.CraneHookThrowCount, Is.EqualTo(throws + 1));
            Assert.That(crane.CraneHookPhase, Is.EqualTo(CombatCraneHookPhase.Stowed));
            AssertCraneHookGeometry(crane);
        }

        private IEnumerator VerifyCraneHookOcclusionAndPause(CombatActor crane, Vector3 heroPosition, Vector3[] starts)
        {
            PlaceCraneHero(heroPosition, Vector3.ProjectOnPlane(crane.transform.position - heroPosition, Vector3.up).normalized);
            var wall = new GameObject("Test suspended worker partition");
            wall.transform.SetParent(root.transform, false);
            BoxCollider barrier = wall.AddComponent<BoxCollider>();
            barrier.size = new Vector3(4f, 3f, .16f);
            Vector3 target = root.Hero.transform.position + Vector3.up * 1.2f;
            Vector3 source = crane.CraneHookCableExit;
            wall.transform.SetPositionAndRotation((source + target) * .5f, Quaternion.LookRotation(target - source));
            Physics.SyncTransforms();
            int throws = crane.CraneHookThrowCount;
            try
            {
                for (int step = 0; step < 96; step++)
                {
                    source = crane.CraneHookCableExit;
                    target = root.Hero.transform.position + Vector3.up * 1.2f;
                    Vector3 delta = target - source;
                    Assert.That(barrier.Raycast(new Ray(source, delta.normalized), out _, delta.magnitude), Is.True,
                        "The fixed partition must cover the current cuff-to-target ray before testing acquisition: " +
                        $"step={step}, from={source}, target={target}, phase={crane.CranePhase}, " +
                        $"localFrom={wall.transform.InverseTransformPoint(source)}, localTarget={wall.transform.InverseTransformPoint(target)}.");
                    TickCrane(crane, CombatTestRoot.SimulationStep);
                    Assert.That(crane.CranePhase, Is.EqualTo(CombatCranePhase.Waiting),
                        "A partition covering the acquisition ray must keep the crane waiting: " +
                        $"step={step}, from={source}, target={target}, currentFrom={crane.CraneHookCableExit}, phase={crane.CranePhase}.");
                }
                Assert.That(crane.CraneHookThrowCount, Is.EqualTo(throws), "A solid wall must prevent initial hook acquisition.");
                barrier.enabled = false;
                Physics.SyncTransforms();
                yield return AdvanceCraneTo(crane, CombatCranePhase.Signal, starts);
                CaptureCraneView(crane, "crane-02-signal");
                yield return AdvanceCraneTo(crane, CombatCranePhase.Striking, starts);
                int steps = 0;
                while (crane.CraneHookPhase == CombatCraneHookPhase.Outbound && crane.CraneHookCableLength < .6f && steps++ < 120)
                    TickCrane(crane, CombatTestRoot.SimulationStep);
                Assert.That(crane.CraneHookPhase, Is.EqualTo(CombatCraneHookPhase.Outbound));
                Assert.That(crane.CraneHookCableLength, Is.GreaterThan(.5f), "Pause must freeze an actually extending hook.");
                Vector3 pausedBody = crane.transform.position, pausedCommand = crane.CraneCarriagePosition;
                Vector3 pausedAnchor = crane.CraneAnchorPosition, pausedHook = crane.CraneHookPosition;
                Vector3 pausedEye = crane.CraneHookEyePosition, pausedExit = crane.CraneHookCableExit;
                float pausedElapsed = crane.CranePhaseElapsed, pausedLength = crane.CraneHookCableLength;
                var pausedEnds = new Vector3[crane.CraneCableCount];
                for (int i = 0; i < pausedEnds.Length; i++) pausedEnds[i] = crane.GetCraneCableEnd(i);
                Assert.That(root.PauseMenu.Open(), Is.True);
                TickCrane(crane, 1f);
                yield return null;
                Assert.That(AudioListener.pause, Is.True);
                int[] pausedSamples = Array.ConvertAll(craneVoices, voice => voice.timeSamples);
                yield return null;
                for (int i = 0; i < craneVoices.Length; i++) Assert.That(craneVoices[i].timeSamples, Is.EqualTo(pausedSamples[i]));
                Assert.That(crane.transform.position, Is.EqualTo(pausedBody));
                Assert.That(crane.CraneCarriagePosition, Is.EqualTo(pausedCommand));
                Assert.That(crane.CraneAnchorPosition, Is.EqualTo(pausedAnchor));
                Assert.That(crane.CraneHookPosition, Is.EqualTo(pausedHook));
                Assert.That(crane.CraneHookEyePosition, Is.EqualTo(pausedEye));
                Assert.That(crane.CraneHookCableExit, Is.EqualTo(pausedExit));
                Assert.That(crane.CraneHookCableLength, Is.EqualTo(pausedLength));
                Assert.That(crane.CranePhaseElapsed, Is.EqualTo(pausedElapsed));
                for (int i = 0; i < pausedEnds.Length; i++) Assert.That(crane.GetCraneCableEnd(i), Is.EqualTo(pausedEnds[i]));
                Assert.That(root.PauseMenu.Cancel(), Is.True);
                yield return WaitFor(() => GameInput.CanRead(GameInputContext.Gameplay), "Pause did not release the hook encounter.");

                // The hook has already left the cuff: block its remaining real
                // flight rather than relying only on acquisition visibility.
                target = root.Hero.transform.position + Vector3.up * 1.2f;
                source = crane.CraneHookPosition;
                wall.transform.SetPositionAndRotation(Vector3.Lerp(source, target, .55f), Quaternion.LookRotation(target - source));
                barrier.enabled = true;
                Physics.SyncTransforms();
                for (int part = -1; part < CombatCraneAssetProvider.HookContactPoints.Length; part++)
                {
                    Vector3 local = part < 0 ? Vector3.up * .1f : CombatCraneAssetProvider.HookContactPoints[part];
                    float radius = part < 0 ? .08f : CombatCraneAssetProvider.HookContactRadii[part];
                    Vector3 centre = crane.GetCraneHookTransform().TransformPoint(local);
                    Assert.That(Vector3.Distance(centre, barrier.ClosestPoint(centre)), Is.GreaterThan(radius + .01f),
                        "The inserted wall must start ahead of the whole released hook, without spawning inside it.");
                }
                yield return AdvanceCraneTo(crane, CombatCranePhase.Waiting, starts);
                Assert.That(root.Hero.ReceivedImpactCount, Is.Zero, "An obstacle inserted during flight must stop the actual hook and its damage.");
                Assert.That(root.Hero.State.Health, Is.EqualTo(root.Hero.State.Settings.MaxHealth));
                Assert.That(crane.CraneHookThrowCount, Is.EqualTo(throws + 1));
                Assert.That(crane.CraneHookPhase, Is.EqualTo(CombatCraneHookPhase.Stowed));
            }
            finally
            {
                barrier.enabled = false;
                Object.Destroy(wall);
                Physics.SyncTransforms();
            }
        }

        private IEnumerator CaptureCraneMotion(CombatActor crane, Vector3[] starts, int maximumFrames, bool transport)
        {
            const string folder = "CombatTest/crane-motion";
            float captureDelta = Time.captureDeltaTime;
            try
            {
                Time.captureDeltaTime = 1f / 24f;
                for (int frame = 0; frame < maximumFrames; frame++)
                {
                    TickCrane(crane, 1f / 24f);
                    AimCraneGameplayCamera(crane);
                    yield return null;
                    string name = "frame" + (craneMovieFrames++).ToString("D4");
                    string path = Path.Combine(Directory.GetCurrentDirectory(), "Captures", folder, name + ".png");
                    LogAssert.Expect(LogType.Log, "Area capture wrote " + path);
                    AreaCaptureFixture.CaptureCurrentCamera(root.CameraFollow.Camera, folder, name);
                    if (frame % 12 == 0) { AssertCraneCablesFollowRig(crane, starts); AssertCraneHookGeometry(crane); }
                    if (transport ? crane.CraneFloorIndex == 1 && !crane.CranePatrolTravelling :
                        crane.CranePhase == CombatCranePhase.Waiting && crane.CraneHookPhase == CombatCraneHookPhase.Stowed) break;
                }
            }
            finally { Time.captureDeltaTime = captureDelta; }
        }

        private void AimCraneGameplayCamera(CombatActor crane)
        {
            Vector3 focus = crane.transform.position + Vector3.up * 1.1f;
            if (crane.CraneHookPhase is CombatCraneHookPhase.Outbound or CombatCraneHookPhase.Holding or CombatCraneHookPhase.Returning)
                focus = (focus + crane.CraneHookPosition) * .5f;
            Vector3 delta = focus - (root.Hero.transform.position + Vector3.up * 1.5f);
            float yaw = Mathf.Atan2(delta.x, delta.z) * Mathf.Rad2Deg;
            float pitch = -Mathf.Atan2(delta.y, new Vector2(delta.x, delta.z).magnitude) * Mathf.Rad2Deg;
            root.CameraFollow.RotateYaw(Mathf.DeltaAngle(root.CameraFollow.Camera.transform.eulerAngles.y, yaw));
            root.CameraFollow.RotatePitch(Mathf.Clamp(pitch, root.CameraFollow.MinimumOrbitPitch, root.CameraFollow.MaximumOrbitPitch) - root.CameraFollow.TargetOrbitPitch);
            root.CameraFollow.Snap();
        }

        private IEnumerator AdvanceCraneTo(CombatActor crane, CombatCranePhase phase, Vector3[] cableStarts)
        {
            const int maximumSteps = 4800;
            int step = 0;
            while ((crane.CranePhase != phase || crane.CranePatrolTravelling) && step < maximumSteps)
            {
                TickCrane(crane, CombatTestRoot.SimulationStep);
                if ((step++ % 30) == 0)
                {
                    AssertCraneCablesFollowRig(crane, cableStarts);
                    if (step % 120 == 1) yield return null;
                }
            }
            Assert.That(crane.CranePhase, Is.EqualTo(phase),
                $"The crane did not reach {phase}; current phase={crane.CranePhase}, elapsed={crane.CranePhaseElapsed:F3}, " +
                $"hook={crane.CraneHookPhase}, eye={crane.CraneHookEyePosition}, cuff={crane.CraneHookCableExit}.");
            Assert.That(crane.CranePatrolTravelling, Is.False);
            AssertCraneCablesFollowRig(crane, cableStarts);
        }

        private void TickCrane(CombatActor crane, float seconds)
        {
            double remaining = seconds;
            while (remaining > .0000001d)
            {
                float step = (float)Math.Min(CombatTestRoot.SimulationStep, remaining);
                Vector3 command = crane.CraneCarriagePosition, head = crane.CraneAnchorPosition;
                int beforeAxis = crane.CraneMotionAxis;
                int beforeImpacts = root.Hero.ReceivedImpactCount;
                Vector3 hookBefore = crane.CraneHookPosition;
                root.Tick(step);
                if (root.Hero.ReceivedImpactCount > beforeImpacts)
                {
                    Assert.That(root.Hero.ReceivedImpactCount, Is.EqualTo(beforeImpacts + 1));
                    Assert.That(crane.CraneHookPhase == CombatCraneHookPhase.Outbound ||
                        crane.CraneHookPhase == CombatCraneHookPhase.Holding ||
                        crane.IsCraneFrenzied && crane.CraneHookPhase == CombatCraneHookPhase.Returning, Is.True,
                        "Only the outbound hook can transact damage, never the fist or returning cable.");
                    Vector3 sweep = crane.CraneHookPosition - hookBefore;
                    float t = sweep.sqrMagnitude > .000001f ? Mathf.Clamp01(Vector3.Dot(root.Hero.LastImpact.Point - hookBefore, sweep) / sweep.sqrMagnitude) : 0f;
                    Assert.That(Vector3.Distance(root.Hero.LastImpact.Point, hookBefore + sweep * t), Is.LessThan(.45f),
                        "The received impact must be on the visible hook's actual short swept path.");
                }
                remaining -= step;
                Vector3 delta = crane.CraneCarriagePosition - command;
                int moved = 0, axis = 0;
                if (Mathf.Abs(delta.x) > .00001f) { moved++; axis = 1; }
                if (Mathf.Abs(delta.y) > .00001f) { moved++; axis = 2; }
                if (Mathf.Abs(delta.z) > .00001f) { moved++; axis = 3; }
                Assert.That(moved, Is.LessThanOrEqualTo(1),
                    "The driven load command must move along one world axis per simulation step.");
                if (moved > 0)
                    Assert.That(axis, Is.EqualTo(beforeAxis).Or.EqualTo(crane.CraneMotionAxis),
                        "The reported crane axis must match its actual driven movement.");
                Vector3 headDelta = crane.CraneAnchorPosition - head;
                Assert.That(Mathf.Abs(headDelta.y), Is.LessThan(.00001f));
                Assert.That(Mathf.Abs(headDelta.x) <= .00001f || Mathf.Abs(headDelta.z) <= .00001f, Is.True,
                    "The physical overhead head must not move diagonally while its load is controlled by one axis.");
                Vector3 anchor = crane.CraneAnchorPosition, carriage = crane.CraneCarriagePosition;
                Assert.That(Vector2.Distance(new Vector2(anchor.x, anchor.z),
                    new Vector2(carriage.x, carriage.z)), Is.LessThan(.001f));
                craneMinimumCommand = Vector3.Min(craneMinimumCommand, carriage);
                craneMaximumCommand = Vector3.Max(craneMaximumCommand, carriage);
                Assert.That(float.IsFinite(crane.CraneSwingOffset.sqrMagnitude), Is.True);
                Assert.That(float.IsFinite(crane.CraneLimbMotionEnergy) && crane.CraneLimbMotionEnergy >= 0f, Is.True);
                maximumCraneSwing = Mathf.Max(maximumCraneSwing, crane.CraneSwingOffset.magnitude);
                maximumCraneLimbEnergy = Mathf.Max(maximumCraneLimbEnergy, crane.CraneLimbMotionEnergy);
                Assert.That(float.IsFinite(crane.CraneHookCableLength), Is.True);
                Assert.That(crane.CraneHookCableLength, Is.LessThanOrEqualTo(CombatActor.CraneHookMaximumLength + .02f));
                if (crane.CraneHookThrowCount != observedCraneThrows)
                {
                    Assert.That(crane.CraneHookThrowCount, Is.EqualTo(observedCraneThrows + 1));
                    observedCraneThrows = crane.CraneHookThrowCount;
                    observedCraneAim = crane.CraneHookAimDirection;
                    observedCraneOrigin = crane.CraneHookFlightOrigin;
                    Vector3 target = root.Hero.transform.position + Vector3.up * 1.2f;
                    Assert.That(Vector3.Dot(observedCraneAim, (target - observedCraneOrigin).normalized), Is.GreaterThan(.98f),
                        "Release must aim at the actual three-dimensional torso position.");
                }
                if (observedCraneThrows > 0)
                {
                    Assert.That(crane.CraneHookAimDirection, Is.EqualTo(observedCraneAim), "The released aim must remain locked while the cuff and body swing.");
                    Assert.That(crane.CraneHookFlightOrigin, Is.EqualTo(observedCraneOrigin));
                }
            }
        }

        private IEnumerator VerifyCraneBlockedGoal(CombatActor crane, Vector3[] cableStarts)
        {
            int steps = 0;
            while ((!crane.CranePatrolTravelling || crane.CraneMotionAxis != 1) && steps++ < 1200)
            {
                TickCrane(crane, CombatTestRoot.SimulationStep);
                if (steps % 120 == 0) yield return null;
            }
            Assert.That(crane.CraneMotionAxis, Is.EqualTo(1), "The real first patrol leg must start its X drive.");
            Assert.That(crane.CraneTravelBlocked, Is.False);
            Vector3 goal = crane.CraneTravelGoal;
            int arrived = crane.CraneArrivedStops, floor = crane.CraneFloorIndex;
            var obstacle = new GameObject("Тестовая преграда крана");
            obstacle.transform.SetParent(root.transform, false);
            obstacle.transform.position = crane.CraneCarriagePosition +
                new Vector3(Mathf.Sign(goal.x - crane.CraneCarriagePosition.x) * 1.4f, .35f, 0f);
            var barrierMesh = new Mesh
            {
                name = "Тестовая сетка преграды крана",
                vertices = new[] {
                    new Vector3(-.1f, -1.3f, -1.5f), new Vector3(.1f, -1.3f, -1.5f),
                    new Vector3(.1f, 1.3f, -1.5f), new Vector3(-.1f, 1.3f, -1.5f),
                    new Vector3(-.1f, -1.3f, 1.5f), new Vector3(.1f, -1.3f, 1.5f),
                    new Vector3(.1f, 1.3f, 1.5f), new Vector3(-.1f, 1.3f, 1.5f)
                },
                triangles = new[] { 0, 2, 1, 0, 3, 2, 4, 5, 6, 4, 6, 7,
                    0, 4, 7, 0, 7, 3, 1, 2, 6, 1, 6, 5, 0, 1, 5, 0, 5, 4, 3, 7, 6, 3, 6, 2 }
            };
            MeshCollider barrier = obstacle.AddComponent<MeshCollider>();
            barrier.sharedMesh = barrierMesh;
            barrier.convex = false;
            Physics.SyncTransforms();
            try
            {
                // Force the reported overlap path at a live posed hand before
                // testing ordinary travel into the same non-convex obstacle.
                Vector3 ahead = obstacle.transform.position;
                obstacle.transform.position = crane.GetCraneCableEnd(1);
                Physics.SyncTransforms();
                crane.Present();
                LogAssert.NoUnexpectedReceived();
                Assert.That(barrier.convex, Is.False, "The warehouse mesh must retain its authored collision shape.");
                obstacle.transform.position = ahead;
                Physics.SyncTransforms();
                steps = 0;
                while (!crane.CraneTravelBlocked && steps++ < 1200)
                {
                    TickCrane(crane, CombatTestRoot.SimulationStep);
                    if (steps % 120 == 0) yield return null;
                }
                Assert.That(crane.CraneTravelBlocked, Is.True, "A solid obstruction must stop the real suspension envelope.");
                TickCrane(crane, .6f);
                Assert.That(crane.CraneArrivedStops, Is.EqualTo(arrived),
                    "A blocked goal must not be counted as a completed patrol stop.");
                Assert.That(crane.CraneFloorIndex, Is.EqualTo(floor),
                    "A blocked route cannot move the authoritative stop to a floor it did not reach.");
                Assert.That(Vector3.Distance(crane.CraneCarriagePosition, goal), Is.GreaterThan(.2f));
                AssertCraneCablesFollowRig(crane, cableStarts);
                CaptureCraneView(crane, "crane-patrol-blocked");
            }
            finally
            {
                barrier.enabled = false;
                barrier.sharedMesh = null;
                Object.Destroy(obstacle);
                Object.Destroy(barrierMesh);
                Physics.SyncTransforms();
            }
        }

        private IEnumerator AdvanceCraneFloor(CombatActor crane, int floor, Vector3[] cableStarts)
        {
            const int maximumSteps = 4800;
            int step = 0;
            while ((crane.CraneFloorIndex != floor || crane.CranePatrolTravelling ||
                    crane.CranePhase != CombatCranePhase.Waiting) && step < maximumSteps)
            {
                TickCrane(crane, CombatTestRoot.SimulationStep);
                if ((step++ % 30) == 0)
                {
                    AssertCraneCablesFollowRig(crane, cableStarts);
                    if (step % 120 == 1) yield return null;
                }
            }
            Assert.That(crane.CraneFloorIndex, Is.EqualTo(floor),
                "The autonomous patrol must reach floor " + floor + " independently of the hero.");
            Assert.That(crane.CranePatrolTravelling, Is.False);
            Assert.That(crane.CranePhase, Is.EqualTo(CombatCranePhase.Waiting));
            Assert.That(crane.CraneCarriagePosition.y,
                Is.EqualTo(floor * WarehouseArenaPlan.LevelRise + .40f).Within(.02f));
            AssertCraneCablesFollowRig(crane, cableStarts);
        }

        private void PlaceCraneHero(Vector3 floorPosition, Vector3 facing)
        {
            Assert.That(root.WarehouseArea.TryGetHeight(floorPosition, out float height, root.Hero.Body.radius), Is.True,
                "The staged hero must stand on a real warehouse floor or stair tread.");
            Assert.That(height, Is.EqualTo(floorPosition.y).Within(.03f));
            root.Hero.ResetActor(floorPosition + Vector3.up * PlayerFactory.GroundedRootOffset, facing);
            Physics.SyncTransforms();
            float yaw = Mathf.Atan2(facing.x, facing.z) * Mathf.Rad2Deg;
            root.CameraFollow.RotateYaw(Mathf.DeltaAngle(root.CameraFollow.Camera.transform.eulerAngles.y, yaw));
            root.CameraFollow.Snap();
        }

        private void AssertCraneAudioBank(CombatActor crane)
        {
            Assert.That(CombatCraneAssetProvider.ClipNames, Has.Length.EqualTo(10));
            foreach (string name in CombatCraneAssetProvider.ClipNames)
                Assert.That(CombatCraneAssetProvider.LoadClip(name).events, Is.Empty);
            AudioClip[] cues = { CombatCraneAssetProvider.LoadWinchSound(), CombatCraneAssetProvider.LoadRailSound(),
                CombatCraneAssetProvider.LoadStartSound(), CombatCraneAssetProvider.LoadBrakeSound(),
                CombatCraneAssetProvider.LoadBodySound(), CombatCraneAssetProvider.LoadHurtSound(), CombatCraneAssetProvider.LoadReleaseSound() };
            for (int i = 0; i < cues.Length; i++)
            {
                for (int j = 0; j < i; j++) Assert.That(cues[i], Is.Not.SameAs(cues[j]));
                var samples = new float[cues[i].samples * cues[i].channels];
                Assert.That(cues[i].GetData(samples, 0), Is.True);
                double energy = 0d;
                bool finite = true;
                foreach (float sample in samples)
                {
                    finite &= float.IsFinite(sample);
                    energy += (double)sample * sample;
                }
                Assert.That(finite, Is.True);
                Assert.That(energy, Is.InRange(.001d, (double)samples.Length), "A retained crane cue needs finite audible energy.");
            }
            craneVoices = Array.FindAll(root.GetComponentsInChildren<AudioSource>(true), voice => Array.IndexOf(cues, voice.clip) >= 0);
            Assert.That(craneVoices, Has.Length.EqualTo(4));
            AudioSource body = null, motor = null;
            foreach (AudioSource voice in craneVoices)
            {
                Assert.That(voice.spatialBlend, Is.EqualTo(1f));
                Assert.That(voice.ignoreListenerPause || voice.playOnAwake, Is.False);
                Assert.That(voice.loop, Is.EqualTo(voice.clip == cues[0] || voice.clip == cues[1]));
                if (voice.clip == cues[4]) body = voice;
                if (voice.clip == cues[0]) motor = voice;
            }
            Assert.That(body, Is.Not.Null); Assert.That(motor, Is.Not.Null);
            Assert.That(Vector3.Distance(body.transform.position, motor.transform.position), Is.GreaterThan(.5f),
                "The hanging worker and overhead machinery have separate spatial sound origins.");
        }

        private void AssertCraneCablesFollowRig(CombatActor crane, Vector3[] starts)
        {
            string[] bones = CombatCraneAssetProvider.SuspensionBoneNames;
            Assert.That(crane.CraneAttached, Is.True);
            Assert.That(crane.CraneCableCount, Is.EqualTo(bones.Length));
            Vector3 position = crane.transform.position;
            Assert.That(Mathf.Abs(position.x), Is.LessThanOrEqualTo(5.75f));
            Assert.That(Mathf.Abs(position.z), Is.LessThanOrEqualTo(5.75f));
            foreach (WarehouseArenaSurface surface in root.WarehousePlan.Surfaces)
                if (surface.Height > 0f)
                    Assert.That(surface.Bounds.Contains(new Vector2(position.x, position.z)), Is.False,
                        "The suspended root travels through the open well rather than a gallery deck.");
            Assert.That(crane.CraneAnchorPosition.y, Is.EqualTo(craneOriginalAnchor.y).Within(.001f));
            for (int i = 0; i < bones.Length; i++)
            {
                Vector3 end = crane.GetCraneCableEnd(i);
                Assert.That(Mathf.Abs(end.x), Is.LessThan(6f));
                Assert.That(Mathf.Abs(end.z), Is.LessThan(6f));
                Assert.That(Vector3.Distance(end, (FindAnatomicalBone(crane, bones[i]).position + FindAnatomicalBone(crane, bones[i]).rotation * CombatCraneAssetProvider.SuspensionLocalOffsets[i])), Is.LessThan(.025f),
                    bones[i] + " must remain attached to the visibly sampled rig, including the throwing wrist.");
                Vector3 initialOffset = starts[i] - craneOriginalAnchor;
                Vector3 feed = crane.GetCraneCableStart(i), previousEnd = feed;
                Vector3 offset = feed - crane.CraneAnchorPosition;
                Vector3 rotatedOffset = Quaternion.AngleAxis(Mathf.DeltaAngle(craneOriginalYaw, crane.transform.eulerAngles.y), Vector3.up) * initialOffset;
                Assert.That(Vector3.Distance(offset, rotatedOffset), Is.LessThan(.002f),
                    "The cable swivel must follow the body's yaw below the fixed overhead case.");
                Assert.That(float.IsFinite(end.sqrMagnitude), Is.True);
                Assert.That(feed.y - end.y, Is.GreaterThan(.01f), bones[i] + " must hang below its cable feed.");
                float visibleLength = 0f;
                for (int segment = 0; segment < CombatActor.CraneCableSegments; segment++)
                {
                    Vector3 from = crane.GetCraneCableSegmentStart(i, segment), to = crane.GetCraneCableSegmentEnd(i, segment);
                    Assert.That(Vector3.Distance(from, previousEnd), Is.LessThan(.001f), "Every curved suspension rope must be continuous.");
                    Transform piece = preparedCraneCables[i, segment];
                    Assert.That(crane.GetCraneCableTransform(i, segment), Is.SameAs(piece));
                    Assert.That(piece.gameObject.activeSelf, Is.True);
                    AssertCraneCableMesh(piece, from, to);
                    visibleLength += Vector3.Distance(from, to);
                    previousEnd = to;
                }
                Assert.That(Vector3.Distance(previousEnd, end), Is.LessThan(.001f));
                Assert.That(visibleLength + .001f, Is.GreaterThanOrEqualTo(Vector3.Distance(feed, end)));
            }
            AssertCraneHookGeometry(crane);
        }

        private readonly struct CraneAnatomicalFrame
        {
            internal readonly Transform Bone;
            private readonly Vector3 localForward, localUp;
            internal Vector3 Forward => Bone.TransformVector(localForward).normalized;
            internal Vector3 Up => Vector3.ProjectOnPlane(Bone.TransformVector(localUp), Forward).normalized;

            private CraneAnatomicalFrame(Transform bone, Vector3 forward, Vector3 up)
            { Bone = bone; localForward = forward; localUp = up; }

            internal static CraneAnatomicalFrame Create(CombatActor actor, string rendererName, string boneName)
            {
                SkinnedMeshRenderer canonical = CombatCraneAssetProvider.LoadBodyRenderer(rendererName);
                int index = Array.FindIndex(canonical.bones, bone => bone != null && bone.name == boneName);
                Assert.That(index, Is.GreaterThanOrEqualTo(0));
                Matrix4x4 bind = canonical.transform.localToWorldMatrix * canonical.sharedMesh.bindposes[index].inverse;
                // The sculpt faces Blender -Y, which reaches Unity as -Z.
                // Calibrate against the imported asset's original bind frame,
                // never a collider or a frame captured from the starting pose.
                Vector3 forward = canonical.transform.root.TransformVector(Vector3.back);
                Vector3 up = canonical.transform.root.TransformVector(Vector3.up);
                return new CraneAnatomicalFrame(FindAnatomicalBone(actor, boneName),
                    bind.inverse.MultiplyVector(forward).normalized, bind.inverse.MultiplyVector(up).normalized);
            }
        }

        private static float CraneAnatomicalHeadYaw(CraneAnatomicalFrame head, CraneAnatomicalFrame torso)
        {
            Vector3 headForward = head.Forward, headUp = head.Up, torsoForward = torso.Forward, torsoUp = torso.Up;
            Vector3 heading = Vector3.ProjectOnPlane(headForward, torsoUp);
            Assert.That(heading.sqrMagnitude, Is.GreaterThan(.05f), "Head heading must remain readable through its held glances.");
            Assert.That(Vector3.Dot(headUp, torsoUp), Is.GreaterThan(.25f),
                "A sideways or inverted mask is a head roll, not an anatomical look-left/right.");
            return Vector3.SignedAngle(Vector3.ProjectOnPlane(torsoForward, torsoUp), heading, torsoUp);
        }

        private IEnumerator CaptureCraneAppearance(CombatActor crane)
        {
            Camera camera = root.CameraFollow.Camera;
            float fov = camera.fieldOfView;
            Vector3 cameraPosition = camera.transform.position;
            Quaternion cameraRotation = camera.transform.rotation;
            float orbitPitch = root.CameraFollow.TargetOrbitPitch, orbitYaw = camera.transform.eulerAngles.y;
            Vector3 heroPosition = root.Hero.transform.position, heroForward = root.Hero.transform.forward;
            CombatCranePhase phase = crane.CranePhase;
            float phaseTime = crane.CranePhaseElapsed;
            Vector3 carriage = crane.CraneCarriagePosition;
            int throws = crane.CraneHookThrowCount;
            var flashlight = root.Hero.GetComponentInChildren<CombatChestFlashlight>(true);
            Assert.That(flashlight, Is.Not.Null);
            Assert.That(flashlight.SpotLight.enabled, Is.True);
            CraneAnatomicalFrame torsoFrame = CraneAnatomicalFrame.Create(crane, "GEO_Torso", "chest");
            Vector3 forward = Vector3.ProjectOnPlane(torsoFrame.Forward, Vector3.up).normalized;
            CraneAnatomicalFrame headFrame = CraneAnatomicalFrame.Create(crane, "GEO_Head", "head");
            Vector3 floor = crane.CraneCarriagePosition; floor.y = crane.CraneGroundPosition.y;
            try
            {
                foreach (int view in new[] { 0, 1 })
                {
                    Vector3 direction = forward;
                    Vector3 standingCenter = floor;
                    if (view == 0)
                    {
                        crane.CaptureContactPose();
                        Assert.That(crane.Hurtboxes.GetRegionFrame(MeleeBodyRegion.Head, out Vector3 head, out _, out _), Is.True);
                        direction = Vector3.ProjectOnPlane(headFrame.Forward, Vector3.up);
                        Assert.That(direction.sqrMagnitude, Is.GreaterThan(.05f));
                        direction.Normalize();
                        standingCenter = new Vector3(head.x, floor.y, head.z);
                    }
                    PlaceCraneHero(standingCenter + direction * 3.2f, -direction);
                    Vector3 focus = Vector3.zero;
                    for (int frame = 0; frame < 4; frame++)
                    {
                        crane.CaptureContactPose();
                        Assert.That(crane.Hurtboxes.GetRegionFrame(MeleeBodyRegion.Head, out Vector3 head, out _, out _), Is.True);
                        focus = view == 0 ? head : crane.transform.position + Vector3.up * .95f;
                        Vector3 delta = focus - (root.Hero.transform.position + Vector3.up * 1.5f);
                        float yaw = Mathf.Atan2(delta.x, delta.z) * Mathf.Rad2Deg;
                        float pitch = -Mathf.Atan2(delta.y, new Vector2(delta.x, delta.z).magnitude) * Mathf.Rad2Deg;
                        root.CameraFollow.RotateYaw(Mathf.DeltaAngle(camera.transform.eulerAngles.y, yaw));
                        root.CameraFollow.RotatePitch(Mathf.Clamp(pitch, root.CameraFollow.MinimumOrbitPitch,
                            root.CameraFollow.MaximumOrbitPitch) - root.CameraFollow.TargetOrbitPitch);
                        root.CameraFollow.Snap();
                        root.Hero.AdvanceSimulation(.1f);
                        root.Hero.Present();
                        yield return null;
                    }
                    string name = view == 0 ? "Test crane - Close head" : "Test crane - Front body";
                    Vector3 eye = focus + direction * 2.8f + Vector3.up * .1f;
                    camera.transform.SetPositionAndRotation(eye, Quaternion.LookRotation(focus - eye));
                    camera.fieldOfView = view == 0 ? 22f : 56f;
                    CaptureCraneView(crane, name, gameplayCamera: true);
                }
            }
            finally
            {
                PlaceCraneHero(heroPosition - Vector3.up * PlayerFactory.GroundedRootOffset, heroForward);
                root.CameraFollow.RotateYaw(Mathf.DeltaAngle(camera.transform.eulerAngles.y, orbitYaw));
                root.CameraFollow.RotatePitch(orbitPitch - root.CameraFollow.TargetOrbitPitch);
                root.CameraFollow.Snap();
                camera.transform.SetPositionAndRotation(cameraPosition, cameraRotation);
                camera.fieldOfView = fov;
            }
            Assert.That(crane.CranePhase, Is.EqualTo(phase));
            Assert.That(crane.CranePhaseElapsed, Is.EqualTo(phaseTime));
            Assert.That(crane.CraneCarriagePosition, Is.EqualTo(carriage));
            Assert.That(crane.CraneHookThrowCount, Is.EqualTo(throws), "Appearance capture cannot advance the encounter.");
        }

        private static void AssertCraneAppearance(CombatActor crane, bool intact)
        {
            Assert.That(crane.GetComponentInChildren<CombatCraneAppearance>(true), Is.Not.Null);
            Assert.That(crane.GetComponentInChildren<CombatZombieAppearance>(true), Is.Null,
                "The crane owns its surface restoration independently of the docker's civilian outfit.");
            CombatCraneAssetProvider.BodyManifest manifest = CombatCraneAssetProvider.LoadBodyManifest();
            Assert.That(manifest.geometry_owner, Is.EqualTo("independent_sculpt"));
            Assert.That(manifest.bounds_space, Is.EqualTo("unity_metres"));
            Texture2D atlas = CombatCraneAssetProvider.LoadBodyAtlas();
            Material material = CombatCraneAssetProvider.LoadBodyMaterial();
            Shader shader = Resources.Load<Shader>("Shaders/WarehouseSurface");
            Texture2D response = Resources.Load<Texture2D>("Combat/CraneMaterialResponse");
            Assert.That(atlas.width, Is.EqualTo(2048));
            Assert.That(response, Is.Not.Null);
            Assert.That(response.width, Is.EqualTo(1024));
            Assert.That(material.shader, Is.SameAs(shader));
            Assert.That(material.GetTexture("_BaseMap"), Is.SameAs(atlas));
            Assert.That(material.GetTexture("_WarehouseMaterialResponse"), Is.SameAs(response));
            Assert.That(manifest.estimated_gpu_bytes, Is.LessThan(7L * 1024 * 1024));
            SkinnedMeshRenderer[] skins = crane.DamageRigRoot.GetComponentsInChildren<SkinnedMeshRenderer>(true);
            var properties = new MaterialPropertyBlock();
            bool fleshSeen = false, carapaceSeen = false;
            foreach (CombatCraneAssetProvider.BodyMesh part in manifest.meshes)
            {
                SkinnedMeshRenderer skin = Array.Find(skins, candidate => candidate.name == part.name);
                Assert.That(skin, Is.Not.Null, "The retained damage source must keep its authored name: " + part.name);
                Assert.That(skin.sharedMesh, Is.SameAs(CombatCraneAssetProvider.LoadBodyRenderer(part.name).sharedMesh),
                    "Wounds and reset must retain the authored crane skin: " + part.name);
                Material[] assigned = skin.sharedMaterials;
                Assert.That(assigned, Has.Length.EqualTo(part.surfaces.Length));
                for (int slot = 0; slot < assigned.Length; slot++)
                {
                    Assert.That(assigned[slot], Is.SameAs(CombatCraneAssetProvider.LoadBodyMaterial(part.surfaces[slot])),
                        "Each sculpt region retains its own shared material role: " + part.name);
                    if (part.visible)
                    {
                        fleshSeen |= part.surfaces[slot] == "flesh";
                        carapaceSeen |= part.surfaces[slot] == "carapace";
                    }
                }
                if (intact || !part.visible)
                    Assert.That(skin.enabled && skin.gameObject.activeInHierarchy, Is.EqualTo(part.visible),
                        "Only the authored anatomical surface set may return on reset: " + part.name);
            }
            Assert.That(fleshSeen && carapaceSeen, Is.True, "The industrial mutant needs separate flesh and embedded carapace surfaces.");
            int paintedSurfaces = 0;
            foreach (SkinnedMeshRenderer skin in skins)
            {
                bool visible = skin.enabled && skin.gameObject.activeInHierarchy;
                if (skin.name.StartsWith("CLO_", StringComparison.Ordinal) || skin.name.StartsWith("HAIR_", StringComparison.Ordinal))
                    Assert.That(visible, Is.False, "Civilian clothes and hair cannot reappear on the insect body.");
                if (!visible || skin.sharedMaterial == null || skin.sharedMaterial.shader != shader) continue;
                skin.GetPropertyBlock(properties);
                Assert.That(properties.GetTexture("_BaseMap") ?? skin.sharedMaterial.GetTexture("_BaseMap"), Is.SameAs(atlas),
                    "Live exterior wounds must keep the crane atlas instead of the original worker painting.");
                Assert.That(skin.sharedMaterial.GetTexture("_WarehouseMaterialResponse"), Is.SameAs(response));
                properties.Clear(); paintedSurfaces++;
            }
            Assert.That(paintedSurfaces, Is.GreaterThan(0));
            MeshFilter[] passive = crane.GetCraneHookTransform().parent.GetComponentsInChildren<MeshFilter>(true);
            foreach (CombatCraneAssetProvider.PassiveMesh part in manifest.passive_meshes)
            {
                int matches = 0;
                foreach (MeshFilter surface in passive)
                {
                    if (surface.name != part.name) continue;
                    matches++;
                    Assert.That(surface.sharedMesh, Is.SameAs(CombatCraneAssetProvider.LoadPassiveSurface(part.name).sharedMesh));
                    Material metal = surface.GetComponent<Renderer>().sharedMaterial;
                    Assert.That(metal.shader, Is.SameAs(shader));
                    Assert.That(metal.GetTexture("_BaseMap"), Is.SameAs(atlas));
                    Assert.That(metal.GetTexture("_WarehouseMaterialResponse"), Is.SameAs(response));
                }
                Assert.That(matches, Is.GreaterThan(0), "The mechanical kit must use the same authored painting: " + part.name);
            }
            if (manifest.hardware != null)
            {
                MeshFilter[] fittings = crane.DamageRigRoot.GetComponentsInChildren<MeshFilter>(true);
                foreach (CombatCraneAssetProvider.HardwareMesh part in manifest.hardware)
                {
                    MeshFilter fitting = Array.Find(fittings, candidate => candidate.name == part.name);
                    Assert.That(fitting, Is.Not.Null, "The sculpt must retain its embedded cable fitting: " + part.name);
                    Assert.That(fitting.transform.parent.name, Is.EqualTo(part.bone));
                    Assert.That(fitting.sharedMesh, Is.SameAs(CombatCraneAssetProvider.LoadPassiveSurface(part.name).sharedMesh));
                    Assert.That(fitting.GetComponent<Renderer>().sharedMaterial, Is.SameAs(CombatCraneAssetProvider.LoadBodyMaterial("steel")));
                    Assert.That(fitting.GetComponent<Renderer>().enabled, Is.True, "Surviving anatomy and reset retain their cable fittings.");
                }
            }
        }

        private void AssertCraneHookGeometry(CombatActor crane)
        {
            Assert.That(crane.CraneRightHandVisible, Is.False, "The cuff and hook replace the ordinary right fist in the live damage rig.");
            Assert.That(crane.GetCraneHookTransform(), Is.SameAs(preparedCraneHook));
            Assert.That(crane.GetCraneHookCableTransform(), Is.SameAs(preparedCraneHookCable));
            Assert.That(preparedCraneHook.gameObject.activeSelf, Is.True);
            Assert.That(preparedCraneHookCable.gameObject.activeSelf, Is.True);
            Assert.That(crane.CraneHookCableLength,
                Is.EqualTo(Vector3.Distance(crane.CraneHookCableExit, crane.CraneHookEyePosition)).Within(.001f));
            AssertCraneCableMesh(preparedCraneHookCable, crane.CraneHookCableExit, crane.CraneHookEyePosition);
            Renderer[] renderers = preparedCraneHook.GetComponentsInChildren<Renderer>(true);
            Assert.That(renderers, Is.Not.Empty);
            Bounds bounds = renderers[0].bounds;
            for (int r = 1; r < renderers.Length; r++) bounds.Encapsulate(renderers[r].bounds);
            Assert.That(bounds.size.magnitude, Is.InRange(.2f, 1.5f), "The real hook mesh retains metre scale while it flies.");
            bounds.Expand(.15f);
            Assert.That(bounds.Contains(crane.CraneHookEyePosition), Is.True);
            Assert.That(bounds.Contains(crane.CraneHookPosition), Is.True, "The damage anchor must travel with the actual visible hook.");
        }

        private static void AssertCraneCableMesh(Transform cable, Vector3 from, Vector3 to)
        {
            Renderer[] renderers = cable.GetComponentsInChildren<Renderer>(true);
            Assert.That(renderers, Is.Not.Empty);
            Bounds bounds = renderers[0].bounds;
            for (int r = 1; r < renderers.Length; r++) bounds.Encapsulate(renderers[r].bounds);
            Assert.That(Vector3.Distance(bounds.center, (from + to) * .5f), Is.LessThan(.08f),
                "The imported mesh must span its real world endpoints.");
            Assert.That(bounds.size.magnitude, Is.EqualTo(Vector3.Distance(from, to)).Within(.08f),
                "Imported scale must not multiply a visible cable's world length.");
        }

        private void CaptureCraneView(CombatActor actor, string name, bool gameplayCamera = false)
        {
            Camera camera = root.CameraFollow.Camera;
            Vector3 oldPosition = camera.transform.position;
            Quaternion oldRotation = camera.transform.rotation;
            float oldFov = camera.fieldOfView;
            try
            {
                if (!gameplayCamera)
                {
                    bool closeBody = actor.CranePhase is CombatCranePhase.Signal or CombatCranePhase.Striking or
                        CombatCranePhase.Recovering or CombatCranePhase.Released;
                    // The central hall is open only inside XZ +/-6m. A shot
                    // from the arrival gallery is hidden behind its own decks.
                    Vector3 focus = closeBody ? (actor.transform.position + Vector3.up * 1.1f + actor.CraneHookPosition) * .5f :
                        (actor.CraneAnchorPosition + actor.transform.position) * .5f;
                    Vector3 eye = closeBody ? focus + new Vector3(3.5f, 2f, 4f) :
                        new Vector3(actor.CraneCarriagePosition.x > 0f ? -4.8f : 4.8f, 3.7f,
                            actor.CraneCarriagePosition.z > 0f ? -4.8f : 4.8f);
                    camera.transform.SetPositionAndRotation(eye, Quaternion.LookRotation(focus - eye));
                    camera.fieldOfView = closeBody ? 60f : 74f;
                }
                string path = Path.Combine(Directory.GetCurrentDirectory(), "Captures", SceneIds.CombatTest, name + ".png");
                LogAssert.Expect(LogType.Log, "Area capture wrote " + path);
                AreaCaptureFixture.CaptureCurrentCamera(camera, SceneIds.CombatTest, name);
            }
            finally
            {
                camera.transform.SetPositionAndRotation(oldPosition, oldRotation);
                camera.fieldOfView = oldFov;
            }
        }
    }
}

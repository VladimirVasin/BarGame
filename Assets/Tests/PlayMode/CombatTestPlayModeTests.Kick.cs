using System.Collections;
using System.IO;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;
using static BarPromenade.Tests.PlayMode.CombatTuning;

namespace BarPromenade.Tests.PlayMode
{
    public sealed class CombatKickAssetsSetup : IPrebuildSetup
    {
        public void Setup()
        {
#if UNITY_EDITOR
            System.Type.GetType("BarPromenade.Editor.CityPedestrianAssetSetup, BarPromenade.Editor", true)
                .GetMethod("ValidateFaceAtlasesOrThrow", System.Type.EmptyTypes).Invoke(null, null);
            System.Type.GetType("BarPromenade.Editor.CombatTestAssetSetup, BarPromenade.Editor", true)
                .GetMethod("BuildOrThrow", System.Type.EmptyTypes).Invoke(null, null);
#endif
        }
    }

    [PrebuildSetup(typeof(CombatKickAssetsSetup))]
    public sealed class CombatKickPlayModeTests
    {
        private CombatTestRoot root;
        private float previousCaptureDelta;

        [UnitySetUp]
        public IEnumerator SetUp()
        {
            previousCaptureDelta = Time.captureDeltaTime;
            Time.captureDeltaTime = 1f / 60f;
            GameSessionState.BeginNewGame();
            yield return null;
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            if (root != null) root.AutomaticSimulation = false;
            RetroAudioService.Instance?.StopAll();
            yield return SceneManager.LoadSceneAsync(SceneIds.MainMenu);
            GameSessionState.BeginNewGame();
            Time.captureDeltaTime = previousCaptureDelta;
            root = null;
        }

        private void PlacePair(float distance)
        {
            root.SetSparring(false);
            Vector3 position = Vector3.up * PlayerFactory.GroundedRootOffset;
            root.Hero.ResetActor(position, Vector3.forward);
            root.Opponent.ResetActor(position + Vector3.forward * distance, Vector3.back);
            Physics.SyncTransforms();
        }

        [UnityTest]
        public IEnumerator Range_KickUsesSoleContactSupportInputAndOwnedRecovery()
        {
            var input = new InputTestFixture();
            GameObject wall = null;
            try
            {
                input.Setup();
                Keyboard keyboard = InputSystem.AddDevice<Keyboard>();
                Gamepad pad = InputSystem.AddDevice<Gamepad>();
                yield return SceneManager.LoadSceneAsync(SceneIds.CombatTest);
                root = Object.FindAnyObjectByType<CombatTestRoot>();
                root.AutomaticSimulation = false;
                PlacePair(.80f);
                root.Opponent.SetBlock(true);
                root.CameraFollow.Snap();
                yield return null;
                Vector3 left = FindKickBone("foot.L").position;
                root.AutomaticSimulation = true;
                input.Press(keyboard.qKey, queueEventOnly: true);
                yield return null;
                root.AutomaticSimulation = false;
                Assert.That(root.Hero.State.IsKicking, Is.True, "Q is consumed by the real gameplay Update.");
                Assert.That(root.Hero.State.Stamina, Is.EqualTo(S.MaxStamina - S.KickCost).Within(.001f));
                input.Release(keyboard.qKey, queueEventOnly: true);
                yield return null;
                Assert.That(root.Hero.TryKick(), Is.False, "The same kick cannot restart itself.");
                root.Tick(.25f);
                yield return null;
                Assert.That(Vector3.Distance(left, FindKickBone("foot.L").position), Is.LessThan(.025f),
                    "The loaded left foot stays in world contact while the right foot lifts.");
                CaptureKick("kick-prepare");
                float held = root.Hero.State.KickElapsed;
                using (GameTimeScaleRuntime.AcquirePause())
                {
                    root.Tick(.3f);
                    Assert.That(root.Hero.State.KickElapsed, Is.EqualTo(held));
                    Assert.That(root.Hero.TryKick(), Is.False);
                }
                for (int i = 0; i < 24 && root.Opponent.State.Health == S.MaxHealth; i++)
                { root.Tick(CombatTestRoot.SimulationStep); yield return null; }
                Assert.That(root.Opponent.State.Health, Is.EqualTo(S.MaxHealth - S.KickDamage),
                    "The actual boot crosses the posed anatomy beneath the raised face guard.");
                Assert.That(root.Opponent.LastImpact.Kind, Is.EqualTo(CombatImpactKind.Kick));
                Assert.That(root.BloodEffects.WoundCountFor(root.Opponent), Is.Zero);
                Assert.That(root.Opponent.LastImpact.Impulse.magnitude, Is.EqualTo(CombatActor.KickImpulse).Within(.001f));
                CaptureKick("kick-contact");
                for (int i = 0; i < 60 && (root.Hero.State.KickRecoveryRemaining > .15f ||
                    root.Hero.State.KickElapsed < root.Hero.State.KickActiveEnd); i++)
                    root.Tick(CombatTestRoot.SimulationStep);
                Assert.That(root.Opponent.ReceivedImpactCount, Is.EqualTo(1), "One foot sweep grants one impact.");
                int kickSequence = root.Hero.State.AttackSequence;
                Assert.That(root.Hero.RequestCharge(), Is.True);
                root.Hero.ReleaseCharge();
                Assert.That(root.Hero.State.IsKicking, Is.False, "The completed kick contact does not block a weapon tap through its whole return.");
                Assert.That(root.Hero.State.AttackSequence, Is.EqualTo(kickSequence + 1));
                Assert.That(root.Hero.State.CooldownRemaining(MeleeBufferedAction.Kick), Is.GreaterThan(0f));
                Assert.That(root.Hero.State.Phase, Is.EqualTo(MeleePhase.Windup));
                Assert.That(root.Hero.State.IsChained, Is.False);
                CaptureKick("kick-return-attack");

                PlacePair(2.4f);
                yield return null;
                Assert.That(root.Hero.TryKick(), Is.True);
                root.Tick(.76f);
                Assert.That(root.Hero.State.KickOutcome, Is.EqualTo(MeleeAttackOutcome.Miss));
                Assert.That(root.Hero.TryStep(Vector2.down), Is.True);
                Assert.That(root.Hero.State.IsKicking, Is.True);
                for (int i = 0; i < 40 && root.Hero.State.Phase != MeleePhase.Step; i++) root.Tick(CombatTestRoot.SimulationStep);
                Assert.That(root.Hero.State.Phase, Is.EqualTo(MeleePhase.Step), "The queued step starts after supported landing.");

                PlacePair(1f);
                wall = new GameObject("Kick test wall");
                wall.transform.position = new Vector3(0f, 1f, .5f);
                wall.AddComponent<BoxCollider>().size = new Vector3(3f, 2f, .05f);
                Physics.SyncTransforms();
                yield return null;
                Assert.That(root.Hero.TryKick(), Is.True);
                root.Tick(.45f);
                yield return null;
                Assert.That(root.Opponent.State.Health, Is.EqualTo(S.MaxHealth));
                Assert.That(root.Hero.State.KickOutcome, Is.EqualTo(MeleeAttackOutcome.Obstacle));
                Assert.That(root.Hero.KickBootPosition.z, Is.LessThan(.51f));
                CaptureKick("kick-wall-return");
                Object.Destroy(wall); wall = null;
                root.ResetRound();
                Assert.That(root.Hero.State.IsKicking, Is.False);
                Assert.That(root.Hero.State.BufferedAction, Is.EqualTo(MeleeBufferedAction.None));

                PlacePair(2.4f);
                root.AutomaticSimulation = true;
                input.Press(pad.buttonEast, queueEventOnly: true);
                yield return null;
                root.AutomaticSimulation = false;
                Assert.That(root.Hero.State.IsKicking, Is.True, "Gamepad east has the same scoped kick action.");
                root.Hero.State.ReceiveKick();
                root.Tick(.01f);
                Assert.That(root.Hero.State.IsKicking, Is.False);
                Assert.That(root.Hero.State.HasBufferedAttack, Is.False);
                root.Hero.enabled = false;
                Assert.That(root.Hero.State.IsKicking, Is.False);
                root.Hero.enabled = true;
                foreach (MeleeSwing side in new[] { MeleeSwing.Forehand, MeleeSwing.Backhand })
                {
                    PlacePair(3f);
                    root.Hero.State.ObserveLateralCue(side == MeleeSwing.Forehand ? -1 : 1);
                    Assert.That(root.Hero.TryAttack(), Is.True);
                    root.Tick(S.WindupSeconds + .01f);
                    Assert.That(root.Hero.State.RecordAttackOutcome(MeleeHitResult.Parried, root.Hero.State.AttackSequence), Is.True);
                    root.Hero.ShowParried(root.Hero.Weapon.transform.position, Vector3.back);
                    root.Hero.Present();
                    root.Tick(.5f);
                    Assert.That(root.Hero.State.Phase, Is.EqualTo(MeleePhase.Recovery));
                    var presentation = (Player3DCharacterPresentation)root.Player.Visual;
                    Assert.That(presentation.ActiveClipName, Is.EqualTo(CombatAssetProvider.SwingClips(side).Recoil),
                        "A finished recoil holds its Ready endpoint through the remaining forced recovery.");
                    CaptureKick("recoil-" + side.ToString().ToLowerInvariant());
                }
            }
            finally
            {
                if (wall != null) Object.Destroy(wall);
                if (root != null) root.AutomaticSimulation = false;
                input.TearDown();
            }
        }

        [UnityTest]
        public IEnumerator Range_KickSelectsWalkingSupportAndCancelsStaleRequests()
        {
            var input = new InputTestFixture();
            GameObject obstruction = null;
            try
            {
                input.Setup();
                yield return SceneManager.LoadSceneAsync(SceneIds.CombatTest);
                root = Object.FindAnyObjectByType<CombatTestRoot>();
                root.AutomaticSimulation = false;
                Mouse mouse = InputSystem.AddDevice<Mouse>();
                RetroUiCanvas canvas = RetroUiTheme.CalculateCanvas(Screen.width, Screen.height);
                Vector2 actionPointer = canvas.LogicalToScreen(new Vector2(320f, 180f));
                actionPointer.y = Screen.height - actionPointer.y;
                input.Set(mouse.position, actionPointer);
                var consume = typeof(CombatTestRoot).GetMethod("UpdateCombatInput",
                    System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
                Assert.That(consume, Is.Not.Null);
                for (int striking = 0; striking < 2; striking++)
                {
                    PlacePair(3f);
                    yield return null;
                    WalkToFootLift(striking);
                    Transform support = FindKickBone(striking == 0 ? "foot.R" : "foot.L");
                    Vector3 planted = support.position;
                    Transform weapon = root.Hero.Weapon.transform, grip = root.Hero.Weapon.transform.parent;
                    Vector3 mount = weapon.localPosition; Quaternion mountRotation = weapon.localRotation;
                    root.Opponent.ResetActor(root.Hero.transform.position + Vector3.forward * .80f, Vector3.back);
                    root.Opponent.SetBlock(true);
                    Physics.SyncTransforms();
                    Assert.That(root.Hero.TryKick(), Is.True, KickWaitDiagnostics());
                    Assert.That(root.Hero.HasPendingKick, Is.False,
                        "The other planted sole permits an immediate kick by the travelling leg.");
                    Assert.That(root.Hero.State.IsKicking, Is.True);
                    Assert.That(root.Hero.KickStrikingSide, Is.EqualTo(striking));
                    Assert.That(root.Hero.Footwork.SelectedSupportSide, Is.EqualTo(1 - striking));
                    Assert.That(root.Hero.State.Stamina, Is.EqualTo(S.MaxStamina - S.KickCost).Within(.001f));
                    for (int tick = 0; tick < 25; tick++)
                    {
                        root.Tick(CombatTestRoot.SimulationStep);
                        Assert.That(root.Hero.KickStrikingSide, Is.EqualTo(striking));
                        Assert.That(Vector3.Distance(planted, support.position), Is.LessThan(.025f));
                        Assert.That(weapon.parent, Is.SameAs(grip), "Both kicks retain the owning right hand.");
                        Assert.That(Vector3.Distance(mount, weapon.localPosition), Is.LessThan(.00001f));
                        Assert.That(Quaternion.Angle(mountRotation, weapon.localRotation), Is.LessThan(.001f));
                    }
                    yield return null;
                    root.Hero.Present(); root.Opponent.Present();
                    var presentation = (Player3DCharacterPresentation)root.Player.Visual;
                    Assert.That(presentation.ActiveClipName, Is.EqualTo(CombatAssetProvider.KickClipNames[striking]));
                    CaptureKick(striking == 0 ? "left-kick-supported" : "right-kick-supported");
                    for (int tick = 0; tick < 80 && root.Hero.State.IsKicking; tick++)
                        root.Tick(CombatTestRoot.SimulationStep);
                    Assert.That(root.Opponent.ReceivedImpactCount, Is.EqualTo(1),
                        "Each actual selected boot reaches the nearby torso once.");
                    Assert.That(root.Opponent.LastImpact.Kind, Is.EqualTo(CombatImpactKind.Kick));
                    Assert.That(root.Opponent.LastImpact.Impulse.magnitude, Is.EqualTo(CombatActor.KickImpulse).Within(.001f));
                }

                // Waiting is reserved for a genuine lack of either usable sole:
                // one is travelling and the other's physical landing is obstructed.
                PlacePair(3f);
                yield return null;
                WalkToFootLift(0);
                obstruction = BlockRightKickSupport();
                Assert.That(root.Hero.TryKick(), Is.True, KickWaitDiagnostics());
                Assert.That(root.Hero.HasPendingKick, Is.True, KickWaitDiagnostics());
                Assert.That(root.Hero.State.IsKicking, Is.False);
                Assert.That(root.Hero.State.Stamina, Is.EqualTo(S.MaxStamina));
                float remaining = root.Hero.PendingKickSeconds;
                using (GameTimeScaleRuntime.AcquirePause())
                {
                    root.Tick(.5f);
                    Assert.That(root.Hero.PendingKickSeconds, Is.EqualTo(remaining));
                }
                Assert.That(root.Hero.TryKick(), Is.True);
                Assert.That(root.Hero.PendingKickSeconds, Is.EqualTo(remaining), "Repeated Q cannot extend the wait.");
                for (int tick = 0; tick < 24 && root.Hero.HasPendingKick; tick++)
                    root.Tick(CombatTestRoot.SimulationStep);
                Assert.That(root.Hero.State.IsKicking, Is.True, "The travelling sole really lands before spending effort. " + KickWaitDiagnostics());
                Assert.That(root.Hero.KickStrikingSide, Is.EqualTo(1));
                Assert.That(root.Hero.Footwork.SelectedSupportSide, Is.Zero);
                Object.Destroy(obstruction); obstruction = null;

                PlacePair(3f);
                yield return null;
                WalkToFootLift(0);
                obstruction = BlockRightKickSupport();
                Assert.That(root.Hero.TryKick(), Is.True);
                Assert.That(root.Hero.HasPendingKick, Is.True, KickWaitDiagnostics());
                int sequence = root.Hero.State.AttackSequence;
                input.Press(mouse.leftButton);
                InputSystem.Update();
                Assert.That(GameInput.WasPressed(GameInputAction.MeleeAttack, GameInputContext.Gameplay), Is.True);
                Assert.That((bool)consume.Invoke(root, null), Is.True);
                Assert.That(root.Hero.HasPendingKick, Is.False, "A fresh LMB replaces the waiting Q in the one action slot.");
                Assert.That(root.Hero.State.IsCharging, Is.True, "Waiting for a sole cannot block a new weapon press.");
                Assert.That(root.Hero.State.AttackSequence, Is.EqualTo(sequence + 1));
                Assert.That(root.Hero.State.BufferedAction, Is.EqualTo(MeleeBufferedAction.None));
                input.Release(mouse.leftButton);
                InputSystem.Update();
                Assert.That((bool)consume.Invoke(root, null), Is.True);
                Assert.That(root.Hero.State.Phase, Is.EqualTo(MeleePhase.Windup));
                root.Tick(.25f);
                Assert.That(root.Hero.State.IsKicking, Is.False, "The replaced Q cannot fire after its former support wait.");
                Assert.That(root.Hero.State.AttackSequence, Is.EqualTo(sequence + 1));
                Object.Destroy(obstruction); obstruction = null;

                PlacePair(3f);
                yield return null;
                WalkToFootLift(0);
                obstruction = BlockRightKickSupport();
                Assert.That(root.Hero.TryKick(), Is.True);
                Assert.That(root.Hero.HasPendingKick, Is.True);
                root.ResetRound();
                Assert.That(root.Hero.HasPendingKick, Is.False);
                root.Tick(.25f);
                Assert.That(root.Hero.State.IsKicking, Is.False);
                Object.Destroy(obstruction); obstruction = null;

                PlacePair(3f);
                yield return null;
                WalkToFootLift(0);
                obstruction = BlockRightKickSupport();
                Assert.That(root.Hero.TryKick(), Is.True);
                root.Hero.State.ReceiveKick();
                root.Tick(CombatTestRoot.SimulationStep);
                Assert.That(root.Hero.HasPendingKick, Is.False);
                root.Tick(.5f);
                Assert.That(root.Hero.State.IsKicking, Is.False, "Interruption cannot postpone Q until recovery.");
                Object.Destroy(obstruction); obstruction = null;

                PlacePair(3f);
                yield return null;
                WalkToFootLift(0);
                obstruction = BlockRightKickSupport();
                Assert.That(root.Hero.TryKick(), Is.True);
                Assert.That(root.Hero.HasPendingKick, Is.True);
                using (GameTimeScaleRuntime.AcquirePause())
                {
                    root.AutomaticSimulation = true;
                    yield return null;
                    root.AutomaticSimulation = false;
                    Assert.That(root.Hero.HasPendingKick, Is.False, "Losing gameplay input ownership cancels the request.");
                }
                root.Tick(.25f);
                Assert.That(root.Hero.State.IsKicking, Is.False);
                LogAssert.NoUnexpectedReceived();
            }
            finally
            {
                if (root != null) root.AutomaticSimulation = false;
                if (obstruction != null) Object.Destroy(obstruction);
                input.TearDown();
            }
        }

        [UnityTest]
        public IEnumerator Range_CloseKicksAgreeWithPresentedBootAndFrozenAnatomy()
        {
            yield return SceneManager.LoadSceneAsync(SceneIds.CombatTest);
            root = Object.FindAnyObjectByType<CombatTestRoot>();
            root.AutomaticSimulation = false;
            var csv = new System.Text.StringBuilder("side,target_pose,distance,elapsed,clip_seconds,target_phase,target_progress,root_gap,minimum_gap,part,sweep_hit,presented_delta,final_delta,boot_surface_distance,forward_reach,lateral_reach,from_x,from_y,from_z,to_x,to_y,to_z,surface_x,surface_y,surface_z,target_attack_elapsed,maximum_boot_projection,projection_beyond_sphere,minimum_boot_vertex_gap,maximum_vertex_penetration,penetrating_part,sphere_hit,surface_witness,witness_count,striking_vertex_penetration,accepted_before_sample,surface_from_x,surface_from_y,surface_from_z,surface_to_x,surface_to_y,surface_to_z\n");
            var mesh = new Mesh();
            var vertices = new System.Collections.Generic.List<Vector3>();
            var strikingVertices = new System.Collections.Generic.List<Vector3>();
            var soleVertices = new System.Collections.Generic.List<Vector3>();
            string path = Path.GetFullPath("TestResults/kick-surface-geometry.csv");
            try
            {
                for (int striking = 0; striking < 2; striking++)
                foreach (bool attacking in new[] { false, true })
                foreach (float distance in new[] { .80f, .90f, 1f, 1.6f })
                {
                    // The .80m control uses Ready: a real NPC attack at this
                    // spacing correctly selects a shove and interrupts the kick.
                    if (attacking && (distance < .85f || distance > 1.1f)) continue;
                    PlacePair(3f);
                    yield return null;
                    WalkToFootLift(striking);
                    root.Opponent.ResetActor(root.Hero.transform.position + root.Hero.transform.forward * distance,
                        -root.Hero.transform.forward);
                    Physics.SyncTransforms();
                    Assert.That(root.Hero.TryKick(), Is.True, KickWaitDiagnostics());
                    Assert.That(root.Hero.KickStrikingSide, Is.EqualTo(striking));
                    Assert.That(root.Hero.KickSurfaceWitnessCount, Is.InRange(1, CombatActor.KickSurfaceWitnessLimit));
                    int sequence = root.Hero.State.AttackSequence, samples = 0, lastSample = 0;
                    int surfaceRepairs = 0;
                    var previousSurface = new Vector3[root.Hero.KickSurfaceWitnessCount];
                    var presentedSurface = new Vector3[root.Hero.KickSurfaceWitnessCount];
                    bool requestedAttack = false, capture = false, previousValid = false, anyHit = false;
                    Vector3 previous = default;
                    float minimum = float.PositiveInfinity, maximumDelta = 0f;
                    float maximumBeyondSphere = float.NegativeInfinity, maximumMissPenetration = 0f, maximumStrikingPenetration = 0f;
                    SkinnedMeshRenderer boot = null, sole = null;
                    foreach (SkinnedMeshRenderer skin in ((Player3DCharacterPresentation)root.Player.Visual).Registry.ModelRoot.GetComponentsInChildren<SkinnedMeshRenderer>(true))
                        if (skin.name.EndsWith("Boot." + (striking == 0 ? "L" : "R"), System.StringComparison.Ordinal))
                            boot = skin;
                        else if (skin.name.EndsWith("BootSole." + (striking == 0 ? "L" : "R"), System.StringComparison.Ordinal))
                            sole = skin;
                    Assert.That(boot, Is.Not.Null, "The independent binding check uses the rendered production boot.");
                    Assert.That(sole, Is.Not.Null, "The sole mesh defines the striking surface without including the ankle cuff or shin.");
                    int[] triangles = boot.sharedMesh.triangles;
                    Vector3[] bootBindVertices = boot.sharedMesh.vertices;
                    int footIndex = System.Array.IndexOf(boot.bones, FindKickBone(striking == 0 ? "foot.L" : "foot.R"));
                    Vector3 bindAnkle = boot.transform.TransformPoint(boot.sharedMesh.bindposes[footIndex].inverse.GetColumn(3));
                    var forefoot = new bool[bootBindVertices.Length];
                    for (int i = 0; i < forefoot.Length; i++)
                        forefoot[i] = Vector3.Dot(boot.transform.TransformPoint(bootBindVertices[i]) - bindAnkle, root.Hero.transform.forward) > 0f;
                    for (int tick = 0; tick < 100 && root.Hero.State.KickElapsed <= S.KickWindupSeconds + S.KickActiveSeconds + .009f; tick++)
                    {
                        if (attacking && !requestedAttack && root.Hero.State.KickElapsed >= .025f)
                        {
                            root.Opponent.State.ObserveLateralCue(-1);
                            Assert.That(root.Opponent.TryAttack(), Is.True);
                            Assert.That(root.Opponent.State.Phase, Is.EqualTo(MeleePhase.Windup));
                            requestedAttack = true;
                        }
                        bool acceptedBeforeSample = root.Opponent.ReceivedImpactCount > 0;
                        root.Tick(CombatTestRoot.SimulationStep);
                        CombatActor.KickSweepObservation sample = root.Hero.LastKickSweep;
                        if (sample.Sequence != sequence || sample.Sample == lastSample) continue;
                        lastSample = sample.Sample; samples++; anyHit |= sample.HasHit;
                        Assert.That(sample.ObstacleDistance, Is.EqualTo(float.PositiveInfinity));
                        if (previousValid)
                            Assert.That(Vector3.Distance(previous, sample.From), Is.LessThan(.00001f),
                                "Adjacent sweeps retain the previous accepted WORLD endpoint.");
                        previous = sample.To; previousValid = true;
                        Assert.That(root.Opponent.Hurtboxes.MeasureSweepSurfaceGap(sample.From, sample.To,
                            CombatActor.BootRadius, out CombatHurtboxes.SweepSurfaceGap gap), Is.True);
                        minimum = Mathf.Min(minimum, gap.Gap);
                        if (gap.Gap < -.00002f) Assert.That(sample.SphereHit, Is.True, "A penetrating sphere cannot be a sphere-query Miss.");
                        if (gap.Gap > .00002f) Assert.That(sample.SphereHit, Is.False, "A physically separated sphere cannot be a sphere-query Hit.");
                        if (sample.SurfaceWitness >= 0)
                        {
                            Assert.That(sample.SphereHit, Is.False, "Surface witnesses repair a sphere Miss without changing its radius.");
                            Assert.That(sample.HasHit, Is.True);
                            Assert.That(root.Opponent.Hurtboxes.MeasureSweepSurfaceGap(sample.SurfaceFrom, sample.SurfaceTo, 0f, out var witnessGap), Is.True);
                            Assert.That(witnessGap.Gap, Is.LessThanOrEqualTo(.000101f), "A surface contact needs the real zero-radius path, within the existing sweep solver tolerance.");
                            if (samples > 1)
                                Assert.That(Vector3.Distance(previousSurface[sample.SurfaceWitness], sample.SurfaceFrom), Is.LessThan(.00001f),
                                    "A repaired contact also starts at its previous WORLD witness endpoint.");
                            if (!acceptedBeforeSample) surfaceRepairs++;
                        }
                        float presentedDelta = Vector3.Distance(sample.PresentedBoot, sample.To);
                        float finalDelta = Vector3.Distance(root.Hero.KickBootPosition, sample.To);
                        // A boundary sweep clamps to ActiveEnd while the final
                        // animation may already have entered its authored return.
                        if (Mathf.Abs(sample.Elapsed - sample.ClipSeconds) < .00002f)
                        {
                            maximumDelta = Mathf.Max(maximumDelta, Mathf.Max(presentedDelta, finalDelta));
                            Assert.That(presentedDelta, Is.LessThan(.002f), "The sampled contact endpoint matches the accepted complete presentation.");
                            Assert.That(finalDelta, Is.LessThan(.002f), "The final rendered rig retains the contact endpoint at the same clip time.");
                        }
                        boot.BakeMesh(mesh, true); vertices.Clear(); mesh.GetVertices(vertices);
                        for (int i = 0; i < vertices.Count; i++) vertices[i] = boot.transform.TransformPoint(vertices[i]);
                        strikingVertices.Clear();
                        for (int i = 0; i < vertices.Count; i++) if (forefoot[i]) strikingVertices.Add(vertices[i]);
                        sole.BakeMesh(mesh, true); soleVertices.Clear(); mesh.GetVertices(soleVertices);
                        foreach (Vector3 vertex in soleVertices) strikingVertices.Add(sole.transform.TransformPoint(vertex));
                        for (int i = 0; i < presentedSurface.Length; i++)
                        {
                            presentedSurface[i] = root.Hero.KickSurfacePosition(i);
                            float binding = float.PositiveInfinity;
                            foreach (Vector3 vertex in strikingVertices)
                                binding = Mathf.Min(binding, Vector3.Distance(vertex, presentedSurface[i]));
                            Assert.That(binding, Is.LessThan(.0005f), "A cached witness must belong to the independently skinned sole/toe, never the cuff or shin.");
                        }
                        if (sample.SurfaceWitness >= 0 && Mathf.Abs(sample.Elapsed - sample.ClipSeconds) < .00002f)
                            Assert.That(Vector3.Distance(sample.SurfaceTo, presentedSurface[sample.SurfaceWitness]), Is.LessThan(.0005f),
                                "The zero-radius contact endpoint stays attached to the final presented shoe.");
                        System.Array.Copy(presentedSurface, previousSurface, presentedSurface.Length);
                        float bootSquare = float.PositiveInfinity;
                        for (int i = 0; i < triangles.Length; i += 3)
                            bootSquare = Mathf.Min(bootSquare, KickPointTriangleSquared(root.Hero.KickBootPosition,
                                vertices[triangles[i]], vertices[triangles[i + 1]], vertices[triangles[i + 2]]));
                        float bootDistance = Mathf.Sqrt(bootSquare);
                        Assert.That(bootDistance, Is.LessThanOrEqualTo(CombatActor.BootRadius + .005f),
                            "The contact centre remains on/inside the rendered boot envelope, not detached from it.");
                        // This observation is stronger than centre-to-mesh
                        // binding: a toe may extend beyond the sphere. Query
                        // only this kick's finite baked vertex set against the
                        // same frozen target. This does not prove triangle coverage.
                        Vector3 towardSurface = (gap.Surface - sample.To).normalized;
                        float maximumProjection = float.NegativeInfinity, vertexGap = float.PositiveInfinity;
                        float penetration = 0f;
                        Player3DAnatomicalPart penetratingPart = default;
                        foreach (Vector3 vertex in vertices)
                        {
                            maximumProjection = Mathf.Max(maximumProjection, Vector3.Dot(vertex - sample.To, towardSurface));
                            if (!root.Opponent.Hurtboxes.MeasureSweepSurfaceGap(vertex, vertex, 0f, out var pointGap))
                                Assert.Fail("The frozen anatomy must also accept finite read-only boot vertex probes.");
                            vertexGap = Mathf.Min(vertexGap, pointGap.Gap);
                            // Distance to a solid is zero inside it. Surface()
                            // then measures the distance back to its boundary,
                            // distinguishing an interior vertex from a mere touch.
                            float depth = pointGap.Gap <= .000001f ? Vector3.Distance(vertex, pointGap.Surface) : 0f;
                            if (depth > penetration + .00002f)
                            { penetration = depth; penetratingPart = pointGap.Part; }
                        }
                        maximumBeyondSphere = Mathf.Max(maximumBeyondSphere, maximumProjection - CombatActor.BootRadius);
                        if (!sample.SphereHit && !acceptedBeforeSample)
                            maximumMissPenetration = Mathf.Max(maximumMissPenetration, penetration);
                        float strikingPenetration = 0f;
                        foreach (Vector3 vertex in strikingVertices)
                        {
                            float binding = float.PositiveInfinity;
                            foreach (Vector3 witness in presentedSurface)
                                binding = Mathf.Min(binding, Vector3.Distance(vertex, witness));
                            if (Mathf.Abs(sample.Elapsed - sample.ClipSeconds) < .00002f)
                                Assert.That(binding, Is.LessThan(.0005f), "Every independently skinned sole/toe vertex must have an attached cached witness.");
                            Assert.That(root.Opponent.Hurtboxes.MeasureSweepSurfaceGap(vertex, vertex, 0f, out var pointGap), Is.True);
                            if (pointGap.Gap <= .000001f)
                                strikingPenetration = Mathf.Max(strikingPenetration, Vector3.Distance(vertex, pointGap.Surface));
                        }
                        if (!acceptedBeforeSample)
                        {
                            maximumStrikingPenetration = Mathf.Max(maximumStrikingPenetration, strikingPenetration);
                            if (strikingPenetration > .00002f && Mathf.Abs(sample.Elapsed - sample.ClipSeconds) < .00002f)
                                Assert.That(sample.HasHit, Is.True, "A visible sole/toe vertex already inside frozen anatomy cannot be missed before the first accepted contact.");
                        }
                        Vector3 reach = sample.To - root.Hero.transform.position;
                        csv.AppendFormat(System.Globalization.CultureInfo.InvariantCulture,
                            "{0},{1},{2:F3},{3:F6},{4:F6},{5},{6:F6},{7:F6},{8:F6},{9},{10},{11:F6},{12:F6},{13:F6},{14:F6},{15:F6},{16:F6},{17:F6},{18:F6},{19:F6},{20:F6},{21:F6},{22:F6},{23:F6},{24:F6},{25:F6},{26:F6},{27:F6},{28:F6},{29:F6},{30},{31},{32},{33},{34:F6},{35},{36:F6},{37:F6},{38:F6},{39:F6},{40:F6},{41:F6}\n",
                            striking == 0 ? "Left" : "Right", attacking ? "Windup" : "Ready", distance,
                            sample.Elapsed, sample.ClipSeconds, sample.TargetPhase, sample.TargetPhaseProgress,
                            Vector3.Distance(root.Hero.transform.position, root.Opponent.transform.position), gap.Gap,
                            gap.Part, sample.HasHit, presentedDelta, finalDelta, bootDistance,
                            Vector3.Dot(reach, root.Hero.transform.forward), Vector3.Dot(reach, root.Hero.transform.right),
                            sample.From.x, sample.From.y, sample.From.z, sample.To.x, sample.To.y, sample.To.z,
                            gap.Surface.x, gap.Surface.y, gap.Surface.z, sample.TargetAttackElapsed,
                            maximumProjection, maximumProjection - CombatActor.BootRadius, vertexGap, penetration, penetratingPart,
                            sample.SphereHit, sample.SurfaceWitness, root.Hero.KickSurfaceWitnessCount, strikingPenetration, acceptedBeforeSample,
                            sample.SurfaceFrom.x, sample.SurfaceFrom.y, sample.SurfaceFrom.z, sample.SurfaceTo.x, sample.SurfaceTo.y, sample.SurfaceTo.z);
                        if (!capture && sample.Elapsed >= .35f &&
                            (Mathf.Abs(distance - .9f) < .001f || !attacking && Mathf.Abs(distance - 1f) < .001f))
                        {
                            capture = true;
                            yield return null; // New render frame: skin and prop must use this same frozen pose.
                            root.Hero.Present(); root.Opponent.Present();
                            CaptureKick((striking == 0 ? "left" : "right") + "-kick-close-" +
                                (attacking ? "windup-" : "ready-") + Mathf.RoundToInt(distance * 100f));
                        }
                    }
                    Assert.That(samples, Is.GreaterThanOrEqualTo(12), "The entire .10s active interval is observed.");
                    Assert.That(root.Opponent.ReceivedImpactCount, Is.EqualTo(anyHit ? 1 : 0),
                        "Actual sphere/surface contact grants one impact; a measured separation grants none.");
                    if (!attacking && distance < .85f)
                        Assert.That(anyHit, Is.True, "The existing .80m physical-contact control remains reachable by both feet.");
                    if (striking == 1 && attacking && Mathf.Abs(distance - .9f) < .001f)
                    {
                        Assert.That(anyHit, Is.True, "Regression: the right sole visibly penetrated the thigh while the offset sphere missed the whole kick.");
                        Assert.That(surfaceRepairs, Is.GreaterThan(0), "This case must exercise the actual zero-radius surface contact, not a larger sphere.");
                    }
                    if (distance > 1.5f) Assert.That(anyHit, Is.False, "The actual sole/toe surface cannot turn a distant physical miss into a Hit.");
                    TestContext.Out.WriteLine($"Kick geometry: {(striking == 0 ? "Left" : "Right")}, {(attacking ? "Windup" : "Ready")}, distance={distance:F2}m, min surface gap={minimum:F6}m, hit={anyHit}, maximum pose delta={maximumDelta:F6}m.");
                    TestContext.Out.WriteLine($"Kick envelope: witnesses={root.Hero.KickSurfaceWitnessCount}, surface repairs={surfaceRepairs}, max projection beyond sphere={maximumBeyondSphere:F6}m, pre-contact boot/striking vertex penetration={maximumMissPenetration:F6}/{maximumStrikingPenetration:F6}m. Vertex probes do not prove the complete triangle envelope.");
                }
                LogAssert.NoUnexpectedReceived();
            }
            finally
            {
                Directory.CreateDirectory(Path.GetDirectoryName(path)); File.WriteAllText(path, csv.ToString());
                Object.Destroy(mesh);
                TestContext.Out.WriteLine(path);
            }
        }

        private static float KickPointTriangleSquared(Vector3 point, Vector3 a, Vector3 b, Vector3 c)
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
                return (point - (b + (c - b) * ((d4 - d3) / (d4 - d3 + d5 - d6)))).sqrMagnitude;
            float sum = va + vb + vc;
            if (Mathf.Abs(sum) < .0000000001f)
                return Mathf.Min(KickPointSegmentSquared(point, a, b),
                    Mathf.Min(KickPointSegmentSquared(point, b, c), KickPointSegmentSquared(point, c, a)));
            return (point - (a + ab * (vb / sum) + ac * (vc / sum))).sqrMagnitude;
        }

        private static float KickPointSegmentSquared(Vector3 point, Vector3 a, Vector3 b)
        {
            Vector3 delta = b - a;
            return (point - (a + delta * (delta.sqrMagnitude > 0f ?
                Mathf.Clamp01(Vector3.Dot(point - a, delta) / delta.sqrMagnitude) : 0f))).sqrMagnitude;
        }


        private GameObject BlockRightKickSupport()
        {
            var obstacle = new GameObject("Test kick support obstruction");
            // This flat-floor fixture's drawn ankle includes the controller-root
            // offset; the grounded ankle used by LandingClear does not. Place
            // the obstacle beside the floor ray, at the real clearance height.
            Vector3 clearance = FindKickBone("foot.R").position +
                Vector3.up * (.025f - PlayerFactory.GroundedRootOffset);
            obstacle.transform.position = clearance + Vector3.right * .06f;
            SphereCollider collider = obstacle.AddComponent<SphereCollider>(); collider.radius = .03f;
            Physics.SyncTransforms();
            Assert.That(Vector3.Distance(clearance, collider.ClosestPoint(clearance)), Is.LessThan(.04f),
                "The physical obstacle must actually overlap the support landing volume.");
            return obstacle;
        }

        private void WalkToFootLift(int side)
        {
            Transform sole = FindKickBone(side == 0 ? "foot.L" : "foot.R");
            float plantedY = sole.position.y;
            for (int tick = 0; tick < 80; tick++)
            {
                root.Hero.Body.Move(Vector3.forward * (1.6f * CombatTestRoot.SimulationStep));
                root.Tick(CombatTestRoot.SimulationStep);
                if (root.Hero.Footwork.TransferringFoot && sole.position.y > plantedY + .055f) return;
            }
            Assert.Fail("The production walking gait did not lift the selected foot. " + KickWaitDiagnostics());
        }

        private string KickWaitDiagnostics()
        {
            CombatActor actor = root.Hero;
            CombatFootwork feet = actor.Footwork;
            return $"phase={actor.State.Phase}, pending={actor.HasPendingKick}, remaining={actor.PendingKickSeconds:F4}, " +
                $"support_reason={feet.LastKickSupportFailure}, gap={feet.LastKickSupportGap:F4}, " +
                $"actual_ankle={FindKickBone("foot.L").position:F4}, measured_ankle={feet.LastKickSupportAnkle:F4}, " +
                $"ground={feet.LastKickSupportGround:F4}, wait_eligible={feet.CanWaitForKickSupport}, " +
                $"balance_ready={actor.HasAttackBalance}, stamina={actor.State.Stamina:F2}, {feet.SupportDiagnostics}";
        }

        [UnityTest]
        public IEnumerator Range_OpponentStylesObserveKickAndResetWithoutChangingCombatStats()
        {
            yield return SceneManager.LoadSceneAsync(SceneIds.CombatTest);
            root = Object.FindAnyObjectByType<CombatTestRoot>();
            root.AutomaticSimulation = false;
            foreach (CombatOpponentStyle style in new[] { CombatOpponentStyle.Cautious, CombatOpponentStyle.Pressuring, CombatOpponentStyle.Patient })
            {
                Assert.That(root.SetOpponentStyle(style), Is.True);
                root.Hero.ResetActor(Vector3.up * PlayerFactory.GroundedRootOffset, Vector3.forward);
                root.Opponent.ResetActor(Vector3.up * PlayerFactory.GroundedRootOffset + Vector3.forward * 1.2f, Vector3.back);
                Physics.SyncTransforms();
                yield return null;
                Assert.That(root.Hero.TryKick(), Is.True);
                root.Tick(.18f);
                Assert.That(root.Opponent.State.Phase, Is.EqualTo(MeleePhase.Ready), "No style evades before seeing the .20s tell.");
                root.Tick(.06f);
                Assert.That(root.Opponent.State.Phase, Is.EqualTo(MeleePhase.Step));
                Assert.That(root.Opponent.State.Settings.MaxHealth, Is.EqualTo(S.MaxHealth));
                Assert.That(root.Opponent.State.Settings.Damage, Is.EqualTo(S.Damage));
                root.ResetRound();
                Assert.That(root.OpponentStyle, Is.EqualTo(style));
                Assert.That(root.Opponent.State.Phase, Is.EqualTo(MeleePhase.Ready));
            }
        }

        private Transform FindKickBone(string name)
        {
            foreach (Transform bone in root.Hero.DamageRigRoot.GetComponentsInChildren<Transform>(true))
                if (bone.name == name) return bone;
            Assert.Fail("Missing production bone " + name); return null;
        }

        private void CaptureKick(string name)
        {
            Camera camera = root.CameraFollow.Camera;
            CaptureKickView(camera, name + "-shoulder");
            Vector3 position = camera.transform.position; Quaternion rotation = camera.transform.rotation;
            float fieldOfView = camera.fieldOfView;
            Vector3 target = root.Hero.transform.position + Vector3.up * .85f + Vector3.forward * .4f;
            try
            {
                Vector3 eye = target + Vector3.left * 2.6f + Vector3.up * .3f - Vector3.forward * .4f;
                camera.transform.SetPositionAndRotation(eye, Quaternion.LookRotation(target - eye));
                camera.fieldOfView = 50f;
                CaptureKickView(camera, name + "-side");
            }
            finally { camera.transform.SetPositionAndRotation(position, rotation); camera.fieldOfView = fieldOfView; }
        }

        private static void CaptureKickView(Camera camera, string name)
        {
            string path = Path.Combine(Directory.GetCurrentDirectory(), "Captures", SceneIds.CombatTest, name + ".png");
            LogAssert.Expect(LogType.Log, "Area capture wrote " + path);
            AreaCaptureFixture.CaptureCurrentCamera(camera, SceneIds.CombatTest, name);
        }
    }
}

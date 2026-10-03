using System.Collections;
using System.Diagnostics;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

namespace BarPromenade.Tests.PlayMode
{
    /// <summary>Bounds simulation CPU work against the currently imported arena assets; not rendered FPS.</summary>
    public sealed partial class CombatPerformancePlayModeTests
    {
        private const float TickSeconds = 1f / 60f;
        private const int MeasuredTicks = 20;
        private CombatTestRoot root;
        private float previousCaptureDelta;

        [UnitySetUp]
        public IEnumerator SetUp()
        {
            previousCaptureDelta = Time.captureDeltaTime;
            Time.captureDeltaTime = TickSeconds;
            RetroAudioService.Instance?.StopAll();
            EnsureListener();
            GameSessionState.BeginNewGame();
            yield return SceneManager.LoadSceneAsync(SceneIds.CombatTest, LoadSceneMode.Single);
            root = Object.FindAnyObjectByType<CombatTestRoot>();
            Assert.That(root, Is.Not.Null);
            root.AutomaticSimulation = false;
            for (int frame = 0; frame < 60 && (!root.IsInitialized ||
                !GameInput.CanRead(GameInputContext.Gameplay)); frame++) yield return null;
            Assert.That(root.IsInitialized && GameInput.CanRead(GameInputContext.Gameplay), Is.True,
                "The imported combat scene must initialize and release its input gate.");
            root.SetSparring(false);
            Assert.That(root.Sparring, Is.False);
        }

        [UnityTest]
        public IEnumerator Range_OpponentKeepsWeaponRoomAndAnswersTheObservedWhiff()
        {
            PlacePair(1.1f);
            foreach (CombatActor actor in new[] { root.Hero, root.Opponent })
            {
                actor.Body.height = 1.7f;
                actor.Body.radius = .32f;
                actor.Body.center = Vector3.up * .85f;
            }
            Physics.SyncTransforms();
            yield return null;
            try
            {
                root.AutomaticSimulation = false;
                MeleeCombatSettings settings = root.Hero.State.Settings;
                Vector3 ground = Vector3.up * PlayerFactory.GroundedRootOffset;
                void SparringPair(float distance, Vector3 heroFacing)
                {
                    root.SetSparring(true);
                    root.Hero.ResetActor(ground, heroFacing);
                    root.Opponent.ResetActor(ground + Vector3.forward * distance, Vector3.back);
                    Physics.SyncTransforms();
                }

                // A stationary target must see a weapon swing, not a clinch
                // created by the opponent's own committed windup movement.
                SparringPair(1.1f, Vector3.forward);
                Assert.That(root.Opponent.TryAttack(), Is.True);
                Quaternion committed = root.Opponent.transform.rotation;
                for (int tick = 0; tick < Mathf.CeilToInt(settings.WindupSeconds / CombatTestRoot.SimulationStep) + 1; tick++)
                {
                    root.Tick(CombatTestRoot.SimulationStep);
                    Assert.That(root.Opponent.State.IsShoving, Is.False,
                        "The opponent's own windup cannot turn an in-range weapon attack into a shove.");
                    Assert.That(Vector3.Distance(root.Hero.transform.position, root.Opponent.transform.position),
                        Is.GreaterThanOrEqualTo(.99f), "The windup and its braking tail keep room for the crowbar.");
                    Assert.That(Quaternion.Angle(committed, root.Opponent.transform.rotation), Is.LessThan(.1f));
                }
                Assert.That(root.Opponent.State.Phase, Is.EqualTo(MeleePhase.Active));

                // The gap is a movement limit, not immunity to being crowded.
                // A hero pressing close still converts the committed windup.
                SparringPair(1.1f, Vector3.forward);
                Assert.That(root.Opponent.TryAttack(), Is.True);
                root.Tick(.05f);
                root.Hero.ResetActor(root.Opponent.transform.position + root.Opponent.transform.forward * .8f,
                    -root.Opponent.transform.forward);
                Physics.SyncTransforms();
                root.Tick(CombatTestRoot.SimulationStep);
                Assert.That(root.Opponent.State.IsShoving, Is.True,
                    "Actual close pressure still routes the weapon windup into its ordinary shove.");
                int shoveSequence = root.Opponent.State.AttackSequence;
                for (int tick = 0; tick < Mathf.CeilToInt((settings.ShoveDurationSeconds + .2f) / CombatTestRoot.SimulationStep) &&
                    root.Opponent.State.IsShoving; tick++) root.Tick(CombatTestRoot.SimulationStep);
                Assert.That(root.Opponent.State.Phase, Is.EqualTo(MeleePhase.Ready));
                // Put the target back in the clinch: even a shove that failed
                // to create physical room must be followed by making space.
                root.Hero.ResetActor(root.Opponent.transform.position + root.Opponent.transform.forward * .8f,
                    -root.Opponent.transform.forward);
                Physics.SyncTransforms();
                root.Tick(.12f);
                Assert.That(root.Opponent.State.AttackSequence, Is.EqualTo(shoveSequence),
                    "The next ready boundary makes space rather than repeating a close shove.");
                Assert.That(root.OpponentIntent, Is.EqualTo(CombatOpponentIntent.Recover));
                Assert.That(Vector3.Distance(root.Hero.transform.position, root.Opponent.transform.position), Is.GreaterThan(.82f));

                // Drive W and the actual mouse bridge. Voluntary approach and
                // its braking tail must keep room for the hero's own windup too.
                var input = new UnityEngine.InputSystem.InputTestFixture();
                UnityEngine.InputSystem.Keyboard keyboard = null;
                UnityEngine.InputSystem.Mouse mouse = null;
                try
                {
                    input.Setup();
                    keyboard = UnityEngine.InputSystem.InputSystem.AddDevice<UnityEngine.InputSystem.Keyboard>();
                    mouse = UnityEngine.InputSystem.InputSystem.AddDevice<UnityEngine.InputSystem.Mouse>();
                    root.SetSparring(false);
                    PlacePair(1.1f);
                    RetroUiCanvas canvas = RetroUiTheme.CalculateCanvas(Screen.width, Screen.height);
                    Vector2 pointer = canvas.LogicalToScreen(new Vector2(320f, 180f));
                    pointer.y = Screen.height - pointer.y;
                    input.Set(mouse.position, pointer);
                    input.Press(keyboard.wKey);
                    input.Press(mouse.leftButton);
                    var consume = typeof(CombatTestRoot).GetMethod("UpdateCombatInput",
                        System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
                    ConsumeCombatMouseInput(consume, true);
                    root.Tick(TickSeconds);
                    input.Release(mouse.leftButton);
                    ConsumeCombatMouseInput(consume, false);
                    Assert.That(root.Hero.State.Phase, Is.EqualTo(MeleePhase.Windup));
                    float startZ = root.Hero.transform.position.z;
                    for (int frame = 0; frame < 40 && root.Hero.State.Phase == MeleePhase.Windup; frame++)
                    {
                        yield return null; // The real PlayerMotor reads the held W.
                        root.Tick(TickSeconds);
                        Assert.That(root.Hero.State.IsShoving, Is.False,
                            "W cannot replace the hero's committed weapon swing with a shove.");
                        Assert.That(Vector3.Distance(root.Hero.transform.position, root.Opponent.transform.position),
                            Is.GreaterThanOrEqualTo(.99f), "Voluntary travel reserves the same metre as NPC travel.");
                    }
                    Assert.That(root.Hero.State.Phase, Is.EqualTo(MeleePhase.Active));
                    Assert.That(root.Hero.transform.position.z, Is.GreaterThan(startZ + .01f),
                        "The spacing constraint limits approach instead of disabling W.");
                }
                finally
                {
                    if (keyboard != null && keyboard.added) UnityEngine.InputSystem.InputSystem.RemoveDevice(keyboard);
                    if (mouse != null && mouse.added) UnityEngine.InputSystem.InputSystem.RemoveDevice(mouse);
                    input.TearDown();
                }

                // Face away to produce a real light miss without harming or
                // staggering the observer. The first visible tell gets guarded.
                SparringPair(1.1f, Vector3.back);
                Assert.That(root.Hero.TryAttack(), Is.True);
                int contactTicks = Mathf.CeilToInt((settings.WindupSeconds + settings.ActiveSeconds) /
                    CombatTestRoot.SimulationStep);
                for (int tick = 0; tick <= contactTicks; tick++) root.Tick(CombatTestRoot.SimulationStep);
                Assert.That(root.Hero.State.AttackOutcome, Is.EqualTo(MeleeAttackOutcome.Miss));
                Assert.That(root.Hero.State.Phase, Is.EqualTo(MeleePhase.Recovery));
                root.Tick(.18f);
                Assert.That(root.Opponent.State.IsAttacking, Is.False,
                    "Seeing a miss still costs a visible reaction delay.");
                root.Tick(.1f);
                Assert.That(root.Hero.State.Phase, Is.EqualTo(MeleePhase.Recovery));
                Assert.That(root.Hero.State.RecoveryRemaining, Is.LessThan(.45f));
                Assert.That(root.Opponent.State.Phase, Is.EqualTo(MeleePhase.Windup),
                    "An observed light whiff must be answerable after reaction consumed part of its recovery.");
                Assert.That(root.Opponent.State.AttackPower, Is.Zero,
                    "The whiff answer uses an ordinary tap rather than waiting out a charge.");
                LogAssert.NoUnexpectedReceived();
            }
            finally
            {
                if (root != null) root.AutomaticSimulation = false;
            }
        }

        [UnityTest]
        public IEnumerator Range_SimulationTicksStayWithinFrameBudget()
        {
            var labels = new string[4];
            var averages = new double[4];
            var watch = new Stopwatch();
            for (int scenario = 0; scenario < labels.Length; scenario++)
            {
                float distance = scenario < 2 ? 1.4f : 4f;
                bool attacking = (scenario & 1) != 0;
                labels[scenario] = (attacking ? "both attacking" : "ready") + " distance=" + distance;
                PlacePair(distance);
                for (int frame = 0; frame < 4; frame++)
                {
                    root.Tick(TickSeconds);
                    yield return null;
                }
                if (attacking)
                {
                    Assert.That(root.Hero.TryAttack(), Is.True, labels[scenario] + ": hero starts");
                    Assert.That(root.Opponent.TryAttack(), Is.True, labels[scenario] + ": opponent starts");
                    // Start timing in the windup and continue through its active contact window.
                    for (int frame = 0; frame < 12; frame++)
                    {
                        root.Tick(TickSeconds);
                        yield return null;
                    }
                    Assert.That(root.Hero.State.IsAttacking && root.Opponent.State.IsAttacking, Is.True,
                        labels[scenario] + ": both swings must still be running when timing starts");
                }

                watch.Reset();
                for (int frame = 0; frame < MeasuredTicks; frame++)
                {
                    watch.Start();
                    root.Tick(TickSeconds);
                    watch.Stop();
                    if (attacking)
                    {
                        Assert.That(root.Hero.ContactPoseSamples, Is.LessThanOrEqualTo(1),
                            "An adjacent 120 Hz interval must not add a pose sample due to float roundoff.");
                        Assert.That(root.Opponent.ContactPoseSamples, Is.LessThanOrEqualTo(1), labels[scenario]);
                    }
                    yield return null;
                }
                averages[scenario] = watch.Elapsed.TotalMilliseconds / MeasuredTicks;
                TestContext.Out.WriteLine($"Combat simulation CPU: {labels[scenario]}, " +
                    $"{averages[scenario]:F3} ms/tick; excludes LateUpdate and rendering.");
                Assert.That(root.Hero != null && root.Opponent != null && root.Hero.Weapon != null &&
                    root.Opponent.Weapon != null, Is.True, labels[scenario] + ": both actors and weapons survive");
                LogAssert.NoUnexpectedReceived();
            }
            for (int scenario = 0; scenario < averages.Length; scenario++)
                Assert.That(averages[scenario], Is.LessThan(12d),
                    labels[scenario] + ": leave CPU headroom for LateUpdate, physics and rendering");
        }

        [UnityTest]
        public IEnumerator Range_FrameHitchDropsCatchUpDebtAndKeepsFractionalStep()
        {
            PlacePair(4f);
            Assert.That(root.Hero.TryAttack() && root.Opponent.TryAttack(), Is.True);
            float halfStep = CombatTestRoot.SimulationStep * .5f;
            float frameLimit = CombatTestRoot.SimulationStep * CombatTestRoot.MaximumFrameSubsteps;
            root.TickFrame(halfStep);
            Assert.That(root.Hero.State.AttackElapsed, Is.Zero);
            // Repeated slow frames reproduce the feedback loop, not just one isolated hitch.
            for (int frame = 1; frame <= 3; frame++)
            {
                root.TickFrame(.5f);
                Assert.That(root.Hero.State.AttackElapsed, Is.EqualTo(frame * frameLimit).Within(.000001f));
                Assert.That(root.Opponent.State.AttackElapsed, Is.EqualTo(root.Hero.State.AttackElapsed));
                yield return null;
            }
            float before = root.Hero.State.AttackElapsed;
            root.TickFrame(halfStep);
            Assert.That(root.Hero.State.AttackElapsed,
                Is.EqualTo(before + CombatTestRoot.SimulationStep).Within(.000001f),
                "Keep the pre-hitch fractional step, but do not replay discarded wall time.");
            before = root.Hero.State.AttackElapsed;
            root.TickFrame(TickSeconds);
            Assert.That(root.Hero.State.AttackElapsed, Is.EqualTo(before + TickSeconds).Within(.000001f));
            root.TickFrame(0f);
            Assert.That(root.Hero.State.AttackElapsed, Is.EqualTo(before + TickSeconds).Within(.000001f));
            LogAssert.NoUnexpectedReceived();
        }

        [UnityTest]
        public IEnumerator Range_RecoveryClickKeepsOneShortLivedIntent()
        {
            var input = new UnityEngine.InputSystem.InputTestFixture();
            UnityEngine.InputSystem.Mouse mouse = null;
            var consume = typeof(CombatTestRoot).GetMethod("UpdateCombatInput",
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
            try
            {
                input.Setup();
                mouse = UnityEngine.InputSystem.InputSystem.AddDevice<UnityEngine.InputSystem.Mouse>();
                RetroUiCanvas canvas = RetroUiTheme.CalculateCanvas(Screen.width, Screen.height);
                Vector2 pointer = canvas.LogicalToScreen(new Vector2(320f, 180f));
                pointer.y = Screen.height - pointer.y;
                input.Set(mouse.position, pointer);
                for (int scenario = 0; scenario < 5; scenario++)
                {
                    input.Release(mouse.leftButton);
                    ConsumeCombatMouseInput(consume, false);
                    PlacePair(scenario == 4 ? .82f : 4f);
                    CombatActor actor = root.Hero;
                    int sequence = actor.State.AttackSequence;
                    // This is the real physical readiness gate, without a hit
                    // whose random landing would obscure input-lifetime assertions.
                    actor.ImpactMotion.BeginRecoveryStep(0, actor.transform.position, .2f);
                    Assert.That(actor.HasAttackBalance, Is.False);
                    input.Press(mouse.leftButton);
                    ConsumeCombatMouseInput(consume, true);
                    Assert.That(actor.State.HasBufferedCharge, Is.True);
                    Assert.That(actor.State.Phase, Is.EqualTo(MeleePhase.Ready));
                    Assert.That(actor.State.AttackSequence, Is.EqualTo(sequence));
                    if (scenario != 1)
                    {
                        input.Release(mouse.leftButton);
                        ConsumeCombatMouseInput(consume, false);
                    }
                    if (scenario == 2)
                        actor.State.Advance(actor.State.Settings.AttackBufferSeconds + .01f, false);
                    else if (scenario == 3)
                        Assert.That(actor.State.ReceiveShove(), Is.True);
                    else actor.State.Advance(.1f, false);
                    // Return just the physical gate; resetting the actor would
                    // erase the input being verified and hide a lifetime defect.
                    actor.ImpactMotion.Reset();
                    root.Tick(CombatTestRoot.SimulationStep);
                    if (scenario < 2 || scenario == 4)
                    {
                        Assert.That(actor.State.Phase, Is.EqualTo(scenario == 1 ? MeleePhase.Charging : MeleePhase.Windup));
                        Assert.That(actor.State.AttackSequence, Is.EqualTo(sequence + 1));
                        Assert.That(actor.State.HasBufferedAttack, Is.False);
                        if (scenario != 1) Assert.That(actor.State.AttackPower, Is.Zero);
                        if (scenario == 4)
                        {
                            root.Tick(CombatTestRoot.SimulationStep);
                            Assert.That(actor.State.Phase, Is.EqualTo(MeleePhase.Shoving),
                                "A consumed balance press keeps the ordinary close-contact shove conversion.");
                            Assert.That(actor.State.HasBufferedAttack, Is.False);
                        }
                        else if (scenario == 1)
                        {
                            root.Tick(actor.State.Settings.AttackBufferSeconds + .01f);
                            Assert.That(actor.State.IsCharging, Is.True,
                                "The queue deadline ends when consumed; it cannot cancel a live held charge.");
                            Assert.That(actor.State.AttackSequence, Is.EqualTo(sequence + 1));
                            input.Release(mouse.leftButton);
                            ConsumeCombatMouseInput(consume, false);
                            Assert.That(actor.State.Phase, Is.EqualTo(MeleePhase.Windup));
                            Assert.That(actor.State.AttackSequence, Is.EqualTo(sequence + 1));
                        }
                    }
                    else
                    {
                        Assert.That(actor.State.HasBufferedAttack, Is.False);
                        Assert.That(actor.State.IsCharging || actor.State.IsAttacking, Is.False,
                            "Expired or interrupted input cannot fire when physical support returns.");
                    }
                    yield return null;
                }
                LogAssert.NoUnexpectedReceived();
            }
            finally
            {
                if (mouse != null && mouse.added) UnityEngine.InputSystem.InputSystem.RemoveDevice(mouse);
                input.TearDown();
            }
        }

        private void PlacePair(float distance)
        {
            root.ResetRound();
            Vector3 ground = Vector3.up * PlayerFactory.GroundedRootOffset;
            root.Hero.ResetActor(ground, Vector3.forward);
            root.Opponent.ResetActor(ground + Vector3.forward * distance, Vector3.back);
            Physics.SyncTransforms();
        }

        [UnityTest]
        public IEnumerator Range_CloseContactUsesFastShove()
        {
            for (int scenario = 0; scenario < 7; scenario++)
            {
                root.SetSparring(false);
                PlacePair(scenario == 3 ? 1.4f : scenario >= 5 ? CombatActor.ShoveRange : .82f);
                bool opponentActs = scenario == 4 || scenario == 6;
                CombatActor actor = opponentActs ? root.Opponent : root.Hero;
                CombatActor target = opponentActs ? root.Hero : root.Opponent;
                string context = scenario switch
                {
                    0 => "hero requested attack", 1 => "hero direct attack", 2 => "hero charge press",
                    3 => "hero charged release", 4 => "opponent requested attack",
                    5 => "hero maximum shove reach", _ => "opponent maximum shove reach"
                };
                if (scenario == 3)
                {
                    Assert.That(actor.RequestCharge(), Is.True, context);
                    root.Tick(.25f);
                    target.ResetActor(actor.transform.position + actor.transform.forward * .82f,
                        -actor.transform.forward);
                    Physics.SyncTransforms();
                }
                float stamina = actor.State.Stamina;
                float targetHealth = target.State.Health;
                Vector3 targetOrigin = target.transform.position;
                int contacts = 0;
                float contactElapsed = 0f, palmError = float.PositiveInfinity, speed = 0f, angularSpeed = 0f;
                CombatImpact contact = default;
                void RememberContact(CombatImpact impact)
                {
                    contacts++;
                    contact = impact;
                    contactElapsed = actor.State.ShoveElapsed;
                    palmError = Vector3.Distance(actor.ShovePalmPosition, impact.Point);
                    speed = Vector3.Dot(target.ImpactMotion.Velocity, impact.Direction);
                    angularSpeed = target.ImpactMotion.AngularVelocity.magnitude;
                }
                target.ImpactReceived += RememberContact;
                try
                {
                    bool started = scenario switch
                    {
                        1 => actor.TryAttack(), 2 => actor.RequestCharge(),
                        3 => actor.ReleaseCharge(), _ => actor.RequestAttack()
                    };
                    Assert.That(started, Is.True, context);
                    Assert.That(actor.State.Phase, Is.EqualTo(MeleePhase.Shoving), context);
                    Assert.That(actor.State.IsAttacking || actor.State.IsCharging, Is.False, context);
                    Assert.That(actor.State.Stamina, Is.EqualTo(stamina - actor.State.Settings.ShoveCost).Within(.001f), context);
                    for (int step = 0; step < 10; step++) root.Tick(CombatTestRoot.SimulationStep);
                    Assert.That(contacts, Is.Zero, context + ": the palm cannot hit before the short tell.");
                    for (int step = 0; step < 14 && contacts == 0; step++) root.Tick(CombatTestRoot.SimulationStep);
                    Vector3 palm = actor.ShovePalmPosition;
                    Vector3 chest = target.Ragdoll.PhysicsController.ChestBody.transform.position;
                    Vector3 axis = Vector3.ProjectOnPlane(target.transform.position - actor.transform.position, Vector3.up).normalized;
                    if (target.Hurtboxes.ChestSurface(actor.transform.position - actor.transform.right * .12f,
                        axis, out var chestSurface)) chest = chestSurface.Point;
                    string measurement = $"{context}: contacts={contacts}, contact={contactElapsed:F5}s, " +
                        $"palmError={palmError:F4}m, speed={speed:F3}m/s, angular={angularSpeed:F3}rad/s, " +
                        $"palm={palm:F4}, chest={chest:F4}, currentGap={Vector3.Distance(palm, chest):F4}m";
                    TestContext.Out.WriteLine(measurement);
                    Assert.That(contacts, Is.EqualTo(1), "The actual palm must reach the chest. " + measurement);
                    Assert.That(contactElapsed, Is.InRange(actor.State.Settings.ShoveContactSeconds - .00001f,
                        actor.State.Settings.ShoveContactSeconds + CombatActor.ShoveContactWindowSeconds + .00001f), context);
                    Assert.That(contactElapsed, Is.LessThan(actor.State.Settings.WindupSeconds * .3f), context);
                    Assert.That(palmError, Is.LessThanOrEqualTo(.08f), context + ": visible hand and impact agree.");
                    Assert.That(contact.Source, Is.SameAs(actor), context);
                    Assert.That(contact.Target, Is.SameAs(target), context);
                    Assert.That(contact.Result, Is.EqualTo(MeleeHitResult.Hit), context);
                    Assert.That(contact.Damage, Is.Zero, context);
                    Assert.That(contact.Impulse.magnitude, Is.EqualTo(CombatActor.ShoveImpulse).Within(.01f), context);
                    Assert.That(speed, Is.GreaterThan(2f), context + ": momentum must reach the existing physical response.");
                    Assert.That(angularSpeed, Is.GreaterThan(.1f), context + ": the chest impact must rock the model.");
                    Assert.That(target.State.Phase, Is.EqualTo(MeleePhase.Stagger), context);
                    Assert.That(target.State.Health, Is.EqualTo(targetHealth), context);
                    Assert.That(root.BloodEffects.EmissionCount, Is.Zero, context);
                    Assert.That(root.BloodEffects.WoundCountFor(target), Is.Zero, context);
                    if (scenario == 0 || scenario == 4)
                        CaptureDuelFrame("shove/" + (actor.IsHero ? "hero" : "opponent"), "contact");

                    float separation = 0f;
                    for (int frame = 0; frame < 30; frame++)
                    {
                        root.Tick(TickSeconds);
                        separation = Mathf.Max(separation, Vector3.Dot(target.transform.position - targetOrigin, contact.Direction));
                        if (frame == 12 && (scenario == 0 || scenario == 4))
                            CaptureDuelFrame("shove/" + (actor.IsHero ? "hero" : "opponent"), "response");
                        yield return null;
                    }
                    Assert.That(contacts, Is.EqualTo(1), context + ": one push cannot repeatedly hit a nearby body.");
                    Assert.That(target.ReceivedImpactCount, Is.EqualTo(1), context);
                    Assert.That(separation, Is.GreaterThan(.08f), context + ": the opponent must physically yield space.");
                    Assert.That(actor.State.Phase, Is.EqualTo(MeleePhase.Ready), context);
                    Assert.That(target.State.Health, Is.EqualTo(targetHealth), context);
                    Assert.That(root.BloodEffects.EmissionCount, Is.Zero, context);
                }
                finally { target.ImpactReceived -= RememberContact; }
            }
            // The same focused selection also exercises automatic decisions at the
            // closest body spacing and with the NPC's usual retreat blocked by a wall.
            yield return Range_CrowdingRestoresGripAndAttack();
            LogAssert.NoUnexpectedReceived();
        }

        [UnityTest]
        public IEnumerator Range_ShoveWindowRequiresOneRealContact()
        {
            var pending = new System.Collections.Generic.List<CombatActor.ShoveContact>();
            for (int scenario = 0; scenario < 5; scenario++)
            {
                root.SetSparring(false);
                PlacePair(.82f);
                for (int warmup = 0; warmup < 6; warmup++) { root.Tick(TickSeconds); yield return null; }
                CombatActor source = scenario % 2 == 0 ? root.Hero : root.Opponent;
                CombatActor target = source == root.Hero ? root.Opponent : root.Hero;
                Transform hand = System.Array.Find(source.DamageRigRoot.GetComponentsInChildren<Transform>(true), bone => bone.name == "hand.L");
                Assert.That(hand, Is.Not.Null);
                float contactAt = source.State.Settings.ShoveContactSeconds;
                float expiresAt = contactAt + CombatActor.ShoveContactWindowSeconds;
                float health = target.State.Health, impactAt = -1f;
                int hits = 0;
                bool missed = false;
                GameObject wall = null;
                void Hit(CombatImpact impact) { hits++; impactAt = source.State.ShoveElapsed; }
                target.ImpactReceived += Hit;
                try
                {
                    Assert.That(source.RequestAttack(), Is.True);
                    for (int step = 0; step < 52; step++)
                    {
                        source.AdvanceSimulation(CombatTestRoot.SimulationStep);
                        target.AdvanceSimulation(CombatTestRoot.SimulationStep);
                        source.Present(); target.Present();
                        source.CaptureContactPose(); target.CaptureContactPose();
                        float elapsed = source.State.ShoveElapsed;
                        bool eligible = source.State.IsShoving && elapsed >= contactAt;
                        // Reproduce a real posed-palm miss independently of root
                        // distance; the following presentation restores the rig.
                        bool forceGap = scenario < 4 && eligible &&
                            (scenario < 2 ? !missed : elapsed <= expiresAt + CombatTestRoot.SimulationStep);
                        if (forceGap) { hand.position += Vector3.up; missed = true; }
                        if (scenario == 4 && eligible && wall == null)
                        {
                            wall = new GameObject("Shove contact obstruction");
                            wall.transform.position = (source.transform.position + target.transform.position) * .5f + Vector3.up;
                            wall.AddComponent<BoxCollider>().size = new Vector3(2f, 2f, .03f);
                            Physics.SyncTransforms();
                        }
                        pending.Clear();
                        source.CollectShoveContacts(pending);
                        source.CollectShoveContacts(pending);
                        Assert.That(pending.Count, Is.LessThanOrEqualTo(1), "Repeated collection cannot duplicate an impulse.");
                        if (forceGap) Assert.That(pending, Is.Empty, "An out-of-reach palm cannot push.");
                        foreach (CombatActor.ShoveContact contact in pending) { contact.Apply(); contact.Apply(); }
                        if (wall != null) { wall.GetComponent<Collider>().enabled = false; Physics.SyncTransforms(); }
                    }
                    Assert.That(hits, Is.EqualTo(scenario < 2 ? 1 : 0), "scenario=" + scenario);
                    Assert.That(target.ReceivedImpactCount, Is.EqualTo(hits));
                    Assert.That(target.State.Health, Is.EqualTo(health));
                    Assert.That(source.State.IsShoving, Is.False);
                    if (scenario < 2) Assert.That(impactAt, Is.InRange(contactAt + .001f, expiresAt),
                        "A later real contact must still work after the first palm miss.");
                }
                finally
                {
                    target.ImpactReceived -= Hit;
                    if (wall != null) Object.Destroy(wall);
                }
                yield return null;
            }
            LogAssert.NoUnexpectedReceived();
        }

        [UnityTest]
        public IEnumerator Range_CrowdingRestoresGripAndAttack()
        {
            for (int scenario = 0; scenario < 3; scenario++)
            {
                root.SetSparring(true);
                float distance = scenario == 1 ? .84f : .72f;
                PlacePair(distance);
                Vector3 heroOrigin = root.Hero.transform.position;
                GameObject wall = null;
                try
                {
                    if (scenario == 2)
                    {
                        wall = new GameObject("Crowded duel rear wall");
                        wall.transform.position = new Vector3(0f, 1.2f, 1.3f);
                        wall.AddComponent<BoxCollider>().size = new Vector3(3f, 2.4f, .1f);
                        Physics.SyncTransforms();
                    }
                    bool shoved = false, pushed = false, recovered = false;
                    for (int frame = 0; frame < 240 && !recovered; frame++)
                    {
                        root.Tick(TickSeconds);
                        shoved |= root.Opponent.State.IsShoving;
                        if (!shoved)
                            Assert.That(root.Opponent.State.IsAttacking, Is.False,
                                "The first offensive action at touching distance must be a shove.");
                        if (root.Hero.ReceivedImpactCount > 0)
                        {
                            pushed = true;
                            Assert.That(root.Hero.LastImpact.Damage, Is.Zero, "A crowded shove is not a weapon hit.");
                        }
                        recovered = pushed && root.Opponent.State.Phase == MeleePhase.Ready && root.Opponent.HasTwoHandSupport;
                        yield return null;
                    }
                    string context = $"scenario={scenario}, hero={root.Hero.SupportArmState}, " +
                        $"opponent={root.Opponent.SupportArmState}/{root.Opponent.State.Phase}, " +
                        $"gap={Vector3.Distance(root.Hero.transform.position, root.Opponent.transform.position):F3}, " +
                        $"blocked={root.Opponent.WeaponBlockingShape}";
                    Assert.That(shoved && pushed, Is.True, "Crowding must produce a physical shove: " + context);
                    Assert.That(recovered, Is.True, "The pushing hand must return to its weapon after the shove: " + context);
                    Assert.That(root.Hero.State.Health, Is.EqualTo(root.Hero.State.Settings.MaxHealth), context);
                    Assert.That(root.BloodEffects.EmissionCount, Is.Zero, context);
                    Assert.That(Vector3.Dot(root.Hero.transform.position - heroOrigin, root.Hero.LastImpact.Direction),
                        Is.GreaterThan(.08f), "The shove must move the body even if it loses balance and falls: " + context);
                    if (scenario == 0) CaptureDuelFrame("crowding", "recovered");
                }
                finally
                {
                    if (wall != null) Object.Destroy(wall);
                }
                yield return null;
            }

            // A shove may put the close opponent on the floor. Ordinary weapon
            // behavior is checked separately with both fighters upright and apart.
            root.SetSparring(false);
            PlacePair(1.4f);
            Assert.That(root.Opponent.RequestAttack(), Is.True);
            Assert.That(root.Opponent.State.IsAttacking, Is.True, "Outside shove range the supported opponent swings normally.");
            LogAssert.NoUnexpectedReceived();
        }

        [System.Serializable]
        private sealed class DuelReadRecord
        {
            public string @event;
            public long seq;
            public DuelReadData data;
        }

        [System.Serializable]
        private sealed class DuelReadData
        {
            public int actor, action, request, round, frame;
            public long tick, dropped_records;
            public double duel_seconds, late_pose_ms, update_to_late_ms;
            public double frame_ms, late_to_next_update_ms, present_ms, weapon_constraint_ms, support_grip_ms;
            public int present_calls, weapon_constraint_calls, support_grip_calls, region, side, target_phase_after;
            public double target_phase_before;
            public long impact_seq;
            public bool active, late_observer_captured, is_critical, is_finisher;
            public string result, reason, phase, code_revision, animation_asset_revision;
            public string code_identity_source, animation_identity_source, workspace_state;
            public string hero_animation_revision, npc_animation_revision;
        }

        [UnityTest]
        public IEnumerator Range_DuelJournalRecordsReasonsWithoutChangingCombat()
        {
            string folder = System.IO.Path.GetFullPath(System.IO.Path.Combine("TestResults", "duel-journal-" + System.Guid.NewGuid().ToString("N")));
            Vector3 expectedPosition = default;
            int expectedSteps = 0;
            DuelJournal journal = null;
            var collector = new double[120];
            var allocated = new long[2, 120];
            using var gc = Unity.Profiling.ProfilerRecorder.StartNew(Unity.Profiling.ProfilerCategory.Memory, "GC Allocated In Frame", 1);
            for (int mode = 0; mode < 2; mode++)
            {
                root.SetDuelLogging(false);
                PlacePair(.82f);
                for (int warmup = 0; warmup < 6; warmup++) { root.Tick(TickSeconds); yield return null; }
                if (mode == 1)
                {
                    root.SetDuelLogging(true, folder);
                    journal = root.JournalForDiagnostics;
                    Assert.That(journal, Is.Not.Null);
                }
                // Match the observed regression: the opponent shoves the hero,
                // whose late presentation must not regrip between catch steps.
                CombatActor victim = root.Hero;
                bool pausedBetweenSteps = false;
                Assert.That(root.Opponent.RequestAttack(), Is.True);
                Assert.That(root.Opponent.RequestAttack(), Is.False, "A second command during a shove is rejected in both runs.");
                for (int frame = 0; frame < collector.Length; frame++)
                {
                    root.Tick(TickSeconds);
                    yield return null;
                    allocated[mode, frame] = gc.Valid && gc.Count > 0 ? gc.LastValue : -1;
                    if (mode == 1) collector[frame] = root.JournalCollectorMilliseconds;
                    if (victim.Footwork.RecoveryEpisodeActive)
                    {
                        Assert.That(victim.SupportGrip.JournalRegripAllowed, Is.False, "Recovery owns the hand through the planning gap.");
                        Assert.That(victim.SupportGrip.IsRegripping, Is.False);
                    }
                    if (!pausedBetweenSteps && victim.ImpactMotion.LandedRecoverySteps == 1 &&
                        victim.Footwork.CatchStepCount == 1 && !victim.Footwork.CatchStepActive && victim.Footwork.RecoveryEpisodeActive)
                    {
                        pausedBetweenSteps = true;
                        long tick = journal?.Tick ?? 0;
                        double seconds = journal?.DuelSeconds ?? 0;
                        Assert.That(root.PauseMenu.Open(), Is.True);
                        for (int pause = 0; pause < 3; pause++) { root.Tick(TickSeconds); yield return null; }
                        root.Tick(0f); victim.Present(); victim.Present();
                        Assert.That(victim.Footwork.RecoveryEpisodeActive, Is.True);
                        Assert.That(victim.Footwork.CatchStepCount, Is.EqualTo(1));
                        Assert.That(victim.ImpactMotion.LandedRecoverySteps, Is.EqualTo(1));
                        if (mode == 1)
                        {
                            Assert.That(journal.Tick, Is.EqualTo(tick));
                            Assert.That(journal.DuelSeconds, Is.EqualTo(seconds));
                        }
                        Assert.That(root.PauseMenu.Cancel(), Is.True);
                        for (int resume = 0; resume < 6 && !GameInput.CanRead(GameInputContext.Gameplay); resume++) yield return null;
                        Assert.That(GameInput.CanRead(GameInputContext.Gameplay), Is.True);
                    }
                }
                Assert.That(pausedBetweenSteps, Is.True, "This regression must exercise the gap between two real landings.");
                Assert.That(victim.ReceivedImpactCount, Is.EqualTo(1));
                Assert.That(victim.State.Health, Is.EqualTo(victim.State.Settings.MaxHealth));
                Assert.That(victim.IsKnockedDown, Is.False);
                Assert.That(victim.HasTwoHandSupport, Is.True);
                Assert.That(victim.Footwork.RecoveryEpisodeActive, Is.False);
                Assert.That(victim.Footwork.CatchStepCount, Is.EqualTo(2));
                if (mode == 0)
                {
                    expectedPosition = victim.transform.position;
                    expectedSteps = victim.ImpactMotion.LandedRecoverySteps;
                }
                else
                {
                    Assert.That(Vector3.Distance(victim.transform.position, expectedPosition), Is.LessThan(.002f));
                    Assert.That(victim.ImpactMotion.LandedRecoverySteps, Is.EqualTo(expectedSteps));
                    Assert.That(GameDiagnosticsSnapshot.Capture("journal-test"), Is.True, "F8's shared route marks the duel even with main logging off.");
                    root.TickFrame(.5f);
                    root.ResetRound();
                    Assert.That(victim.Footwork.RecoveryEpisodeActive, Is.False);
                    yield return null;
                    Assert.That(root.Hero.RequestCharge(), Is.True);
                    root.Opponent.State.ReceiveHit(1000f, 0f, false);
                    root.AutomaticSimulation = true;
                    yield return null;
                    Assert.That(root.RoundFinished, Is.True);
                    Assert.That(root.Hero.State.IsCharging || root.Hero.State.HasBufferedCharge, Is.False,
                        "Round completion still cancels a real held charge.");
                    int request = root.Hero.JournalRequestId;
                    for (int idle = 0; idle < 5; idle++) yield return null;
                    Assert.That(root.Hero.JournalRequestId, Is.EqualTo(request), "Finished rounds must not emit empty charge cancellations every frame.");
                    root.AutomaticSimulation = false;
                    root.SetDuelLogging(false);
                }
            }
            float deadline = Time.realtimeSinceStartup + 5f;
            while (!journal.Completion.IsCompleted && Time.realtimeSinceStartup < deadline) yield return null;
            Assert.That(journal.Completion.IsCompleted, Is.True);
            Assert.That(journal.LastError, Is.Null);
            Assert.That(journal.DroppedRecords, Is.Zero);
            string[] logs = System.IO.Directory.GetFiles(folder, "duel.ndjson", System.IO.SearchOption.AllDirectories);
            Assert.That(logs.Length, Is.EqualTo(2), "Reset closes the old round and starts a distinct journal.");
            bool rejected = false, contact = false, paused = false, marked = false, discarded = false, state = false, frameTiming = false, latePose = false;
            bool identity = false, anatomy = false, delivery = false, heroWork = false, opponentWork = false;
            double frameInterval = 0d, updateToLate = 0d;
            long sequence = 0;
            int recoveryStarts = 0, recoveryEnds = 0;
            System.Array.Sort(logs, System.StringComparer.Ordinal);
            foreach (string log in logs)
            {
                string[] lines = System.IO.File.ReadAllLines(log);
                Assert.That(JsonUtility.FromJson<DuelReadRecord>(lines[0]).@event, Is.EqualTo("round_begin"));
                Assert.That(JsonUtility.FromJson<DuelReadRecord>(lines[lines.Length - 1]).@event, Is.EqualTo("round_end"));
                foreach (string line in lines)
                {
                    DuelReadRecord entry = JsonUtility.FromJson<DuelReadRecord>(line);
                    Assert.That(entry.seq, Is.GreaterThan(sequence)); sequence = entry.seq;
                    Assert.That(entry.data, Is.Not.Null);
                    if (entry.@event == "round_begin")
                        StringAssert.StartsWith("modules:", entry.data.code_revision);
                    if (entry.@event == "revision_identity")
                    {
                        identity = true;
                        Assert.That(entry.data.code_identity_source, Is.EqualTo("editor_compiled_modules"));
                        Assert.That(entry.data.animation_identity_source, Is.EqualTo("editor_asset_dependency_hash"));
                        Assert.That(entry.data.workspace_state, Is.EqualTo("clean").Or.EqualTo("dirty").Or.EqualTo("unavailable"));
                        StringAssert.StartsWith("dependency:", entry.data.hero_animation_revision);
                        StringAssert.StartsWith("dependency:", entry.data.npc_animation_revision);
                    }
                    if (entry.@event == "actor_configuration")
                        StringAssert.StartsWith("dependency:", entry.data.animation_asset_revision);
                    if (entry.@event == "command_result" && entry.data.result == "rejected")
                    {
                        rejected = true;
                        Assert.That(entry.data.reason, Is.Not.Null.And.Not.Empty);
                        Assert.That(entry.data.request, Is.GreaterThan(0));
                    }
                    contact |= entry.@event == "impact_applied";
                    if (entry.@event == "impact_anatomy")
                    {
                        anatomy = true;
                        Assert.That(entry.data.impact_seq, Is.GreaterThan(0));
                        Assert.That(entry.data.region, Is.InRange((int)MeleeBodyRegion.Torso, (int)MeleeBodyRegion.RightLeg));
                        Assert.That(entry.data.side, Is.InRange((int)MeleeHitSide.Front, (int)MeleeHitSide.Bottom));
                        Assert.That(entry.data.is_finisher || entry.data.is_critical, Is.False, "This fixed shove does no HP damage.");
                        StringAssert.DoesNotContain("\"target_phase_before\":null", line);
                        Assert.That(entry.data.target_phase_before, Is.EqualTo((double)(int)MeleePhase.Ready));
                        Assert.That(entry.data.target_phase_after, Is.EqualTo((int)MeleePhase.Stagger));
                    }
                    paused |= entry.@event == "input_gate";
                    marked |= entry.@event == "mark";
                    discarded |= entry.@event == "time_discarded";
                    state |= entry.@event == "state";
                    frameTiming |= entry.@event == "frame";
                    if (entry.@event == "frame") frameInterval = entry.data.frame_ms;
                    if (entry.@event == "recovery" && entry.data.actor == 1 && entry.data.round == 1)
                    {
                        if (entry.data.active) recoveryStarts++; else recoveryEnds++;
                    }
                    if (entry.@event == "frame_detail")
                    {
                        StringAssert.Contains("\"latest_present_wait_ms\":", line);
                        StringAssert.Contains("\"latest_timing_repeat_frames\":", line);
                        updateToLate = entry.data.update_to_late_ms;
                        latePose |= entry.data.late_pose_ms > 0d && entry.data.update_to_late_ms >= entry.data.late_pose_ms;
                    }
                    if (entry.@event == "frame_delivery" && entry.data.late_observer_captured)
                    {
                        delivery = true;
                        Assert.That(entry.data.late_to_next_update_ms, Is.GreaterThanOrEqualTo(0d));
                        Assert.That(updateToLate + entry.data.late_to_next_update_ms, Is.EqualTo(frameInterval).Within(.001d));
                    }
                    if (entry.@event == "pose_work")
                    {
                        Assert.That(entry.data.present_ms, Is.GreaterThanOrEqualTo(0d));
                        Assert.That(entry.data.weapon_constraint_ms, Is.GreaterThanOrEqualTo(0d));
                        Assert.That(entry.data.support_grip_ms, Is.GreaterThanOrEqualTo(0d));
                        bool measured = entry.data.present_calls > 0 && entry.data.weapon_constraint_calls > 0 && entry.data.support_grip_calls > 0 &&
                            entry.data.present_ms > 0d && entry.data.weapon_constraint_ms > 0d && entry.data.support_grip_ms > 0d;
                        heroWork |= entry.data.actor == 1 && measured;
                        opponentWork |= entry.data.actor == 2 && measured;
                    }
                }
                string summaryPath = System.IO.Path.Combine(System.IO.Path.GetDirectoryName(log), "summary.txt");
                Assert.That(System.IO.File.Exists(summaryPath), Is.True);
                string summary = System.IO.File.ReadAllText(summaryPath);
                int milestonesAt = summary.IndexOf("Last major events:", System.StringComparison.Ordinal);
                Assert.That(milestonesAt, Is.GreaterThanOrEqualTo(0));
                string milestones = summary.Substring(milestonesAt);
                StringAssert.DoesNotContain("frame_delivery", milestones, "Routine timing cannot replace the bounded combat milestone history.");
                StringAssert.DoesNotContain("pose_work", milestones, "Per-actor work samples belong in NDJSON and event counts.");
            }
            Assert.That(rejected && contact && paused && marked && discarded && state && frameTiming && latePose, Is.True,
                $"Required evidence: denied={rejected}, impact={contact}, pause={paused}, mark={marked}, discarded={discarded}, state={state}, frame={frameTiming}, latePose={latePose}");
            Assert.That(identity && anatomy && delivery && heroWork && opponentWork, Is.True,
                $"Extended evidence: identity={identity}, anatomy={anatomy}, delivery={delivery}, heroWork={heroWork}, opponentWork={opponentWork}");
            Assert.That(recoveryStarts, Is.EqualTo(1), "Both catch steps belong to one recovery episode.");
            Assert.That(recoveryEnds, Is.EqualTo(1));
            System.Array.Sort(collector);
            double p95 = collector[113];
            TestContext.Out.WriteLine($"Duel journal collection p95={p95:F4}ms/frame; identical seeded shove result with logging off/on. Logs: {folder}");
            for (int mode = 0; mode < 2; mode++)
            {
                var bytes = new long[allocated.GetLength(1) - 12];
                for (int i = 0; i < bytes.Length; i++) bytes[i] = allocated[mode, i + 12];
                System.Array.Sort(bytes);
                TestContext.Out.WriteLine($"Journal {(mode == 0 ? "off" : "on")}: observed GC bytes/frame p50={bytes[bytes.Length / 2]}, p95={bytes[(int)(bytes.Length * .95)]}; Editor timing, not isolated worker allocations.");
            }
            Assert.That(p95, Is.LessThanOrEqualTo(.2d), "Collector budget excludes worker serialization and disk IO.");
            LogAssert.NoUnexpectedReceived();
        }

        [UnityTest, Explicit("Bounded shove comparison; writes review frames and measures Editor frames separately.")]
        public IEnumerator Range_MinimumShoveRecoveryComparison()
        {
            Camera camera = root.CameraFollow.Camera;
            bool followEnabled = root.CameraFollow.enabled;
            RenderTexture previousTarget = camera.targetTexture;
            RenderTexture previousActive = RenderTexture.active;
            var target = new RenderTexture(1280, 720, 24);
            var pixels = new Texture2D(1280, 720, TextureFormat.RGB24, false);
            string output = System.IO.Path.GetFullPath("Captures/CombatTest/minimum-recovery");
            var report = new System.Text.StringBuilder("Same 0.82m shove, AI off, 1280x720 camera.\n" +
                "Videos: 30 simulated frames/s. Timing: separate run, no readback or file writes.\n" +
                "Timing includes Editor frame scheduling, LateUpdate and rendering; not standalone player FPS.\n");
            float minimumRecovery = float.PositiveInfinity;
            bool minimumControls = false, minimumFell = false;
            int minimumSteps = 0, minimumLandings = 0;
            try
            {
                root.CameraFollow.enabled = false;
                camera.targetTexture = target;
                Vector3 focus = new Vector3(0f, .95f, .65f);
                Vector3 eye = focus + new Vector3(-3.2f, .6f, -.4f);
                camera.transform.SetPositionAndRotation(eye, Quaternion.LookRotation(focus - eye));
                for (int profile = 0; profile < 2; profile++)
                {
                    bool experimental = profile == 0;
                    string label = experimental ? "before" : "minimum";
                    string folder = System.IO.Path.Combine(output, label);
                    System.IO.Directory.CreateDirectory(folder);
                    PlacePair(.82f);
                    camera.transform.SetPositionAndRotation(eye, Quaternion.LookRotation(focus - eye));
                    root.Hero.ImpactMotion.ExperimentalRecovery = root.Opponent.ImpactMotion.ExperimentalRecovery = experimental;
                    Time.captureDeltaTime = 1f / 30f;
                    for (int warmup = 0; warmup < 12; warmup++) { root.Tick(1f / 30f); yield return null; }
                    float health = root.Opponent.State.Health;
                    float recoveredAt = float.PositiveInfinity;
                    Assert.That(root.Hero.RequestAttack(), Is.True);
                    Assert.That(root.Hero.State.Phase, Is.EqualTo(MeleePhase.Shoving));
                    for (int frame = 0; frame < 90; frame++)
                    {
                        root.Tick(1f / 30f);
                        CombatActor victim = root.Opponent;
                        if (victim.ReceivedImpactCount > 0 && !victim.IsKnockedDown &&
                            !victim.ImpactMotion.RecoveryInProgress && !victim.Footwork.CatchStepActive &&
                            victim.ImpactMotion.Velocity.magnitude < .1f && victim.HasTwoHandSupport)
                            recoveredAt = Mathf.Min(recoveredAt, (frame + 1) / 30f);
                        camera.Render();
                        RenderTexture.active = target;
                        pixels.ReadPixels(new Rect(0, 0, 1280, 720), 0, 0);
                        pixels.Apply(false, false);
                        System.IO.File.WriteAllBytes(System.IO.Path.Combine(folder, $"{frame:D4}.jpg"), pixels.EncodeToJPG(90));
                        RenderTexture.active = previousActive;
                        yield return null;
                    }
                    Assert.That(root.Opponent.ReceivedImpactCount, Is.EqualTo(1), label);
                    Assert.That(root.Opponent.State.Health, Is.EqualTo(health), label);
                    Assert.That(root.BloodEffects.EmissionCount, Is.Zero, label);
                    report.AppendLine($"{label}: recovery={recoveredAt:F3}s, " + DescribeBalance(root.Opponent));
                    if (!experimental)
                    {
                        minimumRecovery = recoveredAt;
                        minimumSteps = root.Opponent.Footwork.CatchStepCount;
                        minimumLandings = root.Opponent.ImpactMotion.LandedRecoverySteps;
                        minimumFell = root.Opponent.IsKnockedDown;
                        Assert.That(root.Opponent.ImpactMotion.HandSupportSeconds, Is.Zero);
                        Assert.That(root.Opponent.SupportGrip.IsBalanceReaching, Is.False);
                        minimumControls = root.Opponent.TryAttack();
                    }

                    // Replay the same input without capture IO; wall-clock the entire
                    // yielded frames, not just CombatTestRoot.Tick or synthetic deltaTime.
                    PlacePair(.82f);
                    camera.transform.SetPositionAndRotation(eye, Quaternion.LookRotation(focus - eye));
                    Time.captureDeltaTime = TickSeconds;
                    for (int warmup = 0; warmup < 20; warmup++) { root.Tick(TickSeconds); yield return null; }
                    Assert.That(root.Hero.RequestAttack(), Is.True);
                    var frames = new double[120];
                    var wallClock = new Stopwatch();
                    for (int frame = 0; frame < frames.Length; frame++)
                    {
                        wallClock.Restart();
                        root.Tick(TickSeconds);
                        yield return null;
                        frames[frame] = wallClock.Elapsed.TotalMilliseconds;
                    }
                    System.Array.Sort(frames);
                    double total = 0d;
                    foreach (double elapsed in frames) total += elapsed;
                    report.AppendLine($"{label}: Editor mean={total / frames.Length:F3}ms, " +
                        $"p95={frames[113]:F3}ms, max={frames[119]:F3}ms, resolution=1280x720");
                }
            }
            finally
            {
                RenderTexture.active = previousActive;
                camera.targetTexture = previousTarget;
                root.CameraFollow.enabled = followEnabled;
                root.Hero.ImpactMotion.ExperimentalRecovery = root.Opponent.ImpactMotion.ExperimentalRecovery = false;
                Time.captureDeltaTime = TickSeconds;
                target.Release(); Object.Destroy(target); Object.Destroy(pixels);
                System.IO.Directory.CreateDirectory(output);
                System.IO.File.WriteAllText(System.IO.Path.Combine(output, "comparison.txt"), report.ToString());
                TestContext.Out.WriteLine(report.ToString());
            }
            Assert.That(minimumFell, Is.False, "This one moderate shove must recover to stance.");
            Assert.That(minimumSteps, Is.InRange(1, 2));
            Assert.That(minimumLandings, Is.EqualTo(minimumSteps), "Only presented, grounded feet count as completed rescues.");
            Assert.That(minimumRecovery, Is.LessThanOrEqualTo(2f));
            Assert.That(minimumControls, Is.True, "The recovered actor can attack again.");
            LogAssert.NoUnexpectedReceived();
        }

        [UnityTest]
        public IEnumerator Range_BalanceRecoveryUsesActualSupports()
        {
            // The deferred wall/flywheel prototype remains an explicit diagnostic.
            root.Hero.ImpactMotion.ExperimentalRecovery = root.Opponent.ImpactMotion.ExperimentalRecovery = true;
            var watch = new Stopwatch();
            int timedTicks = 0, directionsWithLanding = 0;
            bool multipleSteps = false, pausedDuringCatch = false;
            Vector3 firstTarget = Vector3.zero;
            float targetVariation = 0f;
            for (int rig = 0; rig < 2; rig++)
            for (int directionIndex = 0; directionIndex < 4; directionIndex++)
            {
                PlacePair(4f);
                CombatActor victim = rig == 0 ? root.Hero : root.Opponent;
                for (int frame = 0; frame < 3; frame++) { root.Tick(TickSeconds); yield return null; }
                Vector3 direction = directionIndex switch
                {
                    0 => victim.transform.forward, 1 => -victim.transform.forward,
                    2 => victim.transform.right, _ => -victim.transform.right
                };
                string context = $"rig={rig}, direction={directionIndex}";
                float health = victim.State.Health;
                Vector3 origin = victim.transform.position;
                Quaternion facing = victim.transform.rotation;
                TestContext.Out.WriteLine(context + " neutral: " + DescribeBalanceGeometry(victim));
                ApplyBalanceImpulse(victim, direction, 165f);
                int landings = 0;
                bool reducedMomentum = false, recovered = false, capturedStep = false;
                for (int frame = 0; frame < 240 && !recovered; frame++)
                {
                    watch.Start(); root.Tick(TickSeconds); watch.Stop(); timedTicks++;
                    var motion = victim.ImpactMotion;
                    Assert.That(victim.IsKnockedDown, Is.False,
                        context + ": a moderate reachable catch must get its landing before a fall. " + DescribeBalance(victim));
                    if (motion.LandedRecoverySteps > landings)
                    {
                        landings = motion.LandedRecoverySteps;
                        TestContext.Out.WriteLine(context + $" landing={landings}: " + DescribeBalanceGeometry(victim));
                        Assert.That(motion.LastLandingSpeedAfter, Is.LessThanOrEqualTo(motion.LastLandingSpeedBefore), context);
                        reducedMomentum |= motion.LastLandingSpeedBefore > .01f &&
                            motion.LastLandingSpeedAfter < motion.LastLandingSpeedBefore * .85f;
                    }
                    if (victim.Footwork.CatchStepActive)
                    {
                        if (!capturedStep && directionIndex == 0 && victim.Footwork.CatchStepProgress >= .4f)
                        {
                            CaptureDuelFrame("balance/" + (rig == 0 ? "hero" : "opponent"), "catch");
                            capturedStep = true;
                        }
                        if (!pausedDuringCatch)
                        {
                            int attempts = victim.Footwork.CatchStepCount;
                            int sequence = motion.RecoverySequence;
                            Vector3 velocity = motion.Velocity;
                            float supportSeconds = motion.HandSupportSeconds;
                            root.Tick(0f);
                            for (int sample = 0; sample < 3; sample++) victim.Present();
                            Assert.That(victim.Footwork.CatchStepCount, Is.EqualTo(attempts));
                            Assert.That(motion.LandedRecoverySteps, Is.EqualTo(landings));
                            Assert.That(motion.RecoverySequence, Is.EqualTo(sequence));
                            Assert.That(motion.HandSupportSeconds, Is.EqualTo(supportSeconds));
                            Assert.That(Vector3.Distance(motion.Velocity, velocity), Is.LessThan(.000001f),
                                "A zero clock and repeated presentation cannot advance recovery.");
                            pausedDuringCatch = true;
                        }
                    }
                    multipleSteps |= victim.Footwork.CatchStepCount >= 2;
                    recovered = landings > 0 && frame > 45 && !victim.Footwork.CatchStepActive &&
                        motion.BalanceLoad < .35f && motion.Velocity.magnitude < .1f && victim.HasTwoHandSupport;
                    yield return null;
                }
                TestContext.Out.WriteLine(context + ": " + DescribeBalance(victim));
                Assert.That(recovered && reducedMomentum, Is.True,
                    context + ": actual landings must absorb momentum, settle and restore the supporting grip. " + DescribeBalance(victim));
                Assert.That(victim.State.Health, Is.EqualTo(health), context + ": balance never spends anatomical HP.");
                Assert.That(victim.Footwork.CatchLandingCount, Is.EqualTo(victim.ImpactMotion.LandedRecoverySteps), context);
                directionsWithLanding++;
                Vector3 localTarget = Quaternion.Inverse(facing) * (victim.Footwork.LastCatchTarget - origin);
                if (rig == 0 && directionIndex == 0) firstTarget = localTarget;
                targetVariation = Mathf.Max(targetVariation, Vector3.Distance(firstTarget, localTarget));
                if (directionIndex == 0) CaptureDuelFrame("balance/" + (rig == 0 ? "hero" : "opponent"), "saved");
            }
            Assert.That(directionsWithLanding, Is.EqualTo(8));
            Assert.That(multipleSteps && pausedDuringCatch, Is.True, "Recovery includes a continuing catch sequence and a clock-free preview.");
            Assert.That(targetVariation, Is.GreaterThan(.12f), "Different fall directions must produce different reachable foot placements.");

            // Insert an obstruction after takeoff. The planned target is now
            // invalid even though its original planning query was clear.
            PlacePair(4f);
            CombatActor blocked = root.Hero;
            ApplyBalanceImpulse(blocked, blocked.transform.forward, 165f);
            for (int step = 0; step < 20 && !blocked.Footwork.CatchStepActive; step++) root.Tick(CombatTestRoot.SimulationStep);
            Assert.That(blocked.Footwork.CatchStepActive, Is.True, "The obstruction scenario needs a committed catch.");
            GameObject obstacle = new GameObject("Recovery foot obstruction");
            try
            {
                Vector3 rejectedTarget = blocked.Footwork.LastCatchTarget;
                obstacle.transform.position = rejectedTarget + Vector3.up * .08f;
                obstacle.AddComponent<BoxCollider>().size = new Vector3(.18f, .16f, .18f);
                Physics.SyncTransforms();
                bool rejected = false;
                for (int frame = 0; frame < 60 && !rejected; frame++)
                {
                    root.Tick(TickSeconds);
                    Assert.That(blocked.ImpactMotion.LandedRecoverySteps, Is.Zero,
                        "A blocked or clipped target cannot absorb momentum as a landed step.");
                    rejected = blocked.Footwork.BlockedCatchCount > 0;
                    yield return null;
                }
                Assert.That(rejected, Is.True, "The new obstruction must invalidate the landing. " + DescribeBalance(blocked));
                for (int frame = 0; frame < 30 && !blocked.IsKnockedDown && blocked.Footwork.CatchStepCount < 2; frame++)
                { root.Tick(TickSeconds); yield return null; }
                Assert.That(blocked.IsKnockedDown || blocked.Footwork.CatchStepCount >= 2, Is.True,
                    "After rejection the actor must try another valid step or yield to the fall.");
                if (!blocked.IsKnockedDown)
                    Assert.That(Vector3.Distance(blocked.Footwork.LastCatchTarget, rejectedTarget), Is.GreaterThan(.08f),
                        "A replacement step cannot retry the occupied landing point.");
            }
            finally { Object.Destroy(obstacle); }
            yield return null;

            // A nearby wall is useful only once the real free palm reaches it.
            // Try the bounded moderate impulse range, requiring at least one real catch.
            bool heldWall = false;
            var wallWatch = new Stopwatch();
            int wallTicks = 0;
            for (int attempt = 0; attempt < 3 && !heldWall; attempt++)
            {
                PlacePair(4f);
                CombatActor victim = root.Hero;
                GameObject wall = GameObject.CreatePrimitive(PrimitiveType.Cube);
                wall.name = "Recovery hand support wall";
                try
                {
                    wall.transform.position = victim.transform.position - victim.transform.right * .5f + Vector3.up * 1.2f;
                    wall.transform.localScale = new Vector3(.1f, 2.4f, 3f);
                    BoxCollider surface = wall.GetComponent<BoxCollider>();
                    Physics.SyncTransforms();
                    ApplyBalanceImpulse(victim, -victim.transform.right, 100f + attempt * 32.5f);
                    for (int frame = 0; frame < 100 && !victim.IsKnockedDown && !heldWall; frame++)
                    {
                        wallWatch.Start(); root.Tick(TickSeconds); wallWatch.Stop(); wallTicks++;
                        if (frame % 10 == 0)
                            TestContext.Out.WriteLine($"wall attempt={attempt}, frame={frame}: " + victim.SupportGrip.BalanceDiagnostics);
                        if (frame == 10)
                        {
                            Vector3 palmBefore = victim.SupportGrip.ShovePalmPosition;
                            for (int sample = 0; sample < 3; sample++) victim.Present();
                            Assert.That(Vector3.Distance(victim.SupportGrip.ShovePalmPosition, palmBefore), Is.LessThan(.001f),
                                "Repeated presentation cannot advance the approaching arm.");
                        }
                        if (attempt == 0 && frame == 12) CaptureDuelFrame("balance/hero", "wall-reach");
                        if (victim.ImpactMotion.HasHandSupport)
                        {
                            Assert.That(victim.SupportGrip.HasBalanceHandContact, Is.True);
                            Vector3 point = victim.SupportGrip.BalanceHandPoint;
                            Assert.That(Vector3.Distance(victim.SupportGrip.ShovePalmPosition, point), Is.LessThanOrEqualTo(.048f),
                                "Support requires the live palm at the wall, not just an IK target.");
                            Assert.That(Vector3.Distance(surface.ClosestPoint(point), point), Is.LessThan(.005f));
                            Assert.That(victim.ImpactMotion.HandSupportSeconds, Is.GreaterThan(0f));
                            heldWall = true;
                            CaptureDuelFrame("balance/hero", "wall-support");
                            float supportedSeconds = victim.ImpactMotion.HandSupportSeconds;
                            wall.transform.position += victim.transform.right * 3f;
                            Physics.SyncTransforms();
                            root.Tick(TickSeconds);
                            Assert.That(victim.ImpactMotion.HasHandSupport, Is.False, "A moved wall supplies no cached pressure.");
                            Assert.That(victim.ImpactMotion.HandSupportSeconds, Is.EqualTo(supportedSeconds));
                        }
                        yield return null;
                    }
                    TestContext.Out.WriteLine($"wall impulse={100f + attempt * 32.5f}: " + DescribeBalance(victim));
                }
                finally { Object.Destroy(wall); }
                yield return null;
            }
            Assert.That(heldWall, Is.True, "The reachable wall must produce measured palm support in at least one moderate response.");
            double wallAverage = wallWatch.Elapsed.TotalMilliseconds / wallTicks;
            TestContext.Out.WriteLine($"Wall recovery CPU: {wallAverage:F3} ms/tick; excludes rendering.");
            Assert.That(wallAverage, Is.LessThan(12d), "Hand surface and reach queries must remain bounded.");

            for (int rig = 0; rig < 2; rig++)
            {
                PlacePair(4f);
                CombatActor victim = rig == 0 ? root.Hero : root.Opponent;
                float health = victim.State.Health;
                for (int frame = 0; frame < 90 && !victim.IsKnockedDown; frame++)
                {
                    if (frame % 5 == 0) ApplyBalanceImpulse(victim, -victim.transform.forward, 300f);
                    root.Tick(TickSeconds);
                    yield return null;
                }
                Assert.That(victim.IsKnockedDown, Is.True,
                    "Repeated strong impulses must overwhelm finite recovery attempts. " + DescribeBalance(victim));
                Assert.That(victim.State.Health, Is.EqualTo(health));
            }
            PlacePair(4f);
            foreach (CombatActor actor in new[] { root.Hero, root.Opponent })
            {
                Assert.That(actor.ImpactMotion.RecoverySequence, Is.Zero);
                Assert.That(actor.ImpactMotion.LandedRecoverySteps, Is.Zero);
                Assert.That(actor.ImpactMotion.BlockedRecoverySteps, Is.Zero);
                Assert.That(actor.ImpactMotion.HandSupportSeconds, Is.Zero);
                Assert.That(actor.ImpactMotion.RecoveryStepActive || actor.ImpactMotion.HasHandSupport, Is.False);
                Assert.That(actor.Footwork.CatchStepCount + actor.Footwork.CatchLandingCount + actor.Footwork.BlockedCatchCount, Is.Zero);
            }
            double average = watch.Elapsed.TotalMilliseconds / timedTicks;
            TestContext.Out.WriteLine($"Combat recovery CPU: {average:F3} ms/tick; excludes LateUpdate and rendering.");
            Assert.That(average, Is.LessThan(12d), "Recovery planning and support queries must leave frame headroom.");
            LogAssert.NoUnexpectedReceived();
        }

        private void ApplyBalanceImpulse(CombatActor victim, Vector3 direction, float momentum)
        {
            CombatActor source = victim == root.Hero ? root.Opponent : root.Hero;
            direction.Normalize();
            Vector3 point = victim.Ragdoll.PhysicsController.ChestBody.position - direction * .10f;
            var impact = new CombatImpact(source, victim, victim.ImpactMotion.RecoverySequence + 1,
                point, -direction, direction, victim.State.Health, victim.State.Health,
                MeleeHitResult.Hit, new MeleeHitLocation(MeleeBodyRegion.Torso, MeleeHitSide.Front),
                .6f, Player3DAnatomicalPart.Torso, Vector3.zero, 3f, direction * momentum);
            victim.ApplyImpactForDiagnostics(impact);
        }

        private static string DescribeBalance(CombatActor actor) =>
            $"steps={actor.Footwork.CatchStepCount}, landed={actor.ImpactMotion.LandedRecoverySteps}, " +
            $"blocked={actor.Footwork.BlockedCatchCount}, active={actor.Footwork.CatchStepActive}, " +
            $"load={actor.ImpactMotion.BalanceLoad:F3}, speed={actor.ImpactMotion.Velocity.magnitude:F3}, " +
            $"grip={actor.SupportArmState}, handSeconds={actor.ImpactMotion.HandSupportSeconds:F3}, knocked={actor.IsKnockedDown}, " +
            actor.Footwork.SupportDiagnostics;

        private static string DescribeBalanceGeometry(CombatActor actor) =>
            $"root={actor.transform.position:F3}, com={actor.ImpactMotion.CentreOfMass:F3}, " +
            $"support={actor.ImpactMotion.SupportCentre:F3}, capture={actor.ImpactMotion.CaptureOffset:F3}, " +
            $"target={actor.Footwork.LastCatchTarget:F3}, side={actor.Footwork.LastCatchSide}, " +
            $"age={actor.ImpactMotion.Age:F3}, load={actor.ImpactMotion.BalanceLoad:F3}, " +
            actor.Footwork.SupportDiagnostics;

        private void CaptureDuelFrame(string subject, string name)
        {
            Camera camera = root.CameraFollow.Camera;
            Vector3 position = camera.transform.position;
            Quaternion rotation = camera.transform.rotation;
            try
            {
                Vector3 focus = (root.Hero.transform.position + root.Opponent.transform.position) * .5f + Vector3.up;
                Vector3 eye = focus + new Vector3(-3.2f, .6f, -.4f);
                if (subject.StartsWith("balance/"))
                {
                    CombatActor victim = subject.Contains("opponent") ? root.Opponent : root.Hero;
                    focus = victim.transform.position + Vector3.up * .9f;
                    eye = focus - victim.transform.right * 3f + Vector3.up * .5f + victim.transform.forward * .4f;
                }
                if (name.StartsWith("wall")) eye = focus + new Vector3(2.2f, .6f, 2.4f);
                camera.transform.SetPositionAndRotation(eye, Quaternion.LookRotation(focus - eye));
                string folder = SceneIds.CombatTest + "/" + subject;
                string path = System.IO.Path.Combine(System.IO.Directory.GetCurrentDirectory(), "Captures", folder, name + ".png");
                LogAssert.Expect(LogType.Log, "Area capture wrote " + path);
                AreaCaptureFixture.CaptureCurrentCamera(camera, folder, name);
            }
            finally { camera.transform.SetPositionAndRotation(position, rotation); }
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            if (root != null) root.AutomaticSimulation = false;
            RetroAudioService.Instance?.StopAll();
            Scene active = SceneManager.GetActiveScene();
            if (active.IsValid() && active.name == SceneIds.CombatTest)
            {
                Scene cleanup = SceneManager.CreateScene("Combat Performance Cleanup");
                SceneManager.SetActiveScene(cleanup);
                foreach (AudioListener listener in Object.FindObjectsByType<AudioListener>())
                    if (listener.gameObject.scene == active) listener.enabled = false;
                EnsureListener();
                AsyncOperation unload = SceneManager.UnloadSceneAsync(active);
                if (unload != null) yield return unload;
            }
            GameSessionState.BeginNewGame();
            Time.captureDeltaTime = previousCaptureDelta;
            root = null;
            yield return null;
        }

        private static void EnsureListener()
        {
            foreach (AudioListener listener in Object.FindObjectsByType<AudioListener>())
                if (listener.isActiveAndEnabled) return;
            new GameObject("Combat Performance Audio Listener").AddComponent<AudioListener>();
        }
    }
}

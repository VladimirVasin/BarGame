using System;
using System.Collections.Generic;
using UnityEngine;

namespace BarPromenade
{
    /// <summary>One finite crowd, one pickup source and one enemy simulation owner. Hero input stays in its shared adapter.</summary>
    [DefaultExecutionOrder(-40)]
    [DisallowMultipleComponent]
    public sealed class StairwellEncounterController : MonoBehaviour
    {
        private sealed class Enemy
        {
            internal StairwellEnemyPlacement Placement;
            internal GameObject Root, Passive;
            internal CapsuleCollider RestCollider;
            internal VillageResidentPresentation Visual;
            internal Transform[] Bones;
            internal Vector3[] Positions, Scales;
            internal Quaternion[] Rotations;
            internal CombatActor Actor;
            internal bool Standing, Mobile, DefeatRecorded, CuePlayed;
            internal float NextPose, NextRoute, DefeatAge;
            internal Vector3 Waypoint, AttackDirection;
        }

        private StairwellInteriorRoot scene;
        private Transform space;
        private PlayerCrowbarCombatController combat;
        private StairwellWalkableArea walkable;
        private StairwellEncounterNavigation navigation;
        private Enemy[] enemies;
        private readonly List<CombatActor> actors = new List<CombatActor>(StairwellEncounterState.Population);
        private readonly List<CombatActor> participants = new List<CombatActor>(StairwellEncounterState.Population + 1);
        private RaycastHit[] queries = new RaycastHit[16];
        private readonly AudioSource[] wakeVoices = new AudioSource[2];
        private CombatActor hero;
        private float nextSelection, attackDelay, hurtGrace, previousHealth, retryDelay;
        private int generation;
        private bool sourceCollected, frozen, heroFrozen;
        public StairwellEncounterPlan Plan { get; private set; }
        public StairwellEncounterState State => GameSessionState.StairwellEncounter;
        public IReadOnlyList<CombatActor> ActiveActors => actors;
        public int MobileCount { get; private set; }
        public int CommittedAttackCount { get; private set; }
        public CombatActor GetActor(int index) => enemies[index].Actor;
        public Vector3 GetEnemyPosition(int index) => enemies[index].Root.transform.position;

        public void Initialize(StairwellInteriorRoot root)
        {
            scene = root; space = scene.World.Root;
            CombatZombieAssetProvider.PrepareOrdinaryAssets();
            Plan = new StairwellEncounterPlan(root.Layout.Labyrinth);
            navigation = new StairwellEncounterNavigation(Plan);
            walkable = new StairwellWalkableArea(root.Layout, space, GameSessionState.StairwellLabyrinthPuzzles);
            combat = root.Player.GameObject.GetComponent<PlayerCrowbarCombatController>();
            for (int i = 0; i < wakeVoices.Length; i++)
            {
                var voiceRoot = new GameObject("Stairwell Body Rise " + i);
                voiceRoot.transform.SetParent(transform, false);
                var voice = voiceRoot.AddComponent<AudioSource>();
                voice.playOnAwake = voice.loop = false;
                CityWorkAudio.Configure(voice, true, 7200f, 18f, false);
                voice.minDistance = .9f; voice.volume = .56f;
                wakeVoices[i] = voice;
            }
            generation = State.Generation;
            CreatePopulation();
            RefreshSource();
            GameSessionState.WorldItemsChanged += RefreshSource;
        }

        private void RefreshSource() => sourceCollected = GameSessionState.IsWorldItemCollected(StairwellLabyrinthPuzzles.CrowbarSourceId);

        private void CreatePopulation()
        {
            enemies = new Enemy[Plan.Enemies.Count];
            for (int i = 0; i < enemies.Length; i++)
            {
                StairwellEnemyPlacement placement = Plan.Enemies[i];
                var root = new GameObject("Stairwell Body " + i);
                root.transform.SetParent(space, false);
                root.transform.localPosition = placement.Position;
                root.transform.localRotation = Quaternion.LookRotation(placement.Facing);
                var enemy = new Enemy { Placement = placement, Root = root };
                enemies[i] = enemy;
                if (State.IsDefeated(i)) { enemy.DefeatRecorded = true; root.SetActive(false); continue; }
                enemy.Standing = State.Awakened && State.Elapsed >= StairwellEncounterState.WakeDelay(i) + CombatZombieAssetProvider.OrdinaryRiseSeconds;
                enemy.Passive = CombatZombieAssetProvider.CreateOrdinaryPassive(root.transform, placement.Variant, enemy.Standing);
                enemy.RestCollider = root.AddComponent<CapsuleCollider>();
                enemy.RestCollider.radius = placement.Variant == 2 ? .46f : .38f;
                SetRestCollider(enemy, enemy.Standing);
            }
        }

        private static void SetRestCollider(Enemy enemy, bool standing)
        {
            float height = enemy.Placement.Variant == 2 ? (standing ? 2.02f : 1.62f) : (standing ? 1.90f : 1.20f);
            enemy.RestCollider.height = height;
            enemy.RestCollider.center = Vector3.up * (height * .5f);
        }

        private bool ControlsReady => scene.Player.Motor.InputEnabled && scene.Player.Interactor.InputEnabled &&
            !scene.AnimatedInteraction.IsActive && GameCursor.ApplicationFocused &&
            !SceneTransitionService.IsTransitioning && GameInput.CanRead(GameInputContext.Gameplay) &&
            !WorldItemFoundScreen.For(scene.Player.Interactor).IsPresenting;

        private void Update()
        {
            if (scene == null || !scene.IsInitialized || enemies == null) return;
            if (generation != State.Generation)
            {
                // New Game invalidates this scene's ownership; a local retry rebuilds explicitly below.
                ClearHeroRoster(); SetFrozen(true); return;
            }
            bool blocked = GameTimeScaleRuntime.IsPaused || PauseMenuController.IsAnyPaused ||
                !GameCursor.ApplicationFocused || SceneTransitionService.IsTransitioning || !GameInput.CanRead(GameInputContext.Gameplay);
            SetFrozen(blocked);
            if (blocked) return;
            if (hero == null && combat.Actor != null)
            {
                hero = combat.Actor; previousHealth = hero.State.Health;
                participants.Insert(0, hero); hero.SetCombatParticipants(participants);
            }
            if (State.TryAwaken(sourceCollected, hero != null && ControlsReady))
            { hurtGrace = 3.6f; nextSelection = 0f; }
            if (!State.Awakened) return;
            float seconds = Mathf.Min(Time.deltaTime, .1f);
            State.Advance(Time.deltaTime);
            Vector3 localHero = space.InverseTransformPoint(scene.Player.GameObject.transform.position);
            if (!State.Completed && localHero.y > 14f && scene.Layout.Labyrinth.StageAt(localHero) == 3 &&
                walkable.Contains(scene.Player.GameObject.transform.position, .25f)) State.Complete();
            if (hero != null && hero.State.IsDefeated && !State.Completed)
            {
                CancelAttacks(); retryDelay += seconds;
                if (retryDelay >= .8f) RetryFromRoom();
                return;
            }
            if (hero != null && hero.State.Health < previousHealth)
            { hurtGrace = 1.3f; CancelAttacks(); }
            if (hero != null) previousHealth = hero.State.Health;
            hurtGrace = Mathf.Max(0f, hurtGrace - seconds);
            attackDelay = Mathf.Max(0f, attackDelay - seconds);
            bool sameFloor = localHero.y >= StairwellEncounterPlan.Floor - .3f && localHero.y <= StairwellEncounterPlan.Floor + 2.25f;
            Vector3 navigationTarget = localHero;
            navigationTarget.y = StairwellEncounterPlan.Floor + PlayerFactory.GroundedRootOffset;
            bool inCombatArea = sameFloor && Plan.Contains(navigationTarget) && !State.Completed && ControlsReady;
            if (!State.FirstContactPassed && inCombatArea)
            {
                CombatActor first = enemies[0].Actor;
                bool firstExchangeFinished = first != null && (first.State.IsDefeated ||
                    (first.ReceivedImpactCount > 0 && first.State.Phase == MeleePhase.Ready) ||
                    ((first.State.AttackOutcome is MeleeAttackOutcome.Hit or MeleeAttackOutcome.Parried) &&
                    first.State.Phase == MeleePhase.Recovery && first.State.AttackElapsed >= first.State.AttackActiveEnd + .95f));
                if (firstExchangeFinished || localHero.x > 6f) State.FinishIntroduction();
            }
            if (Time.time >= nextSelection)
            {
                nextSelection = Time.time + .2f;
                SelectMobile(navigationTarget, inCombatArea);
            }
            if (!inCombatArea) CancelAttacks();
            CommittedAttackCount = 0;
            foreach (Enemy enemy in enemies)
                if (enemy.Actor != null && IsCommitted(enemy.Actor)) CommittedAttackCount++;
            foreach (Enemy enemy in enemies)
            {
                if (enemy.DefeatRecorded && enemy.Actor == null) continue;
                float distance = Vector3.Distance(localHero, enemy.Root.transform.localPosition);
                float wake = State.Elapsed - StairwellEncounterState.WakeDelay(enemy.Placement.Id);
                if (!enemy.CuePlayed && wake >= 0f)
                {
                    enemy.CuePlayed = true;
                    if (wake < .25f && distance <= 18f)
                        foreach (AudioSource voice in wakeVoices)
                            if (!voice.isPlaying)
                            {
                                voice.transform.position = enemy.Root.transform.position + Vector3.up * .9f;
                                voice.clip = CombatZombieAssetProvider.LoadAudio(CombatZombieAudio.OrdinaryRiseAudioName(enemy.Placement.Variant)); voice.Play(); break;
                            }
                }
                if (enemy.Actor == null)
                {
                    AdvancePresentation(enemy, wake, distance);
                    if (hero != null && wake >= 0f && (enemy.Mobile || distance < 3.2f) && !State.Completed)
                    {
                        // The handoff already sampled this frame's authoritative wake time.
                        Promote(enemy, wake);
                        continue;
                    }
                }
                if (enemy.Actor == null) continue;
                CombatActor actor = enemy.Actor;
                if (actor.State.IsDefeated)
                {
                    if (!enemy.DefeatRecorded) { State.MarkDefeated(enemy.Placement.Id); enemy.DefeatRecorded = true; }
                    enemy.Mobile = false; enemy.DefeatAge += seconds;
                    actor.Step(seconds);
                    continue;
                }
                actor.SetPresentationFrozen(false);
                if (enemy.Mobile && inCombatArea && actor.ZombieAwake) AdvanceEnemy(enemy, seconds, navigationTarget);
                else { actor.SetZombieWalking(false); actor.SetLocomotion(0f); actor.SetNavigationFacing(Vector3.zero); }
                // Distant presentation can sleep; authoritative recovery still advances.
                if (distance <= 24f || actor.State.Phase != MeleePhase.Ready || actor.IsKnockedDown || actor.IsRagdollActive)
                    actor.Step(seconds);
                else { actor.State.Advance(seconds, false); actor.SetPresentationFrozen(true); }
                actor.CaptureContactPose();
            }
            LimitCorpsePhysics();
            // The hero adapter steps after this owner. Its real sweep sees all nearby bodies, including waiting ones.
            if (hero != null) hero.SetContactTarget(null);
        }

        private void SelectMobile(Vector3 localHero, bool allowed)
        {
            foreach (Enemy enemy in enemies) enemy.Mobile = false;
            MobileCount = 0;
            if (!allowed) return;
            int limit = State.FirstContactPassed ? StairwellEncounterState.MaximumMobile : 1;
            for (int slot = 0; slot < limit; slot++)
            {
                Enemy best = null; float score = 12f * 12f;
                foreach (Enemy enemy in enemies)
                {
                    if (enemy.Mobile || enemy.DefeatRecorded || State.Elapsed < StairwellEncounterState.WakeDelay(enemy.Placement.Id)) continue;
                    float candidate = (enemy.Root.transform.localPosition - localHero).sqrMagnitude;
                    if (candidate >= score) continue;
                    if (!navigation.TryWaypoint(enemy.Root.transform.localPosition, localHero, out _)) continue;
                    score = candidate; best = enemy;
                }
                if (best == null) break;
                best.Mobile = true; MobileCount++;
            }
        }

        private void EnsureVisual(Enemy enemy)
        {
            if (enemy.Visual != null) return;
            enemy.Visual = DefaultNpcFactory.CreateForCharacter(enemy.Root.transform, DefaultNpcPopulation.StairwellEnemyId(enemy.Placement.Id));
            CombatZombieAssetProvider.ApplyOrdinaryAppearance(enemy.Visual.gameObject, enemy.Placement.Variant);
            enemy.Visual.ReleaseAnimation();
            enemy.Visual.Animator.enabled = false;
            enemy.Bones = enemy.Visual.Animator.GetComponentsInChildren<Transform>(true);
            int count = enemy.Bones.Length;
            enemy.Positions = new Vector3[count]; enemy.Rotations = new Quaternion[count]; enemy.Scales = new Vector3[count];
            for (int i = 0; i < count; i++)
            {
                enemy.Bones[i].GetLocalPositionAndRotation(out enemy.Positions[i], out enemy.Rotations[i]);
                enemy.Scales[i] = enemy.Bones[i].localScale;
            }
            enemy.Passive.SetActive(false);
        }

        private void AdvancePresentation(Enemy enemy, float wake, float distance)
        {
            if (wake < 0f) return;
            bool risen = wake >= CombatZombieAssetProvider.OrdinaryRiseSeconds;
            if (distance > 18f && enemy.Visual == null)
            {
                if (risen && !enemy.Standing)
                {
                    Destroy(enemy.Passive);
                    enemy.Passive = CombatZombieAssetProvider.CreateOrdinaryPassive(enemy.Root.transform, enemy.Placement.Variant, true);
                    enemy.Standing = true; SetRestCollider(enemy, true);
                }
                return;
            }
            EnsureVisual(enemy);
            if (risen && !enemy.Standing) { enemy.Standing = true; SetRestCollider(enemy, true); }
            if (Time.time < enemy.NextPose) return;
            enemy.NextPose = Time.time + (distance > 12f ? .12f : 0f);
            AnimationClip clip = CombatZombieAssetProvider.LoadDegradedClip(risen ? CombatZombieAssetProvider.DegradedIdleClip :
                CombatZombieAssetProvider.OrdinaryRiseClip(enemy.Placement.Variant), enemy.Placement.Variant);
            clip.SampleAnimation(enemy.Visual.Animator.gameObject, risen ? (wake - CombatZombieAssetProvider.OrdinaryRiseSeconds) % clip.length : wake);
        }

        private void Promote(Enemy enemy, float wake)
        {
            EnsureVisual(enemy);
            // Combat caches true bind anatomy before sampling the same current visible pose.
            for (int i = 0; i < enemy.Bones.Length; i++)
            { enemy.Bones[i].SetLocalPositionAndRotation(enemy.Positions[i], enemy.Rotations[i]); enemy.Bones[i].localScale = enemy.Scales[i]; }
            enemy.RestCollider.enabled = false;
            var body = enemy.Root.AddComponent<CharacterController>();
            body.height = enemy.Placement.Variant == 2 ? 2.02f : 1.90f;
            body.radius = enemy.Placement.Variant == 2 ? .46f : .38f;
            body.center = Vector3.up * (body.height * .5f);
            body.skinWidth = PlayerFactory.GroundedRootOffset; body.stepOffset = PlayerFactory.StepOffset;
            body.slopeLimit = PlayerFactory.SlopeLimitDegrees; body.minMoveDistance = 0f;
            enemy.Visual.Animator.enabled = true;
            CombatActor actor = enemy.Root.AddComponent<CombatActor>();
            actor.InitializeOpponent(enemy.Visual, body, kind: CombatEnemyKind.Zombie);
            enemy.Actor = actor; actors.Add(actor); participants.Add(actor);
            actor.SetCombatParticipants(participants); actor.SetContactTarget(hero);
            if (wake < CombatZombieAssetProvider.OrdinaryRiseSeconds)
            {
                actor.SetOrdinaryZombieDormant(enemy.Placement.Variant); actor.TryAwakenZombie(playSound: !enemy.CuePlayed);
                actor.Step(Mathf.Max(0f, wake));
            }
            else actor.RestoreOrdinaryZombieIdlePhase(wake - CombatZombieAssetProvider.OrdinaryRiseSeconds);
            actor.CaptureContactPose();
        }

        private void AdvanceEnemy(Enemy enemy, float seconds, Vector3 target)
        {
            CombatActor actor = enemy.Actor;
            actor.SetLocomotion(0f); actor.SetZombieWalking(false);
            if (actor.IsKnockedDown || actor.IsRagdollActive || !actor.IsAvailable || hero == null || !hero.IsAvailable) return;
            Vector3 from = enemy.Root.transform.localPosition;
            Vector3 delta = target - from; delta.y = 0f;
            float distance = delta.magnitude;
            if (IsCommitted(actor))
            {
                float begin = Mathf.Max(actor.State.AttackElapsed, actor.State.AttackWindupSeconds);
                float end = Mathf.Min(actor.State.AttackElapsed + seconds, actor.State.AttackActiveEnd);
                if (end > begin && actor.State.AttackOutcome == MeleeAttackOutcome.None)
                    Move(enemy, enemy.AttackDirection * (.42f * (end - begin) / actor.State.Settings.ActiveSeconds), seconds);
                return;
            }
            if (actor.State.Phase != MeleePhase.Ready) return;
            if (distance <= 1.25f && ClearSight(actor.transform.position, hero.transform.position))
            {
                actor.SetNavigationFacing(space.TransformDirection(delta.normalized));
                int attackLimit = State.FirstContactPassed ? StairwellEncounterState.MaximumAttackers : 1;
                // This grab keeps its yaw through windup; align before committing its narrow hand sweep.
                if (hurtGrace <= 0f && attackDelay <= 0f && CommittedAttackCount < attackLimit && distance <= 1.25f &&
                    Mathf.Abs(hero.transform.position.y - actor.transform.position.y) < .45f &&
                    Vector3.Dot(actor.transform.forward, space.TransformDirection(delta.normalized)) > .995f && actor.TryZombieAttack())
                {
                    enemy.AttackDirection = delta.normalized; attackDelay = .85f; CommittedAttackCount++;
                }
                return;
            }
            if (Time.time >= enemy.NextRoute)
            {
                enemy.NextRoute = Time.time + .35f + enemy.Placement.Id * .003f;
                if (!navigation.TryWaypoint(from, target, out enemy.Waypoint)) return;
            }
            Vector3 direction = enemy.Waypoint - from; direction.y = 0f;
            if (direction.sqrMagnitude < .0025f) return;
            direction.Normalize(); actor.SetNavigationFacing(space.TransformDirection(direction));
            float speed = enemy.Placement.Variant == 2 ? .52f : enemy.Placement.Variant == 1 ? .68f : .75f;
            Vector3 travel = direction * Mathf.Min(speed * seconds, Vector3.Distance(from, enemy.Waypoint));
            Move(enemy, travel, seconds);
        }

        private void Move(Enemy enemy, Vector3 localTravel, float seconds)
        {
            CombatActor actor = enemy.Actor;
            Vector3 before = actor.transform.position;
            Vector3 desired = before + space.TransformDirection(localTravel);
            desired = walkable.Constrain(before, desired, actor.Body.radius + .02f);
            if (!Plan.Contains(space.InverseTransformPoint(desired), actor.Body.radius + .02f)) return;
            // Bodies wait for one another; no accumulated push can throw the hero off the landing.
            foreach (Enemy other in enemies)
            {
                if (other == enemy || other.DefeatRecorded || !other.Root.activeInHierarchy) continue;
                Vector3 separation = desired - other.Root.transform.position; separation.y = 0f;
                Vector3 old = before - other.Root.transform.position; old.y = 0f;
                float clearance = actor.Body.radius + other.RestCollider.radius + .06f;
                if (separation.sqrMagnitude < clearance * clearance && separation.sqrMagnitude < old.sqrMagnitude) return;
            }
            Vector3 heroGap = desired - hero.transform.position; heroGap.y = 0f;
            Vector3 oldHeroGap = before - hero.transform.position; oldHeroGap.y = 0f;
            float heroClearance = actor.Body.radius + hero.Body.radius + .02f;
            if (heroGap.sqrMagnitude < heroClearance * heroClearance && heroGap.sqrMagnitude < oldHeroGap.sqrMagnitude) return;
            actor.Body.Move(desired - before + Vector3.down * (2f * seconds));
            Vector3 achieved = actor.transform.position - before; achieved.y = 0f;
            if (actor.State.Phase == MeleePhase.Ready)
                actor.AdvanceZombieWalkClock(localTravel.magnitude / Mathf.Max(.0001f, actor.PreviewZombieWalkDistance(1f)),
                    localTravel.magnitude, achieved.magnitude);
            actor.SetLocomotion(achieved / Mathf.Max(.001f, seconds));
            actor.SetZombieWalking(achieved.sqrMagnitude > .0000001f);
        }

        private bool ClearSight(Vector3 from, Vector3 to)
        {
            from += Vector3.up * 1.1f; to += Vector3.up * 1.1f;
            Vector3 delta = to - from;
            var hits = CombatTraversalQueries.Raycast(from, delta.normalized, ref queries, delta.magnitude,
                Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore, out int count);
            for (int i = 0; i < count; i++)
                if (hits[i].collider != null && hits[i].collider.GetComponentInParent<CombatActor>() == null &&
                    !CombatCorpseTraversal.IsCorpse(hits[i].collider)) return false;
            return true;
        }

        private static bool IsCommitted(CombatActor actor) => actor.State.Phase is MeleePhase.Windup or MeleePhase.Active;
        private void CancelAttacks()
        {
            foreach (CombatActor actor in actors)
                if (!actor.State.IsDefeated) { if (IsCommitted(actor)) actor.State.CancelAction(); actor.SetZombieWalking(false); actor.SetLocomotion(0f); }
            CommittedAttackCount = 0;
        }

        private void LimitCorpsePhysics()
        {
            int live = 0;
            for (int i = enemies.Length - 1; i >= 0; i--)
            {
                Enemy enemy = enemies[i];
                if (!enemy.DefeatRecorded || enemy.Actor == null) continue;
                live++;
                if (live > 2 && enemy.Actor.Ragdoll.IsSettled) enemy.Actor.SetPresentationFrozen(true);
            }
        }

        private void RetryFromRoom()
        {
            State.Retry(); generation = State.Generation;
            combat.ResetForLocalRetry();
            ClearHeroRoster();
            foreach (Enemy enemy in enemies) { enemy.Root.SetActive(false); Destroy(enemy.Root); }
            actors.Clear(); participants.Clear();
            Vector3 position = space.TransformPoint(scene.LabyrinthPresentation.Rooms[1].InteriorPoint);
            hero.ResetActor(position, space.TransformDirection(Vector3.right));
            GameSessionState.ApplyStoryPistolState(hero);
            scene.Player.Motor.SetInputEnabled(true); scene.Player.Interactor.SetInputEnabled(true);
            scene.CameraFollow.Snap();
            participants.Add(hero); hero.SetCombatParticipants(participants);
            previousHealth = hero.State.Health; hurtGrace = 3.6f; retryDelay = attackDelay = nextSelection = 0f;
            MobileCount = CommittedAttackCount = 0;
            foreach (AudioSource voice in wakeVoices) voice.Stop();
            CreatePopulation();
        }

        private void SetFrozen(bool value)
        {
            if (frozen == value) return;
            frozen = value;
            foreach (CombatActor actor in actors) if (actor != null) actor.SetPresentationFrozen(value);
            if (hero != null) { hero.SetPresentationFrozen(value); heroFrozen = value; }
            foreach (AudioSource voice in wakeVoices)
                if (voice != null) { if (value) voice.Pause(); else voice.UnPause(); }
        }
        private void ClearHeroRoster()
        {
            if (hero == null) return;
            if (heroFrozen) { hero.SetPresentationFrozen(false); heroFrozen = false; }
            hero.SetContactTarget(null); hero.SetCombatParticipants(null);
        }
        private void OnEnable()
        { if (hero != null) hero.SetCombatParticipants(participants); }
        private void OnDisable() { CancelAttacks(); SetFrozen(true); ClearHeroRoster(); }
        private void OnDestroy()
        {
            GameSessionState.WorldItemsChanged -= RefreshSource; ClearHeroRoster();
            if (enemies != null)
                foreach (Enemy enemy in enemies) if (enemy.Root != null) Destroy(enemy.Root);
            foreach (AudioSource voice in wakeVoices) if (voice != null) Destroy(voice.gameObject);
        }
    }
}

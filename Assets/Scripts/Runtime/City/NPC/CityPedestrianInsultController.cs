using System;
using System.Collections.Generic;
using UnityEngine;

namespace BarPromenade
{
    /// <summary>
    /// Street walkers cursing the hero on the last drunkenness stage: one
    /// roaming body that has already turned its head toward him, within
    /// three metres and facing him, says one short line over its own head
    /// and walks on. One speaker at a time on the whole street, eight
    /// seconds between lines, and the same walker says nothing more until
    /// the hero has gone away and come back.
    ///
    /// The story bible's §6 registry row of 2026-09-05 lifts «Никогда не
    /// говорят» for exactly this and nothing else. §16.2 stays literally
    /// true: this component reads <see cref="GameSessionState.IntoxicationLevel"/>
    /// and never anything the hero says — his muttering is a presenter this
    /// class does not know exists. Placed after the pedestrian director's
    /// execution order so the positions and glances it reads are this
    /// frame's, and before the park quarrel so the shared view sees the
    /// street's one line before the park's.
    ///
    /// Speakers are declared ON DEMAND and withdrawn as soon as the line
    /// closes or the body goes back to the pool. The shared view holds
    /// eight speakers for the life of the City, the park pair own two of
    /// them for good, and thirteen pooled presentations could never all be
    /// declared; worse, a released presentation keeps its head bone alive,
    /// so the view's own sweep would never notice that a walker left and
    /// a bubble would go on hanging over the pool root.
    /// </summary>
    [DefaultExecutionOrder(315)]
    [DisallowMultipleComponent]
    public sealed class CityPedestrianInsultController : MonoBehaviour
    {
        public const string RuntimeObjectName = "Pedestrian Insult Controller";

        private IReadOnlyList<CityPedestrianActor> actors;
        private CityPedestrianPersonalSpaceController personalSpace;
        private Transform player;
        private NpcSpeechBubbleView bubbles;
        private bool[] encounterUsed = Array.Empty<bool>();
        private uint lineState;
        private int lastLineIndex = -1;
        private float cooldown;
        private CityPedestrianActor activeActor;
        private CityPedestrianPresentation activePresentation;

        public bool IsInitialized { get; private set; }

        /// <summary>The walker whose line is open, or <c>null</c>.</summary>
        public CityPedestrianActor ActiveActor => activeActor;

        /// <summary>The presentation the open line was declared on — the
        /// owner the shared view knows the speaker by.</summary>
        public CityPedestrianPresentation ActivePresentation => activePresentation;

        public string LastLineKey { get; private set; } = string.Empty;
        public int SpokenLineCount { get; private set; }
        public float CooldownRemaining => cooldown;

        /// <summary>
        /// The production entry: everything comes off the pedestrian
        /// director. Null when the director never came up or when there is
        /// no bubble view to speak through — the Home balcony street has
        /// walkers but no view, and gets no insults without a branch.
        /// </summary>
        public static CityPedestrianInsultController Create(
            Transform parent,
            CityPedestrianDirector director,
            Transform playerTransform,
            NpcSpeechBubbleView bubbleView,
            int citySeed)
        {
            if (director == null ||
                !director.IsInitialized ||
                director.PersonalSpace == null ||
                director.Actors == null)
            {
                return null;
            }

            return Create(
                parent,
                director.Actors,
                director.PersonalSpace,
                playerTransform,
                bubbleView,
                citySeed);
        }

        /// <summary>The same with the pieces handed over one by one, for
        /// a fixture that builds its own street.</summary>
        public static CityPedestrianInsultController Create(
            Transform parent,
            IReadOnlyList<CityPedestrianActor> routeActors,
            CityPedestrianPersonalSpaceController personalSpaceController,
            Transform playerTransform,
            NpcSpeechBubbleView bubbleView,
            int citySeed)
        {
            if (parent == null)
            {
                throw new ArgumentNullException(nameof(parent));
            }

            if (routeActors == null ||
                personalSpaceController == null ||
                playerTransform == null ||
                bubbleView == null)
            {
                return null;
            }

            var host = new GameObject(RuntimeObjectName);
            host.transform.SetParent(parent, false);
            var controller = host.AddComponent<CityPedestrianInsultController>();
            controller.actors = routeActors;
            controller.personalSpace = personalSpaceController;
            controller.player = playerTransform;
            controller.bubbles = bubbleView;
            controller.encounterUsed = new bool[routeActors.Count];
            controller.lineState = CityPedestrianInsultLines.CreateState(citySeed);
            controller.IsInitialized = true;
            return controller;
        }

        /// <summary>Whether this walker has had its say since the hero
        /// last came within reach.</summary>
        public bool IsEncounterUsed(CityPedestrianActor actor)
        {
            int index = IndexOf(actor);
            return index >= 0 && index < encounterUsed.Length && encounterUsed[index];
        }

        /// <summary>
        /// One frame. Public and deterministic in <paramref name="deltaTime"/>
        /// so a fixture can step it; the component calls it from
        /// <c>LateUpdate</c>.
        /// </summary>
        public void Advance(float deltaTime)
        {
            if (!IsInitialized ||
                float.IsNaN(deltaTime) ||
                float.IsInfinity(deltaTime) ||
                deltaTime <= 0f ||
                GameTimeScaleRuntime.IsPaused)
            {
                return;
            }

            if (bubbles == null || player == null || actors == null)
            {
                Disengage();
                return;
            }

            EnsureFlags();
            cooldown = Mathf.Max(0f, cooldown - deltaTime);
            Vector3 heroPosition = player.position;
            for (int index = 0; index < actors.Count; index++)
            {
                CityPedestrianActor actor = actors[index];
                if (actor == null ||
                    !actor.IsSpawned ||
                    CityPedestrianInsultRules.PlanarDistance(
                        actor.Position,
                        heroPosition) >
                    CityPedestrianInsultRules.ReleaseDistance)
                {
                    encounterUsed[index] = false;
                }
            }

            if (activeActor != null)
            {
                // One line at a time. It ends when the view takes the
                // bubble down, or sooner if the body under it was released
                // or handed to another slot — the presentation is the owner
                // the view knows, and it must not outlive the man.
                if (!activeActor.IsSpawned ||
                    activeActor.Presentation != activePresentation ||
                    activePresentation == null ||
                    !bubbles.IsShowing(activePresentation))
                {
                    Disengage();
                }

                return;
            }

            if (cooldown > 0f ||
                !CityPedestrianInsultRules.IsInsultStage(
                    GameSessionState.IntoxicationLevel) ||
                !personalSpace.IsHeroAvailable)
            {
                return;
            }

            TrySpeak(heroPosition);
        }

        /// <summary>
        /// Takes the street's own line down and gives its speaker slot
        /// back. Only this owner: the view is shared with the park, and a
        /// walker who finished has no business closing anybody else's
        /// line.
        /// </summary>
        public void Disengage()
        {
            if (activePresentation != null && bubbles != null)
            {
                bubbles.Dismiss(activePresentation);
                bubbles.WithdrawSpeaker(activePresentation);
            }

            activeActor = null;
            activePresentation = null;
        }

        public void Shutdown()
        {
            Disengage();
            IsInitialized = false;
        }

        private void LateUpdate()
        {
            Advance(Time.deltaTime);
        }

        private void OnDisable()
        {
            Disengage();
        }

        private void OnDestroy()
        {
            Shutdown();
        }

        private void TrySpeak(Vector3 heroPosition)
        {
            CityPedestrianActor nearest = null;
            int nearestIndex = -1;
            float bestDistance = float.PositiveInfinity;
            for (int index = 0; index < actors.Count; index++)
            {
                CityPedestrianActor actor = actors[index];
                if (actor == null ||
                    !actor.IsSpawned ||
                    !actor.isActiveAndEnabled ||
                    encounterUsed[index] ||
                    // Walking admits the walker mid-shove: the personal
                    // space holds its state, and he is facing the hero
                    // anyway. Riders, sitters and stop-waiters never speak.
                    actor.MotionState != CityPedestrianMotionState.Walking ||
                    !actor.IsAttending ||
                    !CityPedestrianInsultRules.MaySpeak(actor.DesignId))
                {
                    continue;
                }

                CityPedestrianPresentation presentation = actor.Presentation;
                if (presentation == null ||
                    presentation.IsSeated ||
                    presentation.Registry == null ||
                    presentation.Registry.HeadAnchor == null)
                {
                    continue;
                }

                if (!CityPedestrianPersonalSpaceRules.WithinReach(
                        actor.Position,
                        heroPosition,
                        CityPedestrianInsultRules.SpeakDistance) ||
                    !CityPedestrianInsultRules.IsFacing(
                        actor.transform.forward,
                        actor.Position,
                        heroPosition))
                {
                    continue;
                }

                Vector3 offset = heroPosition - actor.Position;
                offset.y = 0f;
                if (offset.sqrMagnitude >= bestDistance ||
                    !personalSpace.HasClearSightTo(actor))
                {
                    continue;
                }

                nearest = actor;
                nearestIndex = index;
                bestDistance = offset.sqrMagnitude;
            }

            if (nearest == null)
            {
                return;
            }

            CityPedestrianPresentation owner = nearest.Presentation;
            NpcSpeaker speaker = NpcSpeaker.FromRegistry(
                owner,
                owner.Registry,
                NpcEarshotProfile.Conversation);
            if (!bubbles.DeclareSpeaker(speaker))
            {
                // The view is full. Not this walker's fault: he keeps his
                // turn and the street tries again shortly.
                cooldown = CityPedestrianInsultRules.RetryDelaySeconds;
                return;
            }

            int lineIndex = CityPedestrianInsultLines.NextIndex(
                ref lineState,
                lastLineIndex);
            string key = CityPedestrianInsultLines.LineKeys[lineIndex];
            if (!bubbles.Show(owner, LocalizationService.Get(key)))
            {
                bubbles.WithdrawSpeaker(owner);
                cooldown = CityPedestrianInsultRules.RetryDelaySeconds;
                return;
            }

            lastLineIndex = lineIndex;
            encounterUsed[nearestIndex] = true;
            activeActor = nearest;
            activePresentation = owner;
            cooldown = CityPedestrianInsultRules.CooldownSeconds;
            LastLineKey = key;
            SpokenLineCount++;
        }

        private void EnsureFlags()
        {
            if (encounterUsed.Length >= actors.Count)
            {
                return;
            }

            var grown = new bool[actors.Count];
            Array.Copy(encounterUsed, grown, encounterUsed.Length);
            encounterUsed = grown;
        }

        private int IndexOf(CityPedestrianActor actor)
        {
            if (actor == null || actors == null)
            {
                return -1;
            }

            for (int index = 0; index < actors.Count; index++)
            {
                if (ReferenceEquals(actors[index], actor))
                {
                    return index;
                }
            }

            return -1;
        }
    }
}

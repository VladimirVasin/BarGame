using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

namespace BarPromenade
{
    public enum CocktailPresentationPhase
    {
        ChoosingBase = 0,
        Pouring,
        Mixing,
        Serving,
        RoundResult,
        FinalResult
    }

    [DisallowMultipleComponent]
    public sealed class CocktailMinigameController :
        MonoBehaviour,
        IBarMinigame
    {
        public const float BasePourDuration = 0.85f;
        public const float IngredientPourDuration = 0.72f;
        public const float ServingDuration = 1.05f;
        public const float RoundResultDuration = 1.45f;

        private static readonly CocktailBaseId[] bases =
        {
            CocktailBaseId.Beer,
            CocktailBaseId.Wine,
            CocktailBaseId.Vodka,
            CocktailBaseId.Cognac
        };

        private static readonly CocktailIngredientId[] noOffers =
            Array.Empty<CocktailIngredientId>();
        private static readonly CocktailIngredientId[] noIngredients =
            Array.Empty<CocktailIngredientId>();

        private CocktailMinigameView view;
        private IntoxicationHudView hud;
        private PlayerCameraFollow cameraFollow;
        private readonly BarMinigameModalLock modalLock =
            new BarMinigameModalLock();
        private CocktailMinigameSession session;
        private CocktailIngredientId[] offers = noOffers;
        private CocktailIngredientSelectionResult? lastSelection;
        private CocktailRoundResult? lastRound;
        private float phaseElapsed;
        private float phaseDuration;
        private int inputUnlockFrame;
        private bool serveAfterPour;
        private bool completionRaised;
        private bool persistSessionProgress = true;

        public bool IsOpen { get; private set; }
        public event Action Completed;
        public CocktailPresentationPhase PresentationPhase { get; private set; }
        public int HighlightedBaseIndex { get; private set; }
        public int HighlightedIngredientIndex { get; private set; }
        public CocktailIngredientId ActivePourIngredient { get; private set; }
        public string FeedbackKey { get; private set; } = string.Empty;
        public int FeedbackScore { get; private set; }

        public int BaseCount => bases.Length;
        public int OfferCount => offers.Length;
        public int RoundNumber =>
            (PresentationPhase ==
                 CocktailPresentationPhase.Serving ||
             PresentationPhase ==
                 CocktailPresentationPhase.RoundResult) &&
            lastRound.HasValue
                ? lastRound.Value.RoundNumber
                : session == null
                    ? 1
                    : session.CurrentRoundNumber;
        public int RoundsCompleted => session == null
            ? 0
            : session.RoundsCompleted;
        public int TotalScore => session == null
            ? 0
            : session.TotalScore;
        public int CurrentRoundScore
        {
            get
            {
                if (session != null &&
                    session.Phase == CocktailRoundPhase.Mixing)
                {
                    return session.CurrentRoundScore;
                }

                return PresentationPhase ==
                       CocktailPresentationPhase.Serving &&
                       lastRound.HasValue
                    ? lastRound.Value.Score
                    : 0;
            }
        }
        public int IntoxicationLevel => session == null
            ? GameSessionState.IntoxicationLevel
            : session.Intoxication;
        public int AdditionCount
        {
            get
            {
                if (session != null &&
                    session.Phase == CocktailRoundPhase.Mixing)
                {
                    return session.AdditionCount;
                }

                return PresentationPhase ==
                       CocktailPresentationPhase.Serving &&
                       lastRound.HasValue
                    ? Math.Max(
                        0,
                        lastRound.Value.Ingredients.Count - 1)
                    : 0;
            }
        }
        public bool CanServe => session != null &&
                                PresentationPhase ==
                                CocktailPresentationPhase.Mixing &&
                                session.CanServe;
        public bool HasLastRoundResult => lastRound.HasValue;
        public bool HasLastSelection => lastSelection.HasValue;
        public CocktailIngredientSelectionResult LastSelection =>
            lastSelection ?? default;
        public CocktailRoundResult LastRoundResult =>
            lastRound ?? default;
        public CocktailBaseId CurrentBase => session == null
            ? CocktailBaseId.None
            : session.CurrentBase;
        public CocktailBaseId DisplayBase => CurrentBase != CocktailBaseId.None
            ? CurrentBase
            : lastRound.HasValue
                ? lastRound.Value.BaseId
                : CocktailBaseId.None;
        public IReadOnlyList<CocktailIngredientId> DisplayIngredients
        {
            get
            {
                if (session != null &&
                    session.RoundIngredients.Count > 0)
                {
                    return session.RoundIngredients;
                }

                return lastRound.HasValue
                    ? lastRound.Value.Ingredients
                    : noIngredients;
            }
        }

        public float AnimationProgress => phaseDuration <= 0f
            ? 1f
            : Mathf.Clamp01(phaseElapsed / phaseDuration);

        public float VisualFillAmount
        {
            get
            {
                int ingredientCount = DisplayIngredients.Count;
                if (ingredientCount <= 0)
                {
                    return 0f;
                }

                float visibleCount = ingredientCount;
                if (PresentationPhase ==
                    CocktailPresentationPhase.Pouring)
                {
                    visibleCount -= 1f - AnimationProgress;
                }

                return Mathf.Clamp01(
                    visibleCount /
                    (CocktailMinigameSession.MaximumAdditions + 1f));
            }
        }

        public string FinalRankKey => GetRankKey(TotalScore);
        public bool ReachedMaxIntoxication =>
            session != null &&
            session.Outcome ==
                CocktailSessionOutcome.MaxIntoxicationReached;

        public void Initialize(
            CocktailMinigameView minigameView,
            IntoxicationHudView intoxicationHud,
            PlayerRuntime player,
            PlayerCameraFollow follow,
            bool persistProgress = true)
        {
            view = minigameView;
            hud = intoxicationHud;
            cameraFollow = follow;
            persistSessionProgress = persistProgress;
            view?.Initialize(this);
        }

        public bool Open(PlayerInteractor interactor)
        {
            if (IsOpen ||
                interactor == null ||
                SceneTransitionService.IsTransitioning)
            {
                return false;
            }

            CocktailMinigameSession newSession =
                new CocktailMinigameSession(
                    GameSessionState.CitySeed,
                    persistSessionProgress
                        ? GameSessionState.ActiveBarId
                        : "debug-cocktail",
                    persistSessionProgress
                        ? GameSessionState.IntoxicationLevel
                        : 0,
                    persistSessionProgress
                        ? GameSessionState.LastAlcoholicDrink
                        : DrinkId.None,
                    persistSessionProgress
                        ? GameSessionState.DrinksConsumed
                        : 0);
            if (!modalLock.TryCaptureAndDisable(
                    interactor,
                    cameraFollow,
                    hud))
            {
                return false;
            }

            session = newSession;

            offers = noOffers;
            lastSelection = null;
            lastRound = null;
            HighlightedBaseIndex = 0;
            HighlightedIngredientIndex = 0;
            ActivePourIngredient = CocktailIngredientId.None;
            FeedbackKey = string.Empty;
            FeedbackScore = 0;
            serveAfterPour = false;
            completionRaised = false;
            inputUnlockFrame = Time.frameCount + 1;
            IsOpen = true;

            if (session.IsFinished)
            {
                PresentationPhase =
                    CocktailPresentationPhase.FinalResult;
            }
            else
            {
                PresentationPhase =
                    CocktailPresentationPhase.ChoosingBase;
            }

            phaseElapsed = 0f;
            phaseDuration = 0f;
            RetroAudio.Play(RetroSfxId.UiConfirm);
            return true;
        }

        public bool ChooseBase(int index)
        {
            if (!IsOpen ||
                PresentationPhase !=
                CocktailPresentationPhase.ChoosingBase ||
                index < 0 ||
                index >= bases.Length)
            {
                return false;
            }

            HighlightedBaseIndex = index;
            CocktailBaseId baseId = bases[index];
            offers = session.BeginRound(baseId);
            HighlightedIngredientIndex = FindNextAvailableOffer(-1, 1);
            ActivePourIngredient = CocktailRules.GetBaseIngredient(baseId);
            FeedbackKey = string.Empty;
            FeedbackScore = 0;
            lastSelection = null;
            serveAfterPour = false;
            BeginPhase(
                CocktailPresentationPhase.Pouring,
                BasePourDuration);
            RetroAudio.Play(RetroSfxId.Pour);
            return true;
        }

        public bool AddIngredient(int index)
        {
            if (!IsOpen ||
                PresentationPhase !=
                CocktailPresentationPhase.Mixing ||
                index < 0 ||
                index >= offers.Length)
            {
                return false;
            }

            CocktailIngredientId ingredientId = offers[index];
            if (IsIngredientUsed(ingredientId))
            {
                return false;
            }

            HighlightedIngredientIndex = index;
            CocktailIngredientSelectionResult selection =
                session.AddIngredient(ingredientId);
            lastSelection = selection;
            ActivePourIngredient = ingredientId;
            serveAfterPour = selection.MustServe;
            FeedbackKey = selection.WasCompatible
                ? "cocktail.feedback.good"
                : "cocktail.feedback.bad";
            FeedbackScore = selection.WasCompatible
                ? Mathf.Max(0, selection.ScoreDelta)
                : CocktailMinigameSession.BadIngredientScorePenalty;
            HighlightedIngredientIndex =
                FindNextAvailableOffer(index, 1);
            BeginPhase(
                CocktailPresentationPhase.Pouring,
                IngredientPourDuration);
            RetroAudio.Play(RetroSfxId.Pour);
            return true;
        }

        public bool ServeCocktail()
        {
            if (!CanServe)
            {
                return false;
            }

            FeedbackKey = string.Empty;
            FeedbackScore = 0;
            ActivePourIngredient = CocktailIngredientId.None;
            BeginServing();
            return true;
        }

        public void MoveSelection(int direction)
        {
            if (!IsOpen || direction == 0)
            {
                return;
            }

            int normalizedDirection = direction < 0 ? -1 : 1;
            int previousIndex;
            int currentIndex;
            if (PresentationPhase ==
                CocktailPresentationPhase.ChoosingBase)
            {
                previousIndex = HighlightedBaseIndex;
                HighlightedBaseIndex = Wrap(
                    HighlightedBaseIndex + normalizedDirection,
                    bases.Length);
                currentIndex = HighlightedBaseIndex;
            }
            else if (PresentationPhase ==
                     CocktailPresentationPhase.Mixing &&
                     offers.Length > 0)
            {
                previousIndex = HighlightedIngredientIndex;
                HighlightedIngredientIndex = FindNextAvailableOffer(
                    HighlightedIngredientIndex,
                    normalizedDirection);
                currentIndex = HighlightedIngredientIndex;
            }
            else
            {
                return;
            }

            if (currentIndex != previousIndex)
            {
                RetroAudio.Play(RetroSfxId.UiMove);
            }
        }

        public CocktailBaseId GetBaseId(int index)
        {
            return index >= 0 && index < bases.Length
                ? bases[index]
                : CocktailBaseId.None;
        }

        public CocktailIngredientId GetOfferId(int index)
        {
            return index >= 0 && index < offers.Length
                ? offers[index]
                : CocktailIngredientId.None;
        }

        public string GetBaseLabel(int index)
        {
            CocktailBaseId baseId = GetBaseId(index);
            return baseId == CocktailBaseId.None
                ? string.Empty
                : GetIngredientLabel(
                    CocktailRules.GetBaseIngredient(baseId));
        }

        public string GetOfferLabel(int index)
        {
            CocktailIngredientId ingredientId = GetOfferId(index);
            return ingredientId == CocktailIngredientId.None
                ? string.Empty
                : GetIngredientLabel(ingredientId);
        }

        public bool IsIngredientUsed(CocktailIngredientId ingredientId)
        {
            if (session == null ||
                ingredientId == CocktailIngredientId.None)
            {
                return false;
            }

            IReadOnlyList<CocktailIngredientId> ingredients =
                session.RoundIngredients;
            for (int index = 0; index < ingredients.Count; index++)
            {
                if (ingredients[index] == ingredientId)
                {
                    return true;
                }
            }

            return false;
        }

        public Color GetLiquidColor()
        {
            switch (DisplayBase)
            {
                case CocktailBaseId.Beer:
                    return new Color(0.94f, 0.58f, 0.12f, 0.92f);
                case CocktailBaseId.Wine:
                    return new Color(0.62f, 0.06f, 0.16f, 0.94f);
                case CocktailBaseId.Vodka:
                    return new Color(0.54f, 0.84f, 0.90f, 0.82f);
                case CocktailBaseId.Cognac:
                    return new Color(0.78f, 0.30f, 0.06f, 0.94f);
                default:
                    return new Color(0.62f, 0.64f, 0.66f, 0.75f);
            }
        }

        public void AdvancePresentation(float unscaledDeltaTime)
        {
            if (!IsOpen || phaseDuration <= 0f)
            {
                return;
            }

            phaseElapsed += Mathf.Max(0f, unscaledDeltaTime);
            if (phaseElapsed < phaseDuration)
            {
                return;
            }

            switch (PresentationPhase)
            {
                case CocktailPresentationPhase.Pouring:
                    CompletePour();
                    break;
                case CocktailPresentationPhase.Serving:
                    CompleteServe();
                    break;
                case CocktailPresentationPhase.RoundResult:
                    CompleteRoundResult();
                    break;
            }
        }

        public void Cancel()
        {
            if (!IsOpen)
            {
                return;
            }

            RetroAudio.Play(RetroSfxId.UiCancel);
            Close();
        }

        private void Update()
        {
            if (!IsOpen)
            {
                return;
            }

            AdvancePresentation(Time.unscaledDeltaTime);
            if (Time.frameCount <= inputUnlockFrame)
            {
                return;
            }

            if (IsCancelPressed())
            {
                Cancel();
                return;
            }

            if (PresentationPhase ==
                CocktailPresentationPhase.RoundResult &&
                IsConfirmPressed())
            {
                RetroAudio.Play(RetroSfxId.UiConfirm);
                CompleteRoundResult();
                return;
            }

            if (PresentationPhase ==
                CocktailPresentationPhase.FinalResult &&
                IsConfirmPressed())
            {
                RetroAudio.Play(RetroSfxId.UiConfirm);
                Close();
                return;
            }

            if (IsMoveLeftPressed())
            {
                MoveSelection(-1);
            }
            else if (IsMoveRightPressed())
            {
                MoveSelection(1);
            }

            if (PresentationPhase ==
                    CocktailPresentationPhase.Mixing &&
                IsServePressed())
            {
                ServeCocktail();
            }
            else if (IsConfirmPressed())
            {
                if (PresentationPhase ==
                    CocktailPresentationPhase.ChoosingBase)
                {
                    ChooseBase(HighlightedBaseIndex);
                }
                else if (PresentationPhase ==
                         CocktailPresentationPhase.Mixing)
                {
                    AddIngredient(HighlightedIngredientIndex);
                }
            }
        }

        private void OnDisable()
        {
            Close();
        }

        private void OnDestroy()
        {
            Close();
        }

        private void CompletePour()
        {
            phaseElapsed = 0f;
            phaseDuration = 0f;
            ActivePourIngredient = CocktailIngredientId.None;
            if (serveAfterPour)
            {
                serveAfterPour = false;
                BeginServing();
            }
            else
            {
                RetroAudio.Play(RetroSfxId.Clink);
                PresentationPhase =
                    CocktailPresentationPhase.Mixing;
                inputUnlockFrame = Time.frameCount + 1;
            }
        }

        private void CompleteServe()
        {
            phaseElapsed = 0f;
            phaseDuration = 0f;
            RetroAudio.Play(
                lastRound.HasValue &&
                (lastRound.Value.HasBadMix ||
                 lastRound.Value.SessionOutcome ==
                    CocktailSessionOutcome.MaxIntoxicationReached)
                    ? RetroSfxId.Bad
                    : RetroSfxId.Good);
            BeginPhase(
                CocktailPresentationPhase.RoundResult,
                RoundResultDuration);
        }

        private void BeginServing()
        {
            lastRound = session.Serve();
            offers = noOffers;
            ActivePourIngredient = CocktailIngredientId.None;
            if (persistSessionProgress)
            {
                GameSessionState.UpdateDrinkingProgress(
                    session.Intoxication,
                    session.LastAlcoholicDrink,
                    session.CocktailsConsumed);
            }

            RetroAudio.Play(RetroSfxId.Shake);

            BeginPhase(
                CocktailPresentationPhase.Serving,
                ServingDuration);
        }

        private void CompleteRoundResult()
        {
            phaseElapsed = 0f;
            phaseDuration = 0f;
            FeedbackKey = string.Empty;
            FeedbackScore = 0;
            lastSelection = null;
            if (session.IsFinished)
            {
                PresentationPhase =
                    CocktailPresentationPhase.FinalResult;
                RaiseCompleted();
            }
            else
            {
                lastRound = null;
                HighlightedBaseIndex = 0;
                HighlightedIngredientIndex = 0;
                PresentationPhase =
                    CocktailPresentationPhase.ChoosingBase;
            }

            inputUnlockFrame = Time.frameCount + 1;
        }

        private void BeginPhase(
            CocktailPresentationPhase phase,
            float duration)
        {
            PresentationPhase = phase;
            phaseElapsed = 0f;
            phaseDuration = Mathf.Max(0f, duration);
            inputUnlockFrame = Time.frameCount + 1;
        }

        private void Close()
        {
            if (!IsOpen)
            {
                return;
            }

            IsOpen = false;
            modalLock.Restore();

            session = null;
            offers = noOffers;
            lastSelection = null;
            lastRound = null;
            ActivePourIngredient = CocktailIngredientId.None;
            FeedbackKey = string.Empty;
            FeedbackScore = 0;
            phaseElapsed = 0f;
            phaseDuration = 0f;
            serveAfterPour = false;
        }

        private void RaiseCompleted()
        {
            if (completionRaised)
            {
                return;
            }

            completionRaised = true;
            Completed?.Invoke();
        }

        private int FindNextAvailableOffer(int startIndex, int direction)
        {
            if (offers.Length == 0)
            {
                return 0;
            }

            int index = startIndex;
            for (int step = 0; step < offers.Length; step++)
            {
                index = Wrap(index + direction, offers.Length);
                if (!IsIngredientUsed(offers[index]))
                {
                    return index;
                }
            }

            return Mathf.Clamp(startIndex, 0, offers.Length - 1);
        }

        private static int Wrap(int value, int count)
        {
            if (count <= 0)
            {
                return 0;
            }

            int result = value % count;
            return result < 0 ? result + count : result;
        }

        private static string GetRankKey(int score)
        {
            if (score >= 300)
            {
                return "cocktail.rank.perfect";
            }

            if (score >= 230)
            {
                return "cocktail.rank.master";
            }

            if (score >= 160)
            {
                return "cocktail.rank.amateur";
            }

            if (score >= 90)
            {
                return "cocktail.rank.okay";
            }

            return "cocktail.rank.slop";
        }

        public static string GetIngredientLabel(
            CocktailIngredientId ingredientId)
        {
            return LocalizationService.Get(
                GetIngredientLocalizationKey(ingredientId));
        }

        private static string GetIngredientLocalizationKey(
            CocktailIngredientId ingredientId)
        {
            switch (ingredientId)
            {
                case CocktailIngredientId.Beer:
                    return "cocktail.ingredient.beer";
                case CocktailIngredientId.Wine:
                    return "cocktail.ingredient.wine";
                case CocktailIngredientId.Vodka:
                    return "cocktail.ingredient.vodka";
                case CocktailIngredientId.Cognac:
                    return "cocktail.ingredient.cognac";
                case CocktailIngredientId.Tonic:
                    return "cocktail.ingredient.tonic";
                case CocktailIngredientId.Soda:
                    return "cocktail.ingredient.soda";
                case CocktailIngredientId.Cola:
                    return "cocktail.ingredient.cola";
                case CocktailIngredientId.Orange:
                    return "cocktail.ingredient.orange";
                case CocktailIngredientId.Lemon:
                    return "cocktail.ingredient.lemon";
                case CocktailIngredientId.GingerAle:
                    return "cocktail.ingredient.ginger_ale";
                case CocktailIngredientId.Honey:
                    return "cocktail.ingredient.honey";
                case CocktailIngredientId.Mint:
                    return "cocktail.ingredient.mint";
                case CocktailIngredientId.Berries:
                    return "cocktail.ingredient.berries";
                case CocktailIngredientId.Cherry:
                    return "cocktail.ingredient.cherry";
                case CocktailIngredientId.Ice:
                    return "cocktail.ingredient.ice";
                default:
                    return "drinking.none";
            }
        }

        private static bool IsConfirmPressed()
        {
            Keyboard keyboard = Keyboard.current;
            if (keyboard != null &&
                (keyboard.eKey.wasPressedThisFrame ||
                 keyboard.enterKey.wasPressedThisFrame))
            {
                return true;
            }

            Gamepad gamepad = Gamepad.current;
            return gamepad != null &&
                   gamepad.buttonSouth.wasPressedThisFrame;
        }

        private static bool IsServePressed()
        {
            Keyboard keyboard = Keyboard.current;
            if (keyboard != null &&
                keyboard.spaceKey.wasPressedThisFrame)
            {
                return true;
            }

            Gamepad gamepad = Gamepad.current;
            return gamepad != null &&
                   gamepad.buttonNorth.wasPressedThisFrame;
        }

        private static bool IsMoveLeftPressed()
        {
            Keyboard keyboard = Keyboard.current;
            if (keyboard != null &&
                (keyboard.leftArrowKey.wasPressedThisFrame ||
                 keyboard.aKey.wasPressedThisFrame))
            {
                return true;
            }

            Gamepad gamepad = Gamepad.current;
            return gamepad != null &&
                   gamepad.dpad.left.wasPressedThisFrame;
        }

        private static bool IsMoveRightPressed()
        {
            Keyboard keyboard = Keyboard.current;
            if (keyboard != null &&
                (keyboard.rightArrowKey.wasPressedThisFrame ||
                 keyboard.dKey.wasPressedThisFrame))
            {
                return true;
            }

            Gamepad gamepad = Gamepad.current;
            return gamepad != null &&
                   gamepad.dpad.right.wasPressedThisFrame;
        }

        private static bool IsCancelPressed()
        {
            Keyboard keyboard = Keyboard.current;
            if (keyboard != null &&
                keyboard.escapeKey.wasPressedThisFrame)
            {
                return true;
            }

            Gamepad gamepad = Gamepad.current;
            return gamepad != null &&
                   gamepad.buttonEast.wasPressedThisFrame;
        }
    }
}

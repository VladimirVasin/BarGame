using System;
using UnityEngine;
using UnityEngine.Rendering;

namespace BarPromenade
{
    /// <summary>
    /// The worksite, standing on the plot from the moment the job is
    /// taken until the stone is up. It is one interaction across all
    /// three acts of the work — dig the hole, lower the coffin, fill
    /// it in — because they happen on the same square metre and the
    /// hero approaches them the same way; only the prompt and what is
    /// standing on the ground change.
    ///
    /// While the plot is only marked out it shows a pulsing plate the
    /// size of the hole to come and four pegs at its corners. The
    /// first cut of the spade is the end of those: after that the hole
    /// itself is the marker, and the site is a trigger with nothing
    /// visible in it.
    ///
    /// The site owns no state of its own. It asks the gravedigging
    /// controller to take the next step and is told which stage the
    /// work reached.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class CemeteryGraveDigSiteInteraction :
        MonoBehaviour,
        IInteractable
    {
        public const string RuntimeRootName = "Grave Dig Site";
        public const string DigPromptKey = "interaction.dig_grave";
        public const string CoffinPromptKey =
            "interaction.lower_coffin";
        public const string FillPromptKey = "interaction.fill_grave";
        public const string DugFeedbackKey =
            "cemetery.gravedigging.dug";
        public const string CoffinFeedbackKey =
            "cemetery.gravedigging.coffin";
        public const string SealedFeedbackKey =
            "cemetery.gravedigging.done";
        public const float FeedbackDurationSeconds = 3.0f;

        /// <summary>A slow breath rather than a blink: the marker has
        /// to read at a distance without turning the cemetery into a
        /// fairground.</summary>
        public const float PulsePeriodSeconds = 1.9f;
        public const float PulseFloor = 0.55f;

        public const float PlateHoverMeters = 0.035f;
        public const float PlateThickness = 0.02f;
        public const float PegHeight = 0.34f;
        public const float PegSpan = 0.07f;
        public const float TriggerHeight = 1.7f;

        /// <summary>Chalk-white, the colour a surveyor's line would
        /// be: it never reads as one of the cemetery's own lamps.
        /// </summary>
        internal static readonly Color MarkColor =
            new Color(0.86f, 0.84f, 0.66f);

        private readonly Renderer[] marks = new Renderer[5];

        private Vector3 standPosition;
        private Func<bool> advanceAction;
        private CemeteryGraveWorkStage stage;
        private int markCount;
        private bool isInitialized;

        public string PromptKey => GetPromptKey(stage);
        public Vector3 InteractionPosition => standPosition;

        /// <summary>Which act of the work the site is offering.
        /// </summary>
        public CemeteryGraveWorkStage Stage => stage;

        /// <summary>The pulse phase last written, for tests.</summary>
        public float LastPulse { get; private set; } = 1f;

        /// <summary>
        /// Raises the worksite over one planned grave at the stage the
        /// work has actually reached. The advance action returns false
        /// when the step could not be taken, and the site then simply
        /// stays as it is.
        /// </summary>
        public static CemeteryGraveDigSiteInteraction Create(
            Transform parent,
            CemeteryGravediggingPlan plan,
            CemeteryGraveWorkStage stage,
            Func<bool> onAdvance)
        {
            if (parent == null)
            {
                throw new ArgumentNullException(nameof(parent));
            }

            if (plan == null)
            {
                throw new ArgumentNullException(nameof(plan));
            }

            if (!plan.IsPresent)
            {
                return null;
            }

            var root = new GameObject(RuntimeRootName);
            root.transform.SetParent(parent, false);
            root.transform.position = plan.Ground;

            var site =
                root.AddComponent<CemeteryGraveDigSiteInteraction>();
            site.stage = stage;
            if (stage == CemeteryGraveWorkStage.Marked)
            {
                site.Build(plan);
            }

            site.standPosition = plan.Ground;
            site.advanceAction = onAdvance;
            site.isInitialized = true;

            // The hero reaches the plot from wherever he is standing
            // beside it, so the trigger is the mouth of the grave at
            // walking height rather than a stub on one face.
            BoxCollider trigger = root.AddComponent<BoxCollider>();
            trigger.isTrigger = true;
            trigger.center = new Vector3(0f, TriggerHeight * 0.5f, 0f);
            trigger.size = new Vector3(
                plan.PitMouth.width,
                TriggerHeight,
                plan.PitMouth.height);
            return site;
        }

        /// <summary>
        /// Moves the site on to the act the work has reached. The
        /// marked-out plate and its pegs come down with the first
        /// spadeful and never come back.
        /// </summary>
        public void SetStage(CemeteryGraveWorkStage value)
        {
            stage = value;
            if (value == CemeteryGraveWorkStage.Marked)
            {
                return;
            }

            for (int index = 0; index < markCount; index++)
            {
                Renderer mark = marks[index];
                marks[index] = null;
                if (mark == null)
                {
                    continue;
                }

                if (Application.isPlaying)
                {
                    Destroy(mark.gameObject);
                }
                else
                {
                    DestroyImmediate(mark.gameObject);
                }
            }

            markCount = 0;
        }

        public bool CanInteract(PlayerInteractor interactor)
        {
            return isInitialized &&
                   isActiveAndEnabled &&
                   advanceAction != null &&
                   IsWorkingStage(stage) &&
                   interactor != null &&
                   interactor.isActiveAndEnabled &&
                   interactor.InputEnabled &&
                   !SceneTransitionService.IsTransitioning;
        }

        public void Interact(PlayerInteractor interactor)
        {
            if (!CanInteract(interactor))
            {
                return;
            }

            CemeteryGraveWorkStage attempted = stage;
            if (advanceAction())
            {
                interactor.ShowFeedback(
                    GetFeedbackKey(attempted),
                    FeedbackDurationSeconds);
            }
        }

        /// <summary>The three acts that are the hero's to take. A
        /// finished grave offers nothing.</summary>
        internal static bool IsWorkingStage(
            CemeteryGraveWorkStage value)
        {
            return value == CemeteryGraveWorkStage.Marked ||
                   value == CemeteryGraveWorkStage.Dug ||
                   value == CemeteryGraveWorkStage.Coffined;
        }

        internal static string GetPromptKey(
            CemeteryGraveWorkStage value)
        {
            switch (value)
            {
                case CemeteryGraveWorkStage.Dug:
                    return CoffinPromptKey;
                case CemeteryGraveWorkStage.Coffined:
                    return FillPromptKey;
                default:
                    return DigPromptKey;
            }
        }

        /// <summary>What he says to himself once the act is done. It
        /// is keyed on the stage he took it from, not the one he
        /// arrived at, because the line is about the work.</summary>
        internal static string GetFeedbackKey(
            CemeteryGraveWorkStage value)
        {
            switch (value)
            {
                case CemeteryGraveWorkStage.Dug:
                    return CoffinFeedbackKey;
                case CemeteryGraveWorkStage.Coffined:
                    return SealedFeedbackKey;
                default:
                    return DugFeedbackKey;
            }
        }

        private void Build(CemeteryGravediggingPlan plan)
        {
            Rect mouth = plan.PitMouth;
            AddMark(
                "Grave Mark Plate",
                new Vector3(
                    0f,
                    PlateHoverMeters + PlateThickness * 0.5f,
                    0f),
                new Vector3(
                    mouth.width,
                    PlateThickness,
                    mouth.height));
            for (int corner = 0; corner < 4; corner++)
            {
                float offsetX = (corner & 1) == 0
                    ? -mouth.width * 0.5f
                    : mouth.width * 0.5f;
                float offsetZ = (corner & 2) == 0
                    ? -mouth.height * 0.5f
                    : mouth.height * 0.5f;
                AddMark(
                    $"Grave Mark Peg {corner}",
                    new Vector3(offsetX, PegHeight * 0.5f, offsetZ),
                    new Vector3(PegSpan, PegHeight, PegSpan));
            }
        }

        private void AddMark(
            string name,
            Vector3 localPosition,
            Vector3 size)
        {
            GameObject mark = RuntimePrimitiveFactory.CreateBox(
                name,
                transform,
                localPosition,
                size,
                MarkColor,
                CityNightResources.EmissiveMaterial,
                false);
            Renderer created = mark.GetComponent<Renderer>();
            created.shadowCastingMode = ShadowCastingMode.Off;
            created.receiveShadows = false;
            marks[markCount] = created;
            markCount++;
        }

        private void Update()
        {
            if (!isInitialized || markCount == 0)
            {
                return;
            }

            ApplyPulse(Time.unscaledTime);
        }

        internal void ApplyPulse(float unscaledTime)
        {
            float phase = Mathf.Repeat(
                unscaledTime / PulsePeriodSeconds,
                1f);
            float pulse = Mathf.Lerp(
                PulseFloor,
                1f,
                0.5f - 0.5f * Mathf.Cos(phase * Mathf.PI * 2f));
            LastPulse = pulse;
            var tint = new Color(
                MarkColor.r * pulse,
                MarkColor.g * pulse,
                MarkColor.b * pulse,
                MarkColor.a);
            for (int index = 0; index < markCount; index++)
            {
                Renderer mark = marks[index];
                if (mark != null)
                {
                    RuntimePrimitiveFactory.SetColor(mark, tint);
                }
            }
        }
    }
}

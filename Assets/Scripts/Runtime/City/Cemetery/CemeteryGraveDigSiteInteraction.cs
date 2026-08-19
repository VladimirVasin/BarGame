using System;
using UnityEngine;
using UnityEngine.Rendering;

namespace BarPromenade
{
    /// <summary>
    /// The plot the watchman marked out, standing on the ground until
    /// somebody digs it: a pulsing plate the size of the hole to come,
    /// four pegs at its corners, and one interaction that turns the
    /// whole thing into a grave.
    ///
    /// The marker owns no state of its own. It asks the gravedigging
    /// controller to dig and disappears when the controller says the
    /// hole is open.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class CemeteryGraveDigSiteInteraction :
        MonoBehaviour,
        IInteractable
    {
        public const string RuntimeRootName = "Grave Dig Site";
        public const string DigPromptKey = "interaction.dig_grave";
        public const string DugFeedbackKey =
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
        private Func<bool> digAction;
        private int markCount;
        private bool isInitialized;

        public string PromptKey => DigPromptKey;
        public Vector3 InteractionPosition => standPosition;

        /// <summary>The pulse phase last written, for tests.</summary>
        public float LastPulse { get; private set; } = 1f;

        /// <summary>
        /// Raises the marker over one planned grave. The dig action
        /// returns false when the ground refuses the hole, and the
        /// marker then simply stays up.
        /// </summary>
        public static CemeteryGraveDigSiteInteraction Create(
            Transform parent,
            CemeteryGravediggingPlan plan,
            Func<bool> onDig)
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
            site.Build(plan);
            site.standPosition = plan.Ground;
            site.digAction = onDig;
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

        public bool CanInteract(PlayerInteractor interactor)
        {
            return isInitialized &&
                   isActiveAndEnabled &&
                   digAction != null &&
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

            if (digAction())
            {
                interactor.ShowFeedback(
                    DugFeedbackKey,
                    FeedbackDurationSeconds);
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

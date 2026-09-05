using System;
using UnityEngine;
using UnityEngine.InputSystem;

namespace BarPromenade
{
    /// <summary>
    /// A single real pot: reach with both hands, turn it quietly, then set it
    /// on either shelf dock. The shared action controller owns all hero state.
    /// </summary>
    [DefaultExecutionOrder(300)]
    [DisallowMultipleComponent]
    public sealed class ChurchGardenPotInteraction : MonoBehaviour, IInteractable
    {
        private PlayerRuntime player;
        private PlayerAnimatedInteractionController controller;
        private Player3DAssetRegistry registry;
        private ChurchGardenPotPlan plan;
        private Transform pot;
        private bool ready;
        private bool ownsInteraction;
        private bool held;
        private bool placeRequested;
        private bool released;
        private int selectedDock;
        private int sourceDock;

        public ChurchGardenPotPlan Plan => plan;
        public PlayerAnimatedInteractionController Controller => controller;
        public int DockIndex { get; private set; }
        public int SelectedDockIndex => selectedDock;
        public bool IsHolding => held;
        public bool OwnsActiveInteraction => ownsInteraction;
        public bool IsPlacementRequested => placeRequested;
        public Transform PotTransform => pot;
        public string PromptKey => IsHolding
            ? selectedDock == 0 ? "interaction.place_garden_pot_left" : "interaction.place_garden_pot_right"
            : "interaction.inspect_garden_pot";
        public Vector3 InteractionPosition => plan != null
            ? plan.StandingGroundPosition : transform.position;

        public void Initialize(
            PlayerRuntime playerRuntime,
            PlayerAnimatedInteractionController sharedController,
            ChurchGardenPotPlan interactionPlan,
            Transform physicalPot)
        {
            if (playerRuntime.GameObject == null || sharedController == null ||
                !sharedController.IsInitialized || interactionPlan == null || physicalPot == null)
            {
                throw new ArgumentException("The pot requires a player, shared controller, plan and physical model.");
            }

            Cancel();
            Unsubscribe();
            player = playerRuntime;
            controller = sharedController;
            registry = playerRuntime.GameObject.GetComponentInChildren<Player3DAssetRegistry>();
            plan = interactionPlan;
            pot = physicalPot;
            DockIndex = ChurchGardenPotSessionState.GetDock(plan.SessionKey);
            PlaceAtDock(DockIndex);
            ready = ChurchGardenPotActions.TryAttach(registry);
            controller.PhaseChanged += HandlePhaseChanged;
        }

        public bool CanInteract(PlayerInteractor interactor)
        {
            if (!ready || !isActiveAndEnabled || interactor == null ||
                interactor != player.Interactor || !interactor.InputEnabled ||
                controller == null || !controller.isActiveAndEnabled ||
                CounterMenuInput.IsBlockedByOtherUi())
            {
                return false;
            }

            if (ownsInteraction)
            {
                return controller.Phase == PlayerAnimatedInteractionPhase.Looping && !placeRequested;
            }

            return controller.Phase == PlayerAnimatedInteractionPhase.Idle &&
                Vector3.Dot(player.GameObject.transform.position - plan.StandingGroundPosition,
                    plan.Facing * Vector3.forward) <= 0.30f &&
                Mathf.Abs(player.GameObject.transform.position.y - plan.EntryPose.RootPosition.y) <=
                ChurchGardenPotPlan.ApproachVerticalTolerance;
        }

        public void Interact(PlayerInteractor interactor)
        {
            if (!CanInteract(interactor))
            {
                return;
            }

            if (ownsInteraction)
            {
                RequestPlace();
            }
            else
            {
                Begin();
            }
        }

        public bool Begin()
        {
            if (!CanInteract(player.Interactor) || ownsInteraction)
            {
                return false;
            }

            sourceDock = DockIndex;
            selectedDock = 1 - sourceDock;
            placeRequested = false;
            released = false;
            held = false;
            ownsInteraction = controller.BeginPositioned(
                ChurchGardenPotPlan.CreateDefinition(sourceDock),
                plan.EntryPose, plan.ActionHipPosition, plan.ExitPose,
                ChurchGardenPotPlan.ApproachVerticalTolerance);
            return ownsInteraction;
        }

        public bool SelectDock(int index)
        {
            ChurchGardenPotPlan.ValidateDockIndex(index);
            if (!ownsInteraction || placeRequested ||
                controller.Phase != PlayerAnimatedInteractionPhase.Looping)
            {
                return false;
            }

            selectedDock = index;
            return true;
        }

        public bool RequestPlace()
        {
            if (!ownsInteraction || placeRequested ||
                controller.Phase != PlayerAnimatedInteractionPhase.Looping)
            {
                return false;
            }

            placeRequested = controller.RequestExitAtLoopBoundaryWithClip(
                selectedDock == 0 ? "ChurchPotPlaceLeft" : "ChurchPotPlaceRight");
            return placeRequested;
        }

        public bool Cancel()
        {
            if (!ownsInteraction)
            {
                return false;
            }

            controller?.CancelActiveInteraction();
            RestorePot();
            return true;
        }

        private void Update()
        {
            if (!held || placeRequested || controller == null ||
                controller.Phase != PlayerAnimatedInteractionPhase.Looping ||
                CounterMenuInput.IsBlockedByOtherUi())
            {
                return;
            }

            Keyboard keyboard = Keyboard.current;
            Gamepad pad = Gamepad.current;
            if ((keyboard != null && keyboard.aKey.wasPressedThisFrame) ||
                (pad != null && pad.dpad.left.wasPressedThisFrame))
            {
                SelectDock(0);
            }
            else if ((keyboard != null && keyboard.dKey.wasPressedThisFrame) ||
                (pad != null && pad.dpad.right.wasPressedThisFrame))
            {
                SelectDock(1);
            }
        }

        private void LateUpdate()
        {
            if (!ownsInteraction || pot == null || controller == null)
            {
                return;
            }

            PlayerAnimatedInteractionPhase phase = controller.Phase;
            if (phase == PlayerAnimatedInteractionPhase.Positioning)
            {
                return;
            }

            if (phase == PlayerAnimatedInteractionPhase.Exiting &&
                controller.PhaseProgress >= ChurchGardenPotPlan.ContactProgress)
            {
                if (!released)
                {
                    held = false;
                    released = true;
                    DockIndex = selectedDock;
                    PlaceAtDock(DockIndex);
                    ChurchGardenPotSessionState.SetDock(plan.SessionKey, DockIndex);
                }
                return;
            }

            held = phase == PlayerAnimatedInteractionPhase.Looping ||
                phase == PlayerAnimatedInteractionPhase.Exiting ||
                (phase == PlayerAnimatedInteractionPhase.Entering &&
                 controller.PhaseProgress >= ChurchGardenPotPlan.ContactProgress);
            if (!held)
            {
                return;
            }

            Transform left = registry.Anchors.LeftGrip;
            Transform right = registry.Anchors.RightGrip;
            Vector3 rightAxis = right.position - left.position;
            Vector3 forward = Vector3.Cross(rightAxis.normalized, Vector3.up);
            Quaternion rotation = Quaternion.LookRotation(forward, Vector3.up);
            Vector3 basePosition = (left.position + right.position) * 0.5f -
                Vector3.up * ChurchGardenPotPlan.GripHeight;
            pot.SetPositionAndRotation(basePosition, rotation);
        }

        private void HandlePhaseChanged(PlayerAnimatedInteractionPhase phase)
        {
            if (ownsInteraction && phase == PlayerAnimatedInteractionPhase.Idle)
            {
                RestorePot();
            }
        }

        private void RestorePot()
        {
            ownsInteraction = false;
            held = false;
            placeRequested = false;
            // A cancelled lift restores its source. A physically released pot
            // remains at the committed destination, including scene teardown.
            if (!released)
            {
                DockIndex = sourceDock;
            }
            PlaceAtDock(DockIndex);
        }

        private void PlaceAtDock(int index)
        {
            if (pot != null && plan != null)
            {
                pot.SetPositionAndRotation(plan.GetDockPosition(index), plan.Facing);
            }
        }

        private void Unsubscribe()
        {
            if (controller != null)
            {
                controller.PhaseChanged -= HandlePhaseChanged;
            }
        }

        private void OnDisable() => Cancel();
        private void OnDestroy()
        {
            Cancel();
            Unsubscribe();
        }
    }
}

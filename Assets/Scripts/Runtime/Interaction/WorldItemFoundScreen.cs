using System;
using UnityEngine;
using UnityEngine.InputSystem;

namespace BarPromenade
{
    /// <summary>
    /// The screen a thing found in the world opens: the object itself leaves
    /// the floor, flies to the eye, turns, and is named and described while
    /// the player looks at it. It is the refrigerator's examination screen
    /// with the shelf taken away.
    ///
    /// It lives on the hero, installed once by
    /// <see cref="PlayerFactory"/>, so a pickup anywhere in the game needs no
    /// wiring of its own beyond asking for it. It takes the full modal lock,
    /// unlike the refrigerator's nested inspector, because out in the world
    /// there is no outer interaction already holding one.
    /// </summary>
    [DefaultExecutionOrder(270)]
    [DisallowMultipleComponent]
    public sealed class WorldItemFoundScreen : MonoBehaviour
    {
        /// <summary>
        /// The one "take" line in the game. It keeps the refrigerator
        /// namespace it was written in, because the string itself never
        /// changed and the catalog pins the key; what became shared is the
        /// button, not the shelf. There is deliberately no control hint
        /// beside it - the art bible's §15a bans standing key guides, and the
        /// ones this game used to have are asserted gone by the catalog test.
        /// </summary>
        public const string TakeActionKey = "home.refrigerator.action.take";

        /// <summary>
        /// Shown when the inventory refuses the item. Also written for a
        /// shop, and true in both places for the same reason.
        /// </summary>
        public const string NoRoomFeedbackKey =
            "supermarket.failure.inventory_full";

        /// <summary>
        /// Pointer sensitivity. A dragged screen width turns the object about
        /// one and a half times, which reads as turning it in the hand rather
        /// than spinning it.
        /// </summary>
        public const float PointerDegreesPerPixel = 0.42f;
        public const float StickDegreesPerSecond = 190f;
        public const float StickDeadZone = 0.22f;

        private readonly BarMinigameModalLock modalLock =
            new BarMinigameModalLock();

        private Camera targetCamera;
        private WorldItemInspectionPresenter presenter;
        private WorldItemPickupModel pickup;
        private Func<bool> commit;
        private Action<bool> finished;
        private Transform heldModel;
        private int inputUnlockFrame;
        private string feedbackKey = string.Empty;

        public bool IsInitialized { get; private set; }
        public InventoryItemId ActiveItemId { get; private set; }

        /// <summary>Whether the screen owns the player's attention.</summary>
        public bool IsPresenting => pickup != null && pickup.IsOpen;

        /// <summary>Whether the item is held still and the panel is up.</summary>
        public bool IsShowing => pickup != null && pickup.IsShowing;

        public string FeedbackKey => feedbackKey;

        public InventoryItemDefinition ActiveDefinition =>
            InventoryItemCatalog.Get(ActiveItemId);

        public static WorldItemFoundScreen For(PlayerInteractor interactor)
        {
            return interactor == null
                ? null
                : interactor.GetComponent<WorldItemFoundScreen>();
        }

        public void Initialize(Camera screenCamera)
        {
            targetCamera = screenCamera != null
                ? screenCamera
                : throw new ArgumentNullException(nameof(screenCamera));
            Abandon();
            presenter?.Dispose();
            presenter = null;
            WorldItemFoundView view =
                GetComponent<WorldItemFoundView>();
            if (view == null)
            {
                view = gameObject.AddComponent<WorldItemFoundView>();
            }

            view.Initialize(this);
            IsInitialized = true;
        }

        /// <summary>
        /// Holds one found object up. <paramref name="commitTake"/> is called
        /// once, at the moment the player agrees, and reports whether the
        /// inventory accepted it; <paramref name="onFinished"/> reports how
        /// the screen ended so the owner can remove the object or leave it.
        /// </summary>
        public bool TryPresent(
            PlayerInteractor interactor,
            InventoryItemId itemId,
            Transform model,
            Func<bool> commitTake,
            Action<bool> onFinished)
        {
            if (commitTake == null)
            {
                throw new ArgumentNullException(nameof(commitTake));
            }

            if (model == null)
            {
                throw new ArgumentNullException(nameof(model));
            }

            // Everything that can refuse the find is asked BEFORE the modal
            // lock is taken. A throw between capturing that lock and the
            // Restore at the end of the screen would leave it held by a
            // living interactor, and a held lock is not a stuck screen - it
            // is every door, menu and interaction in the game refusing to
            // open for the rest of the session, with no error anywhere.
            if (!IsInitialized ||
                !isActiveAndEnabled ||
                pickup != null ||
                interactor == null ||
                !InventoryItemCatalog.TryGet(itemId, out _) ||
                !InventoryItemPreviewPoses.TryGet(
                    itemId,
                    out InventoryItemPreviewPose pose))
            {
                return false;
            }

            if (!modalLock.TryCaptureAndDisable(
                    interactor,
                    ResolveCameraFollow(),
                    ResolveHud()))
            {
                return false;
            }

            try
            {
                EnsurePresenter();
                pickup = new WorldItemPickupModel();
                ActiveItemId = itemId;
                commit = commitTake;
                finished = onFinished;
                feedbackKey = string.Empty;
                heldModel = model;
                if (!presenter.Begin(
                        model,
                        WorldItemInspectionPresenter.CalculateWorldBounds(
                            model),
                        pose.BaseRotation,
                        pose.InspectionScale))
                {
                    Abandon();
                    modalLock.Restore();
                    return false;
                }

                if (!pickup.Open())
                {
                    Abandon();
                    return false;
                }

                presenter.Apply(pickup.Timeline.CurrentFrame);
            }
            catch
            {
                Abandon();
                modalLock.Restore();
                throw;
            }

            // The same E that lifted the thing off the floor is still down
            // this frame, and the screen's own confirm reads the same action.
            // Without this the item would be found and taken in one press and
            // nobody would ever see it.
            inputUnlockFrame = Time.frameCount + 1;
            RetroAudio.Play(RetroSfxId.UiMove);
            return true;
        }

        /// <summary>
        /// Takes the item. The inventory is credited here and nowhere else,
        /// and a refusal keeps the object in the world with a line saying so.
        /// </summary>
        public bool Confirm()
        {
            if (pickup == null || !pickup.CanConfirm)
            {
                return false;
            }

            if (!commit())
            {
                feedbackKey = NoRoomFeedbackKey;
                RetroAudio.Play(RetroSfxId.UiCancel);
                return true;
            }

            if (!pickup.Confirm())
            {
                throw new InvalidOperationException(
                    "A committed take must be recorded by the pickup model.");
            }

            // The object is in his pocket the moment he agrees, so it stops
            // being visible at once; the short return that follows is the
            // darkness lifting, not the thing flying back to the floor.
            if (heldModel != null)
            {
                heldModel.gameObject.SetActive(false);
            }

            feedbackKey = string.Empty;
            RetroAudio.Play(RetroSfxId.UiConfirm);
            return true;
        }

        /// <summary>
        /// Puts the find back down and closes the screen the way a take
        /// closes it.
        /// </summary>
        public bool Dismiss()
        {
            if (pickup == null || !pickup.Dismiss())
            {
                return false;
            }

            feedbackKey = string.Empty;
            RetroAudio.Play(RetroSfxId.UiCancel);
            return true;
        }

        public bool Abandon()
        {
            if (pickup == null)
            {
                return false;
            }

            bool taken = pickup.IsTaken;
            pickup.Abandon();
            Finish(taken);
            return true;
        }

        private void Update()
        {
            if (pickup == null)
            {
                return;
            }

            // Not `if (!IsPresenting) return;`. A confirm or a dismiss taken
            // through the API before the item has left the floor closes the
            // timeline the same instant, and an early return here would then
            // never reach Finish: the model would stay on the camera pivot
            // and the modal lock would never come back.
            if (!pickup.IsOpen)
            {
                Finish(pickup.IsTaken);
                return;
            }

            HandleInput();
            if (pickup == null)
            {
                return;
            }

            pickup.Advance(Time.unscaledDeltaTime);
            presenter.Apply(pickup.Timeline.CurrentFrame);
            if (!pickup.IsOpen)
            {
                Finish(pickup.IsTaken);
            }
        }

        private void HandleInput()
        {
            if (Time.frameCount < inputUnlockFrame)
            {
                return;
            }

            ApplyManualTurn();
            if (GameInput.WasPressed(
                    GameInputAction.Interact,
                    GameInputContext.Menu))
            {
                Confirm();
                return;
            }

            // Backing out is not an offered choice - the panel shows one
            // button - but it must exist. Otherwise a hero whose pockets are
            // full meets a screen whose only action keeps failing, and the
            // modal lock it holds shuts every door in the game for the rest
            // of the session.
            if (GameInput.WasPressed(
                    GameInputAction.Cancel,
                    GameInputContext.Menu))
            {
                Dismiss();
            }
        }

        private void ApplyManualTurn()
        {
            float degrees = 0f;
            Mouse mouse = Mouse.current;
            if (mouse != null && mouse.leftButton.isPressed)
            {
                degrees +=
                    mouse.delta.ReadValue().x * PointerDegreesPerPixel;
            }

            Gamepad gamepad = Gamepad.current;
            if (gamepad != null)
            {
                float stick = gamepad.rightStick.ReadValue().x;
                if (Mathf.Abs(stick) >= StickDeadZone)
                {
                    degrees +=
                        stick *
                        StickDegreesPerSecond *
                        Time.unscaledDeltaTime;
                }
            }

            if (!Mathf.Approximately(degrees, 0f))
            {
                pickup.AddManualRotation(-degrees);
            }
        }

        private void Finish(bool taken)
        {
            // Always put the model back under its own owner first, taken or
            // not. Left hanging off the camera pivot it would outlive the
            // object that owns it and ride the camera into the next scene.
            presenter?.Restore();
            Action<bool> callback = finished;
            pickup = null;
            commit = null;
            finished = null;
            heldModel = null;
            feedbackKey = string.Empty;
            ActiveItemId = InventoryItemId.None;
            modalLock.Restore();
            callback?.Invoke(taken);
        }

        /// <summary>
        /// The pivot and the veil are built the first time something is
        /// actually found. Every hero in every scene and every test rig owns
        /// this screen, and most of them never open it.
        /// </summary>
        private void EnsurePresenter()
        {
            presenter ??= new WorldItemInspectionPresenter(
                targetCamera,
                "World Item Found");
        }

        private PlayerCameraFollow ResolveCameraFollow()
        {
            return targetCamera != null
                ? targetCamera.GetComponent<PlayerCameraFollow>()
                : null;
        }

        private static IntoxicationHudView ResolveHud()
        {
            return FindAnyObjectByType<IntoxicationHudView>();
        }

        private void OnDisable()
        {
            // A screen that goes away with the item still in the air must put
            // it back, or the object rides the camera into the next scene.
            Abandon();
        }

        private void OnDestroy()
        {
            Abandon();
            presenter?.Dispose();
            presenter = null;
            IsInitialized = false;
        }
    }
}

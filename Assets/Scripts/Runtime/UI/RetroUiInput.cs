using System;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.SceneManagement;

namespace BarPromenade
{
    /// <summary>
    /// Input System pointer ownership for the existing fixed-rectangle UI.
    /// IMGUI only paints: no native input events or legacy Input Manager are used.
    /// </summary>
    public static class RetroUiInput
    {
        private const int ButtonHint = 0x52455452;
        private static readonly GUIContent ButtonContent = new GUIContent();
        private static int sampledFrame = -1, sampledPaintFrame = -1, pressed, released, held;
        private static int pendingPress, pendingRelease;
        private static int consumedPress, consumedRelease, consumedDrag;
        private static int capturedButton;
        private static bool hasPosition, moved, neutralRequired, scrollConsumed;
        private static Vector2 screenPosition, previousPosition, scroll, rootScreenOrigin;
        private static Vector2 pendingScroll, pendingPressPosition, pressPosition;
        private static Vector2 pendingReleasePosition, releasePosition;
        private static Mouse sampledMouse;
        private static GameInputContext context = GameInputContext.Menu, captureContext;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void Install()
        {
            Reset();
            Application.focusChanged -= OnFocusChanged;
            Application.focusChanged += OnFocusChanged;
            SceneManager.activeSceneChanged -= OnSceneChanged;
            SceneManager.activeSceneChanged += OnSceneChanged;
            InputSystem.onAfterUpdate -= CollectPointer;
            InputSystem.onAfterUpdate += CollectPointer;
        }

        private static void OnFocusChanged(bool focused) { Reset(); neutralRequired = true; }
        private static void OnSceneChanged(Scene before, Scene after) { Reset(); neutralRequired = true; }

        internal static void Reset()
        {
            sampledFrame = sampledPaintFrame = -1;
            capturedButton = 0;
            pressed = released = held = consumedPress = consumedRelease = consumedDrag = 0;
            scroll = Vector2.zero;
            pendingScroll = Vector2.zero;
            pendingPress = pendingRelease = 0;
            hasPosition = moved = scrollConsumed = false;
            sampledMouse = null;
        }

        /// <summary>Start before applying the canvas matrix or opening GUI groups.</summary>
        public static Scope Begin(GameInputContext inputContext = GameInputContext.Menu)
        {
            Vector2 previous = rootScreenOrigin;
            GameInputContext previousContext = context;
            rootScreenOrigin = GUIUtility.GUIToScreenPoint(Vector2.zero);
            context = inputContext;
            return new Scope(previous, previousContext);
        }

        public readonly struct Scope : IDisposable
        {
            private readonly Vector2 previous;
            private readonly GameInputContext previousContext;
            internal Scope(Vector2 origin, GameInputContext inputContext) { previous = origin; previousContext = inputContext; }
            public void Dispose() { rootScreenOrigin = previous; context = previousContext; }
        }

        private static bool CanRead => GameCursor.ApplicationFocused && GameInput.CanRead(context);
        private static bool IsPointerPass => Event.current != null && Event.current.type == EventType.Repaint && CanRead;

        // A GUI group has its own clip origin. Keep the native GUIClip conversion,
        // including the root window offset, rather than inverting only GUI.matrix.
        public static Vector2 MousePosition
        {
            get
            {
                Sample();
                return GUIUtility.ScreenToGUIPoint(rootScreenOrigin + screenPosition);
            }
        }

        /// <summary>Window-local pixels, top-left origin, including the current GUI clip.</summary>
        public static Vector2 ToScreenPosition(Vector2 guiPoint) =>
            GUIUtility.GUIToScreenPoint(guiPoint) - rootScreenOrigin;

        public static Vector2 WindowPointerPosition { get { Sample(); return screenPosition; } }

        public static bool PointerMoved { get { Sample(); return IsPointerPass && moved; } }
        public static bool PointerPressed => GetPointerPressed(0);
        public static bool PointerReleased => GetPointerReleased(0);
        public static bool PointerHeld => GetPointerHeld(0);
        public static bool PointerDragged => GetPointerDragged(0);
        public static bool AnyPointerPressed { get { Sample(); return IsPointerPass && (pressed & ~consumedPress) != 0; } }
        public static bool AnyPointerDragged { get { Sample(); return IsPointerPass && moved && (held & ~consumedDrag) != 0; } }
        public static bool ShiftHeld => Keyboard.current != null && Keyboard.current.shiftKey.isPressed;
        public static bool ControlHeld => Keyboard.current != null && Keyboard.current.ctrlKey.isPressed;
        public static bool CommandHeld => Keyboard.current != null && (Keyboard.current.leftMetaKey.isPressed || Keyboard.current.rightMetaKey.isPressed);
        public static bool AltHeld => Keyboard.current != null && Keyboard.current.altKey.isPressed;

        public static bool GetPointerPressed(int button)
        {
            Sample();
            return IsPointerPass && capturedButton == 0 && (pressed & ~consumedPress & Bit(button)) != 0;
        }

        public static bool GetPointerReleased(int button)
        {
            Sample();
            return IsPointerPass && capturedButton == 0 && (released & ~consumedRelease & Bit(button)) != 0;
        }

        public static bool GetPointerHeld(int button)
        {
            Sample();
            return IsPointerPass && (button != 0 || capturedButton == 0) && (held & Bit(button)) != 0;
        }

        public static bool GetPointerDragged(int button)
        {
            Sample();
            return GetPointerHeld(button) && moved && (consumedDrag & Bit(button)) == 0;
        }

        public static void ConsumePointerPress(int button = 0) { Sample(); consumedPress |= Bit(button); }
        public static void ConsumePointerRelease(int button = 0) { Sample(); consumedRelease |= Bit(button); }
        public static void ConsumePointerDrag(int button = 0) { Sample(); consumedDrag |= Bit(button); }
        public static Vector2 ScrollDelta { get { Sample(); return IsPointerPass && !scrollConsumed ? scroll : Vector2.zero; } }
        public static void ConsumeScroll() { Sample(); scrollConsumed = true; }

        public static bool Button(Rect rect, string label, GUIStyle style)
        {
            ButtonContent.text = label;
            ButtonContent.image = null;
            ButtonContent.tooltip = string.Empty;
            return Button(rect, ButtonContent, style);
        }

        public static bool Button(Rect rect, string label) => Button(rect, label, GUI.skin.button);
        public static bool Button(Rect rect, GUIContent content) => Button(rect, content, GUI.skin.button);

        public static bool Button(Rect rect, GUIContent content, GUIStyle style)
        {
            int id = GUIUtility.GetControlID(ButtonHint, FocusType.Passive, rect);
            if (Event.current.type != EventType.Repaint) return false;
            Sample();
            bool hover = CanRead && rect.Contains(MousePosition);
            if (GUI.enabled && CanRead && GetPointerPressed(0) &&
                rect.Contains(GUIUtility.ScreenToGUIPoint(rootScreenOrigin + pressPosition)))
            {
                capturedButton = id;
                captureContext = context;
                ConsumePointerPress();
            }
            bool clicked = false;
            if (capturedButton == id && (released & ~consumedRelease & 1) != 0)
            {
                capturedButton = 0;
                ConsumePointerRelease();
                clicked = GUI.enabled && CanRead &&
                    rect.Contains(GUIUtility.ScreenToGUIPoint(rootScreenOrigin + releasePosition));
                if (clicked) GUI.changed = true;
            }
            style.Draw(rect, content, GUI.enabled && hover,
                capturedButton == id && (held & 1) != 0, false, false);
            return clicked;
        }

        private static int Bit(int button) => button >= 0 && button < 3 ? 1 << button : 0;

        // Input updates and Editor GUI repaints have independent cadence. Retain
        // one bounded set of edges until a readable pointer repaint sees it.
        private static void CollectPointer()
        {
            if (InputState.currentUpdateType == InputUpdateType.BeforeRender ||
                InputState.currentUpdateType == InputUpdateType.Editor) return;
            Mouse mouse = Mouse.current;
            if (mouse != sampledMouse)
            {
                sampledMouse = mouse;
                hasPosition = false;
                capturedButton = 0;
                neutralRequired = true;
                pendingPress = pendingRelease = 0;
                pendingScroll = Vector2.zero;
            }
            if (!GameCursor.ApplicationFocused || mouse == null || Cursor.lockState == CursorLockMode.Locked)
            {
                capturedButton = 0;
                hasPosition = false;
                neutralRequired = true;
                pendingPress = pendingRelease = held = 0;
                pendingScroll = Vector2.zero;
                return;
            }
            held = 0;
            if (mouse.leftButton.isPressed) held |= 1;
            if (mouse.rightButton.isPressed) held |= 2;
            if (mouse.middleButton.isPressed) held |= 4;
            if (neutralRequired)
            {
                neutralRequired = held != 0;
                return;
            }
            if (mouse.leftButton.wasPressedThisFrame)
            {
                if ((pendingPress & 1) == 0)
                {
                    pendingPressPosition = mouse.position.ReadValue();
                    pendingPressPosition.y = Screen.height - pendingPressPosition.y;
                }
                pendingPress |= 1;
            }
            if (mouse.rightButton.wasPressedThisFrame) pendingPress |= 2;
            if (mouse.middleButton.wasPressedThisFrame) pendingPress |= 4;
            if (mouse.leftButton.wasReleasedThisFrame)
            {
                pendingRelease |= 1;
                pendingReleasePosition = mouse.position.ReadValue();
                pendingReleasePosition.y = Screen.height - pendingReleasePosition.y;
            }
            if (mouse.rightButton.wasReleasedThisFrame) pendingRelease |= 2;
            if (mouse.middleButton.wasReleasedThisFrame) pendingRelease |= 4;
            // Input System wheel ticks are +120 upwards; IMGUI used +3 downwards.
            pendingScroll -= mouse.scroll.ReadValue() * (3f / 120f);
            pendingScroll.x = Mathf.Clamp(pendingScroll.x, -300f, 300f);
            pendingScroll.y = Mathf.Clamp(pendingScroll.y, -300f, 300f);
        }

        private static void Sample()
        {
            if (sampledMouse != Mouse.current)
            {
                // Device replacement can also replace the Input System backend
                // (the isolated device harness does so). Rebind on this cold path.
                InputSystem.onAfterUpdate -= CollectPointer;
                InputSystem.onAfterUpdate += CollectPointer;
                CollectPointer();
            }
            if (sampledFrame != Time.frameCount)
            {
                sampledFrame = Time.frameCount;
                consumedPress = consumedRelease = consumedDrag = 0;
                scrollConsumed = false;
                pressed = released = 0;
                scroll = Vector2.zero;
                moved = false;
                if (GameCursor.ApplicationFocused && sampledMouse != null)
                {
                    screenPosition = sampledMouse.position.ReadValue();
                    screenPosition.y = Screen.height - screenPosition.y;
                    moved = hasPosition && (screenPosition - previousPosition).sqrMagnitude >= .25f;
                    if (!hasPosition || moved) previousPosition = screenPosition;
                    hasPosition = true;
                }
                if (capturedButton != 0 && !GameInput.CanRead(captureContext)) capturedButton = 0;
            }
            if (!IsPointerPass || sampledPaintFrame == Time.frameCount) return;
            sampledPaintFrame = Time.frameCount;
            pressed = pendingPress;
            released = pendingRelease;
            pressPosition = pendingPressPosition;
            releasePosition = pendingReleasePosition;
            scroll = pendingScroll;
            pendingPress = pendingRelease = 0;
            pendingScroll = Vector2.zero;
            // Missing repaints cannot expire a held capture. A vanished
            // control is discarded once its release has had a paint to finish.
            if (capturedButton != 0 && held == 0 && (released & 1) == 0)
            {
                capturedButton = 0;
            }
        }
    }
}

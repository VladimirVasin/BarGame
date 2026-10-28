using System;
using System.Text;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Controls;
using UnityEngine.InputSystem.LowLevel;

namespace BarPromenade
{
    /// <summary>
    /// One bounded text-editing owner. Input System supplies input; TextEditor
    /// retains native editing, selection and clipboard behaviour. No events are
    /// injected into IMGUI, and no legacy Input API is read.
    /// </summary>
    internal sealed class RetroUiTextField
    {
        private const int ControlHint = 0x52545854;
        private const float RepeatDelay = .45f, RepeatInterval = .05f;
        private static readonly Key[] EditKeys =
        {
            Key.LeftArrow, Key.RightArrow, Key.UpArrow, Key.DownArrow,
            Key.Home, Key.End, Key.PageUp, Key.PageDown, Key.Backspace, Key.Delete,
            Key.A, Key.C, Key.X, Key.V, Key.Z, Key.Insert
        };
        private static readonly KeyCode[] EditCodes =
        {
            KeyCode.LeftArrow, KeyCode.RightArrow, KeyCode.UpArrow, KeyCode.DownArrow,
            KeyCode.Home, KeyCode.End, KeyCode.PageUp, KeyCode.PageDown, KeyCode.Backspace, KeyCode.Delete,
            KeyCode.A, KeyCode.C, KeyCode.X, KeyCode.V, KeyCode.Z, KeyCode.Insert
        };

        private readonly int limit;
        private readonly StringBuilder pendingText;
        private readonly Event keyEvent = new Event { type = EventType.KeyDown };
        private readonly GUIContent content = new GUIContent();
        private TextEditor editor;
        private Keyboard keyboard;
        private KeyControl repeating;
        private KeyCode repeatCode;
        private float repeatAt;
        private int processedFrame = -1, controlId;
        private bool open, focused, focusRequested, dragging, previousImeSelected;
        private string composition = string.Empty, initialText = string.Empty;
        private string indexedText, indexedComposition;
        private int[] textElements = Array.Empty<int>(), compositionElements = Array.Empty<int>();

        internal RetroUiTextField(int maximumLength)
        {
            limit = Mathf.Max(1, maximumLength);
            pendingText = new StringBuilder(limit, limit);
        }

        internal void Open(string value)
        {
            Close();
            initialText = value ?? string.Empty;
            open = focusRequested = true;
            processedFrame = -1;
        }

        internal void Close()
        {
            open = focusRequested = focused = dragging = false;
            pendingText.Clear();
            composition = string.Empty;
            repeating = null;
            ReleaseKeyboard();
            editor?.OnLostFocus();
            if (controlId != 0 && GUIUtility.keyboardControl == controlId)
                GUIUtility.keyboardControl = 0;
        }

        // Called even while rendering/audio work is idle, so a lost focus or
        // removed keyboard cannot retain callbacks, composition or a held key.
        internal void Tick()
        {
            Keyboard next = open && focused && CanRead ? Keyboard.current : null;
            if (next == keyboard) return;
            ReleaseKeyboard();
            keyboard = next;
            if (keyboard == null) return;
            previousImeSelected = keyboard.imeSelected.isPressed;
            keyboard.onTextInput += OnTextInput;
            keyboard.onIMECompositionChange += OnComposition;
            keyboard.SetIMEEnabled(true);
        }

        private static bool CanRead => GameCursor.ApplicationFocused && GameInput.CanRead(GameInputContext.Menu);

        private void ReleaseKeyboard()
        {
            if (keyboard != null)
            {
                keyboard.onTextInput -= OnTextInput;
                keyboard.onIMECompositionChange -= OnComposition;
                if (keyboard.added) keyboard.SetIMEEnabled(previousImeSelected);
                keyboard = null;
            }
            pendingText.Clear();
            composition = string.Empty;
            repeating = null;
            dragging = false;
        }

        private void OnTextInput(char character)
        {
            if (!open || !focused || !CanRead || keyboard != Keyboard.current) return;
            // AltGr can produce real text with Ctrl+Alt held. Command shortcuts
            // and Ctrl+Enter never also insert their text callback.
            if (RetroUiInput.CommandHeld || RetroUiInput.ControlHeld && !RetroUiInput.AltHeld) return;
            if (character == '\r') character = '\n';
            if (char.IsControl(character) && character != '\n') return;
            if (pendingText.Length < limit) pendingText.Append(character);
            composition = string.Empty;
        }

        private void OnComposition(IMECompositionString value)
        {
            if (open && focused && CanRead && keyboard == Keyboard.current)
                composition = value.ToString();
        }

        internal string Draw(Rect rect, string value, GUIStyle style, out bool submit, out bool cancel)
        {
            submit = cancel = false;
            controlId = GUIUtility.GetControlID(ControlHint, FocusType.Keyboard, rect);
            if (Event.current.type != EventType.Repaint) return value;
            if (editor == null || editor.position != rect || !ReferenceEquals(editor.style, style))
            {
                int cursor = editor?.cursorIndex ?? 0, selection = editor?.selectIndex ?? 0;
                Vector2 scroll = editor?.scrollOffset ?? Vector2.zero;
                editor?.OnLostFocus();
                // Set metrics before assigning text: TextEditor's public text
                // setter rebuilds its native text handle for this style/rect.
                editor = new TextEditor { style = style, position = rect, controlID = controlId, isMultiline = true };
                editor.text = value ?? initialText;
                if (focused) editor.OnFocus();
                editor.cursorIndex = cursor;
                editor.selectIndex = selection;
                editor.scrollOffset = scroll;
                editor.SaveBackup();
            }
            else
            {
                editor.controlID = controlId;
                if (editor.text != value) { editor.text = value ?? string.Empty; editor.SaveBackup(); }
            }

            if (focusRequested)
            {
                focusRequested = false;
                SetFocused(true);
            }
            Tick();
            bool active = open && focused && CanRead && GUI.enabled;
            if (active) GUIUtility.keyboardControl = controlId;
            else if (GUIUtility.keyboardControl == controlId) GUIUtility.keyboardControl = 0;

            if (open && CanRead && GUI.enabled && processedFrame != Time.frameCount)
            {
                processedFrame = Time.frameCount;
                Keyboard current = Keyboard.current;
                cancel = current != null && current.escapeKey.wasPressedThisFrame;
                submit = current != null && RetroUiInput.ControlHeld &&
                    (current.enterKey.wasPressedThisFrame || current.numpadEnterKey.wasPressedThisFrame);
                ReadPointer(rect);
                if (!cancel && focused && keyboard != null)
                {
                    ReadEditKeys();
                    if (pendingText.Length != 0)
                    {
                        string input = pendingText.ToString();
                        pendingText.Clear();
                        InsertBounded(input);
                    }
                }
                if (cancel) pendingText.Clear();
            }
            // This public entry point invokes native scroll maintenance for a
            // direct event; it does not enqueue or replace Event.current.
            keyEvent.type = EventType.KeyDown;
            editor.UpdateScrollOffsetIfNeeded(keyEvent);
            Paint(rect, style, open && focused && CanRead && GUI.enabled);
            return editor.text;
        }

        private void SetFocused(bool value)
        {
            if (focused == value) return;
            focused = value;
            if (focused) editor.OnFocus();
            else editor.OnLostFocus();
            Tick();
        }

        private void ReadPointer(Rect rect)
        {
            Vector2 pointer = RetroUiInput.MousePosition;
            if (RetroUiInput.GetPointerPressed(0))
            {
                if (!rect.Contains(pointer)) { SetFocused(false); return; }
                SetFocused(true);
                content.text = editor.text;
                int cursor = editor.style.GetCursorStringIndex(rect, content, pointer + editor.scrollOffset);
                editor.cursorIndex = cursor;
                if (!RetroUiInput.ShiftHeld) editor.selectIndex = cursor;
                dragging = true;
                RetroUiInput.ConsumePointerPress();
            }
            if (!dragging) return;
            if (RetroUiInput.GetPointerDragged(0))
            {
                editor.SelectToPosition(pointer);
                RetroUiInput.ConsumePointerDrag();
            }
            if (RetroUiInput.GetPointerReleased(0))
            {
                dragging = false;
                RetroUiInput.ConsumePointerRelease();
            }
            else if (!RetroUiInput.GetPointerHeld(0)) dragging = false;
        }

        private void ReadEditKeys()
        {
            if (composition.Length != 0) { repeating = null; return; }
            bool primary = RetroUiInput.ControlHeld || RetroUiInput.CommandHeld;
            for (int index = 0; index < EditKeys.Length; index++)
            {
                KeyControl key = keyboard[EditKeys[index]];
                if (!key.wasPressedThisFrame || index >= 10 && !primary && EditKeys[index] != Key.Insert) continue;
                Edit(EditCodes[index]);
                repeating = index < 10 ? key : null;
                repeatCode = EditCodes[index];
                repeatAt = Time.unscaledTime + RepeatDelay;
            }
            if (repeating == null) return;
            if (!repeating.isPressed) { repeating = null; return; }
            if (repeating.wasPressedThisFrame || Time.unscaledTime < repeatAt) return;
            Edit(repeatCode);
            repeatAt = Time.unscaledTime + RepeatInterval;
        }

        private void Edit(KeyCode code)
        {
            bool primary = RetroUiInput.ControlHeld || RetroUiInput.CommandHeld;
            if (code == KeyCode.V && primary || code == KeyCode.Insert && RetroUiInput.ShiftHeld)
            { InsertBounded(GUIUtility.systemCopyBuffer); return; }
            if (code == KeyCode.A && primary) { editor.SelectAll(); return; }
            if (code == KeyCode.C && primary || code == KeyCode.Insert && RetroUiInput.ControlHeld)
            { editor.Copy(); return; }
            if (code == KeyCode.X && primary || code == KeyCode.Delete && RetroUiInput.ShiftHeld)
            { editor.SaveBackup(); editor.Cut(); return; }
            if (code == KeyCode.Z && primary) { editor.Undo(); return; }
            if (code == KeyCode.Backspace || code == KeyCode.Delete) editor.SaveBackup();
            keyEvent.type = EventType.KeyDown;
            keyEvent.keyCode = code;
            keyEvent.character = '\0';
            // Native TextEditor mappings distinguish function keys (including
            // arrows and Backspace/Delete) from ordinary character commands.
            keyEvent.modifiers = EventModifiers.FunctionKey |
                (RetroUiInput.ShiftHeld ? EventModifiers.Shift : EventModifiers.None) |
                (RetroUiInput.ControlHeld ? EventModifiers.Control : EventModifiers.None) |
                (RetroUiInput.CommandHeld ? EventModifiers.Command : EventModifiers.None) |
                (RetroUiInput.AltHeld ? EventModifiers.Alt : EventModifiers.None);
            editor.HandleKeyEvent(keyEvent);
        }

        private void InsertBounded(string input)
        {
            if (string.IsNullOrEmpty(input)) return;
            int available = limit - editor.text.Length + editor.SelectedText.Length;
            int length = Mathf.Min(available, input.Length);
            if (length > 0 && char.IsHighSurrogate(input[length - 1])) length--;
            if (length <= 0) return;
            editor.SaveBackup();
            editor.ReplaceSelection(length == input.Length ? input : input.Substring(0, length));
        }

        private void Paint(Rect rect, GUIStyle style, bool active)
        {
            content.text = editor.text;
            int displayCursor = editor.cursorIndex;
            if (active && composition.Length == 0) editor.DrawCursor(editor.text);
            else
            {
                Vector2 previousOffset = style.contentOffset;
                try
                {
                    style.contentOffset = previousOffset - editor.scrollOffset;
                    if (active)
                    {
                        int first = Mathf.Min(editor.cursorIndex, editor.selectIndex);
                        int start = StringIndex(editor.text, first);
                        int selectedLength = editor.SelectedText.Length;
                        int length = Mathf.Min(composition.Length, limit - editor.text.Length + selectedLength);
                        string preview = length == composition.Length ? composition : composition.Substring(0, Mathf.Max(0, length));
                        content.text = editor.text.Remove(start, selectedLength).Insert(start, preview);
                        displayCursor = first + CharacterCount(preview);
                        style.DrawWithTextSelection(rect, content, controlId, first, displayCursor);
                    }
                    else style.Draw(rect, content, false, false, false, false);
                }
                finally { style.contentOffset = previousOffset; }
            }
            if (active && keyboard != null)
            {
                Vector2 caret = style.GetCursorPixelPosition(rect, content, displayCursor) - editor.scrollOffset;
                caret.y += style.lineHeight;
                keyboard.SetIMECursorPosition(RetroUiInput.ToScreenPosition(caret));
            }
        }

        // Match TextEditor's ReplaceSelection contract: cursor indices count
        // grapheme text elements, while insertion/composition APIs use UTF-16.
        // Both caches are bounded by the field/IME text, and rebuilt only when
        // that text changes, not on every repaint.
        private int StringIndex(string text, int characters)
        {
            if (text != indexedText)
            {
                indexedText = text;
                textElements = System.Globalization.StringInfo.ParseCombiningCharacters(text);
            }
            return characters >= textElements.Length ? text.Length : textElements[Mathf.Max(0, characters)];
        }
        private int CharacterCount(string text)
        {
            if (text != indexedComposition)
            {
                indexedComposition = text;
                compositionElements = System.Globalization.StringInfo.ParseCombiningCharacters(text);
            }
            return compositionElements.Length;
        }
    }
}

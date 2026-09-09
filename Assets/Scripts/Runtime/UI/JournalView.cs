using UnityEngine;

namespace BarPromenade
{
    /// <summary>
    /// Draws the journal over the whole logical canvas: a list of
    /// quests, each with a box that is either ticked or empty, and one
    /// panel under it carrying the description of whichever line is
    /// selected.
    ///
    /// The old panel was 360x264 in the middle of the screen and put
    /// every description under its own title, which meant the page ran
    /// out of room after two quests and silently dropped the rest. A
    /// list that scrolls to its selection cannot run out.
    ///
    /// Layout is resolved by static functions rather than inside
    /// OnGUI, because that is the only part of an IMGUI surface a test
    /// can reach without a game view.
    /// </summary>
    public static class JournalView
    {
        public const float RowHeight = 20f;
        public const int VisibleRowCount = 7;
        private const float CheckboxSize = 9f;
        private const float StatusWidth = 92f;

        public static Rect ResolvePanelRect()
        {
            return RetroUiTheme.SnapRect(
                new Rect(24f, 20f, 592f, 320f));
        }

        public static Rect ResolveTitleRect()
        {
            Rect panel = ResolvePanelRect();
            return RetroUiTheme.SnapRect(
                new Rect(
                    panel.x + 16f,
                    panel.y + 10f,
                    panel.width - 32f,
                    28f));
        }

        public static Rect ResolveListRect()
        {
            Rect panel = ResolvePanelRect();
            return RetroUiTheme.SnapRect(
                new Rect(
                    panel.x + 16f,
                    panel.y + 46f,
                    panel.width - 32f,
                    RowHeight * VisibleRowCount + 8f));
        }

        /// <summary>
        /// The row at a visible slot, counted from the top of the list
        /// rather than from the top of the log - a scrolled list draws
        /// its fourth quest in its first slot.
        /// </summary>
        public static Rect ResolveRowRect(int slot)
        {
            Rect list = ResolveListRect();
            return RetroUiTheme.SnapRect(
                new Rect(
                    list.x,
                    list.y + slot * RowHeight,
                    list.width,
                    RowHeight - 2f));
        }

        public static Rect ResolveCheckboxRect(Rect row)
        {
            return RetroUiTheme.SnapRect(
                new Rect(
                    row.x + 4f,
                    row.y + (row.height - CheckboxSize) * 0.5f,
                    CheckboxSize,
                    CheckboxSize));
        }

        public static Rect ResolveRowTitleRect(Rect row)
        {
            Rect box = ResolveCheckboxRect(row);
            return RetroUiTheme.SnapRect(
                new Rect(
                    box.xMax + 8f,
                    row.y,
                    row.xMax - StatusWidth - 8f - (box.xMax + 8f),
                    row.height));
        }

        public static Rect ResolveRowStatusRect(Rect row)
        {
            return RetroUiTheme.SnapRect(
                new Rect(
                    row.xMax - StatusWidth,
                    row.y,
                    StatusWidth,
                    row.height));
        }

        public static Rect ResolveDescriptionRect()
        {
            Rect panel = ResolvePanelRect();
            return RetroUiTheme.SnapRect(
                new Rect(
                    panel.x + 16f,
                    panel.y + 198f,
                    panel.width - 32f,
                    92f));
        }

        public static Rect ResolveHintRect()
        {
            Rect panel = ResolvePanelRect();
            return RetroUiTheme.SnapRect(
                new Rect(
                    panel.x + 16f,
                    panel.yMax - 26f,
                    panel.width - 32f,
                    16f));
        }

        /// <summary>
        /// Which log entry the top slot shows. The window moves only as
        /// far as it must to keep the selection on screen, so paging
        /// through a long log does not jump.
        /// </summary>
        public static int ResolveFirstVisibleRow(
            int selectedIndex,
            int count)
        {
            if (count <= VisibleRowCount || selectedIndex < 0)
            {
                return 0;
            }

            int first = Mathf.Clamp(
                selectedIndex - VisibleRowCount / 2,
                0,
                count - VisibleRowCount);
            return first;
        }

        public static int ResolveVisibleRowCount(int count)
        {
            return Mathf.Min(count, VisibleRowCount);
        }

        /// <summary>
        /// The log index under a point in logical canvas space, or `-1`
        /// where the pointer is not on a row.
        /// </summary>
        public static int ResolveRowIndexAt(
            Vector2 logicalPoint,
            int selectedIndex,
            int count)
        {
            int first = ResolveFirstVisibleRow(selectedIndex, count);
            int visible = ResolveVisibleRowCount(count);
            for (int slot = 0; slot < visible; slot++)
            {
                if (ResolveRowRect(slot).Contains(logicalPoint))
                {
                    return first + slot;
                }
            }

            return -1;
        }

        public static void Draw(
            JournalMenuModel model,
            JournalStyles styles)
        {
            Rect panel = ResolvePanelRect();
            RetroUiTheme.DrawPanel(
                panel,
                RetroUiTheme.Panel,
                RetroUiTheme.BorderMuted,
                false,
                0f,
                1f);
            GUI.Label(
                ResolveTitleRect(),
                LocalizationService.Get("journal.title"),
                styles.Title);

            DrawList(model, styles);
            DrawDescription(model, styles);

            GUI.Label(
                ResolveHintRect(),
                LocalizationService.Get("journal.hint"),
                styles.Hint);
        }

        private static void DrawList(
            JournalMenuModel model,
            JournalStyles styles)
        {
            Rect list = ResolveListRect();
            if (model.Count == 0)
            {
                GUI.Label(
                    list,
                    LocalizationService.Get("journal.empty"),
                    styles.Empty);
                return;
            }

            int first = ResolveFirstVisibleRow(
                model.SelectedIndex,
                model.Count);
            int visible = ResolveVisibleRowCount(model.Count);
            for (int slot = 0; slot < visible; slot++)
            {
                int index = first + slot;
                QuestLogEntry entry = model.GetEntry(index);
                if (!QuestCatalog.TryGet(
                        entry.Id,
                        out QuestDefinition definition))
                {
                    continue;
                }

                Rect row = ResolveRowRect(slot);
                bool completed =
                    entry.Status == QuestStatus.Completed;
                if (index == model.SelectedIndex)
                {
                    RetroUiTheme.DrawSelection(row, true);
                }

                DrawCheckbox(ResolveCheckboxRect(row), completed);
                GUI.Label(
                    ResolveRowTitleRect(row),
                    LocalizationService.Get(
                        definition.TitleLocalizationKey),
                    completed ? styles.RowTitleDone : styles.RowTitle);
                GUI.Label(
                    ResolveRowStatusRect(row),
                    LocalizationService.Get(
                        completed
                            ? "journal.status.completed"
                            : "journal.status.active"),
                    completed
                        ? styles.StatusCompleted
                        : styles.StatusActive);
            }

            if (model.Count > visible)
            {
                DrawScrollNotch(list, first, visible, model.Count);
            }
        }

        /// <summary>
        /// A box and, when the work is done, a tick drawn as five small
        /// squares. Deliberately not a glyph: the interface font is
        /// dynamic and its atlas is rebuilt by whatever else drew this
        /// frame, while five rectangles land on the pixel grid every
        /// time.
        /// </summary>
        private static void DrawCheckbox(Rect box, bool completed)
        {
            RetroUiTheme.DrawPanel(
                box,
                RetroUiTheme.Ink,
                completed
                    ? RetroUiTheme.Accent
                    : RetroUiTheme.BorderMuted,
                false,
                0f,
                1f);
            if (!completed)
            {
                return;
            }

            RetroUiTheme.FillRect(
                new Rect(box.x + 2f, box.y + 4f, 2f, 2f),
                RetroUiTheme.Accent);
            RetroUiTheme.FillRect(
                new Rect(box.x + 3f, box.y + 5f, 2f, 2f),
                RetroUiTheme.Accent);
            RetroUiTheme.FillRect(
                new Rect(box.x + 4f, box.y + 4f, 2f, 2f),
                RetroUiTheme.Accent);
            RetroUiTheme.FillRect(
                new Rect(box.x + 5f, box.y + 3f, 2f, 2f),
                RetroUiTheme.Accent);
            RetroUiTheme.FillRect(
                new Rect(box.x + 6f, box.y + 2f, 2f, 2f),
                RetroUiTheme.Accent);
        }

        /// <summary>
        /// Says the list goes on. A bar rather than arrows, because
        /// arrows are two more controls to hit and this one is only
        /// ever read.
        /// </summary>
        private static void DrawScrollNotch(
            Rect list,
            int first,
            int visible,
            int count)
        {
            var track = new Rect(list.xMax + 4f, list.y, 3f, list.height);
            RetroUiTheme.FillRect(track, RetroUiTheme.Ink);
            float span = Mathf.Max(
                6f,
                track.height * visible / count);
            float travel = track.height - span;
            float offset = count > visible
                ? travel * first / (count - visible)
                : 0f;
            RetroUiTheme.FillRect(
                RetroUiTheme.SnapRect(
                    new Rect(track.x, track.y + offset, track.width, span)),
                RetroUiTheme.BorderMuted);
        }

        private static void DrawDescription(
            JournalMenuModel model,
            JournalStyles styles)
        {
            Rect description = ResolveDescriptionRect();
            RetroUiTheme.DrawPanel(
                description,
                RetroUiTheme.PanelInset,
                RetroUiTheme.BorderMuted,
                false,
                0f,
                1f);
            if (!model.TryGetSelectedDefinition(
                    out QuestDefinition definition,
                    out bool completed))
            {
                return;
            }

            GUI.Label(
                new Rect(
                    description.x + 12f,
                    description.y + 10f,
                    description.width - 24f,
                    description.height - 20f),
                LocalizationService.Get(
                    completed
                        ? definition.CompletedDescriptionLocalizationKey
                        : definition.ActiveDescriptionLocalizationKey),
                styles.Description);
        }
    }

    /// <summary>
    /// The styles the journal draws with, built once. GUIStyle cannot
    /// be allocated inside OnGUI without churning every frame, and the
    /// view is static, so they are carried in rather than cached there.
    /// </summary>
    public sealed class JournalStyles
    {
        public GUIStyle Title { get; private set; }
        public GUIStyle RowTitle { get; private set; }
        public GUIStyle RowTitleDone { get; private set; }
        public GUIStyle StatusActive { get; private set; }
        public GUIStyle StatusCompleted { get; private set; }
        public GUIStyle Description { get; private set; }
        public GUIStyle Empty { get; private set; }
        public GUIStyle Hint { get; private set; }

        public static JournalStyles Create()
        {
            return new JournalStyles
            {
                Title = RetroUiTheme.CreateLabelStyle(
                    22,
                    TextAnchor.MiddleCenter,
                    RetroUiTheme.Text,
                    true),
                RowTitle = RetroUiTheme.CreateLabelStyle(
                    13,
                    TextAnchor.MiddleLeft,
                    RetroUiTheme.Text,
                    true),
                RowTitleDone = RetroUiTheme.CreateLabelStyle(
                    13,
                    TextAnchor.MiddleLeft,
                    RetroUiTheme.Muted,
                    true),
                StatusActive = RetroUiTheme.CreateLabelStyle(
                    10,
                    TextAnchor.MiddleRight,
                    RetroUiTheme.Accent,
                    true),
                StatusCompleted = RetroUiTheme.CreateLabelStyle(
                    10,
                    TextAnchor.MiddleRight,
                    RetroUiTheme.Muted,
                    true),
                Description = RetroUiTheme.CreateLabelStyle(
                    11,
                    TextAnchor.UpperLeft,
                    RetroUiTheme.Muted,
                    false,
                    true),
                Empty = RetroUiTheme.CreateLabelStyle(
                    12,
                    TextAnchor.MiddleCenter,
                    RetroUiTheme.Muted,
                    false,
                    true),
                Hint = RetroUiTheme.CreateLabelStyle(
                    10,
                    TextAnchor.MiddleCenter,
                    RetroUiTheme.Muted,
                    true)
            };
        }
    }
}

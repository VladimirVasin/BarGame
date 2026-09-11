using System;
using System.Collections.Generic;

namespace BarPromenade
{
    public enum DialogueSpeaker { Npc, Hero }
    public enum DialogueNodeKind { Line, Choice, End }

    public sealed class DialogueChoice
    {
        public string TextKey { get; }
        public string NextId { get; }
        public DialogueChoice(string textKey, string nextId)
        { TextKey = textKey; NextId = nextId; }
    }

    /// <summary>Localized content and links only; presentation owns speech completion.</summary>
    public sealed class DialogueNode
    {
        public string Id { get; }
        public DialogueNodeKind Kind { get; }
        public DialogueSpeaker Speaker { get; }
        public string TextKey { get; }
        public string NextId { get; }
        public IReadOnlyList<DialogueChoice> Choices { get; }
        private DialogueNode(string id, DialogueNodeKind kind, DialogueSpeaker speaker,
            string textKey, string nextId, DialogueChoice[] choices)
        {
            Id = id; Kind = kind; Speaker = speaker; TextKey = textKey; NextId = nextId;
            Choices = Array.AsReadOnly(choices ?? Array.Empty<DialogueChoice>());
        }
        public static DialogueNode Line(string id, DialogueSpeaker speaker, string key, string nextId) =>
            new DialogueNode(id, DialogueNodeKind.Line, speaker, key, nextId, null);
        public static DialogueNode Choice(string id, params DialogueChoice[] choices) =>
            new DialogueNode(id, DialogueNodeKind.Choice, DialogueSpeaker.Hero, null, null,
                choices == null ? null : (DialogueChoice[])choices.Clone());
        public static DialogueNode End(string id) =>
            new DialogueNode(id, DialogueNodeKind.End, DialogueSpeaker.Npc, null, null, null);
    }

    public sealed class DialogueGraph
    {
        private readonly Dictionary<string, DialogueNode> nodes = new Dictionary<string, DialogueNode>();
        public string EntryId { get; }
        public DialogueGraph(string entryId, params DialogueNode[] content)
        {
            if (content == null || content.Length == 0) throw new ArgumentException("Dialogue is empty.");
            foreach (DialogueNode node in content)
            {
                if (node == null || string.IsNullOrWhiteSpace(node.Id) || nodes.ContainsKey(node.Id))
                    throw new ArgumentException("Dialogue node IDs must be nonempty and unique.");
                nodes.Add(node.Id, node);
            }
            RequireNode(entryId);
            EntryId = entryId;
            foreach (DialogueNode node in content)
            {
                if (node.Kind == DialogueNodeKind.Line)
                { RequireText(node.TextKey); RequireNode(node.NextId); }
                else if (node.Kind == DialogueNodeKind.Choice)
                {
                    if (node.Choices.Count == 0) throw new ArgumentException("A choice needs an answer.");
                    foreach (DialogueChoice choice in node.Choices)
                    {
                        if (choice == null) throw new ArgumentException("Null dialogue choice.");
                        RequireText(choice.TextKey); RequireNode(choice.NextId);
                    }
                }
            }
            // Every reachable branch must have a way out, including deliberate choice loops.
            var canEnd = new HashSet<string>();
            foreach (DialogueNode node in content) if (node.Kind == DialogueNodeKind.End) canEnd.Add(node.Id);
            bool changed;
            do
            {
                changed = false;
                foreach (DialogueNode node in content)
                {
                    bool reaches = node.Kind == DialogueNodeKind.Line && canEnd.Contains(node.NextId);
                    foreach (DialogueChoice choice in node.Choices) reaches |= canEnd.Contains(choice.NextId);
                    if (reaches) changed |= canEnd.Add(node.Id);
                }
            } while (changed);
            foreach (DialogueNode node in content)
                if (!canEnd.Contains(node.Id)) throw new ArgumentException("Dialogue branch has no exit: " + node.Id);
        }
        public DialogueNode Get(string id) => nodes[id];
        private void RequireNode(string id)
        { if (string.IsNullOrWhiteSpace(id) || !nodes.ContainsKey(id)) throw new ArgumentException("Missing dialogue link: " + id); }
        private static void RequireText(string key)
        { if (string.IsNullOrWhiteSpace(key)) throw new ArgumentException("Missing dialogue localization key."); }
    }

    public sealed class DialogueCursor
    {
        private readonly DialogueGraph graph;
        public DialogueNode Current { get; private set; }
        public DialogueCursor(DialogueGraph definition)
        { graph = definition ?? throw new ArgumentNullException(nameof(definition)); Current = graph.Get(graph.EntryId); }
        public bool CompleteLine()
        {
            if (Current.Kind != DialogueNodeKind.Line) return false;
            Current = graph.Get(Current.NextId); return true;
        }
        public bool Select(int index)
        {
            if (Current.Kind != DialogueNodeKind.Choice || index < 0 || index >= Current.Choices.Count) return false;
            Current = graph.Get(Current.Choices[index].NextId); return true;
        }
    }
}

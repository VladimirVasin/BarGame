using System.Collections.Generic;

namespace BarPromenade
{
    public static class RidgeKioskDialogueCatalog
    {
        public const int RoundCount = 3;
        private static readonly DialogueGraph[] graphs =
        {
            Build("politics"),
            Build("philosophy"),
            Build("misogyny", secondRound: "BAA")
        };

        public static DialogueGraph ForTable(int table) => graphs[table];
        public static string RoundEntry(int round) => "round" + (round + 1);

        private static DialogueGraph Build(string topic, string secondRound = "ABA")
        {
            var nodes = new List<DialogueNode>(2 + RoundCount * 9)
            {
                DialogueNode.Line("welcome", DialogueSpeaker.Npc, "kiosk.dialogue.welcome", RoundEntry(0)),
                DialogueNode.End("end")
            };
            for (int round = 0; round < RoundCount; round++)
            {
                string id = RoundEntry(round), key = "kiosk.dialogue." + topic + "." + (round + 1);
                string next = round + 1 < RoundCount ? RoundEntry(round + 1) : "end";
                nodes.Add(DialogueNode.Line(id, DialogueSpeaker.Npc, key + ".a", id + "q"));
                nodes.Add(DialogueNode.Line(id + "q", DialogueSpeaker.SecondNpc, key + ".q", id + "c"));
                var choices = new DialogueChoice[3];
                for (int choice = 0; choice < choices.Length; choice++)
                {
                    string answer = id + "a" + choice, reaction = id + "r" + choice;
                    string answerKey = key + ".a" + choice;
                    choices[choice] = new DialogueChoice(answerKey, answer);
                    nodes.Add(DialogueNode.Line(answer, DialogueSpeaker.Hero, answerKey, reaction));
                    nodes.Add(DialogueNode.Line(reaction,
                        (round == 1 ? secondRound : "ABA")[choice] == 'A' ? DialogueSpeaker.Npc : DialogueSpeaker.SecondNpc,
                        key + ".r" + choice, next));
                }
                nodes.Add(DialogueNode.Choice(id + "c", choices));
            }
            return new DialogueGraph("welcome", nodes.ToArray());
        }
    }
}

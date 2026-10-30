using System;

namespace BarPromenade
{
    public enum StairwellLabyrinthPuzzleAction
    {
        Roll0, Roll1, Roll2, ClothBolt,
        Shutter, ShutterStop, WeightSwitch, CarriageSend,
        Bird0, Bird1, Bird2, BirdLift
    }

    public enum StairwellLabyrinthPuzzleOutcome { Blocked, Changed, Solved }

    /// <summary>Finite, retained mechanisms. A preview never commits progress or consumes an item.</summary>
    public sealed class StairwellLabyrinthPuzzleState
    {
        public readonly struct Snapshot
        {
            public readonly int Roll0, Roll1, Roll2, Bird0, Bird1, Bird2;
            public readonly bool ShutterRaised, StopEngaged, WeightOnCarriage;
            public readonly int SolvedStages;
            internal Snapshot(int roll0, int roll1, int roll2, int bird0, int bird1, int bird2,
                bool raised, bool stopped, bool carriage, int solved)
            {
                Roll0 = roll0; Roll1 = roll1; Roll2 = roll2;
                Bird0 = bird0; Bird1 = bird1; Bird2 = bird2;
                ShutterRaised = raised; StopEngaged = stopped; WeightOnCarriage = carriage;
                SolvedStages = solved;
            }
            public int Roller(int index) => index == 0 ? Roll0 : index == 1 ? Roll1 : index == 2 ? Roll2 : throw new ArgumentOutOfRangeException(nameof(index));
            public int Bird(int index) => index == 0 ? Bird0 : index == 1 ? Bird1 : index == 2 ? Bird2 : throw new ArgumentOutOfRangeException(nameof(index));
            public bool CanPassGate(int stage) => stage >= 1 && stage <= 3 && SolvedStages >= stage;
        }

        private Snapshot snapshot = Initial();
        public Snapshot Current => snapshot;
        public int Revision { get; private set; }
        public int SolvedStages => snapshot.SolvedStages;
        public bool AllSolved => SolvedStages == 3;
        public bool CanPassGate(int stage) => snapshot.CanPassGate(stage);
        public bool CanEnterStage(int stage) => stage >= 0 && stage <= 3 && (stage <= 1 || CanPassGate(stage - 1));

        public static int StageOf(StairwellLabyrinthPuzzleAction action) => (int)action < 0 || (int)action > 11
            ? 0 : (int)action < 4 ? 1 : (int)action < 8 ? 2 : 3;

        public bool CanOperate(StairwellLabyrinthPuzzleAction action)
        {
            int stage = StageOf(action);
            return stage != 0 && stage == SolvedStages + 1;
        }

        public StairwellLabyrinthPuzzleOutcome Preview(StairwellLabyrinthPuzzleAction action, out Snapshot next)
        {
            next = snapshot;
            if (!CanOperate(action)) return StairwellLabyrinthPuzzleOutcome.Blocked;
            int r0 = snapshot.Roll0, r1 = snapshot.Roll1, r2 = snapshot.Roll2;
            int b0 = snapshot.Bird0, b1 = snapshot.Bird1, b2 = snapshot.Bird2, solved = SolvedStages;
            bool raised = snapshot.ShutterRaised, stopped = snapshot.StopEngaged, carriage = snapshot.WeightOnCarriage;
            switch (action)
            {
                case StairwellLabyrinthPuzzleAction.Roll0: r0 = (r0 + 1) % 3; break;
                case StairwellLabyrinthPuzzleAction.Roll1: r1 = (r1 + 1) % 3; break;
                case StairwellLabyrinthPuzzleAction.Roll2: r2 = (r2 + 1) % 3; break;
                case StairwellLabyrinthPuzzleAction.ClothBolt:
                    if (r0 != 0 || r1 != 0 || r2 != 0) return StairwellLabyrinthPuzzleOutcome.Blocked;
                    solved = 1; break;
                case StairwellLabyrinthPuzzleAction.Shutter:
                    if (carriage || stopped) return StairwellLabyrinthPuzzleOutcome.Blocked;
                    raised = !raised; break;
                case StairwellLabyrinthPuzzleAction.ShutterStop:
                    if (!raised) return StairwellLabyrinthPuzzleOutcome.Blocked;
                    stopped = !stopped;
                    if (!stopped && carriage) raised = false;
                    break;
                case StairwellLabyrinthPuzzleAction.WeightSwitch:
                    carriage = !carriage;
                    if (carriage && !stopped) raised = false;
                    break;
                case StairwellLabyrinthPuzzleAction.CarriageSend:
                    if (!raised || !stopped || !carriage) return StairwellLabyrinthPuzzleOutcome.Blocked;
                    solved = 2; break;
                case StairwellLabyrinthPuzzleAction.Bird0: b0 = (b0 + 1) % 3; break;
                case StairwellLabyrinthPuzzleAction.Bird1: b1 = (b1 + 1) % 3; break;
                case StairwellLabyrinthPuzzleAction.Bird2: b2 = (b2 + 1) % 3; break;
                case StairwellLabyrinthPuzzleAction.BirdLift:
                    if (b0 != 0 || b1 != 0 || b2 != 0) return StairwellLabyrinthPuzzleOutcome.Blocked;
                    solved = 3; break;
                default: return StairwellLabyrinthPuzzleOutcome.Blocked;
            }
            next = new Snapshot(r0, r1, r2, b0, b1, b2, raised, stopped, carriage, solved);
            return solved > SolvedStages ? StairwellLabyrinthPuzzleOutcome.Solved : StairwellLabyrinthPuzzleOutcome.Changed;
        }

        public bool TryApply(StairwellLabyrinthPuzzleAction action)
        {
            if (Preview(action, out Snapshot next) == StairwellLabyrinthPuzzleOutcome.Blocked) return false;
            snapshot = next;
            Revision = Revision == int.MaxValue ? 1 : Revision + 1;
            return true;
        }

        public bool TryCommit(int expectedRevision, StairwellLabyrinthPuzzleAction action) =>
            Revision == expectedRevision && TryApply(action);

        public void Reset()
        {
            snapshot = Initial();
            Revision = Revision == int.MaxValue ? 1 : Revision + 1;
        }
        private static Snapshot Initial() => new Snapshot(1, 2, 1, 1, 2, 1, false, false, false, 0);
    }
}

namespace BarPromenade
{
    /// <summary>
    /// The street's place in the pool. A bag of all twenty lines dealt in
    /// a random order and refilled only when it runs out: the hero hears
    /// every line the street has before he hears any of them a second
    /// time, and never the same one twice running.
    ///
    /// Peek and take are separate because the line is chosen before the
    /// shared bubble view has agreed to show it. A refused line is not a
    /// line the street said, and it must not cost the bag a card.
    /// </summary>
    public sealed class CityPedestrianInsultWalk
    {
        private readonly int[] order;
        private uint state;
        private int cursor;
        private int previousIndex = -1;

        public CityPedestrianInsultWalk(uint seedState)
        {
            state = seedState == 0u ? 0x9E3779B9u : seedState;
            order = new int[CityPedestrianInsultLines.LineKeys.Length];
            if (order.Length > 0)
            {
                CityPedestrianInsultLines.Shuffle(ref state, order, previousIndex);
            }
        }

        /// <summary>The line last taken, or <c>-1</c> before the first.</summary>
        public int PreviousIndex => previousIndex;

        /// <summary>How many of the twenty are still in the bag.</summary>
        public int Remaining => order.Length - cursor;

        /// <summary>The line the street would say next, refilling the bag
        /// when it has run out. Does not spend it.</summary>
        public int Peek()
        {
            if (order.Length == 0)
            {
                return -1;
            }

            if (cursor >= order.Length)
            {
                CityPedestrianInsultLines.Shuffle(ref state, order, previousIndex);
                cursor = 0;
            }

            return order[cursor];
        }

        /// <summary>Spends the line and returns it.</summary>
        public int Take()
        {
            int index = Peek();
            if (index < 0)
            {
                return -1;
            }

            cursor++;
            previousIndex = index;
            return index;
        }
    }
}

using System;

namespace BarPromenade
{
    public sealed class InventoryMenuModel
    {
        public int SelectedItemIndex { get; private set; }
        public bool IsExamining { get; private set; }

        public void Open(int itemCount)
        {
            SelectedItemIndex = ClampIndex(SelectedItemIndex, itemCount);
            IsExamining = false;
        }

        public bool SelectItem(int index, int itemCount)
        {
            if (index < 0 || index >= itemCount)
            {
                return false;
            }

            SelectedItemIndex = index;
            IsExamining = false;
            return true;
        }

        public bool MoveSelection(int delta, int itemCount)
        {
            if (delta == 0 || itemCount <= 0)
            {
                return false;
            }

            SelectedItemIndex = Wrap(
                SelectedItemIndex + delta,
                itemCount);
            IsExamining = false;
            return true;
        }

        public bool BeginExamine(int itemCount)
        {
            if (itemCount <= 0 ||
                SelectedItemIndex < 0 ||
                SelectedItemIndex >= itemCount)
            {
                return false;
            }

            IsExamining = true;
            return true;
        }

        public bool CancelExamine()
        {
            if (!IsExamining)
            {
                return false;
            }

            IsExamining = false;
            return true;
        }

        private static int ClampIndex(int index, int itemCount)
        {
            if (itemCount <= 0)
            {
                return -1;
            }

            return Math.Max(0, Math.Min(index, itemCount - 1));
        }

        private static int Wrap(int index, int count)
        {
            int wrapped = index % count;
            return wrapped < 0 ? wrapped + count : wrapped;
        }
    }
}

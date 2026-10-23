using System;
using UnityEngine;
using Object = UnityEngine.Object;

namespace BarPromenade
{
    /// <summary>One worker, one owner and a bounded FIFO for the hero and two counter guests.</summary>
    public sealed class BarBartenderServiceQueue
    {
        public const int Capacity = 3;
        private readonly Object[] owners = new Object[Capacity];
        private readonly Action[] grants = new Action[Capacity];
        private readonly long[] sequences = new long[Capacity];
        private int count;
        private long nextSequence;
        private bool dispatching;

        public Object CurrentOwner { get; private set; }
        public long CurrentSequence { get; private set; }
        public int QueuedCount => count;
        public int CompletedCount { get; private set; }
        public bool IsBusy => CurrentOwner != null;
        public Object GetQueuedOwner(int index) => index >= 0 && index < count
            ? owners[index] : throw new ArgumentOutOfRangeException(nameof(index));

        public bool RequestHero(Object owner, Action onGranted) => Request(owner, onGranted);
        public bool RequestPatron(BarCounterPatronRefill owner, Action onGranted) => Request(owner, onGranted);
        public bool IsOwner(Object owner) => owner != null && ReferenceEquals(CurrentOwner, owner);
        public bool Contains(Object owner)
        {
            if (IsOwner(owner)) return true;
            for (int index = 0; index < count; index++)
                if (ReferenceEquals(owners[index], owner)) return true;
            return false;
        }

        private bool Request(Object owner, Action onGranted)
        {
            if (owner == null) throw new ArgumentNullException(nameof(owner));
            if (onGranted == null) throw new ArgumentNullException(nameof(onGranted));
            if (IsOwner(owner)) return true;
            for (int index = 0; index < count; index++)
                if (ReferenceEquals(owners[index], owner)) return true;
            if (count + (CurrentOwner != null ? 1 : 0) >= Capacity) return false;
            owners[count] = owner;
            grants[count] = onGranted;
            sequences[count++] = ++nextSequence;
            Dispatch();
            return true;
        }

        public bool Release(Object owner)
        {
            if (!IsOwner(owner)) return false;
            CurrentOwner = null;
            CurrentSequence = 0;
            CompletedCount++;
            Dispatch();
            return true;
        }

        public bool Cancel(Object owner)
        {
            if (ReferenceEquals(owner, null)) return false;
            if (ReferenceEquals(CurrentOwner, owner))
            {
                CurrentOwner = null;
                CurrentSequence = 0;
                Dispatch();
                return true;
            }
            for (int index = 0; index < count; index++)
                if (ReferenceEquals(owners[index], owner)) { Remove(index); return true; }
            return false;
        }

        public void Clear()
        {
            CurrentOwner = null;
            CurrentSequence = 0;
            while (count > 0) Remove(count - 1);
        }

        private void Dispatch()
        {
            if (dispatching) return;
            dispatching = true;
            try
            {
                while (CurrentOwner == null && count > 0)
                {
                    Object owner = owners[0];
                    Action grant = grants[0];
                    long sequence = sequences[0];
                    Remove(0);
                    if (owner == null) continue;
                    CurrentOwner = owner;
                    CurrentSequence = sequence;
                    grant();
                }
            }
            finally { dispatching = false; }
        }

        private void Remove(int index)
        {
            count--;
            for (int next = index; next < count; next++)
            {
                owners[next] = owners[next + 1];
                grants[next] = grants[next + 1];
                sequences[next] = sequences[next + 1];
            }
            owners[count] = null;
            grants[count] = null;
            sequences[count] = 0;
        }
    }
}

using System;
using System.Collections.Generic;

namespace BarPromenade
{
    /// <summary>
    /// Transient vehicle ownership, separate from saved journey progression.
    /// A lease from an unloaded scene cannot release a later session's ride.
    /// </summary>
    public sealed class VehicleActivityState
    {
        private readonly HashSet<long> cablewayOwners = new HashSet<long>();
        private object generation = new object();
        private long nextOwner;
        private bool legacyCablewayActive;

        public bool IsRidingCableway =>
            legacyCablewayActive || cablewayOwners.Count != 0;

        public IDisposable AcquireCablewayRide()
        {
            long owner = ++nextOwner;
            cablewayOwners.Add(owner);
            return new CablewayLease(this, generation, owner);
        }

        /// <summary>Compatibility for explicit debug/test state overrides.</summary>
        public void SetCablewayActive(bool active)
        {
            if (!active)
            {
                Reset();
                return;
            }

            legacyCablewayActive = true;
        }

        public void Reset()
        {
            generation = new object();
            cablewayOwners.Clear();
            nextOwner = 0;
            legacyCablewayActive = false;
        }

        private sealed class CablewayLease : IDisposable
        {
            private VehicleActivityState state;
            private readonly object generation;
            private readonly long owner;

            public CablewayLease(
                VehicleActivityState state, object generation, long owner)
            {
                this.state = state;
                this.generation = generation;
                this.owner = owner;
            }

            public void Dispose()
            {
                if (state == null)
                {
                    return;
                }

                if (ReferenceEquals(state.generation, generation))
                {
                    state.cablewayOwners.Remove(owner);
                }

                state = null;
            }
        }
    }
}

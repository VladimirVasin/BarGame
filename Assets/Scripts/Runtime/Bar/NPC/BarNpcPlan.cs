using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace BarPromenade
{
    public sealed class BarNpcPlan
    {
        internal BarNpcPlan(
            int citySeed,
            uint stableSeed,
            string barId,
            BarActivityKind activity,
            int desiredCount,
            IList<BarNpcDefinition> definitions)
        {
            CitySeed = citySeed;
            StableSeed = stableSeed;
            BarId = barId;
            Activity = activity;
            DesiredCount = desiredCount;
            Definitions = new ReadOnlyCollection<BarNpcDefinition>(
                new List<BarNpcDefinition>(definitions));
        }

        public int CitySeed { get; }
        public uint StableSeed { get; }
        public string BarId { get; }
        public BarActivityKind Activity { get; }
        public int DesiredCount { get; }
        public IReadOnlyList<BarNpcDefinition> Definitions { get; }
        public int Count => Definitions.Count;
    }
}

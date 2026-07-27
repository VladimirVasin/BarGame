using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace BarPromenade
{
    public sealed class CityNightFixturePlan
    {
        internal CityNightFixturePlan(
            IList<StreetLampDescriptor> streetLamps,
            IList<TrafficSignalDescriptor> trafficSignals)
        {
            StreetLamps = new ReadOnlyCollection<StreetLampDescriptor>(
                new List<StreetLampDescriptor>(streetLamps));
            TrafficSignals = new ReadOnlyCollection<TrafficSignalDescriptor>(
                new List<TrafficSignalDescriptor>(trafficSignals));
        }

        public IReadOnlyList<StreetLampDescriptor> StreetLamps { get; }
        public IReadOnlyList<TrafficSignalDescriptor> TrafficSignals { get; }
    }
}

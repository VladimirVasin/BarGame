using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace BarPromenade
{
    public sealed class CityDecorationPlan
    {
        public const int MaximumDescriptorCount = 420;

        private readonly ReadOnlyDictionary<
            CityDecorationKind,
            int> countsByKind;
        private readonly ReadOnlyDictionary<
            CityDistrictKind,
            int> countsByDistrict;
        private readonly ReadOnlyDictionary<
            CityDecorationAnchorKind,
            int> countsByAnchorKind;

        public CityDecorationPlan(
            int seed,
            IReadOnlyList<CityDecorationDescriptor> descriptors)
        {
            if (descriptors == null)
            {
                throw new ArgumentNullException(nameof(descriptors));
            }

            Seed = seed;
            var copy = new List<CityDecorationDescriptor>(
                descriptors.Count);
            for (int index = 0; index < descriptors.Count; index++)
            {
                copy.Add(descriptors[index]);
            }

            copy.Sort(CompareByStableId);
            Descriptors = new ReadOnlyCollection<CityDecorationDescriptor>(
                copy);

            var kindCounts =
                new Dictionary<CityDecorationKind, int>();
            var districtCounts =
                new Dictionary<CityDistrictKind, int>();
            var anchorCounts =
                new Dictionary<CityDecorationAnchorKind, int>();
            for (int index = 0; index < copy.Count; index++)
            {
                CityDecorationDescriptor descriptor = copy[index];
                Increment(kindCounts, descriptor.Kind);
                Increment(districtCounts, descriptor.District);
                Increment(anchorCounts, descriptor.AnchorKind);
            }

            countsByKind =
                new ReadOnlyDictionary<CityDecorationKind, int>(
                    kindCounts);
            countsByDistrict =
                new ReadOnlyDictionary<CityDistrictKind, int>(
                    districtCounts);
            countsByAnchorKind =
                new ReadOnlyDictionary<CityDecorationAnchorKind, int>(
                    anchorCounts);
        }

        public int Seed { get; }
        public IReadOnlyList<CityDecorationDescriptor> Descriptors { get; }
        public IReadOnlyList<CityDecorationDescriptor> Decorations =>
            Descriptors;
        public int Count => Descriptors.Count;
        public int TotalCount => Count;
        public IReadOnlyDictionary<CityDecorationKind, int> CountsByKind =>
            countsByKind;
        public IReadOnlyDictionary<CityDistrictKind, int>
            CountsByDistrict => countsByDistrict;
        public IReadOnlyDictionary<CityDecorationAnchorKind, int>
            CountsByAnchorKind => countsByAnchorKind;

        public int GetCount(CityDecorationKind kind)
        {
            return countsByKind.TryGetValue(kind, out int count)
                ? count
                : 0;
        }

        public int GetCount(CityDistrictKind district)
        {
            return countsByDistrict.TryGetValue(district, out int count)
                ? count
                : 0;
        }

        public int GetCount(CityDecorationAnchorKind anchorKind)
        {
            return countsByAnchorKind.TryGetValue(
                    anchorKind,
                    out int count)
                ? count
                : 0;
        }

        public void ValidateOrThrow(CityLayout layout)
        {
            CityDecorationValidator.ValidateOrThrow(this, layout);
        }

        public void ValidateOrThrow(
            CityLayout layout,
            RoadFencePlan fencePlan,
            CityNightFixturePlan nightPlan)
        {
            CityDecorationValidator.ValidateOrThrow(
                this,
                layout,
                fencePlan,
                nightPlan);
        }

        private static void Increment<T>(
            IDictionary<T, int> counts,
            T key)
        {
            counts[key] = counts.TryGetValue(key, out int count)
                ? count + 1
                : 1;
        }

        private static int CompareByStableId(
            CityDecorationDescriptor left,
            CityDecorationDescriptor right)
        {
            return string.Compare(
                left.StableId,
                right.StableId,
                StringComparison.Ordinal);
        }
    }
}

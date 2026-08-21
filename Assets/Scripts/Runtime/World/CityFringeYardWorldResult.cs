using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using UnityEngine;

namespace BarPromenade
{
    public sealed class CityFringeYardWorldResult
    {
        internal CityFringeYardWorldResult(
            GameObject root,
            IList<CityFringePracticalAnchor> practicalAnchors)
        {
            Root = root != null
                ? root
                : throw new ArgumentNullException(nameof(root));
            PracticalAnchors = new ReadOnlyCollection<
                CityFringePracticalAnchor>(
                new List<CityFringePracticalAnchor>(practicalAnchors));
        }

        public GameObject Root { get; }
        public IReadOnlyList<CityFringePracticalAnchor> PracticalAnchors
        {
            get;
        }
    }
}

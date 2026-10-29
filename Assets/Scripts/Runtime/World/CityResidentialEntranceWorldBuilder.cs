using UnityEngine;

namespace BarPromenade
{
    /// <summary>The same finite entrance paving feeds City and its passive Home view.</summary>
    internal static class CityResidentialEntranceWorldBuilder
    {
        internal const string RootName = "Residential Entrance Walkways";

        internal static void Build(Transform parent, CityLayout layout, HomeExteriorContextPlan home = null)
        {
            Transform root = new GameObject(RootName).transform;
            root.SetParent(parent, false);
            int index = 0;
            foreach (var entrance in CityResidentialEntrancePlan.ForLayout(layout).Entrances)
            {
                bool nearby = home == null;
                if (home != null)
                    foreach (BuildingLot lot in home.NearbyLots)
                        if (ReferenceEquals(lot, entrance.Lot)) { nearby = true; break; }
                if (!nearby) continue;
                foreach (CityEntranceForecourtPlan paving in entrance.Paving)
                    CityEntranceForecourtWorldBuilder.Build("Residential Entrance " + index++, root, paving, home);
            }
        }
    }
}

using System;
using System.Collections.Generic;

namespace BarPromenade
{
    /// <summary>
    /// The two places on the summit the hero can sit, in the shape the
    /// city's own sit offer already understands.
    ///
    /// Nothing here is new: <see cref="CityBenchSitInteraction"/>, its
    /// plan, its three clips and its prompt are all shared, and neither
    /// knows or cares which area it is in. A bench is the bus seat without
    /// the bus, and a counter stool is a bench with a counter behind it.
    /// </summary>
    public static class MountainRoadSeatPlanner
    {
        public static List<CityBenchSitPlan> CreateAll(
            MountainRoadPlan plan)
        {
            if (plan == null)
            {
                throw new ArgumentNullException(nameof(plan));
            }

            MountainRoadTerminalSitePlan site = plan.Terminal.Site;
            var plans = new List<CityBenchSitPlan>(2);
            if (site == null)
            {
                return plans;
            }

            // The bench's ground is the TERRACE, not the yard. The offer
            // compares the player's own height against its dock within
            // `0.35 m`, and the terrace stands `0.66 m` over the yard: get
            // this from the wrong datum and the bench shows a prompt that
            // never seats anybody.
            plans.Add(new CityBenchSitPlan(site.BrinkSeat.ToBenchSeat()));
            plans.Add(new CityBenchSitPlan(site.CounterSeat.ToBenchSeat()));
            return plans;
        }
    }
}

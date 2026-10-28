using System;
using System.Collections.Generic;

namespace BarPromenade
{
    /// <summary>
    /// The summit bench in the shape the city's sit offer understands.
    ///
    /// Uses the same seat plan, clips and prompt as City. The optional
    /// counter descriptor remains supported for standalone authored contracts;
    /// the production mountain site contains only its brink bench.
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
            if (!string.IsNullOrEmpty(site.CounterSeat.StableId))
            {
                plans.Add(new CityBenchSitPlan(site.CounterSeat.ToBenchSeat()));
            }
            return plans;
        }
    }
}

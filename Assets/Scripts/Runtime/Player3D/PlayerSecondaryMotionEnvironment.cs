using UnityEngine;

namespace BarPromenade
{
    /// <summary>The player's shared wind exposure for secondary hair and clothing motion.</summary>
    internal sealed class PlayerSecondaryMotionEnvironment
    {
        private readonly PlayerRuntime runtime;
        private readonly CityGameRoot city;
        private readonly MountainRoadRoot road;
        private readonly AlpineVillageRoot village;
        private readonly HomeInteriorRoot home;

        public PlayerSecondaryMotionEnvironment(PlayerRuntime owner)
        {
            runtime = owner;
            city = owner.GameObject.GetComponentInParent<CityGameRoot>();
            road = owner.GameObject.GetComponentInParent<MountainRoadRoot>();
            village = owner.GameObject.GetComponentInParent<AlpineVillageRoot>();
            home = owner.GameObject.GetComponentInParent<HomeInteriorRoot>();
        }

        public void Sample(out bool outside, out WindSample wind)
        {
            outside = false;
            wind = new WindSample(0f, 0f);
            if (city != null && city.Weather != null) { outside = true; wind = city.Weather.CurrentWind; }
            else if (road != null && road.Weather != null)
            { outside = road.CabinSeat == null || !road.CabinSeat.IsSeated; wind = road.Weather.CurrentWind; }
            else if (village != null && village.Weather != null)
            {
                outside = (village.CabinSeat == null || !village.CabinSeat.IsSeated) &&
                    (village.Workroom == null || !village.Workroom.Environment.IsInside);
                wind = village.Weather.CurrentWind;
            }
            else if (home != null && home.BalconyLayout != null)
            {
                Vector3 position = home.transform.InverseTransformPoint(runtime.GameObject.transform.position);
                outside = home.BalconyLayout.BalconyBounds.Contains(new Vector2(position.x, position.z));
                if (outside) wind = GameWeatherRules.EvaluateCurrentWind();
            }
            outside &= !GameSessionState.IsRidingAVehicle && !SceneTransitionService.IsTransitioning;
        }
    }
}

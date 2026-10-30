using System;
using UnityEngine;

namespace BarPromenade
{
    /// <summary>The imported laundry shares one rigid metre pose with its pure site.</summary>
    public static class CityLaundryWorldBuilder
    {
        public static GameObject Build(Transform parent, CityLaundryPlan plan)
        {
            if (parent == null) throw new ArgumentNullException(nameof(parent));
            if (plan == null) throw new ArgumentNullException(nameof(plan));
            if (!plan.IsEnabled) return null;
            GameObject model = CityLaundryAssetProvider.Create(parent);
            model.name = "Residential Laundry";
            model.transform.SetPositionAndRotation(plan.Position, plan.Rotation);
            model.AddComponent<CityLaundryController>().Initialize(plan, model.transform);
            return model;
        }

        public static GameObject BuildHomeExterior(Transform parent, HomeExteriorContextPlan context)
        {
            if (parent == null) throw new ArgumentNullException(nameof(parent));
            if (context == null) throw new ArgumentNullException(nameof(context));
            CityLaundryPlan plan = CityLaundryPlan.ForLayout(context.Layout);
            if (!plan.IsEnabled) return null;
            Vector3 local = PlayerHomeBalconyGeometry.ToHomeLocal(context.PlayerHome, plan.Position);
            float radius = Mathf.Sqrt(CityLaundryPlan.Width * CityLaundryPlan.Width +
                CityLaundryPlan.Depth * CityLaundryPlan.Depth) * .5f;
            if (local.x - radius < HomeExteriorViewBuilder.ContextMinimumX ||
                new Vector2(local.x - PlayerHomeBalconyGeometry.HomeFacadeX, local.z).magnitude + radius > HomeExteriorContextPlanner.ViewRadius) return null;
            GameObject model = CityLaundryAssetProvider.CreateStatic(parent);
            model.name = "Home Residential Laundry";
            model.transform.localPosition = local;
            model.transform.localRotation = Quaternion.LookRotation(
                PlayerHomeBalconyGeometry.ToHomeLocalDirection(context.PlayerHome, plan.Forward), Vector3.up);
            return model;
        }
    }
}

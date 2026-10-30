using System;
using UnityEngine;

namespace BarPromenade
{
    /// <summary>The same authored court dressing is visible in City and Home.</summary>
    public static class CityResidentialCourtyardWorldBuilder
    {
        public const string RootName = "Residential Courtyards";
        public static GameObject Build(Transform parent, CityResidentialCourtyardPlan plan,
            HomeExteriorContextPlan home = null)
        {
            if (parent == null) throw new ArgumentNullException(nameof(parent));
            if (plan == null) throw new ArgumentNullException(nameof(plan));
            if (home != null) return BuildHomeExterior(parent, plan, home);
            var root = new GameObject(RootName);
            root.transform.SetParent(parent, false);
            foreach (CityResidentialCourtyardPropDescriptor prop in plan.Props)
            {
                GameObject model = CityResidentialCourtyardAssetProvider.Create(root.transform,
                    prop.Kind, prop.Position, prop.Rotation);
                model.name = prop.StableId;
            }
            CityStaticCollisionBuilder.AddColliderGroup(root.transform, "Court Solid Contacts", plan.CollisionBounds);
            return root;
        }

        public static GameObject BuildHomeExterior(Transform parent, HomeExteriorContextPlan home)
            => BuildHomeExterior(parent, CityResidentialCourtyardPlan.ForLayout(home.Layout), home);

        public static GameObject BuildHomeExterior(Transform parent, CityResidentialCourtyardPlan plan,
            HomeExteriorContextPlan home)
        {
            if (parent == null) throw new ArgumentNullException(nameof(parent));
            if (plan == null) throw new ArgumentNullException(nameof(plan));
            if (home == null) throw new ArgumentNullException(nameof(home));
            var root = new GameObject("Home " + RootName);
            root.transform.SetParent(parent, false);
            foreach (CityResidentialCourtyardPropDescriptor prop in plan.Props)
            {
                Vector3 local = PlayerHomeBalconyGeometry.ToHomeLocal(home.PlayerHome, prop.Position);
                float radius = new Vector2(prop.Bounds.extents.x, prop.Bounds.extents.z).magnitude;
                if (local.x - radius < HomeExteriorViewBuilder.ContextMinimumX ||
                    new Vector2(local.x - PlayerHomeBalconyGeometry.HomeFacadeX, local.z).magnitude - radius > HomeExteriorContextPlanner.ViewRadius) continue;
                // The provider returns passive geometry. Home neither copies
                // the physical obstacles nor runs the court population.
                GameObject model = CityResidentialCourtyardAssetProvider.Create(root.transform,
                    prop.Kind, prop.Position, prop.Rotation);
                model.name = prop.StableId;
                model.transform.localPosition = local;
                model.transform.localRotation = Quaternion.LookRotation(
                    PlayerHomeBalconyGeometry.ToHomeLocalDirection(home.PlayerHome, prop.Forward),
                    PlayerHomeBalconyGeometry.ToHomeLocalDirection(home.PlayerHome, prop.Rotation * Vector3.up));
            }
            return root;
        }
    }
}

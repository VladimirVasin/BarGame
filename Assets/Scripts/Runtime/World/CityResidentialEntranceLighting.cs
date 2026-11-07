using System;
using System.Collections.Generic;
using UnityEngine;

namespace BarPromenade
{
    /// <summary>One visible, fixed municipal fixture at every ordinary residential doorway.</summary>
    public static class CityResidentialEntranceLighting
    {
        public const string RootName = "Residential Entrance Lighting";
        public const string FixturePrefix = "Entrance Lamp ";
        public const string LightAnchorName = "IndustrialTaskLight_ResidentialEntrance";
        public const float MountHeight = 2.55f;
        public static readonly Vector3 ModelScale = new Vector3(.6f, .8f, .8f);
        private const string CanopyAnchorName = "IndustrialTaskLight_CourtyardEntrance";
        private static readonly Color LensColor =
            CityNightAtmosphere.GetIndustrialTaskLightColor(CityIndustrialPurpose.CompressorHouse) * 3f;

        public static Transform Build(Transform parent, CityLayout layout, HomeExteriorContextPlan home = null)
        {
            if (parent == null) throw new ArgumentNullException(nameof(parent));
            if (layout == null) throw new ArgumentNullException(nameof(layout));
            // Canopies have already been placed. Their physical lenses already
            // own a source, so a doorway underneath must not gain a second lamp.
            var canopySources = new List<Vector3>();
            foreach (Transform part in parent.GetComponentsInChildren<Transform>(true))
                if (part.name == CanopyAnchorName) canopySources.Add(part.position);

            Transform root = new GameObject(RootName).transform;
            root.SetParent(parent, false);
            int index = 0;
            foreach (CityResidentialEntranceDescriptor entrance in CityResidentialEntrancePlan.ForLayout(layout).Entrances)
            {
                int fixtureIndex = index++;
                if (!IsVisibleLot(entrance.Lot, home)) continue;
                Vector3 cityPosition = entrance.Threshold + Vector3.up * MountHeight + entrance.Outward * .025f;
                Vector3 position = home == null ? root.InverseTransformPoint(cityPosition) :
                    PlayerHomeBalconyGeometry.ToHomeLocal(home.PlayerHome, cityPosition);
                Vector3 outward = home == null ? root.InverseTransformDirection(entrance.Outward).normalized :
                    PlayerHomeBalconyGeometry.ToHomeLocalDirection(home.PlayerHome, entrance.Outward).normalized;
                Vector3 up = home == null ? root.InverseTransformDirection(Vector3.up).normalized :
                    PlayerHomeBalconyGeometry.ToHomeLocalDirection(home.PlayerHome, Vector3.up).normalized;
                if (HasCanopySource(canopySources, root.TransformPoint(position))) continue;

                Transform fixture = new GameObject(FixturePrefix + fixtureIndex + " " + entrance.Lot.Cell).transform;
                fixture.SetParent(root, false);
                fixture.localPosition = position;
                fixture.localRotation = Quaternion.LookRotation(outward, up);
                // The passive Blender fluorescent bulkhead is authored for a
                // ceiling. Turn its mounting plane onto the wall, preserving
                // the imported root's unit factor and its real protective cage.
                Transform model = CityCoveredPassageAssetProvider.Create("PassageLamp", fixture,
                    Vector3.zero, Quaternion.Euler(-90f, 0f, 0f), ModelScale);
                foreach (Renderer renderer in model.GetComponentsInChildren<Renderer>(true))
                    if (renderer.name.EndsWith("__Lens", StringComparison.Ordinal))
                        CityNightGlowRegistry.Register(renderer, LensColor);
                Transform authored = CityResidentialCourtyardAssetProvider.FindPart(model, "ANCHOR_LightSource");
                Transform source = new GameObject(LightAnchorName).transform;
                source.SetParent(fixture, false);
                source.position = authored.position;
                source.rotation = Quaternion.LookRotation(-fixture.up - fixture.forward * .18f, fixture.forward);
                // CityNightAtmosphere binds this measured anchor once, with the
                // same permanent-source/daylight/cleanup contract as other doors.
            }
            return root;
        }

        private static bool IsVisibleLot(BuildingLot lot, HomeExteriorContextPlan home)
        {
            if (home == null) return true;
            foreach (BuildingLot nearby in home.NearbyLots)
                if (ReferenceEquals(nearby, lot)) return true;
            return false;
        }

        private static bool HasCanopySource(List<Vector3> sources, Vector3 mount)
        {
            foreach (Vector3 source in sources)
            {
                Vector3 delta = source - mount;
                if (Mathf.Abs(delta.y) > .8f) continue;
                delta.y = 0f;
                if (delta.sqrMagnitude < .8f * .8f) return true;
            }
            return false;
        }
    }
}

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
        internal const string CeilingMountName = "Residential Entrance Ceiling Mount";
        private const int MaximumFixtures = 192;
        public const float MountHeight = 2.55f;
        public static readonly Vector3 ModelScale = new Vector3(.6f, .8f, .8f);
        private const string CanopyAnchorName = "IndustrialTaskLight_CourtyardEntrance";
        private static readonly Color LensColor =
            CityNightAtmosphere.GetIndustrialTaskLightColor(CityIndustrialPurpose.CompressorHouse) * 3f;

        public static Transform Build(Transform parent)
        {
            if (parent == null) throw new ArgumentNullException(nameof(parent));
            // Canopies have already been placed. Their physical lenses already
            // own a source, so a doorway underneath must not gain a second lamp.
            var canopySources = new List<Vector3>();
            foreach (Transform part in parent.GetComponentsInChildren<Transform>(true))
                if (part.name == CanopyAnchorName) canopySources.Add(part.position);

            Transform root = new GameObject(RootName).transform;
            root.SetParent(parent, false);
            int index = 0;
            // Read the measured entrances of the models actually built here.
            // The lowland route plan excludes the separately built hillside;
            // Home's nearby-lot list likewise excludes its hillside frame.
            // World-space anchors cover both without a second placement list.
            foreach (CityBuildingAssetRegistry building in parent.GetComponentsInChildren<CityBuildingAssetRegistry>(true))
            {
                if (!building.HasEntrance || building.District != CityDistrictKind.Residential) continue;
                if (index >= MaximumFixtures) throw new InvalidOperationException("Residential entrance fixture budget exceeds 192.");
                int fixtureIndex = index++;
                Transform ceiling = building.transform.Find(CeilingMountName);
                Vector3 threshold = building.transform.TransformPoint(building.LocalEntranceThreshold);
                Vector3 outward = building.transform.TransformDirection(building.LocalEntranceOutward).normalized;
                Vector3 up = building.transform.up;
                Vector3 position = ceiling != null ? ceiling.position : threshold + up * MountHeight + outward * .025f;
                if (HasCanopySource(canopySources, position)) continue;

                Transform fixture = new GameObject(FixturePrefix + fixtureIndex + " " + building.transform.parent.name).transform;
                fixture.SetParent(root, false);
                fixture.SetPositionAndRotation(position, Quaternion.LookRotation(outward, up));
                // Keep the authored ceiling mount under a stair landing;
                // otherwise turn the mounting plane onto the facade. Both
                // preserve the imported unit factor and protective cage.
                Transform model = CityCoveredPassageAssetProvider.Create("PassageLamp", fixture,
                    Vector3.zero, ceiling != null ? Quaternion.identity : Quaternion.Euler(-90f, 0f, 0f), ModelScale);
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

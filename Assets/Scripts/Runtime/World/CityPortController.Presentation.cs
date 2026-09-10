using System.Collections.Generic;
using UnityEngine;

namespace BarPromenade
{
    public sealed partial class CityPortController
    {
        private WorldDistancePresentation shorePresentation, vesselPresentation;
        private Bounds shoreBounds;
        public Transform PresentationObserver { get; set; }
        public bool ForcePresentation { get; set; }
        public bool ShorePresentationActive => shorePresentation == null || shorePresentation.IsVisible;
        public bool VesselPresentationActive => vesselPresentation == null || vesselPresentation.IsVisible;
        public Vector3 VesselLogicalPosition
        {
            get
            {
                float approach = Snapshot.Stage == CityPortCycleStage.Approach ? Ease(Snapshot.StageProgress) :
                    Snapshot.Stage == CityPortCycleStage.Depart ? 1f - Ease(Snapshot.StageProgress) :
                    Snapshot.Stage == CityPortCycleStage.Idle ? 0f : 1f;
                return Plan.World(Plan.SampleVesselPosition(approach));
            }
        }

        private void EnsurePresentation()
        {
            // The world builder adds the access road, work lights and crew
            // after Initialize. The gameplay owner binds its observer last.
            if (shorePresentation != null || PresentationObserver == null) return;
            var shoreRoots = new List<Transform>();
            shoreBounds = new Bounds(Plan.World(new Vector3(0, 5, -9)), new Vector3(38, 32, 54));
            foreach (Transform child in transform)
            {
                if (child == Vessel || System.Array.IndexOf(Cargo, child) >= 0 ||
                    child.GetComponent<CityPortCrew>() != null || child.GetComponent<CityPortSound>() != null) continue;
                shoreRoots.Add(child);
                if (child.name == "AccessRoad")
                    foreach (Renderer renderer in child.GetComponentsInChildren<Renderer>(true))
                        shoreBounds.Encapsulate(renderer.bounds);
            }
            shorePresentation = new WorldDistancePresentation(shoreRoots.ToArray());
            vesselPresentation = new WorldDistancePresentation(Vessel);
        }

        private bool UpdatePresentationVisibility()
        {
            EnsurePresentation();
            if (shorePresentation == null) return false;
            bool shore = WorldDistancePresentation.ShouldShow(PresentationObserver, shoreBounds,
                ShorePresentationActive, ForcePresentation);
            bool vessel = WorldDistancePresentation.ShouldShow(PresentationObserver,
                new Bounds(VesselLogicalPosition, new Vector3(32, 24, 32)), VesselPresentationActive, ForcePresentation);
            // Lines, hooks and workers share contacts while the vessel is berthed.
            if (Snapshot.Stage >= CityPortCycleStage.Moor && Snapshot.Stage <= CityPortCycleStage.Unmoor)
                shore = vessel = shore || vessel;
            bool changed = shore != ShorePresentationActive || vessel != VesselPresentationActive;
            shorePresentation.SetVisible(shore);
            vesselPresentation.SetVisible(vessel);
            return changed;
        }

        public void RefreshPresentation()
        {
            if (Plan == null) return;
            if (UpdatePresentationVisibility()) ApplyPresentation();
        }

        private void ApplyPresentation()
        {
            if (VesselPresentationActive)
            {
                ApplyVessel(lastWaveTime);
                ApplyHatches();
            }
            if (ShorePresentationActive)
            {
                ApplyTrolley();
                for (int crane = 0; crane < 2; crane++) ApplyCrane(crane);
                ApplyMoorings();
            }
            ApplyCargo();
        }

        private void OnDestroy()
        {
            shorePresentation?.Dispose();
            vesselPresentation?.Dispose();
        }
    }
}

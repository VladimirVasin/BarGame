using UnityEngine;

namespace BarPromenade
{
    public sealed partial class CityCanneryController
    {
        private WorldDistancePresentation factoryPresentation, truckPresentation;
        private Transform previousPortObserver;
        private bool previousPortForcePresentation;
        public bool ForcePresentation { get; set; }
        public bool HasSpawnedTruck => CityFishSupplySession.HasStarted || !AutoAdvance;
        public bool FactoryPresentationActive => factoryPresentation == null || factoryPresentation.IsVisible;
        public bool TruckPresentationActive => truckPresentation == null || truckPresentation.IsVisible;
        // Include light reach and the complete handling apron, not just a pivot.
        public Bounds FactoryPresentationBounds => new Bounds(Plan.World(new Vector3(0, 3, 0)), new Vector3(32, 20, 32));
        public Bounds TruckPresentationBounds => new Bounds(Truck.TransformPoint(new Vector3(0, 2, 2)), new Vector3(52, 36, 52));

        private void CreatePresentation()
        {
            Transform shell = null;
            var city = GetComponentInParent<CityGameRoot>();
            if (city != null && city.World != null)
                foreach (Transform candidate in city.World.DistrictPointOfInterestRoot.GetComponentsInChildren<Transform>(true))
                    if (candidate.name == "Industrial Cannery" && candidate.Find("Hall") != null)
                    { shell = candidate; break; }
            factoryPresentation = new WorldDistancePresentation(factory, shell);
            truckPresentation = new WorldDistancePresentation(Truck);
            previousPortObserver = port.PresentationObserver;
            previousPortForcePresentation = port.ForcePresentation;
            port.PresentationObserver = hero;
        }

        private bool UpdatePresentationVisibility()
        {
            bool factoryVisible = WorldDistancePresentation.ShouldShow(hero, FactoryPresentationBounds,
                FactoryPresentationActive, ForcePresentation);
            bool truckVisible = HasSpawnedTruck && WorldDistancePresentation.ShouldShow(hero, TruckPresentationBounds,
                TruckPresentationActive, ForcePresentation);
            // A receiver and the moving load share the factory handoff path.
            if (Snapshot.Stage == CityFishSupplyStage.UnloadFish || Snapshot.Stage == CityFishSupplyStage.LoadFinished)
                factoryVisible = truckVisible = factoryVisible || truckVisible;
            bool changed = factoryVisible != FactoryPresentationActive || truckVisible != TruckPresentationActive;
            // Before the first dock visit the vehicle has not entered the
            // world: its physical body must not leave an invisible obstacle.
            // Manual ApplyAt inspection can still reconstruct any sampled pose.
            if (Truck.gameObject.activeSelf != HasSpawnedTruck) Truck.gameObject.SetActive(HasSpawnedTruck);
            factoryPresentation?.SetVisible(factoryVisible);
            truckPresentation?.SetVisible(truckVisible);
            return changed;
        }

        public void RefreshPresentation()
        {
            if (!IsInitialized) return;
            port.ForcePresentation = ForcePresentation;
            bool oldShore = port.ShorePresentationActive;
            port.RefreshPresentation();
            if (UpdatePresentationVisibility() || oldShore != port.ShorePresentationActive)
                ApplyPresentation();
        }

        private void ApplyPresentation()
        {
            ApplyLocalTrolleys();
            if (TruckPresentationActive) ApplyTruckParts();
            ApplyCargoAndLine();
            ApplyVisualDetails();
            ApplyWorkers();
            ApplySounds();
            ApplyShopReceivingDoor();
        }

        private void RestorePresentation()
        {
            factoryPresentation?.Dispose();
            truckPresentation?.Dispose();
            if (port == null) return;
            port.PresentationObserver = previousPortObserver;
            port.ForcePresentation = previousPortForcePresentation;
            port.RefreshPresentation();
        }
    }
}

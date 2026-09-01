using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using UnityEngine;

namespace BarPromenade
{
    public sealed class MothersHouseInteriorWorldResult
    {
        internal MothersHouseInteriorWorldResult(
            Transform root,
            Transform model,
            MothersHouseInteriorAssetRegistry registry,
            Transform collisionRoot,
            IList<Collider> gameplayColliders,
            Collider stairRampCollider,
            Transform entryAnchor,
            Transform spawnAnchor,
            Transform exitAnchor,
            Transform cameraAnchor,
            Transform cameraTargetAnchor,
            Transform fireplaceAnchor,
            Transform fireLightAnchor,
            Transform floorLampLightAnchor,
            Transform tabletopAnchor,
            Transform teapotDockAnchor)
        {
            Root = root ?? throw new ArgumentNullException(nameof(root));
            Model = model ?? throw new ArgumentNullException(nameof(model));
            Registry = registry ??
                throw new ArgumentNullException(nameof(registry));
            CollisionRoot = collisionRoot ??
                throw new ArgumentNullException(nameof(collisionRoot));
            GameplayColliders = new ReadOnlyCollection<Collider>(
                new List<Collider>(
                    gameplayColliders ?? throw new ArgumentNullException(
                        nameof(gameplayColliders))));
            StairRampCollider = stairRampCollider ??
                throw new ArgumentNullException(nameof(stairRampCollider));
            EntryAnchor = entryAnchor ??
                throw new ArgumentNullException(nameof(entryAnchor));
            SpawnAnchor = spawnAnchor ??
                throw new ArgumentNullException(nameof(spawnAnchor));
            ExitAnchor = exitAnchor ??
                throw new ArgumentNullException(nameof(exitAnchor));
            CameraAnchor = cameraAnchor ??
                throw new ArgumentNullException(nameof(cameraAnchor));
            CameraTargetAnchor = cameraTargetAnchor ??
                throw new ArgumentNullException(nameof(cameraTargetAnchor));
            FireplaceAnchor = fireplaceAnchor ??
                throw new ArgumentNullException(nameof(fireplaceAnchor));
            FireLightAnchor = fireLightAnchor ??
                throw new ArgumentNullException(nameof(fireLightAnchor));
            FloorLampLightAnchor = floorLampLightAnchor ??
                throw new ArgumentNullException(
                    nameof(floorLampLightAnchor));
            TabletopAnchor = tabletopAnchor ??
                throw new ArgumentNullException(nameof(tabletopAnchor));
            TeapotDockAnchor = teapotDockAnchor ??
                throw new ArgumentNullException(nameof(teapotDockAnchor));
        }

        public Transform Root { get; }
        public Transform Model { get; }
        public MothersHouseInteriorAssetRegistry Registry { get; }
        public Transform CollisionRoot { get; }
        public IReadOnlyList<Collider> GameplayColliders { get; }
        public Collider StairRampCollider { get; }
        public Transform EntryAnchor { get; }
        public Transform SpawnAnchor { get; }
        public Transform ExitAnchor { get; }
        public Transform CameraAnchor { get; }
        public Transform CameraTargetAnchor { get; }
        public Transform FireplaceAnchor { get; }
        public Transform FireLightAnchor { get; }
        public Transform FloorLampLightAnchor { get; }
        public Transform TabletopAnchor { get; }
        public Transform TeapotDockAnchor { get; }
    }
}

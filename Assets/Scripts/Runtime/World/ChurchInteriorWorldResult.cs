using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using UnityEngine;

namespace BarPromenade
{
    public sealed class ChurchInteriorWorldResult
    {
        internal ChurchInteriorWorldResult(
            Transform root,
            Transform model,
            ChurchAssetRegistry registry,
            Transform collisionRoot,
            IList<Collider> gameplayColliders)
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
        }

        public Transform Root { get; }
        public Transform Model { get; }
        public ChurchAssetRegistry Registry { get; }
        public Transform CollisionRoot { get; }
        public IReadOnlyList<Collider> GameplayColliders { get; }
    }
}

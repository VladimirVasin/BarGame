using System;
using System.Collections.Generic;
using UnityEngine;
using Object = UnityEngine.Object;

namespace BarPromenade
{
    /// <summary>
    /// Pulls the hero prefab and the pooled pedestrian archetype prefabs into
    /// memory while the start menu sits idle, so the first hero-bearing
    /// composition of a session does not pay their first deserialisation
    /// (the hero animation FBX and the two shared pedestrian FBX files are
    /// the bulk of the City root `hero` + `pedestrian_pool` brackets on a
    /// cold load).
    ///
    /// Every load is <see cref="Resources.LoadAsync{T}(string)"/>; nothing
    /// here touches the main thread beyond issuing the request and, in its
    /// completion callback, taking the loaded object into <see cref="held"/>.
    /// That static list is the whole mechanism: a `Single` scene load runs
    /// the unused-asset sweep, and an asset reachable from a static field is
    /// not unused, so the prefabs stay resident until the roots ask for them
    /// by the same path and receive the resident copy.
    /// </summary>
    public static class AreaAssetWarmup
    {
        private struct WarmAsset
        {
            public string Path;
            public Object Asset;
        }

        private struct PendingRequest
        {
            public string Path;
            public ResourceRequest Request;
        }

        private const string LogCategory = "menu";

        // Strong references. Keeping these alive across the scene switch is
        // the point of the class; see the type summary.
        private static readonly List<WarmAsset> held = new List<WarmAsset>();
        private static readonly List<PendingRequest> inFlight =
            new List<PendingRequest>();

        public static int HeldCount => held.Count;
        public static int InFlightCount => inFlight.Count;

        // Domain reload is off in this project, so the held list would
        // outlive a play session (and pin editor-loaded assets) unless it
        // is reset here.
        [RuntimeInitializeOnLoadMethod(
            RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            held.Clear();
            inFlight.Clear();
        }

        /// <summary>
        /// The resource paths the menu warms: the hero first, then every
        /// distinct archetype prefab the City pedestrian pool loads. Derived
        /// from the same catalog the roots read, never listed by hand.
        /// </summary>
        public static IReadOnlyList<string> CollectMenuWarmupPaths()
        {
            var paths = new List<string>
            {
                Player3DResources.PrefabResourcePath
            };
            IReadOnlyList<string> pooled =
                CityPedestrianResources.CollectPooledPrefabResourcePaths(
                    CityPedestrianPopulationProfile.City.PoolSize);
            for (int index = 0; index < pooled.Count; index++)
            {
                string path = pooled[index];
                if (!paths.Contains(path))
                {
                    paths.Add(path);
                }
            }

            return paths;
        }

        /// <summary>
        /// Issues one asynchronous load per path not already resident or in
        /// flight. Safe to call again (a pause-menu restart re-enters the
        /// launch card): already-warm paths are skipped.
        /// </summary>
        internal static void BeginFromMenu()
        {
            IReadOnlyList<string> paths = CollectMenuWarmupPaths();
            int issued = 0;
            int skipped = 0;
            for (int index = 0; index < paths.Count; index++)
            {
                string path = paths[index];
                if (IsHeld(path) || IsInFlight(path))
                {
                    skipped++;
                    continue;
                }

                ResourceRequest request =
                    Resources.LoadAsync<GameObject>(path);
                if (request == null)
                {
                    GameLog.Warning(
                        LogCategory,
                        "asset_warmup_refused",
                        GameLog.Field("path", path));
                    continue;
                }

                issued++;
                if (request.isDone)
                {
                    Hold(path, request.asset);
                    continue;
                }

                inFlight.Add(new PendingRequest
                {
                    Path = path,
                    Request = request
                });
                request.completed += OnRequestCompleted;
            }

            GameLog.Info(
                LogCategory,
                "asset_warmup_begun",
                GameLog.Field("issued", issued),
                GameLog.Field("already_warm", skipped),
                GameLog.Field("in_flight", inFlight.Count));
        }

        private static void OnRequestCompleted(AsyncOperation operation)
        {
            var request = operation as ResourceRequest;
            if (request == null)
            {
                return;
            }

            for (int index = 0; index < inFlight.Count; index++)
            {
                if (!ReferenceEquals(inFlight[index].Request, request))
                {
                    continue;
                }

                string path = inFlight[index].Path;
                inFlight.RemoveAt(index);
                Hold(path, request.asset);
                if (inFlight.Count == 0)
                {
                    GameLog.Info(
                        LogCategory,
                        "asset_warmup_completed",
                        GameLog.Field("held", held.Count));
                }

                return;
            }

            // A request that finished after ResetStatics cleared the list
            // belongs to a previous play session; its asset is not held.
        }

        private static void Hold(string path, Object asset)
        {
            if (asset == null)
            {
                GameLog.Warning(
                    LogCategory,
                    "asset_warmup_missing",
                    GameLog.Field("path", path));
                return;
            }

            if (IsHeld(path))
            {
                return;
            }

            held.Add(new WarmAsset
            {
                Path = path,
                Asset = asset
            });
        }

        private static bool IsHeld(string path)
        {
            for (int index = 0; index < held.Count; index++)
            {
                if (!string.Equals(
                        held[index].Path,
                        path,
                        StringComparison.Ordinal))
                {
                    continue;
                }

                // A destroyed or explicitly unloaded asset is no longer
                // warm; drop the stale entry so it can be requested again.
                if (held[index].Asset != null)
                {
                    return true;
                }

                held.RemoveAt(index);
                return false;
            }

            return false;
        }

        private static bool IsInFlight(string path)
        {
            for (int index = 0; index < inFlight.Count; index++)
            {
                if (string.Equals(
                        inFlight[index].Path,
                        path,
                        StringComparison.Ordinal))
                {
                    return true;
                }
            }

            return false;
        }
    }
}

using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace BarPromenade
{
    /// <summary>
    /// Puts the men on the two park boards.
    ///
    /// Fifty-six of them, and they reach the frame as four renderers:
    /// one per colour per board, combined the way every other run of
    /// park geometry is combined. They are the first thing in this city
    /// built from an imported mesh rather than from boxes, because a
    /// knight is a shape a box cannot state, and they take the board's
    /// own timber sheet so a turned man belongs to the board he stands
    /// on rather than sitting on top of it.
    ///
    /// Nothing here decides where anything goes: `CityChessSetPlan` owns
    /// the position, this owns the geometry, and `CityChessBoardGeometry`
    /// owns the lattice both of them land on.
    /// </summary>
    public static class CityChessSetWorldBuilder
    {
        public const string RuntimeRootName = "Park Chess Set Men";

        /// <summary>
        /// Turned bone and turned bog oak. Both are the board's own two
        /// batch colours pushed apart — brighter than the light inlay,
        /// darker than the dark one — because a light man standing on a
        /// light square has nothing but its own shading to separate it
        /// and the board batch is drawn shadowless and flat.
        /// </summary>
        public static readonly Color LightManColor =
            new Color(0.365f, 0.297f, 0.230f);
        public static readonly Color DarkManColor =
            new Color(0.072f, 0.086f, 0.094f);

        private const CityParkSurfaceKind ManSurface =
            CityParkSurfaceKind.Timber;

        /// <summary>
        /// The sheet pitch and projection a man is unwrapped at. Public
        /// because a live game replaces these batches with one object
        /// per man and a piece that wore a different grain would read
        /// as a different set.
        /// </summary>
        public static float ManSurfaceMetersPerTile =>
            CityParkSurfaceAppearance
                .GetRecipe(ManSurface)
                .MetersPerTile;

        public static RuntimeWorldUvMode ManSurfaceUvMode =>
            CityParkSurfaceAppearance.GetUvMode(ManSurface);

        /// <summary>Textures one man, batched or live, the one way a
        /// man on this board is textured.</summary>
        public static void ApplyManAppearance(
            Renderer renderer,
            bool isLight)
        {
            CityParkSurfaceAppearance.ApplyCombined(
                renderer,
                ManSurface,
                isLight ? LightManColor : DarkManColor);
        }

        /// <summary>
        /// Raises both sets, or returns <c>null</c> when the layout grew
        /// no chess set or the art has not been imported — the city is
        /// still a city with two empty boards in it.
        /// </summary>
        public static GameObject Build(
            Transform parent,
            CityLayout layout,
            CityDecorationPlan decorations,
            int citySeed)
        {
            if (parent == null || layout == null || decorations == null)
            {
                return null;
            }

            CityChessSetProvider provider = CityChessSetProvider.Load();
            if (provider == null || !provider.IsComplete())
            {
                GameLog.Warning(
                    "city",
                    "chess_set_men_absent",
                    GameLog.Field(
                        "reason",
                        provider == null
                            ? "provider_missing"
                            : "meshes_unbound"));
                return null;
            }

            if (!TryDescribeBoardBasis(
                    layout,
                    decorations,
                    out Vector3 origin,
                    out Vector3 forward,
                    out Vector3 tangent))
            {
                return null;
            }

            IReadOnlyList<CityChessManPlacement> men =
                CityChessSetPlan.Create(citySeed);

            var root = new GameObject(RuntimeRootName);
            root.transform.SetParent(parent, false);
            CityChessSetMen men3d = root.AddComponent<CityChessSetMen>();

            // One bucket per (table, colour). Four combined meshes for
            // fifty-six men is the whole reason the plan is a list of
            // squares rather than a list of GameObjects.
            var buckets =
                new Dictionary<int, List<RuntimeMeshPlacement>>(4);
            for (int index = 0; index < men.Count; index++)
            {
                CityChessManPlacement man = men[index];
                Vector3 center = CityChessBoardGeometry.SquareCenterWorld(
                    origin,
                    tangent,
                    forward,
                    man.Table,
                    man.File,
                    man.Rank);
                center += tangent * man.Offset.x + forward * man.Offset.y;

                Quaternion rotation =
                    Quaternion.LookRotation(forward, Vector3.up) *
                    Quaternion.Euler(0f, man.YawDegrees, 0f);

                int key = (man.Table > 0 ? 2 : 0) + (man.IsLight ? 1 : 0);
                if (!buckets.TryGetValue(key, out List<RuntimeMeshPlacement> bucket))
                {
                    bucket = new List<RuntimeMeshPlacement>(32);
                    buckets[key] = bucket;
                }

                bucket.Add(new RuntimeMeshPlacement(
                    provider.GetMesh(man.Kind),
                    root.transform.InverseTransformPoint(center),
                    rotation));
            }

            foreach (KeyValuePair<int, List<RuntimeMeshPlacement>> entry
                     in buckets)
            {
                bool light = (entry.Key & 1) == 1;
                bool draughts = (entry.Key & 2) == 2;
                GameObject combined =
                    RuntimePrimitiveFactory.CreateCombinedMeshes(
                        draughts
                            ? (light ? "Light Draughts" : "Dark Draughts")
                            : (light ? "Light Chessmen" : "Dark Chessmen"),
                        root.transform,
                        entry.Value,
                        light ? LightManColor : DarkManColor,
                        false,
                        CityParkSurfaceAppearance
                            .GetRecipe(ManSurface)
                            .MetersPerTile,
                        CityParkSurfaceAppearance.GetUvMode(ManSurface),
                        Vector3.zero);
                men3d.RegisterBatch(draughts ? 1 : -1, combined);
                Renderer renderer = combined.GetComponent<Renderer>();
                // Unlike the flat board batch these do cast, and it is
                // the one thing that separates a light man from a light
                // square under the lamp.
                renderer.shadowCastingMode = ShadowCastingMode.On;
                renderer.receiveShadows = true;
                CityParkSurfaceAppearance.ApplyCombined(
                    renderer,
                    ManSurface,
                    light ? LightManColor : DarkManColor);
            }

            GameLog.Info(
                "city",
                "chess_set_men_placed",
                GameLog.Field("men", men.Count),
                GameLog.Field("batches", buckets.Count));
            return root;
        }

        /// <summary>
        /// The frame the chess recipe was drawn in: where its middle
        /// is, which way its boards face and which way their files run.
        /// Public because the live game played on those boards has to
        /// land on exactly the same lattice the static set did.
        /// </summary>
        public static bool TryDescribeBoardBasis(
            CityLayout layout,
            CityDecorationPlan decorations,
            out Vector3 origin,
            out Vector3 forward,
            out Vector3 tangent)
        {
            if (layout != null && decorations != null)
            {
                for (int index = 0;
                     index < decorations.Descriptors.Count;
                     index++)
                {
                    CityDecorationDescriptor descriptor =
                        decorations.Descriptors[index];
                    if (descriptor.Kind !=
                        CityDecorationKind.ParkChessTables)
                    {
                        continue;
                    }

                    if (TryDescribeBoardBasis(
                            layout,
                            descriptor,
                            out origin,
                            out forward,
                            out tangent))
                    {
                        return true;
                    }
                }
            }

            origin = Vector3.zero;
            forward = Vector3.forward;
            tangent = Vector3.right;
            return false;
        }

        /// <summary>The same frame for one already-selected chess
        /// decoration.</summary>
        public static bool TryDescribeBoardBasis(
            CityLayout layout,
            CityDecorationDescriptor descriptor,
            out Vector3 origin,
            out Vector3 forward,
            out Vector3 tangent)
        {
            if (layout != null &&
                descriptor.Kind == CityDecorationKind.ParkChessTables &&
                CityDecorationWorldBuilder.TryDescribeRecipeBasis(
                    layout,
                    descriptor,
                    out origin,
                    out forward))
            {
                tangent = new Vector3(-forward.z, 0f, forward.x);
                return true;
            }

            origin = Vector3.zero;
            forward = Vector3.forward;
            tangent = Vector3.right;
            return false;
        }
    }
}

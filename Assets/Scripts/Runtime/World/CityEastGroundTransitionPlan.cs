using System.Collections.Generic;
using System.Runtime.CompilerServices;
using UnityEngine;

namespace BarPromenade
{
    /// <summary>One material boundary shared by the garden and the eastern yard.
    /// Its outer envelope is wider than the irregular 3–5 m worn transition.
    /// Coordinates are local to the seam, so different city sizes keep both
    /// adjoining textures in phase without a different baked asset.</summary>
    public sealed class CityEastGroundTransitionPlan
    {
        public const float HalfDepth = 4f;
        public const float WorldRepeat = 24f;
        public const string TextureResource = "Textures/CityEastGroundTransitionAlbedo";
        private static readonly ConditionalWeakTable<CityLayout, CityEastGroundTransitionPlan> Plans =
            new ConditionalWeakTable<CityLayout, CityEastGroundTransitionPlan>();
        private readonly IReadOnlyList<float> seamStations = System.Array.Empty<float>();

        private CityEastGroundTransitionPlan(CityLayout layout, CityEastExitPlan exit)
        {
            IsEnabled = exit.IsEnabled;
            if (!IsEnabled) return;
            SeamZ = exit.YardBounds.yMin;
            Bounds = Rect.MinMaxRect(exit.YardBounds.xMin, SeamZ - HalfDepth,
                exit.YardBounds.xMax, SeamZ + HalfDepth);
            seamStations = CreateSeamStations(layout, exit);
        }
        public bool IsEnabled { get; }
        public float SeamZ { get; }
        public Rect Bounds { get; }
        // Same periodic outline as build-city-east-ground-texture.py. Used by
        // the existing footstep overlay channel, not by rendering each frame.
        public float SoilWeight(Vector2 point)
        {
            float z = point.y - SeamZ;
            float phase = point.x * (2f * Mathf.PI / WorldRepeat);
            float center = -.55f + .43f * Mathf.Sin(phase) + .24f * Mathf.Sin(3f * phase + .7f);
            float width = 3.7f + .45f * Mathf.Sin(2f * phase - .6f);
            float irregular = .20f * Mathf.Sin(5f * phase + z * 2.1f) + .12f * Mathf.Sin(9f * phase - z * 3.4f);
            float t = Mathf.Clamp01((z - center + irregular) / width + .5f);
            return t * t * (3f - 2f * t);
        }
        /// <summary>Both terrain owners use the same north/south boundary
        /// stations before creating either their visible skin or collision.
        /// The church still owns its flat garden and existing linear slope.</summary>
        internal void AppendSharedSeamAnchors(CitySurfaceDescriptor surface, Rect patch,
            ICollection<float> xAnchors, ICollection<float> zAnchors)
        {
            if (!IsEnabled || patch.yMin > SeamZ + .00001f || patch.yMax < SeamZ - .00001f ||
                patch.xMax < Bounds.xMin || patch.xMin > Bounds.xMax ||
                (surface.Kind != CitySurfaceKind.ChurchGround && surface.AreaId != "yard-east")) return;
            foreach (float x in seamStations)
                if (x > patch.xMin && x < patch.xMax) xAnchors.Add(x);
            zAnchors.Add(SeamZ);
        }

        private IReadOnlyList<float> CreateSeamStations(CityLayout layout, CityEastExitPlan exit)
        {
            var x = new List<float> { Bounds.xMin, Bounds.xMax, exit.CheckpointPosition.x,
                exit.ApproachStart.x + CityEastExitPlan.StreetGradeBlendLength,
                exit.CheckpointPosition.x - CityEastExitPlan.CheckpointApronLength };
            // The yard refines its authored swale to quarter metres and its
            // dressing to half metres. One world-aligned boundary row covers
            // both grids without refining the church's inhabited flat core.
            for (float value = Mathf.Ceil(Bounds.xMin * 4f) * .25f; value < Bounds.xMax; value += .25f)
                x.Add(value);
            foreach (float offset in new[] { -2f, 0f, 2f })
            { x.Add(exit.BoothPad.xMin + offset); x.Add(exit.BoothPad.xMax + offset); }
            var unusedZ = new List<float>();
            Rect stationReach = Rect.MinMaxRect(Bounds.xMin, SeamZ - 4f, Bounds.xMax, SeamZ + 4f);
            foreach (CitySurfaceDescriptor neighbour in layout.Surfaces)
            {
                if (neighbour.AreaId != "yard-east" || Mathf.Abs(neighbour.WorldBounds.yMin - SeamZ) > .001f) continue;
                x.Add(neighbour.WorldBounds.xMin); x.Add(neighbour.WorldBounds.xMax);
                CityTerrainStreetBoundaryPlan.SurfaceBoundaryContext boundary =
                    CityTerrainStreetBoundaryPlan.ForSurface(layout, neighbour);
                if (boundary == null) continue;
                boundary.AppendAxisAnchors(stationReach, x, unusedZ);
                foreach (CityTerrainStreetBoundaryPlan.Cut cut in boundary.Cuts)
                {
                    if (!cut.Bounds.Overlaps(stationReach)) continue;
                    float dz = cut.Second.y - cut.First.y;
                    if (Mathf.Abs(dz) < .000001f) continue;
                    // A cut traversing a neighbouring mesh cell may extend to
                    // the seam even when its finite authored end stops short.
                    float station = cut.First.x + (SeamZ - cut.First.y) * (cut.Second.x - cut.First.x) / dz;
                    if (station > Bounds.xMin && station < Bounds.xMax) x.Add(station);
                }
            }
            x.Sort();
            var result = new List<float>();
            foreach (float value in x)
                if (value >= Bounds.xMin && value <= Bounds.xMax &&
                    (result.Count == 0 || Mathf.Abs(value - result[result.Count - 1]) > .00001f)) result.Add(value);
            return result.AsReadOnly();
        }

        public static CityEastGroundTransitionPlan Create(CityLayout layout) =>
            Plans.GetValue(layout, value => new CityEastGroundTransitionPlan(value, CityEastExitPlanner.Create(value)));
    }
}

using System;
using System.Collections.Generic;
using UnityEngine;

namespace BarPromenade
{
    /// <summary>Metre-space working harbour. All geometry, navigation and choreography use this frame.</summary>
    public sealed class CityPortPlan
    {
        public const float DeckHeight = 1.5f;
        public const float VesselLength = 21.52f;
        public const float VesselBeam = 5.57f;
        public const float VesselDraft = 1.5f;
        public const float BasinDepth = 3.4f;
        public const float BoomLength = 8f;
        public Vector3 Origin { get; }
        public CityPortAccessPlan Access { get; internal set; }
        public Vector3 VesselBerthLocal => new Vector3(0f, 0f, 3.2f);
        public float QuayTopY => Origin.y + DeckHeight;
        public Rect LandBounds => RectAt(-26f, -22f, 19f, 0f);
        public Rect QuayBounds => RectAt(-16f, -10f, 16f, 0f);
        public Rect WarehouseBounds => RectAt(-8f, -19f, 4f, -12f);
        public Rect BreakwaterBounds => RectAt(-25f, -2f, -21f, 30f);
        public Rect BreakwaterHeadBounds => RectAt(-25f, 28f, -7f, 32f);
        public Rect SeaBounds => RectAt(-30f, 0f, 60f, 90f);
        public Rect VesselExclusion => RectAt(-26f, 0f, 59f, 80f);
        public Vector3 WarehouseDropLocal => new Vector3(0f, DeckHeight, -17.5f);
        public Vector3 RavenPerch => World(new Vector3(-23f, DeckHeight, 30.9f));
        public Vector3 RavenCompanion => World(new Vector3(-23f, DeckHeight, 27.1f));

        internal CityPortPlan(Vector3 origin) { Origin = origin; }

        public static CityPortPlan Create(in CitySeacoastFrame frame)
        {
            // Small alternative blueprints retain their old, non-operational mol.
            if (frame.WestZone.width < 115f || frame.BeachRowBounds.height < 23f) return null;
            float x = Mathf.Clamp(frame.ChannelXMin - 75f,
                frame.WestZone.xMin + 31f, frame.ChannelXMin - 61f);
            // A short reclaimed quay reaches out from the toe of the beach.
            // Its rear promenade meets the low shore instead of burying the
            // fixed-height warehouse in the beach's rising street-side slope.
            return new CityPortPlan(new Vector3(x, frame.SeaTopY, frame.WaterlineZ + 20f));
        }

        public Vector3 World(Vector3 local) => Origin + local;
        public Vector3 CraneBaseLocal(int crane) => new Vector3(crane == 0 ? -4f : 4f, DeckHeight, -2f);
        public Vector3 LandingLocal(int crane) => new Vector3(crane == 0 ? -4f : 4f, DeckHeight, -6f);
        public Rect RectAt(float x0, float z0, float x1, float z1) =>
            Rect.MinMaxRect(Origin.x + x0, Origin.z + z0, Origin.x + x1, Origin.z + z1);

        public Vector3 SampleVesselPosition(float approachProgress)
        {
            float t = Mathf.Clamp01(approachProgress), u = 1f - t;
            // Bow enters from the north-east, then follows its heading into the berth.
            return u * u * u * new Vector3(45f, 0f, 65f) +
                3f * u * u * t * new Vector3(45f, 0f, 12f) +
                3f * u * t * t * new Vector3(55f, 0f, 3.2f) +
                t * t * t * VesselBerthLocal;
        }

        public Quaternion SampleVesselRotation(float approachProgress)
        {
            float t = Mathf.Clamp01(approachProgress), u = 1f - t;
            Vector3 tangent = 3f * u * u * new Vector3(0f, 0f, -53f) +
                6f * u * t * new Vector3(10f, 0f, -8.8f) +
                3f * t * t * new Vector3(-55f, 0f, 0f);
            return Quaternion.LookRotation(tangent.normalized, Vector3.up);
        }

        public Quaternion SampleDepartureRotation(float progress) =>
            SampleVesselRotation(1f - progress) * Quaternion.Euler(0f,
                180f * Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(.4f, .75f, progress)), 0f);

        public void AppendWalkableFootprints(ICollection<Rect> destination)
        {
            destination.Add(LandBounds);
            destination.Add(RectAt(-30f, -22f, -25.9f, -19f));
            destination.Add(BreakwaterBounds);
            destination.Add(BreakwaterHeadBounds);
            Access?.AppendWalkableFootprints(destination);
        }

        public float DredgedBottom(Vector2 world, float naturalBottom)
        {
            float x = world.x - Origin.x, z = world.y - Origin.z;
            // The berth wall hides the north/south lip. Blend only the sides of the
            // excavated channel, so a physically deep berth never rides on beach sand.
            float sides = Mathf.SmoothStep(0f, 1f, Mathf.Min((x + 25f) / 6f, (60f - x) / 6f));
            float far = 1f - Mathf.SmoothStep(0f, 1f, (z - 78f) / 12f);
            if (z < 0f || sides <= 0f || far <= 0f) return naturalBottom;
            return Mathf.Lerp(naturalBottom, Mathf.Min(naturalBottom, Origin.y - BasinDepth), sides * far);
        }

        public void ValidateOrThrow()
        {
            for (int leg = 0; leg < 2; leg++)
            for (int i = 0; i <= 160; i++)
            {
                float progress = i / 160f;
                Vector3 local = SampleVesselPosition(leg == 0 ? progress : 1f - progress);
                Quaternion rotation = leg == 0 ? SampleVesselRotation(progress) : SampleDepartureRotation(progress);
                for (int end = -1; end <= 1; end += 2)
                for (int side = -1; side <= 1; side += 2)
                {
                    Vector3 p = World(local + rotation * new Vector3(side * VesselBeam / 2, 0, end * VesselLength / 2));
                    var xz = new Vector2(p.x, p.z);
                    if (!SeaBounds.Contains(xz) || BreakwaterBounds.Contains(xz) || BreakwaterHeadBounds.Contains(xz))
                        throw new InvalidOperationException($"Port vessel sweep leaves clear water: leg={leg}, progress={progress:F3}, corner={p - Origin}.");
                    if (DredgedBottom(xz, Origin.y) > Origin.y - VesselDraft - .5f)
                        throw new InvalidOperationException("Port approach has insufficient under-keel clearance.");
                }
            }
            if (BasinDepth <= VesselDraft + .5f) throw new InvalidOperationException("Port draft clearance is invalid.");
        }
    }
}

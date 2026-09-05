using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using UnityEngine;

namespace BarPromenade
{
    /// <summary>One passive offshore course, expressed in real City coordinates.</summary>
    public readonly struct CityOffshoreBoatRoute
    {
        internal CityOffshoreBoatRoute(
            string stableId, Vector3 start, Vector3 end, int variant,
            float durationSeconds, float cycleSeconds, float phaseSeconds,
            float visualScale, float marginFadeDistance)
        {
            StableId = stableId;
            Start = start;
            End = end;
            Variant = variant;
            DurationSeconds = durationSeconds;
            CycleSeconds = cycleSeconds;
            PhaseSeconds = phaseSeconds;
            VisualScale = visualScale;
            MarginFadeDistance = marginFadeDistance;
        }

        public string StableId { get; }
        public Vector3 Start { get; }
        public Vector3 End { get; }
        public int Variant { get; }
        public float DurationSeconds { get; }
        public float CycleSeconds { get; }
        public float PhaseSeconds { get; }
        public float VisualScale { get; }
        public float MarginFadeDistance { get; }
        public float Length => Vector3.Distance(Start, End);

        public CityOffshoreBoatPose Sample(double scaledSeconds)
        {
            return CityOffshoreBoatMotionRules.Sample(this, scaledSeconds);
        }
    }

    public readonly struct CityOffshoreBoatPose
    {
        internal CityOffshoreBoatPose(
            Vector3 position, Quaternion rotation, float presence, float progress01)
        {
            Position = position;
            Rotation = rotation;
            Presence = presence;
            Progress01 = progress01;
        }

        public Vector3 Position { get; }
        public Quaternion Rotation { get; }
        public float Presence { get; }
        public float Progress01 { get; }
    }

    /// <summary>
    /// Scenery only: no navigation, docking, map extent or gameplay ownership.
    /// The water datum and finite routes come from the existing coast.
    /// </summary>
    public sealed class CityOffshoreBoatPlan
    {
        public const int MaximumBoatCount = 2;
        private readonly ReadOnlyCollection<CityOffshoreBoatRoute> routes;

        internal CityOffshoreBoatPlan(
            IList<CityOffshoreBoatRoute> source, float seaTopY, float waterlineZ)
        {
            if (source == null)
                throw new ArgumentNullException(nameof(source));
            if (source.Count > MaximumBoatCount)
                throw new ArgumentOutOfRangeException(nameof(source));
            routes = new List<CityOffshoreBoatRoute>(source).AsReadOnly();
            SeaTopY = seaTopY;
            WaterlineZ = waterlineZ;
        }

        public IReadOnlyList<CityOffshoreBoatRoute> Routes => routes;
        public float SeaTopY { get; }
        public float WaterlineZ { get; }
    }

    /// <summary>
    /// Pure scaled-world-time motion. A long empty interval separates passes;
    /// the reset occurs only at zero presence. Camera-distance fading and the
    /// sea's own wave sampler remain presentation concerns.
    /// </summary>
    public static class CityOffshoreBoatMotionRules
    {
        public static CityOffshoreBoatPose Sample(
            in CityOffshoreBoatRoute route, double scaledSeconds)
        {
            if (double.IsNaN(scaledSeconds) || double.IsInfinity(scaledSeconds))
                throw new ArgumentOutOfRangeException(nameof(scaledSeconds));
            if (route.DurationSeconds <= 0f ||
                route.CycleSeconds <= route.DurationSeconds)
                throw new ArgumentException("The offshore course needs an invisible rest.", nameof(route));

            double time = (scaledSeconds + route.PhaseSeconds) % route.CycleSeconds;
            if (time < 0d)
                time += route.CycleSeconds;
            float progress = Mathf.Clamp01((float)(time / route.DurationSeconds));
            float edgeDistance = Mathf.Min(progress, 1f - progress) * route.Length;
            float fade = route.MarginFadeDistance > 0f
                ? Mathf.Clamp01(edgeDistance / route.MarginFadeDistance)
                : 1f;
            float presence = time >= route.DurationSeconds
                ? 0f
                : fade * fade * (3f - 2f * fade);
            Vector3 heading = route.End - route.Start;
            Quaternion rotation = heading.sqrMagnitude > 0.0001f
                ? Quaternion.LookRotation(heading, Vector3.up)
                : Quaternion.identity;
            return new CityOffshoreBoatPose(
                Vector3.Lerp(route.Start, route.End, progress),
                rotation, presence, progress);
        }
    }
}

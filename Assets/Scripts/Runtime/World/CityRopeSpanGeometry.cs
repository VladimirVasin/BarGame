using System;
using System.Collections.Generic;
using UnityEngine;

namespace BarPromenade
{
    /// <summary>
    /// The shared sagging-span helper for ropes and lines hung between
    /// two fixed knots. A parabola rather than a catenary for the chess
    /// lamps' reason: over spans this short the two curves differ by
    /// less than the rope's own thickness, and the parabola stays a
    /// pure function of the two knots.
    /// </summary>
    internal static class CityRopeSpanGeometry
    {
        /// <summary>
        /// Chord count for spans under about eight metres. Chosen
        /// against the PS1 composite the way the chess-lamp wire's
        /// twelve was: past this the extra joints land inside one
        /// downsampled pixel.
        /// </summary>
        public const int DefaultSegments = 8;

        /// <summary>
        /// A point on the sagging span. <paramref name="t01"/> runs
        /// from <paramref name="start"/> (0) to <paramref name="end"/>
        /// (1); the drop is deepest mid-span and zero at both knots.
        /// </summary>
        public static Vector3 SamplePoint(
            Vector3 start,
            Vector3 end,
            float sagMeters,
            float t01)
        {
            float amount = Mathf.Clamp01(t01);
            Vector3 flat = Vector3.Lerp(start, end, amount);
            float drop = sagMeters * 4f * amount * (1f - amount);
            return new Vector3(flat.x, flat.y - drop, flat.z);
        }

        /// <summary>
        /// The span as a chain of thin boxes, each laid on its own
        /// chord of the sag, appended for the caller's static batch.
        /// </summary>
        public static void AppendChordBoxes(
            ICollection<RuntimeOrientedBox> target,
            Vector3 start,
            Vector3 end,
            float sagMeters,
            float thickness,
            int segments = DefaultSegments)
        {
            if (target == null)
            {
                throw new ArgumentNullException(nameof(target));
            }

            if (thickness <= 0f)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(thickness),
                    "A rope chord needs a positive thickness.");
            }

            if (segments < 1)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(segments),
                    "A rope span needs at least one chord.");
            }

            Vector3 previous = start;
            for (int index = 1; index <= segments; index++)
            {
                Vector3 next = SamplePoint(
                    start,
                    end,
                    sagMeters,
                    index / (float)segments);
                Vector3 delta = next - previous;
                float length = delta.magnitude;
                if (length > 0.0001f)
                {
                    target.Add(new RuntimeOrientedBox(
                        (previous + next) * 0.5f,
                        Quaternion.LookRotation(
                            delta / length,
                            Vector3.up),
                        new Vector3(thickness, thickness, length)));
                }

                previous = next;
            }
        }
    }
}

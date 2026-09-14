using UnityEngine;

namespace BarPromenade
{
    /// <summary>Broad ordinary earth undulations, sampled by the existing terrain and collision.</summary>
    public sealed class CityEastReliefPlan
    {
        private readonly CityEastExitPlan exit;
        private readonly float phase;
        private readonly Rect[] shedPads = new Rect[3];

        internal CityEastReliefPlan(CityEastExitPlan source)
        {
            exit = source;
            phase = (source.Layout.Seed & 1023) * .017f;
            Bounds = Rect.MinMaxRect(source.YardBounds.xMin, source.YardBounds.yMin,
                source.YardBounds.xMax, source.NorthYardBounds.yMax);
            // Fixed pads belong to the existing sheds. Keep their complete
            // foundations and door approaches on the established datum.
            for (int i = 0; i < shedPads.Length; i++)
            {
                float x = Bounds.xMin + (i == 0 ? CityFringeYardPlanner.FirstEastUtilityShedDepth : 35f + i * 2.2f);
                float z = Mathf.Lerp(source.YardBounds.yMin + 12f, source.YardBounds.yMax - 12f, .18f + i * .32f);
                shedPads[i] = new Rect(x - 4f, z - 4.5f, 8f, 9f);
            }
        }

        public Rect Bounds { get; }

        public float SampleOffset(Vector2 point)
        {
            if (!Bounds.Contains(point)) return 0f;
            float x = point.x - Bounds.xMin, z = point.y - Bounds.yMin;
            // All fence toes, the garden seam and the beach return retain
            // their old height; the rise starts gently away from each edge.
            float ends = Ramp(z, .6f, 5f) * Ramp(Bounds.yMax - point.y, .6f, 5f);
            float post = Ramp(Mathf.Abs(point.y - exit.CheckpointPosition.z), 12f, 20f);
            if (ends * post <= 0f) return 0f;

            bool front = point.x < exit.CheckpointPosition.x;
            float shape;
            float envelope;
            if (front)
            {
                // The walking strip rolls slowly along its length. The
                // existing hollow keeps its own depth, banks and crossings.
                envelope = Ramp(x, .8f, 2.1f) * (1f - Ramp(x, 3.9f, 5.5f));
                shape = .155f * Mathf.Sin(z * .143f + phase) +
                    .065f * Mathf.Sin(z * .067f - phase + x * .34f);
            }
            else
            {
                float behind = point.x - exit.CheckpointPosition.x;
                envelope = Ramp(behind, .7f, 4.5f) * Ramp(Bounds.xMax - point.x, .8f, 5f);
                // Unequal wavelengths break the former straight service
                // strip into wide humps and hollows, with no sawtooth ridges.
                shape = .31f * Mathf.Sin(behind * .29f + .65f * Mathf.Sin(z * .051f + phase)) +
                    .20f * Mathf.Sin(z * .157f + phase) +
                    .11f * Mathf.Sin(behind * .13f - z * .079f - phase);
                foreach (Rect pad in shedPads)
                {
                    float dx = Mathf.Max(pad.xMin - point.x, 0f, point.x - pad.xMax);
                    float dz = Mathf.Max(pad.yMin - point.y, 0f, point.y - pad.yMax);
                    envelope *= Ramp(Mathf.Max(dx, dz), 0f, 3f);
                }
                // Old long spoil banks retain their continuous buried feet;
                // the nearby surface settles before reaching that rear line.
                envelope *= Ramp(Mathf.Abs(x - 50f), 4f, 9f);
            }
            foreach (float crossing in exit.Swale.CrossingZ)
                envelope *= Ramp(Mathf.Abs(point.y - crossing), 2.2f, 5.5f);
            return shape * envelope * ends * post;
        }

        private static float Ramp(float value, float from, float to) =>
            Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(from, to, value));
    }
}

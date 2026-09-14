using System.Collections.Generic;
using System.Collections.ObjectModel;
using UnityEngine;

namespace BarPromenade
{
    /// <summary>A dry roadside hollow in the existing public ground skin.</summary>
    public sealed class CityEastSwalePlan
    {
        private readonly float streetX;
        private readonly float axisZ;
        private readonly float phase;

        internal CityEastSwalePlan(CityEastExitPlan exit)
        {
            streetX = exit.YardBounds.xMin;
            axisZ = exit.CheckpointPosition.z;
            phase = (exit.Layout.Seed & 1023) * .017f;
            StartZ = axisZ + 4f;
            EndZ = exit.NorthYardBounds.yMax - 2f;
            Bounds = Rect.MinMaxRect(streetX + 5.5f, StartZ, streetX + 11.25f, EndZ);
            var crossings = new List<float>();
            for (float z = exit.YardBounds.yMin + exit.Layout.NodeSpacing.y * 3f;
                 z < EndZ - 8f; z += exit.Layout.NodeSpacing.y * 2f)
                if (z > axisZ + 40f) crossings.Add(z);
            CrossingZ = new ReadOnlyCollection<float>(crossings);
        }

        public Rect Bounds { get; }
        public float StartZ { get; }
        public float EndZ { get; }
        public IReadOnlyList<float> CrossingZ { get; }

        private float BroadWeight(float z) => Mathf.SmoothStep(0f, 1f,
            Mathf.InverseLerp(axisZ + 20f, axisZ + 34f, z));

        public float CenterX(float z) => streetX + Mathf.Lerp(10.5f,
            8.12f + .22f * Mathf.Sin((z - StartZ) * .055f + phase), BroadWeight(z));

        public float HalfWidth(float z) => Mathf.Lerp(.45f,
            2.15f + .20f * Mathf.Sin((z - StartZ) * .071f + phase + 1.3f), BroadWeight(z));

        public float Depth(float z)
        {
            if (z <= StartZ || z >= EndZ) return 0f;
            float depth = Mathf.Lerp(.12f,
                .425f + .055f * Mathf.Sin((z - StartZ) * .047f + phase), BroadWeight(z));
            foreach (float crossing in CrossingZ)
            {
                // A broad, almost level ford preserves a shallow dry outlet;
                // the shoulders return gradually to the lower bed.
                float shoulder = Mathf.SmoothStep(0f, 1f,
                    Mathf.InverseLerp(1.75f, 4.55f, Mathf.Abs(z - crossing)));
                depth = Mathf.Lerp(.04f, depth, shoulder);
            }
            return depth * Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(StartZ, StartZ + 3f, z)) *
                Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(EndZ, EndZ - 8f, z));
        }

        public float SampleOffset(Vector2 point)
        {
            if (!Bounds.Contains(point)) return 0f;
            float across = Mathf.Abs(point.x - CenterX(point.y)) / HalfWidth(point.y);
            if (across >= 1f) return 0f;
            float profile = 1f - across * across;
            return -Depth(point.y) * profile * profile;
        }

        public bool IsBed(Vector2 point)
        {
            if (!Bounds.Contains(point) || Depth(point.y) < .075f) return false;
            float bedWidth = HalfWidth(point.y) *
                (.26f + .055f * Mathf.Sin((point.y - StartZ) * .31f + phase));
            return Mathf.Abs(point.x - CenterX(point.y)) < bedWidth;
        }

        public bool OverlapsCrossing(Rect footprint)
        {
            if (!Bounds.Overlaps(footprint)) return false;
            foreach (float z in CrossingZ)
                if (footprint.yMin < z + 2f && footprint.yMax > z - 2f) return true;
            return false;
        }
    }
}

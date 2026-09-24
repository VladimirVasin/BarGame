using UnityEngine;

namespace BarPromenade
{
    /// <summary>A small crossing on the station's forest path, fitted to the
    /// current brook and its banks rather than a seed-specific world position.</summary>
    internal sealed class AlpineVillageFootbridgePlan
    {
        internal const string ObjectName = "Brook Footbridge";
        internal const float ModelLength = 4.8f;
        internal const float Width = 1.9f;
        internal const float DeckHeight = .18f;
        internal const float BeamBottom = -.09f;
        internal const float WaterClearance = .08f;

        internal Vector3 Position { get; private set; }
        internal Quaternion Rotation { get; private set; }
        internal Vector3 Forward => Rotation * Vector3.forward;
        internal float Length { get; private set; }
        internal Vector3 Crossing { get; private set; }
        internal string PathId { get; private set; }

        internal static AlpineVillageFootbridgePlan Create(AlpineVillagePlan plan)
        {
            if (plan?.Brook == null) return null;
            foreach (AlpineVillagePathDescriptor path in plan.Expansion.Paths)
            {
                if (!path.StableId.StartsWith("village-forest-approach-",
                    System.StringComparison.Ordinal)) continue;
                Vector2 start = XZ(path.Start), route = XZ(path.End) - start;
                for (int index = 1; index < plan.Brook.Samples.Count; index++)
                {
                    AlpineVillageBrookSample a = plan.Brook.Samples[index - 1];
                    AlpineVillageBrookSample b = plan.Brook.Samples[index];
                    Vector2 stream = XZ(b.Position - a.Position);
                    float denominator = Cross(route, stream);
                    if (Mathf.Abs(denominator) < .0001f) continue;
                    Vector2 offset = XZ(a.Position) - start;
                    float alongPath = Cross(offset, stream) / denominator;
                    float alongBrook = Cross(offset, route) / denominator;
                    if (alongPath < 0f || alongPath > 1f || alongBrook < 0f || alongBrook > 1f) continue;
                    Vector3 water = Vector3.Lerp(a.Position, b.Position, alongBrook);
                    Vector3 forward = new Vector3(route.x, 0f, route.y).normalized;
                    Vector2 streamRight = new Vector2(stream.y, -stream.x).normalized;
                    float crossingAngle = Mathf.Abs(Vector2.Dot(route.normalized, streamRight));
                    float wetSpan = Mathf.Lerp(a.Width, b.Width, alongBrook) / crossingAngle;
                    // The flat span occupies 3/4 of the model; retain bearing
                    // beyond the water before the shallow approach ramps start.
                    float horizontalLength = Mathf.Max(ModelLength, (wetSpan + 1.2f) / .75f);
                    return Fit(plan, path.StableId, water, forward, horizontalLength);
                }
            }
            return null;
        }

        private static AlpineVillageFootbridgePlan Fit(AlpineVillagePlan plan,
            string pathId, Vector3 water, Vector3 forward, float horizontalLength)
        {
            Vector3 right = Vector3.Cross(Vector3.up, forward);
            Vector3 near = water - forward * (horizontalLength * .5f);
            Vector3 far = water + forward * (horizontalLength * .5f);
            near.y = BankHeight(near);
            far.y = BankHeight(far);
            Quaternion rotation = Quaternion.LookRotation(far - near, Vector3.up);
            Vector3 position = (near + far) * .5f;
            float length = Vector3.Distance(near, far);
            Vector3 normal = rotation * Vector3.up;
            Vector3 along = rotation * Vector3.forward;
            float lift = 0f;
            foreach (AlpineVillageBrookSample sample in plan.Brook.Samples)
            {
                Vector3 relative = sample.Position - position;
                float across = Vector3.Dot(relative, right);
                float distance = Vector3.Dot(relative, forward);
                if (Mathf.Abs(across) > Width * .5f + sample.HalfWidth ||
                    Mathf.Abs(distance) > horizontalLength * .375f) continue;
                float beamY = position.y + along.y * distance / Mathf.Max(.01f, Vector3.Dot(along, forward)) +
                    normal.y * BeamBottom;
                lift = Mathf.Max(lift, sample.Position.y + WaterClearance - beamY);
            }
            return new AlpineVillageFootbridgePlan
            {
                Position = position + Vector3.up * lift,
                Rotation = rotation,
                Length = length,
                Crossing = water,
                PathId = pathId
            };

            float BankHeight(Vector3 point)
            {
                float height = float.NegativeInfinity;
                foreach (float side in new[] { -.5f, 0f, .5f })
                {
                    Vector2 probe = XZ(point + right * (Width * side));
                    height = Mathf.Max(height, AlpineVillageTerrainSampler.SampleHeight(plan, probe),
                        AlpineVillageTerrainSampler.SampleMeshHeight(plan, probe));
                }
                return height + AlpineVillageWorldBuilder.LaneSkinLift;
            }
        }

        private static Vector2 XZ(Vector3 value) => new Vector2(value.x, value.z);
        private static float Cross(Vector2 a, Vector2 b) => a.x * b.y - a.y * b.x;
    }
}

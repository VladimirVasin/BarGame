using System;
using System.Collections.Generic;
using UnityEngine;

namespace BarPromenade
{
    /// <summary>
    /// Low broken planting around the garden's back and wings. Its three
    /// connected runs leave a broad north opening and accept the existing
    /// eastern tree as part of the planted edge. Lamps mark turns and ends;
    /// none lines the main walk or sits between a bench and its view.
    /// </summary>
    public static class ChurchGardenBorderPlan
    {
        public const int HedgeCount = 28;
        public const int UplightCount = 10;
        public const float HedgeHeight = 0.8f;
        public const float HedgeDepth = 0.85f;
        public const float NorthOpeningWidth = 3.9f;
        private const float HedgeTipOverlap = 0.12f;
        private const float Tolerance = 0.001f;

        public static Rect GetNorthOpeningBounds(Rect grounds)
        {
            return Rect.MinMaxRect(grounds.xMin + 23.3f,
                grounds.yMin + 31.4f,
                grounds.xMin + 23.3f + NorthOpeningWidth, grounds.yMax);
        }

        internal static void Append(
            CityChurchPlan church,
            ICollection<CityChurchCourtyardFixtureDescriptor> fixtures)
        {
            int index = 0;
            AppendLine(church, fixtures, ref index,
                new Vector2(2.5f, 28f), new Vector2(2.5f, 34.5f), 4);
            AppendArc(church, fixtures, ref index,
                new Vector2(5.3f, 34.5f), 2.8f, 180f, 90f, 3);
            AppendLine(church, fixtures, ref index,
                new Vector2(5.3f, 37.3f), new Vector2(23f, 37.3f), 9);
            AppendLine(church, fixtures, ref index,
                new Vector2(27.5f, 37.3f), new Vector2(35.95f, 37.3f), 5);
            AppendLine(church, fixtures, ref index,
                new Vector2(43.1f, 26.5f), new Vector2(43.1f, 34.5f), 4);
            AppendArc(church, fixtures, ref index,
                new Vector2(40.3f, 34.5f), 2.8f, 0f, 90f, 3);

            AddLamp(church, fixtures, "west-end", new Vector2(3.4f, 28.2f),
                new Vector2(2.5f, 28.2f));
            AddLamp(church, fixtures, "west-turn", new Vector2(4.7f, 35.8f),
                new Vector2(3.5f, 36.4f));
            AddLamp(church, fixtures, "west-back-near", new Vector2(10.2f, 36.25f),
                new Vector2(10.2f, 37.3f));
            AddLamp(church, fixtures, "west-back", new Vector2(17.6f, 36.25f),
                new Vector2(17.6f, 37.3f));
            AddLamp(church, fixtures, "north-gap-west", new Vector2(22.5f, 36.25f),
                new Vector2(22.5f, 37.3f));
            AddLamp(church, fixtures, "north-gap-east", new Vector2(28f, 36.25f),
                new Vector2(28f, 37.3f));
            AddLamp(church, fixtures, "east-back", new Vector2(34.7f, 36.15f),
                new Vector2(34.7f, 37.3f));
            AddLamp(church, fixtures, "east-turn", new Vector2(41.1f, 35.8f),
                new Vector2(42.15f, 36.55f));
            AddLamp(church, fixtures, "east-end", new Vector2(42f, 26.9f),
                new Vector2(43.1f, 26.9f));
            AddLamp(church, fixtures, "statue", new Vector2(31.05f, 31.9f),
                new Vector2(32.4f, 32.7f), 1);
        }

        private static void AppendLine(
            CityChurchPlan church,
            ICollection<CityChurchCourtyardFixtureDescriptor> fixtures,
            ref int index,
            Vector2 first,
            Vector2 second,
            int count)
        {
            for (int segment = 0; segment < count; segment++)
            {
                AppendHedge(church, fixtures, index++,
                    Vector2.Lerp(first, second, segment / (float)count),
                    Vector2.Lerp(first, second, (segment + 1f) / count));
            }
        }

        private static void AppendArc(
            CityChurchPlan church,
            ICollection<CityChurchCourtyardFixtureDescriptor> fixtures,
            ref int index,
            Vector2 center,
            float radius,
            float startAngle,
            float endAngle,
            int count)
        {
            for (int segment = 0; segment < count; segment++)
            {
                float first = Mathf.Lerp(startAngle, endAngle,
                    segment / (float)count) * Mathf.Deg2Rad;
                float second = Mathf.Lerp(startAngle, endAngle,
                    (segment + 1f) / count) * Mathf.Deg2Rad;
                AppendHedge(church, fixtures, index++,
                    center + new Vector2(Mathf.Cos(first), Mathf.Sin(first)) * radius,
                    center + new Vector2(Mathf.Cos(second), Mathf.Sin(second)) * radius);
            }
        }

        private static void AppendHedge(
            CityChurchPlan church,
            ICollection<CityChurchCourtyardFixtureDescriptor> fixtures,
            int index,
            Vector2 first,
            Vector2 second)
        {
            Vector2 delta = second - first;
            Vector2 direction = delta.normalized;
            Vector2 position = church.Grounds.min + (first + second) * 0.5f;
            float length = delta.magnitude + HedgeTipOverlap;
            // Blender's segment runs along local X. The conservative AABB
            // rotates with that measured footprint, including curved turns.
            Vector2 half = new Vector2(
                Mathf.Abs(direction.x) * length +
                    Mathf.Abs(direction.y) * HedgeDepth,
                Mathf.Abs(direction.y) * length +
                    Mathf.Abs(direction.x) * HedgeDepth) * 0.5f;
            fixtures.Add(new CityChurchCourtyardFixtureDescriptor(
                $"church-courtyard-hedge-{index:00}",
                CityChurchCourtyardFixtureKind.Hedge, 0,
                new Vector3(position.x, church.GroundTopY, position.y),
                Quaternion.Euler(0f,
                    -Mathf.Atan2(direction.y, direction.x) * Mathf.Rad2Deg, 0f),
                new Vector3(length / 2f, 1f, 1f),
                Rect.MinMaxRect(position.x - half.x, position.y - half.y,
                    position.x + half.x, position.y + half.y)));
        }

        private static void AddLamp(
            CityChurchPlan church,
            ICollection<CityChurchCourtyardFixtureDescriptor> fixtures,
            string name,
            Vector2 localPosition,
            Vector2 localTarget,
            int variant = 0)
        {
            Vector2 position = church.Grounds.min + localPosition;
            Vector2 direction = (localTarget - localPosition).normalized;
            fixtures.Add(new CityChurchCourtyardFixtureDescriptor(
                "church-courtyard-uplight-" + name,
                CityChurchCourtyardFixtureKind.Uplight, variant,
                new Vector3(position.x, church.GroundTopY, position.y),
                Quaternion.LookRotation(new Vector3(direction.x, 0f, direction.y)),
                Vector3.one,
                Rect.MinMaxRect(position.x - 0.12f, position.y - 0.12f,
                    position.x + 0.12f, position.y + 0.12f)));
        }

        internal static void ValidateOrThrow(
            CityLayout layout,
            CityChurchCourtyardPlan plan)
        {
            if (plan.GetFixtureCount(CityChurchCourtyardFixtureKind.Hedge) !=
                    HedgeCount ||
                plan.GetFixtureCount(CityChurchCourtyardFixtureKind.Uplight) !=
                    UplightCount)
            {
                throw new InvalidOperationException(
                    "The garden border lost its bounded planting and light composition.");
            }

            Rect opening = GetNorthOpeningBounds(plan.Grounds);
            Rect inhabitedGarden = Rect.MinMaxRect(plan.Grounds.xMin + 6.6f,
                plan.Grounds.yMin + 21f, plan.Grounds.xMin + 41.2f,
                plan.Grounds.yMin + 35.8f);
            int statueLights = 0;
            var hedgeRun = new List<CityChurchCourtyardFixtureDescriptor>(HedgeCount);
            for (int index = 0; index < plan.Fixtures.Count; index++)
            {
                CityChurchCourtyardFixtureDescriptor fixture = plan.Fixtures[index];
                if (fixture.BlockerBounds.Overlaps(opening))
                {
                    throw new InvalidOperationException(
                        "The garden's broad north opening must stay unobstructed.");
                }

                bool hedge = fixture.Kind == CityChurchCourtyardFixtureKind.Hedge;
                bool lamp = fixture.Kind == CityChurchCourtyardFixtureKind.Uplight;
                if (!hedge && !lamp)
                {
                    continue;
                }

                if (hedge)
                {
                    hedgeRun.Add(fixture);
                }

                if (fixture.BlockerBounds.yMax > plan.Grounds.yMin +
                        CityChurchGroundPlan.FlatGardenDepth + Tolerance ||
                    (hedge && fixture.BlockerBounds.Overlaps(inhabitedGarden)))
                {
                    throw new InvalidOperationException(
                        "The low garden border must stay on the level outer planting edge.");
                }

                bool foundGround = CityTerrainSurfacePlan.TrySampleGroundTop(layout,
                    new Vector2(fixture.GroundPosition.x, fixture.GroundPosition.z),
                    out float top, out _);
                if (!foundGround ||
                    Mathf.Abs(top - fixture.GroundPosition.y) > Tolerance)
                {
                    throw new InvalidOperationException(
                        "Garden planting and uplights must touch their authoritative ground.");
                }

                if (lamp && fixture.Variant > 1)
                {
                    throw new InvalidOperationException(
                        "Garden uplights have only hedge and statue roles.");
                }

                if (lamp && fixture.Variant == 1)
                {
                    statueLights++;
                    Vector3 direction = CityChurchCourtyardPlanner.GetFixture(
                        plan, CityChurchCourtyardFixtureKind.Statue).GroundPosition -
                        fixture.GroundPosition;
                    direction.y = 0f;
                    if (Vector3.Dot(fixture.Rotation * Vector3.forward,
                        direction.normalized) < 0.99f)
                    {
                        throw new InvalidOperationException(
                            "The single statue uplight must face the stone figure.");
                    }
                }

                Rect clearance = fixture.BlockerBounds;
                clearance.xMin -= 0.10f;
                clearance.yMin -= 0.10f;
                clearance.xMax += 0.10f;
                clearance.yMax += 0.10f;
                for (int other = 0; other < plan.Fixtures.Count; other++)
                {
                    if (other == index ||
                        (hedge && plan.Fixtures[other].Kind ==
                            CityChurchCourtyardFixtureKind.Hedge))
                    {
                        continue;
                    }

                    if (clearance.Overlaps(plan.Fixtures[other].BlockerBounds))
                    {
                        throw new InvalidOperationException(
                            "Garden border planting and lights must clear existing furniture and trees.");
                    }
                }
            }

            if (statueLights != 1 || opening.width < 3f)
            {
                throw new InvalidOperationException(
                    "The garden requires one statue light and a generous north opening.");
            }

            // The two intentional breaks separate three planted runs. Short
            // segments within each run must keep touching around both arcs.
            for (int index = 1; index < hedgeRun.Count; index++)
            {
                if (index == 16 || index == 21)
                {
                    continue;
                }

                CityChurchCourtyardFixtureDescriptor previous = hedgeRun[index - 1];
                CityChurchCourtyardFixtureDescriptor current = hedgeRun[index];
                Vector3 previousEnd = previous.GroundPosition +
                    previous.Rotation * Vector3.right * previous.Scale.x;
                Vector3 currentStart = current.GroundPosition -
                    current.Rotation * Vector3.right * current.Scale.x;
                if (Vector3.Distance(previousEnd, currentStart) >
                    HedgeTipOverlap + Tolerance)
                {
                    throw new InvalidOperationException(
                        "A planted garden border run has broken into isolated bushes.");
                }
            }
        }
    }
}

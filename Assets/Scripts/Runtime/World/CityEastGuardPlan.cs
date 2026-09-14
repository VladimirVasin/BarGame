using System;
using UnityEngine;

namespace BarPromenade
{
    /// <summary>Measured, accessible stations and short patrols beside the closed civilian exit.</summary>
    public sealed class CityEastGuardPlan
    {
        private readonly CityEastExitPlan exit;
        private readonly Vector3[][] routes;
        public CityEastGuardPlan(CityEastExitPlan eastExit)
        {
            exit = eastExit ?? throw new ArgumentNullException(nameof(eastExit));
            if (!exit.IsEnabled) throw new InvalidOperationException("Guards need the existing eastern post.");
            float x = exit.CheckpointPosition.x, z = exit.CheckpointPosition.z;
            Vector3 senior = Ground(x - 5.2f, z + 4.5f);
            Vector3 junior = Ground(x - 6.5f, z - 4.6f);
            // The northern round stays near the fence, away from the yard's
            // utility sheds and the street-side service equipment.
            Vector3 northTurn = Ground(x - 3.6f, z + 10f);
            Vector3 northEnd = Ground(x - 4.8f, z + 16f);
            Vector3 southTurn = Ground(Mathf.Max(exit.YardBounds.xMin + 5f, x - 10.2f), z - 9f);
            // A compact approach shortens this leg before the street's
            // pavement and graded frontage instead of sending duty into it.
            Vector3 southEnd = Ground(Mathf.Max(exit.YardBounds.xMin + 4f, x - 17f), z - 9f);
            routes = new[] { new[] { senior, northTurn, northEnd, northTurn, senior },
                new[] { junior, southTurn, southEnd, southTurn, junior } };
            Influence = new Bounds(senior, Vector3.one * 3f);
            foreach (Vector3[] route in routes)
                foreach (Vector3 point in route)
                {
                    if (exit.ClosedGroundBounds.Contains(new Vector2(point.x, point.z)) ||
                        exit.BoothBounds.Contains(new Vector2(point.x, point.z)))
                        throw new InvalidOperationException("A checkpoint patrol enters the closed yard or booth.");
                    Bounds expanded = Influence; expanded.Encapsulate(point); Influence = expanded;
                }
        }
        public Bounds Influence { get; private set; }
        public Vector3 Station(int index) => routes[index][0];
        public Vector3 Target(int index, int waypoint) => routes[index][waypoint];
        public float Speed(int index) => index == 0 ? .72f : .89f;
        public Quaternion StationFacing(int index) => Quaternion.Euler(0f, index == 0 ? 242f : 300f, 0f);
        public Vector3 Ground(float x, float z) => new Vector3(x, GroundTop(new Vector2(x, z)), z);
        public float GroundTop(Vector2 point) => exit.RoadBounds.Contains(point)
            ? exit.SampleRoadTop(point.x, point.y) : exit.SampleGroundTop(point);
    }
}

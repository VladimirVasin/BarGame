using System;
using System.Collections.Generic;
using UnityEngine;

namespace BarPromenade
{
    public enum CityPigeonKeeperDayTask { DeckRail, GrainStore, Workbench, Gate, Bath, Sweep, ReturnToDeck }

    /// <summary>One leg of the keeper's closed work circuit and the physical
    /// contacts at its end. Feet and contacts are in City world metres.</summary>
    public sealed class CityPigeonKeeperDayStop
    {
        public CityPigeonKeeperDayTask Task { get; }
        public IReadOnlyList<Vector3> Route { get; }
        public Vector3 Stand => Route[Route.Count - 1];
        public Quaternion Facing { get; }
        public Vector3 RightContact { get; }
        public Vector3 LeftContact { get; }
        public float WorkSeconds { get; }

        private CityPigeonKeeperDayStop(CityPigeonKeeperDayTask task, List<Vector3> route, Vector3 forward,
            Vector3 rightContact, Vector3 leftContact, float seconds)
        {
            Task = task;
            Route = route.AsReadOnly();
            Facing = Quaternion.LookRotation(Vector3.ProjectOnPlane(forward, Vector3.up));
            RightContact = rightContact;
            LeftContact = leftContact;
            WorkSeconds = seconds;
        }

        internal static IReadOnlyList<CityPigeonKeeperDayStop> CreateLoop(CityHillsideGroundPlan ground,
            CityPigeonLoftPlan loft, List<Vector3> commute, int firstGroundWaypoint)
        {
            var yard = loft.Courtyard;
            var stops = new List<CityPigeonKeeperDayStop>(7);
            Vector3 Deck(float x, float y, float z) => loft.Origin + loft.Rotation * new Vector3(x, y, z);

            // Leave the shelf for the south end of the balcony, inside the guard.
            var rail = Deck(1.03f, 2f, -1.75f - CityPigeonKeeper.RestStandOff);
            Add(CityPigeonKeeperDayTask.DeckRail, new List<Vector3> { commute[0], rail },
                loft.Rotation * Vector3.right, Deck(1.47f, 2.95f, -1.91f), Deck(1.47f, 2.95f, -1.54f), 6f);

            // Reuse the commute's actual twelve treads and north-side path. Turn
            // at the first waypoint west of the structure so the descent never
            // cuts under the stair stringers or diagonally through the supports.
            int turn = firstGroundWaypoint;
            while (turn < commute.Count && commute[turn].x >= loft.Bounds.xMin - .7f) turn++;
            if (turn >= commute.Count) throw new InvalidOperationException("Missing west approach around the pigeon loft stairs.");
            var toGrain = new List<Vector3> { rail };
            for (int i = 0; i <= turn; i++) toGrain.Add(commute[i]);
            Vector3 passage = Earth(-145.35f, -334.75f);
            // Keep the head outside the low deck beam; the barrel is set
            // toward this open west edge so its lid stays within arm's reach.
            var grainStand = Earth(yard.GrainStore.x - .75f, yard.GrainStore.z - .09f);
            GroundLeg(toGrain, passage);
            GroundLeg(toGrain, grainStand);
            Quaternion storeRotation = Quaternion.Euler(0f, -90f, 0f);
            Vector3 Store(float x, float y, float z) => yard.GrainStore + storeRotation * new Vector3(x, y, z);
            Add(CityPigeonKeeperDayTask.GrainStore, toGrain, Vector3.right,
                Store(-.29f, .825f, .03f), Store(-.20f, .775f, .197f), 25f);

            // Both hands can reach the screw transverse handle on the south edge
            // of the bench. The hand lamp occupies the opposite end of the top.
            var benchStand = Earth(yard.Workbench.x - .65f, yard.Workbench.z - .91f);
            Quaternion benchRotation = Quaternion.Euler(0f, 180f, 0f);
            Vector3 Bench(float x, float y, float z) => yard.Workbench + benchRotation * new Vector3(x, y, z);
            Add(CityPigeonKeeperDayTask.Workbench, Path(grainStand, passage, Earth(-145.55f, -331.30f), benchStand),
                Vector3.forward, Bench(.49f, .946f, .46f), Bench(.74f, .946f, .46f), 60f);

            // Inspect a fixed panel beside the wicket. Its two welds are
            // outside the leaf's full swept circle, even when a visitor uses it.
            Vector3 a = yard.FencePosts[1], b = yard.FencePosts[2];
            Vector3 inward = Vector3.Cross(Vector3.up, b - a).normalized;
            Vector3 panel = Vector3.Lerp(a, b, .375f);
            var grilleStand = Earth(panel.x + inward.x * .47f, panel.z + inward.z * .47f);
            Add(CityPigeonKeeperDayTask.Gate, Path(benchStand, Earth(-148.25f, -329.30f), grilleStand),
                -inward, Vector3.Lerp(a, b, .5f) + Vector3.up * .938f,
                Vector3.Lerp(a, b, .25f) + Vector3.up * 1.062f, 20f);

            // The pigeon drinks on the south stone. Service the opposite lip,
            // leaving the bird's landing and its straight-in approach open.
            var bathStand = Earth(yard.Bath.x, yard.Bath.z + .80f);
            Add(CityPigeonKeeperDayTask.Bath, Path(grilleStand, Earth(-149.95f, -333.02f),
                Earth(-149.10f, -335.04f), bathStand), Vector3.back,
                yard.Bath + new Vector3(-.15f, .104f, .410f),
                yard.Bath + new Vector3(.15f, .104f, .410f), 30f);

            // Bristle end contacts on the soil, south of the loft. Approach
            // east of the dish and below the flight height of the open nests.
            Vector3 eastOfBath = Earth(-145.73f, -335.97f);
            Vector3 southApron = Earth(-145.05f, -337.25f);
            Vector3 sweepStand = Earth(-144.60f, -338.06f);
            Add(CityPigeonKeeperDayTask.Sweep, Path(bathStand, eastOfBath, southApron, sweepStand), Vector3.back,
                Earth(-144.95f, -338.54f), Earth(-144.26f, -338.54f), 35f);

            var toDeck = Path(sweepStand, southApron, eastOfBath, passage, commute[turn]);
            for (int i = turn - 1; i >= 0; i--) toDeck.Add(commute[i]);
            Add(CityPigeonKeeperDayTask.ReturnToDeck, toDeck, loft.Rotation * Vector3.forward,
                Deck(.28f, 2.75f, -1.35f), Deck(-.32f, 2.79f, -1.39f), CityPigeonKeeper.CycleSeconds);

            return stops.AsReadOnly();

            void Add(CityPigeonKeeperDayTask task, List<Vector3> route, Vector3 forward, Vector3 right, Vector3 left, float seconds) =>
                stops.Add(new CityPigeonKeeperDayStop(task, route, forward, right, left, seconds));

            Vector3 Earth(float x, float z)
            {
                if (!ground.TrySampleTop(new Vector2(x, z), out float top))
                    throw new InvalidOperationException($"Missing pigeon keeper work surface at ({x}, {z}).");
                return new Vector3(x, top, z);
            }

            List<Vector3> Path(Vector3 start, params Vector3[] corners)
            {
                var route = new List<Vector3> { start };
                foreach (var corner in corners) GroundLeg(route, corner);
                return route;
            }

            void GroundLeg(List<Vector3> route, Vector3 end)
            {
                Vector3 start = route[route.Count - 1];
                int samples = Mathf.Max(1, Mathf.CeilToInt(Vector3.Distance(start, end) / .65f));
                for (int i = 1; i < samples; i++)
                {
                    Vector3 p = Vector3.Lerp(start, end, i / (float)samples);
                    route.Add(Earth(p.x, p.z));
                }
                route.Add(end);
            }
        }

    }
}

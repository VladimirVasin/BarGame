using System;
using System.Collections.Generic;
using UnityEngine;

namespace BarPromenade
{
    [DefaultExecutionOrder(325)]
    internal sealed class CityJanitorDirector : MonoBehaviour
    {
        internal const float EntryNear = 14f, EntryFar = 34f, ReleaseFar = 52f;
        private const float Separation = 23f, RepeatDistance = 9f;
        private const int ProbeBudget = 48;
        private readonly CityJanitorWorker[] workers = new CityJanitorWorker[2];
        private readonly List<CityAmbientWalkPlan.Entry> entries = new(192);
        private readonly Plane[] planes = new Plane[6];
        private CityGameRoot city;
        private CityJanitorSite sites;
        private System.Random random;
        private Transform broomTemplate;
        private Func<Vector3, bool> available;
        private float spawnIn = 3f, reviewIn;
        private int nextWorker;
        internal IReadOnlyList<CityJanitorWorker> Workers => workers;

        internal static void Create(CityGameRoot city, Func<Vector3, bool> available)
        {
            var root = new GameObject("Ambient City Janitors");
            root.transform.SetParent(city.transform, false);
            var director = root.AddComponent<CityJanitorDirector>();
            director.city = city;
            director.available = available;
            director.sites = new CityJanitorSite(city);
            director.random = new System.Random(city.Layout.Seed ^ 0x21a5b3);
        }

        private void LateUpdate() => Advance(Time.deltaTime);

        internal void Advance(float delta)
        {
            if (city == null || !city.IsInitialized || city.IsDormant || city.Camera == null ||
                GameTimeScaleRuntime.IsPaused || delta <= 0f) return;
            Vector3 hero = city.Player.GameObject.transform.position;
            spawnIn -= delta;
            reviewIn -= delta;
            bool review = reviewIn <= 0f || spawnIn <= 0f;
            if (review)
            {
                GeometryUtility.CalculateFrustumPlanes(city.Camera, planes);
                reviewIn = .75f;
            }
            foreach (var worker in workers)
            {
                if (worker == null || !worker.Active) continue;
                float distance = (worker.Feet - hero).sqrMagnitude;
                if (review && (!available(worker.Feet) || (distance > ReleaseFar * ReleaseFar ||
                    worker.Age >= worker.Duration && distance > EntryNear * EntryNear) && OutsideView(worker.Feet)))
                    worker.Leave();
                else worker.Advance(delta, distance);
            }
            if (spawnIn > 0f) return;
            spawnIn = TryArrive(hero) ? Range(18f, 32f) : 4f;
        }

        private bool TryArrive(Vector3 hero)
        {
            int slot = nextWorker;
            if (workers[slot]?.Active == true) slot = 1 - slot;
            if (workers[slot]?.Active == true) return false;
            var plan = city.AmbientPedestrians.WalkPlan;
            plan.CollectEntries(hero, EntryFar + 6f, entries);
            // Take a bounded random sample without replacement. Each visit samples fresh
            // distances along and away from the street, including when the hero stands still.
            int streetTries = Mathf.Min(entries.Count, ProbeBudget / 2);
            float bearing = Range(0f, 360f);
            for (int i = 0; i < ProbeBudget; i++)
            {
                Vector3 candidate, side;
                if (i < streetTries)
                {
                    int pick = random.Next(i, entries.Count);
                    var entry = entries[pick];
                    entries[pick] = entries[i]; entries[i] = entry;
                    float along = Mathf.Clamp(entry.Distance + Range(-1.6f, 1.6f), 0f, plan.Length(entry.Link));
                    Vector3 lane = plan.Sample(entry.Link, along, out var tangent);
                    side = Vector3.Cross(Vector3.up, tangent) * (random.Next(2) == 0 ? -1f : 1f);
                    candidate = lane + side * Range(1.85f, 5f);
                }
                else
                {
                    // Courtyards can extend well behind a narrow frontage. Probe the ring
                    // around the current hero even where the street graph has no entries.
                    side = Quaternion.Euler(0f, bearing + i * 137.5f, 0f) * Vector3.forward;
                    candidate = hero + side * Range(EntryNear, EntryFar);
                    if (!sites.TryGroundHeight(candidate, out candidate.y)) continue;
                }
                float distance = (candidate - hero).sqrMagnitude;
                if (distance < EntryNear * EntryNear || distance > EntryFar * EntryFar ||
                    Mathf.Abs(candidate.y - hero.y) > 4f || !available(candidate) || !OutsideView(candidate) ||
                    TooCloseToPreviousWork(candidate) || !city.AmbientPedestrians.ClearOfPeople(candidate) ||
                    !ClearOfWalkingLanes(plan, candidate) || !sites.TryFit(candidate, out var foot, out var normal) ||
                    !OutsideView(foot)) continue;

                if (workers[slot] == null)
                {
                    if (broomTemplate == null)
                    {
                        var model = Resources.Load<GameObject>("City/PigeonLoft/PigeonLoft");
                        broomTemplate = model == null ? null : CityPedestrianHandProps.FindSocket(model.transform, "Broom");
                        if (broomTemplate == null) throw new InvalidOperationException("Missing imported city broom.");
                    }
                    workers[slot] = new CityJanitorWorker(transform, slot == 0 ? DefaultNpcPopulation.CityJanitorFirst :
                        DefaultNpcPopulation.CityJanitorSecond, broomTemplate);
                }
                workers[slot].Arrive(foot, normal, Quaternion.AngleAxis(Range(-32f, 32f), Vector3.up) * side,
                    Range(0f, 18f), Range(.90f, 1.10f), Range(55f, 95f));
                nextWorker = 1 - slot;
                return true;
            }
            return false;
        }

        private bool ClearOfWalkingLanes(CityAmbientWalkPlan plan, Vector3 candidate)
        {
            foreach (var entry in entries)
            {
                if ((entry.Position - candidate).sqrMagnitude > 5f * 5f) continue;
                Vector3 nearest = plan.Sample(entry.Link, plan.ProjectDistance(entry.Link, candidate), out _);
                if ((nearest - candidate).sqrMagnitude < 1.65f * 1.65f) return false;
            }
            return true;
        }

        private bool TooCloseToPreviousWork(Vector3 point)
        {
            foreach (var worker in workers)
            {
                if (worker == null) continue;
                float radius = worker.Active ? Separation : RepeatDistance;
                if ((point - worker.Feet).sqrMagnitude < radius * radius) return true;
            }
            return false;
        }

        private bool OutsideView(Vector3 foot) => !GeometryUtility.TestPlanesAABB(planes,
            new Bounds(foot + Vector3.up, new Vector3(2f, 2.5f, 2f)));

        private float Range(float low, float high) => Mathf.Lerp(low, high, (float)random.NextDouble());

        private void OnDisable()
        {
            foreach (var worker in workers) worker?.Leave();
            spawnIn = 3f;
        }
    }
}

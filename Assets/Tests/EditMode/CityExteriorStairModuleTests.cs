using System;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;

namespace BarPromenade.Tests.EditMode
{
    public sealed class CityExteriorStairModuleTests
    {
        [Test]
        public void PlannerCreatesConnectedGuardedStraightFlight()
        {
            CityExteriorStairPlan plan = CreatePlan();

            Assert.DoesNotThrow(
                () => CityExteriorStairValidator.ValidateOrThrow(plan));
            Assert.That(plan.Flights, Has.Count.EqualTo(1));
            Assert.That(plan.Landings, Has.Count.EqualTo(2));
            Assert.That(plan.Rails, Has.Count.EqualTo(6));
            Assert.That(plan.RetainingWalls, Has.Count.EqualTo(2));

            CityExteriorStairFlightDescriptor flight = plan.Flights[0];
            Assert.That(flight.StepCount, Is.EqualTo(8));
            Assert.That(flight.StepRise, Is.EqualTo(0.16f));
            Assert.That(flight.TreadDepth, Is.EqualTo(0.32f));
            Assert.That(flight.TotalRise, Is.EqualTo(1.28f).Within(0.0001f));
            Assert.That(flight.RunLength, Is.EqualTo(2.56f).Within(0.0001f));
            Assert.That(flight.Direction, Is.EqualTo(Vector3.forward));
            Assert.That(
                Vector3.Distance(
                    plan.Landings[0].EndEdgeCenter,
                    flight.Start),
                Is.LessThan(0.0001f));
            Assert.That(
                Vector3.Distance(
                    plan.Landings[1].StartEdgeCenter,
                    flight.End),
                Is.LessThan(0.0001f));
        }

        [Test]
        public void PlannerRejectsGeometryOutsideExteriorContract()
        {
            Assert.Throws<InvalidOperationException>(
                () => CreatePlan(stepCount: 5));
            Assert.Throws<InvalidOperationException>(
                () => CreatePlan(stepCount: 13));
            Assert.Throws<InvalidOperationException>(
                () => CreatePlan(stepRise: 0.14f));
            Assert.Throws<InvalidOperationException>(
                () => CreatePlan(stepRise: 0.18f));
            Assert.Throws<InvalidOperationException>(
                () => CreatePlan(treadDepth: 0.29f));
            Assert.Throws<InvalidOperationException>(
                () => CreatePlan(treadDepth: 0.35f));
            Assert.Throws<InvalidOperationException>(
                () => CreatePlan(landingLength: 1.49f));
            Assert.Throws<InvalidOperationException>(
                () => CityExteriorStairPlanner.CreateStraightFlight(
                    "invalid-direction",
                    Vector3.zero,
                    Vector3.up,
                    2.4f,
                    8));
        }

        [Test]
        public void ValidatorRejectsExposedEdgeWithoutItsRail()
        {
            CityExteriorStairPlan source = CreatePlan();
            var rails = new List<CityExteriorStairRailDescriptor>(
                source.Rails.Count - 1);
            for (int index = 1; index < source.Rails.Count; index++)
            {
                rails.Add(source.Rails[index]);
            }

            var unguarded = new CityExteriorStairPlan(
                source.Id,
                new List<CityExteriorStairFlightDescriptor>(source.Flights),
                new List<CityExteriorStairLandingDescriptor>(source.Landings),
                rails,
                new List<CityExteriorStairRetainingWallDescriptor>(
                    source.RetainingWalls));

            InvalidOperationException exception =
                Assert.Throws<InvalidOperationException>(
                    () => CityExteriorStairValidator.ValidateOrThrow(
                        unguarded));
            Assert.That(exception.Message, Does.Contain("guard rail"));
        }

        [Test]
        public void BuilderUsesOnlyOneInvisibleRampColliderPerFlight()
        {
            CityExteriorStairPlan plan = CreatePlan();
            var parent = new GameObject("Exterior Stair Test Parent");

            try
            {
                CityExteriorStairWorldResult result =
                    CityExteriorStairWorldBuilder.Build(
                        parent.transform,
                        plan);
                Transform flights = result.Root.transform.Find("Flights");
                Transform flight = flights.Find(plan.Flights[0].Id);
                Transform steps = flight.Find("Visible Steps");

                Assert.That(steps.childCount, Is.EqualTo(8));
                for (int index = 0; index < steps.childCount; index++)
                {
                    GameObject step = steps.GetChild(index).gameObject;
                    Renderer renderer = step.GetComponent<Renderer>();
                    Assert.That(renderer, Is.Not.Null);
                    Assert.That(renderer.enabled, Is.True);
                    Assert.That(step.GetComponent<Collider>(), Is.Null);
                    Assert.That(
                        renderer.sharedMaterial,
                        Is.SameAs(RuntimePrimitiveFactory.DefaultMaterial));
                }

                Assert.That(result.RampColliders, Has.Count.EqualTo(1));
                Assert.That(
                    flight.GetComponentsInChildren<Collider>(true),
                    Has.Length.EqualTo(1));
                Collider rampCollider = result.RampColliders[0];
                Assert.That(rampCollider, Is.TypeOf<BoxCollider>());
                Assert.That(rampCollider.enabled, Is.True);
                Assert.That(
                    rampCollider.GetComponents<Collider>(),
                    Has.Length.EqualTo(1));
                Assert.That(
                    rampCollider.GetComponent<Renderer>().enabled,
                    Is.False);

                Transform landings = result.Root.transform.Find("Landings");
                Assert.That(landings.childCount, Is.EqualTo(2));
                for (int index = 0; index < landings.childCount; index++)
                {
                    Assert.That(
                        landings.GetChild(index).GetComponent<Collider>(),
                        Is.Not.Null);
                }
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(parent);
            }
        }

        private static CityExteriorStairPlan CreatePlan(
            int stepCount = 8,
            float stepRise = 0.16f,
            float treadDepth = 0.32f,
            float landingLength = 1.5f)
        {
            return CityExteriorStairPlanner.CreateStraightFlight(
                "old-town-waterworks-shortcut",
                new Vector3(3f, 0.6f, -4f),
                Vector3.forward,
                2.4f,
                stepCount,
                stepRise,
                treadDepth,
                landingLength,
                CityExteriorStairDropSide.Both,
                true);
        }
    }
}

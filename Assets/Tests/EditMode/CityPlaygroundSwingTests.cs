using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;

namespace BarPromenade.Tests.EditMode
{
    /// <summary>
    /// The park playground: a frame that is actually braced, a bench the
    /// swings cannot reach, and two seats that hang as real pendulums
    /// rather than as baked boxes.
    /// </summary>
    public sealed class CityPlaygroundSwingTests
    {
        // PlayerFactory builds the hero's controller at this radius.
        private const float PlayerCapsuleRadius = 0.32f;

        [Test]
        public void Recipe_BracesEachFrameSideAndLeavesTheBayEmpty()
        {
            CityLayout layout = CreateLayout();
            CityDecorationDescriptor playground = ResolvePlayground(
                layout,
                out Vector3 origin,
                out Vector3 forward);
            var parts = new List<Bounds>();
            CityDecorationWorldBuilder.AppendPartBounds(
                layout,
                playground,
                parts);

            var crossBeams = new List<Vector3>();
            int topBeams = 0;
            for (int index = 0; index < parts.Count; index++)
            {
                Vector3 center = ToLocalPoint(
                    origin,
                    forward,
                    parts[index].center);
                Vector3 size = ToLocalSize(forward, parts[index].size);
                if (Mathf.Abs(
                        size.z - CityPlaygroundGeometry.CrossBeamDepth) <
                    0.01f &&
                    Mathf.Abs(
                        size.x -
                        CityPlaygroundGeometry.CrossBeamThickness) <
                    0.01f)
                {
                    crossBeams.Add(center);
                }

                if (Mathf.Abs(
                        size.x - CityPlaygroundGeometry.TopBeamWidth) <
                    0.01f)
                {
                    topBeams++;
                    Assert.That(
                        center.y,
                        Is.EqualTo(CityPlaygroundGeometry.TopBeamY)
                            .Within(0.001f));
                }

                // Nothing is drawn in the bay any more: the seats and
                // their ropes hang there as bodies instead.
                bool insideBay =
                    center.y < CityPlaygroundGeometry.RopeAnchorY - 0.1f &&
                    Mathf.Abs(center.x) <
                    CityPlaygroundGeometry.PostOffsetX - 0.4f &&
                    Mathf.Abs(center.z) < 1f;
                Assert.That(
                    insideBay,
                    Is.False,
                    "The swing bay must stay clear of baked parts.");
            }

            Assert.That(
                topBeams,
                Is.EqualTo(1),
                "One long beam spans the frame.");
            Assert.That(
                crossBeams,
                Has.Count.EqualTo(2),
                "Each A-frame is capped by its own cross beam.");
            crossBeams.Sort((first, second) => first.x.CompareTo(second.x));
            Assert.That(
                crossBeams[0].x,
                Is.EqualTo(-CityPlaygroundGeometry.PostOffsetX)
                    .Within(0.001f));
            Assert.That(
                crossBeams[1].x,
                Is.EqualTo(CityPlaygroundGeometry.PostOffsetX)
                    .Within(0.001f));
            for (int index = 0; index < crossBeams.Count; index++)
            {
                Assert.That(
                    crossBeams[index].y,
                    Is.EqualTo(CityPlaygroundGeometry.CrossBeamY)
                        .Within(0.001f));
                Assert.That(
                    crossBeams[index].z,
                    Is.EqualTo(0f).Within(0.001f));
                Assert.That(
                    crossBeams[index].y,
                    Is.LessThan(CityPlaygroundGeometry.TopBeamY),
                    "The long beam lands on top of the cross beams.");
            }
        }

        [Test]
        public void Build_HangsTwoPushablePendulumsUnderTheBeam()
        {
            CityLayout layout = CreateLayout();
            CityDecorationPlan plan = CreatePlan(layout);
            ResolvePlayground(
                layout,
                out Vector3 origin,
                out Vector3 forward);
            var parent = new GameObject("Playground Swing Test");
            try
            {
                GameObject root = CityPlaygroundSwingBuilder.Build(
                    parent.transform,
                    layout,
                    plan,
                    CityDecorationWorldBuilder.MasonryBatchColor,
                    CityDecorationWorldBuilder.StreetBatchColor);
                Assert.That(
                    root,
                    Is.Not.Null,
                    "The default city plants a playground.");

                CityPlaygroundSwing[] swings =
                    root.GetComponentsInChildren<CityPlaygroundSwing>(
                        true);
                Assert.That(swings, Has.Length.EqualTo(2));

                var seatOffsets = new List<float>();
                foreach (CityPlaygroundSwing swing in swings)
                {
                    Assert.That(swing.IsInitialized, Is.True);

                    // The pivot is the beam, and the plank hangs one
                    // rope below it.
                    Vector3 pivot = ToLocalPoint(
                        origin,
                        forward,
                        swing.transform.position);
                    Assert.That(
                        pivot.y,
                        Is.EqualTo(CityPlaygroundGeometry.RopeAnchorY)
                            .Within(0.001f));
                    Assert.That(
                        Mathf.Abs(pivot.x),
                        Is.EqualTo(CityPlaygroundGeometry.SwingOffsetX)
                            .Within(0.001f));
                    Assert.That(pivot.z, Is.EqualTo(0f).Within(0.001f));
                    seatOffsets.Add(pivot.x);

                    Vector3 seat = ToLocalPoint(
                        origin,
                        forward,
                        swing.SeatCenter);
                    Assert.That(
                        seat.y,
                        Is.EqualTo(CityPlaygroundGeometry.SeatCenterY)
                            .Within(0.001f));

                    // The seat travels along the recipe's own forward,
                    // never sideways along the beam.
                    Assert.That(swing.PushAxis.y, Is.EqualTo(0f).Within(0.001f));
                    Assert.That(
                        Mathf.Abs(Vector3.Dot(swing.PushAxis, forward)),
                        Is.EqualTo(1f).Within(0.001f));

                    Rigidbody body = swing.Body;
                    Assert.That(body, Is.Not.Null);
                    Assert.That(body.isKinematic, Is.False);
                    Assert.That(body.useGravity, Is.True);
                    Assert.That(
                        body.sleepThreshold,
                        Is.EqualTo(0f),
                        "A resting swing must still report a push.");
                    Assert.That(
                        body.centerOfMass.y,
                        Is.EqualTo(-CityPlaygroundGeometry.RopeLength)
                            .Within(0.001f));

                    var joint = swing.GetComponent<HingeJoint>();
                    Assert.That(joint, Is.Not.Null);
                    Assert.That(joint.connectedBody, Is.Null);
                    Assert.That(joint.anchor, Is.EqualTo(Vector3.zero));
                    Assert.That(joint.axis, Is.EqualTo(Vector3.right));
                    Assert.That(joint.useLimits, Is.True);
                    Assert.That(
                        joint.limits.max,
                        Is.EqualTo(
                            CityPlaygroundGeometry.SwingLimitDegrees)
                            .Within(0.001f));
                    Assert.That(
                        joint.limits.min,
                        Is.EqualTo(
                            -CityPlaygroundGeometry.SwingLimitDegrees)
                            .Within(0.001f));

                    // One solid plank to walk into, one volume that
                    // reads the walking as a push.
                    BoxCollider[] colliders =
                        swing.GetComponents<BoxCollider>();
                    Assert.That(colliders, Has.Length.EqualTo(2));
                    int triggers = 0;
                    foreach (BoxCollider collider in colliders)
                    {
                        if (collider.isTrigger)
                        {
                            triggers++;
                        }
                    }

                    Assert.That(triggers, Is.EqualTo(1));

                    Renderer[] renderers =
                        swing.GetComponentsInChildren<Renderer>(true);
                    Assert.That(renderers, Has.Length.EqualTo(2));
                    foreach (Renderer renderer in renderers)
                    {
                        var properties = new MaterialPropertyBlock();
                        renderer.GetPropertyBlock(properties);
                        Assert.That(
                            properties.GetTexture(
                                Shader.PropertyToID("_BaseMap")),
                            Is.SameAs(
                                CityParkSurfaceAppearance.GetTexture(
                                    CityParkSurfaceKind.Timber)),
                            renderer.name);
                        Assert.That(
                            renderer.name,
                            Does.StartWith("Park "));
                    }
                }

                Assert.That(
                    seatOffsets[0],
                    Is.Not.EqualTo(seatOffsets[1]),
                    "The two seats hang either side of the frame.");
            }
            finally
            {
                Object.DestroyImmediate(parent);
            }
        }

        [Test]
        public void Playground_KeepsTheBenchAndItsDockOffTheSwingArc()
        {
            CityLayout layout = CreateLayout();
            CityDecorationDescriptor playground = ResolvePlayground(
                layout,
                out Vector3 origin,
                out Vector3 forward);
            var seats = new List<CityBenchSeat>();
            CityDecorationWorldBuilder.AppendBenchSeats(
                layout,
                playground,
                seats);
            Assert.That(seats, Has.Count.EqualTo(1));

            CityBenchSeat seat = seats[0];
            float reach = CityPlaygroundGeometry.SeatReach;
            Assert.That(
                reach,
                Is.GreaterThan(1.5f),
                "A swing at its limit really does travel.");

            // The bench's own front edge, and the spot the hero stands
            // on to sit down, both stay outside the swept arc.
            float benchFront =
                ToLocalPoint(origin, forward, seat.SeatTopCenter).z -
                (seat.SeatDepth * 0.5f);
            Assert.That(
                benchFront,
                Is.GreaterThan(reach + 1f),
                "The bench must not sit inside the swing arc.");

            Vector3 dock = seat.SeatTopCenter + seat.FaceDirection *
                ((seat.SeatDepth * 0.5f) +
                 CityBenchSitPlan.EntryEdgeDistance);
            float dockFront =
                ToLocalPoint(origin, forward, dock).z -
                PlayerCapsuleRadius;
            Assert.That(
                dockFront,
                Is.GreaterThan(reach + 0.25f),
                "A swing must not sweep through the seated hero.");
        }

        [Test]
        public void Proxies_BlockTheFramesAndOpenTheSwingBay()
        {
            CityLayout layout = CreateLayout();
            CityDecorationDescriptor playground = ResolvePlayground(
                layout,
                out Vector3 origin,
                out Vector3 forward);
            var proxies = new List<Bounds>();
            CityStaticCollisionBuilder.AddDecorationProxyBounds(
                layout,
                playground,
                proxies);
            Assert.That(proxies, Has.Count.EqualTo(3));

            Vector3 tangent = new Vector3(-forward.z, 0f, forward.x);
            for (int side = -1; side <= 1; side += 2)
            {
                Assert.That(
                    IsBlocked(
                        proxies,
                        origin +
                        (tangent *
                         (side * CityPlaygroundGeometry.PostOffsetX)) +
                        (Vector3.up * 1.2f)),
                    Is.True,
                    "Each A-frame stays solid.");

                // Standing at the seat, and behind it at the far end of
                // the arc, so the hero can walk up and push.
                Vector3 seat =
                    origin +
                    (tangent *
                     (side * CityPlaygroundGeometry.SwingOffsetX)) +
                    (Vector3.up * CityPlaygroundGeometry.SeatCenterY);
                Assert.That(IsBlocked(proxies, seat), Is.False);
                Assert.That(
                    IsBlocked(
                        proxies,
                        seat +
                        (forward * CityPlaygroundGeometry.SeatReach)),
                    Is.False);
                Assert.That(
                    IsBlocked(
                        proxies,
                        seat -
                        (forward * CityPlaygroundGeometry.SeatReach)),
                    Is.False);
            }
        }

        private static bool IsBlocked(
            IReadOnlyList<Bounds> proxies,
            Vector3 point)
        {
            for (int index = 0; index < proxies.Count; index++)
            {
                if (proxies[index].Contains(point))
                {
                    return true;
                }
            }

            return false;
        }

        private static CityLayout CreateLayout()
        {
            return CityLayoutGenerator.Generate(
                CityBlueprintCatalog.Default,
                CityGenerationSettings.Default,
                GameSessionState.DefaultCitySeed);
        }

        private static CityDecorationPlan CreatePlan(CityLayout layout)
        {
            return CityDecorationPlanner.CreatePlan(
                layout,
                RoadFencePlanner.CreatePlan(layout),
                CityNightFixturePlanner.CreatePlan(layout));
        }

        private static CityDecorationDescriptor ResolvePlayground(
            CityLayout layout,
            out Vector3 origin,
            out Vector3 forward)
        {
            CityDecorationPlan plan = CreatePlan(layout);
            foreach (CityDecorationDescriptor descriptor in
                     plan.Descriptors)
            {
                if (descriptor.Kind !=
                    CityDecorationKind.ParkPlayground)
                {
                    continue;
                }

                Assert.That(
                    CityDecorationWorldBuilder.TryDescribeRecipeBasis(
                        layout,
                        descriptor,
                        out origin,
                        out forward),
                    Is.True);
                return descriptor;
            }

            Assert.Fail("The default city must plant a playground.");
            origin = default;
            forward = default;
            return default;
        }

        private static Vector3 ToLocalPoint(
            Vector3 origin,
            Vector3 forward,
            Vector3 world)
        {
            Vector3 tangent = new Vector3(-forward.z, 0f, forward.x);
            Vector3 delta = world - origin;
            return new Vector3(
                Vector3.Dot(delta, tangent),
                delta.y,
                Vector3.Dot(delta, forward));
        }

        private static Vector3 ToLocalSize(Vector3 forward, Vector3 size)
        {
            Vector3 tangent = new Vector3(-forward.z, 0f, forward.x);
            return new Vector3(
                Mathf.Abs(Vector3.Dot(size, tangent)),
                size.y,
                Mathf.Abs(Vector3.Dot(size, forward)));
        }
    }
}

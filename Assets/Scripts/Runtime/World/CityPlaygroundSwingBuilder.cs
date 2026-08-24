using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace BarPromenade
{
    /// <summary>
    /// Hangs the park playground's two swings. Every other decoration is
    /// baked into a batched chunk mesh; a swing cannot be, because it has
    /// to move. Each seat is one hinged rigid body under the top beam
    /// with its two ropes drawn between the beam and the plank, so the
    /// hero pushes a real pendulum instead of walking past a picture of
    /// one.
    ///
    /// PhysX cloth was the other candidate for the ropes and was
    /// rejected: cloth is driven by the world and drives nothing back,
    /// so a cloth rope could not carry a seat, let alone be pushed by
    /// the hero. The ropes are taut under the plank's weight anyway.
    /// </summary>
    internal static class CityPlaygroundSwingBuilder
    {
        public const string RootName = "Park Playground Swings";
        public const string SeatRendererName = "Park Timber Swing Seat";
        public const string RopeRendererName = "Park Timber Swing Ropes";

        /// <summary>A plank and two ropes: light enough to answer a
        /// walking push, heavy enough not to be shoved aside.</summary>
        public const float SeatMass = 9f;

        public static GameObject Build(
            Transform parent,
            CityLayout layout,
            CityDecorationPlan plan,
            Color seatColor,
            Color ropeColor)
        {
            if (parent == null)
            {
                throw new ArgumentNullException(nameof(parent));
            }

            if (layout == null)
            {
                throw new ArgumentNullException(nameof(layout));
            }

            if (plan == null)
            {
                throw new ArgumentNullException(nameof(plan));
            }

            Transform root = null;
            IReadOnlyList<CityDecorationDescriptor> descriptors =
                plan.Descriptors;
            for (int index = 0; index < descriptors.Count; index++)
            {
                CityDecorationDescriptor descriptor = descriptors[index];
                if (descriptor.Kind !=
                    CityDecorationKind.ParkPlayground ||
                    !CityDecorationWorldBuilder.TryDescribeRecipeBasis(
                        layout,
                        descriptor,
                        out Vector3 origin,
                        out Vector3 forward))
                {
                    continue;
                }

                if (root == null)
                {
                    root = new GameObject(RootName).transform;
                    root.SetParent(parent, false);
                }

                BuildFrameSwings(
                    root,
                    descriptor.StableId,
                    origin,
                    forward,
                    seatColor,
                    ropeColor);
            }

            return root == null ? null : root.gameObject;
        }

        private static void BuildFrameSwings(
            Transform parent,
            string stableId,
            Vector3 origin,
            Vector3 forward,
            Color seatColor,
            Color ropeColor)
        {
            Transform frame = new GameObject($"{stableId} Swings").transform;
            frame.SetParent(parent, false);
            frame.localPosition = origin;
            frame.localRotation = Quaternion.LookRotation(
                forward,
                Vector3.up);
            for (int side = -1; side <= 1; side += 2)
            {
                BuildSwing(frame, side, seatColor, ropeColor);
            }
        }

        private static void BuildSwing(
            Transform frame,
            int side,
            Color seatColor,
            Color ropeColor)
        {
            var swing = new GameObject(
                $"Swing {(side < 0 ? 'A' : 'B')}");
            Transform pivot = swing.transform;
            pivot.SetParent(frame, false);

            // The object's own origin is the pivot: it sits on the beam,
            // and everything it carries hangs below it.
            pivot.localPosition = new Vector3(
                side * CityPlaygroundGeometry.SwingOffsetX,
                CityPlaygroundGeometry.RopeAnchorY,
                0f);

            Vector3 seatCenter = SeatLocalCenter;
            AddMesh(
                pivot,
                SeatRendererName,
                new[]
                {
                    new Bounds(
                        seatCenter,
                        new Vector3(
                            CityPlaygroundGeometry.SeatWidth,
                            CityPlaygroundGeometry.SeatThickness,
                            CityPlaygroundGeometry.SeatDepth))
                },
                seatColor);
            AddMesh(
                pivot,
                RopeRendererName,
                new[]
                {
                    CreateRope(-1),
                    CreateRope(1)
                },
                ropeColor);

            BoxCollider plank = swing.AddComponent<BoxCollider>();
            plank.center = seatCenter;
            plank.size = new Vector3(
                CityPlaygroundGeometry.SeatWidth,
                CityPlaygroundGeometry.SeatColliderHeight,
                CityPlaygroundGeometry.SeatDepth);

            BoxCollider push = swing.AddComponent<BoxCollider>();
            push.isTrigger = true;
            // Lifted just enough that the volume's floor clears the
            // lawn: a box dipping into the park's MeshCollider keeps
            // OnTriggerStay firing against the terrain every physics
            // step for the whole session.
            //
            // The height that matters is the seat's above the GROUND, and
            // it has to be spelled that way. `seatCenter` is the plank in
            // the PIVOT's space - straight down the rope from the beam, so
            // its y is about -2.3 - and subtracting that lifted the volume
            // by three metres instead of four centimetres. The trigger sat
            // up by the crossbar where no walker could ever reach it, so
            // the swing could not be pushed at all: not in this test, and
            // not in the park either.
            float pushLift = Mathf.Max(
                0f,
                (CityPlaygroundGeometry.PushVolumeHeight * 0.5f) +
                0.01f -
                CityPlaygroundGeometry.SeatCenterY);
            push.center = seatCenter + (Vector3.up * pushLift);
            push.size = new Vector3(
                CityPlaygroundGeometry.PushVolumeWidth,
                CityPlaygroundGeometry.PushVolumeHeight,
                CityPlaygroundGeometry.PushVolumeDepth);

            Rigidbody body = swing.AddComponent<Rigidbody>();
            body.mass = SeatMass;
            body.useGravity = true;
            body.isKinematic = false;
            body.interpolation = RigidbodyInterpolation.Interpolate;
            body.collisionDetectionMode =
                CollisionDetectionMode.ContinuousSpeculative;
            body.linearDamping = 0.02f;

            // A swing hanging straight down is at equilibrium, so PhysX
            // puts its body to sleep - and a sleeping body receives no
            // trigger callbacks at all. That means a swing that has come to
            // rest can never be pushed again: the walker steps into the
            // push volume and nothing hears it. Keeping this one body awake
            // is the cost of the whole feature working the second time.
            body.sleepThreshold = 0f;

            // A real rope loses very little per swing; this is the
            // rusted-pin share that eventually brings it to rest.
            body.angularDamping = 0.16f;
            body.maxAngularVelocity = 12f;
            body.solverIterations = 12;
            body.solverVelocityIterations = 4;

            // The ropes weigh nothing next to the plank, so the bob is
            // the plank, and the arm is the rope length.
            body.centerOfMass = seatCenter;

            // A sleeping body reports no trigger stay, and a swing at
            // rest is exactly the one the hero walks up to push.
            body.sleepThreshold = 0f;

            HingeJoint joint = swing.AddComponent<HingeJoint>();
            joint.anchor = Vector3.zero;
            joint.axis = Vector3.right;
            joint.autoConfigureConnectedAnchor = true;
            joint.useSpring = false;
            joint.useMotor = false;
            joint.useLimits = true;
            joint.limits = new JointLimits
            {
                min = -CityPlaygroundGeometry.SwingLimitDegrees,
                max = CityPlaygroundGeometry.SwingLimitDegrees,
                bounciness = 0f,
                contactDistance = 2f
            };
            joint.enableCollision = false;

            swing.AddComponent<CityPlaygroundSwing>()
                .Initialize(body, seatCenter);
        }

        /// <summary>The plank's centre in the pivot's own space: straight
        /// down the rope from the beam.</summary>
        public static Vector3 SeatLocalCenter =>
            new Vector3(0f, -CityPlaygroundGeometry.RopeLength, 0f);

        private static Bounds CreateRope(int side)
        {
            return new Bounds(
                new Vector3(
                    side * CityPlaygroundGeometry.RopeOffsetX,
                    CityPlaygroundGeometry.RopeLength * -0.5f,
                    0f),
                new Vector3(
                    CityPlaygroundGeometry.RopeThickness,
                    CityPlaygroundGeometry.RopeLength,
                    CityPlaygroundGeometry.RopeThickness));
        }

        /// <summary>
        /// One combined park mesh on the swing. The UVs are baked into
        /// the mesh in the pivot's own space, so the timber grain rides
        /// the plank through the arc instead of swimming over it.
        /// </summary>
        private static void AddMesh(
            Transform pivot,
            string name,
            IReadOnlyList<Bounds> boxes,
            Color color)
        {
            const CityParkSurfaceKind surface =
                CityParkSurfaceKind.Timber;
            GameObject mesh =
                RuntimePrimitiveFactory.CreateCombinedBoxes(
                    name,
                    pivot,
                    boxes,
                    color,
                    false,
                    CityParkSurfaceAppearance
                        .GetRecipe(surface)
                        .MetersPerTile,
                    CityParkSurfaceAppearance.GetUvMode(surface));
            Renderer renderer = mesh.GetComponent<Renderer>();
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            CityParkSurfaceAppearance.ApplyCombined(
                renderer,
                surface,
                color);
        }
    }
}

using System;
using System.Collections.Generic;
using UnityEngine;

namespace BarPromenade
{
    /// <summary>Places the park layout using the same frame, seat and bench modules as other sites.</summary>
    internal static class CityPlaygroundSwingBuilder
    {
        public const string RootName = "City Playground";
        public const string SeatRendererName = "SeatTimber";
        public const string RopeRendererName = "Ropes";
        public const float SeatMass = 9f;
        private const string ModelDirectory = "City/Playground/";
        private static readonly Color FramePaint = new Color(.20f, .29f, .30f);

        // Standalone entry for the physics fixture. City dressing calls BuildParkSite directly.
        public static GameObject Build(Transform parent, CityLayout layout, CityDecorationPlan plan,
            Color seatColor, Color ropeColor)
        {
            foreach (CityDecorationDescriptor descriptor in plan.Descriptors)
                if (descriptor.Kind == CityDecorationKind.ParkPlayground)
                    return BuildParkSite(parent, layout, descriptor, seatColor, ropeColor);
            return null;
        }

        internal static GameObject BuildParkSite(Transform parent, CityLayout layout,
            CityDecorationDescriptor descriptor, Color seatColor, Color ropeColor)
        {
            if (!CityDecorationWorldBuilder.TryDescribeRecipeBasis(layout, descriptor, out var origin, out var forward))
                throw new InvalidOperationException("Missing playground placement: " + descriptor.StableId);

            var site = new GameObject(RootName).transform;
            site.SetParent(parent, false);
            site.SetLocalPositionAndRotation(origin, Quaternion.LookRotation(forward));
            Transform frame = BuildFrame(site, Vector3.zero, Quaternion.identity);
            BuildSwing(frame, FindAnchor(frame, "PivotLeft"), seatColor, ropeColor);
            BuildSwing(frame, FindAnchor(frame, "PivotRight"), seatColor, ropeColor);
            BuildBench(site, Vector3.forward * CityPlaygroundGeometry.BenchZ, Quaternion.identity, seatColor, ropeColor);
            return site.gameObject;
        }

        internal static Transform BuildFrame(Transform site, Vector3 position, Quaternion rotation)
        {
            Transform frame = CreateModule(site, "SwingFrame", position, rotation, FramePaint, FramePaint);
            for (int side = -1; side <= 1; side += 2)
            {
                AddCollision(frame, new Vector3(side * CityPlaygroundGeometry.PostOffsetX,
                    CityPlaygroundGeometry.PostHeight * .5f, 0f),
                    new Vector3(CityPlaygroundGeometry.PostThickness + .34f, CityPlaygroundGeometry.PostHeight,
                        CityPlaygroundGeometry.PostOffsetZ * 2f + CityPlaygroundGeometry.PostThickness));
            }
            return frame;
        }

        internal static void BuildBench(Transform site, Vector3 position, Quaternion rotation, Color timber, Color metal)
        {
            Transform bench = CreateModule(site, "Bench", position, rotation, timber, metal);
            AddCollision(bench, Vector3.up * .52f, new Vector3(3.80f, 1.04f, .52f));
        }

        internal static CityPlaygroundSwing BuildSwing(Transform frame, Transform hanger, Color seatColor, Color ropeColor)
        {
            Transform pivot = CreateModule(frame, "SwingSeat", frame.InverseTransformPoint(hanger.position),
                Quaternion.identity, seatColor, ropeColor);
            GameObject swing = pivot.gameObject;
            Vector3 seatCenter = pivot.InverseTransformPoint(FindAnchor(pivot, "SeatCenter").position);

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

            CityPlaygroundSwing result = swing.AddComponent<CityPlaygroundSwing>();
            result.Initialize(body, seatCenter, FindAnchor(pivot, "Pelvis"),
                FindAnchor(pivot, "GripLeft"), FindAnchor(pivot, "GripRight"));
            return result;
        }

        public static Vector3 SeatLocalCenter => new Vector3(0f, -CityPlaygroundGeometry.RopeLength, 0f);

        internal static void BuildCourtyard(Transform parent, CityCourtyardPlaygroundSite site,
            HomeExteriorContextPlan home = null)
        {
            if (site == null) return;
            Vector3 position = site.Position;
            Quaternion rotation = site.Rotation;
            if (home != null)
            {
                position = PlayerHomeBalconyGeometry.ToHomeLocal(home.PlayerHome, position);
                float radius = site.HasBridge ? 4.11f : 2.3f;
                if (position.x - radius < HomeExteriorViewBuilder.ContextMinimumX ||
                    new Vector2(position.x - PlayerHomeBalconyGeometry.HomeFacadeX, position.z).magnitude - radius >
                        HomeExteriorContextPlanner.ViewRadius) return;
                rotation = Quaternion.LookRotation(PlayerHomeBalconyGeometry.ToHomeLocalDirection(
                    home.PlayerHome, rotation * Vector3.forward));
            }

            var root = new GameObject("Courtyard Playground").transform;
            root.SetParent(parent, false);
            root.SetLocalPositionAndRotation(position, rotation);
            Color timber = CityDecorationWorldBuilder.MasonryBatchColor;
            if (site.HasSeesaw)
            {
                BuildSeesaw(root, site, timber, home == null);
                return;
            }
            if (site.HasSlide)
            {
                BuildSlide(root, site, timber, home == null);
                return;
            }
            Transform bars = CreateModule(root, "MonkeyBars", CityCourtyardPlaygroundSite.BarsOrigin,
                Quaternion.identity, timber, FramePaint);
            Transform beam = CreateModule(root, site.SteppingTrail ? "SteppingTrail" : "BalanceBeam",
                CityCourtyardPlaygroundSite.BeamOrigin,
                Quaternion.Euler(0f, 180f, 0f), timber, FramePaint);
            if (home != null) return;

            Transform stepIn = FindAnchor(bars, "StepIn"), stepOut = FindAnchor(bars, "StepOut");
            foreach (Transform step in new[] { stepIn, stepOut })
                AddCollision(bars, bars.InverseTransformPoint(step.position) - Vector3.up * .105f,
                    new Vector3(.53f, .21f, .28f));
            Transform[] rungs = new Transform[7];
            for (int index = 0; index < rungs.Length; index++)
                rungs[index] = FindAnchor(bars, "Rung" + index);
            for (int side = -1; side <= 1; side += 2)
            {
                Vector3 front = bars.InverseTransformPoint(rungs[0].position);
                Vector3 back = bars.InverseTransformPoint(rungs[rungs.Length - 1].position);
                float high = front.y + .03f;
                Vector3 postSize = new Vector3(.094f, high + .08f, .094f);
                AddCollision(bars, new Vector3(side * .37f, (high - .08f) * .5f, front.z), postSize);
                AddCollision(bars, new Vector3(side * .37f, (high - .08f) * .5f, back.z), postSize);
                AddCollision(bars, new Vector3(side * .37f, high, (front.z + back.z) * .5f),
                    new Vector3(.084f, .084f, back.z - front.z + .14f));
            }
            foreach (Transform rung in rungs)
                AddCollision(bars, bars.InverseTransformPoint(rung.position), new Vector3(.74f, .042f, .042f));
            Transform top = FindAnchor(beam, site.SteppingTrail ? "TrailTop4" : "Top");
            if (site.SteppingTrail)
                for (int step = 1; step <= 7; step++)
                    AddCollision(beam, beam.InverseTransformPoint(FindAnchor(beam, "TrailTop" + step).position) -
                        Vector3.up * .11f, new Vector3(.30f, .22f, .30f));
            else
                AddCollision(beam, beam.InverseTransformPoint(top.position) - Vector3.up * .11f,
                    new Vector3(.23f, .22f, 2.23f));
            CityCourtyardPlayground.Crossing barsCrossing = new CityCourtyardPlayground.Crossing(
                FindAnchor(bars, "BarsEntry"), FindAnchor(bars, "BarsExit"), CityFairChildAction.BarsCross, rungs);
            CityCourtyardPlayground.Crossing beamCrossing = new CityCourtyardPlayground.Crossing(
                FindAnchor(beam, site.SteppingTrail ? "TrailEntry" : "BeamEntry"),
                FindAnchor(beam, site.SteppingTrail ? "TrailExit" : "BeamExit"), CityFairChildAction.BeamCross, new[] { top });
            root.gameObject.AddComponent<CityCourtyardPlayground>().Initialize(site, barsCrossing, beamCrossing);
        }

        internal static CityPlaygroundBalanceStation BuildBalanceStation(Transform parent, CityPlaygroundBalancePlan plan,
            HomeExteriorContextPlan home = null)
        {
            if (plan == null) return null;
            Vector3 position = plan.Position;
            Quaternion rotation = plan.Rotation;
            if (home != null)
            {
                position = PlayerHomeBalconyGeometry.ToHomeLocal(home.PlayerHome, position);
                if (position.x - .9f < HomeExteriorViewBuilder.ContextMinimumX ||
                    new Vector2(position.x - PlayerHomeBalconyGeometry.HomeFacadeX, position.z).magnitude - .9f >
                    HomeExteriorContextPlanner.ViewRadius) return null;
                rotation = Quaternion.LookRotation(PlayerHomeBalconyGeometry.ToHomeLocalDirection(
                    home.PlayerHome, rotation * Vector3.forward));
            }
            string kind = plan.Block ? "Block" : "Plank";
            Transform model = CreateModule(parent, "BalanceStep" + kind, position, rotation,
                CityDecorationWorldBuilder.MasonryBatchColor, FramePaint);
            if (home != null) return null;
            Transform top = FindAnchor(model, kind + "Top");
            AddCollision(model, model.InverseTransformPoint(top.position) - Vector3.up * .09f,
                new Vector3(plan.Block ? .49f : .40f, .18f, .50f));
            CityPlaygroundBalanceStation station = model.gameObject.AddComponent<CityPlaygroundBalanceStation>();
            station.Initialize(plan, FindAnchor(model, kind + "Entry"), top);
            return station;
        }

        private static void BuildSeesaw(Transform root, CityCourtyardPlaygroundSite site, Color timber, bool interactive)
        {
            Transform stand = CreateModule(root, "SeesawStand", Vector3.zero, Quaternion.identity, timber, FramePaint);
            Transform board = CreateModule(root, "SeesawBoard",
                root.InverseTransformPoint(FindAnchor(stand, "SeesawPivot").position),
                Quaternion.identity, timber, FramePaint);
            if (!interactive) return;
            AddCollision(stand, Vector3.up * .2f, new Vector3(.54f, .4f, .46f));
            AddCollision(board, Vector3.zero, new Vector3(.22f, .072f, 2.56f));
            foreach (string end in new[] { "A", "B" })
            {
                Vector3 seat = board.InverseTransformPoint(FindAnchor(board, "SeesawSeat" + end).position);
                AddCollision(board, seat - Vector3.up * .0275f, new Vector3(.31f, .055f, .34f));
                Vector3 left = board.InverseTransformPoint(FindAnchor(board, "SeesawGripL" + end).position);
                Vector3 right = board.InverseTransformPoint(FindAnchor(board, "SeesawGripR" + end).position);
                AddSegment(board, left, right, .045f, .045f);
            }
            root.gameObject.AddComponent<CityPlaygroundSeesaw>().Initialize(site, board,
                FindAnchor(board, "SeesawDockA"), FindAnchor(board, "SeesawDockB"));
        }

        private static void BuildSlide(Transform root, CityCourtyardPlaygroundSite site, Color timber, bool interactive)
        {
            const float descentTime = 4.58f;
            const float bridgeCrossTime = 4.5f;
            Vector3 origin = CityCourtyardPlaygroundSite.SlideOrigin;
            var firstLane = BuildSlideLane(root, origin, timber, interactive, false, site.HasBridge ? "Left" : "");
            var slideLane = site.HasBridge
                ? BuildSlideLane(root, origin + CityCourtyardPlaygroundSite.BridgeLane, timber, interactive, true, "Right")
                : firstLane;
            if (site.HasBridge) BuildBridge(root, firstLane.deck, slideLane.deck, timber, interactive);
            Transform chute = CreateModule(root, "SlideChute",
                root.InverseTransformPoint(FindAnchor(slideLane.deck, "SlideChuteSocket").position),
                Quaternion.identity, timber, FramePaint);
            if (!interactive) return;

            Transform exit = FindAnchor(chute, "SlideExit");
            Transform[] chuteContacts = AddChuteContacts(chute);
            slideLane.contacts.AddRange(chuteContacts);
            var slide = new CityCourtyardPlayground.Crossing(slideLane.entry, exit,
                CityFairChildAction.SlideCross, slideLane.contacts.ToArray(), descentTime, root.forward);
            var playground = root.gameObject.AddComponent<CityCourtyardPlayground>();
            if (!site.HasBridge)
            {
                playground.Initialize(site, slide);
                return;
            }

            firstLane.contacts.AddRange(chuteContacts);
            var viaBridge = new CityCourtyardPlayground.Crossing(firstLane.entry, exit,
                CityFairChildAction.SlideViaBridge, firstLane.contacts.ToArray(), descentTime + bridgeCrossTime, root.forward);
            playground.Initialize(site, viaBridge, alternate: slide);
        }

        private static (Transform entry, Transform deck, List<Transform> contacts) BuildSlideLane(Transform root,
            Vector3 origin, Color timber, bool interactive, bool frame, string guardSide)
        {
            string prefix = frame ? "Climb" : "Slide";
            Transform ascent = CreateModule(root, frame ? "ClimbFrame" : "SlideStair", origin,
                Quaternion.identity, timber, FramePaint);
            Transform deck = CreateModule(root, "SlideDeck",
                root.InverseTransformPoint(FindAnchor(ascent, prefix + "DeckSocket").position),
                Quaternion.identity, timber, FramePaint);
            Transform guards = CreateModule(root, "SlideGuards" + guardSide, origin,
                Quaternion.identity, timber, FramePaint);
            Transform entry = FindAnchor(ascent, prefix + "Entry");
            if (!interactive) return (entry, deck, null);
            var contacts = new List<Transform>(14);
            for (int i = 0; i < 4; i++)
            {
                Transform step = FindAnchor(ascent, prefix + "Step" + i);
                contacts.Add(step);
                AddCollision(ascent, ascent.InverseTransformPoint(step.position) - Vector3.up * (frame ? .036f : .032f),
                    new Vector3(.66f, frame ? .072f : .064f, frame ? .072f : .27f));
            }
            Vector3 deckTop = deck.InverseTransformPoint(FindAnchor(deck, "SlideDeckTop").position);
            AddCollision(deck, deckTop - Vector3.up * .045f, new Vector3(.80f, .09f, .63f));
            Transform railIn = FindAnchor(guards, "SlideRailIn" + guardSide);
            Transform railOut = FindAnchor(guards, "SlideRailOut" + guardSide);
            contacts.Add(railIn);
            contacts.Add(railOut);
            Vector3 railStart = guards.InverseTransformPoint(railIn.position);
            Vector3 railEnd = guards.InverseTransformPoint(railOut.position);
            for (int side = -1; side <= 1; side += 2)
            {
                Vector3 stairSide = Vector3.right * (side * .29f);
                AddSegment(guards, railStart + stairSide, railEnd + stairSide, .045f, .045f);
                AddSegment(guards, new Vector3(stairSide.x, -.09f, railStart.z),
                    railStart + stairSide, .058f, .058f);
                if (guardSide.Length == 0 || side == (guardSide == "Left" ? -1 : 1))
                {
                    Vector3 deckRail = new Vector3(side * .34f, railEnd.y, 1.30f);
                    AddSegment(guards, deckRail, deckRail + Vector3.forward * .69f, .05f, .05f);
                    AddSegment(guards, deckRail - Vector3.up * .29f,
                        deckRail - Vector3.up * .29f + Vector3.forward * .69f, .05f, .05f);
                }
                for (int end = 0; end < 2; end++)
                {
                    float z = end == 0 ? 0f : .65f;
                    AddCollision(deck, new Vector3(side * .36f, .32f, z), new Vector3(.085f, .88f, .085f));
                }
            }
            return (entry, deck, contacts);
        }

        private static void BuildBridge(Transform root, Transform firstDeck, Transform secondDeck, Color timber,
            bool interactive)
        {
            Vector3 first = root.InverseTransformPoint(FindAnchor(firstDeck, "SlideDeckTop").position);
            Vector3 second = root.InverseTransformPoint(FindAnchor(secondDeck, "SlideDeckTop").position);
            float width = second.x - first.x - .80f;
            Transform bridge = CreateModule(root, "DeckBridge", (first + second) * .5f, Quaternion.identity,
                timber, FramePaint);
            Transform backstop = CreateModule(root, "DeckBridgeBackstop", first + Vector3.forward * .32f,
                Quaternion.identity, timber, FramePaint);
            if (!interactive) return;
            AddCollision(bridge, Vector3.down * .045f, new Vector3(width + .012f, .09f, .56f));
            for (int side = -1; side <= 1; side += 2)
                AddCollision(bridge, new Vector3(0f, .57f, side * .292f), new Vector3(width + .12f, .34f, .05f));
            AddCollision(backstop, Vector3.up * .57f, new Vector3(.72f, .34f, .05f));
        }

        private static Transform[] AddChuteContacts(Transform chute)
        {
            var contacts = new Transform[8];
            contacts[0] = FindAnchor(chute, "SlideGuide0");
            Vector3 previous = chute.InverseTransformPoint(contacts[0].position);
            for (int i = 1; i < contacts.Length; i++)
            {
                contacts[i] = FindAnchor(chute, "SlideGuide" + i);
                Vector3 next = chute.InverseTransformPoint(contacts[i].position);
                AddSegment(chute, previous, next, .55f, .043f, -.0215f);
                foreach (int side in new[] { -1, 1 })
                {
                    Vector3 edge = new Vector3(side * .305f, .031f, 0f);
                    AddSegment(chute, previous + edge, next + edge, .055f, .08f);
                }
                previous = next;
            }
            return contacts;
        }

        private static void AddSegment(Transform parent, Vector3 first, Vector3 second,
            float width, float depth, float normalOffset = 0f)
        {
            var segment = new GameObject("Equipment contact").transform;
            segment.SetParent(parent, false);
            Vector3 difference = second - first;
            Vector3 axis = difference.normalized;
            Quaternion rotation = Quaternion.LookRotation(axis,
                Mathf.Abs(axis.y) > .98f ? Vector3.forward : Vector3.up);
            segment.SetLocalPositionAndRotation((first + second) * .5f, rotation);
            AddCollision(segment, Vector3.up * normalOffset, new Vector3(width, depth, difference.magnitude + .015f));
        }

        private static Transform CreateModule(Transform parent, string module, Vector3 position,
            Quaternion rotation, Color timber, Color metal)
        {
            GameObject asset = Resources.Load<GameObject>(ModelDirectory + module);
            if (asset == null) throw new InvalidOperationException("Missing playground model: " + module);
            // Keep the imported hierarchy and unit scale below an ordinary metric runtime root.
            var root = new GameObject(module).transform;
            root.SetParent(parent, false);
            root.SetLocalPositionAndRotation(position, rotation);
            GameObject visual = UnityEngine.Object.Instantiate(asset, root, false);
            foreach (Renderer renderer in visual.GetComponentsInChildren<Renderer>())
            {
                bool wood = renderer.name.EndsWith("Timber", StringComparison.Ordinal);
                Color tint = renderer.name == "ChuteMetal" ? new Color(.34f, .36f, .35f) : wood ? timber : metal;
                CityParkSurfaceAppearance.ApplyCombined(renderer,
                    wood ? CityParkSurfaceKind.Timber : CityParkSurfaceKind.PaintedMetal, tint);
            }
            return root;
        }

        private static Transform FindAnchor(Transform module, string name)
        {
            foreach (Transform part in module.GetChild(0).GetComponentsInChildren<Transform>())
                if (part.name == name) return part;
            throw new InvalidOperationException($"Playground model {module.name} is missing {name}.");
        }

        private static void AddCollision(Transform transform, Vector3 center, Vector3 size)
        {
            var collider = transform.gameObject.AddComponent<BoxCollider>();
            collider.center = center;
            collider.size = size;
        }
    }
}

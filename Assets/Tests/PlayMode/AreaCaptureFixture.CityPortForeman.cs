using System;
using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

namespace BarPromenade.Tests.PlayMode
{
    public sealed class PortForemanAssetsSetup : IPrebuildSetup
    {
        public void Setup()
        {
#if UNITY_EDITOR
            Type.GetType("BarPromenade.Editor.CityPortForemanAssetSetup, BarPromenade.Editor", true)
                .GetMethod("BuildOrThrow", Type.EmptyTypes).Invoke(null, null);
#endif
        }
    }

    public sealed partial class AreaCaptureFixture
    {
        [UnityTest]
        [Explicit("The seated foreman, raised-hand speech, clear working paths and one shared conversation channel.")]
        [PrebuildSetup(typeof(PortForemanAssetsSetup))]
        public IEnumerator CityPortForeman() => CaptureFocusedPort(CapturePortForeman);

        private static IEnumerator CapturePortForeman(Camera camera, CityGameRoot city,
            CityPortController port, CityPortCrew crew)
        {
            var foreman = crew.Foreman;
            Assert.That(foreman, Is.Not.Null);
            Assert.That(foreman.Interaction, Is.Not.Null);
            Assert.That(Vector3.Distance(foreman.transform.position, port.Plan.ForemanSeatWorld), Is.LessThan(.001f));
            var body = foreman.GetComponent<BoxCollider>();
            var solids = foreman.GetComponents<Collider>();
            Assert.That(body, Is.Not.Null);
            Assert.That(body.isTrigger, Is.False);
            double life = crew.LifeElapsedSeconds + 1000d;
            double held = CityPortCycle.CycleDurationSeconds - .001d;
            port.ApplyAt(held, (float)held); crew.ApplyAt(held, life); foreman.ApplyAt(life);
            Vector3 parkingDirection = Vector3.ProjectOnPlane(port.Trolley.position - foreman.transform.position, Vector3.up);
            float wallDistance = foreman.transform.position.z - port.Plan.WarehouseBounds.yMax;
            Assert.That(wallDistance, Is.InRange(.5f, .65f), "The stool stays against the warehouse front wall.");
            Assert.That(Vector3.Angle(foreman.transform.forward, parkingDirection), Is.LessThan(.1f));
            Vector3 leftFoot = foreman.LeftFoot.position, rightFoot = foreman.RightFoot.position;
            Assert.That(Vector3.Distance(leftFoot, rightFoot), Is.GreaterThan(.6f));
            Assert.That(foreman.Seat.position.y - port.Plan.QuayTopY, Is.EqualTo(.5f).Within(.012f));
            Assert.That(foreman.Head.position.y - port.Plan.QuayTopY, Is.InRange(1.1f, 1.55f));
            foreach (string side in new[] { "L", "R" })
            {
                Bounds sole = MeasureForemanPart(foreman, "GEO_ShoeSole." + side);
                Assert.That(sole.min.y, Is.EqualTo(port.Plan.QuayTopY).Within(.018f), "The rendered sole is planted.");
            }
            Bounds coat = MeasureForemanPart(foreman, "CLO_ForemanBarrelCoat");
            Assert.That(coat.size.x, Is.GreaterThan(.45f));
            Assert.That(coat.size.z, Is.GreaterThan(.45f), "A deep belly, not a slim worker uniformly enlarged.");
            AssertPortBodyBlocksHero(city.Player.GameObject.GetComponent<CharacterController>(), body);
            Physics.SyncTransforms();
            foreach (Collider obstacle in port.Dock.GetComponentsInChildren<Collider>())
                if (obstacle.name.StartsWith("COL_Warehouse", StringComparison.Ordinal))
                    foreach (Collider solid in solids) AssertForemanClear(solid, obstacle, "warehouse wall and furniture");

            // Follow both directions of the real break paths, including the
            // crane operators' later return deadline and the docker's detour.
            // Reconstruct in Work, then observe the real decision to leave;
            // seeking straight to Depart would reconstruct an already resting crew.
            double departure = CityPortCycle.UnloadStartSeconds + CityPortCycle.UnloadDurationSeconds - .25d;
            foreach (double start in new[] { departure, CityPortCycle.CycleDurationSeconds })
            {
                double length = start == departure ? CityPortCycle.CycleDurationSeconds - start : CityPortCycle.UnloadStartSeconds;
                bool outwardWalkObserved = false;
                for (int sample = 0; sample <= Mathf.CeilToInt((float)length * 4f); sample++)
                {
                    double t = sample * .25d;
                    port.ApplyAt(start + t, (float)t);
                    crew.ApplyAt(port.ElapsedSeconds, life + .25d + t);
                    foreman.ApplyAt(crew.LifeElapsedSeconds);
                    outwardWalkObserved |= port.Snapshot.Stage == CityPortCycleStage.Depart &&
                        crew.ShoreWorker.CurrentAction == VillageResidentAction.Walk;
                    for (int role = 2; role < crew.WorkerCount; role++)
                        foreach (Collider solid in solids)
                            AssertForemanClear(solid, crew.GetWorker(role).GetComponent<CapsuleCollider>(), "returning worker " + role);
                    for (int role = 2; role < 4; role++)
                        AssertForemanClear(crew.ShoreWorker.GetComponent<CapsuleCollider>(),
                            crew.GetWorker(role).GetComponent<CapsuleCollider>(), "docker detour and returning operator " + role);
                    foreach (Collider trolley in port.Trolley.GetComponentsInChildren<Collider>())
                        foreach (Collider solid in solids) AssertForemanClear(solid, trolley, "working trolley");
                }
                if (start == departure) Assert.That(outwardWalkObserved, Is.True, "Observe the actual walk to the canopy, not a reconstructed resting pose.");
            }
            Assert.That(Vector3.Distance(leftFoot, foreman.LeftFoot.position), Is.LessThan(.015f));
            Assert.That(Vector3.Distance(rightFoot, foreman.RightFoot.position), Is.LessThan(.015f));
            yield return ValidatePortForemanCarrot(camera, city, port, crew);
            foreach (int hour in new[] { 12, 21 })
            {
                SetPortForemanHour(hour);
                city.DayNight.ApplyCurrentTime(true);
                Vector3 target = foreman.transform.position + Vector3.up * .83f;
                yield return CapturePortSocialPose(camera, "port-foreman-" + (hour == 12 ? "00-day" : "01-night"),
                    target + foreman.transform.forward * 3.2f + foreman.transform.right * .7f + Vector3.up * .48f,
                    target, 49f);
            }
            SetPortForemanHour(12);
            city.DayNight.ApplyCurrentTime(true);
            yield return ValidatePortForemanSpeech(camera, city, port, crew);
            Vector3 overview = (foreman.transform.position + port.Plan.World(port.Plan.LandingLocal(0))) * .5f + Vector3.up * .7f;
            yield return CapturePortSocialPose(camera, "port-foreman-06-trolley-place",
                overview + new Vector3(6f, 3.5f, 2f), overview, 54f);
        }

        private static IEnumerator ValidatePortForemanCarrot(Camera camera, CityGameRoot city,
            CityPortController port, CityPortCrew crew)
        {
            var foreman = crew.Foreman;
            double life = crew.LifeElapsedSeconds;
            double held = CityPortCycle.CycleDurationSeconds - .001d;
            double deadline = life + 60d;
            long oldCycle = foreman.Snack.CycleCount;
            while (foreman.Snack.CycleCount == oldCycle || foreman.Snack.Phase != CityPortForemanSnackPhase.Idle)
            { Step(); Assert.That(life, Is.LessThan(deadline)); }
            long cycle = foreman.Snack.CycleCount;
            Assert.That(foreman.Snack.BitesTaken, Is.Zero);
            Assert.That(foreman.VisibleCarrotBites, Is.EqualTo(3));
            ValidateForemanPalmGrip(foreman, out _);
            int commits = 0, previousBites = 0;
            var capturedBites = new bool[3];
            bool discarded = false, capturedThrow = false, capturedPocket = false;
            Vector3 leftFoot = foreman.LeftFoot.position, rightFoot = foreman.RightFoot.position;
            Transform grip = CityPedestrianHandProps.FindSocket(foreman.ModelRoot, "ANCHOR_ForemanStemGrip");
            Transform release = CityPedestrianHandProps.FindSocket(foreman.ModelRoot, "ANCHOR_ForemanThrowRelease");
            Transform pocket = CityPedestrianHandProps.FindSocket(foreman.ModelRoot, "ANCHOR_ForemanPocket");
            SetPortForemanHour(12);
            city.DayNight.ApplyCurrentTime(true);
            deadline = life + 45d;
            while (foreman.Snack.CycleCount == cycle || foreman.Snack.Phase != CityPortForemanSnackPhase.Idle)
            {
                Step();
                Assert.That(life, Is.LessThan(deadline), "One carrot has a finite three-bite cycle.");
                var snack = foreman.Snack;
                if (snack.BitesTaken > previousBites)
                {
                    Assert.That(snack.BitesTaken, Is.EqualTo(previousBites + 1));
                    commits++;
                }
                previousBites = snack.BitesTaken;
                Assert.That(foreman.VisibleCarrotBites, Is.EqualTo(snack.HoldingCarrot ? 3 - snack.BitesTaken : 0));
                Assert.That(Vector3.Distance(leftFoot, foreman.LeftFoot.position), Is.LessThan(.015f));
                Assert.That(Vector3.Distance(rightFoot, foreman.RightFoot.position), Is.LessThan(.015f));
                if (snack.Phase >= CityPortForemanSnackPhase.Bite1 && snack.Phase <= CityPortForemanSnackPhase.Bite3 &&
                    snack.ActionSeconds >= 1.35d && snack.ActionSeconds < 1.55d)
                {
                    int bite = (int)snack.Phase - (int)CityPortForemanSnackPhase.Bite1;
                    Assert.That(Vector3.Distance(foreman.BiteTip.position, foreman.Mouth.position), Is.LessThan(.025f),
                        "The remaining carrot tip meets the mouth on bite " + (bite + 1));
                    if (!capturedBites[bite])
                    {
                        capturedBites[bite] = true;
                        yield return Frame("port-foreman-03-bite-" + (bite + 1));
                        if (bite == 0)
                        {
                            Vector3 gripCenter = ValidateForemanPalmGrip(foreman, out Vector3 viewOffset);
                            yield return CapturePortSocialPose(camera, "port-foreman-07-palm-grip",
                                gripCenter + viewOffset, gripCenter, 35f);
                        }
                    }
                }
                if (snack.Phase == CityPortForemanSnackPhase.Discard && snack.StemInFlight)
                {
                    discarded = true;
                    Assert.That(snack.BitesTaken, Is.EqualTo(3));
                    Assert.That(snack.HoldingCarrot, Is.False);
                    Assert.That(foreman.ThrownStem.gameObject.activeInHierarchy, Is.True);
                    Bounds thrown = MeasureForemanPart(foreman, "FOOD_ForemanThrownStem");
                    Assert.That(thrown.size.magnitude, Is.InRange(.015f, .18f), "The detached stem retains real world scale.");
                    Assert.That(Vector3.Distance(thrown.center, foreman.ThrownStem.position), Is.LessThan(.10f));
                    if (snack.ThrowProgress < .08f)
                        Assert.That(Vector3.Distance(grip.position, release.position), Is.LessThan(.08f), "The discarded stem starts at the releasing hand.");
                    if (!capturedThrow && snack.ThrowProgress >= .45f)
                    { capturedThrow = true; yield return Frame("port-foreman-04-discard"); }
                }
                if (snack.Phase == CityPortForemanSnackPhase.Take && !snack.HoldingCarrot)
                    Assert.That(foreman.VisibleCarrotBites, Is.Zero, "The next carrot cannot appear before the pocket contact.");
                if (snack.Phase == CityPortForemanSnackPhase.Take && snack.HoldingCarrot && !capturedPocket)
                {
                    Assert.That(snack.ActionSeconds, Is.GreaterThanOrEqualTo(CityPortForemanSnackTimeline.TakePickupSeconds));
                    Assert.That(Vector3.Distance(grip.position, pocket.position), Is.LessThan(.025f),
                        "A fresh carrot appears only while his grip is inside the real pocket.");
                    capturedPocket = true;
                    yield return Frame("port-foreman-05-pocket");
                }
            }
            Assert.That(commits, Is.EqualTo(3));
            Assert.That(capturedBites, Is.All.True);
            Assert.That(discarded && capturedThrow && capturedPocket, Is.True);
            Assert.That(foreman.Snack.CycleCount, Is.EqualTo(cycle + 1));
            Assert.That(foreman.VisibleCarrotBites, Is.EqualTo(3));
            Assert.That(foreman.ThrownStem.gameObject.activeSelf, Is.False);

            void Step()
            {
                life += .05d;
                port.ApplyAt(held, 15f); crew.ApplyAt(held, life); foreman.ApplyAt(life);
            }
            IEnumerator Frame(string name)
            {
                Vector3 target = foreman.transform.position + Vector3.up * .83f;
                return CapturePortSocialPose(camera, name,
                    target + foreman.transform.forward * 3.2f + foreman.transform.right * .7f + Vector3.up * .48f, target, 49f);
            }
        }

        private static void SetPortForemanHour(int hour)
        {
            double remaining = (hour * 60d - GameSessionState.GameTimeOfDayMinutes + 1440d) % 1440d;
            GameSessionState.AdvanceGameTime((float)(remaining / GameTimeState.GameMinutesPerRealSecond));
            Assert.That(GameSessionState.GameTimeOfDayMinutes, Is.EqualTo(hour * 60d).Within(.02d));
        }

        private static void AssertForemanClear(Collider foreman, Collider other, string reason)
        {
            if (other == null || !other.enabled || !other.gameObject.activeInHierarchy || other.isTrigger) return;
            bool intersects = Physics.ComputePenetration(foreman, foreman.transform.position, foreman.transform.rotation,
                other, other.transform.position, other.transform.rotation, out _, out float depth);
            Assert.That(!intersects || depth < .012f, Is.True, "Foreman layout blocks " + reason + " (" + other.name + "): " + depth);
        }

        private static Bounds MeasureForemanPart(BarPromenade.CityPortForeman foreman, string name)
        {
            Vector3[] points = ForemanPartVertices(foreman, name);
            var bounds = new Bounds(points[0], Vector3.zero);
            foreach (Vector3 point in points) bounds.Encapsulate(point);
            return bounds;
        }

        private static Vector3 ValidateForemanPalmGrip(BarPromenade.CityPortForeman foreman, out Vector3 viewOffset)
        {
            Vector3[] palm = ForemanPartVertices(foreman, "GEO_Hand.L");
            Vector3[] thumb = ForemanPartVertices(foreman, "GEO_Thumb.L");
            Vector3[] stem = ForemanPartVertices(foreman, "FOOD_ForemanCarrotStem");
            Vector3 handCenter = Center(palm), thumbCenter = Center(thumb), foodCenter = Center(stem);
            Transform hand = CityPedestrianHandProps.FindSocket(foreman.ModelRoot, "hand.L");
            Transform socket = CityPedestrianHandProps.FindSocket(foreman.ModelRoot, "SOCKET_Grip.L");
            Vector3 alongHand = (socket.position - hand.position).normalized;
            // The shared rig's grip.up defines the palmar side independently
            // of the food anchors. Measure the imported skin, not only a socket.
            Vector3 palmNormal = Vector3.ProjectOnPlane(socket.up, alongHand).normalized;
            Assert.That(Vector3.Dot(foodCenter - handCenter, palmNormal), Is.GreaterThan(.02f),
                "The carrot is held on the palm side, not through the back of the hand.");
            float closest = float.PositiveInfinity;
            foreach (Vector3 thumbPoint in thumb)
                foreach (Vector3 stemPoint in stem) closest = Mathf.Min(closest, (thumbPoint - stemPoint).sqrMagnitude);
            Assert.That(Mathf.Sqrt(closest), Is.LessThan(.025f), "The actual thumb opposes the carrot grip.");
            Vector3 thumbSide = Vector3.Cross(alongHand, palmNormal).normalized;
            if (Vector3.Dot(thumbSide, thumbCenter - handCenter) < 0f) thumbSide = -thumbSide;
            viewOffset = palmNormal * .45f + thumbSide * .16f + alongHand * .10f;
            return (handCenter + foodCenter) * .5f;

            Vector3 Center(Vector3[] points)
            {
                Vector3 sum = Vector3.zero;
                foreach (Vector3 point in points) sum += point;
                return sum / points.Length;
            }
        }

        private static Vector3[] ForemanPartVertices(BarPromenade.CityPortForeman foreman, string name)
        {
            Renderer renderer = CityPedestrianHandProps.FindSocket(foreman.ModelRoot, name).GetComponent<Renderer>();
            var mesh = new Mesh();
            try
            {
                var skinned = renderer as SkinnedMeshRenderer;
                if (skinned != null) skinned.BakeMesh(mesh, true);
                else Object.DestroyImmediate(mesh);
                Mesh source = skinned != null ? mesh : renderer.GetComponent<MeshFilter>().sharedMesh;
                Vector3[] vertices = source.vertices;
                for (int i = 0; i < vertices.Length; i++)
                    vertices[i] = renderer.localToWorldMatrix.MultiplyPoint3x4(vertices[i]);
                return vertices;
            }
            finally { if (mesh != null) Object.DestroyImmediate(mesh); }
        }
    }
}

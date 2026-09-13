using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using Object = UnityEngine.Object;

namespace BarPromenade.Tests.EditMode
{
    public sealed class NpcFootstepsTests
    {
        private const float FrameSeconds = 1f / 60f;

        private readonly List<GameObject> created = new List<GameObject>();

        [SetUp]
        public void SetUp()
        {
            NpcFootstepSources.ResetForTests();
            FootstepGroundOverlay.ResetForTests();
        }

        [TearDown]
        public void TearDown()
        {
            for (int index = created.Count - 1; index >= 0; index--)
            {
                if (created[index] != null)
                {
                    Object.DestroyImmediate(created[index]);
                }
            }

            created.Clear();
            NpcFootstepSources.ResetForTests();
            FootstepGroundOverlay.ResetForTests();
        }

        [Test]
        public void Stride_CountsStepsByTravelAndIgnoresJumpsAndCrawls()
        {
            var tracker = new NpcStrideTracker();
            int steps = Walk(tracker, 1.4f, 10f);
            // Ten metres at the hero's stride, the first frame only priming.
            Assert.That(
                steps,
                Is.EqualTo(Mathf.FloorToInt(10f / PlayerMotor.FootstepStride)));

            // A pool reset across the map is not a step and starts afresh.
            tracker.Reset();
            tracker.Advance(Vector3.zero, FrameSeconds);
            Assert.That(
                tracker.Advance(new Vector3(30f, 0f, 0f), FrameSeconds),
                Is.False);
            Assert.That(tracker.Distance, Is.EqualTo(0f));

            // A crawl under the hero's own threshold never accumulates.
            tracker.Reset();
            Assert.That(Walk(tracker, 0.3f, 3f), Is.EqualTo(0));

            // A run lengthens the stride the way the hero's does.
            tracker.Reset();
            int runSteps = Walk(tracker, 4f, 20f);
            Assert.That(
                runSteps,
                Is.EqualTo(Mathf.FloorToInt(20f / PlayerMotor.RunFootstepStride)));
        }

        [Test]
        public void Director_ResolvesStepsOnlyForKnownGroundWithinEarshot()
        {
            GameObject floor = CreateFloor(new Vector3(0f, -0.05f, 0f), 40f);
            FootstepGround.Stamp(floor, FootstepGroundKind.Wood);
            GameObject hero = Track(new GameObject("Player"));
            NpcFootstepDirector director =
                hero.AddComponent<NpcFootstepDirector>();
            director.Initialize(hero.transform);

            GameObject walker = Track(new GameObject("Walker"));
            walker.transform.position = new Vector3(2f, 0f, 0f);
            NpcFootstepSources.Register(walker.transform);

            Drive(director, walker.transform, Vector3.right, 1.4f, 3f);
            Assert.That(
                director.StepsResolved,
                Is.EqualTo(Mathf.FloorToInt(3f / PlayerMotor.FootstepStride)));
            Assert.That(director.LastKind, Is.EqualTo(FootstepGroundKind.Wood));

            // Out of earshot the same walk is not even measured.
            int before = director.StepsResolved;
            walker.transform.position = new Vector3(30f, 0f, 0f);
            Drive(director, walker.transform, Vector3.right, 1.4f, 3f);
            Assert.That(director.StepsResolved, Is.EqualTo(before));

            // Unknown ground - a bus floor, a cabin - stays silent.
            GameObject deck = CreateFloor(new Vector3(0f, 1.95f, 8f), 6f);
            walker.transform.position = new Vector3(0f, 2f, 8f);
            Drive(director, walker.transform, Vector3.right, 1.4f, 3f);
            Assert.That(director.StepsResolved, Is.EqualTo(before));
            Assert.That(deck.GetComponent<FootstepGround>(), Is.Null);
        }

        [Test]
        public void Director_KeepsWheelchairBodiesAndTheHeroOwnRigQuiet()
        {
            GameObject floor = CreateFloor(new Vector3(0f, -0.05f, 0f), 40f);
            FootstepGround.Stamp(floor, FootstepGroundKind.Stone);
            GameObject hero = Track(new GameObject("Player"));
            NpcFootstepDirector director =
                hero.AddComponent<NpcFootstepDirector>();
            director.Initialize(hero.transform);

            GameObject chair = Track(new GameObject("Wheelchair"));
            chair.AddComponent<CityWheelchairNpcAssetRegistry>();
            GameObject rider = Track(new GameObject("Rider"));
            rider.transform.SetParent(chair.transform, false);
            rider.transform.position = new Vector3(2f, 0f, 2f);
            NpcFootstepSources.Register(rider.transform);

            GameObject ownRig = Track(new GameObject("Hero Rig"));
            ownRig.transform.SetParent(hero.transform, false);
            NpcFootstepSources.Register(ownRig.transform);

            Drive(director, rider.transform, Vector3.forward, 1.4f, 4f);
            Drive(director, ownRig.transform, Vector3.forward, 1.4f, 4f);
            Assert.That(director.StepsResolved, Is.EqualTo(0));
        }

        [Test]
        public void Sources_ForgetDestroyedRootsWhenPruned()
        {
            GameObject walker = Track(new GameObject("Walker"));
            NpcFootstepSources.Register(walker.transform);
            NpcFootstepSources.Register(walker.transform);
            Assert.That(NpcFootstepSources.Count, Is.EqualTo(1));

            Object.DestroyImmediate(walker);
            Assert.That(NpcFootstepSources.Prune(), Is.Empty);
        }

        private static int Walk(
            NpcStrideTracker tracker,
            float speed,
            float length)
        {
            int frames = Mathf.CeilToInt(length / (speed * FrameSeconds));
            int steps = 0;
            Vector3 position = Vector3.zero;
            tracker.Advance(position, FrameSeconds);
            for (int frame = 0; frame < frames; frame++)
            {
                position += Vector3.forward * (speed * FrameSeconds);
                if (tracker.Advance(position, FrameSeconds))
                {
                    steps++;
                }
            }

            return steps;
        }

        private static void Drive(
            NpcFootstepDirector director,
            Transform walker,
            Vector3 direction,
            float speed,
            float length)
        {
            int frames = Mathf.CeilToInt(length / (speed * FrameSeconds));
            Physics.SyncTransforms();
            director.Tick(FrameSeconds);
            for (int frame = 0; frame < frames; frame++)
            {
                walker.position += direction * (speed * FrameSeconds);
                Physics.SyncTransforms();
                director.Tick(FrameSeconds);
            }
        }

        private GameObject Track(GameObject gameObject)
        {
            created.Add(gameObject);
            return gameObject;
        }

        private GameObject CreateFloor(Vector3 center, float extent)
        {
            GameObject floor = Track(new GameObject("Floor"));
            floor.transform.position = center;
            floor.AddComponent<BoxCollider>().size =
                new Vector3(extent, 0.1f, extent);
            return floor;
        }
    }
}

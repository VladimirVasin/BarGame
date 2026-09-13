using System;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using Object = UnityEngine.Object;

namespace BarPromenade.Tests.EditMode
{
    public sealed class HeroFootstepGroundTests
    {
        private readonly List<GameObject> created = new List<GameObject>();

        [SetUp]
        public void SetUp()
        {
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
            FootstepGroundOverlay.ResetForTests();
            FootProbeSurface.ResetPolicyForTests();
        }

        [Test]
        public void StampedFloor_ResolvesToItsKind()
        {
            CreateFloor("Boards", FootstepGroundKind.Wood);

            Assert.That(
                Resolve(Vector3.zero, null, out FootstepGroundKind kind,
                    out Vector3 contact),
                Is.True);
            Assert.That(kind, Is.EqualTo(FootstepGroundKind.Wood));
            Assert.That(contact.y, Is.EqualTo(0f).Within(0.01f));
        }

        [Test]
        public void UnstampedFloor_KeepsThePlainStep()
        {
            CreateFloor("Nobody's floor", FootstepGroundKind.None);

            Assert.That(
                Resolve(Vector3.zero, null, out FootstepGroundKind kind,
                    out _),
                Is.False);
            Assert.That(kind, Is.EqualTo(FootstepGroundKind.None));
        }

        [Test]
        public void TreadTrigger_ReadsTheStampedRootAboveTheFloorBeneath()
        {
            // The visible tread carries a trigger on the probe layer; the
            // body walks a lower, unstamped floor. The tread is the
            // topmost support, and the flight's ROOT says what it is.
            CreateFloor("Ramp", FootstepGroundKind.None);
            GameObject flight = Track(new GameObject("Exterior Stair"));
            FootstepGround.Stamp(flight, FootstepGroundKind.Stone);
            GameObject tread = Track(new GameObject("Step 01"));
            tread.transform.SetParent(flight.transform, false);
            tread.transform.position = new Vector3(0f, 0.30f, 0f);
            tread.transform.localScale = new Vector3(1f, 0.15f, 0.3f);
            FootProbeSurface.AddTreadCollider(tread);

            Vector3 feet = new Vector3(0f, 0.375f, 0f);
            Assert.That(
                Resolve(feet, null, out FootstepGroundKind kind,
                    out Vector3 contact),
                Is.True);
            Assert.That(kind, Is.EqualTo(FootstepGroundKind.Stone));
            Assert.That(contact.y, Is.EqualTo(0.375f).Within(0.01f));
        }

        [Test]
        public void HeroOwnColliderAndForeignTriggers_AreIgnored()
        {
            CreateFloor("Boards", FootstepGroundKind.Wood);
            GameObject hero = Track(new GameObject("Player"));
            GameObject capsule = Track(new GameObject("Hips"));
            capsule.transform.SetParent(hero.transform, false);
            capsule.transform.position = new Vector3(0f, 0.2f, 0f);
            capsule.AddComponent<BoxCollider>().size =
                new Vector3(0.5f, 0.4f, 0.5f);
            GameObject volume = Track(new GameObject("Door Volume"));
            volume.transform.position = new Vector3(0f, 0.3f, 0f);
            BoxCollider trigger = volume.AddComponent<BoxCollider>();
            trigger.size = new Vector3(2f, 0.2f, 2f);
            trigger.isTrigger = true;

            Assert.That(
                Resolve(Vector3.zero, hero.transform,
                    out FootstepGroundKind kind, out _),
                Is.True);
            Assert.That(kind, Is.EqualTo(FootstepGroundKind.Wood));
        }

        [Test]
        public void Overlay_WinsOverTheFloorBeneath_OnlyWithinReach()
        {
            CreateFloor("Boards", FootstepGroundKind.Wood, 12f);
            GameObject rug = Track(new GameObject("Rug"));
            rug.AddComponent<FootstepGroundOverlay>().Initialize(
                FootstepGroundKind.Carpet,
                new[]
                {
                    new RuntimeOrientedBox(
                        new Vector3(0f, 0.015f, 0f),
                        Quaternion.identity,
                        new Vector3(1.5f, 0.03f, 1.3f)),
                    // A rug on a landing a flight above these feet.
                    new RuntimeOrientedBox(
                        new Vector3(3f, 0.6f, 3f),
                        Quaternion.identity,
                        new Vector3(1f, 0.03f, 1f))
                });

            Assert.That(
                Resolve(Vector3.zero, null, out FootstepGroundKind onRug,
                    out _),
                Is.True);
            Assert.That(onRug, Is.EqualTo(FootstepGroundKind.Carpet));

            Assert.That(
                Resolve(new Vector3(1f, 0f, 0f), null,
                    out FootstepGroundKind offRug, out _),
                Is.True);
            Assert.That(offRug, Is.EqualTo(FootstepGroundKind.Wood));

            Assert.That(
                Resolve(new Vector3(3f, 0f, 3f), null,
                    out FootstepGroundKind underLanding, out _),
                Is.True);
            Assert.That(underLanding, Is.EqualTo(FootstepGroundKind.Wood));
        }

        [Test]
        public void PrimitiveBoxOverlay_TakesTheBoxTransformAsItsFootprint()
        {
            GameObject tiles = Track(new GameObject("Home Bathroom Tile Floor"));
            tiles.transform.position = new Vector3(0f, 0.015f, -2.65f);
            tiles.transform.localScale = new Vector3(1.55f, 0.03f, 1.35f);
            FootstepGroundOverlay.AddForPrimitiveBox(
                tiles,
                FootstepGroundKind.Tile);

            Assert.That(
                FootstepGroundOverlay.TryFind(
                    new Vector3(0.5f, 0f, -2.2f),
                    out FootstepGroundKind inside),
                Is.True);
            Assert.That(inside, Is.EqualTo(FootstepGroundKind.Tile));
            Assert.That(
                FootstepGroundOverlay.TryFind(
                    new Vector3(1f, 0f, -2.65f),
                    out _),
                Is.False);
        }

        [Test]
        public void DestroyedOverlay_IsForgottenWithoutOnDisable()
        {
            GameObject rug = Track(new GameObject("Rug"));
            rug.transform.localScale = Vector3.one;
            FootstepGroundOverlay.AddForPrimitiveBox(
                rug,
                FootstepGroundKind.Carpet);
            Assert.That(FootstepGroundOverlay.RegisteredCount, Is.EqualTo(1));

            Object.DestroyImmediate(rug);
            Assert.That(
                FootstepGroundOverlay.TryFind(Vector3.zero, out _),
                Is.False);
            Assert.That(FootstepGroundOverlay.RegisteredCount, Is.EqualTo(0));
        }

        [Test]
        public void Puddle_IsWetOnlyWhereTheFilmIs()
        {
            var patch = new RuntimeOrientedBox(
                Vector3.zero,
                Quaternion.identity,
                new Vector3(2f, 0.003f, 1.5f));
            const float drizzle = 0.18f;
            const float bite = CityPuddleWaterResources.EdgeBite;
            Vector3 centre = Vector3.zero;
            Vector3 rim = new Vector3(0.95f, 0f, 0f);

            // The drizzle floor keeps about half the patch: the middle is
            // water, the rim has dried back to road.
            Assert.That(
                PuddleFootstepOverlay.IsWetAt(patch, centre, drizzle, bite),
                Is.True);
            Assert.That(
                PuddleFootstepOverlay.IsWetAt(patch, rim, drizzle, bite),
                Is.False);
            // A downpour fills it to the rim; no wetness dissolves it.
            Assert.That(
                PuddleFootstepOverlay.IsWetAt(patch, rim, 1f, bite),
                Is.True);
            Assert.That(
                PuddleFootstepOverlay.IsWetAt(patch, centre, 0f, bite),
                Is.False);
            // Feet a flight above the film are not in it.
            Assert.That(
                PuddleFootstepOverlay.IsWetAt(
                    patch,
                    centre + Vector3.up * 0.6f,
                    1f,
                    bite),
                Is.False);
        }

        [Test]
        public void BarInterior_StampsTheBoardsAndLaysCarpetOnlyOnTheFields()
        {
            // Builds the real bar model: the carpet rectangles repeated in
            // the builder are checked against the part's bounds on the
            // way, so a model that moved its carpet fails here.
            GameObject host = Track(new GameObject("Bar Footstep Test"));
            BarInteriorLayoutPlan plan = BarInteriorLayoutPlanner.Generate(
                104,
                "bar-footsteps",
                BarActivityKind.TinctureMatch);
            Transform room = BarInteriorWorldBuilder.Build(
                host.transform,
                plan);

            bool fieldsPartFound = false;
            foreach (Renderer renderer in
                     room.GetComponentsInChildren<Renderer>(true))
            {
                fieldsPartFound |= renderer.name == "Pub Carpet Fields";
            }

            Assert.That(
                fieldsPartFound,
                Is.True,
                "The carpet drift guard needs the merged fields part.");

            Transform floor = room.Find("Floor Collision");
            Assert.That(floor, Is.Not.Null);
            Assert.That(
                floor.GetComponent<FootstepGround>()?.Kind,
                Is.EqualTo(FootstepGroundKind.Wood));

            // The middle of the big field, the bay rug, and the plank aisle
            // between the two big fields that a bounds-only overlay would
            // have carpeted.
            Assert.That(
                FootstepGroundOverlay.TryFind(
                    room.TransformPoint(new Vector3(-0.75f, 0f, -0.65f)),
                    out FootstepGroundKind field),
                Is.True);
            Assert.That(field, Is.EqualTo(FootstepGroundKind.Carpet));
            Assert.That(
                FootstepGroundOverlay.TryFind(
                    room.TransformPoint(new Vector3(7.0f, 0f, 0.55f)),
                    out FootstepGroundKind rug),
                Is.True);
            Assert.That(rug, Is.EqualTo(FootstepGroundKind.Carpet));
            Assert.That(
                FootstepGroundOverlay.TryFind(
                    room.TransformPoint(new Vector3(-5.6f, 0f, 0f)),
                    out _),
                Is.False,
                "The aisle between the carpet fields is bare board.");
        }

        [Test]
        public void ToSfx_MapsEveryKindToADistinctFootstepCue()
        {
            var cues = new HashSet<RetroSfxId>();
            foreach (FootstepGroundKind kind in
                     Enum.GetValues(typeof(FootstepGroundKind)))
            {
                RetroSfxId cue = HeroFootstepGround.ToSfx(kind);
                if (kind == FootstepGroundKind.None)
                {
                    Assert.That(cue, Is.EqualTo(RetroSfxId.None));
                    continue;
                }

                Assert.That(
                    cue.ToString(),
                    Does.StartWith("Footstep"),
                    kind.ToString());
                Assert.That(cues.Add(cue), Is.True, kind + " shares a cue");
            }
        }

        private GameObject Track(GameObject gameObject)
        {
            created.Add(gameObject);
            return gameObject;
        }

        private GameObject CreateFloor(
            string name,
            FootstepGroundKind kind,
            float extent = 4f)
        {
            GameObject floor = Track(new GameObject(name));
            floor.transform.position = new Vector3(0f, -0.05f, 0f);
            floor.AddComponent<BoxCollider>().size =
                new Vector3(extent, 0.1f, extent);
            if (kind != FootstepGroundKind.None)
            {
                FootstepGround.Stamp(floor, kind);
            }

            return floor;
        }

        private static bool Resolve(
            Vector3 feet,
            Transform hero,
            out FootstepGroundKind kind,
            out Vector3 contact)
        {
            Physics.SyncTransforms();
            return HeroFootstepGround.TryResolve(
                feet,
                hero,
                out kind,
                out contact);
        }
    }
}

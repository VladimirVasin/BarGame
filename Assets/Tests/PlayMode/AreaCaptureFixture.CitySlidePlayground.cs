using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace BarPromenade.Tests.PlayMode
{
    public sealed partial class AreaCaptureFixture
    {
        [UnityTest]
        [Explicit("Review a complete child visit to the residential slide.")]
        [Timeout(240000)]
        public IEnumerator CitySlidePlaygroundChildVisit()
        {
            GameSessionState.BeginNewGame();
            GameSessionState.PrepareHomeReturn();
            Assert.That(GameSessionState.TryStartGameTimeAt(12 * 60), Is.True);
            AsyncOperation load = SceneManager.LoadSceneAsync(SceneIds.City, LoadSceneMode.Single);
            CityGameRoot city = null;
            float deadline = Time.realtimeSinceStartup + TimeoutSeconds * 2f;
            while (Time.realtimeSinceStartup < deadline)
            {
                city = Object.FindAnyObjectByType<CityGameRoot>();
                if (load.isDone && city != null && city.IsInitialized && !CompositionDriver.IsComposing) break;
                yield return null;
            }
            Assert.That(city != null && city.IsInitialized && !CompositionDriver.IsComposing, Is.True);
            CityCourtyardPlayground site = city.World.SlidePlayground;
            Assert.That(site, Is.Not.Null, "Default City needs a fitting slide courtyard.");
            Assert.That(city.World.CourtyardPlayground, Is.Not.Null);
            Assert.That(site.Site.Cell, Is.Not.EqualTo(city.World.CourtyardPlayground.Site.Cell));
            Assert.That(city.World.PlaygroundSwings, Has.Length.EqualTo(2));
            var grass = city.World.Root.GetComponentInChildren<CityGrassField>(true);
            Assert.That(grass, Is.Not.Null);
            AssertPlaygroundGroundClear(city, grass, CityRoadPolygon.Rectangle(site.Site.Footprint));
            AssertPlaygroundGroundClear(city, grass, CityRoadPolygon.Rectangle(city.World.CourtyardPlayground.Site.Footprint));
            foreach (CityDecorationDescriptor part in city.World.DecorationPlan.Descriptors)
                if (part.Kind == CityDecorationKind.ParkPlayground)
                    AssertPlaygroundGroundClear(city, grass, CityPlaygroundGeometry.ParkGroundFootprint(city.Layout, part));
            var slide = site.First;
            Assert.That(Vector3.Distance(slide.Entry.position, slide.Exit.position), Is.EqualTo(4.36f).Within(.016f));
            float lip = slide.Entry.position.y;
            Assert.That(slide.Contacts[6].position.y - lip, Is.EqualTo(.76f).Within(.016f));
            Assert.That(slide.Contacts[13].position.y - lip, Is.EqualTo(.06f).Within(.016f));
            Debug.Log($"Slide playground at {site.Site.Cell}, join {site.Site.RouteJoin}, center {site.transform.position}, heading {site.transform.forward}");
            Physics.SyncTransforms();
            CityPlaygroundVisit access = default;
            if (!access.TryStart(System.Array.Empty<CityPlaygroundSwing>(), 100000, site.Site.RouteJoin, slide: site))
                Assert.Fail($"Blocked approach at {site.Site.Cell}. " +
                    $"Join to side: {SlideApproachBlocker(site.Site.RouteJoin, site.SideEntry)}. " +
                    $"Side to stair: {SlideApproachBlocker(site.SideEntry, slide.Entry.position)}.");
            access.Cancel(null, 100000);

            Camera camera = city.Camera;
            PlayerCameraFollow follow = camera.GetComponent<PlayerCameraFollow>();
            if (follow != null) follow.enabled = false;
            city.Player.Motor.SetInputEnabled(false);
            city.Player.Motor.Teleport(site.Site.RouteJoin + site.transform.right * 2f);
            Vector3 center = site.transform.position;
            Vector3 forward = site.transform.forward, right = site.transform.right;
            camera.transform.position = center + right * 4.8f + forward * 1.1f + Vector3.up * 1.7f;
            camera.transform.rotation = Quaternion.LookRotation(center + Vector3.up * .7f - camera.transform.position);

            var blockerRoot = new GameObject("Slide landing test obstacle");
            BoxCollider blocker = blockerRoot.AddComponent<BoxCollider>();
            blockerRoot.transform.position = slide.Exit.position + Vector3.up * .5f;
            blocker.size = new Vector3(.42f, 1f, .42f);
            float previousDelta = Time.captureDeltaTime;
            try
            {
                Time.captureDeltaTime = .065f;
                Physics.SyncTransforms();
                CityAmbientPedestrians ambient = city.AmbientPedestrians;
                int slot = -1, encounter = 0;
                Vector3 departure = default;
                CityPedestrianPresentation child = null;
                Transform hand = null;
                bool climbed = false, waited = false, descended = false, landed = false, returned = false;
                float approachTime = float.NaN;
                CityPlaygroundVisit.Phase phase = CityPlaygroundVisit.Phase.None;
                deadline = Time.realtimeSinceStartup + 120f;
                while (Time.realtimeSinceStartup < deadline && !returned)
                {
                    if (slot < 0 && site.Visitor != 0)
                    {
                        for (int i = 0; i < CityAmbientPedestrians.MaximumActive; i++)
                        {
                            if (!ambient.TryGetWalker(i, out Vector3 feet, out _, out int owner) ||
                                owner != site.Visitor) continue;
                            slot = i;
                            encounter = owner;
                            departure = feet;
                            child = ambient.GetWalkerBody(i);
                            Assert.That(site.TryReserve(encounter + 999), Is.False);
                            foreach (Transform bone in child.Registry.Animator.GetComponentsInChildren<Transform>())
                                if (bone.name == "SOCKET_Grip.L") hand = bone;
                            Assert.That(hand, Is.Not.Null);
                            break;
                        }
                    }
                    else if (slot >= 0)
                    {
                        Assert.That(ambient.GetWalkerBody(slot), Is.SameAs(child));
                        phase = ambient.GetPlaygroundPhase(slot);
                        if (phase == CityPlaygroundVisit.Phase.Slide)
                        {
                            if (float.IsNaN(approachTime)) approachTime = Time.time;
                            ambient.Advance(.0001f);
                            Vector3 pelvis = child.Registry.PelvisAnchor.position;
                            float along = Vector3.Dot(pelvis - slide.Entry.position, forward);
                            Vector3 railStart = slide.Contacts[4].position - right * .29f;
                            Vector3 railEnd = slide.Contacts[5].position - right * .29f;
                            if (!climbed && SegmentDistance(hand.position, railStart, railEnd) < .034f &&
                                OnSlideTread(child.Registry, slide.Contacts, forward))
                            {
                                CaptureCurrentCamera(camera, "CitySlidePlaygroundChildVisit", "01-climb", 960, 720);
                                climbed = true;
                            }
                            if (!waited && Time.time - approachTime > 5.4f)
                            {
                                Assert.That(along, Is.EqualTo(1.97f).Within(.07f), "The child waits above the occupied exit.");
                                blocker.enabled = false;
                                Physics.SyncTransforms();
                                waited = true;
                            }
                            if (!descended && along > 2.66f && along < 3.26f)
                            {
                                float height = SlideHeightAt(along, slide, forward);
                                Assert.That(pelvis.y - height, Is.EqualTo(.118f).Within(.035f), "Seat follows the slide profile.");
                                Assert.That(waited, Is.True);
                                CaptureCurrentCamera(camera, "CitySlidePlaygroundChildVisit", "02-slide", 960, 720);
                                descended = true;
                            }
                        }
                        else if (phase == CityPlaygroundVisit.Phase.SlideExitSide && !landed)
                        {
                            Assert.That(child.AuthoredActionWeight, Is.Zero);
                            Assert.That(Vector3.Distance(child.transform.position, slide.Exit.position), Is.LessThan(.15f));
                            CaptureCurrentCamera(camera, "CitySlidePlaygroundChildVisit", "03-runout", 960, 720);
                            landed = true;
                        }
                        else if (phase == CityPlaygroundVisit.Phase.None)
                        {
                            Assert.That(Vector3.Distance(child.transform.position, departure), Is.LessThan(.7f));
                            Assert.That(site.TryReserve(encounter + 999), Is.True);
                            site.Release(encounter + 999);
                            returned = true;
                        }
                    }
                    yield return null;
                }
                Assert.That(climbed && waited && descended && landed && returned, Is.True,
                    $"cell={site.Site.Cell}, visitor={site.Visitor}, phase={phase}, climb={climbed}, wait={waited}, slide={descended}, ground={landed}, returned={returned}");
            }
            finally
            {
                Object.Destroy(blockerRoot);
                Time.captureDeltaTime = previousDelta;
                city.Player.Motor.SetInputEnabled(true);
                if (follow != null) follow.enabled = true;
            }
        }

        private static bool OnSlideTread(CityPedestrianAssetRegistry actor, Transform[] contacts, Vector3 forward)
        {
            for (int i = 0; i < 4; i++)
                if (Planted(actor.LeftFoot.position, contacts[i].position) || Planted(actor.RightFoot.position, contacts[i].position))
                    return true;
            return false;

            bool Planted(Vector3 foot, Vector3 step) =>
                Mathf.Abs(foot.y - step.y - .065f) < .025f &&
                Mathf.Abs(Vector3.Dot(foot - step, forward)) < .116f;
        }

        private static void AssertPlaygroundGroundClear(CityGameRoot city, CityGrassField grass, Vector2[] polygon)
        {
            Rect bounds = CityRoadPolygon.Bounds(polygon);
            float spacing = CityGrassPlan.Spacing;
            for (int z = Mathf.FloorToInt(bounds.yMin / spacing); z < Mathf.CeilToInt(bounds.yMax / spacing); z++)
            for (int x = Mathf.FloorToInt(bounds.xMin / spacing); x < Mathf.CeilToInt(bounds.xMax / spacing); x++)
            {
                Vector2 point = CityGrassPlan.Root(city.Layout.Seed, x, z);
                if (CityRoadPolygon.Contains(polygon, point))
                    Assert.That(grass.Surface.TrySample(point, out _, out _, out _), Is.False,
                        $"Grass root inside play area at {point:F2}.");
            }
        }

        private static string SlideApproachBlocker(Vector3 from, Vector3 to)
        {
            int count = Mathf.Max(1, Mathf.CeilToInt(Vector3.Distance(from, to) / .4f));
            for (int i = 0; i <= count; i++)
            {
                Vector3 feet = Vector3.Lerp(from, to, (float)i / count);
                if (!Physics.Raycast(feet + Vector3.up * .26f, Vector3.down, out RaycastHit hit, .52f,
                    CityPedestrianCollision.NonPedestrianMask, QueryTriggerInteraction.Ignore) || hit.normal.y < .82f)
                    return $"no ground at {feet}";
                Collider[] blockers = Physics.OverlapCapsule(hit.point + Vector3.up * .32f, hit.point + Vector3.up * 1.07f,
                    .23f, CityPedestrianCollision.NonPedestrianMask, QueryTriggerInteraction.Ignore);
                if (blockers.Length != 0) return $"{blockers[0].name} at {hit.point}";
            }
            return "clear";
        }

        private static float SegmentDistance(Vector3 point, Vector3 start, Vector3 end)
        {
            Vector3 line = end - start;
            return Vector3.Distance(point, start + line * Mathf.Clamp01(Vector3.Dot(point - start, line) / line.sqrMagnitude));
        }

        private static float SlideHeightAt(float along, CityCourtyardPlayground.Crossing slide, Vector3 forward)
        {
            for (int i = 7; i < slide.Contacts.Length; i++)
            {
                Vector3 last = slide.Contacts[i - 1].position, next = slide.Contacts[i].position;
                float a = Vector3.Dot(last - slide.Entry.position, forward), b = Vector3.Dot(next - slide.Entry.position, forward);
                if (along <= b) return Mathf.Lerp(last.y, next.y, Mathf.InverseLerp(a, b, along));
            }
            return slide.Contacts[slide.Contacts.Length - 1].position.y;
        }
    }
}

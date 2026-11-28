using System.Collections;
using System.Linq;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace BarPromenade.Tests.PlayMode
{
    public sealed partial class AreaCaptureFixture
    {
        [UnityTest]
        public IEnumerator CityLeaves_GustLandingAndWalkingContacts() => CaptureCityLocalAcceptance(ReviewWindblownLeaves);

        private static IEnumerator ReviewWindblownLeaves(CityGameRoot city)
        {
            var motor = city.Player.Motor;
            var view = city.Camera.transform;
            Vector3 ridgeEye = city.Layout.PlayerHome.DoorPosition;
            motor.Teleport(ridgeEye);
            yield return null;
            var ridge = Object.FindObjectsByType<CityLeafField>().First(f => f.Sites.Any(s => s.Habitat == CityLeafHabitat.Ridge));
            var planting = CityRidgeVegetationPlan.ForLayout(city.Layout);
            var scattered = planting.Plants.Where(p => p.OnOpenEarth).ToArray();
            Assert.That(scattered.Length, Is.GreaterThanOrEqualTo(10), "Open Ridge slopes must carry trees between the yards.");
            var ridgeStand = scattered.OrderByDescending(p => p.Form.Value.Height).First();
            var treeRoot = city.World.RidgeRoot.transform.Find(CityRidgeVegetationPlan.RootName);
            var grown = treeRoot.Cast<Transform>().First(t => Vector3.Distance(t.position, ridgeStand.Origin) < .01f);
            var foliage = grown.GetComponentsInChildren<MeshFilter>().Select(f => f.sharedMesh.bounds.max.y * f.transform.lossyScale.y +
                f.transform.position.y - grown.position.y).Max();
            Assert.That(foliage, Is.EqualTo(ridgeStand.Form.Value.Height).Within(.04f));
            Assert.That(foliage, Is.GreaterThanOrEqualTo(2f * (5.23f + .24f * ridgeStand.Variant)));
            Assert.That(CityHillsideGroundPlan.ForLayout(city.Layout).IsEarth(new Vector2(grown.position.x, grown.position.z)), Is.True);
            CaptureStand(ridgeStand.Origin, ridgeStand.Form.Value.Height, "ridge-open-earth");
            var descent = CityPawnshopDescentPlan.ForLayout(city.Layout);
            Assert.That(descent.Landings.All(l => ridge.Sites.Any(s => s.Position == l.End)), Is.True);
            var landing = descent.Landings[0];
            ridge.Advance(landing.End, .02f, Vector3.zero, .18f);
            var drift = ridge.LeavesAt(ridge.Sites.ToList().FindIndex(s => s.Position == landing.End))
                .Where(l => l.Placed).ToArray();
            Assert.That(drift.Length, Is.InRange(24, CityLeafSite.MaximumLeaves));
            var core = drift.OrderByDescending(l => l.Shelter).First();
            Assert.That(drift.Count(l => Vector3.Distance(l.Position, core.Position) < .35f), Is.GreaterThanOrEqualTo(12));
            view.position = core.Position + new Vector3(.55f, 1.1f, -1.25f);
            view.LookAt(core.Position);
            CaptureCurrentCamera(city.Camera, "CityWindblownLeaves", "ridge-steps", 960, 540);
            var untouched = drift.Select(l => l.Position).ToArray();
            Vector3 boot = core.Position - Vector3.forward * .10f;
            CityLeafField.Footfall(motor.transform, boot, Vector3.forward * 2.6f);
            Assert.That(drift.Count(l => l.Flying || l.HoldingFoot), Is.GreaterThanOrEqualTo(10),
                "One planted boot must disturb several naturally placed leaves in the same drift.");
            Assert.That(drift.Any(l => !l.Flying && !l.HoldingFoot && l.Velocity.sqrMagnitude < .0009f), Is.True);
            for (int i = 0; i < 5; i++) ridge.Advance(landing.End, .05f, Vector3.zero, .18f);
            int displaced = drift.Where((leaf, i) => Vector3.Distance(leaf.Position, untouched[i]) >= .21f).Count();
            Assert.That(displaced, Is.GreaterThanOrEqualTo(6), "A stride opens a visible gap in one quarter-second.");
            Debug.Log($"Leaf stride review: {displaced} of {drift.Length} leaves moved at least 0.21m in 0.25s.");
            CaptureCurrentCamera(city.Camera, "CityWindblownLeaves", "ridge-kick", 960, 540);

            Vector3 park = city.Layout.Park.TreePositions[0];
            motor.Teleport(park + Vector3.right * 2f);
            for (int i = 0; i < 360 && !city.LowerDistrictReady; i++) yield return null;
            Physics.SyncTransforms();
            var field = Object.FindObjectsByType<CityLeafField>().First(f => f.Sites.Any(s => s.Habitat == CityLeafHabitat.Park));
            foreach (var habitat in new[] { CityLeafHabitat.Birch, CityLeafHabitat.Fence, CityLeafHabitat.Garden,
                CityLeafHabitat.Paving, CityLeafHabitat.StreetEdge })
                Assert.That(field.Sites.Any(s => s.Habitat == habitat), Is.True, habitat.ToString());

            var open = field.Sites.Where(s => s.Habitat == CityLeafHabitat.OpenGround).ToArray();
            Assert.That(open.Length, Is.GreaterThanOrEqualTo(10), "Ordinary city earth outside parks and courtyards must carry trees.");
            var landTree = open.OrderBy(s => (s.Position - park).sqrMagnitude).First();
            Assert.That(new CityMapCityTeleportGround(city.Layout).TryResolveStandingPosition(
                new Vector2(landTree.Position.x, landTree.Position.z), out _), Is.False);
            CaptureStand(landTree.Position, landTree.CrownHeight * 1.25f, "city-open-earth");
            Debug.Log($"Soil tree review: {scattered.Length} open Ridge trees, {open.Length} lower City trees outside the park; " +
                $"tallest scattered Ridge crown {foliage:F2}m, donor {5.23f + .24f * ridgeStand.Variant:F2}m.");
            field.Advance(park, .02f, Vector3.zero, .18f);
            var leaves = field.LeavesAt(field.Sites.ToList().FindIndex(s => s.Position == park));
            Assert.That(leaves.Count(l => l.Placed && !l.InCrown), Is.GreaterThanOrEqualTo(70));
            Assert.That(leaves.Count(l => l.InCrown), Is.GreaterThanOrEqualTo(5));
            var crown = leaves.First(l => l.InCrown);
            Vector3 release = crown.Position;
            for (int i = 0; i < 220 && !crown.Flying; i++) field.Advance(park, .05f, Vector3.right * .6f, .18f);
            Assert.That(crown.Flying, Is.True);
            for (int i = 0; i < 18; i++) field.Advance(park, .05f, Vector3.right * .6f, .18f);
            Assert.That(crown.Position.x, Is.GreaterThan(release.x + .15f));
            view.position = crown.Position + new Vector3(.6f, .25f, -1.2f);
            view.LookAt(crown.Position);
            CaptureCurrentCamera(city.Camera, "CityWindblownLeaves", "park-flight", 960, 540);
            for (int i = 0; i < 440 && crown.Flying; i++) field.Advance(park, .04f, Vector3.zero, .18f);
            Assert.That(crown.Placed && !crown.Flying && crown.Position.y < release.y - 1f, Is.True);
            Vector3 settled = crown.Position;

            var sole = new GameObject("Leaf acceptance foot");
            bool oldMute = AudioListener.pause;
            try
            {
                sole.transform.position = settled + Vector3.up * .08f;
                CityLeafField.Footfall(sole.transform, settled + Vector3.up * 2f, Vector3.forward * 2f);
                Assert.That(crown.HoldingFoot, Is.Null, "A foot on the upper floor cannot touch the lower leaf.");
                CityLeafField.Footfall(sole.transform, settled, Vector3.forward * 2f);
                field.Advance(park, .1f, Vector3.right, .18f);
                Assert.That(crown.Position, Is.EqualTo(settled));
                Assert.That(crown.HoldingFoot, Is.EqualTo(sole.transform));
                sole.transform.position += Vector3.up * .25f;
                field.Advance(park, .05f, Vector3.zero, .18f);
                Assert.That(crown.HoldingFoot, Is.Null);

                // Send an ordinary registered walker's stride through the live director with the audio pool muted.
                var listener = city.Player.GameObject.GetComponent<NpcFootstepDirector>();
                Assert.That(listener, Is.Not.Null);
                AudioListener.pause = true;
                Vector3 line = Vector3.forward;
                sole.transform.position = settled - line * (PlayerMotor.FootstepStride + .20f);
                NpcFootstepSources.Register(sole.transform);
                int contacts = field.StepContacts;
                listener.Tick(.05f);
                for (int i = 0; i < 14; i++)
                {
                    sole.transform.position += line * .10f;
                    listener.Tick(.05f);
                }
                Assert.That(field.StepContacts, Is.GreaterThan(contacts));
                field.Advance(park, .1f, Vector3.zero, .18f);
                Assert.That(Vector3.Distance(crown.Position, settled), Is.GreaterThan(.012f));
                view.position = crown.Position + new Vector3(.26f, .46f, -.65f);
                view.LookAt(crown.Position);
                CaptureCurrentCamera(city.Camera, "CityWindblownLeaves", "park-footfall", 960, 540);
            }
            finally
            {
                AudioListener.pause = oldMute;
                NpcFootstepSources.Unregister(sole.transform);
                Object.Destroy(sole);
            }

            // After a crown leaf is carried beyond supported ground, that tree must keep shedding.
            crown.Placed = false;
            for (int i = 0; i < 440 && !crown.InCrown; i++) field.Advance(park, .1f, Vector3.zero, .18f);
            Assert.That(crown.Placed && crown.InCrown, Is.True, "A lost airborne leaf replenishes from the same tree.");

            // Place two of the gate's own leaves in the boot lanes, then walk the real motor.
            var gate = city.Layout.Park.Gates[0];
            Vector3 start = gate.Center, direction = -gate.OutwardNormal;
            var actor = city.Player.GameObject.transform;
            motor.Teleport(start + Vector3.up * .12f);
            actor.rotation = Quaternion.LookRotation(direction);
            view.position = start + Vector3.up * 1.3f - direction * 2f;
            view.LookAt(start);
            yield return null;
            field.Advance(start, .01f, Vector3.zero, .18f);
            var gateLeaves = field.LeavesAt(field.Sites.ToList().FindIndex(s => s.Habitat == CityLeafHabitat.Paving &&
                s.Position == gate.Center)).Where(l => l.Placed).Take(2).ToArray();
            Assert.That(gateLeaves.Length, Is.EqualTo(2));
            int beforeWalk = field.StepContacts;
            for (int i = 0; i < 65 && field.StepContacts == beforeWalk; i++)
            {
                var metrics = city.Player.Visual.Metrics;
                for (int j = 0; j < gateLeaves.Length; j++)
                {
                    var foot = j == 0 ? metrics.LeftFootWorldPosition : metrics.RightFootWorldPosition;
                    if (!HeroFootstepGround.TryResolve(foot, actor, out _, out var pavement)) continue;
                    gateLeaves[j].Position = pavement + direction * .17f;
                    gateLeaves[j].Velocity = Vector3.zero;
                    gateLeaves[j].Flying = false;
                    gateLeaves[j].Wait = 2f;
                }
                motor.MoveTowardsApproachWaypoint(start + direction * 6f, .1f, .05f);
                yield return null;
            }
            Assert.That(field.StepContacts, Is.GreaterThan(beforeWalk), "The walking hero's planted boot must move a leaf.");

            void CaptureStand(Vector3 root, float height, string name)
            {
                Vector3 target = root + Vector3.up * (height * .45f);
                Vector3 eye = root + Vector3.up * height;
                for (int angle = 0; angle < 360; angle += 30)
                {
                    Vector3 offset = Quaternion.Euler(0, angle, 0) * Vector3.forward * (height * 1.4f);
                    Vector3 from = root + offset + Vector3.up * 35f;
                    if (!Physics.Raycast(from, Vector3.down, out var floor, 75f, Physics.DefaultRaycastLayers,
                        QueryTriggerInteraction.Ignore) || floor.normal.y < .73f) continue;
                    var candidate = floor.point + Vector3.up * 1.75f;
                    if (candidate.y < root.y - 1f || candidate.y > root.y + height * .65f ||
                        Physics.Linecast(candidate, target, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore)) continue;
                    eye = candidate; break;
                }
                view.SetPositionAndRotation(eye, Quaternion.LookRotation(target - eye));
                CaptureCurrentCamera(city.Camera, "CityWindblownLeaves", name, 960, 540);
            }
        }

        [UnityTest]
        public IEnumerator HomeBalconyLeaves_FallOutsideTheRoom()
        {
            GameSessionState.BeginNewGame("balcony-leaves");
            GameSessionState.TryStartGameTimeAt(12 * 60);
            GameSessionState.PrepareHomeArrival(HomeArrivalKind.Normal);
            GameSessionState.EnterHome();
            yield return SceneManager.LoadSceneAsync(SceneIds.HomeInterior, LoadSceneMode.Single);
            HomeInteriorRoot home = null;
            yield return WaitHomeBalcony(() =>
            {
                home = Object.FindAnyObjectByType<HomeInteriorRoot>();
                return home && home.IsInitialized;
            }, "exterior leaf field");
            var field = home.ExteriorView.GetComponentInChildren<CityLeafField>();
            Assert.That(field, Is.Not.Null);
            Vector3 stand = home.Room.TransformPoint(home.SmokingPlan.EntryRootPosition);
            home.Player.Motor.Teleport(stand);
            // The photographer stands beside the hero at the rail, clear of the third-person head mesh.
            Vector3 eye = stand + home.Room.forward * .9f + Vector3.up * 1.6f;
            field.Advance(eye, .02f, Vector3.zero, .15f);
            var groups = Enumerable.Range(0, field.Sites.Count).Select(field.LeavesAt).Where(g =>
                g.Any(l => l.InCrown) && g.Count(l => l.Placed && !l.InCrown) >= 20).ToArray();
            Assert.That(groups, Is.Not.Empty, "Trees in the retained exterior also have dense ground piles.");
            var crown = groups.Select(g => g.First(l => l.InCrown)).OrderByDescending(l =>
                Vector3.Dot((l.Position - eye).normalized, home.Room.right)).First();
            var start = crown.Position;
            for (int i = 0; i < 200 && !crown.Flying; i++) field.Advance(eye, .05f, home.Room.forward * .52f, .15f);
            Assert.That(crown.Flying, Is.True);
            for (int i = 0; i < 16; i++) field.Advance(eye, .05f, home.Room.forward * .52f, .15f);
            Assert.That(Vector3.Distance(crown.Position, start), Is.GreaterThan(.3f));
            Camera.main.transform.SetPositionAndRotation(eye, Quaternion.LookRotation(crown.Position - eye));
            CaptureCurrentCamera(Camera.main, "CityWindblownLeaves", "home-balcony", 960, 540);
        }
    }
}

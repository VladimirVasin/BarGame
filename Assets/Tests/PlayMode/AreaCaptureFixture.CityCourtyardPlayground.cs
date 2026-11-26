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
        [Explicit("Review a complete child visit to the residential playground.")]
        [Timeout(240000)]
        public IEnumerator CityCourtyardPlaygroundChildVisit()
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
            var court = city.World.CourtyardPlayground;
            Assert.That(court, Is.Not.Null, "Default City needs a fitting residential site.");
            Assert.That(city.World.PlaygroundSwings, Has.Length.EqualTo(2));
            Assert.That(Vector3.Distance(court.Bars.Entry.position, court.Bars.Exit.position), Is.EqualTo(2.92f).Within(.015f));
            Assert.That(Vector3.Distance(court.Beam.Entry.position, court.Beam.Exit.position), Is.EqualTo(2.72f).Within(.015f));
            Transform litter = city.World.Root.transform.Find(CityLitterWorldBuilder.RootName);
            Assert.That(litter, Is.Not.Null);
            foreach (Renderer renderer in litter.GetComponentsInChildren<Renderer>())
            {
                Bounds bounds = renderer.bounds;
                Assert.That(court.Site.Footprint.Overlaps(
                    Rect.MinMaxRect(bounds.min.x, bounds.min.z, bounds.max.x, bounds.max.z)), Is.False, renderer.name);
            }

            CityAmbientPedestrians ambient = city.AmbientPedestrians;
            Physics.SyncTransforms();
            CityPlaygroundVisit access = default;
            Assert.That(access.TryStart(System.Array.Empty<CityPlaygroundSwing>(), 100000,
                court.Site.RouteJoin, court), Is.True,
                $"Blocked fitted approach at {court.Site.Cell}, join={court.Site.RouteJoin}, bars={court.Bars.Entry.position}");
            access.Cancel(null, 100000);
            Debug.Log($"Playground at {court.Site.Cell}, join {court.Site.RouteJoin}, center {court.transform.position}");
            Vector3 center = court.transform.position;
            Camera camera = city.Camera;
            PlayerCameraFollow follow = camera.GetComponent<PlayerCameraFollow>();
            if (follow != null) follow.enabled = false;
            city.Player.Motor.SetInputEnabled(false);
            city.Player.Motor.Teleport(court.Site.RouteJoin + court.transform.right * 2f);
            camera.transform.position = center + court.transform.right * 4.2f - court.transform.forward * 3f + Vector3.up * 1.9f;
            camera.transform.rotation = Quaternion.LookRotation(center + Vector3.up * .75f - camera.transform.position);
            float previousDelta = Time.captureDeltaTime;
            try
            {
                Time.captureDeltaTime = .065f;
                Physics.SyncTransforms();
                int slot = -1, encounter = 0;
                CityPedestrianPresentation child = null;
                bool bars = false, beam = false, returned = false;
                Vector3 departure = default;
                int closestRung = -1;
                bool progressed = false;
                int nearChild = 0;
                float closestChild = float.PositiveInfinity;
                CityPlaygroundVisit.Phase last = CityPlaygroundVisit.Phase.None;
                Transform left = null, right = null;
                deadline = Time.realtimeSinceStartup + 120f;
                while (Time.realtimeSinceStartup < deadline && !returned)
                {
                    if (slot < 0)
                    {
                        for (int i = 0; i < CityAmbientPedestrians.MaximumActive; i++)
                        {
                            CityPedestrianPresentation walker = ambient.GetWalkerBody(i);
                            if (walker != null && DefaultNpcCatalog.IsStreetChild(walker.Registry.DesignId))
                            {
                                float distance = Vector3.Distance(walker.transform.position, court.Site.RouteJoin);
                                closestChild = Mathf.Min(closestChild, distance);
                                if (distance < 6f) nearChild++;
                            }
                            if (ambient.GetPlaygroundPhase(i) != CityPlaygroundVisit.Phase.CourtJoin) continue;
                            slot = i;
                            child = ambient.GetWalkerBody(i);
                            ambient.TryGetWalker(i, out departure, out _, out encounter);
                            Assert.That(court.TryReserve(encounter + 999), Is.False);
                            foreach (Transform bone in child.Registry.Animator.GetComponentsInChildren<Transform>())
                            {
                                if (bone.name == "SOCKET_Grip.L") left = bone;
                                if (bone.name == "SOCKET_Grip.R") right = bone;
                            }
                            Assert.That(left != null && right != null, Is.True);
                            break;
                        }
                    }
                    else
                    {
                        Assert.That(ambient.GetWalkerBody(slot), Is.SameAs(child));
                        Assert.That(ambient.TryGetWalker(slot, out Vector3 position, out _, out int current), Is.True);
                        Assert.That(current, Is.EqualTo(encounter));
                        last = ambient.GetPlaygroundPhase(slot);
                        if (last == CityPlaygroundVisit.Phase.Bars)
                        {
                            ambient.Advance(.0001f);
                            int rung = RungContact(left.position, court.Bars);
                            int other = RungContact(right.position, court.Bars);
                            if (rung >= 0 && other >= 0)
                            {
                                if (closestRung == -1) closestRung = Mathf.Min(rung, other);
                                progressed |= Mathf.Min(rung, other) > closestRung;
                                if (!bars && progressed)
                                {
                                    CaptureCurrentCamera(camera, "CityCourtyardPlaygroundChildVisit", "01-hand-over-hand", 960, 720);
                                    bars = true;
                                }
                            }
                        }
                        else if (last == CityPlaygroundVisit.Phase.Beam && !beam)
                        {
                            ambient.Advance(.0001f);
                            Vector3 top = court.Beam.Contacts[0].position;
                            if (BootOnBeam(child.Registry.LeftFoot, top, court.Beam) ||
                                BootOnBeam(child.Registry.RightFoot, top, court.Beam))
                            {
                                CaptureCurrentCamera(camera, "CityCourtyardPlaygroundChildVisit", "02-balance", 960, 720);
                                beam = true;
                            }
                        }
                        else if (last == CityPlaygroundVisit.Phase.None)
                        {
                            Assert.That(Vector3.Distance(position, departure), Is.LessThan(.7f));
                            Assert.That(court.Visitor, Is.Not.EqualTo(encounter));
                            Assert.That(court.TryReserve(encounter + 999), Is.True);
                            court.Release(encounter + 999);
                            returned = true;
                        }
                    }
                    yield return null;
                }
                Assert.That(bars && beam && returned, Is.True,
                    $"cell={court.Site.Cell}, seat={court.Visitor}, slot={slot}, phase={last}, bars={bars}, beam={beam}, " +
                    $"returned={returned}, closestChild={closestChild:F2}, nearSamples={nearChild}");
            }
            finally
            {
                Time.captureDeltaTime = previousDelta;
                city.Player.Motor.SetInputEnabled(true);
                if (follow != null) follow.enabled = true;
            }
        }

        private static int RungContact(Vector3 hand, CityCourtyardPlayground.Crossing crossing)
        {
            Vector3 axis = (crossing.Exit.position - crossing.Entry.position).normalized;
            for (int i = 0; i < crossing.Contacts.Length; i++)
            {
                Vector3 offset = hand - crossing.Contacts[i].position;
                if (Mathf.Abs(offset.y) < .025f && Mathf.Abs(Vector3.Dot(offset, axis)) < .025f &&
                    Mathf.Abs(Vector3.Dot(offset, Vector3.Cross(Vector3.up, axis))) < .36f) return i;
            }
            return -1;
        }

        private static bool BootOnBeam(Transform foot, Vector3 top, CityCourtyardPlayground.Crossing crossing)
        {
            Vector3 axis = (crossing.Exit.position - crossing.Entry.position).normalized;
            Vector3 offset = foot.position - top;
            return Mathf.Abs(offset.y - .065f) < .025f && Mathf.Abs(Vector3.Dot(offset, axis)) < 1.07f &&
                   Mathf.Abs(Vector3.Dot(offset, Vector3.Cross(Vector3.up, axis))) < .105f;
        }
    }
}

using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

namespace BarPromenade.Tests.PlayMode
{
    public sealed partial class AreaCaptureFixture
    {
        [UnityTest]
        [Explicit("City-wide litter: placement, ground contact, colliders and named district views.")]
        public IEnumerator CityLitter()
        {
            GameSessionState.BeginNewGame();
            GameSessionState.TryStartGameTimeFromWake();
            GameSessionState.AdvanceGameTime((float)((12d * 60d - GameSessionState.GameTimeOfDayMinutes) /
                GameTimeState.GameMinutesPerRealSecond));
            CityGameRoot city = null;
            yield return Capture(SceneIds.City, () =>
            {
                city = Object.FindAnyObjectByType<CityGameRoot>();
                return city != null && city.IsInitialized ? city : null;
            }, () =>
            {
                // One establishing frame through the fixture; the close-ups
                // below pose the camera themselves once physics can be asked.
                CityLitterPart? part = First(LitterPlan(city), CityLitterZone.Sidewalk, CityDistrictKind.Residential);
                Assert.That(part.HasValue, Is.True, "The default city must litter at least one residential pavement.");
                // From the carriageway, a stride above eye height: pavement, kerb and frontage in one frame.
                Vector3 eye = Eye(city, part.Value, 5.5f, 2.2f);
                return new[] { Shot.At("litter-overview-residential-day", eye, part.Value.Position) };
            });

            yield return CaptureLitterDetails(city, LitterPlan(city));
            var landing = new CityMapCityTeleportGround(city.Layout);
            Physics.SyncTransforms();
            VerifyCityLitter(city, LitterPlan(city), landing);
            // The eastern strip is built through the same shared instancer now.
            GameObject east = GameObject.Find(CityEastLitterWorldBuilder.RootName);
            Assert.That(east, Is.Not.Null);
            Assert.That(east.transform.childCount,
                Is.EqualTo(CityEastLitterPlan.Create(CityEastExitPlanner.Create(city.Layout)).Parts.Count));
        }

        /// <summary>An eye out on the free side of a part: away from the nearest building, at the given reach and height.</summary>
        private static Vector3 Eye(CityGameRoot city, CityLitterPart part, float reach, float height)
        {
            Vector3 away = Vector3.back;
            float best = float.PositiveInfinity;
            foreach (BuildingLot lot in city.Layout.BuildingLots)
            {
                if (!lot.HasBuilding) continue;
                Vector3 offset = part.Position - lot.Center;
                offset.y = 0f;
                if (offset.sqrMagnitude >= best) continue;
                best = offset.sqrMagnitude;
                away = offset.normalized;
            }
            return part.Position + away * reach + Vector3.up * height;
        }

        private static IEnumerator CaptureLitterDetails(CityGameRoot city, CityLitterPlan plan)
        {
            CityDecorationPlan decoration = CityWorldPlans.GetOrCreate(city.Layout)
                .GetDecoration(CityLayoutCache.GetOrCreateNightPlan(city.Layout));
            var vending = decoration.Descriptors.Where(d => d.Kind == CityDecorationKind.NightlifeVendingAndQueue)
                .Select(d => d.Position).ToArray();
            Camera camera = Camera.main;
            PlayerCameraFollow follow = camera.GetComponent<PlayerCameraFollow>();
            bool oldFollow = follow != null && follow.enabled, oldInput = city.Player.Motor.InputEnabled;
            Vector3 oldPosition = camera.transform.position;
            Quaternion oldRotation = camera.transform.rotation;
            float oldFov = camera.fieldOfView;
            try
            {
                if (follow != null) follow.enabled = false;
                city.Player.Motor.SetInputEnabled(false);
                yield return Detail("litter-sidewalk-nightlife-day", plan.Parts.Where(p => p.Zone == CityLitterZone.Sidewalk &&
                        p.District == CityDistrictKind.Nightlife)
                    .OrderBy(p => vending.Length == 0 ? 0f : vending.Min(v => Vector3.Distance(v, p.Position))).FirstOrDefault());
                yield return Detail("litter-lot-residential-day", First(plan, CityLitterZone.LotGround, CityDistrictKind.Residential));
                yield return Detail("litter-lot-industrial-day", First(plan, CityLitterZone.LotGround, CityDistrictKind.Industrial));
                yield return Detail("litter-sidewalk-old-town-day", First(plan, CityLitterZone.Sidewalk, CityDistrictKind.OldTown));
                yield return Detail("litter-park-bench-day", plan.Parts.FirstOrDefault(p => p.Zone == CityLitterZone.Park));
                yield return Detail("litter-beach-strand-day", plan.Parts.FirstOrDefault(p => p.Zone == CityLitterZone.Beach));
                yield return Detail("litter-bicycle-day", plan.Parts.FirstOrDefault(p => p.Item.Category == CityLitterPlan.BicycleCategory));
                yield return Detail("litter-solid-day", plan.Parts.FirstOrDefault(p => p.Item.Solid && p.Item.Category != CityLitterPlan.BicycleCategory));
            }
            finally
            {
                if (follow != null) follow.enabled = oldFollow;
                camera.transform.SetPositionAndRotation(oldPosition, oldRotation);
                camera.fieldOfView = oldFov;
                city.Player.Motor.SetInputEnabled(oldInput);
            }

            IEnumerator Detail(string name, CityLitterPart? candidate)
            {
                if (!candidate.HasValue) { Debug.Log("CITY LITTER DETAIL " + name + ": no such part in this city."); yield break; }
                CityLitterPart part = candidate.Value;
                Vector3 target = part.Position + Vector3.up * .08f;
                // The free side first, then around the clock until nothing stands between the eye and the thing.
                Vector3 eye = Eye(city, part, 2.1f, 1.3f);
                Vector3 away = eye - part.Position; away.y = 0f;
                for (int turn = 0; turn < 8; turn++)
                {
                    Vector3 direction = Quaternion.Euler(0f, turn * 45f, 0f) * away.normalized;
                    eye = part.Position + direction * 2.1f + Vector3.up * 1.3f;
                    if (!Physics.CheckSphere(eye, .25f, ~0, QueryTriggerInteraction.Ignore) &&
                        !Physics.Linecast(eye, target, ~0, QueryTriggerInteraction.Ignore)) break;
                }
                camera.transform.SetPositionAndRotation(eye, Quaternion.LookRotation(target - eye));
                camera.fieldOfView = 44f;
                for (int frame = 0; frame < 4; frame++) yield return null;
                Debug.Log("CITY LITTER DETAIL " + name + ": part=" + part.Id + " (" + part.Item.Name + "); eye=" + eye);
                CaptureCurrentCamera(camera, SceneIds.City, name);
            }
        }

        private static CityLitterPlan LitterPlan(CityGameRoot city) =>
            CityWorldPlans.GetOrCreate(city.Layout).GetLitter(CityLayoutCache.GetOrCreateNightPlan(city.Layout));

        private static CityLitterPart? First(CityLitterPlan plan, CityLitterZone zone, CityDistrictKind district)
        {
            foreach (CityLitterPart part in plan.Parts)
                if (part.Zone == zone && part.District == district) return part;
            return null;
        }

        private static void VerifyCityLitter(CityGameRoot city, CityLitterPlan plan, CityMapCityTeleportGround landing)
        {
            GameObject root = GameObject.Find(CityLitterWorldBuilder.RootName);
            Assert.That(root, Is.Not.Null);
            Assert.That(plan.Parts.Count, Is.InRange(380, CityLitterPlan.MaximumPartCount));
            Assert.That(root.transform.childCount, Is.EqualTo(plan.Parts.Count));
            Assert.That(LitterPlan(city), Is.SameAs(plan), "The build reads the memoised plan, not a second one.");
            Assert.That(root.GetComponentsInChildren<Light>(true), Is.Empty);
            Assert.That(root.GetComponentsInChildren<AudioSource>(true), Is.Empty);
            Assert.That(root.GetComponentsInChildren<Rigidbody>(true), Is.Empty);
            Assert.That(root.GetComponentsInChildren<Animator>(true), Is.Empty);

            var placed = root.transform.Cast<Transform>().ToDictionary(item => item.name, StringComparer.Ordinal);
            var verticesByMesh = new Dictionary<Mesh, Vector3[]>();
            var firstMeshes = new Dictionary<string, Mesh[]>(StringComparer.Ordinal);
            var firstMaterials = new Dictionary<string, Material[]>(StringComparer.Ordinal);
            float minimumClearance = float.PositiveInfinity, maximumContactGap = 0f;
            var hits = new RaycastHit[32];
            var problems = new List<string>();
            foreach (CityLitterPart part in plan.Parts)
            {
                Assert.That(placed.TryGetValue(part.Id, out Transform instance), Is.True, part.Id);
                Assert.That(instance.position.x, Is.EqualTo(part.Position.x).Within(.001f), part.Id);
                Assert.That(instance.position.z, Is.EqualTo(part.Position.z).Within(.001f), part.Id);
                Assert.That(Quaternion.Angle(instance.rotation, part.Rotation), Is.LessThan(.02f), part.Id);
                MeshFilter[] filters = instance.GetComponentsInChildren<MeshFilter>(true);
                Renderer[] renderers = instance.GetComponentsInChildren<Renderer>(true);
                Assert.That(filters, Is.Not.Empty, part.Id);
                Assert.That(renderers.All(renderer => renderer.enabled && renderer.gameObject.activeInHierarchy), Is.True, part.Id);
                Mesh[] meshes = filters.Select(filter => filter.sharedMesh).ToArray();
                Material[] materials = renderers.SelectMany(renderer => renderer.sharedMaterials).ToArray();
                if (firstMeshes.TryGetValue(part.Item.Name, out Mesh[] first))
                {
                    Assert.That(meshes, Is.EqualTo(first), "Rigid litter instances must reuse imported meshes: " + part.Id);
                    Assert.That(materials, Is.EqualTo(firstMaterials[part.Item.Name]), part.Id);
                }
                else
                {
                    firstMeshes.Add(part.Item.Name, meshes);
                    firstMaterials.Add(part.Item.Name, materials);
                }

                float contactGap = float.PositiveInfinity;
                string support = null;
                foreach (MeshFilter filter in filters)
                {
                    if (!verticesByMesh.TryGetValue(filter.sharedMesh, out Vector3[] vertices))
                    {
                        vertices = filter.sharedMesh.vertices;
                        verticesByMesh.Add(filter.sharedMesh, vertices);
                    }
                    foreach (Vector3 vertex in vertices)
                    {
                        Vector3 world = filter.transform.TransformPoint(vertex);
                        // The highest hit under the vertex that is not litter itself: the pavement, the lot's soil or the sand.
                        float ground = float.NegativeInfinity;
                        Collider highest = null;
                        int count = Physics.RaycastNonAlloc(world + Vector3.up * 2f, Vector3.down, hits, 4f, ~0, QueryTriggerInteraction.Ignore);
                        for (int index = 0; index < count; index++)
                            if (!hits[index].collider.transform.IsChildOf(root.transform) && hits[index].point.y > ground)
                            { ground = hits[index].point.y; highest = hits[index].collider; }
                        Assert.That(float.IsNegativeInfinity(ground), Is.False, "Every rendered vertex needs actual ground below it: " + part.Id + " at " + world);
                        float clearance = world.y - ground;
                        if (clearance < contactGap) { contactGap = clearance; support = highest.name + "/" + highest.GetType().Name + " at " + world; }
                        minimumClearance = Mathf.Min(minimumClearance, clearance);
                    }
                }
                // The beach collider is a 1 m chord of the same plan the sand is drawn from, so it may sit a little off.
                float sink = part.Zone == CityLitterZone.Beach ? .04f : .015f;
                float hover = .04f + (part.Zone == CityLitterZone.Beach ? .03f : 0f);
                if (contactGap < -sink || contactGap > hover)
                    problems.Add(part.Id + " (" + part.Item.Name + ", " + part.Zone + ") gap=" + contactGap.ToString("F3") + " on " + support);
                maximumContactGap = Mathf.Max(maximumContactGap, contactGap);
                Collider[] colliders = instance.GetComponentsInChildren<Collider>(true);
                if (part.Item.Solid)
                {
                    Assert.That(colliders.Length, Is.EqualTo(1), part.Id);
                    Assert.That(colliders[0], Is.TypeOf<BoxCollider>(), part.Id);
                    Assert.That(landing.TryResolveStandingPosition(new Vector2(part.Position.x, part.Position.z), out _), Is.False,
                        "Map arrivals must exclude the same large objects: " + part.Id);
                }
                else Assert.That(colliders, Is.Empty, "Small litter must never catch an ordinary footstep: " + part.Id);
            }
            Assert.That(problems, Is.Empty,
                "A rigid bottle, can or crate must touch the real ground without sinking or hovering:" + System.Environment.NewLine + string.Join(System.Environment.NewLine, problems));
            Debug.Log("CITY LITTER: " + plan.Parts.Count + " parts (" + plan.GetCount(CityLitterZone.Sidewalk) + " sidewalk, " +
                plan.GetCount(CityLitterZone.LotGround) + " lot, " + plan.GetCount(CityLitterZone.Park) + " park, " +
                plan.GetCount(CityLitterZone.Beach) + " beach, " + plan.SolidCount + " solid); terrain minimum=" + minimumClearance +
                " m; largest contact gap=" + maximumContactGap + " m.");
        }
    }
}

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
        [Explicit("City building apron materials, physical contacts and matching bounded Home reconstruction.")]
        public IEnumerator CityBuildingPerimeters()
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
                var shots = new List<Shot>();
                AddBuildingPerimeterShots(city, shots);
                return shots.ToArray();
            });
            AssertBuildingPerimeters(city);
            AssertCommonGroundExcavation(city);
            AssertBuildingPerimeters(city);
            AssertHomeCommonGround(city.Layout);
            HomeInteriorRoot home = null;
            yield return Capture(SceneIds.HomeInterior, () =>
            {
                home = Object.FindAnyObjectByType<HomeInteriorRoot>();
                return home != null && home.IsInitialized ? home : null;
            }, () =>
            {
                Rect activation = home.BalconyLayout.BalconyCameraActivationBounds;
                home.Player.Motor.Teleport(new Vector3(activation.center.x, home.Layout.PlayerSpawn.y, activation.center.y));
                Vector3 eye = new Vector3(activation.center.x, home.Layout.PlayerSpawn.y + EyeHeight, activation.center.y);
                return new[] { Shot.At("building-perimeter-home-balcony", eye,
                    new Vector3(eye.x + 13f, home.BalconyLayout.StreetGroundY, eye.z), 70f) };
            });
        }

        private static void AssertBuildingPerimeters(CityGameRoot city)
        {
            CityBuildingPerimeterPlan plan = CityBuildingPerimeterPlan.ForLayout(city.Layout);
            CityGroundSurfaceSystem system = city.World.Root.GetComponent<CityGroundSurfaceSystem>();
            Assert.That(system.BuildingPerimeterTriangleCount, Is.GreaterThan(0));
            Assert.That(plan.Footprints.Any(part => part.BuildingId == "church"), Is.True);
            Assert.That(plan.Footprints.Any(part => part.BuildingId == "cemetery-lodge"), Is.True);
            Assert.That(plan.Footprints.Any(part => part.BuildingId == "laundry"), Is.True);
            var colliders = system.GetComponentsInChildren<MeshCollider>(true).Where(item => item.enabled &&
                item.gameObject.activeInHierarchy && (item.name.StartsWith("Ground sector") ||
                    item.name.StartsWith("Dynamic ground sector"))).ToArray();
            var asphaltBuildings = new HashSet<string>();
            var block = new MaterialPropertyBlock();
            int contacts = 0;
            foreach (CityBuildingPerimeterFootprint footprint in plan.Footprints)
            for (int edge = 0; edge < footprint.Body.Length; edge++)
            {
                Vector2 a = footprint.Body[edge], b = footprint.Body[(edge + 1) % footprint.Body.Length];
                Vector2 direction = (b - a).normalized;
                Vector2 outward = new Vector2(direction.y, -direction.x);
                foreach (float along in new[] { .2f, .5f, .8f })
                foreach (float across in new[] { .25f, 1.25f })
                {
                    Vector2 point = Vector2.Lerp(a, b, along) + outward * across;
                    if (!plan.Contains(point)) continue; // An attached wing owns this footprint.
                    var ray = new Ray(new Vector3(point.x, 300f, point.y), Vector3.down);
                    bool found = false;
                    RaycastHit nearest = default;
                    foreach (MeshCollider collider in colliders)
                        if (collider.Raycast(ray, out RaycastHit hit, 600f) && (!found || hit.distance < nearest.distance))
                        { nearest = hit; found = true; }
                    if (!found) continue; // Unwalkable water/cliff beyond the mapped terrain.
                    MeshCollider ground = (MeshCollider)nearest.collider;
                    int triangle = nearest.triangleIndex, slot = 0;
                    while (slot < ground.sharedMesh.subMeshCount - 1)
                    {
                        int count = (int)ground.sharedMesh.GetIndexCount(slot) / 3;
                        if (triangle < count) break;
                        triangle -= count; slot++;
                    }
                    ground.GetComponent<Renderer>().GetPropertyBlock(block, slot);
                    // Some retained structural coatings use the ordinary Lit
                    // shader. Its absent enum is not Asphalt's numeric zero.
                    if (!block.HasProperty(Shader.PropertyToID("_GroundKind"))) continue;
                    float kind = block.GetFloat("_GroundKind");
                    Assert.That(kind != (float)GroundSurfaceKind.Soil && kind != (float)GroundSurfaceKind.Lawn, Is.True,
                        "Unpaved wall perimeter: " + footprint.BuildingId + " at " + nearest.point);
                    if (kind != (float)GroundSurfaceKind.Asphalt) continue; // Preserve stone/gravel/sand approaches.
                    Assert.That(ground.GetComponent<FootstepGround>().ResolveKind(nearest.triangleIndex),
                        Is.EqualTo(FootstepGroundKind.Concrete), "Asphalt footsteps at " + footprint.BuildingId +
                        " " + nearest.point + " slot " + slot + " on " + ground.name);
                    Assert.That(CityWetSurfaceRegistry.TryReadRegistration(ground.GetComponent<Renderer>(), slot,
                        out CityWetSurfaceKind wetKind, out _), Is.True);
                    Assert.That(wetKind, Is.EqualTo(CityWetSurfaceKind.Road));
                    contacts++;
                    asphaltBuildings.Add(footprint.BuildingId);
                }
            }
            Assert.That(contacts, Is.GreaterThan(0));
            Assert.That(asphaltBuildings.Count, Is.GreaterThan(city.Layout.BuildingMasses.Count / 2),
                "Perimeter coverage must include the city fabric, not a single pilot building.");
        }

        private static void AddBuildingPerimeterShots(CityGameRoot city, List<Shot> shots)
        {
            foreach (var district in city.Layout.BuildingMasses.Where(item => item.IsOrdinaryBuilding)
                .GroupBy(item => item.District))
            {
                BuildingLot lot = district.First();
                Vector2[] body = lot.CreateCollisionPolygons()[0];
                Vector2 a = body[0], b = body[1], direction = (b - a).normalized;
                Vector2 outward = new Vector2(direction.y, -direction.x);
                Vector2 midpoint = (a + b) * .5f + outward * .6f;
                float top = lot.Center.y;
                if (CityTerrainSurfacePlan.TrySampleGroundTop(city.Layout, midpoint, out float support, out _)) top = support;
                Vector3 target = new Vector3(midpoint.x, top + .1f, midpoint.y);
                shots.Add(Shot.At("building-perimeter-" + district.Key.ToString().ToLowerInvariant(),
                    target + new Vector3(outward.x * 3f - direction.x * 3f, 1.6f,
                        outward.y * 3f - direction.y * 3f),
                    target + new Vector3(direction.x * 3f, .1f, direction.y * 3f), 72f));
            }
        }
    }
}

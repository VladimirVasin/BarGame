using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

namespace BarPromenade.Tests.PlayMode
{
    public sealed class CityResidentialEntrancesAssetSetup : IPrebuildSetup
    {
        public void Setup()
        {
#if UNITY_EDITOR
            Type.GetType("BarPromenade.Editor.CityBuildingAssetSetup, BarPromenade.Editor", true)
                .GetMethod("BuildOrThrow", Type.EmptyTypes).Invoke(null, null);
            Type.GetType("BarPromenade.Editor.CityMapArtReferenceExport, BarPromenade.Editor", true)
                .GetMethod("ExportCurrent", Type.EmptyTypes).Invoke(null, null);
#endif
        }
    }

    public sealed partial class AreaCaptureFixture
    {
        [UnityTest]
        [Explicit("All residential thresholds, physical access to public pavement, five imported forms and the bounded Home exterior.")]
        [PrebuildSetup(typeof(CityResidentialEntrancesAssetSetup))]
        public IEnumerator CityResidentialEntrances()
        {
            GameSessionState.BeginNewGame();
            Assert.That(GameSessionState.TryStartGameTimeAt(12 * 60), Is.True);
            CityGameRoot city = null;
            PlayerCameraFollow follow = null;
            GameTimeRuntime clock = null;
            bool following = false, clockEnabled = false;
            try
            {
                yield return Capture(SceneIds.City, () =>
                {
                    city = Object.FindAnyObjectByType<CityGameRoot>();
                    return city != null && city.IsInitialized && !CompositionDriver.IsComposing ? city : null;
                }, () =>
                {
                    city.Player.Motor.SetInputEnabled(false);
                    follow = city.Camera.GetComponent<PlayerCameraFollow>();
                    following = follow != null && follow.enabled;
                    if (follow != null) follow.enabled = false;
                    clock = Object.FindAnyObjectByType<GameTimeRuntime>();
                    clockEnabled = clock != null && clock.enabled;
                    if (clock != null) clock.enabled = false;
                    return ResidentialEntranceShots(city);
                });
                AssertResidentialEntrancesAndAccess(city);

                SetChurchRemodelTime(12 * 60);
                yield return CaptureResidentialHomeExterior();
                Debug.Log("CITY RESIDENTIAL ENTRANCES OK: every placed residential body has a measured open threshold, a physically supported clear route to public pavement, all five forms and passive Home reuse.");
            }
            finally
            {
                if (follow != null) follow.enabled = following;
                if (clock != null) clock.enabled = clockEnabled;
                if (city != null) city.Player.Motor.SetInputEnabled(true);
            }
        }

        private static void AssertResidentialEntrancesAndAccess(CityGameRoot city)
        {
            CityLayout layout = city.Layout;
            CityResidentialEntrancePlan plan = CityResidentialEntrancePlan.ForLayout(layout);
            BuildingLot[] homes = layout.BuildingMasses.Where(lot =>
                lot.IsOrdinaryBuilding && lot.District == CityDistrictKind.Residential).ToArray();
            Assert.That(plan.Entrances.Count, Is.EqualTo(homes.Length));
            Assert.That(plan.Entrances.Count, Is.GreaterThan(0));
            CityBuildingAssetRegistry[] models = city.World.Root.GetComponentsInChildren<CityBuildingAssetRegistry>(true);
            var claimed = new HashSet<CityBuildingAssetRegistry>();
            var hits = new RaycastHit[64];
            var overlaps = new Collider[128];
            var failures = new List<string>();
            float[] lateralOffsets = { -.3f, 0f, .3f };
            Physics.SyncTransforms();
            foreach (var entry in plan.Entrances)
            {
                Assert.That(homes.Contains(entry.Lot), Is.True);
                CityBuildingPrototypePose pose = CityBuildingPrototypePlacement.ResolveExpectedCityPose(entry.Lot);
                CityBuildingAssetRegistry model = models.FirstOrDefault(candidate =>
                    candidate.District == CityDistrictKind.Residential &&
                    Vector3.Distance(candidate.transform.position, pose.Position) < .01f);
                Assert.That(model, Is.Not.Null, "Missing placed residential body at " + entry.Lot.Cell);
                Assert.That(claimed.Add(model), Is.True, "Each physical body owns its own entrance.");
                Assert.That(model.HasEntrance, Is.True, model.StableId);
                Assert.That(Vector3.Distance(model.transform.TransformPoint(model.LocalEntranceThreshold), entry.Threshold),
                    Is.LessThan(.01f), "The imported entrance must agree with its world path.");
                Assert.That(Vector3.Dot(model.transform.TransformDirection(model.LocalEntranceOutward).normalized,
                    entry.Outward.normalized), Is.GreaterThan(.999f));
                Assert.That(entry.Points.Count, Is.GreaterThanOrEqualTo(2));
                Assert.That(Vector3.Distance(entry.Points[0], entry.Threshold), Is.LessThan(.01f));
                Assert.That(entry.Paving, Is.Not.Empty);

                // The same person can step behind the facade and back out;
                // cutting only the visible facade leaves a solid mass/skirt here.
                for (float depth = .2f; depth <= 1.3f; depth += .35f)
                    ProbeResidentialFootstep(entry.Threshold - entry.Outward * depth, city, hits, overlaps,
                        failures, entry.Lot.Cell + " vestibule", requireNavigation: true);
                for (int leg = 1; leg < entry.Points.Count; leg++)
                {
                    Vector3 a = entry.Points[leg - 1], b = entry.Points[leg];
                    int steps = Mathf.Max(1, Mathf.CeilToInt(Vector3.Distance(a, b) / .4f));
                    Vector3 tangent = b - a; tangent.y = 0f;
                    Vector3 side = Vector3.Cross(Vector3.up, tangent.normalized);
                    for (int step = 0; step <= steps; step++)
                    {
                        Vector3 point = Vector3.Lerp(a, b, step / (float)steps);
                        foreach (float offset in lateralOffsets)
                        {
                            Vector3 foot = point + side * offset;
                            if (plan.TrySampleTop(new Vector2(foot.x, foot.z), out float top)) foot.y = top;
                            else AddResidentialFailure(failures, entry.Lot.Cell + " missing paved support at " + foot);
                            ProbeResidentialFootstep(foot, city, hits, overlaps, failures,
                                entry.Lot.Cell + " route leg " + leg, requireNavigation: true);
                        }
                    }
                }
            }
            Assert.That(failures, Is.Empty, string.Join("\n", failures));
        }

        private static void ProbeResidentialFootstep(Vector3 foot, CityGameRoot city, RaycastHit[] hits,
            Collider[] overlaps, List<string> failures, string context, bool requireNavigation)
        {
            if (failures.Count >= 24) return;
            int count = Physics.RaycastNonAlloc(foot + Vector3.up * .7f, Vector3.down, hits, 1.4f,
                Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);
            Assert.That(count, Is.LessThan(hits.Length), "Residential ground probe buffer must not truncate.");
            bool supported = false;
            float nearest = float.PositiveInfinity;
            for (int i = 0; i < count; i++)
            {
                RaycastHit hit = hits[i];
                if (!hit.transform.IsChildOf(city.World.Root.transform) || hit.normal.y < .7f) continue;
                float error = Mathf.Abs(hit.point.y - foot.y);
                nearest = Mathf.Min(nearest, error);
                if (error <= .035f) supported = true;
            }
            if (!supported) AddResidentialFailure(failures,
                context + " no physical floor at " + foot.ToString("F3") + "; closest height error=" + nearest);
            if (requireNavigation && !city.World.WalkableArea.Contains(foot, .25f))
                AddResidentialFailure(failures, context + " missing walkable area at " + foot.ToString("F3"));

            const float radius = .27f;
            int solidCount = Physics.OverlapCapsuleNonAlloc(foot + Vector3.up * (.3f + radius),
                foot + Vector3.up * (1.75f - radius), radius, overlaps,
                Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);
            Assert.That(solidCount, Is.LessThan(overlaps.Length), "Residential clearance probe must not truncate.");
            for (int i = 0; i < solidCount; i++)
            {
                Collider obstacle = overlaps[i];
                if (obstacle.transform.IsChildOf(city.Player.GameObject.transform) ||
                    obstacle.GetComponentInParent<DefaultNpcAppearance>() != null ||
                    obstacle.GetComponentInParent<CityPedestrianActor>() != null) continue;
                AddResidentialFailure(failures, context + " blocked by " + obstacle.name + " at " + foot.ToString("F3"));
            }
        }

        private static void AddResidentialFailure(List<string> failures, string message)
        {
            if (failures.Count < 24) failures.Add(message);
        }

        private static Shot[] ResidentialEntranceShots(CityGameRoot city)
        {
            var shots = new List<Shot>();
            CityResidentialEntrancePlan plan = CityResidentialEntrancePlan.ForLayout(city.Layout);
            for (int variant = 0; variant < CityBuildingAssetProvider.GetVariantCount(CityDistrictKind.Residential); variant++)
            {
                var entry = plan.Entrances.FirstOrDefault(candidate => candidate.Lot.BuildingVariant == variant);
                Assert.That(entry, Is.Not.Null, "The default City must expose residential form " + variant);
                Vector3 side = Vector3.Cross(Vector3.up, entry.Outward);
                Add("residential-entry-form-" + (variant + 1), entry.Threshold + entry.Outward * 3.8f +
                    side * 1.2f + Vector3.up * EyeHeight, entry.Threshold - entry.Outward * .7f + Vector3.up * 1.15f);
            }
            var longest = plan.Entrances.OrderByDescending(entry => entry.Points.Count).First();
            Vector3 end = longest.Points[longest.Points.Count - 1];
            Vector3 towardEntry = longest.Threshold - end; towardEntry.y = 0f;
            if (towardEntry.sqrMagnitude < .01f) towardEntry = -longest.Outward;
            Add("residential-entry-public-connection", end - towardEntry.normalized * 3f + Vector3.up * EyeHeight,
                longest.Threshold + Vector3.up * .8f, 76f);
            var night = plan.Entrances[0];
            Add("residential-entry-night", night.Threshold + night.Outward * 4f + Vector3.up * EyeHeight,
                night.Threshold - night.Outward * .7f + Vector3.up * 1.15f, night: true);
            return shots.ToArray();

            void Add(string name, Vector3 eye, Vector3 target, float fov = 66f, bool night = false)
            {
                int frames = 0;
                Shot pose = Shot.At(name, eye, target, fov);
                shots.Add(Shot.At(name, eye, target, fov, readyWhen: () =>
                {
                    if (frames++ == 0)
                    {
                        SetChurchRemodelTime(night ? 23 * 60 : 12 * 60);
                        city.DayNight.ApplyCurrentTime(true);
                        MoveCityLightingObserver(city, pose);
                    }
                    return frames > SettleFrames;
                }));
            }
        }

        private static IEnumerator CaptureResidentialHomeExterior()
        {
            GameSessionState.EnterHome();
            yield return SceneManager.LoadSceneAsync(SceneIds.HomeInterior, LoadSceneMode.Single);
            HomeInteriorRoot home = null;
            float deadline = Time.realtimeSinceStartup + TimeoutSeconds;
            while (Time.realtimeSinceStartup < deadline)
            {
                home = Object.FindAnyObjectByType<HomeInteriorRoot>();
                if (home != null && home.IsInitialized) break;
                yield return null;
            }
            Assert.That(home != null && home.IsInitialized, Is.True);
            HomeExteriorContextPlan context = home.ExteriorContext;
            CityResidentialEntrancePlan plan = CityResidentialEntrancePlan.ForLayout(context.Layout);
            var models = home.ExteriorView.GetComponentsInChildren<CityBuildingAssetRegistry>(true)
                .Where(model => model.District == CityDistrictKind.Residential).ToArray();
            Assert.That(models, Is.Not.Empty);
            foreach (CityBuildingAssetRegistry model in models)
            {
                Assert.That(model.HasEntrance, Is.True);
                Vector3 localThreshold = model.transform.TransformPoint(model.LocalEntranceThreshold);
                Assert.That(plan.Entrances.Any(entry => Vector3.Distance(localThreshold,
                    PlayerHomeBalconyGeometry.ToHomeLocal(context.PlayerHome, entry.Threshold)) < .01f), Is.True,
                    "The Home view must preserve the exact City's entry pose.");
            }
            foreach (Collider collider in home.ExteriorView.GetComponentsInChildren<Collider>(true))
                Assert.That(collider.enabled, Is.False, "The bounded Home exterior remains passive.");
            Assert.That(home.ExteriorView.Find(CityResidentialEntranceWorldBuilder.RootName), Is.Not.Null);
            Transform ground = home.ExteriorView.Find("City Ground");
            Assert.That(ground, Is.Not.Null);
            MeshFilter[] sectors = ground.GetComponentsInChildren<MeshFilter>(true);
            int checkedSurfaces = 0;
            foreach (var entry in plan.Entrances)
            {
                if (!context.NearbyLots.Contains(entry.Lot) || entry.Points.Count < 2) continue;
                // Sample beyond the mouth so the shared ground must carry the
                // paving, rather than accidentally proving the model's floor.
                Vector3 cityPoint = Vector3.Lerp(entry.Points[0], entry.Points[1], .7f);
                Vector3 localPoint = PlayerHomeBalconyGeometry.ToHomeLocal(context.PlayerHome, cityPoint);
                if (localPoint.x < HomeExteriorViewBuilder.ExteriorMinimumX + .05f) continue;
                Assert.That(HasRenderedResidentialGround(sectors, localPoint), Is.True,
                    "The Home ground sectors must retain the City's graded approach at " + localPoint.ToString("F3"));
                checkedSurfaces++;
            }
            Assert.That(checkedSurfaces, Is.GreaterThan(0));
            Rect activation = home.BalconyLayout.BalconyCameraActivationBounds;
            home.Player.Motor.Teleport(new Vector3(activation.center.x, home.Layout.PlayerSpawn.y, activation.center.y));
            for (int frame = 0; frame < SettleFrames; frame++) yield return null;
            Assert.That(home.ExteriorAtmosphere.IsBalconyVisibilityActive, Is.True);
            Camera camera = home.CameraFollow.GetComponent<Camera>();
            CaptureCurrentCamera(camera, SceneIds.HomeInterior, "residential-entry-home-balcony");
        }

        private static bool HasRenderedResidentialGround(IEnumerable<MeshFilter> sectors, Vector3 point)
        {
            foreach (MeshFilter filter in sectors)
            {
                Renderer renderer = filter.GetComponent<Renderer>();
                if (renderer == null || !renderer.enabled || !renderer.gameObject.activeInHierarchy) continue;
                Bounds bounds = renderer.bounds;
                if (point.x < bounds.min.x - .01f || point.x > bounds.max.x + .01f ||
                    point.z < bounds.min.z - .01f || point.z > bounds.max.z + .01f) continue;
                Mesh mesh = filter.sharedMesh;
                Vector3[] vertices = mesh.vertices;
                int[] triangles = mesh.triangles;
                Vector3 local = filter.transform.InverseTransformPoint(point);
                for (int i = 0; i < triangles.Length; i += 3)
                {
                    Vector3 a = vertices[triangles[i]], b = vertices[triangles[i + 1]], c = vertices[triangles[i + 2]];
                    Vector2 ab = new Vector2(b.x - a.x, b.z - a.z), ac = new Vector2(c.x - a.x, c.z - a.z);
                    float determinant = ab.x * ac.y - ab.y * ac.x;
                    if (Mathf.Abs(determinant) < .000001f) continue;
                    Vector2 ap = new Vector2(local.x - a.x, local.z - a.z);
                    float u = (ap.x * ac.y - ap.y * ac.x) / determinant;
                    float v = (ab.x * ap.y - ab.y * ap.x) / determinant;
                    if (u < -.0001f || v < -.0001f || u + v > 1.0001f) continue;
                    float top = a.y + u * (b.y - a.y) + v * (c.y - a.y);
                    if (Mathf.Abs(top - local.y) < .035f) return true;
                }
            }
            return false;
        }
    }
}

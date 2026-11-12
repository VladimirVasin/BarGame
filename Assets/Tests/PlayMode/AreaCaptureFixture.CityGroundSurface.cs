using System.Collections;
using System.Collections.Generic;
using System.Linq;
using BarPromenade.Rendering;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

namespace BarPromenade.Tests.PlayMode
{
    public sealed partial class AreaCaptureFixture
    {
        [UnityTest, Explicit("Review the reported summit contacts, west gable sign and pigeon enclosure in the live City.")]
        public IEnumerator CitySummitStreetRepairs()
        {
            GameSessionState.BeginNewGame();
            Assert.That(GameSessionState.TryStartGameTimeAt(19 * 60), Is.True);
            CityGameRoot city = null;
            yield return Capture(SceneIds.City, () =>
            {
                city = Object.FindAnyObjectByType<CityGameRoot>();
                return city != null && city.IsInitialized ? city : null;
            }, () =>
            {
                FreezeLightingPolish(city.Player.Motor);
                var housing = CityHillsideNeighborhoodPlan.ForLayout(city.Layout);
                var eastHouse = housing.AllBuildings.Single(house => house.Lot.Cell == new Vector2Int(21, -7));
                var eastRoad = CityHillsideStreetPlan.ForLayout(city.Layout).Routes
                    .Single(road => road.Bank == CityTerraceTransportBank.East);
                float eastSidewalk = eastRoad.GetLaneZ(CityHillsideStreetRoute.LaneCount - 1) +
                    CityHillsideStreetRoute.OuterHalfWidth;
                Assert.That(eastHouse.FoundationBounds.yMin - eastSidewalk, Is.EqualTo(.65f).Within(.002f));

                var descent = CityPawnshopDescentPlan.ForLayout(city.Layout);
                Assert.That(descent.SummitApronPaving.Min(point => point.y),
                    Is.EqualTo(descent.SummitPavementNorthEdge).Within(.002f));
                Assert.That(descent.SummitApronPaving.Max(point => point.y),
                    Is.GreaterThan(descent.SummitPavementNorthEdge + 1f));
                var apron = city.World.Root.GetComponentsInChildren<MeshCollider>()
                    .Single(collider => collider.name == "Summit stair apron paving");
                float apronSouthEdge = apron.sharedMesh.vertices.Min(vertex => apron.transform.TransformPoint(vertex).z);
                Assert.That(apronSouthEdge, Is.EqualTo(descent.SummitPavementNorthEdge).Within(.002f));

                var home = city.World.Root.GetComponentInChildren<PlayerHomeExteriorAssetRegistry>();
                Assert.That(home.TryGetAnchor("entrance_lamp_ceiling", out Transform ceiling), Is.True);
                var lamp = city.World.Root.GetComponentsInChildren<Renderer>()
                    .Single(part => part.name == "Home Entrance Lamp");
                Assert.That(Vector3.Distance(lamp.bounds.center, ceiling.position), Is.LessThan(.16f));
                var gallery = home.Parts.Single(part => part.SourceName == "Two Storey Gallery Timber").Renderer;
                Assert.That(gallery.bounds.min.y, Is.GreaterThan(city.Layout.PlayerHome.DoorPosition.y + 4.6f));

                var sign = city.World.Root.GetComponentsInChildren<Transform>()
                    .Single(part => part.name == "Ridge fish cannery advertisement");
                var print = sign.GetComponentsInChildren<Renderer>().Single(part => part.name == "PrintedFace");
                Assert.That(print.bounds.size.y, Is.EqualTo(10.375f).Within(.01f));
                Assert.That(sign.forward.x, Is.EqualTo(1f).Within(.01f));
#if UNITY_EDITOR
                AssertSignReadsFromStreet(sign, print);
#endif
                foreach (var fixture in sign.GetComponentsInChildren<Transform>().Where(part =>
                    part.name == "IndustrialTaskLight_RidgeFishSign"))
                    Assert.That(Vector3.Dot(fixture.forward, sign.forward), Is.LessThan(-.2f));
                Assert.That(sign.GetComponentsInChildren<Transform>().Count(part =>
                    part.name == "IndustrialTaskLight_RidgeFishSign"), Is.EqualTo(2));

                var grass = city.World.Root.GetComponentInChildren<CityGrassField>();
                var yard = CityPigeonLoftPlan.ForLayout(city.Layout).Courtyard;
                int outsidePlants = 0;
                for (float z = yard.Bounds.yMin - 1f; z < yard.Bounds.yMax + 1f; z += .24f)
                for (float x = yard.Bounds.xMin - 1f; x < yard.Bounds.xMax; x += .24f)
                {
                    var point = new Vector2(x, z);
                    bool supportsGrass = grass.Surface.TrySample(point, out _, out _, out _);
                    if (CityRoadPolygon.Contains(yard.Enclosure, point))
                        Assert.That(supportsGrass, Is.False, "Grass inside the surveyed fence at " + point);
                    else if (supportsGrass)
                        outsidePlants++;
                }
                Assert.That(outsidePlants, Is.GreaterThan(0));

                var guardedHouse = housing.AllBuildings.Single(house => house.Lot.Cell == CityHillsideBuilding.GuardedEastHouseCell);
                AssertRidgeEntrySupports(city);
                Assert.That(city.World.Root.GetComponentsInChildren<MeshCollider>()
                    .Count(collider => collider.name == "East stair soil cut"), Is.EqualTo(1));
                float approachX = guardedHouse.ApproachBounds.center.x;
                for (float z = guardedHouse.StreetContactWorld.z; z <= guardedHouse.ThresholdWorld.z; z += .25f)
                {
                    Assert.That(guardedHouse.TrySampleApproach(new Vector2(approachX, z), out float y), Is.True);
                    Assert.That(city.World.WalkableArea.Contains(new Vector3(approachX, y, z), .32f),
                        Is.True, "The fence leaves a clear approach at " + z);
                }

                Vector3 stairEye = new(-48.2f, descent.UpperDock.y + 1.67f, -343.3f);
                Vector3 yardEye = new(-154.0f, yard.GateCenter.y + 1.72f, -336.5f);
                return new[]
                {
                    Shot.At("summit-repairs-home", city.Layout.PlayerHome.DoorPosition + new Vector3(-1.0f, 1.7f, 4.5f),
                        ceiling.position - Vector3.up * .5f, 66f),
                    Shot.At("summit-repairs-stairs", stairEye, descent.UpperDock + new Vector3(1.5f, -.75f, 2f), 68f),
                    Shot.At("summit-repairs-sign", print.bounds.center + sign.forward * 16f - new Vector3(0f, 4f, 2f),
                        print.bounds.center, 52f),
                    Shot.At("summit-repairs-yard", yardEye, yard.Arrival + new Vector3(4f, .35f, 1f), 64f,
                        readyWhen: () => WarmGrassShot(grass, city.Camera, yardEye, yard.Arrival)),
                    Shot.At("summit-repairs-east", new Vector3(138.6f, 39.0f, -342.8f),
                        new Vector3(eastHouse.Lot.Center.x, 37.4f, eastHouse.FoundationBounds.yMin), 62f),
                    Shot.At("summit-repairs-east-foundation", new Vector3(122.5f, 36.9f, -329.8f),
                        new Vector3(119.2f, 36.6f, -335f), 68f),
                    Shot.At("summit-repairs-east-entry-rail", new Vector3(111.9f, 39.8f, -339.5f),
                        guardedHouse.ThresholdWorld + Vector3.up * .45f, 70f)
                };
            });
        }

        private static void AssertRidgeEntrySupports(CityGameRoot city)
        {
            var guards = city.World.Root.GetComponentsInChildren<Transform>()
                .Where(part => part.name == "Ridge entry rail").ToDictionary(part => part.parent.name);
            var houses = CityHillsideNeighborhoodPlan.ForLayout(city.Layout).AllBuildings;
            var terrain = CityHillsideGroundPlan.ForLayout(city.Layout);
            Assert.That(guards.Count, Is.EqualTo(houses.Count(house => !house.Lot.IsPlayerHome)));
            foreach (CityHillsideBuilding house in houses)
            {
                if (house.Lot.IsPlayerHome) continue;
                string id = "Building " + house.Lot.Cell;
                Assert.That(guards.TryGetValue(id, out Transform guard), Is.True, id);
                Assert.That(guard.GetComponentsInChildren<BoxCollider>().Length,
                    Is.GreaterThanOrEqualTo(house.EntryRail.Count - 1), id);
                float footing = guard.parent.GetComponentsInChildren<Renderer>()
                    .Where(renderer => renderer.name.StartsWith(CityBuildingPrototypeWorldBuilder.FoundationObjectName))
                    .Min(renderer => renderer.bounds.min.y);
                Assert.That(house.ThresholdWorld.y - footing,
                    Is.EqualTo(CityHillsideNeighborhoodWorld.FoundationDepth(city.Layout, house)).Within(.015f), id);
                foreach (Vector2 corner in CityRoadPolygon.Rectangle(house.FoundationBounds))
                    if (terrain.TrySampleTop(corner, out float ground))
                        Assert.That(ground - footing, Is.GreaterThan(.12f), id + " footing under surveyed ground");

                float inward = Mathf.Sign(house.DoorWorld.z - house.StreetContactWorld.z);
                foreach (Vector3 point in house.EntryRail)
                    Assert.That((point.z - house.PavementEdgeZ) * inward, Is.GreaterThan(.08f), id + " rail behind sidewalk");
                float start = house.StreetContactWorld.z, end = house.CourtBounds.center.y;
                int steps = Mathf.CeilToInt(Mathf.Abs(end - start) / .25f);
                for (int step = 0; step <= steps; step++)
                {
                    var point = new Vector2(house.StreetContactWorld.x, Mathf.Lerp(start, end, step / (float)steps));
                    Assert.That(house.TrySampleApproach(point, out float y), Is.True, id);
                    Assert.That(city.World.WalkableArea.Contains(new Vector3(point.x, y, point.y), .32f), Is.True,
                        id + " clear route from sidewalk to porch at " + point);
                }
            }
        }

#if UNITY_EDITOR
        private static void AssertSignReadsFromStreet(Transform sign, Renderer print)
        {
            var mesh = print.GetComponent<MeshFilter>().sharedMesh;
            using var data = UnityEditor.MeshUtility.AcquireReadOnlyMeshData(mesh);
            using var vertices = new Unity.Collections.NativeArray<Vector3>(mesh.vertexCount, Unity.Collections.Allocator.Temp);
            using var uv = new Unity.Collections.NativeArray<Vector2>(mesh.vertexCount, Unity.Collections.Allocator.Temp);
            data[0].GetVertices(vertices);
            data[0].GetUVs(0, uv);
            Vector3 streetRight = Vector3.Cross(sign.up, -sign.forward);
            int left = 0, right = 0, bottom = 0, top = 0;
            for (int i = 1; i < vertices.Length; i++)
            {
                Vector3 point = print.transform.TransformPoint(vertices[i]);
                float x = Vector3.Dot(point, streetRight);
                if (x < Vector3.Dot(print.transform.TransformPoint(vertices[left]), streetRight)) left = i;
                if (x > Vector3.Dot(print.transform.TransformPoint(vertices[right]), streetRight)) right = i;
                if (point.y < print.transform.TransformPoint(vertices[bottom]).y) bottom = i;
                if (point.y > print.transform.TransformPoint(vertices[top]).y) top = i;
            }
            Assert.That(uv[right].x - uv[left].x, Is.GreaterThan(.99f), "U grows to the viewer's right on the imported sign.");
            Assert.That(uv[top].y - uv[bottom].y, Is.GreaterThan(.99f), "V grows upward on the imported sign.");
        }
#endif

        [UnityTest, Explicit("Ridge pavement meets the local hillside in City and the retained Home view.")]
        public IEnumerator CityRidgeRoadShoulders()
        {
            GameSessionState.BeginNewGame();
            Assert.That(GameSessionState.TryStartGameTimeAt(12 * 60), Is.True);
            const float westLane = -294f;
            var reportedEdge = new Vector2(-92.8f, westLane + CityHillsideStreetRoute.OuterHalfWidth);
            var retainedEdge = new Vector2(-62f, -341f);
            var surveyIssues = new List<string>();
            float retainedTop = 0f;
            CityGameRoot city = null;
            yield return Capture(SceneIds.City, () =>
            {
                city = Object.FindAnyObjectByType<CityGameRoot>();
                return city != null && city.IsInitialized ? city : null;
            }, () =>
            {
                FreezeLightingPolish(city.Player.Motor);
                var grids = CityHillsideGroundGrid.ForLayout(city.Layout);
                var west = grids.Single(grid => grid.Road.Bank == CityTerraceTransportBank.West);
                var east = grids.Single(grid => grid.Road.Bank == CityTerraceTransportBank.East);
                Assert.That(west.Road.TrySampleTop(reportedEdge, out float paved, out var kind), Is.True);
                Assert.That(kind, Is.EqualTo(CityStreetSupportKind.Sidewalk));
                Assert.That(RidgeShoulderStep(west, reportedEdge, Vector2.up, paved), Is.LessThan(.205f),
                    "At X -92.8 the verge must descend without a trench and a near-vertical return.");
                Assert.That(west.TryHeight(reportedEdge + Vector2.up * 1.5f, out float shoulder), Is.True);
                Assert.That(shoulder, Is.EqualTo(28.91f).Within(.12f),
                    "The surveyed outside height is local, between the reported pavement and the next lane.");

                Physics.SyncTransforms();
                var ground = CityHillsideGroundPlan.ForLayout(city.Layout);
                var bankMeshes = city.World.Root.GetComponentsInChildren<MeshCollider>()
                    .Where(mesh => mesh.name == "Terraced ground and courts").ToArray();
                var map = new CityMapCityTeleportGround(city.Layout);
                Vector2 landing = reportedEdge + Vector2.up * 1.5f;
                Assert.That(map.TryResolveStandingPosition(landing, out var arrival), Is.True);
                Assert.That(Vector2.Distance(landing, new Vector2(arrival.x, arrival.z)), Is.LessThan(.04f));
                Assert.That(arrival.y, Is.EqualTo(shoulder + PlayerFactory.GroundedRootOffset).Within(.04f));
                Assert.That(city.World.WalkableArea.Contains(arrival, city.Player.Motor.GetComponent<CharacterController>().radius), Is.True);
                Debug.Log($"Ridge road shoulders map illustrated: {CityMapArtResources.TryGet(city.Map, out _, out _)}.");

                var housing = CityHillsideNeighborhoodPlan.ForLayout(city.Layout).AllBuildings;
                var cuts = CityPawnshopBenchTerracePlan.ForLayout(city.Layout).Footprints
                    .Concat(CityPawnshopDescentPlan.ForLayout(city.Layout).GroundCuts).ToArray();
                foreach (var grid in grids)
                for (int lane = 0; lane < CityHillsideStreetRoute.LaneCount; lane++)
                {
                    int surveyed = 0;
                    for (int station = 1; station <= 7; station++)
                    foreach (float side in new[] { -1f, 1f })
                    {
                        float x = Mathf.Lerp(grid.Road.LaneMinX, grid.Road.LaneMaxX, .1f + station * .1f);
                        Vector2 outward = Vector2.up * side;
                        var edge = new Vector2(x, grid.Road.GetLaneZ(lane) + side * CityHillsideStreetRoute.OuterHalfWidth);
                        if (!grid.Road.TrySampleTop(edge, out float curb, out _) ||
                            grid.Road.TrySampleTop(edge + outward * .05f, out _, out _)) continue;
                        var probe = edge + outward * 1.5f;
                        // Walls and the authored stair/landing cuts have their own geometry.
                        if (housing.Any(house => house.FoundationBounds.Contains(probe)) ||
                            cuts.Any(cut => CityRoadPolygon.Contains(cut, probe)) ||
                            !grid.TryHeight(edge + outward * 2.25f, out _)) continue;
                        float maximumStep = RidgeShoulderStep(grid, edge, outward, curb);
                        if (float.IsPositiveInfinity(maximumStep)) continue;
                        if (maximumStep >= .46f)
                            surveyIssues.Add($"{grid.Road.Bank} lane {lane + 1} at {edge:F2} facing {side}: {maximumStep:F3} m step");
                        Assert.That(ground.TrySampleTop(probe, out float top), Is.True);
                        var from = new Vector3(probe.x, top + .35f, probe.y);
                        Assert.That(bankMeshes.Any(mesh => mesh.Raycast(new Ray(from, Vector3.down), out var hit, .70f) &&
                            Mathf.Abs(hit.point.y - top) < .015f), Is.True, $"Ground mesh contact at {probe:F2}.");
                        surveyed++;
                    }
                    Assert.That(surveyed, Is.GreaterThanOrEqualTo(4), $"{grid.Road.Bank} lane {lane + 1} has testable banks.");
                }
                var benches = CityHillsideBenchPocketPlan.ForLayout(city.Layout);
                foreach (string id in benches.UnconnectedBenchIds)
                    surveyIssues.Add("Missing paved approach at " + id);

                Assert.That(west.TryHeight(retainedEdge, out retainedTop), Is.True);
                var grass = city.World.Root.GetComponentInChildren<CityGrassField>();
                Assert.That(grass.Surface.TrySample(reportedEdge - Vector2.up * .35f, out _, out _, out _), Is.False,
                    "The public sidewalk stays clear of grass.");
                return new[] { ShoulderShot(west, reportedEdge), ShoulderShot(east, new Vector2(117f, reportedEdge.y)) };

                Shot ShoulderShot(CityHillsideGroundGrid grid, Vector2 edge)
                {
                    Assert.That(grid.Road.TrySampleTop(edge, out float top, out _), Is.True);
                    Vector3 seam = new(edge.x, top, edge.y);
                    Vector3 eye = seam + new Vector3(2f, 1.78f, -.80f);
                    Vector3 aim = seam + new Vector3(-6f, -.4f, 1.25f);
                    return Shot.At("ridge-road-shoulder-" + grid.Road.Bank, eye, aim, 68f,
                        readyWhen: () => WarmGrassShot(grass, city.Camera, eye, aim));
                }
            });

            GameSessionState.PrepareHomeArrival(HomeArrivalKind.Normal);
            GameSessionState.EnterHome();
            HomeInteriorRoot home = null;
            yield return Capture(SceneIds.HomeInterior, () =>
            {
                home = Object.FindAnyObjectByType<HomeInteriorRoot>();
                return home != null && home.IsInitialized ? home : null;
            }, () =>
            {
                FreezeLightingPolish(home.Player.Motor);
                var west = CityHillsideGroundGrid.ForLayout(home.ExteriorContext.Layout)
                    .Single(grid => grid.Road.Bank == CityTerraceTransportBank.West);
                Assert.That(west.TryHeight(retainedEdge, out float height), Is.True);
                Assert.That(height, Is.EqualTo(retainedTop).Within(.005f));
                var frame = home.ExteriorView.Find("Summit street from home");
                Assert.That(frame, Is.Not.Null);
                Vector3 seam = new(retainedEdge.x, height, retainedEdge.y);
                Vector3 eye = frame.TransformPoint(seam + new Vector3(2f, 1.80f, -.90f));
                Vector3 aim = frame.TransformPoint(seam + new Vector3(-6f, -.30f, .90f));
                var grass = home.ExteriorView.GetComponentInChildren<CityGrassField>();
                return new[] { Shot.At("ridge-road-shoulder-Home", eye, aim, 67f,
                    readyWhen: () => WarmGrassShot(grass, Camera.main, eye, aim)) };
            });
            Assert.That(surveyIssues, Is.Empty, string.Join("\n", surveyIssues));
        }

        private static float RidgeShoulderStep(CityHillsideGroundGrid grid, Vector2 edge, Vector2 outward, float paved)
        {
            float step = 0f, previous = paved;
            for (int index = 0; index <= 9; index++)
            {
                if (!grid.TryHeight(edge + outward * (index * .25f), out float top)) return float.PositiveInfinity;
                step = Mathf.Max(step, Mathf.Abs(top - previous));
                previous = top;
            }
            return step;
        }

        [UnityTest]
        [Explicit("City coating joints A/B and local rebuilds, with the real Home exterior.")]
        public IEnumerator CityGroundJointPilot()
        {
            GameSessionState.BeginNewGame();
            GameSessionState.TryStartGameTimeFromWake();
            GameSessionState.AdvanceGameTime((float)((12d * 60d - GameSessionState.GameTimeOfDayMinutes) /
                GameTimeState.GameMinutesPerRealSecond));
            CityGameRoot city = null;
            int preview = Shader.PropertyToID("_CityJointPreviewOff");
            Shot measured = default;
            int originalContacts = 0;
            bool previousLens = GraphicsEffectsSettings.IntoxicationLensFxEnabled;
            try
            {
                yield return Capture(SceneIds.City, () =>
                {
                    city = Object.FindAnyObjectByType<CityGameRoot>();
                    return city != null && city.IsInitialized ? city : null;
                }, () =>
                {
                    CityGroundSurfaceSystem system = city.World.Root.GetComponent<CityGroundSurfaceSystem>();
                    Assert.That(CityGroundJointPalette.Supported, Is.True, "The desktop target needs the actual seam shader.");
                    Assert.That(system.CoatingJointCount, Is.GreaterThan(0));
                    Assert.That(system.CoatingJointBytes, Is.InRange(1, 16 * 1024 * 1024));
                    originalContacts = system.CoatingJointCount;
                    city.DayNight.ApplyCurrentTime(true);
                    Physics.SyncTransforms();
                    CityResidentialCourtyardDescriptor court = CityResidentialCourtyardPlan.ForLayout(city.Layout).Courts
                        .First(candidate => candidate.Surfaces.Any(surface => surface.Kind == CityResidentialCourtyardSurfaceKind.Soil));
                    CityResidentialCourtyardSurfaceDescriptor strip = court.Surfaces
                        .Where(surface => surface.Kind == CityResidentialCourtyardSurfaceKind.Soil)
                        .OrderBy(surface => (surface.Footprint.center - new Vector2(court.CommonCenter.x, court.CommonCenter.z)).sqrMagnitude)
                        .First();
                    Vector2 edge = (strip.Polygon[0] + strip.Polygon[1]) * .5f;
                    Vector3 at = new Vector3(edge.x, court.CommonCenter.y, edge.y);
                    if (TryCommonGroundHit(system, at, out RaycastHit support)) at = support.point;
                    Vector3 from = Vector3.MoveTowards(at, court.CommonCenter, 3.0f) + Vector3.up * EyeHeight;
                    if ((from - at).sqrMagnitude < 1f) from += court.GroupForward * 2f;
                    measured = Shot.At("joint draw timing", from, at, 66f);
                    var shots = new List<Shot>();
                    foreach (float wet in new[] { 0f, 1f })
                        foreach (bool enabled in new[] { false, true })
                        {
                            string label = (wet > 0f ? "joint wet" : "joint dry") + (enabled ? " enabled" : " baseline");
                            shots.Add(Shot.At(CityLightingFrame(label), from, at, 66f, readyWhen: () =>
                            {
                                CityWetSurfaceRegistry.SetImmediate(wet);
                                Shader.SetGlobalFloat(preview, enabled ? 0f : 1f);
                                return true;
                            }));
                        }
                    shots.Add(CityJointCurbShot(city, system));
                    AddCityJointAuditShots(city, shots);
                    AssertCityStreetJointCoverage(city, system);
                    shots.Add(Shot.At("ground-joints-court-maximum-lens", from, at, 66f, readyWhen: () =>
                    {
                        GraphicsEffectsSettings.IntoxicationLensFxEnabled = true;
                        IntoxicationRenderState.Set(IntoxicationStageRules.Evaluate(100), 1.25f, 0f, Vector2.zero, Vector3.zero);
                        return true;
                    }));
                    bool nightSet = false;
                    shots.Add(Shot.At("ground-joints-court-night", from, at, 66f, readyWhen: () =>
                    {
                        if (nightSet) return true;
                        IntoxicationRenderState.Clear();
                        GameSessionState.AdvanceGameTime((float)((22d * 60d - GameSessionState.GameTimeOfDayMinutes) /
                            GameTimeState.GameMinutesPerRealSecond));
                        city.DayNight.ApplyCurrentTime(true);
                        nightSet = true;
                        return false;
                    }));
                    Debug.Log($"Ground joints pilot: {system.CoatingJointCount} contacts, " +
                        $"{system.CoatingJointBytes} buffer bytes, max {system.MaximumCoatingCandidates} candidates, " +
                        $"{system.CoatingJointBuildMilliseconds:F1} ms joint build, " +
                        $"{system.BuildMilliseconds:F1} ms ground composition.");
                    return shots.ToArray();
                });
                Assert.That(CountCityLightingDifference("joint dry baseline", "joint dry enabled"), Is.GreaterThan(24));
                Assert.That(CountCityLightingDifference("joint wet baseline", "joint wet enabled"), Is.GreaterThan(24));
                Assert.That(CountCityLightingDifference("garden joint baseline", "garden joint enabled"), Is.GreaterThan(24),
                    "The east seam must no longer remove the layered material from the ordinary church lawn.");
                MeasureCityJointDraw(city.Camera, measured, preview);
                yield return MeasureCityJointFrames(city.Camera, measured, preview);
                AssertCommonGroundExcavation(city);
                yield return AssertCommonGroundSand(city);
                yield return WalkCityJointRoutes(city);
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
                    var ground = home.ExteriorView.GetComponent<CityGroundSurfaceSystem>();
                    Assert.That(ground.CoatingJointBytes, Is.GreaterThan(0),
                        "The real retained balcony view must bind the same City coating mechanism.");
                    return new[] { Shot.At("ground-joints-home-balcony", eye,
                        new Vector3(eye.x + 13f, home.BalconyLayout.StreetGroundY, eye.z), 70f) };
                });
                yield return SceneManager.LoadSceneAsync(SceneIds.City, LoadSceneMode.Single);
                CityGameRoot returned = Object.FindAnyObjectByType<CityGameRoot>();
                Assert.That(returned, Is.Not.Null);
                yield return new WaitUntil(() => returned.IsInitialized);
                var returnedGround = returned.World.Root.GetComponent<CityGroundSurfaceSystem>();
                Assert.That(returnedGround.CoatingJointCount, Is.EqualTo(originalContacts));
                Assert.That(returnedGround.CoatingJointBytes, Is.GreaterThan(0));
#if UNITY_EDITOR
                double withField = returnedGround.BuildMilliseconds;
                int withFieldTriangles = returnedGround.SurfaceTriangleCount;
                CityGroundSurfaceSystem.BypassJointBuildForMeasurement = true;
                yield return SceneManager.LoadSceneAsync(SceneIds.City, LoadSceneMode.Single);
                CityGameRoot baseline = Object.FindAnyObjectByType<CityGameRoot>();
                Assert.That(baseline, Is.Not.Null);
                yield return new WaitUntil(() => baseline.IsInitialized);
                var baselineGround = baseline.World.Root.GetComponent<CityGroundSurfaceSystem>();
                Assert.That(baselineGround.CoatingJointCount, Is.Zero);
                Assert.That(baselineGround.SurfaceTriangleCount, Is.EqualTo(withFieldTriangles));
                Debug.Log($"City ground composition field off/on {baselineGround.BuildMilliseconds:F0}/{withField:F0} ms.");
#endif
            }
            finally
            {
                Shader.SetGlobalFloat(preview, 0f);
                CityWetSurfaceRegistry.SetImmediate(0f);
                GraphicsEffectsSettings.IntoxicationLensFxEnabled = previousLens;
                IntoxicationRenderState.Clear();
#if UNITY_EDITOR
                CityGroundSurfaceSystem.BypassJointBuildForMeasurement = false;
#endif
            }
        }

        private static IEnumerator MeasureCityJointFrames(Camera camera, Shot pose, int preview)
        {
            if (!FrameTimingManager.IsFeatureEnabled())
            {
                Debug.Log("Ground joint frame timing unavailable on this Editor target.");
                yield break;
            }

            var target = new RenderTexture(Width, Height, 24);
            var times = new FrameTiming[1];
            var cpu = new[] { new List<double>(), new List<double>() };
            var gpu = new[] { new List<double>(), new List<double>() };
            PlayerCameraFollow follow = camera.GetComponent<PlayerCameraFollow>();
            bool wasFollowing = follow != null && follow.enabled;
            RenderTexture previous = camera.targetTexture;
            Vector3 position = camera.transform.position;
            Quaternion rotation = camera.transform.rotation;
            float fov = camera.fieldOfView;
            try
            {
                if (follow != null) follow.enabled = false;
                camera.targetTexture = target;
                camera.transform.SetPositionAndRotation(pose.Position,
                    Quaternion.LookRotation(pose.Target - pose.Position, Vector3.up));
                camera.fieldOfView = pose.FieldOfView;
                for (int block = 0; block < 4; block++)
                {
                    int enabled = block == 0 || block == 3 ? 0 : 1;
                    Shader.SetGlobalFloat(preview, enabled == 0 ? 1f : 0f);
                    for (int frame = 0; frame < 20; frame++)
                    {
                        FrameTimingManager.CaptureFrameTimings();
                        yield return null;
                        if (frame < 8 || FrameTimingManager.GetLatestTimings(1, times) == 0) continue;
                        if (times[0].cpuMainThreadFrameTime > 0d) cpu[enabled].Add(times[0].cpuMainThreadFrameTime);
                        if (times[0].gpuFrameTime > 0d) gpu[enabled].Add(times[0].gpuFrameTime);
                    }
                }
                if (cpu[0].Count > 0 && cpu[1].Count > 0)
                    Debug.Log($"Ground joint fixed camera median CPU off/on {Median(cpu[0]):F2}/{Median(cpu[1]):F2} ms.");
                if (gpu[0].Count == 0 || gpu[1].Count == 0)
                    Debug.Log("Ground joint frame timing: the Editor returned no GPU samples.");
                else
                    Debug.Log($"Ground joint fixed camera median GPU off/on {Median(gpu[0]):F2}/{Median(gpu[1]):F2} ms.");
            }
            finally
            {
                if (follow != null) follow.enabled = wasFollowing;
                camera.targetTexture = previous;
                camera.transform.SetPositionAndRotation(position, rotation);
                camera.fieldOfView = fov;
                Shader.SetGlobalFloat(preview, 0f);
                Object.DestroyImmediate(target);
            }

            static double Median(List<double> samples)
            {
                samples.Sort();
                return samples[samples.Count / 2];
            }
        }

        private static void MeasureCityJointDraw(Camera camera, Shot pose, int preview)
        {
            // The 1-pixel readback synchronizes the identical camera renders.
            // This is a render-plus-sync cost in the Editor, not a GPU timer.
            var target = new RenderTexture(Width, Height, 24);
            var pixel = new Texture2D(1, 1, TextureFormat.RGB24, false);
            var timer = new System.Diagnostics.Stopwatch();
            var baseline = new double[5];
            var enabled = new double[5];
            RenderTexture previousTarget = camera.targetTexture, previousActive = RenderTexture.active;
            Vector3 previousPosition = camera.transform.position;
            Quaternion previousRotation = camera.transform.rotation;
            float previousFov = camera.fieldOfView;
            try
            {
                camera.targetTexture = target;
                camera.transform.SetPositionAndRotation(pose.Position,
                    Quaternion.LookRotation(pose.Target - pose.Position, Vector3.up));
                camera.fieldOfView = pose.FieldOfView;
                Draw(true); Draw(false); // warm both paths at the same pose
                for (int i = 0; i < baseline.Length; i++)
                {
                    if ((i & 1) == 0) { baseline[i] = Draw(true); enabled[i] = Draw(false); }
                    else { enabled[i] = Draw(false); baseline[i] = Draw(true); }
                }
                System.Array.Sort(baseline); System.Array.Sort(enabled);
                Debug.Log($"Ground joint camera render + sync: off {baseline[2]:F2} ms, on {enabled[2]:F2} ms.");
            }
            finally
            {
                RenderTexture.active = previousActive;
                camera.targetTexture = previousTarget;
                camera.transform.SetPositionAndRotation(previousPosition, previousRotation);
                camera.fieldOfView = previousFov;
                Shader.SetGlobalFloat(preview, 0f);
                Object.DestroyImmediate(target);
                Object.DestroyImmediate(pixel);
            }

            double Draw(bool off)
            {
                Shader.SetGlobalFloat(preview, off ? 1f : 0f);
                timer.Restart();
                camera.Render();
                RenderTexture.active = target;
                pixel.ReadPixels(new Rect(0f, 0f, 1f, 1f), 0, 0, false);
                timer.Stop();
                return timer.Elapsed.TotalMilliseconds;
            }
        }

        private static void AddCityJointAuditShots(CityGameRoot city, List<Shot> shots)
        {
            BuildingLot working = FindOrdinaryFrontage(city.Layout, CityDistrictKind.Industrial);
            BuildingLot residential = FindOrdinaryFrontage(city.Layout, CityDistrictKind.Residential);
            AddFrontage("working", working);
            AddFrontage("residential", residential);
            CityParkGateDescriptor gate = city.Layout.Park.Gates[0];
            shots.Add(Shot.At("ground-joints-audit-park", gate.Center - gate.OutwardNormal * 3f + Vector3.up * EyeHeight,
                gate.Center + gate.OutwardNormal * 5f, 68f));
            CityChurchCourtyardPlan church = city.World.ChurchCourtyardPlan;
            Rect garden = church.GardenBounds;
            Vector3 gardenGround = new Vector3(garden.center.x, church.GroundTopY, garden.center.y);
            foreach (bool enabled in new[] { false, true })
                shots.Add(Shot.At(CityLightingFrame(enabled ? "garden joint enabled" : "garden joint baseline"),
                    gardenGround + new Vector3(-2f, EyeHeight, -4f), gardenGround + Vector3.forward * 2f, 70f,
                    readyWhen: () =>
                    {
                        Shader.SetGlobalFloat("_CityJointPreviewOff", enabled ? 0f : 1f);
                        return true;
                    }));
            CityRiverPromenadeDescriptor quay = city.Layout.River.Promenades[0];
            Vector3 quayGround = new Vector3(quay.Bounds.center.x, (quay.SouthY + quay.NorthY) * .5f, quay.Bounds.center.y);
            shots.Add(Shot.At("ground-joints-audit-river", quayGround + new Vector3(0f, EyeHeight, -3f),
                quayGround + Vector3.forward * 4f, 68f));
            CityRiverBridgeDescriptor timber = city.Layout.River.Bridges.Single(bridge => bridge.Definition.Role == CityBridgeRole.ParkFootbridge);
            var timberDeck = city.World.RiverRoot.transform.Find("River Bridges/Central Park Timber Footbridge/Timber Deck Planks");
            Assert.That(timberDeck.GetComponent<MeshFilter>().sharedMesh.name, Does.EndWith("Profiled Mesh"));
            var timberProperties = new MaterialPropertyBlock();
            timberDeck.GetComponent<Renderer>().GetPropertyBlock(timberProperties);
            Texture timberAlbedo = timberProperties.GetTexture("_BaseMap");
            Assert.That(timberAlbedo, Is.Not.Null);
            Assert.That(timberAlbedo.name, Is.EqualTo(CityParkSurfaceAppearance.GetTexture(CityParkSurfaceKind.Timber).name));
            Assert.That(timberProperties.GetTexture("_GroundSubstrateMap"), Is.Null,
                "The final board must be timber right through its rounded edge.");
            Rect span = timber.SpanBounds;
            float timberY = timber.AverageY + CityStreetSurfacePlanner.RoadTop;
            var timberEntry = new Vector3(span.xMin, timberY, span.center.y);
            shots.Add(Shot.At("ground-joints-audit-timber-entry",
                timberEntry + new Vector3(-1.7f, EyeHeight, .4f), timberEntry + Vector3.right * 1.6f, 60f));
            Vector3 otherEntry = new Vector3(span.xMax, timberY, span.center.y);
            shots.Add(Shot.At("ground-joints-audit-timber-east",
                otherEntry + new Vector3(1.7f, EyeHeight, -.4f), otherEntry + Vector3.left * 1.6f, 60f));
            CityRiverLandingDescriptor landing = city.Layout.River.Landings[0];
            Vector3 stairCenter = new Vector3(landing.StairBounds.center.x, (landing.UpperY + landing.LowerY) * .5f,
                landing.StairBounds.center.y);
            Vector3 stairTop = stairCenter - landing.DescentDirection * (landing.StairBounds.height * .5f + .65f);
            stairTop.y = landing.UpperY;
            shots.Add(Shot.At("ground-joints-audit-river-stair", stairTop + Vector3.up * EyeHeight, stairCenter, 60f));
            CityPortController portController = Object.FindAnyObjectByType<CityPortController>();
            Assert.That(portController, Is.Not.Null);
            CityPortPlan port = portController.Plan;
            AddPort("port", new Vector3(-19.5f, 3.22f, -6f), new Vector3(-8f, 2f, -.8f), 68f);
            AddPort("port-rack", new Vector3(10.5f, 3.22f, -11.1f), new Vector3(11.3f, 2.9f, -6.9f), 58f);
            CityPortAccessPlan access = port.Access;
            shots.Add(Shot.At("ground-joints-audit-port-road", Vector3.Lerp(access.StreetConnection, access.Gate, .03f) + Vector3.up * EyeHeight,
                Vector3.Lerp(access.StreetConnection, access.Gate, .18f), 66f));
            CityTerraceDistrictPlan terrace = CityTerraceDistrictPlan.ForLayout(city.Layout);
            shots.Add(Shot.At("ground-joints-audit-terrace", terrace.EntryEndWorld + Vector3.up * EyeHeight,
                terrace.LowerArrivalWorld, 68f));
            Rect burial = city.World.CemeteryPlan.Grounds;
            Vector3 grave = new Vector3(burial.center.x, city.World.CemeteryPlan.GroundTopY, burial.center.y);
            shots.Add(Shot.At("ground-joints-audit-cemetery", grave + new Vector3(0f, EyeHeight, -2f),
                grave + Vector3.forward * 4f, 65f));
            CitySeacoastFrame coast = city.World.SeacoastPlan.Frame;
            Vector3 shore = new Vector3(coast.EastZone.center.x, coast.BeachEdgeTopY, coast.WaterlineZ - 3f);
            shots.Add(Shot.At("ground-joints-audit-shore", shore + Vector3.up * EyeHeight,
                shore + Vector3.right * 7f + Vector3.forward * 3f, 68f));
            CityMountainTunnelDescriptor tunnel = city.World.MountainBoundaryPlan.Tunnel;
            shots.Add(Shot.At("ground-joints-audit-tunnel", tunnel.PortalGroundCenter - tunnel.Axis * 5f + Vector3.up * EyeHeight,
                tunnel.PortalGroundCenter, 68f));
            CityEastGroundTransitionPlan east = CityEastGroundTransitionPlan.Create(city.Layout);
            Vector2 seam = new Vector2(east.Bounds.center.x, east.SeamZ);
            if (east.IsEnabled && CityTerrainSurfacePlan.TrySampleGroundTop(city.Layout, seam, out float seamTop, out _))
            {
                Vector3 target = new Vector3(seam.x, seamTop, seam.y);
                shots.Add(Shot.At("ground-joints-audit-east", target + new Vector3(-4f, EyeHeight, -4f),
                    target + Vector3.right * 4f, 68f));
            }

            void AddFrontage(string name, BuildingLot lot)
            {
                Shot pose = FrameWindowFacade(name, lot);
                Vector3 near = Vector3.Lerp(pose.Position, pose.Target, .62f);
                near.y = pose.Position.y;
                Vector3 baseTarget = pose.Target;
                baseTarget.y = pose.Position.y - EyeHeight + .35f;
                shots.Add(Shot.At("ground-joints-audit-" + name, near, baseTarget, 68f));
            }

            void AddPort(string name, Vector3 localEye, Vector3 localTarget, float fov)
            {
                Vector3 eye = port.World(localEye);
                int visibleAfterFrame = -1;
                shots.Add(Shot.At("ground-joints-audit-" + name, eye, port.World(localTarget), fov, readyWhen: () =>
                {
                    if (visibleAfterFrame < 0)
                    {
                        city.Player.Motor.Teleport(eye - Vector3.up * EyeHeight);
                        city.Visibility.Refresh(true);
                        portController.ForcePresentation = true;
                        portController.RefreshPresentation();
                        Physics.SyncTransforms();
                        Assert.That(portController.ShorePresentationActive, Is.True);
                        Assert.That(Physics.RaycastAll(eye, Vector3.down, 2f, Physics.DefaultRaycastLayers,
                            QueryTriggerInteraction.Ignore).Any(hit => hit.collider.transform.IsChildOf(portController.transform) &&
                            Mathf.Abs(hit.point.y - port.QuayTopY) < .06f), Is.True,
                            "The live quay must support the port camera at " + localEye);
                        visibleAfterFrame = Time.frameCount + 3;
                    }
                    return Time.frameCount >= visibleAfterFrame;
                }));
            }
        }

        private static void AssertCityStreetJointCoverage(CityGameRoot city, CityGroundSurfaceSystem system)
        {
            CityLayout layout = city.Layout;
            CityStreetSupportPlan support = CityStreetSurfacePlanner.Create(layout).SupportPlan;
            var sectors = system.GetComponentsInChildren<MeshCollider>(true)
                .Where(collider => collider.enabled && collider.name.StartsWith("Ground sector"))
                .Select(collider => (Collider: collider, Bounds: collider.bounds)).ToArray();
            var missing = new List<string>();
            int samples = 0;
            float sidewalk = layout.RoadWidth * .5f - CityStreetSurfacePlanner.SidewalkWidth * .5f;
            foreach (RoadEdge edge in layout.RoadEdges)
            {
                if (layout.GetPathKind(edge) != CityPathKind.Street || layout.TryGetBridge(edge, out _) ||
                    layout.ElevationPlan.TryGetSignatureStair(edge, out _)) continue;
                CityRoadPath path = layout.RoadGeometry.Get(edge);
                for (int station = 1; station <= 3; station++)
                {
                    CityRoadSample sample = path.SampleDistance(path.Length * station / 4f);
                    for (int lane = -1; lane <= 1; lane++)
                    {
                        Vector2 p = sample.Position + sample.Right * (lane * sidewalk);
                        if (!support.TrySampleTop(p, out float y, out _)) continue;
                        samples++;
                        var ray = new Ray(new Vector3(p.x, y + .10f, p.y), Vector3.down);
                        bool contact = sectors.Any(sector => sector.Bounds.IntersectRay(ray, out float enter) &&
                            enter <= .20f && sector.Collider.Raycast(ray, out RaycastHit hit, .20f) && hit.normal.y > .6f);
                        if (!contact && missing.Count < 12) missing.Add(edge + " " + ray.origin.ToString("F2"));
                    }
                }
            }
            Assert.That(samples, Is.GreaterThan(500), "The survey must span the public street network.");
            Assert.That(missing, Is.Empty, "Missing final support on public streets: " + string.Join(", ", missing));
            Debug.Log("CITY JOINT STREET SURVEY OK " + samples + " lane and pavement contacts");
        }

        private static IEnumerator WalkCityJointRoutes(CityGameRoot city)
        {
            var motor = city.Player.Motor;
            motor.SetInputEnabled(false);
            IReadOnlyList<Vector3> garden = CityChurchCourtyardPlanner.GetLoopWaypoints(city.World.ChurchCourtyardPlan);
            CityTerraceDistrictPlan terrace = CityTerraceDistrictPlan.ForLayout(city.Layout);
            var routes = new[]
            {
                garden,
                new[] { terrace.EntryStartWorld, terrace.EntryEndWorld, terrace.LowerArrivalWorld }
            };
            foreach (IReadOnlyList<Vector3> route in routes)
            {
                motor.Teleport(route[0] + Vector3.up * PlayerFactory.GroundedRootOffset);
                for (int i = 1; i < route.Count; i++)
                    yield return WalkChurchGardenLeg(motor, route[i], "city joints " + route[i].ToString("F2"));
            }
            motor.SetInputEnabled(true);
        }

        private static Shot CityJointCurbShot(CityGameRoot city, CityGroundSurfaceSystem system)
        {
            CityLayout layout = city.Layout;
            CityStreetSurfacePlan streets = CityStreetSurfacePlanner.Create(layout);
            float half = (layout.RoadWidth - 2f * CityStreetSurfacePlanner.SidewalkWidth) * .5f;
            foreach (RoadEdge edge in layout.RoadEdges)
            {
                if (layout.GetPathKind(edge) != CityPathKind.Street || layout.TryGetBridge(edge, out _) ||
                    layout.ElevationPlan.TryGetSignatureStair(edge, out _)) continue;
                CityRoadPath path = layout.RoadGeometry.Get(edge);
                CityRoadSample sample = path.SampleDistance(path.Length * .5f);
                foreach (int side in new[] { -1, 1 })
                {
                    Vector2 across = sample.Right * side;
                    Vector2 rim = sample.Position + across * half;
                    if (!streets.SupportPlan.TrySampleTop(rim - across * .12f, FootstepGroundKind.Concrete, out float road) ||
                        !streets.SupportPlan.TrySampleTop(rim + across * .12f, FootstepGroundKind.Stone, out float walk) ||
                        walk - road < .035f || walk - road > .12f) continue;
                    Vector2 band = rim - across * .006f;
                    var point = new Vector3(band.x, walk, band.y);
                    if (!TryCommonGroundHit(system, point, out RaycastHit curved) ||
                        curved.point.y <= road + .02f || Mathf.Abs(curved.point.y - walk) > .024f) continue;
                    Vector3 tangent = new Vector3(sample.Tangent.x, 0f, sample.Tangent.y);
                    Vector3 inward = new Vector3(across.x, 0f, across.y);
                    return Shot.At("ground-joints-profiled-curb", curved.point - tangent * 1.8f -
                        inward * .65f + Vector3.up * .28f, curved.point + tangent * .6f, 55f);
                }
            }
            Assert.Fail("No raised street curb supplied its authored curved facing and intact support.");
            return default;
        }

        [UnityTest]
        [Explicit("City common ground, painted roads, local excavation and collider-free Home view.")]
        public IEnumerator CityGroundSurface()
        {
            GameSessionState.BeginNewGame();
            GameSessionState.TryStartGameTimeFromWake();
            GameSessionState.AdvanceGameTime((float)((12d * 60d - GameSessionState.GameTimeOfDayMinutes) /
                GameTimeState.GameMinutesPerRealSecond));
            CityGameRoot city = null;
            var deferredStreetChecks = new List<System.Action>();
            yield return Capture(SceneIds.City, () =>
            {
                city = Object.FindAnyObjectByType<CityGameRoot>();
                return city != null && city.IsInitialized ? city : null;
            }, () => CommonGroundShots(city, deferredStreetChecks));
            foreach (System.Action check in deferredStreetChecks) check();
            AssertBuildingPerimeters(city);
            AssertProjectedFringeCoatings(city, city.World.Root.GetComponent<CityGroundSurfaceSystem>());
            AssertCommonGroundExcavation(city);
            yield return AssertCommonGroundSand(city);
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
                return new[] { Shot.At("common-ground-home-balcony-street", eye,
                    new Vector3(eye.x + 13f, home.BalconyLayout.StreetGroundY, eye.z), 70f) };
            });
        }

        private static Shot[] CommonGroundShots(CityGameRoot city, List<System.Action> deferredStreetChecks)
        {
            CityGroundSurfaceSystem system = city.World.Root.GetComponent<CityGroundSurfaceSystem>();
            Assert.That(system, Is.Not.Null);
            Assert.That(system.SourceCount, Is.GreaterThan(0));
            Assert.That(system.SectorCount, Is.GreaterThan(0));
            Assert.That(system.SurfaceTriangleCount, Is.GreaterThan(0));
            Debug.Log($"City ground composition: {system.BuildMilliseconds:F1} ms, {system.SurfaceTriangleCount} top triangles.");
            Transform common = system.transform.Find("City Ground");
            Assert.That(common, Is.Not.Null);
            Assert.That(common.GetComponentsInChildren<MeshRenderer>(), Is.Not.Empty);
            MeshRenderer streetSource = city.World.Root.GetComponentsInChildren<MeshRenderer>(true)
                .Single(renderer => renderer.name == "Unified Street Surface");
            Assert.That(streetSource.enabled, Is.False, "Only the final ground sectors may draw the street.");
            Mesh streetMesh = streetSource.GetComponent<MeshFilter>().sharedMesh;
            Assert.That(streetMesh.subMeshCount, Is.EqualTo(3), "Asphalt, pavement and park path share one source.");
            for (int slot = 0; slot < streetMesh.subMeshCount; slot++)
                Assert.That(streetMesh.GetIndexCount(slot), Is.GreaterThan(0), "The combined street lost region " + slot);
            string[] formerLayers = { "Street Surfaces", "Curved Street Surfaces", "Park Paths",
                "Sidewalk Surfaces", "Curved Sidewalk Surfaces", "Road Center Markings",
                "Curved Road Center Markings", "Pedestrian Crossings" };
            foreach (Renderer renderer in city.World.Root.GetComponentsInChildren<Renderer>(true))
                if (formerLayers.Contains(renderer.name))
                    Assert.That(renderer.enabled, Is.False, renderer.name + " still draws a separate ground layer.");

            Physics.SyncTransforms();
            CityFairPlan fair = CityFairPlanner.Create(city.Layout);
            foreach (CityFairStall stall in fair.Stalls)
                foreach (int x in new[] { -1, 1 })
                    foreach (int z in new[] { -1, 1 })
                    {
                        Vector3 foot = stall.Position + stall.Rotation * new Vector3(x * 1.32f, 0f, z);
                        float support = fair.SampleGroundY(foot);
                        Vector3 expected = new Vector3(foot.x, support, foot.z);
                        Assert.That(TryCommonGroundHit(system, expected, out RaycastHit contact), Is.True,
                            "Missing physical ground under fair foot " + foot);
                        Assert.That(contact.point.y, Is.EqualTo(support).Within(.003f),
                            "Fair support must exist in the final ground triangles.");
                        Assert.That(Mathf.Abs(foot.y - contact.point.y), Is.LessThanOrEqualTo(.035f),
                            "Fair feet must keep their existing physical contact tolerance.");
                    }
            CityStreetSurfacePlan plan = CityStreetSurfacePlanner.Create(city.Layout);
            Assert.That(plan.CenterMarkingGeometry, Is.Not.Empty);
            RuntimeOrientedBox mark = plan.CenterMarkingGeometry[0];
            float markingRoadTop = CommonPlannedRoadTop(plan, mark.Center);
            Vector3 marking = new Vector3(mark.Center.x, markingRoadTop, mark.Center.z);
            AssertCommonGroundContact(system, marking, FootstepGroundKind.Concrete,
                Resources.Load<Texture2D>(CityExteriorAppearance.RoadMarkingTextureResourcePath));
            Assert.That(plan.CrosswalkMarkingGeometry, Is.Not.Empty);
            // Curved crossings also use oriented paint boxes, but their road
            // substrate is a ribbon. Keep this probe on a straight crossing;
            // the supported street probe below checks an actual graded span.
            RuntimeOrientedBox crossingPaint = plan.CrosswalkMarkingGeometry.First(box =>
                plan.StreetGeometry.Any(street => street.TrySampleTop(box.Center, out _)));
            Vector3 crossing = crossingPaint.Center;
            crossing.y = CommonPlannedRoadTop(plan, crossing);
            AssertCommonGroundContact(system, crossing, FootstepGroundKind.Concrete,
                Resources.Load<Texture2D>(CityExteriorAppearance.RoadMarkingTextureResourcePath));

            RuntimeOrientedBox pavement = plan.SidewalkGeometry[0];
            Assert.That(pavement.TrySampleTop(pavement.Center, out float pavementTop), Is.True);
            AssertCommonGroundContact(system, new Vector3(pavement.Center.x, pavementTop, pavement.Center.z),
                FootstepGroundKind.Stone, Resources.Load<Texture2D>(CityExteriorAppearance.SidewalkTextureResourcePath));

            // Compact City retired its curved pilot. Prefer a real curve
            // when present, otherwise exercise an ordinary supported street.
            bool foundStreet = false;
            RoadEdge selectedEdge = default;
            CityRoadSample middle = default;
            Vector3 road = default, side = default;
            IEnumerable<RoadEdge> candidates = city.Layout.RoadGeometry.CurvedEdges
                .Concat(city.Layout.RoadEdges).Distinct();
            foreach (RoadEdge edge in candidates)
            {
                if (city.Layout.GetPathKind(edge) != CityPathKind.Street ||
                    city.Layout.TryGetBridge(edge, out _) ||
                    city.Layout.ElevationPlan.TryGetSignatureStair(edge, out _)) continue;
                CityRoadPath path = city.Layout.RoadGeometry.Get(edge);
                CityRoadSample sample = path.SampleDistance(path.Length * .5f);
                if (!plan.SupportPlan.TrySampleTop(sample.Position, FootstepGroundKind.Concrete, out float roadTop)) continue;
                foreach (int sign in new[] { 1, -1 })
                {
                    Vector2 sidePoint = sample.Position + sample.Right * (sign * (city.Layout.RoadWidth * .5f - .5f));
                    if (!plan.SupportPlan.TrySampleTop(sidePoint, FootstepGroundKind.Stone, out float sideTop)) continue;
                    selectedEdge = edge; middle = sample;
                    road = new Vector3(sample.Position.x, roadTop, sample.Position.y);
                    side = new Vector3(sidePoint.x, sideTop, sidePoint.y);
                    foundStreet = true;
                    break;
                }
                if (foundStreet) break;
            }
            Assert.That(foundStreet, Is.True, "The rendered City must contain an actual supported asphalt/pavement span.");
            AssertCommonGroundContact(system, road, FootstepGroundKind.Concrete,
                Resources.Load<Texture2D>(CityExteriorAppearance.RoadTextureResourcePath), checkTexture: false);
            AssertCommonGroundContact(system, side, FootstepGroundKind.Stone,
                Resources.Load<Texture2D>(CityExteriorAppearance.SidewalkTextureResourcePath));
            string kerbLabel = city.Layout.RoadGeometry.IsCurved(selectedEdge)
                ? "common-ground-02-curved-kerb" : "common-ground-02-street-kerb";

            var shots = new List<Shot>
            {
                Shot.At("common-ground-01-road-paint", marking + new Vector3(2f, 2.1f, -3f), marking, 62f),
                Shot.At(kerbLabel, road + Vector3.up * EyeHeight,
                    side + new Vector3(middle.Tangent.x, .12f, middle.Tangent.y) * 6f, 72f)
            };
            CityEastGroundTransitionPlan seam = CityEastGroundTransitionPlan.Create(city.Layout);
            Assert.That(seam.IsEnabled, Is.True);
            Vector2 seamPoint = new Vector2(seam.Bounds.center.x, seam.SeamZ);
            Assert.That(CityTerrainSurfacePlan.TrySampleGroundTop(city.Layout, seamPoint, out float seamTop, out _), Is.True);
            Vector3 seamTarget = new Vector3(seamPoint.x, seamTop, seamPoint.y);
            Assert.That(TryCommonGroundHit(system, seamTarget, out RaycastHit seamHit), Is.True);
            Assert.That(seamHit.point.y, Is.EqualTo(seamTop).Within(.015f), "The garden/yard join changed its physical height.");
            shots.Add(Shot.At("common-ground-03-garden-yard-seam", seamTarget + new Vector3(-4f, 2.5f, -4f),
                seamTarget + Vector3.right * 4f, 72f));
            List<Rect> authoredPads = CommonGroundAuthoredPads(city.Layout);
            AssertCommonStreetOuterSeams(city, system, plan.SupportPlan, authoredPads, shots, deferredStreetChecks);
            AssertCommonStreetGradeBreak(city, system, plan.SupportPlan, authoredPads, false, shots, deferredStreetChecks);
            AssertCommonStreetGradeBreak(city, system, plan.SupportPlan, authoredPads, true, shots, deferredStreetChecks);
            AddBuildingPerimeterShots(city, shots);
            return shots.ToArray();
        }

        private static void AssertProjectedFringeCoatings(CityGameRoot city, CityGroundSurfaceSystem system)
        {
            Physics.SyncTransforms();
            // Reuse the terrain meshes that were actually composed. The
            // analytical height field is curved between mesh vertices, while
            // paint must preserve these exact physical triangle planes.
            var sources = new List<(Vector3[] vertices, int[] triangles)>();
            foreach (MeshFilter filter in city.World.Root.GetComponentsInChildren<MeshFilter>(true))
            {
                if (filter.name != CityFringeYardGroundWorldBuilder.MountainGroundObjectName &&
                    filter.name != CityFringeYardGroundWorldBuilder.GenericGroundObjectName) continue;
                Mesh mesh = filter.sharedMesh;
                Assert.That(mesh, Is.Not.Null);
                Vector3[] vertices = mesh.vertices;
                Matrix4x4 matrix = filter.transform.localToWorldMatrix;
                for (int i = 0; i < vertices.Length; i++) vertices[i] = matrix.MultiplyPoint3x4(vertices[i]);
                sources.Add((vertices, mesh.triangles));
            }
            Assert.That(sources, Is.Not.Empty);
            int checkedYards = 0;
            foreach (CityFringeYardDescriptor yard in city.World.FringeYardPlan.Yards)
            {
                CityFringeYardPartDescriptor[] representatives = yard.Parts
                    .Where(part => part.IsProjectedGroundCoating)
                    .GroupBy(part => part.Kind)
                    .Select(group => group.OrderByDescending(part => part.Size.z).First())
                    .OrderBy(part => part.Kind == CityFringeYardPartKind.WheelRut ? 0 :
                        part.Kind == CityFringeYardPartKind.RoadShoulder ? 1 : 2).Take(3).ToArray();
                if (representatives.Length == 0) continue;
                foreach (CityFringeYardPartDescriptor part in representatives)
                    foreach (float across in new[] { -.4f, 0f, .4f })
                    {
                        Vector3 point = part.Center + part.Rotation * Vector3.right * (part.Size.x * across);
                        Vector2 xz = new Vector2(point.x, point.z);
                        float support = float.NegativeInfinity;
                        bool found = false;
                        foreach (var source in sources)
                            if (TryCommonFringeSourceTop(source.vertices, source.triangles, xz, out float height))
                            { support = Mathf.Max(support, height); found = true; }
                        Assert.That(found, Is.True, "Projected fringe footprint lost its terrain substrate: " + part.StableId);
                        bool isRut = part.Kind == CityFringeYardPartKind.WheelRut;
                        Texture texture = isRut ? Resources.Load<Texture2D>(CityRiverSurfaceAppearance.BedTextureResourcePath) : null;
                        AssertCommonGroundContact(system, new Vector3(point.x, support, point.z),
                            FootstepGroundKind.Soil, texture, checkTexture: isRut);
                    }
                checkedYards++;
            }
            Assert.That(checkedYards, Is.GreaterThan(0), "Exercise the projected fringe coatings in final ground sectors.");
        }

        private static bool TryCommonFringeSourceTop(Vector3[] vertices, int[] triangles, Vector2 point, out float top)
        {
            top = float.NegativeInfinity;
            bool found = false;
            for (int i = 0; i < triangles.Length; i += 3)
            {
                Vector3 a = vertices[triangles[i]], b = vertices[triangles[i + 1]], c = vertices[triangles[i + 2]];
                if (point.x < Mathf.Min(a.x, Mathf.Min(b.x, c.x)) - .0001f ||
                    point.x > Mathf.Max(a.x, Mathf.Max(b.x, c.x)) + .0001f ||
                    point.y < Mathf.Min(a.z, Mathf.Min(b.z, c.z)) - .0001f ||
                    point.y > Mathf.Max(a.z, Mathf.Max(b.z, c.z)) + .0001f) continue;
                if (CityStreetSupportPatch.TrySampleTop(new[] { a, b, c }, point, out float height))
                { top = Mathf.Max(top, height); found = true; }
            }
            return found;
        }

        private static float CommonPlannedRoadTop(CityStreetSurfacePlan plan, Vector3 point)
        {
            Assert.That(plan.SupportPlan.TrySampleTop(new Vector2(point.x, point.z), FootstepGroundKind.Concrete, out float top),
                Is.True, "The chosen road-paint probe has no planned substrate.");
            return top;
        }

        private static List<Rect> CommonGroundAuthoredPads(CityLayout layout)
        {
            var result = new List<Rect>();
            foreach (CityDistrictPointOfInterestDescriptor point in layout.DistrictPointsOfInterest)
                Add(point.PublicBounds);
            foreach (BuildingLot lot in layout.BuildingLots)
                if (lot.IsBar || lot.IsPlayerHome || lot.IsSupermarket)
                    Add(CityStreetSurfacePlanner.CreateEntranceForecourt(layout, lot, 2f).Footprint);
            CityPortAccessPlan port = CityPortAccessPlan.ForLayout(layout);
            if (port != null) Add(port.GradedBounds);
            CityEastExitPlan east = CityEastExitPlanner.Create(layout);
            if (east.IsEnabled) { Add(east.YardBounds); Add(east.NorthYardBounds); Add(east.StreetOpening); }
            return result;

            void Add(Rect bounds) => result.Add(Rect.MinMaxRect(bounds.xMin - .3f, bounds.yMin - .3f,
                bounds.xMax + .3f, bounds.yMax + .3f));
        }

        private static void AssertCommonStreetOuterSeams(CityGameRoot city, CityGroundSurfaceSystem system,
            CityStreetSupportPlan support, List<Rect> authoredPads, List<Shot> shots, List<System.Action> deferredChecks)
        {
            var seen = new HashSet<(Vector3, Vector3)>();
            bool checkedSeam = false, selected = false;
            float selectedGrade = -1f;
            Vector3 selectedEdge = default;
            Vector2 selectedOutward = default, selectedTangent = default;
            foreach (CitySurfaceDescriptor surface in city.Layout.Surfaces)
            {
                if (surface.Kind != CitySurfaceKind.BuildableGround) continue;
                CityTerrainStreetBoundaryPlan.SurfaceBoundaryContext boundary = CityTerrainStreetBoundaryPlan.ForSurface(city.Layout, surface);
                if (boundary == null) continue;
                foreach (CityTerrainStreetBoundaryPlan.Segment segment in boundary.Segments)
                {
                    if (seen.Contains((segment.Start, segment.End)) || seen.Contains((segment.End, segment.Start))) continue;
                    Vector2 first = new Vector2(segment.Start.x, segment.Start.z);
                    Vector2 delta = new Vector2(segment.End.x - segment.Start.x, segment.End.z - segment.Start.z);
                    float length = delta.magnitude;
                    if (length < .1f) continue;
                    bool belongs = false;
                    int samples = Mathf.Max(1, Mathf.CeilToInt(length));
                    for (int sample = 0; sample < samples; sample++)
                    {
                        Vector2 edge = first + delta * ((sample + .5f) / samples);
                        Vector2 soil = edge + segment.Outward * .04f;
                        if (authoredPads.Any(bounds => bounds.Contains(edge)) ||
                            !CityTerrainSurfacePlan.TrySampleGroundTop(city.Layout, soil, out _, out CitySurfaceDescriptor ground) ||
                            ground.Kind != CitySurfaceKind.BuildableGround || ground != surface ||
                            !support.TrySampleTop(edge - segment.Outward * .01f, out _, out FootstepGroundKind kind) ||
                            kind != FootstepGroundKind.Stone) continue;
                        bool supported = support.TrySampleTop(edge, out float streetTop, out _);
                        float terrainTop = CityTerrainSurfacePlan.SampleTop(city.Layout, ground, edge);
                        deferredChecks.Add(() =>
                        {
                            Assert.That(supported, Is.True);
                            Assert.That(terrainTop, Is.EqualTo(streetTop).Within(.003f),
                                "The full outer pavement seam must meet earth at " + edge);
                        });
                        if (!supported) continue;
                        belongs = true; checkedSeam = true;
                        float grade = Mathf.Abs(segment.End.y - segment.Start.y) / length;
                        if (grade <= selectedGrade) continue;
                        selectedGrade = grade; selected = true;
                        selectedEdge = new Vector3(edge.x, streetTop, edge.y);
                        selectedOutward = segment.Outward; selectedTangent = delta / length;
                    }
                    if (belongs) seen.Add((segment.Start, segment.End));
                }
            }
            bool hasSeam = checkedSeam && selected;
            deferredChecks.Add(() => Assert.That(hasSeam, Is.True, "No ordinary exterior pavement/earth seam was checked."));
            if (!hasSeam) return;
            Vector2 point = new Vector2(selectedEdge.x, selectedEdge.z);
            Vector2 inward = point - selectedOutward * .04f, outward = point + selectedOutward * .04f;
            deferredChecks.Add(() =>
            {
                Assert.That(support.TrySampleTop(inward, FootstepGroundKind.Stone, out float pavementTop), Is.True);
                AssertCommonGroundContact(system, new Vector3(inward.x, pavementTop, inward.y), FootstepGroundKind.Stone,
                    Resources.Load<Texture2D>(CityExteriorAppearance.SidewalkTextureResourcePath));
                Assert.That(CityTerrainSurfacePlan.TrySampleGroundTop(city.Layout, outward, out float soilTop, out _), Is.True);
                bool apron = CityBuildingPerimeterPlan.ForLayout(city.Layout).Contains(outward);
                AssertCommonGroundContact(system, new Vector3(outward.x, soilTop, outward.y),
                    apron ? FootstepGroundKind.Concrete : FootstepGroundKind.Soil,
                    Resources.Load<Texture2D>(apron ? CityExteriorAppearance.RoadTextureResourcePath :
                        CityExteriorAppearance.GroundTextureResourcePath));
            });
            Vector3 normal = new Vector3(selectedOutward.x, 0, selectedOutward.y);
            Vector3 tangent = new Vector3(selectedTangent.x, 0, selectedTangent.y);
            shots.Add(Shot.At("common-ground-04-pavement-earth-seam",
                selectedEdge + normal * .8f - tangent * 3f + Vector3.up * .6f,
                selectedEdge + tangent * 5f, 72f));
        }

        private static void AssertCommonStreetGradeBreak(CityGameRoot city, CityGroundSurfaceSystem system,
            CityStreetSupportPlan support, List<Rect> authoredPads, bool deadEnd, List<Shot> shots, List<System.Action> deferredChecks)
        {
            CityLayout layout = city.Layout;
            // Default geography need not contain every topology. The pure
            // profile regression owns those cases; the rendered fallback still
            // proves a real graded road/pavement contact in this scene.
            for (int selection = 0; selection < 2; selection++)
            foreach (RoadEdge edge in layout.RoadEdges.OrderByDescending(candidate =>
                         Mathf.Abs(layout.GetNodeWorldPosition(candidate.A).y - layout.GetNodeWorldPosition(candidate.B).y)))
            {
                if (layout.GetPathKind(edge) != CityPathKind.Street || layout.RoadGeometry.IsCurved(edge) ||
                    layout.ElevationPlan.TryGetSignatureStair(edge, out _) || layout.TryGetBridge(edge, out _) ||
                    Mathf.Abs(layout.GetNodeWorldPosition(edge.A).y - layout.GetNodeWorldPosition(edge.B).y) < .05f) continue;
                foreach (Vector2Int node in new[] { edge.A, edge.B })
                {
                    RoadEdge[] links = layout.RoadEdges.Where(candidate => candidate.Contains(node) &&
                        layout.GetPathKind(candidate) == CityPathKind.Street).ToArray();
                    if (selection == 0 && (deadEnd ? links.Length != 1 :
                        links.Length != 2 || links[0].IsHorizontal != links[1].IsHorizontal)) continue;
                    CityRoadPath path = layout.RoadGeometry.Get(edge);
                    float station = node == edge.A ? layout.RoadGeometry.GetEndpointInset(edge, node) :
                        path.Length - layout.RoadGeometry.GetEndpointInset(edge, node);
                    if (selection != 0) station = path.Length * .5f;
                    CityRoadSample center = path.SampleDistance(station);
                    float halfCarriageway = (layout.RoadWidth - 2f * CityStreetSurfacePlanner.SidewalkWidth) * .5f;
                    for (int side = -1; side <= 1; side += 2)
                    {
                        Vector2 curb = center.Position + center.Right * (side * halfCarriageway);
                        if (authoredPads.Any(bounds => bounds.Contains(curb)) ||
                            !support.TrySampleTop(curb + center.Right * (side * .1f), FootstepGroundKind.Stone, out _)) continue;
                        foreach (float offset in new[] { -.15f, 0f, .15f })
                        {
                            float distance = station + offset;
                            CityRoadSample sample = path.SampleDistance(distance);
                            float datum = layout.ElevationPlan.SampleRoadDatum(edge, distance / path.Length);
                            Vector2 road = sample.Position + sample.Right * (side * (halfCarriageway - .1f));
                            Vector2 pavement = sample.Position + sample.Right * (side * (halfCarriageway + .1f));
                            deferredChecks.Add(() =>
                            {
                                Assert.That(support.TrySampleTop(road, FootstepGroundKind.Concrete, out float roadTop), Is.True);
                                Assert.That(support.TrySampleTop(pavement, FootstepGroundKind.Stone, out float pavementTop), Is.True);
                                Assert.That(roadTop, Is.EqualTo(datum + CityStreetSurfacePlanner.RoadTop).Within(.003f));
                                Assert.That(pavementTop - roadTop,
                                    Is.EqualTo(CityStreetSurfacePlanner.SidewalkTop - CityStreetSurfacePlanner.RoadTop).Within(.003f),
                                    "The curb must follow the road through its grade break.");
                                AssertCommonGroundContact(system, new Vector3(road.x, roadTop, road.y), FootstepGroundKind.Concrete,
                                    Resources.Load<Texture2D>(CityExteriorAppearance.RoadTextureResourcePath));
                                AssertCommonGroundContact(system, new Vector3(pavement.x, pavementTop, pavement.y), FootstepGroundKind.Stone,
                                    Resources.Load<Texture2D>(CityExteriorAppearance.SidewalkTextureResourcePath));
                            });
                        }
                        Vector3 target = new Vector3(curb.x, layout.ElevationPlan.SampleRoadDatum(edge, station / path.Length) +
                            CityStreetSurfacePlanner.SidewalkTop, curb.y);
                        Vector3 tangent = new Vector3(center.Tangent.x, 0f, center.Tangent.y);
                        Vector3 across = new Vector3(center.Right.x, 0f, center.Right.y) * side;
                        string label = deadEnd ? "common-ground-06-dead-end-grade-break" : "common-ground-05-through-grade-break";
                        if (selection != 0) label += "-ordinary-graded-street";
                        shots.Add(Shot.At(label,
                            target - tangent * 3f - across * 1.2f + Vector3.up * .65f,
                            target + tangent * 5f, 72f));
                        return;
                    }
                }
            }
            deferredChecks.Add(() => Assert.Fail("No ordinary sloping street/pavement contact was available for the rendered proof."));
        }

        private static void AssertCommonGroundContact(CityGroundSurfaceSystem system, Vector3 expected,
            FootstepGroundKind kind, Texture texture, bool checkTexture = true)
        {
            Assert.That(TryCommonGroundHit(system, expected, out RaycastHit hit), Is.True, "Missing common ground at " + expected);
            Assert.That(hit.point.y, Is.EqualTo(expected.y).Within(.003f), "Render/collision height at " + expected);
            FootstepGround marker = hit.collider.GetComponent<FootstepGround>();
            Assert.That(marker, Is.Not.Null);
            Assert.That(marker.ResolveKind(hit.triangleIndex), Is.EqualTo(kind), "Surface sound at " + expected);
            if (!checkTexture) return;
            Assert.That(texture, Is.Not.Null);
            MeshCollider collider = (MeshCollider)hit.collider;
            int triangle = hit.triangleIndex;
            int slot = 0;
            while (slot < collider.sharedMesh.subMeshCount - 1)
            {
                int count = (int)collider.sharedMesh.GetIndexCount(slot) / 3;
                if (triangle < count) break;
                triangle -= count; slot++;
            }
            Renderer renderer = collider.GetComponent<Renderer>();
            var block = new MaterialPropertyBlock(); renderer.GetPropertyBlock(block, slot);
            Texture actual = block.GetTexture("_BaseMap");
            if (actual == null) actual = renderer.sharedMaterials[slot].GetTexture("_BaseMap");
            Texture expectedTexture = ResolveCommonGroundFinalTexture(texture);
            Assert.That(expectedTexture, Is.Not.Null, "Missing final City texture for " + DescribeCommonGroundTexture(texture));
            Assert.That(actual == expectedTexture, Is.True,
                "The contact must belong to its final City material region. " +
                "source=" + DescribeCommonGroundTexture(texture) + ", expected=" + DescribeCommonGroundTexture(expectedTexture) +
                ", actual=" + DescribeCommonGroundTexture(actual));
        }

        private static Texture ResolveCommonGroundFinalTexture(Texture source)
        {
            // CitySurfaceLibrary replaces donor albedos after composition.
            // Match the exact source asset, never a shared filename.
            foreach (CitySurfaceSheet sheet in CitySurfaceLibrary.Manifest.sheets)
                if (!sheet.unique_uv && !string.IsNullOrEmpty(sheet.source) &&
                    Resources.Load<Texture2D>(sheet.source) == source)
                    return Resources.Load<Texture2D>(sheet.albedo);
            return source;
        }

        private static string DescribeCommonGroundTexture(Texture texture)
        {
            if (texture == null) return "null";
            string path = "runtime";
#if UNITY_EDITOR
            path = UnityEditor.AssetDatabase.GetAssetPath(texture);
#endif
            return texture.name + " [" + path + ", instance=" + texture.GetEntityId() + "]";
        }

        private static bool TryCommonGroundHit(CityGroundSurfaceSystem system, Vector3 expected, out RaycastHit nearest)
        {
            nearest = default;
            bool found = false;
            var ray = new Ray(expected + Vector3.up * .30f, Vector3.down);
            foreach (MeshCollider collider in system.GetComponentsInChildren<MeshCollider>(true))
            {
                if (!collider.enabled || !collider.gameObject.activeInHierarchy ||
                    !(collider.name.StartsWith("Ground sector") || collider.name.StartsWith("Dynamic ground sector"))) continue;
                if (collider.Raycast(ray, out RaycastHit hit, .60f) && (!found || hit.distance < nearest.distance))
                { found = true; nearest = hit; }
            }
            return found;
        }

        private static void AssertCommonGroundExcavation(CityGameRoot city)
        {
            CityGroundSurfaceSystem system = city.World.Root.GetComponent<CityGroundSurfaceSystem>();
            CityCemeteryGroundExcavation excavation = city.World.CemeteryGroundExcavation;
            Assert.That(excavation, Is.Not.Null);
            CityCemeteryPartDescriptor alley = city.World.CemeteryPlan.Parts.First(part =>
                part.Kind == CityCemeteryPartKind.Alley && part.Size.x > .8f && part.Size.z > .8f &&
                TryCommonGroundHit(system, part.Center, out RaycastHit hit) && hit.collider.name.StartsWith("Dynamic ground sector"));
            Vector3 point = new Vector3(alley.Center.x, city.World.CemeteryPlan.GroundTopY, alley.Center.z);
            var mouth = new Rect(point.x - .25f, point.z - .30f, .50f, .60f);
            Assert.That(TryCommonGroundHit(system, point, out RaycastHit before), Is.True);
            Assert.That(excavation.Excavate(mouth), Is.True);
            try
            {
                Physics.SyncTransforms();
                Assert.That(TryCommonGroundHit(system, before.point, out _), Is.False,
                    "A dug mouth must remove its painted alley and every ground cap.");
            }
            finally { Assert.That(excavation.Fill(mouth), Is.True); }
            Physics.SyncTransforms();
            Assert.That(TryCommonGroundHit(system, before.point, out RaycastHit filled), Is.True);
            Assert.That(filled.point.y, Is.EqualTo(before.point.y).Within(.003f));
        }

        private static IEnumerator AssertCommonGroundSand(CityGameRoot city)
        {
            CitySandTreading sand = city.World.Root.GetComponentsInChildren<CitySandTreading>().Single();
            Mesh visual = sand.GetComponent<MeshFilter>().sharedMesh;
            MeshCollider collider = sand.GetComponent<MeshCollider>();
            Assert.That(sand.enabled && sand.GetComponent<Renderer>().enabled && collider.enabled, Is.True);
            Assert.That(visual, Is.Not.SameAs(collider.sharedMesh));
            Vector3[] physicalBefore = collider.sharedMesh.vertices;
            Vector3 point = visual.vertices.First(vertex => sand.SampleVisibleDepth(vertex) > .035f);
            float looseBefore = sand.SampleVisibleDepth(point);
            sand.Press(point);
            yield return new WaitForSeconds(CitySandTreading.RebuildInterval * 2f);
            Assert.That(sand.SampleVisibleDepth(point), Is.LessThan(looseBefore * .9f));
            CollectionAssert.AreEqual(physicalBefore, collider.sharedMesh.vertices,
                "Local loose sand must still deform independently of physical ground.");
        }

        private static void AssertHomeCommonGround(CityLayout layout)
        {
            var host = new GameObject("Common Ground Home Acceptance");
            host.SetActive(false);
            try
            {
                HomeInteriorLayoutPlan interior = HomeInteriorLayoutPlanner.Generate();
                Transform exterior = HomeExteriorViewBuilder.Build(host.transform,
                    HomeBalconyLayoutPlanner.Generate(interior), HomeExteriorContextPlanner.Generate(layout));
                CityGroundSurfaceSystem system = exterior.GetComponent<CityGroundSurfaceSystem>();
                Assert.That(system, Is.Not.Null);
                Assert.That(system.SectorCount, Is.GreaterThan(0));
                Assert.That(system.BuildingPerimeterTriangleCount, Is.GreaterThan(0),
                    "The Home reconstruction must include the same asphalt aprons.");
                Assert.That(exterior.GetComponentsInChildren<Collider>(true).Any(collider => collider.enabled), Is.False,
                    "The bounded Home reconstruction must remain collider-free.");
                foreach (Renderer renderer in exterior.GetComponentsInChildren<Renderer>(true))
                    if (renderer.name.StartsWith("Home Exterior Street Surfaces") ||
                        renderer.name == "Home Exterior Unified Street" ||
                        renderer.name.StartsWith("Home Exterior Sidewalk Surfaces") ||
                        renderer.name.StartsWith("Home Exterior Road Center Markings") ||
                        renderer.name.StartsWith("Home Exterior Pedestrian Crossings"))
                        Assert.That(renderer.enabled, Is.False, renderer.name);
            }
            finally { Object.DestroyImmediate(host); }
        }
    }
}

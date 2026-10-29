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

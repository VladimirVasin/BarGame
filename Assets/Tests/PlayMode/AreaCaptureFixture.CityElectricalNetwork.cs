using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.IO;
using NUnit.Framework;
using Unity.Collections;
using UnityEngine;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

#if UNITY_EDITOR
[assembly: UnityEngine.TestRunner.TestRunCallback(typeof(BarPromenade.Tests.PlayMode.CityElectricalRunResult))]
#endif

namespace BarPromenade.Tests.PlayMode
{
#if UNITY_EDITOR
    public sealed class CityElectricalRunResult : UnityEngine.TestRunner.ITestRunCallback
    {
        internal const string Pending = "BarPromenade.CityElectrical.WriteMenuResult";
        internal static string OutputPath => Path.Combine(Application.platform == RuntimePlatform.OSXEditor
            ? "/tmp" : Path.GetTempPath(), "bargame-electrical-live-results.xml");
        public void RunStarted(NUnit.Framework.Interfaces.ITest tests) { }
        public void TestStarted(NUnit.Framework.Interfaces.ITest test) { }
        public void TestFinished(NUnit.Framework.Interfaces.ITestResult result) { }
        public void RunFinished(NUnit.Framework.Interfaces.ITestResult result)
        {
            if (!UnityEditor.SessionState.GetBool(Pending, false)) return;
            string xml = result.ToXml(true).OuterXml;
            if (!xml.Contains("BarPromenade.Tests.PlayMode.AreaCaptureFixture.CityElectricalNetwork") &&
                !xml.Contains("LiveCity_EveryBusStopCanBeBoardedFromThePublicPavement")) return;
            File.WriteAllText(OutputPath, xml);
            UnityEditor.SessionState.EraseBool(Pending);
            Debug.Log("City electrical verification result: " + OutputPath);
        }
    }
#endif

    public sealed partial class AreaCaptureFixture
    {
#if UNITY_EDITOR
        [UnityEditor.MenuItem("Bar Promenade/City/Verify Electrical Network")]
        private static void RunCityElectricalVerification()
        {
            UnityEditor.SessionState.SetBool(CityElectricalRunResult.Pending, true);
            if (File.Exists(CityElectricalRunResult.OutputPath)) File.Delete(CityElectricalRunResult.OutputPath);
            RunPigeonVerification(typeof(AreaCaptureFixture).FullName + "." + nameof(CityElectricalNetwork));
        }
        [UnityEditor.MenuItem("Bar Promenade/City/Verify Electrical Bus Clearances")]
        private static void RunCityElectricalBusVerification()
        {
            UnityEditor.SessionState.SetBool(CityElectricalRunResult.Pending,true);
            if(File.Exists(CityElectricalRunResult.OutputPath)) File.Delete(CityElectricalRunResult.OutputPath);
            RunPigeonVerification("BarPromenade.Tests.PlayMode.CityTerracePilotPlayModeTests.LiveCity_EveryBusStopCanBeBoardedFromThePublicPavement");
        }
#endif
        [UnityTest, Explicit("Source continuity, lower streets, every Ridge facade and the shared balcony view.")]
        public IEnumerator CityElectricalNetwork()
        {
            GameSessionState.BeginNewGame("City electrical network");
            Assert.That(GameSessionState.TryStartGameTimeAt(720), Is.True);
            // Fail pure routing immediately, before a scene initialization failure
            // would leave the capture waiting for a root which cannot become ready.
            var planned=CityLayoutCache.GetOrGenerate(CityBlueprintCatalog.Resolve(GameSessionState.CityBlueprintId),
                CityGenerationSettings.Default,GameSessionState.CitySeed);
            CityCoastalInfrastructurePlan.ForLayout(planned);
            CityGameRoot city = null;
            var intrusions = new List<string>();
            yield return Capture(SceneIds.City, () =>
            {
                city = Object.FindAnyObjectByType<CityGameRoot>();
                return city != null && city.IsInitialized ? city : null;
            }, () => ReviewElectricalCity(city, intrusions));
            Assert.That(intrusions, Is.Empty, "Rendered conductors intersect city architecture:\n" + string.Join("\n", intrusions));
            HomeInteriorRoot home = null;
            yield return Capture(SceneIds.HomeInterior, () =>
            {
                home = Object.FindAnyObjectByType<HomeInteriorRoot>();
                return home != null && home.IsInitialized ? home : null;
            }, () => ReviewElectricalBalcony(home));
        }

        private static Shot[] ReviewElectricalCity(CityGameRoot city, List<string> intrusions)
        {
            CityLayout layout = city.Layout;
            var network = CityCoastalInfrastructurePlan.ForLayout(layout);
            var housing = CityHillsideNeighborhoodPlan.ForLayout(layout);
            var expected = housing.AllBuildings.Select(HouseId).ToList();
            Assert.That(RidgeKioskSitePlan.TryCreate(layout, out var kiosk), Is.True);
            Assert.That(CityPawnshopPlan.TryCreate(layout, out _), Is.True);
            Assert.That(CityPigeonLoftPlan.ForLayout(layout).IsEnabled, Is.True);
            Assert.That(CityGlassReturnSitePlan.ForLayout(layout).IsEnabled, Is.True);
            var cobbler = CityCobblerSitePlan.ForLayout(layout);
            Assert.That(cobbler.IsEnabled, Is.True);
            expected.AddRange(new[] { kiosk.StableId, "pawnshop", "ridge-pigeon-loft",
                CityGlassReturnSitePlan.StableId, CityCobblerSitePlan.StableId });
            if (CityHillsideSiteDetails.ForLayout(layout).Any(site => site.Part == CityHillsideSiteDetails.Kind.YardShelter))
                expected.Add("ridge-court-yard-shelter");
            var consumers = CityElectricalConsumerPlan.ForLayout(layout).Consumers;
            Assert.That(consumers.Select(c => c.StableId), Is.EquivalentTo(expected),
                "The electrical survey must include the actually placed structures on both banks.");
            var services = network.ServiceConnections.ToDictionary(s => s.StableId);
            var root = city.World.Root.transform.Find(CityCoastalInfrastructureWorldBuilder.RootName);
            Assert.That(root, Is.Not.Null);
            var wire = ElectricalMesh(root);
            Assert.That(network.Poles.Count, Is.InRange(2, CityCoastalInfrastructurePlan.MaximumPoleCount));
            Assert.That(network.CableSpans.Count, Is.LessThanOrEqualTo(CityCoastalInfrastructurePlan.MaximumCableSpanCount));
            Assert.That(wire.GetComponent<Collider>(), Is.Null);
            var asphalt = CityStreetSupportPlan.ForLayout(layout);
            var blocked = CityBusNetworkFurniture.Reservations(city.BusPlans, layout).ToArray();
            var neighbours = new List<int>[network.Poles.Count];
            for (int i = 0; i < network.Poles.Count; i++)
            {
                neighbours[i] = new List<int>();
                Vector3 pole = network.Poles[i];
                var part = root.Find("Coastal Power Pole " + i);
                Assert.That(part, Is.Not.Null, "Missing physical support at " + pole);
                var solid = part.GetComponent<BoxCollider>().bounds;
                var footprint = Rect.MinMaxRect(solid.min.x, solid.min.z, solid.max.x, solid.max.z);
                Assert.That(Vector3.Distance(part.position, pole), Is.LessThan(.015f));
                Assert.That(blocked.Any(rect => rect.Overlaps(footprint)), Is.False, "A pole blocks a bus platform at " + pole);
            }
            Vector3[] vertices = wire.sharedMesh.vertices;
            Assert.That(vertices.Length % network.CableSpans.Count, Is.Zero);
            int stride = vertices.Length / network.CableSpans.Count;
            for (int i = 0; i < network.CableSpans.Count; i++)
            {
                var span = network.CableSpans[i];
                // Inspect the deformed FBX, including the middle of its sag, in scene metres.
                foreach (float t in new[] { 0f, .5f, 1f })
                {
                    Vector3 point = span.Sample(t);
                    bool found = false;
                    for (int j = i * stride; j < (i + 1) * stride && !found; j++)
                        found = (wire.transform.TransformPoint(vertices[j]) - point).sqrMagnitude < .04f * .04f;
                    Assert.That(found, Is.True, $"Cable {i} is missing its rendered ring at {point}.");
                }
                int steps = Mathf.Max(1, Mathf.CeilToInt(Vector3.Distance(span.Start, span.End) / .5f));
                for (int s = 0; s <= steps; s++)
                {
                    Vector3 p = span.Sample(s / (float)steps);
                    // This shared support labels asphalt as Concrete and a public sidewalk as Stone.
                    if (asphalt.TrySampleTop(new Vector2(p.x, p.z), FootstepGroundKind.Concrete, out float road))
                        Assert.That(p.y - road, Is.GreaterThanOrEqualTo(CityCoastalInfrastructurePlan.MinimumVehicleWireHeight - .01f),
                            $"Cable {i} over the carriageway at {p}.");
                }
                if (span.IsServiceDrop) continue;
                int a = SupportAt(span.Start), b = SupportAt(span.End);
                Assert.That(a, Is.Not.EqualTo(b), "A trunk span must join two poles.");
                neighbours[a].Add(b); neighbours[b].Add(a);
            }
            var source = network.Facilities.Single(f => f.StableId == "coastal-substation");
            Assert.That(network.CableSpans.Any(s => s.IsServiceDrop &&
                (s.Start - (network.Poles[0] + Vector3.up * CityCoastalInfrastructurePlan.PoleCableHeight)).sqrMagnitude < .5f &&
                (s.End - source.Position).sqrMagnitude < source.Size.sqrMagnitude), Is.True, "Missing physical substation feed.");
            var reached = new HashSet<int> { 0 };
            var pending = new Queue<int>(); pending.Enqueue(0);
            while (pending.Count != 0)
                foreach (int next in neighbours[pending.Dequeue()])
                    if (reached.Add(next)) pending.Enqueue(next);
            Assert.That(reached.Count, Is.EqualTo(network.Poles.Count), "An electrically isolated pole was built.");

            var inlets = root.Cast<Transform>().Where(t => t.name.StartsWith("Coastal Facade Inlet", StringComparison.Ordinal)).ToArray();
            foreach (var consumer in consumers)
            {
                Assert.That(services.ContainsKey(consumer.StableId), Is.True, "Unserved facade: " + consumer.StableId);
                var service = services[consumer.StableId];
                if (!string.IsNullOrEmpty(consumer.SuppliedById))
                {
                    Assert.That(service.SuppliedById, Is.EqualTo(CityCobblerWorkerPlan.ResidenceId));
                    Assert.That(services.ContainsKey(consumer.SuppliedById), Is.True, consumer.SuppliedById);
                    Assert.That(service.Start, Is.EqualTo(cobbler.Yard.CableStart));
                    Assert.That(service.End, Is.EqualTo(cobbler.Yard.CableEnd));
                    var existing = city.World.Root.GetComponentInChildren<CityCobblerYardWorld>(true).transform.Find("FeedCable");
                    Assert.That(existing.GetComponentsInChildren<MeshRenderer>().Any(r => r.bounds.SqrDistance(service.Start) < .04f &&
                        r.bounds.SqrDistance(service.End) < .04f), Is.True, "The workshop needs its original fitted wall feed.");
                    continue;
                }
                if(service.SuppliedById==null) SupportAt(service.Start);
                else
                {
                    Assert.That(services.ContainsKey(service.SuppliedById), Is.True, "Unpowered supplying house.");
                    Assert.That(root.Find("Coastal Supply Outlet " + service.StableId), Is.Not.Null);
                    var supplier=consumers.Single(c=>c.StableId==service.SuppliedById);
                    Assert.That(supplier.Attachments.Any(a=>(a.Position-service.Start).sqrMagnitude<.001f),Is.True);
                }
                Assert.That(consumer.Attachments.Any(a => Vector3.Distance(a.Position, service.End) < .01f &&
                    Vector3.Dot(a.Outward, service.Rotation * Vector3.forward) > .999f), Is.True, consumer.StableId);
                Assert.That(network.CableSpans.Count(s => s.IsServiceDrop && s.Start == service.Start && s.End == service.End),
                    Is.EqualTo(1), consumer.StableId + " must have one visible service cable.");
                var inlet = inlets.SingleOrDefault(t => (t.position - service.End).sqrMagnitude < .45f * .45f);
                Assert.That(inlet, Is.Not.Null, consumer.StableId + " has a cable ending in air.");
                Assert.That(inlet.GetComponentInChildren<MeshRenderer>(), Is.Not.Null);
                Assert.That(Quaternion.Angle(inlet.rotation, service.Rotation), Is.LessThan(.1f), consumer.StableId);
            }
            var shots = new List<Shot>();
            foreach (var district in new[] { CityDistrictKind.Residential, CityDistrictKind.Industrial })
            {
                var lot = layout.BuildingLots.FirstOrDefault(l => l.IsOrdinaryBuilding && l.District == district &&
                    services.ContainsKey($"city:{l.Cell.x}:{l.Cell.y}"));
                Assert.That(lot, Is.Not.Null, district + " lost its lower-city service branch.");
                var service = services[$"city:{lot.Cell.x}:{lot.Cell.y}"];
                Assert.That(lot.WorldBounds.SqrDistance(service.End), Is.LessThan(1f), "Lower-city cable misses its receiver.");
                if(service.SuppliedById==null) SupportAt(service.Start);
                else
                {
                    Assert.That(services.ContainsKey(service.SuppliedById), Is.True, "Unpowered supplying house.");
                    Assert.That(root.Find("Coastal Supply Outlet " + service.StableId), Is.Not.Null);
                    var supplier=consumers.Single(c=>c.StableId==service.SuppliedById);
                    Assert.That(supplier.Attachments.Any(a=>(a.Position-service.Start).sqrMagnitude<.001f),Is.True);
                }
                var eye = ReplanningStreetEye(layout, lot.SidewalkArrivalPosition + lot.FacadeForward * 4f);
                shots.Add(Shot.At("electrical-lower-" + district, eye, Vector3.Lerp(service.End, service.Start, .35f), 68f));
            }
            foreach (var bank in new[] { CityTerraceTransportBank.West, CityTerraceTransportBank.East })
            {
                var street = CityHillsideStreetPlan.ForLayout(layout).Routes.Single(r => r.Bank == bank);
                Vector3 mouth = layout.GetNodeWorldPosition(street.MouthNode);
                Assert.That(network.Poles.Any(p => Vector2.Distance(new Vector2(p.x, p.z), new Vector2(mouth.x, mouth.z)) <= 52f),
                    Is.True, "Missing connection near the " + bank + " climb.");
                var sides = housing.Buildings.Where(h => h.Bank == bank).OrderBy(h => h.StreetContactWorld.y).ToArray();
                var house = sides[sides.Length / 2];
                var service = services[HouseId(house)];
                shots.Add(Shot.At("electrical-ascent-" + bank, house.StreetContactWorld + Vector3.up * EyeHeight,
                    Vector3.Lerp(service.End, service.Start, .4f), 75f));
            }
            var summit = layout.SummitHome;
            shots.Add(Shot.At("electrical-summit-facade", summit.StreetContactWorld + Vector3.up * EyeHeight,
                services[HouseId(summit)].End, 65f));
            AuditElectricalGeometry(city, network, wire, vertices, stride, intrusions);
            var reportedBank = CityHillsideStreetPlan.ForLayout(layout).Routes.Single(r => r.Bank == CityTerraceTransportBank.West);
            var reportedEye = new Vector3(-72.7f, 41.72f, -347.7f);
            var target = new Vector3(-68.7f, 48f, -355f);
            shots.Add(Shot.At("electrical-reported-west-crest", reportedEye, target, 72f));
            var otherEye = new Vector3(reportedEye.x - 14f, reportedBank.SampleLaneTop(CityHillsideStreetRoute.LaneCount - 1, reportedEye.x - 14f)
                + EyeHeight, reportedBank.GetLaneZ(CityHillsideStreetRoute.LaneCount - 1) + 4f);
            shots.Add(Shot.At("electrical-reported-west-crest-profile", otherEye, target, 65f));
            return shots.ToArray();

            int SupportAt(Vector3 point)
            {
                for (int i = 0; i < network.Poles.Count; i++)
                    if ((network.Poles[i] + Vector3.up * CityCoastalInfrastructurePlan.PoleCableHeight - point).sqrMagnitude <= .68f * .68f)
                        return i;
                Assert.Fail("No pole crossarm under conductor at " + point);
                return -1;
            }
            static string HouseId(CityHillsideBuilding h) => $"ridge-{(h.Bank == CityTerraceTransportBank.East ? "east" : "west")}-house:{h.Lot.Cell.x}:{h.Lot.Cell.y}";
        }

        private static MeshFilter ElectricalMesh(Transform root)
        {
            var conductor = root.Find("Coastal Electrical Conductors");
            Assert.That(conductor, Is.Not.Null, "Missing rendered cable network.");
            var filter = conductor.GetComponent<MeshFilter>();
            Assert.That(filter.sharedMesh, Is.Not.Null);
            return filter;
        }

        private sealed class ElectricalAuditLine
        {
            internal readonly string Name, StartOwner, EndOwner;
            internal readonly Vector3[] Rings;
            internal readonly Bounds Bounds;
            internal readonly Bounds[] Segments;
            internal readonly float Radius;

            internal ElectricalAuditLine(string name, string start, string end, Vector3[] rings, float radius)
            {
                Name = name; StartOwner = start; EndOwner = end; Rings = rings; Radius = radius;
                var total = new Bounds(rings[0], Vector3.zero);
                Segments = new Bounds[rings.Length - 1];
                for (int i = 0; i < Segments.Length; i++)
                {
                    var segment = new Bounds(rings[i], Vector3.zero);
                    segment.Encapsulate(rings[i + 1]); segment.Expand(2f * radius + .001f);
                    Segments[i] = segment; total.Encapsulate(segment);
                }
                Bounds = total;
            }
        }

        private static void AuditElectricalGeometry(CityGameRoot city, CityCoastalInfrastructurePlan plan,
            MeshFilter conductors, Vector3[] vertices, int stride, List<string> intrusions)
        {
            var lines = new List<ElectricalAuditLine>(plan.CableSpans.Count + 1);
            var world = city.World.Root.transform;
            for (int i = 0; i < plan.CableSpans.Count; i++)
            {
                var span = plan.CableSpans[i];
                Vector3 along = Vector3.ProjectOnPlane(span.End - span.Start, Vector3.up);
                Assert.That(along.sqrMagnitude, Is.GreaterThan(.0001f));
                var ringBounds = new Bounds[17];
                var seen = new bool[17];
                for (int v = i * stride; v < (i + 1) * stride; v++)
                {
                    Vector3 p = conductors.transform.TransformPoint(vertices[v]);
                    int ring = Mathf.RoundToInt(16f * Vector3.Dot(p - span.Start, along) / along.sqrMagnitude);
                    Assert.That(ring, Is.InRange(0, 16));
                    if (seen[ring]) ringBounds[ring].Encapsulate(p);
                    else { seen[ring] = true; ringBounds[ring] = new Bounds(p, Vector3.zero); }
                }
                Assert.That(seen.All(b => b), Is.True, "Missing a rendered conductor ring on span " + i);
                var rings = ringBounds.Select(b => b.center).ToArray();
                var service = plan.ServiceConnections.FirstOrDefault(s => s.Start == span.Start && s.End == span.End);
                lines.Add(new ElectricalAuditLine("span " + i + " " + (service.StableId ?? "trunk"), service.SuppliedById,
                    service.StableId ?? (i == 0 ? "coastal-substation" : null), rings, .015f));
            }
            // The existing cobbler lead belongs to the fitted yard, outside the combined mesh.
            // Its 12 independently authored tube sections share the single painted FBX with metal end fittings.
            var lead = world.GetComponentInChildren<CityCobblerYardWorld>(true).transform.Find("FeedCable");
            var leadFilter = lead.GetComponentsInChildren<MeshFilter>().Single(f => f.name == "FeedCable__Paint");
            var points = ReadLocalAcceptanceVertices(leadFilter.sharedMesh).Select(leadFilter.transform.TransformPoint).ToArray();
            var ends = lead.GetComponentsInChildren<Transform>();
            Vector3 start = ends.Single(t => t.name == "ANCHOR_FeedStart").position;
            Vector3 end = ends.Single(t => t.name == "ANCHOR_FeedEnd").position;
            var joints = new Vector3[13]; joints[0] = start; joints[12] = end;
            Vector3 axis = end - start;
            for (int j = 1; j < 12; j++)
            {
                float station = j / 12f;
                var ring = points.Where(p => Mathf.Abs(Vector3.Dot(p - start, axis) / axis.sqrMagnitude - station) < .025f).ToArray();
                Assert.That(ring.Length, Is.GreaterThanOrEqualTo(7), "Missing fitted cobbler lead joint " + j);
                var bounds = new Bounds(ring[0], Vector3.zero);
                foreach (Vector3 p in ring) bounds.Encapsulate(p);
                joints[j] = bounds.center;
            }
            lines.Add(new ElectricalAuditLine("cobbler fitted feed", CityCobblerWorkerPlan.ResidenceId,
                CityCobblerSitePlan.StableId, joints, .010f));

            var owners = new Dictionary<Transform, string>();
            var housing = CityHillsideNeighborhoodPlan.ForLayout(city.Layout);
            foreach (var house in housing.AllBuildings)
            {
                if (house.Lot.IsPlayerHome) { Require("Player Home", CityElectricalConsumerPlan.HouseId(house)); continue; }
                string bank = housing.SharedCourtBuildings.Contains(house) ? "Shared passage court" :
                    house.Bank == CityTerraceTransportBank.West ? "Stepped Residences" : "Balcony Courts";
                Require(bank + "/Building " + house.Lot.Cell, CityElectricalConsumerPlan.HouseId(house));
            }
            // A primary lot and a rear wing can share the same cell and GameObject name.
            // Enumerate every placed root, including both, instead of Transform.Find on the first name.
            foreach (Transform child in world)
            {
                if (child.name.StartsWith("Building ", StringComparison.Ordinal))
                {
                    var lot = city.Layout.BuildingLots.FirstOrDefault(l => child.name == $"Building {l.Cell.x}-{l.Cell.y}");
                    owners[child] = lot == null ? child.name : $"city:{lot.Cell.x}:{lot.Cell.y}";
                }
                else if (child.name.StartsWith("Bar ", StringComparison.Ordinal) ||
                         child.name.StartsWith("Kiosk ", StringComparison.Ordinal)) owners[child] = child.name;
            }
            Require("Ridge Kiosk", "ridge-kiosk");
            Require(CityPawnshopWorld.RootName, "pawnshop");
            Require("Ridge Pigeon Loft", "ridge-pigeon-loft");
            Require("Ridge Glass Return", CityGlassReturnSitePlan.StableId);
            Require("Ridge Cobbler Workshop", CityCobblerSitePlan.StableId);
            if (plan.ServiceConnections.Any(s => s.StableId == "ridge-court-yard-shelter"))
                Require("Residential terrace edges/RidgeYardShelter", "ridge-court-yard-shelter");
            foreach (var facility in plan.Facilities)
                Require(CityCoastalInfrastructureWorldBuilder.RootName + "/" + facility.StableId, facility.StableId);
            foreach (string rootName in new[] { "Supermarket", CityChurchWorldBuilder.RootName, "City Diner", "City Fair",
                "Working Fish Cannery", "Working Fishing Port", CityDistrictPointOfInterestWorldBuilder.CityRootName,
                CitySeacoastWorldBuilder.RootName, CityArchShelterWorldBuilder.RootName, CityResidentialCourtyardWorldBuilder.RootName,
                CityEastExitWorldBuilder.RootName, "Authored Fringe Yards", "City Bus Stops" })
            {
                Transform optional = world.Find(rootName);
                if (optional != null) owners.TryAdd(optional, rootName);
            }
            var meshCache = new Dictionary<Mesh, (Vector3[] local, int[] triangles)>();
            int tested = 0;
            foreach (var owner in owners)
            foreach (var renderer in owner.Key.GetComponentsInChildren<MeshRenderer>())
            {
                if (!renderer.enabled || renderer.transform.IsChildOf(lead) ||
                    renderer.name.StartsWith("COL_", StringComparison.Ordinal)) continue;
                var filter = renderer.GetComponent<MeshFilter>();
                if (filter == null || filter.sharedMesh == null) continue;
                var relevant = lines.Where(line => line.Bounds.Intersects(renderer.bounds)).ToArray();
                if (relevant.Length == 0) continue;
                var mesh = filter.sharedMesh;
                if (!meshCache.TryGetValue(mesh, out var geometry))
                {
                    int[] indices;
#if UNITY_EDITOR
                    using (var data = UnityEditor.MeshUtility.AcquireReadOnlyMeshData(mesh))
                    {
                        var all = new List<int>();
                        for (int sub = 0; sub < mesh.subMeshCount; sub++)
                        {
                            var descriptor = data[0].GetSubMesh(sub);
                            if (descriptor.topology != MeshTopology.Triangles) continue;
                            using var triangles = new NativeArray<int>(descriptor.indexCount, Allocator.Temp);
                            data[0].GetIndices(triangles, sub, true); all.AddRange(triangles.ToArray());
                        }
                        indices = all.ToArray();
                    }
#else
                    indices = mesh.triangles;
#endif
                    geometry = (ReadLocalAcceptanceVertices(mesh), indices);
                    meshCache.Add(mesh, geometry);
                }
                var matrix = filter.transform.localToWorldMatrix;
                var placed = new Vector3[geometry.local.Length];
                for (int v = 0; v < placed.Length; v++) placed[v] = matrix.MultiplyPoint3x4(geometry.local[v]);
                foreach (var line in relevant)
                {
                    bool hit = false;
                    for (int seg = 0; seg < line.Segments.Length && !hit; seg++)
                    {
                        if (!line.Segments[seg].Intersects(renderer.bounds)) continue;
                        Vector3 a = line.Rings[seg], b = line.Rings[seg + 1];
                        if (seg == 0 && owner.Value == line.StartOwner) a = Vector3.MoveTowards(a, b, .03f);
                        if (seg == line.Segments.Length - 1 && owner.Value == line.EndOwner) b = Vector3.MoveTowards(b, a, .03f);
                        for (int k = 0; k < geometry.triangles.Length && !hit; k += 3)
                        {
                            var triangle = new CombatHurtboxes.HeadTriangle(placed[geometry.triangles[k]],
                                placed[geometry.triangles[k + 1]], placed[geometry.triangles[k + 2]]);
                            if (!line.Segments[seg].Intersects(triangle.Bounds)) continue;
                            tested++;
                            if (!triangle.FirstContact(a, b - a, line.Radius, out float fraction)) continue;
                            var part = renderer.transform;
                            string path = part.name;
                            while (part != owner.Key) { part = part.parent; path = part.name + "/" + path; }
                            intrusions.Add($"{line.Name} {line.Rings[0]:F2}→{line.Rings[line.Rings.Length-1]:F2}, " +
                                $"section {seg}, hit {Vector3.Lerp(a,b,fraction):F3}: {owner.Value} / {path}, triangle {k/3}.");
                            hit = true;
                        }
                    }
                }
            }
            Assert.That(tested, Is.GreaterThan(0), "No architectural triangles reached the conductor audit.");
            Debug.Log($"City electrical surface check: {lines.Count} fitted wires, {owners.Count} placed roots, {tested} nearby triangles, {intrusions.Count} contacts.");

            void Require(string path, string id)
            {
                var found = world.Find(path);
                Assert.That(found, Is.Not.Null, $"Missing architectural owner {id} at {path}.");
                owners.Add(found, id);
            }
        }

        private static Shot[] ReviewElectricalBalcony(HomeInteriorRoot home)
        {
            var root = home.ExteriorView.Find("Home " + CityCoastalInfrastructureWorldBuilder.RootName);
            Assert.That(root, Is.Not.Null);
            var network = CityCoastalInfrastructurePlan.ForLayout(home.ExteriorContext.Layout);
            var wire = ElectricalMesh(root);
            Assert.That(wire.sharedMesh.vertexCount, Is.GreaterThan(0));
            var eye = new Vector3(6.2f, EyeHeight, -1.45f);
            var poles = root.Cast<Transform>().Where(t => t.name.StartsWith("Exterior Power Pole ", StringComparison.Ordinal)).ToArray();
            Assert.That(poles, Is.Not.Empty, "There must be an observable support near the summit home.");
            var near = poles.OrderBy(p => Vector3.Distance(eye, p.localPosition)).First();
            foreach (var pole in poles)
            {
                Assert.That(network.Poles.Any(p => Vector3.Distance(pole.localPosition,
                    PlayerHomeBalconyGeometry.ToHomeLocal(home.ExteriorContext.PlayerHome, p)) < .01f), Is.True, pole.name);
                Assert.That(pole.GetComponentInChildren<Collider>(), Is.Null);
            }
            return new[] { Shot.At("electrical-balcony", eye, near.localPosition + Vector3.up * CityCoastalInfrastructurePlan.PoleCableHeight, 72f) };
        }
    }
}

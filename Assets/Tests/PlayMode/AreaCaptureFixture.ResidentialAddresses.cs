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
    public sealed partial class AreaCaptureFixture
    {
        [UnityTest, Timeout(480000), Explicit("Compare modular addresses in the street, from Home and in the Alpine village.")]
        public IEnumerator WorldResidentialAddresses()
        {
            GameSessionState.BeginNewGame("residential-addresses");
            try
            {
                GameSessionState.TryStartGameTimeAt(12 * 60);
                var catalog = Resources.Load<GameObject>("Housing/FrontageModules");
                Assert.That(catalog.GetComponentsInChildren<MeshFilter>().Length, Is.EqualTo(19));
                CheckPetuniaOrientation(catalog.transform.Find("RailPetunias").GetComponent<MeshFilter>());
                var source = catalog.transform.Find("WindowKitchen").GetComponent<MeshFilter>();
                Bounds sill = source.sharedMesh.bounds;
                var authored = new Bounds(source.transform.TransformPoint(sill.center), Vector3.zero);
                foreach (var vertex in source.sharedMesh.vertices) authored.Encapsulate(source.transform.TransformPoint(vertex));
                Assert.That(authored.max.y, Is.InRange(.98f, 1.03f), "authored window height in metres");
                Assert.That(authored.max.z, Is.InRange(.24f, .28f), "sill faces out from the glass");

                yield return SceneManager.LoadSceneAsync(SceneIds.City);
                CityGameRoot city = null;
                yield return WaitFor(() => (city = Object.FindAnyObjectByType<CityGameRoot>()) && city.IsInitialized, "City");
                var stock = Object.FindObjectsByType<CityBuildingAssetRegistry>(FindObjectsSortMode.None)
                    .Where(r => r.District == CityDistrictKind.Residential).ToArray();
                Assert.That(stock.Length, Is.GreaterThan(3));
                foreach (var model in stock) CheckFrontage(model.transform);
                var lots = city.Layout.BuildingMasses.Concat(CityHillsideNeighborhoodPlan.ForLayout(city.Layout)
                    .AllBuildings.Select(h => h.Lot)).ToArray();
                // Wings can share a cell name. Use the per-lot window schedule
                // identity already carried by the instantiated registry.
                var addresses = lots.Where(ResidentialAddress.Applies)
                    .ToLookup(l => new ResidentialAddress(l, city.Layout.Seed).Key ^ 0x9e3779b9u);
                BuildingLot LotFor(CityBuildingAssetRegistry model) => addresses[model.WindowScheduleSeed].First();
                ResidentialAddress Address(CityBuildingAssetRegistry model) => new ResidentialAddress(LotFor(model), city.Layout.Seed);
                CheckRailFlowerBoxes(stock, LotFor, city.Layout.Seed);
                var repeated = stock.GroupBy(r => r.StableId).OrderByDescending(g => g.Count()).First().ToArray();
                var examples = repeated.GroupBy(r => Address(r).Family).Select(g => g.First())
                    .Concat(repeated).Distinct().Take(3).ToArray();
                Assert.That(examples.Length, Is.EqualTo(3));
                var signatures = new HashSet<string>();
                for (int i = 0; i < examples.Length; i++)
                {
                    var model = examples[i];
                    signatures.Add(FrontageSignature(model.transform));
                    var address = Address(model);
                    var deck = model.BalconySlots.Where(b => b.Floor == 2).ElementAt((int)(address.Key & 1));
                    CaptureAddress(model.transform, deck.LocalNpcDock, "street-" + i);
                    var open = model.BalconySlots.Where(b => b.Floor == 2).ElementAt((int)(1 - (address.Key & 1)));
                    CaptureAddress(model.transform, open.LocalNpcDock + Vector3.up * .60f,
                        "balcony-" + i, 3.4f, -.55f);
                    if (i == 0)
                    {
                        var panes = model.WindowSlots.Where(p => p.Side == "Front" && p.Floor == 0 &&
                            (model.GroundedWindowMask & (1UL << p.Uv2SlotId)) == 0);
                        var frontWindow = panes.OrderBy(p => address.Household(p)).First();
                        CaptureAddress(model.transform, frontWindow.LocalCenter, "street-window", 3.2f);
                    }
                }
                Assert.That(signatures.Count, Is.GreaterThan(1), "same shell, different addresses");
                var known = new Dictionary<uint,string>();
                foreach (var model in stock.Where(r => r.GroundedWindowMask == 0))
                    known[model.WindowScheduleSeed] = FrontageSignature(model.transform);
                string permanent = FrontageSignature(examples[0].transform);
                AdvanceNightGradeClock(22 * 60);
                city.DayNight.ApplyCurrentTime(true);
                CaptureAddress(examples[0].transform, examples[0].BalconySlots.First(b => b.Floor == 2).LocalNpcDock, "street-night");
                Assert.That(FrontageSignature(examples[0].transform), Is.EqualTo(permanent));

                GameSessionState.PrepareHomeArrival(HomeArrivalKind.Normal);
                GameSessionState.EnterHome();
                yield return SceneManager.LoadSceneAsync(SceneIds.HomeInterior);
                HomeInteriorRoot home = null;
                yield return WaitFor(() => (home = Object.FindAnyObjectByType<HomeInteriorRoot>()) && home.IsInitialized, "Home");
                var neighbours = home.GetComponentsInChildren<CityBuildingAssetRegistry>(true)
                    .Where(r => r.District == CityDistrictKind.Residential).ToArray();
                Assert.That(neighbours, Is.Not.Empty);
                foreach (var model in neighbours)
                {
                    CheckFrontage(model.transform);
                    if (model.GroundedWindowMask == 0 &&
                        known.TryGetValue(model.WindowScheduleSeed, out var signature))
                        Assert.That(FrontageSignature(model.transform), Is.EqualTo(signature), model.transform.parent.name);
                }
                var neighbour = neighbours.First(r => r.transform.parent.name.StartsWith("Building ("));
                var lot = CityHillsideNeighborhoodPlan.ForLayout(home.ExteriorContext.Layout).AllBuildings
                    .Select(h => h.Lot).First(l => (new ResidentialAddress(l, home.ExteriorContext.Layout.Seed).Key ^ 0x9e3779b9u) == neighbour.WindowScheduleSeed);
                // Recompose the City version of this exact address, including
                // when the other district was streamed out during the street walk.
                var reference = new GameObject("Reference city address");
                var replica = CityBuildingPrototypeWorldBuilder.BuildCity(reference.transform, lot, home.ExteriorContext.Layout.Seed, 1f, home.ExteriorContext.Layout);
                Assert.That(replica.RailFlowerBoxMask, Is.EqualTo(neighbour.RailFlowerBoxMask));
                if (neighbour.GroundedWindowMask != 0)
                {
                    replica.GroundedWindowMask = neighbour.GroundedWindowMask;
                    CityResidentialFrontages.RebuildMasked(replica, lot, home.ExteriorContext.Layout.Seed);
                }
                Assert.That(FrontageSignature(replica.transform), Is.EqualTo(FrontageSignature(neighbour.transform)));
                Object.Destroy(reference);

                var heroRoot = new GameObject("Reference hero house");
                var hero = CityPlayerHomeExteriorWorldBuilder.BuildCity(heroRoot.transform, home.ExteriorContext.PlayerHome);
                CheckFrontage(hero.transform);
                Object.Destroy(heroRoot);

                yield return SceneManager.LoadSceneAsync(SceneIds.AlpineVillage);
                AlpineVillageRoot village = null;
                yield return WaitFor(() => (village = Object.FindAnyObjectByType<AlpineVillageRoot>()) && village.IsInitialized, "Village");
                var houses = village.GetComponentsInChildren<VillageHouseholdIdentity>(true);
                CollectionAssert.AreEquivalent(VillageHouseholdCatalog.Houses.Select(h => h.HouseId), houses.Select(h => h.HouseId));
                foreach (var house in houses) CheckFrontage(house.transform);
                foreach (string id in new[] { "village-house-04", "village-mothers-house", "homestead-01", "homestead-11" })
                {
                    Transform house = houses.Single(h => h.HouseId == id).transform;
                    CaptureAddress(house, id.StartsWith("village") ? new Vector3(0, 2, 3) :
                        new Vector3(0, 1, 3), id);
                }
            }
            finally { GameSessionState.BeginNewGame(); }
            yield return SceneManager.LoadSceneAsync(SceneIds.MainMenu);
        }

        private static void CheckPetuniaOrientation(MeshFilter filter)
        {
            // Only the small closed base of a white corolla has triangle
            // edges under 3 cm. Its outward normal points away from the mouth.
            var mesh = filter.sharedMesh;
            var positions = mesh.vertices;
            var normals = mesh.normals;
            var uv = mesh.uv;
            var triangles = mesh.triangles;
            int baseFaces = 0;
            for (int i = 0; i < triangles.Length; i += 3)
            {
                int a = triangles[i], b = triangles[i + 1], c = triangles[i + 2];
                if (Mathf.FloorToInt(uv[a].x * 8) != 7 || Mathf.FloorToInt(uv[a].y * 4) != 2) continue;
                Vector3 ab = filter.transform.TransformVector(positions[b] - positions[a]);
                Vector3 ac = filter.transform.TransformVector(positions[c] - positions[a]);
                if (Mathf.Max(ab.magnitude, ac.magnitude, (ab - ac).magnitude) > .030f) continue;
                Assert.That(filter.transform.TransformDirection(normals[a]).normalized.y, Is.LessThan(-.98f),
                    "White flowers open toward the sky, including the ones on the trailing stems");
                baseFaces++;
            }
            Assert.That(baseFaces, Is.GreaterThan(40));
        }

        private static void CheckRailFlowerBoxes(CityBuildingAssetRegistry[] stock,
            System.Func<CityBuildingAssetRegistry, BuildingLot> lotFor, int seed)
        {
            var seen = new HashSet<string>();
            foreach (var model in stock.Where(m => m.RailFlowerBoxMask != 0))
            {
                var lot = lotFor(model);
                var address = new ResidentialAddress(lot, seed);
                foreach (var deck in model.BalconySlots)
                {
                    ulong bit = 1UL << deck.DoorSlotId;
                    if ((model.RailFlowerBoxMask & bit) == 0 || (model.GroundedWindowMask & (bit | bit << 1)) != 0) continue;
                    var pane = model.WindowSlots.Single(p => p.SlotId == deck.DoorSlotId + 1);
                    Assert.That(deck.Floor, Is.GreaterThan(1));
                    Assert.That(address.Family == 1 && ((pane.Bay / 2 & 1) == (int)(address.Key & 1)), Is.False);
                    uint household = address.Household(pane);
                    string name = (household & (1u << 20)) == 0 ? "geraniums" : "petunias";
                    if (!seen.Add(name)) continue;

                    var anchor = CityResidentialFrontages.FlowerBoxAnchor(deck, household);
                    CaptureAddress(model.transform, anchor, name, 3.1f, -.22f);
                    if (name == "petunias") CaptureAddress(model.transform, anchor, "petunias-above", 1.05f, -1.55f);
                    CaptureAddress(model.transform, anchor, name + "-street", 9f, anchor.y - EyeHeight);
                    string original = FrontageSignature(model.transform);
                    ulong occupied = model.RailFlowerBoxMask;
                    try
                    {
                        model.RailFlowerBoxMask &= ~bit;
                        CityResidentialFrontages.RebuildMasked(model, lot, seed);
                        Assert.That(FrontageSignature(model.transform), Is.Not.EqualTo(original), name);
                        CaptureAddress(model.transform, anchor, name + "-bare", 3.1f, -.22f);
                    }
                    finally
                    {
                        model.RailFlowerBoxMask = occupied;
                        CityResidentialFrontages.RebuildMasked(model, lot, seed);
                    }
                    Assert.That(FrontageSignature(model.transform), Is.EqualTo(original));
                }
            }
            CollectionAssert.AreEquivalent(new[] { "geraniums", "petunias" }, seen);
        }

        private static void CheckFrontage(Transform house)
        {
            var root = house.Find(ResidentialFrontage.RootName);
            Assert.That(root, Is.Not.Null, house.name);
            Assert.That(root.GetComponentsInChildren<MeshRenderer>().Length, Is.InRange(1, 2));
            Assert.That(root.GetComponentsInChildren<Collider>(), Is.Empty);
        }

        private static string FrontageSignature(Transform house)
        {
            var root = house.Find(ResidentialFrontage.RootName);
            return string.Join("/", root.GetComponentsInChildren<MeshFilter>().Select(f =>
            {
                // Hash the actual vertex stream, including the arrangement
                // of furnishings with equal vertex counts or equal bounds.
                var mesh = f.sharedMesh;
                using var buffer = mesh.GetVertexBuffer(0);
                var bytes = new byte[mesh.vertexCount * mesh.GetVertexBufferStride(0)];
                buffer.GetData(bytes);
                uint hash = 2166136261u;
                foreach (byte b in bytes) hash = unchecked((hash ^ b) * 16777619u);
                return hash.ToString("X8");
            }));
        }

        private static void CaptureAddress(Transform house, Vector3 localTarget, string label, float distance = 7.5f, float eyeDrop = 3.4f)
        {
            var camera = Camera.main;
            var saved = new DefaultNpcCameraState(camera);
            try
            {
                var target = house.TransformPoint(localTarget);
                var eye = house.TransformPoint(new Vector3(localTarget.x + distance * .19f,
                    Mathf.Max(1.7f, localTarget.y - eyeDrop), localTarget.z + distance));
                camera.transform.SetPositionAndRotation(eye, Quaternion.LookRotation(target - eye));
                CaptureCurrentCamera(camera, "ResidentialAddresses", label);
            }
            finally { saved.Restore(camera); }
        }
    }
}

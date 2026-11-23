using System.Collections;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace BarPromenade.Tests.PlayMode
{
    public sealed partial class AreaCaptureFixture
    {
        [UnityTest, Explicit("Trace every municipal connection and inspect both streamed districts at eye level.")]
        public IEnumerator CityWaterNetworkReachesEveryBuildingAndTheSea()
        {
            GameSessionState.BeginNewGame("Municipal water");
            GameSessionState.TryStartGameTimeAt(720);
            var layout = CityLayoutCache.GetOrGenerate(CityBlueprintCatalog.Resolve(GameSessionState.CityBlueprintId),
                CityGenerationSettings.Default,GameSessionState.CitySeed);
            var network = CityWaterNetwork.ForLayout(layout);
            var sites = network.Services.ToDictionary(s => s.Id);
            var ridge = CityElectricalConsumerPlan.ForLayout(layout);
            foreach (var consumer in ridge.Consumers) Assert.That(sites.ContainsKey(consumer.StableId),Is.True,consumer.StableId);
            foreach (var id in new[] {"cannery","port-warehouse","church","laundry","supermarket","boat-station","city-diner"})
                Assert.That(sites.ContainsKey(id),Is.True,id);
            Assert.That(sites.Count,Is.GreaterThan(layout.BuildingMasses.Count(l => l.HasBuilding)+ridge.Consumers.Count));
            var supply = new List<Vector3>(); var sewer = new List<Vector3>();
            var gravity = new HashSet<(Vector3,Vector3)>();
            var pressure = new HashSet<(Vector3,Vector3)>();
            var drains = new HashSet<(Vector3,Vector3)>();
            foreach (var pipe in network.Pipes)
            {
                if (pipe.Kind == CityWaterPipeKind.Supply) { pressure.Add((pipe.From,pipe.To)); pressure.Add((pipe.To,pipe.From)); }
                if (pipe.Kind == CityWaterPipeKind.GravitySewer)
                { Assert.That(pipe.From.y,Is.GreaterThan(pipe.To.y)); gravity.Add((pipe.From,pipe.To)); }
                if (pipe.Kind == CityWaterPipeKind.ForceMain || pipe.Kind == CityWaterPipeKind.CoastalDischarge)
                    drains.Add((pipe.From,pipe.To));
            }
            foreach (var site in network.Services)
            {
                network.Trace(site,supply,sewer);
                if (site.OnRidge) CollectionAssert.Contains(supply,network.Booster,site.Id);
                Assert.That(supply[supply.Count-1],Is.EqualTo(site.Inlet));
                Assert.That(sewer[0],Is.EqualTo(site.Drain));
                Assert.That(Vector3.Distance(supply[0],network.Header),Is.LessThan(2f));
                Assert.That(Vector3.Distance(sewer[sewer.Count-1],network.Outfall),Is.LessThan(1f));
                for (int i=1;i<supply.Count;i++) Assert.That(pressure.Contains((supply[i-1],supply[i])),Is.True,site.Id);
                for (int i=1;i<sewer.Count;i++) Assert.That(gravity.Contains((sewer[i-1],sewer[i])) ||
                    drains.Contains((sewer[i-1],sewer[i])),Is.True,site.Id);
            }
            Assert.That(network.Fixtures.Count(f=>f.Kind==CityWaterKit.VentGrille),Is.EqualTo(2));
            var viewIssues = new List<string>();
            bool moved=false;
            CityGameRoot city=null;
            yield return Capture(SceneIds.City,() =>
            {
                city = Object.FindAnyObjectByType<CityGameRoot>();
                if (city == null || !city.IsInitialized) return null;
                if (!moved)
                {
                    var mouth = CityHillsideStreetPlan.ForLayout(city.Layout).Routes[0].MouthNode;
                    city.Player.Motor.Teleport(city.Layout.GetNodeWorldPosition(mouth)+Vector3.up*.65f);
                    moved=true;
                }
                // A direct load builds both districts in one synchronous world.
                // Travel arrivals own separate, independently ready district roots.
                return Object.FindAnyObjectByType<CityWaterOutfall>() != null &&
                    Object.FindObjectsByType<CityWaterVent>().Length == 2 ? city : null;
            },() =>
            {
                var real = CityWaterNetwork.ForLayout(city.Layout);
                var outfall = Object.FindAnyObjectByType<CityWaterOutfall>();
                var head = outfall.transform.parent.Find(nameof(CityWaterKit.OutfallHead));
                Assert.That(head.GetComponentsInChildren<MeshCollider>().Length,Is.GreaterThanOrEqualTo(2));
                var water = outfall.GetComponentInChildren<MeshFilter>();
                Assert.That(water.GetComponent<Renderer>().sharedMaterial.shader.name,Is.EqualTo("Bar Promenade/City Sewer Discharge"));
                Assert.That(water.sharedMesh.bounds.min.z,Is.LessThan(0f),"Flow begins inside the grate.");
                Assert.That(water.sharedMesh.bounds.max.z,Is.GreaterThan(4f),"Runoff reaches the sea.");
                var impact = outfall.GetComponentInChildren<ParticleSystem>();
                var beachEye = real.Outfall+new Vector3(2.45f,1.65f,3.65f);
                var beachTarget = real.Outfall+new Vector3(0,.54f,.64f);
                city.Player.Motor.Teleport(real.Outfall+new Vector3(3f,.9f,-1.6f));
                Assert.That(Object.FindObjectsByType<CityWaterVent>().Length,Is.EqualTo(2));
                var header=real.Fixtures.First(f=>f.Kind==CityWaterKit.PumpHeader);
                var span=real.Fixtures.Last(f=>f.Kind==CityWaterKit.WaterBarrel && f.Scale.z>10f);
                var shop = city.Layout.Supermarket;
                var upperHouse = city.Layout.PlayerHome;
                var houseId = CityElectricalConsumerPlan.ForLayout(city.Layout).Consumers.First(c=>c.Lot==upperHouse).StableId;
                var marketService = real.Services.First(s=>s.Id=="supermarket");
                var upperService = real.Services.First(s=>s.Id==houseId);
                Physics.SyncTransforms();
                void FrontRun(CityWaterService service, BuildingLot lot)
                {
                    real.Trace(service,supply,sewer);
                    int last = supply.Count-1;
                    Vector3 a=supply[last-3], b=supply[last-2];
                    Assert.That(Vector3.Distance(a,b),Is.GreaterThan(2.7f),service.Id);
                    Vector3 eye = lot.SidewalkArrivalPosition+Vector3.up*1.60f;
                    int visible = 0;
                    string nearest = "no pipe";
                    for (int i=1;i<=3;i++)
                    {
                        Vector3 sight = Vector3.Lerp(a,b,i*.25f)-eye;
                        if (!Physics.Raycast(eye,sight.normalized,out var hit,sight.magnitude+.15f,
                            Physics.DefaultRaycastLayers,QueryTriggerInteraction.Ignore)) continue;
                        nearest = hit.collider.name;
                        if (hit.collider.transform.parent?.name != CityWaterNetworkWorld.RootName) continue;
                        float along = Mathf.Clamp01(Vector3.Dot(hit.point-a,b-a)/(b-a).sqrMagnitude);
                        if (Vector3.Distance(hit.point,Vector3.Lerp(a,b,along))<.36f) visible++;
                    }
                    if (visible<2) viewIssues.Add($"{service.Id}: visible samples {visible}, nearest {nearest}");
                }
                FrontRun(marketService,shop);
                FrontRun(upperService,upperHouse);
                Vector3 marketEye = city.World.Supermarket.ReturnPosition+Vector3.up*1.6f;
                Vector3 homeEye = city.World.PlayerHome.ReturnPosition+Vector3.up*1.6f;
                var stairs = CityHillsideAccessPlan.ForLayout(city.Layout).SharedCourt.Flights;
                var stair = stairs[stairs.Count-2];
                var industrial = city.Layout.BuildingMasses.First(l=>l.IndustrialPurpose==CityIndustrialPurpose.BoilerHouse);
                var plant = real.Services.First(s=>s.Id==$"city-{industrial.Cell.x}-{industrial.Cell.y}");
                var plantPose = CityBuildingPrototypePlacement.ResolveExpectedCityPose(industrial);
                var works = CityCoastalInfrastructurePlan.ForLayout(city.Layout).Facilities.First(f=>f.Assembly=="MunicipalWorkshop");
                Vector3 Yard(float x,float y,float z) => works.Position+works.Rotation*new Vector3(x,y,z);

                var wing = city.Layout.CourtyardBlocks.First(block=>block.RearBuilding!=null);
                var primary = real.Services.First(s=>s.Id==$"city-{wing.Primary.Cell.x}-{wing.Primary.Cell.y}");
                var rear = real.Services.First(s=>s.Id==$"city-{wing.RearBuilding.Cell.x}-{wing.RearBuilding.Cell.y}-rear");
                real.Trace(primary,supply,sewer);
                var tee = supply[supply.Count-4];
                real.Trace(rear,supply,sewer);
                CollectionAssert.Contains(supply,tee,"The courtyard's two buildings share a physical tee.");
                return new[] {
                    Shot.At("water-supermarket-street",marketEye,marketEye+real.StartStreetFacing(shop,marketEye),66f),
                    Shot.At("water-home-street",homeEye,homeEye+real.StartStreetFacing(upperHouse,homeEye),66f),
                    Shot.At("water-shop-corner",marketEye+shop.FacadeRotation*new Vector3(11f,0f,1.1f),marketService.Inlet,66f),
                    Shot.At("water-home-corner",upperHouse.DoorPosition+upperHouse.FacadeRotation*new Vector3(10f,1.62f,4.8f),
                        upperService.Inlet-Vector3.up*.7f,68f),
                    Shot.At("water-courtyard",wing.Primary.SidewalkArrivalPosition+Vector3.up*1.6f,primary.Inlet,63f),
                    Shot.At("water-boiler-line",plant.Inlet+plantPose.Rotation*new Vector3(-4f,1.0f,4.2f),
                        plant.Inlet-plantPose.Rotation*Vector3.forward*3f,64f),
                    Shot.At("water-stair-descent",stair.End-stair.Direction*.25f+stair.Right*.50f+Vector3.up*1.60f,
                        stair.Start+stair.Right*1.0f,66f),
                    Shot.At("water-municipal-station",header.Position+header.Rotation*new Vector3(2.1f,1.65f,3.85f),header.Position+Vector3.up*.65f,68f),
                    Shot.At("water-municipal-yard-main",Yard(-6.7f,1.68f,4.9f),Yard(-3.85f,.65f,1.2f),65f),
                    Shot.At("water-ridge-bridge",span.Position+span.Rotation*new Vector3(-4.5f,2f,-3f),span.Position+span.Rotation*Vector3.forward*7f,62f),
                    Shot.At("water-beach-outfall",beachEye,beachTarget,55f,readyWhen: () => impact.particleCount > 0),
                    Shot.At("water-beach-outfall-flow",beachEye,beachTarget,55f,delayFrames: 6)
                };
            });
            Assert.That(viewIssues,Is.Empty,string.Join("\n",viewIssues));
        }
    }
}

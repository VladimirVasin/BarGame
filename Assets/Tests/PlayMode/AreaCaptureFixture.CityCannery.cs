using System;
using System.Collections;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

namespace BarPromenade.Tests.PlayMode
{
    public sealed partial class AreaCaptureFixture
    {
        [UnityTest]
        [Explicit("Cannery MVP: finite supply custody, real driven routes, imported metres, public access, lifecycle and production frames.")]
        public IEnumerator CityCannery()
        {
            Type setup=Type.GetType("BarPromenade.Editor.CityCanneryAssetSetup, BarPromenade.Editor");
            Assert.That(setup,Is.Not.Null);
            setup.GetMethod("ValidateOrThrow").Invoke(null,null);
            Type portSetup=Type.GetType("BarPromenade.Editor.CityPortAssetSetup, BarPromenade.Editor");
            Assert.That(portSetup,Is.Not.Null);
            portSetup.GetMethod("ValidateOrThrow").Invoke(null,null);
            GameSessionState.BeginNewGame();
            GameSessionState.TryStartGameTimeFromWake();
            GameSessionState.AdvanceGameTime(360f);
            CityGameRoot city=null;
            string constructionError=null;
            Application.LogCallback captureError=(message,trace,type)=>
            {
                if(constructionError==null&&(type==LogType.Exception||type==LogType.Error))
                    constructionError=message+"\n"+trace;
            };
            Application.logMessageReceived+=captureError;
            try
            {
                AsyncOperation load=SceneManager.LoadSceneAsync(SceneIds.City,LoadSceneMode.Single);
                float deadline=Time.realtimeSinceStartup+TimeoutSeconds;
                while(!load.isDone&&constructionError==null&&Time.realtimeSinceStartup<deadline)yield return null;
                Assert.That(constructionError,Is.Null,constructionError);
                Assert.That(load.isDone,Is.True);
                deadline=Time.realtimeSinceStartup+TimeoutSeconds;
                while(constructionError==null&&Time.realtimeSinceStartup<deadline)
                {
                    city=Object.FindAnyObjectByType<CityGameRoot>();
                    if(city!=null&&city.IsInitialized)break;
                    yield return null;
                }
                Assert.That(constructionError,Is.Null,constructionError);
            }
            finally{Application.logMessageReceived-=captureError;}
            Assert.That(city,Is.Not.Null);
            Assert.That(city.IsInitialized,Is.True);
            CityCanneryController cannery=city.Cannery;
            Assert.That(cannery,Is.Not.Null);
            Assert.That(cannery.IsInitialized,Is.True);
            cannery.AutoAdvance=false;
            cannery.ForcePresentation=true;
            CityPortController port=city.World.Root.GetComponentInChildren<CityPortController>();
            Assert.That(port,Is.Not.Null);
            port.AutoAdvance=false;
            port.ForcePresentation=true;
            city.Player.Motor.SetInputEnabled(false);
            Camera camera=Camera.main;
            Assert.That(camera,Is.Not.Null);
            foreach(Renderer renderer in city.Player.GameObject.GetComponentsInChildren<Renderer>(true))renderer.enabled=false;
            for(int i=0;i<SettleFrames;i++)yield return null;

            var contractFailures=new List<Exception>();
            DeferCanneryContract(contractFailures,"custody",()=>ValidateCanneryCustody(cannery.Cycle));
            DeferCanneryContract(contractFailures,"geometry",()=>ValidateCanneryGeometry(city,cannery));
            DeferCanneryContract(contractFailures,"routes",()=>ValidateCanneryRoutes(city,cannery));
            DeferCanneryContract(contractFailures,"service traffic",()=>ValidateCanneryTraffic(city,cannery));
            DeferCanneryContract(contractFailures,"reconstruction",()=>ValidateCanneryReconstruction(city,cannery,port));
            foreach(CityFishSupplyStage stationStage in new[]{CityFishSupplyStage.Prepare,CityFishSupplyStage.Fill,
                CityFishSupplyStage.Seal,CityFishSupplyStage.LoadRetort,CityFishSupplyStage.Cool,CityFishSupplyStage.Pack})
            {
                DeferCanneryContract(contractFailures,"station "+stationStage,()=>
                {
                    cannery.ApplyAt(CanneryTime(cannery,stationStage,.5f));
                    Assert.That(cannery.WorkerHandsMatch,Is.True,cannery.LastCrewContactFailure);
                });
            }
            Assert.That(cannery.WorkerCount,Is.EqualTo(5));

            // The real session clock, truck and shared ship stay still while
            // pause owns simulation, including after a direct capture seek.
            cannery.AutoAdvance=true;
            yield return null;
            using(GameTimeScaleRuntime.AcquirePause())
            {
                yield return null;
                double stopped=cannery.WorkingSeconds;
                Vector3 truck=cannery.Truck.position, vessel=port.Vessel.position;
                yield return null;
                yield return null;
                DeferCanneryContract(contractFailures,"pause",()=>
                {
                    Assert.That(cannery.WorkingSeconds,Is.EqualTo(stopped));
                    Assert.That(cannery.Truck.position,Is.EqualTo(truck));
                    Assert.That(port.Vessel.position,Is.EqualTo(vessel));
                });
            }
            cannery.AutoAdvance=false;
            CityCanneryPlan plan=cannery.Plan;
            yield return CaptureCannery(camera,city,cannery,CanneryTime(cannery,CityFishSupplyStage.Prepare,.45f),
                "00-factory-street",plan.World(new Vector3(8,1.8f,12)),plan.World(new Vector3(-3,2,1)));
            yield return CaptureCannery(camera,city,cannery,CanneryTime(cannery,CityFishSupplyStage.Prepare,.45f),
                "01-observer-preparation",plan.World(new Vector3(-1.1f,1.9f,-3.5f)),plan.World(new Vector3(-5.7f,1.25f,-2.05f)));
            yield return CaptureCannery(camera,city,cannery,CanneryTime(cannery,CityFishSupplyStage.Fill,.5f),
                "02-observer-filling",plan.World(new Vector3(-1.1f,1.9f,-2.0f)),plan.World(new Vector3(-5,1.4f,-.35f)));
            yield return CaptureCannery(camera,city,cannery,CanneryTime(cannery,CityFishSupplyStage.Seal,.52f),
                "03-observer-sealing",plan.World(new Vector3(-1.1f,1.9f,.2f)),plan.World(new Vector3(-4.4f,1.5f,-.35f)));
            yield return CaptureCannery(camera,city,cannery,CanneryTime(cannery,CityFishSupplyStage.LoadRetort,.6f),
                "04-observer-retort-loading",plan.World(new Vector3(-1.1f,1.9f,3.7f)),plan.World(new Vector3(-5,1.35f,2.65f)));
            yield return CaptureCannery(camera,city,cannery,CanneryTime(cannery,CityFishSupplyStage.Heat,.5f),
                "05-observer-retort-closed",plan.World(new Vector3(-1.1f,1.9f,3.7f)),plan.World(new Vector3(-5,1.35f,2.65f)));
            yield return CaptureCannery(camera,city,cannery,CanneryTime(cannery,CityFishSupplyStage.Cool,.65f),
                "06-observer-cooling",plan.World(new Vector3(-1.1f,1.9f,4.7f)),plan.World(new Vector3(-5,1.35f,3.4f)));
            yield return CaptureCannery(camera,city,cannery,CanneryTime(cannery,CityFishSupplyStage.Pack,.65f),
                "07-observer-packing",plan.World(new Vector3(-1.1f,1.9f,5.7f)),plan.World(new Vector3(-5,1.25f,5.3f)));

            double unload=TransferTime(cannery,CityFishSupplyStage.UnloadFish,0,.49f);
            cannery.ApplyAt(unload);
            DeferCanneryContract(contractFailures,"tail lift",()=>
                Assert.That(cannery.TailLift.position.y,Is.InRange(cannery.Truck.position.y+.15f,cannery.Truck.position.y+1.1f)));
            yield return CaptureCannery(camera,city,cannery,unload,"08-factory-tail-lift",
                plan.World(new Vector3(8.25f,1.8f,-7.6f)),plan.World(new Vector3(4.7f,1.3f,-4f)));
            DeferCanneryContract(contractFailures,"factory lift crew",()=>
            {
                Assert.That(cannery.WorkerHandsMatch,Is.True,cannery.LastCrewContactFailure);
                Assert.That(cannery.DriverSeatedContactsMatch,Is.True);
            });
            CityPortTruckPose portPose=cannery.Route.PortLoadingPose;
            yield return CaptureCannery(camera,city,cannery,TransferTime(cannery,CityFishSupplyStage.LoadFish,0,.41f),
                "09-port-cold-store-loading",portPose.RearAxle+portPose.Rotation*new Vector3(5.5f,2.15f,-1.5f),
                portPose.RearAxle+portPose.Rotation*new Vector3(0,1.25f,-4.25f));
            DeferCanneryContract(contractFailures,"port lift crew",()=>
            {
                Assert.That(cannery.WorkerHandsMatch,Is.True);
                Assert.That(cannery.DriverSeatedContactsMatch,Is.True);
            });
            DeferCanneryContract(contractFailures,"hero blocks handoff",()=>
            {
                Transform cart=cannery.transform.Find("Trolley");
                Assert.That(cart,Is.Not.Null);
                try
                {
                    city.Player.Motor.Teleport(cart.position);
                    Physics.SyncTransforms();
                    Assert.That(cannery.DetectObstacle(),Is.True,"The person occupying the load path must stop the handoff.");
                }
                finally
                {
                    city.Player.Motor.Teleport(plan.World(new Vector3(8,.08f,8)));
                    Physics.SyncTransforms();
                }
            });
            CityPortTruckPose shop=cannery.Route.ShopPose;
            yield return CaptureCannery(camera,city,cannery,TransferTime(cannery,CityFishSupplyStage.UnloadShop,0,.61f),
                "10-supermarket-rear-receiving",shop.RearAxle+shop.Rotation*new Vector3(2,2.1f,-6.8f),
                shop.RearAxle+shop.Rotation*new Vector3(-2.6f,1.15f,-3.4f));
            DeferCanneryContract(contractFailures,"shop receiving crew",()=>
                Assert.That(cannery.WorkerHandsMatch,Is.True,cannery.LastCrewContactFailure));
            double parked=CanneryTime(cannery,CityFishSupplyStage.Prepare,.5f);
            cannery.ApplyAt(parked);
            Debug.Log($"CANNERY CREW: hands={cannery.WorkerHandsMatch}, seated={cannery.DriverSeatedContactsMatch}");
            yield return CaptureCannery(camera,city,cannery,parked,"13-driver-in-cab",
                cannery.Truck.TransformPoint(new Vector3(-3.3f,2.1f,4.65f)),
                cannery.Truck.TransformPoint(new Vector3(-.5f,2f,4f)));
            DeferCanneryContract(contractFailures,"parked cab contacts",()=>
            {
                Assert.That(cannery.WorkerHandsMatch,Is.True);
                Assert.That(cannery.DriverSeatedContactsMatch,Is.True);
            });
            GameSessionState.AdvanceGameTime((float)(21d*60d-GameSessionState.GameTimeOfDayMinutes));
            city.DayNight.ApplyCurrentTime(true);
            yield return CaptureCannery(camera,city,cannery,CanneryTime(cannery,CityFishSupplyStage.Seal,.5f),
                "11-night-street",plan.World(new Vector3(8,1.8f,12)),plan.World(new Vector3(-3,2,1)));
            yield return CaptureCannery(camera,city,cannery,CanneryTime(cannery,CityFishSupplyStage.Pack,.65f),
                "12-night-production",plan.World(new Vector3(-1.1f,1.9f,5.7f)),plan.World(new Vector3(-5,1.25f,4.7f)));
            yield return ValidateCanneryDistancePresentation(city,cannery,port,contractFailures);
            if(contractFailures.Count>0)
                throw new AggregateException("Cannery contracts failed; production frames were retained for inspection.",contractFailures);
            Debug.Log($"CITY CANNERY ACCEPTANCE OK: origin={plan.Origin}, frontage={plan.FrontageEdge}, " +
                $"shop={cannery.Route.ShopDropPoint}, grade={cannery.Route.MaximumGrade:P3}; finite batches, " +
                "physical truck access, public corridor, import scale, pause and deterministic reconstruction.");
        }

        private static void DeferCanneryContract(ICollection<Exception> failures,string name,Action check)
        {
            try{check();}
            catch(Exception error)
            {
                failures.Add(new InvalidOperationException("Cannery "+name+" contract: "+error.Message,error));
                Debug.Log("CANNERY DEFERRED CONTRACT "+name+": "+error);
            }
        }

        private static IEnumerator ValidateCanneryDistancePresentation(CityGameRoot city,
            CityCanneryController cannery,CityPortController port,ICollection<Exception> failures)
        {
            Vector3 savedHero=city.Player.GameObject.transform.position;
            double savedTime=cannery.WorkingSeconds;
            CityPortCrew crew=port.GetComponentInChildren<CityPortCrew>();
            Vector3 far=cannery.Plan.Origin+new Vector3(2000,200,2000);
            try
            {
                cannery.ForcePresentation=false;
                city.Player.Motor.Teleport(far);
                cannery.ApplyAt(CanneryTime(cannery,CityFishSupplyStage.Prepare,.2f));
                crew.ApplyAt(port.ElapsedSeconds);
                Transform worker=cannery.transform.Find("Cannery Preparation Worker");
                Transform spine=CityCanneryAssetProvider.FindPart(worker.gameObject,"spine");
                Quaternion frozenSpine=spine.localRotation;
                Vector3 frozenSeamer=CityCanneryAssetProvider.FindPart(cannery.Equipment.gameObject,"MOVE_SeamerHead").position;
                cannery.ApplyAt(CanneryTime(cannery,CityFishSupplyStage.Seal,.5f));
                DeferCanneryContract(failures,"distant simulation and presentation",()=>
                {
                    Assert.That(cannery.Snapshot.Stage,Is.EqualTo(CityFishSupplyStage.Seal));
                    Assert.That(cannery.Snapshot.AccountedUnits,Is.EqualTo(6));
                    Assert.That(cannery.FactoryPresentationActive||cannery.TruckPresentationActive||
                        port.ShorePresentationActive||port.VesselPresentationActive,Is.False);
                    Assert.That(worker.gameObject.activeSelf,Is.False);
                    Assert.That(spine.localRotation,Is.EqualTo(frozenSpine),"Distant worker animation must not be sampled.");
                    Assert.That(CityCanneryAssetProvider.FindPart(cannery.Equipment.gameObject,"MOVE_SeamerHead").position,
                        Is.EqualTo(frozenSeamer),"Distant machinery must not be animated.");
                    AssertCanneryHidden(cannery.Equipment);
                    AssertCanneryHidden(cannery.Truck);
                    AssertCanneryHidden(port.Dock);
                    AssertCanneryHidden(port.Vessel);
                    Transform shell=null;
                    foreach(Transform part in city.World.DistrictPointOfInterestRoot.GetComponentsInChildren<Transform>(true))
                        if(part.name=="Industrial Cannery"&&part.Find("Hall")!=null){shell=part;break;}
                    Assert.That(shell,Is.Not.Null);
                    AssertCanneryHidden(shell);
                    Assert.That(cannery.Truck.GetComponentInChildren<Collider>().enabled,Is.True);
                    Assert.That(cannery.Traffic.BlocksSpawn(cannery.Truck.position,cannery.Truck.rotation,
                        new Bounds(new Vector3(0,1,1),new Vector3(2,2,7))),Is.True);
                    city.DayNight.ApplyCurrentTime(true);
                    foreach(Light light in shell.GetComponentsInChildren<Light>(true)) Assert.That(light.gameObject.activeInHierarchy,Is.False);
                    foreach(Light light in cannery.Truck.GetComponentsInChildren<Light>(true)) Assert.That(light.gameObject.activeInHierarchy,Is.False);
                    foreach(Light light in port.GetComponentsInChildren<Light>(true)) Assert.That(light.gameObject.activeInHierarchy,Is.False);
                });

                using(GameTimeScaleRuntime.AcquirePause())
                {
                    DeferCanneryContract(failures,"paused approach and hysteresis",()=>
                    {
                        Bounds bounds=cannery.FactoryPresentationBounds;
                        float[] offsets={88f,79f,88f,97f};
                        bool[] expected={false,true,true,false};
                        for(int i=0;i<offsets.Length;i++)
                        {
                            float offset=offsets[i];
                            city.Player.Motor.Teleport(bounds.center+Vector3.right*(bounds.extents.x+offset));
                            cannery.RefreshPresentation();
                            Assert.That(cannery.FactoryPresentationActive,Is.EqualTo(expected[i]));
                            if(offset==79f) Assert.That(worker.gameObject.activeSelf,Is.True);
                            if(offset==97f) Assert.That(worker.gameObject.activeSelf,Is.False);
                        }
                        city.Player.Motor.Teleport(cannery.Plan.World(new Vector3(-1,1,0)));
                        cannery.RefreshPresentation();
                        Assert.That(cannery.Snapshot.Stage,Is.EqualTo(CityFishSupplyStage.Seal));
                        Assert.That(cannery.FactoryPresentationActive,Is.True);
                        Assert.That(cannery.WorkerHandsMatch,Is.True,cannery.LastCrewContactFailure);
                        Assert.That(cannery.WorkingSeconds,Is.EqualTo(CanneryTime(cannery,CityFishSupplyStage.Seal,.5f)));
                    });
                    yield return null;
                }

                DeferCanneryContract(failures,"independent moving truck",()=>
                {
                    city.Player.Motor.Teleport(far);
                    bool found=false;
                    foreach(float fraction in new[]{.25f,.5f,.75f})
                    {
                        cannery.ApplyAt(CanneryTime(cannery,CityFishSupplyStage.FactoryToShop,fraction));
                        if(cannery.FactoryPresentationBounds.SqrDistance(cannery.Truck.position)<150f*150f)continue;
                        found=true;break;
                    }
                    Assert.That(found,Is.True,"Route needs a remote truck observation point.");
                    Assert.That(cannery.TruckPresentationActive,Is.False);
                    Vector3 moving=cannery.Truck.position;
                    cannery.ApplyAt(cannery.WorkingSeconds+2);
                    Assert.That(Vector3.Distance(moving,cannery.Truck.position),Is.GreaterThan(1));
                    double current=cannery.WorkingSeconds;
                    city.Player.Motor.Teleport(cannery.Truck.position+cannery.Truck.right*4);
                    cannery.RefreshPresentation();
                    Assert.That(cannery.TruckPresentationActive,Is.True);
                    Assert.That(cannery.FactoryPresentationActive,Is.False);
                    Assert.That(cannery.DriverSeatedContactsMatch,Is.True);
                    Assert.That(cannery.WorkingSeconds,Is.EqualTo(current));
                });

                DeferCanneryContract(failures,"independent approaching vessel",()=>
                {
                    city.Player.Motor.Teleport(far);
                    cannery.ApplyAt(1);
                    Vector3 logical=port.VesselLogicalPosition;
                    city.Player.Motor.Teleport(logical+Vector3.forward*65);
                    cannery.RefreshPresentation();
                    crew.ApplyAt(port.ElapsedSeconds);
                    Assert.That(port.VesselPresentationActive,Is.True);
                    Assert.That(port.ShorePresentationActive,Is.False);
                    Assert.That(crew.Captain.gameObject.activeSelf,Is.True);
                    Assert.That(crew.ShoreWorker.gameObject.activeSelf,Is.False);
                    Assert.That(Vector3.Distance(port.Vessel.position,logical),Is.LessThan(2));
                    Assert.That(crew.CaptainHandsMatch,Is.True);
                });

                city.Player.Motor.Teleport(far);
                cannery.RefreshPresentation();
                cannery.AutoAdvance=true;
                yield return null;
                double before=cannery.WorkingSeconds;
                for(int i=0;i<4;i++)yield return null;
                DeferCanneryContract(failures,"live distant clock",()=>
                {
                    Assert.That(cannery.FactoryPresentationActive||cannery.TruckPresentationActive,Is.False);
                    Assert.That(cannery.WorkingSeconds,Is.GreaterThan(before));
                    Assert.That(port.ElapsedSeconds,Is.EqualTo(cannery.Snapshot.PortSeconds));
                });
            }
            finally
            {
                cannery.AutoAdvance=false;
                cannery.ForcePresentation=true;
                city.Player.Motor.Teleport(savedHero);
                cannery.ApplyAt(savedTime);
                crew.ApplyAt(port.ElapsedSeconds);
            }
        }

        private static void AssertCanneryHidden(Transform root)
        {
            foreach(Renderer renderer in root.GetComponentsInChildren<Renderer>(true))
                Assert.That(!renderer.gameObject.activeInHierarchy||!renderer.enabled||renderer.forceRenderingOff,
                    Is.True,root.name+" still renders "+renderer.name);
        }

        private static void ValidateCanneryCustody(CityFishSupplyCycle cycle)
        {
            foreach(CityFishSupplyStage stage in Enum.GetValues(typeof(CityFishSupplyStage)))
            for(int batch=0;batch<2;batch++)
            {
                double start=batch*cycle.Duration+cycle.StageStart(stage);
                foreach(float fraction in new[]{0f,.07f,.26f,.5f,.74f,.93f,.99999f})
                {
                    CityFishSupplySnapshot state=cycle.Sample(start+cycle.StageDuration(stage)*fraction);
                    Assert.That(state.Stage,Is.EqualTo(stage));
                    Assert.That(state.Batch,Is.EqualTo(batch));
                    if(stage==CityFishSupplyStage.PortVisit)
                    {
                        Assert.That(state.AccountedUnits,Is.InRange(0,6));
                        Assert.That(state.PortFish,Is.EqualTo(CityPortCycle.Sample(state.PortSeconds).StoredCargo));
                    }
                    else Assert.That(state.AccountedUnits,Is.EqualTo(6),$"Custody at {stage}/{fraction}");
                    if(stage<CityFishSupplyStage.Pack)
                        Assert.That(state.FactoryCases+state.TruckCases+state.DeliveredCases,Is.Zero);
                }
            }
            Assert.That(cycle.Sample(cycle.Duration).Batch,Is.EqualTo(1));
            Assert.That(cycle.Sample(cycle.Duration).Stage,Is.EqualTo(CityFishSupplyStage.PortVisit));
            Assert.Throws<ArgumentOutOfRangeException>(()=>cycle.Sample(double.NaN));
            Assert.Throws<ArgumentOutOfRangeException>(()=>cycle.Sample(-1));
        }

        private static void ValidateCanneryGeometry(CityGameRoot city,CityCanneryController cannery)
        {
            CityCanneryPlan plan=cannery.Plan;
            Assert.That(city.WeighbridgeAttendants,Is.Empty);
            Assert.That(city.WeighbridgeNeedle,Is.Null);
            Assert.That(Vector3.Distance(cannery.Factory.position,plan.Origin),Is.LessThan(.001f));
            AssertCanneryAnchor(cannery.Equipment,plan,"PreparationLoad",new Vector3(-5.7f,.96f,-2.05f));
            AssertCanneryAnchor(cannery.Equipment,plan,"CanTray",new Vector3(-5,1.19f,-.35f));
            AssertCanneryAnchor(cannery.Equipment,plan,"RawDoor",new Vector3(.3f,.18f,-5.5f));
            AssertCanneryAnchor(cannery.Equipment,plan,"FinishedDoor",new Vector3(.3f,.18f,5));
            Bounds truck=PortLocalMeshBounds(cannery.Truck);
            Assert.That(truck.size.x,Is.InRange(2.4f,2.7f));
            Assert.That(truck.max.z,Is.InRange(5.3f,5.6f));
            Assert.That(truck.min.z,Is.InRange(-2.8f,-2.5f));
            cannery.ApplyAt(CanneryTime(cannery,CityFishSupplyStage.Prepare,.5f));
            Physics.SyncTransforms();
            for(float z=-7.5f;z<=7.5f;z+=.5f)
            {
                Vector3 foot=plan.World(new Vector3(-1.1f,.18f,z));
                Assert.That(city.World.WalkableArea.Contains(foot,.35f),Is.True,$"Public passage {z}");
                Collider[] hits=Physics.OverlapBox(foot+Vector3.up*.95f,new Vector3(.75f,.75f,.12f),
                    plan.Rotation,~0,QueryTriggerInteraction.Ignore);
                foreach(Collider hit in hits)
                {
                    if(IsCanneryDynamicActor(hit)||hit.bounds.max.y<foot.y+.3f)continue;
                    Assert.Fail($"Public passage is blocked by {hit.name} at local z={z}.");
                }
            }
            Assert.That(city.World.WalkableArea.Contains(plan.World(new Vector3(-5,.18f,0)),.35f),Is.False);
        }

        private static void AssertCanneryAnchor(Transform root,CityCanneryPlan plan,string name,Vector3 local)
        {
            Transform anchor=CityCanneryAssetProvider.FindPart(root.gameObject,"ANCHOR_"+name);
            Assert.That(Vector3.Distance(anchor.position,plan.World(local)),Is.LessThan(.015f),name+" imported metres");
        }

        private static void ValidateCanneryRoutes(CityGameRoot city,CityCanneryController cannery)
        {
            ValidateCanneryApron(cannery.Plan);
            cannery.Route.ValidateOrThrow();
            CityCanneryTruckLeg[] ordered={CityCanneryTruckLeg.PortArrive,CityCanneryTruckLeg.PortReverse,
                CityCanneryTruckLeg.PortToFactory,CityCanneryTruckLeg.FactoryReverse,
                CityCanneryTruckLeg.FactoryToShop,CityCanneryTruckLeg.ShopToPort};
            for(int i=0;i<ordered.Length;i++)
            {
                CityPortTruckPose a=cannery.Route.Sample(ordered[i],1);
                CityPortTruckPose b=cannery.Route.Sample(ordered[(i+1)%ordered.Length],0);
                Assert.That(Vector3.Distance(a.RearAxle,b.RearAxle),Is.LessThan(.025f),$"Route seam {ordered[i]}");
                Assert.That(Quaternion.Angle(a.Rotation,b.Rotation),Is.LessThan(.3f),$"Route heading {ordered[i]}");
            }
            var disabled=new List<Collider>();
            foreach(Collider collider in Object.FindObjectsByType<Collider>())
                if(collider.enabled&&IsCanneryDynamicActor(collider)){collider.enabled=false;disabled.Add(collider);}
            try
            {
                Physics.SyncTransforms();
                foreach(CityCanneryTruckLeg leg in ordered)
                {
                    CityFishSupplyStage stage=CanneryStage(leg);
                    int steps=Mathf.Max(2,Mathf.CeilToInt(cannery.Route.Length(leg)/3f));
                    for(int i=0;i<=steps;i++)
                    {
                        double time=CanneryTime(cannery,stage,Mathf.Min(i/(float)steps,.99999f));
                        cannery.ApplyAt(time);
                        Physics.SyncTransforms();
                        Assert.That(cannery.DetectObstacle(),Is.False,$"Static obstacle sensor at {leg} {i}/{steps}: {cannery.Truck.position}; {cannery.LastObstacleName}");
                    }
                    Debug.Log($"CANNERY ROUTE {leg}: {cannery.Route.Length(leg):F2} m");
                }
                cannery.ApplyAt(CanneryTime(cannery,CityFishSupplyStage.PortToFactory,.5f));
                var blocker=new GameObject("Cannery stopped-traffic probe");
                try
                {
                    CityPortTruckPose future=cannery.TruckPose(cannery.Cycle.Sample(cannery.WorkingSeconds+.7d));
                    blocker.transform.SetPositionAndRotation(future.RearAxle+
                        future.Rotation*new Vector3(0,1,4.8f),future.Rotation);
                    blocker.AddComponent<BoxCollider>().size=new Vector3(.5f,1,.5f);
                    Physics.SyncTransforms();
                    Assert.That(cannery.DetectObstacle(),Is.True,"An actual object in the sensor corridor must stop the truck.");
                    Assert.That(cannery.LastObstacleName,Is.EqualTo(blocker.name));
                }
                finally{Object.DestroyImmediate(blocker);}
            }
            finally
            {
                foreach(Collider collider in disabled)if(collider!=null)collider.enabled=true;
                Physics.SyncTransforms();
            }
        }

        private static bool IsCanneryDynamicActor(Collider collider) =>
            collider.GetComponentInParent<PlayerMotor>()!=null ||
            collider.GetComponentInParent<CityPedestrianAssetRegistry>()!=null ||
            collider.GetComponentInParent<CityBusActor>()!=null;

        private static void ValidateCanneryApron(CityCanneryPlan plan)
        {
            Physics.SyncTransforms();
            foreach(float x in new[]{2f,4f,6f,7.5f})
            foreach(float z in new[]{9.2f,9.5f,9.8f})
            {
                Vector3 point=plan.World(new Vector3(x,0,z));
                Assert.That(plan.TrySampleYardTop(point,out float top),Is.True);
                point.y=top;
                float nearest=float.PositiveInfinity;
                foreach(RaycastHit hit in Physics.RaycastAll(point+Vector3.up,Vector3.down,2f,~0,
                    QueryTriggerInteraction.Ignore))
                    if(hit.collider.name.StartsWith("COL_Yard",StringComparison.Ordinal))
                        nearest=Mathf.Min(nearest,Mathf.Abs(hit.point.y-top));
                Assert.That(nearest,Is.LessThan(.004f),
                    $"Authored driveway collider must match route height at local ({x},{z}), worldY={top}.");
            }
            Assert.That(plan.TrySampleYardTop(plan.World(new Vector3(.5f,0,9.5f)),out _),Is.False,
                "The dropped curb does not extend across the entire public frontage.");
            Assert.That(plan.TrySampleYardTop(plan.World(new Vector3(8.5f,0,9.5f)),out _),Is.False);
        }

        private static void ValidateCanneryTraffic(CityGameRoot city,CityCanneryController cannery)
        {
            Assert.That(city.Bus,Is.Not.Null);
            Assert.That(cannery.Traffic,Is.Not.Null);
            HashSet<RoadEdge> served=CityBusPlanner.ServiceRoadEdges(city.BusPlan);
            Assert.That(served.SetEquals(CityBusPlanner.ServiceRoadEdges(
                CityBusPlanner.CreateRoadRouting(city.Layout))),Is.True,"Pure service routing matches the actual bus loop.");
            int shared=0;
            foreach(RoadEdge edge in cannery.Route.StreetEdges)if(served.Contains(edge))shared++;
            Assert.That(shared,Is.EqualTo(cannery.Route.SharedBusStreetCount));
            float minimumRise=float.PositiveInfinity;
            foreach(CityDistrictPointOfInterestAccessDescriptor access in cannery.Plan.Descriptor.Accesses)
            {
                RoadEdge edge=access.FrontageEdge;
                minimumRise=Mathf.Min(minimumRise,Mathf.Abs(city.Layout.ElevationPlan.GetNodeElevation(edge.A)-
                    city.Layout.ElevationPlan.GetNodeElevation(edge.B)));
            }
            RoadEdge frontage=cannery.Plan.FrontageEdge;
            float frontageRise=Mathf.Abs(city.Layout.ElevationPlan.GetNodeElevation(frontage.A)-
                city.Layout.ElevationPlan.GetNodeElevation(frontage.B));
            Assert.That(frontageRise,Is.LessThanOrEqualTo(minimumRise+.01f),
                "The level factory yard chooses the flattest available road before traffic preferences.");
            bool hasSeparateAccess=false;
            foreach(CityDistrictPointOfInterestAccessDescriptor access in cannery.Plan.Descriptor.Accesses)
            {
                RoadEdge edge=access.FrontageEdge;
                float rise=Mathf.Abs(city.Layout.ElevationPlan.GetNodeElevation(edge.A)-
                    city.Layout.ElevationPlan.GetNodeElevation(edge.B));
                if(rise<=minimumRise+.01f&&!served.Contains(edge))hasSeparateAccess=true;
            }
            if(hasSeparateAccess)Assert.That(served.Contains(cannery.Plan.FrontageEdge),Is.False,
                "Factory frontage uses an equally level street outside the bus service loop when available.");
            Debug.Log($"CANNERY TRAFFIC: {shared} shared service edges; complete trips require exclusive clearance.");

            cannery.ApplyAt(CanneryTime(cannery,CityFishSupplyStage.Prepare,.5f));
            cannery.Traffic.Release();
            Assert.That(city.Bus.CanneryBlocksSpawn(cannery.Truck.position,cannery.Truck.rotation),Is.True,
                "Bus spawn eligibility includes the physical delivery body.");
            Assert.That(city.Bus.CanneryBlocksSpawn(cannery.Truck.position+Vector3.up*1000,
                cannery.Truck.rotation),Is.False);
            Bounds busBounds=city.Bus.Actor.LocalVisualBounds;
            var traffic=new CityCanneryTraffic(cannery,null);
            CityFishSupplyStage[] starts={CityFishSupplyStage.PortToFactory,
                CityFishSupplyStage.FactoryToShop,CityFishSupplyStage.ShopToPort};
            CityCanneryTruckLeg[] legs={CityCanneryTruckLeg.PortToFactory,
                CityCanneryTruckLeg.FactoryToShop,CityCanneryTruckLeg.ShopToPort};
            for(int trip=0;trip<3;trip++)
            {
                bool holdingPose=false;
                foreach(int index in city.BusPlan.OrderedLinkIndices)
                {
                    IReadOnlyList<CityBusPathSample> samples=city.BusPlan.Links[index].Samples;
                    for(int sample=0;sample<samples.Count;sample+=40)
                    {
                        CityBusPathSample pose=samples[sample];
                        if(!traffic.CanReserveTripAt(trip,pose.Position,
                            Quaternion.LookRotation(pose.Forward),busBounds))continue;
                        holdingPose=true;break;
                    }
                    if(holdingPose)break;
                }
                Assert.That(holdingPose,Is.True,$"Trip {trip} must leave a real bus holding pose outside its sweep.");
                CityPortTruckPose crossing=cannery.Route.Sample(legs[trip],.5f);
                Assert.That(traffic.CanReserveTripAt(trip,crossing.RearAxle,crossing.Rotation,busBounds),Is.False,
                    "Reservation cannot be granted over a bus already occupying the truck corridor.");
                CityFishSupplySnapshot departure=cannery.Cycle.Sample(CanneryTime(cannery,starts[trip],0));
                Assert.That(traffic.TryAcquire(departure),Is.True,"An absent bus leaves a clear delivery trip.");
                Assert.That(traffic.ReservedTrip,Is.EqualTo(trip));
                Assert.That(traffic.BlocksSpawn(crossing.RearAxle,crossing.Rotation,busBounds),Is.True,
                    "The bus cannot spawn into an upcoming reserved crossing.");
                CityFishSupplyStage last=trip==0?CityFishSupplyStage.FactoryReverse:
                    trip==2?CityFishSupplyStage.PortReverse:CityFishSupplyStage.FactoryToShop;
                Assert.That(traffic.TryAcquire(cannery.Cycle.Sample(CanneryTime(cannery,last,.99f))),Is.True);
                Assert.That(traffic.ReservedTrip,Is.EqualTo(trip),"The reservation survives reverse/arrival sub-legs.");
                Assert.That(traffic.TryAcquire(cannery.Cycle.Sample(CanneryTime(cannery,
                    CityFishSupplyStage.Prepare,.5f))),Is.True);
                Assert.That(traffic.HasReservation,Is.False,"Parking releases street service traffic.");
            }
        }

        private static void ValidateCanneryReconstruction(CityGameRoot city,CityCanneryController cannery,CityPortController port)
        {
            double seek=TransferTime(cannery,CityFishSupplyStage.UnloadFish,2,.49f);
            cannery.ApplyAt(seek);
            Vector3 truck=cannery.Truck.position,lift=cannery.TailLift.position;
            Transform fish=cannery.transform.Find("Fish handling unit 2");
            Assert.That(fish,Is.Not.Null);
            Vector3 cargo=fish.position;
            cannery.ApplyAt(cannery.Cycle.Duration*2+10);
            cannery.ApplyAt(seek);
            Assert.That(cannery.Truck.position,Is.EqualTo(truck));
            Assert.That(cannery.TailLift.position,Is.EqualTo(lift));
            Assert.That(fish.position,Is.EqualTo(cargo));
            var host=new GameObject("Cannery cold reconstruction probe");
            host.SetActive(false);
            try
            {
                CityCanneryController other=CityCanneryController.Build(host.transform,city.Layout,port,city.Player.GameObject.transform);
                other.AutoAdvance=false;
                other.ForcePresentation=true;
                other.ApplyAt(seek);
                Assert.That(Vector3.Distance(other.Truck.position,truck),Is.LessThan(.001f));
                Assert.That(Vector3.Distance(other.TailLift.position,lift),Is.LessThan(.001f));
                Assert.That(Vector3.Distance(other.transform.Find("Fish handling unit 2").position,cargo),Is.LessThan(.001f));
                Assert.That(other.Snapshot.AccountedUnits,Is.EqualTo(cannery.Snapshot.AccountedUnits));
            }
            finally
            {
                Object.DestroyImmediate(host);
                port.AutoAdvance=false;
                port.IsSupplyDriven=true;
                cannery.ApplyAt(seek);
            }
        }

        private static CityFishSupplyStage CanneryStage(CityCanneryTruckLeg leg)
        {
            switch(leg)
            {
                case CityCanneryTruckLeg.PortArrive:return CityFishSupplyStage.PortArrive;
                case CityCanneryTruckLeg.PortReverse:return CityFishSupplyStage.PortReverse;
                case CityCanneryTruckLeg.PortToFactory:return CityFishSupplyStage.PortToFactory;
                case CityCanneryTruckLeg.FactoryReverse:return CityFishSupplyStage.FactoryReverse;
                case CityCanneryTruckLeg.FactoryToShop:return CityFishSupplyStage.FactoryToShop;
                default:return CityFishSupplyStage.ShopToPort;
            }
        }
        private static double CanneryTime(CityCanneryController cannery,CityFishSupplyStage stage,float progress) =>
            cannery.Cycle.StageStart(stage)+cannery.Cycle.StageDuration(stage)*progress;
        private static double TransferTime(CityCanneryController cannery,CityFishSupplyStage stage,int unit,float progress) =>
            cannery.Cycle.StageStart(stage)+12+(cannery.Cycle.StageDuration(stage)-24)*(unit+progress)/6;

        private static IEnumerator CaptureCannery(Camera camera,CityGameRoot city,CityCanneryController cannery,
            double seconds,string name,Vector3 from,Vector3 target)
        {
            PlayerCameraFollow follow=camera.GetComponent<PlayerCameraFollow>();
            bool wasEnabled=follow!=null&&follow.enabled;
            if(follow!=null)follow.enabled=false;
            try
            {
                cannery.ApplyAt(seconds);
                city.Player.Motor.Teleport(from-Vector3.up*EyeHeight);
                camera.transform.SetPositionAndRotation(from,Quaternion.LookRotation(target-from));
                camera.fieldOfView=68f;
                Physics.SyncTransforms();
                for(int frame=0;frame<3;frame++)yield return null;
                Assert.That(cannery.WorkingSeconds,Is.EqualTo(seconds));
                Assert.That(Vector3.Distance(camera.transform.position,from),Is.LessThan(.001f));
                CaptureCurrentCamera(camera,"CityCannery",name);
            }
            finally{if(follow!=null)follow.enabled=wasEnabled;}
        }
    }
}

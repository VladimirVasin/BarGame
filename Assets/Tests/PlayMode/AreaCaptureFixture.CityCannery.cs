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
        [Explicit("Cannery MVP: overlapping finite production, real driven routes, imported metres, public access, lifecycle and production frames.")]
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
            GameSessionState.AdvanceGameTime((float)(360f / GameTimeState.GameMinutesPerRealSecond));
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
            DeferCanneryContract(contractFailures,"three-load port completion",()=>ValidatePortVisitCompletion(port));
            DeferCanneryContract(contractFailures,"shared port loading aisle",()=>ValidateCanneryConcurrentPortLoading(cannery,port));
            DeferCanneryContract(contractFailures,"production during receiving",()=>ValidateCanneryEarlyProduction(cannery));
            DeferCanneryContract(contractFailures,"geometry",()=>ValidateCanneryGeometry(city,cannery));
            DeferCanneryContract(contractFailures,"routes",()=>ValidateCanneryRoutes(city,cannery));
            DeferCanneryContract(contractFailures,"service traffic",()=>ValidateCanneryTraffic(city,cannery));
            DeferCanneryContract(contractFailures,"reconstruction",()=>ValidateCanneryReconstruction(city,cannery,port));
            DeferCanneryContract(contractFailures,"visual product and surfaces",()=>ValidateCanneryVisualProducts(cannery));
            foreach(CityCanneryProductionStage stationStage in new[]{CityCanneryProductionStage.Prepare,CityCanneryProductionStage.Fill,
                CityCanneryProductionStage.Seal,CityCanneryProductionStage.LoadRetort,CityCanneryProductionStage.Cool,CityCanneryProductionStage.Pack})
            {
                foreach(float phase in new[]{.03f,.075f,.125f,.15f,.5f,.875f})
                {
                    DeferCanneryContract(contractFailures,"station "+stationStage+" at "+phase,()=>
                    {
                        cannery.ApplyAt(CanneryTime(cannery,stationStage,phase));
                        Assert.That(cannery.WorkerHandsMatch,Is.True,cannery.LastCrewContactFailure);
                    });
                }
            }
            double packingStart=cannery.Cycle.ProductionStageStart(CityCanneryProductionStage.Pack)+8d/2d;
            double canSlot=(cannery.Cycle.ProductionStageDuration(CityCanneryProductionStage.Pack)-10d/2d)/15d;
            var packingWorker=cannery.transform.Find("Cannery Retort and Packing Worker")
                .GetComponent<VillageResidentPresentation>();
            for(int canIndex=0;canIndex<15;canIndex++)
            {
                foreach(float phase in new[]{.28f,.35f,.5f,.66f,.72f,.8f})
                {
                    DeferCanneryContract(contractFailures,"packing can "+canIndex+" at "+phase,()=>
                    {
                        cannery.ApplyAt(packingStart+(canIndex+phase)*canSlot);
                        Assert.That(cannery.WorkerHandsMatch,Is.True,cannery.LastCrewContactFailure);
                        if(phase<.35f||phase>.66f)return;
                        Transform can=CityCanneryAssetProvider.FindPart(cannery.transform.gameObject,
                            "CanUnit"+canIndex.ToString("D2"));
                        Vector3 gripHeight=can.position+Vector3.up*.075f;
                        // Independently measure both palms against the actual
                        // rendered can, not a spare hand target or the bench.
                        Assert.That(Vector3.Distance(packingWorker.RightGrip.position,gripHeight),Is.LessThan(.08f));
                        Assert.That(Vector3.Distance(packingWorker.LeftGrip.position,gripHeight),Is.LessThan(.08f));
                    });
                }
            }
            Assert.That(cannery.WorkerCount,Is.EqualTo(5));

            // The real session clock, truck and shared ship stay still while
            // pause owns simulation, including after a direct capture seek.
            cannery.AutoAdvance=true;
            yield return null;
            using(GameTimeScaleRuntime.AcquirePause())
            {
                cannery.ApplyAt(CanneryTime(cannery,CityCanneryProductionStage.Seal,.5f));
                yield return null;
                double stopped=cannery.WorkingSeconds;
                Vector3 truck=cannery.Truck.position, vessel=port.Vessel.position;
                double productionSeconds=cannery.Snapshot.Production.Seconds;
                Transform receiver=cannery.transform.Find("Cannery Receiver");
                Vector3 receiverPosition=receiver.position;
                yield return null;
                yield return null;
                DeferCanneryContract(contractFailures,"pause",()=>
                {
                    Assert.That(cannery.WorkingSeconds,Is.EqualTo(stopped));
                    Assert.That(cannery.Truck.position,Is.EqualTo(truck));
                    Assert.That(port.Vessel.position,Is.EqualTo(vessel));
                    Assert.That(cannery.Snapshot.Stage,Is.EqualTo(CityFishSupplyStage.UnloadFish));
                    Assert.That(cannery.Snapshot.Production.Seconds,Is.EqualTo(productionSeconds));
                    Assert.That(receiver.position,Is.EqualTo(receiverPosition));
                });
            }
            cannery.AutoAdvance=false;
            CityCanneryPlan plan=cannery.Plan;
            yield return CaptureCannery(camera,city,cannery,CanneryTime(cannery,CityCanneryProductionStage.Prepare,.45f),
                "00-factory-street",plan.World(new Vector3(8,1.8f,12)),plan.World(new Vector3(-3,2,1)));
            yield return CaptureCannery(camera,city,cannery,CanneryTime(cannery,CityCanneryProductionStage.Prepare,.45f),
                "01-observer-preparation",plan.World(new Vector3(-1.1f,1.9f,-3.5f)),plan.World(new Vector3(-5.7f,1.25f,-2.05f)));
            yield return CaptureCannery(camera,city,cannery,CanneryTime(cannery,CityCanneryProductionStage.Fill,.5f),
                "02-observer-filling",plan.World(new Vector3(-1.1f,1.9f,-2.0f)),plan.World(new Vector3(-5,1.4f,-.35f)));
            yield return CaptureCannery(camera,city,cannery,CanneryTime(cannery,CityCanneryProductionStage.Seal,.52f),
                "03-observer-sealing",plan.World(new Vector3(-1.1f,1.9f,.2f)),plan.World(new Vector3(-4.4f,1.5f,-.35f)));
            yield return CaptureCannery(camera,city,cannery,CanneryTime(cannery,CityCanneryProductionStage.LoadRetort,.6f),
                "04-observer-retort-loading",plan.World(new Vector3(-1.1f,1.9f,3.7f)),plan.World(new Vector3(-5,1.35f,2.65f)));
            yield return CaptureCannery(camera,city,cannery,CanneryTime(cannery,CityCanneryProductionStage.Heat,.5f),
                "05-observer-retort-closed",plan.World(new Vector3(-1.1f,1.9f,3.7f)),plan.World(new Vector3(-5,1.35f,2.65f)));
            yield return CaptureCannery(camera,city,cannery,CanneryTime(cannery,CityCanneryProductionStage.Cool,.65f),
                "06-observer-cooling",plan.World(new Vector3(-1.1f,1.9f,4.7f)),plan.World(new Vector3(-5,1.35f,3.4f)));
            yield return CaptureCannery(camera,city,cannery,CanneryTime(cannery,CityCanneryProductionStage.Pack,.65f),
                "07-observer-packing",plan.World(new Vector3(-1.1f,1.9f,5.7f)),plan.World(new Vector3(-5,1.25f,5.3f)));
            yield return CaptureCannery(camera,city,cannery,CanneryTime(cannery,CityCanneryProductionStage.Prepare,.72f,1),
                "17-receiving-and-production",plan.World(new Vector3(-1.1f,1.9f,-3.5f)),
                plan.World(new Vector3(-6.3f,1.35f,-4.1f)));

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
                Transform cart=cannery.ActiveTrolley;
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
            double parked=CanneryTime(cannery,CityCanneryProductionStage.Prepare,.5f);
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
            GameSessionState.AdvanceGameTime((float)((21d*60d-GameSessionState.GameTimeOfDayMinutes) / GameTimeState.GameMinutesPerRealSecond));
            city.DayNight.ApplyCurrentTime(true);
            yield return CaptureCannery(camera,city,cannery,CanneryTime(cannery,CityCanneryProductionStage.Seal,.5f),
                "11-night-street",plan.World(new Vector3(8,1.8f,12)),plan.World(new Vector3(-3,2,1)));
            yield return CaptureCannery(camera,city,cannery,CanneryTime(cannery,CityCanneryProductionStage.Pack,.65f),
                "12-night-production",plan.World(new Vector3(-1.1f,1.9f,5.7f)),plan.World(new Vector3(-5,1.25f,4.7f)));
            yield return CaptureCannery(camera,city,cannery,CanneryTime(cannery,CityCanneryProductionStage.Fill,.48f),
                "14-open-and-filled-cans",plan.World(new Vector3(-6.9f,2.4f,-1.5f)),plan.World(new Vector3(-5.7f,1.23f,-.35f)));
            yield return CaptureCannery(camera,city,cannery,packingStart+(.5d+7d)*canSlot,
                "15-packing-contact",plan.World(new Vector3(-4.5f,2.0f,6.15f)),plan.World(new Vector3(-6.28f,1.4f,4.94f)));
            yield return CaptureCannery(camera,city,cannery,cannery.Cycle.ProductionStageStart(CityCanneryProductionStage.Cool)+1.1d/2d,
                "16-pressure-vent",plan.World(new Vector3(-5.4f,5.65f,4.0f)),plan.World(new Vector3(-7.65f,5.2f,2.15f)));
            yield return ValidateCanneryDistancePresentation(city,cannery,port,contractFailures);
            if(contractFailures.Count>0)
                throw new AggregateException("Cannery contracts failed; production frames were retained for inspection.",contractFailures);
            Debug.Log($"CITY CANNERY ACCEPTANCE OK: origin={plan.Origin}, frontage={plan.FrontageEdge}, " +
                $"shop={cannery.Route.ShopDropPoint}, grade={cannery.Route.MaximumGrade:P3}; finite batches, " +
                "production during receiving, physical truck access, public corridor, import scale, pause and deterministic reconstruction.");
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

        private static void ValidateCanneryEarlyProduction(CityCanneryController cannery)
        {
            CityFishSupplyCycle cycle=cannery.Cycle;
            Assert.That(CityFishSupplyCycle.HandlingUnits,Is.EqualTo(3));
            int rawModels=0,finishedModels=0;
            foreach(Transform part in cannery.GetComponentsInChildren<Transform>(true))
            {
                if(part.name.StartsWith("Fish handling unit ",StringComparison.Ordinal))rawModels++;
                if(part.name.StartsWith("Finished handling unit ",StringComparison.Ordinal))finishedModels++;
            }
            Assert.That(rawModels,Is.EqualTo(3),"Only the three shipped raw units may exist, including hidden ones.");
            Assert.That(finishedModels,Is.EqualTo(3),"Only three finished handling units may exist, including hidden ones.");
            double transferDuration=2d*CityFishSupplyCycle.TransferEdgeDuration+
                CityFishSupplyCycle.HandlingUnits*CityFishSupplyCycle.TransferUnitDuration;
            double productionDuration=0;
            foreach(CityCanneryProductionStage stage in Enum.GetValues(typeof(CityCanneryProductionStage)))
                if(stage!=CityCanneryProductionStage.Idle)productionDuration+=cycle.ProductionStageDuration(stage);
            Assert.That(cycle.StageDuration(CityFishSupplyStage.UnloadFish),Is.EqualTo(transferDuration));
            double unload=cycle.StageStart(CityFishSupplyStage.UnloadFish);
            double first=cycle.ProductionStageStart(CityCanneryProductionStage.Prepare);
            double firstReceived=CityFishSupplyCycle.TransferEdgeDuration+CityFishSupplyCycle.TransferUnitDuration;
            Assert.That(first-unload,Is.EqualTo(firstReceived).Within(.000001d),
                "Receiving the first of three crates immediately starts production.");
            Assert.That(cycle.Sample(first-.001d).Production.IsActive,Is.False);
            cannery.ApplyAt(first);
            Assert.That(cannery.Snapshot.Stage,Is.EqualTo(CityFishSupplyStage.UnloadFish));
            Assert.That(cannery.Snapshot.Handled,Is.EqualTo(1));
            Assert.That(cannery.Snapshot.TruckFish,Is.EqualTo(2));
            Assert.That(cannery.Snapshot.Production.Stage,Is.EqualTo(CityCanneryProductionStage.Prepare));
            Assert.That(cannery.Snapshot.Production.UnitCount,Is.EqualTo(1));
            Assert.That(cannery.Snapshot.Production.Seconds,Is.Zero);
            Assert.That(cannery.Snapshot.FactoryCases,Is.Zero);
            Transform firstCrate=cannery.transform.Find("Fish handling unit 2");
            Transform firstStore=CityCanneryAssetProvider.FindPart(cannery.Equipment.gameObject,"ANCHOR_RawStore2");
            Assert.That(firstCrate.gameObject.activeSelf,Is.True);
            Assert.That(Vector3.Distance(firstCrate.position,firstStore.position),Is.LessThan(.002f));

            double firstFinished=cycle.ProductionStageStart(CityCanneryProductionStage.Pack)+
                cycle.ProductionStageDuration(CityCanneryProductionStage.Pack);
            Assert.That(firstFinished-unload,Is.EqualTo(firstReceived+productionDuration).Within(.000001d));
            cannery.ApplyAt(firstFinished);
            Assert.That(cannery.Snapshot.Stage,Is.EqualTo(CityFishSupplyStage.UnloadFish));
            Assert.That(cannery.Snapshot.FactoryCases,Is.EqualTo(1),
                "The first case exists while the same truck still holds raw fish.");
            Assert.That(cannery.Snapshot.TruckFish,Is.EqualTo(1));

            int[] counts={1,1,1};
            Assert.That(cycle.ProductionLotCount,Is.EqualTo(counts.Length));
            int prepared=0;
            for(int lot=0;lot<counts.Length;lot++)
            {
                Assert.That(cycle.ProductionLotUnitCount(lot),Is.EqualTo(counts[lot]));
                Assert.That(cycle.ProductionStageStart(CityCanneryProductionStage.Prepare,lot)-unload,
                    Is.EqualTo(firstReceived+lot*productionDuration).Within(.000001d));
                foreach(CityCanneryProductionStage stage in Enum.GetValues(typeof(CityCanneryProductionStage)))
                {
                    if(stage==CityCanneryProductionStage.Idle)continue;
                    double start=cycle.ProductionStageStart(stage,lot);
                    foreach(double offset in new[]{-.001d,0d,.001d,cycle.ProductionStageDuration(stage)*.5d})
                    {
                        cannery.ApplyAt(start+offset);
                        Assert.That(cannery.Snapshot.AccountedUnits,Is.EqualTo(3),$"Lot {lot}/{stage}/{offset} custody");
                        Assert.That(cannery.Snapshot.FactoryFish,Is.InRange(0,3));
                        Assert.That(cannery.Snapshot.InProcess,Is.InRange(0,3));
                        Assert.That(cannery.Snapshot.FactoryCases,Is.InRange(0,3));
                        int visibleRaw=0,visibleCases=0;
                        for(int i=0;i<CityFishSupplyCycle.HandlingUnits;i++)
                        {
                            if(cannery.transform.Find("Fish handling unit "+i).gameObject.activeSelf)visibleRaw++;
                            if(cannery.transform.Find("Finished handling unit "+i).gameObject.activeSelf)visibleCases++;
                        }
                        Assert.That(visibleRaw,Is.EqualTo(cannery.Snapshot.TruckFish+cannery.Snapshot.FactoryFish));
                        Assert.That(visibleCases,Is.EqualTo(cannery.Snapshot.FactoryCases));
                        Assert.That(visibleRaw+cannery.Snapshot.InProcess+visibleCases,Is.EqualTo(3),
                            $"Lot {lot}/{stage}/{offset} visible cargo must agree with custody.");
                    }
                }
                for(int unit=1;unit<=counts[lot];unit++)
                {
                    cannery.ApplyAt(cycle.ProductionStageStart(CityCanneryProductionStage.Prepare,lot)+
                        cycle.ProductionStageDuration(CityCanneryProductionStage.Prepare)*unit/counts[lot]);
                    prepared++;
                    Assert.That(cannery.Snapshot.Production.PreparedUnits,Is.EqualTo(prepared));
                    for(int ordinal=0;ordinal<CityFishSupplyCycle.HandlingUnits;ordinal++)
                    {
                        int id=CityFishSupplyCycle.HandlingUnits-1-ordinal;
                        Transform raw=cannery.transform.Find("Fish handling unit "+id);
                        Assert.That(raw.gameObject.activeSelf,Is.EqualTo(ordinal>=prepared),
                            $"Raw crate {id} must be consumed in reverse-unload FIFO order at lot {lot}, unit {unit}.");
                    }
                }
                ValidateCanneryProductionReturn(cannery,lot);
            }
            double complete=cycle.StageStart(CityFishSupplyStage.LoadFinished);
            Assert.That(complete-unload,Is.EqualTo(firstReceived+counts.Length*productionDuration).Within(.000001d),
                "All three FIFO lots finish before the truck starts loading the three cases.");
            for(int unit=0;unit<CityFishSupplyCycle.HandlingUnits;unit++)
            foreach(double offset in new[]{-.001d,0d,.001d})
                Assert.That(cycle.Sample(cycle.TransferUnitStart(CityFishSupplyStage.UnloadFish,unit)+
                    CityFishSupplyCycle.TransferUnitDuration+offset).AccountedUnits,Is.EqualTo(3),
                    $"Receiving boundary {unit} preserves all three handling units.");
            cannery.ApplyAt(complete);
            Assert.That(cannery.Snapshot.FactoryCases,Is.EqualTo(3));
            Assert.That(cannery.Snapshot.FactoryFish+cannery.Snapshot.InProcess,Is.Zero);
            for(int i=0;i<CityFishSupplyCycle.HandlingUnits;i++)
                Assert.That(cannery.transform.Find("Finished handling unit "+i).gameObject.activeSelf,Is.True);
            cannery.ApplyAt(cycle.Duration*2+firstFinished);
            Assert.That(cannery.Snapshot.Batch,Is.EqualTo(2));
            Assert.That(cannery.Snapshot.FactoryCases,Is.EqualTo(1));
            Assert.That(cannery.Snapshot.AccountedUnits,Is.EqualTo(3));
        }

        private static void ValidateCanneryProductionReturn(CityCanneryController cannery,int lot)
        {
            Transform worker=cannery.transform.Find("Cannery Retort and Packing Worker");
            Transform packing=CityCanneryAssetProvider.FindPart(cannery.Equipment.gameObject,"ANCHOR_PackingWorker");
            Transform retort=CityCanneryAssetProvider.FindPart(cannery.Equipment.gameObject,"ANCHOR_RetortOperator");
            double end=cannery.Cycle.ProductionStageStart(CityCanneryProductionStage.Pack,lot)+
                cannery.Cycle.ProductionStageDuration(CityCanneryProductionStage.Pack);
            foreach(double boundary in new[]{end,end+6d})
            {
                cannery.ApplyAt(boundary-.001d);
                Vector3 before=worker.position;
                cannery.ApplyAt(boundary+.001d);
                Assert.That(Vector3.Distance(worker.position,before),Is.LessThan(.01f),
                    $"Retort worker must not jump at lot {lot} return boundary {boundary-end}.");
            }
            cannery.ApplyAt(end);
            Assert.That(Vector3.Distance(worker.position,packing.position),Is.LessThan(.002f));
            cannery.ApplyAt(end+3d);
            Assert.That(Vector3.Distance(worker.position,packing.position),Is.GreaterThan(.5f));
            Assert.That(Vector3.Distance(worker.position,retort.position),Is.GreaterThan(.5f));
            Assert.That(worker.position.y,Is.EqualTo(retort.position.y).Within(.002f));
            cannery.ApplyAt(end+6d);
            Assert.That(Vector3.Distance(worker.position,retort.position),Is.LessThan(.002f));
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
                cannery.ApplyAt(CanneryTime(cannery,CityCanneryProductionStage.Prepare,.2f));
                crew.ApplyAt(port.ElapsedSeconds);
                Transform worker=cannery.transform.Find("Cannery Preparation Worker");
                Transform spine=CityCanneryAssetProvider.FindPart(worker.gameObject,"spine");
                Quaternion frozenSpine=spine.localRotation;
                Vector3 frozenSeamer=CityCanneryAssetProvider.FindPart(cannery.Equipment.gameObject,"MOVE_SeamerHead").position;
                cannery.ApplyAt(cannery.Cycle.ProductionStageStart(CityCanneryProductionStage.Prepare,1));
                DeferCanneryContract(failures,"distant overlapping completion",()=>
                {
                    Assert.That(cannery.Snapshot.Stage,Is.EqualTo(CityFishSupplyStage.UnloadFish));
                    Assert.That(cannery.Snapshot.FactoryCases,Is.EqualTo(1));
                    Assert.That(cannery.Snapshot.TruckFish,Is.EqualTo(1));
                    Assert.That(cannery.Snapshot.AccountedUnits,Is.EqualTo(3));
                });
                cannery.ApplyAt(CanneryTime(cannery,CityCanneryProductionStage.Seal,.5f));
                DeferCanneryContract(failures,"distant simulation and presentation",()=>
                {
                    Assert.That(cannery.Snapshot.Production.Stage,Is.EqualTo(CityCanneryProductionStage.Seal));
                    Assert.That(cannery.Snapshot.Stage,Is.EqualTo(CityFishSupplyStage.UnloadFish));
                    Assert.That(cannery.Snapshot.TruckFish,Is.EqualTo(2));
                    Assert.That(cannery.Snapshot.AccountedUnits,Is.EqualTo(3));
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

                // Receiving joins the truck and factory visibility ranges;
                // isolate the factory hysteresis after that transfer ends.
                double lateSeal=CanneryTime(cannery,CityCanneryProductionStage.Seal,.5f,2);
                cannery.ApplyAt(lateSeal);
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
                        Assert.That(cannery.Snapshot.Production.Stage,Is.EqualTo(CityCanneryProductionStage.Seal));
                        Assert.That(cannery.Snapshot.Stage,Is.EqualTo(CityFishSupplyStage.WaitForProduction));
                        Assert.That(cannery.FactoryPresentationActive,Is.True);
                        Assert.That(cannery.WorkerHandsMatch,Is.True,cannery.LastCrewContactFailure);
                        Assert.That(cannery.WorkingSeconds,Is.EqualTo(lateSeal));
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

        private static void ValidateCanneryVisualProducts(CityCanneryController cannery)
        {
            foreach(string role in new[]{"CanneryFloor","WetFloor","WashWall","Stainless","Insulation","Cardboard"})
            {
                Material material=CityCanneryAssetProvider.GetSurfaceMaterial(role);
                var texture=material.GetTexture("_BaseMap") as Texture2D;
                Assert.That(texture,Is.Not.Null,role);
                Assert.That(texture.width,Is.LessThanOrEqualTo(512));
                Assert.That(texture.mipmapCount,Is.GreaterThan(1));
                Assert.That(texture.wrapMode,Is.EqualTo(TextureWrapMode.Repeat));
                Assert.That(CityCanneryAssetProvider.GetSurfaceMaterial(role),Is.SameAs(material));
            }
            cannery.ApplyAt(CanneryTime(cannery,CityCanneryProductionStage.Fill,.5f));
            Assert.That(cannery.VisibleFilledCanCount,Is.InRange(1,14));
            Assert.That(cannery.VisibleSealedCanCount,Is.Zero);
            Transform preparation=cannery.transform.Find("Cannery Preparation Worker");
            foreach(string section in new[]{"ApronBib","ApronSkirt"})
            {
                Transform apron=CityCanneryAssetProvider.FindPart(preparation.gameObject,section);
                Vector3 low=Vector3.positiveInfinity,high=Vector3.negativeInfinity;
                foreach(MeshFilter mesh in apron.GetComponentsInChildren<MeshFilter>(true))
                foreach(Vector3 vertex in mesh.sharedMesh.vertices)
                {
                    Vector3 point=preparation.InverseTransformPoint(mesh.transform.TransformPoint(vertex));
                    low=Vector3.Min(low,point); high=Vector3.Max(high,point);
                }
                Assert.That(high.y-low.y,Is.GreaterThan(.35f),section+" hangs vertically");
                Assert.That(high.z-low.z,Is.LessThan(.20f),section+" remains a thin garment");
            }
            cannery.ApplyAt(CanneryTime(cannery,CityCanneryProductionStage.Seal,.5f));
            Assert.That(cannery.VisibleFilledCanCount,Is.EqualTo(15));
            Assert.That(cannery.VisibleSealedCanCount,Is.InRange(1,14));
            cannery.ApplyAt(CanneryTime(cannery,CityCanneryProductionStage.LoadRetort,.5f));
            Assert.That(cannery.VisibleSealedCanCount,Is.EqualTo(15));
            var units=new Transform[15];
            for(int i=0;i<15;i++) units[i]=CityCanneryAssetProvider.FindPart(cannery.transform.gameObject,"CanUnit"+i.ToString("D2"));
            cannery.ApplyAt(CanneryTime(cannery,CityCanneryProductionStage.Pack,.999f));
            for(int i=0;i<15;i++)
            {
                Transform target=CityCanneryAssetProvider.FindPart(cannery.Equipment.gameObject,"ANCHOR_PackingCan"+i.ToString("D2"));
                Assert.That(Vector3.Distance(units[i].position,target.position),Is.LessThan(.002f),"Same can reaches its carton slot "+i);
                Assert.That(units[i].gameObject.activeInHierarchy,Is.True);
                // Inspect rendered lid vertices: an imported can must stand
                // upright, with its lid at the authored height above its base.
                Transform lid=CityCanneryAssetProvider.FindPart(units[i].gameObject,"CanLid"+i.ToString("D2"));
                foreach(MeshFilter mesh in lid.GetComponentsInChildren<MeshFilter>(true))
                foreach(Vector3 vertex in mesh.sharedMesh.vertices)
                    Assert.That(mesh.transform.TransformPoint(vertex).y-units[i].position.y,
                        Is.InRange(.093f,.104f),"Upright can lid "+i);
            }
            cannery.ApplyAt(CanneryTime(cannery,CityCanneryProductionStage.Heat,.5f));
            Assert.That(cannery.RetortPressureFactor,Is.GreaterThan(.9f));
            Assert.That(cannery.SteamParticleCount,Is.Zero);
            double vent=cannery.Cycle.ProductionStageStart(CityCanneryProductionStage.Cool)+1.1d/2d;
            cannery.ApplyAt(vent);
            int particles=cannery.SteamParticleCount;
            Assert.That(particles,Is.InRange(1,10));
            cannery.ApplyAt(CanneryTime(cannery,CityCanneryProductionStage.Cool,.7f));
            Assert.That(cannery.SteamParticleCount,Is.Zero);
            cannery.ApplyAt(vent);
            Assert.That(cannery.SteamParticleCount,Is.EqualTo(particles),"Seek restores only current vent particles.");
        }

        private static void ValidateCanneryCustody(CityFishSupplyCycle cycle)
        {
            foreach(CityFishSupplyStage stage in Enum.GetValues(typeof(CityFishSupplyStage)))
            for(int batch=0;batch<2;batch++)
            {
                if(cycle.StageDuration(stage,batch)<=0d)continue;
                double start=cycle.StageStart(stage,batch);
                foreach(float fraction in new[]{0f,.07f,.26f,.5f,.74f,.93f,.99999f})
                {
                    CityFishSupplySnapshot state=cycle.Sample(start+cycle.StageDuration(stage,batch)*fraction);
                    Assert.That(state.Stage,Is.EqualTo(stage));
                    Assert.That(state.Batch,Is.EqualTo(batch));
                    if(stage<=CityFishSupplyStage.LoadFish)
                    {
                        Assert.That(state.AccountedUnits,Is.EqualTo(state.PortStored));
                        Assert.That(state.PortFish+state.TruckFish,
                            Is.EqualTo(CityPortCycle.Sample(state.PortSeconds).StoredCargo));
                    }
                    else Assert.That(state.AccountedUnits,Is.EqualTo(3),$"Custody at {stage}/{fraction}");
                    if(start+cycle.StageDuration(stage,batch)*fraction<
                        cycle.ProductionStageStart(CityCanneryProductionStage.Pack,0,batch))
                        Assert.That(state.FactoryCases+state.TruckCases+state.DeliveredCases,Is.Zero);
                }
            }
            Assert.That(cycle.Sample(cycle.Duration).Batch,Is.EqualTo(1));
            Assert.That(cycle.Sample(cycle.Duration).Stage,Is.EqualTo(CityFishSupplyStage.PortVisit));
            Assert.Throws<ArgumentOutOfRangeException>(()=>cycle.Sample(double.NaN));
            Assert.Throws<ArgumentOutOfRangeException>(()=>cycle.Sample(-1));
        }

        private static void ValidateCanneryConcurrentPortLoading(CityCanneryController cannery, CityPortController port)
        {
            CityPortCrew crew=port.GetComponentInChildren<CityPortCrew>(true);
            Assert.That(crew,Is.Not.Null);
            Transform driver=cannery.transform.Find("Fish Delivery Driver");
            Transform cart=cannery.PortTrolley;
            Assert.That(driver,Is.Not.Null);
            Assert.That(cart,Is.Not.Null);
            bool manual=crew.UseManualClock;
            double saved=cannery.WorkingSeconds;
            crew.UseManualClock=true;
            try
            {
                for(int batch=0;batch<2;batch++)
                {
                double start=cannery.Cycle.StageStart(CityFishSupplyStage.LoadFish,batch);
                double end=start+cannery.Cycle.StageDuration(CityFishSupplyStage.LoadFish,batch);
                double pickup=cannery.Cycle.TransferUnitStart(CityFishSupplyStage.LoadFish,0,batch)+
                    CityFishSupplyCycle.TransferUnitDuration*.18d;
                double lastStored=cannery.Cycle.PortEventTime(CityFishSupplyCycle.FirstPortCrateStoredAtSeconds +
                    (CityPortCycle.CargoCount - 1) * CityPortCycle.CargoDurationSeconds, batch);
                if(batch==0) Assert.That(pickup,Is.LessThan(lastStored),
                    "The actual city route must allow the first loading pickup before the dock worker stores the last crate.");
                // Check the three independent ship-to-store custody boundaries,
                // including the new simultaneous ship/truck presentation.
                for(int unit=0;unit<CityPortCycle.CargoCount;unit++)
                foreach(double edge in new[]{-.001d,0d,.001d})
                {
                    cannery.ApplyAt(cannery.Cycle.PortEventTime(CityFishSupplyCycle.FirstPortCrateStoredAtSeconds +
                        unit * CityPortCycle.CargoDurationSeconds, batch) + edge);
                    ValidateCanneryPortCargoVisibility(cannery,port);
                }
                for(double seconds=start;seconds<end;seconds+=.5d)
                {
                    cannery.ApplyAt(seconds);
                    crew.ApplyAt(port.ElapsedSeconds,seconds);
                    ValidateCanneryPortCargoVisibility(cannery,port);
                    Physics.SyncTransforms();
                    Vector3 body=driver.position;
                    foreach(Collider obstacle in Physics.OverlapCapsule(body+Vector3.up*.35f,
                        body+Vector3.up*1.53f,.2f,~0,QueryTriggerInteraction.Ignore))
                        Assert.That(obstacle.transform.IsChildOf(cannery.transform),Is.True,
                            $"The driver enters {obstacle.name} while handling at {seconds:F2}, body {body:F3}.");
                    Vector3 separation=driver.position-crew.ShoreWorker.transform.position;
                    if(Mathf.Abs(separation.y)<1.6f)
                        Assert.That(new Vector2(separation.x,separation.z).magnitude,Is.GreaterThan(.5f),
                            $"The driver and dock worker need distinct body space at {seconds:F2}.");
                    if(!cannery.Snapshot.WaitingForPortAccess&&cannery.Snapshot.Seconds>=CityFishSupplyCycle.TrolleyReadyDuration&&
                        cannery.Snapshot.Seconds<cannery.Snapshot.Duration-CityFishSupplyCycle.TransferEdgeDuration&&
                        cannery.Snapshot.TransferProgress<.35f)
                    {
                        separation=cart.position-port.Trolley.position;
                        Assert.That(new Vector2(separation.x,separation.z).magnitude,Is.GreaterThan(1.55f),
                            $"Both carts must retain their physical work space during a fetch at {seconds:F2}.");
                        separation=driver.position-port.Trolley.position;
                        Assert.That(new Vector2(separation.x,separation.z).magnitude,Is.GreaterThan(1.15f),
                            $"The driver must clear the dock cart at {seconds:F2}.");
                    }
                    if(cannery.DriverWaitingForDockWorker)
                    {
                        Assert.That(Vector3.Distance(cart.position,cannery.PortTrolleyQueuePosition),Is.LessThan(.002f),
                            "Every blocked pickup waits at the warehouse entrance.");
                        Assert.That(cannery.Snapshot.TransferProgress,Is.Zero);
                    }
                }
                }
            }
            finally
            {
                crew.UseManualClock=manual;
                cannery.ApplyAt(saved);
                crew.ApplyAt(port.ElapsedSeconds);
            }
        }

        private static void ValidateCanneryPortCargoVisibility(CityCanneryController cannery,CityPortController port)
        {
            int visible=0;
            for(int unit=0;unit<CityPortCycle.CargoCount;unit++)
            {
                bool atSupply=cannery.transform.Find("Fish handling unit "+unit).gameObject.activeSelf;
                bool atShip=port.Cargo[unit].gameObject.activeSelf;
                Assert.That(atSupply,Is.EqualTo(unit<cannery.Snapshot.PortStored),
                    $"Supply crate {unit} only exists after its actual ship-to-store handoff.");
                Assert.That(atSupply&&atShip,Is.False,"One finite unit cannot have both port and supply bodies.");
                if(atSupply)visible++;
                if(atShip)visible++;
            }
            Assert.That(visible,Is.EqualTo(CityPortCycle.CargoCount),"Seeking retains exactly the shipped batch.");
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
            cannery.ApplyAt(CanneryTime(cannery,CityFishSupplyStage.WaitForProduction,.5f));
            Bounds truck=PortLocalMeshBounds(cannery.Truck);
            Assert.That(truck.size.x,Is.InRange(2.38f,2.42f));
            Assert.That(truck.max.z,Is.InRange(4.35f,4.52f));
            Assert.That(truck.min.z,Is.InRange(-2.02f,-1.98f));
            cannery.ApplyAt(CanneryTime(cannery,CityCanneryProductionStage.Prepare,.5f));
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
            CityCanneryTruckLeg[] ordered={CityCanneryTruckLeg.FactoryToPort,CityCanneryTruckLeg.PortArrive,CityCanneryTruckLeg.PortReverse,
                CityCanneryTruckLeg.PortToFactory,CityCanneryTruckLeg.FactoryReverse,
                CityCanneryTruckLeg.FactoryToShop,CityCanneryTruckLeg.ShopToFactory,CityCanneryTruckLeg.FactoryReverse};
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
                var routeObstacles = new List<string>();
                foreach(CityCanneryTruckLeg leg in ordered)
                {
                    CityFishSupplyStage stage=CanneryStage(leg);
                    int steps=Mathf.Max(2,Mathf.CeilToInt(cannery.Route.Length(leg)/3f));
                    int reported = 0;
                    for(int i=0;i<=steps;i++)
                    {
                        double time=CanneryTime(cannery,stage,Mathf.Min(i/(float)steps,.99999f));
                        cannery.ApplyAt(time);
                        Physics.SyncTransforms();
                        if(cannery.DetectObstacle() && reported++ < 3)
                            routeObstacles.Add($"{leg} {i}/{steps}: {cannery.Truck.position}; {cannery.LastObstacleName}");
                    }
                    Debug.Log($"CANNERY ROUTE {leg}: {cannery.Route.Length(leg):F2} m");
                }
                Assert.That(routeObstacles, Is.Empty, "Static obstacles on the actual vehicle routes: " + string.Join("; ", routeObstacles));
                cannery.ApplyAt(CanneryTime(cannery,CityFishSupplyStage.PortToFactory,.5f));
                var blocker=new GameObject("Cannery stopped-traffic probe");
                try
                {
                    CityPortTruckPose future=cannery.TruckPose(cannery.Cycle.Sample(cannery.WorkingSeconds+.7d));
                    blocker.transform.SetPositionAndRotation(future.RearAxle+
                        future.Rotation*new Vector3(0,1,CityCanneryTruckDimensions.Front-.4f),future.Rotation);
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
            Debug.Log($"CANNERY TRAFFIC: {shared} shared service edges; opposing lanes remain open; only the upcoming manoeuvre is reserved.");

            cannery.ApplyAt(CanneryTime(cannery,CityCanneryProductionStage.Prepare,.5f));
            cannery.Traffic.Release();
            Assert.That(city.Bus.CanneryBlocksSpawn(cannery.Truck.position,cannery.Truck.rotation),Is.True,
                "Bus spawn eligibility includes the physical delivery body.");
            Assert.That(city.Bus.CanneryBlocksSpawn(cannery.Truck.position+Vector3.up*1000,
                cannery.Truck.rotation),Is.False);
            Bounds busBounds=city.Bus.Actor.LocalVisualBounds;
            var traffic=new CityCanneryTraffic(cannery,null);
            CityFishSupplyStage[] starts={CityFishSupplyStage.PortToFactory,
                CityFishSupplyStage.FactoryToShop,CityFishSupplyStage.FactoryToPort,CityFishSupplyStage.ShopToFactory};
            CityCanneryTruckLeg[] legs={CityCanneryTruckLeg.PortToFactory,
                CityCanneryTruckLeg.FactoryToShop,CityCanneryTruckLeg.FactoryToPort,CityCanneryTruckLeg.ShopToFactory};
            for(int trip=0;trip<4;trip++)
            {
                CityFishSupplySnapshot departure=cannery.Cycle.Sample(CanneryTime(cannery,starts[trip],0));
                bool holdingPose=false;
                foreach(int index in city.BusPlan.OrderedLinkIndices)
                {
                    IReadOnlyList<CityBusPathSample> samples=city.BusPlan.Links[index].Samples;
                    for(int sample=0;sample<samples.Count;sample+=40)
                    {
                        CityBusPathSample pose=samples[sample];
                        if(!traffic.CanReserveAt(departure,pose.Position,
                            Quaternion.LookRotation(pose.Forward),busBounds))continue;
                        holdingPose=true;break;
                    }
                    if(holdingPose)break;
                }
                Assert.That(holdingPose,Is.True,$"Trip {trip} must leave a real bus holding pose outside its sweep.");
                CityPortTruckPose crossing=cannery.TruckPose(cannery.Cycle.Sample(CanneryTime(cannery,starts[trip],0)+3d));
                Assert.That(traffic.CanReserveAt(departure,crossing.RearAxle,crossing.Rotation,busBounds),Is.False,
                    "Reservation cannot be granted over a bus already occupying the truck corridor.");
                Assert.That(traffic.TryAcquire(departure),Is.True,"An absent bus leaves a clear delivery trip.");
                Assert.That(traffic.ReservedTrip,Is.EqualTo(trip));
                Assert.That(traffic.BlocksSpawn(crossing.RearAxle,crossing.Rotation,busBounds),Is.True,
                    "The bus cannot spawn into an upcoming reserved crossing.");
                CityFishSupplyStage last=trip==0?CityFishSupplyStage.FactoryReverse:
                    trip==2?CityFishSupplyStage.PortReverse:trip==3?CityFishSupplyStage.FactoryReturnReverse:
                    CityFishSupplyStage.FactoryToShop;
                Assert.That(traffic.TryAcquire(cannery.Cycle.Sample(CanneryTime(cannery,last,.99f))),Is.True);
                Assert.That(traffic.ReservedTrip,Is.EqualTo(trip),"The reservation survives reverse/arrival sub-legs.");
                Assert.That(traffic.TryAcquire(cannery.Cycle.Sample(CanneryTime(cannery,
                    CityCanneryProductionStage.Prepare,.5f))),Is.True);
                Assert.That(traffic.HasReservation,Is.False,"Parking releases street service traffic.");
            }
        }

        private static void ValidateCanneryReconstruction(CityGameRoot city,CityCanneryController cannery,CityPortController port)
        {
            double seek=TransferTime(cannery,CityFishSupplyStage.UnloadFish,2,.49f);
            cannery.ApplyAt(seek);
            Vector3 truck=cannery.Truck.position,lift=cannery.TailLift.position;
            Transform fish=cannery.transform.Find("Fish handling unit 0");
            Assert.That(fish,Is.Not.Null);
            Vector3 cargo=fish.position;
            Transform can=CityCanneryAssetProvider.FindPart(cannery.transform.gameObject,"CanUnit09");
            Vector3 canPosition=can.position;
            Quaternion canRotation=can.rotation;
            CityCanneryProductionSnapshot production=cannery.Snapshot.Production;
            cannery.ApplyAt(cannery.Cycle.Duration*2+10);
            cannery.ApplyAt(seek);
            Assert.That(cannery.Truck.position,Is.EqualTo(truck));
            Assert.That(cannery.TailLift.position,Is.EqualTo(lift));
            Assert.That(fish.position,Is.EqualTo(cargo));
            Assert.That(cannery.Snapshot.Production.Stage,Is.EqualTo(production.Stage));
            Assert.That(cannery.Snapshot.Production.Seconds,Is.EqualTo(production.Seconds));
            Assert.That(can.position,Is.EqualTo(canPosition));
            Assert.That(can.rotation,Is.EqualTo(canRotation));
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
                Assert.That(Vector3.Distance(other.transform.Find("Fish handling unit 0").position,cargo),Is.LessThan(.001f));
                Assert.That(other.Snapshot.AccountedUnits,Is.EqualTo(cannery.Snapshot.AccountedUnits));
                Assert.That(other.Snapshot.Production.Stage,Is.EqualTo(production.Stage));
                Assert.That(other.Snapshot.Production.Seconds,Is.EqualTo(production.Seconds));
                Assert.That(Vector3.Distance(CityCanneryAssetProvider.FindPart(other.transform.gameObject,
                    "CanUnit09").position,canPosition),Is.LessThan(.001f));
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
                case CityCanneryTruckLeg.FactoryToPort:return CityFishSupplyStage.FactoryToPort;
                default:return CityFishSupplyStage.ShopToFactory;
            }
        }
        private static double CanneryTime(CityCanneryController cannery,CityFishSupplyStage stage,float progress) =>
            cannery.Cycle.StageStart(stage)+cannery.Cycle.StageDuration(stage)*progress;
        private static double CanneryTime(CityCanneryController cannery,CityCanneryProductionStage stage,float progress,int lot=0) =>
            cannery.Cycle.ProductionStageStart(stage,lot)+cannery.Cycle.ProductionStageDuration(stage)*progress;
        private static double TransferTime(CityCanneryController cannery,CityFishSupplyStage stage,int unit,float progress) =>
            cannery.Cycle.TransferUnitStart(stage,unit)+CityFishSupplyCycle.TransferUnitDuration*progress;

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

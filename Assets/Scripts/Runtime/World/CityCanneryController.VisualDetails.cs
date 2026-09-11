using UnityEngine;
using UnityEngine.Rendering;

namespace BarPromenade
{
    public sealed partial class CityCanneryController
    {
        private readonly Transform[] canUnits=new Transform[15], canContents=new Transform[15], canLids=new Transform[15];
        private readonly Vector3[] canDocks=new Vector3[15], lidDocks=new Vector3[15];
        private readonly Vector3[] packingDocks=new Vector3[15];
        private readonly Quaternion[] canRest=new Quaternion[15];
        private readonly Transform[] locks=new Transform[4], feedRollers=new Transform[8], seamRollers=new Transform[2];
        private readonly Vector3[] lockDocks=new Vector3[4], seamRollerDocks=new Vector3[2];
        private readonly Quaternion[] lockRest=new Quaternion[4], rollerRest=new Quaternion[8], seamRollerRest=new Quaternion[2];
        private Transform pressureNeedle, packingCarton, packingFeedRoller, canSupply, lidSupply;
        private Quaternion pressureRest, packingRollerRest;
        private Renderer[] seamIndicators, retortIndicators;
        private MaterialPropertyBlock indicatorBlock;
        private ParticleSystem steam;
        private readonly ParticleSystem.Particle[] steamParticles=new ParticleSystem.Particle[10];
        private bool packingCanInHand;
        private Vector3 packingCanContact;
        private float packingCanHandWeight;
        public int VisibleFilledCanCount { get; private set; }
        public int VisibleSealedCanCount { get; private set; }
        public float RetortPressureFactor { get; private set; }
        public int SteamParticleCount => steam==null?0:steam.particleCount;

        private void CreateVisualDetails()
        {
            for(int i=0;i<15;i++)
            {
                canUnits[i]=Require(tray,"CanUnit"+i.ToString("D2"));
                canContents[i]=Require(canUnits[i],"CanContents"+i.ToString("D2"));
                canLids[i]=Require(canUnits[i],"CanLid"+i.ToString("D2"));
                canDocks[i]=tray.InverseTransformPoint(canUnits[i].position);
                // These authored groups retain their FBX axis correction.
                canRest[i]=Quaternion.Inverse(Plan.Rotation)*canUnits[i].rotation;
                lidDocks[i]=canUnits[i].InverseTransformPoint(canLids[i].position);
                packingDocks[i]=Anchor("PackingCan"+i.ToString("D2"));
            }
            for(int i=0;i<4;i++)
            {
                locks[i]=Require(equipment,"MOVE_RetortLock"+i);
                lockDocks[i]=retortDoor.InverseTransformPoint(locks[i].position);
                lockRest[i]=Quaternion.Inverse(retortDoor.rotation)*locks[i].rotation;
            }
            for(int i=0;i<8;i++)
            {
                feedRollers[i]=Require(equipment,"MOVE_ConveyorRoller"+i.ToString("D2"));
                rollerRest[i]=Quaternion.Inverse(factory.rotation)*feedRollers[i].rotation;
            }
            for(int i=0;i<2;i++)
            {
                seamRollers[i]=Require(equipment,i==0?"MOVE_SeamerRollerLeft":"MOVE_SeamerRollerRight");
                seamRollerDocks[i]=seamer.InverseTransformPoint(seamRollers[i].position);
                seamRollerRest[i]=Quaternion.Inverse(seamer.rotation)*seamRollers[i].rotation;
            }
            pressureNeedle=Require(equipment,"MOVE_RetortPressureNeedle");
            pressureRest=Quaternion.Inverse(factory.rotation)*pressureNeedle.rotation;
            packingFeedRoller=Require(equipment,"MOVE_PackingFeedRoller");
            packingRollerRest=Quaternion.Inverse(factory.rotation)*packingFeedRoller.rotation;
            packingCarton=Require(equipment,"PackingCarton");
            canSupply=Require(equipment,"CanSupply"); lidSupply=Require(equipment,"LidSupply");
            seamIndicators=Require(equipment,"SeamerRunIndicator").GetComponentsInChildren<Renderer>(true);
            retortIndicators=Require(equipment,"RetortRunIndicator").GetComponentsInChildren<Renderer>(true);
            foreach(Renderer renderer in seamIndicators) renderer.sharedMaterial=CityNightResources.EmissiveMaterial;
            foreach(Renderer renderer in retortIndicators) renderer.sharedMaterial=CityNightResources.EmissiveMaterial;
            indicatorBlock=new MaterialPropertyBlock();
            var host=new GameObject("Cannery Pressure Vent Steam");
            host.transform.SetParent(factory,false);
            steam=host.AddComponent<ParticleSystem>();
            var main=steam.main; main.playOnAwake=false; main.maxParticles=steamParticles.Length;
            main.simulationSpace=ParticleSystemSimulationSpace.World; main.startSpeed=0; main.startLifetime=1;
            var emission=steam.emission; emission.enabled=false;
            var shape=steam.shape; shape.enabled=false;
            var rendererComponent=host.GetComponent<ParticleSystemRenderer>();
            rendererComponent.sharedMaterial=CityNightResources.AtmosphereMaterial;
            rendererComponent.shadowCastingMode=ShadowCastingMode.Off;
            rendererComponent.receiveShadows=false;
            steam.Play(); steam.Pause();
        }

        private void ApplyVisualDetails()
        {
            packingCanInHand=false; packingCanHandWeight=0;
            if(!FactoryPresentationActive)
            {
                if(steam.particleCount>0) steam.Clear();
                return;
            }
            float seconds=(float)Production.Seconds;
            bool filling=Production.Stage==CityCanneryProductionStage.Fill, sealing=Production.Stage==CityCanneryProductionStage.Seal;
            float feed=Mathf.Clamp01((seconds-2)/(float)(Production.Duration-4))*15;
            int active=Mathf.Min(14,Mathf.FloorToInt(feed));
            float phase=feed-active;
            if(filling||sealing)
            {
                Vector3 station=Plan.World(new Vector3(filling?-5.72f:-4.36f,1.19f,-.35f));
                Vector3 from=canDocks[Mathf.Max(0,active-1)]; from.y=0;
                Vector3 to=canDocks[active]; to.y=0;
                Vector3 offset=Vector3.Lerp(from,to,Ease(phase/.2f));
                Vector3 at=station-Plan.Rotation*offset;
                if(seconds<2) at=Vector3.Lerp(station,station-Plan.Rotation*to,Ease(seconds/2));
                if(seconds>Production.Duration-2)
                    at=Vector3.Lerp(at,Plan.World(new Vector3(-4.36f,1.19f,-.35f)),Ease((seconds-(float)Production.Duration+2)/2));
                tray.position=at;
            }
            VisibleFilledCanCount=VisibleSealedCanCount=0;
            float pack=Mathf.Clamp01((seconds-8)/(float)(Production.Duration-10))*15;
            int packing=Mathf.Min(14,Mathf.FloorToInt(pack));
            float packPhase=pack-packing;
            bool isPacking=Production.Stage==CityCanneryProductionStage.Pack;
            // The actual outgoing carton is present on the bench throughout
            // packing and remains the same object through inspection/loading.
            packingCarton.gameObject.SetActive(false);
            canSupply.gameObject.SetActive(Production.Stage<=CityCanneryProductionStage.Fill);
            lidSupply.gameObject.SetActive(Production.Stage<=CityCanneryProductionStage.Seal);
            for(int i=0;i<15;i++)
            {
                bool filled=Production.Stage>CityCanneryProductionStage.Fill||filling&&(i<active||i==active&&phase>=.35f);
                bool sealedCan=Production.Stage>CityCanneryProductionStage.Seal||sealing&&(i<active||i==active&&phase>=.58f);
                canContents[i].gameObject.SetActive(filled);
                canLids[i].gameObject.SetActive(sealedCan||sealing&&i==active&&phase>=.25f);
                if(filled) VisibleFilledCanCount++;
                if(sealedCan) VisibleSealedCanCount++;
                Vector3 position=tray.TransformPoint(canDocks[i]);
                if(isPacking&&seconds>=8)
                {
                    Vector3 target=packingDocks[i];
                    if(i<packing||pack>=15) position=target;
                    else if(i==packing)
                    {
                        Vector3 pickup=Anchor("PackingPickup");
                        // Work in factory coordinates even for a rotated frontage.
                        Vector3 localEdge=Plan.Local(position), localPickup=Plan.Local(pickup);
                        localEdge.z=localPickup.z; localEdge.y=localPickup.y;
                        Vector3 edge=Plan.World(localEdge);
                        if(packPhase<.10f) position=Vector3.Lerp(position,edge,Ease(packPhase/.10f));
                        else if(packPhase<.20f) position=Vector3.Lerp(edge,pickup,Ease((packPhase-.10f)/.10f));
                        else
                        {
                            Vector3 over=target; over.y=Plan.Origin.y+1.49f;
                            float carry=Ease((packPhase-.35f)/.31f);
                            position=Vector3.Lerp(pickup,over,carry)+Vector3.up*(.09f*Mathf.Sin(carry*Mathf.PI));
                            if(packPhase>=.75f) position=Vector3.Lerp(over,target,Ease((packPhase-.75f)/.15f));
                            packingCanInHand=packPhase<.75f;
                            packingCanContact=position+Vector3.up*.075f;
                            packingCanHandWeight=Ease((packPhase-.20f)/.15f)*(1-Ease((packPhase-.66f)/.09f));
                        }
                    }
                }
                canUnits[i].SetPositionAndRotation(position,Plan.Rotation*canRest[i]);
                float lidRaise=sealing&&i==active?.065f*(1-Ease((phase-.25f)/.33f)):0;
                canLids[i].position=canUnits[i].TransformPoint(lidDocks[i])+Vector3.up*lidRaise;
            }
            float press=sealing?Ease((phase-.28f)/.25f)*(1-Ease((phase-.72f)/.23f)):0;
            seamer.position=factory.TransformPoint(seamerDock)-Vector3.up*(.225f*press);
            for(int i=0;i<2;i++)
                seamRollers[i].SetPositionAndRotation(seamer.TransformPoint(seamRollerDocks[i]),seamer.rotation*
                    Quaternion.AngleAxis(sealing?(float)(Production.Seconds*720%360):0,Vector3.up)*seamRollerRest[i]);
            bool drawer=Production.Stage==CityCanneryProductionStage.LoadRetort||Production.Stage==CityCanneryProductionStage.Cool;
            float doorOpen=drawer?Mathf.Min(Ease((seconds-2)/3),Ease(((float)Production.Duration-seconds-1)/3)):0;
            retortDoor.position=factory.TransformPoint(retortDoorDock)+Vector3.up*(1.7f*doorOpen);
            float unlocked=drawer?Mathf.Min(Ease(seconds/1.5f),Ease((float)Production.Duration-seconds)):0;
            for(int i=0;i<4;i++)
                locks[i].SetPositionAndRotation(retortDoor.TransformPoint(lockDocks[i]),retortDoor.rotation*
                    Quaternion.AngleAxis(75*unlocked,Vector3.forward)*lockRest[i]);
            RetortPressureFactor=Production.Stage==CityCanneryProductionStage.Heat?Ease(seconds/5):
                Production.Stage==CityCanneryProductionStage.Cool?1-Ease(seconds/2):0;
            pressureNeedle.rotation=Plan.Rotation*Quaternion.AngleAxis(-115*RetortPressureFactor,Vector3.right)*pressureRest;
            float conveyor=(Production.Stage<CityCanneryProductionStage.LoadRetort?0:Production.Stage==CityCanneryProductionStage.LoadRetort?Mathf.Min(seconds,10):10)*310;
            conveyor+=(Production.Stage<CityCanneryProductionStage.Pack?0:isPacking?Mathf.Min(seconds,8):8)*310;
            conveyor+=(float)((Snapshot.Batch*Cycle.ProductionLotCount+Production.LotIndex)*18d*310d%360d);
            for(int i=0;i<8;i++) feedRollers[i].rotation=Plan.Rotation*Quaternion.AngleAxis(conveyor%360*(i<2?-1:1),
                i%4<2?Vector3.forward:Vector3.right)*rollerRest[i];
            packingFeedRoller.rotation=Plan.Rotation*Quaternion.AngleAxis(isPacking?(Mathf.Floor(pack)*.2f+Mathf.Min(packPhase,.2f))*720:0,
                Vector3.forward)*packingRollerRest;
            SetIndicator(seamIndicators,filling||sealing,new Color(.83f,.53f,.18f));
            SetIndicator(retortIndicators,Production.Stage==CityCanneryProductionStage.Heat,new Color(.79f,.39f,.14f));
            ApplySteam(seconds);
        }

        private void SetIndicator(Renderer[] renderers,bool active,Color color)
        {
            indicatorBlock.SetColor("_BaseColor",color*(active?1.5f:.08f));
            indicatorBlock.SetColor("_EmissionColor",color*(active?1.5f:.08f));
            foreach(Renderer renderer in renderers) renderer.SetPropertyBlock(indicatorBlock);
        }

        private void ApplySteam(float seconds)
        {
            int count=0;
            if(Production.Stage==CityCanneryProductionStage.Cool&&seconds<4)
                for(int i=0;i<steamParticles.Length;i++)
                {
                    float age=seconds-i*.13f;
                    if(age<0||age>1.7f)continue;
                    float progress=age/1.7f;
                    steamParticles[count++]=new ParticleSystem.Particle
                    {
                        position=Anchor("SteamOutlet")+Plan.Forward*(age*.32f)+Vector3.up*(age*.46f)+Plan.Right*(Mathf.Sin(i*2.3f+age)*.07f),
                        startSize=.11f+progress*.38f,startColor=new Color(.65f,.69f,.66f,.16f*Mathf.Sin(progress*Mathf.PI)),
                        startLifetime=1,remainingLifetime=1,randomSeed=(uint)(i+1)
                    };
                }
            steam.SetParticles(steamParticles,count);
        }
    }
}

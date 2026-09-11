using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
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
        [Explicit("One delivery truck: textured cab, physical front, luminous lenses, real road illumination and distance gating.")]
        public IEnumerator CityCanneryTruckAppearance()
        {
            GameSessionState.BeginNewGame();
            GameSessionState.TryStartGameTimeFromWake();
            yield return SceneManager.LoadSceneAsync(SceneIds.City,LoadSceneMode.Single);
            CityGameRoot city=null;
            float deadline=Time.realtimeSinceStartup+TimeoutSeconds;
            while(Time.realtimeSinceStartup<deadline)
            {
                city=Object.FindAnyObjectByType<CityGameRoot>();
                if(city!=null&&city.IsInitialized&&!AreaTravelService.IsComposing)break;
                yield return null;
            }
            Assert.That(city!=null&&city.IsInitialized,Is.True);
            CityCanneryController cannery=city.Cannery;
            Assert.That(cannery,Is.Not.Null);
            cannery.AutoAdvance=false;
            cannery.ForcePresentation=true;
            city.World.Root.GetComponentInChildren<CityPortController>().AutoAdvance=false;
            cannery.ApplyAt(CanneryTime(cannery,CityFishSupplyStage.WaitForProduction,.5f));
            Camera camera=Camera.main;
            Assert.That(camera,Is.Not.Null);
            PlayerCameraFollow follow=camera.GetComponent<PlayerCameraFollow>();
            bool followEnabled=follow!=null&&follow.enabled;
            Vector3 cameraPosition=camera.transform.position,heroPosition=city.Player.GameObject.transform.position;
            Quaternion cameraRotation=camera.transform.rotation;
            float fieldOfView=camera.fieldOfView,aspect=camera.aspect;
            var hidden=new List<Renderer>();
            var failures=new List<Exception>();
            Light[] headlights=cannery.Truck.GetComponentsInChildren<Light>(true);
            using IDisposable pause=GameTimeScaleRuntime.AcquirePause();
            try
            {
                city.Player.Motor.SetInputEnabled(false);
                foreach(Renderer renderer in city.Player.GameObject.GetComponentsInChildren<Renderer>(true))
                    if(renderer.enabled){hidden.Add(renderer);renderer.enabled=false;}
                if(follow!=null)follow.enabled=false;
                camera.aspect=(float)Width/Height;
                GameSessionState.AdvanceGameTime((float)((12d*60d-GameSessionState.GameTimeOfDayMinutes) / GameTimeState.GameMinutesPerRealSecond));
                city.DayNight.ApplyCurrentTime(true);
                for(int frame=0;frame<SettleFrames;frame++)yield return null;
                DeferCanneryContract(failures,"truck painted surfaces",()=>AssertTruckPaintedSurfaces(cannery));
                DeferCanneryContract(failures,"truck physical nose",()=>AssertTruckFrontCoverage(cannery));
                DeferCanneryContract(failures,"truck daytime lamps",()=>AssertTruckHeadlamps(cannery,headlights));
                DeferCanneryContract(failures,"truck moving lamps",()=>AssertTruckMovingHeadlamps(cannery,headlights));
                var daylight=new float[headlights.Length];
                for(int i=0;i<headlights.Length;i++)daylight[i]=headlights[i].intensity;
                yield return CaptureTruckAppearance(camera,cannery,"00-day-front-three-quarter",
                    new Vector3(-5,2.2f,10),new Vector3(0,1.8f,3.6f),58f);
                yield return CaptureTruckAppearance(camera,cannery,"01-day-front-closeup",
                    new Vector3(0,1.65f,7.9f),new Vector3(0,1.58f,5.3f),58f);

                GameSessionState.AdvanceGameTime((float)((21d*60d-GameSessionState.GameTimeOfDayMinutes) / GameTimeState.GameMinutesPerRealSecond));
                city.DayNight.ApplyCurrentTime(true);
                DeferCanneryContract(failures,"truck night lamps",()=>
                {
                    AssertTruckHeadlamps(cannery,headlights);
                    for(int i=0;i<headlights.Length;i++)
                    {
                        Assert.That(headlights[i].intensity,Is.GreaterThanOrEqualTo(daylight[i]));
                        Assert.That(daylight[i],Is.GreaterThanOrEqualTo(headlights[i].intensity*2f/3f-.001f));
                    }
                });
                yield return CaptureTruckAppearance(camera,cannery,"02-night-front-three-quarter",
                    new Vector3(-5,2.2f,10),new Vector3(0,1.8f,3.6f),58f);
                SetTruckAppearanceCamera(camera,cannery,new Vector3(3.5f,2.7f,7.5f),new Vector3(0,0,12),68f);
                for(int frame=0;frame<3;frame++)yield return null;
                DeferCanneryContract(failures,"truck actual road light",()=>AssertTruckRoadLight(camera,cannery,headlights));
                DeferCanneryContract(failures,"truck distant lamps",()=>AssertTruckLampDistance(city,cannery,headlights));
            }
            finally
            {
                cannery.ForcePresentation=true;
                city.Player.Motor.Teleport(heroPosition);
                cannery.RefreshPresentation();
                foreach(Renderer renderer in hidden)if(renderer!=null)renderer.enabled=true;
                camera.transform.SetPositionAndRotation(cameraPosition,cameraRotation);
                camera.fieldOfView=fieldOfView;camera.aspect=aspect;
                if(follow!=null)follow.enabled=followEnabled;
            }
            if(failures.Count>0)throw new AggregateException("Truck appearance contracts failed; direct day/night frames retained.",failures);
        }

        private static void AssertTruckPaintedSurfaces(CityCanneryController cannery)
        {
            foreach(string role in new[]{"Steel","Insulation"})
            {
                Material material=CityCanneryAssetProvider.GetSurfaceMaterial(role);
                var texture=material.GetTexture("_BaseMap") as Texture2D;
                Assert.That(texture,Is.Not.Null);
                Assert.That(texture.width,Is.LessThanOrEqualTo(512));
                Assert.That(texture.mipmapCount,Is.GreaterThan(1));
                Assert.That(texture.wrapMode,Is.EqualTo(TextureWrapMode.Repeat));
                string[] names=role=="Steel"?new[]{"TruckVisible__Steel","DriverDoorVisible__Steel","TruckFrontPanel__Steel"}:
                    new[]{"TruckVisible__Insulation","RearDoorVisibleLeft__Insulation","RearDoorVisibleRight__Insulation"};
                foreach(string name in names)
                {
                    Transform part=CityCanneryAssetProvider.FindPart(cannery.Truck.gameObject,name);
                    MeshRenderer renderer=part.GetComponent<MeshRenderer>();
                    Assert.That(renderer.sharedMaterial,Is.SameAs(material),name+" must use its shared textured surface.");
                    Mesh mesh=part.GetComponent<MeshFilter>().sharedMesh;
                    Assert.That(mesh.uv.Length,Is.EqualTo(mesh.vertexCount));
                    Vector2 low=Vector2.positiveInfinity,high=Vector2.negativeInfinity;
                    foreach(Vector2 uv in mesh.uv){low=Vector2.Min(low,uv);high=Vector2.Max(high,uv);}
                    Assert.That((high-low).sqrMagnitude,Is.GreaterThan(.01f),name+" must sample an area of the texture.");
                }
            }
        }

        private static void AssertTruckMovingHeadlamps(CityCanneryController cannery,Light[] headlights)
        {
            double saved=cannery.WorkingSeconds;
            try
            {
                cannery.ApplyAt(CanneryTime(cannery,CityFishSupplyStage.PortToFactory,.35f));
                AssertTruckHeadlamps(cannery,headlights);
                var positions=new Vector3[headlights.Length];
                for(int i=0;i<headlights.Length;i++)positions[i]=headlights[i].transform.position;
                cannery.ApplyAt(CanneryTime(cannery,CityFishSupplyStage.PortToFactory,.45f));
                AssertTruckHeadlamps(cannery,headlights);
                for(int i=0;i<headlights.Length;i++)
                    Assert.That(Vector3.Distance(positions[i],headlights[i].transform.position),Is.GreaterThan(1f),
                        "The actual lamp must travel with its truck, not stay at its initial world position.");
            }
            finally{cannery.ApplyAt(saved);}
        }

        private static void AssertTruckHeadlamps(CityCanneryController cannery,Light[] headlights)
        {
            Assert.That(headlights.Length,Is.EqualTo(2));
            for(int i=0;i<2;i++)
            {
                string side=i==0?"Left":"Right";
                Transform anchor=CityCanneryAssetProvider.FindPart(cannery.Truck.gameObject,"ANCHOR_Headlamp"+side);
                Vector3 expected=cannery.Truck.TransformPoint(new Vector3(i==0?-.88f:.88f,1.4f,
                    5.37f+CityCanneryTruckDimensions.CabOffset));
                Assert.That(Vector3.Distance(anchor.position,expected),Is.LessThan(.002f),side+" lamp imported metres");
                Transform lens=CityCanneryAssetProvider.FindPart(
                    CityCanneryAssetProvider.FindPart(cannery.Truck.gameObject,"Headlamp"+side).gameObject,"TruckHeadlampGlass");
                Renderer renderer=lens.GetComponent<Renderer>();
                Assert.That(renderer.sharedMaterial,Is.SameAs(CityNightResources.EmissiveMaterial));
                var properties=new MaterialPropertyBlock();renderer.GetPropertyBlock(properties);
                Assert.That(properties.GetColor("_BaseColor").maxColorComponent,Is.GreaterThan(1f));
                Light light=null;
                foreach(Light candidate in headlights)
                    if(Vector3.Distance(candidate.transform.position,anchor.position+cannery.Truck.forward*.04f)<.002f)light=candidate;
                Assert.That(light,Is.Not.Null,"The light must sit just ahead of its own physical lens.");
                Assert.That(light.transform.IsChildOf(cannery.Truck),Is.True);
                Assert.That(light.enabled&&light.gameObject.activeInHierarchy,Is.True);
                Assert.That(light.type,Is.EqualTo(LightType.Spot));
                Assert.That(light.range,Is.GreaterThanOrEqualTo(22f));
                Assert.That(light.intensity,Is.GreaterThan(0));
                Assert.That(Vector3.Dot(light.transform.forward,cannery.Truck.forward),Is.GreaterThan(.98f));
                Assert.That(Vector3.Dot(light.transform.forward,cannery.Truck.up),Is.LessThan(-.1f));
                Assert.That(cannery.TruckPresentationBounds.Contains(light.transform.position+
                    light.transform.forward*light.range),Is.True,"The presentation bounds include the beam's actual far point.");
                float halfAngle=light.spotAngle*.5f*Mathf.Deg2Rad;
                for(int edge=0;edge<8;edge++)
                {
                    float angle=edge*Mathf.PI*.25f;
                    Vector3 direction=new Vector3(Mathf.Sin(halfAngle)*Mathf.Cos(angle),
                        Mathf.Sin(halfAngle)*Mathf.Sin(angle),Mathf.Cos(halfAngle));
                    Vector3 point=light.transform.position+light.transform.rotation*direction*light.range;
                    Assert.That(cannery.TruckPresentationBounds.Contains(point),Is.True,
                        "The distance gate includes the actual outer headlight cone.");
                }
            }
        }

        private static void AssertTruckFrontCoverage(CityCanneryController cannery)
        {
            Transform front=CityCanneryAssetProvider.FindPart(cannery.Truck.gameObject,"TruckFrontPanel__Steel");
            Mesh mesh=front.GetComponent<MeshFilter>().sharedMesh;
            Vector3 low=Vector3.positiveInfinity,high=Vector3.negativeInfinity;
            foreach(Vector3 vertex in mesh.vertices)
            {
                Vector3 point=cannery.Truck.InverseTransformPoint(front.TransformPoint(vertex));
                low=Vector3.Min(low,point);high=Vector3.Max(high,point);
            }
            Assert.That(Vector3.Distance(low,new Vector3(-1.125f,1.05f,4.94f+CityCanneryTruckDimensions.CabOffset)),Is.LessThan(.003f));
            Assert.That(Vector3.Distance(high,new Vector3(1.125f,1.83f,5.34f+CityCanneryTruckDimensions.CabOffset)),Is.LessThan(.003f));
            // Raycast this rendered mesh directly; the truck's broad collision
            // body cannot prove that the visible nose is actually closed.
            MeshCollider probe=front.gameObject.AddComponent<MeshCollider>();
            try
            {
                probe.sharedMesh=mesh;Physics.SyncTransforms();
                foreach(float x in new[]{-1.02f,-.88f,0f,.88f,1.02f})
                foreach(float y in new[]{1.12f,1.32f,1.58f})
                {
                    var ray=new Ray(cannery.Truck.TransformPoint(new Vector3(x,y,5.42f+CityCanneryTruckDimensions.CabOffset)),-cannery.Truck.forward);
                    Assert.That(probe.Raycast(ray,out RaycastHit hit,.5f),Is.True,$"The rendered front must close ({x},{y}).");
                    Assert.That(cannery.Truck.InverseTransformPoint(hit.point).z,
                        Is.EqualTo(5.34f+CityCanneryTruckDimensions.CabOffset).Within(.005f));
                }
            }
            finally{Object.DestroyImmediate(probe);Physics.SyncTransforms();}
        }

        private static void AssertTruckLampDistance(CityGameRoot city,CityCanneryController cannery,Light[] headlights)
        {
            cannery.ForcePresentation=false;
            city.Player.Motor.Teleport(cannery.Truck.position+new Vector3(1000,100,1000));
            cannery.RefreshPresentation();
            Assert.That(cannery.TruckPresentationActive,Is.False);
            foreach(Light light in headlights)Assert.That(light.gameObject.activeInHierarchy,Is.False);
            city.DayNight.ApplyCurrentTime(true);
            foreach(Light light in headlights)Assert.That(light.gameObject.activeInHierarchy,Is.False,
                "The day/night registry cannot revive a distant truck's lamps.");
            city.Player.Motor.Teleport(cannery.Truck.position+cannery.Truck.right*4f);
            cannery.RefreshPresentation();
            AssertTruckHeadlamps(cannery,headlights);
        }

        private static IEnumerator CaptureTruckAppearance(Camera camera,CityCanneryController cannery,
            string name,Vector3 from,Vector3 target,float fieldOfView)
        {
            SetTruckAppearanceCamera(camera,cannery,from,target,fieldOfView);
            for(int frame=0;frame<3;frame++)yield return null;
            CaptureCurrentCamera(camera,"CityCanneryTruckAppearance",name);
        }

        private static void SetTruckAppearanceCamera(Camera camera,CityCanneryController cannery,
            Vector3 from,Vector3 target,float fieldOfView)
        {
            // These front/detail views retain their original distance to the
            // full-sized cab after its move on the shorter chassis.
            from.z+=CityCanneryTruckDimensions.CabOffset;
            target.z+=CityCanneryTruckDimensions.CabOffset;
            Vector3 position=cannery.Truck.TransformPoint(from),look=cannery.Truck.TransformPoint(target);
            camera.transform.SetPositionAndRotation(position,Quaternion.LookRotation(look-position));
            camera.fieldOfView=fieldOfView;
        }

        private static void AssertTruckRoadLight(Camera camera,CityCanneryController cannery,Light[] headlights)
        {
            Physics.SyncTransforms();
            var pixels=new HashSet<int>();
            for(int z=0;z<6;z++)
            for(int x=0;x<5;x++)
            {
                Vector3 point=cannery.Truck.TransformPoint(new Vector3((x-2)*.6f,0,
                    9f+z+CityCanneryTruckDimensions.CabOffset));
                float nearest=float.PositiveInfinity;Vector3 ground=Vector3.zero;
                foreach(RaycastHit hit in Physics.RaycastAll(point+Vector3.up*8f,Vector3.down,12f,~0,QueryTriggerInteraction.Ignore))
                {
                    if(hit.collider.transform.IsChildOf(cannery.Truck)||IsCanneryDynamicActor(hit.collider)||
                        hit.normal.y<.8f||hit.point.y>cannery.Truck.position.y+.8f||hit.distance>=nearest)continue;
                    ground=hit.point;nearest=hit.distance;
                }
                if(float.IsPositiveInfinity(nearest))continue;
                Vector3 view=camera.WorldToViewportPoint(ground+Vector3.up*.025f);
                if(view.z<=0||view.x<.05f||view.x>.95f||view.y<.05f||view.y>.95f)continue;
                Vector3 delta=ground+Vector3.up*.025f-camera.transform.position;
                if(Physics.Raycast(camera.transform.position,delta.normalized,out RaycastHit obstruction,
                    delta.magnitude-.1f,~0,QueryTriggerInteraction.Ignore))continue;
                int px=Mathf.RoundToInt(view.x*Width),py=Mathf.RoundToInt(view.y*Height);
                for(int dy=-4;dy<=4;dy++)for(int dx=-4;dx<=4;dx++)pixels.Add((py+dy)*Width+px+dx);
            }
            Assert.That(pixels.Count,Is.GreaterThan(300),"The beam reference must contain exposed physical road pixels.");
            var enabled=new bool[headlights.Length];
            for(int i=0;i<headlights.Length;i++)enabled[i]=headlights[i].enabled;
            try
            {
                foreach(Light light in headlights)light.enabled=false;
                Color32[] off=ReadTruckAppearance(camera,"03-night-road-lamps-off");
                Color32[] baseline=ReadTruckAppearance(camera,null);
                foreach(Light light in headlights)light.enabled=true;
                Color32[] on=ReadTruckAppearance(camera,"04-night-road-lamps-on");
                double difference=0,noise=0;
                foreach(int index in pixels)
                {
                    difference+=FrostLuma(on[index])-FrostLuma(off[index]);
                    noise+=Math.Abs(FrostLuma(baseline[index])-FrostLuma(off[index]));
                }
                difference/=pixels.Count;noise/=pixels.Count;
                TestContext.Out.WriteLine($"Truck road illumination: signed light delta {difference:F3}, same-frame baseline {noise:F3} (0–255).");
                Assert.That(difference,Is.GreaterThan(Math.Max(.5d,noise*2d+.1d)),
                    "The actual headlamps must brighten the physical road, independently of lens emission and halos.");
            }
            finally{for(int i=0;i<headlights.Length;i++)headlights[i].enabled=enabled[i];}
        }

        private static Color32[] ReadTruckAppearance(Camera camera,string name)
        {
            var target=new RenderTexture(Width,Height,24);
            var pixels=new Texture2D(Width,Height,TextureFormat.RGB24,false);
            RenderTexture previousTarget=camera.targetTexture,previousActive=RenderTexture.active;
            try
            {
                camera.targetTexture=target;camera.Render();RenderTexture.active=target;
                pixels.ReadPixels(new Rect(0,0,Width,Height),0,0);pixels.Apply();
                Assert.That(IsBlank(pixels),Is.False);
                if(name!=null)
                {
                    string folder=Path.Combine(Directory.GetCurrentDirectory(),"Captures","CityCanneryTruckAppearance");
                    Directory.CreateDirectory(folder);File.WriteAllBytes(Path.Combine(folder,name+".png"),pixels.EncodeToPNG());
                }
                return pixels.GetPixels32();
            }
            finally
            {
                camera.targetTexture=previousTarget;RenderTexture.active=previousActive;
                Object.DestroyImmediate(pixels);target.Release();Object.DestroyImmediate(target);
            }
        }
    }
}

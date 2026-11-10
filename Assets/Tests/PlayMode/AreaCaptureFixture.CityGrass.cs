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
        [UnityTest, Explicit("Dense City habitats, imported plant scale, rooted wind/contact, bounded cache and rendered acceptance.")]
        [Timeout(600000)]
        public IEnumerator CityGrassAcceptance()
        {
            GameSessionState.BeginNewGame("Test City grass");
            Assert.That(GameSessionState.TryStartGameTimeAt(12*60),Is.True);
            CityGameRoot city=null;CityGrassField grass=null;
            var evidence=new List<string>();
            yield return Capture(SceneIds.City,()=>
            {
                city=Object.FindAnyObjectByType<CityGameRoot>();
                return city!=null&&city.IsInitialized?city:null;
            },()=>
            {
                FreezeLightingPolish(city.Player.Motor);
                city.DayNight.enabled=false;city.Weather.enabled=false;
                grass=city.World.Root.GetComponentInChildren<CityGrassField>(true);
                Assert.That(grass,Is.Not.Null);
                Assert.That(grass.SurfaceFaceCount,Is.GreaterThan(100));
                Assert.That(grass.GetComponentsInChildren<Collider>(true),Is.Empty);
                Assert.That(grass.GetComponentsInChildren<Rigidbody>(true),Is.Empty);
                GameObject model=Resources.Load<GameObject>(CityGrassField.ResourcePath);
                Assert.That(model.GetComponentsInChildren<MeshFilter>(true).Length,Is.EqualTo(72));
                Shader shader=Resources.Load<Shader>("Shaders/CityGrassLit");
                Assert.That(shader!=null&&shader.isSupported,Is.True);
#if UNITY_EDITOR
                Assert.That(UnityEditor.ShaderUtil.ShaderHasError(shader),Is.False,
                    "City grass must compile its native lighting/depth/shadow/motion variants.");
#endif
                // The same seed and cell keep roots/species/scale independent of traversal and loading order.
                for(int x=-8;x<8;x++)for(int z=-8;z<8;z++)
                {
                    Vector2 p=CityGrassPlan.Root(city.Layout.Seed,x,z);
                    Assert.That(CityGrassPlan.Root(city.Layout.Seed,x,z),Is.EqualTo(p));
                    bool first=CityGrassPlan.Plant(city.Layout.Seed,x,z,CityGrassHabitat.Park,out var a,out float sa,out int va);
                    Assert.That(CityGrassPlan.Plant(city.Layout.Seed,x,z,CityGrassHabitat.Park,out var b,out float sb,out int vb),Is.EqualTo(first));
                    Assert.That((a,sa,va),Is.EqualTo((b,sb,vb)));
                }
                var shots=new List<Shot>();
                foreach(var bridge in city.Layout.River.Bridges)
                {
                    Rect span=bridge.SpanBounds;
                    for(float z=span.yMin;z<span.yMax;z+=.3f)for(float x=span.xMin;x<span.xMax;x+=.3f)
                        Assert.That(grass.Surface.TrySample(new Vector2(x,z),out _,out _,out _),Is.False,
                            "Every declared bridge span must be plant-free: "+bridge.Definition.Id);
                    Vector3 aim=new Vector3(bridge.DeckBounds.center.x,bridge.AverageY+.25f,bridge.DeckBounds.center.y);
                    Vector3 eye=aim+new Vector3(4f,2f,-4f);
                    shots.Add(Shot.At("Test grass - Clear bridge "+bridge.Definition.Id,eye,aim,62f,
                        readyWhen:()=>WarmGrassShot(grass,city.Camera,eye,aim)));
                }
                foreach(var habitat in new[]{CityGrassHabitat.Park,CityGrassHabitat.Courtyard,CityGrassHabitat.Ridge,
                    CityGrassHabitat.Industrial,CityGrassHabitat.Garden,CityGrassHabitat.Cemetery,CityGrassHabitat.River,
                    CityGrassHabitat.Beach,CityGrassHabitat.Fringe})
                {
                    if(!grass.TryFindPlant(habitat,out Vector3 root,out Vector3 tip))
                    {
                        Assert.That(habitat,Is.Not.EqualTo(CityGrassHabitat.Park).And.Not.EqualTo(CityGrassHabitat.Beach)
                            .And.Not.EqualTo(CityGrassHabitat.Ridge),"Primary habitat has no actual rooted plants: "+grass.Surface.DescribeHabitat(habitat));
                        evidence.Add(habitat+": no eligible exposed ground");continue;
                    }
                    string label="Test grass - "+habitat;
                    Vector3 aim=root+Vector3.up*(habitat==CityGrassHabitat.Beach?.55f:.20f);
                    shots.Add(Shot.At(label,root+new Vector3(2.8f,1.65f,-3.2f),aim,62f,readyWhen:()=>
                    {
                        grass.DebugStill=false;grass.DebugWind=new WindSample(75,.75f);grass.DebugPhaseSeconds=1;
                        grass.DebugContact=root+Vector3.right*4;
                        return WarmGrassShot(grass,city.Camera,root+new Vector3(2.8f,1.65f,-3.2f),aim);
                    }));
                    evidence.Add(habitat+": "+root);
                    if(habitat==CityGrassHabitat.Park)
                    {
                        int accepted=0;
                        for(int z=0;z<32;z++)for(int x=0;x<32;x++)
                            if(CityGrassPlan.Plant(city.Layout.Seed,x,z,habitat,out _,out _,out _))accepted++;
                        Assert.That(accepted,Is.GreaterThan(880),"The main lawn must use the approved dense population.");
                        foreach(string state in new[]{"Still","Wind","Contact"})
                        {
                            string moment=state;
                            shots.Add(Shot.At("Test grass - "+moment,root+new Vector3(.8f,.42f,-.9f),root+Vector3.up*.15f,50f,readyWhen:()=>
                            {
                                grass.DebugStill=moment=="Still";grass.DebugPhaseSeconds=1;
                                grass.DebugContact=root+Vector3.right*(moment=="Contact"?.12f:4f);
                                return WarmGrassShot(grass,city.Camera,root+new Vector3(.8f,.42f,-.9f),root+Vector3.up*.15f);
                            }));
                        }
                    }
                }
                Assert.That(shots.Count,Is.GreaterThanOrEqualTo(8));
                return shots.ToArray();
            });
            Assert.That(PolishDifference(SceneIds.City,"Test grass - Still","Test grass - Wind"),Is.GreaterThan(24));
            Assert.That(PolishDifference(SceneIds.City,"Test grass - Wind","Test grass - Contact"),Is.GreaterThan(24));
            Assert.That(grass.CachedChunkCount,Is.LessThanOrEqualTo(CityGrassField.MaximumCachedChunks));
            Camera camera=city.Camera;
            RenderTexture previousTarget=camera.targetTexture;
            var verificationTarget=new RenderTexture(Width,Height,24);
            camera.targetTexture=verificationTarget;
            try
            {
            Assert.That(grass.TryFindPlant(CityGrassHabitat.Park,out Vector3 checkRoot,out _),Is.True);
            WarmGrassShot(grass,camera,checkRoot+new Vector3(2.8f,1.65f,-3.2f),checkRoot+Vector3.up*.2f);
            for(int frame=0;frame<12;frame++){camera.Render();yield return null;}
            camera.Render();
            Assert.That(grass.LastDrawnTufts,Is.GreaterThan(100));
            evidence.Add($"drawn tufts={grass.LastDrawnTufts}; triangles={grass.LastDrawnTriangles}; calls={grass.LastDrawCalls}; preparation ms={grass.LastPreparationMilliseconds:F3}; cached chunks={grass.CachedChunkCount}");
            // Same-frame renders retain the last engine frame's wind history.
            float previous=grass.PreviousRenderedPhaseSeconds;
            grass.DebugPhaseSeconds=2;camera.Render();
            Assert.That(grass.PreviousRenderedPhaseSeconds,Is.EqualTo(previous));
            grass.enabled=false;
            Assert.That(grass.CachedChunkCount,Is.Zero);
            grass.enabled=true;camera.Render();
            Assert.That(grass.PreviousRenderedPhaseSeconds,Is.EqualTo(grass.AppliedPhaseSeconds));
            grass.DebugPhaseSeconds=null;grass.DebugContact=null;
            using(GameTimeScaleRuntime.AcquirePause())
            {
                camera.Render();float frozen=grass.AppliedPhaseSeconds;
                yield return null;yield return null;camera.Render();
                Assert.That(grass.AppliedPhaseSeconds,Is.EqualTo(frozen),"Pause freezes bend phase and recovery.");
            }
            File.WriteAllLines(Path.Combine("Captures",SceneIds.City,"CityGrassAcceptance.txt"),evidence);
            grass.DebugContact=null;grass.DebugWind=null;grass.DebugPhaseSeconds=null;grass.DebugStill=false;
            if(grass.TryFindPlant(CityGrassHabitat.Cemetery,out Vector3 cemeteryRoot,out _))
            {
                var mouth=new Rect(cemeteryRoot.x-.3f,cemeteryRoot.z-.3f,.6f,.6f);
                Assert.That(city.World.CemeteryGroundExcavation.Excavate(mouth),Is.True);
                Assert.That(grass.Surface.TrySample(new Vector2(cemeteryRoot.x,cemeteryRoot.z),out _,out _,out _),Is.False,
                    "An open excavation cannot retain blades over empty air.");
                Assert.That(city.World.CemeteryGroundExcavation.Fill(mouth),Is.True);
                Assert.That(grass.Surface.TrySample(new Vector2(cemeteryRoot.x,cemeteryRoot.z),out _,out _,out _),Is.True);
            }
            }
            finally
            {
                camera.targetTexture=previousTarget;
                verificationTarget.Release();Object.Destroy(verificationTarget);
            }
            yield return SceneManager.LoadSceneAsync(SceneIds.MainMenu,LoadSceneMode.Single);
            yield return null;
            Assert.That(grass==null,Is.True,"Scene unload releases the owner and its material/cache.");

            GameSessionState.PrepareHomeArrival(HomeArrivalKind.Normal);
            GameSessionState.EnterHome();
            HomeInteriorRoot home=null;
            yield return Capture(SceneIds.HomeInterior,()=>
            {
                home=Object.FindAnyObjectByType<HomeInteriorRoot>();
                return home!=null&&home.IsInitialized?home:null;
            },()=>
            {
                FreezeLightingPolish(home.Player.Motor);
                var field=Object.FindAnyObjectByType<CityGrassField>();
                Assert.That(field,Is.Not.Null);
                Assert.That(field.Surface.Home,Is.Not.Null);
                bool found=field.TryFindPlant(CityGrassHabitat.Ridge,out Vector3 root,out _) ||
                    field.TryFindPlant(CityGrassHabitat.Courtyard,out root,out _);
                Assert.That(found,Is.True,"Home retains the real City's seeded planted ground. "+
                    field.Surface.DescribeHabitat(CityGrassHabitat.Ridge)+" / "+field.Surface.DescribeHabitat(CityGrassHabitat.Courtyard)+
                    " / "+field.Surface.DescribeHabitat(CityGrassHabitat.Fringe));
                Assert.That(Vector3.Distance(field.Surface.FromCity(field.Surface.ToCity(root)),root),Is.LessThan(.002f));
                Vector3 eye=root+new Vector3(2f,1.7f,-3f),aim=root+Vector3.up*.2f;
                return new[]{Shot.At("Test grass - Home exterior",eye,aim,62f,
                    readyWhen:()=>WarmGrassShot(field,Camera.main,eye,aim))};
            });
        }

        private static bool WarmGrassShot(CityGrassField grass,Camera camera,Vector3 eye,Vector3 aim)
        {
            camera.transform.SetPositionAndRotation(eye,Quaternion.LookRotation((aim-eye).normalized,Vector3.up));
            grass.WarmView(camera);
            return true;
        }
    }
}

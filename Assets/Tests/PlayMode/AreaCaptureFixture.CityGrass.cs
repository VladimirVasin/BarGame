using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
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

        [UnityTest, Explicit("Ridge ground, rooted grass contact and shared crown/conductor gusts in City and Home.")]
        public IEnumerator CityRidgeWind()
        {
            GameSessionState.BeginNewGame("Ridge wind");
            Assert.That(GameSessionState.TryStartGameTimeAt(720), Is.True);
            CityGameRoot city = null;
            CityWindField wind = null;
            Vector3 westRoot = default;
            yield return Capture(SceneIds.City, () =>
            {
                city = Object.FindAnyObjectByType<CityGameRoot>();
                return city != null && city.IsInitialized ? city : null;
            }, () =>
            {
                FreezeLightingPolish(city.Player.Motor);
                city.Weather.enabled = false;
                var root = city.World.Root.transform;
                wind = root.GetComponent<CityWindField>();
                var grass = root.GetComponentInChildren<CityGrassField>();
                Assert.That(wind, Is.Not.Null);
                Assert.That(wind.Wind.z, Is.EqualTo(city.Weather.CurrentWind.Strength01).Within(.001f));
                AssertKioskApproachesClear(grass, city.Layout);
                var vegetation = CityRidgeVegetationPlan.ForLayout(city.Layout);
                var added = root.Find(CityRidgeVegetationPlan.RootName);
                Assert.That(added, Is.Not.Null);
                Assert.That(added.childCount, Is.EqualTo(vegetation.Plants.Count));
                var map = new CityMapCityTeleportGround(city.Layout);
                Physics.SyncTransforms();
                for (int i = 0; i < vegetation.Plants.Count; i++)
                {
                    var plant = vegetation.Plants[i];
                    var instance = added.GetChild(i);
                    Assert.That(Vector3.Distance(instance.position, plant.Origin), Is.LessThan(.005f));
                    Assert.That(instance.GetComponentsInChildren<MeshRenderer>().All(renderer =>
                        renderer.sharedMaterial.GetFloat("_CityWindGeometry") ==
                        (float)(plant.IsTree ? CityWindGeometry.Tree : CityWindGeometry.Shrub)), Is.True);
                    if (!plant.IsTree) continue;
                    Assert.That(instance.GetComponent<BoxCollider>(), Is.Not.Null);
                    Assert.That(city.World.WalkableArea.Contains(plant.Origin, .3f), Is.False, "The walk mask skips the trunk.");
                }
                foreach (var trail in city.Layout.CourtyardPaths)
                    foreach (var strip in trail.Ribbon(1.5f))
                        Assert.That(grass.Surface.TrySample(strip.Aggregate(Vector2.zero, (sum, p) => sum + p) /
                            strip.Length, out _, out _, out _), Is.False, "An unpaved courtyard trail must also stay clear.");
                foreach (var patch in CityStreetSupportPlan.ForLayout(city.Layout).Patches)
                    if (patch.IsSupport && patch.Kind == CityStreetSupportKind.ParkPath)
                    {
                        Vector3 middle = patch.TopVertices.Aggregate(Vector3.zero, (sum, p) => sum + p) / patch.TopVertices.Count;
                        Assert.That(grass.Surface.TrySample(new Vector2(middle.x, middle.z), out _, out _, out _), Is.False,
                            "A park path must stay clear.");
                    }
                var shots = new List<Shot>();
                foreach (var route in CityHillsideStreetPlan.ForLayout(city.Layout).Routes)
                {
                    Vector3 bank = route.SummitArrivalWorld;
                    var plants = vegetation.Plants.Where(item => item.Bank == route.Bank).ToArray();
                    Debug.Log("Ridge " + route.Bank + " planting: " + plants.Count(item => item.IsTree) +
                        " trees and " + plants.Count(item => !item.IsTree) + " shrubs.");
                    Assert.That(plants.Count(item => item.IsTree), Is.GreaterThanOrEqualTo(4));
                    Assert.That(plants.Count(item => !item.IsTree), Is.GreaterThanOrEqualTo(5));
                    var group = plants.Where(item => item.IsTree).OrderByDescending(item =>
                        item.Origin.y + 5f * plants.Count(member => !member.IsTree &&
                            Vector3.Distance(member.Origin, item.Origin) < 4.5f)).First();
                    if (map.TryResolveStandingPosition(new Vector2(group.Origin.x, group.Origin.z), out var arrival))
                        Assert.That(Vector2.Distance(new Vector2(arrival.x, arrival.z),
                            new Vector2(group.Origin.x, group.Origin.z)), Is.GreaterThan(.34f * group.Scale + .25f));
                    Vector3 plant = FindRidgeGrass(grass, city.Layout.Seed, bank);
                    if (route.Bank == CityTerraceTransportBank.West) westRoot = plant;
                    Assert.That(grass.Surface.TrySample(new Vector2(bank.x, bank.z), out _, out _, out _), Is.False,
                        "The summit public footway stays clear.");
                    city.Player.Motor.Teleport(plant + Vector3.up * PlayerFactory.GroundedRootOffset);
                    Vector3 aim = plant + Vector3.up * .22f;
                    Vector3 eye = FrameRidgeGrass(aim, 3.5f, 1.45f);
                    shots.Add(Shot.At("ridge-wind-grass-" + route.Bank, eye, aim, 62f, readyWhen: () =>
                    {
                        grass.DebugWind = new WindSample(75, .50f);
                        grass.DebugPhaseSeconds = 2f;
                        grass.DebugContact = plant + Vector3.right * 3f;
                        return WarmGrassShot(grass, city.Camera, eye, aim);
                    }));
                    Vector3 groupAim = group.Origin + Vector3.up * 2.55f;
                    Vector3 groupEye = FrameRidgeGrass(groupAim, 8f, -.75f);
                    shots.Add(Shot.At("ridge-wind-planting-" + route.Bank, groupEye, groupAim, 65f,
                        readyWhen: () => WarmGrassShot(grass, city.Camera, groupEye, groupAim)));
                }
                var westAim = westRoot + Vector3.up * .20f;
                var westEye = FrameRidgeGrass(westAim, 1.15f, .32f);
                foreach (bool contact in new[] { false, true })
                    shots.Add(Shot.At(contact ? "ridge-wind-grass-pressed" : "ridge-wind-grass-free", westEye, westAim, 53f,
                        readyWhen: () =>
                        {
                            grass.DebugContact = westRoot + Vector3.right * (contact ? .10f : 3f);
                            return WarmGrassShot(grass, city.Camera, westEye, westAim);
                        }));
                Transform tree = root.Find("Residential terrace edges").Cast<Transform>()
                    .Where(site => site.name == "RidgeYardTree").OrderByDescending(site => site.position.y).First();
                var crown = tree.GetComponentsInChildren<MeshRenderer>().OrderByDescending(part => part.bounds.center.y).First();
                Assert.That(crown.sharedMaterial.GetFloat("_CityWindGeometry"), Is.EqualTo(1f));
                Assert.That(tree.GetComponent<BoxCollider>(), Is.Not.Null);
                Vector3 treeAim = crown.bounds.center - Vector3.up * .4f;
                Vector3 treeEye = tree.position + new Vector3(4.4f, 2.2f, -5.2f);
                var treeWind = new WindSample(90, .50f);
                Vector4 profile = Shader.GetGlobalVector("_CityWindProfile");
                AddWindPair("tree", treeEye, treeAim, treeWind);
                var shrub = vegetation.Plants.Where(item => !item.IsTree).OrderByDescending(item => item.Origin.y).First();
                Vector3 shrubAim = shrub.Origin + Vector3.up * .62f;
                AddWindPair("shrub", FrameRidgeGrass(shrubAim, 3.1f, .40f), shrubAim, new WindSample(75, .68f));
                shots.Add(Shot.At("ridge-wind-tree-lowland", treeEye, treeAim, 62f, readyWhen: () =>
                {
                    Shader.SetGlobalVector("_CityWind", wind.Pack(treeWind, 1.4f));
                    Shader.SetGlobalVector("_CityWindProfile", new Vector4(profile.x, profile.y, 0f, profile.w));
                    return true;
                }));

                var network = CityCoastalInfrastructurePlan.ForLayout(city.Layout);
                var wire = ElectricalMesh(root.Find(CityCoastalInfrastructureWorldBuilder.RootName));
                Shader shader = wire.GetComponent<Renderer>().sharedMaterial.shader;
                Assert.That(shader.name, Is.EqualTo(CitySurfaceLibrary.FoliageShaderName));
#if UNITY_EDITOR
                Assert.That(UnityEditor.ShaderUtil.ShaderHasError(shader), Is.False);
#endif
                var data = new List<Vector4>();
                wire.sharedMesh.GetUVs(3, data);
                Vector3[] wireVertices = wire.sharedMesh.vertices;
                int stride = wire.sharedMesh.vertexCount / network.CableSpans.Count;
                Assert.That(data.Count, Is.EqualTo(wire.sharedMesh.vertexCount));
                var span = network.CableSpans.Where(item => !item.IsServiceDrop && item.Start.y > 38f && item.End.y > 38f)
                    .OrderByDescending(item => item.WindSwing).First();
                Assert.That(span.WindSwing, Is.GreaterThan(.10f));
                var geometry = new CityElectricalClearanceGeometry(city.Layout);
                for (int i = 0; i < network.CableSpans.Count; i++)
                {
                    var line = network.CableSpans[i];
                    Assert.That(geometry.Clear(line, line.WindSwing) || line.WindSwing == 0f, Is.True);
                    Vector3 axis = line.End - line.Start;
                    axis.y = 0f;
                    float lengthSquared = axis.sqrMagnitude;
                    int ends = 0;
                    for (int j = i * stride; j < (i + 1) * stride; j++)
                    {
                        float t = Vector3.Dot(wire.transform.TransformPoint(wireVertices[j]) - line.Start, axis) /
                            Mathf.Max(lengthSquared, .000001f);
                        if (t > .0001f && t < .9999f) continue;
                        Assert.That(new Vector2(data[j].x, data[j].y).sqrMagnitude, Is.LessThan(1e-9f));
                        ends |= t < .5f ? 1 : 2;
                    }
                    Assert.That(ends, Is.EqualTo(3), "The cable model includes both pinned rings.");
                }
                Vector3 mid = span.Sample(.5f);
                float crosswind = Mathf.Atan2(span.Lateral.x, span.Lateral.z) * Mathf.Rad2Deg;
                AddWindPair("wire", mid - Vector3.up * 4.6f + (span.End - span.Start).normalized * 3f,
                    mid, new WindSample(crosswind, .80f));
                shots.Add(RidgeKioskApronShot(city.Layout, grass, city.Camera));
                return shots.ToArray();

                void AddWindPair(string subject, Vector3 eye, Vector3 aim, WindSample sample)
                {
                    foreach (bool gust in new[] { false, true })
                        shots.Add(Shot.At("ridge-wind-" + subject + (gust ? "-gust" : "-still"), eye, aim, 62f,
                            readyWhen: () =>
                            {
                                Shader.SetGlobalVector("_CityWindProfile", profile);
                                Shader.SetGlobalVector("_CityWind", gust ? wind.Pack(sample, 1.4f) : Vector4.zero);
                                return true;
                            }));
                }
            });
            foreach (string subject in new[] { "tree", "shrub", "wire" })
                Assert.That(PolishDifference(SceneIds.City, "ridge-wind-" + subject + "-still",
                    "ridge-wind-" + subject + "-gust"), Is.GreaterThan(24), subject);
            Assert.That(PolishDifference(SceneIds.City, "ridge-wind-grass-free", "ridge-wind-grass-pressed"),
                Is.GreaterThan(24), "A body depresses the rooted leaves.");
            using (GameTimeScaleRuntime.AcquirePause())
            {
                Vector4 paused = wind.Wind;
                yield return null;
                yield return null;
                Assert.That(wind.Wind, Is.EqualTo(paused));
            }

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
                var field = home.ExteriorView.GetComponentInChildren<CityGrassField>();
                Assert.That(home.ExteriorView.GetComponent<CityWindField>(), Is.Not.Null);
                AssertKioskApproachesClear(field, home.ExteriorContext.Layout);
                Assert.That(field.Surface.TrySample(new Vector2(westRoot.x, westRoot.z), out var retained, out _, out _), Is.True);
                Assert.That(retained.y, Is.EqualTo(westRoot.y).Within(.015f));
                var vegetation = CityRidgeVegetationPlan.ForLayout(home.ExteriorContext.Layout);
                var replay = home.ExteriorView.Find("Summit street from home/" + CityRidgeVegetationPlan.RootName);
                Assert.That(replay, Is.Not.Null);
                Assert.That(replay.GetComponentsInChildren<Collider>(true), Is.Empty);
                Assert.That(replay.Cast<Transform>().All(frame => vegetation.Plants.Any(plant =>
                    Vector3.Distance(frame.localPosition, plant.Origin) < .005f)), Is.True);
                Assert.That(replay.Cast<Transform>().Any(frame => frame.name == "Yard shrub"), Is.True);
                return new[] { RidgeKioskApronShot(home.ExteriorContext.Layout, field, Camera.main) };
            });
            Assert.That(wind == null, Is.True, "The City wind owner releases on scene travel.");
        }

        private static Vector3 FindRidgeGrass(CityGrassField field, int seed, Vector3 summit)
        {
            int x = Mathf.FloorToInt(summit.x / CityGrassPlan.Spacing);
            int z = Mathf.FloorToInt(summit.z / CityGrassPlan.Spacing);
            Vector3 closest = default;
            float best = float.PositiveInfinity;
            int plants = 0;
            for (int row = z - 35; row <= z + 35; row++)
            for (int column = x - 42; column <= x + 42; column++)
            {
                Vector2 point = CityGrassPlan.Root(seed, column, row);
                if (!field.Surface.TrySample(point, out var root, out _, out var habitat) ||
                    habitat != CityGrassHabitat.Ridge || root.y < 36f ||
                    !CityGrassPlan.Plant(seed, column, row, habitat, out _, out _, out _)) continue;
                plants++;
                float distance = (root - summit).sqrMagnitude;
                if (distance >= best) continue;
                if (!Physics.Raycast(root + Vector3.up * .3f, Vector3.down, out var hit, .5f,
                    Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore) || Mathf.Abs(hit.point.y - root.y) > .035f) continue;
                best = distance;
                closest = root;
            }
            Assert.That(plants, Is.GreaterThan(100), "There is rooted grass beyond the summit pavement on each bank.");
            Assert.That(best, Is.LessThan(float.PositiveInfinity), "A blade root sits on the live terrain collider.");
            return closest;
        }

        private static void AssertKioskApproachesClear(CityGrassField grass, CityLayout layout)
        {
            foreach (var site in CityKioskSites.ForLayout(layout).Sites)
            for (float z = site.Apron.yMin + .10f; z < site.Apron.yMax; z += .20f)
            for (float x = site.Apron.xMin + .10f; x < site.Apron.xMax; x += .20f)
            {
                var point = new Vector2(x, z);
                if (site.TrySampleApronTop(point, out _))
                    Assert.That(grass.Surface.TrySample(point, out _, out _, out _), Is.False,
                        "Grass must stay off the concrete kiosk approach in City and Home.");
            }
        }

        private static Shot RidgeKioskApronShot(CityLayout layout, CityGrassField grass, Camera camera)
        {
            Assert.That(RidgeKioskSitePlan.TryCreate(layout, out var kiosk), Is.True);
            Vector3 eye = grass.Surface.FromCity(kiosk.Origin + new Vector3(3.65f, 1.8f, 5.3f));
            Vector3 aim = grass.Surface.FromCity(kiosk.Approach + Vector3.up * .23f);
            return Shot.At("ridge-wind-kiosk-clear", eye, aim, 61f,
                readyWhen: () => WarmGrassShot(grass, camera, eye, aim));
        }

        private static Vector3 FrameRidgeGrass(Vector3 aim, float distance, float elevation)
        {
            for (int i = 0; i < 8; i++)
            {
                Vector3 eye = aim + Quaternion.Euler(0f, i * 45f, 0f) * Vector3.back * distance + Vector3.up * elevation;
                if (!Physics.CheckSphere(eye, .13f, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore) &&
                    !Physics.Linecast(eye, aim, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore))
                    return eye;
            }
            return aim + new Vector3(0f, elevation + 1f, -distance);
        }
    }
}

using System;
using System.Collections;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

namespace BarPromenade.Tests.PlayMode
{
    public sealed partial class AreaCaptureFixture
    {
        [UnityTest]
        [Explicit("Working fishing port: imported metres, complete finite visit, physical transfers, lifecycle and day/night frames.")]
        public IEnumerator CityPort()
        {
            Type setup = Type.GetType("BarPromenade.Editor.CityPortAssetSetup, BarPromenade.Editor");
            Assert.That(setup, Is.Not.Null);
            setup.GetMethod("ValidateOrThrow").Invoke(null, null);
            ValidatePortTimeline();
            GameSessionState.BeginNewGame();
            GameSessionState.TryStartGameTimeFromWake();
            GameSessionState.AdvanceGameTime(360f);
            CityGameRoot city = null;
            CityPortController port = null;
            CityPortCrew crew = null;
            yield return Capture(SceneIds.City, () =>
            {
                city = Object.FindAnyObjectByType<CityGameRoot>();
                return city != null && city.IsInitialized ? city : null;
            }, () =>
            {
                port = Object.FindAnyObjectByType<CityPortController>();
                crew = Object.FindAnyObjectByType<CityPortCrew>();
                Assert.That(port, Is.Not.Null);
                Assert.That(crew, Is.Not.Null);
                if (city.Cannery != null)
                {
                    city.Cannery.AutoAdvance = false;
                    city.Cannery.ForcePresentation = true;
                }
                port.AutoAdvance = false;
                port.ForcePresentation = true;
                port.ApplyAt(CityPortCycle.UnloadStartSeconds, 15f);
                crew.ApplyAt(port.ElapsedSeconds);
                city.Player.Motor.SetInputEnabled(false);
                return new[] { Shot.At("port-00-berth-public-east",
                    port.Plan.World(new Vector3(18.5f, CityPortPlan.DeckHeight + EyeHeight, -5f)),
                    port.Plan.World(new Vector3(0f, 3f, 3.2f)), 68f) };
            });

            Camera camera = Camera.main;
            foreach (Renderer renderer in city.Player.GameObject.GetComponentsInChildren<Renderer>())
                renderer.enabled = false;
            city.Player.Motor.SetInputEnabled(false);
            CityPortPlan plan = port.Plan;
            Assert.That(city.World.SeacoastPlan.Port, Is.Not.Null);
            plan.ValidateOrThrow();
            Assert.That(crew.WorkerCount, Is.EqualTo(5));
            // Static paving must be measured below the delivery truck that
            // shares this yard. Restore its collision before physical cargo work.
            var deliveryColliders = new List<Collider>();
            if(city.Cannery != null)
                foreach(Collider collider in city.Cannery.Truck.GetComponentsInChildren<Collider>(true))
                    if(collider.enabled) { deliveryColliders.Add(collider); collider.enabled=false; }
            try
            {
                Physics.SyncTransforms();
                ValidatePortMeshesAndGround(port, city);
                ValidatePortAccess(port, city);
            }
            finally
            {
                foreach(Collider collider in deliveryColliders)
                    if(collider != null) collider.enabled=true;
                Physics.SyncTransforms();
            }
            ValidatePortCargoCycle(port);

            // A cold reconstruction from the same timestamp must not depend on
            // the previous frame's parenting, cargo counters or missed events.
            int lastCargo = CityPortCycle.CargoCount - 1;
            double seek = CityPortCycle.UnloadStartSeconds + lastCargo * CityPortCycle.CargoDurationSeconds + 31d;
            port.ApplyAt(seek, 15f);
            Vector3 savedCargo = port.Cargo[lastCargo].position;
            Vector3 savedCart = port.Trolley.position;
            Quaternion savedCartRotation = port.Trolley.rotation;
            int savedStored = port.Snapshot.StoredCargo;
            port.ApplyAt(30 * CityPortCycle.CycleDurationSeconds + 5d, 500f);
            port.ApplyAt(seek, 15f);
            Assert.That(port.Cargo[lastCargo].position, Is.EqualTo(savedCargo));
            Assert.That(port.Trolley.position, Is.EqualTo(savedCart));
            Assert.That(port.Snapshot.StoredCargo, Is.EqualTo(savedStored));
            var reconstructedHost = new GameObject("Port Reconstruction Probe");
            reconstructedHost.SetActive(false);
            try
            {
                CityPortController reconstructed = CityPortController.Build(reconstructedHost.transform, plan);
                reconstructed.AutoAdvance = false;
                reconstructed.ForcePresentation = true;
                reconstructed.ApplyAt(seek, 15f);
                Assert.That(reconstructed.Cargo[lastCargo].position, Is.EqualTo(savedCargo));
                Assert.That(reconstructed.Trolley.position, Is.EqualTo(savedCart));
                Assert.That(Quaternion.Angle(reconstructed.Trolley.rotation, savedCartRotation), Is.LessThan(.001f));
                Assert.That(reconstructed.Snapshot.StoredCargo, Is.EqualTo(savedStored));
            }
            finally { Object.DestroyImmediate(reconstructedHost); }

            // The real clock and controller stay fixed while pause owns time.
            port.AutoAdvance = true;
            yield return null;
            using (GameTimeScaleRuntime.AcquirePause())
            {
                yield return null;
                double stopped = port.ElapsedSeconds;
                Vector3 stoppedVessel = port.Vessel.position;
                Vector3 stoppedCart = port.Trolley.position;
                yield return null;
                yield return null;
                Assert.That(port.ElapsedSeconds, Is.EqualTo(stopped));
                Assert.That(port.Vessel.position, Is.EqualTo(stoppedVessel));
                Assert.That(port.Trolley.position, Is.EqualTo(stoppedCart));
            }
            port.AutoAdvance = false;
            port.ApplyAt(seek, 15f);
            city.Player.Motor.Teleport(plan.World(new Vector3(100f, 3f, -100f)));
            camera.transform.position = plan.World(new Vector3(-100f, 20f, -100f));
            yield return null;
            Assert.That(port.Vessel.gameObject.activeInHierarchy, Is.True,
                "Forced capture presentation retains the vessel while the observer leaves.");
            Assert.That(port.Trolley.position, Is.EqualTo(savedCart));
            Assert.That(port.Snapshot.StoredCargo, Is.EqualTo(savedStored));

            CityPortSound sound = Object.FindAnyObjectByType<CityPortSound>();
            Assert.That(sound, Is.Not.Null);
            Assert.That(sound.IsInitialized, Is.True);
            double landing = CityPortCycle.UnloadStartSeconds + CityPortCycle.LandedAtSeconds;
            port.ApplyAt(landing - .02d, 15f);
            sound.Advance(landing - .02d, .02f, true);
            int contacts = sound.ContactsPlayed;
            port.ApplyAt(landing + .02d, 15f);
            sound.Advance(landing + .02d, .04f, true);
            Assert.That(sound.ContactsPlayed, Is.EqualTo(contacts + 1));
            sound.Advance(landing + .02d, .04f, true);
            sound.Advance(landing + 100 * CityPortCycle.CycleDurationSeconds, .02f, true);
            Assert.That(sound.ContactsPlayed, Is.EqualTo(contacts + 1), "Repeated samples and time leaps do not replay impacts.");
            Assert.That(sound.EngineSource.transform.position,
                Is.EqualTo(CityPortAssetProvider.FindPart(port.Vessel.gameObject, "ANCHOR_Engine").position));
            foreach (AudioSource source in sound.GetComponentsInChildren<AudioSource>(true))
            {
                Assert.That(source.spatialBlend, Is.EqualTo(1f));
                Assert.That(source.dopplerLevel, Is.Zero);
                Assert.That(source.outputAudioMixerGroup, Is.Not.Null);
            }

            yield return CapturePort(camera, city, port, crew, 36d, "port-01-approach",
                new Vector3(18.5f, 3.22f, -5f), new Vector3(23f, 3f, 14f));
            yield return CapturePort(camera, city, port, crew, 84d, "port-02-mooring",
                new Vector3(-19.5f, 3.22f, -2f), new Vector3(-7f, 2f, 1f));
            double unload = CityPortCycle.UnloadStartSeconds;
            yield return CapturePort(camera, city, port, crew, unload + 5.8d, "port-03-hook-in-open-hold",
                new Vector3(-5f, 8f, -3f), new Vector3(-4f, .3f, 1.85f));
            yield return CapturePort(camera, city, port, crew, unload + 10d, "port-04-hoist",
                new Vector3(-19.5f, 3.22f, -5f), new Vector3(-4f, 5f, 1.85f));
            yield return CapturePort(camera, city, port, crew, unload + 16d, "port-05-slew",
                new Vector3(-19.5f, 3.22f, -8f), new Vector3(-9f, 6f, -2f));
            yield return CapturePort(camera, city, port, crew, unload + 25.5d, "port-06-trolley-landing",
                new Vector3(-19.5f, 3.22f, -10f), new Vector3(-4f, 2.5f, -6f));
            yield return CapturePort(camera, city, port, crew, unload + 31d, "port-07-warehouse-transfer",
                new Vector3(8f, 3.22f, -11f), new Vector3(0f, 2.5f, -12f));
            yield return CapturePort(camera, city, port, crew, unload + CityPortCycle.StoredAtSeconds - .02d, "port-08-hidden-store-handoff",
                new Vector3(0f, 3.22f, -9f), new Vector3(0f, 2.5f, -16f));
            double depart = unload + CityPortCycle.UnloadDurationSeconds +
                CityPortCycle.SecureDurationSeconds + CityPortCycle.UnmoorDurationSeconds;
            yield return CapturePort(camera, city, port, crew, depart + 30d, "port-09-departure",
                new Vector3(18.5f, 3.22f, -5f), new Vector3(24f, 3f, 12f));
            yield return CapturePort(camera, city, port, crew, unload + 16d, "port-10-unpaved-backshore",
                new Vector3(22f, 4.22f, -37f), new Vector3(3f, 3.2f, -37f));
            yield return CapturePort(camera, city, port, crew, unload + 16d, "port-11-west-ramp",
                new Vector3(-29f, 2.5f, -20.5f), new Vector3(-23f, 2f, -15f));
            yield return CapturePort(camera, city, port, crew, unload + 16d, "port-12-breakwater-public",
                new Vector3(-23f, 3.22f, 24f), new Vector3(-4f, 5f, 1f));
            yield return CapturePort(camera, city, port, crew, unload + 16d, "port-15-street-service-junction",
                new Vector3(48f, 4.5f, -46f), new Vector3(44f, 2f, -30f));
            yield return CapturePort(camera, city, port, crew, unload + 16d, "port-16-truck-yard-loading-bay",
                new Vector3(25f, 3.22f, -18f), new Vector3(5f, 2.7f, -17f));
            yield return CapturePort(camera, city, port, crew, unload + 16d, "port-17-shared-access-road",
                new Vector3(42f, 4.2f, -36.5f), new Vector3(49.5f, 2.5f, -27.5f));
            yield return CapturePort(camera, city, port, crew, unload + 25.5d, "port-18-crane-operator-contact",
                new Vector3(-.4f, 3.22f, -5.1f), new Vector3(-2.1f, 2.9f, -2.7f));

            GameSessionState.AdvanceGameTime((float)(21d * 60d - GameSessionState.GameTimeOfDayMinutes));
            city.DayNight.ApplyCurrentTime(true);
            yield return null;
            yield return CapturePort(camera, city, port, crew, unload + 16d, "port-13-night-working-quay",
                new Vector3(-19.5f, 3.22f, -9f), new Vector3(-4f, 5f, -1f));
            yield return CapturePort(camera, city, port, crew, unload + 31d, "port-14-night-cold-store",
                new Vector3(8f, 3.22f, -11f), new Vector3(0f, 3f, -12f));
            Assert.That(camera.farClipPlane, Is.EqualTo(48f));
            Assert.That(crew.CaptainHandsMatch, Is.True);
            Assert.That(crew.CraneHandsMatch, Is.True);
            Assert.That(crew.TrolleyHandsMatch, Is.True);
            port.gameObject.SetActive(false);
            foreach (AudioSource source in sound.GetComponentsInChildren<AudioSource>(true))
                Assert.That(source.isPlaying, Is.False, "Port disable releases its physical voices.");
            Assert.That(crew.gameObject.activeInHierarchy, Is.False);
            Debug.Log("CITY PORT ACCEPTANCE OK: imported metres, shared graded access and forward truck turns, unpaved former footpath, berth and departure clearance, finite cage custody, cold-store concealment, session reconstruction, pause, independent presence, spatial audio and day/night public frames.");
        }

        private static void ValidatePortTimeline()
        {
            Assert.That(CityPortCycle.CargoCount, Is.EqualTo(3));
            Assert.That(CityPortCycle.CycleDurationSeconds, Is.EqualTo(432d));
            double[] durations = { CityPortCycle.ApproachDurationSeconds, CityPortCycle.MoorDurationSeconds,
                CityPortCycle.PrepareDurationSeconds, CityPortCycle.UnloadDurationSeconds,
                CityPortCycle.SecureDurationSeconds, CityPortCycle.UnmoorDurationSeconds,
                CityPortCycle.DepartDurationSeconds, CityPortCycle.IdleDurationSeconds };
            double boundary = 0d;
            for (int stage = 0; stage < durations.Length; stage++)
            {
                CityPortCycleSnapshot now = CityPortCycle.Sample(boundary);
                Assert.That(now.Stage, Is.EqualTo((CityPortCycleStage)stage));
                Assert.That(now.StageProgress, Is.Zero);
                if (stage > 0) Assert.That(CityPortCycle.Sample(boundary - .001d).Stage,
                    Is.EqualTo((CityPortCycleStage)(stage - 1)));
                boundary += durations[stage];
            }
            Assert.That(boundary, Is.EqualTo(CityPortCycle.CycleDurationSeconds));
            Assert.That(CityPortCycle.Sample(boundary).CycleIndex, Is.EqualTo(1));
            Assert.That(CityPortCycle.Sample(boundary).StoredCargo, Is.Zero);
            Assert.Throws<ArgumentOutOfRangeException>(() => CityPortCycle.Sample(double.NaN));
            Assert.Throws<ArgumentOutOfRangeException>(() => CityPortCycle.Sample(double.PositiveInfinity));
            Assert.Throws<ArgumentOutOfRangeException>(() => CityPortCycle.Sample(-1d));
        }

        private static void ValidatePortMeshesAndGround(CityPortController port, CityGameRoot city)
        {
            Bounds vessel = PortLocalMeshBounds(port.Vessel);
            Assert.That(vessel.size.z, Is.InRange(20f, 21.5f));
            Assert.That(vessel.size.x, Is.InRange(5.3f, 5.8f));
            Assert.That(vessel.min.y, Is.EqualTo(-CityPortPlan.VesselDraft).Within(.025f));
            Assert.That(port.Dock.GetComponentsInChildren<MeshCollider>(true).Length, Is.GreaterThanOrEqualTo(5));
            foreach (CityOpenAreaAccessDescriptor access in city.Layout.OpenAreaAccesses)
                if (access.Feature == CityAreaFeatureKind.NorthWaterfront)
                    Debug.Log("PORT COAST ACCESS local=" + (access.Center - port.Plan.Origin) +
                        " outward=" + access.OutwardNormal);
            foreach (Vector3 local in new[] { new Vector3(0f, 1.5f, -20.5f),
                new Vector3(-30f, .32f, -20.5f), new Vector3(21f, 1.5f, -18f) })
            {
                Vector3 point = port.Plan.World(local);
                bool found = CityTerrainSurfacePlan.TrySampleGroundTop(city.Layout,
                    new Vector2(point.x, point.z), out float sand, out _);
                Debug.Log("PORT TERRAIN INTERFACE local=" + local + " sampled=" +
                    (found ? sand.ToString("F3") : "none") + " authored_top=" + point.y.ToString("F3"));
            }
            Physics.SyncTransforms();
            Assert.That(city.World.WalkableArea.Contains(port.Plan.World(new Vector3(-28f, 0f, -6f)), .3f),
                Is.False, "The water beside a narrow ramp must not become an invisible extension of the quay.");
            foreach (Vector3 local in new[] { new Vector3(-19.5f, 1.5f, -6f),
                new Vector3(0f, 1.5f, -20.5f), new Vector3(-23f, 1.5f, 20f),
                new Vector3(-28f, .91f, -20.5f), new Vector3(21f, 1.5f, -18f) })
            {
                Vector3 expected = port.Plan.World(local);
                Assert.That(city.World.WalkableArea.Contains(expected, .3f), Is.True);
                Assert.That(Physics.Raycast(expected + Vector3.up * 6f, Vector3.down,
                    out RaycastHit hit, 10f, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore), Is.True);
                Assert.That(hit.collider.transform.IsChildOf(port.transform), Is.True,
                    "The authored quay, yard and ramp must be the actual exposed ground: " + local);
                Assert.That(hit.point.y, Is.EqualTo(expected.y).Within(.08f));
            }
        }

        private static void ValidatePortAccess(CityPortController port, CityGameRoot city)
        {
            CityPortAccessPlan access=port.Plan.Access;
            Assert.That(access,Is.Not.Null);
            access.ValidateOrThrow(city.Layout);
            Assert.That(city.Layout.HasRoad(access.StreetEdge),Is.True);
            Assert.That(city.Layout.GetPathKind(access.StreetEdge),Is.EqualTo(CityPathKind.Street));
            Transform road=CityPortAssetProvider.FindPart(port.gameObject,"AccessRoad");
            Assert.That(road.GetComponentsInChildren<MeshCollider>(true).Length,Is.GreaterThan(0));
            bool texturedAsphalt=false;
            var roadProperties = new MaterialPropertyBlock();
            foreach(Renderer renderer in road.GetComponentsInChildren<Renderer>(true))
                if (renderer.enabled && renderer.name.EndsWith("__Asphalt", StringComparison.Ordinal))
                {
                    // The city's wet-surface owner binds its actual texture
                    // through a property block on a shared material.
                    renderer.GetPropertyBlock(roadProperties);
                    Texture texture=roadProperties.GetTexture("_BaseMap") ?? renderer.sharedMaterial.GetTexture("_BaseMap");
                    Assert.That(texture, Is.SameAs(Resources.Load<Texture2D>("Textures/CityRoadAsphaltAlbedo")));
                    texturedAsphalt=true;
                    Mesh mesh=renderer.GetComponent<MeshFilter>().sharedMesh;
                    Assert.That(mesh.uv.Length,Is.EqualTo(mesh.vertexCount),"The service road needs actual semantic UVs.");
                    roadProperties.Clear();
                }
            Assert.That(texturedAsphalt,Is.True,"The imported service road must use the authored asphalt texture.");
            Physics.SyncTransforms();
            for(int i=0;i<access.RoadSamples.Count;i+=4)
            {
                CityPortAccessPlan.RoadSample sample=access.RoadSamples[i];
                for(int side=-1;side<=1;side++)
                {
                    float lateral=sample.halfWidth*.90f*side;
                    Vector3 local=sample.center+sample.right*lateral+Vector3.up*(sample.crossfall*lateral);
                    AssertPortPavedPoint(port,city,local,"road "+i+" side "+side);
                }
                for(int side=-1;side<=1;side+=2)
                {
                    float lateral=(sample.halfWidth+.35f)*side;
                    AssertPortPavedPoint(port,city,sample.center+sample.right*lateral+
                        Vector3.up*(sample.crossfall*lateral),"flush shoulder "+i+" side "+side);
                }
            }
            foreach(Rect yard in new[]{access.LowerYard,access.UpperYard})
                for(float x=yard.xMin+1;x<yard.xMax;x+=3f)
                for(float z=yard.yMin+1;z<yard.yMax;z+=3f)
                    AssertPortPavedPoint(port,city,new Vector3(x-port.Plan.Origin.x,1.5f,z-port.Plan.Origin.z),"yard");
            ValidateRemovedPortFootpath(port, city);
            for(int leg=0;leg<3;leg++)
            {
                var kind=(CityPortTruckLeg)leg;
                int count=Mathf.CeilToInt(access.TruckRouteLength(kind)/.3f);
                CityPortTruckPose prior=access.SampleTruck(kind,0);
                for(int i=0;i<=count;i++)
                {
                    CityPortTruckPose pose=access.SampleTruck(kind,i/(float)count);
                    if(i>0)
                    {
                        Vector3 delta=pose.RearAxle-prior.RearAxle;
                        Assert.That(delta.magnitude,Is.LessThan(.34f),"A reserved truck route cannot teleport.");
                        if(delta.sqrMagnitude>.0001f)
                        {
                            Vector3 direction=pose.Rotation*Vector3.forward*(pose.Reversing?-1f:1f);
                            Assert.That(Vector3.Dot(delta.normalized,direction),Is.GreaterThan(.92f),
                                "The rigid truck follows its axle direction instead of rotating in place.");
                        }
                    }
                    foreach(Collider obstacle in Physics.OverlapBox(pose.Center+Vector3.up*1.8f,
                        new Vector3(access.TruckWidth*.5f,1.74f,access.TruckLength*.5f),pose.Rotation,
                        Physics.DefaultRaycastLayers,QueryTriggerInteraction.Ignore))
                    {
                        bool blocked=obstacle.name.StartsWith("COL_Warehouse",StringComparison.Ordinal)||
                            obstacle.name.StartsWith("COL_Rail",StringComparison.Ordinal)||
                            obstacle.name.StartsWith("COL_Awning",StringComparison.Ordinal)||
                            obstacle.name=="Terrain Guard Rails" || obstacle.name=="Safety Rails";
                        Assert.That(blocked,Is.False,"The full truck crosses "+obstacle.name+" at "+kind+" / "+i+
                            ", rear axle local "+(pose.RearAxle-port.Plan.Origin));
                    }
                    for(int end=-1;end<=1;end++)
                    for(int side=-1;side<=1;side++)
                    {
                        Vector3 corner=pose.Center+pose.Rotation*new Vector3(side*access.TruckWidth*.5f,0,end*access.TruckLength*.5f);
                        var xz=new Vector2(corner.x,corner.z);
                        bool paved=access.TrySampleTop(xz,out _);
                        if(!paved)
                            foreach(RoadEdge edge in city.Layout.RoadEdges)
                                if(city.Layout.GetPathKind(edge)==CityPathKind.Street)
                                {
                                    Rect carriageway=city.Layout.GetRoadRect(edge);
                                    if(carriageway.width>carriageway.height)
                                    {carriageway.yMin+=CityStreetSurfacePlanner.SidewalkWidth;carriageway.yMax-=CityStreetSurfacePlanner.SidewalkWidth;}
                                    else
                                    {carriageway.xMin+=CityStreetSurfacePlanner.SidewalkWidth;carriageway.xMax-=CityStreetSurfacePlanner.SidewalkWidth;}
                                    if(carriageway.Contains(xz)){paved=true;break;}
                                }
                        Assert.That(paved,Is.True,"The complete truck must fit on physical paving at "+kind+" / "+i);
                    }
                    prior=pose;
                }
            }
            CityPortTruckPose dock=access.SampleTruck(CityPortTruckLeg.ReverseToStore,1);
            Vector3 bumper=dock.RearAxle-dock.Rotation*Vector3.forward*2.6f-port.Plan.Origin;
            Assert.That(bumper.x,Is.EqualTo(4.4f).Within(.005f));
            Assert.That(bumper.z,Is.EqualTo(-14f).Within(.005f));
            CityPortTruckPose arrival=access.SampleTruck(CityPortTruckLeg.Arrive,0);
            CityPortTruckPose departure=access.SampleTruck(CityPortTruckLeg.Leave,1);
            Assert.That(Vector3.Dot(arrival.Rotation*Vector3.forward,Vector3.right),Is.GreaterThan(.999f));
            Assert.That(Vector3.Dot(departure.Rotation*Vector3.forward,Vector3.left),Is.GreaterThan(.999f));
            Assert.That(PortShoreEntryRoute(port).Count, Is.GreaterThan(1));
        }

        private static void AssertPortPavedPoint(CityPortController port,CityGameRoot city,Vector3 local,string label)
        {
            Vector3 point=port.Plan.World(local);
            Assert.That(Physics.Raycast(point+Vector3.up*7,Vector3.down,out RaycastHit hit,10,
                Physics.DefaultRaycastLayers,QueryTriggerInteraction.Ignore),Is.True,label+" needs physical paving.");
            Assert.That(hit.point.y,Is.EqualTo(point.y).Within(.055f),label+" must match the authored road/yard/path profile at "+local);
            if(CityTerrainSurfacePlan.TrySampleGroundTop(city.Layout,new Vector2(point.x,point.z),out float sand,out CitySurfaceDescriptor surface)
                && surface.Kind==CitySurfaceKind.Beach && port.Plan.Access.TrySampleTop(new Vector2(point.x,point.z),out _))
                Assert.That(sand,Is.LessThanOrEqualTo(point.y-.10f),label+" needs the terrain cut below its actual top.");
        }

        private static void ValidatePortCargoCycle(CityPortController port)
        {
            ValidatePortVisitCompletion(port);
            var ids = new HashSet<Transform>();
            var cageBounds = new Bounds[CityPortCycle.CargoCount];
            for (int index = 0; index < CityPortCycle.CargoCount; index++)
                Assert.That(ids.Add(port.Cargo[index]), Is.True);
            port.ApplyAt(CityPortCycle.UnloadStartSeconds, 15f);
            for (int index = 0; index < port.Cargo.Length; index++)
            {
                Vector3 hold = port.Vessel.InverseTransformPoint(port.Cargo[index].position);
                Assert.That(Vector3.Distance(hold, CityPortController.CargoHoldLocal(index)), Is.LessThan(.001f));
                Bounds cage = PortLocalMeshBounds(port.Cargo[index]);
                cageBounds[index] = cage;
                Assert.That(cage.size.x, Is.LessThan(1.35f));
                Assert.That(hold.y + cage.max.y, Is.LessThan(1.6f), "Loaded cages must fit inside real open holds.");
            }
            Bounds cart = PortLocalMeshBounds(port.Trolley);
            int priorStored = 0;
            for (double seconds = 0d; seconds < CityPortCycle.CycleDurationSeconds; seconds += 1d)
            {
                port.ApplyAt(seconds, 15f);
                CityPortCycleSnapshot now = port.Snapshot;
                Assert.That(now.StoredCargo, Is.InRange(priorStored, CityPortCycle.CargoCount));
                Assert.That(now.LandedCargo, Is.InRange(now.StoredCargo, CityPortCycle.CargoCount));
                priorStored = now.StoredCargo;
                if (now.Stage != CityPortCycleStage.Unload) continue;
                Assert.That(now.ActiveCraneIndex, Is.EqualTo(now.CargoIndex % 2));
                Transform cage = port.Cargo[now.CargoIndex];
                if (now.SecondsInCargo >= CityPortCycle.HookedAtSeconds && now.SecondsInCargo < CityPortCycle.StoredAtSeconds)
                {
                    Bounds bounds = cageBounds[now.CargoIndex];
                    foreach (Collider obstacle in Physics.OverlapBox(cage.TransformPoint(bounds.center),
                        bounds.extents, cage.rotation, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore))
                        Assert.That(obstacle.name.StartsWith("COL_Awning", StringComparison.Ordinal), Is.False,
                            "The real loaded cage crosses the tare canopy at " + seconds);
                }
                if (now.SecondsInCargo >= CityPortCycle.HookedAtSeconds && now.SecondsInCargo < CityPortCycle.LandedAtSeconds)
                {
                    Vector3 lift = CityPortAssetProvider.FindPart(cage.gameObject, "ANCHOR_Lift").position;
                    Vector3 hook = CityPortAssetProvider.FindPart(port.Hooks[now.ActiveCraneIndex].gameObject, "ANCHOR_Load").position;
                    Assert.That(Vector3.Distance(lift, hook), Is.LessThan(.005f), "A carried cage must stay attached to the actual hook.");
                }
                if (now.SecondsInCargo >= CityPortCycle.LandedAtSeconds && now.SecondsInCargo < CityPortCycle.StoredAtSeconds)
                    Assert.That(Vector3.Distance(cage.position,
                        CityPortAssetProvider.FindPart(port.Trolley.gameObject, "ANCHOR_Load").position), Is.LessThan(.005f));
                if (now.SecondsInCargo >= CityPortCycle.UnhookedAtSeconds)
                {
                    Assert.That(port.TrolleyOperatorSpeed, Is.LessThanOrEqualTo(2.5f),
                        "The worker must walk around the cart's real handle at its corners.");
                    Collider[] obstacles = Physics.OverlapBox(port.Trolley.TransformPoint(cart.center),
                        cart.extents * .96f, port.Trolley.rotation, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);
                    foreach (Collider obstacle in obstacles)
                        Assert.That(obstacle.name.StartsWith("COL_Warehouse", StringComparison.Ordinal), Is.False,
                            "The trolley crosses the cold-store wall at " + seconds);
                }
            }
            Assert.That(priorStored, Is.EqualTo(CityPortCycle.CargoCount));
            for (int index = 0; index < CityPortCycle.CargoCount; index++)
            {
                double start = CityPortCycle.UnloadStartSeconds + index * CityPortCycle.CargoDurationSeconds;
                Assert.That(CityPortCycle.Sample(start + CityPortCycle.LandedAtSeconds - .001d).LandedCargo, Is.EqualTo(index));
                Assert.That(CityPortCycle.Sample(start + CityPortCycle.LandedAtSeconds).LandedCargo, Is.EqualTo(index + 1));
                Assert.That(CityPortCycle.Sample(start + CityPortCycle.StoredAtSeconds - .001d).StoredCargo, Is.EqualTo(index));
                Assert.That(CityPortCycle.Sample(start + CityPortCycle.StoredAtSeconds).StoredCargo, Is.EqualTo(index + 1));
            }
            Vector3 source = port.Plan.World(new Vector3(0f, 2.7f, -10f));
            Vector3 destination = port.Plan.World(port.Plan.WarehouseDropLocal) + Vector3.up * 1.2f;
            Assert.That(Physics.Linecast(source, destination, out RaycastHit wall,
                Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore), Is.True);
            Assert.That(wall.collider.name.StartsWith("COL_Warehouse", StringComparison.Ordinal), Is.True,
                "The stored cargo must leave presentation behind physical opaque cold-store walls.");
        }

        private static void ValidatePortVisitCompletion(CityPortController port)
        {
            Assert.That(CityPortCycle.CargoCount, Is.EqualTo(3));
            Assert.That(CityPortCycle.CycleDurationSeconds, Is.EqualTo(432d));
            Assert.That(port.Cargo.Length, Is.EqualTo(3));
            int cargoObjects=0;
            foreach(Transform part in port.GetComponentsInChildren<Transform>(true))
                if(part.name.StartsWith("Port Cargo ",StringComparison.Ordinal))cargoObjects++;
            Assert.That(cargoObjects,Is.EqualTo(3),"One ship visit owns exactly three cargo models, including hidden ones.");
            CityPortCrew crew=port.GetComponentInChildren<CityPortCrew>();
            Assert.That(crew,Is.Not.Null);
            double saved=port.ElapsedSeconds;
            double secure=CityPortCycle.UnloadStartSeconds+CityPortCycle.UnloadDurationSeconds;
            try
            {
                port.ApplyAt(secure-.001d,15f);
                crew.ApplyAt(port.ElapsedSeconds);
                Vector3 deckhand=crew.Deckhand.transform.position,trolley=port.Trolley.position;
                port.ApplyAt(secure+.001d,15f);
                crew.ApplyAt(port.ElapsedSeconds);
                Assert.That(port.Snapshot.Stage,Is.EqualTo(CityPortCycleStage.Secure));
                Assert.That(port.Snapshot.StoredCargo,Is.EqualTo(3));
                Assert.That(Vector3.Distance(crew.Deckhand.transform.position,deckhand),Is.LessThan(.01f),
                    "After the odd last cargo the deckhand must start securing from the hatch he actually reached.");
                Assert.That(Vector3.Distance(port.Trolley.position,trolley),Is.LessThan(.01f),
                    "The last empty trolley return must meet its secured parking position.");
                Assert.That(crew.CaptainHandsMatch,Is.True);
                Assert.That(crew.CraneHandsMatch,Is.True);
                Assert.That(crew.TrolleyHandsMatch,Is.True);
            }
            finally
            {
                port.ApplyAt(saved,15f);
                crew.ApplyAt(port.ElapsedSeconds);
            }
        }

        private static Bounds PortLocalMeshBounds(Transform root)
        {
            Vector3 low = Vector3.positiveInfinity, high = Vector3.negativeInfinity;
            foreach (MeshFilter filter in root.GetComponentsInChildren<MeshFilter>(true))
            {
                if (filter.name.StartsWith("COL_", StringComparison.Ordinal)) continue;
                foreach (Vector3 vertex in filter.sharedMesh.vertices)
                {
                    Vector3 local = root.InverseTransformPoint(filter.transform.TransformPoint(vertex));
                    low = Vector3.Min(low, local);
                    high = Vector3.Max(high, local);
                }
            }
            Assert.That(float.IsInfinity(low.x), Is.False);
            return new Bounds((low + high) * .5f, high - low);
        }

        private static IEnumerator CapturePort(Camera camera, CityGameRoot city, CityPortController port,
            CityPortCrew crew, double seconds, string name, Vector3 fromLocal, Vector3 targetLocal)
        {
            Assert.That(port.AutoAdvance, Is.False, "A capture must hold its requested port timestamp.");
            PlayerCameraFollow follow = camera.GetComponent<PlayerCameraFollow>();
            bool followWasEnabled = follow != null && follow.enabled;
            if (follow != null) follow.enabled = false;
            try
            {
                port.ApplyAt(seconds, 15f);
                crew.ApplyAt(port.ElapsedSeconds);
                Vector3 from = port.Plan.World(fromLocal);
                city.Player.Motor.Teleport(from - Vector3.up * EyeHeight);
                Vector3 target = port.Snapshot.Stage == CityPortCycleStage.Approach || port.Snapshot.Stage == CityPortCycleStage.Depart
                    ? port.Vessel.position + Vector3.up * 2.5f : port.Plan.World(targetLocal);
                Quaternion rotation = Quaternion.LookRotation(target - from);
                camera.transform.SetPositionAndRotation(from, rotation);
                camera.fieldOfView = 68f;
                Physics.SyncTransforms();

                // Production uses GPU Resident Drawer, whose transform uploads
                // are frame based. Multiple seeks and Camera.Render calls in one
                // frame can draw an earlier pose despite correct CPU transforms.
                // Let normal LateUpdate (including crew) and render frames settle;
                // WaitForEndOfFrame is not dispatched by the batch-mode runner.
                int firstFrame = Time.frameCount;
                for (int frame = 0; frame < 3; frame++) yield return null;
                Assert.That(Time.frameCount, Is.GreaterThanOrEqualTo(firstFrame + 3));
                Assert.That(port.ElapsedSeconds, Is.EqualTo(seconds));
                Assert.That(crew.LastSnapshot.Stage, Is.EqualTo(port.Snapshot.Stage));
                Assert.That(crew.LastSnapshot.SecondsInStage, Is.EqualTo(port.Snapshot.SecondsInStage));
                Assert.That(Vector3.Distance(camera.transform.position, from), Is.LessThan(.001f));
                Assert.That(Quaternion.Angle(camera.transform.rotation, rotation), Is.LessThan(.001f));
                CaptureCurrentCamera(camera, SceneIds.City, name);
            }
            finally
            {
                if (follow != null) follow.enabled = followWasEnabled;
            }
        }
    }
}

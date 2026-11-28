using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace BarPromenade.Tests.PlayMode
{
    public sealed class CityRockfaceAssetsSetup : IPrebuildSetup
    {
        public void Setup()
        {
            new CityRockfallAssetsSetup().Setup();
#if UNITY_EDITOR
            Type.GetType("BarPromenade.Editor.CityRockfaceAssetSetup, BarPromenade.Editor", true)
                .GetMethod("BuildOrThrow", Type.EmptyTypes).Invoke(null, null);
#endif
        }
    }

    public sealed partial class AreaCaptureFixture
    {
        [UnityTest]
        [Explicit("Physical ridge relief at the reported summit wall and along the original approaches. Run alone.")]
        [PrebuildSetup(typeof(CityRockfaceAssetsSetup))]
        public IEnumerator CityMountainRockfaces() => CaptureCityLocalAcceptance(ReviewMountainRockfaces);

        [UnityTest]
        [Explicit("Inspect the western beach edge, the joined rock and its submerged tip.")]
        public IEnumerator CityBeachHeadland() => CaptureCityLocalAcceptance(ReviewBeachHeadland);

        private static IEnumerator ReviewBeachHeadland(CityGameRoot city)
        {
            CitySeacoastFrame coast = city.World.SeacoastPlan.Frame;
            var rock = city.World.MountainBoundaryRoot.transform.Find("Physical Ridges/mountain-west-north");
            var colliders = rock.GetComponentsInChildren<MeshCollider>();
            var ridge = city.World.MountainBoundaryPlan.Ridges.Single(r => r.StableId == rock.name);
            var reported = new Vector3(-155.6f, 5.5f, 167.2f);
            city.Player.Motor.Teleport(reported);
            city.Visibility.Refresh(true);
            Physics.SyncTransforms();

            var verge = city.World.MountainBoundaryRoot.transform.Find("West Cape Verge").GetComponent<MeshCollider>();
            Assert.That(verge.GetComponent<MeshFilter>().sharedMesh, Is.SameAs(verge.sharedMesh));
            var streets = CityStreetSupportPlan.ForLayout(city.Layout);
            for (float z = 150.13f; z <= 171.6f; z += .33f)
            for (float x = -168.11f; x <= -149f; x += .33f)
            {
                var point = new Vector2(x, z);
                var ray = new Ray(new Vector3(x, 40f, z), Vector3.down);
                if (streets.TrySampleTop(point, out float paved, out _))
                {
                    Assert.That(RockHeight(point), Is.LessThan(paved), $"Rock crosses paving at {point}");
                    if (verge.Raycast(ray, out var soil, 80f))
                        Assert.That(soil.point.y, Is.LessThan(paved + .015f), $"Verge covers paving at {point}");
                }
                if (z >= 155.6f && x <= -155f)
                {
                    Assert.That(Physics.Raycast(ray, out var hit, 60f, Physics.DefaultRaycastLayers,
                        QueryTriggerInteraction.Ignore), Is.True, $"Open ground seam at {point}");
                    Assert.That(hit.point.y, Is.GreaterThan(coast.SeaTopY), $"Exposed underside at {point}");
                }
            }

            var motor = city.Player.Motor;
            Vector3 Road(float x, float z)
            {
                Assert.That(streets.TrySampleTop(new Vector2(x, z), out float top, out _), Is.True);
                return new Vector3(x, top + PlayerFactory.GroundedRootOffset, z);
            }
            motor.Teleport(Road(-156f, 150f));
            foreach (var target in new[] { Road(-156f, 156f), Road(-149f, 156f) })
            {
                bool reached = false;
                for (int step = 0; step < 200 && !reached; step++)
                    reached = motor.MoveTowardsApproachWaypoint(target, .12f, .05f);
                Assert.That(reached, Is.True, $"Player blocked on the bend towards {target}");
            }
            var eye = Road(-155.9f, 155f) + Vector3.up * (EyeHeight - PlayerFactory.GroundedRootOffset);
            yield return LocalAcceptanceFrame(city, "west-cape-road-corner", eye, new Vector3(-164f, eye.y - .6f, 162f));
            yield return LocalAcceptanceFrame(city, "west-cape-road-reverse",
                Road(-148f, 156f) + Vector3.up, new Vector3(-162f, eye.y - 1f, 161f));
            var sandContact = new Vector3(-158.1f, 25f, 164.5f);
            Assert.That(Physics.Raycast(sandContact, Vector3.down, out var blendHit, 40f,
                Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore), Is.True);
            var blendRenderer = blendHit.collider.GetComponent<MeshRenderer>();
            var block = new MaterialPropertyBlock();
            blendRenderer.GetPropertyBlock(block, CommonGroundSlot(blendHit));
            Assert.That(CitySurfaceLibrary.TryGroundJointMaps(
                CitySeacoastSurfaceAppearance.GetTexture(CitySeacoastSurfaceKind.Sand), out var sand, out var response), Is.True);
            Assert.That(block.GetTexture("_GroundSubstrateMap"), Is.SameAs(sand));
            Assert.That(block.GetTexture("_GroundSubstrateResponse"), Is.SameAs(response));
            var mix = block.GetVector(GroundSurfaceAppearance.ShoulderMixId);
            Assert.That(mix.w, Is.EqualTo(1f));
            var mesh = ((MeshCollider)blendHit.collider).sharedMesh;
            var distances = new List<Vector4>();
            mesh.GetUVs(GroundSurfaceCoordinates.Channel, distances);
            var indices = mesh.triangles;
            int first = blendHit.triangleIndex * 3;
            var weights = blendHit.barycentricCoordinate;
            float distance = Vector3.Dot(weights, new Vector3(distances[indices[first]].x,
                distances[indices[first + 1]].x, distances[indices[first + 2]].x));
            Assert.That(distance / mix.y, Is.InRange(.25f, .75f), "Soil and sand both contribute here.");
            var beach = city.Layout.Surfaces.Single(s => CityBeachSandPlan.BordersWestCape(city.Layout.SpatialPlan, s));
            var grains = new CityBeachSandPlan.Field(city.Layout.ElevationPlan, beach);
            var edge = new Vector2(beach.WorldBounds.xMin, sandContact.z);
            Assert.That(grains.SampleLooseDepth(edge), Is.Zero, "The loose skin must not form a lip at the verge.");
            Assert.That(grains.SampleLooseDepthGradient(edge), Is.EqualTo(Vector2.zero));
            Vector2 halfway = edge + Vector2.right * .7f;
            const float slopeStep = .01f;
            float slope = (grains.SampleLooseDepth(halfway + Vector2.right * slopeStep) -
                grains.SampleLooseDepth(halfway - Vector2.right * slopeStep)) / (2f * slopeStep);
            Assert.That(grains.SampleLooseDepthGradient(halfway).x, Is.EqualTo(slope).Within(.001f));
            yield return LocalAcceptanceFrame(city, "west-cape-sand-transition",
                blendHit.point + new Vector3(4.4f, 1.55f, 2.2f), blendHit.point, 68f);
            foreach (var point in new[] { new Vector2(-156.8f, 179f), new Vector2(-157f, coast.WaterlineZ) })
                Assert.That(RockHeight(point), Is.GreaterThan(coast.SeaTopY));
            var outerShoulder = ridge.Stations[ridge.Stations.Count - 4];
            var tip = ridge.Stations.Last();
            Vector3 crest = Vector3.Lerp(outerShoulder.Toe, outerShoulder.OuterFoot, .52f);
            Assert.That(crest.z, Is.GreaterThan(coast.WaterlineZ + 12f));
            Assert.That(RockHeight(new Vector2(crest.x, crest.z)), Is.GreaterThan(coast.SeaTopY + 2f));
            Assert.That(tip.PeakY, Is.LessThan(coast.SeaTopY - CitySeaResources.WaveHeight));

            yield return LocalAcceptanceFrame(city, "west-cape-beach-edge",
                reported + new Vector3(1.1f, 1.6f, -.5f), new Vector3(-162f, 7f, 185f));
            yield return LocalAcceptanceFrame(city, "west-cape-from-the-beach",
                new Vector3(-132f, 6.7f, 171f), new Vector3(-158f, 6.5f, 189f));
#if UNITY_EDITOR
            if (Environment.GetEnvironmentVariable("BP_EXPORT_CAPE_SURVEY") == "1")
                Type.GetType("BarPromenade.Editor.CityMapArtReferenceExport, BarPromenade.Editor", true)
                    .GetMethod("ExportCurrent").Invoke(null, null);
#endif
            float RockHeight(Vector2 point)
            {
                var ray = new Ray(new Vector3(point.x, 60f, point.y), Vector3.down);
                float top = float.NegativeInfinity;
                foreach (var collider in colliders)
                    if (collider.Raycast(ray, out var hit, 100f)) top = Mathf.Max(top, hit.point.y);
                return top;
            }
        }

        [UnityTest]
        [Explicit("Render the reported City rim with a live local fog field.")]
        public IEnumerator CityRimFogVisibility() => CaptureCityLocalAcceptance(ReviewRimFogVisibility);

        [UnityTest]
        [Explicit("Check the live fog at the reported southeastern summit edge.")]
        public IEnumerator CitySouthEastRimFogAtReportedEdge() => CaptureCityLocalAcceptance(ReviewSouthEastRimFog);

        private static IEnumerator ReviewSouthEastRimFog(CityGameRoot city)
        {
            var reported = new Vector3(132.5f, 38.1f, -373.7f);
            city.Player.Motor.Teleport(reported);
            var rims = city.World.MountainBoundaryRoot.transform.Find("Physical Ridges");
            var rock = rims.Find("mountain-south-east-outer-rim");
            Assert.That(rock, Is.Not.Null);
            var faces = rock.GetComponentsInChildren<MeshRenderer>();
            var colliders = rock.GetComponentsInChildren<MeshCollider>();
            var mist = city.Night.FogField;
            mist.SetSheltered(true);
            mist.SetSheltered(false);
            Assert.That(mist.Particles.particleCount, Is.Positive);
            yield return null;

            var camera = city.Camera;
            var eye = reported + new Vector3(-1.8f, 1.65f, .55f);
            var material = mist.FogRenderer.sharedMaterial;
            int opacityId = Shader.PropertyToID("_NearSurfaceOpacity");
            float limit = material.GetFloat(opacityId);
            using (GameTimeScaleRuntime.AcquirePause())
            {
                foreach (var (name, direction) in new[]
                {
                    ("southeast-rim-grazing", new Vector3(1f, .025f, -.20f)),
                    ("southeast-rim-facing", new Vector3(0f, .18f, -1f))
                })
                {
                    PoseLocalAcceptanceCamera(city, eye, eye + direction, 57f);
                    yield return null;
                    var ray = new Ray(eye, camera.transform.forward);
                    Assert.That(colliders.Any(body => body.Raycast(ray, out _, 34f)), Is.True,
                        name + ": the sightline should hit the physical rock.");
                    try
                    {
                        yield return CaptureRimFrame(camera, name);
                        material.SetFloat(opacityId, .02f);
                        yield return CaptureRimFrame(camera, name + "-previous");
                        material.SetFloat(opacityId, limit);
                        mist.FogRenderer.forceRenderingOff = true;
                        yield return CaptureRimFrame(camera, name + "-air-clear");
                        mist.FogRenderer.forceRenderingOff = false;
                        foreach (var face in faces) face.forceRenderingOff = true;
                        yield return CaptureRimFrame(camera, name + "-rock-hidden");
                    }
                    finally
                    {
                        material.SetFloat(opacityId, limit);
                        mist.FogRenderer.forceRenderingOff = false;
                        foreach (var face in faces) face.forceRenderingOff = false;
                    }
                    float contrast = RimDifference(name, name + "-rock-hidden");
                    Assert.That(contrast, Is.GreaterThan(18f), name + ": the physical rock disappeared into the local fog.");
                    Assert.That(RimDifference(name, name + "-air-clear"), Is.LessThan(contrast * .25f),
                        name + ": dense volumes washed out the rock.");
                }
            }

            var chase = camera.GetComponent<PlayerCameraFollow>();
            bool orbitInput = chase.OrbitInputEnabled;
            try
            {
                GameSessionState.UpdateDrinkingProgress(58, DrinkId.Vodka, 4);
                city.Rain.SetIntensity(.45f);
                city.Player.GameObject.transform.rotation = Quaternion.LookRotation(Vector3.right);
                chase.Initialize(camera, city.Player.GameObject.transform, false);
                chase.SetOrbitInputEnabled(false);
                chase.RotatePitch(-8f);
                chase.enabled = true;
                for (int frame = 0; frame < 10; frame++) yield return null;
                Assert.That(GameSessionState.IntoxicationLevel, Is.EqualTo(58));

                using (GameTimeScaleRuntime.AcquirePause())
                {
                    foreach (int angle in new[] { -20, 0, 20 })
                    {
                        chase.RotateYaw(angle);
                        chase.Snap();
                        yield return null;
                        string name = $"southeast-rim-chase-{angle}";
                        try
                        {
                            yield return CaptureRimFrame(camera, name);
                            foreach (var face in faces) face.forceRenderingOff = true;
                            yield return CaptureRimFrame(camera, name + "-rock-hidden");
                        }
                        finally
                        {
                            foreach (var face in faces) face.forceRenderingOff = false;
                        }
                        chase.RotateYaw(-angle);
                        Assert.That(RimDifference(name, name + "-rock-hidden", .81f), Is.GreaterThan(12f),
                            name + ": shoulder camera lost the rock on the right side of the view.");
                    }
                }
            }
            finally
            {
                chase.enabled = false;
                chase.SetOrbitInputEnabled(orbitInput);
                city.Rain.SetIntensity(0f);
            }
        }

        private static IEnumerator ReviewRimFogVisibility(CityGameRoot city)
        {
            var foot = new Vector3(-171f, 36.4f, -362f);
            city.Player.Motor.Teleport(foot);
            CityFogField mist = city.Night.FogField;
            mist.SetSheltered(true);
            mist.SetSheltered(false);
            Assert.That(mist.Particles.particleCount, Is.Positive);

            var rims = city.World.MountainBoundaryRoot.transform.Find("Physical Ridges");
            var rock = rims.Find("mountain-south-west-before-tunnel");
            var faces = rock.GetComponentsInChildren<MeshRenderer>();
            foreach (Vector3 outward in new[] { Vector3.left, Vector3.back })
            {
                string side = outward == Vector3.left ? "west" : "south";
                Vector3 eye = foot - outward * 3f + Vector3.up * 1.9f;
                PoseLocalAcceptanceCamera(city, eye, foot + outward * 22f + Vector3.up * 6f, 70f);
                yield return null;
                var sightline = new Ray(eye, city.Camera.transform.forward);
                Assert.That(rock.GetComponentsInChildren<MeshCollider>()
                    .Any(body => body.Raycast(sightline, out _, 12f)), Is.True,
                    "The camera must aim at a nearby physical wall.");

                string frame = "rim-fog-" + side;
                using (GameTimeScaleRuntime.AcquirePause())
                {
                    yield return CaptureRimFrame(city.Camera, frame);
                    try
                    {
                        mist.FogRenderer.forceRenderingOff = true;
                        yield return CaptureRimFrame(city.Camera, frame + "-clear");
                        mist.FogRenderer.forceRenderingOff = false;
                        foreach (var face in faces) face.forceRenderingOff = true;
                        yield return CaptureRimFrame(city.Camera, frame + "-hidden");
                    }
                    finally
                    {
                        mist.FogRenderer.forceRenderingOff = false;
                        foreach (var face in faces) face.forceRenderingOff = false;
                    }
                }
                float contrast = RimDifference(frame, frame + "-hidden");
                Assert.That(contrast, Is.GreaterThan(18f), "Local fog erased the visible rock face.");
                Assert.That(RimDifference(frame, frame + "-clear"), Is.LessThan(contrast * .20f),
                    "Dense fog washed out the near rock.");
            }

            // A view from the ascent has a longer fog path than the shot at the toe.
            Vector3 approachEye = foot + Vector3.right * 40f + Vector3.up * 6f;
            city.Player.Motor.Teleport(approachEye - Vector3.up * 1.9f);
            PoseLocalAcceptanceCamera(city, approachEye, foot + Vector3.left * 7f + Vector3.up * 11f, 62f);
            mist.SetSheltered(true);
            mist.SetSheltered(false);
            yield return null;
            Material material = faces[0].sharedMaterial;
            float density = material.GetFloat("_FogDensity");
            float stableHandoff = material.GetFloat("_StableHazeHandoff");
            using (GameTimeScaleRuntime.AcquirePause())
            {
                yield return CaptureRimFrame(city.Camera, "rim-fog-approach");
                try
                {
                    material.SetFloat("_FogDensity", RuntimeSceneSetup.CityFogDensity);
                    material.SetFloat("_StableHazeHandoff", 0f);
                    yield return CaptureRimFrame(city.Camera, "rim-fog-approach-old");
                    foreach (var face in faces) face.forceRenderingOff = true;
                    yield return CaptureRimFrame(city.Camera, "rim-fog-approach-hidden");
                }
                finally
                {
                    material.SetFloat("_FogDensity", density);
                    material.SetFloat("_StableHazeHandoff", stableHandoff);
                    foreach (var face in faces) face.forceRenderingOff = false;
                }
            }
            float currentContrast = RimDifference("rim-fog-approach", "rim-fog-approach-hidden");
            float previousContrast = RimDifference("rim-fog-approach-old", "rim-fog-approach-hidden");
            Assert.That(currentContrast, Is.GreaterThan(previousContrast + 5f),
                "The rock should be easier to distinguish from haze on approach.");
        }

        private static IEnumerator CaptureRimFrame(Camera camera, string frame)
        {
            if (Application.isBatchMode)
            {
                CaptureCurrentCamera(camera, SceneIds.City, frame, 640, 360);
                yield break;
            }
            yield return null;
            yield return new WaitForEndOfFrame();
            Assert.That(camera.targetTexture, Is.Null, "Read back the normal Game View target.");
            Texture2D screenshot = ScreenCapture.CaptureScreenshotAsTexture();
            try
            {
                string folder = Path.Combine(Directory.GetCurrentDirectory(), "Captures", SceneIds.City);
                Directory.CreateDirectory(folder);
                File.WriteAllBytes(Path.Combine(folder, frame + ".png"), screenshot.EncodeToPNG());
            }
            finally { UnityEngine.Object.DestroyImmediate(screenshot); }
        }

        internal static float RimDifference(string first, string second, float horizontal = .5f)
        {
            var image = new Texture2D(2, 2, TextureFormat.RGB24, false);
            try
            {
                Color32[] a = Read(first), b = Read(second);
                float difference = 0f;
                int width = image.width, height = image.height;
                int sampleWidth = Mathf.Max(1, width * 3 / 40), sampleHeight = Mathf.Max(1, height * 4 / 45);
                int left = Mathf.RoundToInt(width * horizontal - sampleWidth * .5f);
                int bottom = (height - sampleHeight) / 2;
                for (int y = bottom; y < bottom + sampleHeight; y++)
                for (int x = left; x < left + sampleWidth; x++)
                    difference += Mathf.Abs(a[y * width + x].g - b[y * width + x].g);
                return difference / (sampleWidth * sampleHeight);
            }
            finally { UnityEngine.Object.DestroyImmediate(image); }

            Color32[] Read(string frame)
            {
                string path = Path.Combine(Directory.GetCurrentDirectory(), "Captures", SceneIds.City, frame + ".png");
                Assert.That(image.LoadImage(File.ReadAllBytes(path)), Is.True);
                return image.GetPixels32();
            }
        }

        private static IEnumerator ReviewMountainRockfaces(CityGameRoot city)
        {
            yield return ReviewFringeApproaches(city);
            var plan = city.World.MountainBoundaryPlan;
            var ridges = city.World.Root.transform.Find("Mountain Boundary/Physical Ridges");
            Assert.That(ridges, Is.Not.Null);
            for (int i = 0; i < CityRockfaceAssetProvider.VariantCount; i++)
            {
                Mesh source = CityRockfaceAssetProvider.GetMesh(i);
                Assert.That(source.isReadable, Is.True, source.name);
                Assert.That(source.bounds.min.x, Is.EqualTo(0f).Within(.001f), source.name);
                Assert.That(source.bounds.min.y, Is.EqualTo(0f).Within(.001f), source.name);
                Assert.That(source.bounds.max.x, Is.EqualTo(1f).Within(.001f), source.name);
                Assert.That(source.bounds.max.y, Is.EqualTo(1f).Within(.001f), source.name);
                Assert.That(source.bounds.min.z, Is.EqualTo(0f).Within(.001f), source.name);
                Assert.That(source.bounds.max.z, Is.InRange(.05f, 1.001f), source.name);
                foreach (Vector3 vertex in source.vertices)
                    if (vertex.x < .0001f || vertex.x > .9999f || vertex.y < .0001f || vertex.y > .9999f)
                        Assert.That(Mathf.Abs(vertex.z), Is.LessThan(.001f), source.name + " has an open seam.");
            }
            foreach (var ridge in plan.Ridges)
            {
                Transform owner = ridges.Find(ridge.StableId);
                Assert.That(owner, Is.Not.Null, ridge.StableId);
                var chunks = owner.GetComponentsInChildren<MeshFilter>();
                Assert.That(chunks.Length, Is.InRange(1, ridge.Stations.Count - 1));
                Assert.That(owner.GetComponentsInChildren<MeshCollider>(), Has.Length.EqualTo(chunks.Length));
                foreach (var chunk in chunks)
                {
                    Transform physical = owner.Find(chunk.name.Replace(" Chunk ", " Toe Collider "));
                    Assert.That(physical, Is.Not.Null, chunk.name);
                    Assert.That(physical.GetComponent<MeshCollider>().sharedMesh, Is.SameAs(chunk.sharedMesh),
                        chunk.name + " must stop the player on the rendered facets.");
                    Assert.That(chunk.sharedMesh.vertexCount, Is.LessThanOrEqualTo((int)ushort.MaxValue), chunk.name);
                }
            }

            var summit = plan.Ridges.Single(r => r.StableId == "mountain-south-west-before-tunnel");
            var reported = new Vector3(-36.2f, 39.8f, -373.7f);
            // This coordinate comes from the player's HUD. Follow the plan to the
            // actual physical toe instead of assuming a cliff direction from its ID.
            int section = -1;
            for (int i = 0; i + 1 < summit.Stations.Count; i++)
            {
                Vector2 a = summit.Stations[i].WorldXZ, b = summit.Stations[i + 1].WorldXZ;
                if (a.x <= reported.x && reported.x <= b.x &&
                    Mathf.Abs(a.y - reported.z) < 1f && Mathf.Abs(b.y - reported.z) < 1f)
                { section = i; break; }
            }
            Assert.That(section, Is.GreaterThanOrEqualTo(0), "The reported wall must belong to a physical summit segment.");
            var left = summit.Stations[section];
            var right = summit.Stations[section + 1];
            float along = Mathf.InverseLerp(left.Toe.x, right.Toe.x, reported.x);
            Vector3 toe = Vector3.Lerp(left.Toe, right.Toe, along);
            Vector3 outward = Vector3.Lerp(left.OutwardNormal, right.OutwardNormal, along).normalized;
            Vector3 tangent = new Vector3(-outward.z, 0f, outward.x);
            Assert.That(Vector3.Distance(toe, reported), Is.LessThan(1.3f), "Wrong physical toe or elevation.");
            AssertRockfaceProjectsFromSlope(ridges.Find(summit.StableId), left, right, reported.x);

            Vector3 closeEye = SupportedRockfaceEye(city, toe, outward, .8f);
            Assert.That(Vector2.Distance(new Vector2(closeEye.x, closeEye.z), new Vector2(reported.x, reported.z)),
                Is.LessThan(3.5f), "The close views must address the reported spot.");
            city.Player.Motor.Teleport(closeEye - Vector3.up * (EyeHeight - PlayerFactory.GroundedRootOffset));
            yield return null;
            yield return LocalAcceptanceFrame(city, "rockface-summit-reported-front", closeEye,
                toe + outward * 1.3f + Vector3.up * 2.25f);
            yield return LocalAcceptanceFrame(city, "rockface-summit-reported-left", closeEye,
                toe - tangent * 8f + outward + Vector3.up * 2.4f);
            yield return LocalAcceptanceFrame(city, "rockface-summit-reported-right", closeEye,
                toe + tangent * 8f + outward + Vector3.up * 2.4f);
            yield return LocalAcceptanceFrame(city, "rockface-summit-reported-up", closeEye,
                Vector3.Lerp(CityMountainBoundaryMeshFactory.CreateCrossSection(left)[4],
                    CityMountainBoundaryMeshFactory.CreateCrossSection(right)[4], along));

            Vector3 streetEye = SupportedRockfaceEye(city, toe, outward, 24f);
            yield return LocalAcceptanceFrame(city, "rockface-summit-street", streetEye, toe + Vector3.up * 5.5f);
            var westYard = city.World.FringeYardPlan.Yards.Single(y => y.Kind == CityFringeYardKind.WestStoneTerraces);
            Vector3 westToe = westYard.Access.Center;
            westToe.x = westYard.AreaBounds.xMin;
            Vector3 westEye = SupportedRockfaceEye(city, westToe, Vector3.left, 7f);
            westToe.y = westEye.y - EyeHeight;
            yield return LocalAcceptanceFrame(city, "rockface-west-north", westEye, westToe + Vector3.up * 3f);

            var eastRim = plan.Ridges.Single(r => r.StableId == "mountain-south-east-outer-rim");
            var east = eastRim.Stations.Where(s => s.OutwardNormal.z < -.97f)
                .OrderBy(s => Mathf.Abs(s.WorldXZ.x - 59f) + Mathf.Abs(s.WorldXZ.y - toe.z)).First();
            Vector3 eastEye = SupportedRockfaceEye(city, east.Toe, east.OutwardNormal, 8f);
            yield return LocalAcceptanceFrame(city, "rockface-summit-east-rim", eastEye, east.Toe + Vector3.up * 3.7f);
        }

        private static Vector3 SupportedRockfaceEye(CityGameRoot city, Vector3 toe, Vector3 outward, float distance)
        {
            var street = CityStreetSupportPlan.ForLayout(city.Layout);
            var hill = CityHillsideGroundPlan.ForLayout(city.Layout);
            Vector3 tangent = new Vector3(-outward.z, 0f, outward.x);
            foreach (float along in new[] { 0f, -1.25f, 1.25f, -3f, 3f, -6f, 6f })
            foreach (float step in new[] { 0f, .5f, 1f, 2f })
            {
                Vector3 foot = toe - outward * (distance + step) + tangent * along;
                Vector2 point = new Vector2(foot.x, foot.z);
                if (!street.TrySampleTop(point, out foot.y, out _) && !hill.TrySampleTop(point, out foot.y) &&
                    !CityTerrainSurfacePlan.TrySampleGroundTop(city.Layout, point, out foot.y, out _)) continue;
                if (!city.World.WalkableArea.Contains(foot, .36f)) continue;
                bool supported = Physics.RaycastAll(foot + Vector3.up, Vector3.down, 2f,
                    Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore)
                    .Any(hit => hit.normal.y > .7f && Mathf.Abs(hit.point.y - foot.y) < .25f &&
                        !hit.collider.transform.IsChildOf(city.Player.GameObject.transform));
                if (!supported || Physics.OverlapCapsule(foot + Vector3.up * .72f, foot + Vector3.up * 1.45f,
                        .36f, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore)
                    .Any(obstacle => !obstacle.transform.IsChildOf(city.Player.GameObject.transform))) continue;
                return foot + Vector3.up * EyeHeight;
            }
            Assert.Fail($"No grounded camera with body clearance at {toe} facing {outward}, setback {distance}.");
            return default;
        }

        private static void AssertRockfaceProjectsFromSlope(Transform owner, CityMountainRidgeStation left,
            CityMountainRidgeStation right, float reportedX)
        {
            var colliders = owner.GetComponentsInChildren<MeshCollider>();
            Vector3[] leftCross = CityMountainBoundaryMeshFactory.CreateCrossSection(left);
            Vector3[] rightCross = CityMountainBoundaryMeshFactory.CreateCrossSection(right);
            float minRelief = float.PositiveInfinity, maxRelief = float.NegativeInfinity;
            for (int column = -4; column <= 4; column++)
            {
                float x = reportedX + column * .55f;
                float along = Mathf.InverseLerp(left.Toe.x, right.Toe.x, x);
                Vector3 toe = Vector3.Lerp(left.Toe, right.Toe, along);
                for (int row = 0; row < 6; row++)
                {
                    const float setback = 4f;
                    float y = toe.y + 1f + row * 1.15f;
                    Vector3 shoulder = Vector3.Lerp(leftCross[3], rightCross[3], along);
                    int band = y < shoulder.y ? 2 : 3;
                    Vector3 lower = Vector3.Lerp(leftCross[band], rightCross[band], along);
                    Vector3 upper = Vector3.Lerp(leftCross[band + 1], rightCross[band + 1], along);
                    float unbrokenZ = Mathf.Lerp(lower.z, upper.z, Mathf.InverseLerp(lower.y, upper.y, y));
                    var ray = new Ray(new Vector3(x, y, toe.z + setback), Vector3.back);
                    float nearest = float.PositiveInfinity;
                    foreach (var collider in colliders)
                        if (collider.Raycast(ray, out RaycastHit hit, 30f)) nearest = Mathf.Min(nearest, hit.distance);
                    Assert.That(float.IsInfinity(nearest), Is.False, $"Rock face has an opening near ({x}, {y}).");
                    float actualZ = ray.GetPoint(nearest).z;
                    Assert.That(actualZ, Is.LessThanOrEqualTo(toe.z + .02f), "Relief entered walking space.");
                    minRelief = Mathf.Min(minRelief, actualZ - unbrokenZ);
                    maxRelief = Mathf.Max(maxRelief, actualZ - unbrokenZ);
                }
            }
            Assert.That(maxRelief, Is.GreaterThan(.10f), "The reported wall must stand proud of its old flat face.");
            Assert.That(maxRelief - minRelief, Is.GreaterThan(.10f), "A uniformly displaced flat wall has no rock relief.");
        }
    }
}

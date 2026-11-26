using System;
using System.Collections;
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
        [Explicit("Render the reported City rim with a live local fog field.")]
        public IEnumerator CityRimFogVisibility() => CaptureCityLocalAcceptance(ReviewRimFogVisibility);

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
                string name = outward == Vector3.left ? "west" : "south";
                Vector3 eye = foot - outward * 3f + Vector3.up * 1.9f;
                PoseLocalAcceptanceCamera(city, eye, foot + outward * 22f + Vector3.up * 6f, 70f);
                yield return null;
                const float MaximumRockDistance = 12f;
                var sightline = new Ray(eye, city.Camera.transform.forward);
                Assert.That(rock.GetComponentsInChildren<MeshCollider>()
                    .Any(body => body.Raycast(sightline, out _, MaximumRockDistance)), Is.True,
                    "The camera must aim at a nearby physical wall.");

                string frame = "rim-fog-" + name;
                CaptureCurrentCamera(city.Camera, SceneIds.City, frame, 640, 360);
                try
                {
                    mist.FogRenderer.forceRenderingOff = true;
                    CaptureCurrentCamera(city.Camera, SceneIds.City, frame + "-clear", 640, 360);
                    mist.FogRenderer.forceRenderingOff = false;
                    foreach (var face in faces) face.forceRenderingOff = true;
                    CaptureCurrentCamera(city.Camera, SceneIds.City, frame + "-hidden", 640, 360);
                }
                finally
                {
                    mist.FogRenderer.forceRenderingOff = false;
                    foreach (var face in faces) face.forceRenderingOff = false;
                }
                float contrast = RimDifference(frame, frame + "-hidden");
                Assert.That(contrast, Is.GreaterThan(18f), "Local fog erased the visible rock face.");
                Assert.That(RimDifference(frame, frame + "-clear"), Is.LessThan(contrast * .20f),
                    "Dense fog washed out the near rock.");
            }
        }

        private static float RimDifference(string first, string second)
        {
            var image = new Texture2D(2, 2, TextureFormat.RGB24, false);
            try
            {
                Color32[] a = Read(first), b = Read(second);
                float difference = 0f;
                const int sampleWidth = 48, sampleHeight = 32;
                for (int y = 180 - sampleHeight / 2; y < 180 + sampleHeight / 2; y++)
                for (int x = 320 - sampleWidth / 2; x < 320 + sampleWidth / 2; x++)
                    difference += Mathf.Abs(a[y * 640 + x].g - b[y * 640 + x].g);
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

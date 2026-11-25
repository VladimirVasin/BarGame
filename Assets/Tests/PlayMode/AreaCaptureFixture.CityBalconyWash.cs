using System.Collections;
using System.Linq;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

namespace BarPromenade.Tests.PlayMode
{
    public sealed partial class AreaCaptureFixture
    {
        [UnityTest, Explicit("Balcony knots, ground views and reversal/settling of the actual imported fabric.")]
        public IEnumerator CityBalconyWash() => CaptureCityLocalAcceptance(ReviewBalconyWash);

        private static IEnumerator ReviewBalconyWash(CityGameRoot city)
        {
            var lines = CityBalconyWashPlan.ForLayout(city.Layout);
            var district = new CityDistrictStreamingPlan(city.Layout);
            var lower = lines.Where(line => district.IsLower(line.Start)).ToArray();
            var ridge = lines.Where(line => district.IsRidge(line.Start)).ToArray();
            Debug.Log($"Balcony wash distribution: {lower.Length} lower, {ridge.Length} Ridge, " +
                $"{lines.Count(line => line.ToPole)} to poles, {lines.Sum(line => line.Count)} garments.");
            foreach (var line in lines)
                Debug.Log($"Balcony wash placement: {line.Id}, {line.Start:F2} to {line.End:F2}, {line.Length:F2} m, {line.Count} garments");
            Assert.That(lines.Count, Is.InRange(8, CityBalconyWashPlan.MaximumLines));
            Assert.That(lower.Length, Is.GreaterThanOrEqualTo(3));
            Assert.That(ridge.Length, Is.GreaterThanOrEqualTo(3));
            Assert.That(lines.Count(s => s.ToPole), Is.GreaterThanOrEqualTo(2));
            Assert.That(lines.Count(s => !s.ToPole), Is.GreaterThanOrEqualTo(2));
            Assert.That(lines.Sum(line => line.Count), Is.LessThanOrEqualTo(CityBalconyWashPlan.MaximumGarments));
            Assert.That(city.World.WindDressingPlan.GetClothCount(CityWindDressingKind.BalconyWash), Is.EqualTo(lines.Sum(line => line.Count)));
            Assert.That(lines.Select(l => l.Count).Distinct().Count(), Is.GreaterThan(1), "Short and long lines must carry different loads.");
            var breeze = new WindSample(45f, .32f);
            float previousStrength = CityWindField.Gust(breeze, 0f).Strength01;
            float minStrength = previousStrength, maxStrength = previousStrength;
            for (int frame = 1; frame <= 360; frame++)
            {
                float strength = CityWindField.Gust(breeze, frame / 60f).Strength01;
                Assert.That(Mathf.Abs(strength - previousStrength), Is.LessThan(.015f), "The common gust must stay continuous.");
                minStrength = Mathf.Min(minStrength, strength);
                maxStrength = Mathf.Max(maxStrength, strength);
                previousStrength = strength;
            }
            Assert.That(maxStrength - minStrength, Is.GreaterThan(.35f), "The shared gust needs a readable lull and crest.");
            for (int i = 0; i < lines.Count; i++)
            for (int j = 0; j < i; j++)
            {
                var gap = Vector3.ProjectOnPlane(lines[i].Center - lines[j].Center, Vector3.up);
                Assert.That(gap.magnitude, Is.GreaterThanOrEqualTo(CityBalconyWashPlan.Spacing - .001f));
            }
            var baked = new Mesh();
            bool weather = city.Weather.enabled;
            var previousGust = city.Weather.CurrentGust;
            var target = new RenderTexture(320, 180, 24);
            var previousTarget = city.Camera.targetTexture;
            target.Create();
            try
            {
                city.Weather.enabled = false;
                city.Camera.targetTexture = target;
                var views = new System.Collections.Generic.HashSet<(bool Ridge, bool Pole)>();
                var checkedFields = new System.Collections.Generic.HashSet<CityWindField>();
                foreach (var line in lower.Concat(ridge))
                {
                    Assert.That(line.Length, Is.InRange(3f, CityBalconyWashPlan.MaximumLength));
                    Assert.That(Mathf.Abs(line.Start.y - line.End.y), Is.LessThanOrEqualTo(1f));
                    Vector3 aim = line.Point(.5f) - Vector3.up * .32f;
                    city.Player.Motor.Teleport(aim - Vector3.up * 2f);
                    Transform root = null;
                    for (int i = 0; i < 90 && root == null; i++)
                    {
                        yield return null;
                        root = GameObject.Find(line.Id)?.transform;
                    }
                    Assert.That(root, Is.Not.Null, line.Id);
                    var fabric = root.GetComponentsInChildren<SkinnedMeshRenderer>();
                    Assert.That(fabric.Length, Is.EqualTo(line.Count));
                    Assert.That(root.Cast<Transform>().Count(p => p.name == "Peg"), Is.EqualTo(line.Count * 2));
                    foreach (var renderer in fabric)
                    {
                        var coefficients = renderer.GetComponent<Cloth>().coefficients;
                        Assert.That(coefficients.Count(p => p.maxDistance == 0f), Is.EqualTo(2));
                        Assert.That(coefficients.Max(p => p.maxDistance), Is.LessThanOrEqualTo(line.ClothTravel));
                        Assert.That(renderer.sharedMesh.bounds.size.x, Is.InRange(.55f, 1.30f));
                    }
                    var rope = root.Find("Clothesline").GetComponent<MeshFilter>();
                    var vertices = rope.sharedMesh.vertices;
                    foreach (Vector3 knot in new[] { line.Start, line.End })
                        Assert.That(vertices.Min(p => Vector3.Distance(rope.transform.TransformPoint(p), knot)), Is.LessThan(.025f));
                    var view = (Ridge: district.IsRidge(line.Start), Pole: line.ToPole);
                    if (views.Contains(view) || !TryWashStreetView(aim, line.End - line.Start, city, out Vector3 eye)) continue;
                    views.Add(view);
                    city.Player.Motor.Teleport(eye - Vector3.up * EyeHeight);
                    city.Camera.transform.SetPositionAndRotation(eye, Quaternion.LookRotation(aim - eye));
                    city.Camera.fieldOfView = 55f;
                    city.Visibility.Refresh(true);
                    CityWindField field = city.World.Root.GetComponent<CityWindField>();
                    Assert.That(field, Is.Not.Null);
                    if (checkedFields.Add(field))
                    {
                        city.Weather.ApplyCurrentWeather();
                        yield return null;
                        Assert.That(field.Wind.z, Is.EqualTo(city.Weather.CurrentGust.Strength01).Within(.001f));
                        Assert.That(Shader.GetGlobalVector("_CityWind"), Is.EqualTo(field.Wind));
                    }
                    var sheet = fabric.FirstOrDefault(r => r.name == "Sheet") ?? fabric.First(r => r.name == "Shirt");
                    var rest = sheet.sharedMesh.vertices;
                    Vector3 axis = Vector3.ProjectOnPlane(sheet.transform.forward, Vector3.up).normalized;
                    float yaw = Mathf.Atan2(axis.x, axis.z) * Mathf.Rad2Deg;
                    var offsets = new float[4];
                    for (int phase = 0; phase < offsets.Length; phase++)
                    {
                        var wind = new WindSample(yaw + (phase == 2 ? 180f : 0f), phase == 3 ? 0f : .32f);
                        if (phase == 0) CityClothWindRegistry.SetWind(wind);
                        else CityClothWindRegistry.SetCityWind(CityWindField.Gust(wind, .95f));
                        for (int frame = 0; frame < 26; frame++)
                        {
                            city.Camera.transform.SetPositionAndRotation(eye, Quaternion.LookRotation(aim - eye));
                            yield return null;
                        }
                        sheet.BakeMesh(baked);
                        var moved = baked.vertices;
                        int free = 0;
                        for (int i = 0; i < moved.Length; i++)
                        {
                            if (rest[i].y > -.003f)
                                Assert.That(Vector3.Distance(rest[i], moved[i]), Is.LessThan(.012f), "A peg slipped.");
                            if (rest[i].y > -.58f) continue;
                            offsets[phase] += Vector3.Dot(sheet.transform.TransformVector(moved[i] - rest[i]), axis);
                            free++;
                        }
                        offsets[phase] /= free;
                        if (phase <= 1)
                        {
                            Shader.SetGlobalVector("_CityWind", phase == 1 ? field.Pack(wind, .95f) : Vector4.zero);
                            CaptureCurrentCamera(city.Camera, SceneIds.City, $"city-wash-wind-{views.Count}-{phase}");
                        }
                    }
                    Debug.Log($"Balcony wash hem, {line.Id}: old {offsets[0]:F3}, gust {offsets[1]:F3}, reverse {offsets[2]:F3}, calm {offsets[3]:F3} m");
                    Assert.That(offsets[1], Is.GreaterThan(.20f), line.Id + " forward gust");
                    Assert.That(offsets[1] - offsets[0], Is.GreaterThan(.10f), line.Id + " stronger than the old wind");
                    Assert.That(offsets[2], Is.LessThan(-.18f), line.Id + " reverse gust");
                    Assert.That(Mathf.Abs(offsets[3]), Is.LessThan(.12f), line.Id + " returns towards calm");
                }
                Assert.That(views.Any(view => view.Ridge), Is.True, "No Ridge line was visible from the ground.");
                Assert.That(views.Any(view => !view.Ridge), Is.True, "No lower city line was visible from the ground.");
                Assert.That(views.Any(view => view.Pole) && views.Any(view => !view.Pole), Is.True,
                    "Both types of receiver must read from street level.");
            }
            finally
            {
                city.Weather.enabled = weather;
                CityClothWindRegistry.SetCityWind(previousGust);
                city.Camera.targetTexture = previousTarget;
                target.Release();
                Object.DestroyImmediate(target);
                Object.DestroyImmediate(baked);
            }
        }

        private static bool TryWashStreetView(Vector3 aim, Vector3 along, CityGameRoot city, out Vector3 eye)
        {
            Vector3 side = Vector3.Cross(along, Vector3.up).normalized;
            for (int i = 0; i < 16; i++)
            {
                Vector3 from = aim + Quaternion.Euler(0f, (i % 2 == 0 ? 0f : 180f) + i / 2 * 18f, 0f) * side * 6.3f;
                Vector2 xz = new(from.x, from.z);
                if (!CityHillsideGroundPlan.ForLayout(city.Layout).TrySampleTop(xz, out float floor) &&
                    !CityTerrainSurfacePlan.TrySampleGroundTop(city.Layout, xz, out floor, out _)) continue;
                if (CityStreetSupportPlan.ForLayout(city.Layout).TrySampleTop(xz, out float paving, out _)) floor = Mathf.Max(floor, paving);
                from.y = floor + EyeHeight;
                if (!Physics.Linecast(from, aim, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore) &&
                    !Physics.Linecast(from, aim + along * .45f, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore) &&
                    !Physics.Linecast(from, aim - along * .45f, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore))
                {
                    eye = from;
                    return true;
                }
            }
            eye = default;
            return false;
        }
    }
}

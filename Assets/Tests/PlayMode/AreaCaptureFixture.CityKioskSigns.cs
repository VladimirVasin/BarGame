using System;
using System.Collections;
using System.Collections.Generic;
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
        [UnityTest]
        [Timeout(240000)]
        [Explicit("The three existing City kiosks: matte physical signs, local projectors and six day/night frames.")]
        public IEnumerator CityKioskSigns()
        {
            GameSessionState.BeginNewGame();
            Assert.That(GameSessionState.TryStartGameTimeAt(720), Is.True);
            var load = SceneManager.LoadSceneAsync(SceneIds.City, LoadSceneMode.Single);
            float deadline = Time.realtimeSinceStartup + 120f;
            CityGameRoot city = null;
            while (Time.realtimeSinceStartup < deadline)
            {
                city = Object.FindAnyObjectByType<CityGameRoot>();
                if (load.isDone && city && city.IsInitialized && !CompositionDriver.IsComposing) break;
                yield return null;
            }
            Assert.That(city && city.IsInitialized && !CompositionDriver.IsComposing, Is.True);
            var sites = CityKioskSites.ForLayout(city.Layout).Sites;
            Assert.That(sites.Count, Is.EqualTo(3), "The sign update must preserve all three existing placements.");
            var boardMaterial = Resources.Load<Material>("Pawnshop/Materials/PawnshopMatteSign");
            var letterMaterial = Resources.Load<Material>("Pawnshop/Materials/PawnshopMatteLetters");
            Assert.That(boardMaterial && letterMaterial, Is.True);
            var manifest = Resources.Load<TextAsset>("RidgeKiosk/RidgeKiosk");
            Assert.That(manifest, Is.Not.Null);
            var text = JsonUtility.FromJson<KioskSignsManifest>(manifest.text);
            Assert.That(text.sign_text, Is.EqualTo("КИОСК"));
            Assert.That(text.hours_text, Is.EqualTo("24/7"));
            foreach (var material in new[] { boardMaterial, letterMaterial })
            {
                Assert.That(material.GetFloat("_Smoothness"), Is.EqualTo(.03f).Within(.001f));
                Assert.That(material.GetFloat("_Metallic"), Is.Zero);
                Color emission = material.GetColor("_EmissionColor");
                Assert.That(Mathf.Max(emission.r, Mathf.Max(emission.g, emission.b)), Is.Zero,
                    "Kiosk letters and board are lit matte surfaces, without self illumination.");
            }
            var follow = city.Camera.GetComponent<PlayerCameraFollow>();
            bool followed = follow && follow.enabled, weatherEnabled = city.Weather.enabled;
            bool motorEnabled = city.Player.Motor.InputEnabled;
            Vector3 savedHero = city.Player.GameObject.transform.position;
            Pose savedCamera = new(city.Camera.transform.position, city.Camera.transform.rotation);
            float savedFov = city.Camera.fieldOfView;
            if (follow) follow.enabled = false;
            city.Weather.enabled = false;
            city.Player.Motor.SetInputEnabled(false);
            var posterTextures = new HashSet<Texture>();
            var neonControllers = new List<KioskHoursNeon>();
            bool capturedNeonWave = false;
            try
            {
                foreach (var site in sites)
                {
                    Transform kiosk = city.World.Root.transform.Find(site.IsRidge ? "Ridge Kiosk" : "Kiosk " + site.StableId);
                    Assert.That(kiosk, Is.Not.Null, site.StableId);
                    Assert.That(Vector3.Distance(kiosk.position, site.Origin), Is.LessThan(.001f));
                    Assert.That(Quaternion.Angle(kiosk.rotation, site.Rotation), Is.LessThan(.01f));
                    var parts = kiosk.GetComponentsInChildren<Transform>(true);
                    Transform source = parts.Single(part => part.name == "KioskSignSource");
                    Transform target = parts.Single(part => part.name == "KioskSignTarget");
                    Transform lamp = parts.Single(part => part.name == CityNightAtmosphere.IndustrialTaskLightPrefix + "KioskSign");
                    Assert.That(Vector3.Distance(source.position, kiosk.TransformPoint(new Vector3(0f, 2.672f, 2.055f))), Is.LessThan(.01f));
                    Assert.That(Vector3.Distance(target.position, kiosk.TransformPoint(new Vector3(0f, 2.432f, 1.756f))), Is.LessThan(.01f));
                    Assert.That(Vector3.Distance(lamp.position, source.position), Is.LessThan(.005f));
                    Assert.That(Vector3.Angle(lamp.forward, target.position - source.position), Is.LessThan(.1f));
                    var renderers = kiosk.GetComponentsInChildren<MeshRenderer>(true);
                    var board = renderers.Single(renderer => renderer.name == "RidgeKiosk_KioskSignBoard");
                    var letters = renderers.Single(renderer => renderer.name == "RidgeKiosk_KioskSignLetters");
                    Assert.That(board.sharedMaterial, Is.SameAs(boardMaterial));
                    Assert.That(letters.sharedMaterial, Is.SameAs(letterMaterial));
                    VerifyKioskHoursBoards(kiosk, board, letters, parts);
                    var neon = kiosk.GetComponent<KioskHoursNeon>();
                    Assert.That(neon, Is.Not.Null);
                    neonControllers.Add(neon);
                    neon.AutoAdvance = false;
                    Bounds neonWord = VerifyKioskHoursNeon(kiosk, neon);
                    var approach = parts.Single(part => part.name == "KioskApproach");
                    Assert.That(Vector3.Distance(approach.position, site.Approach), Is.LessThan(.01f),
                        "Adding the sign cannot move the existing customer approach.");
                    var poster = renderers.Single(renderer => renderer.name == "RidgeKiosk_KioskAdvertisement");
                    VerifyKioskWallPosters(city, site, kiosk, poster, letterMaterial, posterTextures);

                    float dayIntensity = 0f, dayNeon = 0f;
                    foreach (int hour in new[] { 12, 21 })
                    {
                        SetChurchRemodelTime(hour * 60);
                        city.DayNight.ApplyCurrentTime(true);
                        city.Player.Motor.Teleport(site.Approach + site.Rotation * new Vector3(-2f, PlayerFactory.GroundedRootOffset, .5f));
                        Physics.SyncTransforms();
                        city.Night.Atmosphere.RefreshImmediate();
                        neon.ApplyAt(0d);
                        for (int frame = 0; frame < 3; frame++) yield return null;
                        Light light = city.Night.Atmosphere.StreetLightPool.Single(candidate => candidate && candidate.enabled &&
                            Vector3.Distance(candidate.transform.position, source.position) < .025f);
                        Assert.That(light.type, Is.EqualTo(LightType.Spot));
                        Assert.That(light.range, Is.InRange(.5f, 1.61f));
                        Assert.That(light.spotAngle, Is.EqualTo(155f).Within(.1f));
                        Assert.That(light.innerSpotAngle, Is.EqualTo(125f).Within(.1f));
                        Assert.That(light.shadows, Is.EqualTo(LightShadows.None));
                        Assert.That(light.intensity, Is.GreaterThan(0f));
                        if (hour == 12) dayIntensity = light.intensity;
                        else Assert.That(dayIntensity, Is.GreaterThanOrEqualTo(light.intensity * 2f / 3f - .001f),
                            "The local sign light must retain the accepted daylight floor.");
                        float neonRed = KioskNeonColor(neon.Sectors[neon.LeadingSector]).r;
                        if (hour == 12) dayNeon = neonRed;
                        else Assert.That(dayNeon, Is.GreaterThanOrEqualTo(neonRed * 2f / 3f - .005f));
                        Vector3 eye = kiosk.TransformPoint(new Vector3(-2.7f, 1.6f, 4.3f));
                        // Native ordinary frames also update the seller's skin;
                        // a nested Camera.Render can otherwise reuse an older skin buffer.
                        yield return GlassReturnNativeFrame(city.Camera, eye, kiosk.TransformPoint(new Vector3(-.1f, 1.50f, .5f)),
                            "kiosk-sign-" + site.StableId + (hour == 12 ? "-day" : "-night"));
                        AssertKioskNeonPublicWidth(city.Camera, kiosk, neonWord);
                        if (hour == 12)
                            yield return GlassReturnNativeFrame(city.Camera, kiosk.TransformPoint(new Vector3(3.8f, 1.45f, .65f)),
                                kiosk.TransformPoint(new Vector3(1.52f, 1.43f, .65f)), "kiosk-side-poster-" + site.StableId);
                        else if (!capturedNeonWave)
                        {
                            capturedNeonWave = true;
                            neon.ApplyAt(1d);
                            yield return GlassReturnNativeFrame(city.Camera, kiosk.TransformPoint(new Vector3(-1.13f, 2.432f, 3.2f)),
                                kiosk.TransformPoint(new Vector3(-1.13f, 2.432f, 1.750f)), "kiosk-neon-wave-" + site.StableId);
                        }
                    }
                }
                Assert.That(posterTextures.Count, Is.EqualTo(3), "Each fixed kiosk receives its own product-led artwork.");
            }
            finally
            {
                foreach (var neon in neonControllers) if (neon) { neon.enabled = true; neon.AutoAdvance = true; }
                city.Player.Motor.Teleport(savedHero); Physics.SyncTransforms();
                city.Player.Motor.SetInputEnabled(motorEnabled);
                city.Weather.enabled = weatherEnabled;
                city.Camera.transform.SetPositionAndRotation(savedCamera.position, savedCamera.rotation);
                city.Camera.fieldOfView = savedFov;
                if (follow) follow.enabled = followed;
            }
        }

        private static void VerifyKioskWallPosters(CityGameRoot city, CityKioskSitePlan site, Transform kiosk,
            MeshRenderer poster, Material mattePaper, HashSet<Texture> textures)
        {
            string resource = site.StableId switch
            {
                "ridge-kiosk" => "KioskBeer",
                "industrial-kiosk" => "KioskVodka",
                "residential-kiosk" => "KioskPies",
                _ => throw new System.ArgumentOutOfRangeException(nameof(site.StableId))
            };
            var expected = Resources.Load<Texture2D>("RidgeKiosk/Posters/" + resource);
            Assert.That(expected, Is.Not.Null);
            Assert.That((float)expected.width / expected.height, Is.EqualTo(2f / 3f).Within(.005f),
                "Import must retain the portrait artwork's actual aspect ratio.");
            var properties = new MaterialPropertyBlock(); poster.GetPropertyBlock(properties);
            Texture actual = properties.GetTexture(Shader.PropertyToID("_BaseMap"));
            Assert.That(actual, Is.SameAs(expected), site.StableId + " keeps its stable product-led artwork on both walls.");
            Assert.That(textures.Add(actual), Is.True, "A district poster must not silently repeat another kiosk's art.");
            Assert.That(poster.sharedMaterial, Is.SameAs(mattePaper));
            Assert.That(poster.sharedMaterial.GetTextureScale("_BaseMap"), Is.EqualTo(Vector2.one));
            Assert.That(city.World.WalkableArea.Contains(site.Approach, .30f), Is.True,
                "Moving the print onto the walls leaves the original customer dock free.");
            Assert.That(kiosk.GetComponentsInChildren<BoxCollider>().Any(collider => collider.name == "Advertisement stand"),
                Is.False, "There must be no freestanding object or invisible stand collision on the apron.");

            Mesh mesh = poster.GetComponent<MeshFilter>().sharedMesh;
            Vector3[] vertices;
            Vector2[] uv;
#if UNITY_EDITOR
            using (Mesh.MeshDataArray data = UnityEditor.MeshUtility.AcquireReadOnlyMeshData(mesh))
            using (var positions = new Unity.Collections.NativeArray<Vector3>(data[0].vertexCount, Unity.Collections.Allocator.Temp))
            using (var coordinates = new Unity.Collections.NativeArray<Vector2>(data[0].vertexCount, Unity.Collections.Allocator.Temp))
            {
                data[0].GetVertices(positions); data[0].GetUVs(0, coordinates);
                vertices = positions.ToArray(); uv = coordinates.ToArray();
            }
#else
            vertices = mesh.vertices; uv = mesh.uv;
#endif
            Assert.That(uv.Length, Is.EqualTo(vertices.Length));
            Vector3 low = Vector3.positiveInfinity, high = Vector3.negativeInfinity;
            int leftVertices = 0, rightVertices = 0;
            for (int index = 0; index < vertices.Length; index++)
            {
                Vector3 point = kiosk.InverseTransformPoint(poster.transform.TransformPoint(vertices[index]));
                low = Vector3.Min(low, point); high = Vector3.Max(high, point);
                Assert.That(Mathf.Abs(point.x), Is.EqualTo(1.520f).Within(.005f),
                    "The full-scale paper is attached to each real side wall.");
                bool right = point.x > 0f;
                if (right) rightVertices++; else leftVertices++;
                float expectedU = right ? Mathf.InverseLerp(.26f, .98f, point.z) : Mathf.InverseLerp(.98f, .26f, point.z);
                Assert.That(uv[index].x, Is.EqualTo(expectedU).Within(.005f),
                    "Both outward-facing posters must read left-to-right.");
                Assert.That(uv[index].y, Is.EqualTo(Mathf.InverseLerp(.89f, 1.97f, point.y)).Within(.005f),
                    "Each wall receives one upright full portrait, without tiling or inversion.");
            }
            Assert.That(leftVertices, Is.GreaterThanOrEqualTo(4));
            Assert.That(rightVertices, Is.GreaterThanOrEqualTo(4));
            Assert.That(low.y, Is.EqualTo(.89f).Within(.005f));
            Assert.That(high.y, Is.EqualTo(1.97f).Within(.005f));
            Assert.That(low.z, Is.EqualTo(.26f).Within(.005f));
            Assert.That(high.z, Is.EqualTo(.98f).Within(.005f));
            Assert.That(uv.Min(point => point.x), Is.EqualTo(0f).Within(.001f));
            Assert.That(uv.Max(point => point.x), Is.EqualTo(1f).Within(.001f));
            Assert.That(uv.Min(point => point.y), Is.EqualTo(0f).Within(.001f));
            Assert.That(uv.Max(point => point.y), Is.EqualTo(1f).Within(.001f));
        }

        [Serializable]
        private sealed class KioskSignsManifest
        {
            public string sign_text, hours_text;
        }

        private static void VerifyKioskHoursBoards(Transform kiosk, MeshRenderer board, MeshRenderer letters, Transform[] parts)
        {
            Vector3[] boards = KioskLocalVertices(kiosk, board), glyphs = KioskLocalVertices(kiosk, letters);
            Bounds front = KioskIslandBounds(boards, point => point.x > -.77f);
            Assert.That(front.size.y, Is.EqualTo(.35f).Within(.005f));
            Assert.That(front.size.x, Is.EqualTo(1.52f).Within(.005f));
            Assert.That(Vector3.Distance(front.center, new Vector3(0f, 2.432f, 1.732f)), Is.LessThan(.01f));
            Assert.That(KioskIslandBounds(glyphs, point => point.y > 2.1f).size.y, Is.EqualTo(.242f).Within(.005f));
            Assert.That(glyphs.All(point => point.y > 2.1f), Is.True, "The matte cream lettering now belongs only to the front fascia.");
            Transform anchor = parts.Single(part => part.name == "KioskHoursFront");
            Assert.That(parts.Any(part => part.name == "KioskHoursLeft" || part.name == "KioskHoursRight"), Is.False);
            Assert.That(Vector3.Distance(kiosk.InverseTransformPoint(anchor.position),
                new Vector3(-1.13f, 2.432f, 1.750f)), Is.LessThan(.005f));
            Bounds plaque = KioskIslandBounds(boards, point => point.x < -.80f);
            Assert.That(Vector3.Distance(plaque.center, new Vector3(-1.13f, 2.432f, 1.750f)), Is.LessThan(.005f));
            Assert.That(plaque.size.x, Is.EqualTo(.620f).Within(.004f));
            Assert.That(plaque.size.y, Is.EqualTo(.300f).Within(.004f));
            Assert.That(plaque.size.z, Is.EqualTo(.036f).Within(.004f));
            Assert.That(front.min.x - plaque.max.x, Is.GreaterThan(.05f),
                "The larger front hours plate remains separate from the main kiosk name.");
        }

        private static Bounds VerifyKioskHoursNeon(Transform kiosk, KioskHoursNeon neon)
        {
            Assert.That(neon.Sectors.Count, Is.EqualTo(KioskHoursNeon.SectorCount));
            var allVertices = new List<Vector3>();
            for (int index = 0; index < KioskHoursNeon.SectorCount; index++)
            {
                Renderer sector = neon.Sectors[index];
                Assert.That(sector.name, Is.EqualTo("RidgeKiosk_KioskNeon" + index.ToString("00")));
                Assert.That(sector.sharedMaterial, Is.SameAs(CityNightResources.EmissiveMaterial));
                Assert.That(sector.shadowCastingMode, Is.EqualTo(UnityEngine.Rendering.ShadowCastingMode.Off));
                Vector3[] points = KioskLocalVertices(kiosk, (MeshRenderer)sector);
                allVertices.AddRange(points);
                Assert.That(points.All(point => point.x > -1.35f && point.x < -.907f &&
                    point.y > 2.335f && point.y < 2.53f && point.z > 1.769f && point.z < 1.783f), Is.True,
                    "Every physical sector belongs to the larger front-facing hours sign.");
            }
            Bounds word = KioskIslandBounds(allVertices.ToArray(), _ => true);
            Assert.That(word.size.y, Is.InRange(.180f, .195f));
            Assert.That(word.size.x, Is.InRange(.425f, .450f));
            neon.ApplyAt(0d);
            int head = neon.LeadingSector;
            Assert.That(neon.GetSectorPower(head), Is.EqualTo(1f).Within(.001f));
            for (int index = 0; index < KioskHoursNeon.SectorCount; index++)
                Assert.That(neon.GetSectorPower(index), Is.InRange(KioskHoursNeon.MinimumPower, 1f));
            Color pink = KioskNeonColor(neon.Sectors[head]);
            Assert.That(pink.r, Is.GreaterThan(1f));
            Assert.That(pink.r, Is.GreaterThan(pink.g * 4f));
            Assert.That(pink.b, Is.GreaterThan(pink.g * 4f));
            neon.AdvanceAt(.28d);
            Assert.That(neon.LeadingSector, Is.EqualTo((head + 1) % KioskHoursNeon.SectorCount));
            float pausedPhase = neon.PhaseSeconds;
            using (GameTimeScaleRuntime.AcquirePause()) neon.AdvanceAt(.56d);
            Assert.That(neon.PhaseSeconds, Is.EqualTo(pausedPhase));
            neon.ApplyAt(.73d);
            float[] powers = Enumerable.Range(0, KioskHoursNeon.SectorCount).Select(neon.GetSectorPower).ToArray();
            neon.ApplyAt(.73d + KioskHoursNeon.CycleSeconds);
            Assert.That(neon.PhaseSeconds, Is.EqualTo(.73f).Within(.001f));
            for (int index = 0; index < powers.Length; index++)
                Assert.That(neon.GetSectorPower(index), Is.EqualTo(powers[index]).Within(.001f));
            neon.enabled = false;
            try
            {
                neon.ApplyAt(1d);
                neon.AdvanceAt(1.3d);
                Assert.That(neon.Sectors.All(sector => KioskNeonColor(sector).maxColorComponent < .001f), Is.True,
                    "Neither forced seeking nor normal advance may relight a disabled neon owner.");
            }
            finally { neon.enabled = true; }
            Assert.That(neon.Sectors.Any(sector => KioskNeonColor(sector).r > .1f), Is.True,
                "Enabling restores the ordinary shared fixture exposure.");
            neon.ApplyAt(0d);
            return word;
        }

        private static void AssertKioskNeonPublicWidth(Camera camera, Transform kiosk, Bounds word)
        {
            float aspect = camera.aspect;
            camera.aspect = (float)Width / Height;
            try
            {
                float left = float.PositiveInfinity, right = float.NegativeInfinity;
                foreach (int x in new[] { -1, 1 })
                foreach (int y in new[] { -1, 1 })
                foreach (int z in new[] { -1, 1 })
                {
                    Vector3 corner = word.center + Vector3.Scale(word.extents, new Vector3(x, y, z));
                    Vector3 point = camera.WorldToViewportPoint(kiosk.TransformPoint(corner));
                    Assert.That(point.z > 0f && point.x > 0f && point.x < 1f && point.y > 0f && point.y < 1f, Is.True);
                    left = Mathf.Min(left, point.x); right = Mathf.Max(right, point.x);
                }
                float pixels = (right - left) * Width;
                Debug.Log("Kiosk front24/7 public-view width " + pixels.ToString("F1") + "px");
                Assert.That(pixels, Is.GreaterThanOrEqualTo(60f),
                    "The real neon word must be readable in the native public view at a1.6m camera height.");
            }
            finally { camera.aspect = aspect; }
        }

        private static Color KioskNeonColor(Renderer renderer)
        {
            var properties = new MaterialPropertyBlock(); renderer.GetPropertyBlock(properties);
            Color color = properties.GetColor(Shader.PropertyToID("_BaseColor"));
            color.a = 0f;
            return color;
        }

        private static Bounds KioskIslandBounds(Vector3[] vertices, Func<Vector3, bool> select)
        {
            Vector3[] island = vertices.Where(select).ToArray();
            Assert.That(island, Is.Not.Empty);
            var bounds = new Bounds(island[0], Vector3.zero);
            foreach (Vector3 point in island) bounds.Encapsulate(point);
            return bounds;
        }

        private static Vector3[] KioskLocalVertices(Transform kiosk, MeshRenderer renderer)
        {
            Mesh mesh = renderer.GetComponent<MeshFilter>().sharedMesh;
            Vector3[] positions;
#if UNITY_EDITOR
            using (Mesh.MeshDataArray data = UnityEditor.MeshUtility.AcquireReadOnlyMeshData(mesh))
            using (var vertices = new Unity.Collections.NativeArray<Vector3>(data[0].vertexCount, Unity.Collections.Allocator.Temp))
            {
                data[0].GetVertices(vertices); positions = vertices.ToArray();
            }
#else
            positions = mesh.vertices;
#endif
            for (int index = 0; index < positions.Length; index++)
                positions[index] = kiosk.InverseTransformPoint(renderer.transform.TransformPoint(positions[index]));
            return positions;
        }
    }
}

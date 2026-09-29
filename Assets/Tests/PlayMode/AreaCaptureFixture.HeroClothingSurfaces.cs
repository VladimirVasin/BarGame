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
        [UnityTest, Timeout(120000)]
        [Explicit("Focused hero cloth, skin and hair surface acceptance through the production Home camera. Run alone.")]
        [PrebuildSetup(typeof(PlayerRecoveryAssetsSetup))]
        public IEnumerator HeroCharacterSurfaces()
        {
            HomeInteriorRoot home = null;
            Player3DCharacterPresentation presentation = null;
            PlayerAttentionController attention = null;
            bool attentionWasEnabled = false;
            GameObject lighting = null;
            float previousDelta = Time.captureDeltaTime;
            var tangentIssues = new HashSet<string>();
            try
            {
                GameSessionState.BeginNewGame();
                Assert.That(GameSessionState.TryStartGameTimeFromWake(), Is.True);
                Time.captureDeltaTime = 1f / 60f;
                yield return LoadScarfScene<HomeInteriorRoot>(SceneIds.HomeInterior,
                    value => value.IsInitialized, value => home = value);
                home.CameraFollow.enabled = false;
                home.FixedCamera.enabled = false;
                home.Player.Motor.enabled = false;
                home.Player.Motor.Teleport(Vector3.up * 50f);
                Transform hero = home.Player.GameObject.transform;
                hero.rotation = Quaternion.identity;
                presentation = (Player3DCharacterPresentation)home.Player.Visual;
                attention = home.Player.GameObject.GetComponent<PlayerAttentionController>();
                attentionWasEnabled = attention != null && attention.enabled;
                if (attention != null) attention.enabled = false;
                presentation.SetAttentionFocus(null);

                // Keep the real scene camera, grade and PS1 composite. A raking
                // key light makes cloth, skin and hair response inspectable.
                lighting = new GameObject("Temporary hero surface inspection lights");
                foreach (var lamp in new[] {
                    (new Vector3(28f, -55f, 0f), 1.6f),
                    (new Vector3(12f, 145f, 0f), .55f) })
                {
                    var lampObject = new GameObject("Hero surface inspection light");
                    lampObject.transform.SetParent(lighting.transform, false);
                    lampObject.transform.rotation = Quaternion.Euler(lamp.Item1);
                    Light light = lampObject.AddComponent<Light>();
                    light.type = LightType.Directional;
                    light.intensity = lamp.Item2;
                    light.color = new Color(1f, .93f, .86f);
                }

                Camera camera = Camera.main;
                Assert.That(camera, Is.Not.Null);
                Player3DAssetRegistry registry = presentation.Registry;
                AssertHeroSurfaceMaterialBindings(registry);
                AssertHeroSurfacePaletteMerge(registry);
                AssertHeroSeatedSurfaceMaterials(home, registry, tangentIssues);
                AssertHeroFirstPersonSurfaceCopies(registry);
                yield return ScarfFrames(12);

                foreach (string pose in new[] { "Idle", "Walk" })
                {
                    Assert.That(presentation.TryBeginClip(pose), Is.True);
                    presentation.SampleActiveClip(pose == "Walk" ? .2f : .35f);
                    foreach (var shot in new[] {
                        ("full", new Vector3(1.8f, 1.05f, 2.8f), .9f),
                        ("jacket", new Vector3(.48f, 1.25f, 1.05f), 1.22f),
                        ("trousers", new Vector3(.52f, .64f, 1.1f), .65f),
                        ("boots", new Vector3(.48f, .28f, .88f), .17f),
                        ("hair", new Vector3(.55f, 1.7f, .85f), 1.58f) })
                    {
                        AimHeroAppearance(camera, hero, shot.Item2, shot.Item3);
                        yield return ScarfFrames(2);
                        yield return CaptureHeroAppearanceFrame(camera,
                            "surfaces-" + pose.ToLowerInvariant() + "-" + shot.Item1,
                            () => CheckHeroCharacterTangents(registry, tangentIssues));
                    }
                    Bounds hand = registry.MeshBindings.Single(value => value.MeshName == "GEO_Hand.R").Renderer.bounds;
                    Vector3 handCamera = hand.center + hero.TransformVector(new Vector3(-.28f, .1f, .43f));
                    camera.transform.SetPositionAndRotation(handCamera, Quaternion.LookRotation(hand.center - handCamera));
                    camera.fieldOfView = 34f;
                    yield return ScarfFrames(2);
                    yield return CaptureHeroAppearanceFrame(camera, "surfaces-" + pose.ToLowerInvariant() + "-hand");

                    PlayerWardrobe wardrobe = registry.GetComponent<PlayerWardrobe>();
                    var visibility = registry.MeshBindings.ToDictionary(value => value.Renderer, value => value.Renderer.enabled);
                    using (wardrobe.Undress())
                    {
                        Assert.That(wardrobe.Garments.SelectMany(value => value.Renderers).All(value => !value.enabled), Is.True);
                        Assert.That(registry.MeshBindings.Single(value => value.MeshName == "GEO_Torso").Renderer.enabled, Is.True);
                        AimHeroAppearance(camera, hero, new Vector3(.55f, 1.32f, 1.1f), 1.32f);
                        yield return ScarfFrames(2);
                        yield return CaptureHeroAppearanceFrame(camera, "surfaces-" + pose.ToLowerInvariant() + "-skin",
                            () => CheckHeroCharacterTangents(registry, tangentIssues));
                        AssertHeroSurfaceMaterialBindings(registry);
                    }
                    foreach (var state in visibility)
                        Assert.That(state.Key.enabled, Is.EqualTo(state.Value), state.Key.name + " wardrobe lease restore");
                    presentation.EndClip();
                }

                lighting.SetActive(false);
                home.Player.Motor.Teleport(new Vector3(2.075f, .12f, 2.78f));
                home.FixedCamera.enabled = true;
                yield return ScarfFrames(12);
                Assert.That(home.BathroomMirror.IsActive, Is.True);
                yield return CaptureHeroAppearanceFrame(camera, "surfaces-mirror", () =>
                    AssertHeroSurfaceCopy(registry, home.BathroomMirror.Twin,
                        home.BathroomMirror.Twin.MeshBindings.Select(value => value.Renderer), "Mirror"));
                AssertHeroSurfaceMaterialBindings(registry);
                Assert.That(tangentIssues, Is.Empty, string.Join("\n", tangentIssues));
                Debug.Log("HERO_CHARACTER_SURFACES_ACCEPTED: shared cloth/skin/hair maps, UV/tangent bases, " +
                    "palette and wardrobe restoration, first-person/mirror/seated sharing and production PS1 frames.");
            }
            finally
            {
                presentation?.EndClip();
                if (attention != null) attention.enabled = attentionWasEnabled;
                if (lighting != null) Object.Destroy(lighting);
                Time.captureDeltaTime = previousDelta;
            }
            yield return SceneManager.LoadSceneAsync(SceneIds.MainMenu, LoadSceneMode.Single);
        }

        private static readonly string[] HeroSurfaceMaterials =
            { "Player3DV2Clothing", "Player3DV2Skin", "Player3DV2Hair" };
        private static readonly string[] HeroSurfaceMapStems =
            { "PlayerClothing", "PlayerSkin", "PlayerHair" };
        private static readonly float[] HeroSurfaceBumpScales = { .65f, .35f, .5f };
        private static readonly HashSet<string> HeroPaintedSkin = new HashSet<string>(StringComparer.Ordinal)
        {
            "GEO_Torso", "GEO_Pelvis", "GEO_Thigh.L", "GEO_Thigh.R",
            "GEO_Shin.L", "GEO_Shin.R", "GEO_Foot.L", "GEO_Foot.R"
        };

        private static int HeroSurfaceFamily(Player3DMeshBinding binding)
        {
            if (binding.PaletteMaterialName == "MAT_JacketAtlas" || binding.PaletteMaterialName == "MAT_JeansAtlas") return 0;
            if (binding.PaletteMaterialName == "MAT_Skin" || binding.PaletteMaterialName == "MAT_SkinShadow" ||
                binding.PaletteMaterialName == "MAT_SkinDark") return 1;
            return binding.Role == "hair" ? 2 : -1;
        }

        private static void AssertHeroSurfaceMaterialBindings(Player3DAssetRegistry registry)
        {
            var materials = new HashSet<Material>();
            for (int family = 0; family < HeroSurfaceMaterials.Length; family++)
            {
                Player3DMeshBinding[] bindings = registry.MeshBindings.Where(value => HeroSurfaceFamily(value) == family).ToArray();
                Assert.That(bindings, Is.Not.Empty, HeroSurfaceMaterials[family]);
                Material material = bindings[0].Renderer.sharedMaterial;
                Assert.That(materials.Add(material), Is.True, "Cloth, skin and hair must retain independent shared response.");
                Assert.That(material.name, Is.EqualTo(HeroSurfaceMaterials[family]));
                Assert.That(material.shader.name, Is.EqualTo("Bar Promenade/PS1 Lit"));
                Assert.That(material.GetTexture("_BumpMap")?.name, Is.EqualTo(HeroSurfaceMapStems[family] + "Normal"));
                Assert.That(material.GetTexture("_MetallicGlossMap")?.name, Is.EqualTo(HeroSurfaceMapStems[family] + "Response"));
                Assert.That(material.IsKeywordEnabled("_NORMALMAP"), Is.True);
                Assert.That(material.IsKeywordEnabled("_METALLICSPECGLOSSMAP"), Is.True);
                Assert.That(material.IsKeywordEnabled("_SPECULARHIGHLIGHTS_OFF"), Is.False);
                Assert.That(material.IsKeywordEnabled("_ENVIRONMENTREFLECTIONS_OFF"), Is.True);
                Assert.That(material.GetFloat("_Metallic"), Is.Zero);
                Assert.That(material.GetFloat("_Smoothness"), Is.EqualTo(1f));
                Assert.That(material.GetFloat("_BumpScale"), Is.EqualTo(HeroSurfaceBumpScales[family]).Within(.0001f));
                Assert.That(material.GetColor("_BaseColor"), Is.EqualTo(Color.white));
                if (family == 0)
                {
                    Assert.That(material.GetTexture("_BaseMap")?.name, Is.EqualTo("PlayerClothingAtlas"));
                    foreach (string prefix in new[] { "CLO_Jacket", "CLO_Trousers", "CLO_Boot" })
                        Assert.That(bindings.Any(binding => binding.MeshName.StartsWith(prefix, StringComparison.Ordinal)),
                            Is.True, prefix + " must retain the shared layered material.");
                }
                else
                {
                    Texture basis = material.GetTexture("_BaseMap");
                    Assert.That(basis == null || basis == Texture2D.whiteTexture, Is.True,
                        "Skin/hair retain the existing MPB palette or painted body atlas over a white shared base.");
                    Assert.That(bindings.Select(value => value.BaseColor).Distinct().Count(), Is.GreaterThan(1),
                        "The original " + HeroSurfaceMaterials[family] + " palette must retain its distinct tones.");
                }
                foreach (Player3DMeshBinding binding in bindings)
                {
                    Assert.That(binding.Renderer.sharedMaterials.All(value => value == material), Is.True,
                        binding.MeshName + " must not create an instance material.");
                    var properties = new MaterialPropertyBlock();
                    binding.Renderer.GetPropertyBlock(properties);
                    Color expected = family == 0 || HeroPaintedSkin.Contains(binding.MeshName) ? Color.white : binding.BaseColor;
                    AssertHeroSurfaceColor(properties.GetColor("_BaseColor"), expected, binding.MeshName + " retained palette");
                    Assert.That(properties.GetTexture("_BumpMap"), Is.Null, binding.MeshName + " shared normal");
                    Assert.That(properties.GetTexture("_MetallicGlossMap"), Is.Null, binding.MeshName + " shared response");
                    if (family != 1) continue;
                    if (HeroPaintedSkin.Contains(binding.MeshName))
                        Assert.That(properties.GetTexture("_BaseMap"), Is.SameAs(Player3DBathingAppearance.BareSkinAtlas), binding.MeshName);
                    else
                        Assert.That(properties.GetTexture("_BaseMap"), Is.Null, binding.MeshName + " must keep its original flat palette");
                }
            }

            Player3DMeshBinding face = registry.MeshBindings.Single(value => value.MeshName == "GEO_FaceSurface");
            Assert.That(materials.Contains(face.Renderer.sharedMaterial), Is.False, "Expression face remains separate.");
            Assert.That(face.Renderer.sharedMaterial.IsKeywordEnabled("_NORMALMAP"), Is.False);
            var faceProperties = new MaterialPropertyBlock();
            face.Renderer.GetPropertyBlock(faceProperties);
            Assert.That(faceProperties.GetTexture("_BaseMap"), Is.SameAs(registry.FaceAtlas.Texture));
            foreach (Player3DMeshBinding binding in registry.MeshBindings.Where(value => HeroSurfaceFamily(value) < 0))
            {
                Assert.That(binding.Renderer.sharedMaterials.All(value => !materials.Contains(value)), Is.True,
                    binding.MeshName + " must retain its ordinary material.");
                foreach (Material ordinary in binding.Renderer.sharedMaterials)
                {
                    Assert.That(ordinary.IsKeywordEnabled("_NORMALMAP"), Is.False, binding.MeshName);
                    Assert.That(ordinary.GetTexture("_MetallicGlossMap"), Is.Null, binding.MeshName);
                }
            }
        }

        private static void AssertHeroSurfacePaletteMerge(Player3DAssetRegistry registry)
        {
            foreach (Player3DMeshBinding binding in registry.MeshBindings.Where(value => HeroSurfaceFamily(value) >= 0)
                .GroupBy(value => (HeroSurfaceFamily(value), HeroPaintedSkin.Contains(value.MeshName))).Select(group => group.First()))
            {
                Renderer renderer = binding.Renderer;
                Material shared = renderer.sharedMaterial;
                var original = new MaterialPropertyBlock();
                var properties = new MaterialPropertyBlock();
                renderer.GetPropertyBlock(original);
                renderer.GetPropertyBlock(properties);
                int sentinel = Shader.PropertyToID("_HeroSurfaceCaptureSentinel");
                properties.SetFloat(sentinel, .375f);
                renderer.SetPropertyBlock(properties);
                try
                {
                    registry.ApplyPalette();
                    renderer.GetPropertyBlock(properties);
                    Assert.That(properties.GetFloat(sentinel), Is.EqualTo(.375f), binding.MeshName + " merge-safe palette");
                    Assert.That(renderer.sharedMaterial, Is.SameAs(shared));
                    AssertHeroSurfaceColor(properties.GetColor("_BaseColor"), original.GetColor("_BaseColor"),
                        binding.MeshName + " palette merge");
                    Assert.That(properties.GetTexture("_BaseMap"), Is.SameAs(original.GetTexture("_BaseMap")));
                }
                finally { renderer.SetPropertyBlock(original); }
            }
        }

        private static void CheckHeroCharacterTangents(Player3DAssetRegistry registry, ISet<string> issues)
        {
            Player3DMeshBinding[] surfaces = registry.MeshBindings.Where(value => HeroSurfaceFamily(value) >= 0).ToArray();
            foreach (Player3DMeshBinding binding in surfaces)
            {
                Assert.That(binding.Renderer, Is.InstanceOf<SkinnedMeshRenderer>(), binding.MeshName);
                CheckHeroSurfaceTangents(((SkinnedMeshRenderer)binding.Renderer).sharedMesh, binding.MeshName, issues);
            }
            PlayerJacketCloth cloth = registry.GetComponent<PlayerJacketCloth>();
            Assert.That(cloth, Is.Not.Null);
            bool inspectedDeformation = false;
            for (int index = 0; index < cloth.SurfaceCount; index++)
            {
                Mesh deformed = cloth.DeformedMesh(index);
                if (!surfaces.Any(binding => ((SkinnedMeshRenderer)binding.Renderer).sharedMesh == deformed)) continue;
                inspectedDeformation = true;
                Assert.That(deformed, Is.Not.SameAs(cloth.SourceMesh(index)));
                CheckHeroSurfaceTangents(cloth.SourceMesh(index), "Imported jacket " + index, issues);
                CheckHeroSurfaceTangents(deformed, "Deformed jacket " + index, issues);
            }
            Assert.That(inspectedDeformation, Is.True, "The rendered jacket must use its live deformable surface.");
        }

        private static void AssertHeroFirstPersonSurfaceCopies(Player3DAssetRegistry registry)
        {
            using (var arm = new HeroArmOwner(registry))
            {
                AssertHeroSurfaceCopy(registry, arm.Subset.Registry, arm.Subset.VisibleRenderers, "First-person");
                Assert.That(arm.Subset.VisibleRenderers.Any(renderer => renderer.sharedMaterial.name == HeroSurfaceMaterials[0]), Is.True);
                Assert.That(arm.Subset.VisibleRenderers.Any(renderer => renderer.sharedMaterial.name == HeroSurfaceMaterials[1]), Is.True);
            }
        }

        private static void AssertHeroSurfaceCopy(Player3DAssetRegistry source, Player3DAssetRegistry copy,
            IEnumerable<Renderer> inspected, string label)
        {
            Assert.That(copy, Is.Not.Null, label);
            var selected = new HashSet<Renderer>(inspected);
            var originals = source.MeshBindings.ToDictionary(value => value.MeshName, StringComparer.Ordinal);
            foreach (Player3DMeshBinding target in copy.MeshBindings.Where(value => selected.Contains(value.Renderer)))
            {
                Player3DMeshBinding original = originals[target.MeshName];
                Assert.That(target.Renderer.sharedMaterials, Is.EqualTo(original.Renderer.sharedMaterials),
                    label + " " + target.MeshName + " shares production materials");
                var sourceBlock = new MaterialPropertyBlock();
                var copyBlock = new MaterialPropertyBlock();
                original.Renderer.GetPropertyBlock(sourceBlock);
                target.Renderer.GetPropertyBlock(copyBlock);
                AssertHeroSurfaceColor(copyBlock.GetColor("_BaseColor"), sourceBlock.GetColor("_BaseColor"),
                    label + " " + target.MeshName + " palette");
                Assert.That(copyBlock.GetTexture("_BaseMap"), Is.SameAs(sourceBlock.GetTexture("_BaseMap")),
                    label + " " + target.MeshName + " albedo");
            }
        }

        private static void AssertHeroSurfaceColor(Color actual, Color expected, string label)
        {
            // Serialized palette values and native property blocks can differ
            // by a float ULP while representing the same authored colour.
            Assert.That(Vector4.Distance(actual, expected), Is.LessThan(.00001f),
                label + ": expected " + expected + ", actual " + actual);
        }

        private static void AssertHeroSeatedSurfaceMaterials(HomeInteriorRoot home,
            Player3DAssetRegistry registry, ISet<string> issues)
        {
            var owner = new GameObject("Hero surface seated material probe");
            HomeToiletSeatedAppearance seated = owner.AddComponent<HomeToiletSeatedAppearance>();
            try
            {
                seated.Initialize(home);
                Assert.That(seated.Prepare(), Is.True);
                Assert.That(seated.Begin(), Is.True);
                seated.Present(.5f);
                Assert.That(seated.Renderers, Is.Not.Empty);
                foreach (Renderer renderer in seated.Renderers)
                {
                    int family = renderer.name.StartsWith("Bare_", StringComparison.Ordinal) ? 1 : 0;
                    Material shared = registry.MeshBindings.First(value => HeroSurfaceFamily(value) == family).Renderer.sharedMaterial;
                    Assert.That(renderer.sharedMaterials.All(value => value == shared), Is.True,
                        renderer.name + " must reuse its production surface family");
                    CheckHeroSurfaceTangents(((SkinnedMeshRenderer)renderer).sharedMesh, "Seated " + renderer.name, issues);
                    if (family != 1) continue;
                    var block = new MaterialPropertyBlock();
                    renderer.GetPropertyBlock(block);
                    Assert.That(block.GetTexture("_BaseMap"), Is.SameAs(Player3DBathingAppearance.BareSkinAtlas));
                    Assert.That(block.GetColor("_BaseColor"), Is.EqualTo(Color.white));
                }
            }
            finally { seated.End(); Object.DestroyImmediate(owner); }
        }

        private static void CheckHeroSurfaceTangents(Mesh mesh, string label, ISet<string> issues)
        {
            Assert.That(mesh, Is.Not.Null, label);
            Vector4[] tangents = mesh.tangents;
            Vector3[] normals = mesh.normals;
            Vector2[] uv = mesh.uv;
            Assert.That(tangents.Length, Is.EqualTo(mesh.vertexCount), label);
            Assert.That(normals.Length, Is.EqualTo(mesh.vertexCount), label);
            Assert.That(uv.Length, Is.EqualTo(mesh.vertexCount), label + " UV0");
            bool hasArea = false;
            int[] triangles = mesh.triangles;
            for (int index = 0; index < triangles.Length; index += 3)
            {
                Vector2 first = uv[triangles[index + 1]] - uv[triangles[index]];
                Vector2 second = uv[triangles[index + 2]] - uv[triangles[index]];
                hasArea |= Mathf.Abs(first.x * second.y - first.y * second.x) > .00000001f;
            }
            Assert.That(hasArea, Is.True, label + " must have mapped UV area, not a collapsed palette coordinate");
            for (int index = 0; index < tangents.Length; index++)
            {
                Vector4 tangent = tangents[index];
                Vector3 direction = new Vector3(tangent.x, tangent.y, tangent.z);
                bool valid = float.IsFinite(tangent.x) && float.IsFinite(tangent.y) &&
                    float.IsFinite(tangent.z) && float.IsFinite(tangent.w) &&
                    float.IsFinite(normals[index].x) && float.IsFinite(normals[index].y) && float.IsFinite(normals[index].z) &&
                    Mathf.Abs(normals[index].sqrMagnitude - 1f) < .025f &&
                    Mathf.Abs(direction.sqrMagnitude - 1f) < .025f && Mathf.Abs(Mathf.Abs(tangent.w) - 1f) < .001f &&
                    float.IsFinite(uv[index].x) && float.IsFinite(uv[index].y) &&
                    uv[index].x >= -.0001f && uv[index].x <= 1.0001f &&
                    uv[index].y >= -.0001f && uv[index].y <= 1.0001f &&
                    Mathf.Abs(Vector3.Dot(normals[index].normalized, direction.normalized)) < .025f;
                if (!valid)
                {
                    issues.Add($"{label} vertex {index}: tangent {tangent:F4}, normal {normals[index]:F4}, UV {uv[index]:F4}");
                    break;
                }
            }
        }
    }
}

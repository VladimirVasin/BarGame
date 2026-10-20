using System;
using System.Collections;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;

namespace BarPromenade.Tests.PlayMode
{
    public sealed partial class AreaCaptureFixture
    {
        private sealed class CitySurfaceBinding
        {
            internal Renderer Renderer;
            internal int Slot, PropertyIndex;
            internal Material Material;
            internal Texture Albedo, Response, PreviousAlbedo;
            internal float ResponseStrength, NormalStrength;
            internal bool Ground;
        }

        private sealed class CitySurfaceAudit
        {
            internal readonly List<CitySurfaceBinding> Bindings = new List<CitySurfaceBinding>();
            internal readonly List<CitySurfaceBinding> Protected = new List<CitySurfaceBinding>();

            internal void SetResponse(bool enabled)
            {
                var block = new MaterialPropertyBlock();
                foreach (CitySurfaceBinding binding in Bindings)
                {
                    if (binding.Ground || binding.Renderer == null) continue;
                    CitySurfaceLibrary.ReadProperties(binding.Renderer, binding.PropertyIndex, block);
                    block.SetFloat("_SceneResponseStrength", enabled ? binding.ResponseStrength : 0f);
                    block.SetFloat("_SceneNormalStrength", enabled ? binding.NormalStrength : 0f);
                    WriteVillageSurfaceBlock(binding.Renderer, binding.PropertyIndex, block);
                }
            }

            internal void SetPigment(bool enabled)
            {
                var block = new MaterialPropertyBlock();
                foreach (CitySurfaceBinding binding in Bindings)
                {
                    if (binding.Renderer == null || binding.PreviousAlbedo == null) continue;
                    CitySurfaceLibrary.ReadProperties(binding.Renderer, binding.PropertyIndex, block);
                    block.SetTexture("_BaseMap", enabled ? binding.Albedo : binding.PreviousAlbedo);
                    WriteVillageSurfaceBlock(binding.Renderer, binding.PropertyIndex, block);
                }
            }

            internal void AssertRestored()
            {
                var block = new MaterialPropertyBlock();
                foreach (CitySurfaceBinding binding in Bindings)
                {
                    Assert.That(binding.Renderer, Is.Not.Null);
                    Assert.That(binding.Renderer.sharedMaterials[binding.Slot], Is.SameAs(binding.Material), binding.Renderer.name);
                    CitySurfaceLibrary.ReadProperties(binding.Renderer, binding.PropertyIndex, block);
                    Assert.That(VillageBoundTexture(block, binding.Material, "_BaseMap"), Is.SameAs(binding.Albedo), binding.Renderer.name);
                    Assert.That(VillageBoundTexture(block, binding.Material, binding.Ground ? "_GroundResponse" : "_SceneMaterialResponse"),
                        Is.SameAs(binding.Response), binding.Renderer.name);
                    if (binding.Ground) continue;
                    Assert.That(block.GetFloat("_SceneResponseStrength"), Is.EqualTo(binding.ResponseStrength), binding.Renderer.name);
                    Assert.That(block.GetFloat("_SceneNormalStrength"), Is.EqualTo(binding.NormalStrength), binding.Renderer.name);
                }
                foreach (CitySurfaceBinding binding in Protected)
                {
                    if (binding.Renderer == null) continue;
                    Assert.That(binding.Renderer.sharedMaterials[binding.Slot], Is.SameAs(binding.Material), binding.Renderer.name);
                    CitySurfaceLibrary.ReadProperties(binding.Renderer, binding.PropertyIndex, block);
                    Assert.That(VillageBoundTexture(block, binding.Material, "_BaseMap"), Is.SameAs(binding.Albedo), binding.Renderer.name);
                }
            }
        }

        private static CitySurfaceAudit AssertCitySurfaceContracts(CityGameRoot city)
        {
            CitySurfaceManifest manifest = CitySurfaceLibrary.Manifest;
            var registered = new Dictionary<Texture, CitySurfaceSheet>();
            var textures = new HashSet<Texture2D>();
            foreach (CitySurfaceSheet sheet in manifest.sheets)
            {
                Texture2D albedo = AssertVillageSurfaceTexture(sheet.albedo, sheet.size, true);
                Texture2D response = AssertVillageSurfaceTexture(sheet.response, sheet.response_size, false);
                registered.Add(albedo, sheet);
                textures.Add(albedo); textures.Add(response);
                Assert.That(albedo.wrapMode, Is.EqualTo(sheet.unique_uv ? TextureWrapMode.Clamp : TextureWrapMode.Repeat), sheet.family);
                Assert.That(response.wrapMode, Is.EqualTo(albedo.wrapMode), sheet.family);
                if (!string.IsNullOrEmpty(sheet.ground_response))
                {
                    Texture2D ground = Resources.Load<Texture2D>(sheet.ground_response);
                    Assert.That(ground, Is.Not.Null, sheet.family);
                    textures.Add(AssertVillageSurfaceTexture(sheet.ground_response, ground.width, false));
                }
            }
            long bytes = 0;
            foreach (Texture2D texture in textures)
                for (int mip = 0; mip < texture.mipmapCount; mip++)
                    bytes += (long)((Mathf.Max(1, texture.width >> mip) + 3) / 4) *
                        ((Mathf.Max(1, texture.height >> mip) + 3) / 4) * 16;
            Assert.That(bytes, Is.EqualTo(manifest.estimated_gpu_bytes), "The manifest must count each imported BC7 map and mip exactly once.");
            Assert.That(bytes, Is.LessThanOrEqualTo(manifest.gpu_budget_bytes));
            Assert.That(CitySurfaceLibrary.SharedMaterialCount, Is.InRange(1, 512));
            var audit = new CitySurfaceAudit();
            foreach (GameObject root in city.Scene.GetRootGameObjects()) CollectCitySurfaceBindings(root.transform, registered, audit);
            Assert.That(audit.Bindings, Is.Not.Empty);
            int groundCount = 0, uniqueCount = 0;
            var used = new HashSet<Texture>();
            foreach (CitySurfaceBinding binding in audit.Bindings)
            {
                used.Add(binding.Albedo);
                if (binding.Ground) groundCount++;
                if (registered[binding.Albedo].unique_uv) uniqueCount++;
            }
            Assert.That(groundCount, Is.GreaterThan(0), "Consolidated ground must use the refreshed packed ground maps.");
            Assert.That(uniqueCount, Is.GreaterThan(0), "The actual City bar facade must use its guarded unique sheet.");
            foreach (string required in new[] { "CityParkBarkAlbedo", "CityCemeteryGraniteAlbedo", "CityRiverQuayAlbedo",
                         "CitySeacoastPlankAlbedo", "CityFringeConcreteAlbedo" })
            {
                bool covered = false;
                foreach (KeyValuePair<Texture, CitySurfaceSheet> pair in registered)
                    if (!string.IsNullOrEmpty(pair.Value.source) && pair.Value.source.EndsWith("/" + required, StringComparison.Ordinal))
                        covered |= used.Contains(pair.Key);
                Assert.That(covered, Is.True, "Actual scene coverage: " + required);
            }
            Debug.Log("City surfaces: imported BC7 mip bytes=" + bytes + "; shared materials=" + CitySurfaceLibrary.SharedMaterialCount +
                "; ground slots=" + groundCount + "; unique facade slots=" + uniqueCount + "; used sheets=" + used.Count + ".");
            audit.AssertRestored();
            return audit;
        }

        private static void CollectCitySurfaceBindings(Transform root, Dictionary<Texture, CitySurfaceSheet> registered, CitySurfaceAudit audit)
        {
            var block = new MaterialPropertyBlock();
            foreach (Renderer renderer in root.GetComponentsInChildren<Renderer>(true))
            {
                Material[] materials = renderer.sharedMaterials;
                for (int slot = 0; slot < materials.Length; slot++)
                {
                    Material material = materials[slot];
                    if (material == null || !material.HasProperty("_BaseMap")) continue;
                    int index = CitySurfaceLibrary.PropertyIndex(renderer, materials.Length, slot, block);
                    CitySurfaceLibrary.ReadProperties(renderer, index, block);
                    Texture albedo = VillageBoundTexture(block, material, "_BaseMap");
                    bool owns = material.shader.name == CitySurfaceLibrary.ShaderName || material.shader.name == CitySurfaceLibrary.FoliageShaderName;
                    var binding = new CitySurfaceBinding { Renderer = renderer, Slot = slot, PropertyIndex = index, Material = material, Albedo = albedo };
                    bool character = renderer is SkinnedMeshRenderer || renderer.GetComponentInParent<Animator>(true) != null ||
                        renderer.GetComponentInParent<PlayerMotor>(true) != null || renderer.GetComponentInParent<VillageResidentPresentation>(true) != null;
                    if (character || material.renderQueue >= 2500 || material.IsKeywordEnabled("_EMISSION"))
                        Assert.That(owns, Is.False, renderer.name + " must retain its character/glass/emission owner.");
                    // Character accessories legitimately change materials with their live animation/smoking state.
                    // Their contract is exclusion from City rebinding, rather than a frozen gameplay presentation.
                    if (character) continue;
                    bool ground = material.HasProperty("_GroundResponse");
                    bool registeredAlbedo = albedo != null && registered.TryGetValue(albedo, out _);
                    if (!owns && !registeredAlbedo) { audit.Protected.Add(binding); continue; }
                    Assert.That(registeredAlbedo, Is.True, renderer.name + " uses a registered City albedo.");
                    Assert.That(owns || ground, Is.True, renderer.name + " uses the matching physical response shader.");
                    CitySurfaceSheet sheet = registered[albedo];
                    binding.Ground = ground;
                    binding.Response = VillageBoundTexture(block, material, ground ? "_GroundResponse" : "_SceneMaterialResponse");
                    Assert.That(binding.Response, Is.SameAs(Resources.Load<Texture2D>(ground ? sheet.ground_response : sheet.response)), renderer.name);
                    if (!string.IsNullOrEmpty(sheet.source)) binding.PreviousAlbedo = Resources.Load<Texture2D>(sheet.source);
                    if (!ground)
                    {
                        binding.ResponseStrength = block.GetFloat("_SceneResponseStrength");
                        binding.NormalStrength = block.GetFloat("_SceneNormalStrength");
                        Assert.That(binding.ResponseStrength, Is.GreaterThan(0f), renderer.name);
                        Assert.That(binding.NormalStrength, Is.GreaterThan(0f), renderer.name);
                    }
                    if (sheet.unique_uv)
                    {
                        Assert.That(block.GetVector("_BaseMap_ST"), Is.EqualTo(new Vector4(1, 1, 0, 0)), renderer.name);
                        Assert.That(block.GetColor("_BaseColor"), Is.EqualTo(Color.white), renderer.name);
                        Assert.That(renderer.TryGetComponent(out MeshFilter filter), Is.True, renderer.name);
                        Assert.That(filter.sharedMesh.HasVertexAttribute(UnityEngine.Rendering.VertexAttribute.TexCoord1), Is.True,
                            "Original facade UVs remain available in UV1; the authoring validator checks their exact values.");
                    }
                    audit.Bindings.Add(binding);
                }
            }
        }

        private static IEnumerator CaptureCitySurfacePairs(CityGameRoot city, CitySurfaceAudit audit, CityReflectionAcceptance reflections)
        {
            BuildingLot lot = FindOrdinaryFrontage(city.Layout, CityDistrictKind.Residential);
            Shot facade = FrameBuildingSurface("Residential masonry close", lot, FindBuiltPrototype(lot), true, 0f);
            Shot machinery = Shot.At("Cannery close", city.Cannery.Plan.World(new Vector3(-1.1f, 1.9f, -2f)),
                city.Cannery.Plan.World(new Vector3(-5f, 1.4f, -.35f)), 48f);
            yield return Pair("Masonry pigment", facade, audit.SetPigment);
            yield return Pair("Masonry response", facade, audit.SetResponse);
            yield return Pair("Machinery response", machinery, audit.SetResponse);
            audit.AssertRestored();

            IEnumerator Pair(string name, Shot pose, Action<bool> toggle)
            {
                MoveCityLightingObserver(city, pose);
                city.Cannery.RefreshPresentation();
                yield return null;
                RenderTexture previous = city.Camera.targetTexture;
                var target = new RenderTexture(Width, Height, 24);
                try
                {
                    city.Camera.targetTexture = target;
                    double deadline = 0d;
                    while (!CityReflectionsReady(city, ref deadline, reflections, name))
                    { city.Camera.Render(); yield return null; }
                    city.Lighting.PrepareForCamera(city.Camera);
                    toggle(false);
                    CaptureCurrentCamera(city.Camera, SceneIds.City, CityLightingFrame(name + " baseline"));
                    toggle(true);
                    CaptureCurrentCamera(city.Camera, SceneIds.City, CityLightingFrame(name + " enabled"));
                    Assert.That(CountCityLightingDifference(name + " baseline", name + " enabled"), Is.GreaterThan(24),
                        name + " must visibly affect the actual close receiver.");
                }
                finally
                {
                    toggle(true);
                    city.Camera.targetTexture = previous;
                    target.Release(); UnityEngine.Object.DestroyImmediate(target);
                }
            }
        }
    }
}

using System;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.Rendering;

namespace BarPromenade.Tests.PlayMode
{
    public sealed partial class AreaCaptureFixture
    {
        private sealed class RoadSurfaceBinding
        {
            internal Renderer Renderer;
            internal Material Material;
            internal int BlockIndex, MaterialIndex;
            internal Texture Albedo, Response;
            internal bool Physical;
            internal float ResponseStrength, NormalStrength;
        }

        private sealed class RoadProtectedSurface
        {
            internal Renderer Renderer;
            internal Material Material;
            internal int MaterialIndex;
            internal Texture Albedo;
        }

        private sealed class RoadSurfaceAudit
        {
            internal readonly List<RoadSurfaceBinding> Bindings = new List<RoadSurfaceBinding>();
            internal readonly List<RoadProtectedSurface> Protected = new List<RoadProtectedSurface>();

            internal void SetPigment(bool enabled)
            {
                var block = new MaterialPropertyBlock();
                foreach (RoadSurfaceBinding binding in Bindings)
                {
                    if (binding.Renderer == null) continue;
                    ReadRoadSurfaceBlock(binding.Renderer, binding.BlockIndex, block);
                    block.SetTexture("_BaseMap", enabled ? binding.Albedo : Texture2D.grayTexture);
                    WriteRoadSurfaceBlock(binding.Renderer, binding.BlockIndex, block);
                }
            }

            internal void SetResponse(bool enabled)
            {
                var block = new MaterialPropertyBlock();
                foreach (RoadSurfaceBinding binding in Bindings)
                {
                    if (binding.Renderer == null || !binding.Physical) continue;
                    ReadRoadSurfaceBlock(binding.Renderer, binding.BlockIndex, block);
                    block.SetFloat("_SceneResponseStrength", enabled ? binding.ResponseStrength : 0f);
                    block.SetFloat("_SceneNormalStrength", enabled ? binding.NormalStrength : 0f);
                    WriteRoadSurfaceBlock(binding.Renderer, binding.BlockIndex, block);
                }
            }

            internal void Restore() { SetPigment(true); SetResponse(true); }

            internal void AssertRestoredBindings()
            {
                var block = new MaterialPropertyBlock();
                foreach (RoadSurfaceBinding binding in Bindings)
                {
                    Assert.That(binding.Renderer, Is.Not.Null);
                    Assert.That(binding.Renderer.sharedMaterials[binding.MaterialIndex], Is.SameAs(binding.Material), binding.Renderer.name);
                    ReadRoadSurfaceBlock(binding.Renderer, binding.BlockIndex, block);
                    Assert.That(RoadBoundTexture(block, binding.Material, "_BaseMap"), Is.SameAs(binding.Albedo), binding.Renderer.name);
                    Assert.That(RoadBoundTexture(block, binding.Material, binding.Physical ? "_SceneMaterialResponse" : "_GroundResponse"),
                        Is.SameAs(binding.Response), binding.Renderer.name);
                    if (!binding.Physical) continue;
                    Assert.That(RoadBoundFloat(block, binding.Material, "_SceneResponseStrength"), Is.EqualTo(binding.ResponseStrength));
                    Assert.That(RoadBoundFloat(block, binding.Material, "_SceneNormalStrength"), Is.EqualTo(binding.NormalStrength));
                }
                foreach (RoadProtectedSurface surface in Protected)
                {
                    if (surface.Renderer == null) continue;
                    Material material = surface.Renderer.sharedMaterials[surface.MaterialIndex];
                    Assert.That(material, Is.SameAs(surface.Material), surface.Renderer.name);
                    ReadRoadSurfaceBlock(surface.Renderer, surface.MaterialIndex, block);
                    if (block.isEmpty) ReadRoadSurfaceBlock(surface.Renderer, -1, block);
                    Assert.That(RoadBoundTexture(block, material, "_BaseMap"), Is.SameAs(surface.Albedo), surface.Renderer.name);
                }
            }
        }

        private static RoadSurfaceAudit AssertRoadSurfaceContracts(MountainRoadRoot root)
        {
            TextAsset text = Resources.Load<TextAsset>(MountainRoadSurfaceLibrary.ManifestResourcePath);
            Assert.That(text, Is.Not.Null);
            MountainRoadSurfaceManifest manifest = MountainRoadSurfaceLibrary.ParseManifestOrThrow(text.text);
            Assert.That(manifest.gpu_budget_bytes, Is.EqualTo(80L * 1024 * 1024));
            var maps = new HashSet<Texture2D>();
            var families = new HashSet<string>(StringComparer.Ordinal);
            foreach (MountainRoadSurfaceSheet sheet in manifest.sheets)
            {
                Assert.That(families.Add(sheet.family), Is.True);
                Assert.That(sheet.meters_per_tile, Is.GreaterThan(0f));
                maps.Add(AssertRoadSurfaceTexture(sheet.albedo, sheet.size, true, true));
                maps.Add(AssertRoadSurfaceTexture(sheet.response, sheet.response_size, false, true));
                Assert.That(sheet.response_size, Is.EqualTo(sheet.size / 2));
                if (!string.IsNullOrEmpty(sheet.ground_response))
                    maps.Add(AssertRoadSurfaceTexture(sheet.ground_response, 512, false, true));
            }
            Assert.That(families, Is.EquivalentTo(Enum.GetNames(typeof(MountainRoadSurfaceKind))),
                "The refresh must retain every existing road family rather than silently merge palette identities.");
            foreach (MountainRoadSurfaceAdditionalMap map in manifest.additional_maps)
            {
                bool authored = map.resource.StartsWith(MountainRoadSurfaceLibrary.ResourceFolder, StringComparison.Ordinal) ||
                    map.resource == MountainRoadCafeSurfaceAppearance.UniqueAlbedoResourcePath ||
                    map.resource == MountainRoadCafeSurfaceAppearance.UniqueResponseResourcePath;
                maps.Add(AssertRoadSurfaceTexture(map.resource, map.size, map.sRGB, map.mipmaps, authored));
            }
            Texture2D uniqueAlbedo = AssertRoadSurfaceTexture(MountainRoadCafeSurfaceAppearance.UniqueAlbedoResourcePath, 4096, true, true);
            Texture2D uniqueResponse = AssertRoadSurfaceTexture(MountainRoadCafeSurfaceAppearance.UniqueResponseResourcePath, 2048, false, true);
            Assert.That(maps.Contains(uniqueAlbedo) && maps.Contains(uniqueResponse), Is.True,
                "The atlas and retained auxiliary maps belong to the same texture budget.");
            long bytes = 0;
            foreach (Texture2D texture in maps)
                for (int mip = 0; mip < texture.mipmapCount; mip++)
                    bytes += texture.format == TextureFormat.BC7 ?
                        (long)((Mathf.Max(1, texture.width >> mip) + 3) / 4) * ((Mathf.Max(1, texture.height >> mip) + 3) / 4) * 16 :
                        (long)Mathf.Max(1, texture.width >> mip) * Mathf.Max(1, texture.height >> mip) * 4;
            Assert.That(bytes, Is.LessThanOrEqualTo(80L * 1024 * 1024),
                "Imported BC7 blocks plus conservative RGBA32 accounting for retained auxiliary maps must fit the road budget.");
            Assert.That(manifest.estimated_gpu_bytes, Is.GreaterThanOrEqualTo(bytes).And.LessThanOrEqualTo(80L * 1024 * 1024),
                "The published upper bound must cover the shipped maps, including untouched donor formats.");

            var audit = new RoadSurfaceAudit();
            var block = new MaterialPropertyBlock();
            int ground = 0, foliage = 0;
            foreach (Renderer renderer in root.World.Root.GetComponentsInChildren<Renderer>(true))
            {
                Material[] materials = renderer.sharedMaterials;
                for (int index = 0; index < materials.Length; index++)
                {
                    Material material = materials[index];
                    if (material == null) continue;
                    ReadRoadSurfaceBlock(renderer, index, block);
                    int blockIndex = index;
                    if (block.isEmpty) { blockIndex = -1; ReadRoadSurfaceBlock(renderer, -1, block); }
                    Texture albedo = RoadBoundTexture(block, material, "_BaseMap");
                    bool physical = material.shader.name == MountainRoadSurfaceLibrary.ShaderName ||
                        material.shader.name == MountainRoadSurfaceLibrary.FoliageShaderName;
                    bool groundMap = material.HasProperty("_GroundResponse") && maps.Contains(albedo as Texture2D);
                    if (!physical && !groundMap) continue;
                    Assert.That(renderer is SkinnedMeshRenderer, Is.False, "Characters retain their authored material path.");
                    Texture response = RoadBoundTexture(block, material, physical ? "_SceneMaterialResponse" : "_GroundResponse");
                    Assert.That(maps.Contains(albedo as Texture2D), Is.True, renderer.name + " uses a declared local pigment map.");
                    Assert.That(maps.Contains(response as Texture2D), Is.True, renderer.name + " uses its own physical response format.");
                    if (groundMap)
                    {
                        ground++;
                        if (renderer.name == "Upper Snow")
                            foreach (string name in new[] { "_GroundSubstrateMap", "_GroundSubstrateResponse", "_GroundAsphaltMap", "_GroundAsphaltResponse" })
                                Assert.That(maps.Contains(RoadBoundTexture(block, material, name) as Texture2D), Is.True, name);
                        if (renderer.gameObject == root.World.RoadSurface)
                        {
                            Assert.That(RoadBoundFloat(block, material, "_GroundRoadCoordinates"), Is.GreaterThan(.5f));
                            Assert.That(renderer.GetComponent<MeshFilter>().sharedMesh.HasVertexAttribute(VertexAttribute.TexCoord3), Is.True);
                        }
                    }
                    if (material.shader.name == MountainRoadSurfaceLibrary.FoliageShaderName) foliage++;
                    audit.Bindings.Add(new RoadSurfaceBinding { Renderer = renderer, Material = material,
                        BlockIndex = blockIndex, MaterialIndex = index, Albedo = albedo, Response = response,
                        Physical = physical,
                        ResponseStrength = physical ? RoadBoundFloat(block, material, "_SceneResponseStrength") : 0f,
                        NormalStrength = physical ? RoadBoundFloat(block, material, "_SceneNormalStrength") : 0f });
                }
            }
            Assert.That(audit.Bindings, Is.Not.Empty);
            Assert.That(ground, Is.GreaterThan(0));
            Assert.That(foliage, Is.GreaterThan(0), "Real conifer crowns keep the wind variant.");
            AssertRoadSurfaceGroup(root.World.Cableway.StationRoot.transform, audit, "Cableway station");
            AssertRoadSurfaceGroup(root.World.PhysicalRoot.transform.Find("Authored Forest Misc"), audit, "Road furniture and forest misc");
            int unique = 0;
            foreach (MountainRoadCafePartBinding part in root.World.Cafe.Model.Parts)
            {
                Renderer renderer = part.Renderer;
                if (renderer == null) continue;
                ReadRoadSurfaceBlock(renderer, -1, block);
                Material material = renderer.sharedMaterial;
                if (part.Role == "menu_cover" || part.Role == "menu_pages")
                    Assert.That(MountainRoadCafeSurfaceAppearance.IsUnique(part), Is.False,
                        "The complete menu retains its legacy path, including the cover used to build its folding leaves.");
                if (MountainRoadCafeSurfaceAppearance.IsUnique(part))
                {
                    unique++;
                    Assert.That(material.shader.name, Is.EqualTo(MountainRoadSurfaceLibrary.ShaderName), part.SourceName);
                    Assert.That(RoadBoundTexture(block, material, "_BaseMap"), Is.SameAs(uniqueAlbedo), part.SourceName);
                    Assert.That(RoadBoundTexture(block, material, "_SceneMaterialResponse"), Is.SameAs(uniqueResponse), part.SourceName);
                    Assert.That(block.GetVector("_BaseMap_ST"), Is.EqualTo(new Vector4(1, 1, 0, 0)), part.SourceName);
                    Assert.That(block.GetColor("_BaseColor"), Is.EqualTo(Color.white), part.SourceName);
                    Mesh mesh = renderer.GetComponent<MeshFilter>().sharedMesh;
                    Assert.That(mesh.HasVertexAttribute(VertexAttribute.TexCoord0) && mesh.HasVertexAttribute(VertexAttribute.TexCoord1),
                        Is.True, part.SourceName + " retains original UV1 and receives unique UV0.");
                }
                else RememberProtected(renderer);
            }
            Assert.That(unique, Is.GreaterThan(0));
            Assert.That(root.CafeMenu, Is.Not.Null);
            Assert.That(root.CafeMenu.Presentation, Is.Not.Null);
            var menuPage = root.CafeMenu.Presentation.Page;
            Assert.That(menuPage, Is.Not.Null);
            Assert.That(menuPage.RestingPropRenderers, Is.Not.Empty,
                "Audit the separate runtime folding leaves as well as the authored menu meshes.");
            foreach (Renderer renderer in menuPage.PropRenderers) if (renderer != null) RememberProtected(renderer);
            foreach (Renderer renderer in menuPage.RestingPropRenderers) if (renderer != null) RememberProtected(renderer);
            foreach (Renderer renderer in menuPage.RestingHighlightRenderers) if (renderer != null) RememberProtected(renderer);
            Transform vehicleRoot = root.LastRouteCar != null ?
                root.LastRouteCar.GetComponentInParent<LastRouteCarDriver>()?.transform : null;
            foreach (Renderer renderer in root.GetComponentsInChildren<Renderer>(true))
                if (renderer is SkinnedMeshRenderer || renderer.transform.IsChildOf(root.World.Cafe.NpcRoot.transform) ||
                    renderer.transform.IsChildOf(root.Player.GameObject.transform) ||
                    vehicleRoot != null && renderer.transform.IsChildOf(vehicleRoot) ||
                    root.LastRouteFerryman != null && renderer.transform.IsChildOf(root.LastRouteFerryman.transform))
                    RememberProtected(renderer);
            foreach (Shader shader in new[] { Shader.Find(MountainRoadSurfaceLibrary.ShaderName), Shader.Find(MountainRoadSurfaceLibrary.FoliageShaderName) })
            {
                Assert.That(shader, Is.Not.Null);
                Assert.That(shader.isSupported, Is.True);
#if UNITY_EDITOR
                Assert.That(UnityEditor.ShaderUtil.ShaderHasError(shader), Is.False, shader.name);
#endif
            }
            audit.AssertRestoredBindings();
            return audit;

            void RememberProtected(Renderer renderer)
            {
                Material[] materials = renderer.sharedMaterials;
                for (int index = 0; index < materials.Length; index++)
                {
                    Material material = materials[index];
                    if (material == null) continue;
                    Assert.That(material.shader.name, Does.Not.StartWith(MountainRoadSurfaceLibrary.ShaderName), renderer.name);
                    ReadRoadSurfaceBlock(renderer, index, block);
                    if (block.isEmpty) ReadRoadSurfaceBlock(renderer, -1, block);
                    audit.Protected.Add(new RoadProtectedSurface { Renderer = renderer, Material = material,
                        MaterialIndex = index, Albedo = RoadBoundTexture(block, material, "_BaseMap") });
                }
            }
        }

        private static void AssertRoadSurfaceGroup(Transform group, RoadSurfaceAudit audit, string description)
        {
            Assert.That(group, Is.Not.Null, description);
            bool found = false;
            foreach (RoadSurfaceBinding binding in audit.Bindings) found |= binding.Renderer.transform.IsChildOf(group);
            Assert.That(found, Is.True, description + " must use actual refreshed surface maps.");
        }

        private static Texture2D AssertRoadSurfaceTexture(string resource, int size, bool color, bool mipmaps, bool requireBc7 = true)
        {
            Texture2D texture = Resources.Load<Texture2D>(resource);
            Assert.That(texture, Is.Not.Null, resource);
            Assert.That(texture.width, Is.EqualTo(size), resource);
            Assert.That(texture.height, Is.EqualTo(size), resource);
            if (requireBc7) Assert.That(texture.format, Is.EqualTo(TextureFormat.BC7), resource);
            Assert.That(texture.mipmapCount, Is.EqualTo(mipmaps ? 1 + Mathf.FloorToInt(Mathf.Log(size, 2)) : 1), resource);
            Assert.That(texture.isReadable, Is.False, resource);
#if UNITY_EDITOR
            var importer = UnityEditor.AssetImporter.GetAtPath(UnityEditor.AssetDatabase.GetAssetPath(texture)) as UnityEditor.TextureImporter;
            Assert.That(importer, Is.Not.Null, resource);
            Assert.That(importer.sRGBTexture, Is.EqualTo(color), resource);
#endif
            return texture;
        }

        private static Texture RoadBoundTexture(MaterialPropertyBlock block, Material material, string name) =>
            block.GetTexture(name) ?? (material.HasProperty(name) ? material.GetTexture(name) : null);
        private static float RoadBoundFloat(MaterialPropertyBlock block, Material material, string name) =>
            block.HasFloat(Shader.PropertyToID(name)) ? block.GetFloat(name) : material.HasProperty(name) ? material.GetFloat(name) : 0f;
        private static void ReadRoadSurfaceBlock(Renderer renderer, int index, MaterialPropertyBlock block)
        { block.Clear(); if (index < 0) renderer.GetPropertyBlock(block); else renderer.GetPropertyBlock(block, index); }
        private static void WriteRoadSurfaceBlock(Renderer renderer, int index, MaterialPropertyBlock block)
        { if (index < 0) renderer.SetPropertyBlock(block); else renderer.SetPropertyBlock(block, index); }

        private static Shot[] RoadSurfaceShots(MountainRoadRoot root, RoadSurfaceAudit audit)
        {
            var shots = new List<Shot>();
            var cafe = root.Plan.Terminal.Cafe;
            Vector3 cafeEye = cafe.Center - cafe.Right * 2.9f - cafe.Forward * 2.6f + Vector3.up * 1.55f;
            Vector3 cafeAim = cafe.Center + cafe.Right * .4f - cafe.Forward * .2f + Vector3.up * 1.1f;
            AddPair("Cafe pigment", cafeEye, cafeAim, audit.SetPigment);
            AddPair("Cafe physical response", cafeEye, cafeAim, audit.SetResponse);
            foreach (string name in new[] { "Cafe_RefrigeratorBody", "Cafe_Stove", "Cafe_CounterTop", "Cafe_StoolSeats" })
                foreach (MountainRoadCafePartBinding part in root.World.Cafe.Model.Parts)
                    if (part.SourceName == name && part.Renderer != null)
                    {
                        Vector3 aim = part.Renderer.bounds.center;
                        string description = name == "Cafe_RefrigeratorBody" ? "Cafe refrigerator enamel" :
                            name == "Cafe_Stove" ? "Cafe stove surface" : name == "Cafe_CounterTop" ?
                            "Cafe counter surface" : "Cafe stool upholstery";
                        AddView(description, aim - cafe.Forward * 2f - cafe.Right * 1.3f + Vector3.up * .6f, aim, 56f);
                        break;
                    }
            Renderer stationMetal = Array.Find(root.World.Cableway.StationRoot.GetComponentsInChildren<Renderer>(true),
                renderer => renderer.name == "Bullwheel Bearing Housing");
            Assert.That(stationMetal, Is.Not.Null);
            Vector3 stationAim = stationMetal.bounds.center;
            Vector3 stationEye = stationAim - root.Plan.Terminal.Cableway.StationArea.Forward * 3.1f +
                root.Plan.Terminal.Cableway.StationArea.Right * 1.9f + Vector3.up * .8f;
            AddPair("Station physical response", stationEye, stationAim, audit.SetResponse);
            MountainRoadRouteSample road = root.Plan.Route.Sample(18f);
            AddPair("Road pigment", road.Position + road.Right * 1.8f + Vector3.up * 1.45f,
                road.Position + road.Forward * 3f + Vector3.up * .02f, audit.SetPigment);
            var tunnel = root.Plan.Tunnel;
            Vector3 tunnelRight = Vector3.Cross(Vector3.up, tunnel.OutwardAxis);
            AddView("Tunnel stone close view", tunnel.PortalGroundCenter + tunnel.OutwardAxis * 4.5f +
                tunnelRight * 2.8f + Vector3.up * 2.4f,
                tunnel.PortalGroundCenter + tunnelRight * (tunnel.OpeningWidth * .5f + .32f) + Vector3.up * 2.2f, 56f);
            MountainRoadForestDescriptor tree = root.Plan.Forest[0];
            foreach (MountainRoadForestDescriptor candidate in root.Plan.Forest)
                if (candidate.Layer == MountainRoadForestLayer.Physical && candidate.Position.y > root.Plan.Route.Start.y + 10f)
                { tree = candidate; break; }
            AddView("Forest bark close view", tree.Position + root.Plan.Plateau.Right * (tree.TrunkRadius + 1.5f) + Vector3.up * 1.7f,
                tree.Position + Vector3.up * 1.5f, 52f);
            AddView("Forest needles close view", tree.Position + root.Plan.Plateau.Right * (tree.CrownRadius + 2.3f) + Vector3.up * tree.Height * .38f,
                tree.Position + Vector3.up * tree.Height * .42f, 52f);
            Assert.That(root.Plan.Terminal.Site.TryGetPart("site-plough-bank-00", out MountainRoadSitePartDescriptor snowBank), Is.True);
            AddView("Ploughed snow close view", snowBank.Center + root.Plan.Plateau.Forward * 3.2f +
                root.Plan.Plateau.Right * 2.1f + Vector3.up * 2.5f,
                snowBank.Center + Vector3.up * (snowBank.Size.y * .3f), 60f);
            foreach (MountainRoadMiscDescriptor item in root.Plan.Misc)
                if (item.Kind == MountainRoadMiscKind.UtilityCabinet)
                {
                    Vector3 aim = item.Position + Vector3.up * .55f;
                    AddView("Road cabinet paint and rust", aim + item.Rotation * (Vector3.forward * 2.3f + Vector3.right) + Vector3.up * .5f, aim, 56f);
                    break;
                }
            return shots.ToArray();

            void AddView(string subject, Vector3 eye, Vector3 aim, float fov) => shots.Add(Shot.At(RoadLightingFrame(subject), eye, aim, fov));
            void AddPair(string subject, Vector3 eye, Vector3 aim, Action<bool> apply)
            {
                bool captured = false;
                shots.Add(Shot.At(RoadLightingFrame(subject + " enabled"), eye, aim, 64f, readyWhen: () =>
                {
                    if (captured) return true;
                    Camera camera = Camera.main;
                    camera.transform.SetPositionAndRotation(eye, Quaternion.LookRotation((aim - eye).normalized, Vector3.up));
                    camera.fieldOfView = 64f;
                    try
                    {
                        apply(false);
                        CaptureCurrentCamera(camera, SceneIds.MountainRoad, RoadLightingFrame(subject + " baseline"));
                        apply(true);
                        CaptureCurrentCamera(camera, SceneIds.MountainRoad, RoadLightingFrame(subject + " enabled"));
                        audit.AssertRestoredBindings();
                        captured = true;
                    }
                    finally { apply(true); }
                    return true;
                }));
            }
        }
    }
}

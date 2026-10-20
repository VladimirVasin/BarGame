using System;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;

namespace BarPromenade.Tests.PlayMode
{
    public sealed partial class AreaCaptureFixture
    {
        private sealed class VillageSurfaceBinding
        {
            public Renderer Renderer;
            public int Index, MaterialIndex;
            public Material Material;
            public float ResponseStrength, NormalStrength;
            public Texture Albedo, Response;
        }

        private sealed class VillageProtectedSurface
        {
            public Renderer Renderer;
            public int Index;
            public Material Material;
            public Texture Albedo;
        }

        private sealed class VillageSurfaceAudit
        {
            public readonly List<VillageSurfaceBinding> Bindings = new List<VillageSurfaceBinding>();
            public readonly List<VillageProtectedSurface> Protected = new List<VillageProtectedSurface>();
            public Action Cleanup;

            public void SetResponse(bool enabled)
            {
                var block = new MaterialPropertyBlock();
                foreach (VillageSurfaceBinding binding in Bindings)
                {
                    if (binding.Renderer == null) continue;
                    ReadVillageSurfaceBlock(binding.Renderer, binding.Index, block);
                    // Preserve live wind, tint and other owners' updates; only
                    // the two values changed by the synchronized pair are restored.
                    block.SetFloat("_SceneResponseStrength", enabled ? binding.ResponseStrength : 0f);
                    block.SetFloat("_SceneNormalStrength", enabled ? binding.NormalStrength : 0f);
                    WriteVillageSurfaceBlock(binding.Renderer, binding.Index, block);
                }
            }

            public void Restore()
            {
                SetResponse(true);
                Cleanup?.Invoke();
            }

            public void AssertRestoredBindings()
            {
                var block = new MaterialPropertyBlock();
                foreach (VillageSurfaceBinding binding in Bindings)
                {
                    Assert.That(binding.Renderer, Is.Not.Null);
                    Assert.That(binding.Renderer.sharedMaterials[binding.MaterialIndex],
                        Is.SameAs(binding.Material), binding.Renderer.name);
                    ReadVillageSurfaceBlock(binding.Renderer, binding.Index, block);
                    Assert.That(VillageBoundTexture(block, binding.Material, "_BaseMap"), Is.SameAs(binding.Albedo));
                    Assert.That(VillageBoundTexture(block, binding.Material, "_SceneMaterialResponse"),
                        Is.SameAs(binding.Response), binding.Renderer.name);
                    Assert.That(block.GetFloat("_SceneResponseStrength"), Is.EqualTo(binding.ResponseStrength));
                    Assert.That(block.GetFloat("_SceneNormalStrength"), Is.EqualTo(binding.NormalStrength));
                }
                foreach (VillageProtectedSurface surface in Protected)
                {
                    if (surface.Renderer == null) continue;
                    Material material = surface.Renderer.sharedMaterials[surface.Index];
                    Assert.That(material, Is.SameAs(surface.Material), surface.Renderer.name);
                    ReadVillageSurfaceBlock(surface.Renderer, surface.Index, block);
                    if (block.isEmpty) ReadVillageSurfaceBlock(surface.Renderer, -1, block);
                    Assert.That(VillageBoundTexture(block, material, "_BaseMap"), Is.SameAs(surface.Albedo),
                        "Lighting toggles, snow rebuilding and exterior resume must preserve protected surfaces.");
                }
            }
        }

        private static VillageSurfaceAudit AssertVillageSurfaceContracts(AlpineVillageRoot root)
        {
            TextAsset manifestText = Resources.Load<TextAsset>(VillageSurfaceAppearance.ManifestResourcePath);
            Assert.That(manifestText, Is.Not.Null);
            VillageSurfaceManifest manifest = VillageSurfaceAppearance.ParseManifestOrThrow(manifestText.text);
            Assert.That(manifest.gpu_budget_bytes, Is.LessThanOrEqualTo(96L * 1024 * 1024).And.GreaterThan(0));
            Assert.That(manifest.estimated_gpu_bytes, Is.InRange(1L, manifest.gpu_budget_bytes));
            Assert.That(manifest.sheets, Is.Not.Empty);
            var textures = new HashSet<Texture2D>();
            var families = new HashSet<string>(StringComparer.Ordinal);
            foreach (VillageSurfaceSheet sheet in manifest.sheets)
            {
                Assert.That(families.Add(sheet.family), Is.True, "Every surface family has one map pair.");
                Assert.That(sheet.meters_per_tile, Is.GreaterThan(0f), sheet.family);
                Texture2D albedo = AssertVillageSurfaceTexture(sheet.albedo, sheet.size, true);
                Texture2D response = AssertVillageSurfaceTexture(sheet.response, sheet.response_size, false);
                Assert.That(albedo.width / response.width, Is.InRange(1, 2), sheet.family);
                Assert.That(response.wrapMode, Is.EqualTo(albedo.wrapMode), sheet.family);
                Assert.That(response.filterMode, Is.EqualTo(albedo.filterMode), sheet.family);
                textures.Add(albedo); textures.Add(response);
                if (!string.IsNullOrEmpty(sheet.ground_response))
                    textures.Add(AssertVillageSurfaceTexture(sheet.ground_response, 512, false));
            }
            Texture2D uniqueAlbedo = AssertVillageSurfaceTexture(VillageSurfaceAppearance.UniqueAlbedoPath, 4096, true);
            textures.Add(uniqueAlbedo);
            textures.Add(AssertVillageSurfaceTexture(VillageSurfaceAppearance.UniqueResponsePath, 2048, false));
            // The budget owns the resident junction paintings and preserved
            // pictures too. Count actual imported blocks, with no duplicate maps.
            foreach (string path in new[] { AlpineVillageJunctionAppearance.TextureResourcePath,
                         AlpineVillageJunctionAppearance.ResponseResourcePath, VillageExpansionAssetProvider.LodgePicturesTexturePath,
                         VillageExpansionAssetProvider.LodgeGroupPhotographTexturePath })
            {
                Texture2D texture = Resources.Load<Texture2D>(path);
                Assert.That(texture, Is.Not.Null, path); textures.Add(texture);
            }
            long actualBytes = 0;
            foreach (Texture2D texture in textures)
            {
                Assert.That(texture.format, Is.EqualTo(TextureFormat.BC7), texture.name);
                for (int mip = 0; mip < texture.mipmapCount; mip++)
                    actualBytes += (long)((Mathf.Max(1, texture.width >> mip) + 3) / 4) *
                        ((Mathf.Max(1, texture.height >> mip) + 3) / 4) * 16;
            }
            Assert.That(actualBytes, Is.LessThanOrEqualTo(manifest.gpu_budget_bytes),
                "The refreshed environment must stay inside its imported BC7 texture budget.");

            var audit = new VillageSurfaceAudit();
            var block = new MaterialPropertyBlock();
            // Identify protected parts by the authored contract as well as by
            // their current shader. A mistaken rebind must not hide its origin.
            var protectedNames = new HashSet<string>(StringComparer.Ordinal);
            foreach (VillageExpansionPart part in VillageExpansionAssetProvider.LoadOrThrow().Manifest.parts)
                if (part.surface == "Fire" || part.surface == "Glass" || part.surface == "LodgePictures" ||
                    part.surface == "LodgeGroupPhotograph") protectedNames.Add(part.name);
            foreach (VillageWorkroomPart part in VillageWorkroomAssets.Manifest.parts)
                if (part.surface == "Glass") protectedNames.Add(part.name);
            int water = 0, pictures = 0, groundBindings = 0;
            foreach (Renderer renderer in root.World.Root.GetComponentsInChildren<Renderer>(true))
            {
                Material[] materials = renderer.sharedMaterials;
                for (int index = 0; index < materials.Length; index++)
                {
                    Material material = materials[index];
                    if (material == null) continue;
                    ReadVillageSurfaceBlock(renderer, index, block);
                    int bindingIndex = index;
                    if (block.isEmpty) { bindingIndex = -1; ReadVillageSurfaceBlock(renderer, -1, block); }
                    Texture albedo = VillageBoundTexture(block, material, "_BaseMap");
                    bool ownsSurface = material.shader.name == "Bar Promenade/Village Surface" ||
                        material.shader.name == "Bar Promenade/Village Surface Foliage";
                    bool picture = albedo != null && (albedo.name == "LodgePictures" || albedo.name == "LodgeGroupPhotograph");
                    bool protectedSurface = protectedNames.Contains(renderer.name) || picture || renderer.name == "Spring Brook Water" ||
                        material.shader.name.IndexOf("Flame", StringComparison.OrdinalIgnoreCase) >= 0 ||
                        renderer is SkinnedMeshRenderer;
                    if (picture) pictures++;
                    if (renderer.name == "Spring Brook Water") water++;
                    if (protectedSurface)
                    {
                        Assert.That(ownsSurface, Is.False, renderer.name + " must retain its own authored presentation.");
                        audit.Protected.Add(new VillageProtectedSurface
                            { Renderer = renderer, Index = index, Material = material, Albedo = albedo });
                    }
                    if (material.HasProperty("_GroundResponse"))
                    {
                        groundBindings++;
                        Assert.That(textures.Contains(albedo as Texture2D), Is.True, renderer.name + " ground albedo is registered.");
                        Assert.That(textures.Contains(VillageBoundTexture(block, material, "_GroundResponse") as Texture2D),
                            Is.True, renderer.name + " uses the ground response format.");
                        if (renderer.name == AlpineVillageWorldBuilder.SnowDriftObjectName)
                            foreach (string map in new[] { "_GroundSubstrateMap", "_GroundSubstrateResponse",
                                         "_GroundAsphaltMap", "_GroundAsphaltResponse" })
                                Assert.That(textures.Contains(VillageBoundTexture(block, material, map) as Texture2D),
                                    Is.True, "Snow must reveal the same refreshed substrate: " + map);
                    }
                    if (!ownsSurface) continue;
                    Texture response = VillageBoundTexture(block, material, "_SceneMaterialResponse");
                    Assert.That(albedo, Is.Not.Null, renderer.name);
                    Assert.That(response, Is.Not.Null, renderer.name);
                    Assert.That(textures.Contains(albedo as Texture2D), Is.True, renderer.name + " albedo is registered.");
                    Assert.That(textures.Contains(response as Texture2D), Is.True, renderer.name + " response is registered.");
                    if (albedo == uniqueAlbedo)
                    {
                        Assert.That(block.GetVector("_BaseMap_ST"), Is.EqualTo(new Vector4(1, 1, 0, 0)), renderer.name);
                        Assert.That(block.GetColor("_BaseColor"), Is.EqualTo(Color.white), renderer.name);
                    }
                    audit.Bindings.Add(new VillageSurfaceBinding { Renderer = renderer, Index = bindingIndex,
                        MaterialIndex = index, Material = material, Albedo = albedo, Response = response,
                        ResponseStrength = block.HasProperty(Shader.PropertyToID("_SceneResponseStrength")) ?
                            block.GetFloat("_SceneResponseStrength") : material.GetFloat("_SceneResponseStrength"),
                        NormalStrength = block.HasProperty(Shader.PropertyToID("_SceneNormalStrength")) ?
                            block.GetFloat("_SceneNormalStrength") : material.GetFloat("_SceneNormalStrength") });
                }
            }
            Assert.That(audit.Bindings, Is.Not.Empty, "Actual village renderers must use the refreshed material response.");
            Assert.That(water, Is.GreaterThan(0));
            Assert.That(pictures, Is.GreaterThan(0));
            Assert.That(groundBindings, Is.GreaterThan(0));
            AssertVillageSurfaceCoverage(root, audit);
            foreach (SkinnedMeshRenderer character in root.GetComponentsInChildren<SkinnedMeshRenderer>(true))
                foreach (Material material in character.sharedMaterials)
                    Assert.That(material.shader.name, Does.Not.StartWith("Bar Promenade/Village Surface"), character.name);
            audit.AssertRestoredBindings();
            return audit;
        }

        private static void AssertVillageSurfaceCoverage(AlpineVillageRoot root, VillageSurfaceAudit audit)
        {
            Transform world = root.World.Root.transform;
            foreach (string path in new[] { root.Plan.MothersHouse.StableId, "Village Expansion/Ski Lodge",
                         "Village Expansion/Former Trade Warehouse", "Village Expansion/Abandoned Truck Wreck",
                         "Village Expansion/Discarded Wooden Chairs", AlpineVillageTreeBuilder.RootName,
                         "Village Expansion/" + AlpineVillageAbandonmentPlan.RootName })
            {
                Transform group = root.World.SemanticObjects.TryGetValue(path, out Transform semantic) ? semantic : world.Find(path);
                AssertVillageSurfaceGroup(group, audit, path);
            }
            AssertVillageSurfaceGroup(root.World.Workroom.Root, audit, "Workroom");
            AssertVillageSurfaceGroup(root.World.StationRoot.transform, audit, "Upper station");
        }

        private static void AssertVillageSurfaceGroup(Transform group, VillageSurfaceAudit audit, string description)
        {
            Assert.That(group, Is.Not.Null, description);
            bool found = false;
            foreach (VillageSurfaceBinding binding in audit.Bindings)
                found |= binding.Renderer.transform.IsChildOf(group);
            Assert.That(found, Is.True, description + " must contain actual refreshed material receivers.");
        }

        private static Texture2D AssertVillageSurfaceTexture(string resource, int size, bool color)
        {
            Texture2D texture = Resources.Load<Texture2D>(resource);
            Assert.That(texture, Is.Not.Null, resource);
            Assert.That(texture.width, Is.EqualTo(size), resource);
            Assert.That(texture.height, Is.EqualTo(size), resource);
            Assert.That(texture.format, Is.EqualTo(TextureFormat.BC7), resource);
            Assert.That(texture.mipmapCount, Is.EqualTo(1 + Mathf.FloorToInt(Mathf.Log(size, 2))), resource);
            Assert.That(texture.isReadable, Is.False, resource);
#if UNITY_EDITOR
            var importer = UnityEditor.AssetImporter.GetAtPath(UnityEditor.AssetDatabase.GetAssetPath(texture)) as UnityEditor.TextureImporter;
            Assert.That(importer, Is.Not.Null, resource);
            Assert.That(importer.sRGBTexture, Is.EqualTo(color), resource);
#endif
            return texture;
        }

        private static Texture VillageBoundTexture(MaterialPropertyBlock block, Material material, string name) =>
            block.GetTexture(name) ?? (material.HasProperty(name) ? material.GetTexture(name) : null);

        private static void ReadVillageSurfaceBlock(Renderer renderer, int index, MaterialPropertyBlock block)
        {
            block.Clear();
            if (index < 0) renderer.GetPropertyBlock(block); else renderer.GetPropertyBlock(block, index);
        }

        private static void WriteVillageSurfaceBlock(Renderer renderer, int index, MaterialPropertyBlock block)
        {
            if (index < 0) renderer.SetPropertyBlock(block); else renderer.SetPropertyBlock(block, index);
        }

        private static Shot[] VillageSurfaceShots(AlpineVillageRoot root, VillageSurfaceAudit audit)
        {
            var shots = new List<Shot>();
            AlpineVillagePlan plan = root.Plan;
            Vector3 previousPlayerPosition = root.Player.GameObject.transform.position;
            bool previousLantern = LodgeShelterSessionState.LanternLit;
            audit.Cleanup = () =>
            {
                if (root == null || root.Player.GameObject == null) return;
                root.Player.Motor.Teleport(previousPlayerPosition);
                root.LodgeShelter.SetLanternLit(previousLantern);
            };
            AlpineVillagePlotDescriptor mother = plan.MothersHouse;
            Vector3 motherRight = Vector3.Cross(Vector3.up, mother.Facing).normalized;
            AddPair("Mother facade", mother.GroundCenter + mother.Facing * 6.6f + motherRight * 3.1f + Vector3.up * 2.1f,
                mother.GroundCenter + mother.Facing * 3.735f + motherRight * 1.47f + Vector3.up * 2.55f, 70f);
            foreach (AlpineVillagePlotDescriptor plot in plan.Plots)
                if (plot.Kind == AlpineVillagePlotKind.House && plot.StableId != VillageWorkroomPlan.HouseId)
                {
                    Vector3 right = Vector3.Cross(Vector3.up, plot.Facing).normalized;
                    AddView("Core timber and shutters",
                        plot.DoorGroundPosition + plot.Facing * 3.1f + right * .8f + Vector3.up * EyeHeight,
                        plot.DoorGroundPosition + right * .2f + Vector3.up * 1.4f, 62f);
                    break;
                }
            AddPair("Lodge wood and cloth", LodgePoint(root, -3.8f, 1.8f, -2.7f),
                LodgePoint(root, -8f, 1.1f, .7f), 72f, () => root.LodgeShelter.SetLanternLit(true));
            AddView("Lodge minibar and upholstery", LodgePoint(root, 4.7f, 1.5f, -2.7f),
                LodgePoint(root, 7f, .7f, -4.5f), 62f);
            VillageWorkroomPlan workroom = root.World.Workroom.Plan;
            AddPair("Workroom materials", workroom.World(new Vector3(.65f, 1.55f, .1f)),
                workroom.World(new Vector3(-2.1f, .85f, .1f)), 76f);
            AddView("Workroom repairs and cloth", workroom.World(new Vector3(-.8f, 1.3f, .3f)),
                workroom.Anchor("ClothRest"), 48f);
            var photographed = new HashSet<string>(StringComparer.Ordinal);
            foreach (AlpineVillageAbandonedPlot plot in plan.Expansion.Abandonment.Plots)
            {
                if (plot.Model != "TownHall" && plot.Model != "AbandonedHouseA" && plot.Model != "Workshop") continue;
                if (!photographed.Add(plot.Model)) continue;
                string description = plot.Model == "TownHall" ? "Civic masonry" :
                    plot.Model == "Workshop" ? "Old workshop" : "Abandoned household";
                Vector3 from = plot.World(new Vector2(plot.Size.x * .3f, plot.Size.y * .5f + 5f)) + Vector3.up * 1.8f;
                AddView(description, from,
                    plot.GroundCenter + Vector3.up * 2f + plot.Forward * (plot.Size.y * .5f), 65f);
            }
            AlpineVillageExpansionPlan expansion = plan.Expansion;
            AddExpansion("Trade warehouse", expansion.WarehouseCenter, 9f, 3f);
            AddExpansion("Truck rust and old paint", expansion.TruckWreckCenter, 4.5f, 1f);
            AddExpansion("Discarded chair wood", expansion.ChairPileCenter, 4.3f, .8f);
            Vector3 forest = expansion.ForestEntrance;
            forest.y = AlpineVillageTerrainSampler.SampleMeshHeight(plan, new Vector2(forest.x, forest.z));
            AddView("Forest bark and needles", forest + Vector3.up * EyeHeight,
                forest - plan.SlopeRight * 9f + plan.Uphill * 2f + Vector3.up * 2.2f, 66f);
            MountainRoadForestDescriptor closeTree = plan.Trees.ForestTrees[0];
            float nearestTree = float.PositiveInfinity;
            foreach (MountainRoadForestDescriptor candidate in plan.Trees.ForestTrees)
            {
                float distance = Vector3.ProjectOnPlane(candidate.Position - forest, Vector3.up).sqrMagnitude;
                if (distance >= nearestTree) continue;
                closeTree = candidate; nearestTree = distance;
            }
            AddView("Forest bark close view",
                closeTree.Position + plan.SlopeRight * (closeTree.TrunkRadius + 1.7f) + Vector3.up * 1.7f,
                closeTree.Position + Vector3.up * 1.5f, 50f);
            AddView("Forest needles close view",
                closeTree.Position + plan.SlopeRight * (closeTree.CrownRadius + 3f) + Vector3.up * (closeTree.Height * .38f),
                closeTree.Position + Vector3.up * (closeTree.Height * .42f), 52f);
            Vector3 ground = expansion.ToWorld(new Vector2(-70f, 65f));
            ground.y = AlpineVillageTerrainSampler.SampleMeshHeight(plan, new Vector2(ground.x, ground.z));
            AddView("Snow road and soil", ground + plan.SlopeRight * 3f + Vector3.up * 1.8f,
                ground + plan.Uphill * 2f + Vector3.up * .05f, 67f);
            AppendVillageSnowMaterialShots(root, audit, shots, ground);
            shots.Add(Shot.At("Test village - Surface return to core",
                previousPlayerPosition + Vector3.up * EyeHeight,
                plan.MothersHouse.GroundCenter + Vector3.up * 2.8f, 62f,
                readyWhen: ReadyAt(previousPlayerPosition, () => root.LodgeShelter.SetLanternLit(previousLantern), true)));
            return shots.ToArray();

            void AddPair(string subject, Vector3 eye, Vector3 aim, float fov, Action prepare = null)
            {
                shots.Add(Shot.At("Test village - " + subject + " flat response", eye, aim, fov,
                    readyWhen: ReadyAt(eye, () => { prepare?.Invoke(); audit.SetResponse(false); })));
                shots.Add(Shot.At("Test village - " + subject + " material response", eye, aim, fov,
                    readyWhen: () => { audit.SetResponse(true); return true; }));
            }
            void AddExpansion(string subject, Vector3 center, float distance, float height)
            {
                AddView(subject,
                    center - expansion.LodgeForward * distance + plan.SlopeRight * (distance * .4f) + Vector3.up * 1.7f,
                    center + Vector3.up * height, 63f);
            }
            void AddView(string subject, Vector3 eye, Vector3 aim, float fov) =>
                shots.Add(Shot.At("Test village - " + subject, eye, aim, fov, readyWhen: ReadyAt(eye)));

            Func<bool> ReadyAt(Vector3 point, Action prepare = null, bool rootPosition = false)
            {
                bool placed = false;
                int frames = 0;
                return () =>
                {
                    // Material review needs unobscured edges. This fixture
                    // resets only its own test session's presentation clock;
                    // production cold behaviour and severe weather stay live.
                    AlpineColdExposure.ResetSession();
                    if (!placed)
                    {
                        Vector3 foot = point;
                        if (!rootPosition)
                        {
                            foot.y = root.LodgeShelter.ContainsInterior(point) ? plan.Expansion.LodgeFloorHeight :
                                root.World.Workroom.Plan.ContainsInterior(point) ? root.World.Workroom.Plan.House.GroundCenter.y :
                                AlpineVillageTerrainSampler.SampleMeshHeight(plan, new Vector2(point.x, point.z));
                            foot += Vector3.up * PlayerFactory.GroundedRootOffset;
                        }
                        root.Player.Motor.Teleport(foot);
                        prepare?.Invoke();
                        placed = true;
                    }
                    return ++frames > 12;
                };
            }
        }

        private static void AppendVillageSnowMaterialShots(AlpineVillageRoot root, VillageSurfaceAudit audit,
            List<Shot> shots, Vector3 near)
        {
            AlpineVillageSnowTreading snow = root.World.SnowTreading;
            MeshFilter filter = snow.GetComponent<MeshFilter>();
            Vector3 patch = default;
            float closest = float.PositiveInfinity;
            foreach (Vector3 local in filter.sharedMesh.vertices)
            {
                Vector3 point = filter.transform.TransformPoint(local);
                float distance = Vector3.ProjectOnPlane(point - near, Vector3.up).sqrMagnitude;
                if (distance >= closest || distance > 144f || snow.SampleVisibleDepth(point) < .15f) continue;
                closest = distance; patch = point;
            }
            Assert.That(float.IsInfinity(closest), Is.False, "Exercise real positive-depth snow beside the old road.");
            float originalDepth = snow.SampleVisibleDepth(patch);
            Material material = filter.GetComponent<Renderer>().sharedMaterial;
            var properties = new MaterialPropertyBlock();
            filter.GetComponent<Renderer>().GetPropertyBlock(properties);
            Texture substrate = properties.GetTexture("_GroundSubstrateMap");
            Texture response = properties.GetTexture("_GroundSubstrateResponse");
            Vector3 eye = patch + root.Plan.SlopeRight * 2.2f - root.Plan.Uphill * .5f + Vector3.up * 1.4f;
            bool pressed = false, refilled = false, cleared = false;
            shots.Add(Shot.At("Test village - Snow material pressed", eye, patch, 60f, readyWhen: () =>
            {
                if (!pressed)
                {
                    snow.Press(patch); snow.Advance(.11f); pressed = true;
                    Assert.That(snow.SampleVisibleDepth(patch), Is.LessThan(originalDepth * .5f));
                }
                VerifySnowBinding(); return true;
            }));
            shots.Add(Shot.At("Test village - Snow material refilled", eye, patch, 60f, readyWhen: () =>
            {
                if (!refilled)
                {
                    snow.Advance(60f); refilled = true;
                    Assert.That(snow.SampleVisibleDepth(patch), Is.EqualTo(originalDepth).Within(.002f));
                }
                VerifySnowBinding(); return true;
            }));
            shots.Add(Shot.At("Test village - Snow material cleared", eye, patch, 60f, readyWhen: () =>
            {
                if (!cleared)
                {
                    Assert.That(snow.ClearPatch(patch, root.Plan.SlopeRight, Vector2.one * .65f, 1f), Is.GreaterThan(0));
                    cleared = true;
                    Assert.That(snow.SampleVisibleDepth(patch), Is.LessThan(.01f));
                }
                VerifySnowBinding(); audit.AssertRestoredBindings(); return true;
            }));
            void VerifySnowBinding()
            {
                Renderer renderer = filter.GetComponent<Renderer>();
                Assert.That(renderer.sharedMaterial, Is.SameAs(material));
                renderer.GetPropertyBlock(properties);
                Assert.That(properties.GetTexture("_GroundSubstrateMap"), Is.SameAs(substrate));
                Assert.That(properties.GetTexture("_GroundSubstrateResponse"), Is.SameAs(response));
            }
        }
    }
}

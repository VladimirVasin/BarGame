using System;
using System.Collections.Generic;
using UnityEngine;

namespace BarPromenade
{
    internal enum GroundSurfaceKind
    {
        Asphalt, Soil, Gravel, Lawn, Paving, Concrete, Rock,
        Sand, Timber, Snow, Marking, Silt
    }

    /// <summary>
    /// Exterior-only material response. Owners retain albedo, metre UVs, tint,
    /// weather and geometry; this binds one shared Lit variant and a measured
    /// response sheet. No renderer receives a material instance.
    /// </summary>
    internal static class GroundSurfaceAppearance
    {
        internal const string ShaderPath = "Shaders/GroundSurfaceLit";
        internal const string ResponseFolder = "Textures/SurfaceResponse/";
        private static readonly int KindId = Shader.PropertyToID("_GroundKind");
        private static readonly int ResponseId = Shader.PropertyToID("_GroundResponse");
        private static readonly int VertexDataId = Shader.PropertyToID("_GroundVertexData");
        private static readonly int SubstrateId = Shader.PropertyToID("_GroundSubstrateMap");
        private static readonly int SubstrateResponseId = Shader.PropertyToID("_GroundSubstrateResponse");
        private static readonly int SubstrateColorId = Shader.PropertyToID("_GroundSubstrateColor");
        internal static readonly int WetnessId = Shader.PropertyToID("_GroundWetness");
        internal static readonly int CityWetnessId = Shader.PropertyToID("_CityGroundWetness");
        private static readonly Dictionary<string, Texture2D> Responses =
            new Dictionary<string, Texture2D>(StringComparer.Ordinal);
        private static readonly MaterialPropertyBlock Properties = new MaterialPropertyBlock();
        private static Material sharedMaterial;

        internal static Material SharedMaterial
        {
            get
            {
                if (sharedMaterial != null) return sharedMaterial;
                Shader shader = Resources.Load<Shader>(ShaderPath);
                if (shader == null || !shader.isSupported)
                    throw new InvalidOperationException("Missing/unsupported ground surface shader.");
                sharedMaterial = new Material(RuntimePrimitiveFactory.DefaultMaterial)
                {
                    name = "Layered Ground (Shared)", shader = shader,
                    hideFlags = HideFlags.HideAndDontSave, enableInstancing = true
                };
                return sharedMaterial;
            }
        }

        internal static Texture2D GetResponse(Texture2D albedo)
        {
            if (albedo == null) throw new ArgumentNullException(nameof(albedo));
            string name = albedo.name.Replace("Albedo", "Response");
            if (!Responses.TryGetValue(name, out Texture2D response))
            {
                response = Resources.Load<Texture2D>(ResponseFolder + name);
                if (response == null)
                    throw new InvalidOperationException("Missing ground response: " + name);
                Responses.Add(name, response);
            }
            return response;
        }

        internal static void Apply(Renderer renderer, GroundSurfaceKind kind,
            Texture2D albedo, int materialIndex = -1)
        {
            if (renderer == null) return;
            // The editable junction atlas supplies its own response after this
            // call; all tiled surfaces use the deterministic source companion.
            Texture2D response = albedo != null && albedo.name == "VillageJunctionAtlas"
                ? null : GetResponse(albedo);
            AssignSharedMaterial(renderer, SharedMaterial, materialIndex);
            Read(renderer, materialIndex);
            Properties.SetFloat(KindId, (float)kind);
            if (response != null) Properties.SetTexture(ResponseId, response);
            if (kind == GroundSurfaceKind.Snow)
            {
                Properties.SetTexture(SubstrateId,
                    MountainRoadSurfaceAppearance.GetTexture(MountainRoadSurfaceKind.ForestFloor));
                Properties.SetTexture(SubstrateResponseId,
                    GetResponse(MountainRoadSurfaceAppearance.GetTexture(MountainRoadSurfaceKind.ForestFloor)));
                Texture2D asphalt = MountainRoadSurfaceAppearance.GetTexture(MountainRoadSurfaceKind.Asphalt);
                Properties.SetTexture(Shader.PropertyToID("_GroundAsphaltMap"), asphalt);
                Properties.SetTexture(Shader.PropertyToID("_GroundAsphaltResponse"), GetResponse(asphalt));
                Properties.SetColor(Shader.PropertyToID("_GroundAsphaltColor"),
                    MountainRoadSurfaceAppearance.CreateDisplayTint(AlpineVillageRoadSurfaceBuilder.AsphaltTint,
                        MountainRoadSurfaceKind.Asphalt));
                Properties.SetColor(SubstrateColorId,
                    MountainRoadSurfaceAppearance.CreateDisplayTint(
                        new Color(0.155f, 0.175f, 0.145f), MountainRoadSurfaceKind.ForestFloor));
            }
            Write(renderer, materialIndex);
        }

        internal static void SetResponse(Renderer renderer, Texture2D response, int materialIndex = -1)
        {
            if (response == null) throw new ArgumentNullException(nameof(response));
            Read(renderer, materialIndex);
            Properties.SetTexture(ResponseId, response);
            Write(renderer, materialIndex);
        }

        internal static void ApplyCity(Renderer renderer, GroundSurfaceKind kind,
            Texture2D albedo, int materialIndex = -1)
        {
            if (renderer == null) return;
            Apply(renderer, kind, albedo, materialIndex);
            Read(renderer, materialIndex);
            Properties.SetFloat(Shader.PropertyToID("_GroundCityClimate"), 1f);
            Write(renderer, materialIndex);
        }

        internal static void EnableVertexData(Renderer renderer, int materialIndex = -1)
        {
            Read(renderer, materialIndex);
            Properties.SetFloat(VertexDataId, 1f);
            Write(renderer, materialIndex);
        }

        internal static void ApplyRidgeResponse(Renderer renderer, Texture2D texture, int materialIndex = -1)
        {
            Read(renderer, materialIndex);
            Properties.SetTexture(ResponseId, GetResponse(texture));
            Properties.SetFloat(Shader.PropertyToID("_GroundRockLayers"), 1f);
            Write(renderer, materialIndex);
        }

        internal static void SetSnowJunctions(Renderer renderer, AlpineVillagePlan plan, int materialIndex)
        {
            Texture2D response = Resources.Load<Texture2D>(AlpineVillageJunctionAppearance.ResponseResourcePath);
            if (response == null) throw new InvalidOperationException("Missing snow junction response atlas.");
            Read(renderer, materialIndex);
            Properties.SetTexture(Shader.PropertyToID("_GroundJunctionAtlas"), AlpineVillageJunctionAppearance.Texture);
            Properties.SetTexture(Shader.PropertyToID("_GroundJunctionResponse"), response);
            var bounds = new Vector4[4];
            if (plan.Expansion.Junctions.Count != bounds.Length)
                throw new InvalidOperationException("Snow junction atlas must have four tiles.");
            for (int index = 0; index < bounds.Length; index++)
            {
                Rect rect = plan.Expansion.Junctions[index].Bounds;
                bounds[index] = new Vector4(rect.xMin, rect.yMin, rect.width, rect.height);
            }
            Properties.SetVectorArray(Shader.PropertyToID("_GroundJunctionBounds"), bounds);
            Properties.SetFloat(Shader.PropertyToID("_GroundJunctions"), 1f);
            Write(renderer, materialIndex);
        }

        private static void Read(Renderer renderer, int index)
        {
            ReadProperties(renderer, Properties, index);
        }

        private static void Write(Renderer renderer, int index)
        {
            WriteProperties(renderer, Properties, index);
        }

        internal static void AssignSharedMaterial(Renderer renderer, Material material, int materialIndex)
        {
            if (materialIndex < 0) renderer.sharedMaterial = material;
            else
            {
                Material[] materials = renderer.sharedMaterials;
                if (materialIndex >= materials.Length)
                    throw new ArgumentOutOfRangeException(nameof(materialIndex));
                materials[materialIndex] = material;
                renderer.sharedMaterials = materials;
            }
        }

        internal static void ReadProperties(Renderer renderer, MaterialPropertyBlock properties, int materialIndex)
        {
            properties.Clear();
            if (materialIndex < 0) renderer.GetPropertyBlock(properties);
            else renderer.GetPropertyBlock(properties, materialIndex);
        }

        internal static void WriteProperties(Renderer renderer, MaterialPropertyBlock properties, int materialIndex)
        {
            if (materialIndex < 0) renderer.SetPropertyBlock(properties);
            else renderer.SetPropertyBlock(properties, materialIndex);
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void Reset()
        {
            if (sharedMaterial != null)
            {
                if (Application.isPlaying) UnityEngine.Object.Destroy(sharedMaterial);
                else UnityEngine.Object.DestroyImmediate(sharedMaterial);
            }
            sharedMaterial = null;
            Responses.Clear();
        }
    }
}

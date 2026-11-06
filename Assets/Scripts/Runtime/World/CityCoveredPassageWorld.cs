using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using UnityEngine;
using UnityEngine.Rendering;

namespace BarPromenade
{
    /// <summary>Finite, metre-authored dressing. The building still owns the route and collision.</summary>
    public sealed class CityCoveredPassageWorld : MonoBehaviour
    {
        private readonly List<CityCoveredPassageLight> lights = new List<CityCoveredPassageLight>(4);
        public CityCoveredPassagePlan Plan { get; private set; }
        public IReadOnlyList<CityCoveredPassageLight> Lights => lights;
        public int GraffitiCount { get; private set; }
        public int DetailCount { get; private set; }
        public string DressingSignature { get; private set; }

        internal static CityCoveredPassageWorld Build(Transform parent, BuildingLot lot, int seed,
            CityBuildingPrototypePose pose)
        {
            if (!CityCoveredPassagePlan.TryCreate(lot, seed, out var plan)) return null;
            var root = new GameObject("Covered passage " + plan.Id).transform;
            root.SetParent(parent, false);
            root.localPosition = pose.Position;
            root.localRotation = pose.Rotation;
            var world = root.gameObject.AddComponent<CityCoveredPassageWorld>();
            world.Plan = plan;
            world.BuildDressing();
            return world;
        }

        private void BuildDressing()
        {
            Color patch = Color.Lerp(new Color(.72f, .74f, .68f), Plan.AccentColor, .18f);
            var signature = new StringBuilder(512);
            foreach (var slot in Plan.WallSlots)
            {
                // Occupancy, distance, height and motif are independent on each
                // wall. A repair does not automatically sit beneath a mural.
                signature.Append(slot.Side).Append(':').Append(slot.Index).Append(':').Append(slot.Kind)
                    .Append(':').Append(slot.AlongZ.ToString("F3", CultureInfo.InvariantCulture))
                    .Append(':').Append(slot.Height.ToString("F3", CultureInfo.InvariantCulture))
                    .Append(':').Append(slot.Size.x.ToString("F3", CultureInfo.InvariantCulture))
                    .Append(':').Append(slot.Size.y.ToString("F3", CultureInfo.InvariantCulture))
                    .Append(':').Append(slot.Variant).Append(';');
                if (slot.Kind == CityCoveredPassageWallSlotKind.Empty) continue;
                if (slot.Kind == CityCoveredPassageWallSlotKind.Graffiti)
                    Mural(slot.Side, slot.Height, slot.AlongZ, slot.Size.x, slot.Size.y, slot.Variant);
                else
                    Wall(slot.Kind == CityCoveredPassageWallSlotKind.Repair ? "PlasterPatch" : slot.Kind.ToString(),
                        slot.Side, slot.Height, slot.AlongZ, slot.ModelScale,
                        slot.Kind == CityCoveredPassageWallSlotKind.Repair ? patch : null);
            }
            DressingSignature = signature.ToString();
            foreach (var fixture in Plan.Lamps)
            {
                Quaternion rotation = Quaternion.Euler(0f, (Plan.Theme + fixture.Index) % 2 == 0 ? 0f : 90f, 0f);
                Transform lamp = CityCoveredPassageAssetProvider.Create("PassageLamp", transform,
                    fixture.LocalPosition + Vector3.up * .145f, rotation, Vector3.one);
                var anchor = new GameObject("IndustrialTaskLight_CoveredPassage").transform;
                anchor.SetParent(transform, false);
                anchor.localPosition = fixture.LocalPosition;
                anchor.localRotation = Quaternion.LookRotation(Vector3.down, Vector3.forward);
                var source = anchor.gameObject.AddComponent<CityCoveredPassageLight>();
                source.Initialize(Plan, fixture);
                foreach (Renderer renderer in lamp.GetComponentsInChildren<Renderer>(true))
                    if (renderer.name.EndsWith("__Lens", StringComparison.Ordinal))
                        source.RegisterLens(renderer, new Color(1.15f, 1.24f, 1.08f));
                lights.Add(source);
            }
        }

        private Transform Wall(string model, int side, float height, float along, Vector3 scale, Color? color = null)
        {
            Bounds bounds = Plan.LocalBounds;
            // The authored repair is 12 mm thick. Paint must sit outside its
            // front face as well as the original wall, with a small depth margin.
            float inset = model == "GraffitiPanel" ? .035f : model == "PlasterPatch" ? .009f : .025f;
            var position = new Vector3(side < 0 ? bounds.min.x + inset : bounds.max.x - inset,
                bounds.min.y + height, along);
            Transform detail = CityCoveredPassageAssetProvider.Create(model, transform, position,
                Quaternion.Euler(0f, side < 0 ? 90f : -90f, 0f), scale, color);
            if (model != "GraffitiPanel") DetailCount++;
            return detail;
        }

        private void Mural(int side, float height, float along, float width, float tall, int tile)
        {
            Transform mural = Wall("GraffitiPanel", side, height, along, new Vector3(width, tall, 1f));
            CityCoveredPassageAssetProvider.SetGraffiti(mural, tile);
            GraffitiCount++;
        }
    }

    /// <summary>Shared imported meshes/materials. Imported FBX scales remain on the child.</summary>
    internal static class CityCoveredPassageAssetProvider
    {
        internal const string ResourceFolder = "City/CoveredPassage/";
        private static readonly Dictionary<string, GameObject> Templates = new Dictionary<string, GameObject>(8);
        private static readonly MaterialPropertyBlock Properties = new MaterialPropertyBlock();
        private static readonly int BaseColor = Shader.PropertyToID("_BaseColor");
        private static readonly int BaseTransform = Shader.PropertyToID("_BaseMap_ST");
        private static Material graffiti;

        internal static Transform Create(string name, Transform parent, Vector3 position, Quaternion rotation,
            Vector3 scale, Color? color = null)
        {
            if (!Templates.TryGetValue(name, out var template) || template == null)
            {
                template = Resources.Load<GameObject>(ResourceFolder + name);
                if (template == null) throw new InvalidOperationException("Missing covered-passage model: " + name);
                Templates[name] = template;
            }
            var wrapper = new GameObject(name).transform;
            wrapper.SetParent(parent, false);
            wrapper.localPosition = position;
            wrapper.localRotation = rotation;
            wrapper.localScale = scale;
            GameObject model = UnityEngine.Object.Instantiate(template, wrapper, false);
            foreach (MeshRenderer renderer in model.GetComponentsInChildren<MeshRenderer>(true))
            {
                int split = renderer.name.LastIndexOf("__", StringComparison.Ordinal);
                if (split < 0) throw new InvalidOperationException("Covered-passage mesh lacks a surface: " + renderer.name);
                string role = renderer.name.Substring(split + 2);
                bool lens = role == "Lens", paint = role == "Graffiti";
                renderer.sharedMaterial = lens ? CityNightResources.EmissiveMaterial :
                    paint ? GraffitiMaterial : CityPortAssetProvider.GetSurfaceMaterial(role);
                renderer.shadowCastingMode = lens || paint || name == "PlasterPatch" ? ShadowCastingMode.Off : ShadowCastingMode.On;
                renderer.receiveShadows = !lens;
                renderer.lightProbeUsage = lens ? LightProbeUsage.Off : LightProbeUsage.BlendProbes;
                renderer.reflectionProbeUsage = ReflectionProbeUsage.Off;
                if (color.HasValue)
                {
                    Properties.Clear(); renderer.GetPropertyBlock(Properties);
                    Properties.SetColor(BaseColor, color.Value.linear);
                    renderer.SetPropertyBlock(Properties);
                }
                if (!lens && !paint) CitySurfaceLibrary.ApplyRenderer(renderer);
            }
            return wrapper;
        }

        internal static void SetGraffiti(Transform mural, int tile)
        {
            foreach (Renderer renderer in mural.GetComponentsInChildren<Renderer>(true))
            {
                Properties.Clear(); renderer.GetPropertyBlock(Properties);
                Properties.SetVector(BaseTransform, new Vector4(.25f, .5f, (tile % 4) * .25f, (1 - tile / 4) * .5f));
                renderer.SetPropertyBlock(Properties);
            }
        }

        private static Material GraffitiMaterial
        {
            get
            {
                if (graffiti != null) return graffiti;
                Shader shader = Resources.Load<Shader>("Shaders/CitySurfaceLit");
                Texture2D atlas = Resources.Load<Texture2D>(ResourceFolder + "GraffitiAtlas");
                if (shader == null || atlas == null) throw new InvalidOperationException("Missing covered-passage graffiti atlas/shader.");
                graffiti = new Material(shader) { name = "City Passage Graffiti Shared", hideFlags = HideFlags.HideAndDontSave };
                graffiti.SetTexture("_BaseMap", atlas);
                graffiti.SetColor(BaseColor, new Color(.78f, .78f, .78f, 1f));
                graffiti.SetFloat("_Smoothness", .06f);
                graffiti.SetFloat("_AlphaClip", 1f);
                graffiti.SetFloat("_Cutoff", .18f);
                graffiti.SetFloat("_Cull", (float)CullMode.Back);
                graffiti.EnableKeyword("_ALPHATEST_ON");
                graffiti.SetOverrideTag("RenderType", "TransparentCutout");
                graffiti.renderQueue = (int)RenderQueue.AlphaTest;
                return graffiti;
            }
        }
    }
}

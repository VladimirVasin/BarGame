using System;
using UnityEngine;
using Object = UnityEngine.Object;

namespace BarPromenade
{
    /// <summary>
    /// Places the complete passive, collider-free pub exterior. City and the
    /// bounded Home exterior share the same fixed-metre authored building;
    /// gameplay entrances and logical collision remain plan-owned.
    ///
    /// Its source origin is the gameplay door, local +X faces the street and
    /// local Z runs across the frontage. The imported door anchor is measured
    /// in world space before the FBX hierarchy is flattened, so the importer's
    /// 100/0.01 unit conversion cannot move the building away from the route.
    /// </summary>
    public static class CityBarFacadeWorldBuilder
    {
        private static readonly int BaseColorId =
            Shader.PropertyToID("_BaseColor");
        private static readonly int ColorId =
            Shader.PropertyToID("_Color");
        private static readonly int BaseMapId =
            Shader.PropertyToID("_BaseMap");
        private static readonly int SmoothnessId =
            Shader.PropertyToID("_Smoothness");
        private static readonly int MetallicId =
            Shader.PropertyToID("_Metallic");

        public static void BuildCity(
            Transform parent,
            BuildingLot lot)
        {
            Validate(parent, lot);
            Build(
                parent,
                lot.DoorPosition,
                ResolveDirection(lot),
                lot.BarId);
        }

        public static void BuildHomeExterior(
            Transform parent,
            HomeExteriorContextPlan context,
            BuildingLot lot)
        {
            if (context == null)
            {
                throw new ArgumentNullException(nameof(context));
            }

            Validate(parent, lot);
            Build(
                parent,
                PlayerHomeBalconyGeometry.ToHomeLocal(
                    context.PlayerHome,
                    lot.DoorPosition),
                PlayerHomeBalconyGeometry.ToHomeLocalDirection(
                    context.PlayerHome,
                    ResolveDirection(lot)),
                lot.BarId);
        }

        private static void Build(
            Transform parent,
            Vector3 doorPosition,
            Vector3 direction,
            string barId)
        {
            direction.y = 0f;
            direction.Normalize();

            GameObject prefab = BarModelResources.LoadFacadePrefab();
            if (prefab == null)
            {
                throw new InvalidOperationException(
                    "The bar exterior model is missing. Run " +
                    "tools/build-bar-3d-model.py through Blender, then " +
                    "Bar Promenade/Bar/Build Runtime Prefabs.");
            }

            GameObject instance = Object.Instantiate(prefab, parent);
            instance.name = $"Bar Exterior {barId}";
            instance.transform.localPosition = doorPosition;
            // The model is authored with +X outward. LookRotation maps local
            // +Z to its first argument, so direction x up is the forward that
            // makes the imported local +X land on the lot frontage.
            instance.transform.localRotation =
                Quaternion.LookRotation(
                    Vector3.Cross(direction, Vector3.up),
                    Vector3.up);
            instance.transform.localScale = Vector3.one;

            BarAssetRegistry registry =
                instance.GetComponent<BarAssetRegistry>();
            if (registry == null)
            {
                throw new InvalidOperationException(
                    "The bar exterior prefab has no BarAssetRegistry.");
            }

            if (!registry.TryGetAnchor(
                    "exterior_door",
                    out Transform doorAnchor))
            {
                throw new InvalidOperationException(
                    "The bar exterior has no exterior_door anchor.");
            }

            // Never read localPosition from an FBX anchor. Unity imports the
            // authoring root at 100 and each child at 0.01; only the measured
            // world point survives that split unchanged.
            Vector3 targetDoor = parent.TransformPoint(doorPosition);
            instance.transform.position += targetDoor - doorAnchor.position;

            if (!registry.TryGetAnchor(
                    "sign_pivot",
                    out Transform signAnchor))
            {
                throw new InvalidOperationException(
                    "The bar exterior has no sign_pivot anchor.");
            }

            Transform marker = BuildMarker(
                parent,
                barId,
                parent.InverseTransformPoint(signAnchor.position),
                Quaternion.Inverse(parent.rotation) *
                instance.transform.rotation,
                out BarBuildingMarker markerComponent);
            Vector3 signOffset =
                signAnchor.position - doorAnchor.position;

            foreach (BarPartBinding binding in registry.Parts)
            {
                Renderer renderer = binding?.Renderer;
                if (renderer == null)
                {
                    continue;
                }

                Transform part = renderer.transform;
                bool sign = binding.Group != null &&
                    binding.Group.StartsWith(
                        BarAssetRegistry.PivotGroupPrefix,
                        StringComparison.Ordinal);
                Transform destination = sign && marker != null
                    ? marker
                    : parent;
                if (sign && marker == null)
                {
                    Object.DestroyImmediate(part.gameObject);
                    continue;
                }

                if (sign)
                {
                    // Sign meshes are authored around their own zero and the
                    // anchor carries the hanging point. Move them to that
                    // point before preserving their imported world scale.
                    part.position += signOffset;
                }

                //  KeepWorld: an imported FBX splits its unit conversion
                //  across the hierarchy - the authoring root arrives scaled
                //  100 and every part scaled 0.01 - so lifting a part out
                //  without preserving its world transform shrinks it by a
                //  hundred.
                part.SetParent(destination, true);
                ApplyAppearance(binding, renderer);

                if (sign && markerComponent != null)
                {
                    markerComponent.RegisterSignPart(
                        part.gameObject,
                        string.Equals(
                            binding.SourceName,
                            "Bar Sign Panel",
                            StringComparison.Ordinal));
                }
            }

            Object.DestroyImmediate(instance);
        }

        /// <summary>
        /// Hangs the blade sign's own object at the imported semantic anchor.
        /// </summary>
        private static Transform BuildMarker(
            Transform parent,
            string barId,
            Vector3 markerPosition,
            Quaternion markerRotation,
            out BarBuildingMarker markerComponent)
        {
            var markerObject = new GameObject("Bar Landmark Marker");
            markerObject.transform.SetParent(parent, false);
            markerObject.transform.localPosition = markerPosition;
            markerObject.transform.localRotation = markerRotation;
            markerComponent =
                markerObject.AddComponent<BarBuildingMarker>();
            markerComponent.Initialize(barId);
            return markerObject.transform;
        }

        private static void ApplyAppearance(
            BarPartBinding binding,
            Renderer renderer)
        {
            string role = binding.Role ?? string.Empty;
            if (role == "exterior_window_ground")
            {
                renderer.sharedMaterial =
                    CityWindowAppearance.ResolveLitMaterial(
                        CityWindowFamily.Bar);
                CityWindowAppearance.ApplyPlainPane(renderer);
                return;
            }

            uint paneHash = StableHash(binding.SourceName);
            if (role == "exterior_window_upper_warm")
            {
                renderer.sharedMaterial =
                    CityWindowAppearance.ResolveLitMaterial(
                        CityWindowFamily.Bar);
                CityWindowAppearance.ApplyLitPane(renderer, paneHash);
                return;
            }

            Color tint = binding.Tint.Resolve(default);
            if (role == "exterior_window_upper_dark")
            {
                ApplyFlat(renderer, tint, 0.18f, 0f);
                CityWindowAppearance.ApplyDarkPane(renderer, paneHash);
                return;
            }

            if (BarExteriorSurfaceAppearance.TryResolveSheet(
                    binding.Sheet,
                    out BarExteriorSurfaceKind surface))
            {
                BarExteriorSurfaceAppearance.Apply(
                    renderer,
                    surface,
                    tint);
                return;
            }

            bool metal = role == "exterior_metal";
            ApplyFlat(
                renderer,
                tint,
                metal ? 0.24f : 0.10f,
                metal ? 0.38f : 0f);
        }

        private static Vector3 ResolveDirection(BuildingLot lot)
        {
            return new Vector3(
                lot.FrontageDirection.x,
                0f,
                lot.FrontageDirection.y);
        }

        private static void ApplyFlat(
            Renderer renderer,
            Color tint,
            float smoothness,
            float metallic)
        {
            renderer.sharedMaterial = RuntimePrimitiveFactory.DefaultMaterial;
            var properties = new MaterialPropertyBlock();
            renderer.GetPropertyBlock(properties);
            properties.SetTexture(BaseMapId, Texture2D.whiteTexture);
            properties.SetColor(BaseColorId, tint);
            properties.SetColor(ColorId, tint);
            properties.SetFloat(SmoothnessId, smoothness);
            properties.SetFloat(MetallicId, metallic);
            renderer.SetPropertyBlock(properties);
        }

        private static uint StableHash(string value)
        {
            const uint offset = 2166136261u;
            const uint prime = 16777619u;
            uint hash = offset;
            string text = value ?? string.Empty;
            for (int index = 0; index < text.Length; index++)
            {
                hash ^= text[index];
                hash *= prime;
            }

            return hash;
        }

        private static void Validate(
            Transform parent,
            BuildingLot lot)
        {
            if (parent == null)
            {
                throw new ArgumentNullException(nameof(parent));
            }

            if (lot == null)
            {
                throw new ArgumentNullException(nameof(lot));
            }

            if (!lot.IsBar ||
                !lot.HasRoadFrontage)
            {
                throw new ArgumentException(
                    "A passive bar exterior requires a bar lot with street frontage.",
                    nameof(lot));
            }
        }
    }
}

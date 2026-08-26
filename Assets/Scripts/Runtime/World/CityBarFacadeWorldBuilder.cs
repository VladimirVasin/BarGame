using System;
using System.Collections.Generic;
using UnityEngine;
using Object = UnityEngine.Object;

namespace BarPromenade
{
    /// <summary>
    /// Places the passive, collider-free identity of a bar facade. City and
    /// the bounded Home exterior share this recipe; gameplay entrances remain
    /// owned by CityWorldBuilder.
    ///
    /// The geometry is one authored model
    /// (`tools/build-bar-3d-model.py`, facade asset) placed at the door and
    /// TURNED to face the street. The primitive version instead carried two
    /// hand-written size triples per part - one for an X frontage and one for
    /// a Z frontage - which are the same box rotated ninety degrees, written
    /// down twice and free to disagree.
    /// </summary>
    public static class CityBarFacadeWorldBuilder
    {
        private static readonly int BaseColorId =
            Shader.PropertyToID("_BaseColor");
        private static readonly int ColorId =
            Shader.PropertyToID("_Color");

        public static void BuildCity(
            Transform parent,
            BuildingLot lot)
        {
            Validate(parent, lot);
            Build(
                parent,
                lot.DoorPosition,
                ResolveDirection(lot),
                lot.BarId,
                null,
                1f,
                false);
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
                lot.BarId,
                null,
                1f,
                true);
        }

        private static void Build(
            Transform parent,
            Vector3 doorPosition,
            Vector3 direction,
            string barId,
            Material material,
            float colorScale,
            bool clipToHomeExterior)
        {
            direction.y = 0f;
            direction.Normalize();

            GameObject prefab = BarModelResources.LoadFacadePrefab();
            if (prefab == null)
            {
                throw new InvalidOperationException(
                    "The bar facade model is missing. Run " +
                    "tools/build-bar-3d-model.py through Blender, then " +
                    "Bar Promenade/Bar/Build Runtime Prefabs.");
            }

            GameObject instance = Object.Instantiate(prefab, parent);
            instance.name = $"Bar Facade {barId}";
            instance.transform.localPosition = doorPosition;
            //  The model is authored facing +X; one rotation replaces the
            //  whole frontageIsX branch.
            instance.transform.localRotation =
                Quaternion.LookRotation(
                    new Vector3(direction.z, 0f, -direction.x),
                    Vector3.up);
            instance.transform.localScale = Vector3.one;

            BarAssetRegistry registry =
                instance.GetComponent<BarAssetRegistry>();
            if (registry == null)
            {
                throw new InvalidOperationException(
                    "The bar facade prefab has no BarAssetRegistry.");
            }

            Vector3 signPivot = registry.TryGetAnchor(
                "sign_pivot",
                out Transform anchor)
                ? anchor.localPosition
                : new Vector3(0.74f, 3.42f, 0f);

            Transform marker = BuildMarker(
                parent,
                instance.transform,
                barId,
                doorPosition,
                signPivot,
                clipToHomeExterior,
                out BarBuildingMarker markerComponent);

            var properties = new MaterialPropertyBlock();
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

                //  KeepWorld: an imported FBX splits its unit conversion
                //  across the hierarchy - the authoring root arrives scaled
                //  100 and every part scaled 0.01 - so lifting a part out
                //  without preserving its world transform shrinks it by a
                //  hundred.
                part.SetParent(destination, true);

                if (!ClipToHome(part, clipToHomeExterior))
                {
                    Object.DestroyImmediate(part.gameObject);
                    continue;
                }

                properties.Clear();
                Color tint = ScaleColor(
                    binding.Tint.Resolve(default),
                    colorScale);
                properties.SetColor(BaseColorId, tint);
                properties.SetColor(ColorId, tint);
                renderer.SetPropertyBlock(properties);
                if (material != null)
                {
                    renderer.sharedMaterial = material;
                }

                if (sign && markerComponent != null)
                {
                    markerComponent.RegisterSignPart(part.gameObject);
                }
            }

            Object.DestroyImmediate(instance);
        }

        /// <summary>
        /// Hangs the blade sign's own object, or reports that this facade
        /// has none because the marker falls behind the home's facade.
        /// </summary>
        private static Transform BuildMarker(
            Transform parent,
            Transform facade,
            string barId,
            Vector3 doorPosition,
            Vector3 signPivot,
            bool clipToHomeExterior,
            out BarBuildingMarker markerComponent)
        {
            Vector3 markerPosition =
                facade.localRotation * signPivot + doorPosition;
            if (clipToHomeExterior &&
                markerPosition.x <=
                HomeExteriorViewBuilder.ExteriorMinimumX)
            {
                markerComponent = null;
                return null;
            }

            var markerObject = new GameObject("Bar Landmark Marker");
            markerObject.transform.SetParent(parent, false);
            markerObject.transform.localPosition = markerPosition;
            markerObject.transform.localRotation = facade.localRotation;
            markerComponent =
                markerObject.AddComponent<BarBuildingMarker>();
            markerComponent.Initialize(barId);
            return markerObject.transform;
        }

        /// <summary>
        /// Trims a part back to the home exterior's half-space, or reports
        /// that nothing of it survives.
        ///
        /// The primitive version rebuilt each box from the clipped bounds,
        /// which is a shift and a scale rather than a true cut - so doing
        /// exactly that to the authored part reproduces it.
        /// </summary>
        private static bool ClipToHome(Transform part, bool clip)
        {
            if (!clip)
            {
                return true;
            }

            Renderer renderer = part.GetComponent<Renderer>();
            if (renderer == null)
            {
                return true;
            }

            Bounds bounds = renderer.bounds;
            if (!HomeExteriorViewBuilder.TryClipToExteriorHalfSpace(
                    bounds,
                    out Bounds clipped))
            {
                return false;
            }

            if (Mathf.Approximately(bounds.size.x, clipped.size.x))
            {
                return true;
            }

            float factor = bounds.size.x > 0.0001f
                ? clipped.size.x / bounds.size.x
                : 1f;
            Vector3 scale = part.localScale;
            part.localScale = new Vector3(
                scale.x * factor,
                scale.y,
                scale.z);
            part.position += new Vector3(
                clipped.center.x - bounds.center.x,
                0f,
                0f);
            return true;
        }

        private static Vector3 ResolveDirection(BuildingLot lot)
        {
            return new Vector3(
                lot.FrontageDirection.x,
                0f,
                lot.FrontageDirection.y);
        }

        private static Color ScaleColor(
            Color color,
            float scale)
        {
            return new Color(
                color.r * scale,
                color.g * scale,
                color.b * scale,
                color.a);
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
                    "A passive bar facade requires a bar lot with street frontage.",
                    nameof(lot));
            }
        }
    }
}

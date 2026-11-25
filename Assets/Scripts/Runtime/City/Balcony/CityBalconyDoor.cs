using UnityEngine;

namespace BarPromenade
{
    // Residential facades batch their closed glass and frames. Only the occupied
    // slot needs a moving leaf. The room and reveals are part of the imported house.
    internal sealed class CityBalconyDoor
    {
        private static readonly int OpenPane = Shader.PropertyToID("_OpenBalconyPane");
        private static readonly int LeafState = Shader.PropertyToID("_BalconyLeafState");
        private readonly MaterialPropertyBlock properties = new();
        private readonly MaterialPropertyBlock leafProperties = new();
        private readonly Renderer leafGlass;
        private float lastGlassState = -1f;
        private readonly Transform opening, hinge;
        private readonly CityBuildingAssetRegistry building;
        private readonly int pane;
        private bool paneHidden;
        internal Vector3 Handle => hinge.TransformPoint(new Vector3(.665f, 1.06f, .034f));
        internal float Fraction { get; private set; }

        internal CityBalconyDoor(Transform visit, Vector3 threshold, CityBalconySmokerDescriptor descriptor)
        {
            building = CityBuildingAssetRegistry.FindBalcony(descriptor.LotCell, descriptor.BalconySlotStableId, visit.position, out pane);
            opening = new GameObject("Balcony doorway").transform;
            opening.SetParent(visit, false);
            opening.localPosition = threshold;
            hinge = new GameObject("Outward opening leaf").transform;
            hinge.SetParent(opening, false);
            hinge.localPosition = new Vector3(-.365f, 0f, .06f);
            const float w = .73f, h = 2.14f, stile = .038f;
            Part(hinge, "Painted sash", new Color(.105f,.10f,.09f), PlayerHomeExteriorSurfaceKind.PaintedWood,
                Box(stile*.5f,h*.5f,0f,stile,h,.036f), Box(w-stile*.5f,h*.5f,0f,stile,h,.036f),
                Box(w*.5f,h-stile*.5f,0f,w-2*stile,stile,.036f),
                Box(w*.5f,stile*.5f,0f,w-2*stile,stile,.036f));
            leafGlass = Part(hinge, "Balcony door glass", new Color(.18f,.21f,.21f), PlayerHomeExteriorSurfaceKind.WindowGlass,
                Box(w*.5f,h*.5f,0f,w-stile*2,h-stile*2,.012f));
            if (building != null && building.WindowGlassRenderer != null)
                leafGlass.sharedMaterial = building.WindowGlassRenderer.sharedMaterial;
            Part(hinge, "Balcony door latch", new Color(.32f,.32f,.3f), PlayerHomeExteriorSurfaceKind.PaintedMetal,
                Box(w-.063f,1.06f,.034f,.085f,.022f,.032f));
            SetOpen(0f);
        }

        internal void SetOpen(float amount)
        {
            Fraction = amount;
            bool visible = amount > 0f;
            if (paneHidden != visible && building != null && building.WindowGlassRenderer != null)
            {
                var glass = building.WindowGlassRenderer;
                glass.GetPropertyBlock(properties);
                properties.SetFloat(OpenPane, visible ? pane + 1 : 0);
                glass.SetPropertyBlock(properties);
                paneHidden = visible;
            }
            if (opening.gameObject.activeSelf != visible) opening.gameObject.SetActive(visible);
            if (!visible) return;
            hinge.localRotation = Quaternion.Euler(0f, -95f * amount, 0f);
            if (building != null && building.WindowStateBuffer != null && building.WindowStateBuffer[pane] != lastGlassState)
            {
                lastGlassState = building.WindowStateBuffer[pane];
                leafProperties.SetFloat(LeafState, lastGlassState);
                leafGlass.SetPropertyBlock(leafProperties);
            }
        }

        private static Bounds Box(float x,float y,float z,float width,float height,float depth) =>
            new Bounds(new Vector3(x,y,z), new Vector3(width,height,depth));

        private static Renderer Part(Transform parent, string name, Color tint, PlayerHomeExteriorSurfaceKind finish,
            params Bounds[] boxes)
        {
            var part = RuntimePrimitiveFactory.CreateStructuralBoxes(name, parent, boxes, tint, false, 1f,
                RuntimeWorldUvMode.BoxProjected, .003f);
            var surface = part.GetComponent<Renderer>();
            PlayerHomeExteriorSurfaceAppearance.Apply(surface, finish);
            return surface;
        }
    }
}

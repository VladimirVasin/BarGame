using System;
using UnityEngine;
using UnityEngine.Rendering;

namespace BarPromenade
{
    public static class CityGlassReturnWorld
    {
        private static Texture2D floodlightCookie;
        public static Transform Build(Transform parent, CityGlassReturnSitePlan plan, bool interactive, out Collider rainShelter)
        {
            rainShelter = null;
            if (plan == null) throw new ArgumentNullException(nameof(plan));
            if (!plan.IsEnabled) return null;
            Transform shell = CityGlassReturnAssetProvider.CreatePavilion(parent).transform;
            shell.name = "Ridge Glass Return";
            shell.localPosition = plan.Origin; shell.localRotation = plan.Rotation;
            Transform yard = CityGlassReturnAssetProvider.CreateYard(parent).transform;
            yard.localPosition = plan.YardOrigin; yard.localRotation = Quaternion.identity;

            Transform source = CityGlassReturnAssetProvider.FindPart(shell, "ANCHOR_WorkLight");
            Transform aim = CityGlassReturnAssetProvider.FindPart(shell, "ANCHOR_LightTarget");
            Transform lamp = new GameObject(CityNightAtmosphere.IndustrialTaskLightPrefix + "GlassReturnWindow").transform;
            lamp.SetParent(shell, false);
            lamp.SetPositionAndRotation(source.position, Quaternion.LookRotation(aim.position - source.position));
            var lens = CityGlassReturnAssetProvider.FindPart(shell, "LampLens").GetComponent<Renderer>();
            lens.sharedMaterial = CityNightResources.EmissiveMaterial;
            lens.shadowCastingMode = ShadowCastingMode.Off;
            CityNightGlowRegistry.Register(lens, new Color(2.6f, 2.16f, 1.52f));
            BuildFloodlight(parent, plan);
            if (!interactive) return shell;

            // Keep the wide window physically open down to the sill. A single volume
            // filling the whole shed would let the camera collide with thin air at the counter.
            Box(shell, "Plinth", new(0f,-.225f,0f), new(4.6f,.45f,3.2f));
            Box(shell, "Rear wall", new(0f,1.35f,-1.51f), new(4.6f,2.7f,.18f));
            for (int side = -1; side <= 1; side += 2)
            {
                Box(shell, "Side wall", new(side*2.21f,1.35f,0f), new(.18f,2.7f,3.2f));
                Box(shell, "Window pier", new(side*1.805f,1.35f,1.48f), new(.99f,2.7f,.18f));
                Box(yard, "Side fence", new(side*2.045f,.81f,0f), new(.08f,1.62f,3.75f));
                Box(yard, "End fence", new(0f,.81f,side*1.845f), new(4.16f,1.62f,.08f));
            }
            Box(shell, "Window sill", new(0f,.485f,1.48f), new(2.62f,.97f,.18f));
            Box(shell, "Receiving ledge", new(0f,.985f,1.535f), new(2.64f,.07f,.50f));
            Box(shell, "Window lintel", new(0f,2.45f,1.48f), new(2.62f,.50f,.18f));
            Box(shell, "Roof", new(0f,2.755f,0f), new(4.60f,.11f,3.2f));
            rainShelter = Box(shell, "Steel canopy", new(0f,2.33f,1.94f), new(3.58f,.105f,.88f));
            Box(yard, "Yard foundation", new(0f,-.225f,0f), new(4.2f,.45f,3.8f));
            shell.gameObject.AddComponent<CityGlassReturnWorker>().Initialize(shell);
            return shell;
        }

        private static void BuildFloodlight(Transform parent, CityGlassReturnSitePlan plan)
        {
            Transform fixture = CityGlassReturnAssetProvider.CreateFloodlight(parent).transform;
            fixture.name = "Glass Return Wall Floodlight";
            fixture.localPosition = plan.FloodlightWallContact + Vector3.right * .18f;
            fixture.localRotation = Quaternion.LookRotation(Vector3.right);
            Transform head = CityGlassReturnAssetProvider.FindPart(fixture, "MOVE_Head");
            Vector3 target = parent.TransformPoint(plan.FloodlightTarget);
            head.rotation = Quaternion.LookRotation(target - head.position);
            Transform source = CityGlassReturnAssetProvider.FindPart(fixture, "ANCHOR_Source");
            Transform lamp = new GameObject(CityNightAtmosphere.IndustrialTaskLightPrefix + "GlassReturnFloodlight").transform;
            lamp.SetParent(fixture, false);
            lamp.SetPositionAndRotation(source.position, head.rotation);
            var lens = CityGlassReturnAssetProvider.FindPart(fixture, "LampLens").GetComponent<Renderer>();
            lens.sharedMaterial = CityNightResources.EmissiveMaterial;
            lens.shadowCastingMode = ShadowCastingMode.Off;
            CityNightGlowRegistry.Register(lens, new Color(3.3f, 3.6f, 3.7f));
        }

        internal static Texture2D FloodlightCookie(Transform anchor, float spotAngle)
        {
            if (floodlightCookie) return floodlightCookie;
            // The wall fitting's reflector only sends light away from its mounting
            // wall. A very wide circular Spot alone also shines back down that wall,
            // making a glaring pool immediately below the source.
            const int size = 64;
            floodlightCookie = new Texture2D(size, size, TextureFormat.RGBA32, false, true)
            {
                name = "Glass Return Frosted Reflector",
                hideFlags = HideFlags.HideAndDontSave,
                filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp
            };
            var pixels = new Color32[size * size];
            Vector3 normal = anchor.InverseTransformDirection(Vector3.right);
            float tangent = Mathf.Tan(spotAngle * .5f * Mathf.Deg2Rad);
            for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                Vector3 ray = new Vector3((2f * x / (size - 1) - 1f) * tangent,
                    (2f * y / (size - 1) - 1f) * tangent, 1f).normalized;
                float outward = Vector3.Dot(normal, ray);
                byte value = (byte)Mathf.RoundToInt(255f * Mathf.SmoothStep(0f, 1f,
                    Mathf.InverseLerp(.08f, .34f, outward)));
                pixels[y * size + x] = new Color32(value, value, value, value);
            }
            floodlightCookie.SetPixels32(pixels);
            floodlightCookie.Apply(false, true);
            return floodlightCookie;
        }

        private static BoxCollider Box(Transform parent, string name, Vector3 center, Vector3 size)
        {
            var host = new GameObject(name);
            host.transform.SetParent(parent, false);
            var collider = host.AddComponent<BoxCollider>();
            collider.center = center; collider.size = size;
            return collider;
        }
    }
}

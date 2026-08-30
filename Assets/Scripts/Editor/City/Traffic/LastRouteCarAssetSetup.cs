using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace BarPromenade.EditorTools
{
    /// <summary>
    /// Turns the generated Last Route car FBX into the runtime prefab.
    ///
    /// The shape is the bus's, deliberately: manifest in, named parts bound
    /// into a registry once, materials authored here rather than imported,
    /// and a passive prefab - no collider, no Animator, no Light. The car's
    /// obstacle collider lives on the runtime root the factory builds, so the
    /// art asset can be validated as pure presentation.
    /// </summary>
    public static class LastRouteCarAssetSetup
    {
        public const string ModelPath =
            "Assets/Vehicles/Models/LastRouteCar3D.fbx";
        public const string ManifestPath =
            "Assets/Vehicles/Models/LastRouteCar3D.json";
        public const string PrefabPath =
            "Assets/Resources/Vehicles/LastRouteCar3D.prefab";
        public const string ExpectedDesignId = "last_route_ferry_car_v1";
        private const string ShaderName = "Bar Promenade/PS1 Lit";
        private const string MaterialDirectory = "Assets/Vehicles/Materials";
        private const string TextureDirectory = "Assets/Vehicles/Textures";

        private static bool isBuilding;

        public static bool IsBuilding => isBuilding;

        [MenuItem("Bar Promenade/Last Route Car 3D/Build Runtime Prefab")]
        public static void BuildMenu()
        {
            BuildOrThrow();
            Debug.Log($"Last Route car prefab rebuilt at {PrefabPath}.");
        }

        [MenuItem("Bar Promenade/Last Route Car 3D/Validate Imported Contract")]
        public static void ValidateMenu()
        {
            ValidateOrThrow();
            Debug.Log("Last Route car imported contract is valid.");
        }

        public static void QueueBuildWhenSourcesExist()
        {
            if (isBuilding)
            {
                return;
            }

            EditorApplication.delayCall += () =>
            {
                if (isBuilding ||
                    AssetDatabase.LoadAssetAtPath<GameObject>(ModelPath) == null ||
                    !File.Exists(ManifestPath))
                {
                    return;
                }

                BuildOrThrow();
            };
        }

        public static void BuildOrThrow()
        {
            isBuilding = true;
            try
            {
                Manifest manifest = LoadAndValidateManifest();
                GameObject model =
                    AssetDatabase.LoadAssetAtPath<GameObject>(ModelPath);
                if (model == null)
                {
                    throw new InvalidOperationException(
                        $"The Last Route car model is missing at {ModelPath}.");
                }

                BuildPrefab(model, manifest);
                AssetDatabase.SaveAssets();
            }
            finally
            {
                isBuilding = false;
            }
        }

        public static void ValidateOrThrow()
        {
            Manifest manifest = LoadAndValidateManifest();
            GameObject prefab =
                AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
            if (prefab == null)
            {
                throw new InvalidOperationException(
                    $"The Last Route car prefab is missing at {PrefabPath}.");
            }

            var registry = prefab.GetComponent<LastRouteCarAssetRegistry>();
            if (registry == null || !registry.IsBound)
            {
                throw new InvalidOperationException(
                    "The Last Route car prefab carries no bound registry.");
            }

            if (registry.BuildSignature != manifest.build_signature)
            {
                throw new InvalidOperationException(
                    "The Last Route car prefab is stale: its build signature " +
                    "differs from the manifest. Rebuild it.");
            }

            ValidatePrefabPresentation(prefab);
        }

        private static Manifest LoadAndValidateManifest()
        {
            if (!File.Exists(ManifestPath))
            {
                throw new InvalidOperationException(
                    $"The Last Route car manifest is missing at {ManifestPath}.");
            }

            Manifest manifest =
                JsonUtility.FromJson<Manifest>(File.ReadAllText(ManifestPath));
            if (manifest == null || manifest.design_id != ExpectedDesignId)
            {
                throw new InvalidOperationException(
                    $"The manifest design id must be '{ExpectedDesignId}'.");
            }

            if (manifest.colliders || manifest.animation_count != 0)
            {
                throw new InvalidOperationException(
                    "The Last Route car model must ship no colliders and no " +
                    "authored animation.");
            }

            if (manifest.wheel_count != 4)
            {
                throw new InvalidOperationException(
                    "The Last Route car must keep all four wheels.");
            }

            if (manifest.forward_axis != "-Y" ||
                manifest.unity_runtime_forward_axis != "+Z")
            {
                throw new InvalidOperationException(
                    "The Last Route car axis contract changed.");
            }

            return manifest;
        }

        private static void BuildPrefab(GameObject model, Manifest manifest)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(PrefabPath));
            var root = new GameObject("LastRouteCar3D");
            try
            {
                GameObject instance =
                    (GameObject)PrefabUtility.InstantiatePrefab(model);
                instance.name = "Model";
                instance.transform.SetParent(root.transform, false);
                // The generator authors forward along -Y; the runtime wants
                // +Z, exactly as the bus does.
                instance.transform.localRotation = Quaternion.Euler(0f, 180f, 0f);
                PrefabUtility.UnpackPrefabInstance(
                    instance,
                    PrefabUnpackMode.Completely,
                    InteractionMode.AutomatedAction);

                Dictionary<string, Transform> transforms =
                    IndexUniqueTransforms(instance.transform);
                Dictionary<string, Renderer> renderers =
                    IndexUniqueRenderers(instance.transform);

                var bindings = new List<LastRouteCarRendererBinding>();
                var boundRenderers = new List<Renderer>();
                foreach (ManifestPart part in manifest.parts)
                {
                    if (!renderers.TryGetValue(part.name, out Renderer renderer))
                    {
                        throw new InvalidOperationException(
                            $"The imported model has no renderer '{part.name}'.");
                    }

                    var slot = (LastRouteCarMaterialSlot)Enum.Parse(
                        typeof(LastRouteCarMaterialSlot),
                        part.material_slot);
                    renderer.sharedMaterial = ResolveMaterial(slot);
                    renderer.shadowCastingMode =
                        UnityEngine.Rendering.ShadowCastingMode.On;
                    bindings.Add(
                        new LastRouteCarRendererBinding(
                            part.name, part.role, slot, renderer));
                    boundRenderers.Add(renderer);
                }

                var registry = root.AddComponent<LastRouteCarAssetRegistry>();
                registry.Configure(
                    instance.transform,
                    RequireTransform(transforms, "ROOT_Body"),
                    RequireTransform(transforms, "PIVOT_DoorDriver"),
                    RequireTransform(transforms, "PIVOT_DoorPassenger"),
                    RequireTransform(transforms, "PIVOT_WheelFL"),
                    RequireTransform(transforms, "PIVOT_WheelFR"),
                    RequireTransform(transforms, "PIVOT_WheelRL"),
                    RequireTransform(transforms, "PIVOT_WheelRR"),
                    RequireTransform(transforms, "PIVOT_SteeringWheel"),
                    RequireTransform(transforms, "ANCHOR_SteeringGrip.L"),
                    RequireTransform(transforms, "ANCHOR_SteeringGrip.R"),
                    RequireTransform(transforms, "ANCHOR_DriverSeat"),
                    RequireTransform(transforms, "ANCHOR_PassengerSeat"),
                    RequireTransform(transforms, "ANCHOR_DriverDoorEntry"),
                    RequireTransform(transforms, "ANCHOR_PassengerDoorEntry"),
                    RequireTransform(transforms, "ANCHOR_PerchSoles"),
                    RequireTransform(transforms, "ANCHOR_PerchSeat"),
                    boundRenderers.ToArray(),
                    bindings.ToArray(),
                    CalculateLocalBounds(root.transform, boundRenderers),
                    new LastRouteCarDimensions(
                        manifest.dimensions_m.length,
                        manifest.dimensions_m.width,
                        manifest.dimensions_m.height,
                        manifest.dimensions_m.wheelbase,
                        manifest.dimensions_m.wheel_radius),
                    manifest.triangle_count,
                    manifest.generator_version,
                    manifest.design_id,
                    manifest.build_signature,
                    manifest.perch_seat_z,
                    manifest.perch_drop_m);
                registry.ConfigureDashboard(
                    RequireTransform(transforms, "PIVOT_GloveboxLid"),
                    RequireTransform(transforms, "PIVOT_RadioPowerKnob"),
                    RequireTransform(transforms, "PIVOT_RadioTuningKnob"),
                    RequireTransform(transforms, "PIVOT_RadioNeedle"),
                    RequireTransform(transforms, "PIVOT_SpeedoNeedle"),
                    RequireManifestPivot(manifest, "PIVOT_RadioNeedle", "+X")
                        .travel_m,
                    RequireRoleRenderer(bindings, "radio_dial"));
                RequireManifestPivot(manifest, "PIVOT_GloveboxLid", "+X");
                RequireManifestPivot(manifest, "PIVOT_RadioPowerKnob", "+Y");
                RequireManifestPivot(manifest, "PIVOT_RadioTuningKnob", "+Y");
                RequireManifestPivot(manifest, "PIVOT_SpeedoNeedle", "+Y");

                ValidateRegistryBindings(registry, manifest);
                ValidatePrefabPresentation(root);
                PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(root);
            }
        }

        private static void ValidateRegistryBindings(
            LastRouteCarAssetRegistry registry,
            Manifest manifest)
        {
            if (!registry.IsBound)
            {
                throw new InvalidOperationException(
                    "The Last Route car registry is missing a binding.");
            }

            // The grips have to ride the rim, or a later turn of the wheel
            // strands the driver's hands in mid-air.
            if (registry.LeftSteeringGrip.parent != registry.SteeringWheelPivot ||
                registry.RightSteeringGrip.parent != registry.SteeringWheelPivot)
            {
                throw new InvalidOperationException(
                    "Both steering grips must be children of the wheel pivot.");
            }

            // The seats must face each other across the car. This is the
            // predicate the future ride plan will check at runtime, proved
            // here so it can never be discovered in a scene instead.
            //
            // Axes come from the REGISTRY'S OWN transform, never from the
            // imported Body node. The generator authors forward along source
            // -Y and the prefab build rotates the model 180 degrees to reach
            // the runtime's +Z, so the imported node's forward is not the
            // car's forward - this project has now been bitten by that on
            // the bus body, the wheels, the wipers and here.
            Vector3 body = registry.transform.position;
            Vector3 right = registry.transform.right;
            float driverSide = Vector3.Dot(
                registry.DriverSeatAnchor.position - body, right);
            float passengerSide = Vector3.Dot(
                registry.PassengerSeatAnchor.position - body, right);
            if (driverSide * passengerSide >= 0f)
            {
                throw new InvalidOperationException(
                    "The driver and passenger seats sit on the same side.");
            }

            if (Mathf.Abs(Mathf.Abs(driverSide) - Mathf.Abs(passengerSide)) > 0.001f)
            {
                throw new InvalidOperationException(
                    "The two seats are not mirrored across the body.");
            }

            // Headroom is measured against the hero's seated band, not the
            // Ferryman's: the hero reuses the bus clips verbatim, so his pose
            // is the one that actually has to fit under this roof.
            if (manifest.seated_headroom_m < 0.99f ||
                manifest.seated_headroom_m > 1.10f)
            {
                throw new InvalidOperationException(
                    $"Seated headroom {manifest.seated_headroom_m:F3}m is " +
                    "outside the shared rig's 0.99-1.10m band.");
            }

            // He sits on the bonnet with his boots ahead of him on the
            // bumper, facing out over the nose. Both anchors have to be in
            // front of the cabin or he is sitting on the roof.
            Vector3 forward = registry.transform.forward;
            float seatAhead = Vector3.Dot(
                registry.PerchSeatAnchor.position - body, forward);
            float solesAhead = Vector3.Dot(
                registry.PerchSolesAnchor.position - body, forward);
            if (seatAhead <= 0f || solesAhead <= seatAhead)
            {
                throw new InvalidOperationException(
                    "The perch must sit on the bonnet with the boots ahead " +
                    "of it on the bumper.");
            }

            // The dash. The lid is on the passenger's side and under the
            // dash top; the radio and its needle sit on the centre line; the
            // speedometer is the driver's. Same axes as above - the
            // registry's own, never an imported node's.
            Vector3 up = registry.transform.up;
            float passengerSign = passengerSide >= 0f ? 1f : -1f;
            float lidSide = Vector3.Dot(
                registry.GloveboxLidPivot.position - body, right);
            if (lidSide * passengerSign <= 0f)
            {
                throw new InvalidOperationException(
                    "The glovebox lid must hang on the passenger's side.");
            }

            if (Vector3.Dot(registry.GloveboxLidPivot.position - body, up) >=
                manifest.dashboard_top_z)
            {
                throw new InvalidOperationException(
                    "The glovebox lid hinges above the dash top.");
            }

            foreach (Transform centred in new[]
                     {
                         registry.RadioPowerKnobPivot,
                         registry.RadioTuningKnobPivot,
                         registry.RadioNeedlePivot
                     })
            {
                if (Mathf.Abs(Vector3.Dot(centred.position - body, right)) >
                    0.15f)
                {
                    throw new InvalidOperationException(
                        $"{centred.name} is not on the radio's centre line.");
                }
            }

            if (registry.RadioNeedleTravel <= 0f)
            {
                throw new InvalidOperationException(
                    "The radio needle has no travel to slide along.");
            }

            float speedoSide = Vector3.Dot(
                registry.SpeedoNeedlePivot.position - body, right);
            if (speedoSide * passengerSign >= 0f)
            {
                throw new InvalidOperationException(
                    "The speedometer must sit in front of the driver.");
            }

            if (registry.RadioDialRenderer == null)
            {
                throw new InvalidOperationException(
                    "The radio dial renderer is unbound.");
            }
        }

        private static void ValidatePrefabPresentation(GameObject root)
        {
            if (root.GetComponentInChildren<Collider>(true) != null)
            {
                throw new InvalidOperationException(
                    "The Last Route car prefab must carry no collider; the " +
                    "obstacle box belongs on the runtime root.");
            }

            if (root.GetComponentInChildren<Animator>(true) != null ||
                root.GetComponentInChildren<Light>(true) != null ||
                root.GetComponentInChildren<AudioSource>(true) != null)
            {
                throw new InvalidOperationException(
                    "The Last Route car prefab must be pure presentation.");
            }
        }

        private static Material ResolveMaterial(LastRouteCarMaterialSlot slot)
        {
            Directory.CreateDirectory(MaterialDirectory);
            string path = $"{MaterialDirectory}/LastRouteCar{slot}.mat";
            Material material = AssetDatabase.LoadAssetAtPath<Material>(path);
            Shader shader = Shader.Find(ShaderName);
            if (shader == null)
            {
                throw new InvalidOperationException(
                    $"The '{ShaderName}' shader is unavailable.");
            }

            if (material == null)
            {
                material = new Material(shader);
                AssetDatabase.CreateAsset(material, path);
            }

            material.shader = shader;
            material.SetColor("_BaseColor", GetMaterialColor(slot));
            material.SetFloat("_Smoothness", GetMaterialSmoothness(slot));
            material.SetFloat("_Metallic", GetMaterialMetallic(slot));
            material.enableInstancing = true;
            ApplyAlbedo(material, slot);
            ApplySurfaceMode(material, slot);
            ApplyEmission(material, slot);
            EditorUtility.SetDirty(material);
            return material;
        }

        /// <summary>
        /// Hangs the generated albedo sheet on the material. The sheets are
        /// light and the shader multiplies them by the base colour, so the
        /// hue stays where it was authored and the texture only adds the
        /// wear: chalked lacquer, rust scale, brittle lining, worn cloth.
        /// A slot with no sheet keeps its flat colour, which is right for
        /// glass and for the lamps.
        /// </summary>
        private static void ApplyAlbedo(
            Material material,
            LastRouteCarMaterialSlot slot)
        {
            string path = TexturePath(slot);
            Texture2D albedo = path == null
                ? null
                : AssetDatabase.LoadAssetAtPath<Texture2D>(path);
            if (path != null && albedo == null)
            {
                throw new InvalidOperationException(
                    $"The car albedo '{path}' is missing. Run " +
                    "tools/build-last-route-car-textures.py.");
            }

            if (material.HasProperty("_BaseMap"))
            {
                material.SetTexture("_BaseMap", albedo);
                material.SetTextureScale("_BaseMap", Vector2.one);
                material.SetTextureOffset("_BaseMap", Vector2.zero);
            }

            if (material.HasProperty("_MainTex"))
            {
                material.SetTexture("_MainTex", albedo);
            }
        }

        private static string TexturePath(LastRouteCarMaterialSlot slot)
        {
            switch (slot)
            {
                case LastRouteCarMaterialSlot.Body:
                case LastRouteCarMaterialSlot.AccentPaint:
                    return $"{TextureDirectory}/LastRouteCarPaintAlbedo.png";
                case LastRouteCarMaterialSlot.Rust:
                    return $"{TextureDirectory}/LastRouteCarRustAlbedo.png";
                case LastRouteCarMaterialSlot.Interior:
                case LastRouteCarMaterialSlot.Dashboard:
                    return $"{TextureDirectory}/LastRouteCarInteriorAlbedo.png";
                case LastRouteCarMaterialSlot.Seat:
                    return $"{TextureDirectory}/LastRouteCarSeatAlbedo.png";
                case LastRouteCarMaterialSlot.Trim:
                case LastRouteCarMaterialSlot.Chrome:
                case LastRouteCarMaterialSlot.Metal:
                case LastRouteCarMaterialSlot.Rubber:
                case LastRouteCarMaterialSlot.Plate:
                    // A chrome bumper is a chrome bumper; the bus already
                    // owns this sheet and there is nothing car-specific to
                    // say about it.
                    return "Assets/Vehicles/Textures/CityBusMetalAlbedo.png";
                default:
                    return null;
            }
        }

        /// <summary>
        /// The headlights are the one working thing on this car, and they
        /// have to carry through fog at 640x360 - a lit lens says someone is
        /// waiting in a way a parked shape never does. The tail lamps glow
        /// far more faintly: they are reflectors catching what the fog
        /// throws back, not lamps.
        /// </summary>
        private static void ApplyEmission(
            Material material,
            LastRouteCarMaterialSlot slot)
        {
            float strength = GetEmissionStrength(slot);
            if (strength <= 0f)
            {
                if (material.HasProperty("_EmissionColor"))
                {
                    material.SetColor("_EmissionColor", Color.black);
                }

                material.DisableKeyword("_EMISSION");
                material.globalIlluminationFlags =
                    MaterialGlobalIlluminationFlags.EmissiveIsBlack;
                return;
            }

            if (material.HasProperty("_EmissionColor"))
            {
                material.SetColor(
                    "_EmissionColor",
                    GetMaterialColor(slot) * strength);
            }

            material.EnableKeyword("_EMISSION");
            material.globalIlluminationFlags =
                MaterialGlobalIlluminationFlags.RealtimeEmissive;
        }

        private static float GetEmissionStrength(
            LastRouteCarMaterialSlot slot)
        {
            switch (slot)
            {
                case LastRouteCarMaterialSlot.Headlight: return 6.5f;
                case LastRouteCarMaterialSlot.TailLight: return 1.4f;
                // The dial keeps its emission keyword ON so the runtime can
                // light it through a property block when the radio is
                // switched on; the block writes black until then. A zero
                // here would strip the keyword and the dial could never
                // light at all.
                case LastRouteCarMaterialSlot.RadioDial: return 1.6f;
                default: return 0f;
            }
        }

        /// <summary>
        /// The glass has to be glass. URP Lit ships opaque, so a colour with
        /// alpha in it does nothing until the surface mode, the blend pair,
        /// the depth write and the queue all agree - the bus learned this
        /// the same way. The hero will sit behind these panes and look out.
        /// </summary>
        private static void ApplySurfaceMode(
            Material material,
            LastRouteCarMaterialSlot slot)
        {
            bool transparent =
                slot == LastRouteCarMaterialSlot.Glass ||
                slot == LastRouteCarMaterialSlot.CrackedGlass;
            SetFloatIfPresent(material, "_Surface", transparent ? 1f : 0f);
            SetFloatIfPresent(material, "_Blend", 0f);
            SetFloatIfPresent(
                material,
                "_SrcBlend",
                transparent ? (float)BlendMode.SrcAlpha : (float)BlendMode.One);
            SetFloatIfPresent(
                material,
                "_DstBlend",
                transparent
                    ? (float)BlendMode.OneMinusSrcAlpha
                    : (float)BlendMode.Zero);
            SetFloatIfPresent(material, "_ZWrite", transparent ? 0f : 1f);
            if (transparent)
            {
                material.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
                material.renderQueue = (int)RenderQueue.Transparent;
                // A pane that casts a solid shadow is the tell that it is
                // not really transparent.
                material.SetShaderPassEnabled("ShadowCaster", false);
            }
            else
            {
                material.DisableKeyword("_SURFACE_TYPE_TRANSPARENT");
                material.renderQueue = -1;
                material.SetShaderPassEnabled("ShadowCaster", true);
            }
        }

        private static void SetFloatIfPresent(
            Material material,
            string propertyName,
            float value)
        {
            if (material.HasProperty(propertyName))
            {
                material.SetFloat(propertyName, value);
            }
        }

        private static Color GetMaterialColor(LastRouteCarMaterialSlot slot)
        {
            switch (slot)
            {
                case LastRouteCarMaterialSlot.Body: return Hex("#3B382FFF");
                case LastRouteCarMaterialSlot.AccentPaint: return Hex("#2E3B2FFF");
                case LastRouteCarMaterialSlot.Rust: return Hex("#4A2C1CFF");
                case LastRouteCarMaterialSlot.Trim: return Hex("#1B1C1BFF");
                case LastRouteCarMaterialSlot.Chrome: return Hex("#6E7370FF");
                case LastRouteCarMaterialSlot.Rubber: return Hex("#121312FF");
                case LastRouteCarMaterialSlot.Metal: return Hex("#3A3E3CFF");
                case LastRouteCarMaterialSlot.Glass: return Hex("#2A4245A0");
                case LastRouteCarMaterialSlot.CrackedGlass: return Hex("#9AA29ECC");
                case LastRouteCarMaterialSlot.BrokenGlass: return Hex("#171917FF");
                case LastRouteCarMaterialSlot.Interior: return Hex("#1E1E1BFF");
                case LastRouteCarMaterialSlot.Seat: return Hex("#3A3125FF");
                case LastRouteCarMaterialSlot.Dashboard: return Hex("#1A1A18FF");
                case LastRouteCarMaterialSlot.Headlight: return Hex("#FFF6D2FF");
                case LastRouteCarMaterialSlot.TailLight: return Hex("#7A1310FF");
                case LastRouteCarMaterialSlot.Plate: return Hex("#8E8E82FF");
                case LastRouteCarMaterialSlot.RadioDial: return Hex("#E9A24CFF");
                default:
                    throw new ArgumentOutOfRangeException(nameof(slot));
            }
        }

        private static float GetMaterialSmoothness(LastRouteCarMaterialSlot slot)
        {
            switch (slot)
            {
                case LastRouteCarMaterialSlot.Chrome: return 0.62f;
                case LastRouteCarMaterialSlot.Glass:
                case LastRouteCarMaterialSlot.CrackedGlass: return 0.74f;
                case LastRouteCarMaterialSlot.Headlight: return 0.68f;
                case LastRouteCarMaterialSlot.Rust: return 0.06f;
                case LastRouteCarMaterialSlot.Rubber: return 0.08f;
                case LastRouteCarMaterialSlot.Seat: return 0.12f;
                case LastRouteCarMaterialSlot.RadioDial: return 0.40f;
                default: return 0.26f;
            }
        }

        private static float GetMaterialMetallic(LastRouteCarMaterialSlot slot)
        {
            switch (slot)
            {
                case LastRouteCarMaterialSlot.Chrome: return 0.70f;
                case LastRouteCarMaterialSlot.Metal: return 0.55f;
                default: return 0f;
            }
        }

        private static Color Hex(string value)
        {
            if (!ColorUtility.TryParseHtmlString(value, out Color color))
            {
                throw new ArgumentException($"Bad colour '{value}'.");
            }

            return color;
        }

        private static Bounds CalculateLocalBounds(
            Transform root,
            IReadOnlyList<Renderer> renderers)
        {
            var bounds = new Bounds(Vector3.zero, Vector3.zero);
            bool started = false;
            foreach (Renderer renderer in renderers)
            {
                Bounds world = renderer.bounds;
                Vector3 center = root.InverseTransformPoint(world.center);
                var local = new Bounds(center, world.size);
                if (!started)
                {
                    bounds = local;
                    started = true;
                    continue;
                }

                bounds.Encapsulate(local);
            }

            return bounds;
        }

        private static Dictionary<string, Transform> IndexUniqueTransforms(
            Transform root)
        {
            var result = new Dictionary<string, Transform>(StringComparer.Ordinal);
            foreach (Transform transform in root.GetComponentsInChildren<Transform>(true))
            {
                if (result.ContainsKey(transform.name))
                {
                    continue;
                }

                result[transform.name] = transform;
            }

            return result;
        }

        private static Dictionary<string, Renderer> IndexUniqueRenderers(
            Transform root)
        {
            var result = new Dictionary<string, Renderer>(StringComparer.Ordinal);
            foreach (Renderer renderer in root.GetComponentsInChildren<Renderer>(true))
            {
                if (result.ContainsKey(renderer.name))
                {
                    continue;
                }

                result[renderer.name] = renderer;
            }

            return result;
        }

        /// <summary>
        /// A control pivot as the generator wrote it, with the runtime axis
        /// the runtime is about to assume. The bus checks its door button
        /// the same way.
        /// </summary>
        private static ManifestPivot RequireManifestPivot(
            Manifest manifest,
            string name,
            string runtimeAxisLocal)
        {
            foreach (ManifestPivot pivot in manifest.pivots)
            {
                if (pivot.name != name)
                {
                    continue;
                }

                if (pivot.runtime_axis_local != runtimeAxisLocal)
                {
                    throw new InvalidOperationException(
                        $"Pivot '{name}' declares axis " +
                        $"'{pivot.runtime_axis_local}', expected " +
                        $"'{runtimeAxisLocal}'.");
                }

                return pivot;
            }

            throw new InvalidOperationException(
                $"The manifest has no pivot '{name}'.");
        }

        private static Renderer RequireRoleRenderer(
            List<LastRouteCarRendererBinding> bindings,
            string role)
        {
            Renderer found = null;
            foreach (LastRouteCarRendererBinding binding in bindings)
            {
                if (binding.Role != role)
                {
                    continue;
                }

                if (found != null)
                {
                    throw new InvalidOperationException(
                        $"More than one part carries the role '{role}'.");
                }

                found = binding.Renderer;
            }

            if (found == null)
            {
                throw new InvalidOperationException(
                    $"No part carries the role '{role}'.");
            }

            return found;
        }

        private static Transform RequireTransform(
            Dictionary<string, Transform> transforms,
            string name)
        {
            if (!transforms.TryGetValue(name, out Transform transform))
            {
                throw new InvalidOperationException(
                    $"The imported Last Route car has no transform '{name}'.");
            }

            return transform;
        }

        [Serializable]
        private sealed class Manifest
        {
            public string generator_version;
            public string design_id;
            public string forward_axis;
            public string unity_runtime_forward_axis;
            public ManifestDimensions dimensions_m;
            public int triangle_count;
            public bool colliders;
            public int animation_count;
            public int wheel_count;
            public int hubcap_count;
            public float perch_seat_z;
            public float perch_soles_z;
            public float perch_drop_m;
            public float seated_headroom_m;
            public float dashboard_top_z;
            public string build_signature;
            public ManifestPart[] parts;
            public ManifestPivot[] pivots;
        }

        [Serializable]
        private sealed class ManifestDimensions
        {
            public float length;
            public float width;
            public float height;
            public float wheelbase;
            public float wheel_radius;
        }

        [Serializable]
        private sealed class ManifestPart
        {
            public string name;
            public string role;
            public string material_slot;
            public string parent;
            public int vertices;
            public int triangles;
        }

        [Serializable]
        private sealed class ManifestPivot
        {
            public string name;
            public string role;
            public string parent;
            public float[] local_position;
            public float[] local_rotation_degrees;
            public string runtime_axis_local;
            public float travel_m;
        }
    }
}

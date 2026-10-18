using System;
using System.Collections.Generic;
using UnityEngine;

namespace BarPromenade
{
    /// <summary>CombatTest-only authored suspension bank on the retained ordinary worker rig.</summary>
    public static class CombatCraneAssetProvider
    {
        public const string IdleClip = "CraneIdle", SignalClip = "CraneSignal", DescendClip = "CraneDescend",
            StrikeClip = "CraneStrike", RecoverClip = "CraneRecover", AscendClip = "CraneAscend",
            TransportClip = "CraneTransport", StartClip = "CraneStart", BrakeClip = "CraneBrake", HurtClip = "CraneHurt";
        public const float ContactStartSeconds = .35f, ContactEndSeconds = .55f;
        public const float BodyPoseScale = 1.22f;
        public const float CableLengthMetres = 1f;
        public const float HookImpactRadius = .18f, HookImpactOffsetMetres = .48f, HookMassKilograms = 9f;
        public const float HookCoreRadius = .07f, HookCoreLength = .20f, WristSocketRadius = .09f, WristCableExitMetres = .11f;
        public const string HookCableAnchor = "ANCHOR_Cable", HookImpactAnchor = "ANCHOR_Impact", WristCableAnchor = "ANCHOR_CableExit";
        public const string HeadSwivelAnchor = "PIVOT_CableSwivel";
        public static readonly Vector3[] HookContactPoints = { new Vector3(0f, .18f, 0f), new Vector3(-.10f, .36f, 0f),
            new Vector3(-.08f, .50f, 0f), new Vector3(.06f, .54f, 0f), new Vector3(.14f, .43f, 0f) };
        public static readonly float[] HookContactRadii = { .05f, .065f, .07f, .06f, .07f };
        public static readonly string[] RightHandRendererNames = {
            "GEO_HandPalm.R", "GEO_HandFinger0.R", "GEO_HandFinger1.R", "GEO_HandFinger2.R", "GEO_HandFinger3.R", "GEO_HandThumb.R",
            "CLO_GlovePalm.R", "CLO_GloveFinger0.R", "CLO_GloveFinger1.R", "CLO_GloveFinger2.R", "CLO_GloveFinger3.R", "CLO_GloveThumb.R" };
        public const float RightHookGrip = 1f, LeftHookGrip = .82f;
        public static readonly string[] ClipNames = { IdleClip, SignalClip, DescendClip, StrikeClip, RecoverClip, AscendClip,
            TransportClip, StartClip, BrakeClip, HurtClip };
        public static readonly string[] SuspensionBoneNames = { "hand.L", "hand.R", "pelvis", "foot.L", "foot.R", "head" };
        // Metres in each bone's orientation frame, applied after its world
        // rotation; these are not imported centimetre-space TransformPoint inputs.
        public static readonly Vector3[] SuspensionLocalOffsets = { Vector3.zero, Vector3.zero, Vector3.zero, Vector3.zero, Vector3.zero, new Vector3(0f, .25f * BodyPoseScale, 0f) };
        public static readonly string[] HeadAnchorNames = { "ANCHOR_CableLeft", "ANCHOR_CableRight", "ANCHOR_CableRear",
            "ANCHOR_CableFootLeft", "ANCHOR_CableFootRight", "ANCHOR_CableHead" };
        private static readonly Dictionary<string, AnimationClip> Clips = new Dictionary<string, AnimationClip>(10);
        private static GameObject suspensionHead, cableSegment, rails, bridge, hook, wristSocket;
        private static GameObject bodyModel;
        private static BodyManifest bodyManifest;
        private static Texture2D bodyAtlas, bodyResponse;
        private static Material bodyMaterial, carapaceMaterial, metalMaterial;
        private static readonly Dictionary<string, SkinnedMeshRenderer> BodyRenderers =
            new Dictionary<string, SkinnedMeshRenderer>(StringComparer.Ordinal);
        private static readonly Dictionary<string, MeshFilter> PassiveSurfaces =
            new Dictionary<string, MeshFilter>(StringComparer.Ordinal);
        private static AudioClip winchSound, releaseSound, railSound, brakeSound, bodySound, startSound, hurtSound;

        public static bool IsLoop(string name) => name == IdleClip || name == DescendClip || name == AscendClip || name == TransportClip;
        public static float ClipDuration(string name) => name switch
        {
            IdleClip => 2f, SignalClip => .65f, DescendClip or RecoverClip or AscendClip => 1f,
            StrikeClip => 1.1f, TransportClip => 2f, StartClip => .6f, BrakeClip => .7f, HurtClip => .55f,
            _ => throw new ArgumentOutOfRangeException(nameof(name), name, "Unknown crane action.")
        };

        public static AnimationClip LoadClip(string name)
        {
            if (Clips.Count == 0)
            {
                foreach (AnimationClip clip in Resources.LoadAll<AnimationClip>("Combat/CraneActions"))
                {
                    if (Array.IndexOf(ClipNames, clip.name) < 0) continue;
                    if (clip.events.Length != 0 || clip.isLooping != IsLoop(clip.name) ||
                        Mathf.Abs(clip.length - ClipDuration(clip.name)) > .003f)
                        throw new InvalidOperationException("Invalid in-place crane action " + clip.name +
                            "; events=" + clip.events.Length + " (expected 0)" +
                            "; loop=" + clip.isLooping + " (expected " + IsLoop(clip.name) + ")" +
                            "; seconds=" + clip.length.ToString("R", System.Globalization.CultureInfo.InvariantCulture) +
                            " (expected " + ClipDuration(clip.name).ToString("R", System.Globalization.CultureInfo.InvariantCulture) + ")" +
                            "; sampleRate=" + clip.frameRate.ToString("R", System.Globalization.CultureInfo.InvariantCulture));
                    Clips.Add(clip.name, clip);
                }
                if (Clips.Count != ClipNames.Length) throw new InvalidOperationException("Crane action bank is incomplete.");
            }
            if (!Clips.TryGetValue(name, out AnimationClip action))
                throw new ArgumentOutOfRangeException(nameof(name), name, "Unknown crane action.");
            return action;
        }

        public static void ApplyAppearance(GameObject npc, int variant)
        {
            if (npc == null) throw new ArgumentNullException(nameof(npc));
            (npc.GetComponent<CombatCraneAppearance>() ?? npc.AddComponent<CombatCraneAppearance>()).Configure();
        }

        [Serializable] public sealed class BodyManifest
        {
            public string design_id, rig, geometry_owner, bounds_space;
            public bool retained_rest_skeleton;
            public int uv_channel;
            public long estimated_gpu_bytes;
            public BodyMesh[] meshes;
            public PassiveMesh[] passive_meshes;
            public HardwareMesh[] hardware;
            public BodyTexture[] textures;
        }
        [Serializable] public sealed class BodyMesh
        {
            public string name;
            public bool visible;
            public string[] surfaces;
            public float[] bounds_min, bounds_max;
        }
        [Serializable] public sealed class PassiveMesh { public string name, model; }
        [Serializable] public sealed class HardwareMesh { public string name, bone; }
        [Serializable] public sealed class BodyTexture { public string name, sha256; public int size; public bool sRGB; }

        public static BodyManifest LoadBodyManifest()
        {
            if (bodyManifest != null) return bodyManifest;
            TextAsset source = Resources.Load<TextAsset>("Combat/CraneBody");
            if (source == null) throw new InvalidOperationException("Missing authored crane surface manifest.");
            BodyManifest result = JsonUtility.FromJson<BodyManifest>(source.text);
            if (result == null || result.design_id != "crane_body_surface_v1" || result.rig != "NpcHumanV2" ||
                !result.retained_rest_skeleton || result.geometry_owner != "independent_sculpt" ||
                result.bounds_space != "unity_metres" || result.uv_channel != 0 || result.meshes == null ||
                result.meshes.Length == 0 || result.meshes.Length > 128 || result.textures == null || result.textures.Length != 2 ||
                result.passive_meshes == null || result.passive_meshes.Length == 0 || result.passive_meshes.Length > 128)
                throw new InvalidOperationException("Crane body surface manifest violates its retained rig contract.");
            var names = new HashSet<string>(StringComparer.Ordinal);
            foreach (BodyMesh mesh in result.meshes)
            {
                if (mesh == null || string.IsNullOrWhiteSpace(mesh.name) || !mesh.name.StartsWith("GEO_", StringComparison.Ordinal) || !names.Add(mesh.name) ||
                    mesh.surfaces == null || mesh.surfaces.Length == 0 || mesh.surfaces.Length > 3 ||
                    mesh.bounds_min == null || mesh.bounds_min.Length != 3 || mesh.bounds_max == null || mesh.bounds_max.Length != 3)
                    throw new InvalidOperationException("Crane body requires unique original anatomical renderer names.");
                foreach (string surface in mesh.surfaces)
                    if (surface != "flesh" && surface != "carapace" && surface != "steel")
                        throw new InvalidOperationException("Unknown authored crane surface role: " + surface);
                for (int axis = 0; axis < 3; axis++)
                    if (!float.IsFinite(mesh.bounds_min[axis]) || !float.IsFinite(mesh.bounds_max[axis]) || mesh.bounds_min[axis] > mesh.bounds_max[axis])
                        throw new InvalidOperationException("Crane body requires finite authored mesh bounds: " + mesh.name);
            }
            foreach (PassiveMesh mesh in result.passive_meshes)
                if (mesh == null || string.IsNullOrWhiteSpace(mesh.name) || string.IsNullOrWhiteSpace(mesh.model) || !names.Add(mesh.name))
                    throw new InvalidOperationException("Crane passive surfaces require unique source mesh and model names.");
            if (result.hardware != null)
            {
                if (result.hardware.Length > 16) throw new InvalidOperationException("Crane fittings exceed their authored bound.");
                foreach (HardwareMesh mesh in result.hardware)
                    if (mesh == null || string.IsNullOrWhiteSpace(mesh.name) || string.IsNullOrWhiteSpace(mesh.bone) || !names.Add(mesh.name))
                        throw new InvalidOperationException("Crane fittings require unique meshes and retained bone names.");
            }
            names.Clear();
            foreach (BodyTexture texture in result.textures)
            {
                bool albedo = texture != null && texture.name == "CraneBodyAtlas";
                if (texture == null || !names.Add(texture.name) || !albedo && texture.name != "CraneMaterialResponse" ||
                    texture.size != (albedo ? 2048 : 1024) || texture.sRGB != albedo || string.IsNullOrEmpty(texture.sha256) || texture.sha256.Length != 64)
                    throw new InvalidOperationException("Crane body requires its unique albedo and registered linear response.");
            }
            bodyManifest = result;
            return result;
        }

        public static SkinnedMeshRenderer LoadBodyRenderer(string name)
        {
            if (bodyModel == null)
            {
                bodyModel = Resources.Load<GameObject>("Combat/CraneBody")
                    ?? throw new InvalidOperationException("Missing authored crane body skin.");
                foreach (SkinnedMeshRenderer renderer in bodyModel.GetComponentsInChildren<SkinnedMeshRenderer>(true))
                    if (!BodyRenderers.TryAdd(renderer.name, renderer))
                        throw new InvalidOperationException("Repeated crane body renderer: " + renderer.name);
                foreach (MeshFilter filter in bodyModel.GetComponentsInChildren<MeshFilter>(true))
                    if (!PassiveSurfaces.TryAdd(filter.name, filter))
                        throw new InvalidOperationException("Repeated crane passive surface: " + filter.name);
                foreach (BodyMesh mesh in LoadBodyManifest().meshes)
                    if (!BodyRenderers.ContainsKey(mesh.name))
                        throw new InvalidOperationException("Missing crane body renderer: " + mesh.name);
                foreach (PassiveMesh mesh in LoadBodyManifest().passive_meshes)
                    if (!PassiveSurfaces.ContainsKey(mesh.name))
                        throw new InvalidOperationException("Missing crane passive surface: " + mesh.name);
                if (LoadBodyManifest().hardware != null)
                    foreach (HardwareMesh mesh in LoadBodyManifest().hardware)
                        if (!PassiveSurfaces.ContainsKey(mesh.name))
                            throw new InvalidOperationException("Missing crane fitting surface: " + mesh.name);
            }
            BodyRenderers.TryGetValue(name, out SkinnedMeshRenderer result);
            return result;
        }

        public static MeshFilter LoadPassiveSurface(string name)
        {
            // The body load prepares both fixed authored mesh banks once.
            LoadBodyRenderer(string.Empty);
            PassiveSurfaces.TryGetValue(name, out MeshFilter result);
            return result;
        }

        public static Texture2D LoadBodyAtlas() => bodyAtlas != null ? bodyAtlas : bodyAtlas =
            Resources.Load<Texture2D>("Combat/CraneBodyAtlas") ?? throw new InvalidOperationException("Missing crane body albedo.");

        public static Material LoadBodyMaterial(string surface = "flesh")
        {
            if (surface == "carapace")
            {
                if (carapaceMaterial == null)
                {
                    carapaceMaterial = new Material(LoadBodyMaterial()) { name = "Crane Carapace Shared", hideFlags = HideFlags.HideAndDontSave };
                    carapaceMaterial.SetFloat("_Metallic", .03f); carapaceMaterial.SetFloat("_WarehouseNormalStrength", .3f);
                }
                return carapaceMaterial;
            }
            if (surface == "steel") return LoadMetalMaterial();
            if (surface != "flesh") throw new ArgumentOutOfRangeException(nameof(surface), surface, "Unknown authored crane surface.");
            if (bodyMaterial != null) return bodyMaterial;
            Shader shader = Resources.Load<Shader>("Shaders/WarehouseSurface")
                ?? throw new InvalidOperationException("Missing shared authored surface shader.");
            bodyResponse = Resources.Load<Texture2D>("Combat/CraneMaterialResponse")
                ?? throw new InvalidOperationException("Missing crane material response.");
            bodyMaterial = new Material(shader) { name = "Crane Body Shared", hideFlags = HideFlags.HideAndDontSave, enableInstancing = true };
            bodyMaterial.SetTexture("_BaseMap", LoadBodyAtlas());
            bodyMaterial.SetColor("_BaseColor", Color.white);
            bodyMaterial.SetTexture("_WarehouseMaterialResponse", bodyResponse);
            bodyMaterial.SetFloat("_WarehouseResponseStrength", 1f);
            bodyMaterial.SetFloat("_WarehouseNormalStrength", .25f);
            bodyMaterial.SetFloat("_WarehouseAuxiliary", 0f);
            bodyMaterial.SetFloat("_Smoothness", .12f);
            bodyMaterial.SetFloat("_Metallic", 0f);
            return bodyMaterial;
        }

        private static Material LoadMetalMaterial()
        {
            if (metalMaterial != null) return metalMaterial;
            metalMaterial = new Material(LoadBodyMaterial()) { name = "Crane Metal Shared", hideFlags = HideFlags.HideAndDontSave };
            metalMaterial.SetFloat("_Metallic", .4f); metalMaterial.SetFloat("_WarehouseNormalStrength", .2f);
            return metalMaterial;
        }

        public static Renderer CreateHardware(HardwareMesh part, Transform bone)
        {
            MeshFilter source = LoadPassiveSurface(part.name);
            if (source == null || bone == null || source.transform.parent == null || source.transform.parent.name != part.bone)
                throw new InvalidOperationException("Crane fitting lost its authored bone-local frame: " + part.name);
            GameObject result = UnityEngine.Object.Instantiate(source.gameObject, bone, false);
            result.name = part.name;
            Renderer renderer = result.GetComponent<Renderer>();
            if (renderer == null) throw new InvalidOperationException("Crane fitting lacks its authored renderer: " + part.name);
            renderer.sharedMaterial = LoadMetalMaterial();
            result.SetActive(true);
            return renderer;
        }

        private static void ApplyPassiveSurfaces(GameObject model, string name)
        {
            var filters = new Dictionary<string, MeshFilter>(StringComparer.Ordinal);
            foreach (MeshFilter filter in model.GetComponentsInChildren<MeshFilter>(true)) filters.Add(filter.name, filter);
            int replaced = 0;
            foreach (PassiveMesh part in LoadBodyManifest().passive_meshes)
            {
                if (part.model != name) continue;
                if (!filters.TryGetValue(part.name, out MeshFilter target))
                    throw new InvalidOperationException("Crane passive surface lost its original renderer: " + part.name);
                target.sharedMesh = LoadPassiveSurface(part.name).sharedMesh;
                target.GetComponent<Renderer>().sharedMaterial = LoadMetalMaterial();
                replaced++;
            }
            if (replaced != filters.Count)
                throw new InvalidOperationException("Crane surface manifest must account for every mesh of " + name);
        }
        public static GameObject LoadSuspensionHead() => suspensionHead != null ? suspensionHead : suspensionHead =
            Resources.Load<GameObject>("Combat/CraneSuspensionHead") ?? throw new InvalidOperationException("Missing authored crane suspension head.");
        public static GameObject LoadCableSegment() => cableSegment != null ? cableSegment : cableSegment =
            Resources.Load<GameObject>("Combat/CraneCableSegment") ?? throw new InvalidOperationException("Missing authored crane cable segment.");
        public static GameObject LoadRails() => rails != null ? rails : rails =
            Resources.Load<GameObject>("Combat/CraneRails") ?? throw new InvalidOperationException("Missing authored crane rails.");
        public static GameObject LoadBridge() => bridge != null ? bridge : bridge =
            Resources.Load<GameObject>("Combat/CraneBridge") ?? throw new InvalidOperationException("Missing authored crane bridge.");
        public static GameObject LoadHook() => hook != null ? hook : hook =
            Resources.Load<GameObject>("Combat/CraneHook") ?? throw new InvalidOperationException("Missing authored crane hook.");
        public static GameObject LoadWristSocket() => wristSocket != null ? wristSocket : wristSocket =
            Resources.Load<GameObject>("Combat/CraneWristSocket") ?? throw new InvalidOperationException("Missing authored crane wrist socket.");
        public static AudioClip LoadWinchSound() => winchSound != null ? winchSound : winchSound =
            CitySourceSoundSynthesis.CreateRuntimeClip(CitySourceSoundId.IndustrialWeighbridgeMechanismLoop, 0);
        public static AudioClip LoadReleaseSound() => releaseSound != null ? releaseSound : releaseSound = CreateSound(3, .42f);
        public static AudioClip LoadRailSound() => railSound != null ? railSound : railSound = CreateSound(0, 2f);
        public static AudioClip LoadBrakeSound() => brakeSound != null ? brakeSound : brakeSound = CreateSound(1, .7f);
        public static AudioClip LoadBodySound() => bodySound != null ? bodySound : bodySound = CreateSound(2, 1.2f);
        public static AudioClip LoadStartSound() => startSound != null ? startSound : startSound = CreateSound(4, .6f);
        public static AudioClip LoadHurtSound() => hurtSound != null ? hurtSound : hurtSound = CreateSound(5, .55f);

        private static AudioClip CreateSound(int kind, float seconds)
        {
            const int sampleRate = 24000;
            var samples = new float[Mathf.RoundToInt(seconds * sampleRate)];
            uint noiseState = unchecked(0x52ea917bu + (uint)kind * 7919u);
            float lowNoise = 0f;
            for (int i = 0; i < samples.Length; i++)
            {
                float time = i / (float)sampleRate, phase = time / seconds;
                noiseState = unchecked(noiseState * 1664525u + 1013904223u);
                float noise = (noiseState >> 8) / 8388607.5f - 1f;
                lowNoise += (noise - lowNoise) * .06f;
                float envelope = kind == 0 ? 1f : Mathf.Pow(Mathf.Sin(Mathf.PI * phase), .65f);
                float value;
                switch (kind)
                {
                    case 0: // Slow rollers and loaded metal, with a periodic seam.
                        value = Mathf.Sin(time * Mathf.PI * 92f) * .23f + Mathf.Sin(time * Mathf.PI * 184f) * .11f +
                            Mathf.Sin(time * Mathf.PI * 538f) * (.035f + .025f * Mathf.Sin(time * Mathf.PI * 14f)) +
                            lowNoise * .32f + noise * .02f;
                        break;
                    case 1: // A dry brake closes; no cinematic sting.
                        value = Mathf.Sin(Mathf.PI * 2f * (230f * time - 92f * time * time)) * .18f +
                            Mathf.Sin(time * Mathf.PI * 986f) * .055f + noise * .17f;
                        break;
                    case 2: // Air and compressed tissue; not intelligible speech.
                        value = Mathf.Sin(time * Mathf.PI * 86f + Mathf.Sin(time * 29f) * .22f) * .20f +
                            Mathf.Sin(time * Mathf.PI * 344f) * .075f + lowNoise * .68f + noise * .018f;
                        value *= .6f + .4f * Mathf.Sin(time * Mathf.PI * 13f) * Mathf.Sin(time * Mathf.PI * 13f);
                        break;
                    case 3:
                        value = Mathf.Sin(time * Mathf.PI * 382f) * Mathf.Exp(-time * 12f) * .34f +
                            Mathf.Sin(time * Mathf.PI * 1426f) * Mathf.Exp(-time * 22f) * .15f + noise * Mathf.Exp(-time * 30f) * .32f;
                        break;
                    case 4:
                        value = Mathf.Sin(Mathf.PI * 2f * (61f * time + 45f * time * time)) * .20f +
                            noise * Mathf.Exp(-time * 18f) * .13f + lowNoise * .22f;
                        break;
                    default:
                        value = lowNoise * .85f + Mathf.Sin(time * Mathf.PI * (154f - time * 105f)) * .22f +
                            noise * Mathf.Exp(-time * 18f) * .18f;
                        break;
                }
                // Even the reused loop's noise meets itself with zero gain at
                // the boundary; every other cue has a short dry finite tail.
                if (kind == 0) value *= Mathf.Min(1f, Mathf.Min(time, seconds - time) * 80f);
                samples[i] = Mathf.Clamp(value * envelope, -.85f, .85f);
            }
            string[] names = { "Crane rail rollers", "Crane brake", "Suspended worker breath", "Crane release", "Crane drive take-up", "Suspended worker hurt" };
            AudioClip clip = AudioClip.Create(names[kind], samples.Length, 1, sampleRate, false);
            clip.SetData(samples, 0);
            return clip;
        }

        public static GameObject CreateSuspensionHead(Transform parent)
        {
            GameObject result = UnityEngine.Object.Instantiate(LoadSuspensionHead(), parent, false);
            result.name = "Ceiling suspension head";
            ApplyPassiveSurfaces(result, "CraneSuspensionHead");
            return result;
        }

        /// <summary>The imported mesh spans exactly one metre along local +Y; preserve the imported root scale.</summary>
        public static GameObject CreateCableSegment(Transform parent)
        {
            GameObject result = UnityEngine.Object.Instantiate(LoadCableSegment(), parent, false);
            result.name = "Suspension cable";
            ApplyPassiveSurfaces(result, "CraneCableSegment");
            return result;
        }

        public static GameObject CreateRails(Transform parent)
        {
            GameObject result = UnityEngine.Object.Instantiate(LoadRails(), parent, false);
            result.name = "Crane longitudinal rails";
            ApplyPassiveSurfaces(result, "CraneRails");
            return result;
        }

        public static GameObject CreateBridge(Transform parent)
        {
            GameObject result = UnityEngine.Object.Instantiate(LoadBridge(), parent, false);
            result.name = "Crane transverse bridge";
            ApplyPassiveSurfaces(result, "CraneBridge");
            return result;
        }

        /// <summary>Origin at the cable eye; local +Y runs down the forged J toward its working mass.</summary>
        public static GameObject CreateHook(Transform parent)
        {
            GameObject result = UnityEngine.Object.Instantiate(LoadHook(), parent, false);
            result.name = "Crane attack hook";
            ApplyPassiveSurfaces(result, "CraneHook");
            return result;
        }

        /// <summary>Place beside the rig under a unit host; copy the wrist world pose without inheriting its bone scale.</summary>
        public static GameObject CreateWristSocket(Transform parent)
        {
            GameObject result = UnityEngine.Object.Instantiate(LoadWristSocket(), parent, false);
            result.name = "Crane wrist cable socket";
            ApplyPassiveSurfaces(result, "CraneWristSocket");
            return result;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetCache()
        {
            Clips.Clear(); suspensionHead = cableSegment = rails = bridge = hook = wristSocket = null;
            BodyRenderers.Clear(); PassiveSurfaces.Clear(); bodyModel = null; bodyManifest = null; bodyAtlas = bodyResponse = null;
            if (bodyMaterial != null) UnityEngine.Object.Destroy(bodyMaterial);
            if (carapaceMaterial != null) UnityEngine.Object.Destroy(carapaceMaterial);
            if (metalMaterial != null) UnityEngine.Object.Destroy(metalMaterial);
            bodyMaterial = carapaceMaterial = metalMaterial = null;
            if (winchSound != null) UnityEngine.Object.Destroy(winchSound);
            if (releaseSound != null) UnityEngine.Object.Destroy(releaseSound);
            if (railSound != null) UnityEngine.Object.Destroy(railSound);
            if (brakeSound != null) UnityEngine.Object.Destroy(brakeSound);
            if (bodySound != null) UnityEngine.Object.Destroy(bodySound);
            if (startSound != null) UnityEngine.Object.Destroy(startSound);
            if (hurtSound != null) UnityEngine.Object.Destroy(hurtSound);
            winchSound = releaseSound = railSound = brakeSound = bodySound = startSound = hurtSound = null;
        }
    }
}

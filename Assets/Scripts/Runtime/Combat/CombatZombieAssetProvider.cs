using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace BarPromenade
{
    /// <summary>Bounded CombatTest-only bank on the retained ordinary worker skeleton.</summary>
    public static class CombatZombieAssetProvider
    {
        public const string ResourceFolder = "Combat/";
        public const string IdleClip = "ZombieIdle", WalkClip = "ZombieWalk",
            AttackRightClip = "ZombieAttackRight", AttackLeftClip = "ZombieAttackLeft", HitClip = "ZombieHit",
            LoadAdjustClip = "ZombieLoadAdjust", WatchClip = "ZombieWatch",
            DormantClip = "ZombieDormant", RevealClip = "ZombieReveal", FrenzyClip = "ZombieFrenzy";
        public const float RevealSeconds = .65f;
        private const float DormantChestHeight = .214f;
        internal readonly struct MimicBreak
        {
            internal readonly float Seconds;
            internal readonly int[] BoardNumbers;
            internal readonly string EscapeBone;
            internal readonly Vector3 LocalImpulseDirection;
            internal MimicBreak(float seconds, int[] boardNumbers, string escapeBone, Vector3 localImpulseDirection)
            { Seconds = seconds; BoardNumbers = boardNumbers; EscapeBone = escapeBone; LocalImpulseDirection = localImpulseDirection; }
        }
        internal static readonly MimicBreak[] RevealBreaks =
        {
            new MimicBreak(.11f, new[] { 6, 31 }, "foot.L", new Vector3(-.15f, -1f, 0f)),
            new MimicBreak(.15f, new[] { 36 }, "shin.R", Vector3.back),
            new MimicBreak(.19f, new[] { 2, 32 }, "foot.R", new Vector3(.15f, -1f, 0f)),
            new MimicBreak(.28f, new[] { 20, 33 }, "hand.L", new Vector3(-1f, 0f, .25f)),
            new MimicBreak(.34f, new[] { 10, 34 }, "hand.R", new Vector3(1f, 0f, .25f)),
            new MimicBreak(.44f, new[] { 35 }, "head", Vector3.forward)
        };
        public const float WalkCycleDistance = .85f, WalkCycleSeconds = .5f;
        private static readonly Vector2[] WalkTravelKeys =
        {
            new Vector2(0f, 0f), new Vector2(.10f, .03f), new Vector2(.30f, .17f),
            new Vector2(.42f, .44f), new Vector2(.50f, .50f), new Vector2(.60f, .53f),
            new Vector2(.80f, .67f), new Vector2(.92f, .94f), new Vector2(1f, 1f)
        };

        internal static float WalkTravel(float seconds)
        {
            float cycles = seconds / WalkCycleSeconds, whole = Mathf.Floor(cycles), phase = cycles - whole;
            for (int i = 1; i < WalkTravelKeys.Length; i++)
            {
                Vector2 a = WalkTravelKeys[i - 1], b = WalkTravelKeys[i];
                if (phase > b.x) continue;
                float u = Mathf.Clamp01((phase - a.x) / (b.x - a.x));
                float blend = u * u * u * (10f - 15f * u + 6f * u * u);
                return (whole + Mathf.Lerp(a.y, b.y, blend)) * WalkCycleDistance;
            }
            return (whole + 1f) * WalkCycleDistance;
        }
        public const string CatchClip = "ZombieCatchFlop";
        public const float CatchCycleSeconds = 6.4f;
        public static readonly string[] ClipNames = { IdleClip, WalkClip, AttackRightClip, AttackLeftClip, HitClip, LoadAdjustClip, WatchClip, DormantClip, RevealClip, FrenzyClip };
        private static readonly Dictionary<string, AnimationClip> Clips = new Dictionary<string, AnimationClip>(10);
        private static readonly Dictionary<string, AudioClip> Audio = new Dictionary<string, AudioClip>(14);
        private static Mesh dormantCrateMesh;
        private static Bounds dormantCrateCollision;
        private static readonly Texture2D[] Faces = new Texture2D[3];
        private static AudioClip groan, lunge;
        private static AnimationClip catchClip;
        private static GameObject crate;
        private static Material crateMaterial;
        private static bool loaded;

        public static bool IsLoop(string name) => name == IdleClip || name == WalkClip || name == DormantClip;
        public static float ClipDuration(string name) => name switch
        {
            IdleClip => 4f, WalkClip => WalkCycleSeconds, DormantClip => 1f, RevealClip => RevealSeconds,
            AttackRightClip or AttackLeftClip => MeleeCombatSettings.ZombieUnarmed.AnimationAttackDurationSeconds, HitClip => .45f,
            LoadAdjustClip => 2.4f, WatchClip => 2.8f,
            FrenzyClip => CombatActor.ZombieFrenzyDurationSeconds,
            _ => throw new ArgumentOutOfRangeException(nameof(name), name, "Unknown zombie action.")
        };

        public static AnimationClip LoadClip(string name)
        {
            LoadBank();
            if (!Clips.TryGetValue(name, out AnimationClip clip) || clip == null)
                throw new InvalidOperationException("Missing authored zombie action " + name);
            return clip;
        }

        private static void LoadBank()
        {
            if (loaded) return;
            foreach (AnimationClip clip in Resources.LoadAll<AnimationClip>(ResourceFolder + "ZombieActions"))
            {
                if (Array.IndexOf(ClipNames, clip.name) < 0) continue;
                if (clip.events.Length != 0 || clip.isLooping != IsLoop(clip.name) ||
                    Mathf.Abs(clip.length - ClipDuration(clip.name)) > .003f)
                    throw new InvalidOperationException("Invalid in-place zombie action " + clip.name);
                Clips.Add(clip.name, clip);
            }
            if (Clips.Count != ClipNames.Length) throw new InvalidOperationException("Zombie action bank is incomplete.");
            loaded = true;
        }

        public static Texture2D LoadFace(int variant)
        {
            if ((uint)variant >= Faces.Length) throw new ArgumentOutOfRangeException(nameof(variant));
            if (Faces[variant] == null) Faces[variant] = Resources.Load<Texture2D>(ResourceFolder + "ZombieFace" + (variant + 1));
            if (Faces[variant] == null) throw new InvalidOperationException("Missing authored corpse palette.");
            return Faces[variant];
        }

        /// <summary>Apply once per permanent slot; pooled resets retain the same civilian corpse.</summary>
        public static void ApplyAppearance(GameObject npc, int variant)
        {
            if (npc == null) throw new ArgumentNullException(nameof(npc));
            var appearance = npc.GetComponent<CombatZombieAppearance>() ?? npc.AddComponent<CombatZombieAppearance>();
            appearance.Configure(((variant % 3) + 3) % 3);
        }

        public static AudioClip LoadGroan() => groan != null ? groan : groan = RequireAudio("ZombieGroan");
        public static AudioClip LoadLunge() => lunge != null ? lunge : lunge = RequireAudio("ZombieLunge");
        private static AudioClip RequireAudio(string name) => LoadAudio(name);
        public static AudioClip LoadAudio(string name)
        {
            if (Audio.TryGetValue(name, out AudioClip clip) && clip != null) return clip;
            clip = Resources.Load<AudioClip>(ResourceFolder + name)
                ?? throw new InvalidOperationException("Missing authored zombie sound " + name);
            Audio[name] = clip;
            return clip;
        }

        /// <summary>The same grounded box as the folded porter, baked once for passive cargo.</summary>
        public static GameObject CreateDormantCrate(Transform parent, Vector3 position, Quaternion rotation)
        {
            EnsureDormantCrateMesh();
            var result = new GameObject("Fish crate");
            result.transform.SetParent(parent, false);
            result.transform.SetLocalPositionAndRotation(position, rotation);
            result.AddComponent<MeshFilter>().sharedMesh = dormantCrateMesh;
            MeshRenderer renderer = result.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = CrateMaterial;
            renderer.reflectionProbeUsage = ReflectionProbeUsage.Off;
            BoxCollider collision = result.AddComponent<BoxCollider>();
            collision.center = dormantCrateCollision.center;
            collision.size = dormantCrateCollision.size;
            return result;
        }

        private static void EnsureDormantCrateMesh()
        {
            if (dormantCrateMesh != null) return;
            GameObject source = UnityEngine.Object.Instantiate(LoadCrate());
            source.name = "Dormant fish crate bake";
            Transform socket = CityPedestrianHandProps.FindSocket(source.transform, "ChestSocket");
            if (socket == null) throw new InvalidOperationException("Missing dormant chest socket.");
            // The imported socket carries the FBX quarter turn. Match the
            // folded load's real frame: fish above the horizontal crate floor.
            source.transform.rotation = Quaternion.Euler(270f, 0f, 0f) * Quaternion.Inverse(socket.rotation) * source.transform.rotation;
            source.transform.position += new Vector3(0f, DormantChestHeight + PlayerFactory.GroundedRootOffset, 0f) - socket.position;
            var combined = new List<CombineInstance>(3);
            var temporary = new List<Mesh>(2);
            foreach (Renderer renderer in source.GetComponentsInChildren<Renderer>(true))
            {
                Mesh mesh;
                if (renderer is SkinnedMeshRenderer skin)
                {
                    mesh = new Mesh { name = "Dormant crate skin bake" };
                    // Compensate the renderer's imported scale, as for shared
                    // body/contact bakes. The full matrix below then applies
                    // FBX units once, matching the measured collision markers.
                    skin.BakeMesh(mesh, true);
                    temporary.Add(mesh);
                }
                else
                {
                    MeshFilter filter = renderer.GetComponent<MeshFilter>();
                    if (filter == null || filter.sharedMesh == null) continue;
                    mesh = filter.sharedMesh;
                }
                combined.Add(new CombineInstance { mesh = mesh, transform = renderer.localToWorldMatrix });
            }
            dormantCrateMesh = new Mesh { name = "Dormant Fish Crate Shared", hideFlags = HideFlags.HideAndDontSave };
            dormantCrateMesh.CombineMeshes(combined.ToArray(), true, true);
            Transform collision = CityPedestrianHandProps.FindSocket(source.transform, "CollisionBox");
            Transform minimum = CityPedestrianHandProps.FindSocket(collision, "BoundsMin");
            Transform maximum = CityPedestrianHandProps.FindSocket(collision, "BoundsMax");
            dormantCrateCollision = new Bounds((minimum.position + maximum.position) * .5f,
                new Vector3(Mathf.Abs(maximum.position.x-minimum.position.x), Mathf.Abs(maximum.position.y-minimum.position.y), Mathf.Abs(maximum.position.z-minimum.position.z)));
            source.SetActive(false);
            UnityEngine.Object.Destroy(source);
            foreach (Mesh mesh in temporary) UnityEngine.Object.Destroy(mesh);
        }

        public static GameObject LoadCrate() => crate != null ? crate : crate =
            Resources.Load<GameObject>(ResourceFolder + "ZombieCrate")
            ?? throw new InvalidOperationException("Missing authored porter load.");

        internal static AnimationClip LoadCatchClip()
        {
            if (catchClip != null) return catchClip;
            foreach (AnimationClip clip in Resources.LoadAll<AnimationClip>(ResourceFolder + "ZombieCrate"))
                if (clip.name == CatchClip) catchClip = clip;
            if (catchClip == null || !catchClip.isLooping || catchClip.events.Length != 0 ||
                Mathf.Abs(catchClip.length - CatchCycleSeconds) > .003f)
                throw new InvalidOperationException("The fused crate requires its bounded fish animation.");
            return catchClip;
        }

        internal static Material CrateMaterial
        {
            get
            {
                if (crateMaterial != null) return crateMaterial;
                Shader shader = Resources.Load<Shader>("Shaders/Ps1Lit");
                Texture2D atlas = Resources.Load<Texture2D>(ResourceFolder + "ZombieCrateAtlas");
                if (shader == null || atlas == null) throw new InvalidOperationException("Missing shared porter crate surface.");
                crateMaterial = new Material(shader) { name = "Porter Crate Shared", enableInstancing = true,
                    hideFlags = HideFlags.HideAndDontSave };
                crateMaterial.SetTexture("_BaseMap", atlas);
                crateMaterial.SetColor("_BaseColor", Color.white);
                crateMaterial.SetFloat("_Smoothness", .12f);
                return crateMaterial;
            }
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetCache()
        {
            Clips.Clear(); Array.Clear(Faces, 0, Faces.Length);
            Audio.Clear();
            loaded = false; groan = lunge = null; catchClip = null;
            crate = null;
            if (crateMaterial != null) UnityEngine.Object.Destroy(crateMaterial);
            crateMaterial = null;
            if (dormantCrateMesh != null) UnityEngine.Object.Destroy(dormantCrateMesh);
            dormantCrateMesh = null;
        }
    }
}

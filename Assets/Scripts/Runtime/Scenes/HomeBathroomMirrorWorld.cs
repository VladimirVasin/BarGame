using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace BarPromenade
{
    /// <summary>
    /// The bathroom mirror the way old games built one: behind the opening
    /// in the wall stands a mirrored copy of the bathroom, and in it a
    /// second instance of the hero prefab wearing the real hero's pose,
    /// materials and property blocks, refreshed every frame after every
    /// bone writer has run. No render texture, no second camera, no
    /// stencil — the lens simply looks through a hole at a reflected room.
    ///
    /// The copy lives only while the pinned bathroom shot is active: the
    /// bathroom scenes start from it and never leave its hold rectangle,
    /// and no other shot can see the plate. Elsewhere the copy is off and
    /// the original plate plugs the hole.
    /// </summary>
    [DefaultExecutionOrder(320)]
    [DisallowMultipleComponent]
    public sealed class HomeBathroomMirrorWorld : MonoBehaviour
    {
        public const string RootName = "Home Bathroom Mirror World";
        public const string ContentName = "Home Bathroom Mirror Content";
        public const string SpaceName = "Mirror Space";
        public const string TwinName = "Home Bathroom Mirror Hero Twin";
        public const string CloneSuffix = " (Mirror)";

        /// <summary>
        /// The bathroom's contents that lie entirely in front of the plane,
        /// room-local: x 1.00…4.90, y −0.10…3.60, z −0.20…3.862. The lower z
        /// reaches past the bathroom's front wall because the door stands ajar
        /// and swings out into the room. The back tile, the leak stain, the
        /// tube fixture and the opening's own pieces straddle or sit behind
        /// the plane and fall out by themselves.
        /// </summary>
        public static readonly Bounds SelectionBounds = new Bounds(
            new Vector3(2.95f, 1.75f, 1.831f),
            new Vector3(3.90f, 3.70f, 4.062f));

        /// <summary>
        /// The box alone is not enough: it also holds the near corner of the
        /// locked room's front wall, which has no business standing in the
        /// reflection. Everything the bathroom builder and the day dressing
        /// put in the room says so in its name.
        /// </summary>
        public const string SelectionNameMark = "Bathroom";

        private static readonly string[] ExcludedNames =
        {
            HomeBathroomMirrorPlane.PlateName,
            HomeBathroomMirrorPlane.CrackName,
            HomeBathroomMirrorOpeningBuilder.RootName
        };

        private readonly List<HomeMirrorSubtreeClone> statics = new List<HomeMirrorSubtreeClone>();
        private readonly List<string> clonedSourceNames = new List<string>();
        private HomeInteriorRoot home;
        private HomeBathroomMirrorOpening opening;
        private Transform content;
        private Transform space;
        private HomeMirrorHeroTwin twin;
        private HomeApartmentDressing dressing;

        public bool IsInitialized { get; private set; }
        public bool IsActive { get; private set; }
        public Transform Content => content;
        public Transform MirrorSpace => space;
        public HomeBathroomMirrorOpening Opening => opening;
        public bool HasTwin => twin != null;
        public Player3DAssetRegistry Twin => twin?.Registry;
        public Transform TwinRoot => twin?.Root;
        public int TwinPairedBoneCount => twin?.PairedBoneCount ?? 0;
        public int TwinUnpairedBoneCount => twin?.UnpairedBoneCount ?? 0;
        public int TwinPairedRendererCount => twin?.PairedRendererCount ?? 0;
        public int TwinUnpairedRendererCount => twin?.UnpairedRendererCount ?? 0;
        public int StaticCloneCount => statics.Count;
        public IReadOnlyList<string> ClonedSourceNames => clonedSourceNames;

        public void Initialize(HomeInteriorRoot homeRoot, HomeBathroomMirrorOpening mirrorOpening)
        {
            home = homeRoot != null ? homeRoot : throw new ArgumentNullException(nameof(homeRoot));
            opening = mirrorOpening != null ? mirrorOpening : throw new ArgumentNullException(nameof(mirrorOpening));
            if (home.Room == null)
            {
                throw new InvalidOperationException("The bathroom mirror needs the built room.");
            }

            dressing = home.Room.GetComponentInParent<HomeApartmentDressing>();
            content = new GameObject(ContentName).transform;
            content.SetParent(transform, false);
            space = new GameObject(SpaceName).transform;
            space.SetParent(content, false);
            space.localPosition = HomeBathroomMirrorPlane.SpaceLocalPosition;
            space.localRotation = Quaternion.identity;
            space.localScale = HomeBathroomMirrorPlane.SpaceLocalScale;

            BuildStaticClones();
            BuildPatches();
            twin = HomeMirrorHeroTwin.TryCreate(home, space);
            IsInitialized = true;
            ApplyActive(false);
        }

        /// <summary>The rule, pure: the copy shows only behind the pinned bathroom shot.</summary>
        public static bool ShouldBeActive(HomeFixedCameraController fixedCamera)
        {
            return fixedCamera != null &&
                   fixedCamera.IsInitialized &&
                   fixedCamera.ActiveShotKind == HomeCameraShotKind.Bathroom;
        }

        /// <summary>
        /// The twin's renderer rule: head geometry follows the body, not the
        /// source — the first-person views take the real head off because
        /// the lens sits inside it, and a reflection with no head is wrong;
        /// a hero hidden whole (no body drawn) hides his reflection too.
        /// </summary>
        public static bool ResolveTwinRendererEnabled(bool isHead, bool sourceEnabled, bool anyBodyEnabled)
        {
            return isHead ? anyBodyEnabled : sourceEnabled;
        }

        private void LateUpdate()
        {
            if (!IsInitialized)
            {
                return;
            }

            bool active = home != null && ShouldBeActive(home.FixedCamera);
            if (active != IsActive)
            {
                ApplyActive(active);
            }

            if (!IsActive)
            {
                return;
            }

            for (int index = 0; index < statics.Count; index++)
            {
                statics[index].SyncTransforms();
                statics[index].SyncRenderers(false);
                // Every frame, not only on activation: the bathroom tube's
                // flicker and the day's tints live in the property block, and
                // a reflection whose lamp is frozen while the real one
                // stutters is the one thing that gives the trick away.
                statics[index].SyncPropertyBlocks();
            }

            twin?.Sync();
        }

        /// <summary>The plug goes back the moment this stops running.</summary>
        private void OnDisable()
        {
            if (IsInitialized && IsActive)
            {
                ApplyActive(false);
            }
        }

        private void ApplyActive(bool active)
        {
            IsActive = active;
            if (content != null)
            {
                content.gameObject.SetActive(active);
            }

            opening?.SetMirrorActive(active);
            if (!active)
            {
                return;
            }

            // Tints move with the apartment's day while the copy is off.
            for (int index = 0; index < statics.Count; index++)
            {
                statics[index].SyncTransforms();
                statics[index].SyncRenderers(false);
                statics[index].SyncPropertyBlocks();
            }

            twin?.Sync();
        }

        private void BuildStaticClones()
        {
            Transform room = home.Room;
            for (int index = 0; index < room.childCount; index++)
            {
                Transform child = room.GetChild(index);
                if (!IsSelectable(child.name) || HomeMirrorSubtreeClone.IsEffectNode(child))
                {
                    continue;
                }

                if (!TryGetRoomLocalBounds(child, room, out Bounds bounds) ||
                    !SelectionBounds.Contains(bounds.min) ||
                    !SelectionBounds.Contains(bounds.max))
                {
                    continue;
                }

                HomeMirrorSubtreeClone clone = HomeMirrorSubtreeClone.Create(
                    child,
                    space,
                    null,
                    child.name + CloneSuffix);
                if (clone.RendererCount == 0)
                {
                    clone.Destroy();
                    continue;
                }

                statics.Add(clone);
                clonedSourceNames.Add(child.name);
            }
        }

        /// <summary>
        /// Floor, ceiling and the walls the bathroom shares with the rest of
        /// the flat are apartment-sized slabs that straddle the plane, so the
        /// mirrored room gets patches of its own, starting where the real
        /// slabs end and closing the void behind the mirrored doorway.
        /// </summary>
        private void BuildPatches()
        {
            HomeInteriorModelLibrary library = HomeInteriorModelLibrary.Load();
            Color wallTint = library.Binding(HomeBathroomMirrorPlane.BackWallName, "Box").Tint;
            Color floorTint = library.Binding("Home Floor", "Box").Tint;
            Color ceilingTint = library.Binding("Home Ceiling", "Box").Tint;
            Patch("Home Bathroom Mirror Floor", new Vector3(3.04f, -0.08f, 6.50f), new Vector3(3.68f, 0.16f, 5.00f),
                HomeSurfaceKind.PlankFloor, SurfaceProjection.BoxXZ, floorTint, ShadowCastingMode.Off, true);
            Patch("Home Bathroom Mirror Ceiling", new Vector3(3.04f, 3.44f, 6.62f), new Vector3(3.68f, 0.16f, 4.76f),
                HomeSurfaceKind.CeilingPlaster, SurfaceProjection.BoxXZ, ceilingTint, ShadowCastingMode.Off, false);
            // Starts where the real facade pier ends (z 4.00) instead of at the
            // plane, so the two do not share 13 cm of the same surface.
            Patch("Home Bathroom Mirror East Wall", new Vector3(5.00f, 1.70f, 6.50f), new Vector3(0.24f, 3.40f, 5.00f),
                HomeSurfaceKind.Wallpaper, SurfaceProjection.BoxZY, wallTint, ShadowCastingMode.On, true);
            // Reaches from the plane to the reflected west wall exactly.
            Patch("Home Bathroom Mirror West Fill", new Vector3(1.55f, 1.70f, 3.974f), new Vector3(0.18f, 3.40f, 0.216f),
                HomeSurfaceKind.Wallpaper, SurfaceProjection.BoxZY, wallTint, ShadowCastingMode.On, true);
            Patch("Home Bathroom Mirror Beyond Door", new Vector3(2.30f, 1.70f, 9.00f), new Vector3(2.20f, 3.40f, 0.20f),
                HomeSurfaceKind.Wallpaper, SurfaceProjection.BoxXY, wallTint * 0.35f, ShadowCastingMode.Off, true);
        }

        private void Patch(
            string name,
            Vector3 center,
            Vector3 size,
            HomeSurfaceKind kind,
            SurfaceProjection projection,
            Color tint,
            ShadowCastingMode shadows,
            bool receiveShadows)
        {
            GameObject box = RuntimePrimitiveFactory.CreateBox(name, content, center, size, Color.white, false);
            MeshRenderer renderer = box.GetComponent<MeshRenderer>();
            tint.a = 1f;
            HomeSurfaceAppearance.Apply(renderer, kind, projection, tint);
            renderer.shadowCastingMode = shadows;
            renderer.receiveShadows = receiveShadows;
            renderer.lightProbeUsage = LightProbeUsage.Off;
            renderer.reflectionProbeUsage = ReflectionProbeUsage.Off;
            // The copies age with the apartment because they read their
            // sources' blocks; the patches have no source, so they are
            // registered and aged like any other surface of the flat.
            dressing?.RegisterSurface(renderer, kind);
        }

        /// <summary>A room child the mirrored bathroom may hold a copy of.</summary>
        public static bool IsSelectable(string name)
        {
            if (name == null || name.IndexOf(SelectionNameMark, StringComparison.Ordinal) < 0)
            {
                return false;
            }

            for (int index = 0; index < ExcludedNames.Length; index++)
            {
                if (string.Equals(name, ExcludedNames[index], StringComparison.Ordinal))
                {
                    return false;
                }
            }

            return true;
        }

        /// <summary>
        /// The room-local box around every mesh under a node, from the meshes
        /// and transforms rather than renderer bounds, which are stale on
        /// inactive objects (day-gated decor) and disabled renderers.
        /// </summary>
        internal static bool TryGetRoomLocalBounds(Transform node, Transform room, out Bounds bounds)
        {
            bounds = default;
            bool any = false;
            MeshFilter[] filters = node.GetComponentsInChildren<MeshFilter>(true);
            for (int index = 0; index < filters.Length; index++)
            {
                MeshFilter filter = filters[index];
                if (filter.sharedMesh == null || filter.GetComponent<MeshRenderer>() == null)
                {
                    continue;
                }

                Accumulate(ref bounds, ref any, filter.sharedMesh.bounds, filter.transform, room);
            }

            SkinnedMeshRenderer[] skinned = node.GetComponentsInChildren<SkinnedMeshRenderer>(true);
            for (int index = 0; index < skinned.Length; index++)
            {
                if (skinned[index].sharedMesh == null)
                {
                    continue;
                }

                Accumulate(ref bounds, ref any, skinned[index].sharedMesh.bounds, skinned[index].transform, room);
            }

            return any;
        }

        private static void Accumulate(ref Bounds bounds, ref bool any, Bounds local, Transform owner, Transform room)
        {
            Matrix4x4 toRoom = room.worldToLocalMatrix * owner.localToWorldMatrix;
            Vector3 min = local.min;
            Vector3 max = local.max;
            for (int corner = 0; corner < 8; corner++)
            {
                Vector3 point = toRoom.MultiplyPoint3x4(new Vector3(
                    (corner & 1) == 0 ? min.x : max.x,
                    (corner & 2) == 0 ? min.y : max.y,
                    (corner & 4) == 0 ? min.z : max.z));
                if (!any)
                {
                    bounds = new Bounds(point, Vector3.zero);
                    any = true;
                }
                else
                {
                    bounds.Encapsulate(point);
                }
            }
        }

        private void OnDestroy()
        {
            twin?.Destroy();
            twin = null;
            statics.Clear();
        }
    }

    /// <summary>
    /// The reflected hero: a second instance of the production prefab with
    /// its animator, colliders and lights off, whose bones, materials and
    /// property blocks are copied from the real hero every frame. Bones pair
    /// by their path under the registry — the two instances are the same
    /// prefab in the same rest pose, so a verbatim local copy is exact and
    /// carries face bones and sockets along.
    /// </summary>
    internal sealed class HomeMirrorHeroTwin
    {
        private readonly List<Transform> sourceBones = new List<Transform>();
        private readonly List<Transform> twinBones = new List<Transform>();
        private readonly List<Renderer> sourceRenderers = new List<Renderer>();
        private readonly List<Renderer> twinRenderers = new List<Renderer>();
        private readonly List<bool> headFlags = new List<bool>();
        private readonly Transform heroRoot;
        private readonly Transform homeFrame;
        private readonly MaterialPropertyBlock scratch = new MaterialPropertyBlock();

        private HomeMirrorHeroTwin(Player3DAssetRegistry hero, Player3DAssetRegistry twin, Transform homeFrame)
        {
            Registry = twin;
            heroRoot = hero.transform;
            this.homeFrame = homeFrame;
            Pair(hero, twin);
        }

        public Player3DAssetRegistry Registry { get; }
        public Transform Root => Registry != null ? Registry.transform : null;
        public int PairedBoneCount => twinBones.Count;
        public int UnpairedBoneCount { get; private set; }
        public int PairedRendererCount => twinRenderers.Count;
        public int UnpairedRendererCount { get; private set; }

        public static HomeMirrorHeroTwin TryCreate(HomeInteriorRoot home, Transform parent)
        {
            if (home == null || parent == null ||
                !(home.Player.Visual is Player3DCharacterPresentation presentation) ||
                presentation.Registry == null)
            {
                return null;
            }

            if (!Player3DResources.TryInstantiate(parent, out Player3DAssetRegistry twinRegistry))
            {
                return null;
            }

            twinRegistry.gameObject.name = HomeBathroomMirrorWorld.TwinName;
            Neuter(twinRegistry, presentation.Registry.gameObject.layer);
            var twin = new HomeMirrorHeroTwin(presentation.Registry, twinRegistry, home.transform);
            twin.Sync();
            return twin;
        }

        /// <summary>Everything a reflection must not do: animate, collide, light, cast.</summary>
        private static void Neuter(Player3DAssetRegistry registry, int layer)
        {
            Animator animator = registry.Animator;
            if (animator != null)
            {
                animator.applyRootMotion = false;
                animator.runtimeAnimatorController = null;
                animator.enabled = false;
            }

            Collider[] colliders = registry.GetComponentsInChildren<Collider>(true);
            for (int index = 0; index < colliders.Length; index++)
            {
                colliders[index].enabled = false;
            }

            Light[] lights = registry.GetComponentsInChildren<Light>(true);
            for (int index = 0; index < lights.Length; index++)
            {
                lights[index].enabled = false;
            }

            Rigidbody[] bodies = registry.GetComponentsInChildren<Rigidbody>(true);
            for (int index = 0; index < bodies.Length; index++)
            {
                bodies[index].isKinematic = true;
            }

            // A reflection may not act. Everything that would run a frame of its
            // own is switched off by kind, so a component added to the prefab
            // later cannot start living a second life in the mirror; only the
            // registry stays, because the copy is addressed through it.
            MonoBehaviour[] behaviours = registry.GetComponentsInChildren<MonoBehaviour>(true);
            for (int index = 0; index < behaviours.Length; index++)
            {
                MonoBehaviour behaviour = behaviours[index];
                if (behaviour != null && !ReferenceEquals(behaviour, registry))
                {
                    behaviour.enabled = false;
                }
            }

            AudioSource[] sources = registry.GetComponentsInChildren<AudioSource>(true);
            for (int index = 0; index < sources.Length; index++)
            {
                sources[index].enabled = false;
            }

            ParticleSystem[] particles = registry.GetComponentsInChildren<ParticleSystem>(true);
            for (int index = 0; index < particles.Length; index++)
            {
                ParticleSystem.EmissionModule emission = particles[index].emission;
                emission.enabled = false;
                particles[index].Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            }

            Transform[] hierarchy = registry.GetComponentsInChildren<Transform>(true);
            for (int index = 0; index < hierarchy.Length; index++)
            {
                hierarchy[index].gameObject.layer = layer;
            }

            IReadOnlyList<Renderer> renderers = registry.Renderers;
            for (int index = 0; index < renderers.Count; index++)
            {
                Renderer renderer = renderers[index];
                if (renderer == null)
                {
                    continue;
                }

                renderer.shadowCastingMode = ShadowCastingMode.Off;
                renderer.lightProbeUsage = LightProbeUsage.Off;
                renderer.reflectionProbeUsage = ReflectionProbeUsage.Off;
                if (renderer is SkinnedMeshRenderer skinned)
                {
                    skinned.updateWhenOffscreen = true;
                }
            }
        }

        private void Pair(Player3DAssetRegistry hero, Player3DAssetRegistry twin)
        {
            Transform twinRoot = twin.transform;
            Transform[] nodes = twinRoot.GetComponentsInChildren<Transform>(true);
            for (int index = 0; index < nodes.Length; index++)
            {
                Transform node = nodes[index];
                if (node == twinRoot)
                {
                    continue;
                }

                Transform source = hero.transform.Find(PathUnder(twinRoot, node));
                if (source == null)
                {
                    UnpairedBoneCount++;
                    continue;
                }

                sourceBones.Add(source);
                twinBones.Add(node);
            }

            var headByRenderer = new Dictionary<Renderer, bool>();
            IReadOnlyList<Player3DMeshBinding> bindings = hero.MeshBindings;
            for (int index = 0; index < bindings.Count; index++)
            {
                Player3DMeshBinding binding = bindings[index];
                if (binding?.Renderer != null)
                {
                    headByRenderer[binding.Renderer] = Player3DHeadVisibility.IsHeadGeometry(binding.BoneName);
                }
            }

            IReadOnlyList<Renderer> heroRenderers = hero.Renderers;
            IReadOnlyList<Renderer> twinRendererList = twin.Renderers;
            var twinByName = new Dictionary<string, Renderer>();
            for (int index = 0; index < twinRendererList.Count; index++)
            {
                if (twinRendererList[index] != null)
                {
                    twinByName[twinRendererList[index].gameObject.name] = twinRendererList[index];
                }
            }

            for (int index = 0; index < heroRenderers.Count; index++)
            {
                Renderer source = heroRenderers[index];
                if (source == null)
                {
                    continue;
                }

                Renderer mirror = index < twinRendererList.Count &&
                                  twinRendererList[index] != null &&
                                  twinRendererList[index].gameObject.name == source.gameObject.name
                    ? twinRendererList[index]
                    : twinByName.TryGetValue(source.gameObject.name, out Renderer byName) ? byName : null;
                if (mirror == null)
                {
                    UnpairedRendererCount++;
                    continue;
                }

                sourceRenderers.Add(source);
                twinRenderers.Add(mirror);
                headFlags.Add(headByRenderer.TryGetValue(source, out bool isHead) && isHead);
            }
        }

        /// <summary>Root pose in the home frame, bones verbatim, renderer state with the head rule.</summary>
        public void Sync()
        {
            Transform root = Root;
            if (root == null || heroRoot == null)
            {
                return;
            }

            if (homeFrame != null)
            {
                root.localPosition = homeFrame.InverseTransformPoint(heroRoot.position);
                root.localRotation = Quaternion.Inverse(homeFrame.rotation) * heroRoot.rotation;
                // Read in the same frame as the pose, so a scaled home root
                // could not silently inflate the reflection.
                Vector3 frame = homeFrame.lossyScale;
                Vector3 hero = heroRoot.lossyScale;
                root.localScale = new Vector3(
                    Mathf.Approximately(frame.x, 0f) ? hero.x : hero.x / frame.x,
                    Mathf.Approximately(frame.y, 0f) ? hero.y : hero.y / frame.y,
                    Mathf.Approximately(frame.z, 0f) ? hero.z : hero.z / frame.z);
            }
            else
            {
                root.localPosition = heroRoot.position;
                root.localRotation = heroRoot.rotation;
                root.localScale = heroRoot.lossyScale;
            }

            for (int index = 0; index < sourceBones.Count; index++)
            {
                Transform source = sourceBones[index];
                Transform bone = twinBones[index];
                if (source == null || bone == null)
                {
                    continue;
                }

                bone.localPosition = source.localPosition;
                bone.localRotation = source.localRotation;
                bone.localScale = source.localScale;
            }

            bool anyBody = false;
            for (int index = 0; index < sourceRenderers.Count; index++)
            {
                if (!headFlags[index] && sourceRenderers[index] != null && sourceRenderers[index].enabled)
                {
                    anyBody = true;
                    break;
                }
            }

            for (int index = 0; index < sourceRenderers.Count; index++)
            {
                Renderer source = sourceRenderers[index];
                Renderer mirror = twinRenderers[index];
                if (source == null || mirror == null)
                {
                    continue;
                }

                bool enabled = HomeBathroomMirrorWorld.ResolveTwinRendererEnabled(
                    headFlags[index], source.enabled, anyBody);
                if (mirror.enabled != enabled)
                {
                    mirror.enabled = enabled;
                }

                if (!ReferenceEquals(mirror.sharedMaterial, source.sharedMaterial))
                {
                    mirror.sharedMaterials = source.sharedMaterials;
                }

                // Cleared first: a reused block keeps whatever the previous
                // renderer put in it, and the face atlas would smear onto skin.
                scratch.Clear();
                source.GetPropertyBlock(scratch);
                mirror.SetPropertyBlock(scratch);
            }
        }

        public void Destroy()
        {
            if (Registry == null)
            {
                return;
            }

            GameObject instance = Registry.gameObject;
            if (Application.isPlaying)
            {
                UnityEngine.Object.Destroy(instance);
            }
            else
            {
                UnityEngine.Object.DestroyImmediate(instance);
            }
        }

        private static string PathUnder(Transform root, Transform node)
        {
            string path = node.name;
            Transform parent = node.parent;
            while (parent != null && parent != root)
            {
                path = parent.name + "/" + path;
                parent = parent.parent;
            }

            return path;
        }
    }
}

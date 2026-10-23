using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace BarPromenade
{
    /// <summary>
    /// The washroom's broken pane looks into a renderer-only reflected room.
    /// Its intact fragments share one plane and one passive production-hero
    /// twin. The authored backing closes the missing corners permanently.
    /// </summary>
    [DefaultExecutionOrder(420)]
    [DisallowMultipleComponent]
    public sealed class BarWashroomMirrorWorld : MonoBehaviour
    {
        public const string RootName = "Bar Washroom Mirror World";
        public const string FallbackName = "Bar Washroom Mirror Fallback";
        public const string SourcePrefix = "Bar Washroom Mirror Source ";
        public const float PlaneX = -2.416f;
        public const float OpeningMinY = 1.26f, OpeningMaxY = 2.10f;
        public const float OpeningMinZ = 4.70f, OpeningMaxZ = 5.66f;
        private const int MaximumSourceRenderers = 4096, MaximumReflectedRenderers = 1024;
        // Hall architecture already belongs to the environment-cube layer by
        // the time this is built. A mirror must never inherit that capture layer.
        private const int PresentationLayer = 0;
        private const string MirrorPrefix = "Bar Washroom Mirror ";
        private readonly List<HomeMirrorSubtreeClone> copies = new List<HomeMirrorSubtreeClone>();
        private readonly List<Renderer> sourceTemplates = new List<Renderer>();
        private readonly List<bool> templateRenderingOff = new List<bool>();
        private readonly List<string> clonedSourceNames = new List<string>();
        private readonly Plane[] frustum = new Plane[6];
        private Transform room, content, space;
        private Camera camera;
        private HomeMirrorHeroTwin twin;
        private Bounds openingWorldBounds;

        public bool IsInitialized { get; private set; }
        public bool IsActive { get; private set; }
        public Renderer Fallback { get; private set; }
        public Transform MirrorSpace => space;
        public Transform TwinRoot => twin?.Root;
        public Player3DAssetRegistry Twin => twin?.Registry;
        public bool HasTwin => twin != null;
        public int ReflectedRendererCount { get; private set; }
        public int StaticCloneCount => copies.Count;
        public int TwinUnpairedBoneCount => twin?.UnpairedBoneCount ?? 0;
        public int TwinUnpairedRendererCount => twin?.UnpairedRendererCount ?? 0;
        public IReadOnlyList<string> ClonedSourceNames => clonedSourceNames;

        public static BarWashroomMirrorWorld Create(Transform parent, Transform room,
            PlayerRuntime player, Camera camera, Transform fixtureFrame)
        {
            if (parent == null || room == null || camera == null || fixtureFrame == null)
                throw new ArgumentException("The bar mirror needs the built room, camera and toilet fixture.");
            var carrier = new GameObject(RootName);
            // Sibling of Room: reflection capture and the GI source scan only
            // visit Room, so neither can ingest this virtual room or its twin.
            carrier.transform.SetParent(parent, false);
            carrier.transform.SetPositionAndRotation(room.position, room.rotation);
            carrier.transform.localScale = room.localScale;
            var mirror = carrier.AddComponent<BarWashroomMirrorWorld>();
            try { mirror.Initialize(room, player, camera, fixtureFrame); }
            catch
            {
                if (Application.isPlaying) Destroy(carrier);
                else DestroyImmediate(carrier);
                throw;
            }
            return mirror;
        }

        private void Initialize(Transform sourceRoom, PlayerRuntime player, Camera sourceCamera,
            Transform fixtureFrame)
        {
            room = sourceRoom;
            camera = sourceCamera;
            Renderer[] sourceRenderers = room.GetComponentsInChildren<Renderer>(true);
            if (sourceRenderers.Length > MaximumSourceRenderers)
                throw new InvalidOperationException("The bar mirror source scan exceeds its fixed budget.");
            foreach (Renderer source in sourceRenderers)
                if (source.name == FallbackName) { Fallback = source; break; }
            if (Fallback == null || !HomeBathroomMirrorWorld.TryGetRoomLocalBounds(
                    Fallback.transform, room, out Bounds fallbackBounds))
                throw new InvalidOperationException("Missing authored bar mirror fallback.");
            if (Mathf.Abs(fallbackBounds.center.x - PlaneX) > .03f ||
                Mathf.Abs(fallbackBounds.min.y - OpeningMinY) > .008f ||
                Mathf.Abs(fallbackBounds.max.y - OpeningMaxY) > .008f ||
                Mathf.Abs(fallbackBounds.min.z - OpeningMinZ) > .008f ||
                Mathf.Abs(fallbackBounds.max.z - OpeningMaxZ) > .008f)
                throw new InvalidOperationException("The authored bar mirror aperture differs from its reflection plane.");

            openingWorldBounds = TransformBounds(new Bounds(
                new Vector3(PlaneX, (OpeningMinY + OpeningMaxY) * .5f, (OpeningMinZ + OpeningMaxZ) * .5f),
                new Vector3(.12f, OpeningMaxY - OpeningMinY + .12f, OpeningMaxZ - OpeningMinZ + .12f)), room);
            content = new GameObject("Bar Washroom Mirror Content").transform;
            content.SetParent(transform, false);
            space = new GameObject("Mirror Space").transform;
            space.SetParent(content, false);
            space.localPosition = new Vector3(2f * PlaneX, 0f, 0f);
            space.localScale = new Vector3(-1f, 1f, 1f);

            // Clipped, Blender-authored patches replace slabs that cross the
            // plane. Mirroring those slabs whole would overlap the real floor.
            foreach (Renderer source in sourceRenderers)
            {
                if (!source.name.StartsWith(SourcePrefix, StringComparison.Ordinal)) continue;
                AddCopy(source.transform, room);
                sourceTemplates.Add(source);
                templateRenderingOff.Add(source.forceRenderingOff);
                source.forceRenderingOff = true;
            }
            if (sourceTemplates.Count != 8)
                throw new InvalidOperationException("The bar mirror requires its complete authored WC and hall shell.");

            // Include the west-side hall context seen through the real open
            // doorway, as well as the WC furnishings. Whole-plane-crossing
            // parts and the pane's own frame/backing are never reflected.
            for (int index = 0; index < room.childCount; index++)
            {
                Transform source = room.GetChild(index);
                if (source.name.StartsWith(MirrorPrefix, StringComparison.Ordinal) ||
                    source.name == "Bar Washroom Left Wall" ||
                    HomeMirrorSubtreeClone.IsEffectNode(source) ||
                    !HomeBathroomMirrorWorld.TryGetRoomLocalBounds(source, room, out Bounds bounds) ||
                    bounds.max.x > PlaneX + .002f || bounds.min.x < -5.9f ||
                    bounds.min.z < -4.9f || bounds.max.z > 7.5f) continue;
                AddCopy(source, room);
            }
            AddCopy(fixtureFrame, room.parent);
            if (!(player.Visual is Player3DCharacterPresentation presentation) || presentation.Registry == null)
                throw new InvalidOperationException("The bar mirror requires the production hero.");
            twin = HomeMirrorHeroTwin.TryCreate(presentation.Registry, room, space,
                "Bar Washroom Mirror Hero Twin", PresentationLayer);
            if (twin == null)
                throw new InvalidOperationException("The production hero's bar mirror twin could not be created.");
            IsInitialized = true;
            ApplyActive(false);
        }

        private void AddCopy(Transform source, Transform sourceFrame)
        {
            HomeMirrorSubtreeClone copy = HomeMirrorSubtreeClone.CreateInFrame(
                source, space, sourceFrame, source.name + " (Mirror)");
            if (copy.RendererCount == 0) { copy.Destroy(); return; }
            ReflectedRendererCount += copy.RendererCount;
            if (ReflectedRendererCount > MaximumReflectedRenderers)
            {
                copy.Destroy();
                throw new InvalidOperationException("The bar mirror exceeds its fixed reflected-renderer budget.");
            }
            foreach (Transform node in copy.Root.GetComponentsInChildren<Transform>(true))
                node.gameObject.layer = PresentationLayer;
            for (int index = 0; index < copy.RendererCount; index++)
            {
                // Virtual geometry may receive the ordinary scene lighting,
                // but must never cast a second shadow into the real washroom.
                copy.CloneRenderer(index).shadowCastingMode = ShadowCastingMode.Off;
            }
            copies.Add(copy);
            clonedSourceNames.Add(source.name);
        }

        public static Vector3 Reflect(Vector3 roomPoint) =>
            new Vector3(2f * PlaneX - roomPoint.x, roomPoint.y, roomPoint.z);

        private void LateUpdate()
        {
            if (!IsInitialized) return;
            bool active = IsVisibleToCamera();
            if (active != IsActive) ApplyActive(active);
            else if (active) Sync();
        }

        private bool IsVisibleToCamera()
        {
            if (camera == null || room == null || !camera.gameObject.activeInHierarchy ||
                room.InverseTransformPoint(camera.transform.position).x >= PlaneX - .005f) return false;
            GeometryUtility.CalculateFrustumPlanes(camera, frustum);
            return GeometryUtility.TestPlanesAABB(frustum, openingWorldBounds);
        }

        private void ApplyActive(bool active)
        {
            IsActive = active;
            if (active) Sync();
            if (content != null) content.gameObject.SetActive(active);
            if (Fallback != null) Fallback.enabled = !active;
        }

        private void Sync()
        {
            foreach (HomeMirrorSubtreeClone copy in copies)
            {
                copy.SyncTransforms();
                copy.SyncRenderers(true);
                copy.SyncPropertyBlocks();
            }
            twin?.Sync();
        }

        private void OnDisable()
        {
            if (IsInitialized) ApplyActive(false);
        }

        private void OnDestroy()
        {
            if (Fallback != null) Fallback.enabled = true;
            twin?.Destroy();
            twin = null;
            foreach (HomeMirrorSubtreeClone copy in copies) copy.Destroy();
            copies.Clear();
            for (int index = 0; index < sourceTemplates.Count; index++)
                if (sourceTemplates[index] != null)
                    sourceTemplates[index].forceRenderingOff = templateRenderingOff[index];
            sourceTemplates.Clear();
            templateRenderingOff.Clear();
        }

        private static Bounds TransformBounds(Bounds local, Transform frame)
        {
            Bounds result = new Bounds(frame.TransformPoint(local.center), Vector3.zero);
            for (int corner = 0; corner < 8; corner++)
                result.Encapsulate(frame.TransformPoint(new Vector3(
                    (corner & 1) == 0 ? local.min.x : local.max.x,
                    (corner & 2) == 0 ? local.min.y : local.max.y,
                    (corner & 4) == 0 ? local.min.z : local.max.z)));
            return result;
        }
    }
}

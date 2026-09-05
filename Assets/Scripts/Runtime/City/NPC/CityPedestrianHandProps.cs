using System;
using System.Collections.Generic;
using UnityEngine;
using Object = UnityEngine.Object;

namespace BarPromenade
{
    /// <summary>
    /// The nine authored hand props of the pedestrian library, in the
    /// order of the generator's `HAND_PROPS` table. The integer values are
    /// serialized on the prop prefabs; append, never renumber.
    /// </summary>
    public enum CityPedestrianHandPropId
    {
        CarpetBeater = 0,
        Cigarette = 1,
        FuneralBouquet = 2,
        Chalk = 3,
        FishingRod = 4,
        SmokingPipe = 5,
        CafeCigarette = 6,
        ServiceTowel = 7,
        CoffeePot = 8
    }

    /// <summary>
    /// Hand props are separate prefabs attached to a rig socket, never
    /// parts of a body.
    ///
    /// The user's rule of 2026-09-05: «все предметы нужно отделить от
    /// моделек — они должны быть дополнительными ручными поделками». Before
    /// it, the babushka's beater and cigarette, the mourner's bouquet, the
    /// weigher's chalk, the fisherman's rod and pipe and the cafe's
    /// cigarette, towel and coffee pot were skinned `ACC_*` parts of their
    /// bodies, every anonymous copy of a design carried them onto the
    /// street, and three unrelated name tables (the roaming pool, the
    /// balcony smoker, the courtyard) each hid a different subset. Now a
    /// body ships nothing in its hands; the drying yard attaches the
    /// beater, the cemetery attaches the bouquet, and a pooled walker has
    /// nothing to hide.
    ///
    /// Every prop is Blender-authored geometry from
    /// `tools/build-city-pedestrian-3d-model.py` (`--hand-props-only`),
    /// built into `Resources/Pedestrians/HandProps/` by
    /// `CityPedestrianHandPropAssetSetup`, which measures the socket-
    /// relative pose in the bind pose off the imported meshes. Nothing
    /// here re-derives an FBX axis or the 100x bone scale: the prefab's
    /// Mount carries both, so attaching is `SetParent(socket)` at identity.
    /// </summary>
    public static class CityPedestrianHandProps
    {
        public const string ResourceFolder = "Pedestrians/HandProps";

        public const string GripRightSocketName = "SOCKET_Grip.R";
        public const string GripLeftSocketName = "SOCKET_Grip.L";
        public const string CigaretteRightSocketName = "SOCKET_Cigarette.R";
        public const string MouthSocketName = "SOCKET_Mouth";

        /// <summary>The far end of the rod, on <see cref="CityPedestrianHandPropId.FishingRod"/>.</summary>
        public const string RodTipAnchorName = "ANCHOR_RodTip";

        /// <summary>The top of the bowl, on <see cref="CityPedestrianHandPropId.SmokingPipe"/>.</summary>
        public const string PipeEmberAnchorName = "ANCHOR_PipeEmber";

        /// <summary>The spout lip, on <see cref="CityPedestrianHandPropId.CoffeePot"/>, forward along the spout.</summary>
        public const string CoffeePotSpoutAnchorName = "SOCKET_CafePotSpout";

        private static readonly CityPedestrianHandPropId[] AllIds =
        {
            CityPedestrianHandPropId.CarpetBeater,
            CityPedestrianHandPropId.Cigarette,
            CityPedestrianHandPropId.FuneralBouquet,
            CityPedestrianHandPropId.Chalk,
            CityPedestrianHandPropId.FishingRod,
            CityPedestrianHandPropId.SmokingPipe,
            CityPedestrianHandPropId.CafeCigarette,
            CityPedestrianHandPropId.ServiceTowel,
            CityPedestrianHandPropId.CoffeePot
        };

        /// <summary>Every prop, in enum order, for sweeps and test cases.</summary>
        public static IReadOnlyList<CityPedestrianHandPropId> Ids => AllIds;

        /// <summary>The generator's id for a prop: `carpet_beater` and so on.</summary>
        public static string GetManifestId(CityPedestrianHandPropId id)
        {
            switch (id)
            {
                case CityPedestrianHandPropId.CarpetBeater:
                    return "carpet_beater";
                case CityPedestrianHandPropId.Cigarette:
                    return "cigarette";
                case CityPedestrianHandPropId.FuneralBouquet:
                    return "funeral_bouquet";
                case CityPedestrianHandPropId.Chalk:
                    return "chalk";
                case CityPedestrianHandPropId.FishingRod:
                    return "fishing_rod";
                case CityPedestrianHandPropId.SmokingPipe:
                    return "smoking_pipe";
                case CityPedestrianHandPropId.CafeCigarette:
                    return "cafe_cigarette";
                case CityPedestrianHandPropId.ServiceTowel:
                    return "service_towel";
                case CityPedestrianHandPropId.CoffeePot:
                    return "coffee_pot";
                default:
                    throw new ArgumentOutOfRangeException(nameof(id), id, null);
            }
        }

        /// <summary>The prefab file name, which is also the enum name.</summary>
        public static string GetPrefabName(CityPedestrianHandPropId id)
        {
            GetManifestId(id);
            return id.ToString();
        }

        public static string GetResourcePath(CityPedestrianHandPropId id)
        {
            return ResourceFolder + "/" + GetPrefabName(id);
        }

        /// <summary>
        /// The one socket each prop is authored for. The chalk and the
        /// bouquet ride the right grip like the beater and the rod; the
        /// cigarettes ride the dedicated cigarette socket the hero's own
        /// cigarette uses; the pipe rides the mouth; the towel is the one
        /// left-hand prop.
        /// </summary>
        public static string GetSocketName(CityPedestrianHandPropId id)
        {
            switch (id)
            {
                case CityPedestrianHandPropId.CarpetBeater:
                case CityPedestrianHandPropId.FuneralBouquet:
                case CityPedestrianHandPropId.Chalk:
                case CityPedestrianHandPropId.FishingRod:
                case CityPedestrianHandPropId.CoffeePot:
                    return GripRightSocketName;
                case CityPedestrianHandPropId.Cigarette:
                case CityPedestrianHandPropId.CafeCigarette:
                    return CigaretteRightSocketName;
                case CityPedestrianHandPropId.SmokingPipe:
                    return MouthSocketName;
                case CityPedestrianHandPropId.ServiceTowel:
                    return GripLeftSocketName;
                default:
                    throw new ArgumentOutOfRangeException(nameof(id), id, null);
            }
        }

        public static GameObject LoadPrefab(CityPedestrianHandPropId id)
        {
            return Resources.Load<GameObject>(GetResourcePath(id));
        }

        /// <summary>
        /// Whether the prop can be attached at all: the prefab exists and
        /// carries a registry declaring the expected socket. The balcony
        /// smoker's catalog gates on this instead of on a babushka source.
        /// </summary>
        public static bool IsAvailable(CityPedestrianHandPropId id)
        {
            GameObject prefab = LoadPrefab(id);
            CityPedestrianHandPropRegistry registry = prefab != null
                ? prefab.GetComponent<CityPedestrianHandPropRegistry>()
                : null;
            return registry != null &&
                   registry.Id == id &&
                   registry.Mount != null &&
                   registry.Renderers.Count > 0 &&
                   string.Equals(
                       registry.SocketName,
                       GetSocketName(id),
                       StringComparison.Ordinal);
        }

        /// <summary>
        /// Finds the named socket under a model root by exact name, the
        /// way every pedestrian socket lookup works. Null when absent.
        /// </summary>
        public static Transform FindSocket(Transform modelRoot, string socketName)
        {
            if (modelRoot == null || string.IsNullOrEmpty(socketName))
            {
                return null;
            }

            if (string.Equals(modelRoot.name, socketName, StringComparison.Ordinal))
            {
                return modelRoot;
            }

            for (int index = 0; index < modelRoot.childCount; index++)
            {
                Transform found = FindSocket(modelRoot.GetChild(index), socketName);
                if (found != null)
                {
                    return found;
                }
            }

            return null;
        }

        public static Transform FindSocket(Transform modelRoot, CityPedestrianHandPropId id)
        {
            return FindSocket(modelRoot, GetSocketName(id));
        }

        /// <summary>
        /// Attaches a prop to a pedestrian body: the socket is found under
        /// the body's model root, the body's shared material is copied so
        /// the prop renders exactly like the hand that holds it, and the
        /// body's current palette variant tints it unless one is given.
        /// </summary>
        public static CityPedestrianHandPropRegistry Attach(
            CityPedestrianAssetRegistry body,
            CityPedestrianHandPropId id,
            int? paletteVariant = null)
        {
            if (body == null || body.ModelRoot == null)
            {
                throw new ArgumentNullException(nameof(body));
            }

            Transform socket = FindSocket(body.ModelRoot, id);
            if (socket == null)
            {
                throw new InvalidOperationException(
                    $"Pedestrian '{body.DesignId}' has no '{GetSocketName(id)}' " +
                    $"socket for the {id} prop.");
            }

            Material material = null;
            for (int index = 0; index < body.Renderers.Count && material == null; index++)
            {
                Renderer renderer = body.Renderers[index];
                if (renderer != null)
                {
                    material = renderer.sharedMaterial;
                }
            }

            return Attach(socket, id, material, paletteVariant ?? body.PaletteVariant);
        }

        /// <summary>
        /// Attaches a prop to an explicit socket transform — for rigs that
        /// are not <see cref="CityPedestrianAssetRegistry"/> bodies, like
        /// the cafe cast. The socket's name must be the prop's declared
        /// socket; a prop measured against one socket has no meaning under
        /// another, and a silent misfit would be a prop in the wrong hand.
        /// </summary>
        public static CityPedestrianHandPropRegistry Attach(
            Transform socket,
            CityPedestrianHandPropId id,
            Material sharedMaterial,
            int paletteVariant)
        {
            if (socket == null)
            {
                throw new ArgumentNullException(nameof(socket));
            }

            string expectedSocket = GetSocketName(id);
            if (!string.Equals(socket.name, expectedSocket, StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    $"The {id} prop is authored for '{expectedSocket}', not for " +
                    $"'{socket.name}'.");
            }

            CityPedestrianHandPropRegistry registry = Instantiate(id);
            Transform root = registry.transform;
            root.SetParent(socket, false);
            root.localPosition = Vector3.zero;
            root.localRotation = Quaternion.identity;
            root.localScale = Vector3.one;
            registry.RestoreMountToSocketPose();
            SetLayerRecursively(root, socket.gameObject.layer);
            registry.SetSharedMaterial(sharedMaterial);
            registry.ApplyPaletteVariant(paletteVariant);
            return registry;
        }

        /// <summary>
        /// A prop standing in the world rather than in a hand: the bouquet
        /// left on a grave slab. The Mount is reset, so the parts sit in
        /// the import frame in metres with the socket head at the root;
        /// the caller orients the root.
        /// </summary>
        public static CityPedestrianHandPropRegistry Place(
            CityPedestrianHandPropId id,
            Transform parent,
            Vector3 localPosition,
            Quaternion localRotation,
            Material sharedMaterial,
            int paletteVariant)
        {
            CityPedestrianHandPropRegistry registry = Instantiate(id);
            Transform root = registry.transform;
            root.SetParent(parent, false);
            root.localPosition = localPosition;
            root.localRotation = localRotation;
            root.localScale = Vector3.one;
            registry.ResetMountToFreeStanding();
            if (parent != null)
            {
                SetLayerRecursively(root, parent.gameObject.layer);
            }

            registry.SetSharedMaterial(sharedMaterial);
            registry.ApplyPaletteVariant(paletteVariant);
            return registry;
        }

        /// <summary>
        /// Destroys an attached or placed prop. Null-safe, so a role that
        /// may or may not hold something can always call it on release.
        /// </summary>
        public static void Detach(ref CityPedestrianHandPropRegistry attached)
        {
            if (attached == null)
            {
                return;
            }

            GameObject target = attached.gameObject;
            attached = null;
            if (target == null)
            {
                return;
            }

            if (Application.isPlaying)
            {
                Object.Destroy(target);
            }
            else
            {
                Object.DestroyImmediate(target);
            }
        }

        /// <summary>The prop instance currently under a socket, if any.</summary>
        public static CityPedestrianHandPropRegistry FindAttached(
            Transform socket,
            CityPedestrianHandPropId id)
        {
            if (socket == null)
            {
                return null;
            }

            for (int index = 0; index < socket.childCount; index++)
            {
                var registry = socket.GetChild(index)
                    .GetComponent<CityPedestrianHandPropRegistry>();
                if (registry != null && registry.Id == id)
                {
                    return registry;
                }
            }

            return null;
        }

        private static CityPedestrianHandPropRegistry Instantiate(
            CityPedestrianHandPropId id)
        {
            GameObject prefab = LoadPrefab(id);
            if (prefab == null)
            {
                throw new InvalidOperationException(
                    $"Hand prop prefab '{GetResourcePath(id)}' is missing; run " +
                    "the City Pedestrian 3D hand prop build.");
            }

            GameObject instance = Object.Instantiate(prefab);
            instance.name = prefab.name;
            var registry = instance.GetComponent<CityPedestrianHandPropRegistry>();
            if (registry == null || registry.Mount == null)
            {
                Object.DestroyImmediate(instance);
                throw new InvalidOperationException(
                    $"Hand prop prefab '{GetResourcePath(id)}' carries no " +
                    nameof(CityPedestrianHandPropRegistry) + " with a Mount.");
            }

            if (registry.Id != id)
            {
                Object.DestroyImmediate(instance);
                throw new InvalidOperationException(
                    $"Hand prop prefab '{GetResourcePath(id)}' declares " +
                    $"{registry.Id}, not {id}.");
            }

            return registry;
        }

        private static void SetLayerRecursively(Transform root, int layer)
        {
            root.gameObject.layer = layer;
            for (int index = 0; index < root.childCount; index++)
            {
                SetLayerRecursively(root.GetChild(index), layer);
            }
        }
    }
}

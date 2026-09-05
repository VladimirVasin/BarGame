using System;
using System.Collections.Generic;
using System.IO;
using BarPromenade;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEditor.SceneManagement;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace BarPromenade.Editor
{
    /// <summary>
    /// Builds the nine hand-prop prefabs under
    /// `Resources/Pedestrians/HandProps/` from the generator's prop
    /// library FBX and manifest.
    ///
    /// Every prop was authored on ONE reference body (the beater on the
    /// babushka, the bouquet on the mourner, the rod on the fisherman...)
    /// and the generator exports it exactly where that body carried it,
    /// under an Empty `PROP_&lt;PrefabName&gt;` standing at the socket bone
    /// head. This build instantiates the prop library AND the reference
    /// body FBX at identity in one scene, so the two share a frame, and
    /// measures the socket-relative pose once:
    ///
    ///   mount      = socket.worldToLocal · Translate(E)
    ///   freeLocal  = Translate(−E) · part.localToWorld
    ///
    /// with E the Empty's world position. The prefab's `Mount` carries
    /// `mount` (which folds in the inverse of the 100x FBX bone scale
    /// and the axis conversion), the parts carry `freeLocal`, and the
    /// runtime merely parents the root under the socket at identity.
    /// Under an identity Mount the parts stand in the import frame in
    /// metres with the socket head at the origin — the pose a bouquet
    /// laid on a grave slab uses. Nothing at runtime re-derives an FBX
    /// axis; nothing here hard-codes one either.
    ///
    /// The registry stores the socket's rest pose as measured, and the
    /// validation re-measures the live body FBX against it: a Mount is
    /// only right for the socket it was measured on, so a moved socket
    /// queues a rebuild exactly as a changed manifest signature does.
    /// </summary>
    [InitializeOnLoad]
    public static class CityPedestrianHandPropAssetSetup
    {
        public const string ModelPath =
            "Assets/Pedestrians/Props/CityPedestrianHandProps.fbx";
        public const string ManifestPath =
            "Assets/Pedestrians/Props/CityPedestrianHandProps.json";
        public const string PrefabFolder =
            "Assets/Resources/Pedestrians/HandProps";
        public const string SharedMaterialPath =
            CityPedestrianAssetSetup.SharedMaterialPath;
        public const string LibraryName = "CityPedestrianHandProps";
        public const string MountName = "Mount";
        public const string PropRootPrefix = "PROP_";
        public const string CafeWomanDesignId = "cafe_couple_woman_v2";
        public const string CafeAttendantDesignId = "cafe_attendant_v2";

        /// <summary>
        /// The generator parents every part to its Empty with the socket
        /// head as the origin; a part whose origin drifted from the Empty
        /// would measure a Mount for a different point than the one the
        /// geometry was authored around.
        /// </summary>
        private const float PartOriginTolerance = 0.0001f;

        /// <summary>
        /// Blender's FBX_SCALE_NONE leaves the unit scale (~100) on the
        /// bone chain, so the socket's lossyScale is compared RELATIVELY:
        /// a non-uniform socket cannot be folded into one TRS Mount.
        /// </summary>
        private const float SocketScaleRelativeTolerance = 0.0001f;

        /// <summary>
        /// The Empty is authored at the socket bone head; the imported
        /// socket must stand there too, or the prop is measured for a
        /// point the hand never reaches.
        /// </summary>
        private const float MountOffsetTolerance = 0.02f;
        private const float DecompositionRelativeTolerance = 0.00001f;
        private const float RestPositionTolerance = 0.0001f;
        private const float RestAngleTolerance = 0.02f;

        private const string AnchorKindFarthestFromSocket =
            "farthest_from_socket";
        private const string AnchorKindPartCenter = "part_center";
        private const string AnchorKindFarthestFromPart =
            "farthest_from_part";

        /// <summary>
        /// Every body FBX a prop may be measured against. The importer
        /// re-queues the prop build when one of these lands, so the list
        /// is checked against the manifest at build time: a reference
        /// design outside it would build fine and then never rebuild
        /// when its skeleton moved.
        /// </summary>
        private static readonly string[] ReferenceModelPaths =
        {
            CityPedestrianAssetSetup.YardBabushkaModelPath,
            CityPedestrianAssetSetup.CemeteryMournerModelPath,
            CityPedestrianAssetSetup.WeighAttendantModelPath,
            CityPedestrianAssetSetup.LakeFishermanModelPath,
            MountainRoadCafeCastAssetSetup.PairWomanModelPath,
            MountainRoadCafeCastAssetSetup.AttendantModelPath
        };

        private static bool isBuilding;
        private static bool buildQueued;

        public static bool IsBuilding => isBuilding;

        static CityPedestrianHandPropAssetSetup()
        {
            if (!Application.isBatchMode)
            {
                EditorApplication.delayCall += ValidateDependencyStamp;
            }
        }

        [MenuItem("Bar Promenade/City Pedestrian 3D/Build Hand Props")]
        public static void Run()
        {
            BuildOrThrow();
            Debug.Log(
                "City pedestrian hand prop prefabs rebuilt and validated.");
        }

        [MenuItem("Bar Promenade/City Pedestrian 3D/Validate Hand Props")]
        public static void RunValidation()
        {
            ValidateOrThrow();
            Debug.Log(
                "City pedestrian hand prop library, prefabs and reference " +
                "socket rest poses are valid.");
        }

        public static string GetPrefabPath(CityPedestrianHandPropId id)
        {
            return PrefabFolder + "/" +
                   CityPedestrianHandProps.GetPrefabName(id) + ".prefab";
        }

        /// <summary>
        /// Whether an imported asset invalidates the prop prefabs: the
        /// library itself, a reference body FBX or the shared material.
        /// </summary>
        public static bool IsBuildTriggerPath(string path)
        {
            if (string.IsNullOrEmpty(path))
            {
                return false;
            }

            if (string.Equals(path, ModelPath, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(path, ManifestPath, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(path, SharedMaterialPath, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            for (int index = 0; index < ReferenceModelPaths.Length; index++)
            {
                if (string.Equals(
                        path,
                        ReferenceModelPaths[index],
                        StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            return false;
        }

        public static bool SourcesExist()
        {
            if (!File.Exists(ModelPath) ||
                !File.Exists(ManifestPath) ||
                !File.Exists(SharedMaterialPath))
            {
                return false;
            }

            for (int index = 0; index < ReferenceModelPaths.Length; index++)
            {
                if (!File.Exists(ReferenceModelPaths[index]))
                {
                    return false;
                }
            }

            return true;
        }

        public static void QueueBuildWhenSourcesExist()
        {
            if (isBuilding || buildQueued || !SourcesExist())
            {
                return;
            }

            buildQueued = true;
            EditorApplication.delayCall += RunQueuedBuild;
        }

        private static void RunQueuedBuild()
        {
            buildQueued = false;
            if (!SourcesExist())
            {
                return;
            }

            // The body pipelines force-import the reference FBXs this
            // build measures against; measuring mid-import would read a
            // half-imported skeleton, so wait for them to finish.
            if (EditorApplication.isCompiling ||
                EditorApplication.isUpdating ||
                NpcHumanV2AssetSetup.IsAnyPipelineBuilding ||
                Player3DV2AssetSetup.IsBuilding)
            {
                QueueBuildWhenSourcesExist();
                return;
            }

            try
            {
                BuildOrThrow();
            }
            catch (Exception exception)
            {
                Debug.LogError(
                    $"Could not build City pedestrian hand props: {exception}");
            }
        }

        public static void BuildOrThrow()
        {
            if (isBuilding)
            {
                return;
            }

            if (!SourcesExist())
            {
                throw new InvalidOperationException(
                    "City pedestrian hand prop build requires the prop " +
                    "library FBX/manifest, every reference body FBX and " +
                    "the shared Player3DLit material.");
            }

            isBuilding = true;
            GameObject propInstance = null;
            // Everything the build instantiates - the prop library, the
            // reference bodies and the prefab roots - lives in a preview
            // scene, never in whatever scene the user has open: the
            // dependency stamp runs this on every domain reload, and six
            // body FBXs materialising in the open scene would dirty it.
            var previewScope = new PreviewSceneScope();
            try
            {
                // The generator may have written the library into a folder
                // the asset database has never seen; importing an unknown
                // path is a silent no-op that leaves LoadAssetAtPath null.
                if (!AssetDatabase.IsValidFolder(
                        Path.GetDirectoryName(ModelPath)?.Replace('\\', '/')))
                {
                    AssetDatabase.Refresh();
                }

                CityPedestrianAssetSetup.EnsureFolderForAsset(
                    GetPrefabPath(CityPedestrianHandPropId.CarpetBeater));

                AssetDatabase.ImportAsset(
                    ModelPath,
                    ImportAssetOptions.ForceUpdate |
                    ImportAssetOptions.ForceSynchronousImport);
                AssetDatabase.ImportAsset(
                    ManifestPath,
                    ImportAssetOptions.ForceUpdate |
                    ImportAssetOptions.ForceSynchronousImport);
                for (int index = 0; index < ReferenceModelPaths.Length; index++)
                {
                    AssetDatabase.ImportAsset(
                        ReferenceModelPaths[index],
                        ImportAssetOptions.ForceUpdate |
                        ImportAssetOptions.ForceSynchronousImport);
                }

                HandPropLibraryManifest manifest = LoadAndValidateManifest();
                Material sharedMaterial =
                    AssetDatabase.LoadAssetAtPath<Material>(SharedMaterialPath);
                if (sharedMaterial == null)
                {
                    throw new InvalidOperationException(
                        "Shared Player3DLit material is missing at " +
                        $"'{SharedMaterialPath}'.");
                }

                GameObject modelAsset =
                    AssetDatabase.LoadAssetAtPath<GameObject>(ModelPath);
                if (modelAsset == null)
                {
                    throw new InvalidOperationException(
                        $"Hand prop library FBX did not import at '{ModelPath}'.");
                }

                propInstance = Object.Instantiate(modelAsset);
                propInstance.name = modelAsset.name;
                MoveToPreviewScene(propInstance);
                ResetToIdentity(propInstance.transform);

                for (int index = 0; index < manifest.props.Length; index++)
                {
                    BuildProp(
                        manifest,
                        manifest.props[index],
                        CityPedestrianHandProps.Ids[index],
                        propInstance,
                        sharedMaterial);
                }

                AssetDatabase.SaveAssets();
                ValidateOrThrow();
            }
            finally
            {
                if (propInstance != null)
                {
                    Object.DestroyImmediate(propInstance);
                }

                previewScope.Dispose();
                isBuilding = false;
            }
        }

        /// <summary>
        /// Rejects a stale or half-built library: every prefab must match
        /// the manifest part for part and triangle for triangle, and the
        /// reference socket it was measured on must still rest where it
        /// did. Throws <see cref="InvalidOperationException"/> on any
        /// drift, which the interactive dependency stamp turns into a
        /// queued rebuild.
        /// </summary>
        public static void ValidateOrThrow()
        {
            HandPropLibraryManifest manifest = LoadAndValidateManifest();
            Material expectedMaterial =
                AssetDatabase.LoadAssetAtPath<Material>(SharedMaterialPath);
            if (expectedMaterial == null)
            {
                throw new InvalidOperationException(
                    "Shared Player3DLit material is missing at " +
                    $"'{SharedMaterialPath}'.");
            }

            var restPoses = new Dictionary<string, SocketRestPose>(
                StringComparer.Ordinal);
            for (int index = 0; index < manifest.props.Length; index++)
            {
                HandPropManifest prop = manifest.props[index];
                CityPedestrianHandPropId id = CityPedestrianHandProps.Ids[index];
                CityPedestrianHandPropRegistry registry =
                    ValidatePrefab(manifest, prop, id, expectedMaterial);

                string restKey = prop.reference_design + "|" + prop.socket;
                if (!restPoses.TryGetValue(restKey, out SocketRestPose rest))
                {
                    rest = MeasureSocketRest(prop.reference_design, prop.socket);
                    restPoses.Add(restKey, rest);
                }

                float drift = Vector3.Distance(
                    registry.ReferenceSocketRestPosition,
                    rest.Position);
                float turn = Quaternion.Angle(
                    registry.ReferenceSocketRestRotation,
                    rest.Rotation);
                if (drift > RestPositionTolerance || turn > RestAngleTolerance)
                {
                    throw new InvalidOperationException(
                        $"Hand prop '{prop.prefab_name}' was measured on a " +
                        $"'{prop.socket}' of {prop.reference_design} that has " +
                        $"since moved by {drift:0.#####} m / {turn:0.###} deg; " +
                        "rebuild the hand props.");
                }
            }
        }

        private static void ValidateDependencyStamp()
        {
            if (!SourcesExist())
            {
                return;
            }

            try
            {
                ValidateOrThrow();
            }
            catch (InvalidOperationException)
            {
                QueueBuildWhenSourcesExist();
            }
            catch (Exception exception)
            {
                Debug.LogError(
                    "Could not inspect City pedestrian hand prop assets: " +
                    exception);
            }
        }

        // ------------------------------------------------------------------
        // Build
        // ------------------------------------------------------------------

        private static void BuildProp(
            HandPropLibraryManifest manifest,
            HandPropManifest prop,
            CityPedestrianHandPropId id,
            GameObject propInstance,
            Material sharedMaterial)
        {
            GameObject body = null;
            GameObject prefabRoot = null;
            try
            {
                Transform propRoot = RequireChild(
                    propInstance.transform,
                    prop.root,
                    "hand prop library");
                Vector3 emptyPosition = propRoot.position;

                // Every part must be a direct child standing on the Empty:
                // the generator parents them with the socket head as
                // origin, and the free frame below assumes exactly that.
                var partTransforms = new Transform[prop.parts.Length];
                var partMeshes = new Mesh[prop.parts.Length];
                var partsByName = new Dictionary<string, int>(StringComparer.Ordinal);
                for (int index = 0; index < prop.parts.Length; index++)
                {
                    HandPropManifestPart part = prop.parts[index];
                    Transform partTransform = RequireChild(
                        propRoot,
                        part.name,
                        prop.root);
                    if (partTransform.parent != propRoot)
                    {
                        throw new InvalidOperationException(
                            $"Hand prop part '{part.name}' is not a direct " +
                            $"child of '{prop.root}'.");
                    }

                    float originOffset = Vector3.Distance(
                        partTransform.position,
                        emptyPosition);
                    if (originOffset > PartOriginTolerance)
                    {
                        throw new InvalidOperationException(
                            $"Hand prop part '{part.name}' origin sits " +
                            $"{originOffset:0.######} m off its Empty " +
                            $"'{prop.root}'; the generator must parent it " +
                            "at the socket head.");
                    }

                    MeshFilter filter = partTransform.GetComponent<MeshFilter>();
                    Mesh mesh = filter != null ? filter.sharedMesh : null;
                    if (mesh == null ||
                        !string.Equals(
                            AssetDatabase.GetAssetPath(mesh),
                            ModelPath,
                            StringComparison.Ordinal))
                    {
                        throw new InvalidOperationException(
                            $"Hand prop part '{part.name}' carries no mesh " +
                            "from the prop library FBX.");
                    }

                    int triangles = TriangleCount(mesh);
                    if (triangles != part.triangles)
                    {
                        throw new InvalidOperationException(
                            $"Hand prop part '{part.name}' imported " +
                            $"{triangles} triangles; its manifest says " +
                            $"{part.triangles}.");
                    }

                    if (!partsByName.TryAdd(part.name, index))
                    {
                        throw new InvalidOperationException(
                            $"Hand prop '{prop.prefab_name}' manifest lists " +
                            $"'{part.name}' twice.");
                    }

                    partTransforms[index] = partTransform;
                    partMeshes[index] = mesh;
                }

                int meshChildren = 0;
                for (int index = 0; index < propRoot.childCount; index++)
                {
                    if (propRoot.GetChild(index).GetComponent<MeshFilter>() != null)
                    {
                        meshChildren++;
                    }
                }

                if (meshChildren != prop.parts.Length)
                {
                    throw new InvalidOperationException(
                        $"'{prop.root}' carries {meshChildren} meshes; its " +
                        $"manifest lists {prop.parts.Length}.");
                }

                // The reference body, at identity in the same scene, gives
                // the socket the prop was authored around.
                body = InstantiateReferenceBody(prop.reference_design);
                Transform socket = CityPedestrianHandProps.FindSocket(
                    body.transform,
                    prop.socket);
                if (socket == null)
                {
                    throw new InvalidOperationException(
                        $"Reference body '{prop.reference_design}' has no " +
                        $"'{prop.socket}' socket for the {prop.prefab_name} prop.");
                }

                RequireUniformScale(socket, prop.prefab_name);
                float mountOffset = Vector3.Distance(socket.position, emptyPosition);
                if (mountOffset > MountOffsetTolerance)
                {
                    throw new InvalidOperationException(
                        $"Hand prop '{prop.prefab_name}' Empty stands " +
                        $"{mountOffset:0.####} m from '{prop.socket}' of " +
                        $"{prop.reference_design}; the generator authored it " +
                        "at a different socket head than the body imports.");
                }

                Matrix4x4 mountMatrix =
                    socket.worldToLocalMatrix * Matrix4x4.Translate(emptyPosition);
                Decompose(
                    mountMatrix,
                    "Mount of " + prop.prefab_name,
                    out Vector3 mountPosition,
                    out Quaternion mountRotation,
                    out Vector3 mountScale);

                // Assemble with the Mount at identity: the parts then stand
                // in the free frame and anchors can be placed in world
                // space straight from the measurement.
                prefabRoot = new GameObject(prop.prefab_name);
                MoveToPreviewScene(prefabRoot);
                ResetToIdentity(prefabRoot.transform);
                var registry = prefabRoot.AddComponent<CityPedestrianHandPropRegistry>();
                Transform mount = new GameObject(MountName).transform;
                mount.SetParent(prefabRoot.transform, false);
                ResetToIdentity(mount);

                var renderers = new Renderer[prop.parts.Length];
                var bindings = new CityPedestrianRendererBinding[prop.parts.Length];
                for (int index = 0; index < prop.parts.Length; index++)
                {
                    HandPropManifestPart part = prop.parts[index];
                    Transform source = partTransforms[index];
                    Matrix4x4 freeLocal =
                        Matrix4x4.Translate(-emptyPosition) * source.localToWorldMatrix;
                    Decompose(
                        freeLocal,
                        part.name,
                        out Vector3 partPosition,
                        out Quaternion partRotation,
                        out Vector3 partScale);

                    // Mount · freeLocal must give the part back exactly where
                    // the reference body carried it; a decomposition that
                    // dropped a mirror or a shear would fail here, not in
                    // somebody's hand.
                    Matrix4x4 rebuilt =
                        socket.localToWorldMatrix *
                        Matrix4x4.TRS(mountPosition, mountRotation, mountScale) *
                        Matrix4x4.TRS(partPosition, partRotation, partScale);
                    RequireMatrixMatch(
                        rebuilt,
                        source.localToWorldMatrix,
                        0.0001f,
                        $"'{part.name}' through the Mount");

                    var partObject = new GameObject(part.name);
                    Transform partTransform = partObject.transform;
                    partTransform.SetParent(mount, false);
                    partTransform.localPosition = partPosition;
                    partTransform.localRotation = partRotation;
                    partTransform.localScale = partScale;

                    partObject.AddComponent<MeshFilter>().sharedMesh = partMeshes[index];
                    var renderer = partObject.AddComponent<MeshRenderer>();
                    renderer.sharedMaterials = new[] { sharedMaterial };
                    renderer.shadowCastingMode = ShadowCastingMode.On;
                    renderer.receiveShadows = true;
                    renderer.motionVectorGenerationMode =
                        MotionVectorGenerationMode.Object;
                    renderers[index] = renderer;

                    Color baseColor = CityPedestrianAssetSetup.ParseColor(part.base_color);
                    bindings[index] = new CityPedestrianRendererBinding(
                        part.name,
                        part.role,
                        part.palette_name,
                        renderer,
                        baseColor,
                        CityPedestrianAssetSetup.BuildPaletteVariant(
                            part.palette_name, baseColor, 1),
                        CityPedestrianAssetSetup.BuildPaletteVariant(
                            part.palette_name, baseColor, 2),
                        CityPedestrianAssetSetup.BuildPaletteVariant(
                            part.palette_name, baseColor, 3));
                }

                HandPropManifestAnchor[] anchorSources =
                    prop.anchors ?? Array.Empty<HandPropManifestAnchor>();
                var anchors = new CityPedestrianHandPropAnchor[anchorSources.Length];
                var anchorReport = new System.Text.StringBuilder();
                for (int index = 0; index < anchorSources.Length; index++)
                {
                    HandPropManifestAnchor source = anchorSources[index];
                    MeasureAnchor(
                        source,
                        prop,
                        partsByName,
                        partTransforms,
                        partMeshes,
                        socket.position,
                        out Vector3 anchorWorld,
                        out Quaternion anchorRotation);
                    Transform anchor = new GameObject(source.name).transform;
                    anchor.position = anchorWorld - emptyPosition;
                    anchor.rotation = anchorRotation;
                    anchor.SetParent(mount, true);
                    anchors[index] = new CityPedestrianHandPropAnchor(source.name, anchor);
                    anchorReport.Append(
                        $"; {source.name} {Vector3.Distance(anchorWorld, socket.position):0.####} m " +
                        "from the socket");
                    if (!string.IsNullOrEmpty(source.axis_from))
                    {
                        int axisIndex = partsByName[source.axis_from];
                        Vector3 axisCentre = partTransforms[axisIndex].TransformPoint(
                            partMeshes[axisIndex].bounds.center);
                        anchorReport.Append(
                            $", {Vector3.Distance(anchorWorld, axisCentre):0.####} m " +
                            $"from {source.axis_from} centre");
                    }
                }

                mount.localPosition = mountPosition;
                mount.localRotation = mountRotation;
                mount.localScale = mountScale;

                registry.Configure(
                    id,
                    prop.id,
                    prop.socket,
                    prop.reference_design,
                    mount,
                    renderers,
                    bindings,
                    anchors,
                    prop.triangle_count,
                    manifest.generator_version,
                    manifest.build_signature,
                    socket.position,
                    socket.rotation);

                string prefabPath = GetPrefabPath(id);
                GameObject saved = PrefabUtility.SaveAsPrefabAsset(prefabRoot, prefabPath);
                if (saved == null)
                {
                    throw new InvalidOperationException(
                        $"Could not save hand prop prefab '{prefabPath}'.");
                }

                Debug.Log(
                    $"Hand prop {prop.prefab_name}: Mount local position " +
                    $"{mountPosition:F5} (|p| = {mountPosition.magnitude:0.#####}), " +
                    $"rotation {mountRotation.eulerAngles:F3}, scale {mountScale:F5}; " +
                    $"Empty at {emptyPosition:F5}, {prop.socket} at " +
                    $"{socket.position:F5} (offset {mountOffset:0.######} m), " +
                    $"socket lossyScale {socket.lossyScale:F4}{anchorReport}.");
            }
            finally
            {
                if (prefabRoot != null)
                {
                    Object.DestroyImmediate(prefabRoot);
                }

                if (body != null)
                {
                    Object.DestroyImmediate(body);
                }
            }
        }

        /// <summary>
        /// Anchors are measured off the imported vertices in the measuring
        /// scene, exactly as the body anchors used to be: the rod tip is
        /// the vertex farthest from the socket, the pipe ember the centre
        /// of its part, the spout lip the vertex farthest from the pot
        /// body — looking along the spout, so a pour stream can leave it.
        /// </summary>
        private static void MeasureAnchor(
            HandPropManifestAnchor source,
            HandPropManifest prop,
            IReadOnlyDictionary<string, int> partsByName,
            Transform[] partTransforms,
            Mesh[] partMeshes,
            Vector3 socketPosition,
            out Vector3 worldPosition,
            out Quaternion worldRotation)
        {
            if (string.IsNullOrEmpty(source.name) ||
                string.IsNullOrEmpty(source.part) ||
                !partsByName.TryGetValue(source.part, out int partIndex))
            {
                throw new InvalidOperationException(
                    $"Hand prop '{prop.prefab_name}' anchor '{source.name}' " +
                    $"names an unknown part '{source.part}'.");
            }

            Transform part = partTransforms[partIndex];
            Mesh mesh = partMeshes[partIndex];
            switch (source.kind)
            {
                case AnchorKindFarthestFromSocket:
                    worldPosition = FarthestVertex(part, mesh, socketPosition);
                    worldRotation = Quaternion.identity;
                    return;
                case AnchorKindPartCenter:
                    worldPosition = part.TransformPoint(mesh.bounds.center);
                    worldRotation = Quaternion.identity;
                    return;
                case AnchorKindFarthestFromPart:
                    if (string.IsNullOrEmpty(source.axis_from) ||
                        !partsByName.TryGetValue(source.axis_from, out int axisIndex))
                    {
                        throw new InvalidOperationException(
                            $"Hand prop '{prop.prefab_name}' anchor " +
                            $"'{source.name}' names an unknown axis part " +
                            $"'{source.axis_from}'.");
                    }

                    Vector3 axisCentre = partTransforms[axisIndex].TransformPoint(
                        partMeshes[axisIndex].bounds.center);
                    worldPosition = FarthestVertex(part, mesh, axisCentre);
                    Vector3 axis = worldPosition - axisCentre;
                    if (axis.sqrMagnitude < 0.000001f)
                    {
                        throw new InvalidOperationException(
                            $"Hand prop '{prop.prefab_name}' anchor " +
                            $"'{source.name}' has no direction from " +
                            $"'{source.axis_from}'.");
                    }

                    worldRotation = Quaternion.LookRotation(axis, Vector3.up);
                    return;
                default:
                    throw new InvalidOperationException(
                        $"Hand prop '{prop.prefab_name}' anchor '{source.name}' " +
                        $"has unknown kind '{source.kind}'.");
            }
        }

        private static Vector3 FarthestVertex(
            Transform part,
            Mesh mesh,
            Vector3 fromWorldPosition)
        {
            Vector3[] vertices = mesh.vertices;
            if (vertices.Length == 0)
            {
                throw new InvalidOperationException(
                    $"Hand prop part '{part.name}' has no vertices.");
            }

            Vector3 farthest = part.TransformPoint(vertices[0]);
            float best = (farthest - fromWorldPosition).sqrMagnitude;
            for (int index = 1; index < vertices.Length; index++)
            {
                Vector3 candidate = part.TransformPoint(vertices[index]);
                float distance = (candidate - fromWorldPosition).sqrMagnitude;
                if (distance > best)
                {
                    best = distance;
                    farthest = candidate;
                }
            }

            return farthest;
        }

        private static GameObject InstantiateReferenceBody(string designId)
        {
            string modelPath = ResolveReferenceModelPath(designId);
            GameObject modelAsset =
                AssetDatabase.LoadAssetAtPath<GameObject>(modelPath);
            if (modelAsset == null)
            {
                throw new InvalidOperationException(
                    $"Reference body FBX for '{designId}' did not import at " +
                    $"'{modelPath}'.");
            }

            GameObject body = Object.Instantiate(modelAsset);
            body.name = modelAsset.name;
            MoveToPreviewScene(body);
            ResetToIdentity(body.transform);
            return body;
        }

        // ------------------------------------------------------------------
        // Preview scene
        // ------------------------------------------------------------------

        private static Scene previewScene;
        private static bool hasPreviewScene;

        /// <summary>
        /// Owns the one preview scene a build or a validation measures in.
        /// The outermost scope creates and closes it; nested scopes (the
        /// validation the build ends with) simply reuse it. Closing the
        /// scene destroys whatever is still in it, which is the point.
        /// </summary>
        private sealed class PreviewSceneScope : IDisposable
        {
            private readonly bool owner;

            public PreviewSceneScope()
            {
                if (!hasPreviewScene || !previewScene.IsValid())
                {
                    previewScene = EditorSceneManager.NewPreviewScene();
                    hasPreviewScene = true;
                    owner = true;
                }
            }

            public void Dispose()
            {
                if (!owner || !hasPreviewScene)
                {
                    return;
                }

                if (previewScene.IsValid())
                {
                    EditorSceneManager.ClosePreviewScene(previewScene);
                }

                hasPreviewScene = false;
            }
        }

        /// <summary>
        /// Every temporary object the pipeline creates goes here rather
        /// than into the open scene. Transforms, bounds and prefab saving
        /// all work the same in a preview scene; what differs is that the
        /// user's scene is not touched and not marked dirty.
        /// </summary>
        private static void MoveToPreviewScene(GameObject instance)
        {
            if (!hasPreviewScene || !previewScene.IsValid())
            {
                throw new InvalidOperationException(
                    "Hand prop pipeline objects must be created inside a " +
                    nameof(PreviewSceneScope) + ".");
            }

            SceneManager.MoveGameObjectToScene(instance, previewScene);
        }

        /// <summary>
        /// Design id to imported body FBX. The pedestrian descriptors
        /// answer for their designs; the two cafe bodies live in the cafe
        /// setup. The answer must be one of the importer's trigger paths,
        /// or a moved reference skeleton would never queue a rebuild.
        /// </summary>
        private static string ResolveReferenceModelPath(string designId)
        {
            string modelPath;
            if (string.Equals(designId, CafeWomanDesignId, StringComparison.Ordinal))
            {
                modelPath = MountainRoadCafeCastAssetSetup.PairWomanModelPath;
            }
            else if (string.Equals(designId, CafeAttendantDesignId, StringComparison.Ordinal))
            {
                modelPath = MountainRoadCafeCastAssetSetup.AttendantModelPath;
            }
            else if (!CityPedestrianAssetSetup.TryGetModelPath(designId, out modelPath))
            {
                throw new InvalidOperationException(
                    $"Hand prop reference design '{designId}' is not a " +
                    "pedestrian or cafe design.");
            }

            if (Array.IndexOf(ReferenceModelPaths, modelPath) < 0)
            {
                throw new InvalidOperationException(
                    $"Hand prop reference design '{designId}' imports from " +
                    $"'{modelPath}', which the hand prop importer does not " +
                    "watch; add it to ReferenceModelPaths.");
            }

            return modelPath;
        }

        private static SocketRestPose MeasureSocketRest(
            string designId,
            string socketName)
        {
            using (new PreviewSceneScope())
            {
                GameObject body = InstantiateReferenceBody(designId);
                try
                {
                    Transform socket = CityPedestrianHandProps.FindSocket(
                        body.transform,
                        socketName);
                    if (socket == null)
                    {
                        throw new InvalidOperationException(
                            $"Reference body '{designId}' has no '{socketName}' socket.");
                    }

                    return new SocketRestPose(socket.position, socket.rotation);
                }
                finally
                {
                    Object.DestroyImmediate(body);
                }
            }
        }

        private static void RequireUniformScale(Transform socket, string label)
        {
            Vector3 scale = socket.lossyScale;
            float largest = Mathf.Max(
                Mathf.Abs(scale.x),
                Mathf.Max(Mathf.Abs(scale.y), Mathf.Abs(scale.z)));
            if (largest <= 0f ||
                Mathf.Abs(scale.x - scale.y) > SocketScaleRelativeTolerance * largest ||
                Mathf.Abs(scale.y - scale.z) > SocketScaleRelativeTolerance * largest ||
                Mathf.Abs(scale.x - scale.z) > SocketScaleRelativeTolerance * largest)
            {
                throw new InvalidOperationException(
                    $"Socket '{socket.name}' for the {label} prop has a " +
                    $"non-uniform world scale {scale:F5}; a TRS Mount cannot " +
                    "invert it.");
            }
        }

        /// <summary>
        /// TRS decomposition that proves itself: the rebuilt matrix must
        /// reproduce the source to 1e-5 of its largest element, so a
        /// mirrored or sheared source throws instead of saving a Mount
        /// that is almost right.
        /// </summary>
        private static void Decompose(
            Matrix4x4 matrix,
            string label,
            out Vector3 position,
            out Quaternion rotation,
            out Vector3 scale)
        {
            position = matrix.GetColumn(3);
            rotation = matrix.rotation;
            scale = matrix.lossyScale;
            RequireMatrixMatch(
                Matrix4x4.TRS(position, rotation, scale),
                matrix,
                DecompositionRelativeTolerance,
                label + " TRS decomposition");
        }

        private static void RequireMatrixMatch(
            Matrix4x4 actual,
            Matrix4x4 expected,
            float relativeTolerance,
            string label)
        {
            float largest = 0f;
            for (int index = 0; index < 16; index++)
            {
                largest = Mathf.Max(largest, Mathf.Abs(expected[index]));
            }

            float tolerance = relativeTolerance * Mathf.Max(largest, 1f);
            for (int index = 0; index < 16; index++)
            {
                if (Mathf.Abs(actual[index] - expected[index]) > tolerance)
                {
                    throw new InvalidOperationException(
                        $"{label} does not round-trip: element {index} is " +
                        $"{actual[index]:0.#######} against " +
                        $"{expected[index]:0.#######}.");
                }
            }
        }

        // ------------------------------------------------------------------
        // Validation
        // ------------------------------------------------------------------

        private static CityPedestrianHandPropRegistry ValidatePrefab(
            HandPropLibraryManifest manifest,
            HandPropManifest prop,
            CityPedestrianHandPropId id,
            Material expectedMaterial)
        {
            string prefabPath = GetPrefabPath(id);
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
            if (prefab == null)
            {
                throw new InvalidOperationException(
                    $"Hand prop prefab is missing at '{prefabPath}'.");
            }

            if (!string.Equals(prefab.name, prop.prefab_name, StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    $"Hand prop prefab '{prefabPath}' is named '{prefab.name}', " +
                    $"not '{prop.prefab_name}'.");
            }

            var registry = prefab.GetComponent<CityPedestrianHandPropRegistry>();
            if (registry == null)
            {
                throw new InvalidOperationException(
                    $"Hand prop prefab '{prefabPath}' has no registry.");
            }

            if (registry.Id != id ||
                !string.Equals(registry.ManifestId, prop.id, StringComparison.Ordinal) ||
                !string.Equals(registry.SocketName, prop.socket, StringComparison.Ordinal) ||
                !string.Equals(
                    registry.ReferenceDesignId,
                    prop.reference_design,
                    StringComparison.Ordinal) ||
                registry.SourceTriangleCount != prop.triangle_count ||
                !string.Equals(
                    registry.SourceGeneratorVersion,
                    manifest.generator_version,
                    StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    $"Hand prop prefab '{prop.prefab_name}' identity differs " +
                    "from its manifest entry.");
            }

            if (!string.Equals(
                    registry.BuildSignature,
                    manifest.build_signature,
                    StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    $"Hand prop prefab '{prop.prefab_name}' was built from " +
                    "another library signature.");
            }

            Transform mount = registry.Mount;
            if (mount == null ||
                mount.parent != prefab.transform ||
                !string.Equals(mount.name, MountName, StringComparison.Ordinal) ||
                Vector3.Distance(mount.localPosition, registry.MountLocalPosition) > 0.000001f ||
                Quaternion.Angle(mount.localRotation, registry.MountLocalRotation) > 0.001f ||
                Vector3.Distance(mount.localScale, registry.MountLocalScale) > 0.000001f)
            {
                throw new InvalidOperationException(
                    $"Hand prop prefab '{prop.prefab_name}' Mount is missing " +
                    "or disagrees with its recorded pose.");
            }

            if (prefab.transform.localPosition != Vector3.zero ||
                prefab.transform.localRotation != Quaternion.identity ||
                prefab.transform.localScale != Vector3.one)
            {
                throw new InvalidOperationException(
                    $"Hand prop prefab '{prop.prefab_name}' root must be identity.");
            }

            Renderer[] allRenderers = prefab.GetComponentsInChildren<Renderer>(true);
            if (allRenderers.Length != prop.mesh_count ||
                registry.Renderers.Count != prop.mesh_count ||
                registry.RendererBindings.Count != prop.mesh_count ||
                prop.parts.Length != prop.mesh_count)
            {
                throw new InvalidOperationException(
                    $"Hand prop prefab '{prop.prefab_name}' renderer count " +
                    "differs from its manifest.");
            }

            var seen = new HashSet<string>(StringComparer.Ordinal);
            int totalTriangles = 0;
            for (int index = 0; index < prop.parts.Length; index++)
            {
                HandPropManifestPart part = prop.parts[index];
                Renderer renderer = registry.FindRenderer(part.name);
                if (!(renderer is MeshRenderer) ||
                    renderer.transform.parent != mount ||
                    !seen.Add(part.name))
                {
                    throw new InvalidOperationException(
                        $"Hand prop prefab '{prop.prefab_name}' part " +
                        $"'{part.name}' is missing, duplicated or not a " +
                        "MeshRenderer under the Mount.");
                }

                MeshFilter filter = renderer.GetComponent<MeshFilter>();
                Mesh mesh = filter != null ? filter.sharedMesh : null;
                if (mesh == null ||
                    !string.Equals(
                        AssetDatabase.GetAssetPath(mesh),
                        ModelPath,
                        StringComparison.Ordinal))
                {
                    throw new InvalidOperationException(
                        $"Hand prop part '{part.name}' does not reference a " +
                        "mesh of the prop library FBX.");
                }

                int triangles = TriangleCount(mesh);
                if (triangles != part.triangles)
                {
                    throw new InvalidOperationException(
                        $"Hand prop part '{part.name}' has {triangles} " +
                        $"triangles; its manifest says {part.triangles}.");
                }

                totalTriangles += triangles;
                if (renderer.sharedMaterials.Length != 1 ||
                    renderer.sharedMaterial != expectedMaterial ||
                    renderer.shadowCastingMode != ShadowCastingMode.On ||
                    !renderer.receiveShadows)
                {
                    throw new InvalidOperationException(
                        $"Hand prop part '{part.name}' must render with the " +
                        "one shared Player3DLit material and cast shadows.");
                }

                CityPedestrianRendererBinding binding = registry.RendererBindings[index];
                if (binding == null ||
                    binding.Renderer != renderer ||
                    !string.Equals(binding.RendererName, part.name, StringComparison.Ordinal) ||
                    !string.Equals(binding.PaletteName, part.palette_name, StringComparison.Ordinal))
                {
                    throw new InvalidOperationException(
                        $"Hand prop part '{part.name}' binding does not match " +
                        "its renderer.");
                }
            }

            if (totalTriangles != prop.triangle_count)
            {
                throw new InvalidOperationException(
                    $"Hand prop prefab '{prop.prefab_name}' has {totalTriangles} " +
                    $"triangles; its manifest says {prop.triangle_count}.");
            }

            if (prefab.GetComponentsInChildren<SkinnedMeshRenderer>(true).Length != 0 ||
                prefab.GetComponentsInChildren<Collider>(true).Length != 0 ||
                prefab.GetComponentsInChildren<Light>(true).Length != 0 ||
                prefab.GetComponentsInChildren<Animator>(true).Length != 0 ||
                prefab.GetComponentsInChildren<Animation>(true).Length != 0 ||
                prefab.GetComponentsInChildren<Rigidbody>(true).Length != 0 ||
                prefab.GetComponentsInChildren<Camera>(true).Length != 0 ||
                prefab.GetComponentsInChildren<AudioSource>(true).Length != 0)
            {
                throw new InvalidOperationException(
                    $"Hand prop prefab '{prop.prefab_name}' must stay passive: " +
                    "MeshRenderers only, no colliders, lights, animators, " +
                    "physics or audio.");
            }

            MonoBehaviour[] behaviours = prefab.GetComponentsInChildren<MonoBehaviour>(true);
            for (int index = 0; index < behaviours.Length; index++)
            {
                if (behaviours[index] != null &&
                    !(behaviours[index] is CityPedestrianHandPropRegistry))
                {
                    throw new InvalidOperationException(
                        $"Hand prop prefab '{prop.prefab_name}' may carry only " +
                        "its registry.");
                }
            }

            HandPropManifestAnchor[] anchorSources =
                prop.anchors ?? Array.Empty<HandPropManifestAnchor>();
            if (registry.Anchors.Count != anchorSources.Length)
            {
                throw new InvalidOperationException(
                    $"Hand prop prefab '{prop.prefab_name}' anchor count differs " +
                    "from its manifest.");
            }

            for (int index = 0; index < anchorSources.Length; index++)
            {
                Transform anchor = registry.FindAnchor(anchorSources[index].name);
                if (anchor == null ||
                    anchor.parent != mount ||
                    !string.Equals(anchor.name, anchorSources[index].name, StringComparison.Ordinal))
                {
                    throw new InvalidOperationException(
                        $"Hand prop prefab '{prop.prefab_name}' anchor " +
                        $"'{anchorSources[index].name}' is missing or not under " +
                        "the Mount.");
                }
            }

            return registry;
        }

        private static HandPropLibraryManifest LoadAndValidateManifest()
        {
            if (!File.Exists(ManifestPath))
            {
                throw new InvalidOperationException(
                    $"Hand prop manifest is missing at '{ManifestPath}'.");
            }

            var manifest = JsonUtility.FromJson<HandPropLibraryManifest>(
                File.ReadAllText(ManifestPath));
            if (manifest == null ||
                !string.Equals(manifest.library, LibraryName, StringComparison.Ordinal) ||
                string.IsNullOrEmpty(manifest.generator_version) ||
                !IsHex64(manifest.build_signature) ||
                !string.Equals(
                    manifest.material_asset,
                    SharedMaterialPath,
                    StringComparison.Ordinal) ||
                manifest.emissive ||
                manifest.colliders ||
                manifest.props == null)
            {
                throw new InvalidOperationException(
                    "Hand prop manifest is not a deterministic, passive " +
                    "library on the shared Player3DLit material.");
            }

            IReadOnlyList<CityPedestrianHandPropId> ids = CityPedestrianHandProps.Ids;
            if (manifest.props.Length != ids.Count)
            {
                throw new InvalidOperationException(
                    $"Hand prop manifest lists {manifest.props.Length} props; " +
                    $"the runtime knows {ids.Count}.");
            }

            int meshTotal = 0;
            int triangleTotal = 0;
            for (int index = 0; index < ids.Count; index++)
            {
                CityPedestrianHandPropId id = ids[index];
                HandPropManifest prop = manifest.props[index];
                if (prop == null ||
                    !string.Equals(
                        prop.id,
                        CityPedestrianHandProps.GetManifestId(id),
                        StringComparison.Ordinal) ||
                    !string.Equals(
                        prop.prefab_name,
                        CityPedestrianHandProps.GetPrefabName(id),
                        StringComparison.Ordinal) ||
                    !string.Equals(
                        prop.socket,
                        CityPedestrianHandProps.GetSocketName(id),
                        StringComparison.Ordinal) ||
                    !string.Equals(
                        prop.root,
                        PropRootPrefix + prop.prefab_name,
                        StringComparison.Ordinal) ||
                    string.IsNullOrEmpty(prop.reference_design) ||
                    prop.parts == null ||
                    prop.parts.Length == 0 ||
                    prop.parts.Length != prop.mesh_count)
                {
                    throw new InvalidOperationException(
                        $"Hand prop manifest entry {index} does not describe " +
                        $"{id} ({CityPedestrianHandProps.GetManifestId(id)} on " +
                        $"{CityPedestrianHandProps.GetSocketName(id)}).");
                }

                int propTriangles = 0;
                for (int part = 0; part < prop.parts.Length; part++)
                {
                    HandPropManifestPart source = prop.parts[part];
                    if (source == null ||
                        string.IsNullOrEmpty(source.name) ||
                        source.base_color == null ||
                        source.base_color.Length != 4 ||
                        source.triangles <= 0)
                    {
                        throw new InvalidOperationException(
                            $"Hand prop '{prop.prefab_name}' manifest part " +
                            $"{part} is incomplete.");
                    }

                    propTriangles += source.triangles;
                }

                if (propTriangles != prop.triangle_count)
                {
                    throw new InvalidOperationException(
                        $"Hand prop '{prop.prefab_name}' manifest parts sum to " +
                        $"{propTriangles} triangles, not {prop.triangle_count}.");
                }

                meshTotal += prop.mesh_count;
                triangleTotal += prop.triangle_count;
            }

            if (meshTotal != manifest.mesh_count ||
                triangleTotal != manifest.triangle_count)
            {
                throw new InvalidOperationException(
                    "Hand prop manifest totals disagree with its props.");
            }

            return manifest;
        }

        private static bool IsHex64(string value)
        {
            if (value == null || value.Length != 64)
            {
                return false;
            }

            for (int index = 0; index < value.Length; index++)
            {
                char c = value[index];
                bool hex = (c >= '0' && c <= '9') ||
                           (c >= 'a' && c <= 'f') ||
                           (c >= 'A' && c <= 'F');
                if (!hex)
                {
                    return false;
                }
            }

            return true;
        }

        private static int TriangleCount(Mesh mesh)
        {
            int triangles = 0;
            for (int subMesh = 0; subMesh < mesh.subMeshCount; subMesh++)
            {
                triangles += (int)(mesh.GetIndexCount(subMesh) / 3);
            }

            return triangles;
        }

        private static Transform RequireChild(
            Transform root,
            string name,
            string label)
        {
            Transform found = CityPedestrianHandProps.FindSocket(root, name);
            if (found == null || found == root)
            {
                throw new InvalidOperationException(
                    $"Imported {label} hierarchy is missing transform '{name}'.");
            }

            return found;
        }

        private static void ResetToIdentity(Transform transform)
        {
            transform.localPosition = Vector3.zero;
            transform.localRotation = Quaternion.identity;
            transform.localScale = Vector3.one;
        }

        private readonly struct SocketRestPose
        {
            public SocketRestPose(Vector3 position, Quaternion rotation)
            {
                Position = position;
                Rotation = rotation;
            }

            public Vector3 Position { get; }
            public Quaternion Rotation { get; }
        }

        [Serializable]
        private sealed class HandPropLibraryManifest
        {
            public string generator;
            public string generator_version;
            public string blender_version;
            public string library;
            public string anatomy_standard;
            public string forward_axis;
            public string anatomical_left_axis;
            public string material_asset;
            public bool emissive;
            public bool colliders;
            public int mesh_count;
            public int triangle_count;
            public string build_signature;
            public HandPropManifest[] props;
        }

        [Serializable]
        private sealed class HandPropManifest
        {
            public string id;
            public string prefab_name;
            public string display_name;
            public string socket;
            public string bone;
            public string reference_design;
            public string root;
            public float[] socket_head_m;
            public int mesh_count;
            public int triangle_count;
            public float[] bounds_min;
            public float[] bounds_max;
            public HandPropManifestPart[] parts;
            public HandPropManifestAnchor[] anchors;
        }

        [Serializable]
        private sealed class HandPropManifestPart
        {
            public string name;
            public string role;
            public string palette_name;
            public float[] base_color;
            public int vertices;
            public int triangles;
        }

        [Serializable]
        private sealed class HandPropManifestAnchor
        {
            public string name;
            public string kind;
            public string part;
            public string axis_from;
        }
    }
}

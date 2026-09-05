using System;
using UnityEditor;

namespace BarPromenade.Editor
{
    /// <summary>
    /// Import settings for the hand-prop library FBX
    /// (`Assets/Pedestrians/Props/CityPedestrianHandProps.fbx`): the same
    /// mesh settings the pedestrian bodies use, minus everything a rig
    /// needs. The file carries nine `PROP_*` Empties with plain mesh
    /// children, no armature and no animation, so it must import with NO
    /// Avatar: an Avatar generated "from this model" would be a
    /// meaningless second skeleton, and copying the Hero's onto a file
    /// without bones logs an import warning on every reimport.
    ///
    /// `preserveHierarchy` matters more here than on a body: the prop
    /// build measures each part's world matrix against its Empty, and
    /// Unity is free to collapse a single-child Empty into its mesh
    /// unless told to keep it.
    /// </summary>
    public sealed class CityPedestrianHandPropModelImporter : AssetPostprocessor
    {
        private void OnPreprocessModel()
        {
            if (!(assetImporter is ModelImporter importer) ||
                !string.Equals(
                    assetPath,
                    CityPedestrianHandPropAssetSetup.ModelPath,
                    StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            importer.animationType = ModelImporterAnimationType.None;
            importer.avatarSetup = ModelImporterAvatarSetup.NoAvatar;
            importer.sourceAvatar = null;
            importer.importAnimation = false;
            importer.globalScale = 1f;
            importer.bakeAxisConversion = true;
            importer.preserveHierarchy = true;
            importer.optimizeGameObjects = false;
            importer.importCameras = false;
            importer.importLights = false;
            importer.addCollider = false;
            importer.importBlendShapes = false;
            importer.importNormals = ModelImporterNormals.Import;
            importer.importTangents = ModelImporterTangents.None;
            importer.meshCompression = ModelImporterMeshCompression.Off;
            // Readable, unlike the body FBXs: the PlayMode contact sweeps
            // measure these rigid parts vertex by vertex (the cafe woman's
            // filter against her lips, the attendant's pot against the
            // counter), and a non-readable mesh leaves them only its local
            // box, which for a thin cigarette tilted in its part frame is
            // centimetres off the tube's ends. Thirty-three meshes of 840
            // triangles in all — the CPU copy costs nothing worth the
            // blindness.
            importer.isReadable = true;
            importer.weldVertices = true;
            importer.keepQuads = false;
            importer.generateSecondaryUV = false;
            importer.materialImportMode =
                ModelImporterMaterialImportMode.None;
        }

        /// <summary>
        /// Queues the prop build when one of its sources lands: the prop
        /// FBX or manifest, a REFERENCE body FBX (a moved socket
        /// invalidates every Mount measured against it) or the shared
        /// material. Silent while any NPC pipeline is building, because
        /// those pipelines force-import the very same body FBXs and the
        /// batch entry already builds the props in order after them.
        /// </summary>
        private static void OnPostprocessAllAssets(
            string[] importedAssets,
            string[] deletedAssets,
            string[] movedAssets,
            string[] movedFromAssetPaths)
        {
            if (NpcHumanV2AssetSetup.IsAnyPipelineBuilding ||
                Player3DV2AssetSetup.IsBuilding)
            {
                return;
            }

            for (int index = 0; index < importedAssets.Length; index++)
            {
                if (CityPedestrianHandPropAssetSetup.IsBuildTriggerPath(
                        importedAssets[index]))
                {
                    CityPedestrianHandPropAssetSetup.QueueBuildWhenSourcesExist();
                    return;
                }
            }
        }
    }
}

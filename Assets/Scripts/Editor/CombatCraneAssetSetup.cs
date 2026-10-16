using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace BarPromenade.Editor
{
    /// <summary>Dedicated Generic crane bank and compact passive suspension geometry.</summary>
    public sealed class CombatCraneAssetSetup : AssetPostprocessor
    {
        public const string Folder = "Assets/Resources/Combat/";
        public const string BankPath = Folder + "CraneActions.fbx";
        public override uint GetVersion() => 1;
        private bool IsBank => assetPath == BankPath;
        private bool IsPassive => assetPath == Folder + "CraneSuspensionHead.fbx" || assetPath == Folder + "CraneCableSegment.fbx" ||
            assetPath == Folder + "CraneRails.fbx" || assetPath == Folder + "CraneBridge.fbx" ||
            assetPath == Folder + "CraneHook.fbx" || assetPath == Folder + "CraneWristSocket.fbx";

        private void OnPreprocessModel()
        {
            if ((!IsBank && !IsPassive) || !(assetImporter is ModelImporter importer)) return;
            importer.globalScale = 1f; importer.useFileScale = true; importer.bakeAxisConversion = true;
            importer.preserveHierarchy = true; importer.optimizeGameObjects = false;
            importer.importCameras = false; importer.importLights = false; importer.addCollider = false;
            importer.importBlendShapes = false; importer.materialImportMode = ModelImporterMaterialImportMode.None;
            importer.importAnimation = IsBank;
            importer.animationType = IsBank ? ModelImporterAnimationType.Generic : ModelImporterAnimationType.None;
            importer.animationCompression = ModelImporterAnimationCompression.Off;
            if (!IsBank) return;
            Avatar avatar = AssetDatabase.LoadAllAssetsAtPath(Player3DV2AssetSetup.ModelPath).OfType<Avatar>().FirstOrDefault();
            importer.avatarSetup = avatar != null ? ModelImporterAvatarSetup.CopyFromOther : ModelImporterAvatarSetup.CreateFromThisModel;
            importer.sourceAvatar = avatar;
        }

        private void OnPreprocessAnimation()
        {
            if (!IsBank || !(assetImporter is ModelImporter importer)) return;
            ModelImporterClipAnimation[] clips = importer.defaultClipAnimations;
            foreach (ModelImporterClipAnimation clip in clips)
            {
                int separator = clip.name.LastIndexOf('|');
                if (separator >= 0) clip.name = clip.name.Substring(separator + 1);
                if (Array.IndexOf(CombatCraneAssetProvider.ClipNames, clip.name) < 0)
                    throw new InvalidOperationException("Unexpected crane action " + clip.name);
                clip.loopTime = CombatCraneAssetProvider.IsLoop(clip.name); clip.loopPose = false;
                clip.keepOriginalOrientation = true; clip.keepOriginalPositionXZ = true; clip.keepOriginalPositionY = true;
                clip.lockRootRotation = true; clip.lockRootPositionXZ = true; clip.lockRootHeightY = true;
                clip.events = Array.Empty<AnimationEvent>();
            }
            importer.clipAnimations = clips;
        }

        [MenuItem("Bar Promenade/Combat Test/Validate Crane Assets")]
        public static void BuildOrThrow()
        {
            foreach (string file in new[] { "CraneActions.fbx", "CraneSuspensionHead.fbx", "CraneCableSegment.fbx", "CraneRails.fbx", "CraneBridge.fbx",
                "CraneHook.fbx", "CraneWristSocket.fbx", "Crane3D.json" })
                AssetDatabase.ImportAsset(Folder + file, ImportAssetOptions.ForceSynchronousImport | ImportAssetOptions.ForceUpdate);
            Manifest manifest = JsonUtility.FromJson<Manifest>(File.ReadAllText(Folder + "Crane3D.json"));
            if (manifest == null || !manifest.test_only || manifest.root_motion || manifest.animation_events != 0 ||
                manifest.rig != "NpcHumanV2" || !manifest.retained_rest_skeleton || manifest.clips == null ||
                manifest.clips.Length != CombatCraneAssetProvider.ClipNames.Length)
                throw new InvalidOperationException("Crane manifest violates its retained suspension contract.");
            foreach (string name in CombatCraneAssetProvider.ClipNames)
                CombatTestAssetSetup.ValidateInPlaceClip(CombatCraneAssetProvider.LoadClip(name));
            GameObject head = CombatCraneAssetProvider.LoadSuspensionHead();
            Transform swivel = CityPedestrianHandProps.FindSocket(head.transform, CombatCraneAssetProvider.HeadSwivelAnchor);
            if (swivel == null) throw new InvalidOperationException("Crane head lacks its lower cable swivel.");
            foreach (string name in CombatCraneAssetProvider.HeadAnchorNames)
            {
                Transform feed = CityPedestrianHandProps.FindSocket(head.transform, name);
                if (feed == null || !feed.IsChildOf(swivel))
                    throw new InvalidOperationException("Crane feed must belong to the lower swivel: " + name);
            }
            CombatCraneAssetProvider.LoadCableSegment();
            if (manifest.models == null || manifest.models.Length != 6)
                throw new InvalidOperationException("Crane manifest lacks its measured passive geometry.");
            foreach (Model model in manifest.models) ValidatePlacedModel(model);
            foreach (string name in new[] { CombatCraneAssetProvider.HookCableAnchor, CombatCraneAssetProvider.HookImpactAnchor })
                if (CityPedestrianHandProps.FindSocket(CombatCraneAssetProvider.LoadHook().transform, name) == null)
                    throw new InvalidOperationException("Crane hook is missing " + name);
            if (CityPedestrianHandProps.FindSocket(CombatCraneAssetProvider.LoadWristSocket().transform, CombatCraneAssetProvider.WristCableAnchor) == null)
                throw new InvalidOperationException("Crane wrist lacks its authored cable exit.");
            Debug.Log("COMBAT CRANE IMPORTED ACTIONS AND SUSPENSION OK");
        }

        private static void ValidatePlacedModel(Model expected)
        {
            GameObject template = AssetDatabase.LoadAssetAtPath<GameObject>(Folder + expected.name + ".fbx");
            if (template == null || expected.bounds_min == null || expected.bounds_max == null)
                throw new InvalidOperationException("Missing measured crane model " + expected.name);
            GameObject placed = UnityEngine.Object.Instantiate(template);
            try
            {
                // Keep the FBX root scale. World renderer bounds detect a lost
                // centimetre factor even when all logical anchors still agree.
                Bounds actual = default; bool found = false;
                foreach (Renderer renderer in placed.GetComponentsInChildren<Renderer>(true))
                {
                    if (!found) { actual = renderer.bounds; found = true; }
                    else actual.Encapsulate(renderer.bounds);
                }
                Vector3 minimum = new Vector3(expected.bounds_min[0], expected.bounds_min[1], expected.bounds_min[2]);
                Vector3 maximum = new Vector3(expected.bounds_max[0], expected.bounds_max[1], expected.bounds_max[2]);
                if (!found || Vector3.Distance(actual.min, minimum) > .015f || Vector3.Distance(actual.max, maximum) > .015f)
                    throw new InvalidOperationException("Crane imported geometry differs from its measured bounds: " + expected.name +
                        "; actual=" + actual + "; expected=" + minimum + ".." + maximum);
                // Fixed hangers join the joists outside the fan aperture.
                // Only the moving bridge/head must remain below its rotor.
                if ((expected.name == "CraneSuspensionHead" || expected.name == "CraneBridge") && actual.max.y > .28f)
                    throw new InvalidOperationException("Crane gantry exceeds its fan clearance: " + expected.name);
                if (expected.name == "CraneRails" && actual.max.y > .73f)
                    throw new InvalidOperationException("Crane fixed hangers exceed the declared joist attachment.");
            }
            finally { UnityEngine.Object.DestroyImmediate(placed); }
        }

        [Serializable] private sealed class Manifest
        {
            public bool test_only, root_motion, retained_rest_skeleton;
            public int animation_events;
            public string rig;
            public Clip[] clips;
            public Model[] models;
        }
        [Serializable] private sealed class Clip { public string name; public float duration_seconds; public bool loop; }
        [Serializable] private sealed class Model { public string name; public float[] bounds_min, bounds_max; }
    }
}

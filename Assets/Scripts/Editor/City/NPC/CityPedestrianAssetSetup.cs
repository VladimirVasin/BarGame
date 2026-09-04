using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using BarPromenade;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace BarPromenade.Editor
{
    [InitializeOnLoad]
    public static class CityPedestrianAssetSetup
    {
        public const string ModelPath =
            "Assets/Pedestrians/Models/CityPedestrian3D.fbx";
        public const string ManifestPath =
            "Assets/Pedestrians/Models/CityPedestrian3D.json";
        public const string ChairCarrierModelPath =
            "Assets/Pedestrians/Models/ChairCarrierPedestrian3D.fbx";
        public const string ChairCarrierManifestPath =
            "Assets/Pedestrians/Models/ChairCarrierPedestrian3D.json";
        public const string KettleHatModelPath =
            "Assets/Pedestrians/Models/KettleHatPedestrian3D.fbx";
        public const string KettleHatManifestPath =
            "Assets/Pedestrians/Models/KettleHatPedestrian3D.json";
        public const string LongArmModelPath =
            "Assets/Pedestrians/Models/LongArmPedestrian3D.fbx";
        public const string LongArmManifestPath =
            "Assets/Pedestrians/Models/LongArmPedestrian3D.json";
        public const string HelmetLampModelPath =
            "Assets/Pedestrians/Models/HelmetLampPedestrian3D.fbx";
        public const string HelmetLampManifestPath =
            "Assets/Pedestrians/Models/HelmetLampPedestrian3D.json";
        public const string PipebackRollerModelPath =
            "Assets/Pedestrians/Staged/Models/PipebackRoller3D.fbx";
        public const string PipebackRollerManifestPath =
            "Assets/Pedestrians/Staged/Models/PipebackRoller3D.json";
        public const string YardBabushkaModelPath =
            "Assets/Pedestrians/Staged/Models/YardBabushka3D.fbx";
        public const string YardBabushkaManifestPath =
            "Assets/Pedestrians/Staged/Models/YardBabushka3D.json";
        public const string WeighAttendantModelPath =
            "Assets/Pedestrians/Staged/Models/WeighbridgeAttendant3D.fbx";
        public const string WeighAttendantManifestPath =
            "Assets/Pedestrians/Staged/Models/WeighbridgeAttendant3D.json";
        public const string CemeteryMournerModelPath =
            "Assets/Pedestrians/Staged/Models/CemeteryMourner3D.fbx";
        public const string CemeteryMournerManifestPath =
            "Assets/Pedestrians/Staged/Models/CemeteryMourner3D.json";
        public const string CemeteryWatchmanModelPath =
            "Assets/Pedestrians/Staged/Models/CemeteryWatchman3D.fbx";
        public const string CemeteryWatchmanManifestPath =
            "Assets/Pedestrians/Staged/Models/CemeteryWatchman3D.json";
        public const string LakeFishermanModelPath =
            "Assets/Pedestrians/Staged/Models/LakeFisherman3D.fbx";
        public const string LakeFishermanManifestPath =
            "Assets/Pedestrians/Staged/Models/LakeFisherman3D.json";
        public const string ParkChessPlayerModelPath =
            "Assets/Pedestrians/Staged/Models/ParkChessPlayer3D.fbx";
        public const string ParkChessPlayerManifestPath =
            "Assets/Pedestrians/Staged/Models/ParkChessPlayer3D.json";
        public const string ParkCheckersPlayerModelPath =
            "Assets/Pedestrians/Staged/Models/ParkCheckersPlayer3D.fbx";
        public const string ParkCheckersPlayerManifestPath =
            "Assets/Pedestrians/Staged/Models/ParkCheckersPlayer3D.json";
        public const string PlayerModelPath =
            "Assets/Player3D/V2/Models/PlayerCharacter3DV2.fbx";
        public const string AnimationPath =
            "Assets/Pedestrians/Animations/CityPedestrianLocomotion.fbx";
        public const string AnimationManifestPath =
            "Assets/Pedestrians/Animations/CityPedestrianLocomotion.json";
        public const string SharedMaterialPath =
            "Assets/Player3D/Materials/Player3DLit.mat";
        public const string PrefabPath =
            "Assets/Resources/Pedestrians/CityPedestrian3D.prefab";
        public const string ChairCarrierPrefabPath =
            "Assets/Resources/Pedestrians/ChairCarrierPedestrian3D.prefab";
        public const string KettleHatPrefabPath =
            "Assets/Resources/Pedestrians/KettleHatPedestrian3D.prefab";
        public const string LongArmPrefabPath =
            "Assets/Resources/Pedestrians/LongArmPedestrian3D.prefab";
        public const string HelmetLampPrefabPath =
            "Assets/Resources/Pedestrians/HelmetLampPedestrian3D.prefab";
        public const string PipebackRollerPrefabPath =
            "Assets/Pedestrians/Staged/Prefabs/PipebackRoller3D.prefab";
        public const string YardBabushkaPrefabPath =
            "Assets/Resources/Pedestrians/YardBabushka3D.prefab";
        public const string YardBabushkaProviderPath =
            "Assets/Resources/City/DryingYardBabushkaProvider.asset";
        public const string WeighAttendantPrefabPath =
            "Assets/Resources/Pedestrians/WeighbridgeAttendant3D.prefab";
        public const string WeighAttendantProviderPath =
            "Assets/Resources/City/WeighbridgeAttendantProvider.asset";
        public const string CemeteryMournerPrefabPath =
            "Assets/Resources/Pedestrians/CemeteryMourner3D.prefab";
        public const string CemeteryMournerProviderPath =
            "Assets/Resources/City/CemeteryMournerProvider.asset";
        public const string CemeteryWatchmanPrefabPath =
            "Assets/Resources/Pedestrians/CemeteryWatchman3D.prefab";
        public const string CemeteryWatchmanProviderPath =
            "Assets/Resources/City/CemeteryWatchmanProvider.asset";
        public const string LakeFishermanPrefabPath =
            "Assets/Pedestrians/Staged/Prefabs/LakeFisherman3D.prefab";
        public const string SeacoastFishermanProviderPath =
            "Assets/Resources/City/SeacoastFishermanProvider.asset";
        public const string ParkChessPlayerPrefabPath =
            "Assets/Resources/Pedestrians/ParkChessPlayer3D.prefab";
        public const string ParkChessPlayerProviderPath =
            "Assets/Resources/City/ParkChessPlayerProvider.asset";
        public const string ParkCheckersPlayerPrefabPath =
            "Assets/Resources/Pedestrians/ParkCheckersPlayer3D.prefab";
        public const string ParkCheckersPlayerProviderPath =
            "Assets/Resources/City/ParkCheckersPlayerProvider.asset";
        public const string LastRouteFerrymanModelPath =
            "Assets/Pedestrians/Staged/Models/LastRouteFerryman3D.fbx";
        public const string LastRouteFerrymanManifestPath =
            "Assets/Pedestrians/Staged/Models/LastRouteFerryman3D.json";
        public const string LastRouteFerrymanPrefabPath =
            "Assets/Pedestrians/Staged/Prefabs/LastRouteFerryman3D.prefab";
        public const string LastRouteFerrymanProviderPath =
            "Assets/Resources/City/LastRouteFerrymanProvider.asset";
        public const string KettleHatDetailAtlasPath =
            "Assets/Pedestrians/Textures/KettleHatDetailAtlas.png";

        /// <summary>
        /// Every pedestrian detail atlas the texture importer must lock to
        /// the flat pixel-art contract. One entry today; the list exists so
        /// the importer never learns a design's name.
        /// </summary>
        public static readonly string[] DetailAtlasPaths =
        {
            KettleHatDetailAtlasPath
        };

        // The detail atlas contract, mirrored from the Hero V2 clothing
        // atlas: the generator paints into 64 px cells of a 256 px square
        // and keeps every UV one pixel inside its cell.
        private const int DetailAtlasWidth = 256;
        private const int DetailAtlasHeight = 256;
        private const int DetailAtlasUvSafeInsetPixels = 1;
        private const string DetailAtlasShaderProperty = "_BaseMap";
        private const string DetailAtlasColorSpace = "sRGB";
        private const string DetailAtlasFilterMode = "Point";
        private const string DetailAtlasWrapMode = "Clamp";
        private const string DetailAtlasCompression = "Uncompressed";
        private const string DetailAtlasUvOrigin = "bottom_left";
        private const string DetailAtlasTintHex = "FFFFFF";
        // The atlas is greys multiplied by the palette tint that the
        // registry's property block already sets, never a material tint.
        private const string DetailAtlasTintSource = "renderer_palette";
        private const int Sha256HexLength = 64;

        // The boiling kettle: the one declared per-archetype effect, its
        // two rig anchors and the head bone both ride.
        private const string BoilingKettleEffectName = "boiling_kettle";
        private const string RigAnchorKindPivot = "pivot";
        private const string RigAnchorKindAnchor = "anchor";
        private const string HeadBoneName = "head";
        // The spout mouth sits a hand's width to half a metre from the
        // head bone on a 1.75 m envelope; an anchor that landed on the
        // kettle body or out past the handle would still look plausible
        // in the inspector. This does not.
        private const float KettleSpoutReachMinimumMetres = 0.35f;
        private const float KettleSpoutReachMaximumMetres = 0.65f;
        // The pivot's rest is checked in world metres: a local tolerance
        // under the 100x FBX root would be a millimetre, coarser than the
        // lid's own tremble.
        private const float KettlePivotRestPositionTolerance = 0.0001f;
        private const float KettlePivotRestAngleTolerance = 0.01f;
        private const float KettleAxisTolerance = 0.001f;
        // The Hero V2 clothing contract's UV slack: a fortieth of a pixel
        // on a 256 px atlas.
        private const float UvTolerance = 0.0001f;

        private const string LeftWheelPivotName = "PIVOT_Wheel.L";
        private const string RightWheelPivotName = "PIVOT_Wheel.R";
        private const string LeftCasterPivotName = "PIVOT_Caster.L";
        private const string RightCasterPivotName = "PIVOT_Caster.R";
        private const string BellowsPivotName = "PIVOT_Bellows";
        private const string PipeBankPivotName = "PIVOT_PipeBank";
        private const string LeftWheelRendererName = "ACC_WheelTyre.L";
        private const string RightWheelRendererName = "ACC_WheelTyre.R";
        private const string SeatRendererName = "ACC_SeatCushion";
        private const string RightHandBoneName = "hand.R";
        private const string LeftHandBoneName = "hand.L";
        private const string PelvisBoneName = "pelvis";

        // The one worn lamp the pedestrian contract allows. It stays
        // shadowless and short-range so a single moving Spot cannot disturb
        // the City's bounded night-fixture budget.
        private const float HeadLampRange = 7.5f;
        private const float HeadLampIntensity = 3.6f;
        private const float HeadLampSpotAngle = 58f;
        private const float HeadLampInnerSpotAngle = 26f;
        private static readonly Vector3 HeadLampLocalPosition =
            new Vector3(0f, 0.166f, -0.234f);

        private const string ExpectedPose = "apose";
        private const float ExpectedHeight = 1.75f;
        private const int ExpectedBoneCount = 31;
        private const int ExpectedAnimationFps = 24;

        /// <summary>
        /// An idle and a walk for every production or staged design, plus one
        /// authored seated loop for each design that declares a Route 01 ride,
        /// one authored beat for each design that declares an action, one
        /// more for the single design that also has to get off a car, and a
        /// second idle/walk pair for each promoted resident that also roams.
        /// </summary>
        private static int ExpectedLocomotionClipCount
        {
            get
            {
                int count = Descriptors.Length * 2;
                for (int index = 0; index < Descriptors.Length; index++)
                {
                    if (Descriptors[index].RidesBus)
                    {
                        count++;
                    }

                    if (Descriptors[index].HasAction)
                    {
                        count++;
                    }

                    if (Descriptors[index].HasDismount)
                    {
                        count++;
                    }

                    if (Descriptors[index].HasAmbientGait)
                    {
                        count += 2;
                    }
                }

                return count;
            }
        }
        private const float TransformPositionTolerance = 0.0001f;
        private const float TransformAngleTolerance = 0.02f;

        private static readonly PedestrianDescriptor[] Descriptors =
        {
            new PedestrianDescriptor(
                "Lampshade Walker",
                "CityPedestrian3D",
                "lampshade_walker_v1",
                ModelPath,
                ManifestPath,
                PrefabPath,
                "LampshadeIdle",
                "LampshadeWalk",
                2f,
                1.25f,
                800,
                1400,
                sitClipName: "LampshadeSit",
                sitDuration: 3f),
            new PedestrianDescriptor(
                "Chair Carrier",
                "ChairCarrierPedestrian3D",
                "chair_carrier_v1",
                ChairCarrierModelPath,
                ChairCarrierManifestPath,
                ChairCarrierPrefabPath,
                "ChairCarrierIdle",
                "ChairCarrierWalk",
                1.5f,
                1f,
                800,
                1600,
                sitClipName: "ChairCarrierSit",
                sitDuration: 2.5f),
            new PedestrianDescriptor(
                "Kettle Hat Walker",
                "KettleHatPedestrian3D",
                "kettle_hat_walker_v1",
                KettleHatModelPath,
                KettleHatManifestPath,
                KettleHatPrefabPath,
                "KettleHatIdle",
                "KettleHatWalk",
                1.75f,
                0.75f,
                1600,
                2300,
                sitClipName: "KettleHatSit",
                sitDuration: 2.75f,
                carriesBoilingKettle: true,
                detailAtlasPath: KettleHatDetailAtlasPath),
            new PedestrianDescriptor(
                "Long-Arm Walker",
                "LongArmPedestrian3D",
                "long_arm_walker_v1",
                LongArmModelPath,
                LongArmManifestPath,
                LongArmPrefabPath,
                "LongArmIdle",
                "LongArmWalk",
                2.5f,
                1.5f,
                800,
                1300,
                sitClipName: "LongArmSit",
                sitDuration: 3.25f),
            new PedestrianDescriptor(
                "Helmet Lamp Hopper",
                "HelmetLampPedestrian3D",
                "helmet_lamp_hopper_v1",
                HelmetLampModelPath,
                HelmetLampManifestPath,
                HelmetLampPrefabPath,
                "HelmetLampIdle",
                "HelmetLampHop",
                2f,
                1f,
                800,
                1700,
                carriesHeadLamp: true,
                preservesAirborneMotion: true),
            // Authored and imported with the shared animation library, but
            // deliberately saved outside Resources and absent from the
            // runtime catalog until wheelchair-aware navigation exists.
            new PedestrianDescriptor(
                "Pipeback Roller",
                "PipebackRoller3D",
                "pipeback_roller_v1",
                PipebackRollerModelPath,
                PipebackRollerManifestPath,
                PipebackRollerPrefabPath,
                "PipebackIdle",
                "PipebackRoll",
                3f,
                2f,
                1400,
                2400,
                isStaged: true,
                isWheelchair: true),
            // The drying-yard babushka: the idle slot carries the
            // smoking loop and the walk slot the carpet-beating loop.
            // Staged like the rider — authored with the shared art
            // library, outside Resources and the runtime catalog.
            new PedestrianDescriptor(
                "Yard Babushka",
                "YardBabushka3D",
                "yard_babushka_v1",
                YardBabushkaModelPath,
                YardBabushkaManifestPath,
                YardBabushkaPrefabPath,
                "BabushkaSmoke",
                "BabushkaBeat",
                4f,
                1.5f,
                900,
                2000,
                isStaged: true,
                ambientIdleClipName: "BabushkaStreetIdle",
                ambientWalkClipName: "BabushkaStreetWalk",
                ambientIdleDuration: 2f,
                ambientWalkDuration: 1f),
            // The cold-weighbridge attendant: the idle slot carries
            // the weigher's check loop and the walk slot the weighed
            // worker's deck pace. Staged like the babushka — authored
            // with the shared art library, outside Resources and the
            // runtime catalog.
            new PedestrianDescriptor(
                "Weigh Attendant",
                "WeighbridgeAttendant3D",
                "weigh_attendant_v1",
                WeighAttendantModelPath,
                WeighAttendantManifestPath,
                WeighAttendantPrefabPath,
                "WeigherCheck",
                "WeighedPace",
                6f,
                12f,
                900,
                2000,
                isStaged: true,
                sitClipName: "WeigherSit",
                sitDuration: 2.75f,
                ambientIdleClipName: "WeigherStreetIdle",
                ambientWalkClipName: "WeigherStreetWalk",
                ambientIdleDuration: 2f,
                ambientWalkDuration: 1f),
            // The cemetery mourner: the idle slot carries the whole
            // graveside rite (lay, thirty seconds of sobbing, wipe)
            // and the walk slot the grieving gait. Staged like the
            // babushka — authored with the shared art library,
            // outside Resources and the runtime catalog; spawned per
            // visit by the mourner controller, never staged forever.
            new PedestrianDescriptor(
                "Cemetery Mourner",
                "CemeteryMourner3D",
                "cemetery_mourner_v1",
                CemeteryMournerModelPath,
                CemeteryMournerManifestPath,
                CemeteryMournerPrefabPath,
                "MournerMourn",
                "MournerWalk",
                36.5f,
                1.5f,
                900,
                2000,
                isStaged: true,
                ambientIdleClipName: "MournerStreetIdle",
                ambientWalkClipName: "MournerStreetWalk",
                ambientIdleDuration: 2f,
                ambientWalkDuration: 1f),
            // The cemetery watchman: the idle slot carries the snide
            // watch loop and the walk slot the hands-behind-back
            // shuffle reserved for a later patrol pass. Staged like
            // the mourner — authored with the shared art library,
            // outside Resources and the runtime catalog.
            new PedestrianDescriptor(
                "Cemetery Watchman",
                "CemeteryWatchman3D",
                "cemetery_watchman_v1",
                CemeteryWatchmanModelPath,
                CemeteryWatchmanManifestPath,
                CemeteryWatchmanPrefabPath,
                "WatchmanWatch",
                "WatchmanShuffle",
                6f,
                1.5f,
                900,
                2000,
                isStaged: true,
                sitClipName: "WatchmanSit",
                sitDuration: 2.9f,
                ambientIdleClipName: "WatchmanStreetIdle",
                ambientWalkClipName: "WatchmanStreetWalk",
                ambientIdleDuration: 2f,
                ambientWalkDuration: 1f),
            // The lake fisherman: the idle slot carries the leaning
            // fishing loop and the walk slot an oilskin trudge kept
            // for a later pass. Staged like the watchman — authored
            // with the shared art library, outside Resources and the
            // runtime catalog. He is the only design that declares a
            // fishing rig, which is what adds the two bind-pose
            // anchors his pipe and his line hang from.
            new PedestrianDescriptor(
                "Lake Fisherman",
                "LakeFisherman3D",
                "lake_fisherman_v1",
                LakeFishermanModelPath,
                LakeFishermanManifestPath,
                LakeFishermanPrefabPath,
                "FishermanLean",
                "FishermanTrudge",
                8f,
                1.5f,
                900,
                2000,
                isStaged: true,
                ambientIdleClipName: "FishermanStreetIdle",
                ambientWalkClipName: "FishermanStreetWalk",
                ambientIdleDuration: 2f,
                ambientWalkDuration: 1f,
                carriesFishingRig: true),
            // The park chess player: the idle slot carries the brooding
            // loop and the walk slot a park trudge kept for a later
            // pass. Staged like the fisherman — authored with the shared
            // art library, outside Resources and the runtime catalog.
            // He is the only design whose idle is seated on world
            // furniture, so his loop is proved against the drawn bench
            // rather than against the pavement.
            new PedestrianDescriptor(
                "Park Chess Player",
                "ParkChessPlayer3D",
                "park_chess_player_v1",
                ParkChessPlayerModelPath,
                ParkChessPlayerManifestPath,
                ParkChessPlayerPrefabPath,
                "ChessBrood",
                "ChessTrudge",
                12f,
                1.5f,
                900,
                2100,
                isStaged: true,
                ambientIdleClipName: "ChessStreetIdle",
                ambientWalkClipName: "ChessStreetWalk",
                ambientIdleDuration: 2f,
                ambientWalkDuration: 1f,
                actionClipName: "ChessJeer",
                actionDuration: 2f),
            // The park checkers player: the second man at the same set,
            // on the seat opposite-and-across from the chess player's.
            // Staged the same way, and the second design whose idle is
            // seated on world furniture, so his loop is proved against
            // the same drawn bench rather than against the pavement.
            // The clip durations and the triangle band below must stay
            // equal to the ArchetypeSpec in the art generator;
            // ValidateDescriptor fails on a drift.
            new PedestrianDescriptor(
                "Park Checkers Player",
                "ParkCheckersPlayer3D",
                "park_checkers_player_v1",
                ParkCheckersPlayerModelPath,
                ParkCheckersPlayerManifestPath,
                ParkCheckersPlayerPrefabPath,
                "CheckersMull",
                "CheckersTrudge",
                12f,
                1.5f,
                900,
                2200,
                isStaged: true,
                ambientIdleClipName: "CheckersStreetIdle",
                ambientWalkClipName: "CheckersStreetWalk",
                ambientIdleDuration: 2f,
                ambientWalkDuration: 1f,
                actionClipName: "CheckersJeer",
                actionDuration: 2f),
            // The Ferryman. The library's first design with TWO seats:
            // he waits perched on the bonnet of his own car and he
            // drives it away, so unlike every other staged man his
            // seated loop is not a Route 01 ride. `SitClipName` means
            // 'this design has an authored seated loop', which is
            // exactly true of him; the bus never sees him, because he
            // is staged and stays out of the runtime catalog.
            //
            // His action clip is the transition between those two
            // seats rather than a beat on top of an idle, which is why
            // it is one-shot. It is no longer three quarters of a
            // second: it now starts standing at his own door, pulls the
            // handle, gets in and shuts the door behind him, and the
            // drop off the bonnet that used to open it has become a
            // clip of its own with a walk in between.
            new PedestrianDescriptor(
                "Last Route Ferryman",
                "LastRouteFerryman3D",
                "last_route_ferryman_v1",
                LastRouteFerrymanModelPath,
                LastRouteFerrymanManifestPath,
                LastRouteFerrymanPrefabPath,
                "FerrymanWait",
                "FerrymanTrudge",
                4f,
                1.5f,
                900,
                2200,
                isStaged: true,
                sitClipName: "FerrymanDrive",
                sitDuration: 3f,
                actionClipName: "FerrymanBoard",
                actionDuration: 2.5f,
                carriesCoinRig: true,
                dismountClipName: "FerrymanDismount",
                dismountDuration: 1f)
        };

        private static bool isBuilding;
        private static bool buildQueued;

        public static bool IsBuilding => isBuilding;

        static CityPedestrianAssetSetup()
        {
            if (!Application.isBatchMode)
            {
                EditorApplication.delayCall += ValidateDependencyStamp;
            }
        }

        [MenuItem("Bar Promenade/City Pedestrian 3D/Build Runtime Prefab")]
        public static void Run()
        {
            BuildOrThrow();
            Debug.Log(
                "City pedestrian production and staged prefabs rebuilt.");
        }

        [MenuItem("Bar Promenade/City Pedestrian 3D/Validate Imported Contract")]
        public static void RunValidation()
        {
            ValidateOrThrow();
            Debug.Log(
                "City pedestrian production/staged models, custom clips " +
                "and prefab contracts are valid.");
        }

        [MenuItem(
            "Bar Promenade/City Pedestrian 3D/Rebuild Staged Pipeback Roller")]
        public static void RunPipebackRoller()
        {
            BuildPipebackRollerOrThrow();
            Debug.Log("Staged Pipeback Roller prefab rebuilt.");
        }

        [MenuItem(
            "Bar Promenade/City Pedestrian 3D/Rebuild Staged Yard Babushka")]
        public static void RunYardBabushka()
        {
            BuildYardBabushkaOrThrow();
            Debug.Log("Staged Yard Babushka prefab rebuilt and bound.");
        }

        [MenuItem(
            "Bar Promenade/City Pedestrian 3D/Rebuild Staged Weigh Attendant")]
        public static void RunWeighAttendant()
        {
            BuildWeighAttendantOrThrow();
            Debug.Log("Staged Weigh Attendant prefab rebuilt and bound.");
        }

        [MenuItem(
            "Bar Promenade/City Pedestrian 3D/Rebuild Staged Cemetery Mourner")]
        public static void RunCemeteryMourner()
        {
            BuildCemeteryMournerOrThrow();
            Debug.Log("Staged Cemetery Mourner prefab rebuilt and bound.");
        }

        [MenuItem(
            "Bar Promenade/City Pedestrian 3D/Rebuild Staged Cemetery Watchman")]
        public static void RunCemeteryWatchman()
        {
            BuildCemeteryWatchmanOrThrow();
            Debug.Log("Staged Cemetery Watchman prefab rebuilt and bound.");
        }

        [MenuItem(
            "Bar Promenade/City Pedestrian 3D/Rebuild Staged Lake Fisherman")]
        public static void RunLakeFisherman()
        {
            BuildLakeFishermanOrThrow();
            Debug.Log("Staged Lake Fisherman prefab rebuilt and bound.");
        }

        [MenuItem(
            "Bar Promenade/City Pedestrian 3D/Rebuild Staged Park Chess Player")]
        public static void RunParkChessPlayer()
        {
            BuildParkChessPlayerOrThrow();
            Debug.Log("Staged Park Chess Player prefab rebuilt and bound.");
        }

        [MenuItem(
            "Bar Promenade/City Pedestrian 3D/Rebuild Staged Park Checkers Player")]
        public static void RunParkCheckersPlayer()
        {
            BuildParkCheckersPlayerOrThrow();
            Debug.Log("Staged Park Checkers Player prefab rebuilt and bound.");
        }

        [MenuItem(
            "Bar Promenade/City Pedestrian 3D/Rebuild Staged Last Route Ferryman")]
        public static void RunLastRouteFerryman()
        {
            BuildLastRouteFerrymanOrThrow();
            Debug.Log("Staged Last Route Ferryman prefab rebuilt and bound.");
        }

        /// <summary>
        /// True for a model path this pipeline owns. The importer asks
        /// rather than keeping its own list: a design added to
        /// <see cref="Descriptors"/> and forgotten here imports on
        /// default settings, which silently produces its own Avatar and
        /// then fails much later with a rest-transform mismatch.
        /// </summary>
        public static bool IsDeclaredModelPath(string path)
        {
            if (string.IsNullOrEmpty(path))
            {
                return false;
            }

            for (int index = 0; index < Descriptors.Length; index++)
            {
                if (string.Equals(
                        path,
                        Descriptors[index].ModelPath,
                        StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// True for clips whose archetype deliberately leaves the pavement.
        /// Their vertical travel is authored on the pelvis, which this rig's
        /// Avatar treats as the motion node, so locking root height would
        /// silently strip the hop during import.
        /// </summary>
        /// <summary>
        /// A clip that plays once and must not be imported as a loop: the
        /// Ferryman's board transition, which starts on a car bonnet and
        /// ends behind its wheel.
        ///
        /// Two import settings depend on this and one of them matters a
        /// great deal. `loopTime` is merely honest. `loopPose`, though,
        /// normalises a clip's ends towards each other - which is exactly
        /// right for a loop and exactly wrong here, because it would drag
        /// the last frame back towards the bonnet and the runtime crosses
        /// out of that frame into the driving loop.
        ///
        /// Declared as the one-shot ACTION of a design that also declares
        /// a seated loop, which is the shape of a transition between two
        /// postures rather than a beat on top of an idle - and, since he
        /// grew a walk in the middle of getting into his own car, as that
        /// design's DISMOUNT as well. The dismount does not fit the action
        /// shape and never will: it is a second transition on one design,
        /// so it is named rather than inferred, and it needs the loop flags
        /// off for exactly the same reason. Loop-pose on that clip would
        /// drag his landing back towards the bonnet he just left.
        /// </summary>
        public static bool IsOneShotClip(string normalizedClipName)
        {
            if (string.IsNullOrEmpty(normalizedClipName))
            {
                return false;
            }

            for (int index = 0; index < Descriptors.Length; index++)
            {
                PedestrianDescriptor descriptor = Descriptors[index];
                if (descriptor.IsStaged &&
                    descriptor.HasDismount &&
                    string.Equals(
                        normalizedClipName,
                        descriptor.DismountClipName,
                        StringComparison.Ordinal))
                {
                    return true;
                }

                if (!descriptor.HasAction ||
                    !descriptor.RidesBus ||
                    !descriptor.IsStaged)
                {
                    continue;
                }

                if (string.Equals(
                        normalizedClipName,
                        descriptor.ActionClipName,
                        StringComparison.Ordinal))
                {
                    return true;
                }
            }

            return false;
        }

        public static bool IsAirborneClip(string normalizedClipName)
        {
            if (string.IsNullOrEmpty(normalizedClipName))
            {
                return false;
            }

            for (int index = 0; index < Descriptors.Length; index++)
            {
                PedestrianDescriptor descriptor = Descriptors[index];
                if (!descriptor.PreservesAirborneMotion)
                {
                    continue;
                }

                if (string.Equals(
                        normalizedClipName,
                        descriptor.IdleClipName,
                        StringComparison.Ordinal) ||
                    string.Equals(
                        normalizedClipName,
                        descriptor.WalkClipName,
                        StringComparison.Ordinal))
                {
                    return true;
                }
            }

            return false;
        }

        private static void ValidateDeclaredHeadLamp(
            GameObject prefab,
            CityPedestrianAssetRegistry registry,
            PedestrianDescriptor descriptor)
        {
            // Pedestrian presentations stay light-free unless the descriptor
            // declares exactly one worn lamp. The count is checked rather than
            // merely forbidden, so an accidental extra Light still fails.
            Light[] lights = prefab.GetComponentsInChildren<Light>(true);
            if (lights.Length != descriptor.ExpectedLightCount)
            {
                throw new InvalidOperationException(
                    $"'{descriptor.DesignId}' declares " +
                    $"{descriptor.ExpectedLightCount} worn light(s) but the " +
                    $"prefab carries {lights.Length}.");
            }

            if (registry.PreservesAirborneMotion !=
                descriptor.PreservesAirborneMotion)
            {
                throw new InvalidOperationException(
                    $"'{descriptor.DesignId}' airborne-motion flag does not " +
                    "match its descriptor.");
            }

            if (!descriptor.CarriesHeadLamp)
            {
                if (registry.HeadLamp != null)
                {
                    throw new InvalidOperationException(
                        $"'{descriptor.DesignId}' must not register a head " +
                        "lamp.");
                }

                return;
            }

            Light lamp = registry.HeadLamp;
            if (lamp == null || lamp != lights[0])
            {
                throw new InvalidOperationException(
                    $"'{descriptor.DesignId}' does not register its worn " +
                    "lamp on the asset registry.");
            }

            if (lamp.type != LightType.Spot ||
                lamp.shadows != LightShadows.None ||
                lamp.range > HeadLampRange + 0.001f ||
                lamp.intensity > HeadLampIntensity + 0.001f)
            {
                throw new InvalidOperationException(
                    $"'{descriptor.DesignId}' head lamp must stay a " +
                    "shadowless Spot within its bounded range and intensity.");
            }

            if (lamp.transform.parent != registry.HeadAnchor)
            {
                throw new InvalidOperationException(
                    $"'{descriptor.DesignId}' head lamp must hang off the " +
                    "animated head bone.");
            }
        }

        private static void ValidateDescriptorScope(
            PedestrianDescriptor descriptor)
        {
            if (!descriptor.IsStaged)
            {
                return;
            }

            // The model and its manifest are staged art either way: they are
            // authored by the shared library and only ever read through this
            // tool, so they stay out of the shipped Resources bundle.
            if (!descriptor.ModelPath.StartsWith(
                    "Assets/Pedestrians/Staged/",
                    StringComparison.OrdinalIgnoreCase) ||
                !descriptor.ManifestPath.StartsWith(
                    "Assets/Pedestrians/Staged/",
                    StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException(
                    $"Staged pedestrian '{descriptor.DesignId}' must keep " +
                    "its model and manifest under Assets/Pedestrians/Staged.");
            }

            // The prefab is where the two kinds part company.
            //
            // A staged design used to be defined by ABSENCE from the roaming
            // catalog, and this gate enforced that absence. Since 2026-09-02
            // seven ordinary residents are BOTH: the babushka still beats her
            // carpet in the drying yard where the yard planner places her by
            // reference, and she also walks the pavement where the pool loads
            // her by `Resources.Load`. One prefab serves both - the same
            // asset, read through two different clip pairs - so it has to
            // live where `Resources.Load` can find it.
            //
            // The rule that replaces the old one: a design's prefab must sit
            // on the side of the Resources boundary that matches whether the
            // runtime catalog knows its design id. Being in the catalog while
            // staying outside Resources is the failure that matters - the
            // pool would resolve an archetype and then load nothing.
            bool roams = CityPedestrianResources.Roams(descriptor.DesignId);
            bool prefabInResources = descriptor.PrefabPath.StartsWith(
                "Assets/Resources/",
                StringComparison.OrdinalIgnoreCase);
            if (roams != prefabInResources)
            {
                throw new InvalidOperationException(
                    $"Staged pedestrian '{descriptor.DesignId}' " +
                    (roams
                        ? "is in the roaming catalog, so its prefab must " +
                          "live under Assets/Resources where the pool can " +
                          "load it."
                        : "is absent from the roaming catalog, so its " +
                          "prefab must stay outside Resources."));
            }

            if (!roams &&
                !descriptor.PrefabPath.StartsWith(
                    "Assets/Pedestrians/Staged/",
                    StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException(
                    $"Staged pedestrian '{descriptor.DesignId}' must keep " +
                    "its prefab under Assets/Pedestrians/Staged.");
            }
        }

        private static void ValidateWheelchairBindings(
            GameObject prefab,
            CityPedestrianAssetRegistry pedestrianRegistry,
            PedestrianDescriptor descriptor)
        {
            CityWheelchairNpcAssetRegistry[] wheelchairRegistries =
                prefab.GetComponentsInChildren<
                    CityWheelchairNpcAssetRegistry>(true);
            int expectedCount = descriptor.IsWheelchair ? 1 : 0;
            if (wheelchairRegistries.Length != expectedCount)
            {
                throw new InvalidOperationException(
                    $"'{descriptor.DesignId}' declares {expectedCount} " +
                    "wheelchair registry component(s), but its prefab " +
                    $"contains {wheelchairRegistries.Length}.");
            }

            if (!descriptor.IsWheelchair)
            {
                return;
            }

            CityWheelchairNpcAssetRegistry wheelchairRegistry =
                wheelchairRegistries[0];
            if (wheelchairRegistry.gameObject != prefab ||
                wheelchairRegistry.PedestrianRegistry != pedestrianRegistry)
            {
                throw new InvalidOperationException(
                    "The wheelchair registry must live on the prefab root " +
                    "and bind its shared pedestrian registry.");
            }

            Dictionary<string, Transform> transforms =
                IndexUniqueTransforms(prefab, "wheelchair prefab");
            Transform[] expectedPivots =
            {
                RequireTransform(
                    transforms,
                    LeftWheelPivotName,
                    "wheelchair prefab"),
                RequireTransform(
                    transforms,
                    RightWheelPivotName,
                    "wheelchair prefab"),
                RequireTransform(
                    transforms,
                    LeftCasterPivotName,
                    "wheelchair prefab"),
                RequireTransform(
                    transforms,
                    RightCasterPivotName,
                    "wheelchair prefab"),
                RequireTransform(
                    transforms,
                    BellowsPivotName,
                    "wheelchair prefab"),
                RequireTransform(
                    transforms,
                    PipeBankPivotName,
                    "wheelchair prefab")
            };
            Transform[] registeredPivots =
            {
                wheelchairRegistry.LeftWheelPivot,
                wheelchairRegistry.RightWheelPivot,
                wheelchairRegistry.LeftCasterPivot,
                wheelchairRegistry.RightCasterPivot,
                wheelchairRegistry.BellowsPivot,
                wheelchairRegistry.PipeBankPivot
            };
            for (int index = 0; index < expectedPivots.Length; index++)
            {
                if (registeredPivots[index] != expectedPivots[index] ||
                    !registeredPivots[index].IsChildOf(
                        pedestrianRegistry.ModelRoot))
                {
                    throw new InvalidOperationException(
                        "The wheelchair registry has a missing, stale or " +
                        "out-of-model pivot binding.");
                }
            }

            if (registeredPivots.Distinct().Count() !=
                registeredPivots.Length)
            {
                throw new InvalidOperationException(
                    "Every wheelchair mechanism requires its own named " +
                    "pivot transform.");
            }
        }

        private static void ValidateWheelchairVisualScale(
            GameObject prefab,
            float wheelRadius)
        {
            Dictionary<string, Renderer> renderers =
                IndexUniqueRenderers(prefab);
            Renderer leftWheel = RequireRenderer(
                renderers,
                LeftWheelRendererName);
            Renderer rightWheel = RequireRenderer(
                renderers,
                RightWheelRendererName);
            Renderer seat = RequireRenderer(
                renderers,
                SeatRendererName);

            Bounds leftBounds = CalculateLocalBounds(
                prefab.transform,
                new[] { leftWheel });
            Bounds rightBounds = CalculateLocalBounds(
                prefab.transform,
                new[] { rightWheel });
            Bounds seatBounds = CalculateLocalBounds(
                prefab.transform,
                new[] { seat });
            float diameter = wheelRadius * 2f;
            bool wheelsMatch =
                WheelMatches(leftBounds, diameter) &&
                WheelMatches(rightBounds, diameter);
            bool seatMatches =
                seatBounds.size.x >= 0.40f &&
                seatBounds.size.x <= 0.48f &&
                seatBounds.size.y >= 0.05f &&
                seatBounds.size.y <= 0.09f &&
                seatBounds.size.z >= 0.38f &&
                seatBounds.size.z <= 0.48f;
            if (!wheelsMatch || !seatMatches)
            {
                throw new InvalidOperationException(
                    "The staged wheelchair visual geometry has the wrong " +
                    "import scale: " +
                    $"left wheel={leftBounds.size}, " +
                    $"right wheel={rightBounds.size}, " +
                    $"seat={seatBounds.size}; expected {diameter:F2} m " +
                    "drive wheels and a roughly 0.44 x 0.43 m seat.");
            }
        }

        private static bool WheelMatches(Bounds bounds, float diameter)
        {
            return bounds.size.x >= 0.04f &&
                   bounds.size.x <= 0.075f &&
                   Mathf.Abs(bounds.size.y - diameter) <= 0.05f &&
                   Mathf.Abs(bounds.size.z - diameter) <= 0.05f &&
                   Mathf.Abs(bounds.min.y) <= 0.025f;
        }

        private static Renderer RequireRenderer(
            IReadOnlyDictionary<string, Renderer> renderers,
            string name)
        {
            if (renderers.TryGetValue(name, out Renderer renderer) &&
                renderer != null)
            {
                return renderer;
            }

            throw new InvalidOperationException(
                $"Imported wheelchair hierarchy is missing renderer " +
                $"'{name}'.");
        }

        private static bool HasExpectedWheelchairVisualScale(
            GameObject prefab,
            float wheelRadius)
        {
            try
            {
                ValidateWheelchairVisualScale(prefab, wheelRadius);
                return true;
            }
            catch (InvalidOperationException)
            {
                return false;
            }
        }

        private static Light CreateHeadLamp(Transform head)
        {
            // Parented to the head bone so the beam follows the real animated
            // skull rather than a static socket. It is deliberately always on:
            // a miner's lamp is lit because its owner switched it on, not
            // because the city clock reached dusk.
            GameObject lampObject = new GameObject("Head Lamp");
            lampObject.transform.SetParent(head, false);
            lampObject.transform.localPosition = HeadLampLocalPosition;
            lampObject.transform.localRotation =
                Quaternion.LookRotation(Vector3.back, Vector3.up);
            lampObject.transform.localScale = Vector3.one;

            Light lamp = lampObject.AddComponent<Light>();
            lamp.type = LightType.Spot;
            lamp.shadows = LightShadows.None;
            lamp.range = HeadLampRange;
            lamp.intensity = HeadLampIntensity;
            lamp.spotAngle = HeadLampSpotAngle;
            lamp.innerSpotAngle = HeadLampInnerSpotAngle;
            lamp.color = new Color(1f, 0.925f, 0.78f);
            lamp.cullingMask = CityPedestrianCollision.NonPedestrianMask;
            lamp.lightmapBakeType = LightmapBakeType.Realtime;
            return lamp;
        }

        public static bool SourcesExist()
        {
            if (!File.Exists(PlayerModelPath) ||
                !File.Exists(AnimationPath) ||
                !File.Exists(AnimationManifestPath) ||
                !File.Exists(SharedMaterialPath))
            {
                return false;
            }

            for (int index = 0; index < Descriptors.Length; index++)
            {
                if (!File.Exists(Descriptors[index].ModelPath) ||
                    !File.Exists(Descriptors[index].ManifestPath))
                {
                    return false;
                }

                if (Descriptors[index].HasDetailAtlas &&
                    !File.Exists(Descriptors[index].DetailAtlasPath))
                {
                    return false;
                }
            }

            return true;
        }

        /// <summary>
        /// Whether an asset path is one of the pedestrian detail atlases
        /// the texture importer has to lock down.
        /// </summary>
        public static bool IsDetailAtlasPath(string assetPath)
        {
            for (int index = 0; index < DetailAtlasPaths.Length; index++)
            {
                if (string.Equals(
                        assetPath,
                        DetailAtlasPaths[index],
                        StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            return false;
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

        private static void QueuePipebackRollerBuildWhenSourcesExist()
        {
            if (isBuilding || buildQueued || !SourcesExist())
            {
                return;
            }

            buildQueued = true;
            EditorApplication.delayCall += RunQueuedPipebackRollerBuild;
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
                    "City pedestrian build requires every production/staged " +
                    "model FBX/manifest pair, the custom locomotion " +
                    "FBX/manifest, production Hero/NPC V2 model and shared " +
                    "Player3DLit material.");
            }

            isBuilding = true;
            try
            {
                for (int index = 0; index < Descriptors.Length; index++)
                {
                    EnsureFolderForAsset(Descriptors[index].PrefabPath);
                }

                // Import the Avatar dependency first so a clean Library and
                // later Hero/NPC V2 rig changes rebuild every source against
                // the canonical production Generic Avatar.
                AssetDatabase.ImportAsset(
                    PlayerModelPath,
                    ImportAssetOptions.ForceUpdate |
                    ImportAssetOptions.ForceSynchronousImport);
                for (int index = 0; index < Descriptors.Length; index++)
                {
                    AssetDatabase.ImportAsset(
                        Descriptors[index].ModelPath,
                        ImportAssetOptions.ForceUpdate |
                        ImportAssetOptions.ForceSynchronousImport);
                    AssetDatabase.ImportAsset(
                        Descriptors[index].ManifestPath,
                        ImportAssetOptions.ForceUpdate |
                        ImportAssetOptions.ForceSynchronousImport);
                }

                // The generator writes each atlas into a folder the asset
                // database may never have seen. Importing a path the
                // database does not know is a silent no-op that leaves
                // LoadAssetAtPath returning null, so the folder is
                // discovered first.
                if (Descriptors.Any(candidate => candidate.HasDetailAtlas))
                {
                    AssetDatabase.Refresh();
                }

                for (int index = 0; index < Descriptors.Length; index++)
                {
                    if (!Descriptors[index].HasDetailAtlas)
                    {
                        continue;
                    }

                    AssetDatabase.ImportAsset(
                        Descriptors[index].DetailAtlasPath,
                        ImportAssetOptions.ForceUpdate |
                        ImportAssetOptions.ForceSynchronousImport);
                }

                AssetDatabase.ImportAsset(
                    AnimationManifestPath,
                    ImportAssetOptions.ForceUpdate |
                    ImportAssetOptions.ForceSynchronousImport);
                AssetDatabase.ImportAsset(
                    AnimationPath,
                    ImportAssetOptions.ForceUpdate |
                    ImportAssetOptions.ForceSynchronousImport);

                CityPedestrianAnimationManifest animationManifest =
                    LoadAndValidateAnimationManifest();

                Material sharedMaterial =
                    AssetDatabase.LoadAssetAtPath<Material>(
                        SharedMaterialPath);
                if (sharedMaterial == null)
                {
                    throw new InvalidOperationException(
                        $"Shared Player3DLit material is missing at " +
                        $"'{SharedMaterialPath}'.");
                }

                for (int index = 0; index < Descriptors.Length; index++)
                {
                    BuildDescriptor(
                        Descriptors[index],
                        animationManifest,
                        sharedMaterial);
                }

                BindYardBabushkaProvider(Descriptors.Single(candidate =>
                    string.Equals(
                        candidate.DesignId,
                        DryingYardBabushkaProvider.DesignId,
                        StringComparison.Ordinal)));
                BindWeighAttendantProvider(Descriptors.Single(candidate =>
                    string.Equals(
                        candidate.DesignId,
                        WeighbridgeAttendantProvider.DesignId,
                        StringComparison.Ordinal)));
                BindCemeteryMournerProvider(Descriptors.Single(candidate =>
                    string.Equals(
                        candidate.DesignId,
                        CemeteryMournerProvider.DesignId,
                        StringComparison.Ordinal)));
                BindCemeteryWatchmanProvider(Descriptors.Single(candidate =>
                    string.Equals(
                        candidate.DesignId,
                        CemeteryWatchmanProvider.DesignId,
                        StringComparison.Ordinal)));
                BindParkChessPlayerProvider(Descriptors.Single(candidate =>
                    string.Equals(
                        candidate.DesignId,
                        ParkChessPlayerProvider.DesignId,
                        StringComparison.Ordinal)));
                BindParkCheckersPlayerProvider(Descriptors.Single(candidate =>
                    string.Equals(
                        candidate.DesignId,
                        ParkCheckersPlayerProvider.DesignId,
                        StringComparison.Ordinal)));
                BindSeacoastFishermanProvider(Descriptors.Single(candidate =>
                    string.Equals(
                        candidate.DesignId,
                        SeacoastFishermanProvider.DesignId,
                        StringComparison.Ordinal)));
                BindLastRouteFerrymanProvider(Descriptors.Single(candidate =>
                    string.Equals(
                        candidate.DesignId,
                        LastRouteFerrymanProvider.DesignId,
                        StringComparison.Ordinal)));
                AssetDatabase.SaveAssets();
                ValidateOrThrow();
            }
            finally
            {
                isBuilding = false;
            }
        }

        public static void BuildPipebackRollerOrThrow()
        {
            if (isBuilding)
            {
                return;
            }

            if (!SourcesExist())
            {
                throw new InvalidOperationException(
                    "The staged Pipeback Roller build requires its model, " +
                    "manifest, locomotion library, production Hero/NPC V2 " +
                    "model and shared Player3DLit material.");
            }

            PedestrianDescriptor descriptor = Descriptors.Single(candidate =>
                string.Equals(
                    candidate.DesignId,
                    YardWheelchairProvider.DesignId,
                    StringComparison.Ordinal));
            isBuilding = true;
            try
            {
                EnsureFolderForAsset(descriptor.PrefabPath);
                AssetDatabase.ImportAsset(
                    PlayerModelPath,
                    ImportAssetOptions.ForceUpdate |
                    ImportAssetOptions.ForceSynchronousImport);
                AssetDatabase.ImportAsset(
                    descriptor.ModelPath,
                    ImportAssetOptions.ForceUpdate |
                    ImportAssetOptions.ForceSynchronousImport);
                AssetDatabase.ImportAsset(
                    descriptor.ManifestPath,
                    ImportAssetOptions.ForceUpdate |
                    ImportAssetOptions.ForceSynchronousImport);
                AssetDatabase.ImportAsset(
                    AnimationManifestPath,
                    ImportAssetOptions.ForceUpdate |
                    ImportAssetOptions.ForceSynchronousImport);
                AssetDatabase.ImportAsset(
                    AnimationPath,
                    ImportAssetOptions.ForceUpdate |
                    ImportAssetOptions.ForceSynchronousImport);

                CityPedestrianAnimationManifest animationManifest =
                    LoadAndValidateAnimationManifest();
                Material sharedMaterial =
                    AssetDatabase.LoadAssetAtPath<Material>(
                        SharedMaterialPath);
                if (sharedMaterial == null)
                {
                    throw new InvalidOperationException(
                        $"Shared Player3DLit material is missing at " +
                        $"'{SharedMaterialPath}'.");
                }

                BuildDescriptor(
                    descriptor,
                    animationManifest,
                    sharedMaterial);
                AssetDatabase.SaveAssets();
                ValidateDescriptor(descriptor, animationManifest);
            }
            finally
            {
                isBuilding = false;
            }
        }

        public static void BuildYardBabushkaOrThrow()
        {
            if (isBuilding)
            {
                return;
            }

            if (!SourcesExist())
            {
                throw new InvalidOperationException(
                    "The staged Yard Babushka build requires its model, " +
                    "manifest, locomotion library, production Hero/NPC V2 " +
                    "model and shared Player3DLit material.");
            }

            PedestrianDescriptor descriptor = Descriptors.Single(candidate =>
                string.Equals(
                    candidate.DesignId,
                    DryingYardBabushkaProvider.DesignId,
                    StringComparison.Ordinal));
            isBuilding = true;
            try
            {
                EnsureFolderForAsset(descriptor.PrefabPath);
                AssetDatabase.ImportAsset(
                    PlayerModelPath,
                    ImportAssetOptions.ForceUpdate |
                    ImportAssetOptions.ForceSynchronousImport);
                AssetDatabase.ImportAsset(
                    descriptor.ModelPath,
                    ImportAssetOptions.ForceUpdate |
                    ImportAssetOptions.ForceSynchronousImport);
                AssetDatabase.ImportAsset(
                    descriptor.ManifestPath,
                    ImportAssetOptions.ForceUpdate |
                    ImportAssetOptions.ForceSynchronousImport);
                AssetDatabase.ImportAsset(
                    AnimationManifestPath,
                    ImportAssetOptions.ForceUpdate |
                    ImportAssetOptions.ForceSynchronousImport);
                AssetDatabase.ImportAsset(
                    AnimationPath,
                    ImportAssetOptions.ForceUpdate |
                    ImportAssetOptions.ForceSynchronousImport);

                CityPedestrianAnimationManifest animationManifest =
                    LoadAndValidateAnimationManifest();
                Material sharedMaterial =
                    AssetDatabase.LoadAssetAtPath<Material>(
                        SharedMaterialPath);
                if (sharedMaterial == null)
                {
                    throw new InvalidOperationException(
                        $"Shared Player3DLit material is missing at " +
                        $"'{SharedMaterialPath}'.");
                }

                BuildDescriptor(
                    descriptor,
                    animationManifest,
                    sharedMaterial);
                BindYardBabushkaProvider(descriptor);
                AssetDatabase.SaveAssets();
                ValidateDescriptor(descriptor, animationManifest);
            }
            finally
            {
                isBuilding = false;
            }
        }

        public static void BuildWeighAttendantOrThrow()
        {
            if (isBuilding)
            {
                return;
            }

            if (!SourcesExist())
            {
                throw new InvalidOperationException(
                    "The staged Weigh Attendant build requires its " +
                    "model, manifest, locomotion library, production " +
                    "Hero/NPC V2 model and shared Player3DLit material.");
            }

            PedestrianDescriptor descriptor = Descriptors.Single(candidate =>
                string.Equals(
                    candidate.DesignId,
                    WeighbridgeAttendantProvider.DesignId,
                    StringComparison.Ordinal));
            isBuilding = true;
            try
            {
                EnsureFolderForAsset(descriptor.PrefabPath);
                AssetDatabase.ImportAsset(
                    PlayerModelPath,
                    ImportAssetOptions.ForceUpdate |
                    ImportAssetOptions.ForceSynchronousImport);
                AssetDatabase.ImportAsset(
                    descriptor.ModelPath,
                    ImportAssetOptions.ForceUpdate |
                    ImportAssetOptions.ForceSynchronousImport);
                AssetDatabase.ImportAsset(
                    descriptor.ManifestPath,
                    ImportAssetOptions.ForceUpdate |
                    ImportAssetOptions.ForceSynchronousImport);
                AssetDatabase.ImportAsset(
                    AnimationManifestPath,
                    ImportAssetOptions.ForceUpdate |
                    ImportAssetOptions.ForceSynchronousImport);
                AssetDatabase.ImportAsset(
                    AnimationPath,
                    ImportAssetOptions.ForceUpdate |
                    ImportAssetOptions.ForceSynchronousImport);

                CityPedestrianAnimationManifest animationManifest =
                    LoadAndValidateAnimationManifest();
                Material sharedMaterial =
                    AssetDatabase.LoadAssetAtPath<Material>(
                        SharedMaterialPath);
                if (sharedMaterial == null)
                {
                    throw new InvalidOperationException(
                        $"Shared Player3DLit material is missing at " +
                        $"'{SharedMaterialPath}'.");
                }

                BuildDescriptor(
                    descriptor,
                    animationManifest,
                    sharedMaterial);
                BindWeighAttendantProvider(descriptor);
                AssetDatabase.SaveAssets();
                ValidateDescriptor(descriptor, animationManifest);
            }
            finally
            {
                isBuilding = false;
            }
        }

        public static void BuildCemeteryMournerOrThrow()
        {
            if (isBuilding)
            {
                return;
            }

            if (!SourcesExist())
            {
                throw new InvalidOperationException(
                    "The staged Cemetery Mourner build requires its " +
                    "model, manifest, locomotion library, production " +
                    "Hero/NPC V2 model and shared Player3DLit material.");
            }

            PedestrianDescriptor descriptor = Descriptors.Single(candidate =>
                string.Equals(
                    candidate.DesignId,
                    CemeteryMournerProvider.DesignId,
                    StringComparison.Ordinal));
            isBuilding = true;
            try
            {
                EnsureFolderForAsset(descriptor.PrefabPath);
                AssetDatabase.ImportAsset(
                    PlayerModelPath,
                    ImportAssetOptions.ForceUpdate |
                    ImportAssetOptions.ForceSynchronousImport);
                AssetDatabase.ImportAsset(
                    descriptor.ModelPath,
                    ImportAssetOptions.ForceUpdate |
                    ImportAssetOptions.ForceSynchronousImport);
                AssetDatabase.ImportAsset(
                    descriptor.ManifestPath,
                    ImportAssetOptions.ForceUpdate |
                    ImportAssetOptions.ForceSynchronousImport);
                AssetDatabase.ImportAsset(
                    AnimationManifestPath,
                    ImportAssetOptions.ForceUpdate |
                    ImportAssetOptions.ForceSynchronousImport);
                AssetDatabase.ImportAsset(
                    AnimationPath,
                    ImportAssetOptions.ForceUpdate |
                    ImportAssetOptions.ForceSynchronousImport);

                CityPedestrianAnimationManifest animationManifest =
                    LoadAndValidateAnimationManifest();
                Material sharedMaterial =
                    AssetDatabase.LoadAssetAtPath<Material>(
                        SharedMaterialPath);
                if (sharedMaterial == null)
                {
                    throw new InvalidOperationException(
                        $"Shared Player3DLit material is missing at " +
                        $"'{SharedMaterialPath}'.");
                }

                BuildDescriptor(
                    descriptor,
                    animationManifest,
                    sharedMaterial);
                BindCemeteryMournerProvider(descriptor);
                AssetDatabase.SaveAssets();
                ValidateDescriptor(descriptor, animationManifest);
            }
            finally
            {
                isBuilding = false;
            }
        }

        public static void BuildCemeteryWatchmanOrThrow()
        {
            if (isBuilding)
            {
                return;
            }

            if (!SourcesExist())
            {
                throw new InvalidOperationException(
                    "The staged Cemetery Watchman build requires its " +
                    "model, manifest, locomotion library, production " +
                    "Hero/NPC V2 model and shared Player3DLit material.");
            }

            PedestrianDescriptor descriptor = Descriptors.Single(candidate =>
                string.Equals(
                    candidate.DesignId,
                    CemeteryWatchmanProvider.DesignId,
                    StringComparison.Ordinal));
            isBuilding = true;
            try
            {
                EnsureFolderForAsset(descriptor.PrefabPath);
                AssetDatabase.ImportAsset(
                    PlayerModelPath,
                    ImportAssetOptions.ForceUpdate |
                    ImportAssetOptions.ForceSynchronousImport);
                AssetDatabase.ImportAsset(
                    descriptor.ModelPath,
                    ImportAssetOptions.ForceUpdate |
                    ImportAssetOptions.ForceSynchronousImport);
                AssetDatabase.ImportAsset(
                    descriptor.ManifestPath,
                    ImportAssetOptions.ForceUpdate |
                    ImportAssetOptions.ForceSynchronousImport);
                AssetDatabase.ImportAsset(
                    AnimationManifestPath,
                    ImportAssetOptions.ForceUpdate |
                    ImportAssetOptions.ForceSynchronousImport);
                AssetDatabase.ImportAsset(
                    AnimationPath,
                    ImportAssetOptions.ForceUpdate |
                    ImportAssetOptions.ForceSynchronousImport);

                CityPedestrianAnimationManifest animationManifest =
                    LoadAndValidateAnimationManifest();
                Material sharedMaterial =
                    AssetDatabase.LoadAssetAtPath<Material>(
                        SharedMaterialPath);
                if (sharedMaterial == null)
                {
                    throw new InvalidOperationException(
                        $"Shared Player3DLit material is missing at " +
                        $"'{SharedMaterialPath}'.");
                }

                BuildDescriptor(
                    descriptor,
                    animationManifest,
                    sharedMaterial);
                BindCemeteryWatchmanProvider(descriptor);
                AssetDatabase.SaveAssets();
                ValidateDescriptor(descriptor, animationManifest);
            }
            finally
            {
                isBuilding = false;
            }
        }

        public static void BuildLakeFishermanOrThrow()
        {
            if (isBuilding)
            {
                return;
            }

            if (!SourcesExist())
            {
                throw new InvalidOperationException(
                    "The staged Lake Fisherman build requires its " +
                    "model, manifest, locomotion library, production " +
                    "Hero/NPC V2 model and shared Player3DLit material.");
            }

            PedestrianDescriptor descriptor = Descriptors.Single(candidate =>
                string.Equals(
                    candidate.DesignId,
                    SeacoastFishermanProvider.DesignId,
                    StringComparison.Ordinal));
            isBuilding = true;
            try
            {
                EnsureFolderForAsset(descriptor.PrefabPath);
                AssetDatabase.ImportAsset(
                    PlayerModelPath,
                    ImportAssetOptions.ForceUpdate |
                    ImportAssetOptions.ForceSynchronousImport);
                AssetDatabase.ImportAsset(
                    descriptor.ModelPath,
                    ImportAssetOptions.ForceUpdate |
                    ImportAssetOptions.ForceSynchronousImport);
                AssetDatabase.ImportAsset(
                    descriptor.ManifestPath,
                    ImportAssetOptions.ForceUpdate |
                    ImportAssetOptions.ForceSynchronousImport);
                AssetDatabase.ImportAsset(
                    AnimationManifestPath,
                    ImportAssetOptions.ForceUpdate |
                    ImportAssetOptions.ForceSynchronousImport);
                AssetDatabase.ImportAsset(
                    AnimationPath,
                    ImportAssetOptions.ForceUpdate |
                    ImportAssetOptions.ForceSynchronousImport);

                CityPedestrianAnimationManifest animationManifest =
                    LoadAndValidateAnimationManifest();
                Material sharedMaterial =
                    AssetDatabase.LoadAssetAtPath<Material>(
                        SharedMaterialPath);
                if (sharedMaterial == null)
                {
                    throw new InvalidOperationException(
                        $"Shared Player3DLit material is missing at " +
                        $"'{SharedMaterialPath}'.");
                }

                BuildDescriptor(
                    descriptor,
                    animationManifest,
                    sharedMaterial);
                BindSeacoastFishermanProvider(descriptor);
                AssetDatabase.SaveAssets();
                ValidateDescriptor(descriptor, animationManifest);
            }
            finally
            {
                isBuilding = false;
            }
        }

        public static void BuildLastRouteFerrymanOrThrow()
        {
            if (isBuilding)
            {
                return;
            }

            if (!SourcesExist())
            {
                throw new InvalidOperationException(
                    "The staged Ferryman build requires its model, " +
                    "manifest, locomotion library, production Hero/NPC V2 " +
                    "model and shared Player3DLit material.");
            }

            PedestrianDescriptor descriptor = Descriptors.Single(candidate =>
                string.Equals(
                    candidate.DesignId,
                    LastRouteFerrymanProvider.DesignId,
                    StringComparison.Ordinal));
            isBuilding = true;
            try
            {
                EnsureFolderForAsset(descriptor.PrefabPath);
                AssetDatabase.ImportAsset(
                    PlayerModelPath,
                    ImportAssetOptions.ForceUpdate |
                    ImportAssetOptions.ForceSynchronousImport);
                AssetDatabase.ImportAsset(
                    descriptor.ModelPath,
                    ImportAssetOptions.ForceUpdate |
                    ImportAssetOptions.ForceSynchronousImport);
                AssetDatabase.ImportAsset(
                    descriptor.ManifestPath,
                    ImportAssetOptions.ForceUpdate |
                    ImportAssetOptions.ForceSynchronousImport);
                AssetDatabase.ImportAsset(
                    AnimationManifestPath,
                    ImportAssetOptions.ForceUpdate |
                    ImportAssetOptions.ForceSynchronousImport);
                AssetDatabase.ImportAsset(
                    AnimationPath,
                    ImportAssetOptions.ForceUpdate |
                    ImportAssetOptions.ForceSynchronousImport);

                CityPedestrianAnimationManifest animationManifest =
                    LoadAndValidateAnimationManifest();
                Material sharedMaterial =
                    AssetDatabase.LoadAssetAtPath<Material>(
                        SharedMaterialPath);
                if (sharedMaterial == null)
                {
                    throw new InvalidOperationException(
                        $"Shared Player3DLit material is missing at " +
                        $"'{SharedMaterialPath}'.");
                }

                BuildDescriptor(
                    descriptor,
                    animationManifest,
                    sharedMaterial);
                BindLastRouteFerrymanProvider(descriptor);
                AssetDatabase.SaveAssets();
                ValidateDescriptor(descriptor, animationManifest);
            }
            finally
            {
                isBuilding = false;
            }
        }

        public static void BuildParkChessPlayerOrThrow()
        {
            if (isBuilding)
            {
                return;
            }

            if (!SourcesExist())
            {
                throw new InvalidOperationException(
                    "The staged Park Chess Player build requires its " +
                    "model, manifest, locomotion library, production " +
                    "Hero/NPC V2 model and shared Player3DLit material.");
            }

            PedestrianDescriptor descriptor = Descriptors.Single(candidate =>
                string.Equals(
                    candidate.DesignId,
                    ParkChessPlayerProvider.DesignId,
                    StringComparison.Ordinal));
            isBuilding = true;
            try
            {
                EnsureFolderForAsset(descriptor.PrefabPath);
                AssetDatabase.ImportAsset(
                    PlayerModelPath,
                    ImportAssetOptions.ForceUpdate |
                    ImportAssetOptions.ForceSynchronousImport);
                AssetDatabase.ImportAsset(
                    descriptor.ModelPath,
                    ImportAssetOptions.ForceUpdate |
                    ImportAssetOptions.ForceSynchronousImport);
                AssetDatabase.ImportAsset(
                    descriptor.ManifestPath,
                    ImportAssetOptions.ForceUpdate |
                    ImportAssetOptions.ForceSynchronousImport);
                AssetDatabase.ImportAsset(
                    AnimationManifestPath,
                    ImportAssetOptions.ForceUpdate |
                    ImportAssetOptions.ForceSynchronousImport);
                AssetDatabase.ImportAsset(
                    AnimationPath,
                    ImportAssetOptions.ForceUpdate |
                    ImportAssetOptions.ForceSynchronousImport);

                CityPedestrianAnimationManifest animationManifest =
                    LoadAndValidateAnimationManifest();
                Material sharedMaterial =
                    AssetDatabase.LoadAssetAtPath<Material>(
                        SharedMaterialPath);
                if (sharedMaterial == null)
                {
                    throw new InvalidOperationException(
                        $"Shared Player3DLit material is missing at " +
                        $"'{SharedMaterialPath}'.");
                }

                BuildDescriptor(
                    descriptor,
                    animationManifest,
                    sharedMaterial);
                BindParkChessPlayerProvider(descriptor);
                AssetDatabase.SaveAssets();
                ValidateDescriptor(descriptor, animationManifest);
            }
            finally
            {
                isBuilding = false;
            }
        }

        /// <summary>
        /// Creates or rewires the one Resources provider asset that
        /// carries the staged park chess player prefab into a build —
        /// the automated binding the babushka pass established.
        /// </summary>
        private static void BindParkChessPlayerProvider(
            PedestrianDescriptor descriptor)
        {
            GameObject prefab =
                AssetDatabase.LoadAssetAtPath<GameObject>(
                    descriptor.PrefabPath);
            if (prefab == null)
            {
                throw new InvalidOperationException(
                    "Cannot bind the park chess player provider before " +
                    "its prefab exists.");
            }

            ParkChessPlayerProvider provider =
                AssetDatabase.LoadAssetAtPath<ParkChessPlayerProvider>(
                    ParkChessPlayerProviderPath);
            if (provider == null)
            {
                EnsureFolderForAsset(ParkChessPlayerProviderPath);
                provider = ScriptableObject
                    .CreateInstance<ParkChessPlayerProvider>();
                AssetDatabase.CreateAsset(
                    provider,
                    ParkChessPlayerProviderPath);
            }

            var serialized = new SerializedObject(provider);
            SerializedProperty prefabProperty =
                serialized.FindProperty("stagedPrefab");
            if (prefabProperty == null)
            {
                throw new InvalidOperationException(
                    "ParkChessPlayerProvider has no serialized " +
                    "'stagedPrefab' field.");
            }

            prefabProperty.objectReferenceValue = prefab;
            serialized.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(provider);
        }

        public static void BuildParkCheckersPlayerOrThrow()
        {
            if (isBuilding)
            {
                return;
            }

            if (!SourcesExist())
            {
                throw new InvalidOperationException(
                    "The staged Park Checkers Player build requires its " +
                    "model, manifest, locomotion library, production " +
                    "Hero/NPC V2 model and shared Player3DLit material.");
            }

            PedestrianDescriptor descriptor = Descriptors.Single(candidate =>
                string.Equals(
                    candidate.DesignId,
                    ParkCheckersPlayerProvider.DesignId,
                    StringComparison.Ordinal));
            isBuilding = true;
            try
            {
                EnsureFolderForAsset(descriptor.PrefabPath);
                AssetDatabase.ImportAsset(
                    PlayerModelPath,
                    ImportAssetOptions.ForceUpdate |
                    ImportAssetOptions.ForceSynchronousImport);
                AssetDatabase.ImportAsset(
                    descriptor.ModelPath,
                    ImportAssetOptions.ForceUpdate |
                    ImportAssetOptions.ForceSynchronousImport);
                AssetDatabase.ImportAsset(
                    descriptor.ManifestPath,
                    ImportAssetOptions.ForceUpdate |
                    ImportAssetOptions.ForceSynchronousImport);
                AssetDatabase.ImportAsset(
                    AnimationManifestPath,
                    ImportAssetOptions.ForceUpdate |
                    ImportAssetOptions.ForceSynchronousImport);
                AssetDatabase.ImportAsset(
                    AnimationPath,
                    ImportAssetOptions.ForceUpdate |
                    ImportAssetOptions.ForceSynchronousImport);

                CityPedestrianAnimationManifest animationManifest =
                    LoadAndValidateAnimationManifest();
                Material sharedMaterial =
                    AssetDatabase.LoadAssetAtPath<Material>(
                        SharedMaterialPath);
                if (sharedMaterial == null)
                {
                    throw new InvalidOperationException(
                        $"Shared Player3DLit material is missing at " +
                        $"'{SharedMaterialPath}'.");
                }

                BuildDescriptor(
                    descriptor,
                    animationManifest,
                    sharedMaterial);
                BindParkCheckersPlayerProvider(descriptor);
                AssetDatabase.SaveAssets();
                ValidateDescriptor(descriptor, animationManifest);
            }
            finally
            {
                isBuilding = false;
            }
        }

        /// <summary>
        /// Creates or rewires the one Resources provider asset that
        /// carries the staged park checkers player prefab into a build —
        /// the automated binding the babushka pass established.
        /// </summary>
        private static void BindParkCheckersPlayerProvider(
            PedestrianDescriptor descriptor)
        {
            GameObject prefab =
                AssetDatabase.LoadAssetAtPath<GameObject>(
                    descriptor.PrefabPath);
            if (prefab == null)
            {
                throw new InvalidOperationException(
                    "Cannot bind the park checkers player provider before " +
                    "its prefab exists.");
            }

            ParkCheckersPlayerProvider provider =
                AssetDatabase.LoadAssetAtPath<ParkCheckersPlayerProvider>(
                    ParkCheckersPlayerProviderPath);
            if (provider == null)
            {
                EnsureFolderForAsset(ParkCheckersPlayerProviderPath);
                provider = ScriptableObject
                    .CreateInstance<ParkCheckersPlayerProvider>();
                AssetDatabase.CreateAsset(
                    provider,
                    ParkCheckersPlayerProviderPath);
            }

            var serialized = new SerializedObject(provider);
            SerializedProperty prefabProperty =
                serialized.FindProperty("stagedPrefab");
            if (prefabProperty == null)
            {
                throw new InvalidOperationException(
                    "ParkCheckersPlayerProvider has no serialized " +
                    "'stagedPrefab' field.");
            }

            prefabProperty.objectReferenceValue = prefab;
            serialized.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(provider);
        }

        /// <summary>
        /// Creates or rewires the one Resources provider asset that
        /// carries the staged lake fisherman prefab into a build — the
        /// automated binding the babushka pass established.
        /// </summary>
        /// <summary>
        /// Creates or rewires the one Resources provider asset that
        /// carries the staged Ferryman prefab into a build.
        /// </summary>
        private static void BindLastRouteFerrymanProvider(
            PedestrianDescriptor descriptor)
        {
            GameObject prefab =
                AssetDatabase.LoadAssetAtPath<GameObject>(
                    descriptor.PrefabPath);
            if (prefab == null)
            {
                throw new InvalidOperationException(
                    "Cannot bind the Ferryman provider before its " +
                    "prefab exists.");
            }

            LastRouteFerrymanProvider provider =
                AssetDatabase
                    .LoadAssetAtPath<LastRouteFerrymanProvider>(
                        LastRouteFerrymanProviderPath);
            if (provider == null)
            {
                EnsureFolderForAsset(LastRouteFerrymanProviderPath);
                provider = ScriptableObject
                    .CreateInstance<LastRouteFerrymanProvider>();
                AssetDatabase.CreateAsset(
                    provider,
                    LastRouteFerrymanProviderPath);
            }

            var serialized = new SerializedObject(provider);
            SerializedProperty prefabProperty =
                serialized.FindProperty("stagedPrefab");
            if (prefabProperty == null)
            {
                throw new InvalidOperationException(
                    "LastRouteFerrymanProvider has no serialized " +
                    "'stagedPrefab' field.");
            }

            prefabProperty.objectReferenceValue = prefab;
            serialized.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(provider);
        }

        private static void BindSeacoastFishermanProvider(
            PedestrianDescriptor descriptor)
        {
            GameObject prefab =
                AssetDatabase.LoadAssetAtPath<GameObject>(
                    descriptor.PrefabPath);
            if (prefab == null)
            {
                throw new InvalidOperationException(
                    "Cannot bind the lake fisherman provider before " +
                    "its prefab exists.");
            }

            SeacoastFishermanProvider provider =
                AssetDatabase.LoadAssetAtPath<SeacoastFishermanProvider>(
                    SeacoastFishermanProviderPath);
            if (provider == null)
            {
                EnsureFolderForAsset(SeacoastFishermanProviderPath);
                provider = ScriptableObject
                    .CreateInstance<SeacoastFishermanProvider>();
                AssetDatabase.CreateAsset(
                    provider,
                    SeacoastFishermanProviderPath);
            }

            var serialized = new SerializedObject(provider);
            SerializedProperty prefabProperty =
                serialized.FindProperty("stagedPrefab");
            if (prefabProperty == null)
            {
                throw new InvalidOperationException(
                    "SeacoastFishermanProvider has no serialized " +
                    "'stagedPrefab' field.");
            }

            prefabProperty.objectReferenceValue = prefab;
            serialized.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(provider);
        }

        /// <summary>
        /// Creates or rewires the one Resources provider asset that
        /// carries the staged cemetery watchman prefab into a build —
        /// the automated binding the babushka pass established.
        /// </summary>
        private static void BindCemeteryWatchmanProvider(
            PedestrianDescriptor descriptor)
        {
            GameObject prefab =
                AssetDatabase.LoadAssetAtPath<GameObject>(
                    descriptor.PrefabPath);
            if (prefab == null)
            {
                throw new InvalidOperationException(
                    "Cannot bind the cemetery watchman provider before " +
                    "its prefab exists.");
            }

            CemeteryWatchmanProvider provider =
                AssetDatabase.LoadAssetAtPath<CemeteryWatchmanProvider>(
                    CemeteryWatchmanProviderPath);
            if (provider == null)
            {
                EnsureFolderForAsset(CemeteryWatchmanProviderPath);
                provider = ScriptableObject
                    .CreateInstance<CemeteryWatchmanProvider>();
                AssetDatabase.CreateAsset(
                    provider,
                    CemeteryWatchmanProviderPath);
            }

            var serialized = new SerializedObject(provider);
            SerializedProperty prefabProperty =
                serialized.FindProperty("stagedPrefab");
            if (prefabProperty == null)
            {
                throw new InvalidOperationException(
                    "CemeteryWatchmanProvider has no serialized " +
                    "'stagedPrefab' field.");
            }

            prefabProperty.objectReferenceValue = prefab;
            serialized.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(provider);
        }

        /// <summary>
        /// Creates or rewires the one Resources provider asset that
        /// carries the staged cemetery mourner prefab into a build —
        /// the automated binding the babushka pass established.
        /// </summary>
        private static void BindCemeteryMournerProvider(
            PedestrianDescriptor descriptor)
        {
            GameObject prefab =
                AssetDatabase.LoadAssetAtPath<GameObject>(
                    descriptor.PrefabPath);
            if (prefab == null)
            {
                throw new InvalidOperationException(
                    "Cannot bind the cemetery mourner provider before " +
                    "its prefab exists.");
            }

            CemeteryMournerProvider provider =
                AssetDatabase.LoadAssetAtPath<CemeteryMournerProvider>(
                    CemeteryMournerProviderPath);
            if (provider == null)
            {
                EnsureFolderForAsset(CemeteryMournerProviderPath);
                provider = ScriptableObject
                    .CreateInstance<CemeteryMournerProvider>();
                AssetDatabase.CreateAsset(
                    provider,
                    CemeteryMournerProviderPath);
            }

            var serialized = new SerializedObject(provider);
            SerializedProperty prefabProperty =
                serialized.FindProperty("stagedPrefab");
            if (prefabProperty == null)
            {
                throw new InvalidOperationException(
                    "CemeteryMournerProvider has no serialized " +
                    "'stagedPrefab' field.");
            }

            prefabProperty.objectReferenceValue = prefab;
            serialized.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(provider);
        }

        /// <summary>
        /// Creates or rewires the one Resources provider asset that
        /// carries the staged weigh attendant prefab into a build —
        /// the automated binding the babushka pass established.
        /// </summary>
        private static void BindWeighAttendantProvider(
            PedestrianDescriptor descriptor)
        {
            GameObject prefab =
                AssetDatabase.LoadAssetAtPath<GameObject>(
                    descriptor.PrefabPath);
            if (prefab == null)
            {
                throw new InvalidOperationException(
                    "Cannot bind the weigh attendant provider before " +
                    "its prefab exists.");
            }

            WeighbridgeAttendantProvider provider =
                AssetDatabase.LoadAssetAtPath<WeighbridgeAttendantProvider>(
                    WeighAttendantProviderPath);
            if (provider == null)
            {
                EnsureFolderForAsset(WeighAttendantProviderPath);
                provider = ScriptableObject
                    .CreateInstance<WeighbridgeAttendantProvider>();
                AssetDatabase.CreateAsset(
                    provider,
                    WeighAttendantProviderPath);
            }

            var serialized = new SerializedObject(provider);
            SerializedProperty prefabProperty =
                serialized.FindProperty("stagedPrefab");
            if (prefabProperty == null)
            {
                throw new InvalidOperationException(
                    "WeighbridgeAttendantProvider has no serialized " +
                    "'stagedPrefab' field.");
            }

            prefabProperty.objectReferenceValue = prefab;
            serialized.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(provider);
        }

        /// <summary>
        /// Creates or rewires the one Resources provider asset that
        /// carries the staged babushka prefab into a build. The rider's
        /// provider was authored by hand once; automating the binding
        /// here protects the reference across prefab rebuilds.
        /// </summary>
        private static void BindYardBabushkaProvider(
            PedestrianDescriptor descriptor)
        {
            GameObject prefab =
                AssetDatabase.LoadAssetAtPath<GameObject>(
                    descriptor.PrefabPath);
            if (prefab == null)
            {
                throw new InvalidOperationException(
                    "Cannot bind the babushka provider before its " +
                    "prefab exists.");
            }

            DryingYardBabushkaProvider provider =
                AssetDatabase.LoadAssetAtPath<DryingYardBabushkaProvider>(
                    YardBabushkaProviderPath);
            if (provider == null)
            {
                EnsureFolderForAsset(YardBabushkaProviderPath);
                provider = ScriptableObject
                    .CreateInstance<DryingYardBabushkaProvider>();
                AssetDatabase.CreateAsset(
                    provider,
                    YardBabushkaProviderPath);
            }

            var serialized = new SerializedObject(provider);
            SerializedProperty prefabProperty =
                serialized.FindProperty("stagedPrefab");
            if (prefabProperty == null)
            {
                throw new InvalidOperationException(
                    "DryingYardBabushkaProvider has no serialized " +
                    "'stagedPrefab' field.");
            }

            prefabProperty.objectReferenceValue = prefab;
            serialized.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(provider);
        }

        private static void BuildDescriptor(
            PedestrianDescriptor descriptor,
            CityPedestrianAnimationManifest animationManifest,
            Material sharedMaterial)
        {
            CityPedestrianManifest manifest =
                LoadAndValidateManifest(descriptor);
            GameObject modelAsset =
                AssetDatabase.LoadAssetAtPath<GameObject>(
                    descriptor.ModelPath);
            if (modelAsset == null)
            {
                throw new InvalidOperationException(
                    $"Unity did not import a model from " +
                    $"'{descriptor.ModelPath}'.");
            }

            AnimationClip idle = LoadLocomotionClip(
                descriptor.IdleClipName,
                descriptor.IdleDuration,
                animationManifest);
            AnimationClip walk = LoadLocomotionClip(
                descriptor.WalkClipName,
                descriptor.WalkDuration,
                animationManifest);
            AnimationClip sit = descriptor.RidesBus
                ? LoadLocomotionClip(
                    descriptor.SitClipName,
                    descriptor.SitDuration,
                    animationManifest)
                : null;
            AnimationClip action = descriptor.HasAction
                ? LoadLocomotionClip(
                    descriptor.ActionClipName,
                    descriptor.ActionDuration,
                    animationManifest)
                : null;
            AnimationClip ambientIdle = descriptor.HasAmbientGait
                ? LoadLocomotionClip(
                    descriptor.AmbientIdleClipName,
                    descriptor.AmbientIdleDuration,
                    animationManifest)
                : null;
            AnimationClip ambientWalk = descriptor.HasAmbientGait
                ? LoadLocomotionClip(
                    descriptor.AmbientWalkClipName,
                    descriptor.AmbientWalkDuration,
                    animationManifest)
                : null;
            BuildPrefab(
                descriptor,
                modelAsset,
                sharedMaterial,
                idle,
                walk,
                sit,
                action,
                ambientIdle,
                ambientWalk,
                manifest,
                animationManifest);
        }

        public static void ValidateOrThrow()
        {
            CityPedestrianAnimationManifest animationManifest =
                LoadAndValidateAnimationManifest();
            for (int index = 0; index < Descriptors.Length; index++)
            {
                ValidateDescriptor(Descriptors[index], animationManifest);
            }
        }

        private static void ValidateDescriptor(
            PedestrianDescriptor descriptor,
            CityPedestrianAnimationManifest animationManifest)
        {
            CityPedestrianManifest manifest =
                LoadAndValidateManifest(descriptor);
            ValidateImportedModel(descriptor, manifest);

            GameObject prefab =
                AssetDatabase.LoadAssetAtPath<GameObject>(
                    descriptor.PrefabPath);
            if (prefab == null)
            {
                throw new InvalidOperationException(
                    $"City pedestrian prefab is missing at " +
                    $"'{descriptor.PrefabPath}'.");
            }

            CityPedestrianAssetRegistry registry =
                prefab.GetComponent<CityPedestrianAssetRegistry>();
            if (registry == null)
            {
                throw new InvalidOperationException(
                    "City pedestrian prefab has no asset registry.");
            }

            if (registry.Animator == null ||
                registry.Animator.applyRootMotion ||
                registry.Animator.runtimeAnimatorController != null ||
                registry.Animator.avatar == null ||
                !string.Equals(
                    AssetDatabase.GetAssetPath(registry.Animator.avatar),
                    PlayerModelPath,
                    StringComparison.Ordinal) ||
                registry.Animator.cullingMode !=
                    AnimatorCullingMode.CullUpdateTransforms)
            {
                throw new InvalidOperationException(
                    "City pedestrian Animator must be controller-free, " +
                    "culled and have root motion disabled.");
            }

            if (registry.ModelRoot == null ||
                registry.HeadAnchor == null ||
                registry.LeftFootAnchor == null ||
                registry.RightFootAnchor == null)
            {
                throw new InvalidOperationException(
                    "City pedestrian prefab is missing a model/head/foot " +
                    "anchor binding.");
            }

            ValidateFishingRigBindings(prefab, registry, descriptor);
            ValidateCoinRigBindings(prefab, registry, descriptor);
            ValidateKettleRigBindings(prefab, registry, descriptor);
            ValidateDetailAtlasBindings(registry, descriptor, manifest);
            ValidateWheelchairBindings(prefab, registry, descriptor);
            if (descriptor.IsWheelchair)
            {
                ValidateWheelchairVisualScale(
                    prefab,
                    manifest.wheel_radius_m);
            }

            ValidateLocomotionClip(
                registry.IdleClip,
                descriptor.IdleClipName,
                descriptor.IdleDuration,
                animationManifest);
            ValidateLocomotionClip(
                registry.WalkClip,
                descriptor.WalkClipName,
                descriptor.WalkDuration,
                animationManifest);
            if (descriptor.RidesBus)
            {
                ValidateLocomotionClip(
                    registry.SitClip,
                    descriptor.SitClipName,
                    descriptor.SitDuration,
                    animationManifest);
                if (registry.PelvisAnchor == null)
                {
                    throw new InvalidOperationException(
                        "A design that declares a Route 01 ride must bind " +
                        "its pelvis anchor: seating aligns that bone to the " +
                        "cushion instead of pinning the lowest sole.");
                }
            }
            else if (registry.SitClip != null)
            {
                throw new InvalidOperationException(
                    "A design without a declared Route 01 ride must not " +
                    "carry a seated clip.");
            }

            if (descriptor.HasAmbientGait)
            {
                ValidateLocomotionClip(
                    registry.AmbientIdleClip,
                    descriptor.AmbientIdleClipName,
                    descriptor.AmbientIdleDuration,
                    animationManifest);
                ValidateLocomotionClip(
                    registry.AmbientWalkClip,
                    descriptor.AmbientWalkClipName,
                    descriptor.AmbientWalkDuration,
                    animationManifest);
            }
            else if (registry.AmbientIdleClip != null ||
                     registry.AmbientWalkClip != null)
            {
                throw new InvalidOperationException(
                    "A design that does not roam must not carry a street " +
                    "gait: the pool would play a clip nothing validated.");
            }

            if (descriptor.HasAction)
            {
                ValidateLocomotionClip(
                    registry.ActionClip,
                    descriptor.ActionClipName,
                    descriptor.ActionDuration,
                    animationManifest);
            }
            else if (registry.ActionClip != null)
            {
                throw new InvalidOperationException(
                    "A design without a declared authored beat must not " +
                    "carry an action clip.");
            }

            if (registry.Renderers.Count != manifest.mesh_count ||
                registry.RendererBindings.Count != manifest.mesh_count)
            {
                throw new InvalidOperationException(
                    "City pedestrian registry renderer counts differ from " +
                    "the deterministic manifest.");
            }

            if (!string.Equals(
                    registry.DesignId,
                    manifest.design_id,
                    StringComparison.Ordinal) ||
                !string.Equals(
                    registry.SourceGeneratorVersion,
                    manifest.generator_version,
                    StringComparison.Ordinal) ||
                !string.Equals(
                    registry.BuildSignature,
                    manifest.build_signature,
                    StringComparison.Ordinal) ||
                registry.SourceTriangleCount != manifest.triangle_count)
            {
                throw new InvalidOperationException(
                    "City pedestrian registry source metadata is stale.");
            }

            if (Mathf.Abs(registry.LocalBounds.size.y - descriptor.Height) >
                    0.035f ||
                Mathf.Abs(registry.LocalBounds.min.y) > 0.025f)
            {
                throw new InvalidOperationException(
                    "City pedestrian prefab bounds lost canonical height or " +
                    "grounding.");
            }

            ValidateDeclaredHeadLamp(prefab, registry, descriptor);

            if (prefab.GetComponentsInChildren<Collider>(true).Length != 0 ||
                prefab.GetComponentsInChildren<Collider2D>(true).Length != 0 ||
                prefab.GetComponentsInChildren<Rigidbody>(true).Length != 0 ||
                prefab.GetComponentsInChildren<Rigidbody2D>(true).Length != 0 ||
                prefab.GetComponentsInChildren<Camera>(true).Length != 0 ||
                prefab.GetComponentsInChildren<AudioSource>(true).Length != 0)
            {
                throw new InvalidOperationException(
                    "Atmospheric pedestrian prefabs must stay passive: no " +
                    "physics bodies, colliders, lights, cameras or audio.");
            }

            MonoBehaviour[] behaviours =
                prefab.GetComponentsInChildren<MonoBehaviour>(true);
            if (behaviours.Any(behaviour =>
                    behaviour != null &&
                    !(behaviour is CityPedestrianAssetRegistry) &&
                    behaviour is IInteractable))
            {
                throw new InvalidOperationException(
                    "Atmospheric pedestrian prefabs must not be interactive.");
            }

            if (descriptor.IsStaged && behaviours.Any(behaviour =>
                    behaviour != null &&
                    !(behaviour is CityPedestrianAssetRegistry) &&
                    !(behaviour is CityWheelchairNpcAssetRegistry) &&
                    !(behaviour is SeacoastFishermanRigAnchors) &&
                    !(behaviour is LastRouteFerrymanRigAnchors)))
            {
                throw new InvalidOperationException(
                    "A staged pedestrian prefab may carry only its passive " +
                    "asset registries.");
            }

            Material expectedMaterial =
                AssetDatabase.LoadAssetAtPath<Material>(SharedMaterialPath);
            for (int index = 0; index < registry.Renderers.Count; index++)
            {
                Renderer renderer = registry.Renderers[index];
                if (renderer == null ||
                    renderer.sharedMaterials.Length != 1 ||
                    renderer.sharedMaterial != expectedMaterial)
                {
                    throw new InvalidOperationException(
                        "Every pedestrian renderer must reference the one " +
                        "shared Player3DLit material.");
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
                LoadAndValidateAnimationManifest();
                for (int index = 0; index < Descriptors.Length; index++)
                {
                    PedestrianDescriptor descriptor = Descriptors[index];
                    CityPedestrianManifest manifest =
                        LoadAndValidateManifest(descriptor);
                    GameObject prefab =
                        AssetDatabase.LoadAssetAtPath<GameObject>(
                            descriptor.PrefabPath);
                    CityPedestrianAssetRegistry registry = prefab != null
                        ? prefab.GetComponent<CityPedestrianAssetRegistry>()
                        : null;
                    if (registry == null ||
                        !string.Equals(
                            registry.BuildSignature,
                            manifest.build_signature,
                            StringComparison.Ordinal) ||
                        registry.IdleClip == null ||
                        registry.WalkClip == null ||
                        !string.Equals(
                            NormalizeClipName(registry.IdleClip.name),
                            descriptor.IdleClipName,
                            StringComparison.Ordinal) ||
                        !string.Equals(
                            NormalizeClipName(registry.WalkClip.name),
                            descriptor.WalkClipName,
                            StringComparison.Ordinal) ||
                        (descriptor.RidesBus &&
                         (registry.SitClip == null ||
                          registry.PelvisAnchor == null ||
                          !string.Equals(
                              NormalizeClipName(registry.SitClip.name),
                              descriptor.SitClipName,
                              StringComparison.Ordinal))) ||
                        (descriptor.HasAction &&
                         (registry.ActionClip == null ||
                          !string.Equals(
                              NormalizeClipName(registry.ActionClip.name),
                              descriptor.ActionClipName,
                              StringComparison.Ordinal))) ||
                        (descriptor.HasAmbientGait &&
                         (registry.AmbientIdleClip == null ||
                          registry.AmbientWalkClip == null ||
                          !string.Equals(
                              NormalizeClipName(
                                  registry.AmbientIdleClip.name),
                              descriptor.AmbientIdleClipName,
                              StringComparison.Ordinal) ||
                          !string.Equals(
                              NormalizeClipName(
                                  registry.AmbientWalkClip.name),
                              descriptor.AmbientWalkClipName,
                              StringComparison.Ordinal))))
                    {
                        QueueBuildWhenSourcesExist();
                        return;
                    }

                    if (descriptor.IsWheelchair &&
                        !HasExpectedWheelchairVisualScale(
                            prefab,
                            manifest.wheel_radius_m))
                    {
                        QueuePipebackRollerBuildWhenSourcesExist();
                        return;
                    }
                }
            }
            catch (Exception exception)
            {
                Debug.LogError(
                    $"Could not validate City pedestrian source manifest: " +
                    $"{exception}");
                return;
            }

        }

        private static void RunQueuedBuild()
        {
            buildQueued = false;
            if (!SourcesExist())
            {
                return;
            }

            try
            {
                BuildOrThrow();
            }
            catch (Exception exception)
            {
                Debug.LogError(
                    $"Could not build City pedestrian prefab: {exception}");
            }
        }

        private static void RunQueuedPipebackRollerBuild()
        {
            buildQueued = false;
            if (!SourcesExist())
            {
                return;
            }

            try
            {
                BuildPipebackRollerOrThrow();
                Debug.Log("Staged Pipeback Roller prefab rebuilt.");
            }
            catch (Exception exception)
            {
                Debug.LogError(
                    $"Could not build staged Pipeback Roller prefab: " +
                    $"{exception}");
            }
        }

        private static CityPedestrianManifest LoadAndValidateManifest(
            PedestrianDescriptor descriptor)
        {
            ValidateDescriptorScope(descriptor);
            TextAsset source =
                AssetDatabase.LoadAssetAtPath<TextAsset>(
                    descriptor.ManifestPath);
            if (source == null)
            {
                throw new InvalidOperationException(
                    $"Could not import manifest " +
                    $"'{descriptor.ManifestPath}'.");
            }

            CityPedestrianManifest manifest =
                JsonUtility.FromJson<CityPedestrianManifest>(source.text);
            if (manifest == null ||
                manifest.parts == null ||
                manifest.bones == null ||
                manifest.shared_clips == null ||
                manifest.triangle_budget == null ||
                manifest.triangle_budget.Length != 2)
            {
                throw new InvalidOperationException(
                    "City pedestrian manifest is malformed.");
            }

            if (!string.Equals(
                    manifest.design_id,
                    descriptor.DesignId,
                    StringComparison.Ordinal) ||
                !string.Equals(
                    manifest.pose,
                    ExpectedPose,
                    StringComparison.Ordinal) ||
                !string.Equals(
                    manifest.forward_axis,
                    "-Y",
                    StringComparison.Ordinal) ||
                !string.Equals(
                    manifest.anatomical_left_axis,
                    "+X",
                    StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    "City pedestrian design/pose/axis contract differs from " +
                    $"the approved {descriptor.DisplayName}.");
            }

            if (Mathf.Abs(manifest.height_m - descriptor.Height) > 0.0001f ||
                manifest.mesh_count != manifest.parts.Length ||
                manifest.bones.Length != ExpectedBoneCount ||
                manifest.triangle_count < descriptor.MinimumTriangleCount ||
                manifest.triangle_count > descriptor.MaximumTriangleCount ||
                manifest.triangle_budget[0] !=
                    descriptor.MinimumTriangleCount ||
                manifest.triangle_budget[1] !=
                    descriptor.MaximumTriangleCount)
            {
                throw new InvalidOperationException(
                    "City pedestrian manifest height, skeleton, mesh or " +
                    "triangle budget is invalid.");
            }

            if (manifest.emissive ||
                manifest.colliders ||
                manifest.animation_count != 0 ||
                manifest.animations == null ||
                manifest.animations.Length != 0 ||
                !string.Equals(
                    manifest.material_asset,
                    SharedMaterialPath,
                    StringComparison.Ordinal) ||
                !string.Equals(
                    manifest.shared_animation_source,
                    AnimationPath,
                    StringComparison.Ordinal) ||
                !manifest.shared_clips.SequenceEqual(
                    ExpectedSharedClips(descriptor)))
            {
                throw new InvalidOperationException(
                    "City pedestrian must be non-emissive, collider-free, " +
                    "animation-free and reuse Player3DLit plus its custom " +
                    "idle/walk clips.");
            }

            if (string.IsNullOrWhiteSpace(manifest.generator_version) ||
                string.IsNullOrWhiteSpace(manifest.build_signature) ||
                manifest.build_signature.Length != 64)
            {
                throw new InvalidOperationException(
                    "City pedestrian manifest lacks deterministic source " +
                    "metadata.");
            }

            // `staged` and `pool_eligible` used to be opposites; since the
            // seven residents were promoted they are independent. `staged`
            // still means 'authored by the shared library and placed by
            // hand', and `pool_eligible` now means exactly what the runtime
            // catalog says - see `ValidateDescriptorScope` for the same rule
            // applied to where the prefab lives.
            if (descriptor.IsStaged && !manifest.staged)
            {
                throw new InvalidOperationException(
                    "A staged pedestrian manifest must declare its staged " +
                    "status.");
            }

            if (manifest.pool_eligible !=
                CityPedestrianResources.Roams(descriptor.DesignId))
            {
                throw new InvalidOperationException(
                    $"'{descriptor.DesignId}' declares pool_eligible=" +
                    $"{manifest.pool_eligible} in its manifest, which " +
                    "disagrees with the runtime pedestrian catalog. The art " +
                    "generator and the catalog must be promoted together.");
            }

            if (descriptor.IsWheelchair &&
                (Mathf.Abs(manifest.wheel_radius_m - 0.30f) > 0.0001f ||
                 manifest.pivot_names == null ||
                 !manifest.pivot_names.SequenceEqual(new[]
                 {
                     LeftWheelPivotName,
                     RightWheelPivotName,
                     LeftCasterPivotName,
                     RightCasterPivotName,
                     BellowsPivotName,
                     PipeBankPivotName
                 })))
            {
                throw new InvalidOperationException(
                    "The staged wheelchair manifest must declare its " +
                    "0.30 m wheels and six mechanism pivots in stable " +
                    "order.");
            }

            HashSet<string> partNames =
                new HashSet<string>(StringComparer.Ordinal);
            HashSet<string> boneNames =
                new HashSet<string>(StringComparer.Ordinal);
            for (int index = 0; index < manifest.bones.Length; index++)
            {
                CityPedestrianManifestBone bone = manifest.bones[index];
                if (bone == null ||
                    string.IsNullOrEmpty(bone.name) ||
                    !boneNames.Add(bone.name))
                {
                    throw new InvalidOperationException(
                        "City pedestrian manifest contains a missing or " +
                        "duplicate bone.");
                }
            }

            for (int index = 0; index < manifest.parts.Length; index++)
            {
                CityPedestrianManifestPart part = manifest.parts[index];
                if (part == null ||
                    string.IsNullOrEmpty(part.name) ||
                    string.IsNullOrEmpty(part.role) ||
                    string.IsNullOrEmpty(part.palette_name) ||
                    part.base_color == null ||
                    part.base_color.Length != 4 ||
                    !partNames.Add(part.name) ||
                    !boneNames.Contains(part.bone))
                {
                    throw new InvalidOperationException(
                        "City pedestrian manifest contains an invalid part " +
                        "binding.");
                }
            }

            ValidateSignatureEffects(descriptor, manifest);
            ValidateRigAnchors(descriptor, manifest, partNames);
            ValidateTextureBindings(descriptor, manifest, partNames);
            return manifest;
        }

        /// <summary>
        /// A design's always-on effect is declared twice — on its
        /// descriptor here and in the generator's manifest — and the two
        /// must agree, because the runtime factory builds the effect from
        /// the prefab's anchors and the anchors are built from the
        /// descriptor. JsonUtility leaves an absent key null, and absent
        /// is what every design without an effect writes.
        /// </summary>
        private static void ValidateSignatureEffects(
            PedestrianDescriptor descriptor,
            CityPedestrianManifest manifest)
        {
            string[] effects =
                manifest.signature_effects ?? Array.Empty<string>();
            bool declaresKettle = effects.Contains(
                BoilingKettleEffectName,
                StringComparer.Ordinal);
            if (declaresKettle != descriptor.CarriesBoilingKettle ||
                effects.Length != (descriptor.CarriesBoilingKettle ? 1 : 0))
            {
                throw new InvalidOperationException(
                    $"'{descriptor.DisplayName}' " +
                    (descriptor.CarriesBoilingKettle
                        ? "declares a boiling kettle but its manifest " +
                          $"does not list exactly '{BoilingKettleEffectName}' " +
                          "under signature_effects."
                        : "declares no signature effect but its manifest " +
                          "lists one."));
            }
        }

        /// <summary>
        /// The kettle design's two anchors, exactly as the prefab build
        /// will make them: a pivot on the head that the lid and knob are
        /// re-skinned to, and a spout-mouth anchor on the head oriented
        /// along the spout. Every named part has to exist, because the
        /// build measures them off the imported meshes.
        /// </summary>
        private static void ValidateRigAnchors(
            PedestrianDescriptor descriptor,
            CityPedestrianManifest manifest,
            HashSet<string> partNames)
        {
            CityPedestrianManifestRigAnchor[] anchors =
                manifest.rig_anchors ??
                Array.Empty<CityPedestrianManifestRigAnchor>();
            if (!descriptor.CarriesBoilingKettle)
            {
                if (anchors.Length != 0)
                {
                    throw new InvalidOperationException(
                        $"'{descriptor.DisplayName}' declares no rig " +
                        "anchors but its manifest lists some.");
                }

                return;
            }

            if (anchors.Length != 2)
            {
                throw new InvalidOperationException(
                    $"'{descriptor.DisplayName}' must declare exactly two " +
                    "rig anchors: the lid pivot and the spout mouth.");
            }

            RequireRigAnchor(
                anchors[0],
                CityKettleHatRigAnchors.LidAnchorName,
                RigAnchorKindPivot,
                new[]
                {
                    CityKettleHatRigAnchors.LidRendererName,
                    CityKettleHatRigAnchors.KnobRendererName
                },
                string.Empty,
                partNames);
            RequireRigAnchor(
                anchors[1],
                CityKettleHatRigAnchors.SpoutAnchorName,
                RigAnchorKindAnchor,
                new[] { CityKettleHatRigAnchors.SpoutTipRendererName },
                CityKettleHatRigAnchors.SpoutRendererName,
                partNames);
        }

        private static void RequireRigAnchor(
            CityPedestrianManifestRigAnchor anchor,
            string expectedName,
            string expectedKind,
            string[] expectedParts,
            string expectedAxisFrom,
            HashSet<string> partNames)
        {
            if (anchor == null ||
                !string.Equals(
                    anchor.name,
                    expectedName,
                    StringComparison.Ordinal) ||
                !string.Equals(
                    anchor.bone,
                    HeadBoneName,
                    StringComparison.Ordinal) ||
                !string.Equals(
                    anchor.kind,
                    expectedKind,
                    StringComparison.Ordinal) ||
                anchor.parts == null ||
                !anchor.parts.SequenceEqual(
                    expectedParts,
                    StringComparer.Ordinal) ||
                !string.Equals(
                    anchor.axis_from ?? string.Empty,
                    expectedAxisFrom,
                    StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    $"Rig anchor '{expectedName}' must be a '{expectedKind}' " +
                    $"on '{HeadBoneName}' over " +
                    $"[{string.Join(", ", expectedParts)}]" +
                    (expectedAxisFrom.Length == 0
                        ? "."
                        : $" with its axis from '{expectedAxisFrom}'."));
            }

            for (int index = 0; index < expectedParts.Length; index++)
            {
                if (!partNames.Contains(expectedParts[index]))
                {
                    throw new InvalidOperationException(
                        $"Rig anchor '{expectedName}' names part " +
                        $"'{expectedParts[index]}', which the manifest " +
                        "does not contain.");
                }
            }

            if (expectedAxisFrom.Length != 0 &&
                !partNames.Contains(expectedAxisFrom))
            {
                throw new InvalidOperationException(
                    $"Rig anchor '{expectedName}' takes its axis from " +
                    $"'{expectedAxisFrom}', which the manifest does not " +
                    "contain.");
            }
        }

        /// <summary>
        /// The detail atlas contract, ported from the Hero V2 clothing
        /// atlas with one inversion: the texture lives in the renderer's
        /// property block beside the palette tint, not in a material of
        /// its own, so the binding names no materials and its tint source
        /// is the renderer palette. The PNG is hashed here because a
        /// repainted atlas that skipped the generator would otherwise ship
        /// with a manifest still vouching for the old pixels.
        /// </summary>
        private static void ValidateTextureBindings(
            PedestrianDescriptor descriptor,
            CityPedestrianManifest manifest,
            HashSet<string> partNames)
        {
            CityPedestrianManifestTextureBinding[] bindings =
                manifest.texture_bindings ??
                Array.Empty<CityPedestrianManifestTextureBinding>();
            if (!descriptor.HasDetailAtlas)
            {
                if (bindings.Length != 0)
                {
                    throw new InvalidOperationException(
                        $"'{descriptor.DisplayName}' declares no detail " +
                        "atlas but its manifest binds a texture.");
                }

                for (int index = 0; index < manifest.parts.Length; index++)
                {
                    if (!string.IsNullOrEmpty(
                            manifest.parts[index].atlas_region))
                    {
                        throw new InvalidOperationException(
                            $"'{descriptor.DisplayName}' part " +
                            $"'{manifest.parts[index].name}' names an " +
                            "atlas region without a declared atlas.");
                    }
                }

                return;
            }

            if (bindings.Length != 1 || bindings[0] == null)
            {
                throw new InvalidOperationException(
                    $"'{descriptor.DisplayName}' must declare exactly one " +
                    "texture_binding for its detail atlas.");
            }

            CityPedestrianManifestTextureBinding binding = bindings[0];
            if (!string.Equals(
                    binding.texture_asset,
                    descriptor.DetailAtlasPath,
                    StringComparison.Ordinal) ||
                binding.width_px != DetailAtlasWidth ||
                binding.height_px != DetailAtlasHeight ||
                binding.materials == null ||
                binding.materials.Length != 0 ||
                binding.shader_property != DetailAtlasShaderProperty ||
                binding.color_space != DetailAtlasColorSpace ||
                binding.filter_mode != DetailAtlasFilterMode ||
                binding.wrap_mode != DetailAtlasWrapMode ||
                binding.mipmaps ||
                binding.compression != DetailAtlasCompression ||
                binding.uv_channel != 0 ||
                binding.uv_origin != DetailAtlasUvOrigin ||
                binding.uv_safe_inset_px != DetailAtlasUvSafeInsetPixels ||
                binding.material_tint_hex != DetailAtlasTintHex ||
                binding.tint_source != DetailAtlasTintSource)
            {
                throw new InvalidOperationException(
                    $"'{descriptor.DisplayName}' detail atlas settings " +
                    "differ from the material-free, palette-tinted " +
                    "_BaseMap contract.");
            }

            if (string.IsNullOrEmpty(binding.sha256) ||
                binding.sha256.Length != Sha256HexLength ||
                !string.Equals(
                    ComputeFileSha256(descriptor.DetailAtlasPath),
                    binding.sha256,
                    StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException(
                    $"'{descriptor.DisplayName}' detail atlas on disk does " +
                    "not match the SHA-256 its manifest declares; rerun " +
                    "the generator rather than editing the PNG.");
            }

            CityPedestrianManifestTextureRegion[] regions =
                binding.regions ??
                Array.Empty<CityPedestrianManifestTextureRegion>();
            if (regions.Length == 0)
            {
                throw new InvalidOperationException(
                    $"'{descriptor.DisplayName}' detail atlas declares no " +
                    "regions.");
            }

            Dictionary<string, string> regionByRenderer =
                new Dictionary<string, string>(StringComparer.Ordinal);
            HashSet<string> regionNames =
                new HashSet<string>(StringComparer.Ordinal);
            for (int index = 0; index < regions.Length; index++)
            {
                CityPedestrianManifestTextureRegion region = regions[index];
                if (region == null ||
                    string.IsNullOrEmpty(region.name) ||
                    !regionNames.Add(region.name) ||
                    string.IsNullOrEmpty(region.renderer) ||
                    !partNames.Contains(region.renderer) ||
                    !regionByRenderer.TryAdd(region.renderer, region.name) ||
                    region.width_px <= 2 * DetailAtlasUvSafeInsetPixels ||
                    region.height_px <= 2 * DetailAtlasUvSafeInsetPixels ||
                    region.x_px < 0 ||
                    region.y_px < 0 ||
                    region.x_px + region.width_px > DetailAtlasWidth ||
                    region.y_px + region.height_px > DetailAtlasHeight)
                {
                    throw new InvalidOperationException(
                        $"'{descriptor.DisplayName}' detail atlas region " +
                        $"'{region?.name}' is malformed, duplicated, off " +
                        "the atlas or names a renderer the manifest does " +
                        "not contain.");
                }
            }

            for (int index = 0; index < manifest.parts.Length; index++)
            {
                CityPedestrianManifestPart part = manifest.parts[index];
                regionByRenderer.TryGetValue(
                    part.name,
                    out string expectedRegion);
                if (!string.Equals(
                        part.atlas_region ?? string.Empty,
                        expectedRegion ?? string.Empty,
                        StringComparison.Ordinal))
                {
                    throw new InvalidOperationException(
                        $"'{descriptor.DisplayName}' part '{part.name}' " +
                        $"records atlas region '{part.atlas_region}' but " +
                        $"the binding assigns it '{expectedRegion}'.");
                }
            }
        }

        private static string ComputeFileSha256(string path)
        {
            using System.Security.Cryptography.SHA256 sha =
                System.Security.Cryptography.SHA256.Create();
            using FileStream stream = File.OpenRead(path);
            return BitConverter
                .ToString(sha.ComputeHash(stream))
                .Replace("-", string.Empty)
                .ToLowerInvariant();
        }

        private static CityPedestrianAnimationManifest
            LoadAndValidateAnimationManifest()
        {
            TextAsset source = AssetDatabase.LoadAssetAtPath<TextAsset>(
                AnimationManifestPath);
            if (source == null)
            {
                throw new InvalidOperationException(
                    $"Could not import locomotion manifest " +
                    $"'{AnimationManifestPath}'.");
            }

            CityPedestrianAnimationManifest manifest = JsonUtility.FromJson<
                CityPedestrianAnimationManifest>(source.text);
            if (manifest == null ||
                manifest.clips == null ||
                manifest.fps != ExpectedAnimationFps ||
                manifest.bone_count != ExpectedBoneCount ||
                manifest.mesh_count != 0 ||
                manifest.clip_count != ExpectedLocomotionClipCount ||
                manifest.root_motion ||
                string.IsNullOrWhiteSpace(manifest.skeleton_source) ||
                string.IsNullOrWhiteSpace(manifest.generator_version) ||
                string.IsNullOrWhiteSpace(manifest.build_signature) ||
                manifest.build_signature.Length != 64)
            {
                throw new InvalidOperationException(
                    "City pedestrian locomotion manifest has an invalid " +
                    "skeleton, root-motion or source metadata contract.");
            }

            Dictionary<string, PedestrianDescriptor> clipOwners =
                new Dictionary<string, PedestrianDescriptor>(
                    StringComparer.Ordinal);
            for (int index = 0; index < Descriptors.Length; index++)
            {
                PedestrianDescriptor descriptor = Descriptors[index];
                clipOwners.Add(descriptor.IdleClipName, descriptor);
                clipOwners.Add(descriptor.WalkClipName, descriptor);
                if (descriptor.RidesBus)
                {
                    clipOwners.Add(descriptor.SitClipName, descriptor);
                }

                if (descriptor.HasAction)
                {
                    clipOwners.Add(descriptor.ActionClipName, descriptor);
                }

                if (descriptor.HasDismount)
                {
                    clipOwners.Add(descriptor.DismountClipName, descriptor);
                }

                if (descriptor.HasAmbientGait)
                {
                    clipOwners.Add(descriptor.AmbientIdleClipName, descriptor);
                    clipOwners.Add(descriptor.AmbientWalkClipName, descriptor);
                }
            }

            HashSet<string> names =
                new HashSet<string>(StringComparer.Ordinal);
            for (int index = 0; index < manifest.clips.Length; index++)
            {
                CityPedestrianAnimationManifestClip clip =
                    manifest.clips[index];
                if (clip == null ||
                    string.IsNullOrWhiteSpace(clip.name) ||
                    !names.Add(clip.name) ||
                    !clipOwners.TryGetValue(
                        clip.name,
                        out PedestrianDescriptor owner) ||
                    !string.Equals(
                        clip.archetype,
                        owner.DesignId,
                        StringComparison.Ordinal) ||
                    // Almost every clip in this library loops, and asserting
                    // it is what catches a clip that drifts off its own
                    // first frame. The Ferryman's two transitions are the
                    // ones that MUST NOT: one starts on a car bonnet and
                    // ends standing on the lot, the other starts at his own
                    // door and ends behind the wheel, so both the loop flag
                    // and the seam error are meaningless for them. What holds
                    // it together instead is that it opens and closes on
                    // the base poses of the clips either side, which the
                    // art generator states by reusing those pose functions
                    // rather than re-typing numbers.
                    clip.loop == clip.one_shot ||
                    !clip.in_place ||
                    clip.keyed_bone_count != ExpectedBoneCount ||
                    string.IsNullOrWhiteSpace(clip.authored_posture) ||
                    string.IsNullOrWhiteSpace(clip.gait) ||
                    (!clip.one_shot &&
                     Mathf.Abs(clip.loop_max_error) > 0.0001f) ||
                    clip.frame_start != 0 ||
                    clip.frame_end != Mathf.RoundToInt(
                        clip.duration_seconds * ExpectedAnimationFps) ||
                    clip.root_translation_range_m == null ||
                    clip.root_translation_range_m.Length != 3 ||
                    clip.root_translation_range_m.Any(component =>
                        Mathf.Abs(component) > 0.0001f) ||
                    (owner.IsWheelchair &&
                     (clip.wheel_ground_min_m < -0.002f ||
                      clip.wheel_ground_max_contact_gap_m < 0f ||
                      clip.wheel_ground_max_contact_gap_m > 0.002f ||
                      clip.footrest_min_clearance_m < 0.03f ||
                      clip.rim_hand_max_distance_m < 0f ||
                      clip.rim_hand_max_distance_m > 0.10f)))
                {
                    throw new InvalidOperationException(
                        $"Locomotion manifest clip {index} violates the " +
                        "approved looping, in-place Generic contract.");
                }

                float expectedDuration;
                if (string.Equals(
                        clip.name,
                        owner.IdleClipName,
                        StringComparison.Ordinal))
                {
                    expectedDuration = owner.IdleDuration;
                }
                else if (owner.RidesBus &&
                         string.Equals(
                             clip.name,
                             owner.SitClipName,
                             StringComparison.Ordinal))
                {
                    expectedDuration = owner.SitDuration;
                }
                else if (owner.HasAction &&
                         string.Equals(
                             clip.name,
                             owner.ActionClipName,
                             StringComparison.Ordinal))
                {
                    expectedDuration = owner.ActionDuration;
                }
                else if (owner.HasDismount &&
                         string.Equals(
                             clip.name,
                             owner.DismountClipName,
                             StringComparison.Ordinal))
                {
                    expectedDuration = owner.DismountDuration;
                }
                else if (owner.HasAmbientGait &&
                         string.Equals(
                             clip.name,
                             owner.AmbientIdleClipName,
                             StringComparison.Ordinal))
                {
                    expectedDuration = owner.AmbientIdleDuration;
                }
                else if (owner.HasAmbientGait &&
                         string.Equals(
                             clip.name,
                             owner.AmbientWalkClipName,
                             StringComparison.Ordinal))
                {
                    expectedDuration = owner.AmbientWalkDuration;
                }
                else
                {
                    expectedDuration = owner.WalkDuration;
                }
                if (Mathf.Abs(
                        clip.duration_seconds - expectedDuration) > 0.0001f)
                {
                    throw new InvalidOperationException(
                        $"Locomotion manifest clip '{clip.name}' has an " +
                        "unexpected duration.");
                }
            }

            if (names.Count != clipOwners.Count ||
                clipOwners.Keys.Any(name => !names.Contains(name)))
            {
                throw new InvalidOperationException(
                    "Locomotion manifest must contain exactly the approved " +
                    "production and staged archetype clips.");
            }

            ModelImporter importer =
                AssetImporter.GetAtPath(AnimationPath) as ModelImporter;
            Avatar playerAvatar = FindModelAvatar();
            if (importer == null ||
                !importer.importAnimation ||
                importer.animationType != ModelImporterAnimationType.Generic ||
                importer.avatarSetup !=
                    ModelImporterAvatarSetup.CopyFromOther ||
                playerAvatar == null ||
                importer.sourceAvatar != playerAvatar)
            {
                throw new InvalidOperationException(
                    "Locomotion FBX must import animation as Generic and " +
                    "copy the production Hero/NPC V2 Avatar.");
            }

            AnimationClip[] importedClips = AssetDatabase
                .LoadAllAssetsAtPath(AnimationPath)
                .OfType<AnimationClip>()
                .Where(clip => !clip.name.StartsWith(
                    "__preview__",
                    StringComparison.Ordinal))
                .ToArray();
            if (importedClips.Length != clipOwners.Count)
            {
                throw new InvalidOperationException(
                    $"Unity imported {importedClips.Length} pedestrian " +
                    $"locomotion clips; expected {clipOwners.Count}.");
            }

            GameObject animationAsset =
                AssetDatabase.LoadAssetAtPath<GameObject>(AnimationPath);
            if (animationAsset == null ||
                animationAsset.GetComponentsInChildren<Renderer>(true).Length !=
                    0)
            {
                throw new InvalidOperationException(
                    "Pedestrian locomotion FBX must remain animation-only " +
                    "and contain no renderable meshes.");
            }

            Dictionary<string, Transform> animationTransforms =
                IndexUniqueTransforms(animationAsset, "locomotion");
            Transform animationBoneRoot = RequireTransform(
                animationTransforms,
                "root",
                "locomotion");
            if (animationBoneRoot
                    .GetComponentsInChildren<Transform>(true).Length !=
                ExpectedBoneCount)
            {
                throw new InvalidOperationException(
                    "Pedestrian locomotion FBX has added or missing Generic " +
                    "bones.");
            }

            return manifest;
        }

        private static void ValidateImportedModel(
            PedestrianDescriptor descriptor,
            CityPedestrianManifest manifest)
        {
            GameObject model =
                AssetDatabase.LoadAssetAtPath<GameObject>(
                    descriptor.ModelPath);
            GameObject playerModel =
                AssetDatabase.LoadAssetAtPath<GameObject>(PlayerModelPath);
            if (model == null || playerModel == null)
            {
                throw new InvalidOperationException(
                    "Pedestrian or Hero/NPC V2 source model failed to " +
                    "import.");
            }

            Dictionary<string, Transform> pedestrianTransforms =
                IndexUniqueTransforms(model, "pedestrian");
            Dictionary<string, Transform> playerTransforms =
                IndexUniqueTransforms(playerModel, "player");
            for (int index = 0; index < manifest.bones.Length; index++)
            {
                CityPedestrianManifestBone source = manifest.bones[index];
                Transform pedestrian = RequireTransform(
                    pedestrianTransforms,
                    source.name,
                    "pedestrian");
                Transform player = RequireTransform(
                    playerTransforms,
                    source.name,
                    "player");
                string expectedParent = string.IsNullOrEmpty(source.parent)
                    ? "RIG_Player"
                    : source.parent;
                if (pedestrian.parent == null ||
                    !string.Equals(
                        pedestrian.parent.name,
                        expectedParent,
                        StringComparison.Ordinal) ||
                    player.parent == null ||
                    !string.Equals(
                        player.parent.name,
                        expectedParent,
                        StringComparison.Ordinal))
                {
                    throw new InvalidOperationException(
                        $"Bone '{source.name}' lost the exact Hero/NPC V2 " +
                        "parent " +
                        $"'{expectedParent}'.");
                }

                if (Vector3.Distance(
                        pedestrian.localPosition,
                        player.localPosition) > TransformPositionTolerance ||
                    Quaternion.Angle(
                        pedestrian.localRotation,
                        player.localRotation) > TransformAngleTolerance ||
                    Vector3.Distance(
                        pedestrian.localScale,
                        player.localScale) > TransformPositionTolerance)
                {
                    throw new InvalidOperationException(
                        $"Bone '{source.name}' rest transform differs from " +
                        "PlayerCharacter3DV2.");
                }
            }

            Transform pedestrianBoneRoot = RequireTransform(
                pedestrianTransforms,
                "root",
                "pedestrian");
            if (pedestrianBoneRoot
                    .GetComponentsInChildren<Transform>(true).Length !=
                manifest.bones.Length)
            {
                throw new InvalidOperationException(
                    "Pedestrian armature has added or missing Generic bones.");
            }

            Renderer[] renderers =
                model.GetComponentsInChildren<Renderer>(true);
            if (renderers.Length != manifest.mesh_count)
            {
                throw new InvalidOperationException(
                    $"Unity imported {renderers.Length} pedestrian meshes; " +
                    $"manifest declares {manifest.mesh_count}.");
            }

            int triangles = CountTriangles(renderers);
            if (triangles != manifest.triangle_count)
            {
                throw new InvalidOperationException(
                    $"Unity imported {triangles} pedestrian triangles; " +
                    $"manifest declares {manifest.triangle_count}.");
            }

            UnityEngine.Object[] sourceAssets =
                AssetDatabase.LoadAllAssetsAtPath(descriptor.ModelPath);
            if (sourceAssets.Any(asset =>
                    asset is AnimationClip clip &&
                    !clip.name.StartsWith(
                        "__preview__",
                        StringComparison.Ordinal)))
            {
                throw new InvalidOperationException(
                    "Pedestrian FBX unexpectedly imported its own animation.");
            }

            Avatar playerAvatar = FindModelAvatar();
            ModelImporter modelImporter =
                AssetImporter.GetAtPath(descriptor.ModelPath) as ModelImporter;
            if (playerAvatar == null ||
                !playerAvatar.isValid ||
                modelImporter == null ||
                modelImporter.avatarSetup !=
                    ModelImporterAvatarSetup.CopyFromOther ||
                modelImporter.sourceAvatar != playerAvatar)
            {
                throw new InvalidOperationException(
                    "Pedestrian FBX must copy the valid production " +
                    "Hero/NPC V2 Generic Avatar.");
            }
        }

        /// <summary>
        /// The clips a design's own model manifest must declare, in the
        /// order the art generator writes them: idle, walk, then the
        /// optional street gait, the optional seated ride and the optional
        /// authored beat.
        /// </summary>
        private static string[] ExpectedSharedClips(
            PedestrianDescriptor descriptor)
        {
            var clips = new List<string>(6)
            {
                descriptor.IdleClipName,
                descriptor.WalkClipName
            };
            if (descriptor.HasAmbientGait)
            {
                clips.Add(descriptor.AmbientIdleClipName);
                clips.Add(descriptor.AmbientWalkClipName);
            }

            if (descriptor.RidesBus)
            {
                clips.Add(descriptor.SitClipName);
            }

            if (descriptor.HasAction)
            {
                clips.Add(descriptor.ActionClipName);
            }

            if (descriptor.HasDismount)
            {
                clips.Add(descriptor.DismountClipName);
            }

            return clips.ToArray();
        }

        private static void BuildPrefab(
            PedestrianDescriptor descriptor,
            GameObject modelAsset,
            Material sharedMaterial,
            AnimationClip idle,
            AnimationClip walk,
            AnimationClip sit,
            AnimationClip action,
            AnimationClip ambientIdle,
            AnimationClip ambientWalk,
            CityPedestrianManifest manifest,
            CityPedestrianAnimationManifest animationManifest)
        {
            GameObject prefabRoot = new GameObject(descriptor.PrefabRootName);
            try
            {
                GameObject model =
                    PrefabUtility.InstantiatePrefab(modelAsset) as GameObject;
                if (model == null)
                {
                    throw new InvalidOperationException(
                        "Could not instantiate imported pedestrian model.");
                }

                model.name = "Model";
                model.transform.SetParent(prefabRoot.transform, false);
                model.transform.localPosition = Vector3.zero;
                model.transform.localRotation =
                    Quaternion.Euler(0f, 180f, 0f);
                model.transform.localScale = Vector3.one;

                Dictionary<string, Renderer> renderersByName =
                    IndexUniqueRenderers(model);
                Dictionary<string, Transform> transformsByName =
                    IndexUniqueTransforms(model, "pedestrian prefab");
                if (renderersByName.Count != manifest.mesh_count)
                {
                    throw new InvalidOperationException(
                        "Imported pedestrian renderer count differs from " +
                        "the manifest.");
                }

                List<Renderer> rendererList =
                    new List<Renderer>(manifest.parts.Length);
                List<CityPedestrianRendererBinding> bindings =
                    new List<CityPedestrianRendererBinding>(
                        manifest.parts.Length);
                for (int index = 0; index < manifest.parts.Length; index++)
                {
                    CityPedestrianManifestPart source = manifest.parts[index];
                    if (!renderersByName.TryGetValue(
                            source.name,
                            out Renderer renderer))
                    {
                        throw new InvalidOperationException(
                            $"Imported pedestrian is missing renderer " +
                            $"'{source.name}'.");
                    }

                    renderer.sharedMaterials = new[] { sharedMaterial };
                    renderer.shadowCastingMode = ShadowCastingMode.On;
                    renderer.receiveShadows = true;
                    renderer.motionVectorGenerationMode =
                        MotionVectorGenerationMode.Object;
                    if (renderer is SkinnedMeshRenderer skinned)
                    {
                        skinned.updateWhenOffscreen = true;
                    }

                    Color baseColor = ParseColor(source.base_color);
                    bindings.Add(
                        new CityPedestrianRendererBinding(
                            source.name,
                            source.role,
                            source.palette_name,
                            renderer,
                            baseColor,
                            BuildPaletteVariant(
                                source.palette_name,
                                baseColor,
                                1),
                            BuildPaletteVariant(
                                source.palette_name,
                                baseColor,
                                2),
                            BuildPaletteVariant(
                                source.palette_name,
                                baseColor,
                                3),
                            usesDetailAtlas: !string.IsNullOrEmpty(
                                source.atlas_region)));
                    rendererList.Add(renderer);
                }

                Animator animator =
                    model.GetComponentInChildren<Animator>(true);
                if (animator == null)
                {
                    animator = model.AddComponent<Animator>();
                }

                if (animator.avatar == null)
                {
                    animator.avatar = FindModelAvatar();
                }

                if (animator.avatar == null || !animator.avatar.isValid)
                {
                    throw new InvalidOperationException(
                        "Pedestrian model has no valid Generic Avatar.");
                }

                animator.runtimeAnimatorController = null;
                animator.applyRootMotion = false;
                animator.cullingMode =
                    AnimatorCullingMode.CullUpdateTransforms;

                Transform head = RequireTransform(
                    transformsByName,
                    "head",
                    "pedestrian prefab");
                // Seating aligns this one bone to the cushion. Every design
                // shares the hero's 0.835 m V2 rest pelvis, so one seat
                // rule serves four different proportions.
                Transform pelvis = RequireTransform(
                    transformsByName,
                    "pelvis",
                    "pedestrian prefab");
                Transform leftFoot = RequireTransform(
                    transformsByName,
                    "foot.L",
                    "pedestrian prefab");
                Transform rightFoot = RequireTransform(
                    transformsByName,
                    "foot.R",
                    "pedestrian prefab");
                Renderer[] renderers = rendererList.ToArray();
                if (descriptor.IsWheelchair)
                {
                    ValidateWheelchairVisualScale(
                        prefabRoot,
                        manifest.wheel_radius_m);
                }

                Bounds localBounds = CalculateLocalBounds(
                    prefabRoot.transform,
                    renderers);
                if (Mathf.Abs(localBounds.size.y - manifest.height_m) >
                        0.035f ||
                    Mathf.Abs(localBounds.min.y) > 0.025f ||
                    CountTriangles(renderers) != manifest.triangle_count)
                {
                    Renderer largestRenderer = renderers
                        .OrderByDescending(candidate =>
                            candidate.bounds.size.sqrMagnitude)
                        .First();
                    throw new InvalidOperationException(
                        $"Imported pedestrian '{descriptor.DesignId}' " +
                        "geometry lost source bounds or triangle count: " +
                        $"bounds min={localBounds.min}, " +
                        $"size={localBounds.size}, triangles=" +
                        $"{CountTriangles(renderers)}; expected height=" +
                        $"{manifest.height_m} and triangles=" +
                        $"{manifest.triangle_count}. Largest renderer=" +
                        $"'{largestRenderer.name}', localPosition=" +
                        $"{largestRenderer.transform.localPosition}, " +
                        $"localScale={largestRenderer.transform.localScale}, " +
                        $"parent='{largestRenderer.transform.parent?.name}'.");
                }

                Light headLamp = descriptor.CarriesHeadLamp
                    ? CreateHeadLamp(head)
                    : null;
                CityPedestrianAssetRegistry registry =
                    prefabRoot.AddComponent<CityPedestrianAssetRegistry>();
                registry.Configure(
                    animator,
                    model.transform,
                    renderers,
                    bindings.ToArray(),
                    head,
                    leftFoot,
                    rightFoot,
                    idle,
                    walk,
                    localBounds,
                    manifest.triangle_count,
                    manifest.generator_version,
                    manifest.design_id,
                    manifest.build_signature,
                    headLamp,
                    descriptor.PreservesAirborneMotion,
                    pelvis,
                    sit,
                    action,
                    ambientIdle,
                    ambientWalk);

                if (descriptor.HasDetailAtlas)
                {
                    // The atlas rides the same property block as the
                    // palette tint, so Player3DLit stays the one material
                    // every pedestrian renderer is validated to share.
                    Texture2D detailAtlas =
                        AssetDatabase.LoadAssetAtPath<Texture2D>(
                            descriptor.DetailAtlasPath);
                    if (detailAtlas == null ||
                        detailAtlas.width != DetailAtlasWidth ||
                        detailAtlas.height != DetailAtlasHeight)
                    {
                        throw new InvalidOperationException(
                            $"'{descriptor.DisplayName}' detail atlas must " +
                            $"import as {DetailAtlasWidth}x" +
                            $"{DetailAtlasHeight} at " +
                            $"'{descriptor.DetailAtlasPath}'; got " +
                            $"{detailAtlas?.width ?? 0}x" +
                            $"{detailAtlas?.height ?? 0}.");
                    }

                    registry.ConfigureDetailAtlas(detailAtlas);
                    ValidateRegionUvs(
                        manifest.texture_bindings[0],
                        renderersByName);
                }

                if (descriptor.CarriesBoilingKettle)
                {
                    BuildKettleRig(
                        prefabRoot,
                        registry,
                        head,
                        renderersByName);
                }

                if (descriptor.CarriesFishingRig)
                {
                    // Measured once, here, in the bind pose, off the
                    // imported meshes themselves. Both points are drawn
                    // by rigidly skinned vertices and neither has a
                    // Transform, so reconstructing them at runtime would
                    // mean re-deriving the FBX axis conversion and this
                    // prefab's own 180 degree model flip in gameplay
                    // code — twice, and again whenever the art moves.
                    Transform rightHand = RequireTransform(
                        transformsByName,
                        RightHandBoneName,
                        "fisherman prefab");
                    Renderer emberRenderer = RequireRenderer(
                        renderersByName,
                        SeacoastFishermanRigAnchors.PipeEmberRendererName);
                    Renderer rodTipRenderer = RequireRenderer(
                        renderersByName,
                        SeacoastFishermanRigAnchors.RodTipRendererName);
                    Transform emberAnchor = CreateBoneAnchor(
                        head,
                        SeacoastFishermanRigAnchors.PipeEmberAnchorName,
                        BindPoseCenter(emberRenderer));
                    Transform rodTipAnchor = CreateBoneAnchor(
                        rightHand,
                        SeacoastFishermanRigAnchors.RodTipAnchorName,
                        BindPoseFarthestPoint(
                            rodTipRenderer,
                            rightHand.position));
                    prefabRoot
                        .AddComponent<SeacoastFishermanRigAnchors>()
                        .Configure(
                            registry,
                            emberAnchor,
                            rodTipAnchor,
                            emberRenderer);
                }

                if (descriptor.CarriesCoinRig)
                {
                    // Measured once, here, in the bind pose, off the
                    // imported meshes, for the reason the fisherman's
                    // anchors exist: both points are drawn by rigidly
                    // skinned vertices, so neither has a Transform, and
                    // reconstructing them at runtime would mean
                    // re-deriving the FBX axis conversion and this
                    // prefab's own 180 degree model flip in gameplay
                    // code.
                    Transform leftHand = RequireTransform(
                        transformsByName,
                        LeftHandBoneName,
                        "Ferryman prefab");
                    Transform pelvisBone = RequireTransform(
                        transformsByName,
                        PelvisBoneName,
                        "Ferryman prefab");
                    Renderer palmRenderer = RequireRenderer(
                        renderersByName,
                        LastRouteFerrymanRigAnchors
                            .CoinRestRendererName);
                    Renderer hemRenderer = RequireRenderer(
                        renderersByName,
                        LastRouteFerrymanRigAnchors
                            .CoatHemRendererName);
                    Transform coinAnchor = CreateBoneAnchor(
                        leftHand,
                        LastRouteFerrymanRigAnchors.CoinRestAnchorName,
                        BindPoseCenter(palmRenderer));
                    Transform hemAnchor = CreateBoneAnchor(
                        pelvisBone,
                        LastRouteFerrymanRigAnchors.CoatHemAnchorName,
                        BindPoseTopCenter(hemRenderer));

                    // The one number the runtime cannot measure for
                    // itself. Blender walks the posed meshes and reports
                    // how far the pelvis rides above the lowest of them;
                    // at runtime the equivalent would need skinned bounds,
                    // which Unity does not recompute for a manually driven
                    // graph. So it is carried, and the build fails here if
                    // the clip stopped reporting it.
                    CityPedestrianAnimationManifestClip perchClip =
                        animationManifest.clips.FirstOrDefault(candidate =>
                            candidate != null &&
                            string.Equals(
                                candidate.name,
                                descriptor.IdleClipName,
                                StringComparison.Ordinal));
                    if (perchClip == null ||
                        perchClip.seated_drop_m <= 0f)
                    {
                        throw new InvalidOperationException(
                            $"'{descriptor.DisplayName}' needs a measured " +
                            "seated_drop_m on its perched idle clip.");
                    }

                    // And his fifth clip. The shared pedestrian registry
                    // has four slots and this design fills all four, so the
                    // drop off the bonnet rides the component that is
                    // already his alone rather than widening a type every
                    // other pedestrian shares.
                    AnimationClip dismountClip = descriptor.HasDismount
                        ? LoadLocomotionClip(
                            descriptor.DismountClipName,
                            descriptor.DismountDuration,
                            animationManifest)
                        : null;
                    if (dismountClip == null)
                    {
                        throw new InvalidOperationException(
                            $"'{descriptor.DisplayName}' needs his drop off " +
                            "the bonnet; without it he cannot leave it.");
                    }

                    // And his arms, for the wheel. The shared registry
                    // serializes no arm bones and nothing is found by name
                    // at runtime, so the bindings are made here, where name
                    // lookup is the contract. The grip sockets are the
                    // skeleton's own SOCKET_Grip bones - the same palms the
                    // bus driver's hand IK closes on the bus's rim.
                    var arms = new LastRouteFerrymanArmBindings(
                        RequireTransform(
                            transformsByName,
                            "upper_arm.L",
                            "Ferryman prefab"),
                        RequireTransform(
                            transformsByName,
                            "forearm.L",
                            "Ferryman prefab"),
                        leftHand,
                        RequireTransform(
                            transformsByName,
                            "SOCKET_Grip.L",
                            "Ferryman prefab"),
                        RequireTransform(
                            transformsByName,
                            "upper_arm.R",
                            "Ferryman prefab"),
                        RequireTransform(
                            transformsByName,
                            "forearm.R",
                            "Ferryman prefab"),
                        RequireTransform(
                            transformsByName,
                            RightHandBoneName,
                            "Ferryman prefab"),
                        RequireTransform(
                            transformsByName,
                            "SOCKET_Grip.R",
                            "Ferryman prefab"));
                    prefabRoot
                        .AddComponent<LastRouteFerrymanRigAnchors>()
                        .Configure(
                            registry,
                            coinAnchor,
                            hemAnchor,
                            hemRenderer,
                            BindPoseLateralSize(hemRenderer),
                            perchClip.seated_drop_m,
                            dismountClip,
                            arms);
                }

                if (descriptor.IsWheelchair)
                {
                    // The shared animation FBX deliberately remains an exact
                    // 31-bone library. These named mechanism pivots are a
                    // staged contract for future procedural wheel/chair
                    // motion, not auxiliary animation channels.
                    CityWheelchairNpcAssetRegistry wheelchairRegistry =
                        prefabRoot.AddComponent<
                            CityWheelchairNpcAssetRegistry>();
                    wheelchairRegistry.Configure(
                        registry,
                        RequireTransform(
                            transformsByName,
                            LeftWheelPivotName,
                            "wheelchair prefab"),
                        RequireTransform(
                            transformsByName,
                            RightWheelPivotName,
                            "wheelchair prefab"),
                        RequireTransform(
                            transformsByName,
                            LeftCasterPivotName,
                            "wheelchair prefab"),
                        RequireTransform(
                            transformsByName,
                            RightCasterPivotName,
                            "wheelchair prefab"),
                        RequireTransform(
                            transformsByName,
                            BellowsPivotName,
                            "wheelchair prefab"),
                        RequireTransform(
                            transformsByName,
                            PipeBankPivotName,
                            "wheelchair prefab"));
                }

                GameObject saved = PrefabUtility.SaveAsPrefabAsset(
                    prefabRoot,
                    descriptor.PrefabPath,
                    out bool success);
                if (!success || saved == null)
                {
                    throw new InvalidOperationException(
                        $"Could not save City pedestrian prefab at " +
                        $"'{descriptor.PrefabPath}'.");
                }
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(prefabRoot);
            }
        }

        private static Renderer RequireRenderer(
            Dictionary<string, Renderer> renderersByName,
            string rendererName)
        {
            if (!renderersByName.TryGetValue(
                    rendererName,
                    out Renderer renderer) ||
                renderer == null)
            {
                throw new InvalidOperationException(
                    $"Imported pedestrian is missing renderer " +
                    $"'{rendererName}'.");
            }

            return renderer;
        }

        /// <summary>
        /// The lid pivot, the spout anchor and the axes the boil effect
        /// turns the lid about, measured once here in the bind pose off
        /// the imported meshes. Every kettle part is rigidly skinned to
        /// the head and the rig is locked at thirty-one bones, so the lid
        /// gets no bone of its own: an empty with the head's exact frame
        /// is put under the head and the lid's and knob's one bone
        /// reference is repointed at it. At rest the mesh is byte for
        /// byte what it was; the runtime rotates the pivot about the
        /// lid's centre, which is why that centre is stored head-local.
        ///
        /// The axes are never taken from world forward or right — the
        /// prefab's Model child is turned 180 degrees about Y — but from
        /// the kettle itself: up the kettle from body to lid, and across
        /// and along the spout.
        /// </summary>
        private static void BuildKettleRig(
            GameObject prefabRoot,
            CityPedestrianAssetRegistry registry,
            Transform head,
            Dictionary<string, Renderer> renderersByName)
        {
            Renderer lidRenderer = RequireRenderer(
                renderersByName,
                CityKettleHatRigAnchors.LidRendererName);
            Renderer knobRenderer = RequireRenderer(
                renderersByName,
                CityKettleHatRigAnchors.KnobRendererName);
            Renderer bodyRenderer = RequireRenderer(
                renderersByName,
                CityKettleHatRigAnchors.KettleBodyRendererName);
            Renderer spoutRenderer = RequireRenderer(
                renderersByName,
                CityKettleHatRigAnchors.SpoutRendererName);
            Renderer spoutTipRenderer = RequireRenderer(
                renderersByName,
                CityKettleHatRigAnchors.SpoutTipRendererName);

            Transform lidPivot = CreateBonePivot(
                head,
                CityKettleHatRigAnchors.LidAnchorName);
            RepointSkinnedBone(lidRenderer, head, lidPivot);
            RepointSkinnedBone(knobRenderer, head, lidPivot);

            Vector3 lidCentreWorld = BindPoseCenter(lidRenderer);
            Vector3 bodyCentreWorld = BindPoseCenter(bodyRenderer);
            Vector3 spoutBaseWorld = BindPoseCenter(spoutRenderer);
            Vector3 spoutTipWorld = BindPoseFarthestPoint(
                spoutTipRenderer,
                head.position);

            Vector3 kettleAxisWorld = lidCentreWorld - bodyCentreWorld;
            Vector3 spoutDirectionWorld = spoutTipWorld - spoutBaseWorld;
            if (kettleAxisWorld.sqrMagnitude <
                    KettleAxisTolerance * KettleAxisTolerance ||
                spoutDirectionWorld.sqrMagnitude <
                    KettleAxisTolerance * KettleAxisTolerance)
            {
                throw new InvalidOperationException(
                    "The kettle's lid sits on its body or its spout tip " +
                    "sits on its spout; neither axis can be measured.");
            }

            kettleAxisWorld.Normalize();
            spoutDirectionWorld.Normalize();
            Vector3 tiltAxisAWorld =
                Vector3.Cross(kettleAxisWorld, spoutDirectionWorld);
            if (tiltAxisAWorld.sqrMagnitude <
                KettleAxisTolerance * KettleAxisTolerance)
            {
                throw new InvalidOperationException(
                    "The kettle's spout runs along its own axis; the lid " +
                    "has no tilt axis across it.");
            }

            tiltAxisAWorld.Normalize();
            Vector3 tiltAxisBWorld =
                Vector3.Cross(kettleAxisWorld, tiltAxisAWorld).normalized;

            Transform spoutAnchor = CreateBoneAnchor(
                head,
                CityKettleHatRigAnchors.SpoutAnchorName,
                spoutTipWorld);
            spoutAnchor.rotation = Quaternion.LookRotation(
                spoutDirectionWorld,
                Vector3.up);

            prefabRoot
                .AddComponent<CityKettleHatRigAnchors>()
                .Configure(
                    registry,
                    lidPivot,
                    spoutAnchor,
                    lidRenderer,
                    knobRenderer,
                    head.InverseTransformPoint(lidCentreWorld),
                    head.InverseTransformDirection(kettleAxisWorld),
                    head.InverseTransformDirection(tiltAxisAWorld),
                    head.InverseTransformDirection(tiltAxisBWorld),
                    Vector3.Distance(spoutTipWorld, head.position));
        }

        /// <summary>
        /// An empty under a bone with the bone's exact frame, for a
        /// skinned part to be re-bound to. The explicit zero, identity and
        /// one are the whole point: the part's bind pose was computed
        /// against the bone, and the pivot only stands in for it while
        /// its local transform is exactly nothing.
        /// </summary>
        private static Transform CreateBonePivot(
            Transform bone,
            string pivotName)
        {
            var pivot = new GameObject(pivotName).transform;
            pivot.SetParent(bone, false);
            pivot.localPosition = Vector3.zero;
            pivot.localRotation = Quaternion.identity;
            pivot.localScale = Vector3.one;
            return pivot;
        }

        /// <summary>
        /// Replaces the one entry in a skinned renderer's bone array that
        /// references <paramref name="bone"/> with <paramref name="pivot"/>,
        /// leaving the bind poses untouched. Blender writes a cluster for
        /// every bone whether or not it deforms the part, so the bone is
        /// found by reference rather than assumed at any index, and the
        /// part is required to reference it exactly once.
        /// </summary>
        private static void RepointSkinnedBone(
            Renderer renderer,
            Transform bone,
            Transform pivot)
        {
            if (!(renderer is SkinnedMeshRenderer skinned))
            {
                throw new InvalidOperationException(
                    $"'{renderer.name}' must be a SkinnedMeshRenderer to " +
                    "be re-bound to a pivot.");
            }

            Transform[] bones = skinned.bones;
            int hitIndex = -1;
            int hits = 0;
            for (int index = 0; index < bones.Length; index++)
            {
                if (bones[index] == bone)
                {
                    hitIndex = index;
                    hits++;
                }
            }

            if (hits != 1)
            {
                throw new InvalidOperationException(
                    $"'{renderer.name}' references bone '{bone.name}' " +
                    $"{hits} times; exactly one entry can be repointed.");
            }

            bones[hitIndex] = pivot;
            skinned.bones = bones;
        }

        /// <summary>
        /// Every atlas region's renderer must carry UV0 that stays inside
        /// its inset sub-rectangle. Ported from the Hero V2 clothing
        /// contract: a single vertex outside the cell samples a
        /// neighbour's pixels and reads as a smear on the wrong part.
        /// </summary>
        private static void ValidateRegionUvs(
            CityPedestrianManifestTextureBinding binding,
            IReadOnlyDictionary<string, Renderer> renderers)
        {
            for (int regionIndex = 0;
                 regionIndex < binding.regions.Length;
                 regionIndex++)
            {
                CityPedestrianManifestTextureRegion region =
                    binding.regions[regionIndex];
                if (!renderers.TryGetValue(
                        region.renderer,
                        out Renderer renderer))
                {
                    throw new InvalidOperationException(
                        $"Detail atlas region '{region.name}' references " +
                        $"missing renderer '{region.renderer}'.");
                }

                Mesh mesh = GetRendererMesh(renderer);
                if (mesh == null)
                {
                    throw new InvalidOperationException(
                        $"Textured renderer '{region.renderer}' has no mesh.");
                }

                Vector2[] uv = mesh.uv;
                if (uv == null ||
                    uv.Length != mesh.vertexCount ||
                    uv.Length == 0)
                {
                    throw new InvalidOperationException(
                        $"Textured renderer '{region.renderer}' must have " +
                        "UV0.");
                }

                Vector2 minimum = new Vector2(
                    (float)(region.x_px + binding.uv_safe_inset_px) /
                    binding.width_px,
                    (float)(region.y_px + binding.uv_safe_inset_px) /
                    binding.height_px);
                Vector2 maximum = new Vector2(
                    (float)(region.x_px + region.width_px -
                            binding.uv_safe_inset_px) /
                    binding.width_px,
                    (float)(region.y_px + region.height_px -
                            binding.uv_safe_inset_px) /
                    binding.height_px);
                Vector2 observedMinimum = uv[0];
                Vector2 observedMaximum = uv[0];
                for (int uvIndex = 0; uvIndex < uv.Length; uvIndex++)
                {
                    Vector2 point = uv[uvIndex];
                    if (point.x < minimum.x - UvTolerance ||
                        point.x > maximum.x + UvTolerance ||
                        point.y < minimum.y - UvTolerance ||
                        point.y > maximum.y + UvTolerance)
                    {
                        throw new InvalidOperationException(
                            $"Renderer '{region.renderer}' " +
                            $"UV0[{uvIndex}]={point} lies outside region " +
                            $"'{region.name}' {minimum}..{maximum}.");
                    }

                    observedMinimum = Vector2.Min(observedMinimum, point);
                    observedMaximum = Vector2.Max(observedMaximum, point);
                }

                if (observedMaximum.x - observedMinimum.x <= UvTolerance ||
                    observedMaximum.y - observedMinimum.y <= UvTolerance)
                {
                    throw new InvalidOperationException(
                        $"Renderer '{region.renderer}' has degenerate " +
                        "atlas UV0.");
                }
            }
        }

        /// <summary>
        /// Parents an empty anchor to a bone at a measured world point.
        /// The anchor then travels with that bone for free, which is the
        /// entire reason it exists.
        /// </summary>
        private static Transform CreateBoneAnchor(
            Transform bone,
            string anchorName,
            Vector3 worldPosition)
        {
            var anchor = new GameObject(anchorName).transform;
            anchor.SetParent(bone, false);
            anchor.position = worldPosition;
            anchor.localRotation = Quaternion.identity;
            anchor.localScale = Vector3.one;
            return anchor;
        }

        /// <summary>
        /// A rigidly skinned part's bind-pose centre. Every pedestrian
        /// part carries one vertex group at weight one, so the shared
        /// mesh sits in its own renderer's space exactly as authored.
        /// </summary>
        private static Vector3 BindPoseCenter(Renderer renderer)
        {
            Mesh mesh = RequireSharedMesh(renderer);
            return renderer.transform.TransformPoint(mesh.bounds.center);
        }

        /// <summary>
        /// The bind-pose vertex of a part that sits furthest from a
        /// reference point — how the far end of a tapering rod section
        /// is found without hard-coding its length.
        /// </summary>
        private static Vector3 BindPoseFarthestPoint(
            Renderer renderer,
            Vector3 fromWorldPosition)
        {
            Mesh mesh = RequireSharedMesh(renderer);
            Vector3[] vertices = mesh.vertices;
            if (vertices.Length == 0)
            {
                throw new InvalidOperationException(
                    $"Renderer '{renderer.name}' has no vertices.");
            }

            Vector3 farthest = renderer.transform.TransformPoint(
                vertices[0]);
            float best = (farthest - fromWorldPosition).sqrMagnitude;
            for (int index = 1; index < vertices.Length; index++)
            {
                Vector3 candidate = renderer.transform.TransformPoint(
                    vertices[index]);
                float distance =
                    (candidate - fromWorldPosition).sqrMagnitude;
                if (distance > best)
                {
                    best = distance;
                    farthest = candidate;
                }
            }

            return farthest;
        }

        /// <summary>
        /// The centre of a rigidly skinned part's TOP face, in world
        /// space. Computed from the vertices rather than from
        /// `mesh.bounds`, because a part's own space is whatever the
        /// FBX conversion left it in and 'up' there is not necessarily
        /// up here. The world Y of a vertex has no such ambiguity.
        /// </summary>
        private static Vector3 BindPoseTopCenter(Renderer renderer)
        {
            Mesh mesh = RequireSharedMesh(renderer);
            Vector3[] vertices = mesh.vertices;
            if (vertices.Length == 0)
            {
                throw new InvalidOperationException(
                    $"Renderer '{renderer.name}' has no vertices.");
            }

            var world = new Vector3[vertices.Length];
            float top = float.NegativeInfinity;
            for (int index = 0; index < vertices.Length; index++)
            {
                world[index] =
                    renderer.transform.TransformPoint(vertices[index]);
                top = Mathf.Max(top, world[index].y);
            }

            // Everything within a centimetre of the highest vertex is
            // the top face; its average is its centre.
            const float bandMeters = 0.01f;
            var sum = Vector3.zero;
            int count = 0;
            for (int index = 0; index < world.Length; index++)
            {
                if (world[index].y < top - bandMeters)
                {
                    continue;
                }

                sum += world[index];
                count++;
            }

            return sum / count;
        }

        /// <summary>
        /// How wide and how deep a part is, in metres, as (width,
        /// height). Width is the LARGER of the two horizontal extents,
        /// because a garment around a human waist is wider across than
        /// front to back and this must not depend on which way the
        /// bind pose happens to face.
        /// </summary>
        private static Vector2 BindPoseLateralSize(Renderer renderer)
        {
            Mesh mesh = RequireSharedMesh(renderer);
            Vector3[] vertices = mesh.vertices;
            if (vertices.Length == 0)
            {
                throw new InvalidOperationException(
                    $"Renderer '{renderer.name}' has no vertices.");
            }

            Vector3 min = renderer.transform.TransformPoint(vertices[0]);
            Vector3 max = min;
            for (int index = 1; index < vertices.Length; index++)
            {
                Vector3 point =
                    renderer.transform.TransformPoint(vertices[index]);
                min = Vector3.Min(min, point);
                max = Vector3.Max(max, point);
            }

            Vector3 size = max - min;
            return new Vector2(Mathf.Max(size.x, size.z), size.y);
        }

        private static Mesh RequireSharedMesh(Renderer renderer)
        {
            Mesh mesh = renderer is SkinnedMeshRenderer skinned
                ? skinned.sharedMesh
                : renderer.GetComponent<MeshFilter>()?.sharedMesh;
            if (mesh == null)
            {
                throw new InvalidOperationException(
                    $"Renderer '{renderer.name}' has no shared mesh.");
            }

            return mesh;
        }

        /// <summary>
        /// A fishing design owes both bind-pose anchors, on the right
        /// bones, and nobody else may carry the component: an anchor
        /// parented to the wrong bone still resolves at runtime and then
        /// leaves the ember hanging in the air beside his head.
        /// </summary>
        private static void ValidateFishingRigBindings(
            GameObject prefab,
            CityPedestrianAssetRegistry registry,
            PedestrianDescriptor descriptor)
        {
            SeacoastFishermanRigAnchors anchors =
                prefab.GetComponentInChildren<SeacoastFishermanRigAnchors>(true);
            if (!descriptor.CarriesFishingRig)
            {
                if (anchors != null)
                {
                    throw new InvalidOperationException(
                        $"'{descriptor.DisplayName}' declares no fishing " +
                        "rig but its prefab carries anchor metadata.");
                }

                return;
            }

            if (anchors == null ||
                anchors.PedestrianRegistry != registry ||
                anchors.PipeEmberAnchor == null ||
                anchors.RodTipAnchor == null ||
                anchors.PipeEmberRenderer == null)
            {
                throw new InvalidOperationException(
                    $"'{descriptor.DisplayName}' must bind its pipe and " +
                    "rod anchors and its ember renderer.");
            }

            if (anchors.PipeEmberAnchor.parent == null ||
                !string.Equals(
                    anchors.PipeEmberAnchor.parent.name,
                    "head",
                    StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    "The pipe ember anchor must ride the head bone.");
            }

            if (anchors.RodTipAnchor.parent == null ||
                !string.Equals(
                    anchors.RodTipAnchor.parent.name,
                    RightHandBoneName,
                    StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    "The rod tip anchor must ride the right hand bone.");
            }

            // The rod is a two-metre stick on a 1.75 m envelope, so an
            // anchor that landed on the grip instead of the point would
            // still look plausible in the inspector. This does not.
            float reach = Vector3.Distance(
                anchors.RodTipAnchor.position,
                anchors.RodTipAnchor.parent.position);
            if (reach < 1.4f)
            {
                throw new InvalidOperationException(
                    $"The rod tip anchor sits {reach:0.###} m from the " +
                    "hand; it must be at the point of the rod.");
            }
        }

        /// <summary>
        /// A kettle design owes its lid pivot and spout anchor on the
        /// head, with the lid and knob re-bound to the pivot and nobody
        /// else, and nobody else may carry the component. The pivot's
        /// rest is checked in world metres because the runtime effect
        /// assumes the pivot IS the head at rest: a pivot that saved a
        /// hair off the bone would draw the lid a hair off the kettle in
        /// every frame the effect is not running.
        /// </summary>
        private static void ValidateKettleRigBindings(
            GameObject prefab,
            CityPedestrianAssetRegistry registry,
            PedestrianDescriptor descriptor)
        {
            CityKettleHatRigAnchors anchors =
                prefab.GetComponentInChildren<CityKettleHatRigAnchors>(true);
            if (!descriptor.CarriesBoilingKettle)
            {
                if (anchors != null)
                {
                    throw new InvalidOperationException(
                        $"'{descriptor.DisplayName}' declares no boiling " +
                        "kettle but its prefab carries kettle anchor " +
                        "metadata.");
                }

                return;
            }

            if (anchors == null ||
                anchors.PedestrianRegistry != registry ||
                anchors.LidPivot == null ||
                anchors.SpoutAnchor == null ||
                anchors.LidRenderer == null ||
                anchors.KnobRenderer == null)
            {
                throw new InvalidOperationException(
                    $"'{descriptor.DisplayName}' must bind its lid pivot, " +
                    "spout anchor and lid and knob renderers.");
            }

            Transform head = registry.HeadAnchor;
            if (anchors.LidPivot.parent != head ||
                anchors.SpoutAnchor.parent != head ||
                !string.Equals(
                    head.name,
                    HeadBoneName,
                    StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    "The kettle's lid pivot and spout anchor must both " +
                    "ride the head bone.");
            }

            if (!string.Equals(
                    anchors.LidPivot.name,
                    CityKettleHatRigAnchors.LidAnchorName,
                    StringComparison.Ordinal) ||
                !string.Equals(
                    anchors.SpoutAnchor.name,
                    CityKettleHatRigAnchors.SpoutAnchorName,
                    StringComparison.Ordinal) ||
                !string.Equals(
                    anchors.LidRenderer.name,
                    CityKettleHatRigAnchors.LidRendererName,
                    StringComparison.Ordinal) ||
                !string.Equals(
                    anchors.KnobRenderer.name,
                    CityKettleHatRigAnchors.KnobRendererName,
                    StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    "The kettle's anchors and renderers are bound under " +
                    "the wrong names.");
            }

            if (Vector3.Distance(anchors.LidPivot.position, head.position) >
                    KettlePivotRestPositionTolerance ||
                Quaternion.Angle(anchors.LidPivot.rotation, head.rotation) >
                    KettlePivotRestAngleTolerance ||
                Vector3.Distance(anchors.LidPivot.localScale, Vector3.one) >
                    KettlePivotRestPositionTolerance)
            {
                throw new InvalidOperationException(
                    "The kettle's lid pivot must rest exactly on the head " +
                    "bone's frame.");
            }

            ValidatePivotSkinning(anchors.LidRenderer, head, anchors.LidPivot);
            ValidatePivotSkinning(anchors.KnobRenderer, head, anchors.LidPivot);
            for (int index = 0; index < registry.Renderers.Count; index++)
            {
                Renderer renderer = registry.Renderers[index];
                if (renderer == anchors.LidRenderer ||
                    renderer == anchors.KnobRenderer ||
                    !(renderer is SkinnedMeshRenderer skinned))
                {
                    continue;
                }

                if (skinned.bones.Contains(anchors.LidPivot))
                {
                    throw new InvalidOperationException(
                        $"'{renderer.name}' is skinned to the lid pivot; " +
                        "only the lid and its knob may move with it.");
                }
            }

            float reach = Vector3.Distance(
                anchors.SpoutAnchor.position,
                head.position);
            if (reach < KettleSpoutReachMinimumMetres ||
                reach > KettleSpoutReachMaximumMetres ||
                Mathf.Abs(reach - anchors.SpoutReachMetres) >
                    KettlePivotRestPositionTolerance)
            {
                throw new InvalidOperationException(
                    $"The spout anchor sits {reach:0.###} m from the head " +
                    $"(recorded {anchors.SpoutReachMetres:0.###}); it must " +
                    "be at the mouth of the spout, between " +
                    $"{KettleSpoutReachMinimumMetres} and " +
                    $"{KettleSpoutReachMaximumMetres} m.");
            }

            Vector3 axisK = anchors.KettleAxisLocal;
            Vector3 axisA = anchors.LidTiltAxisALocal;
            Vector3 axisB = anchors.LidTiltAxisBLocal;
            if (Mathf.Abs(axisK.magnitude - 1f) > KettleAxisTolerance ||
                Mathf.Abs(axisA.magnitude - 1f) > KettleAxisTolerance ||
                Mathf.Abs(axisB.magnitude - 1f) > KettleAxisTolerance ||
                Mathf.Abs(Vector3.Dot(axisK, axisA)) > KettleAxisTolerance ||
                Mathf.Abs(Vector3.Dot(axisK, axisB)) > KettleAxisTolerance ||
                Mathf.Abs(Vector3.Dot(axisA, axisB)) > KettleAxisTolerance)
            {
                throw new InvalidOperationException(
                    "The kettle's axis and two lid tilt axes must be unit " +
                    "length and mutually orthogonal.");
            }
        }

        /// <summary>
        /// The lid and the knob reference the pivot exactly once and the
        /// head not at all — the whole re-binding — and their bind poses
        /// still line up with their bones, because the repoint changed
        /// references and nothing else.
        /// </summary>
        private static void ValidatePivotSkinning(
            Renderer renderer,
            Transform head,
            Transform pivot)
        {
            if (!(renderer is SkinnedMeshRenderer skinned))
            {
                throw new InvalidOperationException(
                    $"'{renderer.name}' must be a SkinnedMeshRenderer.");
            }

            Transform[] bones = skinned.bones;
            Mesh mesh = skinned.sharedMesh;
            if (mesh == null || mesh.bindposes.Length != bones.Length)
            {
                throw new InvalidOperationException(
                    $"'{renderer.name}' bind poses no longer match its " +
                    "bone array.");
            }

            int pivotHits = 0;
            int headHits = 0;
            for (int index = 0; index < bones.Length; index++)
            {
                if (bones[index] == pivot)
                {
                    pivotHits++;
                }
                else if (bones[index] == head)
                {
                    headHits++;
                }
            }

            if (pivotHits != 1 || headHits != 0)
            {
                throw new InvalidOperationException(
                    $"'{renderer.name}' must be skinned to the lid pivot " +
                    $"exactly once and never to the head; found " +
                    $"{pivotHits} pivot and {headHits} head references.");
            }
        }

        /// <summary>
        /// A design with a detail atlas owes the registry that texture at
        /// the declared size, and exactly the regions' renderers may
        /// sample it; a design without one owes nothing. The material is
        /// not checked here because it is checked where it always was:
        /// the atlas travels in the property block, and every renderer
        /// still shares Player3DLit.
        /// </summary>
        private static void ValidateDetailAtlasBindings(
            CityPedestrianAssetRegistry registry,
            PedestrianDescriptor descriptor,
            CityPedestrianManifest manifest)
        {
            if (!descriptor.HasDetailAtlas)
            {
                if (registry.DetailAtlas != null ||
                    registry.RendererBindings.Any(binding =>
                        binding != null && binding.UsesDetailAtlas))
                {
                    throw new InvalidOperationException(
                        $"'{descriptor.DisplayName}' declares no detail " +
                        "atlas but its registry binds one.");
                }

                return;
            }

            Texture2D atlas = registry.DetailAtlas;
            if (atlas == null ||
                atlas.width != DetailAtlasWidth ||
                atlas.height != DetailAtlasHeight ||
                !string.Equals(
                    AssetDatabase.GetAssetPath(atlas),
                    descriptor.DetailAtlasPath,
                    StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    $"'{descriptor.DisplayName}' registry must hold the " +
                    $"{DetailAtlasWidth}x{DetailAtlasHeight} detail atlas " +
                    $"at '{descriptor.DetailAtlasPath}'.");
            }

            HashSet<string> texturedRenderers = new HashSet<string>(
                manifest.texture_bindings[0].regions
                    .Select(region => region.renderer),
                StringComparer.Ordinal);
            for (int index = 0; index < registry.RendererBindings.Count; index++)
            {
                CityPedestrianRendererBinding binding =
                    registry.RendererBindings[index];
                if (binding == null ||
                    binding.UsesDetailAtlas !=
                    texturedRenderers.Contains(binding.RendererName))
                {
                    throw new InvalidOperationException(
                        $"'{descriptor.DisplayName}' renderer " +
                        $"'{binding?.RendererName}' samples the detail " +
                        "atlas without an atlas region, or has a region " +
                        "and does not sample it.");
                }
            }
        }

        /// <summary>
        /// A coin design owes both bind-pose anchors, on the right
        /// bones, and nobody else may carry the component. The bone is
        /// the part that matters: an anchor parented to the wrong one
        /// still resolves at runtime and then leaves the coin orbiting
        /// a hip, or the coat hanging off a wrist.
        /// </summary>
        private static void ValidateCoinRigBindings(
            GameObject prefab,
            CityPedestrianAssetRegistry registry,
            PedestrianDescriptor descriptor)
        {
            LastRouteFerrymanRigAnchors anchors = prefab
                .GetComponentInChildren<LastRouteFerrymanRigAnchors>(
                    true);
            if (!descriptor.CarriesCoinRig)
            {
                if (anchors != null)
                {
                    throw new InvalidOperationException(
                        $"'{descriptor.DisplayName}' declares no coin " +
                        "rig but its prefab carries anchor metadata.");
                }

                return;
            }

            if (anchors == null ||
                anchors.PedestrianRegistry != registry ||
                anchors.CoinRestAnchor == null ||
                anchors.CoatHemAnchor == null ||
                anchors.CoatHemRenderer == null)
            {
                throw new InvalidOperationException(
                    $"'{descriptor.DisplayName}' must bind its palm " +
                    "and hem anchors and its hem renderer.");
            }

            if (anchors.CoinRestAnchor.parent == null ||
                !string.Equals(
                    anchors.CoinRestAnchor.parent.name,
                    LeftHandBoneName,
                    StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    "The coin rest anchor must ride the left hand bone.");
            }

            if (anchors.CoatHemAnchor.parent == null ||
                !string.Equals(
                    anchors.CoatHemAnchor.parent.name,
                    PelvisBoneName,
                    StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    "The coat hem anchor must ride the pelvis bone.");
            }

            // His arms, for the wheel. Each socket must ride its own hand
            // bone: a socket bound to the wrong side still resolves at
            // runtime and then crosses his arms over the rim.
            if (anchors.LeftUpperArm == null ||
                anchors.LeftForearm == null ||
                anchors.LeftHand == null ||
                anchors.LeftGripSocket == null ||
                anchors.RightUpperArm == null ||
                anchors.RightForearm == null ||
                anchors.RightHand == null ||
                anchors.RightGripSocket == null)
            {
                throw new InvalidOperationException(
                    $"'{descriptor.DisplayName}' must bind both arms " +
                    "and both grip sockets for the wheel.");
            }

            if (anchors.LeftGripSocket.parent != anchors.LeftHand ||
                anchors.RightGripSocket.parent != anchors.RightHand)
            {
                throw new InvalidOperationException(
                    "Each grip socket must ride its own hand bone.");
            }

            // The skirt is cut to this. A zero would produce a cloth
            // panel with no width, which renders as nothing at all and
            // would read as 'the coat did not load'.
            if (anchors.CoatHemSize.x < 0.15f ||
                anchors.CoatHemSize.x > 0.9f)
            {
                throw new InvalidOperationException(
                    $"The coat hem measures " +
                    $"{anchors.CoatHemSize.x:0.###} m across; a waist " +
                    "is between 0.15 and 0.9 m.");
            }

            // His fifth clip, checked here because this component is the
            // only thing that carries it. The loop flag is asserted the
            // hard way round for the same reason the board transition's
            // is: an import that quietly turned a drop off a bonnet back
            // into a loop would normalise its landing towards the metal
            // he just left, and nothing downstream would say so.
            if (!descriptor.HasDismount)
            {
                if (anchors.DismountClip != null)
                {
                    throw new InvalidOperationException(
                        $"'{descriptor.DisplayName}' declares no dismount " +
                        "but its prefab carries one.");
                }

                return;
            }

            if (anchors.DismountClip == null ||
                !string.Equals(
                    NormalizeClipName(anchors.DismountClip.name),
                    descriptor.DismountClipName,
                    StringComparison.Ordinal) ||
                anchors.DismountClip.isLooping ||
                Mathf.Abs(
                    anchors.DismountClip.length -
                    descriptor.DismountDuration) > 1f / 24f)
            {
                throw new InvalidOperationException(
                    $"'{descriptor.DisplayName}' must bind the one-shot " +
                    $"'{descriptor.DismountClipName}' at its approved " +
                    "duration.");
            }
        }

        private static AnimationClip LoadLocomotionClip(
            string clipName,
            float expectedDuration,
            CityPedestrianAnimationManifest animationManifest)
        {
            AnimationClip clip = AssetDatabase
                .LoadAllAssetsAtPath(AnimationPath)
                .OfType<AnimationClip>()
                .FirstOrDefault(candidate =>
                    !candidate.name.StartsWith(
                        "__preview__",
                        StringComparison.Ordinal) &&
                    string.Equals(
                        NormalizeClipName(candidate.name),
                        clipName,
                        StringComparison.Ordinal));
            ValidateLocomotionClip(
                clip,
                clipName,
                expectedDuration,
                animationManifest);
            return clip;
        }

        private static void ValidateLocomotionClip(
            AnimationClip clip,
            string expectedName,
            float expectedDuration,
            CityPedestrianAnimationManifest animationManifest)
        {
            // A transition between two postures is the one kind of clip
            // here that must NOT be imported as a loop, so its loop flag is
            // asserted the other way round rather than skipped: an import
            // that quietly turned FerrymanBoard back into a loop would
            // re-introduce the loop-pose normalisation this clip cannot
            // survive, and would do it silently.
            bool expectLooping = !IsOneShotClip(expectedName);
            if (clip == null ||
                !string.Equals(
                    NormalizeClipName(clip.name),
                    expectedName,
                    StringComparison.Ordinal) ||
                clip.isLooping != expectLooping ||
                Mathf.Abs(clip.length - expectedDuration) > 1f / 24f ||
                !string.Equals(
                    AssetDatabase.GetAssetPath(clip),
                    AnimationPath,
                    StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    $"Pedestrian '{expectedName}' must directly reference " +
                    $"the {(expectLooping ? "looping" : "one-shot")} " +
                    $"custom locomotion clip at '{AnimationPath}'.");
            }

            CityPedestrianAnimationManifestClip source =
                animationManifest.clips.FirstOrDefault(candidate =>
                    candidate != null &&
                    string.Equals(
                        candidate.name,
                        expectedName,
                        StringComparison.Ordinal));
            if (source == null ||
                Mathf.Abs(source.duration_seconds - expectedDuration) >
                    0.0001f)
            {
                throw new InvalidOperationException(
                    $"Locomotion manifest does not describe '{expectedName}' " +
                    "with the approved duration.");
            }
        }

        private static Color BuildPaletteVariant(
            string paletteName,
            Color baseColor,
            int variant)
        {
            if (!IsVariantPalette(paletteName))
            {
                return baseColor;
            }

            Vector3 multiplier;
            if (paletteName.StartsWith(
                    "work_",
                    StringComparison.Ordinal))
            {
                // Chair Carrier variants: tobacco/base, bottle green,
                // faded burgundy and cold grey-blue.
                multiplier = variant == 1
                    ? new Vector3(0.42f, 0.72f, 0.50f)
                    : variant == 2
                        ? new Vector3(0.92f, 0.47f, 0.52f)
                        : new Vector3(0.62f, 0.72f, 0.92f);
            }
            else if (variant == 1)
            {
                multiplier = paletteName.StartsWith(
                    "coat",
                    StringComparison.Ordinal)
                    ? new Vector3(0.74f, 0.86f, 1.08f)
                    : new Vector3(0.84f, 0.92f, 1.03f);
            }
            else if (variant == 2)
            {
                multiplier = paletteName.StartsWith(
                    "coat",
                    StringComparison.Ordinal)
                    ? new Vector3(1.12f, 0.84f, 0.72f)
                    : new Vector3(1.05f, 0.88f, 0.78f);
            }
            else
            {
                multiplier = paletteName.StartsWith(
                    "coat",
                    StringComparison.Ordinal)
                    ? new Vector3(0.92f, 1.02f, 0.76f)
                    : new Vector3(0.94f, 1.00f, 0.86f);
            }

            return new Color(
                Mathf.Clamp01(baseColor.r * multiplier.x),
                Mathf.Clamp01(baseColor.g * multiplier.y),
                Mathf.Clamp01(baseColor.b * multiplier.z),
                baseColor.a);
        }

        private static bool IsVariantPalette(string paletteName)
        {
            return !string.Equals(
                       paletteName,
                       "void",
                       StringComparison.Ordinal) &&
                   !string.Equals(
                       paletteName,
                       "sole",
                       StringComparison.Ordinal) &&
                   !string.Equals(
                       paletteName,
                       "amber",
                       StringComparison.Ordinal) &&
                   !paletteName.StartsWith(
                       "chair_",
                       StringComparison.Ordinal);
        }

        private static Color ParseColor(float[] components)
        {
            return new Color(
                components[0],
                components[1],
                components[2],
                components[3]);
        }

        private static Avatar FindModelAvatar()
        {
            return AssetDatabase
                .LoadAllAssetsAtPath(PlayerModelPath)
                .OfType<Avatar>()
                .FirstOrDefault();
        }

        private static Dictionary<string, Transform> IndexUniqueTransforms(
            GameObject root,
            string label)
        {
            Dictionary<string, Transform> result =
                new Dictionary<string, Transform>(StringComparer.Ordinal);
            Transform[] transforms =
                root.GetComponentsInChildren<Transform>(true);
            for (int index = 0; index < transforms.Length; index++)
            {
                Transform transform = transforms[index];
                if (!result.TryAdd(transform.name, transform))
                {
                    throw new InvalidOperationException(
                        $"Imported {label} hierarchy contains duplicate " +
                        $"transform name '{transform.name}'.");
                }
            }

            return result;
        }

        private static Dictionary<string, Renderer> IndexUniqueRenderers(
            GameObject root)
        {
            Dictionary<string, Renderer> result =
                new Dictionary<string, Renderer>(StringComparer.Ordinal);
            Renderer[] renderers =
                root.GetComponentsInChildren<Renderer>(true);
            for (int index = 0; index < renderers.Length; index++)
            {
                Renderer renderer = renderers[index];
                if (!result.TryAdd(renderer.name, renderer))
                {
                    throw new InvalidOperationException(
                        "Imported pedestrian hierarchy contains duplicate " +
                        $"renderer name '{renderer.name}'.");
                }
            }

            return result;
        }

        private static Transform RequireTransform(
            IReadOnlyDictionary<string, Transform> transforms,
            string name,
            string label)
        {
            if (transforms.TryGetValue(name, out Transform result))
            {
                return result;
            }

            throw new InvalidOperationException(
                $"Imported {label} hierarchy is missing transform " +
                $"'{name}'.");
        }

        private static Bounds CalculateLocalBounds(
            Transform root,
            IReadOnlyList<Renderer> renderers)
        {
            Bounds result = default;
            bool initialized = false;
            for (int index = 0; index < renderers.Count; index++)
            {
                Renderer renderer = renderers[index];
                Mesh mesh = GetRendererMesh(renderer);
                if (mesh == null)
                {
                    throw new InvalidOperationException(
                        $"Renderer '{renderer.name}' has no mesh.");
                }

                Bounds bounds = mesh.bounds;
                Matrix4x4 rendererToRoot =
                    root.worldToLocalMatrix * renderer.localToWorldMatrix;
                for (int corner = 0; corner < 8; corner++)
                {
                    Vector3 point = rendererToRoot.MultiplyPoint3x4(
                        new Vector3(
                            (corner & 1) == 0
                                ? bounds.min.x
                                : bounds.max.x,
                            (corner & 2) == 0
                                ? bounds.min.y
                                : bounds.max.y,
                            (corner & 4) == 0
                                ? bounds.min.z
                                : bounds.max.z));
                    if (!initialized)
                    {
                        result = new Bounds(point, Vector3.zero);
                        initialized = true;
                    }
                    else
                    {
                        result.Encapsulate(point);
                    }
                }
            }

            if (!initialized)
            {
                throw new InvalidOperationException(
                    "City pedestrian model contains no renderers.");
            }

            return result;
        }

        private static int CountTriangles(
            IReadOnlyList<Renderer> renderers)
        {
            int triangleCount = 0;
            for (int index = 0; index < renderers.Count; index++)
            {
                Mesh mesh = GetRendererMesh(renderers[index]);
                if (mesh == null)
                {
                    throw new InvalidOperationException(
                        $"Renderer '{renderers[index].name}' has no mesh.");
                }

                for (int subMesh = 0;
                     subMesh < mesh.subMeshCount;
                     subMesh++)
                {
                    triangleCount +=
                        (int)(mesh.GetIndexCount(subMesh) / 3);
                }
            }

            return triangleCount;
        }

        private static Mesh GetRendererMesh(Renderer renderer)
        {
            if (renderer is SkinnedMeshRenderer skinned)
            {
                return skinned.sharedMesh;
            }

            MeshFilter filter = renderer.GetComponent<MeshFilter>();
            return filter != null ? filter.sharedMesh : null;
        }

        private static string NormalizeClipName(string sourceName)
        {
            int separator = sourceName.LastIndexOf('|');
            return separator >= 0 && separator + 1 < sourceName.Length
                ? sourceName.Substring(separator + 1)
                : sourceName;
        }

        private static void EnsureFolderForAsset(string assetPath)
        {
            string directory = Path.GetDirectoryName(assetPath)
                ?.Replace('\\', '/');
            if (string.IsNullOrEmpty(directory))
            {
                return;
            }

            string[] segments = directory.Split('/');
            string current = segments[0];
            for (int index = 1; index < segments.Length; index++)
            {
                string next = $"{current}/{segments[index]}";
                if (!AssetDatabase.IsValidFolder(next))
                {
                    AssetDatabase.CreateFolder(current, segments[index]);
                }

                current = next;
            }
        }

        [Serializable]
        private sealed class CityPedestrianManifest
        {
            public string generator_version;
            public string design_id;
            public float height_m;
            public string pose;
            public string forward_axis;
            public string anatomical_left_axis;
            public int mesh_count;
            public int triangle_count;
            public int[] triangle_budget;
            public string material_asset;
            public bool emissive;
            public bool colliders;
            public int animation_count;
            public string[] animations;
            public string shared_animation_source;
            public string[] shared_clips;
            public bool staged;
            public bool pool_eligible;
            public float wheel_radius_m;
            public string[] pivot_names;
            public string build_signature;
            public CityPedestrianManifestBone[] bones;
            public CityPedestrianManifestPart[] parts;

            // Written only by designs that declare them; JsonUtility
            // leaves each null on every other manifest, and null is read
            // as empty everywhere they are consumed.
            public string[] signature_effects;
            public CityPedestrianManifestRigAnchor[] rig_anchors;
            public CityPedestrianManifestTextureBinding[] texture_bindings;
        }

        [Serializable]
        private sealed class CityPedestrianManifestBone
        {
            public string name;
            public string parent;
        }

        [Serializable]
        private sealed class CityPedestrianManifestPart
        {
            public string name;
            public string role;
            public string bone;
            public string palette_name;
            public float[] base_color;

            /// <summary>
            /// The detail atlas region this part's UV0 is authored into,
            /// or null/empty for a flat-colour part.
            /// </summary>
            public string atlas_region;
        }

        [Serializable]
        private sealed class CityPedestrianManifestRigAnchor
        {
            public string name;
            public string bone;
            public string kind;
            public string[] parts;
            public string axis_from;
        }

        [Serializable]
        private sealed class CityPedestrianManifestTextureBinding
        {
            public string texture_asset;
            public int width_px;
            public int height_px;
            public string[] materials;
            public string shader_property;
            public string color_space;
            public string filter_mode;
            public string wrap_mode;
            public bool mipmaps;
            public string compression;
            public int uv_channel;
            public string uv_origin;
            public int uv_safe_inset_px;
            public string material_tint_hex;
            public string tint_source;
            public string sha256;
            public CityPedestrianManifestTextureRegion[] regions;
        }

        [Serializable]
        private sealed class CityPedestrianManifestTextureRegion
        {
            public string name;
            public string renderer;
            public int x_px;
            public int y_px;
            public int width_px;
            public int height_px;
        }

        private sealed class PedestrianDescriptor
        {
            public PedestrianDescriptor(
                string displayName,
                string prefabRootName,
                string designId,
                string modelPath,
                string manifestPath,
                string prefabPath,
                string idleClipName,
                string walkClipName,
                float idleDuration,
                float walkDuration,
                int minimumTriangleCount,
                int maximumTriangleCount,
                bool carriesHeadLamp = false,
                bool preservesAirborneMotion = false,
                string sitClipName = null,
                float sitDuration = 0f,
                bool isStaged = false,
                bool isWheelchair = false,
                bool carriesFishingRig = false,
                string actionClipName = null,
                float actionDuration = 0f,
                bool carriesCoinRig = false,
                string dismountClipName = null,
                float dismountDuration = 0f,
                bool carriesBoilingKettle = false,
                string detailAtlasPath = null,
                string ambientIdleClipName = null,
                string ambientWalkClipName = null,
                float ambientIdleDuration = 0f,
                float ambientWalkDuration = 0f)
            {
                AmbientIdleClipName = ambientIdleClipName;
                AmbientWalkClipName = ambientWalkClipName;
                AmbientIdleDuration = ambientIdleDuration;
                AmbientWalkDuration = ambientWalkDuration;
                CarriesBoilingKettle = carriesBoilingKettle;
                DetailAtlasPath = detailAtlasPath;
                DismountClipName = dismountClipName;
                DismountDuration = dismountDuration;
                IsStaged = isStaged;
                IsWheelchair = isWheelchair;
                CarriesFishingRig = carriesFishingRig;
                CarriesCoinRig = carriesCoinRig;
                SitClipName = sitClipName;
                SitDuration = sitDuration;
                ActionClipName = actionClipName;
                ActionDuration = actionDuration;
                CarriesHeadLamp = carriesHeadLamp;
                PreservesAirborneMotion = preservesAirborneMotion;
                DisplayName = displayName;
                PrefabRootName = prefabRootName;
                DesignId = designId;
                ModelPath = modelPath;
                ManifestPath = manifestPath;
                PrefabPath = prefabPath;
                IdleClipName = idleClipName;
                WalkClipName = walkClipName;
                IdleDuration = idleDuration;
                WalkDuration = walkDuration;
                MinimumTriangleCount = minimumTriangleCount;
                MaximumTriangleCount = maximumTriangleCount;
            }

            public string DisplayName { get; }
            public string PrefabRootName { get; }
            public string DesignId { get; }
            public string ModelPath { get; }
            public string ManifestPath { get; }
            public string PrefabPath { get; }
            public string IdleClipName { get; }
            public string WalkClipName { get; }
            public float IdleDuration { get; }
            public float WalkDuration { get; }

            /// <summary>
            /// The design's authored seated loop, or <c>null</c> for a design
            /// that declares no Route 01 ride. It is not a locomotion clip:
            /// its feet leave the pavement plane on purpose.
            /// </summary>
            public string SitClipName { get; }
            public float SitDuration { get; }
            public bool RidesBus => !string.IsNullOrEmpty(SitClipName);

            /// <summary>
            /// The ordinary street gait of a design that has a placed role
            /// AND roams, or <c>null</c> for one that does neither.
            ///
            /// A promoted resident cannot simply have its walk slot
            /// rewritten. The babushka's <c>WalkClipName</c> is
            /// <c>BabushkaBeat</c> - a carpet being beaten on the spot - and
            /// her drying-yard presentation plays exactly that. So the
            /// roaming pool reads this pair when it is declared, and every
            /// staged presentation goes on reading the idle and walk slots
            /// untouched.
            /// </summary>
            public string AmbientIdleClipName { get; }
            public string AmbientWalkClipName { get; }
            public float AmbientIdleDuration { get; }
            public float AmbientWalkDuration { get; }
            public bool HasAmbientGait =>
                !string.IsNullOrEmpty(AmbientIdleClipName);

            /// <summary>
            /// The design's one authored non-locomotion beat, or
            /// <c>null</c> for a design that only ever idles and walks.
            /// Like the seated loop it is not a locomotion clip and is
            /// never ground-validated as one.
            /// </summary>
            public string ActionClipName { get; }
            public float ActionDuration { get; }
            public bool HasAction =>
                !string.IsNullOrEmpty(ActionClipName);

            /// <summary>
            /// The design's second one-shot transition, or <c>null</c>.
            /// Only the Ferryman declares one: he is the only man here who
            /// has to LEAVE a seat under his own power before he can walk,
            /// and a drop off a bonnet cannot be the tail of the clip that
            /// gets him into a car - between them he has three metres of
            /// pavement to cross.
            /// </summary>
            public string DismountClipName { get; }
            public float DismountDuration { get; }
            public bool HasDismount =>
                !string.IsNullOrEmpty(DismountClipName);
            public int MinimumTriangleCount { get; }
            public int MaximumTriangleCount { get; }

            /// <summary>
            /// Declares the one shadowless Spot this design wears. Pedestrian
            /// presentations are otherwise light-free; a design that wants a
            /// working lamp has to say so here rather than smuggling a Light
            /// into the prefab.
            /// </summary>
            public bool CarriesHeadLamp { get; }

            /// <summary>
            /// Declares that this design's clips leave the pavement, so the
            /// runtime must not pin its lowest sole every frame.
            /// </summary>
            public bool PreservesAirborneMotion { get; }

            /// <summary>
            /// Staged designs are authored by this import pipeline but stay
            /// outside Resources and the runtime archetype catalog.
            /// </summary>
            public bool IsStaged { get; }

            /// <summary>
            /// Declares the additional passive mechanism-pivot registry used
            /// by an authored wheelchair design.
            /// </summary>
            public bool IsWheelchair { get; }

            /// <summary>
            /// Declares the two bind-pose anchors a fishing design needs:
            /// the top of the pipe bowl on the head bone and the far end
            /// of the rod on the right hand. Both are drawn by rigidly
            /// skinned vertices, so neither has a Transform of its own
            /// for the runtime to hang an ember or a line from.
            /// </summary>
            public bool CarriesFishingRig { get; }

            /// <summary>
            /// Declares the two bind-pose anchors the Ferryman needs:
            /// the hollow of his open left palm, where the coin lies
            /// between throws, and the top of his coat hem, which the
            /// runtime cloth skirt hangs from. Both are drawn by
            /// rigidly skinned vertices, so neither has a Transform of
            /// its own for the runtime to hang anything off.
            /// </summary>
            public bool CarriesCoinRig { get; }

            /// <summary>
            /// Declares the always-on boiling kettle: a lid pivot under
            /// the head that the lid and knob are re-skinned to, and a
            /// spout-mouth anchor for the steam. The runtime factory
            /// builds the effect only from a prefab that carries these,
            /// so a design that wants to boil has to say so here.
            /// </summary>
            public bool CarriesBoilingKettle { get; }

            /// <summary>
            /// The design's detail atlas, or <c>null</c> for a flat-colour
            /// design. Light greys multiplied by the palette tint through
            /// the registry's property block; never a material.
            /// </summary>
            public string DetailAtlasPath { get; }
            public bool HasDetailAtlas =>
                !string.IsNullOrEmpty(DetailAtlasPath);

            public int ExpectedLightCount => CarriesHeadLamp ? 1 : 0;
            public float Height => ExpectedHeight;
        }

        [Serializable]
        private sealed class CityPedestrianAnimationManifest
        {
            public string generator_version;
            public string skeleton_source;
            public int bone_count;
            public int fps;
            public bool root_motion;
            public int mesh_count;
            public int clip_count;
            public string build_signature;
            public CityPedestrianAnimationManifestClip[] clips;
        }

        [Serializable]
        private sealed class CityPedestrianAnimationManifestClip
        {
            public string name;
            public string archetype;
            public float duration_seconds;
            public int frame_start;
            public int frame_end;
            public bool loop;

            /// <summary>
            /// A clip that plays once and does not return to its own first
            /// frame - a transition between two postures rather than a
            /// posture. Exactly the inverse of <see cref="loop"/>, and both
            /// are written because the pair being consistent is itself
            /// something the contract checks.
            /// </summary>
            public bool one_shot;
            public bool in_place;
            public string authored_posture;
            public string gait;
            public int keyed_bone_count;
            public float loop_max_error;
            public float[] root_translation_range_m;

            /// <summary>
            /// For a perched clip: how far the pelvis bone rides above the
            /// LOWEST drawn point of the posed model - his boot sole. The
            /// runtime needs exactly this to set him down, because the
            /// model origin is the sole plane of the BIND pose and a
            /// perched pose lifts the feet well clear of it.
            /// </summary>
            public float seated_drop_m;
            public float wheel_ground_min_m;
            public float wheel_ground_max_contact_gap_m;
            public float footrest_min_clearance_m;
            public float rim_hand_max_distance_m;
        }
    }
}

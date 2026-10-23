using System;
using UnityEngine;

namespace BarPromenade
{
    /// <summary>Scene services and the measured coordinate frame shared by bathroom actions.</summary>
    public sealed class BathroomInteractionHost : IHomeToiletViewHost
    {
        public Transform transform { get; }
        public Transform Room { get; }
        public PlayerRuntime Player { get; }
        public PlayerCameraFollow CameraFollow { get; }
        public InteractionPromptView InteractionPrompt { get; }
        public IntoxicationHudView IntoxicationHud { get; }
        public RetroAudioService Audio { get; }
        public HomeFixedCameraController FixedCamera { get; }
        private HomeInteriorRoot sourceHome;
        private readonly HomePlayerOcclusionController playerOcclusion;
        public HomePlayerOcclusionController PlayerOcclusion => sourceHome != null ? sourceHome.PlayerOcclusion : playerOcclusion;
        public ToiletFixtureAnchors Toilet { get; }
        public string ResidueScope { get; }
        public Transform Receivers { get; }
        public Func<Transform, bool> ExcludedReceivers { get; }
        public HomeToiletInteraction SmallAction { get; set; }
        public HomeToiletPlungeInteraction LargeAction { get; set; }
        public Transform AnatomyParent { get; set; }

        public BathroomInteractionHost(Transform frame, Transform room, PlayerRuntime player,
            PlayerCameraFollow camera, InteractionPromptView prompt, IntoxicationHudView hud,
            RetroAudioService audio, ToiletFixtureAnchors toilet, string residueScope = null,
            Transform receivers = null, Func<Transform, bool> excludedReceivers = null,
            HomeFixedCameraController fixedCamera = null, HomePlayerOcclusionController playerOcclusion = null)
        {
            transform = frame != null ? frame : throw new ArgumentNullException(nameof(frame));
            Room = room != null ? room : throw new ArgumentNullException(nameof(room));
            Player = player;
            CameraFollow = camera != null ? camera : throw new ArgumentNullException(nameof(camera));
            InteractionPrompt = prompt;
            IntoxicationHud = hud;
            Audio = audio;
            Toilet = toilet;
            ResidueScope = residueScope;
            Receivers = receivers != null ? receivers : room;
            ExcludedReceivers = excludedReceivers;
            FixedCamera = fixedCamera;
            this.playerOcclusion = playerOcclusion;
            AnatomyParent = frame;
        }

        public static BathroomInteractionHost FromHome(HomeInteriorRoot home)
        {
            if (home == null) throw new ArgumentNullException(nameof(home));
            return new BathroomInteractionHost(home.transform, home.Room, home.Player, home.CameraFollow,
                home.InteractionPrompt, home.IntoxicationHud, home.Audio,
                ToiletFixtureAnchors.FromHome(home), fixedCamera: home.FixedCamera,
                playerOcclusion: home.PlayerOcclusion)
            { sourceHome = home, SmallAction = home.ToiletScene, LargeAction = home.ToiletPlunge };
        }

        public bool TryGetAimTarget(Vector3 facing, out Vector3 worldTarget)
        {
            worldTarget = Toilet != null && Toilet.Water != null ? Toilet.Water.position + facing * .15f : Vector3.zero;
            return Toilet != null && Toilet.Water != null;
        }
    }

    /// <summary>The existing authored toilet and hero clips share this metre-space frame.</summary>
    public sealed class ToiletFixtureAnchors
    {
        public Transform Frame { get; }
        public HomeToiletLid Lid { get; }
        public Transform Bowl { get; }
        public Transform Water { get; }
        public Transform Seat { get; }
        public Transform Flush { get; }
        public Vector3 EntryPosition => Frame.TransformPoint(new Vector3(3.32f, 0f, 1.40f));
        public Quaternion EntryRotation => Frame.rotation * Quaternion.LookRotation(Vector3.right);
        public Vector3 ExitPosition => Frame.TransformPoint(new Vector3(3.10f, 0f, 1.40f));
        public Quaternion ExitRotation => Frame.rotation * Quaternion.LookRotation(Vector3.left);
        public Vector3 SeatPelvis => Frame.TransformPoint(HomeToiletActorPresentation.SeatedPelvisLocal);
        public Vector3 BowlLookTarget => Frame.TransformPoint(new Vector3(4.05f, .4373f, 1.40f));
        public Vector3 FlushAudioPosition => Frame.TransformPoint(new Vector3(4.49f, .9f, 1.40f));

        public ToiletFixtureAnchors(Transform frame, HomeToiletLid lid, Transform bowl,
            Transform water, Transform seat, Transform flush)
        {
            Frame = frame != null ? frame : throw new ArgumentNullException(nameof(frame));
            Lid = lid; Bowl = bowl; Water = water; Seat = seat; Flush = flush;
        }

        public static ToiletFixtureAnchors FromHome(HomeInteriorRoot home) => new ToiletFixtureAnchors(
            home.transform, home.Room.GetComponentInChildren<HomeToiletLid>(true),
            home.Room.Find("Home Bathroom Toilet Bowl"), home.Room.Find("Home Bathroom Toilet Water"),
            home.Room.Find("Home Bathroom Toilet Seat"), home.Room.Find("Home Bathroom Toilet Flush"));
    }
}

using System;
using UnityEngine;

namespace BarPromenade
{
    /// <summary>Location adapter for the shared cabinet camera and modal lifecycle.</summary>
    public sealed class RefrigeratorInteractionContext
    {
        public Transform Space { get; }
        public PlayerRuntime Player { get; }
        public PlayerCameraFollow CameraFollow { get; }
        public InteractionPromptView Prompt { get; }
        public IntoxicationHudView Hud { get; }
        public Func<bool> Admission { get; set; }
        public Action PreparingPlayer { get; set; }
        public bool PositionPlayerAtEntry { get; set; }
        public Action EntryPoseReady { get; set; }
        public Action RestoringPlayer { get; set; }
        public Action RestoreCamera { get; set; }
        public Action<float> DoorAmountChanged { get; set; }
        public Action<RetroSfxId, Vector3> PlayCue { get; set; }

        public RefrigeratorInteractionContext(Transform space, PlayerRuntime player,
            PlayerCameraFollow cameraFollow, InteractionPromptView prompt, IntoxicationHudView hud = null)
        {
            Space = space != null ? space : throw new ArgumentNullException(nameof(space));
            Player = player;
            CameraFollow = cameraFollow != null ? cameraFollow : throw new ArgumentNullException(nameof(cameraFollow));
            Prompt = prompt;
            Hud = hud;
            if (player.GameObject == null || player.Interactor == null || player.Visual == null)
                throw new ArgumentException("A refrigerator requires an initialized production player.", nameof(player));
        }
    }
}

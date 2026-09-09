using System;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace BarPromenade
{
    [DefaultExecutionOrder(-950)]
    public sealed class AlpineColdExposureDriver : MonoBehaviour
    {
        private MonoBehaviour owner;
        private Camera gameplayCamera;
        private Func<bool> ready;
        private Func<bool> sheltered;
        private AlpineFrostAudio frostAudio;
        private bool wasAdvancing;
        public AlpineColdExposureModel Model { get; } = new AlpineColdExposureModel();
        public AlpineFrostAudio FrostAudio => frostAudio;
        public bool IsSheltered => owner != null && sheltered != null && sheltered();

        private void Awake()
        {
            frostAudio = new AlpineFrostAudio(gameObject);
            SceneManager.activeSceneChanged += OnActiveSceneChanged;
        }

        public void Bind(MonoBehaviour context, Camera camera, Func<bool> isReady,
            Func<bool> isSheltered)
        {
            owner = context;
            gameplayCamera = camera;
            ready = isReady;
            sheltered = isSheltered;
            wasAdvancing = false;
        }

        private bool ContextReady => owner != null && gameplayCamera != null &&
            owner.gameObject.scene == SceneManager.GetActiveScene() && ready != null && ready() &&
            !AreaTravelService.IsTraveling && !AreaTravelService.IsComposing &&
            !SceneTransitionService.IsTransitioning;

        public bool IsVisible(Camera camera) => camera != null && camera == gameplayCamera &&
            ContextReady && Model.FrostAmount > 0f;

        private void Update()
        {
            bool visible = ContextReady;
            bool frozen = !visible || GameTimeScaleRuntime.IsPaused;
            // The first ready frame can carry a large unscaled delta from
            // synchronous scene composition. It belongs to the loading view,
            // not to time spent outdoors (or warming in the visible house).
            float delta = frozen || !wasAdvancing ? 0f : GameTimeScaleRuntime.CalendarDeltaTime;
            wasAdvancing = !frozen;
            bool warm = visible && IsSheltered;
            Model.Step(delta, warm,
                GameSessionState.IsInventoryItemEquipped(InventoryItemId.Scarf));
            frostAudio.Step(delta, Model.FrostAmount, warm, frozen);
        }

        private void OnActiveSceneChanged(Scene previous, Scene next)
        {
            // Single-load transitions intentionally keep the clock even while
            // their source root and camera have already been destroyed.
            if (next.name != SceneIds.AlpineVillage && next.name != SceneIds.MothersHouseInterior &&
                next.name != SceneIds.DoorTransition && next.name != SceneIds.AreaLoading)
                ResetSession();
        }

        public void ResetSession()
        {
            Model.Reset();
            wasAdvancing = false;
            frostAudio?.Reset();
        }

        private void OnDestroy()
        {
            SceneManager.activeSceneChanged -= OnActiveSceneChanged;
            frostAudio?.Dispose();
        }
    }
}

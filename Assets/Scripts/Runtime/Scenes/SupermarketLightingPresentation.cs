using BarPromenade.Rendering;
using UnityEngine;

namespace BarPromenade
{
    /// <summary>Scene-local reflection coverage; the authored fluorescent light budget is unchanged.</summary>
    [DisallowMultipleComponent]
    public sealed class SupermarketLightingPresentation : SceneLightingPresentation
    {
        private SupermarketInteriorRoot owner;
        public SupermarketReflectionController Reflection { get; private set; }
        public override Light VolumeLight => null;
        public override int HalationSourceCount => 0;

        public void Initialize(SupermarketInteriorRoot root)
        {
            owner = root;
            Reflection = gameObject.AddComponent<SupermarketReflectionController>();
            Reflection.Initialize(root, this);
        }
        protected override void OnEnable()
        {
            base.OnEnable();
            if (Reflection != null) Reflection.enabled = true;
        }
        protected override void OnDisable()
        {
            if (Reflection != null) Reflection.enabled = false;
            base.OnDisable();
        }
        public override bool IsForCamera(Camera camera) => owner != null && owner.isActiveAndEnabled &&
            camera != null && camera.cameraType == CameraType.Game && camera.gameObject.scene == owner.gameObject.scene &&
            !camera.TryGetComponent(out ScenePresentationExclusion _);
        public override void PrepareForCamera(Camera camera) =>
            Reflection?.Prepare(IsForCamera(camera) && GraphicsEffectsSettings.CleanReflectionsEnabled);
        public override bool TryGetHalationSource(int index, out SceneHalationSource source)
        { source = default; return false; }
    }
}

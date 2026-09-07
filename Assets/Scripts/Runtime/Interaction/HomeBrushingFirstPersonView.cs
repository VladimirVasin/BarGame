using UnityEngine;

namespace BarPromenade
{
    /// <summary>The brushing lens stays at the posed eyes; the real mirror supplies the face.</summary>
    public sealed class HomeBrushingFirstPersonView : MonoBehaviour
    {
        public const float FieldOfView = 48f;
        public const float EyeHeightAboveMouth = 0.068f;
        public const float HeadHideBlend = 0.90f;
        private HomeInteriorRoot home;
        private Player3DAssetRegistry registry;
        private Transform head;
        private Vector3 eyeOffsetInHead;
        private Player3DHeadVisibility hiddenHead;
        public bool IsHeadHidden => hiddenHead != null;
        public Vector3 EyePosition => registry.Anchors.Mouth.position + head.rotation * eyeOffsetInHead;
        public Vector3 Position { get; private set; }
        public Quaternion Rotation { get; private set; }

        public bool Initialize(HomeInteriorRoot root)
        {
            End();
            home = root;
            if (!(root.Player.Visual is Player3DCharacterPresentation visual) || visual.Registry == null ||
                !visual.Registry.TryGetPart(Player3DAnatomicalPart.Head, out var part)) return false;
            registry = visual.Registry;
            head = part.Bone;
            return registry.Anchors.Mouth != null && head != null;
        }

        public void Begin()
        {
            eyeOffsetInHead = Quaternion.Inverse(head.rotation) * home.transform.up * EyeHeightAboveMouth;
        }

        public void Present(float blend, Vector3 valve, float valveLook, Vector3 basin, float spitLook)
        {
            Position = EyePosition;
            Vector3 face = home.transform.InverseTransformPoint(registry.Anchors.Mouth.position) + Vector3.up * 0.045f;
            Vector3 target = home.transform.TransformPoint(HomeBathroomMirrorPlane.Reflect(face));
            Quaternion mirrorRotation = Quaternion.LookRotation(target - Position, home.transform.up);
            Quaternion valveRotation = Quaternion.LookRotation(valve - Position, home.transform.up);
            Quaternion basinRotation = Quaternion.LookRotation(basin - Position, home.transform.up);
            Rotation = Quaternion.Slerp(mirrorRotation, valveRotation, Mathf.Clamp01(valveLook));
            Rotation = Quaternion.Slerp(Rotation, basinRotation, Mathf.Clamp01(spitLook));
            if (blend >= HeadHideBlend && hiddenHead == null)
                hiddenHead = Player3DHeadVisibility.Hide(registry);
            else if (blend < HeadHideBlend) RestoreHead();
        }

        public void End() => RestoreHead();
        private void RestoreHead() { hiddenHead?.Restore(); hiddenHead = null; }
        private void OnDisable() => End();
        private void OnDestroy() => End();
    }
}

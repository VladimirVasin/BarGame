using UnityEngine;

namespace BarPromenade
{
    /// <summary>Shapes the existing skinned face from the same pose that selects its atlas cell.</summary>
    internal sealed class Player3DFaceDeformation
    {
        private readonly SkinnedMeshRenderer renderer;
        private readonly int jaw, lids, wideEyes, brows, purse;

        public Player3DFaceDeformation(Player3DFaceAtlasBinding binding)
        {
            renderer = binding?.Renderer as SkinnedMeshRenderer;
            Mesh mesh = renderer != null ? renderer.sharedMesh : null;
            jaw = Index(mesh, "Face.Jaw");
            lids = Index(mesh, "Face.Lids");
            wideEyes = Index(mesh, "Face.WideEyes");
            brows = Index(mesh, "Face.Brows");
            purse = Index(mesh, "Face.Purse");
        }

        private static int Index(Mesh mesh, string name) => mesh != null ? mesh.GetBlendShapeIndex(name) : -1;

        public void Apply(PlayerFacialExpression expression)
        {
            switch (expression)
            {
                case PlayerFacialExpression.HalfBlink: Set(lidWeight: 48f); break;
                case PlayerFacialExpression.ClosedBlink: Set(lidWeight: 100f); break;
                case PlayerFacialExpression.Watchful: Set(eyeWeight: 28f, browWeight: 22f); break;
                case PlayerFacialExpression.Tense: Set(lidWeight: 20f, purseWeight: 16f); break;
                case PlayerFacialExpression.Drowsy: Set(lidWeight: 54f, jawWeight: 12f); break;
                case PlayerFacialExpression.Glazed: Set(lidWeight: 27f, jawWeight: 9f); break;
                case PlayerFacialExpression.Slack: Set(lidWeight: 32f, jawWeight: 29f); break;
                case PlayerFacialExpression.Grimace: Set(lidWeight: 42f, jawWeight: 13f, purseWeight: 25f); break;
                case PlayerFacialExpression.TeethDisplay: Set(jawWeight: 15f); break;
                case PlayerFacialExpression.TeethInspectHalf: Set(jawWeight: 41f); break;
                case PlayerFacialExpression.TeethInspect: Set(jawWeight: 77f); break;
                case PlayerFacialExpression.Spit: Set(jawWeight: 22f, purseWeight: 68f); break;
                case PlayerFacialExpression.Horror: Set(jawWeight: 100f, eyeWeight: 100f, browWeight: 86f); break;
                default: Set(); break;
            }
        }

        public void Apply(in SpeechFacePose pose)
        {
            float opening = pose.Mouth switch
            {
                SpeechMouthPose.Narrow => 22f,
                SpeechMouthPose.Open => 66f,
                SpeechMouthPose.Round => 49f,
                SpeechMouthPose.Wide => 37f,
                SpeechMouthPose.Teeth => 29f,
                _ => 0f
            };
            float closure = pose.Expression switch
            {
                SpeechFaceExpression.HalfBlink => 50f,
                SpeechFaceExpression.Blink => 100f,
                SpeechFaceExpression.Skeptical => 28f,
                _ => 0f
            };
            bool emphasis = pose.Expression == SpeechFaceExpression.Emphasis;
            Set(opening, closure, emphasis ? 36f : 0f, emphasis ? 56f : 0f,
                pose.Mouth == SpeechMouthPose.Round ? 80f : 0f);
        }

        public void CopyTo(Player3DFaceDeformation target)
        {
            if (renderer == null || target.renderer == null || renderer.sharedMesh != target.renderer.sharedMesh)
                return;
            target.Set(Read(jaw), Read(lids), Read(wideEyes), Read(brows), Read(purse));
        }

        internal void SetJaw(float opening) => SetWeight(jaw, opening);

        private float Read(int index) => index >= 0 ? renderer.GetBlendShapeWeight(index) : 0f;

        private void Set(float jawWeight = 0f, float lidWeight = 0f, float eyeWeight = 0f,
            float browWeight = 0f, float purseWeight = 0f)
        {
            if (renderer == null) return;
            SetWeight(jaw, jawWeight);
            SetWeight(lids, lidWeight);
            SetWeight(wideEyes, eyeWeight);
            SetWeight(brows, browWeight);
            SetWeight(purse, purseWeight);
        }

        private void SetWeight(int index, float weight)
        {
            if (index >= 0 && Read(index) != weight) renderer.SetBlendShapeWeight(index, weight);
        }
    }
}

using UnityEngine;

namespace BarPromenade
{
    /// <summary>A conversation leases the face; the actor retains its ordinary presentation.</summary>
    public interface ISpeechFaceActor
    {
        bool TrySetSpeechFace(object owner, SpeechFacePose pose);
        void ReleaseSpeechFace(object owner);
    }

    public static class SpeechFaceAtlasResources
    {
        public const string HeroPath = "Dialogue/Faces/HeroDialogueFace";
        public const string ForemanPath = "Dialogue/Faces/ForemanDialogueFace";
        public const int CellSize = 64, Columns = 8, SoiledOffset = 32;
    }

    /// <summary>Face-local UVs select opaque, authored pixels on the actor's shared material.</summary>
    internal sealed class SpeechFaceAtlasPresenter
    {
        private static readonly int BaseMap = Shader.PropertyToID("_BaseMap");
        private static readonly int BaseMapST = Shader.PropertyToID("_BaseMap_ST");
        private static readonly int MainTex = Shader.PropertyToID("_MainTex");
        private static readonly int MainTexST = Shader.PropertyToID("_MainTex_ST");
        private MaterialPropertyBlock properties;
        private Renderer renderer;
        private Texture2D texture;
        private int rows;
        public bool IsConfigured => renderer != null && texture != null;

        public bool Configure(Renderer target, string resourcePath, bool hasSoiledFaces)
        {
            renderer = target;
            texture = Resources.Load<Texture2D>(resourcePath);
            rows = hasSoiledFaces ? 8 : 4;
            if (renderer != null && texture != null && texture.width == 512 && texture.height == rows * 64)
                return true;
            renderer = null; texture = null;
            return false;
        }

        public bool Apply(SpeechFacePose pose, bool soiled = false)
            => ApplyCell(pose.AtlasCell + (soiled && rows == 8 ? SpeechFaceAtlasResources.SoiledOffset : 0));

        /// <summary>Optional authored quiet expressions use spare cells in the same atlas.</summary>
        public bool ApplyCell(int cell)
        {
            if (!IsConfigured || cell < 0 || cell >= rows * 8) return false;
            properties ??= new MaterialPropertyBlock();
            // Authoring counts rows from the image top; Unity counts from the bottom.
            Vector4 transform = new Vector4(1f / 8f, 1f / rows,
                (cell % 8) / 8f, (rows - 1 - cell / 8) / (float)rows);
            renderer.GetPropertyBlock(properties);
            properties.SetTexture(BaseMap, texture); properties.SetTexture(MainTex, texture);
            properties.SetVector(BaseMapST, transform); properties.SetVector(MainTexST, transform);
            renderer.SetPropertyBlock(properties);
            properties.Clear();
            return true;
        }

        public void Clear() { renderer = null; texture = null; properties?.Clear(); }
    }
}

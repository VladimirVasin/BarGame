using UnityEngine;

namespace BarPromenade
{
    /// <summary>Shared texture drawing for the Editor's linear IMGUI target.</summary>
    public static class RetroUiTexture
    {
#if UNITY_EDITOR
        private static Material linearMaterial;
        private static bool shaderMissing;
#endif

        public static void DrawTexture(Rect position, Texture image,
            ScaleMode scaleMode = ScaleMode.StretchToFill, bool alphaBlend = true,
            float imageAspect = 0f)
        {
#if UNITY_EDITOR
            if (RetroUiTheme.UsesLinearGuiOutput && image != null)
            {
                if (Event.current.type != EventType.Repaint) return;
                if (TryGetMaterial(out Material material))
                {
                    Rect source = new Rect(0f, 0f, 1f, 1f);
                    if (imageAspect == 0f) imageAspect = (float)image.width / image.height;
                    float destinationAspect = position.width / position.height;
                    if (scaleMode == ScaleMode.ScaleAndCrop)
                    {
                        if (destinationAspect > imageAspect)
                        {
                            float fraction = imageAspect / destinationAspect;
                            source = new Rect(0f, (1f - fraction) * .5f, 1f, fraction);
                        }
                        else
                        {
                            float fraction = destinationAspect / imageAspect;
                            source = new Rect((1f - fraction) * .5f, 0f, fraction, 1f);
                        }
                    }
                    else if (scaleMode == ScaleMode.ScaleToFit)
                    {
                        if (destinationAspect > imageAspect)
                        {
                            float width = position.height * imageAspect;
                            position.x += (position.width - width) * .5f;
                            position.width = width;
                        }
                        else
                        {
                            float height = position.width / imageAspect;
                            position.y += (position.height - height) * .5f;
                            position.height = height;
                        }
                    }
                    DrawLinear(position, image, source, alphaBlend, material);
                    return;
                }
            }
#endif
            GUI.DrawTexture(position, image, scaleMode, alphaBlend, imageAspect);
        }

        public static void DrawTextureWithTexCoords(Rect position, Texture image,
            Rect texCoords, bool alphaBlend = true)
        {
#if UNITY_EDITOR
            if (RetroUiTheme.UsesLinearGuiOutput && image != null)
            {
                if (Event.current.type != EventType.Repaint) return;
                if (TryGetMaterial(out Material material))
                {
                    DrawLinear(position, image, texCoords, alphaBlend, material);
                    return;
                }
            }
#endif
            GUI.DrawTextureWithTexCoords(position, image, texCoords, alphaBlend);
        }

#if UNITY_EDITOR
        private static void DrawLinear(Rect position, Texture image, Rect source,
            bool alphaBlend, Material material)
        {
            // Native GUI vertices quantize tint to bytes. Keep those bytes in
            // display space and decode them in the shader, preserving dark
            // authored colours instead of quantizing their small linear values.
            Graphics.DrawTexture(position, image, source, 0, 0, 0, 0,
                GUI.color.gamma, material, alphaBlend ? 0 : 1);
        }

        private static bool TryGetMaterial(out Material material)
        {
            if (linearMaterial == null && !shaderMissing)
            {
                Shader shader = Resources.Load<Shader>("Shaders/RetroUiLinearTexture");
                if (shader != null && shader.isSupported)
                    linearMaterial = new Material(shader) { hideFlags = HideFlags.HideAndDontSave };
                else
                {
                    shaderMissing = true;
                    Debug.LogError("The shared linear Editor UI texture shader is unavailable.");
                }
            }
            material = linearMaterial;
            return material != null;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetSharedResources()
        {
            if (linearMaterial != null)
            {
                if (Application.isPlaying) Object.Destroy(linearMaterial);
                else Object.DestroyImmediate(linearMaterial);
            }
            linearMaterial = null;
            shaderMissing = false;
        }
#endif
    }
}

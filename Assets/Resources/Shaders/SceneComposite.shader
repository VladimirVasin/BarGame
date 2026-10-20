Shader "Hidden/BarPromenade/SceneComposite"
{
    SubShader
    {
        Tags
        {
            "RenderPipeline" = "UniversalPipeline"
            "RenderType" = "Opaque"
        }

        ZWrite Off
        ZTest Always
        Cull Off

        Pass
        {
            Name "NativeComposite"
            HLSLPROGRAM
            #pragma target 3.0
            #pragma vertex Vert
            #pragma fragment FragNative
            #include "SceneComposite.hlsl"

            float4 FragNative(Varyings input) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
                float halfBar = (1.0 - _SceneAspectFraction) * 0.5;
                if (input.texcoord.x < halfBar || input.texcoord.x > 1.0 - halfBar)
                    return float4(0.0, 0.0, 0.0, 1.0);
                float2 targetUv = input.texcoord;
                targetUv.x = (targetUv.x - halfBar) / _SceneAspectFraction;
                float4 color = SampleSceneComposite(targetUv);
                return color;
            }
            ENDHLSL
        }

        Pass
        {
            Name "NativePresentation"
            HLSLPROGRAM
            #pragma target 3.0
            #pragma vertex Vert
            #pragma fragment FragNativePresentation
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.core/Runtime/Utilities/Blit.hlsl"
            float _SceneAspectFraction;

            float4 FragNativePresentation(Varyings input) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
                float halfBar = (1.0 - _SceneAspectFraction) * 0.5;
                if (input.texcoord.x < halfBar || input.texcoord.x > 1.0 - halfBar)
                    return float4(0.0, 0.0, 0.0, 1.0);
                float4 color = SAMPLE_TEXTURE2D_X_LOD(
                    _BlitTexture, sampler_LinearClamp, input.texcoord, 0.0);
                return color;
            }
            ENDHLSL
        }
    }

    Fallback Off
}

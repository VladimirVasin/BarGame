Shader "Hidden/BarPromenade/PS1Composite"
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
            Name "DownsampleRgb555"

            HLSLPROGRAM
            #pragma target 3.0
            #pragma vertex Vert
            #pragma fragment FragDownsample

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.core/Runtime/Utilities/Blit.hlsl"

            float4 _Ps1LowResolutionTexelSize;
            float _Ps1QuantizationStrength;

            float4 FragDownsample(Varyings input) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
                float2 offset =
                    _Ps1LowResolutionTexelSize.xy * 0.25;
                float4 source =
                    SAMPLE_TEXTURE2D_X_LOD(
                        _BlitTexture,
                        sampler_LinearClamp,
                        input.texcoord + float2(-offset.x, -offset.y),
                        0.0) +
                    SAMPLE_TEXTURE2D_X_LOD(
                        _BlitTexture,
                        sampler_LinearClamp,
                        input.texcoord + float2(offset.x, -offset.y),
                        0.0) +
                    SAMPLE_TEXTURE2D_X_LOD(
                        _BlitTexture,
                        sampler_LinearClamp,
                        input.texcoord + float2(-offset.x, offset.y),
                        0.0) +
                    SAMPLE_TEXTURE2D_X_LOD(
                        _BlitTexture,
                        sampler_LinearClamp,
                        input.texcoord + float2(offset.x, offset.y),
                        0.0);
                source *= 0.25;

                float3 boundedLinear = saturate(source.rgb);
                float3 perceptual = LinearToSRGB(boundedLinear);
                float3 quantizedPerceptual =
                    floor(perceptual * 31.0 + 0.5) / 31.0;
                float3 quantizedLinear =
                    SRGBToLinear(quantizedPerceptual);
                source.rgb +=
                    (quantizedLinear - boundedLinear) *
                    saturate(_Ps1QuantizationStrength);
                return source;
            }
            ENDHLSL
        }

        Pass
        {
            Name "PointUpscale"

            HLSLPROGRAM
            #pragma target 3.0
            #pragma vertex Vert
            #pragma fragment FragUpscale

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.core/Runtime/Utilities/Blit.hlsl"

            float4 FragUpscale(Varyings input) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
                return SAMPLE_TEXTURE2D_X_LOD(
                    _BlitTexture,
                    sampler_PointClamp,
                    input.texcoord,
                    0.0);
            }
            ENDHLSL
        }
    }

    Fallback Off
}

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
            float _IntoxicationVignette;
            float _IntoxicationGhostPixels;
            float _IntoxicationWarp;
            float _IntoxicationWarmth;
            float _IntoxicationExposurePulse;
            float _IntoxicationTime;

            float4 FragDownsample(Varyings input) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
                float2 centered = input.texcoord - 0.5;
                float wave =
                    sin(
                        input.texcoord.y * 13.0 +
                        _IntoxicationTime * 1.15) +
                    sin(
                        input.texcoord.x * 8.0 -
                        _IntoxicationTime * 0.73) *
                    0.35;
                float radiusSquared = dot(centered, centered);
                float2 warpedUv =
                    input.texcoord +
                    float2(wave, wave * -0.22) *
                    _IntoxicationWarp +
                    centered *
                    radiusSquared *
                    _IntoxicationWarp *
                    1.8;
                float2 offset =
                    _Ps1LowResolutionTexelSize.xy * 0.25;
                float4 source =
                    SAMPLE_TEXTURE2D_X_LOD(
                        _BlitTexture,
                        sampler_LinearClamp,
                        warpedUv + float2(-offset.x, -offset.y),
                        0.0) +
                    SAMPLE_TEXTURE2D_X_LOD(
                        _BlitTexture,
                        sampler_LinearClamp,
                        warpedUv + float2(offset.x, -offset.y),
                        0.0) +
                    SAMPLE_TEXTURE2D_X_LOD(
                        _BlitTexture,
                        sampler_LinearClamp,
                        warpedUv + float2(-offset.x, offset.y),
                        0.0) +
                    SAMPLE_TEXTURE2D_X_LOD(
                        _BlitTexture,
                        sampler_LinearClamp,
                        warpedUv + float2(offset.x, offset.y),
                        0.0);
                source *= 0.25;

                float2 ghostOffset =
                    _Ps1LowResolutionTexelSize.xy *
                    _IntoxicationGhostPixels *
                    float2(
                        sin(_IntoxicationTime * 0.83),
                        cos(_IntoxicationTime * 1.07) * 0.55);
                float4 ghost =
                    SAMPLE_TEXTURE2D_X_LOD(
                        _BlitTexture,
                        sampler_LinearClamp,
                        warpedUv + ghostOffset,
                        0.0);
                float ghostWeight =
                    saturate(_IntoxicationGhostPixels / 3.0);
                source.rgb = lerp(
                    source.rgb,
                    (source.rgb + ghost.rgb) * 0.5,
                    ghostWeight * 0.38);
                float redSample =
                    SAMPLE_TEXTURE2D_X_LOD(
                        _BlitTexture,
                        sampler_LinearClamp,
                        warpedUv + ghostOffset * 0.7,
                        0.0).r;
                float blueSample =
                    SAMPLE_TEXTURE2D_X_LOD(
                        _BlitTexture,
                        sampler_LinearClamp,
                        warpedUv - ghostOffset * 0.7,
                        0.0).b;
                source.r = lerp(
                    source.r,
                    redSample,
                    ghostWeight * 0.42);
                source.b = lerp(
                    source.b,
                    blueSample,
                    ghostWeight * 0.42);

                source.rgb *=
                    1.0 +
                    sin(_IntoxicationTime * 0.85) *
                    _IntoxicationExposurePulse;
                source.rgb = lerp(
                    source.rgb,
                    source.rgb * float3(1.08, 1.01, 0.94),
                    saturate(_IntoxicationWarmth));
                float edge =
                    saturate((length(centered) - 0.2) / 0.48);
                source.rgb *=
                    1.0 -
                    edge *
                    edge *
                    saturate(_IntoxicationVignette);

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

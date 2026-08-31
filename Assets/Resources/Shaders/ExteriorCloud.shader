Shader "Bar Promenade/Exterior Cloud"
{
    Properties
    {
        [NoScaleOffset] _CloudTex("Packed Cloud Density", 2D) = "gray" {}
        _HazeColor("Haze Color", Color) = (0.330, 0.380, 0.355, 1)
        _CloudShadowColor("Cloud Shadow Color", Color) = (0.285, 0.315, 0.305, 1)
        _CloudLightColor("Cloud Light Color", Color) = (0.405, 0.430, 0.415, 1)
        _Coverage("Coverage", Range(0, 1)) = 0.92
        _EdgeSoftness("Edge Softness", Range(0.01, 0.5)) = 0.18
        _Opacity("Opacity", Range(0, 1)) = 0.94
        _BroadScale("Broad Scale", Range(0.25, 8)) = 1.0
        _DetailScale("Detail Scale", Range(0.25, 12)) = 2.35
        _DetailStrength("Detail Strength", Range(0, 1)) = 0.28
        _ErosionStrength("Erosion Strength", Range(0, 1)) = 0.18
        _BroadPhase("Broad Phase", Vector) = (0, 0, 0, 0)
        _DetailPhase("Detail Phase", Vector) = (0, 0, 0, 0)
        _HorizonFadeStart("Horizon Fade Start", Range(0, 1)) = 0.035
        _HorizonFadeEnd("Horizon Fade End", Range(0, 1)) = 0.22
        _LightningLift("Lightning Lift", Range(0, 1)) = 0
    }

    SubShader
    {
        Tags
        {
            "Queue" = "Transparent-200"
            "RenderType" = "Transparent"
            "RenderPipeline" = "UniversalPipeline"
            "IgnoreProjector" = "True"
        }

        Pass
        {
            Name "ExteriorCloud"
            Tags { "LightMode" = "UniversalForward" }

            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            ZTest LEqual
            Cull Front

            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex CloudVertex
            #pragma fragment CloudFragment
            #pragma multi_compile_instancing

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            TEXTURE2D(_CloudTex);
            SAMPLER(sampler_CloudTex);

            CBUFFER_START(UnityPerMaterial)
                half4 _HazeColor;
                half4 _CloudShadowColor;
                half4 _CloudLightColor;
                half _Coverage;
                half _EdgeSoftness;
                half _Opacity;
                half _BroadScale;
                half _DetailScale;
                half _DetailStrength;
                half _ErosionStrength;
                float4 _BroadPhase;
                float4 _DetailPhase;
                half _HorizonFadeStart;
                half _HorizonFadeEnd;
                half _LightningLift;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float2 uv : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv : TEXCOORD0;
                half domeHeight : TEXCOORD1;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            Varyings CloudVertex(Attributes input)
            {
                Varyings output = (Varyings)0;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);
                output.positionCS = TransformObjectToHClip(
                    input.positionOS.xyz);
                output.uv = input.uv;
                output.domeHeight = saturate(
                    normalize(input.positionOS.xyz).y);
                return output;
            }

            half4 CloudFragment(Varyings input) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);

                float2 broadUv =
                    input.uv * _BroadScale + _BroadPhase.xy;
                float2 detailUv =
                    input.uv * _DetailScale + _DetailPhase.xy;
                half4 broadSample = SAMPLE_TEXTURE2D(
                    _CloudTex,
                    sampler_CloudTex,
                    broadUv);
                half4 detailSample = SAMPLE_TEXTURE2D(
                    _CloudTex,
                    sampler_CloudTex,
                    detailUv);

                half broad = broadSample.r;
                half detail = detailSample.g;
                half erosion = detailSample.b;
                half density = saturate(
                    broad +
                    (detail - 0.5h) * _DetailStrength -
                    (erosion - 0.35h) * _ErosionStrength);
                half threshold = 1.0h - _Coverage;
                half cloudMask = smoothstep(
                    threshold - _EdgeSoftness,
                    threshold + _EdgeSoftness,
                    density);

                half value = saturate(
                    0.30h + broad * 0.54h + detail * 0.16h);
                half3 cloudColor = lerp(
                    _CloudShadowColor.rgb,
                    _CloudLightColor.rgb,
                    value);
                cloudColor = lerp(
                    cloudColor,
                    _CloudLightColor.rgb,
                    saturate(_LightningLift));

                half horizon = smoothstep(
                    min(_HorizonFadeStart, _HorizonFadeEnd - 0.001h),
                    max(_HorizonFadeEnd, _HorizonFadeStart + 0.001h),
                    input.domeHeight);
                half3 color = lerp(
                    _HazeColor.rgb,
                    cloudColor,
                    horizon);
                half alpha = cloudMask * _Opacity * horizon;
                return half4(color, alpha);
            }
            ENDHLSL
        }
    }

    Fallback Off
}

Shader "Bar Promenade/Stairwell Cat Grin"
{
    Properties
    {
        _BaseColor("Teeth Color", Color) = (0.92, 0.90, 0.80, 1)
        _EmissionColor("Emission Color", Color) = (0.42, 0.62, 0.38, 1)
        _EmissionFloor("Emission Floor", Range(0, 1)) = 0.35
        _FeatherArc("Feather Arc", Range(0.001, 0.2)) = 0.06
        _ToothCount("Tooth Count", Float) = 9
        [PerRendererData] _GrinProgress("Grin Progress", Range(0, 1)) = 0
    }

    SubShader
    {
        Tags
        {
            "Queue" = "Transparent"
            "RenderType" = "Transparent"
            "RenderPipeline" = "UniversalPipeline"
            "IgnoreProjector" = "True"
        }

        Pass
        {
            Name "StairwellCatGrin"
            Tags { "LightMode" = "UniversalForward" }

            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite On
            ZTest LEqual
            Cull Back

            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex GrinVertex
            #pragma fragment GrinFragment
            #pragma multi_compile_fog

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half4 _BaseColor;
                half4 _EmissionColor;
                half _EmissionFloor;
                half _FeatherArc;
                half _ToothCount;
                half _GrinProgress;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float2 uv : TEXCOORD0;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv : TEXCOORD0;
                half fogFactor : TEXCOORD1;
            };

            Varyings GrinVertex(Attributes input)
            {
                Varyings output = (Varyings)0;
                float3 positionWS = TransformObjectToWorld(
                    input.positionOS.xyz);
                output.positionCS = TransformWorldToHClip(positionWS);
                output.uv = input.uv;
                output.fogFactor = ComputeFogFactor(
                    output.positionCS.z);
                return output;
            }

            half4 GrinFragment(Varyings input) : SV_Target
            {
                // The mesh bakes normalized arc length into uv.x:
                // 0 at the left tip, 1 at the right, 0.5 at the
                // center of the smile. The reveal therefore grows
                // from the middle outward as _GrinProgress rises
                // and retreats the same way in reverse - the smile
                // is drawn in the air, never faded in.
                half arcDistance = abs((half)input.uv.x - 0.5h);
                half reveal = 0.5h * _GrinProgress;
                half alpha = saturate(
                    (reveal - arcDistance) /
                    max(_FeatherArc, 0.001h));
                if (alpha <= 0.0h)
                {
                    discard;
                }

                // Individual teeth: thin dark seams at regular arc
                // intervals, cheaper and far more legible at PS1
                // resolution than geometric gaps would be.
                half toothPhase = abs(
                    frac((half)input.uv.x * _ToothCount) - 0.5h) *
                    2.0h;
                half toothTint = lerp(
                    0.45h,
                    1.0h,
                    smoothstep(0.0h, 0.22h, toothPhase));

                // The frontier of the drawing stroke glows a touch
                // brighter than the settled enamel behind it.
                half edgeGlow = 1.0h - saturate(
                    (reveal - arcDistance) /
                    (_FeatherArc * 2.0h));
                half3 color =
                    _BaseColor.rgb * toothTint +
                    _EmissionColor.rgb *
                    (_EmissionFloor +
                     (1.0h - _EmissionFloor) * edgeGlow);
                color = MixFog(color, input.fogFactor);
                return half4(color, alpha * _BaseColor.a);
            }
            ENDHLSL
        }
    }

    Fallback Off
}

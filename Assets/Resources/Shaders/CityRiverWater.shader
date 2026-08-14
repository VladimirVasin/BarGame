Shader "Bar Promenade/City River Water"
{
    Properties
    {
        _BaseColor("Base Color", Color) = (0.075, 0.19, 0.205, 1)
        _HighlightColor("Highlight Color", Color) = (0.24, 0.32, 0.29, 1)
        _NightFactor("Night Factor", Range(0, 1)) = 0
        _RainIntensity("Rain Intensity", Range(0, 1)) = 0
        _FlowSpeed("Flow Speed", Range(0, 0.25)) = 0.08
    }

    SubShader
    {
        Tags
        {
            "Queue" = "Geometry"
            "RenderType" = "Opaque"
            "RenderPipeline" = "UniversalPipeline"
        }

        Pass
        {
            Name "RiverWater"
            Tags { "LightMode" = "UniversalForward" }

            ZWrite On
            ZTest LEqual
            Cull Back

            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex RiverVertex
            #pragma fragment RiverFragment
            #pragma multi_compile_fog
            #pragma multi_compile_instancing

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half4 _BaseColor;
                half4 _HighlightColor;
                half _NightFactor;
                half _RainIntensity;
                half _FlowSpeed;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                half fogFactor : TEXCOORD1;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            Varyings RiverVertex(Attributes input)
            {
                Varyings output = (Varyings)0;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);

                VertexPositionInputs positionInputs =
                    GetVertexPositionInputs(input.positionOS.xyz);
                output.positionCS = positionInputs.positionCS;
                output.positionWS = positionInputs.positionWS;
                float viewZ =
                    -TransformWorldToView(positionInputs.positionWS).z;
                output.fogFactor = ComputeFogFactorZ0ToFar(
                    max(viewZ - _ProjectionParams.y, 0));
                return output;
            }

            half4 RiverFragment(Varyings input) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);

                float time = _Time.y * _FlowSpeed;
                half longBand = sin(
                    input.positionWS.z * 0.34 -
                    time +
                    sin(input.positionWS.x * 0.31) * 1.4) *
                    0.5h + 0.5h;
                half crossBand = sin(
                    input.positionWS.x * 1.17 +
                    input.positionWS.z * 0.08 +
                    time * 0.43) *
                    0.5h + 0.5h;
                half rainBreak = sin(
                    input.positionWS.x * 2.9 -
                    input.positionWS.z * 2.3 +
                    time * 4.0) *
                    0.5h + 0.5h;
                half pattern = saturate(
                    longBand * 0.72h +
                    crossBand * 0.20h +
                    rainBreak * _RainIntensity * 0.18h);
                pattern = floor(pattern * 4.0h) * 0.25h;

                half highlight = smoothstep(
                    lerp(0.82h, 0.66h, _RainIntensity),
                    1.0h,
                    pattern);
                half3 color = lerp(
                    _BaseColor.rgb,
                    _HighlightColor.rgb,
                    highlight * lerp(0.34h, 0.56h, _NightFactor));
                color = MixFog(color, input.fogFactor);
                return half4(color, 1.0h);
            }
            ENDHLSL
        }
    }
}

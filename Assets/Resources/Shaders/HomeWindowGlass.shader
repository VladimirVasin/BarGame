Shader "Bar Promenade/Home Window Glass"
{
    Properties
    {
        _BaseColor("Glass Tint", Color) =
            (0.34, 0.48, 0.56, 0.20)
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
            Name "HomeWindowGlass"
            Tags { "LightMode" = "UniversalForward" }

            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            ZTest LEqual
            Cull Back

            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex GlassVertex
            #pragma fragment GlassFragment
            #pragma multi_compile_fog
            #pragma multi_compile_instancing

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half4 _BaseColor;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
                float2 uv : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                half3 normalWS : TEXCOORD1;
                float2 uv : TEXCOORD2;
                half fogFactor : TEXCOORD3;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            Varyings GlassVertex(Attributes input)
            {
                Varyings output = (Varyings)0;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);

                VertexPositionInputs positionInputs =
                    GetVertexPositionInputs(input.positionOS.xyz);
                VertexNormalInputs normalInputs =
                    GetVertexNormalInputs(input.normalOS);
                output.positionCS = positionInputs.positionCS;
                output.positionWS = positionInputs.positionWS;
                output.normalWS = normalInputs.normalWS;
                output.uv = input.uv;
                float viewZ =
                    -TransformWorldToView(positionInputs.positionWS).z;
                output.fogFactor = ComputeFogFactorZ0ToFar(
                    max(viewZ - _ProjectionParams.y, 0));
                return output;
            }

            half4 GlassFragment(Varyings input) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);

                half3 viewDirection =
                    GetWorldSpaceNormalizeViewDir(input.positionWS);
                half facing = saturate(abs(dot(
                    normalize(input.normalWS),
                    viewDirection)));
                half edgeHighlight =
                    pow(1.0h - facing, 2.0h);
                half grime =
                    sin(input.uv.x * 31.0h +
                        input.uv.y * 17.0h) *
                    0.5h + 0.5h;
                half3 color =
                    _BaseColor.rgb *
                    lerp(0.82h, 1.18h, edgeHighlight);
                color = MixFog(color, input.fogFactor);
                half alpha =
                    saturate(
                        _BaseColor.a +
                        edgeHighlight * 0.10h +
                        grime * 0.025h);
                return half4(color, alpha);
            }
            ENDHLSL
        }
    }

    Fallback Off
}

Shader "Bar Promenade/Bar Drink Glass"
{
    Properties
    {
        _BaseColor("Glass Tint", Color) = (0.62, 0.82, 0.86, 0.24)
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
            Name "BarDrinkGlass"
            Tags { "LightMode" = "UniversalForward" }

            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            ZTest LEqual
            Cull Off

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
                VertexPositionInputs positions =
                    GetVertexPositionInputs(input.positionOS.xyz);
                VertexNormalInputs normals =
                    GetVertexNormalInputs(input.normalOS);
                output.positionCS = positions.positionCS;
                output.positionWS = positions.positionWS;
                output.normalWS = normals.normalWS;
                output.uv = input.uv;
                float viewZ = -TransformWorldToView(positions.positionWS).z;
                output.fogFactor = ComputeFogFactorZ0ToFar(
                    max(viewZ - _ProjectionParams.y, 0));
                return output;
            }

            half4 GlassFragment(Varyings input) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
                half3 viewDirection =
                    GetWorldSpaceNormalizeViewDir(input.positionWS);
                half edge = pow(
                    1.0h - saturate(abs(dot(
                        normalize(input.normalWS),
                        viewDirection))),
                    2.0h);
                half facet = step(0.54h, frac(input.uv.x * 8.0h));
                half3 color = _BaseColor.rgb *
                    lerp(0.72h, 1.42h, edge) *
                    lerp(0.94h, 1.04h, facet);
                color = MixFog(color, input.fogFactor);
                half alpha = saturate(_BaseColor.a + edge * 0.20h);
                return half4(color, alpha);
            }
            ENDHLSL
        }
    }

    Fallback Off
}

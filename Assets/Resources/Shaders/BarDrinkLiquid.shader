Shader "Bar Promenade/Bar Drink Liquid"
{
    Properties
    {
        _BaseMap("Surface Albedo", 2D) = "white" {}
        _BaseColor("Liquid Color", Color) = (0.72, 0.30, 0.08, 0.88)
        _SurfaceAlbedoCompensation("Surface Albedo Compensation", Float) = 1.0
    }

    SubShader
    {
        Tags
        {
            "Queue" = "Transparent-10"
            "RenderType" = "Transparent"
            "RenderPipeline" = "UniversalPipeline"
            "IgnoreProjector" = "True"
        }

        Pass
        {
            Name "BarDrinkLiquid"
            Tags { "LightMode" = "UniversalForward" }

            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite On
            ZTest LEqual
            Cull Back

            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex LiquidVertex
            #pragma fragment LiquidFragment
            #pragma multi_compile_fog
            #pragma multi_compile_instancing

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            TEXTURE2D(_BaseMap);
            SAMPLER(sampler_BaseMap);

            CBUFFER_START(UnityPerMaterial)
                float4 _BaseMap_ST;
                half4 _BaseColor;
                half _SurfaceAlbedoCompensation;
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

            Varyings LiquidVertex(Attributes input)
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
                output.uv = TRANSFORM_TEX(input.uv, _BaseMap);
                float viewZ = -TransformWorldToView(positions.positionWS).z;
                output.fogFactor = ComputeFogFactorZ0ToFar(
                    max(viewZ - _ProjectionParams.y, 0));
                return output;
            }

            half4 LiquidFragment(Varyings input) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
                half3 normal = normalize(input.normalWS);
                half top = saturate(normal.y * 0.5h + 0.5h);
                half sideShade = 0.76h +
                    0.24h * abs(dot(normal, half3(0.44h, 0.31h, 0.84h)));
                half3 surface = saturate(
                    SAMPLE_TEXTURE2D(
                        _BaseMap,
                        sampler_BaseMap,
                        input.uv).rgb *
                    _SurfaceAlbedoCompensation);
                half3 texturedTint = _BaseColor.rgb * surface;
                half3 color = texturedTint * sideShade;
                color += top * texturedTint * 0.16h;
                color = MixFog(color, input.fogFactor);
                return half4(color, _BaseColor.a);
            }
            ENDHLSL
        }
    }

    Fallback Off
}

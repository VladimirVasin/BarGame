Shader "Bar Promenade/City Building Window Slots"
{
    Properties
    {
        _OffColor("Unlit Glass", Color) = (0.025, 0.035, 0.04, 1)
        _DayColor("Day Glass", Color) = (0.045, 0.055, 0.062, 1)
        _ColdColor("Cold Light", Color) = (0.48, 0.72, 0.82, 1)
        _WarmColor("Warm Light", Color) = (0.95, 0.62, 0.30, 1)
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
            Name "ForwardLit"
            Tags { "LightMode" = "UniversalForward" }

            Cull Back
            ZWrite On
            ZTest LEqual

            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex WindowVertex
            #pragma fragment WindowFragment
            #pragma multi_compile_instancing
            #pragma multi_compile_fog
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile_fragment _ _SHADOWS_SOFT _SHADOWS_SOFT_LOW _SHADOWS_SOFT_MEDIUM _SHADOWS_SOFT_HIGH
            #pragma multi_compile _ _ADDITIONAL_LIGHTS_VERTEX _ADDITIONAL_LIGHTS
            #pragma multi_compile_fragment _ _ADDITIONAL_LIGHT_SHADOWS

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            #include "Ps1VertexJitter.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half4 _OffColor;
                half4 _DayColor;
                half4 _ColdColor;
                half4 _WarmColor;
            CBUFFER_END

            float _CityWindowNightFactor;
            float _CityBuildingWindowStates[64];
            #define CITY_WINDOW_SLOT_DIVISOR 256.0

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
                float2 slotUv : TEXCOORD1;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                half3 normalWS : TEXCOORD1;
                half fogFactor : TEXCOORD2;
                nointerpolation float encodedState : TEXCOORD3;
                UNITY_VERTEX_INPUT_INSTANCE_ID
                UNITY_VERTEX_OUTPUT_STEREO
            };

            Varyings WindowVertex(Attributes input)
            {
                Varyings output = (Varyings)0;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_TRANSFER_INSTANCE_ID(input, output);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);

                VertexPositionInputs positionInputs =
                    GetVertexPositionInputs(input.positionOS.xyz);
                VertexNormalInputs normalInputs =
                    GetVertexNormalInputs(input.normalOS);
                int slot = clamp(
                    (int)floor(
                        input.slotUv.x * CITY_WINDOW_SLOT_DIVISOR),
                    0,
                    63);
                output.positionCS = Ps1SnapClipPosition(
                    positionInputs.positionCS);
                output.positionWS = positionInputs.positionWS;
                output.normalWS = normalInputs.normalWS;
                output.fogFactor = ComputeFogFactor(
                    positionInputs.positionCS.z);
                output.encodedState = _CityBuildingWindowStates[slot];
                return output;
            }

            half4 WindowFragment(Varyings input) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);

                int encoded = (int)round(input.encodedState);
                int family = encoded / 4;
                int variant = encoded - family * 4;
                half variantFactor =
                    0.88h + ((half)variant) * 0.04h;
                half night = saturate(_CityWindowNightFactor);
                half3 litColor = family == 1
                    ? _ColdColor.rgb
                    : _WarmColor.rgb;
                half3 glass = family == 0
                    ? _OffColor.rgb * variantFactor
                    : lerp(
                        _DayColor.rgb,
                        litColor * variantFactor,
                        night);

                InputData inputData = (InputData)0;
                inputData.positionWS = input.positionWS;
                inputData.positionCS = input.positionCS;
                inputData.normalWS = NormalizeNormalPerPixel(
                    input.normalWS);
                inputData.viewDirectionWS =
                    GetWorldSpaceNormalizeViewDir(input.positionWS);
                inputData.shadowCoord =
                    TransformWorldToShadowCoord(input.positionWS);
                inputData.fogCoord = input.fogFactor;
                inputData.vertexLighting = half3(0.0h, 0.0h, 0.0h);
                inputData.bakedGI = SampleSH(inputData.normalWS);
                inputData.normalizedScreenSpaceUV =
                    GetNormalizedScreenSpaceUV(input.positionCS);
                inputData.shadowMask = half4(1.0h, 1.0h, 1.0h, 1.0h);

                SurfaceData surface = (SurfaceData)0;
                surface.albedo = glass;
                surface.specular = half3(0.28h, 0.28h, 0.28h);
                surface.metallic = 0.05h;
                surface.smoothness = 0.62h;
                surface.normalTS = half3(0.0h, 0.0h, 1.0h);
                surface.emission = family == 0
                    ? half3(0.0h, 0.0h, 0.0h)
                    : litColor * (night * 0.48h);
                surface.occlusion = 1.0h;
                surface.alpha = 1.0h;

                half4 color = UniversalFragmentPBR(inputData, surface);
                color.rgb = MixFog(color.rgb, inputData.fogCoord);
                color.a = 1.0h;
                return color;
            }
            ENDHLSL
        }
    }

    FallBack Off
}

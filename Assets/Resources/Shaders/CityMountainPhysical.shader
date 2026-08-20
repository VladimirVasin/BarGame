Shader "Bar Promenade/City Mountain Physical"
{
    Properties
    {
        _BaseMap("Rock Albedo", 2D) = "white" {}
        _BaseColor("Rock Tint", Color) = (1, 1, 1, 1)
        _HazeColor("City Haze Color", Color) = (0.330, 0.380, 0.355, 1)
        _FogDensity("City Fog Density", Float) = 0.070
        _VisibilityFloor("Distant Visibility Floor", Range(0, 1)) = 0.55
        _NativeFogNear("Native Fog Near", Float) = 9
        _NativeFogFar("Native Fog Far", Float) = 12
        _HandoffNear("Opaque Handoff Near", Float) = 31
        _HandoffFar("Opaque Handoff Far", Float) = 43
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
            Name "MountainPhysical"
            Tags { "LightMode" = "UniversalForward" }

            Cull Back
            ZWrite On
            ZTest LEqual

            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex MountainVertex
            #pragma fragment MountainFragment
            #pragma multi_compile_instancing
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile_fragment _ _SHADOWS_SOFT _SHADOWS_SOFT_LOW _SHADOWS_SOFT_MEDIUM _SHADOWS_SOFT_HIGH

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            TEXTURE2D(_BaseMap);
            SAMPLER(sampler_BaseMap);

            CBUFFER_START(UnityPerMaterial)
                float4 _BaseMap_ST;
                half4 _BaseColor;
                half4 _HazeColor;
                half _FogDensity;
                half _VisibilityFloor;
                half _NativeFogNear;
                half _NativeFogFar;
                half _HandoffNear;
                half _HandoffFar;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                half3 normalOS : NORMAL;
                float2 uv : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                half3 normalWS : TEXCOORD1;
                float2 uv : TEXCOORD2;
                float4 shadowCoord : TEXCOORD3;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            Varyings MountainVertex(Attributes input)
            {
                Varyings output = (Varyings)0;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);

                VertexPositionInputs positionInputs =
                    GetVertexPositionInputs(input.positionOS.xyz);
                output.positionCS = positionInputs.positionCS;
                output.positionWS = positionInputs.positionWS;
                output.normalWS = TransformObjectToWorldNormal(
                    input.normalOS);
                output.uv = TRANSFORM_TEX(input.uv, _BaseMap);
                output.shadowCoord = GetShadowCoord(positionInputs);
                return output;
            }

            half QuantizedInterleavedNoise(float2 pixelPosition)
            {
                float noise = frac(
                    52.9829189 *
                    frac(dot(
                        floor(pixelPosition),
                        float2(0.06711056, 0.00583715))));
                return (floor(noise * 16.0) + 0.5) / 16.0;
            }

            half4 MountainFragment(Varyings input) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);

                float cameraDistance = distance(
                    _WorldSpaceCameraPos,
                    input.positionWS);
                float horizontalDistance = distance(
                    GetCameraPositionWS().xz,
                    input.positionWS.xz);
                half physicalCoverage = 1.0h - smoothstep(
                    _HandoffNear,
                    _HandoffFar,
                    horizontalDistance);
                half dither = QuantizedInterleavedNoise(
                    input.positionCS.xy);
                clip(physicalCoverage - dither);

                half4 sample = SAMPLE_TEXTURE2D(
                    _BaseMap,
                    sampler_BaseMap,
                    input.uv);
                half3 albedo = sample.rgb * _BaseColor.rgb;
                half3 normalWS = NormalizeNormalPerPixel(input.normalWS);
                Light mainLight = GetMainLight(input.shadowCoord);
                half lambert = saturate(dot(
                    normalWS,
                    mainLight.direction));
                half3 ambient = max(
                    SampleSH(normalWS),
                    half3(0.055h, 0.060h, 0.057h));
                half3 direct = mainLight.color *
                    lambert *
                    mainLight.distanceAttenuation *
                    mainLight.shadowAttenuation;
                half3 litColor = albedo * max(
                    ambient + direct,
                    half3(0.12h, 0.12h, 0.12h));

                half fogTerm = _FogDensity * cameraDistance;
                half nativeVisibility = exp2(
                    -1.442695h * fogTerm * fogTerm);
                half floorBlend = smoothstep(
                    _NativeFogNear,
                    _NativeFogFar,
                    cameraDistance);
                half visibility = lerp(
                    nativeVisibility,
                    max(nativeVisibility, _VisibilityFloor),
                    floorBlend);
                half3 color = lerp(
                    _HazeColor.rgb,
                    litColor,
                    visibility);
                return half4(color, 1.0h);
            }
            ENDHLSL
        }

        Pass
        {
            Name "DepthOnly"
            Tags { "LightMode" = "DepthOnly" }

            Cull Back
            ZWrite On
            ColorMask R

            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex MountainDepthVertex
            #pragma fragment MountainDepthFragment
            #pragma multi_compile_instancing

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                float4 _BaseMap_ST;
                half4 _BaseColor;
                half4 _HazeColor;
                half _FogDensity;
                half _VisibilityFloor;
                half _NativeFogNear;
                half _NativeFogFar;
                half _HandoffNear;
                half _HandoffFar;
            CBUFFER_END

            struct MountainDepthAttributes
            {
                float4 positionOS : POSITION;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct MountainDepthVaryings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            MountainDepthVaryings MountainDepthVertex(
                MountainDepthAttributes input)
            {
                MountainDepthVaryings output =
                    (MountainDepthVaryings)0;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);
                VertexPositionInputs positionInputs =
                    GetVertexPositionInputs(input.positionOS.xyz);
                output.positionCS = positionInputs.positionCS;
                output.positionWS = positionInputs.positionWS;
                return output;
            }

            half MountainDepthNoise(float2 pixelPosition)
            {
                float noise = frac(
                    52.9829189 *
                    frac(dot(
                        floor(pixelPosition),
                        float2(0.06711056, 0.00583715))));
                return (floor(noise * 16.0) + 0.5) / 16.0;
            }

            half MountainDepthFragment(
                MountainDepthVaryings input) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
                float horizontalDistance = distance(
                    GetCameraPositionWS().xz,
                    input.positionWS.xz);
                half physicalCoverage = 1.0h - smoothstep(
                    _HandoffNear,
                    _HandoffFar,
                    horizontalDistance);
                clip(
                    physicalCoverage -
                    MountainDepthNoise(input.positionCS.xy));
                return input.positionCS.z;
            }
            ENDHLSL
        }

        Pass
        {
            Name "DepthNormalsOnly"
            Tags { "LightMode" = "DepthNormalsOnly" }

            Cull Back
            ZWrite On

            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex MountainDepthNormalsVertex
            #pragma fragment MountainDepthNormalsFragment
            #pragma multi_compile_fragment _ _GBUFFER_NORMALS_OCT
            #pragma multi_compile_instancing

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                float4 _BaseMap_ST;
                half4 _BaseColor;
                half4 _HazeColor;
                half _FogDensity;
                half _VisibilityFloor;
                half _NativeFogNear;
                half _NativeFogFar;
                half _HandoffNear;
                half _HandoffFar;
            CBUFFER_END

            struct MountainDepthNormalsAttributes
            {
                float4 positionOS : POSITION;
                half3 normalOS : NORMAL;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct MountainDepthNormalsVaryings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                half3 normalWS : TEXCOORD1;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            MountainDepthNormalsVaryings MountainDepthNormalsVertex(
                MountainDepthNormalsAttributes input)
            {
                MountainDepthNormalsVaryings output =
                    (MountainDepthNormalsVaryings)0;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);
                VertexPositionInputs positionInputs =
                    GetVertexPositionInputs(input.positionOS.xyz);
                output.positionCS = positionInputs.positionCS;
                output.positionWS = positionInputs.positionWS;
                output.normalWS = TransformObjectToWorldNormal(
                    input.normalOS);
                return output;
            }

            half MountainDepthNormalsNoise(float2 pixelPosition)
            {
                float noise = frac(
                    52.9829189 *
                    frac(dot(
                        floor(pixelPosition),
                        float2(0.06711056, 0.00583715))));
                return (floor(noise * 16.0) + 0.5) / 16.0;
            }

            half4 MountainDepthNormalsFragment(
                MountainDepthNormalsVaryings input) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
                float horizontalDistance = distance(
                    GetCameraPositionWS().xz,
                    input.positionWS.xz);
                half physicalCoverage = 1.0h - smoothstep(
                    _HandoffNear,
                    _HandoffFar,
                    horizontalDistance);
                clip(
                    physicalCoverage -
                    MountainDepthNormalsNoise(input.positionCS.xy));

                float3 normalWS = normalize(input.normalWS);
                #if defined(_GBUFFER_NORMALS_OCT)
                    float2 octNormalWS = PackNormalOctQuadEncode(normalWS);
                    float2 remappedOctNormalWS = saturate(
                        octNormalWS * 0.5 + 0.5);
                    half3 packedNormalWS = half3(
                        PackFloat2To888(remappedOctNormalWS));
                    return half4(packedNormalWS, 0.0h);
                #else
                    return half4(
                        NormalizeNormalPerPixel(input.normalWS),
                        0.0h);
                #endif
            }
            ENDHLSL
        }
    }

    Fallback Off
}

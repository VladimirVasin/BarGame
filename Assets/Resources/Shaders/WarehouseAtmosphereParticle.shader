Shader "Bar Promenade/Warehouse Atmosphere Particle"
{
    Properties
    {
        [MainColor] _BaseColor("Dust Tint", Color) = (1, 1, 1, 1)
        _EdgePower("Soft Edge Power", Range(0.5, 6)) = 1.1
        _SoftParticleDistance("Depth Fade Distance", Range(0.01, 1)) = 0.18
        _LightResponse("Direct Light Response", Range(0.1, 4)) = 1.3
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
            Name "WarehouseAtmosphere"
            Tags { "LightMode" = "UniversalForward" }

            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            ZTest LEqual
            Cull Off

            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex AtmosphereVertex
            #pragma fragment AtmosphereFragment
            #pragma multi_compile_instancing
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile _ _ADDITIONAL_LIGHTS_VERTEX _ADDITIONAL_LIGHTS
            #pragma multi_compile_fragment _ _ADDITIONAL_LIGHT_SHADOWS
            #pragma multi_compile_fragment _ _SHADOWS_SOFT _SHADOWS_SOFT_LOW _SHADOWS_SOFT_MEDIUM _SHADOWS_SOFT_HIGH
            #pragma multi_compile_fragment _ _LIGHT_COOKIES
            #pragma multi_compile _ _LIGHT_LAYERS
            #pragma multi_compile _ _CLUSTER_LIGHT_LOOP

            // Transparent receivers sample the actual shadow map at the mote,
            // never the opaque surface behind it in screen-space shadows.
            #define _SURFACE_TYPE_TRANSPARENT 1
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/RealtimeLights.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half4 _BaseColor;
                half _EdgePower;
                half _SoftParticleDistance;
                half _LightResponse;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                half4 color : COLOR;
                float2 uv : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                float2 uv : TEXCOORD1;
                float eyeDepth : TEXCOORD2;
                half4 color : COLOR;
                UNITY_VERTEX_INPUT_INSTANCE_ID
                UNITY_VERTEX_OUTPUT_STEREO
            };

            Varyings AtmosphereVertex(Attributes input)
            {
                Varyings output = (Varyings)0;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_TRANSFER_INSTANCE_ID(input, output);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);

                VertexPositionInputs positionInputs =
                    GetVertexPositionInputs(input.positionOS.xyz);
                output.positionCS = positionInputs.positionCS;
                output.positionWS = positionInputs.positionWS;
                output.eyeDepth = max(
                    -TransformWorldToView(positionInputs.positionWS).z, 0);
                output.uv = input.uv;
                output.color = input.color * _BaseColor;
                return output;
            }

            half DustShape(float2 uv)
            {
                // A fixed asymmetric, softly broken flake. Particle size and
                // rotation supply variation; there is no animated shader noise
                // to keep running while the atmosphere owner is paused.
                half2 p = (half2)uv * 2.0h - 1.0h;
                half2 warped = p + half2(
                    sin(p.y * 5.1h + p.x * 2.3h),
                    sin(p.x * 4.7h - p.y * 3.2h)) * 0.09h;
                half radius = length(warped * half2(1.08h, 1.0h));
                half envelope = smoothstep(0.0h, 1.0h,
                    saturate(1.0h - radius));
                half grain = 0.78h + 0.22h *
                    sin(p.x * 8.3h + p.y * 3.7h) *
                    sin(p.y * 6.1h - p.x * 2.9h);
                return pow(envelope, _EdgePower) * grain;
            }

            float3 DustLight(Light light, uint renderingLayers)
            {
                #ifdef _LIGHT_LAYERS
                    if ((light.layerMask & renderingLayers) == 0u)
                        return half3(0, 0, 0);
                #endif

                // A tiny suspended flake scatters from either side. A camera
                // billboard normal would wrongly extinguish the chest beam
                // whenever its source and viewer face the same direction.
                return (float3)light.color *
                    (light.distanceAttenuation * light.shadowAttenuation);
            }

            half4 AtmosphereFragment(Varyings input) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);

                half shape = DustShape(input.uv);
                clip(shape * input.color.a - 0.0005h);

                InputData inputData = (InputData)0;
                inputData.positionWS = input.positionWS;
                inputData.normalizedScreenSpaceUV =
                    GetNormalizedScreenSpaceUV(input.positionCS);
                uint renderingLayers = GetMeshRenderingLayer();
                const half4 shadowMask = half4(1, 1, 1, 1);

                Light mainLight = GetMainLight(
                    TransformWorldToShadowCoord(input.positionWS),
                    input.positionWS, shadowMask);
                float3 illumination = DustLight(mainLight, renderingLayers);

                #if defined(_ADDITIONAL_LIGHTS)
                    uint lightCount = GetAdditionalLightsCount();
                    #if USE_CLUSTER_LIGHT_LOOP
                        [loop] for (uint lightIndex = 0u;
                            lightIndex < min(URP_FP_DIRECTIONAL_LIGHTS_COUNT,
                                MAX_VISIBLE_LIGHTS); ++lightIndex)
                        {
                            Light light = GetAdditionalLight(lightIndex,
                                input.positionWS, shadowMask);
                            illumination += DustLight(light, renderingLayers);
                        }
                    #endif

                    // URP's bounded per-pixel cluster list includes the real
                    // lamps, fan light and chest Spot, with their own shadows.
                    LIGHT_LOOP_BEGIN(lightCount)
                        Light light = GetAdditionalLight(lightIndex,
                            input.positionWS, shadowMask);
                        illumination += DustLight(light, renderingLayers);
                    LIGHT_LOOP_END
                #endif

                // No SH, ambient, emission or fog tint: lightless dust also
                // loses alpha, so blackout cannot leave glowing or dark cards.
                // Keep irradiance in float until compression: a card very
                // near a bulb can exceed half precision before attenuation.
                illumination = max(illumination * _LightResponse, 0.0);
                float brightness = max(illumination.r,
                    max(illumination.g, illumination.b));
                half lightVisibility = brightness / (brightness + 0.035h);
                half3 litTint = input.color.rgb *
                    illumination / (brightness + 0.25h);

                float rawDepth = SampleSceneDepth(
                    inputData.normalizedScreenSpaceUV);
                float sceneEyeDepth =
                    LinearEyeDepth(rawDepth, _ZBufferParams);
                half depthFade = saturate(
                    (sceneEyeDepth - input.eyeDepth) /
                    max(_SoftParticleDistance, 0.01h));
                half nearFade = saturate((input.eyeDepth - 0.2) / 0.45);
                half alpha = input.color.a * shape * depthFade * nearFade *
                    lightVisibility;
                return half4(litTint, alpha);
            }
            ENDHLSL
        }
    }

    Fallback Off
}

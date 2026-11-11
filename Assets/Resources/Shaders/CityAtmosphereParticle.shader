Shader "Bar Promenade/City Atmosphere Particle"
{
    Properties
    {
        [HDR] _BaseColor("Base Color", Color) = (1, 1, 1, 1)
        _EdgePower("Edge Power", Range(0.5, 6)) = 1.5
        _NoiseStrength("Noise Strength", Range(0, 1)) = 0
        _SoftParticleDistance("Depth Fade Distance", Range(0.01, 4)) = 1
        _FogMotion("City Layer Motion", Vector) = (0, 0, 0, 0)
        _ReceiveCitySkyFill("Weather receives City sky fill", Float) = 0
        [HideInInspector] _RainContactFade("Near rain centre and range", Vector) = (0, 0, 0, 0)
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
            Name "CityAtmosphere"
            Tags { "LightMode" = "UniversalForward" }

            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            ZTest LEqual
            Cull Off

            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex AtmosphereVertex
            #pragma fragment AtmosphereFragment
            #pragma multi_compile_fog
            #pragma multi_compile_instancing
            #pragma multi_compile_fragment _ _ADDITIONAL_LIGHT_SHADOWS
            #pragma multi_compile_fragment _ _SHADOWS_SOFT _SHADOWS_SOFT_LOW _SHADOWS_SOFT_MEDIUM _SHADOWS_SOFT_HIGH
            #pragma multi_compile _ _CLUSTER_LIGHT_LOOP

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"
            #define _SURFACE_TYPE_TRANSPARENT 1
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/RealtimeLights.hlsl"
            #define BAR_PROMENADE_LOCAL_LIGHT_SHADOWS 1
            #include "AlpineVillageLocalLighting.hlsl"
            #include "CitySkyFill.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half4 _BaseColor;
                half _EdgePower;
                half _NoiseStrength;
                half _SoftParticleDistance;
                float4 _FogMotion;
                half _ReceiveCitySkyFill;
                float4 _RainContactFade;
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
                half4 color : COLOR;
                float2 uv : TEXCOORD0;
                half fogFactor : TEXCOORD1;
                float eyeDepth : TEXCOORD2;
                half3 localLighting : TEXCOORD3;
                float3 positionWS : TEXCOORD4;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            Varyings AtmosphereVertex(Attributes input)
            {
                Varyings output = (Varyings)0;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);

                VertexPositionInputs positionInputs =
                    GetVertexPositionInputs(input.positionOS.xyz);
                output.positionCS = positionInputs.positionCS;
                output.uv = input.uv;
                output.color = input.color * _BaseColor;
                output.positionWS = positionInputs.positionWS;
                // Preserve the accepted village vertex path. Structural/road
                // sources use exact particle fragments, not interpolated shadows.
                output.localLighting = _AlpineLocalLightingPerPixel != 0 ? 0 :
                    AlpineLocalParticleLighting(positionInputs.positionWS, false);

                float viewZ =
                    -TransformWorldToView(positionInputs.positionWS).z;
                output.eyeDepth = max(viewZ, 0);
                float nearToFarZ =
                    max(viewZ - _ProjectionParams.y, 0);
                output.fogFactor =
                    ComputeFogFactorZ0ToFar(nearToFarZ);
                return output;
            }

            half4 AtmosphereFragment(Varyings input) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);

                half2 centered = input.uv * 2.0h - 1.0h;
                half radius = length(centered);
                half envelope = saturate(1.0h - radius);
                envelope = smoothstep(0.0h, 1.0h, envelope);
                envelope = pow(envelope, _EdgePower);

                half2 flow = input.uv + _FogMotion.xy * (.18h * _FogMotion.z);
                half firstWave =
                    sin(flow.x * 13.1h +
                        sin(flow.y * 7.3h) * 2.2h) *
                    0.5h + 0.5h;
                half secondWave =
                    sin(flow.y * 15.7h -
                        flow.x * 5.1h) *
                    0.5h + 0.5h;
                half noise =
                    saturate(0.46h + (firstWave + secondWave) * 0.27h);
                half shape =
                    envelope * lerp(1.0h, noise, _NoiseStrength);

                float2 screenUv =
                    GetNormalizedScreenSpaceUV(input.positionCS);
                float rawDepth = SampleSceneDepth(screenUv);
                float sceneEyeDepth =
                    LinearEyeDepth(rawDepth, _ZBufferParams);
                half depthFade = saturate(
                    (sceneEyeDepth - input.eyeDepth) /
                    max(_SoftParticleDistance, 0.01h));

                half alpha = input.color.a * shape * depthFade * (1.0h - _FogMotion.w);
                if (_RainContactFade.w > 0.0)
                {
                    float edge = smoothstep(_RainContactFade.w * .625, _RainContactFade.w,
                        distance(input.positionWS.xz, _RainContactFade.xz));
                    float above = smoothstep(3.2, 6.5, input.positionWS.y - _RainContactFade.y);
                    alpha *= max(edge, above);
                }
                half3 lighting = _AlpineLocalLightingPerPixel != 0 ?
                    AlpineLocalParticleLighting(input.positionWS, true) : input.localLighting;
                // Weather carries sky scatter and a lamp still lights it locally.
                // Emissive bulb halos sharing this material preserve their energy.
                half ambient = 1.0h - _ReceiveCitySkyFill * _CitySkyFillLoss;
                half3 color = MixFog(input.color.rgb * (ambient + lighting), input.fogFactor);
                return half4(color, alpha);
            }
            ENDHLSL
        }
    }
}

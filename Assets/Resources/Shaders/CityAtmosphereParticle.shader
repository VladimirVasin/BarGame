Shader "Bar Promenade/City Atmosphere Particle"
{
    Properties
    {
        [HDR] _BaseColor("Base Color", Color) = (1, 1, 1, 1)
        _EdgePower("Edge Power", Range(0.5, 6)) = 1.5
        _NoiseStrength("Noise Strength", Range(0, 1)) = 0
        _SoftParticleDistance("Depth Fade Distance", Range(0.01, 4)) = 1
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

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half4 _BaseColor;
                half _EdgePower;
                half _NoiseStrength;
                half _SoftParticleDistance;
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

                half firstWave =
                    sin(input.uv.x * 13.1h +
                        sin(input.uv.y * 7.3h) * 2.2h) *
                    0.5h + 0.5h;
                half secondWave =
                    sin(input.uv.y * 15.7h -
                        input.uv.x * 5.1h) *
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

                half alpha = input.color.a * shape * depthFade;
                half3 color =
                    MixFog(input.color.rgb, input.fogFactor);
                return half4(color, alpha);
            }
            ENDHLSL
        }
    }
}

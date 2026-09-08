// UV0 remains the house's Fire atlas sheet. UV1 is per-tongue width/height;
// vertex colour carries stable tongue phase, heat variation and layer index.
Shader "BarPromenade/MothersHouseFlame"
{
    Properties
    {
        _BaseMap("House Fire Atlas", 2D) = "white" {}
        [HDR] _BaseColor("Authored Tint", Color) = (1, 1, 1, 1)
        [HDR] _EmissionColor("Thermal Multiplier", Color) = (1, 0.96, 0.88, 1)
        _FireTime("Owned Fire Time", Float) = 0
        _FirePhase("Layer Phase", Float) = 0
        _FireStrength("Combustion Strength", Float) = 1
        _FireSway("Tip Sway In Metres", Range(0, 0.05)) = 0.032
        _Opacity("Flame Opacity", Range(0, 1)) = 0.84
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
            Name "HearthFlame"
            Tags { "LightMode" = "UniversalForward" }
            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            ZTest LEqual
            Cull Back

            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex FlameVertex
            #pragma fragment FlameFragment
            #pragma multi_compile_fog
            #pragma multi_compile_instancing
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            TEXTURE2D(_BaseMap);
            SAMPLER(sampler_BaseMap);
            CBUFFER_START(UnityPerMaterial)
                float4 _BaseMap_ST;
                half4 _BaseColor;
                half4 _EmissionColor;
                float _FireTime;
                float _FirePhase;
                half _FireStrength;
                float _FireSway;
                half _Opacity;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float2 uv : TEXCOORD0;
                float2 tongueUV : TEXCOORD1;
                half4 variation : COLOR;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };
            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 atlasUV : TEXCOORD0;
                float2 tongueUV : TEXCOORD1;
                float2 phaseAndHeat : TEXCOORD2;
                half fogFactor : TEXCOORD3;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            float FireHash(float2 p)
            {
                return frac(sin(dot(p, float2(127.1, 311.7))) * 43758.5453);
            }

            float FireNoise(float2 p)
            {
                float2 cell = floor(p);
                float2 f = frac(p);
                f = f * f * (3.0 - 2.0 * f);
                return lerp(lerp(FireHash(cell), FireHash(cell + float2(1, 0)), f.x),
                    lerp(FireHash(cell + float2(0, 1)), FireHash(cell + float2(1, 1)), f.x), f.y);
            }

            Varyings FlameVertex(Attributes input)
            {
                Varyings output = (Varyings)0;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);
                float height = saturate(input.tongueUV.y);
                float phase = _FirePhase + input.variation.r * 19.37 + input.variation.b * 2.41;
                // Different rising eddies carry each tongue. Smooth noise
                // changes its direction and tempo without a pendulum beat.
                float flowTime = _FireTime * (3.8 + input.variation.g * 1.8);
                float curl = (FireNoise(float2(phase + 11.2,
                    height * 2.1 - flowTime * 0.43)) - 0.5) * 0.9;
                float bend = (FireNoise(float2(phase + curl,
                    height * 3.1 - flowTime)) * 2.0 - 1.0) * 0.72 +
                    (FireNoise(float2(phase * 1.37 - 13.7,
                    height * 6.8 - flowTime * 1.73)) * 2.0 - 1.0) * 0.28;
                float twist = FireNoise(float2(phase * 1.73 + curl + 23.1,
                    height * 4.7 - flowTime * 1.31)) * 2.0 - 1.0;
                float lift = FireNoise(float2(phase + 57.1,
                    height * 2.7 - flowTime * 0.93)) * 2.0 - 1.0;
                float lick = FireNoise(float2(phase * 1.63 + 91.3,
                    height * 6.7 - flowTime * 1.79)) * 2.0 - 1.0;
                float3 positionWS = TransformObjectToWorld(input.positionOS.xyz);
                // Deform in metres after the FBX scale. The first section
                // never moves: every tongue continues to rise from its log.
                float tip = height * height;
                positionWS.x += tip * _FireSway * bend;
                positionWS.z += tip * _FireSway * 0.55 * twist;
                positionWS.y += height * 0.025 * lift + tip * 0.015 * lick;
                output.positionCS = TransformWorldToHClip(positionWS);
                output.atlasUV = input.uv * _BaseMap_ST.xy + _BaseMap_ST.zw;
                output.tongueUV = input.tongueUV;
                output.phaseAndHeat = float2(phase, input.variation.g);
                output.fogFactor = ComputeFogFactor(output.positionCS.z);
                return output;
            }

            half4 FlameFragment(Varyings input) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
                float height = saturate(input.tongueUV.y);
                float across = input.tongueUV.x * 2.0 - 1.0;
                float phase = input.phaseAndHeat.x;
                // Smooth irregular heat moves upward through every tongue;
                // it is not a flashing opacity or an independent particle.
                float flowTime = _FireTime * (3.6 + input.phaseAndHeat.y * 1.8);
                float curl = (FireNoise(float2(phase + 7.9,
                    height * 2.1 - flowTime * 0.47)) - 0.5) * 0.8;
                float flow = FireNoise(float2(across * 1.9 + phase + curl,
                    height * 4.3 - flowTime));
                float fine = FireNoise(float2(across * 4.1 - phase * 0.7 + curl * 0.45,
                    height * 8.7 - flowTime * 1.67));
                float centerShift = (flow - 0.5) * 0.28 * height;
                float distanceFromCore = abs(across + centerShift);
                float edge = 1.0 - smoothstep(0.48, 0.99, distanceFromCore);
                float foot = smoothstep(0.0, 0.075, height);
                float tip = 1.0 - smoothstep(0.76 + flow * 0.12, 1.0, height);
                float heat = saturate((1.0 - distanceFromCore * 0.9) *
                    (1.04 - height * 0.86) + (flow - 0.5) * 0.18);
                float hotCore = smoothstep(0.59, 0.95, heat);
                half3 edgeColor = half3(1.65h, 0.34h, 0.028h);
                half3 bodyColor = half3(3.0h, 1.37h, 0.18h);
                half3 coreColor = half3(4.5h, 3.25h, 1.28h);
                half3 color = lerp(edgeColor, bodyColor, saturate(heat * 1.35));
                color = lerp(color, coreColor, hotCore);
                half3 atlas = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, input.atlasUV).rgb;
                half atlasLuma = dot(atlas, half3(0.299h, 0.587h, 0.114h));
                half authoredLuma = saturate(dot(_BaseColor.rgb, half3(0.299h, 0.587h, 0.114h)));
                color *= (0.82h + atlasLuma * 0.26h) * (0.93h + authoredLuma * 0.07h);
                color *= _EmissionColor.rgb * _FireStrength * (0.92h + fine * 0.12h);
                half alpha = saturate(_Opacity * edge * foot * tip *
                    lerp(0.53, 1.0, heat) * (0.83 + flow * 0.17));
                return half4(MixFog(color, input.fogFactor), alpha);
            }
            ENDHLSL
        }
    }
    Fallback Off
}

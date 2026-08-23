// Falling fountain water: a narrow column the river's foam lace
// scrolls down, plus the splash ring it lands in.
//
// The city's water sheets derive everything from world XZ, which is
// exactly what a vertical stream cannot use - a falling column needs
// its pattern to travel along WORLD Y, and the splash needs the same
// lace travelling outward on the plane. One shader serves both:
// `_PlanarScroll` picks which world axis the scroll runs along, so
// the stream material and the splash material differ only in
// numbers, the way the river, the sea and the basin already share
// one water shader.
//
// Like the sea sheet it writes depth from the transparent queue, so
// the night halos and atmosphere particles that draw after it keep
// testing against real geometry. `_NightFactor` arrives through
// CityWaterResources with every other water in the city.
Shader "Bar Promenade/City Fountain Stream"
{
    Properties
    {
        _StreamColor("Stream Color", Color) = (0.16, 0.21, 0.21, 0.42)
        _LaceColor("Lace Color", Color) = (0.78, 0.84, 0.83, 1)
        _NightFactor("Night Factor", Range(0, 1)) = 0
        _RainIntensity("Rain Intensity", Range(0, 1)) = 0
        _FoamMap("Foam Mask", 2D) = "black" {}
        _ScrollSpeed("Scroll Speed", Float) = 2.4
        _AcrossTiling("Across Metres Per Tile", Float) = 0.9
        _AlongTiling("Along Metres Per Tile", Float) = 1.3
        _PlanarScroll("Planar Scroll", Range(0, 1)) = 0
    }

    SubShader
    {
        Tags
        {
            // After the water sheets (Transparent-100), which the
            // streams stand in, and before the default transparents.
            "Queue" = "Transparent-95"
            "RenderType" = "Transparent"
            "RenderPipeline" = "UniversalPipeline"
            "IgnoreProjector" = "True"
        }

        Pass
        {
            Name "FountainStream"
            Tags { "LightMode" = "UniversalForward" }

            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite On
            ZTest LEqual
            Cull Back

            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex StreamVertex
            #pragma fragment StreamFragment
            #pragma multi_compile_instancing

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half4 _StreamColor;
                half4 _LaceColor;
                half _NightFactor;
                half _RainIntensity;
                float _ScrollSpeed;
                float _AcrossTiling;
                float _AlongTiling;
                half _PlanarScroll;
            CBUFFER_END

            TEXTURE2D(_FoamMap);
            SAMPLER(sampler_FoamMap);

            struct Attributes
            {
                float4 positionOS : POSITION;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            Varyings StreamVertex(Attributes input)
            {
                Varyings output = (Varyings)0;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);
                output.positionWS = TransformObjectToWorld(
                    input.positionOS.xyz);
                output.positionCS = TransformWorldToHClip(
                    output.positionWS);
                return output;
            }

            half4 StreamFragment(Varyings input) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);

                // The scroll axis: world Y for the falling column,
                // a world-plane diagonal for the splash ring. Both
                // read the pattern moving away from the source.
                float across = input.positionWS.x + input.positionWS.z;
                float along = lerp(
                    input.positionWS.y,
                    (input.positionWS.x - input.positionWS.z) * 0.7,
                    _PlanarScroll);
                float2 uv = float2(
                    across / _AcrossTiling,
                    along / _AlongTiling + _Time.y * _ScrollSpeed);

                half lace = SAMPLE_TEXTURE2D(
                    _FoamMap, sampler_FoamMap, uv).r;
                half lace2 = SAMPLE_TEXTURE2D(
                    _FoamMap,
                    sampler_FoamMap,
                    uv * 1.9 + float2(
                        0.37,
                        _Time.y * _ScrollSpeed * 0.35)).r;
                half mask = saturate(lace * 0.75h + lace2 * 0.55h);

                // Rain thickens the pour a little; night dims the
                // whole thing with the rest of the unlit water.
                half night = 1.0h - 0.55h * _NightFactor;
                half3 color = lerp(
                    _StreamColor.rgb,
                    _LaceColor.rgb,
                    mask) * night;
                half alpha = saturate(
                    _StreamColor.a +
                    mask * (1.0h - _StreamColor.a) +
                    _RainIntensity * 0.08h);
                return half4(color, alpha);
            }
            ENDHLSL
        }
    }

    Fallback Off
}

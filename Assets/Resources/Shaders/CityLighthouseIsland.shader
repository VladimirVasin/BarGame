Shader "Bar Promenade/City Lighthouse Island"
{
    Properties
    {
        _HazeColor("City Haze Color", Color) = (0.330, 0.380, 0.355, 1)
        _HazeNear("Haze Near", Range(0, 1)) = 0.72
        _HazeFar("Haze Far", Range(0, 1)) = 0.86
        _HazeNearDistance("Haze Near Distance", Float) = 20
        _HazeFarDistance("Haze Far Distance", Float) = 41
        _FadeStartDistance("Fade Start Distance", Float) = 44.5
        _FadeEndDistance("Fade End Distance", Float) = 47.5
    }

    SubShader
    {
        Tags
        {
            "Queue" = "Transparent-90"
            "RenderType" = "Transparent"
            "RenderPipeline" = "UniversalPipeline"
            "IgnoreProjector" = "True"
        }

        Pass
        {
            Name "LighthouseIsland"
            Tags { "LightMode" = "UniversalForward" }

            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite On
            ZTest LEqual
            Cull Back

            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex IslandVertex
            #pragma fragment IslandFragment
            #pragma multi_compile_instancing

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half4 _HazeColor;
                half _HazeNear;
                half _HazeFar;
                float _HazeNearDistance;
                float _HazeFarDistance;
                float _FadeStartDistance;
                float _FadeEndDistance;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                half4 color : COLOR;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                half4 color : COLOR;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            Varyings IslandVertex(Attributes input)
            {
                Varyings output = (Varyings)0;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);
                output.positionWS = TransformObjectToWorld(
                    input.positionOS.xyz);
                output.positionCS = TransformWorldToHClip(
                    output.positionWS);
                output.color = input.color;
                return output;
            }

            half4 IslandFragment(Varyings input) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);

                // Deliberately no Unity distance fog: at the island's
                // 20-45 m the City's Exp2 fog leaves nothing. The
                // authored colour takes an analytic haze mix graded
                // by camera distance instead — near-full haze from
                // the shore (outlines only), easing off toward the
                // pier head so walking the boards genuinely brings
                // the island out of the fog — then the whole thing
                // dissolves into the clear colour across the fade
                // band, well before the 48 m far plane can clip it.
                // Per-fragment distance: the rocks are metres wide
                // and per-vertex fade would band across them.
                float dist = distance(
                    _WorldSpaceCameraPos,
                    input.positionWS);
                half haze = lerp(
                    _HazeNear,
                    _HazeFar,
                    (half)smoothstep(
                        _HazeNearDistance,
                        _HazeFarDistance,
                        dist));
                half3 color = lerp(
                    input.color.rgb,
                    _HazeColor.rgb,
                    haze);
                half fade = 1.0h - (half)smoothstep(
                    _FadeStartDistance,
                    _FadeEndDistance,
                    dist);
                return half4(color, fade);
            }
            ENDHLSL
        }
    }

    Fallback Off
}

Shader "Bar Promenade/City Mountain Backdrop"
{
    Properties
    {
        _HazeColor("City Haze Color", Color) = (0.330, 0.380, 0.355, 1)
        _HazeStrength("Haze Strength", Range(0, 1)) = 0.34
    }

    SubShader
    {
        Tags
        {
            "Queue" = "Transparent-100"
            "RenderType" = "Transparent"
            "RenderPipeline" = "UniversalPipeline"
            "IgnoreProjector" = "True"
        }

        Pass
        {
            Name "MountainBackdrop"
            Tags { "LightMode" = "UniversalForward" }

            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            ZTest LEqual
            Cull Off

            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex BackdropVertex
            #pragma fragment BackdropFragment
            #pragma multi_compile_instancing

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half4 _HazeColor;
                half _HazeStrength;
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
                UNITY_VERTEX_OUTPUT_STEREO
            };

            Varyings BackdropVertex(Attributes input)
            {
                Varyings output = (Varyings)0;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);
                output.positionCS = TransformObjectToHClip(
                    input.positionOS.xyz);
                output.color = input.color;
                output.uv = input.uv;
                return output;
            }

            half4 BackdropFragment(Varyings input) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);

                // Deliberately no Unity distance-fog variant here. At the
                // shell's 39-44 m radius City Exp2 fog is effectively the
                // clear colour. Instead the authored ridge colour receives
                // one bounded haze mix, slightly stronger toward the base,
                // so the silhouette survives without becoming a cut-out.
                half baseHaze = saturate(
                    _HazeStrength + (1.0h - input.uv.y) * 0.10h);
                half3 color = lerp(
                    input.color.rgb,
                    _HazeColor.rgb,
                    baseHaze);
                return half4(color, input.color.a);
            }
            ENDHLSL
        }
    }

    Fallback Off
}

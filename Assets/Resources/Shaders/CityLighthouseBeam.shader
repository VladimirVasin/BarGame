Shader "Bar Promenade/City Lighthouse Beam"
{
    Properties
    {
        [HDR] _BeamColor("Beam Color", Color) = (3.8, 3.6, 3.12, 1)
        _Intensity("Intensity", Range(0, 8)) = 1
        _Uniform("Uniform Glow", Range(0, 1)) = 0
        _FadeStartDistance("Fade Start Distance", Float) = 44.5
        _FadeEndDistance("Fade End Distance", Float) = 47.5
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
            Name "LighthouseBeam"
            Tags { "LightMode" = "UniversalForward" }

            Blend One One
            ZWrite Off
            ZTest LEqual
            Cull Off

            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex BeamVertex
            #pragma fragment BeamFragment
            #pragma multi_compile_instancing

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half4 _BeamColor;
                half _Intensity;
                half _Uniform;
                float _FadeStartDistance;
                float _FadeEndDistance;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float2 uv : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD1;
                float2 uv : TEXCOORD0;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            Varyings BeamVertex(Attributes input)
            {
                Varyings output = (Varyings)0;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);
                output.positionWS = TransformObjectToWorld(
                    input.positionOS.xyz);
                output.positionCS = TransformWorldToHClip(
                    output.positionWS);
                output.uv = input.uv;
                return output;
            }

            half4 BeamFragment(Varyings input) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);

                // Additive, no scene fog: this is the only channel
                // that survives 40 m of the City's Exp2. uv.x runs
                // along the beam from the lantern, so the light
                // pours out of the lamp and thins toward the mouth;
                // _Uniform = 1 turns the same material into the
                // lens's steady core. The distance fade matches the
                // island silhouette so beam and tower dissolve as
                // one thing.
                float dist = distance(
                    _WorldSpaceCameraPos,
                    input.positionWS);
                half along = pow(
                    saturate(1.0h - (half)input.uv.x),
                    1.6h);
                half envelope = lerp(along, 1.0h, _Uniform);
                half fade = 1.0h - (half)smoothstep(
                    _FadeStartDistance,
                    _FadeEndDistance,
                    dist);
                half strength = envelope * fade * _Intensity;
                return half4(_BeamColor.rgb * strength, 1.0h);
            }
            ENDHLSL
        }
    }

    Fallback Off
}

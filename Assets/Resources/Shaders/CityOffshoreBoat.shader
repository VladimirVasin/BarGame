Shader "Bar Promenade/City Offshore Boat"
{
    Properties
    {
        _HazeColor("City haze", Color) = (0.330, 0.380, 0.355, 1)
        _Presence("Pass presence", Range(0, 1)) = 1
        _NightFactor("Night", Range(0, 1)) = 0
    }
    SubShader
    {
        Tags { "Queue"="Transparent-90" "RenderType"="Transparent" "RenderPipeline"="UniversalPipeline" }
        Pass
        {
            Tags { "LightMode"="UniversalForward" }
            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite On
            ZTest LEqual
            Cull Back
            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile_instancing
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            CBUFFER_START(UnityPerMaterial)
                half4 _HazeColor;
                half _Presence;
                half _NightFactor;
            CBUFFER_END
            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
                half4 color : COLOR;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };
            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                half3 normalWS : TEXCOORD1;
                half4 color : COLOR;
                UNITY_VERTEX_OUTPUT_STEREO
            };
            Varyings Vert(Attributes input)
            {
                Varyings output = (Varyings)0;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);
                output.positionWS = TransformObjectToWorld(input.positionOS.xyz);
                output.positionCS = TransformWorldToHClip(output.positionWS);
                output.normalWS = TransformObjectToWorldNormal(input.normalOS);
                output.color = input.color;
                return output;
            }
            half4 Frag(Varyings input) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
                float dist = distance(_WorldSpaceCameraPos, input.positionWS);
                half alpha = _Presence * (1 - smoothstep(42.0, 47.4, dist));
                clip(alpha - 0.003h);
                // The same fixed-world distant-scenery convention as the island:
                // colour is already hazed here, never fogged a second time.
                half haze = lerp(0.61h, 0.83h, smoothstep(17.0, 41.0, dist));
                half shade = 0.78h + 0.22h * saturate(dot(normalize(input.normalWS),
                    normalize(half3(-0.35h, 0.8h, -0.4h))));
                half3 body = input.color.rgb * shade * lerp(1.0h, 0.73h, _NightFactor);
                return half4(lerp(body, _HazeColor.rgb, haze), alpha);
            }
            ENDHLSL
        }
    }
    Fallback Off
}

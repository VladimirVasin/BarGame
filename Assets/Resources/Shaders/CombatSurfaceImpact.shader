Shader "Bar Promenade/Combat Surface Impact"
{
    SubShader
    {
        Tags { "RenderPipeline" = "UniversalPipeline" "Queue" = "AlphaTest+2" "RenderType" = "TransparentCutout" }
        Pass
        {
            Tags { "LightMode" = "UniversalForward" }
            Cull Off
            ZWrite Off
            Offset -1, -1
            HLSLPROGRAM
            #pragma vertex ImpactVertex
            #pragma fragment ImpactFragment
            #pragma multi_compile_fog
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile_fragment _ _SHADOWS_SOFT
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            #include "Ps1VertexJitter.hlsl"

            struct Attributes { float4 positionOS : POSITION; float3 normalOS : NORMAL; float2 uv : TEXCOORD0; };
            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                half3 normalWS : TEXCOORD1;
                float2 uv : TEXCOORD2;
                half fog : TEXCOORD3;
            };
            Varyings ImpactVertex(Attributes input)
            {
                Varyings output;
                VertexPositionInputs position = GetVertexPositionInputs(input.positionOS.xyz);
                output.positionCS = Ps1SnapClipPosition(position.positionCS);
                output.positionWS = position.positionWS;
                output.normalWS = TransformObjectToWorldNormal(input.normalOS);
                output.uv = input.uv;
                output.fog = ComputeFogFactor(position.positionCS.z);
                return output;
            }
            half4 ImpactFragment(Varyings input) : SV_Target
            {
                float2 delta = (input.uv - .5) / .25;
                float angle = atan2(delta.y, delta.x);
                float radius = length(delta) * (1 + .08 * sin(angle * 7) + .045 * cos(angle * 11));
                clip(1 - radius);
                half core = 1 - smoothstep(.3, .43, radius);
                half rim = 1 - smoothstep(.48, .67, radius);
                half grain = frac(sin(dot(floor(input.uv * 96), float2(12.9898, 78.233))) * 43758.5453);
                half3 color = lerp(half3(.26, .24, .21), half3(.42, .38, .32), grain);
                color = lerp(color, half3(.075, .065, .05), rim);
                color = lerp(color, half3(.009, .008, .006), core);
                half3 normal = normalize(input.normalWS);
                Light main = GetMainLight(TransformWorldToShadowCoord(input.positionWS));
                half3 light = max(half3(.24, .24, .24), SampleSH(normal) + main.color *
                    saturate(dot(normal, main.direction)) * main.distanceAttenuation * main.shadowAttenuation);
                return half4(MixFog(color * light, input.fog), 1);
            }
            ENDHLSL
        }
    }
}

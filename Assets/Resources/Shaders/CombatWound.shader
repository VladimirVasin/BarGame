Shader "Bar Promenade/Combat Wound"
{
    Properties
    {
        [MainTexture] _BaseMap("Blood grain", 2D) = "white" {}
        _BulletWound("Projectile wound", Float) = 1
        _BulletHoleCount("Hole count", Float) = 0
    }
    SubShader
    {
        Tags { "RenderPipeline" = "UniversalPipeline" "Queue" = "AlphaTest+1" "RenderType" = "TransparentCutout" }
        Pass
        {
            Name "ProjectileWounds"
            Tags { "LightMode" = "UniversalForward" }
            Cull Off
            ZWrite Off
            HLSLPROGRAM
            #pragma target 3.0
            #pragma vertex WoundVertex
            #pragma fragment WoundFragment
            #pragma multi_compile_instancing
            #pragma multi_compile_fog
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile_fragment _ _SHADOWS_SOFT
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            #include "Ps1VertexJitter.hlsl"

            TEXTURE2D(_BaseMap);
            SAMPLER(sampler_BaseMap);
            CBUFFER_START(UnityPerMaterial)
                float _BulletWound;
                float _BulletHoleCount;
                float4 _BulletHoles[8];
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
                float2 uv : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };
            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                half3 normalWS : TEXCOORD1;
                float2 uv : TEXCOORD2;
                half fog : TEXCOORD3;
                UNITY_VERTEX_OUTPUT_STEREO
            };
            Varyings WoundVertex(Attributes input)
            {
                Varyings output;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);
                VertexPositionInputs position = GetVertexPositionInputs(input.positionOS.xyz);
                output.positionCS = Ps1SnapClipPosition(position.positionCS);
                output.positionWS = position.positionWS;
                output.normalWS = TransformObjectToWorldNormal(input.normalOS);
                output.uv = input.uv;
                output.fog = ComputeFogFactor(position.positionCS.z);
                return output;
            }
            half4 WoundFragment(Varyings input) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
                half4 surface = half4(0, 0, 0, 0);
                [unroll] for (int index = 0; index < 8; index++)
                {
                    if (index >= _BulletHoleCount) break;
                    float2 delta = (input.uv - _BulletHoles[index].xy) / max(.0001, _BulletHoles[index].z);
                    float angle = atan2(delta.y, delta.x);
                    float radius = length(delta) * (1.0 + .07 * sin(angle * 7.0) + .04 * cos(angle * 11.0));
                    half4 grain = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, delta / 5.2 + .5);
                    half core = 1.0 - smoothstep(.65, .82, radius);
                    half rim = 1.0 - smoothstep(1.15, 1.6, radius);
                    half blood = grain.a * (1.0 - smoothstep(1.8, 2.6, radius));
                    half alpha = max(core, max(rim, blood)) * _BulletWound;
                    half3 color = lerp(grain.rgb * .72, half3(.19, .015, .018), rim);
                    color = lerp(color, half3(.009, .003, .003), core);
                    if (alpha > surface.a) surface = half4(color, alpha);
                }
                clip(surface.a - .35);
                half3 normal = normalize(input.normalWS);
                Light main = GetMainLight(TransformWorldToShadowCoord(input.positionWS));
                half3 light = max(half3(.24, .24, .24), SampleSH(normal) +
                    main.color * saturate(dot(normal, main.direction)) * main.distanceAttenuation * main.shadowAttenuation);
                return half4(MixFog(surface.rgb * light, input.fog), 1);
            }
            ENDHLSL
        }
    }
}

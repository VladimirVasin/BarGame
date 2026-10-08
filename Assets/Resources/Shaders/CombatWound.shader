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
                float4 _BulletShapes[8];
                float4 _BulletStates[8];
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
                half3 normal = normalize(input.normalWS);
                Light main = GetMainLight(TransformWorldToShadowCoord(input.positionWS));
                half3 light = max(half3(.24, .24, .24), SampleSH(normal) +
                    main.color * saturate(dot(normal, main.direction)) * main.distanceAttenuation * main.shadowAttenuation);
                float3 view = GetWorldSpaceNormalizeViewDir(input.positionWS);
                // Derive the patch's moving tangent frame from its original skin.
                // This keeps the recessed edge light attached through any ragdoll pose.
                float3 dx = ddx(input.positionWS), dy = ddy(input.positionWS);
                float2 ux = ddx(input.uv), uy = ddy(input.uv);
                float handedness = ux.x * uy.y - ux.y * uy.x >= 0.0 ? 1.0 : -1.0;
                float3 tangentU = (dx * uy.y - dy * ux.y) * handedness;
                float3 tangentV = (dy * ux.x - dx * uy.x) * handedness;
                tangentU *= rsqrt(max(.00000001, dot(tangentU, tangentU)));
                tangentV *= rsqrt(max(.00000001, dot(tangentV, tangentV)));
                [unroll] for (int index = 0; index < 8; index++)
                {
                    if (index >= _BulletHoleCount) break;
                    float2 delta = (input.uv - _BulletHoles[index].xy) / max(.0001, _BulletHoles[index].z);
                    float4 shape = _BulletShapes[index], state = _BulletStates[index];
                    half skin = 1.0 - step(.5, _BulletHoles[index].w);
                    half glove = step(1.5, _BulletHoles[index].w);
                    half fabric = 1.0 - skin - glove;
                    float2 axis = shape.xy;
                    float2 crossAxis = float2(-axis.y, axis.x);
                    float2 entry = float2(dot(delta, axis), dot(delta, crossAxis));
                    // The actual surface-projected shot direction owns the long
                    // axis; compact glove/boot splits differ from torn cloth.
                    float damage = 1.0 + min(3.0, state.w - 1.0) * .12;
                    float2 opening = entry / damage;
                    opening.x /= max(1.0, shape.z) * lerp(1.0, 1.22, glove);
                    opening.y *= lerp(1.0, 1.22, glove);
                    float angle = atan2(opening.y, opening.x);
                    float irregular = 1.0 + .07 * sin(angle + shape.w) +
                        .045 * sin(angle * 3.0 + shape.w * 1.7) +
                        .03 * cos(angle * 7.0 - shape.w * .7) +
                        fabric * .025 * sin(angle * 11.0 + shape.w * 2.3);
                    float radius = length(opening) * irregular;
                    half core = 1.0 - smoothstep(.58, .86, radius);
                    half lip = smoothstep(.60, .88, radius) * (1.0 - smoothstep(1.06, 1.30, radius));
                    half bruise = skin * smoothstep(.93, 1.15, radius) * (1.0 - smoothstep(1.50, 2.05, radius));
                    float soakRadius = length(entry / float2(1.0 + fabric * .24, 1.0));
                    half4 grain = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap,
                        entry / (5.2 * max(1.0, state.z)) + .5);
                    half blood = grain.a * (1.0 - smoothstep(1.5 * state.z, 2.45 * state.z, soakRadius));
                    blood *= lerp(1.0, .70, glove);
                    // Two unequal nicks avoid a periodic radial star. Their
                    // coarse thread tips stay legible after the PS1 composite.
                    half nickA = smoothstep(.94, .99, cos(angle - shape.w)) *
                        (1.0 - smoothstep(1.15, 1.65, radius));
                    half nickB = smoothstep(.97, .995, cos(angle - shape.w * 1.9 - .8)) *
                        (1.0 - smoothstep(1.05, 1.40, radius));
                    half fray = fabric * max(nickA * .70, nickB * .45) * smoothstep(.80, 1.0, radius);
                    half fibres = fabric * lip * saturate(.30 + .45 * sin(angle * 13.0 + shape.w) +
                        .25 * cos(angle * 7.0 - shape.w * 1.4));
                    half rim = max(lip, max(bruise, max(fray, fibres)));
                    half alpha = max(core, max(rim, blood)) * _BulletWound;
                    half wet = saturate(state.y);
                    half3 dryBlood = half3(.065, .016, .012);
                    half3 freshBlood = half3(.27, .012, .020);
                    half3 color = lerp(dryBlood, freshBlood, wet) * lerp(.72, 1.15, grain.r);
                    color = lerp(color, half3(.13, .037, .042), bruise * .68);
                    half3 edgeColor = skin * half3(.39, .13, .12) + fabric * half3(.24, .18, .115) +
                        glove * half3(.095, .083, .072);
                    color = lerp(color, edgeColor, max(lip * .85, max(fray, fibres)));
                    // A dark recessed centre, lit raised rim and a small wet
                    // glint give local depth without changing the body topology.
                    float2 outward = opening * rsqrt(max(.00001, dot(opening, opening)));
                    float2 outwardUv = axis * outward.x + crossAxis * outward.y;
                    half3 rimNormal = normalize(normal * .72 +
                        (tangentU * outwardUv.x + tangentV * outwardUv.y) * .55);
                    half edgeLight = .40 + .75 * saturate(dot(rimNormal, main.direction));
                    color *= lerp(1.0, edgeLight, lip);
                    color = lerp(color, half3(.009, .002, .003), core);
                    half glint = pow(saturate(dot(normalize(rimNormal + main.direction), view)), 18.0) *
                        wet * lip * (skin * .065 + fabric * .018 + glove * .035);
                    color = color * light + main.color * glint * main.distanceAttenuation * main.shadowAttenuation;
                    if (alpha > surface.a) surface = half4(color, alpha);
                }
                clip(surface.a - .35);
                return half4(MixFog(surface.rgb, input.fog), 1);
            }
            ENDHLSL
        }
    }
}

Shader "Bar Promenade/Home Toilet Bowl Water"
{
    Properties { _BowlWaterClock("Water Clock", Float) = 0 }
    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" "RenderType"="Transparent" "Queue"="Transparent-10" }
        Pass
        {
            Name "BowlWaterBothSides"
            Tags { "LightMode"="UniversalForward" }
            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off Cull Off
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile _ _ADDITIONAL_LIGHTS
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareOpaqueTexture.hlsl"
            CBUFFER_START(UnityPerMaterial)
                float _BowlWaterClock;
            CBUFFER_END
            struct Attributes { float4 positionOS : POSITION; };
            struct Varyings { float4 positionCS : SV_POSITION; float3 world : TEXCOORD0; };
            Varyings Vert(Attributes v)
            {
                Varyings o;
                o.world = TransformObjectToWorld(v.positionOS.xyz);
                o.positionCS = TransformWorldToHClip(o.world);
                return o;
            }
            float3 Glint(Light light, float3 normal, float3 view)
            {
                return light.color * light.distanceAttenuation * light.shadowAttenuation *
                    pow(saturate(dot(normal, normalize(light.direction + view))), 56.0) * .25;
            }
            half4 Frag(Varyings i) : SV_Target
            {
                float2 wave = float2(sin(i.world.x * 48 + i.world.z * 16 + _BowlWaterClock * 1.5),
                    cos(i.world.z * 43 - i.world.x * 11 - _BowlWaterClock * 1.3)) * .045;
                float side = _WorldSpaceCameraPos.y >= i.world.y ? 1.0 : -1.0;
                float3 normal = normalize(float3(wave.x, 1, wave.y)) * side;
                float3 view = GetWorldSpaceNormalizeViewDir(i.world);
                float2 uv = GetNormalizedScreenSpaceUV(i.positionCS);
                float3 behind = SampleSceneColor(saturate(uv + wave * .009));
                float fresnel = pow(1 - saturate(dot(normal, view)), 4);
                // Above, the water's shallow depth absorbs the bright ceramic floor.
                // Below, keep the existing room readable through the surface overhead.
                float absorption = side > 0 ? .58 : .08;
                float3 colour = lerp(behind * float3(.82,.94,.84),
                    float3(.035,.052,.039), absorption + fresnel * .18);
                float3 centre = TransformObjectToWorld(float3(0,0,0));
                float rim = smoothstep(.90, 1.0, length((i.world.xz - centre.xz) / float2(.17,.157)));
                colour += SampleSH(float3(0,1,0)) * rim * .14;
                colour += Glint(GetMainLight(), normal, view);
                #if defined(_ADDITIONAL_LIGHTS)
                    uint count = GetAdditionalLightsCount();
                    for (uint n = 0; n < count; n++) colour += Glint(GetAdditionalLight(n, i.world), normal, view);
                #endif
                return half4(colour, .88);
            }
            ENDHLSL
        }
    }
}

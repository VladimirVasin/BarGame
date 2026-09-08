Shader "Bar Promenade/Home Toilet Bowl Water"
{
    Properties
    {
        _BowlWaterClock("Water Clock", Float) = 0
        _BowlImpact("Water Contact", Vector) = (0,0,0,0)
        _BowlVortex("Flush Flow", Vector) = (0,0,0,0)
    }
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
            #pragma multi_compile _ _CLUSTER_LIGHT_LOOP
            #pragma multi_compile _ _LIGHT_LAYERS
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareOpaqueTexture.hlsl"
            CBUFFER_START(UnityPerMaterial)
                float _BowlWaterClock;
                float4 _BowlImpact;
                float4 _BowlVortex;
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
                float2 contactOffset = i.world.xz - _BowlImpact.xy;
                float contactDistance = length(contactOffset);
                float contactTime = max(0, _BowlWaterClock - _BowlImpact.z);
                wave += contactOffset / max(.004, contactDistance) *
                    sin(contactDistance * 130 - contactTime * 18) *
                    exp(-contactTime * 5 - contactDistance * 8) * _BowlImpact.w * .065;
                float3 centre = TransformObjectToWorld(float3(0,0,0));
                float2 radial = (i.world.xz - centre.xz) / float2(.17,.157);
                float radius = length(radial);
                float azimuth = atan2(radial.y, radial.x);
                float flowPhase = azimuth * 3 + radius * 22 - _BowlVortex.y * 1.6;
                float flow = _BowlVortex.x * (1 - smoothstep(.80, 1.0, radius));
                float2 tangent = float2(-radial.y, radial.x) / max(.04, radius);
                wave += tangent * sin(flowPhase) * .14 * flow;
                wave += radial / max(.04, radius) * cos(flowPhase * .7) * .07 * flow;
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
                float rim = smoothstep(.90, 1.0, length((i.world.xz - centre.xz) / float2(.17,.157)));
                float foam = pow(saturate(sin(flowPhase)), 12) * smoothstep(.06, .24, radius) * flow;
                colour = lerp(colour, float3(.31,.35,.29), foam * .35);
                colour += SampleSH(float3(0,1,0)) * rim * .14;
                uint renderingLayers = GetMeshRenderingLayer();
                Light mainLight = GetMainLight();
                if (IsMatchingLightLayer(mainLight.layerMask, renderingLayers))
                    colour += Glint(mainLight, normal, view);
                #if defined(_ADDITIONAL_LIGHTS)
                    InputData inputData = (InputData)0;
                    inputData.positionWS = i.world;
                    inputData.normalizedScreenSpaceUV = uv;
                    #if USE_CLUSTER_LIGHT_LOOP
                    for (uint lightIndex = 0; lightIndex < min(URP_FP_DIRECTIONAL_LIGHTS_COUNT, MAX_VISIBLE_LIGHTS); lightIndex++)
                    {
                        Light light = GetAdditionalLight(lightIndex, i.world);
                        if (IsMatchingLightLayer(light.layerMask, renderingLayers)) colour += Glint(light, normal, view);
                    }
                    #endif
                    uint count = GetAdditionalLightsCount();
                    LIGHT_LOOP_BEGIN(count)
                        Light light = GetAdditionalLight(lightIndex, i.world);
                        if (IsMatchingLightLayer(light.layerMask, renderingLayers)) colour += Glint(light, normal, view);
                    LIGHT_LOOP_END
                #endif
                return half4(colour, .88);
            }
            ENDHLSL
        }
    }
}

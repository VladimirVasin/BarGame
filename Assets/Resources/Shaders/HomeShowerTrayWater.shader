Shader "Bar Promenade/Home Shower Tray Water"
{
    Properties
    {
        _WaterAmount("Collected Water", Range(0,1)) = 0
        _DrainFlow("Drain Flow", Range(0,1)) = 0
        _WaterClock("Water Clock", Float) = 0
        _DrainWorld("Drain World Position", Vector) = (4.32,.215,3.32,0)
        _NozzleWorld("Nozzle World Position", Vector) = (3.88,1.99,3.42,0)
    }
    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" "RenderType"="Transparent" "Queue"="Transparent-10" }
        Pass
        {
            Name "ShallowCollectedWater"
            Tags { "LightMode"="UniversalForward" }
            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            Cull Back
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile _ _ADDITIONAL_LIGHTS
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareOpaqueTexture.hlsl"

            CBUFFER_START(UnityPerMaterial)
                float _WaterAmount, _DrainFlow, _WaterClock;
                float4 _DrainWorld, _NozzleWorld;
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
                float spec = pow(saturate(dot(normal, normalize(light.direction + view))), 72.0);
                return light.color * light.distanceAttenuation * spec * .65;
            }

            half4 Frag(Varyings i) : SV_Target
            {
                float2 delta = i.world.xz - _DrainWorld.xz;
                float radius = max(.001, length(delta));
                float2 radial = delta / radius;
                float angle = atan2(delta.y, delta.x);
                // Increasing phase moves each crest toward the real aperture.
                float phase = radius * 76.0 + _WaterClock * (5.0 + _DrainFlow * 4.0);
                float strands = .65 + .35 * sin(angle * 9.0 + radius * 8.0);
                float drainRipple = sin(phase) * strands;
                float2 impact = i.world.xz - _NozzleWorld.xz;
                float impactRadius = max(.001, length(impact));
                float impactRipple = sin(impactRadius * 65.0 - _WaterClock * 8.0) * exp(-impactRadius * 5.0);
                float flow = smoothstep(0.0, .08, _DrainFlow);
                float2 slope = radial * drainRipple * .14 * flow + impact / impactRadius * impactRipple * .055 * flow;
                float3 normal = normalize(float3(slope.x, 1.0, slope.y));
                float2 uv = GetNormalizedScreenSpaceUV(i.positionCS);
                float3 behind = SampleSceneColor(saturate(uv + slope * .005 * _WaterAmount));
                float3 view = GetWorldSpaceNormalizeViewDir(i.world);
                float3 colour = behind * float3(.94, .98, .99);
                Light mainLight = GetMainLight();
                colour += Glint(mainLight, normal, view);
                #if defined(_ADDITIONAL_LIGHTS)
                    uint count = GetAdditionalLightsCount();
                    for (uint n = 0; n < count; n++) colour += Glint(GetAdditionalLight(n, i.world), normal, view);
                #endif
                float crest = pow(saturate(drainRipple), 12.0) * .065 * flow;
                float fresnel = pow(1.0 - saturate(dot(normal, view)), 4.0);
                colour += float3(.42,.49,.49) * (crest + fresnel * .07);
                // A faint meniscus picks out the water descending through the grate.
                colour += .055 * flow * (1.0 - smoothstep(.07,.105,radius));
                return half4(colour, smoothstep(0.0,.12,_WaterAmount) * .86);
            }
            ENDHLSL
        }
    }
}

Shader "Hidden/BarPromenade/Mothers House Clean Lit Air"
{
    SubShader
    {
        Tags { "RenderPipeline" = "UniversalPipeline" }
        ZWrite Off Cull Off ZTest Always
        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
        #include "Packages/com.unity.render-pipelines.core/Runtime/Utilities/Blit.hlsl"
        #define _SURFACE_TYPE_TRANSPARENT 1
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/RealtimeLights.hlsl"
        TEXTURE2D_X_FLOAT(_MothersHouseAirDepth);
        TEXTURE2D_X(_MothersHouseAirVolume);
        int _MothersHouseAirSourceCount;
        float4 _MothersHouseAirLightIndices;
        float4 _MothersHouseAirPositions[2];
        float4 _MothersHouseAirMinima[2];
        float4 _MothersHouseAirMaxima[2];
        float4 _MothersHouseAirBlockerMinima[16];
        float4 _MothersHouseAirBlockerMaxima[16];
        float4 _MothersHouseAirTexelSize, _MothersHouseAirScreenBounds;

        float HouseAirRaw(float2 uv)
        { return SAMPLE_TEXTURE2D_X_LOD(_MothersHouseAirDepth, sampler_PointClamp, uv, 0).r; }
        float HouseAirEye(float raw)
        { return unity_OrthoParams.w > .5 ? LinearDepthToEyeDepth(raw) : LinearEyeDepth(raw, _ZBufferParams); }
        float3 HouseAirEndpoint(float2 uv, float raw)
        {
            #if !UNITY_REVERSED_Z
                raw = lerp(UNITY_NEAR_CLIP_VALUE, 1.0, raw);
            #endif
            return ComputeWorldSpacePosition(uv, raw, UNITY_MATRIX_I_VP);
        }
        void HouseAirRay(float2 uv, float raw, out float3 origin, out float3 direction, out float surfaceDistance)
        {
            origin = _WorldSpaceCameraPos;
            if (unity_OrthoParams.w > .5)
            {
                #if UNITY_REVERSED_Z
                    const float nearDepth = 1;
                #else
                    const float nearDepth = 0;
                #endif
                origin = HouseAirEndpoint(uv, nearDepth);
            }
            float3 delta = HouseAirEndpoint(uv, raw) - origin;
            surfaceDistance = length(delta);
            direction = delta / max(surfaceDistance, .0001);
        }
        bool HouseAirBox(float3 minimum, float3 maximum, float3 origin, float3 direction,
            out float enter, out float leave)
        {
            float3 inverse = (step(0.0, direction) * 2.0 - 1.0) / max(abs(direction), .00001);
            float3 a = (minimum - origin) * inverse, b = (maximum - origin) * inverse;
            float3 nearPlane = min(a, b), farPlane = max(a, b);
            enter = max(0.0, max(nearPlane.x, max(nearPlane.y, nearPlane.z)));
            leave = min(farPlane.x, min(farPlane.y, farPlane.z));
            return leave > enter;
        }
        float HouseAirStructuralVisibility(int source, float3 world)
        {
            float3 origin = _MothersHouseAirPositions[source].xyz;
            float3 delta = world - origin;
            float distanceToSample = length(delta);
            if (distanceToSample < .05) return 1;
            float3 direction = delta / distanceToSample;
            float3 segmentMin = min(origin, world), segmentMax = max(origin, world);
            int blockerCount = (int)_MothersHouseAirMinima[source].w;
            [loop] for (int blocker = 0; blocker < blockerCount; blocker++)
            {
                int index = source * 8 + blocker;
                float3 minimum = _MothersHouseAirBlockerMinima[index].xyz;
                float3 maximum = _MothersHouseAirBlockerMaxima[index].xyz;
                // Cheap segment-box rejection precedes the slab division.
                if (any(segmentMax < minimum) || any(segmentMin > maximum)) continue;
                float enter, leave;
                if (HouseAirBox(minimum, maximum, origin, direction, enter, leave) &&
                    leave > .025 && enter < distanceToSample - .025) return 0;
            }
            return 1;
        }
        float4 HouseAirIntegrateRay(float2 uv, float raw)
        {
            float eye = HouseAirEye(raw);
            [branch] if (any(uv < _MothersHouseAirScreenBounds.xy) || any(uv > _MothersHouseAirScreenBounds.zw))
                return float4(0, 0, 0, eye);
            float3 origin, direction;
            float surfaceDistance;
            HouseAirRay(uv, raw, origin, direction, surfaceDistance);
            float3 sum = 0;
            [loop] for (int source = 0; source < _MothersHouseAirSourceCount; source++)
            {
                int lightIndex = (int)_MothersHouseAirLightIndices[source];
                if (lightIndex < 0) continue;
                float3 minimum = _MothersHouseAirMinima[source].xyz;
                float3 maximum = _MothersHouseAirMaxima[source].xyz;
                float enter, leave;
                if (!HouseAirBox(minimum, maximum, origin, direction, enter, leave)) continue;
                float stepLength = (leave - enter) / 10;
                leave = min(leave, surfaceDistance);
                if (leave <= enter) continue;
                [unroll] for (int sampleIndex = 0; sampleIndex < 10; sampleIndex++)
                {
                    float start = enter + sampleIndex * stepLength;
                    float segment = min(stepLength, max(0.0, leave - start));
                    if (segment <= 0) break;
                    float3 world = origin + direction * (start + segment * .5);
                    float3 margins = min(world - minimum, maximum - world);
                    float edge = saturate(min(margins.x, min(margins.y, margins.z)) / .18);
                    if (edge <= 0 || HouseAirStructuralVisibility(source, world) <= 0) continue;
                    Light light = GetAdditionalPerObjectLight(lightIndex, world);
                    float3 irradiance = max((float3)light.color * light.distanceAttenuation, 0);
                    float peak = max(irradiance.r, max(irradiance.g, irradiance.b));
                    if (peak <= .00001) continue;
                    float shadow = AdditionalLightRealtimeShadow(lightIndex, world, light.direction);
                    // A weak molecular-air response, without dust, weather,
                    // independently animated density or global fog changes.
                    sum += irradiance / (1 + peak * .35) * (shadow * edge * segment * .0025);
                }
            }
            return float4(sum, eye);
        }
        float4 HouseAirIntegrate(Varyings input) : SV_Target
        {
            UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
            return HouseAirIntegrateRay(input.texcoord, HouseAirRaw(input.texcoord));
        }
        float4 HouseAirResolve(Varyings input) : SV_Target
        {
            UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
            float2 uv = input.texcoord;
            [branch] if (any(uv < _MothersHouseAirScreenBounds.xy) || any(uv > _MothersHouseAirScreenBounds.zw))
                return 0;
            float raw = HouseAirRaw(uv), eye = HouseAirEye(raw);
            float3 origin, direction;
            float surfaceDistance;
            HouseAirRay(uv, raw, origin, direction, surfaceDistance);
            bool support = false;
            [loop] for (int source = 0; source < _MothersHouseAirSourceCount; source++)
            {
                float enter, leave;
                if (_MothersHouseAirLightIndices[source] >= 0 &&
                    HouseAirBox(_MothersHouseAirMinima[source].xyz, _MothersHouseAirMaxima[source].xyz,
                        origin, direction, enter, leave) && min(leave, surfaceDistance) > enter)
                { support = true; break; }
            }
            [branch] if (!support) return 0;
            float2 pixel = uv * _MothersHouseAirTexelSize.zw - .5;
            float2 cell = floor(pixel), fraction = frac(pixel);
            float3 sum = 0;
            float weights = 0;
            [unroll] for (int y = 0; y < 2; y++)
                [unroll] for (int x = 0; x < 2; x++)
                {
                    float2 sampleUv = (cell + float2(x, y) + .5) * _MothersHouseAirTexelSize.xy;
                    float4 tap = SAMPLE_TEXTURE2D_X_LOD(_MothersHouseAirVolume, sampler_PointClamp, sampleUv, 0);
                    float2 spatial = lerp(1 - fraction, fraction, float2(x, y));
                    float behind = max(tap.a - eye, 0);
                    float weight = spatial.x * spatial.y * saturate(1 - behind / max(.04, eye * .002));
                    sum += tap.rgb * weight; weights += weight;
                }
            return float4(weights > .00001 ? sum / weights : 0, 0);
        }
        ENDHLSL
        Pass
        {
            Name "InteriorAirIntegration"
            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex Vert
            #pragma fragment HouseAirIntegrate
            #pragma multi_compile_fragment _ _ADDITIONAL_LIGHT_SHADOWS
            #pragma multi_compile_fragment _ _SHADOWS_SOFT _SHADOWS_SOFT_LOW _SHADOWS_SOFT_MEDIUM _SHADOWS_SOFT_HIGH
            #pragma multi_compile _ _CLUSTER_LIGHT_LOOP
            ENDHLSL
        }
        Pass
        {
            Name "InteriorAirDepthResolve"
            Blend One One
            ColorMask RGB
            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex Vert
            #pragma fragment HouseAirResolve
            ENDHLSL
        }
    }
    Fallback Off
}

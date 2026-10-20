Shader "Hidden/BarPromenade/Alpine Village Local Lit Air"
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
        #define BAR_PROMENADE_LOCAL_LIGHT_SHADOWS 1
        #include "AlpineVillageLocalLighting.hlsl"
        TEXTURE2D_X_FLOAT(_AlpineAirDepth);
        TEXTURE2D_X(_AlpineAirVolume);
        float4 _AlpineAirTexelSize;
        float4 _AlpineAirScreenBounds;

        float AlpineAirRawDepth(float2 uv)
        {
            return SAMPLE_TEXTURE2D_X_LOD(_AlpineAirDepth, sampler_PointClamp, uv, 0).r;
        }

        float AlpineAirEyeDepth(float raw)
        {
            return unity_OrthoParams.w > .5 ? LinearDepthToEyeDepth(raw) : LinearEyeDepth(raw, _ZBufferParams);
        }

        float3 AlpineAirEndpoint(float2 uv, float raw)
        {
            #if !UNITY_REVERSED_Z
                raw = lerp(UNITY_NEAR_CLIP_VALUE, 1.0, raw);
            #endif
            return ComputeWorldSpacePosition(uv, raw, UNITY_MATRIX_I_VP);
        }

        void AlpineAirRay(float2 uv, float raw, out float3 origin, out float3 direction, out float lengthToSurface)
        {
            origin = _WorldSpaceCameraPos;
            if (unity_OrthoParams.w > .5)
            {
                #if UNITY_REVERSED_Z
                    const float nearDepth = 1.0;
                #else
                    const float nearDepth = 0.0;
                #endif
                origin = AlpineAirEndpoint(uv, nearDepth);
            }
            float3 delta = AlpineAirEndpoint(uv, raw) - origin;
            lengthToSurface = length(delta);
            direction = delta / max(lengthToSurface, .0001);
        }

        bool AlpineAirInterval(int index, float3 origin, float3 direction, out float enter, out float leave)
        {
            if (!AlpineLocalBoxInterval(_AlpineLocalLightMinima[index].xyz,
                _AlpineLocalLightMaxima[index].xyz, origin, direction, enter, leave)) return false;
            float4 plane = _AlpineLocalLightPlanes[index];
            if (dot(plane.xyz, plane.xyz) > .5)
            {
                float start = dot(plane.xyz, origin) + plane.w;
                float slope = dot(plane.xyz, direction);
                if (abs(slope) < .00001) { if (start < 0) return false; }
                else if (slope > 0) enter = max(enter, -start / slope);
                else leave = min(leave, -start / slope);
            }
            int count = (int)_AlpineLocalLightOptions[index].z;
            [loop] for (int clip = 0; clip < count; clip++)
            {
                float4 boundary = _AlpineLocalLightClipPlanes[index * 6 + clip];
                if (dot(boundary.xyz, boundary.xyz) < .0001) continue;
                float start = dot(boundary.xyz, origin) + boundary.w;
                float slope = dot(boundary.xyz, direction);
                if (abs(slope) < .00001) { if (start < 0) return false; }
                else if (slope > 0) enter = max(enter, -start / slope);
                else leave = min(leave, -start / slope);
            }
            return leave > enter;
        }

        float4 AlpineAirIntegrate(Varyings input) : SV_Target
        {
            UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
            float2 uv = input.texcoord;
            float raw = AlpineAirRawDepth(uv);
            float3 origin, direction;
            float lengthToSurface;
            AlpineAirRay(uv, raw, origin, direction, lengthToSurface);
            float3 sum = 0;
            [loop] for (int lightIndex = 0; lightIndex < _AlpineLocalLightCount; lightIndex++)
            {
                float enter, leave;
                if (!AlpineAirInterval(lightIndex, origin, direction, enter, leave)) continue;
                float stepLength = (leave - enter) / 10.0;
                leave = min(leave, lengthToSurface);
                if (leave <= enter) continue;
                [unroll] for (int sampleIndex = 0; sampleIndex < 10; sampleIndex++)
                {
                    float start = enter + sampleIndex * stepLength;
                    float segment = min(stepLength, max(0.0, leave - start));
                    if (segment <= 0) break;
                    float3 world = origin + direction * (start + segment * .5);
                    float3 radiance = AlpineLocalLightRadiance(lightIndex, world);
                    float peak = max(radiance.r, max(radiance.g, radiance.b));
                    if (peak <= .00001) continue;
                    float shadow = AlpineLocalShadowVisibility(lightIndex, world);
                    float nearToFarZ = max(-TransformWorldToView(world).z - _ProjectionParams.y, 0);
                    float visibility = ComputeFogIntensity(ComputeFogFactorZ0ToFar(nearToFarZ));
                    // Local scattering adds light; the existing village fog
                    // continues to own visibility and all weather timing.
                    sum += radiance / (1 + peak * .35) *
                        (segment * .012 * visibility * shadow * _AlpineLocalLightOptions[lightIndex].x);
                }
            }
            return float4(sum, AlpineAirEyeDepth(raw));
        }

        float4 AlpineAirResolve(Varyings input) : SV_Target
        {
            UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
            float2 uv = input.texcoord;
            [branch] if (any(uv < _AlpineAirScreenBounds.xy) || any(uv > _AlpineAirScreenBounds.zw))
                return float4(0, 0, 0, 0);
            float raw = AlpineAirRawDepth(uv);
            float eyeDepth = AlpineAirEyeDepth(raw);
            float3 origin, direction;
            float lengthToSurface;
            AlpineAirRay(uv, raw, origin, direction, lengthToSurface);
            bool supported = false;
            [loop] for (int index = 0; index < _AlpineLocalLightCount; index++)
            {
                float enter, leave;
                if (AlpineAirInterval(index, origin, direction, enter, leave) &&
                    min(leave, lengthToSurface) > enter) { supported = true; break; }
            }
            // Full-resolution support prevents coarse exterior air from being
            // borrowed across a facade onto an interior wall pixel.
            [branch] if (!supported) return float4(0, 0, 0, 0);
            float2 pixel = uv * _AlpineAirTexelSize.zw - .5;
            float2 cell = floor(pixel), fraction = frac(pixel);
            float3 sum = 0;
            float weights = 0;
            [unroll] for (int y = 0; y < 2; y++)
            {
                [unroll] for (int x = 0; x < 2; x++)
                {
                    float2 sampleUv = (cell + float2(x, y) + .5) * _AlpineAirTexelSize.xy;
                    float4 tap = SAMPLE_TEXTURE2D_X_LOD(_AlpineAirVolume, sampler_PointClamp, sampleUv, 0);
                    float2 spatial = lerp(1 - fraction, fraction, float2(x, y));
                    // A shorter scattering prefix is safe; a farther opaque
                    // endpoint can add air behind this surface and is rejected.
                    float behind = max(tap.a - eyeDepth, 0);
                    float tolerance = max(.06, eyeDepth * .003);
                    float weight = spatial.x * spatial.y * saturate(1 - behind / tolerance);
                    sum += tap.rgb * weight;
                    weights += weight;
                }
            }
            return float4(weights > .00001 ? sum / weights : 0, 0);
        }
        ENDHLSL

        Pass
        {
            Name "LocalAirIntegration"
            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex Vert
            #pragma fragment AlpineAirIntegrate
            #pragma multi_compile_fog
            #pragma multi_compile_fragment _ _ADDITIONAL_LIGHT_SHADOWS
            #pragma multi_compile_fragment _ _SHADOWS_SOFT _SHADOWS_SOFT_LOW _SHADOWS_SOFT_MEDIUM _SHADOWS_SOFT_HIGH
            #pragma multi_compile _ _CLUSTER_LIGHT_LOOP
            ENDHLSL
        }
        Pass
        {
            Name "LocalAirDepthResolve"
            Blend One One
            ColorMask RGB
            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex Vert
            #pragma fragment AlpineAirResolve
            ENDHLSL
        }
    }
    Fallback Off
}

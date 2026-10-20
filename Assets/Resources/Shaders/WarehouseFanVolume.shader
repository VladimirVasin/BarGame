Shader "Bar Promenade/Warehouse Fan Volume"
{
    Properties
    {
        [NoScaleOffset] _FanDepthTexture("Scene Depth", 2D) = "white" {}
        [NoScaleOffset] _FanVolumeTexture("Lit Air", 2D) = "black" {}
    }
    SubShader
    {
        Tags { "RenderPipeline" = "UniversalPipeline" }
        ZWrite Off Cull Off ZTest Always

        HLSLINCLUDE
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.core/Runtime/Utilities/Blit.hlsl"
            // Both passes sample the actual light-space atlas, never the
            // screen-space shadow of an unrelated surface behind the air.
            #define _SURFACE_TYPE_TRANSPARENT 1
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/RealtimeLights.hlsl"
            TEXTURE2D_X_FLOAT(_FanDepthTexture);
            TEXTURE2D_X(_FanVolumeTexture);
            float4 _FanVolumeTexelSize;
            float4 _FanScreenBounds;
            float4 _FanSourcePosition, _FanVolumeMinimum, _FanVolumeMaximum;
            int _FanLightIndex;
            #if defined(_FAN_VOLUME_DIAGNOSTICS)
                int _FanDebugView;
            #endif

            float FanRawDepth(float2 uv)
            {
                return SAMPLE_TEXTURE2D_X_LOD(_FanDepthTexture, sampler_PointClamp, uv, 0).r;
            }

            float FanEyeDepth(float raw)
            {
                return unity_OrthoParams.w > .5 ? LinearDepthToEyeDepth(raw)
                    : LinearEyeDepth(raw, _ZBufferParams);
            }

            float FanDeviceDepth(float raw)
            {
                #if UNITY_REVERSED_Z
                    return raw;
                #else
                    return lerp(UNITY_NEAR_CLIP_VALUE, 1.0, raw);
                #endif
            }

            bool FanRayBounds(float3 origin, float3 direction, out float enter, out float leave)
            {
                float3 inverse = (step(0.0, direction) * 2.0 - 1.0) / max(abs(direction), .00001);
                float3 a = (_FanVolumeMinimum.xyz - origin) * inverse;
                float3 b = (_FanVolumeMaximum.xyz - origin) * inverse;
                float3 nearPlane = min(a, b), farPlane = max(a, b);
                enter = max(0.0, max(nearPlane.x, max(nearPlane.y, nearPlane.z)));
                leave = min(farPlane.x, min(farPlane.y, farPlane.z));
                return leave > enter;
            }

            float FanDensity(float3 position)
            {
                float belowSource = _FanSourcePosition.y - position.y;
                float radius = length(position.xz - _FanSourcePosition.xz);
                float coneRadius = min(5.5, belowSource * .8 + .25);
                float edge = 1.0 - smoothstep(max(0.0, coneRadius - .65), coneRadius, radius);
                float ends = smoothstep(.35, 1.2, belowSource) *
                    (1.0 - smoothstep(10.9, 11.8, belowSource));
                // No animated field or independent clock: the existing moving
                // blades and actual light supply every change in this column.
                return edge * ends * .0035;
            }

            float FanPrefixDepthWeight(float sampleDepth, float surfaceDepth,
                float depthScale, float depthTolerance)
            {
                // A nearer tap contains only a shorter scattering integral,
                // so it can underestimate background air but cannot add air
                // behind this pixel's opaque surface. A farther tap can: keep
                // the existing depth rejection on that side of the boundary.
                float behindSurface = max(sampleDepth - surfaceDepth, 0.0);
                return exp2(-behindSurface / depthScale) * step(behindSurface, depthTolerance);
            }

            // Both coarse integration and a rare silhouette repair use the
            // same physical ray, source, shadow atlas and opaque endpoint.
            float4 FanIntegrateRay(float2 uv, float raw)
            {
                float eyeDepth = FanEyeDepth(raw);
                float3 endpoint = ComputeWorldSpacePosition(uv, FanDeviceDepth(raw), UNITY_MATRIX_I_VP);
                float3 origin = _WorldSpaceCameraPos;
                if (unity_OrthoParams.w > .5)
                {
                    #if UNITY_REVERSED_Z
                        const float nearDepth = 1.0;
                    #else
                        const float nearDepth = UNITY_NEAR_CLIP_VALUE;
                    #endif
                    origin = ComputeWorldSpacePosition(uv, nearDepth, UNITY_MATRIX_I_VP);
                }
                float3 ray = endpoint - origin;
                float distanceToSurface = length(ray);
                float3 direction = ray / max(distanceToSurface, .0001);
                float enter, leave;
                if (!FanRayBounds(origin, direction, enter, leave)) return float4(0, 0, 0, eyeDepth);
                // Anchor quadrature to the full column, not the opaque
                // endpoint. Otherwise a thin rail shifts every sample and
                // changes the entire shadow integral across its silhouette.
                float columnLeave = leave;
                leave = min(columnLeave, distanceToSurface);
                if (leave <= enter) return float4(0, 0, 0, eyeDepth);

                float stepLength = (columnLeave - enter) / 12.0;
                float3 scattered = 0.0;
                float transmission = 1.0;
                [unroll] for (int sampleIndex = 0; sampleIndex < 12; sampleIndex++)
                {
                    float segmentStart = enter + sampleIndex * stepLength;
                    float segmentLength = min(stepLength, max(0.0, leave - segmentStart));
                    if (segmentLength <= 0.0) break;
                    // Only the last partial stratum moves its midpoint;
                    // no sample ever lies behind the opaque endpoint.
                    float3 position = origin + direction * (segmentStart + segmentLength * .5);
                    float opticalDepth = FanDensity(position) * segmentLength;
                    // Exactly empty strata cannot change radiance or transmission.
                    // Keep all 12 anchored strata and the clipped last midpoint.
                    [branch] if (opticalDepth == 0.0) continue;
                    // The index refers to URP's global additional-light
                    // buffer, not a per-object or hard-coded light slot.
                    Light light = GetAdditionalPerObjectLight(_FanLightIndex, position);
                    float shadow = AdditionalLightRealtimeShadow(_FanLightIndex, position, light.direction);
                    float3 irradiance = max((float3)light.color * light.distanceAttenuation, 0.0);
                    float peak = max(irradiance.r, max(irradiance.g, irradiance.b));
                    // Compress only scattered radiance near the physical
                    // source; direct lighting and room exposure stay intact.
                    irradiance /= 1.0 + peak * .35;
                    scattered += transmission * irradiance * shadow * opticalDepth;
                    transmission *= exp(-opticalDepth);
                }
                return float4(scattered, eyeDepth);
            }

            float4 ResolveFanVolume(Varyings input) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
                float2 uv = input.texcoord;
                float raw = FanRawDepth(uv);
                float eyeDepth = FanEyeDepth(raw);
                #if defined(_FAN_VOLUME_DIAGNOSTICS)
                    if (_FanDebugView == 1) return float4((eyeDepth / 32.0).xxx, 1.0);
                    if (_FanDebugView == 2)
                        return float4(SAMPLE_TEXTURE2D_X_LOD(_FanVolumeTexture,
                            sampler_PointClamp, uv, 0).rgb * 16.0, 1.0);
                #endif
                // Conservative projection includes the complete outer tap ring.
                // Camera/near-plane crossings deliberately keep a full-screen bound.
                [branch] if (any(uv < _FanScreenBounds.xy) || any(uv > _FanScreenBounds.zw))
                    return float4(0, 0, 0, 1);
                #if defined(_FAN_VOLUME_DIAGNOSTICS)
                    float3 pathCoverage = float3(0, 1, 0); // Ordinary taps: green.
                #endif
                float2 pixel = uv * _FanVolumeTexelSize.zw - .5;
                float2 cell = floor(pixel), fraction = frac(pixel);
                float depthScale = max(.075, eyeDepth * .025);
                float depthTolerance = max(.25, eyeDepth * .05);
                float3 sum = 0.0;
                float weightSum = 0.0;
                float3 prefixSum = 0.0;
                float prefixWeight = 0.0;
                [unroll] for (int y = 0; y < 2; y++)
                {
                    [unroll] for (int x = 0; x < 2; x++)
                    {
                        float2 sampleUv = (cell + float2(x, y) + .5) * _FanVolumeTexelSize.xy;
                        float4 volumeSample = SAMPLE_TEXTURE2D_X_LOD(_FanVolumeTexture, sampler_PointClamp, sampleUv, 0);
                        float2 axisWeight = lerp(1.0 - fraction, fraction, float2(x, y));
                        float error = abs(volumeSample.a - eyeDepth);
                        float depthWeight = exp2(-error / depthScale) * step(error, depthTolerance);
                        float spatialWeight = axisWeight.x * axisWeight.y;
                        float weight = spatialWeight * depthWeight;
                        sum += volumeSample.rgb * weight;
                        weightSum += weight;
                        // Reuse the already evaluated far-depth weight;
                        // accepting a nearer prefix needs no extra exp2.
                        float safeWeight = spatialWeight * (volumeSample.a <= eyeDepth ? 1.0 : depthWeight);
                        prefixSum += volumeSample.rgb * safeWeight;
                        prefixWeight += safeWeight;
                    }
                }
                float3 volume = weightSum > .00001 ? sum / weightSum : 0.0;
                // A sub-pixel rail can occupy all four low-resolution
                // depth samples while this full-resolution pixel sees air
                // behind it. Returning zero there cuts rectangular holes
                // into the beam. Use the four central prefixes plus the
                // bounded outer ring when normal taps have little support.
                [branch] if (weightSum < .05)
                {
                    float3 fallbackSum = prefixSum;
                    float fallbackWeight = prefixWeight;
                    [unroll] for (int y = -1; y <= 2; y++)
                    {
                        [unroll] for (int x = -1; x <= 2; x++)
                        {
                            if (x >= 0 && x <= 1 && y >= 0 && y <= 1) continue;
                            float2 sampleCell = cell + float2(x, y);
                            float2 sampleUv = (sampleCell + .5) * _FanVolumeTexelSize.xy;
                            float4 volumeSample = SAMPLE_TEXTURE2D_X_LOD(_FanVolumeTexture,
                                sampler_PointClamp, sampleUv, 0);
                            float2 offset = sampleCell - pixel;
                            float weight = FanPrefixDepthWeight(volumeSample.a, eyeDepth,
                                depthScale, depthTolerance) /
                                (1.0 + dot(offset, offset));
                            fallbackSum += volumeSample.rgb * weight;
                            fallbackWeight += weight;
                        }
                    }
                    [branch] if (fallbackWeight > .00001)
                    {
                        volume = lerp(fallbackSum / fallbackWeight, volume, saturate(weightSum / .05));
                        #if defined(_FAN_VOLUME_DIAGNOSTICS)
                            pathCoverage = float3(0, 0, 1); // Safe prefix/ring: blue.
                        #endif
                    }
                    else
                    {
                        // A thin foreground rail can be absent from every
                        // low-resolution depth tap. Farther air cannot be
                        // borrowed onto it, but returning zero erases the
                        // real fog in front of it. Retrace only this unsupported
                        // full-resolution pixel, clipped to its own surface.
                        volume = FanIntegrateRay(uv, raw).rgb;
                        #if defined(_FAN_VOLUME_DIAGNOSTICS)
                            pathCoverage = float3(1, 0, 0); // Exact repair: red.
                        #endif
                    }
                }
                #if defined(_FAN_VOLUME_DIAGNOSTICS)
                    if (_FanDebugView == 3) return float4(volume * 16.0, 1.0);
                    if (_FanDebugView == 4) return float4(pathCoverage, 1.0);
                #endif
                // Every repaired foreground ray still ends at its own
                // opaque surface; no farther scattering is filled across it.
                return float4(volume, 0);
            }
        ENDHLSL

        Pass
        {
            Name "FanVolumeIntegration"
            HLSLPROGRAM
                #pragma target 3.5
                #pragma vertex Vert
                #pragma fragment IntegrateFanVolume
                #pragma multi_compile_fragment _ _ADDITIONAL_LIGHT_SHADOWS
                #pragma multi_compile_fragment _ _SHADOWS_SOFT _SHADOWS_SOFT_LOW _SHADOWS_SOFT_MEDIUM _SHADOWS_SOFT_HIGH
                #pragma multi_compile _ _CLUSTER_LIGHT_LOOP

                float4 IntegrateFanVolume(Varyings input) : SV_Target
                {
                    UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
                    return FanIntegrateRay(input.texcoord, FanRawDepth(input.texcoord));
                }
            ENDHLSL
        }

        Pass
        {
            Name "FanVolumeDepthResolve"
            // Read destination through fixed-function blending only. Alpha is retained.
            Blend One One
            ColorMask RGB
            HLSLPROGRAM
                #pragma target 3.5
                #pragma vertex Vert
                #pragma fragment ResolveFanVolume
                #pragma multi_compile_fragment _ _ADDITIONAL_LIGHT_SHADOWS
                #pragma multi_compile_fragment _ _SHADOWS_SOFT _SHADOWS_SOFT_LOW _SHADOWS_SOFT_MEDIUM _SHADOWS_SOFT_HIGH
                #pragma multi_compile _ _CLUSTER_LIGHT_LOOP
                #pragma shader_feature_local_fragment _FAN_VOLUME_DIAGNOSTICS
            ENDHLSL
        }
        Pass
        {
            Name "FanVolumeDiagnostic"
            Blend Off
            ColorMask RGBA
            HLSLPROGRAM
                #pragma target 3.5
                #pragma vertex Vert
                #pragma fragment ResolveFanVolume
                #pragma multi_compile_fragment _ _ADDITIONAL_LIGHT_SHADOWS
                #pragma multi_compile_fragment _ _SHADOWS_SOFT _SHADOWS_SOFT_LOW _SHADOWS_SOFT_MEDIUM _SHADOWS_SOFT_HIGH
                #pragma multi_compile _ _CLUSTER_LIGHT_LOOP
                #pragma shader_feature_local_fragment _FAN_VOLUME_DIAGNOSTICS
            ENDHLSL
        }
    }
    Fallback Off
}

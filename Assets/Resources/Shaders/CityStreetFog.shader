Shader "Bar Promenade/City Street Fog"
{
    Properties
    {
        [HideInInspector] _FogMotion("Field phase and opacity loss", Vector) = (0, 0, 0, 0)
        [HideInInspector] _StreetFlow("Wind travel and roll", Vector) = (0, 0, 0, 1)
        [HideInInspector] _StreetObserver("Hero centre", Vector) = (0, 0, 0, 0)
        [HideInInspector] _DensityNoise("Air density", 3D) = "" {}
    }
    SubShader
    {
        Tags { "RenderPipeline" = "UniversalPipeline" "Queue" = "Transparent" "RenderType" = "Transparent" }
        Pass
        {
            Tags { "LightMode" = "UniversalForward" }
            // A back face also covers the view from inside a volume. Opaque scene
            // depth clips the visible interval even when the back face is behind a wall.
            Cull Front
            ZWrite Off
            ZTest LEqual
            Blend SrcAlpha OneMinusSrcAlpha

            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex FogVertex
            #pragma fragment FogFragment
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"

            CBUFFER_START(UnityPerMaterial)
                float4 _FogMotion;
                float4 _StreetFlow;
                float4 _StreetObserver;
            CBUFFER_END
            TEXTURE3D(_DensityNoise);
            SAMPLER(sampler_DensityNoise);
            static const float HeightRatio = .49;

            struct Attributes
            {
                float3 positionOS : POSITION;
                half4 color : COLOR;
                float3 centerOS : TEXCOORD0;
            };
            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                float4 centerAndRadius : TEXCOORD1;
                half opacity : TEXCOORD2;
            };

            Varyings FogVertex(Attributes input)
            {
                Varyings output;
                float3 center = TransformObjectToWorld(input.centerOS);
                float3 corner = TransformObjectToWorld(input.positionOS) - center;
                float radius = max(.001, length(corner));
                output.centerAndRadius = float4(center, rcp(radius));
                corner.y *= HeightRatio;
                // Slightly expand the faceted proxy around the analytic ellipsoid.
                output.positionWS = center + 1.025 * corner;
                output.positionCS = TransformWorldToHClip(output.positionWS);
                // Conservative front depth lets the GPU skip volumes wholly behind a wall.
                float frontDepth = max(_ProjectionParams.y, -TransformWorldToView(center).z - radius);
                float4 frontCS = mul(UNITY_MATRIX_P, float4(0, 0, -frontDepth, 1));
                output.positionCS.z = (frontCS.z / frontCS.w) * output.positionCS.w;
                output.opacity = input.color.a * (1 - _FogMotion.w);
                return output;
            }

            half4 FogFragment(Varyings input) : SV_Target
            {
                float3 eye = GetCameraPositionWS();
                float3 ray = normalize(input.positionWS - eye);
                float3 metric = float3(1, 1 / HeightRatio, 1) * input.centerAndRadius.w;
                float3 origin = (eye - input.centerAndRadius.xyz) * metric;
                float3 direction = ray * metric;
                float a = dot(direction, direction);
                float closest = -dot(origin, direction) / a;
                float3 atClosest = origin + closest * direction;
                float core = 1 - dot(atClosest, atClosest);
                clip(core);
                float halfChord = sqrt(max(0, core) / a);

                float2 uv = GetNormalizedScreenSpaceUV(input.positionCS);
                float eyeDepth = LinearEyeDepth(SampleSceneDepth(uv), _ZBufferParams);
                float sceneDistance = eyeDepth / max(.001, dot(ray, GetViewForwardDir()));
                const float clearRadius = 6.5;
                float3 toHero = _StreetObserver.xyz - eye;
                float heroDistanceSq = dot(toHero, toHero);
                float nearLimit = clearRadius;
                if (heroDistanceSq < clearRadius * clearRadius)
                {
                    // In third person the camera is behind the hero. Start outside both clear regions.
                    float along = dot(toHero, ray);
                    nearLimit = max(nearLimit, along + sqrt(along * along + clearRadius * clearRadius - heroDistanceSq));
                }
                float entry = max(closest - halfChord, nearLimit);
                float exitDistance = min(min(closest + halfChord, sceneDistance), 22.0);
                float thickness = exitDistance - entry;
                clip(thickness - .001);

                // Exact average of the soft quadratic density over the visible chord.
                float mid = .5 * (entry + exitDistance);
                float meanDensity = saturate(core - a * ((mid - closest) * (mid - closest) +
                    thickness * thickness / 12));
                float3 p = eye + mid * ray;
                float3 q = p * float3(1.0 / 36, .043, 1.0 / 36);
                q.xz -= _StreetFlow.xy;
                // Two world-space scales move through each other. Vertical displacement of
                // the fine folds follows the coarse bank, so the whole street never pulses.
                half bank = SAMPLE_TEXTURE3D(_DensityNoise, sampler_DensityNoise, q).r;
                float3 folds = q.zxy * 2 + float3(.23, .51, .16) * bank;
                folds.xy += .046 * _StreetFlow.zw;
                half fold = SAMPLE_TEXTURE3D(_DensityNoise, sampler_DensityNoise, folds).r;
                half billow = smoothstep(.27h, .68h, bank * .60h + fold * .40h);
                float distanceFade = smoothstep(clearRadius, 11.0, min(mid, distance(p, _StreetObserver.xyz))) * (1 - smoothstep(14.0, 22.0, mid));
                // A short sightline ending on a nearby wall should stay readable
                // even when several moving ellipsoids overlap in front of it.
                float3 surface = eye + sceneDistance * ray;
                float surfaceDistance = min(sceneDistance, distance(surface, _StreetObserver.xyz));
                float surfaceFade = smoothstep(10.0, 18.0, surfaceDistance);
                float opticalDepth = thickness * meanDensity * distanceFade * surfaceFade * (.06 + 2.6 * billow) * input.opacity;
                half height = (p.y - input.centerAndRadius.y) * metric.y;
                half skyScatter = lerp(.84h, 2.35h, saturate(.36h + .39h * height + .55h * fold));
                // Practical lights already scatter through City's local-light volume pass.
                return half4(unity_FogColor.rgb * skyScatter, 1 - exp2(-2.85 * opticalDepth));
            }
            ENDHLSL
        }
    }
}

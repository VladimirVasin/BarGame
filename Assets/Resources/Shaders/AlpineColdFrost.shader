Shader "Bar Promenade/Alpine Cold Frost"
{
    Properties
    {
        [NoScaleOffset] _FrostMask("Frost Mask", 2D) = "black" {}
        [NoScaleOffset] _FrostBlurTexture("Diffused Scene", 2D) = "black" {}
        _FrostWindow("Frost Window", Vector) = (0, 1, 1.777778, 1)
        _FrostBlurStep("Blur Sigma UV", Vector) = (0, 0, 0, 0)
    }
    SubShader
    {
        Tags { "RenderPipeline" = "UniversalPipeline" }
        ZWrite Off Cull Off ZTest Always
        HLSLINCLUDE
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.core/Runtime/Utilities/Blit.hlsl"

            // amount, centered image-width fraction, visible image aspect, diffusion enabled.
            // There is deliberately no clock: growth reveals one fixed field.
            float4 _FrostWindow;
            float4 _FrostBlurStep;
            TEXTURE2D(_FrostMask);
            TEXTURE2D(_FrostBlurTexture);

            float FrostHash(float2 p)
            {
                return frac(sin(dot(p, float2(127.1, 311.7))) * 43758.5453);
            }

            float FrostNoise(float2 p)
            {
                float2 cell = floor(p);
                float2 blend = frac(p);
                blend = blend * blend * (3.0 - 2.0 * blend);
                return lerp(lerp(FrostHash(cell), FrostHash(cell + float2(1, 0)), blend.x),
                    lerp(FrostHash(cell + float2(0, 1)), FrostHash(cell + 1), blend.x), blend.y);
            }

            half4 FragBlur(Varyings input) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
                float2 uv = input.texcoord;
                float2 imageMin = float2((1.0 - _FrostWindow.y) * 0.5, 0.0);
                float2 imageMax = float2((1.0 + _FrostWindow.y) * 0.5, 1.0);
                // Gaussian +/-3 sigma in half-sigma steps. Horizontal and
                // vertical passes use the same UV space at quarter resolution.
                const float weights[7] = { 0.199676, 0.176213, 0.121109,
                    0.064825, 0.027023, 0.008773, 0.002218 };
                half4 blurred = SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp,
                    clamp(uv, imageMin, imageMax)) * weights[0];
                [unroll] for (int i = 1; i <= 6; i++)
                {
                    float2 offset = _FrostBlurStep.xy * (i * 0.5);
                    blurred += (SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp,
                        clamp(uv + offset, imageMin, imageMax)) +
                        SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp,
                        clamp(uv - offset, imageMin, imageMax))) * weights[i];
                }
                return blurred;
            }

            float FrostFilm(float2 uv, float aspect)
            {
                // Ice is a connected film beneath the needles. Filtering its
                // footprint closes tiny gaps without filling large clear areas;
                // the full-resolution bitmap still draws sharp crystals later.
                float2 radius = float2(0.05 / aspect, 0.05);
                float cloud = SAMPLE_TEXTURE2D(_FrostMask, sampler_LinearClamp, uv).r * 0.25;
                cloud += (SAMPLE_TEXTURE2D(_FrostMask, sampler_LinearClamp, uv + float2(radius.x * 0.5, 0)).r +
                    SAMPLE_TEXTURE2D(_FrostMask, sampler_LinearClamp, uv - float2(radius.x * 0.5, 0)).r +
                    SAMPLE_TEXTURE2D(_FrostMask, sampler_LinearClamp, uv + float2(0, radius.y * 0.5)).r +
                    SAMPLE_TEXTURE2D(_FrostMask, sampler_LinearClamp, uv - float2(0, radius.y * 0.5)).r) * 0.125;
                cloud += (SAMPLE_TEXTURE2D(_FrostMask, sampler_LinearClamp, uv + radius * float2(0.7071, 0.7071)).r +
                    SAMPLE_TEXTURE2D(_FrostMask, sampler_LinearClamp, uv + radius * float2(-0.7071, 0.7071)).r +
                    SAMPLE_TEXTURE2D(_FrostMask, sampler_LinearClamp, uv + radius * float2(0.7071, -0.7071)).r +
                    SAMPLE_TEXTURE2D(_FrostMask, sampler_LinearClamp, uv - radius * 0.7071).r) * 0.0625;
                return smoothstep(0.0005, 0.008, cloud);
            }

            half4 Frag(Varyings input) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
                float2 uv = input.texcoord;
                half4 source = SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, uv);
                float amount = saturate(_FrostWindow.x);
                if (amount <= 0.0) return source;

                // The composite later crops this source rectangle and draws
                // its black bars. Frost belongs to the image, never those bars.
                float2 windowUv = float2((uv.x - 0.5) / _FrostWindow.y + 0.5, uv.y);
                if (any(windowUv < 0.0) || any(windowUv > 1.0)) return source;
                float2 edge = min(windowUv, 1.0 - windowUv);
                float depth = min(edge.x, edge.y);
                if (depth >= 0.18) return source;

                float aspect = max(0.1, _FrostWindow.z);
                float2 fieldUv = windowUv * float2(aspect, 1.0);
                // The authored mask carries fine asymmetric fern crystals,
                // cloudy deposits and the gaps between them. Sample its full
                // frame inside the visible image; growth never scales the art.
                float mask = SAMPLE_TEXTURE2D(_FrostMask, sampler_LinearClamp, windowUv).r;
                mask = smoothstep(0.018, 0.96, mask);
                float film = FrostFilm(windowUv, aspect);
                if (mask <= 0.0 && film <= 0.0) return source;

                // Each patch has its own arrival time. Broad lobes, smaller
                // gaps and the actual crystal branches interrupt the front at
                // every stage, including corners. No moving rectangular clip.
                float broad = FrostNoise(fieldUv * 7.0 + float2(7.3, 2.9));
                float medium = FrostNoise(fieldUv * 23.0 + float2(1.8, 13.2));
                float fine = FrostNoise(fieldUv * 71.0 + float2(24.6, 3.1));
                float corner = 1.0 - smoothstep(0.12, 0.34, length(edge));
                float reach = min(0.18, lerp(0.085, 0.12, broad) + corner * 0.065);
                float boundary = 1.0 - smoothstep(max(0.0, reach - 0.035), reach, depth);
                float arrival = depth / reach * 0.82 + (broad - 0.5) * 0.72 +
                    (medium - 0.5) * 0.36 + (fine - 0.5) * 0.12 - mask * 0.16;
                float reveal = smoothstep(arrival - 0.20, arrival + 0.20, amount) * boundary;
                float deposit = reveal * amount;

                // Translucent ice extends a little beyond its white needles.
                // A soft irregular fringe makes the frozen glass read at game
                // size, within the same protected central rectangle.
                float filmReach = min(0.18, reach + 0.03);
                float filmBoundary = 1.0 - smoothstep(filmReach - 0.055, filmReach, depth);
                float filmArrival = depth / filmReach * 0.82 + (broad - 0.5) * 0.72 +
                    (medium - 0.5) * 0.36 + (fine - 0.5) * 0.12 - film * 0.16;
                float frozenFilm = smoothstep(filmArrival - 0.20, filmArrival + 0.20, amount) * filmBoundary;
                float diffusion = smoothstep(0.0, 0.80, frozenFilm) * film * amount;
                if (deposit <= 0.0 && diffusion <= 0.0) return source;
                if (diffusion > 0.0 && _FrostWindow.w > 0.0)
                {
                    half3 blurred = SAMPLE_TEXTURE2D(_FrostBlurTexture, sampler_LinearClamp, uv).rgb;
                    source.rgb = lerp(source.rgb, blurred, saturate(diffusion));
                }

                float crystals = pow(mask, 0.85);
                float veil = smoothstep(0.015, 0.34, mask);
                float grain = FrostNoise(fieldUv * 183.0);
                float opacity = deposit *
                    (crystals * 0.31 + veil * 0.10) * (0.92 + grain * 0.08);
                half3 frost = lerp(half3(0.48h, 0.53h, 0.49h),
                    half3(0.76h, 0.79h, 0.73h), crystals);
                return half4(lerp(source.rgb, frost, saturate(opacity)), source.a);
            }
        ENDHLSL

        Pass
        {
            Name "EdgeCrystals"
            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex Vert
            #pragma fragment Frag
            ENDHLSL
        }
        Pass
        {
            Name "DiffuseHorizontal"
            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex Vert
            #pragma fragment FragBlur
            ENDHLSL
        }
        Pass
        {
            Name "DiffuseVertical"
            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex Vert
            #pragma fragment FragBlur
            ENDHLSL
        }
    }
    Fallback Off
}

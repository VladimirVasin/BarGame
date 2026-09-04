Shader "Hidden/BarPromenade/PS1Composite"
{
    SubShader
    {
        Tags
        {
            "RenderPipeline" = "UniversalPipeline"
            "RenderType" = "Opaque"
        }

        ZWrite Off
        ZTest Always
        Cull Off

        Pass
        {
            Name "DownsampleRgb555"

            HLSLPROGRAM
            #pragma target 3.0
            #pragma vertex Vert
            #pragma fragment FragDownsample

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.core/Runtime/Utilities/Blit.hlsl"

            float4 _Ps1LowResolutionTexelSize;
            float _Ps1QuantizationStrength;
            float _Ps1DitherStrength;
            float _Ps1AspectFraction;
            float _IntoxicationVignette;
            float _IntoxicationGhostPixels;
            float _IntoxicationWarp;
            float _IntoxicationWarmth;
            float _IntoxicationExposurePulse;
            float _IntoxicationTime;

            float4 FragDownsample(Varyings input) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
                float2 centered = input.texcoord - 0.5;
                float wave =
                    sin(
                        input.texcoord.y * 13.0 +
                        _IntoxicationTime * 1.15) +
                    sin(
                        input.texcoord.x * 8.0 -
                        _IntoxicationTime * 0.73) *
                    0.35;
                float radiusSquared = dot(centered, centered);
                // In 4:3 mode the internal frame reads only the
                // centered 4:3 window of the widescreen source — the
                // exact view of a 4:3 camera with the same vertical
                // FOV. Warp, vignette and rain stay in target space,
                // so they follow the visible frame.
                float2 sourceUv = input.texcoord;
                sourceUv.x =
                    0.5 +
                    (sourceUv.x - 0.5) * _Ps1AspectFraction;
                float2 warpedUv =
                    sourceUv +
                    float2(wave, wave * -0.22) *
                    _IntoxicationWarp +
                    centered *
                    radiusSquared *
                    _IntoxicationWarp *
                    1.8;
                float2 offset =
                    _Ps1LowResolutionTexelSize.xy * 0.25;
                float4 source =
                    SAMPLE_TEXTURE2D_X_LOD(
                        _BlitTexture,
                        sampler_LinearClamp,
                        warpedUv + float2(-offset.x, -offset.y),
                        0.0) +
                    SAMPLE_TEXTURE2D_X_LOD(
                        _BlitTexture,
                        sampler_LinearClamp,
                        warpedUv + float2(offset.x, -offset.y),
                        0.0) +
                    SAMPLE_TEXTURE2D_X_LOD(
                        _BlitTexture,
                        sampler_LinearClamp,
                        warpedUv + float2(-offset.x, offset.y),
                        0.0) +
                    SAMPLE_TEXTURE2D_X_LOD(
                        _BlitTexture,
                        sampler_LinearClamp,
                        warpedUv + float2(offset.x, offset.y),
                        0.0);
                source *= 0.25;

                float2 ghostOffset =
                    _Ps1LowResolutionTexelSize.xy *
                    _IntoxicationGhostPixels *
                    float2(
                        sin(_IntoxicationTime * 0.83),
                        cos(_IntoxicationTime * 1.07) * 0.55);
                float4 ghost =
                    SAMPLE_TEXTURE2D_X_LOD(
                        _BlitTexture,
                        sampler_LinearClamp,
                        warpedUv + ghostOffset,
                        0.0);
                float ghostWeight =
                    saturate(_IntoxicationGhostPixels / 3.0);
                source.rgb = lerp(
                    source.rgb,
                    (source.rgb + ghost.rgb) * 0.5,
                    ghostWeight * 0.38);
                float redSample =
                    SAMPLE_TEXTURE2D_X_LOD(
                        _BlitTexture,
                        sampler_LinearClamp,
                        warpedUv + ghostOffset * 0.7,
                        0.0).r;
                float blueSample =
                    SAMPLE_TEXTURE2D_X_LOD(
                        _BlitTexture,
                        sampler_LinearClamp,
                        warpedUv - ghostOffset * 0.7,
                        0.0).b;
                source.r = lerp(
                    source.r,
                    redSample,
                    ghostWeight * 0.42);
                source.b = lerp(
                    source.b,
                    blueSample,
                    ghostWeight * 0.42);

                source.rgb *=
                    1.0 +
                    sin(_IntoxicationTime * 0.85) *
                    _IntoxicationExposurePulse;
                source.rgb = lerp(
                    source.rgb,
                    source.rgb * float3(1.08, 1.01, 0.94),
                    saturate(_IntoxicationWarmth));
                float edge =
                    saturate((length(centered) - 0.2) / 0.48);
                source.rgb *=
                    1.0 -
                    edge *
                    edge *
                    saturate(_IntoxicationVignette);

                float3 boundedLinear = saturate(source.rgb);
                float3 perceptual = LinearToSRGB(boundedLinear);
                // Ordered Bayer 4x4 in internal-pixel space, at most half
                // an RGB555 step, so flat gradients break into the PS1
                // checker instead of banding.
                float2 lowPixel = floor(
                    input.texcoord *
                    _Ps1LowResolutionTexelSize.zw);
                int bayerCell =
                    (int)fmod(lowPixel.y, 4.0) * 4 +
                    (int)fmod(lowPixel.x, 4.0);
                float bayer4[16] =
                {
                    0.0, 8.0, 2.0, 10.0,
                    12.0, 4.0, 14.0, 6.0,
                    3.0, 11.0, 1.0, 9.0,
                    15.0, 7.0, 13.0, 5.0
                };
                float ditherThreshold =
                    (bayer4[bayerCell] + 0.5) / 16.0 - 0.5;
                perceptual +=
                    ditherThreshold *
                    (_Ps1DitherStrength / 31.0);
                float3 quantizedPerceptual =
                    floor(perceptual * 31.0 + 0.5) / 31.0;
                float3 quantizedLinear =
                    SRGBToLinear(quantizedPerceptual);
                source.rgb +=
                    (quantizedLinear - boundedLinear) *
                    saturate(_Ps1QuantizationStrength);
                return source;
            }
            ENDHLSL
        }

        Pass
        {
            Name "PointUpscale"

            HLSLPROGRAM
            #pragma target 3.0
            #pragma vertex Vert
            #pragma fragment FragUpscale

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.core/Runtime/Utilities/Blit.hlsl"

            float4 _Ps1LowResolutionTexelSize;
            float _Ps1ScanlineIntensity;
            float _Ps1AspectFraction;

            float4 FragUpscale(Varyings input) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
                // 4:3 pillarbox: the internal frame occupies the
                // centered fraction of the output; the bars stay pure
                // black under the retro overlay.
                float halfBar = (1.0 - _Ps1AspectFraction) * 0.5;
                if (input.texcoord.x < halfBar ||
                    input.texcoord.x > 1.0 - halfBar)
                {
                    return float4(0.0, 0.0, 0.0, 1.0);
                }

                float2 sampleUv = input.texcoord;
                sampleUv.x =
                    (sampleUv.x - halfBar) / _Ps1AspectFraction;
                float4 color = SAMPLE_TEXTURE2D_X_LOD(
                    _BlitTexture,
                    sampler_PointClamp,
                    sampleUv,
                    0.0);
                // Darken the leading third of each internal row so the
                // point upscale reads as a CRT lattice. A step keeps the
                // line visible at every integer scale; a symmetric cosine
                // would cancel out at exactly 2x (both output rows land
                // on mirrored phases).
                float rowPhase = frac(
                    input.texcoord.y *
                    _Ps1LowResolutionTexelSize.w);
                color.rgb *=
                    1.0 -
                    _Ps1ScanlineIntensity *
                    step(rowPhase, 0.34);
                return color;
            }
            ENDHLSL
        }

        // The Begotten print. Three passes replace the point upscale:
        // the internal frame is reduced to a soft perceptual luminance at
        // half size, blurred again at quarter size for the halation and
        // the scene mean, and printed at output size with no mid-tones -
        // grain decides the boundary, the lamp flickers, the frame weaves
        // in the gate, scratches and dust lie on the stock.
        Pass
        {
            Name "BegottenSoft"

            HLSLPROGRAM
            #pragma target 3.0
            #pragma vertex Vert
            #pragma fragment FragSoft

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.core/Runtime/Utilities/Blit.hlsl"

            float4 _Ps1LowResolutionTexelSize;

            float4 FragSoft(Varyings input) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
                // Four bilinear taps half a source texel out: a sixteen
                // texel footprint, the softness of a rephotographed
                // sixteen millimetre frame.
                float2 offset = _Ps1LowResolutionTexelSize.xy * 0.5;
                float3 sum =
                    SAMPLE_TEXTURE2D_X_LOD(
                        _BlitTexture,
                        sampler_LinearClamp,
                        input.texcoord + float2(-offset.x, -offset.y),
                        0.0).rgb +
                    SAMPLE_TEXTURE2D_X_LOD(
                        _BlitTexture,
                        sampler_LinearClamp,
                        input.texcoord + float2(offset.x, -offset.y),
                        0.0).rgb +
                    SAMPLE_TEXTURE2D_X_LOD(
                        _BlitTexture,
                        sampler_LinearClamp,
                        input.texcoord + float2(-offset.x, offset.y),
                        0.0).rgb +
                    SAMPLE_TEXTURE2D_X_LOD(
                        _BlitTexture,
                        sampler_LinearClamp,
                        input.texcoord + float2(offset.x, offset.y),
                        0.0).rgb;
                float luminance = dot(
                    saturate(sum * 0.25),
                    float3(0.2126, 0.7152, 0.0722));
                // Stored perceptually: the threshold is judged by eye,
                // and an 8-bit channel spends its steps where they show.
                float perceptual = LinearToSRGB(luminance);
                return float4(perceptual, perceptual, perceptual, 1.0);
            }
            ENDHLSL
        }

        Pass
        {
            Name "BegottenGlow"

            HLSLPROGRAM
            #pragma target 3.0
            #pragma vertex Vert
            #pragma fragment FragGlow

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.core/Runtime/Utilities/Blit.hlsl"

            float4 _Ps1LowResolutionTexelSize;

            float FragGlow(Varyings input) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
                // Nine-tap tent one and a half soft texels wide, on top
                // of the bilinear reduction: the halo the burnt whites
                // throw into the black next to them.
                float2 texel = _Ps1LowResolutionTexelSize.xy * 2.0;
                float2 step = texel * 1.5;
                float sum = 0.0;
                float weightSum = 0.0;
                [unroll]
                for (int y = -1; y <= 1; y++)
                {
                    [unroll]
                    for (int x = -1; x <= 1; x++)
                    {
                        float weight = (2.0 - abs(x)) * (2.0 - abs(y));
                        sum +=
                            SAMPLE_TEXTURE2D_X_LOD(
                                _BlitTexture,
                                sampler_LinearClamp,
                                input.texcoord + float2(x, y) * step,
                                0.0).r *
                            weight;
                        weightSum += weight;
                    }
                }

                return sum / weightSum;
            }
            ENDHLSL
        }

        Pass
        {
            Name "BegottenLevels"

            HLSLPROGRAM
            #pragma target 3.0
            #pragma vertex Vert
            #pragma fragment FragLevels

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.core/Runtime/Utilities/Blit.hlsl"

            // One pixel: the scene's light as mean, deviation and peak,
            // from a 12x12 sweep of the glow. The print is exposed for
            // the scene the way a printer exposes for a negative: a
            // night street spreads across the whole scale instead of
            // sitting under one fixed threshold as a field of noise.
            float4 FragLevels(Varyings input) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
                float sum = 0.0;
                float sumOfSquares = 0.0;
                float peak = 0.0;
                [loop]
                for (int y = 0; y < 12; y++)
                {
                    [loop]
                    for (int x = 0; x < 12; x++)
                    {
                        float light = SAMPLE_TEXTURE2D_X_LOD(
                            _BlitTexture,
                            sampler_LinearClamp,
                            float2((x + 0.5) / 12.0, (y + 0.5) / 12.0),
                            0.0).r;
                        sum += light;
                        sumOfSquares += light * light;
                        peak = max(peak, light);
                    }
                }

                float mean = sum / 144.0;
                float variance = max(0.0, sumOfSquares / 144.0 - mean * mean);
                return float4(mean, sqrt(variance), peak, 1.0);
            }
            ENDHLSL
        }

        Pass
        {
            Name "BegottenPrint"

            HLSLPROGRAM
            #pragma target 3.0
            #pragma vertex Vert
            #pragma fragment FragPrint

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.core/Runtime/Utilities/Blit.hlsl"
            #include "BegottenFilm.hlsl"

            TEXTURE2D(_BegottenGlowTex);
            TEXTURE2D(_BegottenLevelsTex);

            float4 _Ps1LowResolutionTexelSize;
            float _Ps1AspectFraction;
            // x, y: one output pixel of the visible window in window UV;
            // z, w: the window in pixels.
            float4 _BegottenOutputTexelSize;
            float _BegottenSeed;
            // x, y: gate weave in soft UV; z: the frame slip in UV.
            float4 _BegottenGate;
            float _BegottenThreshold;
            float _BegottenExposure;
            float _BegottenGrainCell;
            float4 _BegottenScratch0;
            float4 _BegottenScratch1;
            float4 _BegottenScratch2;

            float SampleSoft(float2 uv)
            {
                return SAMPLE_TEXTURE2D_X_LOD(
                    _BlitTexture,
                    sampler_LinearClamp,
                    uv,
                    0.0).r;
            }

            float SampleGlow(float2 uv)
            {
                return SAMPLE_TEXTURE2D_LOD(
                    _BegottenGlowTex,
                    sampler_LinearClamp,
                    uv,
                    0.0).r;
            }

            float4 FragPrint(Varyings input) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
                // The 1.33:1 gate: bars outside it stay pure black.
                float halfBar = (1.0 - _Ps1AspectFraction) * 0.5;
                if (input.texcoord.x < halfBar ||
                    input.texcoord.x > 1.0 - halfBar)
                {
                    return float4(0.0, 0.0, 0.0, 1.0);
                }

                float2 windowUv = input.texcoord;
                windowUv.x = (windowUv.x - halfBar) / _Ps1AspectFraction;
                float2 outputPixel = windowUv * _BegottenOutputTexelSize.zw;

                // The frame sits a little off the gate and, rarely,
                // slips a few lines. Past the frame's edge the stock is
                // unexposed.
                float2 gateUv =
                    windowUv +
                    _BegottenGate.xy +
                    float2(0.0, _BegottenGate.z);
                float inside =
                    step(0.0, gateUv.x) * step(gateUv.x, 1.0) *
                    step(0.0, gateUv.y) * step(gateUv.y, 1.0);

                float2 softTexel = _Ps1LowResolutionTexelSize.xy * 2.0;
                float2 offset = softTexel * 0.75;
                float luminance =
                    (SampleSoft(gateUv + float2(-offset.x, -offset.y)) +
                     SampleSoft(gateUv + float2(offset.x, -offset.y)) +
                     SampleSoft(gateUv + float2(-offset.x, offset.y)) +
                     SampleSoft(gateUv + float2(offset.x, offset.y))) *
                    0.25;
                float glow = SampleGlow(gateUv);

                // The lamp and the vignette act on the light, before the
                // print decides: a dark corner is black with a boiling
                // edge, never grey.
                float2 centered = (windowUv - 0.5) * float2(1.3333, 1.0);
                float edge = length(centered);
                float lowNoise =
                    BegottenValueNoise(windowUv * 3.0, _BegottenSeed * 0.11) -
                    0.5;
                float vignette =
                    1.0 - smoothstep(0.55, 1.05, edge + lowNoise * 0.08);
                float light = _BegottenExposure * vignette * inside;
                luminance *= light;
                glow *= light;

                // Exposed for the scene: the mean of the light prints at
                // the middle of the scale and two deviations reach
                // either end of it (a deviation never counts for less
                // than a twentieth). The frame's common tone - the fog,
                // the sky - lands above the threshold roll and burns to
                // bone; a road a deviation darker prints as sparse grain
                // rather than solid soot, so a black figure still stands
                // on it; two deviations down is soot. A touch of local
                // contrast (the light against its own blur) rims a
                // silhouette against a tone like its own. This is what
                // keeps a night street readable instead of one field of
                // grain under a fixed threshold.
                float3 levels = SAMPLE_TEXTURE2D_LOD(
                    _BegottenLevelsTex,
                    sampler_PointClamp,
                    float2(0.5, 0.5),
                    0.0).rgb;
                float mean = levels.x;
                float scale = max(levels.y, 0.05) * 4.0;
                float local = luminance + (luminance - glow) * 0.5;
                float exposed = 0.5 + (local - mean) / scale;
                float exposedGlow = 0.5 + (glow - mean) / scale;
                float threshold = _BegottenThreshold;

                float grain = BegottenGrain(
                    outputPixel,
                    _BegottenGrainCell,
                    _BegottenSeed);
                float value = smoothstep(
                    threshold - 0.06,
                    threshold + 0.06,
                    exposed + grain);

                // Halation: the burnt whites bleed into the black.
                float halo = smoothstep(
                    threshold + 0.15,
                    threshold + 0.5,
                    exposedGlow);
                value = saturate(value + halo * (1.0 - value) * 0.7);

                // The stock.
                float2 dust = BegottenDust(outputPixel, _BegottenSeed);
                value = lerp(value, 1.0, dust.x * (1.0 - value));
                value = lerp(value, 0.0, dust.y * value);
                float hair = BegottenHair(outputPixel, _BegottenSeed);
                value = lerp(value, 1.0 - step(0.5, value), hair);

                float texelWidth = _BegottenOutputTexelSize.x;
                float scratch0 = BegottenScratch(
                    _BegottenScratch0, windowUv, texelWidth, _BegottenSeed);
                value = lerp(
                    value, step(0.0, _BegottenScratch0.y), scratch0);
                float scratch1 = BegottenScratch(
                    _BegottenScratch1, windowUv, texelWidth, _BegottenSeed + 3.0);
                value = lerp(
                    value, step(0.0, _BegottenScratch1.y), scratch1);
                float scratch2 = BegottenScratch(
                    _BegottenScratch2, windowUv, texelWidth, _BegottenSeed + 6.0);
                value = lerp(
                    value, step(0.0, _BegottenScratch2.y), scratch2);

                // The print: soot and bone, never pure, back to linear
                // for the final blit to encode.
                float print = lerp(0.015, 0.93, value) * inside;
                return float4(SRGBToLinear(print.xxx), 1.0);
            }
            ENDHLSL
        }
    }

    Fallback Off
}

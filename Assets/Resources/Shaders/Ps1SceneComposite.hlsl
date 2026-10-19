#ifndef BARPROMENADE_SCENE_COMPOSITE_INCLUDED
#define BARPROMENADE_SCENE_COMPOSITE_INCLUDED

#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
#include "Packages/com.unity.render-pipelines.core/Runtime/Utilities/Blit.hlsl"

float4 _Ps1LowResolutionTexelSize;
float _Ps1QuantizationStrength;
float _Ps1DitherStrength;
float4 _IntoxicationPixelSize;
float _Ps1AspectFraction;
float _IntoxicationVignette;
float _IntoxicationGhostPixels;
float _IntoxicationWarp;
float _IntoxicationWarmth;
float _IntoxicationExposurePulse;
float _IntoxicationTime;
// (eye.x, eye.y, frame aspect, signed twist in radians) and
// (calm disc radius, radial pull, core drift in internal pixels).
float4 _IntoxicationVertigo;
float4 _IntoxicationVertigoShape;

// The drunk vertigo whirlpool. The frame is wound around the
// hero rather than around the middle of the screen: his own disc
// is left alone and only floats a couple of internal pixels,
// while everything outside it is twisted progressively harder,
// reaching the full angle at the frame corner farthest from him.
// Mirrored on the CPU by BarPromenade.Rendering.
// IntoxicationWhirlpool, which the contract test compares
// against this source.
float2 ApplyVertigoWhirlpool(float2 uv)
{
    float twist = _IntoxicationVertigo.w;
    float2 core = _IntoxicationVertigoShape.zw;
    if (twist == 0.0 && dot(core, core) == 0.0)
    {
        // Still water: the sober frame stays bit-exact, and the
        // boundary margin below never gets to zoom the picture.
        return uv;
    }

    float2 centre = _IntoxicationVertigo.xy;
    float aspect = _IntoxicationVertigo.z;
    float inner = _IntoxicationVertigoShape.x;
    float pull = _IntoxicationVertigoShape.y;

    float2 p = (uv - centre) * float2(aspect, 1.0);
    float radius = length(p);
    float2 far = float2(
        max(centre.x, 1.0 - centre.x) * aspect,
        max(centre.y, 1.0 - centre.y));
    float outer = max(length(far), inner + 0.001);
    float t = saturate(
        (radius - inner) /
        max(0.001, outer - inner));
    float profile = t * t * (3.0 - 2.0 * t);

    float sine;
    float cosine;
    sincos(twist * profile, sine, cosine);
    float2 rotated = float2(
        p.x * cosine - p.y * sine,
        p.x * sine + p.y * cosine);
    rotated *= 1.0 - pull * profile;

    float2 delta = rotated / float2(aspect, 1.0);
    delta +=
        core *
        _IntoxicationPixelSize.xy *
        saturate(1.0 - radius / inner);
    // The radius is shortened to the frame's own boundary along
    // the twisted direction, so the smear points into the vortex
    // instead of becoming an axis-aligned clamp at the edge.
    float2 wall = float2(
        delta.x >= 0.0 ? 0.999 - centre.x : centre.x - 0.001,
        delta.y >= 0.0 ? 0.999 - centre.y : centre.y - 0.001);
    float2 inverse = 1.0 / (abs(delta) + 1e-6);
    float scale = min(
        1.0,
        min(wall.x * inverse.x, wall.y * inverse.y));
    return centre + delta * scale;
}

float4 SampleSceneComposite(float2 targetUv, float4 sampleTexelSize,
    bool averageSource, float quantizationStrength, float ditherStrength)
{
    float2 centered = targetUv - 0.5;
    float wave =
        sin(
            targetUv.y * 13.0 +
            _IntoxicationTime * 1.15) +
        sin(
            targetUv.x * 8.0 -
            _IntoxicationTime * 0.73) *
        0.35;
    float radiusSquared = dot(centered, centered);
    // In 4:3 mode the internal frame reads only the
    // centered 4:3 window of the widescreen source — the
    // exact view of a 4:3 camera with the same vertical
    // FOV. Warp, vignette and rain stay in target space,
    // so they follow the visible frame; the whirlpool is
    // wound in that same target space, before the crop.
    float2 sourceUv = ApplyVertigoWhirlpool(targetUv);
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
        sampleTexelSize.xy * 0.25;
    float4 source;
    if (averageSource)
    {
        source =
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
    }
    else
    {
        source = SAMPLE_TEXTURE2D_X_LOD(
            _BlitTexture, sampler_LinearClamp, warpedUv, 0.0);
    }

    float2 ghostOffset =
        _IntoxicationPixelSize.xy *
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

    if (quantizationStrength == 0.0)
        return source;

    float3 boundedLinear = saturate(source.rgb);
    float3 perceptual = LinearToSRGB(boundedLinear);
    // Ordered Bayer 4x4 in internal-pixel space, at most half
    // an RGB555 step, so flat gradients break into the PS1
    // checker instead of banding.
    float2 lowPixel = floor(
        targetUv *
        sampleTexelSize.zw);
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
        (ditherStrength / 31.0);
    float3 quantizedPerceptual =
        floor(perceptual * 31.0 + 0.5) / 31.0;
    float3 quantizedLinear =
        SRGBToLinear(quantizedPerceptual);
    source.rgb +=
        (quantizedLinear - boundedLinear) *
        saturate(quantizationStrength);
    return source;
}

#endif

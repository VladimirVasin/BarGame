#ifndef BARPROMENADE_PS1_VERTEX_JITTER_INCLUDED
#define BARPROMENADE_PS1_VERTEX_JITTER_INCLUDED

// The PlayStation had no sub-pixel precision: its GPU took whole-number
// screen coordinates, so a vertex did not slide across the screen as the
// camera moved, it jumped from pixel to pixel. Neighbouring vertices
// jumped on different frames, and the triangle between them boiled. That
// is the whole effect, reproduced here by rounding the projected position
// to the grid the frame is actually presented on.
//
// Which grid matters. The scene rasterizes at full resolution and only
// becomes a PS1 image in post, where Ps1CompositeRendererFeature squeezes
// it into 640x360 (or one of the other presets) and point-upscales. Snap
// to the framebuffer and the step lands three times finer than a visible
// pixel - invisible. So the grid is pushed in from the composite, which is
// the only place that knows both the internal resolution and the 4:3 crop
// fraction that shifts it.
//
// The parameters arrive as a global rather than a material property on
// purpose: UnityPerMaterial has to stay byte-identical to stock URP Lit or
// the SRP Batcher stops batching every renderer in the game. URP itself
// declares _LightDirection this way in ShadowCasterPass.hlsl. An unset
// global reads as zero, which means strength zero, which means an exact
// passthrough - so material thumbnails, asset previews and any camera
// rendered without the feature keep the stock image for free.
//
// x, y: half the internal grid count (NDC spans -1..1, hence the halving
//       done on the C# side). z: strength 0..1. w: unused.
float4 _Ps1VertexSnapParams;

// Past this the vertex is off screen regardless, and the multiply back by
// w is where a huge NDC would overflow.
#define PS1_SNAP_GUARD_BAND 64.0

float4 Ps1SnapClipPosition(float4 positionCS)
{
    float strength = _Ps1VertexSnapParams.z;

    // Load-bearing early out. Returning the argument untouched - no
    // divide, no multiply - is what makes strength zero bit-identical to
    // stock URP Lit. Falling through to (xy / w) * w would drift by an ULP
    // and the pixel-identity test would fail along silhouettes.
    if (strength <= 0.0)
    {
        return positionCS;
    }

    // Behind or on the camera plane the perspective divide is meaningless,
    // and an inf or NaN here deletes the whole triangle. Written as a
    // negated comparison so that a NaN w also takes this branch.
    float w = positionCS.w;
    if (!(w > 1e-4))
    {
        return positionCS;
    }

    float2 grid = _Ps1VertexSnapParams.xy;
    if (!(grid.x > 0.0) || !(grid.y > 0.0))
    {
        return positionCS;
    }

    float2 ndc = positionCS.xy / w;

    // floor(v + 0.5) rather than round(): HLSL's round() breaks ties to
    // even on some backends and away from zero on others, and the forward,
    // depth and depth-normals passes have to agree to the bit or SSAO
    // haloes along every silhouette.
    float2 snapped = floor(ndc * grid + 0.5) / grid;

    float2 inBand = step(abs(ndc), PS1_SNAP_GUARD_BAND);
    positionCS.xy = lerp(ndc, snapped, strength * inBand) * w;

    // z and w are left alone. Depth stays bit-identical to the unsnapped
    // transform, so the prepass can never disagree with the forward pass,
    // and perspective correction is untouched - this is vertex jitter
    // only, not the affine texture warping that came with it on hardware.
    return positionCS;
}

#endif

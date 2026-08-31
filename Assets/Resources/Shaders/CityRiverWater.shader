// The city's water surface: the river and the lake now, the sea next.
//
// Everything is a function of world position rather than of UV, which is
// what lets one segment's sheet meet the next without a seam and what
// lets the same shader serve footprints that are nothing like a channel.
// `_FlowDirection` is the only thing that says "river": set it to zero
// and the same material is still water, water that is not going
// anywhere - the crests stop travelling and stand and breathe instead,
// and the ripple sheet orbits rather than drifts.
//
// The lake also switches on `_AdditionalSpecular`, which lets the
// city's own lamps glint on the water. Nothing else in the city needed
// that, because nothing else in the city is still enough to hold a
// reflection; a pond with lamps standing over it is.
//
// It renders in the transparent queue but writes opaque pixels. The
// blend against what is behind the water is done here, from the depth
// and opaque copies URP already captures, so the surface never has to
// sort against anything and stays a correct occluder for the halos and
// atmosphere particles that draw after it.
Shader "Bar Promenade/City River Water"
{
    Properties
    {
        // Rendered tone, not albedo. The sea's flat `(0.10, 0.29, 0.38)`
        // is an albedo on a lit material and reaches the screen at a
        // fraction of itself; this shader composites its own colour and
        // would emit it whole, which puts a tropical lagoon in a grimy
        // city. These are that hue family brought down to what the old
        // flat river actually rendered at, which was art-directed to sit
        // in this palette.
        _BaseColor("Shallow Color", Color) = (0.105, 0.205, 0.205, 1)
        _DeepColor("Deep Color", Color) = (0.070, 0.175, 0.200, 1)
        _HighlightColor("Highlight Color", Color) = (0.24, 0.32, 0.29, 1)
        _NightFactor("Night Factor", Range(0, 1)) = 0
        _RainIntensity("Rain Intensity", Range(0, 1)) = 0
        _FlowSpeed("Flow Speed", Range(0, 2)) = 0.55
        _FlowDirection("Flow Direction (XZ)", Vector) = (0, 1, 0, 0)

        _RippleMap("Ripple Normal", 2D) = "bump" {}
        _FoamMap("Foam Mask", 2D) = "black" {}
        _RippleTiling("Ripple Metres Per Tile", Float) = 4.0
        _FoamTiling("Foam Metres Per Tile", Float) = 3.0
        _NormalStrength("Normal Strength", Range(0, 4)) = 1.6

        _WaveHeight("Wave Height", Range(0, 0.35)) = 0.08
        _WaveLength("Wave Length", Float) = 4.8

        // The mattress's lessons, carried to a surface too big to write
        // on the CPU. The bed reads because its geometry is honest and
        // its shading lies louder than the geometry: lateral normals
        // steepened past their true tilt, light caught facet by facet.
        // The same two cheats live here as `_SlopeGain` (the analytic
        // slope amplified before the normal is built) and
        // `_FacetStrength` (the displaced triangle's own flat normal
        // blended in from screen-space derivatives). `_CrestShading`
        // lets the swell reach the body colour - lifted water toward
        // the shallow tone, troughs toward the deep - and
        // `_CrestFoamStrength` breaks the tallest crests white.
        _SlopeGain("Wave Slope Gain", Range(1, 6)) = 2.3
        _FacetStrength("Wave Facet Strength", Range(0, 1)) = 0.4
        _CrestShading("Crest Shading", Range(0, 1)) = 0.4
        _CrestFoamStrength("Crest Foam Strength", Range(0, 1)) = 0.35
        _CrestFoamThreshold("Crest Foam Threshold", Range(0, 1)) = 0.6

        // x: south Z where the ramp starts, y: 1/ramp length,
        // z: amplitude floor, w: enabled. Off by default - only the
        // sea dies on a shore.
        _ShoreFadeParams("Shore Fade (SouthZ, 1/Len, Floor, On)", Vector) = (0, 0, 1, 0)

        _DepthFadeDistance("Depth Fade Distance", Float) = 0.9
        _FoamDistance("Foam Distance", Float) = 0.42
        _RefractionStrength("Refraction Strength", Range(0, 0.5)) = 0.055
        _SpecularPower("Specular Power", Float) = 48
        _SpecularStrength("Specular Strength", Range(0, 4)) = 0.85
        _FresnelStrength("Fresnel Strength", Range(0, 1)) = 0.30
        _BandSteps("Band Steps", Float) = 4

        // Still-water additions, off by default. Every body turns the
        // lamp glint on now - the river at the sea's 2.0 since its
        // waterside lanterns came down to hang a metre over the
        // channel - but the edge-foam colour stays each body's own
        // choice.
        _AdditionalSpecular("Additional Light Specular", Range(0, 2)) = 0
        _FoamColor("Foam Colour", Color) = (1, 1, 1, 1)

        // The Morrowind mirror: an environment cubemap reflected
        // about the rippled normal. Black and zero by default, which
        // is what the river and the sea keep - only the fountain
        // binds a cube and turns the strength up. The distortion is
        // how much of the ripple bends the mirror: at 1 the full
        // shading normal scatters the cube into metallic noise, at
        // this default the mirrored world stays legible and swims.
        _ReflectionCube("Reflection Cubemap", Cube) = "black" {}
        _ReflectionStrength("Reflection Strength", Range(0, 2)) = 0
        _ReflectionDistortion("Reflection Distortion", Range(0, 1)) = 0.30

        // The puddle film. Wetness dissolves the whole surface into
        // the very background the shader composites from - at 0 the
        // pixel IS the road below, which is how a puddle dries with
        // Blend Off. Edge noise gnaws the patch rim first (the rim
        // mask arrives in TEXCOORD0.x from the puddle mesh), so a
        // drying puddle shrinks toward its middle instead of fading
        // out as a rectangle. Defaults 1 / off: river, sea and
        // fountain never dry and carry no UV stream to read.
        _SurfaceWetness("Surface Wetness", Range(0, 1)) = 1
        _EdgeNoiseParams("Edge Noise (Scale, Bite, On)", Vector) = (0, 0, 0, 0)

        // The lighthouse's virtual lamp. The island deliberately
        // carries no real Light - a point light could never reach the
        // shore across forty metres of sea - so the sea is told where
        // the lantern stands and which way its beams point, and lays
        // the glitter itself. Zero by default, which the river and
        // the fountain keep: only the island builder turns it on, and
        // only on the sea.
        _LanternPosition("Lantern Position XYZ / InvRangeSq W", Vector) = (0, 0, 0, 0)
        _LanternColor("Lantern Color", Color) = (0.95, 0.90, 0.78, 1)
        _LanternGlint("Lantern Glint Strength", Range(0, 4)) = 0
        _LanternBeamDir("Lantern Beam (Sin, Cos, CosHalfWidth)", Vector) = (0, 1, 1, 0)
    }

    SubShader
    {
        Tags
        {
            // Ahead of the default transparent queue: the water is
            // opaque output and a depth writer, so it must be laid down
            // before the light halos and atmosphere particles that
            // expect to test against it.
            "Queue" = "Transparent-100"
            "RenderType" = "Transparent"
            "RenderPipeline" = "UniversalPipeline"
        }

        Pass
        {
            Name "RiverWater"
            Tags { "LightMode" = "UniversalForward" }

            ZWrite On
            ZTest LEqual
            Cull Back
            Blend Off

            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex RiverVertex
            #pragma fragment RiverFragment
            #pragma multi_compile_fog
            #pragma multi_compile_instancing
            // The lamp glints on still water are URP additional lights.
            // The renderer runs Forward+, where URP turns the classic
            // _ADDITIONAL_LIGHTS keyword off and hands lights out through
            // the cluster list instead - without the cluster keyword the
            // glint loop below compiles into a variant that never runs.
            // Both are kept: the cluster pair for the shipping renderer,
            // the classic pair as the plain-Forward fallback.
            #pragma multi_compile_fragment _ _ADDITIONAL_LIGHTS
            #pragma multi_compile _ _CLUSTER_LIGHT_LOOP

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareOpaqueTexture.hlsl"

            TEXTURE2D(_RippleMap);
            SAMPLER(sampler_RippleMap);
            TEXTURE2D(_FoamMap);
            SAMPLER(sampler_FoamMap);
            TEXTURECUBE(_ReflectionCube);
            SAMPLER(sampler_ReflectionCube);

            CBUFFER_START(UnityPerMaterial)
                half4 _BaseColor;
                half4 _DeepColor;
                half4 _HighlightColor;
                half _NightFactor;
                half _RainIntensity;
                half _FlowSpeed;
                float4 _FlowDirection;
                float _RippleTiling;
                float _FoamTiling;
                half _NormalStrength;
                float _WaveHeight;
                float _WaveLength;
                half _SlopeGain;
                half _FacetStrength;
                half _CrestShading;
                half _CrestFoamStrength;
                half _CrestFoamThreshold;
                float4 _ShoreFadeParams;
                float _DepthFadeDistance;
                float _FoamDistance;
                half _RefractionStrength;
                float _SpecularPower;
                half _SpecularStrength;
                half _FresnelStrength;
                float _BandSteps;
                half _AdditionalSpecular;
                half4 _FoamColor;
                half _ReflectionStrength;
                half _ReflectionDistortion;
                half _SurfaceWetness;
                float4 _EdgeNoiseParams;
                float4 _LanternPosition;
                half4 _LanternColor;
                half _LanternGlint;
                float4 _LanternBeamDir;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                // Rim mask for the puddle film, 0 at the patch edge and
                // 1 at its centre. The big water meshes carry no UV
                // stream at all — the missing stream reads as zero, so
                // the vertex stage only consumes it when
                // _EdgeNoiseParams.z turns the film's edge on.
                float2 uv : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                float3 waveNormalWS : TEXCOORD1;
                float4 screenPos : TEXCOORD2;
                float viewZ : TEXCOORD3;
                float crest : TEXCOORD4;
                half edgeMask : TEXCOORD5;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            // Returns the unit downstream axis and, through `flowing`,
            // whether there is a current at all.
            //
            // The fallback is still (0,1), because every caller wants
            // *some* axis to lay its pattern along even when the water
            // is still. What changed is that the callers now know the
            // difference: they used to take this axis on faith and a
            // zero `_FlowDirection` produced a river flowing north with
            // the label taken off.
            float2 FlowAxis(out float flowing)
            {
                float2 axis = _FlowDirection.xy;
                float magnitude = length(axis);
                flowing = step(1e-4, magnitude);
                return magnitude > 1e-4 ? axis / magnitude : float2(0.0, 1.0);
            }

            // The swell dies on the sand. Amplitude ramps from a floor
            // at the ramp's south edge up to full further out, so the
            // trough never undercuts the shore shelf while the deep
            // water heaves at full height.
            //
            // The envelope multiplies the height, so the slope needs
            // the product rule: d(e*w)/dz = e*dw/dz + de/dz*w. Dropping
            // the second term would detach the normal from the surface
            // it shades - the normal must stay the exact derivative of
            // what is actually drawn, for the same reason the wave
            // itself is analytic.
            float ShoreEnvelope(float z, out float slopeZ)
            {
                float t = saturate(
                    (z - _ShoreFadeParams.x) * _ShoreFadeParams.y);
                float eased = t * t * (3.0 - 2.0 * t);
                float envelope = _ShoreFadeParams.z +
                    (1.0 - _ShoreFadeParams.z) * eased;
                slopeZ = (1.0 - _ShoreFadeParams.z) *
                    6.0 * t * (1.0 - t) * _ShoreFadeParams.y *
                    _ShoreFadeParams.w;
                return lerp(1.0, envelope, _ShoreFadeParams.w);
            }

            // Two trains running downstream at different rates plus one
            // lying across them. Summing three keeps the crest line from
            // repeating on any axis, which one train cannot do and four
            // do not visibly improve at this resolution.
            //
            // Returns the height and, through `slope`, the analytic
            // derivative with respect to world X and Z. Taking the
            // derivative rather than recomputing normals from the mesh is
            // the whole reason the segments meet cleanly: it depends only
            // on where the vertex is, never on which sheet it belongs to.
            //
            // `crest01` is how near the summed trains ride to their own
            // ceiling, before the shore envelope - relative, so a wave
            // breaks white on the shelf exactly as it does out deep.
            //
            // CityWaterWaveModel.cs is this function's CPU mirror - the
            // fisherman's float rides it - and a test holds the two to
            // the same constants. Change a number here, change it there.
            float WaveField(
                float2 positionXZ,
                float time,
                out float2 slope,
                out float crest01)
            {
                float flowing;
                float2 downstream = FlowAxis(flowing);
                float2 across = float2(-downstream.y, downstream.x);

                float k0 = 6.2831853 / max(0.05, _WaveLength);
                float k1 = 6.2831853 / max(0.05, _WaveLength * 0.53);
                float k2 = 6.2831853 / max(0.05, _WaveLength * 1.71);

                // A current lines its second train up with its first,
                // because that is what a current does. Still water has
                // no axis to line anything up with, and two trains on
                // one axis there produce corduroy: a static, perfectly
                // parallel ribbing that reads as a ploughed field.
                // Turning the middle train across the other two makes
                // the standing pattern a lattice instead.
                float2 secondAxis = lerp(
                    normalize(downstream + across),
                    downstream,
                    flowing);

                // Multiplying the time term by `flowing` freezes the
                // crest pattern in place without freezing the surface:
                // the amplitudes below keep moving, so still water
                // still lives.
                float p0 = dot(positionXZ, downstream) * k0 - time * 1.00 * flowing;
                float p1 = dot(positionXZ, secondAxis) * k1 - time * 1.63 * flowing;
                float p2 = dot(positionXZ, across) * k2 + time * 0.41 * flowing;

                // Still water does not carry its crests away; it
                // breathes in place. Three rates, none a multiple of
                // another, so the trains never pulse together and the
                // pond never looks like it is being pumped.
                float b0 = lerp(0.55 + 0.45 * sin(time * 0.90), 1.0, flowing);
                float b1 = lerp(0.55 + 0.45 * sin(time * 0.71 + 1.7), 1.0, flowing);
                float b2 = lerp(0.55 + 0.45 * sin(time * 1.37 + 3.1), 1.0, flowing);

                float a0 = _WaveHeight * b0;
                float a1 = _WaveHeight * 0.42 * b1;
                float a2 = _WaveHeight * 0.31 * b2;

                float2 rawSlope = downstream * (a0 * k0 * cos(p0)) +
                                  secondAxis * (a1 * k1 * cos(p1)) +
                                  across * (a2 * k2 * cos(p2));
                float raw = a0 * sin(p0) + a1 * sin(p1) + a2 * sin(p2);

                float envelopeSlopeZ;
                float envelope = ShoreEnvelope(
                    positionXZ.y, envelopeSlopeZ);
                crest01 = raw / max(1e-4, _WaveHeight * 1.73);
                slope = envelope * rawSlope +
                        float2(0.0, envelopeSlopeZ * raw);
                return envelope * raw;
            }

            Varyings RiverVertex(Attributes input)
            {
                Varyings output = (Varyings)0;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);

                float3 positionWS = TransformObjectToWorld(
                    input.positionOS.xyz);
                float time = _Time.y * _FlowSpeed;
                float2 slope;
                float crest01;
                positionWS.y += WaveField(
                    positionWS.xz, time, slope, crest01);

                output.positionWS = positionWS;

                // The bed's ExaggerateDentShading, in shader form: the
                // geometry keeps its honest height while the lateral
                // normal is steepened, so the light answers the swell
                // several times louder than its true tilt. A wave of
                // tens of centimetres over tens of metres is only a few
                // degrees of surface - invisible at 640x360 without it.
                output.waveNormalWS = normalize(float3(
                    -slope.x * _SlopeGain,
                    1.0,
                    -slope.y * _SlopeGain));
                output.crest = crest01;
                output.positionCS = TransformWorldToHClip(positionWS);
                output.screenPos = ComputeScreenPos(output.positionCS);

                // The fog factor is deliberately NOT computed here.
                //
                // A water sheet is a big flat grid seen almost
                // edge-on, and the fog curve is not linear, so
                // interpolating a per-vertex factor across metre-wide
                // rows lays a set of faint parallel bands down the
                // whole surface - perspective turns them into stripes
                // that look like a current on water that has none.
                // Eye depth interpolates exactly, so the fragment
                // stage derives the factor from it instead.
                output.viewZ = -TransformWorldToView(positionWS).z;
                output.edgeMask = _EdgeNoiseParams.z > 0.5
                    ? (half)saturate(input.uv.x)
                    : 1.0h;
                return output;
            }

            // Bilinear value noise for the puddle rim: cheap, stable
            // in world space, and blocky enough for the composite.
            float EdgeNoiseHash(float2 cell)
            {
                return frac(
                    sin(dot(cell, float2(127.1, 311.7))) * 43758.5453);
            }

            float EdgeNoise(float2 positionXZ)
            {
                float2 grid = positionXZ * max(0.5, _EdgeNoiseParams.x);
                float2 cell = floor(grid);
                float2 blend = frac(grid);
                blend = blend * blend * (3.0 - 2.0 * blend);
                float a = EdgeNoiseHash(cell);
                float b = EdgeNoiseHash(cell + float2(1.0, 0.0));
                float c = EdgeNoiseHash(cell + float2(0.0, 1.0));
                float d = EdgeNoiseHash(cell + float2(1.0, 1.0));
                return lerp(
                    lerp(a, b, blend.x),
                    lerp(c, d, blend.x),
                    blend.y);
            }

            // One ripple sheet sampled twice: different pitch, different
            // rate, and rotated a little rather than a lot. The sheet is
            // drawn smeared downstream on purpose, so turning the second
            // sample square to the first would cancel exactly the
            // anisotropy that makes it read as a current.
            float3 SampleRipple(float2 positionXZ, float time)
            {
                float flowing;
                float2 downstream = FlowAxis(flowing);

                // A current drags the sheet downstream forever. Still
                // water has nowhere to drag it to, so it walks a small
                // closed orbit instead: the sheet keeps moving, but it
                // never commits to a direction, which is exactly the
                // difference between a river and a pond.
                float2 stillDrift =
                    float2(sin(time * 0.35), cos(time * 0.29)) * 0.35;
                float2 drift = lerp(stillDrift, downstream * time, flowing);

                float2 uv0 = (positionXZ - drift) / max(0.05, _RippleTiling);
                float2 uv1 = mul(
                    float2x2(0.94, -0.34, 0.34, 0.94),
                    positionXZ - drift * 0.61) /
                    max(0.05, _RippleTiling * 0.43);

                float3 n0 = SAMPLE_TEXTURE2D(
                    _RippleMap, sampler_RippleMap, uv0).xyz * 2.0 - 1.0;
                float3 n1 = SAMPLE_TEXTURE2D(
                    _RippleMap, sampler_RippleMap, uv1).xyz * 2.0 - 1.0;

                // The sheet's tangent plane is world XZ: its U runs along
                // world X and its V along world Z, so the stored pair is
                // the horizontal tilt and world up supplies the rest.
                float2 tilt = (n0.xy + n1.xy * 0.7) * _NormalStrength;
                return normalize(float3(tilt.x, 1.0, tilt.y));
            }

            half4 RiverFragment(Varyings input) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);

                float time = _Time.y * _FlowSpeed;
                float2 screenUV = input.screenPos.xy / input.screenPos.w;

                float3 rippleNormal = SampleRipple(input.positionWS.xz, time);
                float3 waveNormalWS = normalize(input.waveNormalWS);

                // The bed's other lesson: its dent reads because the
                // top is independent per-cell quads and every facet
                // catches its own light. Splitting the water's vertices
                // would break the welded grid its tests pin, so the
                // facet normal comes from the screen-space derivatives
                // of the displaced surface instead - flat per triangle,
                // which is the same thing seen from the light's side.
                if (_FacetStrength > 0.0h)
                {
                    float3 facet = cross(
                        ddy(input.positionWS),
                        ddx(input.positionWS));
                    facet = facet.y < 0.0 ? -facet : facet;
                    float facetLength = length(facet);
                    if (facetLength > 1e-5)
                    {
                        facet /= facetLength;
                        facet.xz *= _SlopeGain;
                        waveNormalWS = normalize(lerp(
                            waveNormalWS,
                            normalize(facet),
                            _FacetStrength));
                    }
                }

                float3 normalWS = normalize(
                    waveNormalWS + float3(
                        rippleNormal.x, 0.0, rippleNormal.z));

                // `ComputeScreenPos` leaves clip w in .w, which for this
                // projection is the eye depth of the fragment itself.
                float surfaceEyeDepth = input.screenPos.w;

                // Refraction. The offset is damped with distance so a
                // far bank does not shear, and it is rejected outright
                // when the offset lands on something nearer than the
                // water: without that test an object standing in front
                // of the river bleeds its own colour into it.
                float2 offset = normalWS.xz * _RefractionStrength /
                                (1.0 + surfaceEyeDepth * 0.12);
                float refractedEyeDepth = LinearEyeDepth(
                    SampleSceneDepth(screenUV + offset), _ZBufferParams);
                bool refractionValid = refractedEyeDepth > surfaceEyeDepth;

                float2 backgroundUV = refractionValid
                    ? screenUV + offset
                    : screenUV;
                float backgroundDepth = refractionValid
                    ? refractedEyeDepth
                    : LinearEyeDepth(
                        SampleSceneDepth(screenUV), _ZBufferParams);
                float3 background = SampleSceneColor(backgroundUV);
                float waterDepth = max(
                    0.0,
                    backgroundDepth - surfaceEyeDepth);

                // Absorption: how much water is between the eye and what
                // it is looking at. Near the granite that is almost
                // nothing, so the floor shows; two metres out it is the
                // river's own colour and nothing else.
                float absorption = saturate(
                    waterDepth / max(0.01, _DepthFadeDistance));
                half3 body = lerp(
                    _BaseColor.rgb,
                    _DeepColor.rgb,
                    absorption);

                // The swell reaches the colour itself: lifted water
                // leans toward the shallow tone, a trough toward the
                // deep. Fresnel and a banded glint alone cannot carry a
                // wave through the 640x360 composite - value change
                // can, which is why the bed exaggerates its normals
                // instead of trusting ten honest degrees.
                half lift = (half)saturate(input.crest);
                half sink = (half)saturate(-input.crest);
                body = lerp(
                    body,
                    _BaseColor.rgb * 1.18h,
                    lift * _CrestShading * 0.6h);
                body = lerp(
                    body,
                    _DeepColor.rgb * 0.72h,
                    sink * _CrestShading);
                half3 color = lerp(background, body, absorption);

                float3 viewDirWS = normalize(
                    GetWorldSpaceViewDir(input.positionWS));

                // The Morrowind mirror. The environment cube is
                // reflected about the rippled normal, so the mirrored
                // world swims with the water instead of lying on it
                // like a decal. The weight rides fresnel but keeps a
                // floor: a fountain is looked INTO from above, and a
                // mirror that only works at grazing angles is no
                // mirror at all. Night lives in the cube itself - the
                // controller re-renders it as the lamps come on -
                // and the glints, foam and fog stay on top.
                if (_ReflectionStrength > 0.0h)
                {
                    // The mirror bends by only a fraction of the
                    // shading normal: the full ripple is right for
                    // light but scatters a mirrored tree into noise.
                    float3 mirrorNormal = normalize(lerp(
                        float3(0.0, 1.0, 0.0),
                        normalWS,
                        _ReflectionDistortion));
                    float3 reflectDir = reflect(
                        -viewDirWS,
                        mirrorNormal);
                    half3 environment = SAMPLE_TEXTURECUBE(
                        _ReflectionCube,
                        sampler_ReflectionCube,
                        reflectDir).rgb;
                    half grazing = (half)pow(
                        1.0 - saturate(dot(normalWS, viewDirWS)),
                        2.0);
                    half weight = saturate(
                        _ReflectionStrength *
                        (0.30h + 0.70h * grazing));
                    color = lerp(color, environment, weight);
                }

                // Sun and lamp glint. Banded, because everything else in
                // this city is: a smooth specular falloff on the one
                // surface with a real highlight would read as a shinier
                // engine rather than as water.
                Light mainLight = GetMainLight();
                float3 halfWS = normalize(mainLight.direction + viewDirWS);
                float specular = pow(
                    saturate(dot(normalWS, halfWS)),
                    max(1.0, _SpecularPower));
                float steps = max(1.0, _BandSteps);
                specular = floor(specular * steps) / steps;

                half3 highlight = _HighlightColor.rgb * mainLight.color;
                color += highlight * specular * _SpecularStrength *
                         lerp(1.0h, 0.45h, _NightFactor);

                // The city's own lamps. Until now this surface read the
                // sun and nothing else, which is why no electric light
                // ever touched the water. A point light standing over a
                // still pond lays a long glitter path toward whoever is
                // looking - it falls out of the same banded highlight,
                // driven by the real fixture, so it tracks the camera
                // and the night cycle for free. Four is the whole boat
                // station and more than the shader can spare.
                #ifdef _ADDITIONAL_LIGHTS
                if (_AdditionalSpecular > 0.0h)
                {
                    // The cluster form of LIGHT_LOOP_BEGIN expands
                    // ClusterInit over a local literally named
                    // `inputData`, and reads only these two fields.
                    InputData inputData = (InputData)0;
                    inputData.positionWS = input.positionWS;
                    inputData.normalizedScreenSpaceUV =
                        GetNormalizedScreenSpaceUV(input.positionCS);

                    // The clamp only reaches the plain-Forward
                    // fallback: the cluster loop ignores its argument
                    // and walks the tile's own list, which the lamps'
                    // short ranges keep at boat-station size anyway.
                    uint lightCount = min(GetAdditionalLightsCount(), 4u);
                    LIGHT_LOOP_BEGIN(lightCount)
                        Light extra = GetAdditionalLight(
                            lightIndex, input.positionWS);
                        float3 halfExtra = normalize(
                            extra.direction + viewDirWS);
                        float glint = pow(
                            saturate(dot(normalWS, halfExtra)),
                            max(1.0, _SpecularPower));
                        glint = floor(glint * steps) / steps;
                        color += extra.color * extra.distanceAttenuation *
                                 glint * _SpecularStrength *
                                 _AdditionalSpecular;
                    LIGHT_LOOP_END
                }
                #endif

                // The lighthouse. Its lantern is not a Light - the
                // island runs on rules and additive cones - so its
                // glitter is a virtual lamp: the same banded highlight
                // as the fixtures above, windowed by distance and swept
                // by the two opposed beams, so the streak crosses the
                // sea in step with the cones overhead. A faint constant
                // shimmer stays between passes: a lit lens on the water
                // even when the beam faces elsewhere.
                if (_LanternGlint > 0.0h)
                {
                    float3 toLantern =
                        _LanternPosition.xyz - input.positionWS;
                    float3 lanternDir = normalize(toLantern);
                    float3 halfLantern =
                        normalize(lanternDir + viewDirWS);
                    float lanternGlint = pow(
                        saturate(dot(normalWS, halfLantern)),
                        max(1.0, _SpecularPower));
                    lanternGlint =
                        floor(lanternGlint * steps) / steps;

                    // A soft window, not 1/d^2: at forty metres under
                    // a 48 m far clip physical falloff leaves nothing,
                    // and the fog already tells the distance story.
                    // w is 1/range^2.
                    float atten = saturate(
                        1.0 -
                        dot(toLantern, toLantern) * _LanternPosition.w);
                    atten *= atten;

                    // The sweep: bearing from lantern to pixel against
                    // the beam axis (sin, cos). abs() folds the opposed
                    // pair into one line - FlashFactorAt's
                    // min(delta, 180 - delta) in cosine space - and z
                    // carries cos(FlashHalfWidthDegrees) from the C#
                    // rules. The length guard is real: the sea's apron
                    // runs under the lantern itself.
                    float2 offsetXZ =
                        input.positionWS.xz - _LanternPosition.xz;
                    float2 bearing =
                        offsetXZ / max(length(offsetXZ), 1e-3);
                    float alongBeam =
                        abs(dot(bearing, _LanternBeamDir.xy));
                    float sweepT = saturate(
                        (alongBeam - _LanternBeamDir.z) /
                        max(1e-4, 1.0 - _LanternBeamDir.z));
                    float sweep = sweepT * sweepT * (3.0 - 2.0 * sweepT);

                    // The day floor mirrors the lantern controller's
                    // DayIntensity = 0.6: a landfall light burns
                    // through the day haze too.
                    float lit = 0.15 + 0.85 * sweep;
                    color += _LanternColor.rgb * lanternGlint * atten *
                             lit * _SpecularStrength * _LanternGlint *
                             lerp(0.6h, 1.0h, _NightFactor);
                }

                // Fresnel: at a grazing angle a river stops being a
                // window and starts being a mirror of the sky, which at
                // this distance is one flat tone.
                float fresnel = pow(
                    1.0 - saturate(dot(normalWS, viewDirWS)),
                    4.0);
                color = lerp(
                    color,
                    _HighlightColor.rgb * lerp(0.85h, 0.30h, _NightFactor),
                    fresnel * _FresnelStrength);


                // Foam. The depth threshold puts it exactly where the
                // water runs out - the foot of the quay walls, the
                // bridge piers, the stair landings - so nothing has to
                // place it. The mask is scrolled and the rain thickens
                // it, because a river in the rain is more broken up, not
                // more glassy.
                float foamEdge = 1.0 - smoothstep(
                    0.0,
                    max(0.01, _FoamDistance),
                    waterDepth);
                float foamFlowing;
                float2 downstream = FlowAxis(foamFlowing);
                float2 foamUV = (input.positionWS.xz - downstream * time *
                                 0.72 * foamFlowing) /
                                max(0.05, _FoamTiling);
                half foamMask = SAMPLE_TEXTURE2D(
                    _FoamMap, sampler_FoamMap, foamUV).r;
                half foam = saturate(
                    foamEdge *
                    (foamMask * 1.5h + 0.25h) *
                    (0.85h + 0.35h * _RainIntensity));
                foam = floor(foam * steps) / steps;
                half foamTone = lerp(1.0h, 0.55h, _NightFactor);
                color = lerp(
                    color,
                    _FoamColor.rgb * foamTone,
                    foam * 0.8h);

                // Whitecaps: where the summed trains ride near their
                // own ceiling, the crest breaks white. Same mask and
                // the same banding as the edge foam, so a cap and the
                // surf it rolls into are one white; the crest measure
                // is relative, so the caps keep breaking over the
                // shore shelf where the envelope has taken the height.
                half whitecap = saturate(
                    smoothstep(
                        _CrestFoamThreshold,
                        1.0h,
                        (half)input.crest) *
                    (foamMask * 1.2h + 0.3h) *
                    _CrestFoamStrength *
                    (0.9h + 0.35h * _RainIntensity));
                whitecap = floor(whitecap * steps) / steps;
                color = lerp(
                    color,
                    _FoamColor.rgb * foamTone,
                    whitecap * 0.85h);

                // Rain chop: the high-frequency break-up the old shader
                // carried, kept because it is what the weather controller
                // has been driving all along.
                half chop = sin(
                    input.positionWS.x * 2.9 -
                    input.positionWS.z * 2.3 +
                    time * 4.0) * 0.5h + 0.5h;
                color += _HighlightColor.rgb *
                         (_RainIntensity * (floor(chop * steps) / steps) *
                          0.10h);

                half fogFactor = ComputeFogFactorZ0ToFar(
                    max(input.viewZ - _ProjectionParams.y, 0));
                color = MixFog(color, fogFactor);

                // The puddle film. Both sides of the lerp are final
                // pixels — the water is fogged above and the sampled
                // background is the already-composited frame — so
                // dissolving here is exact: at zero wetness the pixel
                // IS the road. The rim erodes first, carved by world
                // noise, so a drying puddle pulls toward its middle.
                //
                // The shore stands on the SQUARE ROOT of the wetness.
                // The city never dries below its drizzle (0.18), and
                // measured against the wetness itself that floor left
                // a sliver a quarter of the patch wide - at 640x360 no
                // puddle at all. Against sqrt(0.18) = 0.42 the drizzle
                // keeps about half the patch (the noise varies it from
                // a third to the whole), light rain nearly fills it, a
                // downpour fills it, and zero still dissolves it.
                half film = _SurfaceWetness;
                if (_EdgeNoiseParams.z > 0.5)
                {
                    float erosion =
                        (1.0 - input.edgeMask) *
                        (0.4 + 0.6 * EdgeNoise(input.positionWS.xz)) *
                        max(0.05, _EdgeNoiseParams.y);
                    float shore = sqrt(saturate(_SurfaceWetness));
                    film = (half)smoothstep(
                        0.0,
                        0.12,
                        shore - erosion);
                }

                color = lerp((half3)background, color, saturate(film));
                return half4(color, 1.0h);
            }
            ENDHLSL
        }
    }

    Fallback Off
}

// The city's water surface: river now, sea and lake next.
//
// Everything is a function of world position rather than of UV, which is
// what lets one segment's sheet meet the next without a seam and what
// lets the same shader serve footprints that are nothing like a channel.
// `_FlowDirection` is the only thing that says "river": set it to zero
// and the same material is still water, just water that is not going
// anywhere.
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

        _WaveHeight("Wave Height", Range(0, 0.25)) = 0.05
        _WaveLength("Wave Length", Float) = 3.4

        _DepthFadeDistance("Depth Fade Distance", Float) = 0.9
        _FoamDistance("Foam Distance", Float) = 0.42
        _RefractionStrength("Refraction Strength", Range(0, 0.5)) = 0.055
        _SpecularPower("Specular Power", Float) = 48
        _SpecularStrength("Specular Strength", Range(0, 4)) = 0.85
        _FresnelStrength("Fresnel Strength", Range(0, 1)) = 0.30
        _BandSteps("Band Steps", Float) = 4
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

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareOpaqueTexture.hlsl"

            TEXTURE2D(_RippleMap);
            SAMPLER(sampler_RippleMap);
            TEXTURE2D(_FoamMap);
            SAMPLER(sampler_FoamMap);

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
                float _DepthFadeDistance;
                float _FoamDistance;
                half _RefractionStrength;
                float _SpecularPower;
                half _SpecularStrength;
                half _FresnelStrength;
                float _BandSteps;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                float3 waveNormalWS : TEXCOORD1;
                float4 screenPos : TEXCOORD2;
                half fogFactor : TEXCOORD3;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            float2 FlowAxis()
            {
                float2 axis = _FlowDirection.xy;
                float magnitude = length(axis);
                return magnitude > 1e-4 ? axis / magnitude : float2(0.0, 1.0);
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
            float WaveHeight(float2 positionXZ, float time, out float2 slope)
            {
                float2 downstream = FlowAxis();
                float2 across = float2(-downstream.y, downstream.x);

                float k0 = 6.2831853 / max(0.05, _WaveLength);
                float k1 = 6.2831853 / max(0.05, _WaveLength * 0.53);
                float k2 = 6.2831853 / max(0.05, _WaveLength * 1.71);

                float p0 = dot(positionXZ, downstream) * k0 - time * 1.00;
                float p1 = dot(positionXZ, downstream) * k1 - time * 1.63;
                float p2 = dot(positionXZ, across) * k2 + time * 0.41;

                float a0 = _WaveHeight;
                float a1 = _WaveHeight * 0.42;
                float a2 = _WaveHeight * 0.31;

                slope = downstream * (a0 * k0 * cos(p0) + a1 * k1 * cos(p1)) +
                        across * (a2 * k2 * cos(p2));
                return a0 * sin(p0) + a1 * sin(p1) + a2 * sin(p2);
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
                positionWS.y += WaveHeight(positionWS.xz, time, slope);

                output.positionWS = positionWS;
                output.waveNormalWS = normalize(
                    float3(-slope.x, 1.0, -slope.y));
                output.positionCS = TransformWorldToHClip(positionWS);
                output.screenPos = ComputeScreenPos(output.positionCS);

                float viewZ = -TransformWorldToView(positionWS).z;
                output.fogFactor = ComputeFogFactorZ0ToFar(
                    max(viewZ - _ProjectionParams.y, 0));
                return output;
            }

            // One ripple sheet sampled twice: different pitch, different
            // rate, and rotated a little rather than a lot. The sheet is
            // drawn smeared downstream on purpose, so turning the second
            // sample square to the first would cancel exactly the
            // anisotropy that makes it read as a current.
            float3 SampleRipple(float2 positionXZ, float time)
            {
                float2 downstream = FlowAxis();
                float2 drift = downstream * time;

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
                float3 normalWS = normalize(
                    input.waveNormalWS + float3(
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
                half3 color = lerp(background, body, absorption);

                // Sun and lamp glint. Banded, because everything else in
                // this city is: a smooth specular falloff on the one
                // surface with a real highlight would read as a shinier
                // engine rather than as water.
                Light mainLight = GetMainLight();
                float3 viewDirWS = normalize(
                    GetWorldSpaceViewDir(input.positionWS));
                float3 halfWS = normalize(mainLight.direction + viewDirWS);
                float specular = pow(
                    saturate(dot(normalWS, halfWS)),
                    max(1.0, _SpecularPower));
                float steps = max(1.0, _BandSteps);
                specular = floor(specular * steps) / steps;

                half3 highlight = _HighlightColor.rgb * mainLight.color;
                color += highlight * specular * _SpecularStrength *
                         lerp(1.0h, 0.45h, _NightFactor);

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
                float2 downstream = FlowAxis();
                float2 foamUV = (input.positionWS.xz - downstream * time *
                                 0.72) / max(0.05, _FoamTiling);
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
                    half3(foamTone, foamTone, foamTone),
                    foam * 0.8h);

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

                color = MixFog(color, input.fogFactor);
                return half4(color, 1.0h);
            }
            ENDHLSL
        }
    }

    Fallback Off
}

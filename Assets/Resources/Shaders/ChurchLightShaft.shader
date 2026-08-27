// A shaft of sun standing in the air of the church.
//
// Descended from CityLighthouseBeam - the project's only additive
// shader - with the three things an interior volume needs and a beacon
// in fog does not:
//
//   1. A DEPTH FADE. The lighthouse beam ends in open air; this one
//      lands on a stone floor and crosses ten pew backs on the way. Cut
//      against the depth buffer it would draw a hard line at every
//      intersection, which reads as a decal rather than as light. The
//      eight lines that do it are lifted from CityAtmosphereParticle.
//   2. A CROSS-SECTION falloff. The lighthouse leaves uv.y unused; a
//      shaft with hard flanks reads as a lit box. Here the beam is
//      brightest where you look through the most of it.
//   3. A NEAR fade, because the player walks through these.
//
// The motes are in-shader rather than particles on purpose: the prism
// is re-solved into a new oblique shape every frame as the sun moves,
// and dust living in its object space follows for free where ten
// re-oriented particle systems would not.
Shader "Bar Promenade/Church Light Shaft"
{
    Properties
    {
        [HDR] _BeamColor("Beam Color", Color) = (1.6, 1.9, 2.4, 1)
        _Intensity("Intensity", Range(0, 8)) = 1
        _AlongPower("Along Falloff", Range(0.1, 4)) = 0.65
        _EdgePower("Cross Section Falloff", Range(0.2, 6)) = 1.35
        _CoreGlow("Core Glow", Range(0, 1)) = 0.45
        _SoftParticleDistance("Depth Fade Distance", Range(0.05, 6)) = 1.4
        _NearFadeDistance("Near Fade Distance", Float) = 0.9
        _NearFadeRange("Near Fade Range", Float) = 1.4
        _MoteScale("Mote Scale", Range(0.5, 20)) = 6.5
        _MoteSpeed("Mote Speed", Range(0, 2)) = 0.16
        _MoteSharpness("Mote Sharpness", Range(1, 24)) = 9
        _MoteStrength("Mote Strength", Range(0, 3)) = 0.9
    }

    SubShader
    {
        Tags
        {
            "Queue" = "Transparent"
            "RenderType" = "Transparent"
            "RenderPipeline" = "UniversalPipeline"
            "IgnoreProjector" = "True"
        }

        Pass
        {
            Name "ChurchLightShaft"
            Tags { "LightMode" = "UniversalForward" }

            Blend One One
            ZWrite Off
            ZTest LEqual
            Cull Off

            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex ShaftVertex
            #pragma fragment ShaftFragment
            #pragma multi_compile_instancing

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half4 _BeamColor;
                half _Intensity;
                half _AlongPower;
                half _EdgePower;
                half _CoreGlow;
                half _SoftParticleDistance;
                float _NearFadeDistance;
                float _NearFadeRange;
                half _MoteScale;
                half _MoteSpeed;
                half _MoteSharpness;
                half _MoteStrength;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
                float2 uv : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD1;
                float3 positionOS : TEXCOORD2;
                float3 normalWS : TEXCOORD3;
                float2 uv : TEXCOORD0;
                float eyeDepth : TEXCOORD4;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            // Cheap value hash. Deterministic, and the same every run -
            // nothing in this project is allowed to shimmer differently
            // frame to frame for no reason.
            float MoteHash(float3 cell)
            {
                return frac(
                    sin(dot(cell, float3(12.9898, 78.233, 37.719))) *
                    43758.5453);
            }

            float MoteField(float3 positionOS)
            {
                float3 p = positionOS * _MoteScale;
                p.z += _Time.y * _MoteSpeed * 3.0;
                p.y += _Time.y * _MoteSpeed;
                float3 cell = floor(p);
                float3 f = frac(p);
                f = f * f * (3.0 - 2.0 * f);
                float n000 = MoteHash(cell + float3(0, 0, 0));
                float n100 = MoteHash(cell + float3(1, 0, 0));
                float n010 = MoteHash(cell + float3(0, 1, 0));
                float n110 = MoteHash(cell + float3(1, 1, 0));
                float n001 = MoteHash(cell + float3(0, 0, 1));
                float n101 = MoteHash(cell + float3(1, 0, 1));
                float n011 = MoteHash(cell + float3(0, 1, 1));
                float n111 = MoteHash(cell + float3(1, 1, 1));
                float x00 = lerp(n000, n100, f.x);
                float x10 = lerp(n010, n110, f.x);
                float x01 = lerp(n001, n101, f.x);
                float x11 = lerp(n011, n111, f.x);
                float y0 = lerp(x00, x10, f.y);
                float y1 = lerp(x01, x11, f.y);
                return lerp(y0, y1, f.z);
            }

            Varyings ShaftVertex(Attributes input)
            {
                Varyings output = (Varyings)0;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);

                VertexPositionInputs positionInputs =
                    GetVertexPositionInputs(input.positionOS.xyz);
                output.positionCS = positionInputs.positionCS;
                output.positionWS = positionInputs.positionWS;
                output.positionOS = input.positionOS.xyz;
                output.normalWS = TransformObjectToWorldNormal(
                    input.normalOS);
                output.uv = input.uv;
                output.eyeDepth = max(
                    -TransformWorldToView(positionInputs.positionWS).z,
                    0);
                return output;
            }

            half4 ShaftFragment(Varyings input) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);

                // uv.x runs from the window to the far end, the same
                // convention CreateBeamCone uses.
                half along = pow(
                    saturate(1.0h - (half)input.uv.x),
                    _AlongPower);

                // Grazing a flank is where the sight line crosses the
                // most air, so that is where the silhouette firms up.
                //
                // But it cannot be allowed to reach zero. Late in the
                // day the beams rake down the nave and you look very
                // nearly ALONG them - the longest path through the
                // volume there is - and a pure inverse-Fresnel darkens
                // exactly that view, leaving two bright rails with
                // nothing between them. _CoreGlow is the floor that
                // keeps the column a column from every angle.
                float3 viewDir = normalize(
                    _WorldSpaceCameraPos - input.positionWS);
                half facing = saturate(
                    1.0h - abs(dot(normalize(input.normalWS), viewDir)));
                half edge = _CoreGlow +
                    ((1.0h - _CoreGlow) * pow(facing, _EdgePower));

                float2 screenUv =
                    GetNormalizedScreenSpaceUV(input.positionCS);
                float rawDepth = SampleSceneDepth(screenUv);
                float sceneEyeDepth =
                    LinearEyeDepth(rawDepth, _ZBufferParams);
                half depthFade = saturate(
                    (sceneEyeDepth - input.eyeDepth) /
                    max(_SoftParticleDistance, 0.01h));

                float cameraDistance = distance(
                    _WorldSpaceCameraPos,
                    input.positionWS);
                half nearFade = saturate(
                    (cameraDistance - _NearFadeDistance) /
                    max(_NearFadeRange, 0.01));

                half motes = 1.0h + _MoteStrength * pow(
                    saturate(MoteField(input.positionOS)),
                    _MoteSharpness);

                half strength =
                    along * edge * depthFade * nearFade * motes *
                    _Intensity;
                return half4(_BeamColor.rgb * strength, 1.0h);
            }
            ENDHLSL
        }
    }

    Fallback Off
}
